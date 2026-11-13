#!/usr/bin/env python3
"""Tests of schedule_guard.py and of its wiring in the three scheduled workflows. Run by ci.yml (CI scripts step)."""
import os
import re
import unittest

import schedule_guard as g

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TODAY = "2026-11-13"


def run(id, event="schedule", status="completed", conclusion="success", day=TODAY, title="Nightly"):
    return {"id": id, "event": event, "status": status, "conclusion": conclusion, "created_at": day + "T09:57:10Z", "display_title": title, "name": title}


def decide(event="schedule", runs=(), run_id=100, custom="false", force="false"):
    return g.decide(event, custom, force, run_id, list(runs), TODAY)[0]


class Decide(unittest.TestCase):
    def test_no_earlier_run_runs(self):
        self.assertTrue(decide())
        self.assertTrue(decide("workflow_dispatch"))

    def test_succeeded_run_of_today_skips_cron_and_plain_dispatch(self):
        self.assertFalse(decide("schedule", [run(90, "workflow_dispatch")]))
        self.assertFalse(decide("workflow_dispatch", [run(90, "schedule")]))

    def test_running_older_run_skips_but_running_younger_does_not(self):
        # the dispatch and the late cron created together: the older runs, the younger skips, never both skip
        self.assertFalse(decide(runs=[run(90, status="in_progress", conclusion=None)]))
        self.assertFalse(decide(runs=[run(90, status="queued", conclusion=None)]))
        self.assertTrue(decide(runs=[run(110, status="in_progress", conclusion=None)]))

    def test_failed_or_cancelled_run_does_not_count(self):
        self.assertTrue(decide(runs=[run(90, conclusion="failure")]))
        self.assertTrue(decide(runs=[run(90, conclusion="cancelled")]))

    def test_yesterday_does_not_count(self):
        self.assertTrue(decide(runs=[run(90, day="2026-11-12")]))

    def test_other_events_and_the_run_itself_do_not_count(self):
        self.assertTrue(decide(runs=[run(90, "pull_request")]))
        self.assertTrue(decide(runs=[run(100)]))

    def test_custom_runs_do_not_count(self):
        # a dispatch of one shard is not the day's full run
        self.assertTrue(decide(runs=[run(90, "workflow_dispatch", title="Mutation tests [custom]")]))

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
    """The guard is only worth something if every scheduled workflow calls it and gates its jobs on it."""

    def workflow(self, name):
        with open(os.path.join(ROOT, ".github", "workflows", name), encoding="utf-8") as f:
            return f.read()

    def test_every_scheduled_workflow_is_guarded(self):
        for name, first_jobs in (("nightly.yml", ["nightly"]), ("coverage.yml", ["core", "gate"]), ("mutation.yml", ["changes"])):
            text = self.workflow(name)
            self.assertIn("python3 tools/schedule_guard.py --workflow " + name, text, name)
            self.assertRegex(text, r"\n  guard:\n", name)
            self.assertIn("cron:", text, name + ": the cron line stays as the fallback")
            self.assertIn("workflow_dispatch:", text, name)
            self.assertRegex(text, r"force:\n\s+description:", name + ": force input")
            if name != "nightly.yml":  # it has no input but force
                self.assertIn("[custom]", text, name + ": run-name marks custom dispatches")
            for job in first_jobs:
                block = re.search(r"\n  %s:\n(.*?)(?=\n  [a-z0-9-]+:\n|\Z)" % job, text, re.S).group(1)
                self.assertIn("needs.guard.outputs.run == 'true'", block.split("steps:")[0], f"{name}/{job}")

    def test_guard_job_needs_only_read_access_to_actions(self):
        for name in ("nightly.yml", "coverage.yml", "mutation.yml"):
            block = re.search(r"\n  guard:\n(.*?)(?=\n  [a-z0-9-]+:\n|\Z)", self.workflow(name), re.S).group(1)
            self.assertIn("actions: read", block, name)


if __name__ == "__main__":
    unittest.main()
