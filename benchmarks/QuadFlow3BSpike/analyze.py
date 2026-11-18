#!/usr/bin/env python3
"""Summarise a run_gate.sh output: class counts, time ratio vs stage 2 on clean rows, per-stage medians, the floor (what the
ratio would be with flow, subdivision, greedy and SAT free)."""
import re, sys, statistics as st

def parse(path):
    rows = []; cur = None
    for line in open(path):
        line = line.rstrip("\n")
        if line.startswith("|"):
            f = [x.strip() for x in line.strip("|").split("|")]
            f = [x for x in f if x != ""]
            cur = {"name": f[0], "var": f[1], "tot": float(f[-3]), "ratio": float(f[-2]), "cls": f[-1], "t2": float(f[8])}
        elif line.startswith("PROF") and cur is not None:
            def g(k):
                m = re.search(r"\b" + k + r" ([0-9.]+)", line); return float(m.group(1)) if m else 0.0
            for k in ("tpf", "setup", "flowprep", "subdiv", "greedy", "pos", "extract"): cur[k] = g(k)
            m = re.search(r"netbuild\+mcf ([0-9.]+) \(mcf ([0-9.]+)", line); cur["net"] = float(m.group(1)); cur["mcf"] = float(m.group(2))
            m = re.search(r"sat ([0-9.]+) \(att (\d+) solved (\d+) failed (\d+) dec (\d+)", line)
            if m: cur["sat"] = float(m.group(1)); cur["dec"] = int(m.group(5))
            else: cur["sat"] = g("sat"); cur["dec"] = 0
            cur["inv"] = int(re.search(r"inv (\d+)", line).group(1))
            rows.append(cur); cur = None
    return rows

def med(x): return st.median(x) if x else float("nan")

def main(path):
    rows = parse(path)
    clean = [r for r in rows if r["cls"] == "clean"]; fb = [r for r in rows if r["cls"].startswith("FALLBACK")]; bad = [r for r in rows if r["cls"].startswith("UNDETECTED")]
    print(f"{path}: {len(rows)} runs: clean {len(clean)}, fallback {len(fb)}, undetected bad {len(bad)}")
    if clean:
        rat = [r["ratio"] for r in clean]
        print(f"clean ratio vs stage 2: median {med(rat):.1f} max {max(rat):.1f} min {min(rat):.1f}; <=2x: {sum(1 for x in rat if x <= 2)}/{len(clean)}; total s median {med([r['tot'] for r in clean]):.2f} max {max(r['tot'] for r in clean):.2f}")
        floor = [(r["tpf"] + r["setup"] + r["pos"] + r["extract"]) / r["t2"] for r in clean]
        print(f"floor (tpf+setup+pos+extract)/t2 on clean: median {med(floor):.1f} max {max(floor):.1f} min {min(floor):.1f}; <=2x: {sum(1 for x in floor if x <= 2)}/{len(clean)}")
        for k in ("t2", "tpf", "setup", "flowprep", "net", "mcf", "subdiv", "greedy", "sat", "pos", "extract"):
            print(f"  clean median {k:9s} {med([r[k] for r in clean]):.3f}  max {max(r[k] for r in clean):.3f}")
    if fb:
        print(f"fallback: SAT s median {med([r['sat'] for r in fb]):.2f} max {max(r['sat'] for r in fb):.2f}; total s median {med([r['tot'] for r in fb]):.2f} max {max(r['tot'] for r in fb):.2f}")
    allrows = rows
    for k in ("setup", "flowprep", "net", "mcf", "subdiv", "greedy", "sat", "pos", "extract"):
        print(f"  all runs, sum {k:9s} {sum(r[k] for r in allrows):.2f} s")

if __name__ == "__main__": main(sys.argv[1])
