#!/usr/bin/env python3
"""Daily-run guard for the scheduled workflows (nightly.yml, coverage.yml, mutation.yml).

GitHub creates `schedule` runs hours late and sometimes drops them (docs/QA.md, "Scheduled runs"), so every daily workflow is
started twice: by an external dispatcher at a fixed time (workflow_dispatch) and by its cron line as the fallback. This guard
runs first in the workflow and makes sure the day is run once:

  * pull_request and every other event: run.
  * workflow_dispatch with `force` = true: run.
  * workflow_dispatch with other inputs (`custom`, eg mutation `shards=rounding-1`, coverage `ref=...`): run, never skipped.
  * schedule and a plain workflow_dispatch: skip when an earlier run of the same workflow that is also a daily run (schedule or
    plain dispatch, not custom) is queued or running, or already succeeded, today (UTC date). An earlier failed or cancelled
    run does not count, the new one runs again.

"Earlier" is a smaller run id: when the dispatch and the late cron are created at the same moment, the older one runs and the
younger one skips; both can never skip. A run that is custom carries "[custom]" in its run-name (set by the workflow), the
runs API does not list the inputs. The guard fails open: when the API cannot be read, the run goes ahead.

Usage (in the workflow): python3 tools/schedule_guard.py --workflow nightly.yml --event "$EVENT" --run-id "$RUN_ID"
    [--custom true|false] [--force true|false]    (GITHUB_TOKEN, GITHUB_REPOSITORY, GITHUB_OUTPUT from the environment)
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


def is_true(value):
    return str(value).strip().lower() == "true"


def decide(event, custom, force, run_id, runs, today):
    """Returns (run, reason). `runs` are workflow-run objects of the runs API, `today` the UTC date string YYYY-MM-DD."""
    if event not in DAILY_EVENTS:
        return True, f"event {event} is not a daily run"
    if is_true(force):
        return True, "force requested"
    if event == "workflow_dispatch" and is_true(custom):
        return True, "dispatch with custom inputs"
    for r in runs:
        if r.get("id") == run_id or r.get("event") not in DAILY_EVENTS:
            continue
        if CUSTOM_MARK in (r.get("display_title") or "") or CUSTOM_MARK in (r.get("name") or ""):
            continue
        if not str(r.get("created_at", "")).startswith(today):
            continue
        status, conclusion = r.get("status"), r.get("conclusion")
        if status in LIVE_STATUSES and r.get("id", 0) < run_id:
            return False, f"run {r['id']} ({r['event']}, {status}) of today is already under way"
        if status == "completed" and conclusion == "success":
            return False, f"run {r['id']} ({r['event']}) of today already succeeded"
    return True, "no earlier daily run of today"


def fetch_runs(api, repo, workflow, token, today):
    url = f"{api}/repos/{repo}/actions/workflows/{workflow}/runs?per_page=100&created=%3E%3D{today}"
    request = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json", "Authorization": f"Bearer {token}",
                                                   "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)["workflow_runs"]


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--workflow", required=True, help="workflow file name, eg nightly.yml")
    p.add_argument("--event", required=True)
    p.add_argument("--run-id", type=int, required=True)
    p.add_argument("--custom", default="false")
    p.add_argument("--force", default="false")
    p.add_argument("--today", help="UTC date YYYY-MM-DD (tests)")
    a = p.parse_args(argv)
    today = a.today or datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d")
    runs = []
    needs_runs = a.event in DAILY_EVENTS and not is_true(a.force) and not (a.event == "workflow_dispatch" and is_true(a.custom))
    if needs_runs:
        try:
            runs = fetch_runs(os.environ.get("GITHUB_API_URL", "https://api.github.com"), os.environ["GITHUB_REPOSITORY"],
                              a.workflow, os.environ["GITHUB_TOKEN"], today)
        except (urllib.error.URLError, OSError, KeyError, ValueError) as e:
            print(f"::warning::schedule guard could not read the runs ({e!r}); running", file=sys.stderr)
    run, reason = decide(a.event, a.custom, a.force, a.run_id, runs, today)
    message = f"Schedule guard: {'run' if run else 'SKIP'} ({reason})"
    print(message)
    for path, line in ((os.environ.get("GITHUB_OUTPUT"), f"run={'true' if run else 'false'}"), (os.environ.get("GITHUB_STEP_SUMMARY"), message)):
        if path:
            with open(path, "a", encoding="utf-8") as f:
                f.write(line + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
