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

    def run_script(self, mode, no_full, baseline=None):
        env = dict(os.environ, PATH=self.bin + os.pathsep + os.environ["PATH"], MODE=mode, BASE="HEAD",
                   GITHUB_STEP_SUMMARY=self.summary, PROGRESS_SECONDS="1000000")
        env.pop("NO_FULL", None)
        if no_full:
            env["NO_FULL"] = "1"
        if baseline is not None:
            env["BASELINE_DIR"] = baseline
        r = subprocess.run(["bash", SCRIPT, "stryker-x.json"], cwd=self.work, env=env, capture_output=True, text=True)
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
        with open(os.path.join(self.work, "stryker-x.json"), "w") as f:
            json.dump({"stryker-config": {"mutate": ["**/mesh_ops/ExactMeshBoolean.cs"]}}, f)
        r, calls, summary = self.run_script("source", True, baseline=base)
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertNotIn("stryker", calls)
        self.assertIn("full run on master", summary)

    def test_outside_a_pull_request_full_mode_runs_stryker(self):
        r, calls, summary = self.run_script("full", False)
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertIn("stryker --log-to-file --config-file stryker-x.json", calls)
        self.assertNotIn("full run on master", summary)
        self.assertTrue(os.path.exists(os.path.join(self.work, "StrykerOutput", "full-run")))

    def test_pull_request_tests_mode_still_runs_since(self):
        r, calls, summary = self.run_script("tests", True)
        self.assertEqual(0, r.returncode, r.stderr)
        self.assertIn("--since:HEAD", calls)
        self.assertNotIn("full run on master", summary)


if __name__ == "__main__":
    unittest.main()
