#!/usr/bin/env python3
"""Summaries of a `PddProbe disc <N>` output: verdicts per sampling mode for subsets of the PDD metrics.

    python3 benchmarks/PddProbe/analyze.py benchmarks/PddProbe/results/disc_300.txt

A case is (mode, noise, deformation, shape). eps* of a subset is the smallest eps* of its metrics ('-' counts as 1, i.e.
not separated up to 0.3). Verdict: 'PDD' if the subset's eps* is at most half the baseline's (best of D2 and PCA), 'BASE'
in the other direction, else 'tie'. Cases where both are unseparated ('-') are counted separately as 'neither'.
"""
import sys
from collections import Counter, defaultdict

SUBSETS = {
    "scalable PDD (AMD/colW1, k<=128)": ["AMD8", "colW1_8", "AMD32", "colW1_32", "AMD128", "colW1_128"],
    "exact EMD, k=32": ["EMD32"],
    "full PDD (k=N-1: EMDall, colW1all)": ["EMDall", "colW1all"],
    "all PDD variants": None,
}


def val(s):
    return 1.0 if s == "-" else float(s)


def main(path):
    lines = open(path).read().splitlines()
    header = next(l for l in lines if l.startswith("mode"))
    names = header.split("|")[1].split()
    rows = []
    for l in lines:
        if l.startswith("#") or l.startswith("mode") or "|" not in l:
            continue
        left, mid, _ = l.split("|")
        mode, noise, deform, shape = left.split()
        rows.append((mode, noise, deform, shape, dict(zip(names, map(val, mid.split())))))
    base = ["D2", "PCA"]
    pdd_all = [n for n in names if n not in base]
    print(f"# {path}: {len(rows)} cases, metrics {' '.join(names)}")
    for title, subset in SUBSETS.items():
        subset = [m for m in (subset or pdd_all) if m in names]
        if not subset:
            continue
        per = defaultdict(Counter)
        for mode, noise, deform, shape, v in rows:
            p, b = min(v[m] for m in subset), min(v[m] for m in base)
            key = f"{mode} noise={noise}"
            if p == 1.0 and b == 1.0:
                per[key]["neither"] += 1
            elif p * 2 <= b:
                per[key]["PDD"] += 1
            elif b * 2 <= p:
                per[key]["BASE"] += 1
            else:
                per[key]["tie"] += 1
        tot = Counter()
        print(f"\n## {title}: {', '.join(subset)} vs D2/PCA")
        print(f"{'stratum':22} {'PDD':>4} {'BASE':>5} {'tie':>4} {'neither':>8}")
        for key in sorted(per):
            c = per[key]; tot += c
            print(f"{key:22} {c['PDD']:4} {c['BASE']:5} {c['tie']:4} {c['neither']:8}")
        print(f"{'total':22} {tot['PDD']:4} {tot['BASE']:5} {tot['tie']:4} {tot['neither']:8}")


if __name__ == "__main__":
    main(sys.argv[1])
