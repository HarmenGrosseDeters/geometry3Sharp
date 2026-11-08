#!/usr/bin/env python3
"""Tests of select_shards.py, on the real configs. Run by the changes job of the mutation workflow."""
import fnmatch
import os
import re
import subprocess
import sys
import unittest

from select_shards import ALWAYS, TESTS, load_configs, select, workflow_edit_forces_full

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONFIGS = load_configs()
ALL_MESH = {"rounding-1", "rounding-2", "boolean-1", "boolean-2", "arrangement-1", "arrangement-2", "arrangement-3", "arrangement-4", "planar", "fixed", "generated", "envelope-1", "envelope-2", "envelope-3", "envelope-4"}


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
        self.assertEqual(("none", {"rounding-1": "source", "rounding-2": "source"}), run("mesh_ops/MeshRoundingRepair.cs"))
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
        self.assertEqual(("none", {"boolean-1": "tests", "boolean-2": "tests", "rounding-1": "tests", "rounding-2": "tests"}),
                         run(TESTS + "ExactBooleanIssueCasesTests.cs"))
        self.assertEqual(("none", {"boolean-2": "tests"}), run(TESTS + "ExactBooleanFilterTests.cs"))
        self.assertEqual(("tests", {}), run(TESTS + "PredicateDifferentialTests.cs"))

    def test_test_class_with_a_method_filter_maps_to_its_file(self):
        arithmetic, mesh = run(TESTS + "MeshRoundingRepairTests.cs")
        self.assertEqual("tests", mesh["arrangement-1"])
        self.assertEqual("tests", mesh["rounding-1"])
        self.assertEqual("tests", mesh["rounding-2"])

    def test_source_wins_over_tests(self):
        self.assertEqual("source", run("mesh_ops/MeshRoundingRepair.cs", TESTS + "ExactBooleanIssueCasesTests.cs")[1]["rounding-1"])

    def test_config_wins_over_source(self):
        self.assertEqual(("none", {"rounding-1": "full", "rounding-2": "source"}),
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
                e("cat report.md || true", "cat report.md >> \"$S\" || true"),
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
            self.assertEqual('arithmetic=none\nmesh=[{"shard": "rounding-1", "mode": "source"}, {"shard": "rounding-2", "mode": "source"}]\n', out)
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
