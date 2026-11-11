"""Unit tests of tools/new_tests_on_base.py (python3 -m unittest discover -s tools -p 'test_new_tests_on_base.py')."""

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

    def test_name_status_z_keeps_unusual_paths(self):
        out = "M\0math/Vector2d.cs\0A\0tests/geometry3Sharp.Tests/Füße \"x\".cs\0D\0mesh/Old.cs\0"
        self.assertEqual(n.parse_name_status_z(out), {"math/Vector2d.cs": "M", "mesh/Old.cs": "D",
                                                      "tests/geometry3Sharp.Tests/Füße \"x\".cs": "A"})
        self.assertEqual(n.parse_name_status_z(""), {})

    def test_library_changes(self):
        changes = {"math/Vector2d.cs": "M", "mesh/NewThing.cs": "A", "mesh/Old.cs": "D",
                   "tests/geometry3Sharp.Tests/NewTests.cs": "A", "docs/QA.md": "M", "CLAUDE.md": "M"}
        reset, delete = n.library_changes(changes)
        self.assertEqual(reset, ["math/Vector2d.cs", "mesh/Old.cs"])
        self.assertEqual(delete, ["mesh/NewThing.cs"])

    def test_test_side_changes_include_deleted_files(self):
        # the fallback when an unchanged test file breaks (a helper moved from the tests into the library)
        changes = {"tests/geometry3Sharp.Tests/Helper.cs": "D", "math/Helper.cs": "A",
                   "tests/geometry3Sharp.Tests/NewTests.cs": "A", "docs/examples/Ex.cs": "M", "docs/QA.md": "M"}
        self.assertEqual(n.test_side_changes(changes), ["docs/examples/Ex.cs", "tests/geometry3Sharp.Tests/Helper.cs",
                                                        "tests/geometry3Sharp.Tests/NewTests.cs"])


class Parsing(unittest.TestCase):
    def test_method_of(self):
        self.assertEqual(n.method_of("Ns.C.M(x: 1, s: \"a(b)\")"), "Ns.C.M")
        self.assertEqual(n.method_of("Ns.C.M"), "Ns.C.M")
        self.assertEqual(n.method_of("Ns.Outer+Inner.M(a: 2)"), "Ns.Outer+Inner.M")
        self.assertEqual(n.method_of("Ns.C.Gen<Double>(x: 1.5)"), "Ns.C.Gen")
        self.assertEqual(n.method_of("Ns.C.Gen<List<Int32>>"), "Ns.C.Gen")

    def test_display_names_are_not_method_names(self):
        self.assertTrue(n.is_method_name("geometry3Sharp.Tests.A.Fact1"))
        self.assertTrue(n.is_method_name("Ns.Outer+Inner.M"))
        for name in ("clamp works", "Fact1", "Ns.C.M x", "a|b.c", "Ns.C.M=1"):
            self.assertFalse(n.is_method_name(name), name)

    def test_parse_list_tests(self):
        out = ("Build started\nTest run for /x/geometry3Sharp.Tests.dll (.NETCoreApp,Version=v10.0)\n"
               "The following Tests are available:\n"
               "    geometry3Sharp.Tests.A.Fact1\n"
               "    geometry3Sharp.Tests.A.Theory1(n: 1)\n"
               "    geometry3Sharp.Tests.A.Theory1(n: 2)\n"
               "    geometry3Sharp.Tests.B.Gen<Double>(x: 1.5)\n"
               "    nice name\n")
        self.assertEqual(n.parse_list_tests(out), {"geometry3Sharp.Tests.A.Fact1", "geometry3Sharp.Tests.A.Theory1",
                                                   "geometry3Sharp.Tests.B.Gen", "nice name"})
        self.assertEqual(n.parse_list_tests("    not.a.Test\n"), set())

    def test_filter(self):
        self.assertEqual(n.test_filter({"Ns.B.M", "Ns.A+I.M"}), "FullyQualifiedName=Ns.A+I.M|FullyQualifiedName=Ns.B.M")
        self.assertEqual(n.test_filter({"a(b)"}), "FullyQualifiedName=a\\(b\\)")

    def test_error_files(self):
        with tempfile.TemporaryDirectory() as root:
            f = os.path.join(root, "tests", "geometry3Sharp.Tests", "NewTests.cs")
            out = ("%s(12,5): error CS0117: 'Foo' does not contain a definition for 'Bar' [%s/x.csproj]\n"
                   "%s(13,5): error CS0117: again\n"
                   "/elsewhere/Other.cs(1,1): error CS1002: ; expected\n"
                   "%s/x.csproj : error NU1605: package downgrade\n"
                   "%s(14,5): warning CS0168: unused\n" % (f, root, f, root, os.path.join(root, "W.cs")))
            self.assertEqual(n.error_files(out, root), {"tests/geometry3Sharp.Tests/NewTests.cs"})

    TRX = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>%s</Results>
<ResultSummary outcome="%s"><Counters total="3" /></ResultSummary></TestRun>"""

    def test_parse_trx(self):
        trx = self.TRX % ('<UnitTestResult testName="Ns.C.A" outcome="Passed" />'
                          '<UnitTestResult testName="Ns.C.T(n: 1)" outcome="Passed" />'
                          '<UnitTestResult testName="Ns.C.T(n: 2)" outcome="Failed" />', "Failed")
        self.assertEqual(n.parse_trx(trx), ({"Ns.C.A": ["Passed"], "Ns.C.T": ["Passed", "Failed"]}, "Failed"))

    def test_parse_trx_of_an_aborted_run(self):
        self.assertEqual(n.parse_trx(self.TRX % ("", "Aborted")), ({}, "Aborted"))


class Projects(unittest.TestCase):
    CLIPPER2 = "tests/geometry3Sharp.Clipper2.Tests"

    def make_tree(self, *projects):
        import os, tempfile
        tree = tempfile.TemporaryDirectory()
        self.addCleanup(tree.cleanup)
        for project in projects:
            os.makedirs(os.path.join(tree.name, project))
            open(os.path.join(tree.name, project, os.path.basename(project) + ".csproj"), "w").close()
        return tree.name

    def test_a_project_needs_a_project_file(self):
        import os
        tree = self.make_tree()
        os.makedirs(os.path.join(tree, self.CLIPPER2))              # (an empty directory: left by a reset of added files)
        self.assertEqual(n.projects_of(tree), [])
        tree = self.make_tree(n.PRIMARY, self.CLIPPER2)
        self.assertEqual(n.projects_of(tree), list(n.TEST_PROJECTS))
        self.assertEqual(n.list_tests(tree, "tests/not.There"), set())

    def test_the_clipper2_tests_are_compared_when_the_change_touches_them(self):
        # a bugfix of the Clipper2 addon has its tests in the Clipper2 test project (#583): the gate must see them
        tree = self.make_tree(n.PRIMARY, self.CLIPPER2)
        both = [n.PRIMARY, self.CLIPPER2]
        self.assertEqual(n.active_projects({"mesh/Reducer.cs": "M", "tests/geometry3Sharp.Tests/A.cs": "A"}, tree), [n.PRIMARY])
        self.assertEqual(n.active_projects({self.CLIPPER2 + "/B.cs": "A"}, tree), both)
        self.assertEqual(n.active_projects({"tests/geometry3Sharp.Tests/Harness/Invariance.cs": "M"}, tree), both)
        self.assertEqual(n.active_projects({"docs/examples/Clipper2/Example.cs": "M"}, tree), both)
        self.assertEqual(n.active_projects({"addons/geometry3Sharp.Clipper2/Clipper2Polygons.cs": "M"}, tree), [n.PRIMARY])
        self.assertEqual(n.active_projects({}, self.make_tree(n.PRIMARY)), [n.PRIMARY])

    def test_build_keeps_all_output_and_the_first_failure(self):
        calls = []

        def fake_run(cmd, cwd=None, check=True):
            calls.append(cmd[2])
            code = 1 if "Clipper2" in cmd[2] else 0
            return type("P", (), {"returncode": code, "stdout": "out of " + cmd[2] + "\n"})()
        original, n.run = n.run, fake_run
        try:
            result = n.build("/t", list(n.TEST_PROJECTS))
        finally:
            n.run = original
        self.assertEqual(len(calls), 2)
        self.assertEqual(result.returncode, 1)
        self.assertIn("geometry3Sharp.Tests", result.stdout)
        self.assertIn("Clipper2", result.stdout)
        original, n.run = n.run, lambda cmd, cwd=None, check=True: type("P", (), {"returncode": 0, "stdout": ""})()
        try:
            self.assertEqual(n.build("/t", [n.PRIMARY]).returncode, 0)
        finally:
            n.run = original

    def test_the_library_paths_reset_to_the_base_include_the_addon(self):
        self.assertTrue(n.is_library_path("addons/geometry3Sharp.Clipper2/Clipper2Polygons.cs"))
        self.assertFalse(n.is_library_path("tests/geometry3Sharp.Clipper2.Tests/Clipper2OffsetTests.cs"))
        self.assertTrue(all(p.startswith(n.TEST_SOURCE_PREFIXES) for p in n.TEST_PROJECTS))


class Decision(unittest.TestCase):
    def test_classify(self):
        new = {"N.C.A", "N.C.T", "N.C.Lost", "N.C.NewApi", "N.C.Skipped", "nice name"}
        compiled = {"N.C.A", "N.C.T", "N.C.Lost", "N.C.Skipped"}
        outcomes = {"N.C.A": ["Passed"], "N.C.T": ["Passed", "Failed"], "N.C.Skipped": ["NotExecuted"]}
        self.assertEqual(n.classify(new, compiled, outcomes), {
            "N.C.A": "passes", "N.C.T": "fails", "N.C.Lost": "no result", "N.C.NewApi": "does not compile",
            "N.C.Skipped": "skipped", "nice name": "not a method name (DisplayName), not run"})
        # a run killed by the blame collector (crash, hang): the test without a result crashed or hung
        self.assertEqual(n.classify(new, compiled, outcomes, aborted_by_blame=True)["N.C.Lost"], "crash or hang")
        # a theory with a skipped and a passing case passes
        self.assertEqual(n.verdict_of(["NotExecuted", "Passed"], False), "passes")

    def test_report_informational_never_fails(self):
        self.assertEqual(n.report({"A": "passes"}, set(), False)[1], 0)
        self.assertEqual(n.report({}, set(), False)[1], 0)
        text, code = n.report({}, set(), False, problem="the tests of the base do not build")
        self.assertEqual(code, 0)
        self.assertIn("could not be made", text)

    def test_report_bugfix_needs_a_failing_test(self):
        self.assertEqual(n.report({"A": "passes", "B": "fails"}, set(), True)[1], 0)
        self.assertEqual(n.report({"A": "passes", "B": "crash or hang"}, set(), True)[1], 0)
        for other in ("does not compile", "skipped", "no result", "not a method name (DisplayName), not run"):
            self.assertEqual(n.report({"A": "passes", "B": other}, set(), True)[1], 1, other)
        self.assertEqual(n.report({"A": "passes"}, set(), True)[1], 1)
        text, code = n.report({}, set(), True)
        self.assertEqual(code, 1)
        self.assertIn("no new test fails on the base", text)
        self.assertEqual(n.report({}, set(), True, problem="x")[1], 1)


if __name__ == "__main__":
    unittest.main()
