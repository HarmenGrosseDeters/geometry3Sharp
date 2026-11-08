#!/usr/bin/env python3
"""Summary of job.sh (robust far parts): per build the meshes with status ok, the verdict of MeshRobustRepair (the
path and result_reason), the meshes whose result differs between the builds (after_hash) or whose verdict differs,
and how often #425's retry relative to the largest component ran (robust_scale_retry), with the share that triggered
it. usage: python3 analyze.py <out dir>  (prints Markdown)"""
import collections, json, os, sys


def load(path):
    rows = {}
    for line in open(path):
        line = line.strip()
        if line.startswith('{'):
            r = json.loads(line)
            rows[r.get('file')] = r
    return rows


def verdict(r):
    if r.get('status') != 'ok':
        return r.get('status', '?')
    path = 'wrap' if r.get('robust_used_wrap') else 'solidify' if r.get('robust_used_solidify') else \
        'exact' if r.get('robust_used_exact') else 'repair'
    reason = r.get('robust_result_reason')
    return path + (f' ({reason})' if reason else '')


def main():
    out = sys.argv[1]
    base, pr = load(os.path.join(out, 'base.jsonl')), load(os.path.join(out, 'pr.jsonl'))
    files = sorted(set(base) | set(pr))
    print('# MeshRobustRepair far parts (#425) against its base (#420 head), 400 set\n')
    print(f'{len(files)} meshes; in base {len(base)}, in pr {len(pr)}\n')
    print('| build | status ok | timeout or crash | exact | solidify | wrap | repair kept |')
    print('|---|---|---|---|---|---|---|')
    for name, rows in (('base', base), ('pr', pr)):
        c = collections.Counter(verdict(r).split(' ')[0] for r in rows.values())
        print(f"| {name} | {sum(r.get('status') == 'ok' for r in rows.values())} | {c['timeout_or_crash']} | {c['exact']} | "
              f"{c['solidify']} | {c['wrap']} | {c['repair']} |")
    retry = [f for f in files if pr.get(f, {}).get('robust_scale_retry')]
    shares = sorted((pr[f].get('robust_scale_erosion_share') or 0) for f in files if f in pr)
    nonzero = [s for s in shares if s > 0]
    print(f'\nRetry relative to the largest component (robust_scale_retry): {len(retry)} meshes. '
          f'Meshes with a share > 0 (a triangle below the degenerate area in a component of a multi-component input '
          f'whose first resolves failed): {len(nonzero)}' + (f', share min {nonzero[0]:.3g}, max {nonzero[-1]:.3g}' if nonzero else ''))
    print('\n## Meshes whose result or verdict differs\n')
    print('| mesh | base | pr | same result (after_hash) | retry | share |')
    print('|---|---|---|---|---|---|')
    n = 0
    for f in files:
        b, p = base.get(f, {}), pr.get(f, {})
        same = b.get('after_hash') is not None and b.get('after_hash') == p.get('after_hash')
        if same and verdict(b) == verdict(p):
            continue
        n += 1
        print(f"| {f} | {verdict(b)} | {verdict(p)} | {'yes' if same else 'no'} | {p.get('robust_scale_retry')} | "
              f"{p.get('robust_scale_erosion_share')} |")
    print(f'\n{n} meshes differ.')


main()
