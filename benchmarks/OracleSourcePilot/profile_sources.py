"""Resumable sources-phase diagnostics on procedurally meshed M1 lenses."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def runtime(dll):
    return {p.name: sha(p) for pattern in ('*.dll', '*.deps.json', '*.runtimeconfig.json')
            for p in sorted(dll.parent.glob(pattern))}


def run(command, env, seconds):
    start = time.monotonic()
    try:
        p = subprocess.run(command, env=env, text=True, capture_output=True, timeout=seconds)
        code, stdout, stderr = p.returncode, p.stdout, p.stderr
    except subprocess.TimeoutExpired as e:
        code = 'TIMEOUT'
        stdout = (e.stdout or b'').decode() if isinstance(e.stdout, bytes) else e.stdout or ''
        stderr = (e.stderr or b'').decode() if isinstance(e.stderr, bytes) else e.stderr or ''
    return dict(exit_code=code, process_seconds=time.monotonic()-start,
                output=[json.loads(line) for line in stdout.splitlines() if line.startswith('{')],
                phases_ms={m[0]: float(m[1]) for m in re.findall(r'^ORACLE (.*?) ([0-9.]+) ms$', stderr, re.M)},
                error=stderr if code not in (0, 'TIMEOUT') else '')


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--dotnet', required=True)
    p.add_argument('--pilot', type=Path, required=True)
    p.add_argument('--mesher', type=Path, required=True)
    p.add_argument('--out', type=Path, required=True)
    p.add_argument('--label', required=True)
    p.add_argument('--mode', choices=['grid', 'scan'])
    p.add_argument('--sizes', default='129,257,513,1025,30001', help='points per arc, total 2*m-2')
    p.add_argument('--full-max-points', type=int, default=1024)
    p.add_argument('--seconds', type=float, default=90)
    p.add_argument('--repeats', type=int, default=1)
    args = p.parse_args()
    sizes = list(map(int, args.sizes.split(',')))
    if args.repeats < 1 or min(sizes) < 3 or args.seconds <= 0:
        p.error('Positive repeats/budget and m>=3 required')
    args.out.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ)
    env.pop('G3_ORACLE_SOURCE_INDEX', None)
    if args.mode:
        env['G3_ORACLE_SOURCE_INDEX'] = args.mode
    env['G3_ORACLE_TIMING'] = '1'
    metadata = dict(controller_sha256=sha(__file__), pilot_runtime=runtime(args.pilot),
                    mesher_runtime=runtime(args.mesher), sizes=sizes, mode=args.mode,
                    full_max_points=args.full_max_points, seconds=args.seconds, repeats=args.repeats,
                    commands=['<dotnet> <pilot> lens-input <m> <input>',
                              '<dotnet> <mesher> mesh --no-oracle --milestone M1 --input <input> --output <mesh>',
                              '<dotnet> <pilot> phase <input> <mesh>',
                              '<dotnet> <pilot> full <input> <mesh> <report>'])
    meta = args.out/(args.label+'.metadata.json')
    if meta.exists() and json.loads(meta.read_text()) != metadata:
        raise ValueError('Resume metadata differs')
    meta.write_text(json.dumps(metadata, indent=2)+'\n')
    result = args.out/(args.label+'.jsonl')
    rows = [json.loads(l) for l in result.read_text().splitlines()] if result.exists() else []
    for m in sizes:
        inp, mesh = args.out/f'lens-{m}.input.txt', args.out/f'lens-{m}.mesh.txt'
        fixture = args.out/f'lens-{m}.fixture.json'
        if not fixture.exists():
            subprocess.run([args.dotnet, str(args.pilot), 'lens-input', str(m), str(inp)], check=True, env=env)
            start = time.monotonic()
            subprocess.run([args.dotnet, str(args.mesher), 'mesh', '--no-oracle', '--milestone', 'M1',
                            '--input', str(inp), '--output', str(mesh)], check=True, env=env, capture_output=True, timeout=120)
            fixture.write_text(json.dumps(dict(input_sha256=sha(inp), mesh_sha256=sha(mesh), mesher_runtime=runtime(args.mesher),
                                              mesh_process_seconds=time.monotonic()-start), indent=2)+'\n')
        lineage = json.loads(fixture.read_text())
        if sha(inp) != lineage['input_sha256'] or sha(mesh) != lineage['mesh_sha256'] or runtime(args.mesher) != lineage['mesher_runtime']:
            raise ValueError('Fixture/producer lineage differs')
        for repeat in range(args.repeats):
            for kind in ['phase'] + (['full'] if 2*m-2 <= args.full_max_points else []):
                if any(r['m']==m and r['repeat']==repeat and r['kind']==kind for r in rows):
                    continue
                report = args.out/f'{args.label}-{m}-{repeat}.report.json'
                command = [args.dotnet, str(args.pilot), kind, str(inp), str(mesh)]
                if kind == 'full':
                    command.append(str(report))
                measured = run(command, env, args.seconds)
                if measured['exit_code'] not in (0, 'TIMEOUT'):
                    raise RuntimeError(measured)
                row = dict(m=m, points=2*m-2, repeat=repeat, kind=kind,
                           input_sha256=lineage['input_sha256'], mesh_sha256=lineage['mesh_sha256'], **measured)
                if kind=='full' and measured['exit_code']==0:
                    row['report_sha256'] = sha(report)
                with result.open('a') as output:
                    output.write(json.dumps(row)+'\n'); output.flush()
                rows.append(row)
                print(json.dumps(row), flush=True)


if __name__ == '__main__':
    main()
