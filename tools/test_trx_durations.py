"""Unit tests of tools/trx_durations.py (python3 -m unittest discover -s tools -p 'test_trx_durations.py')."""

import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import trx_durations as t  # noqa: E402

TRX = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testId="1" testName="N.A.Fast" duration="00:00:00.5000000" outcome="Passed" />
    <UnitTestResult testId="2" testName="N.A.SlowTagged" duration="00:01:30.0000000" outcome="Passed" />
    <UnitTestResult testId="3" testName="N.A.SlowUntagged" duration="00:00:07.2500000" outcome="Passed" />
    <UnitTestResult testId="4" testName="N.B+C.Row(x: 1.5)" duration="00:02:00.0000000" outcome="Passed" />
  </Results>
  <TestDefinitions>
    <UnitTest name="N.A.Fast" id="1"><TestMethod className="N.A" name="Fast" /></UnitTest>
    <UnitTest name="N.A.SlowTagged" id="2"><TestCategory><TestCategoryItem TestCategory="Slow" /></TestCategory><TestMethod className="N.A" name="SlowTagged" /></UnitTest>
    <UnitTest name="N.A.SlowUntagged" id="3"><TestCategory><TestCategoryItem TestCategory="Other" /></TestCategory><TestMethod className="N.A" name="SlowUntagged" /></UnitTest>
    <UnitTest name="N.B+C.Row(x: 1.5)" id="4"><TestMethod className="N.B+C" name="Row" /></UnitTest>
  </TestDefinitions>
</TestRun>
"""


class Trx(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.path = os.path.join(self.dir.name, "a.trx")
        with open(self.path, "w", encoding="utf-8") as f:
            f.write(TRX)

    def tearDown(self):
        self.dir.cleanup()

    def test_read(self):
        rows = {r[0]: r for r in t.read_trx(self.path)}
        self.assertEqual(len(rows), 4)
        self.assertTrue(rows["N.A.SlowTagged"][3])
        self.assertFalse(rows["N.A.SlowUntagged"][3])
        self.assertAlmostEqual(rows["N.A.SlowUntagged"][1], 7.25)
        self.assertAlmostEqual(rows["N.A.SlowTagged"][1], 90.0)

    def test_untagged_above_threshold_are_listed(self):
        out = os.path.join(self.dir.name, "summary.md")
        self.assertEqual(t.main([self.path, "--threshold", "5", "--summary", out]), 0)
        with open(out, encoding="utf-8") as f:
            text = f.read()
        self.assertIn("without the trait Category=Slow: 2", text)       # the 7.25 s test and the untagged 120 s row

    def test_fail_above(self):
        self.assertEqual(t.main([self.path, "--fail-above", "60"]), 1)    # the 120 s row has no trait
        self.assertEqual(t.main([self.path, "--fail-above", "300"]), 0)
        self.assertEqual(t.main([self.path]), 0)

    def test_slow_list(self):
        listing = os.path.join(self.dir.name, "slow.txt")
        with open(listing, "w", encoding="utf-8") as f:
            f.write("Test run for x.dll\n\nThe following Tests are available:\n    N.B+C.Row(x: 1.5)\n    N.A.Other\n")
        out = os.path.join(self.dir.name, "summary.md")
        self.assertEqual(t.main([self.path, "--slow-list", listing, "--fail-above", "60", "--summary", out]), 0)
        with open(out, encoding="utf-8") as f:
            self.assertIn("without the trait Category=Slow: 1", f.read())     # only the 7.25 s test is left
        self.assertEqual(t.read_slow_list(listing), {"N.B+C.Row(x: 1.5)", "N.A.Other"})

    def test_method_key(self):
        self.assertEqual(t.method_key("geometry3Sharp.Tests.A.B+C.Row(x: 1.5)"), "B+C.Row")
        self.assertEqual(t.method_key("geometry3Sharp.Tests.A.Fast"), "A.Fast")

    def write(self, name, text):
        path = os.path.join(self.dir.name, name)
        with open(path, "w", encoding="utf-8") as f:
            f.write(text)
        return path

    def test_make_backlog_and_ratchet(self):
        out = os.path.join(self.dir.name, "backlog.txt")
        self.assertEqual(t.main([self.path, "--make-backlog", out, "--backlog-from", "4"]), 0)
        backlog = t.read_backlog(out)
        self.assertEqual(backlog, {"A.SlowTagged", "A.SlowUntagged", "B+C.Row"})
        self.assertEqual(t.main([self.path, "--backlog", out]), 0)         # every violation is on the list

    def test_ratchet_violations(self):
        empty = self.write("empty.txt", "# nothing\n")
        rows = t.read_trx(self.path)
        errors = t.ratchet(rows, set(), None, 5.0, 10.0)
        self.assertEqual(len(errors), 3)                                   # 90 s tagged, 120 s untagged, 7.25 s untagged
        self.assertTrue(any("7.2" in e and "without the trait Slow" in e for e in errors))
        self.assertTrue(any("> 10 s" in e for e in errors))
        self.assertEqual(t.main([self.path, "--backlog", empty]), 1)

    def test_ratchet_stale_and_growing_entries(self):
        rows = t.read_trx(self.path)
        full = {"A.SlowTagged", "A.SlowUntagged", "B+C.Row"}
        self.assertEqual(t.ratchet(rows, full, full, 5.0, 10.0), [])
        stale = t.ratchet(rows, full | {"Gone.Test"}, None, 5.0, 10.0)
        self.assertEqual(len(stale), 1)
        self.assertIn("Gone.Test", stale[0])
        grown = t.ratchet(rows, full, {"A.SlowTagged"}, 5.0, 10.0)        # base has fewer entries: two were added
        self.assertEqual(len(grown), 2)

    def test_a_fast_test_passes_without_backlog(self):
        fast = self.write("fast.trx", TRX.replace('duration="00:01:30.0000000"', 'duration="00:00:01.0000000"')
                          .replace('duration="00:00:07.2500000"', 'duration="00:00:01.0000000"')
                          .replace('duration="00:02:00.0000000"', 'duration="00:00:01.0000000"'))
        empty = self.write("empty.txt", "")
        self.assertEqual(t.main([fast, "--backlog", empty]), 0)

    def test_class_of(self):
        self.assertEqual(t.class_of("N.B+C.Row(x: 1.5)"), "N.B+C")


if __name__ == "__main__":
    unittest.main()
