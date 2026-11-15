"""Check the structural diagnostic against dense graph elimination and the independent interval pilot."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile


def reference(data):
    # Independent full symmetric boolean matrix elimination on small fixtures.
    fixed = data['fixed']
    free = {v: i for i, v in enumerate(v for v in range(len(fixed)) if not fixed[v])}
    n = len(free)
    laplacian = [[False] * len(fixed) for _ in fixed]
    for face in data['faces']:
        for a in face:
            for b in face:
                laplacian[a][b] = True
    matrix = [[False] * n for _ in range(n)]
    assembly = 0
    for row in laplacian:
        entries = [free[v] for v in free if row[v]]
        assembly += len(entries) * (len(entries) + 1) // 2
        for a in entries:
            for b in entries:
                matrix[a][b] = True
    order = data['order']
    assert sorted(order) == list(range(n))
    matrix = [[matrix[a][b] for b in order] for a in order]
    live = sum(matrix[r][c] for c in range(n) for r in range(c, n))
    initial = peak = live
    schur = factor = 0
    for k in range(n):
        neighbors = [i for i in range(k + 1, n) if matrix[i][k]]
        schur += len(neighbors) * (len(neighbors) + 1) // 2
        factor += len(neighbors) + 1
        for a in neighbors:
            for b in neighbors:
                if a >= b and not matrix[a][b]:
                    matrix[a][b] = matrix[b][a] = True
                    live += 1
                    peak = max(peak, live)
        live -= len(neighbors) + 1
    return dict(assembly_updates=assembly, schur_updates=schur, initial_entries=initial,
                peak_live_entries=peak, factor_entries=factor, completed_columns=n, complete=True)


def run(command):
    return subprocess.run(command, capture_output=True, text=True, timeout=30)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('dotnet'); parser.add_argument('producer'); parser.add_argument('checker')
    args = parser.parse_args()
    for name, path in [('producer', Path(args.producer)), ('checker', Path(args.checker)),
                       ('library', Path(args.producer).parent / 'geometry3Sharp.dll')]:
        print(name + '_sha256=' + hashlib.sha256(path.read_bytes()).hexdigest(), flush=True)
    with tempfile.TemporaryDirectory(prefix='bih-structure-controls-') as tmp:
        sample = None
        for n in (3, 5, 7):
            for ordering in ('natural', 'solver'):
                path = Path(tmp) / f'{n}-{ordering}.json'
                command = [args.dotnet, args.producer, '--certificate-structure', str(n), ordering, '0.5', 'rotated', str(path)]
                result = run(command)
                assert result.returncode == 0, result.stdout + result.stderr
                assert 'reason=structure_only' in result.stdout
                data = json.loads(path.read_text())
                assert data['structure'] == reference(data), result.stdout
                assert len(data['vertices_bits']) == n*n and len(data['faces']) == 2*(n-1)**2
                assert 'candidate_bits' not in data and 'target_bits' not in data
                # A repeat uses a fresh file and must produce identical data.
                repeated = Path(tmp) / f'{n}-{ordering}-repeat.json'
                repeat = run(command[:-1] + [str(repeated)])
                assert repeat.returncode == 0 and path.read_bytes() == repeated.read_bytes()
                assert run(command).returncode != 0  # Existing exports are protected.
                print(f'grid={n} ordering={ordering} dense_structure_match=True deterministic_export=True overwrite_refused=True', flush=True)
                if n == 3 and ordering == 'solver':
                    sample = path
        checked = run([args.dotnet, args.checker, 'mesh', '256', str(sample)])
        assert checked.returncode == 0 and 'status=positive_interval_pivots' in checked.stdout, checked.stdout + checked.stderr
        assert 'position_certificate=not_checked' in checked.stdout
        assert 'input_sha256=' + hashlib.sha256(sample.read_bytes()).hexdigest() in checked.stdout
        data = json.loads(sample.read_text())
        # 2^60 exceeds this small matrix's diagonal: the shifted matrix is not SPD.
        data['shift_bits'] = 0x43b0000000000000
        bad = Path(tmp) / 'bad-shift.json'; bad.write_text(json.dumps(data))
        rejected = run([args.dotnet, args.checker, 'mesh', '256', str(bad)])
        assert rejected.returncode != 0 and 'status=uncertain' in rejected.stdout
        data['order'][1] = data['order'][0]
        bad.write_text(json.dumps(data))
        rejected = run([args.dotnet, args.checker, 'mesh', '256', str(bad)])
        assert rejected.returncode != 0 and 'permutation' in rejected.stderr
        print('independent_matrix_control=positive bad_shift=rejected duplicate_order=rejected position_certificate=not_checked', flush=True)


if __name__ == '__main__':
    main()
