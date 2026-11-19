#!/usr/bin/env python3
"""Summary table of the MaxDihedralDegrees sample (benchmarks/TetMeshBenchmark/MaxDihedral.cs, run_maxdih_shard.sh).
Usage: maxdih_summary.py <csv> [<csv> ...]   (the rows master / p4 / p4g of every mesh, one CSV per shard)
Columns as the measurement of the throwaway branch runs/tet-stellar (SUMMARY_tables.md): meshes where the smallest dihedral of every tet is
>= F (min ok), the largest <= G (max ok), both; valid; medians of the 1 % and 99 % quantiles; worst min / max; tets against master; the
improver's time per tet and in total; tets violating F or G (sum). Only meshes with every configuration count."""
import sys, collections, statistics as st

H = "name,alpha,floor,G,cfg,status,tets,imp_s,stage1_s,imp_us_per_tet,total_us_per_tet,min,p01,max,p99,min_lt_F,max_gt_G,fail_either,valid,counts".split(",")
rows = []
for f in sys.argv[1:]:
    for l in open(f):
        p = l.strip().split(",", len(H) - 1)
        if len(p) >= len(H) - 1 and p[5] in ("Success", "PointLimitReached"):
            rows.append(dict(zip(H, p)))
by = collections.defaultdict(dict)
for r in rows: by[r["cfg"]][r["name"]] = r
order = [c for c in ("master", "p4", "p4g", "p4s") if c in by]
common = sorted(set.intersection(*[set(by[c]) for c in order]))
F = float(rows[0]["floor"]); G = float(rows[0]["G"])
print("floor %g, max %g; %d meshes with every config (of %d with a master row)\n" % (F, G, len(common), len(by["master"])))
print("| config | min>=F | max<=G | both | valid | median p01 / p99 | worst min / max | tets vs master (median) | improver us/tet (median) | improver s (sum / max) | violating tets (sum) | selfint | input verts outside |")
print("|---|---:|---:|---:|---:|---|---|---:|---:|---|---:|---:|---:|")
base = by["master"]
for c in order:
    r = [by[c][n] for n in common]
    g = lambda k: [float(x[k]) for x in r]
    minok = sum(float(x["min"]) >= F - 1e-9 for x in r); maxok = sum(float(x["max"]) <= G + 1e-9 for x in r)
    both = sum(float(x["min"]) >= F - 1e-9 and float(x["max"]) <= G + 1e-9 for x in r)
    ratio = st.median([float(by[c][n]["tets"]) / float(base[n]["tets"]) for n in common])
    si = sum(" check_selfint=1" in x["counts"] for x in r); out = sum(" check_input_verts_outside=" in x["counts"] and not " check_input_verts_outside=0/" in x["counts"] for x in r)
    print("| %s | %d | %d | %d | %d | %.1f / %.1f | %.2f / %.1f | %.3f | %.0f | %.0f / %.0f | %d | %d | %d |" % (
        c, minok, maxok, both, sum(x["valid"] == "yes" for x in r), st.median(g("p01")), st.median(g("p99")), min(g("min")), max(g("max")), ratio,
        st.median(g("imp_us_per_tet")), sum(g("imp_s")), max(g("imp_s")), sum(int(x["fail_either"]) for x in r), si, out))
# meshes lost on the min side relative to master
for c in order[1:]:
    lost = [n for n in common if float(base[n]["min"]) >= F - 1e-9 and float(by[c][n]["min"]) < F - 1e-9]
    worse = [n for n in common if float(by[c][n]["min"]) < min(F, float(base[n]["min"])) - 1e-9]
    print("\n%s: %d meshes reach min >= F under master but not here; %d end below min(F, master's worst min)" % (c, len(lost), len(worse)))
