import json,collections,sys
def L(fs):
    out=[]
    for f in fs:
        for l in open(f): out.append(json.loads(l))
    return out
proc=['results/spikeA_proc.jsonl','results/spikeA_proc_plates.jsonl','results/spikeA_proc_cylinder.jsonl','results/spikeA_proc_sphere.jsonl','results/spikeA_proc_box_far.jsonl','results/spikeA_proc_run1_partial.jsonl']
by=collections.defaultdict(dict)
for r in L(proc):
    if 'variant' in r and 'tris' in r: by[(r['input'],r['alpha_den'],r['offset_rel'])].setdefault(r['variant'],r)
names=['box','box_far','cylinder','sphere','plate1e-2','plate1e-3','plate1e-4','lshape','slot0.20','slot0.05','slot0.01']
def H(r): return max(r['h_res_in'],r['h_in_res'])/r['diag']
def fmt(x): return '%.2e'%x
print('| input | alpha | offset | wrap tris | wrap s | wrap H | snapped H (safe) | naive clean? | safe moved fully | safe verdict |')
print('|---|---|---|---:|---:|---:|---:|---|---:|---|')
for n in names:
    for a in (20,100):
        for o in (1e-3,1e-4):
            v=by.get((n,a,o))
            if not v or 'safe' not in v: continue
            print('| %s | d/%d | %g | %d | %.2f | %s | %s | %s | %.1f%% | %s |'%(n,a,o,v['wrap']['tris'],v['wrap']['ms']/1000,fmt(H(v['wrap'])),fmt(H(v['safe'])),v['naive']['verdict'] if v['naive']['verdict']=='clean' else 'no ('+v['naive']['verdict']+('; self-int %d'%v['naive']['selfint'] if v['naive'].get('selfint',0) else '')+')',100*v['safe']['moved_full'],v['safe']['verdict']+(', self-int 0' if v['safe'].get('selfint',1)==0 else '')))
