# Terrain phase 1b: plan

Status: plan of the 1b PR, with what was built (below). It follows [TERRAIN.md](TERRAIN.md) 5.5 and the 1b row of section 11, on top of 1a
([TERRAIN_1A_PLAN.md](TERRAIN_1A_PLAN.md)). Section numbers refer to TERRAIN.md.

## Scope

1. **Terrain input.** `TerrainSlopeDesign.Terrain` (a TIN, height field). With a terrain, `HeightLimit` may be left NaN:
   cut then builds S̃ up to the terrain's highest vertex (fill: down to its lowest), so S̃ exists wherever it can lie
   below (above) the terrain. A given `HeightLimit` is used as in 1a; a region that reaches it ends there
   (`HeightLimit`, see 4).
2. **Topological overlay** of S̃ (the realized `SlopeSurface`, stored doubles) with the terrain, as 5.5 says:
   - overlay vertices by identity: an S̃ vertex (in the terrain triangle that contains it), a terrain vertex (in the S̃
     triangle that contains it), the crossing of an S̃ edge with a terrain edge;
   - decisions by `ExactPredicates.Orient2d` with Simulation of Simplicity: the terrain is shifted by (ε, ε²), and an
     `Orient2d` of two points of one surface and one of the other is decided, when it is 0, by sign comparisons of
     coordinate differences (no arithmetic). So no vertex lies on an edge of the other surface, no vertices coincide,
     and collinear edges do not cross;
   - an overlay face is the intersection of an S̃ triangle and a terrain triangle (convex); its boundary consists of the
     part of each edge of one inside the other.
3. **Exact sign of D = S̃ − T** (cut; fill: T − S̃) at overlay vertices, with the formulas of 5.5 (`Orient3d` times
   `Orient2d`), with no tolerance. D = 0 counts as "no work" (the ε_z shift of 5.5: up for cut, down for fill).
4. **Catch line and region.**
   - A face has 0 or 2 boundary edges where the sign changes (D is linear on it); more is an internal error. A catch
     point is identified by its overlay edge, so both faces next to it name the same point.
   - The region is the set of faces with a negative vertex that is connected to the toe: faces are joined through an
     overlay edge with a negative end, and the seeds are the faces on a toe edge with a negative end. Components that are
     not connected to the toe are not excavated (the hill behind a valley).
   - Catch lines are the chains of catch points of region faces, closed, or ending at the toe (`Toe`), at a lateral limit
     (`LateralLimit`), at the terrain's boundary (`FootprintBoundary`) or at the height limit of a given `HeightLimit`
     (`HeightLimit`). The last two mean the slope does not daylight there: status `NoDaylight`, with the results kept and
     `IsComplete = false`.
   - Positions: on the overlay edge from P to Q with the D values computed in doubles, their signs forced to the exact
     ones, t = D_P / (D_P − D_Q) clamped to [0, 1]; z on S̃. The horizontal accuracy (|∇D|, δ_xy) is 1c.
5. **Composite (optional, `BuildComposite`).** The terrain with the region replaced by S̃:
   - the slope part (`SlopeRegion`, group id = piece) is S̃ clipped to the region, the terrain part is the terrain minus
     the region. Each partial triangle is triangulated (exact CDT, `ExactTriangulation2`) with the region boundary in it
     as constraints, and its inside found by parity from outside;
   - along the catch line both parts share their vertices (z on S̃, the terrain absorbs the difference, 5.5);
   - **the slope joins the toe at the lowered toe** (z − λ, `UsedToeLowering`; plan of 1a, departure 1). Inside the toe
     and beyond the lateral limits the composite stays terrain, where the caller puts the design; along the toe and the
     side lines it has a step (boundary edges on both sides, no shared vertices). Terrain vertices on such a line are
     moved off the slope side by ulps if rounding put them there, so the parts do not overlap in XY;
   - every triangle of `SlopeRegion` passes the exact check against its piece (O1) before `Compute` returns true, in
     the same loop as 1a (a support failure raises λ, a slope failure δ).

## As built: realization details and departures

1. **Catch points in the composite.** Their XY is the interpolated zero of D on the overlay edge; their z is rounded
   exactly from the planes of their slope triangle (on a slope edge the lower of its two planes, for fill the higher, as
   1a takes the lowest incident plane). Interpolated heights were 1e-9 m off the plane at UTM, which the support check
   of thin clipped triangles amplified to excesses of mm.
2. **Snapping (5.5).** A catch point nearer than `MinFeatureSize` to a vertex of its slope edge or triangle snaps to it;
   catch points nearer than that to each other share one position if both stay on their slope planes (same slope edge,
   or one inside a slope triangle and the other inside it or on one of its edges). Where `MinFeatureSize` is larger than
   the features (the frame "rotated, scale 1e-3, utm": 1.5 cm in a 10 cm scene) a move can cross other parts of the
   boundary; the composite is checked exactly (each partial triangle's boundary is simple, the parity is consistent, and
   `XYOverlaps` of the whole composite is 0), and the catch points involved keep their own positions in a rebuild of the
   composite. A failure without a moved point is `Failed`, never a returned overlap.
3. **Steps.** Along the toe and the lateral limits the slope ends (at the lowered toe) and the terrain has its own
   vertices at the crossings of the slope's boundary with terrain edges; where rounding put one on the slope side it is
   moved by ulps off it. So the composite has no overlaps, and a strip of a few ulps there is not covered (the tests allow
   8 ulps times the toe and boundary length in the area check).
4. **SoS-degenerate crossings are snapped to the vertex.** A crossing whose terrain (slope) vertex lies exactly on the
   slope (terrain) edge's line is that vertex, exactly; computing it rounded put two realized points within ulps of
   each other on the toe. Points that the realization puts exactly on a constraint split it.
5. **A 1a defect found by the 1b stress test, fixed here.** On a regular 10-gon the line where two strips are equal passes
   within ulps of the opposite toe vertex; the map then has a cluster of vertices 1e-14 m apart that the edge collapse
   could not all remove (link condition), and a degenerate triangle failed the check at every margin (`SlopeViolation`).
   The realization now merges positions within 64 ulps (z the lowest plane of all of them; the check decides). The
   support excess that sets the measured toe lowering is now exact: in doubles it was 0 at the origin, so λ was never
   raised and the margin δ was raised instead, up to its cap. A margin raise keeps the lowering measured in the same
   build. Test: `RegularPolygon_StripCreaseThroughTheOppositeVertex` (fails without the merge).
6. **λ.** 1b joins at the lowered toe (`UsedToeLowering`, reported per run). Measured in the stress test (24 random
   wavy terrains, `RandomTerrains_CatchLinesAndComposite`): 8e-13 to 9e-11 m at the origin, 3e-9 to 3.5e-5 m at UTM; in
   the benchmark 2.2e-11 m (r 20 m, local), 7.5e-9 m (r 20 m, UTM), 1.6e-6 m (r 200 m, UTM). All within the bound
   reserved in the tolerance budget (1a).

Not in 1b: mixed cut and fill (`Direction.Auto`, the clamp of 5.5) and `ToeOvertopped`; volumes; `StatusKind`; the
catch-line accuracy (`IllConditionedCatchLine`, `TangentialContact`, `RequiredCatchLineAccuracy`); the band-only
overlay of the efficiency bullet of 5.5; km-scale benchmarks. These are 1c (or later, as 11 orders them).

## Look before you build

- Library: `TerrainVolumeDifference` clips triangle pairs in doubles (Sutherland–Hodgman), so it is not symbolic; its
  `XYOverlaps` is reused for the height-field check of the terrain. `ExactPredicates.Orient2d` / `Orient3d` (exact on
  all doubles) decide everything; `ExactTriangulation2` (exact CDT, used by 1a) triangulates the partial triangles;
  `TriangleQuadtree2d` (public API) finds the terrain triangles near an S̃ triangle.
- Literature: Simulation of Simplicity (Edelsbrunner & Mücke 1990); the overlay of two subdivisions and the
  combinatorial convex intersection (O'Rourke et al. 1982; de Berg et al., Computational Geometry, ch. 2);
  Shewchuk 1997 for the predicates. No new error bound: the decisions are exact predicates, the positions are
  approximations whose accuracy is 1c's report.

## Tests

- analytic: straight toe on flat terrain (catch line at H/s, closed for a closed toe), tilted planes; the catch line
  against the brute-force zero of S_ref − T on samples;
- exact signs: the D formulas against rationals on random and on degenerate configurations (terrain vertices on S̃
  vertices and edges, grid-aligned terrains, toe on terrain vertices); a mutation that uses double signs must fail
  the grazing tests;
- topology (O3): catch lines simple and closed or ending with a reason, every face 0 or 2 crossings, the composite a
  height field (`XYOverlaps` = 0), no XY-inverted triangles, boundary edges only at the footprint boundary, the toe
  and side lines; O1 on `SlopeRegion` at UTM;
- region: the hill behind a valley is not excavated; no daylight (terrain rising faster than s) gives `NoDaylight`
  with the results kept;
- invariance over `Similarity.All()` (z rotations), reuse, determinism, cancel, as in 1a.

## Reviews and mutation testing

- **Independent code review** (an agent with the diff only): no correctness bug in the face construction, the SoS
  entry and exit, the chaining or the region. Fixed from it: the overlay and the composite now start from scratch on a
  second run (test `Overlay_IsReusable`); a composite that cannot be built gives `CompositeFailed` and keeps
  `SlopeSurface` and `CatchLines`, instead of `Failed` with no result; a test for a given `HeightLimit` that the region
  reaches (`GivenHeightLimit_TheRegionEndsThere`, which kills the mutants of the `HeightLimit` end reason); catch lines
  and end reasons compared in the reuse and invariance tests; catch-point pairs from a grid (a line along y was
  quadratic). Left for 1c: the per-triangle constraint checks are O(segments × points) in one slope triangle (a straight
  open toe over a 100k terrain: 12.3k catch points, 5.4 s in the composite), and `HeightLimit` from the global terrain
  maximum.
- **Exact-arithmetic review** (SoS, the D signs, the snapped crossings, the exact plane heights, the merge of 1a), with
  brute force over rationals with an explicit ε: `OrientAAB` / `OrientBBA`, `SignAV` / `SignBV` / `SignX` (also for
  crossings that exist only by the perturbation), the snapped crossing and `ExactDyadic` heights all confirmed; on
  36.7k overlay faces every face had 0 or 2 sign changes. Fixed from it: a position merged into another keeps its
  barriers (else a toe edge could be classified as the height limit); the lowering by merged vertices (at most
  2 · 64 ulps · sMax) is reserved in the tolerance budget; a realized slope surface that folds over (an interior edge run
  in the same direction by both triangles) fails instead of being used.
- **No new error bound.** The decisions are exact predicates; the positions of catch points are approximations whose
  accuracy is 1c's report; the merge share above is the only addition to the budget.
- **Stryker** (`tests/mutation/terrain-overlay-stryker.json`, `TerrainSlopeOverlay.cs`, the catch-line tests without the
  stress test, local, 36 min): **97.06 %** (385 killed, 110 timeout, 2 survived, 13 no coverage). Survivors: the two
  `>`→`>=` of the SoS tie break on x (lines 120 and 130), equivalent, as they differ only for a zero-length edge, which
  no surface has. No coverage: the messages and returns of internal-error paths (a face with 1 or 3 sign changes, an
  edge with three faces, a chain that does not close: none occur, the review's brute force confirms) and the sign
  forcing of a computed D whose double sign is wrong. Proposed break: 90.
- **Re-review of the post-review fixes** (a fresh agent, given the diffs of b04def85 and c81a7c80 and the budget
  derivation). Confirmed: the exact `plane_excess`, the barrier union, the fold check (correct; it catches combinatorial
  folds, `Orient2d` the geometric ones). Found, and fixed:
  - the share 2 · 64 ulps · sMax bounded only the merged vertices. The triangles of a moved vertex leave their cells, so
    the surface can lie up to 4 sMax (r + e_xy) below S̃ and up to 2 sMax (r + e_xy) above it (r = 64 e_xy; analytic
    ridge-and-valley constructions). Now the budget reserves 4 sMax (r + e_xy), and a build that merges is repeated
    with a toe lowering of at least 4 e_z + 2 sMax (r + e_xy) (the merge only runs if that fits under λ's bound);
    a merge that makes the mesh non-manifold falls back to the realization without it;
  - the λ loop could repeat a build at λ's bound without trying a larger margin: λ is raised only while below it.
  Test: the lowering of the regular 10-gon run (which merges) is at least 2 sMax · 65 ulps. In that run the measured
  λ is larger anyway, so removing the merge lowering does not make it fail; the bound rests on the derivation.
