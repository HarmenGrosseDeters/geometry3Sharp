# Plan: tests above 20 s in some full runs (#698)

- **Board row:** OPEN_TOPICS.md §3 "Ready to start", row "Platform/QA/CI: tests above 20 s in some full runs"
- **Kind:** ready to start (remaining part needs one owner decision)
- **Tier:** M (tests); the Reducer loop option is M library code, default output bit for bit
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): one cause is fixed (PR `#1341`): `ExactWindingNumberTests.ConcurrentFirstCallInitialization_ClosedAndOpen_MatchesSerial` took 412 s and 544 s in two full CI runs (0.01 s alone) because its `await` resumed on xUnit's busy synchronization context; it now blocks on the pool tasks. Issue #698 stays open; the Reducer loop decision below is still open.

## Current state

Issue #698 (open). Last analysis (issue comment with #1145): 12 CI full runs, 2026-11-18/19, linux net10.0, 4 cores.

- 15.6-15.9k tests per run; summed test time / wall = 3.8-3.94; wall 822-1383 s (runner speed differs ~1.6x).
- Tests above 20 s per run: 1, 1, 0, 2, 1, 0, 0, 0, 5, 1, 1, 2. Above 10 s: 10-64 per run.
- Cause: oversubscription (4 xunit collections x the library's `Parallel.For` on 4 cores), not a found deadlock. A
  test above ~5.5 s alone can exceed 20 s in a slow run; 16 tests have a median alone of >= 5.5 s.
- #1145 (merged) split five 5-8 s tests into rows of the same power.
- Still above 20 s once (max full / alone): `ReducerSharedDeviationQueriesTests.Reduction_SameWithAndWithoutSharing`
  42.8 / 2.2-4.6 s (Reducer loop starvation); `MeshRobustRepairTests.SolidifyNotClean_AlphaWrapped` 30.8 / 3.4-5.4;
  `RemeshReduceCampaignTests.Reducer` 30.0; Meshing2 oracle rows F-5 (22.2 / 4.6-8.4) and F-38/d, F-41/a;
  `SafeRoundingDegenerateInputTests.DegenerateInput_InBooleans_CleanWithinTheBound` 22.1; `ExactBooleanPropertyTests.Boxes` 21.9.
- Fixed 2026-11-22 (PR #1341): the only `async` test of the suite (`ConcurrentFirstCallInitialization_ClosedAndOpen_MatchesSerial`,
  412 / 544 s in full runs, 0.01 s alone; a continuation on `MaxConcurrencySyncContext`, not pool starvation). In the
  three full runs read for #1341 it was the only test above 20 s (next 19.9 s); after the fix, in one local full run,
  `TerrainSafetyTests.SafetyLine_Guaranteed_GapNotCertified` was the slowest (32.6 s full, 6.3 s alone; #680).
- Measured and dropped before: `SetMinThreads`, one collection for heavy classes, `maxParallelThreads=2` (+30 % wall),
  limited-concurrency scheduler (+7 %), a pool cap (starved `Timed`), server GC; for the Reducer loop
  (`Reducer.collapse_within_deviation`, `gParallel.ForRange` over ~10 triangles): serial is 1.4-1.7x slower alone but
  4-8x faster under pool load (issue comment, `runs/reducer-deviation-loop`); caller-first variant 2.7-3.3x slower alone.

## Trigger / decision needed

Owner decision on the Reducer loop, options (each bit-identical, verified on every row of the benchmark):

| Option | Effect | Cost / risk |
|---|---|---|
| A. Keep (today) | Fastest alone; spikes to 20-43 s in full runs and under app-level parallelism | Recurring #698 entries |
| B. Adaptive: serial when the pool is busy (`ThreadPool.PendingWorkItemCount > 0`) or when called from a pool thread | Keeps alone speed, avoids nested starvation | Timing-dependent path choice (results stay identical; only speed varies) |
| C. Serial below N triangles per collapse (internal `DeviationParallelMinTriangles`, exists for the benchmark) | Simple, deterministic | 1.4-1.7x slower alone on 42k-200k meshes |

How others solve it: .NET guidance against nested fine-grained `Parallel.For` (Toub, "Patterns of Parallel
Programming"); TBB and the .NET TPL rely on work stealing with a grain size (TBB `blocked_range` grainsize); OpenMP
disables nested parallelism by default (`OMP_MAX_ACTIVE_LEVELS=1`); libigl / CGAL parallelise at the outer level only.

## Goal

No test above 20 s in any of 10 consecutive full CI runs, without weakening any test.

## Scope

- Tests: `tests/geometry3Sharp.Tests/**` for `SolidifyNotClean_AlphaWrapped`, `DegenerateInput_InBooleans_*`, and the
  vacuous `(500, *)` rows of `ConstrainedDelaunayMesh2Tests.ExtremeScales_GiveTheSameMesh` (all seeds skipped by
  `> 1e70`: fix or delete).
- Library (option B or C only): `mesh_ops/Reducer.cs` (`collapse_within_deviation`), `core/gParallel.cs`.

## Non-goals

No xunit parallelism change (measured), no `Slow` / Nightly moves without the reason in the test, no change to
results. Meshing2 oracle rows are one case each (no split).

## Interface

None public. Option C: an internal threshold constant.

## Look before you build

1. Library: `tools/trx_durations.py`, the `time-rule` summary of ci.yml, `SLOW_BACKLOG.txt`, the isolated re-run (#685),
   `benchmarks/ReducerBenchmark -- loop`.
2. Prior art: see the "How others solve it" line.

## Steps (one PR, clean commits)

1. Pull trx of the last 10 full runs (`gh run download -n full-run-trx`), table with `tools/trx_durations.py`.
2. Split or shrink the tests above ~5.5 s alone that are splittable (same power: same inputs, guards add up).
3. Fix or remove the vacuous `ExtremeScales (500, *)` rows (a test that checks nothing).
4. If the owner picks B or C: change the Reducer loop; benchmark alone and under load (median of 5), bit-identical.

## Acceptance

- `python3 tools/trx_durations.py` over 10 full runs: 0 tests > 20 s (commands and run ids in the PR).
- Split tests: same inputs and random sequence, mutants killed before are still killed (one deleted-line check each).
- Option B/C: `ReducerBenchmark -- loop` before/after alone and with 4 busy pool items; outputs bit-identical.

## Sources and licences

None (own code).

## Budget and stop rule

Steps 1-3: one session. Step 4 only after the owner's pick; stop if alone time regresses > 10 % for B.

## Risks and open questions

- Runner speed varies 1.6x; "10 runs without a spike" may need ~2 weeks of master runs.
