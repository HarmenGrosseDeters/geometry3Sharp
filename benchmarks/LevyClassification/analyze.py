#!/usr/bin/env python3
"""Summarize the phase-1 classification profile (job.sh output) as Markdown.

usage: python3 analyze.py <work dir>/out [ray_share.json]
ray_share.json (optional, from trace_split.py): {cohort: share of the classification phase spent in the replaceable
part (exact rays for closed meshes)}; without it the upper bound assumes the whole phase is replaceable."""
import collections, json, os, sys

TARGET_MS = 5000


def load(path):
    rows = []
    for line in open(path):
        r = json.loads(line)
        if 'ms' in r:
            rows.append(r)
        else:
            print(f'<!-- failed: {r} -->')
    return rows


def main():
    out = sys.argv[1]
    share = json.load(open(sys.argv[2])) if len(sys.argv) > 2 else {}
    here = os.path.dirname(os.path.abspath(__file__))
    cohort = dict(l.split() for l in open(os.path.join(here, 'sample.txt')) if l.strip())
    builds = [l.split()[0] for l in open(os.path.join(out, 'builds.txt'))]
    for b in builds:
        p = os.path.join(out, b + '.jsonl')
        if not os.path.exists(p):
            continue
        rows = load(p)
        print(f'## Build `{b}`\n')
        print('| cohort | ops | Compute s | classification s | share | ops > 5 s | ops <= 5 s if replaceable part = 0 |')
        print('|---|---|---|---|---|---|---|')
        agg = collections.defaultdict(collections.Counter)
        for r in rows:
            c = cohort[r['case']].replace('_rand', '')
            cl = r['phases_ms'].get('classification', 0)
            f = share.get(c, 1.0)
            for key in (c, 'all'):
                a = agg[key]
                a['ops'] += 1; a['ms'] += r['ms']; a['cls'] += cl
                if r['ms'] > TARGET_MS:
                    a['slow'] += 1
                    a['rescued'] += r['ms'] - f * cl <= TARGET_MS
        for c in sorted(agg, key=lambda k: (k == 'all', k)):
            a = agg[c]
            print(f"| {c} | {a['ops']} | {a['ms']/1000:.1f} | {a['cls']/1000:.1f} | {100*a['cls']/max(1,a['ms']):.1f} % "
                  f"| {a['slow']} | {a['slow'] - a['rescued']} |")
        print('\n| model | cohort | op | ms | classification ms | patches | pieces | winding_plane_sides | components A+B | exact winding A/B | status |')
        print('|---|---|---|---|---|---|---|---|---|---|---|')
        for r in sorted(rows, key=lambda r: -r['phases_ms'].get('classification', 0)):
            print(f"| {r['case']} | {cohort[r['case']]} | {r['op']} | {r['ms']} | {r['phases_ms'].get('classification', 0)} "
                  f"| {r['patches']} | {r['counts'].get('pieces')} | {r['counts'].get('winding_plane_sides')} "
                  f"| {r['components_a'] + r['components_b']} | {int(r['exact_winding_a'])}/{int(r['exact_winding_b'])} | {r['status']} |")
        print()


if __name__ == '__main__':
    main()
