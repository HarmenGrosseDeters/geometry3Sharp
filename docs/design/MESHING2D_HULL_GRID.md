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
- **Owner decision 2026-11-03 (strict):** the first may collapse; the second is a defect. The refinement must not collapse what the
  rounding of the input keeps; the oracle accepts a collapse only where the input-only image collapses (rule (ii') below).
- **Prior art:** snap rounding keeps topology "up to collapsing of features" (Guibas and Marimont 1998). Iterated snap rounding (Halperin
  and Packer 2002) and stable snap rounding (Hershberger 2013) keep no sub-pixel feature either.

**Oracle**, rule O-H5 (`Image`, `Snap`, `ChainAcrossCollapsed`, `OnCollapsedPoint`), only with an explicit tolerance. A part is
collapsed where snap rounding leaves it without area, decided on the snap-rounded arrangement itself:
- **Hot pixels:** the pixels of the input points, of the proper crossings of the input's edges, and of every output vertex
  (refinement's grid points are vertices too, and every vertex makes its pixel hot).
- **Images:** the image of an edge is snap rounding's, iterated: the first pass reroutes the input segment itself through every hot
  pixel it contacts (Hobby 1999's half-open pixels, the mesher's rule in `SnapRounding2.Meets`: the open interior, the closed left and
  bottom sides, of the corners only the bottom-left one), in order of the exact entry parameter, then exit; each further pass reroutes the new links between pixel centres through the hot pixels they pass, until
  none is left (Halperin and Packer 2002). Terminates: the image only gains hot pixels, of which there are finitely many. Work per pass
  and link: the smaller of its pixel count (an integer walk between lattice centres) and the hot pixel count (tested in its box).
- **Snapped domain:** rule D5 on the images of the input's rings (a hull domain: the hull's image). A link is collapsed where the
  snapped domain has no interior just beside its middle on either side (2^-40 of its length off it; no other link between pixel
  centres comes that close below 2^30 pixels of length).
- **Segments:** the chain search may cross the collapsed links of the source's own image without a chain, and only those. Each pixel
  of the image is a node, joined to the next where the link is collapsed; the ends of collapsed links join the candidate vertices at
  their pixels' centres.
- **Points:** an unused input point is accepted where every link at its pixel is collapsed, or, with no link there, where its pixel's
  centre is not inside the snapped domain.
- **Rule (ii'), the input's own rounding (owner 2026-11-03):** every excuse must also hold in the images with the input's hot pixels
  alone (input points and crossings). For a segment piece, the full image and the input-only image of the same source are matched at
  the pixels they share, in order (both ends are shared); a link of the full image is excused only if it is collapsed there and every
  link of the input-only image between the same two shared pixels is collapsed in the input-only rounding. A point is excused only if
  its pixel is collapsed in both. Output vertices still reroute (the first pass, `ChainPassesHotPixel`), but only across stretches the
  input's rounding collapses; they never create a collapse. Built as a second lazy state, the same as the first where no output vertex
  lies beyond the input's hot pixels; cost on Herne +3 % oracle time over master. Review 5's S-C (a Steiner vertex (12, 0) on the
  bottom of the triangle (0, -0.45), (20, -0.45), (20, 0.55) that the hypotenuse then contacts, the mesh dropping 6 of 10 px²) is
  rejected. The report counts apart (iii) the segment pieces whose chain search needs a stretch that output vertices rerouted (it
  fails without those bridges), and the points whose pixel has other links in the two states.
- **Post-review of (ii') (B1, fixed):** a first form probed the input-only domain beside the middle of the full image's link. A
  rerouted link can run outside the input's band while the band keeps area next to it: the wedge (0, 0), (40, 4.6), (40, 5.6) with a
  Steiner vertex at (10, 1), both edges then on the link (0, 0) - (10, 1), was accepted although the input's rounding keeps 1.25 px²
  for x < 10. Matching the stretches rejects it (a self-check). An input-only link crossing the probe point had the same effect (S2).
  The converse is a self-check too: a Steiner vertex at (15, 3) in the sliver (0, 0), (20, 4.55), (20, 5.45) opens 7.5 px² that
  the input's rounding collapses, and a chain missing there is rejected (a link is excused only if collapsed in both roundings). A
  re-review found no blocking issue; it searched 500 000 random segments for shared pixels in another order in the two images and
  found none.
- **Interim (c) for SRR's restore step (coordinator 2026-11-03):** D14's snap rounding includes the restore step of Snap Rounding with
  Restore (Belussi, Migliorini, Negri and Pelagatti 2016), whose hot pixels belong to the rounding of the input, not to refinement. The
  input-only state does not compute them yet, so below M3 (no refinement: every output vertex comes from snapping) the two states are
  one. Measured before (c): an M2 fuzz of 3000 star polygons gave 18 false rejections (0.6 %), all now acceptance tests
  (`QualityMesh2CollapsedPartsReportTests.Oracle_AcceptsTheRestoreStepOfSnapping*`). Gaps:
  - At M2 any output vertex beyond the input's points and crossings (a faulty restore pixel, a vertex at a chain end near a
    constraint) can create a collapse that is not reported; the wedge below, judged at M2, is accepted (a pinned self-check).
  - At M3 the restore pixels count as refinement. Of the 3000 seeds of `StarPolygonFuzz` (the test harness's generator, for reuse)
    at 20°, 9 are false rejections (0.3 %, `Oracle_RestoreStepAtM3_KnownFalseRejection`), and 3 are false acceptances (0.1 %,
    `Oracle_RestoreStepAtM3_KnownFalseAcceptance`): the input's rounding without the restore pixels collapses a spike tip that its
    real rounding keeps. Both measured by adding the library's snap pixels to the input-only state outside the oracle.
  - Also at M3: 188 real refinement pinches (6.3 %; the input's own
  rounding keeps area there, eg the spike tip of (15.9, 8), (3.1, 9.9), (2.9, 9), (6.5, 6.2), whose edges both pass pixel (15, 8),
  hot only by a grid point), the mesher's bugfix below; 5 of them, each reduced to a single thin triangle, are pinned
  (`RefinementPinchesASpike_KnownInvalid`). The gaps close with the oracle's own restore step (a), its own PR. Basing the
  input-only state on the library's `SnapRounding2` (b) is out: the oracle must not trust the code it checks.
- **Why images, not distances (review 3):** "narrower than a pixel" is neither necessary nor sufficient for a collapse (snap
  rounding also collapses some parts wider than a pixel, eg the strip of review 4 without its point). A corridor 0.9
  wide whose walls (y 10.05 and 10.95) snap to rows 10 and 11 keeps its area; an oblique corridor 0.85 wide snaps onto two diagonals.
  The distance rule (two-sided sums, a corner guard, three review rounds) excused both, and rejected correct outputs (a free point in
  a collapsed corridor, review 3 S1). The images decide both by definition; no reference defines the collapse by distance (Hobby
  1999; Guibas and Marimont 1998; Halperin and Packer 2002; de Berg, Halperin and Overmars 2007).
- **Snap rounding's invariant on the output (`ChainPassesHotPixel`):** no subsegment passes a hot pixel other than its ends' (Hobby
  1999: a chain runs through every hot pixel it meets). Because every output vertex is hot, a Steiner point placed in the pixel of a
  chain that does not run through it is reported: an extra vertex cannot buy an excuse unless the chains it lies on are routed
  through it, and even then only inside a part the input's own rounding collapses (rule (ii')). All subsegments of an identity output
  (held to the grid), only with a tolerance; a foreign producer (Triangle's files) is no snap rounder and is not held to it.
- **Joins:** in the chain search the candidate vertices join only the ends of collapsed links, and only at the centres of their
  pixels (two vertices sharing a pixel elsewhere, or off its centre in a foreign output, are not connected without an edge; reviews 4
  and 5).
- **Review 4 (blocking, fixed):** a first form rounded only the line between the end pixels' centres, never the input segment. It
  excused a diagonal strip 1.27 pixel wide whose wall passes a free point's pixel (snap rounding keeps a triangle of 5 px² there), and
  rejected a correct mesh whose hypotenuse snap rounding routes through a point's pixel. It also made the zigzag with refinement look
  like an order dependence of iterated rounding (z0 one SegmentMissing, z1 six).
- **The zigzag with refinement (M3), known INVALID, pinned:** under rule (ii') z0 has 3 SegmentMissing and 1 VertexUnused, z1 5
  SegmentMissing (`QualityMesh2CollapsedPartsTests.DomainNarrowerThanAPixel_RefinementPinch_KnownInvalid`). The input-only snapped domain
  keeps a band 0.7 pixel wide where refinement's grid points bring the two sides together: 382 against 358.5 px² (z0, 23.5 px²
  collapsed by refinement), 804 against 635 px² (z1, 169 px²). Snapped alone (M2) both are accepted. The corpus, the catalogue rows and
  the Herne runs keep their verdicts against master.
- **Self-checks:**
  - Images: a segment past no hot pixel; rerouted through one; rerouted again (iteration: (6, 1) is missed by the segment but passed
    by its rerouted link); a corner touch does not reroute; hot pixels in order along a link; each by walking the pixels and by
    testing the hot pixels; iterated twice over the links ((0, 0) - (24, -1.4) through (11, 0), then (14, 0)); review 4's strip
    (rejected) and hypotenuse (accepted); the first pass through a hot pixel's bottom-left corner (rerouted) and top-right corner (not),
    and review 5's triangle whose edge runs through a bottom-left corner (collapsed, accepted).
  - Accept: a corridor 0.25 pixel wide with its segment and its far points, and its segment from the collapsed tip; a point on it with a segment into the square (the point;
    its chain in the square is still required); the dumbbell (two squares joined by a collapsed corridor, a segment from one to the
    other, either way); E2 complete; a chain routed through a Steiner point's pixel.
  - Reject: a corridor three pixels wide whose content is missing; the corridor's mouth unused (one link there with area); a free
    point inside the square unused; two chains straight through their crossing's pixel without a vertex; the collapsed corridor without a tolerance; an unused corner whose
    edges have area; the review's cases E1, E1b, E2, E6, E8, B1 (a segment on the edge of a wide square), the stub across a pixel
    line, the corridor 0.9 wide across a row boundary and the oblique corridor 0.85 wide, each with nothing excused; the dumbbell
    without its chain in one square; a foreign chain with a gap inside one pixel of the image, at (15, 10) and at the end (20, 10) of
    a collapsed link; a chain past a Steiner point's pixel (`ChainPassesHotPixel`); a foreign output is not held to the invariant;
    review 5's S-C, a Steiner vertex that would collapse what the input's rounding keeps (rule (ii')); the post-review's wedge, a
    Steiner vertex that reroutes a chain off the input's band (rule (ii'), B1).
- **Cost:** built once, lazily, on the first piece without a chain or unused point; the invariant runs on every snapped output with
  subsegments, at the work above per subsegment. A corridor with a ring vertex every pixel (4006 vertices, all unused and excused):
  the oracle's conformity phase took 37 s, of which 36 s were the existing closure test of boundary points (`InClosure` scanned all
  ring edges per point and cut each edge through it at all of them); it now goes through a grid of the edges' boxes: 3 s in all.
  `QualityMesh2CollapsedPartsReportTests.Oracle_ManyCollapsedPointsAreCheap` bounds it.
- **Reports:** the counts appear as "on collapsed parts: N segment pieces, M input points (K of them rerouted by output vertices)".

**Mesher:** the diagnostics report, apart, the input segments with a piece that has no triangle on either side already after
snapping (taken before the carve of the outside), and those whose piece lost its triangles in refinement; only pieces on an edge
that also carries a ring or hull edge count (the part collapsed onto its boundary), so a free segment outside the domain or in a
hole is not reported (review 3). The mesher does not decide
"narrower than a pixel"; the oracle does.

**Next, the mesher's bugfix (owner 2026-11-03):** a grid point of refinement must not bring together two chains of the input's image
that are apart there: rejected where its pixel is contacted by two links of the input-only image that share no node, as an
invariant of the split predicate on every pass. Upper bound measured on the outputs without changing the mesher: 0 such points on
the Herne runs (1 715 to 120 432 Steiner pixels each) and the catalogue rows, 22 (z0) and 13 (z1) on the zigzags; so all other
outputs stay bit for bit. The zigzag M3 cases become acceptance tests there.

**Tried and dropped:** checking the output's chains with closed gaps instead of the images ((A')): in collapsed parts the output
carries no chains, the gaps would need rounding with bounded drift (ISRbd, Packer 2008), and in a band 0.7 pixel wide the pieces
cannot be ordered strictly. A certificate of collapsed double chains from the mesher (certifying algorithms, McConnell, Mehlhorn,
Näher and Schweitzer 2011) is not needed under the strict decision.
