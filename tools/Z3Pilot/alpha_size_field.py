"""Local AlphaSizeField obligations and binary64 boundary replays.

Checks the reach cutoff, cell-box lower-bound pruning soundness, thinning
perturbation bound, wide-axis thinning index separation, and binary64
witnesses for wide-index overflow, subnormal r^2 false-skips, and huge-radius
distance-squared overflow.
"""
import math

import z3

from check_pilot_obligations import check_expr, main, report, source_contract

SOURCE = 'mesh_ops/AlphaSizeField.cs'
ANCHORS = [
    ('public AlphaSizeField(', 'void BuildBounds(',
     '2b8bef47c694d2996f842f56499ecb6d1cdaf9b76bb9f5c084adfb7967876749'),
    ('static bool FitsIndex(', 'static double SafeDistance(',
     'ebc553bd25d232a30a77c9feab6c1aae8585c03cfc0d6b5335b7894979cfc19e'),
    ('static double SafeDistance(', 'public double At(Vector3d x)',
     'b7876d644efc650733332499f98ca62f30488d90131abbfe18bf1acb477e192e'),
    ('public double At(Vector3d x, out int visited)', 'public static Vector3d[] Samples(',
     '769af0b62dbbd3f3a1225d6a12a4e0bf5ce2098a313b502c3b7ee8490b437905'),
]


def reach_and_pruning_obligations(timeout_ms):
    a0, a_min, a_m, slope, dist, best = z3.Reals('alpha0 alpha_min alpha_m slope dist best')
    radius = (a0 - a_min) / slope
    reach_premises = [a0 > a_min, a_min > 0, slope > 0, a_m >= a_min, dist > radius]

    # Cell box lower-bound pruning: for p in [bmin, bmax], box distance <= |x - p|.
    xx, xy, xz = z3.Reals('x_x x_y x_z')
    px, py, pz = z3.Reals('p_x p_y p_z')
    mnx, mny, mnz = z3.Reals('bmin_x bmin_y bmin_z')
    mxx, mxy, mxz = z3.Reals('bmax_x bmax_y bmax_z')
    box_premises = [
        mnx <= px, px <= mxx,
        mny <= py, py <= mxy,
        mnz <= pz, pz <= mxz,
    ]
    clamp_gap = lambda lo, hi, v: z3.If(v < lo, lo - v, z3.If(v > hi, v - hi, 0))
    dx, dy, dz = clamp_gap(mnx, mxx, xx), clamp_gap(mny, mxy, xy), clamp_gap(mnz, mxz, xz)
    box_d2 = dx * dx + dy * dy + dz * dz
    pt_d2 = (px - xx) * (px - xx) + (py - xy) * (py - xy) + (pz - xz) * (pz - xz)

    min_alpha, box_d, pt_d = z3.Reals('cell_min_alpha box_dist pt_dist')
    prune_premises = [slope > 0, a_m >= min_alpha, 0 <= box_d, box_d <= pt_d, min_alpha + slope * box_d >= best]

    return [
        check_expr('reach_cutoff_premises_reachable', reach_premises,
                   a_m + slope * dist > a0, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('sample_beyond_reach_can_improve_alpha0', reach_premises,
                   a_m + slope * dist <= a0, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('sample_below_alpha_min_can_improve_beyond_reach',
                   [a0 > a_min, a_min > 0, slope > 0, a_m < a_min, a_m > 0, dist > radius],
                   a_m + slope * dist < a0, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('box_distance_premises_reachable', box_premises,
                   z3.And(xx < mnx, py > mny, pt_d2 > box_d2), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('box_distance_can_exceed_point_distance', box_premises,
                   box_d2 > pt_d2, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('cell_pruning_premises_reachable', prune_premises,
                   a_m + slope * pt_d >= best, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('pruned_cell_can_improve_best', prune_premises,
                   a_m + slope * pt_d < best, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('pruning_with_overestimated_box_dist_can_miss_better_sample',
                   [slope > 0, a_m >= min_alpha, 0 <= pt_d, box_d > pt_d, min_alpha + slope * box_d >= best],
                   a_m + slope * pt_d < best, 'sat', timeout_ms, 'QF_NRA'),
    ]


def thinning_obligations(timeout_ms):
    # Two samples in the same thinning cube of side h = alpha_min / 2 lie within h * sqrt(3).
    # Keeping the smaller alpha_k <= alpha_j shifts alpha_k + L*|x - p_k| by at most L*h*sqrt(3)
    # above alpha_j + L*|x - p_j| (by triangle inequality |x - p_k| <= |x - p_j| + |p_k - p_j|).
    h, slope, a_j, a_k, dj, dk, djk = z3.Reals('cube_h slope alpha_j alpha_k dist_j dist_k dist_jk')
    thin_premises = [
        h > 0, slope > 0, a_k <= a_j,
        dj >= 0, dk >= 0, djk >= 0,
        djk * djk <= 3 * h * h,
        dk <= dj + djk,
    ]
    # Wide thinning index separation (|coord| >= 2^62 * h => binary64 neighbor gap > h).
    mag, gap = z3.Reals('coord_mag neighbor_gap')
    wide_premises = [h > 0, mag >= 2**62 * h, 2**53 * gap >= mag]
    return [
        check_expr('thinning_bound_premises_reachable', thin_premises,
                   a_k + slope * dk > a_j + slope * dj, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('thinning_can_exceed_cube_diagonal_perturbation', thin_premises,
                   (a_k + slope * dk) - (a_j + slope * dj) > slope * djk, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('wide_thin_index_premises_reachable', wide_premises, gap > h, 'sat', timeout_ms, 'QF_LRA'),
        check_expr('wide_thin_index_can_share_cube', wide_premises, gap <= h, 'unsat', timeout_ms, 'QF_LRA'),
        check_expr('narrow_thin_index_can_share_cube',
                   [h > 0, mag >= 2**52 * h, 2**53 * gap >= mag], gap <= h, 'sat', timeout_ms, 'QF_LRA'),
    ]


def replay_fp():
    f64, rnd = z3.Float64(), z3.RNE()
    fp = lambda x: z3.FPVal(x, f64)
    same = lambda expr, value: z3.is_true(z3.simplify(z3.fpEQ(expr, fp(value))))

    # 1. Wide thinning index exceeds signed 64-bit integer range.
    x_wide = 2.0**58
    h = 2.0**-5
    wide_idx = z3.fpDiv(rnd, fp(x_wide), fp(h))

    # 2. Subnormal r^2 in 2D: r^2 rounds down to 2^-1074 while dx^2 + dy^2 = 2 * 2^-1074.
    r_sub = math.sqrt(1.499) * 2.0**-537
    r_sub2 = z3.fpMul(rnd, fp(r_sub), fp(r_sub))
    d_xy = 0.6 * r_sub
    d_xy2 = z3.fpMul(rnd, fp(d_xy), fp(d_xy))
    sum_d2 = z3.fpAdd(rnd, d_xy2, d_xy2)

    # 3. Huge radius: r^2 and d^2 overflow to +inf while scaled norm stays finite.
    r_huge = 2.0**600
    d_huge = 0.5 * r_huge
    r_huge2 = z3.fpMul(rnd, fp(r_huge), fp(r_huge))
    d_huge2 = z3.fpMul(rnd, fp(d_huge), fp(d_huge))

    checks = {
        'wide_thinning_index_exceeds_int64_range':
            same(wide_idx, 2.0**63) and x_wide / h >= 2**63,
        'subnormal_radius_squared_falsely_skips_inside_sample_while_safe_distance_keeps_it':
            same(r_sub2, 2.0**-1074) and same(sum_d2, 2.0 * 2.0**-1074)
            and z3.is_true(z3.simplify(z3.fpGT(sum_d2, r_sub2)))
            and d_xy * math.sqrt(2.0) < r_sub,
        'huge_radius_overflows_distance_squared_while_safe_distance_is_exact':
            same(r_huge2, math.inf) and same(d_huge2, math.inf) and d_huge < r_huge,
    }
    witnesses = {
        'wide_thinning': {'x': x_wide, 'h': h, 'index': x_wide / h},
        'subnormal_radius': {'r': r_sub, 'dx': d_xy, 'true_dist': d_xy * math.sqrt(2.0)},
        'huge_radius': {'r': r_huge, 'd': d_huge},
    }
    return checks, witnesses


def run(timeout_ms=8000):
    source_contract(SOURCE, ANCHORS)
    rows = reach_and_pruning_obligations(timeout_ms) + thinning_obligations(timeout_ms)
    replays, witnesses = replay_fp()
    result = report(rows, replays=replays, witnesses=witnesses)
    result['passed'] &= all(replays.values())
    return result


if __name__ == '__main__':
    raise SystemExit(main(run))
