"""Bounded synthetic A/B + sample-convergence evidence; writes a compact numeric JSON summary.
python3 verify_closest.py --dotnet /path/to/dotnet --base /path/to/base.dll --current /path/to/current.dll --out /path/to/evidence.json

The summary holds no machine paths: commands use the placeholders <dotnet>, <base.dll> and <current.dll>.
Fields that agree between base and current (asserted below) are stored once per case; the new closest_*
fields are stored per sample count. Wall-clock fields are dropped except the two closest-point phase times
(closest_tree_s, closest_measure_s) that the README overhead table cites.
"""
import argparse
import hashlib
import json
import math
import os
import pathlib
import struct
import subprocess
import tempfile

KEPT_TIMINGS = ('closest_tree_s', 'closest_measure_s')
ARGS = ('case', 'grid', 'coords', 'win', 'tol', 'maxerr', 'closest_samples')
CONSTANT = dict(closest_direction='result_to_reference', closest_units='m', closest_sampling='area_weighted_stratified',
                closest_seed=0, closest_status='ok')


def is_timing(key):
    return key.endswith('_s') or key == 'peak_mb'


def compact(runs):
    """Groups raw rows by shape, placement and operation; drops paths and run-to-run timings."""
    shapes = {}
    for run in runs:
        row = run['row']
        shape = shapes.setdefault(run['shape'], dict(grid_sha256=run['grid_sha256'], cases=[]))
        assert shape['grid_sha256'] == run['grid_sha256']
        key = (row['coords'], row['case'])
        case = next((c for c in shape['cases'] if (c['coords'], c['case']) == key), None)
        if case is None:
            case = dict(coords=row['coords'], case=row['case'], existing_fields=None, closest={})
            shape['cases'].append(case)
        existing = {k: v for k, v in row.items()
                    if not k.startswith('closest_') and not is_timing(k) and k not in ARGS}
        if case['existing_fields'] is None:
            case['existing_fields'] = existing
        assert case['existing_fields'] == existing, (run['shape'], key)
        if run['variant'] == 'current':
            for k, v in CONSTANT.items():
                assert row[k] == v, (run['shape'], key, k, row[k])
            assert row['closest_requested'] == int(row['closest_samples'])
            case['closest'][row['closest_samples']] = {
                k: v for k, v in row.items()
                if k.startswith('closest_') and k not in CONSTANT and k not in ('closest_samples', 'closest_requested')
                and (not is_timing(k) or k in KEPT_TIMINGS)}
    return dict(
        command='TERRAIN_DATA=<tmp> <dotnet> <base.dll|current.dll> <case> grid=grid coords=<coords> '
                'win=632000,5712000,632023,5712023 tol=0.1 maxerr=0.1 closest_samples=<samples>',
        grid='24x24 float32 heights, x0=632000 y0=5712000 dx=1 (see main)',
        base_samples=[10000], current_samples=[1000, 10000, 50000],
        existing_fields_note='identical on base and current; timing fields omitted',
        closest_constant=CONSTANT,
        shapes=shapes)


def dump(summary):
    """Indented JSON whose innermost objects (one measured row each) stay on one line."""
    def nested(v):
        return isinstance(v, dict) or isinstance(v, list) and any(isinstance(x, dict) for x in v)

    def emit(value, depth):
        if isinstance(value, dict) and any(nested(v) for v in value.values()):
            pad = ' ' * (depth + 1)
            items = [pad + json.dumps(k) + ': ' + emit(v, depth + 1) for k, v in value.items()]
            return '{\n' + ',\n'.join(items) + '\n' + ' ' * depth + '}'
        if nested(value) and isinstance(value, list):
            pad = ' ' * (depth + 1)
            return '[\n' + ',\n'.join(pad + emit(v, depth + 1) for v in value) + '\n' + ' ' * depth + ']'
        return json.dumps(value)
    return emit(summary, 0) + '\n'


def main():
    p = argparse.ArgumentParser()
    for name in ('dotnet', 'base', 'current', 'out'):
        p.add_argument('--' + name, required=True)
    a = p.parse_args()
    runs = []
    with tempfile.TemporaryDirectory() as d:
        path = pathlib.Path(d)
        for shape in ('flat', 'slope', 'ridge'):
            n = 24
            heights = [2 if shape == 'flat' else 2*i+j if shape == 'slope' else 8*math.exp(-((i-11.5)/3)**2)+.1*j for j in range(n) for i in range(n)]
            data = struct.pack('<' + str(n*n) + 'f', *heights)
            for coords in ('local', 'utm'):
                meta = dict(x0=632000, y0=5712000, dx=1, nx=n, ny=n)
                (path / 'grid.json').write_text(json.dumps(meta))
                (path / 'grid.f32').write_bytes(data)
                for case in ('tdc', 'gtin'):
                    old = None
                    for variant, dll in [('base', a.base), ('current', a.current)]:
                        for count in ([10000] if variant == 'base' else [1000, 10000, 50000]):
                            args = [case, 'grid=grid', 'coords='+coords, 'win=632000,5712000,632023,5712023', 'tol=0.1', 'maxerr=0.1', 'closest_samples='+str(count)]
                            command = [a.dotnet, dll] + args
                            output = subprocess.check_output(command, env=dict(os.environ, TERRAIN_DATA=d), text=True)
                            row = json.loads(output.strip().splitlines()[-1])
                            assert row['status'] == 'ok' and row['ok'], row
                            if variant == 'base':
                                old = row
                                assert 'closest_d3_mean' not in row
                            else:
                                # Every pre-existing non-timing measurement agrees; no new field was zero on base.
                                for k, v in old.items():
                                    if k.endswith('_s') or k in ('peak_mb', 'closest_samples'): continue
                                    assert row[k] == v, (shape, coords, case, k, v, row[k])
                                assert row['closest_valid'] == count
                                assert row['closest_d3_n'] == row['closest_dh_n'] == row['closest_dv_n']
                            runs.append(dict(shape=shape, variant=variant, grid_sha256=hashlib.sha256(data).hexdigest(), args=args, row=row))
    pathlib.Path(a.out).write_text(dump(compact(runs)))
    print(f'{len(runs)} synthetic rows; all pre-existing non-timing fields identical')


if __name__ == '__main__':
    main()
