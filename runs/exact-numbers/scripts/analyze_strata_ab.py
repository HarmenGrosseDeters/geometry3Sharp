# Median-of-n before / after of whole operations per stratum, wall time and GC / allocation separate.
# Usage: python3 analyze_strata_ab.py <dir> <stratum> <n>      rows <dir>/<stratum>_{base,pr}_<i>.jsonl
import json, sys, statistics as st
d, stratum, n = sys.argv[1], sys.argv[2], int(sys.argv[3])
def load(v, i):
    out = {}
    for l in open(f"{d}/{stratum}_{v}_{i}.jsonl"):
        r = json.loads(l)
        if "ms" in r: out[(r["case"], r["op"])] = r
    return out
runs = {v: [load(v, i) for i in range(1, n + 1)] for v in ("base", "pr")}
keys = sorted(set.intersection(*[set(r) for v in runs for r in runs[v]]))
def med(v, k, f): return st.median(r[k][f] for r in runs[v])
tot = {v: {f: sum(med(v, k, f) for k in keys) for f in ("ms", "gc_pause_ms", "alloc_mb")} for v in runs}
# the spread of the total between the runs of one variant (the noise the difference must exceed)
spread = {v: [sum(r[k]["ms"] for k in keys) for r in runs[v]] for v in runs}
same = sum(1 for k in keys if all(r[k]["hash"] == runs["base"][0][k]["hash"] for v in runs for r in runs[v]))
print(f"| {stratum} | {len(keys)} | {tot['base']['ms'] / 1000:.2f} -> {tot['pr']['ms'] / 1000:.2f} ({100 * (tot['pr']['ms'] / tot['base']['ms'] - 1):+.1f} %) | "
      f"{min(spread['base']) / 1000:.2f}-{max(spread['base']) / 1000:.2f} / {min(spread['pr']) / 1000:.2f}-{max(spread['pr']) / 1000:.2f} | "
      f"{tot['base']['gc_pause_ms'] / 1000:.2f} -> {tot['pr']['gc_pause_ms'] / 1000:.2f} | {tot['base']['alloc_mb'] / 1024:.2f} -> {tot['pr']['alloc_mb'] / 1024:.2f} | {same} / {len(keys)} |")
