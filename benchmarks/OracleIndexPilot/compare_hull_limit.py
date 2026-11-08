"""Paired full-query edge-hulls diagnostics on cached, hash-verified timeout meshes."""
import argparse
import hashlib
import json
import pathlib
import statistics
import subprocess
import time

from recheck_edges import digest, runtime_files


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--records', type=pathlib.Path, required=True)
    p.add_argument('--before', type=pathlib.Path, required=True)
    p.add_argument('--after', type=pathlib.Path, required=True)
    p.add_argument('--before-commit', required=True)
    p.add_argument('--after-commit', required=True)
    p.add_argument('--dotnet', required=True)
    p.add_argument('--out', type=pathlib.Path, required=True)
    p.add_argument('--rounds', type=int, default=3)
    p.add_argument('--timeout', type=float, default=180)
    args = p.parse_args()
    if args.rounds < 1:
        p.error('--rounds must be positive')
    rows = [json.loads(line) for line in args.records.read_text().splitlines() if line.strip()]
    if len(rows) != 18 or len({r['case'] for r in rows}) != 18:
        raise ValueError('Expected the 18 distinct former timeout records')
    meshes = []
    for row in rows:
        cmd = row['oracle']['command']
        mesh = pathlib.Path(cmd[cmd.index('--output') + 1])
        if row['verdict'] != 'VALID' or digest(mesh) != row['output_sha256']:
            raise ValueError('Mesh/lineage differs: ' + row['case'])
        meshes.append(mesh)
    args.out.mkdir(parents=True, exist_ok=True)
    metadata = dict(before_commit=args.before_commit, after_commit=args.after_commit,
                    records_sha256=digest(args.records), controller_sha256=digest(__file__),
                    rounds=args.rounds, timeout_seconds=args.timeout,
                    sdk=subprocess.check_output([args.dotnet, '--version'], text=True).strip(),
                    runtime={v: runtime_files(getattr(args, v)) for v in ('before', 'after')},
                    command=['<dotnet>', '<before-or-after>/OracleIndexPilot.dll', 'edge-hulls', '<same-hashed-mesh>'],
                    scope='All edge queries; component includes tree construction, excludes mesh parsing/key preparation; process wall recorded separately. Serial paired runs, alternating order. No concurrent build or other measurement.')
    meta = args.out / 'metadata.json'
    if meta.exists() and json.loads(meta.read_text()) != metadata:
        raise ValueError('Resume metadata differs')
    meta.write_text(json.dumps(metadata, indent=2) + '\n')
    result = args.out / 'pairs.jsonl'
    pairs = [json.loads(line) for line in result.read_text().splitlines()] if result.exists() else []
    done = {(r['case'], r['round']) for r in pairs}
    for run in range(args.rounds):
        for i, (row, mesh) in enumerate(zip(rows, meshes)):
            if (row['case'], run) in done:
                continue
            if digest(mesh) != row['output_sha256']:
                raise ValueError('Mesh changed')
            order = ['before', 'after'] if (run + i) % 2 == 0 else ['after', 'before']
            pair = dict(case=row['case'], round=run, order=order, mesh_sha256=row['output_sha256'])
            for variant in order:
                start = time.monotonic()
                proc = subprocess.run([args.dotnet, str(getattr(args, variant)), 'edge-hulls', str(mesh)],
                                      text=True, capture_output=True, timeout=args.timeout, check=True)
                pair[variant] = dict(process_seconds=time.monotonic() - start, component=json.loads(proc.stdout))
                if proc.stderr.strip():
                    raise ValueError('Unexpected stderr: ' + proc.stderr)
            for key in ('Queries', 'Hits', 'Fingerprint'):
                if pair['before']['component'][key] != pair['after']['component'][key]:
                    raise ValueError('Edge result differs: ' + row['case'] + '/' + key)
            with result.open('a') as output:
                output.write(json.dumps(pair) + '\n')
                output.flush()
            pairs.append(pair)
            print(json.dumps(dict(case=row['case'], round=run,
                                  before=pair['before']['component']['Seconds'],
                                  after=pair['after']['component']['Seconds'])), flush=True)
    summary = []
    for row in rows:
        subset = [r for r in pairs if r['case'] == row['case']]
        medians = {v: statistics.median(r[v]['component']['Seconds'] for r in subset) for v in ('before', 'after')}
        summary.append(dict(case=row['case'], mesh_sha256=row['output_sha256'], runs=len(subset),
                            **medians, ratio=medians['after'] / medians['before'],
                            hits=subset[0]['after']['component']['Hits'], queries=subset[0]['after']['component']['Queries']))
    (args.out / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')


if __name__ == '__main__':
    main()
