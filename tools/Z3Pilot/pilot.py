"""Bounded binary64 verification pilot. See README for the mathematical bridges.
No SAT/UNSAT conclusion is inferred from UNKNOWN. No production code is generated.
"""
import argparse
import hashlib
import json
import pathlib
import re
import struct
import time

import z3 as z

ROOT = pathlib.Path(__file__).resolve().parents[2]
F = z.Float64()
RN = z.RNE()
HALF = z.FPVal(0.5, F)
LIMIT = z.FPVal(2**52, F)


def source_contracts():
    """Fail on code drift instead of silently proving a stale transcription."""
    path = ROOT / 'comp_geom/SnapRounding2.cs'
    text = path.read_text(encoding='utf-8-sig')
    pixel = re.search(r'public long Pixel\(double x\).*?\n        }', text, re.S).group()
    expected = '''public long Pixel(double x) {
        if (W > 0) { double r = x / W + 0.5, f = Math.Floor(r);
        if (f < r) return (long)f; } return PixelExact(x); }'''
    normalize = lambda s: re.sub(r'\s+', '', s)
    if normalize(pixel) != normalize(expected):
        raise ValueError('SnapGrid.Pixel changed; review the SMT model')
    path2 = ROOT / 'benchmarks/Meshing2Benchmark/Oracle/Oracle.cs'
    text2 = path2.read_text(encoding='utf-8-sig')
    def method(text, signature):
        start = text.index(signature)
        opening = text.index('{', start)
        depth = 0
        for end in range(opening, len(text)):
            depth += (text[end] == '{') - (text[end] == '}')
            if depth == 0:
                return text[start:end+1]
        raise ValueError('Unterminated source method')
    query = method(text2, 'internal static bool DistanceQueryBox(')
    query_expected = """internal static bool DistanceQueryBox(Pt p, Q d2, out double xl, out double xh, out double yl, out double yh) {
        xl = xh = yl = yh = 0;
        if (d2.Sign < 0 || !d2.Bounds(out _, out double d2hi) || !p.Bounds(false, out double pxl, out double pxh) || !p.Bounds(true, out double pyl, out double pyh)) return false;
        double reach = Math.Sqrt(Math.Max(0, d2hi)) * (1 + 1e-12) + 1e-300;
        if (double.IsInfinity(reach) || double.IsNaN(reach)) return false;
        xl = pxl - reach; xh = pxh + reach; yl = pyl - reach; yh = pyh + reach;
        return !double.IsInfinity(xl) && !double.IsInfinity(xh) && !double.IsInfinity(yl) && !double.IsInfinity(yh); }"""
    if normalize(query) != normalize(query_expected):
        raise ValueError('DistanceQueryBox changed; review the SMT model')
    exact = (ROOT / 'benchmarks/Meshing2Benchmark/Oracle/Exact.cs').read_text(encoding='utf-8-sig')
    bounds = method(exact, 'public static bool Bounds(BigInteger')
    bounds_expected = """public static bool Bounds(BigInteger n, BigInteger d, out double lo, out double hi) {
        double dn = (double)n, dd = (double)d;
        lo = hi = 0;
        if (double.IsInfinity(dn) || double.IsInfinity(dd) || !(dd > 0)) return false;
        double v = dn / dd, pad = Math.Abs(v) * 1e-13 + 1e-300;
        if (double.IsInfinity(v) || double.IsNaN(v)) return false;
        lo = v - pad; hi = v + pad;
        return true; }"""
    if normalize(bounds) != normalize(bounds_expected):
        raise ValueError('Q.Bounds changed; review the SMT model')
    return {str(path.relative_to(ROOT)): hashlib.sha256(pixel.encode()).hexdigest(),
            str(path2.relative_to(ROOT)): hashlib.sha256(query.encode()).hexdigest(),
            'benchmarks/Meshing2Benchmark/Oracle/Exact.cs': hashlib.sha256(bounds.encode()).hexdigest()}


def obligations():
    # u over-approximates RN(x/W): all binary64 values, including subnormals,
    # infinities and NaNs. The fast-branch guard excludes non-finite r/f.
    u = z.FP('rounded_quotient', F)
    r = z.fpAdd(RN, u, HALF)
    f = z.fpRoundToIntegral(z.RTN(), r)
    accepted = z.fpLT(f, r)
    yield 'pixel_index_range', [accepted], z.Or(z.fpLT(f, z.fpNeg(LIMIT)), z.fpGEQ(f, LIMIT))
    yield 'pixel_lower_regular', [accepted, z.fpGT(f, z.fpNeg(LIMIT))], z.fpLEQ(u, z.fpSub(RN, f, HALF))
    yield 'pixel_lower_endpoint', [accepted, z.fpEQ(f, z.fpNeg(LIMIT))], z.fpLEQ(u, z.FPVal(-(2**52)-1, F))
    yield 'pixel_upper', [accepted], z.fpGEQ(u, z.fpAdd(RN, f, HALF))
    # Actual surviving mutant: remove DistanceQueryBox's final +1e-300 (#376).
    # For nonnegative d2, Q.Bounds has v >= 0; it pads even an underflowed v.
    v = z.FP('bounds_rounded_ratio', F)
    tiny = z.FPVal(1e-300, F)
    hi = z.fpAdd(RN, v, z.fpAdd(RN, z.fpMul(RN, v, z.FPVal(1e-13, F)), tiny))
    yield 'query_bounds_lower', [z.fpGEQ(v, z.FPVal(0, F)), z.Not(z.fpIsInf(v))], z.fpLT(hi, tiny)
    # By monotonic sqrt and multiplication by RN(1+1e-12)>1,
    # reach-before-add >= 1e-151 (a conservative lower bound, see README).
    b = z.FP('reach_before_absolute_pad', F)
    yield 'query_absolute_pad_equivalent', [z.fpGEQ(b, z.FPVal(1e-151, F))], z.Not(z.fpEQ(z.fpAdd(RN, b, tiny), b))


def check(name, assumptions, bad, timeout_ms):
    solver = z.Solver()
    solver.set(timeout=timeout_ms)
    solver.add(*assumptions, bad)
    start = time.monotonic()
    result = solver.check()
    row = dict(name=name, result=str(result), seconds=time.monotonic()-start)
    if result == z.unknown:
        row['reason'] = solver.reason_unknown()
    return solver, result, row


def guard_mutant(timeout_ms):
    # Mutant f <= r incorrectly accepts an integral rounded sum just below a
    # half-integer boundary. W=1, so no real-arithmetic division is needed.
    x = z.FP('mutant_x', F)
    r = z.fpAdd(RN, x, HALF)
    f = z.fpRoundToIntegral(z.RTN(), r)
    solver, result, row = check('pixel_guard_le_mutant', [z.fpGT(x, z.FPVal(.4, F)), z.fpLT(x, HALF)],
                                z.And(z.fpEQ(f, r), z.fpEQ(r, z.FPVal(1, F))), timeout_ms)
    if result == z.sat:
        bits = solver.model().eval(z.fpToIEEEBV(x)).as_long()
        value = struct.unpack('>d', bits.to_bytes(8, 'big'))[0]
        # Independent exact-rational replay using the actual binary64 input.
        from fractions import Fraction
        exact = Fraction.from_float(value) + Fraction(1, 2)
        row.update(x_bits=bits, x_hex=value.hex(), width=1, exact_pixel=exact.numerator//exact.denominator,
                   mutant_pixel=int(value+.5))
        assert row['exact_pixel'] == 0 and row['mutant_pixel'] == 1
    return result, row


def direct_query(timeout_ms):
    x, w = z.FPs('direct_x direct_w', F)
    r = z.fpAdd(RN, z.fpDiv(RN, x, w), HALF)
    f = z.fpRoundToIntegral(z.RTN(), r)
    X, W, B = z.fpToReal(x), z.fpToReal(w), z.fpToReal(f)
    solver, result, row = check('monolithic_pixel', [z.Not(z.fpIsNaN(x)), z.Not(z.fpIsInf(x)),
                       z.fpGT(w, z.FPVal(0,F)), z.Not(z.fpIsInf(w)), z.fpLT(f,r)],
                      z.Or(X < (B-z.RealVal('1/2'))*W, X >= (B+z.RealVal('1/2'))*W), timeout_ms)
    if result == z.sat:
        model = solver.model()
        row['input_bits'] = {name: model.eval(z.fpToIEEEBV(value)).as_long() for name, value in [('x', x), ('w', w)]}
        row['model'] = str(model)
    return row


def run(timeout_ms=20000, direct=False):
    contracts = source_contracts()
    rows = []
    for name, assumptions, bad in obligations():
        _, result, row = check(name, assumptions, bad, timeout_ms)
        witnesses = {
            'pixel_index_range': z.fpEQ(z.FP('rounded_quotient', F), z.FPVal(0, F)),
            'pixel_lower_regular': z.fpEQ(z.FP('rounded_quotient', F), z.FPVal(0, F)),
            'pixel_lower_endpoint': z.fpEQ(z.FP('rounded_quotient', F), z.fpNeg(LIMIT)),
            'pixel_upper': z.fpEQ(z.FP('rounded_quotient', F), z.FPVal(0, F)),
            'query_bounds_lower': z.fpEQ(z.FP('bounds_rounded_ratio', F), z.FPVal(0, F)),
            'query_absolute_pad_equivalent': z.fpEQ(z.FP('reach_before_absolute_pad', F), z.FPVal(1, F)),
        }
        # Fixed witnesses make satisfiability cheap without restricting the
        # universal counterexample query above.
        witness = witnesses.get(name, z.BoolVal(True))
        _, reachable, reach_row = check(name + '_nonvacuity', [], z.And(*assumptions, witness), timeout_ms)
        row['assumptions_result'] = str(reachable)
        if reachable == z.unknown:
            row['assumptions_reason'] = reach_row['reason']
        rows.append(row)
    result, witness = guard_mutant(timeout_ms)
    passed = all(row['result'] == 'unsat' and row['assumptions_result'] == 'sat' for row in rows) and result == z.sat
    report = dict(z3=z.get_version_string(), timeout_ms=timeout_ms, source_contracts=contracts,
                  obligations=rows, witness=witness, obligations_passed=passed,
                  proof_scope='Modular binary64 lemmas plus the IEEE rounding/representability bridges in README; not a monolithic solver proof.')
    if direct:
        report['direct_attempt'] = direct_query(timeout_ms)
        if report['direct_attempt']['result'] == 'sat':
            report['obligations_passed'] = False
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--timeout-ms', type=int, default=20000)
    parser.add_argument('--direct', action='store_true')
    parser.add_argument('--out', type=pathlib.Path, required=True)
    args = parser.parse_args()
    if args.timeout_ms <= 0:
        parser.error('timeout-ms must be positive')
    report = run(args.timeout_ms, args.direct)
    args.out.write_text(json.dumps(report, indent=2)+'\n')
    print(json.dumps(report, indent=2))
    raise SystemExit(0 if report['obligations_passed'] else 1)
