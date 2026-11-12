# The closeness of the exact stages (upper bound for an intermediate-precision filter before them).
# gap = bits of the expression's scale - bits of its exact value; a filter with a relative error of 2^-p decides a
# stage with gap < p - margin. Usage: python3 analyze_hist.py hist.jsonl
import json, sys, collections
rows = [json.loads(l) for l in open(sys.argv[1]) if '"hist"' in l]
def group(r):
    c = r["case"]
    return "#711 timeout unit" if "timeout" in c else ("tail (self-intersecting models, intersection)" if any(k in c for k in ("113685", "1059579", "107554", "42327", "113906")) else "normal (clean model with its rotated copy, union)")
tot = collections.defaultdict(lambda: collections.defaultdict(lambda: [0] * 80))
for r in rows:
    for cls, h in r["hist"].items():
        t = tot[group(r)][cls]
        for i, v in enumerate(h): t[i] += v
print("| stratum | exact stage | calls | exactly zero % | decided at 2^-53 with 8 bits margin (gap < 40) % | gap < 96 (double-double) % | gap < 192 % | gap >= 192, nonzero % |")
print("|---|---|---|---|---|---|---|---|")
for g in sorted(tot):
    for cls in sorted(tot[g]):
        h = tot[g][cls]; n = sum(h)
        if n == 0: continue
        def lt(bits): return sum(h[1:1 + bits // 8])
        print(f"| {g} | {cls} | {n} | {100 * h[0] / n:.1f} | {100 * lt(40) / n:.1f} | {100 * lt(96) / n:.1f} | {100 * lt(192) / n:.1f} | {100 * (n - h[0] - lt(192)) / n:.1f} |")
