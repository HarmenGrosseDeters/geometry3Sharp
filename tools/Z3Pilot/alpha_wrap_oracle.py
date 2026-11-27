"""Local Alpha Wrap oracle pruning obligations and binary64 boundary replays.

Checks the face-plane bounding-box projection bounds, squared vs unsquared gap
equivalence, outward interval-clip bounds, entry discriminant range envelope,
and binary64 witnesses for subnormal/overflowing margin^2, reciprocal overflow
on subnormal directions, 3-rounding clip misses, and UTM box/slab rounding.
"""
from fractions import Fraction
import math

import z3

from check_pilot_obligations import check_expr, main, report, source_contract

SOURCE = 'mesh_ops/AlphaWrapOracle.cs'
ANCHORS = [
    ('struct Tet', 'static readonly int[,] TetEdges =',
     'dcf4a99b99d3daa24a355aba858059743e5127893f3d3faa8bc9a305bae2a05d'),
    ('bool tet_rec(', 'bool face_separates(',
     '7f20cdb50b3843372d379ae7f24ae9e7a74f6778acf43a43d1e79f2f22bd05a8'),
    ('bool face_separates(', 'static bool box_box_apart(',
     '48fae4431b8d9a0ef597d02c9d0e1c54dee0f248668dadba61ad1ad0fbdd9ba9'),
    ('static bool box_box_apart(', 'static bool separated(',
     '588e8e1bc3431390ea9752396b55adc745b9e1278b2f6d3cbed11de28409620c'),
    ('static bool separated(', 'bool tet_primitive_touch(',
     '558c4fcc4937edd9264a3dcff932c267609f2058a685004b7cf2ba287641890b'),
    ('struct Seg', 'double primitive_entry_once(',
     'e5634897828065d106b35241f7cbcf3e4eb73271ce8a016f752ecacb43196a5f'),
    ('static double sphere_entry(', 'static double capsule_entry(',
     '241e2501cb1ecdeeb7c749caf78b81498acc21116318d6ee2bd1a51e21e81d8a'),
]


def face_projection_obligations(timeout_ms):
    nx, ny, nz = z3.Reals('nx ny nz')
    mnx, mny, mnz = z3.Reals('mnx mny mnz')
    mxx, mxy, mxz = z3.Reals('mxx mxy mxz')
    px, py, pz = z3.Reals('px py pz')
    premises = [
        mnx <= px, px <= mxx,
        mny <= py, py <= mxy,
        mnz <= pz, pz <= mxz,
    ]
    lo = lambda a, b: z3.If(a <= b, a, b)
    hi = lambda a, b: z3.If(a >= b, a, b)
    bmin = lo(nx * mnx, nx * mxx) + lo(ny * mny, ny * mxy) + lo(nz * mnz, nz * mxz)
    bmax = hi(nx * mnx, nx * mxx) + hi(ny * mny, ny * mxy) + hi(nz * mnz, nz * mxz)
    dot = nx * px + ny * py + nz * pz
    naive_min = nx * mnx + ny * mny + nz * mnz
    return [
        check_expr('face_box_projection_reachable', premises,
                   z3.And(nx < 0, ny > 0, mnx < px, px < mxx), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('face_box_projection_can_overestimate_lower_bound', premises,
                   bmin > dot, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('face_box_projection_can_underestimate_upper_bound', premises,
                   bmax < dot, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('omitting_sign_min_can_overestimate_lower_bound', premises,
                   naive_min > dot, 'sat', timeout_ms, 'QF_NRA'),
    ]


def clip_interval_obligations(timeout_ms):
    # For lo <= x <= hi and inv_lo <= inv <= inv_hi with inv_lo, inv_hi of the same sign,
    # the sign-selected products enclose x * inv for both positive and negative directions.
    lo, x, hi, inv_lo, inv, inv_hi = z3.Reals('clip_lo clip_x clip_hi inv_lo inv inv_hi')
    pos_premises = [lo <= x, x <= hi, 0 <= inv_lo, inv_lo <= inv, inv <= inv_hi]
    neg_premises = [lo <= x, x <= hi, inv_lo <= inv, inv <= inv_hi, inv_hi <= 0]
    pos_ta = lo * z3.If(lo >= 0, inv_lo, inv_hi)
    pos_tb = hi * z3.If(hi >= 0, inv_hi, inv_lo)
    neg_ta = hi * z3.If(hi >= 0, inv_lo, inv_hi)
    neg_tb = lo * z3.If(lo >= 0, inv_hi, inv_lo)
    return [
        check_expr('clip_interval_pos_reachable', pos_premises,
                   z3.And(lo < 0, hi > 0, inv_lo < inv_hi), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('clip_interval_pos_can_miss_exact_product', pos_premises,
                   z3.Or(pos_ta > x * inv, pos_tb < x * inv), 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('clip_interval_neg_reachable', neg_premises,
                   z3.And(lo < 0, hi > 0, inv_lo < inv_hi), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('clip_interval_neg_can_miss_exact_product', neg_premises,
                   z3.Or(neg_ta > x * inv, neg_tb < x * inv), 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('clip_interval_unselected_inv_can_miss_negative_lo', pos_premises,
                   lo * inv_lo > x * inv, 'sat', timeout_ms, 'QF_NRA'),
    ]


def gap_and_range_obligations(timeout_ms):
    gap, margin, length = z3.Reals('gap margin axis_length')
    gap_premises = [gap > 0, margin >= 0, length > 0]
    a, b, c = z3.Reals('sphere_A sphere_B sphere_C')
    # Supported coordinates lie in [-2^128, 2^128], so edge differences have magnitude <= 2^129
    # and dot products / squared lengths fit below 3 * 2^258 < 2^260. sphere_entry returns 0 when C <= 0,
    # so C > 0 holds at the discriminant B^2 - A * C.
    disc_premises = [a >= 0, a <= 2**260, b >= -2**260, b <= 2**260, c > 0, c <= 2**260]
    return [
        check_expr('gap_comparison_premises_reachable', gap_premises,
                   z3.And(margin > 0, gap > margin * length), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('squared_and_unsquared_gap_can_disagree', gap_premises,
                   (gap * gap > margin * margin * length * length) != (gap > margin * length),
                   'unsat', timeout_ms, 'QF_NRA'),
        check_expr('omitting_margin_factor_can_false_separate', gap_premises,
                   z3.And(gap > margin, gap <= margin * length), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('entry_discriminant_envelope_reachable', disc_premises,
                   b * b - a * c >= 2**519, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('entry_discriminant_can_exceed_2_521', disc_premises,
                   z3.Or(b * b - a * c >= 2**521, b * b - a * c <= -(2**521)),
                   'unsat', timeout_ms, 'QF_NRA'),
        check_expr('entry_discriminant_without_c_pos_bound_can_reach_2_521',
                   [a >= 0, a <= 2**260, b >= -2**260, b <= 2**260, c >= -2**260, c <= 2**260],
                   b * b - a * c >= 2**521, 'sat', timeout_ms, 'QF_NRA'),
    ]


def replay_fp():
    f64, rnd = z3.Float64(), z3.RNE()
    fp = lambda x: z3.FPVal(x, f64)
    same = lambda expr, value: z3.is_true(z3.simplify(z3.fpEQ(expr, fp(value))))

    # 1. Tiny margin at 2^126 scale: margin^2 * l2 underflows to 0 while gap^2 stays positive.
    l2_large = 2.0**504
    len_large = 2.0**252
    tiny_margin = 2.0**-600
    gap_inside = 0.5 * tiny_margin * len_large
    m2_l2_under = z3.fpMul(rnd, z3.fpMul(rnd, fp(tiny_margin), fp(tiny_margin)), fp(l2_large))
    gap2_inside = z3.fpMul(rnd, fp(gap_inside), fp(gap_inside))
    unsquared_lim_under = z3.fpMul(rnd, fp(tiny_margin), fp(len_large))

    # 2. Subnormal margin^2 at 2^125 scale: margin^2 rounds down by 33% so (margin^2)*l2 falsely separates 0.85*margin.
    sub_margin = math.sqrt(1.499) * 2.0**-537
    l2_sub = 2.0**500
    len_sub = 2.0**250
    sub_gap = 0.85 * sub_margin * len_sub
    sub_m2 = z3.fpMul(rnd, fp(sub_margin), fp(sub_margin))
    sub_gap2 = z3.fpMul(rnd, sub_m2, fp(l2_sub))
    sub_g2 = z3.fpMul(rnd, fp(sub_gap), fp(sub_gap))

    # 3. 2D subnormal box_box_dist_sqr: dx = dy = 0.6 * sub_margin rounds up while margin^2 rounds down.
    d_xy = 0.6 * sub_margin
    d_xy2 = z3.fpMul(rnd, fp(d_xy), fp(d_xy))
    box_dist2 = z3.fpAdd(rnd, d_xy2, d_xy2)

    # 4. Huge margin at 2^-126 scale: margin^2 * l2 overflows to +inf while unsquared product is finite.
    l2_small = 2.0**-504
    len_small = 2.0**-252
    huge_margin = 2.0**600
    gap_outside = 2.0 * huge_margin * len_small
    m2_l2_over = z3.fpMul(rnd, z3.fpMul(rnd, fp(huge_margin), fp(huge_margin)), fp(l2_small))
    unsquared_lim_over = z3.fpMul(rnd, fp(huge_margin), fp(len_small))

    # 5. Subnormal segment direction: 1 / dir overflows to +inf, turning (mn - o) * inv into +inf.
    h = 2.0**-1026
    sub_dir = 2.0 * h
    inv_sub = z3.fpDiv(rnd, fp(1.0), fp(sub_dir))
    mul_overflow = z3.fpMul(rnd, fp(h), inv_sub)
    direct_div = z3.fpDiv(rnd, fp(h), fp(sub_dir))

    # 6. UTM small radius: mn - r rounds upward (inward), whereas nextafter downward encloses exact mn - r.
    y0 = 5712043.5
    ulp = 2.0**-30
    r_utm = 1.4 * ulp
    rounded_sub = z3.fpSub(rnd, fp(y0), fp(r_utm))
    widened_sub = math.nextafter(y0 - r_utm, -math.inf)

    # 7. Opposite 3-rounding error in clip exceeds 1 ulp; outward interval bracketing keeps [t0, t1] non-empty.
    px, py = 2.9800848651405232e-12, 3.265311463278164e-12
    r_clip = 6.881275750678796e-29
    ax, ay = -752.9165473261443, -833.5095509181443
    bx, by = 902.686155529702, 999.3106603747793
    dx_c, dy_c = bx - ax, by - ay
    ix_c, iy_c = 1.0 / dx_c, 1.0 / dy_c
    old_tb_x = math.nextafter((math.nextafter(px + r_clip, math.inf) - ax) * ix_c, math.inf)
    old_ta_y = math.nextafter((math.nextafter(py - r_clip, -math.inf) - ay) * iy_c, -math.inf)
    new_tb_x = math.nextafter(math.nextafter(math.nextafter(px + r_clip, math.inf) - ax, math.inf) * math.nextafter(ix_c, math.inf), math.inf)
    new_ta_y = math.nextafter(math.nextafter(math.nextafter(py - r_clip, -math.inf) - ay, -math.inf) * math.nextafter(iy_c, -math.inf), -math.inf)

    checks = {
        'tiny_margin_squared_underflows_while_unsquared_prevents_false_separation':
            same(m2_l2_under, 0.0) and same(gap2_inside, 2.0**-698)
            and same(unsquared_lim_under, 2.0**-348) and gap_inside <= tiny_margin * len_large,
        'subnormal_margin_squared_falsely_separates_while_unsquared_keeps_touch':
            same(sub_m2, 2.0**-1074) and same(sub_gap2, 2.0**-574)
            and z3.is_true(z3.simplify(z3.fpGT(sub_g2, sub_gap2)))
            and sub_gap <= sub_margin * len_sub,
        'subnormal_2d_box_dist_sqr_falsely_prunes_while_scaled_norm_keeps_touch':
            same(box_dist2, 2.0 * 2.0**-1074)
            and z3.is_true(z3.simplify(z3.fpGT(box_dist2, sub_m2)))
            and d_xy * math.sqrt(2.0) <= sub_margin,
        'huge_margin_squared_overflows_while_unsquared_preserves_separation':
            same(m2_l2_over, math.inf) and same(unsquared_lim_over, 2.0**348)
            and gap_outside > huge_margin * len_small,
        'subnormal_direction_reciprocal_overflows_while_direct_division_is_exact':
            same(inv_sub, math.inf) and same(mul_overflow, math.inf) and same(direct_div, 0.5),
        'utm_box_expansion_rounds_inward_while_nextafter_encloses_exact_difference':
            same(rounded_sub, y0 - ulp)
            and Fraction(y0 - ulp) > Fraction(y0) - Fraction(r_utm)
            and Fraction(widened_sub) <= Fraction(y0) - Fraction(r_utm),
        'three_rounding_clip_exceeds_one_ulp_while_per_step_widening_keeps_hit':
            old_ta_y > old_tb_x and new_ta_y <= new_tb_x,
    }
    witnesses = {
        'tiny_margin_underflow': {
            'l2': l2_large, 'margin': tiny_margin, 'gap': gap_inside,
            'unsquared_limit': tiny_margin * len_large,
        },
        'subnormal_margin_squared': {
            'l2': l2_sub, 'margin': sub_margin, 'gap': sub_gap,
            'unsquared_limit': sub_margin * len_sub,
        },
        'huge_margin_overflow': {
            'l2': l2_small, 'margin': huge_margin, 'gap': gap_outside,
            'unsquared_limit': huge_margin * len_small,
        },
        'subnormal_direction': {'offset': h, 'dir': sub_dir, 'exact_t': 0.5},
        'utm_box_expansion': {
            'y0': y0, 'r': r_utm, 'rounded': y0 - r_utm, 'widened_down': widened_sub,
        },
        'three_rounding_clip': {
            'old_ta_y': old_ta_y, 'old_tb_x': old_tb_x, 'new_ta_y': new_ta_y, 'new_tb_x': new_tb_x,
        },
    }
    return checks, witnesses


def run(timeout_ms=8000):
    source_contract(SOURCE, ANCHORS)
    rows = (face_projection_obligations(timeout_ms)
            + clip_interval_obligations(timeout_ms)
            + gap_and_range_obligations(timeout_ms))
    replays, witnesses = replay_fp()
    result = report(rows, replays=replays, witnesses=witnesses)
    result['passed'] &= all(replays.values())
    return result


if __name__ == '__main__':
    raise SystemExit(main(run))
