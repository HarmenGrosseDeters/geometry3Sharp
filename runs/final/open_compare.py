#!/usr/bin/env python3
"""The open-class line of open_remeasure.sh: for the sampled open models, pre (#528's base, the rest phase's rows of
master 79a2c1ab) against fix (job2 correct on 293d0e3b): clean / contacts / unclean by reason / empty / failed per
operation, the operations whose verdict changed, and the times of the timing sample (job2 timing, both builds alone,
interleaved, 2 rounds: per operation the mean of the rounds).
  python3 runs/final/open_compare.py <rest rows dir> <work dir> > open_line.md
A pre row has no contact check: its "self-intersections" may be contacts (the fix rows have the check)."""
import collections, json, os, statistics, sys

rest, w = sys.argv[1], sys.argv[2]
ids = [l.split()[0] for l in open(os.path.join(w, 'open_sample.txt')) if l.strip()]


def rows(path):
    if not os.path.exists(path):
        return []
    return [json.loads(l) for l in open(path) if l.strip().startswith('{')]


# models expected to hit job2's per-model timeout (7200 s on 1 core with the per-piece classification), with their
# numbers from elsewhere (coordinator 2026-11-10: own column "timeout, size")
EXPECTED_TIMEOUT = {
    '1368052': 'open, 2.44M triangles per operand, 56k boundary edges; on 79a2c1ab alone on 4 cores: union Success in '
               '493 s, heap up to 8.7 GB, classification 297 s (60 %), rounding 64 s; rest phase (4 cores): union / '
               'intersection / difference 582 / 497 / 516 s, all "not closed"',
}


def verdict(r):
    if 'exit 124' in str(r.get('error', '')):
        return 'timeout'
    if 'error' in r or 'exception' in r or r.get('ok') is False or r.get('status') not in (None, 'Success'):
        return 'failed'
    v = r.get('verdict', r.get('reason'))
    if v is None:
        return 'no verdict'
    return {'': 'clean', 'empty': 'empty', 'contacts': 'contacts'}.get(v, 'unclean: ' + v)


pre, fix = {}, {}
for i in ids:
    for r in rows(os.path.join(rest, i + '.jsonl')):
        if 'op' in r:
            pre[(i, r['op'])] = r
    for r in rows(os.path.join(w, 'out', 'correct_fix', i + '.jsonl')):
        if 'op' in r:
            fix[(i, r['op'])] = r
        elif 'error' in r:
            for op in ('union', 'intersection', 'difference'):
                fix[(i, op)] = r
print(f"# Open-class re-measure after #528 ({len(ids)} sampled open models)\n")
print("| | ops | " + " | ".join(['clean', 'contacts', 'empty', 'unclean', 'timeout, size', 'failed']) + " |")
print("|---|---|---|---|---|---|---|---|")
for name, d in (('pre (79a2c1ab, rest phase)', pre), ('fix (293d0e3b)', fix)):
    c = collections.Counter(verdict(r) for r in d.values())
    unclean = sum(v for k, v in c.items() if k.startswith('unclean'))
    print(f"| {name} | {len(d)} | {c['clean']} | {c['contacts']} | {c['empty']} | {unclean} | {c['timeout']} | {c['failed'] + c['no verdict']} |")
timeouts = sorted({i for (i, _), r in fix.items() if verdict(r) == 'timeout'}, key=lambda x: int(x) if x.isdigit() else 0)
if timeouts:
    print("\nTimeouts (fix): " + ", ".join(i + (" (expected)" if i in EXPECTED_TIMEOUT else " (NOT expected)") for i in timeouts))
for i, note in EXPECTED_TIMEOUT.items():
    if i in ids:
        print(f"- {i}: {note}")
for name, d in (('pre', pre), ('fix', fix)):
    c = collections.Counter(verdict(r) for r in d.values() if verdict(r).startswith('unclean'))
    if c:
        print(f"\n{name} unclean by reason: " + ", ".join(f"{k[9:]} {v}" for k, v in c.most_common()))
changed = [(k, verdict(pre[k]), verdict(fix[k])) for k in sorted(set(pre) & set(fix)) if verdict(pre[k]) != verdict(fix[k])]
print(f"\nVerdict changed on {len(changed)} of {len(set(pre) & set(fix))} ops (pre -> fix):")
for (i, op), a, b in changed[:40]:
    print(f"- {i} {op}: {a} -> {b}")
if len(changed) > 40:
    print(f"- ... {len(changed) - 40} more")
print("\nTransitions: " + ", ".join(f"{a} -> {b}: {n}" for (a, b), n in collections.Counter((a, b) for _, a, b in changed).most_common()))

t = collections.defaultdict(list)
for r in rows(os.path.join(w, 'out', 'timing.jsonl')):
    if 'ms' in r and r.get('ok', True):
        t[(r['build'], r['id'], r['op'])].append(r['ms'])
ms = {b: {(i, op): statistics.mean(v) for (bb, i, op), v in t.items() if bb == b} for b in ('pre', 'fix')}
both = sorted(set(ms['pre']) & set(ms['fix']))
if both:
    def q(xs, p):
        xs = sorted(xs)
        return xs[min(len(xs) - 1, int(round(p * (len(xs) - 1))))]
    print(f"\nTiming sample: {len(both)} ops of {len({i for i, _ in both})} models, both builds alone, 2 rounds interleaved (ms per op)\n")
    print("| | median | p95 | max | total s |")
    print("|---|---|---|---|---|")
    for b in ('pre', 'fix'):
        xs = [ms[b][k] for k in both]
        print(f"| {b} | {q(xs, 0.5):.0f} | {q(xs, 0.95):.0f} | {max(xs):.0f} | {sum(xs) / 1000:.1f} |")
    ratio = [ms['fix'][k] / ms['pre'][k] for k in both if ms['pre'][k] > 0]
    print(f"\nfix / pre per op: median {q(ratio, 0.5):.2f}x, p95 {q(ratio, 0.95):.2f}x, max {max(ratio):.2f}x; "
          f"total {sum(ms['fix'][k] for k in both) / sum(ms['pre'][k] for k in both):.2f}x")
    worst = sorted(both, key=lambda k: -ms['fix'][k])[:5]
    print("slowest fix ops: " + ", ".join(f"{i} {op} {ms['pre'][(i, op)] / 1000:.1f} -> {ms['fix'][(i, op)] / 1000:.1f} s" for i, op in worst))
