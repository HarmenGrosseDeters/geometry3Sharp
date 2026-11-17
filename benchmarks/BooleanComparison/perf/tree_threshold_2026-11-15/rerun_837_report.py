# Report of rerun_837_variants_2026-11-15.jsonl.gz: python3 rerun_837_report.py <file.jsonl.gz>
import gzip,json,collections,sys,statistics
pass
rows=[json.loads(l) for l in gzip.open(sys.argv[1], "rt") if l.strip()]
P=["intersections.trees","classification","rounding","assembly","triangulation","intersections.pairs"]
by=collections.defaultdict(list)
for r in rows: by[(r['threads'],r['variant'],r['round'])].append(r)
def agg(t,v,rd,f): return sum(f(r) for r in by[(t,v,rd)])
hashes=collections.defaultdict(set)
for r in rows: hashes[r['case']].add(r['hash'])
print("rows",len(rows),"cases with >1 hash:",sum(1 for h in hashes.values() if len(h)>1))
for t in (4,1):
    print(f"\n## {t} thread(s): sums over the cases per round (ms) and mean; change against 262144")
    base={}
    for ph in P+['total','gc_pause','gc_count']:
        line=[]
        for v in (262144,16384,8192):
            per=[]
            for rd in (1,2,3):
                n=len(by[(t,v,rd)])
                if ph=='total': per.append(agg(t,v,rd,lambda r:r['ms'][len(r['ms'])//2] if False else sorted(r['ms'])[len(r['ms'])//2]))
                elif ph=='gc_pause': per.append(agg(t,v,rd,lambda r:r['gc']['pause_ms']))
                elif ph=='gc_count': per.append(agg(t,v,rd,lambda r:r['gc']['gen0']+r['gc']['gen1']+r['gc']['gen2']))
                else: per.append(agg(t,v,rd,lambda r:r['phases_ms'].get(ph,0)))
            m=sum(per)/3
            if v==262144: base[ph]=m
            line.append(f"{v}: {per} mean {m:.0f} ({100*(m/base[ph]-1):+.1f}%)" if base[ph] else f"{v}: {per}")
        print(f"- {ph}: "+" | ".join(line))
    # classification outliers
    print("classification per case (mean over rounds), largest |8192 - 262144|:")
    cs=collections.defaultdict(lambda: collections.defaultdict(list))
    for r in rows:
        if r['threads']==t: cs[r['case']][r['variant']].append(r['phases_ms'].get('classification',0))
    d=sorted(((statistics.mean(v[8192])-statistics.mean(v[262144]),c,statistics.mean(v[262144]),statistics.mean(v[16384]),statistics.mean(v[8192])) for c,v in cs.items() if v[8192] and v[262144]),key=lambda x:-abs(x[0]))[:5]
    for x in d: print("   ",x[1],f"262144 {x[2]:.0f}  16384 {x[3]:.0f}  8192 {x[4]:.0f}")
