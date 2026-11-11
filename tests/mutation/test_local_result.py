#!/usr/bin/env python3
"""Tests of local_result.py (results of local mutation runs and their check). Run by the changes job of the mutation workflow."""
import json
import os
import shutil
import subprocess
import tempfile
import unittest

import local_result

TOOLS = {"version": 1, "isRoot": True, "tools": {"dotnet-stryker": {"version": "5.0.0", "commands": ["dotnet-stryker"], "rollForward": False}}}


def config(brk):
    return {"stryker-config": {"mutate": ["**/mesh_ops/X.cs"], "thresholds": {"high": 90, "low": 81, "break": brk}}}


def mutant(status, line=1):
    return {"id": str(line), "mutatorName": "Equality", "status": status, "location": {"start": {"line": line, "column": 1}, "end": {"line": line, "column": 2}}}


def report(killed, timeout, survived, nocoverage):
    ms = [mutant("Killed", 1)] * killed + [mutant("Timeout", 2)] * timeout + [mutant("Survived", 3)] * survived + \
         [mutant("NoCoverage", 4)] * nocoverage + [mutant("Ignored", 5)] * 7
    return {"files": {"/w/mesh_ops/X.cs": {"mutants": ms}}}


class LocalResult(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.root = os.path.join(self.dir, "repo")
        os.makedirs(os.path.join(self.root, ".config"))
        os.makedirs(os.path.join(self.root, "tests", "mutation"))
        self.write(".config/dotnet-tools.json", json.dumps(TOOLS))
        self.write("tests/mutation/stryker-mesh-boolean-1.json", json.dumps(config(79)))
        self.write("mesh_ops/X.cs", "class X {}\n")
        self.git("init", "-q")
        self.git("config", "user.email", "t@example.com")
        self.git("config", "user.name", "t")
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "base")
        self.sha = self.git("rev-parse", "HEAD").strip()
        self.report = os.path.join(self.dir, "report.json")
        with open(self.report, "w") as f:
            json.dump(report(85, 5, 8, 2), f)           # 90 of 100 = 90 %

    def tearDown(self):
        shutil.rmtree(self.dir, ignore_errors=True)

    def write(self, rel, text):
        p = os.path.join(self.root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", newline="\n") as f:
            f.write(text)

    def git(self, *a):
        return subprocess.run(["git", "-C", self.root] + list(a), check=True, capture_output=True, text=True).stdout

    def make(self, **change):
        """a result for the current HEAD, stored where run_local puts it, with fields changed"""
        r = local_result.record(self.root, "boolean-1", self.report, "2026-11-12T01:00:00Z", "linux")
        directory = change.pop("_dir", r["sha"])
        r.update(change)
        d = os.path.join(self.root, local_result.RESULTS_DIR, directory)
        os.makedirs(d, exist_ok=True)
        path = os.path.join(d, "boolean-1.json")
        with open(path, "w") as f:
            json.dump(r, f)
        return path

    def check(self, path, head=None):
        problems, _ = local_result.check_one(self.root, head or self.git("rev-parse", "HEAD").strip(), path)
        return problems

    def test_record_and_check_roundtrip(self):
        path = self.make()
        self.assertEqual([], self.check(path))
        with open(path) as f:
            r = json.load(f)
        self.assertEqual(self.sha, r["sha"])
        self.assertEqual("5.0.0", r["stryker_version"])
        self.assertEqual(79, r["break"])
        self.assertEqual({"killed": 85, "timeout": 5, "survived": 8, "nocoverage": 2}, r["counts"])
        self.assertAlmostEqual(90.0, r["score"])
        self.assertEqual(sorted(r["survivors"]), r["survivors"])
        self.assertEqual(10, len(r["survivors"]))

    def test_the_result_commit_on_top_of_the_gated_commit_is_fine(self):
        path = self.make()
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "results")
        self.assertEqual([], self.check(path))

    def test_a_code_change_after_the_run_is_refused(self):
        path = self.make()
        self.write("mesh_ops/X.cs", "class X { int a; }\n")
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "code")
        problems = self.check(path)
        self.assertEqual(1, len(problems))
        self.assertIn("run again at the head", problems[0])

    def test_a_sha_that_is_not_in_the_history_is_refused(self):
        path = self.make(sha="0" * 40, _dir="0" * 40)
        self.assertTrue(any("not the head" in p for p in self.check(path)))

    def test_a_changed_config_is_refused(self):
        path = self.make(config_sha256="0" * 64)                 # (what a run with another config file would record)
        self.assertTrue(any("hash differs" in p for p in self.check(path)))
        # and a config edited after the run is a change besides the results
        path = self.make()
        self.write("tests/mutation/stryker-mesh-boolean-1.json", json.dumps(config(60)))
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "config")
        self.assertTrue(any("run again at the head" in p for p in self.check(path)))

    def test_a_score_below_the_break_is_refused(self):
        with open(self.report, "w") as f:
            json.dump(report(70, 5, 20, 5), f)            # 75 %
        path = self.make()
        self.assertTrue(any("below the break" in p for p in self.check(path)))

    def test_a_score_that_does_not_match_the_counts_is_refused(self):
        path = self.make(score=99.0)
        self.assertTrue(any("does not match the counts" in p for p in self.check(path)))

    def test_a_break_that_is_not_the_configs_is_refused(self):
        path = self.make(**{"break": 50})
        self.assertTrue(any("the config has 79" in p for p in self.check(path)))

    def test_another_stryker_version_is_refused(self):
        path = self.make(stryker_version="5.1.0")
        self.assertTrue(any("Stryker 5.1.0" in p for p in self.check(path)))

    def test_only_a_full_run_counts(self):
        path = self.make(mode="incremental")
        self.assertTrue(any("only a full run" in p for p in self.check(path)))

    def test_a_result_in_the_wrong_directory_is_refused(self):
        path = self.make()
        wrong = os.path.join(self.root, local_result.RESULTS_DIR, "other")
        os.makedirs(wrong)
        moved = os.path.join(wrong, "boolean-1.json")
        shutil.move(path, moved)
        self.assertTrue(any("stored in the directory other" in p for p in self.check(moved)))

    def test_missing_and_unknown_keys_are_refused(self):
        path = self.make()
        with open(path) as f:
            r = json.load(f)
        r.pop("score")
        r["extra"] = 1
        with open(path, "w") as f:
            json.dump(r, f)
        self.assertTrue(any("wrong keys" in p for p in self.check(path)))

    def test_an_unknown_shard_is_refused(self):
        path = self.make(shard="nonexistent-9")
        self.assertTrue(self.check(path))

    def test_a_dirty_tree_cannot_be_recorded(self):
        self.write("mesh_ops/X.cs", "class X { int dirty; }\n")
        with self.assertRaises(RuntimeError):
            local_result.record(self.root, "boolean-1", self.report, "now", "linux")

    def test_the_arithmetic_config_has_its_own_name(self):
        self.assertEqual("tests/mutation/stryker-config.json", local_result.config_path("arithmetic"))
        self.assertEqual("tests/mutation/stryker-mesh-rounding-3.json", local_result.config_path("rounding-3"))
        with self.assertRaises(ValueError):
            local_result.config_path("../x")

    def test_crlf_in_the_config_does_not_change_the_hash(self):
        self.assertEqual(local_result.normalized_hash(b"a\nb\n"), local_result.normalized_hash(b"a\r\nb\r\n"))

    def test_check_over_a_tree_and_its_summary(self):
        head = self.git("rev-parse", "HEAD").strip()
        path = self.make(score=1.0)
        summary = os.path.join(self.dir, "summary.md")
        code, text = local_result.check(self.root, head, [], summary)
        self.assertEqual(1, code)
        self.assertIn("INVALID", text)
        with open(summary) as f:
            self.assertIn("**invalid**", f.read())
        path = self.make()                                       # (the valid result replaces it)
        code, text = local_result.check(self.root, head, [], summary)
        self.assertEqual(0, code)
        self.assertIn("ok ", text)
        with open(summary) as f:
            self.assertIn("counts as the shard passing", f.read())

    def test_no_results_is_fine(self):
        code, text = local_result.check(self.root, self.sha, [], None)
        self.assertEqual((0, "no local results"), (code, text))


if __name__ == "__main__":
    unittest.main()
