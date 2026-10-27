# Summary of a RemeshBenchmark output (JSON lines): per RemesherPro configuration the speedup over Remesher
# (Remesher time / RemesherPro time) and the quality ratios, over the cases matching an optional regex.
#   python3 summarize.py results/run.txt [regex]
import collections, json, re, statistics as st, sys

rows = [json.loads(l) for l in open(sys.argv[1]) if l.startswith("{")]
pat = re.compile(sys.argv[2]) if len(sys.argv) > 2 else None
by = collections.defaultdict(dict)
for r in rows:
    if "config" in r and (pat is None or pat.search(r["case"])):
        by[r["case"]][r["config"]] = r
by = {c: v for c, v in by.items() if "remesher" in v}
print(f"{len(by)} cases")
for cfg in ["pro_fastest", "pro_iter"]:
    cs = [c for c in by if cfg in by[c]]
    if not cs:
        continue
    sp = [by[c]["remesher"]["time_s"] / by[c][cfg]["time_s"] for c in cs]
    total = sum(by[c]["remesher"]["time_s"] for c in cs) / sum(by[c][cfg]["time_s"] for c in cs)
    hd = [by[c][cfg]["hausdorff_rel"] / max(by[c]["remesher"]["hausdorff_rel"], 1e-12) for c in cs]
    bad = sum(by[c][cfg]["q_below_0.1"] for c in cs if "q_below_0.1" in by[c][cfg])
    bad0 = sum(by[c]["remesher"]["q_below_0.1"] for c in cs if "q_below_0.1" in by[c]["remesher"])
    det = sum(1 for c in cs if by[c][cfg]["deterministic"] and by[c][cfg]["serial_equal"])
    det0 = sum(1 for c in cs if by[c]["remesher"]["deterministic"] and by[c]["remesher"]["serial_equal"])
    print(f"{cfg}: speedup median {st.median(sp):.2f} (min {min(sp):.2f}, max {max(sp):.2f}), total time ratio {total:.2f}; "
          f"hausdorff ratio median {st.median(hd):.2f} (max {max(hd):.2f}); "
          f"triangles q < 0.1: {bad} vs {bad0}; deterministic and serial-equal: {det}/{len(cs)} vs {det0}/{len(cs)}")
