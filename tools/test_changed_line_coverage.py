"""Gate tests, including real temporary git histories and CLI exit codes."""

import contextlib
import io
import shutil
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).resolve().parent))
import changed_line_coverage as c


class Decisions(unittest.TestCase):
    def test_scope(self):
        for path in ("math/Exact.cs", "mesh/Foo.cs", "tools/g3cli/Program.cs",
                     "addons/geometry3Sharp.Clipper2/Offset.cs"):
            self.assertTrue(c.in_scope(path), path)
        for path in ("tests/A.cs", "benchmarks/A.cs", "docs/examples/A.cs", "Properties/AssemblyInfo.cs",
                     "tools/PredicateCompiler/A.cs", "tools/analyzers/A.cs", "docs/QA.md"):
            self.assertFalse(c.in_scope(path), path)

    def test_only_changed_executable_lines_and_union_of_hits(self):
        self.assertEqual(c.evaluate({"math/A.cs": {2, 3, 4}}, {"math/A.cs": {1: 0, 2: 1, 4: 0}}),
                         {"changed_executable_lines": 2, "covered_lines": 1, "covered_only_by_slow": {},
                          "uncovered": {"math/A.cs": [4]}, "files_without_executable_lines": [], "unmeasured_lines": {"math/A.cs": [3]}, "ok": False})
        self.assertTrue(c.evaluate({"math/A.cs": {3}}, {"math/A.cs": {1: 0}})["ok"])
        r = c.evaluate({"math/New.cs": {1}}, {})
        self.assertEqual((r["files_without_executable_lines"], r["ok"]), (["math/New.cs"], True))
        self.assertTrue(c.evaluate({}, {})["ok"])


    def test_lines_hit_only_by_slow_tests_are_reported_not_failed(self):
        changes = {"math/A.cs": {2, 3, 4}}
        fast = {"math/A.cs": {2: 1, 3: 0, 4: 0}}
        slow = {"math/A.cs": {2: 0, 3: 5, 4: 0}}
        r = c.evaluate(changes, fast, slow)
        self.assertEqual(r["covered_only_by_slow"], {"math/A.cs": [3]})
        self.assertEqual(r["uncovered"], {"math/A.cs": [4]})       # neither tier hits line 4
        self.assertFalse(r["ok"])
        self.assertEqual(r["covered_lines"], 2)                    # line 2 (fast) and line 3 (slow)
        r = c.evaluate(changes, {"math/A.cs": {2: 1, 3: 0, 4: 0}}, {"math/A.cs": {3: 1, 4: 2}})
        self.assertTrue(r["ok"])
        self.assertEqual(r["covered_only_by_slow"], {"math/A.cs": [3, 4]})
        self.assertEqual(c.evaluate(changes, fast, {})["uncovered"], {"math/A.cs": [3, 4]})   # slow run without the file
        self.assertEqual(c.evaluate(changes, fast)["covered_only_by_slow"], {})               # no slow run: nothing excused


class Repository(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.run_git("init", "-q")
        self.run_git("config", "user.name", "Coverage test")
        self.run_git("config", "user.email", "coverage@example.invalid")
        self.write("math/A.cs", "// unchanged\nold();\n// between\nold2();\n")
        self.commit("base")
        self.base = self.run_git("rev-parse", "HEAD").strip()
        self.write("math/A.cs", "// unchanged\nnew1();\n// between\nnew2();\n// comment\n")
        self.commit("head")
        self.results = self.root / "results"
        for suite, assembly in c.SUITES.items():
            self.report(suite, assembly, {"math/A.cs": {2: 1, 4: 0}})

    def run_git(self, *args):
        return c.git(self.root, *args)

    def write(self, path, text):
        p = self.root / path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding="utf-8")

    def commit(self, message):
        self.run_git("add", "math", "tools" if (self.root / "tools").exists() else "math")
        self.run_git("commit", "-qm", message)

    def report(self, suite, assembly, files, sources=None):
        doc = ET.Element("coverage")
        source = ET.SubElement(doc, "sources")
        for name in sources or [str(self.root)]:
            ET.SubElement(source, "source").text = name
        package = ET.SubElement(ET.SubElement(doc, "packages"), "package", name=assembly)
        classes = ET.SubElement(package, "classes")
        for path, hits in files.items():
            cls = ET.SubElement(classes, "class", filename=path)
            lines = ET.SubElement(cls, "lines")
            for line, hit in hits.items():
                ET.SubElement(lines, "line", number=str(line), hits=str(hit))
        target = self.results / suite / "run" / "coverage.cobertura.xml"
        target.parent.mkdir(parents=True, exist_ok=True)
        ET.ElementTree(doc).write(target, encoding="utf-8")
        return target

    def invoke(self, *extra):
        previous = Path.cwd()
        try:
            os.chdir(self.root)
            with contextlib.redirect_stdout(io.StringIO()):
                code = c.main(["--base", self.base, "--coverage-dir", str(self.results),
                               "--json", str(self.root / "gate.json"), *extra])
            return code, json.loads((self.root / "gate.json").read_text())
        finally:
            os.chdir(previous)

    def test_uncovered_lines_are_attributed_to_the_pull_request_that_introduced_them(self):
        self.run_git("checkout", "-q", "-b", "feature")
        self.write("math/A.cs", "// unchanged\nnew1();\n// between\nnew2();\n// comment\nbranch();\n")
        self.commit("feature work")
        self.run_git("checkout", "-q", "-")
        self.run_git("merge", "--no-ff", "-q", "-m", "Merge pull request #7 from x/feature", "feature")
        self.write("math/A.cs", "// unchanged\nnew1();\n// between\nnew2();\n// comment\nbranch();\nsquash();\n")
        self.commit("Squash merged change (#42)")
        head = self.run_git("rev-parse", "HEAD").strip()
        got = c.attribute(self.root, head, {"math/A.cs": [2, 6, 7]})
        self.assertEqual(got["math/A.cs"]["#7"], [6])
        self.assertEqual(got["math/A.cs"]["#42"], [7])
        self.assertEqual(list(got["math/A.cs"]).count("#7"), 1)
        self.assertEqual(len(got["math/A.cs"]), 3)             # line 2 is from "head": no PR, its short hash

    def test_file_names_with_glob_characters_are_matched_literally(self):
        self.write("math/[ab]*.cs", "x();\n")
        self.write("math/a.cs", "y();\n")
        self.commit("glob names")
        _, changes = c.changed_lines(self.root, self.base, "HEAD")
        self.assertEqual(changes["math/[ab]*.cs"], {1})
        self.assertEqual(changes["math/a.cs"], {1})

    def test_main_with_attribute_names_the_pull_request(self):
        code, result = self.invoke("--attribute", "--summary", str(self.root / "summary.md"))
        self.assertEqual(code, 1)
        self.assertEqual(list(result["uncovered_by_pr"]["math/A.cs"].values()), [[4]])
        self.assertIn("`math/A.cs` lines 4 (introduced by", (self.root / "summary.md").read_text())
        self.assertNotIn("uncovered_by_pr", self.invoke()[1])

    def test_expect_shards_through_main_fails_closed_with_exit_2(self):
        code, result = self.invoke("--expect-shards", "core=2")
        self.assertEqual(code, 2)
        self.assertIn("shards", result["error"])

    def test_core_shards_are_merged_by_maximum(self):
        (self.results / "core/run/coverage.cobertura.xml").unlink()
        self.report("core/0", c.SUITES["core"], {"math/A.cs": {2: 1, 4: 0}})
        self.report("core/1", c.SUITES["core"], {"math/A.cs": {2: 0, 4: 3}})
        code, result = self.invoke()
        self.assertEqual((code, result["uncovered"]), (0, {}))
        self.report("core/1", c.SUITES["core"], {"math/A.cs": {2: 0, 4: 0}})
        self.assertEqual(self.invoke()[1]["uncovered"], {"math/A.cs": [4]})

    def test_changed_lines_and_complete_cli_decision(self):
        ancestor, lines = c.changed_lines(self.root, self.base, "HEAD")
        self.assertEqual(ancestor, self.base)
        self.assertEqual(lines, {"math/A.cs": {2, 4, 5}})
        code, result = self.invoke()
        self.assertEqual(code, 1)
        self.assertEqual(result["uncovered"], {"math/A.cs": [4]})
        self.report("cli", "g3cli", {"math/A.cs": {2: 0, 4: 7}})
        code, result = self.invoke("--summary", str(self.root / "summary.md"))
        self.assertEqual(code, 0)
        self.assertEqual(result["covered_lines"], 2)
        self.assertIn("100% required", (self.root / "summary.md").read_text())

    def test_slow_coverage_dir_excuses_only_lines_the_slow_tier_hits(self):
        slow = self.root / "slow"
        fast_results, self.results = self.results, slow
        self.report("core", "geometry3Sharp", {"math/A.cs": {2: 0, 4: 3}})
        self.results = fast_results
        code, result = self.invoke("--slow-coverage-dir", str(slow))
        self.assertEqual(code, 0)
        self.assertEqual(result["covered_only_by_slow"], {"math/A.cs": [4]})
        self.assertTrue(any(r.startswith("slow: ") for r in result["reports"]))
        # the Slow tier does not hit the line either: still a failure
        self.results = slow
        self.report("core", "geometry3Sharp", {"math/A.cs": {2: 0, 4: 0}})
        self.results = fast_results
        code, result = self.invoke("--slow-coverage-dir", str(slow))
        self.assertEqual((code, result["uncovered"]), (1, {"math/A.cs": [4]}))
        # no report for the Slow run, or one for another assembly: incomplete evidence
        self.results = slow
        self.report("core", "g3cli", {"math/A.cs": {4: 1}})
        self.results = fast_results
        self.assertEqual(self.invoke("--slow-coverage-dir", str(slow))[0], 2)
        shutil.rmtree(slow / "core")
        self.assertEqual(self.invoke("--slow-coverage-dir", str(slow))[0], 2)

    def test_merge_base_excludes_target_branch_only_edits(self):
        head = self.run_git("rev-parse", "HEAD").strip()
        self.run_git("checkout", "-q", "-b", "target", self.base)
        self.write("math/Target.cs", "untested_target();\n")
        self.commit("target advanced")
        self.run_git("checkout", "-q", "--detach", head)
        ancestor, changes = c.changed_lines(self.root, "target", head)
        self.assertEqual(ancestor, self.base)
        self.assertEqual(set(changes), {"math/A.cs"})

    def test_renames_new_files_deletions_and_unusual_paths(self):
        self.run_git("mv", "math/A.cs", 'math/Ü name "quote".cs')
        self.write("math/Added.cs", "first();\nsecond();\n")
        self.write("tools/g3cli/New.cs", "cli();\n")
        self.commit("rename and add")
        _, changes = c.changed_lines(self.root, self.base, "HEAD")
        self.assertNotIn("math/A.cs", changes)
        self.assertEqual(changes['math/Ü name "quote".cs'], {1, 2, 3, 4, 5})
        self.assertEqual(changes["math/Added.cs"], {1, 2})
        self.assertEqual(changes["tools/g3cli/New.cs"], {1})

    def test_pure_deletion_is_not_a_changed_head_line(self):
        self.write("math/A.cs", "// unchanged\n// between\n")
        self.commit("delete statements")
        _, changes = c.changed_lines(self.root, self.base, "HEAD")
        self.assertEqual(changes, {})

    def test_changed_file_that_no_report_lists_is_reported_not_failed(self):
        # (a file without sequence points, eg a data table: coverlet lists every method it instruments, so such a file has no code)
        self.write("math/New.cs", "new_code();\n")
        self.commit("new code")
        code, result = self.invoke()
        self.assertEqual(code, 1)    # (the uncovered line of math/A.cs from the fixture still fails)
        self.assertEqual(result["files_without_executable_lines"], ["math/New.cs"])
        self.assertEqual(list(result["uncovered"]), ["math/A.cs"])
        for suite, assembly in c.SUITES.items():
            self.report(suite, assembly, {"math/A.cs": {2: 1, 4: 1}})
        (self.results / "core/run/coverage.cobertura.xml").unlink(missing_ok=True)
        self.report("core", c.SUITES["core"], {"math/A.cs": {2: 1, 4: 1}})
        code, result = self.invoke()
        self.assertEqual((code, result["ok"]), (0, True))

    def test_missing_duplicate_empty_or_malformed_report_fails_closed(self):
        p = self.results / "core/run/coverage.cobertura.xml"
        original = p.read_bytes()
        before = self.invoke()[0]
        for payload in (b"not xml", b"<coverage/>", b"<other/>" ):
            p.write_bytes(payload)
            code, result = self.invoke()
            self.assertEqual(code, 2)
            self.assertIn("error", result)
        p.write_bytes(original)
        duplicate = p.parent.parent / "duplicate/coverage.cobertura.xml"
        duplicate.parent.mkdir()
        duplicate.write_bytes(original)
        # a second report of a suite is a shard (merged by max), not an error: the result does not change
        self.assertEqual(self.invoke()[0], before)
        duplicate.write_bytes(b"not xml")
        self.assertEqual(self.invoke()[0], 2)
        duplicate.unlink()
        p.unlink()
        self.assertEqual(self.invoke()[0], 2)

    def test_trx_attachment_copy_is_not_a_second_collection(self):
        p = self.results / "core/run/coverage.cobertura.xml"
        copy = self.results / "core/trx-run/In/host/coverage.cobertura.xml"
        copy.parent.mkdir(parents=True)
        copy.write_bytes(p.read_bytes())
        self.assertEqual(self.invoke()[0], 1)

    def test_negative_counts_wrong_assembly_and_source_link_rejected(self):
        for assembly, files in (("geometry3Sharp", {"math/A.cs": {2: -1}}),
                                ("wrong", {"math/A.cs": {2: 1}}),
                                ("geometry3Sharp", {"https://example.invalid/A.cs": {2: 1}})):
            self.report("core", assembly, files)
            self.assertEqual(self.invoke()[0], 2)

    def test_method_only_or_incomplete_class_lines_are_not_evidence(self):
        for suite in c.SUITES:
            p = self.results / suite / "run/coverage.cobertura.xml"
            doc = ET.parse(p)
            cls = doc.find(".//class")
            lines = cls.find("lines")
            cls.remove(lines)
            ET.SubElement(ET.SubElement(cls, "methods"), "method").append(lines)
            doc.write(p)
        self.assertEqual(self.invoke()[0], 2)
        # A different valid class must not hide one incomplete class.
        for suite in ("cli", "clipper2"):
            self.report(suite, c.SUITES[suite], {"math/A.cs": {2: 1, 4: 1}})
        p = self.results / "core/run/coverage.cobertura.xml"
        doc = ET.parse(p)
        classes = doc.find(".//classes")
        cls = ET.SubElement(classes, "class", filename="math/A.cs")
        ET.SubElement(ET.SubElement(cls, "lines"), "line", number="2", hits="1")
        doc.write(p)
        self.assertEqual(self.invoke()[0], 2)

    def test_exact_paths_absolute_relative_windows_separators_and_no_suffix_match(self):
        for filename in (str(self.root / "math/A.cs"), "math/A.cs", "math\\A.cs"):
            self.assertEqual(c.coverage_path(filename, [str(self.root)], self.root), "math/A.cs")
        with self.assertRaises(ValueError):
            c.coverage_path("/unrelated/math/A.cs", [], self.root)
        self.write("A.cs", "other();")
        with self.assertRaises(ValueError):
            c.coverage_path("A.cs", [str(self.root / "math")], self.root)

    def test_dirty_checkout_wrong_head_and_bad_base_are_errors(self):
        self.assertEqual(self.invoke("--head", self.base)[0], 2)
        self.assertEqual(self.invoke("--base", "no-such-ref")[0], 2)
        self.write("math/A.cs", "uncommitted();\n")
        self.assertEqual(self.invoke()[0], 2)

    def test_deterministic_merging_and_evaluation(self):
        coverage, reports = c.read_coverage(self.results, self.root)
        self.report("cli", "g3cli", {"math/A.cs": {4: 5, 2: 0}})
        first, first_reports = c.read_coverage(self.results, self.root)
        second, second_reports = c.read_coverage(self.results, self.root)
        self.assertEqual(first, second)
        self.assertEqual(first_reports, second_reports)
        self.assertEqual(first["math/A.cs"], {2: 1, 4: 5})
        self.assertEqual(coverage["math/A.cs"], {2: 1, 4: 0})
        self.assertEqual(reports, first_reports)





class ShardFilter(unittest.TestCase):
    LISTING = "The following Tests are available:\n    A.B.One\n    A.B.Two(x: 1)\n    A.C.Three\n    A.D.Four\n"

    def test_every_class_lands_in_exactly_one_shard(self):
        sys.path.insert(0, str(Path(__file__).parent / "coverage"))
        import shard_filter
        self.assertEqual(shard_filter.classes(self.LISTING), ["A.B", "A.C", "A.D"])
        seen = []
        for i in range(2):
            f = shard_filter.shard_filter(self.LISTING, i, 2, "Category!=Slow")
            self.assertTrue(f.startswith("(Category!=Slow)&("))
            seen += [c for c in ("A.B.", "A.C.", "A.D.") if "~" + c in f]
        self.assertEqual(sorted(seen), ["A.B.", "A.C.", "A.D."])


class Shards(unittest.TestCase):
    def test_a_missing_or_extra_shard_is_incomplete_evidence(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            for shard in ("0", "1", "3"):                     # shard 2 crashed
                target = root / "core" / shard / "id"
                target.mkdir(parents=True)
                (target / "coverage.cobertura.xml").write_text("<coverage/>")
            with self.assertRaisesRegex(ValueError, "expected one report in each of the shards"):
                c.read_coverage(root, root, {"core": "geometry3Sharp"}, expect_shards={"core": 4})


class Summary(unittest.TestCase):
    def test_summary_lists_prs_and_stays_small(self):
        result = {"covered_lines": 3, "changed_executable_lines": 5, "head": "a" * 40, "merge_base": "b" * 40,
                  "uncovered": {"math/A.cs": [4, 5]}, "uncovered_by_pr": {"math/A.cs": {"#7": [4, 5]}},
                  "files_without_executable_lines": ["core/T.cs"], "unmeasured_lines": {"math/A.cs": [3]}}
        text = c.summary_markdown(result)
        self.assertIn("`math/A.cs` lines 4, 5 (introduced by #7)", text)
        self.assertIn("`core/T.cs`", text)
        self.assertIn("1 in 1 files", text)
        self.assertNotIn("```json", text)
        self.assertIn("truncated", c.summary_markdown(result, limit=10))
        self.assertIn("Incomplete evidence", c.summary_markdown({"error": "boom"}))


if __name__ == "__main__":
    unittest.main()
