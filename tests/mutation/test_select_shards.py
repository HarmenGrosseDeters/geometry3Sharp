#!/usr/bin/env python3
"""Tests of select_shards.py, on the real configs. Run by the changes job of the mutation workflow."""
import difflib
import fnmatch
import json
import os
import re
import subprocess
import sys
import unittest

from select_shards import ALWAYS, pr_selection, eligible, expand_only, shard_name, TESTS, config_edit_moves_only_spans, load_configs, mode_of, select, test_class, workflow_edit_forces_full

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONFIGS = load_configs()
RO = sorted("rounding-%d" % i for i in range(1, 16))  # (in the order of the config file names)
BO = ["boolean-%d" % i for i in range(1, 10)]
ALL_MESH = set(RO) | set(BO) | {"arrangement-1", "arrangement-2", "arrangement-3", "arrangement-4", "planar", "fixed", "generated", "envelope-1", "envelope-2", "envelope-3", "envelope-4",
                                                "imprint", "terrain-arith", "terrain-kernel", "terrain-overlay", "hausdorff", "geodesic-1", "geodesic-2",
                                                "uv-atlas", "uv-charts", "uv-pack", "uv-cones", "uv-slim", "uv-foldover", "uv-core", "solid-pipeline", "exact-winding", "cells",
                                                "predicates-1", "predicates-2", "adaptive", "box3", "ray-triangle", "snaprounding", "separating-axes", "bisector-1", "bisector-2", "rdr-1", "rdr-2"}


def run(*changed):
    arithmetic, mesh = select(changed, CONFIGS)
    return arithmetic, {m["shard"]: m["mode"] for m in mesh}


class SelectShards(unittest.TestCase):
    def test_every_exact_path_of_the_editorconfig_is_gated(self):
        # the gate list is the exact-code section of .editorconfig: every existing file that its globs match is gated, and the
        # shards that mutate such a file are among the gated ones (no second hand-kept list)
        import glob
        from select_shards import exact_globs, exact_gated
        globs = exact_globs()
        self.assertIn("mesh_ops/ExactMesh*.cs", globs)
        self.assertIn("math/ExactPredicates.cs", [g for g in globs] + ["math/ExactPredicates.cs"])
        files = {os.path.relpath(f, ROOT).replace(os.sep, "/") for g in globs for f in glob.glob(os.path.join(ROOT, g))}
        self.assertGreater(len(files), 20)
        for f in files:
            self.assertTrue(exact_gated(f), f)
        mutated = set().union(*(c[1] for c in CONFIGS.values()))
        for f in mutated & files:
            self.assertTrue(any(eligible(c, [f]) for c in CONFIGS.values()), f)
        self.assertFalse(exact_gated("mesh_ops/MeshRoundingRepair.cs"))
        with self.assertRaises(ValueError):
            exact_globs("[*.cs]\nx=1\n")

    def test_eligible_is_exact_files_of_the_config_or_the_label(self):
        configs = load_configs()
        self.assertTrue(eligible(configs["config"], ["math/ExactPoint3.cs"]))
        self.assertTrue(eligible(configs["mesh-predicates-1"], ["math/ExactPredicates.cs"]))
        self.assertTrue(eligible(configs["mesh-adaptive"], ["math/AdaptivePredicates.cs"]))
        self.assertTrue(eligible(configs["mesh-box3"], ["intersection/IntrExactBox3.cs"]))
        self.assertTrue(eligible(configs["mesh-separating-axes"], ["intersection/TriangleSeparatingAxes.cs"]))
        self.assertFalse(eligible(configs["mesh-box3"], ["intersection/IntrRay3Box3.cs"]))        # (not an exact-code file: its decision is the shared one)
        self.assertTrue(eligible(configs["mesh-ray-triangle"], ["math/ExactRayTriangle.cs"]))
        self.assertFalse(eligible(configs["mesh-ray-triangle"], ["intersection/IntrRay3Triangle3.cs"]))        # (not an exact-code file: a caller of the exact decision)
        self.assertFalse(eligible(configs["config"], ["math/ExactPredicates.cs"]))        # (moved to the predicates shards)
        self.assertTrue(eligible(configs["mesh-fixed"], ["math/FixedPoint3.cs"]))
        self.assertTrue(eligible(configs["mesh-generated"], ["math/Generated/IndirectPredicates.cs"]))
        self.assertTrue(eligible(configs["mesh-bisector-1"], ["math/Generated/BisectorPredicates.cs"]))      # (exact: math/Generated/*.cs)
        self.assertTrue(eligible(configs["mesh-bisector-2"], ["math/ExactBisectorSoS.cs"]))                 # (exact: math/Exact*.cs)
        self.assertTrue(eligible(configs["mesh-rdr-1"], ["mesh_ops/ExactRestrictedVoronoi.cs"]))            # (exact: named in .editorconfig)
        self.assertTrue(eligible(configs["mesh-rdr-2"], ["mesh_ops/RestrictedDelaunayRefinement.cs"]))
        self.assertFalse(eligible(configs["mesh-rdr-2"], ["mesh_ops/ExactRestrictedVoronoi.cs"]))           # (not mutated by this shard)
        self.assertFalse(eligible(configs["mesh-generated"], ["math/Generated/BisectorPredicates.cs"]))   # (not mutated by that shard)
        self.assertFalse(eligible(configs["mesh-fixed"], ["math/ExactPredicates.cs"]))        # (not mutated by this shard)
        self.assertTrue(eligible(configs["mesh-boolean-1"], ["mesh_ops/ExactMeshBoolean.cs"]))    # (mesh_ops/ExactMesh*: exact code)
        self.assertTrue(eligible(configs["mesh-arrangement-2"], ["mesh_ops/ExactMeshArrangement.cs"]))
        self.assertFalse(eligible(configs["mesh-boolean-1"], ["mesh_ops/ExactMeshArrangement.cs"]))   # (not mutated by this shard)
        self.assertTrue(eligible(configs["mesh-envelope-1"], ["mesh_ops/PolyhedralEnvelope.cs"]))   # (exact in .editorconfig)
        # non-exact code: only with the label `mutation` (an error bound outside the exact files, set by the coordinator)
        self.assertFalse(eligible(configs["mesh-rounding-1"], ["mesh_ops/MeshRoundingRepair.cs"]))
        self.assertTrue(eligible(configs["mesh-rounding-1"], ["mesh_ops/MeshRoundingRepair.cs"], ["mutation"]))
        self.assertFalse(eligible(configs["mesh-rounding-1"], ["mesh_ops/MeshRoundingRepair.cs"], ["bugfix", "full-ci"]))
        self.assertFalse(eligible(configs["mesh-planar"], ["mesh_ops/MeshRoundingRepair.cs"], ["mutation"]))
        # the scripts of the runs are no reason for a PR run of any shard
        self.assertFalse(eligible(configs["mesh-fixed"], ["tests/mutation/run_stryker.sh"]))

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
            for path in {config} | sources:
                self.assertTrue(any(fnmatch.fnmatch(path, p) for p in patterns), f"{name}: {path} is not in the paths")
        # (owner 2026-11-23) a change of tests only makes no PR run, so no test file starts the workflow
        self.assertFalse([p for p in patterns if p.startswith(TESTS)], "test files in the trigger paths")

    def test_all_configs_are_known(self):
        self.assertEqual(ALL_MESH | {"config"}, {n.replace("mesh-", "") for n in CONFIGS})

    def test_unrelated_change_runs_nothing(self):
        self.assertEqual(("none", {}), run("docs/ROADMAP.md", "mesh/Reducer.cs", TESTS + "geometry3Sharp.Tests.csproj"))

    def test_source_runs_only_its_shards_incrementally(self):
        self.assertEqual(("none", {r: "source" for r in RO}), run("mesh_ops/MeshRoundingRepair.cs"))
        self.assertEqual(("none", {b: "source" for b in BO}), run("mesh_ops/ExactMeshBoolean.cs"))
        self.assertEqual(("none", {"arrangement-1": "source", "arrangement-2": "source", "arrangement-3": "source", "arrangement-4": "source"}),
                         run("mesh_ops/ExactMeshArrangement.cs"))
        self.assertEqual(("source", {"predicates-1": "source", "predicates-2": "source", "adaptive": "source"}),
                         run("math/ExactPoint3.cs", "math/ExactPredicates.cs", "math/AdaptivePredicates.cs"))

    def test_shard_with_several_files_runs_incrementally_when_one_changed(self):
        # (the unchanged files keep their baseline results)
        self.assertEqual(("none", {"planar": "source"}), run("mesh_ops/ExactTriangulation2.cs"))
        self.assertEqual(("source", {}), run("math/ExactPoint3.cs"))
        self.assertEqual(("source", {"predicates-1": "source", "predicates-2": "source"}), run("math/ExactPoint3.cs", "math/ExactPredicates.cs"))
        self.assertEqual(("none", {"planar": "source"}),
                         run("mesh_ops/ExactTriangulation2.cs", "mesh_ops/ExactPlanarSimplify.cs"))

    def test_test_file_runs_the_shards_that_use_it_for_a_report(self):
        # (the boolean parts hold coverage-based test sets, coverage_select.py: a part runs the classes of its own set)
        self.assertEqual(("none", dict({"boolean-1": "tests", "boolean-5": "tests", "boolean-7": "tests"}, **{r: "tests" for r in RO})),
                         run(TESTS + "ExactBooleanIssueCasesTests.cs"))
        self.assertEqual(("none", {"boolean-5": "tests", "boolean-7": "tests"}), run(TESTS + "ExactBooleanFilterTests.cs"))
        self.assertEqual(("tests", {"predicates-1": "tests", "predicates-2": "tests", "adaptive": "tests"}), run(TESTS + "PredicateDifferentialTests.cs"))

    def test_test_class_with_a_method_filter_maps_to_its_file(self):
        arithmetic, mesh = run(TESTS + "MeshRoundingRepairTests.cs")
        self.assertEqual("tests", mesh["arrangement-1"])
        for r in RO:
            self.assertEqual("tests", mesh[r])

    def test_filter_with_and_and_a_negated_term_maps_to_the_positive_class(self):
        # terrain-overlay: "(FullyQualifiedName~TerrainSlopeCatchLineTests&FullyQualifiedName!~RandomTerrains)|..."
        self.assertEqual(("none", {"terrain-overlay": "tests"}), run(TESTS + "TerrainSlopeCatchLineTests.cs"))
        self.assertEqual(("none", {"terrain-overlay": "source"}), run("mesh_ops/TerrainSlopeOverlay.cs"))

    def test_hausdorff_shard(self):
        # the certified upper bound of MeshHausdorffDistance (#742) and the cascade it runs in
        for f in ("mesh_ops/MeshHausdorffDistance.cs", "queries/TriangleDeviationBounds.cs", "queries/CertifiedTriangleBounds.cs",
                  "queries/CertifiedPointMeshDistance.cs"):
            self.assertEqual(("none", {"hausdorff": "source"}), run(f))
        self.assertEqual(("none", {"hausdorff": "tests"}), run(TESTS + "HausdorffCertifiedUpperTests.cs"))
        self.assertEqual(("none", {"hausdorff": "tests"}), run(TESTS + "MeshDeviationTests.cs"))

    def test_exact_winding_shard(self):
        # the exact winding number cache (#1294): the error bounds of finish_edge_angle and the exact leaf paths
        self.assertEqual(("none", {"exact-winding": "source"}), run("spatial/ExactWindingCache.cs"))
        for f in ("ExactWindingNumberTests.cs", "ExactWindingCacheInternalTests.cs", "ExactWindingOnSurfaceTests.cs", "FastWindingExpansionUsedTests.cs",
                  "WindingNumberTests.cs", "WindingEdgeCaseTests.cs"):
            self.assertEqual(("none", {"exact-winding": "tests"}), run(TESTS + f), f)

    def test_source_wins_over_tests(self):
        self.assertEqual("source", run("mesh_ops/MeshRoundingRepair.cs", TESTS + "ExactBooleanIssueCasesTests.cs")[1]["rounding-1"])

    def test_config_wins_over_source(self):
        self.assertEqual(("none", dict({r: "source" for r in RO}, **{"rounding-1": "full"})),
                         run("mesh_ops/MeshRoundingRepair.cs", "tests/mutation/stryker-mesh-rounding-1.json"))
        self.assertEqual("full", run("math/ExactPoint3.cs", ".github/workflows/mutation.yml")[0])

    def test_config_change_runs_that_config_in_full(self):
        self.assertEqual(("none", {"planar": "full"}), run("tests/mutation/stryker-mesh-planar.json"))
        self.assertEqual(("full", {}), run("tests/mutation/stryker-config.json"))

    def test_a_config_edit_that_only_moves_spans(self):
        def cfg(mutate, **other):
            return json.dumps({"stryker-config": dict({"mutate": mutate, "test-case-filter": "FullyQualifiedName~T",
                                                       "thresholds": {"break": 70}}, **other)})
        old = cfg(["**/mesh_ops/A.cs{0..100}", "**/mesh_ops/B.cs"])
        self.assertTrue(config_edit_moves_only_spans(old, cfg(["**/mesh_ops/A.cs{0..130}", "**/mesh_ops/B.cs"])))
        self.assertTrue(config_edit_moves_only_spans(old, cfg(["**/mesh_ops/A.cs{20..90}", "**/mesh_ops/B.cs{0..5}"])))
        for new in (cfg(["**/mesh_ops/A.cs{0..100}"]), cfg(["**/mesh_ops/B.cs", "**/mesh_ops/A.cs{0..100}"]),
                    cfg(["**/mesh_ops/A.cs{0..100}", "**/mesh_ops/C.cs"]),
                    cfg(["**/mesh_ops/A.cs{0..100}", "**/mesh_ops/B.cs"], thresholds={"break": 71}),
                    cfg(["**/mesh_ops/A.cs{0..100}", "**/mesh_ops/B.cs"], **{"test-case-filter": "FullyQualifiedName~U"}),
                    cfg(["mesh_ops/A.cs{0..100}", "**/mesh_ops/B.cs"]),
                    "{", "[]", json.dumps({"other": {}})):
            self.assertFalse(config_edit_moves_only_spans(old, new), new)

    def test_a_config_whose_spans_moved_runs_incrementally(self):
        def moved(*changed, names=("mesh-planar",)):
            arithmetic, mesh = select(changed, CONFIGS, moved=set(names))
            return arithmetic, {m["shard"]: m["mode"] for m in mesh}
        self.assertEqual(("none", {"planar": "source"}), moved())
        self.assertEqual(("source", {}), moved(names=("config",)))
        self.assertEqual(("none", {"planar": "full"}), moved("tests/mutation/stryker-mesh-planar.json"))
        self.assertEqual("full", moved(".config/dotnet-tools.json")[1]["planar"])
        self.assertEqual(("full", {s: "full" for s in ALL_MESH}),
                         (lambda a, m: (a, {x["shard"]: x["mode"] for x in m}))(*select([], CONFIGS, run_all=True,
                                                                                        moved={"mesh-planar"})))

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
          "  changes:\n"
          "    runs-on: ubuntu-latest\n"
          "    steps:\n"
          "      - run: echo select\n"
          "  exact-mesh:\n"
          "    needs: changes\n"
          "    runs-on: ubuntu-latest\n"
          "    timeout-minutes: 350\n"
          "    strategy:\n"
          "      fail-fast: false\n"
          "      # at most 4 at a time\n"
          "      max-parallel: 4\n"
          "      matrix:\n"
          "        include: ${{ fromJSON(needs.changes.outputs.mesh) }}\n"
          "    steps:\n"
          "      - uses: actions/checkout@v4\n"
          "      - name: Restore the baseline\n"
          "        if: matrix.mode == 'source'\n"
          "        uses: actions/cache/restore@v4\n"
          "        with:\n"
          "          path: baseline\n"
          "          key: k-${{ matrix.shard }}-${{ hashFiles('x') }}-${{ github.run_id }}\n"
          "          restore-keys: k-${{ matrix.shard }}-${{ hashFiles('x') }}-\n"
          "      - name: Stryker\n"
          "        # a heap limit\n"
          "        env:\n"
          "          MODE: ${{ matrix.mode }}\n"
          "        run: bash run_stryker.sh\n"
          "      - name: Summary\n"
          "        run: >\n"
          "          cat report.md || true\n"
          "      - uses: actions/upload-artifact@v4\n"
          "        with:\n"
          "          name: r\n")

    @staticmethod
    def _diff(old, new):
        import difflib
        return "".join(difflib.unified_diff(old.splitlines(True), new.splitlines(True), "a/w", "b/w", n=1))

    def _forces(self, new, old=None):
        old = self.WF if old is None else old
        return workflow_edit_forces_full(self._diff(old, new), old, new)

    def _edit(self, old, new, text=None):
        text = self.WF if text is None else text
        self.assertEqual(1, text.count(old), old)
        return text.replace(old, new)

    def test_workflow_edits_outside_the_stryker_setup_force_nothing(self):
        e = self._edit
        for new in (
                # the trigger: paths, a comment in them, the header, the cron, a new key under on:
                e("      - 'b.cs'\n", "      - 'b.cs'\n      - 'c*.cs'   # new\n"),
                e("      - 'a.cs'\n", ""),
                e("      - 'a.cs'\n", "      # a comment\n\n      - 'a.cs'\n"),
                e("# header comment\n", "# header comment\n# more header\nname: x\n"),
                e("23 3 * * 1", "23 3 * * 2"),
                e("  schedule:\n", "  workflow_dispatch:\n    inputs:\n      shards:\n        default: all\n  schedule:\n"),
                # the selection job
                e("echo select", "echo selected && true"),
                # scheduling and limits of the Stryker job
                e("max-parallel: 4", "max-parallel: 12"),
                e("      # at most 4 at a time\n", "      # at most 12 at a time, because\n      # of the account's job limit\n"),
                e("fail-fast: false", "fail-fast: true"),
                e("timeout-minutes: 350", "timeout-minutes: 355"),
                # the reporting steps
                e("cat report.md || true", "cat report.md >> \"$GITHUB_STEP_SUMMARY\" || true"),
                e("          name: r\n", "          name: r\n          retention-days: 3\n"),
                # the cache keys (a changed key can only lose baselines)
                e("hashFiles('x') }}-${{ github.run_id }}", "hashFiles('x', 'y') }}-${{ github.run_id }}")):
            self.assertFalse(self._forces(new), new)

    def test_workflow_edits_of_the_stryker_setup_force_a_full_run(self):
        e = self._edit
        for new in (
                e("run: bash run_stryker.sh", "run: bash run_stryker.sh --x"),
                e("          MODE: ${{ matrix.mode }}\n", "          MODE: ${{ matrix.mode }}\n          DOTNET_GCHeapHardLimit: '1'\n"),
                e("        # a heap limit\n", "        # a heap limit of 6 GB\n"),            # a comment inside the Stryker step
                e("runs-on: ubuntu-latest\n    timeout", "runs-on: ubuntu-24.04\n    timeout"),
                e("      - uses: actions/checkout@v4\n", "      - uses: actions/checkout@v5\n"),
                e("      matrix:\n        include:", "      matrix:\n        exclude: []\n        include:"),
                e("    needs: changes\n", "    needs: changes\n    env:\n      X: 1\n"),
                e("          path: baseline\n", "          path: other\n"),                      # what is restored
                e("        if: matrix.mode == 'source'\n", "        if: matrix.mode == 'tests'\n"),
                e("      - name: Stryker\n", "      - name: Setup\n        run: echo x\n      - name: Stryker\n"),   # a new step
                e("on:\n", "onn:\n"),
                e("# header comment\n", "permissions:\n  contents: write\n# header comment\n"),
                e("  changes:\n", "  extra:\n    runs-on: ubuntu-latest\n  changes:\n"),                           # an unknown job
                e("    steps:\n      - uses: actions/checkout@v4\n", "    steps:\n      - run: echo first\n      - uses: actions/checkout@v4\n"),
                # a `#` line between two steps next to a Stryker one, and a quoted item outside the paths
                e("      - name: Summary\n", "      # note\n      - name: Summary\n"),
                e("    runs-on: ubuntu-latest\n    timeout", "    runs-on:\n      - 'self-hosted'\n    timeout")):
            self.assertTrue(self._forces(new), new)
        self.assertTrue(workflow_edit_forces_full("", self.WF, self.WF))      # no diff: not known, so full

    def test_deleting_or_renaming_forces_a_full_run_in_the_stryker_setup(self):
        e = self._edit
        # a pure deletion has no added line: the removed one decides (its region in the old text)
        self.assertTrue(self._forces(e("        env:\n          MODE: ${{ matrix.mode }}\n", "")))
        self.assertTrue(self._forces(e("      - uses: actions/checkout@v4\n", "")))
        self.assertTrue(self._forces(e("      matrix:\n        include: ${{ fromJSON(needs.changes.outputs.mesh) }}\n", "")))
        # a rename of a job header: the known jobs become unknown ones
        self.assertTrue(self._forces(e("  exact-mesh:\n", "  exact-mesh-2:\n")))
        self.assertTrue(self._forces(e("  changes:\n", "  selection:\n")))
        # ... and a pure addition of a forcing line decides by the new text
        self.assertTrue(self._forces(e("        run: bash run_stryker.sh\n", "        run: bash run_stryker.sh\n        shell: bash\n")))

    def test_removing_a_reporting_step_forces_nothing_but_a_comment_in_a_shell_block_does(self):
        removed = self._edit("      - name: Summary\n        run: >\n          cat report.md || true\n", "")
        self.assertFalse(self._forces(removed))
        shell = self._edit("          cat report.md || true\n", "          # note\n          cat report.md || true\n")
        self.assertFalse(self._forces(shell))                     # (inside the reporting step)
        stryker = self._edit("        run: bash run_stryker.sh\n", "        run: |\n          # note\n          bash run_stryker.sh\n")
        self.assertTrue(self._forces(stryker))

    def test_real_workflow_regions(self):
        # the same classification on the lines of the real workflow
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            wf = f.read()
        def edit(old, new, count=1):
            self.assertEqual(count, wf.count(old), old)
            return wf.replace(old, new)
        self.assertFalse(self._forces(edit("max-parallel: ", "max-parallel: 1"), wf))
        self.assertFalse(self._forces(edit("timeout-minutes: 350", "timeout-minutes: 340"), wf))
        self.assertFalse(self._forces(edit("python3 test_baseline.py", "python3 test_baseline.py\n          true"), wf))
        self.assertFalse(self._forces(edit("jq -r '.files", "jq -r '.files | .", ), wf))
        self.assertFalse(self._forces(edit("      - 'tests/mutation/*'\n", "      - 'tests/mutation/*'\n      - 'x.cs'\n"), wf))
        self.assertTrue(self._forces(edit("DOTNET_GCHeapHardLimit: '0x180000000'", "DOTNET_GCHeapHardLimit: '0x100000000'"), wf))
        self.assertTrue(self._forces(edit("run: bash run_stryker.sh stryker-mesh-${{ matrix.shard }}.json", "run: bash run_stryker.sh stryker-mesh-${{ matrix.shard }}.json --x"), wf))
        self.assertTrue(self._forces(edit("dotnet-version: 10.0.x", "dotnet-version: 11.0.x", 2), wf))
        self.assertTrue(self._forces(edit("run: dotnet tool restore", "run: dotnet tool restore --x", 2), wf))
        self.assertTrue(self._forces(edit("DOTNET_NOLOGO: true", "DOTNET_NOLOGO: false"), wf))
        self.assertTrue(self._forces(edit("include: ${{ fromJSON(needs.changes.outputs.mesh) }}", "include: []"), wf))
        self.assertTrue(self._forces(edit("python3 tests/mutation/baseline.py store tests/mutation/StrykerOutput tests/mutation/baseline", "true", 2), wf))

    def test_reporting_steps_are_harmless_only_when_they_only_report(self):
        e = self._edit
        # a new step with a harmless name BEFORE the Stryker step, or a second one, is not a reporting step
        before = e("      - name: Stryker\n", "      - name: Summary\n        run: echo x\n      - name: Stryker\n")
        self.assertTrue(self._forces(before))
        twice = e("      - name: Summary\n", "      - name: Summary\n        run: cat a\n      - name: Summary\n")
        self.assertTrue(self._forces(twice))
        # after the Stryker step, with a command that writes or runs something, it is not read-only
        for cmd in ("echo x >> $GITHUB_ENV", "sed -i s/a/b/ tests/mutation/StrykerOutput/x.json", "cat a > b",
                    "cat a; rm b", "cat a && rm b", "cat a | tee b", "cat a || rm b", "bash run_stryker.sh", "cat $(rm b)",
                    "cat a &"):
            self.assertTrue(self._forces(e("          cat report.md || true\n", "          %s\n" % cmd)), cmd)
        for cmd in ("cat a.md >> \"$GITHUB_STEP_SUMMARY\" || true", "jq -r '.a | .b' x.json || true",
                    "jq -r \"$X\" x.json"):
            self.assertFalse(self._forces(e("          cat report.md || true\n", "          %s\n" % cmd)), cmd)
        # a plain key only: `shell`, `env` or `working-directory` of a reporting step change what it runs
        self.assertTrue(self._forces(e("      - name: Summary\n", "      - name: Summary\n        shell: python\n")))
        self.assertTrue(self._forces(e("      - name: Summary\n", "      - name: Summary\n        env:\n          X: 1\n")))
        self.assertFalse(self._forces(e("      - name: Summary\n", "      - name: Summary\n        if: always()\n")))
        # a reporting step moved before the Stryker step (a removal and an addition)
        moved = self.WF.replace("      - name: Summary\n        run: >\n          cat report.md || true\n", "")
        moved = moved.replace("      - name: Stryker\n", "      - name: Summary\n        run: >\n          cat report.md || true\n      - name: Stryker\n")
        self.assertTrue(self._forces(moved))

    def test_a_named_upload_step_is_reporting_only_after_the_stryker_step(self):
        e = self._edit
        step = ("      - name: Upload the logs\n        if: always()\n        uses: actions/upload-artifact@v4\n"
                "        with:\n          name: logs\n          path: x/\n")
        after = e("      - uses: actions/upload-artifact@v4\n", step + "      - uses: actions/upload-artifact@v4\n")
        self.assertFalse(self._forces(after))
        self.assertTrue(self._forces(e("      - name: Stryker\n", step + "      - name: Stryker\n")))      # before Stryker
        # another action, or a plain key of a step that runs something, is not reporting
        other = step.replace("actions/upload-artifact@v4", "actions/cache/save@v4")
        self.assertTrue(self._forces(e("      - uses: actions/upload-artifact@v4\n", other + "      - uses: actions/upload-artifact@v4\n")))
        self.assertTrue(self._forces(e("      - uses: actions/upload-artifact@v4\n", step.replace("with:", "shell: bash\n        with:") + "      - uses: actions/upload-artifact@v4\n")))
        # a line that only looks like an upload inside another step's `with:` text is not an upload step
        sneaky = ("      - name: Tweak\n        uses: actions/github-script@v7\n        with:\n          script: |\n"
                  "            rm -rf StrykerOutput\n            uses: actions/upload-artifact@v4\n")
        self.assertTrue(self._forces(e("      - uses: actions/upload-artifact@v4\n", sneaky + "      - uses: actions/upload-artifact@v4\n")))
        # the same name twice
        self.assertTrue(self._forces(e("      - uses: actions/upload-artifact@v4\n", step + step + "      - uses: actions/upload-artifact@v4\n")))
        # the real workflow's logs steps are harmless in both jobs (they are in it already: removing one is harmless, too)
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            wf = f.read()
        self.assertEqual(2, wf.count("name: Upload the Stryker logs"))
        removed = wf.replace(wf[wf.index("      - name: Upload the Stryker logs"):wf.index("          if-no-files-found: warn\n", wf.index("      - name: Upload the Stryker logs")) + len("          if-no-files-found: warn\n")], "", 1)
        self.assertFalse(self._forces(removed, wf))

    def test_the_early_stop_limit_matches_each_jobs_timeout(self):
        # run_stryker.sh assumes 350 min unless G3_LIMIT_SECONDS says otherwise: a job with a shorter timeout must say so
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            wf = f.read()
        jobs = re.split(r"\n  (?=[a-z-]+:\n)", wf)
        checked = 0
        for job in jobs:
            m = re.search(r"timeout-minutes:\s*(\d+)", job)
            if not m or "run_stryker.sh" not in job:
                continue
            checked += 1
            limit = re.search(r"G3_LIMIT_SECONDS:\s*'?(\d+)", job)
            minutes = int(m.group(1))
            if minutes != 350:
                self.assertIsNotNone(limit, "a job with timeout-minutes %d needs G3_LIMIT_SECONDS" % minutes)
                self.assertEqual(minutes * 60, int(limit.group(1)))
        self.assertEqual(2, checked)

    def test_yaml_anchors_aliases_and_merge_keys_force_a_full_run(self):
        e = self._edit
        anchored = e("    runs-on: ubuntu-latest\n    timeout", "    runs-on: &runner ubuntu-latest\n    timeout")
        self.assertTrue(self._forces(anchored))
        # an edit anywhere (also a harmless region) of a workflow that uses an alias
        self.assertTrue(self._forces(e("max-parallel: 4", "max-parallel: 5", anchored), anchored))
        self.assertTrue(self._forces(e("          name: r\n", "          name: *runner\n")))
        self.assertTrue(self._forces(e("  changes:\n", "  changes:\n    <<: *defaults\n")))
        # a merge key without an alias, and removing the anchor (only the old text has it)
        self.assertTrue(self._forces(e("  changes:\n", "  changes:\n    <<: {a: b}\n")))
        harmless_anchor = e("max-parallel: 4", "max-parallel: &p 4")
        self.assertTrue(self._forces(self.WF, harmless_anchor))     # (the anchor is only in the old text, in a harmless region)
        self.assertTrue(self._forces(harmless_anchor, self.WF))
        # shell text is not an anchor
        self.assertFalse(self._forces(e("max-parallel: 4", "max-parallel: 5")))
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            self.assertFalse(any(__import__("select_shards")._ANCHOR.search(l) for l in f.read().splitlines()
                                 if not l.lstrip().startswith("#")), "the real workflow must not use anchors")

    def test_non_step_lines_unknown_jobs_and_odd_structure_force_a_full_run(self):
        e = self._edit
        # a value under a job key that is not a step (a job env), the body of an unknown job
        withenv = e("    needs: changes\n", "    needs: changes\n    env:\n      X: 1\n")
        self.assertTrue(self._forces(withenv))
        self.assertTrue(self._forces(e("      X: 1\n", "      X: 2\n", withenv), withenv))
        extra = e("  changes:\n", "  extra:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo a\n  changes:\n")
        self.assertTrue(self._forces(e("echo a", "echo b", extra), extra))
        # a line under `steps:` that is not a step
        odd = e("    steps:\n      - uses: actions/checkout@v4\n", "    steps:\n        run: x\n      - uses: actions/checkout@v4\n")
        self.assertTrue(self._forces(odd))
        # a removed line is judged by its place in the OLD text: the new text has a harmless line there
        gone = e("        run: bash run_stryker.sh\n", "")
        self.assertTrue(self._forces(gone))
        # an arithmetic job: its own harmless and forcing lines
        wf = self.WF + ("  exact-arithmetic:\n    needs: changes\n    runs-on: ubuntu-latest\n    timeout-minutes: 150\n    steps:\n"
                        "      - name: Stryker\n        run: bash run_stryker.sh a.json\n"
                        "      - name: Summary\n        run: cat report.md || true\n")
        self.assertFalse(self._forces(e("timeout-minutes: 150", "timeout-minutes: 160", wf), wf))
        self.assertFalse(self._forces(e("run: cat report.md || true\n", "run: cat report.md >> \"$GITHUB_STEP_SUMMARY\" || true\n", wf), wf))
        self.assertTrue(self._forces(e("run_stryker.sh a.json", "run_stryker.sh b.json", wf), wf))
        self.assertTrue(self._forces(e("    needs: changes\n    runs-on: ubuntu-latest\n    timeout-minutes: 150", "    needs: changes\n    runs-on: ubuntu-24.04\n    timeout-minutes: 150", wf), wf))

    def test_a_diff_that_does_not_fit_the_texts_forces_a_full_run(self):
        # line numbers beyond the texts: not classified, so full
        diff = "--- a/w\n+++ b/w\n@@ -900,1 +900,1 @@\n-      - 'a.cs'\n+      - 'b.cs'\n"
        self.assertTrue(workflow_edit_forces_full(diff, self.WF, self.WF))
        diff = "--- a/w\n+++ b/w\n@@ -6,1 +900,1 @@\n-      - 'a.cs'\n+      - 'b.cs'\n"
        self.assertTrue(workflow_edit_forces_full(diff, self.WF, self.WF))
        # (the same diff at its real place is a harmless trigger edit)
        diff = "--- a/w\n+++ b/w\n@@ -6,1 +6,1 @@\n-      - 'a.cs'\n+      - 'b.cs'\n"
        self.assertFalse(workflow_edit_forces_full(diff, self.WF, self.WF))

    def test_the_workflow_diff_needs_both_commits(self):
        # BASE without HEAD (or the reverse): the workflow edit cannot be classified, so every shard runs in full
        for env in ({"BASE": "a" * 40}, {"HEAD": "a" * 40}):
            p = subprocess.run([sys.executable, os.path.join(HERE, "select_shards.py")], input=".github/workflows/mutation.yml\n",
                               capture_output=True, text=True, env=dict({k: v for k, v in os.environ.items() if k not in ("BASE", "HEAD")}, **env))
            self.assertEqual(0, p.returncode, p.stderr)
            self.assertIn("arithmetic=full", p.stdout)
            self.assertEqual(len(ALL_MESH), p.stdout.count('"mode": "full"'))

    def test_the_classes_of_exact_and_row_terms(self):
        # (coverage_select.py writes exact method names and theory rows: their classes are the test files of the shard)
        self.assertEqual("ExactMeshBooleanTestsCrossCarry", test_class(
            "FullyQualifiedName=geometry3Sharp.Tests.ExactMeshBooleanTestsCrossCarry.CrossCarry_SameAsOwnRays_UnderSimilarity"))
        self.assertEqual("MeshRoundingRepairTests", test_class("FullyQualifiedName~MeshRoundingRepairTests.ExactVertices"))
        self.assertEqual("ExactBooleanFilterTests", test_class("FullyQualifiedName~ExactBooleanFilterTests"))
        cfg = CONFIGS["mesh-boolean-6"]
        self.assertIn("tests/geometry3Sharp.Tests/ExactMeshBooleanTestsLocalFilter.cs", cfg[2])
        self.assertTrue(all(not t.endswith("DisplayName~utm.cs") for t in cfg[2]))
        self.assertEqual("tests", mode_of(cfg, {"tests/geometry3Sharp.Tests/ExactMeshBooleanTestsLocalFilter.cs"}))

    def test_the_scripts_of_the_runs_run_a_representative_shard_in_full(self):
        # a change to run_stryker.sh, incremental.py or baseline.py can break every shard's real run (the fake-dotnet tests
        # cannot see Stryker itself): the cheapest mesh shard runs in full, the others by their own rules
        for path in ("tests/mutation/run_stryker.sh", "tests/mutation/incremental.py", "tests/mutation/baseline.py", "tests/mutation/exclude_other_sources.py"):
            self.assertEqual(("none", {"fixed": "full"}), run(path), path)
        self.assertEqual(("none", dict({r: "source" for r in RO}, fixed="full")),
                         run("tests/mutation/run_stryker.sh", "mesh_ops/MeshRoundingRepair.cs"))
        # the test files and select_shards.py run in the `changes` job of every run (the workflow paths cover them)
        self.assertEqual(("none", {}), run("tests/mutation/test_incremental.py", "tests/mutation/select_shards.py", "tests/mutation/BASELINE_FORMAT"))
        # (not when everything runs in full anyway, and --only keeps its list)
        arithmetic, mesh = select(["tests/mutation/run_stryker.sh"], CONFIGS, run_all=True, only={"planar"})
        self.assertEqual([{"shard": "planar", "mode": "full"}], mesh)

    def test_the_full_run_leaves_runner_slots_for_the_pull_requests(self):
        # owner 2026-11-13: a full run (mesh shards + the arithmetic job + the selection job) must leave at least 10 of the account's
        # 20 concurrent jobs free; the cap is max-parallel, not a concurrency group (a group cancels all but one pending job)
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            wf = f.read()
        jobs = wf[wf.index("\njobs:"):]
        mesh = jobs[jobs.index("\n  exact-mesh:"):]
        cap = int(re.search(r"\n      max-parallel: (\d+)", mesh).group(1))
        self.assertLessEqual(cap + 2, 10)
        self.assertNotIn("concurrency:", mesh)
        # and changing the cap is not a reason for a full run of every shard
        old = wf.replace("max-parallel: %d" % cap, "max-parallel: 12")
        diff = "".join(difflib.unified_diff(old.splitlines(True), wf.splitlines(True), "a", "b"))
        self.assertFalse(workflow_edit_forces_full(diff, old, wf))

    def test_the_workflow_checks_local_results(self):
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            wf = f.read()
        self.assertIn("      - 'tests/mutation/local-results/**'", wf)        # (a result-only commit starts the check)
        self.assertIn("python3 test_local_result.py", wf)
        self.assertIn("python3 test_run_local.py", wf)
        self.assertIn("python3 test_resplit.py", wf)
        step = wf[wf.index("Check the local results of this PR"):wf.index("# Runs once a day although")]
        self.assertIn("github.event_name == 'pull_request'", step)
        self.assertIn("local_result.py check", step)
        self.assertIn('--head "$HEAD"', step)
        # a result file alone selects no shard (the check is all it starts)
        self.assertEqual(("none", []), select(["tests/mutation/local-results/" + "a" * 40 + "/boolean-1.json"], CONFIGS))

    def test_the_30_process_in_the_workflow(self):
        # 3.0 process (README): scheduled full runs on master in two groups, no full run on a PR, runs on master never cancel
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            wf = f.read()
        self.assertEqual(2, wf.count("cron:"))
        self.assertIn("cron: '17 19 * * 1-6'", wf)                  # nightly group, Monday to Saturday
        self.assertIn("cron: '17 19 * * 0'", wf)                    # weekly group, Sunday
        self.assertIn('if [ "$SCHEDULE" = "17 19 * * 0" ]; then SHARDS=weekly; else SHARDS=nightly; fi', wf)
        # a group per kind of run on master (a pending dispatch must not be replaced by a scheduled run), cancel only for PRs
        self.assertIn("group: mutation-${{ github.event_name == 'pull_request' && github.ref || github.event_name }}", wf)
        self.assertIn("cancel-in-progress: ${{ github.event_name == 'pull_request' }}", wf)
        # both Stryker steps tell run_stryker.sh that this is a PR (no full run there)
        self.assertEqual(2, wf.count("NO_FULL: ${{ github.event_name == 'pull_request' && '1' || '' }}"))
        # a PR stores no baseline (it ran no full shard), and a skipped shard has no report to find
        self.assertEqual(2, wf.count("always() && github.event_name != 'pull_request' && ("))
        self.assertEqual(2, wf.count("|| github.event_name == 'pull_request') && 'warn' || 'error'"))
        self.assertIn("python3 test_run_stryker.py", wf)
        # the weekly run is unfiltered by its cron line, not by the weekday at the start of a late shard
        self.assertEqual(2, wf.count("G3_FULL_TESTS: ${{ github.event.schedule == '17 19 * * 0' && '1' || '' }}"))

    def test_the_concurrency_groups_of_the_events(self):
        # the expression of the group, evaluated here for the events: PRs by ref, every other event by its name
        def group(event, ref):
            return "mutation-" + (ref if event == "pull_request" else event)
        self.assertNotEqual(group("schedule", "refs/heads/master"), group("workflow_dispatch", "refs/heads/master"))
        self.assertNotEqual(group("pull_request", "refs/pull/1/merge"), group("pull_request", "refs/pull/2/merge"))

    def test_only_groups_and_prefixes(self):
        names = {shard_name(n) for n in CONFIGS}
        nightly = expand_only("nightly", names)
        weekly = expand_only("weekly", names)
        self.assertEqual(set(), nightly & weekly)
        self.assertEqual(names, nightly | weekly)                  # (every shard runs at least once a week)
        self.assertEqual(set(BO), {n for n in nightly if n.startswith("boolean-")})
        self.assertEqual(4, len({n for n in nightly if n.startswith("arrangement-")}))
        self.assertEqual(15, len({n for n in nightly if n.startswith("rounding-")}))
        self.assertEqual(28, len(nightly))
        self.assertIn("arithmetic", weekly)
        self.assertTrue({"geodesic-1", "geodesic-2", "hausdorff", "planar", "fixed", "generated"} <= weekly)
        self.assertEqual(set(BO), expand_only("boolean-*", names))
        self.assertEqual({"planar", "boolean-2"}, expand_only("planar, boolean-2", names))
        with self.assertRaises(ValueError):
            expand_only("nonexistent-*", names)
        # the selection of a nightly run: only these shards, all in full
        arithmetic, mesh = select([], CONFIGS, run_all=True, only=nightly)
        self.assertEqual("none", arithmetic)
        self.assertEqual(nightly, {m["shard"] for m in mesh})
        self.assertTrue(all(m["mode"] == "full" for m in mesh))

    def test_narrowed_test_sets_belong_to_shards_of_the_sunday_run(self):
        # README "Narrowed test sets": the unfiltered check of record is the scheduled run of a Sunday, which is the weekly group
        # (the nightly group runs Monday to Saturday): a filter file of a nightly shard would never be checked unfiltered
        names = {shard_name(n) for n in CONFIGS}
        weekly = expand_only("weekly", names)
        for f in os.listdir(os.path.join(HERE, "test-filters")):
            self.assertIn(shard_name(os.path.splitext(f)[0][len("stryker-"):]), weekly, f)

    def test_the_workflow_keeps_what_the_harmless_regions_rely_on(self):
        # the trigger, the selection job and the `if` of the Stryker jobs count as harmless in select_shards.py, so what
        # they must keep doing is checked here
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            wf = f.read()
        on = wf[wf.index("\non:"):wf.index("\nconcurrency:")]
        self.assertIn("pull_request:", on)
        self.assertRegex(on, r"types: \[[^\]]*\bsynchronize\b")
        self.assertRegex(on, r"types: \[[^\]]*\bopened\b")
        self.assertRegex(on, r"types: \[[^\]]*\bready_for_review\b")
        # closing or merging a PR must cancel its run (the concurrency group) and start nothing new (the changes job)
        self.assertRegex(on, r"types: \[[^\]]*\bclosed\b")
        self.assertRegex(on, r"types: \[[^\]]*\bconverted_to_draft\b")
        self.assertNotIn("branches:", on)                       # no branch filter: every PR is checked
        self.assertNotIn("paths-ignore", on)
        self.assertIn("schedule:", on)
        self.assertIn("workflow_dispatch:", on)
        # (a scheduled or dispatched run is never cancelled by another one: the late cron would cancel the dispatched full run)
        self.assertIn("cancel-in-progress: ${{ github.event_name == 'pull_request' }}", wf)
        jobs = wf[wf.index("\njobs:"):]
        changes = jobs[jobs.index("\n  changes:"):jobs.index("\n  exact-arithmetic:")]
        self.assertIn("set -o pipefail", changes)
        self.assertRegex(changes, r"\n    if: github\.event\.action != 'closed'\n")
        self.assertIn("tests/mutation/select_shards.py", changes)
        for name, nxt in (("exact-arithmetic", "exact-mesh"), ("exact-mesh", None)):
            block = jobs[jobs.index("\n  %s:" % name):(jobs.index("\n  %s:" % nxt) if nxt else len(jobs))]
            cond = re.search(r"\n    if: (.*)\n", block).group(1)
            self.assertIn("needs.changes.outputs.", cond, name)
            self.assertIn("github.event.pull_request.draft != true", cond, name)
            self.assertNotRegex(cond, r"^\s*(false|0)\s*$", name)
            self.assertIn("needs: changes", block, name)

    def test_cache_keys_name_the_shard_and_the_format(self):
        # a key line is not a reason for a full run (see the table in select_shards.py), so its content is checked here:
        # every key and restore-key names its shard, the tools and BASELINE_FORMAT, never the scripts, and a restore-key
        # ends at the hash. The key does not hold the config: a baseline stores its config (baseline.py store) and
        # incremental.plan reuses it only for the same config up to the spans of `mutate`, so a moved span keeps it
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            lines = f.read().splitlines()
        keys = [l.strip() for l in lines if re.match(r"^\s*(key|restore-keys):", l) and "mutation-baseline-" in l]
        self.assertEqual(6, len(keys), keys)
        for l in keys:
            self.assertRegex(l, r"^(key|restore-keys): mutation-baseline-(arithmetic|mesh-\$\{\{ matrix\.shard \}\})-"
                                r"\$\{\{ hashFiles\('\.config/dotnet-tools\.json', 'tests/mutation/BASELINE_FORMAT'\) \}\}-", l)
            self.assertNotIn("baseline.py", l)
            self.assertNotIn("incremental.py", l)
            if l.startswith("restore-keys:"):
                self.assertTrue(l.endswith(") }}-"), l)
            else:
                self.assertTrue(l.endswith("-${{ github.run_id }}-${{ github.run_attempt }}"), l)

    def test_every_stored_baseline_holds_its_config(self):
        # (without its config a baseline is never reused: incremental.plan; the config must be the shard's own)
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            text = f.read()
        stores = [l.strip() for l in text.splitlines() if "baseline.py store" in l]
        self.assertEqual(["python3 tests/mutation/baseline.py store tests/mutation/StrykerOutput tests/mutation/baseline "
                          "tests/mutation/stryker-config.json",
                          'python3 tests/mutation/baseline.py store tests/mutation/StrykerOutput tests/mutation/baseline '
                          '"tests/mutation/stryker-mesh-$SHARD.json"'], stores)
        self.assertIn("          SHARD: ${{ matrix.shard }}\n", text)

    def test_baseline_format_is_a_positive_integer(self):
        with open(os.path.join(HERE, "BASELINE_FORMAT"), encoding="utf-8") as f:
            text = f.read()
        self.assertRegex(text, r"^[1-9][0-9]*\n$")

    def test_workflow_paths_cover_the_tooling(self):
        # removing the tooling from the trigger paths is a harmless edit for the selection, so it must be caught here (review of #278)
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            workflow = f.read()
        block = workflow[workflow.index("paths:"):workflow.index("schedule:")]
        patterns = re.findall(r"- '([^']+)'", block)
        for path in ALWAYS | {"tests/mutation/select_shards.py"}:
            self.assertTrue(any(fnmatch.fnmatch(path, p) for p in patterns), path)

    def test_command_line_and_the_workflow_diff(self):
        # on a temporary repository: an edit of a harmless workflow region runs nothing by itself, a Stryker-step edit everything,
        # a harmless edit with a source change only that source's shard, and a missing commit fails the step
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
            job_head = commit(self.WF.replace("run: bash run_stryker.sh", "run: bash run_stryker.sh --x"))
            def cli(head, changed=".github/workflows/mutation.yml\n"):
                return subprocess.run([sys.executable, os.path.join(HERE, "select_shards.py")], input=changed,
                                      capture_output=True, text=True, cwd=tmp, env=dict(os.environ, BASE=base, HEAD=head))
            self.assertEqual((0, "arithmetic=none\nmesh=[]\n"), (cli(paths_head).returncode, cli(paths_head).stdout))
            self.assertIn("arithmetic=full", cli(job_head).stdout)
            out = cli(paths_head, ".github/workflows/mutation.yml\nmesh_ops/MeshRoundingRepair.cs\n").stdout
            self.assertEqual("arithmetic=none\nmesh=" + json.dumps([{"shard": r, "mode": "source"} for r in RO]) + "\n", out)
            self.assertNotEqual(0, cli("0" * 40).returncode)
            # a config whose edit only moves a span runs incrementally; another edit of it, and a new config, in full
            cfg_dir = os.path.join(tmp, "tests", "mutation")
            os.makedirs(cfg_dir)
            planar = os.path.join(cfg_dir, "stryker-mesh-planar.json")
            with open(os.path.join(HERE, "stryker-mesh-planar.json"), encoding="utf-8") as f:
                real = json.load(f)
            def write_cfg(doc):
                with open(planar, "w") as f:
                    json.dump(doc, f)
                git("add", "-A"); git("commit", "-q", "-m", "c")
                return git("rev-parse", "HEAD")
            new_cfg_head = write_cfg(real)
            path = "tests/mutation/stryker-mesh-planar.json\n"
            self.assertIn('"mode": "full"', cli(new_cfg_head, path).stdout)
            base = new_cfg_head
            shifted = json.loads(json.dumps(real))
            entry = shifted["stryker-config"]["mutate"][0]
            m = re.match(r"^(.*?)(\{(\d+)\.\.(\d+)\})?$", entry)
            shifted["stryker-config"]["mutate"][0] = m.group(1) + "{0..%d}" % (int(m.group(4) or 1000) + 7)
            shift_head = write_cfg(shifted)
            self.assertEqual('arithmetic=none\nmesh=[{"shard": "planar", "mode": "source"}]\n', cli(shift_head, path).stdout)
            other = json.loads(json.dumps(shifted))
            other["stryker-config"]["thresholds"]["break"] = 1
            self.assertIn('"mode": "full"', cli(write_cfg(other), path).stdout)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    def test_all_flag_runs_everything_in_full(self):
        arithmetic, mesh = select([], CONFIGS, run_all=True)
        self.assertEqual("full", arithmetic)
        self.assertEqual({s: "full" for s in ALL_MESH}, {m["shard"]: m["mode"] for m in mesh})

    def test_only_runs_the_named_shards_in_full(self):
        arithmetic, mesh = select([], CONFIGS, run_all=True, only={"rounding-1", "arithmetic"})
        self.assertEqual(("full", [{"shard": "rounding-1", "mode": "full"}]), (arithmetic, mesh))
        arithmetic, mesh = select([], CONFIGS, run_all=True, only={"planar"})
        self.assertEqual(("none", [{"shard": "planar", "mode": "full"}]), (arithmetic, mesh))
        with self.assertRaises(ValueError):
            select([], CONFIGS, run_all=True, only={"rounding-1", "typo"})

    def test_command_line(self):
        def cli(*args):
            p = subprocess.run([sys.executable, os.path.join(HERE, "select_shards.py")] + list(args), input="",
                               capture_output=True, text=True)
            return p.returncode, p.stdout
        self.assertEqual((0, 'arithmetic=none\nmesh=[{"shard": "rounding-1", "mode": "full"}]\n'),
                         cli("--all", "--only", "rounding-1"))
        code, out = cli("--all", "--only", "all")
        self.assertEqual(0, code)
        self.assertIn("arithmetic=full", out)
        self.assertNotEqual(0, cli("--all", "--only", "nonsense")[0])


class PullRequestSelection(unittest.TestCase):
    """Owner 2026-11-23 (runner load): a PR runs only the incremental (`source`) runs of the shards that mutate a changed exact-code
    file, or, with the label `mutation`, of every shard with a changed file. Full runs, runs of changed tests and source runs of
    non-exact code are not started on a PR; they are listed as skipped, with the reason, and run on master."""

    def pr(self, *changed, labels=()):
        arithmetic, mesh = select(changed, CONFIGS)
        a2, mesh2, skipped = pr_selection(arithmetic, mesh, CONFIGS, changed, labels)
        return (arithmetic, mesh), (a2, mesh2, skipped)

    def kept(self, after):
        a2, mesh2, _ = after
        return ({"arithmetic": a2} if a2 != "none" else {}) | {m["shard"]: m["mode"] for m in mesh2}

    def exact_source(self, name):
        from select_shards import exact_gated
        return sorted(p for p in CONFIGS[name][1] if exact_gated(p))[0]

    def assert_partition(self, before, after):
        # the shards that run on the PR are exactly the selected ones minus the skipped ones, and every one of them is a source run
        (arithmetic, mesh), (a2, mesh2, skipped) = before, after
        selected = ({"arithmetic": arithmetic} if arithmetic != "none" else {}) | {m["shard"]: m["mode"] for m in mesh}
        left = self.kept(after)
        names = [e["shard"] for e in skipped]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual(set(selected), set(left) | set(names))
        self.assertFalse(set(left) & set(names))
        self.assertTrue(all(mode == "source" for mode in left.values()), left)
        self.assertTrue(all(e["why"] for e in skipped))

    def test_a_non_exact_source_change_runs_nothing_on_the_pr(self):
        # (the old selection ran the 13 rounding shards incrementally for every MeshRoundingRepair.cs push)
        before, after = self.pr("mesh_ops/MeshRoundingRepair.cs")
        self.assertEqual({r: "source" for r in RO}, {m["shard"]: m["mode"] for m in before[1]})
        self.assertEqual({}, self.kept(after))
        self.assertEqual(set(RO), {e["shard"] for e in after[2]})
        self.assertTrue(all("no exact-code file" in e["why"] for e in after[2]))
        self.assert_partition(before, after)

    def test_the_label_mutation_runs_non_exact_sources_incrementally(self):
        before, after = self.pr("mesh_ops/MeshRoundingRepair.cs", labels=["mutation"])
        self.assertEqual({r: "source" for r in RO}, self.kept(after))
        self.assertEqual([], after[2])
        # other labels do not
        self.assertEqual({}, self.kept(self.pr("mesh_ops/MeshRoundingRepair.cs", labels=["bugfix", "full-ci", "gate-change"])[1]))

    def test_an_exact_source_change_runs_its_shards_incrementally(self):
        before, after = self.pr("mesh_ops/ExactMeshBoolean.cs")
        self.assertEqual({b: "source" for b in BO}, self.kept(after))
        self.assertEqual([], after[2])
        before, after = self.pr("math/ExactPoint3.cs", "mesh_ops/PolyhedralEnvelope.cs")
        self.assertEqual("source", after[0])
        self.assertTrue({"envelope-1", "envelope-2", "envelope-3", "envelope-4"} <= set(self.kept(after)))
        self.assert_partition(before, after)

    def test_test_only_changes_run_nothing_on_the_pr(self):
        # (the old selection ran a --since report run of every shard that uses the test file)
        before, after = self.pr(TESTS + "ExactBooleanIssueCasesTests.cs")
        self.assertTrue(before[1])
        self.assertEqual({}, self.kept(after))
        self.assertTrue(all("only tests" in e["why"] for e in after[2]))
        self.assert_partition(before, after)
        # the label does not bring them back either (no mutated file changed)
        self.assertEqual({}, self.kept(self.pr(TESTS + "ExactBooleanIssueCasesTests.cs", labels=["mutation"])[1]))

    def test_config_change_on_a_pr_skips_the_shard(self):
        before, after = self.pr(CONFIGS["mesh-planar"][0])
        self.assertEqual(("none", []), after[:2])
        self.assertEqual(["planar"], [e["shard"] for e in after[2]])
        self.assertIn("full run on master", after[2][0]["why"])
        self.assert_partition(before, after)

    def test_arithmetic_config_change_on_a_pr_skips_it(self):
        before, after = self.pr(CONFIGS["config"][0])
        self.assertEqual("full", before[0])
        self.assertEqual(("none", []), after[:2])
        self.assertEqual(["arithmetic"], [e["shard"] for e in after[2]])

    def test_tools_change_on_a_pr_makes_no_full_run(self):
        # (the old selection kept the full run of the representative shard `fixed` on the PR)
        before, after = self.pr("tests/mutation/run_stryker.sh")
        self.assertIn({"shard": "fixed", "mode": "full"}, before[1])
        self.assertEqual({}, self.kept(after))
        self.assertIn("fixed", [e["shard"] for e in after[2]])
        self.assert_partition(before, after)

    def test_a_config_change_with_changed_exact_lines_makes_no_full_run(self):
        # (the old gate kept the full run on the PR; now the dispatched full run on master after the merge is the gate)
        exact = self.exact_source("mesh-boolean-1")
        before, after = self.pr(CONFIGS["mesh-boolean-1"][0], exact)
        self.assertIn({"shard": "boolean-1", "mode": "full"}, before[1])
        self.assertNotIn("boolean-1", self.kept(after))
        why = [e["why"] for e in after[2] if e["shard"] == "boolean-1"][0]
        self.assertIn("dispatch the full run", why)
        # the other boolean shards still run incrementally for the exact file
        self.assertEqual({b: "source" for b in BO if b != "boolean-1"}, {k: v for k, v in self.kept(after).items() if k in BO})
        self.assert_partition(before, after)

    def test_an_exact_change_with_a_tool_change_keeps_the_incremental_run(self):
        # (review: the workflow, the tools or a script force `full` for every shard; for a shard of a changed exact file whose own
        # config did not change the incremental run still fits its baseline, so the PR stays gated instead of losing the run)
        for tool, shard in ((".github/workflows/mutation.yml", "boolean-1"), (".config/dotnet-tools.json", "boolean-1"),
                            ("tests/mutation/run_stryker.sh", "fixed")):
            exact = self.exact_source("mesh-" + shard)
            before, after = self.pr(tool, exact)
            self.assertIn({"shard": shard, "mode": "full"}, before[1], tool)
            self.assertEqual("source", self.kept(after).get(shard), tool)
            self.assertNotIn("planar", self.kept(after), tool)
            self.assert_partition(before, after)
        before, after = self.pr(".github/workflows/mutation.yml", self.exact_source("config"))
        self.assertEqual(("full", "source"), (before[0], after[0]))

    def test_a_gate_moved_to_master_is_a_warning(self):
        exact = self.exact_source("mesh-boolean-1")
        _, after = self.pr(CONFIGS["mesh-boolean-1"][0], exact)
        entry = [e for e in after[2] if e["shard"] == "boolean-1"][0]
        from select_shards import skip_notice
        self.assertTrue(skip_notice(entry).startswith("::warning::boolean-1:"))
        self.assertTrue(skip_notice({"shard": "planar", "why": "only tests of this shard changed: scheduled run on master"}).startswith("::notice::"))

    def test_the_arithmetic_gate_makes_no_full_run(self):
        exact = self.exact_source("config")
        before, after = self.pr(CONFIGS["config"][0], exact)
        self.assertEqual("full", before[0])
        self.assertEqual("none", after[0])
        self.assertIn("arithmetic", [e["shard"] for e in after[2]])

    def test_a_config_that_only_moved_its_spans_runs_incrementally_for_an_exact_change(self):
        # (select_shards.py main: such a config is not in `changed` but in `moved`, mode source)
        exact = self.exact_source("mesh-planar")
        arithmetic, mesh = select([exact], CONFIGS, moved={"mesh-planar"})
        a2, mesh2, skipped = pr_selection(arithmetic, mesh, CONFIGS, [exact])
        self.assertIn({"shard": "planar", "mode": "source"}, mesh2)
        # a resplit alone (no mutated file changed) is a config change: master
        arithmetic, mesh = select([], CONFIGS, moved={"mesh-planar"})
        a2, mesh2, skipped = pr_selection(arithmetic, mesh, CONFIGS, [])
        self.assertEqual(("none", []), (a2, mesh2))
        self.assertEqual(["planar"], [e["shard"] for e in skipped])

    def test_every_pr_selection_is_source_only(self):
        # over single changed files of every config: a PR never gets a full or a tests run
        paths = set()
        for config, sources, tests in CONFIGS.values():
            paths |= {config} | sources | tests
        for p in sorted(paths) + sorted(ALWAYS) + ["tests/mutation/run_stryker.sh"]:
            for labels in ((), ("mutation",)):
                before, after = self.pr(p, labels=labels)
                self.assert_partition(before, after)

    def run_cli(self, *args, stdin="", env=None):
        e = dict(os.environ)
        e.pop("BASE", None); e.pop("HEAD", None)
        return subprocess.run([sys.executable, os.path.join(HERE, "select_shards.py")] + list(args), input=stdin, capture_output=True,
                              text=True, check=True, env=e).stdout.splitlines()

    def test_cli_on_a_pr_prints_the_skipped_shards(self):
        lines = self.run_cli("--pr", stdin=CONFIGS["mesh-planar"][0] + "\n")
        self.assertIn("arithmetic=none", lines)
        self.assertIn("mesh=[]", lines)
        skipped = json.loads([l for l in lines if l.startswith("skipped=")][0][len("skipped="):])
        self.assertEqual(["planar"], [e["shard"] for e in skipped])

    def test_cli_reads_the_labels(self):
        lines = self.run_cli("--pr", "--labels", "bugfix,mutation", stdin="mesh_ops/MeshRoundingRepair.cs\n")
        mesh = json.loads([l for l in lines if l.startswith("mesh=")][0][len("mesh="):])
        self.assertEqual(set(RO), {m["shard"] for m in mesh})
        lines = self.run_cli("--pr", "--labels", "", stdin="mesh_ops/MeshRoundingRepair.cs\n")
        self.assertIn("mesh=[]", lines)

    def test_cli_on_master_selects_as_before(self):
        # (no --pr) the shard runs in full, and no skipped line
        lines = self.run_cli(stdin=CONFIGS["mesh-planar"][0] + "\n")
        self.assertIn('mesh=[{"shard": "planar", "mode": "full"}]', lines)
        self.assertFalse([l for l in lines if l.startswith("skipped=")])

    def test_cli_manual_dispatch_runs_everything_in_full(self):
        # workflow_dispatch / schedule: --all, never skipped
        lines = self.run_cli("--all", "--only", "planar")
        self.assertIn('mesh=[{"shard": "planar", "mode": "full"}]', lines)
        self.assertFalse([l for l in lines if l.startswith("skipped=")])
        # (the workflow passes --pr only for a pull_request event; with --all it would not skip either)
        self.assertIn('mesh=[{"shard": "planar", "mode": "full"}]', self.run_cli("--all", "--only", "planar", "--pr"))

    def test_report_names_the_shard_and_the_reason(self):
        entries = [{"shard": "planar", "why": "x: full run on master"}, {"shard": "arithmetic", "why": "y: scheduled run on master"}]
        notices = self.run_cli("report", json.dumps(entries))
        self.assertEqual(2, len(notices))
        for e, n in zip(entries, notices):
            self.assertTrue(n.startswith("::notice::" + e["shard"] + ":"))
            self.assertIn("NOT RUN", n)
            self.assertIn(e["why"], n)
        summary = "\n".join(self.run_cli("report", json.dumps(entries[:1]), "--summary"))
        self.assertIn("`planar`", summary)
        self.assertIn("full run on master", summary)
        self.assertEqual([], self.run_cli("report", "[]", "--summary"))

    def test_every_exact_file_has_a_shard_or_is_listed(self):
        # (review of #1469: a changed exact file is the only PR trigger, so one without a shard must not go unnoticed)
        import glob
        from select_shards import NO_SHARD, exact_globs, exact_gated
        files = {os.path.relpath(f, ROOT).replace(os.sep, "/") for g in exact_globs() for f in glob.glob(os.path.join(ROOT, g))}
        mutated = set().union(*(c[1] for c in CONFIGS.values()))
        for f in sorted(files):
            self.assertTrue((f in mutated) != (f in NO_SHARD), f"{f}: in a shard and on NO_SHARD, or in neither")
        for f, why in NO_SHARD.items():
            self.assertTrue(exact_gated(f), f)
            self.assertTrue(os.path.isfile(os.path.join(ROOT, f)), f)
            self.assertTrue(why)

    def test_workflow_paths_cover_the_exact_code(self):
        # (so a PR that changes an exact file without a shard still starts the `changes` job and gets the notice)
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            workflow = f.read()
        block = workflow[workflow.index("paths:"):workflow.index("schedule:")]
        patterns = re.findall(r"- '([^']+)'", block)
        from select_shards import exact_globs
        for g in exact_globs():
            self.assertIn(g, patterns, g)

    def test_unsharded_exact_files_get_a_notice(self):
        from select_shards import unsharded
        self.assertEqual(["intersection/TriangleTriangleFilter.cs"], unsharded(["intersection/TriangleTriangleFilter.cs", "math/ExactPoint3.cs", "comp_geom/SnapRounding2.cs", "docs/x.md"]))
        notice = self.run_cli("hint", stdin="intersection/TriangleTriangleFilter.cs\n")
        self.assertTrue(notice and notice[0].startswith("::notice::intersection/TriangleTriangleFilter.cs"))
        self.assertIn("without a mutation shard", "\n".join(self.run_cli("hint", "--summary", stdin="intersection/TriangleTriangleFilter.cs\n")))
        self.assertEqual([], self.run_cli("hint", stdin="math/FixedPoint3.cs\n"))
        # (since #1685 the ray-triangle test has its shard: no notice)
        self.assertEqual([], unsharded(["math/ExactRayTriangle.cs"]))
        self.assertEqual([], self.run_cli("hint", stdin="math/ExactRayTriangle.cs\n"))

    def test_error_bound_hint(self):
        from select_shards import audit_files, error_bound_hint
        files = audit_files()
        self.assertIn("math/ExactPredicates.cs", files)
        self.assertIn("queries/CertifiedDet3.cs", files)
        self.assertEqual(["tools/ErrorBoundAudit/bounds.py"], error_bound_hint(["tools/ErrorBoundAudit/bounds.py", "docs/x.md"]))
        self.assertEqual(["math/ExactPredicates.cs"], error_bound_hint(["math/ExactPredicates.cs"]))
        self.assertEqual([], error_bound_hint(["mesh_ops/MeshRoundingRepair.cs"]))
        self.assertEqual([], self.run_cli("hint", stdin="docs/x.md\n"))
        self.assertTrue(self.run_cli("hint", stdin="tools/ErrorBoundAudit/bounds.py\n")[0].startswith("::warning::"))
        self.assertIn("label `mutation`", "\n".join(self.run_cli("hint", "--summary", stdin="tools/ErrorBoundAudit/bounds.py\n")))


if __name__ == "__main__":
    unittest.main()
