#!/usr/bin/env python3
"""(data branch runs/tet-rescue-stop only) What a convergence stop of the rescue loop would cost, from the per-pass traces of full runs.

Usage: analyze_rescue_stop.py <dir> <floor ...>   (<dir> holds f<floor>-*.csv and trace-<floor>-*.csv of run_floor_shard.sh)
A run stopped before pass k is the prefix of the full run up to pass k (the passes are deterministic and do not look ahead), so its result is
the state the trace records at the start of pass k: tets below the floor, worst angle; its time is the clock at that line. A rule stops in the
rescue phase at the first pass i with: worst(i) - worst(i-N) < gain degrees and bad(i-N) - bad(i) < frac * bad(i-N), N passes after the start of the phase.
Per floor and rule: meshes where it stops, time saved (sum and share, over the meshes with improver time above 20 s separately), and the loss of the
result against the full run: worst angle (degrees, as full - stopped) and tets below the floor (as stopped - full, absolute and as a share of the mesh's tets).
"""
import glob
import os
import statistics
import sys
from collections import defaultdict

RULES = [(5, 0.05, 0.02), (10, 0.05, 0.02), (10, 0.05, 0.05), (10, 0.01, 0.01), (20, 0.05, 0.02), (20, 0.01, 0.01)]


def load_traces(d, floor):
    tr = defaultdict(list)
    for f in sorted(glob.glob(os.path.join(d, f"trace-{floor}-*.csv"))):
        for line in open(f, encoding="utf-8"):
            p = line.rstrip("\n").split(",")
            if len(p) == 6:
                tr[p[0]].append((p[1], int(p[2]), int(p[3]), float(p[4]), float(p[5])))
    return tr


def load_tets(d, floor):
    tets = {}
    for f in glob.glob(os.path.join(d, f"f{floor}-*.csv")):
        for line in open(f, encoding="utf-8"):
            p = line.split(",")
            if len(p) > 25 and p[0] != "name":
                tets[p[0]] = int(p[3])
    return tets


def stop_index(rows, N, gain, frac):
    resc = [i for i, r in enumerate(rows) if r[0] == "rescue"]
    for i in resc:
        j = i - N
        if j < 0 or rows[j][0] != "rescue":
            continue
        if rows[i][3] - rows[j][3] < gain and rows[j][2] - rows[i][2] < frac * rows[j][2]:
            return i
    return None


def main():
    d = sys.argv[1]
    for floor in sys.argv[2:]:
        tr = load_traces(d, floor)
        tets = load_tets(d, floor)
        full_t = {n: r[-1][4] for n, r in tr.items()}
        slow = [n for n, t in full_t.items() if t > 20]
        resc = sum(1 for r in tr.values() if any(x[0] == "rescue" for x in r))
        print(f"\n### floor {floor}\n")
        print(f"- meshes with a trace: {len(tr)}; with a rescue phase: {resc}; improver time above 20 s (full run): {len(slow)}; "
              f"sum of improver time {sum(full_t.values()):.0f} s, of those above 20 s {sum(full_t[n] for n in slow):.0f} s")
        print(f"- full runs: median tets below the floor {statistics.median([r[-1][2] for r in tr.values()] or [0]):.0f}; "
              f"median worst angle {statistics.median([r[-1][3] for r in tr.values()] or [0]):.3g}\n")
        print("| rule N / gain / frac | stops in | time saved s (share) | saved on the >20 s meshes | worst angle lost: max / meshes > 0.5 deg | tets below floor added: max / share of tets max / sum | still >20 s after |")
        print("|---|---|---|---|---|---|---|")
        for N, gain, frac in RULES:
            saved = saved_slow = 0.0
            stops = 0
            lost = []
            added = []
            share = []
            still = 0
            for n, rows in tr.items():
                i = stop_index(rows, N, gain, frac)
                end = rows[-1]
                if i is None:
                    t_after = end[4]
                    if t_after > 20:
                        still += 1
                    continue
                stops += 1
                t_stop = rows[i][4]
                saved += end[4] - t_stop
                if end[4] > 20:
                    saved_slow += end[4] - t_stop
                lost.append(rows[i][3] and (end[3] - rows[i][3]))
                da = rows[i][2] - end[2]
                added.append(da)
                if n in tets and tets[n] > 0:
                    share.append(da / tets[n])
                if t_stop > 20:
                    still += 1
            tot = sum(full_t.values()) or 1
            print(f"| {N} / {gain} / {frac} | {stops} | {saved:.0f} ({100 * saved / tot:.1f} %) | {saved_slow:.0f} | "
                  f"{max(lost, default=0):.3g} / {sum(1 for x in lost if x > 0.5)} | {max(added, default=0)} / {max(share, default=0):.2e} / {sum(added)} | {still} |")


main()
