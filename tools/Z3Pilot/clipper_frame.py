"""Z3 pilot 6: the frame of the Clipper2 adapter (addons/geometry3Sharp.Clipper2/ClipperFrame.cs, `ForBounds`). QA 2.0 T10.

`ForBounds` picks the scale 2^(bits - e) with e = CeilExponent(extent), so that the largest distance of an input point
from the origin, scaled, fits into 2^bits and the integer grid coordinates stay below MaxIntegerCoordinate = 2^52
(exact in a double, and in the range Clipper2 accepts). What is checked (Z3 on integers, bit vectors and binary64 round
to nearest even; every obligation is a counterexample query that must return UNSAT with satisfiable assumptions;
every mutant SAT with a witness replayed in plain Python; UNKNOWN never counts):

  E1  CeilExponent on the 64 bits of a positive normal x (the bit operations of the code, x = (2^52 + m) 2^(biased - 1075)
      by the IEEE definition): with the returned e, 2^(e-1) < x <= 2^e (the smallest power of two that is >= x), so
      extent * 2^(bits - e) lies in (2^(bits-1), 2^bits];
  E2  the subnormal branch: the loop `while (m > 1) { m = (m + 1) >> 1; ++e }` over x = m 2^-1074 (1 <= m < 2^52)
      ends within 52 iterations (unrolled) with 2^(c-1) < m <= 2^c, c the number of iterations, so the same holds;
  E3  the guard sequence of ForBounds, exhaustively over the ints (all precisionBits in [8, 50] and e in [-1074, 1024]):
      PowerOfTwo(n) is exactly 2^n for -1022 <= n <= 1023 (all 2046 doubles), +inf above, 0 below; when the frame is
      accepted, scale * 2^e = 2^bits <= 2^50 < 2^52 and 1 / scale is finite, so a coordinate within the extent maps to an
      integer of magnitude at most 2^bits (the product is at most 2^bits by monotone rounding: reviewed, a
      generic bit-blasted proof of the monotonicity of a rounded subtraction or product does not finish in Z3);
  E4  the extent never falls short of its parts: for the two distances d1, d2 and the margin m >= 0 (binary64), the
      sum max(d1, d2) + m >= max(d1, d2) (no rounding of the margin loses the distance).

MUTANTS: a ceiling that returns the exponent for every mantissa (a floor; extent > 2^e, the scaled extent exceeds 2^bits:
SAT with a witness mantissa), and the E3 enumeration with precisionBits up to 53 (the first frame whose scale * 2^e = 2^bits
is above 2^52, an integer coordinate that is not exact and above MaxIntegerCoordinate, is found and replayed).
`source_contracts` holds the model to the source (the lines it reads, MaxPrecisionBits, MaxIntegerCoordinate).
"""
import json
import pathlib
import re
import struct
import time
from fractions import Fraction

import z3 as z

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = 'addons/geometry3Sharp.Clipper2/ClipperFrame.cs'
F64 = z.Float64()
RNE = z.RNE()


def _norm(s):
    return ''.join(re.sub(r'//[^\n]*', '', s).split())


def source_contracts(text=None):
    if text is None:
        text = (ROOT / SOURCE).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    n = _norm(text)
    required = [
        'publicconstintMinPrecisionBits=8;',
        'publicconstintMaxPrecisionBits=50;',
        'publicconstdoubleMaxIntegerCoordinate=4503599627370496.0;',
        'if(precisionBits<MinPrecisionBits||precisionBits>MaxPrecisionBits)returnfalse;',
        'doubleextent=Math.Max(Math.Max(origin.x-bounds.Min.x,bounds.Max.x-origin.x),Math.Max(origin.y-bounds.Min.y,bounds.Max.y-origin.y))+margin;',
        'inte=CeilExponent(extent);',
        'doublescale=PowerOfTwo(precisionBits-e);',
        'if(!(scale>0)||!IsFinite(scale)||!IsFinite(1.0/scale))returnfalse;',
        'internalstaticintCeilExponent(doublex){longbits=BitConverter.DoubleToInt64Bits(x);intbiased=(int)((bits>>52)&0x7FF);'
        'longmantissa=bits&0xFFFFFFFFFFFFFL;if(biased==0){inte=-1074;while(mantissa>1){mantissa=(mantissa+1)>>1;++e;}returne;}'
        'intexponent=biased-1023;returnmantissa==0?exponent:exponent+1;}',
        'internalstaticdoublePowerOfTwo(intn){if(n>1023)returndouble.PositiveInfinity;if(n<-1022)return0;'
        'returnBitConverter.Int64BitsToDouble((long)(n+1023)<<52);}',
        'doubleq=Math.Round(v*Scale);if(!(Math.Abs(q)<=MaxIntegerCoordinate))',
    ]
    for r in required:
        if n.count(r) != 1:
            raise ValueError('ClipperFrame no longer contains exactly once: ' + r[:80])
    return dict(min_bits=8, max_bits=50, max_integer=2 ** 52)


def _check(hyps, bad, timeout_ms):
    s = z.Solver()
    s.set(timeout=timeout_ms)
    s.add(*hyps)
    s.add(bad)
    t0 = time.monotonic()
    r = s.check()
    return s, r, time.monotonic() - t0


def _row(name, hyps, bad, timeout_ms, expect, witness=None):
    s, r, secs = _check(hyps, bad, timeout_ms)
    out = dict(name=name, result=str(r), expected=expect, seconds=round(secs, 3), ok=str(r) == expect)
    if r == z.unknown:
        out['reason'] = s.reason_unknown()
    sv = z.Solver()
    sv.set(timeout=timeout_ms)
    sv.add(*hyps)
    out['assumptions_result'] = str(sv.check())
    if witness and r == z.sat:
        out['witness'] = witness(s.model())
    return out


def _exact(name, holds, note, expect='unsat'):
    """an exact (non-Z3) check: result 'unsat' when the claim holds, 'sat' when a counterexample exists"""
    result = 'unsat' if holds else 'sat'
    return dict(name=name, result=result, expected=expect, seconds=0.0, ok=result == expect, assumptions_result='sat', note=note)


def _frames(max_bits):
    """every (precisionBits, exponent) the guards of ForBounds accept, with precisionBits up to max_bits: the count, and the first
    whose scale * 2^e is not 2^bits <= 2^52 (the integer coordinates must stay exact and below MaxIntegerCoordinate), or None"""
    accepted, failing = 0, None
    for bits in range(8, max_bits + 1):
        for e in range(-1074, 1025):
            n = bits - e
            if n > 1023 or n < -1022:
                continue          # PowerOfTwo gives inf or 0: the frame is refused
            scale = Fraction(2) ** n
            if not (1 / scale <= Fraction(1.7976931348623157e308)):      # 1.0 / scale must be finite
                continue
            accepted += 1
            if not (Fraction(2) ** e * scale == Fraction(2) ** bits and Fraction(2) ** bits <= Fraction(2) ** 52) and failing is None:
                failing = (bits, e)
    return accepted, failing


def run(timeout_ms=120000):
    contracts = source_contracts()
    rows = []
    # E1: CeilExponent on the 64 bits of a positive normal x (biased exponent 1..2046): the bit operations of the code give e,
    # and x = (2^52 + mantissa) * 2^(biased - 1075) by the IEEE definition; in units of 2^(biased - 1075) the claims
    # 2^(e-1) < x <= 2^e read 2^(e - biased + 1074) < 2^52 + mantissa <= 2^(e - biased + 1075)
    bits = z.BitVec('bits', 64)
    biased_bv = z.LShR(bits, 52) & z.BitVecVal(0x7FF, 64)
    mant_bv = bits & z.BitVecVal(0xFFFFFFFFFFFFF, 64)
    biased, mant = z.Int('biased'), z.Int('mant')
    code_e = z.If(mant == 0, biased - 1023, biased - 1023 + 1)            # `mantissa == 0 ? exponent : exponent + 1`
    def ceil_ok(e_of_code):
        shift = e_of_code - biased + 1075                                    # 2^shift is 2^52 or 2^53 in the units of x
        up = z.If(shift == 52, z.IntVal(2 ** 52), z.If(shift == 53, z.IntVal(2 ** 53), z.IntVal(-1)))
        mantissa_value = z.IntVal(2 ** 52) + mant
        return z.And(up > 0, mantissa_value <= up, mantissa_value > up / 2)
    tie = [z.BV2Int(biased_bv) == biased, z.BV2Int(mant_bv) == mant, z.UGE(biased_bv, 1), z.ULE(biased_bv, 2046), z.ULT(bits, z.BitVecVal(2 ** 63, 64))]
    rows.append(_row('E1_CeilExponent_on_the_bits_is_the_smallest_power_of_two_above', tie, z.Not(ceil_ok(code_e)), timeout_ms, 'unsat'))
    floor_e = biased - 1023                                                  # the mutant: the exponent for every mantissa (a floor)
    rows.append(_row('mutant_floor_instead_of_ceiling_(extent_above_2^e)', tie, z.Not(ceil_ok(floor_e)), timeout_ms, 'sat',
                     lambda mod: dict(mantissa=str(mod.eval(mant, model_completion=True)), biased=str(mod.eval(biased, model_completion=True)))))

    # E2: unrolled subnormal loop on 64-bit vectors
    mm = z.BitVec('mm', 64)
    cur, c = mm, z.BitVecVal(0, 64)
    for _ in range(53):
        step = z.UGT(cur, 1)
        cur, c = z.If(step, z.LShR(cur + 1, 1), cur), z.If(step, c + 1, c)
    sub_hyps = [z.UGE(mm, 1), z.ULT(mm, z.BitVecVal(2 ** 52, 64))]
    two_c = z.BitVecVal(1, 64) << c
    rows.append(_row('E2_subnormal_loop_ends_with_mantissa_1_and_2^(c-1)_<_m_<=_2^c', sub_hyps,
                     z.Not(z.And(z.ULE(cur, 1), z.ULT(c, 53), z.ULE(mm, two_c), z.UGT(z.BitVecVal(2, 64) * mm, two_c))), timeout_ms, 'unsat'))

    # E3: exhaustive
    pw_ok = all(Fraction(struct.unpack('<d', struct.pack('<Q', (n + 1023) << 52))[0]) == Fraction(2) ** n for n in range(-1022, 1024))
    rows.append(_exact('E3_PowerOfTwo_is_exactly_2^n_for_all_n_in_[-1022,1023]', pw_ok, 'exhaustive, 2046 values'))
    accepted, failing = _frames(50)
    rows.append(_exact('E3_accepted_frames_scale_2^e_to_2^bits_<=_2^50_<_2^52', failing is None and accepted > 1000, f'exhaustive, {accepted} frames'))

    # E4
    d1, d2, mg = z.FPs('d1 d2 mg', F64)
    zero = z.FPVal(0.0, F64)
    fin = [z.Not(z.fpIsNaN(x)) for x in (d1, d2, mg)] + [z.Not(z.fpIsInf(x)) for x in (d1, d2, mg)]
    core = z.If(z.fpGT(d1, d2), d1, d2)
    rows.append(_row('E4_extent_is_at_least_the_larger_distance', fin + [z.fpGEQ(mg, zero)],
                     z.Not(z.fpGEQ(z.fpAdd(RNE, core, mg), core)), timeout_ms, 'unsat'))

    # mutant: MaxPrecisionBits 53, the same enumeration: a frame whose integer coordinates exceed 2^52 exists
    acc53, failing53 = _frames(53)
    rows.append(_exact('mutant_MaxPrecisionBits_53_gives_a_frame_with_integers_above_2^52', failing53 is None, f'exhaustive, {acc53} frames, first failing {failing53}', 'sat'))
    passed = all(r['ok'] and r['assumptions_result'] == 'sat' for r in rows)
    return dict(contracts=contracts, obligations=rows, passed=bool(passed))


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
