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
from fractions import Fraction

from check_pilot_obligations import ROOT, main, source as pin, verify

SOURCE = 'intersection/ContinuousCollision.cs'
U = Fraction(1, 2**53)
ETA = Fraction(1, 2**1075)
SPANS = [
    ('internal const double FilterRelative', 'public static readonly double InputRange',
     '81253cd3d1ad48e6cd62912f4322a1e825e3815b36e07c8bbee2ee01406a0086'),
    ('void prepare_filter()', 'bool root_discarded()',
     '568666e7f8194be5c66dd60464953b08a743762e62ee75b491ab0202914e0edf'),
    ('double threshold = thresholds[axis];', 'void clip(ref Box b)',
     '4decdbdf207e777245a58f41ce6f3b06098bbebb7b0ee02b2141914c5716df8c'),
    ('void corners_filter(int axis, ref Box b)', 'internal double[] FilterCornersForTests',
     '446b0afd1a66372c6e74c987f72e157c412e3c77b30149fea03f55f229285bd4'),
    ('if (lo > threshold || hi < -threshold)', 'double w = hi - lo;',
     'ceb0dc3acd65b636e1d83b05157accff24061c5708a36d422e754b2654a7bf9f'),
]


def source_contract(root=ROOT):
    for span in SPANS:
        pin(SOURCE, *span, root=root)


def rational(value):
    value = Fraction(value)
    return f'(/ {value.numerator} {value.denominator})' if value >= 0 else f'(- {rational(-value)})'


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
    source_contract()
    shift, spacings = 47, 16
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
    model = '\n'.join(f'(declare-const {name} Real)' for name in names)
    bound_violation = '(or (> delta e) (< delta (- e)))'
    above = '(and (> computed threshold) (<= exact ms))'
    below = '(and (< computed (- threshold)) (>= exact (- ms)))'

    def with_premises(premises, goal):
        return '(and ' + ' '.join((*premises, goal)) + ')'

    weakened = [x for x in interval if x != f'(>= e {lower})'] + ['(= e 0)', '(= M 0)']
    cull_without_error = [x for x in cull if x != '(>= threshold (+ ms e))'] + ['(= threshold ms)']
    cases = [
        ('corner_error_reachable', interval, '(distinct delta 0)', 'sat'),
        ('corner_error', interval, bound_violation, 'unsat'),
        ('no_absolute_term', weakened, bound_violation, 'sat'),
        ('above_reachable', interval + cull, '(> computed threshold)', 'sat'),
        ('below_reachable', interval + cull, '(< computed (- threshold))', 'sat'),
        ('discard_above', interval + cull, above, 'unsat'),
        ('discard_below', interval + cull, below, 'unsat'),
        ('discard_above_without_error', interval + cull_without_error, above, 'sat'),
        ('discard_below_without_error', interval + cull_without_error, below, 'sat'),
    ]
    result = verify('QF_LRA', model, [(name, with_premises(premises, goal), expected)
                                     for name, premises, goal, expected in cases], timeout_ms)
    assert abs(actual - exact) > 1
    return dict(result, filter_relative_shift=shift, filter_absolute_spacings=spacings,
                mutant_replay=dict(computed=str(actual), exact=str(exact), bound='1', unit='2^-1074'))


if __name__ == '__main__':
    raise SystemExit(main(run))
