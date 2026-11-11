#!/usr/bin/env python3
"""Gate changed executable C# lines using the Linux coverlet reports of the coverage workflow.

Run from the checkout used to collect coverage (.github/workflows/coverage.yml, nightly on master and on demand). The
changed lines are those between the merge base of --base and the committed HEAD (never the tip of a moving branch).
Exit 0: every changed executable line has a hit; 1: uncovered lines (with --attribute, each is named with the pull
request that introduced it); 2: missing or invalid reports (also a core suite run in shards with a shard missing, see
--expect-shards). A changed source file that no report lists has no executable code (coverlet lists every method of the
instrumented assemblies) and is only reported; so are changed lines inside a listed file that carry no sequence point
(`unmeasured_lines`: continuation lines of a statement, code in a conditional-compilation branch that the test build
does not compile, excluded methods). See docs/QA.md section 2.6.

Optional --slow-coverage-dir (a second core report over Category=Slow only; a line only that tier hits is
"covered_only_by_slow" and does not fail) is not used by the workflow, which collects the Slow tier with the rest.
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
        patch = git(root, *common, "--unified=0", ancestor, head, "--", ":(literal)" + path)
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


def read_coverage(directory, root, suites=SUITES, expect_shards=None):
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
        if expect_shards and suite in expect_shards:
            # (a crashed or missing shard would show the classes it runs as uncovered: that is incomplete evidence, not a finding)
            found = sorted(f.relative_to(directory / suite).parts[0] for f in files)
            wanted = sorted(str(i) for i in range(expect_shards[suite]))
            if found != wanted:
                raise ValueError(f"{suite}: expected one report in each of the shards {wanted}, found them in {found}")
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


UNMEASURABLE_FILE = "tools/coverage/unmeasurable_lines.json"
UNMEASURABLE_FIELDS = ("file", "text", "reason", "approved_in")


def load_unmeasurable(path):
    """The reviewed list of executable lines that no coverage run can hit (a defensive branch of a platform probe, a
    branch of another framework, an invariant guard): entries {file, text, occurrence?, reason, approved_in}. The line is
    found by its stripped text (the `occurrence`-th line of that text in the file, default 1), so that it survives edits
    around it. An entry never excuses more than its one line."""
    entries = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(entries, list):
        raise ValueError(f"{path}: expected a list of entries")
    for entry in entries:
        missing = [f for f in UNMEASURABLE_FIELDS if not str(entry.get(f, "")).strip()]
        if missing:
            raise ValueError(f"{path}: entry {entry.get('file')}: {entry.get('text')!r} lacks {', '.join(missing)}")
        if int(entry.get("occurrence", 1)) < 1:
            raise ValueError(f"{path}: occurrence starts at 1")
    return entries


def resolve_unmeasurable(root, entries, coverage):
    """({file: {line numbers}}, stale entries as text). An entry is stale (the gate fails until it is removed or fixed) when its
    line is gone or moved to other text, is not an executable line of the reports any more, or is covered."""
    lines_of, exempt, stale = {}, {}, []
    for entry in entries:
        name = f"{entry['file']}: {entry['text']!r}"
        path = entry["file"]
        if path not in lines_of:
            try:
                lines_of[path] = (root / path).read_text(encoding="utf-8-sig").split("\n")
            except OSError:
                lines_of[path] = None
        lines = lines_of[path]
        if lines is None:
            stale.append(f"{name}: the file is gone")
            continue
        found = [i + 1 for i, line in enumerate(lines) if line.strip() == entry["text"].strip()]
        occurrence = int(entry.get("occurrence", 1))
        if len(found) < occurrence or (len(found) > 1 and "occurrence" not in entry):
            stale.append(f"{name}: found {len(found)} such lines" + ("" if "occurrence" in entry or not found else ", give the occurrence"))
            continue
        number = found[occurrence - 1]
        hits = coverage.get(path, {}).get(number)
        if hits is None:
            stale.append(f"{name}: line {number} is not an executable line of the reports")
        elif hits > 0:
            stale.append(f"{name}: line {number} is covered now, remove the entry")
        else:
            exempt.setdefault(path, set()).add(number)
    return exempt, stale


def evaluate(changes, coverage, slow=None, exempt=None):
    uncovered, missing, only_slow, unmeasured, excused = {}, [], {}, {}, {}
    executable = covered = 0
    for path, changed in sorted(changes.items()):
        if path not in coverage:
            # (coverlet reports every method of the instrumented assemblies, hit or not: a source file that no report
            # lists has no executable code, eg only data or declarations; it is reported, not counted as a failure)
            missing.append(path)
            continue
        measured = changed & coverage[path].keys()
        if changed - measured:
            unmeasured[path] = sorted(changed - measured)
        missed = sorted(n for n in measured if coverage[path][n] == 0)
        executable += len(measured)
        if slow is not None and missed:
            # (hit by the Slow tests only: reported, not a failure)
            slow_hits = slow.get(path, {})
            slow_only = [n for n in missed if slow_hits.get(n, 0) > 0]
            if slow_only:
                only_slow[path] = slow_only
            missed = [n for n in missed if n not in slow_only]
        if exempt and path in exempt:
            # (the reviewed list of unmeasurable lines)
            listed = [n for n in missed if n in exempt[path]]
            if listed:
                excused[path] = listed
            missed = [n for n in missed if n not in exempt[path]]
        covered += len(measured) - len(missed)
        if missed:
            uncovered[path] = missed
    return {"changed_executable_lines": executable, "covered_lines": covered,
            "covered_only_by_slow": only_slow, "excused_unmeasurable": excused,
            "uncovered": uncovered, "files_without_executable_lines": missing, "unmeasured_lines": unmeasured,
            "ok": not uncovered}


def summary_markdown(result, limit=900_000):
    """The job summary: counts, the uncovered lines (by pull request when attributed) and what could not be measured; the
    full JSON is the artifact (the summary of a step is limited to 1 MiB)."""
    out = ["\n### Changed-line coverage (100% required)\n\n"]
    if "error" in result:
        out.append(f"**Incomplete evidence:** {result['error']}\n")
    else:
        out.append(f"{result['covered_lines']} of {result['changed_executable_lines']} changed executable lines are covered "
                   f"(head `{result['head'][:10]}`, merge base `{result['merge_base'][:10]}`).\n\n")
        for path, by_pr in result.get("uncovered_by_pr", {}).items():
            for pr, lines in by_pr.items():
                out.append(f"- `{path}` lines {', '.join(map(str, lines))} (introduced by {pr})\n")
        if "uncovered_by_pr" not in result:
            for path, lines in result["uncovered"].items():
                out.append(f"- `{path}` lines {', '.join(map(str, lines))}\n")
        for entry in result.get("stale_unmeasurable", []):
            out.append(f"- **stale entry of the list of unmeasurable lines:** {entry}\n")
        if result.get("excused_unmeasurable"):
            out.append(f"\n{sum(map(len, result['excused_unmeasurable'].values()))} changed lines are on the reviewed list of "
                       f"unmeasurable lines ({UNMEASURABLE_FILE}).\n")
        if result["files_without_executable_lines"]:
            out.append("\nChanged files that no report lists (no executable code): "
                       + ", ".join(f"`{f}`" for f in result["files_without_executable_lines"]) + "\n")
        if result["unmeasured_lines"]:
            out.append(f"\nChanged lines without a sequence point (not measured), {sum(map(len, result['unmeasured_lines'].values()))} "
                       f"in {len(result['unmeasured_lines'])} files; see the JSON artifact.\n")
    text = "".join(out)
    return text if len(text) < limit else text[:limit] + "\n\n(truncated; see the JSON artifact)\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", default="HEAD")
    parser.add_argument("--coverage-dir", type=Path, required=True)
    parser.add_argument("--slow-coverage-dir", type=Path,
                        help="core/ below it holds the coverlet report of the Category=Slow run")
    parser.add_argument("--expect-shards", default="",
                        help="suite=count[,suite=count]: the suite was run in that many shards (reports in <suite>/0 .. <suite>/count-1)")
    parser.add_argument("--attribute", action="store_true",
                        help="name the pull request that introduced each uncovered line (scheduled runs)")
    parser.add_argument("--unmeasurable", type=Path, default=None,
                        help=f"the reviewed list of lines no run can hit (default: {UNMEASURABLE_FILE} of the checkout, if there is one)")
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
        shards = {name: int(count) for name, count in (item.split("=") for item in args.expect_shards.split(",") if item)}
        coverage, reports = read_coverage(args.coverage_dir.resolve(), root, expect_shards=shards)
        slow = None
        if args.slow_coverage_dir:
            slow, slow_reports = read_coverage(args.slow_coverage_dir.resolve(), root, {"core": SUITES["core"]})
            reports += ["slow: " + r for r in slow_reports]
        listed = args.unmeasurable or (root / UNMEASURABLE_FILE)
        exempt, stale = resolve_unmeasurable(root, load_unmeasurable(listed), coverage) if Path(listed).exists() else ({}, [])
        result = dict(evaluate(changes, coverage, slow, exempt), head=head, merge_base=ancestor, reports=reports,
                      stale_unmeasurable=stale)
        result["ok"] = result["ok"] and not stale
        if args.attribute:
            result["uncovered_by_pr"] = attribute(root, head, result["uncovered"])
        code = 0 if result["ok"] else 1
    except (ValueError, KeyError, OSError, ET.ParseError, subprocess.CalledProcessError) as error:
        result = {"ok": False, "error": str(error)}
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    if args.summary:
        with args.summary.open("a", encoding="utf-8") as output:
            output.write(summary_markdown(result))
    return code


if __name__ == "__main__":
    sys.exit(main())
