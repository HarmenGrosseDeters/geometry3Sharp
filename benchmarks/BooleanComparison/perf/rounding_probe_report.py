#!/usr/bin/env python3
# Tables of the rounding probe (perf/rounding_probe.patch) from `BooleanComparison profile` rows (phases_ms keys "rounding.<Step>[.in_<Outer>]_us / _n").
#   rounding_probe_report.py table <label=rows.jsonl> <label=rows.jsonl> ...   steps at 1 and 4 threads (first two), speed-up, serial share, extrapolation
# Steps booked "in_<Outer>" were called from inside that outer step; the outer step's own (exclusive) time is its total minus those.
# The 12-thread column is an Amdahl extrapolation of each step from its 1 and 4 thread times (no measurement; the machine has 4 cores).
import gzip, json, sys

def rows(paths):
    """rows of one or more files (comma separated): per boolean the row of the fastest run"""
    best = {}
    for path in paths.split(","):
        for l in (gzip.open(path, "rt") if path.endswith(".gz") else open(path)):
            if l.strip():
                r = json.loads(l)
                if r.get("ok"):
                    k = (r["case"], r["op"])
                    if k not in best or min(r["ms"]) < min(best[k]["ms"]):
                        best[k] = r
    return list(best.values())

def sums(rs):
    s = {}
    for r in rs:
        for k, v in r["phases_ms"].items():
            if k.startswith("rounding."):
                s[k] = s.get(k, 0) + v
        s["_rounding_ms"] = s.get("_rounding_ms", 0) + r["phases_ms"].get("rounding", 0)
        s["_total_ms"] = s.get("_total_ms", 0) + min(r["ms"])
    return s

def ex(s):
    """exclusive time per (step, context) in us; outer steps lose their nested calls"""
    d = {}
    for k, v in s.items():
        if not k.endswith("_us") or not k.startswith("rounding."):
            continue
        name = k[len("rounding."):-3]
        d[name] = v
    for k in list(d):
        if ".in_" in k:
            outer = k.split(".in_")[1]
            if outer in d:
                d[outer] -= d[k]
    return d

def main(args):
    runs = [a.split("=", 1) for a in args]
    (l1, p1), (l4, p4) = runs[0], runs[1]
    r1, r4 = rows(p1), rows(p4)
    s1, s4 = sums(r1), sums(r4)
    e1, e4 = ex(s1), ex(s4)
    n1 = {k[len("rounding."):-2]: v for k, v in s1.items() if k.endswith("_n") and k.startswith("rounding.")}
    apply1, apply4 = e1.get("Apply", 0), e4.get("Apply", 0)
    # Apply exclusive = Apply - (steps at the top level) ; the remainder is untimed work of Apply itself
    top = [k for k in e1 if k not in ("Apply", "count_contacts") and ".in_" not in k]
    # steps at the top level that are themselves nested in another top-level one do not exist: wrappers book by context
    # (the exclusive times of all bins, the nested ones included, add up to Apply)
    allk = [k for k in e1 if k not in ("Apply", "count_contacts")]
    res1 = apply1 - sum(e1[k] for k in allk); res4 = apply4 - sum(e4.get(k, 0) for k in allk)
    print(f"{len(r1)} booleans; rounding phase (profile `rounding`): {s1['_rounding_ms']} ms at {l1}, {s4['_rounding_ms']} ms at {l4}; Apply {apply1/1000:.0f} / {apply4/1000:.0f} ms; boolean total {s1['_total_ms']} / {s4['_total_ms']} ms\n")
    print(f"| step (exclusive) | calls | {l1} ms | {l4} ms | speed-up | share of Apply at {l1} | share at {l4} | parallel fraction p | extrapolated 12 thr ms |\n|---|---|---|---|---|---|---|---|---|")
    def line(name, a, b, calls):
        sp = a / b if b else float("inf")
        p = max(0.0, min(1.0, (a - b) / (a * 0.75))) if a else 0
        t12 = a * (1 - p + p / 12)
        print(f"| {name} | {calls} | {a/1000:.0f} | {b/1000:.0f} | {sp:.2f} | {100*a/apply1:.1f} % | {100*b/apply4:.1f} % | {p:.2f} | {t12/1000:.0f} |")
        return t12
    order = sorted(top + [k for k in e1 if ".in_" in k], key=lambda k: -e1.get(k, 0))
    tot12 = 0
    for k in order:
        tot12 += line(k.replace(".in_", " (in "), e1[k], e4.get(k, 0), n1.get(k, 0)) if False else line(k.replace(".in_", " (in ") + (")" if ".in_" in k else ""), e1[k], e4.get(k, 0), n1.get(k, 0))
    tot12 += line("untimed rest of Apply", res1, res4, "")
    outer = {k: e1[k] for k in e1 if k in ("RepairPasses", "SeparateFalseContacts", "RemoveContactVertices", "PositionSearch", "RemoveTiny", "CheckBounds")}
    print()
    # serial share: steps that did not speed up (parallel fraction below 0.3)
    ser1 = sum(e1[k] for k in e1 if k not in ("Apply", "count_contacts") and (e1[k] - e4.get(k, 0)) / max(e1[k], 1) < 0.2) + max(res1, 0)
    ser4 = sum(e4.get(k, 0) for k in e1 if k not in ("Apply", "count_contacts") and (e1[k] - e4.get(k, 0)) / max(e1[k], 1) < 0.2) + max(res4, 0)
    print(f"Serial steps (speed-up below 1.25x, plus the untimed rest): {ser1/1000:.0f} ms of {apply1/1000:.0f} at {l1} ({100*ser1/apply1:.0f} %), {ser4/1000:.0f} ms of {apply4/1000:.0f} at {l4} ({100*ser4/apply4:.0f} %).")
    if True:
        c1, c4 = s1.get("rounding.count_contacts_us", 0), s4.get("rounding.count_contacts_us", 0)
        print(f"`count_contacts` after Apply (in ExactMeshBoolean): {c1/1000:.0f} ms at {l1}, {c4/1000:.0f} ms at {l4}.")
    print(f"Amdahl extrapolation of the sum to 12 threads: {tot12/1000:.0f} ms (Apply at 1 thread {apply1/1000:.0f} ms): {apply1/tot12:.2f}x")

if __name__ == "__main__":
    main(sys.argv[2:])
