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
  of the package.
- Before the harness of 2.1 there were no skipped tests; it adds two that document reported bugs. No flaky
  test showed up in repeated runs, after the `BitArray` races were fixed (ThreadingFixesTests).

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
  PRs only for the configs they touch ([tests/mutation/README.md](../tests/mutation/README.md)).
- A score below the break threshold fails the run. It starts at the measured score (rounded down, less one
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
  `ExactPoint3ConversionTests`). The boolean identities are open.

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
fails 2.3. Coverage-guided fuzzing (SharpFuzz, MIT) can follow for the binary readers if the seeded fuzzing
finds bugs.

### 2.6 Coverage in CI

- coverlet (MIT) collects line and branch coverage in the Linux job.
- ReportGenerator (Apache-2.0) writes a summary per directory into the job summary.
- At first there is no threshold. Coverage shows where tests are missing, and it is not a goal in itself.
- A directory whose coverage drops in a PR is a question for the review.

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
(volume right, non-manifold when welded by position).

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

## 4a. Making agent work verifiable

Most rules in CLAUDE.md are enforced by review only. These items make them machine-checked, or add oracles that do
not depend on the implementer's own tests. Order of priority:

1. **New tests fail on the old code**: a CI job runs the tests a PR adds against the base branch; for a bug fix they
   must fail there. Catches vacuous tests (one was found in review in #186).
2. **API compatibility with upstream 1.0.324**: .NET package validation (`EnablePackageValidation` with baseline
   1.0.324) in the pack step.
3. **Banned APIs and analyzers in exact code**: a banned-API analyzer forbids `Math.FusedMultiplyAdd`, `float` and
   parallel floating-point reductions in `math/Exact*`, `math/AdaptivePredicates.cs` and `mesh_ops/ExactMesh*`;
   warnings as errors there.
4. **Hidden acceptance tests (clean room for tests)**: for spec-driven work (QUADTREE2D_SPEC, MESHING2D_SPEC) a
   separate agent writes acceptance tests from the spec (worked examples, failure catalogues F-*/D-*); the
   implementing session may not change them.
5. **Quality ratchet in CI**: a small fixed benchmark subset with stored metrics (JSON: clean results, max
   deviation, self-intersections); a worse number fails the build.
6. **Same-runner performance A/B**: base and head alternate on one runner, with spread; a regression above a
   threshold is reported. Cross-session numbers drift 20-30 %.
7. **Reproducible claims**: every number in a PR cites the command and attaches the raw output; the reviewer re-runs
   a sample (as `benchmarks/BooleanComparison/perf/` already allows).
8. **Changed-line coverage gate**: new or changed lines must be executed by tests.
9. **Property-based tests with shrinking** (CsCheck, MIT, or FsCheck, BSD), generalizing `MeshMinimizer`.
10. **Metamorphic relations beyond similarity**: vol(A∪B) = vol A + vol B - vol(A∩B); (A-B) ∪ (A∩B) = A;
    commutativity; subdividing input triangles or permuting triangle / vertex order leaves the result geometry
    unchanged; terrain: never steeper, cut + fill consistent; 2D meshing: input segments are unions of output edges.
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
| Coverage in CI | Medium (shows gaps) | Low | Summary, no threshold |
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

Two sessions running the CI steps in the same checkout overwrite each other's build output and package
directory. Run them one after another, or in separate `git worktree`s.
