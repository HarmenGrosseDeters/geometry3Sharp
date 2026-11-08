#!/usr/bin/env python3
"""Summary of job.sh: per build the sums of the best of the two rounds (total, rounding, arrangement pairs), the
operations over 5 s, the result hashes against master (every build must give the same results), and the slow
operations per build. usage: python3 analyze.py <out dir>  (prints Markdown)"""
import collections, json, os, statistics, sys

BUILDS = ['master', 'near', 'neartree', 'all']


def load(path):
    rows = {}
    if not os.path.exists(path):
        return rows
    for line in open(path):
        line = line.strip()
        if not line.startswith('{'):
            continue
        r = json.loads(line)
        case = r['case'].rstrip('/').split('/')[-1]
        rows[(case, r.get('op', 'error'))] = r
    return rows


def main():
    out = sys.argv[1]
    runs = {b: [load(os.path.join(out, f'{b}_r{k}.jsonl')) for k in (1, 2)] for b in BUILDS}
    keys = sorted(k for k in runs['master'][0] if 'ms' in runs['master'][0][k])
    best = collections.defaultdict(dict)
    errors = collections.Counter()
    hash_diff = collections.defaultdict(list)
    ref_hash = {}
    for k in keys:
        hs = {r[k]['hash'] for r in runs['master'] if k in r and 'hash' in r[k]}
        ref_hash[k] = hs.pop() if len(hs) == 1 else None
        if ref_hash[k] is None:
            hash_diff['master'].append(k)
    for b in BUILDS:
        for k in keys:
            rs = [r[k] for r in runs[b] if k in r and 'ms' in r[k]]
            if len(rs) < 2:
                errors[b] += 1
                continue
            best[b][k] = (min(r['ms'][0] for r in rs),
                          min(r['phases_ms'].get('rounding', 0) for r in rs),
                          min(r['phases_ms'].get('intersections.pairs', 0) for r in rs))
            if b != 'master' and any(r.get('hash') != ref_hash[k] for r in rs):
                hash_diff[b].append(k)
    common = [k for k in keys if all(k in best[b] for b in BUILDS)]
    print(f'# Exact Boolean 2.0 final A/B (master, #409, +#412, +#406 +#419)\n\n{len(common)} operations in every build (best of 2 rounds, ms).\n')
    print('| build | total | rounding | arrangement pairs | ops > 5 s | results differing from master | missing |')
    print('|---|---|---|---|---|---|---|')
    base = [sum(best['master'][k][i] for k in common) for i in range(3)]
    for b in BUILDS:
        s = [sum(best[b][k][i] for k in common) for i in range(3)]
        rel = [f'{s[i]} ({100 * (s[i] - base[i]) / base[i]:+.1f} %)' if base[i] else str(s[i]) for i in range(3)]
        over = sum(1 for k in common if best[b][k][0] > 5000)
        print(f'| {b} | {rel[0]} | {rel[1]} | {rel[2]} | {over} | {len(hash_diff[b])} | {errors[b]} |')
    print('\nMedian ratio of the rounding against master (operations with rounding > 50 ms):')
    for b in BUILDS[1:]:
        ratios = [best[b][k][1] / best['master'][k][1] for k in common if best['master'][k][1] > 50]
        print(f'- {b}: {statistics.median(ratios):.3f}')
    print('\n## Operations over 5 s in any build (total / rounding / pairs, ms)\n')
    print('| case | op | ' + ' | '.join(BUILDS) + ' |')
    print('|---|---|' + '---|' * len(BUILDS))
    for k in common:
        if any(best[b][k][0] > 5000 for b in BUILDS):
            cells = [f'{best[b][k][0]} / {best[b][k][1]} / {best[b][k][2]}' for b in BUILDS]
            print(f'| {k[0]} | {k[1]} | ' + ' | '.join(cells) + ' |')
    for b in BUILDS:
        if hash_diff[b]:
            print(f'\nResults differing ({b}): ' + ', '.join(f'{c} {o}' for c, o in hash_diff[b]))


if __name__ == '__main__':
    main()
