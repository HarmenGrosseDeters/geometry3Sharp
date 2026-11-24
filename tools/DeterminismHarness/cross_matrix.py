#!/usr/bin/env python3
# Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
"""The cross-runtime matrix of docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md from the traces of a determinism
workflow run: for every group of cases and every pair of configurations, how many cases have the same hash, and the
names of those that do not. With --require, the exit status is 1 when a case of a named group differs, is missing or failed in any
pair (or the group has no case at all): those groups are claims (the stage 1 and 2 cases of the determinism proof), the rest is measurement.

    python3 cross_matrix.py <traces dir> [--require GROUP[,GROUP...]]

<traces dir> holds the trace directories of the run (linux-net10-serial, windows-net48-serial, ...). A pair is listed
when both of its directories exist. Output: a Markdown table on stdout (rows: groups, columns: pairs, a cell is
"same/all"), then per pair the cases that differ, are missing on one side or failed."""
import os
import sys

from measurement_rows import read_summary

# (short name, A, B): inside one runtime first, then across runtimes, operating systems and architectures
PAIRS = [
    ("lin: par=ser", "linux-net10-parallel", "linux-net10-serial"),
    ("lin: 1 cpu", "linux-net10-serial", "linux-net10-cpu1"),
    ("lin: 3 cpu", "linux-net10-serial", "linux-net10-cpu3"),
    ("lin: no simd", "linux-net10-serial", "linux-net10-jit-nohw"),
    ("lin: no avx2", "linux-net10-serial", "linux-net10-jit-noavx2"),
    ("lin: no tiering", "linux-net10-serial", "linux-net10-jit-notiered"),
    ("lin: minopts", "linux-net10-serial", "linux-net10-jit-minopts"),
    ("win: no simd", "windows-net10-serial", "windows-net10-jit-nohw"),
    ("arm: no simd", "linux-arm64-net10-serial", "linux-arm64-net10-jit-nohw"),
    ("lin=win net10", "linux-net10-serial", "windows-net10-serial"),
    ("win net10=net48", "windows-net10-serial", "windows-net48-serial"),
    ("lin net10=win net48", "linux-net10-serial", "windows-net48-serial"),
    ("net48 x64=x86", "windows-net48-serial", "windows-net48x86-serial"),
    ("lin x64=arm64", "linux-net10-serial", "linux-arm64-net10-serial"),
    ("win x64=arm64 net10", "windows-net10-serial", "windows-arm64-net10-serial"),
    ("win x64=arm64 net48", "windows-net48-serial", "windows-arm64-net48-serial"),
    ("lin=win arm64", "linux-arm64-net10-serial", "windows-arm64-net10-serial"),
    ("lin x64=mac arm64", "linux-net10-serial", "macos-arm64-net10-serial"),
    ("lin x64=mac x64", "linux-net10-serial", "macos-x64-net10-serial"),
    ("mac arm64=x64", "macos-arm64-net10-serial", "macos-x64-net10-serial"),
]

# cases that differ between any two processes by design (per-process hash seeds), left out of the counts
BY_DESIGN = {"probe.hash-seeded"}


def group(name):
    """the group of a case: its first name part; the Boolean cases with a libm-made input are their own group"""
    if name.endswith(".libm-input"):
        return "*.libm-input"
    return name.split(".")[0]


def matrix(traces):
    """-> (pairs present, groups, cell[(group, pair)] = (same, all), differing[pair] = sorted case names with a reason)"""
    data = {}
    for _, a, b in PAIRS:
        for d in (a, b):
            f = os.path.join(traces, d, "summary.txt")
            if d not in data and os.path.isfile(f):
                data[d] = read_summary(f)
    pairs = [(n, a, b) for n, a, b in PAIRS if a in data and b in data]
    groups, cell, differing = [], {}, {}
    for n, a, b in pairs:
        differing[n] = []
        for case in sorted(set(data[a]) | set(data[b])):
            if case in BY_DESIGN:
                continue
            g = group(case)
            if g not in groups:
                groups.append(g)
            x, y = data[a].get(case), data[b].get(case)
            same = x is not None and y is not None and x[0] == y[0] and x[0] != "ERROR"
            s, t = cell.get((g, n), (0, 0))
            cell[(g, n)] = (s + (1 if same else 0), t + 1)
            if not same:
                why = "missing" if x is None or y is None else "failed" if "ERROR" in (x[0], y[0]) else "differs"
                differing[n].append(case + ("" if why == "differs" else " (" + why + ")"))
    return pairs, sorted(groups), cell, differing


def render(pairs, groups, cell, differing):
    lines = ["| group | " + " | ".join(n for n, _, _ in pairs) + " |", "|---" * (len(pairs) + 1) + "|"]
    for g in groups:
        cells = []
        for n, _, _ in pairs:
            s, t = cell.get((g, n), (0, 0))
            cells.append("-" if t == 0 else ("%d/%d" % (s, t) if s == t else "**%d/%d**" % (s, t)))
        lines.append("| " + g + " | " + " | ".join(cells) + " |")
    lines.append("")
    for n, a, b in pairs:
        d = differing[n]
        lines.append("%s (%s vs %s): %s" % (n, a, b, "all the same" if not d else "%d differ: %s" % (len(d), ", ".join(d))))
    return "\n".join(lines)


def violations(pairs, groups, differing, required):
    """-> the lines that say which required group fails: a group without any case, or the cases of it that are not the same in a pair"""
    out = []
    for g in required:
        if g not in groups:
            out.append("required group %s has no case" % g)
    for n, _, _ in pairs:
        for d in differing[n]:
            if group(d.split(" (")[0]) in required:
                out.append("%s: %s" % (n, d))
    return out


def main(argv):
    args = argv[1:]
    required = []
    if len(args) == 3 and args[1] == "--require":
        required = [g for g in args[2].split(",") if g]
        args = args[:1]
    if len(args) != 1:
        sys.stderr.write(__doc__)
        return 2
    pairs, groups, cell, differing = matrix(args[0])
    print(render(pairs, groups, cell, differing))
    bad = violations(pairs, groups, differing, required)
    for line in bad:
        sys.stderr.write("REQUIRED GROUP DIFFERS: " + line + "\n")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
