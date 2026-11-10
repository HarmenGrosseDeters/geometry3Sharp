#!/usr/bin/env python3
"""Exact Boolean 3.0 pre-study, topic 5: table of results/fwn.jsonl (fwn_job.sh rows, prestudy-fwn).
usage: python3 analyze_fwn.py results/fwn.jsonl"""
import json, sys
print('| model | mesh | queries (pieces) | library queries s (serial) | near terms / query | far terms / query | near share of the work | '
      'bound of B (far + traversal free) | dual-tree near / single near | two-tier: accepted at margin 0.1 | cost vs today | accepted flips |')
print('|' + '---|' * 12)
tot = near_w = 0.0
for line in open(sys.argv[1]):
    r = json.loads(line)
    if 'error' in r:
        continue
    tt, tf = r['ns_triangle_term'], r['ns_order2_eval']
    for m in ('mesh0', 'mesh1'):
        x = r[m]
        n, f = x['near_per_query'], x['far_per_query']
        share = n * tt / (n * tt + f * tf)
        t = x['two_tier']['m0.1']
        tot += x['lib_queries_ms']; near_w += x['lib_queries_ms'] * share
        print(f"| {r['case']} | {m[-1]} | {r['pieces']} | {x['lib_queries_ms'] / 1000:.1f} | {n} | {f} | {share:.2f} | "
              f"-{(1 - share) * 100:.0f} % | {x['dual_near_per_query'] / n:.2f} | {t['accepted']:.2f} | {t['cost_vs_single']:.2f} | "
              f"{t['accepted_flips_vs_beta2']} |")
print(f'\nweighted by query time: near share {near_w / tot:.2f}, so variant B saves at most {(1 - near_w / tot) * 100:.0f} % of the '
      f'per-piece query work ({tot / 1000:.0f} s serial in all)')
