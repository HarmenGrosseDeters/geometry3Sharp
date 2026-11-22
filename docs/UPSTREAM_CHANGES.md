# Changes for Users of geometry3Sharp 1.0.324

This fork maintains public API compatibility with upstream `geometry3Sharp 1.0.324` (commit `ece3364`), enforced on `dotnet pack`. A number of existing methods now give improved or corrected results—almost all bug fixes, scale-invariance fixes, and determinism improvements. New classes are not listed here (see [README.md](../README.md) and [OPERATIONS.md](OPERATIONS.md)).

---

## Spatial Trees, Rays, Intersections & Distance Queries

| Class / member | 1.0.324 | Now |
|---|---|---|
| `DMeshAABBTree3.Build()` (empty mesh / leaf count) | Bottom-up strategies built invalid root and queries threw; `TopDownLeafMaxTriCount < 2` stack-overflowed | Valid empty leaf with standard no-hit sentinels; `TopDownLeafMaxTriCount < 2` clamped to `2` |
| `DMeshAABBTree3` tree-vs-tree queries (`TestIntersection`, `FindAllIntersections`, `FindNearestTriangles`) | Checked modification timestamp on receiver mesh only | Also rejects a modified or unbuilt argument tree before traversal; rebuild both trees after edits |
| `DMeshAABBTree3` box padding (`FindNearestTriangle`, `NextBoxF`/`NextBoxDF`) | Fixed `6e-6` padding changed traversal order and tie winners across scales | Padded by `2^-40 * max(meshSize, originDist)`; bit-identical under `2^k` scaling; tighter boxes at unit scale |
| `DMeshAABBTree3.FindNearestHitTriangle` (tied ray hits) | Returned first hit in traversal order (scale-dependent) | Returns lowest triangle ID among tied hits (matches linear search; `2^k` scale-invariant) |
| `IntrRay3Triangle3` (`Find`, `Intersects`) & `IntersectionUtil.Intersects` | Rounded edge tests leaked rays on shared edges/vertices; `~1e-8` / `~1e-4` parallel epsilon missed small triangles | Exact `Orient3d` ray-triangle decision, no parallel tolerance, `2^k` scale-invariant; barycentrics clamped on new hits |
| `DMeshAABBTree3.IsInside` | Parity of `FindAllHitTriangles` miscounted rays through edges/vertices | Symbolic perturbation (Simulation of Simplicity): exact parity for closed, self-intersection-free meshes (use `WindingNumber` on open meshes) |
| `DMeshAABBTree3.WindingNumber` & `MeshWindingNumberGrid` | Hierarchical `WindingCache` suffered `TriSolidAngle` cancellation near edges/faces (`-0.12`/`+1.23` on closed spheres), fell back to O(F) on unwelded closed soups, and returned `NaN` on non-finite queries | Exact `+x` ray-plus-boundary cache (`ExactWindingCache`): exact `Orient3d` integers on closed meshes (and unwelded soups), `+x` row-ray reuse in `MeshWindingNumberGrid`, `2^k` bit-identical across scales, `0` on non-finite queries |
| `DMeshAABBTree3.FastWindingNumber` | Absolute area epsilon `2.2e-16` zeroed tiny triangles; padded box broke far small parts | Scale-free areas and tight-box gating across `2^-200..2^200` (exact sum outside); reaches repair, solidify, deviation, fidelity, and open boolean fast-winding |
| `PointAABBTree3` & `PointAABBTree3.FastWindingNumber` | Fixed `1e-8` split threshold and `1.1e-14` padding broke small clouds and crashed at `> 1.3e8`; `d^3` under/overflowed | Splits every positive spread, tight-box gate for `FWNBeta >= sqrt(3)`, factored distance at extreme magnitudes (`2^-341` / `2^341`); `2^k` scale-invariant |
| `IntrSegment2Segment2`, `IntrLine2Segment2`, `IntrLine2Line2`, `IntrSegment2Triangle2`, `IntrLine2Triangle2` (`Find`) | Absolute `1e-8` constants and rounded center-direction tests misreported touching, collinear and nearly parallel pairs (about 4 % of engineered pairs) and every pair of a triangle below `1e-8`; `NaN` input could hit | Exact closed decision, no tolerance, `2^k` scale-invariant; non-finite input is an `InvalidQuery` (see the note below) |
| `IntrTriangle3Triangle3` (`Test`, `Intersects`, `Find`) | Floating-point separating axes missed coplanar/sliver touches and misreported disjoint pairs within `1e-8` | Exact decision (`ExactTriangleIntersection`) on coplanar, near-parallel (`sin < 1e-4`), sliver, and near-plane pairs; non-finite → no hit |
| `IntrLine3Box3`, `IntrRay3Box3`, `IntrSegment3Box3`, `IntrLine3AxisAlignedBox3`, `IntrRay3AxisAlignedBox3` (`Find`, `Test`, `Intersects`, `FindRayIntersectT`) | A `1e-8` slack and rounded dot products gave false hits and misses (up to 38 % of engineered cases), wrong Point/Segment types, wrong `Test()`, points off by up to `1e15` ulp, `NaN` hits | Exact closed decision, no slack; `NaN`/`Inf` is an `InvalidQuery`; `Test`, `Intersects`, `Find` agree; `2^k` invariant (see the note below) |
| `DistTriangle3Triangle3`, `DistLine3Triangle3`, `MeshQueries.TrianglesDistance` | Squared distances `<= 1e-8` returned `0`; triangles `< 1e-8` treated as parallel | `0` only within 16 ULPs of size/coordinates; `DistLine3Triangle3` handles tiny triangles without parallel cutoff |
| `PointHashGrid2d/3d`, `SegmentHashGrid2d` (`FindNearestInRadius`) | Cell range cast to `int` looped forever or missed points beyond `2^31` cells; empty ranges scanned slowly | Range computed in `double`; large/out-of-range queries scan occupied cells in deterministic order |
| `PlaneIntersectionTarget.RayIntersect` | Computed in `float` (up to `0.5` error at UTM); non-finite rays returned `true` | Computed in `double` (`PlaneFrame` remains `Frame3f`); non-finite hits return `false` |

### Notes on Spatial & Intersection Changes

- **2D line, segment and triangle intersections:** the decision is exact on the rounded ends `Segment2d.P0/P1` (a documented limit of the center/direction/extent representation) and on a line's origin and direction exactly as given (no rounded second point, so equal directions are parallel and no crossing lies at infinity); ends and corners are returned exactly, a crossing in floating point on the segment (accuracy limited by the angle), coordinates near the range limit (1e308) are scaled by a power of two first and stay finite. `IntervalThreshold > 0` (segment classes) keeps the old tolerance route with `DotThreshold` (only used there and by the static line tests). A degenerate triangle (collinear corners) or a segment that collapsed to a point keeps the old route. Two segments that share an input end can still miss each other when the rounding of `P0/P1` separates them (use the threshold). Callers outside the diff: `Segment2d.Intersects/Find` and `Line2d` helpers (`math/Segment2.cs`, `math/Line2.cs`), `DGraph2Util`, `SegmentSet2d` (select the tolerance route), `GraphSplitter2d`, `Arrangement2d`, `Polygon2d` (tolerance route, behaviour unchanged), `MeshInsertUVPolyCurve` (tolerance route).
- **`IntrTriangle3Triangle3`:** `Find()` constructs contact geometry from exact decisions (at least one point whenever `Test()` is `true`; polygon for exact coplanar pairs with `ReportCoplanarIntersection`, segment/point otherwise). Nearly coincident non-coplanar faces now report crossing segments even without `ReportCoplanarIntersection`. Pairs in general position above the near-parallel band keep the fast floating-point path.
- **3D line, ray and segment vs. box classes (`IntrExactBox3`):** the box is the closed solid. An axis-aligned box is its `Min`/`Max`; an oriented `Box3d` is the intersection of the slabs `|dot(x - Center, Axis_i)| <= Extent_i` with exact dot products of the given doubles (the box when the axes are orthonormal, which the old code assumed too; non-orthogonal axes give the slab intersection). A `Segment3d` is the closed segment between its rounded ends `P0`/`P1`. `Solid = false` on `IntrSegment3Box3` means only the surface counts (a segment in the open interior does not hit). `expandExtents` of `IntrRay3Box3.Intersects` and `IntrRay3AxisAlignedBox3.Intersects` moves each face outward by that amount (one rounding of the face). The public `Clip` methods are unchanged but no longer used. Callers outside `intersection/`: `DMeshAABBTree3.box_ray_intersect_t` (`FindRayIntersectT`) and `DCurve3BoxTree` (`IntrRay3Box3.Intersects`); The traversal of `DMeshAABBTree3` uses an internal conservative form (a certified miss, else "may hit"), so its queries are not slower (`benchmarks/IntrBox3Benchmark`); `DCurve3BoxTree` ray queries are 1.6 to 1.9 times slower. Results that can change inside today's range, with an example each: a line passing `1e-9` outside a face was a hit and is a miss; a ray along an edge's extension that touches a corner is a hit with `Point0 = Point1` exactly the corner (`Type` Point, was Segment or Empty by rounding); `LineParam0/1`, `RayParam0/1` and the points are now rounded from the exact contact (a line parallel to a face at `1e-16` distance gave points up to `1e15` ulp off); `Test()` / `Intersects()` equal `Find()` (a ray one ulp beside a corner: `Test()` was true); a segment that ends exactly on a face with `solidBox = false` is a hit (was decided by rounding); `NaN`/`Inf` input is an `InvalidQuery` (could hit).
- **`DMeshAABBTree3.WindingNumber` & `MeshWindingNumberGrid`:** Uses Antipodal/FEWN `+x` ray-plus-boundary decomposition with `ExactPredicates` (`ExactWindingCache`), skipping non-finite triangles and matching the `DMesh3.WindingNumber` solid-angle-sum convention on exact on-surface vertices, edges, and coplanar faces. Coordinate-keyed boundary reduction cancels opposite edges on unwelded closed STL soups (`|∂M| = 0`).
- **`DMeshAABBTree3.FastWindingNumber`:** Uses Barill order-2 far-field approximation down to `~2^-200` (up to ~0.1 documented error vs. exact winding) instead of falling back to exact sums on tiny meshes, eliminating multi-minute stalls on far tiny components.

---

## Mesh Representation, Topology, Generators & I/O

| Class / member | 1.0.324 | Now |
|---|---|---|
| `DMesh3.CompactInPlace()` | Could throw on isolated live vertices above deletion holes | Preserves isolated vertices, attributes, and vertex mapping |
| `DMesh3.RebuildFromEdgeRefcounts()` & `gSerialization.Restore(DMesh3, ...)` | Reused destinations kept stale refcounts/adjacency; truncated or corrupt streams filled zeros or corrupted state | Resets derived topology before rebuilding; throws on inconsistent counts/topology (`GenericReaderError`) or truncated arrays (`EndOfStreamException`) |
| `DMesh3.RemoveVertex(..., bPreserveManifold)` | Order-dependent `Failed_WouldCreateBowtie` after partial triangle deletion | Checks whole fan first; more removals succeed, and failures leave the mesh unchanged |
| `DMesh3.GetBounds()`, `CachedBounds`, `DGraph2/3.GetBounds()` (empty) | Returned zero-size box at origin | Returns `AxisAlignedBox3d.Empty`; sampling grids throw `ArgumentException` on empty meshes |
| `DMesh3.AllocateTriangleGroup()` | Skipped an ID (returned `6` for groups `0..4`) | Returns next unused ID (`5`) |
| `NTMesh3.GetTriFrame` (float) | Left-handed basis (`edge`, `edge × normal`, `normal`) produced wrong quaternion frames | Right-handed basis (`normal × edge`), matching `DMesh3.GetTriFrame` |
| `DSubmesh3Set.Submeshes` & `KeyToMesh` | Populated in worker completion order | Deterministic `TriangleSetKeys` order; last duplicate key wins |
| `MeshSpatialSort.Sort` | Invalid bbox comparator; worker-ordered lists accumulated across calls | Deterministic tie-breaking by `Components` order; clears `InsideOf`/`InsideSet` per call |
| `MeshTransforms.Rotate(mesh, origin, Quaternionf)` | Rotated positions only | Also rotates vertex normals in double precision |
| `MeshMeasurements.MassProperties(..., bodyCoords)` | Ignored `bodyCoords` | Honors `bodyCoords` |
| `MeshNormals.Compute` / `QuickCompute` | Absolute area cutoffs left short/zero normals on small meshes; parallel sum varied in last bits; NaN propagated | Scale-free unit normals (`2^k` bit-identical), deterministic triangle-ID summation order, `Zero` for degenerate or NaN one-rings |
| `MeshUtil.ScaleMesh`, `MeshLoopClosure`, `MeshInsertProjectedPolygon` | Cast positions to `float` before `Frame3f` mapping (up to `0.25` error at UTM) | Maps positions in `double` (frames stay `Frame3f`) |
| `MeshBoundaryLoops` | Bowtie/inconsistent boundaries often fell back to open spans; diagnostic flags persisted across calls | Decomposes bowties into closed loops cleanly; resets flags and `FailureBowties` on every `Compute()` |
| Cylinder/cone/arrow generators (`OpenCylinder`, `CappedCylinder`, `Cone`, `VerticalGeneralizedCylinder`, `Radial3DArrow`) | Triangles faced inward (negative volume) with default `Clockwise = false` | Face outward like all other generators; remove compensating `ReverseOrientation()` / `Clockwise = true` |
| `RoundRectGenerator.GetBorderLoop`, `RevolveGenerator` caps, `TrivialBox3Generator` normals | Incorrect border loop / normals | Corrected |
| `VoxelSurfaceGenerator` (`MaxMeshElementCount`) | Could exceed limit by up to 17 vertices (off-by-one and fixed reservation) | Enforces hard `<=` bound per voxel (limits `< 24` vertices emit one voxel per mesh) |
| `OBJReader`, `OFFReader` (out-of-range indices) | OBJ threw on bad normal/UV indices; OFF dropped polygons silently or threw on negative indices | OBJ falls back to default normal/UV; OFF fan-triangulates polygons and skips out-of-range polygons with a warning |
| Mesh readers (OBJ, STL, OFF, PLY, 3MF, glTF/GLB, .g3mesh) on NaN/Inf vertices | Framework-dependent parsing or silent acceptance of NaN/Inf | Fails with `FileParsingError` by default across runtimes (checked after 3MF/glTF transforms); opt in with `ReadOptions.AllowNonFiniteCoordinates = true` |

---

## Mesh Repair, Remeshing, Simplification & Cutting

| Class / member | 1.0.324 | Now |
|---|---|---|
| `MeshAutoRepair` tolerances | Absolute `RepairTolerance = 1e-6`, `MinEdgeLengthTol = 1e-4` broke across scales and UTM | Default `ToleranceMode = Relative` (`1e-8` / `1e-6` of diagonal, clamped to median edge / 100 and ≥ 16 ULPs); non-default values stay absolute; inspect `Effective*` properties |
| `MeshRobustRepair` working frame & `PassThroughCleanParts` | Stages ran at input scale (scale-dependent windows); clean isolated parts were altered | Exact `2^k` working frame for scale-invariant results; clean isolated closed parts pass through untouched (`PassThroughCleanParts`, default on); far-apart coarse wraps refused (`WrapTooCoarse`) |
| `MergeCoincidentEdges`, `RemoveDuplicateTriangles`, `RemoveOccludedTriangles` | Absolute `1e-6`/`1e-8` tolerances; threw on tiny cracks; infinite loop past 1024 duplicate candidates; thread-ordered removal | Default `NaN` selects scale-relative tolerances (inspect `Effective*`); handles tiny cracks and >1024 duplicates; deterministic ID-ordered `RemovedT` |
| `MeshRepairOrientation` & `RemoveOccludedTriangles` | Skipped triangles with area `< 1e-6`; thread-ordered ray evidence; threw on infinite vertices | Relative `DegenerateAreaFactor = 1e-10 * diag^2`, deterministic evidence sum, skips non-finite triangles |
| `MinimalHoleFill.Apply()` | Fill edge matching an outer edge was midpoint-split, creating a self-intersection | Flips conflicting fill diagonals when valid without normal inversion |
| `PlanarHoleFiller`, `PlanarSpansFiller` | Unit-box degenerate check scaled inversely with hole size; absolute `1e-6` merge tolerance | Fixed `1e-6` in unit box and relative `1e-6 * maxSide` loop merge |
| `Remesher` flips & non-finite guard | Flipped only for valence; NaN/Inf vertices spread across the mesh or threw | Also flips degenerate-triangle edges when valid; aborts pass on non-finite vertices (`NonFiniteVertices`) |
| `Remesher.BasicRemeshPass` & `RemesherPro` inversion guard | Passes could invert closed components or flatten cones to zero volume | Rolls back inverting/collapsing passes (`RejectInvertingPasses`, default on; `InvertingPassesRejected`; `EdgeLoopRemesher` unchanged) |
| `RemesherPro` queued & projection passes | Thread-dependent edge queue order; absolute `1e-6`/`1e-8` tolerances and `float` rotations failed at UTM | Deterministic sorted prime-step order; relative tolerances and `double` rotations (`2^k` scale- and UTM-invariant) |
| `MeshProjectionTarget.Project` | Threw when no nearest triangle existed (NaN point or target) | Returns input point unchanged with zero normal |
| `Reducer` boundaries, quadrics, bowties & NaN targets | Midpoint boundary collapses; unregularized quadrics in world coordinates; moved bowties; NaN collapsed everything | `PreserveBoundaryShape` (default on) + `QuadricRegularization = 1e-6`; quadrics relative to `quadricOrigin`; preserves bowties; midpoint avoids overflow; NaN accepts no collapses |
| `Reducer.ReduceToMaxQuadricError` | Checked float queue priority without rechecking constrained position | Rechecks final double quadric error against budget across rounded priority bucket; NaN accepts nothing |
| `Reducer.PreventSelfIntersections` | Open AABB pre-filter skipped axis-flat coplanar overlaps and touching boxes | Tests axis-flat and touching pairs exactly via `TriangleTriangleFilter` + `ExactTriangleIntersection` (faster overall, freezes overlapping sheets) |
| `Reducer.PreventFoldOver` (new, default on) | Checked only current adjacent normals (chains could fold surfaces and raise boundary walls) | Guards against absorbed input-normal flips and `BoundaryTriangleMinNormalDot = 0.7`; disable with `PreventFoldOver = false` |
| `QuadricError.OptimalPoint(ref result)` | Absolute determinant threshold `1000 * Epsilon` failed on small/large scales | Scale-relative `det / (trace / 3)^3` check (rejects overflow/underflow and non-finite solutions cleanly) |
| `MeshPlaneCut.Cut` | Absolute `1e-6` collapse tolerance and `MathUtil.Epsilon` plane test; overflowed past `1e154` | Scale-relative `RelativeDegenerateEdgeTol = 1e-6 * diag`, 16-ULP plane tolerance, overflow-safe up to `2^1020` |
| `MeshICP.Solve` | Missing rotation on translation update (diverged at UTM); absolute `1e-5` tolerance; NaN hung SVD | `Translation = RotUpdate * Translation + TransUpdate`; default `RelativeConvergeTolerance = 5e-6 * diag`; skips non-finite source points |
| `LaplacianMeshSmoother` & `SmoothedHoleFill` | Solved in absolute world coordinates (cm-scale errors at UTM) | Solved relative to bounds center (translation-invariant) |

### Notes on Repair & Simplification Changes

- **`Reducer` subclasses:** `vertQuadrics` and quadrics passed to `OnEdgeCollapse` are now centered at `quadricOrigin` (mesh bounds center). Use `plane_quadric` / `evaluate` helpers instead of assuming world-space quadrics.
- **`MeshAutoRepair` explicit defaults:** Passing a value equal to the upstream default (`RepairTolerance = 1e-6` or `MinEdgeLengthTol = 1e-4`) is treated as unset (relative mode). Set `ToleranceMode = Absolute` if exact 1.0.324 absolute values are required.

---

## Contouring, Implicit Fields & Sampling Grids

| Class / member | 1.0.324 | Now |
|---|---|---|
| `MarchingCubes.Generate` (parallel & validation) | Thread-dependent vertex/triangle IDs; invalid `CubeSize`/bounds gave bad dimensions or wrapped errors | Parallel slab batching published in serial `z/y/x` order (bit-identical to serial); validates finite positive `CubeSize` and `int` axis counts (`ArgumentException`), no dense-array product limit |
| `MarchingCubesPro` (`ParallelCompute` & `GenerateContinuation`) | Visited/corner grids omitted upper layer; mixed double/float edge values varied with threads | Covers complete inclusive grid; consistent float values and edge corner order (parallel matches serial) |
| `MarchingCubes` / `MarchingCubesPro` root finding | Absolute `1e-5` iso/edge snaps staircased small objects | Relative `1e-5` of edge field delta; `2^k` scale-invariant |
| `DualContouring.Generate` (without `HermiteF`) | Linear edge interpolation and `QEFEigenTolerance = 0.1` | 60-step bisection on `Implicit` (`ExactHermite = true`) and `QEFEigenTolerance = 0.02` for sharper creases; set `ExactHermite = false` + `0.1` for prior behavior |
| `WindingContouring` refinement margins | Cell offsets/counters could wrap near `int` limits | Intersects ranges with `int` domain using `long` counters |
| `MeshSignedDistanceGrid` (`UseParallel`, no `Spatial`) | Tied closest triangles followed thread order; serial used `/ dx` vs parallel `* (1/dx)` | Deterministic lowest triangle ID and `* (1/dx)` in both (parallel equals serial) |
| Sampling grids (`MeshSignedDistanceGrid`, `CachingMeshSDF`, `MeshWindingNumberGrid`, `MeshScalarSamplingGrid`) | Float grid origin/samples drifted up to `0.25` at UTM; bad cell sizes or `> int.MaxValue` samples threw `OverflowException`/`OOM` | Exact `double` origin (`GridOriginD`) and samples; throws `ArgumentException` on invalid size/bounds/overflow (also `ImplicitFieldSampler3d`) |
| `GridImplicits3d.Bounds()`, `ImplicitIntersection3d.Bounds()`, `Gradient`, `MeshScalarSamplingGrid` | `Bounds()` 1 cell too large / union instead of intersection; `Gradient` per cell; outside narrow band returned `0` | Exact grid extent / box intersection; `Gradient` per unit length (`/ cellSize`); outside narrow band returns last known value |
| `MeshIsoCurves.Compute` & `SplitAtIsoCrossings` | NaN/Inf values emitted NaN vertices or spurious curves; `1e308`/`1e-200` overflowed; vertex crossings created zero-length splits | Skips NaN/opposite-Inf triangles (`Status = NonFiniteInput`); rescales overflowing brackets; skips vertex crossings |
| `ScaleGridIndexer2/3`, `ShiftGridIndexer2/3`, `FrameGridIndexer3`, `GridIndexing` | Truncated negative coordinates; runtime-dependent overflow casts; float UTM error in `FrameGridIndexer3` | Floor semantics, saturating `int` casts (NaN → `0`), double-precision origin subtraction in `FrameGridIndexer3` |

---

## 2D Geometry, Curves, Solvers & Core Math

| Class / member | 1.0.324 | Now |
|---|---|---|
| `Polygon2d.SignedArea` (and `Area`, `IsClockwise`, `PlanarComplex`) | Absolute shoelace terms cancelled far from origin | Summed relative to first vertex (translation-invariant; minor last-bit differences on ordinary inputs) |
| `GeneralPolygon2d.Contains(Polygon2d)` | Accepted polygons overlapping a hole | Rejects polygons overlapping holes |
| `PlanarComplex.Add(polygon)` | Stored direct reference to caller's polygon | Stores a copy |
| `PolySimplification2` | Compared linear `PreserveStraightSegLen` against squared lengths | Squares threshold before comparison; removes duplicate vertices |
| `CurveSampler2`, `PolyLine2DCurve`, `Polygon2DCurve`, `Arc2d`, `Circle2d` | Closed sampler repeated first point; `ParamLength = VertexCount`; mirror transforms kept arc orientation | No duplicate closing point; `ParamLength = VertexCount - 1`; `Polygon2DCurve.SampleT` wraps; mirroring flips `IsReversed` |
| `MeshInsertPolygon` / `MeshInsertUVPolyCurve` | Absolute UV tolerances merged distinct vertices or threw at small scale | Scale-relative default tolerances and bin bounds over referenced vertices |
| `ContMinCircle2` | Failed at `1e-3` scale and missed points at unit scale; random permutation varied last bits | Deterministic Welzl algorithm in translated `2^k`-scaled frame; dimensionless `epsilon` (non-finite throws `ArgumentException`) |
| `ConvexHull2` | Wrong `Dimension`/ring below `~2^-52`; unimplemented modes had vague message | Power-of-two internal scaling (`2^k` scale-invariant); clearer `NotImplementedException` naming `QT_DOUBLE`/`QT_INT64` |
| `AxisAlignedBox3d` constructors, `Contain`, `Intersect` | Signed-zero handling differed between .NET Framework and modern .NET | Deterministic `MinExact`/`MaxExact` signed zeros; propagates NaN |
| `AxisAlignedBox2i/3i.IndicesInclusive` | Wrapped forever when `Max` was `int.MaxValue` | Terminates cleanly after the last cell |
| `Vector2/3/4d/f.IsFinite` | Summed components, falsely flagging large finite vectors (e.g., `(1e308, 1e308)`) | Tests each component individually |
| `Snapping.SnapToIncrementLow/High`, `Units`, `Util.IsBinaryStream` | Exact multiples shifted; miles = `1609.34 m`; streams `< 500 B` treated as binary | Keeps exact multiples; miles = `1609.344 m`; short streams not automatically binary |
| `LaplacianCurveDeformer`, `SparseSymmetricCG`, `SparseSymmetricCGMultipleRHS` | Returned `true` on failed/unconverged/overflowed solves; NaN ran all iterations | Returns `false` immediately on NaN/Inf/overflow without writing unconverged curve results |
| `gParallel.ForRange` worker exceptions (parallel MC, `DSubmesh3Set`, `MeshSpatialSort`) | Single worker exception wrapped in `AggregateException` | Rethrows single inner exception directly; multiple worker exceptions remain `AggregateException` |

---

## Geodesics, Hausdorff & Terrain (Fork Behavior Updates)

| Class / member | Earlier behavior | Now |
|---|---|---|
| `IntrinsicTriangulation.DelaunayRefine` & `CoreMath` across geodesic/intrinsic classes | Concentric corner shells used absolute powers of two; platform `Math.*` differed in last bit across OS/CPU | Shells scaled by mean input edge length; correctly rounded `CoreMath` gives bit-identical cross-platform results (~`1e-16` shift vs. platform `Math`) |
| `MeshHausdorffDistance.Compute` | Overflowed/underflowed outside `[2^-32, 2^65)`; coordinates far from extent stalled refinement; `Symmetric` could starve second direction | Exact `2^k` scaling and shift normalization; certified `LowerBound`; ignores isolated vertices; splits `MaxSubdivisions` fairly across both directions |
| `TerrainGreedyTIN.Compute` | Vertex rounding looped infinitely at `MaxError = 0`; `RemoveVertices` used absolute `1e-12` tie tolerance | Zero error at triangle vertices; scale-free `(maxError, sqError)` ordering |
| `TerrainSafetyLines.Compute` | Tiny tolerances or non-finite vertices could overflow/OOM | Returns `false` cleanly on non-finite vertices or past `MaxCertifySteps` / `MaxSourcePoints` / `MaxRefinedTriangles` |
| `TerrainVolumeDifference.XYOverlapPairs`, `TerrainDualContouring.RemoveOverlaps` | Pairs and leaf splits followed thread order | Sorted `(a, b)` pairs and index-ordered splits (deterministic) |

---

## Signatures & Deprecations (Recompile / Warnings)

| Member | 1.0.324 | Now |
|---|---|---|
| `Util.ParseInt` | Returned `float` | Returns `int` |
| `DSubmesh3.WantComponents`, `ComputeTriMaps`, `OverrideGroupID` | Fields | Properties; mutating after construction recomputes `SubMesh` |
| `Remesher.vModifiedV` (`protected`) | `BitArray` | `bool[]` |
| `DMeshAABBTree3.box_centers`, `box_extents` (`protected`) | `DVector<Vector3f>` | `DVector<Vector3d>` (accurate far from origin / UTM) |
| `DVector.Clear()` | Undeprecated | `[Obsolete]` (warning `CS0618`) |
| `MeshMeshCut`, `MeshBoolean` (float) | Undeprecated | `[Obsolete]` (warning `CS0618`, behavior unchanged); points to `ExactMeshImprint` and `ExactMeshBoolean` |

---

## Performance Notes

- `DMeshAABBTree3` stores node boxes in `double` precision (accurate at UTM coordinates, ~5–15% slower nearest-triangle queries, 2× box memory).
- `Reducer` is ~5–10% slower due to boundary quadrics and regularized optimal-point solves.
- `FastWindingNumber` is slightly faster while remaining scale-free.
