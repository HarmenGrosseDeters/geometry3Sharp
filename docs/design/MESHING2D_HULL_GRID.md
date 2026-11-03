# Meshing2: hull edges on the grid (design note for the owner)

Status: **decided (owner, 2026-11-02; section 7), built (section 8).** Written after the reference comparison found OffGrid vertices on the Herne tiles at snap
0.01 (ALKIS parcels and buildings, three tiles, not committed). Refinement on the grid (`MESHING2D_SNAP.md` section 3g, #277) was
designed and fuzz-tested with outer rings only. A **hull domain** has no outer ring (D13: the convex hull of the input is the
boundary). With a hull domain, refinement on the grid has not been specified, and it does not work. This note collects what was found,
the options, and the questions for the owner. It proposes the acceptance test that the change must pass.

## 1. What happens today (master)

- `QualityMesh2.Refiner.ConstrainHull` makes each hull edge of the triangulation a constraint with its own source (`Source.Hull`).
  With a snap tolerance, its ends are snapped points, so the hull is the hull of the **snapped** input.
- `GridSplitPoint` splits a hull piece like a segment piece. A hull edge has no input segment, so the "envelope" is the set of pixels
  that the snapped hull edge crosses. That set lies on both sides of the edge, half of it outside the hull.
- `ValidGridSplit` checks the new pieces against nearby vertices and constraints, which it finds with `TrianglesNear(e / 3, ...)`.
- The oracle judges the boundary against the hull of the **input**, with the tube (the pixel width). Its grid exemptions
  (`GridUnsplittable`, `CircumcentreBehindGridUnsplittable`) recompute envelopes from input segments only.

## 2. What was found (reductions in the WIP branch, not pushed)

The defects come in layers. The oracle stops at the first violation kind, so each one hid the next.

1. **OffGrid** (the reported finding). For a hull piece, the half-edge `e` can lie on the ghost side. `TrianglesNear` then started in
   the ghost triangle, skipped it and returned nothing, so `ValidGridSplit` checked nothing. The new pieces crossed a segment at a
   small angle, and the builder made an unrounded crossing vertex.
   - Reduction: three points (1276.182, 991.666), (1274.32, 997.353), (1274.032, 998.26) with two segments, snap 0.01, 20°.
   - Fix: start from the real neighbour. This fix alone only moves the failure, see 2 and 3.
2. **The boundary moves out around hull corners.** Split points in pixels outside the hull move the boundary out by up to a pixel's
   diagonal. At a corner, two outside points can enclose the corner vertex, which then becomes interior. The boundary edge between
   them cuts the corner (BoundaryNotConstrained). It also lies more than the tube from the input hull (TriangleOutsideDomain).
3. **The snapped hull is not the input hull.** A hull corner that snapping moves inside is skipped by the snapped hull's edge (1.4 mm on
   herne_371800). The mesher is right by D14, since snapping comes first, but the oracle did not know the snapped hull.
4. **Hull parts narrower than a pixel** (a separate, pre-existing defect, section 6).

## 3. Measured

**Herne:** three tiles, each at q20, at q20 with snap 0.01, and at q30 with snap 0.01 (all at Steiner limit 200 000).

| Code | Runs that pass the oracle |
|---|---|
| master | 2 of 9: q20 on two tiles (all three with the oracle fix of #309); all six snap runs fail (OffGrid first) |
| WIP (inner band, below) | 9 of 9 |

**Fuzz:** the proposed acceptance test of section 5, 24 seeds × 8 similarity frames = 192 runs, each frame 24 runs.
- The columns:
  - master is judged by master's oracle;
  - the others by the WIP oracle, which knows the snapped hull (rule O-H1 in section 4);
  - "+ ghost fix" is master's mesher with only the fix of 2.1;
  - WIP is all rules of option B below.

| Frame | master | master's mesher, WIP oracle | + ghost fix (option A, partly) | WIP (option B) |
|---|---|---|---|---|
| identity | 14 | 14 | 16 | 9 |
| utm | 14 | 14 | 15 | 10 |
| scale 2^-10 | 14 | 14 | 16 | 9 |
| scale 2^10 | 14 | 14 | 16 | 9 |
| scale 1e-3 | 15 | 15 | 16 | 9 |
| scale 1e3 | 15 | 15 | 16 | 10 |
| rotated | 12 | 17 | 19 | 10 |
| rotated, 1e-3, utm | 21 | 21 | 23 | 15 |
| **All 192** | **119** | **124** | **137** | **81** |

What the failures are:
- master: 54 AngleBelowBound, 15 BoundaryNotConstrained, 2 NotDelaunayGlobal, 2 TriangleOutsideDomain.
- With the ghost fix: 46 AngleBelowBound, 9 NotDelaunayGlobal.
- WIP: 93 AngleBelowBound, 18 NotDelaunayGlobal. The inner band makes hull pieces unsplittable; the oracle has no exemption for them,
  and refinement gets no other way to meet the bound.

The first, smaller fuzz (12 seeds, gentle slopes, identity only) gave "5 fixed, 3 worse" for the band. The wider fuzz contradicts that,
so the band is not proposed. The ghost fix with O-H1 is the best start. The angle failures left there are refinement near hull
edges that the oracle cannot excuse; Q-H1 and Q-H3 below must close them.

## 4. The questions, with options

### Q-H1: where may a split point of a hull edge go?

- **Option A, crossed pixels (today, plus the ghost fix).**
  - The boundary may move out by up to about 0.7 px from the snapped hull, which is within the tube of the snapped hull.
  - It needs two oracle rules:
    - O-H1: the boundary is judged against the input hull and the snapped hull, with the tube.
    - O-H2: a boundary edge whose ends are near different hull edges may cut a corner that lies within the tube of that edge.
  - It needs one mesher rule: a hull chain must not enclose a hull vertex. Route the chain through every vertex whose pixel it
    crosses, as snap rounding does with hot pixels (Hobby 1999; Halperin and Packer 2002).
- **Option B, an inner band (the WIP).**
  - Pixel centres on the edge or on its domain side, closer to the edge than a pixel. The boundary never moves out.
  - It needs these rules:
    - tag cutting (Q-H2);
    - no vertex inside the triangle that is cut off;
    - no split point at a vertex already joined to the piece by a constraint (otherwise the chains merge at a tip);
    - a vertex on the edge itself is allowed;
    - a fallback through hot pixels.
  - Measured: more unsplittable pieces, worse on the fuzz (section 3).
- **Option C, snap-round the hull edges in `ConstrainHull`.** Treat each hull edge of the snapped input as an input segment of snap
  rounding (hot pixels, then the restore step of #277). Hull pieces then have a proper envelope and origin, and the existing
  segment machinery applies unchanged, including the oracle's envelope exemptions once it knows the snapped hull.
  - This is the most uniform option. Its cost: hull chains bend by up to half a pixel, like every other chain.

**Recommendation:** C, with the ghost fix and the oracle rules O-H1 and O-H2. It reuses #277's guarantees instead of inventing hull rules.
A is the fallback if C's restore step does not terminate on hull chains, which has to be measured. Both start from what measured best,
the ghost fix with O-H1 (137 of 192). B is dropped (81 of 192).

### Q-H2: tag cutting (only for option B)

- A split point inside the hull cuts off the triangle u-q-w. When q is a new vertex, that is one new triangle, tagged outside from the
  lifted piece.
- When q is an existing vertex, the region consists of older triangles that keep their domain tag. The WIP floods that region,
  bounded to the triangle u-q-w.
- Options A and C never cut, so the question disappears with them.

### Q-H3: the oracle's grid exemptions for hull pieces

- `GridUnsplittable` and `CircumcentreBehindGridUnsplittable` know only input-segment envelopes. A hull piece needs the envelope that
  the chosen option uses: for C, the snapped hull edge as a segment; for A, the crossed pixels; for B, the band.
- Every relaxation needs a rule with its spec basis (D13, D14, O11) and a self-check that accepts and one that rejects, both
  mutation-checked.
- An independent review must check whether the relaxation could hide a real mesher defect. This was the coordinator's condition.

### Q-H4: what does D14 promise for hull parts narrower than a pixel? (section 6)

## 5. Acceptance test (proposed)

- **The fuzz.** For each seed, a hull edge of 64 m with slope 0.0037, 0.005, 0.0073, 0.011, 0.13 or 0.41, a third hull point, and 3 to 8
  input points 2 to 9 mm inside the edge on the grid of 0.01. Minimum angle 20° or 30°, snap 0.01.
  - Each input runs in the eight frames of `Similarity.All()`, the tolerance scaled with the frame.
  - Every run must pass the oracle, and the 2^k frames must give the same mesh bit for bit.
- **The Herne tiles** from `G3_HERNE_DIR`: all nine runs pass.
- **The existing suite**, unchanged: golden rows and the fuzz of #277.

## 6. Separate item: hull parts narrower than a pixel lose their tip vertex (pre-existing)

- **Reduction:** a polyline (1053.72, 595.965), (1033.244, 661.372), (1022.714, 700.24) with two segments, snap 0.01. This is
  herne_381000 moved by (-380000, -5710000). Its hull is a sliver with a tip of 0.83°.
  - At M2 the oracle accepts it.
  - At M3, even with a minimum angle of 0 (no refinement), the tip vertex (input 0) lies in no triangle (VertexUnused).
- **Same class:** collinear points that snap to a zigzag of one pixel (four points on a line rotated by 31° and 17°). The chains of both
  sides of the hull meet.
- **Question for the owner:** should a hull tip narrower than a pixel keep its vertex? It would keep it through a sliver triangle,
  judged with the area tolerance. Or may it collapse? Then the oracle would accept an unused input vertex that snapping put on a
  collapsed part, and report it.
- Listed as Q17 in `MESHING2D_SPEC.md` section 4.

## 7. Owner decisions (2026-11-02, relayed by the coordinator)

- **Q-H1: option C.**
  - `ConstrainHull` snap-rounds the hull edges as input segments (hot pixels, then #277's restore), on top of the ghost fix and the
    snapped-hull rule O-H1.
  - Option A (crossed pixels) is only the fallback, if C's restore does not terminate on hull chains. That is measured first.
- **Q-H2: dropped.** C never cuts.
- **Q-H3:** the oracle treats each snapped hull edge as a segment envelope. Every relaxation (O-H1, O-H2 and any new one) needs:
  - a rule with its spec basis (D13, D14, O11);
  - self-checks that accept and reject, checked by mutation;
  - a review focused on whether it could hide a mesher defect.
- **Q-H4: (b).** A hull part narrower than a pixel may collapse, consistent with D14 and the semantics of snap rounding. The oracle
  accepts an unused input vertex only on a collapsed part, and the mesher reports it in the diagnostics, like #277's collapses of rings
  and segments.
- **Acceptance:**
  - the 192-run fuzz of section 5, its pass count reported against today's 119 (master) and 137 (ghost fix with O-H1);
  - the nine Herne runs;
  - no golden row changes without a reason.
- **The implementation** is its own PR, with the label `bugfix` where a test fails on master.

## 8. As built

**Mesher** (`QualityMesh2`, `SnapRounding2`, `ConstrainedDelaunayMesh2.Builder`):
- With an explicit tolerance, a hull domain, and refinement on, `Compute` appends the exact convex hull edges of the input (collinear
  points left out) as segments before snap rounding.
- `SnapRounding2.SegmentChainStart` locates their chains, and the builder makes them hull sources (`HullSegmentsFrom`/`To`).
- `ConstrainHull` then adds no hull edges of its own; it only floods what lies outside the chains.
- Splits of hull pieces use the envelopes of the true hull edges, as segment pieces do (`snap.Origins`).
- `TrianglesNear` starts from a ghost's real neighbour (the OffGrid finding).
- Vertices of the hull chains left in no triangle are reported in the diagnostics: "snap grid: N vertices of the hull chains in no
  triangle".

**Oracle**, rule O-H4 (`CollapsedHullTip`):
- **Rule:** with an explicit tolerance at M3 (D14) on a hull domain (D13), an unused input point is accepted only if, at some corner
  of the input's hull, both hull edges pass one pixel, and either
  - the point is that corner, and a used output vertex lies in such a pixel, or
  - the point's own pixel is such a pixel, so the point lies on the shared chain.
  Snap rounding routes both chains through that pixel's vertex, so the tip between has no area (Q-H4).
- **"Passes a pixel"** is decided as for grid splits: the edge crosses the pixel's interior, a hair smaller, or passes its centre
  (half-open pixels). An edge that only touches a pixel's boundary does not count (review: a 90 degree corner on a pixel boundary must
  stay judged). A point without its own output vertex (`map < 0`) is never excused.
- **Self-checks:** accept (a collapsed tip), and reject (the next vertex's pixel passed by one hull edge only; a corner on a pixel
  boundary; no tolerance).
- O-H1 (judging the boundary against the snapped hull as well) and a separate hull envelope (Q-H3) were not needed: with the hull edges
  snap-rounded as segments, the existing segment rules apply.

**Measured** (all runs judged by the oracle):

| | master | built |
|---|---|---|
| Fuzz of section 5: 24 seeds × 8 frames (`Fuzz_PointsInsideASlopedHullEdge_PassTheOracle`) | 119 of 192 (Python generator) | 192 of 192; the 2^k frames bit for bit |
| Herne, nine runs | 2 of 9 | 9 of 9 (with #309) |
| Meshing2, QualityMesh2 and SnapRounding2 suites | | all pass, no golden row changed |

**Q17, domains narrower than a pixel everywhere (decided 2026-11-03, within the owner decision of 2026-11-02):** a polyline whose
hull is about 7 mm wide, on a grid of 1 cm, lost whole segment chains (SegmentMissing) and corners (VertexUnused): a collapse beyond a
corner tip, which O-H4 does not cover.

- **Two mechanisms:**
  - Snapping: snapped corners fall on one line, so the part has no area.
  - Refinement pinches: a grid split point on one side's chain lands in a pixel that the other side's chain passes.
- **Neither is a defect:** without refinement the probe shows the first; with it, the segments that had a chain lose it at the pinches.
- **Prior art:** snap rounding keeps topology "up to collapsing of features" (Guibas and Marimont 1998). Iterated snap rounding (Halperin
  and Packer 2002) and stable snap rounding (Hershberger 2013) keep no sub-pixel feature either.

**Oracle**, rule O-H5 (`CollapsedStretches`, `ChainAcrossCollapsed`, `OnCollapsedPoint`), only with an explicit tolerance:
- **Collapsed stretch:** a stretch of an input piece is collapsed where the domain across it is narrower than a pixel:
  - a piece of an input segment: boundary on its left and on its right whose distances add up to less than a pixel (the side of an
    edge is that of its nearest point. An edge whose nearest point lies on the piece's line serves both sides where that point is an
    end of the edge, a tip or corner, or the piece's point itself; neither where the edge passes across the line ahead of the piece,
    a T-junction (review 3: a stub 0.4 long across a pixel line was excused)). Exactly:
    d1 + d2 < p² and 4·d1·d2 < (p² − d1 − d2)² for the squared distances d1, d2;
  - a piece of a boundary edge: another boundary edge closer than a pixel whose nearest point lies on the domain's side of the piece
    (the side of a point 2^-40 of the piece's length beside its middle that lies inside), or on its line. An edge on the outside side
    (a slit of the outside between two walls) does not count;
  - the source edge (the whole input segment or boundary edge, not only the piece) never counts against itself, nor does an edge on
    the piece's line (review 3: a segment on the bottom edge of a wide square was excused, that edge serving both sides). A segment
    with the domain on one side only (on a boundary edge) is judged as a boundary piece; the sides come from points 2^-40 of the
    piece's length beside its middle, and where the side changes along the piece the middle decides (stricter, never looser).
  - It is decided exactly per stretch between the piece's pixel crossings, for one and the same edge (or pair of edges) at both ends: a
    distance to a segment, and a sum of two, is convex along a line.
- **Why two-sided (review 2):** a first form excused a stretch closer than a pixel to any one other edge. That excused gaps snap rounding
  does not create, on parts of a wide domain: a constraint 0.7 pixel from one edge of a 20 pixel square (E1, also diagonal 0.9, E1b), a
  constraint 0.8 long into a wide square from its bottom (a T-junction, E6), an outer edge beside a hole that shares a stretch of it
  (E2: the piece, not the source, was excluded), and the walls of a slit of the outside 0.5 wide (E8). All five are reject self-checks
  now: nothing in them may be excused as collapsed.
- **Corners:** at a corner shared with the piece, the adjacent edge comes closer than a pixel, but two chains from one hot pixel
  part there.
  - Near the shared end, the adjacent edge counts only if the wedge between the two is still narrower than a pixel where the piece
    leaves the corner's square of half-width two pixels, that is below about 30° (2 sin θ < 1).
  - The zigzag's tip, about 0.2°, collapses; a 90° or 60° corner does not.
- **Diagonal parts:** parts between one and 1.4 pixels wide that a diagonal grid could collapse are judged strictly: their missing
  chains are reported.
- **Segments:** the chain search may cross a collapsed stretch without a chain, and only there. A stretch of a piece that is not
  collapsed still needs its chain. A stretch is joined to every vertex near the piece whose closed pixel meets it: stretches begin
  and end at pixel boundaries, and the chain reaches the hot pixel at a corridor's mouth half a pixel before (found with a dumbbell,
  two squares joined by a corridor 0.25 wide, which a join by projection onto the stretch rejected).
- **Points:** an unused input point is accepted only when every input constraint and boundary edge that ends at it is collapsed along
  its whole length. A point with one constraint on a part that has area keeps its violation; so does a corner whose edges have area.
- **Inputs:** the stretches come from the input and the grid alone, never from the output.
- **Cost:** O(E) to select the boundary edges whose box, widened by a pixel, meets the piece's; then per pixel crossing a box test
  against each of them, an exact distance only within a pixel's box, and per stretch the pairs of a left and a right edge within a
  pixel. An unused point looks at the O(S + E) edges ending at it. All of it runs only on the failure paths (a piece without a chain,
  an unused point), never for the pieces of a valid mesh: no O(V·S) on the normal path.
- **Rejected first forms:**
  - Pixel coverage of the input piece: sub-millimetre gaps on the hull caps, where a chain runs straight between hot pixel centres.
  - A 3×3 pixel neighbourhood: it let through the middle of a corridor three pixels wide.
  - The snapped images of the edges (pixel centre to pixel centre): with the distance rule they covered nothing more; dropped, so
    fewer excuses.
- **Self-checks:**
  - Accept: a corridor 0.25 pixel wide, with its segment and its far points.
  - Reject:
    - a corridor three pixels wide whose content is missing, the segment along its middle in particular;
    - the collapsed corridor without a tolerance;
    - a point with one constraint on the corridor and one into the square;
    - an unused corner whose edges have area;
    - a pixel only touched by an edge;
    - the review's cases E1, E1b, E2, E6 and E8 (above), with nothing excused; E2 complete is accepted; review 3's segment on the
      edge of a wide square and stub across a pixel line;
    - a segment through the dumbbell's corridor without its chain in one square (either way; accepted with both chains).
  - Direct checks of the stretches: a 90°, 60° and 35° corner not collapsed, a 25° corner and a 1° tip collapsed, a corner exactly
    a pixel wide where the piece leaves the guard's square not; a segment in a corridor 0.25 wide collapsed, 1.5 wide not, exactly a
    pixel wide not; distances 0.45 and 0.5 collapsed, 0.45 and 0.6 not (their squares sum below a pixel); a segment 0.3 or 0.7 from
    one edge of a wide domain not; a boundary piece beside an outside slit not, with an edge 0.5 away inside it is, exactly a pixel
    away not.
  - Mutations: 19 mutants of the rule, all killed except two equivalent ones: the tolerance check in the point rule (the stretches
    are empty without one) and `rest > 0` against `rest ≥ 0` in the exact sum (rest 0 needs 4·d1·d2 < 0). Two survivors pointed at
    code that could not matter, start and goal nodes for stretches at the piece's ends (an end without a vertex is a violation of
    its own unless the whole piece is collapsed); it is removed, which is stricter.
- **Reports:** the counts appear as "on collapsed parts: N segment pieces, M input points".

**Mesher:** the diagnostics report, apart, the input segments with a piece that has no triangle on either side already after
snapping (taken before the carve of the outside), and those whose piece lost its triangles in refinement; only pieces on an edge
that also carries a ring or hull edge count (the part collapsed onto its boundary), so a free segment outside the domain or in a
hole is not reported (review 3). The mesher does not decide
"narrower than a pixel"; the oracle does.

**Follow-up (not done):** whether refinement should avoid Steiner points in pixels that two chains pass, since those make the pinch.
