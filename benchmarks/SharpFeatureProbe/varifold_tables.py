#!/usr/bin/env python3
"""Tables of the varifold study (docs/research/VARIFOLD_WRAP_QUALITY_2026-11.md) from the rows of
`SharpFeatureProbe varifold`.

    python3 benchmarks/SharpFeatureProbe/varifold_tables.py rows.jsonl [more.jsonl ...]
"""
import json
import math
import sys

KERNELS = ["mass", "current", "varifold", "oriented2", "oriented8"]
CLASSIC = ["haus", "chamfer", "normal_deg", "area_ratio"]


def load(paths):
    rows = []
    for p in paths:
        with open(p) as f:
            for line in f:
                line = line.strip()
                if line.endswith("}"):
                    rows.append(json.loads(line))
    return [r for r in rows if "sigma" in r]


def fmt(v, digits=3):
    if v is None or (isinstance(v, float) and math.isnan(v)):
        return "-"
    if isinstance(v, float):
        if v == 0:
            return "0"
        if abs(v) < 1e-3 or abs(v) >= 1e4:
            return "%.2e" % v
        return ("%." + str(digits) + "g") % v
    return str(v)


def table(header, lines):
    print("| " + " | ".join(header) + " |")
    print("|" + "|".join(["---"] + ["---:"] * (len(header) - 1)) + "|")
    for line in lines:
        print("| " + " | ".join(line) + " |")
    print()


def median(xs):
    xs = sorted(xs)
    n = len(xs)
    return float("nan") if n == 0 else (xs[n // 2] if n % 2 else 0.5 * (xs[n // 2 - 1] + xs[n // 2]))


def spearman(xs, ys):
    def rank(v):
        order = sorted(range(len(v)), key=lambda i: v[i])
        r = [0.0] * len(v)
        i = 0
        while i < len(order):
            j = i
            while j + 1 < len(order) and v[order[j + 1]] == v[order[i]]:
                j += 1
            for k in range(i, j + 1):
                r[order[k]] = 0.5 * (i + j)
            i = j + 1
        return r
    if len(xs) < 3:
        return float("nan")
    rx, ry = rank(xs), rank(ys)
    mx, my = sum(rx) / len(rx), sum(ry) / len(ry)
    num = sum((a - mx) * (b - my) for a, b in zip(rx, ry))
    den = math.sqrt(sum((a - mx) ** 2 for a in rx) * sum((b - my) ** 2 for b in ry))
    return num / den if den > 0 else float("nan")


def damage(rows):
    items = sorted({r["file"] for r in rows if r["ref"] == "P"})
    for item in items:
        sel = [r for r in rows if r["file"] == item]
        sigmas = sorted({r["sigma"] for r in sel})
        print("### %s\n" % item)
        variants = []
        for r in sel:
            if r["variant"] not in variants:
                variants.append(r["variant"])
        header = ["variant", "Hausdorff", "Chamfer", "normal deg", "area ratio - 1"]
        for s in sigmas:
            header += ["%s @%g" % (k, s) for k in ("mass", "current", "varifold", "oriented8")]
        lines = []
        for v in variants:
            first = next(r for r in sel if r["variant"] == v)
            line = [v, fmt(first["haus"]), fmt(first["chamfer"]), fmt(first["normal_deg"]), fmt(first["area_ratio"] - 1)]
            for s in sigmas:
                r = next((r for r in sel if r["variant"] == v and r["sigma"] == s), None)
                line += [fmt(r["rel_" + k]) if r else "-" for k in ("mass", "current", "varifold", "oriented8")]
            lines.append(line)
        table(header, lines)


def cases(rows):
    sel = [r for r in rows if r["ref"] in ("O", "I") and not r["file"].startswith("pose:")]
    if not sel:
        return
    files = []
    for r in sel:
        if r["file"] not in files:
            files.append(r["file"])
    sigmas = sorted({r["sigma"] for r in sel})

    def get(f, v, ref, s):
        return next((r for r in sel if r["file"] == f and r["variant"] == v and r["ref"] == ref and r["sigma"] == s), None)

    for ref in ("O", "I"):
        for s in sigmas:
            print("### reference %s, sigma = %g offsets: per case (F = quadrature floor, A = plain, K = feature pass)\n" % (ref, s))
            header = ["case", "cov 0.25 A", "cov 0.25 K", "o8 F", "o8 A", "o8 K", "A/K", "varifold A/K", "mass A/K", "current A/K",
                      "Hausdorff A", "K", "Chamfer A/K", "normal deg A", "K", "area ratio A", "K"]
            lines = []
            for f in files:
                a, k, fl = get(f, "A", ref, s), get(f, "K", ref, s), get(f, "F", ref, s)
                if not a or not k:
                    continue

                def ratio(key):
                    return fmt(a[key] / k[key]) if k[key] > 0 else "-"
                lines.append([f, fmt(a.get("cov025")), fmt(k.get("cov025")), fmt(fl["rel_oriented8"]) if fl else "-",
                              fmt(a["rel_oriented8"]), fmt(k["rel_oriented8"]), ratio("rel_oriented8"), ratio("rel_varifold"), ratio("rel_mass"),
                              ratio("rel_current"), fmt(a["haus"]), fmt(k["haus"]), ratio("chamfer"), fmt(a["normal_deg"]), fmt(k["normal_deg"]),
                              fmt(a["area_ratio"], 5), fmt(k["area_ratio"], 5)])
            table(header, lines)

    # summary: separation of A from K (paired ratio), and rank correlation with the crease coverage over all results
    print("### summary: the plain wrap against the feature pass (paired, clean cases at the origin and posed)\n")
    header = ["measure", "reference", "sigma", "median A/K", "min A/K", "cases with A > K", "Spearman with 1 - cov 0.25 (R, A, K)"]
    lines = []
    for ref in ("O", "I"):
        keys = [("rel_" + k, s) for s in sigmas for k in KERNELS] + [(c, None) for c in CLASSIC]
        for key, s in keys:
            s0 = s if s is not None else sigmas[0]
            ratios, xs, ys = [], [], []
            for f in files:
                a, k = get(f, "A", ref, s0), get(f, "K", ref, s0)
                if not a or not k:
                    continue

                def val(r):
                    return abs(r[key] - 1) if key == "area_ratio" else r[key]
                if val(k) > 0:
                    ratios.append(val(a) / val(k))
                for v in ("R", "A", "K"):
                    r = get(f, v, ref, s0)
                    if r and "cov025" in r:
                        xs.append(1 - r["cov025"])
                        ys.append(val(r))
            if not ratios:
                continue
            name = key[4:] if key.startswith("rel_") else ("|area ratio - 1|" if key == "area_ratio" else key)
            lines.append([name, ref, fmt(s) if s is not None else "-", fmt(median(ratios)), fmt(min(ratios)),
                          "%d / %d" % (sum(1 for x in ratios if x > 1), len(ratios)), fmt(spearman(xs, ys))])
    table(header, lines)

    print("### cost (ms, one process, 4 cores): the three inner products of one pair, with the quadrature\n")
    header = ["sigma", "median points (result / reference)", "median ms", "max ms", "median pipeline ms (A)"]
    lines = []
    for s in sigmas:
        rs = [r for r in sel if r["sigma"] == s and r["variant"] in ("A", "K")]
        if not rs:
            continue
        pa = [r["pipeline_ms"] for r in sel if r["sigma"] == s and r["variant"] == "A" and r["ref"] == "O"]
        lines.append([fmt(s), "%d / %d" % (median([r["points"] for r in rs]), median([r["ref_points"] for r in rs])),
                      fmt(median([r["varifold_ms"] for r in rs])), fmt(max(r["varifold_ms"] for r in rs)), fmt(median(pa))])
    table(header, lines)


def pose(rows):
    sel = [r for r in rows if r["file"].startswith("pose:")]
    if not sel:
        return
    print("### the metric under similarities of both meshes\n")
    sigmas = sorted({r["sigma"] for r in sel})
    header = ["pose"] + ["%s @%g" % (k, s) for s in sigmas for k in ("varifold", "oriented8")] + ["relative change of oriented8 @%g" % sigmas[0]]
    base = next(r for r in sel if r["variant"] == "identity" and r["sigma"] == sigmas[0])
    lines = []
    seen = []
    for r in sel:
        if r["variant"] in seen:
            continue
        seen.append(r["variant"])
        line = [r["variant"]]
        for s in sigmas:
            x = next(q for q in sel if q["variant"] == r["variant"] and q["sigma"] == s)
            line += ["%.9e" % x["rel_varifold"], "%.9e" % x["rel_oriented8"]]
        x = next(q for q in sel if q["variant"] == r["variant"] and q["sigma"] == sigmas[0])
        line.append(fmt(abs(x["rel_oriented8"] / base["rel_oriented8"] - 1)))
        lines.append(line)
    table(header, lines)


def main():
    rows = load(sys.argv[1:])
    damage(rows)
    cases(rows)
    pose(rows)


if __name__ == "__main__":
    main()
