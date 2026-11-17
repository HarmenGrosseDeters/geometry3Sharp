import json,sys
sys.path.insert(0,'/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark'); import criteria
S=sys.argv[1]
rows={}
for c in "RPQST":
    for l in open(f"{S}/sweep_out/raw/{c}.jsonl"):
        r=json.loads(l); rows[(r['file'][:-4],c)]=r
def own(r): return criteria.repaired(dict(r, after_degenerate_tris=r.get('after_degenerate_tris_own', r.get('after_degenerate_tris'))))
def tag(r):
    if r.get('status')!='closed': return 'nc'
    a='T' if r.get('apply_returned') else 'F'
    s='R' if criteria.repaired(r) else 'D' if own(r) else 'm'
    lost=int(float(r.get('pf_lost_all',0) or 0))
    return a+s+('x' if lost else '.')
for v in ("v0","v1","v2"):
    print(f"\n{v}: R P Q S T per f in (2, 8, 16)")
    for r_ in (4,8,12,16,20,24):
        print(f"r={r_:2d} " + " | ".join(" ".join(tag(rows[(f'sw_{v}_r{r_}_f{f}',c)]) for c in "RPQST") for f in (2,8,16)))
