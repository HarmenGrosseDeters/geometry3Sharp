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

def band(r):
    # the certified Hausdorff upper bound result -> input (relative to the input's diagonal) over offset + alpha (both
    # relative to the diagonal: 1 / BENCH_WRAP_OFFSET + 1 / BENCH_WRAP_ALPHA); None for runs without a wrap
    try:
        return get(r, 'hd_fwd_hi_rel', None) / (1 / float(r['wrap_rel_offset']) + 1 / float(r['wrap_rel_alpha']))
    except (KeyError, TypeError, ValueError, ZeroDivisionError):
        return None

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
    amin = [r['after_angle_min'] for r in xs if 'after_angle_min' in r]
    amax = [r['after_angle_max'] for r in xs if 'after_angle_max' in r]
    ang = [r for r in xs if 'after_angle_lt05' in r]      # results with the angle distribution (not in older runs)
    p01 = [r['after_angle_p01'] for r in xs if 'after_angle_p01' in r]
    bands = [b for b in (band(r) for r in xs) if b is not None]
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
        'angle min: global / median of meshes (deg)': f"{min(amin):.3f} / {q(amin, .5):.2f}" if amin else 'n/a',
        'angle max: global / median of meshes (deg)': f"{max(amax):.1f} / {q(amax, .5):.1f}" if amax else 'n/a',
        'meshes with a triangle < 0.5 deg / > 170 deg': f"{sum(r['after_angle_lt05'] > 0 for r in ang)} / {sum(r['after_angle_gt170'] > 0 for r in ang)}" if ang else 'n/a',
        'triangles < 0.5 / < 1 / < 10 deg (sum)': f"{sum(r['after_angle_lt05'] for r in ang)} / {sum(r['after_angle_lt1'] for r in ang)} / {sum(r['after_angle_lt10'] for r in ang)}" if ang else 'n/a',
        'angle p01 (1 % worst min angle): median / min of meshes (deg)': f"{q(p01, .5):.2f} / {min(p01):.2f}" if p01 else 'n/a',
        'share of triangles < 0.5 / > 170 deg (ppm)': f"{1e6 * sum(r['after_angle_lt05'] for r in ang) / max(1, sum(get(r, 'after_tris', 0) for r in ang)):.1f} / {1e6 * sum(r['after_angle_gt170'] for r in ang) / max(1, sum(get(r, 'after_tris', 0) for r in ang)):.1f}" if ang else 'n/a',
        'band: Hausdorff result->input / (offset + alpha), median / p90 / max': f"{q(bands, .5):.2f} / {q(bands, .9):.2f} / {max(bands):.2f}" if bands else 'n/a',
        'meshes with band > 1': sum(b > 1 for b in bands) if bands else 'n/a',
        'triangles > 150 / > 170 deg (sum)': f"{sum(r['after_angle_gt150'] for r in ang)} / {sum(r['after_angle_gt170'] for r in ang)}" if ang else 'n/a',
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

for (na, ra), (nb, rb) in [(runs[0], other) for other in runs[1:]]:
    # paired, per mesh: the first run against each other run (eg wrap against cgalwrap)
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
        ang = [k for k in both if 'after_angle_lt05' in ra[k] and 'after_angle_lt05' in rb[k]]
        if ang:
            bad = lambda r: r['after_angle_lt05'] + r['after_angle_gt170']
            fewer = [k for k in ang if bad(ra[k]) < bad(rb[k])]; more = [k for k in ang if bad(ra[k]) > bad(rb[k])]
            print(f"  triangles < 0.5 or > 170 deg: fewer in {na} on {len(fewer)} meshes, more on {len(more)} {sorted(more)[:12]}")
            mn = [k for k in ang if 'after_angle_min' in ra[k] and 'after_angle_min' in rb[k]]
            print(f"  min angle higher in {na} on {sum(ra[k]['after_angle_min'] > rb[k]['after_angle_min'] for k in mn)} of {len(mn)} meshes; "
                  f"p01 higher on {sum(get(ra[k], 'after_angle_p01', 0) > get(rb[k], 'after_angle_p01', 0) for k in mn)}")
        bb = [(band(ra[k]), band(rb[k])) for k in both]
        bb = [(x, y) for x, y in bb if x is not None and y is not None and y > 0]
        if bb:
            print(f"  band ratio: median {q([x / y for x, y in bb], .5):.2f}; band higher in {na} on {sum(x > y for x, y in bb)} of {len(bb)} meshes")
