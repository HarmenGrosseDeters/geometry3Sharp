#!/usr/bin/env python3
"""Summary of `TetMeshBenchmark floor` rows (docs/research/TET_DIHEDRAL_FLOOR_SPIKE_2026-11.md).
Usage: summarize_floor_spike.py <rows.csv> [more.csv ...]"""
import csv, statistics, sys

def rows(path):
    with open(path) as f:
        head = next(csv.reader(f))
        for r in csv.reader(f):
            if len(r) < len(head):
                yield {"method": r[0], "param": r[1], "name": r[2], "status": r[3]}
            else:
                d = dict(zip(head, r)); d["status"] = "ok"; yield d

def extra(d, key):
    for kv in d.get("extra", "").split():
        if kv.startswith(key + "="): return int(kv.split("=")[1])
    return 0

for path in sys.argv[1:]:
    rs = list(rows(path))
    print("==", path)
    ours = {r["name"]: r for r in rs if r["method"] == "ours" and r["param"] == "crease=0" and r["status"] == "ok"}
    for method, param in sorted({(r["method"], r["param"]) for r in rs}):
        g = [r for r in rs if r["method"] == method and r["param"] == param]
        ok = [r for r in g if r["status"] == "ok"]
        valid = [r for r in ok if r["valid"] == "yes"]
        f = lambda k: [float(r[k]) for r in ok]
        line = "%-9s %-10s inputs %d meshed %d valid %d | min dihedral worst %.2f median %.2f, all>=10: %d | max dihedral worst %.1f" % (
            method, param, len(g), len(ok), len(valid), min(f("mindih_worst")), statistics.median(f("mindih_worst")),
            sum(r["dih_ge10_all"] == "yes" for r in ok), max(f("max_dihedral")))
        line += " | tets median %d | time median %.2f s max %.2f" % (statistics.median(f("tets")), statistics.median(f("time_s")), max(f("time_s")))
        line += " | dev_in max: median %.4f worst %.4f, mean: median %.5f" % (statistics.median(f("dev_in_max_rel")), max(f("dev_in_max_rel")), statistics.median(f("dev_in_mean_rel")))
        if ours and method == "stuffing":
            both = [r for r in ok if r["name"] in ours]
            ratio = lambda k: statistics.median(float(r[k]) / float(ours[r["name"]][k]) for r in both)
            line += " | per input against ours (median of ratios): tets %.2f, dev_in max %.2f, dev_in mean %.2f, time %.2f" % (
                ratio("tets"), ratio("dev_in_max_rel"), ratio("dev_in_mean_rel"), ratio("time_s"))
        if method == "stuffing":
            line += " | inverted background tets %d, rule conflicts %d, quadruple-zero discarded %d" % (
                sum(extra(r, "inverted_background") for r in ok), sum(extra(r, "rule_conflicts") for r in ok), sum(extra(r, "quadzero_discarded") for r in ok))
        print(line)
        for r in g:
            if r["status"] != "ok" or r["valid"] != "yes":
                print("   not meshed or not valid:", r["name"], "-", r["status"] if r["status"] != "ok" else r["valid"])
