"""Local SMT pilots for binary64 interval successors and segment slabs."""
import hashlib
import math
import re
import struct
from fractions import Fraction

import z3 as z

from pilot import F, RN, ROOT, check

U = Fraction(1, 2**53)
EPS = Fraction(1, 2**1074)
MANTISSA = 2**52


def pin(path, start, end, expected):
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    start = text.index(start)
    end = text.index(end, start + 1)
    code = re.sub(r'\s+', '', re.sub(r'//[^\n]*', '', text[start:end]))
    if hashlib.sha256(code.encode()).hexdigest() != expected:
        raise ValueError(f'{path}: review the SMT transcription near {text[start:start + 60]}')


def q(value):
    return z.RealVal(str(Fraction(value)))


def abs_value(value):
    return z.If(value >= 0, value, -value)


def collect(obligations, timeout_ms):
    rows = []
    for name, assumptions, bad, expected in obligations:
        _, _, row = check(name, assumptions, bad, timeout_ms)
        row['expected'] = expected
        rows.append(row)
    return dict(z3=z.get_version_string(), obligations=rows,
                passed=all(row['result'] == row['expected'] for row in rows))


def binary64(bits):
    return struct.unpack('>d', bits.to_bytes(8, 'big'))[0]


def bits_of(value):
    return int.from_bytes(struct.pack('>d', value), 'big')


def successor_replay(value):
    """Transcription for concrete boundary replays separate from Z3."""
    absolute = abs(value)
    if absolute >= 2.0**-969 and value != -math.inf:
        return value + absolute * 1.1102230246251568e-16
    if value == -math.inf:
        return -float.fromhex('0x1.fffffffffffffp+1023')
    if absolute <= 2.0**-1022:
        return -0.0 if value == -float(EPS) else value + float(EPS)
    scaled = value * 2.0**54
    error = abs(scaled) * 1.1102230246251568e-16
    return (scaled + error) * 2.0**-54


def interval_successor(timeout_ms):
    pin('math/ExactIntervals.cs', 'const double Phi1', 'static ExactInterval make(', '19750023bf68654b12783b1c37999e57d2f9f2511ab60d92c2d8d3182a6dcf14')
    assert bits_of(1.1102230246251568e-16) == 0x3CA0000000000001
    bits = z.BitVec('small_value_bits', 64)
    value = z.fpToFP(bits, F)
    sign = z.Extract(63, 63, bits) == 1
    magnitude = bits & (2**63 - 1)
    tiny = z.FPVal(float(EPS), F)
    negative_epsilon = bits == 0x8000000000000001
    ordinary = z.fpToIEEEBV(z.fpAdd(RN, value, tiny))
    expected = z.If(magnitude == 0, z.BitVecVal(1, 64), z.If(sign, bits - 1, bits + 1))
    implemented = z.If(negative_epsilon, z.BitVecVal(2**63, 64), ordinary)
    small = [z.ULE(magnitude, 0x0010000000000000)]

    # In units of the binade spacing of x, the exact correction is
    # M*(2^52+1)/2^105. For this branch the correction is normal.
    # Rounding is monotone at the two correction midpoints.
    m = z.Int('significand')
    mantissa_range = [MANTISSA <= m, m < 2 * MANTISSA]
    delta = q(Fraction(MANTISSA + 1, 2 * MANTISSA**2)) * m
    exponent = z.Int('middle_exponent')
    middle = [exponent >= -1022, exponent <= -970]
    middle_negative = z.Bool('middle_negative')
    neighbor_exponent = exponent + z.If(z.And(middle_negative, m == MANTISSA), -1,
                                        z.If(z.And(z.Not(middle_negative), m == 2 * MANTISSA - 1), 1, 0))
    above_min_normal = z.Or(exponent > -1022, m > MANTISSA)
    power_correction = Fraction(1, 2) + Fraction(1, 2 * MANTISSA)
    # At a negative exact power of two the successor is a half-step
    # towards zero. The exact correction for M=2^52 is representable
    # and below that step's next midpoint at 3/4.
    obligations = [
        ('small_positive_bits_differ', small + [z.Not(sign)], implemented != expected, 'unsat'),
        ('small_negative_bits_differ', small + [sign], implemented != expected, 'unsat'),
        ('negative_zero_special_case_matters', small + [negative_epsilon], ordinary != expected, 'sat'),
        ('correction_can_round_to_half_step', mantissa_range,
         delta <= q(Fraction(1, 2) + Fraction(1, 4 * MANTISSA)), 'unsat'),
        ('correction_can_round_above_one_step', mantissa_range,
         delta >= q(1 + Fraction(1, 2 * MANTISSA)), 'unsat'),
        ('negative_power_correction_can_leave_half_step_cell', [m == MANTISSA],
         z.Or(delta != q(power_correction), delta <= q(Fraction(1, 4)), delta >= q(Fraction(3, 4))), 'unsat'),
        ('middle_rescaling_can_miss_normal_rump_range', middle,
         z.Or(exponent + 54 < -969, exponent + 54 > 1023), 'unsat'),
        ('middle_successor_rescaling_can_leave_normal_range',
         middle + mantissa_range + [above_min_normal],
         z.Or(neighbor_exponent < -1022, neighbor_exponent > 1023), 'unsat'),
    ]
    report = collect(obligations, timeout_ms)

    # Check both signs, both sides of each branch boundary, all
    # normal exponent bins and both zero signs. The finite normal
    # combination with arbitrary host rounding is outside this replay.
    samples = [0.0, -0.0, math.inf, -math.inf, float(EPS), -float(EPS)]
    for e in range(1, 2047):
        for significand in (0, 1, 2, MANTISSA // 2, MANTISSA - 2, MANTISSA - 1):
            sample = binary64((e << 52) | significand)
            samples.extend([sample, -sample])
    for value in samples:
        successor = math.nextafter(value, math.inf)
        if value == -float(EPS):
            successor = -0.0
        assert bits_of(successor_replay(value)) == bits_of(successor), value.hex()
        # Down is defined by this reflection in the source.
        predecessor = math.nextafter(value, -math.inf)
        if value == float(EPS):
            predecessor = 0.0
        assert bits_of(-successor_replay(-value)) == bits_of(predecessor), value.hex()
    assert math.isnan(successor_replay(math.nan))
    report['replays'] = len(samples)
    return report


def segment_box(timeout_ms):
    pin('mesh_ops/ExactMeshArrangement.cs', 'static bool segment_hits(', 'void morton_order(', '6586871762521ffa64ae29321df65a45bc53e9a13c6ae50968fa389294a676e7')
    p, other, lo, hi, t = z.Reals('slab_p slab_q slab_lo slab_hi slab_t')
    point = (1 - t) * p + t * other
    intersecting = [lo <= hi, t >= 0, t <= 1, lo <= point, point <= hi]
    endpoints_outside = z.Or(z.And(p < lo, other < lo), z.And(p > hi, other > hi))

    # Local finite quotient bridge. The exact endpoint parameter
    # corresponds to either computed parameter before sorting.
    a, b, parameter, computed, span, relative, slack = z.Reals(
        'rounded_lo rounded_hi exact_t rounded_t span relative_slack slack')
    low, high = z.Reals('low high')
    absolute_slack = q(1e-12)
    quotient = [
        a <= b, z.Or(computed == a, computed == b),
        abs_value(parameter - computed) <= q(4 * U) * abs_value(computed) + q(2 * EPS),
        span >= q(1 - U) * (abs_value(a) + abs_value(b)),
        relative >= q(Fraction(1e-9) * (1 - U)) * span - q(EPS / 2), relative >= 0,
        slack >= q(1 - U) * (relative + absolute_slack),
        abs_value(low - (a - slack)) <= q(U) * abs_value(a - slack),
        abs_value(high - (b + slack)) <= q(U) * abs_value(b + slack),
    ]
    # For finite arithmetic the subtractions give a ratio error
    # no larger than 2u/(1-u). Add the division rounding itself,
    # allowing the half-spacing absolute term when it underflows.
    before_round = z.Real('before_division_rounding')
    quotient_rounding = [
        abs_value(parameter - before_round) <= q(2 * U / (1 - U)) * abs_value(before_round),
        abs_value(computed - before_round) <= q(U) * abs_value(before_round) + q(EPS / 2),
    ]
    fp_p, fp_q, fp_lo = (z.FPVal(v, F) for v in (0.0, 2e-310, 1e-310))
    displacement = z.fpSub(RN, fp_q, fp_p)
    obligations = [
        ('near_parallel_endpoint_rule_can_cull_intersection', intersecting, endpoints_outside, 'unsat'),
        ('original_near_parallel_branch_drops_intersection', [],
         z.And(z.fpLT(z.fpAbs(displacement), z.FPVal(1e-300, F)),
               z.fpLT(fp_p, fp_lo), z.fpLEQ(fp_lo, fp_q)), 'sat'),
        ('finite_division_error_exceeds_admitted_interval', quotient_rounding,
         abs_value(parameter - computed) > q(4 * U) * abs_value(computed) + q(2 * EPS), 'unsat'),
        ('finite_slack_fails_to_enclose_exact_parameter', quotient,
         z.Or(low > parameter, high < parameter), 'unsat'),
        ('finite_parameter_enclosure_reachable', quotient, parameter == q(Fraction(1, 2)), 'sat'),
    ]
    report = collect(obligations, timeout_ms)
    report['witness'] = dict(p_x=0.0, q_x=2e-310, slab_lo=1e-310, slab_hi=1.0,
                             box_contains_q=True, source_small_branch_rejects=True)
    return report


PILOTS = dict(interval_successor=interval_successor, segment_box=segment_box)
