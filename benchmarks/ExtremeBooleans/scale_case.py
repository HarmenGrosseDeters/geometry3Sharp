# Writes a copy of a case directory (A.off, B.off) with every coordinate multiplied by 2^k (exact for normal doubles:
# only the exponent changes), and optionally every coordinate of magnitude below <zero below> set to 0.
#   python3 scale_case.py <case dir> <out dir> <k> [zero below]
import os, sys
src, out, k = sys.argv[1], sys.argv[2], int(sys.argv[3]); zb = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
os.makedirs(out, exist_ok=True)
for name in ("A.off", "B.off"):
    L = open(os.path.join(src, name)).read().splitlines(); nv = int(L[1].split()[0]); res = L[:2]
    for l in L[2:2 + nv]:
        v = [float(x) for x in l.split()]
        res.append(" ".join(repr(0.0 if abs(x) < zb else x * 2.0 ** k) for x in v))
    res += L[2 + nv:]
    open(os.path.join(out, name), "w").write("\n".join(res) + "\n")
