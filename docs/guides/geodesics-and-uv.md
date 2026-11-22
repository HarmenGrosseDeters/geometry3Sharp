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
```

### Refinement Status & Common Subdivision

- **`IntrinsicRefinementResult.Status`:**
  - `Success` — Delaunay, all cotan weights non-negative, angle bound met (input corners with angle sum `< 60°` cannot be improved and are reported in `SkippedTriangles`; concentric shells at powers of two of mean edge length ensure termination).
  - `InsertionLimitReached` — valid and Delaunay, though some triangles remain below `minAngle`.
  - `Incomplete` — a circumcenter landed on an existing vertex.
  - `NonManifoldInput` — bowtie vertices prevented refinement (flips only).
- **Common subdivision (`ExtractCommonSubdivision`):** Traces every intrinsic edge across input triangles and validates that output polygons tile both meshes (area checked to `1e-6`). Reports `Status = Inconsistent` with `Error` rather than emitting corrupt faces if floating-point tracing disagrees.

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
