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
  B3  (T1) cos x <= 1 - x^2 / 2 + x^4 / 24 for every real x (the alternating Taylor series), so with x = 2 t:
      cos(2 t) <= c = 1 - 2 t^2 + (2/3) t^4. T1 proves cos(phi) >= c for the angle phi between the computed and the exact
      (den, num), so cos(phi) >= cos(2 t); arccos is decreasing on [-1, 1] and phi, 2 t are in [0, pi] (2 t <= 0.8), so
      phi <= 2 t. (Review of #1624: the earlier c = 1 - 2 t^2, which is the lower bound of cos 2t, gave only
      phi <= 2 asin t, which is above 2 t.) The angle phi is the change of atan2 (no wrap: the term is not taken where
      |num| <= tol and den <= tol, so the exact num has the computed one's sign or den > 0 in both).
  B4  T1's claim is invariant under a rotation and a positive scale of the (den, num) plane (T1 at (1, 0); its error box,
      rotated, lies in the disk of radius sqrt(2) tol). T1 holds up to t = tol / hypot of about 0.68 (checked: UNSAT at
      17/25, SAT at 7/10), so the unmodelled rounding of hypot, of 0.4 hypot and of 4 tol / hypot (a few u each) is covered
      by the step from the code's 0.4.
  Not covered: the SafeNorms pass of antipodal_sum (a squared norm outside [1e-200, 1e300], la from scaled_norm instead of
  the rounded sqrt of N2), which only differences next to the underflow range reach.
  Underflow: the relative-error model fails for every operation whose result is subnormal (or flushes to 0), e.g. a
  square of one tiny coordinate in N1 (a + b = (1e-3, 0, 1e-160)) or in N2 (a v_i^2), or a tiny product a_i b_i in D1
  (a = (1, 0, 1e-160), b = (0, 1, 1e-160)); there the rounding can also go up. Such an error is absolute, at most a few
  times 2^-1075 for the few operations involved, and the margins absorb it: N1 uses 7u of its 8u (the squared factor
  (1 + 4u)^2 against (1 + u)^7), N2 7u of its 10u, and S3 uses D1 only as an absolute term (4u (1 + 10u)) far above it.
  Every other floating operation is the exact result times (1 + d), |d| <= u = 2^-53 (Higham (2.4); no overflow:
  antipodal_sum takes the squared norms only in [1e-200, 1e300], and a and b are unit vectors).

The obligations, each UNSAT with satisfiable assumptions:
  N1  the rounded norm fl(sqrt(fl(fl(fl(x^2) + fl(y^2)) + fl(z^2)))) of the rounded sum s = fl(a + b) is within 4u of the
      exact |a + b| (in squares: (1 - 4u)^2 t^2 <= n^2 <= (1 + 4u)^2 t^2), in four queries: N1a the rounded sum, N1b the
      rounded sum of squares, N1c the rounded sqrt, N1 their composition (one query of all of it took up to the timeout).
  N2  a normalized vector (each coordinate fl(v_i / la), la the rounded norm of v) has 1 - 10u <= |a|^2 <= 1 + 10u: N2a
      the rounded division, then the composition with N1b (the squares of v) and N1c (the sqrt).
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
import time
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


def n1a():
    """N1a: the rounded sum s = fl(a + b), per coordinate, against v = a + b: |s|^2 within (1 +- u)^2 of |v|^2"""
    v = [z3.Real(f'v{i}') for i in range(3)]
    d, dc = deltas('d', 3)
    s2 = sum(fl(v[i], d[i]) * fl(v[i], d[i]) for i in range(3))
    v2 = sum(v[i] * v[i] for i in range(3))
    return dc, z3.And(s2 <= q((1 + U) ** 2) * v2, s2 >= q((1 - U) ** 2) * v2)


def n1b():
    """N1b: the rounded sum of squares fl(fl(fl(x^2) + fl(y^2)) + fl(z^2)) within (1 +- u)^3 of the exact one"""
    s = [z3.Real(f's{i}') for i in range(3)]
    e, ec = deltas('e', 5)
    sq = [fl(s[i] * s[i], e[i]) for i in range(3)]
    qq = fl(fl(sq[0] + sq[1], e[3]) + sq[2], e[4])
    s2 = sum(s[i] * s[i] for i in range(3))
    return ec, z3.And(qq <= q((1 + U) ** 3) * s2, qq >= q((1 - U) ** 3) * s2)


def n1c():
    """N1c: the rounded square root n = fl(sqrt(Q)): n^2 within (1 +- u)^2 of Q"""
    Q, r, n = z3.Reals('Q r n')
    g, gc = deltas('g', 1)
    asm = gc + [Q >= 0, r >= 0, r * r == Q, n == fl(r, g[0])]
    return asm, z3.And(n * n <= q((1 + U) ** 2) * Q, n * n >= q((1 - U) ** 2) * Q)


def n1(lo_factor=1 - NORM_U * U, hi_factor=1 + NORM_U * U):
    """N1: N1a-c composed (linear): the computed norm n of a + b has (1 - 4u)^2 t^2 <= n^2 <= (1 + 4u)^2 t^2"""
    A, S, Q, N = z3.Reals('A S Q N')
    asm = [A >= 0,
           S <= q((1 + U) ** 2) * A, S >= q((1 - U) ** 2) * A,
           Q <= q((1 + U) ** 3) * S, Q >= q((1 - U) ** 3) * S,
           N <= q((1 + U) ** 2) * Q, N >= q((1 - U) ** 2) * Q]
    return asm, z3.And(N <= q(hi_factor * hi_factor) * A, N >= q(lo_factor * lo_factor) * A)


def n2a():
    """N2a: one coordinate of the rounded division a_i = fl(v_i / la): a_i^2 la^2 within (1 +- u)^2 of v_i^2"""
    v, a, la = z3.Reals('v a la')
    d, dc = deltas('d', 1)
    asm = dc + [la > 0, a * la == fl(v, d[0])]
    return asm, z3.And(a * a * la * la <= q((1 + U) ** 2) * v * v, a * a * la * la >= q((1 - U) ** 2) * v * v)


def n2(bound=UNIT_U):
    """N2: a vector normalized by its rounded norm (la = fl(sqrt(sa)), sa the rounded squares of N1b, la^2 by N1c, each
    coordinate's division by N2a) has 1 - 10u <= |a|^2 <= 1 + 10u"""
    A, Sa, L2, P = z3.Reals('A Sa L2 P')
    # (w_i = v_i^2, p_i = a_i^2 la^2: N2a per coordinate; A = sum w_i, P la^2 = sum p_i)
    w = [z3.Real(f'w{i}') for i in range(3)]
    pp = [z3.Real(f'p{i}') for i in range(3)]
    asm = [A > 0, L2 > 0, P >= 0, A == w[0] + w[1] + w[2], P * L2 == pp[0] + pp[1] + pp[2],
           Sa <= q((1 + U) ** 3) * A, Sa >= q((1 - U) ** 3) * A,
           L2 <= q((1 + U) ** 2) * Sa, L2 >= q((1 - U) ** 2) * Sa]
    asm += [z3.And(w[i] >= 0, pp[i] <= q((1 + U) ** 2) * w[i], pp[i] >= q((1 - U) ** 2) * w[i]) for i in range(3)]
    return asm, z3.And(P <= q(1 + bound * U), P >= q(1 - bound * U))


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
    # cos(phi) >= c >= cos(x) (B3) with x = 2 t, t = tol / hypot = tol, so phi <= 2 t; for the mutant x = t (phi <= t)
    x2 = tol * tol if half_angle else 4 * tol * tol
    c = 1 - x2 / 2 + x2 * x2 / 24
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

    lemma('N1a_rounded_sum', *n1a())
    lemma('N1b_rounded_squares', *n1b())
    lemma('N1c_rounded_sqrt', *n1c())
    lemma('N1_rounded_norm', *n1())
    lemma('N2a_rounded_division', *n2a())
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
    mutant('N1_bound_3u', *n1(1 - 3 * U, 1 + 3 * U))
    mutant('N2_bound_6u', *n2(bound=6))

    report = {}
    ok = True
    for name, (formulas, expected) in queries.items():
        t0 = time.perf_counter()
        got = check(formulas, timeout_ms)
        report[name] = {'expected': expected, 'got': got, 'seconds': round(time.perf_counter() - t0, 2)}
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
