# CI Workflows, Gates, Scheduled Runs & Performance Watch

Back to [QA Hub](README.md) · [docs/QA.md](../QA.md)

---

## 2.8 API compatibility

This is in place since #92: pack validates the public API against 1.0.324. Accepted breaks are listed in
`CompatibilitySuppressions.xml`, and changed results in [UPSTREAM_CHANGES.md](../UPSTREAM_CHANGES.md).

## 2.8a Gate-change check (owner 2026-11-17)

A change must not modify the checks that judge it. `.github/workflows/gate-change.yml` (`pull_request_target`, so it runs
from the base branch and never checks out or runs PR code; token `contents: read`, `pull-requests: read`) lists the PR's
changed files through the REST API (`filename` and `previous_filename`, so a rename out of a gate path counts) and the live
labels, reads the gate list `tools/gate_files.txt` from the base branch and applies it with `tools/gate_check.py` (unit tests:
`tools/test_gate_check.py`, in the `linux` job and at the start of the workflow). A PR that changes a gate file fails
unless it carries the label `gate-change` (in enforce mode, see below). The list holds globs with a reason each: the
workflows, the analyzers and their lists (`BannedSymbols.txt`, `DeterministicApis.txt`, `core/ExactCodeAllowList.cs`,
`.editorconfig`, `CompatibilitySuppressions.xml`), the time-rule inputs (`SLOW_BACKLOG.txt`, `tools/*.py`), the
mutation configs, thresholds and test filters, the golden rows and ratchet limits of the 2D mesher, the design notes
that tests check against generators, the rules (`CLAUDE.md`, `AGENTS.md`, `docs/AGENT_WORKFLOW.md`,
`docs/REVIEW_CHECKLIST.md`) and the list and script themselves. A new gate input (a threshold, allow list or baseline
file) is added to the list in the PR that introduces it.

Who sets the label (owner 2026-11-17): the coordinator, after reviewing the gate-file diff, for a change that keeps or
tightens the checks (tests added to filters or projects, a `#pragma` for the obsolete warning CS0618 in a tool or test, a new
CI test, a threshold raised), and logs it with the PR and the gate file; the owner for a change that loosens a check (a
threshold lowered, a new allow-list, `SLOW_BACKLOG.txt` or suppression entry, any other warning disabled, a job or test removed
or disabled, a trigger narrowed) or changes the gate check itself (list, script, workflow) or the rules; in doubt, the owner.
Workers never set it. A push after the coordinator's label gets a fresh review of the gate-file diff before the merge; if it
loosens a check, the label is removed and the owner decides ([AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md) section 8).

Limits of the label (a social rule, not a technical one): it approves the PR, not a diff, so it is set after the last
push (a later push keeps it, hence the fresh review above); and nothing checks who set it (branch protection and the repository's permissions do). A broken
`gate_files.txt` or a failing `test_gate_check.py` fails the job (a broken list must not pass silently; the unit-test step only
in enforce mode). A PR against another base branch is judged by that branch's workflow and list.

Observe mode is the current state: the job reports (step summary, one warning annotation per file) and passes. The owner
switches to enforce mode with the repository variable `GATE_CHANGE_ENFORCE=1`; no PR is needed. A PR with 3000 or more changed
files (the REST list is cut there) counts as a gate change. The workflow itself takes effect only once it is on master.

## 2.12 Scheduled runs: dispatch path and guard (2026-11-13)

GitHub's `schedule` event is unreliable here. Diagnosis from the Actions run lists (2026-11-11/12): the runs are **created** late,
not queued late (`created_at` = `run_started_at`; the runner wait was 3 s to 47 min): `nightly.yml` (cron 02:23 UTC) created
09:36 and 09:57 (+7 h 13 / +7 h 34), `coverage.yml` (03:41) created 11:48 (+8 h 07) and none at all for 2026-11-11,
`mutation.yml` (Monday 03:23) created 11:25 and 11:48 (+8 h). Runner priorities or other cron times do not help against that. So
`nightly.yml` and `coverage.yml` have two starters and run once a day (`mutation.yml` has the cron lines only since 2026-11-16, see below):

| Workflow | Dispatch (UTC, by the coordinator's scheduled tasks) | Inputs | Cron line (fallback, GitHub runs it late) |
|---|---|---|---|
| `nightly.yml` | 02:30 | none | `23 2 * * *` |
| `coverage.yml` | 03:45 (after the nightly) | none (a `ref` makes it a custom run) | `41 3 * * *` |
| `mutation.yml` | none since 2026-11-16 (the dispatch tasks were disabled when #958 moved the schedule into the cron lines) | `shards=all` (manual dispatches only) | `17 19 * * 1-6` (nightly group: `boolean-*`, `arrangement-*`, `rounding-*`) and `17 19 * * 0` (weekly group: the other shards), see tests/mutation/README.md "3.0 process" |

The dispatch is a `workflow_dispatch` on `master` (`actions_run_trigger run_workflow` with `ref: master` and the inputs above, or
`gh workflow run <file> --ref master -f shards=all`). The first job of `nightly.yml` and `coverage.yml`, `guard` (in `mutation.yml` a step of the `changes` job; `tools/schedule_guard.py`, tests
in `tools/test_schedule_guard.py`, run by `ci.yml`), skips the whole workflow when an earlier plain daily run of the same workflow
is queued or running today (UTC date), or already succeeded. Rules:

- Only `schedule` and a plain `workflow_dispatch` on the default branch can be skipped. Pull requests and runs on other branches never are,
  and such runs do not count for the day either.
- A dispatch with other inputs is a **custom run**, never skipped and never counted as the day's run: `mutation.yml` with `shards`
  other than `all`, `coverage.yml` with a `ref`. The guard step is then named `Schedule guard [custom]`, which the guard reads
  from the jobs API (the runs API does not list inputs).
- `force: true` (input of all three) runs although the day is taken.
- A run counts as the day's run only if its work ran: it is queued or running, or it succeeded **and** a job other than
  `guard` / `changes` concluded success. A run that the guard skipped is green too, but its work jobs are skipped, so it blocks
  no retry. A failed or cancelled run does not count: the next starter runs again.
- "Today" is the UTC date of the run's own creation. "Earlier" is the smaller run id, so when the dispatch and the late cron are
  created together exactly one of them runs. Only runs of the default branch are looked at, one request per event, so 100 PR
  runs cannot push the earlier daily run out of the page. The guard is not keyed on the master SHA: master moves during the day,
  and a late cron must not run the day a second time on a newer commit.
- A live earlier run counts only once its guard step is listed in the jobs API: a queued run has no steps, so a custom run
  (`shards=...`, `ref=...`) cannot be told from a plain one yet. Such a run does not cause a skip: a double run of the day is
  acceptable, a lost day is not.
- A `schedule` payload has no `repository`, so `github.event.repository.default_branch` is empty there; the workflows fall back to
  `master`, and the guard treats an empty default branch as unknown and never gives a schedule run a branch verdict (a schedule
  runs on the default branch only).
- It fails open: when the API cannot be read or answers anything unexpected (any exception), the run goes ahead.
- Known gap, retry by hand: when the only running starter is cancelled or fails after the other one was skipped, nothing
  retries by itself. Dispatch the workflow again (`force` is not needed: the failed or cancelled run does not count).
- In `mutation.yml` the guard is a step of the `changes` job, not a job of its own (`select_shards.py` takes a new job or top key
  of that workflow as a reason to run every shard in full), and the concurrency group cancels only pull-request runs and is per kind
  of run on master (`mutation-schedule`, `mutation-workflow_dispatch`): with a dispatched full run under way (a manual one now), a late cron starts next to it and is
  skipped by the guard (it never cancels hours of work, and never replaces a pending dispatch).
- `coverage.yml` looks for the previous `gate-result` in the last 60 master runs (a guard-skipped run has none).
- Master CI runs: `ci.yml` has one concurrency group per ref without cancel-in-progress (2026-11-15: master runs queue; GitHub keeps the
  run under way and the newest pending one, so a burst of merges skips the middle commits' runs, cancelled as pending). A regression of
  such a commit shows at a later commit: when a master run is red, bisect over the commits since the last green run.

**Why the scheduled `mutation.yml` runs ended cancelled (diagnosis 2026-11-17, run and job data of the three `schedule` runs; no current code cause):**

| Run | Created (UTC) | Result | Cause |
|---|---|---|---|
| 41436698451 (2026-11-02) | 11:25 (cron 03:23 Mon, +8 h) | 9 jobs green, 2 cancelled | job timeouts: `The job has exceeded the maximum execution time of 5h50m0s` (`timeout-minutes: 350` of `exact-mesh`) of the two then unsplit shards `rounding` and `envelope`. Both were split afterwards (8381db07, 2e1a81f1 on 11-08, then 89586db5 on 11-10 and 7cd56d64 on 11-15: now `rounding-1..13`, `envelope-1..4`; the single configs no longer exist), so this limit no longer applies as it did |
| 42611917701 (2026-11-09) | 11:48 (cron 03:23 Mon, +8 h) | 2 green, 20 cancelled at 12:37:50 to 12:38:10 | annotation `The run was canceled by @HarmenGrosseDeters.`: cancelled from a session on the owner's account (most likely a coordinator action, sessions use the owner's token); not a workflow defect |
| 44003452066 (2026-11-17) | 02:30 (cron 19:17 Mon of 11-16, +7 h 13) | 3 green, 22 cancelled at 03:51:25 to 03:51:54 | same annotation: the coordinator cancelled it with the owner's approval (a run on the older sha 2172017a, 15 commits behind; boolean-1 and boolean-2 had finished, 13 rounding jobs were waiting for runners) to free the runners for the 3.0 gate dispatch 44013112256, created 8 s later (03:51:33); an approved one-off, not a defect |

- Past defect, fixed: at 722b7f99 and aa514cb6 the concurrency group was `mutation-${{ github.ref }}` with `cancel-in-progress: true`, so
  the late cron runs cancelled the day's dispatch runs: 41395763685 at 11:26:21 on 11-02 and 42567558219 at 11:48:44 on 11-09
  (annotation `Canceling since a higher priority waiting request for mutation-refs/heads/master exists`). Fixed in 8f0c5c8e
  (a group per kind of run, cancel only for pull requests).
- Excluded as a current cause: concurrency (the scheduled run is in group `mutation-schedule`, the dispatch in
  `mutation-workflow_dispatch`, and `cancel-in-progress` is only true for pull requests, so neither cancels the other), the guard
  (`changes` ran its steps to success and the shards started: no earlier plain run existed that day, because `mutation.yml` has no
  dispatcher since 2026-11-16, see below), and a runner limit (all cancelled jobs had started or were queued; none ended with a
  runner error).
- Dispatcher status: the coordinator's tasks "Dispatch nightly mutation run" and "Dispatch Monday mutation run" were disabled on
  2026-11-16 when #958 moved the schedule into the cron lines of the workflow (the coordinator disabled them). The cron lines (GitHub
  runs them 7 to 8 h late or not at all) are the only starter of `mutation.yml` now; manual `shards=all` dispatches remain possible.
  The dispatch rows above for `nightly.yml` and `coverage.yml` still apply.
- Until 2026-11-16 the cron line was Monday only, so there was one schedule run per week; the Mon-Sat nightly lines were added on
  2026-11-16 (8f0c5c8e), so 44003452066 is the first run of the new lines and says nothing yet about the other days.
- Not built: no guard or workflow change fixes a manual cancel. (1) A policy for late cron runs (eg the guard
  skips a `schedule` run created more than N hours after its cron time) is not built: late cron runs stay as they are,
  since the cron lines are the only starter of `mutation.yml` (owner 2026-11-17). (2) A watch item, not a change: the slowest shards stay
  well under `timeout-minutes: 350` (watch any shard near 350 min).

**Measurement (2026-11-15, Actions run history, UTC; all times from `created_at`):**

| Workflow | Starter | Planned | Created | Delay |
|---|---|---|---|---|
| `nightly.yml` | cron, 11-13 | 02:23 | 09:43:42 | +7 h 20 min |
| `nightly.yml` | cron, 11-14 | 02:23 | 09:06:24 (guard skipped it: 15 s, work jobs skipped) | +6 h 43 min |
| `nightly.yml` | dispatch, 11-14 | 02:27 | 02:27:54 | +1 min |
| `nightly.yml` | dispatch, 11-15 | 02:27 | 03:27:38 | +1 h 01 min (cause not visible in the history) |
| `coverage.yml` | cron, 11-13 | 03:41 | 11:30:11 | +7 h 49 min |
| `coverage.yml` | cron, 11-14 | 03:41 | 10:53:36 (ran 15 min: the dispatch run of that day had failed, so it did not count) | +7 h 12 min |
| `coverage.yml` | dispatch, 11-14 | 03:45 | 03:46:11 | +1 min |
| `coverage.yml` | dispatch, 11-15 | 03:45 | 03:45:47 | +1 min |
| `mutation.yml` | dispatch, 11-14 | 01:20 | 01:21:18 | +1 min |

- The cron lines are still 6.7 to 7.8 hours late (no cron run of 2026-11-15 existed at 07:56 UTC, 5.5 h after the nightly's
  02:23): the diagnosis holds. The dispatch is on time to within about a minute, except one nightly dispatch an hour late.
- The guard did its job: the nightly cron run of 11-14 is green with all work jobs skipped, 15 s; the coverage cron of 11-14
  ran because the earlier run of the day had failed (as designed).
- Not on the schedule: `mutation.yml` was dispatched on 11-14 at 03:15 (green, 1 h 53 min) and 04:45 (cancelled after 25 min), and on
  11-15 at 03:27 (cancelled after 3 min); a 01:20 run exists only for 11-14 (cancelled after 35 min). Why they were cancelled is not
  visible in the history. The only complete dispatch run since the cap of 6 took 1 h 53 min, so the estimate of about 17 h
  for a full run of all 46 shards (45 mesh shards plus the arithmetic job since #1006) was not confirmed or refuted by the 11-15 data. Since 2026-11-17 one full run exists, which does not settle it either (shards run at most 6 at a time and 5 failed): master run 44013112256 (dispatched 03:51 UTC, the Exact Boolean 3.0 gate), 35 shards green, 5 red.
- `coverage.yml` ended red in every scheduled and dispatched run from 11-12 to 11-15 (the pull_request runs were green) (the changed-line gate, a separate matter); green on 11-16 (the 06:52 UTC dispatch and the 12:01 UTC cron run, after #959); red again on 11-17 (dispatch 03:47 UTC and cron 12:25 UTC, job `gate`, step "Changed-line coverage").
- Queue wait of PR CI jobs (`created_at` to `started_at`) before the runner relief (13 Nov afternoon, one mutation PR run and one master
  run in progress): `linux` 93 min, `isolated` 103 min, `netstandard-b` 67 min, `scope` 1 to 11 min. After the cap of 6, the
  cancelling master group and the draft skip (15 Nov morning, two PR runs): `linux` 11 and 16 min, `isolated` 0.3 and 4.6 min,
  `netstandard-b` 11.5 min, `scope` 0.1 and 16 min, `time-rule` 0.02 and 4.4 min. About a sixth to a tenth of the former wait, still
  not zero on a morning with several PR runs at once; two runs are a sample, not a statistic.

## 2.13 Hang diagnosis of the Windows net48 shards (#824)

`netstandard-b` (net48 shard b) hung twice on 2026-11-13 (#761, #820) until the 60-minute job timeout, without output and without a
trx, so no test was named. The net48 test steps (both shards and the Clipper2 package run) now run `dotnet test --blame
--blame-hang --blame-hang-timeout 10m --blame-hang-dump-type mini`:

- a test that runs 10 minutes (the suite's limit is 5 s, 10 s with the `Slow` tag) hangs the test host: vstest kills it and
  writes a mini dump (`*_hangdump*.dmp`) next to `Sequence_<guid>.xml` (the tests that started, `Completed="True"` once finished)
  into `test-results/`, which the `if: always()` artifact step uploads (`netstandard-results`, `netstandard-b-results`);
- no ProcDump is needed: a forced hang on `windows-latest` (a test that sleeps forever, net48, `--blame-hang-timeout 1m`, run 43482696511 of
  a probe branch, 2026-11-13) gave the same result with and without ProcDump (Chocolatey, `PROCDUMP_PATH`): the log says "The test running
  when the crash occurred: <test>", and `probe.trx`, `Sequence_<guid>.xml` (305 bytes) and `testhost.net48_<pid>_<time>_hangdump.dmp` (2 MB)
  are in the results directory. The step exits 1, so the job fails and the summary step runs. The timeout is an inactivity time (no
  test finished for that long), not a limit per test;
- the step `Blame summary` (`tools/blame_summary.py`, run when a step failed or the job was cancelled) lists the tests of the
  sequence files that did not complete in the job summary: the hang is in one of them (usually the last);
- `--blame` also names the running test when the test host crashes. The trx of a healthy run is the same as before.

## 2.14 Two-stage PR CI and batched master CI (owner 2026-11-23)

Runner load: about 9,000 runner-minutes a day on GitHub-hosted runners (public repository, about 20 parallel jobs), queues of 45
minutes and more. Mutation runs were 44 % (almost all on PRs, many on PRs that touch no exact code; their new rule is in
[mutation-and-coverage.md](mutation-and-coverage.md) 2.2), CI 27 %: every push to a ready PR ran the whole matrix, and every
merge a full master run. Self-hosted runners stay forbidden (fork PRs could run code on them).

**Quick stage** (job `quick` of `ci.yml`, every event of a ready PR while the full stage does not run): builds the library for
net10.0 and netstandard2.0 and the net10.0 test project (compile errors in either target), and runs the net10.0 tests of the
changed areas without `Slow` and `Nightly`. `tools/ci_scope.py --quick` picks them: the test classes of a changed test file; for a
changed library file the classes of the test files that name one of its own types (the types whose names the file name starts
with or that start with it, e.g. `partial class MeshRoundingRepair` in `MeshRoundingRepairIncremental.cs`); `DocExamplesTests` for
`docs/examples` and `docs/OPERATIONS.md`; and a smoke set (`SMOKE`) of fast core classes for every library change. A change too
broad to map (the test harness, a csproj or other build setting, a type that more than `MAX_CLASSES` test files use) runs the
smoke set and the classes whose names start with the changed types, and the job summary says "Broad change: smoke set only".
Tools, benchmarks and docs the tests do not read build only; the add-on and the other test projects run in the full stage only.
The quick stage is a signal; it can miss a test, the full stage cannot.

**Full stage**: `linux` (all net10.0 tests, the CLI and Clipper2 suites, pack, smoke tests, the benchmark and tool steps),
`netstandard` / `netstandard-b` (net48 against netstandard2.0), `isolated`, `time-rule`, `tier-rule` (a job of `ci.yml` on every
full run; `tier-rule.yml` stays for a manual run), `new-tests-on-base` and `determinism` (reusable workflows that `ci.yml` calls
in the PR's context: `new-tests-on-base.yml` always, `determinism.yml` when the PR changes what it runs, `tools/ci_scope.py`
`DETERMINISM_PATHS`; neither has a `pull_request` trigger of its own any more). On a PR it runs only while the PR has the label
`full-ci`:

- The coordinator sets `full-ci` (like `bugfix`) when the PR is ready to merge. The `labeled` event starts the full stage on the
  current head; every later push while the label is on runs it again on the new head, so no removing and re-adding is needed.
- Setting `bugfix` re-runs it (so `new-tests-on-base` then requires a failing test). Any other label (`mutation`,
  `gate-change`, ...) starts a run in a concurrency group of its own whose jobs are all skipped: it cancels nothing and posts no
  status (the gate is the status, not the job checks; its skipped job checks stand next to the real ones of the same head).
  Still, set `bugfix` before `full-ci` when it is known, to save a full run. Removing a label starts nothing.
- When the full stage runs, `quick` is skipped (the full stage covers it).

**Merge gate**: the commit status `full-ci` on the PR's head, posted by the job `full-ci-status` (`statuses: write`, `if:
!cancelled()`: a run that a newer one cancelled posts nothing): pending ("label full-ci not set") without the label; success only
if `linux`, `netstandard`, `tier-rule` and `new-tests-on-base` passed and `isolated`, `time-rule` and `determinism` passed or were
skipped (a PR that does not change the library, or not what determinism runs); failure otherwise. A new push has no status until
its own run ends, so an old green never stands for a new head. A PR of Markdown only that no test reads never runs `ci.yml`
(its `paths`; the Markdown that the tests or the tier rule read, `tools/ci_scope.py` `CI_MARKDOWN`, is in them: the Markdown files
of `SHARED` and `docs/guides/**`, kept equal by `test_ci_scope.py`), so `docs.yml` posts `full-ci` for it: success when the docs
check passed, failure when it failed (`tools/ci_scope.py --docs-only`). The merge rule: `full-ci` green on the final head. A fork
PR's token cannot write statuses: the job warns, and such a PR needs the owner's check of the jobs and an admin merge (bypass).

Limits of the gate (review of #1469):
- The status is posted for the head, but the tests ran on the merge commit with the base of that time. A merge to master after
  that is not re-tested on the PR ("require branches to be up to date" would close this at the cost of a full run per merge);
  the batched master run catches the combination.
- A same-repository PR could post `full-ci` itself from a changed workflow or `tools/ci_scope.py`: such a PR changes gate files,
  so it needs the label `gate-change` and the owner's review of exactly that.
- `.md` is matched case-sensitively, like GitHub's path filter: a `README.MD` is not Markdown for either, so it runs CI.
- GitHub disables scheduled workflows of a repository after 60 days without activity; then the batched master run stops until
  it is re-enabled (Actions tab).

**Branch protection (the owner's setting; this PR changes no repository setting).** Settings > Branches > rule for `master` (or a
ruleset): "Require status checks to pass before merging" with the single required check `full-ci` (a commit status, source:
any). Remove the old job names (`linux`, `netstandard`, `netstandard-b`, `isolated`, `time-rule`) if they are required now: they
are skipped on every push without the label, and a skipped check counts as passed. Not `quick` (a docs-only PR has none, and the
full stage covers it), not `tier-rule`, `new-tests-on-base`, `determinism` (all in `full-ci` now). "Require branches to be up to
date" stays off (it would re-run the full stage after every merge to master). `gate-change` stays as it is.

**Batched master CI**: no run per merge. `ci.yml` runs on master every 2 hours (`schedule`, `41 */2 * * *` UTC) and on
`workflow_dispatch`; tags run as before. The scheduled run asks `tools/master_batch.py` first. It finds the last master run that
tested its head among the last 30 completed master runs (events schedule, workflow_dispatch, push: a fork PR from a branch named
master does not count): every full-stage job ran to a result, success or failure (`linux`, `netstandard`, `netstandard-b`, and
`tier-rule` where the run has it; `isolated` and `time-rule` may also be skipped; none cancelled or timed out). It skips when
that run's head is master's head, or when only Markdown that no test reads changed since then (`docs_only`). A batch of tool or
benchmark changes only runs the full stage with the library suites skipped (the suites from `tools/ci_scope.py` on the diff), so
the tool tests, the benchmark builds and the error bound audit of `linux` run on master's head. Everything else runs the full
matrix, and so does a run that finds no tested run or whose diff fails. A red run on an unchanged head is not repeated: a flake
needs "Re-run failed jobs" or a dispatch. The nightly, coverage and runtime-watch workflows are unchanged.

**A red batch** (several merges in one run): `tools/master_bisect.py --good <last green head> --bad <red head> --list` lists the
merges; `--filter "<the failing tests from the run's log>"` bisects them locally over the first-parent history (`git bisect
--first-parent` with `dotnet test ... -f net10.0 --filter`; `--cmd` takes any other check, e.g. the net48 build, chained with
`&&`), about log2(merges)
builds and no runner minutes, and names the first bad merge. A failure that shows only on the Windows net48 shards and cannot be
reproduced locally: make a branch at the middle merge, `gh workflow run ci.yml --ref <branch>`, and halve by hand (each step
one full run). The bad merge gets a fix PR (with `bugfix`), a revert only for a real defect that cannot wait.

**gate-change** runs on every `synchronize` and label event of every PR; a run is one short job (about 15 s, billed as a
minute). A `paths` filter would make the check disappear for PRs without gate files (a required check would then wait forever)
and an early exit inside the job saves seconds, not billed minutes, so it stays as it is.

## 3.1 Micro benchmarks with regression detection

BenchmarkDotNet (MIT) covers the kernels:
- the exact predicates;
- AABB tree queries;
- the fast winding number;
- the exact arrangement on a fixed input;
- reducer and remesher passes.

It runs by hand and in a scheduled workflow. The results are compared with the last stored run. Shared CI
runners are noisy, so only large changes (more than 20 %) are flagged, and a performance claim in a PR
still needs before/after numbers from one machine (CLAUDE.md).

## 3.2 Complexity tests

Each operation with a nested loop over the input gets a scaling test, which counts work at n and 4n
(candidate pairs, visited cells, or time with a wide margin). It is cheaper and less noisy than timing.
Past cases: #63, #72, #105.

Wall-clock budget tests must not compete with other test collections in the same runner. The collapsed-corridor
oracle check remains in `QualityMesh2CollapsedPartsReportTests` with its unchanged input, result assertions and
budget, but that class uses the `Meshing2OracleTiming` collection with `DisableParallelization = true`.
The scheduling regression in `RobustnessSweepRemainingTests` checks both the assignment and opt-out metadata.
This uses xUnit's [collection-level parallelism opt-out](https://xunit.net/docs/running-tests-in-parallel), not a
suite-wide concurrency change; it does not eliminate noise from unrelated processes or other test assemblies.

## 3.3 Runtime watch (extreme cases, nightly)

`.github/workflows/runtime-watch.yml` times a fixed list of known extreme cases of the Exact Boolean and the Alpha Wrap
daily at 01:17 UTC on master (owner 2026-11-13) and on demand (a full run is about 1 h typical, about 4 h
if every case hits its timeout; a run under way is never cancelled by the next trigger) (`tools/runtime-watch/cases.json`, run by `tools/runtime_watch.py`). Owner decision 2026-11-12: it
watches the tail, not the median. Each case is one process per run (isolated, never two cases at once), a few repeats,
the median counts; the machine (CPU, cores, memory, dotnet) is recorded. Procedural cases run directly; Thingi10K meshes
are fetched at run time by the nightly job only (never committed, never fetched by a PR-tier test).

Outlier rule (`rules` in the case list): a median more than 1.3x the median of the last 10 runs of the case and more than 1 s
above it (a regression), applied once the case has a baseline; a failed or timed-out case. There is no absolute time limit:
the 20 s limit is for tests, not for the tail cases (owner 2026-11-15, #888; before, a median over 20 s was flagged `hard`
and made 28 of 35 cases outliers on the first run). A case is judged from 3 earlier successful runs on (`min_runs`; the
median of one or two runs flags any noisy first night, from three one outlier cannot set it, owner 2026-11-16, against the median of
the runs that exist up to 10); before that the report marks it "baseline building (n of 10)" (n successful runs so far,
until the window of 10 is full) and it gets no flag. The
job summary lists all cases; one tracking issue, "Runtime watch: outliers", gets a comment only when something is new (none of the last 3 nights had the flag: a
regression or a failure), so a known slow case is not repeated every night. Results are a workflow
artifact; the history (`history.json`, last 60 entries per case, plus one result file per commit) is on the data branch
`runs/runtime-watch` (never merged). A run with a case subset (`only`) does not update the history. GitHub-hosted runners
vary, so the absolute 1 s and the 1.3x are a floor; a persistent step change enters the rolling median after about five nights.

Add a case: an entry in `cases.json` (`area` boolean or alphawrap, `kind` procedural or thingi, `setup` and `run` argv lists with
`{work}`, `{dll:Project}` and `{thingi:ID}`, `time_field` the JSON field of the run's last output line, or `wall`), then
`python3 tools/runtime_watch.py validate tools/runtime-watch/cases.json`. The tooling's tests (`tools/test_runtime_watch.py`:
rule, case-list parsing, runner on stub commands) run in the PR tier without network or dotnet.
