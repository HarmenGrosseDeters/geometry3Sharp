#!/usr/bin/env python3
"""Summary of results/detect.md: per detector and tessellation, the mean and the worst of the scores over the solids
with fillets, and the marked share of the rod (a cylinder that is no fillet). Usage: summarize.py results/detect.md"""
import sys
from collections import defaultdict

def num(x):
    x = x.strip()
    return None if x == '-' else float(x)

rows = defaultdict(list)
rod = defaultdict(list)
for line in open(sys.argv[1]):
    c = [x.strip() for x in line.strip().strip('|').split('|')]
    if len(c) < 11 or c[0] in ('solid', '---') or c[0].startswith('-'):
        continue
    name, det = c[0], c[2]
    tess = 'CAD strips' if ' cad ' in name else 'subdivided'
    if name.startswith('rod'):
        rod[(det, tess)].append(num(c[7]))
        continue
    rows[(det, tess)].append(dict(p=num(c[3]), r=num(c[4]), f=num(c[5]), wrong=num(c[8]), rad=num(c[9]), t=num(c[10]), tris=int(c[1])))

def mean(v):
    v = [x for x in v if x is not None]
    return sum(v) / len(v) if v else float('nan')

print('| detector | tessellation | cases | precision (mean) | recall (mean) | F1 mean | F1 worst | radius error (mean of medians) | rod area marked (worst) | s per 10k triangles |')
print('|---|---|---|---|---|---|---|---|---|---|')
for key in sorted(rows, key=lambda k: (k[1], k[0])):
    v = rows[key]
    # a detector that marks nothing has no precision; it counts as 0 in the mean
    p = mean([x['p'] if x['p'] is not None else 0.0 for x in v])
    speed = sum(x['t'] for x in v) / sum(x['tris'] for x in v) * 1e4
    rd = max(rod[key]) if rod[key] else float('nan')
    print('| %s | %s | %d | %.2f | %.2f | %.2f | %.2f | %.3f | %.2f | %.1f |' % (
        key[0], key[1], len(v), p, mean([x['r'] for x in v]), mean([x['f'] for x in v]), min(x['f'] for x in v),
        mean([x['rad'] for x in v]), rd, speed))
