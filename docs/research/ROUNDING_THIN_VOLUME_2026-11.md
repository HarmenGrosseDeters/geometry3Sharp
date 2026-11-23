# The volume that SafeRounding moves on thin results

Status: diagnosis, fixtures and options; option a1 is built (owner 2026-11-23): `MeshRoundingRepair.VolumeBefore` /
`VolumeAfter` and `SolidBooleanReport.RoundingVolumeBefore` / `RoundingVolumeAfter`. The output of the rounding is
unchanged. The other options are not taken for now.

## The finding

On thin results the default rounding of `ExactMeshBoolean` and `SolidBooleanPipeline` (`SafeRounding`) can change the
volume by the order of 1 % against the exact result, and the run says nothing: the status is `Success`, the result is
clean, no component is removed, `RoundingRemoved` is empty. The shapes are ones described by the owner; the fixtures
are built procedurally in `RoundingThinVolumeTests`.

Oracle: the exact result with its vertices rounded to the nearest double (`SafeRounding = false`). Its volume differs
from the exact rational one by rounding units of the coordinates only.

## What the bound is

The suspicion was a rounding bound of about 1e-5 of the diagonal, as large as the result is thick. The code
(`MeshRoundingRepair.component_scales`, `ExactMeshBoolean.RoundingMaxDisplacement`):

- The bound of an edit is `max(1e-5 * D, 256 ulp(largest coordinate), AbsoluteTolerance)`, with `D` the diagonal of the
  largest connected component of the result (not of the inputs).
- A triangle counts as degenerate below the area `1e-12 * D^2` (`DegenerateArea`). The repair edits where the rounded
  mesh has an intersection, a contact the rounding made or such a triangle, and nowhere else.
- The proven guarantee is a distance: the surface moves by at most `Rounding.MaxDrift <= bound` (Hausdorff). For the
  volume it gives `|dV| <= MaxDrift * area`, which for a plate of thickness `t` is `2 MaxDrift / t` of its volume: a
  bound that is void where `t` is near the bound.

So the suspicion is right about the size of the bound, but the bound is not what is reached: in the fixture the surface
moves by 1.7e-6, a twelfth of the bound, and that is enough.

## Shape 1a: the union of two thin plates (reproduced)

`ThinPlates`: A is one plate, the unit square with faceted corners, 2e-5 thick (1 / 50 000 of its side, 22 face
planes). B is four separate plates beside it, 7.5e-5 thick (24 face planes). The corners of B towards A lie 3.5e-6
inside A's side face and 1.5e-6 to 1.7e-6 below A's bottom face: near misses of about 1e-6 of the diagonal (2.0), no
coplanar faces.

| | value |
|---|---|
| exact volume of the union | 5.178e-5 (A 2.0e-5, B 3.2e-5) |
| default result | +3.70e-7: **+0.715 %** of the union, **+1.85 %** of the thin plate A |
| status | `Success`, `RoundingClean`, 0 components removed, `Report.Clean`, empty route |
| edits | 6 degenerate triangles, 6 contractions, nothing else |
| `MaxDrift` | 1.73e-6 (the largest near miss); the bound is 1.99e-5 |
| `MaxDrift * area` | 4.9e-6, 13 times the change |

**Where the volume goes.** The vertical corner edges of B pierce A's bottom face 3.5e-6 from its edge. The triangles
between such a point and B's corner 1.5e-6 below it are about 3.5e-6 by 1.5e-6, below the degenerate area (4e-12), so
the repair contracts the edge between the two: the kept vertex is B's corner (an input vertex; input vertices are kept
where possible), and the point on A's bottom face moves down onto it. That point is a vertex of the triangulation of
A's whole bottom face, and its triangles there, a fan of up to half the face, go down with it. The volume of the cone
under the fan, a third of the move times the fan's area, is the change: up to 1.5e-6 * 0.5 / 3 = 2.5e-7 for one
vertex, 3.7e-7 for the six (their fans overlap). This is the repair working as designed (it removes features below
its resolution, after Milenkovic and Sacks) on a mesh whose large flat faces are fans.

**The thickness does not enter.** The change is the same volume at every thickness of A, so its share grows as the
plate gets thinner:

| thickness of A | 1.6e-4 | 8e-5 | 4e-5 | 2e-5 | 1e-5 | 5e-6 |
|---|---|---|---|---|---|---|
| change | 3.7e-7 | 3.7e-7 | 3.7e-7 | 3.7e-7 | 3.7e-7 | 3.7e-7 |
| of the union | 0.19 % | 0.33 % | 0.52 % | 0.72 % | 0.89 % | 1.01 % |
| of A | 0.23 % | 0.46 % | 0.93 % | 1.85 % | 3.7 % | 7.4 % |

**The near miss decides.** The change is linear in the near miss across the plate (0.247 of volume per unit of it)
while the triangles between the two features are degenerate by area, and zero above that:

| near miss across (d), 3.5e-6 along | 1e-8 | 1e-7 | 5e-7 | 1e-6 | 1.5e-6 | 2e-6 | 2.5e-6 | 5e-6 |
|---|---|---|---|---|---|---|---|---|
| contractions | 6 | 6 | 6 | 6 | 6 | 3 | 0 | 0 |
| change of the union | 0.005 % | 0.048 % | 0.24 % | 0.48 % | 0.72 % | 0.27 % | 0 | 0 |

With the near miss along the face smaller than the one across it (1e-6 against 1.5e-6) the repair contracts the
shorter, horizontal edge: six contractions again, and no volume moves (below 1e-7).

So the change is at most about `(1/3) * near miss * fan area` per contraction, it needs two features within about
`1.4e-6 * D` of each other in two directions (so that the triangle between them is degenerate by area), and it is
large where the moved vertex sits in a large flat face.

## Shape 1b: a thin curved shell cut by a box (not reproduced)

`ShellAndBox`: a shell of 364 triangles, 1 / 4000 of its length thick, that sags in the middle; a box 40 long whose top
face lies a few 1e-6 above the lowest part of the shell's bottom and whose side faces pass within 1e-8 to 3e-7 of
vertex columns. The intersection is a lens strip 6e-6 to 2.6e-5 of its diagonal thick.

The default rounding keeps its volume (below 1e-6 in the five cases of the test). In about 1 260 further random placements
of four constructions (a box face through the shell near vertex columns, a nearly tangent box face, a box corner near
a vertex, the lens strip) no case changed by more than 0.1 %, the threshold of the search; the cases looked at had no
edit or one contraction along a face. The reasons, from the
mechanism above: a single near miss gives long slivers, which are not degenerate by area and are left alone; where the
repair does contract, the edge runs along a face; and the fans of a tessellated shell cover a column, not a face of the
size of the result. A change of 0.8 % on such a shape needs a contraction across the strip at a vertex whose fan covers
much of the strip; I did not find the placement that gives it. The test documents the behaviour found, not the one
reported.

### The second description (still not reproduced)

The owner then described 1b more closely, with the numbers of the run: a clearly curved panel (the mid-surface bulges 6 % of the length, the
normals span almost 90 degrees, 1 / 3400 of the length thick, about 400 triangles), clamped between two box side faces
with its rim vertices within 2e-12 of the diagonal of them, and the box's top face at about 5 degrees to the panel's
mean plane; the exact result a thin wedge strip with 1.4 % of the panel's volume and 6 % of its thickness on average;
3 intersecting pairs and 19 degenerate triangles before the repair, a drift of 3e-6 of the diagonal, 276 triangles
before and 246 after, a loss of 0.84 % from contractions whose new triangles cut across the curved strip.

Two deliberate constructions (`ClampedPanel`, a cylindrical arc of half angle 0.7, 416 triangles, the rim vertices
within 3e-12 of the faces; the arc follows the span of the normals of the description, so it bulges 18 % of its
length, not 6 %: a circular arc cannot have both):

- **The top face at 5 degrees through the panel** (600 heights, a vault and a sagging panel, two arcs). With exactly
  flush rims nothing is edited (1 600 placements). With the near-flush rims the repair edited in about a third of the
  placements (measured before #1464): 1 to 4 degenerate triangles at the rims, each cured by a flip, with no drift and no
  change of the volume. The result is a piece of the panel in its full thickness (4 % to 97 % of it), not a thin strip.
- **The top face inside the thickness of one facet**, nearly parallel to it (72 cases): a lens of 0.2 % to 2 % of the
  panel's volume across the full width. Here the repair contracts an edge with a drift of 1.7e-6 to 7e-6 of the
  diagonal, the size described, and the volume stays (below 1e-9 in the four cases of the test, and no change at
  the 1e-6 that the probe printed in the others): the lens lies inside one flat facet, so the contraction runs along
  it and no new triangle cuts across a crease.

What I could not build is the reported strip itself: a plane that stays within 6 % of the thickness of a panel over
two thirds of its length, while the panel bulges 200 thicknesses over that length. In a faceted cylindrical panel a
plane can lie inside the thickness over one facet (a fourteenth of the length here), and where it crosses facets at
5 degrees it leaves the thickness within 11 thicknesses. The loss as described needs a strip that is thin **and** runs
over creases, so that a contraction replaces two facets by a triangle across them: the mechanism of 1a with a crease in
place of the near miss.

**Outcome (owner 2026-11-23):** shape 1b is not reproduced procedurally, and no further attempt is planned. The two
attempts above stay as documentation of what the rounding does on these constructions. Where the change occurs,
`RoundingVolumeBefore` and `RoundingVolumeAfter` report it.

## Shape 2: a curved plate partly flush with a block (the guard)

`PlateOnBlock`: a plate of 152 triangles, 1 / 250 of its length thick, flat over its first quarter and curving down
after it, on a block with faceted corners (76 triangles): 8 vertices of the plate lie exactly on the block's top face,
12 of its triangles are coplanar with it, and the curved part enters the block at a clear angle. The intersection is
`Success` and clean, and its volume is the exact one (below 1e-9). No change expected, none found.

## A grid

- `InputGrid` cures the near misses below its cell: at 2^-20 (9.5e-7) a near miss of 1e-7 snaps to 0, the two bottoms
  are coplanar, the exact boolean resolves that without a sliver, and the change against the exact union of the
  snapped inputs is below 1e-9 (0.048 % without the grid).
- It does not cure a near miss above the cell: 1.5e-6 snaps to two cells, the same contraction follows, and the change
  stays of the order of 1 %.
- The snap itself moves the faces of a thin plate: A is 21 cells thick, and against the exact union of the inputs as
  given the result with the grid is off by 0.5 % to 2 %. A grid chosen from the coordinate scale can be far coarser
  than a thin result (in the shell fixtures, a cell of 2^-20 of the box's 40 flattens the lens strip entirely).
- The Grid step (`AutomaticSteps`) does not run: the run is a success. Its cell (2^12 ulp, 9e-13 here) would not merge
  near misses of 1e-6 either.

## Building blocks in the library

- `MeshRoundingRepair.MaxDrift` (public, on `ExactMeshBoolean.Rounding`): the largest move of the surface. Not in the
  pipeline's report.
- The class comment of `MeshRoundingRepair` already states the volume change of each kind of edit (a third of the move
  times the area vector of the link for a move or a contraction).
- `MeshMeasurements.VolumeArea`: volume and area in one pass.
- The thickness `2 |V| / area` of a solid: `SolidBooleanPipeline.MinThickness`, `RemovedComponentInfo.Thickness` (#1216).
- The contraction's choice of the kept vertex (`MeshRoundingRepair`, "the kept vertex at a fixed end, else at either
  end or the midpoint").

## Prior art

From the papers as known; not re-read for this note, and no code was read.

- Milenkovic and Sacks, *Geometric rounding and feature separation in meshes* (CAD 2019), which the repair follows:
  the guarantee is a displacement bound and a separation of the features; volume is not bounded or reported.
- Snap rounding in 2D and 3D (Hobby 1999; Devillers, Lazard, Lenhart, *3D snap rounding*, SoCG 2018): distance bounds.
- Mesh simplification has the remedy for the same effect: Lindstrom and Turk (*Fast and memory efficient polygonal
  simplification*, 1998) place the vertex of a contraction so that the volume is preserved; Garland and Heckbert's
  quadrics (1997) charge a vertex for leaving the planes of its fan, which is exactly what moves the volume here.

## Options

| | what | kind | cost | effect on the fixture |
|---|---|---|---|---|
| a1 | Report the change: the volume before and after the repair (`MeshRoundingRepair`, `SolidBooleanReport`), absolute and relative | new public members | two passes of `VolumeArea`, a few lines, tests | the 0.7 % is in the report; the result is unchanged |
| a2 | Report `MaxDrift` against the thickness `2 V / area` of the result | new public member | as a1 | 4.7 % here: an indicator, not a bound (the proven bound `MaxDrift * area / V` is 9.5 %; the real change is 0.7 %), cheap but coarse |
| a3 | A limit on a1 (`MaxRoundingVolumeChange`, relative): beyond it the result is not clean, with `StrictRounding` the run fails | new option, off by default | a1 plus the verdict | the run reports the loss as a failure the caller chose |
| b | The pipeline answers a large change with the Grid step | output change | small, on a1 and the Grid step | none: the step's cell is far below the near miss, and a coarser cell moves thin plates itself |
| c | The repair prefers the contraction that moves less volume: the other end, the midpoint, or the position that keeps the volume (Lindstrom and Turk) | output change of the default rounding | a spike first: the kept end is an input vertex today, by design; then the mutation shards of the rounding | unknown; in the fixture the other end would move B's corner up and leave A's face flat |
| d | The repair refuses an edit that moves more than a fraction of the component's volume | output change | small in code, but refused edits leave defects | the six triangles stay degenerate, the result is not clean |

**Decision (owner 2026-11-23): a1 only.** The recommendation was: a1 now, a3 with it if the owner wants a verdict, and a spike for c. a1 is cheap, changes no
output and turns a silent change into a number a caller can test; a2 alone would cry wolf (4.7 % for 0.7 %). b does
not help on the measured case. c is the only option that removes the change instead of reporting it, and the
simplification literature has the rule for it, but it touches which vertices the rounding keeps, so it needs its own
measurement on the benchmark corpora before a decision.

## Caps first

A second mechanism, with an output change the owner approved (2026-11-23): a degenerate triangle that is a cap, with one
short edge.

- **The shape** (`RoundingCapFirstTests.Step`, procedural): a slab under a block, extruded. On both end faces the foot of
  the riser, A, lies w beyond the corner C of the slab's top (w below the bound) and e = 1e-12 off the line of the top,
  so that the triangle (A, C, B) has three corners almost on one line: its area is below the degenerate area, its only
  edge below the bound is A-C.
- **Before:** `fix_degenerate` tried the contraction of a short edge first, and the flip only where no contraction
  was accepted. It contracted A-C on both end faces: the foot of the riser moved by w, and with it the riser. The
  volume changed by w H W / 2: 2.5e-7, 6.25e-7 and 1.0e-6 of 3.0e-4 for w = 2e-6, 5e-6 and 8e-6 (0.08 %, 0.21 %,
  0.33 %), with `MaxDrift` = w.
- **Now:** a cap is flipped first. A cap is a degenerate triangle whose shortest and longest edge would span 32 times
  the degenerate area at a right angle, so the angle between them is below 1 / 32 (`CapRatio` in `fix_degenerate`: half the
  product of the shortest and the longest edge, over the degenerate area of the component, at least 32): it is degenerate
  by its angle, not by its short edge. The flip of its longest edge removes it and moves no vertex. On the shape: 2 flips,
  no contraction, the volume unchanged, `MaxDrift` at most e. Where the flip is not accepted the order is the old one.
  The decision is made on lengths and on an area that scale by 2^k and 4^k, so it is the same at every power-of-two scale
  (`RoundingCapFirstTests.Step_AtTheLimitOfACap`, 0.9 and 1.1 times the limit at the scales 1, 2^-10, 2^10).
- **The limit 32** (owner decision 2026-11-23: the ratio form, not the bare distance of the apex from the longest edge, which
  breaks an existing test). The first version took 1024; a cap with a ratio of a few hundred was still contracted. The
  reproducer (`Step(w = 5e-6, e = 1e-12, l: l)`: l is the long edge of the cap, an extra corner on the top of the slab;
  ratio = 0.5 shortest longest / degenerate area), measured on top of #1464 (net10.0):

  | l | ratio | limit 1024 | limit 32 |
  |---|---|---|---|
  | 4e-4 | 761 | 2 contractions, volume +6.25e-7 (of 3.0e-4: 0.21 %), drift 5e-6 | 2 flips, volume change 0, drift 0 |
  | 2.5e-4 | 476 | as 4e-4 | as 4e-4 |
  | 1e-4 | 190 | as 4e-4 | as 4e-4 |
  | 4e-5 | 76 | as 4e-4 | as 4e-4 |
  | 1e-5 | 19 | 2 contractions, +6.25e-7 | the same: below the limit |
  | 2e-6 | 3.8 (the shortest edge is then l) | 2 contractions, volume change -1e-18, drift 2e-6 | the same |
  | none (the edge A-B, 0.5) | 9.5e5 | 2 flips | 2 flips |

  With 256 (measured by the previous worker, on the base before #1464) the first two rows were flipped, the next two still
  contracted. What each limit breaks, from the rounding tests (the 1139 tests whose names contain Rounding or SolidBoolean;
  the older numbers are the previous worker's, on the base before #1464, not re-measured):
  - 1024, 256, 64, 32: no test fails, apart from the pins of the limit itself; the old clamped-panel tests of that base failed
    at 32 in `InRange(Collapses, 1, 3)` (all four tilts), and #1464 rewrote them: on top of #1464 they pass unchanged at 1024 and 32.
  - 16: only the tests of the limit itself (a ratio of 28.8 is flipped, a ratio of 19 as well): measured on top of #1464.
  - 4 and 1: on top of #1464 `RoundingThinVolumeTests.ClampedPanelShavedInsideOneFacet_ATiltedLens_TheRepairContractsAcrossIt_AndTheVolumeChangeStaysSmall`
    fails (the lens is flipped, no contraction: the assertion on 2 contractions), and the tests of the limit; nothing else.
    On the base before #1464 the previous worker found at 4 `FoldedCluster 52 8 4` not clean, and at 1 also ShortEdge and
    DensePatch: with the exact InCircle of #1464 these three no longer fail at 4 and 1 (measured), so the floor of 32 rests
    on the owner's decision, the lens test and that earlier measurement, not on a failing test of the current base.
  - the bare apex-distance test: breaks an existing test (previous worker; not re-measured, not in the code).
  The floor is a trade: a cap below 32 (the ratio of 19 above) is still contracted, with the volume change of a contraction
  across a thin strip. From 16 384 on every result of the old base was that of 1024; the shape qualifies up to about 9.5e5.
- **No effect on the fixtures above:** the plates, the shell and the clamped panel give the numbers they gave (their
  tests are unchanged): their degenerate triangles are not caps by this test, or have no flip that is accepted.
- **A limit:** in one frame of `Similarity.All()` (rotated, millimetre scale, far from the origin) the rounding of the
  input folds the cap over its neighbour by 2e-8 of the diagonal. No triangle is degenerate there; the mesh intersects
  itself, and `fix_intersection` resolves that as before, by the contraction of A-C, with the volume change of that
  (6.3e-7). Two variants of a flip first in `fix_intersection` (for a pair across an edge, by the angle and by opposed
  normals) did not change it and are not in the code. Open.
- **Cost and effect elsewhere** (measured 2026-11-23, before and after, net10.0):
  - **At the limit 32, on top of #1464** (2026-11-23, this machine, net10.0): the same job (`run <case> safe`, every third case of
    booleans, utm and clusters, all of cylinder and drop, scale up to 81: 214 cases, each in its own process, one after the other,
    two passes with the order of the two builds alternating, the faster of the two per case), `CapRatio` = 1e300 (no cap is
    taken: the old order) against 32: 83.2 s against 83.2 s (ratio 1.000); the largest ratio of a single case above 200 ms is
    1.17 (`bool:Collinear:0:Difference:0`, noise). 205 of 214 cases give the same result (clean, triangle count, edits, drift);
    nine differ, none in cleanliness (the one case that is not clean, `cluster:FoldedCluster:36:8:3`, is not clean before and
    after): edits +1 in four of them, +2 in `FoldedCluster:36:8:0`, -2 in `FoldedCluster:44:8:3` (triangles 484 to 488), -1 in
    `FoldedCluster:52:8:3` (drift 2.7e-5 to 3.0e-6); the same edits with another drift in `FoldedCluster:36:8:3` (3.2e-5 to
    2.8e-5) and `scale:81:Intersection` (triangles 51 688 to 51 690, drift 1.2e-5 to
    8.4e-6). The driver script is not in the repository; the numbers of the next three bullets are of the first version (1024).
  - `benchmarks/SnapRoundingExperiment`, `run <case> safe` on 214 cases (every third case of the sets booleans, utm
    and clusters, all of cylinder, drop and scale up to resolution 81), two alternating passes each, the faster of the
    two per case: 89.7 s before, 88.8 s after (ratio 0.99; no set above 1.00). 202 cases clean before and after; two
    cases (Cluster, k = 44) have one edit more, with the same triangle count and drift.
  - (The two bullets below: measured with the limit 1024 on the base before #1464; the previous worker found the same lines at 256 and 32 for the probes; not re-measured on top of #1464.)
  - The stress inputs of two probes under review (unions of spheres with gaps and overlaps down to 1e-12, 20 cases;
    tube skeletons with grazing forks and near-tangent branches, 9 cases) through `SolidBooleanPipeline`: every line
    identical before and after (status, verdict, triangle count, the volume before and after the rounding). 29 of 29
    succeed, 28 clean and one with contacts only (two spheres exactly tangent); the largest change of the volume by
    the rounding is 1.3e-9 of the volume.
- **Prior art:** caps flipped, needles collapsed is Botsch and Kobbelt 2001 ([sources](../sources/meshing-and-repair.md)),
  already listed for the repair's flips; the order between the two edits for a cap with a short edge is the change here.

## Update 2026-11-23 (#1464): the exact InCircle removes most of these cases

The delaunay code of `ExactPlanarSimplify` now decides the flips by the exact `InCircle` (the filter alone returned 0
where it could not decide: a correctness fix of stage S2-3 of the determinism proof, made for the input-order PR). Flat
regions are now Delaunay, so the caps that used to round to degenerate triangles at the rims are gone, and most of the
placements above need no repair. Measured on this branch (the fixtures of `RoundingThinVolumeTests`):

- **Five degrees, default settings**: nothing to repair at seed 7 in all four cases (volume the exact one), and at seed 8
  in three of them; the sagging panel at z0 -0.05836649455985357 with seed 8 has 1 degenerate triangle, cured by 1 flip,
  drift 1.5e-7 of the length (1.1e-7 of the diagonal), volume change -3.5e-10 relative. (Other seeds were not swept for this
  update; a sweep of seeds 0..39 by the first worker of the PR found one more contraction, at seed 22, drift 5.2e-6.)
- **Five degrees, `SimplifyFlatRegions = false`** (the near-flush rim triangles stay): 18 to 48 degenerate triangles
  (18, 18, 48, 26 in the four cases, seed 7), 9 to 24 contractions, drift about 1.1e-12, volume within 2.5e-12, clean.
- **The lens inside one facet** (tilt -3e-4..1e-4 against the facet, top face 0.02 to 0.04 of the thickness above it, seeds
  8 and 9): 20 of 24 combinations need no repair. Two pairs contract: tilt -3e-4 at 0.04 t (2 contractions, drift 1.3e-6 of
  the diagonal, volume within 5e-15: the contraction runs along the facet) and tilt +1e-4 at 0.02 t (2 contractions, drift
  6.8e-7, **volume -1.6e-6 relative**: a contraction that changes the volume, a case of its own). At 0.03 t nothing contracts.

The old assertions "1 to 8 degenerate triangles, cured by flips" and "1 to 3 contractions in all four tilts, volume within
1e-9" have no successor: that behaviour is gone with the caps.

The change goes in both directions, and the repair is not better in general. The lens at tilt +1e-4 and 0.02 t has no volume
change without this PR's behaviour changes (2 contractions, the same drift), and with them it loses 1.6e-6 of the lens's
volume; the lens at tilt -3e-4 and 0.04 t went the other way, from about -1.0e-6 to -4.5e-15 (reviewer's measurement
with the behaviour changes reverted). Which edges there are to contract follows the triangulation, and the volume is not
the repair's concern (options c and d above): the test of the +1e-4 lens pins an upper bound of 2e-6 on the magnitude, not
the loss.

What the tests now guard, honestly: the default five-degree cases no longer exercise the repair (nothing is left to repair),
so the old claim "the flips move nothing, the volume stays within 1e-12" has no successor; the seed-8 case with one flip
allows 1e-9 (measured 3.5e-10), and the variant without `SimplifyFlatRegions` allows 5e-12 where the old test allowed 1e-12
(measured 1.6e-12, 2.5e-12, 1.6e-12 and 3e-14 in the four cases, which is why 1e-12 no longer holds: the contractions of
18 to 48 rim triangles add up). The bounds are about 2 to 3 times the measured values and were not tightened further.

## Tests

`RoundingCapFirstTests`: the step (the flip, the volume, four cases), the limit between a cap and a needle on both
sides, reuse, and the invariance over `Similarity.All()` (the 2^k scales bit for bit; the frame of the limit pinned as
it is).

`RoundingVolumeReportTests` checks the two numbers of a1: on the plates (the 3.7e-7), where the repair changes nothing
(shape 2, closed boxes: equal bit for bit), on the paths without a repair (0 and 0), on a strict refusal, after the
Grid step, on the repair itself (far from the origin, inside out, open, empty, not finite), reuse, and the invariance
over `Similarity.All()`.

`RoundingThinVolumeTests` (37 cases, each well under a second): the plates (the change and the silence, the cause, the
thickness sweep, the near-miss sweep, the horizontal contraction, the grid, the Grid step), the shell cut by a box, the clamped
panel of the second description (the cut at 5 degrees, with and without the simplification, the flip at seed 8, the lenses inside a facet), and the guard. They document the behaviour as it is: a fix of option c or d will change the plates' numbers, and those
tests then get the new ones.
