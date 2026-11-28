"""Local Z3 obligations, complexity witnesses, and binary64 replays for Alpha Wrap
RefineNonManifold / PullPinchPoints (#1045) and R1 1-Lipschitz bisection worst cases.

Formally verifies the geometric guarantees, parameter boundaries, and worst-case
complexity of:
  - mesh_ops/MeshAlphaWrap.cs: PullMaxOffsets (line 215), rule_intersection (1369-1390),
    refine_non_manifold (1513-1579), cells_moved_by_manifold_step (1742-1783),
    pinch_point (1789-1827), pulled_pinch_point (1839-1862);
  - mesh_ops/AlphaWrapOracle.cs: FirstEntry (433-443), sphere_entry (566-577);
  - docs/design/ALPHA_WRAP_MANIFOLD_QUALITY.md (#1045) & ALPHA_WRAP_PAPER_NOTES.md.

1. PullPinchPoints geometry, band cap, and angle bound (QF_NRA & QF_LRA):
   - Given empty circumsphere B(c, r) (every vertex v at d_cv >= r), separation
     s = min(delta, alpha - delta) with 0 < s < r, offset projection q with L = |q - c| > r - s,
     and pulled point x = c + ((r - s) / L) * (q - c):
     * parameter t = (r - s) / L lies strictly in (0, 1);
     * x lies strictly inside B(c, r) (|x - c| = r - s < r);
     * every existing vertex satisfies |x - v| >= s (triangle inequality; UNSAT to violate);
     * negative control (SAT): variant C (half depth 0.5 * s, ALPHA_WRAP_MANIFOLD_QUALITY.md §2)
       violates separation.
   - Along the closest-point ray (dc > delta, L = dc - delta > r - s, dx = dc - (r - s)):
     * geometric triangle-inequality clearance from any point y inside the open offset ball
       (|y - q_I| < delta, dx <= d_xy + d_yq) proves d_xy > 0 (d_xy <= 0 is UNSAT);
     * without the PullMaxOffsets = 2.0 cap, dx = 3.3 * delta is SAT (reproducing Soup 28 in
       AlphaWrapPullPinchTests.cs:95);
     * negative control (SAT): if cc lies inside the offset (dc < delta), dx < delta is reachable,
       explaining why pulled_pinch_point explicitly checks dx >= offset (MeshAlphaWrap.cs:1858).
   - Law-of-sines angle bound (e / (2*R) >= s / (2*alpha)):
     * holds for every gate facet (R <= alpha, e >= s; UNSAT to violate);
     * broken by make_manifold flips (R > alpha even with e >= s; SAT, matching Soup 42);
     * broken by variant C half-separation (R <= alpha, e >= 0.5 * s; SAT).
2. RefineNonManifold loop termination & LocalPinchTrials confirmation (QF_LIA):
   - Proves every continuing round strictly advances both round count (<= MaxNonManifoldRounds)
     and Steiner count (<= MaxSteinerPoints).
   - Proves (SAT witness) why LocalPinchTrials requires a confirming full trial: remove_pockets
     can flip an enclosed pocket cell away from changedSinceTrial, turning an unseeded vertex
     non-manifold.
3. R1 1-Lipschitz bisection worst case vs analytic sphere_entry (QF_LRA & QF_NRA):
   - Proves 1-Lipschitz interval pruning soundness: d(mid) - delta > (s1 - s0) / 2 => d(s) > delta
     for all s in [s0, s1] (UNSAT for d(s) <= delta), with SAT negative controls for an overly
     tight (s1 - s0) / 4 guard and for a 2-Lipschitz function.
   - Proves grazing-tangent non-pruning worst case: a segment at clearance delta + eps (eps > 0)
     never enters the offset volume (UNSAT), yet defeats 1-Lipschitz pruning across the entire
     quadratic band u^2 <= 2*delta*(half_w - eps) (UNSAT to prune in QF_NRA!), yielding
     Omega(sqrt(delta / eps)) unpruned intervals at depth ~ log2(L / eps), whereas
     AlphaWrapOracle.sphere_entry's quadratic discriminant (B^2 - A*C < 0 => A*t^2 + 2*B*t + C > 0)
     soundly rejects every grazing tangent in O(1) (with SAT hit negative control).
4. Three binary64 replays mirroring MeshAlphaWrap and AlphaWrapOracle:
   - Replay 1: pulled_pinch_point on Soup 42 (strict candidate fails, pulled point accepted in
     [offset, 2*offset]) and Soup 28 (3.3*offset rejected by PullMaxOffsets = 2.0).
   - Replay 2: grazing tangent at eps = 2^-20: 1-Lipschitz bisection expands hundreds/thousands of
     unpruned sub-intervals across 20 levels while sphere_entry rejects in 1 discriminant check.
   - Replay 3: rule_intersection 60-step exit-bisection stall near t = 1 (7 wasted iterations)
     and at UTM scale 5e6 (29 redundant oracle evaluations on identical binary64 coordinates).
"""
import math
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

WRAP_SOURCE = 'mesh_ops/MeshAlphaWrap.cs'
ORACLE_SOURCE = 'mesh_ops/AlphaWrapOracle.cs'

WRAP_ANCHORS = [
    ('internal const double PullMaxOffsets = 2.0;', '/// <summary>',
     '7fdcd5720faa08d121e1a8638ea1e075393fdd6799a5cba9e956e8f97924a711'),
    ('bool rule_intersection(Vector3d from, Vector3d to, out Vector3d p)', 'double vertex_distance(int v, RoundGate g = null)',
     '1dcd54814b2888519a0844314e8d7e50c645a1c198e9fadf4ac09b6d2b0484dc'),
    ('WrapStatus refine_non_manifold(bool confirm = true)', '// (totals: the pinch refinement runs again after the quality refinement)',
     '07baa4065b2552b23f453327ed4e30d0d972600bf8865695513a1d9fa847b734'),
    ('WrapStatus cells_moved_by_manifold_step(List<int> moved, out bool local, bool full = false)', 'bool lastPinchPulled;',
     '97a35675835b175c2d98f091b0c0ba00cb455ec6d866c0a469930364d35bf274'),
    ('bool pinch_point(int c, out Vector3d p)', 'bool pulled_pinch_point(int c, Vector3d cc, double depth, out Vector3d p)',
     '445cba7c7cd51c802296922b53ec35c86f6014d55543037d8b6e5a20ff73ed2d'),
    ('bool pulled_pinch_point(int c, Vector3d cc, double depth, out Vector3d p)', 'int[] pocketStamp = new int[0];',
     '96227d30a947e1678f3974c6ca53536f94eb88e65ac38a954006a44411540596'),
]

ORACLE_ANCHORS = [
    ('public double FirstEntry(Vector3d a, Vector3d b, double r)', 'internal double PrimitiveEntry(',
     '902d30e6bb1d75acf3e936781c7b7be3eb1cb7ffdfdffe4354dd5877c2f2b9a2'),
    ('static double sphere_entry(Vector3d a, Vector3d d, Vector3d c, double r, double tmax)', 'static double capsule_entry(',
     '241e2501cb1ecdeeb7c749caf78b81498acc21116318d6ee2bd1a51e21e81d8a'),
]


def pull_pinch_geometry_obligations():
    """Verify PullPinchPoints separation, containment, offset-band clearance, cap necessity, and angle bound."""
    rows = []

    # 1. Pulled-point parameter, circumsphere containment, and vertex separation (QF_NRA, 6 vars)
    r, s, L, t, d_cv, d_xv = z3.Reals('r s L t d_cv d_xv')
    d_cx = t * L
    sep_prem = [
        r > 0, s > 0, s < r,
        L > r - s,
        t * L == r - s,
        d_cv >= r,
        d_xv >= 0,
        d_cv <= d_cx + d_xv,  # triangle inequality |v - c| <= |x - c| + |x - v|
    ]
    rows.append(('pull_sep_assumptions_sat', sep_prem, z3.And(r == 12, s == 1, L == 12), 'sat', 'QF_NRA'))
    rows.append((
        'pulled_point_parameter_strictly_inside_segment',
        sep_prem,
        z3.Or(t <= 0, t >= 1),
        'unsat',
        'QF_NRA',
    ))
    rows.append((
        'pulled_point_strictly_inside_circumsphere',
        sep_prem,
        d_cx >= r,
        'unsat',
        'QF_NRA',
    ))
    rows.append((
        'pulled_point_preserves_separation',
        sep_prem,
        d_xv < s,
        'unsat',
        'QF_NRA',
    ))
    # Negative control: variant C (half depth 0.5 * s, ALPHA_WRAP_MANIFOLD_QUALITY.md §2) violates separation
    varc_prem = [
        r > 0, s > 0, s < r,
        L > r - s / 2,
        t * L == r - s / 2,
        d_cv >= r,
        d_xv >= 0,
        d_cv <= d_cx + d_xv,
    ]
    rows.append((
        'variant_c_half_depth_violates_separation_negative_control',
        varc_prem,
        d_xv < s,
        'sat',
        'QF_NRA',
    ))

    # 2. Outside-offset clearance along closest-point ray & 2*delta band cap (QF_LRA)
    alpha, delta, r2, s2, dc, dx, d_xy, d_yq = z3.Reals('alpha delta r2 s2 dc dx d_xy d_yq')
    sep_def = s2 == z3.If(delta <= alpha - delta, delta, alpha - delta)
    ray_base = [
        alpha > delta, delta > 0, sep_def,
        r2 > s2,
        dx == dc - (r2 - s2),
        d_yq >= 0, d_yq < delta, d_xy >= 0,
        dx <= d_xy + d_yq,  # triangle inequality: dx = dist(x, I) <= dist(x, q_I) <= dist(x, y) + dist(y, q_I)
    ]
    # When cc is outside offset (dc > delta) and L = dc - delta > r2 - s2, x clears every point y in open offset ball
    ray_outside = ray_base + [dc > delta, dc - delta > r2 - s2]
    rows.append(('pull_band_assumptions_sat', ray_outside, z3.And(alpha == 10, delta == 1, dx == 1.5), 'sat', 'QF_LRA'))
    rows.append((
        'pulled_point_clears_open_offset_ball_when_cc_outside',
        ray_outside,
        d_xy <= 0,
        'unsat',
        'QF_LRA',
    ))
    # Uncapped witness: without dx <= 2*delta (PullMaxOffsets), dx = 3.3*delta is reachable (Soup 28)
    rows.append((
        'pulled_point_uncapped_can_exceed_2delta_witness',
        ray_outside,
        dx == z3.Q(33, 10) * delta,
        'sat',
        'QF_LRA',
    ))
    # Negative control: dropping dc > delta (cc inside offset volume) allows dx < delta (even hitting input!)
    rows.append((
        'pulled_point_cc_inside_offset_can_enter_offset_negative_control',
        ray_base + [dc >= 0, dc < delta],
        z3.And(d_xy == 0, d_yq == 0),
        'sat',
        'QF_LRA',
    ))

    # 3. Minimum angle bound sin(theta_min) = e / (2*R) >= s / (2*alpha) <=> e*alpha >= s*R (QF_NRA, 5 vars)
    a3, d3, s3, R3, e3 = z3.Reals('a3 d3 s3 R3 e3')
    ang_base = [a3 > d3, d3 > 0, s3 == z3.If(d3 <= a3 - d3, d3, a3 - d3), R3 > 0, e3 > 0]
    rows.append(('angle_bound_assumptions_sat', ang_base + [R3 <= a3, e3 >= s3], z3.And(a3 == 10, d3 == 1), 'sat', 'QF_NRA'))
    rows.append((
        'gate_facet_keeps_min_angle_bound',
        ang_base + [R3 <= a3, e3 >= s3],
        e3 * a3 < s3 * R3,
        'unsat',
        'QF_NRA',
    ))
    # Negative control: make_manifold flip exposes R3 > a3 (breaks bound even when separation e3 >= s3 holds; Soup 42)
    rows.append((
        'manifold_flip_facet_breaks_angle_bound_negative_control',
        ang_base + [R3 > a3, e3 >= s3],
        e3 * a3 < s3 * R3,
        'sat',
        'QF_NRA',
    ))
    # Negative control: variant C half-separation (2*e3 >= s3) breaks angle bound even on gate facets (R3 <= a3)
    rows.append((
        'variant_c_half_sep_breaks_angle_bound_negative_control',
        ang_base + [R3 <= a3, 2 * e3 >= s3],
        e3 * a3 < s3 * R3,
        'sat',
        'QF_NRA',
    ))
    return rows


def refine_non_manifold_loop_obligations():
    """Verify refine_non_manifold bounded progress and LocalPinchTrials confirmation necessity (QF_LIA)."""
    rows = []

    # 1. Bounded termination & strict progress per continuing round (MeshAlphaWrap.cs:1519-1579)
    rnd, max_rnd, steiner, max_st, pinch, ins = z3.Ints('rnd max_rnd steiner max_st pinch ins')
    loop_prem = [
        max_rnd >= 1, max_st >= 1,
        0 <= rnd, rnd < max_rnd,
        0 <= steiner, steiner <= max_st,
        pinch >= 1,
        ins >= 1,
        steiner + ins <= max_st,
    ]
    rows.append(('refine_loop_assumptions_sat', loop_prem, z3.And(rnd == 0, max_rnd == 8, ins == 1), 'sat', 'QF_LIA'))
    rows.append((
        'refine_non_manifold_continuing_round_strict_progress',
        loop_prem,
        z3.Or(rnd + 1 > max_rnd, steiner + ins <= steiner, steiner + ins > max_st),
        'unsat',
        'QF_LIA',
    ))

    # 2. LocalPinchTrials confirmation necessity (MeshAlphaWrap.cs:1537-1545 & 1755-1763):
    #    A vertex v outside changedSinceTrial & lastTrialVertices (in_seeds == 0) can become non-manifold
    #    when remove_pockets flips an enclosed pocket cell incident to v -> local trial misses it, full catches it.
    in_seeds, pocket_flip, was_nm, is_nm, local_found, full_found = z3.Ints(
        'in_seeds pocket_flip was_nm is_nm local_found full_found'
    )
    trial_prem = [
        z3.Or(in_seeds == 0, in_seeds == 1),
        z3.Or(pocket_flip == 0, pocket_flip == 1),
        z3.Or(was_nm == 0, was_nm == 1),
        z3.Or(is_nm == 0, is_nm == 1),
        # If neither changed (in_seeds == 0) nor pocket-flipped (pocket_flip == 0), a manifold vertex stays manifold
        z3.Implies(z3.And(in_seeds == 0, pocket_flip == 0, was_nm == 0), is_nm == 0),
        local_found == z3.If(z3.And(in_seeds == 1, is_nm == 1), 1, 0),
        full_found == is_nm,
    ]
    rows.append((
        'local_trial_complete_without_pocket_flip',
        trial_prem + [pocket_flip == 0, was_nm == 0],
        z3.And(full_found == 1, local_found == 0),
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'local_trial_can_miss_remote_pocket_flip_witness',
        trial_prem + [was_nm == 0],
        z3.And(full_found == 1, local_found == 0),
        'sat',
        'QF_LIA',
    ))
    return rows


def lipschitz_bisection_obligations():
    """Verify 1-Lipschitz pruning soundness, grazing-tangent non-pruning worst case, and analytic sphere_entry."""
    rows = []

    # 1. 1-Lipschitz interval pruning soundness along arclength [s0, s1] (QF_LRA, 6 vars)
    s0, s1, s, delta, d_mid, d_s = z3.Reals('s0 s1 s delta d_mid d_s')
    sm = (s0 + s1) / 2
    dist_to_mid = z3.If(s >= sm, s - sm, sm - s)
    lip1_prem = [
        delta > 0, s0 < s1, s0 <= s, s <= s1,
        d_mid - d_s <= dist_to_mid,  # 1-Lipschitz lower bound on d(s)
        d_mid - delta > (s1 - s0) / 2,
    ]
    rows.append(('lipschitz_prune_assumptions_sat', lip1_prem, z3.And(s0 == 0, s1 == 1, delta == 1, d_mid == 2), 'sat', 'QF_LRA'))
    rows.append((
        'lipschitz_1_interval_pruning_sound',
        lip1_prem,
        d_s <= delta,
        'unsat',
        'QF_LRA',
    ))
    # Negative control 1: overly tight quarter-width guard (s1 - s0) / 4 admits an offset entry (SAT)
    rows.append((
        'lipschitz_quarter_width_guard_unsound_negative_control',
        [delta > 0, s0 < s1, s0 <= s, s <= s1, d_mid - d_s <= dist_to_mid, d_mid - delta > (s1 - s0) / 4],
        d_s <= delta,
        'sat',
        'QF_LRA',
    ))
    # Negative control 2: 2-Lipschitz function violates 1-Lipschitz pruning guard (SAT)
    rows.append((
        'lipschitz_2_violates_1lipschitz_pruning_negative_control',
        [delta > 0, s0 < s1, s0 <= s, s <= s1, d_mid - d_s <= 2 * dist_to_mid, d_mid - delta > (s1 - s0) / 2],
        d_s <= delta,
        'sat',
        'QF_LRA',
    ))

    # 2. Grazing-tangent worst case: segment at closest distance delta + eps > delta never enters offset,
    #    yet defeats 1-Lipschitz pruning across the entire quadratic band u^2 <= 2*delta*(half_w - eps),
    #    giving Omega(sqrt(delta / eps)) unpruned intervals of width 2*half_w (QF_NRA, 5 vars)
    u, del_g, eps, d_u, half_w = z3.Reals('u del_g eps d_u half_w')
    graz_prem = [del_g > 0, eps > 0, d_u >= 0, d_u * d_u == u * u + (del_g + eps) * (del_g + eps)]
    rows.append(('grazing_tangent_assumptions_sat', graz_prem, z3.And(u == 0, del_g == 1, eps == z3.Q(1, 1000)), 'sat', 'QF_NRA'))
    rows.append((
        'grazing_tangent_never_enters_offset',
        graz_prem,
        d_u <= del_g,
        'unsat',
        'QF_NRA',
    ))
    band_prem = graz_prem + [half_w >= eps, u * u <= 2 * del_g * (half_w - eps)]
    rows.append(('grazing_band_assumptions_sat', band_prem, z3.And(del_g == 1, eps == z3.Q(1, 100), half_w == z3.Q(1, 10), u == z3.Q(1, 5)), 'sat', 'QF_NRA'))
    rows.append((
        'grazing_tangent_quadratic_band_defeats_lipschitz_pruning',
        band_prem,
        d_u - del_g > half_w,
        'unsat',
        'QF_NRA',
    ))

    # 3. Analytic sphere_entry discriminant soundness (AlphaWrapOracle.cs:566-577; QF_NRA, 4 vars)
    A, B, C, tau = z3.Reals('A B C tau')
    disc_prem = [A > 0, B * B - A * C < 0]
    rows.append(('sphere_entry_disc_assumptions_sat', disc_prem, z3.And(A == 1, B == 0, C == 1), 'sat', 'QF_NRA'))
    rows.append((
        'analytic_sphere_entry_negative_disc_rejects_all_t',
        disc_prem,
        A * tau * tau + 2 * B * tau + C <= 0,
        'unsat',
        'QF_NRA',
    ))
    # Negative control: non-negative discriminant with B < 0, C > 0 admits a positive entry root (SAT)
    rows.append((
        'analytic_sphere_entry_nonnegative_disc_admits_hit_negative_control',
        [A > 0, B < 0, C > 0, B * B - A * C >= 0],
        z3.And(tau > 0, A * tau * tau + 2 * B * tau + C <= 0),
        'sat',
        'QF_NRA',
    ))
    return rows


# ---- Python binary64 mirrors for replay_fp() ----

def _vsub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _vadd(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _vmul(s, a):
    return (s * a[0], s * a[1], s * a[2])


def _vdot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _vdist(a, b):
    d = _vsub(a, b)
    return math.sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2])


def _mirror_pulled_pinch_point(cc, verts, q_in, offset, depth, pull_max_offsets=2.0):
    """1-to-1 binary64 mirror of MeshAlphaWrap.pulled_pinch_point (MeshAlphaWrap.cs:1839-1862) against point q_in."""
    if not all(math.isfinite(c) for c in cc):
        return False, None, None
    r = min(_vdist(cc, v) for v in verts)
    if not (r > depth):
        return False, None, None
    d_cc = _vdist(cc, q_in)
    if not (d_cc > 0.0):
        return False, None, None
    # project_to_offset: q = q_in + offset * (cc - q_in) / |cc - q_in|
    q = _vadd(q_in, _vmul(offset / d_cc, _vsub(cc, q_in)))
    # Strict first candidate test (pinch_point:1796)
    strict_ok = _vdist(cc, q) <= r - depth
    length = _vdist(cc, q)
    if not (length > r - depth):
        return False, None, (strict_ok, None)
    x = _vadd(cc, _vmul((r - depth) / length, _vsub(q, cc)))
    # Exact-equivalent strict in-circumsphere check (ball centered at cc with radius r)
    if not (_vdist(cc, x) < r):
        return False, None, (strict_ok, None)
    dx = _vdist(x, q_in)
    if not (dx >= offset and dx <= pull_max_offsets * offset):
        return False, x, (strict_ok, dx)
    return True, x, (strict_ok, dx)


def _mirror_sphere_entry(a, d, c, r, tmax):
    """1-to-1 binary64 mirror of AlphaWrapOracle.sphere_entry (AlphaWrapOracle.cs:566-577)."""
    m = _vsub(a, c)
    C = _vdot(m, m) - r * r
    if C <= 0.0:
        return 0.0
    A = _vdot(d, d)
    B = _vdot(m, d)
    if A == 0.0 or B >= 0.0:
        return -1.0
    disc = B * B - A * C
    if disc < 0.0:
        return -1.0
    t = (-B - math.sqrt(disc)) / A
    return t if (0.0 <= t <= tmax) else -1.0


def _lipschitz_bisect_point_count(a, b, c, delta, max_depth=55):
    """1-Lipschitz interval-pruning bisection (Portaneri et al. App. E) against point obstacle c."""
    d = _vsub(b, a)
    seg_len = math.sqrt(_vdot(d, d))
    stack = [(0.0, 1.0, 0)]
    evals = 0
    max_reached = 0
    while stack:
        t0, t1, depth = stack.pop()
        if depth > max_reached:
            max_reached = depth
        tm = 0.5 * (t0 + t1)
        pm = _vadd(a, _vmul(tm, d))
        dm = _vdist(pm, c)
        evals += 1
        if dm <= delta:
            return tm, evals, max_reached
        if dm - delta > 0.5 * (t1 - t0) * seg_len:
            continue  # 1-Lipschitz prune
        if depth < max_depth and tm > t0 and tm < t1:
            stack.append((tm, t1, depth + 1))
            stack.append((t0, tm, depth + 1))
    return -1.0, evals, max_reached


def _rule_intersection_exit_bisect(a, b, dist_fn, offset):
    """1-to-1 binary64 mirror of MeshAlphaWrap.rule_intersection exit bisection (MeshAlphaWrap.cs:1383-1388)."""
    d = _vsub(b, a)
    lo, hi = 0.0, 1.0
    mid_stalls = 0
    coord_stalls = 0
    prev_p = None
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        if mid == lo or mid == hi:
            mid_stalls += 1
        p_mid = _vadd(a, _vmul(mid, d))
        if p_mid == prev_p:
            coord_stalls += 1
        prev_p = p_mid
        if dist_fn(p_mid) > offset:
            hi = mid
        else:
            lo = mid
    p_end = _vadd(a, _vmul(hi, d))
    return p_end, mid_stalls, coord_stalls


def replay_fp():
    replays = []

    # 1. PullPinchPoints replay: Soup 42 acceptance (1.5 * offset) & Soup 28 cap rejection (3.3 * offset)
    alpha, offset = 1.0, 0.1
    depth = min(offset, alpha - offset)
    q_in = (0.0, 0.0, 0.0)
    # Soup 42-like cell: cc at 12.6 * offset, r = 1.21 -> q only 0.5 * offset inside sphere (strict fails), pulled at 1.5 * offset
    cc42 = (0.0, 0.0, 1.26)
    r42 = 1.21
    verts42 = [
        (r42, 0.0, 1.26), (-r42, 0.0, 1.26),
        (0.0, r42, 1.26), (0.0, -r42, 1.26),
    ]
    ok42, x42, (strict42, dx42) = _mirror_pulled_pinch_point(cc42, verts42, q_in, offset, depth)
    min_sep42 = min(_vdist(x42, v) for v in verts42) if x42 is not None else 0.0
    # Soup 28-like cell: cc at 15.0 * offset, r = 1.27 -> pulled point at 3.3 * offset (rejected by PullMaxOffsets = 2.0)
    cc28 = (0.0, 0.0, 1.50)
    r28 = 1.27
    verts28 = [
        (r28, 0.0, 1.50), (-r28, 0.0, 1.50),
        (0.0, r28, 1.50), (0.0, -r28, 1.50),
    ]
    ok28, _x28, (strict28, dx28) = _mirror_pulled_pinch_point(cc28, verts28, q_in, offset, depth)
    replays.append(dict(
        name='pulled_pinch_point_soup42_and_soup28_replay',
        soup42_strict_failed=(not strict42),
        soup42_pulled_accepted=ok42,
        soup42_dx_over_offset=round(dx42 / offset, 6),
        soup42_sep_kept=(min_sep42 >= depth * (1.0 - 1e-12)),
        soup28_strict_failed=(not strict28),
        soup28_capped_rejected=(not ok28),
        soup28_dx_over_offset=round(dx28 / offset, 6),
        passed=(not strict42 and ok42 and abs(dx42 - 0.15) < 1e-12
                and min_sep42 >= depth * (1.0 - 1e-12)
                and not strict28 and not ok28 and abs(dx28 - 0.33) < 1e-12),
    ))

    # 2. Grazing-tangent replay: eps = 2^-20 defeats 1-Lipschitz bisection (Omega(sqrt(delta/eps)) blow-up) vs O(1) sphere_entry
    delta = 0.01
    eps = math.ldexp(1.0, -20)
    a_g = (-0.5, delta + eps, 0.0)
    b_g = (0.5, delta + eps, 0.0)
    c_g = (0.0, 0.0, 0.0)
    t_analytic = _mirror_sphere_entry(a_g, _vsub(b_g, a_g), c_g, delta, 1.0)
    t_lip, lip_evals, lip_depth = _lipschitz_bisect_point_count(a_g, b_g, c_g, delta, max_depth=24)
    replays.append(dict(
        name='lipschitz_grazing_tangent_vs_analytic_sphere_entry_replay',
        analytic_result=t_analytic,
        lipschitz_result=t_lip,
        lipschitz_evals=lip_evals,
        lipschitz_depth=lip_depth,
        passed=(t_analytic == -1.0 and t_lip == -1.0 and lip_depth >= 19 and lip_evals >= 500),
    ))

    # 3. rule_intersection 60-step exit-bisection stall near t = 1 and at UTM 5e6
    off_near1 = 1.0 - math.ldexp(1.0, -50)
    _, stalls_t1, _ = _rule_intersection_exit_bisect(
        (0.0, 0.0, 0.0), (1.0, 0.0, 0.0), lambda p: p[0], off_near1
    )
    utm = 5.0e6
    p_utm, _, coord_stalls_utm = _rule_intersection_exit_bisect(
        (utm, 0.0, 0.0), (utm + 1.0, 0.0, 0.0), lambda p: p[0] - utm, 1.0e-3
    )
    replays.append(dict(
        name='rule_intersection_exit_bisection_60step_stall_replay',
        near1_mid_stalls=stalls_t1,
        utm_coord_stalls=coord_stalls_utm,
        utm_exit_err=abs((p_utm[0] - utm) - 1.0e-3),
        passed=(stalls_t1 >= 6 and coord_stalls_utm >= 25 and abs((p_utm[0] - utm) - 1.0e-3) <= 1e-8),
    ))
    return replays


def run(timeout_ms=120000):
    source_contract(WRAP_SOURCE, WRAP_ANCHORS)
    source_contract(ORACLE_SOURCE, ORACLE_ANCHORS)
    obligations = (
        pull_pinch_geometry_obligations()
        + refine_non_manifold_loop_obligations()
        + lipschitz_bisection_obligations()
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
