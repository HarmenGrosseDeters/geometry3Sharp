# Runs mesh boolean implementations on the same inputs and evaluates all results the same way.
# Usage: python3 compare.py <mesh list file or directory> <out.jsonl> [--methods ours,cgal_epick,...] [--timeout 300] [-j 1]
# Inputs: each mesh A with a rotated, shifted copy B (BooleanComparison export). Results are evaluated by
# BooleanComparison eval (closedness, manifoldness, exact self-intersections, volume identities) and deleted.
# Tool locations: environment variables CGAL_BOOL, CHERCHI_BOOL, MANIFOLD_PYTHON (see README.md).
import argparse, json, os, shutil, subprocess, sys, tempfile, time
from concurrent.futures import ThreadPoolExecutor

here = os.path.dirname(os.path.abspath(__file__))
# BOOLCMP_TOOL: a copy of the built tool (so that rebuilding the library during a run does not mix versions)
tool = ["dotnet", os.environ.get("BOOLCMP_TOOL", os.path.join(here, "bin/Release/net10.0/BooleanComparison.dll"))]
OPS = ["union", "intersection", "difference"]

def commands(method, op, a, b, out):
    if method == "ours":
        return tool + ["run", a, b, op, out + ".off"]
    if method in ("cgal_epick", "cgal_epeck"):
        return [os.environ["CGAL_BOOL"], method.split("_")[1], op, a, b, out + ".off"]
    if method == "cherchi":
        return [os.environ["CHERCHI_BOOL"], {"difference": "subtraction"}.get(op, op), a, b, out + ".obj"]
    if method == "manifold":
        return [os.environ.get("MANIFOLD_PYTHON", "python3"), os.path.join(here, "external/manifold_bool.py"), op, a, b, out + ".off"]
    raise ValueError(method)

def run_one(method, op, d, timeout):
    out = os.path.join(d, f"{method}_{op}")
    cmd = commands(method, op, os.path.join(d, "A.off"), os.path.join(d, "B.off"), out)
    t0 = time.perf_counter()
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        return {"ok": False, "error": "timeout"}
    wall = round((time.perf_counter() - t0) * 1000)
    res = None
    # the tools print one JSON line with ms (cherchi on stderr)
    for line in (p.stdout + "\n" + p.stderr).splitlines():
        line = line.strip()
        if line.startswith("{") and '"ms"' in line or '"ok"' in line:
            try:
                res = json.loads(line)
            except ValueError:
                pass
    if res is None:
        res = {"ok": p.returncode == 0, "error": None if p.returncode == 0 else f"exit {p.returncode}: {p.stderr.strip()[-200:]}"}
    if method == "cherchi":
        res["ok"] = p.returncode == 0 and os.path.exists(out + ".obj")
        if not res["ok"] and not res.get("error"):
            res["error"] = f"exit {p.returncode}: {p.stderr.strip()[-200:]}"
    res["wall_ms"] = wall
    return res

def process(mesh, methods, timeout):
    d = tempfile.mkdtemp(prefix="boolcmp_")
    try:
        e = subprocess.run(tool + ["export", mesh, d], capture_output=True, text=True, timeout=1800)
        info = json.loads(e.stdout.strip().splitlines()[-1])
        rows = []
        runs = {}
        for m in methods:
            runs[m] = {op: run_one(m, op, d, timeout) for op in OPS}
        ev = subprocess.run(tool + ["eval", d], capture_output=True, text=True, timeout=3600)
        evals = {}
        for line in ev.stdout.splitlines():
            if line.startswith("{"):
                r = json.loads(line)
                evals[r["method"]] = r
        for m in methods:
            r = {"file": info.get("file"), "method": m, "input": info}
            for op in OPS:
                for k, v in runs[m][op].items():
                    r[f"{op}_{k}"] = v
            r.update({k: v for k, v in evals.get(m, {}).items() if k not in ("file", "method")})
            rows.append(r)
        return rows
    except Exception as ex:
        return [{"file": os.path.basename(mesh), "method": "export", "error": repr(ex)}]
    finally:
        shutil.rmtree(d, ignore_errors=True)

ap = argparse.ArgumentParser()
ap.add_argument("inputs")
ap.add_argument("out")
ap.add_argument("--methods", default="ours,cgal_epick,cgal_epeck,cherchi,manifold")
ap.add_argument("--timeout", type=int, default=300)
ap.add_argument("-j", type=int, default=1)
args = ap.parse_args()
if os.path.isdir(args.inputs):
    meshes = sorted(os.path.join(args.inputs, f) for f in os.listdir(args.inputs))
else:
    meshes = [l.strip() for l in open(args.inputs) if l.strip()]
methods = args.methods.split(",")
with open(args.out, "a") as f, ThreadPoolExecutor(args.j) as ex:
    for rows in ex.map(lambda m: process(m, methods, args.timeout), meshes):
        for r in rows:
            f.write(json.dumps(r) + "\n")
        f.flush()
        print(rows[0].get("file"), flush=True)
