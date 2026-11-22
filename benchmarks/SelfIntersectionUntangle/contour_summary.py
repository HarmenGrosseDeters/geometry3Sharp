#!/usr/bin/env python3
"""Summary of the rows of the contour mode (Contours.cs), as markdown.
   python3 benchmarks/SelfIntersectionUntangle/contour_summary.py rows.tsv [more.tsv ...]"""
import csv
import statistics
import sys


def load(paths):
    rows = []
    for p in paths:
        with open(p, encoding="utf-8", newline="") as f:
            rows += list(csv.DictReader(f, delimiter="\t"))
    return rows


def num(s):
    try:
        return float(s)
    except (TypeError, ValueError):
        return None


def summary(rows):
    out = []
    n = len(rows)
    contours = [r for r in rows if r.get("contour") == "self-intersecting"]
    out.append("inputs: %d; no row result (load failed, timeout or crash): %d" % (n, sum(r.get("path", "-") == "-" for r in rows)))
    paths = {}
    for r in rows:
        paths[r.get("path", "-")] = paths.get(r.get("path", "-"), 0) + 1
    out.append("repair path: " + ", ".join("%s %d" % kv for kv in sorted(paths.items())))
    out.append("solidify stopped on a self-intersecting contour: %d" % len(contours))
    if not contours:
        return out
    c = len(contours)
    only = sum(r["contour_defects"] == "self-intersections" for r in contours)
    sep = sum(r["separate_reason"] == "clean" for r in contours)
    unt = [r for r in contours if r["untangle_reason"] == "clean"]
    out.append("")
    out.append("| of the %d contours | count |" % c)
    out.append("|---|---|")
    out.append("| self-intersections are the only defect | %d |" % only)
    out.append("| clean after SeparateTouchingVertices | %d |" % sep)
    out.append("| clean after the untangling prototype | %d |" % len(unt))
    out.append("| finished solidify clean, raw contour (no change) | %d |" % sum(r["finish_raw"] == "clean" for r in contours))
    out.append("| finished solidify clean, separated contour | %d |" % sum(r["finish_separated"] == "clean" for r in contours))
    out.append("| finished solidify clean, untangled contour (the repair would not wrap) | %d |" % sum(r["finish_untangled"] == "clean" for r in contours))
    moves = [num(r["untangle_move_per_diag"]) for r in unt]
    cells = [num(r["untangle_move_per_cell"]) for r in unt]
    if unt:
        out.append("")
        out.append("largest vertex move of the %d untangled contours, per diagonal: median %.2e, max %.2e; per cell: median %.2e, max %.2e"
                   % (len(unt), statistics.median(moves), max(moves), statistics.median(cells), max(cells)))
        for bound in (1e-6, 1e-3):
            out.append("moves below %g of the diagonal: %d of %d" % (bound, sum(m < bound for m in moves), len(unt)))
        out.append("moves below 0.01 cell: %d of %d" % (sum(x < 0.01 for x in cells), len(unt)))
    both = [(num(r["h_wrap_to_input"]), num(r["h_finished_untangled_to_input"])) for r in contours]
    both = [(a, b) for a, b in both if a is not None and b is not None]
    if both:
        out.append("distance of the result to the input, per diagonal (%d inputs with both): wrap median %.2e, finished untangled solidify median %.2e; "
                   "the solidify is closer in %d" % (len(both), statistics.median(a for a, _ in both), statistics.median(b for _, b in both), sum(b < a for a, b in both)))
    out.append("")
    cols = ["input", "triangles", "path", "contour_triangles", "pairs", "separate_reason", "untangle_reason", "untangle_rounds", "untangle_move_per_diag",
            "untangle_move_per_cell", "finish_raw", "finish_untangled", "h_wrap_to_input", "h_finished_untangled_to_input", "untangle_ms"]
    out.append("| " + " | ".join(cols) + " |")
    out.append("|" + "---|" * len(cols))
    for r in contours:
        out.append("| " + " | ".join(r.get(k, "-") for k in cols) + " |")
    return out


if __name__ == "__main__":
    print("\n".join(summary(load(sys.argv[1:]))))
