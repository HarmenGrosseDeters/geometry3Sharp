#!/usr/bin/env python3
# Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
"""The rows of the net48 / net10.0 measurement (docs/design/DETERMINISM_PROOF.md 5.3) from the traces of a
determinism workflow run: one row per case of the given prefixes, its hash and value count per trace directory, and
whether all of them agree.

    python3 measurement_rows.py <traces dir> [--prefix kernel. --prefix ledger. ...]

<traces dir> holds the trace directories of the run (the merged artifacts traces-*: linux-net10-serial,
windows-net10-serial, windows-net48-serial, windows-net48x86-serial, ...). Output: a Markdown table on stdout,
then one line per case that differs."""
import os
import sys

DEFAULT_PREFIXES = ["kernel.", "ledger.", "boolean.", "probe.bigint-double", "probe.double-bigint", "probe.minmax-zero",
                    "probe.cast-range"]
# the columns, in this order when present: net10.0 on Linux and Windows, net48 64-bit and 32-bit (serial runs)
COLUMNS = ["linux-net10-serial", "windows-net10-serial", "windows-net48-serial", "windows-net48x86-serial",
           "linux-arm64-net10-serial"]


def read_summary(path):
    """case -> (hash, count); a case that failed has hash 'ERROR' and the message as its count"""
    cases = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            parts = line.split("\t")
            if len(parts) >= 3:
                cases[parts[0]] = (parts[1], parts[2])
    return cases


def rows(traces, prefixes):
    cols = [c for c in COLUMNS if os.path.isfile(os.path.join(traces, c, "summary.txt"))]
    data = {c: read_summary(os.path.join(traces, c, "summary.txt")) for c in cols}
    names = sorted({n for c in cols for n in data[c] if any(n.startswith(p) for p in prefixes)})
    out = []
    for n in names:
        cells = [data[c].get(n) for c in cols]
        hashes = {x[0] for x in cells if x is not None}
        same = len(hashes) == 1 and all(x is not None for x in cells) and "ERROR" not in hashes
        out.append((n, cells, same))
    return cols, out


def render(cols, out):
    lines = ["| case | " + " | ".join(cols) + " | same |", "|---" * (len(cols) + 2) + "|"]
    for n, cells, same in out:
        lines.append("| `" + n + "` | " + " | ".join("-" if x is None else x[0] + " (" + x[1] + ")" for x in cells)
                     + " | " + ("yes" if same else "**no**") + " |")
    diff = [n for n, _, same in out if not same]
    lines.append("")
    lines.append("%d cases, %d the same in every column, %d differ%s" % (len(out), len(out) - len(diff), len(diff),
                                                                      (": " + ", ".join(diff)) if diff else ""))
    return "\n".join(lines)


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    traces, prefixes = argv[0], []
    i = 1
    while i < len(argv):
        if argv[i] == "--prefix" and i + 1 < len(argv):
            prefixes.append(argv[i + 1])
            i += 2
        else:
            print("unknown argument: " + argv[i])
            return 2
    cols, out = rows(traces, prefixes or DEFAULT_PREFIXES)
    if not cols:
        print("no trace directory with a summary.txt under " + traces)
        return 2
    print(render(cols, out))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
