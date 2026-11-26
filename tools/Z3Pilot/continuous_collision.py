"""One-axis B1 and coordinate culling checks for ContinuousCollision (CCD.md §10.2).

Assume binary64 round-to-nearest without FMA and finite intermediates on
the filter path. For + and - the rounding error is at most u times the
unrounded magnitude (subnormal sums/differences are exact). For * add
half a subnormal spacing. Exact t, u_param and v_param are in [0, 1],
also at the clipped face's vertices. The exact interpolated coordinates
are convex combinations, bounded by M. Taking absolute errors loses
correlations and covers both signs in the edge-edge and vertex-triangle
expressions. This pilot checks the axis discard. Extending corner
separation to the search box uses the affine-in-space, linear-in-time
convex hull argument in CCD.md. Normal-direction culling is separate.
"""
import argparse
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import re

import z3

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('intersection/ContinuousCollision.cs')
U = Fraction(1, 2**53)
ETA = Fraction(1, 2**1075)
# Whitespace and line comments are ignored. A change to rounding order,
# range gating, corner selection or the comparisons requires review.
METHODS = {
    'void prepare_filter()': 'e2a6097338ed3f6fa9f5a2cce62b80bc3afd69bef9b0a5a6a2de37ee67b60a37',
    'void corners_filter(int axis, ref Box b)': '446b0afd1a66372c6e74c987f72e157c412e3c77b30149fea03f55f229285bd4',
    'void clip(ref Box b)': 'c2c8e4e5304cc6006bad5048691bb41911acb3463c575e1a0e601151a0315e98',
    'bool evaluate(ref Box b, out double width)': 'dba4e27af6bdc9a48ed561b42afbc3011b844cf28a2e218cbe110e92d28452d6',
}


def source_contract(root=ROOT):
    text = (root / SOURCE).read_text(encoding='utf-8')
    code = re.sub(r'\s+|//[^\n]*', '', text)
    for signature, expected in METHODS.items():
        start = code.index(re.sub(r'\s+', '', signature) + '{')
        end = code.index('{', start)
        depth = 1
        while depth:
            end += 1
            depth += (code[end] == '{') - (code[end] == '}')
        actual = hashlib.sha256(code[start:end + 1].encode()).hexdigest()
        if actual != expected:
            raise ValueError(f'{SOURCE}: review the Z3 model after changes to {signature}')
    match = re.search(
        r'internalconstdoubleFilterRelative=1\.0/\(1L<<(\d+)\);'
        r'internalconstdoubleFilterAbsolute=(\d+)\*double\.Epsilon;', code)
    if match is None or 'FilterRange=BitConverter.Int64BitsToDouble((1023L+500)<<52);' not in code:
        raise ValueError(f'{SOURCE}: filter constants or range changed shape')
    return tuple(map(int, match.groups()))


def rational(value):
    value = Fraction(value)
    return f'(/ {value.numerator} {value.denominator})' if value >= 0 else f'(- {rational(-value)})'


def query(assumptions, violation, names, timeout_ms):
    declarations = [f'(declare-const {name} Real)' for name in names]
    assertions = [f'(assert {formula})' for formula in (*assumptions, violation)]
    solver = z3.SolverFor('QF_LRA')
    solver.set(timeout=timeout_ms)
    solver.from_string('\n'.join(declarations + assertions))
    return str(solver.check())


def corner_error():
    """Generate a linear bound in M. Each pair is (coefficient of M, constant)."""
    def rounded(exact_magnitude, *inputs, multiply=False):
        relative = sum(error[0] for error in inputs)
        absolute = sum(error[1] for error in inputs)
        return ((1 + U) * relative + U * exact_magnitude,
                (1 + U) * absolute + (ETA if multiply else 0))

    difference = rounded(2)
    displacement = rounded(2, difference, multiply=True)
    position = rounded(1, displacement)
    edge = rounded(2, position, position)
    weighted_edge = rounded(2, edge, multiply=True)
    partial = rounded(4, edge, weighted_edge)
    return rounded(6, partial, weighted_edge)


def replay_mutant():
    """Binary64 corner replay and independent rational reference, in subnormal units."""
    spacing = float.fromhex('0x0.0000000000001p-1022')
    starts = [-2, -2, -2, -1]
    ends = [-3, -3, -1, 0]

    def evaluate(start, end, half):
        x0, x1, x2, x3 = [a + half * (b - a) for a, b in zip(start, end)]
        return ((x0 - x2) + half * (x1 - x0)) - half * (x3 - x2)

    s = [n * spacing for n in starts]
    e = [n * spacing for n in ends]
    actual = Fraction(evaluate(s, e, 0.5)) / Fraction(spacing)
    exact = evaluate(starts, ends, Fraction(1, 2))
    # Flushing the absolute term from the formula leaves zero: the
    # relative product underflows. ExactInterval.Up(0) adds one spacing.
    if max(map(abs, s + e)) * 2**-47 != 0:
        raise AssertionError('mutant no longer has a zero relative term')
    return actual, exact


def run(timeout_ms=120000):
    shift, spacings = source_contract()
    relative, absolute = corner_error()
    bound = f'(+ (* {rational(relative)} M) {rational(absolute)})'
    # e = Up(fl(fl(M * 2^-shift) + spacings*epsilon)). The product
    # has one possible half-spacing loss from scaling by a power of two.
    # Up after correctly rounding the sum is at least the unrounded sum.
    lower = f'(+ (* {rational(Fraction(1, 2**shift))} M) {rational((2 * spacings - 1) * ETA)})'
    interval = ['(>= M 0)', f'(<= M {2**500})',
                f'(<= (- {bound}) delta)', f'(<= delta {bound})',
                f'(>= e {lower})', '(>= e 0)']
    cull = ['(>= ms 0)', '(>= threshold (+ ms e))']
    names = ['M', 'delta', 'e', 'ms', 'threshold', 'exact', 'computed']
    cull += ['(= computed (+ exact delta))']
    actual, exact = replay_mutant()
    witness = [f'(= computed {rational(actual)})', f'(= exact {rational(exact)})']
    results = {
        'corner_error': query(interval, '(or (> delta e) (< delta (- e)))', names, timeout_ms),
        'above_reachable': query(interval + cull, '(> computed threshold)', names, timeout_ms),
        'below_reachable': query(interval + cull, '(< computed (- threshold))', names, timeout_ms),
        'discard_above': query(interval + cull, '(and (> computed threshold) (<= exact ms))', names, timeout_ms),
        'discard_below': query(interval + cull, ' (and (< computed (- threshold)) (>= exact (- ms)))', names, timeout_ms),
        'no_absolute_term': query(witness, '(or (> (- computed exact) 1) (> (- exact computed) 1))', names, timeout_ms),
    }
    passed = results == {
        'corner_error': 'unsat', 'above_reachable': 'sat', 'below_reachable': 'sat',
        'discard_above': 'unsat', 'discard_below': 'unsat', 'no_absolute_term': 'sat',
    }
    return dict(z3=z3.get_version_string(), passed=passed, results=results,
                filter_relative_shift=shift, filter_absolute_spacings=spacings,
                mutant=dict(computed=str(actual), exact=str(exact), bound='1', unit='2^-1074'))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path)
    parser.add_argument('--timeout-ms', type=int, default=120000)
    args = parser.parse_args()
    report = run(args.timeout_ms)
    output = json.dumps(report, indent=2) + '\n'
    if args.out:
        args.out.write_text(output, encoding='utf-8')
    else:
        print(output, end='')
    raise SystemExit(0 if report['passed'] else 1)
