"""Durable, bounded Boolean evaluation queue; independent Wrap and Boolean producers."""
import concurrent.futures
import csv
import hashlib
import json
import os
from pathlib import Path
import shutil
import threading
import time
import run_full as legacy

ROOT = legacy.ROOT
RESULTS = legacy.RESULTS
QUEUE = ROOT / 'pending-evaluation'
PROBE = ROOT / 'bin' / 'EvaluationProbe' / 'BooleanComparison.dll'
STOP = ROOT / 'STOP'
MAX_JOBS = 16
MAX_QUEUE_BYTES = 4 * 1024 ** 3
RESERVE_BYTES = 8 * 1024 ** 3
EVALUATORS = 2


class RunLease:
    def __init__(self, path):
        self.path = path

    def __enter__(self):
        self.file = self.path.open('a+b')
        self.file.seek(0, os.SEEK_END)
        if self.file.tell() == 0:
            self.file.write(b'\0')
            self.file.flush()
        self.file.seek(0)
        try:
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(self.file.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.file.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            self.file.close()
            raise RuntimeError('Another pipeline runner holds the run lease')
        return self

    def __exit__(self, *args):
        self.file.close()


def safe_name(name):
    return isinstance(name, str) and bool(name) and name not in ('.', '..') and not any(c in name for c in '/\\\\:\0')


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        while block := f.read(1024 * 1024):
            h.update(block)
    return h.hexdigest()


def durable_json(path, value):
    temp = path.with_suffix(path.suffix + '.tmp')
    with temp.open('w', encoding='utf-8') as f:
        json.dump(value, f, indent=2)
        f.flush()
        os.fsync(f.fileno())
    for attempt in range(8):
        try:
            temp.replace(path)
            return
        except OSError as error:
            if getattr(error, 'winerror', None) not in (5, 32, 33) or attempt == 7:
                raise
            time.sleep(min(0.1 * 2 ** attempt, 1.0))


def append(path, raw):
    with path.open('a', encoding='utf-8') as f:
        f.write(json.dumps(raw) + '\n')
        f.flush()
        os.fsync(f.fileno())


def read_results(path):
    rows = {}
    if not path.exists():
        return rows
    data = path.read_bytes()
    offset = 0
    for line in data.splitlines(keepends=True):
        try:
            row = json.loads(line)
        except ValueError:
            if offset + len(line) != len(data) or line.endswith(b'\n'):
                raise RuntimeError('Corrupt JSONL record: ' + str(path))
            with path.open('r+b') as f:
                f.truncate(offset)
                f.flush()
                os.fsync(f.fileno())
            break
        if row.get('commit') != legacy.COMMIT:
            raise RuntimeError('Foreign measured commit: ' + str(path))
        if row['file'] in rows and rows[row['file']].get('status') != 'download_failed':
            raise RuntimeError('Duplicate final result: ' + row['file'])
        rows[row['file']] = row
        offset += len(line)
    # A valid final record without a newline must not be concatenated with the next append.
    if data and offset == len(data) and not data.endswith(b'\n'):
        with path.open('ab') as f:
            f.write(b'\n')
            f.flush()
            os.fsync(f.fileno())
    return rows


class JobStore:
    def __init__(self, root, binary_hashes):
        self.root = root
        self.root.mkdir(parents=True, exist_ok=True)
        self.binary_hashes = binary_hashes
        self.condition = threading.Condition()
        self.claimed = set()

    def recover_incomplete(self, quarantine):
        for directory in self.root.iterdir():
            if directory.is_dir() and not (directory / 'job.json').exists():
                quarantine.mkdir(parents=True, exist_ok=True)
                directory.rename(quarantine / (directory.name + '-' + str(time.time_ns())))

    def paths(self):
        return sorted(self.root.glob('*/job.json'))

    def stats(self):
        with self.condition:
            paths = self.paths()
            return len(paths), sum(json.loads(p.read_text(encoding='utf-8'))['bytes'] for p in paths)

    def publish(self, directory, raw, runs):
        filename = raw['file']
        if not safe_name(filename):
            raise ValueError('Unsafe job filename')
        artifacts = {}
        for p in sorted(directory.iterdir()):
            if p.is_file() and p.name != 'job.json':
                with p.open('r+b') as f:
                    os.fsync(f.fileno())
                artifacts[p.name] = {'bytes': p.stat().st_size, 'sha256': digest(p)}
        job = {'version': 1, 'commit': legacy.COMMIT, 'binary_sha256': self.binary_hashes,
               'file': filename, 'raw': raw, 'runs': runs, 'artifacts': artifacts,
               'bytes': sum(a['bytes'] for a in artifacts.values())}
        target = self.root / filename
        with self.condition:
            if target.exists():
                raise RuntimeError('Job already exists: ' + filename)
            directory.rename(target)
            # Only this durable manifest makes a directory eligible for evaluation/recovery.
            durable_json(target / 'job.json', job)
            self.condition.notify_all()
        return target / 'job.json'

    def read(self, path):
        job = json.loads(path.read_text(encoding='utf-8'))
        if (job['version'] != 1 or job['commit'] != legacy.COMMIT or
                job['binary_sha256'] != self.binary_hashes or path.parent.name != job['file'] or
                job['raw']['file'] != job['file'] or job['raw']['commit'] != legacy.COMMIT or
                not safe_name(job['file']) or job['bytes'] != sum(a['bytes'] for a in job['artifacts'].values())):
            raise RuntimeError('Job provenance mismatch: ' + str(path))
        for name, evidence in job['artifacts'].items():
            p = path.parent / name
            if not safe_name(name) or not p.is_file() or p.stat().st_size != evidence['bytes'] or digest(p) != evidence['sha256']:
                raise RuntimeError('Job artifact mismatch: ' + str(p))
        return job

    def claim(self):
        with self.condition:
            for path in self.paths():
                if path not in self.claimed:
                    self.claimed.add(path)
                    return path
        return None

    def release(self, path):
        with self.condition:
            self.claimed.discard(path)
            self.condition.notify_all()

    def remove(self, path):
        with self.condition:
            shutil.rmtree(path.parent)
            self.claimed.discard(path)
            self.condition.notify_all()

    def wait_capacity(self, stopped, missing_bytes):
        with self.condition:
            while True:
                if stopped():
                    return False
                count, size = self.stats()
                free = shutil.disk_usage(self.root).free
                # Byte high-water mark plus at most one in-flight computation. Wrap has its own producer.
                required = missing_bytes() + RESERVE_BYTES
                if count == 0 and free < required:
                    raise RuntimeError('Remaining corpus plus temporary-space reserve does not fit')
                if count < MAX_JOBS and size < MAX_QUEUE_BYTES and free >= required:
                    return True
                self.condition.wait(timeout=1)


class ProbeSubprocess(legacy.LoggedSubprocess):
    @staticmethod
    def run(command, **kwargs):
        if len(command) > 2 and command[2] == 'eval':
            command = [command[0], str(PROBE), *command[2:]]
            env = os.environ.copy()
            env['THINGI_EVAL_PHASE_LOG'] = str(legacy.LOCAL.logs / 'eval.phases.jsonl')
            kwargs['env'] = env
        return legacy.LoggedSubprocess.run(command, **kwargs)


def compute_boolean(mesh):
    start = time.monotonic()
    raw = {'class': 'thingi10k', 'file': mesh.name, 'method': 'ours', 'input': None}
    runs = {}
    directory = ROOT / 'scratch' / ('pipeline_' + mesh.name)
    if directory.exists():
        directory.rename(directory.with_name(directory.name + '.incomplete-' + str(time.time_ns())))
    directory.mkdir()
    try:
        legacy.LOCAL.stage = 'export'
        export_start = time.monotonic()
        try:
            result = legacy.LoggedSubprocess.run(legacy.cmp.tool + ['export', str(mesh), str(directory)],
                                                capture_output=True, text=True, timeout=1800)
            info = json.loads(result.stdout.strip().splitlines()[-1])
            if result.returncode != 0 or 'error' in info:
                raise RuntimeError(info.get('error') or result.stderr.strip())
            raw['input'] = info
        finally:
            raw['export_wall_ms'] = round((time.monotonic() - export_start) * 1000)
        for op in legacy.OPS:
            legacy.LOCAL.stage = op
            op_start = time.monotonic()
            try:
                run = legacy.cmp.run_one('ours', op, str(directory), 300)
                if run.get('status') == 'timeout':
                    run['error'] = 'TimeoutExpired: Boolean operation exceeded 300 seconds'
                elif legacy.LOCAL.last_process.returncode != 0:
                    run.update(status='crash', ok=False, error=f'Exit {legacy.LOCAL.last_process.returncode}: ' + legacy.LOCAL.last_process.stderr.strip())
            except Exception as error:
                run = {'status': 'exception', 'ok': False, 'error': legacy.error_text(error)}
            run.setdefault('wall_ms', round((time.monotonic() - op_start) * 1000))
            runs[op] = run
            raw.update({op + '_' + k: v for k, v in run.items()})
    except Exception as error:
        raw['error'] = 'Export: ' + legacy.error_text(error)
        for op in legacy.OPS:
            if op not in runs:
                raw.update({op + '_status': 'not_run_export_failed', op + '_error': raw['error']})
    raw['compute_wall_ms'] = round((time.monotonic() - start) * 1000)
    raw.update(commit=legacy.COMMIT, thingi_id=mesh.stem, benchmark_mode='boolean', evaluation_state='pending')
    return directory, raw, runs


def evaluate_job(store, path):
    job = store.read(path)
    raw = dict(job['raw'])
    legacy.LOCAL.logs = RESULTS / 'diagnostics' / raw['file']
    legacy.LOCAL.stage = 'eval'
    start = time.monotonic()
    try:
        evaluated = legacy.cmp.evaluate(str(path.parent), ['ours'], {'class': 'thingi10k', 'name': raw['file']},
                                        raw['input'], {'ours': job['runs']}, list(legacy.OPS))
        result = legacy.LOCAL.last_process
        if result.returncode != 0:
            raise RuntimeError(f'Eval exit {result.returncode}: ' + result.stderr.strip())
        raw.update(evaluated[0])
        raw['evaluation_state'] = 'completed'
    except Exception as error:
        raw['evaluation_error'] = legacy.error_text(error)
        raw['evaluation_state'] = 'failed'
    raw['eval_wall_ms'] = round((time.monotonic() - start) * 1000)
    compute = raw.get('compute_wall_ms')
    raw['wall_ms'] = compute + raw['eval_wall_ms'] if compute is not None else None
    raw['wall_time_semantics'] = 'compute_plus_eval_excluding_queue_wait'
    return raw


def validate_config(config):
    for relative, expected in config['binary_sha256'].items():
        if digest(ROOT / relative) != expected:
            raise RuntimeError('Frozen binary SHA256 changed: ' + relative)
    if digest(ROOT / 'run_full.py') != config['runner_sha256']:
        raise RuntimeError('Frozen legacy runner SHA256 changed')
    for relative, expected in config['benchmark_source_sha256'].items():
        if digest(ROOT / 'sources' / relative) != expected:
            raise RuntimeError('Frozen benchmark source SHA256 changed: ' + relative)
    pipeline = config['pipeline']
    for name, expected in pipeline['probe_files_sha256'].items():
        if not safe_name(name) or digest(PROBE.parent / name) != expected:
            raise RuntimeError('Probe asset SHA256 changed: ' + name)
    if pipeline['version'] != 1 or digest(PROBE) != pipeline['probe_sha256'] or digest(Path(__file__)) != pipeline['runner_sha256']:
        raise RuntimeError('Pipeline provenance mismatch')
    if digest(PROBE.parent / 'geometry3Sharp.dll') != config['library_unchanged_sha256']:
        raise RuntimeError('Probe references a different library')


def main():
    config = json.loads((ROOT / 'config.json').read_text(encoding='utf-8'))
    inventory = json.loads((legacy.DATA / 'inventory.json').read_text(encoding='utf-8'))
    if config['commit'] != legacy.COMMIT or config['dataset_revision'] != inventory['dataset_revision']:
        raise RuntimeError('Measurement provenance mismatch')
    validate_config(config)
    legacy.cmp.subprocess = ProbeSubprocess
    files = inventory['files']
    expected = {Path(e['path']).name for e in files}
    if len(expected) != len(files):
        raise RuntimeError('Duplicate inventory filenames')
    geometry = {}
    with (legacy.DATA / 'geometry_data.csv').open(encoding='utf-8', newline='') as f:
        geometry = {r['file_id']: r for r in csv.DictReader(f)}
    RESULTS.mkdir(exist_ok=True)
    (ROOT / 'scratch').mkdir(exist_ok=True)
    rows = {m: read_results(RESULTS / (m + '.jsonl')) for m in legacy.MODES}
    computed = read_results(RESULTS / 'computed.jsonl')
    store = JobStore(QUEUE, config['binary_sha256'])
    store.recover_incomplete(ROOT / 'scratch' / 'incomplete-publications')
    for path in store.paths():
        if path.parent.name in rows['boolean'] and rows['boolean'][path.parent.name].get('status') != 'download_failed':
            store.remove(path)
        else:
            job = store.read(path)
            if job['file'] not in computed:
                append(RESULTS / 'computed.jsonl', job['raw'])
                computed[job['file']] = job['raw']
    pending_names = {p.parent.name for p in store.paths()}
    if computed.keys() - rows['boolean'].keys() - pending_names:
        raise RuntimeError('Computed result has neither final evaluation nor durable artifacts')
    for filename, converter in (('per_model.csv', legacy.model_row), ('per_operation.csv', legacy.operation_rows)):
        with (RESULTS / filename).open('w', encoding='utf-8', newline='') as f:
            writer = csv.DictWriter(f, fieldnames=legacy.FIELDS)
            writer.writeheader()
            for mode, records in rows.items():
                for raw in records.values():
                    converted = converter(mode, raw)
                    writer.writerows(converted if isinstance(converted, list) else [converted])
    lock = threading.RLock()
    download_lock = threading.Lock()
    active = {}
    boolean_done = threading.Event()
    failed = threading.Event()
    def stopped():
        return STOP.exists() or failed.is_set()
    def progress(state='running'):
        count, size = store.stats()
        durable_json(ROOT / 'progress.json', {'state': state, 'pid': os.getpid(),
            'updated_at': time.strftime('%Y-%m-%dT%H:%M:%S%z'), 'expected_meshes': len(files),
            'completed': {m: len(expected & rows[m].keys()) for m in legacy.MODES},
            'boolean_computed': len(expected & (computed.keys() | rows['boolean'].keys())),
            'pending_evaluations': count, 'pending_bytes': size,
            'download_failures': {m: sum(r.get('status') == 'download_failed' for r in rows[m].values()) for m in legacy.MODES},
            'active_models': dict(active), 'pipeline_version': 1})
    def stage(key, value):
        with lock:
            if value is None:
                active.pop(key, None)
            else:
                active[key] = value
            progress()
    def record(mode, raw):
        raw.update(commit=legacy.COMMIT, thingi_id=Path(raw['file']).stem, benchmark_mode=mode,
                   pipeline_version=1, probe_sha256=config['pipeline']['probe_sha256'])
        with lock:
            append(RESULTS / (mode + '.jsonl'), raw)
            rows[mode][raw['file']] = raw
            for filename, converted in (('per_model.csv', [legacy.model_row(mode, raw)]), ('per_operation.csv', legacy.operation_rows(mode, raw))):
                with (RESULTS / filename).open('a', encoding='utf-8', newline='') as f:
                    csv.DictWriter(f, fieldnames=legacy.FIELDS).writerows(converted)
                    f.flush()
                    os.fsync(f.fileno())
            progress()
            print(f"{mode} {raw['file']}: {legacy.model_row(mode, raw)['status']}", flush=True)
    base = f"https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/{inventory['dataset_revision']}"
    def missing_bytes():
        return sum(e['size'] for e in files if not (legacy.DATA / e['path']).exists())
    def produce(mode):
        try:
            for entry in files:
                if stopped():
                    break
                mesh = legacy.DATA / entry['path']
                with lock:
                    finished = rows[mode].get(mesh.name, {}).get('status') != 'download_failed' and mesh.name in rows[mode]
                if finished or (mode == 'boolean' and (QUEUE / mesh.name / 'job.json').exists()):
                    continue
                if mode == 'boolean':
                    stage(mode, 'waiting_for_evaluation_capacity')
                    if not store.wait_capacity(stopped, missing_bytes):
                        break
                stage(mode, 'download:' + mesh.name)
                legacy.LOCAL.logs = RESULTS / 'diagnostics' / mesh.name
                try:
                    with download_lock:
                        legacy.download(f"{base}/{entry['path']}", mesh, entry['size'], entry.get('lfs', {}).get('oid'))
                except Exception as error:
                    record(mode, {'file': mesh.name, 'status': 'download_failed', 'error': legacy.error_text(error)})
                    continue
                stage(mode, 'compute:' + mesh.name)
                if mode == 'wrap':
                    faces = geometry.get(mesh.stem, {}).get('num_faces')
                    try:
                        timeout = 900 if not faces or int(faces) > 100000 else 120
                    except (ValueError, OverflowError):
                        timeout = 900
                    record(mode, legacy.run_wrap(mesh, timeout))
                else:
                    directory, raw, runs = compute_boolean(mesh)
                    if raw.get('input') is None:
                        raw.update(wall_ms=raw['compute_wall_ms'], evaluation_state='not_run')
                        record(mode, raw)
                        shutil.rmtree(directory)
                    else:
                        store.publish(directory, raw, runs)
                        with lock:
                            append(RESULTS / 'computed.jsonl', raw)
                            computed[mesh.name] = raw
                            progress()
        except BaseException:
            failed.set()
            raise
        finally:
            if mode == 'boolean':
                boolean_done.set()
            stage(mode, None)
    def evaluate(worker):
        key = 'eval:' + str(worker)
        try:
            while not stopped():
                path = store.claim()
                if path is None:
                    if boolean_done.is_set():
                        break
                    time.sleep(0.25)
                    continue
                try:
                    stage(key, path.parent.name)
                    record('boolean', evaluate_job(store, path))
                    # A final JSONL fsync must happen before deleting any queued artifact.
                    store.remove(path)
                finally:
                    store.release(path)
                    stage(key, None)
        except BaseException:
            failed.set()
            raise
    with lock:
        progress()
    try:
        with concurrent.futures.ThreadPoolExecutor(2 + EVALUATORS) as pool:
            futures = [pool.submit(produce, m) for m in legacy.MODES]
            futures += [pool.submit(evaluate, i) for i in range(EVALUATORS)]
            for future in concurrent.futures.as_completed(futures):
                future.result()
        with lock:
            complete = all(expected <= r.keys() for r in rows.values()) and not store.paths()
            downloads_failed = any(r.get('status') == 'download_failed' for rs in rows.values() for r in rs.values())
            progress('stopped' if stopped() else 'completed_with_download_failures' if downloads_failed else 'completed' if complete else 'incomplete')
    except BaseException:
        with lock:
            progress('interrupted')
        raise


if __name__ == '__main__':
    with RunLease(ROOT / 'pipeline.lock'):
        main()
