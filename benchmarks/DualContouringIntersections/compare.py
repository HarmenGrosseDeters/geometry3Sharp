#!/usr/bin/env python3
"""A/B timing of two DualContouringIntersections --timing runs (baseline, candidate), same inputs.

usage: python3 compare.py --base <base.jsonl>... --cand <cand.jsonl>...
Per input family and resolution: summed total time, DC phase time and check + repair time (ResolveMilliseconds), of
the repetitions given (the median per input over the repetitions is used), and the candidate / baseline ratios."""
import collections, json, statistics, sys


def family(name):
    if name.startswith('imp:'):
        return name.split('@')[0]
    return 'mesh:procedural' if name.startswith('mesh:') else 'T1'


def load(files):
    per = collections.defaultdict(list)
    for f in files:
        for l in open(f):
            d = json.loads(l)
            dc = sum(v for k, v in d.items() if k.startswith('dc_ms_'))
            per[(d['input'], d['res'])].append((d.get('ms', 0), dc, d.get('resolve_ms', 0)))
    return {k: tuple(statistics.median(x[i] for x in v) for i in range(3)) for k, v in per.items()}


def main():
    args = sys.argv[1:]
    base = args[args.index('--base') + 1:args.index('--cand')]
    cand = args[args.index('--cand') + 1:]
    b, c = load(base), load(cand)
    groups = collections.OrderedDict()
    for k in sorted(set(b) & set(c), key=lambda k: (family(k[0]), k[1], k[0])):
        groups.setdefault((family(k[0]), k[1]), []).append(k)
    print('| input | res | runs | total s base / cand | ratio | DC s base / cand | ratio | check + repair s base / cand | ratio |')
    print('|' + '---|' * 9)
    for (name, res), keys in groups.items():
        sb = [sum(b[k][i] for k in keys) / 1000 for i in range(3)]
        sc = [sum(c[k][i] for k in keys) / 1000 for i in range(3)]
        r = lambda i: f"{sc[i] / sb[i]:.2f}" if sb[i] > 0 else '-'
        print(f"| {name} | {res} | {len(keys)} | {sb[0]:.1f} / {sc[0]:.1f} | {r(0)} | {sb[1]:.1f} / {sc[1]:.1f} | {r(1)} | {sb[2]:.2f} / {sc[2]:.2f} | {r(2)} |")


if __name__ == '__main__':
    main()
