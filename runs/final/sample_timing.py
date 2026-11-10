#!/usr/bin/env python3
"""Writes the fixed stratified timing sample of job2.sh timing: from FINAL rows (one per operation), per class the
models grouped by their slowest operation into 5 time strata (quantiles), and from each stratum n models, seeded.
  python3 runs/final/sample_timing.py <rows.jsonl> [more.jsonl ...] [--per-stratum 8] [--seed 20261110] > sample.txt
Output: "id class" per line, sorted by class and id."""
import json, random, sys
args = sys.argv[1:]
per = int(args[args.index('--per-stratum') + 1]) if '--per-stratum' in args else 8
seed = int(args[args.index('--seed') + 1]) if '--seed' in args else 20261110
files = [a for i, a in enumerate(args) if not a.startswith('--') and (i == 0 or not args[i - 1].startswith('--'))]
slowest, cls = {}, {}
for f in files:
    for line in open(f):
        r = json.loads(line)
        if 'ms' not in r:
            continue
        slowest[r['id']] = max(slowest.get(r['id'], 0), r['ms'])
        cls[r['id']] = r['class']
rng = random.Random(seed)
out = []
for c in sorted(set(cls.values())):
    ids = sorted((i for i in cls if cls[i] == c), key=lambda i: (slowest[i], i))
    for s in range(5):
        stratum = ids[s * len(ids) // 5:(s + 1) * len(ids) // 5]
        out += [(c, i) for i in rng.sample(stratum, min(per, len(stratum)))]
for c, i in sorted(out, key=lambda x: (x[0], int(x[1]) if x[1].isdigit() else 0)):
    print(i, c)
