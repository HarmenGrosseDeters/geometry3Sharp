#!/usr/bin/env python3
"""Run the test methods a branch adds against the library of its base (docs/QA.md 4a item 1).

A test added with a change should fail on the old code: otherwise it may not test the change at all (a vacuous
test). This script

  1. builds the test projects (tests/geometry3Sharp.Tests; tests/geometry3Sharp.Clipper2.Tests when the change
     touches it) at the head and at the merge base and lists their test methods; the new tests are the methods of
     the head that the base does not have,
  2. builds the head's test projects against the base's library: in a worktree of the head, every changed library
     file is reset to the merge base (added ones are deleted). Library files are all paths outside tests/,
     benchmarks/, docs/, tools/, .github/ and the Markdown files at the root. A changed test file that does not
     compile against the base library (it uses new API) is reset to the base too; if no changed file can be
     blamed, the deleted test files are restored, then all changed test files are reset. Their new tests count as
     "does not compile",
  3. runs the new tests there and reports for each: fails, passes, skipped, crash or hang (the test run was aborted
     while it ran), no result (the run failed for another reason) or does not compile.

With --require-failing (CI: PRs with the label `bugfix`) at least one new test must fail, crash or hang on the base.
Without it the report is informational: the exit code is 0, also when the script itself cannot run (the reason goes
into the report).

Usage (from the repository root, with the .NET 10 SDK):
    python3 tools/new_tests_on_base.py --base origin/master [--head HEAD] [--require-failing]
        [--work <dir outside the repository>] [--summary <markdown file>]
"""

import argparse
import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

# the test projects whose new tests are compared (a project missing at the base or the head is skipped): the new tests of
# the Clipper2 addon run against the addon of the base like those of the library against the library of the base
TEST_PROJECTS = ("tests/geometry3Sharp.Tests", "tests/geometry3Sharp.Clipper2.Tests")
FRAMEWORK = "net10.0"
# paths that are not part of the library; everything else that changed is reset to the base
NON_LIBRARY_PREFIXES = ("tests/", "benchmarks/", "docs/", "tools/", ".github/")
# the test sources a compile error may be blamed on (reset to the base, or deleted if new)
TEST_SOURCE_PREFIXES = ("tests/", "docs/examples/")
MAX_BUILD_ATTEMPTS = 6
# tests run one by one after an aborted run, to find the one that crashed or hung
MAX_SINGLE_RUNS = 40
COUNTS_AS_FAILING = ("fails", "crash or hang")


class CannotRun(Exception):
    """the comparison cannot be made (a build of the base or the head fails, ...)"""


# ---- pure helpers (unit tested in tools/test_new_tests_on_base.py) ----

def is_library_path(path):
    """a repository-relative path that belongs to the library (reset to the base)"""
    if path.startswith(NON_LIBRARY_PREFIXES):
        return False
    if "/" not in path and path.lower().endswith(".md"):
        return False
    return True


def parse_name_status_z(out):
    """`git diff --name-status --no-renames -z`: path -> status letter (A, M, D, T, ...)"""
    parts = out.split("\0")
    changes = {}
    i = 0
    while i + 1 < len(parts):
        status, path = parts[i], parts[i + 1]
        if not status:
            break
        changes[path] = status[0]
        i += 2
    return changes


def library_changes(changes):
    """(paths to reset to the base, paths to delete) of the changed library files"""
    reset = sorted(p for p, s in changes.items() if is_library_path(p) and s != "A")
    delete = sorted(p for p, s in changes.items() if is_library_path(p) and s == "A")
    return reset, delete


def test_side_changes(changes):
    """the changed (also deleted) test sources: the fallback when a compile error cannot be blamed on a file"""
    return sorted(p for p in changes if p.startswith(TEST_SOURCE_PREFIXES))


_NAME = re.compile(r"^[A-Za-z_]\w*([.+][A-Za-z_]\w*)+$")


def method_of(test_name):
    """the method of a test (display) name: 'Ns.Class.Method(x: 1)' -> 'Ns.Class.Method'; a generic method
    'Ns.C.M<Double>(x: 1)' -> 'Ns.C.M'"""
    i = test_name.find("(")
    name = (test_name[:i] if i >= 0 else test_name).strip()
    if name.endswith(">") and "<" in name:
        name = name[:name.index("<")]
    return name


def is_method_name(name):
    """a fully qualified method name (not a DisplayName such as 'clamp works')"""
    return bool(_NAME.match(name))


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


def test_filter(tests):
    """a vstest filter that selects exactly these methods"""
    return "|".join("FullyQualifiedName=" + re.sub(r"([\\()&|=!~])", r"\\\1", t) for t in sorted(tests))


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
    """(test method -> list of outcomes ('Passed', 'Failed', 'NotExecuted', ...), outcome of the run) of a .trx"""
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    root = ET.fromstring(text)
    results = {}
    for r in root.iterfind(".//t:Results/t:UnitTestResult", ns):
        results.setdefault(method_of(r.get("testName", "")), []).append(r.get("outcome", ""))
    summary = root.find(".//t:ResultSummary", ns)
    run_outcome = summary.get("outcome", "") if summary is not None else ""
    return results, run_outcome


def verdict_of(outcomes, aborted_by_blame):
    """the verdict of one test from its outcomes; aborted_by_blame: the run was killed by a crash or a hang"""
    if not outcomes:
        return "crash or hang" if aborted_by_blame else "no result"
    if any(x not in ("Passed", "NotExecuted") for x in outcomes):
        return "fails"
    if all(x == "NotExecuted" for x in outcomes):
        return "skipped"
    return "passes"


def classify(new_tests, compiled, outcomes, aborted_by_blame=False):
    """per new test: fails, passes, skipped, crash or hang, no result, does not compile, or not a method name"""
    verdict = {}
    for t in sorted(new_tests):
        if not is_method_name(t):
            verdict[t] = "not a method name (DisplayName), not run"
        elif t not in compiled:
            verdict[t] = "does not compile"
        else:
            verdict[t] = verdict_of(outcomes.get(t, []), aborted_by_blame)
    return verdict


def report(verdict, dropped, require_failing, problem=None):
    """(markdown report, exit code)"""
    lines = ["## New tests against the base library", ""]
    if problem:
        lines += ["**The comparison could not be made:** " + problem, ""]
    failing = [t for t, v in verdict.items() if v in COUNTS_AS_FAILING]
    if not verdict:
        if not problem:
            lines.append("The branch adds no test methods to `%s`." % "`, `".join(TEST_PROJECTS))
    else:
        def count(v):
            return sum(x == v for x in verdict.values())
        others = len(verdict) - len(failing) - count("passes") - count("skipped") - count("does not compile")
        lines.append("%d new test method(s): %d fail on the base (or crash / hang), %d pass, %d skipped, "
                     "%d do not compile on it, %d without a result."
                     % (len(verdict), len(failing), count("passes"), count("skipped"), count("does not compile"),
                        others))
        lines += ["", "| Test | On the base |", "|---|---|"]
        for t, v in verdict.items():
            lines.append("| `%s` | %s |" % (t, v))
        if count("passes"):
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
                          "without it (skipped tests, tests that do not compile on the base and runs without a "
                          "result do not count)."]
            code = 1
    return "\n".join(lines) + "\n", code


# ---- git and dotnet ----

def run(cmd, cwd=None, check=True):
    p = subprocess.run(cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if check and p.returncode != 0:
        sys.stdout.write(p.stdout[-20000:])
        raise CannotRun("`%s` failed (exit %d)" % (" ".join(cmd), p.returncode))
    return p


def git(*args, cwd=None, check=True):
    return run(["git"] + list(args), cwd=cwd, check=check).stdout


PRIMARY = TEST_PROJECTS[0]
# sources a test project compiles from outside its directory (a change there can add tests to it)
LINKED_SOURCES = {
    "tests/geometry3Sharp.Clipper2.Tests": ("tests/geometry3Sharp.Tests/Harness/Invariance.cs", "docs/examples/Clipper2/"),
}


def projects_of(tree):
    """the test projects that exist in the tree (with a project file)"""
    return [p for p in TEST_PROJECTS if glob.glob(os.path.join(tree, p, "*.csproj"))]


def active_projects(changes, tree):
    """the projects to compare: the library tests always, another project only when the change touches its directory or
    a source it links (a library-only change cannot add tests to it, and building it costs minutes)"""
    result = []
    for project in projects_of(tree):
        touched = any(path.startswith(project + "/") or path.startswith(LINKED_SOURCES.get(project, ())) for path in changes)
        if project == PRIMARY or touched:
            result.append(project)
    return result


class BuildResult:
    def __init__(self, returncode, stdout):
        self.returncode, self.stdout = returncode, stdout


def build(tree, projects):
    """build the test projects; the first failure is the result, the output of all builds is kept"""
    code, out = 0, ""
    for project in projects:
        p = run(["dotnet", "build", os.path.join(tree, project), "-c", "Release", "-f", FRAMEWORK, "-nologo"],
                check=False)
        out += p.stdout
        code = code or p.returncode
    return BuildResult(code, out)


def list_tests(tree, project):
    if not os.path.isdir(os.path.join(tree, project)):
        return set()
    p = run(["dotnet", "test", os.path.join(tree, project), "-c", "Release", "-f", FRAMEWORK, "--no-build",
             "--list-tests", "-nologo"])
    return parse_list_tests(p.stdout)


def reset_to_base(tree, base, paths, changes):
    """reset paths of the tree to the base: added files are deleted, others checked out from the base"""
    for path in paths:
        if changes.get(path) == "A":
            full = os.path.join(tree, path)
            if os.path.lexists(full):
                os.remove(full)
        else:
            git("checkout", base, "--", path, cwd=tree)


def run_tests(tree, project, tests, results):
    """run the tests of one project; (method -> outcomes, the run was killed by a crash or hang, problem or None)"""
    shutil.rmtree(results, ignore_errors=True)
    p = run(["dotnet", "test", os.path.join(tree, project), "-c", "Release", "-f", FRAMEWORK, "--no-build",
             "-nologo", "--filter", test_filter(tests), "--logger", "trx;LogFileName=base.trx",
             "--results-directory", results, "--blame-hang-timeout", "10m", "--blame-hang-dump-type", "none"],
            check=False)
    print(p.stdout[-20000:])
    blamed = bool(glob.glob(os.path.join(results, "**", "Sequence_*.xml"), recursive=True))
    trx = os.path.join(results, "base.trx")
    if not os.path.exists(trx):
        return {}, blamed, None if blamed else "no test results (exit %d)" % p.returncode
    with open(trx, encoding="utf-8-sig") as f:
        outcomes, run_outcome = parse_trx(f.read())
    problem = None
    if not outcomes and not blamed:
        problem = "the test run gave no results (run outcome %s, exit %d)" % (run_outcome or "?", p.returncode)
    return outcomes, blamed, problem


def compare(repo, base, head, work):
    """(verdict, dropped files)"""
    base_tree, hybrid = os.path.join(work, "base"), os.path.join(work, "hybrid")
    git("worktree", "add", "--detach", base_tree, base, cwd=repo)
    git("worktree", "add", "--detach", hybrid, head, cwd=repo)
    changes = parse_name_status_z(git("diff", "--name-status", "--no-renames", "-z", base, head, cwd=repo))
    active = active_projects(changes, hybrid)
    for tree, what in ((base_tree, "base"), (hybrid, "head")):
        # (a project besides the library tests that does not build is left out: it must not take the gate down)
        for project in list(active):
            b = build(tree, [project])
            if b.returncode != 0:
                sys.stdout.write(b.stdout[-20000:])
                if project == PRIMARY:
                    raise CannotRun("the tests of the %s do not build" % what)
                print("::warning::new tests on base: %s does not build at the %s, its tests are not compared" % (project, what))
                active.remove(project)
    new_by_project = {project: list_tests(hybrid, project) - list_tests(base_tree, project) for project in active}
    new_tests = set().union(*new_by_project.values()) if new_by_project else set()
    print("new test methods: %d (%s)" % (len(new_tests), ", ".join("%s %d" % (p, len(t)) for p, t in new_by_project.items())))
    if not new_tests:
        return {}, set()
    reset, delete = library_changes(changes)
    print("library files reset to the base: %d changed, %d added" % (len(reset), len(delete)))
    reset_to_base(hybrid, base, reset + delete, changes)
    dropped = set()
    for attempt in range(MAX_BUILD_ATTEMPTS):
        b = build(hybrid, active)
        if b.returncode == 0:
            break
        blame = {f for f in error_files(b.stdout, hybrid)
                 if f.startswith(TEST_SOURCE_PREFIXES) and f in changes and f not in dropped}
        if not blame:
            # an unchanged test file broke: first restore the test files the change deleted (a helper moved into the
            # library), then all changed test files (a restore error, ...)
            blame = {f for f in test_side_changes(changes) if changes[f] == "D"} - dropped
            if not blame:
                blame = set(test_side_changes(changes)) - dropped
            if not blame:
                sys.stdout.write(b.stdout[-20000:])
                others = [p for p in active if p != PRIMARY]
                if others:
                    # (the other projects may be what does not build: compare the library tests alone)
                    print("::warning::new tests on base: left out of the comparison, it does not build against the base: " + ", ".join(others))
                    active = [PRIMARY]
                    continue
                raise CannotRun("the head's tests do not build against the base library, also with all changed "
                                "test files reset to the base")
            print("errors not in a changed test file")
        print("not compiling against the base library, reset: " + ", ".join(sorted(blame)))
        reset_to_base(hybrid, base, sorted(blame), changes)
        dropped |= blame
    else:
        sys.stdout.write(b.stdout[-20000:])
        raise CannotRun("the head's tests do not build against the base library")
    results = os.path.join(work, "results")
    verdict, single_runs = {}, 0
    for project in active:
        new_p = new_by_project[project]
        compiled = {t for t in list_tests(hybrid, project) & new_p if is_method_name(t)}
        if not compiled:
            verdict.update(classify(new_p, compiled, {}))
            continue
        outcomes, blamed, problem = run_tests(hybrid, project, compiled, results)
        if problem:
            raise CannotRun(problem)
        missing = sorted(t for t in compiled if t not in outcomes)
        if not (blamed and len(missing) > 1):
            verdict.update(classify(new_p, compiled, outcomes, aborted_by_blame=blamed))
            continue
        single_runs += len(missing)
        if single_runs > MAX_SINGLE_RUNS:
            raise CannotRun("the test run was aborted (crash or hang) with %d new tests without a result" % len(missing))
        # one test crashed or hung and took the results of the others with it: run those one by one
        print("the run of %s was aborted; running the %d tests without a result one by one" % (project, len(missing)))
        crashed = set()
        for t in missing:
            o, b, _ = run_tests(hybrid, project, {t}, results)
            outcomes.update(o)
            if t not in o and b:
                crashed.add(t)
        part = classify(new_p, compiled, outcomes, aborted_by_blame=False)
        for t in crashed:
            part[t] = "crash or hang"
        verdict.update(part)
    return verdict, dropped


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--base", required=True, help="base branch or commit (the merge base with --head is used)")
    ap.add_argument("--head", default="HEAD")
    ap.add_argument("--require-failing", action="store_true",
                    help="fail unless at least one new test fails on the base (bug fixes)")
    ap.add_argument("--work", help="directory for the worktrees, outside the repository (default: a temporary one)")
    ap.add_argument("--summary", help="also append the markdown report to this file (GITHUB_STEP_SUMMARY)")
    a = ap.parse_args(argv)

    verdict, dropped, problem = {}, set(), None
    repo = work = None
    try:
        repo = git("rev-parse", "--show-toplevel").strip()
        head = git("rev-parse", a.head, cwd=repo).strip()
        base = git("merge-base", a.base, head, cwd=repo).strip()
        work = os.path.realpath(a.work or tempfile.mkdtemp(prefix="new-tests-on-base-"))
        if (work + os.sep).startswith(os.path.realpath(repo) + os.sep):
            # (the library project compiles every .cs below the repository root)
            work = None
            raise CannotRun("--work must be outside the repository")
        os.makedirs(work, exist_ok=True)
        for t in ("base", "hybrid"):
            git("worktree", "remove", "--force", os.path.join(work, t), cwd=repo, check=False)
            shutil.rmtree(os.path.join(work, t), ignore_errors=True)
        git("worktree", "prune", cwd=repo, check=False)
        print("head %s, merge base %s" % (head[:12], base[:12]))
        verdict, dropped = compare(repo, base, head, work)
    except CannotRun as e:
        problem = str(e)
    finally:
        if repo and work:
            for t in ("base", "hybrid"):
                git("worktree", "remove", "--force", os.path.join(work, t), cwd=repo, check=False)
            git("worktree", "prune", cwd=repo, check=False)
    text, code = report(verdict, dropped, a.require_failing, problem)
    print(text)
    if problem:
        print("::warning::new tests on base: " + problem)
    if a.summary:
        with open(a.summary, "a", encoding="utf-8") as f:
            f.write(text)
    return code


if __name__ == "__main__":
    sys.exit(main())
