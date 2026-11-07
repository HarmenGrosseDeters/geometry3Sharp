"""Bounded synthetic A/B + sample-convergence evidence; writes numeric JSON only.
python3 verify_closest.py --dotnet /path/to/dotnet --base /path/to/base.dll --current /path/to/current.dll --out /path/to/evidence.json
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
                            runs.append(dict(shape=shape, variant=variant, grid_sha256=hashlib.sha256(data).hexdigest(), command=command, row=row))
    pathlib.Path(a.out).write_text(json.dumps(dict(runs=runs), indent=2)+'\n')
    print(f'{len(runs)} synthetic rows; all pre-existing non-timing fields identical')


if __name__ == '__main__':
    main()
