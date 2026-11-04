"""Pinned Thingi10K corpus: MeshRepairBenchmark wrap and BooleanComparison ours.
All inputs, source snapshots, diagnostics and resumable results are external to Git.
"""
import concurrent.futures
import csv
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import threading
import time
import urllib.request

ROOT = Path(__file__).resolve().parent
DATA = ROOT.parent / 'dataset'
RESULTS = ROOT / 'results'
COMMIT = 'cd7fb8abba0af09d5725b60c700dfec95234e7ad'
MODES = ('wrap', 'boolean')
WORKERS = 2
LOCK = threading.Lock()
LOCAL = threading.local()
OPS = ('union', 'intersection', 'difference')
WRAP_TOOL = ROOT / 'bin' / 'MeshRepairBenchmark' / 'MeshRepairBenchmark.dll'
BOOL_TOOL = ROOT / 'bin' / 'BooleanComparison' / 'BooleanComparison.dll'
spec = importlib.util.spec_from_file_location('compare', ROOT / 'sources' / 'benchmarks' / 'BooleanComparison' / 'compare.py')
cmp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cmp)
cmp.tool = ['dotnet', str(BOOL_TOOL)]
os.environ.pop('BOOLCMP_SAFE_ROUNDING', None)


def atomic_json(path, value):
    temp = path.with_suffix(path.suffix + '.tmp')
    temp.write_text(json.dumps(value, indent=2), encoding='utf-8')
    for attempt in range(8):
        try:
            temp.replace(path)
            return
        except OSError as error:
            if getattr(error, 'winerror', None) not in (5, 32, 33) or attempt == 7:
                raise
            time.sleep(min(0.1 * 2 ** attempt, 1.0))


def download(url, path, size, digest=None):
    if path.exists() and path.stat().st_size == size:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    part = path.with_suffix(path.suffix + '.part')
    for attempt in range(4):
        try:
            h = hashlib.sha256()
            with urllib.request.urlopen(url, timeout=180) as response, part.open('wb') as out:
                while block := response.read(1024 * 1024):
                    out.write(block)
                    h.update(block)
            if part.stat().st_size != size or (digest and h.hexdigest() != digest):
                raise IOError('Downloaded size/SHA256 does not match pinned inventory')
            part.replace(path)
            return
        except Exception:
            part.unlink(missing_ok=True)
            if attempt == 3:
                raise
            time.sleep(2 ** attempt)


def text(value):
    return value.decode('utf-8', errors='replace') if isinstance(value, bytes) else value or ''


class LoggedSubprocess:
    TimeoutExpired = subprocess.TimeoutExpired

    @staticmethod
    def run(command, **kwargs):
        directory = getattr(LOCAL, 'logs', RESULTS / 'diagnostics')
        directory.mkdir(parents=True, exist_ok=True)
        stage = getattr(LOCAL, 'stage', 'unknown')
        log = directory / stage
        start = time.monotonic()
        try:
            result = subprocess.run(command, **kwargs)
            LOCAL.last_process = result
            log.with_suffix('.stdout.log').write_text(text(result.stdout), encoding='utf-8')
            log.with_suffix('.stderr.log').write_text(text(result.stderr), encoding='utf-8')
            return result
        except subprocess.TimeoutExpired as error:
            log.with_suffix('.stdout.log').write_text(text(error.stdout), encoding='utf-8')
            log.with_suffix('.stderr.log').write_text(text(error.stderr), encoding='utf-8')
            raise
        finally:
            atomic_json(log.with_suffix('.command.json'), {
                'command': command, 'timeout_seconds': kwargs.get('timeout'),
                'wall_ms': round((time.monotonic() - start) * 1000)})


cmp.subprocess = LoggedSubprocess


def error_text(error):
    return type(error).__name__ + ': ' + str(error)


def normalize_wrap(raw):
    status = raw.get('status')
    reason = raw.get('error') or raw.get('wrap_error') or raw.get('not_clean_reason') or ''
    if status in ('exception', 'timeout', 'crash', 'download_failed'):
        return status, reason
    if raw.get('wrap_status') != 'Success':
        return 'failed', reason or 'MeshAlphaWrap status: ' + str(raw.get('wrap_status'))
    if reason:
        return 'unclean', reason
    if raw.get('after_finite') is False or raw.get('after_valid') is False:
        return 'unclean', 'Result is non-finite or failed CheckValidity'
    if raw.get('after_finite') is not True or raw.get('after_valid') is not True or 'not_clean_reason' not in raw:
        return 'validation_missing', 'Required result validation is missing'
    if raw.get('not_clean_self_intersections_checked') is not True:
        return 'quality_unchecked', 'Self-intersection check skipped above 300000 result triangles'
    return 'ok', ''


def normalize_boolean(raw, op):
    status = raw.get(op + '_status', 'not_run')
    if status != 'ok':
        return status, raw.get(op + '_error') or raw.get('error') or 'Operation was not run'
    nr = raw.get(op + '_not_clean_reason')
    if nr and not (nr == 'empty' and raw.get(op + '_tris') == 0):
        return 'unclean', nr
    if raw.get(op + '_not_clean_exception'):
        return 'evaluation_failed', raw[op + '_not_clean_exception']
    if raw.get('evaluation_error'):
        return 'evaluation_failed', raw['evaluation_error']
    for suffix in ('finite', 'wclosed', 'manifold_closed'):
        value = raw.get(op + '_' + suffix)
        if value is False:
            return 'unclean', 'BooleanComparison eval: ' + suffix + '=false'
        if value is not True:
            return 'validation_missing', 'BooleanComparison eval: missing ' + suffix
    for suffix in ('degenerate', 'zero_area'):
        if raw.get(op + '_' + suffix) != 0:
            return 'unclean', 'BooleanComparison eval: ' + suffix + '=' + str(raw.get(op + '_' + suffix))
    if nr is None:
        return 'validation_missing', 'NotCleanReason is missing'
    if raw.get(op + '_not_clean_self_intersections_checked') is not True or raw.get(op + '_selfint', -1) < 0:
        return 'quality_unchecked', 'Self-intersection check skipped above 400000 result triangles'
    if raw[op + '_selfint'] > 0:
        return 'unclean', 'self-intersections'
    return 'ok', ''


def run_wrap(mesh, timeout):
    start = time.monotonic()
    env = os.environ.copy()
    for key in tuple(env):
        if key.startswith('BENCH_'):
            del env[key]
    env['BENCH_REPAIR'] = 'wrap'
    env['BENCH_WRAP_ALPHA'] = '100'
    env['BENCH_WRAP_OFFSET'] = '1000'
    LOCAL.stage = 'wrap'
    raw = {'file': mesh.name, 'status': 'crash'}
    try:
        result = LoggedSubprocess.run(['dotnet', str(WRAP_TOOL), str(mesh), 'std', 'none', 'rel'],
                                     env=env, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=timeout)
        parsed = False
        for line in reversed(result.stdout.splitlines()):
            try:
                row = json.loads(line)
            except ValueError:
                continue
            if isinstance(row, dict) and row.get('file') == mesh.name:
                raw.update(row)
                parsed = True
                break
        if result.returncode != 0 or not parsed:
            raw.update(status='crash', error=f'Exit {result.returncode}: ' + result.stderr.strip())
        raw['exit_code'] = result.returncode
    except subprocess.TimeoutExpired as error:
        raw.update(status='timeout', error=error_text(error))
    except Exception as error:
        raw.update(status='exception', error=error_text(error))
    raw.update(wall_ms=round((time.monotonic() - start) * 1000), timeout_seconds=timeout)
    return raw


def run_boolean(mesh):
    start = time.monotonic()
    raw = {'class': 'thingi10k', 'file': mesh.name, 'method': 'ours'}
    runs = {}
    directory = tempfile.mkdtemp(prefix='boolcmp_', dir=ROOT / 'scratch')
    try:
        LOCAL.stage = 'export'
        export_start = time.monotonic()
        try:
            result = LoggedSubprocess.run(cmp.tool + ['export', str(mesh), directory],
                                          capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=1800)
            if result.returncode != 0:
                raise RuntimeError(f'Export exit {result.returncode}: ' + result.stderr.strip())
            info = json.loads(result.stdout.strip().splitlines()[-1])
            if info.get('error'):
                raise RuntimeError(info['error'])
        finally:
            raw['export_wall_ms'] = round((time.monotonic() - export_start) * 1000)
        raw['input'] = info
        for op in OPS:
            LOCAL.stage = op
            op_start = time.monotonic()
            try:
                run = cmp.run_one('ours', op, directory, 300)
                if run.get('status') == 'timeout':
                    run['error'] = 'TimeoutExpired: Boolean operation exceeded 300 seconds'
                elif LOCAL.last_process.returncode != 0:
                    run.update(status='crash', ok=False, error=f'Exit {LOCAL.last_process.returncode}: ' + LOCAL.last_process.stderr.strip())
            except Exception as error:
                run = {'status': 'exception', 'ok': False, 'error': error_text(error)}
            run.setdefault('wall_ms', round((time.monotonic() - op_start) * 1000))
            runs[op] = run
            raw.update({op + '_' + k: v for k, v in run.items()})
        LOCAL.stage = 'eval'
        eval_start = time.monotonic()
        try:
            evaluated = cmp.evaluate(directory, ['ours'], {'class': 'thingi10k', 'name': mesh.name}, info, {'ours': runs}, list(OPS))
            result = LOCAL.last_process
            if result.returncode != 0:
                raise RuntimeError(f'Eval exit {result.returncode}: ' + result.stderr.strip())
            raw.update(evaluated[0])
        except Exception as error:
            raw['evaluation_error'] = error_text(error)
        finally:
            raw['eval_wall_ms'] = round((time.monotonic() - eval_start) * 1000)
    except Exception as error:
        raw['error'] = 'Export: ' + error_text(error)
        for op in OPS:
            if op not in runs:
                raw.update({op + '_status': 'not_run_export_failed', op + '_error': raw['error']})
    finally:
        shutil.rmtree(directory, ignore_errors=True)
    raw['wall_ms'] = round((time.monotonic() - start) * 1000)
    return raw


FIELDS = ('thingi_id', 'file', 'project', 'mode', 'operation', 'commit', 'status', 'time_ms',
          'wall_ms', 'reason', 'algorithm_status', 'not_clean_reason', 'self_intersections_checked')


def operation_rows(mode, raw):
    base = {'thingi_id': Path(raw['file']).stem, 'file': raw['file'], 'mode': mode, 'commit': COMMIT,
            'project': 'MeshRepairBenchmark' if mode == 'wrap' else 'BooleanComparison'}
    if mode == 'wrap':
        status, reason = normalize_wrap(raw)
        return [dict(base, operation='wrap', status=status, reason=reason, time_ms=raw.get('wrap_ms'),
                     wall_ms=raw.get('wall_ms'), algorithm_status=raw.get('wrap_status'),
                     not_clean_reason=raw.get('not_clean_reason'),
                     self_intersections_checked=raw.get('not_clean_self_intersections_checked'))]
    rows = []
    for op in OPS:
        status, reason = normalize_boolean(raw, op)
        if raw.get('status') == 'download_failed':
            status, reason = 'download_failed', raw['error']
        rows.append(dict(base, operation=op, status=status, reason=reason, time_ms=raw.get(op + '_ms'),
                         wall_ms=raw.get(op + '_wall_ms'), algorithm_status=raw.get(op + '_status'),
                         not_clean_reason=raw.get(op + '_not_clean_reason'),
                         self_intersections_checked=raw.get(op + '_not_clean_self_intersections_checked')))
    return rows


def model_row(mode, raw):
    rows = operation_rows(mode, raw)
    row = dict(rows[0], operation='all' if mode == 'boolean' else 'wrap', wall_ms=raw.get('wall_ms'))
    if len(rows) > 1:
        statuses = {r['status'] for r in rows}
        row['status'] = rows[0]['status'] if len(statuses) == 1 else 'mixed_or_failed'
        row['reason'] = '; '.join(r['operation'] + ': ' + r['reason'] for r in rows if r['status'] != 'ok')
        row['algorithm_status'] = '; '.join(r['operation'] + ': ' + str(r['algorithm_status']) for r in rows)
        row['time_ms'] = sum(r['time_ms'] for r in rows) if all(r['time_ms'] is not None for r in rows) else None
        row['not_clean_reason'] = '; '.join(r['operation'] + ': ' + r['not_clean_reason'] for r in rows if r['not_clean_reason'])
        row['self_intersections_checked'] = all(r['self_intersections_checked'] is True for r in rows)
    return row


def load_results(path):
    rows = {}
    if path.exists():
        lines = path.read_text(encoding='utf-8').splitlines()
        for i, line in enumerate(lines):
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                if i != len(lines) - 1:
                    raise RuntimeError('Invalid non-final JSONL row in ' + str(path))
                continue
            if row.get('status') != 'download_failed':
                rows[row['file']] = row
    temp = path.with_suffix('.jsonl.tmp')
    temp.write_text(''.join(json.dumps(r) + '\n' for r in rows.values()), encoding='utf-8')
    temp.replace(path)
    return rows


def main():
    inventory = json.loads((DATA / 'inventory.json').read_text(encoding='utf-8'))
    config = json.loads((ROOT / 'config.json').read_text(encoding='utf-8'))
    if config['commit'] != COMMIT or config['dataset_revision'] != inventory['dataset_revision']:
        raise RuntimeError('Measurement provenance mismatch')
    for relative, digest in config['binary_sha256'].items():
        if hashlib.sha256((ROOT / relative).read_bytes()).hexdigest() != digest:
            raise RuntimeError('Frozen binary SHA256 changed: ' + relative)
    files = inventory['files']
    expected = {Path(e['path']).name for e in files}
    if len(expected) != len(files):
        raise RuntimeError('Duplicate raw filenames')
    with (DATA / 'geometry_data.csv').open(encoding='utf-8', newline='') as f:
        geometry = {r['file_id']: r for r in csv.DictReader(f)}
    RESULTS.mkdir(exist_ok=True)
    (ROOT / 'scratch').mkdir(exist_ok=True)
    needed = sum(e['size'] for e in files if not (DATA / e['path']).exists())
    if shutil.disk_usage(DATA).free < needed + 8 * 1024 ** 3:
        raise RuntimeError('Full corpus plus 8 GiB temporary-space reserve does not fit')
    rows = {mode: load_results(RESULTS / (mode + '.jsonl')) for mode in MODES}
    active = {}
    for filename, converter in (('per_model.csv', model_row), ('per_operation.csv', operation_rows)):
        temp = RESULTS / (filename + '.tmp')
        with temp.open('w', encoding='utf-8', newline='') as f:
            writer = csv.DictWriter(f, fieldnames=FIELDS)
            writer.writeheader()
            for mode in MODES:
                for raw in rows[mode].values():
                    converted = converter(mode, raw)
                    writer.writerows(converted if isinstance(converted, list) else [converted])
        temp.replace(RESULTS / filename)
    def progress(state):
        atomic_json(ROOT / 'progress.json', {'state': state, 'pid': os.getpid(),
            'updated_at': time.strftime('%Y-%m-%dT%H:%M:%S%z'), 'expected_meshes': len(files),
            'completed': {m: len(expected & rows[m].keys()) for m in MODES},
            'download_failures': {m: sum(r.get('status') == 'download_failed' for r in rows[m].values()) for m in MODES},
            'active_models': active})
    def record(mode, raw):
        raw.update(commit=COMMIT, thingi_id=Path(raw['file']).stem, benchmark_mode=mode)
        with LOCK:
            with (RESULTS / (mode + '.jsonl')).open('a', encoding='utf-8') as f:
                f.write(json.dumps(raw) + '\n')
                f.flush()
                os.fsync(f.fileno())
            rows[mode][raw['file']] = raw
            for filename, converted in (('per_model.csv', [model_row(mode, raw)]),
                                        ('per_operation.csv', operation_rows(mode, raw))):
                with (RESULTS / filename).open('a', encoding='utf-8', newline='') as f:
                    csv.DictWriter(f, fieldnames=FIELDS).writerows(converted)
                    f.flush()
                    os.fsync(f.fileno())
            progress('running')
            print(f"{mode} {raw['file']}: {model_row(mode, raw)['status']} ({len(rows[mode])}/{len(files)})", flush=True)
    base = f"https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/{inventory['dataset_revision']}"
    def work(entry):
        mesh = DATA / entry['path']
        pending = [m for m in MODES if mesh.name not in rows[m]]
        if not pending:
            return
        with LOCK:
            active[mesh.name] = 'download'
            progress('running')
        LOCAL.logs = RESULTS / 'diagnostics' / mesh.name
        try:
            download(f"{base}/{entry['path']}", mesh, entry['size'], entry.get('lfs', {}).get('oid'))
        except Exception as error:
            for mode in pending:
                record(mode, {'file': mesh.name, 'status': 'download_failed', 'error': error_text(error)})
        else:
            faces = geometry.get(mesh.stem, {}).get('num_faces')
            try:
                timeout = 900 if not faces or int(faces) > 100000 else 120
            except (ValueError, OverflowError):
                timeout = 900
            for mode in pending:
                with LOCK:
                    active[mesh.name] = mode
                    progress('running')
                record(mode, run_wrap(mesh, timeout) if mode == 'wrap' else run_boolean(mesh))
        with LOCK:
            del active[mesh.name]
            progress('running')
    with LOCK:
        progress('running')
    try:
        with concurrent.futures.ThreadPoolExecutor(WORKERS) as pool:
            for _ in pool.map(work, files):
                pass
        with LOCK:
            failures = any(r.get('status') == 'download_failed' for rs in rows.values() for r in rs.values())
            complete = all(expected <= rs.keys() for rs in rows.values())
            progress('completed_with_download_failures' if failures else 'completed' if complete else 'incomplete')
    except BaseException:
        with LOCK:
            progress('runner_failed')
        raise


if __name__ == '__main__':
    main()
