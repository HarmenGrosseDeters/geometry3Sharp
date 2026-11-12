import re, sys, glob, math
# robust repair range (README "Range domains"): magnitude in [2^-200, 2^400], extent >= 2^-40 of it, no subnormal coordinate
for path in sorted(glob.glob(sys.argv[1])):
    lines = open(path).read().splitlines()
    op = lines[0].split(":")[0]
    soup = "soup" in op or "rounding" in op
    pts, cur = {"A": [], "B": []}, None
    for l in lines[1:]:
        if l.startswith("A vertices"): cur = "A"; continue
        if l.startswith("B vertices"): cur = "B"; continue
        if not l.startswith("  "): cur = None; continue
        if cur: pts[cur].append([float(x) for x in l.split(":")[1].split()])
    p = pts["A"] + (pts["B"] if soup else [])
    mag = max(abs(c) for q in p for c in q)
    meshes = [pts["A"]] + ([pts["B"]] if soup else [])
    ext = min(max(max(q[i] for q in m) - min(q[i] for q in m) for i in range(3)) for m in meshes)
    sub = any(c != 0 and abs(c) < 2.2250738585072014e-308 for q in p for c in q)
    inr = (2**-200 <= mag <= 2**400) and ext >= 2**-40 * mag and not sub
    print(f"{path.split('dump-')[1][:22]} {op:34s} n={len(p):2d} mag=2^{math.log2(mag):7.1f} minMeshExt/mag=2^{(math.log2(ext/mag) if ext > 0 else float("-inf")):6.1f} subnormal={sub} -> {'IN' if inr else 'out'}")
