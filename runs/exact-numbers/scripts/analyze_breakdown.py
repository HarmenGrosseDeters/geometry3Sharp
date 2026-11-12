# Per exact path: share of the stratum's time, calls, time per call and the bytes allocated inside it.
# Usage: python3 analyze_breakdown.py <label>=<rows.jsonl> ...
import json, sys, collections
print("| stratum | path | % of time | calls | ns / call | allocated inside, GB (% of all) |")
print("|---|---|---|---|---|---|")
for a in sys.argv[1:]:
    label, f = a.split("=", 1)
    tot = collections.Counter(); calls = collections.Counter(); mb = collections.Counter(); wall = 0; alloc = 0
    for l in open(f):
        r = json.loads(l)
        if "paths" not in r: continue
        wall += r["ms"]; alloc += r["alloc_mb"]
        for k, v in r["paths"].items():
            if k.startswith("pt."): continue
            tot[k] += max(0, v[1] - v[0] * r["pair_ns"] * 1e-6); calls[k] += v[0]; mb[k] += v[2]
    for k, v in tot.most_common():
        if 100 * v / wall < 0.05: continue
        print(f"| {label} | {k} | {100 * v / wall:.1f} | {int(calls[k])} | {v * 1e6 / max(1, calls[k]):.0f} | {mb[k] / 1024:.2f} ({100 * mb[k] / max(1e-9, alloc):.1f}) |")
