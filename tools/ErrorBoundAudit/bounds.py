"""The machine-derived part of the table of docs/research/ERROR_BOUND_AUDIT_2026-11.md: one model per bound.

A model writes the guarded expression and the code's bound with the operators of fpbound.py, in the order of the
source, and asks for the proof obligation (sign_dominates for a sign filter, dominates for an enclosure). Its `anchors`
are lines of the source (whitespace ignored) that must still be there, inside the function named by `scope` where one
is given: a changed expression, constant or guard fails test_error_bound_audit.py until the model is brought up to
date. The anchors are the lines the model transcribes and the guards it relies on; a change elsewhere in the function
(a new operation between them) is not seen, so a model is still re-read when its function changes.

Run: python3 tools/ErrorBoundAudit/audit.py
"""
from decimal import Decimal, getcontext
from fractions import Fraction
import math

import os
import re

from fpbound import U, UR, ETA, Poly, Mirrored, Verdict, dominates, sign_dominates, frac, norm3_lower

EPS = 1.1102230246251565e-16            # the code's literal of 2^-53
assert Fraction(EPS) == U
U52 = 2.220446049250313e-16             # 2^-52
assert Fraction(U52) == 2 * U


class Entry:
    def __init__(self, ident, file, anchors, guards, assumption, claimed, model, sample=None):
        self.id, self.file, self.anchors, self.guards = ident, file, anchors, guards
        self.assumption, self.claimed, self.model = assumption, claimed, model
        self.sample = dict(sample or {})
        # normal_only: derived where no product is subnormal; the subnormal range is covered by the bound's absolute
        # term (+ Tiny) by the note's argument, and sampled (the final claim only) with exponents down to tiny_emin
        self.normal_only = self.sample.pop('normal_only', False)
        self.tiny_emin = self.sample.pop('tiny_emin', -1070)
        self.samples = self.sample.pop('samples', None)
        # scope: text that starts the function the anchors must be in (the anchors are searched from there on, in the
        # next SCOPE_CHARS characters without whitespace); None: anywhere in the file
        self.scope = self.sample.pop('scope', None)


ENTRIES = []


def entry(ident, file, anchors, guards, assumption, claimed, **sample):
    def deco(fn):
        ENTRIES.append(Entry(ident, file, anchors, guards, assumption, claimed, fn, sample))
        return fn
    return deco


class Run:
    """what a model reports: verdicts (symbolic run) and observations (concrete run: error against the bound)"""

    def __init__(self, ctx):
        self.ctx, self.verdicts, self.observed = ctx, [], []

    def add(self, label, verdict):
        self.verdicts.append((label, verdict))

    def finding(self, label, ident, detail):
        """a claim this audit found not to hold as stated (the note's findings F1..): not proved, and expected so"""
        v = Verdict(False, None, detail)
        v.finding = ident
        self.verdicts.append((label, v))

    def observe_sign(self, value, bound):
        """concrete run: where the filter decides, the sign must be right; records |error| / bound"""
        c = self.ctx
        if c.concrete is None or value.fv is None or not math.isfinite(value.fv) or not math.isfinite(bound.fv):
            return
        e = abs(Fraction(value.fv) - value.xv)
        if bound.fv > 0:
            self.observed.append(float(e / Fraction(bound.fv)))
        if abs(value.fv) > bound.fv and (value.fv > 0) != (value.xv > 0):
            c.violations.append(('WRONG SIGN', value.fv, float(value.xv), bound.fv))

    def observe_err(self, value, bound):
        c = self.ctx
        if c.concrete is None or value.fv is None or not math.isfinite(value.fv) or not math.isfinite(bound.fv):
            return
        e = abs(Fraction(value.fv) - value.xv)
        if bound.fv > 0:
            self.observed.append(float(e / Fraction(bound.fv)))
        if e > Fraction(bound.fv):
            c.violations.append(('ERROR ABOVE BOUND', value.fv, float(e), bound.fv))


def floor_of(constant, limit):
    """the least value of fl(constant * x) for a double x > limit: the product's rounding and its underflow"""
    return frac(constant) * frac(limit) * (1 - UR) - ETA


def mleaves(c, names, kind='diff', **kw):
    if kind == 'diff':
        return [Mirrored.leaf(c.diff(n, **kw), U) for n in names.split()]
    return [Mirrored.leaf(c.exact(n, **kw), 0) for n in names.split()]


def mdet3(ux, uy, uz, vx, vy, vz, wx, wy, wz):
    """w . (u x v) in the order of ExactPredicates.Orient3dFilter (and of every copy of it in the exact code)"""
    return wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx)


# ------------------------------------------------------------------------------------------------- ExactPredicates

ORIENT2D = (3.0 + 16.0 * EPS) * EPS
ORIENT3D = (7.0 + 56.0 * EPS) * EPS
INCIRCLE = (10.0 + 96.0 * EPS) * EPS
INSPHERE = (16.0 + 224.0 * EPS) * EPS
UNDERFLOW_LIMIT = 1e-200
UNDERFLOW_UNIT = 2.0 ** -1066


@entry('EP-1 Orient2d filter', 'math/ExactPredicates.cs',
       ['~const double Orient2dBound = (3.0 + 16.0 * Epsilon) * Epsilon;', '~internal const double UnderflowLimit = 1e-200;',
        'double l = ux * vy, r = uy * vx;', 'double det = l - r;', 'double sum = Math.Abs(l) + Math.Abs(r);',
        'double bound = Orient2dBound * sum;', 'if (det > bound && sum > UnderflowLimit) return 1;', 'if (-det > bound && sum > UnderflowLimit) return -1;'],
       'sign of (b - a) x (c - a) from differences rounded once',
       'none on the coordinates; the guard sum > 1e-200 replaces the underflow term',
       '(3 + 16 u) u * sum', emin=-200, emax=200, scope='public static int Orient2d(Vector2d a, Vector2d b, Vector2d c)')
def orient2d(c, r):
    ux, uy, vx, vy = mleaves(c, 'ux uy vx vy')
    det = ux * vy - uy * vx
    m = det.magnitude_atom('SUM')
    b = c.const(ORIENT2D) * m
    r.add('sign', dominates(det.sign_error('SUM'), b.lo, b.defect, abs_floor=floor_of(ORIENT2D, UNDERFLOW_LIMIT)))
    r.observe_sign(det.v, b)


def with_underflow_note():
    """ExactPredicates.with_underflow returns `relative` alone where sum < 2^256. The claim that this is the same double
    as relative + sum * 2^-1066 wherever a filter decides: every bound constant >= 2^-51 and permanent > 1e-200 > 2^-665
    give relative >= 2^-716 (a double: rounding is monotone), half an ulp of it >= 2^-769, and sum * 2^-1066 < 2^-810."""
    assert min(ORIENT3D, INCIRCLE, INSPHERE) >= 2.0 ** -51 and Fraction(UNDERFLOW_LIMIT) > Fraction(1, 2**665)
    assert Fraction(1.157920892373162e77) == 2**256
    assert Fraction(2**256, 2**1066) + ETA < Fraction(1, 2**769)


@entry('EP-2 Orient3dFilter', 'math/ExactPredicates.cs',
       ['~internal const double Orient3dBound = (7.0 + 56.0 * Epsilon) * Epsilon;',
        'double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);',
        'double permanent = Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy))',
        'double bound = with_underflow(Orient3dBound * permanent, Math.Abs(wx) + Math.Abs(wy) + Math.Abs(wz) + 1);',
        'if (det > bound && permanent > UnderflowLimit) return 1;', 'if (-det > bound && permanent > UnderflowLimit) return -1;',
        '~internal const double UnderflowUnit = double.Epsilon * 256;', '~internal const double UnderflowSumNegligible = 1.157920892373162e77;',
        '~return sum < UnderflowSumNegligible ? relative : relative + sum * UnderflowUnit;'],
       'sign of ((b - a) x (c - a)) . (d - a) from differences rounded once',
       'none (the underflow term covers subnormal products; with_underflow drops it only where it rounds away)',
       '(7 + 56 u) u * permanent + 2^-1066 (|wx| + |wy| + |wz| + 1)', emin=-340, emax=300,
       scope='internal static int Orient3dFilter(double ux')
def orient3d(c, r):
    with_underflow_note()
    L = mleaves(c, 'ux uy uz vx vy vz wx wy wz')
    det = mdet3(*L)
    m = det.magnitude_atom('PERM')
    s = L[6].m + L[7].m + L[8].m + 1.0
    b = c.const(ORIENT3D) * m + s * c.const(UNDERFLOW_UNIT)
    r.add('sign', dominates(det.sign_error('PERM'), b.lo, b.defect))
    r.observe_sign(det.v, b)


@entry('EP-3 InCircle filter', 'math/ExactPredicates.cs',
       ['~internal const double InCircleBound = (10.0 + 96.0 * Epsilon) * Epsilon;',
        'if (det > bound && permanent > UnderflowLimit) return 1;',
        'double bdxcdy = bdx * cdy, cdxbdy = cdx * bdy, alift = adx * adx + ady * ady;',
        'double det = alift * (bdxcdy - cdxbdy) + blift * (cdxady - adxcdy) + clift * (adxbdy - bdxady);',
        'double permanent = (Math.Abs(bdxcdy) + Math.Abs(cdxbdy)) * alift',
        'double bound = with_underflow(InCircleBound * permanent, alift + blift + clift + Math.Abs(bdxcdy) + Math.Abs(cdxbdy)'],
       'sign of the incircle determinant from differences rounded once',
       'none (underflow term)', '(10 + 96 u) u * permanent + 2^-1066 (lifts + |products| + 1)', emin=-260, emax=200,
       scope='public static int InCircle(Vector2d a, Vector2d b, Vector2d c, Vector2d d)')
def incircle(c, r):
    adx, ady, bdx, bdy, cdx, cdy = mleaves(c, 'adx ady bdx bdy cdx cdy')
    bdxcdy, cdxbdy, alift = bdx * cdy, cdx * bdy, adx * adx + ady * ady
    cdxady, adxcdy, blift = cdx * ady, adx * cdy, bdx * bdx + bdy * bdy
    adxbdy, bdxady, clift = adx * bdy, bdx * ady, cdx * cdx + cdy * cdy
    det = alift * (bdxcdy - cdxbdy) + blift * (cdxady - adxcdy) + clift * (adxbdy - bdxady)
    m = det.magnitude_atom('PERM')
    s = (alift.m + blift.m + clift.m + bdxcdy.m + cdxbdy.m + cdxady.m + adxcdy.m + adxbdy.m + bdxady.m + 1.0)
    b = c.const(INCIRCLE) * m + s * c.const(UNDERFLOW_UNIT)
    r.add('sign', dominates(det.sign_error('PERM'), b.lo, b.defect, amgm=True))
    r.observe_sign(det.v, b)


@entry('EP-4 InSphere filter', 'math/ExactPredicates.cs',
       ['~internal const double InSphereBound = (16.0 + 224.0 * Epsilon) * Epsilon;',
        'if (det > bound && permanent > UnderflowLimit) return -1;',
        'double abc = aez * bc - bez * ac + cez * ab;', 'double bcd = bez * cd - cez * bd + dez * bc;',
        'double cda = cez * da + dez * ac + aez * cd;', 'double dab = dez * ab + aez * bd + bez * da;',
        'double det = (dlift * abc - clift * dab) + (blift * cda - alift * bcd);',
        'double permanent = (cdp * bezp + bdp * cezp + bcp * dezp) * alift',
        'double bound = with_underflow(InSphereBound * permanent, (alift + blift + clift + dlift) * (aezp + bezp + cezp + dezp + 1)'],
       'sign of the insphere determinant from differences rounded once',
       'none (underflow term)', '(16 + 224 u) u * permanent + 2^-1066 ((lifts)(|z| + 1) + |minors| + 1)',
       emin=-200, emax=150, scope='public static int InSphere(Vector3d a, Vector3d b, Vector3d c, Vector3d d, Vector3d e)')
def insphere(c, r):
    aex, bex, cex, dex, aey, bey, cey, dey, aez, bez, cez, dez = mleaves(c, 'aex bex cex dex aey bey cey dey aez bez cez dez')
    ab, bc, cd, da = aex * bey - bex * aey, bex * cey - cex * bey, cex * dey - dex * cey, dex * aey - aex * dey
    ac, bd = aex * cey - cex * aey, bex * dey - dex * bey
    abc = aez * bc - bez * ac + cez * ab
    bcd = bez * cd - cez * bd + dez * bc
    cda = cez * da + dez * ac + aez * cd
    dab = dez * ab + aez * bd + bez * da
    alift, blift = aex * aex + aey * aey + aez * aez, bex * bex + bey * bey + bez * bez
    clift, dlift = cex * cex + cey * cey + cez * cez, dex * dex + dey * dey + dez * dez
    # the four terms, each with its own computed magnitude (the code multiplies the minor's magnitude by the lift: the
    # same double as the mirrored product), then the code's two orders of adding them
    ta, tb, tc, td = bcd * alift, cda * blift, dab * clift, abc * dlift
    va, vb, vc, vd = ta.as_value('PA'), tb.as_value('PB'), tc.as_value('PC'), td.as_value('PD')
    det = (vd - vc) + (vb - va)
    perm = ((ta.magnitude_atom('PA') + tb.magnitude_atom('PB')) + tc.magnitude_atom('PC')) + td.magnitude_atom('PD')
    # (the |minors| of the underflow sum are not needed by this proof: the lifts' squares cover them, amgm)
    s = (alift.m + blift.m + clift.m + dlift.m) * (aez.m + bez.m + cez.m + dez.m + 1.0) + 1.0
    b = c.const(INSPHERE) * perm + s * c.const(UNDERFLOW_UNIT)
    r.add('sign', sign_dominates(det, b.lo, b.defect, amgm=True))
    r.observe_sign(det, b)


def volume_model(n):
    def model(c, r):
        total = perm = under = absd = None
        for t in range(n):
            L = mleaves(c, ' '.join('%s%d' % (k, t) for k in 'ux uy uz vx vy vz wx wy wz'.split()))
            d = mdet3(*L)
            dv = d.as_value2('M%d' % t, 'D%d' % t)      # det_t: magnitude |det_t| itself, error in its permanent M_t
            mt = d.magnitude_atom('M%d' % t)
            ut = L[6].m + L[7].m + L[8].m + 1.0
            # (the code starts its sums at 0: 0 + x is exact, so the first term is the sum)
            total = dv if total is None else total + dv
            absd = dv.abs() if absd is None else absd + dv.abs()
            perm = mt if perm is None else perm + mt
            under = ut if under is None else under + ut
        g = (n + 2) * EPS / (1 - (n + 2) * EPS)
        b = (c.const(ORIENT3D) * perm + c.const(UNDERFLOW_UNIT) * under + c.const(g) * absd) * (1 + 3 * g)
        r.add('enclosure, n = %d' % n, dominates(total.err, b.lo, b.defect))
        r.observe_err(total, b)
    return model


VOLUME = dict(file='math/ExactPredicates.cs',
              anchors=['double g = (n + 2) * Epsilon / (1 - (n + 2) * Epsilon);',
                       'bound = (Orient3dBound * perm + UnderflowUnit * under + g * abs) * (1 + 3 * g);',
                       'under += Math.Abs(wx) + Math.Abs(wy) + Math.Abs(wz) + 1;', 'return finite && g < 0.25;',
                       'double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);', 'sum += det;',
                       'abs += Math.Abs(det);', 'perm += Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy)) + Math.Abs(wy) * (Math.Abs(uzvx) + Math.Abs(uxvz))'],
              guards='|S_fp - S| <= E for the sum of n determinants (an enclosure, not only a sign)',
              assumption='g < 1/4 (checked by the code); derived for n = 1, 2, 7, 40 triangles only (n is symbolic in the code)',
              claimed='(Orient3dBound perm + 2^-1066 under + gamma_(n+2) abs)(1 + 3 gamma_(n+2))')
for _n in (1, 2, 7, 40):
    ENTRIES.append(Entry('EP-5 VolumeSumWithBound, n = %d' % _n, VOLUME['file'], VOLUME['anchors'], VOLUME['guards'],
                         VOLUME['assumption'], VOLUME['claimed'], volume_model(_n), dict(emin=-100, emax=100, samples=40 if _n < 10 else 1,
                              scope='internal static bool VolumeSumWithBound(double[] xyz')))


# -------------------------------------------------------------------------------------------------- CertifiedDet3

@entry('CD-1 CertifiedDet3.Compute', 'queries/CertifiedDet3.cs',
       ['internal const double TenU = 5.0 / 4503599627370496.0;', 'internal const double UnderflowUnit = 16 * double.Epsilon;',
        'double det = r1.x * (m1 - m2) + r1.y * (m3 - m4) + r1.z * (m5 - m6);',
        'double mp1 = ExactInterval.Up(mx + 1);', 'double t = ExactInterval.Up(TenU * perm);',
        'double a = ExactInterval.Up(mp1 * UnderflowUnit);', 'e = ExactInterval.Up(t + a);',
        'e = ExactInterval.Up(t * AbsorbFactor);', 'if (perm >= AbsorbedAbove * mp1) {'],
       '|Det - exact| <= E for a 3x3 determinant of rows rounded once (an enclosure)',
       'no overflow (checked: not finite is undecided)', 'Up(Up(10 u perm) + Up(2^-1070 Up(M + 1)))', emin=-340, emax=300)
def certified_det3(c, r):
    L = mleaves(c, 'r2x r2y r2z r3x r3y r3z r1x r1y r1z')
    det = mdet3(*L)
    m = det.magnitude_atom('PERM')
    # M, the largest absolute entry: every entry's atom is at most M
    mx, MX = c.param('MX')
    if c.concrete is not None:
        big = max(abs(x.v.fv) for x in L)
        mx.fv, mx.xv = big, Fraction(big)
        c.atoms['MX'] = Fraction(big)
    err = det.error('PERM')
    for x in 'r1x r1y r1z'.split():
        err = err.subst(x, MX)
    mp1 = (mx + 1.0).up()
    t = (c.const(5.0 / 4503599627370496.0) * m).up()
    e = (t + (mp1 * c.const(16 * 5e-324)).up()).up()
    r.add('enclosure (general branch)', dominates(err, e.lo, e.defect))
    r.observe_err(det.v, e)
    # the absorbed branch: perm >= 2^-980 mp1, E = Up(t (1 + 2^-39)) >= t + 2^-39 t (the product loses at most
    # u t (1 + 2^-39) < one ulp of the result, which Up gives back), and 2^-39 t >= 2^-39 10 u 2^-980 mp1
    absorbed = Fraction(1, 2**39) * 10 * U * Fraction(1, 2**980)
    assert Fraction(1.0 + 1.0 / 549755813888.0) == 1 + Fraction(1, 2**39)
    assert absorbed == Fraction(10, 4) * Fraction(1, 2**1070)
    lo = Poly.atom('PERM') * (10 * U) + (MX + 1) * absorbed
    r.add('enclosure (absorbed branch)', dominates(err, lo))


# ------------------------------------------------------------------------------- exact arithmetic claims (numbers)

def arith(label, ok, detail):
    """a check of numbers (widths, ranges, factors): no expression of the source is modelled"""
    return (label, Verdict(bool(ok), None, detail, kind='arithmetic'))


@entry('EP-6 TryCompareAbsVolume6 slack', 'math/ExactPredicates.cs',
       ['if ((a - bound) * (1 - 4 * Epsilon) > t3 * (1 + 4 * Epsilon)) { sign = 1; return true; }',
        'if ((a + bound) * (1 + 4 * Epsilon) < t3 * (1 - 4 * Epsilon)) { sign = -1; return true; }',
        'if (corners == null || corners.Length < 3 || !(t >= MinNormal) || double.IsInfinity(t))'],
       '|S| - E > 3 t and |S| + E < 3 t decided in doubles', 't a normal double (checked), 3 t finite (checked)',
       'a relative slack of 4 u on each side')
def compare_volume(c, r):
    one_m, one_p = Fraction(1 - 4 * EPS), Fraction(1 + 4 * EPS)
    assert one_m == 1 - 4 * U and one_p == 1 + 4 * U
    # greater: L^ <= (a - E)(1 + u')^2 (1 - 4u) (the difference and the product round up at most), R^ >= 3 t (1 + 4u) / (1 + u)^2
    # (3 t and the product are normal: t >= 2^-1022). L^ > R^ gives a - E > 3 t k with k below; k >= 1 is the claim.
    k = one_p / (1 + U) ** 2 / ((1 + UR) ** 2 * one_m)
    r.add('greater', Verdict(k >= 1, k, 'a - E > %s * 3 t' % float(k)))
    # less: L^ >= (a + E)(1 + 4u) / (1 + u)^2 - eta (a + E may be subnormal), R^ <= 3 t (1 - 4u)(1 + u')^2, and
    # eta <= (u / 3) 3 t: a + E < 3 t k2
    k2 = (one_m * (1 + UR) ** 2 + U / 3) * (1 + U) ** 2 / one_p
    r.add('less', Verdict(k2 <= 1, 1 / k2, 'a + E < %s * 3 t' % float(k2)))


@entry('EP-7 orient3d_int widths', 'math/ExactPredicates.cs',
       ['const double Pow62 = 4611686018427387904.0;', '// p q - s t in 128-bit two\'s complement (|p|, |q|, |s|, |t| < 2^62)',
        '// (mh, ml) * w in 192-bit two\'s complement (|m| < 2^125, |w| < 2^62)'],
       'the determinant of integer differences below 2^62 in 128 / 192 bit two\'s complement',
       '|difference| < 2^62 in units of 2^e (checked by diff)', 'minors < 2^125, sum < 2^189')
def orient3d_int(c, r):
    d = 2**62 - 1
    minor = 2 * d * d
    total = 3 * minor * d
    r.add(*arith('minor fits 128 bits', minor < 2**125 <= 2**127, '|minor| <= %d bits' % minor.bit_length()))
    r.add(*arith('sum fits 192 bits', total < 2**189 <= 2**191, '|sum| <= %d bits' % total.bit_length()))


@entry('AP-1 AdaptivePredicates range', 'math/ExactPredicates.cs',
       ['const double MinMagnitude = 7.888609052210118e-31;', 'const double MaxMagnitude = 1.2676506002282294e30;',
        'const double Splitter = 134217729.0;'],
       'no overflow or underflow in the expansions (Shewchuk assumes neither)',
       'every coordinate 0 or in [2^-100, 2^100] (Expansion.InRange, checked by the callers of AdaptivePredicates)',
       'degree-4 products and their rounding errors are normal')
def adaptive_range(c, r):
    assert Fraction(7.888609052210118e-31) == Fraction(1, 2**100) and Fraction(1.2676506002282294e30) == 2**100
    grid = Fraction(1, 2**152)           # a nonzero coordinate >= 2^-100 is a multiple of its ulp >= 2^-152
    r.add(*arith('no underflow', grid**4 >= Fraction(1, 2**1022), 'every nonzero value of degree <= 4 is a multiple of 2^-608'))
    # differences < 2^101, lifts < 2^203, incircle terms < 2^203 * 2 * 2^202, sum of three, times the splitter in a product's split
    big = 3 * 2**203 * 2 * 2**202 * 2**28
    r.add(*arith('no overflow', big < 2**1023, 'largest intermediate below 2^%d' % big.bit_length()))


SHEWCHUK = {  # Shewchuk 1997, section 4 (orient2d), 4.? (orient3d, incircle): the constants of predicates.c, in units of eps
    'ResultErrBound': (3, 8, 1), 'CcwErrBoundB': (2, 12, 1), 'CcwErrBoundC': (9, 64, 2), 'O3dErrBoundB': (3, 28, 1),
    'O3dErrBoundC': (26, 288, 2), 'IccErrBoundB': (4, 48, 1), 'IccErrBoundC': (44, 576, 2)}


@entry('AP-2 AdaptivePredicates constants', 'math/AdaptivePredicates.cs',
       ['const double ResultErrBound = (3.0 + 8.0 * Eps) * Eps;', 'const double CcwErrBoundB = (2.0 + 12.0 * Eps) * Eps;',
        'const double CcwErrBoundC = (9.0 + 64.0 * Eps) * Eps * Eps;', 'const double O3dErrBoundB = (3.0 + 28.0 * Eps) * Eps;',
        'const double O3dErrBoundC = (26.0 + 288.0 * Eps) * Eps * Eps;', 'const double IccErrBoundB = (4.0 + 48.0 * Eps) * Eps;',
        'const double IccErrBoundC = (44.0 + 576.0 * Eps) * Eps * Eps;',
        'double errbound = CcwErrBoundB * detsum;', 'errbound = CcwErrBoundC * detsum + ResultErrBound * Math.Abs(det);',
        'double errbound = O3dErrBoundB * permanent;', 'errbound = O3dErrBoundC * permanent + ResultErrBound * Math.Abs(det);',
        'double errbound = IccErrBoundB * permanent;', 'errbound = IccErrBoundC * permanent + ResultErrBound * Math.Abs(det);'],
       'stages B and C of orient2d, orient3d, incircle', 'AP-1', 'Shewchuk\'s constants, eps = 2^-53')
def adaptive_constants(c, r):
    # the constants of the source, parsed, against the paper's
    with open(os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), 'math/AdaptivePredicates.cs'), encoding='utf-8') as f:
        src = f.read()
    for name, (a, b_, p) in SHEWCHUK.items():
        m = re.search(r'const double %s = \((\d+)\.0 \+ (\d+)\.0 \* Eps\)((?: \* Eps)+);' % name, src)
        ok = m is not None and (int(m.group(1)), int(m.group(2)), m.group(3).count('Eps')) == (a, b_, p)
        r.verdicts.append((name, Verdict(ok, None, '(%d + %d eps) eps^%d' % (a, b_, p), kind='reference')))
    ok = re.search(r'const double Eps = 1\.1102230246251565e-16;', src) is not None
    r.verdicts.append(('Eps', Verdict(ok, None, '2^-53', kind='reference')))
    # stage B, first order, re-derived: B is the exact determinant of the rounded differences, so its error is the
    # tails' alone: (2 u + u^2) per product of two differences
    acx, bcy, acy, bcx = [c.diff(n, minmag=Fraction(1, 2**152)) for n in 'acx bcy acy bcx'.split()]
    need = (acx.hi * bcy.err + (bcy.hi + bcy.err) * acx.err) + (acy.hi * bcx.err + (bcx.hi + bcx.err) * acy.err)
    detsum = (acx * bcy).abs() + (acy * bcx).abs()
    b = c.const((2.0 + 12.0 * EPS) * EPS) * detsum
    v = dominates(need, b.lo, b.defect, abs_floor=None)
    # (first order only: the estimate's own error, at most ResultErrBound |det|, is not in this model, and the margin
    #  of 1 + 2.5 u against the tails does not cover it at second order; Shewchuk's proof does, by the computed values)
    r.verdicts.append(('CcwErrBoundB, first order: the tails need 2 u', Verdict(v.margin >= 1, v.margin, v.detail, kind='arithmetic')))


# ------------------------------------------------------------------------------------------------ ExactMeshBoolean

CONV = 2 * U        # a double made from an exact value by one truncation or rounding: |x^ - x| <= 2^-52 |x^| (one ulp of x^)
TINY_COORD = Fraction(1, 2**300)
BOOL = 'mesh_ops/ExactMeshBoolean.cs'


@entry('EB-1 front_side_filtered', BOOL,
       ['double a = pa[0] * (rx - gx), b = pa[1] * (ry - gy), c = pa[2] * (rz - gz);',
        'double mag = Math.Abs(pa[0]) * (Math.Abs(rx) + Math.Abs(gx)) + Math.Abs(pa[1]) * (Math.Abs(ry) + Math.Abs(gy))',
        'double bound = 1e-14 * mag;', 'if (!(mag < double.MaxValue) || mag < 1e-290)', 'double v = a + b + c;'],
       'sign of n . (r - g), n and the points given by doubles within one ulp',
       'inputs within 2^-52 of the exact values; mag >= 1e-290 (checked)', '1e-14 * mag', emin=-250, emax=250,
       scope='internal static int front_side_filtered(')
def front_side(c, r):
    pa = [c.leaf('pa%d' % k, Poly.atom('pa%d' % k) * CONV) for k in range(3)]
    rr = [c.leaf('r%d' % k, Poly.atom('r%d' % k) * CONV) for k in range(3)]
    g = [c.leaf('g%d' % k, Poly.atom('g%d' % k) * CONV) for k in range(3)]
    v = pa[0] * (rr[0] - g[0]) + pa[1] * (rr[1] - g[1]) + pa[2] * (rr[2] - g[2])
    mag = pa[0].abs() * (rr[0].abs() + g[0].abs()) + pa[1].abs() * (rr[1].abs() + g[1].abs()) + pa[2].abs() * (rr[2].abs() + g[2].abs())
    b = c.const(1e-14) * mag
    r.add('sign', sign_dominates(v, b.lo, b.defect, abs_floor=floor_of(1e-14, 1e-290)))
    r.observe_sign(v, b)


@entry('EB-2 plane_side_filtered', BOOL,
       ['double a = pa[0] * x, b = pa[1] * y, c = pa[2] * z, d = pa[3];',
        'double mag = Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(d);', 'double v = a + b + c - d;',
        'double bound = 1e-14 * mag;', 'if (!(mag < double.MaxValue) || mag < 1e-290)'],
       'sign of A x + B y + C z - D, all given by doubles within one ulp', 'as EB-1', '1e-14 * mag', emin=-250, emax=250,
       scope='internal static int plane_side_filtered(')
def plane_side(c, r):
    pa = [c.leaf('pa%d' % k, Poly.atom('pa%d' % k) * CONV) for k in range(4)]
    x = [c.leaf('x%d' % k, Poly.atom('x%d' % k) * CONV) for k in range(3)]
    a, b_, cc = pa[0] * x[0], pa[1] * x[1], pa[2] * x[2]
    v = a + b_ + cc - pa[3]
    mag = a.abs() + b_.abs() + cc.abs() + pa[3].abs()
    b = c.const(1e-14) * mag
    r.add('sign', sign_dominates(v, b.lo, b.defect, abs_floor=floor_of(1e-14, 1e-290)))
    r.observe_sign(v, b)


@entry('EB-3 plane_side_abs', BOOL,
       ['double bound = 1e-14 * mag + (Math.Abs(pa[0]) + Math.Abs(pa[1]) + Math.Abs(pa[2])) * e * (1 + 1e-15);',
        'double a = pa[0] * x.x, b = pa[1] * x.y, c = pa[2] * x.z, d = pa[3];', 'if (!(mag < double.MaxValue) || mag < 1e-290)',
        'double mag = Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(d);', 'double v = a + b + c - d;'],
       'sign of the plane equation at a point within e per coordinate of the exact one',
       'plane coefficients within one ulp; mag >= 1e-290 (checked)', '1e-14 * mag + (|A| + |B| + |C|) e (1 + 1e-15)',
       emin=-200, emax=200, scope='internal static int plane_side_abs(')
def plane_side_abs(c, r):
    pa = [c.leaf('pa%d' % k, Poly.atom('pa%d' % k) * CONV) for k in range(4)]
    e, E = c.param('E', scale=-30)
    x = [c.leaf('x%d' % k, E) for k in range(3)]
    a, b_, cc = pa[0] * x[0], pa[1] * x[1], pa[2] * x[2]
    v = a + b_ + cc - pa[3]
    mag = a.abs() + b_.abs() + cc.abs() + pa[3].abs()
    b = c.const(1e-14) * mag + (pa[0].abs() + pa[1].abs() + pa[2].abs()) * e * (1 + 1e-15)
    r.add('sign', sign_dominates(v, b.lo, b.defect, abs_floor=floor_of(1e-14, 1e-290)))
    r.observe_sign(v, b)


SQRT3_HI = Fraction(1732050807568877294, 10**18)
assert SQRT3_HI ** 2 >= 3
SQRT2_HI = Fraction(1414213562373095049, 10**18)
assert SQRT2_HI ** 2 >= 2


def rows_filter(c, r, EU, eu, label):
    """orient3d_rows_filtered: the rows as computed (exact leaves, each 0 or >= 2^-352: differences of coordinates that
    are 0 or >= 2^-300), eu, ev, ew doubles with the true row within tau * e of the computed one (Euclidean).
    Hadamard: |det(u + du, v + dv, w + dw) - det(u, v, w)| <= (|u| + |du|)(|v| + |dv|)(|w| + |dw|) - |u| |v| |w|
    (multilinear expansion, each of the 7 terms by Hadamard's inequality)."""
    mm = Fraction(1, 2**352)
    L = mleaves(c, 'ux uy uz vx vy vz wx wy wz', kind='exact', minmag=mm)
    det = mdet3(*L)
    perm = det.magnitude_atom('PERM')
    # norm(x, y, z) = fl(sqrt(x x + y y + z z) * (1 + 1e-15)) >= rho N, N the true norm of the computed row (atom)
    rho = norm3_lower(1 + 1e-15)
    n = []
    for k, row in enumerate('uvw'):
        nv, N = c.param('n' + row, minmag=Fraction(1, 2**353))
        if c.concrete is not None:
            x, y, z = (L[3 * k + i].v.fv for i in range(3))
            nv.fv = math.sqrt(x * x + y * y + z * z) * (1 + 1e-15)
            nv.xv = Fraction(nv.fv)
            c.atoms['n' + row] = nv.xv
        n.append((nv, N))
    (nu, NU), (nv, NV), (nw, NW) = n
    (eu_, ev_, ew_) = eu
    perturb = eu_ * nv * nw + nu * ev_ * nw + nu * nv * ew_ + eu_ * ev_ * nw + eu_ * nv * ew_ + nu * ev_ * ew_ + eu_ * ev_ * ew_
    b = c.const(1.01) * (perturb + c.const(1e-15) * perm)
    # the true norms are at most n / rho, the true row errors at most tau e (EU: polynomials in the e atoms)
    tn = [N * (1 / rho) for N in (NU, NV, NW)]
    need = (tn[0] + EU[0]) * (tn[1] + EU[1]) * (tn[2] + EU[2]) - tn[0] * tn[1] * tn[2]
    # (sign_dominates' argument: the whole error of the unrounded z, the rows' perturbation included, times (1 + u'))
    need = need * (1 + UR) + det.sign_error('PERM')
    r.add(label, dominates(need, b.lo, b.defect, abs_floor=frac(1.01) * frac(1e-15) * frac(1e-280) * (1 - UR) ** 3 - 2 * ETA))
    return det, b


@entry('EB-4 orient3d_filtered', BOOL,
       ['const double rel = 3 * 2.220446049250313e-16;',
        'double eu = rel * norm(Math.Abs(b.x) + ax, Math.Abs(b.y) + ay, Math.Abs(b.z) + az);',
        'double perturb = eu * nv * nw + nu * ev * nw + nu * nv * ew + eu * ev * nw + eu * nv * ew + nu * ev * ew + eu * ev * ew;',
        'double bound = 1.01 * (perturb + 1e-15 * permanent);',
        'if (!(bound < double.MaxValue) || !(Math.Abs(det) < double.MaxValue) || permanent < 1e-280)',
        'double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);', 'if (tiny(a.x) || tiny(a.y) || tiny(a.z)',
        '~return Math.Sqrt(x * x + y * y + z * z) * (1 + 1e-15);',
        '~internal const double TinyCoordinate = 4.909093465297727e-91;',
        '~static bool tiny(double v) { return v != 0 && !(Math.Abs(v) >= TinyCoordinate); }'],
       'sign of det(b - a, c - a, d - a) for points given by doubles within one ulp',
       'coordinates 0 or >= 2^-300 (checked: tiny); permanent >= 1e-280 (checked)',
       '1.01 ((|u|+eu)(|v|+ev)(|w|+ew) - |u||v||w| + 1e-15 permanent), eu = 3 * 2^-52 |(|b|+|a|)|',
       scope='internal static int orient3d_filtered(ref Vector3d a')
def orient3d_filtered(c, r):
    assert Fraction(4.909093465297727e-91) == TINY_COORD
    # a row coordinate fl(b^ - a^) against b - a: 2^-52 (|b^| + |a^|) for the inputs and u' (|b^| + |a^|) for the
    # difference; as a vector at most kappa S, S the true norm of (|b^| + |a^|) per coordinate.
    kappa = CONV + UR
    # eu = fl(rel * norm(fl(|b| + |a|), ...)) >= rel rho S: the components' sums round (1 - u') each
    rho_s = norm3_lower(1 + 1e-15, extra_sum_roundings=1) * (1 - UR)
    tau = kappa / (frac(3 * 2.220446049250313e-16) * rho_s)
    e = [c.param('e' + k, scale=-50, minmag=Fraction(1, 2**354)) for k in 'uvw']
    det, b = rows_filter(c, r, [E * tau for _, E in e], [v for v, _ in e], 'sign (row error %.3f eu)' % float(tau))


@entry('EB-5 orient3d_filtered_err / _near / rows', BOOL,
       ['const double half = 1.1102230246251565e-16, root3 = 1.7320508075688774;',
        'double eu = root3 * (ea + eb) * (1 + 1e-15) + half * norm(Math.Abs(b.x) + ax, Math.Abs(b.y) + ay, Math.Abs(b.z) + az);',
        'double ew = root3 * (ea + eb) * (1 + 1e-15) + half * norm(Math.Abs(b.x) + ax, Math.Abs(b.y) + ay, Math.Abs(b.z) + az);',
        'double ev = root3 * (ea + ec) * (1 + 1e-15) + half * norm(Math.Abs(c.x) + ax, Math.Abs(c.y) + ay, Math.Abs(c.z) + az);',
        'if (!(ea >= 0 && eb >= 0 && ec >= 0 && ed >= 0))', 'if (!(ep >= 0 && eq >= 0 && ea >= 0 && eb >= 0))',
        'static int orient3d_rows_filtered(double ux, double uy, double uz, double vx, double vy, double vz, double wx, double wy, double wz,'],
       'the same for points with absolute errors ea..ed per coordinate',
       'as EB-4; ea..ed >= 0 (checked)', 'eu = sqrt(3)(ea + eb)(1 + 1e-15) + u |(|b|+|a|)|')
def orient3d_filtered_err(c, r):
    # the true row error: sqrt(3)(ea + eb) + u' S. The computed eu = fl(fl(fl(root3 fl(ea + eb)) (1 + 1e-15)) + fl(half norm))
    root3, k = Fraction(1.7320508075688774), Fraction(1 + 1e-15)
    f1 = root3 * k * (1 - UR) ** 4                      # the coefficient of (ea + eb) in a lower bound of eu
    f2 = U * norm3_lower(1 + 1e-15, extra_sum_roundings=1) * (1 - UR) ** 2   # that of S
    tau = max(SQRT3_HI / f1, UR / f2)
    # (where ea + eb is subnormal its products round absolutely, by at most 2 eta in all: f2 S >= 2^-354 for a row that
    #  is not zero, so that is below 2^-720 of eu, inside the 1.01 of the bound)
    r.add(*arith('root3 (1 + 1e-15) rounded is at least sqrt(3)', (f1 * f1 >= 3), 'f1^2 - 3 = %.3g' % float(f1 * f1 - 3)))
    # (eu >= u |(|b| + |a|)| >= 2^-354 where the row is not zero; a zero row has permanent 0: undecided)
    e = [c.param('e' + k_, scale=-50, minmag=Fraction(1, 2**354)) for k_ in 'uvw']
    rows_filter(c, r, [E * tau for _, E in e], [v for v, _ in e], 'sign (row error %.6f eu)' % float(tau))


@entry('EB-6 widened', BOOL,
       ['double m = e * (1 + 1e-9) + 1e-300 + 1e-14 * Math.Abs(v);', 'lo = v - m;', 'hi = v + m;'],
       '[lo, hi] contains [v - e, v + e]', 'none', 'e (1 + 1e-9) + 1e-300 + 1e-14 |v|', emin=-300, emax=300)
def widened(c, r):
    v, V = c.param('V')
    e, E = c.param('E', scale=-20)
    m = e * (1 + 1e-9) + 1e-300 + c.const(1e-14) * v
    # fl(v - m) <= v - m + u' (|v| + m) must be <= v - e: e + u' (|v| + m) <= m, with m <= its upper bound
    need = E + (V + m.hi) * UR
    r.add('enclosure', dominates(need, m.lo, m.defect))
    if c.concrete is not None:
        lo, hi = v.fv - m.fv, v.fv + m.fv
        if not (Fraction(lo) <= Fraction(v.fv) - Fraction(e.fv) and Fraction(hi) >= Fraction(v.fv) + Fraction(e.fv)):
            c.violations.append(('widened', v.fv, e.fv))


def negative_volume_term(c):
    """one term x . (y x z) of negative_volume, Vector3d.Dot and Cross as the library writes them; returns the error's
    largest coefficient over the six monomials |x_i y_j z_k| (their sum is at most sqrt(2) |x| |y| |z|: the vector
    (|y_j z_k| + |y_k z_j|)_i has norm at most sqrt(2) |y| |z|, then Cauchy-Schwarz with x)"""
    x = [c.diff('x%d' % k) for k in range(3)]
    y = [c.diff('y%d' % k) for k in range(3)]
    z = [c.diff('z%d' % k) for k in range(3)]
    cr = [y[1] * z[2] - y[2] * z[1], y[2] * z[0] - y[0] * z[2], y[0] * z[1] - y[1] * z[0]]
    d = x[0] * cr[0] + x[1] * cr[1] + x[2] * cr[2]
    return d


@entry('EB-7 negative_volume', BOOL,
       ['v6 += x.Dot(y.Cross(z));', 'norms += x.Length * y.Length * z.Length;',
        'under += Math.Abs(x.x) + Math.Abs(x.y) + Math.Abs(x.z) + 1;',
        'return v6 < -((32.0 + component.Count) * 1.1102230246251565e-16 * norms + ExactPredicates.UnderflowUnit * under);'],
       'v6 < 0 is certain where v6 < -((32 + n) u S + 2^-1066 sum (|x|_1 + 1)), S the sum of |x| |y| |z|',
       'none: the absolute term covers the subnormal products (the relative part is derived where all are normal)',
       '(32 + n) u * sum of |x||y||z| + 2^-1066 * sum (|x|_1 + 1)', emin=-100, emax=100)
def negative_volume(c, r):
    d = negative_volume_term(c)
    rel = d.err.without_const()
    coeff = max(rel.t.values())
    per_term = coeff * SQRT2_HI             # |term^ - term| <= per_term * T (+ the absolute part: the finding)
    r.add(*arith('one term', per_term <= 32 * U, 'error at most %.3f u T (claimed: below 32 u T)' % float(per_term / U)))
    # n terms, recursive summation: |v6^ - v6| <= per_term S + gamma_(n-1) sum |term^|, |term^| <= (1 + per_term) T
    # (Hadamard: |term| <= T). The computed norms: each Length >= (1 - u)^2.5 of the true norm (norm3 without the
    # last product), the two products and the n - 1 sums of non-negative terms lose (1 - u') each; the bound's own
    # two products one more each.
    rho_t = ((1 - U) ** 3) ** 3 * (1 - UR) ** 2
    worst_n = None
    for bits in range(0, 32):
        n = 2**bits
        gamma = (n - 1) * U / (1 - (n - 1) * U)
        need = per_term + gamma * (1 + per_term)
        have = (32 + n) * U * rho_t * (1 - (n + 1) * UR)       # (1 - u')^(n + 1) >= 1 - (n + 1) u' (Bernoulli)
        if need <= have:
            worst_n = n
        else:
            break
    r.add(*arith('n terms', worst_n is not None and worst_n >= 2**26, 'holds up to n = 2^%d triangles' % (worst_n.bit_length() - 1)))
    if not d.err.const_part() > 0:
        raise AssertionError('the model lost its underflow term')
    # the absolute part (#1347): a subnormal product errs by at most eps / 2 (eps = 2^-1074 = 2 ETA); the cross product's six
    # products by eps per component, which the dot product multiplies by |x_i|, and its three products add 3 eps / 2: at most
    # eps (|x|_1 + 3/2) per term; the bound adds 2^-1066 (|x|_1 + 1) = 256 eps (|x|_1 + 1) per triangle. Where S underflows
    # (S < 2^-1022) the relative part (32 + n) u S is below (32 + n) eps / 2 <= 256 n eps (largest at n = 1)
    eps = 2 * ETA
    covers_terms = all(256 * eps * (a + 1) >= eps * (a + Fraction(3, 2)) for a in (Fraction(0), Fraction(1, 2), Fraction(1), Fraction(10 ** 6)))
    covers_relative = all((32 + n) * eps / 2 <= 256 * n * eps for n in (1, 2, 3, 10, 2 ** 26))
    r.add(*arith('underflow term', covers_terms and covers_relative,
                 'the absolute error of the subnormal products is below 2^-1066 (|x|_1 + 1) per term (a margin of 170 at |x|_1 = 0)'))


# --------------------------------------------------------------------------------------------- ExactMeshArrangement

ARR = 'mesh_ops/ExactMeshArrangement.cs'
TINY = 1e-300


def enclosure_check(c, v, e, w):
    """concrete run: [fl(v - w), fl(v + w)] contains [v - e, v + e] (and with v negated: the code widens both ways)"""
    if c.concrete is None:
        return
    for x in (v.fv, -v.fv):
        lo, hi = x - w.fv, x + w.fv
        if not (Fraction(lo) <= Fraction(x) - Fraction(e.fv) and Fraction(hi) >= Fraction(x) + Fraction(e.fv)):
            c.violations.append(('ENCLOSURE', x, e.fv, w.fv))


def diff_abs(c, name, E):
    """fl(p^ - q^) of two doubles within e of the exact p, q: |x^ - x| <= 2 e + u |x^|"""
    return c.leaf(name, E * 2 + Poly.atom(name) * U)


def subst_all(p, names, M):
    for n in names:
        p = p.subst(n, M)
    return p


@entry('AR-4 plane_value', ARR,
       ['double ax = A * x.x, by = B * x.y, cz = C * x.z;',
        'err = 1.01 * (3 * U * (Math.Abs(ax) + Math.Abs(by) + Math.Abs(cz) + Math.Abs(D)) + (Math.Abs(A) + Math.Abs(B) + Math.Abs(C)) * e);',
        'return ax + by + cz - D;', 'const double U = 2.220446049250313e-16;'],
       '|value - (A x + B y + C z - D)| <= err, coefficients within one ulp, the point within e per coordinate',
       'no product underflows: the coefficients are integers and e >= 1e-300 where a coordinate is not an integer '
       '(store adds Tiny); taken as (|A|+|B|+|C|) e >= 1e-300 or exact products', '1.01 (3 * 2^-52 sum|terms| + (|A|+|B|+|C|) e)',
       emin=-100, emax=100)
def plane_value(c, r):
    pa = [c.leaf(k, Poly.atom(k) * CONV, minmag=Fraction(1)) for k in 'ABCD']
    e, E = c.param('E', scale=-30)
    x = [c.leaf('x%d' % k, E) for k in range(3)]
    ax, by, cz = pa[0] * x[0], pa[1] * x[1], pa[2] * x[2]
    v = ax + by + cz - pa[3]
    b = c.const(1.01) * (c.const(3 * U52) * (ax.abs() + by.abs() + cz.abs() + pa[3].abs()) + (pa[0].abs() + pa[1].abs() + pa[2].abs()) * e)
    r.add('enclosure', dominates(v.err, b.lo, b.defect, abs_floor=frac(1.01) * frac(1e-300) * (1 - UR) ** 3))
    r.observe_err(v, b)


@entry('AR-6 orient2d_approx', ARR,
       ['double eps = 2 * e + U * M;', 'err = 1.01 * (2 * eps * (2 * M + eps) + 3 * U * M * M) + Tiny;',
        'double det = bx * cy - by * cx;', 'internal const double Tiny = 1e-300;'],
       '|det - det of the exact points| <= err, coordinates within e', 'no overflow (checked: not finite is undecided)',
       '1.01 (2 eps (2 M + eps) + 3 * 2^-52 M^2) + 1e-300, eps = 2 e + 2^-52 M', emin=-140, emax=140, normal_only=True)
def orient2d_approx(c, r):
    e, E = c.param('E', scale=-30)
    names = 'bx by cx cy'.split()
    bx, by, cx, cy = [diff_abs(c, n, E) for n in names]
    det = bx * cy - by * cx
    m, M = c.param('M')
    if c.concrete is not None:
        big = max(abs(x.fv) for x in (bx, by, cx, cy))
        m.fv = big
        m.xv = c.atoms['M'] = Fraction(big)
    eps = 2.0 * e + c.const(U52) * m
    b = c.const(1.01) * (2.0 * eps * (2.0 * m + eps) + c.const(3 * U52) * m * m) + TINY
    r.add('enclosure', dominates(subst_all(det.err, names, M), b.lo, b.defect))
    r.observe_err(det, b)


@entry('AR-8 order_key', ARR,
       ['err = 1.01 * (2.33 * e + 2 * U * (Math.Abs(x) + Math.Abs(y) + Math.Abs(z))) + Tiny;',
        'internal static readonly double[] Alpha = { 1.0, 0.7548776662466927, 0.5698402909980532 };',
        'double bound = 1.01 * (ea + eb);'],
       '|key - alpha . p| <= err for a point within e per coordinate', 'none', '1.01 (2.33 e + 2 * 2^-52 sum|alpha_k a_k|) + 1e-300',
       emin=-300, emax=300)
def order_key(c, r):
    e, E = c.param('E', scale=-30)
    a = [c.leaf('a%d' % k, E) for k in range(3)]
    x, y, z = c.const(1.0) * a[0], c.const(0.7548776662466927) * a[1], c.const(0.5698402909980532) * a[2]
    key = x + y + z
    b = c.const(1.01) * (2.33 * e + c.const(2 * U52) * (x.abs() + y.abs() + z.abs())) + TINY
    r.add('enclosure', dominates(key.err, b.lo, b.defect))
    r.observe_err(key, b)


@entry('AR-9 merge_points interval', ARR,
       ['e = 1.01 * e + U * Math.Abs(key);', 'lo[k] = key - e;', 'hi[k] = key + e;'],
       '[lo, hi] contains [key - err, key + err]', 'none', '1.01 err + 2^-52 |key|', emin=-300, emax=300, normal_only=True)
def merge_interval(c, r):
    key, K = c.param('KEY')
    e, E = c.param('E', scale=-20)
    w = c.const(1.01) * e + c.const(U52) * key
    r.add('enclosure', dominates(E + (K + w.hi) * UR, w.lo, w.defect, abs_floor=None))
    enclosure_check(c, key, e, w)
    # (the product 2^-52 |key| is exact or underflows: where it underflows |key| < 2^-970 and 0.01 e covers u' (|key| + w)
    #  only if e is not smaller still; then lo and hi are the neighbours' and the test stays conservative by the 1.01)


@entry('AR-12 incircle_approx', ARR,
       ['double eps = 2 * e + U * M, eta = 2 * eps * (2 * M + eps), M2 = M * M;',
        'bound = 1.01 * (3 * eta * (4 * M2 + eta) + 64 * U * M2 * M2) + Tiny;',
        'double fdet = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy)'],
       '|fdet - incircle determinant of the exact points| <= bound, coordinates within e',
       'no overflow (checked)', '1.01 (3 eta (4 M^2 + eta) + 64 * 2^-52 M^4) + 1e-300', emin=-60, emax=60, normal_only=True, tiny_emin=-260)
def incircle_approx(c, r):
    e, E = c.param('E', scale=-30)
    names = 'adx ady bdx bdy cdx cdy'.split()
    adx, ady, bdx, bdy, cdx, cdy = [diff_abs(c, n, E) for n in names]
    fdet = ((adx * adx + ady * ady) * (bdx * cdy - cdx * bdy) + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
            + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady))
    m, M = c.param('M')
    if c.concrete is not None:
        big = max(abs(x.fv) for x in (adx, ady, bdx, bdy, cdx, cdy))
        m.fv = big
        m.xv = c.atoms['M'] = Fraction(big)
    eps = 2.0 * e + c.const(U52) * m
    eta = 2.0 * eps * (2.0 * m + eps)
    m2 = m * m
    b = c.const(1.01) * (3.0 * eta * (4.0 * m2 + eta) + c.const(64 * U52) * m2 * m2) + TINY
    r.add('enclosure', dominates(subst_all(fdet.err, names, M), b.lo, b.defect))
    r.observe_err(fdet, b)


@entry('AR-13 along_approx', ARR,
       ['bound = 1.01 * (2 * e * (Math.Abs(fdx) + Math.Abs(fdy) + Math.Abs(fdz))',
        '+ 5 * U * (Math.Abs(fdx * ux) + Math.Abs(fdy * uy) + Math.Abs(fdz * uz))) + Tiny;',
        'return fdx * ux + fdy * uy + fdz * uz;'],
       '|value - d . (a - b)| <= bound, d within one ulp, the points within e', 'none',
       '1.01 (2 e sum|fd| + 5 * 2^-52 sum|fd_k u_k|) + 1e-300', emin=-140, emax=140, normal_only=True)
def along_approx(c, r):
    e, E = c.param('E', scale=-30)
    fd = [c.leaf('fd%d' % k, Poly.atom('fd%d' % k) * CONV) for k in range(3)]
    u = [diff_abs(c, 'u%d' % k, E) for k in range(3)]
    p = [fd[k] * u[k] for k in range(3)]
    v = p[0] + p[1] + p[2]
    b = c.const(1.01) * (2.0 * e * (fd[0].abs() + fd[1].abs() + fd[2].abs()) + c.const(5 * U52) * (p[0].abs() + p[1].abs() + p[2].abs())) + TINY
    r.add('enclosure', dominates(v.err, b.lo, b.defect))
    r.observe_err(v, b)


@entry('AR-14 BoxGrid2 box pad', ARR,
       ['double w = e + 4 * U * (Math.Abs(r[k]) + e);', 'r[k] += (k < 2) ? -w : w;'],
       'the box side moved by w contains the exact coordinate (within e of the approximation)', 'none',
       'e + 4 * 2^-52 (|r| + e)', emin=-300, emax=300, normal_only=True)
def box_pad(c, r):
    rv, R = c.param('R')
    e, E = c.param('E', scale=-20)
    w = e + c.const(4 * U52) * (rv + e)
    r.add('enclosure', dominates(E + (R + w.hi) * UR, w.lo, w.defect, abs_floor=None))
    enclosure_check(c, rv, e, w)


# --------------------------------------------------------------------------------------------------- SnapRounding2

@entry('SN-5 SnapGrid.CrossesInteriorFilter', 'comp_geom/SnapRounding2.cs',
       ['double ex = cx - a.x, ey = cy - a.y, l = dx * ey, r = dy * ex, o = l - r;',
        'double bound = 1e-14 * (Math.Abs(dx) * (Math.Abs(ey) + Math.Abs(cy)) + Math.Abs(dy) * (Math.Abs(ex) + Math.Abs(cx)));',
        'bool Ok(double v) => v == 0 || (Math.Abs(v) > 1e-140 && Math.Abs(v) < 1e140);'],
       'sign of the orientation of a pixel corner (k +- 1/2) w, rounded once, against the segment a-b',
       'every input, side and product 0 or in [1e-140, 1e140] (checked: Ok)', '1e-14 (|dx| (|ey| + |cy|) + |dy| (|ex| + |cx|))',
       emin=-100, emax=100)
def crosses_interior(c, r):
    # (no range fact on the differences: the code checks Ok(l), Ok(r), Ok(bound), so where the filter decides the
    #  products are normal; the model keeps their underflow terms and pays them from the floor of Ok(bound))
    dx, dy = c.diff('dx'), c.diff('dy')
    cxv, CX = c.param('CX')
    cyv, CY = c.param('CY')
    # ex = fl(cx - ax): the corner's rounding u |cx| and the difference's u |ex|
    ex = c.leaf('ex', CX * U + Poly.atom('ex') * U)
    ey = c.leaf('ey', CY * U + Poly.atom('ey') * U)
    o = dx * ey - dy * ex
    b = c.const(1e-14) * (dx.abs() * (ey.abs() + cyv) + dy.abs() * (ex.abs() + cxv))
    r.add('sign', sign_dominates(o, b.lo, b.defect, abs_floor=Fraction(1e-140)))
    r.observe_sign(o, b)


# ------------------------------------------------------------- chains of rounding factors (non-negative quantities)

PI_HI = Fraction(314159265358979323846264338327950289, 10**35)      # above pi
PI_LO = Fraction(314159265358979323846264338327950288, 10**35)      # below pi


@entry('RC-1 ExactPoint3.ApproxVector3d / AddPoint', 'math/ExactPoint3.cs',
       ['double x = (double)X / w, y = (double)Y / w, z = (double)Z / w;',
        'static bool is_safe(double v) { return double.IsInfinity(v) == false && double.IsNaN(v) == false && Math.Abs(v) > 1e-290; }'],
       '|x^ - X / W| for x^ = fl((double)X / (double)W)',
       '(double)BigInteger is within one ulp of the integer (truncated on .NET, as FixedArith.ToDouble states and the '
       'tests of FixedPoint3 check); the quotient normal (checked: is_safe)', 'relative error below 4 * 2^-52')
def approx_vector(c, r):
    # X in X^ [1 - 2u, 1 + 2u] whichever way the conversion rounds within an ulp; W alike; the quotient (1 +- u')
    lo = (1 - 2 * U) / (1 + 2 * U) / (1 + UR)       # the exact x over the computed one, least
    hi = (1 + 2 * U) / (1 - 2 * U) / (1 - UR)
    rel = max(1 - lo, hi - 1)
    r.add('relative error', Verdict(rel <= 8 * U, 8 * U / rel, 'at most %.3f * 2^-52 of the computed value' % float(rel / (2 * U))))


@entry('RC-2 one conversion: grid_coord, LocalPoints, LocalFrame.PointError (2.3e-16)', ARR,
       ['points.Add(new Vector3d(lx, ly, 2.3e-16 * m));'],
       '|x^ - x| for x^ the exact quotient rounded to nearest (ExactPoint3.ToDouble, correctly rounded since #1410)',
       'the value normal (checked: tiny / 1e-290 gates)', '2.3e-16 |x^|')
def one_conversion(c, r):
    # one rounding to nearest (ties to even, #1410): |x^ - x| <= ulp(x^) / 2 <= u |x^| (u = 2^-53); the bound used here, one ulp
    # (the old truncation's), is twice that and still holds; the product 2.3e-16 * m rounds
    have = Fraction(2.3e-16) * (1 - UR)
    r.add('one ulp', Verdict(have >= 2 * U, have / (2 * U), '2.3e-16 (1 - u\') against 2^-52'))


@entry('RC-3 norm (rounded up)', BOOL, ['return Math.Sqrt(x * x + y * y + z * z) * (1 + 1e-15);'],
       'norm(x, y, z) >= the Euclidean norm of the doubles', 'no square underflows (the callers\' tiny gate)', '(1 + 1e-15)')
def norm_up(c, r):
    rho = norm3_lower(1 + 1e-15)
    r.add('rounded up', Verdict(rho >= 1, rho, 'at least %s of the true norm (needs 4 u, has 9 u)' % float(rho)))


def distance_rho():
    """rho with fl(Distance(x, y)) >= rho * |x - y| for the exact difference of the doubles, no square underflowing: the three
    differences round (the exact norm is at most (1 + u) that of the computed differences), the radicand loses at most
    (1 - u)^3 (a square and the two sums), its root (1 - u)^1.5, the root's rounding (1 - u): (1 - u)^3 / (1 + u) is below
    all of it (norm3_lower counts a product with a round-up factor too, which Distance does not have)"""
    return (1 - U) ** 3 / (1 + U)


@entry('RC-4 boundary_bound Lengths and the sum factor', BOOL,
       ['double dist = x.Distance(y);', 'b.Lengths[t] = (dist + E * r) * Factor(10);', 'b.Total += b.Lengths[t];',
        '~internal static double SumTermsFactor(long m)', '~long k = m + 4 + (m & 1);'],
       'Lengths[t] >= the length of the segment between the EXACT grid points X and Y (the doubles x, y are their conversions, each coordinate within 2^-52 relative); a sum over the segments times SumFactor >= the exact sum of the true terms',
       'integer grid values (no underflow of the squares); at most 2^26 segments (SumFactor is +infinity above)',
       '(dist + 2^-52 R) (1 + 10 u); 1 + (ns + 4 + ns mod 2) u')
def boundary_lengths(c, r):
    E = Fraction(1, 2 ** 52)
    # |x - y| <= dist / rho_d
    rho_d = distance_rho()
    # the end points: |X_i - x_i| <= E |X_i| <= E |x_i| / (1 - E), and R = |x|_1 + |y|_1 <= R_c / (1 - u)^3 (the deepest term of
    # the five additions); E R_c is a power of two times R_c: exact
    need = max(1 / rho_d, 1 / ((1 - E) * (1 - U) ** 3))
    # Lengths = fl(fl(dist + E R_c) f) with f = 1 + 10 u exactly (k even): at least (1 - u)^2 f (dist + E R_c)
    have = (1 - U) ** 2 * (1 + 10 * U)
    r.add('one length, the end points included', Verdict(have >= need, have / need,
          'the true length of the exact end points: has %.2f u, needs %.2f u' % (float((have - 1) / U), float((need - 1) / U))))
    # the sums: m terms of a far term, each at least rho_term of the true one (the far_term model: two products and a quotient,
    # Math.PI below pi), summed left to right ((1 - u)^(m - 1) at the deepest term), times SumFactor (f = 1 + k u exact, k = m + 4 +
    # (m mod 2)), whose product rounds once more
    rho_term = (PI_LO / Fraction(math.pi)) * (1 - UR) / (1 + UR) ** 2
    getcontext().prec = 80
    worst = None
    for m in (1, 2, 3, 4, 5, 100, 1000, 10 ** 6, 2 ** 20, 2 ** 26 - 1, 2 ** 26):
        k = m + 4 + (m % 2)
        d_u = Decimal(1) / Decimal(2 ** 53)
        p = (1 + k * d_u) * (1 - d_u) ** m
        p = Fraction(p) * rho_term
        worst = p if worst is None or p < worst else worst
    r.add('sum of m <= 2^26 terms', Verdict(worst >= 1, worst, 'the sum factor beats (m - 1) roundings, the product and the term chain: has %.2f u spare'
                                              % float((worst - 1) / U)))


@entry('RC-5 far_term', BOOL,
       ['double q = 4 * Math.PI * d * d;', 'double r = MathUtil.MinExact(length / q, count / (2 * Math.PI * d));',
        'return r < 2.2250738585072014E-308 && length > 0 && count > 0 ? r + 2 * double.Epsilon : r;',
        'return length > 0 || count > 0 ? r * Factor(6) + 4 * double.Epsilon : r;', 'return sum_bound(g, b);',
        'cost = sum_bound(g, b) * diam;'],
       'the computed term against min(length / (4 pi d^2), count / (2 pi d)) (MathUtil.MinExact since the determinism proof stage 2: it equals Math.Min where both operands are non-negative and finite, as here, so the model is unchanged)',
       'one term; the sums of terms are RC-4',
       'the sums\' SumFactor; 2 * 2^-1074 on a subnormal result; (1 + 6 u) + 4 * 2^-1074 on the overflow path')
def far_term(c, r):
    assert PI_LO < Fraction(math.pi) * (1 + U) and Fraction(math.pi) < PI_LO     # Math.PI is below pi: q is too small, the term too large
    # q <= 4 pi^ d^2 (1 + u')^2, the quotient loses (1 - u'): the computed term >= rho * the true one
    rho1 = (PI_LO / Fraction(math.pi)) * (1 - UR) / (1 + UR) ** 2
    rho2 = (PI_LO / Fraction(math.pi)) * (1 - UR) / (1 + UR) ** 2      # count / (2 pi^ d): one product, one quotient, at least this
    need = 1 / min(rho1, rho2)
    have = (1 + 6 * U) * (1 - UR)                                       # SumFactor of one term (k = 6), its product rounds once
    r.add('one term, rounded up by the sum factor', Verdict(have >= need, have / need, 'loses at most %.2f u, the factor adds 6 u' % float((need - 1) / U)))
    # a subnormal result: absolute errors. The division rounds by at most half the spacing (ETA), the two roundings of q (relative u')
    # move the quotient by 2 u' of the result (below 2^-1022), and Math.PI below pi gives the computed term back (PI_LO / pi^ - 1)
    # of it: at most ETA + 2 u' 2^-1022 - gain, against the 2 * 2^-1074 added
    tiny = Fraction(1, 2 ** 1022)
    shortfall = ETA + 2 * UR * tiny - (PI_LO / Fraction(math.pi) - 1) * tiny
    r.add('subnormal result', Verdict(2 * 2 * ETA >= shortfall, 2 * 2 * ETA / shortfall, 'at most %.2f * 2^-1074 short, 2 * 2^-1074 added' % float(shortfall * 2 ** 1074)))
    # the overflow path: three quotients (1 - u') each, the product (1 + 6 u) and the sum with 4 * 2^-1074 one more each; d > 1e150
    # there (4 pi d^2 overflowed), so an underflow error eta of a quotient only shrinks in the next: 3 eta + the product's < 4 * 2^-1074
    rho = (1 - UR) ** 5 * (1 + 6 * U)
    r.add('overflow path', Verdict(rho >= 1 and 4 * ETA < 4 * Fraction(5e-324), rho, 'relative %s, absolute 4 eta < 4 * 2^-1074' % float(rho)))


@entry('RC-7 piece_diameter', BOOL,
       ['return (MathUtil.MaxExact(pa.Distance(pb), MathUtil.MaxExact(pb.Distance(pc), pc.Distance(pa))) + 2 * err) * Factor(8);'],
       'the result >= the diameter of the true piece: its corners are within e_i per coordinate of the approximations (ApproxError, err = e_A + e_B + e_C)',
       'the edges\' squares do not underflow (a subnormal product is absorbed by the margin 2e-6 of the open winding decision)',
       '(M + 2 err) (1 + 8 u)')
def piece_diameter(c, r):
    sqrt3 = Fraction(17320508075688773, 10 ** 16)                        # above sqrt(3)
    # the result fl(fl(M + 2 err_c) f) is at least (1 - u)^2 f (M + 2 err_c) and the caller's product rounds once more: (1 - u)^3 f
    have = (1 - U) ** 3 * (1 + 8 * U)
    rho_d = distance_rho()
    r.add('the edge', Verdict(have * rho_d >= 1, have * rho_d, 'has %.2f u spare' % float((have * rho_d - 1) / U)))
    # the corners' errors: an edge moves by at most sqrt(3) (e_i + e_j) <= sqrt(3) err, err_c >= (1 - u)^2 err, against 2 err_c
    r.add('the corner errors', Verdict(2 * have * (1 - U) ** 2 >= sqrt3, 2 * have * (1 - U) ** 2 / sqrt3, '2 against sqrt(3): a margin of 15%'))


@entry('RC-8 carry_length', BOOL,
       ['return ((gp.Distance(gq) + 1.7320508075688774 * (ep + eq)) + k * (4 * U)) * Factor(14);'],
       'the result >= the distance of the exact points whose approximations gp, gq are off by at most ep, eq per coordinate',
       'no underflow',
       '(D + sqrt3_c (ep + eq) + 2^-51 K) (1 + 14 u)')
def carry_length(c, r):
    # fl(fl(fl(D_c + Q) + W) f) is at least (1 - u)^3 f of D_c + Q + W, and the caller's product with G rounds once more: (1 - u)^4 f
    have = (1 - U) ** 4 * (1 + 14 * U)
    rho_d = distance_rho()
    r.add('the distance', Verdict(have * rho_d >= 1, have * rho_d, 'has %.2f u spare (W = 2^-51 K is a spare term)' % float((have * rho_d - 1) / U)))
    # the errors: sqrt3_c (ep + eq) >= sqrt(3) (ep + eq) rounds twice (the sum, the product): Q >= (1 - u)^2 of it
    r.add('the corner errors', Verdict(have * (1 - U) ** 2 >= 1, have * (1 - U) ** 2, 'has %.2f u spare' % float((have * (1 - U) ** 2 - 1) / U)))


@entry('RC-6 ExactInterval (one rule)', 'math/ExactIntervals.cs',
       ['return new ExactInterval(Down(lo), Up(hi));', 'return BitConverter.Int64BitsToDouble(x > 0 ? b + 1 : b - 1);',
        'return double.Epsilon;'],
       'the exact result of an operation on any values of the operand intervals lies in the result',
       'none (NaN gives the whole line)', 'one spacing on each side of the round-to-nearest result')
def exact_interval(c, r):
    # round to nearest is within half the spacing on the side of the exact value; Up / Down move a whole spacing
    # (also across 0 and into the subnormals: Up(0) = 2^-1074, Up(-2^-1074) = -0). Checked on doubles:
    ok = True
    for x in (0.0, 5e-324, -5e-324, 1.0, -1.0, 2.2250738585072014e-308, 1.7976931348623157e308, -1.7976931348623157e308):
        ok = ok and math.nextafter(x, math.inf) > x
    r.add(*arith('widening covers one rounding', ok, 'half a spacing against a whole one, on every binade and at 0'))


# ---------------------------------------------------------------------------------------------------------------------
# The node error of the BoundaryTree of the exact winding cache (spatial/ExactWindingCache.cs, node_query and build_node;
# design note docs/design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md, "The decision margin"; #1427). One model per clause of the
# node's error as the code computes it; the clause list is the margin table of the note. NOT re-derived here (the note's
# argument stands): the analytic remainder rem = AAbs r / (d - r)^3 (a Jacobian bound), the cap geometry (cosA, the 1e-9
# angular margin, the range test of fast_path_certificate) and the coordinate term 2.3e-16 (pm + M1) / dr of coordEps.
# ---------------------------------------------------------------------------------------------------------------------
WIND = 'spatial/ExactWindingCache.cs'
PI_HI = Fraction(314159265358979323847, 10**20)                  # above pi
ULP2 = Fraction(1, 2**50)                                         # 2 ulp of a result in [2, 4): the assumption of the note on Math.Atan2
ONE_E14 = Fraction(1, 10**14)


@entry('BT-1 node radius R and distance d', WIND,
       ['double rUp = r * (1.0 + 1e-14);', 'double d = dn * (1.0 - 1e-15);', 'double lv = dv.Length;', 'Vector3d dv = v - c;'],
       'R >= the largest distance of the node\'s points (exact doubles) from the stored centre c; d <= the exact distance |c - p|',
       'no square under- or overflows (the range gates of node_query: d in [2^-97, 2^99], 1e-140 <= dd <= 1e140)',
       'R = max |v - c| (1 + 1e-14); d = |c - p| (1 - 1e-15)')
def node_radius_distance(c, r):
    rho = distance_rho()                                  # fl(Length of three rounded differences) >= rho |exact difference|
    mul = (1 - UR) * Fraction(1.0 + 1e-14)                # the product with the double 1 + 1e-14 rounds once
    have = mul * rho
    r.add('R is at least the true radius', Verdict(have >= 1, (have - 1) / (1 - rho) + 1,
          'the length loses %.2f u, the factor adds %.0f u' % (float((1 - rho) / U), float((Fraction(1.0 + 1e-14) - 1) / U))))
    # d: the computed distance is at most this much above the exact one: three differences ((1 + u) each, in the norm: (1 + u)^2 of the
    # radicand), three squares and two sums on non-negative terms (1 + u)^5, the root halves that and rounds, the product rounds
    radicand = (1 + U) ** 2 * (1 + U) ** 5
    over = (1 + (radicand - 1) / 2) * (1 + U)             # sqrt(1 + x) <= 1 + x / 2
    f = Fraction(1.0 - 1e-15)
    total = over * f * (1 + U)
    r.add('d is at most the true distance', Verdict(total <= 1, (1 - f) / (over * (1 + U) - 1),
          'the roundings add %.2f u, the factor takes %.2f u' % (float((over * (1 + U) - 1) / U), float((1 - f) / U))))


@entry('BT-2 the error of the area vector (VaErr)', WIND,
       ['double vaError = 0.5 * prods * (count + 8) * 4.5e-16;', 'double aAbsVal = 0.5 * abs + vaError;', 'Vector3d cr = dv.Cross(dw);',
        'va += cr;', 'prods += lv * lw;', 'double vaErrTerm = 0.5 * tree.VaErr[k] * absDx * invD3;'],
       'each component of the stored 0.5 va differs from 0.5 sum (v - c) x (w - c) of the exact doubles by at most VaErr = 0.5 prods (count + 8) 4.5e-16',
       'no underflow; count <= 2^26',
       'VaErr = 0.5 prods (count + 8) 4.5e-16')
def va_error(c, r):
    u = U
    rho = distance_rho()
    worst = None
    for count in (1, 2, 3, 4, 5, 8, 100, 10**4, 10**6, 2**20, 2**26):
        # one cross component fl(fl(a b') - fl(a' b)) of the computed differences (relative error u each, two factors per product), the products
        # (u), the difference (u): at most (3 u + 3 u^2 + u (1 + 3 u)) T with T = |a b'| + |a' b| <= |dv| |dw| (Cauchy-Schwarz); the sum of `count`
        # terms adds u |partial| <= u sum |cr| per addition, |cr| <= |dv| |dw|
        need = (3 * u + 3 * u ** 2 + u * (1 + 3 * u) + count * u) * (1 + 10 * u)
        # prods = sum fl(lv lw) >= rho^2 (1 - u)^(count + 1) of the exact sum of |v - c| |w - c| (lengths, products, the sum)
        have = Fraction(4.5e-16) * (count + 8) * rho * rho * (1 - (count + 1) * u)       # (1 - u)^n >= 1 - n u
        m = have / need
        worst = m if worst is None or m < worst else worst
    r.add('VaErr bounds the error of 0.5 va', Verdict(worst >= 1, worst, 'smallest ratio over count = 1 .. 2^26: (count + 8) 4.5e-16 against (4 + count) u'))
    r.add(*arith('AAbs = 0.5 abs + VaErr covers 0.5 sum |cross|', worst >= 2,
                 'abs sums the computed norms of the same terms, each off by at most the error that VaErr already bounds (a ratio of %.1f)' % float(worst)))


@entry('BT-3 the dipole evaluation', WIND,
       ['halfOmega = 0.5 * (vx * dx + vy * dy + vz * dz) * invD3;', 'double invD3 = 1.0 / (dd * dn);',
        'double dipScale = 0.5 * (Math.Abs(vx) + Math.Abs(vy) + Math.Abs(vz)) * absDx * invD3;',
        'nodeErr = rem + vaErrTerm + (1e-12 + 16.0 * coordEps) * dipScale + 1e-11 + 16.0 * coordEps'],
       'the computed halfOmega differs from 0.5 (A . dx) / |dx|^3 of the same doubles by at most (1e-12 + 16 coordEps) dipScale (the coordinate part, coordEps: not modelled)',
       'no underflow',
       '(1e-12 + 16 coordEps) dipScale')
def dipole_evaluation(c, r):
    u = U
    # relative to 0.5 sum |v_i dx_i| / |dx|^3 (dipScale uses |dx|_1 >= every |dx_i|: larger, and itself computed within 12 u)
    dd = (1 + u) ** 3 - 1                       # three squares and two sums on non-negative terms
    dn = dd / 2 + u                             # the root halves the radicand's error and rounds
    prod = dd + dn + u + dd * dn                # dd * dn
    inv = prod + u                              # 1 / (...)
    dot = 3 * u + 3 * u ** 2                    # three products and two sums (relative to sum |v_i dx_i|)
    total = (dot + inv + u) * (1 + 20 * u)      # times invD3, times 0.5 (exact)
    have = Fraction(1e-12) * (1 - 12 * u)
    r.add('relative error of halfOmega', Verdict(have >= total, have / total,
          'about %.1f u of dipScale, against 1e-12 = %.0f u' % (float(total / u), float(Fraction(1e-12) / u))))


@entry('BT-4 the sum order and the decision margin', WIND,
       ['double sumOrderErr = 7.5e-16 * (double)boundary.Length * tree.TotalWeight;',
        'return 1.01 * (bErr + 1e-9 + 4e-15 * (Math.Abs((double)chi) + Math.Abs(bSum)));',
        'bErr = 1.01 * (err + sumOrderErr) / MathUtil.TwoPI;'],
       'the tree sum and the full sum differ by at most the summation order: 2 N u pi sum|w| (N edges, N u < 0.0099); the quotient by 2 pi and chi + bSum round at most u relative each',
       'N u < 0.0099 (the factor 1.01 covers the accumulation of err)',
       '7.5e-16 N W; 1.01 (bErr + 1e-9 + 4e-15 (|chi| + |bSum|))')
def order_and_margin(c, r):
    # each addition of the sum adds at most u |partial| <= u N pi W to a sum of N terms |w_e| pi; two orders: 2 N u pi W
    # (n additions of a sum with n u <= 0.0099 have the exact bound n u / (1 - n u) <= 1.0101 n u: the factor is part of the need; the 1.01 on bErr
    # in the code is a second cushion, not counted here, so the margin is the one of the constant alone)
    need = 2 * U * PI_HI / (1 - Fraction(99, 10000))
    r.add('7.5e-16 N W against 2 N u pi W', Verdict(Fraction(7.5e-16) >= need, Fraction(7.5e-16) / need, 'the constant has %.3f of the 2 u pi / (1 - N u) = %.4e it needs (N u <= 0.0099)' % (float(Fraction(7.5e-16) / need), float(need))))
    # the division by 2 pi (the double TwoPI is itself within u of 2 pi, the same in both sums) and chi + bSum: u relative each, in both sums (4 u of |chi| + |bSum|)
    r.add('4e-15 (|chi| + |bSum|)', Verdict(Fraction(4e-15) >= 4 * U, Fraction(4e-15) / (4 * U), '%.1f times the four roundings (two per sum)' % float(Fraction(4e-15) / (4 * U))))
    # the factor 1.01 on err and sumOrderErr: err is a sum of n positive terms, rounded n times: (1 + u)^n <= 1.01 while n u <= 0.0099 (n < 8.9e13)
    n = Fraction(99, 10000) / U
    nu = n * U                                                  # (1 + u)^n <= exp(n u) <= 1 + n u + (n u)^2 for n u < 1
    r.add('1.01 covers the accumulation of err', Verdict(1 + nu + nu * nu <= Fraction(101, 100), Fraction(101, 100) / (1 + nu + nu * nu),
          'for up to %.2e terms (2^26 edges at most here)' % float(n)))


@entry('BT-5 the edge angle under the certificate (A1)', WIND,
       ['return yz - r >= 1e-9 * d;', 'internal static bool ball_is_small(double r, double d) { return r < 0.25 * d; }',
        '+ 1e-12 * (double)(tree.Hi[k] - tree.Lo[k]);', 'nodeErr = rem + vaErrTerm + (1e-12 + 16.0 * coordEps) * dipScale + 1e-11 + 16.0 * coordEps'],
       'for every edge (a, b) of an accepted node and for the chord: |computed angle - exact angle| <= 1e-14 (A1); the code charges 1e-12 per replaced edge and 1e-11 for the chord',
       'the node is accepted: r < d / 4 and the yz part of c - p exceeds r by 1e-9 d (so every edge is on the fast path, away from the exact zone and from the +x axis)',
       '1e-12 (hi - lo) and 1e-11 in nodeErr')
def edge_angle_certified(c, r):
    # The constants of the proof (steps 2, 4, 5) with the geometry of an accepted node: the unit vectors U, V of two points are at most
    # 2 asin(1/4) apart, so 1 + U.V >= 1 + cos(2 asin(1/4)) = 1 + (1 - 2/16); hypot = |a||b| sqrt(2 (1 + U.V) x_u x_v);
    # S1 <= 2 |a||b| sqrt(x_u x_v), S2 <= 4 |a||b| sqrt(x_u x_v) (x_u = 1 - U_x, ...)
    one_plus = 1 + (1 - Fraction(2, 16))
    h = Fraction(math.sqrt(float(2 * one_plus))) * (1 - Fraction(1, 10**15))          # <= sqrt(2 (1 + U.V)) (rounded down)
    angle = PI_HI / 2 * (2 * U * 2 + 22 * U * 4) / h                                  # steps 4, 5: |dnum| <= 2 u S1, |dden| <= 22 u S2, asin x <= (pi / 2) x
    rounded_vectors = 2 * PI_HI * U                                                   # step 3
    total = angle + rounded_vectors + ULP2
    # the proof's 22 u S2 is loose: step 4 itself gives |dden| <= 16 u da db + 3 u (|u_y v_y| + |u_z v_z|) <= 16 u S2 (da db <= S2, the rest <= S2); the same
    # three terms with 16 (a margin of 1.3 where the note's 22 gives 1.013); the claim of the note keeps the 22, a bound that is valid and simpler
    sharp = PI_HI / 2 * (2 * U * 2 + 16 * U * 4) / h + rounded_vectors + ULP2
    r.add('A1: the sharper count (16 u S2)', Verdict(sharp <= ONE_E14 and sharp < total, ONE_E14 / sharp, 'step 4 with 16 u S2: %.3e against 1e-14 (the proof of the note keeps 22 u S2: %.3e)' % (float(sharp), float(total))))
    r.add('A1: the three terms', Verdict(total <= ONE_E14, ONE_E14 / total,
          'step 5 %.3e + step 3 %.3e + atan2 %.3e = %.3e against 1e-14 (the note: 9.9e-15)' % (float(angle), float(rounded_vectors), float(ULP2), float(total))))
    r.add('the 1e-12 charged per replaced edge', Verdict(Fraction(1, 10**12) >= total, Fraction(1, 10**12) / total, 'a margin of %.0f against the bound that the certificate implies' % float(Fraction(1, 10**12) / total)))
    r.add('the 1e-11 charged for the chord', Verdict(Fraction(1, 10**11) >= total, Fraction(1, 10**11) / total, 'the chord is one edge: a margin of %.0f' % float(Fraction(1, 10**11) / total)))
    # the certificate keeps phi (the angle between the yz parts of two points) away from pi: r / |yz_c| <= 1 - 1e-9 gives phi <= 2 asin(1 - 1e-9)
    gap = math.pi - 2 * math.asin(1 - 1e-9)
    r.add('the certificate keeps phi below pi - 8.9e-5', Verdict(gap >= 8.9e-5, gap / 8.9e-5, 'pi - phi_max = %.3e' % gap))
    # with it the exact zone cannot fire: |phi| >= 1e-6 gives |num| >= 1e-6 S1 > numErr = 1e-12 S1, and |phi| < 1e-6 gives den >= 0.999 S2 > denErr = 1e-12 S2
    r.add(*arith('the exact zone never fires', 1e-6 > 1e-12 and 0.999 > 1e-12, 'num >= 1e-6 S1 against 1e-12 S1; den >= 0.999 S2 against 1e-12 S2'))
    _sample_edges(c, r, certified=True)


def _exact_edge(p, a, b):
    """the exact num, den and the term sizes S1, S2 of the edge (a, b) seen from p, from the exact differences (Decimal, 80 digits)"""
    getcontext().prec = 80
    A = [Fraction(a[i]) - Fraction(p[i]) for i in range(3)]
    B = [Fraction(b[i]) - Fraction(p[i]) for i in range(3)]

    def dec(x):
        return Decimal(x.numerator) / Decimal(x.denominator)
    yzA = A[1] ** 2 + A[2] ** 2
    yzB = B[1] ** 2 + B[2] ** 2
    la = dec(A[0] ** 2 + yzA).sqrt()
    lb = dec(B[0] ** 2 + yzB).sqrt()
    da = dec(yzA) / (la + dec(A[0])) if A[0] > 0 else la - dec(A[0])
    db = dec(yzB) / (lb + dec(B[0])) if B[0] > 0 else lb - dec(B[0])
    num = dec(A[2] * B[1] - A[1] * B[2])
    den = da * db + dec(A[1] * B[1] + A[2] * B[2])
    s1 = abs(dec(A[1] * B[2])) + abs(dec(A[2] * B[1]))
    s2 = da * db + abs(dec(A[1] * B[1])) + abs(dec(A[2] * B[2]))
    return num, den, s1, s2


def _fast_edge(p, a, b):
    """the fast path of boundary_edge_angle_x on doubles, in the order of the source (no FMA): the rounded angle and whether finish_edge_angle
    sends the edge to the accurate evaluation instead (the cancellation clause, or the branch cut clause with the subnormal guard)"""
    ax, ay, az = a[0] - p[0], a[1] - p[1], a[2] - p[2]
    bx, by, bz = b[0] - p[0], b[1] - p[1], b[2] - p[2]
    yzA2 = ay * ay + az * az
    yzB2 = by * by + bz * bz
    la = math.sqrt(ax * ax + yzA2)
    lb = math.sqrt(bx * bx + yzB2)
    da = yzA2 / (la + ax) if ax > 0.0 else la - ax
    db = yzB2 / (lb + bx) if bx > 0.0 else lb - bx
    p1, p2 = ay * bz, az * by
    num = p2 - p1
    den = da * db + (ay * by + az * bz)
    s1 = abs(p1) + abs(p2)
    s2 = da * db + abs(ay * by) + abs(az * bz)
    an = abs(num)
    accurate = an + abs(den) < FAST_ANGLE_RATIO * (s1 + s2) or (den <= 1e-12 * s2 and (an <= 1e-12 * s1 or s1 < SUBNORMAL_S1))
    return math.atan2(num, den), accurate


def _angle_error_bound(num, den, s1, s2):
    """the error bound of the computed angle without any certificate (the proof of A1, steps 3 to 5): asin(|d| / hypot) <= (pi / 2) |d| / hypot
    for |d| <= 2 u S1 + 22 u S2 (steps 4, 5) -- unbounded as hypot -> 0 (p approaches the edge), then at most pi -- plus 2 pi u (step 3) and 2 ulp of atan2"""
    hyp = (num * num + den * den).sqrt()
    u = Decimal(1) / Decimal(2 ** 53)
    d = 2 * u * s1 + 22 * u * s2
    pi = Decimal('3.14159265358979323846264338327950288')
    first = pi if hyp == 0 or d >= hyp else min(pi, pi / 2 * d / hyp)
    return first + 2 * pi * u + Decimal(1) / Decimal(2 ** 50)


def _accepted_edge(rng, s):
    """two points of a node that the certificate accepts (r < d / 4, the yz part of the centre exceeds r by 1e-9 d .. 1e-3 d), p, the centre and r;
    half of the pairs are the two points of the ball's yz disk that the tangents from p touch (phi near pi - 8.9e-5), the extreme of the certificate"""
    p = tuple(rng.uniform(-1, 1) * s for _ in range(3))
    d = s * rng.uniform(0.1, 10)
    rr = d * rng.uniform(0.01, 0.2499)
    yz = rr + d * 10 ** rng.uniform(-9, -3)
    if yz >= d:
        return None
    ang = rng.uniform(0, 2 * math.pi)
    e = (math.cos(ang), math.sin(ang))
    f = (-e[1], e[0])
    ctr = (p[0] + math.sqrt(d * d - yz * yz) * (1 if rng.random() < 0.5 else -1), p[1] + yz * e[0], p[2] + yz * e[1])
    if rng.random() < 0.5:
        # tangent points of the disk seen from p: at distance L = sqrt(yz^2 - rr^2) from p, the angle asin(rr / yz) either side of e (shrunk inside the ball)
        g = math.asin(rr / yz)
        L = math.sqrt(yz * yz - rr * rr)
        pts = []
        for sg in (1, -1):
            dirn = (math.cos(g) * e[0] + sg * math.sin(g) * f[0], math.cos(g) * e[1] + sg * math.sin(g) * f[1])
            q = (L * dirn[0], L * dirn[1])
            yzc = (yz * e[0], yz * e[1])
            q = (yzc[0] + (q[0] - yzc[0]) * 0.9999, yzc[1] + (q[1] - yzc[1]) * 0.9999)
            pts.append((ctr[0] + rng.uniform(-1, 1) * rr * 0.01, p[1] + q[0], p[2] + q[1]))
        a, b = pts
    else:
        pts = []
        for _ in range(2):
            w = [rng.uniform(-1, 1) for _ in range(3)]
            n = math.sqrt(sum(x * x for x in w)) or 1.0
            ln = rr * 0.999 * rng.random() ** 0.3
            pts.append(tuple(ctr[i] + w[i] / n * ln for i in range(3)))
        a, b = pts
    ok = all(math.dist(x, ctr) <= rr for x in (a, b)) and math.hypot(ctr[1] - p[1], ctr[2] - p[2]) - rr >= 1e-9 * d and rr < 0.25 * d
    return (p, a, b) if ok else None


def _sample_edges(c, r, certified):
    """concrete run: edges of accepted nodes (certified) or edges with p close to them (not), against the exact angle: the error must be below
    the bound (1e-14, or the formula of _angle_error_bound); records error / bound"""
    if c.concrete is None:
        return
    rng = c.concrete.rng
    slack = Decimal(4) / Decimal(10 ** 16)          # the exact angle is atan2 of two correctly rounded doubles: at most 4e-16 off
    for _ in range(10):
        s = 2.0 ** rng.randint(-20, 20)
        if certified:
            case = _accepted_edge(rng, s)
            if case is None:
                continue
            p, a, b = case
        else:
            a = tuple(rng.uniform(-1, 1) * s for _ in range(3))
            b = tuple(rng.uniform(-1, 1) * s for _ in range(3))
            t = rng.random()
            delta = s * 10 ** (-rng.uniform(1, 13))
            p = tuple(a[i] + t * (b[i] - a[i]) + rng.uniform(-1, 1) * delta for i in range(3))
            if rng.random() < 0.5:                          # (p in the plane x = const of both end points: the cancellation of den is largest)
                b = (a[0], b[1], b[2])
                p = (a[0], p[1], p[2])
        if (a[1] == p[1] and a[2] == p[2]) or (b[1] == p[1] and b[2] == p[2]):
            continue
        num, den, s1, s2 = _exact_edge(p, a, b)
        got, accurate = _fast_edge(p, a, b)
        if certified and accurate:
            c.violations.append(('CERTIFIED EDGE LEAVES THE ROUNDED PATH', got, 0.0, 0.0))
        if not certified and accurate:
            continue                                        # (the accurate evaluation: integer arithmetic, not modelled here)
        exact = math.atan2(float(num), float(den))
        err = abs(got - exact)
        if not certified:
            # an uncertified edge (p near it) may lie across the branch cut of the arctangent: the angles are compared as such, modulo 2 pi.
            # A certified edge never does (part of the proof of A1: |num| >= 1e-6 S1 >> 2 u S1, so the sign of num is the exact one), so a 2 pi
            # difference there is a real error of 2 pi and stays one: no reduction
            err = min(err, abs(err - 2 * math.pi))
        bound = _angle_error_bound(num, den, s1, s2) + slack
        limit = min(bound, ONE_E14_D + slack) if certified else min(bound, ROUNDED_ANGLE_BOUND)
        ratio = Decimal(err) / limit
        r.observed.append(float(ratio))
        if ratio > 1:
            c.violations.append(('ERROR ABOVE BOUND', got, err, float(limit)))


ONE_E14_D = Decimal(1) / Decimal(10 ** 14)
FAST_ANGLE_RATIO = 0.03125                  # 2^-5, `FastAngleRatio` of the source
SUBNORMAL_S1 = 9.3326361850321888e-302      # 2^-1000, `SubnormalS1` of the source
ROUNDED_ANGLE_BOUND = Decimal(2) / Decimal(10 ** 13)    # the 2e-13 the rounded evaluation is kept within


@entry('BT-6 the edge angle error against the distance from the edge', WIND,
       ['double den = da * db + dotYZ;', 'double dotYZ = ay * by + az * bz;', 'double s1 = Math.Abs(p1) + Math.Abs(p2);',
        'if (an + Math.Abs(den) < FastAngleRatio * (s1 + s2) || (den <= 1e-12 * s2 && (an <= 1e-12 * s1 || s1 < SubnormalS1))) {',
        'const double FastAngleRatio = 0.03125;', 'const double SubnormalS1 = 9.3326361850321888e-302;', 'return Math.Atan2(num, den);'],
       'the rounded angle of finish_edge_angle differs from the exact one by at most (pi / 2)(4.01 u S1 + 22.1 u S2) / hypot(num, den) + 2 pi u + 2 ulp, '
       'and the rounded path is kept only where |num| + |den| >= 2^-5 (S1 + S2), so hypot >= 0.0220 (S1 + S2)',
       'the rounded path (normal products in S2; S2 >= 2^-602 on the fast path, S1 may be subnormal). Bounded by 1.8e-13 wherever it is kept: the evaluation near an open edge '
       '(1 + U.V -> 0, where hypot is small against S1 + S2) goes to the accurate path (exact integers, within 2e-15: not modelled, the note derives it). The sign of the rounded num is the exact one where '
       'it decides the branch cut (den <= 1e-12 S2): |num| > 1e-12 S1 with S1 >= 2^-1000, the absolute rounding of the products (2^-1074) being below 2^-74 S1; S1 < 2^-1000 goes to the accurate path. '
       'NOT the A1 of the tree (BT-5: under the certificate the edge keeps the rounded path, margin 10 on the tested quantity)',
       '(pi / 2)(4.01 u S1 + 22.1 u S2) / hypot')
def edge_angle_domain(c, r):
    # the decision of finish_edge_angle: rounded where |num| + |den| >= 2^-5 (S1 + S2); hypot >= (|num| + |den|) / sqrt 2
    hyp_lo = FAST_ANGLE_RATIO / math.sqrt(2)
    r.add(*arith('the rounded path has hypot >= %.5f (S1 + S2)' % hyp_lo, hyp_lo >= 0.0220 and FAST_ANGLE_RATIO == 2.0 ** -5,
                 'hypot(num, den) >= (|num| + |den|) / sqrt 2 >= 2^-5 / sqrt 2 (S1 + S2) = 0.022097 (S1 + S2) >= 0.0220 (S1 + S2)'))
    # |Delta| <= 4.01 u S1 + 22.1 u S2 <= 22.1 u (S1 + S2) (the note's roundings: num 3u + u, da db and dotYZ 22.1u); asin(x) <= (pi / 2) x on [0, 1]
    ratio = Fraction(221, 10) * U / Fraction(22, 1000)
    k = float(PI_HI / 2 * ratio)
    tot = k + float(2 * PI_HI * U) + 2.0 ** -50
    r.add(*arith('the error of a kept rounded angle is at most %.3e (with 2 pi u + 2 ulp: %.3e), below 2e-13' % (k, tot), tot < 2e-13,
                 '(pi / 2) 22.1 u (S1 + S2) / (0.0220 (S1 + S2)) = (pi / 2) 1005 u, no dependence on the distance from the edge: the cancellation is cut off by the clause'))
    # the sign of num where it decides the branch cut: |Delta num| <= 4.01 u S1 + 2^-1074 <= (4.01 u + 2^-74) S1 for S1 >= 2^-1000, below 1e-12 S1 < |num|
    sign_rel = 4.01 * float(U) + 2.0 ** -74
    r.add(*arith('the sign of num is exact where |num| > 1e-12 S1 and S1 >= 2^-1000 (error %.3e S1)' % sign_rel,
                 sign_rel < 1e-12 and 2.0 ** -1074 <= 2.0 ** -74 * SUBNORMAL_S1 and SUBNORMAL_S1 == 2.0 ** -1000,
                 'the products round to the subnormal grid below 2^-1022 (absolute error 2^-1075 each, 2^-1074 for num); from S1 = 2^-1000 on that is 2^-74 S1; '
                 'below S1 ~ 2^-1034 it exceeds 1e-12 S1, so the guard 2^-1000 sends S1 < 2^-1000 to the accurate path (the reviewer input of #1514: +pi against -pi)'))
    # S2 of the fast path is far above the subnormal grid: |yz| >= 2^-100 |a|, 2^-100 <= |a|: da >= yz^2 / (2 |a|) >= 2^-201 |a| >= 2^-301
    r.add(*arith('the subnormal products of num do not touch the angle bound (S2 >= 2^-602 on the fast path)',
                 2.0 ** -1074 / 2.0 ** -602 < float(U) * 2.0 ** -100,
                 'absolute error 2^-1074 of num against 22.1 u S2: 2^-472 relative to S2, below u 2^-100'))
    theta_cert = math.pi - 2 * math.asin(0.25)
    r.add(*arith('an accepted node keeps theta >= %.2f rad: |num| + |den| >= 0.32 (S1 + S2), above 2^-5 by the factor 10' % theta_cert, 0.32 / FAST_ANGLE_RATIO > 10,
                 'r < d / 4 puts the two unit vectors at most 2 asin(1/4) = 0.505 rad apart (`ball_is_small`); the design note: hypot >= 1.93 la lb sqrt(x_u x_v), S1 + S2 <= 6 la lb sqrt(x_u x_v), '
                 'so every edge of an accepted node keeps the rounded path, bit for bit (the concrete run counts a certified edge on the accurate path as a violation)'))
    _sample_edges(c, r, certified=False)
