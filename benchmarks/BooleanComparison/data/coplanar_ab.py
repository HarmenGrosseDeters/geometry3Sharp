#!/usr/bin/env python3
"""Summary of the order-randomized A/B of the coplanar pre-decision (method ours_ab of abc_chained.py, `chain-ab` of the benchmark tool;
docs/perf/BOOLEAN_COPLANAR_PREDECISION.md). Input: result rows (jsonl) or the text of a job log with the lines `AB {json}`.
Usage: coplanar_ab.py summary <file>...

Per step: the sum over chains of the median times with the pre-decision on and off (s), their change, the median of the per-chain changes,
the number of chains whose result hashes differ (must be 0)."""
import json, sys, statistics as st, collections

def load(files):
    rows = {}
    for f in files:
        for l in open(f, errors="replace"):
            i = l.find("AB {")
            if i >= 0:
                l = l[i + 3:]
            l = l.strip()
            if not l.startswith("{"):
                continue
            try:
                r = json.loads(l)
            except ValueError:
                continue
            if "on_median_ms" in r:
                rows[(r.get("file", r.get("seed", "")), r["step"])] = r      # the last row of a step wins
    return list(rows.values())

def summary(files):
    rows = load(files)
    by = collections.defaultdict(list)
    for r in rows:
        if r.get("ok", True):
            by[r["step"]].append(r)
    out = ["| step | chains | on s | off s | change (sum) | median chain change | hashes differ |", "|---|---|---|---|---|---|---|"]
    for s in sorted(by):
        l = by[s]
        on = sum(r["on_median_ms"] for r in l); off = sum(r["off_median_ms"] for r in l)
        per = [100.0 * (r["on_median_ms"] / r["off_median_ms"] - 1) for r in l if r["off_median_ms"] > 0]
        bad = sum(1 for r in l if not r.get("hash_equal", True))
        out.append("| %d | %d | %.2f | %.2f | %+.1f %% | %+.1f %% | %d |" % (s, len(l), on / 1000, off / 1000, 100.0 * (on / off - 1), st.median(per), bad))
    return "\n".join(out)

if __name__ == "__main__":
    if len(sys.argv) < 3 or sys.argv[1] != "summary":
        print(__doc__)
        sys.exit(2)
    print(summary(sys.argv[2:]))
