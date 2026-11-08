"""Re-judge point-pilot timeouts on identical meshes with point and hull-edge indices."""
import argparse
import concurrent.futures
import hashlib
import json
import os
import pathlib
import re
import subprocess
import time


def digest(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def runtime_files(benchmark):
    directory = benchmark.parent
    files = set(directory.glob('*.dll')) | set(directory.glob('*.deps.json')) | set(directory.glob('*.runtimeconfig.json'))
    runtime = directory/'runtimes'
    if runtime.exists(): files.update(p for p in runtime.rglob('*') if p.is_file())
    return {str(p.relative_to(directory)):digest(p) for p in sorted(files)}


def baseline_rows(path):
    rows = {}
    for line in path.read_text().splitlines(keepends=True):
        if not line.endswith('\n'):  # the producer may currently be appending
            continue
        row = json.loads(line)
        key = (row['case'], row['mode'])
        if key in rows:
            raise ValueError('Duplicate baseline record')
        rows[key] = (row, hashlib.sha256(line.encode()).hexdigest())
    return rows


def complete(path):
    try:
        summary = json.loads(path.read_text())
        return summary['planned'] > 0 and summary['completed'] == summary['planned']
    except (FileNotFoundError, json.JSONDecodeError):
        return False


def stage(command, timeout, environment):
    start = time.monotonic()
    try:
        run = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=timeout)
        return dict(command=command, exit_code=run.returncode, stdout=run.stdout,
                    stderr=run.stderr, seconds=time.monotonic()-start)
    except subprocess.TimeoutExpired as e:
        def text(value):
            return value.decode(errors='replace') if isinstance(value, bytes) else value or ''
        return dict(command=command, timeout=timeout, stdout=text(e.stdout), stderr=text(e.stderr),
                    seconds=time.monotonic()-start)


def classify(result, row, expected_hash):
    if 'timeout' in result:
        return 'ORACLE_TIMEOUT'
    if row is None or row.get('input_sha256') != expected_hash:
        return 'ORACLE_ERROR'
    if result['exit_code'] == 0 and re.search(r'^VALID ', result['stdout'], re.M):
        return 'VALID'
    if result['exit_code'] == 1 and re.search(r'^INVALID ', result['stdout'], re.M):
        return 'INVALID'
    return 'ORACLE_ERROR'


def judge(base, source_hash, args, environment):
    key = hashlib.sha256((base['case']+'|'+base['mode']).encode()).hexdigest()[:20]
    mesh = args.work/'meshes'/(key+'.mesh.txt')
    row_path = args.work/(key+'.row.json')
    source = pathlib.Path(base['mesh']['command'][base['mesh']['command'].index('--input')+1])
    if digest(source) != base['input_sha256']:
        raise ValueError('Baseline input hash differs')
    record = dict(case=base['case'], mode=base['mode'], baseline_record_sha256=source_hash,
                  baseline_verdict=base['verdict'], input_sha256=base['input_sha256'],
                  expected_output_sha256=base['output_sha256'])
    record['cached_identical_mesh'] = mesh.exists() and digest(mesh) == base['output_sha256']
    if not record['cached_identical_mesh']:
        mesh.unlink(missing_ok=True)
        command = list(base['mesh']['command'])
        command[0] = str(args.dotnet)
        command[1] = str(args.mesh_benchmark)
        command[command.index('--output')+1] = str(mesh)
        record['mesh'] = stage(command, args.mesh_timeout, environment)
        if 'timeout' in record['mesh']:
            return dict(record, verdict='MESH_TIMEOUT')
        if record['mesh']['exit_code'] != 0 or not mesh.exists():
            return dict(record, verdict='MESH_ERROR')
    record['output_sha256'] = digest(mesh)
    if record['output_sha256'] != base['output_sha256']:
        return dict(record, verdict='MESH_MISMATCH')
    row_path.unlink(missing_ok=True)
    command = list(base['oracle']['command'])
    command[0] = str(args.dotnet)
    command[1] = str(args.pilot_benchmark)
    command[command.index('--output')+1] = str(mesh)
    command[command.index('--row')+1] = str(row_path)
    pilot_environment = dict(environment, G3_ORACLE_POINT_INDEX='strtree', G3_ORACLE_EDGE_INDEX='strtree-hulls', G3_ORACLE_TIMING='1')
    record['oracle'] = stage(command, args.oracle_timeout, pilot_environment)
    stderr = record['oracle']['stderr']
    marker = stderr.rfind('ORACLE-JUDGE-BEGIN')
    if marker >= 0:
        record['oracle']['selfcheck_timing_lines_discarded'] = len(stderr[:marker].splitlines())
        record['oracle']['stderr'] = stderr[marker:]
    try:
        row = json.loads(row_path.read_text()) if row_path.exists() else None
    except json.JSONDecodeError:
        record['row_parse_error'] = 'Incomplete or malformed oracle row'
        row = None
    record['row'] = row
    record['verdict'] = classify(record['oracle'], row, base['input_sha256'])
    phases = re.findall(r'^ORACLE (.+?) [0-9.]+ ms$', record['oracle']['stderr'], re.M)
    record['last_completed_phase'] = phases[-1] if marker >= 0 and phases else None
    return record


def run(args):
    inherited = {k:v for k,v in os.environ.items() if k.startswith(('G3_M2_', 'G3_ORACLE_'))}
    if inherited:
        raise ValueError('Unexpected inherited variants: '+', '.join(sorted(inherited)))
    args.work.mkdir(parents=True, exist_ok=True)
    (args.work/'meshes').mkdir(exist_ok=True)
    old_meta = json.loads(args.baseline_metadata.read_text())
    binaries = {}
    for prefix, benchmark in [('mesh', args.mesh_benchmark), ('pilot', args.pilot_benchmark)]:
        for name, filename in [('benchmark',benchmark.name), ('oracle','Meshing2Oracle.dll'), ('library','geometry3Sharp.dll')]:
            path = benchmark.parent/filename
            binaries[prefix+'_'+name] = dict(path=str(path),sha256=digest(path))
            if prefix == 'mesh' and binaries[prefix+'_'+name]['sha256'] != old_meta[name+'_sha256']:
                raise ValueError('Frozen baseline binary differs: '+name)
    metadata = dict(binaries=binaries, baseline_metadata_sha256=digest(args.baseline_metadata),
                    controller_sha256=digest(__file__), working_directory=str(pathlib.Path.cwd()),
                    runtime_files={'mesh':runtime_files(args.mesh_benchmark),'pilot':runtime_files(args.pilot_benchmark)},
                    inherited_variants=inherited, pilot_environment={'G3_ORACLE_POINT_INDEX':'strtree','G3_ORACLE_EDGE_INDEX':'strtree-hulls','G3_ORACLE_TIMING':'1'},
                    mesh_timeout=args.mesh_timeout, oracle_timeout=args.oracle_timeout,
                    concurrency='one while baseline runs; two after complete baseline summary')
    meta = args.work/'metadata.json'
    if meta.exists() and json.loads(meta.read_text()) != metadata:
        raise ValueError('Resume metadata differs; use another directory')
    meta.write_text(json.dumps(metadata,indent=2)+'\n')
    output = args.work/'results.jsonl'
    done = {}
    if output.exists():
        for line in output.read_text().splitlines():
            row = json.loads(line); key = (row['case'],row['mode'])
            if key in done: raise ValueError('Duplicate pilot result')
            done[key] = row
    pending = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
        while True:
            baselines = baseline_rows(args.baseline_results)
            for key, row in done.items():
                if key not in baselines or baselines[key][0]['verdict'] != 'ORACLE_TIMEOUT' or row['baseline_record_sha256'] != baselines[key][1]:
                    raise ValueError('Completed pilot record no longer matches baseline')
            selected = {key:value for key,value in baselines.items() if value[0]['verdict']=='ORACLE_TIMEOUT'}
            finished = complete(args.baseline_summary)
            capacity = 2 if finished else 1
            queued = set(pending.values())
            for key in sorted(selected):
                if len(pending) >= capacity: break
                if key in done or key in queued: continue
                base, source_hash = selected[key]
                pending[executor.submit(judge,base,source_hash,args,dict(os.environ))] = key
            if finished and not pending and len(done)==len(selected): break
            if pending:
                ready,_ = concurrent.futures.wait(pending,timeout=1,return_when=concurrent.futures.FIRST_COMPLETED)
                for future in ready:
                    key = pending.pop(future); record = future.result()
                    with output.open('a') as stream:
                        stream.write(json.dumps(record)+'\n'); stream.flush(); os.fsync(stream.fileno())
                    done[key] = record
                    print(f"edge-pilot {len(done)}/{len(selected)} known timeouts: {key[0]} {key[1]} -> {record['verdict']}; last phase {record.get('last_completed_phase')}",flush=True)
            else:
                time.sleep(1)
    for info in binaries.values():
        if digest(info['path']) != info['sha256']: raise ValueError('Binary changed during pilot')
    for name, benchmark in [('mesh',args.mesh_benchmark),('pilot',args.pilot_benchmark)]:
        if runtime_files(benchmark) != metadata['runtime_files'][name]: raise ValueError('Runtime dependency changed during pilot')
    counts = {}
    for row in done.values(): counts[row['verdict']] = counts.get(row['verdict'],0)+1
    summary = dict(selected=len(selected),completed=len(done),verdicts=counts,
                   valid_with_steiner_limit=sum(r['verdict']=='VALID' and bool((r.get('row')or{}).get('steiner_limit_reached')) for r in done.values()),
                   baseline_results_sha256=digest(args.baseline_results))
    (args.work/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    print(json.dumps(summary),flush=True)
    return 0 if counts and set(counts)=={'VALID'} else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ['baseline-results','baseline-metadata','baseline-summary','mesh-benchmark','pilot-benchmark','dotnet','work']:
        parser.add_argument('--'+name,type=pathlib.Path,required=True)
    parser.add_argument('--mesh-timeout',type=int,default=60)
    parser.add_argument('--oracle-timeout',type=int,default=120)
    args = parser.parse_args()
    if min(args.mesh_timeout,args.oracle_timeout)<=0: parser.error('Budgets must be positive')
    raise SystemExit(run(args))
