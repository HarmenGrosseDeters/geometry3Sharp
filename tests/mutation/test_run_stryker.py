#!/usr/bin/env python3
"""run_stryker.sh on a pull request (NO_FULL=1): no full run, "full run on master" in the summary, success.

A stub `dotnet` on the PATH records its arguments, so no Stryker runs. Run: python3 test_run_stryker.py"""
import json
import os
import shutil
import stat
import subprocess
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "run_stryker.sh")


class RunStryker(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.bin = os.path.join(self.dir, "bin")
        os.mkdir(self.bin)
        self.calls = os.path.join(self.dir, "calls.txt")
        stub = os.path.join(self.bin, "dotnet")
        with open(stub, "w") as f:
            f.write('#!/usr/bin/env bash\necho "$@" >> "%s"\n' % self.calls)
        os.chmod(stub, os.stat(stub).st_mode | stat.S_IEXEC)
        self.summary = os.path.join(self.dir, "summary.md")
        self.work = os.path.join(self.dir, "work")
        os.mkdir(self.work)

    def tearDown(self):
        shutil.rmtree(self.dir, ignore_errors=True)

    def run_script(self, mode, no_full, baseline=None, base="HEAD"):
        env = dict(os.environ, PATH=self.bin + os.pathsep + os.environ["PATH"], MODE=mode, BASE=base,
                   GITHUB_STEP_SUMMARY=self.summary, PROGRESS_SECONDS="1000000", G3_NO_EXCLUDE="1")   # (the exclusion has its own tests in test_run_local.py)
        env.pop("NO_FULL", None)
        if no_full:
            env["NO_FULL"] = "1"
        if "G3_CHANGED_PATHS_FILE" not in env and base == "HEAD":
            empty = os.path.join(self.dir, "empty.txt")
            open(empty, "w").close()
            env["G3_CHANGED_PATHS_FILE"] = empty
        if baseline is not None:
            env["BASELINE_DIR"] = baseline
        r = subprocess.run(["bash", SCRIPT, "stryker-mesh-boolean-1.json"], cwd=self.work, env=env, capture_output=True, text=True)
        return r, self.read(self.calls), self.read(self.summary)

    @staticmethod
    def read(path):
        if not os.path.exists(path):
            return ""
        with open(path) as f:
            return f.read()

    def test_pull_request_full_mode_runs_nothing(self):
        r, calls, summary = self.run_script("full", True)
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertNotIn("stryker", calls)
        self.assertIn("full run on master", summary)
        self.assertIn("config or the tools changed", summary)
        self.assertTrue(os.path.exists(os.path.join(self.work, "StrykerOutput", "skipped-full")))
        self.assertFalse(os.path.exists(os.path.join(self.work, "StrykerOutput", "full-run")))   # (so nothing is stored as a baseline)

    def test_pull_request_source_mode_without_baseline_runs_nothing(self):
        r, calls, summary = self.run_script("source", True, baseline=os.path.join(self.dir, "nobaseline"))
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertNotIn("stryker", calls)
        self.assertIn("full run on master", summary)
        self.assertIn("no baseline in the cache", summary)

    def test_pull_request_source_mode_with_an_unusable_baseline_runs_nothing(self):
        # (a baseline whose files do not match the config: incremental.py plan answers `full`)
        base = os.path.join(self.dir, "baseline")
        os.mkdir(base)
        with open(os.path.join(base, "stryker-report.json"), "w") as f:
            json.dump({"files": {}}, f)
        with open(os.path.join(self.work, "stryker-mesh-boolean-1.json"), "w") as f:
            json.dump({"stryker-config": {"mutate": ["**/mesh_ops/ExactMeshBoolean.cs"]}}, f)
        r, calls, summary = self.run_script("source", True, baseline=base)
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertNotIn("stryker", calls)
        self.assertIn("full run on master", summary)

    def gated_run(self, paths, config_sources):
        """full mode on a pull request, a config that mutates `config_sources`, the PR changing `paths`"""
        with open(os.path.join(self.work, "changed.txt"), "w") as f:
            f.write("\n".join(paths))
        os.makedirs(os.path.join(self.work, "x"), exist_ok=True)
        cfg = os.path.join(HERE, "stryker-x-gate-test.json")        # (select_shards.py reads the configs next to itself)
        with open(cfg, "w") as f:
            json.dump({"stryker-config": {"mutate": ["**/" + p for p in config_sources], "test-case-filter": "FullyQualifiedName~T"}}, f)
        self.addCleanup(os.remove, cfg)
        env_file = os.path.join(self.work, "changed.txt")
        old = os.environ.get("G3_CHANGED_PATHS_FILE")
        os.environ["G3_CHANGED_PATHS_FILE"] = env_file
        try:
            return self.run_script_config("full", True, "stryker-x-gate-test.json")
        finally:
            if old is None:
                del os.environ["G3_CHANGED_PATHS_FILE"]
            else:
                os.environ["G3_CHANGED_PATHS_FILE"] = old

    def run_script_config(self, mode, no_full, config):
        env = dict(os.environ, PATH=self.bin + os.pathsep + os.environ["PATH"], MODE=mode, BASE="HEAD",
                   GITHUB_STEP_SUMMARY=self.summary, PROGRESS_SECONDS="1000000", G3_NO_EXCLUDE="1")
        env.pop("NO_FULL", None)
        if no_full:
            env["NO_FULL"] = "1"
        r = subprocess.run(["bash", SCRIPT, config], cwd=self.work, env=env, capture_output=True, text=True)
        return r, self.read(self.calls), self.read(self.summary)

    def test_pull_request_that_changes_an_exact_file_of_the_shard_keeps_the_full_run(self):
        # (the gate of CLAUDE.md: a PR that touches exact arithmetic is never merged without the threshold check)
        r, calls, summary = self.gated_run(["math/ExactPredicates.cs"], ["math/ExactPredicates.cs", "math/ExactPoint3.cs"])
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertIn("stryker --log-to-file --config-file", calls)
        self.assertNotIn("full run on master", summary)
        self.assertTrue(os.path.exists(os.path.join(self.work, "StrykerOutput", "full-run")))

    def test_pull_request_that_changes_an_exact_file_of_another_shard_skips_this_one(self):
        r, calls, summary = self.gated_run(["math/ExactPredicates.cs"], ["mesh_ops/ExactMeshBoolean.cs"])
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertNotIn("stryker", calls)
        self.assertIn("full run on master", summary)

    def test_pull_request_that_changes_an_exact_mesh_file_of_the_shard_keeps_the_full_run(self):
        # (mesh_ops/ExactMesh*.cs is exact code in .editorconfig: a boolean shard that must run in full on such a PR is gated)
        r, calls, summary = self.gated_run(["mesh_ops/ExactMeshBoolean.cs"], ["mesh_ops/ExactMeshBoolean.cs"])
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertIn("stryker --log-to-file --config-file", calls)
        self.assertNotIn("full run on master", summary)

    def test_pull_request_that_changes_a_not_exact_file_of_the_shard_skips_the_full_run(self):
        r, calls, summary = self.gated_run(["mesh_ops/MeshRoundingRepair.cs"], ["mesh_ops/MeshRoundingRepair.cs"])
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertNotIn("stryker", calls)
        self.assertIn("full run on master", summary)

    def test_a_git_error_in_the_gate_fails_closed(self):
        # (the diff against an unknown base fails: the gate must not read that as "nothing changed")
        env_old = os.environ.pop("G3_CHANGED_PATHS_FILE", None)
        r, calls, summary = self.run_script("full", True, base="no-such-ref-xyz")
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertIn("stryker --log-to-file --config-file", calls)
        self.assertNotIn("full run on master", summary)

    def test_an_unknown_config_fails_closed(self):
        self.assertIn("unknown config", subprocess.run(["python3", os.path.join(HERE, "select_shards.py"), "gate", "stryker-x.json"],
                                                       input="", capture_output=True, text=True).stdout)

    def test_a_script_change_keeps_the_full_run_of_the_representative_shard(self):
        with open(os.path.join(self.work, "changed.txt"), "w") as f:
            f.write("tests/mutation/run_stryker.sh\n")
        old = os.environ.get("G3_CHANGED_PATHS_FILE")
        os.environ["G3_CHANGED_PATHS_FILE"] = os.path.join(self.work, "changed.txt")
        try:
            r, calls, summary = self.run_script_config("full", True, "stryker-mesh-fixed.json")
            self.assertIn("stryker --log-to-file --config-file", calls)
            self.assertNotIn("full run on master", summary)
        finally:
            if old is None:
                del os.environ["G3_CHANGED_PATHS_FILE"]
            else:
                os.environ["G3_CHANGED_PATHS_FILE"] = old

    def test_outside_a_pull_request_full_mode_runs_stryker(self):
        r, calls, summary = self.run_script("full", False)
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertIn("stryker --log-to-file --config-file stryker-mesh-boolean-1.json", calls)
        self.assertNotIn("full run on master", summary)
        self.assertTrue(os.path.exists(os.path.join(self.work, "StrykerOutput", "full-run")))

    def test_pull_request_tests_mode_still_runs_since(self):
        r, calls, summary = self.run_script("tests", True)
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertIn("--since:HEAD", calls)
        self.assertNotIn("full run on master", summary)


if __name__ == "__main__":
    unittest.main()
