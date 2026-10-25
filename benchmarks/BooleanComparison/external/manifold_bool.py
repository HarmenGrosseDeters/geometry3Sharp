# Manifold (https://github.com/elalish/manifold) booleans, for the comparison in benchmarks/BooleanComparison.
# Usage: python3 manifold_bool.py <union|intersection|difference> A.off B.off out.off
#        python3 manifold_bool.py obb --request request.json --result result.json [--write-last-only]
# Prints one JSON line: ms of the boolean (including the conversion to Manifold, without file I/O).
# The obb mode runs the operation lists of an Open Boolean Benchmark request in memory (see ../ObbRunner.cs;
# only what compare.py needs: OFF input, OBJ output, binary booleans).
import json, os, sys, time
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

def obb(args):
    request, result = args[args.index("--request") + 1], args[args.index("--result") + 1]
    last_only = "--write-last-only" in args
    req = json.load(open(request))
    out = {"kind": req["kind"], "version": req["version"], "id": req["id"], "runs": []}
    kinds = {"boolean-union": "union", "boolean-intersection": "intersection", "boolean-difference": "difference"}
    for run in req["runs"]:
        t_run = time.perf_counter()
        ops, slots, status = [], {}, "success"
        r = {"case_id": run["case_id"], "status": status, "ops": ops}
        out["runs"].append(r)
        n = len(run["operations"])
        for i, o in enumerate(run["operations"]):
            res = {}
            ops.append(res)
            if o["op"] == "load-mesh":
                t0 = time.perf_counter()
                v, t = read_off(o["path"])
                t1 = time.perf_counter()
                m = mf.Manifold(mf.Mesh64(v, t))
                res.update(io_ms=(t1 - t0) * 1000, import_ms=(time.perf_counter() - t1) * 1000)
                if m.status() != mf.Error.NoError:
                    status, res["error"] = "invalid_input", f"input not manifold: {m.status()}"
                slots[i] = m
            elif o["op"] in kinds and len(o["args"]) == 2:
                a, b = slots[o["args"][0]], slots[o["args"][1]]
                t0 = time.perf_counter()
                k = kinds[o["op"]]
                m = a + b if k == "union" else (a ^ b if k == "intersection" else a - b)
                m.num_tri()   # Manifold evaluates lazily
                res["operation_ms"] = (time.perf_counter() - t0) * 1000
                slots[i] = m
            else:
                status, res["error"] = "unsupported", "only binary booleans"
            if status == "success" and run.get("out_dir") and (not last_only or i == n - 1):
                mesh = slots[i].to_mesh64()
                res["file"] = os.path.join(run["out_dir"], f"op_{i}.off")
                write_off(res["file"], np.asarray(mesh.vert_properties)[:, :3], np.asarray(mesh.tri_verts))
            res["status"] = status
            if status != "success":
                break
        r["status"], r["duration_ms"] = status, (time.perf_counter() - t_run) * 1000
    with open(result + ".tmp", "w") as f:
        json.dump(out, f)
    os.replace(result + ".tmp", result)

if sys.argv[1] == "obb":
    obb(sys.argv[2:])
    sys.exit(0)

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
