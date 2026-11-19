# TetMeshBenchmark

Stage 0 of [docs/design/TET_MESHING.md](../../docs/design/TET_MESHING.md): the insertion rate of `Delaunay3` and the tetrahedral
mesh that the inside cells of `MeshAlphaWrap` give (`MeshAlphaWrap.ExtractTetMesh`), with validity and quality.
Measured 2026-11-09 in the cloud container (single thread, Release, net10.0; the machine is shared, so read timings as
orders of magnitude).

```
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- insert [n ...]
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- wrap [relativeAlpha] [relativeOffset]
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- file <mesh.obj|stl|off> [relativeAlpha] [relativeOffset]
```

Thingi10K subset: every 250th id of `metadata/file_ids.txt` of the Hugging Face dataset `Thingi10K/Thingi10K`
(`raw_meshes/<id>.stl`; zenodo, appspot and github.com are blocked in the cloud container, huggingface.co is reachable).
Meshes must not be committed; per-mesh result rows are in `results/thingi10k-subset-2026-11-09.csv`, produced by
`dotnet <TetMeshBenchmark.dll> file <id>.stl 20 600` per mesh (timeout 180 s; the 126 MB mesh 120628 separately with 900 s).
Summary and the comparison with fTetWild's published numbers: section 10.1 of the design note.

## Delaunay3 insertion rate (`insert 20000 100000 400000`, uniform random points in the unit box, `minSeparation` 0)

```
insert,random order,20000,20000,133874,517.1 ms,25.86 us/point,3.863 us/cell
insert,grid-sorted,20000,20000,133874,251.3 ms,12.56 us/point,1.877 us/cell
insert,random order,100000,100000,672106,3513.8 ms,35.14 us/point,5.228 us/cell
insert,grid-sorted,100000,100000,672106,1303.0 ms,13.03 us/point,1.939 us/cell
insert,random order,400000,400000,2696480,27083.6 ms,67.71 us/point,10.044 us/cell
insert,grid-sorted,400000,400000,2696480,5243.9 ms,13.11 us/point,1.945 us/cell
```

Grid-sorted (coarse 32^3 cell order) is the realistic case: about **13 us per point, 1.9 us per final cell**, constant in n.
Random order degrades with n (the point location walk gets long). The design note had assumed 1-2 us per point.

## Inside cells of the wrap, relative alpha 20, relative offset 600 (`wrap 20 600`)

All eight outputs are valid (`TetMeshQuality.NotValidReason` null; the test oracle agrees on the test-sized inputs).

```
input,status,input_tris,steiner_points,wrap_ms,extract_ms,validity_ms,measure_ms,tets,boundary_tris,wrap_us_per_tet,extract_us_per_tet,re_worst,re_p1,re_p5,re_median,re_gt2,mindih_worst,mindih_p1,mindih_p5,mindih_median,maxdih_worst,dih_lt10,dih_lt5,dih_lt1,maxdih_gt170,valid
sphere n=6,Success,300,477,141.349,1.927,14.3526,6.6732,1358,950,104.086,1.419,18.2482,12.1469,8.2474,3.64743,1190,0.0230192,0.0241812,1.96638,9.23615,179.967,743,277,30,215,yes
sphere n=16,Success,2700,794,180.713,18.4928,21.8974,5.9161,2271,1584,79.574,8.14302,11.9152,11.1009,10.5501,7.34638,2241,0.460172,0.78276,1.62062,5.14077,179.185,2081,1075,49,1120,yes
sphere n=40,Success,18252,886,239.256,1.2829,4.3724,5.7738,2465,1768,97.0615,0.520446,10.066,9.81011,9.46833,8.7624,2420,0.103156,0.418218,1.18733,4.23716,179.835,2177,1431,90,1432,yes
box 1x0.5x0.25,Success,12,602,63.643,0.6343,11.5943,3.1641,1606,1200,39.6283,0.394956,12.1362,4.66126,2.40487,1.63444,447,4.33431E-11,3.85119,9.63317,31.1349,180,85,30,9,22,yes
cylinder,Success,128,745,90.823,1.0184,2.7476,5.319,2510,1486,36.1845,0.405737,10.0703,8.73772,8.3691,3.3343,1720,2.84217E-14,0.0156986,1.05713,15.8454,180,876,342,122,289,yes
torus,Success,2304,1011,172.128,1.9721,8.1841,13.0497,4544,2022,37.8803,0.434001,8.43397,6.62385,5.57492,3.15173,3574,0.0698338,0.789263,2.28672,9.91474,179.807,2292,729,67,457,yes
torus thin,Success,2048,1082,140.846,1.5303,4.4269,4.7067,4935,2164,28.5402,0.310091,4.8001,3.77016,2.90644,2.04881,2562,0.00620433,0.565809,2.31314,12.4423,179.989,1721,641,88,455,yes
two spheres (soup),Success,2904,1033,41.6189,1.1888,3.8056,3.0426,3083,2060,13.4995,0.385598,11.1735,9.59476,8.64383,5.94252,2926,0.00720726,0.557309,1.64358,5.93188,179.99,2450,1143,66,1264,yes
```

Surface (the wrap's own boundary, same run): interior dihedral angle at the surface edges and triangle min angle.

```
input,surf_edges,surf_dih_min,surf_edges_lt90,surf_edges_lt45,surf_edges_lt20,surf_edges_gt270,surf_tri_minangle_worst,surf_tri_minangle_p1
sphere n=6,1425,157.727,0,0,0,0,16.0071,17.0886
sphere n=16,2376,168.858,0,0,0,0,26.7716,26.7716
sphere n=40,2652,170.719,0,0,0,0,29.9893,32.2233
box 1x0.5x0.25,1800,91.3465,0,0,0,0,9.28384,9.70033
cylinder,2229,88.013,2,0,0,0,8.57185,15.561
torus,3033,149.957,0,0,0,0,15.0519,15.4645
torus thin,3246,133.436,0,0,0,0,15.0748,16.5908
two spheres (soup),3090,86.4282,1,0,0,2,16.6673,21.8411
```

## Relative alpha 50, relative offset 1500 (`wrap 50 1500`, selected columns)

```
input,status,wrap_ms,tets,boundary_tris,re_worst,re_p5,re_median,re_gt2,mindih_worst,mindih_p5,maxdih_worst,dih_lt10,dih_lt5,dih_lt1,maxdih_gt170,valid
sphere n=6,Success,416.553,7328,5164,17.1102,10.9076,5.77486,7181,2.55795E-13,2.06276,180,5498,2002,106,1696,yes
sphere n=16,Success,282.181,6605,4394,19.4558,14.4552,9.11289,6404,0.00100327,1.63241,179.999,6178,4892,162,4690,yes
sphere n=40,Success,154.764,5323,3608,16.0579,14.573,11.092,5306,0.148011,0.69112,179.747,5036,4186,374,4186,yes
box 1x0.5x0.25,Success,126.359,9051,6354,19.0464,5.25493,2.51753,5680,0.209826,4.91415,179.291,1664,487,24,84,yes
cylinder,Success,77.7188,10448,6506,19.952,10.2005,4.91831,8399,0.000340196,1.34439,180,4681,2387,453,1612,yes
torus,Success,125.203,10321,5458,18.5589,9.1361,4.5373,9357,0.0245915,2.06019,179.957,5585,2066,158,1219,yes
torus thin,Success,81.1958,7779,3714,12.41,4.71516,2.36347,5197,0.0312119,2.76652,179.901,2530,856,100,511,yes
two spheres (soup),Success,85.3176,7271,4756,21.5346,13.5657,7.47241,6992,0.00601521,1.2399,179.984,6483,4096,239,4025,yes
```

## Comparison with fTetWild (black box), modes `ours` / `eval`

`.github/workflows/tet-compare.yml` (on demand) builds fTetWild on the GitHub-hosted runner from its README steps (MPL-2.0:
binary only, no source read, outputs deleted after the evaluation) and runs `compare/run_shard.sh` on the 40 Thingi10K ids of
`compare/thingi-subset-ids.txt` (every 250th id of the dataset, fetched at run time, never committed), in 4 shards by default (inputs `shards`, `ft_threads`), one mesher at
a time per runner: `ours` (alpha wrap 20 / 600 + inside cells, serial, timed in process) and fTetWild with its defaults at 4 threads
(1 thread optional). `eval <tet file> <input mesh>` checks a `.mesh` (MEDIT) or MSH 2.x ASCII file with our own exact code
(`TetFiles.Evaluate`): orientation by `Orient3d` (a file that is negative throughout is taken as the other convention; minority
and zero tets count as inverted, nothing is re-oriented silently), matched facets and a closed manifold boundary
(`TetMeshQuality.NotValidReason`), the boundary free of self-intersections (exact), quality (`TetMeshQuality.Measure`) and the
vertex-sampled surface deviation both ways relative to the input's diagonal. `compare/summarize.py` makes the table; the CSVs and
`SUMMARY.md` go to the data branch `runs/tet-meshing` (`compare/<date>-run<id>/`).

## Stage 1 and 2 (`refine`)

`refine [proc|<mesh>] [relativeAlpha] [relativeOffset] [maxSteiner] [surfaceMinAngle]` runs `MeshTetrahedralization` (stage 1
refinement, peeling, and stage 2 improvement with its defaults) and prints the `eval` row plus status, times and counts.
Subset numbers (one thread, 4-core cloud container): `results/thingi10k-subset-stage1*.csv` and `results/thingi10k-subset-stage2-*.csv`;
summary in `docs/design/TET_MESHING.md` sections 14.8, 14.9 and 15 (stage 2: p01 >= 10 and min >= 1 degree on 40 of 40).

### Edge splits and contractions (TET_MESHING.md section 16, 2026-11-17)

Switches from the environment (benchmark only): `G3_TET_SPLIT=0`, `G3_TET_CONTRACT=0` (stage 2 as before), `G3_TET_PASSES=<n>`
(default 50), `G3_TET_DIAG=1` (one `diag,<mesh>,<angle> <shape> <touch> ...` line per tet left below 10 degrees on stderr, and
`improve_diag_*` counts). Before / after on the 40-mesh subset, one process at a time, `refine <mesh> 20 600`
(`results/thingi10k-subset-stage2-master-2026-11-17.csv` from master d1b86232, `results/thingi10k-subset-stage2-splits-2026-11-17.csv`):

| | master | splits + contractions |
|---|---|---|
| every tet >= 10 degrees | 17 / 40 | 36 / 40 |
| worst smallest dihedral | 3.17 | 6.11 |
| time median / max / sum | 0.69 s / 12.2 s / 41.2 s | 0.81 s / 14.6 s / 54.9 s |
| refinement + improvement per tet, median | 55 us | 78 us |

### Runner sample of stage P1 (TET_MESHING.md sections 17.5 and 17.8, 2026-11-17)

`.github/workflows/tet-sample.yml` (on demand; ask the coordinator first, runner budget) runs `compare/run_sample_shard.sh` on up to 6
runners: `refine <id>.stl <alpha> 600` for every id of `compare/thingi-sample-ids.txt` (500 Thingi10K ids, fetched at run time, never
committed) and every alpha of its input `alphas` (base first), with `G3_TET_WRAP_TIME=0` (skips the separate timing wrap: `wrap_s` is 0
and `refine_s` the whole `Compute`). `compare/summarize_sample.py <dir> 20 40 80` prints the table and the go / no-go of 17.5. The
`counts` of a `refine` row start with `input_tris=<n>`. Rows of run 44122189889: `results/thingi10k-sample500-alpha{20,40,80}-2026-11-17.csv`.

| 500 meshes | / 20 | / 40 (default since P1) | / 80 |
|---|---:|---:|---:|
| every tet >= 10 degrees | 461 | 494 | 499 |
| input to boundary max, median | 0.0343 | 0.0228 | 0.0135 |
| time median / max (s) | 0.64 / 8.6 | 0.83 / 9.8 | 1.41 / 11.7 |
| tets, median | 4.1k | 7.3k | 19.5k |

### Rescue phase (TET_MESHING.md sections 17.11, 2026-11-17 / 18)

`G3_TET_RESCUE=<n>` sets `RescuePasses` (default 150 since owner decision 2026-11-18, 0 = off). Subset, five variants interleaved per mesh, one
process at a time (`results/thingi10k-subset-stage2-ab-{a20_off,a20_on,a20_p200,a40_off,a40_on}-2026-11-17.csv`; `a40` = `refine <mesh> 40 600`):

| | every tet >= 10 degrees | worst | time sum |
|---|---|---|---|
| alpha 20, rescue off / on / off with 200 passes | 36 / 38 / 37 | 6.11 / 7.67 / 6.11 | 47.5 / 48.5 / 53.3 s |
| alpha 40, rescue off / on | 40 / 40 (identical) | 10.00 / 10.00 | 41.6 / 42.5 s |

The 6 meshes of the 500-mesh sample left below 10 degrees at alpha 40 (`results/thingi10k-sample6-rescue-ab-alpha40-2026-11-18.csv`, rows with
`rescue_passes` 0 and 150):

| mesh | worst dihedral off -> on | time off -> on |
|---|---|---|
| 138102 | 7.118 -> 10.005 | 1.71 -> 2.01 s |
| 763398 | 9.148 -> 10.004 | 1.45 -> 1.33 s |
| 83719 | 9.604 -> 10.002 | 2.42 -> 2.82 s |
| 50656 | 9.877 -> 10.003 | 1.41 -> 1.20 s |
| 38781 | 9.432 -> 9.432 | 3.01 -> 3.15 s |
| 282598 | 9.532 -> 9.532 | 2.67 -> 3.26 s |

### Size control (TET_MESHING.md section 17.12, 2026-11-19)

`size [relativeAlpha] [relativeOffset]` (default 12 / 600) runs `MeshTetrahedralization` on sphere n=16, the box, the cylinder and the torus with a uniform
`TargetEdgeLength` h = 1, 0.7, 0.5, 0.35, 0.25 times alpha and with `GradingFactor` 2, 1, 0.5, 0.25, and prints the tet count, the estimate volume / (0.12 h^3) and their
ratio, the time, the smallest dihedral and the counts (`results/tet-size-control-alpha12-2026-11-19.csv`, one thread, local). For h at most alpha / 2 the count is 1.41 to 1.5 times the
estimate (1.8 on the torus at alpha / 2); at h = alpha the wrap's own tets dominate (2.3 to 8.7 times). Time 10 to 40 us per tet (wrap included).

| tets | h = alpha | 0.7 | 0.5 | 0.35 | 0.25 | graded g = 2 | 1 | 0.5 | 0.25 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| sphere n=16 (unsized 3 122) | 3 863 | 7 208 | 16 889 | 48 065 | 130 727 | 3 618 | 4 928 | 6 687 | 8 863 |
| box (unsized 1 527) | 2 706 | 5 829 | 13 923 | 39 462 | 108 580 | 1 951 | 2 598 | 3 253 | 4 068 |
| cylinder (unsized 3 733) | 5 188 | 9 819 | 22 990 | 64 261 | 174 664 | 4 487 | 5 825 | 7 615 | 9 803 |
| torus (unsized 5 014) | 5 091 | 5 455 | 8 469 | 20 645 | 54 695 | 5 104 | 5 525 | 6 336 | 7 594 |


## Early rejection of split attempts in the improver (#1154, `G3_TET_FLOOR`)

`refine <mesh> 40 600` with `G3_TET_FLOOR=<SliverDihedralDegrees>`, `G3_TET_WRAP_TIME=0`, one process at a time, master (base) and the
branch (candidate) built from the same checkout state and run one after the other on the same 4-core cloud container. `time_s` is the whole
`Compute()` (stage 1 and 2, about 2-4 s of it stage 1). The diagnosis (a stopwatch per operation, 40011 at floor 20): 90 % of the improver's
time is split attempts, of which 0.2-6 % succeed; 82 % of the attempts have their neighbourhood's worst tet outside the edge's ring and cannot
succeed (the exact argument is the comment in `TetMeshImprover.try_split`). The candidate skips those attempts. Every other column of the
rows (tets, vertices, quality, deviations) and every count (including `improve_split_refused_input`) are equal between base and candidate.

| floor | mesh | base time_s | candidate time_s | speedup | rest of the row equal |
|---|---|---|---|---|---|
| 20 | Thingi 40011 | 111.4 | 43.7 | 2.55x | yes |
| 20 | Thingi 38781 | 105.5 | 48.9 | 2.16x | yes |
| 30 | Thingi 301798 | 266.3 | 115.4 | 2.31x | yes |
| 10 (default) | Thingi 40011 | 2.1 | 1.8 | 1.13x | yes |
| 10 (default) | Thingi 301798 | 1.8 | 2.0 | 0.92x | yes |
| 10 (default) | Thingi 100033 | 0.5 | 0.5 | 1.01x | yes |
| 10 (default) | Thingi 100282 | 2.3 | 2.3 | 0.99x | yes |
| 10 (default) | Thingi 1004825 | 0.8 | 0.8 | 0.98x | yes |

The default path is unchanged (differences of 10 % at 2 s are timing noise of one run). The runs at floors 20 and 30 stay above 20 s:
what is left is the 19 % of attempts that are tried to their end (16.7 s of 44 s on 40011), the other operations of the passes (about 16 s)
and the skipped attempts' own check (6 s). An edge-keyed memo of failed splits on top (15-41 % of the attempts repeat an edge under an
unchanged two-ring signature) was measured at about 6 % net after the cost of the signature and was not built.

### Local alpha (TET_MESHING.md section 17.13, 2026-11-19)

`compare/run_local_alpha.sh <dll> <mesh dir> <out csv>` runs the 40-mesh subset (meshes fetched at run time, not kept; 120628 skipped as above 40 MB) with uniform alpha 40 / 80 / 160 and with the
local alpha (`refine` with `G3_TET_DEV=<x>`: `MaxSurfaceDeviation` = diagonal / x, `G3_TET_LOCALPASSES=<n>`), and `compare/summarize_local_alpha.py` prints the medians. Rows:
`results/tet-local-alpha-subset39-2026-11-19.csv`. Medians over 39 meshes (ratios per mesh against uniform /80): diagonal / 150 gives 0.52x the tets at 1.00x the input-to-boundary max distance and 1.17x the time;
diagonal / 250 gives 0.68x the tets at 0.92x the distance and 1.31x the time (two passes: 0.81x the tets, 0.92x, 1.69x); uniform /160 is 3.75x the tets at 0.70x the distance and 2.39x the time. All 39 valid, no self-intersection, every tet >= 10 degrees in every configuration.


### MaxDihedralDegrees: the objective on both ends (opt-in, 2026-11-19)

`MeshTetrahedralization.MaxDihedralDegrees` (0 = off, the default; bit for bit the stage as before) adds the "op 4" of Klingner's thesis (ch. 3-5, paper only) to
stage 2: the quality of a tet is min(smallest dihedral, F + G - largest dihedral), F = `SliverDihedralDegrees`, G = the option, so a tet meets the target
exactly when its smallest dihedral is >= F and its largest <= G. The measurement on the throwaway branch `runs/tet-stellar` (objective alone, "p4") lost
meshes on the min side (498 -> 458 of 500 reach min >= F at 10 / 150). Two additions, both inside the option:

- **guard**: no operation may leave a tet with a smallest dihedral below min(F, that of the tets it replaces) (smoothing, boundary moves, contraction, edge
  and face removal, split). It does not repair the loss: the stage-1 result has about 100 tets below F that master fixes, and with the objective a max-side
  tet next to such a tet masks it (the neighbourhood's worst quality is the max-side one), so the repair is not accepted.
- **staged**: the improver first runs on the smallest dihedral alone (master's behaviour), then with the objective and the guard. The guard then keeps what
  the first phase reached.

500-mesh sample (`compare/thingi-sample-ids.txt`, alpha / 40, relative offset 600, floor 10, G = 150; `MaxDihedral.cs`, `compare/run_maxdih_shard.sh`,
`compare/maxdih_summary.py`; stage 1 once per mesh, then each configuration's improver from that result; meshes fetched at run time, not kept; 4-core cloud container,
3 shards in parallel, so the improver times are indicative only). master = option off; p4 = objective alone; p4g = with the guard; p4s = guard and staged (the option as built):

| config | min>=F | max<=G | both | valid | median p01 / p99 | worst min / max | tets vs master | improver us/tet (median) | improver s (sum / max) | violating tets (sum) |
|---|---:|---:|---:|---:|---|---|---:|---:|---|---:|
| master | 498 | 0 | 0 | 500 | 11.4 / 157.8 | 9.43 / 179.0 | 1.000 | 18 | 89 / 1 | 164860 |
| p4 | 454 | 466 | 449 | 500 | 14.0 / 148.2 | 2.58 / 158.8 | 0.981 | 26 | 166 / 10 | 443 |
| p4g | 462 | 467 | 447 | 500 | 14.0 / 148.2 | 3.87 / 168.5 | 0.982 | 19 | 122 / 5 | 322 |
| p4s | 499 | 443 | 443 | 500 | 14.0 / 148.2 | 9.53 / 170.4 | 0.983 | 22 | 161 / 8 | 748 |

The measurement's numbers for p4 were 458 / 472 / 456 (before #1180 and #1184). The guard alone gives back 8 meshes on the min side (44 -> 36 meshes lost
against master) and costs 2 on the max side; p4s loses no mesh on the min side (0 meshes below min(F, master's worst min); 499 against master's 498) and
clears the max side on 443 meshes (the other 57 keep a few tets above G). The improver costs 1.8x master's time (161 s against 89 s over 500 meshes, max
8 s). A 24-mesh subset with `G3_CHECK=1` (boundary self-intersection, input vertices outside): 0 and 0 in every configuration. Rows: data branch `runs/tet-maxdih`.

Cost of a target the shape cannot meet (`Compute()` of the 4.4k-tet sphere of `TetMeshingStage2Tests.Input("sphere")`, alpha 40, offset 600, floor 10, one run each, shared container): G = 130: 2.0 s (8 passes); 120: 12 s (201 passes); 110: 147 s (201 passes); 100, 90, 80: more than 200 s (stopped). Values below 70.6 are off (no tet can meet them), so the run is as long as without the option. Where to cut off between is open (owner decision).
