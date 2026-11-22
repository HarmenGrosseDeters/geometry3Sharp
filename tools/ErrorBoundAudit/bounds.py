"""The machine-derived part of the table of docs/research/ERROR_BOUND_AUDIT_2026-11.md: one model per bound.

A model writes the guarded expression and the code's bound with the operators of fpbound.py, in the order of the
source, and asks for the proof obligation (sign_dominates for a sign filter, dominates for an enclosure). Its `anchors`
are lines of the source (whitespace ignored) that must still be there, inside the function named by `scope` where one
is given: a changed expression, constant or guard fails test_error_bound_audit.py until the model is brought up to
date. The anchors are the lines the model transcribes and the guards it relies on; a change elsewhere in the function
(a new operation between them) is not seen, so a model is still re-read when its function changes.

Run: python3 tools/ErrorBoundAudit/audit.py
"""
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
        'return v6 < -(32.0 + component.Count) * 1.1102230246251565e-16 * norms;'],
       'v6 < 0 is certain where v6 < -(32 + n) u S, S the sum of |x| |y| |z|',
       'NO UNDERFLOW: every product of three differences normal. Not checked by the code (finding F1)',
       '(32 + n) u * sum of |x||y||z|', emin=-100, emax=100)
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
    r.finding('subnormal products', 'F1', 'absolute error %.3g per term with no term or guard in the bound: wrong below about 1e-103 '
              '(reproducer: Adversarial, F1)' % float(d.err.const_part()))


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
       '|x^ - x| for x^ the exact quotient truncated to a double (ExactPoint3.ToDouble: 64 bits, then 53)',
       'the value normal (checked: tiny / 1e-290 gates)', '2.3e-16 |x^|')
def one_conversion(c, r):
    # truncating twice toward zero is truncating once: |x^ - x| < ulp(x^) <= 2 u |x^|; the product 2.3e-16 * m rounds
    have = Fraction(2.3e-16) * (1 - UR)
    r.add('one ulp', Verdict(have >= 2 * U, have / (2 * U), '2.3e-16 (1 - u\') against 2^-52'))


@entry('RC-3 norm (rounded up)', BOOL, ['return Math.Sqrt(x * x + y * y + z * z) * (1 + 1e-15);'],
       'norm(x, y, z) >= the Euclidean norm of the doubles', 'no square underflows (the callers\' tiny gate)', '(1 + 1e-15)')
def norm_up(c, r):
    rho = norm3_lower(1 + 1e-15)
    r.add('rounded up', Verdict(rho >= 1, rho, 'at least %s of the true norm (needs 4 u, has 9 u)' % float(rho)))


@entry('RC-4 boundary_bound Lengths', BOOL, ['b.Lengths[t] = x.Distance(y) * (1 + 1e-15);', 'b.Total += b.Lengths[t];'],
       'Lengths[t] >= the length of the segment between the doubles x and y',
       'the doubles are the segment: NOT so, they are the exact ends rounded once (finding F4); no underflow of the squares',
       '(1 + 1e-15)')
def boundary_lengths(c, r):
    # Distance: differences (1 + u each, so the exact norm is at most (1 + u) that of the computed differences)
    rho = norm3_lower(1 + 1e-15) / (1 + U)
    r.add('one length', Verdict(rho >= 1, rho, 'at least %s of the length between the doubles' % float(rho)))
    r.finding('Total and g (sums of n terms)', 'F4', 'a sum of n terms loses up to (n - 1) u, the ends\' own rounding up to '
              '2 * 2^-52 of the coordinate size per segment: neither is in a fixed factor; absorbed by the rule\'s spare 1e-6')


@entry('RC-5 far_term', BOOL,
       ['double q = 4 * Math.PI * d * d;', 'return Math.Min(length / q, count / (2 * Math.PI * d));',
        'return length > 0 || count > 0 ? r * (1 + 1e-15) + 4 * double.Epsilon : r;', 'return g * (1 + 1e-12);',
        'cost = g * (1 + 1e-12) * diam;'],
       'the computed term against min(length / (4 pi d^2), count / (2 pi d))',
       'one term; q and length / q normal (the callers have d >= 1e-3 s and the grid\'s tiny gate); the sums of terms are finding F4',
       'the callers\' (1 + 1e-12); (1 + 1e-15) + 4 * 2^-1074 on the overflow path')
def far_term(c, r):
    assert PI_LO < Fraction(math.pi) * (1 + U) and Fraction(math.pi) < PI_LO     # Math.PI is below pi: q is too small, the term too large
    # q <= 4 pi^ d^2 (1 + u')^2, the quotient loses (1 - u'): the computed term >= rho * the true one
    rho1 = (PI_LO / Fraction(math.pi)) * (1 - UR) / (1 + UR) ** 2
    rho2 = (PI_LO / Fraction(math.pi)) * (1 - UR) / (1 + UR) ** 2      # count / (2 pi^ d): one product, one quotient, at least this
    need = 1 / min(rho1, rho2)
    have = Fraction(1 + 1e-12) * (1 - UR)
    r.add('one term, rounded up by the caller', Verdict(have >= need, have / need, 'loses at most %.2f u, the caller adds 1e-12' % float((need - 1) / U)))
    # the overflow path: three quotients (1 - u') each, the product (1 + 1e-15) one more; d > 1e150 there (4 pi d^2
    # overflowed), so an underflow error eta of a quotient only shrinks in the next: 3 eta + the product's < 4 * 2^-1074
    # (and the final sum r (1 + 1e-15) + 4 * 2^-1074: one more rounding)
    rho = (1 - UR) ** 5 * Fraction(1 + 1e-15)
    r.add('overflow path', Verdict(rho >= 1 and 4 * ETA < 4 * Fraction(5e-324), rho, 'relative %s, absolute 4 eta < 4 * 2^-1074' % float(rho)))


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
