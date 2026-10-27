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

RemesherPro is deterministic and scale invariant only with PR #151 (sorted edge queue, relative projection
tolerances); without it the RemesherPro rows vary from run to run.

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
- `not_clean` (JSON only): `MeshRobustRepair.NotCleanReasons` for closed inputs, every failed check separated by ", "
  (earlier runs: only the first one, `NotCleanReason`: "degenerate triangles" hid self-intersections and inside-out
  results).

## Results
Measured on 4 cores (net10.0, Release) with PR #151 (head c0a333e, merged as master 3f18aa3: RemesherPro
determinism and scale invariance; Remesher is unchanged by it); `runs=5` procedural, `runs=3` Thingi10K. All Hausdorff computations
converged.

### Procedural inputs and the TerrainSafety band
| case | config | time s | tris | len mean | len p5 | len p95 | q min | q p5 | q<0.1 | hausdorff/target | det. | serial= | 2^-10= | 2^10= | band min/max excess |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---|---|---|
| sphere_refine | remesher | 0.597 | 24446 | 0.953 | 0.765 | 1.137 | 0.638 | 0.946 | 0 | 0.04 | True | True | True | True |  |
| sphere_refine | pro_fastest | 0.388 | 24484 | 0.953 | 0.762 | 1.14 | 0.608 | 0.941 | 0 | 0.041 | True | True | True | True |  |
| sphere_refine | pro_iter | 0.444 | 24288 | 0.956 | 0.765 | 1.14 | 0.77 | 0.946 | 0 | 0.04 | True | True | True | True |  |
| bumpy_uniform | remesher | 2.002 | 74750 | 0.963 | 0.759 | 1.23 | 0.59 | 0.847 | 0 | 0.043 | True | True | False | True |  |
| bumpy_uniform | pro_fastest | 0.702 | 74326 | 0.966 | 0.761 | 1.23 | 0.408 | 0.848 | 0 | 0.039 | True | True | True | True |  |
| bumpy_uniform | pro_iter | 0.861 | 74442 | 0.965 | 0.76 | 1.23 | 0.527 | 0.848 | 0 | 0.039 | True | True | False | True |  |
| noisy_coarsen | remesher | 3.168 | 32456 | 0.952 | 0.748 | 1.15 | 0 | 0.885 | 23 | 0.092 | True | True | False | True |  |
| noisy_coarsen | pro_fastest | 4.013 | 32334 | 0.954 | 0.749 | 1.153 | 0.008 | 0.877 | 13 | 0.091 | True | True | False | True |  |
| noisy_coarsen | pro_iter | 4.269 | 32394 | 0.952 | 0.749 | 1.153 | 0 | 0.884 | 11 | 0.089 | True | True | False | True |  |
| open_stretched | remesher | 0.083 | 3890 | 0.949 | 0.765 | 1.13 | 0.706 | 0.928 | 0 | 0.032 | True | True | True | True |  |
| open_stretched | pro_fastest | 0.062 | 3796 | 0.958 | 0.765 | 1.145 | 0.665 | 0.939 | 0 | 0.033 | True | True | False | False |  |
| open_stretched | pro_iter | 0.068 | 3921 | 0.943 | 0.751 | 1.138 | 0.719 | 0.929 | 0 | 0.033 | True | True | True | True |  |
| terrain_band | remesher | 0.139 | 18062 | 0.948 | 0.749 | 1.156 | 0.024 | 0.88 | 1 | 0.01 | True | True | True | True | 0.231 / 0.441 |
| terrain_band | pro_fastest | 0.095 | 18325 | 0.944 | 0.724 | 1.194 | 0.174 | 0.858 | 0 | 0.01 | True | True | True | True | 0.232 / 0.459 |
| terrain_band | pro_iter | 0.159 | 18048 | 0.948 | 0.749 | 1.158 | 0.028 | 0.885 | 1 | 0.01 | True | True | True | True | 0.215 / 0.363 |
| terrain_band_bumpy | remesher | 0.157 | 18505 | 0.939 | 0.742 | 1.144 | 0.02 | 0.882 | 3 | 0.238 | True | True | True | True |  |
| terrain_band_bumpy | pro_fastest | 0.144 | 18708 | 0.932 | 0.735 | 1.141 | 0.041 | 0.903 | 1 | 0.223 | True | True | True | True |  |
| terrain_band_bumpy | pro_iter | 0.167 | 18432 | 0.941 | 0.746 | 1.148 | 0.017 | 0.881 | 2 | 0.242 | True | True | True | True |  |
`terrain_safety` (TerrainSafetyLines AlongSurface end to end): 0.350 s with Remesher (`runs=15`, 18186 remeshed
triangles, line points 0.211..0.396 beyond d); 0.336 s with the band remeshed by 8 x `RemeshIteration` (prototype,
`runs=5`, 18116 triangles, 0.215..0.383 beyond d). Both deterministic, no line point too close.

### Thingi10K (20 meshes, target = mean edge length, the `g3cli remesh` default)
Measured before `Remesher.RejectInvertingPasses` (default on since): it changes only thingi_135363 (passes that
turn it inside out are rolled back, in all three configurations) and costs 5..11% of the time.
```
20 cases
pro_fastest: speedup median 0.67 (min 0.20, max 1.31), total time ratio 0.44; hausdorff ratio median 1.02 (max 3.68); triangles q < 0.1: 6365 vs 5763; deterministic and serial-equal: 20/20 vs 20/20
pro_iter: speedup median 0.81 (min 0.56, max 1.18), total time ratio 0.80; hausdorff ratio median 1.00 (max 1.52); triangles q < 0.1: 5830 vs 5763; deterministic and serial-equal: 20/20 vs 20/20
```
Speedup = Remesher time / RemesherPro time per mesh (> 1: RemesherPro faster); total time ratio = sum of the
Remesher times / sum of the RemesherPro times. Worst cases: thingi_104582 `pro_fastest` 0.20x (RemesherPro 4.43 s
vs Remesher 0.89 s); thingi_78295 `pro_fastest` 0.41x with the Hausdorff distance 5.43 vs 4.13 targets and 333 vs
80 triangles with q < 0.1; thingi_129918 `pro_fastest` Hausdorff 1.10 vs 0.30 targets, thingi_1053376 1.58 vs 0.45.

### TerrainSafetyLines on real terrain (earlier state of #151)
`benchmarks/TerrainBenchmark` AlongSurface cases (`safety ... mode=surface`) on the DGM1 2022 of the Hambach mine
(windows C250 with d = 5 and C500 with d = 10 of `run_all.sh`, the grid TIN and the simplified `tdc` TIN), with
the band remeshed by Remesher and by RemesherPro (prototype in `TerrainSafety.cs`), measured on top of PR #151 at
8517f16 (sorted queue only). Remesh step alone (stopwatch around it, median of 3):

| window, terrain | Remesher | `FastestRemesh(8)` | 8 x `RemeshIteration` |
|---|---:|---:|---:|
| C250 d=5 grid | 4.47 s | 3.47 s (1.29x) | 3.66 s (1.22x) |
| C250 d=5 tdc | 3.95 s | 4.72 s (0.84x) | 3.76 s (1.05x) |
| C500 d=10 tdc | 5.53 s | 6.12 s (0.90x) | 5.02 s (1.10x) |
| C500 d=10 grid | 5.14 s | 2.74 s (1.88x) | 2.46 s (2.09x) |

End to end with 8 x `RemeshIteration`: 1.04..1.24x, with equivalent lines (closer than d by the Dijkstra bound 1106
vs 1120 and 905 vs 900, uncovered source points within 1%).

### Findings
- `g3cli remesh`: RemesherPro is faster on the smooth procedural inputs (`bumpy_uniform` 2.9x, `sphere_refine`
  1.5x, `open_stretched` 1.3x), but slower when coarsening (`noisy_coarsen` 0.79x) and on the Thingi10K meshes,
  which are what the command sees: median 0.67x (`FastestRemesh`) / 0.81x (`RemeshIteration`), 0.44x / 0.80x in
  total, with single meshes clearly worse in the Hausdorff distance (above).
- TerrainSafety band: `FastestRemesh` is faster on the procedural band (0.095 vs 0.139 s, 0.144 vs 0.157 s, fewer
  poor triangles), but slower on the simplified real terrain (`tdc`: 0.84x, 0.90x on the earlier state of #151),
  so it is not a candidate. 8 x `RemeshIteration` was faster on real terrain (1.05..2.09x) and on the procedural
  band on the earlier state of #151; with the final #151 (fixed edge order, prime-step walk) it is slower on the
  procedural band (0.159 vs 0.139 s, 0.167 vs 0.157 s), and switching TerrainSafetyLines to it fails an existing
  test: the band of `TerrainSafetyTests.SafetyLine_AlongSurface_BandIsOneCleanPiece` keeps a zero-area triangle
  (Remesher: none).
- Determinism: with #151 every configuration is bit-identical run to run, and parallel equals serial.
- Scale invariance: without projection (terrain band) all configurations are 2^k scale invariant. With projection
  neither is on every input: Remesher fails at 2^-10 on `bumpy_uniform` and `noisy_coarsen` (and 2 Thingi
  meshes), RemesherPro on `bumpy_uniform` (`pro_iter`), `noisy_coarsen` (both) and `open_stretched`
  (`pro_fastest`, both scales). Probably ties in `MeshProjectionTarget.Project` (the nearest of equally near
  triangles depends on the AABB traversal), not verified.
- Poor triangles: both remeshers leave degenerate triangles or self-intersections on 18 of the 20 Thingi10K meshes
  (`MeshRobustRepair.NotCleanReason`); 99% of the triangles with q < 0.1 touch a feature constraint. A separate
  opt-in quality pass follows in its own PR.

### Decision
- `g3cli remesh` keeps Remesher: RemesherPro is not faster at equal quality on real meshes.
- `TerrainSafetyLines` keeps Remesher: `FastestRemesh` is slower on simplified real terrain, and 8 x
  `RemeshIteration` is with the final #151 slower on the band and leaves a degenerate triangle in an existing
  test. To be measured again with the quality pass.

Open bug (docs/ROADMAP.md, Phase 2): a remesher must not leave degenerate triangles; RemesherPro leaves a zero-area
triangle in the band of `TerrainSafetyTests.SafetyLine_AlongSurface_BandIsOneCleanPiece`, and both remeshers leave
degenerate triangles on most Thingi10K meshes.

## Further fidelity measures (`fm_*`, `MeshFidelityMetrics`)

Every row also carries the `fm_*` fields of `benchmarks/Shared/FidelityFields.cs` against the input: F-score,
normal consistency, reversed share, Edge Chamfer distance and edge precision / recall on sharp edges, Verdict
triangle quality of the result and topology changes. All relative to the input's size; 20k samples per mesh. A
second table prints them, and `summarize.py` adds their medians. Procedural cases, `runs=1`, branch of #157:

| case | config | F 0.1 % | Edge Chamfer | edge recall 0.5 % | edge precision 0.5 % | min angle 1 % worst |
|---|---|---:|---:|---:|---:|---:|
| terrain_band | remesher | 1.0000 | 0.00000 | 1.000 | 1.000 | 33.6 |
| terrain_band | pro_fastest | 1.0000 | 0.00072 | 1.000 | 0.971 | 29.6 |
| terrain_band | pro_iter | 1.0000 | 0.00000 | 1.000 | 1.000 | 33.6 |
| terrain_band_bumpy | remesher | 0.9550 | 0.00050 | 0.919 | 1.000 | 31.7 |
| terrain_band_bumpy | pro_fastest | 0.9782 | 0.00040 | 0.957 | 0.993 | 34.3 |
| terrain_band_bumpy | pro_iter | 0.9549 | 0.00199 | 0.913 | 0.958 | 30.6 |
| noisy_coarsen | remesher | 0.9999 | 0.08886 | 0.099 | 0.635 | 22.4 |
| noisy_coarsen | pro_fastest | 1.0000 | 0.06781 | 0.124 | 0.777 | 22.1 |

The spheres and the open heightfield have no sharp edges and an F-score of 1 in all three configurations. What
Hausdorff (ratio 1.00 between the configurations) and `q < 0.1` do not show:
- On `terrain_band`, `pro_fastest` adds sharp edges that the input does not have (edge precision 0.97) and has
  worse small angles (1 % worst 29.6° instead of 33.6°).
- On `terrain_band_bumpy`, `pro_fastest` keeps the surface and the break lines better (F 0.978 instead of 0.955,
  edge recall 0.96 instead of 0.92), while `pro_iter` moves the edges it keeps (Edge Chamfer 4x).
- On `noisy_coarsen` the "sharp edges" are the noise that the coarsening is meant to remove, so a low edge recall
  is expected there.
