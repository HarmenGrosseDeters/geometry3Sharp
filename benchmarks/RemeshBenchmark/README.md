# RemeshBenchmark

`Remesher` vs `RemesherPro` with the settings of the library's internal callers of `Remesher`, to decide whether
they should switch to `RemesherPro` (which, after a first full pass, only processes queued edges and starts with
split-only passes).

## Usage
```
dotnet run -c Release --project benchmarks/RemeshBenchmark -- runs=5 > results/procedural.txt
python3 benchmarks/RemeshBenchmark/fetch_data.py          # optional: 20 clean Thingi10K meshes to data/meshes
dotnet run -c Release --project benchmarks/RemeshBenchmark -- runs=3 thingi=benchmarks/RemeshBenchmark/data/meshes only=thingi > results/thingi.txt
python3 benchmarks/RemeshBenchmark/summarize.py results/thingi.txt
```
`only=<regex>` selects cases. The program prints one JSON line per case and configuration, then a markdown
table. No mesh files are committed; Thingi10K models carry individual licenses (`data/input_summary.csv`).

## Configurations
| caller | Remesher today | `pro_fastest` | `pro_iter` |
|---|---|---|---|
| `g3cli remesh` (defaults) | 20 x `BasicRemeshPass`, smoothing 0.5, projection onto the input (`MeshProjectionTarget.Auto`), boundary and 30 degree creases kept | `FastestRemesh(20)` (fast splits, then 20 queued iterations, projection on every other one and on the last) | 20 x `RemeshIteration` (first full, then queued) |
| `TerrainSafetyLines` AlongSurface band | 8 x `BasicRemeshPass`, smoothing 0.5, boundary and 20 degree break lines kept, then one pass without smoothing; no projection | `FastestRemesh(8)`, then one full `RemeshIteration` without smoothing | 8 x `RemeshIteration`, then one full one without smoothing |

The target edge length is a multiple of the input's mean edge length (`g3cli`'s default is 1x), and h = d / 8 =
0.625 for the terrain band (d = 5, `TerrainSafetyLines.EdgeLength` default).

## Inputs
- `sphere_refine`: normalized-cube sphere (3.5k triangles), target 0.5 x mean edge (refinement).
- `bumpy_uniform`: bumpy sphere (77k triangles), target 1 x mean edge (equalization).
- `noisy_coarsen`: noisy sphere (307k triangles), target 3 x mean edge (coarsening).
- `open_stretched`: open heightfield with anisotropic triangles (aspect 2.5), target 1 x mean edge, boundary kept.
- `terrain_band`: the band `TerrainSafetyLines` remeshes (within 3 d + 2 h of the crest, triangles of edge 2 h, as
  `refine_near_sources` leaves them) at a pit edge: plateau, 35 degree slope, straight crest. The exact distance
  along the surface to the crest is known, so the AlongSurface line (heat method from the vertices nearest the
  crest, iso line at d + 0.35 h, as in `TerrainSafetyLines`) computed on each remeshed band is checked against it:
  `band min/max excess` is the exact distance of the line points minus d (negative = too close).
- `terrain_band_bumpy`: the same with 0.3 m bumps (no exact distance).
- `terrain_safety`: `TerrainSafetyLines` AlongSurface end to end on the full 80 x 80 m pit, with whatever remesher
  the library uses (before/after a switch).
- `thingi_*` (optional): 20 closed, manifold, single-component Thingi10K meshes with 2k..50k faces (seed 42),
  target 1 x mean edge (the `g3cli remesh` default).

## Metrics
- `time s`: median of `runs` runs (after one warm-up run), parallel smoothing and projection on (the default).
- `tris`; `len mean/p5/p95`: edge lengths relative to the target.
- `q min/p5`: triangle quality 4 sqrt(3) area / sum of squared edge lengths (1 equilateral, 0 degenerate);
  `q<0.1`: the number of poor triangles.
- `hausdorff/target`: symmetric Hausdorff distance to the input (`MeshHausdorffDistance`, upper bound) / target.
- `det.`: all runs bit-identical; `serial=`: parallel equals serial (smoothing and projection);
  `2^-10=`, `2^10=`: the input scaled by 2^k and the result scaled back is bit-identical (scaling by 2^k is exact,
  so a scale-invariant method gives the same bits).
- `not_clean` (JSON only): `MeshRobustRepair.NotCleanReason` for closed inputs.

## Results
RESULTS
