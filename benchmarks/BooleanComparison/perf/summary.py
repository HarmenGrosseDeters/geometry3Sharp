#!/usr/bin/env python3
"""Per class of a run_classes.sh output directory: runs, median per case (sum of the operations' best times),
total, GC pause share, allocations per run, and the phase shares: python3 summary.py <out dir>..."""
import collections, json, os, statistics, sys

PHASES = ['input', 'setup.points', 'setup.triangles', 'intersections.trees', 'intersections.pairs',
          'intersections.planes', 'intersections.intersect', 'merge.points', 'merge.crossings', 'triangulation',
          'classification', 'simplify', 'assembly']

for d in sys.argv[1:]:
    print('==', d)
    for cls in ['cherchi', 'cgal', 'thingi', 'nary']:
        p = os.path.join(d, cls + '.jsonl')
        if not os.path.exists(p):
            continue
        rows = [json.loads(l) for l in open(p) if l.startswith('{')]
        per_case = collections.defaultdict(float); ph = collections.Counter(); tot = pause = alloc = 0
        for r in rows:
            m = min(r['ms']); per_case[r['case']] += m; tot += m
            pause += r['gc']['pause_ms']; alloc += r['alloc_mb']
            for k, v in r['phases_ms'].items():
                ph[k] += v
        print(f"{cls}: {len(rows)} runs, median/case {statistics.median(per_case.values()):.0f} ms, total {tot / 1000:.2f} s, "
              f"GC {100 * pause / tot:.0f} %, {alloc / len(rows):.0f} MB/run")
        print('   ' + ' '.join(f"{k}:{100 * ph[k] / tot:.0f}%" for k in PHASES))
