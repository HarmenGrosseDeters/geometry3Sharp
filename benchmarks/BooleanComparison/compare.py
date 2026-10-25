# Runs mesh boolean implementations on the same inputs and evaluates all results the same way.
# Usage: python3 compare.py <inputs> <out.jsonl> [--methods ours,cgal_epick,...] [--timeout 300] [-j 1] [--class NAME]
# <inputs> is one of
#   - a directory or a list file of meshes: each mesh A with a rotated, shifted copy B (BooleanComparison
#     export), data class --class (default thingi10k);
#   - a .jsonl manifest of cases (written by the scripts in data/ and by BooleanComparison synth), one object per
#     line with "class", "name" and one of: "mesh" (A with a rotated, shifted copy), "a" + "b" (two meshes as
#     given), "case" (a directory with A.off, B.off, info.json) or "sequence" (a directory with an Open Boolean
#     Benchmark request.json and info.json: a list of booleans, of which the final result is evaluated).
# Results are evaluated by BooleanComparison eval (closedness, manifoldness, exact self-intersections, volume
# identities, analytically known volumes) and deleted.
# Each run gets a status: ok, rejected (the tool refuses the input), failed (the tool reports a failure on an
# accepted input), crash (abnormal termination), timeout.
# Tool locations: environment variables CGAL_BOOL, CHERCHI_BOOL, MANIFOLD_PYTHON (see README.md).
import argparse, json, os, shutil, subprocess, sys, tempfile, time
from concurrent.futures import ThreadPoolExecutor

here = os.path.dirname(os.path.abspath(__file__))
# BOOLCMP_TOOL: a copy of the built tool (so that rebuilding the library during a run does not mix versions)
tool = ["dotnet", os.environ.get("BOOLCMP_TOOL", os.path.join(here, "bin/Release/net10.0/BooleanComparison.dll"))]
manifold = [os.environ.get("MANIFOLD_PYTHON", "python3"), os.path.join(here, "external/manifold_bool.py")]
OPS = ["union", "intersection", "difference"]
# error messages of the tools that mean: the input is refused (not a failure on an accepted input)
REJECT = ["input not closed", "input self-intersects", "does not bound a volume", "read failed", "input not manifold",
          "input meshes must be closed", "non-finite coordinates", "invalid_input"]

def commands(method, op, a, b, out):
    if method == "ours":
        return tool + ["run", a, b, op, out + ".off"]
    if method in ("cgal_epick", "cgal_epeck"):
        return [os.environ["CGAL_BOOL"], method.split("_")[1], op, a, b, out + ".off"]
    if method == "cherchi":
        return [os.environ["CHERCHI_BOOL"], {"difference": "subtraction"}.get(op, op), a, b, out + ".obj"]
    if method == "manifold":
        return manifold + [op, a, b, out + ".off"]
    raise ValueError(method)

def status_of(res, returncode):
    if res.get("ok"):
        return "ok"
    err = str(res.get("error") or "")
    if any(s in err for s in REJECT):
        return "rejected"
    # a tool that ends normally and reports why is a failure; a signal, an exception or exit code without a
    # report of the tool is a crash
    return "failed" if returncode == 0 and err else "crash"

def execute(cmd, timeout):
    t0 = time.perf_counter()
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        return None, None
    return p, round((time.perf_counter() - t0) * 1000)

def parse_json_line(text):
    res = None
    for line in text.splitlines():
        line = line.strip()
        if line.startswith("{") and ('"ms"' in line or '"ok"' in line):
            try:
                res = json.loads(line)
            except ValueError:
                pass
    return res

def run_one(method, op, d, timeout):
    out = os.path.join(d, f"{method}_{op}")
    p, wall = execute(commands(method, op, os.path.join(d, "A.off"), os.path.join(d, "B.off"), out), timeout)
    if p is None:
        return {"ok": False, "error": "timeout", "status": "timeout"}
    # the tools print one JSON line with ms (cherchi on stderr)
    res = parse_json_line(p.stdout + "\n" + p.stderr)
    if res is None:
        res = {"ok": p.returncode == 0, "error": None if p.returncode == 0 else f"exit {p.returncode}: {p.stderr.strip()[-200:]}"}
    if method == "cherchi":
        res["ok"] = p.returncode == 0 and os.path.exists(out + ".obj")
        if not res["ok"] and not res.get("error"):
            res["error"] = f"exit {p.returncode}: {p.stderr.strip()[-200:]}"
    res["wall_ms"] = wall
    res["status"] = status_of(res, p.returncode)
    return res

# --- sequences: an Open Boolean Benchmark request, of which the result of the last operation is evaluated

KINDS = {"boolean-union": "union", "boolean-intersection": "intersection", "boolean-difference": "difference"}

def run_sequence(method, request, d, timeout):
    """Runs all operations; writes <method>_final.(off|obj) to d. ours and manifold run the request in memory
    (Open Boolean Benchmark runners), CGAL its own list format in memory, Cherchi et al. one process per boolean."""
    ops = request["runs"][0]["operations"]
    final = os.path.join(d, f"{method}_final")
    t0 = time.perf_counter()
    if method in ("ours", "manifold"):
        req = json.loads(json.dumps(request))
        outdir = os.path.join(d, method + "_out")
        os.makedirs(outdir)
        req["runs"][0]["out_dir"] = outdir
        rq, rs = os.path.join(d, method + "_request.json"), os.path.join(d, method + "_result.json")
        json.dump(req, open(rq, "w"))
        cmd = (tool + ["obb"] if method == "ours" else manifold + ["obb"]) + ["--request", rq, "--result", rs, "--write-last-only"]
        p, wall = execute(cmd, timeout)
        res = {"done": 0}
        if p is None:
            res.update(ok=False, error="timeout", status="timeout")
        elif not os.path.exists(rs):
            res.update(ok=False, error=f"exit {p.returncode}: {p.stderr.strip()[-200:]}", status="crash")
        else:
            run = json.load(open(rs))["runs"][0]
            res["done"] = sum(1 for o in run["ops"] if "operation_ms" in o and o["status"] == "success")
            res["ms"] = round(sum(o.get("operation_ms", 0) for o in run["ops"]))
            res["ok"] = run["status"] == "success"
            res["error"] = next((o.get("error") or o["status"] for o in run["ops"] if o["status"] != "success"), None)
            # protocol status: invalid_input = the input is refused, crash = exception or abnormal end
            res["status"] = {"success": "ok", "invalid_input": "rejected", "crash": "crash"}.get(run["status"], "failed")
            if res["ok"]:
                last = run["ops"][-1]["file"]
                shutil.move(last, final + os.path.splitext(last)[1])
    elif method in ("cgal_epick", "cgal_epeck"):
        lines = [f"load {o['path']}" if o["op"] == "load-mesh" else f"{KINDS[o['op']]} {o['args'][0]} {o['args'][1]}" for o in ops]
        fops = os.path.join(d, method + "_ops.txt")
        open(fops, "w").write("\n".join(lines) + "\n")
        p, wall = execute([os.environ["CGAL_BOOL"], method.split("_")[1], "seq", fops, final + ".off"], timeout)
        if p is None:
            res = {"ok": False, "error": "timeout", "status": "timeout"}
        else:
            res = parse_json_line(p.stdout) or {"ok": False, "error": f"exit {p.returncode}: {p.stderr.strip()[-200:]}"}
            res["status"] = status_of(res, p.returncode)
    elif method == "cherchi":
        files, ms, done, res = {}, 0, 0, None
        deadline = time.perf_counter() + timeout
        for i, o in enumerate(ops):
            if o["op"] == "load-mesh":
                files[i] = o["path"]
                continue
            out = os.path.join(d, f"cherchi_op{i}.obj")
            remaining = deadline - time.perf_counter()
            p, _ = execute([os.environ["CHERCHI_BOOL"], {"difference": "subtraction"}.get(KINDS[o["op"]], KINDS[o["op"]]),
                            files[o["args"][0]], files[o["args"][1]], out], max(remaining, 1)) if remaining > 0 else (None, None)
            if p is None:
                res = {"ok": False, "error": "timeout", "status": "timeout"}
                break
            if p.returncode != 0 or not os.path.exists(out):
                res = {"ok": False, "error": f"exit {p.returncode}: {p.stderr.strip()[-200:]}", "status": "crash"}
                break
            ms += (parse_json_line(p.stderr) or {}).get("ms", 0)
            done += 1
            for x in o["args"]:   # intermediate results are not needed any more
                if files[x].startswith(d) and x != i:
                    os.remove(files[x])
            files[i] = out
        if res is None:
            shutil.move(files[len(ops) - 1], final + ".obj")
            res = {"ok": True, "status": "ok"}
        res["ms"], res["done"] = ms, done
    else:
        raise ValueError(method)
    res["wall_ms"] = round((time.perf_counter() - t0) * 1000)
    return res

def evaluate(d, methods, case, info, runs, ops):
    ev = subprocess.run(tool + ["eval", d], capture_output=True, text=True, timeout=3600)
    evals = {}
    for line in ev.stdout.splitlines():
        if line.startswith("{"):
            r = json.loads(line)
            evals[r["method"]] = r
    rows = []
    for m in methods:
        r = {"class": case["class"], "file": case["name"], "method": m, "ops": ops, "input": info}
        for op in ops:
            for k, v in runs[m][op].items():
                r[f"{op}_{k}"] = v
        r.update({k: v for k, v in evals.get(m, {}).items() if k not in ("file", "method")})
        rows.append(r)
    return rows

def process(case, methods, timeout):
    d = tempfile.mkdtemp(prefix="boolcmp_")
    try:
        if "sequence" in case:
            info = json.load(open(os.path.join(case["sequence"], "info.json")))
            shutil.copy(os.path.join(case["sequence"], "info.json"), d)
            request = json.load(open(os.path.join(case["sequence"], "request.json")))
            for o in request["runs"][0]["operations"]:
                if o["op"] == "load-mesh":
                    o["path"] = os.path.join(case["sequence"], o["path"])
            runs = {m: {"final": run_sequence(m, request, d, timeout)} for m in methods}
            return evaluate(d, methods, case, info, runs, ["final"])
        if "case" in case:
            for f in ("A.off", "B.off", "info.json"):
                shutil.copy(os.path.join(case["case"], f), d)
            info = json.load(open(os.path.join(d, "info.json")))
        else:
            cmd = tool + (["export", case["mesh"], d] if "mesh" in case else ["pair", case["a"], case["b"], d])
            e = subprocess.run(cmd, capture_output=True, text=True, timeout=1800)
            info = json.loads(e.stdout.strip().splitlines()[-1])
            if "error" in info:
                raise RuntimeError(info["error"])
        runs = {m: {op: run_one(m, op, d, timeout) for op in OPS} for m in methods}
        return evaluate(d, methods, case, info, runs, OPS)
    except Exception as ex:
        return [{"class": case["class"], "file": case["name"], "method": "export", "error": repr(ex)}]
    finally:
        shutil.rmtree(d, ignore_errors=True)

def read_cases(inputs, cls):
    if inputs.endswith(".jsonl"):
        return [json.loads(l) for l in open(inputs) if l.strip()]
    if os.path.isdir(inputs):
        meshes = sorted(os.path.join(inputs, f) for f in os.listdir(inputs))
    else:
        meshes = [l.strip() for l in open(inputs) if l.strip()]
    return [{"class": cls, "name": os.path.basename(m), "mesh": m} for m in meshes]

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("inputs")
    ap.add_argument("out")
    ap.add_argument("--methods", default="ours,cgal_epick,cgal_epeck,cherchi,manifold")
    ap.add_argument("--timeout", type=int, default=300, help="seconds per boolean, or per sequence")
    ap.add_argument("-j", type=int, default=1)
    ap.add_argument("--class", dest="cls", default="thingi10k", help="data class of a list or directory of meshes")
    ap.add_argument("--only", help="comma-separated case names to run")
    args = ap.parse_args()
    cases = read_cases(args.inputs, args.cls)
    if args.only:
        cases = [c for c in cases if c["name"] in args.only.split(",")]
    methods = args.methods.split(",")
    with open(args.out, "a") as f, ThreadPoolExecutor(args.j) as ex:
        for rows in ex.map(lambda c: process(c, methods, args.timeout), cases):
            for r in rows:
                f.write(json.dumps(r) + "\n")
            f.flush()
            print(rows[0].get("class"), rows[0].get("file"), flush=True)
