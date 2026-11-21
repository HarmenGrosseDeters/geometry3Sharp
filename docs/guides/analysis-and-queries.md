# Analysis, Metrics, Spatial Queries & Collision Detection

This guide covers mesh measurements, 3D spatial trees and batch queries (`DMeshAABBTree3`, `MeshBatchQueries`), 2D loose quadtrees (`LooseQuadtree2d` and typed adapters), distance/deviation/fidelity/curvature metrics, wall thickness, primitive fitting, and continuous collision detection.

---

## Core Measurements & 3D Spatial Queries

- **Namespace & classes:** `g3.MeshMeasurements`, `g3.MeshQueries`, `g3.DMeshAABBTree3`, `g3.PointHashGrid3d<T>`, `g3.PointAABBTree3`, `g3.MeshBatchQueries`
- **Examples:** [MeasurementsExample.cs](../examples/MeasurementsExample.cs), [SpatialQueriesExample.cs](../examples/SpatialQueriesExample.cs), [PointQueriesExample.cs](../examples/PointQueriesExample.cs), [BatchQueriesAndCcdExample.cs](../examples/BatchQueriesAndCcdExample.cs)
- **Further reading:** [BATCH_QUERIES.md](../design/BATCH_QUERIES.md)

```csharp
using g3;

Vector2d volumeArea = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex);

var tree = new DMeshAABBTree3(mesh, autoBuild: true);
int nearestTid = tree.FindNearestTriangle(point);
int hitTid = tree.FindNearestHitTriangle(new Ray3d(origin, dir));
bool inside = tree.IsInside(point);            // exact parity for closed meshes without self-intersections
double winding = tree.WindingNumber(point);    // ~1 inside, ~0 outside (works on open meshes too)
```

### Important Spatial-Query Guarantees

- **Watertight exact ray hits (`IntrRay3Triangle3` / `FindNearestHitTriangle`):** Hit/miss decisions use exact `Orient3d` predicates with no parallel epsilon; rays hitting shared edges or vertices never leak through cracks, and ties return the lowest triangle ID (matching linear search and invariant under power-of-two scaling).
- **Exact parity `IsInside`:** Uses symbolic perturbation (Simulation of Simplicity) so rays passing through edges or vertices count each crossing once. Valid for closed meshes without self-intersections; use `WindingNumber` or `FastWindingNumber` on open or broken meshes.
- **Exact ray-plus-boundary `WindingNumber` & `MeshWindingNumberGrid`:** Uses an exact symbolically perturbed `+x` ray plus the coupled Antipodal/FEWN exterior-edge boundary sum (`ExactPredicates.Orient2d`/`Orient3d`), returning exact integers in `O(log F)` on closed meshes (including unwelded closed STL soups), reusing one `+x` line traversal per row in `MeshWindingNumberGrid`, and remaining `2^k` scale-invariant across extreme/mixed exponents. Opt-in exact winding is also available on `WindingNumberOctree` / `MeshWindingNumberSolidify` / `MeshWindingNumberBoolean` (`UseExactWinding = true`), `MeshBatchQueries.IsInsideBatch` (`InsideMethod.ExactWindingNumber`), and `MeshDeviation` (`Signs.ExactWindingNumber`); threshold queries on large-boundary open meshes (`|∂M| >= 500`) automatically use certified hierarchical boundary clustering with exact fallback.
- **Scale-free `DMeshAABBTree3` & `FastWindingNumber`:** Bounding boxes are stored in `double` (accurate at UTM coordinates) with scale-relative padding (`2^-40`), and `FastWindingNumber` is scale-free across `2^-200..2^200`. Rebuild trees after modifying their mesh.
- **Batch queries (`MeshBatchQueries`):** Evaluates large sets of rays (including pinhole camera depth images), nearest-point queries, and inside tests (`Crossings`, `WindingNumber`, `ExactWindingNumber`) in parallel with deterministic order-independent output.

---

## 2D Loose Quadtrees (`LooseQuadtree2d` & Adapters)

- **Namespace & classes:** `g3.LooseQuadtree2d`, `g3.PointQuadtree2d`, `g3.SegmentQuadtree2d`, `g3.TriangleQuadtree2d`, `IQuadtreeVisitor2d`, `IQuadtreeDistance2d`
- **Input / mutability:** Stores immutable geometry snapshots with stable integer IDs (does not track later edits to source meshes or lists). `TriangleQuadtree2d.Over(array)` reads a caller-owned array without copying (the array must remain immutable while the tree is used).
- **Example:** [QuadtreeQueriesExample.cs](../examples/QuadtreeQueriesExample.cs)
- **Further reading:** [QUADTREE2D_SPEC.md](../design/QUADTREE2D_SPEC.md), [QuadtreeBenchmark README](../../benchmarks/QuadtreeBenchmark/README.md)

```csharp
using g3;

var tree = new TriangleQuadtree2d(triangles);
var ids = new List<int>(128);
tree.QueryRectangle(new AxisAlignedBox2d(0, 0, 20, 20), ids);
tree.FindNearestK(new Vector2d(10, 10), 4, ids);
bool found = tree.TryGetHeight(new Vector2d(10, 10), out double height, tolerance: 0.01);
```

### Key Semantics

- **IDs & mutation:** Bulk IDs match input positions; `Insert` returns a new ID and expands bounds; `Remove` never renumbers IDs or shrinks bounds. Failed validation consumes no ID.
- **Rectangle & segment queries:** Rectangle queries return exact inclusive AABB hits. Core segment queries test AABBs padded by `0.0002` (candidate filter); `SegmentQuadtree2d.QueryIntersections` tests exact closed-segment intersections including collinear overlaps and touches. Build `SegmentQuadtree2d` from vertices/edges or `Insert(a, b)` when exact endpoint hits matter (`Segment3d` rounds endpoints).
- **Height interpolation (`TriangleQuadtree2d.TryGetHeight`):** Containment uses exact 2D orientation signs. All exact hits take precedence over tolerance hits (lowest ID breaks ties); tolerance hits use absolute XY Euclidean distance and clamp to the nearest boundary point so interpolated height stays inside the triangle's `z` range. Zero-area triangles produce no height; unrepresentable heights throw `ArithmeticException`.
- **Zero-allocation & concurrency:** Buffer-taking queries clear the output buffer first; pre-size buffers or use struct visitors (`IQuadtreeVisitor2d`) for zero-allocation traversal. Concurrent read-only queries with separate caller-owned buffers are safe; inserts/removals must not overlap queries.
- **Validation:** Coordinates must be finite, boxes ordered, and tolerances finite and non-negative (`maxDistance` on nearest queries may be `+Infinity`). `parallelBuild` affects build speed only, not query semantics.

---

## Comparison, Quality & Shape Analysis

| Task | Classes | Output & key guarantees | Example |
|---|---|---|---|
| Certified Hausdorff distance | `gs.MeshHausdorffDistance` | Proven `LowerBound` and `UpperBound` within `Tolerance`; scale-free power-of-two internal frame | [DistancesExample.cs](../examples/DistancesExample.cs) |
| Signed/unsigned deviation map | `gs.MeshDeviation`, `gs.DeviationStatistics` | Per-vertex signed distance (pseudo-normal or winding sign), area-weighted stats, optional certified Hausdorff bound | [AnalysisBuildingBlocksExample.cs](../examples/AnalysisBuildingBlocksExample.cs) |
| Multi-metric mesh fidelity comparison | `gs.MeshFidelityMetrics` | F-score, Chamfer, normal consistency & reversed share, sharp-edge Chamfer/recall, outer volume IoU, curvature fidelity, topology changes | [FidelityMetricsExample.cs](../examples/FidelityMetricsExample.cs) |
| Discrete vertex curvature | `gs.MeshCurvature` | Mean (`H`) and Gaussian (`K`) curvature per vertex (Meyer et al. 2003: mixed Voronoi areas, cotan normal, angle deficit); no NaN/Inf output | — |
| Triangle quality & topology summary | `gs.TriangleQualityStatistics`, `gs.MeshTopologySummary` | Verdict quality percentiles (min/max angle, aspect ratio, scaled Jacobian, condition), genus, Euler characteristic, components | [FidelityMetricsExample.cs](../examples/FidelityMetricsExample.cs) |
| Wall thickness | `gs.MeshWallThickness` | Per-vertex / per-triangle thickness via inward watertight ray, shape diameter function (SDF), or largest inscribed ball | [AnalysisBuildingBlocksExample.cs](../examples/AnalysisBuildingBlocksExample.cs) |
| Geometric primitive fitting | `g3.PrimitiveFit3` | Fits plane, sphere, cylinder, or cone to 3D points (optional normals) via deterministic RANSAC + Levenberg-Marquardt | [AnalysisBuildingBlocksExample.cs](../examples/AnalysisBuildingBlocksExample.cs) |

### Notes on Key Metrics

- **`MeshFidelityMetrics`:** Catches defects that Hausdorff distance and raw volume IoU miss—such as reversed normal patches, smoothed-away sharp edges, or lost components. Outer volume IoU compares against the solid bounded by the input's outer faces (ignoring interior faces, nested shells, and double-sided sheets).
- **`MeshCurvature`:** Handles degenerate triangles, boundaries, and bowtie vertices gracefully (marking affected vertices `Valid == false` rather than producing NaN or infinite values).
- **`MeshHausdorffDistance`:** Automatically normalizes extreme scales (`< 2^-32` or `>= 2^65`) and far-from-origin coordinates (`> 2^24 * extent`) via exact power-of-two shifts/scales, and splits `MaxSubdivisions` fairly across both directions when `Symmetric = true`.

---

## Continuous Collision Detection (CCD)

- **Namespace & classes:** `g3.ContinuousCollision`, `g3.MeshSweepCollision`
- **Purpose:** Exact/conservative continuous collision queries for moving triangles and rigid/linearly sweeping meshes (first time of impact without tunneling).
- **Example:** [BatchQueriesAndCcdExample.cs](../examples/BatchQueriesAndCcdExample.cs)
- **Further reading:** [CCD.md](../design/CCD.md)
