#!/usr/bin/env python3
# UVAtlasGenerator vs xatlas on the same meshes (compare.py), both evaluated by the same code (UVAtlasBenchmark evaluate).
#   python3 analyze.py --compare --ours results/ours.jsonl --xatlas results/xatlas.jsonl [--selection data/selection.csv]
# Prints markdown: the table per category (ours / xatlas), the Pareto view over (seam length, mean conformal, mean area
# distortion, coverage) for the meshes both tools make valid, and the meshes where one tool overlaps or fails.
#
# valid: an atlas (status ok) with no overlapping or flipped UV triangle, no degenerate UV triangle and every chart a disc.
# Failed: no atlas written or the atlas does not match the input (status other than ok). DistortionLimitExceeded of
# UVAtlasGenerator is an atlas, so it is valid here (the limits are its own options, xatlas has none).
# Medians are over the atlases of each tool (ok status); time over every mesh a tool ran on.
import argparse, csv, json, os, statistics

DIRECTIONS = [("seam_rel", -1), ("mean_conformal", -1), ("mean_area", -1), ("coverage", +1)]
CATEGORIES = ["clean", "open", "multi", "broken", "large"]


def load(path):
    if not path:
        return []
    return [json.loads(line) for line in open(path) if line.strip()]


def is_valid(r):
    return (r.get("status") == "ok" and r.get("overlap_pairs", 1) == 0 and r.get("inverted", 1) == 0
            and r.get("degenerate_uv", 0) == 0 and r.get("discs") == r.get("components"))


def is_failed(r):
    return r.get("status") != "ok" or r.get("producer_status") not in (None, "Success", "DistortionLimitExceeded", "ok")


def med(xs):
    xs = [x for x in xs if x is not None and x == x]
    return statistics.median(xs) if xs else float("nan")


def fmt(x, nd=3):
    return "-" if x != x else ("%." + str(nd) + "f") % x


def dominates(a, b):
    # a is at least as good as b on every metric and better on one (ties: neither)
    better = False
    for key, sign in DIRECTIONS:
        va, vb = sign * a[key], sign * b[key]
        if va < vb:
            return False
        if va > vb:
            better = True
    return better


def summary(rows):
    ok = [r for r in rows if r.get("status") == "ok"]
    return {
        "meshes": len(rows),
        "valid": sum(1 for r in rows if is_valid(r)),
        "failed (no atlas / mismatch)": sum(1 for r in rows if is_failed(r)),
        "overlaps or flipped UVs": sum(1 for r in ok if r.get("overlap_pairs", 0) > 0 or r.get("inverted", 0) > 0),
        "charts not discs": sum(1 for r in ok if r.get("discs") != r.get("components")),
        "time median (s)": fmt(med([r["ms"] / 1000 for r in rows if r.get("ms") is not None]), 2),
        "time p90 (s)": fmt(percentile([r["ms"] / 1000 for r in rows if r.get("ms") is not None], 0.9), 2),
        "charts median": fmt(med([r.get("charts") for r in ok]), 0),
        "coverage median": fmt(med([r.get("coverage") for r in ok]), 3),
        "seam length / sqrt(area) median": fmt(med([r.get("seam_rel") for r in ok]), 2),
        "mean conformal median": fmt(med([r.get("mean_conformal") for r in ok]), 3),
        "mean area distortion median": fmt(med([r.get("mean_area") for r in ok]), 3),
        "texel density log-std median": fmt(med([r.get("density_logstd") for r in ok]), 3),
        "min gap (px, 1024) median": fmt(med([r.get("min_gap_px") for r in ok]), 1),
    }


def percentile(xs, q):
    if not xs:
        return float("nan")
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(q * len(xs)))]


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--ours", required=True)
    ap.add_argument("--xatlas", required=True)
    ap.add_argument("--selection", default=None)
    args = ap.parse_args(argv)
    ours = {r["file"]: r for r in load(args.ours)}
    xa = {r["file"]: r for r in load(args.xatlas)}
    cat = {}
    if args.selection and os.path.exists(args.selection):
        for r in csv.DictReader(open(args.selection)):
            cat[os.path.basename(r["path"])] = r["category"]
    files = sorted(set(ours) | set(xa))
    for f in files:
        c = (ours.get(f) or xa.get(f) or {}).get("category") or cat.get(f, "?")
        for d in (ours, xa):
            if f in d:
                d[f]["category"] = c

    cats = [c for c in CATEGORIES if any(r.get("category") == c for r in list(ours.values()) + list(xa.values()))]
    cats.append("all")
    print("# UVAtlasGenerator vs xatlas (%d meshes)\n" % len(files))
    print("Each cell: UVAtlasGenerator / xatlas. Metrics: `UVAtlasBenchmark evaluate` for both.\n")
    print("| | " + " | ".join(cats) + " |")
    print("|---" * (len(cats) + 1) + "|")

    def rows_of(d, c):
        return [r for r in d.values() if c == "all" or r.get("category") == c]
    sums = {c: (summary(rows_of(ours, c)), summary(rows_of(xa, c))) for c in cats}
    for key in sums["all"][0]:
        print("| " + key + " | " + " | ".join("%s / %s" % (sums[c][0][key], sums[c][1][key]) for c in cats) + " |")

    # Pareto over the meshes both tools make valid
    print("\n## Pareto view (seam length, mean conformal, mean area distortion, coverage)\n")
    print("Both valid: %d meshes. Dominance is strict: at least as good on all four, better on one. Ties on all four: equal."
          % sum(1 for f in files if f in ours and f in xa and is_valid(ours[f]) and is_valid(xa[f])))
    print("\n| | " + " | ".join(cats) + " |")
    print("|---" * (len(cats) + 1) + "|")
    pareto = {}
    for c in cats:
        both = [f for f in files if f in ours and f in xa and (c == "all" or ours[f].get("category") == c)
                and is_valid(ours[f]) and is_valid(xa[f])]
        w = {"ours dominates": 0, "xatlas dominates": 0, "equal": 0, "incomparable": 0}
        for f in both:
            a, b = ours[f], xa[f]
            if dominates(a, b):
                w["ours dominates"] += 1
            elif dominates(b, a):
                w["xatlas dominates"] += 1
            elif all(a[k] == b[k] for k, _ in DIRECTIONS):
                w["equal"] += 1
            else:
                w["incomparable"] += 1
        pareto[c] = (len(both), w)
    print("| meshes valid for both | " + " | ".join(str(pareto[c][0]) for c in cats) + " |")
    for k in ["ours dominates", "xatlas dominates", "equal", "incomparable"]:
        print("| " + k + " | " + " | ".join(str(pareto[c][1][k]) for c in cats) + " |")
    print("\nPer metric, meshes (both valid) where the tool is strictly better:\n")
    print("| metric | ours better | xatlas better |")
    print("|---|---|---|")
    for key, sign in DIRECTIONS:
        both = [f for f in files if f in ours and f in xa and is_valid(ours[f]) and is_valid(xa[f])]
        o = sum(1 for f in both if sign * ours[f][key] > sign * xa[f][key])
        x = sum(1 for f in both if sign * ours[f][key] < sign * xa[f][key])
        print("| %s (%s) | %d | %d |" % (key, "lower" if sign < 0 else "higher", o, x))

    # where each tool overlaps or fails
    for name, d in (("UVAtlasGenerator", ours), ("xatlas", xa)):
        bad = [r for r in d.values() if r.get("status") == "ok" and (r.get("overlap_pairs", 0) > 0 or r.get("inverted", 0) > 0)]
        failed = [r for r in d.values() if is_failed(r)]
        print("\n## %s: overlaps or flipped UVs (%d), failed (%d)\n" % (name, len(bad), len(failed)))
        for r in sorted(bad, key=lambda r: r["file"]):
            print("- %s (%s): overlap pairs %s, flipped %s" % (r["file"], r.get("category"), r.get("overlap_pairs"), r.get("inverted")))
        for r in sorted(failed, key=lambda r: r["file"]):
            print("- %s (%s): %s (producer %s) %s" % (r["file"], r.get("category"), r.get("status"),
                                                   r.get("producer_status"), (r.get("error") or "")[:120]))

