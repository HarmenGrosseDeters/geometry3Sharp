# ab2.py <ab.jsonl>: B vs C per mesh: identical output, pipe / wrap / touch / intersection sums
import json, sys, statistics as st
d = {}
for l in open(sys.argv[1]):
    x = json.loads(l); r = x["row"]; d.setdefault(r["file"], {})[x["v"]] = r
def ph(r, suf): return sum(v for k, v in r.get("pipe_phases", {}).items() if k.endswith("/" + suf))
same = diff = 0; S = {}
rp = []
for f, v in sorted(d.items()):
    if "B" not in v or "C" not in v or "pipe_ms" not in v["B"] or "pipe_ms" not in v["C"]:
        print("incomplete", f); continue
    B, C = v["B"], v["C"]
    eq = B.get("after_hash") == C.get("after_hash") and B["raw_tris"] == C["raw_tris"]
    same += eq; diff += not eq
    rp.append(C["pipe_ms"] / B["pipe_ms"])
    row = {}
    for name, get in [("pipe", lambda r: r["pipe_ms"]), ("wrap", lambda r: r["wrap_ms"]), ("touch", lambda r: ph(r, "touch")),
                      ("inter", lambda r: ph(r, "intersection")), ("insert", lambda r: ph(r, "insert")), ("cert", lambda r: r.get("pipe_phases", {}).get("certify", 0))]:
        b, c = get(B), get(C); S.setdefault(name, [0, 0]); S[name][0] += b; S[name][1] += c; row[name] = (b, c)
    print(f"{f:14s} {'same' if eq else 'DIFF'} " + " ".join(f"{k} {b}->{c}" for k, (b, c) in row.items()))
print(f"identical {same}/{same+diff}; pipe ratio median {st.median(rp):.3f}")
for k, (b, c) in S.items(): print(f"  {k:7s} sum {b:8d} -> {c:8d}  ratio {c/max(b,1):.3f}")
