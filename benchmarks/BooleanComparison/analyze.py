# Summarises compare.py results per data class as Markdown tables: outcome per case and method, the outcomes on
# the cases that every method accepts, and the time on the cases that every method solves correctly.
# Usage: python3 analyze.py results.jsonl [more.jsonl ...] [--cases CLASS,...] [--methods ours,...]
# Outcome of a case for a method (all its operations; the first that applies):
#   rejected    the tool refuses the input (CGAL: not closed / self-intersecting; Manifold: not manifold)
#   crash       abnormal termination (signal, exception, exit code without a report), or no result written
#   timeout     the time limit was exceeded
#   failed      the tool reports a failure on an input it accepts (CGAL: "output not manifold", exceptions)
#   not closed  a result is not closed in the winding sense (some edge not used equally often in both directions)
#   volume      closed, but a volume identity or an analytically known volume is off by more than 1e-6 (relative)
#   self-int.   correct, but a result has self-intersections (after rounding to double, or touching parts)
#   non-manif.  correct, no self-intersections, but non-manifold edges
#   clean       correct, closed manifold, no self-intersections
import argparse, collections, json, statistics as st

OPS = ["union", "intersection", "difference"]
OUTCOMES = ["clean", "non-manif.", "self-int.", "volume", "not closed", "failed", "timeout", "crash", "rejected"]
CORRECT = {"clean", "non-manif.", "self-int."}
NAMES = {"ours": "ExactMeshBoolean", "cgal_epeck": "CGAL EPECK", "cgal_epick": "CGAL EPICK", "cherchi": "Cherchi et al.", "manifold": "Manifold"}
TOL = 1e-6

def status(r, op):
    s = r.get(f"{op}_status")
    if s:
        return s
    # results of older versions of compare.py
    if r.get(f"{op}_ok"):
        return "ok"
    e = str(r.get(f"{op}_error") or "")
    return "timeout" if e == "timeout" else ("rejected" if "input" in e or "read failed" in e else "crash")

def volume_errors(r):
    return [r[k] for k in ("ie_error", "diff_error", "expected_error") if r.get(k) is not None]

def outcome(r):
    ops = r.get("ops", OPS)
    sts = [status(r, op) for op in ops]
    for s in ("rejected", "crash", "timeout", "failed"):
        if s in sts:
            return s
    if not r.get("all_results"):
        return "crash"
    if not all(r.get(f"{op}_wclosed") for op in ops):
        return "not closed"
    if any(e > TOL for e in volume_errors(r)):
        return "volume"
    if any(r.get(f"{op}_selfint", 0) != 0 for op in ops):
        return "self-int."
    if not all(r.get(f"{op}_manifold_closed") for op in ops):
        return "non-manif."
    return "clean"

def ms(r):
    return sum(r.get(f"{op}_ms", 0) or 0 for op in r.get("ops", OPS))

def valid_input(i):   # closed, oriented, no self-intersections: usable by every method
    return bool(i.get("wclosed") and i.get("selfint") == 0 and i.get("closed"))

def table(header, rows):
    out = ["| " + " | ".join(header) + " |", "|" + "---|" * len(header)]
    out += ["| " + " | ".join(str(c) for c in row) + " |" for row in rows]
    return "\n".join(out)

def outcome_table(cases, methods):
    counts = {m: collections.Counter(outcome(c[m]) for c in cases if m in c) for m in methods}
    cols = [o for o in OUTCOMES if any(counts[m][o] for m in methods)]
    return table(["method"] + cols, [[NAMES.get(m, m)] + [counts[m][o] or "–" for o in cols] for m in methods])

def time_table(cases, methods):
    common = [c for c in cases if all(m in c and outcome(c[m]) in CORRECT for m in methods)]
    if not common:
        return "", 0
    rows = []
    for m in methods:
        t = [ms(c[m]) for c in common]
        rel = [ms(c[m]) / max(ms(c["ours"]), 1) for c in common] if "ours" in methods else []
        rows.append([NAMES.get(m, m), f"{st.median(t):.0f}", f"{sum(t) / 1000:.1f}", f"{st.median(rel):.2f}" if rel else "–"])
    return table(["method", "median ms", "total s", "median time relative to ours"], rows), len(common)

SHORT = {"clean": "clean", "non-manif.": "non-manif.", "self-int.": "self-int.", "volume": "**volume**", "not closed": "**not closed**",
         "failed": "failed", "timeout": "timeout", "crash": "**crash**", "rejected": "rejected"}

def case_table(cases, methods):
    rows = []
    for name, c in sorted(cases.items()):
        cells = []
        for m in methods:
            if m not in c:
                cells.append("")
                continue
            o = outcome(c[m])
            cell = SHORT[o]
            if o == "failed" and c[m].get("ops") != ["final"]:   # which operations failed
                cell += " (" + ", ".join(op[:5] for op in c[m].get("ops", OPS) if status(c[m], op) == "failed") + ")"
            if c[m].get("ops") == ["final"]:   # sequences: time, or how far it got
                if o in CORRECT:
                    cell += f" {ms(c[m]) / 1000:.1f} s"
                elif c[m].get("final_done") is not None:
                    cell += f" after {c[m]['final_done']} of {c[m]['input'].get('booleans', '?')}"
            cells.append(cell)
        rows.append([name] + cells)
    return table(["case"] + [NAMES.get(m, m) for m in methods], rows)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("results", nargs="+")
    ap.add_argument("--cases", default="synthetic,sequence", help="classes with a table per case")
    ap.add_argument("--methods")
    args = ap.parse_args()
    rows = [json.loads(l) for f in args.results for l in open(f) if l.strip()]
    errors = [r for r in rows if r.get("method") == "export"]
    rows = [r for r in rows if r.get("method") != "export"]
    methods = args.methods.split(",") if args.methods else list(dict.fromkeys(r["method"] for r in rows))
    by_class = collections.defaultdict(lambda: collections.defaultdict(dict))
    for r in rows:
        by_class[r.get("class", "thingi10k")][r["file"]][r["method"]] = r
    for cls, by_case in by_class.items():
        cases = list(by_case.values())
        valid = [c for c in cases if valid_input(next(iter(c.values()))["input"])]
        print(f"## {cls}: {len(cases)} cases, {len(valid)} of them with closed, oriented inputs without self-intersections\n")
        print(outcome_table(cases, methods) + "\n")
        accepted = [c for c in cases if all(m in c and outcome(c[m]) != "rejected" for m in methods)]
        if len(accepted) != len(cases):
            print(f"Cases that every method accepts ({len(accepted)}):\n")
            print(outcome_table(accepted, methods) + "\n")
        t, n = time_table(cases, methods)
        if t:
            print(f"Time (all operations, boolean only) on the {n} cases that every method solves correctly:\n")
            print(t + "\n")
        if cls in args.cases.split(","):
            print(case_table(by_case, methods) + "\n")
    if errors:
        print(f"{len(errors)} cases could not be prepared: " + ", ".join(f"{r['file']} ({r['error'][:60]})" for r in errors))

if __name__ == "__main__":
    main()
