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
- **Realization.** Vertex XY is rounded correctly to the final double coordinates (from the exact rational point), then
  z is the minimum of the incident face planes at that XY, rounded correctly. Each face is triangulated by ears at its
  exact corners (no triangles across collinear points); every triangle is checked exactly (XY orientation, (i), (ii)).
- **Margin.** `s_build = s·(1 − δ)`, δ = `SlopeMargin` (default 1e-6), as 5.4. In addition, see below, all planes are
  lowered by λ.

## Departures from the design, for review

1. **Check (ii) needs a vertical margin; the toe is realized λ below the specified toe.** 5.4 derives the margin for
   (i) (the slope) only. For (ii) a far triangle's plane is extrapolated back to the toe: with vertex errors e (crease
   vertices, e ≈ 2s·e_xy) and the smallest altitude h, the plane at distance D is off by up to e·(1 + 4D / h). At UTM
   (e ≈ 1e-9 m), D = 500 m and h = 1 m that is about 2e-6 m above the toe, so (ii) fails although the triangle is below
   every cone in fact. Giving the toe triangles the real toe heights while the planes are lowered would break (i) for
   them instead. 1a therefore lowers all toe points (and so all planes) by λ = 2·e_z·(1 + 4·D_max / MinFeatureSize).
   With `MinFeatureSize` derived from δ and e as in 5.4, λ ≈ δ·s·D_max / 2 whatever the size of the coordinates: the
   margin term δ·s·D that the tolerance budget of 5.1 already contains. So every triangle with an altitude of at least
   `MinFeatureSize` passes (i) by δ and (ii) by λ; smaller ones are slivers for the repair. The toe vertices of the output
   lie λ below the specified toe (0.25 mm at δ = 1e-6, s = 1, D_max = 500 m). 1b will have to join the design at these
   heights, or decide differently: a question for the owner.
2. **No design TIN in 1a** (see Scope): `CrossDesignInfluence` comes with the design area.
3. **`HeightLimit`** replaces `TargetHeights` and the terrain extent until 1b; it is one height, not a list.

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
