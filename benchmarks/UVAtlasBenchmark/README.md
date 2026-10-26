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

## Results

master 2be7cd8 (with the SLIM changes of #122) plus `ComponentCount`, 4 cores (Intel Xeon 2.1 GHz, cloud
container), .NET 10, Release, `BENCH_P=3`, October 2026.

| | clean | open | multi | broken | all |
|---|---|---|---|---|---|
| meshes | 100 | 50 | 50 | 50 | 250 |
| Success | 80 | 31 | 21 | 23 | 155 |
| DistortionLimitExceeded | 18 | 17 | 28 | 15 | 78 |
| Failed | 2 | 2 | 1 | 12 | 17 |
| InvalidMesh / load_failed | 0 | 0 | 0 | 0 | 0 |
| timeout / crash / exception | 0 | 0 | 0 | 0 | 0 |
| with overlaps or inverted UVs | 2 | 0 | 1 | 2 | 5 |
| time median / p90 / max (s) | 1.72 / 8.85 / 97.9 | 2.79 / 50.34 / 72.7 | 2.80 / 29.12 / 118.6 | 2.15 / 17.76 / 51.6 | 2.33 / 17.76 / 118.6 |
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
123302.stl (broken): Failed 6 overlapping UV triangle pairs, 0 inverted UV triangles, 0 UV triangles of zero area on non-degenerate surface triangle
130013.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 2 bou
136393.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 3 bou
1582393.stl (clean): Failed 1 overlapping UV triangle pairs, 0 inverted UV triangles, 0 UV triangles of zero area on non-degenerate surface triangle
270395.stl (broken): Failed 3 overlapping UV triangle pairs, 1 inverted UV triangles, 0 UV triangles of zero area on non-degenerate surface triangle
283361.stl (broken): Failed no injective parameterization of a chart (Tutte embedding not injective, eg degenerate surface triangles)
44096.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 39 bo
52310.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 142 b
53749.stl (clean): Failed 4 overlapping UV triangle pairs, 0 inverted UV triangles, 0 UV triangles of zero area on non-degenerate surface triangle
56106.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 56 bo
59323.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 113 b
636815.stl (open): Failed no injective parameterization of a chart (Tutte embedding not injective, eg degenerate surface triangles)
888455.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 18 bo
93704.stl (broken): Failed cutting a chart into a disc failed: the cut mesh is not a set of discs: a component has Euler characteristic 1 and 2 bou
96070.stl (multi): Failed 10 overlapping UV triangle pairs, 0 inverted UV triangles, 0 UV triangles of zero area on non-degenerate surface triangl
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
- **Failed, 17 of 250:**
  - 9 broken meshes: `MeshDiscCut` cannot cut a chart into a disc (a component with Euler characteristic 1 but
    several boundaries, ie the chart mesh is not a manifold surface after the bowtie split).
  - 3 meshes: no injective map of a chart, the Tutte fallback is not injective either (degenerate surface
    triangles).
  - 5 meshes (2 clean): overlaps in the *packed* atlas. In all five, every chart is overlap-free before packing
    (`Pack = false` gives no overlap): the similarity transform of `UVPacker` (rotation to the minimum-area
    rectangle, scale, translation) is rounded, and triangles that nearly touch inside a chart then overlap.
    These are not boundary overlaps of SLIM, so a scaffold (Jiang et al. 2017) would not remove them; the
    packing needs an exact check per chart (eg fall back to an unrotated placement, or move the chart's UVs
    to a grid that keeps the orientations).
- **Large meshes** take 3.9 min (median, up to 11 min); 16 of 20 reach `MaxCharts` and keep charts over the
  limits, and 13% of their charts are Tutte fallbacks (mean conformal distortion 6.3, median). `MaxCharts`
  should grow with the mesh for these.
- Float rounding: 44 meshes have defects only in the float UVs stored in the mesh (`FloatRoundingDefects`),
  reported, not failed.
