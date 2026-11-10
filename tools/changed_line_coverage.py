#!/usr/bin/env python3
"""Gate changed executable C# lines using the three Linux coverlet reports.

Run from the checkout used to collect coverage. Compare committed HEAD with its
merge base, never with the tip of a moving target branch. Exit 1 for uncovered
lines, 2 for missing/invalid evidence. See docs/QA.md section 2.6.

The coverage of CI is collected over the fast tier (Category!=Slow and !=Nightly). With
--slow-coverage-dir (the core suite run over Category=Slow, collected only when the fast tier
left changed lines uncovered) a changed line that only the Slow tests hit is reported as
"covered_only_by_slow" and does not fail the gate.
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path
import xml.etree.ElementTree as ET


SUITES = {"core": "geometry3Sharp", "cli": "g3cli", "clipper2": "geometry3Sharp.Clipper2"}
EXCLUDED = ("tests/", "benchmarks/", "docs/", "tools/", "addons/", "Properties/", ".github/")


def in_scope(path):
    return path.endswith(".cs") and (
        path.startswith(("tools/g3cli/", "addons/geometry3Sharp.Clipper2/"))
        or not path.startswith(EXCLUDED))


def git(root, *args):
    return subprocess.check_output(
        ["git", "-C", str(root), *args], text=True, encoding="utf-8")


def changed_lines(root, base, head):
    ancestor = git(root, "merge-base", base, head).strip()
    common = ("diff", "--no-ext-diff", "--no-textconv", "--no-renames")
    paths = git(root, *common, "--name-only", "--diff-filter=AMT", "-z", ancestor, head).split("\0")
    result = {}
    for path in paths:
        if not in_scope(path):
            continue
        patch = git(root, *common, "--unified=0", ancestor, head, "--", path)
        lines = set()
        for start, count in re.findall(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@", patch, re.M):
            start = int(start)
            lines.update(range(start, start + (int(count) if count else 1)))
        if lines:
            result[path] = lines
    return ancestor, result


def coverage_path(filename, sources, root):
    # Do not match by basename/suffix: two unrelated files can share a name.
    filename = filename.replace("\\", "/")
    if "://" in filename:
        raise ValueError("SourceLink filenames are unsupported; set UseSourceLink=false")
    name = Path(filename)
    candidates = [name] if name.is_absolute() else [root / name] + [Path(s) / name for s in sources]
    matches = set()
    for candidate in candidates:
        try:
            relative = candidate.resolve().relative_to(root).as_posix()
        except ValueError:
            continue
        if (root / relative).is_file():
            matches.add(relative)
    if len(matches) != 1:
        raise ValueError(f"coverage filename is absent or ambiguous in this checkout: {filename}")
    return matches.pop()


def read_coverage(directory, root, suites=SUITES):
    merged = {}
    reports = []
    for suite, assembly in suites.items():
        # VSTest writes <suite>/<attachment-id>/coverage.cobertura.xml. A TRX
        # logger additionally copies it below <suite>/<run>/In/<host>/; that
        # copy is not a second collection and must not be merged as evidence.
        # A suite run in shards (core) has <suite>/<shard>/<attachment-id>/coverage.cobertura.xml for every shard.
        files = sorted((directory / suite).glob("*/coverage.cobertura.xml")) or sorted((directory / suite).glob("*/*/coverage.cobertura.xml"))
        if not files:
            raise ValueError(f"{suite}: expected a coverlet report, found none (clean results first)")
        for file in files:
            _read_report(file, suite, assembly, suites, directory, root, merged, reports)
    return merged, reports


def _read_report(file, suite, assembly, suites, directory, root, merged, reports):
    report = ET.parse(file).getroot()
    if report.tag != "coverage":
        raise ValueError(f"{suite}: expected Cobertura coverage root")
    sources = [node.text for node in report.findall("./sources/source") if node.text]
    packages = report.findall("./packages/package")
    if not any(p.get("name") == assembly and p.findall("./classes/class/lines/line") for p in packages):
        raise ValueError(f"{suite}: no executable lines for expected assembly {assembly}")
    reports.append(str(file.relative_to(directory)))
    for package in packages:
        if package.get("name") not in suites.values():
            raise ValueError(f"unexpected instrumented assembly: {package.get('name')}")
        for cls in package.findall("./classes/class"):
            class_lines = cls.findall("./lines/line")
            method_numbers = {int(n.attrib["number"]) for n in cls.findall("./methods/method/lines/line")}
            if method_numbers - {int(n.attrib["number"]) for n in class_lines}:
                raise ValueError("incomplete class-level line evidence")
            if not class_lines:
                continue
            path = coverage_path(cls.attrib["filename"], sources, root)
            hits = merged.setdefault(path, {})
            # Class-level lines avoid counting the duplicate method-level entries.
            for line in class_lines:
                number, count = int(line.attrib["number"]), int(line.attrib["hits"])
                if number < 1 or count < 0:
                    raise ValueError(f"invalid line/hit count in {path}")
                hits[number] = max(hits.get(number, 0), count)


def _is_ancestor(root, ancestor, descendant):
    return subprocess.run(["git", "merge-base", "--is-ancestor", ancestor, descendant], cwd=root).returncode == 0


def introducing_pr(root, commit, head, cache):
    """The pull request that brought `commit` into `head`: the first merge on head's first-parent line that
    contains it only through its second parent ("Merge pull request #N"), else a "(#N)" in its own subject
    (squash merge), else its short hash."""
    if commit not in cache:
        found = None
        rows = git(root, "log", "--first-parent", "--merges", "--reverse", "--format=%H %P\t%s", f"{commit}..{head}")
        for row in filter(None, rows.split("\n")):
            ids, subject = row.split("\t", 1)
            _, first, second = ids.split()[:3]
            if _is_ancestor(root, commit, second) and not _is_ancestor(root, commit, first):
                found = re.search(r"#(\d+)", subject)
                break
        found = found or re.search(r"\(#(\d+)\)", git(root, "log", "-n", "1", "--format=%s", commit))
        cache[commit] = f"#{found.group(1)}" if found else commit[:10]
    return cache[commit]


def attribute(root, head, uncovered):
    """{path: {pr: [lines]}} for the uncovered lines, by git blame at head."""
    cache, result = {}, {}
    for path, lines in uncovered.items():
        wanted, owner = set(lines), {}
        for row in git(root, "blame", "--porcelain", head, "--", path).split("\n"):
            match = re.match(r"^([0-9a-f]{40}) \d+ (\d+)", row)
            if match and int(match.group(2)) in wanted:
                owner[int(match.group(2))] = match.group(1)
        for number in sorted(owner):
            result.setdefault(path, {}).setdefault(introducing_pr(root, owner[number], head, cache), []).append(number)
    return result


def evaluate(changes, coverage, slow=None):
    uncovered, missing, only_slow = {}, [], {}
    executable = covered = 0
    for path, changed in sorted(changes.items()):
        if path not in coverage:
            missing.append(path)
            continue
        measured = changed & coverage[path].keys()
        missed = sorted(n for n in measured if coverage[path][n] == 0)
        executable += len(measured)
        if slow is not None and missed:
            # (hit by the Slow tests only: reported, not a failure)
            slow_hits = slow.get(path, {})
            slow_only = [n for n in missed if slow_hits.get(n, 0) > 0]
            if slow_only:
                only_slow[path] = slow_only
            missed = [n for n in missed if n not in slow_only]
        covered += len(measured) - len(missed)
        if missed:
            uncovered[path] = missed
    return {"changed_executable_lines": executable, "covered_lines": covered,
            "covered_only_by_slow": only_slow,
            "uncovered": uncovered, "missing_files": missing,
            "ok": not uncovered and not missing}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", default="HEAD")
    parser.add_argument("--coverage-dir", type=Path, required=True)
    parser.add_argument("--slow-coverage-dir", type=Path,
                        help="core/ below it holds the coverlet report of the Category=Slow run")
    parser.add_argument("--attribute", action="store_true",
                        help="name the pull request that introduced each uncovered line (scheduled runs)")
    parser.add_argument("--json", type=Path, required=True, help="machine-readable result")
    parser.add_argument("--summary", type=Path, help="append a Markdown CI summary")
    args = parser.parse_args(argv)
    result = {}
    code = 2
    try:
        root = Path(git(Path.cwd(), "rev-parse", "--show-toplevel").strip()).resolve()
        head = git(root, "rev-parse", "--verify", args.head + "^{commit}").strip()
        if head != git(root, "rev-parse", "HEAD").strip():
            raise ValueError("--head must be the checkout used to collect coverage")
        if git(root, "status", "--porcelain", "--untracked-files=no"):
            raise ValueError("tracked worktree changes: coverage must describe committed HEAD")
        ancestor, changes = changed_lines(root, args.base, head)
        coverage, reports = read_coverage(args.coverage_dir.resolve(), root)
        slow = None
        if args.slow_coverage_dir:
            slow, slow_reports = read_coverage(args.slow_coverage_dir.resolve(), root, {"core": SUITES["core"]})
            reports += ["slow: " + r for r in slow_reports]
        result = dict(evaluate(changes, coverage, slow), head=head, merge_base=ancestor, reports=reports)
        if args.attribute:
            result["uncovered_by_pr"] = attribute(root, head, result["uncovered"])
        code = 2 if result["missing_files"] else (0 if result["ok"] else 1)
    except (ValueError, KeyError, OSError, ET.ParseError, subprocess.CalledProcessError) as error:
        result = {"ok": False, "error": str(error)}
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    if args.summary:
        with args.summary.open("a", encoding="utf-8") as output:
            output.write("\n### Changed-line coverage (100% required)\n\n")
            for path, by_pr in result.get("uncovered_by_pr", {}).items():
                for pr, lines in by_pr.items():
                    output.write(f"- `{path}` lines {', '.join(map(str, lines))} (introduced by {pr})\n")
            output.write("\n```json\n" + json.dumps(result, indent=2) + "\n```\n")
    return code


if __name__ == "__main__":
    sys.exit(main())
