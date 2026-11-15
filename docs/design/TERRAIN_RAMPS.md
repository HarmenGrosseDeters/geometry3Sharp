# Design note: ramps as design geometry (terrain item 2)

Status: **R1 implemented** (branch `claude/terrain-ramps-r1`, see "R1 as built"); owner decision 2026-11-13: yes to all three proposals (the path is the inner edge, R1 is cut only with access ramps ending at daylight, one constant grade per ramp).

## Goal

A haul road or access ramp into a pit: a path, a width and a grade, cut into the slope of a `TerrainSlopeDesign`.
The result is still a design that is certified per triangle: the slope faces are never steeper than their spec, and
the road is never steeper than its grade. The road is never narrower than its width.

## How others do it (prior-art line)

- Vendor practice builds strings bench by bench: toe, then the ramp, then the crest. Datamine Studio OP has
  "toe-ramp-crest" and Ramp Layout (a gradient, berm width and direction per road segment). Maptek Vulcan's
  Automated Pit Designer and its "Insert road to pit / dump" add a road of given width, grade and switchbacks to an
  existing wall and modify the wall to take it. These are approaches only, from the vendor documentation.
- Hustrulid, Kuchta and Martin, *Open Pit Mine Planning and Design* (2013), ramp chapter: the ramp pushes the toe of
  the walls above it out by the road width, and the inter-ramp angle is measured between ramps.
- Ceron (MassMin 2020, ACG) does assisted ramp layout by mathematical programming on a block model (layout
  optimisation, not surface construction). Patent US 9589076 (area-based pit designer) has defaults of a 10 % grade
  and a road 3.5 truck widths wide.
- None of these certifies slopes per triangle. They build strings and loft surfaces between them, which section 5.9
  rejects as having no slope guarantee.

## Model (R1, a single face, as today)

A ramp is a graded berm along a path. It is section 10's iteration ("face, crest level line, berm, next toe") with an
inclined level instead of a horizontal one:

- **Inputs** (`SlopeRamp`, additive): `Toe` (the toe the ramp leaves), `Path` (an XY polyline, the road's inner
  edge on the pit side, starting on that toe), `StartHeight` (default: the toe's height at the start), `Grade` (a
  `SlopeSpec`: Percent or Rise, so it can be checked exactly), `Width`, and `Side` (the side of the path the road
  lies on, given and never inferred, like `SlopeToe.Side`).
- **Road surface Z_R:** z = StartHeight + g · arc length along the path, constant across the road. On each straight
  path segment the road is a plane of gradient g. At a bend the wedge between the segments' rectangles is flat at
  the vertex height (gradient 0).
- **Lower wall S_lo:** the slope of the original toe, as today.
- **Upper wall S_up:** the R1 envelope of the road's outer edge, an open toe with the road's heights (strips, end
  cones, `EndModes` as for open toes).
- **Surface:** F = min(S_lo, max(Z_R, S_up)) near the ramp, then the clamp with the terrain as today (cut:
  min(T, F)). The crest of the lower wall, the road's real inner edge, is the level curve {S_lo = Z_R}. It is not an
  input: the overlay finds it with exact signs, the same machinery as the catch line (D = S_lo − Z_R).
- **Building blocks** (library search):
  - `SlopeTerrainOverlay` and `SlopeComposite` for the min / max of two TINs: they already compute the clamp
    max(S_fill, min(T, S_cut)) with exact signs.
  - `TerrainOffset` for the outer edge.
  - `TerrainSafetyLines.MinLineDistance` for the width oracle, as planned for berms in section 10.
  - `Slope.IsNotSteeper` for both checks.

## Interaction with the exact never-steeper oracle

- Every triangle of F is a piece of S_lo, of S_up, of Z_R or of T:
  - slope triangles get today's check (`IsNotSteeper` plus the support at their toe piece's ends), the clipped ones
    included (O1);
  - road triangles get `Slope.IsNotSteeper(tri, Grade)`, exact for Percent and Rise; the ramp's pieces go into
    `Pieces`, with a kind;
  - terrain triangles are the terrain's own planes.
- New oracle: the **road width** is never below `Width`, by the exact minimum distance between the crest
  {S_lo = Z_R} and the outer edge.
- `gs.Slope.IsNotSteeper` is unchanged: no signature or semantics change for `ContourLoft`.
- Not in it: inter-ramp angles. They stay parked with berms (not modelled in practice).

## Statuses (additive enum values, with `StatusKind`)

- `InvalidInput`: a path that does not start on its toe, self-intersects, or has a segment below MinFeatureSize; a
  non-positive width; an invalid grade or a Degrees grade (not exactly checkable).
- `ToeSteeperThanSlope`, existing: the grade of the outer edge (g, larger along the inside of a bend) is at least the
  upper wall's slope.
- `RampNotOnWall`, new, GeometricallyInfeasible: the road needs fill. Z_R is above S_lo across the full width over a
  stretch (a hanging road), or the crest comes closer than `Width` to the outer edge. Reported with the path range.
- `ToeUndercut`, existing: cones of other toe parts, eg the opposite wall of a narrow pit, undercut the outer edge.
- `NoDaylight`, existing, also at the top end of the road. An access ramp ends where it daylights: the road meets
  the terrain on the catch line, and above the terrain it would be fill. Cut only in R1.

## Scope and phases

- **R1** (one PR):
  - one or more ramps on a cut design;
  - straight-segment paths of constant grade, no switchbacks;
  - tests: oracle, width, statuses;
  - `Similarity.All()` invariance in a `LocalFrame`;
  - reuse;
  - the campaign extended by a ramp generator;
  - a benchmark row.
- **R2** (later, on a use case):
  - switchbacks (a flat landing and a reversal);
  - grade changes along the path;
  - dump ramps (fill), `Auto`;
  - cross-fall.
- Tier L: the design note to the owner first, then a bounds review of the new overlay pairing (S_lo against Z_R
  uses the same exact-sign rules, so no new error bound is expected; the road's z by arc length is rounded once per
  vertex and enters the budget like the toe heights).
- Estimate: about the size of terrain 1b (#285).

## Owner decisions (2026-11-13)

1. The path is the road's **inner edge** (crest side), not its centreline: the width is guaranteed outwards.
2. R1 is a **pure cut ramp up to the daylight line**: an access ramp ends where the road meets the terrain on the catch
   line; no fill embankments in R1.
3. **One constant grade per ramp.**

## Questions for the owner (answered above)

1. Is the path the road's inner edge (crest side, as proposed: the width is then guaranteed outward), or its
   centreline?
2. Is R1 cut only, with access ramps ending at daylight, or are fill embankments at the top of an access ramp
   needed in R1?
3. Constant grade per ramp (R1), or a grade per path segment from the start?

## R1 implementation plan (accepted by the coordinator 2026-11-13; hand-over)

Branch `claude/terrain-ramps-r1` from master. The PR is opened only after #752, #757 and #761 are merged, with master
merged in (not stacked). Tier L.

1. **API (additive).**
   - `SlopeRamp { Toe; Path; StartHeight; Grade; Width; Side }`: `Path` is the XY inner edge and starts on its toe;
     `StartHeight` defaults to the toe's height there; `Grade` is Percent or Rise (exactly checkable).
   - `TerrainSlopeDesign.Ramps`.
   - A new status value `RampNotOnWall` (GeometricallyInfeasible).
   - With `Direction` Fill or Auto a ramp is `NotImplemented` in R1.
2. **Road surface Z_R.** z = StartHeight + g · arc length along the inner edge, constant across the road: one plane per
   straight segment and a flat wedge at each bend vertex. The outer edge is the path offset by `Width` (`TerrainOffset`
   or a direct offset of the polyline). A self-intersecting path or outer edge is `InvalidInput`, decided with exact
   orientation tests.
3. **Upper wall S_up.** The R1 envelope of the outer edge as an open toe with the road's heights: the existing strips,
   end cones and lateral limits (`EndModes`). If the grade of the outer edge reaches the slope, the existing
   `ToeSteeperThanSlope` reports it.
4. **Combination.** F = min(S_lo, max(Z_R, S_up)), then the terrain clamp as today.
   - Before building a TIN min/max step, check what `SlopeTerrainOverlay` and `SlopeComposite` already give: they
     overlay two height-field TINs with exact signs of D = A − B at the overlay vertices and build
     max(S_fill, min(T, S_cut)) with the region, snap and certificate logic. A Z_R-against-S_up overlay and an
     S_lo-against-result overlay may reuse them with the roles of slope and terrain swapped. Record the reuse in the PR.
   - The crest {S_lo = Z_R} comes out of the overlay exactly, as the catch line does.
5. **Oracles on every success path.**
   - Each slope triangle passes `IsNotSteeper` and the support check, as today, clipped parts included.
   - Each road triangle passes `IsNotSteeper(Grade)`.
   - The road is at least `Width` wide, by the exact minimum distance between crest and outer edge
     (`TerrainSafetyLines.MinLineDistance`).
   - The composite is valid: counter-clockwise, no XY overlaps, finite.
6. **Tests.**
   - Design cases: a straight ramp in a square pit; a ramp around a convex corner (the bend wedges); a ramp ending at
     daylight.
   - Statuses: `RampNotOnWall` (the road above the wall), `ToeSteeperThanSlope`, `InvalidInput`.
   - Invariance over `Similarity.All()` in a `LocalFrame`, 2^k bit for bit.
   - Reuse: `Compute()` twice.
   - The campaign extended by random ramps on the T11 scenes.
   - No parallel path, so no determinism test. Each new test is mutation-checked.
7. **Benchmark.** One `TerrainBenchmark` row (a pit with a ramp), with the numbers in the PR.
8. **Reviews.** An independent review. The bounds review of the min/max step is routed by the coordinator. No new
   error bound is expected: the signs are exact, and the road's z is rounded once per vertex and enters the budget like
   the toe heights.

## R1 as built (branch `claude/terrain-ramps-r1`)

Code: `mesh_ops/TerrainSlopeRamps.cs` (`SlopeRamp`, the road polygons, the width oracle), `SlopeMap.InsertPolygonPiece`,
hooks in `TerrainSlopeDesign` (pieces, checks, statuses, the tolerance budget) and `TerrainSlopeLocalFrame`. Tests:
`TerrainSlopeRampTests`, `TerrainSlopeCampaignTests.RampCampaign`. Benchmark: `TerrainBenchmark` `slope ... ramp=1`.

### Deviation from steps 3 and 4: the road as pieces of the envelope (reuse check)

The reuse check of step 4 found that `SlopeMap` already builds F = min over pieces of F_k, where F_k is the maximum of
a piece's planes (strips and fans, a convex function), with exact labels. A road polygon with the road plane Z_i is
such a function too: F_road = max(Z_i, the outward strip planes of its edges, the cone tangent planes at its corners) is
Z_i on the polygon and the slope cone of its boundary outside. Every wall plane is checked exactly at construction
(not steeper than the wall's slope, on or below every corner of the polygon), which makes F_road = Z_i on the polygon.
So the ramp's pieces go into the same envelope as the toe's:

- min(S_lo, road with its walls) comes out of the existing exact map: the crest {S_lo = Z_R} and the upper wall S_up
  (the cone of the outer edge) are lines of the map, with no new TIN-vs-TIN pairing.
- `SlopeTerrainOverlay` and `SlopeComposite` are reused unchanged for the clamp with the terrain, as for any design;
  realization, toe lowering λ, the δ/λ loop, catch lines, volumes, `LocalFrame` and the 2^k invariance stay as they are.
- The start, end and outer edges all get cones, so there is no lateral-limit step at the ramp's ends (with S_up as an
  open toe with normal limits there would be a vertical step of about s W at the start).
- Each segment's polygon reaches inwards from the path towards the toe (to the distance of its ends from the toes), so
  a crest that lies inwards of the path widens the road (the plan's semantics), without a lip on the pit side.

### Bends (R1)

- The turn at a path vertex is decided exactly (`Orient2d` of the three path points): a collinear point is no bend.
- A bend away from the road (towards the pit: a convex wall) gets a flat kite at the vertex height between the two
  segments' polygons, out to the miter point; a bend so slight that a kite edge is below the smallest realizable
  feature is `InvalidInput` (remove the nearly collinear point).
- The end face of a segment (rising at s from its end line) must not cut into the next segment's road (rising at g):
  for any point of the next strip this holds when s cos(turn) >= g; 0.9 covers the facets of the cone. Sharper turns
  are `NotImplemented` (a switchback is R2).
- A bend towards the road (a concave wall) is `NotImplemented`: with a road that is constant across, the two segments'
  planes differ along any line across the road through the vertex, so either a wall cuts into the road or the road is
  lowered by up to g W sin(turn). It needs a road that is not constant across (R2).

### Margins and the tolerance budget

- The road is built at the grade g − RoadMargin, RoadMargin = δ sMin / 2: a road vertex is off its plane by the rounding
  of its height and of its XY times g (< sMin), so a triangle with altitudes of at least MinFeatureSize is steeper than
  its plane by at most about δ sMin / 4. The exact check decides; a failure raises δ like any slope failure.
- New budget term (**bound review**, two rounds), `ramp_share`, subtracted from the facet share and part of the snap
  certificate's budget: the largest over the ramps of the sum (each product and the sum rounded up) of
  - the road below its specified heights: the built grade gb = g − RoadMargin is lower by g − gb (exact: Sterbenz, as
    gb > g / 2, which is checked first, before the budget: a too small grade is reported as such) over the path's length
    L (summed rounded up), plus (n + 1) (ulp(2 max|z|) + g ulp(L)) for the roundings of the vertex heights along the path
    (max|z| includes the toe's heights at the start, which the start height is interpolated from) and of the segment
    lengths;
  - the wall above the outer edge, built at Width (1 + 2δ): it stands 2δ Width further back, at a miter corner of a bend
    2δ Width / cos(turn / 2) (the largest over the ramp's outer corners, rounded up), so at most the wall's slope s times
    that lower (found by the independent reviews: without it a wide road with a large margin was certified up to
    2 x Tolerance below its specified wall, and without the miter factor up to 39 % of the term at a sharp bend was not
    counted);
  - the XY rounding of the polygon's corners: an outer corner up to e_xy further out (2 s e_xy), and the road plane
    through a rounded across point tilted by up to e_xy / Wb, which moves its height at the inner corners (E away) by
    g e_xy E / Wb.
  When only the ramp's share does not fit: `InvalidInput`, "the ramp is too long or too wide for Tolerance", with both
  parts. `UsedRoadMargin` reports the margin used.
- **The reach D0 counts the lowest built height** (#861, 2026-11-14). The road and the wall above it are built up to the
  share below the specified heights, so their cones reach up to share / (sMin (1 − δ)) further than from the specified
  toe. D0, which the toe lowering's bound, the δ term, the facets and the domain all scale with, is
  (H − zMin + share) / (sMin (1 − δ)). Before, it used the specified zMin, and only the 1.1 of those terms covered the
  difference: up to L of about 0.2 D0 / δ, 200 D0 at δ = 1e-3 but 20 D0 at δ = 0.01. A ramp of 250 m at 2 % with δ = 0.01
  reaches 13 % further; its budget at the built reach was 1.748 against a Tolerance of 1.7
  (`TerrainSlopeRampReachTests`).
  - The share depends on e_xy, which depends on D0, so D0 is a fixed point. D0 counts a share at least the one computed
    at its own e_xy. It settles in one or two rounds, because e_xy changes only where maxXY crosses a power of 2.
  - `reach_and_resolution` computes it for the build and for the naming of a Tolerance under `MaxFanDirections` (#808).
    The naming keeps the build's own diagnostics of the share (`RampShare*`, `RampGradeTooSmall`).
  - Designs without ramps are unchanged bit for bit (share 0).
- The outer edge is built at Width (1 + 2δ); the width oracle's nominal polygons run from Width δ / 2 to Width
  (1 + 3δ / 2) and end Width δ inside each segment, so no rounding of a boundary they share with the road decides it.
- The road's z at the path's vertices: z_{i+1} = z_i + g_b L_i in doubles (rounded once per vertex); the polygon
  corners' heights are the road plane's, rounded down exactly (`height_down`).

### Checks and oracles

- Road triangles: `Slope.IsNotSteeper(Grade)` and every vertex on or below the road plane (exact; this proves the
  triangle lies below the road, without extrapolating it to far corners).
- Wall triangles: `IsNotSteeper(wall slope)` and their plane on or below every corner of the polygon (exact): then
  they lie below the polygon's slope cone.
- Width oracle (instead of `MinLineDistance`, the same intent, stronger): every nominal polygon, at least Width wide
  (checked exactly), is the road of its own segment (or bend): no other triangle, not even another segment's road (a
  lower segment reaching inwards under a later one), overlaps its interior (exact separating-axis test), and that road
  covers its area (to 1e-7, relative to the polygon; a hole can only be where nothing is built). Else `RampNotOnWall`
  with `RampProblems` (ramp, path segment). This also finds a hanging road. Not checked: strips of Width δ along the
  path's start and end and on both sides of each vertex (the nominals end Width δ inside each segment and kite, so that
  no rounding of a boundary they share with a neighbour decides the oracle).
- The wall's slope is the cut slope of the toe segment the path starts on (the API has no field for it).
- Tests (independent of the construction): the surface between a brute-force reference from below (roads reaching far
  inwards, Tolerance) and above (the specified roads and their cones, the toe's cone envelope), every triangle not
  steeper than its piece's spec, slope triangles supported (O1), the road's heights, the width by point samples, the
  boundary only at the toe and the height limit, the catch lines between the references.

### A fix found by the ramp campaign

The ramp campaign found a design that was not the same scaled by 2^-9: the second prefilter of `SlopeMap.InsertPiece`
skipped faces smaller than the error of their approximate corners (at the ramp's start on a toe vertex, where many
planes meet). Fixed separately in #806 (issue #805: `SlopeMap.prefilter_skips`, `MaxGradient` bounded by the map from
the planes it is handed); `InsertPolygonPiece` hands it the road and wall planes too, and its own polygon prefilter
counts the same corner error. `TerrainSlopeCampaignTests.Regression_PrefilterOfTinyFaces_IsScaleInvariant` keeps the
ramp case.

### Limits of R1

Cut only (`Direction` Fill or Auto: `NotImplemented`); straight segments of one grade; no bends towards the road, no
turns with s cos(turn) < g / 0.9; the road above the terrain is clipped by the clamp (the access ramp ends where it
daylights; a road through a terrain valley has a gap there, which R2's fill would close).

A ramp on an axis-parallel pit (`TerrainSlopeRampTests.Straight`) needs a raised margin δ when the cone facets per turn are
a multiple of 4 (32, 64 and 65 seen; 21, 35, 50, 79 and 111 certify in one attempt), with any `MaxFanDirections`. Then
fan directions coincide with the pit's edge directions. The design still succeeds, in two attempts, so it is a cost, not
a defect. Near the bound of `MaxFanDirections`, the raised margin's facets can need one more than the bound, and the
design is refused with the Tolerance they fit from (#850).
