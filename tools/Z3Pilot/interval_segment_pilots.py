"""Local SMT pilots for binary64 interval successors and segment slabs."""
import math
import struct
from fractions import Fraction

import z3 as z

from check_pilot_obligations import collect, source as pin
from pilot import F, RN

U = Fraction(1, 2**53)
EPS = Fraction(1, 2**1074)
MANTISSA = 2**52


def q(value):
    return z.RealVal(str(Fraction(value)))


def abs_value(value):
    return z.If(value >= 0, value, -value)


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
    power_correction = Fraction(1, 2) + Fraction(1, 2 * MANTISSA)
    assert Fraction(1, 4) < power_correction < Fraction(3, 4)
    for exponent in range(-1022, -969):
        assert -969 <= exponent + 54 <= 1023
        for sign_change in (-1, 0, 1):
            if exponent == -1022 and sign_change == -1:
                continue  # -MinNormal is handled by the smaller branch.
            assert -1022 <= exponent + sign_change <= 1023

    obligations = [
        ('small_positive_bits_differ', small + [z.Not(sign)], implemented != expected, 'unsat'),
        ('small_negative_bits_differ', small + [sign], implemented != expected, 'unsat'),
        ('small_positive_branch_reachable', small + [z.Not(sign)], magnitude > 0, 'sat'),
        ('small_negative_branch_reachable', small + [sign], magnitude > 0, 'sat'),
        ('skip_positive_step_mutant', small + [z.Not(sign)], bits != expected, 'sat'),
        ('negative_zero_special_case_matters', small + [negative_epsilon], ordinary != expected, 'sat'),
        ('correction_can_round_to_half_step', mantissa_range,
         delta <= q(Fraction(1, 2) + Fraction(1, 4 * MANTISSA)), 'unsat'),
        ('correction_can_round_above_one_step', mantissa_range,
         delta >= q(1 + Fraction(1, 2 * MANTISSA)), 'unsat'),
        ('correction_input_reachable', mantissa_range, m == MANTISSA + 1, 'sat'),
        ('phi1_rounded_down_mutant', mantissa_range,
         m * q(Fraction(1, 2 * MANTISSA)) <= q(Fraction(1, 2) + Fraction(1, 4 * MANTISSA)), 'sat'),
        ('phi1_doubled_mutant', mantissa_range,
         2 * delta >= q(1 + Fraction(1, 2 * MANTISSA)), 'sat'),
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
    pin('mesh_ops/ExactMeshArrangement.cs', 'static bool segment_hits(', 'void morton_order(', '79e202891b094e8c7323a3034403402467f27316132b22c1132bb25771692ad3')
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
    p_x, q_x, slab_lo = 0.0, 2e-310, 1e-310
    assert abs(q_x - p_x) < 1e-300 and p_x < slab_lo <= q_x
    obligations = [
        ('near_parallel_endpoint_rule_can_cull_intersection', intersecting, endpoints_outside, 'unsat'),
        ('near_parallel_intersection_reachable', intersecting, z.And(p < lo, lo <= other), 'sat'),
        ('start_only_cull_mutant', intersecting, z.Or(p < lo, p > hi), 'sat'),
        ('finite_division_error_exceeds_admitted_interval', quotient_rounding,
         abs_value(parameter - computed) > q(4 * U) * abs_value(computed) + q(2 * EPS), 'unsat'),
        ('finite_division_reachable', quotient_rounding, computed > 0, 'sat'),
        ('ignore_subnormal_division_error_mutant', quotient_rounding,
         abs_value(parameter - computed) > q(4 * U) * abs_value(computed), 'sat'),
        ('finite_slack_fails_to_enclose_exact_parameter', quotient,
         z.Or(low > parameter, high < parameter), 'unsat'),
        ('finite_parameter_enclosure_reachable', quotient, parameter == q(Fraction(1, 2)), 'sat'),
        ('omit_slack_from_endpoints_mutant', quotient, z.Or(a > parameter, b < parameter), 'sat'),
    ]
    report = collect(obligations, timeout_ms)
    # The witness culled the segment until #1673; the source now applies the endpoint rule proved above
    # (near_parallel_endpoint_rule_can_cull_intersection), and the start-only rule is the mutant.
    report['witness'] = dict(p_x=0.0, q_x=2e-310, slab_lo=1e-310, slab_hi=1.0,
                             box_contains_q=True, source_small_branch_rejects=False,
                             rejected_before='#1673')
    return report


PILOTS = dict(interval_successor=interval_successor, segment_box=segment_box)
