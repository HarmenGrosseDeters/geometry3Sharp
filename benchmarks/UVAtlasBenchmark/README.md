# UVAtlasBenchmark

`UVAtlasGenerator` (default options, `Method = Auto`) on real meshes from
[Thingi10K](https://huggingface.co/datasets/Thingi10K/Thingi10K): status, time, how many components are
flattened with cones and how many with chart segmentation, charts per method, coverage, seam length,
overlaps and distortion. No mesh files are committed: `fetch_data.py` downloads them (pinned dataset revision) into the git-ignored
`data/` (Thingi10K models carry individual licenses, see `data/input_summary.csv`).

```
python3 fetch_data.py               # ~600 MB; --no-large skips the 20 large meshes
./run_all.sh [auto|charts|cones|single]   # results/<method>.jsonl, results/<method>_large.jsonl; BENCH_P meshes in parallel
python3 analyze.py results/auto.jsonl     # table per category, list of the failures
python3 analyze.py results/auto_large.jsonl
```

The selection is fixed (seeded) in `fetch_data.py`, all meshes <= 100k faces unless noted:

| category | meshes | Thingi10K flags |
|---|---|---|
| clean | 100 | closed, edge and vertex manifold, no degenerate or duplicated faces, PWN, one component |
| open | 50 | manifold, no degenerate or duplicated faces, one component, not closed |
| multi | 50 | manifold, closed, no degenerate or duplicated faces, several components |
| broken | 50 | any of the defects (not closed, not manifold, degenerate or duplicated faces, not PWN) |
| large | 20 | clean, 100k..500k faces |

Each mesh runs in its own process with a timeout (300 s, large meshes 1200 s). Columns of `analyze.py`:
- status: `Success` (all charts within `MaxAreaDistortion` / `MaxConformalDistortion`, no overlaps),
  `DistortionLimitExceeded` (overlap-free, some charts over the limits, eg at `MaxCharts`), `Failed`
- components flattened with cones: `ConeComponents` of `ComponentCount` (components after splitting bowtie
  vertices); triangles in cone charts: share of all triangles
- coverage: fraction of the unit square covered after packing; seam length relative to sqrt(surface area)
- distortion: area weighted means over the whole atlas (`ParameterizationDistortion`)
- time, charts, coverage and seams count every mesh with an atlas (also `Failed`), distortion every mesh with a
  distortion measure; medians are true medians, p90 is nearest rank
- `error` in the JSON lines is only written when the status is not `Success`

Since the evaluation metrics (#157) the JSON lines also carry `q3d` and `quv`: Verdict triangle quality
(`TriangleQualityStatistics`: min / max angle, aspect ratio, scaled Jacobian, condition number) of the mesh and of
its UV triangles. They also carry `atlas_topo`, the topology of the atlas mesh (`MeshTopologySummary`), where every
chart should be a disc (genus 0, one boundary loop). `analyze.py` shows the 1 %-worst min angle in 3D and in UV and
counts meshes with charts that are not discs. The tables below are from before these columns.

## Comparison with xatlas

`compare.py` runs `UVAtlasGenerator` (`atlas` mode: Method = Auto, defaults) and [xatlas](https://github.com/jpcy/xatlas)
(MIT, default options) on the same 250 meshes. Both atlases are evaluated by the same code, the `evaluate` mode
(`benchmarks/Shared/UvEvaluation.cs`): exact overlaps and flipped UV triangles, charts and discs, mean and max conformal
and area distortion, seam length / sqrt(area), coverage of the unit square, the gap between charts on a 1024 px raster
(capped at 8 px; 0 = shared pixel, 1 = touching), the spread of the texel density across charts, and the time.
Definitions: the seam is an input edge along which the atlas is cut (its two faces meet at different atlas vertices, so a
cut inside one chart counts); the gap is the Chebyshev distance in pixels between the nearest pixels of two charts (1 =
touching, 0 = a pixel inside two charts, 9 = no two charts closer than 8 pixels); UVs are stored in float by both tools,
and an atlas with a UV outside the unit square is reported as `uv_out_of_range`.
`analyze.py` gives the table per category and the Pareto view: a mesh where one tool is at least as good on seam length,
mean conformal, mean area distortion and coverage, and better on one of them, is dominated by that tool.

```
python3 compare.py --dll <UVAtlasBenchmark.dll> --xatlas <xatlas_driver> --selection data/selection.csv \
    --meshes data/meshes --work <scratch dir> --out results    # results/ours.jsonl, results/xatlas.jsonl
python3 analyze.py --compare --ours results/ours.jsonl --xatlas results/xatlas.jsonl --selection data/selection.csv
```

xatlas is a black box: `.github/workflows/uvatlas-compare.yml` (workflow_dispatch only) clones it at a pinned commit and
builds `xatlas/xatlas_driver.cpp` on the runner, which calls the public API with default options and writes the atlas in
the layout of `UvEvaluation`. The xatlas source is not in this repository. Its UVs are divided by the atlas width and
height (per axis, as a texture is addressed). The job publishes the analysis in its summary and as an artifact with the
metrics only; the meshes and atlases stay on the runner. The evaluation has tests on procedural meshes
(`tests/geometry3Sharp.Tests/UVAtlasEvaluationTests.cs`).

## Results

Main set: master fcf8da7 plus the exact check of the packed charts (`UVPacker`, commit 5fe85a5, see below); large set: master
2be7cd8 (with the SLIM changes of #122) plus `ComponentCount`, before that check. 4 cores (Intel Xeon 2.1 GHz,
cloud container), .NET 10, Release, `BENCH_P=3`, October 2026.

| | clean | open | multi | broken | all |
|---|---|---|---|---|---|
| meshes | 100 | 50 | 50 | 50 | 250 |
| Success | 82 | 31 | 22 | 24 | 159 |
| DistortionLimitExceeded | 18 | 17 | 28 | 16 | 79 |
| Failed | 0 | 2 | 0 | 10 | 12 |
| InvalidMesh / load_failed | 0 | 0 | 0 | 0 | 0 |
| timeout / crash / exception | 0 | 0 | 0 | 0 | 0 |
| with overlaps or inverted UVs | 0 | 0 | 0 | 0 | 0 |
| time median / p90 / max (s) | 1.98 / 8.78 / 100.1 | 3.33 / 51.55 / 71.6 | 3.03 / 31.03 / 147.8 | 2.16 / 21.29 / 57.3 | 2.60 / 21.29 / 147.8 |
| components flattened with cones | 7 of 100 (7%) | 1 of 50 (2%) | 112 of 388 (29%) | 578 of 2538 (23%) | 698 of 3076 (23%) |
| triangles in cone charts | 3% | 0% | 7% | 10% | 5% |
| charts per mesh median / p90 | 50 / 128 | 98 / 128 | 128 / 128 | 70 / 128 | 73 / 128 |
| charts by method | bff 5636, tutte 232, slim 207, cones 7 | bff 3601, tutte 240, slim 213, cones 1 | bff 4548, tutte 238, slim 188, cones 112 | bff 2675, cones 578, slim 216, tutte 168, point 1 | bff 16460, tutte 878, slim 824, cones 698, point 1 |
| meshes with charts over the limits | 18 | 17 | 28 | 16 | 79 |
| coverage median | 0.48 | 0.39 | 0.51 | 0.43 | 0.46 |
| seam length / sqrt(area) median | 19.6 | 21.6 | 25.9 | 17.9 | 20.3 |
| mean conformal distortion median / p90 | 1.005 / 7.637 | 1.022 / 17.174 | 1.210 / 24.539 | 1.017 / 757.674 | 1.013 / 17.676 |
| mean area distortion median / p90 | 0.004 / 0.209 | 0.009 / 0.834 | 0.062 / 1.336 | 0.020 / 1.585 | 0.009 / 0.792 |
| meshes with float rounding defects | 9 | 6 | 11 | 18 | 44 |

The failures (`analyze.py` lists them):

```
1051179.stl (open): Failed no injective parameterization of a chart (Tutte embedding not injective, eg degenerate surface triangles)
112853.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 258 b
130013.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 2 bou
136393.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 3 bou
283361.stl (broken): Failed no injective parameterization of a chart (Tutte embedding not injective, eg degenerate surface triangles)
44096.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 39 bo
52310.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 142 b
56106.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 56 bo
59323.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 113 b
636815.stl (open): Failed no injective parameterization of a chart (Tutte embedding not injective, eg degenerate surface triangles)
888455.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 18 bo
93704.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 2 bou
```

Large set (20 clean meshes, 100k..500k faces):

| | large |
|---|---|
| meshes | 20 |
| Success | 4 |
| DistortionLimitExceeded | 16 |
| Failed | 0 |
| InvalidMesh / load_failed | 0 |
| timeout / crash / exception | 0 |
| with overlaps or inverted UVs | 0 |
| time median / p90 / max (s) | 232.52 / 663.23 / 686.8 |
| components flattened with cones | 0 of 20 (0%) |
| triangles in cone charts | 0% |
| charts per mesh median / p90 | 128 / 128 |
| charts by method | bff 1913, tutte 314, slim 100 |
| meshes with charts over the limits | 16 |
| coverage median | 0.40 |
| seam length / sqrt(area) median | 41.1 |
| mean conformal distortion median / p90 | 4.593 / 159.877 |
| mean area distortion median / p90 | 0.121 / 1.718 |
| meshes with float rounding defects | 8 |

## Findings

- No timeouts, crashes or exceptions; every atlas is returned.
- **Cones vs. charts.** `Auto` uses cones for 23% of the components but only 5% of the triangles: cones succeed
  on small, simple components (eg the many small parts of the broken meshes), while larger components fall
  back to the chart segmentation. On the clean single-component meshes 7 of 100 are flattened with cones.
- **Charts.** 73 charts per mesh (median); 84 of 250 meshes reach `MaxCharts` (128), 77 of them with charts
  still over the limits. Two meshes have more charts than `MaxCharts` (145 and 253): every component gets at
  least one chart. BFF flattens 87% of the charts, SLIM 4%, the Tutte fallback 5%.
  Coverage of the unit square is about 0.46 (median).
- **Failed, 12 of 250** (17 before the packer check):
  - 9 broken meshes: `MeshDiscCut` cannot cut a chart into a disc (a component with Euler characteristic 1 but
    several boundaries, ie the chart mesh is not a manifold surface after the bowtie split).
  - 3 meshes: no injective map of a chart, the Tutte fallback is not injective either (degenerate surface
    triangles).

- **Large meshes** take 3.9 min (median, up to 11 min); 16 of 20 reach `MaxCharts` and keep charts over the
  limits, and 13% of their charts are Tutte fallbacks (mean conformal distortion 6.3, median). `MaxCharts`
  should grow with the mesh for these.
- Float rounding: 44 meshes have defects only in the float UVs stored in the mesh (`FloatRoundingDefects`),
  reported, not failed.

### Before / after: exact check of the packed charts

Before, 5 meshes (2 clean) failed with overlaps in the *packed* atlas, although every chart was overlap-free
before packing (`Pack = false` gave no overlap). Cause: `UVPacker` rotates a chart to its minimum-area
rectangle, and the rotation is rounded; in charts of flat CAD faces, vertices lie exactly on the line of another
triangle's edge (T-junctions, collinear chains), and rounding moves some of them across it. Scaling and
translating keep the order of each coordinate, which keeps axis-parallel chains, but can still move a vertex
across a nearly collinear slanted edge.

Now the packed charts are checked with exact predicates: a chart with overlapping, inverted or degenerate UV
triangles is packed again without the rotation (`UnrotatedCharts`), and a vertex that rounding moved across a
nearby edge is moved back to its side in the input by a few ulps (`RepairedVertices`).

Before: master 2be7cd8, after: master fcf8da7 with the check; between the two only doc comments and
`ComponentCount` changed in `UVAtlasGenerator`, so the difference is the check.

| main set (250) | before (master 2be7cd8) | after |
|---|---|---|
| Success | 155 | 159 |
| DistortionLimitExceeded | 78 | 79 |
| Failed | 17 | 12 |
| with overlaps or inverted UVs | 5 | 0 |
| time median / p90 / max (s) | 2.33 / 17.76 / 118.6 | 2.60 / 21.29 / 147.8 |

The five meshes: 53749, 96070, 123302 and 1582393 now succeed, 270395 has no overlap but a chart over the
limits. Time per mesh after / before: median 1.04 (p10 0.90, p90 1.35), sum 2083 s against 1959 s. The check costs
one more `UVOverlap` of the packed atlas (two with an unrotated repack), and the run overlapped with test runs
on the same machine, so part of the 6% is load.

## Spectral conformal map against LSCM, per chart (`chart-ab`)

`chart-ab` computes the atlas (Auto, defaults), then flattens each of its charts (discs, as the atlas cut them) with
`LSCMParameterization` (the default) and `SpectralConformalParameterization` (the opt-in, Mullen et al. 2008), and
writes one JSON line per chart; `spectral_ab.py` summarizes. The atlas itself uses LSCM only where BFF fails (once on
these 250 meshes), so the comparison is on all charts, not on the atlas results.

```
ls data/meshes/* | xargs -P3 -I{} dotnet bin/Release/net10.0/UVAtlasBenchmark.dll chart-ab {} > results/chart_ab.jsonl
python3 spectral_ab.py results/chart_ab.jsonl
```

Main set, 250 meshes, 18,861 charts (master 40583f8d plus the spectral option; cloud container, 4 cores, 3 meshes in
parallel, so times are relative; the output of `spectral_ab.py`):

| | LSCM (default) | spectral (opt-in) |
|---|---|---|
| status | Success 18417, FlippedTriangles 439, SolverFailed 4, InvalidMesh 1 | Success 18241, FlippedTriangles 450, SolverFailed (nearly double) 154, SolverFailed 15, InvalidMesh 1 |
| charts with flipped or degenerate UV triangles | 439 | 450 |
| Success but not injective (exact overlap test) | 1234 | 1155 |
| Success where the other is FlippedTriangles | 32 | 13 |
| mean conformal, median / p90 / max (both Success, 18228) | 1 / 1.017 / 11.51 | 1 / 1.017 / 11.43 |
| mean conformal: spectral lower / higher / equal (1e-6); spectral / LSCM p1 / median / p99 / max | | 3799 / 1294 / 13135; 0.9919 / 1 / 1.001 / 1.335 |
| max conformal, median / p90 / max (both Success, 18228) | 1 / 1.296 / 1.002e+04 | 1 / 1.298 / 6937 |
| max conformal: spectral lower / higher / equal (1e-6); spectral / LSCM p1 / median / p99 / max | | 2829 / 2715 / 12684; 0.9023 / 1 / 1.039 / 530.2 |
| mean area distortion, median / p90 / max (both Success, 18228) | 2.63e-14 / 0.03031 / 19.9 | 8.601e-15 / 0.02968 / 20.26 |
| mean area distortion: spectral lower / higher / equal (1e-6); spectral / LSCM p1 / median / p99 / max | | 12064 / 5628 / 536; 0 / 0.8897 / 7.278 / 1.74e+05 |
| time, all both-Success charts (18228): total; per chart spectral / LSCM median, p90 | 11.7 s | 25.9 s (2.21x); 2.46x, 4.61x |
| time, charts with >= 2000 triangles (75): total; per chart spectral / LSCM median, p90 | 1.4 s | 2.7 s (1.90x); 1.86x, 2.80x |
| spectral iterations median / p90 / max (next pair: median) | | 3 / 6 / 198 (4) |
| refused as nearly double: atlas method of the chart, median triangles | | {'bff': 112, 'tutte': 25, 'slim': 17}, 16 |

- Most atlas charts are nearly developable (median conformal distortion 1), where both maps agree.
- No clear winner on distortion: spectral has a lower maximum on 2829 charts and a higher one on 2715. Its worst ratio is
  530x (a 1071-triangle chart of 1620053.stl: 559 against 1.05); LSCM's worst chart (128058.stl, 1.0e4) is 6.9e3
  with spectral.
- Folds: spectral folds 32 charts that LSCM maps without folds, and maps 13 that LSCM folds (450 against 439 in all).
- Refused (169, 0.9 %): 154 as nearly double, mostly tiny charts (median 16 triangles) whose flat pieces meet only
  through zero-area triangles, so they have more than one conformal map; 15 where the shifted matrix is not positive
  definite (more null modes than the translations). There the caller keeps LSCM.
- Time: 2.2x LSCM in all, 1.9x on the charts with at least 2000 triangles (the next-pair estimate costs about 4 more
  solves).
