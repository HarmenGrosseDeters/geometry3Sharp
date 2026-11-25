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



## Sharp creases in the tet boundary (P4(b), `CreaseAngleDegrees`, TET_MESHING.md 17.14; 2026-11-19)

```
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- feature [relativeAlpha] [relativeOffset]      (15 procedural sharp solids; G3_PROBE_RECOVER=1 adds the midpoint-split proxy)
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- featurefiles <dir with .stl>                  (S1-R and S1-F per mesh; Thingi10K ids fetched with benchmarks/LevyClassification/fetch_thingi.py, not committed)
```
Rows: `results/tet-creases-solids-2026-11-19.csv`, `results/tet-creases-thingi24-2026-11-19.csv`. RelativeAlpha 40, RelativeOffset 600 (the defaults), one run per row, cloud container (time is an order of magnitude).
S1-R = the default boundary (the wrap's rounded surface), S1-F = `CreaseAngleDegrees` 30. Crease deviation = distance of 17 points per input edge turning by more than 30 degrees to the boundary, in offsets
(diagonal / 600), maximum / mean, separately for convex and concave (re-entrant) creases. Concave creases are where the wrap bridges at the scale of alpha (15 offsets here).

| solid | tets R / F | time s R / F | convex max/mean R | F | concave max/mean R | F | min dihedral R / F | Steiner |
|---|---|---|---|---|---|---|---|---|
| cube | 10814 / 11021 | 1.14 / 0.72 (JIT) | 0.81/0.53 | 1/0.91 | | | 10.01 / 10.02 | 0 |
| box 3x2x1 | 8264 / 8243 | 0.28 / 0.44 | 0.78/0.43 | 1/0.85 | | | 10.02 / 10.01 | 0 |
| wedge 60 / 30 / 20 / 12 deg | 7109 / 5877 / 6002 / 4610 vs 7187 / 5863 / 5965 / 4692 | 0.19 / 0.11 / 0.09 / 0.12 vs 0.26 / 0.22 / 0.14 / 0.20 | 0.71 to 0.79 / 0.38 to 0.46 | 1/0.79 to 0.87 | | | 10.0 to 10.09 / 10.0 to 10.06 | 3 / 10 / 0 / 14 |
| box rotated, offset, x2^-10, x2^10 | 4485, 8164, 8264, 8264 vs 4713, 8119, 8243, 8243 | 0.07 to 0.25 vs 0.15 to 0.43 | 0.72 to 0.78 / 0.41 to 0.43 | 1/0.85 to 0.88 | | | 10.02 to 10.06 / 10.01 to 10.03 | 1, 0, 0, 0 |
| L-shape | 7866 / 8367 | 0.23 / 0.42 | 1.0/0.47 | 1/0.95 | 11.6/6.7 | **6.1/1.6** | 10.0 / 10.04 | 3 |
| octant | 11106 / 11712 | 0.30 / 0.50 | 1.0/0.53 | 1/0.95 | 14.3/6.3 | **10.8/2.2** | 10.0 / 10.02 | 0 |
| slot | 9380 / 10675 | 0.26 / 0.58 | 1.0/0.46 | 1/0.94 | 13.7/7.5 | **5.6/1.5** | 10.02 / 10.0 | 22 |
| V-notch | 9481 / 9538 | 0.25 / 0.47 | 1.0/0.49 | 1/0.90 | 31.1/22.4 | **8.4/4.0** | 10.0 / 10.02 | 20 |
| chamfer | 7935 / 8286 | 0.29 / 0.35 | 0.92/0.51 | 1/0.96 | | | 10.01 / 10.01 | 1 |

- **Recovered on 15 of 15 solids** (no fallback), at most 22 Steiner points; the featured surface has 33 to 152 missing triangles / 17 to 80 missing edges before the recovery (1.3 to 3.7 % of its triangles), the surface edge flips remove 95 to 100 % of them.
  All tet meshes are exactly valid (`TetMeshQuality.NotValidReason`), `MinDihedralMet` is true on all.
- **Concave creases** (the gain): mean 6.3 to 22.4 offsets with the rounded wrap, 1.5 to 4.0 with S1-F (2.9 to 5.6 times closer: L-shape 4.2, octant 2.9, slot 5.0, V-notch 5.6; the average of the four is 2.3; the octant's maximum only 14.3 -> 10.8). **Convex creases** are within one offset either way (the rounded wrap at 0.4 to 0.5 offsets mean,
  the sharp (mitred) surface at 0.8 to 0.96: no gain by this distance, the surface is sharp but no closer). The tets stay at 0.99 to 1.14 times S1-R (the slot, 1.14 times, the most); the time is 1.2 to 2.2 times (the feature pass and the recovery; the cube's 0.63 is the JIT of the first row). Only 4 of the 15 solids have concave creases; the others show recovery on convex creases (0 to 14 Steiner points).
- **Boundary slide** (`BoundarySlideFactor` 0.5, TetMeshImprover untouched): it takes back nothing of the gain. The same runs with `BoundarySlideFactor` 0 give the same crease deviation to three digits (the 15 solids), but the slide matters for the dihedral: on the 12 degree wedge without it the smallest dihedral is 7.6 degrees (with it 10.06).
- **Creases below 20 degrees and `MinDihedralMet`**: 0 of 5 end with it false (wedge 12 and Thingi 44041 with an 11 degree, 55429 with a 12 degree and 94240 with a degenerate crease; the V-notch's 10.8 degree bottom edge is a sixth: also met); a tet at a crease cannot have a
  dihedral above the crease's angle, so this holds by the slide and the improver, not by a bound; `Targets` still reports a miss, never silently.

24 Thingi10K meshes of the 2026-11-17 subset (the fastest 24 with status Success; ids in the CSV): **recovered on 19 of 24** (the others fall back to S1-R: 132560 and 91589 at the floor, 81132 over the Steiner budget, 208721 and 68659 because a flipped or split surface triangle touches the input, caught by the exact check), `NotValidReason` null on all, `MinDihedralMet` true on all.
Over the 19 recovered: tets 1.05 times S1-R at the median (0.94 to 1.25), time 4.2 times at the median (max 15; 0.1 to 3.3 s per mesh; the exact input-touch check of the changed triangles is up to 1.9 s of that), mean concave crease distance 0.92 times at the median over the 17 with concave creases (best 0.30: 49884 8.9 -> 2.6, 113956 8.0 -> 2.6, 70379 6.6 -> 3.1; 9 of 17 within 10 % of S1-R or worse).
So the gain on real meshes is much smaller than on CAD solids: most of their concave edges are not the alpha-scale bridges that the pass moves (they are fine detail below alpha or noise), at a clear time cost.

**Worst absolute time with `CreaseAngleDegrees` on** (this container, one run): 0.58 s on the solids (the slot, 10.7k tets), 3.0 s on the Thingi meshes (94240: 4.4k boundary triangles, S1-R 0.25 s; the exact input-touch check of the changed triangles is up to 1.9 s of it). No input is near the 20 s limit of CLAUDE.md.

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

Cost of a target the shape cannot meet (`Compute()` of the 4.4k-tet sphere of `TetMeshingStage2Tests.Input("sphere")`, alpha 40, offset 600, floor 10, one run each, shared container). Without a stall stop: G = 130: 2.0 s; 120: 12 s; 110: 147 s; 100, 90, 80: more than 200 s (stopped). With the stall stop (`maxdih_stall_stop`: 4 passes that neither lower the number of bad tets nor raise the worst quality by 0.1 degrees, while more than 1 % of the tets are bad): G = 150: 1.4 s (3 passes, no stop); 130: 1.5 s; 110: 10 s; 100: 9 s; 90: 13 s; 80: 13 s, ending at a largest dihedral of 125-128 degrees and a smallest of 10.0-10.6. Values below 70.6 are off (no tet can meet them). At G = 150 the stop is never taken on the 500-mesh sample (rows equal, see above).

## Work cap (`MaxImproveVisitsPerVertex`, TET_MESHING.md 17.16, #1154; 2026-11-19)

Opt-in bound on the visits of a bad tet in the improver (default 0 = off, bit for bit). Measured on the 500-mesh sample (`refine <mesh> 40 600`, `G3_TET_FLOOR`,
`G3_TET_VISITS=<n>`, 6 GitHub shards, 300 s timeout, per-pass traces); every round has its own baseline in the same run, so times compare within a table only
(timing on shared runners moves by a few meshes near the 20 s line between runs: the baseline had 3 to 4 meshes above 20 s at the floor 20 and 107 to 118 at the floor 30).
Data and analyzer: branch `runs/tet-work-cap` (`work-cap/`, `benchmarks/TetMeshBenchmark/compare/analyze_work_cap.py`).

Budget n = visits per vertex of the stage-1 mesh. Columns: meshes where the cap was reached, meshes with improver time above 20 s (timeouts), sum of the improver time and the
share saved, worst angle lost against the baseline (max over meshes / meshes losing more than 0.5 degrees), tets below the floor (total, baseline in the first row of the floor; the largest increase on one mesh).

| floor | n | reached | > 20 s (timeouts) | time sum | saved | angle lost max / > 0.5 | tets below floor (max added on a mesh) |
|---|---|---|---|---|---|---|---|
| 20 | 0 | - | 3 (0) | 447 s | - | - | 4912 |
| 20 | 8 | 8 | 0 (0) | 364 s | 18.5 % | 1.94 / 1 | 6847 (+1148) |
| 20 | 7 | 13 | 0 (0) | 354 s | 20.8 % | 1.94 / 2 | 7310 (+1338) |
| 20 | 6 | 14 | 0 (0) | 342 s | 23.4 % | 1.94 / 3 | 7821 (+1571) |
| 20 | 0 | - | 3 (0) | 549 s | - | - | 4912 |
| 20 | 10 | 7 | 1 (0) | 458 s | 16.6 % | 1.94 / 1 | 6456 (+993) |
| 20 | 5 | 17 | 0 (0) | 387 s | 29.5 % | 1.94 / 3 | 8822 (+1956) |
| 20 | 2 | 36 | 0 (0) | 299 s | 45.6 % | 5.62 / 14 | 17184 (+5189) |
| 30 | 0 | - | 118 (7) | 8516 s | - | - | 113131 |
| 30 | 10 | 138 | 59 (0) | 5331 s | 41.6 % | 7.04 / 36 | 202356 (+997) |
| 30 | 5 | 238 | 12 (0) | 3686 s | 58.7 % | 8.83 / 82 | 231911 (+2278) |
| 30 | 2 | 427 | 0 (0) | 1655 s | 81.2 % | 10.4 / 293 | 527823 (+6452) |

(The floor-20 rows are two runs: n = 6, 7, 8 with their baseline, then n = 10, 5, 2 with theirs.) The visits are the right unit because the improver's time follows them:
0.2 to 0.4 ms per visit in the baseline traces (mesh 46017 at the floor 20: 223k visits, 91 s). **Recommendation for the floor 20: n = 8** (0 meshes above 20 s in the
run, 1 mesh loses more than 0.5 degrees, at most 1.94 degrees; n = 10 left one mesh at the 20 s line in another run). **At the floor 30 it is a time/quality trade, not a fix**: the
floor is a target the improver does not reach on most meshes (median 43 tets below it in the baseline), and every level that removes the long tail costs worst angle on
tens to hundreds of meshes.

Round 2 (a cap on the split attempts only, `MaxSplitAttemptsPerVertex`, not kept): 8 / 24 / 64 attempts per vertex left 2 / 3 / 4 meshes above 20 s at the floor 20
(baseline 4) and 73 / 84 / 106 at the floor 30 (baseline 107), saving 13 / 11 / 4 % and 31 / 23 / 14 % of the time: the slow meshes spend 50 to 60 % of their time in the
operations a split cap does not bound.

## Tie order of the improver (`tieorder`, docs/research/TET_TIE_ORDER_2026-11.md; 2026-11-22)

`tieorder <mesh|proc> <alpha> <offset> <floor> [orders] [trace.csv]` runs stage 1 once and then the improver for order 0 (the stage-1 mesh as it is, bit for bit the
library's result) and for `orders` deterministic relabelings of it (vertex ids, tet order, corner order within a tet, boundary triangles; `G3_TIE_PARTS` = a subset
of `vtcb` limits it to parts). The geometry is the same; only the choices the improver makes by index or list position change. Effort is in visits. `tieorder check`
is the harness's self-check on procedural inputs. Inputs and summary: `compare/run_tie_order.sh`, `compare/summarize_tie_order.py`; rows `results/tie-order-*-2026-11-22.csv`.

| input (`40 600`) | today | 8 relabelings |
|---|---|---|
| floor 20: 38781, 40011, 46017, 85539 (the meshes above 20 s) | 51678 to 216175 visits, 163 to 721 tets below 20 | fastest / median visits 0.64 to 0.90 (every measure); none reaches 20 degrees |
| floor 10: 38781 | 9.432 degrees | 8 of 8 reach 10 degrees (64 of 64) |
| floor 10: 282598 | 9.532 degrees | 4 of 8 reach 10 degrees (38 of 64) |

Tet order alone or corner order alone lifts the two (10 to 15 of 16 runs); vertex ids and boundary order change nothing on them. On the 40-mesh subset 159 of 160 relabelled
runs reach 10 degrees (today 40 of 40); on five harder meshes 74 of 80. Conclusion and the proposal for the owner: the research note. No library change, no default change.

## Dihedral-floor spike: a lattice mesher next to `MeshTetrahedralization` (`floor`, `floornoise`; 2026-11-22)

Research spike for HP 5 / P5(b) ([docs/research/TET_DIHEDRAL_FLOOR_SPIKE_2026-11.md](../../docs/research/TET_DIHEDRAL_FLOOR_SPIKE_2026-11.md), which has
the tables and the reading). `Stuffing.cs` is a prototype of uniform isosurface stuffing (Labelle and Shewchuk 2007, from the paper's text), not
library code. `floor [relativeCell ...]` runs `MeshTetrahedralization` (default, and with `CreaseAngleDegrees` 30) and the lattice (cell = diagonal /
relativeCell) on the 8 procedural inputs and 13 inputs with acute creases, thin parts and near contacts; the columns are `TetFiles.Header` plus the
largest dihedral angle and the input's smallest dihedral angle. `floornoise [seeds]` checks the angle bound on random labels and cut points.

```
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- floor 20 40 80
G3_STUFF_OURS=0 G3_STUFF_ALPHA=0.24999,0.41189 dotnet run -c Release --project benchmarks/TetMeshBenchmark -- floor 20 40 80
G3_STUFF_OURS=0 G3_STUFF_OFFSET=0 dotnet run -c Release --project benchmarks/TetMeshBenchmark -- floor 40 80
G3_STUFF_FILE=<mesh> dotnet run -c Release --project benchmarks/TetMeshBenchmark -- floor 40 80 160
python3 benchmarks/TetMeshBenchmark/compare/summarize_floor_spike.py benchmarks/TetMeshBenchmark/results/tet-floor-spike-*2026-11-22.csv
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- floornoise 20
```

Rows (2026-11-22, the 4-core cloud container, Release, net10.0): `results/tet-floor-spike-2026-11-22.csv` (the 10.78 degree parameters),
`-safe-` (0.24999 / 0.41189), `-offset0-` (the input surface instead of its offset), `-thingi2-` (38781 and 282598, fetched by hand, not kept).
Summary (`summarize_floor_spike.py`, first file):

```
ours      crease=0   inputs 21 meshed 21 valid 21 | min dihedral worst 10.00 median 10.02, all>=10: 21 | max dihedral worst 168.4 | tets median 6028 | time median 0.22 s max 1.05 | dev_in max: median 0.0017 worst 0.1847, mean: median 0.00057
ours      crease=30  inputs 21 meshed 21 valid 21 | min dihedral worst 10.00 median 10.02, all>=10: 21 | max dihedral worst 169.0 | tets median 6028 | time median 0.25 s max 0.61 | dev_in max: median 0.0017 worst 0.1847, mean: median 0.00111
stuffing  cell=20    inputs 21 meshed 20 valid 19 | min dihedral worst 15.23 median 21.02, all>=10: 20 | max dihedral worst 152.5 | tets median 9787 | time median 0.19 s max 0.57 | dev_in max: median 0.0240 worst 0.3747, mean: median 0.01169 | per input against ours (median of ratios): tets 1.23, dev_in max 10.65, dev_in mean 14.97, time 0.72 | inverted background tets 171 on 2 inputs (with output: 0), rule conflicts 0, quadruple-zero discarded 593
   not meshed or not valid: torus thin - a boundary edge occurs twice with the same direction
   not meshed or not valid: plate 1x1x0.02 - empty
stuffing  cell=40    inputs 21 meshed 20 valid 20 | min dihedral worst 15.91 median 20.41, all>=10: 20 | max dihedral worst 157.2 | tets median 75248 | time median 0.88 s max 2.39 | dev_in max: median 0.0136 worst 0.2002, mean: median 0.00532 | per input against ours (median of ratios): tets 9.47, dev_in max 6.53, dev_in mean 9.12, time 3.50 | inverted background tets 3162 on 7 inputs (with output: 0), rule conflicts 0, quadruple-zero discarded 4684
   not meshed or not valid: plate 1x1x0.02 - empty
stuffing  cell=80    inputs 21 meshed 21 valid 21 | min dihedral worst 16.16 median 20.79, all>=10: 21 | max dihedral worst 156.8 | tets median 563180 | time median 3.42 s max 10.05 | dev_in max: median 0.0050 worst 0.1833, mean: median 0.00175 | per input against ours (median of ratios): tets 69.48, dev_in max 1.80, dev_in mean 2.67, time 13.63 | inverted background tets 12054 on 9 inputs (with output: 0), rule conflicts 0, quadruple-zero discarded 24928
```

`floornoise 20`:

```
alpha_long,alpha_short,seeds,tets,min_dihedral,max_dihedral,degenerate_tets,inverted_background,inverted_background_with_output,rule_conflicts,quadzero_discarded
0.28511,0.39882,20,773644,14.4636,159.3380,0,0,0,0,298902
0.24999,0.41189,20,776780,12.5600,151.6538,0,0,0,0,295760
```

`inverted_background` counts the warped lattice tets whose exact orientation changed (the discarded ones included; with the 10.78 degree parameters
they occur mostly on planar faces, with the safe ones never), `inverted_background_with_output` those that produce output tets. `wrap 40 600 hard` prints the
wrap's own surface angles on the 13 hard inputs (smallest interior dihedral at a surface edge 29.5 to 90 degrees).

## Radius-edge at the fixed boundary and `BoundaryFollowsSize` (`surfprobe`, docs/research/TET_RADIUS_EDGE_AT_SURFACE_2026-11.md; 2026-11-24)

`surfprobe <relAlpha> <surfAngle> <improve> <slide> all <relH> <follow>`: four procedural shapes (sphere, torus, box 2x2x1 with a cylindrical
hole, twisted bar), `TargetEdgeLength` = diagonal / relH, one thread, defaults otherwise (alpha = diagonal / 40, Improve on); "bad" = tets
with radius-edge > 2 in the result. 4-core cloud container, one process at a time; the times are single runs (noise about 10-20 %).

| shape | h | off: tets / bad (share) / s | `BoundaryFollowsSize`: tets / bad (share) / s | bad removed |
|---|---|---|---|---|
| sphere | diag / 40 | 76040 / 419 (0.55 %) / 1.56 | 78843 / 2 (0.003 %) / 2.84 | 99.5 % |
| torus | diag / 40 | 32484 / 237 (0.73 %) / 0.73 | 34179 / 37 (0.11 %) / 1.09 | 84 % |
| box with hole | diag / 40 | 93539 / 518 (0.55 %) / 1.82 | 97263 / 37 (0.04 %) / 2.23 | 93 % |
| twisted bar | diag / 40 | 18892 / 184 (0.97 %) / 0.42 | 21510 / 46 (0.21 %) / 0.80 | 75 % |
| sphere | diag / 60 | 254782 / 3729 (1.46 %) / 4.01 | 262238 / 9 (0.003 %) / 5.17 | 99.8 % |
| torus | diag / 60 | 105570 / 2182 (2.07 %) / 1.80 | 111475 / 33 (0.03 %) / 2.83 | 98.5 % |
| box with hole | diag / 60 | 305904 / 5675 (1.86 %) / 4.91 | 315775 / 28 (0.01 %) / 5.61 | 99.5 % |
| twisted bar | diag / 60 | 60721 / 1607 (2.65 %) / 0.86 | 66554 / 43 (0.06 %) / 1.85 | 97 % |

Totals: diag / 40 1358 -> 122 (91 % removed), diag / 60 13193 -> 113 (99 %); time 1.1-2.2x, tets 1.03-1.14x; the smallest dihedral angle stays
>= 10 degrees on all. The rest (about 30-60 per shape) is the level without size control (24-65 at the defaults): tets at curved and sharp
parts of the fixed surface, which only boundary splits would remove (option C of the note).

## Stall stop and the scan cache (#1154, 2026-11-26)

`tieorder <mesh> 40 600 <floor> 0` (order 0 = the library's result) with `G3_TIE_STALL=<N>` (the opt-in `StallStopPasses`, 0 = off) and `G3_TIE_REFERENCE=1` (the loop without the scan cache,
`TetMeshImprover.ReferenceHillClimb`). Meshes fetched at run time from `https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/<id>.stl` and not kept. Cloud container, 4 cores,
net10.0, Release, one run at a time (timings vary by about 5 %; the digests of "reference loop" and "scan cache" are equal):

| mesh (floor) | setting | improver s | passes (main + rescue) | tets below the floor | worst dihedral |
|---|---|---|---|---|---|
| 40011 (20) | off, reference loop | 44.2 | 200 + 150 | 504 | 16.019 |
| | off, scan cache (the default) | 43.8 | 200 + 150 | 504 | 16.019 |
| | `StallStopPasses` 10 | 28.4 | 80 + 40 | 511 | 16.019 |
| | `StallStopPasses` 5 | 24.5 | 58 + 23 | 539 | 16.019 |
| 38781 (20) | off, reference loop | 48.1 | 200 + 150 | 628 | 12.859 |
| | off, scan cache (the default) | 47.2 | 200 + 150 | 628 | 12.859 |
| | `StallStopPasses` 10 | 21.0 | 67 + 32 | 648 | 12.859 |
| | `StallStopPasses` 5 | 17.8 | 55 + 31 | 649 | 12.859 |
| 51809 (30) | off, reference loop | 58.4 | 200 + 150 | 502 | 22.046 |
| | off, scan cache (the default) | 56.5 | 200 + 150 | 502 | 22.046 |
| | `StallStopPasses` 10 | 30.1 | 63 + 29 | 684 | 22.046 |
| | `StallStopPasses` 5 | 21.5 | 41 + 19 | 591 | 22.046 |

The default stays above 20 s on all three (diagnosis in docs/design/TET_MESHING.md 17.17: no stall, real work with diminishing returns). With N = 10 the runs take 44 to 64 % of the time and
leave 1 to 36 % more tets below the floor at the same worst angle (N = 5: 37 to 55 % of the time, 3 to 18 % more). The procedural box (floor 30, alpha / 40): 4.6 s to 3.9 s from the scan cache alone.
Open lever (not built): the 82k real split attempts at 0.24 ms each are 47 % of the 40011 run; a cheaper rejection before the exact checks of a split would lower the cost per attempt.
