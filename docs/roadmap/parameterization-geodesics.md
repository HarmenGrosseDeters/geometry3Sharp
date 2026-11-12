# Parameterization and geodesics (Phase 3)

Part of the [roadmap](../ROADMAP.md).

- **Scope:** parameterization (LSCM, ARAP, SLIM, BFF, cones, seams, charts, UV packing), intrinsic triangulations
  and refinement, the vector heat method and log maps, geodesic paths, foldover-free maps.
- **Status (2026-11-12):** the Phase 3 operations are done; the items under Open remain. Foldover-free maps (Phase 5
  #7) merged in #374; the affine heat method stopped at its gate (#371); the AffineLocal follow-up closed (#538);
  geodesic Bézier curves merged (#540).
- **Related:** [design/AFFINE_LOGMAP_GATE.md](../design/AFFINE_LOGMAP_GATE.md),
  [design/FOLDOVER_FREE_MAPS.md](../design/FOLDOVER_FREE_MAPS.md),
  [benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md](../../benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md);
  Phase 5 items in [research.md](research.md#phase-5-candidates-from-recent-research).

### Open

- **AffineLocal log map, follow-up measurement: closed 2026-11-10** (#538, docs only): gate 2 (estimated normals, far
  field up to 0.9 pi) does not win clearly (G1 3 of 21 cells, G2/G3 fail near the cut locus); no API. AffineLocal is
  clearly better only in the mid field (12-24 edge lengths); reopen only with a brief naming such a workload and its own
  pre-registered gate. Details in [design/AFFINE_LOGMAP_GATE.md](../design/AFFINE_LOGMAP_GATE.md).
- **Integer (normal) coordinates** (Gillespie, Sharp, Crane 2021) for a combinatorially exact common subdivision;
  signposts with the area checks suffice for the current operations. Also a research question
  ([research.md](research.md#open-research-questions)).
- **Geodesic Bézier curves** (Phase 5 #1): paths, waypoints and loops are done; Bézier curves merged in #540 (done).
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
  - [x] **Near-field log map from affine heat: gated, not shipped (2026-11-08).** The near-field gate (AffineLocal
    against Legacy and `MeshLocalParam`, both modes, 7 procedural fixtures including noisy and anisotropic meshes, radii
    of 3, 6 and 12 edge lengths, 21 cells; criteria fixed in the design note before the run) did **not** show a clear
    win: AffineLocal is 10 to 100 times more accurate than Legacy and exact on the plane, but `MeshLocalParam` is as good
    or better on the sphere up to 6 edge lengths (AffineLocal's mean error 1.0 to 1.5 times its), so G1 (9 of 21 cells,
    16 needed) and G2 failed. No API. Affine wins at 12 edge lengths and on the cylinder, and is 1.5 to 2.5 times faster
    than Legacy. Result, tables and the reopen condition (a patch of 12 or more edge lengths, or poor normals for
    `MeshLocalParam`): [AFFINE_LOGMAP_GATE.md](../design/AFFINE_LOGMAP_GATE.md). Gate code and prototype, for reuse:
    branch `agents/affine-local-logmap-gate` at `ab9ea81e` (not for merge).
  - [x] **The current log map is not exact on a plane: not a defect (2026-11-08).** The Vector Heat paper reports no
    numeric planar log-map error (only O(h) convergence of the transported vector, Fig. 13, and t = h²); ours is
    first order with t = h² (0.11 to 0.31 mean edge lengths), so there is nothing to be clearly worse than. Details in
    the design note.
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
