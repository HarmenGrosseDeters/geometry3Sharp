#!/usr/bin/env python3
"""Print the classes of docs/REVIEW_CHECKLIST.md whose diff trigger matches a diff.

    python3 tools/review_triggers.py origin/master...HEAD     # a git range (git diff <range>)
    git diff base...head | python3 tools/review_triggers.py - # a unified diff on stdin

The triggers are read from the checklist itself: each class heading ("## 5. Floating-point range ...") is followed by a
line `<!-- trigger: path=GLOB GLOB; add=REGEX -->` (or `<!-- trigger: always -->`). `path=` globs (fnmatch, `*` also
crosses `/`) match the changed file paths; `add=` / `del=` are regexes matched against each added / removed line of a code file (.cs, .py, .csproj, .yml ...), comment lines excluded; `ladd=` looks only at added lines outside tests/ and benchmarks/. A pattern must not contain "; ". A trigger is a hint read
from the diff text, not a proof that a class is irrelevant. Exit code 0 always (it is a helper, not a gate).
"""
import fnmatch
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHECKLIST = os.path.join(ROOT, "docs", "REVIEW_CHECKLIST.md")
HEADING = re.compile(r"^## (\d+a?)\. (.*)$")
CODE = (".cs", ".py", ".csproj", ".props", ".targets", ".yml", ".sh")
TRIGGER = re.compile(r"^<!-- trigger: (.*) -->$")


def compile_rx(cls, rx):
    try:
        return re.compile(rx)
    except re.error as e:
        raise ValueError("class %s: bad trigger regex %r: %s" % (cls[0], rx, e))


def parse_checklist(text):
    """-> [(number, title, always, [globs], add regex or None, del regex or None, library-add regex or None)] in file order, for each class that has a trigger line"""
    classes = []
    current = None
    for line in text.split("\n"):
        h = HEADING.match(line)
        if h:
            current = (h.group(1), h.group(2))
            continue
        t = TRIGGER.match(line)
        if t and current:
            always, globs, rx, rxdel, rxlib = False, [], None, None, None
            for part in t.group(1).split("; "):
                part = part.strip()
                if part == "always":
                    always = True
                elif part.startswith("path="):
                    globs = part[5:].split()
                elif part.startswith("add="):
                    rx = compile_rx(current, part[4:])
                elif part.startswith("ladd="):
                    rxlib = compile_rx(current, part[5:])
                elif part.startswith("del="):
                    rxdel = compile_rx(current, part[4:])
            classes.append((current[0], current[1], always, globs, rx, rxdel, rxlib))
            current = None
    return classes


HEADER = re.compile(r'^diff --git (?:"?a/(.*?)"?) (?:"?b/(.*?)"?)$')
# lines of these trees are not library lines: `ladd=` (public API, ...) does not look at them
NOT_LIBRARY = ("tests/", "benchmarks/")


def parse_diff(diff):
    """-> (changed paths, added lines, removed lines, added library lines) of a unified diff. Paths come from the `diff --git` headers (renames,
    mode changes and binary files have no hunks); lines are read by hunk, so a line that starts with `++ ` or `-- ` is a line."""
    paths, added, removed, lib = [], [], [], []
    code = lib_file = False  # lines count only in code files (not in docs or data: prose would match the patterns)
    in_hunk = False
    for line in diff.split("\n"):
        h = HEADER.match(line)
        if h:
            old, new = h.group(1), h.group(2)
            paths += [old, new]
            code = new.endswith(CODE)
            lib_file = code and not new.startswith(NOT_LIBRARY)
            in_hunk = False
        elif line.startswith("@@"):
            in_hunk = True
        elif line.startswith("--- ") and not in_hunk:
            p = line[4:].strip()
            if p.startswith("a/"):
                paths.append(p[2:])
        elif line.startswith("+++ ") and not in_hunk:
            p = line[4:].strip()
            if p != "/dev/null":
                paths.append(p[2:] if p.startswith("b/") else p)
            if not h:
                code = p != "/dev/null" and p.endswith(CODE)
                lib_file = code and not p[2:].startswith(NOT_LIBRARY)
        elif in_hunk and code and line[:1] in ("+", "-") and not is_comment(line[1:]):
            (added if line[0] == "+" else removed).append(line[1:])
            if lib_file and line[0] == "+":
                lib.append(line[1:])
    return sorted(set(paths)), added, removed, lib


def is_comment(text):
    t = text.strip()
    return t.startswith("//") or t.startswith("#") or t.startswith("*") or t.startswith("/*")


def matches(cls, paths, added, removed, lib):
    """-> why the class matches ('' if it does not)"""
    _, _, always, globs, rx, rxdel, rxlib = cls
    if always:
        return "always"
    for g in globs:
        for p in paths:
            if fnmatch.fnmatchcase(p, g):
                return "path " + p
    if rx:
        for a in added:
            if rx.search(a):
                return "added: " + a.strip()[:60]
    if rxlib:
        for a in lib:
            if rxlib.search(a):
                return "added (library): " + a.strip()[:60]
    if rxdel:
        for a in removed:
            if rxdel.search(a):
                return "removed: " + a.strip()[:60]
    return ""


def review_classes(diff, text):
    paths, added, removed, lib = parse_diff(diff)
    hits, skipped = [], []
    for cls in parse_checklist(text):
        why = matches(cls, paths, added, removed, lib)
        (hits if why else skipped).append((cls, why))
    return hits, skipped


def main(argv):
    # (a Windows console or pipe has a legacy code page: paths and diff lines are UTF-8, and a character it cannot show must not stop the run)
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    if len(argv) != 2:
        print(__doc__)
        return 2
    if argv[1] == "-":
        diff = sys.stdin.buffer.read().decode("utf-8", errors="replace")
    else:
        try:
            diff = subprocess.run(["git", "-c", "core.quotepath=false", "diff", "--no-ext-diff", "--src-prefix=a/", "--dst-prefix=b/", argv[1]], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", check=True).stdout
        except subprocess.CalledProcessError as e:
            print("git diff failed: " + e.stderr.strip())
            return 2
    with open(CHECKLIST, encoding="utf-8") as f:
        text = f.read()
    hits, skipped = review_classes(diff, text)
    print("Check these classes of docs/REVIEW_CHECKLIST.md:")
    for (n, title, *_), why in hits:
        print("  %s. %s   [%s]" % (n, title, why))
    print("Skipped (no trigger matched; name them in the review): " + ", ".join(c[0] for c, _ in skipped))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
