"""Aggregate the RAW lines of `ProceduralSphereUnionProbe scaling` (#1466): medians and spread per family, size and route.

Usage: python aggregate_scaling.py results/scaling_*_raw.txt [key ...]
Without keys it prints the total time, the triangle count and the main stage times; with keys (t.<phase> or c.<count>) it prints their medians.
"""
import statistics, sys, collections

def load(path):
    runs = collections.defaultdict(list)
    info = {}
    for line in open(path, encoding="utf-8"):
        p = line.split()
        if not p:
            continue
        if p[0] == "INFO":
            info[(p[1], int(p[2]))] = " ".join(p[3:])
        elif p[0] == "RAW":
            d = {"ms": int(p[5]), "tris": int(p[6]), "status": p[7]}
            for kv in p[8].split(";") if len(p) > 8 else []:
                k, v = kv.split("=")
                d[k] = int(v)
            runs[(p[1], int(p[2]), p[3])].append(d)
    return runs, info

def med(runs, key):
    v = [r.get(key, 0) for r in runs]
    return statistics.median(v), min(v), max(v)

if __name__ == "__main__":
    runs, info = load(sys.argv[1])
    keys = sys.argv[2:] or ["ms", "tris", "t.arrangement", "t.classification", "t.assembly", "t.rounding"]
    for (fam, n, route), rs in sorted(runs.items()):
        cells = []
        for k in keys:
            m, lo, hi = med(rs, k)
            cells.append(f"{k}={m:g}[{lo:g}-{hi:g}]")
        print(fam, n, route, "reps=%d" % len(rs), " ".join(cells))
