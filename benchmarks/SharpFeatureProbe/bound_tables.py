#!/usr/bin/env python3
"""Tables of the upper-bound probe (SharpFeatureProbe bound; docs/research/PROVABLE_SHARP_WRAP_STUDY.md).

usage: bound_tables.py <bound.jsonl>
Distances are in offsets. cov = share of the oracle's feature network within that many offsets of a sharp edge of the
result; fh = the same distance (max / p95); corner = share of the network's corners with a result vertex within 0.25
offsets; prec = share of the result's sharp edges within 0.25 offsets of the network."""
import json, statistics as st, sys

ORDER = ["A", "K", "KC", "KCK", "KO", "KOC", "O"]


def load(path):
    rows = {}
    for l in open(path):
        r = json.loads(l)
        rows[(r["file"], r["variant"])] = r
    return rows


def f(x, p=3):
    if x is None or not isinstance(x, (int, float)) or x != x:
        return "n/a"
    return f"{x:.{p}f}"


def med(rows, files, v, key):
    xs = [rows[(n, v)].get(key) for n in files if (n, v) in rows and rows[(n, v)].get("status") == "ok"]
    xs = [x for x in xs if isinstance(x, (int, float)) and x == x]
    return st.median(xs) if xs else float("nan")


def worst(rows, files, v, key, fn=max):
    xs = [rows[(n, v)].get(key) for n in files if (n, v) in rows and rows[(n, v)].get("status") == "ok"]
    xs = [x for x in xs if isinstance(x, (int, float)) and x == x]
    return fn(xs) if xs else float("nan")


def group(rows, title, files, variants):
    files = [n for n in files if any((n, v) in rows for v in variants)]
    if not files:
        return
    print(f"\n### {title} ({len(files)} cases; medians, worst case in brackets)\n")
    print("| variant | valid (clean, 0 crossings) | triangles | cov 0.1 | cov 0.25 | cov 1 | convex / concave cov 0.25 | fh p95 | fh max | corner | prec | band min / max | time s |")
    print("|---|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---|---:|")
    for v in variants:
        ok = [n for n in files if (n, v) in rows and rows[(n, v)].get("status") == "ok"]
        if not ok:
            continue
        valid = sum(1 for n in ok if rows[(n, v)]["clean"] and rows[(n, v)]["cross_input"] == 0 and rows[(n, v)]["verts_inside"] == 0)
        print(f"| {v} | {valid}/{len(ok)} | {med(rows, ok, v, 'tris'):.0f} | {f(med(rows, ok, v, 'cov01'))} | {f(med(rows, ok, v, 'cov025'))} [{f(worst(rows, ok, v, 'cov025', min))}] | "
              f"{f(med(rows, ok, v, 'cov1'))} | {f(med(rows, ok, v, 'cov025_cvx'))} / {f(med(rows, ok, v, 'cov025_ccv'))} | {f(med(rows, ok, v, 'fh_p95'), 2)} | "
              f"{f(med(rows, ok, v, 'fh_max'), 2)} [{f(worst(rows, ok, v, 'fh_max'), 2)}] | {f(med(rows, ok, v, 'corner025'), 2)} [{f(worst(rows, ok, v, 'corner025', min), 2)}] | "
              f"{f(med(rows, ok, v, 'prec025'), 2)} | {f(worst(rows, ok, v, 'band_min', min), 2)} / {f(worst(rows, ok, v, 'band_max'), 2)} | {f(med(rows, ok, v, 'ms') / 1000, 1)} |")


def per_case(rows, title, files, variants):
    files = [n for n in files if any((n, v) in rows for v in variants)]
    if not files:
        return
    print(f"\n### {title}\n")
    print("| case | " + " | ".join(f"{v} cov 0.25 / fh max / corner" for v in variants) + " | O valid; its band |")
    print("|---|" + "---|" * (len(variants) + 1))
    for n in files:
        cells = []
        for v in variants:
            r = rows.get((n, v))
            if r is None or r.get("status") != "ok":
                cells.append("n/a" if r is None else r.get("status", "?"))
                continue
            bad = "" if (r["clean"] and r["cross_input"] == 0 and r["verts_inside"] == 0) else " (INVALID)"
            cells.append(f"{f(r.get('cov025'))} / {f(r.get('fh_max'), 2)} / {f(r.get('corner025'), 2)}{bad}")
        o = rows.get((n, "O"))
        if o is None or o.get("status") != "ok":
            ov = "n/a"
        else:
            ov = "yes" if o["clean"] and o["cross_input"] == 0 and o["verts_inside"] == 0 else f"no: {o['not_clean'] or '-'}, {o['cross_input']} triangles cross the input"
            ov += f"; band {f(o['band_min'], 2)} - {f(o['band_max'], 2)}"
        print(f"| {n} | " + " | ".join(cells) + f" | {ov} |")


def totals(rows, clean):
    ok = [r for r in rows.values() if r.get("status") == "ok"]
    wrap = [r for r in ok if r["variant"] != "O"]
    valid = sum(1 for r in wrap if r["clean"] and r["cross_input"] == 0 and r["verts_inside"] == 0)
    print("\n### Totals\n")
    print(f"- rows: {len(rows)} ({len(rows) - len(ok)} without a result); wrap-based results (every variant but O): {len(wrap)}, of them closed, manifold, "
          f"free of self-intersections, 0 triangles crossing the input and 0 vertices inside it: {valid}")
    o = [r for r in ok if r["variant"] == "O"]
    print(f"- oracle O valid on {sum(1 for r in o if r['clean'] and r['cross_input'] == 0 and r['verts_inside'] == 0)} of {len(o)} cases")
    print(f"- smallest / largest distance of a wrap-based result's sample point to the input, in offsets: {min(r['band_min'] for r in wrap):.4f} / {max(r['band_max'] for r in wrap):.2f}")
    kc = [r for r in ok if r["variant"] == "KC"]
    print(f"- corner step (KC, raw wrap, before the simplification): {sum(r['corner_moved'] for r in kc)} of {sum(r['net_corners'] for r in kc)} corners moved, {sum(r['corner_tries'] for r in kc)} moves tried")
    k = [r for r in ok if r["variant"] == "K" and r["file"] in clean]
    rej = [r["k_rejected"] / max(1, r["k_rejected"] + r["k_crease_snaps"] + r["k_splits"] + r["k_flips"]) for r in k]
    print(f"- pass K on the {len(k)} clean cases: rejected / (rejected + crease snaps + splits + flips), median {st.median(rej):.2f}")
    for v in ("K", "KC"):
        xs = [rows[(n, v)]["cov025"] for n in clean if (n, v) in rows]
        print(f"- {v}, cov 0.25 over all {len(xs)} clean cases: median {st.median(xs):.4f}, min {min(xs):.3f}")


def main():
    rows = load(sys.argv[1])
    names = []
    for (n, v) in rows:
        if n not in names:
            names.append(n)
    sweep = [n for n in names if n.split(":")[0] in ("wedge", "ridge", "notch")]
    dirty = [n for n in names if rows[(n, next(v for v in ORDER if (n, v) in rows))].get("dirty")]
    clean = [n for n in names if n not in sweep and n not in dirty]
    variants = [v for v in ORDER if any((n, v) in rows for n in names)]
    totals(rows, clean)
    group(rows, "Clean CAD-like solids, at the origin and axis-aligned", [n for n in clean if "@" not in n], variants)
    group(rows, "Clean CAD-like solids, rotated", [n for n in clean if n.endswith("@rot")], variants)
    group(rows, "Clean CAD-like solids, far from the origin", [n for n in clean if n.endswith("@far")], variants)
    group(rows, "Clean CAD-like solids, rotated and far", [n for n in clean if n.endswith("@rotfar")], variants)
    group(rows, "Dirty inputs", dirty, variants)
    per_case(rows, "Clean solids, per case", clean, [v for v in ["A", "K", "KC"] if v in variants])
    per_case(rows, "Dirty inputs, per case", dirty, [v for v in ["A", "K", "KC", "KO", "KOC"] if v in variants])
    per_case(rows, "Sweeps (crease angle; feature size in alphas), per case", sweep, [v for v in ["A", "K", "KC"] if v in variants])


if __name__ == "__main__":
    main()
