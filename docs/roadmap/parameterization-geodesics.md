# Parameterization and geodesics (Phase 3)

Part of the [roadmap](../ROADMAP.md).

- **Scope:** parameterization (LSCM, ARAP, SLIM, BFF, cones, seams, charts, UV packing), intrinsic triangulations
  and refinement, the vector heat method and log maps, geodesic paths, foldover-free maps.
- **Status (2026-11-08):** the Phase 3 operations are done; three small items are open. Foldover-free maps (Phase 5
  #7) merged in #374; the affine heat method stopped at its gate (#371).
- **Related:** [design/AFFINE_LOGMAP_GATE.md](../design/AFFINE_LOGMAP_GATE.md),
  [design/FOLDOVER_FREE_MAPS.md](../design/FOLDOVER_FREE_MAPS.md),
  [benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md](../../benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md);
  Phase 5 items in [research.md](research.md#phase-5-candidates-from-recent-research).

### Open

- **Near-field log map from affine heat** (priority: medium-low, small): a new gate on the local-parameterization use
  against `MeshLocalParam` and Legacy; code on branch `agents/implement-logarithmic-map-brief-2` at f8a5f388. A good
  owner-local brief. Details below.
- **The current log map is not exact on a plane** (priority: low): compare with the Vector Heat paper's reported
  errors first; a defect only if ours is clearly worse.
- **Integer (normal) coordinates** (Gillespie, Sharp, Crane 2021) for a combinatorially exact common subdivision;
  signposts with the area checks suffice for the current operations. Also a research question
  ([research.md](research.md#open-research-questions)).
- **Geodesic Bézier curves** (Phase 5 #1): paths, waypoints and loops are done, Bézier curves open.
- **Foldover-free maps** (Phase 5 #7, #374): open the paper's energy-decrease stop instead of epsilon <= 1e-6; the SLIM
  quality target is not universally met; the torus global-overlap fallback remains. Local flip freedom does not close
  the global-injectivity gap (cones are not guaranteed globally injective, eg on a torus).
- **P1 collapse** (issue-tracker follow-up): virtual fan vertices or spectral conformal; foldover-free maps are a lead
  ([platform.md](platform.md#release-when-its-done)).

### Done / history

- [x] Parameterization: LSCM, ARAP, SLIM (Rabinovich et al. 2017), Boundary First Flattening (Sawhney & Crane 2017);
  UV packing; distortion measures; sparse Cholesky solver (`SparseCholesky`)
- [x] Cutting closed surfaces into discs (seams) and automatic chart segmentation; BFF with cone singularities:
  `MeshDiscCut` (tree-cotree cut graph with shortest loops, Eppstein; Erickson & Whittlesey; any genus, boundaries,
  components, bowties; vertices split along the seams), `MeshChartSegmentation` (normal-based region growing with
  compactness, VSA / D-Charts style), `ConeFlattening` (BFF with greedily placed cones; a cube's corners become cones
  and it unfolds without distortion), `UVOverlap` (exact overlap test), and the pipeline `UVAtlasGenerator`
  (cones where overlap-free, else charts split until within the distortion limits, packing, status code);
  `g3cli uv`. Cones are not guaranteed globally injective (eg on a torus); the pipeline then falls back to charts.
- [x] Intrinsic Delaunay triangulations (Sharp & Crane), for stable Laplacians and heat on poor meshes:
  `IntrinsicTriangulation` (edge flips with signposts, tracing of intrinsic edges over the input mesh,
  intrinsic mollification of degenerate triangles); `MeshGeodesicDistance.IntrinsicDelaunay`. On a plane of
  169-degree triangles the mean distance error drops from 2.2 to 0.06 edge lengths.
- [x] Vector Heat Method (Sharp, Soliman, Crane 2019), including a robust log map (beyond `MeshLocalParam`):
  `VectorHeatMethod` (transport, scalar extension, log map), on the intrinsic Delaunay triangulation,
  with the direct solver `SparseCholesky`.
  - [ ] **Near-field log map from affine heat (priority: medium-low, small).** Brief 2 (#371) failed its global
    gate only toward the cut locus. Near the source, AffineLocal is far more accurate than the current log map
    ([AFFINE_LOGMAP_GATE.md](../design/AFFINE_LOGMAP_GATE.md)): exact on the plane where Legacy errs by 0.03-0.07; on
    the sphere for r < π/4 a mean of 0.0024 against 0.028; on the cylinder's first two bands 0.0006-0.0015 against
    0.05-0.08. Queries are also 2-3 times faster. Use: local parameterization (decals, texture stamps, surface
    brushes, local polar coordinates, geodesic means), the role of `MeshLocalParam` (discrete exponential map,
    Schmidt 2006). Task: a new gate on that use, comparing AffineLocal with `MeshLocalParam` and Legacy within a patch
    radius, on the smooth fixtures plus noisy and anisotropic meshes. If it wins clearly, ship it as
    `ComputeLogMap(int, LogMapStrategy.AffineLocal)` with its documented range ("accurate near the source, not for
    distances up to the cut locus"). Prior art: geometry-central offers AffineLocal exactly as the fast near-source
    variant. The code is on branch `agents/implement-logarithmic-map-brief-2` at f8a5f388; reuse it, do not port
    again. Why medium-low: no library caller (the value is API for users), after the mesher queue and the
    robustness and CI work; a good owner-local brief.
  - [ ] **The current log map is not exact on a plane (priority: low).** It errs by 0.03-0.07 on the planar grid
    of the #371 gate, up to 0.38 in the outer band. This is probably the heat method's own smoothing (time step
    ~h²). First compare with the Vector Heat paper's reported errors; treat it as a defect only if ours is clearly
    worse.
- [x] Intrinsic Delaunay *refinement* (vertex insertion): `IntrinsicTriangulation.DelaunayRefine` (insertion in
  faces and on edges with signpost updates, Chew / Ruppert circumcenter refinement with a minimum angle,
  boundary edges opposite obtuse angles split, concentric shells and skipped triangles at small input
  angles, status codes); options `IntrinsicRefine` in `MeshGeodesicDistance` and `VectorHeatMethod`,
  `IntrinsicTriangulation.BuildDelaunay` for the cotan Laplacian. All cotan weights non-negative also on the
  boundary; on the plane of 169-degree triangles the mean distance error drops from 0.06 to 0.02 edge
  lengths (max 0.8 to 0.1; `benchmarks/IntrinsicBenchmark`). Common subdivision by tracing (`ExtractCommonSubdivision`, area-checked, reports
  `Inconsistent` instead of wrong faces).
  - [ ] Integer (normal) coordinates (Gillespie, Sharp, Crane 2021) for a combinatorially exact common
    subdivision; signposts with the area checks suffice for the current operations
