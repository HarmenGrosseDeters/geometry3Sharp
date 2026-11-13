# Test impact analysis for PR CI (study, 2026-11-13)

Status: study with one measurement (section 8a), no library or workflow code in this PR. Author: study session (Sonnet) for the
coordinator. Tier S (docs only). Owner decisions of 2026-11-13 are in section 12.

## 1. Question

CI runners are saturated (jobs wait 1-3 h); the library suite takes 20-30 min per job on linux and on the two Windows
shards. Can a PR run only the tests whose coverage touches the changed lines, **without losing failures**?

Terms: *test impact analysis* (TIA) = *regression test selection* (RTS): select a subset of tests from the change and a
recorded test-to-code dependency map. *Safe* (Rothermel & Harrold): the subset contains every test that can change its
outcome because of the change. Safe selection is a property of the map, not of the tool; all practical maps have holes
(section 5), so we need a safety net that detects a miss.

## 2. What exists in the repo

| Building block | What it does | Use for TIA |
|---|---|---|
| `tools/ci_scope.py` + `scope` job | path-based: a PR touching only `tools/`, `benchmarks/`, `docs/` (minus the `SHARED` list the test projects read) skips the library suites | already a coarse, **safe-by-construction** TIA at directory level; its `SHARED` list + `test_ci_scope.py` (every path a `tests/*.csproj` includes must be listed) is the model for "a data file is a dependency" |
| `Category=Slow` / `Nightly` tiers, `FULL_TIER` | draft PRs skip `Slow`; Nightly only in `nightly.yml` | a tier split, not a change-based one; keep |
| `tools/trx_durations.py`, `SLOW_BACKLOG.txt` (60 entries) | per-test durations from trx, ratchet, `--rerun-filter` | gives the **duration weights** and the isolated job's filter; the isolated job must keep running the Slow/backlog set **when the map selects them** (see 7) |
| `coverage.yml` (nightly master, 4 shards by class via `tools/coverage/shard_filter.py`, coverlet 6.0.4, `SingleHit`, cobertura) | one cobertura per shard, **per line, merged over all tests of the shard** | coverage per *shard*, not per test. Its first scheduled run failed (platform.md); it is not yet a reliable producer |
| `tools/changed_line_coverage.py` | changed lines of `base..HEAD` (`changed_lines`), cobertura reader (`read_coverage`, `coverage_path`), unmeasurable-lines list | the **diff side** of TIA is done: line sets per file, rename-safe (`--no-renames`), unmeasured lines reported. Only the per-test map is missing |
| `tools/new_tests_on_base.py` | builds head tests against the base library, lists new test methods | independent of selection (it runs only *new* tests) |
| `tests/mutation/narrow_tests.py` (open PR #777) | from Stryker `coveredBy/killedBy` builds a greedy test cover per mutation shard, with content hashes of mutated and test files as staleness guard, falls back to the full set when stale | **same design pattern** (map + hash guard + fallback to full) and a lesson: Stryker records no killer for timeouts. Reusable code: the hash/staleness guard and the vstest filter escaping (`FullyQualifiedName=...`, theory rows reduced to the method) |
| `ExactCodeAllowList`, analyzers, `.editorconfig` lists | exact-code file lists | the exact files are a natural "always full" set |

Not existing: any per-test coverage, any test-to-source map, any selection in `ci.yml`. The only SDK-side facts available
to this study are the repo files; this container has no .NET SDK and CI artifacts (trx) of master are not retrievable
(the latest master `ci.yml` runs are cancelled by the backlog), so numbers in section 8 come from a static probe, see there.

## 3. Prior art (citations marked *(memory)* were recalled, not re-fetched; verify before quoting)

- **Rothermel & Harrold**, "A safe, efficient regression test selection technique", ACM TOSEM 6(2), 1997 *(memory)*. Safety
  definition; control-flow-graph based; safe under the "controlled regression testing" assumption (same environment, same
  inputs). Everything below is judged against that assumption.
- **Gligoric, Eloussi, Marinov**, "Practical regression test selection with dynamic file dependencies" (Ekstazi), ISSTA 2015
  *(memory)*. Coarse (class/file) dynamic dependencies: record every file a test class reads (classes loaded, resources);
  rerun a class when a dependency's checksum changed. Result reported: coarse granularity beats method/statement level on
  end-to-end time (the bookkeeping of fine maps costs more than it saves). Handles reflection and files because it records
  *files opened*, not just code.
- **Legunsen, Hariri, Shi, Lu, Zhang, Marinov**, "An extensive study of static regression test selection in modern software
  evolution" (STARTS), FSE 2016 *(memory)*. Static class-level dependency graph, transitive closure; safe w.r.t. the
  graph but **unsafe under reflection and unanalysed resources**, and over-selects when the graph has hub classes. This is
  exactly our probe's result (section 8: hub types `Vector3d`, `DMesh3`).
- **Microsoft, "Test Impact Analysis" in Azure DevOps / Visual Studio** (docs, `learn.microsoft.com`) *(memory)*. Test-level
  coverage data collected by the VSTest data collector, selection per changed *method*; documented limits: managed code,
  historically .NET Framework/Windows only, and "runs all tests periodically" (a periodic full run is part of the product).
- **Google**: TAP / "Taming Google-scale continuous testing", Memon et al., ICSE-SEIP 2017 *(memory)*. Build-graph (Bazel-like,
  *declared* dependencies) selection: tests are affected if a transitive build dependency changed; post-submit runs
  continuously and bisects culprits. Safe because the build graph is declared and hermetic. We have one test assembly, so
  the "declared graph" degenerates to one node: this route does not transfer without splitting the project.
- **Machalica, Samylkin, Porth, Chandra (Facebook)**, "Predictive test selection", ICSE-SEIP 2019 *(memory)*. ML model
  (gradient-boosted trees on file-change/test-history features) selects tests; reports catching >99.9 % of faulty changes
  while running about a third of tests; **not safe**, so it needs a full run as backstop and treats test-run cost vs. missed
  failure rate as a tunable. Needs a large history of failures; we have almost no red-test history per test.
- Related *(memory)*: Zhang, "Hybrid regression test selection", ICSE 2018 (combine static and dynamic); Elbaum, Rothermel,
  Penix, "Techniques for improving regression testing in continuous integration development environments", FSE 2014
  (history-based prioritisation, time windows).

**What goes wrong (all documented in the above; each is a live risk here):**

| Hole | Why it breaks safety | Present in this repo? |
|---|---|---|
| Reflection / `Type.GetType` / attribute scans | dependency invisible to the map | 13 test files use `GetTypes()` / `GetMethods()` (`grep`, 2026-11-13); `ExactCodeAnalyzerTests`, `MutationConfigTests` read the source tree and configs |
| Data files read by path | no code edge; the file change is not a code change | 21 test files use `File.*`/`Directory.*`; `test_files/`, `property_validation/`, `benchmarks/Meshing2Benchmark/golden/` (the `SHARED` list of `ci_scope.py` already enumerates them) |
| Source-reading tests | the "dependency" is the text of another file | `MutationConfigTests`, `ExactCodeAnalyzerTests`, `GeneratedPredicatesTests` (docs table), `DocExamplesTests`, `RobustnessSweepTests` (docs) |
| Static state, test-order and shared fixtures | a test's outcome depends on a test that is not selected | xUnit runs classes in parallel; shared statics (caches, `Random` seeds) are possible; selection changes the interleaving but not the outcome of a deterministic test (see `docs/design/DETERMINISM.md`) |
| Generated code | edge goes through the generator, not the output | `PredicateCompiler` (`predicates.spec` -> generated predicates); change to the spec must select the generated-predicate tests, and the generator tests |
| Build-level changes | `.csproj`, `Directory.Build.props`, `BannedSymbols.txt`, `.editorconfig`, analyzers, global.json affect every test | must force full |
| Changed code that has no executable line in the map | declarations, constants, `#if NETSTANDARD2_0` branches, attributes, field initialisers | `changed_line_coverage.py` already counts these as `unmeasured_lines` |
| Timing/budget tests | outcome depends on load, not code | `RobustnessSweepTests.Sweep_SameResultUnderSimilarity`, `UVAtlasTests.UVOverlap_MixedSizes_NotQuadratic`; they stay in the always set |
| Cross-TFM | net48/netstandard2.0 take other code paths (`#if`) | coverage is net10.0 only; the Windows shards cannot trust a net10.0 map for `#if` lines |

## 4. Options

Cost tiers per AGENT_WORKFLOW section 1 (S/M/L of the implementation).

### A. Directory-level scope, extended (tier S)
Extend `ci_scope.py`: more outputs (`exact`, `meshing2`, `terrain`, ...) from path prefixes, mapping a path prefix to a
test filter by naming convention only where the test class names are known to follow the code (Terrain*, Meshing2*).
*Guarantee*: only what a hand-reviewed table says; a missing row defaults to full. *Gain*: small (library PRs usually touch
`mesh_ops/`, `math/`, `core/`, and those have hub dependencies). Cheap, safe, but by section 8 the PRs it can help are the
few feature PRs of an isolated area (Terrain, Meshing2, Clipper2 addon).

### B. Static class-level RTS, STARTS-style (tier M)
A script builds the type-reference graph (what the probe in section 8 does with regexes; a Roslyn pass would be exact),
selects test files that reference a changed type, directly or transitively. *Guarantee*: safe w.r.t. the graph only.
*Result*: transitive closure selects 99 % of the weight on any change to a core file; direct-only selects 7-10 % but is
**unsafe** (a test that reaches the changed file through a helper type does not name it). Not recommended alone.

### C. Dynamic per-test-class coverage map, Ekstazi/MS-TIA-style (tier M-L) -- recommended
Produce `class -> {file: line ranges}` (and `class -> files read`), store it as an Actions cache/artifact keyed like the
mutation baseline, select classes whose lines intersect the changed lines of the PR (reusing `changed_lines()`), add the
safety net (section 6), run the rest on master and nightly.
Granularity choice: **test class**, not test method (Ekstazi's finding; xUnit runs classes as units; map size and
collection cost scale with 458 files, not ~9000 methods).

How to produce it from what `coverage.yml` runs: the existing job runs one `dotnet test` per shard of ~115 classes and
merges lines. A per-class map needs one coverage report per class. Two ways, to be measured by a probe on a machine with
the SDK (not possible in this container):
1. *Per-class loop*: `dotnet test --no-build --filter FullyQualifiedName~Class. --collect:"XPlat Code Coverage"` per class,
   in the same 4 shards (hundreds of vstest start-ups at roughly 5-10 s each = 40-80 min extra per shard, on top of the
   ~1 h that coverage already costs; the suite is parallel across classes today, so a per-class loop also loses that
   parallelism: the cost is the open question and could well be 2-3x the current coverage job; classes run alone are also
   the "isolated" timing view). Nightly-tier cost only, so acceptable.
2. *Test-level collector*: `dotnet-coverage collect` with test-level session data / Microsoft's TIA collector (memory:
   mainly VSTest + Windows for the Visual Studio path). Cheaper in start-ups but a new tool, and its output format and
   .NET 10 support must be checked first.
Staleness: the map is built from master at commit M; a PR on base B uses the map of the newest M that is an ancestor of
B (as `coverage.yml` already picks its base run). Lines moved between M and B are mapped through `git diff M..B` (a
file-level fallback: any file changed between M and B that the PR also touches selects all classes that cover the file).
Staleness is **conservative if the map is file-level for stale files**; line-level only for files unchanged since M.
Size: library 169 k lines; 458 test files; cobertura per class ~ the lines hit (a few MB each uncompressed); stored as
`file -> bitset of lines` per class, estimated low tens of MB compressed. To be measured. How fast it goes stale: every
merged PR to a library file invalidates line numbers of that file; with 33 of 50 recent PRs touching library or test code,
a daily rebuild (nightly) with a file-level fallback for files changed since is the design, not a per-PR rebuild.

### D. Predictive selection (Facebook-style) (tier L)
Needs a failure history we do not have (CI is mostly green; the red runs are build breaks and timing). Not now.

### E. Reduce the cost differently (tier S/M, orthogonal; not TIA)
Runner time is the constraint, not selection per se: cancel superseded runs (done), run the net48 shards only on `ready`
PRs and master (the drafts do already skip Slow), shard `linux` (it runs both halves that the Windows shards split, ~4160 s
summed). These are cheaper than C and complement it. Listed so the owner sees C is not the only lever.

**Ranking** (value / cost): C first for the net10.0 `linux` and Windows shards, A as its fallback layer, E in parallel,
B as a staleness fallback inside C (not alone), D not now.

## 5. What can be guaranteed and what is measured

- **Guaranteed (by construction):** the full set runs on master after every merge and nightly (no failure stays hidden
  longer than one merge-queue cycle); a build-level change, a missing map, a map older than N days, a changed test
  helper, a data/golden/doc file in `SHARED`, a changed `.csproj/.props/.editorconfig/BannedSymbols` selects everything;
  every test of a changed test file runs; every class with no map entry runs; the smoke set runs.
- **Measured, not guaranteed:** that the map contains every class that can fail (holes: reflection, timing, static state,
  `#if`). Quantified by the miss detector (6), not assumed.
- **Not covered by TIA at all:** non-test CI steps (pack/API validation, smoke tests, benchmark builds) keep running on
  every PR; the TIA only decides `dotnet test` filters.

## 6. Safety net design

Always run on a PR (the "base set"):
1. every test class defined in a changed test file (and every class in a changed *test helper*: `Harness/*`, anything
   with no `[Fact]`; a changed helper = full, as the mutation guard of #777 documents helpers as its unguarded gap);
2. every class with **no entry** in the map (new class, renamed class, map older than the class);
3. a fixed smoke set (about 30 classes: one per area; 2-3 min), chosen so a core-math regression is seen on every PR;
4. the timing/budget tests and the tests that read repo files (the 21 `File.*` users, `MutationConfigTests`,
   `ExactCodeAnalyzerTests`, `GeneratedPredicatesTests`, `DocExamplesTests`): "environment" tests whose dependency is not
   a code line;
5. all tests when the diff touches: exact code (`math/Exact*`, `AdaptivePredicates.cs`, `mesh_ops/ExactMesh*`, the
   `.editorconfig` list) -- the repo's own rule gives these tier L; `core/`+`math/` hub files whose selection would exceed
   ~70 % anyway (one rule: if selected weight > 70 %, run all, avoids a second job round).

Backstop and feedback:
- **Master/nightly full run** stays (`ci.yml` on push to master runs everything, as today; nightly runs Nightly).
- **Miss detection**: when a master or nightly run has a failing test, a script (`tools/tia_check.py`, new) takes the last PR
  merged before it, recomputes what TIA *would have selected* for that PR from the map of its base, and reports
  "selected: yes/no". A `no` is a **miss**: the map is wrong for that class. Feedback: (a) the failing class gets added to the
  always-set for that file (a `tia_overrides.json`, reviewed list like `unmeasurable_lines.json`), (b) the miss is counted
  in the step summary; (c) a miss rate over a rolling window above a threshold (say 1 per 50 merges) turns selection off
  (full runs) until a person looks. Selection must be **fail-closed**: any script error = full run.
- **Sampling**: on a random 10 % of PRs (hash of the head sha, deterministic) run the full set *and* the selection, report
  the diff. This measures the miss rate without waiting for master to fail, and costs ~10 % of today's runner time.
- Merge-time guarantee: master CI is the gate for "PR is green" only for the selected tests; the repo's rule that master
  stays green then relies on the backstop, so a reverted/forward-fixed merge is the cost of a miss. The owner decides whether
  this trade is acceptable (decision list).

## 7. Interaction with the repo's rules

- **new-tests-on-base**: independent: it runs only the methods a PR adds, against the base library. New classes are in the
  base set (rule 2) so the head run covers them. Keep as is. The `bugfix` label rule is unaffected.
- **Time rule** (`time-rule`, `isolated`): `isolated` already needs the full run trx (`full-run-trx` artifact from `linux`)
  and the Slow/backlog set. With selection, `linux` produces a *partial* trx; `trx_durations.py` must (a) judge only tests
  that ran, (b) not fail "a backlog entry that names no test of the run" (renamed or deleted) for a test merely not selected
  -- today that check would fire for every unselected backlog entry. Fix: judge backlog-entry existence against
  `--list-tests` (a full listing is cheap) instead of the trx. `isolated` reruns only selected Slow/backlog tests; the
  ratchet runs fully on master. This is the one non-trivial interaction and needs its own test in `test_trx_durations.py`.
- **Complementary shards**: the Windows shard filters are complementary by namespace (`Mesh2|Meshing2|Terrain` vs the
  rest); selection is an extra `&(Class1|Class2...)` clause on each; every test still lands in exactly one shard.
  Empty selection in a shard: the job still starts (check names fixed) and passes with a note.
- **net48/netstandard2.0**: the map comes from net10.0; for lines under `#if NETSTANDARD2_0`/`NETFRAMEWORK` and for any
  changed file containing such a directive the Windows shards run **all** classes of the shard (documented unmeasured
  class in `changed_line_coverage.py`).
- **Determinism**: the selection must be a pure function of (map, diff, rules): sorted output, no clocks; the 10 %
  sampling uses a hash of the head sha, not `Random`. A test for the selector with `Similarity`-style invariance does not
  apply (it is not geometry); the repo-style test plan is below.
- **Draft/ready tiers**: orthogonal; selection applies on both, `Slow` classes selected only when `FULL_TIER`.

## 8. Upper bound (what share of the suite a PR would run)

Constraints of this measurement: no .NET SDK in the study container; trx of master not retrievable (recent master `ci.yml`
runs are cancelled); `coverage.yml` has produced no usable map. So there is **no per-test coverage and no trx duration**
here. Instead a **static probe** (throwaway, not committed; Python, regex over identifiers) computed, for the last 50 first-parent
merges on master (2026-11-13 checkout):
- *direct*: test files that name a type declared in a changed library file, plus changed test files (an **under**-estimate
  of dynamic selection: it misses helper-mediated reach);
- *transitive*: closure over the library's type-reference graph (an **over**-estimate: a hub type pulls in everything);
- weight per test file = number of `[Fact]/[Theory]` methods + 10 per `Slow` tag (a weak stand-in for duration; real
  weights are the trx).

Corpus: 458 test files, 169 k library lines, 123 k test lines. Of 50 merges: 33 changed library code or other
non-doc/non-test files (counted conservatively as "needs a decision"), 8 only tests, 9 only docs/tools/workflows.

| Selected share of test weight | mean (all 50) | median | mean (33 library-touching) | median (library-touching) | p90 (all) |
|---|---|---|---|---|---|
| direct (under-estimate) | 6.5 % | 0.3 % | 9.8 % | 1.9 % | 23 % |
| transitive (over-estimate) | 31.9 % | 0.3 % | 48.2 % | 7.7 % | 98.8 % |

Reading: the **median PR is cheap in either model** (docs/test-only PRs; most of them already skipped by `ci_scope.py` or
touching a handful of test files). The **mean is dominated by PRs that touch a core file** (`DMesh3`, `Vector3d`
dependents: 309 and 376 of 458 test files name these): in the transitive model any such PR selects 98.8 %. Real
line-level coverage falls between the models, because a changed *line* in `DMesh3.cs` is hit by the tests that run that
method, not all tests that use `DMesh3`; how far down it goes (for a typical core-file change) is exactly what the
per-class map measures and this probe cannot say. An honest planning range for the **library-touching** PRs is
10 % (direct) to 48 % (transitive) of the weight, central guess 20-30 % *(estimate, not measured)*; for the whole PR
population 7-32 %.

Per job (summed net10.0 test time of the full run, from the shard comment in `ci.yml`: a 2050 s, b 2113 s; the rest of
each job is fixed build/pack/smoke time that selection does not change). Applying the share uniformly to the shards,
which assumes areas are spread evenly (the Terrain/Meshing2 shard a is area-heavy; a PR in those areas moves one shard
only, and an exact-code PR is always full):

| job | full test time (s) | direct 6.5 % mean | central 25 % | transitive 32 % mean | notes |
|---|---|---|---|---|---|
| `linux` (net10.0, whole suite) | about 4160 (sum a+b) | 270 s | about 1040 s | 1330 s | plus fixed steps (pack, smoke, fuzz harness tests, benchmark builds) that stay |
| `netstandard` (shard a: Mesh2/Meshing2/Terrain) | 2050 | 130 s | about 510 s | 650 s | wall-clock was 14 min after the 2026-11-09 split |
| `netstandard-b` (rest) | 2113 | 140 s | about 530 s | 680 s | was at 32 min |
| `isolated` (Slow + backlog, 60 + tagged methods) | judged alone, about 5-10 s each | selected subset only | | | job is skipped when no Slow/backlog test is selected; on master unchanged |

Do not read these seconds as wall-clock: queue time (1-3 h) dominates the pain, and selection shortens the *job* but not
the number of jobs. The runner-hour saving is job time x share; the queue relief comes from the shorter occupancy and from
skipping the empty shard jobs' setup where the selection is empty (cannot skip the job itself: required check names).

Needed to turn this into a real number (one run on a machine with the SDK, 1 coordinator job): the nightly per-class map and
the trx of one full run; then replay the same 50 merges against the map (the replay script is the same selector as the
production one, so the bound is also the first test of it).

## 8a. Measurement (2026-11-13, local run with the SDK)

Done after the owner approved the measurement step. Tool: `tools/tia_map.py` (`collect`, `replay`), kept on the branch
`claude/test-impact-measure` (commit e71b63f, with its tests and the `tia-map` / `tia-replay` jobs for `coverage.yml`; not part of
this PR). The tool itself is exercised by 13 unit tests, 12 hand-made mutants of it are all killed.

Setup: linux, net10.0 Release, .NET SDK 10.0.115 (apt), 4 cores, one `dotnet test` per test class (468 classes of
`tests/geometry3Sharp.Tests`, `Category!=Nightly`, the Slow tier included) under coverlet 6.0.4 with `tests/coverage.runsettings`
(`SingleHit`); map made at master d2e02a1. Classes ran one after another, so their times are close to "alone", but under the
instrumentation. Every class produced a report; the host crashed (segfault in libc) in a first attempt that ran two coverage
processes in parallel, a single process had no crash and no `.missing` class; the tool retries a crashed class twice.

| Fact | Value |
|---|---|
| classes / test time summed (under coverage) | 468 / 4550 s (CI full run, summed net10.0 times: about 4160 s: same order) |
| of which shard a (Mesh2, Meshing2, Terrain) | 1624 s (36 %) |
| cost of the collection | about 20-35 s per class, 468 classes in about 3.5 h on 4 cores in one process (CI: four shards, about 1 h each) |
| size of the map | 8.2 MB as JSON (line ranges per class and file), 1.5 MB gzipped: no storage problem |
| heaviest classes | `RobustRepairFarPartTests` 221 s, `Meshing2OracleFastPathCheckTests` 187 s, `TerrainSlopeBandRepairTests` 171 s; the top 20 classes are 45 % of the time |
| failing tests in the map run | 0 |

Replay of the last 50 first-parent merges of master (HEAD d2e02a1) against that map. Selection as in sections 5 and 6, minus
the smoke set, the sampling and the "all tests of a changed test helper" rule (those only add): changed lines of the merge,
translated to the map's commit (a line that a later change touched falls back to its whole file), a class is selected when
it hits a changed line (line level) or covers the changed file (file level); a changed test file selects the classes it
declares; build-level files and exact code force everything. Weights are the measured per-class test seconds.

| Share of the summed test time that runs | mean | median | p75 | p90 | max |
|---|---|---|---|---|---|
| line level, the 41 merges that change code | 13.4 % | 0.4 % | 14.3 % | 29.1 % | 100 % (a forced-full merge) |
| file level (safe over-estimate of the line level) | 15.9 % | 0.4 % | | | |
| line level, all 50 merges (9 have no code change) | 11.0 % | | | | |
| line level, shard a (Mesh2, Meshing2, Terrain) / shard b | 12.2 % / 14.1 % | | | | |

Of the 41 code merges: 3 are forced full (build-level files or exact code: 100 %), 10 select no class of this suite (changes
to the CLI tests, the Clipper2 tests, the mutation tooling: their own suites run), the other 28 select 0.1 % to 61 %. The
largest line-level case that is not forced is a merge of 13 library files (60.8 %). Line level beats file level mostly on core
files (da929431: 100 % at file level, 60.8 % at line level; e48dd964: 59.4 % to 29.1 %).

Reading against the rule "go only if the selected share is <= 40 %": **the mean (13.4 % line level, 15.9 % file level) is far
below 40 %**, and all 38 merges that are not forced full stay at 61 % or less. The earlier planning range of section 8 (central guess 20-30 %) was
too high; the static transitive model (32 % mean) was the over-estimate it was labelled as, the direct model (6.5 %) too low.
Limits of this number, in the order of weight:
1. 50 merges is one window; the share is dominated by the few core-file merges (the mean is 13.4 %, the median 0.4 %). A window
   with more core changes (DMesh3, Vector3d dependents) is worse; the CI replay of the same script on a later master is the
   check.
2. It measures the share of the *test* time. The fixed steps of the `linux` job (pack, smoke, fuzz-harness tests, benchmark
   builds) and the build do not shrink, and the saving is in runner minutes, not in queue position.
3. Time weights come from the coverage run, where relative class costs differ somewhat from the plain run (instrumented loops
   are slower).
4. It says nothing about **safety**: the replay shows what *would* run, not what *would have failed*; no failing run of master
   was replayed, so the miss rate is not measured. That is what the shadow period (section 6) is for.
5. Windows shards, `#if` code and the net48 differences are not measured (net10.0 map; owner decision: `linux` only).
6. With the 10 % sampled full run (owner decision) the expected PR share is about 13.4 % + 0.1 x 86.6 % = 22 % of the test time.

## 9. Test plan (repo style)

New files only under `tools/` (no library change); CLAUDE.md "tools/tests/new files" level of local checks.
1. `tools/test_tia_select.py` (unittest, run in the `linux` job's CI-script step next to `test_ci_scope.py`): fixed fake
   maps; cases: line intersect selects; unrelated line does not; unknown file selects all; new class selects itself;
   changed test file selects its classes; changed helper selects all; build-level file selects all; exact-code path selects
   all; `#if`-file selects all Windows classes; stale map (older than N days / base not descendant) = full;
   script error / malformed map = full (fail-closed); empty diff = smoke set only. Mutation-check by hand as in #777: break
   each guard, a test must fail.
2. **Completeness test of the map** (`TiaMapCompleteness`): every test class listed by `--list-tests` appears in the map or
   in the always-set; every path a test project includes (`tests/*.csproj`, as `test_ci_scope.py` does) is in `SHARED` or
   the map's file list.
3. **Determinism**: the selector's output for the same inputs is byte-identical across runs and independent of dict order
   (run twice with shuffled input; sampling by head-sha hash only).
4. **Replay test** (script + CI summary, not a unit test): replay the last 50 merges; for each, run `selector` and compare
   against the PRs' real failed-test lists where known; expected miss count 0 on the 50, selected-weight table as in 8.
   This doubles as the upper-bound measurement.
5. **trx_durations.py**: new tests that a partial trx does not trip "backlog entry names no test" when a full listing is
   given; ratchet still fails for a >10 s test that ran.
6. **Live miss detector test**: inject a synthetic failure (a deliberately removed map entry) into a fixture and check the
   `tia_check.py` classification as "miss" and the auto-disable threshold.
7. Rollout proof: shadow mode first (selector runs, prints what it *would* skip, all tests still run) for N merges;
   enforcement only after the shadow miss count is 0 over those N merges. Owner decides N.

## 10. Risks

- **Missed failure merged to master** (the core risk): bounded by the backstop and the sampled full run, not eliminated.
  Mitigation: fail-closed, shadow mode, auto-disable on miss rate.
- **Map cost and rot**: a per-class coverage loop may cost more than the saved runner time if run too often; nightly only.
  The first `coverage.yml` scheduled run failed (platform.md), so the producer is not yet reliable: C depends on it.
- **Hub dependency**: the largest PRs (core files) gain nothing; the saving is on isolated-area PRs. Expect ~3-4x less
  runner time on those and 1x on the rest.
- **Windows/net48 divergence**: a net10.0 map cannot see `#if`-only code; handled by running whole shards for such files.
- **Complexity vs. the problem**: selection logic adds a failure mode into the very CI that is the safety net. A smaller win
  (option E, sharding `linux`, fewer queued jobs) may remove most of the queue pain with no safety trade.
- **Coverage tool behaviour under parallel tests**: per-class collection with coverlet needs the class run in its own process;
  `SingleHit` is fine (we need hit/no-hit). Methods compiled differently under instrumentation (inlining) are not an issue
  for hit sets but timing tests are (already excluded in `coverage.yml`).

## 11. Recommendation

**Option C at class granularity, introduced in shadow mode, with A as the first cheap layer and E in parallel.**
Order: (1) one measurement run with the SDK (per-class map + trx) to replace the estimate in section 8 -- decides go/no-go on
the actual share (go if the central estimate of library-touching PRs stays under ~40 %; otherwise do only A and E);
(2) build selector + tests (section 9) + shadow mode; (3) enforce on `linux` and the Windows shards for non-exact, non-`#if`
changes; (4) keep the full run on master and the sampled full run on PRs. Not B alone (unsafe or useless), not D.

## 12. Decisions for the owner

1. Is "a missed failure can reach master and is caught by the master/nightly run" acceptable? If not, TIA stays advisory
   (shadow only) and only A and E are worth building.
2. Go for one measurement run (coordinator job on a machine with the SDK, per-class coverage loop + one trx) before any
   code? (It also decides the 40 % go/no-go.)
3. Sampling rate of the full run on PRs (proposal 10 %) and the miss threshold that turns selection off (proposal 1 per 50
   merges).
4. Map producer: new nightly job, or extend `coverage.yml` (which has not yet run green)? Fix that first?
5. Windows shards: select there too, or run them fully and only select on `linux` (simplest, halves the gain)?
6. Is the shadow period N merges (proposal 20) acceptable?
7. Should option E (shard `linux`, run Windows shards only on ready PRs/master) be done first, independent of TIA?

### Owner decisions of 2026-11-13 (relayed by the coordinator session)

1. A missed failure caught later by master / nightly is acceptable. 2. A measurement run first; build only if the selected share
is <= 40 % (section 8a: 13.4 % mean, 15.9 % file level, from 468 classes and 50 merges: the condition holds on this window).
3. 10 % sampling, at most 1 miss per 50 merges. 4. Extend `coverage.yml` rather than a new workflow. 5. `linux` only.
6. 20 merges in shadow mode before anything gates. 7. Option E is mostly done by #781: drop what #781 covers.
**Update:** nothing is built until a real measurement on master data shows a saving worth having; this note is merged as a
docs PR. The measurement plan (which master trx files, which number decides) is in the PR description.
