#!/usr/bin/env python3
"""The exact imprint before and after the sliver cleanup at near contact (#1034, docs/design/IMPRINT_SLIVER_CLEANUP.md).

Reads the results-*.jsonl of two runs of thingi-contact.yml in mode imprint (the rows of compare.py, one per case and method; the
exact imprint's fields are prefixed with the operation, a_by_b_ / b_by_a_) and writes, per class:
- the operations, good (status ok, closed, NotCleanReason empty, as thingi_imprint.py summary) and RoundingClean, before and after;
- after: how often the cleanup stage ran, the operations with a reroute of a split edge through a target vertex (and through a new
  vertex) and their total count, the largest drift used against the bound (MaxDrift / MaxLength);
- the operations good before whose counts differ after (an output change of a clean input: should be none), and the operations
  good before on which the stage ran (should be none: it runs only where the result is not clean without it);
- every exact-imprint operation not good after, with the fields that say why.

Usage: python3 data/imprint_sliver_stats.py <before dir or files...> -- <after dir or files...>
"""
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import thingi_imprint as ti  # noqa: E402

OPS = ("a_by_b", "b_by_a")
# fields of an ok exact imprint compared between the runs for a result that was good before (an output change shows in them)
SAME = ("new_vertices", "curve_edges", "result_tris", "curve_length", "volume", "result_reason")


def files(args):
    out = []
    for a in args:
        out += sorted(glob.glob(os.path.join(a, "**", "results-*.jsonl"), recursive=True)) if os.path.isdir(a) else [a]
    return out


def rows(args):
    """{(class group, file): row} of the exact imprint"""
    return {(ti.group(r), r["file"]): r for r in ti.read_rows(files(args)) if r["method"] == "exact_imprint"}


def g(r, op, k, default=None):
    return r.get(f"{op}_{k}", default) if r else default


def stats(before, after, out=sys.stdout):
    groups = sorted({k[0] for k in after} | {k[0] for k in before}, key=lambda x: (x != "thingi-pairs", x))
    w = out.write
    w("| class | operations | good before | good after | rounding clean before | rounding clean after | stage ran | "
      "ops rerouted through a target vertex (count) | ops rerouted through a new vertex (count) | largest drift / bound | "
      "good before, changed after | good before, stage ran |\n")
    w("|---|---|---|---|---|---|---|---|---|---|---|---|\n")
    left, changed = [], []
    for grp in groups:
        keys = sorted(k for k in set(before) | set(after) if k[0] == grp)
        n = gb = ga = cb = ca = st = tvo = tvc = nvo = nvc = ch = stb = 0
        worst = 0.0
        for k in keys:
            b, a = before.get(k), after.get(k)
            for op in OPS:
                if g(a, op, "status") is None and g(b, op, "status") is None:
                    continue
                n += 1
                good_b = b is not None and ti.outcome(b, op) == "good"
                good_a = a is not None and ti.outcome(a, op) == "good"
                gb += good_b
                ga += good_a
                cb += g(b, op, "rounding_clean") is True
                ca += g(a, op, "rounding_clean") is True
                ran = (g(a, op, "sliver_cleanup_stage") or 0) > 0
                st += ran
                t, v = g(a, op, "split_edge_reroutes_through_target_vertex") or 0, g(a, op, "split_edge_reroutes_through_new_vertex") or 0
                tvo += t > 0
                tvc += t
                nvo += v > 0
                nvc += v
                d, bound = g(a, op, "rounding_max_drift"), g(a, op, "rounding_bound")
                if d is not None and bound:
                    worst = max(worst, d / bound)
                if good_b:
                    diff = [f for f in SAME if g(b, op, f) != g(a, op, f)]
                    if diff:
                        ch += 1
                        changed.append((grp, k[1], op, diff))
                    stb += ran
                if a is not None and not good_a:
                    left.append((grp, k[1], op, a))
        w(f"| {grp} | {n} | {gb} | {ga} | {cb} | {ca} | {st} | {tvo} ({tvc}) | {nvo} ({nvc}) | {worst:.3g} | {ch} | {stb} |\n")
    w("\n**Good before, counts differ after** (should be none):\n\n")
    for grp, f, op, diff in changed:
        w(f"- {f} {op}: {', '.join(diff)}\n")
    if not changed:
        w("none\n")
    w("\n**Exact imprint operations not good after** (the fields that say why):\n\n")
    w("| class | case | op | outcome | stage ran | degenerate left | intersections left | contacts left | evaluation limit | drift / bound |\n")
    w("|---|---|---|---|---|---|---|---|---|---|\n")
    for grp, f, op, a in left:
        d, bound = g(a, op, "rounding_max_drift"), g(a, op, "rounding_bound")
        ratio = f"{d / bound:.3g}" if d is not None and bound else "-"
        w(f"| {grp} | {f} | {op} | {ti.outcome(a, op)} | {g(a, op, 'sliver_cleanup_stage', '-')} | "
          f"{g(a, op, 'rounding_remaining_degenerate', '-')} | {g(a, op, 'rounding_remaining_intersections', '-')} | "
          f"{g(a, op, 'rounding_remaining_false_contacts', '-')} | {g(a, op, 'rounding_limit', '-')} | {ratio} |\n")
    if not left:
        w("none\n")


def main(argv):
    if "--" not in argv:
        sys.exit(__doc__)
    i = argv.index("--")
    stats(rows(argv[:i]), rows(argv[i + 1:]))


if __name__ == "__main__":
    main(sys.argv[1:])
