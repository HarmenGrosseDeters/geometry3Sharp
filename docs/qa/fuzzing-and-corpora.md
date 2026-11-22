# Fuzzing, Corpora, Robustness Sweep & Issue Cases

Back to [QA Hub](README.md) · [docs/QA.md](../QA.md)

---

## 2.5 Fuzzing of the readers and of mesh inputs

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
[ReaderFuzzPilot](../../tools/ReaderFuzzPilot/README.md), replayed without a campaign by `ReaderFuzzReplayTests`, the mesh operations in
[MeshFuzzPilot](../../tools/MeshFuzzPilot/README.md) (campaigns on a runner, not in CI).

## 2.7 A small regression corpus as a CI smoke benchmark

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

## 2.7a Data sets beyond Thingi10K (benchmarks, by hand)

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

## 2.9 Minimizing a failure

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

## 2.10a Reference parts: what a chain of operations loses (owner 2026-11-19)

A small suite of functional technical parts (a flange with a cross bore, a housing with a thin rib, an assembly with a separate small
screw), built analytically with known targets and deliberate defects (unwelded, flipped, duplicated, a small gap), run through a chain
(repair, subtract a cut-out, reduce with a two-sided certified bound, STL float32 and OBJ export, once `g3cli convert`) with a functional check
after every step (a pin of fixed radius passes the bore, the rib is connected and keeps its thickness, the screw stays a body of its own
with its box). It reports the first failing step per check, not a per-operation metric; the findings decide where development time goes.
Tests: `tests/geometry3Sharp.Tests/ReferencePartsTests.cs` (the parts, mutation-checked checks, today's results pinned, invariance over
`Similarity.All()`), `tests/g3cli.Tests/ReferencePartsCliTests.cs`; the sweep and its table: [benchmarks/ReferenceParts](../../benchmarks/ReferenceParts/README.md).

## 2.10 Robustness sweep (`RobustnessSweepTests`, findings in [ROBUSTNESS_SWEEP.md](../ROBUSTNESS_SWEEP.md))

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

## 2.11 Hard cases from other libraries' issue trackers

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
that one is new and no normal turns around ([UPSTREAM_CHANGES.md](../UPSTREAM_CHANGES.md)). Finding R1, fixed:
- **The bug.** `MeshPostRepairCleanup.FindInvertedComponents` took a closed component whose interior lies mostly
  inside another for a nested one, also when their surfaces cross. So of two outward boxes crossing at 3 or 10
  degrees, one was flipped as a cavity, and the repair returned their difference as a clean result. The volume check
  accepted it, because the input's reference solid uses the same nesting rule.
- **The fix.** A component whose surface meets (exactly) that of a component its samples lie in has no nesting
  at all: crossing components keep their orientation (they are never flagged).

The
analysis and fix ideas of the findings are in [ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md) (also R2, a large
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
ideas, are in [ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md).

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
[ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md)).

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
([ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md)): BFF Disk with the conformal extension does not keep the boundary on the circle on
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
([ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md)): chains of collapses folded a noisy terrain and stood triangles up
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
test as the exact winding number of `VolumeSampleCells`). Findings in [ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md);
`ExactRayTriangleTests` checks the perturbation against big integers, parity and hits through the vertices and edges
of a closed grid cube along 29 directions, and invariance over `Similarity.All()`.
