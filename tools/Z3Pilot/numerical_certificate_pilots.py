"""Five local obligations for source-coupled geometric certificates."""
import math
from fractions import Fraction

import z3 as z

from check_pilot_obligations import collect as results, source as source_contract

U = Fraction(1, 2**53)
TINY = Fraction(1, 2**1074)


def q(value):
    return z.RealVal(str(Fraction(value)))


def absolute(value):
    return z.If(value >= 0, value, -value)


def rounded(actual, exact):
    return absolute(actual - exact) <= q(U) * absolute(exact)


def volume(timeout_ms):
    """Compare the exact magnitude interval to the exact value 3*t."""
    path = 'math/ExactPredicates.cs'
    source_contract(path, 'internal static bool VolumeCompareFilter(', 'internal static bool TryCompareAbsVolume6(', '552861d4c68ff8d34fc266145b2a66b547f5a53f0cc31906be029ae6bd26c332')
    source_contract(path, 'internal const double Epsilon =', 'internal const double', '72a9738f343422cfb967703b3d8ce2d3a7592dc8daa37ec53abf869524a4f4ad')
    a, error, mag, threshold = z.Reals('a error mag threshold')
    lower, upper, t3 = z.Reals('lower upper t3')
    lo_in, hi_in, t_in, t_out = z.Reals('lo_in hi_in t_in t_out')
    # Round-to-nearest is monotone. The exact endpoints and 3*t are
    # rounded by the same mode, including cancellation and ties.
    premises = [
        a >= 0, error >= 0, mag >= 0, threshold > 0, t3 > 0,
        a - error <= mag, mag <= a + error,
        z.Implies(a - error <= threshold, lower <= t3),
        z.Implies(a + error >= threshold, upper >= t3),
        # The products' direction follows from monotonicity and 0 < 1-4u <= 1 <= 1+4u.
        z.Implies(lower >= 0, lo_in <= lower), z.Implies(lower < 0, lo_in <= 0),
        hi_in >= upper, t_out >= t3, 0 <= t_in, t_in <= t3,
    ]
    # Reverse the in/out margin directions at an exactly equal value.
    inward = premises + [lo_in == lower, hi_in == upper, t_in == t3, t_out == t3]
    result = results([
        ('above_reachable', premises, lo_in > t_out, 'sat'),
        ('below_reachable', premises, hi_in < t_in, 'sat'),
        ('above_false_decision', premises, z.And(lo_in > t_out, mag <= threshold), 'unsat'),
        ('below_false_decision', premises, z.And(hi_in < t_in, mag >= threshold), 'unsat'),
        ('above_reversed_margin_mutant', inward,
         z.And(lower * q(1 + 4 * U) > t3 * q(1 - 4 * U), mag <= threshold), 'sat'),
        ('below_reversed_margin_mutant', inward,
         z.And(upper * q(1 - 4 * U) < t3 * q(1 + 4 * U), mag >= threshold), 'sat'),
    ], timeout_ms)
    # Concrete IEEE replays. At exact equality, reverse the margin directions.
    max_finite = float.fromhex('0x1.fffffffffffffp+1023')
    assert math.isinf(3 * max_finite)
    assert 3 * float(1 + 4 * U) > 3 * float(1 - 4 * U)
    # The exact comparison for this integer-valued sum is equality.
    result['mutant'] = dict(t=1, sum=3, error=0, expected=0, inward_sign=1)
    return result


def envelope(timeout_ms):
    """Check the normal plane offset delta and the one-axis search pad."""
    path = 'mesh_ops/PolyhedralEnvelope.cs'
    source_contract(path, 'const double U =', 'const double Lambda', '893071f6830be6f03b84aee4020e2305a5b1814a1551d48f1a57259eedd9e9c5')
    source_contract(path, 'bool search_box(', 'internal static double norm_inf', '06afe52fd4775d8ee41cd383d02d86fe04a1e00e8a71cbdfe38e89fbe9cca1fd')
    source_contract(path, 'delta = epsilon *', 'double r = Math.Max(length(v1 - v0)', '38682ee7dd9164df9579f3902961404b91302e5fee0a91a97eb22446177d274d')
    source_contract('math/AxisAlignedBox3d.cs', 'public void Expand(double fRadius)', 'public void Contract(double fRadius)', '636b036603bb6bdd25d2c4e4c8c9fef7578a89b07e7dcd61ff2e47307867d6a2')
    eps, product, delta = z.Reals('epsilon product delta')
    k = Fraction(0.57735026918962573)
    upper_factor = k * (1 + U)**2 * (1 - 4 * U)
    # Rational certificate that the upper factor is strictly below 1/sqrt(3).
    assert 3 * upper_factor**2 < 1
    margin_premises = [eps >= q(1e-280), rounded(product, q(k) * eps),
                       rounded(delta, product * q(1 - 4 * U))]
    endpoint, maximum, p1, p2, pad, edge = z.Reals('endpoint maximum p1 p2 pad edge')
    pad_premises = [
        eps >= q(1e-280), maximum >= absolute(endpoint),
        rounded(p1, eps * q(1 + 4 * U)),
        absolute(p2 - q(4 * U) * maximum) <= q(4 * U * U) * maximum + q(TINY / 2),
        rounded(pad, p1 + p2), rounded(edge, endpoint + pad),
    ]
    witness_eps = float.fromhex('0x1.0000000000004p+0')
    witness_mutant = witness_eps * float(k)
    result = results([
        ('delta_range_reachable', margin_premises, delta > 0, 'sat'),
        ('delta_exceeds_safe_factor', margin_premises, delta > q(upper_factor) * eps, 'unsat'),
        ('search_box_face_can_round_inward', pad_premises, edge < endpoint + eps, 'unsat'),
        ('search_box_extends_face', pad_premises, edge > endpoint, 'sat'),
        ('without_delta_contraction_mutant',
         [margin_premises[0], margin_premises[1], delta == product],
         delta > q(upper_factor) * eps, 'sat'),
        ('without_coordinate_pad_mutant', pad_premises[:2] + [pad_premises[2],
         p2 == 0] + pad_premises[4:], edge < endpoint + eps, 'sat'),
    ], timeout_ms)
    mutant_exceeds = 3 * Fraction(witness_mutant)**2 > Fraction(witness_eps)**2
    result['passed'] &= mutant_exceeds
    result['mutant'] = dict(epsilon=witness_eps.hex(), delta=witness_mutant.hex(),
                            exceeds_reference=mutant_exceeds)
    return result


def geodesic(timeout_ms):
    """Bound each coordinate of a disk, then check the short-edge gate."""
    path = 'mesh_ops/ExactMeshGeodesicDistance.cs'
    source_contract(path, 'static double Mid(', 'static double Ulp(', '2fcd1387f032d37be3aa8fef851b13997bb13303c08670005dba66ee95bdf30a')
    source_contract(path, 'static double Ulp(', 'static double RoundUpSum(', '2970171ad7e8b39d8602bfee63608dc53e71a396f1cbd52f193aa8ad22beb3c3')
    source_contract(path, 'internal static double DiskOfBox(', 'bool VertexBeats(', 'e3559478c27537288ddbfcaa21742cc0d66208d1dbb36f473026932b14c0eddf')
    source_contract(path, 'internal static bool TransformDisk(', 'double dx = wx / len', '7dad4abb6a48e77f0ade52efce8d41263cafe008ebfde9cefb99cf60253ef71a')
    source_contract('math/ExactIntervals.cs', 'const double Phi1', 'public static double Down(', '38fbc15657650f3801d65cc0042947dfd87983980e4a18194943e29dda09cd10')

    # Binary64 halves are exact except for at most half a subnormal step.
    # The center addition has error at most half an upward spacing of
    # its absolute result. Up on the computed half width encloses the
    # exact difference of these already rounded halves, by the successor
    # bridge documented in the README.
    assumptions = []
    distances, widths, spacings = [], [], []
    for label in ('x', 'y'):
        lo, hi, low_half, high_half, point, center, spacing, width = z.Reals(
            ' '.join(f'{label}_{name}' for name in ('lo', 'hi', 'l2', 'h2', 'p', 'c', 'ulp', 'width')))
        assumptions.extend([
            lo <= point, point <= hi,
            absolute(low_half - lo / 2) <= q(TINY / 2),
            absolute(high_half - hi / 2) <= q(TINY / 2),
            spacing >= q(TINY), absolute(center - low_half - high_half) <= spacing / 2,
            width >= high_half - low_half, width >= 0,
        ])
        distances.append(absolute(point - center))
        widths.append(width)
        spacings.append(spacing)
    pad, radius = z.Reals('disk_pad radius')
    assumptions += [
        pad >= q(2 * (1 - U)) * sum(spacings),
        radius >= sum(widths) + pad,
    ]
    length, scaled, p_error, q_error, first, last = z.Reals(
        'midpoint_length scaled_length p_error q_error first_subtraction lenLo')
    gate = [length >= scaled, p_error >= 0, q_error >= 0,
            # Monotonic rounding of a subtraction against representable q_error.
            z.Implies(scaled - p_error <= q_error, first <= q_error),
            z.Implies(first <= q_error, last <= 0)]
    result = results([
        ('disk_assumptions_reachable', assumptions, radius > 0, 'sat'),
        ('disk_point_outside_one_norm_bound', assumptions, sum(distances) > radius, 'unsat'),
        ('overlapping_endpoint_disks_may_pass', gate,
         z.And(length <= p_error + q_error, last > 0), 'unsat'),
        ('separated_endpoint_disks_can_pass', gate, last > 0, 'sat'),
        ('remove_midpoint_padding_mutant', assumptions[:-2] +
         [pad == 0, radius >= sum(widths)], sum(distances) > radius, 'sat'),
        ('omit_q_error_mutant', gate[:-2] + [first == scaled - p_error, last == first],
         z.And(length <= p_error + q_error, last > 0), 'sat'),
    ], timeout_ms)
    # This independent replay uses two consecutive doubles. Omitting
    # midpoint padding leaves a radius near half their separation.
    lo, hi = 1.0, math.nextafter(1.0, math.inf)
    center = 0.5 * lo + 0.5 * hi
    up = lambda value: math.nextafter(value, math.inf)
    half_width = up(0.5 * hi - 0.5 * lo)
    mutant_radius = up(up(half_width + up(0.0)))
    gap = Fraction(hi) - Fraction(center)
    replayed = gap > Fraction(mutant_radius)
    result['passed'] &= replayed
    result['mutant'] = dict(lo=lo.hex(), hi=hi.hex(), center=center.hex(),
                            radius=mutant_radius.hex(), excludes_endpoint=replayed)
    # The guard lemma does require its length premise. With two
    # components of two binary64 subnormal steps, Hypot normalizes
    # both to one. Rescaling sqrt(2) to the original grid rounds up.
    eps = math.ulp(0.0)
    hyp = math.ldexp(math.sqrt(2), -1073)
    adjusted = hyp * (1 - 4e-16)
    overestimates = Fraction(adjusted)**2 > 2 * Fraction(2 * eps)**2
    result['passed'] &= overestimates
    result['short_length_replay'] = dict(dx_steps=2, dy_steps=2,
                                         adjusted_steps=adjusted / eps,
                                         overestimates=overestimates)
    return result


def double_word(timeout_ms):
    """Check the denominator and error inflation assuming a certified residual."""
    path = 'math/ExactDoubleWord.cs'
    source_contract(path, 'const double ErrFloor', 'public DoubleWord(double hi', '7172e68d018291e66d1cf08f0b76cb34de8dff69f40ac5e329f1db3068998b97')
    source_contract(path, 'public static DoubleWord Div(', 'public bool RoundsTo(', '48a1bce22ab78f636cdff8be7dad61c6779e90e3d895cefe5eb61c950ae8b80a')
    source_contract(path, 'public double Mag', 'static void TwoSum(', '7779f0e5811e188630b500b605745a79d75574e0971f37f813ce18c565d56c4c')
    head, tail, error, exact_divisor, first, den = z.Reals('head tail error divisor first den')
    exact_den = head - tail - error
    premises = [
        head > 0, 0 <= tail, tail <= q(U) * head, 0 <= error, error <= head / 4,
        absolute(exact_divisor) >= exact_den,
        rounded(first, head - tail), rounded(den, first - error),
    ]
    # Each normal numerator/quotient operation and the final inflation
    # multiplication can round down by 1-u (four operations).
    inflation = Fraction(1 + 2**-48)
    numerator, mag_down, sum_down, quot_down, safe_error = z.Reals(
        'residual_bound mag_down sum_down quot_down certified_quotient_error')
    # Normalize by the positive true divisor magnitude. The actual
    # remaining error before the quotient rounding is at most numerator.
    ratio = [
        numerator >= 0,
        mag_down >= q(1 - U) * numerator,
        sum_down >= q(1 - U) * mag_down,
        quot_down >= q((1 - U) / (1 + 4 * U)) * sum_down,
        safe_error >= q(inflation * (1 - U)) * quot_down,
    ]
    pre_rounding_quotient = z.Real('pre_rounding_quotient')
    min_normal = q(Fraction(1, 2**1022))
    # A rounded quotient below MinNormal has a pre-rounding value below
    # MinNormal by rounding monotonicity against the representable threshold.
    # Multiplication by ErrUp cannot turn a normal quotient subnormal.
    floor_case = [
        numerator >= 0,
        pre_rounding_quotient >= q((1 - U)**2 / (1 + 4 * U)) * numerator,
        pre_rounding_quotient < min_normal,
    ]
    one_third = Fraction(1, 3)
    weakened = Fraction(1.0 / 3.0)
    result = results([
        ('accepted_denominator_reachable', premises, den > 0, 'sat'),
        ('accepted_denominator_below_floor', premises, exact_den < q(Fraction(7, 10)) * head, 'unsat'),
        ('without_error_guard_mutant',
         [head > 0, 0 <= tail, tail <= q(U) * head, error >= 0] + premises[-3:],
         z.And(den > 0, exact_den < q(Fraction(7, 10)) * head), 'sat'),
        ('denominator_rounds_up_too_far', premises, den > q(1 + 4 * U) * exact_den, 'unsat'),
        ('omit_denominator_error_mutant', premises[:-1] + [rounded(den, first)],
         den > q(1 + 4 * U) * exact_den, 'sat'),
        ('inflation_premises_reachable', ratio, numerator > 0, 'sat'),
        ('normal_error_inflation_too_small', ratio, safe_error < numerator, 'unsat'),
        ('omit_final_inflation_mutant', ratio[:-1] + [safe_error == quot_down],
         safe_error < numerator, 'sat'),
        ('subnormal_quotient_reachable', floor_case, numerator > 0, 'sat'),
        ('floor_fails_for_subnormal_quotient', floor_case,
         numerator > q(Fraction(1, 2**1000)), 'unsat'),
        ('min_normal_floor_mutant', floor_case, numerator > min_normal, 'sat'),
    ], timeout_ms)
    assert Fraction(1, 2**960) * (1 - U)**3 / (1 + 4 * U) > Fraction(1, 2**1022)
    replayed = weakened < one_third
    result['passed'] &= replayed
    result['mutant'] = dict(numerator=1, denominator=3, rounded_without_inflation=float(weakened).hex(),
                            below_exact=replayed)
    return result


def contour_bins(timeout_ms):
    """Check local bin decisions under a monotone, bracketed coordinate map."""
    path = 'mesh_ops/ContouringTopologyCertificate.cs'
    source_contract(path, 'int first_bin(', 'int estimate_bin(', '653a8cecf55afd63bc2900d749f9a607177c3b15078c157ea5ba287d72748a4a')
    source_contract(path, 'int estimate_bin(', 'static int fdiv(', 'ac57eab9290cef27280ddca3db68ef1f65165dd3ab01d49b41333697e82fa375')
    source_contract('mesh_generators/DualContouring.cs', 'public override double Coord(int axis, int i)',
                    'public override int BinSize', '09802629bb39fb2664a4e3b03c4fc2ee5a73d7723128fc9c08fb7671bbe3b098')
    start, target = z.Ints('start target')
    edge, next_edge, target_edge, target_next, coordinate = z.Reals(
        'edge next_edge target_edge target_next coordinate')
    minimum, maximum = -(2**20), 2**20 - 1
    valid = [start >= minimum, start <= maximum, target >= minimum, target <= maximum]
    rows = []
    for name, left, right in (
        ('first', edge >= coordinate, next_edge < coordinate),
        ('last', edge > coordinate, next_edge <= coordinate),
    ):
        # Link all four coordinate samples by monotone order, including
        # equality when two symbolic indices name the same grid node.
        samples = ((start, edge), (start + 1, next_edge),
                   (target, target_edge), (target + 1, target_next))
        monotone = [z.Implies(i <= j, value <= other)
                    for i, value in samples for j, other in samples if value is not other]
        bracket = ([target_edge < coordinate, coordinate <= target_next] if name == 'first'
                   else [target_edge <= coordinate, coordinate < target_next])
        contract = monotone + bracket
        premises = valid + contract
        rows.extend([
            (f'{name}_target_reachable_at_boundary', premises,
             z.And(start == target, edge == coordinate if name == 'last' else next_edge == coordinate), 'sat'),
            (f'{name}_source_guard_disagrees_with_bracket', premises,
             z.Or(left != (start > target), right != (start < target)), 'unsat'),
            (f'{name}_change_equality_mutant', premises,
             z.Or(edge > coordinate if name == 'first' else edge >= coordinate,
                  next_edge <= coordinate if name == 'first' else next_edge < coordinate)
             != (start != target), 'sat'),
        ])
    # With both estimate and destination in the allowed bin domain,
    # m+1 stays in the checked interval. Fix B at the assumed upper
    # bound for the multiplication proof and use B>0 for monotonicity.
    rows += [
        ('bounded_index_reachable', valid, start == maximum, 'sat'),
        ('double_bin_size_mutant', valid, 2048 * (start + 1) > 2**31 - 1, 'sat'),
        ('bounded_index_product_can_overflow', valid,
         z.Or(1024 * start < -(2**31), 1024 * (start + 1) > 2**31 - 1), 'unsat'),
    ]
    result = results(rows, timeout_ms)
    estimate, factor = 600_000_000, 4
    # These values are model replays. Whether the estimate is reachable
    # through a concrete IGrid is a separate question.
    fine = estimate * factor
    assert estimate < 10**9 and fine > 2**31 - 1
    edge_at_boundary, next_at_boundary, query = 0, 1, 1
    assert edge_at_boundary < query and next_at_boundary == query
    assert not next_at_boundary < query and next_at_boundary <= query
    wrapped = (fine + 2**31) % 2**32 - 2**31
    result['overflow_example'] = dict(estimate=estimate, bin_size=factor,
                                      exact_fine_index=fine, unchecked_int=wrapped)
    return result


PILOTS = dict(volume=volume, envelope=envelope, geodesic=geodesic,
              double_word=double_word, contour_bins=contour_bins)
