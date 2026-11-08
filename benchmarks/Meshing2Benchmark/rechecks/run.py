"""Re-mesh historical exceptions with pinned inputs; never relabel old outputs.
Sequential, resumable, bounded stages; records numeric rows, not mesh assets.
"""
import argparse
import hashlib
import json
import os
import pathlib
import re
import subprocess
import time


def digest(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def classify(code, stdout, row, expected_hash):
    if row is None or row.get('input_sha256') != expected_hash:
        return 'ORACLE_ERROR'
    if code == 0 and re.search(r'^VALID ', stdout, re.M):
        return 'VALID'
    if code == 1 and re.search(r'^INVALID ', stdout, re.M):
        return 'INVALID'
    return 'ORACLE_ERROR'


def stage(command, timeout):
    start = time.monotonic()
    try:
        run = subprocess.run(command, capture_output=True, text=True, timeout=timeout)
        return dict(command=command, exit_code=run.returncode, seconds=time.monotonic()-start,
                    stdout=run.stdout, stderr=run.stderr)
    except subprocess.TimeoutExpired as e:
        def text(v): return v.decode(errors='replace') if isinstance(v, bytes) else v or ''
        return dict(command=command, timeout=timeout, seconds=time.monotonic()-start,
                    stdout=text(e.stdout), stderr=text(e.stderr))


def variant_environment():
    variants = {k:v for k,v in os.environ.items() if k.startswith(('G3_M2_', 'G3_ORACLE_'))}
    if variants:
        raise ValueError('Default-version recheck refuses variant environment: ' + ', '.join(sorted(variants)))
    return variants


def run(args):
    variants = variant_environment()
    spec = json.loads(args.manifest.read_text())
    keys = [(j['case'], j['mode']) for j in spec['jobs']]
    if not keys or len(set(keys)) != len(keys):
        raise ValueError('Manifest must contain nonempty unique case/mode jobs')
    args.work.mkdir(parents=True, exist_ok=True)
    metadata = dict(variant_environment=variants, benchmark_sha256=digest(args.benchmark),
                    oracle_sha256=digest(args.benchmark.parent/'Meshing2Oracle.dll'),
                    library_sha256=digest(args.benchmark.parent/'geometry3Sharp.dll'),
                    mesh_timeout=args.mesh_timeout, oracle_timeout=args.oracle_timeout,
                    manifest_sha256=digest(args.manifest), reference_commit=spec['reference_commit'],
                    historical_g3_commit=spec['historical_g3_commit'])
    meta_path = args.work/'metadata.json'
    if meta_path.exists() and json.loads(meta_path.read_text()) != metadata:
        raise ValueError('Resume metadata differs: use a fresh work directory')
    meta_path.write_text(json.dumps(metadata, indent=2)+'\n')
    results_path = args.work/'results.jsonl'
    done = set()
    if results_path.exists():
        done = {(row['case'],row['mode']) for line in results_path.read_text().splitlines() if (row:=json.loads(line))}
    jobs = sorted(spec['jobs'], key=lambda j: (j['reference_verdict']!='VALID',
                  j['old_verdict']=='ORACLE-TIMEOUT', int(j['old_triangles'] or 0), j['case'], j['mode']))
    for index, job in enumerate(jobs):
        if (job['case'],job['mode']) in done: continue
        source = pathlib.Path(job['input_path'])
        if digest(source) != job['input_sha256']: raise ValueError('Input hash differs: '+str(source))
        key = hashlib.sha256((job['case']+'|'+job['mode']).encode()).hexdigest()[:20]
        output = args.work/(key+'.mesh.txt'); row_path = args.work/(key+'.row.json')
        output.unlink(missing_ok=True); row_path.unlink(missing_ok=True)
        record = {k:v for k,v in job.items() if k != 'input_path'}
        record['mesh'] = stage([str(args.dotnet),str(args.benchmark),'mesh','--input',str(source),
                '--output',str(output),'--milestone','M3','--no-oracle'], args.mesh_timeout)
        mesh = record['mesh']
        if 'timeout' in mesh: record['verdict']='MESH_TIMEOUT'
        elif mesh['exit_code'] != 0 or not output.exists() or not re.search(r'^status (Success|Empty),',mesh['stdout'],re.M):
            record['verdict']='MESH_ERROR'
        else:
            record['output_sha256']=digest(output)
            record['oracle']=stage([str(args.dotnet),str(args.benchmark),'oracle','--input',str(source),
                    '--output',str(output),'--milestone','M3','--case',job['case'],'--row',str(row_path)], args.oracle_timeout)
            oracle=record['oracle']
            row=json.loads(row_path.read_text()) if row_path.exists() else None
            record['row']=row
            record['verdict']='ORACLE_TIMEOUT' if 'timeout' in oracle else classify(oracle['exit_code'],oracle['stdout'],row,job['input_sha256'])
        # Commit the record durably before releasing large, regenerable mesh artifacts.
        with results_path.open('a') as stream:
            stream.write(json.dumps(record)+'\n');stream.flush()
            __import__('os').fsync(stream.fileno())
        output.unlink(missing_ok=True); row_path.unlink(missing_ok=True)
        print(f"{index+1}/{len(jobs)} {job['case']} {job['mode']}: {job['old_verdict']} -> {record['verdict']}",flush=True)
    rows=[json.loads(line) for line in results_path.read_text().splitlines()]
    tally={}
    for row in rows: tally[row['verdict']]=tally.get(row['verdict'],0)+1
    summary=dict(planned=len(jobs),completed=len(rows),verdicts=tally,
                 valid_with_steiner_limit=sum(r['verdict']=='VALID' and bool((r.get('row') or {}).get('steiner_limit_reached')) for r in rows))
    (args.work/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    print(json.dumps(summary),flush=True)
    return summary


def acceptance_exit(summary):
    return 0 if summary['planned'] > 0 and summary['completed'] == summary['planned'] and set(summary['verdicts']) <= {'VALID'} else 1


if __name__=='__main__':
    p=argparse.ArgumentParser()
    for key in ('manifest','dotnet','benchmark','work'):p.add_argument('--'+key,type=pathlib.Path,required=True)
    p.add_argument('--mesh-timeout',type=int,default=60)
    p.add_argument('--oracle-timeout',type=int,default=120)
    a=p.parse_args()
    if min(a.mesh_timeout,a.oracle_timeout)<=0:p.error('Timeouts must be positive')
    raise SystemExit(acceptance_exit(run(a)))
