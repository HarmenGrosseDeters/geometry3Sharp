# ab_analyze.py <ab.jsonl>: baseline B vs candidate C per mesh: identical output (after_hash, raw triangles), time ratios
import json, sys, statistics as st
d = {}
for l in open(sys.argv[1]):
    x = json.loads(l); r = x["row"]; d.setdefault(r["file"], {})[x["v"]] = r
def trial(r): ph = r["pipe_phases"]; return ph.get("wrap.pinches/trial", 0) + ph.get("wrap.pinches2/trial", 0)
def manif(r): return r["pipe_phases"].get("wrap.manifold", 0)
same = diff = 0; rp = []; rt = []; rw = []; sb = sc = 0; tb = tc = 0
for f, v in sorted(d.items()):
    if "B" not in v or "C" not in v or "pipe_ms" not in v["B"] or "pipe_ms" not in v["C"]:
        print("incomplete", f); continue
    B, C = v["B"], v["C"]
    eq = B.get("after_hash") == C.get("after_hash") and B["raw_tris"] == C["raw_tris"]
    same += eq; diff += not eq
    rp.append(C["pipe_ms"] / B["pipe_ms"]); rw.append(C["wrap_ms"] / B["wrap_ms"])
    if trial(B) > 0: rt.append((trial(C) + manif(C)) / max(1, trial(B) + manif(B)))
    sb += B["pipe_ms"]; sc += C["pipe_ms"]; tb += trial(B) + manif(B); tc += trial(C) + manif(C)
    print(f"{f:14s} {'same' if eq else 'DIFF'} pipe {B['pipe_ms']:6d} -> {C['pipe_ms']:6d} trial+manifold {trial(B)+manif(B):5d} -> {trial(C)+manif(C):5d}"
          + ("" if eq else f"  raw {B['raw_tris']} -> {C['raw_tris']} after {B['after_tris']} -> {C['after_tris']}"))
print(f"identical {same}/{same+diff}; pipe ratio median {st.median(rp):.3f} (sum {sc/sb:.3f}), wrap median {st.median(rw):.3f}; "
      f"trial+manifold median {st.median(rt):.3f} (sum {tc/max(tb,1):.3f})")
