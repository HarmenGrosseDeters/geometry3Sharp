"""Unit tests of tools/check_docs.py (python3 -m unittest discover -s tools -p 'test_check_docs.py')."""

import io
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import check_docs as cd  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def write_file(base, rel, content):
    path = os.path.join(base, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(content)


class CheckDocsUnitTests(unittest.TestCase):
    def test_detects_conflict_markers(self):
        for marker in ("<<<<<<< HEAD\n", ">>>>>>> origin/master\n", "||||||| base\n", "<<<<<<<\n"):
            with tempfile.TemporaryDirectory() as td:
                write_file(td, "README.md", "# Title\n" + marker)
                _, errors = cd.run_checks(td)
                self.assertTrue(any("conflict marker" in e for e in errors), (marker, errors))

    def test_detects_broken_relative_link_and_titled_link(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(
                td,
                "README.md",
                '# Title\nSee [missing](docs/does-not-exist.md) and [titled](docs/nope.md "Title").\n',
            )
            _, errors = cd.run_checks(td)
            self.assertEqual(len([e for e in errors if "broken link" in e]), 2, errors)

    def test_accepts_titled_link_with_valid_anchor(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "README.md", '# Title\nSee [ok](docs/OPERATIONS.md#real-heading "Ops").\n')
            write_file(td, "docs/OPERATIONS.md", "# Operations\n## Real Heading ##\n")
            _, errors = cd.run_checks(td)
            self.assertEqual(errors, [])

    def test_rejects_absolute_and_escaping_paths(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "README.md", "# Title\n[abs](/tmp) and [esc](../../../../etc/hosts).\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("absolute filesystem link" in e for e in errors), errors)
            self.assertTrue(any("escapes repository root" in e for e in errors), errors)

    def test_detects_broken_anchor_and_handles_duplicate_and_unicode_headings(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(
                td,
                "README.md",
                "# Title\n"
                "[dup0](docs/OPERATIONS.md#section)\n"
                "[dup1](docs/OPERATIONS.md#section-1)\n"
                "[uni](docs/OPERATIONS.md#φ-bound)\n"
                "[bad](docs/OPERATIONS.md#section-2)\n",
            )
            write_file(td, "docs/OPERATIONS.md", "# Ops\n## Section\n## Section\n## Φ Bound\n")
            _, errors = cd.run_checks(td)
            self.assertEqual(len(errors), 1, errors)
            self.assertIn("broken anchor", errors[0])

    def test_accepts_valid_anchor_with_underscores_and_code(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "tools/README.md", "# Tools\n[g3cli](g3cli/README.md)\n")
            write_file(td, "tools/g3cli/README.md", "# g3cli\n[sub](docs/sub.md#cell_volumes--tpms)\n")
            write_file(td, "tools/g3cli/docs/sub.md", "# Sub\n## `cell_volumes` & `tpms`\n")
            _, errors = cd.run_checks(td)
            self.assertEqual(errors, [])

    def test_ignores_inline_code_and_indented_fenced_blocks(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(
                td,
                "README.md",
                "# Title\n"
                "Inline `[fake](missing.md)` and `` ` ``` ` `` do not break parsing.\n"
                "  ```sh\n"
                "  # fake-heading\n"
                "  | " + ("x" * 520) + " |\n"
                "  [also-ignored](missing2.md)\n"
                "  ```\n"
                "Real [broken](still-missing.md).\n",
            )
            _, errors = cd.run_checks(td)
            self.assertEqual(len(errors), 1, errors)
            self.assertIn("still-missing.md", errors[0])

    def test_table_row_length_boundary(self):
        with tempfile.TemporaryDirectory() as td:
            row_500 = "| " + ("a" * 496) + " |"
            row_501 = "| " + ("a" * 497) + " |"
            self.assertEqual(len(row_500), 500)
            self.assertEqual(len(row_501), 501)
            write_file(td, "docs/guides/sample.md", f"# Sample\n\n| A |\n|---|\n{row_500}\n")
            write_file(td, "docs/OPERATIONS.md", "# Ops\n[sample](guides/sample.md)\n")
            _, errors = cd.run_checks(td)
            self.assertEqual(errors, [])

            write_file(td, "docs/guides/sample.md", f"# Sample\n\n| A |\n|---|\n{row_501}\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("table row is 501 chars" in e for e in errors), errors)

    def test_index_completeness_fails_closed_when_index_missing(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "docs/qa/orphan.md", "# Orphan\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("docs/qa/README.md: missing index file" in e for e in errors), errors)

    def test_index_completeness_uses_boundary_match_and_archive_walk(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "docs/qa/README.md", "# QA\n- [foo](foo-bar.md)\n")
            write_file(td, "docs/qa/foo-bar.md", "# Foo Bar\n")
            write_file(td, "docs/qa/bar.md", "# Bar\n")
            write_file(td, "docs/archive/README.md", "# Archive\n")
            write_file(td, "docs/archive/top.md", "# Top\n")
            write_file(td, "docs/archive/sub/deep.md", "# Deep\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("missing index entry for 'bar.md'" in e for e in errors), errors)
            self.assertTrue(any("missing index entry for 'top.md'" in e for e in errors), errors)
            self.assertTrue(any("missing index entry for 'sub/deep.md'" in e for e in errors), errors)

    def test_docs_root_allowlist_and_skip_dirs(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "docs/README.md", "# Hub\n")
            write_file(td, "docs/RANDOM_NEW_DOC.md", "# Random\n")
            write_file(td, "test-results/ignored.md", "[bad](missing.md)\n")
            _, errors = cd.run_checks(td)
            self.assertEqual(len(errors), 1, errors)
            self.assertIn("unexpected file in docs/ root", errors[0])

    def test_main_exit_codes(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "README.md", "# Clean\n")
            buf_out, buf_err = io.StringIO(), io.StringIO()
            with redirect_stdout(buf_out), redirect_stderr(buf_err):
                self.assertEqual(cd.main(["--root", td]), 0)
            self.assertIn("OK", buf_out.getvalue())

            write_file(td, "README.md", "# Broken\n[x](nope.md)\n")
            buf_out, buf_err = io.StringIO(), io.StringIO()
            with redirect_stdout(buf_out), redirect_stderr(buf_err):
                self.assertEqual(cd.main(["--root", td]), 1)
            self.assertIn("broken link", buf_err.getvalue())

    def test_repo_passes_all_checks(self):
        md_files, errors = cd.run_checks(ROOT)
        self.assertGreater(len(md_files), 100)
        self.assertEqual(errors, [])


if __name__ == "__main__":
    unittest.main()
