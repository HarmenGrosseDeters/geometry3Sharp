# Remeshing, Simplification, Smoothing, Deformation & Cutting

This guide covers isotropic triangle remeshing (`Remesher`, `RemesherPro`), QEM simplification (`Reducer`, `PartitionedReducer`, `MeshEnvelope`), quad-dominant and pure-quad remeshing (`QuadRemesher`), smoothing, handle-based deformation (`ArapMeshDeformer`, `BiharmonicMeshDeformer`), and plane cutting (`MeshPlaneCut`).

---

## Overview

| Task | Classes | Mutability | Example |
|---|---|---|---|
| Isotropic triangle remeshing | `g3.Remesher`, `gs.RemesherPro`, `g3.RegionRemesher`, `g3.EdgeLoopRemesher` | In-place | [RemeshExample.cs](../examples/RemeshExample.cs) |
| QEM simplification & certified deviation | `g3.Reducer`, `gs.PartitionedReducer`, `gs.MeshEnvelope` | In-place (`PartitionedReducer`: new mesh) | [SimplificationExample.cs](../examples/SimplificationExample.cs) |
| Quad-dominant & pure-quad remeshing | `gs.QuadRemesher`, `g3.DPolyMesh`, `g3.PolyMeshIO` | New `DPolyMesh` | [QuadRemeshExample.cs](../examples/QuadRemeshExample.cs) |
| Surface smoothing | `g3.MeshIterativeSmooth`, `g3.LaplacianMeshSmoother` | In-place | [SmoothingExample.cs](../examples/SmoothingExample.cs) |
| Handle-based deformation | `g3.ArapMeshDeformer`, `g3.BiharmonicMeshDeformer`, `g3.LaplacianMeshDeformer` | New / in-place (see below) | [DeformationExample.cs](../examples/DeformationExample.cs) |
| Plane cut & cap | `g3.MeshPlaneCut` | In-place | [PlaneCutExample.cs](../examples/PlaneCutExample.cs) |

---

## Isotropic Remeshing (`Remesher`, `RemesherPro`)

- **Namespace & classes:** `g3.Remesher`, `gs.RemesherPro`, `g3.RegionRemesher`, `g3.EdgeLoopRemesher`, `g3.MeshConstraintUtil`, `g3.MeshProjectionTarget`, `gs.RemeshSizing`
- **Input / mutability:** Modifies `DMesh3` in place (pass `new DMesh3(mesh)` to keep the input).
- **Guarantees & limits:** Upstream defaults do not guard against flipped normals, low-quality slivers, or crossing sharp creases. For CAD, flat, or creased inputs, enable constraints and opt-in quality/self-intersection safeguards below. `RejectInvertingPasses` (on by default) rolls back any pass that would turn a closed component inside out or flatten it to zero volume (`InvertingPassesRejected`). Non-finite vertices abort the pass safely (`NonFiniteVertices`).
- **Example:** [RemeshExample.cs](../examples/RemeshExample.cs)
- **Further reading:** [REDUCER_REMESHER_20.md](../design/REDUCER_REMESHER_20.md), [REMESHER_PHASE2_MEASURED.md](../perf/REMESHER_PHASE2_MEASURED.md)

```csharp
using g3;

var remesher = new Remesher(mesh);
remesher.SetTargetEdgeLength(0.5);
remesher.SetProjectionTarget(MeshProjectionTarget.Auto(mesh));
MeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, mesh, creaseAngleDeg: 40);
MeshConstraintUtil.PinSharpVertices(remesher, mesh, minAngleDeficitDeg: 75);
remesher.PreventNormalFlips = true;
remesher.MinTriangleQuality = 0.2;   // or remesher.ApplySafePreset()
// A fixed pass count can stop in the middle of the refinement (the passes needed grow with the ratio of the
// input's to the target's edge length): loop until few edges are modified, not for a fixed count.
for (int i = 0; i < 50; ++i) {
    remesher.BasicRemeshPass();
    if (remesher.ModifiedEdgesLastPass < 0.15 * mesh.EdgeCount)
        break;      // converged (ish); also stop for good if InvertingPassesRejected > 0
}
```

### Safeguards & Sizing Options

- **Feature preservation:** `MeshConstraintUtil.PreserveBoundaryAndFeatures` locks boundary edges, sharp crease edges, and bowtie vertices. Call `MeshConstraintUtil.PinSharpVertices` afterward to pin sharp apices (such as cone tips) by angle deficit.
- **`MinTriangleQuality` (recommended `0.2`, opt-in):** Rejects flips, collapses, smoothing, and projection moves that create degenerate or sliver triangles.
- **Turns against the surrounding surface (with `PreventNormalFlips` or `MinTriangleQuality`):** A collapse or valence flip is also rejected if a new triangle ends more than 70 degrees from the normal of the triangles around the edge (weighted with the cosine to its own previous normal) and farther from it than before. The flip test alone compares a triangle with its own previous normal, so turns of thin, tilted triangles added up and left triangles facing into smooth surfaces (issue #1441). Triangles that do not turn (collapses along a crease) are never rejected by it. Not covered: `PreventNormalFlips` alone does not guard smoothing and projection moves; see the summary of `Remesher.MinTriangleQuality` for what stays unguarded.
- **Convergence and what the options do not guarantee (issue #1578):** `ModifiedEdgesLastPass` (edges split, collapsed or flipped in the last pass) divided by the edge count is the convergence signal; there is no other counter. A box of 12 triangles refined to 4 % of its diagonal with `PreventNormalFlips` modifies 32 to 42 % of its edges in passes 3 to 7, holds 1 to 11 triangles facing against the input surface and self-intersections there (the same box at a 1 % target and 10 passes: 807 inverted triangles of 103946, all in the unconverged state), and none from pass 8 on (14 % modified at pass 13); `RemesherConvergenceReportTests` pins this. `MinTriangleQuality` (also in `ApplySafePreset()`) guards the edge operations and the smoothing and projection moves, not a floor under the result: splits and the projection of their new vertices are not guarded, so after the loop above 8 to 10 triangles of that box stay below 0.2 (11013 of 73274 below 0.1 after the 1 % box's 10 passes). Count them if a bound matters. The safe preset costs about 6 to 17 times the default on these boxes (the exact intersection tests of the new triangles with their neighbours, about 40 % of it in `TriangleHashGrid.Query` and its tests, linear in the triangle count), so a 30 pass 1 % target takes 25 s natively.
- **`PreventSelfIntersections` (opt-in):** Rejects any edit that introduces a new self-intersection; inputs without self-intersections remain self-intersection-free (higher runtime cost).
- **`ApplySafePreset()` (opt-in):** Sets `MinTriangleQuality = 0.2` and `PreventSelfIntersections = true`.
- **Spatially varying edge length (`TargetEdgeLengthF`):** Provide a custom sizing function, or use `RemeshSizing.FromWallThickness` to refine thin walls so opposite faces do not fold through each other.
- **`RemesherPro`:** Extends `Remesher` with active-set `FastestRemesh()`, `SharpEdgeReprojectionRemesh()`, and `FastSplitIteration()`. Queued passes are deterministic across thread counts.

---

## Mesh Simplification (`Reducer`, `PartitionedReducer`, `MeshEnvelope`)

- **Namespace & classes:** `g3.Reducer`, `gs.PartitionedReducer`, `gs.MeshEnvelope`
- **Input / mutability:** `Reducer` modifies `DMesh3` in place. `PartitionedReducer` returns a new simplified mesh without attributes.
- **Guarantees & limits:** Uses Quadric Error Metrics (QEM) centered at `quadricOrigin` (bounds center), with `PreserveBoundaryShape` (default on) and `PreventFoldOver` (default on, guarding against multi-collapse surface folding and vertical boundary walls). Bowtie vertices are pinned automatically.
- **Example:** [SimplificationExample.cs](../examples/SimplificationExample.cs)
- **Further reading:** [BOUNDED_SIMPLIFICATION.md](../design/BOUNDED_SIMPLIFICATION.md), [ReducerBenchmark README](../../benchmarks/ReducerBenchmark/README.md)

```csharp
using g3;

var reducer = new Reducer(mesh) {
    MaxDeviation = 0.01,
    PreventSelfIntersections = true
};
reducer.ReduceToTriangleCount(1000);
```

### Deviation Bounds, Guards & Modes

- **`MaxDeviation` (one-sided certified bound):** Guarantees every point of the reduced mesh lies within `MaxDeviation` of the input using certified bounds (`TriangleDeviationBounds`). Thin input spikes or open rims can still be clipped.
- **`MaxDeviationTwoSided` (opt-in):** Also keeps every point of the input within `MaxDeviation` of the result, certifying the two-sided Hausdorff distance. Applies to `FastCollapsePass` as well (cannot be combined with post-reduction projection, custom `SetDeviationReference`, or external mesh edits between passes).
- **`FastCollapseEndpointFallback` (opt-in, `FastCollapsePass` only):** When a midpoint collapse fails a guard (flip, fold-over, `MaxDeviation`, `PreventSelfIntersections`), retries collapsing onto the sharper endpoint while enforcing all bounds and constraints.
- **`ReduceToMaxQuadricError(maxError)`:** Checks the final constrained collapse position's double-precision quadric error against `maxError` (note: quadric error is not a Hausdorff bound and does not cover `AfterRefinement` projection).
- **`PreventSelfIntersections`:** Uses `TriangleTriangleFilter` in front of exact triangle-triangle tests, including axis-flat and touching boxes.
- **`PartitionedReducer` (opt-in):** Runs `ReduceToMaxQuadricError` across 8 fixed slabs in parallel. Deterministic for any thread count (differs from serial `Reducer`), best suited for `PreventSelfIntersections` on meshes above ~20k triangles; does not preserve attributes or boundary shapes.

### Exact Envelope Containment (`MeshEnvelope`)

- **Namespace & class:** `gs.MeshEnvelope`
- **Guarantees & limits:** Decides exactly whether a point (`Contains`), segment (`ContainsSegment`), or triangle (`ContainsTriangle`) lies inside the union of convex prisms around the reference triangles (`0.8 * eps` normal, `0.55 * eps` sideways; Wang et al. 2020). A `true` return guarantees the distance bound regardless of floating-point rounding. Check `Status` (`Ready`, `InvalidEpsilon`, `TooThin`, `Empty`, `TooLarge`, `OutOfRange`).

```csharp
var envelope = new MeshEnvelope(reference, 0.01);
bool inside = envelope.Status == MeshEnvelope.EnvelopeStatus.Ready
    && envelope.ContainsTriangle(a, b, c);
```

---

## Quad Remeshing (`QuadRemesher`, `DPolyMesh`)

- **Namespace & classes:** `gs.QuadRemesher`, `gs.CrossField`, `gs.PositionField`, `gs.IntegrableFrames`, `g3.DPolyMesh`, `g3.PolyMeshIO`, `gs.PolyMeshMetrics`
- **Input / mutability:** Read-only manifold triangle mesh (`DMesh3`); outputs a new `DPolyMesh` (with whole quad/polygon faces).
- **Guarantees & limits:** Computes a smooth cross field (or principal-curvature-aligned / crease-aligned via `CurvatureAlignment`, `AlignCreases`, `CreaseAngleDegrees` when `Field == null`), integrates an integer-lattice position field (boundary corners pinned above `CornerAngleDegrees`), and extracts a quad-dominant polygon mesh (mostly quads with a few triangles/pentagons/hexagons near singularities). Requires an explicit `TargetEdgeLength`.
- **Opt-in size adaptation (`SizeAdaptation`):** `Off` (default, uniform spacing), `Scale`, or `ScaleAndRotation` runs `IntegrableFrames` to reduce lattice curl on closed smooth shapes (applied only if curl drops by at least `SizeAdaptationMinGain`, default 20 %; can be combined with `PureQuads`).
- **Example:** [QuadRemeshExample.cs](../examples/QuadRemeshExample.cs)
- **Further reading:** [QUAD_REMESHING.md](../design/QUAD_REMESHING.md)

### Size Adaptation, User Sizing, Sharp Features & Pure-Quad Mode

- **`SizeAdaptation` (opt-in: `Off`, `Scale`, `ScaleAndRotation`):** Solves `IntegrableFrames` for a smooth spacing field (and optional small cross-field rotation) that lowers lattice curl and cuts irregular vertices on curved surfaces. On open meshes, boundary rotations are eliminated from the normal equations and `IntegrableFrames.BoundarySpacingWeight` (default `0.05`) keeps boundary spacing bounded without changing closed meshes.
- **`SizeFunction` (opt-in, default `null`):** Supplies a desired local edge length in mesh units (`Func<Vector3d, double>`, compatible with `RemeshSizing.FromWallThickness`). Solved through `IntegrableFrames` with weight `SizeWeight` (default `1.0`); when active, omits the mean-log-spacing = 0 gauge so absolute sizes are preserved and skips the `SizeAdaptationMinGain` curl gate. Invalid samples (`NaN`, `<= 0`, infinity) are ignored and counted in `IntegrableFrames.InvalidSizeSamples`. Targets more than `MaxSpacingRatio` (default 2.5) times larger or smaller than `TargetEdgeLength` are clamped, so the requested size is not reached there; `IntegrableFrames.ClampedSizeSamples` counts them (a subset of `ValidSizeSamples`). Can be combined with `PureQuads` (local spacing in the repair).
- **`PreserveCreases` (opt-in, default `false`):** Treats interior sharp creases (dihedral angle above `CreaseAngleDegrees`, default `30°`) as lattice lines and crease corners/junctions as fixed lattice points in `PositionField` (and enables `CrossField.AlignCreases` automatically when `Field` is `null`), preserving sharp CAD edges and corners. Not combined with `PureQuads` (`InvalidInput`: the pure-quad repair does not keep feature curves).
- **`PureQuads` (opt-in, closed meshes only):** Runs the pure-quad topology repair stage after extraction (can be combined with `SizeAdaptation` for local spacing and frame rotation). Inspect `PureQuadStatus` (and, as a diagnostic that is not a check, `PureQuadAreaRatio`: the lattice area of the integer map over the surface area, about 1 when the map covers the surface once, NaN when the attempt ended before or in the flow; `PureQuadAreaRatioAfterFlow` is the value right after the flow) (`Success` means quads only; otherwise names the first failed check and falls back to the quad-dominant mesh rather than failing; open meshes report `FlowInfeasible`). A failed attempt on a closed mesh is repeated once internally with the fixed variables of its flow freed (no option; a result that succeeds on the first attempt is unchanged); if both fail, the status and counters are those of the first attempt.
- **Saving quad meshes:** Use `PolyMeshIO` to write `.obj` (4-vertex `f` lines) or `.ply` (ASCII/binary). Triangle-only formats (STL, OFF, 3MF, glTF) cannot store quads.

---

## Surface Smoothing

- **Namespace & classes:** `g3.MeshIterativeSmooth`, `g3.LaplacianMeshSmoother`
- **Input / mutability:** Modifies `DMesh3` in place.
- **Guarantees & limits:** `MeshIterativeSmooth` applies iterative Laplacian smoothing with uniform, cotangent, or mean-value weights. `LaplacianMeshSmoother` solves a global soft-constrained sparse system centered at the mesh bounding box (so results are translation-invariant even at UTM coordinates).
- **Example:** [SmoothingExample.cs](../examples/SmoothingExample.cs)

---

## Handle-Based Deformation

- **Example:** [DeformationExample.cs](../examples/DeformationExample.cs)

| Class | Energy / properties | Rotation invariance | Further reading |
|---|---|---|---|
| `g3.ArapMeshDeformer` | As-rigid-as-possible local/global iteration, nonnegative cotan weights, Anderson acceleration with energy safeguard, exact handle positions | **Yes** (compatible with rotations of mesh and targets) | [ARAP_DEFORMATION.md](../design/ARAP_DEFORMATION.md) |
| `g3.BiharmonicMeshDeformer` | Minimizes squared cotangent Laplacian, exact handle positions, equivariant under similarity transforms, bounded by `MaxMatrixContributions` | No (use `ArapMeshDeformer` for large rotations) | [BIHARMONIC_DEFORMATION.md](../design/BIHARMONIC_DEFORMATION.md) |
| `g3.LaplacianMeshDeformer` | Basic uniform-weight Laplacian deformation via CG solve | No | — |

Both `ArapMeshDeformer` and `BiharmonicMeshDeformer` report explicit `Status` and convergence residuals (`FixedPointResidual` / `StationarityResidual`).

---

## Plane Cutting (`MeshPlaneCut`)

- **Namespace & class:** `g3.MeshPlaneCut`
- **Input / mutability:** Modifies `DMesh3` in place, removing the positive half-space (`planeNormal` side) and returning `CutLoops` and `CutSpans`.
- **Guarantees & limits:** Uses a scale-relative collapse tolerance (`RelativeDegenerateEdgeTol = 1e-6 * diagonal` when `DegenerateEdgeTol` is at its default) and avoids overflow up to `2^1020`, keeping cuts scale-invariant. Optionally caps open cut loops via `PlanarHoleFiller`.
- **Example:** [PlaneCutExample.cs](../examples/PlaneCutExample.cs)
