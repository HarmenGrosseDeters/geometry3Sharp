#!/usr/bin/env python3
"""Compares the time and the result of two builds of SnapRoundingExperiment on the same cases (`run <case> safe`, one process per run).

  python3 compare_builds.py <dir of build A> <dir of build B> [<out.json>]

A and B are copies of bin/Release/net10.0 of two builds (eg of the library with and without a change). The cases: every third case of
the sets booleans, utm and clusters, all of cylinder and drop, scale up to resolution 81 (214 cases); two passes, the order of the
builds alternating, the faster run per case counts. Prints the total times, the cases whose result (clean, triangles, edits, drift)
differs, and the largest ratio of a single case above 200 ms. Used for docs/research/ROUNDING_THIN_VOLUME_2026-11.md ("Caps first").
"""
import json, subprocess, sys, os
DIRS = {"base": sys.argv[1], "c32": sys.argv[2]}
OUT = sys.argv[3] if len(sys.argv) > 3 else "compare_builds.json"
def lst(spec, dll):
    return subprocess.run(["dotnet", dll, "list", spec], capture_output=True, text=True, check=True).stdout.split()
dll = {v: os.path.join(d, "SnapRoundingExperiment.dll") for v, d in DIRS.items()}
cases = []
for spec in ("booleans", "utm", "clusters"):
    cases += lst(spec, dll["base"])[::3]
cases += lst("cylinder", dll["base"]) + lst("drop", dll["base"])
cases += [c for c in lst("scale", dll["base"]) if int(c.split(":")[1]) <= 81]
print(len(cases), file=sys.stderr)
res = {}
for p in range(2):
    order = ("base", "c32") if p == 0 else ("c32", "base")
    for c in cases:
        for v in order:
            try:
                o = subprocess.run(["dotnet", dll[v], "run", c, "safe"], capture_output=True, text=True, timeout=300).stdout
                r = json.loads(o.splitlines()[0])
            except Exception as e:
                r = {"ms": float("nan"), "clean": None}
            d = res.setdefault(c, {}).setdefault(v, {"ms": [], "r": []})
            d["ms"].append(r.get("ms", float("nan"))); d["r"].append((r.get("clean"), r.get("tris"), r.get("edits"), r.get("max_drift")))
json.dump(res, open(OUT, "w"))
tb = sum(min(v["base"]["ms"]) for v in res.values()); tc = sum(min(v["c32"]["ms"]) for v in res.values())
print("cases", len(res), "base_s %.1f c32_s %.1f ratio %.3f" % (tb / 1000, tc / 1000, tc / tb))
diff = [c for c, v in res.items() if v["base"]["r"][0] != v["c32"]["r"][0]]
print("cases with a different result (clean, tris, edits, drift):", len(diff))
for c in diff: print(c, res[c]["base"]["r"][0], res[c]["c32"]["r"][0])
worse = [(c, min(v["c32"]["ms"]) / min(v["base"]["ms"])) for c, v in res.items() if min(v["base"]["ms"]) > 200]
print("max per-case ratio among cases above 200 ms:", max(worse, key=lambda x: x[1]) if worse else None)
