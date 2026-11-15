"""Unit tests of tools/ci_scope.py (python3 -m unittest discover -s tools -p 'test_ci_scope.py')."""

import glob
import os
import re
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ci_scope as c  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


class CiWorkflow(unittest.TestCase):
    """Runner load (2026-11-13): the conditions of ci.yml that keep master and draft PRs cheap."""

    @classmethod
    def setUpClass(cls):
        with open(os.path.join(ROOT, ".github", "workflows", "ci.yml"), encoding="utf-8") as f:
            cls.text = f.read()

    def job(self, name):
        return re.search(r"\n  %s:\n(.*?)(?=\n  [a-z0-9-]+:\n|\Z)" % name, self.text, re.S).group(1)

    def test_master_runs_share_one_group_that_cancels(self):
        # a group per SHA let every merge run to the end (20 runs piled up)
        block = re.search(r"\nconcurrency:\n(.*?)\n\n", self.text, re.S).group(1)
        self.assertNotIn("github.sha", block)
        self.assertIn("github.event.pull_request.number || github.ref", block)
        self.assertIn("cancel-in-progress: true", block)

    def test_windows_jobs_skip_drafts_like_isolated(self):
        condition = "github.event_name != 'pull_request' || github.event.pull_request.draft == false"
        self.assertIn(condition, self.job("isolated").split("steps:")[0])
        header = self.job("netstandard").split("steps:")[0]
        self.assertIn("runs-on: windows-latest", header)
        self.assertRegex(header, r"\n    if: " + re.escape(condition) + r"\n")
        # its two shards (netstandard, netstandard-b) are the matrix of that one job
        self.assertIn("name: netstandard-b", header)


class Scope(unittest.TestCase):
    def test_library_and_tests_run_everything(self):
        for p in ("math/Vector2d.cs", "tests/geometry3Sharp.Tests/FooTests.cs", ".github/workflows/ci.yml",
                  "geometry3Sharp_netstandard.csproj", "BannedSymbols.txt", "addons/geometry3Sharp.Clipper2/X.cs",
                  "tests/g3cli.Tests/A.cs", "docs/design/FIXED_WIDTH_EXACT_POINTS.md", "docs/examples/Ex.cs",
                  "docs/OPERATIONS.md", "docs/ROBUSTNESS_SWEEP.md", "docs/design/DETERMINISM_PROOF.md"):
            self.assertTrue(c.scope([p])["library"], p)

    def test_tools_benchmarks_docs_only(self):
        for p in ("tools/new_tests_on_base.py", "tools/ReaderFuzzPilot/a.py", "benchmarks/MeshRepairBenchmark/x.py",
                  "benchmarks/TerrainBenchmark/Program.cs", "docs/design/OTHER.md", "tools/g3cli/Program.cs",
                  "tools/PredicateCompiler/Program.cs", "benchmarks/Meshing2Benchmark/Program.cs"):
            self.assertFalse(c.scope([p])["library"], p)

    def test_shared_files_count_as_library(self):
        for p in ("benchmarks/QuadtreeBenchmark/Q.cs", "benchmarks/Meshing2Benchmark/Oracle/O.cs",
                  "benchmarks/Meshing2Benchmark/golden/m3.json", "tools/analyzers/G3ExactCodeAnalyzers/A.cs",
                  "tools/PredicateCompiler/Compiler.cs", "tools/g3cli/TrianglePolyFormat.cs"):
            self.assertTrue(c.scope([p])["library"], p)

    def test_one_library_file_among_tools_runs_everything(self):
        self.assertTrue(c.scope(["tools/a.py", "math/Vector2d.cs", "docs/x.md"])["library"])

    def test_g3cli_and_ps1(self):
        r = c.scope(["tools/g3cli/Program.cs"])
        self.assertEqual((r["library"], r["g3cli"], r["ps1"]), (False, True, False))
        r = c.scope(["benchmarks/MeshRepairBenchmark/test_validity_reporting.ps1"])
        self.assertEqual((r["library"], r["g3cli"], r["ps1"]), (False, False, True))
        r = c.scope(["tools/new_tests_on_base.py"])
        self.assertEqual((r["library"], r["g3cli"], r["ps1"]), (False, False, False))

    def test_empty_change_list_is_not_a_library_change(self):
        self.assertFalse(c.scope([])["library"])


class SharedListIsComplete(unittest.TestCase):
    """Every file a test project or the library project includes from tools/, benchmarks/ or docs/ is on the list."""

    def test_included_paths_are_shared(self):
        # (tests/g3cli.Tests includes tools/g3cli: its tests run when that changes, the g3cli output)
        projects = [p for p in glob.glob(os.path.join(ROOT, "tests", "*", "*.csproj")) if "g3cli.Tests" not in p]
        projects.append(os.path.join(ROOT, "geometry3Sharp_netstandard.csproj"))
        problems = []
        for proj in projects:
            base = os.path.dirname(proj)
            text = open(proj, encoding="utf-8").read()
            for m in re.finditer(r'(?:Include|Update)="([^"]+)"', text):
                inc = m.group(1).replace("\\", "/")
                if "%(" in inc and "*" not in inc:
                    continue
                full = os.path.normpath(os.path.join(base, inc.split("*")[0]))
                rel = os.path.relpath(full, ROOT).replace("\\", "/")
                if not rel.startswith(c.TOOL_DIRS):
                    continue
                probe = rel if not inc.endswith("*.cs") and "*" not in inc else rel.rstrip("/") + "/probe"
                if inc.split("*")[0].endswith("/") or "*" in inc:
                    probe = rel.rstrip("/") + "/probe"
                if not c.affects_library_tests(probe):
                    problems.append(f"{os.path.relpath(proj, ROOT)}: {inc}")
        self.assertEqual(problems, [], "add these to SHARED in tools/ci_scope.py")


if __name__ == "__main__":
    unittest.main()
