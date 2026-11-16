#!/usr/bin/env python3
"""Tables of the block test (stage 2, Theorem 6) from `solid` runs: per set and cell size, uncertified cells and
certified runs without and with blocks, wrong-and-certified runs, the blocks tried and certified, and the times.
usage: analyze_blocks.py <solid.jsonl>...   (several files: one per repetition; times are reported as min-max)"""
import json, sys
from collections import defaultdict

runs = defaultdict(list)          # (input, res) -> rows of the repetitions
for path in sys.argv[1:]:
    for line in open(path):
        r = json.loads(line)
        runs[(r['input'], r['res'])].append(r)

def group(name):
    return 'CAD solids' if name.startswith('mesh:') else 'T1 clean'

table = defaultdict(lambda: defaultdict(list))
for (name, res), rows in sorted(runs.items()):
    if not rows[0].get('in_clean') or 'blocks_status' not in rows[0]:
        continue
    t = table[(group(name), res)]
    r = rows[0]
    t['n'].append(1)
    t['unc'].append(r['uncertified_cells']); t['unc_b'].append(r['blocks_uncertified'])
    t['cert'].append(r['status'] == 'Certified'); t['cert_b'].append(r['blocks_status'] == 'Certified')
    t['wrong'].append(r.get('result_sig') != r.get('truth_sig'))
    t['wrong_cert_b'].append(r['blocks_status'] == 'Certified' and r.get('blocks_result_sig') != r.get('truth_sig'))
    t['tried'].append(r['blocks_tried']); t['bcert'].append(r['blocks_certified'])
    for k in ('contour_ms', 'certificate_ms', 'blocks_certificate_ms', 'blocks_ms'):
        t[k].append([x[k] for x in rows])

print('| set | cell | runs | uncertified cells: stage 1 -> blocks | certified runs: stage 1 -> blocks | wrong results | wrong and certified | blocks tried / certified | contour ms | certificate ms: stage 1 -> blocks | of it blocks ms |')
print('|---|---|---|---|---|---|---|---|---|---|---|')
for (g, res), t in sorted(table.items(), key=lambda kv: (kv[0][1], kv[0][0])):
    def total(k):     # summed over the inputs, per repetition: min-max
        reps = list(zip(*t[k]))
        sums = [sum(x) for x in reps]
        return f'{min(sums)}' if min(sums) == max(sums) else f'{min(sums)}-{max(sums)}'
    u0, u1 = sum(t['unc']), sum(t['unc_b'])
    share = f' ({100.0 * (u0 - u1) / u0:.0f} % certified)' if u0 else ''
    print(f"| {g} | / {res} | {len(t['n'])} | {u0} -> {u1}{share} | {sum(t['cert'])} -> {sum(t['cert_b'])} | {sum(t['wrong'])} | "
          f"{sum(t['wrong_cert_b'])} | {sum(t['tried'])} / {sum(t['bcert'])} | {total('contour_ms')} | {total('certificate_ms')} -> "
          f"{total('blocks_certificate_ms')} | {total('blocks_ms')} |")
