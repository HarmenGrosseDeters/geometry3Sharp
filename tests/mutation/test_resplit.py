#!/usr/bin/env python3
"""Tests of resplit.py. Run by the changes job of the mutation workflow."""
import json
import os
import shutil
import tempfile
import unittest

import resplit

SOURCE = """namespace N
{
    public class C
    {
        int a;

        public void First()
        {
            a = 1;
        }

        // the second one
        [Obsolete]
        public static int Second(int x)
        {
            return x + 1;
        }

        void Third() { a = 3; }

        void Third(int x) { a = x; }
    }
}
"""

TABLE = """    static readonly Dictionary<string, string[]> SplitMethod = new Dictionary<string, string[]>() {
        ["mesh_ops/C.cs"] = new[] { "public static int Second(", "void Third() {" },
        ["mesh_ops/D.cs"] = new[] { "void X(" },
    };
"""


def config(span):
    return json.dumps({"stryker-config": {"mutate": ["**/mesh_ops/C.cs{%s}" % span]}})


class Resplit(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.root = os.path.join(self.dir, "repo")
        os.makedirs(os.path.join(self.root, "mesh_ops"))
        with open(os.path.join(self.root, "mesh_ops", "C.cs"), "w", newline="") as f:
            f.write(SOURCE)
        self.second = SOURCE.index("        // the second one")
        self.signature = SOURCE.index("        public static int Second")
        self.third = SOURCE.index("        void Third() {")
        self.cfg = os.path.join(self.dir, "cfg")
        os.mkdir(self.cfg)

    def tearDown(self):
        shutil.rmtree(self.dir, ignore_errors=True)

    def write(self, name, span):
        with open(os.path.join(self.cfg, name), "w", newline="") as f:
            f.write(config(span))

    def read_span(self, name):
        with open(os.path.join(self.cfg, name)) as f:
            return json.load(f)["stryker-config"]["mutate"][0].split("{")[1].rstrip("}")

    def test_the_table_is_read(self):
        t = resplit.split_methods(TABLE)
        self.assertEqual({"mesh_ops/C.cs": ["public static int Second(", "void Third() {"], "mesh_ops/D.cs": ["void X("]}, t)

    def test_the_real_table_and_configs_agree(self):
        with open(resplit.TABLE, encoding="utf-8-sig") as f:
            rows = resplit.plan(f.read())
        self.assertGreater(len(rows), 10)
        self.assertEqual([], [r for r in rows if not r[6]])

    def test_offsets_start_at_the_comment_above_and_accept_up_to_the_signature(self):
        cuts = resplit.offsets_of(SOURCE, ["public static int Second(", "void Third() {"])
        self.assertEqual(self.second, cuts[0][0])
        self.assertIn(self.signature, cuts[0][1])
        self.assertIn(self.second, cuts[0][1])
        self.assertEqual(self.third, cuts[1][0])

    def test_a_name_that_matches_twice_or_not_at_all_is_an_error(self):
        with self.assertRaises(ValueError):
            resplit.offsets_of(SOURCE, ["void Third("])           # (two overloads)
        with self.assertRaises(ValueError):
            resplit.offsets_of(SOURCE, ["void Missing("])

    def test_the_methods_must_be_in_the_order_of_the_file(self):
        with self.assertRaises(ValueError):
            resplit.offsets_of(SOURCE, ["void Third() {", "public static int Second("])

    def plan(self):
        table = TABLE.replace('        ["mesh_ops/D.cs"] = new[] { "void X(" },\n', "")
        return resplit.plan(table, self.root, self.cfg)

    def test_stale_offsets_are_found_and_rewritten(self):
        self.write("stryker-mesh-c-1.json", "0..%d" % (self.second - 5))
        self.write("stryker-mesh-c-2.json", "%d..%d" % (self.second - 5, self.third + 7))
        self.write("stryker-mesh-c-3.json", "%d..1000000" % (self.third + 7))
        rows = self.plan()
        self.assertEqual([False, False, False], [r[6] for r in rows])
        self.assertEqual(["stryker-mesh-c-1.json", "stryker-mesh-c-2.json", "stryker-mesh-c-3.json"], resplit.apply(rows))
        self.assertEqual("0..%d" % self.second, self.read_span("stryker-mesh-c-1.json"))
        self.assertEqual("%d..%d" % (self.second, self.third), self.read_span("stryker-mesh-c-2.json"))
        self.assertEqual("%d..1000000" % self.third, self.read_span("stryker-mesh-c-3.json"))
        self.assertTrue(all(r[6] for r in self.plan()))

    def test_an_acceptable_offset_stays_untouched(self):
        # (at the signature line instead of the comment above: MutationConfigTests accepts it, and rewriting would only lose baselines)
        self.write("stryker-mesh-c-1.json", "0..%d" % self.signature)
        self.write("stryker-mesh-c-2.json", "%d..%d" % (self.signature, self.third))
        self.write("stryker-mesh-c-3.json", "%d..%d" % (self.third, len(SOURCE) + 827))
        rows = self.plan()
        self.assertTrue(all(r[6] for r in rows))
        self.assertEqual([], resplit.apply(rows))
        self.assertEqual("0..%d" % self.signature, self.read_span("stryker-mesh-c-1.json"))

    def test_the_number_of_configs_must_fit_the_splits(self):
        self.write("stryker-mesh-c-1.json", "0..%d" % self.second)
        self.write("stryker-mesh-c-2.json", "%d..1000000" % self.second)
        with self.assertRaises(ValueError):
            self.plan()

    def test_a_last_part_that_ends_before_the_end_of_the_file_is_stale(self):
        self.write("stryker-mesh-c-1.json", "0..%d" % self.second)
        self.write("stryker-mesh-c-2.json", "%d..%d" % (self.second, self.third))
        self.write("stryker-mesh-c-3.json", "%d..%d" % (self.third, len(SOURCE) - 3))
        self.assertEqual([True, True, False], [r[6] for r in self.plan()])


if __name__ == "__main__":
    unittest.main()
