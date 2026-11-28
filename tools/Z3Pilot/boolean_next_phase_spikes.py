"""Formal Z3 proofs supporting the 2026-11 exact-boolean next-phase spikes.

Real-arithmetic (QF_NRA / QF_LRA) obligations for four spike lemmas:

1. Spike 2B-0 Part A / 4A-1 (degree-4 transverse radial-order fast path):
   Project perpendicular to directed edge e = u -> v = (0, 0, +1); each incident
   half-plane defines a nonzero 2D ray r_i and Orient3d(u, v, p_i, p_j) has the
   exact sign of cross2(r_i, r_j). Fixing r_0 = a1 = (1, 0) WLOG:
   - When both surfaces are locally flat (a2 = -a1, b2 = -b1), 1 Orient3d test
     (cross(a1, b1) > 0) proves strict CCW cyclic order a1 -> b1 -> a2 -> b2.
   - For arbitrary non-flat dihedral creases along e, the 4 consecutive
     Orient3d > 0 tests (cross(a1,b1) > 0, cross(b1,a2) > 0, cross(a2,b2) > 0,
     cross(b2,a1) > 0) are BOTH necessary and sufficient for strict CCW cyclic
     order (checked against an independent non-circular half-plane total order):
     dropping any single one admits a SAT counterexample, and double-wrapping is
     impossible for n = 4 (4 * pi < 4*pi), whereas n = 5 admits a SAT pentagram
     double-wrap (so degree >= 5 must keep general radial_order).

2. Spike 3-0 (differentiable volumetric CSG moment polynomials):
   Exact algebraic equivalence over all reals of the closed-form simplex averages
   TriSq, TriCube, TriInt2, TriInt3 against the analytical monomial integral
   (1/|T|) int_T l0^i l1^j l2^k dA = 2! i! j! k! / (i + j + k + 2)!.

3. Spike 2B-0 Part B (multi-material coupled rounding, shared=3 impossibility):
   Two distinct-sheet triangles sharing all 3 vertex IDs (a, b, c) admit NO
   separating plane under any vertex displacement in R^9 (combinatorially
   unresolvable by vertex moves alone; requires sub-ulp cell collapse or
   RoundingNotClean refusal), in contrast to a shared=2 seam hinge (a, b, c1)
   vs (a, b, c2), which IS separable (SAT).

4. Spike 4A-2-0 (UnionMany Sphere AND OBB conservative overlap filter):
   Any shared point p inside both bounding spheres and both OBBs forces both
   the bounding-sphere overlap test |cA - cB|^2 <= (rA + rB)^2 and every OBB
   separating-axis projection test to report overlap (zero false negatives).

Every UNSAT obligation is paired with SAT non-vacuity and controlled SAT mutants
so that the checks have verified power.
"""
from fractions import Fraction
from math import factorial

import z3

from check_pilot_obligations import collect, main


def cross2(a, b):
    return a[0] * b[1] - a[1] * b[0]


def abs_real(x):
    return z3.If(x >= 0, x, -x)


def strictly_before_from_pos_x(p, q):
    """Exact non-circular total order 0 < theta(p) < theta(q) < 2*pi in R^2.

    Partition R^2 \\ {y == 0 and x >= 0} into:
      H+ : y > 0                  (angle in (0, pi))
      N- : y == 0 and x < 0       (angle == pi)
      H- : y < 0                  (angle in (pi, 2*pi))
    Points on the non-negative x-axis (including the origin) return False.
    """
    px, py = p
    qx, qy = q
    p_up = py > 0
    q_up = qy > 0
    p_neg = z3.And(py == 0, px < 0)
    q_neg = z3.And(qy == 0, qx < 0)
    p_dn = py < 0
    q_dn = qy < 0
    c = cross2(p, q)
    return z3.Or(
        z3.And(p_up, q_up, c > 0),
        z3.And(p_up, z3.Or(q_neg, q_dn)),
        z3.And(p_neg, q_dn),
        z3.And(p_dn, q_dn, c > 0),
    )


def exact_monomial_avg(i, j, k):
    return Fraction(2 * factorial(i) * factorial(j) * factorial(k), factorial(i + j + k + 2))


def exact_avg_prod2(p, q):
    acc = 0
    for i1 in range(3):
        for i2 in range(3):
            exps = [0, 0, 0]
            exps[i1] += 1
            exps[i2] += 1
            w = exact_monomial_avg(*exps)
            acc = acc + z3.Q(w.numerator, w.denominator) * p[i1] * q[i2]
    return acc


def exact_avg_prod3(p, q, r):
    acc = 0
    for i1 in range(3):
        for i2 in range(3):
            for i3 in range(3):
                exps = [0, 0, 0]
                exps[i1] += 1
                exps[i2] += 1
                exps[i3] += 1
                w = exact_monomial_avg(*exps)
                acc = acc + z3.Q(w.numerator, w.denominator) * p[i1] * q[i2] * r[i3]
    return acc


def obligations():
    out = []

    # ---- Family 1: Spike 2B-0 Part A / 4A-1 degree-4 transverse Orient3d fast path ----
    a1 = (z3.RealVal(1), z3.RealVal(0))
    a2_flat = (z3.RealVal(-1), z3.RealVal(0))
    bx, by = z3.Reals('bx by')
    b1_flat = (bx, by)
    b2_flat = (-bx, -by)
    ccw4_flat = z3.And(
        strictly_before_from_pos_x(b1_flat, a2_flat),
        strictly_before_from_pos_x(a2_flat, b2_flat),
    )
    out.append(('deg4_flat_premises_sat', [], cross2(a1, b1_flat) > 0, 'sat'))
    out.append(('deg4_flat_1orient_sufficient', [cross2(a1, b1_flat) > 0], z3.Not(ccw4_flat), 'unsat'))
    out.append(('deg4_flat_1orient_reversed_mutant', [cross2(a1, b1_flat) < 0], z3.Not(ccw4_flat), 'sat'))

    a2x, a2y, b1x, b1y, b2x, b2y = z3.Reals('a2x a2y b1x b1y b2x b2y')
    a2 = (a2x, a2y)
    b1 = (b1x, b1y)
    b2 = (b2x, b2y)
    nz = [
        a2x * a2x + a2y * a2y > 0,
        b1x * b1x + b1y * b1y > 0,
        b2x * b2x + b2y * b2y > 0,
    ]
    c_a1_b1 = cross2(a1, b1) > 0
    c_b1_a2 = cross2(b1, a2) > 0
    c_a2_b2 = cross2(a2, b2) > 0
    c_b2_a1 = cross2(b2, a1) > 0
    ccw4 = z3.And(
        strictly_before_from_pos_x(b1, a2),
        strictly_before_from_pos_x(a2, b2),
    )
    # Non-vacuity: genuinely non-flat crease (a2 != -a1, b2 != -b1) satisfies all 4 signs
    out.append((
        'deg4_crease_4orient_premises_sat',
        [],
        z3.And(*nz, c_a1_b1, c_b1_a2, c_a2_b2, c_b2_a1, a2y != 0, b1x + b2x != 0),
        'sat',
    ))
    # Sufficiency: 4 consecutive Orient3d > 0 alone guarantee exact CCW order a1 -> b1 -> a2 -> b2
    out.append((
        'deg4_crease_4orient_sufficient',
        nz + [c_a1_b1, c_b1_a2, c_a2_b2, c_b2_a1],
        z3.Not(ccw4),
        'unsat',
    ))
    # Strict necessity: dropping any 1 of the 4 Orient3d tests admits a SAT counterexample
    out.append(('deg4_crease_drop_a2b2_mutant', nz + [c_a1_b1, c_b1_a2, c_b2_a1], z3.Not(ccw4), 'sat'))
    out.append(('deg4_crease_drop_b1a2_mutant', nz + [c_a1_b1, c_a2_b2, c_b2_a1], z3.Not(ccw4), 'sat'))
    out.append(('deg4_crease_drop_a1b1_mutant', nz + [c_b1_a2, c_a2_b2, c_b2_a1], z3.Not(ccw4), 'sat'))
    out.append(('deg4_crease_drop_b2a1_mutant', nz + [c_a1_b1, c_b1_a2, c_a2_b2], z3.Not(ccw4), 'sat'))

    # Boundary of theorem: for n = 5 rays, 5 consecutive Orient3d > 0 CAN double-wrap (pentagram -> SAT)
    r1x, r1y, r2x, r2y, r3x, r3y, r4x, r4y = z3.Reals('r1x r1y r2x r2y r3x r3y r4x r4y')
    r1, r2, r3, r4 = (r1x, r1y), (r2x, r2y), (r3x, r3y), (r4x, r4y)
    ccw5 = z3.And(
        strictly_before_from_pos_x(r1, r2),
        strictly_before_from_pos_x(r2, r3),
        strictly_before_from_pos_x(r3, r4),
    )
    out.append((
        'deg5_pentagram_double_wrap_mutant',
        [
            cross2(a1, r1) > 0,
            cross2(r1, r2) > 0,
            cross2(r2, r3) > 0,
            cross2(r3, r4) > 0,
            cross2(r4, a1) > 0,
        ],
        z3.Not(ccw5),
        'sat',
    ))

    # ---- Family 2: Spike 3-0 exact Hadamard moment polynomial identities ----
    u = z3.Reals('u0 u1 u2')
    v = z3.Reals('v0 v1 v2')
    a = z3.Reals('a0 a1 a2')

    tri_sq = (u[0] * u[0] + u[1] * u[1] + u[2] * u[2] + u[0] * u[1] + u[1] * u[2] + u[2] * u[0]) / 6
    tri_sq_mut = (u[0] * u[0] + u[1] * u[1] + u[2] * u[2] + 2 * u[0] * u[1] + u[1] * u[2] + u[2] * u[0]) / 6
    ex_sq = exact_avg_prod2(u, u)
    out.append(('moment_trisq_exact', [], tri_sq != ex_sq, 'unsat'))
    out.append(('moment_trisq_mutant', [], tri_sq_mut != ex_sq, 'sat'))

    tri_cube = ((u[0] + u[1] + u[2]) * (u[0] * u[0] + u[1] * u[1] + u[2] * u[2]) + u[0] * u[1] * u[2]) / 10
    tri_cube_mut = ((u[0] + u[1] + u[2]) * (u[0] * u[0] + u[1] * u[1] + u[2] * u[2])) / 10
    ex_cube = exact_avg_prod3(u, u, u)
    out.append(('moment_tricube_exact', [], tri_cube != ex_cube, 'unsat'))
    out.append(('moment_tricube_missing_prod_mutant', [], tri_cube_mut != ex_cube, 'sat'))

    tri_int2 = (
        u[0] * (2 * a[0] + a[1] + a[2])
        + u[1] * (a[0] + 2 * a[1] + a[2])
        + u[2] * (a[0] + a[1] + 2 * a[2])
    ) / 12
    tri_int2_mut = (
        u[0] * (a[0] + a[1] + a[2])
        + u[1] * (a[0] + 2 * a[1] + a[2])
        + u[2] * (a[0] + a[1] + 2 * a[2])
    ) / 12
    ex_int2 = exact_avg_prod2(u, a)
    out.append(('moment_triint2_exact', [], tri_int2 != ex_int2, 'unsat'))
    out.append(('moment_triint2_diag_mutant', [], tri_int2_mut != ex_int2, 'sat'))

    tri_int3_cross = (
        u[0] * (v[1] * a[2] + v[2] * a[1])
        + u[1] * (v[0] * a[2] + v[2] * a[0])
        + u[2] * (v[0] * a[1] + v[1] * a[0])
    )
    tri_int3_base = (
        6 * (u[0] * v[0] * a[0] + u[1] * v[1] * a[1] + u[2] * v[2] * a[2])
        + 2 * (
            u[0] * v[0] * (a[1] + a[2]) + u[1] * v[1] * (a[0] + a[2]) + u[2] * v[2] * (a[0] + a[1])
            + u[0] * a[0] * (v[1] + v[2]) + u[1] * a[1] * (v[0] + v[2]) + u[2] * a[2] * (v[0] + v[1])
            + v[0] * a[0] * (u[1] + u[2]) + v[1] * a[1] * (u[0] + u[2]) + v[2] * a[2] * (u[0] + u[1])
        )
    )
    tri_int3 = (tri_int3_base + tri_int3_cross) / 60
    tri_int3_mut = tri_int3_base / 60
    ex_int3 = exact_avg_prod3(u, v, a)
    out.append(('moment_triint3_exact', [], tri_int3 != ex_int3, 'unsat'))
    out.append(('moment_triint3_missing_cross_mutant', [], tri_int3_mut != ex_int3, 'sat'))

    # ---- Family 3: Spike 2B-0 Part B combinatorial shared=3 impossibility vs shared=2 separability ----
    # Plane signed values at vertices: sa = n.a + d, sb = n.b + d, sc = n.c + d (and sc2 = n.c2 + d for shared=2).
    # For shared=3 triangles T1=(a,b,c) and T2=(b,a,c), any plane putting T1 on >= 0 and T2 on <= 0 forces
    # sa == sb == sc == 0, so NO vertex can be strictly separated under ANY vertex displacement in R^9.
    sa, sb, sc, sc2 = z3.Reals('sa sb sc sc2')
    shared3_weak = [sa >= 0, sb >= 0, sc >= 0, sa <= 0, sb <= 0, sc <= 0]
    out.append(('shared3_premises_sat', [], z3.And(*shared3_weak), 'sat'))
    out.append(('shared3_no_strict_separator', shared3_weak, z3.Or(sa > 0, sb > 0, sc > 0), 'unsat'))
    # Contrast: shared=2 seam hinge T1=(a,b,c) and T2=(a,b,c2) IS strictly separable across a plane through edge (a,b)
    out.append((
        'shared2_hinge_separable_mutant',
        [sa == 0, sb == 0, sc >= 0, sc2 <= 0],
        z3.And(sc > 0, sc2 < 0),
        'sat',
    ))

    # ---- Family 4: Spike 4A-2-0 Sphere AND OBB conservative overlap filter ----
    # Sphere triangle-inequality (WLOG by rigid motion in the plane of cA, cB, p: cA=(0,0), cB=(d,0), p=(px,py)):
    d, px, py, rA, rB = z3.Reals('d px py rA rB')
    sph_prem = [rA >= 0, rB >= 0, d >= 0, px * px + py * py <= rA * rA, (px - d) * (px - d) + py * py <= rB * rB]
    out.append(('sphere_filter_premises_sat', [], z3.And(*sph_prem, d > 0), 'sat'))
    out.append(('sphere_filter_conservative', sph_prem, d * d > (rA + rB) * (rA + rB), 'unsat'))
    out.append(('sphere_filter_r2_sum_mutant', sph_prem, d * d > rA * rA + rB * rB, 'sat'))

    # OBB SAT projection lemma:
    # Step A (per-axis product bound): |t| <= e, e >= 0 => |t * alpha| <= e * |alpha|
    t, e, al = z3.Reals('t_obb e_obb alpha_obb')
    term_prem = [e >= 0, -e <= t, t <= e]
    out.append(('obb_sat_term_premises_sat', [], z3.And(*term_prem, e > 0, al != 0), 'sat'))
    out.append(('obb_sat_term_conservative', term_prem, abs_real(t * al) > e * abs_real(al), 'unsat'))
    out.append(('obb_sat_term_half_mutant', term_prem, 2 * abs_real(t * al) > e * abs_real(al), 'sat'))

    # Step B (6-axis sum bound, pure LRA): (oB - oA).L = sum_{i=0..2} d_i - sum_{i=0..2} g_i
    # with |d_i| <= bd_i and |g_i| <= bg_i => |(oB - oA).L| <= sum(bd_i) + sum(bg_i)
    ds = z3.Reals('d0 d1 d2')
    gs = z3.Reals('g0 g1 g2')
    bds = z3.Reals('bd0 bd1 bd2')
    bgs = z3.Reals('bg0 bg1 bg2')
    sum_prem = (
        [b >= 0 for b in bds + bgs]
        + [z3.And(-b <= x, x <= b) for x, b in zip(ds + gs, bds + bgs)]
    )
    proj_diff = (ds[0] + ds[1] + ds[2]) - (gs[0] + gs[1] + gs[2])
    rhs_full = bds[0] + bds[1] + bds[2] + bgs[0] + bgs[1] + bgs[2]
    rhs_drop = bds[0] + bds[1] + bds[2] + bgs[0] + bgs[1]
    out.append(('obb_sat_sum_premises_sat', [], z3.And(*sum_prem, bds[0] > 0, bgs[0] > 0), 'sat'))
    out.append(('obb_sat_sum_conservative', sum_prem, abs_real(proj_diff) > rhs_full, 'unsat'))
    out.append(('obb_sat_sum_missing_axis_mutant', sum_prem, abs_real(proj_diff) > rhs_drop, 'sat'))

    return out


def run(timeout_ms=120000):
    return collect(obligations(), timeout_ms)


if __name__ == '__main__':
    raise SystemExit(main(run))
