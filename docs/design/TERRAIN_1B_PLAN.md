# Terrain phase 1b: plan

Status: plan of the 1b PR. It follows [TERRAIN.md](TERRAIN.md) 5.5 and the 1b row of section 11, on top of 1a
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
