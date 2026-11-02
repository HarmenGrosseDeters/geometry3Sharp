# Terrain phase 1c: plan

Status: plan of the 1c PR, first part: the literature check for the speed of the slope surface and the upper bound of
each idea, before any of it is built (coordinator, 2026-11-02, "look before you build"; AGENT_WORKFLOW.md 2, 3, 8).
Section numbers refer to [TERRAIN.md](TERRAIN.md). Scope of 1c (section 11): volumes (5.7), failure kinds (5.6),
catch-line accuracy (5.5), km-scale benchmarks (8), and the speed case of 1a (the wavy 64-gon).

## 1. Where the time goes (measured)

The speed case of 1a, `TerrainBenchmark slope toe=wavy n=64 r=100 h=30 coords=utm` (closed wavy 64-gon, toe heights
±4 m, 1:1, `Tolerance` 0.01), 4 cores, 2026-11-02, master with 1b (#285):

| total | builds | insert | simplify | realize / check | predicates (exact) | exact stage | triangles | λ |
|---|---|---|---|---|---|---|---|---|
| 44.0 s | 3 | 17.0 s | 26.3 s | 0.39 / 0.12 s | 79.5M (6.0M = 7.5 %) | 18.4 s | 14577 | 5.9e-6 m |

- The exact stage was timed by a local stopwatch around the exact branches of `ExactSlopeKernel.Side` / `Orient` (not
  committed): 18.4 s for 6.0M calls, about 3 µs each; the filtered 73.5M take the remaining 25 s, about 0.34 µs each.
- Per build: about 14.7 s, almost all in `insert` and `simplify`; realization and the exact output check are about 1 %.
- 5400 predicates per output triangle.

## 2. Literature: lower envelopes of planes and cones

S̃ = min_k F_k, each F_k the maximum of the planes of one toe piece (a convex polyhedral function), so S̃ is the lower
envelope of n convex polyhedral functions, or, with all planes, of N piecewise linear functions; its projection is the
minimization diagram. The exact R1 surface (min of cones) has as its diagram a weighted Voronoi diagram of the toe
segments.

| family | what it gives | sources | fits here? |
|---|---|---|---|
| Lower envelopes of piecewise linear functions / triangles in 3D, divide and conquer | O(n² α(n)) worst-case complexity and time for n triangles; the merge of two envelopes is an overlay | H. Edelsbrunner, L. J. Guibas, M. Sharir, *The upper envelope of piecewise linear functions: algorithms and applications*, DCG 4, 1989; M. Sharir, P. K. Agarwal, *Davenport–Schinzel Sequences and Their Geometric Applications*, Cambridge 1995, ch. 7 | the worst case is for arbitrary triangles; our functions are convex per piece and the diagram is Voronoi-like (each piece owns a connected region near its segment), where the complexity is linear in practice. The merge step is the 1b overlay with a min instead of a difference |
| Randomized incremental construction (RIC) | expected time bounded by the expected size of the structure after each step (backwards analysis); for envelopes of triangles O(n² α(n)) expected, output-sensitive for Voronoi-like diagrams, O(n log n) | K. L. Clarkson, P. W. Shor, *Applications of random sampling in computational geometry II*, DCG 4, 1989; K. Mulmuley, *Computational Geometry: An Introduction through Randomized Algorithms*, 1994; J.-D. Boissonnat, K. Dobrindt, *On-line construction of the upper envelope of triangles and surface patches in three dimensions*, CGTA 5, 1996 | **yes, closest to 1a**: 1a already inserts piece after piece into a planar map; it inserts in toe order and keeps conflict information only through the quadtree. A random order bounds the expected number of faces a new piece changes |
| Sweep (Fortune) | O(n log n) for Voronoi diagrams of points, of segments and of additively weighted points, by a transformation that makes the sweep line meet a site before its region | S. Fortune, *A sweepline algorithm for Voronoi diagrams*, Algorithmica 2, 1987 | for constant slope the R1 diagram is the Voronoi diagram of the toe segments with additive weights (the toe heights, linear along a segment): curved bisectors (conics), exact predicates of high degree; varying slopes give multiplicative weights. Not for the plane-based S̃ of 1a, whose cells are polygons; a sweep over the planar map would need the event structure for every plane crease |
| Multiplicatively weighted diagrams | Θ(n²) complexity in the worst case for points | F. Aurenhammer, H. Edelsbrunner, *An optimal algorithm for constructing the weighted Voronoi diagram in the plane*, Pattern Recognition 17, 1984 | explains why varying slopes have no near-linear guarantee; per toe piece the diagram is still a connected cell in our cases |
| Lower envelope of planes = convex polytope (duality, 3D hull) | O(N log N) | F. P. Preparata, S. J. Hong 1977; Clarkson–Shor 1989 | only for a min of planes (concave) or one F_k (convex, a max): it builds each F_k, which is cheap already; S̃ is a min of convex functions, neither convex nor concave |
| Kinetic / sweep over heights (offset curves level by level) | the level sets of S̃ are offsets of the toe | contour offsetting, eg Held 1991 (pocket machining) | gives the level lines, not a surface with the slope guarantee (TERRAIN.md 5.9 rejected it as the core) |

## 3. What the library has

- `SlopeMap` (1a): an incremental planar map of convex faces with exact predicates, a loose quadtree over faces, insertion
  by clipping a face with a piece and an incremental max; `Simplify` merges same-label faces afterwards.
- `SlopeTerrainOverlay` (1b): a symbolic overlay of two triangulated surfaces with SoS; with min instead of the sign of a
  difference it is the merge step of a divide and conquer.
- `ExactTriangulation2`, `LooseQuadtree2d` / `TriangleQuadtree2d` (public API), `ExactSlopeKernel` (filter, then exact).

## 4. Ideas and their upper bounds

Each bound is the time left if the idea removed its whole share, measured above (an upper bound of the speedup, not
a forecast). Ordered by bound per effort.

| # | idea | removes | time left at best | bound | effort, risk |
|---|---|---|---|---|---|
| A | **One build, λ by re-realization.** Lowering the toe by λ lowers every plane by λ. In exact arithmetic the line of two planes, Nz_a·D_b − Nz_b·D_a, does not change when both D grow by λ·N_z, so the map is the same for every λ; only the lines with the cap plane change. Build the map once from the unlowered planes (cap at H, so the lowered surface ends at H − λ, never above it), and realize with z = exact height − λ, exactly rounded. A support failure then costs a re-realization and a check (about 0.5 s here), not a build | builds 2 and 3 | 14.7 + 2 × 0.5 ≈ 15.7 s | **≤ 2.8×** here, ≤ 2× in the 2-build cases | small: the kernel planes get an exact offset; the attempt loop changes; δ raises still need a build. To check before building: the undercut test and the planes that are dropped by the support check at construction must use the lowered points |
| B | **No `Simplify` pass**: insert a piece into a face as one convex region per label (its cells merged while they are made), so no split lines are left to merge afterwards | 26.3 s | 17.7 s (with A: 6.2 s) | ≤ 2.5× (with A ≤ 7×) | medium: the incremental max must produce merged cells directly; the failed-merge memo of 1a shows how much of `Simplify` is re-trying |
| C | **Faster exact stage**: fixed-width integer arithmetic instead of `BigInteger` dyadics, as the generated predicates of the library do (docs/design/FIXED_WIDTH_EXACT_POINTS.md), or a tighter filter (Shewchuk-style static bounds for the side test) so fewer calls reach it | ≤ 18.4 s | 25.5 s | ≤ 1.7× | medium: the degree of the side test (planes through 3 points, crossing of two plane lines) sets the width; a bounds review for every new filter constant |
| D | **Randomized insertion order** (Clarkson–Shor, Boissonnat–Dobrindt): insert the pieces in a random (seeded, so deterministic) order instead of along the toe | the faces a piece changes and later pieces undo | not measurable before: the bound is the gap between 5400 predicates per output triangle and an output-sensitive O(log n) per face | ≤ 30× in principle (100 predicates per triangle) | small to try; the result must stay independent of the order (ties keep the earlier label today, so the order changes which of two equal labels wins: needs a canonical tie rule first) |
| E | **Divide and conquer**: envelopes of the two halves of the toe, merged by the 1b overlay with a min | the faces far from a piece | as D | as D | large; the merge is the 1b overlay with min, a building block that exists |
| F | Exact curved R1 diagram (Fortune-type sweep over the toe segments, additively weighted) | the planes altogether | not comparable: another surface (curved cells, approximated afterwards) | — | rejected: bisectors are conics with high-degree exact predicates, varying slopes make it multiplicatively weighted (Θ(n²) worst case), and the plane-based guarantee of 1a would have to be rebuilt |

Proposal: A first (the largest bound for the smallest change, also the 1b repeated builds), then measure again and
decide between B, C and D with the new split. D needs a canonical tie rule before any order change, so the result stays
deterministic and independent of the input order.

## 5. The rest of 1c

- Volumes (5.7): on the 1b overlay, D linear on convex faces, integrated exactly as in `TerrainVolumeDifference`; cut,
  fill, areas, per piece; `TerrainVolumeDifference(Terrain, Composite)` as the independent check.
- `StatusKind` (5.6) for every status.
- Catch-line accuracy (5.5): per segment |∇D| and δ_xy = ε_D / |∇D| (local estimate), `IllConditionedCatchLine`,
  `TangentialContact`, `CatchLineWarningThreshold`, `RequiredCatchLineAccuracy`, `TerrainVerticalUncertainty`.
- km-scale benchmarks (section 8): procedural first; the Hambach data via `fetch_data.py` (nothing committed).
- From 1b: the band-only overlay, a local height limit instead of the terrain's global maximum, the per-triangle
  constraint checks of the composite (O(segments × points)).

## 6. Realization repairs and their bounds (done in 1c)

The map is exact; its realization rounds every vertex to the nearest doubles (e_xy, e_z: the rounding in XY and z).
Three repairs keep the rounded TIN a valid slope surface. Each changes the surface by a bounded amount, and the toe
lowering λ covers it (the exact check of every triangle decides in the end; the bounds decide what is allowed).

- **Merge** of positions closer than r = 64 e_xy (`SlopeMap.merge_close_positions`, greedy: each position moves by
  less than r, no chains). A vertex then sits up to r + e_xy from where its planes meet, so the surface moves by up to
  sMax (r + e_xy) per plane both ways. Share below S̃ (the slope margin budget): `MergeShare` = 4 sMax (r + e_xy) +
  `FlipShare`; above S̃ (toe lowering): `RepairLambda(k)` = 4 e_z + 2 k sMax (r + e_xy), k the number of repair kinds
  used (merge, flips). A realization merges or flips only if `RepairLambda(2)` ≤ the λ bound (`mayMerge`), else it
  realizes without them.
- **Dropped faces**: a face narrower than the rounding (between two creases a few ulps apart) has exactly collinear
  rounded corners. Its triangles are dropped (kept, they would be vertical walls over their neighbours) and the
  triangle on the other side of the long edge is split at the middle corner, which lies exactly on that edge
  (`SlopeMap.stitch`): no gap, no T-junction. No change of the surface beyond the rounding. Unit test at the
  realization level: `TerrainSlopeRepairTests.Realize_DropsAFaceNarrowerThanTheRounding_NoGapNoOverlap` (both the
  missing drop and the missing stitch fail it); the crack check `boundary_is_toe_limit_or_cap` runs on every realization.
- **Needle flips** (`SlopeMap.FlipNeedles(mesh, minAltitude = r, maxDeviation = FlipShare)`): a triangle thinner than r
  across its longest edge is flipped with its neighbour. A flip changes the surface by zero at the quad's corners and
  linearly on the four sub-triangles around the crossing of its diagonals, so most at that crossing
  (`diagonal_change`). It is evaluated exactly and rounded up: in doubles the crossing of the nearly parallel
  diagonals of two needles on one long edge is off by eps / sin of their angle, up to 1.6e-6 m at UTM magnitude, more
  than the whole share (a fixed 16 ulps of z did not cover it; `DiagonalChange_IsAnUpperBound_AtNearlyParallelDiagonals`
  against an exact reference, 893 of 2000 cases below it before). The changes add up per triangle (a flip keeps the
  two triangle ids, which carry the larger of the two bounds plus its change), and a flip that would take a triangle
  beyond `FlipShare` = 2 sMax (r + e_xy) is refused. Tests: `NeedleFlip_WithinItsShare` (the bound),
  `WavyToe_130mHigh_AtUtm` (flips needed for success, λ at least the requirement).

### Local height limit

With a terrain and `HeightLimit` NaN, the cap starts at the highest terrain vertex near the toe (within the toe's box)
plus `Tolerance`, at least the highest toe point plus `Tolerance`. If the region reaches the cap
(`SlopeTerrainOverlay.ReachedHeightLimit`), the cap is raised (the highest terrain within twice the reach of this cap,
at least the height range again) up to the global limit. A region that does not reach the cap is bounded by the catch
line and the toe, so it is the region of any higher cap. The result is that of the global limit up to Tolerance, not
bit for bit: the facets, the margins, λ and the minimum feature size depend on the cap (its height sets D_max), so the
catch lines differ within their accuracy (`LocalHeightLimit_IsRaised_AndMatchesTheGlobalLimit`). While the cap may
still grow, no composite is built (a region at the cap is not final); a cancelled run leaves no state behind
(`Reuse_AfterACancelledLocalLimitRun`).

### The composite outside the slope

Only the terrain within the box of the slope surface must be a height field (the overlay rejects overlaps there,
`NotHeightField`); outside it the composite copies the terrain as given (`TerrainOverlap_FarFromTheSlope_IsTheTerrainAsGiven`).
The composite's own overlap check covers the triangles it makes (slope and cut terrain triangles) against each other
and the copied terrain triangles within their box (`SlopeComposite.OverlapsOfConstructed`), not the copied ones among
themselves (a cut terrain triangle can reach beyond the slope's box: its overlap with a copied one outside the box is
found there, CompositeFailed); copied triangles take their vertices by terrain vertex id, except where a cut triangle meets them (by
position, so they share its vertices: `AssertComposite` checks one vertex per position).

### Baseline for the band-only overlay and composite (Hambach, 1 m grid)

Measured in benchmarks/TerrainBenchmark/README.md (slope design on the Hambach DGM): one toe edge of 200 m gives its
catch line in 0.86 s (terrain check 0.07, 1a 0.02, overlay 0.44 s); the composite adds 1.4 s (4.6 s before its
overlap check was restricted), of which 1.0 s builds the 2 million copied terrain triangles into the output mesh.
The octagon: overlay 8.9 s, composite 6.7 s, peak 1.50 GB (3.1 GB before the candidate restriction). Targets of the
band-only work: the composite for one edge below 1 s (it needs a cheaper copy of the untouched terrain, or an
output that does not copy it), and the peak below that 3.1 GB at any toe size on the 1 m grid.

## 7. The replacement output and one edge under 1 s (next PR after #297)

`TerrainSlopeDesign.CompositeKind` (additive, default `Full`, today's composite):

- `Replacement`: `Replacement` holds only the part of the composite that replaces terrain triangles: the slope region
  and the pieces of the cut terrain triangles. `ReplacedTerrainTriangles` (sorted) are the terrain triangles it
  replaces; `CompositeTerrainVertices` gives, per vertex of `Composite` or `Replacement`, the terrain vertex at its
  position (or -1; one per position, if the terrain has several vertices at one position), and
  `TerrainCompositeVertices` the other way round, per terrain vertex (all of several at one position). Replacement plus
  the other terrain triangles, joined through `TerrainCompositeVertices`, is the full composite: the same triangles, the
  same vertices (as many, an unwelded seam stays unwelded where the composite keeps it) (`TerrainSlopeReplacementTests`: random terrains cut and fill, at UTM,
  invariance over `Similarity.All()`, reuse, an unwelded terrain).
- Terrain triangles wholly in the region (all overlay faces in it, no catch point, no boundary of the slope surface
  inside, corners on the slope surface) contribute nothing and are skipped before their CDT (checked against the CDT
  on the tests and three Hambach cases before it was removed: the same composites;
  `TerrainTriangleAroundThePit_IsNotWhollyReplaced`: a terrain triangle around the pit, all faces in the region, is
  not skipped).
- The composite's overlap check takes the terrain triangles near the made ones from a list the overlay keeps during
  its one pass over the terrain (those within a quarter of the slope's size of its box), used only when the box of the
  made triangles lies inside that list's box; else all of them.
- The local height limit starts at a fixed point instead of the highest terrain at the toe: h = (the highest terrain
  within (h - zMin) / sMin of the toe box) + Tolerance, from one pass over the terrain triangles (a triangle counts
  with its highest corner at the distance of its bounding box, rounded up to a distance class: an upper bound; the
  vertices alone are not, a triangle reaching into the box from outside is higher there). At a fixed point a slope of
  the specified ratio cannot reach the cap; the built surface is a little flatter (the margin, the facets, sloped toe
  segments: its gradient bound Slb is below S), so this is an estimate that saved the raise in every case measured
  here, not a guarantee; the raise still decides (`LocalHeightLimit_IsRaised_AndMatchesTheGlobalLimit` checks both).

Measured in benchmarks/TerrainBenchmark/README.md: one toe edge on the 1 m Hambach grid, warm (repeated on one
instance), 0.28 s with the replacement output, 0.14 s for the catch line only, 0.94 s with the full composite; the
first call of a process takes about 0.7 s more (JIT): 1.01 s, 0.63 s and 1.61 s (medians over processes).

## 8. Functional completeness (#314)

- **StatusKind (5.6).** `TerrainSlopeDesign.StatusKind` for every status: `None` (Success; NoDaylight, a partial
  result, not a failure), `InvalidInput` (InvalidInput, NotHeightField), `UnsupportedByMethod` (NotImplemented;
  ToeUndercut where the cone that causes it reaches the toe through the design area), `GeometricallyInfeasible`
  (ToeSteeperThanSlope; ToeUndercut where the path from the causing part of the toe runs on the slope side),
  `NotCertified` (SlopeViolation, CatchLineAccuracyNotReached), `Internal` (Failed, CompositeFailed), `Cancelled`. The
  path of an undercut: from the point of the causing segment nearest to the undercut point, straight (the cone's
  value comes from there); through the design area if it leaves or arrives from the design side of its segment, or
  crosses a toe segment (`StatusKinds_OfEveryStatus`, `TwoLevelToe_IsUndercut`: a U-shaped toe against a convex closed
  toe with the slope inside).
- **Volumes (5.7).** `Volume`, `PlanArea`, `SlopeArea`, and per `SlopePiece`: integrated on the overlay, each region
  face a convex polygon (the XY intersection of its two triangles) with the difference linear on it, split at 0, in
  doubles relative to the centre of the slope surface. Checked against the exact cone volume over flat terrain (within
  `Tolerance` × plan area) and against `TerrainVolumeDifference(Terrain, Composite)` (`TerrainSlopeVolumeTests`).
- **Catch-line accuracy (5.5).** Per segment `CatchLine.Gradient` (|∇(S − T)| on its face), `Uncertainty`
  ((`Tolerance` + `TerrainVerticalUncertainty`) / gradient + `MinFeatureSize`, the move of the composite's snapping; a
  local linearized estimate, not a proven bound) and `Tangential`: the segment touches a planar contact (a face whose
  two planes coincide, exact, sharing an overlay vertex with the segment's face; such a face counts as no work, so the
  region, and the catch line, ends at its border). Reports:
  `IllConditionedCatchLine` above `CatchLineWarningThreshold`, `TangentialContact`. `RequiredCatchLineAccuracy`: a
  smaller `Tolerance` (`UsedTolerance`, global, at most three rounds; the per-piece refinement of 5.5 is later), else
  `CatchLineAccuracyNotReached` with the catch lines kept (also when a smaller Tolerance fails, eg below the numerical
  margins; `TerrainSlopeAccuracyTests`). The undercut path of StatusKind is decided exactly on the doubles (Orient2d;
  at a toe vertex both adjacent segments; a touch counts as through the design area, the conservative side).

## 9. Randomized insertion order with a canonical result (idea D)

The pieces are inserted in a seeded random order (Fisher–Yates with SplitMix64, the same on every platform;
`TerrainSlopeDesign.InsertionOrder` replaces it in tests). The map after `Simplify` is a function of the input only,
not of that order:

- **Labels: the lexicographic minimum of (height, plane id).** A point gets the plane of the lowest piece; where two
  pieces are equally low over an area (their planes are the same plane, as a fan plane that repeats a neighbouring
  strip), the lower plane id wins, and within a piece the maximum takes the lowest id of equal planes. In the insert, a
  plane that is the same as the face's label (its line with it is zero at every corner) wins the face only if its id is
  lower; before, the earlier label kept it, so the order decided. A function of the point, so its minimum over the
  pieces does not depend on the order.
- **The fan planes of a face: all of them near the apex.** The insert takes the fan planes whose angles lie within
  2θ of the face's angular range as seen from the apex, from the approximate corners. A corner 1e-13 m from the apex has
  a direction from its approximation that is off by more than θ, so a plane was left out, the piece's maximum came out
  too low there, and a sliver of 1e-27 m² got another label, depending on which faces the earlier pieces had left.
  Within 1e-11 / θ of the coordinates of the apex all fan planes are taken (the approximation is within 1e-12 of the
  coordinates); a corner whose approximation is the apex is skipped only if it is the apex exactly
  (`ExactSlopeKernel.IsAt`), else it counts as near. Found by inserting every piece a second time without the prefilters: no face changed, yet two orders
  differed; with all fan planes they agreed.
- **The partition: canonical.** `Simplify` no longer merges pairs greedily (that result depended on the order of the
  splits: 16007 against 46805 triangles for one surface). A component (the faces of one label, connected across edges
  that are no barrier) is cut along one ray from each reflex corner of its boundary: the extension of the boundary edge
  into the corner, up to the first point of the component's boundary (rays cross each other and go on). Every corner of
  a part between the rays is then at most 180°, so each part is convex and simply connected, and becomes one face; a
  convex component is one face. Then every vertex of degree two between collinear edges of the same barrier (or none)
  is removed, the box boundary included. All of it uses Orient and Side only; the order along a ray uses `CompareXY`,
  the exact lexicographic order of two kernel vertices (the rounded coordinates first, which rounding to nearest keeps
  in order, then the exact ones). This is the reflex-vertex decomposition of a polygon by segments (Keil's survey,
  below), except that a ray does not stop at another ray: then no ray depends on the order of the others.
- **The order of the output.** `OrderedFaces`: the faces sorted by their corners from the lowest in `CompareXY`, each
  cycle starting there. `Realize` and `Undercuts` walk the faces in that order, so the mesh, its vertex and triangle
  ids and the first undercut cause are the same for every insertion order.

Tests: `InsertionOrder_DoesNotChangeTheResult` (in order, reversed and three seeds against the default, on the 19
stress cases: status, builds, toe lowering, rays, toe problems, slope surface and composite bit for bit with the
triangle groups, catch lines; and `SlopeMap.CanonicalProblem`: every face strictly convex somewhere and convex at every
corner, no vertex left that `Simplify` removes). The invariance over `Similarity.All()` and the determinism tests run
as before.

The composite needed one fix that the new surface showed: along a terrain edge, `edge_sequence` sorted the runs of
overlay edges by the parameter of their first vertex only. Several runs start at one point when slope edges meet at a
terrain vertex (the square pit's corner on a grid node in `NoDaylight_KeepsThePartialResult`); the run that goes on
along the edge then came first, and the partial triangle took a sliver of the next one (non-manifold composite). The
runs that start at one point are ordered by the parameter of their end.

Tests whose input no longer reaches a repair: `Merge_RaisesTheToeLowering` takes two other regular polygons (a sweep
over 3000 random ones found three that merge); `RegularPolygon_StripCreaseThroughTheOppositeVertex` no longer leaves a
cluster (no merge, so the lowering of a merge is checked only if one happens); `WavyToe_130mHigh_AtUtm` has no needle
fan (no flip; the flips keep their unit tests in `TerrainSlopeRepairTests`); `TwoPits_MeetInARidge` checks the ridge
height at (0, 0) on the surface and on the ridge's vertices, which are now only its corners.

Mutations (each must fail a test): the tie rule in the clip, the sector window near the apex, the cut mark of a split
chord, the exact x in `CompareXY` and its sign of W, the face order, the cycle start, no rays, no vertex removal, the
ray direction, the run order of the composite, the order of the undercut causes: all caught. Not caught, because no
test input reaches them: the lowest id among equal planes of one piece (`Math.Min` in the incremental maximum), the
slit tip as a reflex corner, the removal of vertices on the box boundary, and a corner approximated at the apex that
is not the apex. The numbers are in benchmarks/TerrainBenchmark/README.md; the octagon on the 1 m grid is slower (a
second realization, see there).


## 10. Mixed cut and fill, target heights (next package)

**Owner decision 2026-11-02:** accepted as below: two independent runs on T, one partial triangulation of the
terrain triangles both replace, the fill run's mirrored undercut as `ToeOvertopped`, `CutVolume` / `FillVolume` /
`CutArea` / `FillArea`, target heights as the contour of `SlopeRegion` at z = h. Implemented after #318.

### Mixed cut and fill (`Direction.Auto`, TERRAIN.md 5.5)

The final surface is the clamp max(S_fill, min(T, S_cut)), the median of the three since S_fill ≤ S_cut; S_fill ≤
S_cut holds iff neither surface fails its own toe check (appendix A of TERRAIN.md): the cut run's `ToeUndercut`, and the
fill run's mirrored undercut, reported as `ToeOvertopped` in Auto. So the cut region {T > S_cut} and the fill region
{T < S_fill} are disjoint, and each is the region of its own run on T.

Spike (2026-11-02, public API, square toe at z 0 in a terrain tilted 0.15, 1:1.5): the cut run and the fill run on T
each succeed with one open catch line from toe to toe; both lines end at the same two crossing points of the toe with
the terrain, (−2, ±10). 3 of the 36 + 21 replaced terrain triangles are replaced by both runs (the ones at the
crossings). Running fill on the cut composite instead (min(T, S_cut) as the terrain) fails (`NoDaylight`, a closed
catch line of 90 points): the realized cut surface lies λ below S_cut and the fill surface λ above S_fill, so along
the whole cut stretch of the toe, where both start at the toe height, the realized fill surface lies above the
realized cut slope in a band (2λ / (s_C + s_F) wide). Not that way.

Plan:
1. Auto needs a terrain. Run cut and fill on T as today, each with its own pieces, specs (`CutStart/End`,
   `FillStart/End`), local height limit, λ and checks; an undercut of the fill run is `ToeOvertopped`
   (GeometricallyInfeasible, or UnsupportedByMethod through the design area, as for the undercut).
2. The composite: the terrain with the replaced triangles of both runs taken out; the triangles of both runs'
   replacements; the terrain triangles replaced by both get one partial triangulation with the constraints of both
   overlays (the catch segments of both, the region boundaries of both; parity per run, a part is cut, fill or
   terrain). The two regions meet only at the toe crossings, so the constraint graph stays simple there; where the
   rounded catch points of the two runs at one crossing differ, they are snapped to one (as the composite snaps
   catch points today).
3. Outputs: `CutVolume`, `FillVolume`, `CutArea`, `FillArea` (TERRAIN.md 7), the catch lines of both (in order: cut
   then fill, each line knows its direction), `SlopeSurface` of both (groups: pieces of the cut run, then of the fill
   run), `Pieces` with their direction. `Volume` / `PlanArea` stay the single-direction totals (cut + fill in Auto).
4. Tests: the counterexample of appendix A (`ToeOvertopped`); a toe crossing a tilted terrain (both volumes against
   the separate runs, the composite manifold and a height field, the clamp checked at sample points against
   median(S_fill, T, S_cut) of the separate surfaces); a toe fully below / fully above the terrain (Auto equals Cut /
   Fill bit for bit); invariance over `Similarity.All()`; reuse; insertion order (D) for both runs.
5. Library building blocks: `SlopeTerrainOverlay` and `SlopeComposite` per run (unchanged), the composite's
   `partial` for the shared triangles with the union of both constraint sets, `ExactTriangulation2`.

Details of the composite (from `SlopeComposite.build_once`):
- `SlopeComposite` takes one overlay. Auto gives it a second, optional overlay of the same terrain B (the fill run's;
  its own slope surface A_F). The slope part runs per overlay as today (the region triangles of A_C, then of A_F,
  their groups offset by the cut run's piece count). Composite vertices are found by exact position (`compVerts`),
  so the two parts join where they share positions: the terrain vertices, and the catch points at the toe crossings.
- The terrain part: `replacedSet` is the union of both. A terrain triangle replaced by one run only takes that run's
  `partial` (the other run does not touch it: its faces there are all outside its region). A triangle replaced by
  both gets one CDT whose part is tb minus (R_C ∪ R_F): the constraints are the sub-pieces of tb's edges outside both
  regions (the two `edge_sequence`s of an edge merged by their parameter along it; a piece is terrain if it is
  outside the region in both), and inside the catch segments and the slope boundary edges next to the region, of
  both overlays. The two regions are disjoint in exact arithmetic (S_fill ≤ S_cut), so their boundaries meet only at
  points (the toe crossings), and the parity of the union is the part.
- The catch points of the two runs at one toe crossing: both are the point of the toe where T meets it. Each run snaps
  its own (`snap_catch_points`); the shared triangle needs both at one position, so the fill run's catch points
  within the snapping radius of a cut run's catch point at the toe take its position (checked on four tilted terrains: they
  are not the same, 3e-13 to 6e-13 m apart in XY, as the cut run ends λ below the toe and the fill run λ above; so the
  snap to one position is needed).
- Found on the way (a bug of the single-direction composite, on master too): a 7-gon pit (r 11, phase 0.2, 1:1.5)
  in a grid of 23 × 25 cells over (−60, 60)², tilted 0.07 towards 2.1 rad, gives `CompositeFailed` (overlapping
  composite triangles) for cut and for fill alone. Fixed first in this package, with that case as its test.
- `terrain_whole` (a terrain triangle wholly in the region, skipped) holds per run; a triangle wholly in one region
  is in no face of the other's region.
- What it does not need: an overlay of the two slope surfaces with each other (they never overlap where both are the
  final surface), or the band-only overlay.
- Order of work: (1) the second overlay in `SlopeComposite` with the merged `partial`, tested on the spike geometry
  and random tilted terrains against the clamp at sample points; (2) `Direction.Auto` in `TerrainSlopeDesign` (two
  runs, the outputs, `ToeOvertopped`); (3) target heights; (4) docs.

### Target heights (TERRAIN.md 10, the hook for berms)

`TargetHeights` (input, optional): for each height h the level line of the final slope at h, `TargetHeightLines`
(output, polylines, closed where they close). On the slope surface the level line is exact in the kernel: the line of
a face's plane with the horizontal plane at h; chained across the faces. Realized: the contour of the realized
`SlopeRegion` (with a terrain; else `SlopeSurface`) at z = h, vertex signs exact (z − h), crossing points on the
edges interpolated and rounded, chained by edge; open lines end where the region ends (catch line, lateral limit).
Tests: a square pit over flat terrain (lines at h are squares offset by h / s, closed), an open toe (lines end at the
lateral limits), heights outside the slope's range (no line), invariance, and the line lying on the slope surface
(each point on a triangle of `SlopeRegion` within the rounding).

Docs: OPERATIONS.md entries for Auto, target heights, the volumes and the 1c outputs; SOURCES.md (no new sources
for the clamp: TERRAIN.md appendix A; the contour by marching triangles, eg de Berg et al. for the chaining).

### Implemented (#320)

- **The composite fix at a toe crossing** (first two commits). Root cause: the catch point where a catch line ends on a
  toe edge, rounded off the edge's line to the outside, so the slope part's boundary from the toe vertex to it left the
  slope side, where the terrain nodes on the edge are moved to. The catch point is now moved by ulps onto the slope
  side of the edge's line, the mirror of the terrain nodes' nudge (`TerrainSlopeToeCrossingTests`: 5 cases, cut and
  fill, 2 failing before; 12 tests fail without the move). A first fix kept every node on the slope part's boundary
  edges; it broke a fill run in a UTM frame (non-manifold), so it was replaced.
- **Auto** as planned. Two points the plan did not have:
  - At a toe crossing the fill point takes the cut point's XY only. It keeps its own z: a shared vertex would be λ below
    the toe for the cut slope's exact check and λ above it for the fill slope's, which no single height is. The two
    parts meet at one XY with a step, as the slope and the terrain do along the toe.
  - The pair is found on the same toe segment, each the other's nearest, within 1e-4 of the toes' size. The distance
    is (λ_cut + λ_fill) / (the terrain's slope along the toe): 3e-13 m in the spike, 5e-8 at UTM and scale 1e-3, so
    MinFeatureSize is not the radius. A pair whose move fails keeps its own positions in the next build (its own
    set, not the partner's own snapping, which is not blocked by it).
- **Target heights:** `TargetHeights`, `TargetHeightLines` (`TargetHeightLine`: height, points, closed), the level lines
  of `SlopeRegion` (else `SlopeSurface`), oriented with the higher side on the left.
- **Not changed, noted:**
  - MinFeatureSize, 2 (8 ulp(max |xy|) + 4 ulp(2 max |z|) / s_min) / δ, is absolute: about 1.5 cm at UTM with the
    default δ = 1e-6. A toe of a few cm (the test geometries at scale 1e-3 in UTM) is below it, and the composite or
    the region fail there, on master too. The frame tests of this package use δ = 1e-4.
  - A toe corner on a terrain vertex, made a near-coincidence by a rotated UTM frame, gives overlapping composite
    triangles on master too (a square pit with its corners on the grid nodes); its own item.
  - Follow-up item: the octagon on the 1 m grid is slower since #318 (6.1 → 8.8 s): its large slope faces make the
    exact check of the clipped region triangles raise λ, which costs a second realization with overlay and composite
    (benchmarks/TerrainBenchmark/README.md). Taken up when a real case is slow (TERRAIN.md section 11, order after
    phase 1).

