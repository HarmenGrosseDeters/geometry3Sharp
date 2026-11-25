"""Z3 lemma: the range of the expansion arithmetic, Expansion.InRange (math/ExactPredicates.cs), against the needs of
AdaptivePredicates (math/AdaptivePredicates.cs). docs/qa/exact-bound-inventory.md, gap G2 (hand mutant H6).

Claim (the comment of AdaptivePredicates): if every coordinate is 0 or has a magnitude in [2^-m, 2^M] (m = 100,
M = 100 in the source), every value that the expansion arithmetic of a predicate of degree d computes (differences,
their rounding errors, Dekker's splits, the products and their rounding errors, the sums and their tails) is 0 or a
normal double below the overflow threshold, so Shewchuk's error-free transformations are exact.

The argument, each step a lemma (UNSAT counterexample query, its assumptions SAT, a controlled mutant SAT and replayed
outside Z3 in exact arithmetic or Python's IEEE doubles):

  I   grid_input     a binary64 x with |x| >= 2^-m is a multiple of 2^-(m + 52)                  (FP, bit-precise)
  C   grid_*         in the scaled frame (binary64 precision, 15-bit exponent) RNE sums, differences and products of
                     integers are integers; TwoSum, Dekker's split and TwoProduct are chains of these operations, so
                     their tails are integers too (FP, bit-precise)
  B   bridge         a binary64 operation whose exact result is 0 or normal and finite returns that result rounded to
                     53 bits, as the wide frame does (the IEEE 754 definition; stated, not queried: Z3 4.15 does not
                     decide the frame-equality query for + and * within 300 s; with the condition on the binary64
                     result instead of the exact one it is false: an exact product just below 2^-1022 rounds up)
  U   no_underflow   for each predicate degree d: a nonzero value of degree <= d on the grid 2^-d(m+52) is >= 2^-1022,
                     i.e. d (m + 52) <= 1022 (integer arithmetic over the source constants)
  O   no_overflow    every intermediate is below 2^(d (M + 1) + GROWTH): d (M + 1) + GROWTH <= 1023 (integer)

The degree d of a predicate's stage D (2, 3, 4) and the growth bound GROWTH are reviewed statements (README.md): the
source contract pins the predicates, the range check and its callers, so a new predicate, a changed range or an
unguarded call fails here first. Not proved: Shewchuk's theorems themselves (exactness of his routines without
underflow and overflow), and that the degree of every intermediate is at most d (read from the code).
"""
import hashlib
import math
import re
import struct
import sys
import time
from fractions import Fraction

import z3 as z

import pilot

sys.path.insert(0, str(pilot.ROOT / 'tools' / 'ErrorBoundAudit'))
import range_callers    # noqa: E402

ROOT = pilot.ROOT
PREDICATES = 'math/ExactPredicates.cs'
ADAPTIVE = 'math/AdaptivePredicates.cs'

F64 = z.Float64()
XF = z.FPSort(15, 53)
RNE = z.RNE()

# the degree of the exact determinant of each predicate of AdaptivePredicates (reviewed: orient2d is a 2x2
# determinant of differences, orient3d a 3x3, incircle the lifted 3x3, of degree 4)
DEGREES = {'orient2d': 2, 'orient3d': 3, 'incircle': 4}
# the largest intermediate of degree d is below 2^(d (M + 1) + GROWTH) for coordinates below 2^M (differences below
# 2^(M + 1)): incircle sums three products of a lift (two squares) and a minor (two products), < 12 2^(4 (M + 1)) < 2^(4 (M + 1) + 4);
# the expansions' sums at most double that; the splitter (2^27 + 1 < 2^28) multiplies an operand: + 28 (AP-1 of
# tools/ErrorBoundAudit uses the same count). Reviewed, conservative.
GROWTH = 4 + 1 + 28
NORMAL_MIN_EXPONENT = -1022
MAX_EXPONENT = 1023

# the reviewed text of AdaptivePredicates (whitespace removed): the degrees and GROWTH above were read from it. Any change
# of the file fails the contract until they are reviewed again and this hash is updated (sha256 of the normalized text)
ADAPTIVE_SHA256 = 'eaa19935f97821e7aa0f4db5048c07d3b6cf3d48281cc20907de78e702d9df32'

# the range check of every argument (each overload checks all its arguments)
IN_RANGE_OVERLOADS = """public static bool InRange(double a, double b, double c, double d, double e, double f)
        {
            return in_range(a) && in_range(b) && in_range(c) && in_range(d) && in_range(e) && in_range(f);
        }
        public static bool InRange(double a, double b, double c, double d, double e, double f, double g, double h)
        {
            return InRange(a, b, c, d, e, f) && in_range(g) && in_range(h);
        }
        public static bool InRange(double a, double b, double c, double d, double e, double f, double g, double h, double i, double j, double k, double l)
        {
            return InRange(a, b, c, d, e, f) && InRange(g, h, i, j, k, l);
        }"""


def _normalize(s):
    return re.sub(r'\s+', '', s)


def _power_of_two(text, name):
    """the exponent e of a 'const double name = <literal>;' that is exactly 2^e"""
    m = re.search(r'const double %s = ([0-9.eE+-]+);' % name, text)
    if m is None:
        raise ValueError(f'{PREDICATES}: {name} not found')
    q = Fraction(float(m.group(1)))
    e = q.numerator.bit_length() - 1 if q >= 1 else -(q.denominator.bit_length() - 1)
    if q != Fraction(2)**e:
        raise ValueError(f'{PREDICATES}: {name} = {m.group(1)} is not a power of two')
    return e


IN_RANGE = """static bool in_range(double x)
        {
            double ax = Math.Abs(x);
            if (double.IsNaN(x))
                throw new ArgumentException("ExactPredicates: coordinates must be finite");
            return ax == 0 || (ax >= MinMagnitude && ax <= MaxMagnitude);
        }"""


def source_contract(root=ROOT):
    """the constants the lemmas use, read from the source; raises on a change that the model does not describe"""
    pred = (root / PREDICATES).read_text(encoding='utf-8')
    adaptive = (root / ADAPTIVE).read_text(encoding='utf-8')
    if _normalize(IN_RANGE) not in _normalize(pred) or _normalize(IN_RANGE_OVERLOADS) not in _normalize(pred):
        raise ValueError(f'{PREDICATES}: in_range or InRange changed: check it against tools/Z3Pilot/adaptive_range.py')
    if hashlib.sha256(_normalize(adaptive).encode('utf-8')).hexdigest() != ADAPTIVE_SHA256:
        raise ValueError(f'{ADAPTIVE} changed: review DEGREES and GROWTH of tools/Z3Pilot/adaptive_range.py, then update ADAPTIVE_SHA256')
    if 'const double Splitter = 134217729.0;' not in pred:
        raise ValueError(f'{PREDICATES}: the splitter changed (GROWTH counts 2^27 + 1)')
    lo, hi = _power_of_two(pred, 'MinMagnitude'), _power_of_two(pred, 'MaxMagnitude')
    # the public predicates of AdaptivePredicates: exactly those with a reviewed degree
    found = {n.lower() for n in re.findall(r'\n\s*public static int (\w+)\(double', adaptive)}
    if found != set(DEGREES):
        raise ValueError(f'{ADAPTIVE}: predicates {sorted(found)}, the model knows {sorted(DEGREES)}: review the degrees')
    # every call of a public stage in the library checks the range of all its arguments first (also checked on every
    # library change by tools/ErrorBoundAudit/test_range_callers.py)
    calls = range_callers.unguarded_calls(root)
    if calls != range_callers.KNOWN_UNGUARDED:
        raise ValueError(f'unguarded calls of AdaptivePredicates {calls}, known {range_callers.KNOWN_UNGUARDED}')
    return dict(min_exponent=lo, max_exponent=hi, degrees=dict(DEGREES), growth=GROWTH, unguarded=calls)


# ---------------------------------------------------------------------------------------------------------------

def _fp(name, sort=F64):
    return z.FP(name, sort)


def _val(x, sort=F64):
    return z.FPVal(x, sort)


def _finite(*xs):
    return [c for x in xs for c in (z.Not(z.fpIsInf(x)), z.Not(z.fpIsNaN(x)))]


def _integral(x):
    return z.fpEQ(z.fpRoundToIntegral(RNE, x), x)


def _ext(t):
    return z.fpFPToFP(RNE, t, XF)


def _double(model, t):
    bits = model.eval(z.fpToIEEEBV(t), model_completion=True).as_long()
    return struct.unpack('>d', bits.to_bytes(8, 'big'))[0]


def fp_obligations(c):
    """(step, name, assumptions, bad, witness): bad must be UNSAT under the assumptions, the witness SAT"""
    m = -c['min_exponent']
    x = _fp('x')
    # I: the scale 2^(m + 52) is exact in the wide frame (15-bit exponent)
    scaled = z.fpMul(RNE, _ext(x), _val(2.0**(m + 52), XF))
    yield ('I', 'grid_input', _finite(x) + [z.fpGEQ(z.fpAbs(x), _val(2.0**-m))], z.Not(_integral(scaled)),
           z.fpEQ(x, _val(-(2.0**-m) * (1 + 2.0**-52))))
    gx, gy = _fp('gx', XF), _fp('gy', XF)
    grid = _finite(gx, gy) + [_integral(gx), _integral(gy)]
    w = z.And(z.fpEQ(gx, _val(2.0**60 + 2.0**8, XF)), z.fpEQ(gy, _val(-3.0, XF)))
    for op, nm in [(z.fpAdd, 'grid_sum'), (z.fpSub, 'grid_difference'), (z.fpMul, 'grid_product')]:
        r = op(RNE, gx, gy)
        yield 'C', nm, grid + [z.Not(z.fpIsInf(r))], z.Not(_integral(r)), w


def int_obligations(c):
    """(step, name, assumptions, bad, witness) over integers: the exponents of the values of degree d"""
    m, M = -c['min_exponent'], c['max_exponent']
    for pred, d in sorted(c['degrees'].items()):
        e = z.Int('e')            # a nonzero value of degree d on the grid 2^-d(m+52) has |v| >= 2^e
        yield ('U', f'no_underflow_{pred}', [e == -d * (m + 52)], e < NORMAL_MIN_EXPONENT, None)
        t = z.Int('t')            # every intermediate is below 2^t
        yield ('O', f'no_overflow_{pred}', [t == d * (M + 1) + c['growth']], t > MAX_EXPONENT, None)


def _check(assumptions, bad, timeout_ms):
    s = z.Solver()
    s.set(timeout=timeout_ms)
    s.add(*assumptions)
    s.add(bad)
    t0 = time.monotonic()
    r = s.check()
    return s, r, time.monotonic() - t0


def mutants(c, timeout_ms):
    """controlled mutants: each must be SAT, and the replay shows the failure outside Z3"""
    out = []
    # H6: MinMagnitude 2^-100 to 2^-250 (the hand mutant of #1572); the grid unit of a double >= 2^-250 is 2^-302
    m = 250
    e = z.Int('e')
    s, r, secs = _check([e == -4 * (m + 52)], e < NORMAL_MIN_EXPONENT, timeout_ms)
    unit = 2.0**-(m + 52)
    p = ((unit * unit) * unit) * unit
    out.append(dict(name='H6_min_magnitude_2^-250_incircle', result=str(r), seconds=round(secs, 3),
                    exponent=s.model()[e].as_long() if r == z.sat else None, product=p.hex(),
                    replayed=r == z.sat and p != Fraction(unit)**4,
                    note='degree-4 product of the grid unit 2^-302 rounds to %s in binary64; the .NET replay is '
                         'ExactPredicatesRangeEdgeTests (InCircle 1 for exactly cocircular points at 2^-220)' % p.hex()))
    # MaxMagnitude 2^100 to 2^250: the proof fails (no wrong sign was found below about 2^254)
    M = 250
    t = z.Int('t')
    s, r, secs = _check([t == 4 * (M + 1) + c['growth']], t > MAX_EXPONENT, timeout_ms)
    big = 2.0**(M + 1)
    q = big * big * big * big * 12
    out.append(dict(name='max_magnitude_2^250_incircle', result=str(r), seconds=round(secs, 3), product=repr(q),
                    replayed=r == z.sat and math.isinf(q * 2.0**28)))
    # I with one bit less: a double >= 2^-m is not always a multiple of 2^-(m + 51)
    mm = -c['min_exponent']
    x = _fp('x')
    scaled = z.fpMul(RNE, _ext(x), _val(2.0**(mm + 51), XF))
    s, r, secs = _check(_finite(x) + [z.fpGEQ(z.fpAbs(x), _val(2.0**-mm))], z.Not(_integral(scaled)), timeout_ms)
    row = dict(name='grid_input_one_bit_less', result=str(r), seconds=round(secs, 3))
    if r == z.sat:
        v = _double(s.model(), x)
        k = Fraction(v) * 2**(mm + 51)
        row.update(x=v.hex(), replayed=k.denominator != 1)
    else:
        row['replayed'] = False
    out.append(row)
    # C for an operation the expansions do not use: a quotient of integers is not always an integer
    gx, gy = _fp('gx', XF), _fp('gy', XF)
    q = z.fpDiv(RNE, gx, gy)
    s_, r, secs = _check(_finite(gx, gy) + [_integral(gx), _integral(gy), z.Not(z.fpIsZero(gy)), z.Not(z.fpIsInf(q)),
                          z.fpLEQ(z.fpAbs(gx), _val(2.0**20, XF)), z.fpLEQ(z.fpAbs(gy), _val(2.0**20, XF))],
                         z.Not(_integral(q)), timeout_ms)
    row = dict(name='grid_quotient', result=str(r), seconds=round(secs, 3))
    if r == z.sat:
        vx, vy = (_double(s_.model(), z.fpFPToFP(RNE, t, F64)) for t in (gx, gy))
        row.update(gx=repr(vx), gy=repr(vy), replayed=vy != 0 and (Fraction(vx) / Fraction(vy)).denominator != 1)
    else:
        row['replayed'] = False
    out.append(row)
    return out


def run(timeout_ms=120000):
    c = source_contract()
    rows = []
    for step, name, hyps, bad, witness in list(fp_obligations(c)) + list(int_obligations(c)):
        s, r, secs = _check(hyps, bad, timeout_ms)
        row = dict(step=step, name=name, result=str(r), seconds=round(secs, 3))
        if r == z.unknown:
            row['reason'] = s.reason_unknown()
        _, rr, _ = _check(hyps, witness if witness is not None else z.BoolVal(True), timeout_ms)
        row['assumptions_result'] = str(rr)
        rows.append(row)
    muts = mutants(c, timeout_ms)
    m, M = -c['min_exponent'], c['max_exponent']
    d = max(c['degrees'].values())
    margins = dict(smallest_min_exponent=-((-NORMAL_MIN_EXPONENT) // d - 52),
                   largest_max_exponent=(MAX_EXPONENT - c['growth']) // d - 1)
    passed = (all(r['result'] == 'unsat' and r['assumptions_result'] == 'sat' for r in rows)
              and all(x['result'] == 'sat' and x['replayed'] for x in muts))
    return dict(z3=z.get_version_string(), timeout_ms=timeout_ms, contract=c, range=dict(min=f'2^-{m}', max=f'2^{M}'),
                margins=margins, obligations=rows, mutants=muts, passed=passed,
                total_seconds=round(sum(r['seconds'] for r in rows + muts), 3),
                proof_scope='The range gives every intermediate of degree <= d a normal, finite value; Shewchuk\'s '
                            'theorems (exactness without underflow or overflow) and the degree of each intermediate '
                            'are assumptions, see README.md.')


if __name__ == '__main__':
    import argparse
    import json
    parser = argparse.ArgumentParser()
    parser.add_argument('--timeout-ms', type=int, default=120000)
    parser.add_argument('--out')
    args = parser.parse_args()
    report = run(args.timeout_ms)
    text = json.dumps(report, indent=2)
    if args.out:
        with open(args.out, 'w') as fh:
            fh.write(text + '\n')
    print(text)
    raise SystemExit(0 if report['passed'] else 1)
