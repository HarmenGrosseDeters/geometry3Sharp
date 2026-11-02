#!/usr/bin/env python3
"""Summary of run.py results: per method, on all cases and on the cases SafeRounding leaves unclean ("left").

  python3 summarize.py <results.jsonl>...

Then the numbers of the owner's criterion 2 (SAFE_ROUNDING.md section 8.7) per fallback method.

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
        if m not in ("plain", "safe", "patch"):
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


def criteria(rows, timeout_ms=120000.0):
    """the owner's criterion 2 (SAFE_ROUNDING.md section 8.7) per fallback method, against SafeRounding: new clean
    results, drift beyond the bound, Hausdorff within the bound, and the time ratio to SafeRounding per case (median and
    p95, over all cases and over the affected ones, where the method ran; a failed run counts as the time limit)"""
    by = {}
    for r in rows:
        by.setdefault(r["case"], {})[r["method"]] = r
    cases = [c for c, d in by.items() if d.get("safe", {}).get("ok")]
    def p95(x):
        x = sorted(x)
        return x[int(0.95 * (len(x) - 1))] if x else None
    print("\nCriterion 2 (%d cases)\n" % len(cases))
    print("| method | ran on | clean | new clean | drift beyond the bound | new clean within the Hausdorff bound | time ratio all, median / p95 | affected, median / p95 |")
    print("|---|---|---|---|---|---|---|---|")
    for m in ("again", "B", "Bpaper"):
        ran = [c for c in cases if m in by[c]]
        if not ran:
            continue
        def ok(c): return by[c][m].get("ok")
        clean = sum(1 for c in cases if (by[c][m].get("clean") if c in ran and ok(c) else by[c]["safe"].get("clean")))
        new = [c for c in ran if ok(c) and by[c][m].get("clean") and not by[c]["safe"].get("clean")]
        beyond = [c for c in ran if ok(c) and by[c][m].get("max_drift", 0) > by[c][m].get("max_length", float("inf"))]
        hd = [c for c in new if by[c][m].get("hausdorff", float("inf")) <= by[c][m].get("max_length", 0)]
        ratio = {c: (by[c][m]["ms"] if ok(c) else timeout_ms) / max(by[c]["safe"]["ms"], 1e-3) for c in ran}
        all_r = [ratio.get(c, 1.0) for c in cases]
        aff = list(ratio.values())
        print("| %s | %d | %d | %d | %d | %d | %s / %s | %s / %s |" % (m, len(ran), clean, len(new), len(beyond), len(hd),
              fmt(statistics.median(all_r)), fmt(p95(all_r)), fmt(statistics.median(aff)), fmt(p95(aff))))


def main():
    rows = load(sys.argv[1:])
    allc = set(r["case"] for r in rows)
    left = set(r["case"] for r in rows if r["method"] == "safe" and not r.get("clean"))
    rleft = set(r["case"] for r in rows if r["method"] == "safe" and r.get("ok") and not r.get("rounding_clean"))
    table(rows, allc, "All cases")
    criteria(rows)
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
