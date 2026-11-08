"""Re-mesh historical exceptions with pinned inputs; never relabel old outputs.
Sequential, resumable, bounded stages; records numeric rows, not mesh assets.

Records are compact: captured stdout/stderr are reduced to per-violation-class
counts, line counts and SHA-256 digests before anything is written, and local
paths in commands are replaced by placeholders (<dotnet>, <benchmark>, <inputs>,
<work>). The oracle row's area rational and reason text are kept as digests.
"""
import argparse
import csv
import hashlib
import json
import os
import pathlib
import re
import subprocess
import time

VIOLATION = re.compile(r'^VIOLATION (\w+):', re.M)
VERDICT_COLUMNS = ['case', 'mode', 'historical_verdict', 'current_verdict', 'historical_reference_verdict',
                   'triangles', 'steiner_limit_reached', 'violations', 'input_sha256', 'output_sha256']


def digest(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def text_digest(text):
    return hashlib.sha256(text.encode()).hexdigest()


def input_name(case, mode):
    """File name of a prepared input in the inputs directory (prepare.py writes it, run.py reads it)."""
    return hashlib.sha256((case+'|'+mode).encode()).hexdigest()+'.input.txt'


def job_key(job):
    return job['case']+'|'+job['mode']


def classify(code, stdout, row, expected_hash):
    if row is None or row.get('input_sha256') != expected_hash:
        return 'ORACLE_ERROR'
    if code == 0 and re.search(r'^VALID ', stdout, re.M):
        return 'VALID'
    if code == 1 and re.search(r'^INVALID ', stdout, re.M):
        return 'INVALID'
    return 'ORACLE_ERROR'


def count_classes(names):
    counts = {}
    for name in names: counts[name] = counts.get(name, 0)+1
    return dict(sorted(counts.items()))


def portable(value, roots):
    """Replace absolute local path prefixes by placeholders (roots: prefix -> placeholder)."""
    for prefix, placeholder in sorted(roots.items(), key=lambda p: -len(p[0])):
        if value == prefix: return placeholder
        if value.startswith(prefix.rstrip('/')+'/'): return placeholder+'/'+value[len(prefix.rstrip('/'))+1:]
    return value


def compact_stage(stage, roots):
    """Drop captured output: keep violation-class counts, line counts and digests of stdout and stderr."""
    out = {k: v for k, v in stage.items() if k not in ('stdout', 'stderr')}
    out['command'] = [portable(str(c), roots) for c in stage['command']]
    stdout, stderr = stage.get('stdout') or '', stage.get('stderr') or ''
    status = re.search(r'^status (\w+),', stdout, re.M)
    if status: out['status'] = status.group(1)
    out['violations'] = count_classes(VIOLATION.findall(stdout))
    for name, text in (('stdout', stdout), ('stderr', stderr)):
        out[name+'_lines'] = len(text.splitlines()); out[name+'_sha256'] = text_digest(text)
    return out


def compact_row(row):
    """The oracle row without single-triangle text and the exact area rational (kept as digests)."""
    if row is None: return None
    out = dict(row)
    if 'area2' in out: out['area2_sha256'] = text_digest(str(out.pop('area2')))
    if 'reason' in out:
        reason = out.pop('reason') or ''
        out['reason_violations'] = count_classes(p.split(':')[0].strip() for p in reason.split('; ') if p.strip())
        out['reason_sha256'] = text_digest(reason)
    return out


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


def load_jobs(manifest, prepared):
    """Join the historical jobs with the prepared case|mode -> input_sha256 map."""
    spec = json.loads(manifest.read_text())
    keys = [job_key(j) for j in spec['jobs']]
    if not keys or len(set(keys)) != len(keys):
        raise ValueError('Manifest must contain nonempty unique case/mode jobs')
    hashes = json.loads(prepared.read_text())
    if list(hashes) != keys:
        raise ValueError('Prepared manifest does not list exactly the manifest jobs in order')
    return spec, [dict(j, input_sha256=hashes[job_key(j)]) for j in spec['jobs']]


def verdict_rows(records):
    rows = []
    for r in sorted(records, key=lambda r: (r['case'], r['mode'])):
        row = r.get('row') or {}
        rows.append(dict(case=r['case'], mode=r['mode'], historical_verdict=r['old_verdict'], current_verdict=r['verdict'],
                         historical_reference_verdict=r['reference_verdict'], triangles=str(row.get('triangles', '')),
                         steiner_limit_reached=str(row.get('steiner_limit_reached', '')),
                         violations=';'.join(row.get('reason_violations', {})),
                         input_sha256=r['input_sha256'], output_sha256=r.get('output_sha256', '')))
    return rows


def write_verdicts(records, path):
    with open(path, 'w', newline='') as stream:
        writer = csv.DictWriter(stream, VERDICT_COLUMNS, lineterminator='\n')
        writer.writeheader(); writer.writerows(verdict_rows(records))


def summarize(records, planned):
    tally = {}
    for row in records: tally[row['verdict']] = tally.get(row['verdict'], 0)+1
    return dict(planned=planned, completed=len(records), verdicts=tally,
                valid_with_steiner_limit=sum(r['verdict']=='VALID' and bool((r.get('row') or {}).get('steiner_limit_reached')) for r in records))


def run(args):
    variants = variant_environment()
    spec, jobs = load_jobs(args.manifest, args.prepared)
    args.work.mkdir(parents=True, exist_ok=True)
    metadata = dict(variant_environment=variants, benchmark_sha256=digest(args.benchmark),
                    oracle_sha256=digest(args.benchmark.parent/'Meshing2Oracle.dll'),
                    library_sha256=digest(args.benchmark.parent/'geometry3Sharp.dll'),
                    mesh_timeout=args.mesh_timeout, oracle_timeout=args.oracle_timeout,
                    manifest_sha256=digest(args.manifest), prepared_sha256=digest(args.prepared),
                    reference_commit=spec['reference_commit'], historical_g3_commit=spec['historical_g3_commit'])
    meta_path = args.work/'metadata.json'
    if meta_path.exists() and json.loads(meta_path.read_text()) != metadata:
        raise ValueError('Resume metadata differs: use a fresh work directory')
    meta_path.write_text(json.dumps(metadata, indent=2)+'\n')
    roots = {str(args.dotnet): '<dotnet>', str(args.benchmark): '<benchmark>',
             str(args.inputs.resolve()): '<inputs>', str(args.inputs): '<inputs>',
             str(args.work.resolve()): '<work>', str(args.work): '<work>'}
    results_path = args.work/'results.jsonl'
    done = set()
    if results_path.exists():
        done = {(row['case'],row['mode']) for line in results_path.read_text().splitlines() if (row:=json.loads(line))}
    jobs = sorted(jobs, key=lambda j: (j['reference_verdict']!='VALID',
                  j['old_verdict']=='ORACLE-TIMEOUT', int(j['old_triangles'] or 0), j['case'], j['mode']))
    for index, job in enumerate(jobs):
        if (job['case'],job['mode']) in done: continue
        source = args.inputs/input_name(job['case'], job['mode'])
        if not source.exists() or digest(source) != job['input_sha256']: raise ValueError('Input hash differs: '+job_key(job))
        key = hashlib.sha256(job_key(job).encode()).hexdigest()[:20]
        output = args.work/(key+'.mesh.txt'); row_path = args.work/(key+'.row.json')
        output.unlink(missing_ok=True); row_path.unlink(missing_ok=True)
        record = dict(job)
        mesh = stage([str(args.dotnet),str(args.benchmark),'mesh','--input',str(source),
                '--output',str(output),'--milestone','M3','--no-oracle'], args.mesh_timeout)
        record['mesh'] = compact_stage(mesh, roots)
        if 'timeout' in mesh: record['verdict']='MESH_TIMEOUT'
        elif mesh['exit_code'] != 0 or not output.exists() or not re.search(r'^status (Success|Empty),',mesh['stdout'],re.M):
            record['verdict']='MESH_ERROR'
        else:
            record['output_sha256']=digest(output)
            oracle=stage([str(args.dotnet),str(args.benchmark),'oracle','--input',str(source),
                    '--output',str(output),'--milestone','M3','--case',job['case'],'--row',str(row_path)], args.oracle_timeout)
            record['oracle']=compact_stage(oracle, roots)
            row=json.loads(row_path.read_text()) if row_path.exists() else None
            record['row']=compact_row(row)
            record['verdict']='ORACLE_TIMEOUT' if 'timeout' in oracle else classify(oracle['exit_code'],oracle['stdout'],row,job['input_sha256'])
        # Commit the record durably before releasing large, regenerable mesh artifacts.
        with results_path.open('a') as stream:
            stream.write(json.dumps(record)+'\n');stream.flush()
            os.fsync(stream.fileno())
        output.unlink(missing_ok=True); row_path.unlink(missing_ok=True)
        print(f"{index+1}/{len(jobs)} {job['case']} {job['mode']}: {job['old_verdict']} -> {record['verdict']}",flush=True)
    rows=[json.loads(line) for line in results_path.read_text().splitlines()]
    summary=summarize(rows, len(jobs))
    (args.work/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    write_verdicts(rows, args.work/'verdicts.csv')
    print(json.dumps(summary),flush=True)
    return summary


def acceptance_exit(summary):
    return 0 if summary['planned'] > 0 and summary['completed'] == summary['planned'] and set(summary['verdicts']) <= {'VALID'} else 1


if __name__=='__main__':
    p=argparse.ArgumentParser()
    for key in ('manifest','prepared','inputs','dotnet','benchmark','work'):p.add_argument('--'+key,type=pathlib.Path,required=True)
    p.add_argument('--mesh-timeout',type=int,default=60)
    p.add_argument('--oracle-timeout',type=int,default=120)
    a=p.parse_args()
    if min(a.mesh_timeout,a.oracle_timeout)<=0:p.error('Timeouts must be positive')
    raise SystemExit(acceptance_exit(run(a)))
