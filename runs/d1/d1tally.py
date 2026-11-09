import sys,re
from collections import Counter
# per remaining degenerate triangle: the attempts of its last FIX block and their outcome codes
for f in sys.argv[1:]:
    L=open(f).read().split('\n')
    rem=[l.split()[1] for l in L if l.startswith('REMAINING')]
    last={}
    for i,l in enumerate(L):
        if l.startswith('FIX '): last[l.split()[1]]=i
    tot=Counter(); nofix=0
    for t in rem:
        if t not in last: nofix+=1; continue
        i=last[t]+1; cur=None
        while i<len(L) and not L[i].startswith('FIX') and not L[i].startswith('REMAINING'):
            l=L[i]
            if re.match(r'R\d',l): tot[(cur,l)]+=1; cur=cur+'*' if cur else None
            elif l: 
                if cur and not cur.endswith('*'): tot[(cur,'pre')]+=1
                cur=l.split()[0]
            i+=1
    print(f.split('/')[-2],f.split('_')[-1][:-4],'remaining',len(rem),'never fixed-tried',nofix,dict(sorted(((f"{a}:{b}",v) for (a,b),v in tot.items()),key=lambda kv:-kv[1])[:10]))
