# Operations

A map of the main mesh operations of geometry3Sharp: which class to use, how to call it, and a small
example for each. The examples in [examples/](examples/) are compiled into the test project and run by
`tests/geometry3Sharp.Tests/DocExamplesTests.cs`, which also checks the behaviour described here (volumes,
closedness, edge lengths, error bounds), so the examples cannot silently go stale. The API reference is
the XML documentation of the classes (shipped in the NuGet package, so IDEs and coding agents see it).

Without writing code: [tools/g3cli](../tools/g3cli/README.md) runs the main operations (measure, repair,
boolean, resolve, remesh, simplify, convert) on mesh files, with JSON results, and as an MCP server.

Namespaces: most classes are in `g3`; repair, solidify, hole filling, Hausdorff and geodesic distances are
in `gs`. Add both: `using g3; using gs;`.

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
  `IOCode` with a message instead of throwing.

## Operations

| Task | Classes | Example |
|---|---|---|
| Build meshes, generators | `DMesh3`, `TrivialBox3Generator`, `GridBox3Generator`, `Sphere3Generator_NormalizedCube`, `CappedCylinderGenerator` | [MeshConstructionExample](examples/MeshConstructionExample.cs) |
| Read / write OBJ, STL, OFF, PLY, 3MF, glTF/GLB | `StandardMeshReader`, `StandardMeshWriter`, `ReadOptions`, `WriteOptions`, `PLYReader`/`PLYWriter`, `ThreeMFReader`/`ThreeMFWriter`, `GLTFReader`/`GLTFWriter` | [MeshIOExample](examples/MeshIOExample.cs) |
| Repair (keep the triangles) | `gs.MeshAutoRepair`, `gs.MeshPostRepairCleanup` | [MeshRepairExample](examples/MeshRepairExample.cs) |
| Repair with a clean result guaranteed | `gs.MeshRobustRepair` | [MeshRepairExample](examples/MeshRepairExample.cs) |
| Fill holes | `gs.MinimalHoleFill`, `gs.SmoothedHoleFill`, `gs.AutoHoleFill`, `SimpleHoleFiller`, `MeshBoundaryLoops` | [HoleFillExample](examples/HoleFillExample.cs) |
| Check a mesh (self-intersections, folds, volume IoU) | `gs.MeshRepairMetrics`, `MeshRobustRepair.NotCleanReason`, `DMesh3.CheckValidity`, `MeshValidation` | [RepairMetricsExample](examples/RepairMetricsExample.cs) |
| Rebuild any input as a closed solid | `gs.MeshWindingNumberSolidify` | [WindingNumberSolidifyExample](examples/WindingNumberSolidifyExample.cs) |
| Booleans (union, intersection, difference), resolve self-intersections | `ExactMeshBoolean`, `ExactMeshBoolean.Resolve` | [ExactMeshBooleanExample](examples/ExactMeshBooleanExample.cs) |
| Isotropic remeshing | `Remesher`, `gs.RemesherPro`, `MeshConstraintUtil`, `MeshProjectionTarget` | [RemeshExample](examples/RemeshExample.cs) |
| Simplification, with a guaranteed maximum deviation | `Reducer` | [SimplificationExample](examples/SimplificationExample.cs) |
| Smoothing | `MeshIterativeSmooth`, `LaplacianMeshSmoother` | [SmoothingExample](examples/SmoothingExample.cs) |
| Plane cut and cap | `MeshPlaneCut` | [PlaneCutExample](examples/PlaneCutExample.cs) |
| Measurements (volume, area, bounds, edge lengths, genus) | `MeshMeasurements`, `MeshQueries` | [MeasurementsExample](examples/MeasurementsExample.cs) |
| Nearest point, ray hit, inside test, winding number | `DMeshAABBTree3`, `MeshQueries` | [SpatialQueriesExample](examples/SpatialQueriesExample.cs) |
| Nearest neighbours among points | `PointHashGrid3d<T>`, `PointAABBTree3` | [PointQueriesExample](examples/PointQueriesExample.cs) |
| Signed distance field, offset surfaces | `MeshSignedDistanceGrid`, `DenseGridTrilinearImplicit`, `MarchingCubes` | [OffsetSurfaceExample](examples/OffsetSurfaceExample.cs) |
| Hausdorff distance, geodesic distance | `gs.MeshHausdorffDistance`, `gs.MeshGeodesicDistance` | [DistancesExample](examples/DistancesExample.cs) |
| Intrinsic Delaunay triangulation and refinement: cotan Laplacian without negative weights, geodesics and vector heat on poor meshes, overlay with the input | `gs.IntrinsicTriangulation`, `gs.MeshGeodesicDistance.IntrinsicRefine`, `gs.VectorHeatMethod.IntrinsicRefine`, `gs.IntrinsicCommonSubdivision` | [IntrinsicTriangulationExample](examples/IntrinsicTriangulationExample.cs) |

## Which repair or boolean class?

| You have | Use | Result |
|---|---|---|
| A mostly good mesh with small holes, duplicate vertices, degenerate triangles | `MeshAutoRepair` (with `PostRepairCleanup = true`) | the input triangles, welded and closed where possible; not guaranteed closed |
| Any mesh, and you need a closed, manifold, self-intersection-free result | `MeshRobustRepair` | the repaired mesh if it is clean, otherwise its self-intersections resolved exactly (`UsedExactResolve`), otherwise the winding number solid (`UsedSolidify`, `FallbackReason`) |
| A triangle soup, overlapping parts, large holes | `MeshWindingNumberSolidify` | a new, remeshed closed surface; features below the cell size are lost |
| Closed meshes that intersect themselves or each other, where the triangles must be kept exactly | `ExactMeshBoolean`, `ExactMeshBoolean.Resolve(mesh)` | exact arrangement, only the output coordinates are rounded |
| Booleans of broken meshes (open, soups, overlapping shells) | `gs.MeshWindingNumberBoolean` | a new, remeshed closed surface (winding numbers on an adaptive octree) |

`MeshBoolean` is the old, non-robust union of the original library; prefer `ExactMeshBoolean`.

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

### Repair

```csharp
var repair = new MeshAutoRepair(mesh) { PostRepairCleanup = true };
repair.Apply();
DMesh3 repaired = repair.Mesh;           // a compacted copy: always read repair.Mesh

var robust = new MeshRobustRepair(mesh);
robust.Apply();                          // robust.Mesh is closed, manifold, without self-intersections
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
```

See [RemeshExample](examples/RemeshExample.cs), [SimplificationExample](examples/SimplificationExample.cs),
[SmoothingExample](examples/SmoothingExample.cs) and [PlaneCutExample](examples/PlaneCutExample.cs).

### Measurements and queries

```csharp
Vector2d volumeArea = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex);
var tree = new DMeshAABBTree3(mesh, autoBuild: true);
int tid = tree.FindNearestTriangle(point);
bool inside = tree.IsInside(point);             // closed meshes
double winding = tree.WindingNumber(point);     // ~1 inside, ~0 outside, also for open meshes
```

See [MeasurementsExample](examples/MeasurementsExample.cs), [SpatialQueriesExample](examples/SpatialQueriesExample.cs),
[PointQueriesExample](examples/PointQueriesExample.cs), [OffsetSurfaceExample](examples/OffsetSurfaceExample.cs)
and [DistancesExample](examples/DistancesExample.cs).

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
- On a plane of 169-degree triangles (8400 vertices) refinement inserts 10400 vertices in about 1 s and lowers
  the mean heat method distance error from 0.06 to 0.02 edge lengths, the maximum from 0.8 to 0.1.
- **Common subdivision**: every intrinsic edge is traced over the input and each input triangle is cut along
  the traced segments. The result is checked (faces tile every input triangle and every intrinsic triangle,
  areas to 1e-6); if traces are inconsistent (crossings in the wrong order) `Status` is `Inconsistent`
  with an `Error` instead of wrong faces. Integer (normal) coordinates (Gillespie, Sharp and Crane 2021),
  which make this combinatorially exact, are not implemented: with flips, insertions and edge splits the
  traces are exact up to rounding, and the checks catch the cases where rounding would matter.

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

## Adding an example

1. Add `docs/examples/<Name>Example.cs`: a `public static class` in namespace `g3.Examples` with plain
   library calls and short comments. It is compiled into the test project only, not into the library.
2. Add a test `<Name>Example_<WhatItChecks>` to `DocExamplesTests` that runs it and asserts the documented
   behaviour, so that it fails if the behaviour changes.
3. Link the file from the table above. `DocExamplesTests.EveryExample_IsTestedAndLinked` enforces 2 and 3.
