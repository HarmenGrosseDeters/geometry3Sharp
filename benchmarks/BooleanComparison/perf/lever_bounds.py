#!/usr/bin/env python3
"""Amdahl bounds of the levers in docs/perf/BOOLEAN_THREAD_SCALING.md, from two probe runs (1 thread and n threads, same cases).

  python3 perf/lever_bounds.py <probe 1 thread.jsonl> <probe n threads.jsonl> <n> [<target threads>=12]

A lever is a group of scopes (see GROUPS: serial probe scopes `R.*`, `A.*`, `C.*`, `par:<file>:<line>` and the phase times of
the profile rows). For each group: the time at 1 thread (T1) and at n threads (Tn), the serial fraction s = (Tn - T1/n) / (T1 - T1/n)
(1: no speedup, 0: linear), the time at the target thread count under the same s (T1 (s + (1 - s) / m)), and the upper bound of the
total speedup (Amdahl) if the group became fully parallel: total(n) / (total(n) - (Tn - T1/n)) at n threads and the same at the target.
The target extrapolation assumes that s stays the same, which is an upper bound for the cost of the serial part (more threads, more
contention) and a lower bound for what a parallel loop gains.
"""
import gzip, json, sys

def opener(p):
    return gzip.open(p, "rt", encoding="utf-8-sig") if p.endswith(".gz") else open(p, encoding="utf-8-sig")

def load(path):
    rows = {}
    for l in opener(path):
        l = l.strip()
        if l.startswith("{"):
            j = json.loads(l)
            if "probe" in j:
                rows[(j["case"], j["op"])] = j
    return rows

# group name -> (probe scopes, profile phases); a scope or phase listed once, nested scopes not repeated
GROUPS = [
    ("box trees: input meshes (par 1879) + SafeRounding rebuild_tree", ["par:ExactMeshArrangement.cs:1879", "R.rebuild_tree"], []),
    ("assembly serial steps (A.1-A.5, A.7-A.10: gather, slots, group_edges, edge pairing, vertices, append triangles)",
        ["A.1 gather kept pieces", "A.2 corner slots", "A.3 group_edges", "A.4 edge pairing (serial union-find)", "A.5 used points", "A.7 vertices",
         "A.8 non-manifold splits", "A.9 append triangles", "A.10 fixed / point ids"], []),
    ("ExactPlanarSimplify (simplify phase)", [], ["simplify"]),
    ("classification serial steps (C.2-C.4, C.6: group_edges, patch joins, open prep, decisions)",
        ["C.2 group_edges", "C.3 patch joins (serial union-find)", "C.4 open prep (serial)", "C.6 open winding + decisions (serial)"], []),
    ("rounding: component_scales, remove_tiny_components, false_contacts, edit loop (repair_passes)",
        ["R.component_scales", "R.remove_tiny_components", "R.false_contacts", "R.repair_passes", "R.position_search"], []),
    ("setup.points + input phase", [], ["setup.points", "input"]),
    ("self-join and mesh-pair search, load balance (par 2210, 1926)", ["par:ExactMeshArrangement.cs:2210", "par:ExactMeshArrangement.cs:1926"], []),
    ("SafeRounding pair search (intersecting_pairs_all)", ["R.intersecting_pairs_all"], []),
    ("count_contacts + counts after the rounding", ["X.count_contacts"], []),
]

def sums(rows, scopes, phases):
    t = 0.0
    for j in rows.values():
        for s in scopes:
            if s in j["probe"]:
                t += j["probe"][s]["ms"]
        for p in phases:
            t += j["phases_ms"].get(p, 0)
    return t

def main(a):
    r1, rn, n = load(a[0]), load(a[1]), int(a[2])
    m = int(a[3]) if len(a) > 3 else 12
    keys = sorted(set(r1) & set(rn))
    r1 = {k: r1[k] for k in keys}; rn = {k: rn[k] for k in keys}
    tot1 = sum(min(j["ms"]) for j in r1.values()); totn = sum(min(j["ms"]) for j in rn.values())
    totm = None
    print(f"{len(keys)} booleans; total {tot1:.0f} ms at 1 thread, {totn:.0f} ms at {n} (speedup {tot1 / totn:.2f}); serial fraction of the whole s = {(totn - tot1 / n) / (tot1 - tot1 / n):.2f}\n")
    print(f"| lever (group of scopes) | T1 ms | T{n} ms | share of T{n} | s | T{m} ms (same s) | bound total speedup at {n} | at {m} |")
    print("|---|---|---|---|---|---|---|---|")
    # the whole at m threads under one s per group is the sum of the groups' projections plus the rest's
    s_all = (totn - tot1 / n) / (tot1 - tot1 / n)
    tot_m = tot1 * (s_all + (1 - s_all) / m)
    sum_n = sum_m = 0.0
    for name, scopes, phases in GROUPS:
        t1, tn = sums(r1, scopes, phases), sums(rn, scopes, phases)
        if t1 <= 0:
            continue
        s = (tn - t1 / n) / (t1 - t1 / n)
        tm = t1 * (s + (1 - s) / m)
        gain_n = max(0.0, tn - t1 / n)
        gain_m = max(0.0, tm - t1 / m)
        if "pair search" not in name and "self-join" not in name:
            sum_n += gain_n; sum_m += gain_m
        print(f"| {name} | {t1:.0f} | {tn:.0f} | {100 * tn / totn:.1f} % | {s:.2f} | {tm:.0f} | {tot1 / (totn - gain_n):.2f} (now {tot1 / totn:.2f}) | {tot1 / max(1.0, tot_m - gain_m):.2f} (now {tot1 / tot_m:.2f}) |")
    print(f"\nAll serial levers together (not the two pair-search rows): speedup bound {tot1 / (totn - sum_n):.2f} at {n} threads, {tot1 / max(1.0, tot_m - sum_m):.2f} at {m}.")
    print(f"\nWhole boolean at {m} threads under one s: {tot_m:.0f} ms (speedup {tot1 / tot_m:.2f}); the rows' bounds are for removing that group's serial part alone.")

if __name__ == "__main__":
    main(sys.argv[1:])
