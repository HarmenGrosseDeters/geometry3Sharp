# Meshing2: hull edges on the grid (design note for the owner)

Status: **decided (owner, 2026-11-02; section 7), not built yet.** Written after the reference comparison found OffGrid vertices on the Herne tiles at snap
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
