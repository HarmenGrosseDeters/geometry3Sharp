# Design note: RoundToGrid for the terrain slope design (parked, owner decision R1)

Planned as the last terrain item before the pause (ROADMAP; TERRAIN.md §11, "Face 2, clipped slivers"). This is the
design and measurement step that CLAUDE.md and AGENT_WORKFLOW.md §2, 3 and 8 ask for before an algorithm: the
building blocks, the literature, and an upper bound first. The upper bound showed that the planned approach does not
work at 1 mm (§3), so it is parked (§5).

## 1. What is asked

An optional output step `RoundToGrid(origin, resolution)`:
- iterated snap rounding to the target grid;
- then the exact slope and support checks again on the rounded coordinates, with a status.

Features below the grid merge in a controlled way instead of failing silently. The same vertex-edge separation
repairs face 2 (clipped slivers far below `MinFeatureSize`), with no second mechanism. TERRAIN.md 5.4 ("Quantized
output") has the contract: quantize **before** the final check, then repair or reject, then check, store, read back.

## 2. The upper bound (measured, 2026-11-03)

**Setup.** The Hambach DGM cases of TerrainBenchmark (master 8364ac74; the facets of #363 differ by 10–19 %). The
slope region and the slope surface (cut) are rounded to a grid at the origin 0, and the exact checks run again.
Counts are triangles; z is rounded to nearest, or down (a cut plane only lowers):

| case | grid | region: flipped / steeper / unsupported (of n) | surface: steeper / unsupported (of n) |
|---|---|---|---|
| one toe edge, 1 m | 1 mm | 0 / 211 / 202 (421) | 1 / 1 (3) |
| octagon r 100 m | 1 mm | 2 / 1177 / 902 (2257) | 88 / 4 (159) |
| octagon, z down | 1 mm | 2 / 1308 / 869 | 159 / 0 |
| L shape | 1 mm | 0 / 549 / 309 (1149) | 77 / 19 (255) |
| wavy 48-gon | 1 mm | 0 / 1146 / 976 (2274) | 225 / 202 (474) |
| wavy 48-gon | 0.1 mm | 0 / 939 / 1003 | 51 / 204 |
| wavy 48-gon | 1 cm | 27 / 1240 / 902 | 225 / 198 |

**Rounding after the fact breaks about half the triangles, even at 0.1 mm.** The reason is the margins, which follow
the doubles' resolution, not a grid:
- δ = 1e-6 of the slope;
- λ (the toe lowering) is 1e-8 to 1e-4 m.

A 1 mm move of a vertex changes a 1 m triangle's slope by about 0.4 % (TERRAIN.md 5.4). It moves the plane at the
toe point by about the slope × 0.7 mm, far more than λ. Rounding z down removes the support failures of the
realized surface, but not of the clipped region: there the XY rounding at the toe moves the plane too. It also
makes more triangles steeper.

## 3. What the bound of 5.4 gives with a grid (corrected)

5.4 derives everything from the coordinate resolution:
- e_xy: the XY rounding;
- e_eval: the z rounding;
- `MinFeatureSize` = 2·(8·e_xy + 4·e_eval / s) / δ;
- the λ bound 2·e_z·(1 + 4.4·D0 / `MinFeatureSize`), with e_z = 2·s·e_xy + e_eval;
- the merge share 6·s·(r_merge + e_xy), with r_merge = 64·e_xy;
- the margin's share δ·s·1.1·D0.

All of them must fit into `Tolerance`. With a grid of width w as the resolution, e_xy = w/2 and e_eval = w (one-sided).
The table gives the largest δ that fits at `Tolerance` 5 cm and slope 1:1.5. A quarter of `Tolerance` is reserved
for the snap, and the shares are capped at 0.9 × `Tolerance` in total. The reaches are assumed, not measured: 45 m is a
height of 30 m at 1:1.5. The largest margin the build realizes is 1e-3:

| w | reach D0 | merge radius | merge share | largest δ | `MinFeatureSize` |
|---|---|---|---|---|---|
| 1 mm | 45 m | 64·e_xy (today) | 130 mm | none: infeasible | – |
| 1 mm | 45 m | e_xy | 4 mm | 3.8e-4 | 52 m |
| 1 mm | 15 m | e_xy | 4 mm | 1.1e-3, capped at 1e-3 | 20 m |
| 0.1 mm | 45 m | 64·e_xy | 13 mm | 2.9e-4 | 6.9 m |
| 0.1 mm | 45 m | e_xy | 0.4 mm | 4.8e-4 | 4.2 m |

An earlier version of this note gave "from about 11 m" at 1 mm. It counted the margin's share only, not the λ bound
and the merge share. The owner's decision 1 (§5) was taken on that number.

**The realization collapses every edge shorter than `MinFeatureSize`** (5.4, the repair). With the grid as the
resolution of the whole build, at 1 mm that destroys the design. So the grid cannot be the build's resolution:
approach (A) below does not work.

**A post-step instead** (measured on the DGM wavy 48-gon): the build at doubles, then the region rounded with z one-sided down, then the
exact checks.

| δ | 1 mm grid: steeper / unsupported | 0.1 mm grid: steeper / unsupported |
|---|---|---|
| 1e-6 (today) | 53 % / 31 % | 42 % / 32 % |
| 1e-4 (near the budget's limit; 4e-4 is `InvalidInput`) | 21 % / 28 % | 15 % / 27 % |

- **The unsupported ones** need a λ floor of about s·0.71·w + w (1.5 mm at 1 mm). That is cheap in the budget.
- **The steeper ones** are the small clipped triangles at the catch line. A move of half a pixel on a triangle with an
  altitude of 0.1–1 m changes its slope by more than any δ the budget allows.
- **What remains** needs a local re-triangulation of the clipped region on the grid: larger triangles, or vertices
  chosen per neighbourhood under the slope constraint. That is rounding with constraints (Milenkovic; Fortune 1999),
  a research item.

## 4. Proposed design

**(A) The grid as the coordinate resolution.** (Does not work at 1 mm, see §3; kept for the record.)
- The realization already rounds XY first and then takes z from the vertex's plane, rounded (5.4). With an
  `OutputGrid`, it rounds XY to the grid and z to the grid, one-sided (cut down, fill up).
- e_xy and e_eval of the whole build (`MinFeatureSize`, δ, λ, the band repair, the needle flips, the merge radius)
  then follow from w instead of the ulp. The existing machinery collapses and merges what is below the grid, in a
  controlled way, and the exact check decides as today.
- 2^k invariance holds if the grid scales with the input.
- **What changes:**
  - the realization's rounding function (`SlopeMap` realize);
  - the derived e_xy and e_eval in `build`;
  - the catch points of the composite (snapped to grid points, which the certificate of #363 already covers as a
    move);
  - the terrain's own vertices (on the DGM's grid, usually on the output grid; else they move too).

**(B) The composite's topology: snap rounding.**
- Rounding the composite's vertices to the grid can make edges cross or vertices fall onto edges: the face 2
  slivers, and the T-junction class of #361.
- `comp_geom/SnapRounding2` gives it: Hobby 1999 plus the restore step of Belussi et al. 2016, every vertex at least
  half a pixel from every edge not incident to it, exact decisions, 2^k bitwise (owner decision D14,
  MESHING2D_SNAP.md, against Halperin-Packer's iterated SR and Hershberger's stable SR). Its contract is the one the
  ROADMAP names.
- **Gaps to fill:**
  1. **A grid origin.** `SnapGrid` is anchored at 0. An origin needs either an offset in `SnapGrid`, or an exact
     translation of the input, which is not exact in doubles in general.
  2. **TIN to PSLG and back.**
     - The composite's edges as segments, the region and terrain groups as regions, heights carried along.
     - Then the triangles again (`ConstrainedDelaunayMesh2` with `SnapTolerance` = w), with each new triangle
       attributed to its toe piece for the support check.
  3. **The steps.** At the toe, the lateral limits and toe crossings, two vertices share an XY at different z.
     A 2D PSLG merges them (the lowest index wins its height). They need one hot-pixel set for both sides, and a
     z per side: the region and the terrain snapped as two PSLGs on the same pixels.
  4. **z of new vertices.** SnapRounding2 interpolates z in doubles. Here z must come from the vertex's own plane,
     rounded one-sided to the grid.
- **Face 2.** A clipped sliver below half a pixel cannot survive the vertex-edge separation: its vertex snaps onto
  the edge's pixel and the sliver collapses. That is the repair TERRAIN.md §11 asks for.

**(C) After the rounding.**
- The exact slope and support checks on every region and surface triangle.
- The catch-point certificate of #363 with the rounding as the move, so `Tolerance` holds outside the steps.
- What fails raises δ (a larger `MinFeatureSize` at the grid) as today. If that does not fit, `SlopeViolation`
  (NotCertified) with the triangles.
- A read-back test per format (OBJ with round-trip formatting) as in 5.4.

**Budget.** The grid's own share is s·e_xy + e_eval for the surface plus the certificate's move. It is reserved
like the snap share, from the facets.

## 5. Owner decisions (2026-11-03)

1. ~~The guarantee at 1 mm is accepted, a priori from about 11 m.~~ **Withdrawn.** It rested on the wrong bound that §3
   corrects.
2. **z is rounded one-sidedly:** cut down, fill up, at most one pixel.
3. **The grid may move the toe step by half a pixel.** The certificate of #363 measures that move and charges it
   against `Tolerance`.

**Decided 2026-11-03: R1.** RoundToGrid is parked, with no code before the pause. The options were:
- **(R1)** No RoundToGrid code before the pause: this note, TERRAIN.md §11 (parked) and the measurement arguments.
- **(R2)** A reduced RoundToGrid: snap rounding of the outputs, the λ floor, the exact check, and NotCertified for what
  fails (20–50 % of the region triangles at 1 mm).
- **(R3)** The local re-meshing under the slope constraint, a multi-PR research item.

## 6. Prior art (the prior-art line, AGENT_WORKFLOW §2)

- **Snap rounding:**
  - Hobby 1999 (hot pixels);
  - Halperin and Packer 2002 (iterated SR, vertex-edge separation);
  - Hershberger 2008 (stable SR);
  - Belussi, Migliorini, Negri and Pelagatti 2016 (SRR, the restore step `SnapRounding2` uses).
- **Rounding with constraints:** Milenkovic's rounding of arrangements, and Fortune's vertex rounding of 3D polyhedral
  subdivisions (1999), which keeps the topology of a 2.5D or 3D subdivision while rounding its vertices. This is the
  same problem as the composite's steps.
- **GIS:** a fixed precision model with a snap-rounding noder (JTS `SnapRoundingNoder`, EDL/BSD-3; read for the
  contract only, not ported).
- **Geogram (BSD-3):** not read for this note, since the library's `SnapRounding2` already gives the contract. If a
  file is read later, its Inria header is checked first, and the triangle and TetGen files stay excluded.
- **In the library:** `SnapRounding2`, `ConstrainedDelaunayMesh2.SnapTolerance`, the exact checks of
  `Slope.IsNotSteeper` / `IsPlaneOnOrBelow`, the certificate of #363, and SAFE_ROUNDING.md (3D, not for this; its
  separation certificate is a test harness).

## 7. How others solve it

- **JTS / GEOS OverlayNG.** A fixed `PrecisionModel` (a scale, eg 1000 for mm) is applied to the output of an overlay.
  Its snap-rounding noder (Hobby's hot pixels) nodes the input on that grid, so the result's vertices are on the grid
  and its topology is valid. Features below a pixel collapse; that is part of the contract. That is (B) here.
  OverlayNG rounds coordinates to nearest; it has no slope constraint, so it has no counterpart to the re-check of (C).
- **Directed rounding, as in interval arithmetic.** An interval result rounds its lower bound down and its upper bound
  up, so that it holds the exact value. The one-sided z here rounds the same way: a cut surface's z goes down, so the
  rounded plane stays on or below the exact one at its vertices. That keeps the support check's direction (5.4 (ii))
  at the cost of at most one pixel.
- **The output precision of LandXML and DGM exchange.** These formats store coordinates as text with a chosen number
  of decimals (LandXML), or on a fixed grid such as the DGM1 1 m raster with heights in cm or mm. They define no rule
  for keeping a geometric property, such as a slope bound, through the rounding. A guarantee that must survive the
  export has to be checked on the rounded coordinates, as 5.4 says, which is what (C) does.

## 8. Next steps

**If R1:**
- Nothing more before the pause.
- At the restart, the research item first: the local re-triangulation of the clipped region on the grid under the
  slope constraint, with the λ floor.
- Then (B) and (C) of §4 on top. (B) is still the right tool for the topology: the grid origin in `SnapGrid`, TIN to
  PSLG with the steps, face 2.

**If R2:**
- (B) and (C) with the λ floor and the one-sided z.
- NotCertified for what the check rejects.
- Re-measure at 1 and 0.1 mm.
- The tests of the earlier plan: every output vertex on the grid, the oracle of #363 on the rounded composite, 2^k
  invariance with the grid scaled, a read-back test, mutants per repair class.
