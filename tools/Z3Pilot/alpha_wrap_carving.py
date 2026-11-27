"""Local MeshAlphaWrap empty-ball range and parallel round-disjointness proofs.

Checks:
1. EmptyBallRadius normal-line minimization (Phi(f)^2 = R^2 + dist(0, [t_lo, t_hi])^2 >= R^2)
   and power-of-two rescaling invariance of the facet circumcenter formula.
2. Parallel carve_rounds_loop conflict-selection soundness (QF_UF): write-write disjointness
   in Phase 4, forward read-after-write disjointness (serial replay lemma), shared-outside
   carving reachability, cavity-boundary closure preventing a later insertion from deleting
   an earlier carving's outside neighbor, and three negative-control mutants proving each
   conflict guard is required.
3. Binary64 boundary replays for degree-5 overflow/underflow, thin-sliver w^2 underflow,
   hypot overflow, and subnormal squaring.
"""
from fractions import Fraction
import math

import z3

from check_pilot_obligations import check_expr, main, report, source_contract

WRAP_SOURCE = 'mesh_ops/MeshAlphaWrap.cs'
WRAP_ANCHORS = [
    ('bool round_marked(int c)', '// body(k) for k in [0, count):',
     '6592a9d5d3054eb4a199e2d2428e615e3a2c7e06ef31e6fc28d8790a5352b0fb'),
    ('if (roundInner[c] == roundMarkValue)', 'if (roundGates.Count == 0)',
     '41bb3ab0603d1f8089c24e597f537d6510669a0b4e0724ee66b3d71eda24dff9'),
    ('bool clash = round_marked(g.C) || round_marked(g.N);', "// (the evaluation's wall time,",
     '787305b7cafdc2a3e569ee51edbc326accdcf4835591da46eb0d43880fa091da'),
    ('void apply_gate(RoundGate g)', '// the gates the serial loop pushes',
     'dc031b18b52838b20f7608e2a96f6e4023d32534b2b07b49d5c509e7d9dda94e'),
    ('const double MinNormal', 'Vector3d clamp_center(',
     '6dc2d99da30c07cbbaad599284a28258234d1c6ad56fda4604dc71a3e998ca8c'),
]
DELAUNAY_SOURCE = 'comp_geom/Delaunay3.cs'
DELAUNAY_ANCHORS = [
    ('var seen = plan.Seen;', 'if (result != InsertResult.Inserted)',
     '1d14b68f44f6f96d3173b1ff5a1014e49421e0a3e3193d136c4cfd4677940071'),
]


def empty_ball_obligations(timeout_ms):
    R, t0, t1, t = z3.Reals('R t0 t1 t')
    t_lo = z3.If(t0 <= t1, t0, t1)
    t_hi = z3.If(t0 <= t1, t1, t0)
    # Closest point on [t_lo, t_hi] to 0, matching EmptyBallRadius's sign test.
    crosses = z3.And(z3.Not(t_lo > 0), z3.Not(t_hi < 0))
    abs_min = z3.If(z3.If(t0 >= 0, t0, -t0) <= z3.If(t1 >= 0, t1, -t1),
                    z3.If(t0 >= 0, t0, -t0), z3.If(t1 >= 0, t1, -t1))
    t_star_abs = z3.If(crosses, 0, abs_min)
    phi2 = R * R + t_star_abs * t_star_abs
    seg_premises = [R >= 0, t_lo <= t, t <= t_hi]

    # Power-of-two rescaling identity for FacetCircumcenter:
    # u = s * us, v = s * vs, w = s^2 * ws_vec, wn = ws_vec / ws (so w = (s^2 * ws) * wn).
    s, ws, num_unit, wn2 = z3.Reals('s ws num_unit wn2')
    scale_premises = [s > 0, ws > 0, wn2 > 0]
    lhs_num = (s**5) * ws * num_unit
    lhs_den = 2 * (s**4) * (ws**2) * wn2
    rhs_num = s * num_unit
    rhs_den = 2 * ws * wn2

    return [
        check_expr('empty_ball_premises_reachable', seg_premises,
                   z3.And(t_lo > 0, R * R + t * t > phi2), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('empty_ball_can_exceed_any_segment_point_radius', seg_premises,
                   phi2 > R * R + t * t, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('empty_ball_can_drop_below_circumradius', seg_premises,
                   phi2 < R * R, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('unclamped_circumradius_underestimates_one_sided_segment',
                   [R >= 0, t_lo > 0, t_lo <= t, t <= t_hi],
                   R * R < R * R + t * t, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('circumcenter_rescaling_premises_reachable', scale_premises,
                   num_unit != 0, 'sat', timeout_ms, 'QF_NRA'),
        check_expr('circumcenter_pow2_rescaling_can_change_exact_value', scale_premises,
                   lhs_num * rhs_den != rhs_num * lhs_den, 'unsat', timeout_ms, 'QF_NRA'),
    ]


def _round_model(drop_gn_check=False, drop_carve_mark=False, drop_reads_check=False):
    CARVE, INSERT, REQUEUE = 0, 1, 2
    k1, k2 = z3.Ints('k1 k2')
    c1, n1, c2, n2, x = z3.Ints('c1 n1 c2 n2 x')
    is_inside = z3.Function('is_inside', z3.IntSort(), z3.BoolSort())
    reads1 = z3.Function('reads1', z3.IntSort(), z3.BoolSort())
    writes1 = z3.Function('writes1', z3.IntSort(), z3.BoolSort())
    cav1 = z3.Function('cav1', z3.IntSort(), z3.BoolSort())
    reads2 = z3.Function('reads2', z3.IntSort(), z3.BoolSort())
    writes2 = z3.Function('writes2', z3.IntSort(), z3.BoolSort())
    cav2 = z3.Function('cav2', z3.IntSort(), z3.BoolSort())
    adj = z3.Function('adj', z3.IntSort(), z3.IntSort(), z3.BoolSort())

    cells = [x, c1, n1, c2, n2]
    premises = [
        z3.Or(k1 == CARVE, k1 == INSERT, k1 == REQUEUE),
        z3.Or(k2 == CARVE, k2 == INSERT, k2 == REQUEUE),
        c1 >= 0, c2 >= 0,
        is_inside(c1), is_inside(c2),
        z3.Implies(n1 >= 0, z3.Not(is_inside(n1))),
        z3.Implies(n2 >= 0, z3.Not(is_inside(n2))),
        # Phase 1: roundInner guarantees distinct inside cells.
        c1 != c2,
        # Facet adjacency between a gate's inside cell and outside neighbor.
        z3.Implies(n1 >= 0, z3.And(adj(c1, n1), adj(n1, c1))),
        z3.Implies(n2 >= 0, z3.And(adj(c2, n2), adj(n2, c2))),
    ]

    for z in cells:
        # Gate 1 read/write semantics (evaluate + apply_gate + Delaunay3.PlanInsert).
        premises.extend([
            z3.Implies(k1 == CARVE, z3.And(
                writes1(z) == (z == c1),
                reads1(z) == z3.Or(z == c1, z3.And(n1 >= 0, z == n1)),
                z3.Not(cav1(z)),
            )),
            z3.Implies(k1 == REQUEUE, z3.And(
                z3.Not(writes1(z)),
                reads1(z) == z3.Or(z == c1, z3.And(n1 >= 0, z == n1)),
                z3.Not(cav1(z)),
            )),
            z3.Implies(k1 == INSERT, z3.And(
                z3.Implies(writes1(z), reads1(z)),
                z3.Implies(cav1(z), writes1(z)),
            )),
            # Gate 2 read/write semantics.
            z3.Implies(k2 == CARVE, z3.And(
                writes2(z) == (z == c2),
                reads2(z) == z3.Or(z == c2, z3.And(n2 >= 0, z == n2)),
                z3.Not(cav2(z)),
            )),
            z3.Implies(k2 == REQUEUE, z3.And(
                z3.Not(writes2(z)),
                reads2(z) == z3.Or(z == c2, z3.And(n2 >= 0, z == n2)),
                z3.Not(cav2(z)),
            )),
            z3.Implies(k2 == INSERT, z3.And(
                z3.Implies(writes2(z), reads2(z)),
                z3.Implies(cav2(z), writes2(z)),
            )),
        ])

    # Delaunay3.PlanInsert cavity-boundary closure (Delaunay3.cs:760-766):
    # every neighbor of a cavity cell is in plan.Reads.
    for a_cell in cells:
        for b_cell in cells:
            premises.append(z3.Implies(z3.And(k1 == INSERT, cav1(a_cell), adj(a_cell, b_cell)), reads1(b_cell)))
            premises.append(z3.Implies(z3.And(k2 == INSERT, cav2(a_cell), adj(a_cell, b_cell)), reads2(b_cell)))

    # Phase 3 marking after g1 is kept (MeshAlphaWrap.cs:1019-1030).
    def marked(z):
        m_ins = z3.And(k1 == INSERT, z3.Or(z == c1, z3.And(n1 >= 0, z == n1), reads1(z)))
        m_carve = z3.BoolVal(False) if drop_carve_mark else z3.And(k1 == CARVE, z == c1)
        return z3.Or(m_ins, m_carve)

    # Phase 3 selection: g2 is kept only if not clashing (MeshAlphaWrap.cs:992-996).
    premises.append(z3.Not(marked(c2)))
    if not drop_gn_check:
        premises.append(z3.Implies(n2 >= 0, z3.Not(marked(n2))))
    if not drop_reads_check:
        for z in cells:
            premises.append(z3.Implies(z3.And(k2 == INSERT, reads2(z)), z3.Not(marked(z))))

    return premises, {
        'CARVE': CARVE, 'INSERT': INSERT, 'REQUEUE': REQUEUE,
        'k1': k1, 'k2': k2, 'c1': c1, 'n1': n1, 'c2': c2, 'n2': n2, 'x': x,
        'reads1': reads1, 'writes1': writes1, 'cav1': cav1,
        'reads2': reads2, 'writes2': writes2, 'cav2': cav2,
    }


def round_disjointness_obligations(timeout_ms):
    p, v = _round_model()
    p_no_gn, v_no_gn = _round_model(drop_gn_check=True)
    p_no_cm, v_no_cm = _round_model(drop_carve_mark=True)
    p_no_rd, v_no_rd = _round_model(drop_reads_check=True)

    return [
        check_expr('round_shared_outside_carvings_reachable', p,
                   z3.And(v['k1'] == v['CARVE'], v['k2'] == v['CARVE'], v['n1'] == v['n2'], v['n1'] >= 0),
                   'sat', timeout_ms, 'QF_UFLIA'),
        check_expr('round_insert_pair_reachable', p,
                   z3.And(v['k1'] == v['INSERT'], v['k2'] == v['INSERT'], v['writes1'](v['c1']), v['writes2'](v['c2'])),
                   'sat', timeout_ms, 'QF_UFLIA'),
        check_expr('parallel_apply_can_have_write_write_race', p,
                   z3.And(v['writes1'](v['x']), v['writes2'](v['x'])),
                   'unsat', timeout_ms, 'QF_UFLIA'),
        check_expr('earlier_kept_write_can_hit_later_kept_eval_read', p,
                   z3.And(v['writes1'](v['x']), v['reads2'](v['x'])),
                   'unsat', timeout_ms, 'QF_UFLIA'),
        check_expr('later_insert_cavity_can_delete_earlier_carve_outside_neighbor', p,
                   z3.And(v['k1'] == v['CARVE'], v['k2'] == v['INSERT'], v['n1'] >= 0, v['cav2'](v['n1'])),
                   'unsat', timeout_ms, 'QF_UFLIA'),
        check_expr('mutant_drop_round_marked_gN_allows_raw_conflict', p_no_gn,
                   z3.And(v_no_gn['writes1'](v_no_gn['x']), v_no_gn['reads2'](v_no_gn['x'])),
                   'sat', timeout_ms, 'QF_UFLIA'),
        check_expr('mutant_drop_carve_mark_gC_allows_raw_conflict', p_no_cm,
                   z3.And(v_no_cm['writes1'](v_no_cm['x']), v_no_cm['reads2'](v_no_cm['x'])),
                   'sat', timeout_ms, 'QF_UFLIA'),
        check_expr('mutant_drop_insert_reads_check_allows_write_write_race', p_no_rd,
                   z3.And(v_no_rd['writes1'](v_no_rd['x']), v_no_rd['writes2'](v_no_rd['x'])),
                   'sat', timeout_ms, 'QF_UFLIA'),
    ]


def replay_fp():
    f64, rnd = z3.Float64(), z3.RNE()
    fp = lambda x: z3.FPVal(x, f64)
    same = lambda expr, value: z3.is_true(z3.simplify(z3.fpEQ(expr, fp(value))))

    # 1. Degree-5 numerator overflow at s = 2^210: u^2 = 2^420, v x w = 2^630 -> product = +inf.
    s_big = 2.0**210
    u2_big = z3.fpMul(rnd, fp(s_big), fp(s_big))
    vxw_big = z3.fpMul(rnd, fp(s_big), u2_big)
    num_big = z3.fpMul(rnd, u2_big, vxw_big)

    # 2. Degree-5 numerator underflow at s = 2^-220: u^2 = 2^-440, v x w = 2^-660 -> product = 0.0.
    s_tiny = 2.0**-220
    u2_tiny = z3.fpMul(rnd, fp(s_tiny), fp(s_tiny))
    vxw_tiny = z3.fpMul(rnd, fp(s_tiny), u2_tiny)
    num_tiny = z3.fpMul(rnd, u2_tiny, vxw_tiny)

    # 3. Thin sliver w^2 underflow at h = 2^-540: h^2 = 0.0 -> 0/0 = NaN on unscaled formula.
    h_sliv = 2.0**-540
    w2_sliv = z3.fpMul(rnd, fp(h_sliv), fp(h_sliv))
    nan_sliv = z3.fpDiv(rnd, fp(0.0), w2_sliv)

    # 4. Huge hypot overflow at R = t = 2^600: R^2 + t^2 = +inf while scaled hypot stays finite.
    r_big = 2.0**600
    r_big2 = z3.fpMul(rnd, fp(r_big), fp(r_big))

    # 5. Subnormal squaring at R = sqrt(1.499) * 2^-537 triggers 4 * MinNormal guard.
    r_sub = math.sqrt(1.499) * 2.0**-537
    r_sub2 = z3.fpMul(rnd, fp(r_sub), fp(r_sub))

    checks = {
        'degree5_numerator_overflows_at_2p210':
            same(num_big, math.inf) and Fraction(s_big) > Fraction(2**100),
        'degree5_numerator_underflows_at_2m220':
            same(num_tiny, 0.0) and Fraction(s_tiny) < Fraction(1, 2**100),
        'thin_sliver_w2_underflows_to_nan_at_2m540':
            same(w2_sliv, 0.0) and z3.is_true(z3.simplify(z3.fpIsNaN(nan_sliv))),
        'hypot_squares_overflow_at_2p600':
            same(r_big2, math.inf),
        'subnormal_hypot_square_caught_by_4x_min_normal':
            z3.is_true(z3.simplify(z3.fpGT(r_sub2, fp(0.0))))
            and z3.is_true(z3.simplify(z3.fpLT(r_sub2, fp(4.0 * 2.0**-1022)))),
    }
    witnesses = {
        'degree5_overflow': {'s': s_big},
        'degree5_underflow': {'s': s_tiny},
        'sliver_w2_underflow': {'h': h_sliv, 'exact_yc': -2.0**537},
        'hypot_overflow': {'R': r_big},
        'hypot_subnormal': {'R': r_sub},
    }
    return checks, witnesses


def run(timeout_ms=8000):
    source_contract(WRAP_SOURCE, WRAP_ANCHORS)
    source_contract(DELAUNAY_SOURCE, DELAUNAY_ANCHORS)
    rows = empty_ball_obligations(timeout_ms) + round_disjointness_obligations(timeout_ms)
    replays, witnesses = replay_fp()
    result = report(rows, replays=replays, witnesses=witnesses)
    result['passed'] &= all(replays.values())
    return result


if __name__ == '__main__':
    raise SystemExit(main(run))
