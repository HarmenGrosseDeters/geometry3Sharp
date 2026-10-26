# RemeshBenchmark

`Remesher` vs `RemesherPro` with the settings of the library's internal callers of `Remesher`, to decide whether
they should switch to `RemesherPro` (which, after a first full pass, only processes queued edges and starts with
split-only passes).

## Usage
```
mkdir -p benchmarks/RemeshBenchmark/results
dotnet run -c Release --project benchmarks/RemeshBenchmark -- runs=5 > benchmarks/RemeshBenchmark/results/procedural.txt
python3 benchmarks/RemeshBenchmark/fetch_data.py          # optional: 20 clean Thingi10K meshes to data/meshes
dotnet run -c Release --project benchmarks/RemeshBenchmark -- runs=3 thingi=benchmarks/RemeshBenchmark/data/meshes only=thingi > benchmarks/RemeshBenchmark/results/thingi.txt
python3 benchmarks/RemeshBenchmark/summarize.py benchmarks/RemeshBenchmark/results/thingi.txt
```
`only=<regex>` selects cases. The program prints one JSON line per case and configuration, then a markdown
table. No mesh files are committed; Thingi10K models carry individual licenses (`data/input_summary.csv`). The
selection (seed 42 over the current Thingi10K metadata) was: 129918 1043471 39460 299291 242225 135363 125069
89451 118679 996810 63709 105686 1053376 121869 237642 267659 78295 104582 93370 196126.

RemesherPro is deterministic only with the sorted edge queue of PR #151; without it the RemesherPro rows vary
from run to run.

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
- `open_stretched`: open heightfield, 64 x 40 cells on 4 x 1 (cell aspect 2.5), target 1 x mean edge, boundary kept.
- `terrain_band`: the band `TerrainSafetyLines` remeshes, built as there from an 80 x 80 m pit on a 1 m grid
  (plateau, 35 degree slope, straight crest at y = 40): edges longer than 2 h near the crest split
  (`refine_near_sources`), then the triangles within 3 d + 2 h of the crest cut out. The exact distance along the
  surface to the crest is known, so the AlongSurface line (heat method from the vertices nearest the crest, iso
  line at d + 0.35 h, as in `TerrainSafetyLines`) computed on each remeshed band is checked against it:
  `band min/max excess` is the exact distance of the line points minus d (negative = too close).
- `terrain_band_bumpy`: the same with 0.3 m bumps (no exact distance).
- `terrain_safety`: `TerrainSafetyLines` AlongSurface end to end on the full 80 x 80 m pit, with whatever remesher
  the library uses (before/after a switch).
- `thingi_*` (optional): 20 closed, manifold, single-component Thingi10K meshes with 2k..50k faces (seed 42),
  target 1 x mean edge (the `g3cli remesh` default).

## Metrics
- `time s`: median of `runs` runs (after one warm-up run, a full GC before each), parallel smoothing and projection
  on (the default). It includes what the callers pay besides the passes: the projection target (AABB tree) and the
  constraints.
- `tris`; `len mean/p5/p95`: edge lengths relative to the target.
- `q min/p5`: triangle quality 4 sqrt(3) area / sum of squared edge lengths (1 equilateral, 0 degenerate);
  `q<0.1`: the number of poor triangles.
- `hausdorff/target`: symmetric Hausdorff distance to the input (`MeshHausdorffDistance`, upper bound, within
  0.01 target of the lower bound) / target.
- `det.`: all runs bit-identical; `serial=`: parallel equals serial (smoothing and projection);
  `2^-10=`, `2^10=`: the input scaled by 2^k and the result scaled back is bit-identical (scaling by 2^k is exact,
  so a scale-invariant method gives the same bits).
- `not_clean` (JSON only): `MeshRobustRepair.NotCleanReason` for closed inputs.

## Results
Measured on 4 cores (net10.0, Release) with the RemesherPro determinism fix (sorted edge queue, PR #151 at
8517f16; Remesher is unchanged by it); `runs=5` procedural, `runs=3` Thingi10K. All Hausdorff computations
converged.

### Procedural inputs and the TerrainSafety band
| case | config | time s | tris | len mean | len p5 | len p95 | q min | q p5 | q<0.1 | hausdorff/target | det. | serial= | 2^-10= | 2^10= | band min/max excess |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---|---|---|
| sphere_refine | remesher | 0.595 | 24446 | 0.953 | 0.765 | 1.137 | 0.638 | 0.946 | 0 | 0.04 | True | True | True | True |  |
| sphere_refine | pro_fastest | 0.336 | 24390 | 0.955 | 0.764 | 1.143 | 0.698 | 0.943 | 0 | 0.042 | True | True | False | False |  |
| sphere_refine | pro_iter | 0.450 | 24340 | 0.955 | 0.765 | 1.137 | 0.346 | 0.945 | 0 | 0.04 | True | True | False | False |  |
| bumpy_uniform | remesher | 2.029 | 74750 | 0.963 | 0.759 | 1.23 | 0.59 | 0.847 | 0 | 0.043 | True | True | False | True |  |
| bumpy_uniform | pro_fastest | 0.608 | 74254 | 0.966 | 0.761 | 1.231 | 0.33 | 0.848 | 0 | 0.041 | True | True | False | False |  |
| bumpy_uniform | pro_iter | 0.782 | 74544 | 0.964 | 0.76 | 1.232 | 0.454 | 0.847 | 0 | 0.041 | True | True | False | False |  |
| noisy_coarsen | remesher | 3.151 | 32456 | 0.952 | 0.748 | 1.15 | 0 | 0.885 | 23 | 0.092 | True | True | False | True |  |
| noisy_coarsen | pro_fastest | 3.532 | 32400 | 0.952 | 0.746 | 1.153 | 0.009 | 0.881 | 29 | 0.09 | True | True | False | False |  |
| noisy_coarsen | pro_iter | 3.615 | 32450 | 0.952 | 0.747 | 1.151 | 0 | 0.881 | 16 | 0.09 | True | True | False | False |  |
| open_stretched | remesher | 0.085 | 3890 | 0.949 | 0.765 | 1.13 | 0.706 | 0.928 | 0 | 0.032 | True | True | True | True |  |
| open_stretched | pro_fastest | 0.059 | 3809 | 0.957 | 0.762 | 1.151 | 0.678 | 0.936 | 0 | 0.035 | True | True | False | False |  |
| open_stretched | pro_iter | 0.057 | 3940 | 0.941 | 0.752 | 1.13 | 0.774 | 0.938 | 0 | 0.033 | True | True | False | False |  |
| terrain_band | remesher | 0.141 | 18062 | 0.948 | 0.749 | 1.156 | 0.024 | 0.88 | 1 | 0.01 | True | True | True | True | 0.231 / 0.441 |
| terrain_band | pro_fastest | 0.091 | 18251 | 0.945 | 0.728 | 1.195 | 0.116 | 0.862 | 0 | 0.01 | True | True | True | True | 0.235 / 0.383 |
| terrain_band | pro_iter | 0.123 | 18002 | 0.95 | 0.746 | 1.17 | 0 | 0.874 | 2 | 0.01 | True | True | True | True | 0.205 / 0.386 |
| terrain_band_bumpy | remesher | 0.157 | 18505 | 0.939 | 0.742 | 1.144 | 0.02 | 0.882 | 3 | 0.238 | True | True | True | True |  |
| terrain_band_bumpy | pro_fastest | 0.130 | 18716 | 0.932 | 0.736 | 1.141 | 0.196 | 0.897 | 0 | 0.229 | True | True | True | True |  |
| terrain_band_bumpy | pro_iter | 0.137 | 18664 | 0.936 | 0.745 | 1.148 | 0.017 | 0.875 | 3 | 0.23 | True | True | True | True |  |
With `runs=15` the band remesh takes 0.139 s (Remesher) / 0.098 s (`pro_fastest`) / 0.132 s (`pro_iter`) on
`terrain_band`, and 0.157 / 0.123 / 0.140 s on `terrain_band_bumpy`.

`terrain_safety` (TerrainSafetyLines AlongSurface end to end with Remesher, `runs=15`): 0.350 s, 18186
remeshed triangles, deterministic, line points 0.211..0.396 beyond d (none too close). With the band remeshed by
`FastestRemesh(8)` instead (prototype): 0.280 s, 0.214..0.434 beyond d.

### Thingi10K (20 meshes, target = mean edge length, the `g3cli remesh` default)
```
20 cases
pro_fastest: speedup median 0.74 (min 0.27, max 1.48), total time ratio 0.58; hausdorff ratio median 1.03 (max 3.88); triangles q < 0.1: 6335 vs 5763; deterministic and serial-equal: 20/20 vs 20/20
pro_iter: speedup median 0.89 (min 0.60, max 1.34), total time ratio 0.87; hausdorff ratio median 1.00 (max 3.08); triangles q < 0.1: 5899 vs 5763; deterministic and serial-equal: 20/20 vs 20/20
```
Speedup = Remesher time / RemesherPro time per mesh (> 1: RemesherPro faster); total time ratio = sum of the
Remesher times / sum of the RemesherPro times. Worst cases: thingi_104582 `pro_fastest` 0.27x (RemesherPro
3.04 s vs Remesher 0.82 s); thingi_78295 `pro_fastest` 0.50x with the Hausdorff distance 6.02 vs 4.13 targets and
331 vs 80 triangles with q < 0.1; thingi_1053376 Hausdorff 1.64 (`pro_fastest`) / 1.40 (`pro_iter`) vs 0.45
targets; thingi_129918 `pro_fastest` 1.16 vs 0.30.

### TerrainSafetyLines on real terrain
`benchmarks/TerrainBenchmark` AlongSurface cases (`safety ... mode=surface`) on the DGM1 2022 of the Hambach mine
(windows C250 with d = 5 and C500 with d = 10 of `run_all.sh`, the grid TIN and the simplified `tdc` TIN), with
the band remeshed by Remesher (today) and by RemesherPro (prototype in `TerrainSafety.cs`), measured on top of
PR #151. Remesh step alone (stopwatch around it, median of 3):

| window, terrain | Remesher | `FastestRemesh(8)` | 8 x `RemeshIteration` |
|---|---:|---:|---:|
| C250 d=5 grid | 4.47 s | 3.47 s (1.29x) | 3.66 s (1.22x) |
| C250 d=5 tdc | 3.95 s | 4.72 s (0.84x) | 3.76 s (1.05x) |
| C500 d=10 tdc | 5.53 s | 6.12 s (0.90x) | 5.02 s (1.10x) |
| C500 d=10 grid | 5.14 s | 2.74 s (1.88x) | 2.46 s (2.09x) |

End to end (`safety_s`, all checks of the TerrainBenchmark) with 8 x `RemeshIteration`: 12.15 -> 10.37 s (C250 grid,
utm), 11.68 -> 10.40 (local), 10.88 -> 9.89 (C250 tdc, utm), 10.74 -> 10.29 (local), 16.02 -> 14.45 (C500 tdc),
17.48 -> 14.05 (C500 grid): 1.04..1.24x. The results stay equivalent: line points closer than d by the
Dijkstra bound 1106 vs 1120 and 905 vs 900, uncovered source points within 1%, remeshed triangles within 0.4%; all
runs deterministic.

### Findings
- `g3cli remesh`: RemesherPro is faster on the smooth procedural inputs (`bumpy_uniform` 3.3x, `sphere_refine`
  1.8x, `open_stretched` 1.4x), but slower when coarsening (`noisy_coarsen` 0.89x) and on the Thingi10K meshes,
  which are what the command sees: median 0.74x (`FastestRemesh`) / 0.89x (`RemeshIteration`), 0.58x / 0.87x in
  total. The median Hausdorff distance is equal, but single meshes get clearly worse (above), and both variants
  leave more triangles with q < 0.1 (6335 / 5899 vs 5763).
- TerrainSafety band: on the procedural band `FastestRemesh` is 1.42x / 1.27x faster with fewer poor triangles,
  `RemeshIteration` 1.05x / 1.12x. On real terrain `FastestRemesh` is slower on the simplified TIN (0.84..0.90x),
  while 8 x `RemeshIteration` is faster on all four (1.05..2.09x, end to end 1.04..1.24x) with equivalent lines.
- Determinism: with the fix every configuration is bit-identical run to run, and parallel equals serial.
- Scale invariance: without projection (terrain band) all configurations are 2^k scale invariant. With projection
  RemesherPro is not; Remesher is not at 2^-10 on some inputs (`bumpy_uniform`, `noisy_coarsen`, 2 Thingi meshes).
  Both are open (the absolute `ZeroTolerancef` in `RemesherPro.TrackedProjectionPass`; for Remesher probably ties in
  `MeshProjectionTarget.Project`, not verified).
- Poor triangles: both remeshers leave degenerate triangles or self-intersections on 18 of the 20 Thingi10K meshes
  (`MeshRobustRepair.NotCleanReason`); 99% of the triangles with q < 0.1 touch a feature constraint (pre-existing,
  a separate task).

### Decision
- `g3cli remesh` keeps Remesher: RemesherPro is not faster at equal quality on real meshes.
- `TerrainSafetyLines` (AlongSurface) switches to RemesherPro with 8 x `RemeshIteration` and a last full pass
  without smoothing, once the determinism fix (#151) is in master.
