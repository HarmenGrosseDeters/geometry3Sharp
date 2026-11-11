#!/usr/bin/env python3
"""Tests of select_shards.py, on the real configs. Run by the changes job of the mutation workflow."""
import fnmatch
import json
import os
import re
import subprocess
import sys
import unittest

from select_shards import ALWAYS, TESTS, load_configs, select, workflow_edit_forces_full

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONFIGS = load_configs()
RO = ["rounding-%d" % i for i in range(1, 9)]
ALL_MESH = set(RO) | {"boolean-1", "boolean-2", "arrangement-1", "arrangement-2", "arrangement-3", "arrangement-4", "planar", "fixed", "generated", "envelope-1", "envelope-2", "envelope-3", "envelope-4",
                                                "terrain-arith", "terrain-kernel", "terrain-overlay"}


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
        self.assertEqual(("none", {r: "source" for r in RO}), run("mesh_ops/MeshRoundingRepair.cs"))
        self.assertEqual(("none", {"boolean-1": "source", "boolean-2": "source"}), run("mesh_ops/ExactMeshBoolean.cs"))
        self.assertEqual(("none", {"arrangement-1": "source", "arrangement-2": "source", "arrangement-3": "source", "arrangement-4": "source"}),
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
        self.assertEqual(("none", dict({"boolean-1": "tests", "boolean-2": "tests"}, **{r: "tests" for r in RO})),
                         run(TESTS + "ExactBooleanIssueCasesTests.cs"))
        self.assertEqual(("none", {"boolean-2": "tests"}), run(TESTS + "ExactBooleanFilterTests.cs"))
        self.assertEqual(("tests", {}), run(TESTS + "PredicateDifferentialTests.cs"))

    def test_test_class_with_a_method_filter_maps_to_its_file(self):
        arithmetic, mesh = run(TESTS + "MeshRoundingRepairTests.cs")
        self.assertEqual("tests", mesh["arrangement-1"])
        for r in RO:
            self.assertEqual("tests", mesh[r])

    def test_filter_with_and_and_a_negated_term_maps_to_the_positive_class(self):
        # terrain-overlay: "FullyQualifiedName~TerrainSlopeCatchLineTests&FullyQualifiedName!~RandomTerrains"
        self.assertEqual(("none", {"terrain-overlay": "tests"}), run(TESTS + "TerrainSlopeCatchLineTests.cs"))
        self.assertEqual(("none", {"terrain-overlay": "source"}), run("mesh_ops/TerrainSlopeOverlay.cs"))

    def test_source_wins_over_tests(self):
        self.assertEqual("source", run("mesh_ops/MeshRoundingRepair.cs", TESTS + "ExactBooleanIssueCasesTests.cs")[1]["rounding-1"])

    def test_config_wins_over_source(self):
        self.assertEqual(("none", dict({r: "source" for r in RO}, **{"rounding-1": "full"})),
                         run("mesh_ops/MeshRoundingRepair.cs", "tests/mutation/stryker-mesh-rounding-1.json"))
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

    def test_the_scripts_of_the_runs_run_a_representative_shard_in_full(self):
        # a change to run_stryker.sh, incremental.py or baseline.py can break every shard's real run (the fake-dotnet tests
        # cannot see Stryker itself): the cheapest mesh shard runs in full, the others by their own rules
        for path in ("tests/mutation/run_stryker.sh", "tests/mutation/incremental.py", "tests/mutation/baseline.py"):
            self.assertEqual(("none", {"fixed": "full"}), run(path), path)
        self.assertEqual(("none", dict({r: "source" for r in RO}, fixed="full")),
                         run("tests/mutation/run_stryker.sh", "mesh_ops/MeshRoundingRepair.cs"))
        # the test files and select_shards.py run in the `changes` job of every run (the workflow paths cover them)
        self.assertEqual(("none", {}), run("tests/mutation/test_incremental.py", "tests/mutation/select_shards.py", "tests/mutation/BASELINE_FORMAT"))
        # (not when everything runs in full anyway, and --only keeps its list)
        arithmetic, mesh = select(["tests/mutation/run_stryker.sh"], CONFIGS, run_all=True, only={"planar"})
        self.assertEqual([{"shard": "planar", "mode": "full"}], mesh)

    def test_the_workflow_checks_local_results(self):
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            wf = f.read()
        self.assertIn("      - 'tests/mutation/local-results/**'", wf)        # (a result-only commit starts the check)
        self.assertIn("python3 test_local_result.py", wf)
        self.assertIn("python3 test_run_local.py", wf)
        step = wf[wf.index("Check the local results of this PR"):wf.index("      - id: select")]
        self.assertIn("github.event_name == 'pull_request'", step)
        self.assertIn("local_result.py check", step)
        self.assertIn('--head "$HEAD"', step)
        # a result file alone selects no shard (the check is all it starts)
        self.assertEqual(("none", []), select(["tests/mutation/local-results/" + "a" * 40 + "/boolean-1.json"], CONFIGS))

    def test_the_30_process_in_the_workflow(self):
        # 3.0 process (README): nightly full runs on master, no full run on a PR, runs on master queue
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            wf = f.read()
        self.assertRegex(wf, r"cron: '\d+ \d+ \* \* \*'")                      # every night
        self.assertNotRegex(wf, r"cron: '[^']*\* \* [1-7]'")                       # no weekly run beside it
        self.assertEqual(1, wf.count("cron:"))
        # one group for the runs on master, and cancel-in-progress only for pull requests
        self.assertIn("group: mutation-${{ github.event_name == 'pull_request' && github.ref || 'full-runs' }}", wf)
        # both Stryker steps tell run_stryker.sh that this is a PR (no full run there)
        self.assertEqual(2, wf.count("NO_FULL: ${{ github.event_name == 'pull_request' && '1' || '' }}"))
        # a PR stores no baseline (it ran no full shard), and a skipped shard has no report to find
        self.assertEqual(2, wf.count("always() && github.event_name != 'pull_request' && ("))
        self.assertEqual(2, wf.count("|| github.event_name == 'pull_request') && 'warn' || 'error'"))

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
        self.assertNotIn("branches:", on)                       # no branch filter: every PR is checked
        self.assertNotIn("paths-ignore", on)
        self.assertIn("schedule:", on)
        self.assertIn("workflow_dispatch:", on)
        self.assertIn("cancel-in-progress: ${{ github.event_name == 'pull_request' }}", wf)     # (a nightly run must not cancel a dispatch on master)
        jobs = wf[wf.index("\njobs:"):]
        changes = jobs[jobs.index("\n  changes:"):jobs.index("\n  exact-arithmetic:")]
        self.assertIn("set -o pipefail", changes)
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
        # every key and restore-key names its config and BASELINE_FORMAT, never the scripts, and a restore-key ends at
        # the hash, so that it can only match a baseline of the same config, tools and format
        with open(os.path.join(ROOT, ".github/workflows/mutation.yml"), encoding="utf-8") as f:
            lines = f.read().splitlines()
        keys = [l.strip() for l in lines if re.match(r"^\s*(key|restore-keys):", l) and "mutation-baseline-" in l]
        self.assertEqual(6, len(keys), keys)
        for l in keys:
            arithmetic = "mutation-baseline-arithmetic-" in l
            self.assertRegex(l, r"^(key|restore-keys): mutation-baseline-(arithmetic|mesh-\$\{\{ matrix\.shard \}\})-\$\{\{ hashFiles\(", l)
            config = "'tests/mutation/stryker-config.json'" if arithmetic else "format('tests/mutation/stryker-mesh-{0}.json', matrix.shard)"
            self.assertIn(config, l)
            if not arithmetic:
                self.assertIn("mutation-baseline-mesh-${{ matrix.shard }}-${{ hashFiles(", l)
            self.assertIn("'.config/dotnet-tools.json'", l)
            self.assertIn("'tests/mutation/BASELINE_FORMAT'", l)
            self.assertNotIn("baseline.py", l)
            self.assertNotIn("incremental.py", l)
            if l.startswith("restore-keys:"):
                self.assertTrue(l.endswith(") }}-"), l)
            else:
                self.assertTrue(l.endswith("-${{ github.run_id }}-${{ github.run_attempt }}"), l)

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


if __name__ == "__main__":
    unittest.main()
