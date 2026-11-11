#!/usr/bin/env python3
"""run_local.sh with a stub dotnet: the result file it writes passes local_result.py check. Run by the changes job."""
import json
import os
import shutil
import stat
import subprocess
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
FILES = ["run_local.sh", "run_stryker.sh", "local_result.py", "incremental.py", "select_shards.py", "stryker-mesh-boolean-1.json",
         "stryker-config.json", "exclude_other_sources.py"]

STUB = r'''#!/usr/bin/env bash
case "$1" in
  tool) exit 0 ;;
  stryker)
    { head -1 "$G3_STUB_ROOT/mesh_ops/ExactMeshBoolean.cs"; head -1 "$G3_STUB_ROOT/mesh_ops/Other.cs"; } > "$G3_STUB_SEEN"
    out=StrykerOutput/2026/reports
    mkdir -p "$out"
    cp "$G3_STUB_REPORT" "$out/mutation-report.json"
    exit "${G3_STUB_RC:-0}" ;;
esac
exit 0
'''


def report(killed, survived):
    ms = [{"id": str(i), "mutatorName": "Equality", "status": "Killed", "location": {"start": {"line": 1, "column": 1}}} for i in range(killed)]
    ms += [{"id": "s%d" % i, "mutatorName": "Equality", "status": "Survived", "location": {"start": {"line": 2, "column": 1}}} for i in range(survived)]
    return {"files": {"/w/mesh_ops/ExactMeshBoolean.cs": {"mutants": ms}}}


class RunLocal(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.repo = os.path.join(self.dir, "repo")
        os.makedirs(os.path.join(self.repo, "tests", "mutation"))
        os.makedirs(os.path.join(self.repo, ".config"))
        os.makedirs(os.path.join(self.repo, "mesh_ops"))
        for name in ("ExactMeshBoolean.cs", "Other.cs"):
            with open(os.path.join(self.repo, "mesh_ops", name), "w") as f:
                f.write("class %s {}\n" % name[:-3])
        self.seen = os.path.join(self.dir, "seen.txt")
        for f in FILES:
            shutil.copy(os.path.join(HERE, f), os.path.join(self.repo, "tests", "mutation", f))
        shutil.copy(os.path.join(HERE, "..", "..", ".config", "dotnet-tools.json"), os.path.join(self.repo, ".config"))
        self.bin = os.path.join(self.dir, "bin")
        os.mkdir(self.bin)
        stub = os.path.join(self.bin, "dotnet")
        with open(stub, "w") as f:
            f.write(STUB)
        os.chmod(stub, os.stat(stub).st_mode | stat.S_IEXEC)
        for c in (["init", "-q"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "-A"], ["commit", "-q", "-m", "base"]):
            subprocess.run(["git", "-C", self.repo] + c, check=True, capture_output=True)
        self.sha = subprocess.run(["git", "-C", self.repo, "rev-parse", "HEAD"], check=True, capture_output=True, text=True).stdout.strip()

    def tearDown(self):
        shutil.rmtree(self.dir, ignore_errors=True)

    def run_script(self, args, killed=90, survived=10, rc="0"):
        rep = os.path.join(self.dir, "rep.json")
        with open(rep, "w") as f:
            json.dump(report(killed, survived), f)
        env = dict(os.environ, PATH=self.bin + os.pathsep + os.environ["PATH"], G3_STUB_REPORT=rep, G3_STUB_RC=rc, PROGRESS_SECONDS="1000000",
                   G3_STUB_ROOT=self.repo, G3_STUB_SEEN=self.seen)
        return subprocess.run(["bash", os.path.join(self.repo, "tests", "mutation", "run_local.sh")] + args, cwd=self.repo, env=env,
                              capture_output=True, text=True)

    def result_path(self, shard="boolean-1"):
        return os.path.join(self.repo, "tests", "mutation", "local-results", self.sha, shard + ".json")

    def test_a_full_run_writes_a_result_that_the_check_accepts(self):
        r = self.run_script(["boolean-1"])
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        with open(self.result_path()) as f:
            res = json.load(f)
        self.assertEqual((self.sha, "boolean-1", "full", 90.0), (res["sha"], res["shard"], res["mode"], res["score"]))
        check = subprocess.run(["python3", os.path.join(HERE, "local_result.py"), "check", "--root", self.repo, "--head", self.sha],
                               capture_output=True, text=True)
        self.assertEqual(0, check.returncode, check.stdout + check.stderr)

    def test_below_the_break_the_result_is_written_and_the_exit_code_is_1(self):
        r = self.run_script(["boolean-1"], killed=70, survived=30)
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertTrue(os.path.exists(self.result_path()))
        check = subprocess.run(["python3", os.path.join(HERE, "local_result.py"), "check", "--root", self.repo, "--head", self.sha],
                               capture_output=True, text=True)
        self.assertEqual(1, check.returncode)
        self.assertIn("below the break", check.stdout)

    def test_a_dirty_tree_is_refused(self):
        with open(os.path.join(self.repo, "tests", "mutation", "stryker-config.json"), "a") as f:
            f.write(" ")
        r = self.run_script(["boolean-1"])
        self.assertEqual(2, r.returncode)
        self.assertIn("not clean", r.stderr)
        self.assertFalse(os.path.exists(self.result_path()))

    def test_an_unknown_shard_and_no_shard_are_errors(self):
        self.assertEqual(2, self.run_script(["nonexistent-9"]).returncode)
        self.assertEqual(2, self.run_script([]).returncode)

    def test_the_other_files_are_marked_while_stryker_runs_and_restored_after(self):
        r = self.run_script(["boolean-1"])
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        with open(self.seen) as f:
            seen = f.read().split("\n")
        self.assertEqual("class ExactMeshBoolean {}", seen[0])                 # the shard's file: untouched
        self.assertIn("<auto-generated", seen[1])                              # any other library file: marked
        git = ["git", "-C", self.repo]
        self.assertEqual(0, subprocess.run(git + ["diff", "--quiet"]).returncode)    # restored, byte for byte

    def test_the_files_are_restored_when_stryker_fails(self):
        r = self.run_script(["boolean-1"], rc="7")
        self.assertEqual(0, subprocess.run(["git", "-C", self.repo, "diff", "--quiet"]).returncode)
        with open(os.path.join(self.repo, "mesh_ops", "Other.cs")) as f:
            self.assertEqual("class Other {}\n", f.read())

    def test_a_failed_restore_fails_the_run(self):
        # (a tracked file that differs after the undo: here a file the undo cannot know, changed by Stryker itself)
        stub = os.path.join(self.bin, "dotnet")
        with open(stub, "a") as f:
            f.write('')
        with open(stub) as f:
            text = f.read()
        text = text.replace('    exit "${G3_STUB_RC:-0}" ;;', '    echo changed > "$G3_STUB_ROOT/mesh_ops/Other.cs"\n    exit "${G3_STUB_RC:-0}" ;;')
        with open(stub, "w") as f:
            f.write(text)
        r = self.run_script(["boolean-1"])
        self.assertNotEqual(0, r.returncode)
        self.assertIn("differ from the checkout", r.stderr + r.stdout)

    def test_the_run_is_a_full_run_not_a_pull_request_run(self):
        # (NO_FULL from the environment must not turn the local run into a skipped one)
        rep = os.path.join(self.dir, "rep.json")
        with open(rep, "w") as f:
            json.dump(report(90, 10), f)
        env = dict(os.environ, PATH=self.bin + os.pathsep + os.environ["PATH"], G3_STUB_REPORT=rep, NO_FULL="1", PROGRESS_SECONDS="1000000",
                   G3_STUB_ROOT=self.repo, G3_STUB_SEEN=self.seen)
        r = subprocess.run(["bash", os.path.join(self.repo, "tests", "mutation", "run_local.sh"), "boolean-1"], cwd=self.repo, env=env,
                           capture_output=True, text=True)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertTrue(os.path.exists(self.result_path()))


if __name__ == "__main__":
    unittest.main()
