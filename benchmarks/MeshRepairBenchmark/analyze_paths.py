#!/usr/bin/env python3
"""Which stage of MeshRobustRepair produced each result (BENCH_REPAIR=robust): the repair itself, the exact
resolve, the solidify or the alpha wrap; why the earlier stages were not used; the outcome (strict "repaired"
criterion of analyze_fidelity.py) per stage and per defect category. Optionally writes one CSV row per mesh.
Usage: analyze_paths.py results/robust.jsonl [data/selection.csv] [--csv out.csv] [--markdown]"""
import collections, csv, json, sys
from criteria import get, clean, selfint as selfint_count, iou, hd, HD, IOU

args = [a for a in sys.argv[1:] if not a.startswith('--')]
f = args[0]
sel = args[1] if len(args) > 1 else 'data/selection.csv'
out_csv = sys.argv[sys.argv.index('--csv') + 1] if '--csv' in sys.argv else None
if out_csv in args: args.remove(out_csv)
md = '--markdown' in sys.argv

rows = [json.loads(l) for l in open(f) if l.strip()]
cat = {}
try:
    for r in csv.DictReader(open(sel)):
        cat[r['path'].split('/')[-1]] = r['category']
except FileNotFoundError:
    pass

def stage(r):
    if r.get('status') != 'closed' and r.get('status') != 'not_closed':
        return 'failed (' + str(r.get('status')) + ')'
    if get(r, 'robust_used_wrap', False): return 'alpha wrap'
    if get(r, 'robust_used_solidify', False): return 'solidify'
    if get(r, 'robust_used_exact', False): return 'exact resolve'
    return 'repair'

def selfint(r):
    v = selfint_count(r)
    return None if v == -1 else v > 0       # -1: not computed (large results)

# 'repaired' is the criterion of criteria.py; a result whose self-intersections were not computed is not
# counted as repaired (as in analyze_fidelity), but shown in a column of its own
def outcome(r):
    if not clean(r): return 'not clean'
    s = selfint(r)
    if s: return 'self-intersections'
    if hd(r) >= HD: return 'deviation > 1 %'
    if iou(r) <= IOU: return 'volume (IoU)'
    return 'repaired' if s is False else 'self-int. not checked'

def why(r):
    # why the earlier stages were not used
    parts = []
    if get(r, 'hybrid_reason', ''): parts.append('repair: ' + r['hybrid_reason'])
    if get(r, 'robust_exact_reason', ''): parts.append('exact: ' + r['robust_exact_reason'])
    if get(r, 'robust_solidify_reason', ''): parts.append('solidify: ' + r['robust_solidify_reason'])
    return '; '.join(parts)

stages = ['repair', 'exact resolve', 'solidify', 'alpha wrap']
by_stage = collections.defaultdict(list)
for r in rows:
    by_stage[stage(r)].append(r)
outcomes = ['repaired', 'self-int. not checked', 'deviation > 1 %', 'volume (IoU)', 'self-intersections', 'not clean']

def med(v):
    v = sorted(v)
    return v[len(v) // 2] if v else float('nan')

def table(header, lines):
    if md:
        print('| ' + ' | '.join(header) + ' |')
        print('|' + '---|' * len(header))
        for l in lines: print('| ' + ' | '.join(str(x) for x in l) + ' |')
    else:
        w = [max(len(str(x)) for x in col) for col in zip(header, *lines)]
        for l in [header] + lines: print('  '.join(str(x).ljust(n) for x, n in zip(l, w)))
    print()

print(f"{f}: {len(rows)} meshes\n")
print("Stage that produced the result, and the outcome:\n")
lines = []
for s in stages + sorted(k for k in by_stage if k not in stages):
    rs = by_stage.get(s, [])
    if not rs: continue
    oc = collections.Counter(outcome(r) for r in rs)
    lines.append([s, len(rs)] + [oc.get(o, 0) for o in outcomes]
                 + [f"{100 * med([hd(r) for r in rs]):.2f} %", f"{med([iou(r) for r in rs]):.4f}",
                    f"{med([get(r, 'after_tris', 0) for r in rs]) / 1000:.0f}k", f"{med([get(r, 'repair_ms', 0) for r in rs]) / 1000:.1f} s"])
table(['stage', 'meshes'] + outcomes + ['Hausdorff median', 'IoU median', 'triangles median', 'time median'], lines)

print("Why the stages before were not used (meshes):\n")
for s in stages[1:]:
    rs = by_stage.get(s, [])
    if not rs: continue
    c = collections.Counter()
    for r in rs:
        c['repair: ' + (get(r, 'hybrid_reason', '') or '-')] += 1
        if s != 'exact resolve': c['exact: ' + (get(r, 'robust_exact_reason', '') or 'not tried')] += 1
        if s == 'alpha wrap': c['solidify: ' + (get(r, 'robust_solidify_reason', '') or '-')] += 1
    table([s, 'meshes'], [[k, v] for k, v in c.most_common()])

if cat:
    cats = sorted(set(cat.get(r['file'], '?') for r in rows))
    print("Stage per defect category (meshes; in brackets: repaired):\n")
    lines = []
    for c in cats:
        rs = [r for r in rows if cat.get(r['file'], '?') == c]
        cells = []
        for s in stages:
            sr = [r for r in rs if stage(r) == s]
            cells.append(f"{len(sr)} ({sum(1 for r in sr if outcome(r) == 'repaired')})")
        lines.append([c, len(rs)] + cells)
    table(['category', 'meshes'] + stages, lines)

if out_csv:
    with open(out_csv, 'w', newline='') as fo:
        w = csv.writer(fo)
        w.writerow(['file', 'category', 'stage', 'outcome', 'why_earlier_stages_not_used', 'hausdorff_rel', 'iou',
                    'tris_before', 'tris_after', 'repair_ms'])
        for r in sorted(rows, key=lambda r: r['file']):
            w.writerow([r['file'], cat.get(r['file'], ''), stage(r), outcome(r), why(r), f"{hd(r):.5f}",
                        f"{iou(r):.5f}", get(r, 'before_tris', ''), get(r, 'after_tris', ''), get(r, 'repair_ms', '')])
    print(f"wrote {out_csv}")
