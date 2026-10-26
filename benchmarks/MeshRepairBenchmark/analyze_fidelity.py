#!/usr/bin/env python3
"""Summary of a benchmark run with the fidelity metrics (exact self-intersections, certified Hausdorff,
winding-number IoU). A mesh counts as repaired if it is closed, valid, topologically clean (no bowties,
consistent orientation, no duplicate/degenerate triangles, no inside-out components), has no exact
self-intersections and stays close to the input: Hausdorff upper bound < HD (relative to the diagonal) in
both directions and IoU > IOU.
Usage: analyze_fidelity.py results/<run>.jsonl [HD=0.01] [IOU=0.99]"""
import json, sys
from criteria import get, clean, no_solid, iou, faithful as faithful_hd_iou

f = sys.argv[1]
HD = float(sys.argv[2]) if len(sys.argv) > 2 else 0.01
IOU = float(sys.argv[3]) if len(sys.argv) > 3 else 0.99
rs = [json.loads(l) for l in open(f) if l.strip()]
n = len(rs)

# the criterion is in criteria.py (shared with analyze_paths and analyze_wrap)
def no_selfint(r): return get(r, 'after_selfint_exact', 1) == 0
def faithful(r): return faithful_hd_iou(r, HD, IOU)

c = sum(clean(r) for r in rs)
cs = sum(clean(r) and no_selfint(r) for r in rs)
csf = sum(clean(r) and no_selfint(r) and faithful(r) for r in rs)
print(f"{f}: {n} meshes")
print(f"  closed + topologically clean:            {c}")
print(f"  ... and no exact self-intersections:     {cs}")
print(f"  ... and faithful (HD<{HD}, IoU>{IOU}):   {csf}   <- repaired")
status = {}
for r in rs: status[r.get('status')] = status.get(r.get('status'), 0) + 1
print(f"  status: {status}")
for k in ['after_selfint_exact', 'after_bowties', 'after_degenerate_tris', 'after_inverted_components', 'after_folded_edges_gt175deg']:
    print(f"  meshes with {k}: {sum(1 for r in rs if get(r, k, 0) not in (0, None))}")
hd = sorted(max(get(r, 'hd_fwd_hi_rel', 0), get(r, 'hd_back_hi_rel', 0)) for r in rs if 'hd_fwd_hi_rel' in r)
iou_l = sorted(iou(r) for r in rs if 'iou' in r and not no_solid(r))
print(f"  inputs without a solid (IoU not defined): {sum(1 for r in rs if 'iou' in r and no_solid(r))}")
if hd:
    q = lambda a, p: a[min(len(a) - 1, int(p * (len(a) - 1)))]
    print(f"  Hausdorff (max of both, rel): median {q(hd, .5):.4f}, p90 {q(hd, .9):.4f}, max {hd[-1]:.4f}; > {HD}: {sum(x >= HD for x in hd)}")
    if iou_l:
        print(f"  IoU: median {q(iou_l, .5):.4f}, p10 {q(iou_l, .1):.4f}, min {iou_l[0]:.4f}; < {IOU}: {sum(x <= IOU for x in iou_l)}")
    worst = sorted([r for r in rs if 'iou' in r and not no_solid(r)], key=iou)[:5]
    print("  lowest IoU:", [(r['file'], round(iou(r), 3), round(get(r, 'iou_missing', 0), 3), round(get(r, 'iou_extra', 0), 3)) for r in worst])
ms = sorted(get(r, 'repair_ms', 0) for r in rs if 'repair_ms' in r)
if ms: print(f"  repair time: median {ms[len(ms)//2]/1000:.2f} s, max {ms[-1]/1000:.1f} s")

# Further measures (MeshFidelityMetrics, fm_* fields; not part of the "repaired" criterion): what Hausdorff and IoU
# miss. Flags per mesh, thresholds relative to the input's size (FLAGS below):
#   surface: F-score at 0.5% below 0.95 (parts of either surface farther than 0.5% from the other)
#   reversed: more than 1% of the result's surface near the input has normals against the input's
#   edges: the input has sharp edges and fewer than 90% of them are within 0.5% of the result's sharp edges
#   handles / parts: genus changed, or fewer components (topology changes)
#   triangles: more than 1% of the result's triangles have a min angle below 10 degrees
FLAGS = {
    'surface': lambda r: num(r, 'fm_f_0005', 1) < 0.95,
    'reversed': lambda r: num(r, 'fm_reversed_fwd', 0) > 0.01,
    'edges': lambda r: num(r, 'fm_sharp_in', 0) > 0 and num(r, 'fm_er_0005', 1) < 0.9,
    'handles': lambda r: any(c in str(r.get('fm_topo_changes', '')) for c in ('HandlesClosed', 'HandlesCreated')),
    'parts': lambda r: 'ComponentsLost' in str(r.get('fm_topo_changes', '')),
    'triangles': lambda r: num_q(r, 'minangle_lt10') > 0.01,
}


def num(r, k, d=float('nan')):
    v = r.get(k, d)
    try:
        return float(v)
    except (TypeError, ValueError):
        return d


def num_q(r, k):
    q = r.get('fm_q_out') or {}
    return q.get(k, 0) / max(1, q.get('tris', 1))


fm = [r for r in rs if r.get('fm_ok')]
if fm:
    from criteria import repaired
    rep = [r for r in fm if repaired(r, HD, IOU)]
    q = lambda a, p: a[min(len(a) - 1, int(p * (len(a) - 1)))] if a else float('nan')
    print(f"\nFurther measures (fm_*, relative to the input size; {len(fm)} meshes with them, {len(rep)} of them repaired)")
    print(f"  {'measure':38s} {'all: median':>12s} {'p10/p90':>9s} {'repaired: median':>17s} {'p10/p90':>9s}")
    cols = [('F-score 0.1%', 'fm_f_0001', .1), ('F-score 0.5%', 'fm_f_0005', .1), ('F-score 1%', 'fm_f_001', .1),
            ('Chamfer distance', 'fm_chamfer', .9), ('normal consistency |cos|', 'fm_nc', .1),
            ('reversed share (result to input)', 'fm_reversed_fwd', .9), ('Edge Chamfer distance', 'fm_ecd', .9),
            ('edge recall 0.5%', 'fm_er_0005', .1), ('edge precision 0.5%', 'fm_ep_0005', .1)]
    for name, k, p in cols:
        a = sorted(x for x in (num(r, k) for r in fm) if x == x)
        b = sorted(x for x in (num(r, k) for r in rep) if x == x)
        print(f"  {name:38s} {q(a, .5):12.4f} {q(a, p):9.4f} {q(b, .5):17.4f} {q(b, p):9.4f}")
    a = sorted(r['fm_q_out']['minangle'][1] for r in fm if 'fm_q_out' in r)
    b = sorted(r['fm_q_out']['minangle'][1] for r in rep if 'fm_q_out' in r)
    print(f"  {'min angle, 1% worst (degrees)':38s} {q(a, .5):12.2f} {q(a, .1):9.2f} {q(b, .5):17.2f} {q(b, .1):9.2f}")
    changes = {}
    for r in fm:
        for c in str(r.get('fm_topo_changes', 'None')).split(', '):
            changes[c] = changes.get(c, 0) + 1
    print(f"  topology changes (all): {changes}")
    print(f"  flags (all / repaired):")
    for name, flag in FLAGS.items():
        hit = [r for r in rep if flag(r)]
        print(f"    {name:10s} {sum(1 for r in fm if flag(r)):4d} / {len(hit):4d}   repaired examples: {[r['file'] for r in hit[:6]]}")
    print(f"  repaired with any flag: {sum(1 for r in rep if any(f(r) for f in FLAGS.values()))} of {len(rep)}")
