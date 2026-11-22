# ContouringSubgridProbe

Upper-bound measurements for the contouring spike of 2026-11-22
([docs/research/CONTOURING_SDF_SUBGRID_2026-11.md](../../docs/research/CONTOURING_SDF_SUBGRID_2026-11.md): the numbers,
their reading, the options). Measurement only, public API only, no library change. Inputs are procedural, rotated and
shifted against the grid per seed.

```
B="dotnet run -c Release --project benchmarks/ContouringSubgridProbe --"
$B sharp 3 > benchmarks/ContouringSubgridProbe/results/sharp.txt    # about 1 min on 4 cores
$B thin 3  > benchmarks/ContouringSubgridProbe/results/thin.txt     # about 4 min
$B cert    > benchmarks/ContouringSubgridProbe/results/cert.txt     # about 5 s
$B cull 512 > benchmarks/ContouringSubgridProbe/results/cull.txt    # about 5 min, 10 GB (cull 256: 1 min, 3 GB)
```

The second argument is the number of seeds (for `cull`: the largest N of the dense run). `results/` holds the raw output of these four commands (sharp, thin, cert on master e340ef5c, cull on de976aa6;
a 4-core Linux container, net10.0, 2026-11-22); each file ends with (or is) the summary table.

| Part | File | What it measures |
|---|---|---|
| `sharp` | `Sharp.cs` | `DualContouring` on a grid of exact signed distances of the six CAD solids of `benchmarks/SharpFeatureProbe` (24 and 48 cells). All variants share the sign grid and differ in the Hermite data: `interp` / `trilin` (grid only, old and current defaults of `DualContouring` on the trilinear interpolant), and through `HermiteF` `plane` (a local grid-only estimator by the empty-ball rule), `oracle` (exact crossing and normal from the input mesh; `oracle-x` / `oracle-n` only one of them). Distance of the truth's sharp edges and corners to the result, edge Chamfer, Hausdorff, in cells |
| `thin` | `Thin.cs` | a rotated plate, a spherical shell and a gyroid sheet clipped to a ball with walls of 0.25 to 1.5 cells on a 32-cell grid: `MarchingCubes`, `DualContouring`, marching cubes at half and a quarter of the cell, and `SubgridMT`, a restricted prototype of Subgrid Marching Tetrahedra (Baktash, Gillespie, Crane 2026, from the paper text). Crossings by 8 samples per edge (`sub8`), by sphere tracing with a Lipschitz bound (`subtrace`; `subtrace-n` pairs scoops nested) or, for fields `max(|g| - w, d)`, from the node values of g and d alone (`sub2level`). Coverage of the true surface, `NotCleanReason`, components and genus, field evaluations |
| `cert` | `Cert.cs` | the number of cells of an interval subdivision with the two tests of Plantinga and Vegter (2004) for the gyroid sheet and the shell: the wall as one function (`single`), as two levels of one function (`pair`), and the mid-surface (`mid`) |
| `cull` | `Cull.cs` | plan #1276: evaluations of a top-down octree build from a Lipschitz bound against the dense grid at N = 128, 256, 512 (sphere, thin plate, gyroid sheet), and the share of the phases `sample` and `crossings` in the dense `DualContouring` run |

Limits of the prototype (so of the numbers): `SubgridMT` fills loops with fans instead of the paper's cases, so it has
no proof of freedom from self-intersections (none was found in 120 distinct runs; 13 of them stop earlier at degenerate triangles)
and it is off by up to 0.7 cells at rims and scoops; `subtrace-n` never differed from `subtrace` (no face had two scoops on one edge); the gyroid's k is nominal (the wall is 0.58 to 0.71 of it); it is serial and keeps its edges in dictionaries, so its times mean nothing.
`cert` counts cells with plain double intervals, without outward rounding and without balancing the octree. The
gyroid has no certified reference topology (`truth` there is a marching-cubes run at an eighth of the cell, which is
often not clean).

Headline numbers (3 seeds; details and all rows in the note):

| | grid-only today | bound or prototype |
|---|---|---|
| sharp, 48 cells: mean distance of the sharp edges to the result, cells | 0.204 | 0.016 (oracle), 0.143 (`plane`) |
| sharp, 48 cells: edge Chamfer / maximum, cells | 0.257 / 0.626 | 0.037 / 0.391 (oracle) |
| thin, plate of 0.25 cells: coverage / right topology | 0.141 / 0 of 3 (`MarchingCubes`) | 0.921 / 3 of 3 (`subtrace`) |
| thin, shell 0.25 to 1.5 cells: two spheres | 3 of 15 (`MarchingCubes`) | 15 of 15 (`sub2level`), 4 of 15 (`subtrace`) |
| thin, gyroid of 0.25 cells: coverage | 0.059 (`MarchingCubes`) | 0.908 (`sub2level`, node values only) |
| cert, gyroid wall of 0.25 cells: cells | 16 181 936 (`single`) | 19 440 (`pair`); mid-surface 18 208 |
| cull, N = 512: evaluations of the dense grid / share of sampling in the dense run | 135.0 M / 40 % sphere, 45 % plate, 6 % gyroid | 2.4 % sphere, 1.0 % plate, 16 % gyroid of the evaluations |
