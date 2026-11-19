import csv,statistics as st,collections as C,sys
d=sys.argv[1]
ph=C.defaultdict(list)
for r in csv.DictReader(open(d+'/phases.csv')):
    if r['phase'].endswith('_us'): continue
    ph[(int(r['threads']),r['case'],r['op'],r['phase'])].append(float(r['ms']))
tot=C.defaultdict(list)
for r in csv.DictReader(open(d+'/cases.csv')):
    tot[(int(r['threads']),r['case'],r['op'])].append(float(r['ms']))
leaf=['input','setup.points','setup.triangles','intersections.trees','intersections.cull','intersections.pairs','intersections.planes','intersections.intersect','merge','triangulation','classification','simplify','assembly','rounding']
for t in (1,4,12):
    T=sum(st.median(v) for k,v in tot.items() if k[0]==t)
    s={}
    for p in leaf:
        s[p]=sum(st.median(v) for k,v in ph.items() if k[0]==t and k[3]==p)
    s['assembly_wo_simplify']=s['assembly']-s['simplify']
    rest=T-sum(s[p] for p in leaf if p!='simplify')
    print(f"threads {t}: total {T:.0f} ms")
    for p in leaf+['assembly_wo_simplify']:
        print(f"  {p:28s} {s[p]:8.0f} {100*s[p]/T:5.1f}%")
    print(f"  {'rest':28s} {rest:8.0f} {100*rest/T:5.1f}%")
