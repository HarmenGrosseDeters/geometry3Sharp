#!/usr/bin/env python3
"""Tables of the speed study docs/research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md from the rows of
`BooleanComparison seam-levers` and `BooleanComparison profile` (one JSON object per line, plain or .gz).

  seam_levers_report.py phases <profile rows, 1 thread> <profile rows, n threads>
      the phase sums of a case set (eg T6) at two thread counts, the speed-up and what stays outside the timers
  seam_levers_report.py seam <seam-levers rows>...
      per operation and tube length: time, the far work (the time above the shortest tube) and its split by phase
  seam_levers_report.py selfjoin <seam-levers rows>...
      the operand self-join: the arrangement with and without it, per case and summed
  seam_levers_report.py tree <seam-levers rows> [<profile rows of the same cases and thread count>]
      the box-tree work alone (build, self-join traversal, queries) and the local set, summed
  seam_levers_report.py local <seam-levers rows with --local-rounding>...
      the rounding's pair search with a tree of all triangles against a tree of the new triangles only, and the
      contact count by dictionary against the flat table

Sums are over the best run of each row. Nothing here reads a mesh.
"""
import collections
import gzip
import json
import sys

TOP = ["input", "setup", "intersections", "merge", "triangulation", "classification", "assembly", "rounding"]


def load(path):
    opener = gzip.open if path.endswith(".gz") else open
    with opener(path, "rt") as f:
        return [json.loads(line) for line in f if line.strip()]


def best(row):
    return row["best_ms"] if "best_ms" in row else min(row["ms"])


def phase_sums(rows):
    sums = collections.Counter()
    total = 0.0
    for r in rows:
        total += best(r)
        for k, v in r["phases_ms"].items():
            sums[k] += v / 1000.0 if k.endswith("_us") else v
    sums["rest (outside the timers)"] = total - sum(sums[k] for k in TOP)
    return sums, total


def phases(args):
    a, b = load(args[0]), load(args[1])
    booleans = [r for r in a if "phases_ms" in r]
    sa, ta = phase_sums(booleans)
    sb, tb = phase_sums([r for r in b if "phases_ms" in r])
    ca, cb = booleans[0]["cores"], [r for r in b if "phases_ms" in r][0]["cores"]
    hashes = {(r["case"], r["op"]): r["hash"] for r in a if "hash" in r}
    drift = sum(1 for r in b if "hash" in r and hashes.get((r["case"], r["op"])) != r["hash"])
    print(f"{len(booleans)} booleans; result hashes that differ between the two files: {drift}")
    print(f"| phase | {ca} thr ms | {cb} thr ms | speed-up | share at {ca} | share at {cb} |")
    print("|---|---:|---:|---:|---:|---:|")
    for k in sa:
        x, y = sa[k], sb[k]
        if max(x, y) < 0.005 * ta:
            continue
        print(f"| {k} | {x:.0f} | {y:.0f} | {x / max(y, 1e-9):.2f} | {100 * x / ta:.1f} % | {100 * y / tb:.1f} % |")
    print(f"| **total** | {ta:.0f} | {tb:.0f} | {ta / tb:.2f} | | |")
    for rows, t, c in ((a, ta, ca), (b, tb, cb)):
        rows = [r for r in rows if "phases_ms" in r]
        print(f"{c} thr: allocated {sum(r['alloc_mb'] for r in rows) / 1024:.1f} GB, GC pauses {sum(r['gc']['pause_ms'] for r in rows)} ms"
              f" ({100 * sum(r['gc']['pause_ms'] for r in rows) / t:.1f} %)")


def seam(args):
    rows = [r for p in args for r in load(p) if r.get("part") == "boolean"]
    keys = ["input", "setup", "intersections.trees", "intersections.pairs", "merge", "triangulation", "classification", "assembly", "rounding"]
    by = collections.defaultdict(dict)
    for r in rows:
        by[(r["cores"], r["op"])][r["half"]] = r
    print("| threads | operation | half length | input triangles | result triangles | ms | far work | " + " | ".join(keys) + " | rest |")
    print("|---|---|---:|---:|---:|---:|---:|" + "---:|" * (len(keys) + 1))
    for (cores, op), d in sorted(by.items()):
        shortest = d[min(d)]
        for half in sorted(d):
            r = d[half]
            p = r["phases_ms"]
            rest = best(r) - sum(p[k] for k in TOP)
            far = 1 - best(shortest) / best(r)
            print(f"| {cores} | {op} | {half:g} | {r['input_triangles']} | {r['triangles']} | {best(r):.0f} | {100 * far:.0f} % | "
                  + " | ".join(str(p[k]) for k in keys) + f" | {rest:.0f} |")
        long = d[max(d)]
        dn = long["input_triangles"] - shortest["input_triangles"]
        if dn > 0:
            print(f"|  | {op}: per far input triangle |  |  |  | {1000 * (best(long) - best(shortest)) / dn:.2f} us |  | "
                  + " | ".join(f"{1000 * (long['phases_ms'][k] - shortest['phases_ms'][k]) / dn:.2f}" for k in keys) + " |  |")


def selfjoin(args):
    rows = [r for p in args for r in load(p) if r.get("part") == "arrangement"]
    by = collections.defaultdict(list)
    for r in rows:
        by[(r["cores"], r["set"])].append(r)
    print("| threads | set | cases | input triangles | with self-join ms | without ms | self-join | share of the arrangement | pieces equal | self pairs found |")
    print("|---|---|---:|---:|---:|---:|---:|---:|---|---:|")
    for (cores, name), rs in sorted(by.items()):
        groups = collections.defaultdict(list)
        for r in rs:
            groups[r["half"]].append(r)
        for half, g in sorted(groups.items()):
            w, n = sum(r["self_ms"] for r in g), sum(r["noself_ms"] for r in g)
            label = name if half == 0 else f"{name}, half {half:g}"
            print(f"| {cores} | {label} | {len(g)} | {sum(r['input_triangles'] for r in g)} | {w:.0f} | {n:.0f} | {w - n:.0f} | {100 * (w - n) / w:.0f} % | "
                  f"{sum(1 for r in g if r['same_pieces'])} of {len(g)} | {sum(r['self_intersecting_pairs'] for r in g)} |")


def tree(args):
    rows = load(args[0])
    profile = load(args[1]) if len(args) > 1 else []
    for half in sorted({r["half"] for r in rows}):
        t = [r for r in rows if r.get("part") == "traversal" and r["half"] == half]
        n = [r for r in rows if r.get("part") == "near" and r["half"] == half]
        s = [r for r in rows if r.get("part") == "stamp" and r["half"] == half]
        if not t:
            continue
        tris = sum(r["input_triangles"] for r in t)
        print(f"half {half:g}: {len(t)} cases, {tris} input triangles")
        print(f"  tree build {sum(r['tree_build_ms'] for r in t):.0f} ms, self-join traversal {sum(r['self_traversal_ms'] for r in t):.0f} ms "
              f"({sum(r['self_box_pairs'] for r in t)} box pairs), A in B queries {sum(r['cross_query_ms'] for r in t):.0f} ms ({sum(r['cross_box_hits'] for r in t)} hits)")
        if n:
            near = sum(r["near_a"] + r["near_b"] for r in n)
            shares = sorted((r["near_a"] + r["near_b"]) / (r["triangles_a"] + r["triangles_b"]) for r in n)
            print(f"  local set {near} triangles = {100 * near / tris:.1f} % of the input (per case: median {100 * shares[len(shares) // 2]:.1f} %, "
                  f"p10 {100 * shares[len(shares) // 10]:.1f} %, p90 {100 * shares[(9 * len(shares)) // 10]:.1f} %), found in {sum(r['near_query_ms'] for r in n):.0f} ms")
        if s:
            print(f"  content stamp: 64-bit mix {sum(r['mix64_ms'] for r in s):.0f} ms, SHA-256 {sum(r['sha256_ms'] for r in s):.0f} ms")
    if profile:
        total = sum(best(r) for r in profile if "phases_ms" in r)
        ops = len([r for r in profile if "phases_ms" in r]) / max(1, len({r["case"] for r in profile if "phases_ms" in r}))
        t = [r for r in rows if r.get("part") == "traversal"]
        trav = ops * sum(r["self_traversal_ms"] + r["cross_query_ms"] for r in t)
        print(f"  against the booleans of the profile ({total:.0f} ms, {ops:.0f} operations per case): the input traversals are {100 * trav / total:.1f} % of the time")


def local(args):
    rows = [r for p in args for r in load(p) if r.get("part") == "boolean" and "local_rounding" in r]
    by = collections.defaultdict(list)
    for r in rows:
        by[(r["cores"], r["half"], r["op"])].append(r)
    print("| threads | set | operation | booleans | boolean ms | rounding ms | result triangles | new triangles | tree of all + queries ms | tree of the new + join + scan ms | saved | of the rounding | of the boolean | same pairs | contacts: dictionary ms | flat ms |")
    print("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|")
    totals = collections.defaultdict(lambda: collections.Counter())
    for (cores, half, op), g in sorted(by.items()):
        L = [r["local_rounding"] for r in g]
        full = sum(x["full_build_ms"] + x["full_query_ms"] for x in L)
        loc = sum(x["local_build_ms"] + x["local_join_ms"] + x["local_scan_ms"] for x in L)
        rounding = sum(r["phases_ms"].get("rounding", 0) for r in g)
        total = sum(best(r) for r in g)
        C = [r["contacts"] for r in g if "contacts" in r]
        name = "cases" if half == 0 else f"tubes, half {half:g}"
        print(f"| {cores} | {name} | {op} | {len(g)} | {total:.0f} | {rounding} | {sum(x['result_triangles'] for x in L)} | {sum(x['new_triangles'] for x in L)} | "
              f"{full:.0f} | {loc:.0f} | {full - loc:.0f} | {100 * (full - loc) / max(rounding, 1):.0f} % | {100 * (full - loc) / total:.1f} % | "
              f"{sum(1 for x in L if x['same_pairs'])} of {len(L)} | {sum(c['dictionary_ms'] for c in C):.0f} | {sum(c['flat_ms'] for c in C):.0f} |")
        if half == 0:
            t = totals[cores]
            t["n"] += len(g); t["total"] += total; t["rounding"] += rounding; t["full"] += full; t["loc"] += loc
            t["dict"] += sum(c["dictionary_ms"] for c in C); t["flat"] += sum(c["flat_ms"] for c in C)
            t["same"] += sum(1 for c in C if c["same_count"]); t["contacts"] += len(C)
    for cores, t in sorted(totals.items()):
        print(f"cases, {cores} thr, all operations: {t['n']} booleans, {t['total']:.0f} ms; rounding {t['rounding']} ms; search structure {t['full']:.0f} -> {t['loc']:.0f} ms "
              f"(-{100 * (t['full'] - t['loc']) / t['total']:.1f} % of the booleans); contacts {t['dict']:.0f} -> {t['flat']:.0f} ms "
              f"(-{100 * (t['dict'] - t['flat']) / t['total']:.1f} %), counts equal in {t['same']} of {t['contacts']}")


if __name__ == "__main__":
    commands = {"phases": phases, "seam": seam, "selfjoin": selfjoin, "tree": tree, "local": local}
    if len(sys.argv) < 3 or sys.argv[1] not in commands:
        sys.exit(__doc__)
    commands[sys.argv[1]](sys.argv[2:])
