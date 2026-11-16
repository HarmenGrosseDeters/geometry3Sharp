# Design note: sector polygons and the exterior distance (terrain item 3)

Status: **parked (owner 2026-11-16), see [the status section](#status-2026-11-16-parked-owner).** Earlier: owner decision 2026-11-13: yes to the proposals (below): E1, the exterior distance, first; R2 attribution with the
smaller slope on a sector boundary; an ε-approximation within the Tolerance budget for S1. Ordered after ramps R1;
nothing is built yet. `TerrainSlopeDesign.Modes.SectorPolygons` and `Modes.ExteriorDistance` exist in the API sketch
(TERRAIN.md 6) only; **they are not in the code** (see the status section).

## Goal

Two extensions of the cone envelope R1 (TERRAIN.md 2.2, 2.3, 5.2):

1. **Exterior distance.** For a closed toe the design area is an obstacle. A cone must not reach a wall point through the
   area's interior. Today such designs are detected and rejected (`CrossDesignInfluence`). This one replaces the
   Euclidean distance by the shortest path around the design area.
2. **Sector polygons.** Geotechnical domains are given as polygons in plan, each with its own slope. The slope a wall
   point must obey is that of the sector it lies in (R2, attribution by location), not that of the nearest toe point
   (R1).

The guarantee stays the same: every output triangle is never steeper than its rule allows, checked exactly. Tolerance
bounds the over-excavation against the exact surface of the mode.

## The exact surfaces

- **Exterior distance:** S(x) = min over toe points q of z(q) + s · d_ext(x, q), with d_ext the length of the shortest
  path from q to x in the closure of the exterior. A shortest path around a polygonal obstacle bends only at its reflex
  vertices (Lee and Preparata 1984). So S is the min of:
  - the toe's own cones, restricted to the points that see their apex;
  - secondary cones at the reflex vertices v of the design area, at height z_v = min over q of (z(q) + s · d_ext(v, q)),
    restricted to the points that see v around the obstacle (the shadow regions).
- **Sectors:** S(x) = min over q of z(q) + d_σ(x, q), with d_σ the weighted geodesic distance of the slope field σ,
  piecewise constant on the sector polygons. This is the largest function with |∇S| ≤ σ that is ≤ z on the toe (McShane
  extension in the length metric of σ). Shortest paths refract at sector boundaries (Snell's law); this is the weighted
  region problem (Mitchell and Papadimitriou 1991).

## How it fits the existing construction

- **Exterior distance (proposed first, E1):**
  - The secondary apexes are toe pieces of zero length at the reflex vertices. Their height comes from a visibility
    graph of the toe vertices and the reflex vertices, with exact orientation tests for visibility and path lengths
    rounded down, so z_v is a lower bound: conservative.
  - Their cones are clipped to the shadow region behind v, a wedge bounded by the obstacle edges at v.
  - The clipped cones need a change in the envelope: a cone restricted to a convex wedge is the max of its tangent
    planes and the wedge's two "wall" half-planes at +∞ outside. The planar map (TERRAIN.md 5.3) already handles
    lateral limits of open toes the same way: limits as barriers. So the wedge is a lateral limit of the secondary
    piece.
  - The support check (ii) of the oracle holds per piece as today: the plane is at or below the apex height z_v.
  - Through the design area nothing is supported any more: `CrossDesignInfluence` disappears for this mode.
- **Sectors (S1, after E1):**
  - The sector boundaries become constraints of the realization: triangles do not cross them.
  - Each triangle is checked exactly against the slope of its sector. A triangle on a boundary gets the smaller slope.
  - The construction approximates d_σ with Steiner points on the sector boundaries (Aleksandrov, Maheshwari and Sack
    2000 / 2005: a (1 + ε)-approximation with O(1/ε log 1/ε) points per edge). Each Steiner point is a secondary apex
    whose height is a graph distance rounded down, with cones of the sector beyond.
  - The error of the approximation is part of the Tolerance budget: a new share, with a derivation and the bounds review.
    The exact check per triangle decides as always.
  - Fast Marching / eikonal solvers are not conservative (TERRAIN.md 5.9) and stay rejected.

## API (additive)

- `TerrainSlopeDesign.Mode`: `ExteriorDistance` (closed toes; the area inside the toe is the obstacle, or an explicit
  `Design` TIN's footprint), `SectorPolygons` with `Sectors`.
- `SlopeSector { List<Vector2d> Polygon; SlopeSpec Cut, Fill; }`: simple polygons. Together they cover the slope's
  footprint and do not overlap (exact), else `InvalidInput`.
- The outputs are as today. `Pieces` gets the secondary pieces, with a kind and the toe point or reflex vertex they come
  from.

## Statuses

- `InvalidInput`: overlapping sectors, a gap in the cover, a non-simple polygon.
- `NotImplemented`: the combinations not in a phase, for example sectors with Auto in S1.
- The existing statuses keep their meaning. `ToeUndercut` uses d_ext or d_σ instead of the Euclidean distance (the
  undercut test of 5.2 is the same with the secondary cones).

## How others do it (prior-art line)

- Shortest paths around polygons: visibility graphs (Lee and Preparata 1984); continuous Dijkstra in O(n log n)
  (Hershberger and Suri 1999).
- Weighted regions: Mitchell and Papadimitriou 1991 (exact, Snell's law); Aleksandrov, Maheshwari and Sack 2000 / 2005
  (Steiner-point ε-approximation).
- Mining practice: slopes by geotechnical domain or by wall azimuth (Maptek Vulcan's batter angle per region, Datamine);
  variable slope angles in pit optimisation (Khalokakaie 1999; Gilani and Sattarvand 2015). These build strings and do
  not certify per triangle.
- Nothing was found that combines a weighted-region distance with a per-triangle slope certificate. Terms searched:
  weighted region shortest path slope design, sector slope open pit TIN.

## Phases and size

- **E1:** the exterior distance for closed toes, R1 slopes; tier L. About the size of 1b. Its new error bound is only
  the rounding of the secondary apexes' heights.
- **S1:** sectors with Steiner points; tier L, research-sized: a new Tolerance share and its derivation.
- Both get the usual tests: the exact oracle per triangle, `Similarity.All()` in a `LocalFrame`, reuse, and campaign
  generators.

## Owner decisions (2026-11-13)

1. **E1 (the exterior distance) first**, then the sectors (S1); both after ramps R1.
2. Sectors: **attribution by the location of the wall point (R2)**, the smaller slope on a sector boundary.
3. Sectors: an **ε-approximation** of the R2 optimum (Steiner points) **within the Tolerance budget** is acceptable.

## Questions for the owner (answered above)

1. The order: E1 (exterior distance) first, as proposed, or the sectors first?
2. Sectors: the attribution by the location of the wall point (R2), and the smaller slope on a sector boundary?
3. Sectors: is an ε-approximation of the R2 optimum in the Tolerance budget acceptable? The exact weighted-region surface
   has no practical exact algorithm.

## Status 2026-11-16: parked (owner)

The E1 session read this note against the code before building and stopped. Owner decision 2026-11-16: park E1, S1 and
R2; no behaviour changes. Findings:

1. **Premises of this note that are not in the code.**
   - There is no `Mode`, `Modes.ExteriorDistance` or `Modes.SectorPolygons` in `TerrainSlopeDesign`; they exist only in
     the API sketch of TERRAIN.md 6. Nothing is "rejected as `NotImplemented` today" for them.
   - There is no `CrossDesignInfluence` status or check. TERRAIN.md 5.2 designed it, phase 1 did not build it. Today the only
     guard is `ToeUndercut`, which `through_design_area` classifies as `UnsupportedByMethod` when the causing cone reaches
     the toe through the design area. A Euclidean cone across the design area that undercuts no toe is accepted, which is
     valid for R1's Euclidean definition (the output is the exact Euclidean envelope), not the exterior surface.
   - So "`CrossDesignInfluence` disappears for this mode" has nothing to remove.
2. **A wedge clip is not enough.** The visibility region of an obstacle vertex in the exterior is star-shaped and not
   convex; for a non-convex design area or holes, other parts of the design area cut it. The toe's own primary pieces (strip
   planes and fans) must also be clipped to the points that see them: a weak-visibility region per toe edge, bounded by
   shadow lines through obstacle vertices parallel to the fall direction. A subset of the visible region leaves S above the
   exact surface; a superset breaks the Tolerance bound. Both need exact handling.
3. **The planar map has no per-piece clip.** Lateral limits are domain barriers shared by the whole toe, not a region per
   piece. `InsertPiece` / `insert_into` would need a variant "valid only inside convex clip cells, +infinity outside",
   which touches the exact core (`insert_into`, `Simplify`, `Undercuts`, the support check). The height of a secondary apex is
   the small part: for a closed toe whose design area is its own interior, z_v = z(v) when no toe is undercut, so "the new
   error bound is only the rounding of the apex heights" is nearly vacuous. The bounds that matter are the shadow-line
   direction errors and the clip boundaries, which this note does not budget.
4. **Also affected:** ramps (polygon pieces), `through_design_area`, the overlay and catch-line certificates, the local
   frame, Auto and fill (z negated).

**Size.** About 2-3 times 1b, not "about the size of 1b": a visibility graph, exact visibility and shadow regions, a map
insertion variant, a Tolerance-budget derivation, and an oracle test against an independent exact exterior-distance
reference.

**Options put to the owner.**
- **A.** Restricted E1: only designs with a simple exact exterior distance (for example one closed toe with a convex-separable
  obstacle); everything else keeps `ToeUndercut` / `NotImplemented`. Smaller, covers few real pits.
- **B.** Full E1 with weak-visibility clipping, a multi-PR tier-L feature: rewrite this note first, owner review, then the
  map change, then the Design integration.
- **C.** First add the missing `CrossDesignInfluence` detection (conservative hull test, TERRAIN.md 5.2). It changes which
  designs are accepted, so it is an owner decision.

Decision: park everything (owner 2026-11-16). If E1 resumes, the recommended order is C, then B with a rewritten note.

**How others solve it (prior-art line).** Shortest paths among polygonal obstacles: visibility graph, Lee and Preparata
1984; continuous Dijkstra, Hershberger and Suri 1999; weak visibility polygons, Guibas, Hershberger, Leven, Sharir and
Tarjan 1987. Nothing found that combines them with a per-triangle slope certificate (terms as above).
