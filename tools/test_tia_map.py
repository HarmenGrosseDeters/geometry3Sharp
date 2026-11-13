"""Tests of tools/tia_map.py: range helpers, readers, line translation and the selector on real temporary git histories."""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import tia_map as t

TRX = """<?xml version="1.0"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>
<UnitTestResult testName="Ns.AClass.M1" duration="00:00:01.500" outcome="Passed"/>
<UnitTestResult testName="Ns.AClass.M2(x: 1)" duration="00:00:02.000" outcome="Failed"/>
<UnitTestResult testName="Ns.Other.M3" duration="00:00:09.000" outcome="Passed"/></Results></TestRun>"""


def git(root, *args):
    subprocess.run(["git", "-C", str(root), "-c", "user.name=t", "-c", "user.email=t@t", *args], check=True, capture_output=True)


class Helpers(unittest.TestCase):
    def test_ranges_and_membership(self):
        self.assertEqual(t.ranges({5, 1, 2, 3, 9}), [[1, 3], [5, 5], [9, 9]])
        rs = [[1, 3], [9, 9]]
        self.assertTrue(t.in_ranges(rs, 3) and t.in_ranges(rs, 9))
        self.assertFalse(t.in_ranges(rs, 4) or t.in_ranges(rs, 10) or t.in_ranges([], 1))

    def test_trx_cost_sums_only_the_class(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d, "c.trx")
            p.write_text(TRX)
            self.assertEqual(t.trx_cost(p, "Ns.AClass"), (3.5, 2, 1))
            self.assertEqual(t.trx_cost(p, "Ns.Missing")[1], 0)

    def test_cobertura_hits_keeps_hit_library_lines_only(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d).resolve()
            (root / "core").mkdir()
            (root / "core" / "A.cs").write_text("x")
            (root / "tests").mkdir()
            (root / "tests" / "T.cs").write_text("x")
            xml = f"""<coverage><sources><source>{root}/</source></sources><packages><package name="geometry3Sharp"><classes>
            <class filename="core/A.cs"><lines><line number="3" hits="1"/><line number="4" hits="0"/><line number="7" hits="2"/></lines></class>
            <class filename="core/A.cs"><lines><line number="9" hits="0"/></lines></class>
            <class filename="tests/T.cs"><lines><line number="1" hits="1"/></lines></class>
            <class filename="gone/B.cs"><lines><line number="1" hits="1"/></lines></class>
            </classes></package></packages></coverage>"""
            Path(d, "c.xml").write_text(xml)
            self.assertEqual(t.cobertura_hits(Path(d, "c.xml"), root), {"core/A.cs": {3, 7}})


class History(unittest.TestCase):
    """base: lib file with 10 lines + one test file; a merged branch changes line 5; main moves on afterwards."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name).resolve()
        r = self.root
        git(r, "init", "-q", "-b", "main")
        (r / "core").mkdir()
        (r / "tests/geometry3Sharp.Tests").mkdir(parents=True)
        (r / "core/A.cs").write_text("".join(f"a{i}\n" for i in range(1, 11)))
        (r / "core/B.cs").write_text("b1\nb2\n")
        (r / "tests/geometry3Sharp.Tests/ATests.cs").write_text("class ATests {}\n")
        git(r, "add", "-A")
        git(r, "commit", "-qm", "base")
        git(r, "checkout", "-qb", "feature")
        (r / "core/A.cs").write_text("".join(f"a{i}\n" if i != 5 else "CHANGED\n" for i in range(1, 11)))
        git(r, "commit", "-qam", "change line 5")
        git(r, "checkout", "-q", "main")
        git(r, "merge", "--no-ff", "-qm", "merge feature", "feature")
        self.merge = subprocess.check_output(["git", "-C", str(r), "rev-parse", "HEAD"], text=True).strip()
        self.cmap = {
            "Ns.ATests": {"class": "Ns.ATests", "seconds": 1.0, "failed": 0, "files": {"core/A.cs": [[4, 6]], "core/B.cs": [[1, 2]]}},
            "Ns.NearTests": {"class": "Ns.NearTests", "seconds": 2.0, "failed": 0, "files": {"core/A.cs": [[8, 9]]}},
            "Ns.BTests": {"class": "Ns.BTests", "seconds": 4.0, "failed": 0, "files": {"core/B.cs": [[1, 2]]}},
        }

    def tearDown(self):
        self.tmp.cleanup()

    def test_line_level_selects_only_the_class_that_hits_the_line(self):
        reason, files, lines = t.select(self.root, self.merge, self.merge, self.cmap)
        self.assertIsNone(reason)
        self.assertEqual(files, {"Ns.ATests", "Ns.NearTests"})  # file level: every class covering core/A.cs
        self.assertEqual(lines, {"Ns.ATests"})

    def test_lines_are_translated_to_the_maps_commit(self):
        r = self.root
        (r / "core/A.cs").write_text("new top\n" + (r / "core/A.cs").read_text())  # all lines shift by one
        git(r, "commit", "-qam", "shift")
        head = subprocess.check_output(["git", "-C", str(r), "rev-parse", "HEAD"], text=True).strip()
        self.assertEqual(t.translate(r, self.merge, head, "core/A.cs", {5, 9}), {5: 6, 9: 10})
        # the map (made at head) records the hit at 6 (the old line 5): selected; a hit only at old 8-9 (= 9-10) is not
        cmap = {"Ns.ATests": {"seconds": 1.0, "failed": 0, "files": {"core/A.cs": [[6, 6]]}},
                "Ns.NearTests": {"seconds": 1.0, "failed": 0, "files": {"core/A.cs": [[9, 10]]}}}
        _, _, lines = t.select(self.root, self.merge, head, cmap)
        self.assertEqual(lines, {"Ns.ATests"})

    def test_changed_later_line_falls_back_to_the_whole_file(self):
        r = self.root
        (r / "core/A.cs").write_text((r / "core/A.cs").read_text().replace("CHANGED", "REWRITTEN"))
        git(r, "commit", "-qam", "rewrite")
        head = subprocess.check_output(["git", "-C", str(r), "rev-parse", "HEAD"], text=True).strip()
        self.assertEqual(t.translate(r, self.merge, head, "core/A.cs", {5}), {5: None})
        _, _, lines = t.select(self.root, self.merge, head, self.cmap)
        self.assertEqual(lines, {"Ns.ATests", "Ns.NearTests"})

    def test_deleted_file_at_head_gives_none(self):
        r = self.root
        git(r, "rm", "-q", "core/A.cs")
        git(r, "commit", "-qm", "delete")
        head = subprocess.check_output(["git", "-C", str(r), "rev-parse", "HEAD"], text=True).strip()
        self.assertIsNone(t.translate(r, self.merge, head, "core/A.cs", {5}))

    def test_unhit_changed_line_selects_all_classes_of_the_file(self):
        cmap = {"Ns.ATests": {"seconds": 1.0, "failed": 0, "files": {"core/A.cs": [[1, 2]]}},
                "Ns.NearTests": {"seconds": 1.0, "failed": 0, "files": {"core/A.cs": [[8, 9]]}}}
        _, _, lines = t.select(self.root, self.merge, self.merge, cmap)
        self.assertEqual(lines, {"Ns.ATests", "Ns.NearTests"})

    def test_file_no_class_covers_selects_nothing_by_map(self):
        cmap = {"Ns.BTests": {"seconds": 4.0, "failed": 0, "files": {"core/B.cs": [[1, 2]]}}}
        reason, files, lines = t.select(self.root, self.merge, self.merge, cmap)
        self.assertEqual((files, lines), (set(), set()))  # (no class maps the file: the map selects nothing; a new file's tests are in the changed test files)


class Rules(unittest.TestCase):
    def _merge(self, files):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        r = Path(tmp.name).resolve()
        git(r, "init", "-q", "-b", "main")
        (r / "README").write_text("x")
        git(r, "add", "-A")
        git(r, "commit", "-qm", "base")
        git(r, "checkout", "-qb", "f")
        for name, text in files.items():
            (r / name).parent.mkdir(parents=True, exist_ok=True)
            (r / name).write_text(text)
        git(r, "add", "-A")
        git(r, "commit", "-qm", "f")
        git(r, "checkout", "-q", "main")
        git(r, "merge", "--no-ff", "-qm", "m", "f")
        return r, subprocess.check_output(["git", "-C", str(r), "rev-parse", "HEAD"], text=True).strip()

    def test_docs_only_selects_nothing(self):
        r, m = self._merge({"docs/A.md": "x", "tools/x.py": "y"})
        self.assertEqual(t.select(r, m, m, {"C": {"seconds": 1.0, "files": {}}})[0], "no-code")

    def test_build_level_and_exact_code_force_full(self):
        cmap = {"C": {"seconds": 1.0, "failed": 0, "files": {}}, "D": {"seconds": 1.0, "failed": 0, "files": {}}}
        for f in ("Directory.Build.props", "geometry3Sharp.csproj", "math/ExactPredicates.cs", ".editorconfig", "mesh_ops/ExactMeshX.cs"):
            r, m = self._merge({f: "x\n"})
            reason, files, lines = t.select(r, m, m, cmap)
            self.assertEqual((reason, lines), ("force-full", {"C", "D"}), f)

    def test_changed_test_file_selects_its_classes(self):
        r, m = self._merge({"tests/geometry3Sharp.Tests/XTests.cs": "public class XTests {}\n"})
        cmap = {"Ns.XTests": {"seconds": 1.0, "failed": 0, "files": {}}, "Ns.Other": {"seconds": 1.0, "failed": 0, "files": {}}}
        self.assertEqual(t.select(r, m, m, cmap)[2], {"Ns.XTests"})


class Determinism(unittest.TestCase):
    def test_load_map_is_sorted_and_stable(self):
        with tempfile.TemporaryDirectory() as d:
            for n in ("b", "a", "c"):
                Path(d, n + ".json").write_text(json.dumps({"class": n, "seconds": 1.0, "failed": 0, "files": {}}))
            self.assertEqual(list(t.load_map(d)), ["a", "b", "c"])
            self.assertEqual(list(t.load_map(d)), list(t.load_map(d)))


if __name__ == "__main__":
    unittest.main()
