# Design note: ramps as design geometry (terrain item 2)

Status: **proposal, owner decision pending** (2026-11-12; the coordinator routes the three questions at the end to the owner). Nothing is built. Proposed answers: the path is the inner edge, R1 is cut only with access ramps ending at daylight, one constant grade per ramp.

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

## Questions for the owner

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
