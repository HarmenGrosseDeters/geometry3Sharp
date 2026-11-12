# A/B of the fuzz units (issue #711 class): today's *_bigint fallback against the prototypes, same build, switched at
# run time (EXACT_PROTO). Usage: python3 analyze_ab.py base.jsonl label=variant.jsonl ...
import json, sys, statistics as st
def load(p):
    d = {}
    for l in open(p):
        try: r = json.loads(l)
        except ValueError: continue
        if "ms" in r: d[(r["case"], r["op"])] = r
    return d
base = load(sys.argv[1])
def fb(r):   # time and calls of the double-input fallback (the timed region is the same in both variants)
    ms = calls = 0.0
    for k in ("o2.bigint", "o3.bigint", "ic.bigint"):
        if k in r["paths"]:
            c, t, _ = r["paths"][k]; calls += c; ms += max(0.0, t - c * r["pair_ns"] * 1e-6)
    return calls, ms
tb = sum(r["ms"] for r in base.values())
cb, mb = map(sum, zip(*(fb(r) for r in base.values())))
print(f"base: {len(base)} units, total {tb / 1000:.1f} s, median {st.median(r['ms'] for r in base.values()) / 1000:.2f} s, max {max(r['ms'] for r in base.values()) / 1000:.1f} s; "
      f"fallback calls {cb / 1e6:.1f}M, {mb / 1000:.1f} s = {100 * mb / tb:.1f} % of the time; allocated {sum(r['alloc_mb'] for r in base.values()) / 1024:.1f} GB; "
      f"units above 5 s: {sum(1 for r in base.values() if r['ms'] > 5000)}")
print("| variant | units | total s | median s | max s | units above 5 s | speedup (total) | fallback ms per unit (median) | fallback share % | allocated GB | hashes equal to base | predicate counts equal |")
print("|---|---|---|---|---|---|---|---|---|---|---|---|")
print(f"| today (BigInteger) | {len(base)} | {tb / 1000:.1f} | {st.median(r['ms'] for r in base.values()) / 1000:.2f} | {max(r['ms'] for r in base.values()) / 1000:.1f} | {sum(1 for r in base.values() if r['ms'] > 5000)} | 1.00 | {st.median(fb(r)[1] for r in base.values()):.0f} | {100 * mb / tb:.1f} | {sum(r['alloc_mb'] for r in base.values()) / 1024:.1f} | | |")
for a in sys.argv[2:]:
    label, path = a.split("=", 1)
    v = load(path)
    keys = [k for k in base if k in v]
    t0 = sum(base[k]["ms"] for k in keys); t1 = sum(v[k]["ms"] for k in keys)
    same = sum(1 for k in keys if base[k]["hash"] == v[k]["hash"] and base[k]["status"] == v[k]["status"] and base[k]["rounding_clean"] == v[k]["rounding_clean"])
    samep = sum(1 for k in keys if all(base[k]["predicates"].get(n) == v[k]["predicates"].get(n) for n in ("orient2d", "orient3d", "incircle")) and base[k]["counts"] == v[k]["counts"])
    c1, m1 = map(sum, zip(*(fb(v[k]) for k in keys)))
    print(f"| {label} | {len(keys)} | {t1 / 1000:.1f} | {st.median(v[k]['ms'] for k in keys) / 1000:.2f} | {max(v[k]['ms'] for k in keys) / 1000:.1f} | {sum(1 for k in keys if v[k]['ms'] > 5000)} | {t0 / t1:.2f} | {st.median(fb(v[k])[1] for k in keys):.0f} | {100 * m1 / t1:.1f} | {sum(v[k]['alloc_mb'] for k in keys) / 1024:.2f} | {same} / {len(keys)} | {samep} / {len(keys)} |")
