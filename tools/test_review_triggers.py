#!/usr/bin/env python3
"""Tests of review_triggers.py: the parser of the checklist's trigger lines and the matching of a diff."""
import os
import subprocess
import sys
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
        d = "diff --git a/core/A.cs b/core/A.cs\n--- a/core/A.cs\n+++ b/core/A.cs\n@@\n-return Success;\n+return 1;\n"
        self.assertEqual(names(d), ["0"])

    def test_path_glob_crosses_directories(self):
        self.assertEqual(names(diff("math/ExactFoo.cs", "x")), ["0", "2"])
        self.assertEqual(names(diff("mesh_ops/ExactMeshBoolean.cs", "x")), ["0", "2"])
        self.assertEqual(names(diff("math/Other.cs", "x")), ["0"])

    def test_prose_in_docs_does_not_match_a_pattern(self):
        self.assertEqual(names(diff("docs/X.md", "the Success path and ErrBound")), ["0"])

    def test_removed_line_pattern(self):
        d = "diff --git a/core/A.cs b/core/A.cs\n--- a/core/A.cs\n+++ b/core/A.cs\n@@\n-if (n > Cap) return;\n+return 1;\n"
        self.assertEqual(names(d), ["0", "4"])
        self.assertEqual(names(d.replace("-if (n > Cap)", "-if (n > Lim)")), ["0"])

    def test_deleted_file_matches_by_path(self):
        d = "diff --git a/math/ExactOld.cs b/math/ExactOld.cs\ndeleted file mode 100644\n--- a/math/ExactOld.cs\n+++ /dev/null\n@@\n-x\n"
        self.assertEqual(names(d), ["0", "2"])

    def test_skipped_classes_are_reported(self):
        _, skipped = r.review_classes(diff("core/A.cs", "x"), TEXT)
        self.assertEqual([c[0] for c, _ in skipped], ["1", "2", "4"])


class Parser(unittest.TestCase):
    def test_pure_rename_and_binary_have_paths(self):
        d = "diff --git a/mesh_ops/Old.cs b/math/ExactNew.cs\nsimilarity index 100%\nrename from mesh_ops/Old.cs\nrename to math/ExactNew.cs\n"
        self.assertEqual(names(d), ["0", "2"])
        d = "diff --git a/math/ExactA.bin b/math/ExactA.bin\nBinary files a/math/ExactA.bin and b/math/ExactA.bin differ\n"
        self.assertEqual(names(d), ["0", "2"])

    def test_line_starting_like_a_header_is_a_line(self):
        d = "diff --git a/a.cs b/a.cs\n--- a/a.cs\n+++ b/a.cs\n@@\n--- Cap x\n+++ Success y\n"
        paths, added, removed, _ = r.parse_diff(d)
        self.assertEqual(paths, ["a.cs"])
        self.assertEqual(added, ["++ Success y"])
        self.assertEqual(removed, ["-- Cap x"])

    def test_comment_lines_do_not_match(self):
        self.assertEqual(names(diff("core/A.cs", "// the Success path", "/// Cap Success", "# Success")), ["0"])

    def test_ladd_ignores_tests_and_benchmarks(self):
        t = "## 1. A\n<!-- trigger: ladd=\\bpublic\\b -->\n"
        self.assertEqual(names(diff("core/A.cs", "public int X;"), t), ["1"])
        self.assertEqual(names(diff("tests/A.cs", "public int X;"), t), [])
        self.assertEqual(names(diff("benchmarks/A.cs", "public int X;"), t), [])

    def test_add_looks_at_test_lines_too(self):
        self.assertEqual(names(diff("tests/A.cs", "return Success;")), ["0", "1"])
        self.assertEqual(names(diff("math/ExactX.cs", "x")), ["0", "2"])

    def test_bad_regex_names_the_class(self):
        with self.assertRaises(ValueError) as e:
            r.parse_checklist("## 1. A\n<!-- trigger: add=( -->\n")
        self.assertIn("class 1", str(e.exception))

    def test_multiple_files_and_del_with_path(self):
        d = diff("core/A.cs", "x") + diff("math/ExactB.cs", "y")
        self.assertEqual(names(d), ["0", "2"])


class RealSamples(unittest.TestCase):
    """one positive added line per real class: a trigger regex that can never match fails here, and plain test code matches few classes"""
    SAMPLES = {
        "1": "return Success;", "2": "double tol = 1e-6;", "3": "var n = v.Normalized();", "4": "if (m.IsBoundaryEdge(e)) {}",
        "5": "int i = (int)x;", "6": "int cells = nx * ny;", "7": "public bool Compute() {", "8": "var d = double.Parse(s);",
        "9": "Parallel.For(0, n, i => {});", "10": "var g = new SpatialHash();" , "10a": "var oracle = new Oracle();",
        "11": "public class Foo {", "12": "var g = new Generator();",
    }

    def test_every_class_has_a_matching_sample(self):
        text = open(r.CHECKLIST, encoding="utf-8").read()
        for n, line in self.SAMPLES.items():
            self.assertIn(n, names(diff("core/Sample.cs", line), text), n)

    def test_plain_test_code_matches_only_always(self):
        text = open(r.CHECKLIST, encoding="utf-8").read()
        self.assertEqual(names(diff("tests/geometry3Sharp.Tests/FooTests.cs", "public void Foo() { Assert.Equal(1, 2); }"), text), ["0"])


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


class Console(unittest.TestCase):
    def test_non_ascii_paths_and_invalid_bytes_do_not_stop_the_run_on_a_legacy_code_page(self):
        # (Windows: stdout and stdin use the ANSI code page, which cannot show every character of a path, and a diff of a UTF-8 file
        # holds bytes that code page does not decode)
        path = "tools/caf" + chr(0xe9) + chr(0x2260) + ".py"
        raw = ("diff --git a/%s b/%s" + '\n' + "--- a/%s" + '\n' + "+++ b/%s" + '\n' + "@@ -1 +1 @@" + '\n' + "+x = 1" + '\n') % ((path,) * 4)
        raw = raw.encode("utf-8") + b"+y = 2 # " + bytes([0x81, 0x8d]) + '\n'.encode()
        env = dict(os.environ, PYTHONIOENCODING="ascii", PYTHONUTF8="0")
        p = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "review_triggers.py"), "-"], input=raw, capture_output=True, env=env)
        self.assertEqual(0, p.returncode, p.stderr)
        self.assertIn(path, p.stdout.decode("utf-8"))


if __name__ == "__main__":
    unittest.main()
