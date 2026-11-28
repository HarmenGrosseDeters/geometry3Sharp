"""Check the weighted endpoint scan rules under the retained-vertex premise."""
import z3

from check_pilot_obligations import check_expr, main, report


def run(timeout_ms=8000):
    sources, points, cap, raw = z3.Ints('sources points cap raw_references')
    found, remaining, unseen_inner = z3.Ints('found_inner remaining unseen_inner')
    classified, added_inner = z3.Ints('newly_classified newly_confirmed_inner')
    base = sources + points
    full = base + found + unseen_inner
    low, high = base + found, base + found + remaining
    premises = [sources >= 1, cap >= 1, 0 <= remaining, remaining <= points, points <= raw,
                0 <= found, found <= points - remaining, 0 <= unseen_inner, unseen_inner <= remaining]
    step = premises + [0 <= classified, classified <= remaining, 0 <= added_inner, added_inner <= classified]
    next_low = low + added_inner
    next_high = next_low + remaining - classified
    prefix_sources, prefix_points, prefix_inner = z3.Ints('prefix_sources prefix_points prefix_inner')
    prefix = [0 <= prefix_sources, prefix_sources <= sources, 0 <= prefix_points, prefix_points <= points,
              0 <= prefix_inner, prefix_inner <= prefix_points, prefix_inner <= found + unseen_inner]
    checks = [
        ('positive_two_pass_stop_can_disagree_with_the_full_weighted_scan',
         premises + [low > cap], full <= cap, 'unsat'),
        ('negative_two_pass_stop_can_hide_a_full_weighted_certificate',
         premises + [high <= cap], full > cap, 'unsat'),
        ('classifying_another_batch_can_widen_the_interval',
         step, z3.Or(next_low < low, next_high > high), 'unsat'),
        ('classifying_a_batch_consistent_with_retained_truth_can_exclude_that_truth',
         step + [0 <= unseen_inner - added_inner, unseen_inner - added_inner <= remaining - classified],
         z3.Or(next_low > full, next_high < full), 'unsat'),
        ('a_non_strict_refusal_can_reject_equality',
         premises + [low >= cap], full == cap, 'sat'),
        ('weighted_raw_gate_can_hide_a_full_weighted_certificate',
         premises + [sources + 2*raw <= cap], full > cap, 'unsat'),
        ('division_based_weighted_raw_gate_can_differ_from_the_doubled_ceiling',
         premises + [cap >= sources], (raw <= (cap - sources) / 2) != (sources + 2*raw <= cap), 'unsat'),
        ('a_weighted_source_prefix_can_exceed_the_full_weighted_bound',
         premises + prefix, prefix_sources + prefix_points + prefix_inner > full, 'unsat'),
        ('an_interval_straddling_the_cap_can_finish_at_or_below_it',
         premises + [low <= cap, high > cap], full <= cap, 'sat'),
        ('an_interval_straddling_the_cap_can_finish_above_it',
         premises + [low <= cap, high > cap], full > cap, 'sat'),
    ]
    return report([check_expr(*row, timeout_ms, 'QF_LIA') for row in checks])


if __name__ == '__main__':
    raise SystemExit(main(run))
