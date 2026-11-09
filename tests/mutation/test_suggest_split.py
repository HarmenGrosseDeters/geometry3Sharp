#!/usr/bin/env python3
"""Tests of suggest_split.py. Run by the changes job of the mutation workflow."""
import glob
import itertools
import json
import os
import random
import re
import subprocess
import sys
import tempfile
import unittest

import suggest_split as sp

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

SRC = """using System;
namespace N
{
    public class A
    {
        int field = Compute(3);
        public int Prop { get; set; }

        /// <summary>doc</summary>
        [Obsolete]
        [Other("x")]
        public static int first(int a)
        {
            int local = helper(a);
            if (local > 0) { return local; }
            Func<int, int> f = x => x + 1;
            return f(a);
            int inner(int q)
            {
                return q;
            }
        }

        void second(string s)    // { not a block
        {
            var t = new List<int>(3);
            foreach (var x in t) { Console.WriteLine("}{"); }
        }

        class Nested
        {
            internal bool third<T>(T v)
            {
                return v != null;
            }
        }

        public A(int z) { field = z; }

        static long fourth() => 4;
    }
}
"""


def rep(source, mutants):
    return {"files": {"/w/A.cs": {"source": source, "mutants": mutants}}}


def at(source, needle, status):
    """a mutant on the line holding `needle`"""
    for n, line in enumerate(source.split("\n"), 1):
        if needle in line:
            return {"status": status, "location": {"start": {"line": n, "column": 5}, "end": {"line": n, "column": 9}}}
    raise KeyError(needle)


class Detection(unittest.TestCase):
    def test_weights_by_status(self):
        w = sp.weights(b=10, ratio=1.5, extra=10)
        self.assertEqual({"Timeout": 25.0, "Survived": 10.0, "Killed": 3.0, "NoCoverage": 0.0}, w)
        self.assertAlmostEqual(27.0, sp.weights(b=20, ratio=1.1, extra=5)["Timeout"])

    def test_method_starts_are_members_of_class_bodies_at_their_first_attribute_line(self):
        found = sp.method_starts(SRC)
        lines = [SRC[o:SRC.index("\n", o)].strip() for o, _, _ in found]
        # at the first comment or attribute line above (not at `public static int first`), in a nested class, a
        # constructor, an expression-bodied member; not: statements, calls, local functions and lambdas of a body, the
        # field, the property
        self.assertEqual(["/// <summary>doc</summary>", "void second(string s)    // { not a block", "internal bool third<T>(T v)",
                          "public A(int z) { field = z; }", "static long fourth() => 4;"], lines)
        self.assertEqual(["/// <summary>doc</summary>", "[Obsolete]", "[Other(\"x\")]", "public static int first(int a)"],
                         [SRC[x:SRC.index("\n", x)].strip() for x in found[0][2]])
        for o, _, _ in found:
            self.assertTrue(o == 0 or SRC[o - 1] == "\n")          # at the start of a line

    def test_the_committed_splits_are_method_starts(self):
        # the tool must find every offset the repository's own configs split at (MutationConfigTests checks the same rule)
        n = 0
        for cfg in glob.glob(os.path.join(HERE, "stryker-mesh-*.json")):
            with open(cfg, encoding="utf-8") as f:
                for entry in json.load(f)["stryker-config"]["mutate"]:
                    m = re.match(r"^\*\*/(.+?)\{(\d+)\.\.(\d+)\}$", entry)
                    if not m or int(m.group(2)) == 0:
                        continue
                    with open(os.path.join(ROOT, m.group(1)), encoding="utf-8-sig", newline="") as src:
                        text = src.read()
                    self.assertIn(int(m.group(2)), [x for _, _, ok in sp.method_starts(text) for x in ok], cfg)
                    n += 1
        self.assertGreaterEqual(n, 8)


class Partition(unittest.TestCase):
    def test_partition_minimises_the_largest_part(self):
        rnd = random.Random(7)
        for _ in range(60):
            costs = [rnd.choice([0, 1, 2, 5, 9, 30]) * 1.0 for _ in range(rnd.randint(2, 9))]
            parts = rnd.randint(1, 4)

            def largest(cuts):
                b = [0] + list(cuts) + [len(costs)]
                return max(sum(costs[x:y]) for x, y in zip(b, b[1:]))
            best = min(largest(c) for k in range(0, parts) for c in itertools.combinations(range(1, len(costs)), k))
            cuts = sp.partition(costs, parts)
            self.assertLessEqual(len(cuts), parts - 1)
            self.assertEqual(sorted(cuts), list(cuts))
            self.assertAlmostEqual(best, largest(cuts), msg=(costs, parts, cuts))

    def test_partition_of_one_part_has_no_cuts(self):
        self.assertEqual([], sp.partition([3.0, 4.0], 1))
        self.assertEqual([], sp.partition([], 3))


class Suggest(unittest.TestCase):
    def report(self):
        # first: 2 timeouts, second: 1 timeout and 1 survivor, third: 3 killed, fourth: 1 survivor, 1 no coverage
        ms = [at(SRC, "int local", "Timeout"), at(SRC, "return f(a)", "Timeout"),
              at(SRC, "var t = new", "Timeout"), at(SRC, "foreach", "Survived"),
              at(SRC, "return v != null", "Killed"), at(SRC, "return v != null", "Killed"), at(SRC, "return v != null", "Killed"),
              at(SRC, "static long fourth", "Survived"), at(SRC, "static long fourth", "NoCoverage"),
              dict(at(SRC, "static long fourth", "Ignored"))]
        return rep(SRC, ms)

    def test_parts_cost_and_boundaries(self):
        r = sp.suggest(self.report(), "A.cs", parts=2, concurrency=2, b=10, ratio=1.5, extra=10, overhead=60)
        (s1, e1, f1, n1, m1), (s2, e2, f2, n2, m2) = r["parts"]
        self.assertEqual((0, e1), (0, s2))
        self.assertEqual(len(SRC), e2)
        self.assertEqual(9, n1 + n2)                            # ten mutants, the ignored one is not counted
        # weights in seconds: first 2 x 25, second 25 + 10, third 3 x 3, fourth 10: 104 in all
        self.assertAlmostEqual(104 / 2 / 60, r["testing_minutes"], places=9)
        self.assertAlmostEqual(r["testing_minutes"], (m1 - 60) + (m2 - 60), places=9)
        # the cuts at a method start: before the second method (50 | 54 s), before the third (85 | 19 s) ...: 54 s is
        # the smallest largest part
        self.assertAlmostEqual(54 / 2 / 60, max(m1, m2) - 60, places=9)
        self.assertTrue(SRC[s2:].startswith("        void second"))
        self.assertEqual("void second(string s)    // { not a block", f2)
        self.assertEqual("the start of the file", f1)

    def test_the_fewest_parts_within_the_target(self):
        rep_ = self.report()
        one = sp.suggest(rep_, "A.cs", parts=1)
        self.assertEqual(1, len(one["parts"]))
        # a target above the whole file: one part; below it: more parts (every part carries the overhead)
        self.assertEqual(1, len(sp.suggest(rep_, "A.cs", target=61.2, overhead=60)["parts"]))
        many = sp.suggest(rep_, "A.cs", target=60.6, overhead=60)
        self.assertGreater(len(many["parts"]), 1)
        self.assertLessEqual(max(p[4] for p in many["parts"]), 60.6 + 1e-9)
        self.assertEqual(sum(p[3] for p in many["parts"]), sum(p[3] for p in one["parts"]))

    def test_errors(self):
        with self.assertRaises(ValueError):
            sp.suggest(self.report(), "Other.cs", parts=2)
        with self.assertRaises(ValueError):
            sp.suggest(rep(SRC.replace("\n", "\r\n"), []), "A.cs", parts=2)
        with self.assertRaises(ValueError):
            sp.suggest(self.report(), "A.cs", parts=2, source=SRC + " ")
        with self.assertRaises(ValueError):
            sp.suggest(self.report(), "A.cs")
        self.assertEqual(2, len(sp.suggest(self.report(), "A.cs", parts=2, source=SRC)["parts"]))

    def test_command_line(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "r.json")
            with open(path, "w") as f:
                json.dump(self.report(), f)
            def cli(*args):
                p = subprocess.run([sys.executable, os.path.join(HERE, "suggest_split.py")] + list(args), capture_output=True, text=True)
                return p.returncode, p.stdout, p.stderr
            code, out, err = cli(path, "mesh_ops/A.cs", "--parts", "2")
            self.assertEqual(2, code)                           # the file is not in the report (path differs)
            self.assertIn("is not in the report", err)
            code, out, err = cli(path, "A.cs", "--parts", "2")
            self.assertEqual(0, code, err)
            self.assertIn("mutate entries", out)
            self.assertRegex(out, r'"\*\*/A\.cs\{0\.\.\d+\}"')
            self.assertRegex(out, r'"\*\*/A\.cs\{\d+\.\.1000000\}"')
            self.assertEqual(2, cli(path, "A.cs")[0])           # neither --parts nor --target-minutes: argparse error
            self.assertEqual(2, cli(path, "A.cs", "--parts", "2", "--target-minutes", "9")[0])


if __name__ == "__main__":
    unittest.main()
