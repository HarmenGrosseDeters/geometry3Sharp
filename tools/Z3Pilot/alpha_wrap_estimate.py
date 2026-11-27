"""Local Z3 obligations, performance-cliff witnesses, and binary64 replays for AlphaWrapEstimate.

Investigates the documented Alpha Wrap budget and size-estimator cliffs (issues #1113, #1593,
#1726, and Thingi10K 40118 in docs/perf/ALPHA_DEFAULTS_800.md) together with extreme-scale
range obligations on mesh_ops/AlphaWrapEstimate.cs:

1. Grid-welding saturation and ULP equivalence (PrepareFrame / Analyze):
   - When span / cell > 2^54 (including SafeGridLimit = 1e18 in the [0, 2) normalized frame),
     cell < 0.5 * (span * 2^-52), strictly smaller than half a ULP at the normalized span:
     exact-coordinate bit keys agree with infinite-precision grid cells without Int64 clamp
     collapse.
   - Negative control (SAT): at span / cell = 2^50, distinct binary64 coordinates can still share
     a grid cell.
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
     w < 2 * alpha (too narrow for the alpha ball to enter) and length L > 4 * alpha where the
     axis-aligned opening diagonal sqrt(w^2 + L^2) exceeds OpeningFactor * alpha = 4 * alpha,
     doubling bulk area and adding rim triangles unnecessarily.
   - Thingi10K 40118 neck-closure cliff (ALPHA_DEFAULTS_800.md): with MaxAlphaCoarsening = c_max >= 1,
     UsedAlpha <= c_max * alpha_0, so any neck wider than c_max * alpha_0 stays open (UNSAT to seal);
     without a cap, budget coarsening k = sqrt(E / B) admits a neck-sealing witness (SAT).
4. Concrete binary64 replays pinning UTM+tiny-alpha Cell collapse, 2^-360 cubic volume underflow,
   2^350 cubic volume NaN + cross.Length overflow, 2^±600 LargestOpening overflow/underflow, and
   the documented 40118 budget/cap cliff.
"""
import math
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

ESTIMATE_SOURCE = 'mesh_ops/AlphaWrapEstimate.cs'
WRAP_SOURCE = 'mesh_ops/MeshAlphaWrap.cs'

ESTIMATE_ANCHORS = [
    ('public const double OpeningFactor = 4.0;', 'static int Pow2Exp(', '32d6ae063d08e88a5f09fc5466b5b34c33bf7b4fb876b5807261b809c2ec9b34'),
    ('static bool PrepareFrame(', 'public static double OpenArea(', 'f1408e0bcda17b4d8334f0e515d1d27206090482a3d21e2b8bf5a43336043f9a'),
    ('for (int root = 0; root < nv; ++root)', 'public static double LargestOpening(', '43fcbcfa4164396f661218ed4df1bc3dd78f6b9f6d6cc463670321bfcc4c6393'),
    ('static void Analyze(', 'int nv = ids.Count;', '312952cff7f1edc6ea6ff6d606a581c79f93b5699fc90cff88a6018615e236d2'),
]

WRAP_ANCHORS = [
    ('double cap = MaxAlphaCoarsening > 0 ?', 'UsedAlpha = alpha; UsedOffset = offset;', 'c94ce0bef20a4adb21db447954441da6f6d641d912ad26f65921b932a2bcb2f4'),
]


def grid_saturation_obligations():
    rows = []
    span, cell = z3.Reals('span cell')
    ulp = span * z3.Q(1, 1 << 52)
    sat_prem = [span > 0, cell > 0, span > cell * (1 << 54)]
    rows.append(('grid_saturation_assumptions_sat', sat_prem, span == 2, 'sat', 'QF_NRA'))
    rows.append((
        'grid_saturation_implies_sub_half_ulp',
        sat_prem,
        cell >= z3.Q(1, 2) * ulp,
        'unsat',
        'QF_NRA',
    ))
    # Negative control: below 2^52, a cell can exceed one ULP (welding distinct binary64 points)
    rows.append((
        'grid_below_threshold_can_weld_distinct_ulps_negative_control',
        [span > 0, cell > 0, span == cell * (1 << 50)],
        cell > ulp,
        'sat',
        'QF_NRA',
    ))

    # SafeGridLimit = 1e18 < 9e18 < Int64.MaxValue (~9.22e18): floor(q) never saturates Int64
    q = z3.Int('q')
    limit = 10**18
    int64_max = (1 << 63) - 1
    bound_prem = [-limit <= q, q <= limit]
    rows.append(('safe_grid_limit_assumptions_sat', bound_prem, q > 0, 'sat', 'QF_LIA'))
    rows.append((
        'safe_grid_limit_within_int64',
        bound_prem,
        z3.Or(q > 9 * 10**18, q < -9 * 10**18, q >= int64_max),
        'unsat',
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
    #    A narrow slit w < 2*alpha (ball cannot enter -> 1-sided wrap) with length L > 4*alpha
    #    has opening diagonal sqrt(w^2 + L^2) > OpeningFactor * alpha (4*alpha) -> doubles estimate.
    w, L, alpha = z3.Reals('w L alpha')
    rows.append((
        'documented_cliff_narrow_slit_classified_open_witness',
        [alpha > 0, w > 0, L > 0, w < z3.Q(1, 2) * alpha],
        w * w + L * L > (4 * alpha) * (4 * alpha),
        'sat',
        'QF_NRA',
    ))

    # 2. Documented 40118 neck-closure cliff (docs/perf/ALPHA_DEFAULTS_800.md):
    #    Cavity behind neck of width w_neck > alpha0: under MaxAlphaCoarsening = c_max >= 1,
    #    used_alpha <= c_max * alpha0, so any neck wider than c_max * alpha0 cannot be sealed.
    alpha0, w_neck, c_max, coarse = z3.Reals('alpha0 w_neck c_max coarse')
    used = z3.If(coarse > c_max * alpha0, c_max * alpha0, coarse)
    neck_prem = [alpha0 > 0, c_max >= 1, coarse >= alpha0, w_neck > c_max * alpha0]
    rows.append(('neck_cap_assumptions_sat', neck_prem, c_max == z3.Q(6, 5), 'sat', 'QF_NRA'))
    rows.append((
        'coarsening_cap_preserves_neck_above_cap',
        neck_prem,
        used >= w_neck,
        'unsat',
        'QF_NRA',
    ))
    # Negative control (SAT): without MaxAlphaCoarsening, budget coarsening k = sqrt(E / B) seals the neck
    E, B, k = z3.Reals('E B k')
    rows.append((
        'uncapped_budget_seals_neck_cliff_negative_control',
        [alpha0 > 0, E > B, B > 0, k > 1, k * k * B == E, w_neck > alpha0, w_neck == z3.Q(3, 2) * alpha0],
        k * alpha0 > w_neck,
        'sat',
        'QF_NRA',
    ))
    return rows


def replay_fp():
    replays = []

    # 1. UTM (5e6) + tiny alpha (1e-12): old Cell saturates at 9e18 and merges all vertices; origin-shifted stays distinct
    x0, x1, alpha = 5e6, 5e6 + 1.0, 1e-12
    cell = alpha / 8.0
    old_c0 = int(max(-9e18, min(9e18, math.floor(x0 / cell))))
    old_c1 = int(max(-9e18, min(9e18, math.floor(x1 / cell))))
    new_c0 = int(math.floor((x0 - x0) / cell))
    new_c1 = int(math.floor((x1 - x0) / cell))
    replays.append(dict(
        name='utm_tiny_alpha_cell_collapse',
        old_collapsed=(old_c0 == old_c1),
        new_distinct=(new_c0 != new_c1),
        passed=(old_c0 == old_c1) and (new_c0 != new_c1),
    ))

    # 2. Cubic volume underflow at 2^-360: s^2 normal, s^3 underflows to 0 -> thick cube misclassified as thin
    s_small = math.ldexp(1.0, -360)
    old_area = 6.0 * (s_small * s_small)
    old_vol = (s_small * s_small) * s_small
    a_small = 0.05 * s_small
    old_false_thin = (old_area > 0.0) and (2.0 * abs(old_vol) / old_area < 0.2 * a_small)
    # Normalized frame (scaleExp = -360): s' = 1, V' = 1, A' = 6, alpha' = 0.05 -> 2/6 = 0.333 >= 0.01 (thick)
    new_thin = (2.0 * 1.0 / 6.0) < (0.2 * 0.05)
    replays.append(dict(
        name='cubic_volume_underflow_2m360',
        old_area_positive=(old_area > 0.0),
        old_vol=old_vol,
        old_false_thin=old_false_thin,
        new_thin=new_thin,
        passed=old_false_thin and (not new_thin),
    ))

    # 3. Cubic volume overflow (+Inf + -Inf = NaN) and cross.Length overflow (+Inf -> normal = 0 -> false sharp) at 2^350
    s_big = math.ldexp(1.0, 350)
    vol_pos = (s_big * s_big) * s_big
    vol_sum = vol_pos + (-vol_pos)
    cross_mag = s_big * s_big
    old_cross_len = math.sqrt(cross_mag * cross_mag)
    old_normal_x = cross_mag / old_cross_len  # finite / +Inf = 0.0 -> dot = 0 < SharpCos -> every edge looks sharp!
    replays.append(dict(
        name='cubic_volume_nan_and_cross_inf_2p350',
        old_vol_nan=math.isnan(vol_sum),
        old_cross_inf=math.isinf(old_cross_len),
        old_normal_zero=(old_normal_x == 0.0),
        passed=math.isnan(vol_sum) and math.isinf(old_cross_len) and (old_normal_x == 0.0),
    ))

    # 4. LargestOpening at 2^600 and 2^-600: old .Length gives +Inf and 0.0
    d_big = math.ldexp(1.0, 600)
    d_small = math.ldexp(1.0, -600)
    old_open_big = math.sqrt(d_big * d_big + d_big * d_big)
    old_open_small = math.sqrt(d_small * d_small + d_small * d_small)
    new_open_big = math.ldexp(math.sqrt(2.0), 600)
    new_open_small = math.ldexp(math.sqrt(2.0), -600)
    replays.append(dict(
        name='largest_opening_2p600_and_2m600',
        old_big_inf=math.isinf(old_open_big),
        old_small_zero=(old_open_small == 0.0),
        new_finite=math.isfinite(new_open_big) and (new_open_small > 0.0),
        passed=math.isinf(old_open_big) and (old_open_small == 0.0) and math.isfinite(new_open_big) and (new_open_small > 0.0),
    ))

    # 5. Thingi10K 40118 budget cliff replay (docs/perf/ALPHA_DEFAULTS_800.md: E = 2.70M, B = 300k, neck cliff in (1.22, 1.50))
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
