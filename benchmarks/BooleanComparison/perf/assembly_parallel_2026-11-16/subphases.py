"""Sums of the classification / assembly sub-phases (microsecond entries `*_us` of phases_ms, in ms) and of the three phases per
thread count: python3 subphases.py <label>=<file.jsonl[.gz]> ... (the faster run per boolean). Raw rows of docs/perf/BOOLEAN_THREAD_SCALING.md, section 6."""
import json,sys,collections,gzip
def load(p):
    rows={}
    for l in (gzip.open(p,'rt') if p.endswith('.gz') else open(p)):
        l=l.strip()
        if not l.startswith("{"): continue
        j=json.loads(l)
        if "phases_ms" not in j: continue
        k=(j["case"],j["op"]); ms=min(j["ms"]) if isinstance(j["ms"],list) else j["ms"]
        if k not in rows or ms<rows[k][0]: rows[k]=(ms,j)
    return rows
out={}
for a in sys.argv[1:]:
    n,p=a.split("="); rows=load(p); s=collections.OrderedDict(); tot=0
    for k,(ms,j) in rows.items():
        tot+=ms
        for name,v in j["phases_ms"].items():
            if name.endswith("_us"): s[name]=s.get(name,0)+v/1000
            elif name in("classification","assembly","simplify"): s[name]=s.get(name,0)+v
    out[n]=(s,tot)
names=list(out[list(out)[0]][0])
print("total ms",{n:round(out[n][1]) for n in out})
for nm in names: print(f"{nm:34s}",*[f"{out[n][0].get(nm,0):9.0f}" for n in out])
