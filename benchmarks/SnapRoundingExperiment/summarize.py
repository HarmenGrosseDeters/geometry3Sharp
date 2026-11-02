#!/usr/bin/env python3
"""Summary of run.py results: per method, on all cases and on the cases SafeRounding leaves unclean ("left").

  python3 summarize.py <results.jsonl>...

Columns: cases, clean (rounding clean and MeshRobustRepair.NotCleanReasons empty), certified (clean and 2e-separated,
SeparationCertificate), failures (timeouts, errors), the
largest and the median drift in units of the bound (MaxLength), the largest drift in ulps of the largest coordinate,
the largest Hausdorff distance to the exact result (rounded) in units of the bound, removed components, and the median
and largest time.
"""
import json, statistics, sys
from collections import OrderedDict


def load(paths):
    rows = []
    for p in paths:
        with open(p) as f:
            rows += [json.loads(l) for l in f if l.strip()]
    return rows


def fmt(x, spec="%.3g"):
    return "-" if x is None else spec % x


def table(rows, cases, title):
    # (again, A, B and AB run only where SafeRounding's result is not clean, run.py --left, or not rounding clean,
    # --rounding-left: a case where a method did not run counts with SafeRounding's result)
    methods = OrderedDict()
    have = set()
    safe = {}
    for r in rows:
        if r["case"] in cases:
            methods.setdefault(r["method"], []).append(r)
            have.add((r["case"], r["method"]))
            if r["method"] == "safe":
                safe[r["case"]] = r
    for m in methods:
        if m not in ("plain", "safe"):
            methods[m] += [safe[c] for c in cases if (c, m) not in have and c in safe]
    print("\n%s (%d cases)\n" % (title, len(cases)))
    print("| method | clean | certified | failed | drift / bound, max | median | drift, ulps max | Hausdorff / bound, max | removed comp. | ms median | ms max |")
    print("|---|---|---|---|---|---|---|---|---|---|---|")
    for m, rs in methods.items():
        ok = [r for r in rs if r.get("ok")]
        clean = sum(1 for r in ok if r.get("clean"))
        # (clean, and every pair of disjoint features farther apart than 2e: SeparationCertificate)
        certified = sum(1 for r in ok if r.get("clean") and r.get("uncertified") == 0 and not r.get("cert_capped"))
        rel = [r["max_drift"] / r["max_length"] for r in ok if r.get("max_length")]
        ulps = [r["drift_ulps"] for r in ok if "drift_ulps" in r and r.get("max_length")]
        hd = [r["hausdorff"] / r["max_length"] for r in ok if "hausdorff" in r and r.get("max_length")]
        rem = sum(r.get("removed_components", 0) for r in ok)
        ms = [r["ms"] for r in ok]
        print("| %s | %d | %s | %d | %s | %s | %s | %s | %d | %s | %s |" % (
            m, clean, "-" if m == "plain" else str(certified), len(rs) - len(ok), fmt(max(rel) if rel else None), fmt(statistics.median(rel) if rel else None),
            fmt(max(ulps) if ulps else None), fmt(max(hd) if hd else None), rem,
            fmt(statistics.median(ms) if ms else None, "%.0f"), fmt(max(ms) if ms else None, "%.0f")))


def main():
    rows = load(sys.argv[1:])
    allc = set(r["case"] for r in rows)
    left = set(r["case"] for r in rows if r["method"] == "safe" and not r.get("clean"))
    rleft = set(r["case"] for r in rows if r["method"] == "safe" and r.get("ok") and not r.get("rounding_clean"))
    table(rows, allc, "All cases")
    if rleft != left and rleft:
        # (the others are not clean for reasons of their input, eg open meshes: run.py --rounding-left)
        table(rows, rleft, "Cases SafeRounding leaves not rounding clean")
    if left:
        table(rows, left, "Cases SafeRounding leaves unclean")
        for c in sorted(left):
            parts = []
            for r in rows:
                if r["case"] == c and r["method"] != "plain":
                    parts.append("%s %s" % (r["method"], "clean" if r.get("clean") else (r.get("reasons") or r.get("error", "?"))))
            print("  %s: %s" % (c, "; ".join(parts)))


if __name__ == "__main__":
    main()
