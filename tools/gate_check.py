#!/usr/bin/env python3
"""Flag a pull request that changes the files that judge it (docs/QA.md, "Gate-change check").

Reads the gate list (tools/gate_files.txt: `glob | reason` per line), the names of the changed files of the PR (one JSON string per
line: `filename` and `previous_filename` of every entry of the REST file list) and the labels of the PR (one JSON string per line).
A changed file that matches a gate glob is a hit. The PR passes if it has no hit or carries the label `gate-change`.

Observe mode (default): hits are reported (step summary, one warning annotation per file) and the exit code is 0.
Enforce mode (--enforce, or GATE_ENFORCE=1): a hit without the label exits 1.

Run from the base branch by .github/workflows/gate-change.yml (pull_request_target); never from the PR's checkout.
Every file name is PR-controlled text: it is escaped for annotations and summary, never passed to a shell.

Usage:
    python3 tools/gate_check.py --gates tools/gate_files.txt --changed changed.txt --labels labels.txt [--summary FILE]
        [--enforce] [--file-count N]
"""

import argparse
import json
import os
import re
import sys

LABEL = "gate-change"
# the REST list of the files of a pull request holds at most 3000 entries
API_FILE_LIMIT = 3000


def glob_to_regex(glob):
    """`*` and `?` stay in one path segment; `**/` is any number of directories (also none); `**` is anything."""
    out = []
    i = 0
    while i < len(glob):
        c = glob[i]
        if glob.startswith("**/", i):
            out.append("(?:.*/)?")
            i += 3
        elif glob.startswith("**", i):
            out.append(".*")
            i += 2
        elif c == "*":
            out.append("[^/]*")
            i += 1
        elif c == "?":
            out.append("[^/]")
            i += 1
        else:
            out.append(re.escape(c))
            i += 1
    return re.compile("".join(out) + r"\Z", re.DOTALL)


def parse_gates(text):
    """Return [(glob, reason, regex)]; a malformed line raises ValueError (a broken list must not pass silently)."""
    gates = []
    for number, line in enumerate(text.splitlines(), 1):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        glob, sep, reason = (part.strip() for part in line.partition("|"))
        if not sep or not glob or not reason:
            raise ValueError(f"gate list line {number}: expected 'glob | reason'")
        if glob.startswith("/") or ".." in glob.split("/"):
            raise ValueError(f"gate list line {number}: the glob must be a repository-relative path")
        gates.append((glob, reason, glob_to_regex(glob)))
    if not gates:
        raise ValueError("the gate list is empty")
    return gates


def parse_json_lines(text):
    return [json.loads(line) for line in text.splitlines() if line.strip()]


def find_hits(gates, files):
    """Return [(file, glob, reason)], one per changed file (the first matching glob), in input order, without duplicates."""
    hits = []
    seen = set()
    for f in files:
        if f in seen:
            continue
        seen.add(f)
        for glob, reason, rx in gates:
            if rx.match(f):
                hits.append((f, glob, reason))
                break
    return hits


def clean(text):
    """Printable form for the summary: control characters and backticks cannot break the markdown."""
    return "".join("�" if (ord(c) < 32 or ord(c) == 127 or c == "`") else c for c in text)


def escape_annotation(text):
    """Escape for a workflow command message, so a file name cannot start another command."""
    return text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def judge(gates, files, labels, enforce, file_count=None):
    """Return (exit_code, summary_markdown, annotations)."""
    hits = find_hits(gates, files)
    truncated = file_count is not None and file_count >= API_FILE_LIMIT
    labelled = LABEL in labels
    lines = ["### Gate-change check", ""]
    annotations = []
    if truncated:
        note = (f"The PR changes {file_count} files; the REST file list is cut at {API_FILE_LIMIT}, so the check cannot "
                "see all of them. Treated as a gate change.")
        lines += [note, ""]
        annotations.append(f"::warning::{escape_annotation(note)}")
    if not hits and not truncated:
        lines.append("No gate file changed.")
        return 0, "\n".join(lines) + "\n", annotations
    if hits:
        lines += [f"{len(hits)} changed file(s) are gate files (tools/gate_files.txt of the base branch):", ""]
        lines += [f"- `{clean(f)}` ({clean(glob)}: {clean(reason)})" for f, glob, reason in hits]
        lines.append("")
        for f, glob, reason in hits:
            annotations.append(f"::warning::{escape_annotation(f'gate file changed: {f} ({reason})')}")
    if labelled:
        lines.append(f"The label `{LABEL}` is set: the owner approved this change of the checks.")
        return 0, "\n".join(lines) + "\n", annotations
    if enforce:
        lines.append(f"**Failed**: a PR that changes gate files needs the label `{LABEL}` (set by the owner).")
        annotations.append(f"::error::{escape_annotation(f'gate files changed without the label {LABEL}')}")
        return 1, "\n".join(lines) + "\n", annotations
    lines.append(f"Observe mode: not failing. In enforce mode this PR would need the label `{LABEL}` (set by the owner).")
    return 0, "\n".join(lines) + "\n", annotations


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--gates", required=True)
    ap.add_argument("--changed", required=True, help="file with one JSON string per line (filename and previous_filename)")
    ap.add_argument("--labels", required=True, help="file with one JSON string per line (label names)")
    ap.add_argument("--summary", help="append the markdown summary to this file ($GITHUB_STEP_SUMMARY)")
    ap.add_argument("--enforce", action="store_true", help="fail on a hit without the label (also GATE_ENFORCE=1)")
    ap.add_argument("--file-count", type=int, help="number of changed files of the PR (a count at the API limit counts as a hit)")
    args = ap.parse_args(argv)
    enforce = args.enforce or os.environ.get("GATE_ENFORCE") == "1"
    with open(args.gates, encoding="utf-8") as f:
        gates = parse_gates(f.read())
    with open(args.changed, encoding="utf-8") as f:
        files = parse_json_lines(f.read())
    with open(args.labels, encoding="utf-8") as f:
        labels = parse_json_lines(f.read())
    code, summary, annotations = judge(gates, files, labels, enforce, args.file_count)
    sys.stdout.write(summary)
    for a in annotations:
        print(a)
    if args.summary:
        with open(args.summary, "a", encoding="utf-8") as f:
            f.write(summary)
    return code


if __name__ == "__main__":
    sys.exit(main())
