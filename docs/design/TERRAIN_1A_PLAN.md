# Terrain phase 1a: plan

Status: plan of the 1a PR (owner approval of [TERRAIN.md](TERRAIN.md) on 2026-11-01). It names what 1a builds, what it
leaves to 1b / 1c, and where the implementation departs from the design, with the reason. Section numbers refer to
TERRAIN.md.

## Scope

1. **The oracle** (5.4, 6), usable on its own to check any TIN, also results of other tools:
   - `SlopeSpec` (ratio 1:n, percent, degrees, rise) with `S` never steeper than the spec;
   - `Slope.IsNotSteeper(a, b, c, spec)`: exact `nx² + ny² ≤ s²·nz²` on the stored doubles, exactly against the spec as
     entered for ratio and percent (`(nx² + ny²)·n² ≤ nz²`, `(nx² + ny²)·10⁴ ≤ p²·nz²`), against the double `S` for
     degrees;
   - `Slope.IsSupportedBy(a, b, c, p0, p1, spec)`: `IsNotSteeper` and the plane of the triangle on or below both toe
     points (exact `Orient3d` / `Orient2d`).
2. **The slope surface** (5.1–5.4) for cut or fill, built up to a given height:
   - toes: open (left / right, lateral limits at the end normals) or closed (inside / outside), several toes in one
     envelope; one slope per segment, so jumps at vertices are possible (values per segment end; start and end of a
     segment equal in 1a);
   - pieces, planes with double coordinates, the exact envelope, realization with the margin, the exact check of every
     output triangle inside `Compute()`, `SlopeViolation` rather than a violating result;
   - toe validation (`InvalidInput` with the location), `ToeSteeperThanSlope`, `ToeUndercut`.
3. **The NoData guard of `TerrainSafetyLines`** (owner decision for 1a): reject outlier vertices (a NoData value such as
   z = −9999) near the sources with a status of their own, and count the band's 3D area, not only its horizontal area, in
   `MaxRefinedTriangles`. Tests: steep terrain, a single outlier vertex, the out-of-memory scenario as a bounded test that
   finishes fast.

Not in 1a: the overlay with the terrain and the catch line (1b); volumes, `StatusKind`, catch-line accuracy, km-scale
benchmarks (1c); a design TIN as obstacle and `CrossDesignInfluence` (with 1c's failure kinds, as the check needs the
design area); continuous change of the slope along a segment and the three interpolations (rejected with
`NotImplemented` until they come); explicit lateral limits and round end caps; `OutputGrid` (quantized output).

## Construction (as built)

- **No terrain in 1a, so a height bounds the surface.** The caller gives `HeightLimit`: cut builds up to it, fill down to
  it (the hook of section 10, "build the slope up to a target height"). The horizontal plane at that height enters the
  minimum as a cap; its cells are not output, and the boundary between cap and slope cells is the level line. 1b will
  take the limit from the terrain.
- **Fill is cut with z ↦ −z** (exact), as 5 says.
- **Planes through three double points.** Every plane of the construction is given by three double points, so it is an
  exact rational plane: a strip plane through both (lowered) toe end points and a third point, a cone tangent through its
  (lowered) apex. All planes at one toe vertex pass exactly through the same point, so their creases meet there exactly,
  with no tiny cells around the apex (5.3 rounds the offset `c` instead, which would leave them). Each plane is checked
  exactly when it is made: slope ≤ `s_build` (the oracle on its three points) and on or below both end points of its piece
  (the support check); a cone tangent that fails the second check (directions next to the strip direction) is dropped,
  which keeps `F_k ≤ f_k`.
- **Exact predicates.** All decisions of the envelope are signs of polynomials in the input doubles (the side of a line
  of two planes, or of a toe edge, at a vertex of two such lines). They are decided by interval arithmetic with outward
  rounding (`NextUp` / `NextDown` after every operation, so the enclosure is a proof without a hand-derived constant),
  and exactly with `BigInteger` dyadic numbers when the interval contains 0. The code is under the exact-code analyzers.
- **Envelope as a planar map of convex faces.** The domain (box minus the design side of the toes, cut at the lateral
  limits) is triangulated (`ExactTriangulation2`), every face labelled with the cap. Each piece is inserted in turn: in a
  face with plane L, the region where the piece is lower is the convex polygon `{max_j P_kj < L}`; only if it is not empty
  is the face split along its edges, and the new part is split further into the cells of the piece's planes (incremental
  maximum). Splits go through a half-edge structure, so neighbouring faces share their vertices (no T-junctions). Ties
  keep the earlier label, so the result does not depend on the thread schedule. A loose quadtree (public API) finds the
  faces near a piece.
- **Cone tangents on a global angle grid.** The tangent planes of all apexes use the same directions (step θ, from the
  facet share of `Tolerance`) and the same distance (a power of two near the reach) for their points, so equal apexes,
  directions and slopes give identical planes. A grid direction within θ/2 of a strip direction at the same apex
  (its own piece's or a neighbour's) would make two nearly coplanar planes through one point, whose crease runs in an
  arbitrary direction: it is dropped, or replaced by an exact copy of the neighbour's strip plane if that passes the
  piece's checks. A direction is then at most 3θ/4 from a tangent plane, and θ is chosen for that.
- **Simplification.** Splits along lines of planes that are not the minimum in the end are removed: neighbouring faces
  with the same label are merged where the union is convex (exact), and vertices left in the middle of a straight edge
  are removed. Such lines cross the true creases at arbitrary, sometimes nearly coincident points.
- **Realization.** Vertex XY is rounded correctly to the final double coordinates (from the exact rational point), then
  z is the minimum of the incident face planes at that XY, rounded correctly. Each face is triangulated by ears at its
  exact corners (no triangles across collinear points).
- **Repair** (5.4). Nearly coincident lines remain possible (a triangulation edge of the domain along a crease, in
  symmetric designs). Edges shorter than `MinFeatureSize` are collapsed, shortest first, onto the end whose planes
  contain the other end's planes, so the triangles stay in their cells; toe and lateral-limit vertices are only removed
  onto another such vertex. Then every triangle is checked exactly (XY orientation, (i), (ii)); if one fails, the build is
  repeated with 8 times the margin (up to 1e-3), and then `SlopeViolation`.
- **Margin.** `s_build = s·(1 − δ)`, δ = `SlopeMargin` (default 1e-6), as 5.4. In addition, see below, all planes are
  lowered by λ.

## Departures from the design, for review

1. **Check (ii) needs a vertical margin; the toe is realized λ below the specified toe.** 5.4 derives the margin for
   (i) (the slope) only. For (ii) a far triangle's plane is extrapolated back to the toe: with vertex errors e (crease
   vertices, e ≈ 2s·e_xy) and the smallest altitude h, the plane at distance D is off by up to e·(1 + 4D / h). At UTM
   (e ≈ 1e-9 m), D = 500 m and h = 1 m that is about 2e-6 m above the toe, so (ii) fails although the triangle is below
   every cone in fact. Giving the toe triangles the real toe heights while the planes are lowered would break (i) for
   them instead. So all toe points (and with them all planes) are lowered by λ.
   - **Measured, not the worst case** (owner, 2026-11-01). The construction is exact (rational planes and vertices),
     so the only rounding is the final one into the output coordinates; a local frame would change nothing there. The
     first build lowers by a small λ0 = 2·e_z·257; if a triangle fails (ii) only, the next build lowers by
     max(2λ, 4 × the largest excess measured on the output). The worst-case bound λ_bound = 2·e_z·(1 + 4·D_max /
     MinFeatureSize) ≈ 1.1·δ·s·D_max (the review measured 0.55 mm for δ = 1e-6, s = 1, D_max = 500 m) is reserved in the
     tolerance budget and caps λ; above it the margin δ is raised instead. `UsedToeLowering` reports the λ used.
   - Measured (`TerrainSlopeDesignTests.ToeLowering_Origin_vs_Utm`, a 9-gon of radius 60 m, slope 1:1, 40 m high):
     λ = 3.0e-10 m at the origin and 9.6e-7 m at UTM (632117, 5712043); both succeed with δ = 1e-6. At UTM λ is driven by
     the output grid: e_xy is the ulp of the northing (9.3e-10 m).
   - Without any lowering (λ = 0, the design as written), 23 of the 27 slope surface tests failed the exact check, also
     at local coordinates (`SlopeViolation`, 150 to 350 triangles each).
   - 1b will have to join the design at these heights, or decide differently: a question for the owner.
2. **No design TIN in 1a** (see Scope): `CrossDesignInfluence` comes with the design area.
3. **`HeightLimit`** replaces `TargetHeights` and the terrain extent until 1b; it is one height, not a list.

## Look before you build (owner rule, AGENT_WORKFLOW 3)

- **Library building blocks used:** `ExactTriangulation2` (the domain), `LooseQuadtree2d` (faces near a piece, public
  API only), `ExactPredicates.Orient2d` / `Orient3d` (the oracle's support check, the toe validation), `DMesh3`
  edge collapse (the repair); in the tests `TerrainVolumeDifference.XYOverlaps` (height field), `TerrainGreedyTIN`
  (existing results for the oracle), `Invariance` / `Similarity`. Looked at and not used: `ExactPoint3` /
  `ExactMeshArrangement` (three-plane points in 3D on an integer grid, a different setting from lines of plane pairs in
  XY), the fixed-width `FixedPoint3` (needs integer-grid input), the generated filters of `tools/PredicateCompiler`
  (static bounds per predicate; the degree-14 side test of two plane-pair lines would be a new spec entry: a candidate
  for the performance pass, measured against the interval filter first).
- **Literature:** the review of TERRAIN.md section 4 (descriptive geometry of slope cones, Lipschitz extensions,
  weighted Voronoi diagrams as cone envelopes, straight skeletons, Fast Marching, open-pit design, road daylighting,
  map overlay); for the implementation interval arithmetic (Moore 1966), the half-edge structure (Muller & Preparata
  1978; de Berg et al.) and Shewchuk 1997. Listed in docs/SOURCES.md.

## Review (independent, 2026-11-01) and fixes

No violation of the guarantee was found. Fixed in this PR, each with a regression test (`Review_*`):
- valid inputs with uneven toe heights and mixed slopes failed with `SlopeViolation`: the planes of a failed attempt
  were kept for the next one (fixed), and the fan sector window wrapped at ±π (now centred on the sector);
- `MaxSlopeRatio` was a double estimate (1.00104 on a needle): now a bound, rounded up and confirmed exactly, else 1;
  faces are triangulated by dynamic programming over their corners (largest smallest quality), which avoids needles
  at nearly flat corners;
- an open toe with its slope on the concave side was rejected: each lateral ray now ends at its first toe or at the
  other ray;
- exceptions are mapped to `Statuses.Failed`; slopes with S = 0 or subnormal are invalid; a reach that is not finite
  is `InvalidInput`; toe segments below the smallest realizable feature are `InvalidInput` with their location;
- `TerrainSafetyLines.Status` is `Failed` after an exception, not the status of an earlier run.
Open (not blocking, recorded): flips of needles without a short edge inside a cell (5.4 "caps"); the O2 oracle samples
vertices and centroids only; `Simplify` repeats whole passes; the toe validation is a sweep in x only.

## Tests (per CLAUDE.md and TERRAIN.md 7)

- oracle: hand-computed triangles exactly at the slope and ±1 ulp, against rationals; ratio and percent exactly; the
  `BigInteger` path at extreme magnitudes; degenerate triangles; `IsSupportedBy` against hand cases; against existing
  `TerrainGreedyTIN` results (the oracle agrees with a slope computed in rationals on every triangle);
- surface: straight toe (planes, analytic), square and n-gon (fans, no inscribed facets), L and U shapes (valleys), a
  slope jump at a vertex, an inclined toe, `ToeSteeperThanSlope` at |g| = s_build, a two-level toe (`ToeUndercut`),
  invalid toes, fill; every success path checks O1 (`IsSupportedBy` per triangle), O2 against a brute-force minimum of
  exact cones (0 ≤ S_ref − S̃ ≤ Tolerance), a height field without gaps (exact orientation, boundary only at the toe,
  the lateral limits and the level line) and finite coordinates;
- invariance over `Similarity.All()` with rotations about z (2^k bit for bit; O1 in every frame), reuse, determinism;
- mutation checks: drop the margin, the lowering λ, or a plane check, swap piece labels: O1 must fail.

Performance numbers are not claimed in 1a; the km-scale benchmark is 1c.
