#!/usr/bin/env python3
"""Compares alpha wrapping (BENCH_REPAIR=wrap) with other modes (eg solidify_dc_adaptive, robust) on the same meshes:
guarantees (closed, valid, manifold, oriented, no exact self-intersections), fidelity (certified Hausdorff in both
directions, volume IoU against the input's winding number solid), output size and time.
Usage: analyze_wrap.py results/wrap.jsonl results/solidify_dc_adaptive.jsonl ..."""
import json, os, sys
from criteria import get, no_solid, repaired

def load(f):
    return {r['file']: r for r in (json.loads(l) for l in open(f) if l.strip())}

def guarantees(r):
    # closed, valid, no bowties, consistently oriented, no inside-out components, no exact self-intersections
    return (r.get('status') == 'closed' and get(r, 'after_valid', False) and get(r, 'after_bowties', 1) == 0
            and get(r, 'after_inconsistent_orient_edges', 1) == 0 and get(r, 'after_inverted_components', 1) == 0
            and get(r, 'after_selfint_exact', 1) == 0)      # -1 (not computed, more than 300k triangles): not verified

def q(a, p):
    a = sorted(a)
    return a[min(len(a) - 1, int(p * (len(a) - 1)))] if a else float('nan')

runs = [(os.path.basename(f).replace('.jsonl', ''), load(f)) for f in sys.argv[1:]]
common = sorted(set.intersection(*[set(r) for _, r in runs]))
print(f"{len(common)} meshes in all runs\n")
rows = []
for name, rs in runs:
    xs = [rs[k] for k in common]
    hd = [max(get(r, 'hd_fwd_hi_rel', 1), get(r, 'hd_back_hi_rel', 1)) for r in xs if 'hd_fwd_hi_rel' in r]
    fwd = [get(r, 'hd_fwd_hi_rel', 1) for r in xs if 'hd_fwd_hi_rel' in r]
    back = [get(r, 'hd_back_hi_rel', 1) for r in xs if 'hd_back_hi_rel' in r]
    iou = [float(r['iou']) for r in xs if 'iou' in r and not no_solid(r)]
    ms = [get(r, 'repair_ms', 0) / 1000 for r in xs if 'repair_ms' in r]
    tris = [get(r, 'after_tris', 0) for r in xs]
    rows.append((name, {
        'meshes': len(xs),
        'guarantees met (closed, manifold, oriented, no self-int.)': sum(guarantees(r) for r in xs),
        'exceptions / timeouts': sum(r.get('status') in ('exception', 'timeout_or_crash') for r in xs),
        'not closed or emptied': sum(r.get('status') in ('not_closed', 'emptied') for r in xs),
        'with exact self-intersections': sum(get(r, 'after_selfint_exact', 0) > 0 for r in xs),
        'self-intersections not computed (> 300k tris)': sum(get(r, 'after_selfint_exact', 0) == -1 for r in xs),
        'with bowties': sum(get(r, 'after_bowties', 0) not in (0, None) for r in xs),
        'Hausdorff result->input, median / p90 (% diag)': f"{100 * q(fwd, .5):.2f} / {100 * q(fwd, .9):.2f}",
        'Hausdorff input->result, median / p90 (% diag)': f"{100 * q(back, .5):.2f} / {100 * q(back, .9):.2f}",
        'Hausdorff both < 1 %': sum(h < 0.01 for h in hd),
        'IoU median / p10': f"{q(iou, .5):.3f} / {q(iou, .1):.3f}",
        'IoU > 0.99': sum(v > 0.99 for v in iou),
        'repaired (criteria.py: clean, no self-int., HD < 1 %, IoU > 0.99)': sum(repaired(r) for r in xs),
        'output triangles, median': int(q(tris, .5)),
        'time median / p90 / max (s)': f"{q(ms, .5):.2f} / {q(ms, .9):.2f} / {max(ms) if ms else 0:.1f}",
    }))
keys = list(rows[0][1].keys())
w = max(len(k) for k in keys)
print(' ' * w + ''.join(f"  {n:>22}" for n, _ in rows))
for k in keys:
    print(f"{k:{w}}" + ''.join(f"  {str(d[k]):>22}" for _, d in rows))

wrap = [rs for n, rs in runs if n.startswith('wrap')]
if wrap:
    rs = wrap[0]
    st = {}
    for k in common:
        s = rs[k].get('wrap_status', rs[k].get('status'))
        st[s] = st.get(s, 0) + 1
    print(f"\n{runs[0][0]} status: {st}")
    bad = [k for k in common if not guarantees(rs[k])]
    print(f"guarantees not met: {len(bad)} {[(k, rs[k].get('status'), rs[k].get('wrap_status'), rs[k].get('error', rs[k].get('wrap_error'))) for k in bad[:10]]}")

if len(runs) >= 2:
    # paired, per mesh: first run against the second (eg wrap against cgalwrap)
    (na, ra), (nb, rb) = runs[0], runs[1]
    both = [k for k in common if get(ra[k], 'repair_ms', 0) > 0 and get(rb[k], 'repair_ms', 0) > 0 and 'after_tris' in ra[k] and 'after_tris' in rb[k]]
    if both:
        tr = [ra[k]['repair_ms'] / rb[k]['repair_ms'] for k in both]
        nr = [ra[k]['after_tris'] / max(1, rb[k]['after_tris']) for k in both]
        sa, sb = sum(ra[k]['repair_ms'] for k in both) / 1000, sum(rb[k]['repair_ms'] for k in both) / 1000
        print(f"\n{na} / {nb}, per mesh ({len(both)} meshes):")
        print(f"  time ratio: median {q(tr, .5):.2f}, p10 {q(tr, .1):.2f}, p90 {q(tr, .9):.2f}; total {sa:.1f} s / {sb:.1f} s")
        print(f"  triangle ratio: median {q(nr, .5):.2f}, p10 {q(nr, .1):.2f}, p90 {q(nr, .9):.2f}")
        us = [1000 * ra[k]['repair_ms'] / max(1, ra[k]['after_tris']) for k in both]
        ub = [1000 * rb[k]['repair_ms'] / max(1, rb[k]['after_tris']) for k in both]
        print(f"  microseconds per output triangle: median {q(us, .5):.1f} / {q(ub, .5):.1f}")
