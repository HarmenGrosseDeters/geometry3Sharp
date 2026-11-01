#!/usr/bin/env python3
"""Run the test methods a branch adds against the library of its base (docs/QA.md 4a item 1).

A test added with a change should fail on the old code: otherwise it may not test the change at all (a vacuous
test). This script

  1. builds tests/geometry3Sharp.Tests at the head and at the merge base and lists their test methods; the new tests
     are the methods of the head that the base does not have,
  2. builds the head's test project against the base's library: in a worktree of the head, every changed library
     file is reset to the merge base (added ones are deleted). Library files are all paths outside tests/,
     benchmarks/, docs/, tools/, .github/ and the Markdown files at the root. A test file that does not compile
     against the base library (it uses new API) is reset to the base too, and its new tests count as "does not
     compile on the base",
  3. runs the new tests there and reports for each whether it fails on the base.

With --require-failing (CI: PRs with the label `bugfix`) at least one new test must fail (or crash / hang) on the
base; otherwise the report is informational and the exit code is 0.

Usage (from the repository root, with the .NET 10 SDK):
    python3 tools/new_tests_on_base.py --base origin/master [--head HEAD] [--require-failing]
        [--work <dir>] [--summary <markdown file>]
"""

import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

TEST_PROJECT = "tests/geometry3Sharp.Tests"
FRAMEWORK = "net10.0"
# paths that are not part of the library; everything else that changed is reset to the base
NON_LIBRARY_PREFIXES = ("tests/", "benchmarks/", "docs/", "tools/", ".github/")
# the test sources a compile error may be blamed on (reset to the base, or deleted if new)
TEST_SOURCE_PREFIXES = ("tests/", "docs/examples/")
MAX_BUILD_ATTEMPTS = 6


# ---- pure helpers (unit tested in tools/test_new_tests_on_base.py) ----

def is_library_path(path):
    """a repository-relative path that belongs to the library (reset to the base)"""
    if path.startswith(NON_LIBRARY_PREFIXES):
        return False
    if "/" not in path and path.lower().endswith(".md"):
        return False
    return True


def library_changes(name_status):
    """from `git diff --name-status --no-renames base head`: (paths to reset to the base, paths to delete)"""
    reset, delete = [], []
    for line in name_status.splitlines():
        parts = line.split("\t")
        if len(parts) < 2:
            continue
        status, path = parts[0], parts[-1]
        if not is_library_path(path):
            continue
        (delete if status.startswith("A") else reset).append(path)
    return reset, delete


def method_of(test_name):
    """the method of a test (display) name: 'Ns.Class.Method(x: 1)' -> 'Ns.Class.Method'"""
    i = test_name.find("(")
    return (test_name[:i] if i >= 0 else test_name).strip()


def parse_list_tests(output):
    """the test methods in the output of `dotnet test --list-tests`"""
    methods, started = set(), False
    for line in output.splitlines():
        if "The following Tests are available" in line:
            started = True
            continue
        if started and line.startswith("    ") and line.strip():
            methods.add(method_of(line.strip()))
    return methods


_ERROR = re.compile(r"^\s*(.+?)\(\d+,\d+\): error [A-Z]+\d+:", re.M)


def error_files(build_output, root):
    """repository-relative paths of the files with compile errors"""
    root = os.path.realpath(root)
    files = set()
    for m in _ERROR.finditer(build_output):
        p = os.path.realpath(m.group(1).strip())
        if p.startswith(root + os.sep):
            files.add(os.path.relpath(p, root).replace(os.sep, "/"))
    return files


def parse_trx(text):
    """test method -> list of outcomes ('Passed', 'Failed', ...) from a .trx file"""
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    root = ET.fromstring(text)
    results = {}
    for r in root.iterfind(".//t:Results/t:UnitTestResult", ns):
        results.setdefault(method_of(r.get("testName", "")), []).append(r.get("outcome", ""))
    return results


def classify(new_tests, compiled, outcomes):
    """per new test: 'fails', 'passes', 'no result' (crash or hang on the base) or 'does not compile'"""
    verdict = {}
    for t in sorted(new_tests):
        if t not in compiled:
            verdict[t] = "does not compile"
            continue
        o = outcomes.get(t, [])
        if not o:
            verdict[t] = "no result"
        elif any(x != "Passed" and x != "NotExecuted" for x in o):
            verdict[t] = "fails"
        elif all(x == "NotExecuted" for x in o):
            verdict[t] = "no result"
        else:
            verdict[t] = "passes"
    return verdict


def report(verdict, dropped, require_failing):
    """(markdown report, exit code)"""
    failing = [t for t, v in verdict.items() if v in ("fails", "no result")]
    lines = ["## New tests against the base library", ""]
    if not verdict:
        lines.append("The branch adds no test methods to `%s`." % TEST_PROJECT)
    else:
        lines.append("%d new test method(s); %d fail on the base, %d pass on the base, %d do not compile on it."
                     % (len(verdict), len(failing), sum(v == "passes" for v in verdict.values()),
                        sum(v == "does not compile" for v in verdict.values())))
        lines += ["", "| Test | On the base |", "|---|---|"]
        for t, v in verdict.items():
            lines.append("| `%s` | %s |" % (t, v))
        if any(v == "passes" for v in verdict.values()):
            lines += ["", "A new test that passes on the base does not show the change; check that it is not vacuous "
                          "(a refactoring or a new feature with a passing regression test is fine)."]
    if dropped:
        lines += ["", "Reset to the base because they do not compile against the base library: "
                  + ", ".join("`%s`" % d for d in sorted(dropped)) + "."]
    code = 0
    if require_failing:
        if failing:
            lines += ["", "Label `bugfix`: at least one new test fails on the base. OK."]
        else:
            lines += ["", "**Label `bugfix`: no new test fails on the base.** A bug fix needs a test that fails "
                          "without it."]
            code = 1
    return "\n".join(lines) + "\n", code


# ---- git and dotnet ----

def run(cmd, cwd=None, check=True):
    p = subprocess.run(cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if check and p.returncode != 0:
        sys.stdout.write(p.stdout)
        raise SystemExit("failed (%d): %s" % (p.returncode, " ".join(cmd)))
    return p


def git(*args, cwd=None, check=True):
    return run(["git"] + list(args), cwd=cwd, check=check).stdout


def build(tree):
    return run(["dotnet", "build", os.path.join(tree, TEST_PROJECT), "-c", "Release", "-f", FRAMEWORK, "-nologo"],
               check=False)


def list_tests(tree):
    p = run(["dotnet", "test", os.path.join(tree, TEST_PROJECT), "-c", "Release", "-f", FRAMEWORK, "--no-build",
             "--list-tests", "-nologo"])
    return parse_list_tests(p.stdout)


def reset_to_base(tree, base, paths, added):
    for path in paths:
        if path in added:
            full = os.path.join(tree, path)
            if os.path.exists(full):
                os.remove(full)
        else:
            git("checkout", base, "--", path, cwd=tree)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--base", required=True, help="base branch or commit (the merge base with --head is used)")
    ap.add_argument("--head", default="HEAD")
    ap.add_argument("--require-failing", action="store_true",
                    help="fail unless at least one new test fails on the base (bug fixes)")
    ap.add_argument("--work", help="directory for the worktrees (default: a temporary one)")
    ap.add_argument("--summary", help="also append the markdown report to this file (GITHUB_STEP_SUMMARY)")
    a = ap.parse_args(argv)

    repo = git("rev-parse", "--show-toplevel").strip()
    head = git("rev-parse", a.head, cwd=repo).strip()
    base = git("merge-base", a.base, head, cwd=repo).strip()
    work = a.work or tempfile.mkdtemp(prefix="new-tests-on-base-")
    os.makedirs(work, exist_ok=True)
    base_tree, hybrid = os.path.join(work, "base"), os.path.join(work, "hybrid")
    for t in (base_tree, hybrid):
        if os.path.exists(t):
            git("worktree", "remove", "--force", t, cwd=repo, check=False)
            shutil.rmtree(t, ignore_errors=True)
    git("worktree", "add", "--detach", base_tree, base, cwd=repo)
    git("worktree", "add", "--detach", hybrid, head, cwd=repo)
    print("head %s, merge base %s" % (head[:12], base[:12]))
    try:
        for tree, what in ((base_tree, "base"), (hybrid, "head")):
            b = build(tree)
            if b.returncode != 0:
                sys.stdout.write(b.stdout)
                raise SystemExit("the %s does not build" % what)
        new_tests = list_tests(hybrid) - list_tests(base_tree)
        print("new test methods: %d" % len(new_tests))
        verdict, dropped = {}, set()
        if new_tests:
            name_status = git("diff", "--name-status", "--no-renames", base, head, cwd=repo)
            reset, delete = library_changes(name_status)
            print("library files reset to the base: %d changed, %d added" % (len(reset), len(delete)))
            reset_to_base(hybrid, base, reset + delete, set(delete))
            changed = {}
            for line in name_status.splitlines():
                parts = line.split("\t")
                if len(parts) >= 2:
                    changed[parts[-1]] = parts[0]
            for attempt in range(MAX_BUILD_ATTEMPTS):
                b = build(hybrid)
                if b.returncode == 0:
                    break
                blame = {f for f in error_files(b.stdout, hybrid)
                         if f.startswith(TEST_SOURCE_PREFIXES) and f in changed and f not in dropped}
                if not blame:
                    sys.stdout.write(b.stdout)
                    raise SystemExit("the head's tests do not build against the base library, and no changed test "
                                     "file is to blame")
                print("not compiling against the base library, reset: " + ", ".join(sorted(blame)))
                reset_to_base(hybrid, base, sorted(blame), {f for f in blame if changed[f].startswith("A")})
                dropped |= blame
            else:
                sys.stdout.write(b.stdout)
                raise SystemExit("the head's tests do not build against the base library")
            compiled = list_tests(hybrid) & new_tests
            outcomes = {}
            if compiled:
                results = os.path.join(work, "results")
                shutil.rmtree(results, ignore_errors=True)
                flt = "|".join("FullyQualifiedName=" + t for t in sorted(compiled))
                p = run(["dotnet", "test", os.path.join(hybrid, TEST_PROJECT), "-c", "Release", "-f", FRAMEWORK,
                         "--no-build", "-nologo", "--filter", flt, "--logger", "trx;LogFileName=base.trx",
                         "--results-directory", results, "--blame-hang-timeout", "10m"], check=False)
                print(p.stdout[-20000:])
                trx = os.path.join(results, "base.trx")
                if os.path.exists(trx):
                    with open(trx, encoding="utf-8-sig") as f:
                        outcomes = parse_trx(f.read())
            verdict = classify(new_tests, compiled, outcomes)
        text, code = report(verdict, dropped, a.require_failing)
        print(text)
        if a.summary:
            with open(a.summary, "a", encoding="utf-8") as f:
                f.write(text)
        return code
    finally:
        for t in (base_tree, hybrid):
            git("worktree", "remove", "--force", t, cwd=repo, check=False)


if __name__ == "__main__":
    sys.exit(main())
