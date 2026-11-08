"""Z3 pilot 2: the per-operation rules from which tools/PredicateCompiler derives the static error bounds of its
floating-point filters (Compiler.cs, filter_bounds), each stated as a small lemma and checked by Z3.

Every obligation is a counterexample query (assumptions AND NOT claim) that must return UNSAT; its assumptions alone
must be SAT (no vacuous proof); every rule has at least one controlled mutant that must return SAT, and every SAT
witness is replayed outside Z3 (exact fractions, or Python's own IEEE binary64 arithmetic). UNKNOWN never counts.
The whole predicates (Orient3dLPI, Orient3dTPI) are not proved monolithically: the composition of the rules over an
expression graph is a structural induction stated in README.md, and `composition_check` re-derives the generated
constants from the generated code with the same rules, independently of the C# generator. See README.md.
"""
import math
import re
import struct
import time
from fractions import Fraction

import z3 as z

import pilot

ROOT = pilot.ROOT
COMPILER = 'tools/PredicateCompiler/Compiler.cs'
GENERATED = 'math/Generated/IndirectPredicates.cs'

# The rule constants. The drift check below keeps them equal to Compiler.cs.
U = Fraction(1, 2**53)          # unit roundoff of binary64 round to nearest
GRID_EXPONENT = 960             # k (a + 52) <= 960: every nonzero computed value is >= 2^-960
INPUT_ULP_BITS = 52             # a nonzero input of magnitude >= 2^-a is a multiple of 2^-(a + 52)
K_MIN_EXPONENT = -62            # K >= 2^-62, so K m~ >= 2^-1022 (normal)
NORMAL_MIN_EXPONENT = -1022
BOUND_BITS = 1000               # GateHigh: every value bound below 2^1000 (not needed for soundness, see README)

F64 = z.Float64()
# binary64 precision with a 15-bit exponent: the frame in which the grid lemmas are stated (scaling by 2^s is exact)
XF = z.FPSort(15, 53)
RNE = z.RNE()


# ---------------------------------------------------------------------------------------------------------------
# source contracts: the rules as written in Compiler.cs (fail on drift instead of checking a stale transcription)

def _normalize(s):
    return re.sub(r'\s+', '', s)


def _method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 0
    for end in range(opening, len(text)):
        depth += (text[end] == '{') - (text[end] == '}')
        if depth == 0:
            return text[start:end+1]
    raise ValueError('Unterminated source method')


FILTER_BOUNDS = """static void filter_bounds(Predicate p)
        {
            var u = Rational.U;
            var one = Rational.One;
            foreach (var n in p.Nodes) {
                switch (n.Op) {
                    case Op.Input: n.C = Rational.Zero; break;
                    case Op.Add: case Op.Sub:
                        n.C = n.Leaf ? u : (u + Rational.Max(n.A.C, n.B.C)) / (one - u);
                        break;
                    case Op.Mul:
                        n.C = (u + n.A.C + n.B.C + n.A.C * n.B.C) / (one - u);
                        break;
                }
            }
            p.FilterK = p.Signs.Select(n => (n.C / (one - u)).CeilDouble()).ToList();

            if (!p.Homogeneous)
                throw new NotSupportedException($"{p.Name}: the double entry needs a homogeneous formula (sums of equal degree)");
            p.GateLow = 960 / p.Degree - 52;
            // the decision product K m~ must stay normal for m~ >= 2^-960: K >= 2^-62
            if (p.Signs.Any(n => (n.C / (one - u)).CeilDouble() < Math.ScaleB(1.0, -62)))
                throw new NotSupportedException($"{p.Name}: a filter constant below 2^-62");
            p.GateHigh = 0;
            while (p.GateHigh < 1000 && p.Nodes.All(n => n.Bound.At(BigInteger.Pow(2, p.GateHigh + 1)).GetBitLength() <= 1000))
                p.GateHigh++;
            if (p.GateLow < 1 || p.GateHigh < 1)
                throw new NotSupportedException($"{p.Name}: no filter range (degree {p.Degree})");
        }"""

FILTER_VALUE = """static void emit_filter_value(Node n, StringBuilder sb, string I2)
        {
            string e, m;
            if (n.Op == Op.Input) {
                e = $"{n.Point}.{n.Comp}";
                m = $"Math.Abs({val(n)})";
            } else {
                e = $"{val(n.A)} {opc(n.Op)} {val(n.B)}";
                m = n.Leaf ? $"Math.Abs({val(n)})" : n.Op == Op.Mul ? $"m{val(n.A)} * m{val(n.B)}" : $"m{val(n.A)} + m{val(n.B)}";
            }
            sb.Append($"{I2}double {val(n)} = {e}, m{val(n)} = {m};".PadRight(96)).Append($" // c = {n.C.InUnitsOfU()}\\n");
        }"""

FILTER_DECISION = """static void emit_filter_decision(Predicate p, StringBuilder sb, string I2)
        {
            sb.Append($"{I2}int __r = 1;\\n");
            for (int f = 0; f < p.Signs.Count; ++f) {
                var n = p.Signs[f];
                sb.Append($"{I2}// {ref_(n)}: |e~ - e| <= {n.C.InUnitsOfU()} m~ (K = c / (1 - u), rounded up)\\n");
                sb.Append($"{I2}if (m{val(n)} == 0) return 0;\\n");
                sb.Append($"{I2}if (!(Math.Abs({val(n)}) > {lit(p.FilterK[f])} * m{val(n)})) return 2;\\n");
                sb.Append($"{I2}if ({val(n)} < 0) __r = -__r;\\n");
            }
            sb.Append($"{I2}return __r;\\n");
        }"""

UNIT_ROUNDOFF = 'public static readonly Rational U = new Rational(1, BigInteger.Pow(2, 53));'
GATE_HELPER = 'static bool __in(double x, double lo, double hi) { double a = Math.Abs(x); return x == 0 || (a >= lo && a <= hi); }'


def source_contracts():
    """The rule code of Compiler.cs must equal the transcription the lemmas model; returns the checked constants."""
    text = (ROOT / COMPILER).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    for signature, expected, what in [('static void filter_bounds(', FILTER_BOUNDS, 'filter_bounds'),
                                      ('static void emit_filter_value(', FILTER_VALUE, 'emit_filter_value'),
                                      ('static void emit_filter_decision(', FILTER_DECISION, 'emit_filter_decision')]:
        if _normalize(_method(text, signature)) != _normalize(expected):
            raise ValueError(f'Compiler.cs {what} changed; review the lemmas of tools/Z3Pilot/filter_rules.py')
    for line, what in [(UNIT_ROUNDOFF, 'Rational.U'), (GATE_HELPER, 'the gate helper __in')]:
        if _normalize(line) not in _normalize(text):
            raise ValueError(f'Compiler.cs {what} changed; review the lemmas of tools/Z3Pilot/filter_rules.py')
    # the constants of the lemmas, read back from the checked source (a second guard next to the text comparison)
    found = dict(u=Fraction(1, 2**int(re.search(r'Rational U = new Rational\(1, BigInteger\.Pow\(2, (\d+)\)\)', text).group(1))),
                 grid=int(re.search(r'GateLow = (\d+) / p\.Degree - (\d+);', text).group(1)),
                 ulp=int(re.search(r'GateLow = (\d+) / p\.Degree - (\d+);', text).group(2)),
                 kmin=int(re.search(r'Math\.ScaleB\(1\.0, (-\d+)\)\)\)', text).group(1)),
                 bound=int(re.search(r'GetBitLength\(\) <= (\d+)\)', text).group(1)))
    expected = dict(u=U, grid=GRID_EXPONENT, ulp=INPUT_ULP_BITS, kmin=K_MIN_EXPONENT, bound=BOUND_BITS)
    if found != expected:
        raise ValueError(f'rule constants differ: Compiler.cs {found}, lemmas {expected}')
    return {k: str(v) for k, v in found.items()}


# ---------------------------------------------------------------------------------------------------------------
# the lemmas. Real-arithmetic rules are written once over a backend, so a SAT witness of a mutant is replayed with
# exact fractions by the same formula, outside Z3.

class ZB:
    """Z3 backend"""
    u = z.RealVal(f'1/{2**53}')
    And, Or, Not = staticmethod(z.And), staticmethod(z.Or), staticmethod(z.Not)
    idiv = staticmethod(lambda a, b: a / b)       # Z3 integer division (floor for positive operands)

    @staticmethod
    def const(q):
        return z.RealVal(f'{q.numerator}/{q.denominator}')


class QB:
    """exact rational backend (the replay)"""
    u = U
    And = staticmethod(lambda *a: all(a))
    Or = staticmethod(lambda *a: any(a))
    Not = staticmethod(lambda a: not a)
    idiv = staticmethod(lambda a, b: a // b)

    @staticmethod
    def const(q):
        return q


def le_abs(B, a, b):
    return B.And(-b <= a, a <= b)


def gt_abs(B, a, b):
    return B.Or(a > b, -a > b)


class Vars(dict):
    __getattr__ = dict.__getitem__


def leaf_rule(B, v, c=None):
    """R2, a sum or difference of two inputs: m~ = |e~|, c = u. Model: e = e~ (1 + d), |d| <= u (Higham (2.5))."""
    c = B.u if c is None else c
    hyps = [le_abs(B, v.d, B.u), v.e == v.et * (1 + v.d), v.m >= 0, B.Or(v.m == v.et, v.m == -v.et)]
    return hyps, gt_abs(B, v.et - v.e, c * v.m)


def sum_rule(B, v, sign, variant=None):
    """R3, x +- y unless both are inputs: e~ = fl(x~ +- y~), m~ = fl(m~x + m~y), c = (u + max(cx, cy)) / (1 - u).
    Model: fl(s) = s (1 + d), |d| <= u (Higham (2.4)). M is any upper bound of cx and cy (the generator: the max)."""
    u = B.u
    hyps = [v.cx >= 0, v.cy >= 0, le_abs(B, v.ax - v.ex, v.cx * v.mx), le_abs(B, v.ay - v.ey, v.cy * v.my),
            le_abs(B, v.ax, v.mx), le_abs(B, v.ay, v.my), le_abs(B, v.d1, u), le_abs(B, v.d2, u)]
    if variant != 'cx_only':
        hyps += [v.M >= v.cx, v.M >= v.cy]
    e = v.ex + sign * v.ey
    et = (v.ax + sign * v.ay) * (1 + v.d1)
    m = (v.mx + v.my) * (1 + v.d2)
    c = {None: (u + v.M) / (1 - u), 'no_div': u + v.M, 'cx_only': (u + v.cx) / (1 - u)}[variant]
    return hyps, gt_abs(B, et - e, c * m)


def mul_rule(B, v, variant=None):
    """R4, x y: e~ = fl(x~ y~), m~ = fl(m~x m~y), c = (u + cx + cy + cx cy) / (1 - u)."""
    u = B.u
    hyps = [v.cx >= 0, v.cy >= 0, le_abs(B, v.ax - v.ex, v.cx * v.mx), le_abs(B, v.ay - v.ey, v.cy * v.my),
            le_abs(B, v.ax, v.mx), le_abs(B, v.ay, v.my), le_abs(B, v.d1, u), le_abs(B, v.d2, u)]
    e = v.ex * v.ey
    et = v.ax * v.ay * (1 + v.d1)
    m = v.mx * v.my * (1 + v.d2)
    c = {None: (u + v.cx + v.cy + v.cx * v.cy) / (1 - u), 'no_cross': (u + v.cx + v.cy) / (1 - u),
         'no_div': u + v.cx + v.cy + v.cx * v.cy}[variant]
    return hyps, gt_abs(B, et - e, c * m)


def decision_rule(B, v, variant=None):
    """R5: |e~ - e| <= c m~, m~ > 0, K >= c / (1 - u), |e~| > fl(K m~) = K m~ (1 + d)  =>  sign(e) = sign(e~) != 0."""
    u = B.u
    t = v.K * v.m * (1 + v.d)
    hyps = [v.c >= 0, v.m > 0, le_abs(B, v.et - v.e, v.c * v.m), le_abs(B, v.d, u),
            v.K >= v.c if variant == 'k_is_c' else v.K * (1 - u) >= v.c,
            B.Or(v.et >= t, -v.et >= t) if variant == 'ge' else gt_abs(B, v.et, t)]
    return hyps, v.et * v.e <= 0


def zero_rule(B, v, variant=None):
    """R6: |e~ - e| <= c m~, |e~| <= m~, m~ = 0  =>  e = 0 (the shortcut 'if (m~ == 0) return 0')."""
    hyps = [v.c >= 0, v.m >= 0, le_abs(B, v.et - v.e, v.c * v.m), le_abs(B, v.et, v.m),
            v.et == 0 if variant == 'tests_value' else v.m == 0]
    return hyps, B.Not(v.e == 0)


def value_rule(B, v, variant=None):
    """R0, the value rule of the bound polynomials: |x| <= Mx, |y| <= My => |x +- y| <= Mx + My, |x y| <= Mx My."""
    prod = v.Mx + v.My if variant == 'product_as_sum' else v.Mx * v.My
    hyps = [le_abs(B, v.x, v.Mx), le_abs(B, v.y, v.My)]
    return hyps, B.Or(gt_abs(B, v.x + v.y, v.Mx + v.My), gt_abs(B, v.x - v.y, v.Mx + v.My), gt_abs(B, v.x * v.y, prod))


def exponent_rule(B, v, variant=None):
    """E: the gate exponents. Degree k >= 1, a = 960 / k - 52 >= 1 (integer division), a value of degree j <= k:
    j (a + 52) <= 960 (every nonzero value and magnitude >= 2^-960) and K m~ >= 2^(-62 - 960) >= 2^-1022 (normal)."""
    ulp = INPUT_ULP_BITS - 1 if variant == 'gate_low_plus_one' else INPUT_ULP_BITS
    kmin = K_MIN_EXPONENT - 1 if variant == 'k_min_minus_one' else K_MIN_EXPONENT
    a = B.idiv(GRID_EXPONENT, v.k) - ulp
    hyps = [v.k >= 1, v.j >= 1, v.j <= v.k, a >= 1]
    return hyps, B.Or(v.j * (a + INPUT_ULP_BITS) > GRID_EXPONENT, kmin - GRID_EXPONENT < NORMAL_MIN_EXPONENT)


RN_REAL = z.Function('RN', z.RealSort(), z.RealSort())


def rounding_axioms():
    """RN as a function of the exact result: monotone and odd (IEEE 754: every operation is the exact result rounded
    by one rounding function; round to nearest even is monotone and symmetric). Oddness is also checked bit-precisely
    for binary64 (rn_odd_*); monotonicity is the assumption (README)."""
    s, t = z.Reals('rn_s rn_t')
    return [z.ForAll([s, t], z.Implies(s <= t, RN_REAL(s) <= RN_REAL(t))), z.ForAll([s], RN_REAL(-s) == -RN_REAL(s))]


def magnitude_rule(B, v, op):
    """M: |x~| <= m~x, |y~| <= m~y  =>  |RN(x~ op y~)| <= RN(m~x + m~y) (op + or -), |RN(x~ y~)| <= RN(m~x m~y).
    The exact backend replays the operand assumptions only (RN is uninterpreted)."""
    hyps = [le_abs(B, v.ax, v.mx), le_abs(B, v.ay, v.my)]
    if B is QB:
        return hyps, True
    e = {'+': v.ax + v.ay, '-': v.ax - v.ay, '*': v.ax * v.ay}[op]
    m = v.mx * v.my if op == '*' else v.mx + v.my
    return rounding_axioms() + hyps, z.Not(le_abs(B, RN_REAL(e), RN_REAL(m)))


def real_vars(names):
    return Vars({n: z.Real(n) for n in names.split()})


def int_vars(names):
    return Vars({n: z.Int(n) for n in names.split()})


SUM_VARS = 'ex ey ax ay mx my cx cy M d1 d2'
MUL_VARS = 'ex ey ax ay mx my cx cy d1 d2'


def _eq(v, values):
    return [v[k] == (z.RealVal(f'{Fraction(x).numerator}/{Fraction(x).denominator}') if isinstance(v[k], z.ArithRef)
                     and v[k].is_real() else x) for k, x in values.items()]


def real_obligations():
    """(rule, name, model, variables, formula(B, v), nonvacuity witness values)"""
    u = U
    yield ('R0 value rule', 'value_rule', 'NRA', real_vars('x y Mx My'), lambda B, v: value_rule(B, v),
           dict(x=3, y=-2, Mx=3, My=2))
    yield ('R2 leaf', 'leaf_input_pm_input', 'NRA, fl(s) = s / (1 + d)', real_vars('e et d m'), leaf_rule,
           dict(et=1, d=u, e=1 + u, m=1))
    for sign, nm in [(1, 'sum_add'), (-1, 'sum_sub')]:
        yield ('R3 sum', nm, 'NRA, fl(s) = s (1 + d)', real_vars(SUM_VARS),
               (lambda s: lambda B, v: sum_rule(B, v, s))(sign),
               dict(ex=1, ey=1, ax=1 + u, ay=1 - u, mx=2, my=2, cx=u, cy=u, M=u, d1=u, d2=-u))
    yield ('R4 product', 'product', 'NRA, fl(p) = p (1 + d)', real_vars(MUL_VARS), lambda B, v: mul_rule(B, v),
           dict(ex=1, ey=2, ax=1 + u, ay=2 - 2 * u, mx=2, my=2, cx=u, cy=u, d1=u, d2=-u))
    yield ('R5 decision', 'decision', 'NRA, fl(K m~) = K m~ (1 + d)', real_vars('e et m c K d'),
           lambda B, v: decision_rule(B, v), dict(e=1, et=1, m=1, c=18 * u, K=Fraction(1, 2**40), d=u))
    for op, nm in [('+', 'magnitude_sum'), ('-', 'magnitude_difference'), ('*', 'magnitude_product')]:
        yield ('M magnitude', nm, 'reals, RN monotone and odd (quantified)', real_vars('ax ay mx my'),
               (lambda o: lambda B, v: magnitude_rule(B, v, o))(op),
               dict(ax=-1, ay=Fraction(1, 2), mx=1, my=1))
    yield ('R6 zero shortcut', 'zero_magnitude', 'LRA', real_vars('e et m c'), lambda B, v: zero_rule(B, v),
           dict(e=0, et=0, m=0, c=18 * u))
    yield ('E gate exponents', 'gate_exponents', 'NIA (integer division)', int_vars('k j'),
           lambda B, v: exponent_rule(B, v), dict(k=6, j=6))


def real_mutants():
    """(rule, name, variables, formula(B, v)): each must be SAT, and its witness must replay with fractions"""
    one_ulp_below_u = U - Fraction(1, 2**106)        # the double just below u = 2^-53
    yield 'R0 value rule', 'value_product_as_sum', real_vars('x y Mx My'), lambda B, v: value_rule(B, v, 'product_as_sum')
    yield ('R2 leaf', 'leaf_c_one_ulp_below_u', real_vars('e et d m'),
           lambda B, v: leaf_rule(B, v, B.const(one_ulp_below_u)))
    yield 'R3 sum', 'sum_without_div_1_minus_u', real_vars(SUM_VARS), lambda B, v: sum_rule(B, v, 1, 'no_div')
    yield 'R3 sum', 'sum_cx_instead_of_max', real_vars(SUM_VARS), lambda B, v: sum_rule(B, v, -1, 'cx_only')
    yield 'R4 product', 'product_without_cx_cy', real_vars(MUL_VARS), lambda B, v: mul_rule(B, v, 'no_cross')
    yield 'R4 product', 'product_without_div_1_minus_u', real_vars(MUL_VARS), lambda B, v: mul_rule(B, v, 'no_div')
    yield 'R5 decision', 'decision_k_equals_c', real_vars('e et m c K d'), lambda B, v: decision_rule(B, v, 'k_is_c')
    yield 'R5 decision', 'decision_ge_instead_of_gt', real_vars('e et m c K d'), lambda B, v: decision_rule(B, v, 'ge')
    yield 'R6 zero shortcut', 'zero_tests_value_not_magnitude', real_vars('e et m c'), lambda B, v: zero_rule(B, v, 'tests_value')
    yield 'E gate exponents', 'gate_low_one_too_large', int_vars('k j'), lambda B, v: exponent_rule(B, v, 'gate_low_plus_one')
    yield 'E gate exponents', 'k_min_one_too_small', int_vars('k j'), lambda B, v: exponent_rule(B, v, 'k_min_minus_one')


# ---------------------------------------------------------------------------------------------------------------
# bit-precise lemmas (IEEE binary64, round to nearest even), and the frame of the grid lemmas

def _fp(name, sort=F64):
    return z.FP(name, sort)


def _finite(*xs):
    return [c for x in xs for c in (z.Not(z.fpIsInf(x)), z.Not(z.fpIsNaN(x)))]


def _integral(x):
    return z.fpEQ(z.fpRoundToIntegral(RNE, x), x)


def _val(x, sort=F64):
    return z.FPVal(x, sort)


def fp_obligations():
    """(rule, name, model, assumptions, bad, nonvacuity witness constraint)"""
    # M, bit-precise part: RN is odd for the operations of the filter (the 'odd' axiom of rounding_axioms)
    a, b = _fp('a'), _fp('b')
    w_ab = z.And(z.fpEQ(a, _val(1.0 + 2.0**-52)), z.fpEQ(b, _val(-(2.0**-60))))
    yield ('M magnitude', 'rn_odd_sum', 'binary64 RNE', _finite(a, b),
           z.Not(z.fpEQ(z.fpNeg(z.fpAdd(RNE, a, b)), z.fpAdd(RNE, z.fpNeg(a), z.fpNeg(b)))), w_ab)
    yield ('M magnitude', 'rn_odd_product', 'binary64 RNE', _finite(a, b),
           z.Not(z.fpEQ(z.fpAbs(z.fpMul(RNE, a, b)), z.fpMul(RNE, z.fpAbs(a), z.fpAbs(b)))), w_ab)
    # G: the no-underflow argument (grid), stated with integers in binary64 precision and a 15-bit exponent
    x = _fp('x', F64)
    yield ('G grid', 'grid_input', 'binary64', _finite(x) + [z.fpGEQ(z.fpAbs(x), _val(2.0**52))],
           z.Not(_integral(x)), z.fpEQ(x, _val(-(2.0**60))))
    gx, gy = _fp('gx', XF), _fp('gy', XF)
    grid = _finite(gx, gy) + [_integral(gx), _integral(gy)]
    w_grid = z.And(z.fpEQ(gx, _val(2.0**60 + 2.0**8, XF)), z.fpEQ(gy, _val(-3.0, XF)))
    for op, nm in [(z.fpAdd, 'grid_sum'), (z.fpSub, 'grid_difference'), (z.fpMul, 'grid_product')]:
        r = op(RNE, gx, gy)
        yield 'G grid', nm, 'precision 53, exponent 15 bits, RNE', grid + [z.Not(z.fpIsInf(r))], z.Not(_integral(r)), w_grid
    # O: overflow is harmless (an infinite or NaN magnitude never decides)
    m1, m2 = _fp('m1'), _fp('m2')
    nonneg = [z.Not(z.fpIsNaN(m1)), z.Not(z.fpIsNaN(m2)), z.fpGEQ(m1, _val(0.0)), z.fpGEQ(m2, _val(0.0))]
    w_nn = z.And(z.fpEQ(m1, _val(2.0**1000)), z.fpEQ(m2, _val(2.0**20)))
    for op, nm in [(z.fpAdd, 'overflow_sum'), (z.fpMul, 'overflow_product')]:
        r = op(RNE, m1, m2)
        yield ('O overflow', nm, 'binary64 RNE', nonneg + _finite(r), z.Or(z.fpIsInf(m1), z.fpIsInf(m2)), w_nn)
    K, m, et = _fp('K'), _fp('m'), _fp('et')
    yield ('O overflow', 'overflow_never_decides', 'binary64 RNE',
           _finite(K) + [z.fpGT(K, _val(0.0)), z.Or(z.fpIsNaN(m), z.fpEQ(m, z.fpPlusInfinity(F64)))],
           z.Or(z.fpEQ(m, _val(0.0)), z.fpGT(z.fpAbs(et), z.fpMul(RNE, K, m))),
           z.And(z.fpEQ(K, _val(1.998401444325284e-15)), z.fpIsNaN(m), z.fpEQ(et, _val(1.0))))


def _double(model, t):
    if z.is_true(model.eval(z.fpIsNaN(t), model_completion=True)):
        return math.nan     # the bits of a NaN are unspecified in SMT-LIB
    bits = model.eval(z.fpToIEEEBV(t), model_completion=True).as_long()
    return struct.unpack('>d', bits.to_bytes(8, 'big'))[0]


def round_to_precision(q, p):
    """q rounded to p significant bits, ties to even (unbounded exponent)"""
    if q == 0:
        return q
    sign, q = (-1 if q < 0 else 1), abs(q)
    e = q.numerator.bit_length() - q.denominator.bit_length()
    if Fraction(2)**e > q:
        e -= 1
    scale = Fraction(2)**(p - 1 - e)
    n = q * scale
    i = n.numerator // n.denominator
    rest = n - i
    if rest > Fraction(1, 2) or (rest == Fraction(1, 2) and i % 2 == 1):
        i += 1
    return sign * i / scale


def fp_mutants():
    """(rule, name, assumptions, bad, replay(model) -> dict): SAT, and the replay with Python's IEEE doubles"""
    ax, ay, mx, my = (_fp(n) for n in ('ax', 'ay', 'mx', 'my'))
    mono = _finite(ax, ay, mx, my) + [z.fpLEQ(z.fpAbs(ax), mx), z.fpLEQ(z.fpAbs(ay), my)]

    def replay_mono(model):
        vals = {n: _double(model, t) for n, t in [('ax', ax), ('ay', ay), ('mx', mx), ('my', my)]}
        ok = abs(vals['ax']) <= vals['mx'] and abs(vals['ay']) <= vals['my'] and \
            abs(vals['ax'] - vals['ay']) > vals['mx'] - vals['my']
        return dict(vals, replayed=ok)
    # the magnitude of a difference taken as m~x - m~y (a missing absolute sum)
    yield ('M magnitude', 'magnitude_difference_as_difference', mono,
           z.Not(z.fpLEQ(z.fpAbs(z.fpSub(RNE, ax, ay)), z.fpSub(RNE, mx, my))), replay_mono)
    x = _fp('x')

    def replay_grid(model):
        v = _double(model, x)
        return dict(x=v.hex(), replayed=abs(v) >= 2.0**51 and v != math.floor(v))
    yield ('G grid', 'grid_input_threshold_2_51', _finite(x) + [z.fpGEQ(z.fpAbs(x), _val(2.0**51))],
           z.Not(_integral(x)), replay_grid)
    a, b = _fp('a'), _fp('b')
    ext = lambda t: z.fpFPToFP(RNE, t, XF)
    r = z.fpMul(RNE, a, b)

    def replay_frame(model):
        va, vb = _double(model, a), _double(model, b)
        exact = Fraction(va) * Fraction(vb)
        # the binary64 product (subnormal) differs from the product rounded to 53 bits
        return dict(a=va.hex(), b=vb.hex(), product=(va * vb).hex(),
                    replayed=exact != 0 and Fraction(va * vb) != round_to_precision(exact, 53))
    # the bridge from binary64 to the grid frame needs |exact result| >= 2^-1022: a subnormal product differs
    yield ('G grid', 'frame_product_subnormal', _finite(a, b) + [z.fpIsSubnormal(r)],
           z.Not(z.fpEQ(ext(r), z.fpMul(RNE, ext(a), ext(b)))), replay_frame)
    m1, m2 = _fp('m1'), _fp('m2')
    rp = z.fpMul(RNE, m1, m2)

    def replay_overflow(model):
        v1, v2 = _double(model, m1), _double(model, m2)
        p = v1 * v2
        return dict(m1=v1, m2=v2, product=p, replayed=not math.isinf(p) and (math.isinf(v1) or math.isinf(v2)))
    # 'finite' read as 'not infinite': 0 * inf = NaN would pass
    yield ('O overflow', 'overflow_nan_counted_as_finite',
           [z.Not(z.fpIsNaN(m1)), z.Not(z.fpIsNaN(m2)), z.fpGEQ(m1, _val(0.0)), z.fpGEQ(m2, _val(0.0)), z.Not(z.fpIsInf(rp))],
           z.Or(z.fpIsInf(m1), z.fpIsInf(m2)), replay_overflow)
    K, m, et = _fp('K'), _fp('m'), _fp('et')

    def replay_le(model):
        vK, vm, ve = _double(model, K), _double(model, m), _double(model, et)
        decides = not (vm == 0) and not (abs(ve) <= vK * vm)
        return dict(K=vK, m=repr(vm), et=ve, replayed=decides and (math.isnan(vm) or math.isinf(vm)))
    # the test written as 'if (Math.Abs(e) <= K * m) return 2;': a NaN magnitude would decide
    yield ('O overflow', 'overflow_test_le_instead_of_not_gt',
           _finite(K) + [z.fpGT(K, _val(0.0)), z.Or(z.fpIsNaN(m), z.fpEQ(m, z.fpPlusInfinity(F64)))],
           z.And(z.Not(z.fpEQ(m, _val(0.0))), z.Not(z.fpLEQ(z.fpAbs(et), z.fpMul(RNE, K, m)))), replay_le)


def leaf_tightness():
    """u is attained: 1 + 2^-53 rounds to 1 (ties to even), error 2^-53 = u |e~| (Python's IEEE doubles)."""
    x, y = 1.0, 2.0**-53
    et = x + y
    err = abs(Fraction(et) - (Fraction(x) + Fraction(y)))
    return dict(x=x.hex(), y=y.hex(), rounded=et.hex(), error_over_u_m=str(err / (U * abs(Fraction(et)))))


# ---------------------------------------------------------------------------------------------------------------
# composition: the generated filters re-derived from the generated code with the same rules (not with Z3)

LINE = re.compile(r'^\s*double (\w+) = ([^,]+), m(\w+) = ([^;]+);\s*// c = ([0-9.]+) u\s*$')
DECIDE = re.compile(r'if \(!\(Math\.Abs\((\w+)\) > ([0-9.e+-]+) \* m(\w+)\)\) return 2;')


def ceil_double(q):
    """the smallest double >= q (q > 0)"""
    d = float(q)
    while Fraction(d) < q:
        d = math.nextafter(d, math.inf)
    while Fraction(math.nextafter(d, 0.0)) >= q:
        d = math.nextafter(d, 0.0)
    return d


def composition_check(text=None):
    """For every generated XFilter: each value line applies R1-R4 to its operands (m~ form and c), every K is the
    smallest double >= c / (1 - u) and >= 2^-62, each decided factor is tested for m~ == 0 first, the formula is
    homogeneous and the gate is [2^-(960 / k - 52), 2^b] with b from the value rule. Raises on any difference."""
    if text is None:
        text = (ROOT / GENERATED).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    out = {}
    filters = list(re.finditer(r'every coordinate 0 or of magnitude in \[2\^-(\d+), 2\^(\d+)\] </summary>\n'
                               r'\s*\[MethodImpl\(JitHints\.Optimize\)\]\n\s*internal static int (\w+)Filter\(([^)]*)\)\n\s*\{\n(.*?)\n        \}',
                               text, re.S))
    if len(filters) < 2:
        raise ValueError('generated filters not found')
    for f in filters:
        low, high, name, body = int(f.group(1)), int(f.group(2)), f.group(3), f.group(5)
        c, kind, deg, bound = {}, {}, {}, {}
        lines = body.split('\n')
        decisions = []
        for i, line in enumerate(lines):
            mm = LINE.match(line)
            if mm:
                v, e, mv, me, shown = mm.groups()
                if mv != v:
                    raise ValueError(f'{name}: magnitude name of {v}')
                e = e.strip()
                me = me.strip()
                parts = e.split()
                if len(parts) == 1:
                    if not re.fullmatch(r'\w+\.[xyz]', e) or me != f'Math.Abs({v})':
                        raise ValueError(f'{name}: input line {v}')
                    c[v], kind[v], deg[v], bound[v] = Fraction(0), 'input', 1, 'm'
                else:
                    a, op, b = parts
                    leaf = op in '+-' and kind[a] == 'input' and kind[b] == 'input'
                    if leaf:
                        want_m, cv = f'Math.Abs({v})', U
                    elif op == '*':
                        want_m, cv = f'm{a} * m{b}', (U + c[a] + c[b] + c[a] * c[b]) / (1 - U)
                    else:
                        want_m, cv = f'm{a} + m{b}', (U + max(c[a], c[b])) / (1 - U)
                    if me != want_m:
                        raise ValueError(f'{name}: magnitude of {v} is {me}, the rule gives {want_m}')
                    if op in '+-' and deg[a] != deg[b]:
                        raise ValueError(f'{name}: not homogeneous at {v}')
                    c[v], kind[v] = cv, 'op'
                    deg[v] = deg[a] + deg[b] if op == '*' else deg[a]
                    bound[v] = (a, op, b)
                if abs(float(c[v] / U) - float(shown)) > 1e-6:
                    raise ValueError(f'{name}: c of {v} shown as {shown} u, the rules give {float(c[v] / U)} u')
                continue
            dm = DECIDE.search(line)
            if dm:
                v, k, mv = dm.groups()
                if mv != v or lines[i - 1].strip() != f'if (m{v} == 0) return 0;':
                    raise ValueError(f'{name}: decision of {v}')
                want = ceil_double(c[v] / (1 - U))
                if float(k) != want:
                    raise ValueError(f'{name}: K of {v} is {k}, the rules give {want!r}')
                if want < 2.0**K_MIN_EXPONENT:
                    raise ValueError(f'{name}: K below 2^{K_MIN_EXPONENT}')
                decisions.append(dict(value=v, c_over_u=float(c[v] / U), K=k))
        if not decisions:
            raise ValueError(f'{name}: no decision found')
        degree = max(deg.values())
        if low != GRID_EXPONENT // degree - INPUT_ULP_BITS:
            raise ValueError(f'{name}: gate low 2^-{low}, the rule gives 2^-{GRID_EXPONENT // degree - INPUT_ULP_BITS}')

        def at(b):
            vals = {}
            for v, x in bound.items():
                if x == 'm':
                    vals[v] = 2**b
                else:
                    a, op, w = x
                    vals[v] = vals[a] * vals[w] if op == '*' else vals[a] + vals[w]
            return vals
        gate_high = 0
        while gate_high < BOUND_BITS and all(x.bit_length() <= BOUND_BITS for x in at(gate_high + 1).values()):
            gate_high += 1
        if high != gate_high:
            raise ValueError(f'{name}: gate high 2^{high}, the value rule gives 2^{gate_high}')
        out[name] = dict(degree=degree, gate=[-low, high], values=len(c), decisions=decisions)
    return out


# ---------------------------------------------------------------------------------------------------------------

def _check(assumptions, bad, timeout_ms):
    s = z.Solver()
    s.set(timeout=timeout_ms)
    s.add(*assumptions)
    s.add(bad)
    t0 = time.monotonic()
    r = s.check()
    return s, r, time.monotonic() - t0


def _replay_real(model, v, formula):
    vals = {}
    for k, var in v.items():
        val = model.eval(var, model_completion=True)
        if z.is_int_value(val):
            vals[k] = val.as_long()
        elif z.is_rational_value(val):
            vals[k] = val.as_fraction()
        else:
            return dict(replayed=False, reason=f'{k} is not rational: {val}')
    hyps, bad = formula(QB, Vars(vals))
    return dict({k: str(x) for k, x in vals.items()}, replayed=bool(all(hyps) and bad))


def run(timeout_ms=120000):
    contracts = source_contracts()
    composition = composition_check()
    rows, mutants = [], []
    for rule, name, model, v, formula, witness in real_obligations():
        hyps, bad = formula(ZB, v)
        s, r, secs = _check(hyps, bad, timeout_ms)
        row = dict(rule=rule, name=name, model=model, result=str(r), seconds=round(secs, 3))
        if r == z.unknown:
            row['reason'] = s.reason_unknown()
        _, rr, _ = _check(hyps + _eq(v, witness), z.BoolVal(True), timeout_ms)
        row['assumptions_result'] = str(rr)
        # the same witness through the exact backend (no Z3)
        qh, _ = formula(QB, Vars({k: x if v[k].is_int() else Fraction(x) for k, x in witness.items()}))
        row['assumptions_replayed'] = all(qh)
        rows.append(row)
    for rule, name, model, hyps, bad, witness in fp_obligations():
        s, r, secs = _check(hyps, bad, timeout_ms)
        row = dict(rule=rule, name=name, model=model, result=str(r), seconds=round(secs, 3))
        if r == z.unknown:
            row['reason'] = s.reason_unknown()
        _, rr, _ = _check(hyps, witness, timeout_ms)
        row['assumptions_result'] = str(rr)
        row['assumptions_replayed'] = True
        rows.append(row)
    for rule, name, v, formula in real_mutants():
        hyps, bad = formula(ZB, v)
        s, r, secs = _check(hyps, bad, timeout_ms)
        row = dict(rule=rule, name=name, result=str(r), seconds=round(secs, 3))
        row.update(_replay_real(s.model(), v, formula) if r == z.sat else dict(replayed=False))
        mutants.append(row)
    for rule, name, hyps, bad, replay in fp_mutants():
        s, r, secs = _check(hyps, bad, timeout_ms)
        row = dict(rule=rule, name=name, result=str(r), seconds=round(secs, 3))
        row.update(replay(s.model()) if r == z.sat else dict(replayed=False))
        mutants.append(row)
    tight = leaf_tightness()
    covered = {row['rule'] for row in rows}
    killed = {m['rule'] for m in mutants if m['result'] == 'sat' and m['replayed']}
    passed = (all(row['result'] == 'unsat' and row['assumptions_result'] == 'sat' and row['assumptions_replayed']
                  for row in rows)
              and all(m['result'] == 'sat' and m['replayed'] for m in mutants)
              and covered <= killed and tight['error_over_u_m'] == '1')
    return dict(z3=z.get_version_string(), timeout_ms=timeout_ms, source_contracts=contracts,
                composition=composition, obligations=rows, mutants=mutants, leaf_tightness=tight,
                total_seconds=round(sum(r['seconds'] for r in rows + mutants), 3), obligations_passed=passed,
                proof_scope='Local rule lemmas of the PredicateCompiler filter; the rounding model (Higham 2.4/2.5) '
                            'and the structural induction over the expression graph are assumptions, see README.')


if __name__ == '__main__':
    import argparse
    import json
    parser = argparse.ArgumentParser()
    parser.add_argument('--timeout-ms', type=int, default=120000)
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    report = run(args.timeout_ms)
    with open(args.out, 'w') as fh:
        fh.write(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))
    raise SystemExit(0 if report['obligations_passed'] else 1)
