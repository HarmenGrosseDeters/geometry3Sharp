#!/usr/bin/env python3
"""Summary of the alpha-defaults-800 run (see README.md in this directory).

A = library defaults, B = BENCH_WRAP_FEATURES=1, C = BENCH_WRAP_BUDGET=300000; the same 800 meshes
(sample800_2611), wrappipe, diagonal/300, offset/3000, full fidelity, TIMEOUT=400, one process at a time.

Reads <dir>/raw/A.jsonl, B.jsonl, C.jsonl and <dir>/wall.tsv (the merge of the shards, see alpha_defaults.py) and the mesh
list; prints the summary as markdown to stdout. Only meshes with a row in all three configurations enter the comparison.
Usage: python3 -I alpha_defaults_summary.py <dir> [<list>] > summary.md
"""
import json
import math
import os
import statistics as st
import sys

D = sys.argv[1]
LIST = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "cases", "alpha_defaults_list800.txt")
CFG = ["A", "B", "C"]
LABEL = {"A": "A (defaults)", "B": "B (features)", "C": "C (budget 300k)"}
LIMIT_S = 20.0
TIMEOUT_S = 400.0

ids = [x.strip() for x in open(LIST) if x.strip()]
rows = {c: {} for c in CFG}
for c in CFG:
    p = os.path.join(D, "raw", c + ".jsonl")
    if os.path.exists(p):
        for line in open(p):
            if line.strip():
                r = json.loads(line)
                rows[c][r["file"]] = r
wall = {}
p = os.path.join(D, "wall.tsv")
if os.path.exists(p):
    for line in open(p):
        f, c, s = line.rstrip("\n").split("\t")
        wall[(f, c)] = float(s)

files = [i + ".stl" for i in ids]
done = [f for f in files if all(f in rows[c] for c in CFG)]
res = {c: {f: rows[c][f] for f in done} for c in CFG}


def is_result(r):
    return "after_tris" in r


def num(r, k):
    v = r.get(k)
    if isinstance(v, bool) or v is None:
        return None
    if isinstance(v, (int, float)) and math.isfinite(v):
        return float(v)
    return None


def hd(r):
    a, b = num(r, "hd_fwd_hi_rel"), num(r, "hd_back_hi_rel")
    return None if a is None or b is None else max(a, b)


def guarantees(r):
    closed = r.get("after_closed") is True
    return {
        "closed": closed,
        "manifold": closed and r.get("after_bowties") == 0 and r.get("after_boundary_edges") == 0,
        "no_self_int": r.get("after_selfint_exact") == 0,
        "valid": r.get("after_valid") is True,
    }


METRICS = {  # name: (extractor, better direction: +1 higher is better, -1 lower is better, 0 none)
    "triangles": (lambda r: num(r, "after_tris"), 0),
    "hausdorff_hi": (hd, -1),
    "p01": (lambda r: num(r, "after_angle_p01"), +1),
    "min_angle": (lambda r: num(r, "after_angle_min"), +1),
}


def fmt(x, nd=3):
    return "-" if x is None else f"{x:.{nd}f}"


out = []
w = out.append
w("# alpha-defaults-800: summary")
w("")
w(f"Meshes with a row in all three configurations: **{len(done)} of {len(files)}**. "
  "Times are the benchmark's own (ms) unless marked wall (wall clock per process, from `wall.tsv`).")
w("")

# -- failures and guarantees
w("## Guarantees, timeouts and crashes")
w("")
w("| config | rows | results | timeouts | crashes | closed | manifold | no self-intersection (exact) | valid | wrap not Success |")
w("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
for c in CFG:
    rr = res[c].values()
    results = [r for r in rr if is_result(r)]
    to = cr = 0
    for f in done:
        r = res[c][f]
        if not is_result(r):
            if wall.get((f, c), 0.0) >= TIMEOUT_S:
                to += 1
            else:
                cr += 1
    g = [guarantees(r) for r in results]
    cnt = lambda k: sum(1 for x in g if x[k])
    nonsucc = sum(1 for r in results if r.get("wrap_status") != "Success")
    w(f"| {LABEL[c]} | {len(done)} | {len(results)} | {to} | {cr} | {cnt('closed')} | {cnt('manifold')} | "
      f"{cnt('no_self_int')} | {cnt('valid')} | {nonsucc} |")
w("")
w("A timeout is a row with `timeout_or_crash` and wall time at or above 400 s, a crash any other such row.")
w("")

# -- against A
for c in ["B", "C"]:
    w(f"## {LABEL[c]} against A")
    w("")
    pairs = [f for f in done if is_result(res["A"][f]) and is_result(res[c][f])]
    w(f"Meshes with a result in both: {len(pairs)}.")
    w("")
    # guarantee losses
    lost = {k: 0 for k in ["closed", "manifold", "no_self_int", "valid"]}
    for f in pairs:
        ga, gc = guarantees(res["A"][f]), guarantees(res[c][f])
        for k in lost:
            if ga[k] and not gc[k]:
                lost[k] += 1
    w("Guarantees lost against A (A had it, " + LABEL[c] + " does not): "
      + ", ".join(f"{k} {v}" for k, v in lost.items()) + ".")
    w("")
    w("| metric | n | median ratio | worst ratio | range (min-max) | worse | better | identical |")
    w("|---|---:|---:|---:|---|---:|---:|---:|")
    for name, (ex, direction) in METRICS.items():
        ratios, worse, better, identical = [], 0, 0, 0
        for f in pairs:
            a, b = ex(res["A"][f]), ex(res[c][f])
            if a is None or b is None:
                continue
            if a == b:
                identical += 1
                ratios.append(1.0)
                continue
            if a != 0:
                ratios.append(b / a)
            if direction == 0:
                continue
            if (b - a) * direction < 0:
                worse += 1
            else:
                better += 1
        if ratios:
            med = st.median(ratios)
            if direction == -1:
                worst = max(ratios)
            elif direction == +1:
                worst = min(ratios)
            else:
                worst = None
            rng = f"{min(ratios):.4f}-{max(ratios):.4f}"
        else:
            med = worst = None
            rng = "-"
        wlabel = "" if direction == 0 else ""
        w(f"| {name} | {len(ratios)} | {fmt(med, 4)} | {fmt(worst, 4)} | {rng} | "
          f"{worse if direction else '-'} | {better if direction else '-'} | {identical} |")
    w("")
    w("Worse / better: Hausdorff bound (upper, relative to the diagonal) lower is better; p01 and min angle higher is better;"
      " triangles have no direction (their row counts only identical). Ratios are candidate / A (worst = largest Hausdorff"
      " ratio, smallest angle ratio).")
    w("")
    # coarsening / features
    if c == "C":
        co = [num(res[c][f], "wrap_coarsening") for f in pairs]
        co = [x for x in co if x is not None]
        coarse = [x for x in co if x > 1.0]
        w(f"Budget active (coarsening > 1): {len(coarse)} meshes"
          + (f", median factor {st.median(coarse):.3f}, max {max(coarse):.3f}" if coarse else "") + ".")
        w("")
    if c == "B":
        feat = [res[c][f].get("features") for f in pairs]
        nz = [x for x in feat if x and x != "0/0/0/0/0"]
        w(f"Sharp-feature pass changed something (features not 0/0/0/0/0): {len(nz)} meshes.")
        w("")

# -- times
w("## Time")
w("")
w("| config | pipe_ms sum | pipe_ms median | repair_ms sum | repair_ms median | wrap_ms sum | wall sum (s) | wall median (s) |")
w("|---|---:|---:|---:|---:|---:|---:|---:|")
for c in CFG:
    results = [res[c][f] for f in done if is_result(res[c][f])]
    def s(k):
        v = [num(r, k) for r in results]
        v = [x for x in v if x is not None]
        return (sum(v), st.median(v)) if v else (None, None)
    ps, pm = s("pipe_ms")
    rs, rm = s("repair_ms")
    ws, _ = s("wrap_ms")
    walls = [wall[(f, c)] for f in done if (f, c) in wall]
    w(f"| {LABEL[c]} | {fmt(ps, 0)} | {fmt(pm, 0)} | {fmt(rs, 0)} | {fmt(rm, 0)} | {fmt(ws, 0)} | "
      f"{fmt(sum(walls), 0)} | {fmt(st.median(walls) if walls else None, 2)} |")
w("")
if len(done) and all(is_result(res[c][f]) for c in CFG for f in done):
    pass
base_pipe = sum(num(res["A"][f], "pipe_ms") or 0 for f in done if is_result(res["A"][f]))
for c in ["B", "C"]:
    tot = sum(num(res[c][f], "pipe_ms") or 0 for f in done if is_result(res[c][f]))
    if base_pipe:
        w(f"{LABEL[c]} pipe_ms sum / A: {tot / base_pipe:.3f}.")
        w("")

w(f"## Meshes above {LIMIT_S:.0f} s")
w("")
w("Listed when the benchmark's own repair time (`repair_ms`) or pipeline time (`pipe_ms`) is above "
  f"{LIMIT_S:.0f} s, or the wall clock of the process is (wall includes start-up and the warm-up).")
w("")
any_over = False
for c in CFG:
    over = []
    for f in done:
        r = res[c][f]
        big = any((num(r, k) or 0) > LIMIT_S * 1000 for k in ("repair_ms", "pipe_ms")) if is_result(r) else False
        if big or wall.get((f, c), 0.0) > LIMIT_S:
            over.append(f)
    any_over = any_over or bool(over)
    w(f"- {LABEL[c]}: {len(over)}" + (": " + ", ".join(over) if over else ""))
w("")

print("\n".join(out))
