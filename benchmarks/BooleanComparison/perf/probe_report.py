#!/usr/bin/env python3
"""Tables of the serial probe (docs/perf/BOOLEAN_THREAD_SCALING.md): `BooleanComparison profile` rows with a "probe" field
(a build with perf/serial_probe.patch, G3_SERIAL_PROBE=1). Sums over the cases and operations in common, per thread count.

  python3 perf/probe_report.py <threads>=<file.jsonl> ...  [--top N] [--prefix R.|A.|C.|par:]
Columns: wall ms at each thread count (inclusive of nested scopes, wall clock), calls, the speedup against the first count.
`par:<file>:<line>` is a gParallel.ForRange call site (wall time of the loop), labelled with the phase lap that follows it.
"""
import gzip, json, sys, collections

def load(path):
    rows = {}
    for l in (gzip.open(path, "rt") if path.endswith(".gz") else open(path)):
        l = l.strip()
        if not l.startswith("{"):
            continue
        j = json.loads(l)
        if "probe" not in j or "phases_ms" not in j:
            continue
        rows[(j["case"], j["op"])] = j
    return rows

def main(argv):
    top = 60
    prefix = None
    args = []
    i = 0
    while i < len(argv):
        if argv[i] == "--top": top = int(argv[i + 1]); i += 2
        elif argv[i] == "--prefix": prefix = argv[i + 1]; i += 2
        else: args.append(argv[i]); i += 1
    runs = {}
    for a in args:
        n, f = a.split("=", 1)
        runs[int(n)] = load(f)
    ns = sorted(runs)
    keys = sorted(set.intersection(*[set(r) for r in runs.values()]))
    sums = {n: collections.defaultdict(float) for n in ns}
    calls = {n: collections.defaultdict(float) for n in ns}
    phase = {}
    tot = {n: 0.0 for n in ns}
    for n in ns:
        for k in keys:
            j = runs[n][k]
            tot[n] += min(j["ms"]) if isinstance(j["ms"], list) else j["ms"]
            for name, v in j["probe"].items():
                sums[n][name] += v["ms"]
                calls[n][name] += v["n"]
                if v.get("phase"): phase[name] = v["phase"]
    names = sorted(set().union(*[set(s) for s in sums.values()]), key=lambda x: -sums[ns[0]][x])
    print(f"{len(keys)} case/operations, total boolean time at {', '.join(f'{n} thr {tot[n]:.0f} ms' for n in ns)}\n")
    print("| scope | phase | calls | " + " | ".join(f"{n} thr ms" for n in ns) + " | " + " | ".join(f"x{n}" for n in ns[1:]) + " | share of boolean at 1 thr |")
    print("|---|---|---|" + "---|" * (2 * len(ns)))
    shown = 0
    for name in names:
        if prefix and not name.startswith(prefix):
            continue
        t1 = sums[ns[0]][name]
        sp = " | ".join(f"{t1 / sums[n][name]:.2f}" if sums[n][name] > 0 else "-" for n in ns[1:])
        print(f"| {name} | {phase.get(name, '')} | {calls[ns[0]][name]:.0f} | " + " | ".join(f"{sums[n][name]:.0f}" for n in ns) + f" | {sp} | {100 * t1 / tot[ns[0]]:.1f} % |")
        shown += 1
        if shown >= top: break

if __name__ == "__main__":
    main(sys.argv[1:])
