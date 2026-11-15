#!/usr/bin/env python3
"""Names the tests that were running when a blamed `dotnet test` run hung or crashed (docs/QA.md, "Hang diagnosis").

`dotnet test --blame --blame-hang --blame-hang-timeout 10m` writes Sequence_<guid>.xml into the results directory: every test
that started, with Completed="True" once it finished. A test host that hung (killed after the timeout) or crashed leaves the
tests that did not complete in it; the last ones are the suspects. This script lists them and writes a Markdown table to the
step summary, so the next hang of netstandard-b (issue #824) names the test without opening the artifact.

Usage: python3 tools/blame_summary.py <results directory> [--summary FILE]
Exit code 0 always: it only reports.
"""
import argparse
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET


SHOWN = 20


def incomplete_tests(sequence_file):
    """[(name, source)] of the tests of one Sequence file that started and did not complete, in start order"""
    try:
        root = ET.parse(sequence_file).getroot()
    except (ET.ParseError, OSError):
        return None
    return [(t.get("Name", "?"), re.split(r"[\\/]", t.get("Source", ""))[-1]) for t in root.iter("Test") if t.get("Completed", "").lower() != "true"]


def report(results_dir):
    """(Markdown lines, number of incomplete tests) over all Sequence files below results_dir"""
    files = sorted(glob.glob(os.path.join(results_dir, "**", "Sequence_*.xml"), recursive=True))
    if not files:
        # (vstest keeps a Sequence file only when the test host hung or crashed: after an ordinary failing test there is none)
        return ["### Blame", "No Sequence file in " + results_dir + ": no test host hang or crash was recorded. The failure is an ordinary "
                "test failure (see the test log), or the run did not get as far as running tests."], 0
    lines, total = ["### Blame: tests that started and did not complete"], 0
    for f in files:
        tests = incomplete_tests(f)
        if tests is None:
            lines.append(f"- `{os.path.basename(f)}`: not readable (a test host killed while writing it)")
        elif not tests:
            lines.append(f"- `{os.path.basename(f)}`: every started test completed")
        else:
            total += len(tests)
            # the suspects are the LAST tests that started (the sequence file is in start order, a hung test is still running
            # when the host is killed): the last SHOWN, not the first
            shown = tests[-SHOWN:]
            lines.append(f"- `{os.path.basename(f)}`: {len(tests)} did not complete (the hang or crash is in one of them, usually the last):")
            if len(tests) > SHOWN:
                lines.append(f"  - ... {len(tests) - SHOWN} earlier ones not shown")
            lines += [f"  - `{name}` ({source})" for name, source in shown]
    return lines, total


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("results_dir")
    p.add_argument("--summary", help="append the Markdown to this file (the step summary)")
    a = p.parse_args(argv)
    lines, total = report(a.results_dir)
    print("\n".join(lines))
    if a.summary:
        with open(a.summary, "a", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
    if total:
        print(f"::warning::{total} test(s) started and did not complete: see the blame summary")
    return 0


if __name__ == "__main__":
    sys.exit(main())
