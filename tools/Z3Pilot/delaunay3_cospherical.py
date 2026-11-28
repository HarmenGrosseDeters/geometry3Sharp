"""Local Z3 obligations, cospherical complexity witnesses, and binary64 replays for Delaunay3.

Formally verifies the star-shape cavity theorem, cospherical cavity blow-up bounds,
Portaneri et al.'s cospherical ball-center breaker guarantee boundary, and extreme-scale
Circumcenter / CellVolume / WithinSeparation invariants of comp_geom/Delaunay3.cs
(docs/design/ALPHA_WRAP_PAPER_NOTES.md §1, docs/design/ALPHA_WRAP_ROUNDS.md §4.3 & §6):

1. Exact Delaunay strict cavity is ALWAYS star-shaped (Delaunay3.cs:615-616 & 879-881):
   - WLOG by rigid motion + positive scaling, place a boundary facet (a, b, c) in z = 0 with
     unit circumcircle centered at (0, 0, 0). Adjacent tetrahedra T1 (z < 0, sphere center
     (0, 0, h1)) and T2 (z > 0, sphere center (0, 0, h2)) satisfy the local Delaunay condition
     iff h2 >= h1 (equality in the cospherical case).
   - For any point p = (px, py, pz) with r2 = px^2 + py^2 >= 0, power w.r.t. T_k's sphere is
     Pi_k(p) = r2 + pz^2 - 1 - 2*h_k*pz.
   - Proves (QF_NRA, 4 vars): [h2 >= h1, Pi1 < 0, Pi2 >= 0] => pz < 0 ALWAYS (pz >= 0 is UNSAT),
     so p sees every boundary facet strictly from inside and the star-shape growth loop never
     adds a cell in an exact Delaunay triangulation (box-boundary facets are already excluded by
     the strict Box.Min < p < Box.Max guard at Delaunay3.cs:587 & 851).
   - Proves (QF_NRA): when T1 and T2 are cospherical (h1 == h2), they always have identical
     strict cavity membership (Xor(Pi1 < 0, Pi2 < 0) is UNSAT) — explaining why cospherical domes
     collapse all at once.
   - Negative control (SAT): dropping local Delaunay (h2 < h1) allows pz >= 0 (star-shape growth
     would be needed on a non-Delaunay facet).
2. Cospherical cavity Euler formula & quadratic blow-up (ALPHA_WRAP_ROUNDS.md:159):
   - For a topological 3-ball cavity of T tetrahedra, F boundary facets, F_int interior facets,
     V_b boundary vertices (V_i = 0 interior vertices), E_b = 3*V_b - 6 boundary edges, and
     E_i interior edges: Euler's formula V_b - (E_b + E_i) + (F + F_int) - T = 1 and facet
     incidence 4*T = 2*F_int + F imply F == 2*T + 2 - 2*E_i (UNSAT mismatch; SAT negative control
     when V_i >= 1).
   - Proves F < T is reachable (SAT, motivating AssignInsertIds's free_cell loop).
   - Derives 2*T == V_b^2 - 5*V_b + 6 directly inside Z3 from the Euler premises plus the complete
     chord count 2*E_i == V_b*(V_b - 1) - 2*E_b, and proves T exceeds c * F for ANY linear constant
     c >= 1 once V_b >= 4*c + 5 (UNSAT for T <= c * F; SAT negative control without V_b >= 4*c + 5),
     explaining the Thingi10K 1-hour cospherical timeout.
3. Portaneri et al. (2022) cospherical ball-center breaker guarantee boundary (ALPHA_WRAP_PAPER_NOTES.md §1):
   - In QF_LRA: for an empty Delaunay ball B(c, R) with R > alpha > 0, offset delta > 0, every
     existing vertex v has d_v >= R > alpha >= min(alpha, delta) -> inserting c ALWAYS preserves
     vertex separation (UNSAT to violate; SAT negative control when R <= alpha).
   - Geometric triangle-inequality clearance from every point x inside the open offset ball
     (|x - q_I| < delta, dc <= d_cx + d_xq): if dc >= delta, then d_cx > 0 ALWAYS (d_cx <= 0 is
     UNSAT), while dc > delta (SAT) relaxes on-surface placement; if unguarded (dc >= 0 only),
     d_cx == 0 with d_xq == 0 (dc == 0, center ON the input) is SAT, answering
     ALPHA_WRAP_PAPER_NOTES.md:67.
4. Per-edge Circumcenter & combined row+column CellVolume scaling + FastScale bounds + replays:
   - Proves (QF_NRA, 6 vars) on a 3-axis tetrahedron with independent scales (su, sv, sw > 0)
     that per-edge scaling cancels su*sv*sw in dens and preserves both Circumcenter and CellVolume
     identically, while single-axis scaling fails when su != sv (SAT negative control).
   - Proves (QF_LIA) that FastScaleMin = 2^-100 and FastScaleMax = 2^100 keep degree-4 products
     strictly inside binary64 normal range (-1022 < 4*lo_e <= 4*hi_e < 1023), with SAT negative
     controls at +-270.
   - 6 binary64 replays mirroring comp_geom/Delaunay3.cs 1-to-1: isotropic 2^±270 & 2^±600,
     mixed-exponent H1 ((1, 2^-600, 2^-600) and (2^520, 2^520, 2^-1000)), pre == 1 ([-9e307, 9e307]),
     large-offset H2 (|cc - a| > DBL_MAX while |cc| <= DBL_MAX), anisotropic CellVolume (corner,
     diagonal Kuhn cell, fast-path column-thin cell, and rotated anisotropic sliver via shortest-edge
     anchor + combined row+column scaling), and WithinSeparation underflow/overflow.
"""
import math
import struct
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

DT_SOURCE = 'comp_geom/Delaunay3.cs'

DT_ANCHORS = [
    ('const double MinNormal = 2.2250738585072014e-308;', 'int new_cell(',
     '968b2fd9f9c457dc7a9e66c6c433643af04c866681dd58c3767f61cd8f1c384a'),
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
    """Prove cavity Euler identity F = 2T + 2 - 2*E_i, derive 2*T = V_b^2 - 5*V_b + 6, and prove quadratic blow-up."""
    rows = []
    T, F, F_int, V_b, V_i, E_b, E_i = z3.Ints('T F F_int V_b V_i E_b E_i')
    base_euler = [
        V_b >= 4, T >= 1, F >= 4, F_int >= 0, E_i >= 0,
        E_b == 3 * V_b - 6,
        F == 2 * V_b - 4,
        4 * T == 2 * F_int + F,
        (V_b + V_i) - (E_b + E_i) + (F + F_int) - T == 1,  # 3-ball Euler characteristic
    ]
    euler_prem = base_euler + [V_i == 0]
    rows.append(('cavity_euler_assumptions_sat', euler_prem, z3.And(V_b == 14, F < T), 'sat', 'QF_LIA'))
    rows.append((
        'cavity_euler_formula_exact',
        euler_prem,
        F != 2 * T + 2 - 2 * E_i,
        'unsat',
        'QF_LIA',
    ))
    # Negative control: allowing interior vertices (V_i >= 1) breaks F == 2*T + 2 - 2*E_i
    rows.append((
        'cavity_euler_with_interior_vertex_negative_control',
        base_euler + [V_i >= 1],
        F != 2 * T + 2 - 2 * E_i,
        'sat',
        'QF_LIA',
    ))

    # Derive 2*T == V_b^2 - 5*V_b + 6 directly from Euler + complete interior chord count 2*E_i == V_b*(V_b - 1) - 2*E_b
    chord_prem = euler_prem + [2 * E_i == V_b * (V_b - 1) - 2 * E_b]
    rows.append((
        'cospherical_complete_chords_derive_quadratic_tet_count',
        chord_prem,
        2 * T != V_b * V_b - 5 * V_b + 6,
        'unsat',
        'QF_NIA',
    ))

    # Quadratic cospherical blow-up: T exceeds c * F for ANY linear constant c >= 1 once V_b >= 4*c + 5
    c = z3.Int('c')
    quad_prem = chord_prem + [c >= 1, V_b >= 4 * c + 5]
    rows.append(('cospherical_blowup_assumptions_sat', quad_prem, z3.And(c == 5, V_b == 25), 'sat', 'QF_NIA'))
    rows.append((
        'cospherical_cavity_beats_any_linear_boundary_bound',
        quad_prem,
        T <= c * F,
        'unsat',
        'QF_NIA',
    ))
    # Negative control: relaxing V_b >= 4*c + 5 allows T <= c * F
    rows.append((
        'cospherical_cavity_below_threshold_allows_linear_bound_negative_control',
        chord_prem + [c >= 1],
        T <= c * F,
        'sat',
        'QF_NIA',
    ))
    return rows


def ball_center_breaker_obligations():
    """Analyze Portaneri et al. (2022) cospherical ball-center breaker guarantee boundary in QF_LRA."""
    rows = []
    alpha, delta, R, dv, dc, d_cx, d_xq = z3.Reals('alpha delta R dv dc d_cx d_xq')
    sep = z3.If(alpha <= delta, alpha, delta)

    # 1. Empty Delaunay ball B(c, R) with R > alpha: every existing vertex v has dv >= R > min(alpha, delta)
    sep_prem = [alpha > 0, delta > 0, R > alpha, dv >= R]
    rows.append(('ball_center_sep_assumptions_sat', sep_prem, delta < alpha, 'sat', 'QF_LRA'))
    rows.append((
        'ball_center_preserves_separation',
        sep_prem,
        dv < sep,
        'unsat',
        'QF_LRA',
    ))
    # Negative control: dropping R > alpha (allowing R <= alpha) admits dv < min(alpha, delta)
    rows.append((
        'ball_center_small_radius_violates_separation_negative_control',
        [alpha > 0, delta > 0, R > 0, R <= alpha, dv >= R],
        dv < sep,
        'sat',
        'QF_LRA',
    ))

    # 2. Geometric triangle-inequality clearance from any point x in the open offset ball (|x - q_I| < delta):
    #    since q_I in I and dc = dist(c, I) <= dist(c, q_I) <= dist(c, x) + dist(x, q_I) = d_cx + d_xq,
    #    guarding dc >= delta implies d_cx > 0 for every x in the open offset volume (d_cx <= 0 is UNSAT).
    tri_base = [alpha > 0, delta > 0, R > alpha, d_xq >= 0, d_xq < delta, d_cx >= 0, dc <= d_cx + d_xq]
    guard_prem = tri_base + [dc >= delta]
    rows.append(('ball_center_guarded_off_surface_witness', guard_prem, dc > delta, 'sat', 'QF_LRA'))
    rows.append((
        'ball_center_guarded_clears_open_offset_ball',
        guard_prem,
        d_cx <= 0,
        'unsat',
        'QF_LRA',
    ))
    # Negative control: unguarded (dc >= 0 only) admits d_cx == 0 with d_xq == 0 (center ON the input!)
    rows.append((
        'ball_center_unguarded_can_hit_input_negative_control',
        tri_base + [dc >= 0],
        z3.And(d_cx == 0, d_xq == 0),
        'sat',
        'QF_LRA',
    ))
    return rows


def circumcenter_scale_obligations():
    """Verify per-edge Circumcenter/CellVolume scaling (QF_NRA, 6 vars) and FastScale exponent bounds (QF_LIA)."""
    rows = []
    # 3-axis tetrahedron: u = (su*uhat, 0, 0), v = (0, sv*vhat, 0), w = (0, 0, sw*what)
    # Per-edge normalization cancels su*sv*sw in dens and leaves su, sv, sw on the three offset components.
    su, sv, sw, uhat, vhat, what = z3.Reals('su sv sw uhat vhat what')
    ux, vy, wz = su * uhat, sv * vhat, sw * what
    den = 2 * ux * vy * wz
    dens = 2 * uhat * vhat * what
    num_x, num_y, num_z = ux * ux * vy * wz, vy * vy * wz * ux, wz * wz * ux * vy
    nums_x, nums_y, nums_z = uhat * uhat * vhat * what, vhat * vhat * what * uhat, what * what * uhat * vhat

    scale_prem = [su > 0, sv > 0, sw > 0, uhat != 0, vhat != 0, what != 0]
    rows.append((
        'circumcenter_per_edge_scale_assumptions_sat',
        scale_prem,
        z3.And(su == 4, sv == 2, sw == 1, uhat == 1, vhat == 1, what == 1),
        'sat',
        'QF_NRA',
    ))
    rows.append((
        'circumcenter_per_edge_pow2_scaling_exact',
        scale_prem,
        z3.Or(
            su * nums_x * den != num_x * dens,
            sv * nums_y * den != num_y * dens,
            sw * nums_z * den != num_z * dens,
        ),
        'unsat',
        'QF_NRA',
    ))
    rows.append((
        'cell_volume_per_edge_pow2_scaling_exact',
        scale_prem,
        su * sv * sw * (uhat * vhat * what) != ux * vy * wz,
        'unsat',
        'QF_NRA',
    ))
    # Negative control: scaling all three components back by a single su fails on anisotropic axes (su != sv)
    rows.append((
        'circumcenter_single_scale_anisotropic_negative_control',
        scale_prem,
        su * nums_y * den != num_y * dens,
        'sat',
        'QF_NRA',
    ))

    # FastScaleMin = 2^-100, FastScaleMax = 2^100 keep degree-4 exponents strictly in (-1022, 1023)
    lo_e, hi_e = z3.Ints('lo_e hi_e')
    fast_prem = [lo_e >= -100, hi_e <= 100, lo_e <= hi_e]
    rows.append(('fast_scale_bounds_assumptions_sat', fast_prem, z3.And(lo_e == -100, hi_e == 100), 'sat', 'QF_LIA'))
    rows.append((
        'fast_scale_degree4_stays_normal_and_finite',
        fast_prem,
        z3.Or(4 * lo_e <= -1022, 4 * hi_e >= 1023),
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'fast_scale_2p270_overflows_degree4_negative_control',
        [lo_e <= hi_e, hi_e == 270],
        4 * hi_e >= 1023,
        'sat',
        'QF_LIA',
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
    sbits = struct.unpack('<Q', struct.pack('<d', v * 18014398509481984.0))[0]
    return ((sbits >> 52) & 0x7FF) - 1023 - 54


def _ieee_div(x, y):
    if y != 0.0:
        return x / y
    if x == 0.0 or math.isnan(x) or math.isnan(y):
        return float('nan')
    neg = (math.copysign(1.0, x) < 0.0) != (math.copysign(1.0, y) < 0.0)
    return float('-inf') if neg else float('inf')


def _vsub(p, q):
    return (p[0] - q[0], p[1] - q[1], p[2] - q[2])


def _vadd(p, q):
    return (p[0] + q[0], p[1] + q[1], p[2] + q[2])


def _vmul(s, u):
    return (s * u[0], s * u[1], s * u[2])


def _vdiv(u, s):
    return (_ieee_div(u[0], s), _ieee_div(u[1], s), _ieee_div(u[2], s))


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
    def _sb(x):
        if x == 0.0 or not math.isfinite(x):
            return x
        try:
            return math.ldexp(x, n)
        except OverflowError:
            return math.copysign(float('inf'), x)
    return (_sb(u[0]), _sb(u[1]), _sb(u[2]))


def _safe_diff(p, q):
    d = _vsub(p, q)
    if _vfinite(d):
        return d, 0
    return (0.5 * p[0] - 0.5 * q[0], 0.5 * p[1] - 0.5 * q[1], 0.5 * p[2] - 0.5 * q[2]), 1


def _old_circumcenter(a, b, c, d):
    u, v, w = _vsub(b, a), _vsub(c, a), _vsub(d, a)
    vw, wu, uv = _vcross(v, w), _vcross(w, u), _vcross(u, v)
    den = 2.0 * _vdot(u, vw)
    num = _vadd(_vadd(_vmul(_vlen2(u), vw), _vmul(_vlen2(v), wu)), _vmul(_vlen2(w), uv))
    return _vadd(a, _vdiv(num, den))


def _new_circumcenter(a, b, c, d):
    u, v, w = _vsub(b, a), _vsub(c, a), _vsub(d, a)
    if _vfinite(u) and _vfinite(v) and _vfinite(w):
        mu, mv, mw = _vmaxabs(u), _vmaxabs(v), _vmaxabs(w)
        hi, lo = max(mu, mv, mw), min(mu, mv, mw)
        if hi <= _FAST_MAX and lo >= _FAST_MIN:
            vw, wu, uv = _vcross(v, w), _vcross(w, u), _vcross(u, v)
            den = 2.0 * _vdot(u, vw)
            num = _vadd(_vadd(_vmul(_vlen2(u), vw), _vmul(_vlen2(v), wu)), _vmul(_vlen2(w), uv))
            return _vadd(a, _vdiv(num, den))
    if not (_vfinite(a) and _vfinite(b) and _vfinite(c) and _vfinite(d)):
        return (float('nan'), float('nan'), float('nan'))
    u, pu = _safe_diff(b, a)
    v, pv = _safe_diff(c, a)
    w, pw = _safe_diff(d, a)
    um, vm, wm = _vmaxabs(u), _vmaxabs(v), _vmaxabs(w)
    if um == 0.0 or vm == 0.0 or wm == 0.0:
        return (float('nan'), float('nan'), float('nan'))
    eu, ev, ew = _pow2_exp(um), _pow2_exp(vm), _pow2_exp(wm)
    us, vs, ws = _vscale(u, -eu), _vscale(v, -ev), _vscale(w, -ew)
    vws, wus, uvs = _vcross(vs, ws), _vcross(ws, us), _vcross(us, vs)
    dens = 2.0 * _vdot(us, vws)
    raw1 = _vdiv(_vmul(_vlen2(us), vws), dens)
    raw2 = _vdiv(_vmul(_vlen2(vs), wus), dens)
    raw3 = _vdiv(_vmul(_vlen2(ws), uvs), dens)
    if not (_vfinite(raw1) and _vfinite(raw2) and _vfinite(raw3)):
        return _vadd(a, _vadd(_vadd(raw1, raw2), raw3))
    su, sv, sw = eu + pu, ev + pv, ew + pw
    t1, t2, t3 = _vscale(raw1, su), _vscale(raw2, sv), _vscale(raw3, sw)
    res = _vadd(a, _vadd(_vadd(t1, t2), t3))
    if _vfinite(res):
        return res
    max_e = _pow2_exp(_vmaxabs(a)) if _vmaxabs(a) > 0.0 else -1074
    if _vmaxabs(raw1) > 0.0:
        max_e = max(max_e, su + _pow2_exp(_vmaxabs(raw1)))
    if _vmaxabs(raw2) > 0.0:
        max_e = max(max_e, sv + _pow2_exp(_vmaxabs(raw2)))
    if _vmaxabs(raw3) > 0.0:
        max_e = max(max_e, sw + _pow2_exp(_vmaxabs(raw3)))
    shift = max_e - 1000
    if shift <= 0:
        return res
    s_sum = _vadd(
        _vscale(a, -shift),
        _vadd(_vadd(_vscale(raw1, su - shift), _vscale(raw2, sv - shift)), _vscale(raw3, sw - shift)),
    )
    return _vscale(s_sum, shift)


def _col_residual_exp(ux, eu, vx, ev, wx, ew):
    ec = None
    if ux != 0.0:
        v = _pow2_exp(abs(ux)) - eu
        ec = v if ec is None else max(ec, v)
    if vx != 0.0:
        v = _pow2_exp(abs(vx)) - ev
        ec = v if ec is None else max(ec, v)
    if wx != 0.0:
        v = _pow2_exp(abs(wx)) - ew
        ec = v if ec is None else max(ec, v)
    return ec


def _new_cell_volume(a, b, d, e):
    u, v, w = _vsub(b, a), _vsub(d, a), _vsub(e, a)
    if _vfinite(u) and _vfinite(v) and _vfinite(w):
        mu, mv, mw = _vmaxabs(u), _vmaxabs(v), _vmaxabs(w)
        hi, lo = max(mu, mv, mw), min(mu, mv, mw)
        mx = max(abs(u[0]), abs(v[0]), abs(w[0]))
        my = max(abs(u[1]), abs(v[1]), abs(w[1]))
        mz = max(abs(u[2]), abs(v[2]), abs(w[2]))
        lo_col = min(mx, my, mz)
        if hi <= _FAST_MAX and (hi == 0.0 or (lo >= _FAST_MIN and lo_col >= _FAST_MIN)):
            return _vdot(_vcross(u, v), w) / 6.0
    if not (_vfinite(a) and _vfinite(b) and _vfinite(d) and _vfinite(e)):
        return float('nan')
    u, pu = _safe_diff(b, a)
    v, pv = _safe_diff(d, a)
    w, pw = _safe_diff(e, a)
    best_p, best_m, anchor = pu, _vmaxabs(u), 0
    if pv < best_p or (pv == best_p and _vmaxabs(v) < best_m):
        best_p, best_m = pv, _vmaxabs(v)
    if pw < best_p or (pw == best_p and _vmaxabs(w) < best_m):
        best_p, best_m = pw, _vmaxabs(w)
    bd, pbd = _safe_diff(d, b)
    if pbd < best_p or (pbd == best_p and _vmaxabs(bd) < best_m):
        best_p, best_m, anchor = pbd, _vmaxabs(bd), 1
    be, pbe = _safe_diff(e, b)
    if pbe < best_p or (pbe == best_p and _vmaxabs(be) < best_m):
        best_p, best_m, anchor = pbe, _vmaxabs(be), 1
    de, pde = _safe_diff(e, d)
    if pde < best_p or (pde == best_p and _vmaxabs(de) < best_m):
        anchor = 2
    if anchor == 1:
        u, pu = _safe_diff(a, b)
        v, pv = be, pbe
        w, pw = bd, pbd
    elif anchor == 2:
        u, pu = de, pde
        v, pv = _safe_diff(a, d)
        w, pw = _safe_diff(b, d)
    um, vm, wm = _vmaxabs(u), _vmaxabs(v), _vmaxabs(w)
    if um == 0.0 or vm == 0.0 or wm == 0.0:
        return 0.0
    eu, ev, ew = _pow2_exp(um), _pow2_exp(vm), _pow2_exp(wm)
    ecx = _col_residual_exp(u[0], eu, v[0], ev, w[0], ew)
    ecy = _col_residual_exp(u[1], eu, v[1], ev, w[1], ew)
    ecz = _col_residual_exp(u[2], eu, v[2], ev, w[2], ew)
    if ecx is None or ecy is None or ecz is None:
        return 0.0
    us = (math.ldexp(u[0], -(eu + ecx)), math.ldexp(u[1], -(eu + ecy)), math.ldexp(u[2], -(eu + ecz)))
    vs = (math.ldexp(v[0], -(ev + ecx)), math.ldexp(v[1], -(ev + ecy)), math.ldexp(v[2], -(ev + ecz)))
    ws = (math.ldexp(w[0], -(ew + ecx)), math.ldexp(w[1], -(ew + ecy)), math.ldexp(w[2], -(ew + ecz)))
    val = _vdot(_vcross(us, vs), ws) / 6.0
    try:
        return math.ldexp(val, eu + ev + ew + ecx + ecy + ecz + pu + pv + pw)
    except OverflowError:
        return math.copysign(float('inf'), val)


def _safe_distance(a, b):
    if not (_vfinite(a) and _vfinite(b)):
        return float('nan')
    d, pre = _safe_diff(a, b)
    m = _vmaxabs(d)
    if m == 0.0:
        return 0.0
    e = _pow2_exp(m)
    ds = _vscale(d, -e)
    try:
        return math.ldexp(math.sqrt(_vlen2(ds)), e + pre)
    except OverflowError:
        return float('inf')


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
    return _safe_distance(a, b) <= min_sep


def replay_fp():
    replays = []

    # 1. Isotropic Circumcenter at 2^+-270 and 2^+-600: degree-4 s^4 overflows/underflows on old formula; new is exact
    iso_ok = True
    for exp in (270, -270, 600, -600):
        s = math.ldexp(1.0, exp)
        old_cc = _old_circumcenter((0.0, 0.0, 0.0), (s, 0.0, 0.0), (0.0, s, 0.0), (0.0, 0.0, s))
        new_cc = _new_circumcenter((0.0, 0.0, 0.0), (s, 0.0, 0.0), (0.0, s, 0.0), (0.0, 0.0, s))
        expected = (math.ldexp(0.5, exp),) * 3
        old_failed = (not _vfinite(old_cc)) or (old_cc != expected)
        iso_ok = iso_ok and old_failed and (new_cc == expected)
    replays.append(dict(
        name='circumcenter_isotropic_2p270_2p600_replay',
        passed=iso_ok,
    ))

    # 2. Mixed-exponent H1 Circumcenter: (1, 2^-600, 2^-600) and (2^520, 2^520, 2^-1000)
    mixed_ok = True
    for ex, ey, ez in ((0, -600, -600), (0, -400, -400), (520, 520, -1000)):
        sx, sy, sz = math.ldexp(1.0, ex), math.ldexp(1.0, ey), math.ldexp(1.0, ez)
        old_m = _old_circumcenter((0.0, 0.0, 0.0), (sx, 0.0, 0.0), (0.0, sy, 0.0), (0.0, 0.0, sz))
        new_m = _new_circumcenter((0.0, 0.0, 0.0), (sx, 0.0, 0.0), (0.0, sy, 0.0), (0.0, 0.0, sz))
        expected = (math.ldexp(0.5, ex), math.ldexp(0.5, ey), math.ldexp(0.5, ez))
        old_failed = (not _vfinite(old_m)) or (old_m != expected)
        mixed_ok = mixed_ok and old_failed and (new_m == expected)
    replays.append(dict(
        name='circumcenter_mixed_exponent_anisotropic_replay',
        passed=mixed_ok,
    ))

    # 3. Circumcenter pre == 1 ([-9e307, +9e307]): raw b - a overflows; half-scaled mirror gives (0, 0, 0)
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

    # 4. Large-offset H2 Circumcenter (|cc - a| > DBL_MAX while |cc| <= DBL_MAX): rescaled shift frame stays finite
    h2_cases = [
        ((-1.6e308, 0.0, 0.0), (1.6e308, 1.4e308, 0.0), (1.6e308, -1.4e308, 0.0), (1.6e308, 0.0, 1.4e308), 3.0625e307),
        ((-1.0e308, 0.0, 0.0), (0.0, 1.7e308, 0.0), (0.0, -1.7e308, 0.0), (0.0, 0.0, 1.7e308), 9.45e307),
    ]
    h2_ok = True
    for a, b, c, d, exp_x in h2_cases:
        old_h2 = _old_circumcenter(a, b, c, d)
        new_h2 = _new_circumcenter(a, b, c, d)
        h2_ok = (h2_ok and (not _vfinite(old_h2)) and _vfinite(new_h2)
                 and abs(new_h2[0] - exp_x) <= 1e295 and abs(new_h2[1]) <= 1e295 and abs(new_h2[2]) <= 1e295)
    replays.append(dict(
        name='circumcenter_large_offset_rescaled_frame_replay',
        passed=h2_ok,
    ))

    # 5. CellVolume on corner, diagonal Kuhn cell, fast-path column-thin cell, and rotated anisotropic sliver
    s520, sm1000 = math.ldexp(1.0, 520), math.ldexp(1.0, -1000)
    sm100, s100 = math.ldexp(1.0, -100), math.ldexp(1.0, 100)
    a0, b0, d0, e0 = (0.0, 0.0, 0.0), (s520, 0.0, 0.0), (0.0, s520, 0.0), (0.0, 0.0, sm1000)
    a1, b1, d1, e1 = (0.0, 0.0, 0.0), (s520, 0.0, 0.0), (s520, 0.0, sm1000), (s520, s520, sm1000)
    a2, b2, d2, e2 = (0.0, 0.0, 0.0), (sm100, 0.0, sm1000), (sm100, 0.0, 0.0), (0.0, s100, 0.0)
    a3, b3, d3, e3 = (0.0, 0.0, 0.0), (s520, s520, s520), (s520, -s520, 0.0), (sm1000, sm1000, -2.0 * sm1000)
    old_vol0 = _vdot(_vcross(_vsub(b0, a0), _vsub(d0, a0)), _vsub(e0, a0)) / 6.0
    old_vol2 = _vdot(_vcross(_vsub(b2, a2), _vsub(d2, a2)), _vsub(e2, a2)) / 6.0
    new_vol0 = _new_cell_volume(a0, b0, d0, e0)
    new_vol1 = abs(_new_cell_volume(a1, b1, d1, e1))
    new_vol2 = _new_cell_volume(a2, b2, d2, e2)
    new_vol3 = _new_cell_volume(a3, b3, d3, e3)
    new_vol3_perm = _new_cell_volume(b3, a3, e3, d3)
    expected_vol = math.ldexp(1.0 / 6.0, 40)
    expected_thin = math.ldexp(1.0 / 6.0, -1000)
    expected_rot = math.ldexp(1.0, 40)
    replays.append(dict(
        name='cell_volume_anisotropic_sliver_replay',
        old_inf=math.isinf(old_vol0),
        old_col_thin_zero=(old_vol2 == 0.0),
        new_exact=(new_vol0 == expected_vol and new_vol1 == expected_vol
                   and new_vol2 == expected_thin and new_vol3 == expected_rot and new_vol3_perm == expected_rot),
        passed=(math.isinf(old_vol0) and old_vol2 == 0.0 and new_vol0 == expected_vol
                and new_vol1 == expected_vol and new_vol2 == expected_thin
                and new_vol3 == expected_rot and new_vol3_perm == expected_rot),
    ))

    # 6. WithinSeparation at tiny (2^-540 vs 2^-545) and huge (2^520 vs 2^515) scales
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
