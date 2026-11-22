# Before / after table of the kept boxes of `near`'s pending list (MeshRoundingRepair, 2026-11-22; README, "The pending
# list of the rounding's box query").
#   near_pending_boxes_report.py <base.jsonl> <kept.jsonl> [<label of the second column>]
# (also the table of the separating axes of the full pair search: perf/first_search_axes_2026-11-22, label "with the axes")
# Each file: the lines of `BooleanComparison profile --repeat 3 --median --safe-rounding <case>...` (one per operation;
# "ms" holds the three times, "phases_ms" the phases of the median run). Prints per operation the median boolean and
# its rounding phase, and whether the hashes of all runs are equal on both sides.
import json, sys

def load(path):
    rows = {}
    for line in open(path):
        if line.startswith("{"):
            d = json.loads(line)
            rows[(d["case"].rstrip("/").split("/")[-1], d["op"])] = d
    return rows

def median(v):
    return sorted(v)[len(v) // 2]

base, kept = load(sys.argv[1]), load(sys.argv[2])
label = sys.argv[3] if len(sys.argv) > 3 else "with the kept boxes"
print("| operation | boolean (median of 3) | %s | rounding | %s | hashes |" % (label, label))
print("|---|---:|---:|---:|---:|---|")
tb = tk = rb = rk = same = 0
for k, b in base.items():
    n = kept[k]
    mb, mk = median(b["ms"]), median(n["ms"])
    r0, r1 = b["phases_ms"].get("rounding", 0), n["phases_ms"].get("rounding", 0)
    eq = set(b["hash"].split()) == set(n["hash"].split()) and len(set(b["hash"].split())) == 1
    tb += mb; tk += mk; rb += r0; rk += r1; same += eq
    print("| %s %s | %.2f s | %.2f s (%+.1f %%) | %.2f s | %.2f s | %s |" % (k[0], k[1], mb / 1000, mk / 1000, 100 * (mk - mb) / mb, r0 / 1000, r1 / 1000, "equal" if eq else "**differ**"))
print("| **sum of %d** | %.1f s | %.1f s (%+.1f %%) | %.1f s | %.1f s (%+.1f %%) | %d of %d equal |" % (
    len(base), tb / 1000, tk / 1000, 100 * (tk - tb) / tb, rb / 1000, rk / 1000, 100 * (rk - rb) / rb, same, len(base)))
