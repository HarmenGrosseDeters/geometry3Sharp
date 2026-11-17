#!/usr/bin/env python3
"""Tests of review_triggers.py: the parser of the checklist's trigger lines and the matching of a diff."""
import os
import unittest

import review_triggers as r

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TEXT = """# Review checklist
## 0. Always (every diff)
**Trigger:** every diff.
<!-- trigger: always -->
## 1. Silent success (#1)
<!-- trigger: add=\\b(Success|Converged)\\b -->
## 2. Exact
<!-- trigger: path=math/Exact* mesh_ops/ExactMesh*; add=ErrBound -->
## 4. Removed
<!-- trigger: del=\\bCap\\b -->
## 3. No trigger line
- text
"""


def diff(path, *added):
    return "diff --git a/%s b/%s\n--- a/%s\n+++ b/%s\n@@ -1 +1 @@\n-old\n%s\n" % (path, path, path, path, "\n".join("+" + a for a in added))


def names(d, text=TEXT):
    hits, _ = r.review_classes(d, text)
    return [c[0] for c, _ in hits]


class Parse(unittest.TestCase):
    def test_classes_with_trigger_lines_only(self):
        cs = r.parse_checklist(TEXT)
        self.assertEqual([c[0] for c in cs], ["0", "1", "2", "4"])
        self.assertTrue(cs[0][2])
        self.assertEqual(cs[2][3], ["math/Exact*", "mesh_ops/ExactMesh*"])
        self.assertTrue(cs[2][4].search("x ErrBound y"))

    def test_real_checklist_every_class_has_a_trigger(self):
        text = open(r.CHECKLIST, encoding="utf-8").read()
        headings = [l for l in text.split("\n") if r.HEADING.match(l)]
        self.assertEqual(len(r.parse_checklist(text)), len(headings))
        self.assertGreaterEqual(len(headings), 14)


class Match(unittest.TestCase):
    def test_always_matches_an_empty_diff(self):
        self.assertEqual(names(""), ["0"])

    def test_added_line_pattern(self):
        self.assertEqual(names(diff("core/A.cs", "return Success;")), ["0", "1"])
        self.assertEqual(names(diff("core/A.cs", "return 1;")), ["0"])

    def test_removed_line_does_not_match(self):
        d = "--- a/core/A.cs\n+++ b/core/A.cs\n@@\n-return Success;\n+return 1;\n"
        self.assertEqual(names(d), ["0"])

    def test_path_glob_crosses_directories(self):
        self.assertEqual(names(diff("math/ExactFoo.cs", "x")), ["0", "2"])
        self.assertEqual(names(diff("mesh_ops/ExactMeshBoolean.cs", "x")), ["0", "2"])
        self.assertEqual(names(diff("math/Other.cs", "x")), ["0"])

    def test_prose_in_docs_does_not_match_a_pattern(self):
        self.assertEqual(names(diff("docs/X.md", "the Success path and ErrBound")), ["0"])

    def test_removed_line_pattern(self):
        d = "--- a/core/A.cs\n+++ b/core/A.cs\n@@\n-if (n > Cap) return;\n+return 1;\n"
        self.assertEqual(names(d), ["0", "4"])
        self.assertEqual(names(d.replace("-if (n > Cap)", "-if (n > Lim)")), ["0"])

    def test_deleted_file_matches_by_path(self):
        d = "--- a/math/ExactOld.cs\n+++ /dev/null\n@@\n-x\n"
        self.assertEqual(names(d), ["0", "2"])

    def test_skipped_classes_are_reported(self):
        _, skipped = r.review_classes(diff("core/A.cs", "x"), TEXT)
        self.assertEqual([c[0] for c, _ in skipped], ["1", "2", "4"])


class RealChecklist(unittest.TestCase):
    """a few anchors of the real checklist, so that a broken trigger line fails here"""

    def hits(self, d):
        text = open(r.CHECKLIST, encoding="utf-8").read()
        return names(d, text)

    def test_exact_math_change_matches_the_float_range_class(self):
        self.assertIn("5", self.hits(diff("math/ExactFoo.cs", "x")))

    def test_parallel_loop_matches_ordering_class(self):
        self.assertIn("9", self.hits(diff("mesh_ops/A.cs", "Parallel.For(0, n, i => {});")))

    def test_reader_matches_untrusted_input(self):
        self.assertIn("8", self.hits(diff("io/FooReader.cs", "x")))


if __name__ == "__main__":
    unittest.main()
