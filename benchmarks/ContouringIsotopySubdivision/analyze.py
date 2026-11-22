#!/usr/bin/env python3
"""Tables of docs/research/CONTOURING_ISOTOPY_SUBDIVISION_2026-11.md from results/all.csv.

    python3 benchmarks/ContouringIsotopySubdivision/analyze.py benchmarks/ContouringIsotopySubdivision/results/all.csv
"""
import csv
import statistics
import sys


def load(path):
    with open(path, newline="") as f:
        rows = list(csv.DictReader(f))
    for r in rows:
        for k in ("N", "seed", "surface_cells", "pv_cells", "s_cells", "both", "neither", "pv_refuted", "not_smooth_cells",
                  "uncertain_node_cells", "ambiguous_faces_in_pv", "multi_patch_in_pv", "pv_at_t0", "pv_at_t1", "pv_at_t2",
                  "pv_at_t3", "pv_at_t4", "s_fail_s0", "s_fail_s1", "s_fail_s2", "s_fail_s3_graph", "s_fail_s3_circles"):
            r[k] = int(r[k])
        for k in ("pv_ms", "s_ms", "dc_ms", "k"):
            r[k] = float(r[k])
    return rows


GROUPS = [
    ("smooth, feature >= 2 cells", lambda r: (r["case"] == "torus" and r["k"] >= 2) or r["case"] == "sphere"),
    ("torus, tube 0.5 and 1 cells", lambda r: r["case"] == "torus" and r["k"] < 2),
    ("gap (two spheres, min)", lambda r: r["case"] == "gap"),
    ("shell (two levels, max)", lambda r: r["case"] == "shell"),
    ("tunnel (crease 90 degrees)", lambda r: r["case"] == "tunnel"),
    ("rod (concave crease)", lambda r: r["case"] == "rod"),
    ("capped TPMS, level 0", lambda r: r["case"] in ("gyroid", "schwarzp", "diamond") and r["k"] == 0),
    ("capped gyroid, levels 0.6 to 1.35", lambda r: r["case"] == "gyroid" and r["k"] > 0),
    ("Cassini, b/a 1.2 to 1.01 and 0.99 to 0.95", lambda r: r["case"] == "cassini" and abs(r["k"] - 1) > 0.005),
    ("Cassini, b/a 1.001, 1, 0.999", lambda r: r["case"] == "cassini" and abs(r["k"] - 1) < 0.005),
    ("sphere through nodes", lambda r: r["case"] == "nodesphere"),
]


def pct(a, b):
    return "-" if b == 0 else "%.1f %%" % (100.0 * a / b)


def main():
    rows = load(sys.argv[1])
    print("## A. Certified runs and cells, per group and grid\n")
    print("| group | N | runs | contour right / wrong / truth unknown | PV certified (of them wrong) | S certified (of them wrong) | surface cells | PV cells | S cells | only PV | only S | neither |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for name, sel in GROUPS:
        for n in sorted({r["N"] for r in rows}):
            g = [r for r in rows if sel(r) and r["N"] == n]
            if not g:
                continue
            right = sum(r["dc_right"] == "yes" for r in g)
            wrong = sum(r["dc_right"] == "no" for r in g)
            unk = sum(r["dc_right"] == "?" for r in g)
            pv = [r for r in g if r["pv_certified"] == "yes"]
            s = [r for r in g if r["s_certified"] == "yes"]
            cells = sum(r["surface_cells"] for r in g)
            pvc = sum(r["pv_cells"] for r in g)
            sc = sum(r["s_cells"] for r in g)
            both = sum(r["both"] for r in g)
            print("| %s | %d | %d | %d / %d / %d | %d (%d) | %d (%d) | %d | %s | %s | %s | %s | %s |" % (
                name, n, len(g), right, wrong, unk, len(pv), sum(r["dc_right"] == "no" for r in pv), len(s),
                sum(r["dc_right"] == "no" for r in s), cells, pct(pvc, cells), pct(sc, cells), pct(pvc - both, cells),
                pct(sc - both, cells), pct(sum(r["neither"] for r in g), cells)))

    print("\n## B. Why cells fail\n")
    print("| group | surface cells | PV: passed at t = 0 / 1 / 2 / 3 / 4 | PV: C1 refuted | PV: not smooth | uncertain node | S fails: edges (S1) / faces (S2) / no graph direction / several circles |")
    print("|---|---|---|---|---|---|---|")
    for name, sel in GROUPS:
        g = [r for r in rows if sel(r)]
        if not g:
            continue
        cells = sum(r["surface_cells"] for r in g)
        print("| %s | %d | %s | %s | %s | %s | %s |" % (
            name, cells, " / ".join(pct(sum(r["pv_at_t%d" % t] for r in g), cells) for t in range(5)),
            pct(sum(r["pv_refuted"] for r in g), cells), pct(sum(r["not_smooth_cells"] for r in g), cells),
            pct(sum(r["uncertain_node_cells"] for r in g), cells),
            " / ".join(pct(sum(r[k] for r in g), cells) for k in ("s_fail_s1", "s_fail_s2", "s_fail_s3_graph", "s_fail_s3_circles"))))

    print("\n## C. The subdivision (octree to four levels below the grid)\n")
    print("| group | N | runs | runs with unresolved leaves | unresolved leaves (of them with a non-smooth point) | deepest C1 leaf, max over runs | most frequent C1 level, median |")
    print("|---|---|---|---|---|---|---|")
    for name, sel in GROUPS:
        for n in sorted({r["N"] for r in rows}):
            g = [r for r in rows if sel(r) and r["N"] == n and r["sub_leaves"] != "-"]
            if not g:
                continue
            unres = [int(r["sub_unresolved"]) for r in g]
            deep = [int(r["sub_deepest_c1"]) for r in g if r["sub_deepest_c1"] != "-"]
            modal = [int(r["sub_modal_c1"]) for r in g if r["sub_modal_c1"] != "-"]
            print("| %s | %d | %d | %d | %d (%d) | %s | %s |" % (
                name, n, len(g), sum(u > 0 for u in unres), sum(unres), sum(int(r["sub_unresolved_not_smooth"]) for r in g),
                max(deep) if deep else "-", statistics.median(modal) if modal else "-"))

    print("\n## D. Time (ms, one run; ratio to DualContouring.Generate)\n")
    print("| N | runs | contour ms, median | PV test: median ratio (min to max) | S test in addition: median ratio (min to max) | octree: median ratio |")
    print("|---|---|---|---|---|---|")
    for n in sorted({r["N"] for r in rows}):
        g = [r for r in rows if r["N"] == n and r["dc_ms"] > 0]
        pv = [r["pv_ms"] / r["dc_ms"] for r in g]
        s = [r["s_ms"] / r["dc_ms"] for r in g]
        sub = [float(r["sub_ms"]) / r["dc_ms"] for r in g if r["sub_ms"] != "-"]
        print("| %d | %d | %.0f | %.2f (%.2f to %.2f) | %.2f (%.2f to %.2f) | %s |" % (
            n, len(g), statistics.median(r["dc_ms"] for r in g), statistics.median(pv), min(pv), max(pv),
            statistics.median(s), min(s), max(s), "%.2f" % statistics.median(sub) if sub else "-"))

    print("\n## E. Agreement of the verdicts (runs with a class P result)\n")
    g = [r for r in rows if r["class_p"] in ("yes", "no")]
    print("| PV | S | class P (its own contour) | runs | DualContouring right | class P contour right |")
    print("|---|---|---|---|---|---|")
    for pv in ("yes", "no"):
        for s in ("yes", "no"):
            for p in ("yes", "no"):
                h = [r for r in g if r["pv_certified"] == pv and r["s_certified"] == s and r["class_p"] == p]
                if h:
                    print("| %s | %s | %s | %d | %d | %d |" % (pv, s, p, len(h), sum(r["dc_right"] == "yes" for r in h),
                                                             sum(r["class_p_sig"] == r["truth"] for r in h)))
    print("\nruns without a class P input (the marching-cubes mesh is itself wrong or open): %d" % sum(r["class_p"] == "no-input" for r in rows))

    print("\n## F. Soundness\n")
    print("runs: %d; PV certified: %d, of them with a wrong contour: %d; S certified: %d, of them wrong: %d; class P certified: %d, of them with a wrong class P contour: %d" % (
        len(rows), sum(r["pv_certified"] == "yes" for r in rows), sum(r["pv_certified"] == "yes" and r["dc_right"] == "no" for r in rows),
        sum(r["s_certified"] == "yes" for r in rows), sum(r["s_certified"] == "yes" and r["dc_right"] == "no" for r in rows),
        sum(r["class_p"] == "yes" for r in rows), sum(r["class_p"] == "yes" and r["class_p_sig"] != r["truth"] for r in rows)))
    amb = sum(r["ambiguous_faces_in_pv"] for r in rows)
    print("ambiguous faces among PV cells (each counted from both sides): %d; PV cells with more than one patch: %d" % (amb, sum(r["multi_patch_in_pv"] for r in rows)))


if __name__ == "__main__":
    main()
