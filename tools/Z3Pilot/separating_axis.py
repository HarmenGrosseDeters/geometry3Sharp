"""Z3 check of the separating-axis prefilter (docs/design/SNAP_ROUNDING_3D.md, section 5; a candidate rule, the prototype
is benchmarks/SnapRoundingExperiment/CandidateCulling.cs, not library code).

The rule. For two triangles A, B with double corners and ANY vector a with double components (the prototype takes the
normal of one of them and the in-plane normals of its edges, computed in doubles; the lemma does not use where a comes
from), let for a corner x

    p~(x) = fl(fl(fl(a_x x_x) + fl(a_y x_y)) + fl(a_z x_z))                        the projection, in doubles
    s~(x) = fl(fl(fl(|a_x| |x_x|) + fl(|a_y| |x_y|)) + fl(|a_z| |x_z|))            its magnitude
    m~_T  = fl(GAMMA max_k s~(T_k)),  GAMMA = 8u, u = 2^-53                        the margin of triangle T
    lo~_T = fl(min_k p~(T_k) - m~_T),  hi~_T = fl(max_k p~(T_k) + m~_T).

The prefilter says "apart" iff lo~_B > hi~_A or lo~_A > hi~_B. Claim: then A and B have no common point, so
ExactTriangleIntersection.IntersectsBeyondShared is false (also for triangles that share a corner: they are never
"apart").

The proof in obligations, each UNSAT with satisfiable assumptions:
  R1  |p~ - p| <= 4u S, with p = t1 + t2 + t3 the exact projection and S = |t1| + |t2| + |t3| (t_i the exact products);
      every operation's result is the exact one times (1 + d), |d| <= u (Higham (2.4); no underflow, see below).
  R2  fl(GAMMA s~) >= 4u S: the margin computed in doubles covers the error of R1 (s~ rounds the same exact magnitudes).
  R3  if every exact projection of A is within its margin of the computed one, the same for B, and
      lo_B - m_B > hi_A + m_A in the reals, then max_k p(A_k) < min_k p(B_k).
  G1  (geometry) if max_k a.A_k < min_k a.B_k, A and B have no common point (corners with free real coordinates).
Bridges (reviewed, not Z3): rounding is monotonic, so fl(x) > fl(y) implies x > y: the comparison of the rounded lo~, hi~
implies the real one of R3; fl(|a| |x|) = |fl(a x)| (the sign is exact); min and max of doubles are exact; a
projection that is NaN or infinite makes every comparison false ("not apart").
Assumption (the supported range of MeshRoundingRepair, [2^-200, 2^200], no subnormal coordinate): products of an axis
component (a product of up to three coordinate differences) and a coordinate neither underflow to a subnormal nor
overflow; where an axis component underflows the axis is still some vector, which is all the lemma needs.

Mutants (must be SAT): GAMMA = 2u (a margin smaller than the error), the margin of one corner only, a non-strict
comparison (touching triangles), one corner of A left out of the maximum.
"""
import json
import os
import sys
from fractions import Fraction

import z3

U = Fraction(1, 2**53)
GAMMA = 8 * U


def q(x):
    return z3.RealVal(str(Fraction(x)))


def absr(x):
    return z3.If(x >= 0, x, -x)


def rounded(x, d):
    return x * (1 + d)


def deltas(prefix, n):
    ds = [z3.Real(f'{prefix}{i}') for i in range(n)]
    return ds, [z3.And(d >= -q(U), d <= q(U)) for d in ds]


def dot3(t, d):
    """fl(fl(fl(t1) + fl(t2)) + fl(t3)) for the exact products t (each rounded once), d the five relative errors"""
    return rounded(rounded(rounded(t[0], d[0]) + rounded(t[1], d[1]), d[3]) + rounded(t[2], d[2]), d[4])


def r1(bound=4):
    t = [z3.Real(f't{i}') for i in range(3)]
    d, dc = deltas('d', 5)
    p, s = t[0] + t[1] + t[2], absr(t[0]) + absr(t[1]) + absr(t[2])
    return dc, absr(dot3(t, d) - p) <= bound * q(U) * s


def r2(gamma=GAMMA):
    t = [z3.Real(f't{i}') for i in range(3)]
    e, ec = deltas('e', 6)
    mags = [absr(x) for x in t]
    margin = rounded(q(gamma) * dot3(mags, e), e[5])
    return ec, margin >= 4 * q(U) * (mags[0] + mags[1] + mags[2])


def r3(strict=True, margin_of_all=True):
    pa = [z3.Real(f'pa{k}') for k in range(3)]
    pb = [z3.Real(f'pb{k}') for k in range(3)]
    ca = [z3.Real(f'ca{k}') for k in range(3)]     # computed
    cb = [z3.Real(f'cb{k}') for k in range(3)]
    ea = [z3.Real(f'ea{k}') for k in range(3)]     # the error bound of each corner (4u S), >= 0
    eb = [z3.Real(f'eb{k}') for k in range(3)]
    ma, mb = z3.Real('ma'), z3.Real('mb')
    a = [e >= 0 for e in ea + eb]
    a += [absr(pa[k] - ca[k]) <= ea[k] for k in range(3)] + [absr(pb[k] - cb[k]) <= eb[k] for k in range(3)]
    # the margin of a triangle covers the error of each of its corners (R2, with the maximum over the corners)
    if margin_of_all:
        a += [ma >= e for e in ea] + [mb >= e for e in eb]
    else:
        a += [ma >= ea[0], mb >= eb[0]]
    for k in range(3):
        for j in range(3):
            a.append(cb[j] - mb > ca[k] + ma if strict else cb[j] - mb >= ca[k] + ma)
    claim = z3.And(*[pa[k] < pb[j] for k in range(3) for j in range(3)])
    return a, claim


def g1(strict=True, corners=3):
    A = [[z3.Real(f'A{k}{c}') for c in 'xyz'] for k in range(3)]
    B = [[z3.Real(f'B{k}{c}') for c in 'xyz'] for k in range(3)]
    ax = [z3.Real(f'a{c}') for c in 'xyz']
    lam = [z3.Real(f'l{k}') for k in range(3)]
    mu = [z3.Real(f'm{k}') for k in range(3)]
    proj = lambda x: ax[0] * x[0] + ax[1] * x[1] + ax[2] * x[2]
    a = [v >= 0 for v in lam + mu] + [lam[0] + lam[1] + lam[2] == 1, mu[0] + mu[1] + mu[2] == 1]
    for k in range(corners):
        for j in range(3):
            a.append(proj(A[k]) < proj(B[j]) if strict else proj(A[k]) <= proj(B[j]))
    common = [lam[0] * A[0][c] + lam[1] * A[1][c] + lam[2] * A[2][c] == mu[0] * B[0][c] + mu[1] * B[1][c] + mu[2] * B[2][c]
              for c in range(3)]
    return a, common


def check(formulas, timeout_ms):
    s = z3.Solver()
    s.set('timeout', timeout_ms)
    s.add(*formulas)
    return str(s.check())


def run(timeout_ms=120000):
    queries = {}

    def lemma(name, assumptions, claim):
        queries[name] = (assumptions + [z3.Not(claim)], 'unsat')
        queries[name + '_assumptions'] = (assumptions, 'sat')

    def mutant(name, assumptions, claim):
        queries['mutant_' + name] = (assumptions + [z3.Not(claim)], 'sat')

    lemma('R1_projection_error', *r1())
    lemma('R2_margin_covers_error', *r2())
    lemma('R3_decision', *r3())
    a, common = g1()
    queries['G1_apart_no_common_point'] = (a + common, 'unsat')
    queries['G1_assumptions'] = (a, 'sat')
    mutant('R1_bound_2u', *r1(bound=2))
    mutant('R2_gamma_2u', *r2(gamma=2 * U))
    mutant('R3_margin_of_one_corner', *r3(margin_of_all=False))
    mutant('R3_not_strict', *r3(strict=False))
    a, common = g1(strict=False)
    queries['mutant_G1_not_strict'] = (a + common, 'sat')
    a, common = g1(corners=2)
    queries['mutant_G1_two_corners'] = (a + common, 'sat')

    report = {}
    ok = True
    for name, (formulas, expected) in queries.items():
        got = check(formulas, timeout_ms)
        report[name] = {'expected': expected, 'got': got}
        ok = ok and got == expected
    return ok, report


if __name__ == '__main__':
    timeout = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
    ok, report = run(timeout)
    print(json.dumps(report, indent=1))
    out = os.environ.get('Z3_SEPARATING_AXIS_REPORT')
    if out:
        json.dump(report, open(out, 'w'), indent=1)
    sys.exit(0 if ok else 1)
