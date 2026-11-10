#!/usr/bin/env python3
"""Summary of the D1 cohort run (runs/d1/job.sh): python3 analyze.py <out dir> > summary.md

<out dir> holds base.jsonl and cand.jsonl (or the per-model files in base/ and cand/). Per operation (id, op):
  status   clean (ok, RoundingClean, NotCleanReason ""), empty (NotCleanReason "empty"), unclean: <reason or
           'rounding'>, failed (Compute false, an exception, a crashed or timed-out runner, a missing row)
Sections:
  1. clean -> not clean and not clean -> clean (empty counts as clean here: it is a valid result, and the lists show
     a change of it anyway in section 1c)
  2. operations whose base row has no edit rejected by the bound (smallest rejected drift infinite): the result
     hashes must be equal; every difference is listed (the stop rule of the PR)
  3. deciles of MaxDrift / MaxLength per build (operations with a rounding and a positive bound)
  4. times: sum, median, p95, max of the operation and of the rounding per build; operations slower by more than 20 %
     and 0.2 s (whole operation)
"""
import glob
import json
import os
import sys


def load(out, build):
    rows = {}
    path = os.path.join(out, build + '.jsonl')
    files = [path] if os.path.exists(path) else sorted(glob.glob(os.path.join(out, build, '*.jsonl')))
    for f in files:
        for line in open(f):
            line = line.strip()
            if not line.startswith('{'):
                continue
            d = json.loads(line)
            rows[(d['id'], d.get('op', '?'))] = d
    return rows


def status(d):
    if d is None or 'error' in d or 'exception' in d or not d.get('ok'):
        return 'failed'
    reason = d.get('reason', '')
    if reason == 'empty':
        return 'empty'
    if d.get('rounding_clean') and reason == '':
        return 'clean'
    return 'unclean: ' + (reason or 'rounding')


def num(x):
    if x is None:
        return None
    if isinstance(x, str):
        return float('inf') if x == 'Infinity' else float('nan') if x == 'NaN' else float(x)
    return float(x)


def quantile(xs, q):
    xs = sorted(xs)
    if not xs:
        return float('nan')
    i = min(len(xs) - 1, max(0, int(round(q * (len(xs) - 1)))))
    return xs[i]


def main():
    out = sys.argv[1]
    base, cand = load(out, 'base'), load(out, 'cand')
    # every operation of either build; a model with only an error row counts with its three operations
    ids = {k[0] for k in base} | {k[0] for k in cand}
    keys = sorted({(i, op) for i in ids for op in ('union', 'intersection', 'difference')},
                  key=lambda k: (int(k[0]) if k[0].isdigit() else 0, k[1]))

    def row(rows, k):
        return rows.get(k) or rows.get((k[0], '?'))

    print('# D1 cohort run: base (apex rule) against cand (reference rule)\n')
    print(f'{len(keys)} operations of {len(ids)} models; rows: base {len(base)}, cand {len(cand)}\n')
    st = {k: (status(row(base, k)), status(row(cand, k))) for k in keys}
    for name, i in (('base', 0), ('cand', 1)):
        counts = {}
        for k in keys:
            s = st[k][i].split(':')[0]
            counts[s] = counts.get(s, 0) + 1
        print(f'- {name}: ' + ', '.join(f'{v} {s}' for s, v in sorted(counts.items())))
    print()

    good = ('clean', 'empty')
    worse = [k for k in keys if st[k][0] in good and st[k][1] not in good]
    better = [k for k in keys if st[k][0] not in good and st[k][1] in good]
    other = [k for k in keys if st[k][0] != st[k][1] and k not in worse and k not in better]

    def table(title, ks):
        print(f'## {title}: {len(ks)}\n')
        if ks:
            print('| id | op | base | cand | base drift | cand drift | base smallest rejected | cand smallest rejected |')
            print('|---|---|---|---|---|---|---|---|')
            for k in ks:
                b, c = row(base, k) or {}, row(cand, k) or {}
                print(f"| {k[0]} | {k[1]} | {st[k][0]} | {st[k][1]} | {b.get('drift_ratio')} | {c.get('drift_ratio')} | "
                      f"{b.get('smallest_rejected_ratio')} | {c.get('smallest_rejected_ratio')} |")
        print()

    table('1a. clean -> not clean (stop rule: each one analysed before the merge)', worse)
    table('1b. not clean -> clean', better)
    table('1c. other status changes (eg clean <-> empty, one reason for another)', other)

    no_reject = [k for k in keys if row(base, k) and num(row(base, k).get('smallest_rejected_ratio')) == float('inf')]
    diff = [k for k in no_reject if (row(base, k) or {}).get('hash') != (row(cand, k) or {}).get('hash')]
    print(f'## 2. no edit rejected by the bound in base: {len(no_reject)} operations, {len(diff)} with a different '
          f'result (stop rule: each one analysed)\n')
    if diff:
        print('| id | op | base status | cand status | base edits | cand edits |')
        print('|---|---|---|---|---|---|')
        for k in diff:
            b, c = row(base, k) or {}, row(cand, k) or {}
            e = lambda d: '/'.join(str(d.get(f, '-')) for f in ('collapses', 'flips', 'moves', 'zips', 'strip_zips', 'vertex_removals'))
            print(f'| {k[0]} | {k[1]} | {st[k][0]} | {st[k][1]} | {e(b)} | {e(c)} |')
        print()
    rejected = [k for k in keys if k not in no_reject and (row(base, k) or {}).get('smallest_rejected_ratio') is not None and row(cand, k)]
    same = sum(1 for k in rejected if row(base, k).get('hash') == row(cand, k).get('hash'))
    print(f'Operations with a rejected edit in base: {len(rejected)}, of them {same} with the same result.\n')

    print('## 3. MaxDrift / MaxLength, deciles (operations with a rounding)\n')
    print('| build | n | 0 | 0.1 | 0.2 | 0.3 | 0.4 | 0.5 | 0.6 | 0.7 | 0.8 | 0.9 | 1 | > 0.9 |')
    print('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    for name, rows in (('base', base), ('cand', cand)):
        xs = [num(d.get('drift_ratio')) for d in rows.values() if d.get('drift_ratio') is not None]
        xs = [x for x in xs if x == x]
        print(f'| {name} | {len(xs)} | ' + ' | '.join(f'{quantile(xs, q / 10):.3g}' for q in range(11))
              + f' | {sum(1 for x in xs if x > 0.9)} |')
    print()

    print('## 4. times (ms)\n')
    print('| build | field | sum | median | p95 | max |')
    print('|---|---|---|---|---|---|')
    for name, rows in (('base', base), ('cand', cand)):
        for field in ('ms', 'rounding_ms'):
            xs = [num(d[field]) for d in rows.values() if d.get(field) is not None and num(d[field]) >= 0]
            print(f'| {name} | {field} | {sum(xs):.0f} | {quantile(xs, 0.5):.0f} | {quantile(xs, 0.95):.0f} | {max(xs) if xs else 0:.0f} |')
    slow = []
    for k in keys:
        b, c = row(base, k), row(cand, k)
        if b and c and b.get('ms') is not None and c.get('ms') is not None:
            tb, tc = num(b['ms']), num(c['ms'])
            if tc > 1.2 * tb and tc - tb > 200:
                slow.append((k, tb, tc, num(b.get('rounding_ms', -1)), num(c.get('rounding_ms', -1))))
    print(f'\nSlower by more than 20 % and 0.2 s: {len(slow)}\n')
    if slow:
        print('| id | op | base ms | cand ms | base rounding ms | cand rounding ms |')
        print('|---|---|---|---|---|---|')
        for k, tb, tc, rb, rc in sorted(slow, key=lambda x: x[2] - x[1], reverse=True)[:100]:
            print(f'| {k[0]} | {k[1]} | {tb:.0f} | {tc:.0f} | {rb:.0f} | {rc:.0f} |')


if __name__ == '__main__':
    main()
