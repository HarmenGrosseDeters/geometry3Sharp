import contextlib
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest

import gate_check as g

HERE = os.path.dirname(os.path.abspath(__file__))
GATES = g.parse_gates("""
# comment
.github/workflows/** | workflows
tests/mutation/stryker-*.json | shard configs
tests/mutation/*.py | mutation scripts
CLAUDE.md | rules
**/CompatibilitySuppressions.xml | api breaks
tools/*.py | ci scripts
""")


def run(files, labels=(), enforce=False, count=None):
    return g.judge(GATES, files, list(labels), enforce, count)


class GlobTests(unittest.TestCase):
    def match(self, glob, path):
        return g.glob_to_regex(glob).match(path) is not None

    def test_star_stays_in_segment(self):
        self.assertTrue(self.match("tools/*.py", "tools/ci_scope.py"))
        self.assertFalse(self.match("tools/*.py", "tools/sub/x.py"))
        self.assertFalse(self.match("tools/*.py", "tools/x.pyc"))

    def test_double_star(self):
        self.assertTrue(self.match(".github/workflows/**", ".github/workflows/ci.yml"))
        self.assertTrue(self.match(".github/workflows/**", ".github/workflows/a/b.yml"))
        self.assertFalse(self.match(".github/workflows/**", ".github/workflowsx/ci.yml"))
        self.assertFalse(self.match(".github/workflows/**", ".github/other.yml"))

    def test_double_star_slash_matches_zero_directories(self):
        self.assertTrue(self.match("**/CompatibilitySuppressions.xml", "CompatibilitySuppressions.xml"))
        self.assertTrue(self.match("**/CompatibilitySuppressions.xml", "addons/x/CompatibilitySuppressions.xml"))
        self.assertFalse(self.match("**/CompatibilitySuppressions.xml", "addons/xCompatibilitySuppressions.xml"))

    def test_dot_is_literal_and_match_is_anchored(self):
        self.assertFalse(self.match("CLAUDE.md", "CLAUDEXmd"))
        self.assertFalse(self.match("CLAUDE.md", "docs/CLAUDE.md"))
        self.assertFalse(self.match("CLAUDE.md", "CLAUDE.md.bak"))
        self.assertTrue(self.match("tests/mutation/stryker-*.json", "tests/mutation/stryker-mesh-fixed.json"))

    def test_question_mark(self):
        self.assertTrue(self.match("a/?.txt", "a/b.txt"))
        self.assertFalse(self.match("a/?.txt", "a//.txt"))


class ParseTests(unittest.TestCase):
    def test_comments_and_blank_lines_are_skipped(self):
        self.assertEqual(len(GATES), 6)

    def test_malformed_lines_raise(self):
        for bad in ("no separator", "| reason only", "glob only |", "/abs | r", "../x | r", "# only comments\n"):
            with self.assertRaises(ValueError, msg=bad):
                g.parse_gates(bad)

    def test_real_list_parses_and_covers_the_required_files(self):
        with open(os.path.join(HERE, "gate_files.txt"), encoding="utf-8") as f:
            gates = g.parse_gates(f.read())
        required = [
            "tests/mutation/stryker-mesh-fixed.json", "tests/mutation/select_shards.py", ".github/workflows/ci.yml",
            ".github/workflows/gate-change.yml", "BannedSymbols.txt", "core/ExactCodeAllowList.cs", "CompatibilitySuppressions.xml",
            ".editorconfig", "tests/geometry3Sharp.Tests/SLOW_BACKLOG.txt", "DeterministicApis.txt", "tools/ci_scope.py",
            "tools/new_tests_on_base.py", "tools/trx_durations.py", "tools/changed_line_coverage.py", "docs/REVIEW_CHECKLIST.md",
            "CLAUDE.md", "tools/analyzers/G3ExactCodeAnalyzers/ExactCodeAnalyzers.cs", "tools/gate_files.txt", "tools/gate_check.py",
        ]
        for path in required:
            self.assertEqual(len(g.find_hits(gates, [path])), 1, path)

    def test_real_list_does_not_flag_ordinary_files(self):
        with open(os.path.join(HERE, "gate_files.txt"), encoding="utf-8") as f:
            gates = g.parse_gates(f.read())
        for path in ["math/ExactPoint3.cs", "tests/geometry3Sharp.Tests/FooTests.cs", "docs/ROADMAP.md", "README.md",
                     "benchmarks/Foo/Program.cs"]:
            self.assertEqual(g.find_hits(gates, [path]), [], path)


class JudgeTests(unittest.TestCase):
    def test_no_gate_file_passes(self):
        code, summary, ann = run(["math/Foo.cs", "docs/x.md"], enforce=True)
        self.assertEqual((code, ann), (0, []))
        self.assertIn("No gate file changed", summary)

    def test_observe_reports_and_passes(self):
        code, summary, ann = run(["math/Foo.cs", "CLAUDE.md", "tests/mutation/stryker-a.json"])
        self.assertEqual(code, 0)
        self.assertEqual(len(ann), 2)
        self.assertTrue(all(a.startswith("::warning::") for a in ann))
        self.assertIn("CLAUDE.md", ann[0])
        self.assertIn("stryker-a.json", ann[1])
        self.assertIn("Observe mode", summary)
        self.assertIn("`CLAUDE.md`", summary)

    def test_enforce_fails_without_label(self):
        code, summary, ann = run(["CLAUDE.md"], enforce=True)
        self.assertEqual(code, 1)
        self.assertIn("Failed", summary)
        self.assertTrue(any(a.startswith("::error::") for a in ann))

    def test_label_passes_in_enforce_mode_but_still_reports(self):
        code, summary, ann = run(["CLAUDE.md"], labels=["bugfix", "gate-change"], enforce=True)
        self.assertEqual(code, 0)
        self.assertEqual(len(ann), 1)
        self.assertIn("approved", summary)

    def test_similar_label_does_not_count(self):
        for label in ("gate-changes", "Gate-Change", "gate"):
            self.assertEqual(run(["CLAUDE.md"], labels=[label], enforce=True)[0], 1, label)

    def test_rename_out_of_a_gate_path_counts(self):
        # the REST list gives filename (new) and previous_filename (old): the old name is in the changed list
        code, _, ann = run(["docs/moved.md", "CLAUDE.md"], enforce=True)
        self.assertEqual(code, 1)
        self.assertEqual(len(ann) - 1, 1)

    def test_rename_into_a_gate_path_counts(self):
        self.assertEqual(run(["tools/ci_scope.py", "docs/old.md"], enforce=True)[0], 1)

    def test_duplicates_are_reported_once(self):
        _, _, ann = run(["CLAUDE.md", "CLAUDE.md"])
        self.assertEqual(len(ann), 1)

    def test_truncated_file_list_counts_as_a_hit(self):
        code, summary, ann = run(["math/Foo.cs"], enforce=True, count=3000)
        self.assertEqual(code, 1)
        self.assertIn("cut at 3000", summary)
        self.assertEqual(run(["math/Foo.cs"], enforce=True, count=2999)[0], 0)
        self.assertEqual(run(["math/Foo.cs"], labels=["gate-change"], enforce=True, count=3000)[0], 0)

    def test_hostile_file_names_cannot_start_a_workflow_command(self):
        text = g.escape_annotation("gate file changed: a\n::error::x\r%b:c")
        self.assertNotIn("\n", text)
        self.assertNotIn("\r", text)
        self.assertIn("%25", text)
        gates = g.parse_gates("a*b | hostile")
        _, summary, ann = g.judge(gates, ["a`\n::stop-commands::\nb"], [], False)
        self.assertEqual(len(ann), 1)
        self.assertEqual(ann[0].count("\n"), 0)
        self.assertTrue(ann[0].startswith("::warning::"))
        self.assertEqual([l for l in summary.splitlines() if l.startswith("::")], [])
        self.assertNotIn("`\n", summary)


class ListTests(unittest.TestCase):
    def test_no_glob_of_the_real_list_is_dead(self):
        import subprocess
        try:
            files = subprocess.check_output(["git", "ls-files"], cwd=os.path.join(HERE, ".."), text=True).splitlines()
        except (OSError, subprocess.CalledProcessError):
            self.skipTest("not a git checkout")
        with open(os.path.join(HERE, "gate_files.txt"), encoding="utf-8") as f:
            gates = g.parse_gates(f.read())
        dead = [glob for glob, _, rx in gates if not any(rx.match(x) for x in files)]
        self.assertEqual(dead, [])

    def test_jq_expression_of_the_workflow_emits_filename_and_previous_filename(self):
        import re
        import shutil
        import subprocess
        if shutil.which("jq") is None:
            self.skipTest("jq missing")
        with open(os.path.join(HERE, "..", ".github", "workflows", "gate-change.yml"), encoding="utf-8") as f:
            expr = re.search(r"/files\" --jq '([^']+)'", f.read()).group(1)
        rest = json.dumps([{"filename": "docs/new.md", "previous_filename": "CLAUDE.md"}, {"filename": "a\n`b.cs"}])
        out = subprocess.run(["jq", "-r", expr], input=rest, capture_output=True, text=True, check=True).stdout
        self.assertEqual(g.parse_json_lines(out), ["docs/new.md", "CLAUDE.md", "a\n`b.cs"])
        self.assertEqual(len(out.splitlines()), 3)

    def test_truncation_boundary_with_label(self):
        self.assertEqual(run(["x"], labels=["gate-change"], enforce=True, count=2999)[0], 0)
        self.assertEqual(run(["x"], enforce=True, count=2999)[0], 0)


class CleanTests(unittest.TestCase):
    def test_clean_replaces_control_characters_and_backticks(self):
        self.assertEqual(g.clean("a`b\nc\x7fd"), "a\ufffdb\ufffdc\ufffdd")
        self.assertEqual(g.clean("plain/path.cs"), "plain/path.cs")


class MainTests(unittest.TestCase):
    def run_main(self, files, labels, extra=(), env=None):
        with tempfile.TemporaryDirectory() as d:
            paths = {}
            for key, content in (("changed", files), ("labels", labels)):
                paths[key] = os.path.join(d, key + ".txt")
                with open(paths[key], "w", encoding="utf-8") as f:
                    f.write("".join(json.dumps(x) + "\n" for x in content))
            gates = os.path.join(d, "gates.txt")
            with open(gates, "w", encoding="utf-8") as f:
                f.write("CLAUDE.md | rules\n")
            summary = os.path.join(d, "summary.md")
            old = os.environ.get("GATE_ENFORCE")
            os.environ.pop("GATE_ENFORCE", None)
            if env is not None:
                os.environ["GATE_ENFORCE"] = env
            try:
                out = io.StringIO()
                with contextlib.redirect_stdout(out):
                    code = g.main(["--gates", gates, "--changed", paths["changed"], "--labels", paths["labels"],
                                   "--summary", summary, *extra])
            finally:
                os.environ.pop("GATE_ENFORCE", None)
                if old is not None:
                    os.environ["GATE_ENFORCE"] = old
            with open(summary, encoding="utf-8") as f:
                return code, out.getvalue(), f.read()

    def test_observe_is_the_default(self):
        code, out, summary = self.run_main(["CLAUDE.md"], [])
        self.assertEqual(code, 0)
        self.assertIn("::warning::gate file changed", out)
        self.assertIn("Observe mode", summary)

    def test_env_flag_enforces(self):
        self.assertEqual(self.run_main(["CLAUDE.md"], [], env="1")[0], 1)
        self.assertEqual(self.run_main(["CLAUDE.md"], [], env="0")[0], 0)
        self.assertEqual(self.run_main(["CLAUDE.md"], ["gate-change"], env="1")[0], 0)

    def test_flag_enforces(self):
        self.assertEqual(self.run_main(["CLAUDE.md"], [], extra=["--enforce"])[0], 1)

    def test_file_count_option(self):
        self.assertEqual(self.run_main(["x"], [], extra=["--enforce", "--file-count", "3000"])[0], 1)


class Console(unittest.TestCase):
    def test_a_replacement_character_in_the_summary_is_printed_on_a_legacy_code_page(self):
        with tempfile.TemporaryDirectory() as d:
            for name, text in (("gates.txt", ".github/** | workflows" + chr(10)), ("changed.txt", json.dumps(".github/a" + chr(96) + "b") + chr(10)),
                               ("labels.txt", "")):
                with open(os.path.join(d, name), "w", encoding="utf-8", newline="") as f:
                    f.write(text)
            env = dict(os.environ, PYTHONIOENCODING="ascii", PYTHONUTF8="0")
            env.pop("GATE_ENFORCE", None)
            p = subprocess.run([sys.executable, os.path.join(HERE, "gate_check.py"), "--gates", os.path.join(d, "gates.txt"),
                                "--changed", os.path.join(d, "changed.txt"), "--labels", os.path.join(d, "labels.txt")],
                               capture_output=True, env=env)
            self.assertEqual(0, p.returncode, p.stderr)
            self.assertIn(chr(0xfffd), p.stdout.decode("utf-8"))


if __name__ == "__main__":
    unittest.main()
