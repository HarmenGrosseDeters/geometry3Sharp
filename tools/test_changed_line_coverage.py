"""Gate tests, including real temporary git histories and CLI exit codes."""

import contextlib
import io
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
                          "uncovered": {"math/A.cs": [4]}, "missing_files": [], "ok": False})
        self.assertTrue(c.evaluate({"math/A.cs": {3}}, {"math/A.cs": {1: 0}})["ok"])
        self.assertEqual(c.evaluate({"math/New.cs": {1}}, {})["missing_files"], ["math/New.cs"])
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

    def test_missing_changed_file_is_error_even_with_no_sequence_points(self):
        self.write("math/New.cs", "new_code();\n")
        self.commit("new code")
        code, result = self.invoke()
        self.assertEqual(code, 2)
        self.assertEqual(result["missing_files"], ["math/New.cs"])

    def test_missing_duplicate_empty_or_malformed_report_fails_closed(self):
        p = self.results / "core/run/coverage.cobertura.xml"
        original = p.read_bytes()
        for payload in (b"not xml", b"<coverage/>", b"<other/>" ):
            p.write_bytes(payload)
            code, result = self.invoke()
            self.assertEqual(code, 2)
            self.assertIn("error", result)
        p.write_bytes(original)
        duplicate = p.parent.parent / "duplicate/coverage.cobertura.xml"
        duplicate.parent.mkdir()
        duplicate.write_bytes(original)
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


if __name__ == "__main__":
    unittest.main()
