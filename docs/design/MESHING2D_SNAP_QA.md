# 2D meshing: snap-rounding answers from the owner's Triangle.NET fork (clean-room Q&A)

Owner decision of 2026-11-01: the owner implemented snap rounding in his own fork of Triangle.NET
(https://github.com/HarmenGrosseDeters/Triangle.NET), privately, with Claude; the commits are his own work. A separate
comparison session (the dirty side) read only those commits and answered the questions of
[MESHING2D_SNAP.md](MESHING2D_SNAP.md) §3d in specification terms: behaviour, cases, results, parameter values, test
inputs in words. No code, pseudocode, data structures, routine decomposition or names. The coordinator checked the
report against these rules before the meshing session received it. The meshing session never reads the fork. This
document is a behavioural input, like a user report; it is not a source of code.

Provenance of the report: commits ccee980..504ab65 of the fork (after upstream fa6f2fd), mainly the snap rounding,
grid, wedge and noder commits of 2026-10-24/25, their tests, and the notes in a since-deleted robustness-analysis
document (read from history). Report date 2026-11-01. "Inferred" marks what the fork did not test or write down.

## Answers

**1. Variant and grid.** Iterated snap rounding with hot pixels (the notes cite Hobby 1999 and Halperin–Packer 2002). It has
no restore step and no spreading step. Each round first routes every segment through the hot pixels of the vertices that
already exist. New intersections are computed only in a round where no segment needed rerouting. Each intersection is
rounded to the nearest grid point, and an existing vertex at that grid point is reused. Rounds repeat until nothing changes.
A segment that has been split is never formed again; this rule was added to stop oscillation. The iteration cap is a
parameter (default 100), and there is also a cap on new vertices that grows with the input size. Pixel = one grid cell of
side g, centred on a grid point. The grid is anchored at the coordinate origin, so it is independent of the data and its
bounding box. For decimal g (0.1, 0.01, …), a rounded coordinate equals the double nearest to the decimal value. Ties are
inferred to go to even (the platform default). Snap rounding also runs automatically as the last step of the ordinary
tolerance noder, when that noder fails to converge at every tolerance; g is then the largest tolerance.

**2. Contact rule.** Closed. A vertex's pixel counts as touched by a segment when the closed square meets the segment's
supporting line, the vertex's projection falls strictly inside the segment, and the vertex is not one of the segment's
endpoints. Sides and corners count, and the rule is symmetric. Hobby's half-open rule was not used, and neither the code
nor the notes record a comparison between the rules.

**3. Input on the grid.** A point already on the grid stays exactly where it is, as the same vertex. A point off the grid
moves to the nearest grid point. Points that round to the same grid point are merged: the first one is kept, and its label
is taken over if it had none. A segment whose two ends merge is removed.

**4. Separation.** The documentation claims: no crossings, and every vertex at least half a cell (g/2) from every segment
not incident to it. This comes from iterating hot-pixel routing to a fixpoint, not from spreading. The tests check only
that no segments cross and that no vertex lies exactly inside a segment. **The g/2 bound itself is not tested.** If the cap
is reached, the result falls back to tolerance noding with tolerance g. In that case not all vertices are on the grid, and
the guarantee does not hold.

**5. Drift.** The documentation claims input segments bend by at most half a cell. The input vertex moved by the initial
rounding moves at most half a cell per axis (≤ 0.71 g). Drift over several rounds was not measured. Drift is not reported
to the caller. Two measures limit drift during meshing. First, split points are projected onto the original input segment
before rounding, so errors don't add up over repeated splits. Second, the final pass may move a vertex to another corner of
its cell, up to about 1.41 g. That pass reports only how many vertices it could not put on the grid. Wedge merging is
bounded by the wedge tolerance, measured against the original input segment, even for chains of wedges.

**6. Collapse.**
- **Hole smaller than the tolerance:** its contour collapses and its segments disappear. A hole or region marker within one
  cell of a collapsed segment is dropped. Before this fix, a surviving hole marker emptied the whole surrounding domain.
  Test: 10×10 square with a 0.01 hole, tolerance 0.1; the area stays above 99.
- **Hole ring that degenerates to collinear points:** its edges become plain segments, and it removes nothing.
- **Two nearly coincident borders:** they are merged by hot-pixel routing (test: two parcels whose shared boundary differs
  by 3 mm, 1 cm grid).
- **Narrow wedges:** a separate optional rule handles these. A segment that leaves another segment at under about 10° (a
  fixed, documented limit), and whose far end is within the wedge tolerance of that segment, is merged into it. Tried and
  dropped: merging only the part of a long wedge where the lines are closer than the tolerance. The tip just moved and the
  small angle stayed; on real data this was neutral to slightly worse.
- **Polygons collapsing to lines or points:** they are not reported. Degenerate pieces are removed silently, and edges that
  survive are kept as segments.

**7. Failures and how they were resolved.**
- (a) **A CDT test input with long edges crossing a polyline many times at almost 0°:** the tolerance noder never
  converged, so snap rounding was added as a last resort. The first version computed intersections first, and the vertex
  count exploded from 244 to 12,634 in four rounds. Routing through existing hot pixels first fixed this, and inputs that
  converged before gave the same results.
- (b) **Snap cycles at large tolerance:** a vertex snapped into a segment whose piece then re-captured the original
  endpoint. Fixed by never re-forming split segments.
- (c) **Crossings merged into an endpoint:** these were wrongly marked as "split" and blocked later snapping. A 0.0002°
  wedge survived and used up the whole Steiner budget. One tile went from 203,175 triangles to 24,355 after the fix.
- (d) **Refinement on the grid:** relocating Steiner points cycled, so relocation is switched off. At small input angles
  refinement cascaded down to the grid (random data: 360,000 points instead of 1,500); fixed with an angle-refinement floor
  of two cells. Subsegments with no valid grid split were common; triangles that encroached on them were dropped for good,
  leaving bad triangles with edges up to 40 m. They are now inserted anyway.
- (e) **Rounding a full-precision mesh afterwards:** this flipped about 1 % of triangles (33,858 / 92,501 at 20° / 30° on
  the 201 tiles). With the grid option, none flip.

Re-snapping a result (idempotence) was not tested. Inferred: a second pass changes nothing, because on-grid input stays
put. Polygon validity is not checked beyond "no crossings".

**8. Cost.**
- **201 cadastral tiles, 1 cm grid + 3 cm wedge:** 66 s total for 2.97 M triangles at 20°; 102 s for 6.59 M at 30°.
- **One tile at 30°:** 1155 → 846 ms after skipping, on the grid, a relocation search whose result was discarded (24 % of
  the time). Later storage work brought it to about 430–550 ms.
- **Where the time goes:** mostly quality refinement. Snap rounding was not timed separately, and no iteration counts are
  recorded.
- **Other runs:** a noding pass that was quadratic on long parallel segments (20,000 lines, 4.7 s) was fixed. A Natural
  Earth layer with 148,000 points finishes in 4.3 s once a suitable snap tolerance is set.

**9. Test inputs.**
- 30–60 random segments in a square of side 1 or 10, grid 0.01, seeds 1–3, quality 25–33°.
- Two adjacent parcels with a 3 mm mismatch on the shared boundary.
- A 10×10 square with 8 pairs of nearly coincident lines a few mm apart. Without the grid, rounding afterwards flips
  triangles; with it, none flip.
- 2000 random points in the unit square, grid 0.01, for the final pass.
- A locked break line already on the grid, which stays unchanged.
- Stacked squares offset by 1e-6, with one iteration allowed, to force the fallback.
- A CDT stress input: long edges crossing a polyline many times at almost 0°.
- Corpus: German cadastral data (ALKIS; parcels, buildings and land use) of four cities, 201-250 tiles each, 1 cm grid,
  3 cm wedge, 20° and 30°. All runs were valid; the share of bad triangles was 0.2–0.7 %, and 2.2 % for one city.
- Natural Earth US counties: a boundary runs 5 km out and back about 0.06 mm apart. It is resolved only by a data-sized
  snap tolerance.

**10. Interaction with the mesher.**
- **Order:** snap rounding runs before triangulation, after the optional tolerance and wedge noding.
- **During refinement:** Steiner points are rounded to the grid. Segment split points are projected onto the input segment
  and then rounded. A split is rejected if it would create a flat or inverted triangle, or if it lands on an endpoint; that
  subsegment is then never split. Triangles whose shortest edge is under two cells are exempt from angle refinement.
- **After refinement:** vertices still off the grid move to the nearest grid point, or to another corner of the cell, but
  only if no triangle around them flips. The mesh is then no longer exactly constrained Delaunay.
- **Merges:** refinement never undoes a merge (inferred: it only adds vertices).
- **Slim triangles:** refinement does meet slim triangles left by snapping, and they stay. Residue at 30° on one city:
  2.22 %, mostly in 2–5 cm strips between nearly parallel lines and at true small input angles. At most 0.19 % / 0.30 %
  (20° / 30°) of triangles might be avoidable.

## Coordinator notes for the meshing session (differences from the approved D14 design)

- The fork uses iterated SR with a **closed, symmetric** contact rule and **no restore**; D14 keeps SR's contact rules
  (Hershberger 2025) and adds SRR's restore. The fork's g/2 separation is claimed but untested; ours must be tested.
- The fork's grid is anchored at the origin, as O11 assumes; keep it so.
- Failures worth turning into our own gates (inputs described in words above): the vertex explosion when intersections are
  computed before routing through existing hot pixels (7a), snap cycles (7b), refinement cascading to the grid at small
  input angles (7d; the fork's floor is two cells), unsplittable subsegments (7d), silent removal of collapsed pieces (6;
  our strict mode reports them), and a hole marker near a collapsed hole emptying the domain (6).
- Steiner points on the grid during refinement (10) is a design question for M3 + snap, not decided by D14.
