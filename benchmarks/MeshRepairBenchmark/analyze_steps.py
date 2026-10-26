#!/usr/bin/env python3
"""Which MeshAutoRepair step makes the repair deviate from the input (results from BENCH_REPAIR=steps).
For every step: in how many meshes it pushes the Hausdorff bound (each direction) over HD, the IoU below IOU,
or leaves the mesh not closed in the winding number sense (counted at the last step that changed it).
Usage: analyze_steps.py results/steps.jsonl [HD=0.01] [IOU=0.99] [file ...]   (files: print their steps)"""
import json, sys, collections
f = sys.argv[1]
HD = float(sys.argv[2]) if len(sys.argv) > 2 else 0.01
IOU = float(sys.argv[3]) if len(sys.argv) > 3 else 0.99
show = set(sys.argv[4:])
rows = [json.loads(l) for l in open(f) if l.strip()]
rows = [r for r in rows if r.get('stages')]
def iou(st):
    v = st.get('iou', 1)
    return 1.0 if v in ('NaN', None) else float(v)
cnt = collections.defaultdict(collections.Counter)
order = []
for r in rows:
    prev = {'fwd': False, 'back': False, 'iou': False}
    for st in r['stages']:
        name = st['stage']
        if name not in order: order.append(name)
        now = {'fwd': st.get('hd_fwd_hi_rel', 0) >= HD, 'back': st.get('hd_back_hi_rel', 0) >= HD, 'iou': iou(st) <= IOU}
        for k in now:
            if now[k] and not prev[k]: cnt[name][k] += 1
            if prev[k] and not now[k]: cnt[name][k + '_fixed'] += 1
        prev = now
    last = r['stages'][-1]
    cnt['(final)']['not_wclosed'] += 0 if last.get('wclosed') else 1
    for k in prev: cnt['(final)'][k] += 1 if prev[k] else 0
    if r['file'] in show:
        print(r['file'])
        for st in r['stages']:
            print(f"  {st['stage']:26} tris {st['tris']:7} bdry {st['boundary_edges']:5} wclosed {str(st.get('wclosed')):5} "
                  f"hd {100 * st.get('hd_fwd_hi_rel', 0):6.2f}% {100 * st.get('hd_back_hi_rel', 0):6.2f}% iou {iou(st):.4f}")
print(f"{f}: {len(rows)} meshes. Meshes where the step pushes the metric over the bound (in brackets: back within it)")
print(f"{'step':26}{'HD result->input':>18}{'HD input->result':>18}{'IoU':>10}")
for name in order + ['(final)']:
    c = cnt[name]
    cell = lambda k: f"{c[k]}" + (f" ({c[k + '_fixed']})" if c[k + '_fixed'] else "")
    print(f"{name:26}{cell('fwd'):>18}{cell('back'):>18}{cell('iou'):>10}")
print(f"not closed in the winding number sense after the repair: {cnt['(final)']['not_wclosed']}")
