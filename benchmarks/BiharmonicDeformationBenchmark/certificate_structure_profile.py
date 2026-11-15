"""Bounded growth diagnosis; matrix proofs here never certify a deformation output."""
import argparse
import hashlib
import json
import re
import statistics
from pathlib import Path
import struct
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('dotnet'); parser.add_argument('producer'); parser.add_argument('checker')
parser.add_argument('--runs', type=int, choices=(1, 5), default=1)
args = parser.parse_args()
for name, path in [('producer', Path(args.producer)), ('checker', Path(args.checker)),
                   ('library', Path(args.producer).parent / 'geometry3Sharp.dll')]:
    print(name + '_sha256=' + hashlib.sha256(path.read_bytes()).hexdigest(), flush=True)
print('timing_scope=fresh_process_including_jit_and_gc not_release_cold_acceptance', flush=True)
with tempfile.TemporaryDirectory(prefix='bih-structure-profile-') as tmp:
    for n in (20, 30, 50, 100):
        solver_path = None
        for ordering in ('natural', 'solver'):
            path = Path(tmp) / f'{n}-{ordering}.json'
            result = subprocess.run([args.dotnet, args.producer, '--certificate-structure', str(n),
                                     ordering, '0.5', 'rotated', str(path)], capture_output=True, text=True, timeout=30)
            print(result.stdout + result.stderr, end='', flush=True)
            if result.returncode:
                raise RuntimeError('structural probe failed')
            print('structure_input_sha256=' + hashlib.sha256(path.read_bytes()).hexdigest(), flush=True)
            if ordering == 'solver':
                solver_path = path
        # Start with the present shift; a smaller proposal tests the larger-grid
        # shift diagnosis. No production limits, precision or tolerances change.
        if n > 50:
            print('matrix_check=skipped reason=bounded_experiment position_certificate=not_checked', flush=True)
            continue
        for shift in ((0.5, 0.125) if n == 50 else (0.5,)):
            data = json.loads(solver_path.read_text())
            data['shift_bits'] = struct.unpack('>q', struct.pack('>d', shift))[0]
            path = Path(tmp) / f'{n}-shift-{shift}.json'
            path.write_text(json.dumps(data, separators=(',', ':')))
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            print(f'matrix_case_grid={n} bits=256 shift={shift} input_sha256={digest}', flush=True)
            for mode in ('mesh', 'mesh-scalar'):
                totals, factors = [], []
                for repetition in range(args.runs):
                    print(f'matrix_method={mode} repetition={repetition+1}', flush=True)
                    try:
                        checked = subprocess.run([args.dotnet, args.checker, mode, '256', str(path)],
                                                 capture_output=True, text=True, timeout=30)
                    except subprocess.TimeoutExpired:
                        print('matrix_check=timeout seconds=30 position_certificate=not_checked', flush=True)
                        break
                    print(checked.stdout + checked.stderr, end='', flush=True)
                    if 'input_sha256=' + digest not in checked.stdout:
                        raise RuntimeError('checker did not bind its input')
                    if checked.returncode == 0:
                        status = 'positive_interval_pivots' if mode == 'mesh' else 'positive_scalar_pivots'
                        if 'status='+status not in checked.stdout or 'position_certificate=not_checked' not in checked.stdout:
                            raise RuntimeError('unexpected matrix-only checker status')
                        updates = data['structure']['schur_updates']
                        counter = 'updates' if mode == 'mesh' else 'products'
                        if f'{counter}={updates} ' not in checked.stdout:
                            raise RuntimeError('independent checker and structural operation counts disagree')
                        factors.append(float(re.search(r'(?<!_)elapsed_ms=([0-9.]+)', checked.stdout)[1]))
                        totals.append(float(re.search(r'mesh_total_ms=([0-9.]+)', checked.stdout)[1]))
                    elif 'status=uncertain' not in checked.stdout:
                        raise RuntimeError('unexpected checker failure')
                    else:
                        break  # An inconclusive/failed check is not a fast timing.
                if len(totals) == args.runs:
                    print(f'matrix_summary grid={n} shift={shift} method={mode} runs={args.runs} '
                          f'total_median_ms={statistics.median(totals):.3f} total_range_ms={min(totals):.3f}..{max(totals):.3f} '
                          f'factor_median_ms={statistics.median(factors):.3f} factor_range_ms={min(factors):.3f}..{max(factors):.3f} '
                          'position_certificate=not_checked', flush=True)
