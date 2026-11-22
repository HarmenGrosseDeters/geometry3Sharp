# Tables of docs/research/ROUNDING_COST_2026-11.md from the output of a tool built with instrument_phases.py.
#   summarize_phases.py phases <profile.jsonl> <profile.err>    per operation: the boolean, the rounding and its parts
#   summarize_phases.py bound <profile.jsonl> <profile.err>     the upper bounds: the boolean without the first search,
#                                                               without the tree and the setup, without `near`
#   summarize_phases.py near <profile.jsonl> <profile.err>      the parts of `near` where it takes 0.15 s or more
#   summarize_phases.py ab <base> <variant> <rounds>            the A/B of run_ab_phases.sh (<base>_r1.jsonl, ...): per
#                                                               operation the faster round, rounding times, equal hashes
#   summarize_phases.py absum <dir> <rounds> <variant>...       the sums of the A/B per group of cases, every variant
#   summarize_phases.py cull <cull.jsonl>...                    the variants of `SnapRoundingExperiment cull`
# <profile.jsonl> is the standard output of `BooleanComparison profile --repeat 1 --safe-rounding <case>...` with
# G3_ROUNDING_PHASES=1, <profile.err> its standard error. Each process prints nine warm-up booleans first; the lines of
# the standard error are matched to the operations in order, twelve per case (nine warm-ups, then the three operations).
import json, sys

def load(path):
    return [json.loads(l) for l in open(path) if l.startswith("{")]

def phases(path):
    return [json.loads(l[len("ROUNDING_PHASES "):]) for l in open(path) if l.startswith("ROUNDING_PHASES ")]

def name(d):
    return "%s %s" % (d["case"].rstrip("/").split("/")[-1], d["op"])

def joined(out, err):
    rows, ph = load(out), phases(err)
    cases = []
    for d in rows:
        if d["case"] not in cases:
            cases.append(d["case"])
    per = len(ph) // len(cases)
    res = []
    for d in rows:
        k = cases.index(d["case"])
        ops = [r for r in rows if r["case"] == d["case"]]
        p = ph[k * per + per - len(ops) + ops.index(d)]
        assert p["triangles"] == d["triangles"] or d["triangles"] < 0, (name(d), p["triangles"], d["triangles"])
        res.append((d, p))
    return res

SETUP = ["P.setup.anchors_boundary", "P.setup.component_scales", "P.setup.range_scan", "P.setup.position_map", "P.setup.exact_flags"]

def cmd_phases(out, err):
    print("| operation | result triangles | queried | boolean | rounding | share | first search | first tree | setup | passes and later | `near` | later trees | later searches |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
    for d, p in joined(out, err):
        r = p["total_ms"]
        setup = sum(p.get(k, 0) for k in SETUP)
        rest = r - setup - p.get("P.tree.first", 0) - p.get("P.search.first", 0)
        queried = "%d" % (p["triangles"] - p["skipped_exact"]) if p["full_searches"] <= 1 and p["triangles"] >= p["skipped_exact"] else "-"
        print("| %s | %d | %s | %.2f s | %.2f s | %.0f %% | %.2f s | %.2f s | %.2f s | %.2f s | %.2f s (%d) | %.2f s (%d) | %.2f s (%d) |" % (
            name(d), d["triangles"], queried, d["ms"][0] / 1000, r / 1000, 100 * r / d["ms"][0],
            p.get("P.search.first", 0) / 1000, p.get("P.tree.first", 0) / 1000, setup / 1000, rest / 1000,
            p.get("T.near", 0) / 1000, p.get("N.near", 0), (p.get("T.tree", 0) - p.get("P.tree.first", 0)) / 1000, p.get("N.tree", 1) - 1,
            (p.get("T.search", 0) - p.get("P.search.first", 0)) / 1000, p.get("N.search", 1) - 1))

def cmd_bound(out, err):
    rows = joined(out, err)
    print("| operation | boolean | rounding | without the first search | without tree and setup | without `near` |")
    print("|---|---:|---:|---:|---:|---:|")
    tot = [0.0] * 5
    for d, p in rows:
        ms = d["ms"][0]
        s = p.get("P.search.first", 0)
        ts = p.get("P.tree.first", 0) + sum(p.get(k, 0) for k in SETUP)
        nr = p.get("T.near", 0)
        for i, v in enumerate([ms, p["total_ms"], s, ts, nr]):
            tot[i] += v
        print("| %s | %.2f s | %.2f s | -%.0f %% | -%.0f %% | -%.0f %% |" % (name(d), ms / 1000, p["total_ms"] / 1000, 100 * s / ms, 100 * ts / ms, 100 * nr / ms))
    print("| **sum of %d** | %.1f s | %.1f s | -%.1f %% | -%.1f %% | -%.1f %% |" % (len(rows), tot[0] / 1000, tot[1] / 1000, 100 * tot[2] / tot[0], 100 * tot[3] / tot[0], 100 * tot[4] / tot[0]))

def rounds_of(prefix, rounds):
    # per operation the row of the faster round, with the hashes of all rounds
    best, hashes = {}, {}
    for r in range(1, rounds + 1):
        for d in load("%s_r%d.jsonl" % (prefix, r)):
            k = name(d)
            hashes.setdefault(k, set()).add(d["hash"].split()[0])
            if k not in best or min(d["ms"]) < min(best[k]["ms"]):
                best[k] = d
    for k in best:
        best[k]["hashes"] = hashes[k]
    return best

def cmd_ab(base, variant, rounds):
    b, v = rounds_of(base, rounds), rounds_of(variant, rounds)
    print("| operation | boolean | with the variant | rounding | with the variant | hash |")
    print("|---|---:|---:|---:|---:|---|")
    tb = tv = rb = rv = 0
    same = 0
    for k, d in v.items():
        o = b[k]
        r0, r1 = o["phases_ms"].get("rounding", 0), d["phases_ms"].get("rounding", 0)
        m0, m1 = min(o["ms"]), min(d["ms"])
        tb += m0; tv += m1; rb += r0; rv += r1
        eq = len(o["hashes"] | d["hashes"]) == 1
        same += eq
        print("| %s | %.2f s | %.2f s | %.2f s | %.2f s | %s |" % (k, m0 / 1000, m1 / 1000, r0 / 1000, r1 / 1000, "same" if eq else "**differs**"))
    print("| **sum of %d** | %.1f s | %.1f s (%+.1f %%) | %.1f s | %.1f s (%+.1f %%) | %d of %d the same |" % (
        len(v), tb / 1000, tv / 1000, 100 * (tv - tb) / tb, rb / 1000, rv / 1000, 100 * (rv - rb) / max(rb, 1), same, len(v)))

def cmd_absum(folder, rounds, variants):
    b = rounds_of(folder + "/base", rounds)
    groups = [("procedural thin (gear, cyl, fins, plate)", lambda k: "_" in k.split()[0] and not k.startswith("sphere")),
              ("procedural control (sphere)", lambda k: k.startswith("sphere")),
              ("Thingi10K rotated, clean (519511, 1517923, 1063860, 668056, 119829)", lambda k: k.split()[0] in ("519511", "1517923", "1063860", "668056", "119829")),
              ("Thingi10K rotated, dirty (67709, 42327, 1368057, 102375, 498458, 486343)", lambda k: k.split()[0] in ("67709", "42327", "1368057", "102375", "498458", "486343")),
              ("all", lambda k: True)]
    print("| group | operations | base: booleans, rounding | " + " | ".join(variants) + " |")
    print("|---|---:|---:|" + "---:|" * len(variants))
    for title, f in groups:
        keys = [k for k in b if f(k)]
        tb = sum(min(b[k]["ms"]) for k in keys); rb = sum(b[k]["phases_ms"].get("rounding", 0) for k in keys)
        cells = []
        for vn in variants:
            v = rounds_of(folder + "/" + vn, rounds)
            if any(k not in v for k in keys):
                cells.append("-"); continue
            tv = sum(min(v[k]["ms"]) for k in keys); rv = sum(v[k]["phases_ms"].get("rounding", 0) for k in keys)
            same = sum(len(b[k]["hashes"] | v[k]["hashes"]) == 1 for k in keys)
            cells.append("%+.1f %%, rounding %+.1f %%, %d of %d hashes the same" % (100 * (tv - tb) / tb, 100 * (rv - rb) / max(rb, 1), same, len(keys)))
        print("| %s | %d | %.1f s, %.1f s | %s |" % (title, len(keys), tb / 1000, rb / 1000, " | ".join(cells)))

def cmd_near(out, err):
    print("| operation | rounding | `near` | calls | per call | tree of the start | tree of the changed: builds | its queries | linear list | returned per call | start-tree hits per call | list length per call |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
    for d, p in joined(out, err):
        n = p.get("N.near", 0)
        if p.get("T.near", 0) < 150:
            continue
        print("| %s | %.2f s | %.2f s | %d | %.1f us | %.2f s | %.2f s (%d, %.0f triangles each) | %.2f s | %.2f s | %.0f | %.0f | %.0f |" % (
            name(d), p["total_ms"] / 1000, p["T.near"] / 1000, n, 1000 * p["T.near"] / n, p.get("T.near_main", 0) / 1000,
            p.get("T.near_dirty_build", 0) / 1000, p.get("N.near_dirty_builds", 0), p.get("N.near_dirty_build_size", 0) / max(1, p.get("N.near_dirty_builds", 0)),
            p.get("T.near_dirty_query", 0) / 1000, p.get("T.near_pending", 0) / 1000, p["N.near_hits"] / n, p.get("N.near_main_raw", 0) / n, p["N.near_pending_scanned"] / n))

def cmd_cull(paths):
    rows = [d for p in paths for d in load(p)]
    variants = [k for k, v in rows[0].items() if isinstance(v, dict)]
    print("| operation | queries | " + " | ".join(variants) + " | all the same pairs | nodes visited per query: library tree, median split |")
    print("|---|---:|" + "---:|" * len(variants) + "---|---|")
    for d in rows:
        cells = []
        for v in variants:
            x = d[v]
            cells.append("%.0f ms, %.0f hits, %.2fM tests" % (x["ms"], x["hits_per_query"], x["pair_tests"] / 1e6))
        visits = "%.0f, %.0f" % (d["visits_per_query_lib"], d["visits_per_query_own"]) if "visits_per_query_lib" in d else ""
        print("| %s | %d | %s | %s | %s |" % (name(d), d["queries"], " | ".join(cells), "yes" if all(d[v]["same_pairs"] for v in variants) else "**no**", visits))

if __name__ == "__main__":
    c = sys.argv[1]
    if c == "phases": cmd_phases(sys.argv[2], sys.argv[3])
    elif c == "bound": cmd_bound(sys.argv[2], sys.argv[3])
    elif c == "near": cmd_near(sys.argv[2], sys.argv[3])
    elif c == "ab": cmd_ab(sys.argv[2], sys.argv[3], int(sys.argv[4]))
    elif c == "absum": cmd_absum(sys.argv[2], int(sys.argv[3]), sys.argv[4:])
    elif c == "cull": cmd_cull(sys.argv[2:])
    else: raise SystemExit("unknown command " + c)
