# candidate B probe: medians of 5 alternating whole-operation runs, base against the limb prototype
# (python3 analyze_b.py <results dir>)
import json, sys, glob, statistics, collections
d = sys.argv[1]
def load(prefix, v):
    rows = collections.defaultdict(list)
    for f in sorted(glob.glob(f"{d}/{prefix}_{v}_*.jsonl")):
        for l in open(f):
            r = json.loads(l)
            rows[(r['case'], r['op'])].append(r)
    return rows
def med(rs, k): return statistics.median(r[k] for r in rs)
print("## tail models (4 cores, timers off): per operation, median of the runs; ms is wall time with collections")
print("| case | op | runs | base ms (range) | limbs ms (range) | change | GC pause ms base -> limbs | allocated GB base -> limbs | hash equal |")
print("|---|---|---|---|---|---|---|---|---|")
b, l = load("mt", "base"), load("mt", "limbs")
for k in sorted(b):
    if k not in l: continue
    bm, lm = med(b[k], 'ms'), med(l[k], 'ms')
    eq = all(r['hash'] == b[k][0]['hash'] and r['status'] == b[k][0]['status'] for r in b[k] + l[k])
    print(f"| {k[0]} | {k[1]} | {len(b[k])}/{len(l[k])} | {bm:.0f} ({min(r['ms'] for r in b[k])}-{max(r['ms'] for r in b[k])}) | {lm:.0f} ({min(r['ms'] for r in l[k])}-{max(r['ms'] for r in l[k])}) | {100*(lm/bm-1):+.1f} % | "
          f"{med(b[k],'gc_pause_ms'):.0f} -> {med(l[k],'gc_pause_ms'):.0f} | {med(b[k],'alloc_mb')/1024:.2f} -> {med(l[k],'alloc_mb')/1024:.2f} | {eq} |")
print()
print("## normal strata (4 cores, timers off): the sum of the per-operation medians")
print("| stratum | operations | base s (range of the run totals) | limbs s (range) | change | GC pause s | allocated GB | hashes equal |")
print("|---|---|---|---|---|---|---|---|")
for name, prefix in (("20 pairs", "npairs"), ("13 models with a rotated copy", "ncopy")):
    b, l = load(prefix, "base"), load(prefix, "limbs")
    keys = [k for k in b if k in l]
    if not keys: continue
    sb = sum(med(b[k], 'ms') for k in keys) / 1000; sl = sum(med(l[k], 'ms') for k in keys) / 1000
    def totals(rows):
        n = min(len(rows[k]) for k in keys)
        return [sum(rows[k][i]['ms'] for k in keys) / 1000 for i in range(n)]
    tb, tl = totals(b), totals(l)
    eq = sum(1 for k in keys if all(r['hash'] == b[k][0]['hash'] for r in b[k] + l[k]))
    print(f"| {name} | {len(keys)} | {sb:.2f} ({min(tb):.2f}-{max(tb):.2f}) | {sl:.2f} ({min(tl):.2f}-{max(tl):.2f}) | {100*(sl/sb-1):+.1f} % | "
          f"{sum(med(b[k],'gc_pause_ms') for k in keys)/1000:.2f} -> {sum(med(l[k],'gc_pause_ms') for k in keys)/1000:.2f} | "
          f"{sum(med(b[k],'alloc_mb') for k in keys)/1024:.2f} -> {sum(med(l[k],'alloc_mb') for k in keys)/1024:.2f} | {eq} / {len(keys)} |")
