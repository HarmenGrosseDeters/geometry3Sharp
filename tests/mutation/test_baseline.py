#!/usr/bin/env python3
"""Tests of baseline.py. Run by the changes job of the mutation workflow."""
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest

import ast
import hashlib
import inspect

import baseline
import incremental

HERE = os.path.dirname(os.path.abspath(__file__))


def mutant(status, reason=""):
    return {"id": "1", "mutatorName": "Equality", "status": status, "statusReason": reason,
            "location": {"start": {"line": 1, "column": 1}, "end": {"line": 1, "column": 2}}}


# as Stryker 5.0 writes it: files keyed by absolute paths
REPORT = {"schemaVersion": "2", "thresholds": {"high": 80, "low": 60}, "projectRoot": "/w/repo", "files": {
    "/w/repo/mesh_ops/MeshRoundingRepair.cs": {"language": "cs", "source": "class A {}",
                                               "mutants": [mutant("Killed"), mutant("Survived"), mutant("Ignored")]},
    "/w/repo/mesh/DMesh3.cs": {"language": "cs", "source": "class B {}", "mutants": [mutant("Ignored"), mutant("Ignored")]},
    "/w/repo/core/Empty.cs": {"language": "cs", "source": "", "mutants": []},
}}


# The cache key of a baseline holds BASELINE_FORMAT (a file) instead of the hashes of this script and incremental.py:
# an edit of a comment, of this file or of an unrelated function then keeps every shard's baseline. The functions below decide
# what a stored baseline looks like and how incremental.py reads it. When one of them changes (this test fails),
# decide: does an OLD baseline still give the same results through the new code? If not (layout, keys, how mutants are
# matched, counted or scored), bump BASELINE_FORMAT, so that every shard runs in full once and stores a new baseline
# (the next weekly or dispatched run on master seeds them); if so, only update the fingerprint.
FORMAT_FUNCTIONS = (baseline.relative_key, baseline.strip, incremental.parse_mutate, incremental.baseline_file,
                    incremental.touches, incremental.counted, incremental.score, incremental.plan, incremental.merge)
FORMAT_FINGERPRINT = "a7da7b708a6ec1bf"


def format_fingerprint():
    h = hashlib.sha256()
    for f in FORMAT_FUNCTIONS:
        h.update(ast.dump(ast.parse(textwrap.dedent(inspect.getsource(f)))).encode())
    return h.hexdigest()[:16]


class Baseline(unittest.TestCase):
    def test_a_change_of_the_baseline_format_functions_needs_a_decision_on_BASELINE_FORMAT(self):
        self.assertEqual(FORMAT_FINGERPRINT, format_fingerprint(),
                         "the functions that write or read the baseline changed: bump tests/mutation/BASELINE_FORMAT if an "
                         "old baseline would give other results (README, Baseline), then set FORMAT_FINGERPRINT to "
                         + format_fingerprint())

    def test_strip_keeps_only_files_with_tested_mutants_under_relative_keys(self):
        stripped = baseline.strip(REPORT)
        self.assertEqual(["mesh_ops/MeshRoundingRepair.cs"], list(stripped["files"]))
        self.assertEqual(REPORT["files"]["/w/repo/mesh_ops/MeshRoundingRepair.cs"],
                         stripped["files"]["mesh_ops/MeshRoundingRepair.cs"])
        self.assertEqual(REPORT["thresholds"], stripped["thresholds"])

    def test_relative_key(self):
        self.assertEqual("mesh_ops/A.cs", baseline.relative_key("/w/repo/mesh_ops/A.cs", "/w/repo"))
        self.assertEqual("mesh_ops/A.cs", baseline.relative_key("/w/repo/mesh_ops/A.cs", "/w/repo/"))
        self.assertEqual("mesh_ops/A.cs", baseline.relative_key("C:\\w\\repo\\mesh_ops\\A.cs", "C:\\w\\repo"))
        self.assertEqual("mesh_ops/A.cs", baseline.relative_key("mesh_ops/A.cs", "/w/repo"))   # already relative
        self.assertEqual("/elsewhere/A.cs", baseline.relative_key("/elsewhere/A.cs", "/w/repo"))

    def store(self, d, runs):
        for run, files in runs:
            os.makedirs(os.path.join(d, "out", run, "reports"))
            with open(os.path.join(d, "out", run, "reports", "mutation-report.json"), "w") as f:
                json.dump(dict(REPORT, files=files), f)
        env = dict(os.environ, GITHUB_OUTPUT=os.path.join(d, "gh-output"))
        p = subprocess.run([sys.executable, os.path.join(HERE, "baseline.py"), "store", os.path.join(d, "out"),
                            os.path.join(d, "baseline")], env=env, capture_output=True, text=True)
        with open(env["GITHUB_OUTPUT"]) as f:
            return p, f.read()

    def test_store_takes_the_latest_report(self):
        with tempfile.TemporaryDirectory() as d:
            p, gh = self.store(d, [("2026-10-01.10-00-00", {}), ("2026-10-02.10-00-00", REPORT["files"])])
            self.assertEqual(0, p.returncode, p.stdout + p.stderr)
            with open(os.path.join(d, "baseline", "stryker-report.json")) as f:
                self.assertEqual(["mesh_ops/MeshRoundingRepair.cs"], list(json.load(f)["files"]))
            self.assertEqual("stored=true\n", gh)

    def test_store_without_a_report_stores_nothing(self):
        with tempfile.TemporaryDirectory() as d:
            p, gh = self.store(d, [])
            self.assertEqual(0, p.returncode)
            self.assertFalse(os.path.exists(os.path.join(d, "baseline")))
            self.assertEqual("stored=false\n", gh)


if __name__ == "__main__":
    unittest.main()
