import json, statistics as S, collections, sys
P='/tmp/claude-0/pb/'
LOW={'orient2d':2.7,'orient3d':6.5,'incircle':9,'insphere':26,'arr.orient':5,'arr.incircle':12,'arr.along':4,'arr.compare':5,'winding.orient3d':6.5,'plane_side':2}
NS={'orient2d':11.3,'orient3d':16.6,'incircle':18.6,'insphere':32.0,'arr.orient':12,'arr.incircle':30,'arr.along':10,'arr.compare':12,'winding.orient3d':17,'plane_side':6.9}
cnt=collections.defaultdict(lambda: collections.Counter()); tm=collections.Counter()
for l in open(P+'counts_boolean.jsonl'):
    d=json.loads(l)
    if 'ms' not in d: continue
    for k,(c,e) in (d.get('predicates') or {}).items():
        cnt[d['case']][k]+=c; cnt[d['case']]['x_'+k]+=e
    cnt[d['case']]['plane_side']+=(d.get('counts') or {}).get('winding_plane_sides',0); cnt[d['case']]['x_plane_side']+=(d.get('counts') or {}).get('winding_plane_sides_exact',0)
for l in open(P+'time_boolean.jsonl'):
    d=json.loads(l)
    if 'ms' not in d: continue
    tm[d['case']]+=sorted(d['ms'])[len(d['ms'])//2]
rows=[]
for l in open(P+'prof_boolean.jsonl'):
    r=json.loads(l); b=r['buckets']; T=b['T']; c=r['case']
    if c not in tm: continue
    g=lambda pred: 100*sum(v for k,v in b.items() if pred(k))/T
    predex=g(lambda k:k.startswith('exact|') and k!='exact|other'); cons=g(lambda k:k.startswith('construction')); xo=g(lambda k:k=='exact|other'); fs=g(lambda k:k.startswith('filter|'))
    F=sum(cnt[c][k]*NS[k] for k in NS if k in cnt[c])/1e6   # ms
    Fp=100*F/max(tm[c],1)
    FL=sum(cnt[c][k]*LOW[k] for k in LOW if k in cnt[c])/1e6; FpL=100*FL/max(tm[c],1)
    rows.append(dict(case=c.split('/')[-1],T_ms=tm[c],predex=predex,cons=cons,xo=xo,fs=fs,Fp=Fp,FpL=FpL,
      calls=sum(cnt[c][k] for k in NS if k in cnt[c]),xcalls=sum(cnt[c]['x_'+k] for k in NS if k in cnt[c])))
json.dump(rows,open(P+'table_boolean.json','w'),indent=1)
def q(xs,p): xs=sorted(xs); return xs[min(len(xs)-1,int(round(p*(len(xs)-1))))]
def line(name,f):
    xs=[f(r) for r in rows]; print(f"{name:55s} median {S.median(xs):5.1f}  p95 {q(xs,.95):5.1f}  max {max(xs):5.1f}")
line('(a) predicate exact stages free',lambda r:r['predex'])
line("(a') all exact arithmetic free (pred+construction+other)",lambda r:r['predex']+r['cons']+r['xo'])
line('    of which exact point/plane constructions',lambda r:r['cons'])
line('    of which other BigInteger/exact arithmetic',lambda r:r['xo'])
line('filter time LOW (throughput microbench), % of T',lambda r:r['FpL'])
line('filter time HIGH (call-overhead microbench), % of T',lambda r:r['Fp'])
line('filter time sampled (lower bound, inlined filters invisible)',lambda r:r['fs'])
line('(b) all predicates free = (a)+filter',lambda r:r['predex']+r['Fp'])
line("(b') (a')+filter",lambda r:r['predex']+r['cons']+r['xo']+r['Fp'])
line('(c) filters 2x faster = filter/2 (HIGH)',lambda r:r['Fp']/2)
line('(c) filters 2x faster = filter/2 (LOW)',lambda r:r['FpL']/2)
print('inputs with (a) > 10%:',[r['case'] for r in rows if r['predex']>10])
print("inputs with (a') > 10%:",[(r['case'],round(r['predex']+r['cons']+r['xo'],1)) for r in rows if r['predex']+r['cons']+r['xo']>10])
print('inputs with filter% > 10:',[(r['case'],round(r['Fp'],1)) for r in rows if r['Fp']>10])
tot=collections.Counter()
for c in cnt:
    for k in NS:
        tot[k]+=cnt[c][k]; tot['x_'+k]+=cnt[c]['x_'+k]
for k in NS: print(f"{k:18s} calls {tot[k]:>12d}  exact stage {tot['x_'+k]:>10d}  {100*tot['x_'+k]/max(tot[k],1):5.1f} %")
