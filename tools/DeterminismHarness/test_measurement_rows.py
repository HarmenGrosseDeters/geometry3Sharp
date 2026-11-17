# Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
# python3 -m unittest discover -s tools/DeterminismHarness -p 'test_measurement_rows.py'
import os
import tempfile
import unittest

import measurement_rows as mr


def write(root, name, lines):
    d = os.path.join(root, name)
    os.makedirs(d)
    with open(os.path.join(d, "summary.txt"), "w", encoding="utf-8") as f:
        f.write("# runtime\tx\n" + "".join(l + "\n" for l in lines))


class MeasurementRowsTests(unittest.TestCase):
    def test_rows_compare_the_columns(self):
        with tempfile.TemporaryDirectory() as root:
            write(root, "linux-net10-serial", ["kernel.a\t11\t3", "kernel.b\t22\t3", "libm.sin\t1\t1", "kernel.e\tERROR\tboom"])
            write(root, "windows-net48-serial", ["kernel.a\t11\t3", "kernel.b\t23\t3", "kernel.c\t5\t1", "kernel.e\tERROR\tboom"])
            cols, out = mr.rows(root, ["kernel."])
            self.assertEqual(["linux-net10-serial", "windows-net48-serial"], cols)
            got = {n: same for n, _, same in out}
            # equal; a different hash; missing in one column; an error on both sides is not "the same"
            self.assertEqual({"kernel.a": True, "kernel.b": False, "kernel.c": False, "kernel.e": False}, got)
            text = mr.render(cols, out)
            self.assertIn("| `kernel.a` | 11 (3) | 11 (3) | yes |", text)
            self.assertIn("| `kernel.c` | - | 5 (1) | **no** |", text)
            self.assertIn("4 cases, 1 the same in every column, 3 differ: kernel.b, kernel.c, kernel.e", text)
            self.assertNotIn("libm.sin", text)


if __name__ == "__main__":
    unittest.main()
