# Robustness sweep of the upstream code (2026-10)

Three classes of bugs each turned up by chance in one week (#151, #154): absolute thresholds that break scale
invariance, float arithmetic inside double pipelines, and non-finite input that throws or gives garbage. This
sweep looks for them across the library, mostly in the upstream code. A fourth class, results that depend on the
thread schedule, was checked by reading only.

How the findings were found:

- **Dynamically**, by `tests/geometry3Sharp.Tests/RobustnessSweepTests.cs` (docs/QA.md 2.10). It runs 23 public
  operations under every `Similarity.All()` plus scales of 2^-24 and 2^20, and with a NaN or infinite vertex
  or parameter. The cases that fail are listed in `RobustnessSweepTests.KnownFailures` with the finding id
  below. That list is a strict expected-failure list: a fix that makes a case pass must remove its entry.
  Two exceptions: known hangs are not run, so they are not strict, and on .NET Framework the list is not strict
  because it was taken on net10, where double-to-int casts saturate.
  Ids R1 to R21.
- **Statically**, by reading `mesh_ops`, `mesh`, `spatial`, `distance`, `queries`, `intersection`, `solvers`,
  `parameterization`, `implicit`, `comp_geom`, `containment` and `curve`. These are ids S1 to S40. Findings
  that the sweep also hit are merged into the R entries.

Not covered: files that other sessions are working on (`mesh/Remesher*.cs`, `mesh_ops/ExactMesh*.cs`,
`math/Exact*`, the metrics) were not read. The sweep still runs `Remesher`. g3cli commands wrap the same
operations and were not swept separately.

Severity order: **wrong answer** > **silent garbage** (garbage output with a success status) > **exception**
(or hang) instead of a status > **conservative failure** (refuses or does less than it could) > performance.

## Dynamic findings (sweep)

| Id | Operation | Class | Repro (sweep case) | Severity | Cause |
|---|---|---|---|---|---|
| R1 | **fixed** `MeshNormals.Compute` (area weighted vertex normals) | 1 | scale 1e-3 or 2^-10: normal (0.56, …) becomes (7.6e-8, …) | wrong answer | `mesh/MeshNormals.cs:109` normalizes only if \|Σ area·n\|² > `ZeroTolerancef` (an L⁴ quantity against 1e-6). One-rings with an area below about 1e-3 keep tiny, non-unit normals. Everything that offsets along vertex normals is affected (`RemoveOccludedTriangles` PerVertex, extrusions). `QuickCompute(mesh, vid)` (line 140) uses `Normalized` at Epsilon, which is the same issue at extreme scales. |
| R2 | `LaplacianMeshSmoother`, and through it `SmoothedHoleFill` | 1/precision | utm: smoothed area 11.17 becomes 11.90; `SmoothedHoleFill` area 12.56 becomes 12.38 | wrong answer | The CG solve runs on absolute coordinates with a tolerance relative to \|b\|. At UTM, \|b\| is about 6e6, so the error is about 0.06, of the order of the model. Rotation also changes the result (3.9e-5). Fix: solve relative to a local origin (bounds center). |
| R3 | **fixed** `MeshSignedDistanceGrid` (also `CachingMeshSDF`, `MeshWindingNumberGrid`, `MeshScalarSamplingGrid`) | 2 | utm: MC volume 3.988 becomes 3.956; rotated, 1e-3, utm: volume 0 | wrong answer | The grid origin is a `Vector3f`, and sample points are computed in float (`spatial/MeshSignedDistanceGrid.cs:136,167,249,316,434,570`). The same pattern is in `implicit/CachingMeshSDF.cs:95,120,234`, `spatial/MeshWindingNumberGrid.cs:89,120,172,…` and `spatial/MeshScalarSamplingGrid.cs:57,80,…`. `MeshWindingNumberSolidify` recenters first and is not affected. |
| R1 | `MeshNormals.Compute` (area weighted vertex normals) | 1 | scale 1e-3 or 2^-10: normal (0.56, …) becomes (7.6e-8, …) | wrong answer | `mesh/MeshNormals.cs:109` normalizes only if \|Σ area·n\|² > `ZeroTolerancef` (an L⁴ quantity against 1e-6). One-rings with an area below about 1e-3 keep tiny, non-unit normals. Everything that offsets along vertex normals is affected (`RemoveOccludedTriangles` PerVertex, extrusions). `QuickCompute(mesh, vid)` (line 140) uses `Normalized` at Epsilon, which is the same issue at extreme scales. |
| R2 | **fixed** `LaplacianMeshSmoother`, and through it `SmoothedHoleFill` | 1/precision | utm: smoothed area 11.17 becomes 11.90; `SmoothedHoleFill` area 12.56 becomes 12.38 | wrong answer | The CG solve runs on absolute coordinates with a tolerance relative to \|b\|. At UTM, \|b\| is about 6e6, so the error is about 0.06, of the order of the model. Rotation also changes the result (3.9e-5). Fix: solve relative to a local origin (bounds center). |
| R3 | `MeshSignedDistanceGrid` (also `CachingMeshSDF`, `MeshWindingNumberGrid`, `MeshScalarSamplingGrid`) | 2 | utm: MC volume 3.988 becomes 3.956; rotated, 1e-3, utm: volume 0 | wrong answer | The grid origin is a `Vector3f`, and sample points are computed in float (`spatial/MeshSignedDistanceGrid.cs:136,167,249,316,434,570`). The same pattern is in `implicit/CachingMeshSDF.cs:95,120,234`, `spatial/MeshWindingNumberGrid.cs:89,120,172,…` and `spatial/MeshScalarSamplingGrid.cs:57,80,…`. `MeshWindingNumberSolidify` recenters first and is not affected. |
| R4 | `PointHashGrid3d.FindNearestInRadius` (also `PointHashGrid2d`, `SegmentHashGrid`) | 3/1 | rotated, 1e-3, utm (cell 3e-4 at 5.7e6): hang | exception (hang) | A cell index ≥ 2^31 saturates to `int.MaxValue` on .NET 9 and later (net10), and `for (xi = …; xi <= max; xi++)` wraps forever (`spatial/PointHashGrid3d.cs:134-145`). .NET Framework casts such values to `int.MinValue` instead, which gives wrong cell ranges rather than a hang. From reading the code, an infinite query point, or a cell size of 0, hangs the same way on net10; the sweep's parameter is cell size and radius at once and does not show it. This also reaches `MeshMeshCut`/`MeshBoolean` snapping and `TerrainSafety` queries. |
| R5 | **fixed** `MeshPlaneCut.Cut` | 1 | scale 2^-24: the cut loop and the cap are lost (1 loop becomes 0; open result) | wrong answer | `DegenerateEdgeTol = ZeroTolerancef` (1e-6, absolute; `mesh_ops/MeshPlaneCut.cs:50,221`), on by default. A fine mesh at 2^-10 or 1e-3 also loses cut edges. The on-plane test against `MathUtil.Epsilon` (line 140) is too tight at UTM and gives t≈0 slivers. |
| R6 | `MeshBoolean` / `MeshMeshCut` (upstream boolean; `ExactMeshBoolean` is the recommended one) | 1 | scale 1e-3 and 2^-10: exception "unsortable vertex"; 2^-24: exception in `find_intersections`; rotated: 498 vs 504 triangles; rotated, 1e-3, utm: hang | wrong answer / exception | `VertexSnapTol = 1e-5` is absolute (`MeshBoolean.cs:34`, `MeshMeshCut.cs:35`), and so is the `MergeCoincidentEdges` tolerance (2x that, `MeshBoolean.cs:131`). The 1e-8 plane-distance snap in `IntrTriangle3Triangle3.cs:491` and the hash-grid hang (R4) add to it. |
| R7 | `Reducer` (quadric error) | precision | utm: area after reduction 12.157 becomes 12.151; rotated, 1e-3, utm differs too | wrong answer (different result) | The plane quadrics are built on absolute coordinates: d = -n·p with \|p\| about 6e6, and the d² terms cancel. The collapse order and the optimal points change. Fix: quadrics relative to a local origin. |
| R8 | `MeshICP.Solve` | 1 | scale 1e-3 and 2^-10: stops early (mean distance 3.4e-5 becomes 8.2e-3); 1e3, 2^10, utm: `Converged` false | wrong answer / conservative failure | `ConvergeTolerance = 0.00001` is an absolute distance (`mesh_ops/MeshICP.cs:23,77`). The failure at large scale and at UTM also needs a look (it could be the absolute convergence test alone). |
| R9 | **partly fixed** (the four grids) `MeshSignedDistanceGrid` + `MarchingCubes` | 3 | +Inf vertex, NaN/±Inf cell size: `AggregateException` (IndexOutOfRange) | exception | Grid dimensions come from non-finite bounds or cell size with no guard (`spatial/MeshSignedDistanceGrid.cs:169`, `DenseGrid3.cs:49`). The same applies to `MeshWindingNumberGrid`, `MeshScalarSamplingGrid`, `CachingMeshSDF`, `ImplicitFieldSampler3d` and `MarchingCubes.CubeSize`. A very small finite cell size overflows `ni*nj*nk` (OOM). |
| R10 | **fixed** `MarchingCubes` on distance fields | 1 | scale 2^-10: volume 3.9882 becomes 3.9900; 2^-24: 3.969; 2^10 and 2^20: not bit identical (the marching cubes run serially in the sweep) | silent garbage (staircase) | The root finder has absolute 1e-5 thresholds on field differences (`mesh_generators/MarchingCubes.cs:415-423`, `MarchingCubesPro.cs:703-711`). At small scale it returns midpoints or endpoints. Winding-number fields (dimensionless) are not affected. |
| R11 | `MeshAutoRepair.Apply` | 3 | +Inf vertex: `AggregateException` from `DMeshAABBTree3.FindNearestHitTriangle` ("ray direction is not normalized") | exception | The occlusion step builds rays from a non-finite vertex. A NaN vertex passes. |
| R12 | `MeshICP.Solve` | 3 | NaN or +Inf vertex in the source: hang | exception (hang) | Nearest-point queries with a non-finite point, then a convergence test on NaN that never succeeds. |
| R13 | `Reducer`, `MeshIterativeSmooth` (`Remesher`: **fixed by #163**) | 3 | NaN or +Inf vertex: 1 non-finite vertex becomes 2 to 327 | silent garbage | Collapse and smoothing average the bad vertex into its neighbours. NaN quadrics also break the heap order (`Reducer.cs:614-634`). `Reducer.ReduceToMaxQuadricError(NaN)` and `ReduceToEdgeLength(NaN)` collapse everything (`Reducer.cs:187,857`). The `Remesher` part is in a file owned by another session. |
| R14 | `Reducer` | 1 | scale 2^-24: other result (area, triangle count) | wrong answer (extreme scale) | Absolute constants below 2^-25 (known: `Reducer.cs:578` Epsilon on a length, `:394` ZeroTolerance cell size). Only at edges of about 1e-8. |
| R15 | `MeshIsoCurves` | 1/3 | scale 2^-24: 48 vs 47 graph vertices; NaN iso value: success with NaN geometry | wrong answer (extreme scale) / silent garbage | 1e-8 on field differences in the root finder (`mesh_ops/MeshIsoCurves.cs:290`). Non-finite field values are not rejected. |
| R16 | `MeshGeodesicDistance` | precision | rotated, 1e-3, utm: max distance 3.0087 vs 3.0114 (9e-4 relative) | to investigate | Larger than the coordinate resolution there (about 1e-7 of the size). It could be intrinsic Delaunay flips on near ties. Fork code. |
| R17 | `DMeshAABBTree3.WindingNumber` | 1 | scale 2^-24: -1.5e-17 vs 0 | negligible | Not bit identical under a power-of-two scale. Some absolute threshold in the fast winding or solid-angle path. |
| R18 | **fixed** `MeshNormals.Compute` | 4 | repeated runs differ in the last bits (seen as flaky 2^k comparisons) | nondeterminism | Area-weighted face normals are summed into the vertices under a SpinLock in thread order (`mesh/MeshNormals.cs:92-104`). Everything downstream of vertex normals inherits it. |
| R19 | `MeshBoolean` (`MeshMeshCut.RemoveContained`) | 4 | 2^20: bit identical in some runs and not in others | nondeterminism | `SafeListBuilder` order follows the thread schedule, which changes the removal and free-list order and so the later ids (`mesh_ops/MeshMeshCut.cs:80-86`). |
| R20 | **fixed** `MeshNormals.Compute` | 3 | NaN or +Inf vertex: NaN normals on its one-ring (7 vertices), no status | silent garbage (low) | Inherent to normals of faces with a bad corner; there is no status to report it. |
| R20 | `MeshNormals.Compute` | 3 | NaN or +Inf vertex: NaN normals on its one-ring (7 vertices), no status | silent garbage (low) | Inherent to normals of faces with a bad corner; there is no status to report it. |
| R21 | `LaplacianMeshSmoother` | precision | rotated: vertices differ by about 5e-5 of the size | to investigate (low) | CG stops at 1e-8 of each axis' right-hand side; with the condition number of the smoothing system the solution is only accurate to about 5e-5, and a rotation redistributes that between the axes. A solver tolerance of upstream, not a placement bug (R2, the translation part, is fixed). |

## Static findings (code reading, not yet reproduced by the sweep)

| Id | Location | Class | Consequence | Severity |
|---|---|---|---|---|
| S1 | **fixed** `distance/DistTriangle3Triangle3.cs:76,101` | 1 | Squared distance ≤ `ZeroTolerance` (1e-8, so a distance of 1e-4) is reported as 0. At 1e-3 scale that is 10% of the model. Reaches `MeshQueries.TrianglesDistance` and `DMeshAABBTree3.FindNearestTriangles`. | wrong answer |
| S2 | `intersection/IntrTriangle3Triangle3.cs:491-494,313`, and `UnitCross` at Epsilon (186-381) | 1 | Vertices within 1e-8 of the plane snap onto it, and tiny triangles count as degenerate. Wrong intersections at about 2^-20. Reaches `DMeshAABBTree3.FindAllIntersections` and `TestIntersection`. | wrong answer (small scale) |
| S3 | **fixed** `mesh_ops/MeshInsertProjectedPolygon.cs:110,129,169` | 2 | Positions are cast to `Vector3f` before `Frame3f.ToPlaneUV`: 2D jitter up to 0.25 m at UTM, so flipped or collapsed edges, wrong containment, and lifted vertices off. | wrong answer |
| S4 | **fixed** `mesh_ops/MeshLoopClosure.cs:80,90` | 2 | `ProjectToPlane((Vector3f)v)` with a float plane origin moves every loop vertex by up to 0.25 m at UTM. | wrong answer |
| S5 | **fixed** `mesh/MeshUtil.cs:44-47` (`ScaleMesh`) | 2 | Every vertex goes through `Vector3f`: up to 0.25 m at UTM, even at scale 1. | wrong answer |
| S6 | `queries/MeshQueries.cs:39-46,159-195` (`NearestPointFrame`, `RayHitPointFrame`, `SurfaceFrame`); `mesh/DMesh3.cs:590-608,824-841` (`GetVertexFrame`, `GetTriFrame`) | 2 | They return a `Frame3f` with the surface point as origin: the position is off by up to 0.25 m. These are API return types (cannot change); new double variants are needed. | wrong answer (API) |
| S7 | `mesh_ops/AutoHoleFill.cs:222,273,281` | 2 | The planar-spans path flattens loops in float: wrong nesting of nearby holes at UTM. | wrong answer (rare) |
| S8 | `spatial/BasicIntersectionTargets.cs:74-80` (`PlaneIntersectionTarget`) | 2 | Ray and plane are intersected in float, d = -n·origin in float: up to 0.5 m. | wrong answer |
| S9 | `spatial/GridIndexing.cs:169-186` (`FrameGridIndexer3`) | 2 | Float point to cell index: wrong cell below about 0.5 m cells at UTM. | wrong answer |
| S10 | `mesh/MeshEditor.cs:34-48` (`AddTriangleStrip(Frame3f)`), `mesh_generators/GenCylGenerators.cs:101-144` (`TubeGenerator`), `mesh_ops/MeshLocalParam.cs` (float positions; also 1e-6 on a squared distance, :240), `implicit/MarchingQuads.cs`, `implicit/Implicit2d.cs` | 2 | Float positions or frame origins: up to 0.25 m at UTM. Some of these are API types. | wrong answer (API) |
| S11 | `mesh_ops/PlanarHoleFiller.cs:195`, `PlanarSpansFiller.cs:111` | 1 | `ZeroTolerancef * scale` with scale = 1/MaxDim makes the threshold grow as the hole shrinks. Holes of 1e-3 reject edges below 0.1%; at 1e-6 every hole is refused. | conservative failure |
| S12 | `mesh_ops/PlanarHoleFiller.cs:251,321` | 1 | Vertex merge at an absolute 1e-6: bad merges and cracks at small scale. | conservative failure |
| S13 | `mesh_ops/MergeCoincidentEdges.cs:21`, `RemoveDuplicateTriangles.cs:21` defaults, as used by `MeshAssembly.cs:58-62` | 1 | 1e-6 absolute: distinct edges are welded and distinct triangles deleted at small scale. The classes themselves have invariance tests with explicit tolerances, but `MeshAssembly` calls them with the defaults. | wrong answer (small scale) |
| S14 | `containment/ContMinCircle2.cs:28,156,99` | 1 | A 1e-5 epsilon on a 2x2 determinant (L²): at 1e-3 scale every 3-point circle is rejected. | wrong answer |
| S15 | **fixed** `queries/IntersectionUtil.cs:54-56` | 1 | dir·(E1×E2) < 1e-8 counts as parallel: triangles with edges below about 1e-4 are missed. This is the same bug already fixed in `IntrRay3Triangle3`. No internal callers. | wrong answer (API) |
| S16 | `mesh_ops/RemoveOccludedTriangles.cs:41` | 1 | `NormalOffset = ZeroTolerance` (absolute) is only about 10 ulp at UTM, so test points stay on the surface. `MeshAutoRepair` overrides it with a relative value. | wrong answer (direct use) |
| S17 | `mesh_generators/DualContouring.cs:648` | 1 | Imposed sign ±1e-3 on distance fields: at small scale the crossings are pushed to cell edges. | silent garbage |
| S18 | `mesh_ops/MeshPostRepairCleanup.cs:180` (known) | 1 | `oldN.Normalized` is Zero for one-ring areas ≤ 2.2e-16 (edges about 1e-8), so fold flips are refused. | conservative failure |
| S19 | `mesh/Reducer.cs:1230` (`QuadricError.OptimalPoint(ref)`, public, unused internally) | 1 | det ≤ 1000·Epsilon on an L⁶ quantity: always false at 1e-3 scale. | conservative failure (API) |
| S20 | `spatial/DMeshAABBTree.cs:1856`, `PointAABBTree3.cs:710`, `DMeshAABBTree.cs:2323` | 1 | Absolute 1e-8 split threshold and 6e-6 box padding: pruning degrades at small scale. | performance |
| S21 | `comp_geom/Arrangement2d.cs:30,210`, `GraphSplitter2d.cs:25`, `curve/Polygon2d.cs:720`, `curve/PlanarComplex.cs:306,362`, `mesh/EdgeLoop.cs:277`, `mesh/EdgeSpan.cs:182` | 1 | 2D and curve tolerances (1e-5 to 1e-8) are absolute: wrong snaps or joins at small scale, missed joins at UTM. | wrong answer (2D) |
| S22 | `solvers/SparseMatrix.cs:105,122`, `PackedSparseMatrix.cs:557,596` | 1 | MᵀM entries with \|v\| ≤ 1e-8 are dropped. The current callers use dimensionless weights. | low |
| S23 | `MeshEditor`, `MinimalHoleFill`, `SimpleHoleFiller` | 3 | A NaN vertex passes through silently (no spreading in the sweep for the hole fillers; `MeshNormals` is R20). | low |
| S24 | `spatial/DMeshAABBTree.cs:1700` | 3 | A non-finite triangle trips a `Debug.Assert` (debug builds only). The release build skips it. | low |
| S25 | `mesh_ops/PlanarHoleFiller.cs:167`, `PlanarSpansFiller.cs:90` | 3 | A very small finite `FillTargetEdgeLen` makes n² grid vertices (OOM). | exception |
| S26 | `mesh_ops/TerrainSafety.cs:633-640,570,456,708` | 3 | `ExcessTolerance ≤ 0` gives n = int.MaxValue, then `new double[n+1]` throws `OverflowException`. An infinite source coordinate, or a tolerance or edge length of 0, makes about 2^31 steps. Only `SafetyDistance` is validated. | exception |
| S27 | `mesh_ops/TerrainOffset.cs:108` | 3 | A very small finite `Distance` next to tall walls gives `maxSteps` = int.MaxValue (near hang). | exception (hang) |
| S28 | `mesh_ops/MeshWindingNumberSolidify.cs:160`, `MeshWindingNumberBoolean.cs:109`, `TerrainDualContouring.cs:135` | 3 | `CellSize = +Inf` passes the `> 0` check: degenerate output with success. | silent garbage |
| S29 | `mesh_ops/TerrainDualContouring.cs:194-199` with `TerrainVolumeDifference.cs:172-191` (`RemoveOverlaps`) | 4 | Overlap pairs are collected under a lock in thread order and not sorted. Which triangle is dropped when a triangle is in two pairs depends on the schedule, so the geometry varies. | nondeterminism (geometry) |
| S30 | `mesh_ops/TerrainDualContouring.cs:175-185` | 4 | The split set is filled in pair order, so leaf and vertex ids vary. | nondeterminism (ids) |
| S31 | `mesh_generators/MarchingCubes.cs:126,228`, `MarchingCubesPro.cs:530`; reached from `MeshWindingNumberSolidify` with `Method = MarchingCubes` | 4 | Vertices and triangles are appended in thread order, so ids vary. Solidify's post-processing breaks ties by id. | nondeterminism (ids, maybe geometry) |
| S32 | `RemoveOccludedTriangles.cs:174-205`, `DSubmesh3Set.cs:81-89`, `MeshSpatialSort.cs:133-166`, `TerrainVolumeDifference.XYOverlapPairs` | 4 | The order of output lists follows the thread schedule; the sets are stable. | nondeterminism (order) |

Reported to the PR manager, not edited here (other sessions own these files): R13 for `Remesher` (fixed by #163). No findings in
`ExactMesh*` or the metrics files (they were not read).

## Fix plan, most severe first

1. Done: R3, R9 (the four grids; `ImplicitFieldSampler3d` and `MarchingCubes.CubeSize` are still open), R10.
2. Done: R1, R18, R20 (relative normalization in `MeshNormals`, a deterministic per-vertex sum, Zero for non-finite sums).
3. Done: R2 (local origin in `LaplacianMeshSmoother`; fixes `SmoothedHoleFill` too).
4. R7 (+ R14): local origin for the `Reducer` quadrics.
5. Done: R5 (`MeshPlaneCut`: degenerate-edge length relative to the bounds diagonal while `DegenerateEdgeTol` has its default, on-plane test relative to the coordinate rounding).
6. R4: clamp or reject cell ranges in the hash grids (no hang on any input).
7. Done: S1, S15 (and the `UnitCross` threshold in `DistLine3Triangle3`); S2, S14 open.
8. Done: S3 to S5 (positions mapped through the `Frame3f` in double); S6 to S10 open.
9. R6, R8, R11 to R13, R15, S11 to S13, S25 to S32.
