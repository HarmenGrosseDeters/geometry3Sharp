#!/usr/bin/env python3
"""Tests of incremental.py and run_stryker.sh. Run by the changes job of the mutation workflow."""
import json
import os
import subprocess
import sys
import tempfile
import unittest

import incremental as inc

HERE = os.path.dirname(os.path.abspath(__file__))

OLD = "class A {\n    int f(int a) { return a + 1; }\n    int g(int a) { return a - 1; }\n    int h(int a) { return a * 2; }\n}\n"
# g changed, a line inserted after h
NEW = "class A {\n    int f(int a) { return a + 1; }\n    int g(int a) { return a - 2; }\n    int h(int a) { return a * 2; }\n    int k() { return 0; }\n}\n"


def mutant(line, status, end_line=None):
    return {"id": str(line), "mutatorName": "Arithmetic mutation", "status": status,
            "location": {"start": {"line": line, "column": 5}, "end": {"line": end_line or line, "column": 9}}}


def report(source, mutants, key="/w/repo/A.cs"):
    return {"projectRoot": "/w/repo", "files": {key: {"language": "cs", "source": source, "mutants": mutants}}}


class Pure(unittest.TestCase):
    def test_parse_mutate(self):
        self.assertEqual(("mesh_ops/X.cs", (0, 52017)), inc.parse_mutate("**/mesh_ops/X.cs{0..52017}"))
        self.assertEqual(("mesh_ops/X.cs", None), inc.parse_mutate("**/mesh_ops/X.cs"))
        self.assertEqual(("math/A.cs", None), inc.parse_mutate("math/A.cs"))

    def test_changes(self):
        new_ranges, old_ranges = inc.changes(OLD, NEW)
        self.assertEqual([(2, 3), (4, 5)], new_ranges)       # g; the inserted k
        self.assertEqual([(2, 3), (4, 4)], old_ranges)       # g; an insertion before old line 4 ("}")
        self.assertEqual(([], []), inc.changes(OLD, OLD))

    def test_spans_are_the_changed_lines(self):
        new_ranges, _ = inc.changes(OLD, NEW)
        spans = inc.spans_of(NEW, new_ranges)
        self.assertEqual(["    int g(int a) { return a - 2; }\n", "    int k() { return 0; }\n"],
                         [NEW[a:b] for a, b in spans])

    def test_spans_are_cut_to_the_configs_span(self):
        new_ranges, _ = inc.changes(OLD, NEW)
        g = NEW.index("    int g")
        self.assertEqual([(g, g + 10)], inc.spans_of(NEW, new_ranges, (0, g + 10)))
        self.assertEqual([], inc.spans_of(NEW, new_ranges, (0, g)))      # (the split of a sharded file)

    def test_adjacent_changed_lines_give_one_span(self):
        new = OLD.replace("a - 1", "a - 3").replace("a * 2", "a * 3")
        new_ranges, _ = inc.changes(OLD, new)
        spans = inc.spans_of(new, new_ranges)
        self.assertEqual(1, len(spans))
        self.assertIn("a - 3", new[spans[0][0]:spans[0][1]])
        self.assertIn("a * 3", new[spans[0][0]:spans[0][1]])

    def test_deletion_runs_the_lines_around_it(self):
        new = OLD.replace("    int g(int a) { return a - 1; }\n", "")
        new_ranges, old_ranges = inc.changes(OLD, new)
        self.assertEqual([(2, 2)], new_ranges)
        spans = inc.spans_of(new, new_ranges)
        self.assertEqual("    int f(int a) { return a + 1; }\n    int h(int a) { return a * 2; }\n",
                         new[spans[0][0]:spans[0][1]])

    def test_touches(self):
        _, old_ranges = inc.changes(OLD, NEW)
        self.assertFalse(inc.touches(mutant(2, "Killed"), old_ranges))     # f
        self.assertTrue(inc.touches(mutant(3, "Killed"), old_ranges))      # g, changed
        self.assertFalse(inc.touches(mutant(4, "Killed"), old_ranges))     # h, before the insertion
        self.assertTrue(inc.touches(mutant(1, "Killed", 5), old_ranges))   # the class, around both
        self.assertTrue(inc.touches(mutant(4, "Killed", 5), old_ranges))   # spans the insertion point

    def test_score_is_strykers(self):
        # (the full rounding run of 1 November: 431 killed, 45 timeouts, 184 survived, 25 no coverage -> 69.49 %)
        d, u = inc.counted([{"status": "Killed"}] * 431 + [{"status": "Timeout"}] * 45 +
                           [{"status": "Survived"}] * 184 + [{"status": "NoCoverage"}] * 25 +
                           [{"status": "Ignored"}] * 7 + [{"status": "CompileError"}] * 3)
        self.assertEqual((476, 209), (d, u))
        self.assertAlmostEqual(69.49, inc.score(d, u), places=2)
        self.assertIsNone(inc.score(0, 0))

    def test_baseline_file_by_relative_or_absolute_key(self):
        self.assertIsNotNone(inc.baseline_file(report(OLD, []), "A.cs"))
        self.assertIsNotNone(inc.baseline_file(report(OLD, [], key="A.cs"), "A.cs"))
        self.assertIsNone(inc.baseline_file(report(OLD, [], key="/w/repo/BA.cs"), "A.cs"))


class PlanAndMerge(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.root = self.dir.name
        with open(os.path.join(self.root, "A.cs"), "w") as f:
            f.write(NEW)
        self.config = {"mutate": ["**/A.cs"], "thresholds": {"break": 50}, "since": {"target": "x"}}
        self.baseline = report(OLD, [mutant(2, "Killed"), mutant(3, "Killed"), mutant(3, "Survived"),
                                     mutant(4, "Survived"), mutant(1, "Killed", 6), mutant(2, "Ignored")])

    def tearDown(self):
        self.dir.cleanup()

    def test_plan_limits_mutate_to_the_changed_lines(self):
        mutate, files = inc.plan(self.config, self.baseline, self.root)
        self.assertEqual(2, len(mutate))
        a, b = inc.parse_mutate(mutate[0])[1]
        self.assertEqual("    int g(int a) { return a - 2; }\n", NEW[a:b])

    def test_plan_without_the_file_in_the_baseline_runs_in_full(self):
        self.assertIsNone(inc.plan(self.config, report(OLD, [], key="/w/repo/Other.cs"), self.root))

    def test_merge(self):
        run = report(NEW, [mutant(3, "Killed"), mutant(3, "Killed"), mutant(5, "Survived")])
        c = inc.merge(self.config, self.baseline, run, self.root)
        self.assertEqual((2, 1), c["run"])
        self.assertEqual((1, 1), c["baseline"])          # f (killed) and h (survived) kept
        self.assertEqual(3, c["replaced"])               # the two old g mutants and the class mutant
        self.assertEqual((1, 1), inc.merge(self.config, self.baseline, None, self.root)["baseline"])

    def cli(self, *args):
        env = dict(os.environ, REPO_ROOT=self.root, GITHUB_OUTPUT=os.path.join(self.root, "gh"))
        p = subprocess.run([sys.executable, os.path.join(HERE, "incremental.py")] + list(args), env=env,
                           capture_output=True, text=True, cwd=self.root)
        return p.returncode, p.stdout

    def write(self, name, doc):
        path = os.path.join(self.root, name)
        with open(path, "w") as f:
            json.dump(doc, f)
        return path

    def test_cli_plan_writes_the_run_config(self):
        cfg = self.write("cfg.json", {"stryker-config": self.config})
        bl = self.write("bl.json", self.baseline)
        code, out = self.cli("plan", cfg, bl, os.path.join(self.root, "run.json"))
        self.assertEqual(0, code, out)
        self.assertIn("plan=run", out)
        with open(os.path.join(self.root, "run.json")) as f:
            run = json.load(f)["stryker-config"]
        self.assertEqual(2, len(run["mutate"]))
        self.assertNotIn("since", run)
        self.assertEqual({"break": 50}, run["thresholds"])

    def test_cli_plan_none_and_full(self):
        cfg = self.write("cfg.json", {"stryker-config": self.config})
        with open(os.path.join(self.root, "A.cs"), "w") as f:
            f.write(OLD)
        self.assertIn("plan=none", self.cli("plan", cfg, self.write("bl.json", self.baseline), "run.json")[1])
        self.assertIn("plan=full", self.cli("plan", cfg, os.path.join(self.root, "missing.json"), "run.json")[1])

    def test_cli_merge_fails_below_the_break_threshold(self):
        bl = self.write("bl.json", self.baseline)
        ok = self.write("run-ok.json", report(NEW, [mutant(3, "Killed"), mutant(5, "Killed")]))
        bad = self.write("run-bad.json", report(NEW, [mutant(3, "Survived"), mutant(5, "Survived"), mutant(5, "NoCoverage")]))
        cfg = self.write("cfg.json", {"stryker-config": self.config})
        code, out = self.cli("merge", cfg, bl, ok)
        self.assertEqual(0, code, out)
        self.assertIn("75.00 %", out)                    # 3 of 4
        code, out = self.cli("merge", cfg, bl, bad)
        self.assertEqual(1, code, out)                   # 1 of 5 = 20 % < 50
        self.assertIn("below the break threshold", out)


class RunStryker(unittest.TestCase):
    """run_stryker.sh with a fake dotnet that logs its arguments and writes a report"""

    def run_script(self, mode, with_baseline, source=NEW):
        with tempfile.TemporaryDirectory() as d:
            root = os.path.join(d, "repo")
            work = os.path.join(root, "tests", "mutation")
            os.makedirs(work)
            with open(os.path.join(root, "A.cs"), "w") as f:
                f.write(source)
            with open(os.path.join(work, "stryker-x.json"), "w") as f:
                json.dump({"stryker-config": {"mutate": ["**/A.cs"], "thresholds": {"break": 0}}}, f)
            bindir = os.path.join(d, "bin")
            os.makedirs(bindir)
            run_report = json.dumps(report(NEW, [mutant(3, "Killed")]))
            with open(os.path.join(bindir, "dotnet"), "w") as f:
                f.write('#!/usr/bin/env bash\necho "ARGS: $*"\nmkdir -p StrykerOutput/2026-01-01.00-00-00/reports\n'
                        "echo '%s' > StrykerOutput/2026-01-01.00-00-00/reports/mutation-report.json\n" % run_report)
            os.chmod(os.path.join(bindir, "dotnet"), 0o755)
            if with_baseline:
                os.makedirs(os.path.join(work, "baseline"))
                with open(os.path.join(work, "baseline", "stryker-report.json"), "w") as f:
                    json.dump(report(OLD, [mutant(2, "Killed"), mutant(3, "Killed")]), f)
            env = dict(os.environ, PATH=bindir + os.pathsep + os.environ["PATH"], MODE=mode, BASE="abc123",
                       REPO_ROOT=root, GITHUB_STEP_SUMMARY=os.path.join(d, "summary"))
            env.pop("GITHUB_OUTPUT", None)
            p = subprocess.run(["bash", os.path.join(HERE, "run_stryker.sh"), "stryker-x.json"], cwd=work, env=env,
                               capture_output=True, text=True)
            args = [l[len("ARGS: "):] for l in p.stdout.splitlines() if l.startswith("ARGS: ")]
            full_marker = os.path.isfile(os.path.join(work, "StrykerOutput", "full-run"))
            return p.returncode, args, full_marker, p.stdout

    def test_full(self):
        self.assertEqual((0, ["stryker --config-file stryker-x.json"], True), self.run_script("full", True)[:3])

    def test_tests_only_reports(self):
        self.assertEqual((0, ["stryker --config-file stryker-x.json --since:abc123 --break-at 0"], False),
                         self.run_script("tests", False)[:3])

    def test_source_with_a_baseline_runs_the_changed_lines(self):
        code, args, full_marker, out = self.run_script("source", True)
        self.assertEqual((0, ["stryker --config-file incremental-run.json --break-at 0"], False),
                         (code, args, full_marker), out)
        self.assertIn("tested in this run", out)
        self.assertIn("Score of the shard: 100.00 %", out)

    def test_source_unchanged_runs_nothing(self):
        code, args, full_marker, out = self.run_script("source", True, source=OLD)
        self.assertEqual((0, [], False), (code, args, full_marker), out)
        self.assertIn("nothing to run", out)

    def test_source_without_a_baseline_runs_in_full(self):
        self.assertEqual((0, ["stryker --config-file stryker-x.json"], True), self.run_script("source", False)[:3])

    def test_unknown_mode_fails(self):
        self.assertNotEqual(0, self.run_script("nonsense", False)[0])


if __name__ == "__main__":
    unittest.main()
