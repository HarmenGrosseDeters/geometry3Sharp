"""Bisector predicates and their symbolic perturbation (docs/design/BISECTOR_PREDICATES.md; certified remeshing stage 1b).

Real-arithmetic obligations (nonlinear, quantifier free) for the formulas of tools/PredicateCompiler/bisector.spec and the
cascades of math/ExactBisectorSoS.cs. The error bounds are not modelled here (the generator and the Symbolic Taylor check own
them); these lemmas are about the exact formulas:

1. the perturbation: with free weights w (any reals, so also the infinitesimal ones of the cascade), each perturbed value is
   the plain value plus a linear form in the weights with the coefficients the cascade uses, and it equals the geometric
   value (the power difference at the weighted point, times the denominator); the coefficients sum to 0 and the one of the
   tested site is the denominator, so the cascade always decides where the point exists;
2. consistency: the same point gives the same side whichever site anchors the formula, and a side value along a clipped
   edge is affine, so a crossing exists exactly between ends of opposite signs, and never on an edge parallel to the bisector;
3. the rewritten R2 / R3 tests: sign(|N|^2 - r^2 d^2) is the sign of |y|^2 - r^2 for y = N / d, and the radius-edge test is
   the sign of R^2 - rho^2 |pq|^2 for the circumradius R.

Every UNSAT obligation has a SAT mutant (a broken rule) so that the check has power. Coordinates are relative to the site a
(a = 0), as in the spec; n is any vector (every vector is the normal (t1 - t0) x (t2 - t0) of some triangle) and h any real.
"""
import z3

from check_pilot_obligations import collect, main, source

SPEC = 'tools/PredicateCompiler/bisector.spec'
SOS = 'math/ExactBisectorSoS.cs'
# the transcribed spans (whitespace and line comments ignored)
CONTRACTS = [
    (SPEC, 'ipredicate BisectorSide2(', '# P1: the side', '80c0554b99eed3b8a42162d037097ae282499b2abf8d4ca886204c569ff06f56'),
    (SPEC, 'ipredicate BisectorSide3(', '# The coefficients', 'ddf135ac216e7efb996d9e040a62deb1c2a7cdc377cbd0b99c331a875882ff5e'),
    (SPEC, 'ipredicate DistanceAbove2(', '# At x = BTPI', '82de77444d7728c7a6ababd4ff1de68802934bacc328dc6d45ce799c8415c63b'),
    (SPEC, 'ipredicate DistanceAbove3(', '# R3:', 'd2703c1a2c78d14e131683ad060d57b507a1acd8e82c2f57ed555a0cb3d76d26'),
    (SPEC, 'ipredicate RadiusEdgeAbove(', 'sign L - R4', '55dfd14f461b7d33238e662f41cfc224c6f08c7012f0487445d44f17f76d7093'),
    (SOS, 'internal static int Side1(', 'internal static int Side2(', 'a7876962a78243a78d358eb600904a2931a8a77a15fd4bb0b46ed368ef6eaa50'),
    (SOS, 'internal static int Side2(', 'internal static int Side3(', 'bfdf4ad4881515f3d55b71048a25c3ec6118d6d4c09f5cf7cd0bc679293e8bc8'),
    (SOS, 'internal static int Side3(', 'internal static int InSphere(', '4e80747237f12fc00ab074e9d2b97e4ae6e44e636a37309c92138045b44661b0'),
]


def vec(name):
    return [z3.Real(f'{name}{k}') for k in 'xyz']


def dot(u, v):
    return u[0] * v[0] + u[1] * v[1] + u[2] * v[2]


def cross(u, v):
    return [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]


def add(*vs):
    return [sum(v[k] for v in vs) for k in range(3)]


def sub(u, v):
    return [u[k] - v[k] for k in range(3)]


def scale(s, v):
    return [s * v[k] for k in range(3)]


def det(u, v, w):
    return dot(u, cross(v, w))


def differs(u, v):
    return z3.Or(*[u[k] != v[k] for k in range(3)]) if isinstance(u, list) else u != v


def sgn(x):
    return z3.If(x > 0, 1, z3.If(x < 0, -1, 0))


def obligations():
    b, c, e, n, y = vec('b'), vec('c'), vec('e'), vec('n'), vec('y')
    q0, q1 = vec('q0'), vec('q1')
    h, r, t = z3.Reals('h r t')
    wa, wb, wc, we = z3.Reals('wa wb wc we')
    db, dc, de = wb - wa, wc - wa, we - wa
    bb, cc, ee = dot(b, b), dot(c, c), dot(e, e)
    D = lambda p, q: det(p, q, n)
    out = []

    # ---- P1 = BisectorSide3 (spec: N = bb (c' x n) + cc (n x b') + hh (b' x c'), det = b'.(c' x n), sign eN - ee det, det) ----
    dt = dot(b, cross(c, n))
    N3 = lambda pb, pc: add(scale(pb, cross(c, n)), scale(pc, cross(n, b)), scale(2 * h, cross(b, c)))
    N3w = N3(bb - db, cc - dc)                       # the weighted right-hand sides |b'|^2 - (w_b - w_a), |c'|^2 - (w_c - w_a)
    f3 = dot(e, N3(bb, cc)) - ee * dt
    f3w = dot(e, N3w) - (ee - de) * dt               # the perturbed value: pi_a - pi_e has the constant |e'|^2 - (w_e - w_a)
    # Cramer: N - 2 det y = residuals of the three plane equations 2 b'.y = R1, 2 c'.y = R2, n.y = h
    res = add(scale(bb - db - 2 * dot(b, y), cross(c, n)), scale(cc - dc - 2 * dot(c, y), cross(n, b)),
              scale(2 * (h - dot(n, y)), cross(b, c)))
    out.append(('side3_cramer_identity', [], differs(sub(N3w, scale(2 * dt, y)), res), 'unsat'))
    out.append(('side3_cramer_without_two_mutant', [], differs(sub(N3w, scale(2 * dt, y)), add(scale(bb - db - 2 * dot(b, y), cross(c, n)),
                scale(cc - dc - 2 * dot(c, y), cross(n, b)), scale(h - dot(n, y), cross(b, c)))), 'sat'))
    # the perturbed value is the geometric one: f3w = det (2 e'.y - |e'|^2 + w_e - w_a) wherever y solves the equations
    out.append(('side3_geometric_value', [], f3w != dt * (2 * dot(e, y) - ee + de) + dot(e, res), 'unsat'))
    out.append(('side3_geometric_without_residual_mutant', [], f3w != dt * (2 * dot(e, y) - ee + de), 'sat'))
    # linear in the weights, with the cascade's coefficients: w_a -D(b - e, c - e), w_b -D(e, c), w_c -D(b, e), w_e D(b, c)
    ka, kb, kc, ke = -D(sub(b, e), sub(c, e)), -D(e, c), -D(b, e), D(b, c)
    out.append(('side3_linear_coefficients', [], f3w != f3 + ka * wa + kb * wb + kc * wc + ke * we, 'unsat'))
    out.append(('side3_coefficients_sum_to_zero', [], ka + kb + kc + ke != 0, 'unsat'))
    out.append(('side3_sum_wrong_a_mutant', [], -ka + kb + kc + ke != 0, 'sat'))
    # the cascade decides: the tested site's coefficient is det, never 0 where the point exists
    out.append(('side3_cascade_decides', [dt != 0], z3.And(ka == 0, kb == 0, kc == 0, ke == 0), 'unsat'))
    out.append(('side3_without_tested_site_mutant', [dt != 0], z3.And(ka == 0, kb == 0), 'sat'))
    out.append(('side3_wrong_sign_mutant', [], f3w != f3 + ka * wa - kb * wb + kc * wc + ke * we, 'sat'))

    # ---- P4 = BisectorSide2 (l_k = 2 b'.q'_k - bb, N = q'0 l1 - q'1 l0, d = l1 - l0, sign 2 c'.N - cc d, d) ----
    l0, l1 = 2 * dot(b, q0) - bb + db, 2 * dot(b, q1) - bb + db      # the weighted bisector function at the ends
    N2w, d2 = sub(scale(l1, q0), scale(l0, q1)), l1 - l0
    f2w = 2 * dot(c, N2w) - (cc - dc) * d2
    l00, l10 = 2 * dot(b, q0) - bb, 2 * dot(b, q1) - bb
    f2 = 2 * dot(c, sub(scale(l10, q0), scale(l00, q1))) - cc * (l10 - l00)
    y2 = add(q0, scale(t, sub(q1, q0)))
    g = 2 * dot(b, y2) - bb + db                     # 0 at the crossing
    out.append(('side2_crossing_identity', [], differs(sub(scale(d2, y2), N2w), scale(g, sub(q1, q0))), 'unsat'))
    out.append(('side2_crossing_reversed_mutant', [], differs(sub(scale(d2, y2), N2w), scale(g, sub(q0, q1))), 'sat'))
    out.append(('side2_geometric_value', [],
                f2w != d2 * (2 * dot(c, y2) - cc + dc) - 2 * dot(c, sub(q1, q0)) * g, 'unsat'))
    out.append(('side2_geometric_factor_mutant', [], f2w != d2 * (2 * dot(c, y2) - cc + dc) - dot(c, sub(q1, q0)) * g, 'sat'))
    dq = sub(q1, q0)
    ka2, kb2, kc2 = 2 * dot(sub(c, b), dq), -2 * dot(c, dq), 2 * dot(b, dq)      # DotSites(b, c), -DotSites(a, c), DotSites(a, b)
    out.append(('side2_linear_coefficients', [], f2w != f2 + ka2 * wa + kb2 * wb + kc2 * wc, 'unsat'))
    out.append(('side2_coefficient_c_is_d', [], kc2 != d2, 'unsat'))
    out.append(('side2_coefficient_c_sign_mutant', [], kc2 != -d2, 'sat'))
    out.append(('side2_cascade_decides', [d2 != 0], z3.And(ka2 == 0, kb2 == 0, kc2 == 0), 'unsat'))
    out.append(('side2_cascade_one_site_mutant', [d2 != 0], ka2 == 0, 'sat'))
    out.append(('side2_missing_factor_two_mutant', [], f2w != f2 + dot(sub(c, b), dq) * wa + kb2 * wb + kc2 * wc, 'sat'))

    # ---- P3 = BisectorSide1 and the tie rule (the smaller id has the larger weight) ----
    v = vec('v')
    f1w = 2 * dot(e, v) - ee + de
    dist = lambda p, w: dot(sub(v, p), sub(v, p)) - w     # power distance
    zero = [z3.RealVal(0)] * 3
    out.append(('side1_is_the_power_difference', [], f1w != dist(zero, wa) - dist(e, we), 'unsat'))
    out.append(('side1_weight_sign_mutant', [], f1w != dist(zero, we) - dist(e, wa), 'sat'))
    out.append(('side1_tie_goes_to_the_larger_weight', [2 * dot(e, v) - ee == 0, we > wa], f1w <= 0, 'unsat'))
    out.append(('side1_tie_reversed_mutant', [2 * dot(e, v) - ee == 0, we < wa], f1w <= 0, 'sat'))

    # ---- consistency (Lemma S2) ----
    # one point, one answer: at a point of the power bisectors of (a, b) and (a, c), pi_a - pi_e = pi_b - pi_e = pi_c - pi_e
    x = vec('x')
    pa, pb, pc, pe = dist2(x, zero, wa), dist2(x, b, wb), dist2(x, c, wc), dist2(x, e, we)
    out.append(('anchor_independence', [pa == pb, pa == pc], z3.Or(pa - pe != pb - pe, pa - pe != pc - pe), 'unsat'))
    out.append(('anchor_without_tie_mutant', [pa == pb], pa - pe != pc - pe, 'sat'))
    # along a clipped edge from z0 to z1 a side value is affine; a crossing between ends of opposite signs is inside the edge
    z0, z1 = vec('z0'), vec('z1')
    lam = z3.Real('lam')
    side = lambda p: 2 * dot(e, p) - ee + de
    zl = add(z0, scale(lam, sub(z1, z0)))
    out.append(('crossing_inside_the_edge', [side(z0) < 0, side(z1) > 0, side(zl) == 0], z3.Or(lam <= 0, lam >= 1), 'unsat'))
    out.append(('crossing_closed_edge_mutant', [side(z0) <= 0, side(z1) >= 0, side(zl) == 0], z3.Or(lam <= 0, lam >= 1), 'sat'))
    # an edge parallel to the bisector (the side value does not change along it) never has ends on two sides, with or
    # without weights: its kind-2 / kind-3 point is never constructed (d = 0 or det = 0 is not reached)
    out.append(('parallel_edge_never_crosses', [dot(e, sub(z1, z0)) == 0], side(z0) * side(z1) < 0, 'unsat'))
    out.append(('parallel_edge_mutant', [dot(e, sub(z1, z0)) != 0], side(z0) * side(z1) < 0, 'sat'))

    # ---- R2: the distance tests (DistanceAbove2: |N|^2 - (r d)^2, d^2; DistanceAbove3: |N|^2 - (2 r det)^2, det^2) ----
    dd = z3.Real('dd')
    Nd = scale(dd, y)                                # y = N / d
    out.append(('r2_crossing_is_the_distance', [dd != 0],
                sgn(dot(Nd, Nd) - (r * dd) * (r * dd)) * sgn(dd * dd) != sgn(dot(y, y) - r * r), 'unsat'))
    out.append(('r2_crossing_unsquared_mutant', [dd != 0], sgn(dot(Nd, Nd) - r * r * dd) != sgn(dot(y, y) - r * r), 'sat'))
    N2t = scale(2 * dd, y)                           # y = N / (2 det)
    out.append(('r2_btpi_is_the_distance', [dd != 0],
                sgn(dot(N2t, N2t) - (2 * r * dd) * (2 * r * dd)) * sgn(dd * dd) != sgn(dot(y, y) - r * r), 'unsat'))
    out.append(('r2_btpi_without_two_mutant', [dd != 0], sgn(dot(N2t, N2t) - (r * dd) * (r * dd)) != sgn(dot(y, y) - r * r), 'sat'))
    Nf = vec('N')                                    # the generator's N where the point does not exist (d = 0): any vector
    out.append(('r2_missing_point_is_zero', [dd == 0], sgn(dot(Nf, Nf) - (r * dd) * (r * dd)) * sgn(dd * dd) != 0, 'unsat'))
    out.append(('r2_missing_point_without_factor_mutant', [dd == 0], sgn(dot(Nf, Nf) - (r * dd) * (r * dd)) != 0, 'sat'))

    # ---- R3: the radius-edge test against the circumradius (a = 0, the triangle 0, b, c; o its circumcentre) ----
    o = vec('o')
    rho2 = z3.Real('rho2')
    nt = cross(b, c)
    nn = dot(nt, nt)
    # the circumcentre: 2 nn o = |b|^2 (c x n) + |c|^2 (n x b), up to the residuals of o.b = |b|^2/2, o.c = |c|^2/2, o.n = 0
    oc_res = add(scale(bb - 2 * dot(o, b), cross(c, nt)), scale(cc - 2 * dot(o, c), cross(nt, b)), scale(-2 * dot(o, nt), cross(b, c)))
    oc = add(scale(bb, cross(c, nt)), scale(cc, cross(nt, b)))
    out.append(('r3_circumcentre_identity', [], differs(sub(oc, scale(2 * nn, o)), oc_res), 'unsat'))
    out.append(('r3_circumcentre_without_two_mutant', [], differs(sub(oc, scale(nn, o)), oc_res), 'sat'))
    # |2 nn o|^2 = nn |b|^2 |c|^2 |c - b|^2, so 4 nn R^2 = |b|^2 |c - b|^2 |c|^2 (the spec's L with k.y = 1)
    out.append(('r3_circumradius_identity', [], dot(oc, oc) != nn * bb * cc * dot(sub(c, b), sub(c, b)), 'unsat'))
    out.append(('r3_circumradius_without_nn_mutant', [], dot(oc, oc) != bb * cc * dot(sub(c, b), sub(c, b)), 'sat'))
    # (the identities above make L = 4 nn R^2; the sign step is about the numbers L, nn, R^2, |pq|^2 only)
    L, lpq, R2, nn = z3.Reals('L lpq R2 nnr')
    premises = [nn > 0, lpq >= 0, 4 * nn * R2 == L]
    out.append(('r3_is_the_circumradius_test', premises, sgn(L - 4 * rho2 * lpq * nn) != sgn(R2 - rho2 * lpq), 'unsat'))
    out.append(('r3_without_four_mutant', premises, sgn(L - rho2 * lpq * nn) != sgn(R2 - rho2 * lpq), 'sat'))
    return out


def dist2(x, p, w):
    return dot(sub(x, p), sub(x, p)) - w


def run(timeout_ms=120000):
    for item in CONTRACTS:
        source(*item)
    return collect(obligations(), timeout_ms)


if __name__ == '__main__':
    raise SystemExit(main(run))
