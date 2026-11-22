# Geodesics, Intrinsic Triangulations & UV Parameterization

This guide covers intrinsic Delaunay triangulations (`IntrinsicTriangulation`), heat and exact polyhedral geodesic distances (`MeshGeodesicDistance`, `PolyhedralGeodesicDistance`), vector heat transport (`VectorHeatMethod`), geodesic paths and Bezier curves (`MeshGeodesicPath`, `MeshGeodesicBezier`), UV atlases (`UVAtlasGenerator`), and single-chart parameterizations & UV untangling.

---

## Geodesic Distances & Vector Heat

| Goal | Classes | Method & accuracy | Example |
|---|---|---|---|
| Fast geodesic distance from source vertices | `gs.MeshGeodesicDistance` | Heat method on intrinsic Delaunay mesh (default on; opt-in `IntrinsicRefine`) | [DistancesExample.cs](../examples/DistancesExample.cs) |
| Exact polyhedral shortest distance & globally shortest surface path | `gs.PolyhedralGeodesicDistance`, `gs.PolyhedralGeodesicPath` | Window propagation in `double` (`~5e-15` relative); `TracePath` to vertex or triangle point | — |
| Parallel transport, scalar extension, geodesic log map | `gs.VectorHeatMethod` | Vector heat method on intrinsic triangulation (opt-in `IntrinsicRefine`) | — |
| Intrinsic Delaunay flips, refinement & common subdivision | `gs.IntrinsicTriangulation`, `gs.IntrinsicCommonSubdivision` | Signpost intrinsic mesh; non-negative cotan Laplacian | [IntrinsicTriangulationExample.cs](../examples/IntrinsicTriangulationExample.cs) |
| Straighten edge path / closed loop into an intrinsic geodesic | `gs.MeshGeodesicPath` | FlipOut edge flips (Sharp & Crane 2020); locally shortest in homotopy class | [IntrinsicTriangulationExample.cs](../examples/IntrinsicTriangulationExample.cs) |
| Geodesic Bezier curves on a surface | `gs.MeshGeodesicBezier` | De Casteljau via geodesic subdivisions on a shared `IntrinsicTriangulation` | — |

All intrinsic and geodesic math uses correctly rounded `CoreMath` elementary functions, making results bit-identical across operating systems, CPUs, and .NET runtimes.

---

## Intrinsic Triangulations (`IntrinsicTriangulation`)

- **Namespace & classes:** `gs.IntrinsicTriangulation`, `gs.IntrinsicRefinementResult`, `gs.IntrinsicCommonSubdivision`
- **Input / mutability:** Read-only `DMesh3`; builds an intrinsic signpost triangulation leaving the input mesh unchanged.
- **Guarantees & limits:** Keeps the original surface geometry while replacing edges with intrinsic geodesics. `FlipToDelaunay()` eliminates negative interior cotangent weights; `DelaunayRefine(minAngle, maxInsertions)` also splits obtuse boundary edges and inserts circumcenters (Chew/Ruppert refinement, Sharp, Soliman & Crane 2019). `minAngle` is clamped to `[0, 30]` degrees (`0` splits obtuse boundary edges only); default insertion limit is `10 * V + 1000`.
- **Example:** [IntrinsicTriangulationExample.cs](../examples/IntrinsicTriangulationExample.cs)
- **Further reading:** [GEODESICS.md](../design/GEODESICS.md), [IntrinsicBenchmark README](../../benchmarks/IntrinsicBenchmark/README.md)

```csharp
using g3;
using gs;

IntrinsicRefinementResult result;
var T = IntrinsicTriangulation.BuildDelaunay(mesh, refine: true, out result, minAngle: 25);
SymmetricSparseMatrix L = T.CotanLaplacian();   // T.VertexID(i) = input vid, or -1 if inserted

var geodesic = new MeshGeodesicDistance(mesh) { IntrinsicRefine = true };
geodesic.Compute(new[] { source });             // distances reported at input mesh vertices

IntrinsicCommonSubdivision overlay = T.ExtractCommonSubdivision();

// opt-in: integer coordinates, for a common subdivision without tracing (before the first flip)
var Ti = new IntrinsicTriangulation(mesh);
Ti.EnableIntegerCoordinates();
Ti.DelaunayRefine(0);                           // flips and boundary splits keep them
IntrinsicCommonSubdivision exact = Ti.ExtractCommonSubdivision();
```

### Refinement Status & Common Subdivision

- **`IntrinsicRefinementResult.Status`:**
  - `Success` — Delaunay, all cotan weights non-negative, angle bound met (input corners with angle sum `< 60°` cannot be improved and are reported in `SkippedTriangles`; concentric shells at powers of two of mean edge length ensure termination).
  - `InsertionLimitReached` — valid and Delaunay, though some triangles remain below `minAngle`.
  - `Incomplete` — a circumcenter landed on an existing vertex.
  - `NonManifoldInput` — bowtie vertices prevented refinement (flips only).
- **Common subdivision (`ExtractCommonSubdivision`):** Traces every intrinsic edge across input triangles and validates that output polygons tile both meshes (area checked to `1e-6`). Reports `Status = Inconsistent` with `Error` rather than emitting corrupt faces if floating-point tracing disagrees. That includes a false crossing, which the areas do not show: a trace that crosses an input edge of its own tail or head vertex just outside the snap tolerance (it would add a subdivision vertex with three edges; #1313), and faces lost where crossings coincide up to rounding (edges of the result with a face on one side only; #1326: the boundary of every input triangle must be a cycle of the arrangement of the traces on its own). An intrinsic triangle left without a face is reported too (#1335: the zero-area sliver between a vertex inserted within the snap tolerance, a barycentric 1e-9, of an input boundary edge and that edge passes the area check with none). `Faces` is empty unless `Status` is `Success`. A `Success` is checked by the areas and by these rules, not proven: the connectivity comes from floating-point traces (see the [integer coordinates study](../research/INTRINSIC_INTEGER_COORDINATES_2026-11.md)).
- **Integer coordinates (`EnableIntegerCoordinates`, opt-in):** see [Integer coordinates](#integer-coordinates) below.

### Integer coordinates

`IntrinsicTriangulation.EnableIntegerCoordinates()` switches on the normal coordinates and roundabouts of Gillespie, Sharp and Crane 2021: two integers per halfedge that count how the input edges cross the intrinsic edges. `ExtractCommonSubdivision` then takes the connectivity of the subdivision from these integers and does not trace.

- **When to call:** before the first flip or insertion. Later it returns `false` and changes nothing.
- **What keeps them:** `FlipEdge`, `FlipToDelaunay`, a `SplitEdge` of a boundary edge, and `DelaunayRefine(0)` (flips plus the splits of boundary edges opposite obtuse angles).
- **Known limit:** `InsertVertex`, a `SplitEdge` of an interior edge and the circumcenter insertions of `DelaunayRefine(minAngle > 0)` are not covered. They switch the integers off for good (`HasIntegerCoordinates` is `false`), and the extraction traces as without them. A flip whose new edge would be crossed more than `int.MaxValue` times does the same.
- **What is exact:** the connectivity (which input edge crosses which intrinsic edge, in which order) for the triangulation at hand. The Delaunay test, the edge lengths and the positions of the crossings stay floating point.
- **Positions:** a crossing lies on its input edge at the parameter the unfolded edge gives, clamped to the order of the crossings along that edge. Where rounding puts two crossings at the same place, a face can have the area 0.
- **Status:** `Success` means the integers are consistent (every input edge ends at the vertex its roundabouts name, every crossing is used once) and the faces tile every input triangle (to `1e-6`; `Face.Area` is never negative). On `Inconsistent`, `Faces` is empty and `Points` is not complete. `MaxIntrinsicAreaError` is reported but does not fail the result unless it is not finite (lengths whose squares leave the double range): relative to the area of a needle triangle the rounding of the lengths can exceed `1e-6` (measured `1.1e-5` on a ring sphere with vertices `2.6e-7` apart and `6.1e-5` on a grid stretched by `10^6`, both meshes on which tracing reports `Inconsistent`). So with the integers on, the intrinsic area error is reported and not gated; without them it is gated at `1e-6` as before.
- **Cost and gain** (`benchmarks/IntrinsicIntegerCoordinates`, stage `library`): 8 bytes per halfedge; flips 5 to 30 % slower with the layer on (20 and 24 % on the two meshes of 32 000 vertices); the extraction 6 to 28 times faster than tracing. On 27 adversarial meshes in 4 placements the result is valid in 108 of 108 runs, after flips and after `DelaunayRefine(0)`; tracing succeeds in 101, with the same subdivision.

---

## Geodesic Paths & Bezier Curves

### `MeshGeodesicPath` (FlipOut)

- **Namespace & class:** `gs.MeshGeodesicPath`
- **Input / mutability:** Read-only `DMesh3`; outputs `Status`, `Length`, `Path` (3D polyline), and `PathVertices`.
- **Guarantees & limits:** Starts from a Dijkstra or caller-supplied edge path and straightens wedges with angle `< π` by intrinsic flips. Returns an exact intrinsic geodesic in the homotopy class of the initial path (locally shortest; globally shortest is not guaranteed—use `PolyhedralGeodesicDistance.TracePath` for globally shortest paths). Boundaries are supported.
- **Statuses:** `Success`, `Collapsed` (contractible closed loop shrank to a point or fixed vertex), `NotConverged` (`MaxIterations` reached; valid and shorter), `Stuck` (self-overlapping path wedge; valid), `NoPath`, `InvalidInput` (bad IDs, non-adjacent edges, bowties), `TraceFailed` (`Length` valid, polyline trace incomplete).

```csharp
var path = new MeshGeodesicPath(mesh);
path.Compute(source, target);
path.ComputeThrough(new[] { a, b, c }, closed: true);
path.Shorten(edgeLoop, closed: true);
```

### `MeshGeodesicBezier`

- **Namespace & class:** `gs.MeshGeodesicBezier`
- **Guarantees & limits:** Evaluates surface Bezier curves via geodesic de Casteljau subdivision on one shared `IntrinsicTriangulation` (Morera, Carvalho & Velho 2008). Reproduces Euclidean Bezier curves on planes and unfolded Bezier curves on developable cylinders. Cost is `degree * (degree + 1) / 2` geodesics per parameter value (`O(N log N)` each).

```csharp
var curve = new MeshGeodesicBezier(mesh);
curve.Compute(new[] { a, b, c, d }, 32);   // curve.Points, curve.Path, curve.Length
```

---

## UV Atlases & Seams (`UVAtlasGenerator`)

- **Namespace & classes:** `g3.UVAtlasGenerator`, `g3.MeshDiscCut`, `g3.MeshChartSegmentation`, `g3.ConeFlattening`, `g3.UVOverlap`, `g3.UVPacker`
- **Input / mutability:** Read-only `DMesh3` (closed or open, any genus, multiple components); outputs a new seam-split `DMesh3` in `AtlasMesh` with vertex UVs packed into `[0, 1]^2` and mappings `AtlasToInputVertex`, `AtlasToInputTriangle`, `TriangleChart`.
- **Guarantees & limits:** Validates final double UVs with exact predicates (`UVOverlap`) and also checks the stored `float` UVs (`FloatRoundingDefects` reports defects caused purely by float rounding on tiny/sliver triangles). Returns `DistortionLimitExceeded` if `MaxCharts` was exhausted (the layout remains overlap-free).
- **Example:** [UVAtlasExample.cs](../examples/UVAtlasExample.cs)

```csharp
using g3;

var atlas = new UVAtlasGenerator(mesh);
if (atlas.Compute()) {
    DMesh3 textured = atlas.AtlasMesh;
}

// Manual cut + parameterization for closed surfaces:
var cut = new MeshDiscCut(mesh);
if (cut.Compute())
    new BoundaryFirstFlattening(cut.CutMesh).Compute();
```

### Atlas Methods

| `UVAtlasGenerator` Method | Charts produced | Best for |
|---|---|---|
| `Auto` (default) | One chart per component via cones when overlap-free and within distortion limits, else segmented charts | General texture atlases |
| `Cones` | Greedy cone singularities (`ConeFlattening`), falling back to charts if overlapping | Few seams, low area distortion |
| `Charts` | Normal-based `MeshChartSegmentation`, recursively split until overlap-free and within distortion bounds | Complex / high-genus geometry |
| `SingleChart` | Cut to a single disc (`MeshDiscCut`) and flattened with BFF | Mandatory single chart (higher distortion on closed surfaces) |

---

## Single-Chart Parameterization & Fixed-Boundary Untangling

For meshes that are already topological discs (or cut with `MeshDiscCut`), all parameterizers return a `ParameterizationResult` containing `Status`, `UV`, and `ParameterizationDistortion`:

| Class | Formulation & best use |
|---|---|
| `g3.BoundaryFirstFlattening` | Conformal flattening with prescribed boundary lengths or angles (minimal area distortion, disk, rectangle) |
| `g3.LSCMParameterization` | Least-squares conformal map (fast, free boundary) |
| `g3.SpectralConformalParameterization` | Opt-in pin-free conformal map (discs only; refuses non-discs or nearly double smallest eigenvalues—fall back to LSCM) |
| `g3.ARAPParameterization` | As-rigid-as-possible local/global solver balancing angle and area preservation |
| `g3.SLIMParameterization` | Symmetric Dirichlet energy minimization, guaranteed free of flipped triangles |
| `g3.TutteEmbedding` | Convex-combination fixed-boundary embedding |
| `g3.FoldoverFreeParameterization` | Untangles inverted/degenerate triangles in supplied UVs while keeping every boundary vertex fixed bit-for-bit |

### `FoldoverFreeParameterization` Contract

- **Input:** One connected open manifold mesh (including annuli) without degenerate surface triangles, plus finite starting UVs indexed by vertex ID. Input mesh and UV array are left unchanged.
- **Options:** `MaxIterations` (default `200`), `InitialEpsilon` (`1`), `MinimumEpsilon` (`1e-12`), `EnergyDecreaseTolerance` (`1e-3`).
- **Guarantees & limits:** `Status == Success` guarantees finite UVs, zero flipped or near-collapsed triangles (UV shape ratio `>= 1e-8` of surface triangle), and finite symmetric Dirichlet distortion. It does **not** guarantee global injectivity (run `UVOverlap.Compute(mesh, result.UV)` separately) or feasible fixed boundaries. If iterations exhaust with remaining invalid triangles, returns `FlippedTriangles` with the best finite iterate (`Converged = false`).
- **Further reading:** [FOLDOVER_FREE.md](../../benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md)
