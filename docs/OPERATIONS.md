# Operations

A map of the main mesh operations of geometry3Sharp: which class to use, how to call it, and a small
example for each. The examples in [examples/](examples/) are compiled into the test project and run by
`tests/geometry3Sharp.Tests/DocExamplesTests.cs`, which also checks the behaviour described here (volumes,
closedness, edge lengths, error bounds), so the examples cannot silently go stale. The API reference is
the XML documentation of the classes (shipped in the NuGet package, so IDEs and coding agents see it).

Without writing code: [tools/g3cli](../tools/g3cli/README.md) runs the main operations (measure, repair,
boolean, resolve, remesh, simplify, uv, convert, the 2D mesher `mesh2d` and the quadtree queries `query2d`) on mesh
files, with JSON results, and as an MCP server; worked examples in [Command line and MCP](#command-line-and-mcp-g3cli).

Namespaces: most classes are in `g3`; many mesh operations are in `gs` (repair, alpha wrap, solidify, hole
filling, wall thickness, deviation, Hausdorff and geodesic distances, intrinsic triangulations, terrain).
Add both: `using g3; using gs;`.

## Common patterns

- **The mesh type** is `DMesh3`: an editable triangle mesh whose edges are always manifold (at most two
  triangles per edge; `AppendTriangle` returns a negative id such as `DMesh3.NonManifoldID` instead).
  `new DMesh3(other)` makes a copy, `new DMesh3(other, bCompact: true)` also removes gaps in the ids.
- **Operation objects**: create the operation with its input(s), set public option fields, call
  `Compute()` / `Apply()` / `Cut()`, then read the output fields. A `bool` return value says whether it
  worked; operations with an `Error` or `FallbackReason` field say why.
- **In place or new mesh**: each class comment says which. In place: `MeshAutoRepair`, `Remesher`,
  `Reducer`, smoothers, hole fillers, `MeshPlaneCut`. New mesh in `Result`: `ExactMeshBoolean`,
  `MeshWindingNumberSolidify`. Pass `new DMesh3(mesh)` to keep the input of an in-place operation.
- **Files**: `StandardMeshReader` / `StandardMeshWriter` pick the format from the extension and return an
  `IOCode` with a message instead of throwing (`StandardMeshReader.ReadMesh` returns null on failure, and
  throws if the file was read but contains no mesh).

## Operations

| Task | Classes | Example |
|---|---|---|
| Build meshes, generators | `DMesh3`, `TrivialBox3Generator`, `GridBox3Generator`, `Sphere3Generator_NormalizedCube`, `CappedCylinderGenerator` | [MeshConstructionExample](examples/MeshConstructionExample.cs) |
| Read / write OBJ, STL, OFF, PLY, 3MF, glTF/GLB | `StandardMeshReader`, `StandardMeshWriter`, `ReadOptions`, `WriteOptions`, `PLYReader`/`PLYWriter`, `ThreeMFReader`/`ThreeMFWriter`, `GLTFReader`/`GLTFWriter` | [MeshIOExample](examples/MeshIOExample.cs) |
| Repair (keep the triangles) | `gs.MeshAutoRepair`, `gs.MeshPostRepairCleanup` | [MeshRepairExample](examples/MeshRepairExample.cs) |
| Repair with a clean result guaranteed | `gs.MeshRobustRepair` | [MeshRepairExample](examples/MeshRepairExample.cs) |
| Fill holes | `gs.MinimalHoleFill`, `gs.SmoothedHoleFill`, `gs.AutoHoleFill`, `SimpleHoleFiller`, `MeshBoundaryLoops` | [HoleFillExample](examples/HoleFillExample.cs) |
| Check a mesh (self-intersections, folds, volume IoU) | `gs.MeshRepairMetrics`, `MeshRobustRepair.NotCleanReason` (first failed check; `NotCleanReasons` / `FindDefects`: all of them), `DMesh3.CheckValidity`, `MeshValidation` | [RepairMetricsExample](examples/RepairMetricsExample.cs) |
| Compare a result with its input: F-score, normal consistency and reversed normals, sharp edges kept (Edge Chamfer), volume IoU against the solid of the input's outer faces (interior faces, nested shells and double-sided sheets left out), curvature fidelity (mean / Gaussian curvature at corresponding points and the distance of their distributions), triangle quality (Verdict), topology changes | `gs.MeshFidelityMetrics`, `gs.MeshCurvature`, `gs.TriangleQualityStatistics`, `gs.MeshTopologySummary` | [FidelityMetricsExample](examples/FidelityMetricsExample.cs) |
| Discrete mean and Gaussian curvature per vertex (Meyer et al. 2003: mixed Voronoi areas, cotan mean curvature normal, angle deficit). Degenerate and non-finite triangles, boundaries and bowties are handled (their vertices not `Valid`); no NaN or infinite output | `gs.MeshCurvature` | `MeshCurvature.Compute(mesh).Mean[vid]` (tests: `MeshCurvatureTests`) |
| Rebuild any input as a closed solid | `gs.MeshWindingNumberSolidify` | [WindingNumberSolidifyExample](examples/WindingNumberSolidifyExample.cs) |
| Booleans (union, intersection, difference) of two or more meshes, winding number predicates (eg inside at least k of n), resolve self-intersections | `ExactMeshBoolean`, `ExactMeshBoolean.AtLeast`, `ExactMeshBoolean.Resolve` | [ExactMeshBooleanExample](examples/ExactMeshBooleanExample.cs) |
| Isotropic remeshing | `Remesher`, `gs.RemesherPro`, `MeshConstraintUtil`, `MeshProjectionTarget` | [RemeshExample](examples/RemeshExample.cs) |
| Simplification, with a guaranteed maximum deviation; exact envelope containment | `Reducer`, `gs.MeshEnvelope` | [SimplificationExample](examples/SimplificationExample.cs) |
| Smoothing | `MeshIterativeSmooth`, `LaplacianMeshSmoother` | [SmoothingExample](examples/SmoothingExample.cs) |
| Plane cut and cap | `MeshPlaneCut` | [PlaneCutExample](examples/PlaneCutExample.cs) |
| Measurements (volume, area, bounds, edge lengths, genus) | `MeshMeasurements`, `MeshQueries` | [MeasurementsExample](examples/MeasurementsExample.cs) |
| Nearest point, ray hit, inside test, winding number | `DMeshAABBTree3`, `MeshQueries` | [SpatialQueriesExample](examples/SpatialQueriesExample.cs) |
| Nearest neighbours among points | `PointHashGrid3d<T>`, `PointAABBTree3` | [PointQueriesExample](examples/PointQueriesExample.cs) |
| Signed distance field, offset surfaces | `MeshSignedDistanceGrid`, `DenseGridTrilinearImplicit`, `MarchingCubes` | [OffsetSurfaceExample](examples/OffsetSurfaceExample.cs) |
| Iso-surface of an implicit field. Which one: `MarchingCubes` for vertices on the iso-surface (on the grid edges), a closed result and output that is deterministic also in parallel; `DualContouring` for sharp edges and corners (QEF vertices), far fewer triangles, sparse mode and local refinement, but ambiguous non-manifold quads are left out (`NonManifoldTriangles`, small holes); `gs.MarchingCubesPro` only for `GenerateContinuation` (surface-following from seed points, cost proportional to the surface, not the volume). `MarchingCubesPro` is maintained, not developed further: bug fixes only, and its parallel vertex ids still follow the thread schedule (geometry unaffected) | `MarchingCubes`, `DualContouring`, `gs.MarchingCubesPro` | [OffsetSurfaceExample](examples/OffsetSurfaceExample.cs) |
| Hausdorff distance, geodesic distance | `gs.MeshHausdorffDistance`, `gs.MeshGeodesicDistance` | [DistancesExample](examples/DistancesExample.cs) |
| Intrinsic Delaunay triangulation and refinement: cotan Laplacian without negative weights, geodesics and vector heat on poor meshes, overlay with the input; geodesic paths and loops (FlipOut) | `gs.IntrinsicTriangulation`, `gs.MeshGeodesicDistance.IntrinsicRefine`, `gs.VectorHeatMethod.IntrinsicRefine`, `gs.IntrinsicCommonSubdivision`, `gs.MeshGeodesicPath` | [IntrinsicTriangulationExample](examples/IntrinsicTriangulationExample.cs) |
| Slope design from a toe line to a terrain (open-pit benches, dumps, embankments): cut, fill or mixed cut and fill (`Directions.Auto`, the clamp max(S_fill, min(T, S_cut))), never steeper than the given slope (exact check of every output triangle); catch lines with their accuracy, the composite terrain (or only the replacement of the changed terrain triangles), cut and fill volumes and areas, level lines at target heights (`TargetHeights`, the hook for berms). The result does not depend on the order of the toe pieces. Statuses with a kind (`StatusKind`): invalid input, unsupported by the method, geometrically infeasible (eg `ToeUndercut`, `ToeOvertopped`), not certified. The smallest feature it can guarantee (`UsedMinFeatureSize`) follows from the coordinates' magnitude, about 1.5 cm at UTM with the default `SlopeMargin`; `LocalFrame` computes on the inputs translated to a local origin and returns the outputs in that frame (XY + `UsedOrigin` is the input's; translating them back rounds by up to half an ulp and is not covered by the exact checks). With `LocalFrame` the caller's `Terrain` is translated in place while `Compute` runs and restored bit for bit when it returns (also on an exception; its timestamps do not change, so a spatial index on it stays valid): do not share that terrain with work running concurrently. A copy instead cost 0.2–0.6 s, 167 MB of allocations and 300 MB of peak memory on a 2-million-triangle terrain | `gs.TerrainSlopeDesign`, `SlopeToe`, `SlopeSpec`, `SlopePiece`, `CatchLine`, `TargetHeightLine` | tests: `TerrainSlopeDesignTests`, `TerrainSlopeCatchLineTests`, `TerrainSlopeMixedTests`, `TerrainSlopeTargetHeightTests`, `TerrainSlopeLocalFrameTests`; design: [TERRAIN.md](design/TERRAIN.md); example: [TerrainSlopeExample](examples/TerrainSlopeExample.cs); CLI: `g3cli slope` |
| 2D Delaunay of points; constrained Delaunay of points, segments, rings, holes and regions with crossings split (noding). Exact predicates, output in doubles valid on those doubles. Limits: coordinates up to 1e70; a crossing is rounded to nearest, so a piece may lie a few ulps off its segment (in the subnormal range, farther); vertices within 2^-41 of max(coordinate, extent) of a segment become its vertices; exact duplicates only are merged | `DelaunayMesh2`, `ConstrainedDelaunayMesh2`, `TriangulationMesh2`, `ConstrainedMesh2` | tests: `DelaunayMesh2Tests`, `ConstrainedDelaunayMesh2Tests`; design: [MESHING2D_PLAN](design/MESHING2D_PLAN.md); CLI: [`g3cli mesh2d --quality false`](#command-line-and-mcp-g3cli) |
| 2D quality mesh: Delaunay refinement (Ruppert, Shewchuk) of the constrained triangulation to a minimum angle, a maximum area (also per region) and a size function, with a Steiner point limit; locked segments. Two modes: constrained Delaunay (the default, `Conforming = false`; up to a minimum angle of 25.65° a segment is encroached only by a vertex in its diametral lens, Shewchuk 2002 §6.4, above by its diametral circle) and conforming Delaunay (`Conforming = true`, every triangle Delaunay also across segments, O2). Off-centres (Üngör 2004) for triangles bad by their angle. A small angle forced by the input stays and is reported (`BadTriangles`); at the Steiner limit the mesh is valid but the bounds are not guaranteed (`SteinerLimitReached`). With a snap tolerance every vertex, also those refinement adds, lies on the grid | `QualityMesh2`, `ConstrainedMesh2Input`, `Mesh2Region` | tests: `QualityMesh2Tests`, `QualityMesh2LensTests`, `Meshing2TrackerCasesTests`; design: [MESHING2D_PLAN](design/MESHING2D_PLAN.md); CLI: [`g3cli mesh2d`](#command-line-and-mcp-g3cli) |
| UV atlas of any mesh: seams, charts, BFF with cones, packing, exact overlap test | `UVAtlasGenerator`, `MeshDiscCut`, `MeshChartSegmentation`, `ConeFlattening`, `UVOverlap`, `UVPacker` | [UVAtlasExample](examples/UVAtlasExample.cs) |
| Parameterize a disc | `BoundaryFirstFlattening`, `LSCMParameterization`, `ARAPParameterization`, `SLIMParameterization`, `ParameterizationDistortion` | [UVAtlasExample](examples/UVAtlasExample.cs) |
| 2D polygon booleans, polyline clipping, offsets, Minkowski sums, simplification (optional package `geometry3Sharp.Clipper2`) | `g3.Clipper2.Clipper2Polygons`, `ClipperFrame`, `Clipper2Options`, `Clipper2Result` | [Clipper2Example](examples/Clipper2/Clipper2Example.cs) |

## Which repair or boolean class?

| You have | Use | Result |
|---|---|---|
| A mostly good mesh with small holes, duplicate vertices, degenerate triangles | `MeshAutoRepair` (with `PostRepairCleanup = true`) | the input triangles, welded and closed where possible; not guaranteed closed |
| Any mesh, and you need a closed, manifold, self-intersection-free result | `MeshRobustRepair` | the repaired mesh if it is clean, otherwise its self-intersections resolved exactly (`UsedExactResolve`), otherwise the winding number solid (`UsedSolidify`), otherwise the alpha wrap (`UsedAlphaWrap`; simplified within half its offset, then a quality pass, `AlphaWrapQualityPass`, on by default: fewer slivers and caps, up to 5 % more triangles, certified within the same distance); see `FallbackReason`. If the clean result lost volume (`MinVolumeIoU`) the input itself is resolved or solidified instead. `Apply()` returns false (`Mesh` unchanged) if cancelled or if neither the solidify nor the alpha wrap gave a mesh; a solidified result that is not clean either is kept when the wrap fails or is off (`UsedSolidify` and `SolidifyReason`) |
| A triangle soup, overlapping parts, large holes | `MeshWindingNumberSolidify` | a new, remeshed closed surface; features below the cell size are lost |
| Closed meshes that intersect themselves or each other, where the triangles must be kept exactly | `ExactMeshBoolean`, `ExactMeshBoolean.Resolve(mesh)` | exact arrangement, only the output coordinates are rounded; `SafeRounding` (on by default) repairs the slivers and self-intersections the rounding leaves (`MeshRoundingRepair`, checked exactly, `RoundingClean`; `false` for speed), `ComputeExactVertices = true` gives the exact rational coordinates (`ResultExactVertices`); flipped input triangles are reoriented per component first (the larger part by area keeps its orientation, `ReorientedTriangles`) |
| Booleans of broken meshes (open, soups, overlapping shells) | `gs.MeshWindingNumberBoolean` | a new, remeshed closed surface (winding numbers on an adaptive octree) |

`MeshBoolean` is the old, non-robust union of the original library; prefer `ExactMeshBoolean`. Its snap
tolerance (`VertexSnapTol`, 1e-5) is absolute: with the default it throws at millimetre scale and can hang or give
other results at UTM coordinates.

`ExactMeshBoolean` also takes open meshes (`AllowOpenMeshes`, default true): their solid is where the generalized
winding number is above 1/2 (`ExactWinding[i]` false), and the result is generally open (`IsClosed()` false,
`MeshRobustRepair.NotCleanReason` "not closed"). It is consistently oriented, also where an open boundary of an
input lies on an edge that other pieces meet: there no inside/outside choice fits all the pieces, the pieces that
run the edge the same way are not joined, and the result is open along the edge; `ConflictEdges` counts these edges
(0 for closed inputs).

**By design** (not defects; tested in `ExactBooleanByDesignTests`):
- **Open inputs give open results.** An open mesh has no inside in the exact sense; its solid is where the
  generalized winding number is above 1/2, and that solid is not bounded by a closed surface in general. Where the
  result keeps pieces of the open input, it ends at the input's open boundary (boundary edges,
  `IsWindingClosed(result)` false). Set `AllowOpenMeshes = false` to refuse open inputs (`Status` `OpenInput`), or
  close them first (`MeshRobustRepair`, `MeshAlphaWrap`).
- **Solids that touch along an edge give a result that is non-manifold there**, as in Manifold. The union of two
  boxes that share only an edge is two solids meeting in a line: the four triangles at the edge are split into two
  manifold pairs, with copies of the edge's vertices (`NonManifoldEdges` counts these edges), so the `DMesh3` is
  closed and manifold, but welded by position the edge has four triangles.
  `MeshRobustRepair.SeparateTouchingVertices` moves such contacts apart if a strictly manifold mesh is needed.

## The examples

### Booleans

```csharp
var boolean = new ExactMeshBoolean(cube, bar, ExactMeshBoolean.Operations.Difference);   // A minus B
DMesh3 result = boolean.Compute() ? boolean.Result : null;   // closed if the inputs are closed
if (result == null)
    Console.WriteLine(boolean.Error);
```

The test checks the exact volumes of union, intersection and difference of a cube and a bar with
coplanar faces ([ExactMeshBooleanExample](examples/ExactMeshBooleanExample.cs)).

Nearly coincident inputs give intersection points that nearly coincide: rounded to double, they leave slivers
(degenerate triangles) and sometimes self-intersections. `SafeRounding` (on by default) repairs them after the
rounding; `RoundingClean` tells whether the result is verified free of both:

```csharp
var boolean = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);   // SafeRounding = true is the default
boolean.Compute();
bool clean = boolean.RoundingClean;   // no self-intersections, no triangles below 1e-12 diagonal^2 (exact check)
```

"The diagonal" of the tolerances is the diagonal of the result's largest connected component, not of the bounding box of
all components: a small part far away (eg 1e8 off) does not make the others' triangles degenerate. Only the ulp term
below is per component. A long thin component still sets the diagonal for all (eg a rod of length 1e8 makes a unit part
"tiny", and it is removed), as the bound of `docs/design/SAFE_ROUNDING.md` section 1 decides.

It costs 1.2 to 1.9 times the time of the boolean (the exact check of the rounded result for self-intersections
is most of it); `SafeRounding = false` gives the plainly rounded result, for speed or with `ComputeExactVertices`
when the exact coordinates are used anyway. Not every case is repaired: `RoundingClean` false with `Status`
`Success` means that defects remain (`Rounding.RemainingIntersections`, `RemainingDegenerate`).

What it guarantees: triangles the repair does not touch are the exact result, rounded. Degenerate triangles are
contracted (also exact ones, eg a well-shaped triangle of size 2^-20 where an exact cut leaves it: such tiny
triangles trouble slicers, FEM and remeshers), and the edits also change well-shaped neighbours (flips, contractions,
moves), all within a bound: the surface moves by at most `Rounding.MaxDrift` from the rounded result, at most
`MaxDisplacement` = 1e-5 times the diagonal, in Hausdorff distance (without the components it removes as tiny
or thin, counted in `Rounding.RemovedComponents`; a closed component thinner than the bound that holds real volume for its
area is kept instead, counted in `Rounding.ScaleDisparity`, and the result is not clean; up to an absolute rounding error of a few ulps of the largest
coordinate per edit). Edits beyond the bound are not applied. The volume changes per edit by at most about the edit's
displacement times the area of the triangles it replaces, plus the volume of removed components (class comment of
`MeshRoundingRepair`). `MeshRobustRepair` can run a second repair after its separation step, with a budget of its
own: there the total is at most twice the bound.

More than two meshes go into one arrangement, with an operation or a predicate on the winding numbers of all
inputs (Zhou et al. 2016). Status is the result code (`Success`, `InvalidInput`, `OpenInput`):

```csharp
var all = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union);   // also Intersection; Difference = first minus all others
var two = new ExactMeshBoolean(meshes, ExactMeshBoolean.AtLeast(2));          // inside at least two of the meshes
var odd = new ExactMeshBoolean(meshes, w => w.Count(x => x > 0) % 2 == 1);   // any predicate on the winding numbers w[i]
if (two.Compute() == false)
    Console.WriteLine(two.Status + ": " + two.Error);
```

One operation over all inputs is faster than a chain of pairwise booleans and rounds the new vertices only
once: the 10×10×10 cube grid with a void in each cube (2000 meshes, 1999 pairwise booleans) takes 27 s in one
pass ([BooleanComparison](../benchmarks/BooleanComparison/README.md#many-meshes-in-one-operation)).

### Repair

```csharp
var repair = new MeshAutoRepair(mesh) { PostRepairCleanup = true };
repair.Apply();
DMesh3 repaired = repair.Mesh;           // a compacted copy: always read repair.Mesh

var robust = new MeshRobustRepair(mesh);
if (robust.Apply())                      // robust.Mesh: closed, manifold, no self-intersections, except
    mesh = robust.Mesh;                  // in the rare case UsedSolidify && SolidifyReason != ""
```

See [MeshRepairExample](examples/MeshRepairExample.cs), [HoleFillExample](examples/HoleFillExample.cs),
[RepairMetricsExample](examples/RepairMetricsExample.cs) and
[WindingNumberSolidifyExample](examples/WindingNumberSolidifyExample.cs).

### Remeshing, simplification, smoothing

```csharp
var remesher = new Remesher(mesh);
remesher.SetTargetEdgeLength(0.5);
remesher.SetProjectionTarget(MeshProjectionTarget.Auto(mesh));
MeshConstraintUtil.FixAllBoundaryEdges(remesher);
for (int i = 0; i < 20; ++i)
    remesher.BasicRemeshPass();

new Reducer(mesh) { MaxDeviation = 0.01 }.ReduceToTriangleCount(1000);

var envelope = new MeshEnvelope(reference, 0.01);         // Status: Ready, InvalidEpsilon, TooThin, Empty, TooLarge, OutOfRange
bool inside = envelope.ContainsTriangle(a, b, c);         // exact; also Contains(p), ContainsSegment(a, b)
```

With the defaults (as upstream) the remeshers check no move for flipped triangles and flip and collapse edges across
creases. For flat, coarse or creased inputs constrain the features with `MeshConstraintUtil.PreserveBoundaryAndFeatures`
and set `PreventNormalFlips` (edge flips and collapses) and `MinTriangleQuality` (also the smoothing and projection
moves); without them a coarse L-shaped part loses a leg and a flat fan folds (docs/ISSUE_CASE_FINDINGS.md, S2 and S3).
`Remesher.MinTriangleQuality` (opt-in, `g3cli remesh` uses 0.2) guards against poor and flat triangles;
`Remesher.PreventSelfIntersections` (opt-in, about 17x the time) rejects every operation and move that would make
triangles intersect, so an input without self-intersections stays without them (benchmarks/RemeshQualityBenchmark).
`MeshConstraintUtil.PinSharpVertices` (after `PreserveBoundaryAndFeatures`) pins points that are no junction of
creases, eg the apex of a cone with many slices, by their angle deficit.
`PreserveBoundaryAndFeatures` also pins bowtie vertices (fans of triangles meeting only at a vertex, eg two cones tip
to tip), so the remeshers neither move them nor join the fans; the `Reducer` keeps bowtie vertices in place by itself.
`Remesher.TargetEdgeLengthF` (opt-in) sets the target edge length per point; `RemeshSizing.FromWallThickness` sets it
to (a fraction of) the local wall thickness, so thin walls are refined instead of folded through each other.

`Reducer.MaxDeviation` guarantees that every point of the result lies within the tolerance of the input,
checked with certified distance bounds (`TriangleDeviationBounds`).

`Reducer.ReduceToMaxQuadricError(maxError)` keeps float queue ordering, but checks the actual collapse position's
quadric error in double precision after constraint/inline projection adjustments. Errors equal to the budget
are allowed even if their queue priority rounds up. A NaN budget accepts no collapses. This budget is not a
Hausdorff bound and does not cover the separate `AfterRefinement` projection pass.

`MeshEnvelope` (after Wang et al. 2020) decides exactly whether a point, segment or triangle lies in the union
of convex prisms around the mesh triangles. The prisms lie inside the epsilon envelope: 0.8 epsilon along the
normal, 0.55 epsilon sideways. A result of true guarantees the distance bound whatever the rounding. A result
of false means some point lies outside all prisms.

Queries whose triangle lies in one prism, or whose corner is outside all prisms, are decided in floating point.
Otherwise the query is clipped exactly, which is slow for triangles that span many prisms. It is therefore not
used for the `Reducer`: see [ReducerBenchmark](../benchmarks/ReducerBenchmark/README.md).

See [RemeshExample](examples/RemeshExample.cs), [SimplificationExample](examples/SimplificationExample.cs),
[SmoothingExample](examples/SmoothingExample.cs) and [PlaneCutExample](examples/PlaneCutExample.cs).

### Measurements and queries

```csharp
Vector2d volumeArea = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex);
var tree = new DMeshAABBTree3(mesh, autoBuild: true);
int tid = tree.FindNearestTriangle(point);
bool inside = tree.IsInside(point);             // closed meshes (exact parity, also through edges and vertices)
double winding = tree.WindingNumber(point);     // ~1 inside, ~0 outside, also for open meshes
```

See [MeasurementsExample](examples/MeasurementsExample.cs), [SpatialQueriesExample](examples/SpatialQueriesExample.cs),
[PointQueriesExample](examples/PointQueriesExample.cs), [OffsetSurfaceExample](examples/OffsetSurfaceExample.cs)
and [DistancesExample](examples/DistancesExample.cs).

### 2D loose quadtree

`LooseQuadtree2d` stores AABB snapshots with stable integer IDs. `PointQuadtree2d`,
`SegmentQuadtree2d` and `TriangleQuadtree2d` store points, segment endpoints and triangles
as snapshots and query their XY geometry; z is used only for triangle-height interpolation.
They complement the existing grids and do not follow changes to source meshes or lists.
`TriangleQuadtree2d.Over(array)` skips the copy of the triangles: it reads the caller's array, which then must not change.

```csharp
var tree = new TriangleQuadtree2d(triangles);
var ids = new List<int>(128);
tree.QueryRectangle(new AxisAlignedBox2d(0, 0, 20, 20), ids);
tree.FindNearestK(new Vector2d(10, 10), 4, ids);
bool found = tree.TryGetHeight(new Vector2d(10, 10), out double height, tolerance: 0.01);
```

* Bulk IDs are input positions; insertion returns a new ID and grows the extent, even for an
  initially empty tree. Removal never renumbers IDs or shrinks the extent. Removing a value removes
  its first live equal snapshot. Missing removals are no-ops. Failed input validation consumes no ID.
* Rectangle queries return exact inclusive AABB intersections. Core segment queries clip against
  AABBs enlarged by 0.0002 coordinate units; they are candidates, not exact geometry intersections.
  `SegmentQuadtree2d.QueryIntersections` tests actual closed segments, including collinear overlap
  and touching. `QueryPoint` supports absolute tolerance and optional endpoint exclusion / early stop.
  Build it from vertices and edges, or `Insert(a, b)`, when queries must hit the endpoints exactly: a `Segment3d`
  rounds them, depending on z too. `GetEndpoints` returns them as stored. `GetItem` rebuilds a `Segment3d`, which
  `Remove` accepts, and throws `ArithmeticException` for endpoints a `Segment3d` cannot hold.
* `PointQuadtree2d.QueryNearby` uses inclusive Euclidean XY distance, not a per-axis tolerance.
* Height hits use exact orientation signs for containment and absolute XY distance for tolerance.
  All exact hits take precedence over tolerance hits, including when stopping after the first result.
  One result from tolerance comes from the nearest triangle, the lowest ID on a tie.
  Zero-area triangles produce no height. A point beside a triangle gets the height at the nearest point of its
  boundary, so it stays in the triangle's z range. Height construction is floating point, not an exact predicate. Robust fallbacks
  for extreme or nearly degenerate inputs, and for a distance within rounding of the tolerance, may allocate; ordinary
  hot queries with reserved buffers do not. An unrepresentable height throws `ArithmeticException`, not a successful infinity result. A
  query exception can leave partial buffer results; the index itself remains unchanged.
* Nearest queries measure distance to AABBs in the core, actual XY geometry in the adapters.
  No hit returns -1; k-nearest results are sorted by distance, ties by insertion ID. An inclusive
  maximum distance may restrict either query. Removed items never participate.
* Every result-buffer query clears its output first. Reserve enough capacity to avoid list growth.
  Concurrent and nested read-only queries use independent caller-owned buffers; inserts/removals
  must not overlap queries. Struct visitors (`IQuadtreeVisitor2d`) allow allocation-free traversal:
  `TestRegion` prunes a region and `Visit` returning true stops at a real caller-defined hit. A region is the bounds of what
  that part of the tree holds, with no slack and possibly of zero area: a test with a margin has to add it. Empty parts are not shown.
  Built-in nearest metrics compare exact squared distances when floating-point distances overflow;
  ID ordering then breaks only actual ties. Custom nearest metrics (`IQuadtreeDistance2d`) must never
  be smaller than point-to-item-AABB distance; their returned double values define ties, including infinity.
* Coordinates must be finite, boxes ordered and tolerances nonnegative and finite. Only a nearest
  maximum distance may be positive infinity. Construction has fixed internal settings; the optional
  `parallelBuild` flag changes execution policy, not query semantics.

Runnable example: [QuadtreeQueriesExample](examples/QuadtreeQueriesExample.cs).
See the [behavioural specification](design/QUADTREE2D_SPEC.md) and the
[procedural benchmark](../benchmarks/QuadtreeBenchmark/README.md).

### Intrinsic triangulations

```csharp
// Delaunay by flips, then refined by vertex insertion: no negative cotan weights, angles >= 25 degrees
IntrinsicRefinementResult result;
var T = IntrinsicTriangulation.BuildDelaunay(mesh, true, out result, 25);
SymmetricSparseMatrix L = T.CotanLaplacian();      // vertex indices; T.VertexID(i) = mesh vid, -1 if inserted
Console.WriteLine(result);                          // Status, inserted vertices, min angle, skipped triangles

var geodesic = new MeshGeodesicDistance(mesh) { IntrinsicRefine = true };   // same for VectorHeatMethod
geodesic.Compute(new[] { source });                 // geodesic.Refinement: the same result record

IntrinsicCommonSubdivision overlay = T.ExtractCommonSubdivision();   // polygons of input x intrinsic triangles
```

An intrinsic triangulation keeps the surface and the vertices of the mesh, but its edges are geodesics
described by their lengths (signposts give their directions on the input). `FlipToDelaunay()` makes every
interior cotan weight non-negative. Boundary edges opposite an obtuse angle cannot be flipped and keep a
negative weight; `DelaunayRefine(minAngle, maxInsertions)` splits them at their midpoints and inserts the
circumcenters of triangles with an angle below `minAngle` (Chew / Ruppert refinement on the intrinsic
triangulation, Sharp, Soliman and Crane 2019). Inserted vertices lie on the input surface
(`VertexPosition`, `GetInputLocation`); results of `MeshGeodesicDistance` and `VectorHeatMethod` are still
reported at the mesh vertices only.

- **Status** (`IntrinsicRefinementResult.Status`): `Success` (Delaunay, all weights non-negative, angle bound
  met), `InsertionLimitReached` (valid and Delaunay, but triangles may remain below the bound; boundary splits
  get twice the limit), `Incomplete` (a circumcenter fell onto an existing vertex), `NonManifoldInput`
  (`MeshGeodesicDistance` with bowtie vertices: not refined, flips only).
- **Small input angles**: triangles whose small angle comes from the input (a vertex with an angle sum below
  60 degrees, eg a jagged boundary) cannot be improved and are counted in `SkippedTriangles`; boundary edges
  there are split at powers of two from the corner (concentric shells), so the refinement terminates.
- `minAngle` is clamped to [0, 30] degrees; 0 only splits the boundary edges opposite obtuse angles (few
  insertions). The default insertion limit is 10 times the vertex count plus 1000.
- On a plane of 169-degree triangles (8200 vertices) refinement inserts 10250 vertices in about 1.5 s and lowers
  the mean heat method distance error from 0.06 to 0.02 edge lengths, the maximum from 0.8 to 0.1; splitting
  only the boundary edges (0 degrees, 40 vertices) already lowers the maximum to 0.23
  ([IntrinsicBenchmark](../benchmarks/IntrinsicBenchmark/README.md), also the ring sphere and the log map).
- **Common subdivision**: every intrinsic edge is traced over the input and each input triangle is cut along
  the traced segments. The result is checked (faces tile every input triangle and every intrinsic triangle,
  areas to 1e-6); if traces are inconsistent (crossings in the wrong order) `Status` is `Inconsistent`
  with an `Error` instead of wrong faces. Integer (normal) coordinates (Gillespie, Sharp and Crane 2021),
  which make this combinatorially exact, are not implemented: with flips, insertions and edge splits the
  traces are exact up to rounding, and the checks catch the cases where rounding would matter.

#### Geodesic paths and loops

```csharp
var path = new MeshGeodesicPath(mesh);
path.Compute(source, target);                       // Status; Length; Path (polyline); PathVertices
path.ComputeThrough(new[] { a, b, c }, closed: true);   // through fixed waypoints
path.Shorten(edgeLoop, closed: true);               // tighten a loop along mesh edges to a closed geodesic
```

`MeshGeodesicPath` starts from a path along mesh edges (Dijkstra between the waypoints, or the given one) and
straightens it by intrinsic edge flips (FlipOut, Sharp and Crane 2020): wherever the path turns by less than pi
at a free vertex, the edges inside that wedge are flipped and the path rerouted along its outer side. The
result is an exact geodesic of the mesh's (intrinsic) geometry, in the homotopy class of the initial path: it
passes through a vertex only where the angle is at least pi on both sides (saddles, reflex boundary corners).
On developable meshes it is the straight line in the unfolding to rounding (the tests check plane, cube surface
and cylinder against closed forms). `Compute(a, b)` from the Dijkstra path usually gives the globally shortest
geodesic, but this is not guaranteed. The mesh is not modified.

- **Status**: `Success` (geodesic), `Collapsed` (a contractible closed loop shrank to a point, to its fixed
  vertex if it has one), `NotConverged` (`MaxIterations`; valid and shorter), `Stuck` (a wedge holds edges of
  the same, self-overlapping path, which are not flipped; valid), `NoPath` (waypoints not connected),
  `InvalidInput` (ids, a non-adjacent edge path, bowtie vertices), `TraceFailed` (`Length` is valid, but an edge
  could not be traced over the mesh triangles, so `Path` may cut across the surface there).
- A loop around a cone vertex with an angle below pi (a sharp tip) can only contract through the tip, which is
  longer for a moment; a loop of one edge whose wedge only goes out to the tip and back is therefore reported
  as `Collapsed` directly.
- Boundaries are allowed (the path stays on the surface); a geodesic loop around a handle or a cylinder is found
  with `Shorten(loop, closed: true)`, also when it winds around more than once.

See [IntrinsicTriangulationExample](examples/IntrinsicTriangulationExample.cs).

### Files

```csharp
DMesh3 mesh = StandardMeshReader.ReadMesh("scan.glb");       // null if reading failed
IOWriteResult result = StandardMeshWriter.WriteMesh("scan.3mf", mesh, WriteOptions.Defaults);
if (result.code != IOCode.Ok)
    Console.WriteLine(result.message);
```

Format details (units, precision, supported features) are in the README table and the reader/writer class
comments. See [MeshIOExample](examples/MeshIOExample.cs).

### UV atlas and seams

```csharp
var atlas = new UVAtlasGenerator(mesh);            // closed or open, any genus, several components
atlas.Compute();                                   // false unless atlas.Status == UVAtlasStatus.Success
DMesh3 textured = atlas.AtlasMesh;                 // the triangles of mesh, split along the seams, vertex UVs in [0,1]^2
int src = atlas.AtlasToInputVertex[vid];           // and AtlasToInputTriangle, TriangleChart

var cut = new MeshDiscCut(mesh);                   // only the seams: one disc per component
if (cut.Compute())
    new BoundaryFirstFlattening(cut.CutMesh).Compute();
```

The disc-based parameterizations need a cut before processing closed surfaces. `MeshDiscCut` cuts any mesh into discs along a cut graph (tree-cotree with
shortest paths: on a closed surface of genus g, 2g short loops; between boundary loops, shortest paths; for a
sphere, a path), and splits the vertices along the seams in a new mesh. `ConeFlattening` is BFF with cone
singularities: cones are placed greedily where the conformal scale factor is largest, the mesh is cut through
them, and the rest becomes flat (a cube: its eight corners, unfolded without distortion). `UVAtlasGenerator`
combines them: per component, cones if the result is overlap-free and within the distortion limits, otherwise
charts grown by normal (`MeshChartSegmentation`) flattened with BFF and split in two while a chart overlaps
itself or exceeds `MaxAreaDistortion` / `MaxConformalDistortion`; then `UVPacker`. The final UVs are checked
with exact predicates (`UVOverlap`) on the double UVs; the float UVs stored in the mesh are tested too, and
defects caused only by rounding (tiny or sliver triangles, eg vertices 1e-7 apart) are reported in
`FloatRoundingDefects`), and `Status` reports `DistortionLimitExceeded` when `MaxCharts` was not
enough (the layout is still overlap-free). From the command line: `g3cli uv mesh.obj atlas.obj`.

### Untangling supplied UVs without moving the boundary

`new FoldoverFreeParameterization(mesh, startUV).Compute()` attempts to remove inverted/degenerate UV
triangles on one connected open manifold mesh, including annuli. It rejects degenerate surface triangles.
UVs are indexed by vertex id; all used vertices need finite entries. Every boundary vertex is fixed bit for
bit. The input mesh and UV list are unchanged and reread on each Compute. No atlas integration is enabled.

Options: `MaxIterations` (200, zero retains the initial map), `InitialEpsilon` (1) and `MinimumEpsilon`
(1e-12, positive and <= InitialEpsilon). The epsilon schedule is equation 6 of Garanzha et al.; no alternate
optimizer or free-boundary mode. `Success` requires finite UVs, zero flipped/degenerate triangles and finite
symmetric Dirichlet distortion; it does **not** mean global injectivity or optimal distortion. Use
`UVOverlap.Compute(mesh, result.UV)` separately. The result is the final iterate when it has no more invalid
triangles than any earlier one (otherwise the earlier iterate with the fewest); `Converged` is false when that is
not the final, converged iterate. `Energy` is the normalized shape/area objective evaluated at InitialEpsilon, not
symmetric Dirichlet; surface area is normalized to one and UVs use a translated power-of-two frame.

Iteration exhaustion or stalling with invalid triangles returns `FlippedTriangles` with the best finite map
(fewest invalid triangles, then lowest common-epsilon energy). Invalid input/constraints and unrepresentable
arithmetic/solver failures have distinct failure statuses and no UVs. The method cannot guarantee a feasible
fixed boundary, a bounded iteration count to success, global overlap removal or SLIM-equivalent distortion.
A collapsed locked edge or invalid fully locked triangle is rejected; near-collapsed boundaries are not
silently declared infeasible. At extreme source/UV scales the existing distortion diagnostics can be
unrepresentable even after internal normalization; this returns `SolverFailed`, not success.
See [measured acceptance](../benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md).

| Method | Charts | Use |
|---|---|---|
| `Auto` (default) | one chart per component where cones work, else several | texture atlases |
| `Cones` | one chart through cones (falls back to charts if it overlaps) | few seams, low area distortion |
| `Charts` | normal-based charts, split until within the limits | robust, more seams |
| `SingleChart` | one chart (cut to a disc), plain BFF | when one chart is required; distorts closed surfaces |

See [UVAtlasExample](examples/UVAtlasExample.cs).

### Terrain slope design

`gs.TerrainSlopeDesign` builds the slope from toe lines to a terrain: open-pit benches and pits (cut), dumps and
embankments (fill), or both (`Directions.Auto`). Design: [design/TERRAIN.md](design/TERRAIN.md).

```csharp
using g3; using gs;

var design = new TerrainSlopeDesign() {
    Toes = { SlopeToe.Constant(toePoints, closed: true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)) },   // 1:1.5
    Terrain = tin,                  // a DMesh3 TIN (a height field under the slope)
    Tolerance = 0.05,               // the professional accuracy in m: required, no library default
    BuildComposite = true,          // the whole changed terrain; CompositeKinds.Replacement: only the replaced part
};
design.Compute();                   // false also for NoDaylight, a partial result: check Status
if (design.Status != TerrainSlopeDesign.Statuses.Success && design.Status != TerrainSlopeDesign.Statuses.NoDaylight)
    Console.WriteLine(design.Status + " (" + design.StatusKind + "): " + design.Message);
// design.CatchLines, design.Composite, design.SlopeRegion, design.CutVolume / FillVolume / CutArea / FillArea
```

- **Never steeper.** Every output triangle is checked exactly against its slope and its support (the toe), so a result is
  never steeper than specified: a design that cannot be certified fails (`SlopeViolation`, `StatusKinds.NotCertified`)
  instead of returning a violating surface.
- **Within `Tolerance`.** The composite is within `Tolerance` of the clamp of the terrain and the slope (cut below
  the terrain, fill above it), except near the steps at the toe, the lateral limits and the cap, where the move of the
  catch points shifts the step: within `UsedMinFeatureSize` in a single run, up to 1e-4 × the toes' size more at a
  toe crossing with `Direction` Auto. That is certified per composite: a
  composite whose catch points moved it too far fails with `SlopeViolation` (the message names the catch point).
- **Slopes** per toe segment (start and end, cut and fill): `SlopeSpec.Ratio(n)` (1:n), `Percent`, `Degrees`, `Rise`.
  Open toes (`Closed` false) have lateral limits; closed ones a side `Inside` or `Outside`.
- **Statuses** and what to do (`StatusKind` says which kind a status is):

  | Status | Kind | Meaning, what to do |
  |---|---|---|
  | `Success` | None | complete result |
  | `NoDaylight` | None | the slope does not reach the terrain everywhere within its footprint or below `HeightLimit`: the result is kept but partial (`IsComplete` false); the catch lines that end there say so (`EndReasons.FootprintBoundary`, `HeightLimit`). Extend the terrain or set `HeightLimit` |
  | `InvalidInput`, `NotHeightField` | InvalidInput | a toe, slope or parameter is wrong (`ToeProblems`), or the terrain under the slope is no height field: fix the input |
  | `ToeSteeperThanSlope`, `ToeUndercut`, `ToeOvertopped` | GeometricallyInfeasible or UnsupportedByMethod | no surface meets the specification (or the phase 1 method cannot build it): `ToeProblems` names the segments |
  | `NotImplemented` | UnsupportedByMethod | a slope that changes continuously along a segment (start and end differ): not in phase 1 |
  | `SlopeViolation`, `CatchLineAccuracyNotReached` | NotCertified | the result could not be certified within `Tolerance` (or `RequiredCatchLineAccuracy`): a larger `Tolerance`, or `LocalFrame` at UTM coordinates for small features |
  | `Cancelled` | Cancelled | `CancelF` returned true: no result |
  | `CompositeFailed`, `Failed` | Internal | an internal failure (a bug: please report it with the input); `CompositeFailed` keeps `SlopeSurface` and `CatchLines` |

- **Coordinates and the smallest feature.** The stored points are rounded to the input's coordinates, so the smallest
  feature the result can guarantee (`UsedMinFeatureSize`) follows from their magnitude: about 1.5 cm at UTM with the default
  `SlopeMargin`, micrometres near the origin. It scales with the input. For smaller features at UTM set `LocalFrame`: the
  design is computed on the inputs translated to a local origin near the toes, and **every output is then in that local
  frame** (add `UsedOrigin` to x and y for the input's frame; that rounds by up to half an ulp and is not covered by the
  exact checks). With `LocalFrame` the caller's `Terrain` is translated in place while `Compute` runs and restored bit for
  bit when it returns (also on an exception; its timestamps do not change, so a spatial index on it stays valid): do not
  share that terrain with work running concurrently. `LocalFrameFallback`: an input coordinate could not be translated
  exactly, and the design ran in the input's frame.
- **Composite.** `CompositeKinds.Full` (default) is the whole terrain with the slope region; `Replacement` only the part that
  replaces terrain triangles (those the region cuts, and their neighbours split where a cut one has a point inside
  their shared edge), with `ReplacedTerrainTriangles` and `CompositeTerrainVertices` to join it, far less output on
  a large terrain.
- **Mixed cut and fill** (`Directions.Auto`): a cut run and a fill run on the terrain, the clamp max(S_fill, min(T, S_cut)),
  one composite. **Target heights** (`TargetHeights`): the level lines of the slope region at those heights
  (`TargetHeightLines`, eg bench levels).
- **Determinism.** The result does not depend on the order of the toe pieces, and scaling the input by a power of two
  scales it bit for bit (tested over `Similarity.All()`). Single-threaded.

Runnable example: [TerrainSlopeExample](examples/TerrainSlopeExample.cs) (a pit, then the same at UTM in a local frame).
The command line: `g3cli slope` (below).

### 2D polygon booleans and offsets (optional package)

The package `geometry3Sharp.Clipper2` (project `addons/geometry3Sharp.Clipper2`) wraps
[Clipper2](https://github.com/AngusJohnson/Clipper2) (Angus Johnson, Boost-1.0) for the polygon types of the
library. The core package does not depend on it.

```csharp
using g3; using g3.Clipper2; using Clipper2Lib;

Clipper2Result merged = Clipper2Polygons.Union(footprints);                  // also Intersection, Difference, Xor
Clipper2Result zone = Clipper2Polygons.Offset(merged.Polygons, 3.0,
    new Clipper2Options { JoinType = JoinType.Miter, MiterLimit = 2 });     // > 0 inflates, < 0 deflates
Clipper2Result free = Clipper2Polygons.Difference(new[] { parcel }, zone.Polygons);
if (!free.Ok)
    Console.WriteLine(free.Status + ": " + free.Error);
List<GeneralPolygon2d> result = free.Polygons;                               // outer CCW, holes CW, islands separate
```

| Method | Input | Result |
|---|---|---|
| `Union`, `Intersection`, `Difference`, `Xor`, `Boolean(op, ...)` | sets of `GeneralPolygon2d`; each one is its region first (inside the outer ring, nonzero, minus its holes, also where holes overlap or reach outside), then the regions of one operand combine by `FillRule` (NonZero default: union; EvenOdd: overlaps cancel) | `Polygons` |
| `BooleanPaths(op, ...)` | raw `Polygon2d` rings, orientation as given (self-intersecting rings, fill rules) | `Polygons` |
| `ClipPolyLines(op, lines, clip)` | open `PolyLine2d`s and polygons; Intersection keeps the inside pieces, Difference the outside | `PolyLines` |
| `Offset(polygons, delta)` | `JoinType` (Round default, Miter with `MiterLimit`, Square, Bevel), `ArcTolerance` (default \|delta\| / 1000, at most \|delta\| / 4); `MiterLimit` up to 1000 | `Polygons` |
| `OffsetPolyLines(lines, delta)` | delta &gt;= 0; `EndType` Round (default), Butt, Square, or Joined (closed polyline); a single point gives a disc (Round), a square (Square) or nothing | `Polygons` |
| `MinkowskiSum`, `MinkowskiDifference` | a pattern around (0,0) and a closed (interior included) or open path | `Polygons` |
| `Simplify`, `SimplifyPolyLines` | vertices within epsilon of the line through their neighbours removed; polygons are re-unioned | `Polygons` / `PolyLines` |

Status is `Success` (also for an empty result), `InvalidInput` (NaN or infinite coordinates, a coordinate plus the
extent and offset beyond the double range, or extents below about 1e-290), `InvalidParameter` (see `Error`) or `Failed` (Clipper2 reported an
error); nothing throws on bad input. Clipper2 computes on integers: each call maps its inputs to a grid
(`ClipperFrame`: origin at the bounds center, a power-of-two scale with the half extent plus the offset margin at
2^`PrecisionBits`, default 40). Input vertices are snapped to that grid and new vertices rounded to it, so result
vertices are off by about a grid spacing from their exact position: about 1e-12 times the input extent, under 10 nm
for a 10 km footprint at UTM coordinates. Scaling the input by a
power of two gives the same result, scaled, bit for bit; other similarities give it up to the grid spacing (tested
over `Similarity.All()`). Features below the grid spacing vanish. `Clipper2Options.Frame` sets a fixed grid instead
(eg Scale = 1000 for millimetres on metre coordinates), and `ClipperFrame` converts between the polygon types and
Clipper2's `Path64` / `PolyTree64` for direct use of Clipper2. Single-threaded and deterministic.

The example is in [examples/Clipper2/](examples/Clipper2/Clipper2Example.cs) and runs in
`tests/geometry3Sharp.Clipper2.Tests` (`Clipper2ExampleTests`), not in `DocExamplesTests`, because it needs the package.

## Command line and MCP (g3cli)

The same operations as [tools/g3cli](../tools/g3cli/README.md) commands, one JSON result per call; with `g3cli mcp`
they are MCP tools with the same arguments. `g3cli` below stands for
`dotnet tools/g3cli/bin/Release/net10.0/g3cli.dll` after `dotnet build tools/g3cli -c Release`. The tests are in
`tests/g3cli.Tests` (`Mesh2dQueryCliTests`, `SlopeCliTests`, `McpTests`).

**2D quality mesh** (`mesh2d`, `QualityMesh2`): a 100 x 60 field with a building (a hole), a fence across it (a
segment with a marker) and two labelled regions, the right one finer; the heights given per point are interpolated:

```
cat > field.json <<'JSON'
{
  "points": [[0,0], [100,0], [100,60], [0,60], [40,20], [60,20], [60,40], [40,40], [70,0], [70,60]],
  "heights": [10, 20, 26, 16, 14, 16, 18, 16, 17, 23],
  "rings": [[0, 1, 2, 3], {"vertices": [4, 5, 6, 7], "hole": true}],
  "segments": [{"a": 8, "b": 9, "marker": 5}],
  "regions": [{"seed": [10, 10], "label": 1}, {"seed": [90, 10], "label": 2, "max_area": 5}]
}
JSON
g3cli mesh2d field.json field.obj --min-angle 30 --max-area 50
#   -> "triangles": 817, "added_vertices": 442, "smallest_angle": 30.2, "largest_area": 49.6, "area": 5600,
#      "bad_triangles": 0, "steiner_limit_reached": false, "labels": { "1": 234, "2": 583 }
```

The output lies in the XY plane (z = the heights, or 0), counterclockwise, with the region labels as triangle groups
(OBJ `g` lines); only vertices of triangles are written (not duplicate input points, nor points in holes), and
`added_vertices` counts those the mesher made (Steiner points, crossings). Rings are outer boundaries or holes (`"hole": true`); without rings the domain is the convex hull of the
points. `--quality false` gives the constrained Delaunay triangulation only (`ConstrainedDelaunayMesh2`, no Steiner
points); `--steiner-limit` bounds the inserted points (then `steiner_limit_reached` and a warning); crossing constraints
are split unless `--resolve-crossings false`; `--snap-tolerance t` snap-rounds the input to a grid of width t first (m2.snap: every
vertex, also those refinement adds, on the grid; merges and collapsed rings are reported in `warnings`). The result is a constrained
Delaunay triangulation; `--conforming true` makes every triangle Delaunay also across segments (more points, the circumcircles of
the triangles empty against all vertices). An agent passes the input
inline instead of a file:

```
g3cli mesh2d --geometry '{"points": [[0,0],[4,0],[4,3],[0,3]], "segments": [[0,2]]}' --output plain.off --quality false
#   -> "triangles": 2, "added_vertices": 0 (the diagonal 0-2 is kept as an edge)
```

Triangle's `.poly` / `.node` files, the common exchange format of 2D meshers, are read and written too (by extension). A
`.poly` gives the domain Triangle `-p` gives (what the segments enclose; mapping and limits in the [g3cli README](../tools/g3cli/README.md#triangle-files-poly-node-ele)), hole points remove holes, regional
attributes are integer labels with optional maximum areas; `--poly-heights` takes the first vertex attribute as the
height. An output ending in `.node`, `.ele` or `.poly` writes all three files. An L-shape with a square hole:

```
cat > l.poly <<'POLY'
10 2 0 1
1 0 0
2 2 0
3 2 1
4 1 1
5 1 2
6 0 2
7 0.25 0.25
8 0.75 0.25
9 0.75 0.75
10 0.25 0.75
10 1
1 1 2 5
2 2 3 5
3 3 4 0
4 4 5 0
5 5 6 0
6 6 1 0
7 7 8 9
8 8 9 9
9 9 10 9
10 10 7 9
1
1 0.5 0.5
0
POLY
g3cli mesh2d l.poly l_out.ele --max-area 0.1 --min-angle 25
#   -> "files": ["l_out.node", "l_out.ele", "l_out.poly"], "rings_from_segments": 2, "area": 2.75 (the L minus the hole)
```

**Quadtree queries on a terrain** (`query2d`, `TriangleQuadtree2d`): heights at points (exact containment; with
`--tolerance`, points just outside get the height of the nearest triangle), the triangles whose boxes meet a
rectangle, and the nearest triangles, here on the mesh above. A point inside several triangles (on an edge or a vertex,
or overlapping layers) lists them all in `hits`; `height` and `triangle` come from the lowest id (`exact`: true), or,
outside every triangle, from the nearest one within the tolerance (`exact`: false):

```
g3cli query2d field.obj --points "10,10;50,30;85,30" --rect "65,25,75,35" --nearest 2
#   -> "points": [ { "x": 10, "y": 10, "height": 11.54, "triangle": 46, "exact": true, "hits": [...], "nearest": [46, 58] },
#                  { "x": 50, "y": 30, "height": null, ... },   (in the building: no triangle)
#                  { "x": 85, "y": 30, "height": 21.5, ... } ],
#      "rect": { "triangles_count": 58, "triangles": [...] }
```

**Slope design** (`slope`, `TerrainSlopeDesign`): toe lines as GeoJSON (a `LineString` or `Polygon` per toe, with `side`
and `slope` properties, coordinates as given, `crs` passed through), the terrain as a TIN mesh file. The outputs are
checked against the library's result in `SlopeCliTests`; the input format is in the
[g3cli README](../tools/g3cli/README.md#slope-design-input-slope):

```
g3cli slope dgm_tin.ply pit_toe.geojson pit_composite.ply --tolerance 0.05 --catch-lines pit_catch.geojson --region pit_region.ply
#   -> "design_status": "success", "status_kind": "none", "complete": true, "cut_volume": ..., "fill_volume": 0,
#      "catch_lines": [{"points": ..., "closed": true, "max_uncertainty": ...}], "min_feature_size": 0.0149, "attempts": 1, ...
g3cli slope dgm_tin.ply road.geojson part.g3mesh --tolerance 0.05 --direction auto --composite replacement
g3cli slope dgm_tin.ply pit_toe.geojson local.g3mesh --tolerance 0.05 --local-frame     # outputs local: add "used_origin"
```

A design that cannot be certified fails with `operation_failed`, an invalid toe or terrain with `read_error`; the error
result carries `design_status`, `status_kind`, `design_message`, `toe_problems` and `problem_triangles`.

**n-ary and SafeRounding booleans** (`boolean`, `resolve`; `ExactMeshBoolean`), on closed meshes `a.stl`, `b.stl`,
`c.stl` of your own. `xor` keeps the points whose winding number is positive in an odd number of the inputs, so for three
inputs it equals `(a xor b) xor c` and keeps the core inside all three; its parts touch along the curves where the input
surfaces cross (closed, with copies of the vertices there, like solids that touch along an edge, so `measure` counts the
contacts as self-intersections):

```
g3cli boolean a.stl b.stl xor.stl --op xor --more c.stl        # inside an odd number of the inputs, one arrangement
g3cli boolean a.stl b.stl two.stl --op at_least --k 2 --more c.stl
#   -> "boolean": { "safe_rounding": true, "rounding_clean": true,
#                   "rounding": { "initial_intersections": ..., "remaining_intersections": 0, "max_drift": ..., ... } }
g3cli boolean a.stl b.stl plain.stl --op union --no-safe-rounding            # plain rounding, faster ("rounding": null)
g3cli resolve overlapping.off solid.ply --simplify-flat-regions false        # keep the crossings of coplanar faces
g3cli resolve site_utm.stl solid.stl --strict-rounding     # fail (RoundingNotClean) rather than write defects
g3cli resolve site_utm.stl solid.stl --rounding-ulp-factor 0 --rounding-absolute-tolerance 1e-3   # the bound in metres
```

Far from the origin (UTM coordinates) the repair may move the surface by up to `--rounding-ulp-factor` (256) units in the
last place of the largest coordinate beyond the relative bound, or `--rounding-absolute-tolerance`; `--strict-rounding` allows
the relative bound only and fails instead of writing a result with defects (`ExactMeshBoolean.RoundingUlpFactor`,
`RoundingAbsoluteTolerance`, `StrictRounding`); `--rounding-max-displacement` sets the relative bound itself (default 1e-5 of
the diagonal, `RoundingMaxDisplacement`), `--rounding-max-pair-tests` its effort budget (default 5M exact pair tests,
`RoundingMaxPairTests`; past it the repair stops and reports `rounding.evaluation_limit_reached`). `repair` takes the same five options for its exact stage (`MeshRobustRepair`), where a
strict rounding that is not clean rejects the exact resolve (`exact_resolve_reason`) and the other stages run, while the repair
after the separation of touching vertices uses the relative bound only and does not fail; the two rounding
repairs of that stage are reported as `exact_resolve_rounding` and `separated_rounding`.

`rounding` has the counts of `MeshRoundingRepair` (defects before and after, edits, removed tiny components, the
largest drift and `max_length`, the bound: 1e-5 of the largest component's diagonal, or far from the origin the ulp or absolute
term, `scale_disparity`, the closed components kept although thin by the bound, with real volume: not clean, and
`scale_not_finite`, a diagonal that is not finite or overflows: every edit rejected, not clean). **Robust repair options** (`repair`, `MeshRobustRepair`):
`--exact-resolve-max-pairs`, `--safe-rounding`, `--alpha-wrap`, `--simplify-alpha-wrap`, `--check-lost-components`,
`--reject-topology-change`; the result reports `used_alpha_wrap`, `alpha_wrap_simplified`, `wrap_reason`, `used_input`
and `result_reason`. Parts far apart compared to their size (eg one 1e8 away from parts of size 1) make the degenerate
area of the clean check (1e-12 of the squared diagonal of all triangles) cover their real triangles: the repair then fails
with the message "scale disparity: ..." and writes nothing, instead of a coarse wrap (`MeshRobustRepair.Apply()` returns
false with the exactly resolved mesh, `ResultReason` `ScaleDisparity`):

```
g3cli repair scan.obj scan_fixed.stl --check-lost-components --no-alpha-wrap
```

## Adding an example

1. Add `docs/examples/<Name>Example.cs`: a `public static class` in namespace `g3.Examples` with plain
   library calls and short comments. It is compiled into the test project only, not into the library.
2. Add a test `<Name>Example_<WhatItChecks>` to `DocExamplesTests` that runs it and asserts the documented
   behaviour, so that it fails if the behaviour changes.
3. Link the file from the table above. `DocExamplesTests.EveryExample_IsTestedAndLinked` enforces 2 and 3.
