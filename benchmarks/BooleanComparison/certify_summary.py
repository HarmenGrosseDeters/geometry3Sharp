#!/usr/bin/env python3
"""Summary of the certify runs (perf/certify_cost.txt, perf/certify_scale.txt): the checker's parts as multiples of the boolean.

Usage: python3 certify_summary.py perf/certify_cost.txt [perf/certify_scale.txt ...]
"""
import sys


def rows(path):
    with open(path) as f:
        lines = [l.rstrip("\n") for l in f if l.strip() and not l.startswith("#")]
    head = [h.strip() for h in lines[0].split("|")]
    for l in lines[1:]:
        cells = [c.strip() for c in l.split("|")]
        yield dict(zip(head, cells))


def num(s):
    try:
        return float(s)
    except ValueError:
        return None


def main(paths):
    print("case | pieces | boolean ms | C2a filtered total ms (x boolean) | C2a float floor total (x) | C2a BigInteger total (x) | stages 1-2 as built ms (x) | NotClean(R) (x)")
    for path in paths:
        for r in rows(path):
            b = num(r["boolean"])
            witness = num(r["points"]) + num(r["approx"]) + num(r["broad"])
            filt = witness + num(r["C2a filtered"])
            floor = witness + num(r["C2a float floor"])
            big = num(r["points"]) + num(r["broad"]) + num(r["C2a BigInteger"])
            s12 = [num(r.get(k, "")) for k in ("C0", "C1", "C3+C4", "C5")]
            s12 = sum(s12) if all(x is not None for x in s12) else None
            clean = num(r["NotClean(R)"])

            def x(v):
                return "n/a" if v is None else "%.0f (%.1f)" % (v, v / b) if v >= 10 else "%.1f (%.1f)" % (v, v / b)
            print("%s | %s | %.1f | %s | %s | %s | %s | %s" % (r["case"], r["pieces"], b, x(filt), x(floor), x(big), x(s12), x(clean)))


if __name__ == "__main__":
    main(sys.argv[1:])
