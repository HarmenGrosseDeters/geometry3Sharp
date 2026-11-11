#!/usr/bin/env python3
"""List the slowest tests of one or more .trx files and check the test tiers (docs/QA.md, "Test tiers").

Tests that take at least --threshold seconds must carry [Trait("Category", "Slow")] (the draft tier of CI skips
them). The trx of a full run records each test's duration but not its traits, so the tagged tests come from a
list: the output of `dotnet test --no-build --list-tests --filter "Category=Slow"` (--slow-list; the lines with the
test names are indented). This script lists the tests above the threshold that are not on it.

Ratchet (CLAUDE.md, "Test time"): with --backlog FILE the script fails for
  - a test of more than --max-seconds (default 10) whose method is not on the backlog,
  - a test of --fail-from seconds or more (default 7) without the trait Slow whose method is not on the backlog (the rule is 5 s,
    --threshold, and tests from there to --fail-from are reported as warnings only: the CI runner's timings vary by a
    second or two around the line),
  - a backlog entry that names no test of the run (renamed or deleted: remove it),
  - with --backlog-base FILE, a backlog entry that the base branch's list does not have (the list only shrinks).
The backlog (tests/geometry3Sharp.Tests/SLOW_BACKLOG.txt) has one `Class.Method` per line (nested classes as
`Outer+Inner.Method`, all rows of a theory together), `#` starts a comment. --make-backlog FILE writes one from the
trx: every method with a row of --backlog-from seconds or more (default 4: a margin under the threshold, the CI
runner is not the machine the list was made on).

Usage:
    python3 tools/trx_durations.py <file.trx>... [--slow-list <file>] [--top N] [--threshold SECONDS]
        [--fail-above SECONDS] [--backlog FILE [--backlog-base FILE] [--max-seconds S]] [--summary <markdown file>]
    python3 tools/trx_durations.py <file.trx> --make-backlog FILE [--backlog-from SECONDS]

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


def method_key(full_name):
    """`Class.Method` of a test display name (namespace and theory arguments dropped, nested classes as Outer+Inner)."""
    head = full_name.split("(")[0]
    cls, _, method = head.rpartition(".")
    return cls.split(".")[-1] + "." + method


def read_backlog(path):
    with open(path, encoding="utf-8") as f:
        return {line.split("#")[0].strip() for line in f if line.split("#")[0].strip()}


def max_by_method(rows):
    result = {}
    for name, sec, _, _ in rows:
        k = method_key(name)
        result[k] = max(result.get(k, 0.0), sec)
    return result


def make_backlog(rows, floor):
    return sorted(k for k, v in max_by_method(rows).items() if v >= floor)


def ratchet(rows, backlog, base, threshold, max_seconds, fail_from=7.0):
    """-> list of error lines (the untagged tests from threshold to fail_from are not errors: see untagged_warnings)."""
    errors = []
    for name, sec, _, slow in sorted(rows, key=lambda r: -r[1]):
        if method_key(name) in backlog:
            continue
        if sec > max_seconds:
            errors.append(f"{sec:.1f} s > {max_seconds:g} s, not on the backlog: {name}")
        elif sec >= max(threshold, fail_from) and not slow:
            errors.append(f"{sec:.1f} s without the trait Slow, not on the backlog: {name}")
    present = {method_key(r[0]) for r in rows}
    for entry in sorted(backlog - present):
        errors.append(f"backlog entry names no test of this run (renamed or deleted: remove it): {entry}")
    if base is not None:
        for entry in sorted(backlog - base):
            errors.append(f"backlog entry that the base branch does not have (the list only shrinks): {entry}")
    return errors


def untagged_warnings(rows, backlog, threshold, fail_from=7.0):
    """The untagged tests from threshold to fail_from seconds that are not on the backlog (reported, not failed)."""
    return [f"{sec:.1f} s without the trait Slow: {name}" for name, sec, _, slow in sorted(rows, key=lambda r: -r[1])
            if threshold <= sec < fail_from and not slow and method_key(name) not in backlog]


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("trx", nargs="+")
    p.add_argument("--slow-list", help="output of dotnet test --list-tests --filter Category=Slow")
    p.add_argument("--top", type=int, default=20)
    p.add_argument("--threshold", type=float, default=5.0)
    p.add_argument("--fail-above", type=float, default=None)
    p.add_argument("--summary")
    p.add_argument("--backlog", help="SLOW_BACKLOG.txt: switches the ratchet on")
    p.add_argument("--backlog-base", help="the base branch's SLOW_BACKLOG.txt (the list must not grow)")
    p.add_argument("--max-seconds", type=float, default=10.0)
    p.add_argument("--report-only", action="store_true", help="print the ratchet's violations (as warnings) but exit 0")
    p.add_argument("--fail-from", type=float, default=7.0, help="untagged tests from this many seconds fail the ratchet (default 7)")
    p.add_argument("--make-backlog", help="write a backlog from the trx to this file and stop")
    p.add_argument("--backlog-from", type=float, default=4.0)
    a = p.parse_args(argv)

    rows = [r for f in a.trx for r in read_trx(f)]
    if a.slow_list:
        names = read_slow_list(a.slow_list)
        rows = [(n, sec, o, slow or n in names) for n, sec, o, slow in rows]
    if a.make_backlog:
        entries = make_backlog(rows, a.backlog_from)
        with open(a.make_backlog, "w", encoding="utf-8", newline="\n") as f:
            f.write("# Tests above the time rule (CLAUDE.md, \"Test time\"): Class.Method, all rows of a theory together.\n"
                    "# The list only shrinks (tools/trx_durations.py --backlog-base): fix a test, then delete its line.\n")
            f.writelines(e + "\n" for e in entries)
        print(f"{len(entries)} entries written")
        return 0
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
    errors = []
    if a.backlog:
        backlog = read_backlog(a.backlog)
        base = read_backlog(a.backlog_base) if a.backlog_base else None
        errors = ratchet(rows, backlog, base, a.threshold, a.max_seconds, a.fail_from)
        warnings = untagged_warnings(rows, backlog, a.threshold, a.fail_from)
        lines += ["", f"Ratchet: backlog of {len(backlog)} methods, {len(errors)} violations, {len(warnings)} warnings (untagged, {a.threshold:g} to {a.fail_from:g} s)"]
        lines += ["  " + e for e in errors[:50]] + ["  warning: " + w for w in warnings[:50]]
    out = "\n".join(lines)
    print(out)
    if a.summary:
        with open(a.summary, "a", encoding="utf-8") as f:
            f.write("### Test tiers\n```\n" + out + "\n```\n")
    if errors and a.report_only:
        print(f"::warning::{len(errors)} violations of the test time rule (CLAUDE.md, Test time), report mode (RATCHET_ENFORCE=0)")
    elif errors:
        print(f"::error::{len(errors)} violations of the test time rule (CLAUDE.md, Test time)")
        return 1
    if a.fail_above is not None and any(r[1] >= a.fail_above for r in untagged):
        print(f"::error::untagged tests of {a.fail_above:g} s or more (tag them with [Slow])")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
