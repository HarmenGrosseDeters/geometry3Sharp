# Per-mesh comparison of two result files of the same mode: which meshes got better or worse.
# Usage: compare_runs.py before.jsonl after.jsonl
import json, sys
strict = ['boundary_edges', 'bowties', 'inconsistent_orient_edges', 'duplicate_tris', 'degenerate_tris', 'inverted_components']
def load(f): return {r['file']: r for r in (json.loads(l) for l in open(f) if l.strip())}
def clean(r): return 'after_tris' in r and all(r['after_' + m] == 0 for m in strict)
def full(r): return clean(r) and r['after_folded_edges_gt175deg'] == 0 and r['after_self_intersecting_pairs'] == 0
a, b = load(sys.argv[1]), load(sys.argv[2])
common = sorted(set(a) & set(b))
print(f"{len(common)} meshes in both")
for name, f in [('closed', lambda r: r.get('after_closed', False)), ('topologically clean', clean), ('+ no folds/self-intersections', full),
                ('exception/timeout', lambda r: r['status'] in ('exception', 'timeout_or_crash'))]:
    nb, na = sum(f(a[k]) for k in common), sum(f(b[k]) for k in common)
    better = [k for k in common if f(b[k]) and not f(a[k])]
    worse = [k for k in common if f(a[k]) and not f(b[k])]
    print(f"{name:32} before {nb:4}  after {na:4}   gained {len(better):3} {better[:6]}   lost {len(worse):3} {worse[:6]}")
metrics = strict + ['folded_edges_gt175deg', 'self_intersecting_pairs']
print("\nmeshes where a metric got better / worse:")
for m in metrics:
    k2 = [k for k in common if 'after_' + m in a[k] and 'after_' + m in b[k]]
    print(f"  {m:28} better {sum(b[k]['after_'+m] < a[k]['after_'+m] for k in k2):3}  worse {sum(b[k]['after_'+m] > a[k]['after_'+m] for k in k2):3}")
