#!/usr/bin/env python3
"""Compares two corpus outputs of `Meshing2Benchmark corpus` (base and candidate, same machine) line by line.

usage: analyze_corpus.py BASE.txt CAND.txt
Prints: counts of verdicts per side, every run whose verdict differs, the triangle counts (sum, and the ratio per run: 5 % / median / 95 %),
the meshing time (sum of the 'N ms' of the lines), the runs that reached the Steiner limit on either side. Exit code 0 always.
A line looks like:  <input> <mode>: <verdict> | <N> triangles, <V> vertices, <T> ms[, Steiner limit]  (detail as the corpus prints it).
"""
import re
import statistics
import sys

LINE = re.compile(r'^(?P<key>\S+ M[123](?:@[\d.]+)?): (?P<verdict>\S+) \| (?P<detail>.*)$')
TRI = re.compile(r'(\d+) triangles')
MS = re.compile(r'(\d+) ms')


def read(path):
    rows = {}
    for line in open(path, encoding='utf-8', errors='replace'):
        m = LINE.match(line.rstrip('\n'))
        if not m:
            continue
        d = m.group('detail')
        t, ms = TRI.search(d), MS.search(d)
        rows[m.group('key')] = dict(verdict=m.group('verdict'), tri=int(t.group(1)) if t else None,
                                    ms=int(ms.group(1)) if ms else None, limit='Steiner limit' in d, detail=d[:200])
    return rows


def main(base_path, cand_path):
    base, cand = read(base_path), read(cand_path)
    keys = sorted(set(base) & set(cand))
    print(f'runs: base {len(base)}, candidate {len(cand)}, common {len(keys)}')
    for name, rows in (('base', base), ('candidate', cand)):
        counts = {}
        for r in rows.values():
            counts[r['verdict']] = counts.get(r['verdict'], 0) + 1
        print(name, 'verdicts:', dict(sorted(counts.items())))
    print('\nverdict changes:')
    changed = [k for k in keys if base[k]['verdict'] != cand[k]['verdict']]
    for k in changed:
        print(f"  {k}: {base[k]['verdict']} -> {cand[k]['verdict']} | {cand[k]['detail']}")
    if not changed:
        print('  none')
    both = [k for k in keys if base[k]['tri'] and cand[k]['tri'] and base[k]['verdict'] == 'ok' and cand[k]['verdict'] == 'ok']
    ratios = sorted(cand[k]['tri'] / base[k]['tri'] for k in both)
    if ratios:
        q = lambda p: ratios[min(len(ratios) - 1, int(p * len(ratios)))]
        print(f"\ntriangles, both ok ({len(both)} runs): base {sum(base[k]['tri'] for k in both)}, candidate {sum(cand[k]['tri'] for k in both)}; "
              f"ratio 5% {q(0.05):.3f} / median {statistics.median(ratios):.3f} / 95% {q(0.95):.3f}; changed {sum(1 for r in ratios if r != 1)}")
        worst = sorted(both, key=lambda k: cand[k]['tri'] / base[k]['tri'])
        print('  largest savings:', [(k, base[k]['tri'], cand[k]['tri']) for k in worst[:5]])
        print('  largest increases:', [(k, base[k]['tri'], cand[k]['tri']) for k in worst[-5:] if cand[k]['tri'] > base[k]['tri']])
    tb = sum(base[k]['ms'] for k in both if base[k]['ms'])
    tc = sum(cand[k]['ms'] for k in both if cand[k]['ms'])
    print(f'\nmeshing time (sum of the ms of both-ok runs): base {tb} ms, candidate {tc} ms')
    print('Steiner limit: base', [k for k in keys if base[k]['limit']], 'candidate', [k for k in keys if cand[k]['limit']])


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
