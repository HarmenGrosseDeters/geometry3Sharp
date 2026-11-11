#!/usr/bin/env python3
"""Recomputes the character offsets of the split shards (stryker-mesh-*.json) from the method names that the splits are at.

A big file is mutated in parts: `**/mesh_ops/F.cs{a..b}` in the config of each part (README, "Splits"). The offsets move
with every edit of the file before them, but the SplitMethod table of tests/geometry3Sharp.Tests/MutationConfigTests.cs
names the method each split is at, so after an edit

  python3 tests/mutation/resplit.py check     # exit 1 if a config's offsets are not at their method any more
  python3 tests/mutation/resplit.py apply     # rewrite the offsets of the configs from the method names

(`check` does what MutationConfigTests does, for all files at once and with a hint what to run.) The parts of a file are
its configs in order of their start offset; the first starts at 0 and the last runs to 1000000. A config whose offsets
change gets a new cache key, so its next baseline comes from the next full run on master (the nightly one): do not re-split
for a small edit that leaves the split points where they were relative to their method; the check says when it must be done.
The offset of a split is the first line of the attributes and comments directly above the method, as the test reads it.
To add or move a split, change the method name in SplitMethod (a new part also needs a new config file, a copy of a
neighbour with the same test filter and break threshold), then `apply`.
"""
import glob
import json
import os
import re
import sys

import suggest_split

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
TABLE = os.path.join(ROOT, "tests", "geometry3Sharp.Tests", "MutationConfigTests.cs")
END = 1000000
_SPAN = re.compile(r'("\*\*/(?P<file>[^"{}]+)\{)(?P<a>\d+)\.\.(?P<b>\d+)(\}")')


def split_methods(table_text):
    """{file: [method signature prefix, ...]} from the SplitMethod dictionary of MutationConfigTests.cs"""
    block = re.search(r"SplitMethod\s*=\s*new Dictionary<string, string\[\]>\(\)\s*\{(?P<body>.*?)\n\s*\};", table_text, re.S)
    if not block:
        raise ValueError("SplitMethod not found in MutationConfigTests.cs")
    out = {}
    for m in re.finditer(r'\["(?P<file>[^"]+)"\]\s*=\s*new\[\]\s*\{(?P<names>[^}]*)\}', block.group("body")):
        out[m.group("file")] = re.findall(r'"((?:[^"\\]|\\.)*)"', m.group("names"))
    return out


def read_source(path):
    """the text as Stryker reads it on Linux: no byte order mark, LF line breaks"""
    with open(path, encoding="utf-8-sig", newline="") as f:
        return f.read().replace("\r\n", "\n")


def offsets_of(text, names):
    """[(start offset, acceptable offsets)] of each named method, ascending; ValueError when a name is missing, ambiguous or
    out of order. The acceptable offsets are the line starts from the first attribute or comment line above the method to its
    signature line, which MutationConfigTests accepts as well: an existing split anywhere there stays as it is."""
    starts = suggest_split.method_starts(text)
    out = []
    for name in names:
        hits = [(o, acc) for o, sig, acc in starts if sig.startswith(name)]
        if len(hits) != 1:
            raise ValueError("%r matches %d methods (a split needs exactly one)" % (name, len(hits)))
        out.append(hits[0])
    firsts = [o for o, _ in out]
    if firsts != sorted(firsts) or len(set(firsts)) != len(firsts):
        raise ValueError("the split methods are not in the order of the file: %s" % ", ".join(names))
    return out


def configs_of(directory=HERE):
    """{file: [(start, config path)] ascending} of every span in stryker-mesh-*.json"""
    out = {}
    for path in sorted(glob.glob(os.path.join(directory, "stryker-mesh-*.json"))):
        with open(path, encoding="utf-8-sig") as f:
            text = f.read()
        for m in _SPAN.finditer(text):
            out.setdefault(m.group("file"), []).append((int(m.group("a")), path))
    return {k: sorted(v) for k, v in out.items()}


def plan(table_text, root=ROOT, directory=HERE):
    """[(config path, file, wanted start, wanted end, current start, current end, ok)] for every part; a part is ok when its
    start and end are acceptable offsets of the methods (the last end: anywhere from the end of the file on)"""
    methods = split_methods(table_text)
    parts = configs_of(directory)
    if set(methods) != set(parts):
        raise ValueError("SplitMethod names %s, the configs split %s" % (sorted(methods), sorted(parts)))
    rows = []
    for file, names in sorted(methods.items()):
        text = read_source(os.path.join(root, file))
        cut_info = offsets_of(text, names)
        cuts = [o for o, _ in cut_info]
        cfgs = parts[file]
        if len(cfgs) != len(names) + 1:
            raise ValueError("%s: %d split methods need %d configs, there are %d" % (file, len(names), len(names) + 1, len(cfgs)))
        bounds = [0] + cuts + [END]
        for i, (a, path) in enumerate(cfgs):
            with open(path, encoding="utf-8-sig") as f:
                m = next(x for x in _SPAN.finditer(f.read()) if x.group("file") == file)
            ca, cb = int(m.group("a")), int(m.group("b"))
            start_ok = ca == 0 if i == 0 else ca in cut_info[i - 1][1]
            end_ok = cb >= len(text) if i == len(cfgs) - 1 else cb in cut_info[i][1]
            rows.append((path, file, bounds[i], bounds[i + 1], ca, cb, start_ok and end_ok))
    return rows


def apply(rows):
    changed = []
    for path, file, a, b, ca, cb, ok in rows:
        if ok:
            continue
        with open(path, encoding="utf-8-sig", newline="") as f:
            text = f.read()
        new = _SPAN.sub(lambda m: m.group(1) + "%d..%d" % (a, b) + m.group(5) if m.group("file") == file else m.group(0), text)
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(new)
        changed.append(os.path.basename(path))
    return changed


def main(argv):
    if len(argv) != 1 or argv[0] not in ("check", "apply"):
        print(__doc__)
        return 2
    with open(TABLE, encoding="utf-8-sig") as f:
        rows = plan(f.read())
    stale = [r for r in rows if not r[6]]
    if argv[0] == "check":
        for path, file, a, b, ca, cb, _ in stale:
            print("%s: %s{%d..%d}, its methods are at {%d..%d}" % (os.path.basename(path), file, ca, cb, a, b))
        if stale:
            print("run: python3 tests/mutation/resplit.py apply (the changed configs then have no baseline until the next full run on master)")
        return 1 if stale else 0
    for name in apply(rows):
        print("rewrote", name)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
