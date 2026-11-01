#!/usr/bin/env python3
"""Tests of select_shards.py, on the real configs. Run by the changes job of the mutation workflow."""
import fnmatch
import os
import re
import subprocess
import sys
import unittest

from select_shards import ALWAYS, TESTS, load_configs, select, workflow_edit_is_trigger_only

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONFIGS = load_configs()
ALL_MESH = {"rounding", "boolean-1", "boolean-2", "arrangement-1", "arrangement-2", "planar", "fixed"}


def run(*changed):
    arithmetic, mesh = select(changed, CONFIGS)
    return arithmetic, {m["shard"]: m["mode"] for m in mesh}


class SelectShards(unittest.TestCase):
    def test_every_path_in_the_configs_exists(self):
        # a renamed file or test class would otherwise silently stop selecting its shard
        for name, (config, sources, tests) in CONFIGS.items():
            for path in {config} | sources | tests:
                self.assertTrue(os.path.isfile(os.path.join(ROOT, path)), f"{name}: {path}")

    def test_workflow_paths_cover_every_config(self):
        # GitHub starts the workflow only for these paths: one missing would skip its shard without a word
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            workflow = f.read()
        block = workflow[workflow.index("paths:"):workflow.index("schedule:")]
        patterns = re.findall(r"- '([^']+)'", block)
        for name, (config, sources, tests) in CONFIGS.items():
            for path in {config} | sources | tests:
                self.assertTrue(any(fnmatch.fnmatch(path, p) for p in patterns), f"{name}: {path} is not in the paths")

    def test_all_configs_are_known(self):
        self.assertEqual(ALL_MESH | {"config"}, {n.replace("mesh-", "") for n in CONFIGS})

    def test_unrelated_change_runs_nothing(self):
        self.assertEqual(("none", {}), run("docs/ROADMAP.md", "mesh/Reducer.cs", TESTS + "geometry3Sharp.Tests.csproj"))

    def test_source_runs_only_its_shards_incrementally(self):
        self.assertEqual(("none", {"rounding": "source"}), run("mesh_ops/MeshRoundingRepair.cs"))
        self.assertEqual(("none", {"boolean-1": "source", "boolean-2": "source"}), run("mesh_ops/ExactMeshBoolean.cs"))
        self.assertEqual(("none", {"arrangement-1": "source", "arrangement-2": "source"}),
                         run("mesh_ops/ExactMeshArrangement.cs"))
        self.assertEqual(("source", {}), run("math/ExactPoint3.cs", "math/ExactPredicates.cs", "math/AdaptivePredicates.cs"))

    def test_shard_with_several_files_runs_incrementally_when_one_changed(self):
        # (the unchanged files keep their baseline results)
        self.assertEqual(("none", {"planar": "source"}), run("mesh_ops/ExactTriangulation2.cs"))
        self.assertEqual(("source", {}), run("math/ExactPoint3.cs"))
        self.assertEqual(("source", {}), run("math/ExactPoint3.cs", "math/ExactPredicates.cs"))
        self.assertEqual(("none", {"planar": "source"}),
                         run("mesh_ops/ExactTriangulation2.cs", "mesh_ops/ExactPlanarSimplify.cs"))

    def test_test_file_runs_the_shards_that_use_it_for_a_report(self):
        self.assertEqual(("none", {"boolean-1": "tests", "boolean-2": "tests", "rounding": "tests"}),
                         run(TESTS + "ExactBooleanIssueCasesTests.cs"))
        self.assertEqual(("none", {"boolean-2": "tests"}), run(TESTS + "ExactBooleanFilterTests.cs"))
        self.assertEqual(("tests", {}), run(TESTS + "PredicateDifferentialTests.cs"))

    def test_test_class_with_a_method_filter_maps_to_its_file(self):
        arithmetic, mesh = run(TESTS + "MeshRoundingRepairTests.cs")
        self.assertEqual("tests", mesh["arrangement-1"])
        self.assertEqual("tests", mesh["rounding"])

    def test_source_wins_over_tests(self):
        self.assertEqual("source", run("mesh_ops/MeshRoundingRepair.cs", TESTS + "ExactBooleanIssueCasesTests.cs")[1]["rounding"])

    def test_config_wins_over_source(self):
        self.assertEqual(("none", {"rounding": "full"}),
                         run("mesh_ops/MeshRoundingRepair.cs", "tests/mutation/stryker-mesh-rounding.json"))
        self.assertEqual("full", run("math/ExactPoint3.cs", ".github/workflows/mutation.yml")[0])

    def test_config_change_runs_that_config_in_full(self):
        self.assertEqual(("none", {"planar": "full"}), run("tests/mutation/stryker-mesh-planar.json"))
        self.assertEqual(("full", {}), run("tests/mutation/stryker-config.json"))

    def test_tools_and_workflow_run_everything_in_full(self):
        for path in (".config/dotnet-tools.json", ".github/workflows/mutation.yml"):
            arithmetic, mesh = run(path)
            self.assertEqual("full", arithmetic)
            self.assertEqual({s: "full" for s in ALL_MESH}, mesh)

    WF = ("# header comment\n"
          "\n"
          "on:\n"
          "  pull_request:\n"
          "    paths:\n"
          "      - 'a.cs'\n"
          "      - 'b.cs'\n"
          "  schedule:\n"
          "    - cron: '23 3 * * 1'\n"
          "jobs:\n"
          "  x:\n"
          "    runs-on: ubuntu-latest\n"
          "    steps:\n"
          "      - run: >\n"
          "          jq . report.json || true\n")

    @staticmethod
    def _diff(old, new):
        import difflib
        return "".join(difflib.unified_diff(old.splitlines(True), new.splitlines(True), "a/w", "b/w", n=1))

    def _trigger_only(self, new):
        return workflow_edit_is_trigger_only(self._diff(self.WF, new), self.WF, new)

    def test_workflow_edit_of_trigger_paths_only(self):
        wf = self.WF
        self.assertTrue(self._trigger_only(wf.replace("      - 'b.cs'\n", "      - 'b.cs'\n      - 'c*.cs'   # new\n")))
        self.assertTrue(self._trigger_only(wf.replace("      - 'a.cs'\n", "")))
        self.assertTrue(self._trigger_only(wf.replace("      - 'a.cs'\n", "      # a comment\n\n      - 'a.cs'\n")))
        self.assertTrue(self._trigger_only(wf.replace("# header comment\n", "# header comment\n# more header\n")))

    def test_workflow_edit_that_changes_a_job_runs_in_full(self):
        wf = self.WF
        for new in (wf.replace("      - 'a.cs'\n", "      - 'x.cs'\n").replace("ubuntu-latest", "ubuntu-24.04"),
                    wf.replace("23 3 * * 1", "23 3 * * 2"),
                    wf + "    timeout-minutes: 9\n",
                    # a quoted list item outside the paths (review of #278)
                    wf.replace("    runs-on: ubuntu-latest\n", "    runs-on:\n      - 'self-hosted'\n"),
                    # a "comment" inside a folded run block is shell text (review of #278)
                    wf.replace("          jq . report.json", "          # note\n          jq . report.json"),
                    # a path line retyped, and a header line moved below on:
                    wf.replace("      - 'a.cs'\n", "      - 'a.cs' \n").replace("  x:\n", "  # c\n  x:\n"),
                    wf.replace("on:\n", "onn:\n"),
                    # a key in the header, and a line just outside the paths list (the region bounds)
                    wf.replace("# header comment\n", "name: x\n# header comment\n"),
                    wf.replace("    paths:\n", "    # x\n    paths:\n"),
                    wf.replace("  schedule:\n", "      - 'z.cs'\n  schedule:\n").replace("  pull_request:\n", "  pull_request:\n      - 'y.cs'\n")):
            self.assertFalse(self._trigger_only(new), new)
        self.assertFalse(workflow_edit_is_trigger_only("", wf, wf))

    def test_workflow_paths_cover_the_tooling(self):
        # removing the tooling from the trigger paths is a trigger-only edit, so it must be caught here (review of #278)
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            workflow = f.read()
        block = workflow[workflow.index("paths:"):workflow.index("schedule:")]
        patterns = re.findall(r"- '([^']+)'", block)
        for path in ALWAYS | {"tests/mutation/select_shards.py"}:
            self.assertTrue(any(fnmatch.fnmatch(path, p) for p in patterns), path)

    def test_command_line_and_the_workflow_diff(self):
        # on a temporary repository: a trigger-only edit of the workflow runs nothing by itself, a job edit everything,
        # a trigger-only edit with a source change only that source's shard, and a missing commit fails the step
        import shutil, tempfile
        tmp = tempfile.mkdtemp()
        try:
            def git(*a):
                return subprocess.run(["git", "-C", tmp, "-c", "user.name=t", "-c", "user.email=t@t"] + list(a),
                                      check=True, capture_output=True, text=True).stdout.strip()
            git("init", "-q")
            wf = os.path.join(tmp, ".github", "workflows")
            os.makedirs(wf)
            def write(text):
                with open(os.path.join(wf, "mutation.yml"), "w") as f:
                    f.write(text)
            write(self.WF)
            git("add", "-A"); git("commit", "-q", "-m", "a")
            base = git("rev-parse", "HEAD")
            def commit(text):
                write(text)
                git("commit", "-q", "-am", "b")
                return git("rev-parse", "HEAD")
            paths_head = commit(self.WF.replace("      - 'b.cs'\n", "      - 'b.cs'\n      - 'c.cs'\n"))
            job_head = commit(self.WF.replace("ubuntu-latest", "ubuntu-24.04"))
            def cli(head, changed=".github/workflows/mutation.yml\n"):
                return subprocess.run([sys.executable, os.path.join(HERE, "select_shards.py")], input=changed,
                                      capture_output=True, text=True, cwd=tmp, env=dict(os.environ, BASE=base, HEAD=head))
            self.assertEqual((0, "arithmetic=none\nmesh=[]\n"), (cli(paths_head).returncode, cli(paths_head).stdout))
            self.assertIn("arithmetic=full", cli(job_head).stdout)
            out = cli(paths_head, ".github/workflows/mutation.yml\nmesh_ops/MeshRoundingRepair.cs\n").stdout
            self.assertEqual('arithmetic=none\nmesh=[{"shard": "rounding", "mode": "source"}]\n', out)
            self.assertNotEqual(0, cli("0" * 40).returncode)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    def test_all_flag_runs_everything_in_full(self):
        arithmetic, mesh = select([], CONFIGS, run_all=True)
        self.assertEqual("full", arithmetic)
        self.assertEqual({s: "full" for s in ALL_MESH}, {m["shard"]: m["mode"] for m in mesh})

    def test_only_runs_the_named_shards_in_full(self):
        arithmetic, mesh = select([], CONFIGS, run_all=True, only={"rounding", "arithmetic"})
        self.assertEqual(("full", [{"shard": "rounding", "mode": "full"}]), (arithmetic, mesh))
        arithmetic, mesh = select([], CONFIGS, run_all=True, only={"planar"})
        self.assertEqual(("none", [{"shard": "planar", "mode": "full"}]), (arithmetic, mesh))
        with self.assertRaises(ValueError):
            select([], CONFIGS, run_all=True, only={"rounding", "typo"})

    def test_command_line(self):
        def cli(*args):
            p = subprocess.run([sys.executable, os.path.join(HERE, "select_shards.py")] + list(args), input="",
                               capture_output=True, text=True)
            return p.returncode, p.stdout
        self.assertEqual((0, 'arithmetic=none\nmesh=[{"shard": "rounding", "mode": "full"}]\n'),
                         cli("--all", "--only", "rounding"))
        code, out = cli("--all", "--only", "all")
        self.assertEqual(0, code)
        self.assertIn("arithmetic=full", out)
        self.assertNotEqual(0, cli("--all", "--only", "nonsense")[0])


if __name__ == "__main__":
    unittest.main()
