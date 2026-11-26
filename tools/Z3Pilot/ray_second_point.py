"""Local exponent/encoding checks and an IEEE replay for SecondPoint."""
import math
import struct

from check_pilot_obligations import check, main, report, source

SOURCE = 'math/ExactRayTriangle.cs'
CONTRACTS = [
    ('public static Vector3d SecondPoint(', 'static int exponent(', '07473910d46ef90eb08f7d2e58d9b05165d379940e7c197bdd49d060dc9f6afa'),
    ('static int exponent(', 'static bool is_finite(', '2ee9eb5b2784ad8cc9c4d2528205738ecd26df7ed8993403f0020ceb31cd4611'),
]
SCALE = 'math/ExactPoint3.cs'
SCALE_CONTRACTS = [
    ('public static double ScaleB(', 'public static long BitLength(', 'a154593e520e403416532c2c99d716f84a86180d68eaaa458c59c4218c043a8f'),
]
PINS = [(SOURCE, CONTRACTS, False), (SCALE, SCALE_CONTRACTS, False)]

EXPONENTS = """
(declare-const origin Int)
(declare-const direction Int)
(declare-const e Int)
(assert (and (<= (- 1074) origin) (<= origin 1023)))
(assert (and (<= (- 1074) direction) (<= direction 1023)))
(define-fun shift () Int (+ (- origin direction) 1))
"""
ENCODING = """
(declare-const shift (_ BitVec 64))
(assert (and (bvsle (bvneg (_ bv1022 64)) shift)
             (bvsle shift (_ bv1023 64))))
(define-fun biased () (_ BitVec 64) (bvadd shift (_ bv1023 64)))
(define-fun bits () (_ BitVec 64) (bvshl biased (_ bv52 64)))
"""

# For the x component, origin = epsilon, direction = epsilon, scale = 1/2.
# A separate component direction = 4*epsilon makes SecondPoint select m=-1.
# A wider evaluation keeps the first half-spacing until the sum; the
# fma here serves as a one-rounding reference for these exact operands.
IEEE = """
(define-fun eps () (_ FloatingPoint 11 53) ((_ to_fp 11 53) #x0000000000000001))
(define-fun half () (_ FloatingPoint 11 53) ((_ to_fp 11 53) #x3fe0000000000000))
(define-fun two_eps () (_ FloatingPoint 11 53) ((_ to_fp 11 53) #x0000000000000002))
(define-fun binary64 () (_ FloatingPoint 11 53) (fp.add RNE eps (fp.mul RNE eps half)))
(define-fun retained () (_ FloatingPoint 11 53) (fp.fma RNE eps half eps))
"""


def from_bits(bits):
    return struct.unpack('>d', bits.to_bytes(8, 'big'))[0]


def to_bits(value):
    return int.from_bytes(struct.pack('>d', value), 'big')


def scale_b(value, shift):
    """Transcribe the source normalized-mantissa path for boundary replays."""
    bits = to_bits(value)
    exponent = (bits >> 52) & 0x7ff
    if exponent == 0:
        bits = to_bits(value * 2.0**64)
        exponent = (bits >> 52) & 0x7ff
        shift -= 64
    exponent += shift - 1023
    sign = bits & (1 << 63)
    fraction = bits & ((1 << 52) - 1)
    if exponent > 1023:
        return from_bits(sign | (0x7ff << 52))
    if exponent >= -1022:
        return from_bits(sign | ((exponent + 1023) << 52) | fraction)
    if exponent < -1080:
        return from_bits(sign)
    normalized = from_bits(sign | (1 << 52) | fraction)
    return normalized * from_bits((exponent + 2045) << 52)


def boundary_replays():
    values = [1, 2, 3, (1 << 52) - 1, 1 << 52, (1 << 52) + 1,
              0x3fefffffffffffff, 0x3ff0000000000000, 0x3ff0000000000001,
              0x7fefffffffffffff]
    shifts = (-2096, -1081, -1080, -1075, -1074, -1023, -1022, -1,
              0, 1, 52, 1022, 1023, 1024, 2098)
    cases = 0
    for bits in values:
        for sign in (0, 1 << 63):
            value = from_bits(bits | sign)
            for shift in shifts:
                try:
                    expected = math.ldexp(value, shift)
                except OverflowError:
                    expected = math.copysign(math.inf, value)
                if to_bits(scale_b(value, shift)) != to_bits(expected):
                    raise AssertionError(f'ScaleB boundary mismatch: {value.hex()}, {shift}')
                if -1022 <= shift <= 1023 and to_bits(value * 2.0**shift) != to_bits(expected):
                    raise AssertionError(f'fast scale mismatch: {value.hex()}, {shift}')
                cases += 1
    return cases


def run(timeout_ms=120000):
    for path, contracts, prose in PINS:
        for item in contracts:
            source(path, *item, prose=prose)
    checks = [
        ('shift_outside_derived_range', 'QF_LIA', EXPONENTS,
         '(or (< shift (- 2096)) (> shift 2098))', 'unsat'),
        ('old_clamp_would_change_shift', 'QF_LIA', EXPONENTS,
         '(> shift 1023)', 'sat'),
        ('subnormal_scale_factor_exponent_out_of_range', 'QF_LIA', EXPONENTS,
         '(and (<= (- 1080) e) (< e (- 1022)) (or (< (+ e 2045) 1) (> (+ e 2045) 2046)))', 'unsat'),
        ('normal_shift_bad_encoding', 'QF_BV', ENCODING,
         '(or (distinct ((_ extract 63 63) bits) #b0) '
         '(distinct ((_ extract 51 0) bits) (_ bv0 52)) '
         '(distinct ((_ zero_extend 53) ((_ extract 62 52) bits)) biased) '
         '(= ((_ extract 62 52) bits) (_ bv0 11)) '
         '(= ((_ extract 62 52) bits) (_ bv2047 11)))', 'unsat'),
        ('intermediate_precision_mismatch', 'QF_FP', IEEE,
         '(and (fp.eq binary64 eps) (fp.eq retained two_eps) (not (fp.eq binary64 retained)))', 'sat'),
    ]
    rows = [check(*row, timeout_ms) for row in checks]
    epsilon = from_bits(1)
    return report(rows, scale_boundary_replays=boundary_replays(),
                  witness=dict(origin=epsilon.hex(), axis_direction=epsilon.hex(),
                                     other_direction=(4 * epsilon).hex(), shift=-1,
                                     separately_rounded=(epsilon + epsilon * 0.5).hex(),
                                     retained_result=(2 * epsilon).hex()))


if __name__ == '__main__':
    raise SystemExit(main(run))
