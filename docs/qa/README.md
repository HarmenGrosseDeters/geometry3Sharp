# Quality Assurance & Test Strategy

This directory organizes the QA strategy, test tiers, verification gates, fuzzing corpora, and local setup instructions for `geometry3Sharp`.

| Document | Scope |
|---|---|
| [README.md](README.md) | Current QA baseline, fault-class history, and test tiers (Fast / Slow / Nightly) |
| [testing-and-invariance.md](testing-and-invariance.md) | Invariance, reuse, determinism, result validation, certifying checkers, supported input range, verifiable agent checks |
| [mutation-and-coverage.md](mutation-and-coverage.md) | Stryker mutation testing shards and changed-line coverage gate |
| [fuzzing-and-corpora.md](fuzzing-and-corpora.md) | Fuzzing, regression corpora, failure minimization, reference parts, robustness sweep, external issue cases |
| [ci-and-gates.md](ci-and-gates.md) | API compatibility, gate-change check, scheduled runs, Windows `net48` hang diagnosis, performance & runtime watch |
| [local-setup.md](local-setup.md) | Local .NET SDK installation and verification commands |

---

## 1. Where we stand

The first part describes the state before the QA work (October 2026); the second part describes what was
done since and what is open.

**Suite.**
- `tests/geometry3Sharp.Tests` has 1216 xUnit tests, which take about 3.5 minutes wall clock on net10.0 and
  13 minutes of CPU time.
- The slowest tests are geodesics on rough terrain (70 s), terrain safety lines (35 s) and the
  solidify/wrap paths of `MeshRobustRepair` (30 s each).
- `tests/g3cli.Tests` covers the CLI and the MCP server.
- CI runs both suites on Linux (net10.0), runs the library tests on Windows (net48 against the
  netstandard2.0 build), packs the package with API validation against 1.0.324, and runs a smoke test
  of the package. The library tests run in two tiers, see "Test tiers" below.
- Before the harness of 2.1 there were no skipped tests; it adds two that document reported bugs. No flaky
  test showed up in repeated runs, after the `BitArray` races were fixed (ThreadingFixesTests).

**Test tiers (2026-11-09).** A full run of the suite (8133 tests) took 21-23 min on 4 cores, and PR CI 25-35 min.
- A test that takes 5 s or more (alone: CI re-runs the tests tagged Slow and the backlog alone and judges those on that time; every other test on its full-run time, see "Judged alone" below) carries `[Trait("Category", "Slow")]`. A long test is split into
  test classes that xUnit runs in parallel (it runs the classes in parallel, but the tests of one class one after
  another), each class checking a share of the rows, with a test that the shares are exactly the old set
  (`RowGroups_PartitionTheCoveredRows`, `TagCheckRows_PartitionTheCoveredM3Rows`, `QueryMeshes_AreCoveredByTheOracleTests`).
- A draft PR gets no CI (2026-11-15, runner load: most runs of drafts were cancelled by the next push; the gate is the `if:` of the job
  `scope`, the other jobs need it; a skipped run of a draft has its own concurrency group, so it cannot cancel a real run). On demand:
  `workflow_dispatch` (Actions tab, or `gh workflow run ci.yml --ref <branch>`; the checks attach to the head commit), which runs
  everything. A PR that is ready for review (`ready_for_review` starts the run), every push to master and manual runs run all jobs;
  check a draft locally with the CLAUDE.md list (with the net48 build). `FULL_TIER` in `ci.yml` is the switch for the Slow tier: it is
  true for every run that exists now (the draft conditions of `isolated`, `time-rule` and the Windows jobs stay as a second guard).
  PR runs cancel on a new push. Master and manual runs: one concurrency group per ref without cancel-in-progress (they queue; GitHub keeps
  one pending run, so a burst of merges runs the first and the newest commit; the newest covers all merged changes).
- The full run lists the slowest tests and the untagged tests of 5 s or more in its step summary
  (`tools/trx_durations.py`; a trx has no traits, so the tagged tests come from `dotnet test --list-tests --filter
  "Category=Slow"`). The limits it enforces are those of the ratchet below (it replaced the gate at 60 s for untagged tests,
  b73c151d). Tag a test that shows up in the list.
- The scheduled tier: `[Trait("Category", "Nightly")]` tests run only in `.github/workflows/nightly.yml` (daily on master, and
  `workflow_dispatch`; `dotnet test --filter "Category=Nightly"`, 90 min budget, durations in the step summary). PR and master CI
  exclude the category (`Category!=Nightly` on Linux and both net48 shards). It holds the rows above 10 s whose run is the check:
  the exact reference path of the 2D mesher oracle on its largest catalogue rows (`CatalogueRowsNightly`, `CorruptedRowsNightly`,
  and F-38/d of `ExtremeScales`). Also three K checks that are runs by nature, each with a reduced variant in the PR tier
  (`Solidify_IsDeterministic_Reduced`, `InsertionOrder_Reduced_DoesNotChangeTheResult`, `RemesherPro_FullRemesh_Reduced_...`). The Stryker filters of `tests/mutation` name classes of the exact arithmetic and do not
  contain these; a class that gets Nightly tests and is in a filter needs `&Category!=Nightly` there.
- The test time rule of CLAUDE.md (5 s, up to 10 s with `Slow` and a reason, nothing above) is a ratchet over the full run: the tests tagged `Slow` and those on the backlog are judged on their time alone (CI re-runs them, below), every other test on its full-run time:
  `tools/trx_durations.py --backlog tests/geometry3Sharp.Tests/SLOW_BACKLOG.txt` fails for a test above 10 s, and for one of
  5 s or more without `Slow`, unless its method (`Class.Method`) is on that list. The list was made from a full run with
  every method that has a row of 4 s or more (a margin: the CI runner is not that machine) and may only shrink: the run
  fails for an entry that the base branch's list does not have, and for an entry that names no test (renamed or deleted).
  The run fails for an untagged test only from 7 s (`--fail-from`): the CI runner's timings vary by a second or two around the
  5 s line, so 5 to 7 s untagged is reported as a warning in the step summary, 10 s is a failure always (except Nightly).
  While the backlog is filled from the runner timings of master's full runs, the ratchet runs in report mode
  (`RATCHET_ENFORCE: '0'` in `ci.yml`: violations are printed as warnings and the step passes); a PR sets it to 1.
  Splitting a CPU-bound test into parallel classes does not reduce its duration in the saturated full run (four classes
  of 9 s instead of one of 9 s, #521): only less work does.
  **Judged alone.** The full run oversubscribes the cores (test classes in parallel, each with the library's parallel loops), so a
  test takes several times its time alone: `RemeshReduceCampaignTests.Reducer` ran 3 to 95 s between runs of the same code and
  takes 6 s alone; a sequential run of 30 classes of the backlog had 2 of 48 entries at 5 s or more. So the rule is judged on
  the time alone: `trx_durations.py full.trx --threshold 5 --flag-filter flagged.txt` writes a `dotnet test --filter`
  expression for every method with a row of 5 s or more; those tests run again alone
  (`dotnet test --no-build --filter "$(cat flagged.txt)" --logger trx -- xUnit.ParallelizeTestCollections=false`), and
  `trx_durations.py full.trx --isolated alone.trx --backlog ...` replaces the full-run time of each re-run test by its time
  alone (the report keeps "full run N s"); a re-run that failed keeps the full-run time. Measured on 4 cores: 93 flagged methods
  (498 test rows, all rows of a flagged theory) took 15 min alone, against 15.5 min for the full run; 129 tests of 5 s or more
  in the full run were 50 alone. Rows of methods below the threshold in the full run are not re-run. In CI the job `isolated` (parallel to `linux`) runs the tests tagged
  `Slow` and the methods of `SLOW_BACKLOG.txt` alone, one class after the other (`--rerun-filter`: the candidates are known before the
  full run), and the job `time-rule` judges the full run's trx with `--isolated`, with the "Potential defects" section in its step
  summary. It runs in report mode (`RATCHET_ENFORCE: '0'` in the job) until the backlog is remade from isolated times and the job has
  been green on master. A test above the rule that is neither tagged Slow nor on the backlog has no result alone: it stays judged
  on its full-run time, and the report lists it.
  **Above 20 s (owner 2026-11-12).** A test above 20 s in any run, the full parallel run included, is a potential defect
  (starvation, a hang, a pathological input or algorithm), whatever its time alone: it gets an issue with the full-run and
  isolated times and a diagnosis, not only a backlog line. The report lists them in a separate "Potential defects" section
  (`--defect-above`, default 20) with the time alone next to the full-run time; a flagged test without a passed re-run result
  is listed too and stays judged on its full-run time.
  Fix a slow test, then delete its line. The reason on a `Slow` tag is a review item, not machine-checked.
- A PR that changes only `tools/`, `benchmarks/` or `docs/` files that the library build and the test projects do not read
  (`tools/ci_scope.py`, which lists the ones they read; `test_ci_scope.py` checks that against the `.csproj` includes)
  skips the library suites (net10.0, net48, Clipper2). The library build, the API validation (pack), the smoke tests,
  the benchmark builds, the tool tests and the acceptance gates run as before; the g3cli tests run when `tools/g3cli`
  changed. The `netstandard` jobs still start (the check names are fixed) and skip their steps.

**Kinds of tests.**
- Almost all tests are example-based regression tests: one hand-built input per bug, with the expected
  output.
- Robustness is tested case by case, for example UTM coordinates in some analysis and terrain tests,
  and subnormal values in the predicate tests.

**What is missing.**
- There are no systematic invariance or property tests, no fuzzing, no coverage measurement, and no
  automated mutation testing; mutation checks are done by hand, as CLAUDE.md requires.
- Benchmarks (`benchmarks/`) run only by hand, mostly on Thingi10K meshes that cannot be committed (procedural
  exceptions: the cube grid of `BooleanComparison`, synthetic terrain).

**After the first QA round (end of October 2026).**
- In place:
  - the invariance, reuse and determinism harness (#120, 2.1);
  - the review checklist (#126);
  - seeded fuzzing of all readers in the suite (#127, 2.5);
  - the Stryker workflow for the exact arithmetic (#128, 2.2);
  - differential tests of the predicate filters and exact tests of the `ExactPoint3` conversions (#137, 2.4).
- The suite has about 1470 tests and no skipped test. It takes about 5 minutes wall clock on net10.0.
- Bugs found by the new tests and fixed:
  - absolute tolerances: `MeshGeodesicDistance` (#115), and the boundary shells of the intrinsic refinement
    (#135);
  - nondeterminism: `ExactMeshBoolean` (#123), `MarchingCubesPro` (#124) and `MeshSignedDistanceGrid` (#125);
  - untrusted input:
    - the `.g3mesh` reader trusted the stored buffers (#130);
    - OBJ, OFF and PLY had index overflows and header-sized allocations (#131);
    - non-finite coordinates are now rejected by every reader (#136);
  - a test gap: no test caught an InSphere filter bound that was too small (#137).
- Open, in this order:
  1. defect generators and fuzzing of mesh inputs (2.1, 2.5; done since: `Harness/Defects.cs`, coverage-guided fuzzing in
     ReaderFuzzPilot and MeshFuzzPilot);
  2. result validation in the existing success tests (2.3);
  3. mutation testing of `mesh_ops/ExactMesh*` (2.2; done, open gaps in tests/mutation/README.md) and the open
     `AdaptivePredicates` survivors;
  4. coverage (2.6; done since: the changed-line gate in `coverage.yml`, #434);
  5. the regression corpus (2.7; first part done: `RegressionCorpusTests`);
  6. performance (3.1, 3.2).

**What the reviews found.**
- The retroactive reviews (packages A-F) and the post-merge reviews led to 45 bug-fix PRs (#60-#112).
- Of these, 33 came from package reviews, 7 from post-merge reviews, and 5 from benchmarks or real data.
  No bug report came from a user.
- By fault class, counting primary and secondary classes (the PRs are listed in the checklist; #84 fixed a
  benchmark script, the others the library):

| Fault class | Fixes | Would have been caught by |
|---|---|---|
| Silent success (result not verified, wrong status, hard limit reported as clean) | 10 | result validation in every success test (2.3) |
| Scale, units, far from the origin, absolute tolerances | 8 | invariance tests (2.1) |
| Degenerate input | 7 | defect generators in the invariance harness (2.1), fuzzing of mesh inputs (2.5) |
| Floating-point range (subnormal, overflow, NaN casts) | 7 | randomized differential tests against exact arithmetic (2.4), mutation tests (2.2) |
| Stale state, reused instances | 5 | reuse tests (2.1) |
| Integer overflow, sizes, hard limits | 4 | large-extent and limit tests (2.1), fuzzing (2.5) |
| Untrusted input (readers, CLI) | 4 | fuzzing (2.5) |
| Open, non-manifold, bowtie input | 5 | defect generators (2.1) |
| Complexity | 3 | scaling tests (3.2) |
| Inexact ordering (`atan2`, angle sums) | 2 | randomized near-degenerate tests (2.4) |
| Compatibility, undocumented behavior change | 2 | package validation (exists since #92), checklist |

- Nondeterminism did not appear in that list because nobody looked for it. The first determinism tests
  found three cases right away, and the invariance tests a fourth bug (2.1).
- 2 of the 45 fixes made existing tests tighter: loose absolute bounds had hidden a UTM bug (#95), and
  one test was missing (#101). Tests that compare with absolute bounds catch less than tests that
  compare results with each other.
