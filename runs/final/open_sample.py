#!/usr/bin/env python3
"""The open-class sample of open_remeasure.sh, from the per-model rows of FINAL-1's rest phase (one file per model):
every open model with an operation slower than 5 s, not clean (reason other than "", "empty" and "not closed": an
open input gives an open result, so "not closed" alone is expected) or with an error ("must"), filled to n from the other open models stratified by their slowest operation (5 quantile strata, equal
shares, seeded; "strat").
  python3 runs/final/open_sample.py <rest rows dir> [n] [--seed 20261110] > open_sample.txt
Output: "id must|strat" per line, sorted by id."""
import glob, json, os, random, sys

args = sys.argv[1:]
seed = int(args[args.index('--seed') + 1]) if '--seed' in args else 20261110
pos = [a for i, a in enumerate(args) if not a.startswith('--') and (i == 0 or not args[i - 1].startswith('--'))]
rest, n = pos[0], int(pos[1]) if len(pos) > 1 else 300
slowest, must = {}, set()
for f in sorted(glob.glob(os.path.join(rest, '*.jsonl'))):
    rows = [json.loads(l) for l in open(f) if l.strip().startswith('{')]
    if not rows or rows[0].get('class') != 'open':
        continue
    i = rows[0]['id']
    slowest[i] = max([r.get('ms', 0) for r in rows] + [0])
    if any('error' in r or 'exception' in r or r.get('ok') is False or r.get('ms', 0) > 5000
           or r.get('reason', '') not in ('', 'empty', 'not closed') for r in rows):
        must.add(i)
others = sorted((i for i in slowest if i not in must), key=lambda i: (slowest[i], i))
k = max(0, n - len(must))
rng = random.Random(seed)
picked = []
for s in range(5):
    stratum = others[s * len(others) // 5:(s + 1) * len(others) // 5]
    take = k // 5 + (1 if s < k % 5 else 0)
    picked += rng.sample(stratum, min(take, len(stratum)))
out = [(i, 'must') for i in must] + [(i, 'strat') for i in picked]
for i, tag in sorted(out, key=lambda x: int(x[0]) if x[0].isdigit() else 0):
    print(i, tag)
print(f"{len(slowest)} open models in the rest phase, {len(must)} must, {len(picked)} stratified", file=sys.stderr)
