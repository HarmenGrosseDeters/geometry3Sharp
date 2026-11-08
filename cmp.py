import json,sys,statistics as st
def load(f): return {r['file']:r for r in map(json.loads,open(f)) if 'file' in r and r.get('status')!='timeout_or_crash'}
cfgs=sys.argv[1:]
D={c:load(f'results/fa_{c}.jsonl') for c in cfgs}
base=D['off']
files=[f for f in base if all(f in D[c] for c in cfgs)]
edge=[f for f in files if base[f].get('fm_ecd') is not None]
print(len(files),'meshes;',len(edge),'with sharp-edge metrics')
def med(xs): return st.median(xs) if xs else float('nan')
for c in cfgs:
    d=D[c]; g=lambda k,fs=edge:[d[f][k] for f in fs if d[f].get(k) is not None]
    row=dict(
     ecd_med=med(g('fm_ecd')), ecd_mean=st.mean(g('fm_ecd')),
     er=med(g('fm_er_0005')),ep=med(g('fm_ep_0005')),ef=med(g('fm_ef_0005')),
     ef001=med(g('fm_ef_001')),
     tris=med(g('after_tris',files)), p01=med(g('after_angle_p01',files)), p01min=min(g('after_angle_p01',files)),
     lt05=sum(d[f]['after_angle_lt05'] for f in files), gt170=sum(d[f]['after_angle_gt170'] for f in files),
     t=med(g('pipe_ms',files))/1000)
    if c!='off':
        w=sum(1 for f in edge if d[f]['fm_ecd']<base[f]['fm_ecd']*0.95); b=sum(1 for f in edge if d[f]['fm_ecd']>base[f]['fm_ecd']*1.05)
        row['ecd_better5%']=w; row['ecd_worse5%']=b
        row['ecd_ratio_med']=med([d[f]['fm_ecd']/base[f]['fm_ecd'] for f in edge if base[f]['fm_ecd']>0])
    print(c,{k:(round(v,5) if isinstance(v,float) else v) for k,v in row.items()})
