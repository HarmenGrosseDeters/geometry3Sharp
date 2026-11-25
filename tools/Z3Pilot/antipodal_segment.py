"""Z3 check of the segment test and of the wide bound of the Antipodal sum (#1587; mesh_ops/ExactMeshBoolean.cs, antipodal_sum).

The code. For a boundary segment with ends s0, s1 and the point p (grid doubles), antipodal_sum computes v = s0 - p,
la = sqrt(v.v), the unit vector a = v / la (the same b for s1), the error measures

    ea = (2.3e-16 (pm + |s0|_1) + pointError) / la,   eb the same for s1,   tol = 16 (ea + eb + 1e-15 + directionError),

returns 1 if not tol < 0.01 (the point next to an end), and then

    new (#1587):  |a + b| <= tol            (Vector3d ab = a + b; if (ab.Length <= tol) return 1;)
    old:          1 + a.b <= tol

"the point on or next to the segment, undecided whatever the ray". The old test is quadratic in the angle between -a
and b (1 + a.b = |a + b|^2 / 2) against a tol that is linear in the error of a and b, so it fired up to about sqrt(2 tol)
away (#1587: about u^(1/3) of the segment's length for a far point). For a term that passed, the sum bounds the error of
2 atan2(num, den) by 4 tol / hypot (hypot = |(den, num)|), where num and den are each within tol of their exact values.

The model (bridges, reviewed, not Z3; they are the model the sum's comments already state, with its 16 times margin):
  B1  |a - A| <= Ea = 2.01 ea + 5u for the exact unit vector A = V / |V| (V the exact difference): every coordinate of v is
      a difference of two rounded doubles, rounded once, so |v - V| <= |v - V|_1 <= 2.22e-16 (pm + |s0|_1) (1 + u), at most
      ea la; a unit vector moves by at most 2 |v - V| / |V| for a change |v - V| of its vector; the normalization by the
      rounded la adds the rounding of la and of the division (N2 below bounds |a|^2).
  B2  the triangle inequality: | |a + b| - |A + B| | <= |a - A| + |b - B|; and Cauchy-Schwarz |a_x b_x| + |a_y b_y| +
      |a_z b_z| <= |a| |b|.
  B3  (T1) cos x >= 1 - x^2 / 2 for every real x, and arccos is decreasing on [-1, 1]: so cos(phi) >= 1 - 2 t^2 with
      0 <= 2 t <= 0.8 gives phi <= 2 t. The angle phi between the computed (den, num) and the exact one is the change of
      atan2 (no wrap: the term is not taken where |num| <= tol and den <= tol, so the exact num has the computed one's sign
      or den > 0 in both).
  B4  the relative error model is invariant under a positive scale (N2 at |v| = 1), and T1's claim under a rotation and a
      positive scale of the (den, num) plane (T1 at (1, 0); its error box rotated lies in the disk of radius sqrt(2) tol).
  Every floating operation is the exact result times (1 + d), |d| <= u = 2^-53 (Higham (2.4); no underflow or overflow:
  antipodal_sum takes the squares only in [1e-200, 1e300], and a and b are unit vectors).

The obligations, each UNSAT with satisfiable assumptions:
  N1  the rounded norm fl(sqrt(fl(fl(fl(x^2) + fl(y^2)) + fl(z^2)))) of the rounded sum s = fl(a + b) is within 4u of the
      exact |a + b| (in squares: (1 - 4u)^2 t^2 <= n^2 <= (1 + 4u)^2 t^2).
  N2  a normalized vector (each coordinate fl(v_i / la), la the rounded norm of v) has 1 - 10u <= |a|^2 <= 1 + 10u.
  D1  the rounded dot product fl(fl(fl(a_x b_x) + fl(a_y b_y)) + fl(a_z b_z)) is within 4u (|a_x b_x| + |a_y b_y| + |a_z b_z|)
      of the exact one.
  S1  soundness: a point on the segment (A + B = 0) gives n <= tol, so status 1 (no ray is tried in vain).
  S2  linear: n <= tol gives |A + B| <= 1.2 tol (an angle of at most about 1.2 tol between -A and B).
  S3  bit-identity: n <= tol (with tol < 0.01) gives fl(1 + fl(a.b)) <= tol: whatever the new test sends to status 1 the
      old test sent there too, so the new test changes no value that was decided (only undecided ones become decided).
  T1  the wide bound: with |e_den|, |e_num| <= tol and tol <= 0.4 hypot, the angle between the computed and the exact
      (den, num) is at most 2 tol / hypot, so twice it is within the sum's 4 tol / hypot (the old argument used tol / hypot
      below 0.02, which maxBound 1e-4 gave; maxBound 0.25 of the wide evaluation does not, hence the check per term).

Mutants (must be SAT): S2 with the old test (quadratic: 1 + a.b <= tol does not bound |A + B| linearly), S1 with tol
without its factor 16, S3 without the 1e-15 of tol, T1 with tol <= 0.8 hypot, T1 with the angle bound tol / hypot.
"""
import json
import os
import sys
from fractions import Fraction

import z3

U = Fraction(1, 2**53)
# the constants of the code and of the model
TOL_FACTOR = 16
TOL_FLOOR = Fraction(1, 10**15)
ROUNDED_TOL = Fraction(1599, 100)      # fl(16 fl(fl(ea + eb) + 1e-15)) >= 15.99 (ea + eb + 1e-15)
EA_FACTOR = Fraction(201, 100)         # B1: |a - A| <= 2.01 ea + 5u
EA_U = 5
NORM_U = 4                             # N1
UNIT_U = 10                            # N2
DOT_U = 4                              # D1
LINEAR = Fraction(6, 5)                # S2: |A + B| <= 1.2 tol
CONDITION = Fraction(2, 5)             # T1: tol <= 0.4 hypot


def q(x):
    return z3.RealVal(str(Fraction(x)))


def absr(x):
    return z3.If(x >= 0, x, -x)


def deltas(prefix, n):
    ds = [z3.Real(f'{prefix}{i}') for i in range(n)]
    return ds, [z3.And(d >= -q(U), d <= q(U)) for d in ds]


def fl(x, d):
    return x * (1 + d)


def n1():
    """N1: the rounded norm of the rounded sum of a and b"""
    a = [z3.Real(f'a{i}') for i in range(3)]
    b = [z3.Real(f'b{i}') for i in range(3)]
    d, dc = deltas('d', 9)
    s = [fl(a[i] + b[i], d[i]) for i in range(3)]
    sq = [fl(s[i] * s[i], d[3 + i]) for i in range(3)]
    qq = fl(fl(sq[0] + sq[1], d[6]) + sq[2], d[7])
    r, n = z3.Real('r'), z3.Real('n')
    t2 = sum((a[i] + b[i]) * (a[i] + b[i]) for i in range(3))
    asm = dc + [r >= 0, r * r == qq, n == fl(r, d[8])]
    lo, hi = (1 - NORM_U * U), (1 + NORM_U * U)
    return asm, z3.And(n * n <= q(hi * hi) * t2, n * n >= q(lo * lo) * t2)


def n2():
    """N2: |a|^2 of a vector normalized by its rounded norm"""
    v = [z3.Real(f'v{i}') for i in range(3)]
    d, dc = deltas('d', 9)
    sq = [fl(v[i] * v[i], d[i]) for i in range(3)]
    sa = fl(fl(sq[0] + sq[1], d[3]) + sq[2], d[4])
    r, la = z3.Real('r'), z3.Real('la')
    v2 = v[0] * v[0] + v[1] * v[1] + v[2] * v[2]
    # a_i = fl(v_i / la): a_i la = v_i (1 + d)
    a = [z3.Real(f'a{i}') for i in range(3)]
    # (the model is invariant under a positive scale of v, B4: |v| = 1)
    asm = dc + [v2 == 1, r > 0, r * r == sa, la == fl(r, d[5])] + [a[i] * la == fl(v[i], d[6 + i]) for i in range(3)]
    a2 = a[0] * a[0] + a[1] * a[1] + a[2] * a[2]
    return asm, z3.And(a2 <= q(1 + UNIT_U * U), a2 >= q(1 - UNIT_U * U))


def d1(bound=DOT_U):
    """D1: the rounded dot product, with the exact products t_i"""
    t = [z3.Real(f't{i}') for i in range(3)]
    d, dc = deltas('d', 5)
    dot = fl(fl(fl(t[0], d[0]) + fl(t[1], d[1]), d[3]) + fl(t[2], d[2]), d[4])
    s = absr(t[0]) + absr(t[1]) + absr(t[2])
    return dc, absr(dot - (t[0] + t[1] + t[2])) <= bound * q(U) * s


def scalars(tol_factor=ROUNDED_TOL, floor=TOL_FLOOR):
    """the common assumptions of S1-S3: the error measures, tol, the norms and the bridges B1, B2, N1"""
    ea, eb, tol, t, T, n = z3.Reals('ea eb tol t T n')
    Ea = q(EA_FACTOR) * ea + q(EA_U * U)
    Eb = q(EA_FACTOR) * eb + q(EA_U * U)
    asm = [ea >= 0, eb >= 0, tol >= q(tol_factor) * (ea + eb + q(floor)), tol < q(Fraction(1, 100)),
           t >= 0, T >= 0, absr(t - T) <= Ea + Eb,
           n >= 0, n >= q(1 - NORM_U * U) * t, n <= q(1 + NORM_U * U) * t]
    return asm, ea, eb, tol, t, T, n


def s1(tol_factor=ROUNDED_TOL):
    asm, ea, eb, tol, t, T, n = scalars(tol_factor)
    return asm + [T == 0], n <= tol


def s2(old=False):
    asm, ea, eb, tol, t, T, n = scalars()
    if old:
        # the old test 1 + a.b <= tol, with 1 + a.b = t^2 / 2 (exact unit vectors would give this; the rounding only adds)
        return asm + [t * t / 2 <= tol], T <= q(LINEAR) * tol
    return asm + [n <= tol], T <= q(LINEAR) * tol


def s3(floor=TOL_FLOOR):
    asm, ea, eb, tol, t, T, n = scalars(floor=floor)
    al, be, P, D, c = z3.Reals('al be P D c')
    dd, ddc = deltas('dd', 1)
    asm = asm + ddc + [n <= tol,
                       al >= q(1 - UNIT_U * U), al <= q(1 + UNIT_U * U), be >= q(1 - UNIT_U * U), be <= q(1 + UNIT_U * U),
                       # |a + b|^2 = |a|^2 + |b|^2 + 2 a.b
                       t * t == al + be + 2 * P,
                       # D1 with Cauchy-Schwarz (B2): sum |a_i b_i| <= |a| |b| <= (1 + 10u)
                       absr(D - P) <= q(DOT_U * U * (1 + UNIT_U * U)),
                       c == fl(1 + D, dd[0])]
    return asm, c <= tol


def t1(condition=CONDITION, half_angle=False):
    """T1 at (den, num) = (1, 0): the claim is invariant under a rotation and a positive scale of the plane (B4), so the
    computed (den, num) is taken as (1, 0), hypot 1, and the error box |e_den|, |e_num| <= tol as the disk of radius
    sqrt(2) tol that contains it in every rotated frame; V = (1, 0) - e is the exact (den, num)"""
    ex, ey, tol = z3.Reals('ex ey tol')
    Vx, Vy = 1 - ex, -ey
    V2 = Vx * Vx + Vy * Vy
    asm = [tol >= 0, ex * ex + ey * ey <= 2 * tol * tol, tol <= q(condition)]
    # cos(phi) >= 1 - 2 t^2, t = tol / hypot = tol (phi <= 2 t), or, for the mutant, 1 - t^2 / 2 (phi <= t)
    k = q(Fraction(1, 2)) if half_angle else q(2)
    c = 1 - k * tol * tol
    return asm, z3.And(Vx >= 0, c >= 0, Vx * Vx >= V2 * c * c)


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

    lemma('N1_rounded_norm', *n1())
    lemma('N2_normalized_vector', *n2())
    lemma('D1_rounded_dot', *d1())
    lemma('S1_on_the_segment_is_status_1', *s1())
    lemma('S2_status_1_is_linear_in_the_angle', *s2())
    lemma('S3_new_status_1_implies_old_status_1', *s3())
    lemma('T1_wide_term_error', *t1())
    mutant('S2_old_quadratic_test', *s2(old=True))
    mutant('S1_tol_without_factor_16', *s1(tol_factor=Fraction(999, 1000)))
    mutant('S3_tol_without_floor', *s3(floor=0))
    mutant('T1_condition_0.8', *t1(condition=Fraction(4, 5)))
    mutant('T1_angle_tol_over_hypot', *t1(half_angle=True))
    mutant('D1_bound_2u', *d1(bound=2))

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
    out = os.environ.get('Z3_ANTIPODAL_SEGMENT_REPORT')
    if out:
        json.dump(report, open(out, 'w'), indent=1)
    sys.exit(0 if ok else 1)
