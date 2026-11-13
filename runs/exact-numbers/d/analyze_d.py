import json, statistics, sys
S='/tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/results/'+sys.argv[1]+'/'
A, B = sys.argv[2], sys.argv[3]
def rows(prefix, v):
    out={}
    for i in range(1,6):
        try:
            for l in open(S+'%s_%s_%d.jsonl'%(prefix,v,i)):
                r=json.loads(l); out.setdefault((r['case'],r['op']),[]).append(r)
        except FileNotFoundError: pass
    return out
med=statistics.median
print('| case | op | runs | %s s (range) | %s s (range) | change | GC pause s | allocated GB | hashes equal |' % (A, B))
print('|---|---|---|---|---|---|---|---|---|')
for prefix in ('t55928','mt'):
    b=rows(prefix,A); n=rows(prefix,B)
    for k in sorted(b):
        o=[r['ms']/1000 for r in b[k]]; x=[r['ms']/1000 for r in n.get(k,[])]
        if not x: continue
        print('| %s | %d | %d/%d | %.1f (%.1f-%.1f) | %.1f (%.1f-%.1f) | %+.1f %% | %.1f -> %.1f | %.1f -> %.1f | %s |' % (k[0],k[1],len(o),len(x),med(o),min(o),max(o),med(x),min(x),max(x),100*(med(x)/med(o)-1), med([r['gc_pause_ms'] for r in b[k]])/1000, med([r['gc_pause_ms'] for r in n[k]])/1000, med([r['alloc_mb'] for r in b[k]])/1024, med([r['alloc_mb'] for r in n[k]])/1024, all(p['hash']==q['hash'] and p['status']==q['status'] for p in b[k] for q in n[k])))
print()
for prefix,name in (('npairs','normal, 20 pairs'),('ncopy','normal, 13 models with a rotated copy')):
    b=rows(prefix,A); n=rows(prefix,B)
    if not b or not n: continue
    tb=sum(med([r['ms'] for r in v]) for v in b.values())/1000; tn=sum(med([r['ms'] for r in v]) for v in n.values())/1000
    rb=[sum(v[i]['ms'] for v in b.values())/1000 for i in range(min(len(v) for v in b.values()))]
    rn=[sum(v[i]['ms'] for v in n.values())/1000 for i in range(min(len(v) for v in n.values()))]
    gb=sum(med([r['gc_pause_ms'] for r in v]) for v in b.values())/1000; gn=sum(med([r['gc_pause_ms'] for r in v]) for v in n.values())/1000
    ab=sum(med([r['alloc_mb'] for r in v]) for v in b.values())/1024; an=sum(med([r['alloc_mb'] for r in v]) for v in n.values())/1024
    eq=sum(all(p['hash']==q['hash'] for p in b[k] for q in n[k]) for k in b)
    print('| %s | %d ops | sum of medians %.2f -> %.2f s (%+.1f %%) | run totals %.2f-%.2f / %.2f-%.2f s | GC pause %.2f -> %.2f s | allocated %.2f -> %.2f GB | hashes equal %d / %d |' % (name, len(b), tb, tn, 100*(tn/tb-1), min(rb), max(rb), min(rn), max(rn), gb, gn, ab, an, eq, len(b)))
