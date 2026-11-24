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

    def test_required_groups_fail_on_a_difference_a_missing_case_or_no_case(self):
        with tempfile.TemporaryDirectory() as root:
            write(root, "linux-net10-serial", ["stage1.a\t1\t1", "stage1.b\t2\t1", "stage2.c\t3\t1", "libm.sin\t1\t9"])
            write(root, "windows-net10-serial", ["stage1.a\t1\t1", "stage1.b\t2\t1", "stage2.c\t3\t1", "libm.sin\t2\t9"])
            pairs, groups, cell, differing = cm.matrix(root)
            # (libm differs, which is a measurement: not required)
            self.assertEqual([], cm.violations(pairs, groups, differing, ["stage1", "stage2"]))
            self.assertEqual(["lin=win net10: libm.sin"], cm.violations(pairs, groups, differing, ["libm"]))
            self.assertEqual(["required group stage3 has no case"], cm.violations(pairs, groups, differing, ["stage3"]))
            self.assertEqual(0, cm.main(["x", root, "--require", "stage1,stage2"]))
            self.assertEqual(1, cm.main(["x", root, "--require", "stage1,libm"]))
            self.assertEqual(2, cm.main(["x", root, "--require"]))
        with tempfile.TemporaryDirectory() as root:
            write(root, "linux-net10-serial", ["stage1.a\t1\t1", "stage2.c\t3\t1"])
            write(root, "windows-net10-serial", ["stage1.a\t9\t1"])
            pairs, groups, cell, differing = cm.matrix(root)
            # a hash that differs, and a case that is missing on one side
            self.assertEqual(["lin=win net10: stage1.a", "lin=win net10: stage2.c (missing)"], cm.violations(pairs, groups, differing, ["stage1", "stage2"]))


if __name__ == "__main__":
    unittest.main()
