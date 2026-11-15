#!/usr/bin/env python3
"""Tables of the block growth (stage 3, remark 3 of Theorem 6) from `solid` runs: per set and cell size, uncertified
cells and certified runs with blocks as they are and with grown blocks, wrong-and-certified runs, what the grown blocks
leave and why, and the times; then the cells left per input.
usage: analyze_growth.py <solid.jsonl>...   (several files: one per repetition; times are reported as min-max)"""
import json, sys
from collections import defaultdict

REASONS = ('mesh side', 'transition', 'not a ball', 'level-set count', 'contour count')
runs = defaultdict(list)          # (input, res) -> rows of the repetitions
for path in sys.argv[1:]:
    for line in open(path):
        r = json.loads(line)
        runs[(r['input'], r['res'])].append(r)

def group(name):
    return 'CAD solids' if name.startswith('mesh:') else 'T1 clean'

def span(values):
    return f'{min(values)}' if min(values) == max(values) else f'{min(values)}-{max(values)}'

table = defaultdict(lambda: defaultdict(list))
per_input = []
for (name, res), rows in sorted(runs.items()):
    if not rows[0].get('in_clean') or 'grow_status' not in rows[0]:
        continue
    t = table[(group(name), res)]
    r = rows[0]
    # the counts must agree across the repetitions
    for k in ('uncertified_cells', 'blocks_uncertified', 'grow_uncertified', 'grow_status', 'grow_left_by'):
        assert all(x[k] == r[k] for x in rows), (name, res, k)
    wrong = r.get('result_sig') != r.get('truth_sig')
    t['n'].append(1)
    t['unc'].append(r['uncertified_cells']); t['unc_b'].append(r['blocks_uncertified']); t['unc_g'].append(r['grow_uncertified'])
    t['cert'].append(r['status'] == 'Certified'); t['cert_b'].append(r['blocks_status'] == 'Certified')
    t['cert_g'].append(r['grow_status'] == 'Certified')
    t['wrong'].append(wrong)
    t['wrong_cert'].append((r['blocks_status'] == 'Certified' or r['grow_status'] == 'Certified') and wrong)
    t['grown'].append(r['grow_grown']); t['grown_cells'].append(r['grow_grown_cells'])
    left = [int(x) for x in r['grow_left_by'].split(',')]
    t['left_right'].append(left if not wrong else [0] * 5); t['left_wrong'].append(left if wrong else [0] * 5)
    for k in ('contour_ms', 'blocks_certificate_ms', 'grow_certificate_ms', 'blocks_ms', 'grow_blocks_ms'):
        t[k].append([x[k] for x in rows])
    if r['grow_uncertified']:
        per_input.append(f"| {name} | / {res} | {'wrong' if wrong else 'right'} | {r['blocks_uncertified']} -> {r['grow_uncertified']} | "
                         + ', '.join(f'{REASONS[i]} {v}' for i, v in enumerate(left) if v) + ' |')

print('| set | cell | runs | uncertified cells: blocks -> grown | certified runs: stage 1, blocks, grown | wrong results | wrong and certified | grown blocks (cells added) | contour ms | certificate ms: blocks -> grown | of it blocks ms: blocks -> grown |')
print('|---|---|---|---|---|---|---|---|---|---|---|')
for (g, res), t in sorted(table.items(), key=lambda kv: (kv[0][1], kv[0][0])):
    def total(k):     # summed over the inputs, per repetition: min-max
        return span([sum(x) for x in zip(*t[k])])
    print(f"| {g} | / {res} | {len(t['n'])} | {sum(t['unc_b'])} -> {sum(t['unc_g'])} | {sum(t['cert'])}, {sum(t['cert_b'])}, {sum(t['cert_g'])} | "
          f"{sum(t['wrong'])} | {sum(t['wrong_cert'])} | {sum(t['grown'])} ({sum(t['grown_cells'])}) | {total('contour_ms')} | "
          f"{total('blocks_certificate_ms')} -> {total('grow_certificate_ms')} | {total('blocks_ms')} -> {total('grow_blocks_ms')} |")
print()
print('| set | cell | cells left in right runs, by reason | in wrong runs |')
print('|---|---|---|---|')
for (g, res), t in sorted(table.items(), key=lambda kv: (kv[0][1], kv[0][0])):
    lr = [sum(x) for x in zip(*t['left_right'])]; lw = [sum(x) for x in zip(*t['left_wrong'])]
    fmt = lambda v: ', '.join(f'{REASONS[i]} {x}' for i, x in enumerate(v) if x) or '-'
    print(f'| {g} | / {res} | {fmt(lr)} | {fmt(lw)} |')
print()
print('| input | cell | result | uncertified cells: blocks -> grown | left by |')
print('|---|---|---|---|---|')
for line in per_input:
    print(line)
