#!/usr/bin/env python3
"""Batched master CI (owner 2026-11-23, runner load; docs/qa/ci-and-gates.md "Batched master CI").

A merge to master no longer starts its own CI run. ci.yml runs on master every 2 hours (schedule) and on workflow_dispatch; the
scheduled run first asks this script whether there is anything to test:

- the last master run of ci.yml that tested its head (every full-stage job ran to success or failure: linux, netstandard,
  netstandard-b and, where the run has it, tier-rule; isolated and time-rule may also be skipped, as in a tools-only batch;
  none cancelled or timed out) is found among the recent completed runs;
- its head is master's head: nothing new, skip;
- the changes since then are Markdown only that no test reads (tools/ci_scope.py docs_only): skip;
- a red last tested run on master's head: skip too (the head did not change); a flake needs a re-run of the failed jobs or a
  dispatch;
- a batch of tool or benchmark changes only runs the full stage with the library suites skipped (`library=false`, the CLI and
  Windows benchmark checks as the diff needs them), like a tools-only PR: the tool tests, benchmark builds and the error bound
  audit of the job linux run on master's head;
- otherwise, and whenever that run cannot be found or the diff fails: the full matrix runs on master's head.

A red batch is narrowed down to one merge with tools/master_bisect.py.

Prints `full=true|false` and `reason=<text>` for $GITHUB_OUTPUT, and with a full run that follows from a diff the suites it needs
(`library=`, `g3cli=`, `ps1=`, tools/ci_scope.py); --summary <file> appends a markdown line.

Usage (in ci.yml, with GH_TOKEN set):
    python3 tools/master_batch.py --repo OWNER/NAME --head <sha> --run-id <this run's id> [--branch master] [--summary <file>]
"""

import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ci_scope  # noqa: E402

# how many completed master runs are searched for the last tested one (a day of 2-hourly runs plus dispatches)
MAX_RUNS = 30
TESTED = ("success", "failure")
# the full-stage jobs whose result a tested run must have (not cancelled, not skipped); OPTIONAL may also be skipped (a run with
# library=false); NEWER is required only in runs that have it (runs from before it was added)
REQUIRED = ("linux", "netstandard", "netstandard-b")
OPTIONAL = ("isolated", "time-rule")
NEWER = ("tier-rule",)
# the events of master runs (a fork PR from a branch named master has head_branch master too)
MASTER_EVENTS = ("schedule", "workflow_dispatch", "push")


def tested(jobs):
    """True if the run's full stage ran to a result (whatever the outcome): every REQUIRED job (and NEWER, where present) ended
    in success or failure, every OPTIONAL one in success, failure or skipped; nothing cancelled or timed out"""
    by_name = {}
    for j in jobs:
        by_name.setdefault(j.get("name"), []).append(j.get("conclusion"))
    for name in REQUIRED:
        if not by_name.get(name) or any(c not in TESTED for c in by_name[name]):
            return False
    for name in NEWER:
        if any(c not in TESTED for c in by_name.get(name, [])):
            return False
    for name in OPTIONAL:
        if any(c not in TESTED + ("skipped",) for c in by_name.get(name, [])):
            return False
    return True


def failed(jobs):
    """True if a full-stage job of the run failed (the head it tested is red)"""
    return any(j.get("name") in REQUIRED + OPTIONAL + NEWER and j.get("conclusion") == "failure" for j in jobs)


def decide(head, runs, jobs_of, diff, run_id=None, branch="master"):
    """(full, reason, suites): suites is the scope of the diff (tools/ci_scope.py) for a full run that follows from it, else None
    (then the run tests everything). runs: the completed ci.yml runs of the branch, newest first (dicts with id, head_sha, head_branch);
    jobs_of(run) -> its jobs; diff(a, b) -> the changed paths between two commits, or None if git cannot tell"""
    last, last_jobs = None, []
    for run in [r for r in runs if r.get("id") != run_id and r.get("head_branch") == branch
                and r.get("event", "push") in MASTER_EVENTS][:MAX_RUNS]:
        try:
            jobs = jobs_of(run)
            if tested(jobs):
                last, last_jobs = run, jobs
                break
        except Exception as e:  # an API error: not this run; the search goes on (and ends in a full run if nothing is found)
            print(f"jobs of run {run.get('id')}: {e}", file=sys.stderr)
    if last is None:
        return True, f"no master run that tested its head among the last {MAX_RUNS} completed runs: full run", None
    sha = last["head_sha"]
    if sha == head:
        return False, f"master's head {head[:10]} was already tested (run {last['id']})", None
    paths = diff(sha, head)
    if paths is None:
        return True, f"the diff {sha[:10]}..{head[:10]} failed: full run", None
    if not paths or ci_scope.docs_only(paths):
        return False, f"since {sha[:10]} (run {last['id']}) only Markdown that no test reads changed ({len(paths)} files)", None
    suites = ci_scope.scope(paths)
    if failed(last_jobs):
        # (re-review N2: the last tested head was red; a tools-only batch without the library suites would turn it green)
        suites = {k: True for k in suites}
        what = "the last tested head was red: everything runs"
    else:
        what = "library" if suites["library"] else "tools and benchmarks only (library suites skipped)"
    return True, f"{len(paths)} files changed since the last tested master head {sha[:10]} (run {last['id']}): {what}", suites


def gh_json(path):
    out = subprocess.run(["gh", "api", path], check=True, capture_output=True, text=True).stdout
    return json.loads(out)


def git_diff(a, b):
    p = subprocess.run(["git", "diff", "--name-only", "--no-renames", f"{a}..{b}"], capture_output=True, text=True)
    if p.returncode != 0:
        return None
    return [line for line in p.stdout.splitlines() if line.strip()]


def main(argv):
    def arg(name, default=None):
        return argv[argv.index(name) + 1] if name in argv else default

    repo, head, branch = arg("--repo"), arg("--head"), arg("--branch", "master")
    run_id = int(arg("--run-id", "0") or 0)
    try:
        runs = gh_json(f"repos/{repo}/actions/workflows/ci.yml/runs?branch={branch}&status=completed&per_page={MAX_RUNS}")["workflow_runs"]
    except Exception as e:  # (no list: the full run is the safe answer)
        print(f"listing the runs failed: {e}", file=sys.stderr)
        runs = []
    full, reason, suites = decide(head, runs, lambda r: gh_json(f"repos/{repo}/actions/runs/{r['id']}/jobs?per_page=100")["jobs"],
                          git_diff, run_id, branch)
    print("full=" + ("true" if full else "false"))
    print("reason=" + reason.replace("\n", " "))
    for k in ("library", "g3cli", "ps1"):
        if full and suites is not None:
            print(f"{k}={'true' if suites[k] else 'false'}")
    summary = arg("--summary")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(f"### Batched master CI\n{'Full run' if full else 'Skipped'}: {reason}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
