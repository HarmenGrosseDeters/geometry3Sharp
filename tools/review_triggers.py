#!/usr/bin/env python3
"""Print the classes of docs/REVIEW_CHECKLIST.md whose diff trigger matches a diff.

    python3 tools/review_triggers.py origin/master...HEAD     # a git range (git diff <range>)
    git diff base...head | python3 tools/review_triggers.py - # a unified diff on stdin

The triggers are read from the checklist itself: each class heading ("## 5. Floating-point range ...") is followed by a
line `<!-- trigger: path=GLOB GLOB; add=REGEX -->` (or `<!-- trigger: always -->`). `path=` globs (fnmatch, `*` also
crosses `/`) match the changed file paths; `add=` / `del=` are regexes matched against each added / removed line of a code file (.cs, .py, .csproj, .yml ...). A trigger is a hint read
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


def parse_checklist(text):
    """-> [(number, title, always, [globs], add regex or None, del regex or None)] in file order, for each class that has a trigger line"""
    classes = []
    current = None
    for line in text.split("\n"):
        h = HEADING.match(line)
        if h:
            current = (h.group(1), h.group(2))
            continue
        t = TRIGGER.match(line)
        if t and current:
            always, globs, rx, rxdel = False, [], None, None
            for part in t.group(1).split("; "):
                part = part.strip()
                if part == "always":
                    always = True
                elif part.startswith("path="):
                    globs = part[5:].split()
                elif part.startswith("add="):
                    rx = re.compile(part[4:])
                elif part.startswith("del="):
                    rxdel = re.compile(part[4:])
            classes.append((current[0], current[1], always, globs, rx, rxdel))
            current = None
    return classes


def parse_diff(diff):
    """-> (changed paths, added lines, removed lines) of a unified diff"""
    paths, added, removed = [], [], []
    code = False  # added lines count only in code files (not in docs or data: prose would match the patterns)
    for line in diff.split("\n"):
        if line.startswith("+++ "):
            p = line[4:].strip()
            code = p != "/dev/null" and p.endswith(CODE)
            if p != "/dev/null":
                paths.append(p[2:] if p.startswith("b/") else p)
        elif line.startswith("--- "):
            p = line[4:].strip()
            if p != "/dev/null" and p.startswith("a/"):
                paths.append(p[2:])
        elif code and line.startswith("+") and not line.startswith("+++"):
            added.append(line[1:])
        elif code and line.startswith("-") and not line.startswith("---"):
            removed.append(line[1:])
    return sorted(set(paths)), added, removed


def matches(cls, paths, added, removed):
    """-> why the class matches ('' if it does not)"""
    _, _, always, globs, rx, rxdel = cls
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
    if rxdel:
        for a in removed:
            if rxdel.search(a):
                return "removed: " + a.strip()[:60]
    return ""


def review_classes(diff, text):
    paths, added, removed = parse_diff(diff)
    hits, skipped = [], []
    for cls in parse_checklist(text):
        why = matches(cls, paths, added, removed)
        (hits if why else skipped).append((cls, why))
    return hits, skipped


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2
    if argv[1] == "-":
        diff = sys.stdin.read()
    else:
        diff = subprocess.run(["git", "diff", argv[1]], cwd=ROOT, capture_output=True, text=True, check=True).stdout
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
