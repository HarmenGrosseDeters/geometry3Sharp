# Compares quality metrics before/after AutoRepair: how many meshes are clean, and what repair introduced.
import collections, json, csv, os, sys
sel = {r['path'].split('/')[1]: r['category'] for f in ['data/selection.csv', 'data/selection_large.csv'] if os.path.exists(f) for r in csv.DictReader(open(f))}
rows = [json.loads(l) for f in sys.argv[1:] for l in open(f) if l.strip()]
total = len(rows)
failed = collections.Counter(r.get('status') for r in rows if 'after_tris' not in r)
rows = [r for r in rows if 'after_tris' in r]
metrics = ['boundary_edges', 'bowties', 'inconsistent_orient_edges', 'duplicate_tris', 'degenerate_tris',
           'folded_edges_gt175deg', 'self_intersecting_pairs', 'inverted_components', 'sliver_tris']
# meshes without a result (exception, timeout, crash) are in the denominators below: they are not clean
print(f"{total} meshes, {len(rows)} with results, without: {dict(failed)}\n")
print(f"{'metric':28}{'bad before':>12}{'bad after':>11}{'got worse':>11}{'new (0->>0)':>13}{'sum before':>12}{'sum after':>11}")
for m in metrics:
    b = [r['before_' + m] for r in rows]; a = [r['after_' + m] for r in rows]
    ok = [(x, y) for x, y in zip(b, a) if x >= 0 and y >= 0]
    print(f"{m:28}{sum(x > 0 for x, _ in ok):>12}{sum(y > 0 for _, y in ok):>11}{sum(y > x for x, y in ok):>11}"
          f"{sum(x == 0 and y > 0 for x, y in ok):>13}{sum(x for x, _ in ok):>12}{sum(y for _, y in ok):>11}")
strict = ['boundary_edges', 'bowties', 'inconsistent_orient_edges', 'duplicate_tris', 'degenerate_tris', 'inverted_components']
clean_topo = [r for r in rows if all(r['after_' + m] == 0 for m in strict)]
fully = [r for r in clean_topo if r['after_folded_edges_gt175deg'] == 0 and r['after_self_intersecting_pairs'] == 0]
print(f"\nAfter repair: topologically clean (closed, manifold, oriented, no dup/degenerate, not inside-out): {len(clean_topo)}/{total}")
print(f"              additionally no folds and no self-intersections:                                   {len(fully)}/{total}")
cl = [r for r in rows if sel.get(r['file']) == 'clean']
if cl:
    print(f"\nClean controls ({len(cl)}): something got worse in:",
          [(r['file'], [m for m in metrics if r['after_' + m] > max(r['before_' + m], 0)]) for r in cl if any(r['after_' + m] > max(r['before_' + m], 0) for m in metrics)])
