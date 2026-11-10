#!/usr/bin/env python3
"""Exact Boolean 3.0 pre-study, topic 1: per-phase scaling and Amdahl shares from scaling_job.sh output.

usage: python3 analyze_scaling.py <work dir>/scaling <ids file> [--per-op]
Reads c<n>/<id>.jsonl (BooleanComparison profile rows) for n in 1, 2, 4. Prints markdown tables:
  - per cohort and phase: time at 1/2/4 cores, speedup, Amdahl parallel fraction f (from 1 -> 4 cores),
    the share of the 4-core time, and the upper bound of a perfectly parallel phase (t4 - t1/4) as % of the total;
  - a projection T(n) = sum_p t1_p ((1 - f_p) + f_p / n) for 8, 16, 64 cores;
  - ops above 5 s at each core count.
Phases (top level, exclusive): input, setup, intersections, merge, triangulation, classification, simplify,
assembly (without simplify, which assemble() times inside its lap), rounding."""
import json, os, sys
from collections import defaultdict

PHASES = ['input', 'setup', 'intersections', 'merge', 'triangulation', 'classification', 'simplify', 'assembly', 'rounding']
SUB = ['intersections.trees', 'intersections.pairs', 'intersections.planes', 'intersections.intersect',
       'merge.points', 'merge.crossings', 'merge.points2']


def phases(row):
    p = row.get('phases_ms', {})
    out = {k: p.get(k, 0) for k in PHASES}
    out['assembly'] = p.get('assembly', 0) - p.get('simplify', 0)
    for k in SUB:
        out[k] = p.get(k, 0)
    out['total'] = sum(out[k] for k in PHASES)
    out['wall'] = row['ms'][0] if isinstance(row.get('ms'), list) else row.get('ms', 0)
    return out


def load(d, ids):
    rows = {}
    for n in (1, 2, 4):
        for i in ids:
            f = os.path.join(d, f'c{n}', f'{i}.jsonl')
            if not os.path.exists(f):
                continue
            for line in open(f):
                try:
                    r = json.loads(line)
                except ValueError:
                    continue
                if 'phases_ms' not in r:
                    continue
                rows[(n, i, r['op'])] = (phases(r), r)
    return rows


def amdahl(t1, tn, n):
    if t1 <= 0 or tn <= 0:
        return float('nan')
    s = t1 / tn
    return (1 - 1 / s) / (1 - 1 / n)


def table(title, keys, rows, cols):
    print(f'\n### {title}\n')
    print(f'ops with all three core counts: {len(keys)}\n')
    tot = {n: defaultdict(float) for n in (1, 2, 4)}
    for (i, op) in keys:
        for n in (1, 2, 4):
            ph = rows[(n, i, op)][0]
            for k in cols + ['total']:
                tot[n][k] += ph[k] / 1000
    T4 = tot[4]['total']
    print('| phase | 1 core s | 2 cores s | 4 cores s | speedup 4 | Amdahl f | share at 4 | bound: perfect 4-core scaling |')
    print('|---|---|---|---|---|---|---|---|')
    proj = {8: 0.0, 16: 0.0, 64: 0.0}
    for k in cols + ['total']:
        t1, t2, t4 = tot[1][k], tot[2][k], tot[4][k]
        f = amdahl(t1, t4, 4)
        bound = (t4 - t1 / 4) / T4 * 100 if T4 else 0
        sp = t1 / t4 if t4 else float('nan')
        print(f'| {k} | {t1:.1f} | {t2:.1f} | {t4:.1f} | {sp:.2f} | {f:.2f} | {t4 / T4 * 100:.1f} % | {bound:.1f} % |')
        if k in PHASES:
            fc = min(max(f, 0), 1) if f == f else 0
            for n in proj:
                proj[n] += t1 * ((1 - fc) + fc / n)
    if cols == PHASES:
      print(f'\nprojection (Amdahl per phase, fitted 1 -> 4): 8 cores {proj[8]:.1f} s, 16 cores {proj[16]:.1f} s, 64 cores '
          f'{proj[64]:.1f} s (4 cores measured {T4:.1f} s); serial floor sum t1 (1 - f) = '
          f'{sum(tot[1][k] * (1 - min(max(amdahl(tot[1][k], tot[4][k], 4), 0), 1)) for k in PHASES if tot[4][k] > 0):.1f} s')
    print()
    for n in (1, 2, 4):
        slow = sum(1 for (i, op) in keys if rows[(n, i, op)][0]['wall'] > 5000)
        print(f'- ops > 5 s at {n} core(s): {slow}')
    print()


def main():
    d, idsf = sys.argv[1], sys.argv[2]
    per_op = '--per-op' in sys.argv
    cohort = {}
    for line in open(idsf):
        p = line.split()
        if p:
            cohort[p[0]] = p[1] if len(p) > 1 else '-'
    rows = load(d, list(cohort))
    keys = sorted({(i, op) for (n, i, op) in rows if all((m, i, op) in rows for m in (1, 2, 4))})
    groups = defaultdict(list)
    for (i, op) in keys:
        c = cohort[i].replace('_rand2', '').replace('_rand', '')
        groups[c].append((i, op))
    table('all', keys, rows, PHASES)
    table('all, sub-phases of intersections and merge', keys, rows, SUB)
    for c in sorted(groups):
        table(f'cohort {c}', groups[c], rows, PHASES)
    # determinism: the result hash must not depend on the core count
    diff = [(i, op) for (i, op) in keys if len({rows[(n, i, op)][1].get('hash') for n in (1, 2, 4)}) > 1]
    print(f'\nresult hashes differing across core counts: {len(diff)} {diff[:10]}')
    if per_op:
        print('\n| model | op | cohort | 1 core ms | 4 cores ms | speedup | slowest phase at 4 |')
        print('|---|---|---|---|---|---|---|')
        for (i, op) in sorted(keys, key=lambda k: -rows[(4,) + k][0]['wall']):
            p4 = rows[(4, i, op)][0]
            p1 = rows[(1, i, op)][0]
            top = max(PHASES, key=lambda k: p4[k])
            print(f'| {i} | {op} | {cohort[i]} | {p1["wall"]} | {p4["wall"]} | {p1["wall"] / max(p4["wall"], 1):.2f} | {top} {p4[top]} |')


if __name__ == '__main__':
    main()
