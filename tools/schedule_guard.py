#!/usr/bin/env python3
"""Daily-run guard for the scheduled workflows (nightly.yml, coverage.yml, mutation.yml).

GitHub creates `schedule` runs hours late and sometimes drops them (docs/QA.md, "Scheduled runs"), so every daily workflow is
started twice: by an external dispatcher at a fixed time (workflow_dispatch) and by its cron line as the fallback. This guard
runs first in the workflow and makes sure the day is run once:

  * pull_request and every other event: run.
  * A run on another branch than the default branch: run (a feature-branch dispatch is neither skipped nor counted).
  * workflow_dispatch with `force` = true: run.
  * workflow_dispatch with other inputs (`custom`, eg mutation `shards=rounding-1`, coverage `ref=...`): run, never skipped.
  * schedule and a plain workflow_dispatch: skip when an earlier daily run of the same workflow on the default branch that is
    also plain (not custom) is queued or running, or has succeeded, today. "Succeeded" counts only if the work ran: a run
    whose guard skipped it concludes "success" too, but all its work jobs are skipped; it does not block a retry. A failed or
    cancelled run does not count either, the new one runs again.

Today is the UTC date of this run's own creation (not the wall clock of the job). "Earlier" is a smaller run id: when the
dispatch and the late cron are created at the same moment, the older one runs and the younger skips; both can never skip.
A custom run is recognised by its guard step, which the workflow names "Schedule guard [custom]" (the runs API does not
list the inputs, the jobs API lists the step names). Known gap: if the only run of the day was skipped by the guard in
favour of a run that then failed or was cancelled before it ran, nobody retries on its own; dispatch the workflow again by hand.
The guard fails open: when the API cannot be read, the run goes ahead.

Usage (in the workflow): python3 tools/schedule_guard.py --workflow nightly.yml --event "$EVENT" --run-id "$RUN_ID"
    --branch "$GITHUB_REF_NAME" --default-branch "$DEFAULT_BRANCH" [--custom true|false] [--force true|false]
    (GITHUB_TOKEN, GITHUB_REPOSITORY, GITHUB_OUTPUT, GITHUB_STEP_SUMMARY from the environment)
Writes `run=true|false` to $GITHUB_OUTPUT and the reason to the job summary.
"""
import argparse
import datetime
import json
import os
import sys
import urllib.error
import urllib.request

DAILY_EVENTS = ("schedule", "workflow_dispatch")
LIVE_STATUSES = ("queued", "in_progress", "waiting", "requested", "pending")
CUSTOM_MARK = "[custom]"
GUARD_STEP = "Schedule guard"
NOT_WORK_JOBS = ("guard", "changes")        # the guard job, and the selection job of mutation.yml, which also succeeds when skipped


def is_true(value):
    return str(value).strip().lower() == "true"


def is_custom_run(jobs):
    return any(CUSTOM_MARK in (s.get("name") or "") for j in jobs for s in j.get("steps") or [])


def work_ran(jobs):
    """True if a job other than the guard (and the selection) concluded success: a skipped run has only skipped work jobs"""
    return any(j.get("conclusion") == "success" and j.get("name") not in NOT_WORK_JOBS for j in jobs)


def decide(event, custom, force, run_id, runs, today, jobs_of, branch=None, default_branch=None):
    """Returns (run, reason). `runs` are workflow-run objects of the runs API (default branch, daily events), `today` the UTC
    date string YYYY-MM-DD, `jobs_of(run)` the job objects (with steps) of a run or None if they cannot be read."""
    if event not in DAILY_EVENTS:
        return True, f"event {event} is not a daily run"
    if branch is not None and default_branch is not None and branch != default_branch:
        return True, f"branch {branch} is not the default branch {default_branch}"
    if is_true(force):
        return True, "force requested"
    if event == "workflow_dispatch" and is_true(custom):
        return True, "dispatch with custom inputs"
    for r in runs:
        if r.get("id") == run_id or r.get("event") not in DAILY_EVENTS:
            continue
        if default_branch is not None and r.get("head_branch") not in (None, default_branch):
            continue
        if not str(r.get("created_at", "")).startswith(today):
            continue
        status, conclusion = r.get("status"), r.get("conclusion")
        live = status in LIVE_STATUSES and r.get("id", 0) < run_id
        done = status == "completed" and conclusion == "success"
        if not (live or done):
            continue
        jobs = jobs_of(r)
        if jobs is not None and is_custom_run(jobs):
            continue
        if live:
            return False, f"run {r['id']} ({r['event']}, {status}) of today is already under way"
        if jobs is not None and work_ran(jobs):
            return False, f"run {r['id']} ({r['event']}) of today already succeeded"
    return True, "no earlier daily run of today"


def api(path, token):
    url = os.environ.get("GITHUB_API_URL", "https://api.github.com") + path
    request = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json", "Authorization": f"Bearer {token}",
                                                   "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--workflow", required=True, help="workflow file name, eg nightly.yml")
    p.add_argument("--event", required=True)
    p.add_argument("--run-id", type=int, required=True)
    p.add_argument("--branch", help="branch of this run")
    p.add_argument("--default-branch", help="the default branch of the repository")
    p.add_argument("--custom", default="false")
    p.add_argument("--force", default="false")
    p.add_argument("--today", help="UTC date YYYY-MM-DD (tests; default: the creation date of this run)")
    a = p.parse_args(argv)
    repo, token = os.environ.get("GITHUB_REPOSITORY", ""), os.environ.get("GITHUB_TOKEN", "")
    needs_runs = a.event in DAILY_EVENTS and not is_true(a.force) and not (a.event == "workflow_dispatch" and is_true(a.custom))
    needs_runs = needs_runs and (a.branch is None or a.default_branch is None or a.branch == a.default_branch)
    runs, today, jobs_cache = [], a.today, {}
    if needs_runs:
        try:
            if today is None:
                today = api(f"/repos/{repo}/actions/runs/{a.run_id}", token)["created_at"][:10]
            for event in DAILY_EVENTS:      # one request per event, on the default branch: 100 PR runs cannot push a daily run out
                query = f"per_page=100&event={event}&created=%3E%3D{today}" + (f"&branch={a.default_branch}" if a.default_branch else "")
                runs += api(f"/repos/{repo}/actions/workflows/{a.workflow}/runs?{query}", token)["workflow_runs"]
        except (urllib.error.URLError, OSError, KeyError, ValueError) as e:
            print(f"::warning::schedule guard could not read the runs ({e!r}); running", file=sys.stderr)
            runs = []

    def jobs_of(run):
        if run["id"] not in jobs_cache:
            try:
                jobs_cache[run["id"]] = api(f"/repos/{repo}/actions/runs/{run['id']}/jobs?per_page=100", token)["jobs"]
            except (urllib.error.URLError, OSError, KeyError, ValueError) as e:
                print(f"::warning::schedule guard could not read the jobs of run {run['id']} ({e!r})", file=sys.stderr)
                jobs_cache[run["id"]] = None
        return jobs_cache[run["id"]]

    today = today or datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d")
    run, reason = decide(a.event, a.custom, a.force, a.run_id, runs, today, jobs_of, a.branch, a.default_branch)
    message = f"Schedule guard: {'run' if run else 'SKIP'} ({reason})"
    print(message)
    for path, line in ((os.environ.get("GITHUB_OUTPUT"), f"run={'true' if run else 'false'}"), (os.environ.get("GITHUB_STEP_SUMMARY"), message)):
        if path:
            with open(path, "a", encoding="utf-8") as f:
                f.write(line + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
