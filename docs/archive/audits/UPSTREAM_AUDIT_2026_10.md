# geometry3Sharp — Upstream Bug Audit (October 2026)

> **Historical Record (Status: Archived, 2026-10; translated from German `docs/Analyse-2026-10.md`):**
> This document records the initial October 2026 audit of upstream `gradientspace/geometry3Sharp` (`master` commit `ece3364`) and the 15 fix rounds that addressed those findings. For the current list of user-visible behavioral differences versus upstream `1.0.324`, see [docs/UPSTREAM_CHANGES.md](../../UPSTREAM_CHANGES.md).

Codebase audited: upstream `master` `ece3364` (fast-forward baseline). All 183 upstream issues (bug candidates in detail) and every directory class-by-class were reviewed.

**Method:** Reading source code and running small repro programs on .NET 8 against the full library.

**Status column:**
- **C** (`B` in original) = confirmed by repro (wrong output or exception observed)
- **S** = certain by code inspection
- **P** = plausible, not executed

**Severity:** **C** (`K`) = critical, **H** = high, **M** = medium, **L** (`N`) = low.

Findings refer to `ece3364`; several (such as `Matrix3d/f` constructor, `MeshMeshCut:208`, `IndexPriorityQueue.Contains`, `DistPoint2Circle2`, and `Dijkstra.Compute_Dense`) were already fixed in early commits.

---

## Status of Fix Rounds (Rounds 1–15)

- **Round 1 (one-liners, `ReviewFixesTests.cs`):** Fixed `MathUtil.InvSqrt`, `Index3i`/`Index4i` copy constructors, `MatrixUtil.Transpose3x3`, `DijkstraGraphDistance.update_neighbours_dense`, `BiArcFit2` (`arc2`), `ContOrientedBox3.ResultValid`, `BaseCurve2.GetMinTime`, `DistTriangle3Triangle3` (closest points), `LinearFalloff.Duplicate`, `ImplicitPoint2d.InBounds`, `EdgeSpan.CheckValidity`, `MarchingCubesPro.edge_hash`, `MeshPlaneCut.FillHoles`, and `DMeshAABBTree3.find_any_intersection`.
- **Round 2 (I/O, `IOFixesTests.cs`):** Fixed `CultureInfo.InvariantCulture` parsing in `OBJReader` (incl. `.mtl`), `STLReader`, and `OFFReader`; invariant formatting in `OBJWriter` (incl. `.mtl`), `OFFWriter`, and `STLWriter` even when given a culture-specific `TextWriter`; `.mtl` flushing; closing material file handles in `OBJReader.ReadMaterials`; loop-reading in `BufferUtil.DecompressZLib`; and exception propagation in `ParallelStream.Run` (benefiting binary `STLWriter`).
- **Round 3 (Solvers, `SolverFixesTests.cs`, ~250 matrices):** Fixed `SingularValueDecomposition` (upstream #168: Householder accumulation on already-reduced rows/columns, left Givens sign in `DiagonalEntriesNonzero`, `MathUtil.Epsilon` instead of `double.Epsilon`) and `SymmetricEigenSolver` (consistent Householder application, `GetEigenvector(c)` permutation flag check, `IsRotation()` transposition parity)—benefiting `OrthogonalPlaneFit3`, `GaussPointsFit3`, `QuadraticFit2`, `MeshICP`, and `DualContouring` QEF solves.
- **Round 4 (Threading, `ThreadingFixesTests.cs`):** Synchronized `DMeshAABBTree3` / `PointAABBTree3` FastWinding cache builds under a lock with `volatile` timestamps (upstream #72); replaced `BitArray` with `bool[]` for `Remesher`/`RemesherPro` `vModifiedV` and `RemoveOccludedTriangles` (`PerVertex`).
- **Round 5 (Generators, `GeneratorOrientationTests.cs`):** Fixed inward winding (negative volume) in `OpenCylinderGenerator`, `CappedCylinderGenerator`, `ConeGenerator`, `VerticalGeneralizedCylinderGenerator`, and `Radial3DArrowGenerator` (`Clockwise = false` now faces outward). Also fixed stray `(0,0,0)` wedge triangles in cylinders/cones/discs, cone normal formula, and `ConeGenerator` `NoSharedVertices` zero-area triangles.
- **Round 6 (Curves, `CurveFixesTests.cs`, 23 tests):** Fixed `BSplineBasis.Clone` / `NURBSCurve2.Clone`, `BezierCurve2.Clone` (degree 2), `Arc2d.Bounds` (negative angles), `Arc2d.Transform` / `Circle2d.Transform` mirroring, `Polygon2DCurve` closing edge / `TangentT` / `SampleArcLength`, `PolyLine2DCurve` `ParamLength = VertexCount - 1`, `PlanarComplex.Add` mutating caller inputs, `CurveSampler2` duplicate closing vertex, `ParametricCurveSequence2`, `GeneralPolygon2d.Contains(Polygon2d)` with holes, `DPolyLine2f.OrderVertices`, `CurveUtils.IterativeSmooth`/`InPlaceSmooth`, `CurveResampler`, `CurveUtils2.SplitToTargetLength`, `DGraph2Util.FindRayIntersection` / `ChainOpenPaths`, `DGraph2Resampler` on empty graphs, `PolySimplification2`, `Polygon2d.Chamfer`, `Circle3d(center, radius)` right-handed frame, and periodic NURBS intervals.
- **Round 7 (Mesh classes, `MeshFixesTests.cs`, 24 tests + `MeshEditorBowtieTests.cs` + `DSubmesh3Tests.cs`):** Fixed `DMesh3.CompactInPlace` (`MapV` & timestamp), `EnableTriangleGroups`/`AllocateTriangleGroup`, `Copy` timestamp/groups, `SetTriangle` reorientation, `DMesh3Builder.Build` with groups (#104), `NTMesh3` boundary/poke/copy, `MeshEditor` rollback on errors, `MeshDecomposition` min/max, `SimpleMesh` attribute preservation, `EdgeLoop.IsSameLoop`, `MeshMeasurements.MassProperties(..., bodyCoords)` & `ExtremeVertices`, `MeshNormals.Compute` with `VertexF`, `FaceGroupUtil` group `-1`, `MeshTransforms.Rotate(..., Quaternionf)` in double, `MeshWeights.VoronoiArea`/`CotanCentroid`, `MeshConstraintUtil` `setID`, `Remesher.update_after_split` `FixedSetID == 0`, `MeshEditor.DisconnectBowtie`, and `DSubmesh3` properties (`ComputeTriMaps`, `WantComponents`, `OverrideGroupID`).
- **Round 8 (Degenerate inputs, `DegenerateFixesTests.cs`, 8 tests):** Fixed `IntrTriangle3Triangle3` (#85) and `Segment2d.Intersects` (#137) on degenerate triangles and zero-length segments.
- **Round 9 (`OBJReader`, `OBJReaderFixesTests.cs`, 7 tests):** Fixed `Triangle.is_complex` self-comparison bug and `BuildMeshes_ByMaterial` UV/group/missing-index handling.
- **Round 10 (`math/`, `MathFixesTests.cs`, 19 tests):** Fixed `MathUtil.SolveQuadratic`, `Matrix2d/f.Inverse` returning shared `Zero`, `Box3d/f.MinExtent`, `Vector2dTuple4` setter, `Ray3f.DistanceSquared`, `Quaterniond/f.SetToSlerp` (`dot < 0`), `MathUtil.PlaneAngleSignedD` (antiparallel `180°`), `Interval1d/1i` negation, `AxisAlignedBox3d↔3f` empty conversion, `Segment2d.WhichSide`, `PrimalQuery2d.ToLine`, and `TransformSequence2` (`ScaleIsUniform`, `TransformN`).
- **Round 11 (Parallel fixes across 5 areas):** `solvers/`/`intersection/`/`comp_geom/` (PR #21, `SolverIntersectionFixesTests.cs`, 29 tests), `spatial/`/`distance/`/`queries/`/`containment/` (PR #22, `SpatialFixesTests.cs`, 35 tests), `implicit/` (PR #20, `ImplicitFixesTests.cs`, 13 tests), `core/`/`io/`/`color/`/`approximation/` (PR #24, `CoreIOFixesTests.cs`, 45 tests), and `mesh_ops/`/`mesh_generators/`/`mesh_selection/` (PR #23, `MeshOpsFixesTests.cs`, 26 tests).
- **Round 12 (Design decisions, `DesignDecisionFixesTests.cs`, 9 tests):** `MeshBoolean.Compute` seam welding & closedness return, `[Obsolete]` on `Colorf(int,int,int,int)` (pointing to `Colorf.FromBytes`) and `DVector.Clear()` (pointing to `Fill`/`resize(0)`), fresh `ReadOptions.Defaults` per access, `MeshScalarSamplingGrid` outside-band values, `DMeshAABBTree3.WindingNumber` cache lock, and `MergeCoincidentEdges` buffer-growth infinite loop (>1,024 hits).
- **Round 13 (`MeshBoolean`, `MeshBooleanSeamTests.cs`, 5 tests):** Short boundary-edge collapse, re-weld, and 3-edge seam-triangle fill (closing 119/120 random sphere pairs).
- **Round 14 (Low-severity items):** `math/` (PR #37, `MathLowFixesTests.cs`, 36 tests: `Equals(object)`, invariant `ToString`, `ClampAngle`, `sort_indices`, empty int boxes, `Matrix3*.Inverse`, `Box2d(Segment2d)`, `Frame3f.ToFrame`, `Interval1i` `int.MaxValue`) and `mesh/`/`curve/` (PR #36, `MeshCurveLowFixesTests.cs`, 17 tests: `RemesherPro.TrackedProjectionPass`, `AddTriangleStrip`, `EdgeSpan.IsSameSpan`, `DGraph*.GetBounds`, `Timestamp++`, `PlanarComplex.FindSolidRegions`, `BezierCurve2.Reverse`, `Ellipse2d.FromCoefficients`, thread-safe `NURBSCurve2`).
- **Round 15 (Remaining decisions, `BoundsAndMirrorFixesTests.cs`, 6 tests):** `DMesh3.GetBounds()` returning `AxisAlignedBox3d.Empty` on empty meshes, `MeshTransforms.Scale(..., bFixOrientation)` overload, and `MeshTransforms.FlipLeftRightCoordSystems(Frame3f)`.

---

## 1. Upstream Issues

### 1.1 Confirmed Bugs Present in Upstream `ece3364`

| Issue | Title | Root cause (`file:line`) | Sev | Status | Fix |
|---|---|---|---|---|---|
| #215 | `DistPoint2Circle2` incorrect distance | `distance/DistPoint2Circle2.cs:63`: `Center + Radius` added scalar to both x and y | M | C | `Center + new Vector2d(Radius, 0)` |
| #211 | `ContOrientedBox3.ResultValid` always false | `containment/ContBox3.cs:15-35`: `ResultValid` never set | M | C | Set `ResultValid = true` after `Box.Contain` |
| #168 | SVD test case fails | `solvers/SingularValueDecomposition.cs:392,431,506`: zero-length Householder accumulated; wrong Givens `sn` sign on rank-deficient matrices | **C** | C | Guard `length > 0`; negate `sn` in `GivensRotation` |
| #154 | `OBJWriter` does not flush `.mtl` | `io/OBJWriter.cs:252`: `StreamWriter` never flushed | H | C | `w.Flush()` / `leaveOpen: true` |
| #138 | `MeshMeshCut` wrong coordinate | `mesh_ops/MeshMeshCut.cs:208`: `find_existing_vertex(isect.point0)` instead of `(v)` | **C** | C | Pass `(v)` |
| #137 | `Segment2d` ∩ zero-length segment | `math/Segment2.cs:213-245`: zero direction entered parallel branch asymmetrically | M | C | Handle zero-length segments as points |
| #133 | `OBJReader` culture-dependent | `io/OBJReader.cs:366-395,642-653,705-707`: under `de-DE`, `1.5` parsed as `15` | H | C | Use `CultureInfo.InvariantCulture` everywhere |
| #53 | `OBJWriter` culture-dependent | `io/OBJWriter.cs:86-110,261-277`: wrote `v 1,5 ...` when called directly | L–M | C | Format with `CultureInfo.InvariantCulture` |
| #176 | `TubeGenerator` unequal segment lengths | `curve/CurveUtils.cs:24`: length-weighted tangent, no miter scaling | M | C | Normalized edge-direction tangent + `1/cos(θ/2)` scaling |
| #110 | `LaplacianMeshSmoother` crash | `mesh_ops/LaplacianMeshSmoother.cs:397-405`: `constrained` was `null` when region had no border | M | C | Always allocate `constrained` |
| #72 | `FastWindingNumber` parallel race | `spatial/PointAABBTree3.cs:229-231,307-313`, `DMeshAABBTree.cs:1314-1317`: unsynchronized cache build | H | C | Build locally and publish under `lock` |
| #85 | `IntrTriangle3Triangle3` degenerate triangles | `intersection/IntrTriangle3Triangle3.cs:162,179`: zero normal broke separating-axis test | L | C | Detect `N.LengthSquared < eps` and fall back to segment/point |
| (#104) | *Additional:* `DMesh3Builder.Build` with groups | `mesh/DMesh3Builder.cs:183-191`: indexed group by loop index and ignored `AppendTriangle` return | M | C | Pass `groups[i]` to `AppendTriangle` and check return |

### 1.2 Already Fixed Upstream Before Audit
`#44` (`MeshIsoCurves.compute_full`), `#42` (`CurveUtils.ArcLength`), `#28` (`StandardMeshWriter` culture), `#19` (`Vector2.Cross`).

### 1.3 Not a Bug (Usage, Misunderstanding, or Intended Behavior)

| Issue | Reason |
|---|---|
| #155 | `Segment2d.Direction` is normalized in place in constructor; `PointAt` is correct |
| #136 | `Normalize()` intentionally returns the old length |
| #11 | Float-normalized vectors are not unit length in `double`; calling `Normalize()` afterward is expected |
| #179 | `PositionF` did not shift (`v * 1f`) |
| #157 | `PreserveBoundaryLoops` intentionally does not pin corners |
| #131 | Inverted input winding; call `ReverseOrientation()` |
| #117 | `GetEdgeT` intentionally returns `-1` on boundary edges |
| #111, #37, #69, #175 | Unity / build environment setup (`G3_USING_UNITY`, .NET version, external DLL) |
| #62 | `CompactCopy` copies normals; `Reducer` does not update normals (call `QuickCompute`) |
| #56 | `ContOrientedBox3` is a covariance OBB, not minimal-volume OBB |
| #43, #23 | Unity cube vertices are unwelded (every edge is a boundary edge) |
| #32 | Cap center outside profile (`OverrideCapCenter` added) |
| #145 | Extra vertices are expected behavior of `MeshInsertPolygon` |
| #135 | Local vs. world coordinates mixed up in Unity |
| #104 | Non-manifold edge (`DMesh3` allows at most 2 triangles per edge; group crash was a separate bug above) |
| #7 | Subsequent behavior explained by fixed boundary/UV seams; original crash already fixed |

### 1.4 Feature Requests, Questions & Non-Reproducible Issues
- **Feature requests / questions:** `#153`, `#134`, `#70`, `#60`, `#50`, `#45`, `#35`, `#30` (plus ~130 other feature requests/questions).
- **Undecidable (missing inputs):** `#199` (images only), `#181` (`FindNearestTriangle` verified against brute force), `#173` (missing STL).

---

## 2. Class-by-Class Code Review

### 2.1 `math/`

| Location | Class / Method | Sev | Status | Defect → Fix |
|---|---|---|---|---|
| `MathUtil.cs:222` | `MathUtil.InvSqrt` | H | C | `f / Math.Sqrt(f)` returned `√f` instead of `1/√f`, breaking `GenerateComplementBasis` → `1.0 / Math.Sqrt(f)` |
| `Matrix3d.cs:53`, `Matrix3f.cs:53` | Constructor `(Func<int,int,…>)` | H | C | `matF(1,2)` instead of `matF(2,1)` in `Row2` |
| `IndexTypes.cs:15,322` | `Index3i`, `Index4i` copy ctors | H | C | `c = copy.b` → `copy.c` |
| `MatrixUtil.cs:47-57` | `Transpose3x3` | H | C | `MTranspose[8]` never assigned |
| `MathUtil.cs:617-636` | `SolveQuadratic` | H | C | `b = 0` → `Sign(0) = 0` division by zero; `a = 0` → `±Inf` |
| `Matrix2d.cs:6,11,99` (`2f` too) | `Matrix2d` mutable class | H | C | `Inverse()` returned shared mutable `Matrix2d.Zero` (`Ellipse2.FromCoefficients` mutated it) |
| `Box3.cs:158,559` | `Box3d/f.MinExtent` | M | C | `Min(x, Max(y, z))` → `Min(x, Min(y, z))` |
| `VectorTuple.cs:116` | `Vector2dTuple4` setter | M | C | Indices 0 and 1 swapped (corrupted `Box2d.ComputeVertices`) |
| `Ray3.cs:115-120` | `Ray3f.DistanceSquared` | M | C | Treated ray as line (did not clamp `t < 0`) |
| `Segment2.cs:110-121` | `Segment2d.WhichSide` (instance) | M | C | `P0`/`P1` swapped, opposite sign vs. static overload |
| `PrimalQuery2d.cs:100-109` | `ToLine(..., out order)` | M | C | `sqrLength` computed from wrong vector |
| `TransformSequence2.cs:59,215` + `Segment2.cs:156` | `ScaleIsUniform`, `TransformN` | M | C | Compared scale to pivot; scaled direction instead of normalizing (quadratic scaling) |
| `AxisAlignedBox3d.cs:369-376` | `3f`↔`3d` conversion | M | C | Empty box became infinite box |
| `AxisAlignedBox2d.cs:236`, `2f.cs:212` | `Contains`/`Intersects` | M | C | 2D strict `<` vs. 3D inclusive (box did not contain itself) |
| `Interval1d.cs:128`, `Interval1i.cs:105` | Unary `-`, negative `*` | M | C | Returned inverted interval (`a > b`) |
| `Quaterniond.cs:242`, `Quaternionf.cs:283`, `Frame3f.cs:436` | `Slerp`, `Interpolate` | M | C | Missing `dot < 0` handling (long-way rotation or zero quaternion) |
| `MathUtil.cs:317-324` | `PlaneAngleSignedD` (2D) | M | C | Antiparallel returned `0°` instead of `180°` |
| `Box3.cs:42`, `Quaterniond.cs:195`, `Vector4f.cs:68,77`, `AxisAlignedBox3f.cs:172`, `AxisAlignedBox2i/3i` | Minor precision/clamping/overflow | L | C | Float extent/constants in double types, unclamped `Contract`, empty int-box overflow |
| Vector/Index/Box types, `IndexUtil.cs:245`, `MathUtil.cs:111-166,650`, `Box3.cs:196` | `Equals(object)`, `ToString()`, `sort_indices`, `ModuloIteration`, `ClampAngle`, `Box3d.Contain` | L | C | Threw on `null`/foreign type; culture-dependent `ToString()`; duplicate sort bug; step `31337`; single `±360` clamp |

Minor blemishes noted: `Interval1i.RangeInclusive` doc mismatch, unused parameters, `(int)` cast vs. floor in `AxisAlignedBox*i.Contain`, `double.Epsilon` thresholds, left-handed `Box2d(Segment2d)`, mutating `Frame3f.ToFrame(ref Box)`, inconsistent `==`/`Equals`, `Interval1i` `int.MaxValue` loop.

### 2.2 `mesh/`

Core topology operators (`SplitEdge`, `FlipEdge`, `CollapseEdge`, `PokeTriangle`, `MergeEdges`) passed validity checks with and without attributes.

| Location | Class / Method | Sev | Status | Defect → Fix |
|---|---|---|---|---|
| `DMesh3.cs:2282-2446` | `CompactInPlace` | H | C | `MapV` returned `int.MinValue` for unmoved vertices; missed timestamp update; empty crash |
| `NTMesh3.cs:479` | `GetOrientedBoundaryEdgeV` | H | C | `triangles[ti]` instead of `3 * ti` |
| `MeshDecomposition.cs:102,188` | `BuildLinear` / `extract_submesh` | H | C | `if ... else if` corrupted min/max bounds |
| `SimpleMesh.cs:161-176` | `AppendVertices` | H | C | Dropped normals/colors/UVs; wrote color into `Normals` |
| `MeshCaches.cs:20` | `MeshTriInfoCache` | H | C | Allocated `TriangleCount` instead of `MaxTriangleID` (crashed on non-compact meshes) |
| `DMesh3_edge_operators.cs:164-251` | `SetTriangle` | M | C | Looked up edges before removal → exception on reorientation |
| `DMesh3.cs:253-274,641,1298` | `Copy`, `EnableTriangleGroups`, `AllocateTriangleGroup` | M | C | Stale groups/timestamp; reused/skipped group IDs |
| `MeshEditor.cs:125,157,287,330,604-636` | Fan/stitch rollback, `DisconnectBowtie` | M | C | Incomplete rollback left orphaned triangles; `DisconnectBowtie` cut cracks |
| `NTMesh3.cs:116,1398` | `NTMesh3(DMesh3)`, `PokeTriangle` | M | C | Omitted attributes on copy; ignored `bary` |
| `Remesher.cs:244,458,566`, `RemesherPro.cs:454…`, `MeshConstraintUtil.cs:162,197` | Parallel `BitArray`, `setID`, `next_edge`, `FixedSetID` | M/L | C/S | Parallel `BitArray` race; dropped `setID`; step `31337`; `FixedSetID > 0` instead of `>= 0` |
| `DSubmesh3.cs:35`, `EdgeLoop.cs:293`, `EdgeSpan.cs:229`, `SimpleMesh.cs:191-213` | Submesh init, loops, spans, groups | M | C | Immediate `compute()` ignored initializers; wrong vertex lookup; always-true `CheckValidity`; dropped groups |
| `MeshMeasurements.cs:147,392-409`, `MeshWeights.cs:44-56,83`, `MeshNormals.cs:76`, `FaceGroupUtil.cs:78,110`, `MeshTransforms.cs:46`, `DMesh3Builder.cs:183` | Measurements, weights, normals, groups, transforms | M | C | Ignored `bodyCoords`/`VertexF`; `else if` bug; normalized-edge Voronoi area; right-angle cotan bug; group `-1` crash; unrotated float `Rotate` |

### 2.3 `curve/`, `shapes3/`

| Location | Class / Method | Sev | Status | Defect → Fix |
|---|---|---|---|---|
| `BSplineBasis.cs:82` → `NURBSCurve2.cs:426`, `BezierCurve2.cs:221` | `Clone` | H | C | Missing `mBD0` / degree-2 NRE |
| `Arc2.cs:159-169,144` | `Arc2d.Bounds`, `Transform` | H/M | C | Negative angle index crash; mirroring gave complementary arc |
| `Polygon2d.cs:806,819,722`, `PolyLine2d.cs:394,410`, `PolyLine2f.cs:135-191` | Curve adapters, `Chamfer`, `OrderVertices` | H/M | C | `SampleArcLength` threw; missed closing edge; off-by-one `ParamLength`; empty-list crash; swapped convex/concave chamfer |
| `CurveUtils.cs:180-191`, `CurveResampler.cs:19,84`, `CurveSampler2.cs:50-89`, `CurveUtils2.cs:267` | Smoothing & resampling | H/M | C | Open endpoints collapsed to origin; `GetVertex(N)` crash; duplicate closing vertex |
| `DGraph2Util.cs:241,389`, `DGraph2Resampler.cs:83,177`, `DGraph.cs:344` | Graph queries & resampling | H/M/L | C | Iterated vertices instead of edges; `% 0` on empty graph; `SplitEdge` doc mismatch |
| `BaseCurve2.cs:22`, `PlanarComplex.cs:138,918`, `ParametricCurveSequence2.cs:19`, `GeneralPolygon2d.cs:181,208`, `PolySimplification2.cs:109,146`, `NURBSCurve2.cs:449-487`, `Circle3.cs:38` | Misc curve/polygon bugs | M/L | C | `GetMinTime` returned `mTMax`; mutated input polygon; ignored `closed`; hole check bug; squared vs. linear threshold; periodic NURBS; left-handed `Circle3d` |

### 2.4 `spatial/`, `distance/`, `queries/`, `containment/`

Verified correct against brute force: `DMeshAABBTree3` nearest/hit queries (all build strategies), `PointAABBTree3`, `WindingNumber`, SDF grid (serial/parallel), and multiple `Dist*` classes.

| Location | Class / Method | Sev | Status | Defect → Fix |
|---|---|---|---|---|
| `DMeshAABBTree.cs:604,1314,1587`, `PointAABBTree3.cs:229,519` | `find_any_intersection`, FastWinding cache, `build_top_down` | H/M | C/S | Infinite recursion (`oBox` vs `oChild1`); parallel cache race (#72); NaN triangles corrupted counts |
| `MeshWindingNumberGrid.cs:155`, `MeshScalarSamplingGrid.cs:151`, `MeshSignedDistanceGrid.cs:971,993,1020` | `make_grid`, `point_triangle_distance` | H/M | C | `BufferCells != 1` out-of-range; degenerate-triangle NaN / `/0` |
| `DistTriangle3Triangle3.cs:90`, `DistPoint3Triangle3.cs:132`, `MeshQueries.cs:269,462` | Distance queries | H/M/L | C | Swapped closest points (94/300 random pairs); degenerate NaN; unsquared `fNearestSqr` |
| `GridIndexing.cs:80ff,215-230`, `GridIndexing2.cs:149-163`, `DenseGrid2.cs:67`, `Bitmap3.cs:126`, `DSparseGrid3.cs:109` | Grid indexers & grids | H/M/L | C | Negative-multiple floor bug; ignored shifts; off-by-one border; in-place `Filter`; `Infinity` density |
| `DCurveBoxTree.cs:57,132,301`, `Polygon2dBoxTree.cs:162,220`, `BasicIntersectionTargets.cs:11,24,82`, `DistPoint2Circle2.cs:63`, `DistPoint3Cylinder3.cs:91,155`, `TilingUtil.cs:22,77`, `IntersectionUtil.cs:210`, `RayIntersection.cs:19,40,70`, `ContMinBox2.cs:54`, `ContBox3.cs:15`, `SegmentHashGrid.cs:54` | Trees, targets, distances, containment | M/L | C/P | Unsquared distance; 0/1-vertex infinite loop; null `BaseTarget` / hardcoded `AxisY`; #215; axis NaN; 1 vs. 25 tiles; `LineSphere` vs `RaySphere`; interior `Math.Abs`; collinear `NotImplementedException`; #211; unlocked `MaxExtent` |

### 2.5 `core/`, `io/`, `color/`, `approximation/`

| Location | Class / Method | Sev | Status | Defect → Fix |
|---|---|---|---|---|
| `DijkstraGraphDistance.cs:233,545` | `Compute_Dense`, `update_neighbours_dense` | **C**/H | C | Overwrote distance with `max_value`; failed to update `nbr.distance` |
| `IndexPriorityQueue.cs:90,197,353`, `DynamicPriorityQueue.cs:66,290` | `Contains`, `IsValidQueue`, `Clear` | H/L | C/S | Stale `Contains` after dequeue (corrupted `Reducer` priorities); `<` vs. `<=`; retained refs |
| `OBJReader.cs:65,67,203,276,396,426,597`, `OBJWriter.cs:42-110,252` | OBJ/MTL I/O | H/M/L | C | `is_complex` self-comparison; culture bugs (#133/#53); unflushed `.mtl` (#154); handle leak; multi-mesh `vn`/`vt` offsets; missing `//n` index crash |
| `STLReader.cs:137,209`, `STLWriter.cs:60,107`, `OFFReader.cs:61,84,109`, `OFFWriter.cs:57`, `gParallel.cs:199` | STL/OFF & `ParallelStream` | H/M | C | Culture bugs; `ParallelStream` killed process or spun forever; OFF polygons > 3 vertices truncated |
| `BufferUtil.cs:151,548`, `HBitArray.cs:130,156`, `BiArcFit2.cs:240` | ZLib, `HBitArray`, `BiArcFit2` | H/L | C | Single `Read` truncated ZLib on .NET 6+; premature parent clear & skipped last block; `arc1` instead of `arc2` |
| `Util.cs:88,131,140`, `GaussPointsFit3.cs:118,138,148,171`, `OrthogonalPlaneFit3.cs:56`, `gSerialization.cs:393`, `DVector.cs`, `Colorf.cs:44,111`, `Colorb.cs:16,18`, `SVGWriter.cs:418`, `SmallListSet.cs:144`, `Snapping.cs:274`, `Units.cs:66` | Misc core/color/fitting | M/L | C/P | `IsBinaryStream` default `-1`; `ParseInt` returned `float`; weighted fit off by `1/N` & transposed axes; diagonal covariance rejected; dropped isolated vertices; `DVector` empty/`pop_back`; color shadowing/alpha; miles `1609.34` |

### 2.6 `intersection/`, `implicit/`, `comp_geom/`, `solvers/`

| Location | Class / Method | Sev | Status | Defect → Fix |
|---|---|---|---|---|
| `SingularValueDecomposition.cs:101,392,431,506` | `Bidiagonalize`, `DiagonalEntriesNonzero`, `Solve` | **C**/M | C/S | Identity matrix gave wrong `U·S·V^T` (#168); Givens sign bug; `double.Epsilon` |
| `SymmetricEigenSolver.cs:228,277,304,403` | `Tridiagonalize`, `GetEigenvector`, `IsRotation` | H/M | C | Same Householder bug; wrong permutation index; wrong parity |
| `DenseMatrix.cs:101-279`, `SparseSymmetricCG.cs:60,67,298,312`, `SparseMatrix.cs:151`, `PackedSparseMatrix.cs`, `CholeskyDecomposition.cs:99` | Linear solvers | H/M/L | C | Non-square indexing bugs; `RHS = 0` → NaN; converged RHS kept iterating; asymmetric product stored symmetrically; empty row crash; `N = 1` parallel crash |
| `IntrLine2Triangle2.cs:156,168`, `IntrRay3Triangle3.cs:68,76,133`, `IntrTriangle2Triangle2.cs:103`, `IntrTriangle3Triangle3.cs:67,162` | Intersections | M/L | C | Degenerate exception; absolute parallel tolerance (`< 1e-3`); stale `Type`/`Quantity`; CCW assumption; #85 |
| `ImplicitOperators.cs:38,126`, `Implicit2d.cs:59,76`, `FalloffFunctions.cs:45`, `MarchingQuads.cs:388,411`, `GridImplicits3d.cs`, `CachingGridImplicit3d.cs`, `CachingMeshSDF.cs` | Implicits | H/M/L | C | Missing parentheses; `x` vs. `y`; ignored radius; `LinearFalloff.Duplicate` returned `WyvillFalloff`; shifted bounds & dirty reuse in `MarchingQuads`; missing `/ CellSize`; `(int)` vs. floor; float cache marker loss |
| `ConvexHull2.cs:196`, `Arrangement2d.cs:270` | 2D computational geometry | H/M | C | Step `31337` dropped hull points at `N = k · 31337`; squared distance used as snap radius |

### 2.7 `mesh_ops/`, `mesh_generators/`, `mesh_selection/`

Verified clean at audit time: hole fillers (`Simple`/`Minimal`/`Smoothed`/`Auto`), `MeshAutoRepair`, `Extrude*`, `MarchingCubes` (+ `Pro` sphere), `MeshPlaneCut` with cap, box/sphere/revolve/tube generators.

| Location | Class / Method | Sev | Status | Defect → Fix |
|---|---|---|---|---|
| `MeshMeshCut.cs:208` | `find_segments` | **C** | C | See #138 (corrupted mesh validity) |
| `CylinderGenerators.cs` (several) | Open/Capped cylinder, cone, generalized cylinder | H | C | Inward winding (negative volume), contradicting normals and all other generators |
| `MarchingCubesPro.cs:198-200` | `edge_hash` | H | C | `const int 1 << 60` wrapped to `1 << 28` → hash collisions past 4096 cells in y (`1L << 60`) |
| `RemoveOccludedTriangles.cs:132` | `Apply` (`PerVertex`) | H | C | Parallel writes to `BitArray` (non-deterministic, affected `MeshAutoRepair`) |
| `CylinderGenerators.cs:29,95,232,247,266,368`, `DiscGenerators.cs:21,72`, `BoxGenerators.cs:25`, `PlaneGenerators.cs:319,355`, `GenCylGenerators.cs:110,144`, `RevolveGenerator.cs:108,277`, `PointsMeshGenerators.cs:168` | Mesh generators | M/L | C | Stray `(0,0,0)` wedge triangles; cone normal bug; scalar `Box.Center[i]`; `RoundRect` UV/border; twisted closed `TubeGenerator` seam; cap normals `±Z` vs `±Y`; missing normals |
| `MeshPlaneCut.cs:239`, `MergeCoincidentEdges.cs:409`, `LaplacianMeshSmoother.cs:115,397`, `LaplacianMeshDeformer.cs:115`, `MeshVertexSelection.cs:155,291`, `MeshFaceSelection.cs:34,342`, `MeshIsoCurves.cs:64,318,342`, `MeshBoolean.cs:55`, `MeshInsertUVPolyCurve.cs:202`, `MeshBoundaryLoops.cs:207,217`, `MeshTopology.cs:130`, `MeshICP.cs:169` | Mesh operations & selections | M/L | C/S | Inverted `FillHoles` check; `continue` in wrong loop; #110; default-filter NRE; unresized `BitArray`; unreset `EdgeLocations`; open unwelded `MeshBoolean`; boundary-loop jump; `vid > 0` vs `>= 0`; `M.Set(vid,vid)` vs `(i,i)` |

---

## 3. Cross-Cutting Themes

1. **Culture dependence:** Direct reader/writer calls silently misparsed numbers under non-English locales (e.g., `de-DE`).
2. **Threading:** Parallel writes to `BitArray` (`Remesher`, `RemesherPro`, `RemoveOccludedTriangles`), unsynchronized FastWinding caches, and `ParallelStream` exception handling.
3. **Stride `31337`:** `(i + 31337) % N` silently skipped elements whenever `gcd(31337, N) != 1`.
4. **`double.Epsilon` as tolerance:** Used in multiple places (`SVD`, `Matrix3.Inverse`, `Segment2d.Intersects`) where machine epsilon was intended.
5. **Degenerate inputs:** NaNs or exceptions in distance and intersection routines on collinear triangles, zero-length segments, and on-axis points.
6. **Linear solvers:** `SVD` and `SymmetricEigenSolver` failed on structured (diagonal, axis-aligned, rank-deficient) matrices, propagating to fitting and `MeshICP`.

## 4. Recommended Fix Order (Historical Plan)

1. High-impact one-liners (`InvSqrt`, `Matrix3`, `Index3i/4i`, `Transpose3x3`, `MeshMeshCut:208`, `Dijkstra`, `IndexPriorityQueue`, `OBJReader.is_complex`, `BiArcFit2`, `MarchingCubesPro`, `DistTriangle3Triangle3`, `find_any_intersection`, `ContOrientedBox3`, `DistPoint2Circle2`, `BaseCurve2`, `MeshPlaneCut.FillHoles`, `EdgeSpan`, `LinearFalloff`, `ImplicitPoint2d`).
2. I/O (`InvariantCulture`, `.mtl` flush, handle leak, `DecompressZLib`, `ParallelStream`).
3. Solvers (`SVD`, `SymmetricEigenSolver`).
4. Threading (`bool[]` instead of `BitArray`, FastWinding locks).
5. Generators (cylinder/cone outward winding—user-visible change).
6. Remaining medium/low findings by area, each paired with a unit test project and regression tests.
