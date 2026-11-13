#!/usr/bin/env python3
"""Tests of blame_summary.py and of the blame wiring of the net48 steps in ci.yml (python3 -m unittest discover -s tools -p 'test_blame_summary.py')."""
import os
import re
import tempfile
import unittest

import blame_summary as b

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SEQUENCE = """<?xml version="1.0"?>
<TestSequence>
  <Test Name="A.Done" Source="C:\\x\\geometry3Sharp.Tests.dll" Completed="True" />
  <Test Name="B.Hangs" Source="C:\\x\\geometry3Sharp.Tests.dll" Completed="False" />
  <Test Name="C.NoAttribute" Source="C:\\x\\geometry3Sharp.Tests.dll" />
</TestSequence>"""


class Summary(unittest.TestCase):
    def write(self, directory, name, text):
        os.makedirs(os.path.join(directory, "guid"), exist_ok=True)
        path = os.path.join(directory, "guid", name)
        with open(path, "w", encoding="utf-8") as f:
            f.write(text)
        return path

    def test_names_the_tests_that_did_not_complete(self):
        with tempfile.TemporaryDirectory() as d:
            path = self.write(d, "Sequence_1.xml", SEQUENCE)
            self.assertEqual([("B.Hangs", "geometry3Sharp.Tests.dll"), ("C.NoAttribute", "geometry3Sharp.Tests.dll")], b.incomplete_tests(path))
            lines, total = b.report(d)
            self.assertEqual(2, total)
            self.assertTrue(any("B.Hangs" in l for l in lines))
            self.assertFalse(any("A.Done" in l for l in lines))

    def test_a_clean_run_reports_nothing_incomplete(self):
        with tempfile.TemporaryDirectory() as d:
            self.write(d, "Sequence_1.xml", '<TestSequence><Test Name="A" Source="x.dll" Completed="True"/></TestSequence>')
            lines, total = b.report(d)
            self.assertEqual(0, total)
            self.assertTrue(any("every started test completed" in l for l in lines))

    def test_a_missing_or_torn_file_does_not_raise(self):
        with tempfile.TemporaryDirectory() as d:
            lines, total = b.report(d)
            self.assertEqual(0, total)
            self.assertIn("No Sequence file", " ".join(lines))
            self.write(d, "Sequence_2.xml", "<TestSequence><Test Name=")        # killed while writing
            lines, total = b.report(d)
            self.assertTrue(any("not readable" in l for l in lines))

    def test_main_writes_the_summary_file(self):
        with tempfile.TemporaryDirectory() as d:
            self.write(d, "Sequence_1.xml", SEQUENCE)
            out = os.path.join(d, "summary.md")
            self.assertEqual(0, b.main([d, "--summary", out]))
            with open(out, encoding="utf-8") as f:
                self.assertIn("B.Hangs", f.read())


class Wiring(unittest.TestCase):
    """#824: netstandard-b hung until the 60-minute timeout without naming a test; the net48 steps now run under blame-hang."""

    @classmethod
    def setUpClass(cls):
        with open(os.path.join(ROOT, ".github", "workflows", "ci.yml"), encoding="utf-8") as f:
            cls.text = f.read()
        job = re.search(r"\n  netstandard:\n(.*?)(?=\n  [a-z0-9-]+:\n|\Z)", cls.text, re.S).group(1)
        cls.steps = re.split(r"\n      - ", job)

    def step(self, name_part):
        return next(s for s in self.steps if s.startswith("name: " + name_part))

    def test_both_net48_test_steps_run_under_blame_hang(self):
        for name in ("Test (net48 / netstandard2.0)", "Test the Clipper2 package (net48 / netstandard2.0)"):
            run = self.step(name)
            self.assertIn("--blame --blame-hang --blame-hang-timeout 10m --blame-hang-dump-type mini", run, name)
            self.assertIn("--results-directory test-results", run, name)       # where the artifact step and the summary look

    def test_the_summary_runs_when_the_tests_fail_or_are_cancelled(self):
        s = self.step("Blame summary")
        self.assertIn("failure() || cancelled()", s)
        self.assertIn("python tools/blame_summary.py test-results --summary", s)

    def test_the_results_with_the_sequence_files_and_dumps_are_uploaded_always(self):
        upload = next(s for s in self.steps if s.startswith("uses: actions/upload-artifact"))
        self.assertIn("if: always()", upload)
        self.assertIn("path: test-results/", upload)

    def test_procdump_for_net_framework_is_installed_without_breaking_the_job(self):
        s = self.step("Install ProcDump")
        self.assertIn("continue-on-error: true", s)
        self.assertIn("PROCDUMP_PATH", s)


if __name__ == "__main__":
    unittest.main()
