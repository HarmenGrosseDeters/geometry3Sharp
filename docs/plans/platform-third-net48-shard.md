# Plan: a third `net48` test shard in CI

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Platform/QA/CI: a third net48 test shard"
- **Kind:** parked (trigger: `net48` shard `b` remains the CI critical path AND Windows runner queue wait is short)
- **Tier:** S/M (CI workflow split — touches `.github/workflows/ci.yml` and `tools/ci_scope.py`, both gate files -> needs `gate-change` label)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Recorded in [platform.md](../roadmap/platform.md#open-for-the-future-2026-11-15) lines 435, 476-477:
  - `net48` tests on Windows currently run in two shards (`a` and `b`), rebalanced in #381 (at that time: `a` ~39 min
    dominated by mesher tests, `b` ~30 min with envelope tests; later shard `b` grew as new suites were added).
  - Rule from 2026-11-16: add a third `net48` shard **only if** (1) Windows runner queue wait is short (adding a 3rd
    Windows job does not stall behind GitHub's concurrent Windows runner cap) AND (2) shard `b` is consistently the
    critical path of PR CI.

## Trigger / decision needed

Measurement across the last 20 full `ci.yml` runs showing that:
1. `net48` shard `a` or `b` is the slowest job (critical path) of `ci.yml` on >= 75 % of runs, AND
2. Splitting into 3 shards cuts total PR wall-clock time by >= 20 % after accounting for Windows queue wait and build
   overhead.

## Goal

Cut PR CI wall-clock time by splitting the `net48` test suite cleanly into 3 balanced shards (`a`, `b`, `c`) with zero
dropped or duplicated tests.

## Scope

- `.github/workflows/ci.yml` (update `net48` matrix / filters — **gate file**, needs `gate-change` label)
- `tools/ci_scope.py` and `tools/test_ci_scope.py` (if shard selection is tracked there)
- New or updated shard-partition check test ensuring `shard_a ∪ shard_b ∪ shard_c == all net48 tests` and pairwise
  disjoint (no silent test drops!)
- `docs/QA.md`, `docs/roadmap/platform.md`, `docs/OPEN_TOPICS.md`

## Non-goals

Do not exclude or skip any `net48` test; do not change test parallelism or timeouts (`blame-hang` stays on); do not
touch mutation workflows.

## Interface

None public (CI matrix only).

## Look before you build

1. Library / repo: `.github/workflows/ci.yml` (current `net48` shard filter expressions and timings), `tools/trx_durations.py`,
   `tools/gate_files.txt`, `gh run list --workflow ci.yml --limit 20`.
2. Prior art: greedy bin-packing of test classes by historical `.trx` durations (LPT scheduling) or deterministic hash
   bucketing (`FullyQualifiedName` hash mod 3) so newly added classes auto-balance without manual filter edits.

## Steps (one PR, clean commits)

1. **Queue-wait & critical-path measurement (before any edit):** pull job start/complete/queue timestamps of the last
   20 full `ci.yml` runs via `gh run view --json jobs`; compute median/p90 wall time of linux vs `net48` shard `a` vs
   shard `b` and Windows queue wait. Stop here if `net48` is not the critical path or queue wait dominates!
2. Design a partition of `net48` test classes into 3 balanced shards (`a`, `b`, `c`) — verify total test count across
   the 3 shards equals the single-run `net48` test count exactly.
3. Update `.github/workflows/ci.yml` (and `tools/test_ci_scope.py` / partition check), run Python tool unit tests, and
   compare before/after CI wall time on the PR's full CI run.

## Acceptance

- Sum of executed test counts across shards `a + b + c` == exact total `net48` test count (0 dropped, 0 duplicated;
  verified in PR summary from CI logs).
- Before/after CI wall-clock table (command + run IDs in PR): `net48` critical-path wall time reduced by >= 20 %.
- `python3 -m unittest discover -s tools -p 'test_ci_scope.py'` and `test_gate_check.py` green.
- `gate-change` label reviewed (coordinator can label if strictly preserving/tightening coverage).

## Sources and licences

Own CI tooling.

## Budget and stop rule

0.5 h measurement + 1 short session if triggered. Stop immediately at step 1 if `linux` or queue wait — not `net48`
test execution — is the actual critical path.

## Risks and open questions

- Manual prefix/substring filters drift over time and risk dropping new classes if not written as a total partition
  (e.g. `shard_c = !(shard_a || shard_b)` or automatic hash sharding via a small helper script, like `coverage.yml`'s
  4-shard core split). Reusing `coverage.yml`'s sharding pattern avoids drift completely!
