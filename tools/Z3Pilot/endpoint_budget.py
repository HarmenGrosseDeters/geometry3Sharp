"""Arithmetic checks for the Exact Boolean endpoint budget, with source drift guards."""
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

SOURCE = 'mesh_ops/ExactMeshArrangement.cs'
ANCHORS = [
    ('if (EnableEarlyPieceBudgetCheck && MaxPieces > 0', 'int before = PointCount;',
     '8a21762bcb08a85004ae6bc5beb2f22ddc70cc61996d2fd04ad8adb778ad453d'),
    ('long endpoint_budget_lower_bound()', 'int[] touched_points(InputTriangle t)',
     '357a198f3c9cc97bcff81e548901fc764e3de38341f6615babceaf0495369b32'),
    ('int[] touched_points(InputTriangle t)', 'int predicted_pieces(int ti)',
     'effe7ed92ae242a0e162574a12f9a6ae2f93418b327d1af4b37172ddf8cf2ede'),
    ('int predicted_pieces(int ti)', 'void build_points()',
     '333ebbbba5662a9eff6161293d543edb46b9cd0b88af754c7713e03ddf60fc75'),
    ('void prepare_triangle(int ti)', 'void triangulate(int ti',
     'e08fbaeaf5b7ffa6737361e724997b73de5ff3859ea9c37554a104bb89802192'),
    ('void intersect(int ti, int ui)', '// Devillers and Guigue (2002), the proper case',
     '7f3547c4212cb5ca354d0c2e4b45d4c0f58f9d529a6bdf6c44e5e44fa476588a'),
    ('bool clip_into(InputTriangle t, int p, int q, int line)', '// r (on the line p-q)',
     'a1414c0fd2dddd6af5a03e7f16b6034071c3e2464748912b12445178ca4c621c'),
]


def geometry_obligations(timeout_ms):
    t0, t1, u0, u1, x = z3.Reals('t_lo t_hi u_lo u_hi section_coordinate')
    sections = [t0 <= t1, u0 <= u1]
    lo, hi = z3.If(t0 >= u0, t0, u0), z3.If(t1 <= u1, t1, u1)
    overlap = z3.And(t0 <= x, x <= t1, u0 <= x, x <= u1)
    rows = [
        ('section_max_min_can_change_membership', sections, overlap != z3.And(lo <= x, x <= hi), 'unsat'),
        ('coincident_section_endpoint_is_reachable', sections + [t0 < u0, u0 == t1, t1 < u1],
         z3.And(lo == hi, x == lo, overlap), 'sat'),
        ('selecting_smaller_section_start_can_leave_other_section',
         sections + [t0 < u0, lo <= hi], z3.And(x == t0, z3.Not(overlap)), 'sat'),
    ]
    result = [check_expr(*row, timeout_ms, 'QF_LRA') for row in rows]
    sa, sb, parameter = z3.Reals('edge_start_plane_sign edge_end_plane_sign segment_parameter')
    result.append(check_expr('proper_plane_section_endpoint_can_leave_source_edge',
                             [sa > 0, sb < 0, (1-parameter)*sa + parameter*sb == 0],
                             z3.Or(parameter <= 0, parameter >= 1), 'unsat', timeout_ms, 'QF_NRA'))

    # In the coplanar projection, normalize the receiving edge to (0,0)-(1,0).
    # Exchange P and Q if necessary so that P is above the line and Q below.
    px, py, qx, qy, cross = z3.Reals('px py qx qy crossing_x')
    den = py - qy
    num = qx*py - px*qy
    # Intersect the two lines in the normalized frame. The reciprocal test is
    # the product of the signs at the two receiving-edge endpoints.
    segment_crosses_line = [py > 0, qy < 0, cross*den == num]
    both_straddle = segment_crosses_line + [(-num)*(den-num) < 0]
    result.extend([
        check_expr('proper_coplanar_crossing_is_reachable', both_straddle,
                   z3.And(0 < cross, cross < 1), 'sat', timeout_ms, 'QF_NRA'),
        check_expr('proper_coplanar_crossing_can_leave_receiving_edge', both_straddle,
                   z3.Or(cross <= 0, cross >= 1), 'unsat', timeout_ms, 'QF_NRA'),
        check_expr('segment_can_cross_support_line_outside_receiving_edge', segment_crosses_line,
                   cross > 1, 'sat', timeout_ms, 'QF_NRA'),
    ])
    return result


def run(timeout_ms=8000):
    source_contract(SOURCE, ANCHORS)
    inner, edge, known, raw, limit = z3.Ints('interior boundary known raw limit')
    # The geometric premise is that the known points survive as distinct, non-corner vertices.
    points = [inner >= 0, edge >= 0, known >= 0, known <= inner + edge, known <= raw, limit > 0]
    pieces = 1 + 2*inner + edge
    lower = 1 + known
    prior_lower, prior_exact, later_exact = z3.Ints('prior_lower prior_exact later_exact')
    prefix = points + [0 <= prior_lower, prior_lower <= prior_exact, 0 <= later_exact]
    total = prior_exact + pieces + later_exact
    remain, point_refs, segment_refs = z3.Ints('remaining_budget point_references segment_references')
    max_int, max_long = 2**31-1, 2**63-1
    contribution = 1 + point_refs + 2*segment_refs
    remaining = remain - contribution
    known_edge, known_inner = z3.Ints('known_boundary known_interior')
    dropped = z3.Int('dropped_candidates')
    weighted = 1 + known_edge + 2*known_inner
    classified = [0 <= known_edge, known_edge <= edge, 0 <= known_inner, known_inner <= inner]
    checks = [
        ('a_known_vertex_subset_can_exceed_the_exact_piece_count', points, lower > pieces, 'unsat'),
        ('counting_candidates_discarded_during_preparation_can_refuse_a_valid_budget',
         points + [dropped > 0, pieces <= limit], lower + dropped > limit, 'sat'),
        ('endpoint_prefix_can_refuse_a_budget_the_exact_count_accepts',
         prefix + [prior_lower + lower > limit], total <= limit, 'unsat'),
        ('using_nonstrict_comparison_can_refuse_equality',
         prefix, z3.And(prior_lower + lower == limit, total == limit), 'sat'),
        ('raw_reference_filter_can_skip_a_certifying_scan',
         points + [1 + raw <= limit], lower > limit, 'unsat'),
        ('raw_budget_subtraction_can_overflow',
         [0 <= remain, remain <= max_long, 0 <= point_refs, point_refs <= max_int,
          0 <= segment_refs, segment_refs <= max_int],
         z3.Or(remaining < -max_long-1, remaining > max_long), 'unsat'),
        ('weighting_certified_interior_endpoints_can_exceed_the_exact_count',
         classified, weighted > pieces, 'unsat'),
        ('weighting_can_certify_when_counting_each_endpoint_once_cannot',
         classified + [limit > 0, 1 + known_edge + known_inner <= limit],
         z3.And(weighted > limit, pieces > limit), 'sat'),
        ('unweighted_raw_filter_can_hide_a_weighted_certificate',
         classified + [known_edge + known_inner <= raw, limit > 0, 1 + raw <= limit],
         weighted > limit, 'sat'),
        ('doubled_raw_references_can_hide_a_weighted_certificate',
         classified + [known_edge + known_inner <= raw, limit > 0, 1 + 2*raw <= limit],
         weighted > limit, 'unsat'),
    ]
    return report([check_expr(*row, timeout_ms, 'QF_LIA') for row in checks] + geometry_obligations(timeout_ms))


if __name__ == '__main__':
    raise SystemExit(main(run))
