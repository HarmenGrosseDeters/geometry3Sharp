#!/usr/bin/env python3
"""Coverage-based test sets for mutation shards whose full kill report cannot be made (tests/mutation/README.md,
"Coverage-based test sets").

Under Stryker's coverage capture the tests of a shard run some 100 to 400 times slower than plain when their hot loops sit in
the mutated file itself (ExactMeshBoolean.cs: an open-winding test of 0.3 s takes minutes), so a shard whose filter holds the
whole class list needs hours for the capture alone, and no full run of it, the input narrow_tests.py needs, can finish. This
script chooses a smaller set from plain per-test LINE coverage instead (coverlet, at normal speed): for each shard, the lines
of its span that any test covers, and a greedy cover of them by test methods, cheapest per new line first; a method that is
the only one to cover some line is always kept. It is weaker than narrow_tests.py's choice from recorded killers (a test that
covers a line need not kill its mutants), so a later full kill report should replace it (narrow_tests.py make).

  python3 coverage_select.py collect --methods M --out DIR                    # coverlet per test method (about 30 minutes)
  python3 coverage_select.py choose --coverage DIR --durations TRX --file F CONFIG...   # print each shard's set and cost
  python3 coverage_select.py apply  --coverage DIR --durations TRX --file F CONFIG...   # write the sets into the configs

M lists one item per line: a test method (Namespace.Class.Method), or "Method|row" for the rows of a theory whose display
name contains row (a theory over Similarity.All() costs all its rows, and often one row alone covers what the others do not);
DIR gets one subdirectory per method with its cobertura file. F is the mutated file (repository-relative), CONFIG the shard
configs that split it. A config's `test-case-filter` becomes `FullyQualifiedName=...` terms of the chosen methods.
"""
import argparse
import collections
import concurrent.futures
import hashlib
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
FLOOR = 0.01            # seconds a method without a recorded duration is assumed to take
SPAN = re.compile(r"^\*\*/(?P<file>[^{}]+)\{(?P<a>\d+)\.\.(?P<b>\d+)\}$")


def durations(trx):
    """seconds per test method (Namespace.Class.Method; the rows of a theory added), and per item "Method|row" the rows
    whose display name contains row"""
    rows = []
    for r in ET.parse(trx).getroot().iter(TRX_NS + "UnitTestResult"):
        h, m, s = r.get("duration").split(":")
        rows.append((r.get("testName"), int(h) * 3600 + int(m) * 60 + float(s)))
    out = collections.defaultdict(float)
    for name, secs in rows:
        out[name.split("(")[0]] += secs

    class Durations(dict):
        def get(self, item, default=0.0):
            if "|" not in item:
                return dict.get(self, item, default)
            method, row = item.split("|", 1)
            return sum(secs for name, secs in rows if name.split("(")[0] == method and row in name)
    return Durations(out)


def item_filter(item):
    """the vstest filter term of a test method, or of the rows of a theory method ("Method|row": the rows whose display name
    contains row)"""
    if "|" not in item:
        return "FullyQualifiedName=" + item
    method, row = item.split("|", 1)
    return "(FullyQualifiedName=%s&DisplayName~%s)" % (method, row)


def covered_lines(cobertura, file_suffix):
    """the line numbers of the file (path ending in file_suffix) with hits > 0"""
    lines = set()
    for cls in ET.parse(cobertura).getroot().iter("class"):
        if not cls.get("filename", "").replace("\\", "/").endswith(file_suffix):
            continue
        for ln in cls.iter("line"):
            if int(ln.get("hits", "0")) > 0:
                lines.add(int(ln.get("number")))
    return lines


def load_coverage(directory, file_suffix):
    """method -> covered lines of the file, from collect's directory"""
    out = {}
    for d in sorted(os.listdir(directory)):
        p = os.path.join(directory, d)
        if not os.path.isfile(os.path.join(p, "done")):
            continue
        with open(os.path.join(p, "name"), encoding="utf-8") as f:
            name = f.read().strip()
        out[name] = covered_lines(os.path.join(p, "cov.xml"), file_suffix)
    return out


def line_of_offsets(text):
    """offset -> 1-based line number, as a list of line start offsets"""
    starts = [0]
    for i, c in enumerate(text):
        if c == "\n":
            starts.append(i + 1)
    return starts


def span_lines(text, a, b):
    """the line numbers that intersect the character span [a, b) (the text as Stryker reads it: LF line ends)"""
    starts = line_of_offsets(text)
    import bisect
    first = bisect.bisect_right(starts, a)
    last = bisect.bisect_right(starts, max(a, min(b, len(text)) - 1))
    return set(range(first, last + 1))


def cover(need, coverage, cost):
    """greedy weighted set cover: the methods to keep for the lines in need"""
    kept = set()
    for line in need:
        who = [m for m, ls in coverage.items() if line in ls]
        if len(who) == 1:
            kept.add(who[0])
    left = {ln for ln in need if not any(ln in coverage[m] for m in kept)}
    while left:
        best = min(sorted(coverage), key=lambda m: (cost(m) + FLOOR) / (len(coverage[m] & left) or 1e-9))
        if not coverage[best] & left:
            break
        kept.add(best)
        left -= coverage[best]
    return kept


def choose(coverage_dir, trx, file_rel, configs):
    """-> [(config path, kept methods, their summed plain seconds, lines needed, lines in the span)]"""
    with open(os.path.join(ROOT, file_rel), encoding="utf-8-sig") as f:
        text = f.read().replace("\r\n", "\n")
    cov = load_coverage(coverage_dir, file_rel)
    dur = durations(trx)
    # (the trx names carry the namespace: match a method by its full name, else by its Class.Method tail)
    def cost(m):
        return dur.get(m, 0.0)
    any_cov = set().union(*cov.values()) if cov else set()
    out = []
    for path in configs:
        with open(path, encoding="utf-8") as f:
            cfg = json.load(f)["stryker-config"]
        lines = set()
        for entry in cfg["mutate"]:
            m = SPAN.match(entry)
            if not m or not file_rel.endswith(m.group("file").lstrip("*/")):
                continue
            lines |= span_lines(text, int(m.group("a")), int(m.group("b")))
        need = lines & any_cov
        kept = cover(need, cov, cost)
        out.append((path, sorted(kept), sum(cost(m) for m in kept), len(need), len(lines)))
    return out


def filter_text(items):
    return "|".join(item_filter(m) for m in items)


def collect(methods_file, out_dir, jobs, settings, project):
    with open(methods_file, encoding="utf-8") as f:
        methods = [m.strip() for m in f if m.strip()]
    os.makedirs(out_dir, exist_ok=True)

    def one(m):
        d = os.path.join(out_dir, hashlib.sha1(m.encode()).hexdigest()[:12])
        if os.path.isfile(os.path.join(d, "done")):
            return m, True
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, "name"), "w", encoding="utf-8") as f:
            f.write(m + "\n")
        r = subprocess.run(["dotnet", "test", project, "-c", "Release", "-f", "net10.0", "--no-build", "--filter",
                            item_filter(m), "--settings", settings, "--collect", "XPlat Code Coverage",
                            "--results-directory", d], capture_output=True, text=True)
        found = [os.path.join(p, n) for p, _, ns in os.walk(d) for n in ns if n == "coverage.cobertura.xml"]
        if r.returncode == 0 and found:
            os.replace(found[0], os.path.join(d, "cov.xml"))
            open(os.path.join(d, "done"), "w").close()
            return m, True
        return m, False

    with concurrent.futures.ThreadPoolExecutor(jobs) as ex:
        bad = [m for m, ok in ex.map(one, methods) if not ok]
    print("collected %d of %d methods" % (len(methods) - len(bad), len(methods)))
    for m in bad:
        print("failed: " + m)
    return 1 if bad else 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("collect")
    c.add_argument("--methods", required=True)
    c.add_argument("--out", required=True)
    # (one at a time: coverlet instruments the library in the test project's bin directory in place, and runs side by side
    # undo each other's instrumentation, which gives empty coverage files)
    c.add_argument("--jobs", type=int, default=1)
    c.add_argument("--settings", default=os.path.join(HERE, "coverage_select.runsettings"))
    c.add_argument("--project", default=os.path.join(ROOT, "tests", "geometry3Sharp.Tests"))
    for name in ("choose", "apply"):
        p = sub.add_parser(name)
        p.add_argument("--coverage", required=True)
        p.add_argument("--durations", required=True)
        p.add_argument("--file", required=True)
        p.add_argument("configs", nargs="+")
    a = ap.parse_args(argv)
    if a.cmd == "collect":
        return collect(a.methods, a.out, a.jobs, a.settings, a.project)
    rows = choose(a.coverage, a.durations, a.file, a.configs)
    for path, kept, secs, need, total in rows:
        print("%s: %d methods, %.1f s plain, %d of %d span lines covered" % (os.path.basename(path), len(kept), secs, need, total))
        if a.cmd == "apply":
            with open(path, encoding="utf-8") as f:
                raw = json.load(f)
            raw["stryker-config"]["test-case-filter"] = filter_text(kept)
            with open(path, "w", encoding="utf-8") as f:
                json.dump(raw, f, indent=2)
                f.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
