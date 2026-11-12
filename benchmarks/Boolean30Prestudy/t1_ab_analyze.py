#!/usr/bin/env python3 -I
"""Median over the rounds of the total time and of chosen phases, per (case, cores, build), and the hashes (must be equal).
usage: t1_ab_analyze.py results.jsonl [phase ...]   (default phases: input intersections.trees assembly)"""
import json, sys, statistics
rows = [json.loads(l) for l in open(sys.argv[1]) if l.strip()]
phases = sys.argv[2:] or ["input", "intersections.trees", "assembly"]
groups = {}
for r in rows:
    groups.setdefault((r["case"], r["cores"]), {}).setdefault(r["build"], []).append(r)
print("| case | cores | build | total ms | " + " | ".join(phases) + " | rounds | hash |")
print("|---|---|---|---|" + "---|" * len(phases) + "---|---|")
for (case, cores), g in sorted(groups.items()):
    hashes = {b: {r["hash"] for r in rs} for b, rs in g.items()}
    for b in ("base", "new"):
        rs = g.get(b, [])
        if not rs:
            continue
        tot = statistics.median(min(r["ms"]) for r in rs)
        ph = [statistics.median(r["phases_ms"].get(p, 0) for r in rs) for p in phases]
        print(f"| {case} | {cores} | {b} | {tot:.0f} | " + " | ".join(f"{x:.0f}" for x in ph) + f" | {len(rs)} | {'equal' if len(set.union(*hashes.values())) == 1 else 'DIFFERENT'} |")
