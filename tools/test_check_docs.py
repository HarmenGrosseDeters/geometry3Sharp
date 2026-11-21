"""Unit tests of tools/check_docs.py (python3 -m unittest discover -s tools -p 'test_check_docs.py')."""

import os
import sys
import tempfile
import unittest

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
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "README.md", "# Title\n>>>>>>> origin/master\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("conflict marker" in e for e in errors), errors)

    def test_detects_broken_relative_link(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "README.md", "# Title\nSee [missing](docs/does-not-exist.md).\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("broken link" in e for e in errors), errors)

    def test_detects_broken_anchor_in_checked_doc(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "README.md", "# Title\nSee [target](docs/OPERATIONS.md#no-such-heading).\n")
            write_file(td, "docs/OPERATIONS.md", "# Operations\n## Real Heading\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("broken anchor" in e for e in errors), errors)

    def test_accepts_valid_anchor_with_underscores_and_code(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "tools/g3cli/README.md", "# g3cli\n[sub](docs/sub.md#cell_volumes--tpms)\n")
            write_file(td, "tools/g3cli/docs/sub.md", "# Sub\n## `cell_volumes` & `tpms`\n")
            _, errors = cd.run_checks(td)
            self.assertEqual(errors, [])

    def test_detects_oversized_table_row_in_user_doc(self):
        with tempfile.TemporaryDirectory() as td:
            long_row = "| col | " + ("x" * 510) + " |\n"
            write_file(td, "docs/guides/sample.md", "# Sample\n\n| A | B |\n|---|---|\n" + long_row)
            write_file(td, "docs/OPERATIONS.md", "# Ops\n[sample](guides/sample.md)\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("table row is" in e for e in errors), errors)

    def test_detects_missing_index_entry(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "docs/qa/README.md", "# QA\n")
            write_file(td, "docs/qa/new-topic.md", "# New Topic\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("missing index entry for 'new-topic.md'" in e for e in errors), errors)

    def test_detects_disallowed_docs_root_file(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "docs/RANDOM_NEW_DOC.md", "# Random\n")
            _, errors = cd.run_checks(td)
            self.assertTrue(any("unexpected file in docs/ root" in e for e in errors), errors)

    def test_ignores_links_inside_fenced_code_blocks(self):
        with tempfile.TemporaryDirectory() as td:
            write_file(td, "README.md", "# Title\n```markdown\n[example](does/not/exist.md)\n```\n")
            _, errors = cd.run_checks(td)
            self.assertEqual(errors, [])

    def test_repo_passes_all_checks(self):
        md_files, errors = cd.run_checks(ROOT)
        self.assertGreater(len(md_files), 100)
        self.assertEqual(errors, [])


if __name__ == "__main__":
    unittest.main()
