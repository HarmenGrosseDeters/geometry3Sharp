"""Local Z3 obligations, performance-cliff witnesses, and binary64 replays for AlphaWrapEstimate.

Investigates the documented Alpha Wrap budget and size-estimator cliffs (issues #1113, #1593,
#1726, and Thingi10K 40118 in docs/perf/ALPHA_DEFAULTS_800.md) together with extreme-scale
range obligations on mesh_ops/AlphaWrapEstimate.cs:

1. Grid-welding exact-integer bound and ULP behavior (PrepareFrame / Analyze / Cell):
   - SafeGridLimit = 2^52 keeps |v / cell| inside binary64's exact integer range (ulp <= 1,
     strictly below 2^53 and Int64.MaxValue) so Cell(v, cell) neither loses unit spacing nor
     saturates at ±9e18.
   - Negative control (SAT): a limit of 1e18 exceeds 2^53 (binary64 exact integers), allowing
     distinct coordinates up to 16 * alpha apart to collapse in Cell.
   - When span / cell > 2^54, cell < 0.5 * (span * 2^-52), strictly smaller than half a ULP for
     coordinates of magnitude Theta(span) (in [1, 2) of the normalized frame); exact-bit welding
     agrees with infinite-precision cells on [1, 2) once workCell < 2^-52 (2 / workCell > 2^53),
     and acts as a conservative non-collapsing fallback in [2^-52, 2^-51) before v / workCell
     exceeds binary64's exact integer range (not claiming exact equivalence near lo).
   - Negative control (SAT): at span / cell = 2^50, a grid cell still spans multiple ULPs.
2. Thin-plate classifier scale invariance and geometry bounds (Terms):
   - Under positive rescaling s > 0 (V = s^3 V', A = s^2 A', alpha = s alpha'),
     2|V'|/A' < ThinFactor * alpha' iff 2|V|/A < ThinFactor * alpha.
   - Negative control (SAT): omitting the scaling of alpha breaks invariance.
   - Closed slab of thickness h, face area F, perimeter P: 2V/A = 2Fh / (2F + Ph) < h, so
     h <= ThinFactor * alpha guarantees thin classification.
   - Closed cube of side L: 2V/A = L/3, so L >= 3 * ThinFactor * alpha = 0.6 * alpha guarantees
     thick classification.
   - Disc-rim lower bound: ThinRim = max(0.5 * sharp, 2 * sqrt(pi * 0.5 * A)) >= 2 * sqrt(pi * 0.5 * A)
     even when sharp == 0 (welded thin-plate rim edges with uses > 2).
3. Documented performance cliffs:
   - Long narrow slit cliff (AlphaWrapEstimate.cs:34): Z3 synthesizes a SAT witness with slit width
     w < 0.5 * alpha (too narrow for the alpha ball to enter) and length L > 4 * alpha where the
     axis-aligned opening diagonal sqrt(w^2 + L^2) exceeds OpeningFactor * alpha = 4 * alpha,
     doubling bulk area and adding rim triangles unnecessarily.
   - Thingi10K 40118 neck-closure cliff (ALPHA_DEFAULTS_800.md, MeshAlphaWrap.cs:144): the wrap
     closes an opening narrower than about 2 * UsedAlpha. With MaxAlphaCoarsening = c_max >= 1,
     UsedAlpha <= c_max * alpha_0, so any neck of full width w_neck > 2 * c_max * alpha_0 stays
     open (UNSAT for 2 * UsedAlpha >= w_neck); without a cap, budget coarsening k = sqrt(E / B)
     admits a neck-sealing witness (SAT).
4. Concrete binary64 replays mirroring PrepareFrame, SafeLength, SafeUnit, and Cell at UTM + 1e-12,
   the SafeGridLimit = 2^52 vs 1e18 fast-path and normalized-path counterexamples, 2^-360 cubic
   volume underflow, 2^350 cubic volume NaN + cross.Length overflow, 2^±600 LargestOpening, and
   the documented 40118 budget/cap cliff.
"""
import math
import struct
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

ESTIMATE_SOURCE = 'mesh_ops/AlphaWrapEstimate.cs'
WRAP_SOURCE = 'mesh_ops/MeshAlphaWrap.cs'

ESTIMATE_ANCHORS = [
    ('public const double OpeningFactor = 4.0;', 'static int Pow2Exp(', '48455ac1f4c74f5a9dd54ba138d80f009c507a2a0b917dab2b679c19a92d6d29'),
    ('static int Pow2Exp(', 'static long NormBits(', '53d6b9eb1c20797a17e100a5db734841b37dd5b434f98381f4a87291281b0a73'),
    ('static long NormBits(', 'public static double OpenArea(', 'f6c165e7536c359263f95ac40c8c455b1e5f2b6c3952744242b393c5cf6e107a'),
    ('public static readonly double ThinFactor = 0.2;', 'public static EstimateTerms Terms(', 'dca2a8cccf779bbb923b1cc4d94d24c92bc00f6879c18c4a15775977abe90ab2'),
    ('for (int root = 0; root < nv; ++root)', 'public static double LargestOpening(', '125cb92e84bfe9f67f61a15102bf1dd6443a3217822806b369118acb9d80c886'),
    ('static void Analyze(', 'int nv = ids.Count;', '312952cff7f1edc6ea6ff6d606a581c79f93b5699fc90cff88a6018615e236d2'),
    ('static long Cell(', 'static void CountEdge(', '56728091ee5be31c5c6e3e51435effbe097fb11fa72f9cb4750b8ed7d4168d76'),
]

WRAP_ANCHORS = [
    ('if (MaxEstimatedTriangles > 0 && EstimatedTriangles > MaxEstimatedTriangles) {', 'UsedAlpha = alpha; UsedOffset = offset;', '719f8fbd756e6581e3e1596776b988f09da90c6f3753841950563afd11405cc0'),
]


def grid_saturation_obligations():
    rows = []
    span, cell = z3.Reals('span cell')
    ulp_at_span = span * z3.Q(1, 1 << 52)
    sat_prem = [span > 0, cell > 0, span > cell * (1 << 54)]
    rows.append(('grid_saturation_assumptions_sat', sat_prem, span == 2, 'sat', 'QF_NRA'))
    rows.append((
        'grid_saturation_implies_sub_half_ulp_at_span',
        sat_prem,
        cell >= z3.Q(1, 2) * ulp_at_span,
        'unsat',
        'QF_NRA',
    ))
    # Negative control: below 2^52, a cell can exceed one ULP at magnitude Theta(span)
    rows.append((
        'grid_below_threshold_can_weld_distinct_ulps_negative_control',
        [span > 0, cell > 0, span == cell * (1 << 50)],
        cell > ulp_at_span,
        'sat',
        'QF_NRA',
    ))

    # SafeGridLimit = 2^52 keeps |v / cell| in binary64's exact integer range (< 2^53) and below ±9e18 / Int64.MaxValue
    q = z3.Int('q')
    safe_limit = 1 << 52
    b64_exact_int = 1 << 53
    int64_max = (1 << 63) - 1
    bound_prem = [-safe_limit <= q, q <= safe_limit]
    rows.append(('safe_grid_limit_assumptions_sat', bound_prem, q == safe_limit, 'sat', 'QF_LIA'))
    rows.append((
        'safe_grid_limit_exact_binary64_and_int64',
        bound_prem,
        z3.Or(q >= b64_exact_int, q <= -b64_exact_int, q > 9 * 10**18, q < -9 * 10**18, q >= int64_max),
        'unsat',
        'QF_LIA',
    ))
    # Negative control: a 1e18 limit admits quotients > 2^53 where binary64 ulp(v / cell) >= 2
    rows.append((
        'grid_limit_1e18_exceeds_binary64_exact_int_negative_control',
        [-10**18 <= q, q <= 10**18],
        q > b64_exact_int,
        'sat',
        'QF_LIA',
    ))
    return rows


def thin_plate_obligations():
    rows = []
    thin = z3.Q(1, 5)  # ThinFactor = 0.2

    # 1. Scale invariance of 2 * |V| / A < ThinFactor * alpha under s > 0
    s, vp, ap, alp = z3.Reals('s vp ap alp')
    scale_prem = [s > 0, vp >= 0, ap > 0, alp > 0]
    v = s * s * s * vp
    a = s * s * ap
    al = s * alp
    rows.append(('thin_scale_assumptions_sat', scale_prem, 2 * vp < thin * alp * ap, 'sat', 'QF_NRA'))
    rows.append((
        'thin_plate_classifier_scale_invariant',
        scale_prem,
        z3.Xor(2 * vp < thin * alp * ap, 2 * v < thin * al * a),
        'unsat',
        'QF_NRA',
    ))
    # Negative control: forgetting to scale alpha breaks classification invariance
    rows.append((
        'thin_plate_unscaled_alpha_negative_control',
        scale_prem,
        z3.Xor(2 * vp < thin * alp * ap, 2 * v < thin * alp * a),
        'sat',
        'QF_NRA',
    ))

    # 2. Slab geometry bound: A = 2*F + P*h, V = F*h => 2*V / A < h
    F, P, h, alpha = z3.Reals('F P h alpha')
    slab_prem = [F > 0, P > 0, h > 0, alpha > 0, h <= thin * alpha]
    rows.append(('slab_assumptions_sat', slab_prem, h == thin * alpha, 'sat', 'QF_NRA'))
    rows.append((
        'slab_below_thin_factor_always_classified_thin',
        slab_prem,
        2 * (F * h) >= thin * alpha * (2 * F + P * h),
        'unsat',
        'QF_NRA',
    ))

    # 3. Cube geometry bound: A = 6*L^2, V = L^3 => 2*V / A = L / 3 >= 0.2 * alpha iff L >= 0.6 * alpha
    L = z3.Real('L')
    cube_prem = [L > 0, alpha > 0, L >= z3.Q(3, 5) * alpha]
    rows.append(('cube_assumptions_sat', cube_prem, L == alpha, 'sat', 'QF_NRA'))
    rows.append((
        'cube_above_0p6_alpha_never_classified_thin',
        cube_prem,
        2 * (L * L * L) < thin * alpha * (6 * L * L),
        'unsat',
        'QF_NRA',
    ))

    # 4. Disc-rim lower bound: max(0.5 * sharp, disc) >= disc even when sharp == 0
    sharp, disc = z3.Reals('sharp disc')
    rim_prem = [sharp >= 0, disc > 0]
    rim = z3.If(z3.Q(1, 2) * sharp >= disc, z3.Q(1, 2) * sharp, disc)
    rows.append(('thin_rim_assumptions_sat', rim_prem, sharp == 0, 'sat', 'QF_NRA'))
    rows.append((
        'thin_rim_at_least_disc_perimeter',
        rim_prem,
        rim < disc,
        'unsat',
        'QF_NRA',
    ))
    return rows


def performance_cliff_obligations():
    rows = []

    # 1. Documented slit over-estimation cliff (AlphaWrapEstimate.cs:34):
    #    A narrow slit w < 0.5 * alpha (ball cannot enter -> 1-sided wrap) with length L > 4 * alpha
    #    has opening diagonal sqrt(w^2 + L^2) > OpeningFactor * alpha (4 * alpha) -> doubles estimate.
    w, L, alpha = z3.Reals('w L alpha')
    rows.append((
        'documented_cliff_narrow_slit_classified_open_witness',
        [alpha > 0, w > 0, L > 0, w < z3.Q(1, 2) * alpha],
        w * w + L * L > (4 * alpha) * (4 * alpha),
        'sat',
        'QF_NRA',
    ))

    # 2. Documented 40118 neck-closure cliff (docs/perf/ALPHA_DEFAULTS_800.md, MeshAlphaWrap.cs:144):
    #    The wrap closes an opening narrower than about 2 * UsedAlpha. Under MaxAlphaCoarsening = c_max >= 1,
    #    UsedAlpha <= c_max * alpha0, so any neck of full width w_neck > 2 * c_max * alpha0 stays open.
    alpha0, w_neck, c_max, coarse = z3.Reals('alpha0 w_neck c_max coarse')
    used = z3.If(coarse > c_max * alpha0, c_max * alpha0, coarse)
    neck_prem = [alpha0 > 0, c_max >= 1, coarse >= alpha0, w_neck > 2 * c_max * alpha0]
    rows.append(('neck_cap_assumptions_sat', neck_prem, c_max == z3.Q(6, 5), 'sat', 'QF_NRA'))
    rows.append((
        'coarsening_cap_preserves_neck_above_cap',
        neck_prem,
        2 * used >= w_neck,
        'unsat',
        'QF_NRA',
    ))
    # Negative control (SAT): without MaxAlphaCoarsening, budget coarsening k = sqrt(E / B) seals a 3 * alpha0 neck
    E, B, k = z3.Reals('E B k')
    rows.append((
        'uncapped_budget_seals_neck_cliff_negative_control',
        [alpha0 > 0, E > B, B > 0, k > 1, k * k * B == E, w_neck == 3 * alpha0],
        2 * k * alpha0 > w_neck,
        'sat',
        'QF_NRA',
    ))
    return rows


# ---- Python binary64 mirrors of AlphaWrapEstimate helpers for replay_fp() ----

_MIN_NORMAL = 2.2250738585072014e-308  # 2^-1022
_SAFE_GRID_LIMIT = float(1 << 52)


def _pow2_exp(v):
    bits = struct.unpack('<Q', struct.pack('<d', v))[0]
    biased = (bits >> 52) & 0x7FF
    if biased != 0:
        return biased - 1023
    sbits = struct.unpack('<Q', struct.pack('<d', v * float(1 << 54)))[0]
    return ((sbits >> 52) & 0x7FF) - 1023 - 54


def _max_abs3(vx, vy, vz):
    if math.isnan(vx) or math.isnan(vy) or math.isnan(vz):
        return float('nan')
    return max(abs(vx), abs(vy), abs(vz))


def _safe_length(vx, vy, vz):
    d2 = vx * vx + vy * vy + vz * vz
    if d2 >= 4.0 * _MIN_NORMAL and not math.isinf(d2):
        return math.sqrt(d2)
    m = _max_abs3(vx, vy, vz)
    if math.isnan(m):
        return float('nan')
    if m == 0.0:
        return 0.0
    if math.isinf(m):
        return float('inf')
    e = _pow2_exp(m)
    sx, sy, sz = math.ldexp(vx, -e), math.ldexp(vy, -e), math.ldexp(vz, -e)
    return math.ldexp(math.sqrt(sx * sx + sy * sy + sz * sz), e)


def _safe_unit(vx, vy, vz):
    m = _max_abs3(vx, vy, vz)
    if not (m > 0.0) or math.isinf(m):
        return None
    e = _pow2_exp(m)
    sx, sy, sz = math.ldexp(vx, -e), math.ldexp(vy, -e), math.ldexp(vz, -e)
    l = math.sqrt(sx * sx + sy * sy + sz * sz)
    return (sx / l, sy / l, sz / l)


def _cell(v, cell):
    return int(max(-9e18, min(9e18, math.floor(v / cell))))


def _norm_bits(d):
    return struct.unpack('<q', struct.pack('<d', 0.0 if d == 0.0 else d))[0]


def replay_fp():
    replays = []

    # 1. UTM (5e6) + tiny alpha (1e-12): old Cell saturates at 9e18 and merges all vertices; origin-shifted stays distinct
    x0, x1, alpha = 5e6, 5e6 + 1.0, 1e-12
    cell = alpha / 8.0
    old_c0, old_c1 = _cell(x0, cell), _cell(x1, cell)
    new_c0, new_c1 = _cell(x0 - x0, cell), _cell(x1 - x0, cell)
    replays.append(dict(
        name='utm_tiny_alpha_cell_collapse',
        old_collapsed=(old_c0 == old_c1),
        new_distinct=(new_c0 != new_c1),
        passed=(old_c0 == old_c1) and (new_c0 != new_c1),
    ))

    # 2. SafeGridLimit = 2^52 vs 1e18 binary64 ulp>1 counterexamples (fast path and normalized path)
    side30 = math.ldexp(1.0, -30)
    utm_a, utm_b = 7_500_000.0, 7_500_000.0 + side30
    utm_cell = 1e-10 / 8.0
    old_utm_collapse = (_cell(utm_a, utm_cell) == _cell(utm_b, utm_cell))  # 6e17 in [2^53, 1e18]
    # Under SafeGridLimit = 2^52: 7_500_000 / utm_cell > 2^52 -> shifts by lo and rescales by Pow2Exp(side30) = -30
    utm_exp = _pow2_exp(side30)
    new_utm_cell = math.ldexp(1e-10, -utm_exp) / 8.0
    new_utm_distinct = (_cell(math.ldexp(utm_a - utm_a, -utm_exp), new_utm_cell)
                        != _cell(math.ldexp(utm_b - utm_a, -utm_exp), new_utm_cell))
    eps52 = math.ldexp(1.0, -52)
    norm_a, norm_b = 1.5, 1.5 + eps52
    norm_cell = 1.6e-17 / 8.0  # 2 / norm_cell = 1e18 > 2^52
    old_norm_collapse = (_cell(norm_a, norm_cell) == _cell(norm_b, norm_cell))
    new_norm_exact = (2.0 / norm_cell > _SAFE_GRID_LIMIT) and (_norm_bits(norm_a) != _norm_bits(norm_b))
    replays.append(dict(
        name='safe_grid_limit_2p52_vs_1e18_counterexamples',
        old_utm_collapse=old_utm_collapse,
        new_utm_distinct=new_utm_distinct,
        old_norm_collapse=old_norm_collapse,
        new_norm_exact=new_norm_exact,
        passed=old_utm_collapse and new_utm_distinct and old_norm_collapse and new_norm_exact,
    ))

    # 3. Cubic volume underflow at 2^-360: old s^3 underflows to 0 -> false thin; PrepareFrame ldexp keeps thick
    s_small = math.ldexp(1.0, -360)
    a_small = 0.05 * s_small
    old_area = 6.0 * (s_small * s_small)
    old_vol = (s_small * s_small) * s_small
    old_false_thin = (old_area > 0.0) and (2.0 * abs(old_vol) / old_area < 0.2 * a_small)
    exp_s = _pow2_exp(s_small)
    ws = math.ldexp(s_small, -exp_s)
    wa = math.ldexp(a_small, -exp_s)
    new_area = 6.0 * (ws * ws)
    new_vol = (ws * ws) * ws
    new_thin = (new_area > 0.0) and (2.0 * abs(new_vol) / new_area < 0.2 * wa)
    replays.append(dict(
        name='cubic_volume_underflow_2m360',
        old_area_positive=(old_area > 0.0),
        old_vol=old_vol,
        old_false_thin=old_false_thin,
        new_thin=new_thin,
        passed=old_false_thin and (not new_thin),
    ))

    # 4. Cubic volume NaN and cross.Length overflow at 2^350: old fails, PrepareFrame + SafeLength + SafeUnit succeed
    s_big = math.ldexp(1.0, 350)
    vol_pos = (s_big * s_big) * s_big
    vol_sum = vol_pos + (-vol_pos)
    cross_mag = s_big * s_big
    old_cross_len = math.sqrt(cross_mag * cross_mag)
    old_normal_x = cross_mag / old_cross_len
    exp_b = _pow2_exp(s_big)
    wb = math.ldexp(s_big, -exp_b)
    new_vol_sum = ((wb * wb) * wb) + (-((wb * wb) * wb))
    new_cross = wb * wb
    new_len = _safe_length(new_cross, 0.0, 0.0)
    new_unit = _safe_unit(new_cross, 0.0, 0.0)
    replays.append(dict(
        name='cubic_volume_nan_and_cross_inf_2p350',
        old_vol_nan=math.isnan(vol_sum),
        old_cross_inf=math.isinf(old_cross_len),
        old_normal_zero=(old_normal_x == 0.0),
        new_ok=(new_vol_sum == 0.0) and (new_len == 1.0) and (new_unit == (1.0, 0.0, 0.0)),
        passed=(math.isnan(vol_sum) and math.isinf(old_cross_len) and (old_normal_x == 0.0)
                and (new_vol_sum == 0.0) and (new_len == 1.0) and (new_unit == (1.0, 0.0, 0.0))),
    ))

    # 5. LargestOpening at 2^600 and 2^-600: old .Length gives +Inf and 0.0; SafeLength mirror stays finite and scaled
    d_big = math.ldexp(1.0, 600)
    d_small = math.ldexp(1.0, -600)
    old_open_big = math.sqrt(d_big * d_big + d_big * d_big)
    old_open_small = math.sqrt(d_small * d_small + d_small * d_small)
    new_open_big = _safe_length(d_big, d_big, 0.0)
    new_open_small = _safe_length(d_small, d_small, 0.0)
    new_exact = (math.isfinite(new_open_big) and abs(new_open_big - math.ldexp(math.sqrt(2.0), 600)) == 0.0
                 and (new_open_small > 0.0) and abs(new_open_small - math.ldexp(math.sqrt(2.0), -600)) == 0.0)
    replays.append(dict(
        name='largest_opening_2p600_and_2m600',
        old_big_inf=math.isinf(old_open_big),
        old_small_zero=(old_open_small == 0.0),
        new_finite=new_exact,
        passed=math.isinf(old_open_big) and (old_open_small == 0.0) and new_exact,
    ))

    # 6. Thingi10K 40118 budget cliff replay (docs/perf/ALPHA_DEFAULTS_800.md: E = 2.70M, B = 300k, neck cliff in (1.22, 1.50))
    est_40118, budget_40118 = 2_700_000.0, 300_000.0
    k_uncapped = math.sqrt(est_40118 / budget_40118)
    k_capped = min(k_uncapped, 1.2)
    replays.append(dict(
        name='mesh_40118_neck_cliff_replay',
        k_uncapped=k_uncapped,
        k_capped=k_capped,
        passed=(k_uncapped > 1.5) and (k_capped < 1.22),
    ))
    return replays


def run(timeout_ms=120000):
    source_contract(ESTIMATE_SOURCE, ESTIMATE_ANCHORS)
    source_contract(WRAP_SOURCE, WRAP_ANCHORS)
    obligations = (
        grid_saturation_obligations()
        + thin_plate_obligations()
        + performance_cliff_obligations()
    )
    rows = [
        check_expr(name, prem, goal, expected, timeout_ms, logic)
        for name, prem, goal, expected, logic in obligations
    ]
    replays = replay_fp()
    out = report(rows, replays=replays)
    out['passed'] = out['passed'] and all(r['passed'] for r in replays)
    return out


if __name__ == '__main__':
    sys.exit(main(run))
