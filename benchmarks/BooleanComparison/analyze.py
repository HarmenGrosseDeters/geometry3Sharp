# Summarises compare.py results: per method success, correctness of the results and time.
# Usage: python3 analyze.py results.jsonl
import collections, json, statistics as st, sys

rows = [json.loads(l) for l in open(sys.argv[1]) if l.strip()]
rows = [r for r in rows if r.get("method") != "export"]
OPS = ["union", "intersection", "difference"]
methods = list(dict.fromkeys(r["method"] for r in rows))
by_file = collections.defaultdict(dict)
for r in rows:
    by_file[r["file"]][r["method"]] = r

def ran(r):        # all three operations produced a result
    return all(r.get(f"{op}_ok") for op in OPS) and r.get("all_results")

def correct(r):    # closed in the winding sense and the volume identities hold
    return ran(r) and all(r.get(f"{op}_wclosed") for op in OPS) and r.get("ie_error", 1) < 1e-6 and r.get("diff_error", 1) < 1e-6

def clean(r):      # correct, manifold, no self-intersections
    return correct(r) and all(r.get(f"{op}_manifold_closed") and r.get(f"{op}_selfint", 1) == 0 for op in OPS)

def ms(r):
    return sum(r.get(f"{op}_ms", 0) for op in OPS)

def valid_input(rs):   # input usable by all exact methods: closed, oriented, no self-intersections
    i = next(iter(rs.values()))["input"]
    return i.get("wclosed") and i.get("selfint") == 0 and i.get("closed")

def pct(xs, q):
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(q * len(xs)))] if xs else float("nan")

print(f"{len(by_file)} meshes, {sum(valid_input(rs) for rs in by_file.values())} of them closed, oriented, without self-intersections\n")
for title, files in [("all inputs", list(by_file)), ("valid inputs", [f for f, rs in by_file.items() if valid_input(rs)])]:
    print(f"== {title} ({len(files)})")
    print(f"{'method':12} {'ran':>5} {'correct':>8} {'clean':>6}   errors")
    for m in methods:
        rs = [by_file[f][m] for f in files if m in by_file[f]]
        errs = collections.Counter()
        for r in rs:
            if not ran(r):
                e = next((r.get(f"{op}_error") for op in OPS if not r.get(f"{op}_ok")), None) or "no result"
                errs[str(e)[:40]] += 1
        print(f"{m:12} {sum(map(ran, rs)):5} {sum(map(correct, rs)):8} {sum(map(clean, rs)):6}   {dict(errs.most_common(3))}")
    print()

# time on the meshes where every method gave a correct result
common = [f for f, rs in by_file.items() if all(m in rs and correct(rs[m]) for m in methods)]
print(f"== time (union + intersection + difference, boolean only) on the {len(common)} meshes all methods solve correctly")
base = {f: ms(by_file[f]["ours"]) for f in common} if "ours" in methods else {}
for m in methods:
    t = [ms(by_file[f][m]) for f in common]
    if not t:
        continue
    rel = [ms(by_file[f][m]) / max(base[f], 1) for f in common] if base else []
    print(f"{m:12} median {st.median(t):8.0f} ms   p90 {pct(t, .9):8.0f} ms   total {sum(t) / 1000:7.1f} s"
          + (f"   relative to ours: median {st.median(rel):.2f}" if rel else ""))

# self-intersections after rounding
print("\n== results with self-intersections (any operation), of those with a result")
for m in methods:
    rs = [r for r in (by_file[f].get(m) for f in by_file) if r and ran(r)]
    n = sum(any(r.get(f"{op}_selfint", 0) > 0 for op in OPS) for r in rs)
    print(f"{m:12} {n} / {len(rs)}")
