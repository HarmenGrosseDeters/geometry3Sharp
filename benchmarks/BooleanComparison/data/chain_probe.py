#!/usr/bin/env python3
"""Summary of the probe rows (methods ours_probe / ours_probe_expr of abc_chained.py, `chain-probe*` of the benchmark tool; task of
2026-11-19, docs/perf/BOOLEAN_CHAINS_PROBE_2026-11.md). Usage: chain_probe.py summary <results.jsonl>...   (rows of other classes are ignored)

Read the rows as follows. Per step and chain, `off` is the boolean without the probe (the reference time), `p1` the counters and the self-join
time of each operand (wall time of the self-join in the parallel run), `p2s` the serial run with a timestamp pair per pair (CPU times by class; its
total is inflated by the timestamps, so shares of it are taken against p2s's own phase sum), `skip` the self-join of the certified operands skipped
(chain: the left operand of steps >= 2 when the previous result is clean; expression: every source) and `triv` the coplanar pairs that add only
corners and boundary edges adding nothing. Hashes are the result hashes (same_hash_as_off)."""
import json, sys, statistics as st, collections

PH = ["input", "setup", "intersections.trees", "intersections.cull", "intersections.pairs", "intersections.planes", "intersections.intersect",
      "merge", "triangulation", "classification", "simplify", "assembly", "rounding"]
SHORT = {"intersections.trees": "trees", "intersections.cull": "cull", "intersections.pairs": "pairs", "intersections.planes": "planes",
         "intersections.intersect": "intersect"}

def load(files):
    rows = []
    for f in files:
        for l in open(f):
            l = l.strip()
            if l.startswith("{"):
                try:
                    r = json.loads(l)
                except ValueError:
                    continue
                if r.get("class") == "abc-chained-probe" or ("off" in r and "p1" in r):
                    rows.append(r)
    return rows

def key(r):
    return r.get("file") or "%s:%s" % (r.get("family"), r.get("seed"))

def med(v):
    v = [x for x in v if x is not None]
    return st.median(v) if v else float("nan")

def pct(a, b):
    return 100.0 * a / b if b else float("nan")

def self_us(cfg, m):
    return cfg["phases"].get("probe.self_us.m%d" % m, 0)

def tab(rows, expr):
    out = []
    by = collections.defaultdict(list)
    for r in rows:
        if r.get("method") in (None, "ours_probe", "ours_probe_expr") and "off" in r and r["off"].get("ok") and (r.get("expr", False) == expr):
            by[r["step"]].append(r)
    if not by:
        return out
    steps = sorted(by)
    out.append("")
    out.append("#### %s: time split of today's boolean (`off`; median over chains, ms; sum over chains in brackets, s)" % ("ExactMesh expression, one n-ary arrangement per step" if expr else "Chain, each step on the previous rounded result"))
    out.append("")
    out.append("| phase | " + " | ".join("step %d (n=%d)" % (s, len(by[s])) for s in steps) + " |")
    out.append("|---|" + "---|" * len(steps))
    for k in PH + ["TOTAL"]:
        cells = []
        for s in steps:
            if k == "TOTAL":
                v = [r["off"]["median_ms"] for r in by[s]]
            else:
                v = [r["off"]["phases"].get(k, 0) for r in by[s]]
            cells.append("%.0f (%.1f)" % (med(v), sum(v) / 1000.0))
        out.append("| %s | %s |" % (SHORT.get(k, k), " | ".join(cells)))
    out.append("")
    out.append("Share of the sum over chains (%): " + "; ".join(
        "step %d: " % s + ", ".join("%s %.0f" % (SHORT.get(k, k), pct(sum(r["off"]["phases"].get(k, 0) for r in by[s]), sum(r["off"]["median_ms"] for r in by[s]))) for k in PH if k in ("intersections.pairs", "intersections.intersect", "merge", "triangulation", "classification", "rounding", "intersections.planes"))
        for s in steps))

    # step-3 diagnosis from the serial probe run
    out.append("")
    out.append("#### Pair stage and triangulation by class, serial CPU (`p2s`; median over chains of the share of the run's phase sum, %; counts: sum over chains)")
    out.append("")
    classes = ["sep_t", "sep_u", "coplanar", "prefilter_apart", "construct_apart", "meet_point", "meet_segment"]
    out.append("| class | " + " | ".join("step %d n / %% time" % s for s in steps) + " |")
    out.append("|---|" + "---|" * len(steps))
    def tot_us(r):
        return 1000.0 * sum(r["p2s"]["phases"].get(k, 0) for k in PH if k in r["p2s"]["phases"] and not k.startswith("assembly.")) or 1
    for c in classes + ["co:share3 trivial", "co:share2 trivial", "co:share1 trivial", "co:adds (not trivial)", "prep (touched triangles)", "prep trivial", "tri (touched triangles)", "tri trivial"]:
        cells = []
        for s in steps:
            rs = [r for r in by[s] if "p2s" in r and r["p2s"].get("ok")]
            def g(r, name):
                return r["p2s"]["phases"].get("probe." + name, 0)
            if c in classes:
                n = sum(g(r, "i_" + c) for r in rs); sh = [pct(g(r, "i_%s_us" % c), tot_us(r)) for r in rs]
            elif c.startswith("co:share"):
                sh_k = c.split()[0][-1]
                n = sum(g(r, "co_share%s_trivial" % sh_k) for r in rs); sh = [pct(g(r, "co_share%s_trivial_us" % sh_k), tot_us(r)) for r in rs]
            elif c.startswith("co:adds"):
                n = sum(g(r, "co_share%d_adds" % i) for r in rs for i in range(4)); sh = [pct(sum(g(r, "co_share%d_adds_us" % i) for i in range(4)), tot_us(r)) for r in rs]
            else:
                nm = {"prep (touched triangles)": "prep", "prep trivial": "prep_trivial", "tri (touched triangles)": "tri", "tri trivial": "tri_trivial"}[c]
                n = sum(g(r, nm) for r in rs); sh = [pct(g(r, nm + "_us"), tot_us(r)) for r in rs]
            cells.append("%d / %.1f" % (n, med(sh)))
        out.append("| %s | %s |" % (c, " | ".join(cells)))
    # probe overhead
    out.append("")
    out.append("Probe overhead (p2s serial against off, median ratio of the whole boolean): " + ", ".join(
        "step %d: %.2f" % (s, med([r["p2s"]["median_ms"] / r["off"]["median_ms"] for r in by[s] if "p2s" in r and r["p2s"].get("ok") and r["off"]["median_ms"] > 0])) for s in steps))

    # lever 2: self-join of the operands
    out.append("")
    out.append("#### Self-join of the operands (lever 2): wall time in the parallel run `p1`, as a share of the boolean (median over chains, %), and what skipping it gives")
    out.append("")
    out.append("| step | operand | certified | triangles (median) | self-join ms (median) | share of boolean % (median) | share of sums % |")
    out.append("|---|---|---|---|---|---|---|")
    for s in steps:
        rs = [r for r in by[s] if "p1" in r and r["p1"].get("ok")]
        nm = max([r.get("arrangement_meshes", 2) for r in rs] or [2])
        for m in range(nm):
            sel = [r for r in rs if m < r.get("arrangement_meshes", 2)]
            if not sel: continue
            cert = ("yes" if all(r.get("left_certified") for r in sel) else "no") if (m == 0 and not expr) else "no (raw input)" if not expr else "if certified"
            us = [self_us(r["p1"], m) / 1000.0 for r in sel]
            sh = [pct(self_us(r["p1"], m) / 1000.0, r["p1"]["median_ms"]) for r in sel]
            tr = [r["p1"]["phases"].get("probe.self_tris.m%d" % m, 0) for r in sel]
            out.append("| %d | %d | %s | %.0f | %.1f | %.1f | %.1f |" % (s, m, cert, med(tr), med(us), med(sh), pct(sum(us), sum(r["p1"]["median_ms"] for r in sel))))
    out.append("")
    out.append("Skip experiment (`skip` against `p1`, same process, same counters; sum of the median times over chains, s): ")
    out.append("")
    out.append("| step | chains with skip | p1 s | skip s | change % | median per-chain change % | hash equal | skipped operands unclean (hash differs) |")
    out.append("|---|---|---|---|---|---|---|---|")
    for s in steps:
        rs = [r for r in by[s] if "skip" in r and r["skip"].get("ok") and "p1" in r]
        if not rs: continue
        a = sum(r["p1"]["median_ms"] for r in rs); b = sum(r["skip"]["median_ms"] for r in rs)
        per = [100.0 * (r["skip"]["median_ms"] / r["p1"]["median_ms"] - 1) for r in rs if r["p1"]["median_ms"] > 0]
        eq = sum(1 for r in rs if r["skip"]["same_hash_as_off"])
        out.append("| %d | %d | %.1f | %.1f | %+.1f | %+.1f | %d of %d | %d |" % (s, len(rs), a / 1000, b / 1000, 100.0 * (b / a - 1), med(per), eq, len(rs), len(rs) - eq))
    out.append("")
    out.append("Trivial-coplanar experiment (`triv` against `p1`): ")
    out.append("")
    out.append("| step | chains | p1 s | triv s | change % | median per-chain change % | hash equal |")
    out.append("|---|---|---|---|---|---|---|")
    for s in steps:
        rs = [r for r in by[s] if "triv" in r and r["triv"].get("ok") and "p1" in r]
        if not rs: continue
        a = sum(r["p1"]["median_ms"] for r in rs); b = sum(r["triv"]["median_ms"] for r in rs)
        per = [100.0 * (r["triv"]["median_ms"] / r["p1"]["median_ms"] - 1) for r in rs if r["p1"]["median_ms"] > 0]
        eq = sum(1 for r in rs if r["triv"]["same_hash_as_off"])
        out.append("| %d | %d | %.1f | %.1f | %+.1f | %+.1f | %d of %d |" % (s, len(rs), a / 1000, b / 1000, 100.0 * (b / a - 1), med(per), eq, len(rs)))
    # hash agreement of probe-off against probe-on
    bad = [(key(r), r["step"], c) for r in rows for c in ("p1", "p2s", "skip", "triv") if c in r and isinstance(r[c], dict) and r[c].get("ok") and r.get("expr", False) == expr and c in ("p1", "p2s") and not r[c]["same_hash_as_off"]]
    out.append("")
    out.append("Result hash of the probe runs (p1, p2s) differing from `off`: %d" % len(bad) + ("" if not bad else " " + str(bad[:5])))
    return out

def summary(files):
    rows = load(files)
    out = ["### Boolean chains probe (rows: %d; chains: %d)" % (len(rows), len({key(r) for r in rows}))]
    errs = [r for r in rows if "runner_error" in r]
    if errs:
        out.append("")
        out.append("Runner errors: %d (e.g. %s)" % (len(errs), str(errs[0])[:200]))
    out += tab(rows, False)
    out += tab(rows, True)
    return "\n".join(out)

if __name__ == "__main__":
    if len(sys.argv) < 3 or sys.argv[1] != "summary":
        print(__doc__)
        sys.exit(2)
    print(summary(sys.argv[2:]))
