# Quality assurance and test strategy

This page says how geometry3Sharp is tested, where the gaps are, and what is done about them, in order of
priority: robustness and correctness first, performance second. The review checklist that goes with it is
[REVIEW_CHECKLIST.md](REVIEW_CHECKLIST.md).

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
- A test that takes 5 s or more (measured in a full run) carries `[Trait("Category", "Slow")]`. A long test is split into
  test classes that xUnit runs in parallel (it runs the classes in parallel, but the tests of one class one after
  another), each class checking a share of the rows, with a test that the shares are exactly the old set
  (`RowGroups_PartitionTheCoveredRows`, `TagCheckRows_PartitionTheCoveredM3Rows`, `QueryMeshes_AreCoveredByTheOracleTests`).
- Draft PRs run the library tests with `--filter "Category!=Slow"` (Linux, and both net48 shards with `&Category!=Slow`
  added to their complementary filters). A PR that is ready for review (`ready_for_review` starts the run), every push
  to master and manual runs run all tests. `FULL_TIER` in `ci.yml` is the switch.
- The full run lists the slowest tests and the untagged tests of 5 s or more in its step summary
  (`tools/trx_durations.py`; a trx has no traits, so the tagged tests come from `dotnet test --list-tests --filter
  "Category=Slow"`). It fails for an untagged test of 60 s or more and only reports below that: durations on a shared
  runner are noisy, and a gate at 5 s would turn that noise into red CI. Tag a test that shows up in the list.
- The scheduled tier: `[Trait("Category", "Nightly")]` tests run only in `.github/workflows/nightly.yml` (daily on master, and
  `workflow_dispatch`; `dotnet test --filter "Category=Nightly"`, 90 min budget, durations in the step summary). PR and master CI
  exclude the category (`Category!=Nightly` on Linux and both net48 shards). It holds the rows above 10 s whose run is the check:
  the exact reference path of the 2D mesher oracle on its largest catalogue rows (`CatalogueRowsNightly`, `CorruptedRowsNightly`,
  and F-38/d of `ExtremeScales`). Also three K checks that are runs by nature, each with a reduced variant in the PR tier
  (`Solidify_IsDeterministic_Reduced`, `InsertionOrder_Reduced_DoesNotChangeTheResult`, `RemesherPro_FullRemesh_Reduced_...`). The Stryker filters of `tests/mutation` name classes of the exact arithmetic and do not
  contain these; a class that gets Nightly tests and is in a filter needs `&Category!=Nightly` there.
- The test time rule of CLAUDE.md (5 s, up to 10 s with `Slow` and a reason, nothing above) is a ratchet in the full run:
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
  1. defect generators and fuzzing of mesh inputs (2.1, 2.5);
  2. result validation in the existing success tests (2.3);
  3. mutation testing of `mesh_ops/ExactMesh*` (2.2; done, open gaps in tests/mutation/README.md) and the open
     `AdaptivePredicates` survivors;
  4. coverage (2.6);
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

## 2. Robustness and correctness, by priority

### 2.1 Invariance, reuse and determinism tests (#120: `tests/geometry3Sharp.Tests/Harness/Invariance.cs`)

A shared harness runs an operation on a procedural input, then on transformed copies, and compares the
results mapped back to the original frame.

**Similarities** (`Similarity.All()`):
- the identity (the reference);
- a translation to UTM (632117.25, 5712043.5, 113.75);
- a scale of 1e-3 and of 1e3;
- a rotation;
- a rotation, a scale of 1e-3 and the UTM translation together.
- **A scale by 2^-10 and by 2^10.** A power-of-two scale is exact in floating point, so an operation without
  absolute constants must give the same result, scaled, up to the last bits; most tests check it bit for bit
  (`Similarity.IsExact`). This catches
  absolute tolerances that a scale by 1e3 hides in rounding noise.
- The other similarities compare with a tolerance derived from the coordinate resolution
  (`Similarity.Resolution`), not with a hand-picked constant.

**Permutation of the input** (`Invariance.Permuted`): vertex order, triangle order and the starting corner of
each triangle.

**Reuse:** compute A, then B on the same instance, and compare with a fresh instance. This covers stale
caches, counters and status.

**Determinism:**
- Parallel equals serial where the class has a switch (`ParallelCompute`, `UseParallel`,
  `EnableParallel*`).
- Repeated runs give bit-identical output (`Invariance.Difference`).
- Where the id order may follow the thread schedule, the output must be the same set of triangles
  (`Invariance.GeometricDifference`).

**First findings:** four bugs within the first hour, one from the invariance tests and three from the
determinism tests.
- `MeshGeodesicDistance` regularizes its Poisson solve with an absolute `1e-8 * mass`. Its distances change
  by 7e-4 (relative) at a scale of 2^10.
- `ExactMeshBoolean` gives the same triangles in a different order from run to run: its output order depends
  on the thread schedule.
- `MarchingCubesPro` returns the double field value on the first evaluation of a grid corner and the float
  value afterwards, so its parallel result depends on the thread schedule.
- `MeshSignedDistanceGrid`'s parallel narrow band breaks distance ties by thread order.
- All four are fixed: the geodesic distances on closed meshes by the rework in #115 (the invariance test runs
  over all similarities since), the boolean order in #123, the last two in #124 and #125 (tests in
  ThreadingFixesTests). On open meshes the boundary splits of the intrinsic refinement place their concentric
  shells at absolute powers of two, so the distances still depended on the scale (4 % at 1e-3); fixed in
  #135.

**Not bugs:**
- `MeshAlphaWrap`'s relative alpha comes from the axis-aligned bounds, so it changes under a rotation. This
  is intended.
- `MeshGeodesicDistance` with `IntrinsicDelaunay` is discontinuous at cocircular quads: on the cube sphere,
  many of whose quads are exactly cocircular, a perturbation of 1e-9 (above the flip tolerance of 1e-10)
  decides the Delaunay flips differently and moves the distances by about 1 % (the lumped masses and
  per-triangle gradients differ; the cotan Laplacian is the same). Away from such ties the distances follow a
  perturbation one to one. Invariance tests of intrinsic methods therefore use an input without exact ties.
- `Invariance.VertexDistance` has rounding noise of about 1e-8 times the size
  (`Similarity.DistanceResolution`), because point-triangle distances come from squared distances.

**Rule.** Every new operation, and every operation a PR changes, gets an invariance test over
`Similarity.All()`, a reuse test if it has a `Compute()`, and a determinism test if it has a parallel
path. The tests run in the normal suite, so they gate every PR.

**Defect generators** (next step): procedural inputs for the degenerate, open, bowtie and nested cases of
the checklist, so that each operation runs over the same set of awkward inputs:
- zero-area ears;
- T-junctions;
- coincident vertices;
- slivers;
- an interior bowtie;
- touching sheets;
- tiny and nested components;
- a mesh with an unused far vertex;
- empty meshes and single triangles.

### 2.2 Mutation testing in CI for the exact parts

The exact arithmetic is the base of everything else, and a wrong error bound in it fails only on rare
inputs. Stryker.NET (Apache-2.0) mutates `math/ExactPredicates.cs`, `math/ExactPoint3.cs` and
`math/AdaptivePredicates.cs`, and runs the predicate tests against each mutant.

- A workflow reports the mutation score and the surviving mutants. It runs in full weekly and on demand, and on ready
  PRs only for the configs they touch ([tests/mutation/README.md](../tests/mutation/README.md)). A PR that changes a
  mutated file runs incrementally: Stryker mutates only the changed lines, the other mutants keep their result from the
  baseline of the last full run (its report, kept in the Actions cache, matched by a line diff), and the break
  threshold applies to the combined score of the shard; without a baseline the shard runs in full.
- A score below the break threshold fails a full run (a run for changed tests only reports). It starts at the measured score (rounded down, less one
  point for timeouts that vary between runners) and only goes up.
- Surviving mutants are either killed with a new test or recorded as equivalent, with a reason.
- The score is 87.05 % (October 2026, from 83.4 % at the start); the break threshold is 86. Details are in
  [tests/mutation/README.md](../tests/mutation/README.md).
- **The exact mesh pipeline** (October 2026): a second job, `exact-mesh`, mutates `mesh_ops/ExactMeshBoolean.cs`,
  `ExactMeshArrangement.cs`, `ExactPlanarSimplify.cs`, `ExactTriangulation2.cs` and `MeshRoundingRepair.cs` in six
  shards that run in parallel (1 to 5 hours each). The survivors led to three new test classes
  (`MeshRoundingRepairStrategyTests`, `ExactTriangulation2Tests`, `ExactBooleanFilterTests`) and cases in
  `ExactPlanarSimplifyTests`: each edit of the rounding repair on its own, the triangulation against the definition
  of a constrained Delaunay triangulation, the boolean's own filters against exact signs. Scores per shard, break
  thresholds, the equivalent groups and the open gaps are in [tests/mutation/README.md](../tests/mutation/README.md).
- Next targets:
  - the open gaps of the mesh shards (README);
  - the result checks of the repair, such as `NotCleanReason` and `MeshRepairMetrics`;
  - the open survivors in `AdaptivePredicates`.
- A survivor in a result check is a silent-success risk.
- This does not replace the manual mutation check that CLAUDE.md asks for: it covers code the manual
  check never revisits.

### 2.3 Result validation in success tests

Silent success is the most frequent fault class. Each test of a `Success` or `true` path checks the
result itself, not only the status:
- closed;
- no bowties;
- no self-intersections;
- no lost components;
- finite coordinates;
- volume and area within the expected bounds.

For mesh outputs, `MeshRobustRepair.NotCleanReason` is the standard check. The checklist also asks
whether a hard limit can end in a success status.

### 2.4 Differential tests against exact results

- The double predicates and filters are compared with exact rational evaluation (`BigInteger`) on
  randomized inputs.
- The randomized inputs have exponents spread from subnormal to 1e300.
- Some inputs are near-degenerate: almost collinear or almost coplanar, and points nudged by a few ulps.
- `ExactMeshBoolean` is checked against identities that hold exactly, for example
  vol(A ∪ B) + vol(A ∩ B) = vol(A) + vol(B).
- The randomized tests are seeded and run a fixed number of cases in CI, so they are reproducible and not
  flaky.
- A failing seed becomes an example test.
- Done for the predicates and the `ExactPoint3` conversions (#137: `PredicateDifferentialTests`,
  `ExactPoint3ConversionTests`). Boolean identities are covered by `ExactBooleanMetamorphicTests` and the seeded
  CsCheck campaigns in `ExactBooleanPropertyTests.CsCheck.cs` (2D mesher: `QualityMesh2PropertyCampaign.CsCheck.cs`, `G3_PROPERTY_SAMPLES`, `G3_PROPERTY_CAMPAIGN` / `G3_PROPERTY_REPLAY`, `G3_PROPERTY_STRICT`); the directed-family follow-up gives each
  box relation its own generated campaign (see `design/BOOLEAN_PROPERTY_TESTS.md`).

### 2.5 Fuzzing of the readers and of mesh inputs

The readers (OBJ, STL, OFF, PLY, 3MF, glTF) and the CLI / MCP server take untrusted input.

The fuzz tests are seeded and run inside the suite:
- They take valid files generated in the test, and mutate them: flipped bytes, truncation, duplicated
  blocks, huge counts, `NaN`, `1e400`, `1,5`, `../` paths.
- They assert that a reader returns a result or a documented error within a time and memory budget, with
  no other exception and no hang.
- Done for all readers (#127: `ReaderFuzzTests`). The fuzzing found the bugs fixed in #130, #131 and #136.

Mesh inputs to the operations are fuzzed the same way:
- random soups;
- random edits of clean meshes (flips, removed or duplicated triangles, welded vertices).

Each operation must return a documented status, never throw, and never report success on an output that
fails 2.3. Coverage-guided fuzzing (SharpFuzz, MIT): all readers (STL, OBJ, OFF, PLY, 3MF, GLB, .g3mesh, MiniJson, height grids, Triangle files, MCP requests) in
[ReaderFuzzPilot](../tools/ReaderFuzzPilot/README.md), replayed without a campaign by `ReaderFuzzReplayTests`, the mesh operations in
[MeshFuzzPilot](../tools/MeshFuzzPilot/README.md) (campaigns on a runner, not in CI).

### 2.5a Supported input range: correct inside, fail gracefully outside (owner 2026-11-09)

Fuzzers and random generators reach magnitudes no real input has (coordinates near 1e154, components below 1e-154,
a sphere of radius 1e100). Findings there are real robustness gaps, but they are not correctness bugs of the same
weight as a wrong result on a CAD assembly. So every operation is tested against two properties, split by a
documented **supported range**:

1. **Inside the supported range: correct.** The full checks apply (2.3, `NotCleanReason`, the operation's own
   guarantees, invariance under `Similarity.All()`). A failure is a bug of the owning area, fixed with a test that
   fails on the old code.
2. **Outside the supported range: fail gracefully.** No crash, no hang (time budget), no non-finite output spread to
   finite vertices, no false success: the operation returns a documented status or a documented
   `ArgumentException`. A crash or hang there is fixed if the fix is cheap (an early range check is usually enough);
   any other deviation is documented as a limit, not built for.

The range is set per module by the QA 2.0 range task (see the roadmap) and written in the module's class doc and in
one table here. It covers at least: the absolute coordinate magnitude, the ratio of the largest to the smallest
feature or component (scale disparity, the F1 class of the repair campaign), the triangle / vertex count, and
non-finite values (always outside). The default is derived from what the exact kernels and their bound tables allow
([design/FIXED_WIDTH_EXACT_POINTS.md](design/FIXED_WIDTH_EXACT_POINTS.md)) and from overflow of squared lengths
(about 1e154), with margin; a module may support less and says so. Generators of property campaigns sample inside
the range (with a bias toward its edges, where the scale-disparity bugs were found), and add a separate small
out-of-range sample for property 2. Fuzz harnesses report out-of-range findings in their own class.

**The ranges (2026-11-10).** "Magnitude" is the absolute value of each nonzero coordinate (zero is always fine);
"disparity" is the ratio of the largest to the smallest part or component of one input. Measured with a scale sweep
(a standard input scaled by 2^e, e from -1000 to 1000, 20 s budget per run; see the table notes), then set inside the
measured range with margin. Subnormal coordinates (below 2.2e-308) are outside for every module, and out of the 2^k
scale checks of the campaigns (the scaling is not exact there).

| Module | Magnitude (nonzero) | Disparity | Measured | Outside: status |
|---|---|---|---|---|
| `ExactMeshBoolean`, SafeRounding | 2^-200 .. 2^200 | 2^60 | cubes: ok 2^-250 .. 2^500; empty `Success` below 2^-280; invalid rounding above 2^700 | `Failed` / range status (PR per module) |
| `MeshRoundingRepair` | 2^-200 .. 2^200 | 2^60 | invalid at 4e176 (FINDINGS 6) | not clean, honest (documented limit) |
| `MeshRobustRepair` | 2^-200 .. 2^400, extent >= 2^-40 of the largest magnitude | any, reported: `ScaleDisparity` / `LostFarPart` (`Apply()` false) | spheres: ok 2^-250 .. 2^500, hang at 1e176 (FINDINGS 3) | `Apply()` false with a reason |
| `MeshAlphaWrap` | 2^-128 .. 2^128, extent >= 2^-40 of the largest magnitude | 2^40 | ok 2^-160 .. 2^160; hang at 2^+-180/200; `Failed`/`InvalidParameters` from 2^+-220 | `InvalidInput` |
| `Reducer` | 1e-150 .. 1e150 (the extent to the 4th must not matter: a power-of-two frame) | -- | sphere: the same relative result at every power-of-two scale; below about 1e-160 the cross products of the flip test underflow: nothing is collapsed, the mesh is left unchanged; above 1e150 a collapse whose cross products overflow is refused (closed and finite, possibly not reduced) | not guaranteed (documented limit) |
| `Remesher`, `PartitionedReducer` | 2^-200 .. 2^200 (`PartitionedReducer` also in the Reducer's range) | -- | sphere: ok 2^-250 .. 2^500 | not guaranteed (documented limit) |
| `DMeshAABBTree3` ray, `IsInside`, winding numbers | up to 2^1000 | -- | fuzzed to 2^1000 | not guaranteed above |
| `DMeshAABBTree3` nearest (squared distance) | extent 2^-450 .. 2^450 | -- | squared distance representable | not guaranteed outside |
| 2D mesher (`DelaunayMesh2` and the constrained variants) | up to 1e70, any small magnitude | -- | exact predicates | `ArgumentException` |
| `TerrainVolumeDifference`, `TerrainOffset` | <= 2^200, extent 2^-8 .. 2^200 | -- | bumpy 8 x 8 terrain: correct 2^-20 .. 2^250; zero volumes with `true` below 2^-25 and above 2^260; offset wrong with `true` above 2^260, `false` below 2^-30 | `Compute()` false (PR per module) |
| Other terrain modules (grid TIN, greedy TIN, slope map / design / overlay, dual contouring, safety) | not swept: the slope classes decide with the exact kernel (`ExactSlopeKernel`) | -- | -- | open |

The ranges are class-doc statements ("Supported input range") of the classes above (those of `ExactMeshBoolean` and
`MeshRoundingRepair` come with their range-check PRs: the mutation shards address these files by character offset). The checks that turn the
"Outside" column into a status land per module (bugfix PRs); until then the column is the documented behaviour of
the module outside the range, or "not guaranteed".

### 2.6 Coverage in CI

- coverlet.collector (MIT, pinned 6.0.4) collects Cobertura coverage in its own workflow, `.github/workflows/coverage.yml`,
  **not on pull requests**: coverlet makes the suites about 3x slower (a first version in the PR jobs took 59 min for
  the core fast tier alone, and eight parallel shards were limited by the runner quota), so the gate runs cyclically
  (owner decision 2026-11-10): every night on master (`schedule`) and on demand (`workflow_dispatch`, optional `ref`
  input: a branch is then checked against the last evaluated master run) and once on a pull request that changes the workflow or the gate (that run reports but never fails: its findings belong to other pull requests). The existing Microsoft.NET.Test.Sdk 17.8.0
  supplies the integration; Windows/net48 runs do not collect coverage.
- The `core` jobs run the core suite in four shards by test class (`tools/coverage/shard_filter.py`), **including the
  Slow tier** (nobody waits for this run), without Nightly; the `gate` job runs the CLI and Clipper2 package suites and
  merges the reports (per line the maximum of the hits; several reports per suite are shards). Two tests with a
  wall-clock budget (`RobustnessSweepTests.Sweep_SameResultUnderSimilarity`, `UVAtlasTests.UVOverlap_MixedSizes_NotQuadratic`)
  fail under the instrumentation and are excluded **in this run only**; the normal CI jobs keep enforcing them. A failing
  test does not fail a collection step (a missing report fails the gate); `SingleHit` in `tests/coverage.runsettings` (one hit per line is all the gate needs) avoids coverlet's per-hit counters, which made one shard of the first run exceed 150 min; `--blame-hang-timeout` names a hanging test.
- `tools/changed_line_coverage.py` requires **100% of the changed executable lines** in the core library,
  `addons/geometry3Sharp.Clipper2` and `tools/g3cli` to have at least one hit across those runs. Existing unmodified gaps do
  not fail it. There is no whole-library or branch-coverage threshold.
- The comparison base is the HEAD of the last run that evaluated its lines (its `gate-result` artifact, uploaded for exit
  code 0 and 1, not after incomplete evidence), else the master of 24 hours ago. With `--attribute` the report names, per
  file, the pull request that introduced each uncovered line (git blame, then the first-parent merge that brought the
  commit in, else the `(#N)` of a squash subject, else the short hash). The job summary lists them and the scheduled run
  fails when there are any. **There is no exemption mechanism** and a nightly report blocks no pull request: uncovered
  lines become follow-up work (a test) for the owning session. A line that cannot be hit by a test (defensive code) is
  discussed in that follow-up, not excluded by a marker.
- Limits, stated so that nobody reads more into a green run than it says: (1) only lines with a sequence point are
  measured; changed lines without one (continuation lines of a statement, code in a conditional-compilation branch the
  test build does not compile such as `#if NETSTANDARD2_0`, methods with `[ExcludeFromCodeCoverage]`) are listed as
  `unmeasured_lines` in the JSON and counted in the summary, not failed; (2) hits are merged by maximum per file and
  line over all class records and suites, so a line that several class records list (field initializers copied into each
  constructor, generic instantiations, iterator state machines) counts as covered when one of them ran; (3) a renamed
  or moved file is checked as new code (`--no-renames`), its old gaps are reported and attributed by `git blame` to the
  PRs that wrote them; (4) a finding is reported once: the next run starts at the HEAD of this one, so lines nobody
  covered do not come back unless the work-list from the artifact is kept; (5) `--expect-shards core=4` makes a core
  shard without a report incomplete evidence (exit 2), but a test class that matches no shard filter would be missed
  (it shows as uncovered lines, never as a false pass).
- The base is only accepted from a run whose HEAD is an ancestor of master (a manual run of a branch on master does not
  move it). Added files and renames are checked as added lines; deleted lines are ignored. Coverlet's class-level line records
  define executable lines, so comments and whitespace within a measured file need no hits. Missing, malformed or empty suite reports fail the gate (exit 2). A changed source file that no report lists (`files_without_executable_lines` in the JSON; the first run found `core/ExactCodeAllowList.cs`, a data table) has no executable code, because coverlet lists every method of the instrumented assemblies, hit or not: it is reported and does not fail the run. Tests, benchmark code, documentation examples, assembly metadata
  and build tools are outside this gate. The unit tests of the gate run in the `linux` job of `ci.yml`.
- Optional `--slow-coverage-dir` (a second core report over `Category=Slow` only) reports lines that only the Slow tier
  hits as `covered_only_by_slow`; the cyclic run does not need it, because it collects the Slow tier with the rest.
- Local reproduction from a clean committed checkout, using the same SDK as CI:

  ```sh
  rm -rf test-results
  dotnet test tests/geometry3Sharp.Tests -c Release --filter "Category!=Nightly" --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory test-results/core
  dotnet test tests/g3cli.Tests -c Release --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory test-results/cli
  dotnet test tests/geometry3Sharp.Clipper2.Tests -c Release --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory test-results/clipper2
  python3 -m unittest discover -s tools -p 'test_changed_line_coverage.py' -v
  python3 tools/changed_line_coverage.py --base origin/master --attribute --coverage-dir test-results --json test-results/changed-line-coverage.json
  ```

  (the core run takes about an hour with coverage; `--filter` on the classes of the changed files is enough locally).
  Exit codes: 0 covered/no changed executable lines, 1 uncovered lines, 2 incomplete or invalid evidence. Use a fresh
  results directory: a stale report left in it would mask a gap. Coverage demonstrates execution, not assertion quality;
  mutation gates remain.

### 2.7 A small regression corpus as a CI smoke benchmark

The Thingi10K benchmarks cannot run in CI, because of the license and the run time.

A procedural corpus runs instead, in a few minutes:
- generated defect meshes from 2.1;
- the cube grid of the boolean benchmark;
- terrain grids at UTM coordinates.

It runs the main pipelines and checks the following against stored thresholds:
- the pipelines: repair, robust repair, exact boolean, alpha wrap, remesh / reduce;
- quality: clean result, volume error, Hausdorff distance;
- run time: generous, to catch only order-of-magnitude regressions on shared runners.

Meshes with a permissive license can be added if a case cannot be generated.

**Done, first part (October 2026): `RegressionCorpusTests`.**
- The corpus is the defect cases of 2.1 (`Harness/Defects.cs`: 20 fixed cases, 12 seeded random edits), at the
  origin and at UTM coordinates: 64 meshes, all generated, no mesh file.
- Seven pipelines run over it: `MeshAutoRepair`, `MeshRobustRepair`, `ExactMeshBoolean` (union with a box, and
  `Resolve`), `Remesher`, `Reducer` and `MeshAlphaWrap`.
- The outcome of a case is the result's `MeshRobustRepair.NotCleanReason`, or "clean" and the volume (1 %
  tolerance). A table in the test stores the outcome of every case and pipeline; a change in either direction
  fails, with the list of changed cases and the counts of clean results. An improvement updates the table
  (`G3_CORPUS_PRINT=1` prints it).
- Counts of clean results (of 64): auto repair 46, robust repair 62 (the two empty inputs fail as documented),
  union with a box 39, resolve 31, remesh 12, reduce 12, alpha wrap 62.
- It is a test class of the suite, not a CI job of its own: the suite already runs in CI on net10.0 and net48,
  and a job of its own would build everything again for about a minute of work. The cases run in parallel; the
  class takes about a minute on 4 cores, most of it in three robust repairs ("flat", "partly flipped", "single
  triangle") that take the solidify path.
- `DefectInputTests` checks that each operation keeps its documented guarantees; the corpus checks what the
  operations achieve. It caught both mutations of the check (orientation repair of `MeshAutoRepair` removed, alpha
  of `MeshAlphaWrap` changed), which `DefectInputTests` lets through.
- Open: the cube grid of the boolean benchmark and terrain grids (both have their own tests, but no stored
  outcomes), the Hausdorff distance, and run-time limits.

### 2.7a Data sets beyond Thingi10K (benchmarks, by hand)

The benchmarks (`benchmarks/`) use Thingi10K (repair, booleans, UV atlas), the boolean benchmark's cases
and NRW terrain (dl-de/zero). Thingi10K covers broken 3D-print meshes; other kinds of input fail
differently, so a result tuned on Thingi10K alone can be misleading. As for Thingi10K, every data set is
fetched by a `fetch_data.py` of the benchmark, never committed (CLAUDE.md), and its source and license
are listed in the benchmark's README. The license of every object is checked before it is used.

| Data set | Kind of input | For | License (checked October 2026) | Priority |
|---|---|---|---|---|
| [Objaverse-XL](https://huggingface.co/datasets/allenai/objaverse-xl) / Objaverse | game, scan and CAD exports from Sketchfab, GitHub and others: open surfaces, many parts, wrong normals | a second repair and wrap corpus | ODC-By for the collection; each object has its own license (metadata column `license`): keep CC0, CC-BY and permissive code licenses, drop NC, GPL and Polycam objects (non-commercial) | 1 |
| [ABC](https://archive.nyu.edu/handle/2451/43778) (1 million Onshape models) | CAD: sharp edges, thin walls, exactly coplanar and cocircular faces | exact booleans, `ExactPlanarSimplify`, ties of the intrinsic Delaunay | the release is MIT; the models belong to their creators under the Onshape terms of use (section 1.g.ii, not checked): read before use | 2 |
| [swissALTI3D](https://opendata.swiss/en/dataset/swissalti3d) | high mountains, cliffs | terrain dual contouring, TIN, safety lines | open use, also commercial, with attribution to swisstopo | 3 |
| [Keenan Crane's model repository](https://www.cs.cmu.edu/~kmcrane/Projects/ModelRepository/) | smooth reference meshes (Spot, Bob, Blub) | parameterization, geodesics, remeshing; comparable with the papers | CC0 | 4 |
| Benchmark of "Lifting Simplices to Find Injectivity" (Du et al. 2020, Zenodo 3827969) | 10,743 hard parameterization cases | `parameterization/`, UV atlas | not confirmed yet: check the Zenodo record before use | 5 |
| Stanford 3D Scanning Repository | scans (bunny, dragon, Armadillo) | reference only | no license; the page asks not to apply boolean operators to the models, among others (to be confirmed on the page): not for boolean or repair benchmarks | — |

Not used: ShapeNet and ModelNet (non-commercial research licenses).

### 2.8 API compatibility

This is in place since #92: pack validates the public API against 1.0.324. Accepted breaks are listed in
`CompatibilitySuppressions.xml`, and changed results in [UPSTREAM_CHANGES.md](UPSTREAM_CHANGES.md).

### 2.9 Minimizing a failure

A failing defect or fuzz case (hundreds of triangles, kilobytes of a file) is reduced automatically to a small one
that shows the same error, by delta debugging (Zeller and Hildebrandt 2002, ddmin; `Harness/DeltaDebug.cs`,
`Harness/MeshMinimizer.cs`).

- `MeshMinimizer.Minimize(mesh, fails, options)` takes a mesh and a predicate "the error is still present". The
  steps, coarse to fine, repeat until nothing changes: drop the attributes, the connected components, ddmin
  over the triangles down to single ones (the result is 1-minimal), the unused vertices, then round the
  coordinates of the triangles' vertices to power-of-two grids relative to the extent (all together, then vertex by vertex), so the case
  becomes readable.
- `MinimizeOptions`: a time budget (the best case so far is returned, status `Stopped`), a timeout per call, a
  seed for the order of the subsets. A predicate that throws or does not finish is a different error, not the
  one being minimized, unless `ExceptionMeansFailure` / `TimeoutMeansFailure` say so.
- The result is a C# method (`MeshMinimizer.ToCSharp`) to paste into a test as a procedural fixture (mesh files
  are not committed), and an OBJ in `.g3-minimized/` (git-ignored) to look at.
- `DefectInputTests` and `ReaderFuzzTests` minimize their failures with `G3_MINIMIZE=1`
  (`Harness/FailureMinimizer.cs`): the message of the failing test gets the minimized case. "The same error"
  is the exception type and the first line of its message with the numbers masked. Reader inputs are
  minimized by lines (text formats), then by bytes. `G3_MINIMIZE_SECONDS` sets the budget (default 120),
  `G3_MINIMIZE_ROUND=0` keeps the coordinates.

```
G3_MINIMIZE=1 dotnet test tests/geometry3Sharp.Tests -c Release --filter "FullyQualifiedName~DefectInputTests"
```

Rounding can turn a case into a degenerate one with the same message (ddmin finds the smallest input with
that message, not necessarily with the same cause). Check both the rounded and the unrounded case. Example:
"random edits 6" (301 triangles) became 5 triangles in 10 s
(`DefectInputTests.MinimizedRandomEdits6`), unrounded 34 (`MinimizedRandomEdits6Unrounded`). Here both had the same
cause, an open boundary on an edge that other pieces meet, in different shapes: the rounding made three triangles
coplanar (with the sign of zero as the only difference between coincident vertices), the unrounded case kept the
stray triangle that the comment of the original test called an interior sheet.

### 2.10 Robustness sweep (`RobustnessSweepTests`, findings in [ROBUSTNESS_SWEEP.md](ROBUSTNESS_SWEEP.md))

A table of public operations (mostly upstream code), each with a procedural input of size 1, its scalar
parameter (a length, if any) and a signature of its result in the input's units (counts, lengths, areas,
volumes, points, directions). Every operation runs:

- under `Similarity.All()` plus scales of 2^-24 and 2^20. Under power-of-two scales the signature must be bit
  identical, which catches absolute thresholds. Under the other similarities it must agree within the
  operation's tolerance, which catches float code at UTM coordinates. An operation whose result varies in the
  last bits from run to run would set `BitExact = false` (none does since R18 and R19 are fixed).
- with a NaN or +Inf vertex, and a NaN or ±Inf parameter. The operation must finish within 20 s. It must not
  throw, except an `ArgumentException` from its own type. It must not spread non-finite coordinates to
  vertices that were finite. With a non-finite parameter it must report failure or give a finite result.

Known failures are listed in `RobustnessSweepTests.KnownFailures` with their finding id. The list is strict: a
listed case that passes fails the test, so a fix removes the entry. Two exceptions: known hangs are not run
in the normal suite, so they are not strict, and on .NET Framework the list is not strict because it was taken
on net10, where double-to-int casts saturate. Operations without an output mesh report their per-vertex values
(normals, UVs, distances) with `Sig.PerVertex`, and the spreading check covers those values too.
`Harness_*` tests check the harness itself: an invariant operation passes, and an absolute threshold, a float
position, a value with the wrong unit, spreading and a deep exception are all found.
`G3_SWEEP_REPORT=1` prints the outcome of every case, hangs included, instead of asserting. Hanging threads
keep running until the process exits:

```
G3_SWEEP_REPORT=1 dotnet test tests/geometry3Sharp.Tests -c Release --filter "FullyQualifiedName~RobustnessSweepTests" --logger "console;verbosity=detailed" | grep SWEEP
```

To add an operation, add an `Op` to `RobustnessSweepTests.Ops`, run the report, and list its failures as
findings.

### 2.11 Hard cases from other libraries' issue trackers

Libraries that implement the same algorithms (booleans, repair, remeshing, simplification, parameterization,
geodesics, hole filling, triangulation) have collected years of failure reports from real inputs. Their issue
trackers are a list of hard special cases that our own tests may not think of.

How:
- Per operation, search the issues of the leading libraries (eg libigl, geogram, PMP, cinolib, geometry-central,
  Manifold, trimesh, MeshLib, CGAL) for reported failures: crashes, wrong results, hangs, non-manifold output.
- Take only the failure mode (the geometric situation), never code. Rebuild the input procedurally in a test; do not
  download or commit the meshes attached to issues (their licenses are unknown, see CLAUDE.md).
- From GPL projects (CGAL, Easy3D) and source-available ones (MeshLib), read only the description of the
  situation; do not copy code, patches or test code from them. Fixes here come from papers or from MIT / Apache /
  BSD sources, as always.
- A case we fail becomes a finding with a test (strict known-failure list as in 2.10, or fixed right away); record
  the issue URL in the test's comment so the case can be traced.

This is a release criterion (ROADMAP, Release): the library should pass the cases that its peers fixed or
documented.

**Booleans (October 2026):** `ExactBooleanIssueCasesTests` rebuilds 13 failure modes from the trackers of Manifold,
CGAL and libigl: shared faces in random orientations, two prisms that form a box, flush cuts, skewed prisms on a
shared face, a sphere minus its lens with a shifted copy, batch unions at 3.6e8 against sequential and reversed order,
one box triangulated two ways, a 130-level rotate-and-union spiral, tiny rotations, mirrored inputs, cavities, a
stepped cylinder tool with rounding between the steps, inside-out operands. `ExactMeshBoolean` gives clean, correct
results on all of them. Known failure (pinned in `KnownLimitation_TwoTriangularPrisms_RotatedInexactly_KeepATinyVoid`):
when the shared face of two inputs is not exactly planar after an inexact rotation, its two triangulations enclose a
tetrahedron of about 3e-15 volume, which the exact union keeps as a void touching the outer surface along edges
(volume right, non-manifold when welded by position). This regression pins the rounded coordinates rather
than generating them with runtime-dependent `Sin`/`Cos`: net48 and net10 quaternion rotation produce
different exact input solids. `TwoTriangularPrisms_RoundedRotationWithoutVoid_FormOneClosedSolid`
pins the other observed rounding and requires a clean, closed single-component result. The limitation
assertions are unchanged; there is no runtime-conditional expected outcome.

**Mesh repair (November 2026):** `MeshRepairIssueCasesTests` rebuilds 15 failure modes from the trackers of CGAL,
PMP, libigl, Manifold, Open3D and MeshLib: holes of one triangle and planar square holes (closed exactly), a quad
hole whose diagonal is already an edge of the mesh, several holes at once, the open base of a cone (filled flat), a
hole next to a near-zero-area rim triangle, a hole boundary that touches itself, the smoothed fill on models 1e-3 and
1e3 in size (the same triangles, bit for bit under 2^k), a sphere cut open along a meridian, two halves with a narrow
gap, two open boxes touching at a hole corner, a ring of boxes whose last one nearly coincides with the first, a
single flipped triangle, a thin fold of two boxes crossing at 3 and 10 degrees, and a chain of overlapping boxes.
Situations the defect cases of 2.1 already cover (soups, inside-out shells, cavities, overlapping solids) are left
out. Every case checks the result (`NotCleanReasons`, the volume, the bounds, the triangle or component count). The
large holes (a cap, the quad hole) use `MeshAutoRepair`: `MeshRobustRepair` compares a repair with the winding number
solid of the open input, and a large fill differs from that by more than `MinVolumeIoU`, so it remeshes the input
instead (by design). One fix, with a test that fails on the old code: `MinimalHoleFill` ended a quad hole on the
existing diagonal, which `RegionOperator.BackPropropagate` then split at its midpoint, so the fill touched the
existing edge (pmp-library#181); such edges between two loop vertices are now flipped to the other diagonal where
that one is new and no normal turns around ([UPSTREAM_CHANGES.md](UPSTREAM_CHANGES.md)). Finding R1, fixed:
- **The bug.** `MeshPostRepairCleanup.FindInvertedComponents` took a closed component whose interior lies mostly
  inside another for a nested one, also when their surfaces cross. So of two outward boxes crossing at 3 or 10
  degrees, one was flipped as a cavity, and the repair returned their difference as a clean result. The volume check
  accepted it, because the input's reference solid uses the same nesting rule.
- **The fix.** A component whose surface meets (exactly) that of a component its samples lie in has no nesting
  at all: crossing components keep their orientation (they are never flagged).

The
analysis and fix ideas of the findings are in [ISSUE_CASE_FINDINGS.md](ISSUE_CASE_FINDINGS.md) (also R2, a large
hole fill rejected by `MeshRobustRepair`'s volume check, by design).

**Alpha wrap (November 2026):** `AlphaWrapIssueCasesTests` rebuilds 9 failure modes from the trackers of CGAL,
Open3D, MeshLib and Manifold: separate components (one shell each, at two offsets, CGAL#7625), components closer
than alpha (one shell), overlapping closed inputs and nested shells (one shell), a cube at a coarse alpha (corners
within offset + 2 alpha, the triangle count bounded), lattice point clouds with cospherical points, nearly cospherical points at
2.8e6, a too small alpha (stopped at the point limit with a closed, enclosing result) and a two-sided sheet of zero
thickness. Every case runs `AlphaWrapTests.CheckWrap` (all guarantees, distances against a brute-force oracle);
`MeshAlphaWrap` passes all of them.

The `KnownFailures` lists of the `*IssueCasesTests` classes, and the wrong outcomes they pin, are taken on net10; on
.NET Framework they are not strict (`Harness/IssueCases.cs`), as in 2.10. The findings, with their causes and fix
ideas, are in [ISSUE_CASE_FINDINGS.md](ISSUE_CASE_FINDINGS.md).

**Geodesics and intrinsic triangulations (November 2026):** `GeodesicsIssueCasesTests` rebuilds 11 failure modes from
the trackers of libigl, geometry-central and potpourri3d that the existing tests did not cover (cases that duplicated
them, such as loops around a cone apex, paths that double back and flat grids with a corner source, were left out or
reduced to their new part): two disjoint triangles in the vector heat method, a source inside a face (poked, also
1e-9 from an edge), an open box with rim sources, a disc with a boundary source, a Gaussian bump traced from face
points, a hilly terrain 1500 edges long (transport, log map, extension), the coarse square and kite against their
refinements, a reversed edge path, a 1.69 degree wedge and needle in the Delaunay refinement, slivers down to zero
width in the vector transport, and a reversed triangle. Distances are checked against |p - s| or exact (FlipOut) paths,
the heat method within its documented accuracy. Fixed: `MeshGeodesicPath.Path` repeated the end point of an edge
traced exactly through a vertex (G1); `MeshGeodesicPath` reported `TraceFailed` for edges of 1.25e-4 at UTM
coordinates, its trace tolerance now has a floor of 16 eps times the largest coordinate (G3); tangent frames at
coincident vertices were arbitrary, so `ToWorld` of transported vectors was off by up to 135 degrees there, and a
zero-length edge now gets the world angle interpolated between its neighbours (G2,
[ISSUE_CASE_FINDINGS.md](ISSUE_CASE_FINDINGS.md)).

**Parameterization (November 2026):** `ParameterizationIssueCasesTests` rebuilds 12 failure modes from the trackers of
libigl, boundary-first-flattening and xatlas, each over LSCM, ARAP, SLIM, Tutte, BFF (default and Disk) and the atlas
methods: closed icospheres, several components and holes, unused vertices (also at 1e7), degenerate patches, the
orientation (facing -z, in the plane x = 0, with pins), a three-peak terrain, a low-poly cylinder without a seam, two
identical holes (symmetry), saddles, a long thin triangle in the plane x = 0 (and a needle quad with both diagonals),
packing with rotated charts, and a thin tall box. Success is checked by finite UVs, the surface's orientation, flips where a method
promises none, overlaps, disc charts with uniform texel density. Fixed: BFF Disk threw an `ArithmeticException` on a
needle quad whose boundary scale factors overflow, now `SolverFailed` (P4); LSCM (and ARAP from it) collapsed the
unpinned ring of a low-poly cylinder and reported success, now LSCM and ARAP report `FlippedTriangles` (with the map)
for a map with flipped or degenerate UV triangles (P1; the collapse itself stays). Known failures
([ISSUE_CASE_FINDINGS.md](ISSUE_CASE_FINDINGS.md)): BFF Disk with the conformal extension does not keep the boundary on the circle on
meshes without interior vertices, and on two needles gives a flipped sliver (P2). By design: LSCM with automatic pins is
asymmetric by 1e-4 on a symmetric patch (P3: the minimizer depends on the pins, and the farthest pairs are two mirrored
diagonals at the same distance, so no rule on the geometry alone picks a mirrored pair; bounded at 1e-3 in the test).

**Simplification (November 2026):** `ReducerIssueCasesTests` rebuilds 13 failure modes from the trackers of
meshoptimizer, libigl, Manifold and CGAL: a noisy terrain reduced to 20%, a 24-triangle patch reduced to 10, almost
flat grids (singular quadrics), a flat and a curved grid at z = 900 and at UTM offsets (translation), a pinched 4 x 1 x 1
body reduced to 1 triangle, a 100 x 20 torus reduced coarsely (genus), a pyramid with a sliver base, a box-cylinder
union rotated by -89.999999999999 degrees, a sphere with 50 tiny cylinders (unreachable target), an unwelded quad,
two cones tip to tip, self-intersecting input, and a target already met. The results are checked for validity,
closedness, folds, bounds, volume, Euler characteristic and components. Fixed: the Reducer joined the fans of a bowtie
vertex (and created duplicate triangles); a bowtie vertex is now kept in place. Fixed too, S1
([ISSUE_CASE_FINDINGS.md](ISSUE_CASE_FINDINGS.md)): chains of collapses folded a noisy terrain and stood triangles up
at an open border, because the flip test compared with the current, not the input normals. `Reducer.PreventFoldOver`
(default on) now also checks against the absorbed input normals (`ReducerFoldOverTests`).

**Remeshing (November 2026):** `RemesherIssueCasesTests` rebuilds 10 failure modes from the trackers of PMP,
geogram, CGAL and MeshLib (Remesher, RemesherPro, RegionRemesher): a flat non-convex fan with fixed boundary, open
surfaces with the recommended boundary constraints, nearly touching sheets with PreventSelfIntersections, a partial
remesh with locked vertices (bit-exact), zero-area triangles and kinks, slivers and needles (termination), a degenerate
face on the border, two cones tip to tip, long thin sharp parts, and a creased L prism (bounds). The box corners of
pmp-library #132 are already covered by `FeaturePreservationTests`. Fixed: `PreserveBoundaryAndFeatures` now pins
bowtie vertices (the remesher joined the two cones). Known failures: S2, without the opt-in `MinTriangleQuality`
smoothing folds the fan and leaves a flat triangle on a crease of a long thin box (with 0.2 they pass); S3, without feature
constraints a coarse L prism loses a leg (volume 3 -> 0.73, bounds shifted by 1).

**Point, ray and inside queries (November 2026):** `SpatialQueryIssueCasesTests` rebuilds 12 failure modes from the
trackers of libigl, trimesh and Open3D: a triangle fan around a ray under all 48 axis permutations and sign flips,
rays grazing box faces and edges (unit, 2^20, UTM), vertical rays on a mesh scaled by 2^k and 1e-2 ... 1e-6, rays onto
the shared diagonal and vertices of a split square, parity rays exactly through a vertex or an edge, signed distance
grids whose sign rays run along the edges of a cube and over a sphere on a 64^3 grid, a query exactly on a face, doubled,
overlapping and cancelling shells (generalized winding number), a cube with a zero-length edge and needles, a mesh
with vertices but no triangles, and point in polygon at 4e5, 2^26 and UTM against the same rule decided exactly. Ray
casts are compared with a brute-force loop over the same leaf predicate, so a difference can only come from the tree:
the tree equals brute force everywhere (the 0 * inf slab case of libigl does not occur), and the grids, the winding
numbers and `Polygon2d.Contains` are right. Fixed: `FindNearestHitTriangle` picked among equal-t triangles by traversal
order, which the absolute box padding changes below about 1e-6 model size (Q1; ties now go to the lowest id);
`IntrRay3Triangle3` was not watertight, so a rounded ray onto a shared edge could miss both triangles (Q2, at 1e-3 and
1e-7; now decided by exact `Orient3d` signs against the closed triangle), and `IsInside` counted vertex hits 0 times
and edge hits twice (Q3; now the crossings of a ray perturbed by Simulation of Simplicity, each counted once, the same
test as the exact winding number of `VolumeSampleCells`). Findings in [ISSUE_CASE_FINDINGS.md](ISSUE_CASE_FINDINGS.md);
`ExactRayTriangleTests` checks the perturbation against big integers, parity and hits through the vertices and edges
of a closed grid cube along 29 directions, and invariance over `Similarity.All()`.

## 3. Performance

### 3.1 Micro benchmarks with regression detection

BenchmarkDotNet (MIT) covers the kernels:
- the exact predicates;
- AABB tree queries;
- the fast winding number;
- the exact arrangement on a fixed input;
- reducer and remesher passes.

It runs by hand and in a scheduled workflow. The results are compared with the last stored run. Shared CI
runners are noisy, so only large changes (more than 20 %) are flagged, and a performance claim in a PR
still needs before/after numbers from one machine (CLAUDE.md).

### 3.2 Complexity tests

Each operation with a nested loop over the input gets a scaling test, which counts work at n and 4n
(candidate pairs, visited cells, or time with a wide margin). It is cheaper and less noisy than timing.
Past cases: #63, #72, #105.

Wall-clock budget tests must not compete with other test collections in the same runner. The collapsed-corridor
oracle check remains in `QualityMesh2CollapsedPartsReportTests` with its unchanged input, result assertions and
budget, but that class uses the `Meshing2OracleTiming` collection with `DisableParallelization = true`.
The scheduling regression in `RobustnessSweepRemainingTests` checks both the assignment and opt-out metadata.
This uses xUnit's [collection-level parallelism opt-out](https://xunit.net/docs/running-tests-in-parallel), not a
suite-wide concurrency change; it does not eliminate noise from unrelated processes or other test assemblies.

## 4a. Making agent work verifiable

Most rules in CLAUDE.md are enforced by review only. These items make them machine-checked, or add oracles that do
not depend on the implementer's own tests. Order of priority:

1. **New tests fail on the old code**: a CI job runs the tests a PR adds against the base branch; for a bug fix they
   must fail there. Catches vacuous tests (one was found in review in #186). Done: workflow `New tests on base`
   (`.github/workflows/new-tests-on-base.yml`, `tools/new_tests_on_base.py`, locally
   `python3 tools/new_tests_on_base.py --base origin/master`). New tests are the methods of
   `tests/geometry3Sharp.Tests` and `tests/geometry3Sharp.Clipper2.Tests` (against the addon of the base) that the
   merge base does not have (from `dotnet test --list-tests`). They run in a
   worktree of the head whose library files (all paths outside `tests/`, `benchmarks/`, `docs/`, `tools/`,
   `.github/` and the root `*.md`) are reset to the merge base; a changed test file that does not compile against
   the old library (new API) is reset as well and its tests are reported as "does not compile" (if the errors are in
   an unchanged file, the deleted test files are restored first, then all changed ones). The report (job log and
   step summary) lists per test: fails, passes, skipped, crash or hang (one by one after an aborted run), no
   result, does not compile. Informational (exit 0, also when the comparison cannot be made: the reason is in the
   report), except with the PR label `bugfix`: then at least one new test must fail, crash or hang on the base.
   Not covered: `tests/g3cli.Tests`, changed (not added) test methods, tests with a `DisplayName` (reported, not
   run).
2. **API compatibility with upstream 1.0.324**: .NET package validation (`EnablePackageValidation` with baseline
   1.0.324) in the pack step. Done (#92, §2.8): `dotnet pack` fails with CP0002 etc. on a break; the accepted breaks
   are in `CompatibilitySuppressions.xml`.
3. **Banned APIs and analyzers in exact code**: a banned-API analyzer forbids `Math.FusedMultiplyAdd`, `float` and
   parallel floating-point reductions in `math/Exact*`, `math/AdaptivePredicates.cs` and `mesh_ops/ExactMesh*`;
   warnings as errors there. Done, in the exact files listed in `.editorconfig` (`math/Exact*`,
   `math/AdaptivePredicates.cs`, `mesh_ops/ExactMesh*`, and the exact parts the mesh pipeline calls:
   `ExactTriangulation2`, `ExactPlanarSimplify`, `ExactTriangleIntersection`), each rule an error there and off
   elsewhere:
   - RS0030 (`Microsoft.CodeAnalysis.BannedApiAnalyzers`, `BannedSymbols.txt`): `Math` / `double.FusedMultiplyAdd`,
     `MultiplyAddEstimate`, the hardware intrinsics namespaces, `MathF`, `System.Single`, `Interlocked` on double /
     float. No allow-list.
   - G3E0001 (`tools/analyzers/G3ExactCodeAnalyzers`): every expression, declaration and signature whose type is or
     contains `float`, including structs with float fields (`Vector3f`, `AxisAlignedBox3f`, a `GetVertexNormal`
     result); RS0030 sees only member accesses such as `float.Epsilon`.
   - G3E0002: every use of `Parallel`, PLINQ and `gParallel`. The analyzer cannot see what a loop body aggregates;
     the reviewed existing loops (integer aggregation only, output normalized afterwards) are allowed per member in
     `core/ExactCodeAllowList.cs`, which suppresses only G3E0002.
   - G3E0003: every framework method named as a fused multiply-add (`FusedMultiply*`, `MultiplyAdd*`,
     `MultiplySubtract*`), in any type.

   `ExactCodeAnalyzerTests` checks the analyzers, that the allow-list switches off only G3E0002, that every banned ID
   and allow-list target names an existing symbol, the severities by Roslyn's own reading of `.editorconfig`, and
   that the library project runs the analyzers. Not covered: `Task` / `Thread`, and a new loop inside an allowed
   member.
4. **Acceptance against references**: for spec-driven work, an oracle by definition, a differential against other libraries, and a golden
   master of accepted results ([strategy](design/ACCEPTANCE_STRATEGY.md)).
5. **Quality ratchet in CI**: a small fixed benchmark subset with stored metrics (JSON: clean results, max
   deviation, self-intersections); a worse number fails the build.
6. **Same-runner performance A/B**: base and head alternate on one runner, with spread; a regression above a
   threshold is reported. Cross-session numbers drift 20-30 %.
7. **Reproducible claims**: every number in a PR cites the command and attaches the raw output; the reviewer re-runs
   a sample (as `benchmarks/BooleanComparison/perf/` already allows).
8. **Changed-line coverage gate**: new or changed lines must be executed by tests. Implemented for the core,
   Clipper2 and CLI as a nightly workflow on master with attribution to the pull request, not as a PR gate (section 2.6).
9. **Property-based tests with shrinking** (CsCheck, Apache-2.0, or FsCheck, BSD), generalizing `MeshMinimizer`.
   Boolean test-only implementation: [plan](design/BOOLEAN_PROPERTY_TESTS.md), with parameter-level shrinking and
   replay; CsCheck campaigns on net10.0, deterministic regressions also on net48. Done for `ExactMeshBoolean`
   (`ExactBooleanPropertyTests`, `ExactBooleanMetamorphicTests`) and for `MeshRobustRepair` /
   `MeshRoundingRepair` (`RepairCampaignTests`, QA 2.0 T3: scenes of 1-4 pieces of very different sizes and
   distances, touching, nearly touching, overlapping, flipped / duplicated / dropped faces, jitter; properties: Apply()
   true implies clean by `NotCleanReason`, no defect the input lacks, displacement <= MaxDrift, 2^k scale invariance
   bit for bit, determinism, parallel = serial, reuse; findings in `RepairCampaignFindings`). Campaign classes carry
   `[Trait("Category", "Campaign")]` and have "Campaign" in their name; the Stryker `test-case-filter` terms are name
   parts and must not select them (`CampaignConventionTests` checks it). CI runs the default sample counts; a large
   local campaign: `G3_PROPERTY_SAMPLES=2000 dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter
   "Category=Campaign"` (add `G3_PROPERTY_ALL=1` to list every failing seed instead of stopping at the first,
   `G3_CAMPAIGN_DEFAULTS=1` for the default solidify / wrap resolution).
10. **Metamorphic relations beyond similarity**: vol(A∪B) = vol A + vol B - vol(A∩B); (A-B) ∪ (A∩B) = A;
    commutativity; subdividing input triangles or permuting triangle / vertex order leaves the result geometry
    unchanged. The [Boolean plan](design/BOOLEAN_PROPERTY_TESTS.md) includes independent analytic box oracles,
    triangulation-independent comparisons and orientation-preserving transformations, without changes to
    `ExactMesh*`. Other modules: terrain: never steeper, cut + fill consistent; 2D meshing: input segments are
    unions of output edges.
11. **Cross-platform determinism**: the bit-identity hashes weekly on Windows and arm64, and the net48 tests (not only
    the build) on a Windows runner.
12. **Tool-checked error bounds**: new floating-point filter bounds also derived with FPTaylor (MIT) and compared in
    the bounds review.
13. **Compiled doc examples**: code in README and docs/OPERATIONS.md compiles and runs in CI.

## 4. Assessment of the candidates

| Candidate | Value for robustness | Cost | Decision |
|---|---|---|---|
| Invariance and property tests | High: covers scale and stale state, with the defect generators also degenerate and open input | Low (harness, seeded) | Done for the core operations; required for new ones |
| FsCheck | Shrinking of counterexamples | A dependency, F# runtime on net48 | Optional, for the predicates only; seeded theories are enough elsewhere |
| Differential tests against exact results | High for `math/`, filters | Low | Done for the predicates (#137); boolean identities open |
| Fuzzing | High for readers / CLI (4 security fixes) | Low in-suite, high coverage-guided | Seeded in-suite fuzzing: done for the readers (#127), next for mesh inputs |
| Stryker.NET mutation tests | High for exact arithmetic | Long runs (scheduled) | Scheduled workflow for `math/Exact*`, `AdaptivePredicates.cs` and the exact mesh pipeline (sharded) |
| Coverage in CI | Medium (shows gaps) | Low | Changed executable lines: 100%; no whole-library threshold |
| Determinism tests | High: found three bugs at once | Low | Done; required for parallel paths |
| Regression corpus in CI | High for the pipelines | Medium | Done for the defect cases (`RegressionCorpusTests`, 2.7); cube grid and terrain open |
| API compatibility | High for users | Done (#92) | Keep |
| Review checklist | High (reviews found 40 of 45 bugs) | Low | [REVIEW_CHECKLIST.md](REVIEW_CHECKLIST.md) |
| BenchmarkDotNet | Performance only | Medium | After the robustness items |

## 5. Local setup

The CI steps in CLAUDE.md need the .NET 10 SDK. In a fresh Linux container (Ubuntu or Debian, as in Claude
Code cloud sessions, where the Microsoft download host may be blocked), the distribution package is enough:

```
apt-get update && apt-get install -y dotnet-sdk-10.0
dotnet tool restore            # Stryker.NET, for the mutation tests (tests/mutation/README.md)
```

As an environment setup script, these two lines make every session start with a working SDK. The net48 build
needs no Mono or Windows: it takes its reference assemblies from the `Microsoft.NETFramework.ReferenceAssemblies`
package, which is restored like any other. Running the net48 tests needs Windows (the `netstandard` job in CI).

Culture-sensitive regressions are exercised explicitly with `de-DE` and `en-US`, independently of the host's
locale. The Triangle CLI fixture formats hole seeds invariantly, and `TriangleWriters_AreCultureIndependent`
checks the production `.node` / `.poly` / `.ele` writers and read-back. The production writers already used
invariant formatting; the German-locale failure was in the test fixture. Remesher pinned findings also use
invariant decimal formatting. The generated-file comparison normalizes CRLF to LF on both sides, including
verbatim strings embedded in the generator's Windows checkout; it still checks all non-line-ending content.

```powershell
dotnet test tests\g3cli.Tests -c Release --filter FullyQualifiedName~TrianglePolyCliTests
dotnet test tests\geometry3Sharp.Tests -c Release --filter "FullyQualifiedName~PinnedFailures_AreCultureIndependent|FullyQualifiedName~GeneratedFile_IsUpToDate"
```

Two sessions running the CI steps in the same checkout overwrite each other's build output and package
directory. Run them one after another, or in separate `git worktree`s.
