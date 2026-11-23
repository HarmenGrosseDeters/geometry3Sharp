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
1e-9" have no successor: that behaviour is gone with the caps. The statement that a canonical order of the input makes the
outcome deterministic, not better, holds here as well: where a repair is still needed, it is as large as before.

## Tests

`RoundingVolumeReportTests` checks the two numbers of a1: on the plates (the 3.7e-7), where the repair changes nothing
(shape 2, closed boxes: equal bit for bit), on the paths without a repair (0 and 0), on a strict refusal, after the
Grid step, on the repair itself (far from the origin, inside out, open, empty, not finite), reuse, and the invariance
over `Similarity.All()`.

`RoundingThinVolumeTests` (37 cases, each well under a second): the plates (the change and the silence, the cause, the
thickness sweep, the near-miss sweep, the horizontal contraction, the grid, the Grid step), the shell cut by a box, the clamped
panel of the second description (the cut at 5 degrees, with and without the simplification, the flip at seed 8, the lenses inside a facet), and the guard. They document the behaviour as it is: a fix of option c or d will change the plates' numbers, and those
tests then get the new ones.
