#!/usr/bin/env python3
# Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
"""The cross-runtime matrix of docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md from the traces of a determinism
workflow run: for every group of cases and every pair of configurations, how many cases have the same hash, and the
names of those that do not. With --require, the exit status is 1 when a case of a named group differs, is missing or failed in any pair
of GATE_PAIRS (the comparisons of the determinism workflow's default legs: every pair must have both trace directories), when the
group does not have exactly its expected number of cases (EXPECTED_CASES) in a pair, or when the group has no case; an empty list is
refused (status 2). Those groups are claims (the stage 1 and 2 cases of the determinism proof), the rest is measurement."""
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

def _gate_pairs():
    """the pairs whose stage 1 and 2 cases must be the same: every comparison that the default legs of determinism.yml (linux, windows,
    linux-arm64, windows-arm64; not macOS, which is opt-in) make, so a leg that did not run fails the gate"""
    out = []
    def add(name, a, b):
        out.append((name, a, b))
    add("lin par=par2", "linux-net10-parallel", "linux-net10-parallel2")
    add("lin par=ser", "linux-net10-parallel", "linux-net10-serial")
    for n in (1, 2, 3, 8):
        add("lin cpu%d" % n, "linux-net10-serial", "linux-net10-cpu%d" % n)
    for k in ("nohw", "noavx2", "notiered", "minopts"):
        add("lin jit-" + k, "linux-net10-serial", "linux-net10-jit-" + k)
    add("win48 par=par2", "windows-net48-parallel", "windows-net48-parallel2")
    add("win48 par=ser", "windows-net48-parallel", "windows-net48-serial")
    add("win10 par=ser", "windows-net10-parallel", "windows-net10-serial")
    add("win10=win48 serial", "windows-net10-serial", "windows-net48-serial")
    add("win10=win48 parallel", "windows-net10-parallel", "windows-net48-parallel")
    add("lin=win net10", "linux-net10-serial", "windows-net10-serial")
    add("lin net10=win net48", "linux-net10-serial", "windows-net48-serial")
    add("win48 x64=x86", "windows-net48-serial", "windows-net48x86-serial")
    for k in ("nohw", "notiered"):
        add("win10 jit-" + k, "windows-net10-serial", "windows-net10-jit-" + k)
    for a in ("linux-arm64", "windows-arm64"):
        add(a + " par=par2", a + "-net10-parallel", a + "-net10-parallel2")
        add(a + " par=ser", a + "-net10-parallel", a + "-net10-serial")
        for n in (1, 3):
            add(a + " cpu%d" % n, a + "-net10-serial", a + "-net10-cpu%d" % n)
        for k in ("nohw", "notiered"):
            add(a + " jit-" + k, a + "-net10-serial", a + "-net10-jit-" + k)
        add("lin x64=" + a, "linux-net10-serial", a + "-net10-serial")
        add("win48 x64=" + a, "windows-net48-serial", a + "-net10-serial")
    add("win10 x64=arm64", "windows-net10-serial", "windows-arm64-net10-serial")
    add("winarm48 par=ser", "windows-arm64-net48-parallel", "windows-arm64-net48-serial")
    add("win48 x64=arm64", "windows-net48-serial", "windows-arm64-net48-serial")
    add("winarm 10=48", "windows-arm64-net10-serial", "windows-arm64-net48-serial")
    return out


GATE_PAIRS = _gate_pairs()

# the number of cases of the groups that are claims: a case that vanishes in every configuration (or one that is added) changes it
EXPECTED_CASES = {"stage1": 12, "stage2": 4}

# cases that differ between any two processes by design (per-process hash seeds), left out of the counts
BY_DESIGN = {"probe.hash-seeded"}


def group(name):
    """the group of a case: its first name part; the Boolean cases with a libm-made input are their own group"""
    if name.endswith(".libm-input"):
        return "*.libm-input"
    return name.split(".")[0]


def matrix(traces, pair_defs=None):
    """-> (pairs present, groups, cell[(group, pair)] = (same, all), differing[pair] = sorted case names with a reason)"""
    data = {}
    pair_defs = PAIRS if pair_defs is None else pair_defs
    for _, a, b in pair_defs:
        for d in (a, b):
            f = os.path.join(traces, d, "summary.txt")
            if d not in data and os.path.isfile(f):
                data[d] = read_summary(f)
    pairs = [(n, a, b) for n, a, b in pair_defs if a in data and b in data]
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


def violations(pairs, groups, cell, differing, required, expected, pair_defs):
    """-> the lines that say what fails: a required pair without both trace directories, a required group without any case, a group
    whose number of cases in a pair is not the expected one, or the cases of it that are not the same in a pair"""
    out = []
    present = {n for n, _, _ in pairs}
    for n, a, b in pair_defs:
        if n not in present:
            out.append("pair %s (%s vs %s): a trace directory is missing" % (n, a, b))
    for g in required:
        if g not in groups:
            out.append("required group %s has no case" % g)
    for n, _, _ in pairs:
        for g in required:
            if g in expected and cell.get((g, n), (0, 0))[1] != expected[g]:
                out.append("%s: group %s has %d cases, expected %d" % (n, g, cell.get((g, n), (0, 0))[1], expected[g]))
        for d in differing[n]:
            if group(d.split(" (")[0]) in required:
                out.append("%s: %s" % (n, d))
    return out


def main(argv, gate_pairs=None, expected=None):
    args = argv[1:]
    required = None
    if len(args) == 3 and args[1] == "--require":
        required = [g for g in args[2].split(",") if g]
        args = args[:1]
        if not required:
            sys.stderr.write("--require needs at least one group\n")
            return 2
    if len(args) != 1:
        sys.stderr.write(__doc__)
        return 2
    gate_pairs = GATE_PAIRS if gate_pairs is None else gate_pairs
    expected = EXPECTED_CASES if expected is None else expected
    pairs, groups, cell, differing = matrix(args[0])
    print(render(pairs, groups, cell, differing))
    if required is None:
        return 0
    gpairs, ggroups, gcell, gdiffering = matrix(args[0], gate_pairs)
    bad = violations(gpairs, ggroups, gcell, gdiffering, required, expected, gate_pairs)
    for line in bad:
        sys.stderr.write("REQUIRED GROUP DIFFERS: " + line + "\n")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
