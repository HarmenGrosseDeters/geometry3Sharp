"""Z3 pilot 5: the termination of the step loops of MeshRoundingRepair (mesh_ops/MeshRoundingRepair.cs), the class of
#452: `fix_degenerate` walked `for (double s = s0; s <= bound; s *= 4)` with an s0 that underflowed to 0 for
components below about 2^-517, so s stayed 0 and the loop never ended. QA 2.0 T14.

The loops: fix_degenerate (factor 4, twice: the slide along the boundary and the move), try_separate (factor 4) and
fix_intersection (factor 16, twice: the slide and the moves), each starting at s0 > 0 or at ulp(bound) and ending at `s <= bound`.

What is checked (binary64, round to nearest even, subnormals included; every obligation is a counterexample query
that must return UNSAT with satisfiable assumptions; every mutant must return SAT with a witness replayed in plain
Python floats; UNKNOWN never counts):

  P1  progress: for s > 0 and a bound that is finite or NaN (the guard of maxLengthC), while the loop runs
      (s <= bound) the next value s * f is larger than s (the factors 4 and 16; subnormals included);
  P1b for every finite s > 0, s * f is +infinity or at least s + s;
  P2  the bound on the iterations (exact integers, not a Z3 result): s_k >= 2^k s0 or +infinity (P1b), and
      2^2100 * 2^-1074 > MaxValue, so the condition s <= bound is false after at most 2100 iterations for a finite bound;
  P3  the start: ulp(x) of MeshRoundingRepair (a bit manipulation) is a positive finite number for every finite
      x >= 0 (so ulp(bound) and 4 * ulp(largest) cannot be the stuck start 0), checked on the 64 bits;
      NaN gives NaN, for which `s <= bound` is false at once.

MUTANTS: a start of 0 (the code before #452): s * f == s and 0 <= bound, so the loop never ends (SAT); an infinite
bound: s reaches +infinity, `inf <= inf` holds and inf * f == inf (SAT). The finiteness of the bound is a property of
maxLengthC (a component's diagonal, bounded by the range check `OutsideRange`, 2^200) that is reviewed, not proved here.

The model is held to the source by `source_contracts` (the loops and the ulp function must be present as modelled).
"""
import json
import pathlib
import re
import struct
import time

import z3 as z

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = 'mesh_ops/MeshRoundingRepair.cs'
F64 = z.Float64()
RNE = z.RNE()
MAX_DOUBLE = struct.unpack('<d', struct.pack('<Q', 0x7FEFFFFFFFFFFFFF))[0]
MIN_SUBNORMAL = struct.unpack('<d', struct.pack('<Q', 1))[0]


def _norm(s):
    return ''.join(s.split())


def source_contracts(text=None):
    """The loops and the ulp function the lemmas model, read from the source; fails on drift."""
    if text is None:
        text = (ROOT / SOURCE).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    n = _norm(re.sub(r'//[^\n]*', '', text))      # without line comments
    counts = {
        'for(doubles=s0;s<=bound;s*=4)': 3,
        'for(doubles=ulp;s<=bound;s*=16)': 2,
        'if(!(s0>0)){': 1,
        'if(!(s0>0))return;': 1,
        'if(!(ulp>0))ulp=MeshRoundingRepair.ulp(bound);': 1,
        'if(!(ml<double.PositiveInfinity))ml=double.NaN;maxLengthC[c]=ml;': 1,      # the bound is finite or NaN, never +infinity
        'doubles0=2*h;': 1,
        's0=largest>0?4*ulp(largest):ulp(bound);': 2,
        'if(!(s0>0&&s0<double.PositiveInfinity))returnfalse;': 1,
        'staticdoubleulp(doublex){if(!(x<double.PositiveInfinity))returndouble.NaN;longbits=BitConverter.DoubleToInt64Bits(x);'
        'inte=(int)((bits>>52)&0x7FF);returne>52?BitConverter.Int64BitsToDouble((long)(e-52)<<52):e>0?'
        'BitConverter.Int64BitsToDouble(1L<<(e-1)):double.Epsilon;}': 1,
    }
    for pattern, expected in counts.items():
        found = n.count(pattern)
        if found != expected:
            raise ValueError(f'MeshRoundingRepair: {pattern[:70]}... occurs {found} times, the model expects {expected}')
    return dict(loops_factor_4=3, loops_factor_16=2, start_guard='!(s0 > 0)', ulp='bit manipulation')


def _check(hyps, bad, timeout_ms):
    s = z.Solver()
    s.set(timeout=timeout_ms)
    s.add(*hyps)
    s.add(bad)
    t0 = time.monotonic()
    r = s.check()
    return s, r, time.monotonic() - t0


def _row(name, hyps, bad, timeout_ms, expect, extra=None):
    s, r, secs = _check(hyps, bad, timeout_ms)
    out = dict(name=name, result=str(r), expected=expect, seconds=round(secs, 3), ok=str(r) == expect)
    if r == z.unknown:
        out['reason'] = s.reason_unknown()
    sv = z.Solver()
    sv.set(timeout=timeout_ms)
    sv.add(*hyps)
    out['assumptions_result'] = str(sv.check())
    if extra and r == z.sat:
        out['witness'] = extra(s.model())
    return out


def _value(model, var):
    bits = model.eval(z.fpToIEEEBV(var), model_completion=True).as_long()
    return struct.unpack('<d', struct.pack('<Q', bits))[0]


def _ulp_bits(bits):
    """MeshRoundingRepair.ulp on the 64 bits of a non-negative finite x (the result's bits)"""
    e = z.Extract(10, 0, z.LShR(bits, 52))                       # ((bits >> 52) & 0x7FF), 11 bits
    e64 = z.ZeroExt(53, e)
    normal = z.Concat(z.BitVecVal(0, 1), z.Extract(10, 0, e64 - 52), z.BitVecVal(0, 52))       # (long)(e - 52) << 52
    sub = z.If(e64 > 0, z.LShR(z.BitVecVal(1, 64), 0) << z.ZeroExt(53, z.Extract(10, 0, e64 - 1)), z.BitVecVal(1, 64))
    return z.If(e64 > 52, normal, sub)


def _f(x):
    return z.FPVal(x, F64)


def run(timeout_ms=120000):
    contracts = source_contracts()
    rows = []
    s, bound = z.FPs('s bound', F64)
    zero = z.FPVal(0.0, F64)
    inf = z.fpPlusInfinity(F64)
    # the state of a loop `for (s = s0; s <= bound; s *= f)`: s is a start > 0 or a later value, bound is finite or NaN
    # (the guard of maxLengthC, a source contract); NaN entails `s <= bound` false, a loop that is not entered
    start_ok = [z.Not(z.fpIsNaN(s)), z.fpGT(s, zero)]
    bound_ok = [z.Not(z.fpIsInf(bound)), z.Not(z.fpIsNaN(bound))]
    for f in (4, 16):
        r = z.fpMul(RNE, s, _f(float(f)))
        in_loop = [z.fpLEQ(s, bound)]
        # P1: while the loop runs (s <= bound, so s is finite), the next value is larger: s * f > s
        rows.append(_row(f'P1_factor_{f}_advances_while_the_loop_runs', start_ok + bound_ok + in_loop, z.Not(z.fpGT(r, s)), timeout_ms, 'unsat'))
        # P1b: and at least twice as large or infinite (what P2 uses), for every finite s > 0
        twice = z.fpGEQ(r, z.fpAdd(RNE, s, s))
        rows.append(_row(f'P1b_factor_{f}_gives_at_least_twice_s_or_infinity', start_ok + [z.Not(z.fpIsInf(s))], z.Not(z.Or(z.fpIsInf(r), twice)), timeout_ms, 'unsat'))

    # P2: exact integers, not a Z3 result: s_k >= 2^k * s0 (P1b) and 2^2100 * 2^-1074 > MaxValue, so the loop is over after 2100 steps
    from fractions import Fraction
    k = 2100
    ok = Fraction(2) ** k * Fraction(MIN_SUBNORMAL) > Fraction(MAX_DOUBLE)
    rows.append(dict(name=f'P2_doubling_from_the_smallest_subnormal_passes_MaxValue_within_{k}_steps_(exact_integers,_not_Z3)',
                     result='holds' if ok else 'fails', expected='holds', seconds=0.0, ok=ok, assumptions_result='sat'))

    # P3: ulp(x) is positive and finite for every finite x >= 0
    x = z.FP('x', F64)
    xb = z.fpToIEEEBV(x)
    x_ok = [z.Not(z.fpIsNaN(x)), z.Not(z.fpIsInf(x)), z.Not(z.fpIsNegative(x))]
    u = z.fpToFP(_ulp_bits(xb), F64)
    rows.append(_row('P3_ulp_of_a_finite_nonnegative_number_is_positive_and_finite', x_ok,
                     z.Not(z.And(z.fpGT(u, zero), z.Not(z.fpIsInf(u)), z.Not(z.fpIsNaN(u)))), timeout_ms, 'unsat'))

    # MUTANTS: the same P1 query with one hypothesis dropped must be SAT, with a witness replayed in plain floats
    r4 = z.fpMul(RNE, s, _f(4.0))
    def replay(model):
        sv, bv = _value(model, s), _value(model, bound)
        return dict(s=repr(sv), bound=repr(bv), next=repr(sv * 4), stuck=bool(sv <= bv and not (sv * 4 > sv)))
    # (1) the start may be 0 (the code before #452): s * 4 == s and 0 <= bound
    rows.append(_row('mutant_P1_with_a_start_of_0_(the_code_before_#452)', [z.Not(z.fpIsNaN(s)), z.fpGEQ(s, zero)] + bound_ok + [z.fpLEQ(s, bound)],
                     z.Not(z.fpGT(r4, s)), timeout_ms, 'sat', replay))
    # (2) the bound may be +infinity (no guard on maxLengthC): s reaches +infinity, inf <= inf and inf * 4 == inf
    rows.append(_row('mutant_P1_with_an_infinite_bound_(no_guard_on_maxLengthC)', start_ok + [z.Not(z.fpIsNaN(bound)), z.fpLEQ(s, bound)],
                     z.Not(z.fpGT(r4, s)), timeout_ms, 'sat', replay))
    passed = all(r['ok'] and r['assumptions_result'] == 'sat' for r in rows)
    return dict(contracts=contracts, obligations=rows, passed=passed)


if __name__ == '__main__':
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument('--out')
    ap.add_argument('--timeout-ms', type=int, default=120000)
    args = ap.parse_args()
    report = run(args.timeout_ms)
    text = json.dumps(report, indent=2)
    if args.out:
        pathlib.Path(args.out).write_text(text + '\n')
    print(text)
    raise SystemExit(0 if report['passed'] else 1)
