# IntrinsicIntegerCoordinates

Research spike for plan #1284: integer (normal) coordinates for intrinsic triangulations (Gillespie, Sharp, Crane,
*Integer Coordinates for Intrinsic Geometry Processing*, 2021). The study, the options and the leaning are in
[docs/research/INTRINSIC_INTEGER_COORDINATES_2026-11.md](../../docs/research/INTRINSIC_INTEGER_COORDINATES_2026-11.md).
This is a prototype, not library code, and it changes no library code.

```
dotnet run -c Release --project benchmarks/IntrinsicIntegerCoordinates [-- signpost | integer | cost] [--verbose]
```

Without an argument all three stages run (about 3 minutes). The exit code is 1 if a check of the prototype
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

## Limits of the prototype

- The library still computes its signposts in every flip and insertion, so "library" times include them and
  "+ integers" is the cost on top, not the cost of an integer-only structure.
- Insertion into a face only: no edge splits, no refinement loop, no input location of inserted vertices.
- The polygons are not attributed to input triangles and have no barycentric corners, which the library's result
  has; that part of the time comparison favours the prototype.
- The meshes are adversarial and small; no corpus was run.
