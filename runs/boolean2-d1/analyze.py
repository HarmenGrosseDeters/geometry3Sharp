#!/usr/bin/env python3
"""Summary of job.sh (D1): per build the operations with a clean rounding (rounding_clean in both rounds), the
operations whose clean status differs from master, the results that differ from master (hash), the sums of the best of
the two rounds (total, rounding), and the operations over 5 s. usage: python3 analyze.py <out dir>  (prints Markdown)"""
import collections, json, os, sys

BUILDS = ['master', 'd1']
D1 = {'100344', '1012236', '1053875', '118037', '129937', '1396900', '472043', '472046', '548875', '548878', '551020',
      '1514904', '669979', '686056'}


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
    keys = sorted(set(k for b in BUILDS for r in runs[b] for k in r))
    clean, best, rnd, hashes, errors, unstable = {}, {}, {}, {}, collections.Counter(), []
    for b in BUILDS:
        for k in keys:
            rs = [r[k] for r in runs[b] if k in r]
            ok = [r for r in rs if 'ms' in r]
            if len(ok) < 2:
                errors[b] += 1
                continue
            cs = {r.get('rounding_clean') for r in ok}
            if len(cs) > 1:
                unstable.append((b, k))
            clean[(b, k)] = all(r.get('rounding_clean') for r in ok)
            best[(b, k)] = min(min(r['ms']) for r in ok)
            rnd[(b, k)] = min(r.get('phases_ms', {}).get('rounding', 0) for r in ok)
            hashes[(b, k)] = ok[0].get('hash')
    print('# D1 apex rule against master\n')
    print(f'{len(keys)} operations; errors: {dict(errors) or "none"}; clean status differing between rounds: '
          f'{unstable or "none"}\n')
    print('| build | clean | of them D1 models | total ms | rounding ms | ops > 5 s | results differing from master |')
    print('|---|---|---|---|---|---|---|')
    for b in BUILDS:
        ks = [k for k in keys if (b, k) in clean]
        nclean = sum(clean[(b, k)] for k in ks)
        nd1 = sum(clean[(b, k)] for k in ks if k[0] in D1)
        tot = sum(best[(b, k)] for k in ks)
        rt = sum(rnd[(b, k)] for k in ks)
        slow = sum(best[(b, k)] > 5000 for k in ks)
        diff = sum(hashes.get(('master', k)) != hashes[(b, k)] for k in ks)
        print(f'| {b} | {nclean} of {len(ks)} | {nd1} of {sum(k[0] in D1 for k in ks)} | {tot} | {rt} | {slow} | {diff} |')
    print('\n## Operations whose clean status differs from master\n')
    print('| case | op | master | d1 | D1 model |')
    print('|---|---|---|---|---|')
    for k in keys:
        if ('master', k) in clean and ('d1', k) in clean and clean[('master', k)] != clean[('d1', k)]:
            print(f'| {k[0]} | {k[1]} | {"clean" if clean[("master", k)] else "not clean"} | '
                  f'{"clean" if clean[("d1", k)] else "not clean"} | {"yes" if k[0] in D1 else "no"} |')
    print('\n## Operations not clean in d1\n')
    print(', '.join(f'{k[0]} {k[1]}' for k in keys if ('d1', k) in clean and not clean[('d1', k)]) or 'none')
    print('\n## Time ratio d1 / master (total, best of 2), operations over 1 s in master\n')
    rs = sorted(best[('d1', k)] / best[('master', k)] for k in keys
                if ('master', k) in best and ('d1', k) in best and best[('master', k)] > 1000)
    if rs:
        print(f'median {rs[len(rs) // 2]:.3f}, min {rs[0]:.3f}, max {rs[-1]:.3f} over {len(rs)} operations')


main()
