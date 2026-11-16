"""Count complete scalar certificate work without executing its Schur updates.

First compare against explicit graph elimination and the independent small dense
reference. Larger runs factor an SPD surrogate with the identical Q pattern;
these are structural counts, not original-matrix proofs or deformation timings.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile

from certificate_structure_controls import reference
from certificate_trace_diagnostic import closed_grid, sparsity_bound, perron_bound

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('dotnet')
parser.add_argument('producer')
args = parser.parse_args()
producer = Path(args.producer).resolve()
for path in (producer, producer.parent/'geometry3Sharp.dll'):
    print(path.name+'_sha256='+hashlib.sha256(path.read_bytes()).hexdigest(), flush=True)

def run(width, height, output, growth=True):
    command = [args.dotnet, str(producer), '--certificate-growth' if growth else '--certificate-structure',
               f'{width}x{height}' if growth else str(width), 'solver', '0.015625', 'rotated', str(output)]
    result = subprocess.run(command, capture_output=True, text=True, timeout=60)
    if result.returncode:
        raise RuntimeError(result.stdout+result.stderr)
    return result.stdout, json.loads(output.read_text())

with tempfile.TemporaryDirectory(prefix='bih-growth-') as directory:
    directory = Path(directory)
    for size in (3, 5, 7, 20, 50):
        _, explicit = run(size, size, directory/f'explicit-{size}.json', False)
        _, counted = run(size, size, directory/f'growth-{size}.json')
        expected = explicit['structure']
        if size <= 7:
            assert expected == reference(explicit)
        assert expected['complete']
        for key in ('assembly_updates', 'schur_updates', 'initial_entries', 'factor_entries'):
            assert counted[key] == expected[key], (size, key)
        assert counted['position_certificate'] == 'not_checked'
        print(f'grid={size} explicit_counts_match=True dense_reference_match={size<=7}', flush=True)
    for width, height in ((100,100),(200,125),(250,200),(400,250)):
        stdout, data = run(width, height, directory/f'{width}x{height}.json')
        assert data['vertices'] == width*height and data['free'] == (width-2)*height
        assert data['scalar_retained_entries_lower_bound'] == data['factor_entries']-data['free']
        assert 'position_certificate=not_checked' in stdout
        print(stdout, end='', flush=True)
        assert len(data['preceding_counts'])==data['free']
        assert sum(data['preceding_counts'])==data['factor_entries']-data['free']
        trace, rayleigh = closed_grid(width, height)
        bound = sparsity_bound(data)
        print(f'model=ideal_rational_grid sparse_bound_III_upper={float(bound):.12g} rayleigh_upper={float(rayleigh):.12g} correction_over_rayleigh={float(bound/rayleigh):.6g} position_certificate=not_checked', flush=True)
        improved = perron_bound(data)
        print(f'model=ideal_rational_grid perron_2026_upper={float(improved):.12g} correction_over_rayleigh={float(improved/rayleigh):.6g} position_certificate=not_checked',flush=True)
