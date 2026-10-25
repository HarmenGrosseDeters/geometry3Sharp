#!/usr/bin/env python3
"""Summary of a benchmark run with the fidelity metrics (exact self-intersections, certified Hausdorff,
winding-number IoU). A mesh counts as repaired if it is closed, valid, topologically clean (no bowties,
consistent orientation, no duplicate/degenerate triangles, no inside-out components), has no exact
self-intersections and stays close to the input: Hausdorff upper bound < HD (relative to the diagonal) in
both directions and IoU > IOU.
Usage: analyze_fidelity.py results/<run>.jsonl [HD=0.01] [IOU=0.99]"""
import json, sys

f = sys.argv[1]
HD = float(sys.argv[2]) if len(sys.argv) > 2 else 0.01
IOU = float(sys.argv[3]) if len(sys.argv) > 3 else 0.99
rs = [json.loads(l) for l in open(f) if l.strip()]
n = len(rs)

def get(r, k, d=None):
    v = r.get(k, d)
    return d if v is None else v

def clean(r):
    return (r.get('status') == 'closed' and get(r, 'after_valid', False) and get(r, 'after_bowties', 1) == 0
            and get(r, 'after_inconsistent_orient_edges', 1) == 0 and get(r, 'after_duplicate_tris', 1) == 0
            and get(r, 'after_degenerate_tris', 1) == 0 and get(r, 'after_inverted_components', 1) == 0)
def no_selfint(r): return get(r, 'after_selfint_exact', 1) == 0
def faithful(r):
    return (get(r, 'hd_fwd_hi_rel', 1) < HD and get(r, 'hd_back_hi_rel', 1) < HD and get(r, 'iou', 0) > IOU)

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
iou = sorted(get(r, 'iou', 0) for r in rs if 'iou' in r)
if hd:
    q = lambda a, p: a[min(len(a) - 1, int(p * (len(a) - 1)))]
    print(f"  Hausdorff (max of both, rel): median {q(hd, .5):.4f}, p90 {q(hd, .9):.4f}, max {hd[-1]:.4f}; > {HD}: {sum(x >= HD for x in hd)}")
    print(f"  IoU: median {q(iou, .5):.4f}, p10 {q(iou, .1):.4f}, min {iou[0]:.4f}; < {IOU}: {sum(x <= IOU for x in iou)}")
    worst = sorted(rs, key=lambda r: get(r, 'iou', 1))[:5]
    print("  lowest IoU:", [(r['file'], round(get(r, 'iou', 0), 3), round(get(r, 'iou_missing', 0), 3), round(get(r, 'iou_extra', 0), 3)) for r in worst])
ms = sorted(get(r, 'repair_ms', 0) for r in rs if 'repair_ms' in r)
if ms: print(f"  repair time: median {ms[len(ms)//2]/1000:.2f} s, max {ms[-1]/1000:.1f} s")
