# The Grid step of `SolidBooleanPipeline`: the cell from the data

`SolidBooleanPipeline.AutomaticSteps = Grid` (opt-in, default off) answers two failures of a run with one retry on a
snapping grid:

- the boolean's `RoundingNotClean` (the strict rounding refused the result), and
- the sheet role's refusal `RimInsideWorkpiece`, typically a rim that is meant to lie flush on the surface of the first
  input but sits a few rounding units inside it.

The retry is the run with `InputGrid` set to a cell that is taken from the data. This note derives the cell. User-facing
behaviour: [booleans-and-imprint.md](../guides/booleans-and-imprint.md#the-grid-step-automaticsteps).

## The cell

Let `M` be the largest absolute finite coordinate of all vertices of all inputs, and `ulp(M) = 2^(e-52)` for
`M` in `[2^e, 2^(e+1))`. The cell is

    g = 2^12 * ulp(M) = 2^(e-40)

(`SolidBooleanPipeline.DataGrid`, `GridBinades = 12`). It is a power of two, so the snap is exact (`InputGrid`), and
inputs scaled by `2^k` get the cell scaled by `2^k`: the result is the same, bit for bit. No constant with a unit
enters. No cell exists where `M` is 0 or the cell would be subnormal (`M < 2^-982`); then the step does not run.

`M` is the coordinate magnitude, not the size of the model: the noise the step answers comes from rounding at the
coordinates as stored, so a small part far from the origin gets the cell of its coordinates.

## Why 12 binades

A snap makes two coordinates equal when both round to the same multiple of `g`. Two coordinates that are meant to be
equal but differ by noise `d` stay different when a rounding boundary (an odd multiple of `g / 2`) lies between them,
which for a position that is not related to the grid has the chance `d / g`. With `n` such coordinate pairs that all
have to agree, the retry fails with a chance of about `n * d / g`.

**The noise.** A coordinate that comes out of a short chain of double operations (a rigid transform is three products
and three sums per coordinate, with a rounding of half an ulp of the partial result each; a projection onto a plane, an
interpolation along an edge or two chained transforms are of the same kind) is off by a few ulps of the largest
coordinate involved: typically 1 to 4, and up to about `2^4` for chains. Noise of a different origin (decimal
round-trips with few digits, single precision, tolerant modelling) is larger by orders of magnitude and is not what an
automatic step should absorb: the caller knows that quantum and states it as `InputGrid`.

**The lower bound.** For a miss chance of about 1 % with `d = 2^4 ulp` and a handful of coordinates (`n` about 4) the
cell has to be about `2^4 * 4 * 100`, that is `2^12` to `2^13` ulp.

**The upper bound.** A vertex moves by at most `g / 2 = 2^11 ulp(M)` per coordinate, at most `2^-41 M` (4.5e-13 of the
largest coordinate): 2 micrometres at coordinates of 6.6e6 m (UTM northings), 2e-12 m at 4 m. That stays five orders of
magnitude below the single precision grid (`2^29 ulp`), so coordinates that were read from single precision values of
the magnitude of `M` are multiples of the cell and do not move. Every binade more halves the miss chance and doubles
the move and the size of the features that the snap can collapse; beyond the noise model there is nothing left to gain.

**Measured** (probe, 150 random boxes per row with faces at random coordinates, an open sheet whose rim lies on four
side faces, pushed inside by 1 to `d` ulps per side, so four coordinate pairs; cured = the sheet cuts on the grid, the
result is clean; without a grid 0 of 20 cut):

| box at | `d` | `2^6` | `2^8` | `2^10` | `2^12` | `2^14` | `2^16` |
|---|---|---|---|---|---|---|---|
| 0 | 2 ulp | 128 | 146 | 149 | 149 | 150 | 150 |
| 0 | 16 ulp | 84 | 131 | 140 | 149 | 149 | 150 |
| 1000 | 2 ulp | 138 | 147 | 150 | 149 | 150 | 150 |
| 1000 | 16 ulp | 90 | 133 | 145 | 149 | 150 | 150 |
| 6.6e6 | 2 ulp | 140 | 150 | 147 | 150 | 150 | 150 |
| 6.6e6 | 16 ulp | 87 | 136 | 143 | 150 | 150 | 150 |

The counts follow `n * d / g`. `2^12` is the smallest cell that cures 149 of 150 or more in every row; the two
coarser cells cure at most one case more per row. The probe was a one-off run with `InputGrid` set to each cell;
`SolidBooleanPipelineGridStepTests.TheCell_CuresRandomFlushRims` keeps a reduced form of the `2^12` column (60 boxes,
`d` = 16 ulp) as a test.

The rounding case of the tests (the fragment of `SolidBooleanPipelineInputGridTests`, two faceted solids at coordinates
up to 6.6e6 whose shared faces differ by noise of about 1e-8, 11 ulp) succeeds at every cell from `2^1` to `2^24` ulp.
At 5 of these 24 cells, `2^12` among them, the snapped solids touch along one contact, which the final check reports as
it does for any run (`NotCleanReason`, `OnlyContacts`); the result is closed and manifold. That is a property of that
input at that cell, not a reason to move the constant: one sample does not rank neighbouring cells.

## What the step does not cure

A snap makes coordinates equal. It puts a rim onto a face that is parallel to a coordinate plane, and it merges
vertices that are meant to be one point. It does not put a rim onto a slanted face: the snapped rim vertices lie within
a cell of the plane, on either side, so a rim a few ulps inside a slanted face stays refused unless its vertices
coincide with vertices of the face. Then the retry fails, and the first failure is the result. Curing that case needs a
projection of the rim onto the surface, which is a different step (it moves vertices by a rule about the other input,
not by a grid).

## One cell, no ladder

The step runs once with one cell. A ladder of coarser cells would cure some of the remaining misses (the rounding
boundaries of `g` and `2 g` are disjoint, so a pair that `g` separates is merged by `2 g`), at the price of a retry per
cell on inputs that no grid cures, which is the common case of a second failure (a rim that really ends inside, a
slanted face), and of a result that depends on which cell first succeeded. A caller who wants a coarser grid states it
as `InputGrid`, which the step never overrides.

## Where the step sits in a run

- A run that succeeds is never retried.
- A tool that the sheet route tried and that was refused with `RimInsideWorkpiece` is retried on the grid before the
  fallback to solids: the rim was probably meant to be flush, and the fallback (with `InputRepair`) would close the
  sheet as a solid, which changes its meaning. If the sheet is refused on the grid too, the fallback runs as without
  the step, off the grid.
- Otherwise the retry repeats the run that failed, on the same route (after a fallback: with the inputs as solids).
- One retry per `Compute`: a rounding refusal of the fallback after a failed retry of the sheet is not retried.
- A retry that fails leaves the first failure as the result (status, error, report), with one `Grid` step in
  `Report.Route` that says so; a cancelled retry is `Cancelled`.
- `ConfigureBoolean` runs once more for the retry. The cost of a failed retry is one more run up to its failure (for a
  routed sheet with a rim inside: three arrangements, the sheet, the sheet on the grid, the fallback).
- A retry counts as a success under the rule of every run (`Status` is `Success`); `Report.NotCleanReason` reports the
  final check as always.

## Prior art

How other kernels pick a snapping distance from the data (documentation and papers only; no code was read or ported):

- JTS, `OverlayNGRobust` (javadoc): the overlay runs in floating precision first and, only when that fails, again with
  "a SnappingNoder using an automatically-determined snap tolerance", repeated "with increasing snap tolerance, up to a
  limit", and finally with snap rounding at "an automatically-determined scale factor intended to preserve input
  precision". The tolerance is derived from the largest ordinate magnitude of the inputs; the factor is not part of the
  published documentation. The same shape as here: retry only after a failure, the distance from the magnitude of the
  data. JTS grows the tolerance in steps; this step uses one cell (above).
- Manifold (project discussion, pull request 1836 and issue 2060): the tolerance of a mesh is a relative precision of
  about 1e-12 times the scale of its bounding box. `2^12 ulp(M)` is between 4.5e-13 and 9.1e-13 of `M`, the same order.
- Snap rounding (J. Hobby, *Practical segment intersection with finite precision output*, Comput. Geom. 13, 1999;
  D. Halperin, E. Packer, *Iterated snap rounding*, Comput. Geom. 23, 2002) takes the pixel grid as given.

## Tests

`SolidBooleanPipelineGridStepTests`: the flush rim (refused without the step, cut with it, as a routed and as a stated
sheet), the rounding case, the retry that fails too, the cases where the step does not run, a cancelled retry, the cell
(`DataGrid`), reuse, and the invariance over `Similarity.All()` (bit for bit under the `2^k` scales).
