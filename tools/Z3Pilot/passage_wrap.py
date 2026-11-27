"""Local PassageWrap obligations and binary64 boundary replays.

Checks the barycentric ball identity and Claims 1-2 of docs/design/PASSAGE_WRAP.md,
the scaled rationalized MaxAlpha root equivalence, optimality and monotonicity,
the 1-Lipschitz PolylineClearance interval lower bound, and binary64 witnesses
for extreme-scale overflow/underflow, near-blocked cancellation, UTM margin,
and adjacent-double midpoint stall.
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
     'a546d624acda890eab807dbe65f1e44de9e590a9dc974e1f7999e0c314eabe09'),
    ('internal static void PolylineClearance(', 'public bool Compute()',
     'ca2aa88fb82a585bcb3d45fccfbc29b6fcf35abe48b0286d8a29b5e9abc02bc5'),
    ('double estimate(double alpha, bool openToo)', 'sealed class MinHeap',
     'c371f55870e4d480c6f461476193ae75c8ed8d3c576ef3e93a0018249f625eee'),
    ('internal static double PolylineDistance(', 'PassagePinResult[] measure(',
     'e8ce5429019304219b7dc523f09ad2a3dd515f6d4d13fa429d80cd607a6abfbd'),
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

    # 2. Claims 1 & 2 from (*):
    #    - For q on facet f: R^2 = d_qm^2 + S_pq with S_pq >= (r - d)^2 -> R >= r - d > a.
    #    - For y on facet f: d_yq^2 = S_pq_y - S_py with S_pq_y >= (r - d)^2 and S_py <= R^2 <= a^2
    #      -> d_yq^2 >= (r - d)^2 - a^2.
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
    r, p, k, s, a, a2 = z3.Reals('r p k s a a2')
    u = p / r
    diff_sq = (1 - u) * (1 + u)
    disc = k * k + (1 - k) * (1 + k) * diff_sq
    root_premises = [
        r > p, p >= 0, k > 0, k < 1,
        s > 0, s * s == disc,
        a == r * (diff_sq / (k + s)),
    ]
    # Monotonicity in p (fixing r, k): larger p2 > p gives smaller a2 < a.
    p2, s2 = z3.Reals('p2 s2')
    u2 = p2 / r
    diff_sq2 = (1 - u2) * (1 + u2)
    mono_p_premises = root_premises + [
        p2 > p, p2 < r,
        s2 > 0, s2 * s2 == k * k + (1 - k) * (1 + k) * diff_sq2,
        a2 == r * (diff_sq2 / (k + s2)),
    ]
    return [
        check_expr('max_alpha_premises_reachable', root_premises,
                   z3.And(a > 0, r - k * a > 0), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('max_alpha_fails_quadratic_root_identity', root_premises,
                   z3.Or(a <= 0, r - k * a <= 0, (r - k * a) ** 2 - a * a != p * p),
                   'unsat', timeout_ms, 'QF_NRA'),
        check_expr('larger_alpha_can_still_satisfy_clearance_bound',
                   root_premises + [a2 > a, r - k * a2 >= 0],
                   a2 * a2 + p * p <= (r - k * a2) ** 2, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('blocked_pin_can_have_positive_valid_alpha',
                   [r > 0, p >= r, k > 0, k < 1, a > 0, r - k * a >= 0],
                   a * a + p * p <= (r - k * a) ** 2, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('max_alpha_monotonicity_in_pin_radius_can_fail', mono_p_premises,
                   a2 >= a, 'unsat', timeout_ms, 'QF_NRA'),
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


def replay_fp():
    f64, rnd = z3.Float64(), z3.RNE()
    fp = lambda x: z3.FPVal(x, f64)
    same = lambda expr, value: z3.is_true(z3.simplify(z3.fpEQ(expr, fp(value))))
    is_nan = lambda expr: z3.is_true(z3.simplify(z3.fpIsNaN(expr)))

    # 1. Old MaxAlpha at r = 2^600, p = 2^599 overflows r^2 - p^2 to NaN.
    r_big, p_big = 2.0**600, 2.0**599
    old_diff_big = z3.fpSub(rnd, z3.fpMul(rnd, fp(r_big), fp(r_big)),
                            z3.fpMul(rnd, fp(p_big), fp(p_big)))

    # 2. Old MaxAlpha at r = 2^-600, p = 2^-601 underflows r^2 and p^2 to 0, returning negative -r*k/(1-k^2).
    r_tiny, p_tiny, k = 2.0**-600, 2.0**-601, 1.0 / 30.0
    r_tiny2 = z3.fpMul(rnd, fp(r_tiny), fp(r_tiny))
    p_tiny2 = z3.fpMul(rnd, fp(p_tiny), fp(p_tiny))
    old_num_tiny = z3.fpAdd(rnd, z3.fpMul(rnd, fp(-r_tiny), fp(k)), z3.fpSqrt(rnd, fp(0.0)))

    # 3. Near-blocked r = 3, p = 3 - 2^-50: exact Sterbenz (r - p) = 2^-50 vs old cancellation.
    r_nb = 3.0
    gap = 2.0**-50
    p_nb = r_nb - gap
    sterbenz_gap = z3.fpSub(rnd, fp(r_nb), fp(p_nb))

    # 4. Adjacent doubles at 1e14: midpoint 0.5*a + 0.5*b collapses onto an endpoint.
    x0 = 1e14
    x1 = math.nextafter(x0, math.inf)
    mid = z3.fpAdd(rnd, z3.fpMul(rnd, fp(0.5), fp(x0)), z3.fpMul(rnd, fp(0.5), fp(x1)))

    # 5. UTM rounding: ulp(2^23) = 2^-29 >> 1e-12 * 1.0, while 64 * eps * 2^23 dominates ulp(2^23).
    utm = 2.0**23
    utm_ulp = Fraction(math.ulp(utm))
    old_margin = Fraction(1, 10**12)
    new_margin = Fraction(64) * Fraction(2.0**-52) * Fraction(utm)

    checks = {
        'old_max_alpha_overflows_to_nan_at_2p600':
            is_nan(old_diff_big) and Fraction(p_big, r_big) == Fraction(1, 2),
        'old_max_alpha_underflows_negative_at_2m600':
            same(r_tiny2, 0.0) and same(p_tiny2, 0.0)
            and z3.is_true(z3.simplify(z3.fpLT(old_num_tiny, fp(0.0)))),
        'near_blocked_sterbenz_difference_is_exact':
            same(sterbenz_gap, gap) and Fraction(r_nb) - Fraction(p_nb) == Fraction(gap),
        'adjacent_double_midpoint_stalls_on_endpoint':
            z3.is_true(z3.simplify(z3.Or(z3.fpEQ(mid, fp(x0)), z3.fpEQ(mid, fp(x1))))) and x1 > x0,
        'utm_coordinate_ulp_exceeds_old_diag_margin_but_not_new_margin':
            utm_ulp > old_margin and new_margin > utm_ulp,
    }
    witnesses = {
        'overflow_max_alpha': {'r': r_big, 'p': p_big},
        'underflow_max_alpha': {'r': r_tiny, 'p': p_tiny, 'k': k},
        'near_blocked': {'r': r_nb, 'p': p_nb, 'gap': gap},
        'adjacent_midpoint': {'x0': x0, 'x1': x1},
        'utm_margin': {'coord': utm, 'ulp': float(utm_ulp), 'old_margin': float(old_margin), 'new_margin': float(new_margin)},
    }
    return checks, witnesses


def run(timeout_ms=8000):
    source_contract(SOURCE, ANCHORS)
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
