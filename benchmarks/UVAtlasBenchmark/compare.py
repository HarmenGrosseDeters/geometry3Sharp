#!/usr/bin/env python3
# UV atlas comparison: UVAtlasGenerator (Method = Auto, defaults) and xatlas (default options, black box through
# xatlas_driver) on the same meshes. Both outputs are evaluated by the same code (UVAtlasBenchmark evaluate), so the
# metrics are comparable. Writes results/ours.jsonl and results/xatlas.jsonl, one JSON line per mesh. analyze.py reads them.
#
#   python3 compare.py --dll UVAtlasBenchmark.dll --xatlas xatlas_driver --selection data/selection.csv \
#       --meshes data/meshes --work <scratch dir> --out results [--jobs 3] [--timeout 300]
#
# Every run is a separate process with a timeout; a timeout or crash is a result (status), not a missing row.
import argparse, csv, json, os, subprocess
from concurrent.futures import ThreadPoolExecutor


def last_json(text):
    for line in reversed(text.splitlines()):
        line = line.strip()
        if line.startswith("{"):
            try:
                return json.loads(line)
            except ValueError:
                pass
    return None


def run(cmd, timeout):
    # (returncode, stdout, stderr); returncode None on timeout
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return p.returncode, p.stdout, p.stderr
    except subprocess.TimeoutExpired:
        return None, "", "timeout"


def evaluate(args, mesh, uv, tool, producer, cat, file):
    ms = producer.get("ms")
    cmd = ["dotnet", args.dll, "evaluate", mesh, uv, "--tool", tool,
           "--ms", "NaN" if ms is None else repr(float(ms)), "--status", producer["status"]]
    rc, out, err = run(cmd, args.timeout)
    row = last_json(out) if rc == 0 else None
    if row is None:
        row = {"file": file, "tool": tool, "status": "evaluate_failed", "producer_status": producer["status"],
               "error": (err or "")[-300:]}
    row["category"] = cat
    row["ms"] = ms
    return row


def process(args, item):
    mesh_path, cat = item
    file = os.path.basename(mesh_path)
    stem = os.path.splitext(file)[0]
    inp = os.path.join(args.work, "in", stem + ".obj")
    rc, _, err = run(["dotnet", args.dll, "to-obj", mesh_path, inp], args.timeout)
    if rc != 0 or not os.path.exists(inp):
        bad = {"status": "load_failed", "ms": None}
        return [dict(evaluate_fail(file, t, bad, cat, "to-obj failed")) for t in ("UVAtlasGenerator", "xatlas")]

    # UVAtlasGenerator
    ours = os.path.join(args.work, "ours", stem + ".obj")
    rc, out, _ = run(["dotnet", args.dll, "atlas", mesh_path, ours], args.timeout)
    prod = last_json(out) if rc == 0 else None
    if prod is None:
        prod = {"status": "timeout" if rc is None else "crash", "ms": None}
    row_ours = evaluate(args, mesh_path, ours if os.path.exists(ours) else "-", "UVAtlasGenerator", prod, cat, file)

    # xatlas (driver: same input, default options)
    xa = os.path.join(args.work, "xatlas", stem + ".obj")
    rc, out, _ = run([args.xatlas, inp, xa], args.timeout)
    prod = last_json(out) if rc == 0 else None
    if prod is None:
        prod = {"status": "timeout" if rc is None else "crash", "ms": None}
    ok = prod.get("status") == "ok" and os.path.exists(xa)
    row_xa = evaluate(args, mesh_path, xa if ok else "-", "xatlas", prod, cat, file)
    return [row_ours, row_xa]


def evaluate_fail(file, tool, prod, cat, error):
    return {"file": file, "tool": tool, "status": "no_output", "producer_status": prod["status"], "ms": None,
            "category": cat, "error": error}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dll", required=True)
    ap.add_argument("--xatlas", required=True)
    ap.add_argument("--selection", required=True)
    ap.add_argument("--meshes", required=True)
    ap.add_argument("--work", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--jobs", type=int, default=3)
    ap.add_argument("--timeout", type=int, default=300)
    args = ap.parse_args()
    if not os.access(args.xatlas, os.X_OK):
        ap.error("xatlas driver not executable: " + args.xatlas)
    for d in ("in", "ours", "xatlas"):
        os.makedirs(os.path.join(args.work, d), exist_ok=True)
    os.makedirs(args.out, exist_ok=True)
    items = []
    with open(args.selection) as f:
        for r in csv.DictReader(f):
            items.append((os.path.join(args.meshes, os.path.basename(r["path"])), r["category"]))
    rows = {"UVAtlasGenerator": [], "xatlas": []}
    with ThreadPoolExecutor(max_workers=args.jobs) as ex:
        for pair in ex.map(lambda it: process(args, it), items):
            for row in pair:
                rows[row["tool"]].append(row)
    for tool, name in (("UVAtlasGenerator", "ours"), ("xatlas", "xatlas")):
        with open(os.path.join(args.out, name + ".jsonl"), "w") as f:
            for row in rows[tool]:
                f.write(json.dumps(row) + "\n")
        print(name, len(rows[tool]), "meshes")


if __name__ == "__main__":
    main()
