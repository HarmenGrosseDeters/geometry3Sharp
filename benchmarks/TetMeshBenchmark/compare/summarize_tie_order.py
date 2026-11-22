#!/usr/bin/env python3
"""Summary of the tie-order measurement (docs/research/TET_TIE_ORDER_2026-11.md; run_tie_order.sh, TieOrder.cs).

Usage: summarize_tie_order.py <tie-order.csv> <tie-order-trace.csv>

Per input (mesh, floor): order 0 is today's order, orders 1 .. K are the relabelings. Effort is in visits. Per order:
  total     visits until the improver stops
  rescue    visits of the rescue phase (total - visits at its start; 0 if it did not run)
  own       visits at the first trace sample with at most the order's own final count of tets below the floor
            ("visits to reach the final quality"; resolution: one sample every vertices / 32 visits)
  common    visits at the first trace sample with at most L tets below the floor, L = the largest final count over all orders
            (the level every order reaches: the same goal for every order)
  below     tets below the floor at the end;  worst = the worst smallest dihedral angle at the end
Printed per input: order 0, then minimum / median / maximum over orders 1 .. K, and minimum / median of the visit columns.
Stop rule of the brief: the minimum is above 0.5 of the median on every input (every visit column), and no order lifts an
input that misses its floor under order 0 (the brief's two floor-10 meshes) to the floor.
"""
import csv
import statistics
import sys
from collections import defaultdict

RULE = 0.5


def main():
    rows = defaultdict(dict)
    with open(sys.argv[1], encoding="utf-8") as f:
        for r in csv.DictReader(f):
            if r["status"] in ("MISSING_MESH", "ERROR") or not r.get("visits_total"):
                print(f"no result: {r['name']} floor {r['floor']}: {r['status']}")
                continue
            if int(r["order"]) in rows[(r["name"], r["floor"])]:
                raise SystemExit(f"duplicate row: {r['name']} floor {r['floor']} order {r['order']}")
            rows[(r["name"], r["floor"])][int(r["order"])] = r
    trace = defaultdict(list)
    with open(sys.argv[2], encoding="utf-8") as f:
        for p in csv.reader(f):
            trace[(p[0], p[1], int(p[2]))].append((int(p[3]), int(p[4]), float(p[5])))

    def first(key, order, level):
        for v, b, _ in trace[key + (order,)]:
            if b <= level:
                return v
        raise SystemExit(f"trace of {key} order {order} never reaches {level}")

    fmt = lambda x: (f"{x:.0f}" if x == int(x) and x > 180 else f"{x:.3f}") if isinstance(x, float) else str(x)
    stop, incomplete = True, False
    for key in sorted(rows, key=lambda k: (float(k[1]), k[0])):
        o = rows[key]
        ks = sorted(k for k in o if k > 0)
        if 0 not in o or not ks:
            print(f"incomplete (not judged): {key}")
            incomplete = True
            continue
        if any(r["valid"] != "yes" for r in o.values()):
            raise SystemExit(f"{key}: a result is not valid")
        level = max(int(r["below_floor"]) for r in o.values())
        m = {}
        for k, r in o.items():
            total, at = int(r["visits_total"]), int(r["visits_at_rescue"])
            m[k] = {"total": total, "rescue": total - at if at >= 0 else 0, "own": first(key, k, int(r["below_floor"])),
                    "common": first(key, k, level), "below": int(r["below_floor"]), "worst": float(r["worst_dihedral"])}
        r0 = o[0]
        floor = float(key[1])
        print(f"\n### {key[0]}, floor {key[1]}: {r0['vertices_in']} vertices, {r0['tets_in']} tets, {r0['below_floor_in']} below the floor after stage 1; "
              f"{len(ks)} orders; common level {level} tets below the floor\n")
        print("| | total | rescue | own | common | below | worst |")
        print("|---|---|---|---|---|---|---|")
        cols = ["total", "rescue", "own", "common", "below", "worst"]
        print("| order 0 (today) | " + " | ".join(fmt(m[0][c]) for c in cols) + " |")
        for name, fn in (("minimum", min), ("median", statistics.median), ("maximum", max)):
            print(f"| {name} of {len(ks)} | " + " | ".join(fmt(fn([m[k][c] for k in ks])) for c in cols) + " |")
        # a visit column counts for the rule only if its median is at least 1 % of the median total (a rescue phase of a few
        # visits has no ratio worth the name)
        ratios = []
        small = 0.01 * statistics.median([m[k]["total"] for k in ks])
        ratio = lambda c: (lambda v: min(v) / statistics.median(v) if statistics.median(v) >= small and statistics.median(v) > 0 else None)([m[k][c] for k in ks])
        for c in ("total", "rescue", "own", "common"):
            if ratio(c) is not None:
                ratios.append(ratio(c))
        cell = lambda c: "-" if ratio(c) is None else f"{ratio(c):.2f}"
        print("| minimum / median | " + " | ".join(cell(c) for c in ("total", "rescue", "own", "common")) + " | | |")
        best0 = min(m[k]["common"] for k in ks) / m[0]["common"] if m[0]["common"] > 0 else float("nan")
        lifted = [k for k in o if m[k]["worst"] >= floor]
        print(f"\n- the fastest order reaches the common level in {best0:.2f} of order 0's visits; orders (0 included) that reach the floor: {len(lifted)} of {len(o)}")
        fast = not all(x > RULE for x in ratios)
        lift = m[0]["worst"] < floor and any(k > 0 for k in lifted)       # today's order misses the floor and a relabeling reaches it
        why = ", ".join(w for w, on in (("an order is much faster", fast), ("an order lifts it to the floor", lift)) if on)
        print(f"- stop rule on this input: {'DOES NOT HOLD: ' + why if fast or lift else 'holds'} (lowest minimum / median {f'{min(ratios):.2f}' if ratios else '-'})")
        stop = stop and not fast and not lift
    print(f"\nStop rule over all inputs{' with a result (some are incomplete)' if incomplete else ''}: "
          f"{'holds: no order is much faster, and none lifts an input that misses its floor today' if stop else 'DOES NOT HOLD'}")


if __name__ == "__main__":
    main()
