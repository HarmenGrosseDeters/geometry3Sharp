# Alpha Wrap 3.0 phase 1: per-phase profile and upper bounds per lever from the instrumented wrappipe rows.
# usage: analyze_aw3.py <rows.jsonl> [...]
import json, sys, statistics as st
def us(ph, phase, k): return ph.get(f"wrap.prof.{phase}.{k}", 0) / 1000.0
rows = []
for fn in sys.argv[1:]:
    for l in open(fn):
        r = json.loads(l)
        if "pipe_phases" not in r: print("no result:", r.get("file"), r.get("status")); continue
        rows.append(r)
def parts(r):
    ph = r["pipe_phases"]; T = r["pipe_ms"]
    p = {}
    W = ["wrap", "wrap.pinches", "wrap.quality", "wrap.pinches2"]
    for k in ["isect", "touch", "proj", "insert", "gate", "trial", "points"]:
        p[k] = sum(us(ph, w, k) for w in W)
    p["setup"] = ph.get("wrap.setup", 0)
    carve = sum(us(ph, w, "carve") for w in W)
    wrapPhases = sum(ph.get(w, 0) for w in W)
    p["carve_other"] = carve - sum(us(ph, w, k) for w in W for k in ["isect", "touch", "proj", "gate"]) - sum(us(ph, w, "insert") for w in W)
    # (pinch / quality points inserted outside refine_and_carve are in insert; the remainder of the passes)
    p["pass_other"] = wrapPhases - carve - p["trial"] - p["points"]
    p["manifold"] = ph.get("wrap.manifold", 0); p["extract"] = ph.get("wrap.extract", 0)
    p["red_setup"] = ph.get("simplify.prof.reduce.setup", 0)
    p["red_parallel"] = ph.get("simplify.prof.reduce.parallel_wall", 0)
    p["red_round_serial"] = ph.get("simplify.prof.reduce.rounds", 0) - p["red_parallel"]
    p["red_last"] = ph.get("simplify.prof.reduce.last", 0)
    p["red_other"] = ph.get("simplify.reduce", 0) - p["red_setup"] - ph.get("simplify.prof.reduce.rounds", 0) - p["red_last"]
    p["check"] = ph.get("simplify.check", 0); p["certify"] = ph.get("simplify.certify", 0)
    p["rest"] = T - sum(p.values())
    return T, p
def budget(r): return max(5000, 0.05 * r["raw_tris"])
groups = {}
for r in rows:
    T, p = parts(r)
    groups.setdefault("over" if T > budget(r) else "within", []).append((r, T, p))
allg = [x for g in groups.values() for x in g]
print(f"rows {len(rows)}: over budget {len(groups.get('over', []))}, within {len(groups.get('within', []))}")
keys = list(parts(rows[0])[1].keys())
for name, g in [("all", allg)] + sorted(groups.items()):
    tot = sum(T for _, T, _ in g)
    print(f"\n== {name}: {len(g)} meshes, sum {tot/1000:.0f} s, median {st.median(T for _,T,_ in g)/1000:.2f} s")
    print("  share: " + ", ".join(f"{k} {100*sum(p[k] for _,_,p in g)/tot:.1f}%" for k in keys))
# levers: (name, phases removed, phases that scale on 4 cores)
L = {
 "L1 oracle+touch (zero)": dict(zero=["isect", "touch", "proj"]),
 "L2 insertion (zero)": dict(zero=["insert"]),
 "L3 gate tests 4 cores": dict(par=["isect", "touch", "proj", "gate"]),
 "L3 + certify 4 cores": dict(par=["isect", "touch", "proj", "gate", "certify"]),
 "L4 pinch trials (zero)": dict(zero=["trial", "points"]),
 "L4 pinch trials 4 cores": dict(par=["trial", "points"]),
 "L5 reduce (zero)": dict(zero=["red_setup", "red_parallel", "red_round_serial", "red_last", "red_other"]),
 "L5 reduce serial parts 4 cores": dict(par=["red_round_serial", "red_last", "red_setup"]),
 "certify (zero)": dict(zero=["certify"]),
 "check (zero)": dict(zero=["check"]),
 "L1+L3+certify 4 cores, L2 2x": dict(par=["isect", "touch", "proj", "gate", "certify"], half=["insert"]),
 "all but insert+carve_other 4 cores": dict(par=["isect","touch","proj","gate","certify","trial","points","red_round_serial","red_last","red_setup","check","manifold","extract"]),
}
over = groups.get("over", [])
print(f"\n== upper bounds (time if the phase costs zero, or scales perfectly on 4 cores); over-budget set {len(over)}, all {len(allg)}")
print(f"{'lever':40s} {'over: sum':>10s} {'gain':>6s} {'pass':>8s} {'p.med T/B':>9s} | {'all: sum':>9s} {'gain':>6s}")
def apply(T, p, spec):
    t = T
    for k in spec.get("zero", []): t -= p[k]
    for k in spec.get("par", []): t -= 0.75 * p[k]
    for k in spec.get("half", []): t -= 0.5 * p[k]
    return t
for n, spec in [("master today", {})] + list(L.items()):
    to = [(apply(T, p, spec), budget(r)) for r, T, p in over]
    so = sum(t for t, _ in to); so0 = sum(T for _, T, _ in over)
    sa = sum(apply(T, p, spec) for _, T, p in allg); sa0 = sum(T for _, T, _ in allg)
    passed = sum(1 for t, b in to if t <= b)
    print(f"{n:40s} {so/1000:9.0f}s {so0/max(so,1):5.2f}x {passed:4d}/{len(over):<3d} {st.median(t/b for t,b in to):9.2f} | {sa/1000:8.0f}s {sa0/max(sa,1):5.2f}x")
