"""Unit tests of tools/new_tests_on_base.py (python3 -m unittest discover -s tools -p 'test_*.py')."""

import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import new_tests_on_base as n  # noqa: E402


class LibraryPaths(unittest.TestCase):
    def test_library_and_non_library(self):
        for p in ("math/ExactPredicates.cs", "mesh_ops/ExactMeshBoolean.cs", "geometry3Sharp_netstandard.csproj",
                  "CompatibilitySuppressions.xml", "BannedSymbols.txt", ".editorconfig"):
            self.assertTrue(n.is_library_path(p), p)
        for p in ("tests/geometry3Sharp.Tests/FooTests.cs", "docs/QA.md", "docs/examples/Ex.cs",
                  "benchmarks/X/Program.cs", "tools/g3cli/Program.cs", ".github/workflows/ci.yml", "README.md",
                  "CLAUDE.md"):
            self.assertFalse(n.is_library_path(p), p)

    def test_library_changes(self):
        ns = "M\tmath/Vector2d.cs\nA\tmesh/NewThing.cs\nD\tmesh/Old.cs\nA\ttests/geometry3Sharp.Tests/NewTests.cs\n" \
             "M\tdocs/QA.md\nM\tCLAUDE.md\n"
        reset, delete = n.library_changes(ns)
        self.assertEqual(reset, ["math/Vector2d.cs", "mesh/Old.cs"])
        self.assertEqual(delete, ["mesh/NewThing.cs"])


class Parsing(unittest.TestCase):
    def test_method_of(self):
        self.assertEqual(n.method_of("Ns.C.M(x: 1, s: \"a(b)\")"), "Ns.C.M")
        self.assertEqual(n.method_of("Ns.C.M"), "Ns.C.M")
        self.assertEqual(n.method_of("Ns.Outer+Inner.M(a: 2)"), "Ns.Outer+Inner.M")

    def test_parse_list_tests(self):
        out = ("Build started\nTest run for /x/geometry3Sharp.Tests.dll (.NETCoreApp,Version=v10.0)\n"
               "The following Tests are available:\n"
               "    geometry3Sharp.Tests.A.Fact1\n"
               "    geometry3Sharp.Tests.A.Theory1(n: 1)\n"
               "    geometry3Sharp.Tests.A.Theory1(n: 2)\n"
               "    geometry3Sharp.Tests.B.Fact2\n")
        self.assertEqual(n.parse_list_tests(out), {"geometry3Sharp.Tests.A.Fact1", "geometry3Sharp.Tests.A.Theory1",
                                                   "geometry3Sharp.Tests.B.Fact2"})
        self.assertEqual(n.parse_list_tests("    not.a.Test\n"), set())

    def test_error_files(self):
        with tempfile.TemporaryDirectory() as root:
            f = os.path.join(root, "tests", "geometry3Sharp.Tests", "NewTests.cs")
            out = ("%s(12,5): error CS0117: 'Foo' does not contain a definition for 'Bar' [%s/x.csproj]\n"
                   "%s(13,5): error CS0117: again\n"
                   "/elsewhere/Other.cs(1,1): error CS1002: ; expected\n"
                   "%s(14,5): warning CS0168: unused\n" % (f, root, f, os.path.join(root, "W.cs")))
            self.assertEqual(n.error_files(out, root), {"tests/geometry3Sharp.Tests/NewTests.cs"})

    def test_parse_trx(self):
        trx = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>
<UnitTestResult testName="Ns.C.A" outcome="Passed" />
<UnitTestResult testName="Ns.C.T(n: 1)" outcome="Passed" />
<UnitTestResult testName="Ns.C.T(n: 2)" outcome="Failed" />
</Results></TestRun>"""
        self.assertEqual(n.parse_trx(trx), {"Ns.C.A": ["Passed"], "Ns.C.T": ["Passed", "Failed"]})


class Decision(unittest.TestCase):
    def test_classify(self):
        new = {"A", "T", "Hang", "NewApi", "Skipped"}
        v = n.classify(new, {"A", "T", "Hang", "Skipped"},
                       {"A": ["Passed"], "T": ["Passed", "Failed"], "Skipped": ["NotExecuted"]})
        self.assertEqual(v, {"A": "passes", "T": "fails", "Hang": "no result", "NewApi": "does not compile",
                             "Skipped": "no result"})

    def test_report_informational_never_fails(self):
        _, code = n.report({"A": "passes"}, set(), False)
        self.assertEqual(code, 0)
        _, code = n.report({}, set(), False)
        self.assertEqual(code, 0)

    def test_report_bugfix_needs_a_failing_test(self):
        self.assertEqual(n.report({"A": "passes", "B": "fails"}, set(), True)[1], 0)
        self.assertEqual(n.report({"A": "passes", "B": "no result"}, set(), True)[1], 0)
        self.assertEqual(n.report({"A": "passes", "B": "does not compile"}, set(), True)[1], 1)
        self.assertEqual(n.report({"A": "passes"}, set(), True)[1], 1)
        text, code = n.report({}, set(), True)
        self.assertEqual(code, 1)
        self.assertIn("no new test fails on the base", text)


if __name__ == "__main__":
    unittest.main()
