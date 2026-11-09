#!/usr/bin/env python3
"""Ops above 5 s with all levers (levers build) from both samples: input size, time and phase mix (Markdown).

usage: python3 exceptions.py results/levers.jsonl results/random/levers.jsonl"""
import json, os, sys

PHASES = ('input', 'setup', 'intersections', 'merge', 'triangulation', 'classification', 'simplify', 'assembly', 'rounding')
here = os.path.dirname(os.path.abspath(__file__))
cohort = {}
for f in ('sample.txt', 'sample_random.txt'):
    cohort.update(l.split() for l in open(os.path.join(here, f)) if l.strip())
rows = [json.loads(l) for p in sys.argv[1:] for l in open(p)]
rows = sorted((r for r in rows if r['ms'] > 5000), key=lambda r: -r['ms'])
print('| model | cohort | op | input triangles A+B | ms | µs per input triangle | ' + ' | '.join(PHASES) + ' |')
print('|---|---|---|---|---|---|' + '---|' * len(PHASES))
for r in rows:
    n = r['tris_a'] + r['tris_b']
    mix = ' | '.join(f"{100 * r['phases_ms'].get(p, 0) / r['ms']:.0f}" for p in PHASES)
    print(f"| {r['case']} | {cohort[r['case']]} | {r['op']} | {n} | {r['ms']} | {1000 * r['ms'] / n:.1f} | {mix} |")
print(f'\n{len(rows)} operations above 5 s; phase columns in % of the operation time.')
