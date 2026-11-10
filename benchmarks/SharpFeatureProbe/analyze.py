#!/usr/bin/env python3
"""Tables for benchmarks/SharpFeatureProbe/README.md from the JSON lines of the probe.

usage: analyze.py <out.jsonl>... [--per-mesh]
Medians over the meshes that have a result for every variant shown; the paired ratios are against variant A."""
import json, statistics as st, sys

VARIANTS = ["A", "R", "B", "C1", "C2", "C3", "D1"]
NAMES = {"A": "A wrap (library defaults)", "R": "R raw wrap (before simplify)", "B": "B dual contouring on the winding number", "C1": "C1 snap to the offset miter (on A)",
         "C2": "C2 snap to the input's crease (on A)", "C3": "C3 = C1 on the raw wrap, then simplify", "D1": "D1 wrap with alpha / 2 (bound for option 4)"}


def load(files):
    rows = {}
    for f in files:
        for l in open(f):
            r = json.loads(l)
            if r.get("status") == "ok":
                rows[(r["file"], r["variant"])] = r
    return rows


def med(xs):
    xs = [x for x in xs if isinstance(x, (int, float)) and x == x and abs(x) != float("inf")]
    return st.median(xs) if xs else float("nan")


def fmt(x, p=3):
    return "n/a" if x != x else (f"{x:.{p}g}")


def table(rows, files, variants, title):
    print(f"\n### {title} ({len(files)} inputs)\n")
    cols = ["variant", "tris (A = 1)", "time s", "clean", "hits input", "vtx dist min / offset", "angle min", "p01 med", "<0.5° / >170°",
            "edge chamfer (·1e-3 size)", "…in offsets", "edge F 0.1 %", "edge F 0.5 %", "crease normal 2δ mean / p90 (°)", "crease normal α mean (°)"]
    print("| " + " | ".join(cols) + " |")
    print("|" + "---|" * len(cols))
    for v in variants:
        rs = [rows[(f, v)] for f in files if (f, v) in rows]
        if not rs:
            continue
        a = [rows[(f, "A")] for f in files if (f, v) in rows]
        tris = med([r["tris"] / x["tris"] for r, x in zip(rs, a)])
        clean = sum(1 for r in rs if r["clean"])
        hits = sum(1 for r in rs if r["hits_input"])
        ecd_off = [r["fm_ecd"] * r["fm_size"] / r["delta"] for r in rs if r.get("fm_ecd") is not None]
        print("| " + " | ".join([
            NAMES[v], fmt(tris, 3), fmt(med([r["ms"] / 1000 for r in rs]), 3), f"{clean}/{len(rs)}", f"{hits}/{len(rs)}",
            fmt(min(r["vdist_min_over_offset"] for r in rs), 2), fmt(min(r["angle_min"] for r in rs), 3), fmt(med([r["angle_p01"] for r in rs]), 3),
            f"{sum(r['angle_lt05'] for r in rs)} / {sum(r['angle_gt170'] for r in rs)}",
            fmt(med([r["fm_ecd"] * 1e3 for r in rs]), 3), fmt(med(ecd_off), 3), fmt(med([r["fm_ef_0001"] for r in rs]), 3), fmt(med([r["fm_ef_0005"] for r in rs]), 3),
            f"{fmt(med([r['crease_nd_d2_mean'] for r in rs]), 3)} / {fmt(med([r['crease_nd_d2_p90'] for r in rs]), 3)}",
            fmt(med([r["crease_nd_a1_mean"] for r in rs]), 3)]) + " |")
    print("\nPaired against A (median of per-input ratios; better / worse = more than 5 % off):\n")
    print("| variant | edge chamfer ratio | better / worse | crease normal 2δ ratio | edge F 0.5 % diff |")
    print("|---|---|---|---|---|")
    for v in variants:
        if v == "A":
            continue
        fs = [f for f in files if (f, v) in rows and (f, "A") in rows and rows[(f, "A")]["fm_ecd"] > 0]
        if not fs:
            continue
        ratio = [rows[(f, v)]["fm_ecd"] / rows[(f, "A")]["fm_ecd"] for f in fs]
        nd = [rows[(f, v)]["crease_nd_d2_mean"] / rows[(f, "A")]["crease_nd_d2_mean"] for f in fs if rows[(f, "A")]["crease_nd_d2_mean"] > 0]
        print(f"| {NAMES[v]} | {fmt(med(ratio), 3)} | {sum(1 for x in ratio if x < 0.95)} / {sum(1 for x in ratio if x > 1.05)} | {fmt(med(nd), 3)} | "
              f"{fmt(med([rows[(f, v)]['fm_ef_0005'] - rows[(f, 'A')]['fm_ef_0005'] for f in fs]), 3)} |")


def main():
    files_arg = [a for a in sys.argv[1:] if not a.startswith("--")]
    rows = load(files_arg)
    names = sorted({k[0] for k in rows})
    proc = [n for n in names if n.startswith("proc:")]
    t1 = [n for n in names if not n.startswith("proc:")]
    for title, group in (("T1 meshes", t1), ("procedural CAD-like inputs", proc)):
        have = [v for v in VARIANTS if any((f, v) in rows for f in group)]
        fs = [f for f in group if all((f, v) in rows for v in have if v not in ("D1", "R"))]
        if fs:
            table(rows, fs, have, title)
    if "--per-mesh" in sys.argv:
        print("\n| input | variant | tris | ms | edge chamfer | edge F 0.5 % | crease 2δ | clean |")
        print("|---|---|---|---|---|---|---|---|")
        for (f, v), r in sorted(rows.items(), key=lambda kv: (kv[0][0], VARIANTS.index(kv[0][1]))):
            print(f"| {f} | {v} | {r['tris']} | {r['ms']} | {r['fm_ecd']:.5f} | {r['fm_ef_0005']:.3f} | {r['crease_nd_d2_mean']:.2f} | {r['clean']} |")


main()
