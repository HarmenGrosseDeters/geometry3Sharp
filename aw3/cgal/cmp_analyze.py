# cmp_analyze.py <cmp.jsonl> <f800_aw3.jsonl>: CGAL alpha_wrap_3 vs MeshAlphaWrap (raw wrap) and the g3 pipeline, per group
import json, sys, statistics as st
d = {}
for l in open(sys.argv[1]):
    x = json.loads(l); d.setdefault(x['row']['file'], {})[x['v']] = x['row']
pipe = {json.loads(l)['file']: json.loads(l) for l in open(sys.argv[2])}
tail = sorted(pipe.values(), key=lambda r: -r['pipe_ms'])[:20]; tailset = {r['file'] for r in tail}
def ok(r): return r.get('status') == 'closed' and r.get('after_valid', True)
def med(v): v = [x for x in v if x is not None]; return st.median(v) if v else float('nan')
for name, sel in [('tail 20', lambda f: f in tailset), ('random 30', lambda f: f not in tailset)]:
    fs = [f for f in d if sel(f) and 'cgalwrap' in d[f] and 'wrap' in d[f]]
    if not fs: continue
    C = [d[f]['cgalwrap'] for f in fs]; W = [d[f]['wrap'] for f in fs]; P = [pipe[f] for f in fs]
    ratio = [w.get('wrap_ms', 0) / max(c.get('wrap_ms', 1), 1) for c, w in zip(C, W)]
    print(f"\n## {name} ({len(fs)} meshes; CGAL 5.6 single-threaded; g3 default threads = 4 on 4 cores; one process at a time)")
    print("| | CGAL alpha_wrap_3 | g3 raw wrap | g3 pipeline (wrap + simplify) |")
    print("|---|---:|---:|---:|")
    print(f"| guarantees (closed, valid) | {sum(ok(r) for r in C)}/{len(C)} | {sum(ok(r) for r in W)}/{len(W)} | {sum(ok(r) for r in P)}/{len(P)} |")
    print(f"| time median / max (s) | {med(r.get('wrap_ms') for r in C)/1000:.1f} / {max(r.get('wrap_ms',0) for r in C)/1000:.1f} | {med(r.get('wrap_ms') for r in W)/1000:.1f} / {max(r.get('wrap_ms',0) for r in W)/1000:.1f} | {med(r['pipe_ms'] for r in P)/1000:.1f} / {max(r['pipe_ms'] for r in P)/1000:.1f} |")
    print(f"| time sum (s) | {sum(r.get('wrap_ms',0) for r in C)/1000:.0f} | {sum(r.get('wrap_ms',0) for r in W)/1000:.0f} | {sum(r['pipe_ms'] for r in P)/1000:.0f} |")
    print(f"| g3 raw / CGAL time, median (min, max) | | {med(ratio):.2f} ({min(ratio):.2f}, {max(ratio):.2f}) | |")
    print(f"| triangles, median | {med(r.get('after_tris') for r in C)/1000:.0f}k | {med(r.get('after_tris') for r in W)/1000:.0f}k | {med(r.get('after_tris') for r in P)/1000:.0f}k |")
    print(f"| min angle: global / median (deg) | {min(r.get('after_angle_min',99) for r in C):.2f} / {med(r.get('after_angle_min') for r in C):.2f} | {min(r.get('after_angle_min',99) for r in W):.2f} / {med(r.get('after_angle_min') for r in W):.2f} | {min(r.get('after_angle_min',99) for r in P):.2f} / {med(r.get('after_angle_min') for r in P):.2f} |")
    print(f"| p01 angle median (deg) | {med(r.get('after_angle_p01') for r in C):.1f} | {med(r.get('after_angle_p01') for r in W):.1f} | {med(r.get('after_angle_p01') for r in P):.1f} |")
    print(f"| max angle: global (deg) | {max(r.get('after_angle_max',0) for r in C):.1f} | {max(r.get('after_angle_max',0) for r in W):.1f} | {max(r.get('after_angle_max',0) for r in P):.1f} |")
    print(f"| certified Hausdorff result -> input, median (upper, % diag) | {100*med(r.get('hd_fwd_hi_rel') for r in C):.3f} | {100*med(r.get('hd_fwd_hi_rel') for r in W):.3f} | {100*med(r.get('hd_fwd_hi_rel') for r in P):.3f} |")
    print(f"| certified Hausdorff input -> result, median (upper, % diag) | {100*med(r.get('hd_back_hi_rel') for r in C):.3f} | {100*med(r.get('hd_back_hi_rel') for r in W):.3f} | {100*med(r.get('hd_back_hi_rel') for r in P):.3f} |")
    print("\nper mesh: file, CGAL s, g3 raw s, g3 pipeline s, CGAL / g3 raw / g3 pipeline triangles")
    for f, c, w, p in sorted(zip(fs, C, W, P), key=lambda x: -x[1].get('wrap_ms', 0)):
        print(f"  {f} {c.get('wrap_ms',0)/1000:.1f} {w.get('wrap_ms',0)/1000:.1f} {p['pipe_ms']/1000:.1f}  {c.get('after_tris')} {w.get('after_tris')} {p.get('after_tris')}  {c.get('status')} {w.get('status')}")
