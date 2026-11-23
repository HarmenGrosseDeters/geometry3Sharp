"""Unit tests of tools/master_batch.py and tools/master_bisect.py
(python3 -m unittest discover -s tools -p 'test_master_batch.py')."""

import os
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import master_batch as mb  # noqa: E402
import master_bisect as bis  # noqa: E402

HEAD = "h" * 40
OLD = "a" * 40


def run(i, sha, branch="master"):
    return {"id": i, "head_sha": sha, "head_branch": branch}


def jobs(linux):
    return [{"name": "scope", "conclusion": "success"}, {"name": "linux", "conclusion": linux}]


class Decide(unittest.TestCase):
    def decide(self, runs, linux_of, diff=lambda a, b: ["math/Vector3d.cs"], run_id=None):
        return mb.decide(HEAD, runs, lambda r: jobs(linux_of[r["id"]]), diff, run_id)

    def test_head_already_tested_skips(self):
        full, reason = self.decide([run(2, HEAD)], {2: "success"})
        self.assertFalse(full)
        self.assertIn("already tested", reason)
        # a red run tested it too: running it again would not change the head's result
        self.assertFalse(self.decide([run(2, HEAD)], {2: "failure"})[0])

    def test_library_change_since_the_last_tested_head_runs(self):
        full, reason = self.decide([run(3, HEAD), run(2, OLD)], {3: "skipped", 2: "success"})
        self.assertTrue(full)
        self.assertIn(OLD[:10], reason)

    def test_cancelled_or_skipped_runs_are_not_tested_ones(self):
        # (the newest run on the head was cancelled or was a skipped batch: the search goes on to OLD)
        for linux in ("cancelled", "skipped", None):
            full, reason = self.decide([run(3, HEAD), run(2, OLD)], {3: linux, 2: "success"})
            self.assertTrue(full, linux)

    def test_only_docs_tools_benchmarks_since_skips(self):
        full, reason = self.decide([run(2, OLD)], {2: "success"}, diff=lambda a, b: ["docs/x.md", "tools/a.py", "benchmarks/B/P.cs"])
        self.assertFalse(full)
        self.assertIn("only docs", reason)
        # one file the tests read: full
        self.assertTrue(self.decide([run(2, OLD)], {2: "success"}, diff=lambda a, b: ["docs/x.md", "docs/OPERATIONS.md"])[0])

    def test_no_tested_run_found_falls_back_to_a_full_run(self):
        full, reason = self.decide([], {})
        self.assertTrue(full)
        self.assertIn("full run", reason)
        full, _ = self.decide([run(2, OLD), run(1, OLD)], {2: "cancelled", 1: "skipped"})
        self.assertTrue(full)

    def test_a_failed_diff_falls_back_to_a_full_run(self):
        full, reason = self.decide([run(2, OLD)], {2: "success"}, diff=lambda a, b: None)
        self.assertTrue(full)
        self.assertIn("diff", reason)

    def test_an_api_error_for_one_run_goes_on(self):
        def jobs_of(r):
            if r["id"] == 3:
                raise RuntimeError("HTTP 502")
            return jobs("success")
        full, reason = mb.decide(HEAD, [run(3, HEAD), run(2, HEAD)], jobs_of, lambda a, b: [])
        self.assertFalse(full)
        self.assertIn("run 2", reason)

    def test_the_own_run_tag_runs_and_other_branches_are_ignored(self):
        full, _ = self.decide([run(9, HEAD), run(8, HEAD, branch="v1.0"), run(2, OLD)], {9: "success", 8: "success", 2: "success"},
                              run_id=9)
        self.assertTrue(full)

    def test_the_search_is_bounded(self):
        runs = [run(i, HEAD) for i in range(100, 100 - mb.MAX_RUNS - 5, -1)]
        linux = {r["id"]: "skipped" for r in runs}
        linux[runs[-1]["id"]] = "success"      # beyond the bound
        self.assertTrue(self.decide(runs, linux)[0])

    def test_tested(self):
        self.assertTrue(mb.tested(jobs("success")))
        self.assertTrue(mb.tested(jobs("failure")))
        self.assertFalse(mb.tested(jobs("cancelled")))
        self.assertFalse(mb.tested([{"name": "quick", "conclusion": "success"}]))


class Bisect(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.addCleanup(__import__("shutil").rmtree, self.dir)
        self.cwd = os.getcwd()
        os.chdir(self.dir)
        self.addCleanup(os.chdir, self.cwd)

        def g(*a):
            return subprocess.run(["git"] + list(a), check=True, capture_output=True, text=True).stdout.strip()
        self.g = g
        g("init", "-q", "-b", "master")
        g("config", "user.email", "t@example.com")
        g("config", "user.name", "t")
        g("config", "commit.gpgsign", "false")
        with open("v.txt", "w") as f:
            f.write("ok\n")
        g("add", "v.txt")
        g("commit", "-q", "-m", "base")
        self.good = g("rev-parse", "HEAD")
        # five merges; the third makes the check red; each merge brings a side commit that is red on its own (first-parent skips it)
        self.merges = []
        for i in range(5):
            g("checkout", "-q", "-b", f"pr{i}")
            with open(f"side{i}.txt", "w") as f:
                f.write("x\n")
            with open("v.txt", "w") as f:
                f.write("bad\n")
            g("add", ".")
            g("commit", "-q", "-m", f"side {i}")
            with open("v.txt", "w") as f:
                f.write("bad\n" if i >= 2 else "ok\n")
            g("commit", "-q", "-am", f"fix {i}", "--allow-empty")
            g("checkout", "-q", "master")
            g("merge", "-q", "--no-ff", f"pr{i}", "-m", f"Merge pull request #{i}")
            self.merges.append(g("rev-parse", "HEAD"))
        self.bad = self.merges[-1]

    def test_merges_lists_the_first_parent_commits(self):
        found = bis.merges(self.good, self.bad)
        self.assertEqual(self.merges, [sha for sha, _ in found])
        self.assertEqual("Merge pull request #0", found[0][1])

    def test_bisect_finds_the_first_bad_merge(self):
        sha, subject = bis.bisect(self.good, self.bad, "grep -q ok v.txt")
        self.assertEqual(self.merges[2], sha)
        self.assertEqual("Merge pull request #2", subject)
        # the bisect is reset
        self.assertEqual(self.bad, self.g("rev-parse", "HEAD"))

    def test_a_dirty_tree_is_refused(self):
        with open("v.txt", "a") as f:
            f.write("dirty\n")
        with self.assertRaises(SystemExit):
            bis.bisect(self.good, self.bad, "true")

    def test_the_check_command(self):
        self.assertIn("--filter 'FullyQualifiedName~.FooTests.'", bis.check_command("FullyQualifiedName~.FooTests.", None))
        self.assertEqual("make", bis.check_command("x", "make"))
        with self.assertRaises(SystemExit):
            bis.check_command(None, None)


if __name__ == "__main__":
    unittest.main()
