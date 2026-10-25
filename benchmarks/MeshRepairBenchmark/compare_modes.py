# Compares result files of several repair modes: cleanliness, geometric deviation, triangle count, time.
# Usage: compare_modes.py results/cleanup.jsonl results/solidify.jsonl ...
import json, os, statistics as st, sys

strict = ['boundary_edges', 'bowties', 'inconsistent_orient_edges', 'duplicate_tris', 'degenerate_tris', 'inverted_components']

def load(f):
    return [json.loads(l) for l in open(f) if l.strip()]

def pct(xs, q):
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(q * len(xs)))] if xs else 0

cols = []
for f in sys.argv[1:]:
    allrows = load(f)
    rows = [r for r in allrows if 'after_tris' in r]     # the statistics need a result; the counts include the rest
    topo = [r for r in rows if all(r['after_' + m] == 0 for m in strict)]
    full = [r for r in topo if r['after_folded_edges_gt175deg'] == 0 and r['after_self_intersecting_pairs'] == 0]
    dev = [max(r.get('dev_max_rel', 0), r.get('back_max_rel', 0)) for r in rows]
    devmean = [r.get('dev_mean_rel', 0) + r.get('back_mean_rel', 0) for r in rows]
    tri = [r['after_tris'] / max(1, r['before_tris']) for r in rows]
    ms = [r.get('repair_ms', 0) for r in rows]
    cols.append((os.path.basename(f).replace('.jsonl', ''), {
        'meshes': len(allrows),
        'without result (exception, timeout)': len(allrows) - len(rows),
        'topologically clean': len(topo),
        '+ no folds / self-intersections': len(full),
        'self-intersections (meshes)': sum(r['after_self_intersecting_pairs'] > 0 for r in rows),
        'fallback to solidify': sum(bool(r.get('hybrid_fallback')) for r in rows),
        'deviation mean, median (% diag)': f"{100 * st.median(devmean):.3f}" if rows else '-',
        'deviation max, median (% diag)': f"{100 * st.median(dev):.2f}" if rows else '-',
        'deviation max, p90 (% diag)': f"{100 * pct(dev, .9):.2f}",
        'meshes with max deviation > 1% diag': sum(d > 0.01 for d in dev),
        'triangles after/before, median': f"{st.median(tri):.2f}" if rows else '-',
        'time median (s)': f"{st.median(ms) / 1000:.2f}" if ms else '-',
        'time max (s)': f"{max(ms) / 1000:.1f}" if ms else '-',
    }))
keys = list(cols[0][1].keys())
print(f"{'':38}" + "".join(f"{n:>16}" for n, _ in cols))
for k in keys:
    print(f"{k:38}" + "".join(f"{str(c[k]):>16}" for _, c in cols))
