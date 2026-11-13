# group (d), #794: for each crash input, log2(max |coordinate| / largest box extent) of A (the mesh the op reduces).
# After the power-of-two normalization (diagonal in [1, 2)) this is about log2 of the largest normalized coordinate,
# so the 2^40 copy of the target overflows (beyond 2^1024) when it is >= 984.
import glob, math, sys
rows = []
for p in sorted(glob.glob(sys.argv[1])):
    L = open(p).read().splitlines(); A, cur = [], False
    for l in L[1:]:
        if l.startswith("A vertices"): cur = True; continue
        if not l.startswith("  "): cur = False; continue
        if cur: A.append([float(x) for x in l.split(":")[1].split()])
    ext = max(max(q[i] for q in A) - min(q[i] for q in A) for i in range(3))
    mx = max(abs(c) for q in A for c in q)
    rows.append((math.log2(mx) - math.log2(ext), p.split("dump-")[1][:22]))
for r, n in rows: print(f"{n} {r:7.1f}")
v = [r for r, _ in rows]
print(f"n={len(v)} min={min(v):.1f} max={max(v):.1f} overflow(>=984)={sum(1 for x in v if x >= 984)}", file=sys.stderr)
