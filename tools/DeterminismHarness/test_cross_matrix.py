# Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
# python3 -m unittest discover -s tools/DeterminismHarness -p 'test_cross_matrix.py'
import os
import tempfile
import unittest

import cross_matrix as cm


def write(root, name, lines):
    d = os.path.join(root, name)
    os.makedirs(d)
    with open(os.path.join(d, "summary.txt"), "w", encoding="utf-8") as f:
        f.write("# runtime\tx\n" + "".join(l + "\n" for l in lines))


class CrossMatrixTests(unittest.TestCase):
    def test_groups(self):
        self.assertEqual("examples", cm.group("examples.Remesh.Run"))
        self.assertEqual("boolean", cm.group("boolean.union.exact"))
        self.assertEqual("*.libm-input", cm.group("boolean.union.libm-input"))
        self.assertEqual("reducer", cm.group("reducer"))

    def test_matrix_counts_per_group_and_pair(self):
        with tempfile.TemporaryDirectory() as root:
            write(root, "linux-net10-serial", ["libm.sin\t1\t9", "libm.cos\t2\t9", "boolean.a\t7\t5", "probe.hash-seeded\t1\t1",
                                               "examples.X.Run\tERROR\tboom", "reducer\t5\t5"])
            write(root, "windows-net10-serial", ["libm.sin\t1\t9", "libm.cos\t3\t9", "boolean.a\t7\t5", "probe.hash-seeded\t2\t1",
                                                 "examples.X.Run\tERROR\tboom"])
            write(root, "linux-net10-parallel", ["libm.sin\t1\t9"])       # (its partner of the first pair exists: listed)
            pairs, groups, cell, differing = cm.matrix(root)
            self.assertEqual(["lin: par=ser", "lin=win net10"], [n for n, _, _ in pairs])
            self.assertEqual(["boolean", "examples", "libm", "reducer"], groups)
            # equal; one of two differs; an error on both sides is not "the same"; missing on one side; the by-design case is not counted
            self.assertEqual((1, 1), cell[("boolean", "lin=win net10")])
            self.assertEqual((1, 2), cell[("libm", "lin=win net10")])
            self.assertEqual((0, 1), cell[("examples", "lin=win net10")])
            self.assertEqual((0, 1), cell[("reducer", "lin=win net10")])
            self.assertNotIn(("probe", "lin=win net10"), cell)
            self.assertEqual(["examples.X.Run (failed)", "libm.cos", "reducer (missing)"], differing["lin=win net10"])
            text = cm.render(pairs, groups, cell, differing)
            self.assertIn("| libm | **1/2** | **1/2** |", text)
            self.assertIn("| boolean | **0/1** | 1/1 |", text)
            self.assertIn("lin=win net10 (linux-net10-serial vs windows-net10-serial): 3 differ: examples.X.Run (failed), libm.cos, reducer (missing)", text)

    def test_a_pair_without_both_directories_is_left_out(self):
        with tempfile.TemporaryDirectory() as root:
            write(root, "linux-net10-serial", ["libm.sin\t1\t9"])
            pairs, groups, cell, differing = cm.matrix(root)
            self.assertEqual([], pairs)
            self.assertEqual("| group |  |\n|---|\n", cm.render(pairs, groups, cell, differing))

    GATE = [("g1", "linux-net10-serial", "windows-net10-serial"), ("g2", "linux-net10-serial", "linux-net10-parallel")]
    EXPECTED = {"stage1": 2, "stage2": 1}

    def run_main(self, root, require, gate=None, expected=None):
        return cm.main(["x", root] + (["--require", require] if require is not None else []),
                       gate_pairs=self.GATE if gate is None else gate, expected=self.EXPECTED if expected is None else expected)

    def both(self, root, lines_a, lines_b, par=None):
        write(root, "linux-net10-serial", lines_a)
        write(root, "windows-net10-serial", lines_b)
        write(root, "linux-net10-parallel", par if par is not None else lines_a)

    def test_gate_pairs_are_the_comparisons_of_the_workflow(self):
        names = [n for n, _, _ in cm.GATE_PAIRS]
        self.assertEqual(len(names), len(set(names)))
        dirs = {d for _, a, b in cm.GATE_PAIRS for d in (a, b)}
        for d in ("linux-net10-cpu2", "linux-net10-cpu8", "windows-net48-parallel", "windows-net48-parallel2", "windows-net10-parallel",
                  "windows-net10-jit-notiered", "linux-arm64-net10-parallel", "windows-arm64-net10-cpu3", "windows-arm64-net48-parallel"):
            self.assertIn(d, dirs)
        self.assertFalse(any("macos" in d for d in dirs))

    def test_required_groups_pass_when_the_same_everywhere(self):
        with tempfile.TemporaryDirectory() as root:
            ok = ["stage1.a\t1\t1", "stage1.b\t2\t1", "stage2.c\t3\t1", "libm.sin\t1\t9"]
            self.both(root, ok, ["stage1.a\t1\t1", "stage1.b\t2\t1", "stage2.c\t3\t1", "libm.sin\t2\t9"])   # (libm differs: measurement)
            self.assertEqual(0, self.run_main(root, "stage1,stage2"))
            self.assertEqual(1, self.run_main(root, "stage1,libm", expected={}))

    def test_required_groups_fail_on_a_difference_a_failed_or_a_missing_case(self):
        for bad in (["stage1.a\t9\t1", "stage1.b\t2\t1", "stage2.c\t3\t1"],           # a hash differs
                    ["stage1.a\tERROR\tx", "stage1.b\t2\t1", "stage2.c\t3\t1"],       # a failed case (also in a, so not "the same")
                    ["stage1.b\t2\t1", "stage2.c\t3\t1"]):                             # a case missing on one side
            with tempfile.TemporaryDirectory() as root:
                self.both(root, ["stage1.a\t1\t1", "stage1.b\t2\t1", "stage2.c\t3\t1"], bad)
                self.assertEqual(1, self.run_main(root, "stage1,stage2"), bad)

    def test_a_required_pair_without_its_traces_fails(self):
        with tempfile.TemporaryDirectory() as root:
            ok = ["stage1.a\t1\t1", "stage1.b\t2\t1", "stage2.c\t3\t1"]
            write(root, "linux-net10-serial", ok)
            write(root, "windows-net10-serial", ok)           # (the parallel directory of the second pair is not there)
            pairs, groups, cell, differing = cm.matrix(root, self.GATE)
            self.assertEqual(["pair g2 (linux-net10-serial vs linux-net10-parallel): a trace directory is missing"],
                             cm.violations(pairs, groups, cell, differing, ["stage1", "stage2"], self.EXPECTED, self.GATE))
            self.assertEqual(1, self.run_main(root, "stage1,stage2"))

    def test_a_case_that_vanishes_everywhere_fails(self):
        with tempfile.TemporaryDirectory() as root:
            gone = ["stage1.a\t1\t1", "stage2.c\t3\t1"]      # stage1.b is in no configuration
            self.both(root, gone, gone)
            self.assertEqual(1, self.run_main(root, "stage1,stage2"))
            pairs, groups, cell, differing = cm.matrix(root, self.GATE)
            self.assertEqual(["g1: group stage1 has 1 cases, expected 2", "g2: group stage1 has 1 cases, expected 2"],
                             cm.violations(pairs, groups, cell, differing, ["stage1", "stage2"], self.EXPECTED, self.GATE))
            # and a case that is added without its count being updated
            more = ["stage1.a\t1\t1", "stage1.b\t2\t1", "stage1.x\t2\t1", "stage2.c\t3\t1"]
            with tempfile.TemporaryDirectory() as root2:
                self.both(root2, more, more)
                self.assertEqual(1, self.run_main(root2, "stage1,stage2"))

    def test_a_required_group_with_no_case_fails(self):
        with tempfile.TemporaryDirectory() as root:
            ok = ["stage1.a\t1\t1", "stage1.b\t2\t1", "stage2.c\t3\t1"]
            self.both(root, ok, ok)
            self.assertEqual(1, self.run_main(root, "stage3", expected={}))

    def test_an_empty_require_list_is_refused(self):
        with tempfile.TemporaryDirectory() as root:
            ok = ["stage1.a\t1\t1"]
            self.both(root, ok, ok)
            for arg in ("", ",", ",,"):
                self.assertEqual(2, self.run_main(root, arg))
            self.assertEqual(2, cm.main(["x", root, "--require"]))


if __name__ == "__main__":
    unittest.main()
