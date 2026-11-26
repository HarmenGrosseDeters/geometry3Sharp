"""Follow-up lemmas and exact replays for the three pilot series."""
from fractions import Fraction
from itertools import product

import z3 as z

from pilot import F, RN, ROOT, check

U = Fraction(1, 2**53)
EPS = Fraction(1, 2**1074)


def real(value):
    return z.RealVal(str(Fraction(value)))


def abs_value(value):
    return z.If(value >= 0, value, -value)


def source_contains(path, *snippets):
    source = (ROOT / path).read_text(encoding='utf-8-sig')
    compact = ''.join(source.split())
    for snippet in snippets:
        if ''.join(snippet.split()) not in compact:
            raise ValueError(f'{path}: review the follow-up model near {snippet}')


def intrinsic_and_limb():
    source_contains('mesh_ops/IntrinsicIntegerCoordinates.cs',
                    'Math.Abs(cJlo - cJup) + Math.Abs(cIlo - cIup) - eL - eK')
    source_contains('math/ExactLimbSigns.cs',
                    'carry = (carry != 0 && t == 0) ? 1UL : 0UL')
    left_i, left_j, right_i, right_j, left_exit, right_exit, crossings = z.Ints(
        'left_i left_j right_i right_j left_exit right_exit crossings')
    left = left_i + left_j + left_exit
    right = right_i + right_j + right_exit
    doubled_odd_terms = abs_value(right_i - left_i) + abs_value(right_j - left_j) - left_exit - right_exit
    # Conservation counts each arc that enters the old diagonal once.
    # All other addends of flipped_coordinate_twice are already even.
    counts = [left == crossings, right == crossings]
    base, power, prefix, output, carry, word, value, next_carry = z.Ints(
        'base power prefix output carry word value next_carry')
    radix = [base == 2**64, power > 0,
             output + carry * power == power - prefix,
             value + next_carry * base == base - 1 - word + carry]
    next_prefix = prefix + word * power
    next_output = output + value * power
    return [
        ('intrinsic_conserved_flip_can_be_odd', counts, doubled_odd_terms % 2 != 0, 'unsat'),
        ('intrinsic_positive_counts_reachable', counts,
         z.And(crossings == 3, left_i == 1, left_j == 2, right_i == 3), 'sat'),
        ('limb_carry_invariant_fails_at_next_word', radix,
         next_output + next_carry * power * base != power * base - next_prefix, 'unsat'),
    ]


def volume_and_geodesic():
    source_contains('math/ExactPredicates.cs',
                    'return (n + 2) * Epsilon / (1 - (n + 2) * Epsilon)')
    source_contains('mesh_ops/ExactMeshGeodesicDistance.cs',
                    'double dP = (HalfWidth(PA.X) + HalfWidth(PA.Y)) * (1 + 1e-15) + 2 * (Ulp(px) + Ulp(py))',
                    'double lenLo = len * (1 - 4e-16) - dP - dQ')
    a = z.Real('nu')
    u = real(U)
    # Positive denominators clear the induction relation
    # [(1+gamma_n)(1+u)] <= [1+gamma_(n+1)].
    volume = [a >= 0, a + u < 1]
    widths, spacing, sum_widths, enlarged, correction, disk = z.Reals(
        'widths spacing sum_widths enlarged correction disk')
    expansion = real(1 + 1e-15)
    disks = [
        widths >= 0, spacing >= real(2 * EPS),
        sum_widths >= (1 - u) * widths,
        enlarged >= (1 - u) * expansion * sum_widths,
        correction >= 2 * (1 - u) * spacing,
        disk >= (1 - u) * (enlarged + correction),
    ]
    # Previous per-axis lemma needs half a center spacing plus one
    # subnormal step. The spacing is at least that step on each axis.
    eps = z.FPVal(float(EPS), F)
    subnormal_length = z.fpMul(RN, z.fpSqrt(RN, z.FPVal(8, F)), eps)
    adjusted = z.fpMul(RN, subnormal_length, z.FPVal(1 - 4e-16, F))
    return [
        ('volume_gamma_product_induction_fails', volume,
         (1 + u) * (1 - a - u) > 1 - a, 'unsat'),
        ('transform_endpoint_disks_can_underestimate', disks, disk < widths + 3 * spacing / 2, 'unsat'),
        ('transform_endpoint_disk_bridge_reachable', disks, disk > 0, 'sat'),
        ('geodesic_subnormal_adjustment_keeps_three_steps', [],
         z.fpEQ(adjusted, z.FPVal(3 * float(EPS), F)), 'sat'),
    ]


def contour_and_ccd():
    source_contains('mesh_ops/ContouringTopologyCertificate.cs',
                    'while (grid.Coord(axis, B * (m + 1)) < x) m++')
    source_contains('intersection/ContinuousCollision.cs',
                    'double a11 = (e0 + 1.0 * e1) - 1.0 * e2',
                    'if (lo > threshold || hi < -threshold)')
    p, q, r, s = z.Reals('p q r s')
    # On the domain surface, the endpoint sums of the two sheet field
    # values are negative. This survives affine interpolation.
    theta = z.Real('theta')
    g = p + theta * (r - p)
    h = q + theta * (s - q)
    contour = [0 <= theta, theta <= 1, p + q < 0, r + s < 0]
    low, high = z.Ints('low high')
    mid = low + (high - low) / 2
    search = [-(2**20) <= low, low < high, high <= 2**20]
    # At the CCD root, clipping [0,1]^2 by u+v<=1 retains
    # exactly the three corners visited by the VF shortcut; EE
    # retains all four. Boundary intersections only repeat corners.
    visited = {(u, v) for u, v in product(range(2), repeat=2) if u + v <= 1}
    assert visited == {(0, 0), (0, 1), (1, 0)}
    a, b, c, d, threshold = z.Reals('ccd_a ccd_b ccd_c ccd_d ccd_threshold')
    wa, wb, wc, wd = z.Reals('ccd_wa ccd_wb ccd_wc ccd_wd')
    # For VF these are barycentric weights and wd=0. For EE they
    # are the bilinear weights. Combining both t endpoints uses
    # the same statement with two nonzero weights.
    domain = [wa >= 0, wb >= 0, wc >= 0, wd >= 0, wa + wb + wc + wd == 1]
    combined = wa * a + wb * b + wc * c + wd * d
    return [
        ('two_sheet_outsides_can_overlap_on_segment', contour, z.And(g >= 0, h >= 0), 'unsat'),
        ('two_sheet_interpolation_reachable', contour, z.And(theta == real(Fraction(1, 2)), g < 0, h < 0), 'sat'),
        ('bounded_bin_search_cannot_shrink', search,
         z.Or(mid < low, mid >= high, 2 * (mid - low) > high - low, 2 * (high - (mid + 1)) > high - low), 'unsat'),
        ('ccd_root_positive_corners_can_have_nonstrict_interior',
         domain + [a > threshold, b > threshold, c > threshold, d > threshold],
         combined <= threshold, 'unsat'),
        ('ccd_root_negative_corners_can_have_nonstrict_interior',
         domain + [a < -threshold, b < -threshold, c < -threshold, d < -threshold],
         combined >= -threshold, 'unsat'),
    ]


def ray_replay():
    source_contains('queries/IntersectionUtil.cs',
                    'ExactRayTriangle.SecondPoint(rayOrigin, rayDirection)')
    source_contains('math/ExactRayTriangle.cs',
                    'm = exponent(mo) - exponent(md) + 1')
    # Units of binary64's smallest spacing. The exact ray is
    # (1,0,0)+t*(1,4,0), crosses y=8 at (3,8,0), and the triangle
    # has xz vertices (2,-2), (5,-2), (2,2).
    t = Fraction(8, 4)
    x = 1 + t
    u, v = (x - 2) / 3, Fraction(1, 2)
    assert u > 0 and v > 0 and u + v < 1
    source_x = float(EPS) + float(EPS) * .5
    source_y = 4 * float(EPS) * .5
    assert source_x == float(EPS) and source_y == 2 * float(EPS)
    assert Fraction(source_x) / EPS < 2
    # The representation also cannot encode all forward exact rays:
    # there is no finite binary64 point ahead of MaxValue along +x.
    maxval = z.FPVal(float.fromhex('0x1.fffffffffffffp+1023'), F)
    p = z.FP('forward_point', F)
    # The exact direction formulation can use the usual three scalar
    # triple products. Their sum is the signed dot with the plane
    # normal, independent of the chosen origin.
    direction = z.Reals('ray_dx ray_dy ray_dz')
    a = z.Reals('ray_ax ray_ay ray_az')
    b = z.Reals('ray_bx ray_by ray_bz')
    c = z.Reals('ray_cx ray_cy ray_cz')

    def triple(x, y, w):
        return sum(x[i] * (y[(i + 1) % 3] * w[(i + 2) % 3]
                           - y[(i + 2) % 3] * w[(i + 1) % 3]) for i in range(3))

    signed_sum = triple(direction, a, b) + triple(direction, b, c) + triple(direction, c, a)
    plane_dot = triple(direction, [y - x for x, y in zip(a, b)], [y - x for x, y in zip(a, c)])
    rows = [
        ('exact_direction_edge_sum_disagrees_with_plane_dot', [],
         z.simplify(signed_sum - plane_dot, som=True) != 0, 'unsat'),
        ('finite_second_point_ahead_of_max_value', [z.Not(z.fpIsInf(p)), z.Not(z.fpIsNaN(p))],
         z.fpGT(p, maxval), 'unsat'),
    ]
    return rows, dict(exact_hit_units=[int(x), 8, 0],
                      barycentrics=[str(1 - u - v), str(u), str(v)],
                      constructed_ray_x_units=int(Fraction(source_x) / EPS))


def storage_and_bounds():
    source_contains('mesh_ops/ExactMeshArrangement.cs',
                    'return (c != 0) ? c : x.Line.CompareTo(y.Line)',
                    'if (seen.Add(EdgeKey(s.P, s.Q))) unique.Add(s)',
                    'T.AddPartner(ui)')
    source_contains('intersection/IntrExactBox3.cs',
                    't = tn - 3 * U * tn - AbsErr')
    source_contains('mesh_ops/TerrainSlopeDesign.cs',
                    'box2.Expand(1.05 * dMax + 1e-6 * toeBox.MaxDim)')
    label_flat, label_plane = z.Ints('label_flat label_plane')
    retained = z.If(label_flat < label_plane, label_flat, label_plane)
    normal, exact, pad, reduced = z.Reals('rounded_entry exact_entry pad reduced_entry')
    u = real(U)
    # Usual relative bounds on normal intermediates only. The final
    # AbsErr subtraction and clamping to zero cannot raise the bound
    # above a nonnegative exact entry.
    finite = [
        exact >= 0, normal >= 0,
        normal <= real((1 + U)**2) * exact,
        pad >= 3 * u * (1 - u) * normal, pad <= normal,
        reduced <= (1 + u) * (normal - pad),
    ]
    reach = 5.056130373333055
    box_edge = float(2**52)
    stored = box_edge + (1.05 * reach + 1e-6 * 20_000)
    outward = float.fromhex('0x1.0000000000006p+52')
    assert Fraction(stored) - Fraction(box_edge) == 5
    assert Fraction(stored) < Fraction(box_edge) + Fraction(reach) <= Fraction(outward)
    return [
        ('coplanar_fold_loses_triangle_plane_label', [label_flat == 5, label_plane == 8],
         z.And(retained % 4 != 0, label_plane % 4 == 0), 'sat'),
        ('normal_ray_entry_lower_bound_can_exceed_exact_entry', finite, reduced > exact, 'unsat'),
        ('normal_ray_entry_can_be_positive', finite, z.And(reduced > 0, exact > 0), 'sat'),
    ]


def envelope_and_residual():
    source_contains('mesh_ops/PolyhedralEnvelope.cs',
                    'const double Tiny = 1e-300',
                    'double bound = 5 * U * (Math.Abs(m.x * q.x) +',
                    'mu = Math.Max(mu, Math.Abs(r) + bound + Tiny)')
    computed = z.Reals('computed_p0 computed_p1 computed_p2')
    exact = z.Reals('exact_p0 exact_p1 exact_p2')
    plane, first, second, residual, magnitude, bound, mu0, mu = z.Reals(
        'plane first second residual magnitude bound mu0 mu')
    u = real(U)

    def rounded(got, target):
        return abs_value(got - target) <= u * abs_value(target)

    scale = sum(abs_value(term) for term in computed) + abs_value(plane)
    assumptions = [
        abs_value(a - b) <= real(U / (1 - U)) * abs_value(a) + real(EPS / (2 * (1 - U)))
        for a, b in zip(computed, exact)
    ]
    assumptions += [
        rounded(first, computed[0] + computed[1]),
        rounded(second, first + computed[2]),
        rounded(residual, second - plane),
        magnitude >= real((1 - U)**3) * scale, bound >= 0,
        mu0 >= (1 - u) * (abs_value(residual) + bound),
        mu >= (1 - u) * (mu0 + real(1e-300)), mu >= mu0,
    ]
    # Independent rounding errors overapproximate actual binary64.
    # SAT for the current factor is a limit of this proof, not a
    # reachable false envelope certificate.
    def margin(factor):
        return bound >= real(factor * U * (1 - U)) * magnitude - real(EPS / 2)

    exceed = abs_value(sum(exact) - plane) > mu
    x, y, x_true, y_true, quot, x_err, y_err = z.Reals('x y xp yp quot x_err y_err')
    residual_premises = [abs_value(x - x_true) <= x_err, abs_value(y - y_true) <= y_err,
                         x_err >= 0, y_err >= 0]
    return [
        ('current_facet_residual_factor_is_inconclusive_in_relaxed_model',
         assumptions + [margin(5)], exceed, 'sat'),
        ('six_unit_candidate_encloses_relaxed_residual',
         assumptions + [margin(6)], exceed, 'unsat'),
        ('double_word_residual_operand_propagation_fails', residual_premises,
         abs_value(x_true - quot * y_true) > abs_value(x - quot * y) + x_err + abs_value(quot) * y_err, 'unsat'),
    ]


def run(timeout_ms=120000):
    ray_rows, witness = ray_replay()
    rows = []
    for name, assumptions, bad, expected in intrinsic_and_limb() + volume_and_geodesic() + contour_and_ccd() + storage_and_bounds() + envelope_and_residual() + ray_rows:
        _, _, row = check(name, assumptions, bad, timeout_ms)
        row['expected'] = expected
        rows.append(row)
    return dict(passed=all(row['result'] == row['expected'] for row in rows),
                z3=z.get_version_string(), obligations=rows, ray_replay=witness)
