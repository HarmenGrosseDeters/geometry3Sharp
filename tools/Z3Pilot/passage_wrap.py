"""Local PassageWrap obligations and binary64 boundary replays.

Checks the barycentric ball identity and Claims 1-2 of docs/design/PASSAGE_WRAP.md,
the scaled rationalized MaxAlpha root equivalence, optimality and monotonicity,
the 1-Lipschitz PolylineClearance interval lower bound, and binary64 witnesses
for extreme-scale overflow/underflow, near-blocked cancellation, new MaxAlpha
and IntervalLowerBound replays, SafeLength subnormal squares, PolylineDistance
dot underflow, estimate division order, UTM margin, and adjacent-double stall.
"""
from fractions import Fraction
import math

import z3

from check_pilot_obligations import check_expr, main, report, source_contract

SOURCE = 'mesh_ops/PassageWrap.cs'
ANCHORS = [
    ('const double MinNormal', 'public PassageWrap(DMesh3 mesh)',
     '649e7fd55e5f5820e3da33698c85c2fec98c32e7097903bb2685790d910c4254'),
    ('public static double MaxAlpha(', 'static double SafeLength(',
     'f6fe581a312e3f76ad1041f5e907b365037463cc9466c57c5b098408851c03ea'),
    ('static double SafeLength(', 'internal static void PolylineClearance(',
     '4590fbddece271709362b724e4f8754d39f50c09eba5450de319435dddc99f50'),
    ('internal static void PolylineClearance(', 'public bool Compute()',
     'ca2aa88fb82a585bcb3d45fccfbc29b6fcf35abe48b0286d8a29b5e9abc02bc5'),
    ('double estimate(double alpha, bool openToo)', 'sealed class MinHeap',
     'c371f55870e4d480c6f461476193ae75c8ed8d3c576ef3e93a0018249f625eee'),
    ('static double PolylineDistance(', 'PassagePinResult[] measure(',
     '903566d5b938016c50471ba9a849f09292e58c75921af64375384fc343b1a14f'),
]
MATHUTIL_SOURCE = 'math/MathUtil.cs'
MATHUTIL_ANCHORS = [
    ('public const double Epsilon', 'public const double SqrtTwo',
     'e0a94399016ac04c8cc71377bdeee39bd927a810535542503faf6a6b60578d9c'),
]


def barycentric_and_clearance_obligations(timeout_ms):
    # 1. Per-axis barycentric variance identity (*): summing over x, y, z gives
    #    sum l_i |p_i - m|^2 = |q - m|^2 + sum l_i |p_i - q|^2 for any m.
    p1, p2, p3, m, l1, l2 = z3.Reals('p1 p2 p3 m l1 l2')
    l3 = 1 - l1 - l2
    q = l1 * p1 + l2 * p2 + l3 * p3
    lhs = l1 * (p1 - m) ** 2 + l2 * (p2 - m) ** 2 + l3 * (p3 - m) ** 2
    rhs = (q - m) ** 2 + l1 * (p1 - q) ** 2 + l2 * (p2 - q) ** 2 + l3 * (p3 - q) ** 2
    bary_premises = [l1 >= 0, l2 >= 0, l3 >= 0]

    # 2. Claims 1 & 2 from (*).
    r, d, a, R, d_qm2, s_pq, s_py, d_yq2 = z3.Reals('r d a R d_qm2 s_pq s_py d_yq2')
    claim1_premises = [
        r > 0, d >= 0, a > 0, R >= 0, d_qm2 >= 0,
        r - d > a,
        s_pq >= (r - d) * (r - d),
        R * R == d_qm2 + s_pq,
    ]
    claim2_premises = [
        r > 0, d >= 0, a > 0, R >= 0,
        r - d >= 0,
        R <= a,
        s_pq >= (r - d) * (r - d),
        s_py <= R * R,
        d_yq2 == s_pq - s_py,
    ]

    return [
        check_expr('barycentric_identity_premises_reachable', bary_premises,
                   z3.And(l1 > 0, l2 > 0, l3 > 0, p1 != p2), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('barycentric_variance_identity_can_fail', bary_premises,
                   lhs != rhs, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('claim1_premises_reachable', claim1_premises,
                   R > a, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('claim1_interface_facet_can_contain_axis_point', claim1_premises,
                   R <= a, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('claim2_premises_reachable', claim2_premises,
                   d_yq2 >= (r - d) * (r - d) - a * a, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('claim2_clearance_can_violate_lower_bound', claim2_premises,
                   d_yq2 < (r - d) * (r - d) - a * a, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('claim2_without_empty_ball_cap_can_violate_bound',
                   [r > 0, d >= 0, a > 0, R > a, r - d >= 0, s_pq >= (r - d) * (r - d),
                    s_py <= R * R, d_yq2 == s_pq - s_py],
                   d_yq2 < (r - d) * (r - d) - a * a, 'sat', timeout_ms, 'QF_NRA'),
    ]


def max_alpha_obligations(timeout_ms):
    # Scale-invariant form with u = p / r in [0, 1) and alpha_hat = a / r:
    # avoids nested divisions so QF_NRA solves in milliseconds.
    r, u, k, s, a, a2 = z3.Reals('r u k s a a2')
    p = r * u
    diff_sq = (1 - u) * (1 + u)
    disc = k * k + (1 - k) * (1 + k) * diff_sq
    root_premises = [
        r > 0, u >= 0, u < 1, k > 0, k < 1,
        s > 0, s * s == disc,
        a * (k + s) == r * diff_sq,
    ]
    larger_premises = root_premises + [a2 > a, r - k * a2 >= 0]
    smaller_premises = root_premises + [a2 > 0, a2 < a, r - k * a2 >= 0]
    blocked_premises = [r > 0, u >= 1, k > 0, k < 1, a > 0, r - k * a >= 0]

    # Normalized r = 1 monotonicity in u = p / r (since a scales linearly with r):
    u2, s2, h1, h2 = z3.Reals('u2 s2 h1 h2')
    diff_sq2 = (1 - u2) * (1 + u2)
    mono_u_premises = [
        u >= 0, u < u2, u2 < 1, k > 0, k < 1,
        s > 0, s * s == disc,
        s2 > 0, s2 * s2 == k * k + (1 - k) * (1 + k) * diff_sq2,
        h1 * (k + s) == diff_sq,
        h2 * (k + s2) == diff_sq2,
    ]
    return [
        check_expr('max_alpha_premises_reachable', root_premises,
                   z3.And(a > 0, r - k * a > 0), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('max_alpha_fails_quadratic_root_identity', root_premises,
                   z3.Or(a <= 0, r - k * a <= 0, (r - k * a) ** 2 - a * a != p * p),
                   'unsat', timeout_ms, 'QF_NRA'),
        check_expr('larger_alpha_premises_reachable', larger_premises,
                   a2 > a, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('larger_alpha_can_still_satisfy_clearance_bound', larger_premises,
                   a2 * a2 + p * p <= (r - k * a2) ** 2, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('smaller_alpha_satisfies_clearance_bound', smaller_premises,
                   a2 * a2 + p * p < (r - k * a2) ** 2, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('blocked_pin_premises_reachable', blocked_premises,
                   a > 0, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('blocked_pin_can_have_positive_valid_alpha', blocked_premises,
                   a * a + p * p <= (r - k * a) ** 2, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('mono_u_premises_reachable', mono_u_premises,
                   h2 < h1, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('max_alpha_monotonicity_in_pin_radius_can_fail', mono_u_premises,
                   h2 >= h1, 'unsat', timeout_ms, 'QF_NRA'),
    ]


def lipschitz_interval_obligations(timeout_ms):
    fa, fb, ft, L, t = z3.Reals('fa fb ft L t')
    lip_premises = [
        L >= 0, 0 <= t, t <= L,
        fa >= 0, fb >= 0, ft >= 0,
        fa - ft <= t, ft - fa <= t,
        fb - ft <= L - t, ft - fb <= L - t,
    ]
    return [
        check_expr('lipschitz_interval_premises_reachable', lip_premises,
                   2 * ft >= fa + fb - L, 'sat', timeout_ms, 'QF_LRA'),
        check_expr('lipschitz_interval_lower_bound_can_exceed_true_value', lip_premises,
                   2 * ft < fa + fb - L, 'unsat', timeout_ms, 'QF_LRA'),
        check_expr('two_lipschitz_function_can_violate_one_lipschitz_lower_bound',
                   [L > 0, 0 <= t, t <= L, fa >= 0, fb >= 0, ft >= 0,
                    fa - ft <= 2 * t, fb - ft <= 2 * (L - t)],
                   2 * ft < fa + fb - L, 'sat', timeout_ms, 'QF_LRA'),
    ]


def _fp_max_alpha(f64, rnd, r_val, p_val, ratio_val):
    fp = lambda x: z3.FPVal(x, f64)
    r, p, ratio = fp(r_val), fp(p_val), fp(ratio_val)
    k = z3.fpDiv(rnd, fp(1.0), ratio)
    u = z3.fpDiv(rnd, p, r)
    one_minus_u = z3.fpDiv(rnd, z3.fpSub(rnd, r, p), r)
    one_plus_u = z3.fpAdd(rnd, fp(1.0), u)
    diff_sq = z3.fpMul(rnd, one_minus_u, one_plus_u)
    disc = z3.fpAdd(
        rnd,
        z3.fpMul(rnd, k, k),
        z3.fpMul(rnd, z3.fpMul(rnd, z3.fpSub(rnd, fp(1.0), k), z3.fpAdd(rnd, fp(1.0), k)), diff_sq),
    )
    return z3.fpMul(rnd, r, z3.fpDiv(rnd, diff_sq, z3.fpAdd(rnd, k, z3.fpSqrt(rnd, disc))))


def replay_fp():
    f64, rnd = z3.Float64(), z3.RNE()
    fp = lambda x: z3.FPVal(x, f64)
    same = lambda expr, value: z3.is_true(z3.simplify(z3.fpEQ(expr, fp(value))))
    is_nan = lambda expr: z3.is_true(z3.simplify(z3.fpIsNaN(expr)))
    is_pos_finite = lambda expr: z3.is_true(z3.simplify(
        z3.And(z3.fpGT(expr, fp(0.0)), z3.Not(z3.fpIsInf(expr)), z3.Not(z3.fpIsNaN(expr)))))

    # 1. Old MaxAlpha at r = 2^600, p = 2^599 overflows r^2 - p^2 to NaN; new rationalized form is exact.
    r_big, p_big = 2.0**600, 2.0**599
    old_diff_big = z3.fpSub(rnd, z3.fpMul(rnd, fp(r_big), fp(r_big)),
                            z3.fpMul(rnd, fp(p_big), fp(p_big)))
    new_big = _fp_max_alpha(f64, rnd, r_big, p_big, 30.0)
    ref_base = _fp_max_alpha(f64, rnd, 2.0, 1.0, 30.0)
    expected_big = z3.fpMul(rnd, ref_base, fp(2.0**599))

    # 2. Old MaxAlpha at r = 2^-600, p = 2^-601 underflows negative; new form scales bit-for-bit.
    r_tiny, p_tiny, k = 2.0**-600, 2.0**-601, 1.0 / 30.0
    r_tiny2 = z3.fpMul(rnd, fp(r_tiny), fp(r_tiny))
    p_tiny2 = z3.fpMul(rnd, fp(p_tiny), fp(p_tiny))
    old_num_tiny = z3.fpAdd(rnd, z3.fpMul(rnd, fp(-r_tiny), fp(k)), z3.fpSqrt(rnd, fp(0.0)))
    new_tiny = _fp_max_alpha(f64, rnd, r_tiny, p_tiny, 30.0)
    expected_tiny = z3.fpMul(rnd, ref_base, fp(2.0**-601))

    # 3. Near-blocked r = 3, p = 3 - 2^-50: new rationalized MaxAlpha stays positive and accurate (~30*gap).
    r_nb = 3.0
    gap = 2.0**-50
    p_nb = r_nb - gap
    sterbenz_gap = z3.fpSub(rnd, fp(r_nb), fp(p_nb))
    new_nb = _fp_max_alpha(f64, rnd, r_nb, p_nb, 30.0)
    approx_nb = 30.0 * gap
    nb_err = z3.fpAbs(z3.fpSub(rnd, new_nb, fp(approx_nb)))

    # 4. IntervalLowerBound avoids fa + fb overflow at 1e308 while staying <= (fa + fb - len) / 2.
    fa_big, fb_big, len_big = 1e308, 1e308, 1e307
    old_sum_big = z3.fpAdd(rnd, fp(fa_big), fp(fb_big))
    new_half_sum = z3.fpAdd(rnd, z3.fpMul(rnd, fp(0.5), fp(fa_big)), z3.fpMul(rnd, fp(0.5), fp(fb_big)))

    # 5. SafeLength subnormal component squaring: each square < 2^-1022 while sum >= 2^-1022.
    c_sub = 1.4142135623730951 * 2.0**-512
    c_sub2 = z3.fpMul(rnd, fp(c_sub), fp(c_sub))
    sum_3c2 = z3.fpAdd(rnd, z3.fpAdd(rnd, c_sub2, c_sub2), c_sub2)

    # 6. PolylineDistance dot underflow when len2 >= MinNormal (ab = 2^-510, qa = 2^-565 -> dot = 0.0).
    ab_x, qa_x = 2.0**-510, 2.0**-565
    ab_len2 = z3.fpMul(rnd, fp(ab_x), fp(ab_x))
    dot_under = z3.fpMul(rnd, fp(qa_x), fp(ab_x))

    # 7. estimate() division order: EstimateDensity * total overflows at 1e308 while (total / alpha) / alpha stays finite.
    tot_big, alpha_big, density = 1e308, 1e100, 12.0
    old_est_num = z3.fpMul(rnd, fp(density), fp(tot_big))
    new_est = z3.fpMul(rnd, fp(density), z3.fpDiv(rnd, z3.fpDiv(rnd, fp(tot_big), fp(alpha_big)), fp(alpha_big)))

    # 8. Adjacent doubles at 1e14: midpoint 0.5*a + 0.5*b collapses onto an endpoint.
    x0 = 1e14
    x1 = math.nextafter(x0, math.inf)
    mid = z3.fpAdd(rnd, z3.fpMul(rnd, fp(0.5), fp(x0)), z3.fpMul(rnd, fp(0.5), fp(x1)))

    # 9. UTM rounding: ulp(2^23) = 2^-29 >> 1e-12 * 1.0, while 64 * eps * 2^23 dominates ulp(2^23).
    utm = 2.0**23
    utm_ulp = Fraction(math.ulp(utm))
    old_margin = Fraction(1, 10**12)
    new_margin = Fraction(64) * Fraction(2.0**-52) * Fraction(utm)

    checks = {
        'old_max_alpha_overflows_to_nan_while_new_scales_bit_for_bit_at_2p600':
            is_nan(old_diff_big) and Fraction(p_big) / Fraction(r_big) == Fraction(1, 2)
            and is_pos_finite(new_big) and z3.is_true(z3.simplify(z3.fpEQ(new_big, expected_big))),
        'old_max_alpha_underflows_negative_while_new_scales_bit_for_bit_at_2m600':
            same(r_tiny2, 0.0) and same(p_tiny2, 0.0)
            and z3.is_true(z3.simplify(z3.fpLT(old_num_tiny, fp(0.0))))
            and is_pos_finite(new_tiny) and z3.is_true(z3.simplify(z3.fpEQ(new_tiny, expected_tiny))),
        'near_blocked_rationalized_max_alpha_avoids_cancellation':
            same(sterbenz_gap, gap) and Fraction(r_nb) - Fraction(p_nb) == Fraction(gap)
            and z3.is_true(z3.simplify(z3.fpLT(nb_err, fp(1e-9 * approx_nb)))),
        'interval_lower_bound_avoids_fa_plus_fb_overflow':
            same(old_sum_big, math.inf) and same(new_half_sum, 1e308),
        'safe_length_4x_min_normal_catches_subnormal_component_squares':
            z3.is_true(z3.simplify(z3.fpLT(c_sub2, fp(2.0**-1022))))
            and z3.is_true(z3.simplify(z3.fpGEQ(sum_3c2, fp(2.0**-1022))))
            and z3.is_true(z3.simplify(z3.fpLT(sum_3c2, fp(4.0 * 2.0**-1022)))),
        'polyline_distance_dot_underflows_even_when_len2_is_normal':
            same(ab_len2, 2.0**-1020) and same(dot_under, 0.0),
        'estimate_dividing_before_density_avoids_overflow':
            same(old_est_num, math.inf) and is_pos_finite(new_est),
        'adjacent_double_midpoint_stalls_on_endpoint':
            z3.is_true(z3.simplify(z3.Or(z3.fpEQ(mid, fp(x0)), z3.fpEQ(mid, fp(x1))))) and x1 > x0,
        'utm_coordinate_ulp_exceeds_old_diag_margin_but_not_new_margin':
            utm_ulp > old_margin and new_margin > utm_ulp,
    }
    witnesses = {
        'overflow_max_alpha': {'r': r_big, 'p': p_big},
        'underflow_max_alpha': {'r': r_tiny, 'p': p_tiny, 'k': k},
        'near_blocked': {'r': r_nb, 'p': p_nb, 'gap': gap},
        'interval_overflow': {'fa': fa_big, 'fb': fb_big, 'len': len_big},
        'safe_length_subnormal_sq': {'c': c_sub},
        'dot_underflow': {'ab_x': ab_x, 'qa_x': qa_x},
        'estimate_overflow': {'total': tot_big, 'alpha': alpha_big, 'density': density},
        'adjacent_midpoint': {'x0': x0, 'x1': x1},
        'utm_margin': {'coord': utm, 'ulp': float(utm_ulp), 'old_margin': float(old_margin), 'new_margin': float(new_margin)},
    }
    return checks, witnesses


def run(timeout_ms=8000):
    source_contract(SOURCE, ANCHORS)
    source_contract(MATHUTIL_SOURCE, MATHUTIL_ANCHORS)
    rows = (
        barycentric_and_clearance_obligations(timeout_ms)
        + max_alpha_obligations(timeout_ms)
        + lipschitz_interval_obligations(timeout_ms)
    )
    replays, witnesses = replay_fp()
    result = report(rows, replays=replays, witnesses=witnesses)
    result['passed'] &= all(replays.values())
    return result


if __name__ == '__main__':
    raise SystemExit(main(run))
