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
- `sphere_refine`: normalized-cube sphere (6.3k triangles), target 0.5 x mean edge (refinement).
- `bumpy_uniform`: bumpy sphere (75k triangles), target 1 x mean edge (equalization).
- `noisy_coarsen`: noisy sphere (303k triangles), target 3 x mean edge (coarsening).
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
Measured on 4 cores (net10.0, Release) with the RemesherPro determinism fix (sorted edge queue, PR #151 at
8517f16); `runs=5` procedural, `runs=3` Thingi10K. Remesher is unchanged by the fix.

### Procedural inputs and the TerrainSafety band
| case | config | time s | tris | len mean | len p5 | len p95 | q min | q p5 | q<0.1 | hausdorff/target | det. | serial= | 2^-10= | 2^10= | band min/max excess |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---|---|---|
| sphere_refine | remesher | 0.542 | 24446 | 0.953 | 0.765 | 1.137 | 0.638 | 0.946 | 0 | 0.125 | True | True | True | True |  |
| sphere_refine | pro_fastest | 0.355 | 24390 | 0.955 | 0.764 | 1.143 | 0.698 | 0.943 | 0 | 0.128 | True | True | False | False |  |
| sphere_refine | pro_iter | 0.442 | 24340 | 0.955 | 0.765 | 1.137 | 0.346 | 0.945 | 0 | 0.125 | True | True | False | False |  |
| bumpy_uniform | remesher | 2.016 | 74750 | 0.963 | 0.759 | 1.23 | 0.59 | 0.847 | 0 | 0.201 | True | True | False | True |  |
| bumpy_uniform | pro_fastest | 0.602 | 74254 | 0.966 | 0.761 | 1.231 | 0.33 | 0.848 | 0 | 0.199 | True | True | False | False |  |
| bumpy_uniform | pro_iter | 0.765 | 74544 | 0.964 | 0.76 | 1.232 | 0.454 | 0.847 | 0 | 0.199 | True | True | False | False |  |
| noisy_coarsen | remesher | 3.178 | 32456 | 0.952 | 0.748 | 1.15 | 0 | 0.885 | 23 | 0.191 | True | True | False | True |  |
| noisy_coarsen | pro_fastest | 3.587 | 32400 | 0.952 | 0.746 | 1.153 | 0.009 | 0.881 | 29 | 0.189 | True | True | False | False |  |
| noisy_coarsen | pro_iter | 3.647 | 32450 | 0.952 | 0.747 | 1.151 | 0 | 0.881 | 16 | 0.19 | True | True | False | False |  |
| open_stretched | remesher | 0.222 | 12795 | 0.992 | 0.866 | 1.252 | 0.717 | 0.856 | 0 | 0.15 | True | True | True | True |  |
| open_stretched | pro_fastest | 0.084 | 12795 | 0.992 | 0.866 | 1.252 | 0.709 | 0.856 | 0 | 0.151 | True | True | False | False |  |
| open_stretched | pro_iter | 0.101 | 12795 | 0.992 | 0.866 | 1.252 | 0.709 | 0.856 | 0 | 0.151 | True | True | False | False |  |
| terrain_band | remesher | 0.161 | 18843 | 0.965 | 0.75 | 1.179 | 0.038 | 0.836 | 2 | 0.139 | True | True | True | True | 0.214 / 0.386 |
| terrain_band | pro_fastest | 0.209 | 19771 | 0.937 | 0.745 | 1.138 | 0.26 | 0.904 | 0 | 0.139 | True | True | True | True | 0.172 / 0.364 |
| terrain_band | pro_iter | 0.191 | 18509 | 0.974 | 0.757 | 1.185 | 0.039 | 0.834 | 1 | 0.139 | True | True | True | True | 0.212 / 0.365 |
| terrain_band_bumpy | remesher | 0.173 | 18920 | 0.964 | 0.755 | 1.175 | 0.046 | 0.844 | 2 | 0.413 | True | True | True | True |  |
| terrain_band_bumpy | pro_fastest | 0.210 | 20000 | 0.933 | 0.741 | 1.132 | 0.289 | 0.907 | 0 | 0.392 | True | True | True | True |  |
| terrain_band_bumpy | pro_iter | 0.194 | 18671 | 0.971 | 0.754 | 1.183 | 0.065 | 0.833 | 2 | 0.41 | True | True | True | True |  |

`terrain_safety` (TerrainSafetyLines AlongSurface end to end, library as is = Remesher): 0.339 s, 18186 remeshed
triangles, deterministic, line points 0.211..0.396 beyond d (none too close).

### Thingi10K (20 meshes, target = mean edge length, the `g3cli remesh` default)
```
20 cases
pro_fastest: speedup median 0.73 (min 0.27, max 1.46), total time ratio 0.61; hausdorff ratio median 1.03 (max 3.67); triangles q < 0.1: 6335 vs 5763; deterministic and serial-equal: 20/20 vs 20/20
pro_iter: speedup median 0.87 (min 0.61, max 1.31), total time ratio 0.86; hausdorff ratio median 1.00 (max 2.77); triangles q < 0.1: 5899 vs 5763; deterministic and serial-equal: 20/20 vs 20/20
```
Speedup = Remesher time / RemesherPro time per mesh (> 1: RemesherPro faster); total time ratio = sum of the
Remesher times / sum of the RemesherPro times. Worst cases for `pro_fastest`: thingi_104582 0.27x (2.77 s vs
0.74 s), thingi_78295 0.50x with the Hausdorff distance 6.06 vs 4.17 targets and 331 vs 80 triangles with q < 0.1,
thingi_1053376 Hausdorff 1.72 vs 0.53 targets, thingi_129918 1.17 vs 0.32.

### Findings
- Speed: RemesherPro is clearly faster only on smooth inputs without features that need refinement or
  equalization (`bumpy_uniform` 3.3x, `open_stretched` 2.6x, `sphere_refine` 1.5x). It is slower when
  coarsening (`noisy_coarsen` 0.89x), on the TerrainSafety band (0.77..0.89x), and on the Thingi10K meshes, which
  are what `g3cli remesh` sees: median 0.73x (`FastestRemesh`) / 0.87x (`RemeshIteration`), 0.61x / 0.86x in total.
- Quality: on the procedural inputs edge lengths, Hausdorff distance and q p5 are equal; q min is lower on
  some (`bumpy_uniform` 0.33 / 0.45 vs 0.59), and `FastestRemesh` gives fewer poor triangles on the terrain band. On Thingi10K the median Hausdorff distance is equal, but single meshes get
  clearly worse (above), and both RemesherPro variants leave more triangles with q < 0.1 (6335 / 5899 vs 5763).
- TerrainSafety checks: the AlongSurface line on the band is safe with all three (min excess 0.17..0.21 > 0);
  no gain from switching.
- Determinism: with the fix all configurations are bit-identical run to run and parallel equals serial.
- Scale invariance: RemesherPro with projection is not 2^k scale invariant (PR #151 makes the absolute
  `ZeroTolerancef` in `TrackedProjectionPass` relative; not in the measured state). Remesher with projection is
  not invariant at 2^-10 either on some inputs (`bumpy_uniform`, `noisy_coarsen`, 2 Thingi meshes): ties between
  equally near triangles in `MeshProjectionTarget.Project` depend on the traversal of `DMeshAABBTree3`, whose boxes
  are padded by an absolute `box_eps` (pre-existing, not addressed here).
- Both remeshers leave degenerate triangles or self-intersections on 18 of the 20 Thingi10K meshes
  (`MeshRobustRepair.NotCleanReason`), with or without the switch (pre-existing).

### Decision
No switch: RemesherPro is not faster at equal quality for either caller. `TerrainSafetyLines` and `g3cli remesh`
keep `Remesher`. MinimalHoleFill keeps RemesherPro (not measured here).
