# Plan: narrowed mutation filters made in CI

- **Board row:** OPEN_TOPICS.md §3 "Ready to start", row "Platform/QA/CI: narrowed mutation filters in CI"
- **Kind:** ready to start; **goes through the coordinator** (mutation tooling, AGENTS.md "Mutation tooling")
- **Tier:** M (CI tooling); gate files: needs the `gate-change` label (coordinator for a tightening, owner otherwise)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- `tests/mutation/narrow_tests.py` (`make` from a full-filter report, `apply` with a staleness guard) is wired into
  `full()` of `run_stryker.sh`; stale / missing filter -> full filter (never loses kills, only the saving).
- Owner 2026-11-13: `--timeouts one` default; the Sunday (UTC) scheduled run and every `G3_FULL_TESTS=1` run are
  unfiltered and are the check of record and the source of new filters.
- Checked in (`tests/mutation/test-filters/`, made 2026-11-15 by hand): envelope-1 (9 of 21 tests, est. saving 93 %),
  envelope-2 (9/21, 85 %), envelope-3 (10/21, 52 %), terrain-arith (18/47, 57 %). Not made: envelope-4 (8 %),
  terrain-overlay (19 %) below the 20 % floor; terrain-kernel and rounding-1..8 stale; arrangement, boolean, planar had no
  full master report then (they have now: 3.0 completion runs 44217581261, 44295650298, 44313345652).
- Open ([platform.md](../roadmap/platform.md) and tests/mutation/README.md "Narrowed test sets"): make the filter files in
  CI after the nightly / Sunday full run, stored like the baseline; this touches the cache lines of `mutation.yml`.

## Trigger / decision needed

Coordinator go (every PR's mutation gate depends on the workflow). Option choice:

| Option | What | Cost of the edit | Risk |
|---|---|---|---|
| A. Filters as cache entries | A step after an unfiltered full run runs `make` and saves `test-filters/<cfg>.json` with the baseline cache key | Edits a reporting / cache step: per AGENTS.md a cache-key or reporting edit runs nothing by itself | Cache eviction (7 days unused) -> silent fallback to full (safe) |
| B. Filters as a bot commit / PR | The same step opens a PR with refreshed `test-filters/` | No workflow-region risk beyond a new step; needs a token with write | PR noise weekly; staleness between merges |
| C. Keep by hand | Refresh with a local `make` after each Sunday run | None | Filters go stale (as rounding-1..8 did within 4 days) |

Leaning: A (the baseline already lives in the cache; `apply` treats a missing file as full).
How others solve it: Stryker's own "since" / incremental mode keeps a baseline report (Stryker.NET `--with-baseline`,
dashboard or disk storage); PIT's history files (`historyInputLocation`) reuse kills between runs; Google's mutation
testing at scale (Petrović & Ivanković, ICSE-SEIP 2018) runs only tests covering changed lines.

## Goal

Every shard with a >= 20 % estimated saving runs narrowed between Sunday runs, with filters never older than the last
unfiltered full run.

## Scope

- `.github/workflows/mutation.yml`: one step after "upload report" in the full-run path (reporting region only; must not
  touch the Stryker step, runner, setup, matrix or workflow `env`, which would rerun all shards in full, 4-5 h).
- `tests/mutation/run_stryker.sh` only if `apply` must read the filter from the cache path (a change here runs the
  `fixed` shard in full as a representative).
- `tests/mutation/README.md`, `tests/mutation/test_*.py` for any script change.

## Non-goals

No change to `narrow_tests.py`'s guard or the Sunday rule; no threshold change; no new shards.

## Interface

Cache key: `narrow-filter-<cfg>-<BASELINE_FORMAT>-<sha of cfg>`; filter file format unchanged.

## Look before you build

1. Library: `baseline.py` cache handling, `select_shards.py` (region rules), `tools/gate_files.txt`.
2. Prior art: see the "How others solve it" line.

## Steps (one PR, clean commits)

1. Refresh the checked-in filters by hand from the 3.0 completion reports (`narrow_tests.py make --durations ...`) as the
   first commit (shows the saving per shard, table in the PR).
2. Add the CI step (option A) for unfiltered full runs only; restore step before `apply`.
3. Tests: `python3 test_select_shards.py && python3 test_baseline.py && python3 test_incremental.py && python3 test_suggest_split.py`
   plus `python3 -m unittest test_narrow_tests`.

## Acceptance

- The tests above green; `python3 tools/review_triggers.py master...HEAD` classes named in the review.
- One dispatched full run of one cheap shard (eg `terrain-arith`) saves a filter; the next run's summary says
  `narrowed: N of M tests`; a deliberately changed test file makes it say `full: ...`.
- Before/after wall time of that shard from the job summaries (run ids in the PR).

## Sources and licences

None (own tooling).

## Budget and stop rule

One session plus one dispatched shard run. Stop if the edit cannot avoid the "full rerun" regions of `mutation.yml`.

## Risks and open questions

- A narrowed run's report must never seed a filter (guarded by "unfiltered only"); assert it in the step.
