#!/usr/bin/env python3
"""Exact Boolean 3.0 pre-study, topic 4: table of results/exact.jsonl (exact_job.sh rows).
usage: python3 analyze_exact.py results/exact.jsonl"""
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1])]
print('| model | op | SafeRounding ms | unrounded ms | + exact vertices ms | rounding phase ms | RoundingClean | unrounded NotCleanReason | '
      'vertices | of them doubles | max numerator bits | max W bits | mean bits / vertex |')
print('|' + '---|' * 13)
tot = [0, 0, 0]
nonempty = unclean = 0
for r in rows:
    if 'rounded_ms' not in r or r.get('exact_vertices') is None:
        continue
    tot[0] += r['rounded_ms']; tot[1] += r['unrounded_ms']; tot[2] += r['exact_ms']
    reason = r.get('unrounded_not_clean_reason') or 'clean'
    if r['exact_vertices'] > 0:
        nonempty += 1
        unclean += reason != 'clean'
    print(f"| {r['case']} | {r['op']} | {r['rounded_ms']} | {r['unrounded_ms']} | {r['exact_ms']} | "
          f"{r['rounded_phases_ms'].get('rounding', 0)} | {r.get('rounding_clean')} | {reason} | {r['exact_vertices']} | "
          f"{r['exact_double_vertices']} | {r['exact_max_num_bits']} | {r['exact_max_w_bits']} | {r['exact_mean_bits_per_vertex']} |")
print(f'\nsums: SafeRounding {tot[0] / 1000:.1f} s, unrounded {tot[1] / 1000:.1f} s ({(tot[1] / tot[0] - 1) * 100:+.1f} %), '
      f'with exact vertices {tot[2] / 1000:.1f} s; unrounded output not clean on {unclean} of {nonempty} non-empty ops')
