"""Check actual large production outputs; record cold/reuse costs and file hashes.

Build the producer first. No external certificate claim is made by this harness;
the production certificate has the runtime premises documented in the proof.
Generated meshes exist only in a temporary directory. Use --expect-rejection with
a producer loading the preceding library to record the former domain limit.
"""
import argparse
import hashlib
from pathlib import Path
import subprocess
import tempfile
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('dotnet')
parser.add_argument('producer')
parser.add_argument('--sizes', nargs='+', type=int, default=[50, 100])
parser.add_argument('--reuse', action='store_true')
parser.add_argument('--expect-rejection', action='store_true')
args = parser.parse_args()
if args.reuse and args.expect_rejection:
    parser.error('a rejected call has no prepared state to profile')
producer = Path(args.producer).resolve()
for path in (producer, producer.parent / 'geometry3Sharp.dll'):
    print(f'{path.name}_sha256={hashlib.sha256(path.read_bytes()).hexdigest()}', flush=True)
with tempfile.TemporaryDirectory(prefix='bih-large-') as directory:
    for size in args.sizes:
        for orientation in ('aligned', 'rotated'):
            output = Path(directory) / f'{size}-{orientation}.json'
            command = [args.dotnet, str(producer),
                       '--certificate-reuse-profile' if args.reuse else '--certificate',
                       str(size), orientation, 'strict', str(output)]
            start = time.monotonic()
            result = subprocess.run(command, capture_output=True, text=True, timeout=240)
            print(f'grid={size} orientation={orientation} exit={result.returncode} process_seconds={time.monotonic()-start:.3f}', flush=True)
            print(result.stdout + result.stderr, end='', flush=True)
            if args.expect_rejection:
                if result.returncode != 1 or 'status=AccuracyNotEstablished' not in result.stdout or output.exists():
                    raise RuntimeError('expected the old strict size rejection')
            else:
                if result.returncode != 0 or 'status=Success accuracy=Certified' not in result.stdout:
                    raise RuntimeError('large output was not certified')
                print('candidate_sha256=' + hashlib.sha256(output.read_bytes()).hexdigest(), flush=True)
