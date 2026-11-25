# Testing, Invariance, Determinism & Verification

Back to [QA Hub](README.md) · [docs/QA.md](../QA.md)

---

## 2.1 Invariance, reuse and determinism tests (#120: `tests/geometry3Sharp.Tests/Harness/Invariance.cs`)

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

**2D inputs** (`Similarity.All2D()`, `Apply2d`, `Inverse2d`, `Angle2d`): the rotations of `All()` also tilt about x and y, so
the xy part of their image is a rotation with a squash, not a similarity of the plane. Tests whose input is a set of points,
segments or polygons in the plane use `All2D()` (tests that map a planar loop into the orthonormal frame of the transformed plane,
as `PlanarComplexNestingProbeTests` and `PolygonSignedAreaTests` do, keep `All()`: for them the tilted members are valid inputs): the identity, UTM, the scales 2^-10, 2^10, 1e-3 and 1e3, a rotation about z
by 31 degrees, and a rotation about z by 23 degrees with the scale 1e-3 and the UTM translation. A pinned case that needs the
squash (`QualityMesh2NearTJunctionTests.RotatedFan`, `QualityMesh2GridRerouteSweepTests`) keeps `ByName("rotated")`. Operations on
3D points with a height (the quadtree adapters, which project) keep `All()`.

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

**Defect generators** (built: `tests/geometry3Sharp.Tests/Harness/Defects.cs`): procedural inputs for the degenerate, open, bowtie and nested cases of
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

## 2.3 Result validation in success tests

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

## 2.4 Differential tests against exact results

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
  box relation its own generated campaign (see `design/BOOLEAN_PROPERTY_TESTS.md`). `ExactBooleanFamiliesPropertyTests.CsCheck.cs` (#1038) adds the families
  beyond boxes (subdivided and rotated boxes, corner slivers, components touching at a face, edge or vertex, open inputs, scales 2^-150 to 2^150) with the algebraic
  laws (also two-operand inclusion-exclusion: one operand cannot tell an intersection from a union), serial against parallel, reuse, n-ary against the chain, the status
  contract and bit-exact power-of-two scaling; the fuzz targets `nary`, `determinism`, `reuse`, `open-antipodal` and `rounding-equivalence` of `tools/MeshFuzzPilot` cover the same properties on arbitrary decoded inputs.

## 2.4a Certifying checker of the exact boolean (test-only, owner 2026-11-17)

`tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs` checks a result of `ExactMeshBoolean` against its inputs, as an
oracle that is independent of the code it checks (stages 1-2 of direction 2 of
[BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](../research/BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md), sections 3.3-3.6). The boolean
records its decisions for it (`RecordCertificate`, internal: the winding numbers in front of each classified piece, keep,
flip, and the result before the rounding; recording changes nothing). The checker has its own exact tests (BigInteger
determinants on the exact points, no floating-point predicate decides anything) and reuses no classification code:
- **C0 inputs**: the arrangement's meshes are the caller's (reversed triangles exactly as many as `ReorientedTriangles`,
  none in a component that was consistently oriented, and the orientable ones consistent afterwards), every
  non-degenerate input triangle is in the arrangement once; every triangle the early-out culled meets no triangle of
  the culling mesh and lies outside it (an exact ray per connected set of culled triangles); the rounding's bound is at
  most the documented one, from the boolean's settings. The meeting test is the library's exact
  `ExactTriangleIntersection` on the input doubles (an exact predicate, tested on its own), the one place where the
  checker shares code with the arrangement; the ray still catches a culled set wholly inside the culling mesh.
- **C1 pieces**: every piece lies in its source triangle's plane, inside it, oriented like it; the pieces of a source tile
  it (paired inner edges, unpaired ones along the source's edges, projected areas adding up exactly).
- **C3 classification**: the coplanar stacks from the input triangles grouped by exact plane; the back of a piece from its
  front and its stack; around every edge the pieces sorted radially, consecutive ones agreeing on the wedge between them;
  one anchor per connected set: an exact ray (signed crossings with every input triangle).
- **C4 selection**: kept iff the predicate differs on the two sides, flipped iff the front is inside; copies never kept.
- **C5 result**: the result before rounding equals the kept pieces as an oriented point set (per plane, the boundary of
  the difference is zero, swept per supporting line, and the unsigned areas are equal, so that no two-sided sheet hides
  in the chain); the result is closed and oriented; result and exact result lie within the rounding bound (`MaxLength`
  plus a few ulps) of each other, by exact squared distances of the corners (a triangle is within delta of a triangle
  iff its corners are; a degenerate one by its edges), with four-way splits where one triangle does not cover. Where
  the rounding removed components (a documented decision), the components of the exact result that are tiny or thin by
  the removal's rule are left out of its coverage by the result; every other one is covered. Those parts are **listed**
  (`RemovedParts`: triangles, area, box) and `Check()` does not report the result as clean ("not clean: the rounding
  removed n part(s) ..."; owner 2026-11-22): the exemption is never silent.
- **C2 embedding** (stage 3, test-only): no two classified pieces of different sources meet except in a corner or along
  an edge they share by exact point, so that nothing passes through a piece (C3 assumes it: the winding number is
  constant on one side of a piece, the radial order around an edge is complete). A box tree of its own over the pieces
  (not the arrangement's `TriangleBoxTree`) and a pair test by the signs of orientation determinants of exact points
  only (BigInteger; a `long` path for integer points below 2^19, also exact): no floating-point filter, so there is no
  error bound to review, and no code of the arrangement or of the library's triangle tests (the test is checked against
  `ExactTriangleIntersection.IntersectsBeyondShared` on lattices). Only the classified pieces take part: the other
  pieces of a coplanar stack are copies, which the arrangement does not triangulate like the classified ones (the bottom
  face of A on the top face of B), so they overlap them in the plane by design; they are not kept, and the stacks
  account for them in C3. That accounting takes a coplanar input triangle for a piece by the piece's centroid, so C2 also
  checks that no classified piece straddles the boundary of a coplanar input triangle (it lies inside it, or the interiors
  are disjoint; exact separating-axis test). Pieces of one source are not paired (C1 certifies their tiling).

Class: closed inputs. Open inputs and an Intersection whose early-out culled triangles against several meshes are
`n/a` (the oracle runs Intersection with `CullOutsideA = false`); a Difference's early-out is checked (the other meshes'
winding numbers count only where the first mesh's is positive, with an anchor per connected set under that rule). The
embedding (C2) is checked since stage 3; the point set is not exact where the rounding removed parts: they are listed.

Where it runs: `BooleanCertificateTests` (the box and tetrahedron families of the metamorphic corpus with their
representation changes, spheres, nested, hollow, self-intersecting and duplicated solids, a winding predicate, the
similarities of `Similarity.All()`); every case of the CsCheck campaigns `ExactBooleanPropertyTests` (boxes, directed
families, tetrahedra; so `G3_PROPERTY_SAMPLES` campaigns use it as their oracle); and a Nightly corpus of random spheres,
tetrahedra and boxes (`Certifies_RandomSolids_Nightly`, 480 booleans with its seed, about 13 s). Teeth: 150 mutations
of the winding numbers (a patch's winding number of one mesh off by one, two patches' values swapped; the selection
made to agree, so that only C3 can see them; 6 equivalent ones under the early-out skipped), a nested component read as
outside (caught only by its anchor ray), 114 mutations of the selection (keep, flip, a coplanar copy kept, A and B
swapped in a difference), a wrongly culled component and a culled triangle that meets the culling mesh, mutations of
the inputs, the bound, the pieces (each C1 condition on its own) and the result (a triangle removed or reversed, a
vertex moved, a two-sided sheet, a moved exact vertex): all caught. C2 has its own: every split source triangle of four
cases put back unsplit (the arrangement's classic fault, a missed intersection: C1 still holds, only C2 sees it), a sheet
through the middle of a result, and a folded coplanar piece. No false alarm: every case above, the campaigns and the
Nightly corpus certify. The checker itself is a test file, so Stryker does
not mutate it; `tools/certificate_hand_mutants.py` switches off or reverses each condition and sign of the checker (50
mutants, about 45 minutes), and `BooleanCertificateTests` kills every one. Cost (warm, two spheres, 696 pieces): the boolean 4 ms, C1 19 ms, C3 + C4 10 ms,
C5 65 ms, C2 about 40 ms (the prototype of the research note measured 9 us per pair with BigInteger): several times the
boolean, as BigInteger decides every sign (an oracle for tests, not a mode for users).

## 2.5a Supported input range: correct inside, fail gracefully outside (owner 2026-11-09)

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
([design/FIXED_WIDTH_EXACT_POINTS.md](../design/FIXED_WIDTH_EXACT_POINTS.md)) and from overflow of squared lengths
(about 1e154), with margin; a module may support less and says so. Generators of property campaigns sample inside
the range (with a bias toward its edges, where the scale-disparity bugs were found), and add a separate small
out-of-range sample for property 2. Fuzz harnesses report out-of-range findings in their own class.

**The ranges (2026-11-10).** "Magnitude" is the absolute value of each nonzero coordinate (zero is always fine; the rounding repair and the exact boolean's rounding test the largest coordinate of the triangles and subnormals, and the boolean judges its result, not its inputs);
"disparity" is the ratio of the largest to the smallest part or component of one input. Measured with a scale sweep
(a standard input scaled by 2^e, e from -1000 to 1000, 20 s budget per run; see the table notes), then set inside the
measured range with margin. Subnormal coordinates (below 2.2e-308) are outside for every module, and out of the 2^k
scale checks of the campaigns (the scaling is not exact there).

| Module | Magnitude (nonzero) | Disparity | Measured | Outside: status |
|---|---|---|---|---|
| `ExactMeshBoolean`, SafeRounding | 2^-200 .. 2^200 | 2^60 | cubes: ok 2^-250 .. 2^500; empty `Success` below 2^-280; invalid rounding above 2^700 | `Failed` / range status (PR per module) |
| `MeshRoundingRepair` | 2^-200 .. 2^200 (the largest coordinate; no subnormal; `OutsideRange`) | 2^60 (`ScaleDisparity`, not a range) | invalid at 4e176 (FINDINGS 6) | not clean, honest (documented limit) |
| `MeshRobustRepair` | 2^-200 .. 2^400, extent >= 2^-40 of the largest magnitude | any, reported: `ScaleDisparity` / `LostFarPart` / `LostFarOpenPart` / `LostSeparatePart` (`Apply()` false) | spheres: ok 2^-250 .. 2^500, hang at 1e176 (FINDINGS 3) | `Apply()` false with a reason |
| `MeshAlphaWrap` | 2^-128 .. 2^128, extent >= 2^-40 of the largest magnitude | 2^40 | ok 2^-160 .. 2^160; hang at 2^+-180/200; `Failed`/`InvalidParameters` from 2^+-220 | `InvalidInput` |
| `Reducer` | 1e-150 .. 1e150 and beyond (a power-of-two frame for the quadrics, the edges scaled in the flip test) | -- | sphere: reduces, closed and finite, from 1e-300 to 1e307 with the same relative result at every power-of-two scale (a vertex position is only optimal where the frame scale is a normal double, 2^+-1000) | not guaranteed (documented limit) |
| `Remesher`, `PartitionedReducer` | 2^-200 .. 2^200 (`PartitionedReducer` also in the Reducer's range) | -- | sphere: ok 2^-250 .. 2^500 | not guaranteed (documented limit) |
| `DMeshAABBTree3` ray, `IsInside`, winding numbers | up to 2^1000 | -- | fuzzed to 2^1000 | not guaranteed above |
| `DMeshAABBTree3` nearest (squared distance) | extent 2^-450 .. 2^450 | -- | squared distance representable | not guaranteed outside |
| 2D mesher (`DelaunayMesh2` and the constrained variants) | up to 1e70, any small magnitude; constrained: a domain more than 2^-41 of the larger of the largest coordinate and the extent wide (the automatic tolerance, O11) | -- | exact predicates; pentagon at aspect 2^e: mesh up to 2^±40, `Empty` from 2^±41 (#986) | `ArgumentException`; a domain no wider may vanish: `Empty` with a diagnostic without a snap tolerance (documented limit, owner 2026-11-17) |
| `TerrainVolumeDifference`, `TerrainOffset` | <= 2^200, extent 2^-8 .. 2^200 | -- | bumpy 8 x 8 terrain: correct 2^-20 .. 2^250; zero volumes with `true` below 2^-25 and above 2^260; offset wrong with `true` above 2^260, `false` below 2^-30 | `Compute()` false (PR per module) |
| `DualContouring` (`ExactHermite`, the default; dense mode) | geometry 2^-100 .. 2^200 with the field scaled along (edges below 2^250: the degenerate-triangle rules use a fourth power); node values normal `float` (1.2e-38 .. 1.7e38); any gradient magnitude | -- | the same bits for values x 2^-100 .. 2^100, geometry x 2^-60 .. 2^200, both x 2^-100 .. 2^200 (sphere and box; the sphere to 2^400): `DualContouringFieldScaleTests`. Values below 1.4e-45: an empty mesh (`benchmarks/MagnitudeRobustness`) | not guaranteed: an empty or partial mesh without a status below the `float` range (documented limit, #1376); `ExactHermite = false`: absolute tests (2.2e-16), ordinary sizes only; sparse mode and refinement: not swept |
| `IntrinsicTriangulation` (`FlipToDelaunay`, `DelaunayRefine`, `ExtractCommonSubdivision`), `MeshGeodesicPath`, `MeshGeodesicBezier`, `VectorHeatMethod` | any: prescaled by 2^-k (k from the longest edge), lengths in unit size; results in the units of the mesh | 2^200 (`HasLengthDisparity`; the classes with a status refuse, the constructor, flips and `VectorHeatMethod` do not) | bits equal to the unit result times 2^e from 2^-1010 to 2^1020 (`MagnitudeRobustnessTests`); `ExtractCommonSubdivision` `Inconsistent` from 2^513 (face areas in the units of the mesh overflow) | `Inconsistent`, `TraceFailed` |
| `MeshGeodesicDistance` (with `TracePath`) | any (prescaled, `TracePath` on a scaled copy) | 2^200 | bits equal to the unit result times 2^e from 2^-1010 to 2^1020, also with `IntrinsicDelaunay = false` | `Compute()` false (`Distance` null; also for a NaN distance) |
| `PolyhedralGeodesicDistance` (with `TracePath`) | any (the positions, windows and lengths in unit size) | 2^200 | bits equal to the unit result times 2^e from 2^-1010 to 2^1020; `MaxDistance` in the units of the mesh | `DegenerateTriangles` |
| `QuadRemesher` (without a given `Field` / `Frames`) | any (a scaled copy; `Result`, `TargetEdgeLength` and `SizeFunction` in the units of the mesh) | 2^200 | bits equal to the unit result times 2^e from 2^-1010 to 2^1020 | `InvalidInput` |
| Other terrain modules (grid TIN, greedy TIN, slope map / design / overlay, dual contouring, safety) | not swept: the slope classes decide with the exact kernel (`ExactSlopeKernel`) | -- | -- | open |

The ranges are class-doc statements ("Supported input range") of the classes above (those of `ExactMeshBoolean` and
`MeshRoundingRepair` come with their range-check PRs: the mutation shards address these files by character offset). The checks that turn the
"Outside" column into a status land per module (bugfix PRs); until then the column is the documented behaviour of
the module outside the range, or "not guaranteed".

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
   master of accepted results ([strategy](../design/ACCEPTANCE_STRATEGY.md)).
5. **Quality ratchet in CI**: a small fixed benchmark subset with stored metrics (JSON: clean results, max
   deviation, self-intersections); a worse number fails the build.
6. **Same-runner performance A/B**: base and head alternate on one runner, with spread; a regression above a
   threshold is reported. Cross-session numbers drift 20-30 %.
7. **Reproducible claims**: every number in a PR cites the command and attaches the raw output; the reviewer re-runs
   a sample (as `benchmarks/BooleanComparison/perf/` already allows).
8. **Changed-line coverage gate**: new or changed lines must be executed by tests. Implemented for the core,
   Clipper2 and CLI as a nightly workflow on master with attribution to the pull request, not as a PR gate (section 2.6).
9. **Property-based tests with shrinking** (CsCheck, Apache-2.0, or FsCheck, BSD), generalizing `MeshMinimizer`.
   Boolean test-only implementation: [plan](../design/BOOLEAN_PROPERTY_TESTS.md), with parameter-level shrinking and
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
    unchanged. The [Boolean plan](../design/BOOLEAN_PROPERTY_TESTS.md) includes independent analytic box oracles,
    triangulation-independent comparisons and orientation-preserving transformations, without changes to
    `ExactMesh*`. `ExactBooleanFuzzTests` (2026-11-22, [research note](../research/EXACT_BOOLEAN_FUZZ_2026-11.md)) checks the
    set identities exactly on near-degenerate generated solids (an exact surface fingerprint, no tolerance): a seeded smoke
    per family in the PR tier, the campaign in the Nightly tier. Other modules: terrain: never steeper, cut + fill consistent; 2D meshing: input segments are
    unions of output edges.
    Symmetry and cost relations (S1, C1, C2 of [METAMORPHIC_RELATIONS.md](../design/METAMORPHIC_RELATIONS.md)):
    `SymmetryInvarianceTests` maps the input by one of the 48 symmetries of the cube (`Harness/Symmetry.cs`; exact, 24 are
    reflections that are re-wound) and compares the sweep operations (`RobustnessSweepTests.Ops`), `ExactMeshBoolean` and the 2D
    mesher with the mapped result: bit for bit unless the operation is a classified row of `Toleranced` (with its reason); six
    symmetries in the PR tier, all 48 in the nightly job (`Category=Nightly`). `CostInvarianceTests`: the output size on a
    transformed copy is equal under the 2^k scales and at most twice as large otherwise (#664 as a rule, the 2D mesher and
    `ExactMeshBoolean`), and the wall time of every sweep operation at most 20 times and 50 ms more (nightly).
    Curved solids (session S2, 2026-11-25): `BooleanMetamorphicCurvedTests` runs `ExactMeshBoolean` and `SolidBooleanPipeline`
    on 13 procedural pairs (spheres, a torus, rotated boxes; overlapping, nested, disjoint, touching, near-coincident, identical;
    `Harness/CurvedSolids.cs`; non-dyadic coordinates, so volumes are compared within the measured rounding drift, itself checked
    against the documented 1e-5 bound): commutativity (same solid, same status and clean state; the same mesh is not promised),
    inclusion-exclusion, A u A = A, A - A empty, refinement by midpoint splits of one or both operands, and an inverted operand
    (refused, or the Boolean with that operand empty; `ByNesting` gives the result of the outward input). `OperationMetamorphicTests`:
    `MeshOffset` (offset(d) contains offset(d') for d > d', insets nested in the input) and `Remesher` with a projection target
    (volume and area within 5 %, clean with `PreventSelfIntersections`). All held on master (a sweep of 81 further generated pairs, shifts 1e-14 to 1,
    found no violation); the slowest test takes 2.5 s. `MeshRobustRepair` / `MeshAutoRepair` under a permutation of vertex and triangle order
    (measurement only, not asserted): 11 broken inputs x 19 orders gave the same result in every order (same triangles by position, same status).
    Round 2 (`MetamorphicRound2Tests`, tolerances derived in each test): `MeshWindingNumberBoolean` and `MeshWindingNumberSolidify` against
    `SolidBooleanPipeline` within sqrt(3) cell sizes times the area; `MeshAlphaWrap` contains the input (winding number of the input vertices and
    centroids) with vertices between Offset and 1.1 (Offset + Alpha) from it; `Reducer` with a two-sided `MaxDeviation` stays inside its band on both sides
    and keeps the volume within deviation times area; `TpmsLattice` is periodic (Origin or Box moved by whole periods, six types, three styles). All held on master.
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
| Differential tests against exact results | High for `math/`, filters | Low | Done for the predicates (#137); boolean identities done since (`ExactBooleanMetamorphicTests`, [BOOLEAN_PROPERTY_TESTS.md](../design/BOOLEAN_PROPERTY_TESTS.md)) |
| Fuzzing | High for readers / CLI (4 security fixes) | Low in-suite, high coverage-guided | Seeded in-suite fuzzing: done for the readers (#127); coverage-guided for readers and mesh operations since (2.5) |
| Stryker.NET mutation tests | High for exact arithmetic | Long runs (scheduled) | Scheduled workflow for `math/Exact*`, `AdaptivePredicates.cs` and the exact mesh pipeline (sharded) |
| Coverage in CI | Medium (shows gaps) | Low | Changed executable lines: 100%; no whole-library threshold |
| Determinism tests | High: found three bugs at once | Low | Done; required for parallel paths |
| Regression corpus in CI | High for the pipelines | Medium | Done for the defect cases (`RegressionCorpusTests`, 2.7); cube grid and terrain open |
| API compatibility | High for users | Done (#92) | Keep |
| Review checklist | High (reviews found 40 of 45 bugs) | Low | [REVIEW_CHECKLIST.md](../REVIEW_CHECKLIST.md) |
| BenchmarkDotNet | Performance only | Medium | After the robustness items |
