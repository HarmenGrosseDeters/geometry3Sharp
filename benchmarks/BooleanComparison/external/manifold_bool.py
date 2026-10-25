# Manifold (https://github.com/elalish/manifold) booleans, for the comparison in benchmarks/BooleanComparison.
# Usage: python3 manifold_bool.py <union|intersection|difference> A.off B.off out.off
# Prints one JSON line: ms of the boolean (including the conversion to Manifold, without file I/O).
import json, sys, time
import numpy as np
import manifold3d as mf

def read_off(path):
    toks = open(path).read().split()
    assert toks[0] == 'OFF'
    nv, nf = int(toks[1]), int(toks[2])
    k = 4
    v = np.array(toks[k:k + 3 * nv], dtype=np.float64).reshape(nv, 3); k += 3 * nv
    tris = []
    for _ in range(nf):
        n = int(toks[k]); f = [int(x) for x in toks[k + 1:k + 1 + n]]; k += 1 + n
        for j in range(1, n - 1):
            tris.append((f[0], f[j], f[j + 1]))
    return v, np.array(tris, dtype=np.uint64).reshape(-1, 3)

def write_off(path, v, t):
    with open(path, 'w') as f:
        f.write(f"OFF\n{len(v)} {len(t)} 0\n")
        f.writelines(f"{x!r} {y!r} {z!r}\n" for x, y, z in v.tolist())
        f.writelines(f"3 {a} {b} {c}\n" for a, b, c in t.tolist())

op, fa, fb, fout = sys.argv[1:5]
va, ta = read_off(fa)
vb, tb = read_off(fb)
t0 = time.perf_counter()
A = mf.Manifold(mf.Mesh64(va, ta))
B = mf.Manifold(mf.Mesh64(vb, tb))
if A.status() != mf.Error.NoError or B.status() != mf.Error.NoError:
    print(json.dumps({"ok": False, "error": f"input not manifold: {A.status()} {B.status()}"}))
    sys.exit(0)
R = A + B if op == 'union' else (A ^ B if op == 'intersection' else A - B)
m = R.to_mesh64()
ms = (time.perf_counter() - t0) * 1000
v = np.asarray(m.vert_properties)[:, :3]
t = np.asarray(m.tri_verts)
write_off(fout, v, t)
print(json.dumps({"ok": True, "ms": round(ms)}))
