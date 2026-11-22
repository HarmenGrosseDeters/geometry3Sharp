# Parameterization and geodesics (Phase 3)

Part of the [roadmap](../ROADMAP.md).

- **Scope:** parameterization (LSCM, ARAP, SLIM, BFF, cones, seams, charts, UV packing), intrinsic triangulations
  and refinement, the vector heat method and log maps, geodesic paths, foldover-free maps.
- **Status (2026-11-17):** the Phase 3 operations are done; the items under Open remain. Foldover-free maps (Phase 5
  #7) merged in #374, round-off in #754; the affine heat method stopped at its gate (#371); the AffineLocal follow-up
  closed (#538); geodesic Bézier curves merged (#540). The geodesics study (#784, #798, #807) is merged; the exact
  geodesic distance as an internal test oracle (`ExactMeshGeodesicDistance`) is merged in #869 (2026-11-15), its A6 merge rule
  fixed in PR #1062 (issue #1058, 2026-11-17); LSCM near-collapse is fixed in PR #1054 (issue #1049, 2026-11-17).
- **Related:** [design/AFFINE_LOGMAP_GATE.md](../design/AFFINE_LOGMAP_GATE.md),
  [design/FOLDOVER_FREE_MAPS.md](../design/FOLDOVER_FREE_MAPS.md),
  [benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md](../../benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md);
  Phase 5 items in [research.md](research.md#phase-5-candidates-from-recent-research).

### Open

- **AffineLocal log map, follow-up measurement: closed 2026-11-10** (#538, docs only): gate 2 (estimated normals, far
  field up to 0.9 pi) does not win clearly (G1 3 of 21 cells, G2/G3 fail near the cut locus); no API. AffineLocal is
  clearly better only in the mid field (12-24 edge lengths); reopen only with a brief naming such a workload and its own
  pre-registered gate. Details in [design/AFFINE_LOGMAP_GATE.md](../design/AFFINE_LOGMAP_GATE.md).
- **Exact geodesic paths (#1, `PolyhedralGeodesicDistance.TracePath`, 2026-11-19):** the globally shortest path from the nearest source to a vertex or to a point in a triangle, as a polyline on the surface, by backtracing through the unfolded window origins; its length equals the distance of the run (measured up to 6e-15 relative on 317 x 317, tolerance stated 1e-14). Unlike `MeshGeodesicPath` (FlipOut, locally shortest) it is the global minimum. Design and measurements: [design/GEODESICS.md](../design/GEODESICS.md) A.8.
- **Exact geodesic distance (option A of [design/GEODESICS.md](../design/GEODESICS.md))**: a certified [Lower, Upper]
  enclosure of the polyhedral distance by window propagation in interval arithmetic. Role (coordinator, 2026-11-13): a
  **test oracle for meshes up to about 10^4 vertices**; the 10^5 targets belong to a later public class. Study and gates
  2 and 3 merged (#784, #798, #807; exact on every test mesh; the 10^5 terrain 18.8 s and the aligned ridge 81 x 81 16 s in
  the interval version of the probe); A7 reviewed, A8 withdrawn, A9's observations (b)-(h) not reviewed. Option C (a
  Lipschitz lower bound for the heat field) dropped.
  **#869 (opened 2026-11-13, merged 2026-11-15):** `ExactMeshGeodesicDistance`, internal and serial, with A1-A6, subsumption,
  terminal windows and flat vertices as pseudo-sources; no A7, no A9. As stated in the PR: terrain 101² (10201 vertices)
  in 4.0 s, but exactly flat or aligned inputs hit `WindowLimit` at about 10^3 vertices (plane 101², ridge 41²), short of
  the 10^4 target; Upper up to 1.3 % loose on flat regions. Open: A7 (continuation windows) with duplicate control, since
  it duplicates windows on flat regions (GEODESICS.md 6b); the flat-tie redesign; the heat-method tests against the
  oracle.
  **#1058 (2026-11-17):** the 1.3 % was the plane from a corner source; measured, Upper was up to 8.2 % loose on flat and
  nearly flat input (planes, domes and saddles with k <= 1e-5) because A6 merging dropped one of two inner intervals.
  A6 now merges only when at most one is nonempty: width at most about 2e-8 on the whole procedural family, 2-7 times
  fewer windows on flat input, the ridge 41² completes in 5.7 s (GEODESICS.md 6c, `benchmarks/GeodesicOracleBenchmark`).
- **Integer (normal) coordinates** (Gillespie, Sharp, Crane 2021) for a combinatorially exact common subdivision;
  signposts with the area checks suffice for the current operations. Also a research question
  ([research.md](research.md#open-research-questions)).
- **Geodesic Bézier curves** (Phase 5 #1): done (paths, waypoints and loops; Bézier curves merged in #540).
- **Foldover-free maps** (Phase 5 #7, #374; round-off merged in #754, 2026-11-13; done, limits documented): the paper's energy-decrease stop is in (iterations
  115 / 132 / 169 / 132 -> 28 / 41 / 54 / 16, quality within 0.1 % on all four fixtures, time about 2-6x less). The SLIM
  gap is the fixed boundary, not the solver (with SLIM's boundary: disc 1.000, three peaks 1.083 x SLIM); documented.
  The torus fallback is a proven limit: the fixed cone-cut boundary polygon crosses itself 4 times, so no map with it
  is injective; no fix exists inside fixed-boundary untangling. Details in
  [FOLDOVER_FREE.md](../../benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md#round-off-2026-11-12).
- **P1 collapse** (issue-tracker follow-up): closed 2026-11-12 as a documented limitation
  ([ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md)); the real fix stays research: virtual fan vertices (spectral
  conformal was measured 2026-11-17 and is no fix: on nearly symmetric annuli its eigenvalue pair is nearly double and
  the map folds, [GEOGRAM_MEASUREMENTS_2026-11.md](../research/GEOGRAM_MEASUREMENTS_2026-11.md); it is built as the opt-in `SpectralConformalParameterization` for disc charts, merged in #1057 on 2026-11-17, LSCM's default unchanged, P3); foldover-free maps are a lead ([platform.md](platform.md#quality-bar-when-its-done)).

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

### Open for the future (2026-11-15)

List as of 2026-11-17.

1. `ExactMeshGeodesicDistance` as a certified test oracle (#869 merged 2026-11-15, #1062 2026-11-17): reach on flat / aligned inputs below 10^4 vertices (the ridge 41² completes in 5.7 s since #1058); the heat-method tests against the oracle (plan #1238).
2. A7' (exactly flat vertices emit nothing; GEODESICS.md 6d, A.6; reviewed, merged #1147 2026-11-19) is done in the oracle: flat and aligned inputs of 10^4 vertices in 3-6 s. A9 is not built (0 gain on flat input, at most 1.2-1.8 times elsewhere, 6d). Next: the fragmentation lever (merging with a propagated window, 6d limit 2; plan #1238), then a public class for 10^5 vertices (done: `PolyhedralGeodesicDistance`, #1185, 2026-11-19; its saddle tail is #1186).
3. Integer (normal) coordinates for an exact common subdivision (research; no issue; plan #1284).
4. P1 collapse: virtual fan vertices (research; documented limitation; plan #1285). Spectral conformal measured 2026-11-17: no fix for P1 (folds on nearly symmetric annuli), but it removes the P3 pin asymmetry (to 1e-13): owner 2026-11-17: an opt-in for disc charts; built in #1057 (2026-11-17) with the per-chart A/B against LSCM on the atlas corpus in `benchmarks/UVAtlasBenchmark/README.md`, default unchanged ([GEOGRAM_MEASUREMENTS_2026-11.md](../research/GEOGRAM_MEASUREMENTS_2026-11.md)).
5. AffineLocal log map: reopen only with a brief naming a mid-field workload (closed, #538).
