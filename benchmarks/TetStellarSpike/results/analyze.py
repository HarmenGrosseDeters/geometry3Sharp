import sys,glob,statistics as st,collections
H="name,alpha,floor,G,cfg,status,tets,imp_s,stage1_s,imp_us_per_tet,total_us_per_tet,min,p01,max,p99,min_lt_F,max_gt_G,fail_either,valid,counts".split(",")
def load(pattern):
    rows=[]
    for f in glob.glob(pattern):
        for l in open(f):
            p=l.strip().split(",",len(H)-1)
            if len(p)>=len(H)-1 and p[5] in("Success","PointLimitReached"): rows.append(dict(zip(H,p)))
    return rows
def table(rows,title,order=None):
    F=float(rows[0]["floor"]); G=float(rows[0]["G"])
    by=collections.defaultdict(dict)
    for r in rows: by[r["cfg"]][r["name"]]=r
    common=sorted(set.intersection(*[set(v) for v in by.values()]))
    print("\n### %s  (floor %g, max %g; %d meshes with every config)\n"%(title,F,G,len(common)))
    print("| config | min>=F | max<=G | both | valid | median p01 / p99 | worst min / max | tets vs master (median) | improver us/tet (median) | improver s (sum / max) | pipeline us/tet (median) | violating tets (sum) |")
    print("|---|---:|---:|---:|---:|---|---|---:|---:|---|---:|---:|")
    base=by["master"]
    for c in (order or list(by.keys())):
        if c not in by: continue
        r=[by[c][n] for n in common]; g=lambda k:[float(x[k]) for x in r]
        minok=sum(float(x["min"])>=F-1e-9 for x in r); maxok=sum(float(x["max"])<=G+1e-9 for x in r)
        both=sum(float(x["min"])>=F-1e-9 and float(x["max"])<=G+1e-9 for x in r)
        ratio=st.median([float(by[c][n]["tets"])/float(base[n]["tets"]) for n in common])
        print("| %s | %d | %d | %d | %d | %.1f / %.1f | %.2f / %.1f | %.3f | %.0f | %.0f / %.0f | %.0f | %d |"%(c,minok,maxok,both,sum(x["valid"]=="yes" for x in r),st.median(g("p01")),st.median(g("p99")),min(g("min")),max(g("max")),ratio,st.median(g("imp_us_per_tet")),sum(g("imp_s")),max(g("imp_s")),st.median(g("total_us_per_tet")),sum(int(x["fail_either"]) for x in r)))
ORDER=["master","p1","p2","p3","p4","p1+p2+p3","p1+p4","p2+p4","p3+p4","p1+p2+p3+p4"]
S=sys.argv[1]
table(load(S+"/out/s500_10_150_k*.csv"),"500-mesh sample, alpha/40, floor 10 (the default), max 150",ORDER)
table(load(S+"/out/s500_20_150_*.csv"),"500-mesh sample, alpha/40, floor 20, max 150",ORDER)
table(load(S+"/out/proto_20_150_s*.csv"),"40-mesh subset, alpha/40, floor 20, max 150",ORDER)
table(load(S+"/out/proto_30_140_s*.csv"),"40-mesh subset, alpha/40, floor 30, max 140",ORDER)
table(load(S+"/out/proto_dec_20_150_s*.csv")+[r for r in load(S+"/out/proto_20_150_s*.csv") if r["cfg"] in ("master",)],"40-mesh subset, floor 20 / max 150, refinement floor decoupled (rNN = stage 1 at NN)",None)
