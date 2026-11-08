"""Rotate four isolated edge-hulls builds on one hashed mesh; diagnostic controls only."""
import argparse
import json
import pathlib
import subprocess
import time
from recheck_edges import digest, runtime_files


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--variant', nargs=3, action='append', required=True, metavar=('NAME', 'DLL', 'EDGE_SOURCE'))
    p.add_argument('--mesh', type=pathlib.Path, required=True)
    p.add_argument('--mesh-sha256', required=True)
    p.add_argument('--dotnet', required=True)
    p.add_argument('--out', type=pathlib.Path, required=True)
    args = p.parse_args()
    if digest(args.mesh) != args.mesh_sha256 or len({v[0] for v in args.variant}) != len(args.variant):
        raise ValueError('Mesh hash or variant names differ')
    metadata = dict(mesh_sha256=args.mesh_sha256, controller_sha256=digest(__file__),
                    variants={name: dict(runtime=runtime_files(pathlib.Path(dll)), edge_source_sha256=digest(source))
                              for name, dll, source in args.variant}, rounds=3,
                    controls={'uncapped': 'b58b0078 with only HullLimit=16 changed to int.MaxValue',
                              'uncounted': 'b58b0078 with only pruneTests++ omitted; HullLimit stays 16'})
    args.out.mkdir(parents=True, exist_ok=True)
    meta = args.out / 'overhead-metadata.json'
    if meta.exists() and json.loads(meta.read_text()) != metadata:
        raise ValueError('Resume metadata differs')
    meta.write_text(json.dumps(metadata, indent=2) + '\n')
    result = args.out / 'overhead-rounds.jsonl'
    records = [json.loads(line) for line in result.read_text().splitlines()] if result.exists() else []
    for run in range(3):
        if any(r['round'] == run for r in records):
            continue
        variants = args.variant[run:] + args.variant[:run]
        record = dict(round=run, order=[v[0] for v in variants], results={})
        for name, dll, _ in variants:
            if digest(args.mesh) != args.mesh_sha256:
                raise ValueError('Mesh changed')
            start = time.monotonic()
            proc = subprocess.run([args.dotnet, dll, 'edge-hulls', str(args.mesh)], text=True,
                                  capture_output=True, timeout=180, check=True)
            record['results'][name] = dict(process_seconds=time.monotonic() - start, component=json.loads(proc.stdout))
            if proc.stderr.strip():
                raise ValueError('Unexpected stderr')
        for key in ('Queries', 'Hits', 'Fingerprint'):
            if len({r['component'][key] for r in record['results'].values()}) != 1:
                raise ValueError('Result differs: ' + key)
        with result.open('a') as output:
            output.write(json.dumps(record) + '\n')
            output.flush()
        print(json.dumps({name: r['component']['Seconds'] for name, r in record['results'].items()}), flush=True)


if __name__ == '__main__':
    main()
