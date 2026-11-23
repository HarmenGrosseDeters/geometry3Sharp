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

    def test_master_runs_share_one_group_and_queue(self):
        # a group per SHA let every merge run to the end (20 runs piled up); cancelling master runs lost commits' results
        block = re.search(r"\nconcurrency:\n(.*?)\n\n", self.text, re.S).group(1)
        self.assertNotIn("github.sha", block)
        self.assertIn("github.event.pull_request.number || github.ref", block)
        self.assertIn("cancel-in-progress: ${{ github.event_name == 'pull_request' }}", block)

    def test_draft_gate_is_on_scope_and_everything_needs_it(self):
        header = self.job("scope").split("steps:")[0]
        self.assertRegex(header, r"(^|\n)    if: github.event_name != 'pull_request' \|\| github.event.pull_request.draft == false\n")
        # labeled starts the full stage (label full-ci, owner 2026-11-23); a labeled run of a ready PR is a complete run (scope runs,
        # so no job is skipped that ran before on the same head, except quick, which the full stage covers). No unlabeled: a run
        # without the label on a head that had a full run would replace its real checks by skipped ones
        self.assertRegex(self.text, r"pull_request:\n    types: \[opened, synchronize, reopened, ready_for_review, labeled\]\n")
        self.assertNotIn("unlabeled", re.search(r"pull_request:\n    types: (.*)\n", self.text).group(1))
        names = re.findall(r"\n  ([a-z0-9-]+):\n(?:    [^\n]*\n|\s*#[^\n]*\n)*?    (?:runs-on|name|needs|if):", self.text.split("\njobs:\n")[1])
        self.assertGreaterEqual(len(set(names)), 5)
        for name in set(names) - {"scope"}:
            self.assertRegex(self.job(name).split("steps:")[0], r"needs: (scope|\[scope)", name)

    def gate(self, event_name, draft=False):
        header = self.job("scope").split("steps:")[0]
        expr = re.search(r"^    if: (.*)$", header, re.M).group(1)

        class Ns:
            pass

        g = Ns()
        g.event_name = event_name
        g.event = Ns()
        g.event.pull_request = Ns()
        g.event.pull_request.draft = draft
        py = expr.replace("&&", " and ").replace("||", " or ").replace("false", "False")
        return bool(eval(py, {"github": g}))

    def test_gate_event_matrix(self):
        for ev in ("push", "workflow_dispatch"):
            self.assertTrue(self.gate(ev, draft=False))
        self.assertTrue(self.gate("pull_request", draft=False))
        self.assertFalse(self.gate("pull_request", draft=True))

    def test_skipped_runs_have_their_own_group(self):
        # the only skipped run is one of a draft; it must not cancel a real run of the PR (a ready PR run under way, then a draft push)
        group = re.search(r"\n  group: (.*)\n", self.text).group(1)
        self.assertIn("github.event.pull_request.draft && '-skipped'", group)

    def test_windows_jobs_skip_drafts_like_isolated(self):
        condition = "github.event_name != 'pull_request' || github.event.pull_request.draft == false"
        self.assertIn(condition, self.job("isolated").split("steps:")[0])
        header = self.job("netstandard").split("steps:")[0]
        self.assertIn("runs-on: windows-latest", header)
        self.assertRegex(header, r"\n    if: needs\.scope\.outputs\.full == 'true' && \(" + re.escape(condition) + r"\)\n")
        # its two shards (netstandard, netstandard-b) are the matrix of that one job
        self.assertIn("name: netstandard-b", header)


class TwoStageWorkflow(unittest.TestCase):
    """Owner 2026-11-23 (runner load): quick stage on every event of a ready PR, the full matrix only with the label full-ci, the
    commit status full-ci as the merge gate, and batched master runs instead of one per merge."""

    @classmethod
    def setUpClass(cls):
        with open(os.path.join(ROOT, ".github", "workflows", "ci.yml"), encoding="utf-8") as f:
            cls.text = f.read()
        cls.on = cls.text.split("\njobs:\n")[0]

    def job(self, name):
        return re.search(r"\n  %s:\n(.*?)(?=\n  [a-z0-9-]+:\n|\Z)" % name, self.text, re.S).group(1)

    def header(self, name):
        return self.job(name).split("steps:")[0]

    def test_full_stage_jobs_need_the_full_output(self):
        for name in ("linux", "isolated", "time-rule", "netstandard", "tier-rule"):
            self.assertIn("needs.scope.outputs.full == 'true'", self.header(name), name)
        # (the tier rule is a merge-gate check: in ci.yml, so the status covers it; tier-rule.yml has no pull_request trigger)
        with open(os.path.join(ROOT, ".github", "workflows", "tier-rule.yml"), encoding="utf-8") as f:
            self.assertNotIn("pull_request", f.read().split("\njobs:")[0].replace("# (pull requests", ""))

    def test_quick_runs_on_a_pr_exactly_when_the_full_stage_does_not(self):
        h = self.header("quick")
        self.assertRegex(h, r"\n    if: github\.event_name == 'pull_request' && needs\.scope\.outputs\.full != 'true'\n")
        q = self.job("quick")
        self.assertIn("dotnet build geometry3Sharp_netstandard.csproj -c Release\n", q)     # both target frameworks
        self.assertIn("dotnet build tests/geometry3Sharp.Tests -c Release -f net10.0", q)
        self.assertIn("tools/ci_scope.py --quick", q)
        self.assertIn('--filter "$(cat filter.txt)"', q)
        self.assertIn("timeout-minutes: 20", h)

    def test_the_full_output_comes_from_the_label_the_batch_or_always(self):
        step = self.job("scope").split("- id: full")[1]
        self.assertIn("FULL_LABEL: ${{ contains(github.event.pull_request.labels.*.name, 'full-ci') }}", step)
        self.assertIn('echo "full=$FULL_LABEL"', step)
        self.assertIn("schedule)\n              python3 tools/master_batch.py", step)
        self.assertIn('*)\n              echo "full=true"', step)
        self.assertIn("full: ${{ steps.full.outputs.full }}", self.header("scope"))

    def test_master_is_batched(self):
        push = self.on.split("  push:\n")[1].split("\n  pull_request:")[0]
        self.assertNotIn("branches", push)           # no run per merge
        self.assertIn("tags: ['**']", push)
        self.assertRegex(self.on, r"\n  schedule:\n    - cron: '\d+ \*/2 \* \* \*'")
        self.assertIn("workflow_dispatch:", self.on)
        self.assertIn("actions: read", self.header("scope"))

    def test_the_status_is_the_merge_gate(self):
        h = self.header("full-ci-status")
        self.assertIn("needs: [scope, linux, isolated, time-rule, netstandard, tier-rule]", h)
        self.assertIn("always()", h)
        self.assertIn("statuses: write", h)
        body = self.job("full-ci-status")
        self.assertIn("context=full-ci", body)
        self.assertIn('SHA: ${{ github.event.pull_request.head.sha }}', body)
        # pending without the label; success only from linux and netstandard passed
        self.assertIn('if [ "$FULL" != true ]; then\n            state=pending', body)
        self.assertIn('[ "$LINUX" = success ] && [ "$NETSTANDARD" = success ] && [ "$TIER_RULE" = success ]', body)

    def test_the_other_full_stage_workflows_need_the_label(self):
        for name in ("new-tests-on-base.yml", "determinism.yml"):
            with open(os.path.join(ROOT, ".github", "workflows", name), encoding="utf-8") as f:
                text = f.read()
            self.assertIn("contains(github.event.pull_request.labels.*.name, 'full-ci')", text, name)
            self.assertRegex(text, r"types: \[[^\]]*\blabeled\b", name)


class DocsOnly(unittest.TestCase):
    """A PR of Markdown only never runs ci.yml (its paths), so docs.yml posts the merge gate `full-ci` for it (review: without that
    such a PR would wait forever for a required `full-ci`)."""

    def test_docs_only(self):
        self.assertTrue(c.docs_only(["docs/a.md", "README.md"]))
        self.assertFalse(c.docs_only(["docs/a.md", "tools/check_docs.py"]))
        self.assertFalse(c.docs_only(["docs/design/FIXED_WIDTH_EXACT_POINTS.md"]))
        self.assertFalse(c.docs_only([]))

    def test_the_exceptions_are_those_of_ci_yml(self):
        with open(os.path.join(ROOT, ".github", "workflows", "ci.yml"), encoding="utf-8") as f:
            text = f.read()
        paths = re.search(r"\n  pull_request:\n(?:    [^\n]*\n)*?    paths: \[(.*)\]\n", text).group(1)
        items = [x.strip().strip("'") for x in paths.split(",")]
        self.assertEqual(["**", "!**/*.md"], items[:2])
        self.assertEqual(sorted(c.CI_MARKDOWN), sorted(items[2:]))

    def test_docs_yml_posts_the_gate_only_for_docs_only_prs(self):
        with open(os.path.join(ROOT, ".github", "workflows", "docs.yml"), encoding="utf-8") as f:
            text = f.read()
        self.assertIn("statuses: write", text)
        self.assertIn("tools/ci_scope.py --docs-only", text)
        self.assertIn("context=full-ci", text)
        self.assertIn("steps.docs_only.outputs.docs_only == 'true'", text)


class Quick(unittest.TestCase):
    """tools/ci_scope.py --quick: the net10.0 test classes of the changed areas."""

    def setUp(self):
        import tempfile
        self.root = tempfile.mkdtemp()
        self.addCleanup(__import__("shutil").rmtree, self.root)

    def write(self, path, text):
        full = os.path.join(self.root, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8") as f:
            f.write(text)

    def case(self, cls, uses=""):
        return "namespace g3.Tests {\n public class %s {\n  [Fact] public void A() { %s }\n }\n}\n" % (cls, uses)

    def quick(self, *paths):
        return c.quick_tests(paths, root=self.root)

    def test_a_changed_test_file_runs_its_classes(self):
        self.write(c.TEST_DIR + "FooTests.cs", self.case("FooTests") + "class Helper {}\n")
        r = self.quick(c.TEST_DIR + "FooTests.cs")
        self.assertIn("FooTests", r["classes"])
        self.assertNotIn("Helper", r["classes"])
        self.assertFalse(r["broad"])
        self.assertTrue(set(c.SMOKE) <= set(r["classes"]))

    def test_a_partial_class_file_gives_its_class(self):
        self.write(c.TEST_DIR + "FooTestsExtra.cs", "// class NotThis\n" + self.case("FooTestsExtra"))
        self.assertIn("FooTestsExtra", self.quick(c.TEST_DIR + "FooTestsExtra.cs")["classes"])
        self.assertNotIn("NotThis", self.quick(c.TEST_DIR + "FooTestsExtra.cs")["classes"])

    def test_a_library_file_runs_the_tests_that_use_its_types(self):
        self.write("mesh_ops/Widget.cs", "namespace g3 {\n public class Widget {\n  class Before {}\n }\n}\n")
        self.write(c.TEST_DIR + "WidgetTests.cs", self.case("WidgetTests", "new Widget();"))
        self.write(c.TEST_DIR + "OtherTests.cs", self.case("OtherTests", "Widget w;"))
        self.write(c.TEST_DIR + "UnrelatedTests.cs", self.case("UnrelatedTests", "Before b; Widgets x;"))
        r = self.quick("mesh_ops/Widget.cs")
        self.assertIn("WidgetTests", r["classes"])
        self.assertIn("OtherTests", r["classes"])
        self.assertNotIn("UnrelatedTests", r["classes"])      # (a nested type and a longer word do not count)
        self.assertFalse(r["broad"])

    def test_a_partial_library_file_counts_for_its_class(self):
        self.write("mesh_ops/WidgetIncremental.cs", "namespace g3 { public partial class Widget { } }\n")
        self.write(c.TEST_DIR + "OtherTests.cs", self.case("OtherTests", "Widget w;"))
        self.assertIn("OtherTests", self.quick("mesh_ops/WidgetIncremental.cs")["classes"])

    def test_a_widely_used_type_is_broad(self):
        self.write("math/Vector.cs", "namespace g3 { public struct Vector { } }\n")
        for i in range(c.MAX_CLASSES + 1):
            self.write(c.TEST_DIR + "Use%dTests.cs" % i, self.case("Use%dTests" % i, "Vector v;"))
        self.write(c.TEST_DIR + "VectorTests.cs", self.case("VectorTests", "Vector v;"))
        r = self.quick("math/Vector.cs")
        self.assertTrue(r["broad"])
        self.assertEqual(sorted(set(c.SMOKE) | {"VectorTests"}), r["classes"])
        self.assertIn("Broad change: smoke set only", c.quick_summary(r))

    def test_harness_build_settings_and_data_are_broad(self):
        for p in (c.TEST_DIR + "Harness/Oracle.cs", "geometry3Sharp_netstandard.csproj", "BannedSymbols.txt",
                  c.TEST_DIR + "geometry3Sharp.Tests.csproj"):
            r = self.quick(p)
            self.assertTrue(r["broad"], p)
            self.assertEqual(sorted(c.SMOKE), r["classes"], p)

    def test_tools_docs_and_other_projects_run_no_test(self):
        for p in ("tools/a.py", "docs/x.md", "benchmarks/B/Program.cs", "tests/g3cli.Tests/A.cs", "addons/geometry3Sharp.Clipper2/X.cs"):
            r = self.quick(p)
            self.assertEqual([], r["classes"], p)
            self.assertFalse(r["broad"], p)
        self.assertEqual("", c.quick_filter([]))

    def test_files_the_tests_read_and_doc_examples(self):
        self.assertIn("DocExamplesTests", self.quick("docs/OPERATIONS.md")["classes"])
        self.assertIn("DocExamplesTests", self.quick("docs/examples/Ex.cs")["classes"])
        self.assertIn("RobustnessSweepTests", self.quick("docs/ROBUSTNESS_SWEEP.md")["classes"])

    def test_a_deleted_file_is_left_to_the_build(self):
        r = self.quick("mesh_ops/Gone.cs")
        self.assertFalse(r["broad"])
        self.assertEqual(sorted(c.SMOKE), r["classes"])

    def test_the_filter(self):
        f = c.quick_filter(["BTests", "ATests"])
        self.assertEqual("(FullyQualifiedName~.ATests.|FullyQualifiedName~+ATests.|FullyQualifiedName~.BTests.|FullyQualifiedName~+BTests.)"
                         "&Category!=Slow&Category!=Nightly", f)
        # (a class of the global namespace has the FullyQualifiedName `C.M`)
        self.assertEqual("(FullyQualifiedName~GTests.)&Category!=Slow&Category!=Nightly", c.quick_filter(["GTests"], ["GTests"]))

    def test_a_class_without_a_namespace_is_marked_global(self):
        self.write(c.TEST_DIR + "Spatial/GTests.cs", "using Xunit;\npublic class GTests {\n [Fact] public void A() {}\n}\n")
        self.write(c.TEST_DIR + "NTests.cs", self.case("NTests"))
        r = self.quick(c.TEST_DIR + "Spatial/GTests.cs", c.TEST_DIR + "NTests.cs")
        self.assertEqual(["GTests"], r["global"])
        self.assertIn("GTests", r["classes"])
        real = c.quick_tests([c.TEST_DIR + "Spatial/NtsQuadtreeComparisonTests.cs"])
        self.assertIn("NtsQuadtreeComparisonTests", real["global"])

    def test_the_smoke_classes_exist_and_are_not_slow(self):
        files = c.test_files(ROOT)
        for cls in c.SMOKE:
            hits = [f for f in files if cls in c.test_classes_of(open(os.path.join(ROOT, f), encoding="utf-8").read())]
            self.assertTrue(hits, cls)
            for f in hits:
                self.assertNotIn('"Slow"', open(os.path.join(ROOT, f), encoding="utf-8").read(), cls)

    def test_on_the_real_tree(self):
        r = c.quick_tests(["mesh_ops/MeshRoundingRepair.cs"])
        self.assertIn("MeshRoundingRepairTests", r["classes"])
        self.assertLessEqual(len(r["classes"]), c.MAX_CLASSES + len(c.SMOKE) + 20)

    def test_cli(self):
        import subprocess
        out = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "ci_scope.py"), "--quick"], input="tools/a.py\n",
                             capture_output=True, text=True, check=True).stdout.splitlines()
        self.assertEqual(["filter=", "broad=false"], out)


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
        for path in ("benchmarks/net48-compare/run.ps1", "benchmarks/BooleanComparison/Profile.cs", "benchmarks/SmallInputWrapBenchmark/Program.cs"):
            r = c.scope([path])
            self.assertEqual((r["library"], r["g3cli"], r["ps1"]), (False, False, True), path)
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
