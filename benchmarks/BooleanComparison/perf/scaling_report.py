#!/usr/bin/env python3
"""Thread scaling of ExactMeshBoolean by phase, from `BooleanComparison profile` output (one JSON line per case and operation).

  python3 perf/scaling_report.py <threads>=<file.jsonl>[,<file.jsonl>...] ...        (several files of one thread count: the best
                                                                                      time per case and operation is taken)
  python3 perf/scaling_report.py --probe <threads>=<file.jsonl> ...                   the serial-probe rows (docs/perf/BOOLEAN_THREAD_SCALING.md)
  python3 perf/scaling_report.py --hashes ...                                         only the drift check: every hash equal across the files

Per phase: the sum over the cases and operations at each thread count, the speedup against the first count, and the serial fraction s
of Amdahl's law implied by it, s = (T_n - T_1 / n) / (T_1 - T_1 / n) (1 = does not scale at all, 0 = scales linearly). `--bound` adds
the total speedup at each thread count if one phase alone became linear (its time falling to T_1 / n). Phases that contain others (setup,
intersections, merge) are not listed; simplify is part of assembly in the rows and listed apart; "rest" is the time of the boolean
outside the timed phases. Files may be gzip (.gz).
"""
import gzip, json, sys, collections

PARTS = ["input", "setup.points", "setup.triangles", "intersections.trees", "intersections.cull", "intersections.pairs",
         "intersections.planes", "intersections.intersect", "merge.points", "merge.crossings", "merge.points2", "merge.predict",
         "triangulation", "classification", "simplify", "assembly", "rounding"]
# assembly includes simplify (the phase clock of the simplification is nested in it); simplify is reported separately and
# subtracted from assembly
def load(paths):
    rows = {}
    for path in paths:
        for l in (gzip.open(path, "rt") if path.endswith(".gz") else open(path)):
            l = l.strip()
            if not l.startswith("{"):
                continue
            j = json.loads(l)
            if "phases_ms" not in j or not j.get("ok", True):
                continue
            key = (j["case"], j["op"])
            ms = min(j["ms"]) if isinstance(j["ms"], list) else j["ms"]
            if key not in rows or ms < rows[key][0]:
                rows[key] = (ms, j)
    return rows

def phase_sums(rows, keys):
    s = collections.OrderedDict((p, 0.0) for p in PARTS)
    s["rest"] = 0.0
    total = 0.0
    for k in keys:
        ms, j = rows[k]
        ph = dict(j["phases_ms"])
        ph["assembly"] = ph.get("assembly", 0) - ph.get("simplify", 0)
        tot = 0.0
        for p in PARTS:
            v = float(ph.get(p, 0))
            s[p] += v
            tot += v
        s["rest"] += ms - tot
        total += ms
    s["total"] = total
    return s

def serial_fraction(t1, tn, n):
    if t1 <= 0 or n <= 1:
        return float("nan")
    return (tn - t1 / n) / (t1 - t1 / n)

def main(argv):
    flags = [a for a in argv if a.startswith("--")]
    args = [a for a in argv if not a.startswith("--")]
    runs = {}
    for a in args:
        n, files = a.split("=", 1)
        runs[int(n)] = load(files.split(","))
    if "--hashes" in flags:
        base = None; bad = 0
        for n, rows in sorted(runs.items()):
            for k, (ms, j) in rows.items():
                if k not in (base or {}):
                    base = base or {}
                    base[k] = j["hash"]
                elif base[k] != j["hash"]:
                    bad += 1
                    print("HASH DIFFERS", n, k, base[k], j["hash"])
        print("hash drift:", bad, "of", sum(len(r) for r in runs.values()), "rows")
        return 1 if bad else 0
    ns = sorted(runs)
    common = set.intersection(*[set(r) for r in runs.values()])
    keys = sorted(common)
    sums = {n: phase_sums(runs[n], keys) for n in ns}
    n1 = ns[0]
    print(f"{len(keys)} case/operations in common, thread counts {ns} (sums of the best time per case and operation, ms)\n")
    head = "| phase | " + " | ".join(f"{n} thr" for n in ns) + " | " + " | ".join(f"x{n}" for n in ns[1:]) + " | " + " | ".join(f"s({n})" for n in ns[1:]) + " | share at 1 |"
    print(head); print("|" + "---|" * (head.count("|") - 1))
    for p in list(sums[n1].keys()):
        t1 = sums[n1][p]
        cells = [f"{sums[n][p]:.0f}" for n in ns]
        sp = [f"{t1 / sums[n][p]:.2f}" if sums[n][p] > 0 else "-" for n in ns[1:]]
        sf = [f"{serial_fraction(t1, sums[n][p], n / n1):.2f}" if t1 > 0 else "-" for n in ns[1:]]
        print(f"| {p} | " + " | ".join(cells) + " | " + " | ".join(sp) + " | " + " | ".join(sf) + f" | {100 * t1 / sums[n1]['total']:.1f} % |")
    if "--bound" in flags:
        print("\nTotal speedup at n threads if the serial part of one phase were removed (Amdahl, from the measured sums):\n")
        print("| phase | " + " | ".join(f"{n} thr" for n in ns[1:]) + " |"); print("|---|" + "---|" * (len(ns) - 1))
        for p in PARTS + ["rest"]:
            cells = []
            for n in ns[1:]:
                tn = sums[n]["total"]
                # the phase's time at n threads falls to its ideal t1 / n
                ideal = sums[n1][p] / (n / n1)
                gain = max(0.0, sums[n][p] - ideal)
                cells.append(f"{sums[n1]['total'] / (tn - gain):.2f}")
            print(f"| {p} | " + " | ".join(cells) + " |")
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
