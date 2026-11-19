#!/usr/bin/env python3
"""(data branch runs/tet-work-cap only) The deterministic work cap of the improver (#1154, MaxSplitAttemptsPerVertex) against the same run without it.

Usage: analyze_work_cap.py <dir> <config ...>   (<dir> holds f<config>*.csv and trace-<config>*.csv of run_floor_shard.sh; a config is <floor> or
<floor>x<cap>; the first config of a floor is its baseline, normally <floor>x0, the improver without the cap)
Per config: meshes above 20 s of improver time (a timeout counts as above), the sum of the improver time and the time saved against the baseline, the
loss of the worst angle against the baseline (max and number of meshes above 0.5 degrees), the tets left below the floor (sum, and added against the
baseline: max, sum, max share of the mesh's tets), the meshes where the cap was reached and the timeouts. A baseline row also prints the split
attempts per vertex of its meshes (percentiles, overall and of the meshes above 20 s): the scale of the cap.
The improver time and the result come from the end line of the per-pass trace (<name>,end,<passes>,<tets below floor>,<worst angle>,<seconds>).
"""
import glob
import os
import statistics
import sys
from collections import defaultdict

LIMIT = 20.0


def read_traces(d, cfg):
    end, att = {}, {}
    for f in glob.glob(os.path.join(d, f"trace-{cfg}.csv")) + glob.glob(os.path.join(d, f"trace-{cfg}-*.csv")):
        for line in open(f, encoding="utf-8"):
            p = line.rstrip("\n").split(",")
            if len(p) == 7 and p[1] == "attempts":          # <name>,attempts,<split attempts>,<vertices>,0,0,<seconds>
                att[p[0]] = (int(p[2]), int(p[3]))
            elif len(p) == 6 and p[1] == "end":
                end[p[0]] = (int(p[3]), float(p[4]), float(p[5]))
    return end, att


def read_rows(d, cfg):
    """per mesh: (tets, cap_reached, failure status or None)"""
    rows = {}
    for f in glob.glob(os.path.join(d, f"f{cfg}.csv")) + glob.glob(os.path.join(d, f"f{cfg}-*.csv")):
        for line in open(f, encoding="utf-8"):
            p = line.rstrip("\n").split(",")
            if len(p) == 4 and p[3] in ("TIMEOUT", "MISSING_MESH") or (len(p) == 4 and p[3].startswith("ERROR")):
                rows[p[0]] = (0, False, p[3])
            elif len(p) > 25:
                rows[p[0]] = (int(p[3]), "improve_work_cap_reached=1" in line, None)
    return rows


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(q * len(v)))] if v else 0.0


def main():
    d = sys.argv[1]
    base = {}
    for cfg in sys.argv[2:]:
        floor = cfg.split("x")[0]
        end, att = read_traces(d, cfg)
        rows = read_rows(d, cfg)
        timeouts = sorted(n for n, r in rows.items() if r[2] == "TIMEOUT")
        times = {n: e[2] for n, e in end.items()}
        for n in timeouts:
            times[n] = float("inf")
        over = [n for n, t in times.items() if t > LIMIT]
        if floor not in base:
            base[floor] = (cfg, end, rows, times)
            print(f"\n### floor {floor}: baseline {cfg}\n")
            print(f"- meshes with a result: {len(end)}; improver time above {LIMIT:.0f} s: {len(over)} (of them timeouts: {len(timeouts)}); sum of the improver time {sum(t for t in times.values() if t < float('inf')):.0f} s")
            print(f"- median tets below the floor {statistics.median([e[0] for e in end.values()] or [0]):.0f}; median worst angle {statistics.median([e[1] for e in end.values()] or [0]):.3g}")
            apv = {n: a[0] / max(1, a[1]) for n, a in att.items()}
            slow = [apv[n] for n in over if n in apv]
            print(f"- split attempts per vertex: all meshes p50 {pct(apv.values(), .5):.2f} / p90 {pct(apv.values(), .9):.2f} / max {max(apv.values(), default=0):.2f}; "
                  f"meshes above {LIMIT:.0f} s ({len(slow)}) p10 {pct(slow, .1):.2f} / p50 {pct(slow, .5):.2f} / p90 {pct(slow, .9):.2f}\n")
            print("| config | cap reached | improver time > 20 s | timeouts | improver time sum s | saved s (share) | worst angle lost: max / meshes > 0.5 deg | tets below floor: sum / added sum / added max / added share max |")
            print("|---|---|---|---|---|---|---|---|")
        bcfg, bend, brows, btimes = base[floor]
        lost, added, share = [], [], []
        saved = 0.0
        for n, e in end.items():
            if n in bend:
                lost.append(bend[n][1] - e[1])
                da = e[0] - bend[n][0]
                added.append(da)
                if brows.get(n, (0,))[0] > 0:
                    share.append(da / brows[n][0])
                if btimes[n] < float("inf"):
                    saved += btimes[n] - e[2]
        tsum = sum(t for t in times.values() if t < float("inf"))
        bsum = sum(t for t in btimes.values() if t < float("inf"))
        reached = sum(1 for r in rows.values() if r[1])
        print(f"| {cfg} | {reached} | {len(over)} | {len(timeouts)} | {tsum:.0f} | {saved:.0f} ({100 * saved / max(bsum, 1e-9):.1f} %) | "
              f"{max(lost, default=0):.3g} / {sum(1 for x in lost if x > 0.5)} | {sum(e[0] for e in end.values())} / {sum(added)} / {max(added, default=0)} / {max(share, default=0):.2e} |")


main()
