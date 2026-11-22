import json,sys,collections,re
d=json.load(open(sys.argv[1]))
frames=[f['name'] for f in d['shared']['frames']]
dec=re.compile(r'Meshing2Acceptance\.Exact\.|Meshing2Acceptance\.Q\.|Meshing2Acceptance\.Pt\.|System\.Numerics|Run\.InCircle|Run\.Orient|WithinTube|InTube|TubeFilter|ZoneFilter')
isdec=[bool(dec.search(n)) for n in frames]
checks=['Run.Segments','Run.Coverage','Run.Conformity','Run.Quality','Run.Delaunay','Run.Marks','Domain..ctor','Run.Sources','Run.Filters','Run.InventedVertices','Run.Regions','Run.Locate','Run.EdgesAndNeighbours']
which=[next((c for c in checks if c+'(' in n),None) for n in frames]
isexec=['Run.Execute(' in n for n in frames]
tot=collections.Counter(); de=collections.Counter()
for p in d['profiles']:
    if p['type']!='evented': continue
    stack=[]; last=None
    for e in p['events']:
        t=e['at']
        if last is not None and stack and any(isexec[i] for i in stack):
            dt=t-last
            c=next((which[i] for i in stack if which[i]),'other')
            tot[c]+=dt
            if any(isdec[i] for i in stack): de[c]+=dt
        last=t
        if e['type']=='O': stack.append(e['frame'])
        else: stack.pop()
T=sum(tot.values())
print(sys.argv[1].split('/')[-1],'oracle total (both runs) ms',round(T))
for c,v in tot.most_common(8): print('  %-22s %5.1f %% of the oracle; deciding %5.1f %%, locating %5.1f %% of the check'%(c,100*v/T,100*de[c]/v,100-100*de[c]/v))
print('  all: deciding %.1f %%, locating %.1f %%'%(100*sum(de.values())/T,100-100*sum(de.values())/T))
