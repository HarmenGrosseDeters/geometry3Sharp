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
| a2 | Report `MaxDrift` against the thickness `2 V / area` of the result | new public member | as a1 | 4.7 % here: an upper estimate (the real change is 0.7 %), cheap but coarse |
| a3 | A limit on a1 (`MaxRoundingVolumeChange`, relative): beyond it the result is not clean, with `StrictRounding` the run fails | new option, off by default | a1 plus the verdict | the run reports the loss as a failure the caller chose |
| b | The pipeline answers a large change with the Grid step | output change | small, on a1 and the Grid step | none: the step's cell is far below the near miss, and a coarser cell moves thin plates itself |
| c | The repair prefers the contraction that moves less volume: the other end, the midpoint, or the position that keeps the volume (Lindstrom and Turk) | output change of the default rounding | a spike first: the kept end is an input vertex today, by design; then the mutation shards of the rounding | unknown; in the fixture the other end would move B's corner up and leave A's face flat |
| d | The repair refuses an edit that moves more than a fraction of the component's volume | output change | small in code, but refused edits leave defects | the six triangles stay degenerate, the result is not clean |

**Decision (owner 2026-11-23): a1 only.** The recommendation was: a1 now, a3 with it if the owner wants a verdict, and a spike for c. a1 is cheap, changes no
output and turns a silent change into a number a caller can test; a2 alone would cry wolf (4.7 % for 0.7 %). b does
not help on the measured case. c is the only option that removes the change instead of reporting it, and the
simplification literature has the rule for it, but it touches which vertices the rounding keeps, so it needs its own
measurement on the benchmark corpora before a decision.

## Tests

`RoundingVolumeReportTests` checks the two numbers of a1: on the plates (the 3.7e-7), where the repair changes nothing
(shape 2, closed boxes: equal bit for bit), on the paths without a repair (0 and 0), on a strict refusal, after the
Grid step, on the repair itself (far from the origin, inside out, open, empty, not finite), reuse, and the invariance
over `Similarity.All()`.

`RoundingThinVolumeTests` (24 cases, each well under a second): the plates (the change and the silence, the cause, the
thickness sweep, the near-miss sweep, the horizontal contraction, the grid, the Grid step), the shell cut by a box, and
the guard. They document the behaviour as it is: a fix of option c or d will change the plates' numbers, and those
tests then get the new ones.
