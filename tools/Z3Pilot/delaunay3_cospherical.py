"""Local Z3 obligations, cospherical complexity witnesses, and binary64 replays for Delaunay3.

Formally verifies the star-shape cavity theorem, cospherical cavity blow-up bounds,
Portaneri et al.'s cospherical ball-center breaker guarantee boundary, and extreme-scale
Circumcenter / CellVolume / WithinSeparation invariants of comp_geom/Delaunay3.cs
(docs/design/ALPHA_WRAP_PAPER_NOTES.md §1, docs/design/ALPHA_WRAP_ROUNDS.md §4.3 & §6):

1. Exact Delaunay strict cavity is ALWAYS star-shaped (Delaunay3.cs:729-731):
   - WLOG by rigid motion + positive scaling, place a boundary facet (a, b, c) in z = 0 with
     unit circumcircle centered at (0, 0, 0). Adjacent tetrahedra T1 (z < 0, sphere center
     (0, 0, h1)) and T2 (z > 0, sphere center (0, 0, h2)) satisfy the local Delaunay condition
     iff h2 >= h1 (equality in the cospherical case).
   - For any point p = (px, py, pz) with r2 = px^2 + py^2 >= 0, power w.r.t. T_k's sphere is
     Pi_k(p) = r2 + pz^2 - 1 - 2*h_k*pz.
   - Proves (QF_NRA, 4 vars): [h2 >= h1, Pi1 < 0, Pi2 >= 0] => pz < 0 ALWAYS (pz >= 0 is UNSAT),
     so p sees every boundary facet strictly from inside and the star-shape growth loop never
     adds a cell in an exact Delaunay triangulation.
   - Proves (QF_NRA): when T1 and T2 are cospherical (h1 == h2), they always have identical
     strict cavity membership (Xor(Pi1 < 0, Pi2 < 0) is UNSAT) — explaining why cospherical domes
     collapse all at once.
   - Negative control (SAT): dropping local Delaunay (h2 < h1) allows pz >= 0 (star-shape growth
     would be needed on a non-Delaunay facet).
2. Cospherical cavity Euler formula & quadratic blow-up (ALPHA_WRAP_ROUNDS.md:159):
   - For a topological 3-ball cavity of T tetrahedra, F boundary facets, F_int interior facets,
     V_b boundary vertices (V_i = 0 interior vertices), E_b = 3*V_b - 6 boundary edges, and
     E_i interior edges: Euler's formula V_b - (E_b + E_i) + (F + F_int) - T = 1 and facet
     incidence 4*T = 2*F_int + F imply F == 2*T + 2 - 2*E_i (UNSAT mismatch).
   - Proves F < T is reachable (SAT, motivating AssignInsertIds's free_cell loop).
   - Proves that on a cospherical cavity with Theta(V_b^2) interior chords (E_i = V_b*(V_b-1)/2 - E_b),
     T = (V_b^2 - 5*V_b + 6) / 2 exceeds c * F for ANY linear constant c >= 1 once V_b >= 4*c + 5
     (UNSAT for T <= c * F, explaining the Thingi10K 1-hour cospherical timeout).
3. Portaneri et al. (2022) cospherical ball-center breaker guarantee boundary (ALPHA_WRAP_PAPER_NOTES.md §1):
   - For an empty Delaunay ball B(c, R) with R > alpha > 0, offset delta > 0, every existing vertex v
     has distance d_v >= R > alpha >= min(alpha, delta) -> inserting c ALWAYS preserves vertex
     separation (UNSAT to violate separation).
   - Distance to input d_c = dist(c, I): if d_c >= delta, c stays strictly outside the open offset
     volume (UNSAT for d_c < delta) while relaxing on-surface placement when d_c > delta (SAT);
     if unguarded (d_c < delta, even d_c == 0), inserting c places a vertex inside the offset volume
     or on the input itself (SAT counterexample answering ALPHA_WRAP_PAPER_NOTES.md:67).
4. Circumcenter & separation scale invariance + binary64 replays:
   - Proves positive-scale homogeneity of Circumcenter (scaling u, v, w by 1/s and scaling the
     offset back by s is exact in QF_NRA, with unscaled negative control).
   - 4 binary64 replays mirroring Delaunay3.Circumcenter (2^±270, 2^±600, pre==1), CellVolume
     (2^520 * 2^520 * 2^-1000 right-angle sliver), and WithinSeparation (2^-540 vs 2^-545; 2^520 vs 2^515).
"""
import math
import struct
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

DT_SOURCE = 'comp_geom/Delaunay3.cs'

DT_ANCHORS = [
    ('const double MinNormal = 2.2250738585072014e-308;', 'int new_cell(',
     '86ba2845075e8d6b574ca7b907d17b4485ab1097bddf63dd07839f753f205fd8'),
    ('// grow the cavity until p sees every boundary facet strictly from inside (star-shaped);', '// every vertex of the cavity must stay',
     '8ecd35b7343fdac08cec2327da39309bafabc68947a7e19287dfab1978dfbfc3'),
    ('// the nearest vertex of p is a vertex of its conflict region', 'if (support) {',
     '54c8798827129d31ab8de58f653653472d7f93baecf0dc62382b29d8af750238'),
    ('// (the star-shape growth of Insert, kept for parity with it:', '// the read set: the cavity and every cell adjacent to it',
     'dfa7956e10b75d838023cdbbbcaec9e4f1fabf00291546b36f739ba7a1d52ec2'),
    ('if (!vset.Contains(cv[4 * c + i])) return InsertResult.Failed;', 'foreach (var (c, i) in boundary) {',
     '036c373fcc8b899e2cd989fa2562b885279d0e6c629e8dc0af9107eb5370bf7e'),
    ('internal void AssignInsertIds(InsertPlan plan)', 'internal void ApplyInsert(InsertPlan plan)',
     'e35eb1673995bb5e3923e73a8261d4d2525f330b993e826dd7f613bcbdc0d19e'),
]


def star_shape_and_cospherical_obligations():
    """Prove exact Delaunay cavity star-shapedness and cospherical co-membership in QF_NRA (4 vars)."""
    rows = []
    h1, h2, pz, r2 = z3.Reals('h1 h2 pz r2')
    # Power of p = (px, py, pz) with r2 = px^2 + py^2 w.r.t. sphere centered at (0, 0, h_k) of radius sqrt(1 + h_k^2)
    pi1 = r2 + pz * pz - 1 - 2 * h1 * pz
    pi2 = r2 + pz * pz - 1 - 2 * h2 * pz

    # Local Delaunay across facet z = 0 (T1 on z < 0, T2 on z > 0): h2 >= h1
    del_prem = [r2 >= 0, h2 >= h1, pi1 < 0, pi2 >= 0]
    rows.append(('star_shape_assumptions_sat', del_prem, pz == -1, 'sat', 'QF_NRA'))
    rows.append((
        'exact_delaunay_cavity_always_star_shaped',
        del_prem,
        pz >= 0,
        'unsat',
        'QF_NRA',
    ))

    # Cospherical adjacent tets (h1 == h2) have identical strict cavity membership (both in or both out)
    cosph_prem = [r2 >= 0, h1 == h2]
    rows.append(('cospherical_assumptions_sat', cosph_prem, z3.And(pi1 < 0, pi2 < 0), 'sat', 'QF_NRA'))
    rows.append((
        'cospherical_adjacent_tets_same_cavity_membership',
        cosph_prem,
        z3.Xor(pi1 < 0, pi2 < 0),
        'unsat',
        'QF_NRA',
    ))

    # Negative control: violating local Delaunay (h2 < h1) allows pz >= 0 (non-star-shaped strict cavity)
    rows.append((
        'non_delaunay_facet_needs_star_shape_growth_negative_control',
        [r2 >= 0, h2 < h1, pi1 < 0, pi2 >= 0],
        pz >= 0,
        'sat',
        'QF_NRA',
    ))
    return rows


def cavity_euler_and_blowup_obligations():
    """Prove cavity Euler identity F = 2T + 2 - 2*E_i, F < T shrinkage, and quadratic cospherical blow-up."""
    rows = []
    T, F, F_int, V_b, E_b, E_i = z3.Ints('T F F_int V_b E_b E_i')
    euler_prem = [
        V_b >= 4, T >= 1, F >= 4, F_int >= 0, E_i >= 0,
        E_b == 3 * V_b - 6,
        F == 2 * V_b - 4,
        4 * T == 2 * F_int + F,
        V_b - (E_b + E_i) + (F + F_int) - T == 1,  # 3-ball Euler characteristic with V_i == 0
    ]
    rows.append(('cavity_euler_assumptions_sat', euler_prem, z3.And(V_b == 14, F < T), 'sat', 'QF_LIA'))
    rows.append((
        'cavity_euler_formula_exact',
        euler_prem,
        F != 2 * T + 2 - 2 * E_i,
        'unsat',
        'QF_LIA',
    ))

    # Quadratic cospherical blow-up: with E_i = V_b*(V_b-1)/2 - (3*V_b - 6) interior chords,
    # 2*T = F + 2*E_i - 2 = V_b^2 - 5*V_b + 6 -> exceeds c * F for any linear constant c >= 1 once V_b >= 4*c + 5
    c, Vb, Tq, Fq = z3.Ints('c Vb Tq Fq')
    quad_prem = [
        c >= 1,
        Vb >= 4 * c + 5,
        Fq == 2 * Vb - 4,
        2 * Tq == Vb * Vb - 5 * Vb + 6,
    ]
    rows.append(('cospherical_blowup_assumptions_sat', quad_prem, z3.And(c == 5, Vb == 25), 'sat', 'QF_NIA'))
    rows.append((
        'cospherical_cavity_beats_any_linear_boundary_bound',
        quad_prem,
        Tq <= c * Fq,
        'unsat',
        'QF_NIA',
    ))
    return rows


def ball_center_breaker_obligations():
    """Analyze Portaneri et al. (2022) cospherical ball-center breaker guarantee boundary (ALPHA_WRAP_PAPER_NOTES.md §1)."""
    rows = []
    alpha, delta, R, dv, dc = z3.Reals('alpha delta R dv dc')
    sep = z3.If(alpha <= delta, alpha, delta)

    # 1. Empty Delaunay ball B(c, R) with R > alpha: every existing vertex v has dv >= R > min(alpha, delta)
    sep_prem = [alpha > 0, delta > 0, R > alpha, dv >= R]
    rows.append(('ball_center_sep_assumptions_sat', sep_prem, delta < alpha, 'sat', 'QF_NRA'))
    rows.append((
        'ball_center_preserves_separation',
        sep_prem,
        dv < sep,
        'unsat',
        'QF_NRA',
    ))

    # 2. Strict enclosure / offset-band boundary:
    #    - Guarded (dc >= delta): c stays outside the open offset volume (dc < delta is UNSAT),
    #      while dc > delta (SAT) relaxes the "vertices on I_delta" promise.
    #    - Unguarded (no dc >= delta check): SAT counterexample with dc == 0 (center ON the input!),
    #      violating strict enclosure / no-contact if c reaches the output surface.
    guard_prem = [alpha > 0, delta > 0, R > alpha, dc >= delta]
    rows.append(('ball_center_guarded_off_surface_witness', guard_prem, dc > delta, 'sat', 'QF_NRA'))
    rows.append((
        'ball_center_guarded_stays_outside_offset_volume',
        guard_prem,
        dc < delta,
        'unsat',
        'QF_NRA',
    ))
    rows.append((
        'ball_center_unguarded_can_hit_input_negative_control',
        [alpha > 0, delta > 0, R > alpha, dc >= 0],
        dc == 0,
        'sat',
        'QF_NRA',
    ))
    return rows


def circumcenter_scale_obligations():
    """Verify scale homogeneity of Circumcenter's power-of-two scaling (QF_NRA, 5 vars)."""
    rows = []
    # Model 1D component of offset = num / den where num is homogeneous degree 4 and den is degree 3:
    # scaled num' = num / s^4, den' = den / s^3 => s * (num' / den') == num / den.
    s, num, den, off, off_w = z3.Reals('s num den off off_w')
    poly_prem = [
        s > 0, den != 0,
        off * den == num,
        off_w * den * s == num,  # equivalent to off_w * (den / s^3) == num / s^4 (multiply by s^4)
    ]
    rows.append(('circumcenter_scale_assumptions_sat', poly_prem, z3.And(s == 2, num == 4, den == 2), 'sat', 'QF_NRA'))
    rows.append((
        'circumcenter_pow2_scaling_exact',
        poly_prem,
        s * off_w != off,
        'unsat',
        'QF_NRA',
    ))
    # Negative control: forgetting to scale the offset back by s breaks equality
    rows.append((
        'circumcenter_unscaled_offset_negative_control',
        poly_prem,
        off_w != off,
        'sat',
        'QF_NRA',
    ))
    return rows


# ---- Python binary64 mirrors of Delaunay3 helpers for replay_fp() ----

_MIN_NORMAL = 2.2250738585072014e-308
_FAST_MIN = 7.888609052210118e-31
_FAST_MAX = 1.2676506002282294e30


def _pow2_exp(v):
    bits = struct.unpack('<Q', struct.pack('<d', v))[0]
    biased = (bits >> 52) & 0x7FF
    if biased != 0:
        return biased - 1023
    sbits = struct.unpack('<Q', struct.pack('<d', v * float(1 << 54)))[0]
    return ((sbits >> 52) & 0x7FF) - 1023 - 54


def _vsub(p, q):
    return (p[0] - q[0], p[1] - q[1], p[2] - q[2])


def _vadd(p, q):
    return (p[0] + q[0], p[1] + q[1], p[2] + q[2])


def _vcross(u, v):
    return (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])


def _vdot(u, v):
    return u[0] * v[0] + u[1] * v[1] + u[2] * v[2]


def _vlen2(u):
    return u[0] * u[0] + u[1] * u[1] + u[2] * u[2]


def _vfinite(u):
    return math.isfinite(u[0]) and math.isfinite(u[1]) and math.isfinite(u[2])


def _vmaxabs(u):
    if any(math.isnan(c) for c in u):
        return float('nan')
    return max(abs(u[0]), abs(u[1]), abs(u[2]))


def _vscale(u, n):
    return (math.ldexp(u[0], n), math.ldexp(u[1], n), math.ldexp(u[2], n))


def _old_circumcenter(a, b, c, d):
    u, v, w = _vsub(b, a), _vsub(c, a), _vsub(d, a)
    vw, wu, uv = _vcross(v, w), _vcross(w, u), _vcross(u, v)
    den = 2.0 * _vdot(u, vw)
    u2, v2, w2 = _vlen2(u), _vlen2(v), _vlen2(w)
    if den == 0.0:
        return (float('nan'), float('nan'), float('nan'))
    num = (u2 * vw[0] + v2 * wu[0] + w2 * uv[0],
           u2 * vw[1] + v2 * wu[1] + w2 * uv[1],
           u2 * vw[2] + v2 * wu[2] + w2 * uv[2])
    return (a[0] + num[0] / den, a[1] + num[1] / den, a[2] + num[2] / den)


def _new_circumcenter(a, b, c, d):
    if not (_vfinite(a) and _vfinite(b) and _vfinite(c) and _vfinite(d)):
        return (float('nan'), float('nan'), float('nan'))
    u, v, w = _vsub(b, a), _vsub(c, a), _vsub(d, a)
    pre = 0
    if not (_vfinite(u) and _vfinite(v) and _vfinite(w)):
        pre = 1
        ha = (0.5 * a[0], 0.5 * a[1], 0.5 * a[2])
        u = (0.5 * b[0] - ha[0], 0.5 * b[1] - ha[1], 0.5 * b[2] - ha[2])
        v = (0.5 * c[0] - ha[0], 0.5 * c[1] - ha[1], 0.5 * c[2] - ha[2])
        w = (0.5 * d[0] - ha[0], 0.5 * d[1] - ha[1], 0.5 * d[2] - ha[2])
    m = max(_vmaxabs(u), _vmaxabs(v), _vmaxabs(w))
    if not (m > 0.0):
        return (float('nan'), float('nan'), float('nan'))
    exp = _pow2_exp(m)
    us, vs, ws = _vscale(u, -exp), _vscale(v, -exp), _vscale(w, -exp)
    vw, wu, uv = _vcross(vs, ws), _vcross(ws, us), _vcross(us, vs)
    den = 2.0 * _vdot(us, vw)
    if den == 0.0:
        return (float('nan'), float('nan'), float('nan'))
    u2, v2, w2 = _vlen2(us), _vlen2(vs), _vlen2(ws)
    off = ((u2 * vw[0] + v2 * wu[0] + w2 * uv[0]) / den,
           (u2 * vw[1] + v2 * wu[1] + w2 * uv[1]) / den,
           (u2 * vw[2] + v2 * wu[2] + w2 * uv[2]) / den)
    return _vadd(a, _vscale(off, exp + pre))


def _new_cell_volume(a, b, d, e):
    u, v, w = _vsub(b, a), _vsub(d, a), _vsub(e, a)
    eu, ev, ew = _pow2_exp(_vmaxabs(u)), _pow2_exp(_vmaxabs(v)), _pow2_exp(_vmaxabs(w))
    us, vs, ws = _vscale(u, -eu), _vscale(v, -ev), _vscale(w, -ew)
    return math.ldexp(_vdot(_vcross(us, vs), ws) / 6.0, eu + ev + ew)


def _new_within_separation(a, b, min_sep):
    if math.isnan(min_sep) or min_sep < 0.0:
        return False
    if min_sep == 0.0:
        return a == b
    min_sqr = min_sep * min_sep
    d = _vsub(a, b)
    d2 = _vlen2(d)
    if (min_sqr >= 4.0 * _MIN_NORMAL and not math.isinf(min_sqr)
            and d2 >= 4.0 * _MIN_NORMAL and not math.isinf(d2)):
        return d2 <= min_sqr
    pre = 0
    if not _vfinite(d):
        pre = 1
        d = (0.5 * a[0] - 0.5 * b[0], 0.5 * a[1] - 0.5 * b[1], 0.5 * a[2] - 0.5 * b[2])
    m = _vmaxabs(d)
    if m == 0.0:
        return 0.0 <= min_sep
    e = _pow2_exp(m)
    ds = _vscale(d, -e)
    return math.ldexp(math.sqrt(_vlen2(ds)), e + pre) <= min_sep


def replay_fp():
    replays = []

    # 1. Circumcenter at 2^270 and 2^-270: degree-4 s^4 (2^+-1080) overflows/underflows on old formula; scaled mirror is exact
    s_hi = math.ldexp(1.0, 270)
    s_lo = math.ldexp(1.0, -270)
    old_hi = _old_circumcenter((0.0, 0.0, 0.0), (s_hi, 0.0, 0.0), (0.0, s_hi, 0.0), (0.0, 0.0, s_hi))
    new_hi = _new_circumcenter((0.0, 0.0, 0.0), (s_hi, 0.0, 0.0), (0.0, s_hi, 0.0), (0.0, 0.0, s_hi))
    old_lo = _old_circumcenter((0.0, 0.0, 0.0), (s_lo, 0.0, 0.0), (0.0, s_lo, 0.0), (0.0, 0.0, s_lo))
    new_lo = _new_circumcenter((0.0, 0.0, 0.0), (s_lo, 0.0, 0.0), (0.0, s_lo, 0.0), (0.0, 0.0, s_lo))
    replays.append(dict(
        name='circumcenter_2p270_and_2m270_replay',
        old_hi_failed=not _vfinite(old_hi),
        old_lo_failed=(old_lo[0] != math.ldexp(0.5, -270)),
        new_hi_exact=(new_hi == (math.ldexp(0.5, 270),) * 3),
        new_lo_exact=(new_lo == (math.ldexp(0.5, -270),) * 3),
        passed=(not _vfinite(old_hi) and old_lo[0] != math.ldexp(0.5, -270)
                and new_hi == (math.ldexp(0.5, 270),) * 3 and new_lo == (math.ldexp(0.5, -270),) * 3),
    ))

    # 2. Circumcenter pre == 1 ([-9e307, +9e307]): raw b - a overflows; half-scaled mirror gives (0, 0, 0)
    big = 9e307
    verts = ((-big, -big, -big), (big, big, -big), (big, -big, big), (-big, big, big))
    old_pre1 = _old_circumcenter(*verts)
    new_pre1 = _new_circumcenter(*verts)
    replays.append(dict(
        name='circumcenter_pre1_overflow_replay',
        old_failed=not _vfinite(old_pre1),
        new_origin=(new_pre1 == (0.0, 0.0, 0.0)),
        passed=(not _vfinite(old_pre1)) and (new_pre1 == (0.0, 0.0, 0.0)),
    ))

    # 3. CellVolume on anisotropic right-angle sliver (2^520 * 2^520 * 2^-1000 / 6 = 2^40 / 6):
    #    old Cross overflows to +Inf -> Inf; per-edge Pow2Exp scaling returns exact 2^40 / 6!
    a = (0.0, 0.0, 0.0)
    b = (math.ldexp(1.0, 520), 0.0, 0.0)
    d = (0.0, math.ldexp(1.0, 520), 0.0)
    e = (0.0, 0.0, math.ldexp(1.0, -1000))
    old_vol = _vdot(_vcross(_vsub(b, a), _vsub(d, a)), _vsub(e, a)) / 6.0
    new_vol = _new_cell_volume(a, b, d, e)
    expected_vol = math.ldexp(1.0 / 6.0, 40)
    replays.append(dict(
        name='cell_volume_anisotropic_sliver_replay',
        old_inf=math.isinf(old_vol),
        new_exact=(new_vol == expected_vol),
        passed=math.isinf(old_vol) and (new_vol == expected_vol),
    ))

    # 4. WithinSeparation at tiny (2^-540 vs 2^-545) and huge (2^520 vs 2^515) scales:
    #    old d2 <= minSqr gives 0 <= 0 (True) and Inf <= Inf (True); new SafeDistance returns False on both!
    c_tiny = math.ldexp(1.0, -540)
    sep_tiny = math.ldexp(1.0, -545)
    old_tiny_reject = _vlen2((c_tiny, c_tiny, c_tiny)) <= (sep_tiny * sep_tiny)
    new_tiny_reject = _new_within_separation((0.0, 0.0, 0.0), (c_tiny, c_tiny, c_tiny), sep_tiny)
    c_huge = math.ldexp(1.0, 520)
    sep_huge = math.ldexp(1.0, 515)
    old_huge_reject = _vlen2((c_huge, 0.0, 0.0)) <= (sep_huge * sep_huge)
    new_huge_reject = _new_within_separation((0.0, 0.0, 0.0), (c_huge, 0.0, 0.0), sep_huge)
    replays.append(dict(
        name='within_separation_underflow_and_overflow_replay',
        old_tiny_false_reject=old_tiny_reject,
        new_tiny_reject=new_tiny_reject,
        old_huge_false_reject=old_huge_reject,
        new_huge_reject=new_huge_reject,
        passed=old_tiny_reject and (not new_tiny_reject) and old_huge_reject and (not new_huge_reject),
    ))
    return replays


def run(timeout_ms=120000):
    source_contract(DT_SOURCE, DT_ANCHORS)
    obligations = (
        star_shape_and_cospherical_obligations()
        + cavity_euler_and_blowup_obligations()
        + ball_center_breaker_obligations()
        + circumcenter_scale_obligations()
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
