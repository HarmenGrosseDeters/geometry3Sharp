#!/usr/bin/env python3
"""Tables of the sharp-feature study. usage: analyze2.py <jsonl>... ; medians over the inputs that have every listed variant; ratios paired against A."""
import json, statistics as st, sys
rows={}
for f in sys.argv[1:]:
    for l in open(f):
        r=json.loads(l)
        if r.get('status')=='ok': rows[(r['file'],r['variant'])]=r
def med(xs):
    xs=[x for x in xs if isinstance(x,(int,float)) and x==x and abs(x)!=float('inf')]
    return st.median(xs) if xs else float('nan')
def f(x,p=3): return 'n/a' if x!=x else f"{x:.{p}g}"
names=sorted({k[0] for k in rows})
order=['A','R','B','BS','C1','KA','KR','KP','KU','L','KL','WDC']
for title,grp in (('T1',[n for n in names if not n.startswith('proc:')]),('procedural',[n for n in names if n.startswith('proc:')])):
    vs=[v for v in order if any((n,v) in rows for n in grp)]
    print(f"\n### {title}\n")
    print("| variant | n | tris/A | time s | clean | exact crossings of input (meshes) | vtx dist min/δ | angle min | p01 med | <0.5° / >170° | edge Chamfer δ | ratio to A | edge F 0.1% | crease normal 2δ ° | convex miter mean δ / within 1δ | concave miter mean δ / within 1δ | precision vs miter mean δ / within 3δ |")
    print("|"+"---|"*17)
    for v in vs:
        fs=[n for n in grp if (n,v) in rows and (n,'A') in rows]
        if not fs: continue
        rs=[rows[(n,v)] for n in fs]; a=[rows[(n,'A')] for n in fs]
        cross=[r.get('fin_cross_input') for r in rs]
        crossS = f"{sum(1 for c in cross if c)}" if all(c is not None for c in cross) else ("hits "+str(sum(1 for r in rs if r['hits_input'])))
        ecd=[r['fm_ecd']*r['fm_size']/r['delta'] for r in rs]
        ratio=[r['fm_ecd']/x['fm_ecd'] for r,x in zip(rs,a) if x['fm_ecd']>0]
        print("| "+" | ".join([v,str(len(fs)),f(med([r['tris']/x['tris'] for r,x in zip(rs,a)])),f(med([r['ms']/1000 for r in rs])),f"{sum(1 for r in rs if r['clean'])}/{len(rs)}",crossS,
            f(min(r['vdist_min_over_offset'] for r in rs),2),f(min(r['angle_min'] for r in rs)),f(med([r['angle_p01'] for r in rs])),f"{sum(r['angle_lt05'] for r in rs)} / {sum(r['angle_gt170'] for r in rs)}",
            f(med(ecd)),f(med(ratio)),f(med([r['fm_ef_0001'] for r in rs])),f(med([r['crease_nd_d2_mean'] for r in rs])),
            f"{f(med([r.get('ec_cvx_mi_mean',float('nan')) for r in rs]))} / {f(med([r.get('ec_cvx_mi_w1',float('nan')) for r in rs]))}",
            f"{f(med([r.get('ec_ccv_mi_mean',float('nan')) for r in rs]))} / {f(med([r.get('ec_ccv_mi_w1',float('nan')) for r in rs]))}",
            f"{f(med([r.get('ec_prec_mi_mean',float('nan')) for r in rs]))} / {f(med([r.get('ec_prec_mi_w3',float('nan')) for r in rs]))}"])+" |")
