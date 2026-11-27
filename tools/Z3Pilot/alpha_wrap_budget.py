"""Local Alpha Wrap size-estimate obligations and binary64 boundary replays.

The input terms are frozen at the originally requested alpha and offset.
The real model is the budget algebra, not a bound on wrap output size.
"""
from fractions import Fraction
import math

import z3

from check_pilot_obligations import check_expr, main, report, source_contract

SOURCE = 'mesh_ops/MeshAlphaWrap.cs'
ANCHORS = [
    ('double ratio = offset > 0 && alpha > 0', 'UsedAlpha = alpha; UsedOffset = offset;', '3ff84595d15b7c127be117f7564b608f99828a107de51f9b545d0294f2bceeb1'),
    ('public const double EstimateDensity =', 'public bool RefineNonManifold', '487c31f84f20d538cc19c2f8bdcdfe5205007c1dc165b94ea8c89cbd42b38955'),
    ('double area = 0;', 'double ratio = offset > 0 && alpha > 0', 'bb525b44a01c8e3df904a3c10383ea115cce56f7eddce26c7bd30a94040235db'),
    ('const double RangeMin =', 'WrapStatus setup()', '729937ad0dda758e176e288dd96648d2e0156a5d206a9c0172f5072378b79628'),
    ('double margin = 2 * (offset + alpha)', 'dt = new Delaunay3(box);', 'fe438fc130a0ce3befa8789c8ada7584acbcd43f7c297a6cc8d3b7efd36f6973'),
]


def real_obligations(timeout_ms):
    p, r, budget, a, b = z3.Reals('P R B a b')
    assumptions = [p >= 0, r >= 0, p+r > 0, budget > 0, a > 0, b >= a]
    radical = z3.Real('sqrt_discriminant')
    branch = assumptions + [radical >= 0, radical*radical == r*r+4*budget*p,
                            2*budget*a == r+radical]
    # Sign of the estimate minus budget, multiplied by a positive square.
    excess = lambda k: p+r*k-budget*k*k
    return [
        check_expr('positive_mixed_terms_and_order_reachable', assumptions, z3.And(p > 0, r > 0, b > a), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('mixed_quadratic_branch_reachable', branch, z3.And(p > 0, r > 0), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('mixed_quadratic_branch_can_miss_exact_budget', branch,
                   excess(a) != 0, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('estimate_can_fail_to_decrease', assumptions + [b > a],
                   p*b*b+r*a*b*b <= p*a*a+r*b*a*a, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('smaller_scale_can_meet_budget_when_cap_cannot', assumptions + [excess(b) > 0],
                   excess(a) <= 0, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('larger_scale_can_violate_budget_after_root', assumptions + [excess(a) == 0],
                   excess(b) > 0, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('omitting_rims_can_choose_an_insufficient_scale', assumptions + [p == budget*a*a],
                   excess(a) > 0, 'sat', timeout_ms, 'QF_NRA'),
    ]


TERMS_SOURCE = 'mesh_ops/AlphaWrapEstimate.cs'
TERMS_ANCHORS = [
    ('public const double OpeningFactor =', 'public static double OpenArea(', '307c00ec243b8c09968a66ef02171336bab1cf481878bc2582dcf48b72311279'),
    ('double cell = alpha / WeldCells;', 'comp = new int[nv];', '7dc600ba9fdc4a1434ff0aa2a7547ae6d0c88e1f0abf24ae9f37e5de583d599c'),
    ('pos = new Vector3d[nv];', 'var chain = new int[nv];', '23ae3b16b0cda7d1e0fac52106ac7902a5b98ad75768997085043d601b923eba'),
    ('public static EstimateTerms Terms(', 'public static double LargestOpening(', '862ecfbc33be1f98e971f9d34da43fbf2b41a0f484dd760940f82f94d1abdc1e'),
    ('const double CellIndexLimit', 'static void CountEdge', '0a12ef70891cf9da5255efdf888b4531b79597fc3640b48e2866f3da470c04c2'),
]


def weld_cell_obligations(timeout_ms):
    magnitude, width, gap = z3.Reals('coordinate_magnitude weld_cell_width adjacent_coordinate_gap')
    # The gap to any neighboring binary64 coordinate is at least |coordinate| / 2^53.
    # A rounded quotient outside +/-9e18 has exact magnitude above 2^62.
    premises = [width > 0, magnitude >= 2**62*width, 2**53*gap >= magnitude]
    return [
        check_expr('wide_index_premises_are_reachable', premises, gap > width, 'sat', timeout_ms, 'QF_LRA'),
        check_expr('wide_index_can_have_distinct_coordinates_in_one_cell', premises,
                   gap <= width, 'unsat', timeout_ms, 'QF_LRA'),
        check_expr('narrow_index_can_have_distinct_coordinates_in_one_cell',
                   [width > 0, magnitude >= 2**52*width, 2**53*gap >= magnitude],
                   gap <= width, 'sat', timeout_ms, 'QF_LRA'),
    ]


def discriminant_range_obligations(timeout_ms):
    # Conservative source-range enclosures, with the argument for the input
    # terms in the README. Prove the algebraic bounds using the largest coefficients.
    area, opened, thin, edge, rim, sharp, ratio, budget = z3.Reals(
        'area open_area thin_area open_edge thin_rim sharp_edge ratio budget')
    premises = [x >= 0 for x in (area, opened, thin, edge, rim, sharp, ratio, budget)]
    premises += [x <= 2**300 for x in (area, opened, thin)]
    premises += [x <= 2**192 for x in (edge, rim, sharp)]
    premises += [ratio <= 2**20, budget <= 2**31]
    bulk = 3*(area+opened)+(opened+thin)*ratio
    rim_alpha = (2*(edge+rim)+sharp)*ratio
    disc_premises = [z3.Real('bulk')>=0, z3.Real('bulk')<=2**324,
                     z3.Real('rim')>=0, z3.Real('rim')<=2**217, budget>=0, budget<=2**31]
    return [
        check_expr('range_envelopes_are_reachable', premises,
                   z3.And(area>0, opened>0, thin>0, edge>0, rim>0, sharp>0, ratio>0, budget>0),
                   'sat', timeout_ms, 'QF_NRA'),
        check_expr('supported_term_envelopes_allow_large_bulk', premises,
                   bulk >= 2**323, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('supported_term_envelopes_allow_large_rim', premises,
                   rim_alpha >= 2**216, 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('bounded_quadratic_terms_are_reachable', disc_premises,
                   z3.Real('rim')**2+4*budget*z3.Real('bulk') >= 2**434,
                   'sat', timeout_ms, 'QF_NRA'),
        check_expr('bounded_quadratic_terms_allow_large_discriminant', disc_premises,
                   z3.Real('rim')**2+4*budget*z3.Real('bulk') >= 2**436,
                   'unsat', timeout_ms, 'QF_NRA'),
    ]


def replay_fp():
    f64, rnd = z3.Float64(), z3.RNE()
    def fp(x):
        return z3.FPVal(x, f64)
    def same(expr, value):
        return z3.is_true(z3.simplify(z3.fpEQ(expr, fp(value))))
    root = math.sqrt(3.0)
    alpha, cap, coarse = 3.0, 1.2, 3.6
    huge_area = 2.0**1022
    tiny_area = 2.0**-1074
    tiny_alpha = 2.0**-550
    square = z3.fpMul(rnd, fp(tiny_alpha), fp(tiny_alpha))
    square_first = z3.fpDiv(rnd, fp(tiny_area), square)
    twice_divided = z3.fpDiv(rnd, z3.fpDiv(rnd, fp(tiny_area), fp(tiny_alpha)), fp(tiny_alpha))
    discriminant = z3.fpAdd(rnd, fp(1), z3.fpMul(rnd, fp(4), fp(huge_area)))
    # Coarsened alpha observed on the unit-scale public-API reproducer.
    coarse_alpha = 0.026868839879520764
    minsub = 2.0**-1074
    factor = z3.fpDiv(rnd, fp(coarse_alpha), fp(minsub))
    legacy_offset = z3.fpMul(rnd, fp(minsub), factor)
    wide_offset = 2.0**-49
    exact_offset = lambda offset: Fraction(offset) * Fraction(coarse_alpha) / Fraction(minsub)
    translated, other_corner = 2.0**57, 2.0**57 + 64
    width = 2.0**-7
    limit = 9e18
    old_index = lambda v: min(limit, math.floor(v / width))
    # Inputs and measured first Steiner coordinates from --frame-audit.
    padding = 1e-3 * math.sqrt((65 / 32)**2 + 2)
    ordinary_alpha, dyadic_alpha = 0.026868839879520764, (0.125 - padding) / 4
    shift_y, shift_z = 5712043.5, 113.75
    local_steiner_z, utm_steiner_z = -0.02634638046891702, -0.026346380468922348
    def margin(a):
        return z3.fpAdd(rnd, z3.fpMul(rnd, fp(2), z3.fpAdd(rnd, fp(a), fp(a))), fp(padding))
    def local_box_min_y(a):
        return z3.fpSub(rnd, z3.fpSub(rnd, fp(shift_y), margin(a)), fp(shift_y))
    rounded_steiner = z3.fpAdd(rnd, fp(shift_z), fp(local_steiner_z))
    checks = {
        'ordinary_seed_box_y_loses_translation_bits':
            same(local_box_min_y(ordinary_alpha), -0.10995043069124222)
            and not same(local_box_min_y(ordinary_alpha), -(4*ordinary_alpha + padding)),
        'dyadic_seed_box_y_translates_exactly':
            same(margin(dyadic_alpha), 0.125) and same(local_box_min_y(dyadic_alpha), -0.125),
        'dyadic_first_steiner_z_roundtrip_matches_utm_run':
            same(z3.fpSub(rnd, rounded_steiner, fp(shift_z)), utm_steiner_z)
            and Fraction(local_steiner_z) + Fraction(shift_z) != Fraction(local_steiner_z + shift_z),
        'legacy_weld_cell_clamp_collapses_two_distinct_corners':
            old_index(translated) == old_index(other_corner) == limit
            and not same(fp(translated), other_corner),
        'smallest_alpha_underflows_weld_cell_width':
            same(z3.fpDiv(rnd, fp(minsub), fp(8)), 0.0),
        'factor_overflows_but_same_ratio_coarsened_offset_is_finite':
            same(factor, math.inf) and same(legacy_offset, math.inf)
            and float(exact_offset(minsub)) == coarse_alpha
            and minsub * (coarse_alpha / minsub) == math.inf,
        'reordering_as_offset_over_alpha_can_also_overflow':
            same(z3.fpDiv(rnd, fp(wide_offset), fp(minsub)), math.inf)
            and math.isfinite(float(exact_offset(wide_offset))),
        'rounded_bulk_root_slightly_exceeds_exact_budget':
            same(z3.fpSqrt(rnd, fp(3)), root) and Fraction(root)**2 < 3,
        'rounded_ratio_hides_exact_cap_crossing':
            same(z3.fpDiv(rnd, fp(coarse), fp(alpha)), cap) and coarse/alpha == cap
            and Fraction(coarse) > Fraction(alpha)*Fraction(cap),
        'square_first_underflows_but_sequential_division_keeps_a_finite_estimate':
            same(square, 0) and same(square_first, math.inf) and same(twice_divided, 2.0**26)
            and tiny_alpha*tiny_alpha == 0 and tiny_area/tiny_alpha/tiny_alpha == 2.0**26,
        'mixed_discriminant_overflows_while_root_is_representable':
            same(discriminant, math.inf) and math.isinf(1+4*huge_area)
            and math.isfinite(0.5 + math.hypot(0.5, math.sqrt(huge_area))),
    }
    return checks, {
        'bulk_root': {'P': 3, 'B': 1, 'coarse': root},
        'cap': {'alpha': alpha, 'cap': cap, 'coarse': coarse},
        'underflow': {'alpha': tiny_alpha, 'P': tiny_area},
        'overflow': {'bulk_area': huge_area, 'rim_alpha': 1, 'budget': 1},
        'factor_overflow': {'requested_alpha': minsub, 'requested_offset': minsub,
                            'coarsened_alpha': coarse_alpha, 'rounded_offset': float(exact_offset(minsub))},
        'offset_ratio_overflow': {'requested_alpha': minsub, 'requested_offset': wide_offset,
                                  'coarsened_alpha': coarse_alpha, 'rounded_offset': float(exact_offset(wide_offset))},
        'wrap_frame': {'ordinary_alpha': ordinary_alpha, 'dyadic_alpha': dyadic_alpha,
                       'shift_y': shift_y, 'shift_z': shift_z, 'local_steiner_z': local_steiner_z,
                       'utm_steiner_z_mapped_back': utm_steiner_z},
    }


def run(timeout_ms=8000):
    source_contract(SOURCE, ANCHORS)
    source_contract(TERMS_SOURCE, TERMS_ANCHORS)
    rows = real_obligations(timeout_ms) + discriminant_range_obligations(timeout_ms) + weld_cell_obligations(timeout_ms)
    f64 = z3.Float64()
    ratio = z3.FP('uncapped_ratio', f64)
    maximum = z3.FPVal(1e6, f64)
    zero = z3.FPVal(0, f64)
    assumptions = [z3.fpGEQ(ratio, zero)]
    capped = z3.If(z3.fpGT(ratio, maximum), maximum, ratio)
    rows += [
        check_expr('ratio_cap_can_leave_nonfinite_or_out_of_range_value', assumptions,
                   z3.Or(z3.fpIsInf(capped), z3.fpIsNaN(capped), z3.fpGT(capped, maximum)),
                   'unsat', timeout_ms, 'QF_FP'),
        check_expr('omitting_ratio_cap_can_leave_infinity', assumptions, z3.fpIsInf(ratio),
                   'sat', timeout_ms, 'QF_FP'),
    ]
    replays, witnesses = replay_fp()
    result = report(rows, replays=replays, witnesses=witnesses)
    result['passed'] &= all(replays.values())
    return result


if __name__ == '__main__':
    raise SystemExit(main(run))
