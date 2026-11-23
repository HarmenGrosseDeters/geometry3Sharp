#!/usr/bin/env python3
"""Batched master CI (owner 2026-11-23, runner load; docs/qa/ci-and-gates.md "Batched master CI").

A merge to master no longer starts its own CI run. ci.yml runs on master every 2 hours (schedule) and on workflow_dispatch; the
scheduled run first asks this script whether there is anything to test:

- the last master run of ci.yml that tested the library (its job `linux` ran to success or failure, not cancelled or skipped)
  is found among the recent completed runs;
- its head is master's head: nothing new, skip;
- the changes since then do not touch what the library tests read (tools/ci_scope.py: docs, benchmarks and tools only): skip;
- otherwise, and whenever that run cannot be found or the diff fails: the full matrix runs on master's head.

A red batch is narrowed down to one merge with tools/master_bisect.py.

Prints `full=true|false` and `reason=<text>` for $GITHUB_OUTPUT; --summary <file> appends a markdown line.

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


def tested(jobs):
    """True if the run's job `linux` ran to a result (the full matrix ran, whatever the outcome)"""
    return any(j.get("name") == "linux" and j.get("conclusion") in TESTED for j in jobs)


def decide(head, runs, jobs_of, diff, run_id=None, branch="master"):
    """(full, reason). runs: the completed ci.yml runs of the branch, newest first (dicts with id, head_sha, head_branch);
    jobs_of(run) -> its jobs; diff(a, b) -> the changed paths between two commits, or None if git cannot tell"""
    last = None
    for run in [r for r in runs if r.get("id") != run_id and r.get("head_branch") == branch][:MAX_RUNS]:
        try:
            if tested(jobs_of(run)):
                last = run
                break
        except Exception as e:  # an API error: not this run; the search goes on (and ends in a full run if nothing is found)
            print(f"jobs of run {run.get('id')}: {e}", file=sys.stderr)
    if last is None:
        return True, f"no master run that tested the library among the last {MAX_RUNS} completed runs: full run"
    sha = last["head_sha"]
    if sha == head:
        return False, f"master's head {head[:10]} was already tested (run {last['id']})"
    paths = diff(sha, head)
    if paths is None:
        return True, f"the diff {sha[:10]}..{head[:10]} failed: full run"
    if not ci_scope.scope(paths)["library"]:
        return False, f"since {sha[:10]} (run {last['id']}) only docs, benchmarks or tools that the tests do not read changed ({len(paths)} files)"
    return True, f"{len(paths)} files changed since the last tested master head {sha[:10]} (run {last['id']})"


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
    full, reason = decide(head, runs, lambda r: gh_json(f"repos/{repo}/actions/runs/{r['id']}/jobs?per_page=100")["jobs"],
                          git_diff, run_id, branch)
    print("full=" + ("true" if full else "false"))
    print("reason=" + reason.replace("\n", " "))
    summary = arg("--summary")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(f"### Batched master CI\n{'Full run' if full else 'Skipped'}: {reason}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
