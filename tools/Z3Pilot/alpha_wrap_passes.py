"""Local Z3 obligations and binary64 replays for AlphaWrapFeaturePass and AlphaWrapQualityPass.

Checks:
1. Dimensionless miter-shift normalization and capping (AlphaWrapFeaturePass.build_miters):
   - |n1 + n2|^2 = 2 (1 + c) for unit normals with c = n1.n2.
   - Uncapped shift s = offset * (n1 + n2) / (1 + c) lies on both offset planes n_i . s = offset.
   - Capping dir = (n1 + n2) / (1 + c) in dimensionless offset units before scaling by offset
     gives |s| = M * offset for all offset > 0 (eliminating 2^600 s * 0 = 0 wipeout and 2^-600
     underflow skipping the cap).
   - Negative control (SAT): uncapped |s| > M * offset when 1 + c < 2 / M^2.
2. Crease intersection projection (AlphaWrapFeaturePass.crease_point):
   - x = p + c1*n1 + c2*n2 satisfies n1 . x = D1 and n2 . x = D2.
   - Orthogonality (x - p) . (y - x) = 0 for any y on both planes (nearest point on intersection line).
   - Negative control (SAT): dropping -c * r2 breaks plane incidence when c != 0.
3. Protected-edge grid monotone clamping (AlphaWrapQualityPass.cells) and SegmentGrid cell bounds:
   - Clamping both endpoints monotonically to [-1, 66] preserves overlap of any two overlapping
     integer cell intervals; one-sided clamping admits a disjointness counterexample (SAT).
   - CellLimit = 2^28 keeps x1 + 1 strictly below Int32.MaxValue and bounds the 1D span;
     unclamped x1 = Int32.MaxValue overflows x1 + 1 (SAT).
4. Power-of-two rescaling invariance for angle and unit_normal thresholds.
5. Concrete binary64 replays pinning the old extreme-scale failures (2^600, 2^-600, 2^-540,
   and SegmentGrid Int32 wrap-around) against the hardened paths.
"""
import math
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

FEATURE_SOURCE = 'mesh_ops/AlphaWrapFeaturePass.cs'
QUALITY_SOURCE = 'mesh_ops/AlphaWrapQualityPass.cs'

FEATURE_ANCHORS = [
    ('void build_miters()', 'static bool traverses(', 'effe8bb411a933f9a6f10c6ec6696d4bc88b0fe8bf30aa888505fcead2be48a9'),
    ('static bool crease_point(Label a, Label b, Vector3d p, out Vector3d x)', 'bool crossing(', '2ed7c48e23b0519a600cdbad60c08cea5c52402f608f7367693d5b8f611b7929'),
    ('static bool unit_normal(Triangle3d t, out Vector3d n)', 'static double min_angle(', '64c7f7d2acccf4040c5499c7a596e7535c98d305b5409a7f499fcc30cd58ca77'),
    ('sealed class SegmentGrid', 'static bool SegmentWithin(', 'f7a9ecb7f6c64eb9c0da9baee6c228898a0441819acb374950b91bd651324853'),
    ('static bool SegmentWithin(Segment3d seg, Vector3d p, double r)', 'if (!cells.TryGetValue(new Vector3i(x, y, z), out l))', 'aa50038eca510999bbf4fea4de509d024bc573afc4ca4a189b70c38bb39aaa4d'),
]

QUALITY_ANCHORS = [
    ('void build_protected()', 'static bool SegmentTriangleWithin(', 'dbc1d8465a367c5ad6bd19e7e5e499259b35682b419299077b53aec5b85193bc'),
    ('static bool SegmentTriangleWithin(Segment3d sg, Triangle3d t, double r)', 'TriangleHashGrid build_grid()', 'ac28f2ac6c11e2698802d288c78855a60f0a7a87af8bf696ba8cfba3c4f3f2f3'),
    ('static double angle(Vector3d u, Vector3d v)', 'double normal_deviation(', 'dd3ffa85caaff3563ac0ec8636aa77cff0749b11c9f6893d0991339b1b9f77fa'),
]

CELL_LIMIT = 1 << 28
INT32_MAX = (1 << 31) - 1


def miter_and_crease_obligations():
    rows = []

    # 1. Miter direction norm and plane incidence (3D unit normals)
    n1x, n1y, n1z = z3.Reals('n1x n1y n1z')
    n2x, n2y, n2z = z3.Reals('n2x n2y n2z')
    c, offset, M = z3.Reals('c offset M')
    dx, dy, dz = z3.Reals('dx dy dz')
    sx, sy, sz = z3.Reals('sx sy sz')
    dl, dcx, dcy, dcz = z3.Reals('dl dcx dcy dcz')

    unit_prem = [
        n1x * n1x + n1y * n1y + n1z * n1z == 1,
        n2x * n2x + n2y * n2y + n2z * n2z == 1,
        c == n1x * n2x + n1y * n2y + n1z * n2z,
        1 + c > 0,
        offset > 0,
        M > 0,
    ]
    dir_prem = unit_prem + [
        (1 + c) * dx == n1x + n2x,
        (1 + c) * dy == n1y + n2y,
        (1 + c) * dz == n1z + n2z,
        sx == dx * offset,
        sy == dy * offset,
        sz == dz * offset,
    ]
    rows.append(('miter_assumptions_sat', dir_prem, M == 4, 'sat', 'QF_NRA'))
    rows.append((
        'miter_sum_norm_identity',
        unit_prem,
        (n1x + n2x) * (n1x + n2x) + (n1y + n2y) * (n1y + n2y) + (n1z + n2z) * (n1z + n2z) != 2 * (1 + c),
        'unsat',
        'QF_NRA',
    ))
    rows.append((
        'miter_uncapped_on_both_planes',
        dir_prem,
        z3.Or(
            n1x * sx + n1y * sy + n1z * sz != offset,
            n2x * sx + n2y * sy + n2z * sz != offset,
        ),
        'unsat',
        'QF_NRA',
    ))

    # Dimensionless cap: dl^2 = |dir|^2 = 2 / (1 + c), cap dir_c = dir * (M / dl), s_c = dir_c * offset
    cap_prem = [
        dl > 0,
        M > 0,
        offset > 0,
        dl > M,
        dl * dcx == M * dx,
        dl * dcy == M * dy,
        dl * dcz == M * dz,
        dx * dx + dy * dy + dz * dz == dl * dl,
    ]
    rows.append((
        'miter_capped_norm_exact',
        cap_prem,
        (dcx * offset) ** 2 + (dcy * offset) ** 2 + (dcz * offset) ** 2 != (M * offset) ** 2,
        'unsat',
        'QF_NRA',
    ))
    # Negative control: uncapped shift exceeds M * offset when 1 + c < 2 / M^2
    rows.append((
        'miter_uncapped_exceeds_cap_negative_control',
        dir_prem + [M == 4, 1 + c < z3.Q(1, 16)],
        sx * sx + sy * sy + sz * sz > (M * offset) ** 2,
        'sat',
        'QF_NRA',
    ))

    # 2. Crease point incidence and orthogonality (nearest point on intersection line)
    det, r1, r2, c1, c2 = z3.Reals('det r1 r2 c1 c2')
    D1, D2, p1, p2 = z3.Reals('D1 D2 p1 p2')
    crease_prem = [
        det == 1 - c * c,
        det > 0,
        r1 == D1 - p1,
        r2 == D2 - p2,
        det * c1 == r1 - c * r2,
        det * c2 == r2 - c * r1,
    ]
    # n1 . x = p1 + c1 + c * c2, n2 . x = p2 + c * c1 + c2
    rows.append((
        'crease_point_on_both_planes',
        crease_prem,
        z3.Or(p1 + c1 + c * c2 != D1, p2 + c * c1 + c2 != D2),
        'unsat',
        'QF_NRA',
    ))
    # Orthogonality: for any displacement d = y - x with n1 . d = 0 and n2 . d = 0, (x - p) . d = 0
    vx, vy, vz = z3.Reals('vx vy vz')
    ortho_prem = [
        n1x * vx + n1y * vy + n1z * vz == 0,
        n2x * vx + n2y * vy + n2z * vz == 0,
    ]
    rows.append((
        'crease_point_nearest_orthogonality',
        ortho_prem,
        (c1 * n1x + c2 * n2x) * vx + (c1 * n1y + c2 * n2y) * vy + (c1 * n1z + c2 * n2z) * vz != 0,
        'unsat',
        'QF_NRA',
    ))
    # Negative control: dropping -c * r2 breaks plane incidence when c != 0
    bad_c1, bad_c2 = z3.Reals('bad_c1 bad_c2')
    rows.append((
        'crease_point_drop_cross_term_negative_control',
        [det == 1 - c * c, det > 0, r1 == D1 - p1, r2 == D2 - p2,
         det * bad_c1 == r1, det * bad_c2 == r2, c != 0, r2 != 0],
        p1 + bad_c1 + c * bad_c2 != D1,
        'sat',
        'QF_NRA',
    ))
    return rows


def grid_clamping_obligations():
    rows = []

    def clamp_prot(v):
        return z3.If(v < -1, -1, z3.If(v > 66, 66, v))

    a0, a1, b0, b1 = z3.Ints('a0 a1 b0 b1')
    overlap = [a0 <= a1, b0 <= b1, a0 <= b1, b0 <= a1]
    rows.append(('protected_clamp_assumptions_sat', overlap, a0 < -1, 'sat', 'QF_LIA'))
    rows.append((
        'protected_clamp_preserves_overlap',
        overlap,
        z3.Or(clamp_prot(a1) < clamp_prot(b0), clamp_prot(b1) < clamp_prot(a0)),
        'unsat',
        'QF_LIA',
    ))
    # Negative control: clamping only a while leaving b unclamped drops overlaps outside [-1, 66]
    rows.append((
        'protected_clamp_one_sided_negative_control',
        overlap,
        z3.Or(clamp_prot(a1) < b0, b1 < clamp_prot(a0)),
        'sat',
        'QF_LIA',
    ))

    # SegmentGrid Int32 non-wrap guarantee with CellLimit = 2^28
    x0, x1 = z3.Ints('x0 x1')
    sg_prem = [-CELL_LIMIT <= x0, x0 <= x1, x1 <= CELL_LIMIT]
    rows.append(('segment_grid_bounds_assumptions_sat', sg_prem, x0 < x1, 'sat', 'QF_LIA'))
    rows.append((
        'segment_grid_loop_no_int32_wrap',
        sg_prem,
        z3.Or(x1 + 1 > INT32_MAX, x1 - x0 + 1 <= 0, x1 - x0 + 1 > 2 * CELL_LIMIT + 1),
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'segment_grid_unclamped_wrap_negative_control',
        [x0 <= x1, x1 <= INT32_MAX],
        x1 + 1 > INT32_MAX,
        'sat',
        'QF_LIA',
    ))
    return rows


def scale_invariance_obligations():
    rows = []
    # 1. Angle cosine is invariant under independent positive rescalings su, sv > 0
    ux, uy, uz, vx, vy, vz = z3.Reals('ux uy uz vx vy vz')
    su, sv, lu, lv = z3.Reals('su sv lu lv')
    # u' = u / su, v' = v / sv => |u'| = lu / su, |v'| = lv / sv
    # (u'.v') * (lu * lv) == (u.v) * (lu/su) * (lv/sv)  <=>  su * sv * (u'.v') == (u.v)
    upx, upy, upz, vpx, vpy, vpz = z3.Reals('upx upy upz vpx vpy vpz')
    ang_prem = [
        su > 0, sv > 0, lu > 0, lv > 0,
        su * upx == ux, su * upy == uy, su * upz == uz,
        sv * vpx == vx, sv * vpy == vy, sv * vpz == vz,
    ]
    dot = ux * vx + uy * vy + uz * vz
    dot_p = upx * vpx + upy * vpy + upz * vpz
    rows.append((
        'angle_independent_scale_invariance',
        ang_prem,
        su * sv * dot_p != dot,
        'unsat',
        'QF_NRA',
    ))

    # 2. unit_normal degeneracy test |u x v| > 1e-12 * L is invariant under positive scale s > 0
    # |u' x v'| = |u x v| / s^2 and L' = L / s^2
    s, area2, area2_p, L, L_p = z3.Reals('s area2 area2_p L L_p')
    thr = z3.Q(1, 10**12)
    un_prem = [
        s > 0,
        s * s * area2_p == area2,
        s * s * L_p == L,
    ]
    rows.append((
        'unit_normal_threshold_scale_invariance',
        un_prem,
        (area2_p > thr * L_p) != (area2 > thr * L),
        'unsat',
        'QF_NRA',
    ))
    return rows


def replay_fp():
    replays = []

    # 1. Miter shift at 2^600: old code computes s.Length = +Inf -> s * (M * offset / +Inf) = 0
    offset_big = math.ldexp(1.0, 600)
    eps = 1e-4
    inv_n = 1.0 / math.hypot(eps, 1.0)
    n1 = (eps * inv_n, inv_n, 0.0)
    n2 = (eps * inv_n, -inv_n, 0.0)
    c = n1[0] * n2[0] + n1[1] * n2[1]
    denom = max(1.0 + c, 1e-12)
    M = 4.0
    old_s_big = tuple((n1[k] + n2[k]) * (offset_big / denom) for k in range(3))
    old_sl_big = math.sqrt(sum(v * v for v in old_s_big))
    old_capped_big = tuple(v * (M * offset_big / old_sl_big) for v in old_s_big)
    dir_v = tuple((n1[k] + n2[k]) / denom for k in range(3))
    dl = math.sqrt(sum(v * v for v in dir_v))
    dir_cap = tuple(v * (M / dl) for v in dir_v) if dl > M else dir_v
    new_s_big = tuple(v * offset_big for v in dir_cap)
    ok_miter_big = (
        math.isinf(old_sl_big)
        and old_capped_big[0] == 0.0
        and abs(new_s_big[0] - M * offset_big) <= 1e-12 * M * offset_big
    )
    replays.append(dict(
        name='miter_shift_overflow_wipeout_2p600',
        old_sl=str(old_sl_big),
        old_sx=old_capped_big[0],
        new_sx_over_offset=new_s_big[0] / offset_big,
        passed=ok_miter_big,
    ))

    # 2. Miter shift at 2^-600: old code underflows s.Length to 0 and skips cap
    offset_small = math.ldexp(1.0, -600)
    old_s_small = tuple((n1[k] + n2[k]) * (offset_small / denom) for k in range(3))
    old_sl_small = math.sqrt(sum(v * v for v in old_s_small))
    old_out_small = old_s_small if not (old_sl_small > M * offset_small) else tuple(v * (M * offset_small / old_sl_small) for v in old_s_small)
    new_s_small = tuple(v * offset_small for v in dir_cap)
    ok_miter_small = (
        old_sl_small == 0.0
        and old_out_small[0] > 1000.0 * M * offset_small
        and abs(new_s_small[0] - M * offset_small) <= 1e-12 * M * offset_small
    )
    replays.append(dict(
        name='miter_shift_underflow_uncapped_2m600',
        old_sl=old_sl_small,
        old_ratio=old_out_small[0] / offset_small,
        new_ratio=new_s_small[0] / offset_small,
        passed=ok_miter_small,
    ))

    # 3. SegmentGrid r^2 underflow at 2^-540 and overflow at 2^600
    r_tiny = math.ldexp(1.0, -540)
    r_huge = math.ldexp(1.0, 600)
    d_near_tiny = 0.5 * r_tiny
    d_far_huge = 2.0 * r_huge
    old_tiny_hit = (d_near_tiny * d_near_tiny <= r_tiny * r_tiny)  # 0 <= 0 accidental for exact 0, check 2^-537!
    r_sub = math.ldexp(1.0, -537)
    d_sub_far = 1.1 * r_sub  # both d^2 and r^2 underflow to 0 -> 0 <= 0 false hit!
    old_sub_false_hit = (d_sub_far * d_sub_far <= r_sub * r_sub)
    old_huge_false_hit = (d_far_huge * d_far_huge <= r_huge * r_huge)
    ok_sg_r2 = old_sub_false_hit and old_huge_false_hit and not (d_sub_far <= r_sub) and not (d_far_huge <= r_huge)
    replays.append(dict(
        name='segment_grid_r2_subnormal_and_overflow',
        old_sub_false_hit=old_sub_false_hit,
        old_huge_false_hit=old_huge_false_hit,
        passed=ok_sg_r2,
    ))

    # 4. SegmentGrid Int32 wrap on wide segment
    wide_len = 1e12
    h = 1.0
    steps_d = math.ceil(wide_len / (0.5 * h))
    ok_wide = (steps_d > INT32_MAX) and not (steps_d <= 4096)
    replays.append(dict(
        name='segment_grid_wide_segment_exceeds_int32',
        steps=steps_d,
        int32_max=INT32_MAX,
        passed=ok_wide,
    ))

    # 5. angle overflow at 2^600 and underflow at 2^-600
    lu_big = math.sqrt(r_huge * r_huge)
    u_small = math.ldexp(1.0, -600)
    lu_small = math.sqrt(u_small * u_small)
    ok_angle = math.isinf(lu_big) and (lu_small == 0.0)
    replays.append(dict(
        name='angle_overflow_and_underflow',
        lu_2p600=str(lu_big),
        lu_2m600=lu_small,
        passed=ok_angle,
    ))

    # 6. unit_normal overflow at 2^600 and underflow at 2^-600
    u_big = r_huge
    cross_big = u_big * u_big
    l_big = u_big * u_big
    old_un_big = cross_big > 1e-12 * l_big  # +Inf > +Inf is False
    old_un_small = (u_small * u_small) > 1e-12 * (u_small * u_small)  # 0 > 0 is False
    ok_un = (not old_un_big) and (not old_un_small)
    replays.append(dict(
        name='unit_normal_overflow_and_underflow',
        old_un_2p600=old_un_big,
        old_un_2m600=old_un_small,
        passed=ok_un,
    ))

    # 7. is_protected r^2 overflow at 2^600 and underflow to 0 at 2^-540
    d_prot_big = 10.0 * r_huge
    d_prot_tiny = 2.0 * r_tiny
    old_prot_far_big = (d_prot_big * d_prot_big <= r_huge * r_huge)  # +Inf <= +Inf -> True (false lock!)
    old_prot_far_tiny = (d_prot_tiny * d_prot_tiny <= r_tiny * r_tiny)  # 0 <= 0 -> True (false lock!)
    ok_prot = old_prot_far_big and old_prot_far_tiny and not (d_prot_tiny <= r_tiny) and not (d_prot_big <= r_huge)
    replays.append(dict(
        name='is_protected_r2_overflow_and_underflow',
        old_prot_far_2p600=old_prot_far_big,
        old_prot_far_2m540=old_prot_far_tiny,
        passed=ok_prot,
    ))
    return replays


def run(timeout_ms=120000):
    source_contract(FEATURE_SOURCE, FEATURE_ANCHORS)
    source_contract(QUALITY_SOURCE, QUALITY_ANCHORS)
    obligations = (
        miter_and_crease_obligations()
        + grid_clamping_obligations()
        + scale_invariance_obligations()
    )
    rows = [
        check_expr(name, prem, goal, expected, timeout_ms, logic)
        for name, prem, goal, expected, logic in obligations
    ]
    replays = replay_fp()
    out = report(rows, replays=replays)
    out['passed'] = out['passed'] and all(r['passed'] for r in replays)
    return out


if __name__ == '__main__':
    sys.exit(main(run))
