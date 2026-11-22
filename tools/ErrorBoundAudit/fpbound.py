"""A small forward error analysis of binary64 expressions in exact rational arithmetic.

The checker of docs/research/ERROR_BOUND_AUDIT_2026-11.md. It re-derives the hand-written error bounds of the exact
code from the expressions alone, with the standard model of round-to-nearest arithmetic (Higham, Accuracy and
Stability of Numerical Algorithms, 2nd ed., sections 2.2 and 3.1; the underflow terms as in Demmel 1984 and Higham
section 2.8; the bounds of whole predicates as Shewchuk 1997 derives them):

    fl(x + y) = (x + y)(1 + d)                 |d| <= u = 2^-53   (exact where the sum is subnormal)
    fl(x * y) = (x * y)(1 + d) + n             |d| <= u, |n| <= eta = 2^-1075, n = 0 where the product is normal
    fl(x / y) likewise, fl(sqrt(x)) = sqrt(x)(1 + d)

and |fl(z) - z| <= u |fl(z)| as well (the error is at most half an ulp of the result). No FMA, no extended precision:
the exact code bans both (BannedSymbols.txt, analyzer G3E0003).

A computed double x^ that stands for an exact real x is a Value with three facts, each a polynomial with non-negative
rational coefficients in named atoms (non-negative reals: the magnitudes of the inputs, norms, ...):

    |x^| <= hi          |x^ - x| <= err          |x^| >= lo - defect     (lo only without cancellation)

Every operation maps these facts of its operands to those of its result (the rules are next to the operators below).
A filter "the sign of x^ is the sign of x where |x^| > B^" is sound where err <= lower bound of B^ for every value of
the atoms; `dominates` proves that monomial by monomial (coefficient <= coefficient), which is sufficient and needs no
search over the inputs. The margin of a bound is the smallest ratio of the two coefficients: the claimed constant
could be that much smaller. A margin below 1 is a bound that this analysis cannot prove (not yet a counterexample:
the analysis is an upper bound of the error; an input is needed, see adversarial search in audit.py).

Soundness of the analysis itself is tested, not only argued: every model also runs on concrete doubles (Python floats
are binary64 with round to nearest) next to exact fractions, and each node's three facts are checked at each sample
(Context(concrete=...), test_error_bound_audit.py).
"""
from fractions import Fraction
import math

U = Fraction(1, 2**53)            # unit roundoff, round to nearest
ETA = Fraction(1, 2**1075)        # half the spacing of the subnormals: the absolute error of an underflowing product
MIN_NORMAL = Fraction(1, 2**1022)
# the sharp relative error of one rounding: |fl(z) - z| <= u / (1 + u) |z| (Knuth; Jeannerod and Rump 2018, "On relative
# errors of floating-point operations", Math. Comp. 87, Theorem 2.1), so z / (1 + u) <= |fl(z)| <= z (1 + UR)
UR = U / (1 + U)


def frac(x):
    """the exact value of a double (or int, or Fraction)"""
    return x if isinstance(x, Fraction) else Fraction(x)


class Poly:
    """a polynomial with rational coefficients in named atoms; a monomial is a sorted tuple of (atom, power)"""

    __slots__ = ('t',)

    def __init__(self, terms=None):
        self.t = {m: c for m, c in (terms or {}).items() if c != 0}

    @staticmethod
    def const(c):
        return Poly({(): frac(c)})

    @staticmethod
    def atom(name):
        return Poly({((name, 1),): Fraction(1)})

    def __add__(self, o):
        o = as_poly(o)
        t = dict(self.t)
        for m, c in o.t.items():
            t[m] = t.get(m, 0) + c
        return Poly(t)

    __radd__ = __add__

    def __mul__(self, o):
        o = as_poly(o)
        t = {}
        for m1, c1 in self.t.items():
            for m2, c2 in o.t.items():
                d = dict(m1)
                for a, p in m2:
                    d[a] = d.get(a, 0) + p
                m = tuple(sorted(d.items()))
                t[m] = t.get(m, 0) + c1 * c2
        return Poly(t)

    __rmul__ = __mul__

    def __sub__(self, o):
        return self + as_poly(o) * Fraction(-1)

    def subst(self, atom, poly):
        """the polynomial with `atom` replaced by poly (an upper bound of it: the coefficients are non-negative)"""
        r = ZERO
        for m, c in self.t.items():
            term = Poly({tuple(x for x in m if x[0] != atom): c})
            for a, p in m:
                if a == atom:
                    for _ in range(p):
                        term = term * poly
            r = r + term
        return r

    def is_zero(self):
        return not self.t

    def nonneg(self):
        return all(c >= 0 for c in self.t.values())

    def eval(self, atoms):
        s = Fraction(0)
        for m, c in self.t.items():
            for a, p in m:
                c *= frac(atoms[a]) ** p
            s += c
        return s

    def const_part(self):
        return self.t.get((), Fraction(0))

    def without_const(self):
        return Poly({m: c for m, c in self.t.items() if m != ()})

    def __repr__(self):
        def mono(m):
            return '*'.join(a if p == 1 else '%s^%d' % (a, p) for a, p in m) or '1'
        return ' + '.join('%.6g %s' % (float(c), mono(m)) for m, c in sorted(self.t.items())) or '0'


def as_poly(x):
    return x if isinstance(x, Poly) else Poly.const(x)


ZERO = Poly()


class Value:
    """a computed double with its facts (module docstring); fv / xv: the double and the exact value in a concrete run"""

    def __init__(self, ctx, hi, err, lo=None, defect=None, minmag=None, fv=None, xv=None, sign_known=False, pre=None):
        self.ctx, self.hi, self.err = ctx, as_poly(hi), as_poly(err)
        # |z - x| <= pre for the unrounded result z of the last operation (x^ = fl(z)); a sign filter needs only this
        # (see sign_dominates). A leaf has no last operation: pre = err
        self.pre = self.err if pre is None else as_poly(pre)
        self.lo = None if lo is None else as_poly(lo)
        self.defect = ZERO if defect is None else as_poly(defect)
        # a nonzero value has magnitude >= minmag (None: nothing known): decides whether a product can underflow
        self.minmag = minmag
        self.fv, self.xv = fv, xv
        self.nonneg = sign_known            # the value and the exact one are both >= 0: sums do not cancel
        ctx.check(self)

    # ---- rules ----
    def _addsub(self, o, sign):
        o = self.ctx.lift(o)
        fv = xv = None
        if self.ctx.concrete is not None:
            fv = self.fv + sign * o.fv
            xv = self.xv + sign * o.xv
        # |fl(s) - s| <= u |s| <= u (hi_x + hi_y), s = x^ +- y^; no underflow term (a subnormal sum is exact)
        err = self.err + o.err + (self.hi + o.hi) * UR
        hi = (self.hi + o.hi) * (1 + UR)
        lo = defect = None
        nonneg = sign > 0 and self.nonneg and o.nonneg
        if nonneg and self.lo is not None and o.lo is not None:
            # no cancellation: fl(s) >= s (1 - u)
            lo = (self.lo + o.lo) * (1 - UR)
            defect = self.defect + o.defect
        # (a nonzero sum of non-negative terms is at least its smallest nonzero term)
        minmag = min(self.minmag, o.minmag) if nonneg and self.minmag is not None and o.minmag is not None else None
        return Value(self.ctx, hi, err, lo, defect, minmag, fv, xv, nonneg, pre=self.err + o.err)

    def __add__(self, o):
        return self._addsub(o, 1)

    __radd__ = __add__

    def __sub__(self, o):
        return self._addsub(o, -1)

    def __rsub__(self, o):
        return self.ctx.lift(o)._addsub(self, -1)

    def __neg__(self):
        return Value(self.ctx, self.hi, self.err, self.lo, self.defect, self.minmag,
                     None if self.fv is None else -self.fv, None if self.xv is None else -self.xv)

    def __mul__(self, o):
        o = self.ctx.lift(o)
        fv = xv = None
        if self.ctx.concrete is not None:
            fv = self.fv * o.fv
            xv = self.xv * o.xv
        # no underflow where both factors are 0 or of magnitude >= minmag and the product of the two bounds is normal
        # (a product of nonzero doubles of magnitudes >= a, b is >= a b, and rounding is monotone)
        under = not (self.minmag is not None and o.minmag is not None and self.minmag * o.minmag >= MIN_NORMAL)
        under = under and not self.ctx.assume_normal
        eta = Poly.const(ETA) if under else ZERO
        # |fl(p) - x y| <= |x^| |y^ - y| + |y| |x^ - x| + u |x^ y^| + eta, |y| <= hi_y + err_y
        err = self.hi * o.err + (o.hi + o.err) * self.err + self.hi * o.hi * UR + eta
        hi = self.hi * o.hi * (1 + UR) + eta
        lo = defect = None
        if self.lo is not None and o.lo is not None:
            # (Px - Dx)(Py - Dy) >= Px Py - Px Dy - Py Dx also where a lower bound is negative (the values are >= 0)
            lo = self.lo * o.lo * (1 - UR)
            defect = self.lo * o.defect + o.lo * self.defect + eta
        minmag = None if under or self.minmag is None or o.minmag is None else self.minmag * o.minmag
        return Value(self.ctx, hi, err, lo, defect, minmag, fv, xv, self.nonneg and o.nonneg,
                     pre=self.hi * o.err + (o.hi + o.err) * self.err)

    __rmul__ = __mul__

    def abs(self):
        return Value(self.ctx, self.hi, self.err, self.lo, self.defect, self.minmag,
                     None if self.fv is None else abs(self.fv), None if self.xv is None else abs(self.xv), True)

    def up(self):
        """ExactInterval.Up: the next double above (the value is >= 0 here): at least the value itself"""
        fv = None if self.fv is None else math.nextafter(self.fv, math.inf)
        return Value(self.ctx, self.hi * (1 + 2 * U) + Poly.const(2 * ETA), self.err + self.hi * (2 * U) + Poly.const(2 * ETA),
                     self.lo, self.defect, self.minmag, fv, self.xv, self.nonneg)



class Context:
    """makes the leaves of a model. concrete: None for the symbolic run, else a Sampler that draws the inputs"""

    def __init__(self, concrete=None, assume_normal=False, check_nodes=True):
        self.concrete = concrete
        # assume_normal: the claim is derived only for inputs where no product is subnormal (the model's entry says
        # what covers the others); check_nodes: a concrete run checks every node's facts (off for a run that samples
        # outside the model's assumptions and checks the final claim only)
        self.assume_normal, self.check_nodes = assume_normal, check_nodes
        self.atoms = {}             # atom -> its value in a concrete run
        self.violations = []

    def lift(self, x):
        return x if isinstance(x, Value) else self.const(x)

    def const(self, c):
        """a double literal (exact: the literal's double value is the number the code multiplies by)"""
        c = float(c)
        f = Fraction(c)
        return Value(self, abs(f), ZERO, abs(f), None, abs(f) if f != 0 else None, c, f, f >= 0)

    def exact(self, name, minmag=None):
        """an input double that is the exact value itself; atom |name|"""
        fv = xv = None
        if self.concrete is not None:
            fv = self.concrete.exact(name)
            xv = Fraction(fv)
            self.atoms[name] = abs(xv)
        a = Poly.atom(name)
        return Value(self, a, ZERO, a, None, minmag, fv, xv)

    def diff(self, name, minmag=None):
        """x^ = fl(p - q) of two exact input doubles, standing for p - q: |x^ - x| <= u |x^| (half an ulp of the
        result; exact where it is subnormal); atom |x^|"""
        fv = xv = None
        if self.concrete is not None:
            p, q = self.concrete.pair(name)
            fv = p - q
            xv = Fraction(p) - Fraction(q)
            self.atoms[name] = abs(Fraction(fv))
        a = Poly.atom(name)
        return Value(self, a, a * U, a, None, minmag, fv, xv)

    def leaf(self, name, err, minmag=None):
        """an input double with |x^ - x| <= err, a polynomial in the atom |name| and atoms made by self.param"""
        fv = xv = None
        if self.concrete is not None:
            fv = self.concrete.exact(name)
            self.atoms[name] = abs(Fraction(fv))
            xv = Fraction(fv) + as_poly(err).eval(self.atoms) * self.concrete.unit()
        a = Poly.atom(name)
        return Value(self, a, err, a, None, minmag, fv, xv)

    def param(self, name, scale=0, minmag=None):
        """a non-negative input double that stands for itself (an error bound e, a size); returns (Value, atom).
        scale: the concrete value is drawn 2^scale times a coordinate's size"""
        fv = None
        if self.concrete is not None:
            fv = abs(self.concrete.exact(name)) * 2.0**scale
            self.atoms[name] = Fraction(fv)
        a = Poly.atom(name)
        return Value(self, a, ZERO, a, None, minmag, fv, None if fv is None else Fraction(fv), True), a

    def check(self, v):
        """a concrete run checks every node's facts at the sample"""
        if self.concrete is None or v.fv is None or not self.check_nodes:
            return
        if math.isinf(v.fv) or math.isnan(v.fv):
            self.violations.append(('not finite', v.fv))
            return
        f = abs(Fraction(v.fv))
        if f > v.hi.eval(self.atoms):
            self.violations.append(('hi', v.fv, float(v.hi.eval(self.atoms))))
        if abs(Fraction(v.fv) - v.xv) > v.err.eval(self.atoms):
            self.violations.append(('err', v.fv, float(abs(Fraction(v.fv) - v.xv)), float(v.err.eval(self.atoms))))
        if v.lo is not None and f < v.lo.eval(self.atoms) - v.defect.eval(self.atoms):
            self.violations.append(('lo', v.fv, float(v.lo.eval(self.atoms))))


class Sampler:
    """draws the inputs of a concrete run: doubles with exponents in [emin, emax], with the mantissa patterns that
    make rounding errors large (all ones, a one after zeros, random), zeros with probability pzero; pairs for a
    rounded difference that are sometimes close (cancellation) and sometimes far apart (a large rounding error)"""

    def __init__(self, rng, emin=-30, emax=30, pzero=0.0):
        self.rng, self.emin, self.emax, self.pzero = rng, emin, emax, pzero
        self.values = {}

    def double(self):
        r = self.rng
        if r.random() < self.pzero:
            return 0.0
        k = r.randrange(4)
        if k == 0:
            m = (1 << 53) - 1 - r.randrange(4)
        elif k == 1:
            m = (1 << 52) + 1 + r.randrange(4)
        elif k == 2:
            m = (1 << 52) + (1 << r.randrange(52)) + r.randrange(2)
        else:
            m = (1 << 52) + r.getrandbits(52)
        v = math.ldexp(m, r.randint(self.emin, self.emax) - 52)
        return -v if r.random() < 0.5 else v

    def exact(self, name):
        if name not in self.values:
            self.values[name] = self.double()
        return self.values[name]

    def pair(self, name):
        if name not in self.values:
            p = self.double()
            k = self.rng.randrange(3)
            if k == 0:
                q = self.double()
            elif k == 1:        # close: the difference is exact (Sterbenz)
                q = p * (1 + self.rng.randint(-8, 8) * 2.0**-50)
            else:               # far apart in exponent: the difference rounds
                q = self.double() * 2.0**-self.rng.randint(1, 60)
            self.values[name] = (p, q)
        return self.values[name]

    def unit(self):
        """a rational in [-1, 1], often at an end"""
        k = self.rng.randrange(4)
        return Fraction(1) if k == 0 else Fraction(-1) if k == 1 else Fraction(self.rng.randint(-1000, 1000), 1000)


def norm3_lower(roundup=1.0, extra_sum_roundings=0):
    """rho with fl(sqrt(fl(fl(x x + y y) + z z)) * roundup) >= rho * |(x, y, z)| where no square underflows (the caller's
    range argument): three squares (1 - u each), two sums (1 - u each, the terms are non-negative), so the radicand is
    >= (1 - u)^3 of the true one, its root >= (1 - u)^1.5 >= (1 - u)^2, the root's rounding (1 - u), the product (1 - u).
    extra_sum_roundings: roundings in the components themselves (eg fl(|b| + |a|))"""
    return frac(roundup) * (1 - U) ** (4 + extra_sum_roundings)


class Mirrored:
    """A computed value e^ with the computed magnitude m^ that the code evaluates next to it (the "permanent": the same
    expression on absolute values, sums for differences), and the invariant of Shewchuk 1997 (section 4.2) and of
    tools/PredicateCompiler (Compiler.cs, filter_bounds):

        |e^| <= m^ (rounding is monotone)        |e^ - e| <= c m^ + A        |z - e| <= cpre m^ + Apre

    c, cpre numbers, A, Apre polynomials in the input atoms (the absolute underflow terms; zero where no product can
    underflow), z the unrounded result of the last operation. Stating the error in the computed m^ itself, not in the
    inputs, is what makes the constant tight to second order in u (a bound like (7 + 56 u) u is decided by it). Rules
    (u' = u / (1 + u); t <= (1 + u) fl(t) for a sum, t <= (1 + u)(fl(t) + eta) for a product that can underflow):

        leaf                 c as given (0 exact, u a rounded difference), m^ = |e^|
        x +- y               z = x^ +- y^: cpre = max(cx, cy)(1 + u), since cx m^x + cy m^y <= max (m^x + m^y) and
                             m^x + m^y <= (1 + u) m^; c = cpre + u (|fl(z) - z| <= u |fl(z)| <= u m^); A = Ax + Ay
        x y                  z = x^ y^: |z - x y| <= |x^| |y^ - y| + |y| |x^ - x|
                             <= (cx + cy + cx cy) m^x m^y + (1 + cx) m^x Ay + (1 + cy) m^y Ax + Ax Ay,
                             m^x m^y <= (1 + u)(m^ + eta): cpre = (cx + cy + cx cy)(1 + u), c = cpre + u,
                             Apre = that absolute part + cpre eta, A = Apre + eta
    """

    def __init__(self, v, m, c, A=ZERO, cpre=None, Apre=None):
        self.v, self.m, self.c, self.A = v, m, frac(c), as_poly(A)
        self.cpre = self.c if cpre is None else frac(cpre)
        self.Apre = self.A if Apre is None else as_poly(Apre)
        ctx = v.ctx
        if ctx.concrete is not None and v.fv is not None and not (math.isinf(v.fv) or math.isnan(v.fv)):
            if not ctx.check_nodes:
                return
            if abs(Fraction(v.fv)) > Fraction(m.fv):
                ctx.violations.append(('mirror |e^| <= m^', v.fv, m.fv))
            if abs(Fraction(v.fv) - v.xv) > self.c * Fraction(m.fv) + self.A.eval(ctx.atoms):
                ctx.violations.append(('mirror err', v.fv, float(abs(Fraction(v.fv) - v.xv)), float(self.c * Fraction(m.fv))))

    @staticmethod
    def leaf(v, c):
        return Mirrored(v, v.abs(), c)

    def _addsub(self, o, sign):
        v = self.v + o.v if sign > 0 else self.v - o.v
        m = self.m + o.m
        cpre = max(self.c, o.c) * (1 + U)
        return Mirrored(v, m, cpre + U, self.A + o.A, cpre, self.A + o.A)

    def __add__(self, o):
        return self._addsub(o, 1)

    def __sub__(self, o):
        return self._addsub(o, -1)

    def __mul__(self, o):
        v = self.v * o.v
        m = self.m * o.m
        cc = self.c + o.c + self.c * o.c
        cpre = cc * (1 + U)
        absolute = self.m.hi * o.A * (1 + self.c) + o.m.hi * self.A * (1 + o.c) + self.A * o.A
        # the mirror's product can underflow unless its factors' magnitudes exclude it (Value.__mul__)
        m_under = not (self.m.minmag is not None and o.m.minmag is not None and self.m.minmag * o.m.minmag >= MIN_NORMAL)
        v_under = not (self.v.minmag is not None and o.v.minmag is not None and self.v.minmag * o.v.minmag >= MIN_NORMAL)
        m_under, v_under = m_under and not self.v.ctx.assume_normal, v_under and not self.v.ctx.assume_normal
        Apre = absolute + (Poly.const(ETA) * cpre if m_under else ZERO)
        A = Apre + (Poly.const(ETA) if v_under else ZERO)
        return Mirrored(v, m, cpre + U, A, cpre, Apre)

    def as_value(self, name):
        """e^ as a Value over the atom `name` = m^ (the computed magnitude): for a sum of mirrored terms that the code
        adds in another order than their magnitudes (InSphere)"""
        ctx = self.v.ctx
        if ctx.concrete is not None:
            ctx.atoms[name] = Fraction(self.m.fv)
        a = Poly.atom(name)
        return Value(ctx, a, a * self.c + self.A, None, None, None, self.v.fv, self.v.xv, False, pre=a * self.cpre + self.Apre)

    def as_value2(self, mname, dname):
        """e^ as a Value whose magnitude is its own atom `dname` = |e^| and whose error is stated in `mname` = m^"""
        ctx = self.v.ctx
        if ctx.concrete is not None:
            ctx.atoms[mname] = Fraction(self.m.fv)
            ctx.atoms[dname] = abs(Fraction(self.v.fv))
        a, d = Poly.atom(mname), Poly.atom(dname)
        # (the last rounding is at most u |e^| = u D, not u m^)
        return Value(ctx, d, a * self.cpre + d * U + self.A, d, None, None, self.v.fv, self.v.xv, False, pre=a * self.cpre + self.Apre)

    def magnitude_atom(self, name):
        """m^ as an input of the bound's expression: a Value that is the atom `name` (the computed double itself)"""
        ctx = self.v.ctx
        if ctx.concrete is not None:
            ctx.atoms[name] = Fraction(self.m.fv)
        a = Poly.atom(name)
        return Value(ctx, a, ZERO, a, None, self.m.minmag, self.m.fv, None if self.m.fv is None else Fraction(self.m.fv), True)

    def error(self, name):
        """|e^ - e| <= this, in the atom `name` of magnitude_atom and the input atoms"""
        return Poly.atom(name) * self.c + self.A

    def sign_error(self, name):
        """the filter "|e^| > B^ => sign e^ = sign e" is sound where this <= B^: |z| >= (|e^| - eta) / (1 + u') and
        |z - e| <= cpre m^ + Apre (sign_dominates)"""
        return (Poly.atom(name) * self.cpre + self.Apre) * (1 + UR) + Poly.const(ETA)


class Verdict:
    """kind: 'derived' (a proof obligation of this analysis), 'arithmetic' (numbers checked, no expression
    modelled) or 'reference' (a constant compared with its published value)"""

    def __init__(self, ok, margin, detail, kind='derived'):
        self.ok, self.margin, self.detail, self.kind = ok, margin, detail, kind

    def __repr__(self):
        return 'Verdict(ok=%s, margin=%s, %s)' % (self.ok, None if self.margin is None else '%.4g' % float(self.margin), self.detail)


def dominates(err, bound_lo, bound_defect=ZERO, abs_floor=None, amgm=False):
    """err <= B^ for every value of the atoms, given B^ >= bound_lo - bound_defect (polynomials) and, optionally,
    B^ >= abs_floor (a number: what a guard such as "permanent > L" gives).

    Proof obligation, monomial by monomial: the demand (err plus the defect's monomials) has in every monomial except
    the constant a coefficient <= (1 - share) times that of bound_lo; the constants (absolute underflow terms) are
    <= share times abs_floor, since B^ = (1 - share) B^ + share B^ >= (1 - share)(bound_lo - defect) + share abs_floor.
    With no abs_floor, share = 0 and the constants need a constant in bound_lo.

    amgm: a demand monomial that bound_lo lacks may be replaced by the two it is at most half the sum of,
    x y r <= (x^2 r + y^2 r) / 2 for atoms x != y of power 1 in it, where bound_lo has both (the cross terms of an
    underflow error against the squares in a lift).

    margin: the smallest (1 - share) bound_lo coefficient / demand coefficient (how much smaller the bound could be)."""
    demand = dict((err + bound_defect.without_const()).t)
    defect_const = bound_defect.const_part()
    missing = []
    for m in list(demand):
        if m == () or bound_lo.t.get(m, 0) > 0:
            continue
        c = demand.pop(m)
        done = False
        # every atom is the magnitude of a finite double, below 2^1024: a monomial whose coefficient makes it less than
        # eta there (a product of two underflow errors) is a constant
        cap = c * Fraction(2**1024) ** sum(p for _, p in m)
        if cap <= ETA:
            demand[()] = demand.get((), 0) + cap
            continue
        if amgm:
            ones = [a for a, p in m if p == 1]
            for i in range(len(ones)):
                for j in range(i + 1, len(ones)):
                    d = dict(m)
                    x, y = ones[i], ones[j]
                    dx = dict(d); del dx[y]; dx[x] = 2
                    dy = dict(d); del dy[x]; dy[y] = 2
                    mx, my = tuple(sorted(dx.items())), tuple(sorted(dy.items()))
                    if bound_lo.t.get(mx, 0) > 0 and bound_lo.t.get(my, 0) > 0:
                        demand[mx] = demand.get(mx, 0) + c / 2
                        demand[my] = demand.get(my, 0) + c / 2
                        done = True
                        break
                if done:
                    break
        if not done:
            missing.append(m)
    if missing:
        return Verdict(False, Fraction(0), 'error monomials without a counterpart in the bound: %r' % (missing,))
    # the share of B^ that pays the absolute terms: just what they need of the guard's floor
    consts = demand.get((), Fraction(0)) + defect_const
    if abs_floor is None:
        share = Fraction(0)
    else:
        if frac(abs_floor) <= 0 or consts >= frac(abs_floor):
            return Verdict(False, None, 'absolute terms %.3g exceed the guard\'s floor %.3g' % (float(consts), float(frac(abs_floor))))
        share = consts / frac(abs_floor)
    margin = worst = None
    for m, c in demand.items():
        if c == 0 or (m == () and abs_floor is not None):
            continue
        b = bound_lo.t.get(m, Fraction(0)) * (1 - share)
        if m == ():
            b -= defect_const
        r = b / c if b > 0 else Fraction(0)
        if margin is None or r < margin:
            margin, worst = r, m
    ok = margin is None or margin >= 1
    detail = 'tightest monomial %s' % ('*'.join(a if p == 1 else '%s^%d' % (a, p) for a, p in worst) if worst else '1' if worst == () else '-')
    if abs_floor is not None:
        detail += '; absolute terms %.3g within the guard floor %.3g' % (float(consts), float(frac(abs_floor)))
    return Verdict(ok, margin, detail)


def sign_dominates(value, bound_lo, bound_defect=ZERO, abs_floor=None, amgm=False):
    """The filter "x^ > B^ => x > 0, x^ < -B^ => x < 0" is sound. x^ = fl(z) has the sign of z (rounding is monotone
    and fl(z) != 0 here), and |z| >= (|x^| - eta) / (1 + u), so it suffices that (1 + u) pre + eta <= B^, pre the
    error of the unrounded z (Shewchuk 1997, section 4.2: the last rounding is relative to the result itself and
    cannot change its sign)."""
    return dominates(value.pre * (1 + UR) + Poly.const(ETA), bound_lo, bound_defect, abs_floor, amgm)
