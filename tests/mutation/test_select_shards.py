#!/usr/bin/env python3
"""Tests of select_shards.py, on the real configs. Run by the changes job of the mutation workflow."""
import fnmatch
import os
import re
import subprocess
import sys
import unittest

from select_shards import TESTS, load_configs, select

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONFIGS = load_configs()
ALL_MESH = {"rounding", "boolean-1", "boolean-2", "arrangement-1", "arrangement-2", "planar"}


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

    def test_shard_with_several_files_runs_in_full_unless_all_changed(self):
        # (--since would leave the unchanged file out of the score)
        self.assertEqual(("none", {"planar": "full"}), run("mesh_ops/ExactTriangulation2.cs"))
        self.assertEqual(("full", {}), run("math/ExactPoint3.cs"))
        self.assertEqual(("full", {}), run("math/ExactPoint3.cs", "math/ExactPredicates.cs"))
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
