import json,sys
def load(f):
    out=[]
    for l in open(f):
        r=json.loads(l); out.append(r)
    return out
a1=load(sys.argv[1]); a4=load(sys.argv[2])
n=min(len(a1),len(a4))
keys=['a.setup','b.initpairs','c.initbad','d.falsecontacts0','e1.fixdeg','e2.pairs','e3.fixint','e4.bad','f.after','g.final','z.apply']
tot1={k:0 for k in keys}; tot4={k:0 for k in keys}; op1=op4=0; tree1=tree4=0
for r1,r4 in zip(a1[:n],a4[:n]):
    for k in keys: tot1[k]+=r1.get('rr.'+k,0); tot4[k]+=r4.get('rr.'+k,0)
    op1+=r1['ms']; op4+=r4['ms']; tree1+=r1.get('rr.x.rebuild_tree(incl)',0); tree4+=r4.get('rr.x.rebuild_tree(incl)',0)
print('ops',n,'op ms 1c',round(op1),'4c',round(op4))
for k in keys: print(f"{k:18s} {tot1[k]:9.0f} {tot4[k]:9.0f}")
print('tree',round(tree1),round(tree4))
