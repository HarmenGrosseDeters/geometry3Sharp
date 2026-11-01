#!/usr/bin/env python3
"""Tests of baseline.py and run_stryker.sh. Run by the changes job of the mutation workflow."""
import json
import os
import subprocess
import tempfile
import unittest

import baseline

HERE = os.path.dirname(os.path.abspath(__file__))


def mutant(status, reason=""):
    return {"id": "1", "mutatorName": "Equality", "status": status, "statusReason": reason,
            "location": {"start": {"line": 1, "column": 1}, "end": {"line": 1, "column": 2}}}


REPORT = {"schemaVersion": "2", "thresholds": {"high": 80, "low": 60}, "projectRoot": "/x", "files": {
    "mesh_ops/MeshRoundingRepair.cs": {"language": "cs", "source": "class A {}",
                                       "mutants": [mutant("Killed"), mutant("Survived"), mutant("Ignored")]},
    "mesh/DMesh3.cs": {"language": "cs", "source": "class B {}", "mutants": [mutant("Ignored"), mutant("Ignored")]},
    "core/Empty.cs": {"language": "cs", "source": "", "mutants": []},
}}


class Baseline(unittest.TestCase):
    def test_strip_keeps_only_files_with_tested_mutants(self):
        stripped = baseline.strip(REPORT)
        self.assertEqual(["mesh_ops/MeshRoundingRepair.cs"], list(stripped["files"]))
        self.assertEqual(REPORT["files"]["mesh_ops/MeshRoundingRepair.cs"], stripped["files"]["mesh_ops/MeshRoundingRepair.cs"])
        self.assertEqual(REPORT["thresholds"], stripped["thresholds"])

    def test_counts(self):
        report = {"files": {"a.cs": {"mutants": [mutant("Killed", baseline.FROM_BASELINE),
                                                 mutant("Survived", baseline.FROM_BASELINE),
                                                 mutant("Killed", "Mutant changed compared to target commit"),
                                                 mutant("Ignored", "Removed by since filter")]}}}
        c = baseline.counts(report)
        self.assertEqual((1, 2, 1), (c["tested in this run"], c["from baseline"], c["ignored"]))

    def test_store_takes_the_latest_report(self):
        with tempfile.TemporaryDirectory() as d:
            for run, files in (("2026-10-01.10-00-00", {}), ("2026-10-02.10-00-00", REPORT["files"])):
                os.makedirs(os.path.join(d, "out", run, "reports"))
                with open(os.path.join(d, "out", run, "reports", "mutation-report.json"), "w") as f:
                    json.dump(dict(REPORT, files=files), f)
            env = dict(os.environ, GITHUB_OUTPUT=os.path.join(d, "gh-output"))
            p = subprocess.run(["python3", os.path.join(HERE, "baseline.py"), "store", os.path.join(d, "out"),
                                os.path.join(d, "baseline")], env=env, capture_output=True, text=True)
            self.assertEqual(0, p.returncode, p.stdout + p.stderr)
            with open(os.path.join(d, "baseline", "stryker-report.json")) as f:
                self.assertEqual(["mesh_ops/MeshRoundingRepair.cs"], list(json.load(f)["files"]))
            with open(env["GITHUB_OUTPUT"]) as f:
                self.assertEqual("stored=true\n", f.read())

    def test_store_without_a_report_stores_nothing(self):
        with tempfile.TemporaryDirectory() as d:
            env = dict(os.environ, GITHUB_OUTPUT=os.path.join(d, "gh-output"))
            p = subprocess.run(["python3", os.path.join(HERE, "baseline.py"), "store", os.path.join(d, "none"),
                                os.path.join(d, "baseline")], env=env, capture_output=True, text=True)
            self.assertEqual(0, p.returncode)
            self.assertFalse(os.path.exists(os.path.join(d, "baseline")))
            with open(env["GITHUB_OUTPUT"]) as f:
                self.assertEqual("stored=false\n", f.read())


class RunStryker(unittest.TestCase):
    """run_stryker.sh with a fake dotnet that prints its arguments"""

    def run_script(self, mode, with_baseline):
        with tempfile.TemporaryDirectory() as d:
            bindir = os.path.join(d, "bin")
            os.makedirs(bindir)
            with open(os.path.join(bindir, "dotnet"), "w") as f:
                f.write('#!/usr/bin/env bash\necho "ARGS: $*"\n')
            os.chmod(os.path.join(bindir, "dotnet"), 0o755)
            if with_baseline:
                os.makedirs(os.path.join(d, "baseline"))
                with open(os.path.join(d, "baseline", "stryker-report.json"), "w") as f:
                    f.write("{}")
            env = dict(os.environ, PATH=bindir + os.pathsep + os.environ["PATH"], MODE=mode, BASE="abc123", PR="7")
            p = subprocess.run(["bash", os.path.join(HERE, "run_stryker.sh"), "stryker-x.json"], cwd=d, env=env,
                               capture_output=True, text=True)
            copied = os.path.isfile(os.path.join(d, "StrykerOutput", "baseline", "abc123", "stryker-report.json"))
            args = [l for l in p.stdout.splitlines() if l.startswith("ARGS: ")]
            return p.returncode, args[0][len("ARGS: "):] if args else None, copied

    def test_full(self):
        self.assertEqual((0, "stryker --config-file stryker-x.json", False), self.run_script("full", True))

    def test_tests_only_reports(self):
        self.assertEqual((0, "stryker --config-file stryker-x.json --since:abc123 --break-at 0", False),
                         self.run_script("tests", False))

    def test_source_with_a_baseline(self):
        self.assertEqual((0, "stryker --config-file stryker-x.json --with-baseline:abc123 --version:pr-7", True),
                         self.run_script("source", True))

    def test_source_without_a_baseline_runs_in_full(self):
        self.assertEqual((0, "stryker --config-file stryker-x.json", False), self.run_script("source", False))

    def test_unknown_mode_fails(self):
        self.assertNotEqual(0, self.run_script("nonsense", False)[0])


if __name__ == "__main__":
    unittest.main()
