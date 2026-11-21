# Plan: coverage gate red on master

- **Board row:** OPEN_TOPICS.md §3 "Ready to start", row "Platform/QA/CI: coverage gate red on master"
- **Kind:** ready to start (likely stale: close or watch)
- **Tier:** S (row update); M if a new red run needs tests
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

> [!IMPORTANT]
> The board row is stale. `gh run list --workflow coverage.yml --limit 6` on 2026-11-21 05:59 (Berlin):

| Run | Result | Created (UTC) | Head |
|---|---|---|---|
| 44862838943 | success | 2026-11-21 03:46 | 976ef224 |
| 44733374616 | success | 2026-11-20 12:09 | 8e3b06bf |
| 44669978200 | success | 2026-11-20 03:46 | 1eafce1a |
| 44639928980 | success | 2026-11-19 23:42 | a41db05b |
| 44636704717 | failure | 2026-11-19 23:19 | fdb068d5 |
| 44624183721 | success | 2026-11-19 21:59 | 2cb3d90f |

The one failure (44636704717) is in job `gate`, step "Changed-line coverage" (the core shards 0-3 passed); the next
run 23 min later was green, so the uncovered lines were covered or left the comparison window (the base is the last
evaluated run, or master of 24 h ago: `coverage.yml` "Base of the comparison"). History: red 2026-11-13/14/15/17/18,
green 2026-11-16 after #959; #1089 and #1124 added tests and five reviewed allow-list entries
([platform.md](../roadmap/platform.md#open-for-the-future-2026-11-15)).

## Trigger / decision needed

None for closing the row. A new red nightly run reopens it.

## Goal

The board shows the real state of the coverage gate, and a red gate on master becomes a tracked task within a day.

## Scope

- `docs/OPEN_TOPICS.md` (row removed from §3; if wanted, a "watch" line in §6), `docs/roadmap/platform.md` status line.
- If a later run is red: tests in `tests/geometry3Sharp.Tests/**` for the reported lines.

## Non-goals

No change to `coverage.yml`, `tools/changed_line_coverage.py` or the allow list (gate files: `tools/gate_files.txt`;
loosening needs the owner's `gate-change` label).

## Interface

None.

## Look before you build

1. Library: `tools/changed_line_coverage.py`, its tests `tools/test_changed_line_coverage.py`, the `gate-result`
   artifact of each run (lists the uncovered changed lines).
2. Prior art: diff-coverage gates (diff-cover, Codecov patch status) gate per PR; this repo gates nightly on master
   against the last evaluated run, so a single red run after a merge is expected and is cleared by the next run.

## Steps (one PR, clean commits)

1. Re-run `gh run list --workflow coverage.yml --limit 10`; if the last three are green, remove the row (docs-only).
2. If red: download `gate-result` (`gh run download <id> -n gate-result`), list the files / lines, write tests that
   execute them (success-path tests check the result, not only the status), check them by deleting the line.
3. Record the state with run ids in platform.md.

## Acceptance

- Three consecutive green `coverage.yml` runs on master, ids named in the PR; or every listed uncovered line has a test
  that fails when the line is removed.

## Sources and licences

None.

## Budget and stop rule

One session, < 1 h for the docs path. If the same lines go red twice without a change in between: flakiness in the
base selection; report to the coordinator instead of adding allow-list entries.

## Risks and open questions

- A red run caused by base selection rather than missing tests (the run of 23:19 vs 23:42) would be noise; worth one
  look at `gate-result` of 44636704717 before closing.
