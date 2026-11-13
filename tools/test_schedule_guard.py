#!/usr/bin/env python3
"""Tests of schedule_guard.py and of its wiring in the three scheduled workflows. Run by ci.yml (CI scripts step)."""
import os
import re
import unittest

import schedule_guard as g

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TODAY = "2026-11-13"


def run(id, event="schedule", status="completed", conclusion="success", day=TODAY, branch="master"):
    return {"id": id, "event": event, "status": status, "conclusion": conclusion, "created_at": day + "T09:57:10Z", "head_branch": branch}


def jobs(work="success", custom=False):
    """the jobs of a run: the guard job (always green) and one work job; `work` is its conclusion, 'skipped' for a guard skip"""
    mark = " [custom]" if custom else ""
    return [{"name": "guard", "conclusion": "success", "steps": [{"name": "Schedule guard" + mark}]},
            {"name": "nightly", "conclusion": work, "steps": []}]


def decide(event="schedule", runs=(), run_id=100, custom="false", force="false", jobs_of=None, branch="master", default="master"):
    jobs_of = jobs_of or (lambda r: jobs())
    return g.decide(event, custom, force, run_id, list(runs), TODAY, jobs_of, branch, default)[0]


class Decide(unittest.TestCase):
    def test_no_earlier_run_runs(self):
        self.assertTrue(decide())
        self.assertTrue(decide("workflow_dispatch"))

    def test_succeeded_run_of_today_skips_cron_and_plain_dispatch(self):
        self.assertFalse(decide("schedule", [run(90, "workflow_dispatch")]))
        self.assertFalse(decide("workflow_dispatch", [run(90, "schedule")]))

    def test_running_older_run_skips_but_running_younger_does_not(self):
        self.assertFalse(decide(runs=[run(90, status="in_progress", conclusion=None)]))
        self.assertFalse(decide(runs=[run(90, status="queued", conclusion=None)]))
        self.assertTrue(decide(runs=[run(110, status="in_progress", conclusion=None)]))

    def test_same_moment_pair_from_both_sides_with_the_older_ending_cancelled(self):
        # dispatch A (90) and late cron B (100) are created together. Seen from B: A is under way, B skips. Seen from A: B is younger,
        # A runs. When A is then cancelled, B (skipped) is no reason to skip a retry: a new dispatch C (110) sees A cancelled and
        # B skipped, and runs.
        live = lambda i: run(i, status="in_progress", conclusion=None)
        self.assertFalse(decide(run_id=100, runs=[live(90)]))
        self.assertTrue(decide(run_id=90, runs=[live(100)]))
        a_cancelled, b_skipped = run(90, conclusion="cancelled"), run(100)
        self.assertTrue(decide(run_id=110, runs=[a_cancelled, b_skipped], jobs_of=lambda r: jobs("skipped") if r["id"] == 100 else jobs()))

    def test_failed_or_cancelled_run_does_not_count(self):
        self.assertTrue(decide(runs=[run(90, conclusion="failure")]))
        self.assertTrue(decide(runs=[run(90, conclusion="cancelled")]))

    def test_a_run_skipped_by_the_guard_is_green_but_does_not_count(self):
        # A (dispatch) failed, B (late cron) was skipped by the guard and concluded success: a retry must run
        a_failed, b_skipped = run(90, "workflow_dispatch", conclusion="failure"), run(100)
        self.assertTrue(decide("workflow_dispatch", [a_failed, b_skipped], run_id=110, jobs_of=lambda r: jobs("skipped")))
        self.assertFalse(decide("workflow_dispatch", [a_failed, b_skipped], run_id=110, jobs_of=lambda r: jobs("success")))

    def test_the_selection_job_of_mutation_does_not_count_as_work(self):
        only_selection = [{"name": "changes", "conclusion": "success", "steps": [{"name": "Schedule guard"}]},
                          {"name": "exact-arithmetic", "conclusion": "skipped", "steps": []}]
        self.assertTrue(decide(runs=[run(90)], jobs_of=lambda r: only_selection))
        ran = only_selection[:1] + [{"name": "exact-mesh (rounding-1)", "conclusion": "success", "steps": []}]
        self.assertFalse(decide(runs=[run(90)], jobs_of=lambda r: ran))

    def test_unreadable_jobs_fail_open_for_a_success_and_count_for_a_live_run(self):
        self.assertTrue(decide(runs=[run(90)], jobs_of=lambda r: None))
        self.assertFalse(decide(runs=[run(90, status="in_progress", conclusion=None)], jobs_of=lambda r: None))

    def test_yesterday_does_not_count(self):
        self.assertTrue(decide(runs=[run(90, day="2026-11-12")]))

    def test_other_events_and_the_run_itself_do_not_count(self):
        self.assertTrue(decide(runs=[run(90, "pull_request")]))
        self.assertTrue(decide(runs=[run(100)]))

    def test_a_run_on_another_branch_neither_counts_nor_is_skipped(self):
        self.assertTrue(decide(runs=[run(90, branch="feature")]))
        self.assertTrue(decide("workflow_dispatch", [run(90)], branch="feature"))
        self.assertTrue(decide("schedule", [run(90)], branch="feature"))

    def test_custom_runs_do_not_count(self):
        # a dispatch of one shard is not the day's full run; the jobs API shows it as a step name
        self.assertTrue(decide(runs=[run(90, "workflow_dispatch")], jobs_of=lambda r: jobs(custom=True)))
        self.assertTrue(decide(runs=[run(90, "workflow_dispatch", status="in_progress", conclusion=None)], jobs_of=lambda r: jobs(custom=True)))

    def test_custom_dispatch_is_never_skipped(self):
        self.assertTrue(decide("workflow_dispatch", [run(90)], custom="true"))
        self.assertFalse(decide("schedule", [run(90)], custom="true"))  # custom only means something for a dispatch

    def test_force_is_never_skipped(self):
        self.assertTrue(decide("workflow_dispatch", [run(90)], force="true"))
        self.assertTrue(decide("schedule", [run(90)], force="true"))

    def test_pull_request_always_runs(self):
        self.assertTrue(decide("pull_request", [run(90)]))
        self.assertTrue(decide("push", [run(90)]))


class Wiring(unittest.TestCase):
    """The guard is only worth something if every scheduled workflow calls it and gates its work on it."""

    def workflow(self, name):
        with open(os.path.join(ROOT, ".github", "workflows", name), encoding="utf-8") as f:
            return f.read()

    def block(self, text, job):
        return re.search(r"\n  %s:\n(.*?)(?=\n  [a-z0-9-]+:\n|\Z)" % job, text, re.S).group(1)

    def test_every_scheduled_workflow_calls_the_guard(self):
        for name in ("nightly.yml", "coverage.yml", "mutation.yml"):
            text = self.workflow(name)
            self.assertIn("python3 tools/schedule_guard.py --workflow " + name, text, name)
            self.assertIn("--branch \"$BRANCH\" --default-branch \"$DEFAULT_BRANCH\"", text, name)
            self.assertIn("cron:", text, name + ": the cron line stays as the fallback")
            self.assertIn("workflow_dispatch:", text, name)
            self.assertRegex(text, r"force:\n\s+description:", name + ": force input")
            self.assertNotIn("run-name:", text, name + ": custom runs are marked by the guard step name, not by a run-name")
            if name != "nightly.yml":     # it has no input but force
                self.assertIn("' [custom]'", text, name + ": the guard step is named [custom] for a dispatch with inputs")

    def test_nightly_and_coverage_gate_their_jobs_on_a_guard_job(self):
        for name, jobs in (("nightly.yml", ["nightly"]), ("coverage.yml", ["core", "gate"])):
            text = self.workflow(name)
            guard = self.block(text, "guard")
            self.assertIn("actions: read", guard, name)
            self.assertIn("contents: read", guard, name)      # a job-level permissions block replaces the workflow's
            for job in jobs:
                self.assertIn("needs.guard.outputs.run == 'true'", self.block(text, job).split("steps:")[0], f"{name}/{job}")

    def test_mutation_guard_is_a_step_of_the_selection_job_not_a_new_job(self):
        # select_shards.py takes a new job or top key of mutation.yml as a reason to run every shard in full
        text = self.workflow("mutation.yml")
        self.assertNotRegex(text, r"\n  guard:\n")
        changes = self.block(text, "changes")
        header, steps = changes.split("steps:", 1)
        self.assertIn("actions: read", header)
        self.assertIn("contents: read", header)
        self.assertRegex(steps, r"- if: github\.event_name != 'pull_request'\n\s+name: Schedule guard")
        self.assertIn("GUARD: ${{ steps.guard.outputs.run }}", steps)
        self.assertIn("echo 'arithmetic=none'", steps)         # a skipped run selects nothing: the Stryker jobs are skipped
        self.assertIn("echo 'mesh=[]'", steps)
        self.assertIn("cancel-in-progress: ${{ github.event_name == 'pull_request' }}", text)


if __name__ == "__main__":
    unittest.main()
