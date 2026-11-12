# Alpha Wrap 3.0 phase 1: lever scenarios on f800-machine-equivalent times (T / MF).
# Inputs: prof_over.jsonl (the 189 over budget in f800), prof_rest80.jsonl (80 of the 611 others, seed 1).
import json, statistics as st, importlib.util, sys
S = sys.argv[1] if len(sys.argv) > 1 else '.'
spec = importlib.util.spec_from_file_location("a", S + "/analyze_aw3.py")
MF = 1.136   # clean master here / f800 run, median of 16 meshes (cal.jsonl)
def us(ph, phase, k): return ph.get(f"wrap.prof.{phase}.{k}", 0) / 1000.0
W = ["wrap", "wrap.pinches", "wrap.quality", "wrap.pinches2"]
def parts(r):
    ph = r["pipe_phases"]; p = {}
    for k in ["isect", "touch", "proj", "insert", "gate", "trial"]: p[k] = sum(us(ph, w, k) for w in W)
    p["full_trial_extra"] = us(ph, "wrap.pinches", "trial")          # pass 1's trials: its labels are discarded when pass 2 runs
    p["red_regions"] = ph.get("simplify.prof.reduce.parallel_wall", 0)
    p["red_serial"] = ph.get("simplify.reduce", 0) - p["red_regions"]
    p["certify"] = ph.get("simplify.certify", 0)
    return r["pipe_ms"], p
def budget(r): return max(5000, 0.05 * r["raw_tris"])
# scenario: dict phase -> factor applied to that phase's time (time = t * f)
SC = [
 ("today (master + KeepClearOf, f800 machine eq.)", {}),
 ("M L1 zero: oracle+touch cost 0", {"isect": 0, "touch": 0, "proj": 0}),
 ("E L1 serial 1.3x (prefilters, caching)", {"isect": 1/1.3, "touch": 1/1.3, "proj": 1/1.3}),
 ("M L2 zero: insertion cost 0", {"insert": 0}),
 ("E L2 1.25x per insertion", {"insert": 0.8}),
 ("M L3 gate tests perfect 4 cores", {"isect": .25, "touch": .25, "proj": .25, "gate": .25}),
 ("E L3 gate tests 2.5x on 4 cores", {"isect": .4, "touch": .4, "proj": .4, "gate": .4}),
 ("M L3b certify perfect 4 cores", {"certify": .25}),
 ("E L3b certify 3x", {"certify": 1/3}),
 ("M L4 trials zero", {"trial": 0}),
 ("E L4 2 of 3 full trials gone (local tracking)", {"trial": 1/3}),
 ("M L5 reduce zero", {"red_regions": 0, "red_serial": 0}),
 ("M L5 reduce serial part perfect 4 cores", {"red_serial": .25}),
 ("E L1+L3+L3b (stacked: L1 1.3x, then 2.5x / 3x)", {"isect": .4/1.3, "touch": .4/1.3, "proj": .4/1.3, "gate": .4, "certify": 1/3}),
 ("E L1+L3+L3b+L4", {"isect": .4/1.3, "touch": .4/1.3, "proj": .4/1.3, "gate": .4, "certify": 1/3, "trial": 1/3}),
 ("E L1+L2+L3+L3b+L4", {"isect": .4/1.3, "touch": .4/1.3, "proj": .4/1.3, "gate": .4, "certify": 1/3, "trial": 1/3, "insert": .8}),
 ("M all levers 1-4 perfect 4 cores (Amdahl)", {"isect": .25, "touch": .25, "proj": .25, "gate": .25, "certify": .25, "trial": .25, "insert": 1}),
]
over = [json.loads(l) for l in open(S + "/prof_over.jsonl")]
rest = [json.loads(l) for l in open(S + "/prof_rest80.jsonl")]
def run(rows, sc):
    out = []
    for r in rows:
        T, p = parts(r); t = T - sum(p[k] * (1 - f) for k, f in sc.items())
        out.append((t / MF, budget(r), T / MF))
    return out
print(f"{'scenario':52s} {'189 over: pass':>15s} {'T/B med':>8s} {'speedup':>8s} | {'80 others: pass':>15s} | {'800 est.':>9s}")
for n, sc in SC:
    o = run(over, sc); q = run(rest, sc)
    po = sum(1 for t, b, _ in o if t <= b); pq = sum(1 for t, b, _ in q if t <= b)
    sp = sum(T for _, _, T in o) / sum(t for t, _, _ in o)
    est = po + pq / len(q) * 611
    print(f"{n:52s} {po:6d}/189 {100*po/189:5.0f}% {st.median(t/b for t,b,_ in o):8.2f} {sp:7.2f}x | {pq:7d}/80 | {est:5.0f} {100*est/800:3.0f}%")
