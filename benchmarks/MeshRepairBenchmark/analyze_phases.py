# Time per stage of MeshRobustRepair (robust mode): where the repair spends its time, and the outliers per stage.
#   python3 analyze_phases.py results/robust.jsonl [data/selection.csv] [--csv out.csv]
# Prints per stage (robust_phases of each result line): meshes, median, sum and the three slowest meshes (with their
# input triangles and, for the wrap, the triangles of the wrap). --csv writes one row per mesh: category, stage that
# produced the result, why the exact stage was not used, triangles, total time and the time per stage in ms.
import csv, json, statistics, sys

STAGES = ["repair", "check", "volume", "exact.orient", "exact.resolve", "exact.check", "exact.volume",
          "solidify", "solidify.contour", "solidify.selfint", "wrap", "wrap.simplify"]

def stage(r):
    return ("alpha wrap" if r.get("robust_used_wrap") else "solidify" if r.get("robust_used_solidify")
            else "exact resolve" if r.get("robust_used_exact") else "repair")

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out = sys.argv[sys.argv.index("--csv") + 1] if "--csv" in sys.argv else None
    if out in args:
        args.remove(out)
    rows = [json.loads(l) for l in open(args[0]) if l.strip()]
    cat = {}
    if len(args) > 1:
        cat = {r["file_id"] + ".stl": r["category"] for r in csv.DictReader(open(args[1]))}
    total = sum(r.get("repair_ms") or 0 for r in rows)
    print(f"{args[0]}: {len(rows)} meshes, repair time {total / 1000:.0f} s")
    print(f"{'stage':16} {'meshes':>6} {'median s':>9} {'sum s':>7}  slowest (s, input triangles, wrap triangles)")
    for s in STAGES:
        v = [((r.get("robust_phases") or {}).get(s), r) for r in rows]
        v = [(x, r) for x, r in v if x]
        if not v:
            continue
        top = sorted(v, key=lambda x: -x[0])[:3]
        print(f"{s:16} {len(v):6} {statistics.median(x for x, _ in v) / 1000:9.2f} {sum(x for x, _ in v) / 1000:7.0f}  "
              + "; ".join(f"{r['file']} {x / 1000:.0f} ({r.get('before_tris')}, {r.get('robust_wrap_tris') or 0})" for x, r in top))
    if out:
        with open(out, "w", newline="") as f:
            w = csv.writer(f, lineterminator="\n")
            w.writerow(["file", "category", "stage", "exact_reason", "tris_before", "wrap_tris", "total_ms"]
                       + [s.replace(".", "_") + "_ms" for s in STAGES])
            for r in sorted(rows, key=lambda r: r["file"]):
                ph = r.get("robust_phases") or {}
                w.writerow([r["file"], cat.get(r["file"], ""), stage(r), (r.get("robust_exact_reason") or "").replace(",", ";"),
                            r.get("before_tris"), r.get("robust_wrap_tris") or 0, r.get("repair_ms")] + [ph.get(s, "") for s in STAGES])

main()
