#!/usr/bin/env python3
"""List the slowest tests of one or more .trx files and check the test tiers (docs/QA.md, "Test tiers").

Tests that take at least --threshold seconds must carry [Trait("Category", "Slow")] (the draft tier of CI skips
them). The trx of a full run records each test's duration but not its traits, so the tagged tests come from a
list: the output of `dotnet test --no-build --list-tests --filter "Category=Slow"` (--slow-list; the lines with the
test names are indented). This script lists the tests above the threshold that are not on it.

Usage:
    python3 tools/trx_durations.py <file.trx>... [--slow-list <file>] [--top N] [--threshold SECONDS]
        [--fail-above SECONDS] [--summary <markdown file>]

Exit code 1 when an untagged test took --fail-above seconds or more (default: never fail, report only).
"""

import argparse
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SLOW = "Slow"


def parse_duration(text):
    """'hh:mm:ss.fffffff' -> seconds."""
    if not text:
        return 0.0
    h, m, s = text.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def read_slow_list(path):
    """The test names of a `dotnet test --list-tests` output (the indented lines)."""
    with open(path, encoding="utf-8") as f:
        return {line.strip() for line in f if line.startswith("    ") and line.strip()}


def read_trx(path):
    """-> list of (name, seconds, outcome, tagged_slow) for the unit tests of a trx file (tagged_slow: a test category
    "Slow" in the trx, which only some loggers write; see read_slow_list)."""
    root = ET.parse(path).getroot()
    tests = {}
    for ut in root.iterfind(".//t:UnitTest", NS):
        cats = {c.get("TestCategory") for c in ut.iterfind(".//t:TestCategoryItem", NS)}
        method = ut.find("t:TestMethod", NS)
        key = ut.get("id")
        tests[key] = (ut.get("name"), SLOW in cats, method.get("className") if method is not None else "")
    rows = []
    for r in root.iterfind(".//t:UnitTestResult", NS):
        name, slow, cls = tests.get(r.get("testId"), (r.get("testName"), False, ""))
        rows.append((cls + "." + r.get("testName") if cls and not r.get("testName").startswith(cls) else r.get("testName"),
                     parse_duration(r.get("duration")), r.get("outcome"), slow))
    return rows


def class_of(full_name):
    """The test class of a test display name (the part before the method; theory rows have parameters in brackets)."""
    head = full_name.split("(")[0]
    return head.rsplit(".", 1)[0] if "." in head else head


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("trx", nargs="+")
    p.add_argument("--slow-list", help="output of dotnet test --list-tests --filter Category=Slow")
    p.add_argument("--top", type=int, default=20)
    p.add_argument("--threshold", type=float, default=5.0)
    p.add_argument("--fail-above", type=float, default=None)
    p.add_argument("--summary")
    a = p.parse_args(argv)

    rows = [r for f in a.trx for r in read_trx(f)]
    if a.slow_list:
        names = read_slow_list(a.slow_list)
        rows = [(n, sec, o, slow or n in names) for n, sec, o, slow in rows]
    total = sum(r[1] for r in rows)
    lines = [f"{len(rows)} tests, sum of durations {total:.0f} s", ""]
    lines.append(f"Slowest {a.top} tests:")
    for name, sec, outcome, slow in sorted(rows, key=lambda r: -r[1])[:a.top]:
        lines.append(f"  {sec:8.1f} s  {'Slow    ' if slow else 'untagged'}  {name}")
    untagged = sorted((r for r in rows if r[1] >= a.threshold and not r[3]), key=lambda r: -r[1])
    tagged_fast = sorted((r for r in rows if r[3] and r[1] < a.threshold / 5), key=lambda r: -r[1])
    lines += ["", f"Tests of {a.threshold:g} s or more without the trait Category=Slow: {len(untagged)}"]
    for name, sec, _, _ in untagged[:50]:
        lines.append(f"  {sec:8.1f} s  {name}")
    if len(untagged) > 50:
        lines.append(f"  ... and {len(untagged) - 50} more")
    lines += ["", f"Tests tagged Slow that took less than {a.threshold / 5:g} s: {len(tagged_fast)} (candidates to untag)"]
    for name, sec, _, _ in tagged_fast[:5]:
        lines.append(f"  {sec:8.1f} s  {name}")
    out = "\n".join(lines)
    print(out)
    if a.summary:
        with open(a.summary, "a", encoding="utf-8") as f:
            f.write("### Test tiers\n```\n" + out + "\n```\n")
    if a.fail_above is not None and any(r[1] >= a.fail_above for r in untagged):
        print(f"::error::untagged tests of {a.fail_above:g} s or more (tag them with [Slow])")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
