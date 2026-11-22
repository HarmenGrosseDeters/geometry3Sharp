# IntrinsicIntegerCoordinates

Research spike for plan #1284: integer (normal) coordinates for intrinsic triangulations (Gillespie, Sharp, Crane,
*Integer Coordinates for Intrinsic Geometry Processing*, 2021). The study, the options and the leaning are in
[docs/research/INTRINSIC_INTEGER_COORDINATES_2026-11.md](../../docs/research/INTRINSIC_INTEGER_COORDINATES_2026-11.md).
This is a prototype, not library code, and it changes no library code.

```
dotnet run -c Release --project benchmarks/IntrinsicIntegerCoordinates [-- signpost | integer | cost | library] [--verbose]
```

Without an argument all four stages run (about 6 minutes). The exit code is 1 if a check of the prototype
fails. Raw output of the run the study quotes: [results/run_2026-11-22.txt](results/run_2026-11-22.txt).

## Files

| File | Content |
|---|---|
| `Meshes.cs` | 27 adversarial procedural meshes (no mesh files) and four similarity transforms |
| `NormalCoordinates.cs` | The prototype: normal coordinates and roundabouts next to a library `IntrinsicTriangulation` (which executes the flips and insertions), their update on a flip and on an insertion into a face, tracing of the input edges by integers, positions by unfolding, the polygons of the common subdivision. Implemented from the paper's text. |
| `Program.cs` | The three stages and the checks |
| `mutants.sh` | Deliberate errors in the prototype, to see that the checks catch them |

## Stages

- **signpost** (the upper bound): `ExtractCommonSubdivision` of the library after `FlipToDelaunay` and after
  `DelaunayRefine(25)`, per mesh and transform; counts `Inconsistent` results by reason, and `Success` results
  with false crossings.
- **integer**: the prototype on the same triangulations after flips, after flips and V/2 random insertions, and with
  every fifth inserted point at a barycentric distance of 1e-7 from an edge. Per run:
  - the prototype's own checks: every input edge, traced by integers, ends at the vertex its roundabouts name at
    both ends; every crossing and every input edge is used exactly once; the polygons are a consistently oriented
    conforming complex with the Euler characteristic and the boundary edges of the input; every crossing vertex
    has four edges; the polygon areas sum to the mesh area (relative 1e-6);
  - against the library, where its traced subdivision reports `Success`: the same number of crossings and faces
    and the same crossing positions (1e-7 of the bounding box diagonal), unless the traced one has crossing
    vertices that do not have four edges (false crossings), which are counted;
  - whether the counts are the same over the four transforms.
- **cost**: times of the flips, of V/2 insertions and of the subdivision, library against library plus prototype.
- **library**: the layer of the library (`IntrinsicTriangulation.EnableIntegerCoordinates`, built after the study)
  on the steps it covers, flips and flips plus boundary splits (`DelaunayRefine(0)`): the checks of stage
  integer on its result, the comparison with the traced result of the same steps without the layer, and the
  times of both.

The checks were tried against nine deliberate errors (`bash benchmarks/IntrinsicIntegerCoordinates/mutants.sh`,
output in [results/mutants.txt](results/mutants.txt)): each is caught. The checks do not prove that an inserted
point is in the right region where the library gives no result to compare with (the near-edge step); there only
the area check judges it.

## Results (2026-11-22)

Linux container, .NET 10, Release. Summary; the tables per mesh are in the raw output.

| Step | Prototype valid | Traced `Success` (library) | of these with false crossings |
|---|---|---|---|
| flips | 108 of 108 | 101 of 108 | 0 |
| flips + refinement 25° (library only, stage signpost) | not built | 92 of 108 | 0 |
| flips + insertions | 108 of 108 | 87 of 108 | 11 |
| flips + insertions near edges | 108 of 108 | 0 of 108 | 0 |

Same counts as the identity for the mesh scaled by 2^-30: 81 of 81 groups; over all four transforms: 54 of 81
(the flips differ at ties). Largest normal coordinate: 236. The 16 false crossings all have three edges. In the
near-edge step the library's `InsertVertex` refused 231 points (its strict-inside test, in sliver triangles).

Times (median of 5 for the flips, of 3 otherwise, after a warm-up; shared container, times below 5 ms vary by a
factor of 2):

| Mesh | V | Flips | Flips: library | Flips: + integers | V/2 insertions: library | V/2 insertions: + integers | Subdivision: traced | Subdivision: integers, connectivity | Subdivision: integers, positions and polygons | Crossings |
|---|---|---|---|---|---|---|---|---|---|---|
| obtuse plane dy=0.05 | 1513 | 1368 | 0.7 ms | 0.7 ms | 1.7 ms | 1.9 ms | 14.1 ms | 0.6 ms | 1.3 ms | 1368 |
| ring sphere 160x24 | 3818 | 3480 | 2.0 ms | 2.6 ms | 3.5 ms | 3.7 ms | 54.0 ms | 0.8 ms | 5.0 ms | 3480 |
| sheared torus s=3 | 1280 | 2560 | 1.1 ms | 1.1 ms | 0.7 ms | 1.2 ms | 29.6 ms | 0.3 ms | 1.1 ms | 5120 |
| sheared torus s=9 | 1280 | 6400 | 2.6 ms | 3.0 ms | 0.8 ms | 2.2 ms | 73.5 ms | 0.6 ms | 3.2 ms | 20480 |
| sheared torus s=15 | 1280 | 1800 | 0.8 ms | 1.0 ms | 0.8 ms | 1.0 ms | 18.3 ms | 0.3 ms | 0.9 ms | 2840 |
| sheared torus s=9, 200 x 160 | 32000 | 800000 | 321.1 ms | 371.1 ms | 31.7 ms | 306.1 ms | 15571.3 ms | 44.8 ms | 632.6 ms | 3072000 |
| obtuse plane 40 x 800 | 32441 | 31560 | 18.3 ms | 17.2 ms | 27.3 ms | 39.8 ms | 784.4 ms | 8.6 ms | 17.0 ms | 31560 |

Since the fix of #1313 (2026-11-22) the library reports a false crossing as `Inconsistent`: the row "flips +
insertions" now reads 76 of 108 with 0 false crossings; nothing else changes. The table above and the raw output
are the state before the fix.

## Limits of the prototype

- The library still computes its signposts in every flip and insertion, so "library" times include them and
  "+ integers" is the cost on top, not the cost of an integer-only structure.
- Insertion into a face only: no edge splits, no refinement loop, no input location of inserted vertices.
- The polygons are not attributed to input triangles and have no barycentric corners, which the library's result
  has; that part of the time comparison favours the prototype.
- The meshes are adversarial and small; no corpus was run.

## Library layer (2026-11-22)

`dotnet run -c Release --project benchmarks/IntrinsicIntegerCoordinates -- library`; raw output in
[results/library_2026-11-22.txt](results/library_2026-11-22.txt). Linux container, .NET 10, Release. Before: the
library without the layer (tracing). After: with `EnableIntegerCoordinates`.

| Step | Integers valid | Traced `Success` | Same subdivision where both succeed |
|---|---|---|---|
| flips | 108 of 108 | 101 of 108 | 101 of 101 |
| flips + boundary splits (`DelaunayRefine(0)`) | 108 of 108 | 101 of 108 | 101 of 101 |

The crossings of both lie within 3.0e-10 of the bounding box diagonal of each other. The largest relative
difference between an intrinsic triangle and its faces is 6.1e-5 (stretched grid 10^6) and 1.1e-5 (clustered
ring sphere), on meshes where tracing fails; it is reported and does not fail the result.

Times, identity transform, median of 5 (build) and 3 (subdivision). "Build" is `FlipToDelaunay` or
`DelaunayRefine(0)` including the constructor. The builds of a few milliseconds are noisy (-9 % to +113 % on
runs of 2 to 15 ms); the two large meshes give +20 % and +24 % for the flips.

| Mesh | V | Step | Flips | Boundary splits | Build without layer | Build with layer | Subdivision traced | Subdivision from integers | Crossings |
|---|---|---|---|---|---|---|---|---|---|
| obtuse plane dy=0.05 | 1513 | flips | 1368 | 0 | 2.3 ms | 3.3 ms | 16.8 ms | 1.3 ms (13x) | 1368 |
| obtuse plane dy=0.05 | 1513 | flips + boundary splits | 1388 | 36 | 3.7 ms | 3.4 ms | 14.3 ms | 2.3 ms (6x) | 1448 |
| ring sphere 160x24 | 3818 | flips | 3480 | 0 | 10.4 ms | 12.1 ms | 59.0 ms | 6.1 ms (10x) | 3480 |
| sheared cylinder s=9 | 1280 | flips | 6240 | 0 | 6.8 ms | 14.4 ms | 141.2 ms | 5.1 ms (28x) | 19968 |
| sheared cylinder s=9 | 1280 | flips + boundary splits | 6240 | 0 | 5.5 ms | 6.9 ms | 239.4 ms | 8.5 ms (28x) | 19968 |
| sheared torus s=3 | 1280 | flips | 2560 | 0 | 5.0 ms | 5.6 ms | 42.5 ms | 2.3 ms (18x) | 5120 |
| sheared torus s=9 | 1280 | flips | 6400 | 0 | 5.1 ms | 6.4 ms | 100.6 ms | 11.8 ms (9x) | 20480 |
| sheared torus s=15 | 1280 | flips | 1800 | 0 | 2.4 ms | 3.1 ms | 20.0 ms | 1.6 ms (12x) | 2840 |
| sheared torus s=9, 200 x 160 | 32000 | flips | 800000 | 0 | 538.7 ms | 669.9 ms (+24 %) | 16871.5 ms | 1923.8 ms (9x) | 3072000 |
| obtuse plane 40 x 800 | 32441 | flips | 31560 | 0 | 84.9 ms | 102.3 ms (+20 %) | 1009.0 ms | 109.3 ms (9x) | 31560 |
| obtuse plane 40 x 800 | 32441 | flips + boundary splits | 31580 | 92 | 86.9 ms | 91.4 ms (+5 %) | 988.8 ms | 45.5 ms (22x) | 31640 |

Without the layer the library is unchanged except for one null check per flip.
