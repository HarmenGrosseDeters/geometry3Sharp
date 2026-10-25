# Attributes newly introduced defects to AutoRepair pipeline steps (results from BENCH_REPAIR=instr).
# For each step: total increase of each metric over all meshes, and in how many meshes it increased.
import json, sys, collections
rows = [json.loads(l) for l in open(sys.argv[1]) if l.strip()]
rows = [r for r in rows if 'stages' in r]
metrics = ['q_duplicate_tris', 'q_degenerate_tris', 'q_folded_edges_gt175deg', 'q_inverted_components',
           'q_inconsistent_orient_edges', 'bowties', 'boundary_edges']
inc = collections.defaultdict(lambda: collections.Counter())
meshes = collections.defaultdict(lambda: collections.Counter())
order = []
for r in rows:
    prev = {m: r['before_' + m[2:] if m.startswith('q_') else 'before_' + m] for m in metrics}
    for st in r['stages']:
        name = st['stage']
        if name not in order: order.append(name)
        for m in metrics:
            d = st[m] - prev[m]
            if d > 0:
                inc[name][m] += d; meshes[name][m] += 1
            prev[m] = st[m]
print(f"{len(rows)} meshes. Cells: total increase (meshes affected)\n")
short = {'q_duplicate_tris': 'dup', 'q_degenerate_tris': 'degen', 'q_folded_edges_gt175deg': 'folds',
         'q_inverted_components': 'inverted', 'q_inconsistent_orient_edges': 'orient', 'bowties': 'bowties', 'boundary_edges': 'bdry'}
print(f"{'step':26}" + "".join(f"{short[m]:>16}" for m in metrics))
for name in order:
    print(f"{name:26}" + "".join(f"{(str(inc[name][m]) + ' (' + str(meshes[name][m]) + ')') if inc[name][m] else '-':>16}" for m in metrics))
