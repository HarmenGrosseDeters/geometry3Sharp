#!/usr/bin/env python3
"""Compares alpha wrapping (BENCH_REPAIR=wrap) with other modes (eg solidify_dc_adaptive, robust) on the same meshes:
guarantees (closed, valid, manifold, oriented, no exact self-intersections), fidelity (certified Hausdorff in both
directions, volume IoU against the input's winding number solid), output size and time.
Usage: analyze_wrap.py results/wrap.jsonl results/solidify_dc_adaptive.jsonl ..."""
import json, os, sys

def load(f):
    return {r['file']: r for r in (json.loads(l) for l in open(f) if l.strip())}

def get(r, k, d=None):
    v = r.get(k, d)
    return d if v is None else v

def guarantees(r):
    # closed, valid, no bowties, consistently oriented, no inside-out components, no exact self-intersections
    return (r.get('status') == 'closed' and get(r, 'after_valid', False) and get(r, 'after_bowties', 1) == 0
            and get(r, 'after_inconsistent_orient_edges', 1) == 0 and get(r, 'after_inverted_components', 1) == 0
            and get(r, 'after_selfint_exact', 1) in (0, -1))      # -1: not computed (more than 300k triangles)

def no_solid(r):
    v = r.get('iou')
    return v == 'NaN' or (v == 0 and get(r, 'iou_missing', 1) == 0)

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
        'repaired (guarantees, HD < 1 %, IoU > 0.99)': sum(guarantees(r) and max(get(r, 'hd_fwd_hi_rel', 1), get(r, 'hd_back_hi_rel', 1)) < 0.01
                                                         and (no_solid(r) or float(get(r, 'iou', 0)) > 0.99) for r in xs),
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
