#!/usr/bin/env python3
"""Summary of the position search cohort run (runs/ps/job.sh): python3 analyze.py <out dir> > summary.md

<out dir> holds base.jsonl and cand.jsonl (or the per-model files in base/ and cand/) of the correctness pass.
Per operation (id, op):
  status   clean (ok, RoundingClean, NotCleanReason ""), empty (NotCleanReason "empty"), unclean: <reason or
           'rounding'>, failed (Compute false, an exception, a crashed or timed-out runner, a missing row)
Sections:
  1. clean -> not clean and not clean -> clean (empty counts as clean here), other status changes
  2. operations that are rounding clean in base: the result hashes must be equal (the search does not run on them);
     every difference is listed (the stop rule of the PR). And the operations that are not: how many changed
  3. deciles of MaxDrift / MaxLength per build (operations with a rounding and a positive bound), and of the
     operations the search moved a vertex in
  4. times of the correctness pass (one core per process, four at a time, base then cand per model: a rough
     comparison, not an acceptance timing): sums of the operation and of the rounding per build, and the operations
     slower by more than 20 % and 0.5 s
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

    print('# Position search cohort run: base (master) against cand (the position search)\n')
    print(f'{len(keys)} operations of {len(ids)} models; rows: base {len(base)}, cand {len(cand)}\n')
    st = {k: (status(row(base, k)), status(row(cand, k))) for k in keys}
    for name, i in (('base', 0), ('cand', 1)):
        counts = {}
        for k in keys:
            s = st[k][i].split(':')[0]
            counts[s] = counts.get(s, 0) + 1
        print(f'- {name}: ' + ', '.join(f'{v} {s}' for s, v in sorted(counts.items())))
    print()

    copied = sum(1 for d in cand.values() if d.get('reason_from_base'))
    print(f'cand: {copied} operations took the verdict of base (the same result hash), the others were evaluated\n')
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

    rclean = [k for k in keys if row(base, k) and row(base, k).get('rounding_clean')]
    diff = [k for k in rclean if (row(base, k) or {}).get('hash') != (row(cand, k) or {}).get('hash')]
    print(f'## 2. rounding clean in base: {len(rclean)} operations, {len(diff)} with a different result (stop rule: '
          f'each one analysed)\n')
    e = lambda d: '/'.join(str(d.get(f, '-')) for f in ('collapses', 'flips', 'moves', 'zips', 'strip_zips', 'vertex_removals', 'position_search_moves'))
    if diff:
        print('| id | op | base status | cand status | base edits | cand edits |')
        print('|---|---|---|---|---|---|')
        for k in diff:
            b, c = row(base, k) or {}, row(cand, k) or {}
            print(f'| {k[0]} | {k[1]} | {st[k][0]} | {st[k][1]} | {e(b)} | {e(c)} |')
        print()
    unclean = [k for k in keys if row(base, k) and row(base, k).get('ok') and not row(base, k).get('rounding_clean') and row(cand, k)]
    changed = [k for k in unclean if row(base, k).get('hash') != row(cand, k).get('hash')]
    searched = [k for k in keys if (row(cand, k) or {}).get('position_search_moves')]
    print(f'Not rounding clean in base: {len(unclean)} operations, {len(changed)} with a different result; the search '
          f'moved a vertex in {len(searched)} operations ({sum(row(cand, k)["position_search_moves"] for k in searched)} moves).\n')
    if changed:
        print('| id | op | base status | cand status | base left (degenerate, pairs) | cand left | base drift | cand drift | base edits | cand edits (the last: search moves) | limit base / cand |')
        print('|---|---|---|---|---|---|---|---|---|---|---|')
        left = lambda d: f"{d.get('remaining_degenerate')}, {d.get('remaining_intersections')}"
        for k in changed:
            b, c = row(base, k), row(cand, k)
            print(f"| {k[0]} | {k[1]} | {st[k][0]} | {st[k][1]} | {left(b)} | {left(c)} | {b.get('drift_ratio')} | {c.get('drift_ratio')} | {e(b)} | {e(c)} | {b.get('limit')} / {c.get('limit')} |")
        print()
    worse_left = [k for k in changed if (row(cand, k).get('remaining_degenerate') or 0) + (row(cand, k).get('remaining_intersections') or 0)
                  > (row(base, k).get('remaining_degenerate') or 0) + (row(base, k).get('remaining_intersections') or 0)]
    print(f'Of them with more defects left in cand than in base: {len(worse_left)} (expected 0: no edit adds a defect).\n')

    print('## 3. MaxDrift / MaxLength, deciles (operations with a rounding)\n')
    print('| build | n | 0 | 0.1 | 0.2 | 0.3 | 0.4 | 0.5 | 0.6 | 0.7 | 0.8 | 0.9 | 1 | > 0.9 |')
    print('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    for name, rows in (('base', base), ('cand', cand)):
        xs = [num(d.get('drift_ratio')) for d in rows.values() if d.get('drift_ratio') is not None]
        xs = [x for x in xs if x == x]
        print(f'| {name} | {len(xs)} | ' + ' | '.join(f'{quantile(xs, q / 10):.3g}' for q in range(11))
              + f' | {sum(1 for x in xs if x > 0.9)} |')
    xs = [num(row(cand, k).get('drift_ratio')) for k in searched if row(cand, k).get('drift_ratio') is not None]
    xb = [num(row(base, k).get('drift_ratio')) for k in searched if row(base, k) and row(base, k).get('drift_ratio') is not None]
    for name, v in (('base, the operations the search moved a vertex in', xb), ('cand, the same', xs)):
        print(f'| {name} | {len(v)} | ' + ' | '.join(f'{quantile(v, q / 10):.3g}' for q in range(11)) + f' | {sum(1 for x in v if x > 0.9)} |')
    over = [k for k in keys if row(cand, k) and row(cand, k).get('drift_ratio') is not None and num(row(cand, k)['drift_ratio']) > 1]
    print(f'\nMaxDrift above MaxLength in cand: {len(over)} (expected 0).\n')

    tkeys = [k for k in keys if row(base, k) and row(cand, k) and row(base, k).get('ms') is not None and row(cand, k).get('ms') is not None]
    print(f'## 4. times (ms), correctness pass (one core per process): {len(tkeys)} operations\n')
    print('| build | field | sum | median | p95 | max |')
    print('|---|---|---|---|---|---|')
    for name, rows in (('base', base), ('cand', cand)):
        for field in ('ms', 'rounding_ms'):
            v = [num(row(rows, k)[field]) for k in tkeys if row(rows, k).get(field) is not None and num(row(rows, k)[field]) >= 0]
            print(f'| {name} | {field} | {sum(v):.0f} | {quantile(v, 0.5):.0f} | {quantile(v, 0.95):.0f} | {max(v) if v else 0:.0f} |')
    for name, sel in (('rounding clean in base', set(rclean)), ('not rounding clean in base', set(unclean))):
        for bname, rows in (('base', base), ('cand', cand)):
            v = [num(row(rows, k)['rounding_ms']) for k in tkeys if k in sel and row(rows, k).get('rounding_ms') is not None]
            print(f'| {bname} | rounding_ms, {name} | {sum(v):.0f} | {quantile(v, 0.5):.0f} | {quantile(v, 0.95):.0f} | {max(v) if v else 0:.0f} |')
    slow = []
    for k in tkeys:
        tb, tc = num(row(base, k)['ms']), num(row(cand, k)['ms'])
        if tc > 1.2 * tb and tc - tb > 500:
            slow.append((k, tb, tc, num(row(base, k).get('rounding_ms', -1)), num(row(cand, k).get('rounding_ms', -1))))
    print(f'\nSlower by more than 20 % and 0.5 s: {len(slow)}\n')
    if slow:
        print('| id | op | base ms | cand ms | base rounding ms | cand rounding ms | rounding clean in base | search moves |')
        print('|---|---|---|---|---|---|---|---|')
        for k, tb, tc, rb, rc in sorted(slow, key=lambda x: x[2] - x[1], reverse=True)[:100]:
            print(f"| {k[0]} | {k[1]} | {tb:.0f} | {tc:.0f} | {rb:.0f} | {rc:.0f} | {row(base, k).get('rounding_clean')} | {row(cand, k).get('position_search_moves')} |")


if __name__ == '__main__':
    main()
