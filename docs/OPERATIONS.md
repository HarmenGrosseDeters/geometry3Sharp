# Operations

A map of the main mesh operations of geometry3Sharp: which class to use, how to call it, and a small
example for each. The examples in [examples/](examples/) are compiled into the test project and run by
`tests/geometry3Sharp.Tests/DocExamplesTests.cs`, which also checks the behaviour described here (volumes,
closedness, edge lengths, error bounds), so the examples cannot silently go stale. The API reference is
the XML documentation of the classes (shipped in the NuGet package, so IDEs and coding agents see it).

Without writing code: [tools/g3cli](../tools/g3cli/README.md) runs the main operations (measure, repair,
boolean, resolve, remesh, simplify, uv, convert) on mesh files, with JSON results, and as an MCP server.

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
| Check a mesh (self-intersections, folds, volume IoU) | `gs.MeshRepairMetrics`, `MeshRobustRepair.NotCleanReason`, `DMesh3.CheckValidity`, `MeshValidation` | [RepairMetricsExample](examples/RepairMetricsExample.cs) |
| Rebuild any input as a closed solid | `gs.MeshWindingNumberSolidify` | [WindingNumberSolidifyExample](examples/WindingNumberSolidifyExample.cs) |
| Booleans (union, intersection, difference) of two or more meshes, winding number predicates (eg inside at least k of n), resolve self-intersections | `ExactMeshBoolean`, `ExactMeshBoolean.AtLeast`, `ExactMeshBoolean.Resolve` | [ExactMeshBooleanExample](examples/ExactMeshBooleanExample.cs) |
| Isotropic remeshing | `Remesher`, `gs.RemesherPro`, `MeshConstraintUtil`, `MeshProjectionTarget` | [RemeshExample](examples/RemeshExample.cs) |
| Simplification, with a guaranteed maximum deviation | `Reducer` | [SimplificationExample](examples/SimplificationExample.cs) |
| Smoothing | `MeshIterativeSmooth`, `LaplacianMeshSmoother` | [SmoothingExample](examples/SmoothingExample.cs) |
| Plane cut and cap | `MeshPlaneCut` | [PlaneCutExample](examples/PlaneCutExample.cs) |
| Measurements (volume, area, bounds, edge lengths, genus) | `MeshMeasurements`, `MeshQueries` | [MeasurementsExample](examples/MeasurementsExample.cs) |
| Nearest point, ray hit, inside test, winding number | `DMeshAABBTree3`, `MeshQueries` | [SpatialQueriesExample](examples/SpatialQueriesExample.cs) |
| Nearest neighbours among points | `PointHashGrid3d<T>`, `PointAABBTree3` | [PointQueriesExample](examples/PointQueriesExample.cs) |
| Signed distance field, offset surfaces | `MeshSignedDistanceGrid`, `DenseGridTrilinearImplicit`, `MarchingCubes` | [OffsetSurfaceExample](examples/OffsetSurfaceExample.cs) |
| Hausdorff distance, geodesic distance | `gs.MeshHausdorffDistance`, `gs.MeshGeodesicDistance` | [DistancesExample](examples/DistancesExample.cs) |
| UV atlas of any mesh: seams, charts, BFF with cones, packing, exact overlap test | `UVAtlasGenerator`, `MeshDiscCut`, `MeshChartSegmentation`, `ConeFlattening`, `UVOverlap`, `UVPacker` | [UVAtlasExample](examples/UVAtlasExample.cs) |
| Parameterize a disc | `BoundaryFirstFlattening`, `LSCMParameterization`, `ARAPParameterization`, `SLIMParameterization`, `ParameterizationDistortion` | [UVAtlasExample](examples/UVAtlasExample.cs) |

## Which repair or boolean class?

| You have | Use | Result |
|---|---|---|
| A mostly good mesh with small holes, duplicate vertices, degenerate triangles | `MeshAutoRepair` (with `PostRepairCleanup = true`) | the input triangles, welded and closed where possible; not guaranteed closed |
| Any mesh, and you need a closed, manifold, self-intersection-free result | `MeshRobustRepair` | the repaired mesh if it is clean, otherwise its self-intersections resolved exactly (`UsedExactResolve`), otherwise the winding number solid (`UsedSolidify`), otherwise the alpha wrap (`UsedAlphaWrap`); see `FallbackReason`. If the clean result lost volume (`MinVolumeIoU`) the input itself is resolved or solidified instead. `Apply()` returns false (`Mesh` unchanged) if cancelled or if neither the solidify nor the alpha wrap gave a mesh; a solidified result that is not clean either is kept when the wrap fails or is off (`UsedSolidify` and `SolidifyReason`) |
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

All parameterizations need discs. `MeshDiscCut` cuts any mesh into discs along a cut graph (tree-cotree with
shortest paths: on a closed surface of genus g, 2g short loops; between boundary loops, shortest paths; for a
sphere, a path), and splits the vertices along the seams in a new mesh. `ConeFlattening` is BFF with cone
singularities: cones are placed greedily where the conformal scale factor is largest, the mesh is cut through
them, and the rest becomes flat (a cube: its eight corners, unfolded without distortion). `UVAtlasGenerator`
combines them: per component, cones if the result is overlap-free and within the distortion limits, otherwise
charts grown by normal (`MeshChartSegmentation`) flattened with BFF and split in two while a chart overlaps
itself or exceeds `MaxAreaDistortion` / `MaxConformalDistortion`; then `UVPacker`. The final UVs are checked
with exact predicates (`UVOverlap`), in double and again as stored in the mesh (float; defects within the float
resolution, eg at vertices 1e-7 apart, are only reported in `SubResolutionDefects`), and `Status` reports `DistortionLimitExceeded` when `MaxCharts` was not
enough (the layout is still overlap-free). From the command line: `g3cli uv mesh.obj atlas.obj`.

| Method | Charts | Use |
|---|---|---|
| `Auto` (default) | one chart per component where cones work, else several | texture atlases |
| `Cones` | one chart through cones (falls back to charts if it overlaps) | few seams, low area distortion |
| `Charts` | normal-based charts, split until within the limits | robust, more seams |
| `SingleChart` | one chart (cut to a disc), plain BFF | when one chart is required; distorts closed surfaces |

See [UVAtlasExample](examples/UVAtlasExample.cs).

## Adding an example

1. Add `docs/examples/<Name>Example.cs`: a `public static class` in namespace `g3.Examples` with plain
   library calls and short comments. It is compiled into the test project only, not into the library.
2. Add a test `<Name>Example_<WhatItChecks>` to `DocExamplesTests` that runs it and asserts the documented
   behaviour, so that it fails if the behaviour changes.
3. Link the file from the table above. `DocExamplesTests.EveryExample_IsTestedAndLinked` enforces 2 and 3.
