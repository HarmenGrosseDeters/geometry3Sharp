"""Exact audit of small .NET scalar traces; not a general runtime/mesh proof."""
import argparse
from fractions import Fraction as F
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
from certificate_probe import scalar_cholesky_probe as reference


def value(raw):
    return struct.unpack('>d', struct.pack('>q', raw))[0]


def replay(bounds, records):
    """Dense symbolic elimination and scalar recurrence, independent of C# lists.

    Bounds validate the initial matrix separately. Require the entire trace,
    including its linkage and exact operand/result bits, to match this replay.
    """
    n = bounds['size']
    pattern = [[False]*n for _ in range(n)]
    matrix = [[0.0]*n for _ in range(n)]
    for entry in bounds['entries']:
        i, j = entry['row'], entry['column']
        pattern[i][j] = pattern[j][i] = True
        matrix[i][j] = matrix[j][i] = value(entry['midpoint'])
    for i, raw in enumerate(bounds['adjusted']):
        matrix[i][i] = value(raw)
    factor = [[0.0]*n for _ in range(n)]
    cursor = 0

    def operation(name, a, b=0.0):
        nonlocal cursor
        function = dict(mul=reference.product, sub=reference.difference, div=reference.quotient, sqrt=reference.root)[name]
        result = function(a) if name == 'sqrt' else function(a, b)
        assert cursor < len(records), 'truncated trace'
        record = records[cursor]; cursor += 1
        assert record['operation'] == name, 'operation sequence'
        for key, expected in (('a', a), ('b', b), ('result', result)):
            assert reference.bits(value(record[key])) == reference.bits(expected), 'trace linkage: ' + key
        return result

    previous_audit = reference.AUDIT
    reference.AUDIT = True
    try:
        for j in range(n):
            neighbors = [i for i in range(j+1, n) if pattern[i][j]]
            for a in neighbors:
                for b in neighbors:
                    pattern[a][b] = True
            diagonal = matrix[j][j]
            for k in range(j):
                if pattern[j][k]:
                    diagonal = operation('sub', diagonal, operation('mul', factor[j][k], factor[j][k]))
            assert diagonal > 0
            pivot = operation('sqrt', diagonal)
            for i in neighbors:
                result = matrix[i][j]
                for k in range(j):
                    if pattern[i][k] and pattern[j][k]:
                        result = operation('sub', result, operation('mul', factor[i][k], factor[j][k]))
                factor[i][j] = operation('div', result, pivot)
        assert cursor == len(records), 'extra trace operations'
    finally:
        reference.AUDIT = previous_audit


def audit(records):
    bounds = records[0]
    n, precision = bounds['size'], bounds['bits']
    unit = F(1, 2**1074)
    radius = [F(0) for _ in range(n)]
    diagonal = {}
    entries = []
    for entry in bounds['entries']:
        row, column = entry['row'], entry['column']
        lo, hi = F(int(entry['lo']), 2**precision), F(int(entry['hi']), 2**precision)
        assert lo <= hi
        midpoint = F(value(entry['midpoint']))
        uncertainty = max(abs(midpoint-lo), abs(hi-midpoint))
        radius[row] += uncertainty
        if row != column:
            radius[column] += uncertainty
        else:
            assert midpoint > 0 and row not in diagonal
            diagonal[row] = midpoint
        entries.append([row, column, str(lo), str(hi)])
    assert len(diagonal) == n
    exact_correction = F(n+1, 2**53 - 2*(n+1)) * sum(diagonal.values())
    correction = int(bounds['correction_units']) * unit
    if bounds.get('envelope_bound'):
        widths = [0]*n
        for entry in bounds['entries']:
            row,column = entry['row'],entry['column']
            widths[row] = max(widths[row],row-column)
        assert widths == bounds['envelope']
        ceil_units = lambda term: (term.numerator*2**1074+term.denominator-1)//term.denominator
        envelope = sum(ceil_units(diagonal[i]*F(widths[i]+2,2**53-2*(widths[i]+2))) for i in range(n))
        assert correction == min(ceil_units(exact_correction),envelope)*unit
    else:
        assert exact_correction <= correction < exact_correction + unit
    rho = int(bounds['radius_units']) * unit
    assert rho == max(radius)
    for i, raw in enumerate(bounds['adjusted']):
        adjusted = value(raw)
        exact = diagonal[i] - correction - rho
        assert F(adjusted) <= exact < F(math.nextafter(adjusted, math.inf))
    counts = dict(mul=0, sub=0, div=0, sqrt=0)
    for record in records[1:]:
        operation = record['operation']; counts[operation] += 1
        a, b, result = (value(record[key]) for key in ('a', 'b', 'result'))
        for operand in (a, b, result):
            reference.checked(operand)
        if operation == 'sqrt':
            reference.audit_round(result, F(a), squared=True)
            continue
        exact = F(a)*F(b) if operation == 'mul' else F(a)-F(b) if operation == 'sub' else F(a)/F(b)
        reference.audit_round(result, exact)
        assert exact == 0 or abs(exact) >= F(1, 2**1022), 'underflow'
    ok, detail = reference.verify(dict(size=n, entries=entries))
    assert ok, detail
    replay(bounds, records[1:])
    return counts


def run(command):
    return subprocess.run(command, capture_output=True, text=True, timeout=30)


def mutation(args):
    # Run alone in a dedicated worktree after a fresh baseline build. A hard
    # process kill bypasses finally; do not interrupt it or build concurrently.
    root = Path(__file__).resolve().parents[2]
    source = root / 'benchmarks/BiharmonicCertificateArithmetic/ScalarCholesky.cs'
    checker = Path(args.checker).resolve()
    expected = Path(args.mutation_artifacts).resolve() / 'bin/BiharmonicCertificateArithmetic/release/BiharmonicCertificateArithmetic.dll'
    if checker != expected:
        raise ValueError('checker must be the release output under mutation-artifacts; refusing to mutate')
    backups = {path: path.read_bytes() for path in (source, checker, checker.with_suffix('.pdb'))}
    original = backups[source].decode()
    old = 'if (!remainder.IsZero) correction++;'
    assert original.count(old) == 1
    command = [sys.executable, str(Path(__file__).resolve()), args.dotnet, args.producer, args.checker]
    baseline = run(command)
    assert baseline.returncode == 0, baseline.stdout + baseline.stderr
    print('scalar_mutation_baseline=green', flush=True)
    try:
        source.write_text(original.replace(old, 'correction = BigInteger.Zero;'))
        built = subprocess.run([args.dotnet, 'build', 'benchmarks/BiharmonicCertificateArithmetic', '-c', 'Release',
                                '--artifacts-path', args.mutation_artifacts, '--no-restore', '-m:1',
                                '-p:UseSharedCompilation=false'], cwd=root, capture_output=True, text=True, timeout=60)
        assert built.returncode == 0, built.stdout + built.stderr
        tested = run(command)
        assert tested.returncode != 0 and 'exact_correction <= correction' in tested.stderr, tested.stdout + tested.stderr
        print('scalar_mutation=omit_trace_correction killed_by=exact_rational_bound_check', flush=True)
    finally:
        for path, data in backups.items():
            path.write_bytes(data)
        print('scalar_source_and_artifacts=restored', flush=True)
    restored = run(command)
    assert restored.returncode == 0, restored.stdout + restored.stderr
    print('scalar_mutation_restored_controls=green', flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('dotnet'); parser.add_argument('producer'); parser.add_argument('checker')
    parser.add_argument('--envelope',action='store_true',help='audit the envelope-aware criterion')
    parser.add_argument('--mutation-artifacts', help='temporarily remove the trace correction; use a dedicated worktree and fresh artifact build')
    args = parser.parse_args()
    if args.envelope and args.mutation_artifacts:
        parser.error('--mutation-artifacts supports the global control only; run envelope audits separately')
    if args.mutation_artifacts:
        mutation(args)
        return
    for name, path in [('producer', Path(args.producer)), ('checker', Path(args.checker))]:
        print(name + '_sha256=' + hashlib.sha256(path.read_bytes()).hexdigest(), flush=True)
    tested = run([args.dotnet, args.checker, 'scalar-selftest'])
    assert tested.returncode == 0, tested.stdout + tested.stderr
    print(tested.stdout, end='', flush=True)
    with tempfile.TemporaryDirectory(prefix='bih-scalar-controls-') as tmp:
        for n in (3, 5, 7):
            path = Path(tmp) / f'{n}.json'; trace = Path(tmp) / f'{n}.jsonl'
            produced = run([args.dotnet, args.producer, '--certificate-structure', str(n), 'solver', '0.5', 'rotated', str(path)])
            assert produced.returncode == 0, produced.stdout + produced.stderr
            checked = run([args.dotnet, args.checker, 'mesh-envelope' if args.envelope else 'mesh-scalar', '256', str(path), str(trace)])
            assert checked.returncode == 0 and 'status=positive_scalar_pivots' in checked.stdout, checked.stdout + checked.stderr
            assert 'position_certificate=not_checked' in checked.stdout and 'position_certificate=within_tolerance' not in checked.stdout
            records = [json.loads(line) for line in trace.read_text().splitlines()]
            assert records[0]['input_sha256'] == hashlib.sha256(path.read_bytes()).hexdigest()
            counts = audit(records)
            print(f'grid={n} exact_trace_audit=passed operations={sum(counts.values())} counts={counts} independent_python_matrix_check=True', flush=True)
            # Audit negative controls: a wrong rounding and each missing bound.
            for kind in ('product', 'correction', 'radius', 'truncated', 'linkage'):
                corrupted = json.loads(json.dumps(records))
                if kind == 'product':
                    operation = next(item for item in corrupted[1:] if item['operation'] == 'mul')
                    wrong = math.nextafter(value(operation['result']), math.inf)
                    operation['result'] = struct.unpack('>q', struct.pack('>d', wrong))[0]
                elif kind in ('correction', 'radius'):
                    corrupted[0][kind+'_units'] = '0'
                elif kind == 'truncated':
                    corrupted = corrupted[:1]
                else:
                    operation = next(item for item in corrupted[1:] if item['operation'] == 'mul')
                    operation.update(a=0, b=0, result=0)  # Correct arithmetic, wrong data flow.
                rejected = False
                try:
                    audit(corrupted)
                except (AssertionError, ArithmeticError):
                    rejected = True
                assert rejected, kind + ' audit mutant survived'
            data = json.loads(path.read_text()); data['shift_bits'] = 0x43b0000000000000
            bad = Path(tmp) / f'{n}-bad-shift.json'; bad.write_text(json.dumps(data))
            rejected = run([args.dotnet, args.checker, 'mesh-envelope' if args.envelope else 'mesh-scalar', '256', str(bad)])
            assert rejected.returncode != 0 and 'status=uncertain' in rejected.stdout
            print(f'grid={n} bad_shift=rejected audit_negative_controls=5 position_certificate=not_checked', flush=True)


if __name__ == '__main__':
    main()
