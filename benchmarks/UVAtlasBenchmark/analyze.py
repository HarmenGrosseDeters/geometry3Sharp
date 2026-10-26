# Summary of a UV atlas benchmark run per category (data/selection*.csv):
#   python3 analyze.py results/auto.jsonl [results/auto_large.jsonl ...]
# Rows without a result (timeout, crash, exception) stay in the counts as failures.
import csv, json, os, statistics, sys

here = os.path.dirname(os.path.abspath(__file__))
cat = {}
for name in ["selection.csv", "selection_large.csv"]:
    p = os.path.join(here, "data", name)
    if os.path.exists(p):
        for r in csv.DictReader(open(p)):
            cat[os.path.basename(r["path"])] = r["category"]

def pct(xs, q):
    if not xs: return float("nan")
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(q * len(xs)))]

def summary(rows):
    n = len(rows)
    st = {}
    for r in rows: st[r.get("status")] = st.get(r.get("status"), 0) + 1
    done = [r for r in rows if r.get("status") in ("Success", "DistortionLimitExceeded", "Failed")]
    ms = [r["ms"] / 1000 for r in done]
    comps = sum(r["components"] for r in done)
    conec = sum(r["cone_components"] for r in done)
    tris = sum(r["faces"] for r in done)
    conet = sum(r["chart_method_triangles"].get("cones", 0) for r in done)
    methods = {}
    for r in done:
        for m, k in r["chart_methods"].items(): methods[m] = methods.get(m, 0) + k
    overl = sum(1 for r in done if r.get("overlap_pairs", 0) > 0 or r.get("inverted", 0) > 0)
    return {
        "meshes": n,
        "Success": st.get("Success", 0),
        "DistortionLimitExceeded": st.get("DistortionLimitExceeded", 0),
        "Failed": st.get("Failed", 0),
        "InvalidMesh / load_failed": st.get("InvalidMesh", 0) + st.get("load_failed", 0),
        "timeout / crash / exception": st.get("timeout_or_crash", 0) + st.get("exception", 0),
        "with overlaps or inverted UVs": overl,
        "time median / p90 / max (s)": "%.2f / %.2f / %.1f" % (pct(ms, 0.5), pct(ms, 0.9), max(ms) if ms else float("nan")),
        "components flattened with cones": "%d of %d (%.0f%%)" % (conec, comps, 100.0 * conec / comps if comps else 0),
        "triangles in cone charts": "%.0f%%" % (100.0 * conet / tris if tris else 0),
        "charts per mesh median / p90": "%d / %d" % (pct([r["charts"] for r in done], 0.5), pct([r["charts"] for r in done], 0.9)),
        "charts by method": ", ".join("%s %d" % kv for kv in sorted(methods.items(), key=lambda kv: -kv[1])),
        "meshes with charts over the limits": sum(1 for r in done if r["charts_over_limit"] > 0),
        "coverage median": "%.2f" % pct([r["coverage"] for r in done], 0.5),
        "seam length / sqrt(area) median": "%.1f" % pct([r["seam_rel"] for r in done], 0.5),
        "mean conformal distortion median / p90": "%.3f / %.3f" % (pct([r["mean_conformal"] for r in done if "mean_conformal" in r], 0.5),
                                                               pct([r["mean_conformal"] for r in done if "mean_conformal" in r], 0.9)),
        "mean area distortion median / p90": "%.3f / %.3f" % (pct([r["mean_area"] for r in done if "mean_area" in r], 0.5),
                                                          pct([r["mean_area"] for r in done if "mean_area" in r], 0.9)),
        "meshes with float rounding defects": sum(1 for r in done if r.get("float_defects", 0) > 0),
    }

rows = [json.loads(l) for f in sys.argv[1:] for l in open(f) if l.strip()]
groups = {}
for r in rows:
    groups.setdefault(cat.get(r["file"], "?"), []).append(r)
order = [c for c in ["clean", "open", "multi", "broken", "large", "?"] if c in groups]
cols = order + ["all"]
sums = {c: summary(groups[c]) for c in order}
sums["all"] = summary(rows)
keys = list(sums["all"].keys())
print("| | " + " | ".join(cols) + " |")
print("|---" * (len(cols) + 1) + "|")
for k in keys:
    print("| " + k + " | " + " | ".join(str(sums[c][k]) for c in cols) + " |")
fails = [r for r in rows if r.get("status") not in ("Success", "DistortionLimitExceeded")]
if fails:
    print("\nnot Success / DistortionLimitExceeded:")
    for r in fails:
        print("  %s (%s): %s %s" % (r["file"], cat.get(r["file"], "?"), r.get("status"), (r.get("error") or "")[:120]))
