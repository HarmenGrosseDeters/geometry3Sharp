#!/usr/bin/env python3
"""Summary of the Exact Boolean 2.0 final run (runs/final/job.sh).

usage: python3 analyze.py <work dir> [phase ...]   (default: every phase found in <work dir>/out)
Per class and phase: operations, clean / empty / unclean (the #385 evaluator: MeshRobustRepair.NotCleanReason of the
result), failures (status, exception, timeout), time median / p95 / max (nearest rank), and the operations over the
target max(5 s, 20 us x arrangement pieces), with the documented exceptions (#428) listed apart. With a control phase,
the target class per operation against the #385 baseline build on the same machine."""
import json, math, os, sys
from collections import Counter, defaultdict

EXCEPTIONS = {'113685', '1059579', '105693', '107543', '107545', '107554', '113906'}   # #428, exceptions_levers.md
BASELINE_385 = {'target': {'operations': 16239, 'unclean': 81, 'p95_s': 4.502, 'max_s': 281.956}}   # all classes, timing
CLASSES = ['target', 'selfint', 'unknown', 'open', 'unavailable']


def rows(path):
    out = []
    for name in sorted(os.listdir(path)):
        if not name.endswith('.jsonl'):
            continue
        lines = [l for l in open(os.path.join(path, name)) if l.strip()]
        rs = []
        for l in lines:
            try:
                rs.append(json.loads(l))
            except ValueError:
                pass
        mid = name[:-6]
        if not any('op' in r for r in rs):
            cls = rs[0].get('class', '?') if rs else '?'
            err = rs[0].get('error', 'no output') if rs else 'no output'
            out += [{'id': mid, 'class': cls, 'op': op, 'failure': err} for op in ('union', 'intersection', 'difference')]
            continue
        out += rs
    return out


def verdict(r):
    if 'failure' in r:
        return 'failure:' + r['failure']
    if 'exception' in r:
        return 'failure:exception'
    if not r.get('ok'):
        return 'failure:status ' + r.get('status', '?')
    reason = r.get('reason')
    if reason is None:
        return 'failure:not evaluated'
    if reason == '':
        return 'clean'
    if reason == 'empty':
        return 'empty'
    return 'unclean:' + reason


def pct(xs, p):
    if not xs:
        return float('nan')
    xs = sorted(xs)
    return xs[max(0, math.ceil(p * len(xs)) - 1)]


def target_ms(r):
    return max(5000.0, 0.02 * max(r.get('pieces', 0), 0))


def summary(phase, rs):
    print(f'## Phase `{phase}`\n')
    print('| class | models | ops | clean | empty | unclean | failures | median s | p95 s | max s | over target | of which documented exceptions |')
    print('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    by = defaultdict(list)
    for r in rs:
        by[r.get('class', '?')].append(r)
    for cls in CLASSES + sorted(set(by) - set(CLASSES)):
        g = by.get(cls)
        if not g:
            continue
        v = Counter(verdict(r).split(':')[0] for r in g)
        times = [r['ms'] / 1000 for r in g if 'ms' in r]
        over = [r for r in g if 'ms' in r and r['ms'] > target_ms(r)]
        exc = [r for r in over if r['id'] in EXCEPTIONS]
        print(f"| {cls} | {len({r['id'] for r in g})} | {len(g)} | {v['clean']} | {v['empty']} | {v['unclean']} | {v['failure']} | "
              f"{pct(times, 0.5):.3f} | {pct(times, 0.95):.3f} | {max(times) if times else float('nan'):.3f} | {len(over)} | {len(exc)} |")
    print()
    for cls in CLASSES:
        g = by.get(cls)
        if not g:
            continue
        reasons = Counter(verdict(r) for r in g if not verdict(r) in ('clean', 'empty'))
        if reasons:
            print(f'{cls}: ' + ', '.join(f'{k} {n}' for k, n in reasons.most_common()) + '\n')
        over = sorted((r for r in g if 'ms' in r and r['ms'] > target_ms(r) and r['id'] not in EXCEPTIONS), key=lambda r: -r['ms'])
        if over:
            print(f'{cls}, over max(5 s, 20 us x pieces), not a documented exception ({len(over)}):\n')
            print('| id | op | s | pieces | target s | rounding s | verdict |')
            print('|---|---|---:|---:|---:|---:|---|')
            for r in over[:40]:
                print(f"| {r['id']} | {r['op']} | {r['ms'] / 1000:.2f} | {r.get('pieces')} | {target_ms(r) / 1000:.2f} | {r.get('rounding_ms', -1) / 1000:.2f} | {verdict(r)} |")
            print()
        if cls == 'target':
            b = BASELINE_385['target']
            v = Counter(verdict(r).split(':')[0] for r in g)
            times = [r['ms'] / 1000 for r in g if 'ms' in r]
            print(f"#385 baseline (cd7fb8ab, another machine): {b['operations']} ops, {b['unclean']} unclean; timing over all classes "
                  f"p95 {b['p95_s']} s, max {b['max_s']} s. This phase: {len(g)} ops, {v['unclean']} unclean, p95 {pct(times, 0.95):.3f} s, "
                  f"max {max(times) if times else float('nan'):.3f} s.\n")


def control(final, ctrl):
    f = {(r['id'], r['op']): r for r in final if r.get('class') == 'target'}
    c = {(r['id'], r['op']): r for r in ctrl}
    both = [k for k in f if k in c and 'ms' in f[k] and 'ms' in c[k]]
    if not both:
        return
    print('## Final against the #385 baseline build on the same machine (target class)\n')
    ft = sum(f[k]['ms'] for k in both) / 1000
    ct = sum(c[k]['ms'] for k in both) / 1000
    ratios = sorted(f[k]['ms'] / max(c[k]['ms'], 1e-3) for k in both)
    print(f'{len(both)} operations timed in both: total {ct:.0f} s -> {ft:.0f} s ({100 * (ft / ct - 1):+.1f} %); '
          f'per operation ratio median {pct(ratios, 0.5):.2f}, p95 {pct(ratios, 0.95):.2f}.\n')
    print('| | baseline cd7fb8ab | final |')
    print('|---|---:|---:|')
    for name, fn in [('median s', lambda g: pct([r['ms'] / 1000 for r in g], 0.5)), ('p95 s', lambda g: pct([r['ms'] / 1000 for r in g], 0.95)),
                     ('max s', lambda g: max(r['ms'] / 1000 for r in g)),
                     ('over max(5 s, 20 us x pieces)', lambda g: sum(1 for r in g if r['ms'] > target_ms(r))),
                     ('unclean', lambda g: sum(1 for r in g if verdict(r).startswith('unclean')))]:
        print(f'| {name} | {fn([c[k] for k in both]):.3f} | {fn([f[k] for k in both]):.3f} |')
    print()
    fixed = sorted(k for k in both if verdict(c[k]).startswith('unclean') and not verdict(f[k]).startswith('unclean'))
    new = sorted(k for k in both if verdict(f[k]).startswith('unclean') and not verdict(c[k]).startswith('unclean'))
    print(f'unclean in the baseline, not in the final: {len(fixed)}; unclean in the final, not in the baseline: {len(new)} '
          + (' (' + ', '.join(f'{i} {o}' for i, o in new[:30]) + ')' if new else '') + '\n')


def main():
    w = sys.argv[1]
    phases = sys.argv[2:] or [p for p in ('target', 'control', 'rest') if os.path.isdir(os.path.join(w, 'out', p))]
    data = {p: rows(os.path.join(w, 'out', p)) for p in phases}
    print('# Exact Boolean 2.0 final run\n')
    builds = os.path.join(w, 'out', 'builds.txt')
    if os.path.exists(builds):
        print('Builds: ' + '; '.join(l.strip() for l in open(builds)) + '\n')
    for p in phases:
        summary(p, data[p])
    if 'target' in data and 'control' in data:
        control(data['target'], data['control'])


if __name__ == '__main__':
    main()
