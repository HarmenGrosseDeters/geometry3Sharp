# Alpha wrap defaults on the 800-mesh sample (#736, #846)

Measurement for the two owner default decisions: the sharp-feature pass (`AlphaWrapSharpFeatures`, lever 6 K, #736) and the
raw-size budget (`MaxEstimatedTriangles`, #846). Both are opt-in today. **No default is changed here**; this page gives the
data and the options, the decision is the owner's.

## Setup

- Run: GitHub Actions `alpha-defaults.yml` (#998), run 43972566855, library master `4ec52b2c`, 16 shards of 50 meshes on
  GitHub-hosted `ubuntu-latest` runners (4 cores, .NET 11.0.100 as installed on the runner image), at most 6 shards at a time.
  Raw rows, `wall.tsv` and the generated summary: branch `runs/alpha-defaults-800`, directory `run-43972566855`.
- Meshes: the 800 ids of `sample800_2611` (Thingi10K, at most 100k faces; `cases/alpha_defaults_list800.txt`), fetched at run time.
- Pipeline: `BENCH_REPAIR=wrappipe`, alpha = diagonal / 300, offset = diagonal / 3000, full fidelity, warm-up soup, timeout 400 s per
  run, one process at a time. The three configurations run interleaved per mesh on the same runner:
  A library defaults; B A + `BENCH_WRAP_FEATURES=1`; C A + `BENCH_WRAP_BUDGET=300000`.
- All 2400 runs returned a row: no timeout, no crash, wrap status Success everywhere.

## Guarantees: unchanged in all three

800 / 800 in each of A, B, C: closed, manifold (no bowties, no boundary edges), no self-intersection (exact), valid, no degenerate or
duplicate triangles, no inverted components. Neither lever loses a guarantee on any mesh.

## Size, quality and time

Medians over 800 meshes (p95 in brackets); Hausdorff is the upper bound `hd_hi`, relative to the diagonal.

| | A (defaults) | B (features) | C (budget 300k) |
|---|---:|---:|---:|
| result triangles | 33,470 (p95 101.9k, max 248.9k) | 22,590 (82.2k, 248.0k) | 29,690 (77.0k, 209.0k) |
| raw wrap triangles | 129.4k (309.6k, max 1.205M) | 130.8k (314k, 1.216M) | 126.3k (144.1k, 559.4k) |
| Hausdorff upper bound | 0.00506 (0.0331, 0.164) | 0.00415 (0.0329, 0.164) | 0.00547 (0.0356, **0.207**) |
| min angle, median | 9.70 deg | 10.01 deg | 9.64 deg |
| p01 angle, median / worst mesh | 11.33 / 10.61 deg | 10.97 / **8.92** deg | 11.39 / 10.61 deg |
| IoU, median | 0.9805 | 0.9809 | 0.9792 |
| pipeline time `pipe_ms`, median (p95, max) | 4.6 s (10.6, 47.8) | 5.8 s (16.3, 89.1) | 4.3 s (6.4, 24.1) |
| pipeline time, sum | 4412 s | 5852 s (**1.33x**) | 3445 s (**0.78x**) |
| meshes with `pipe_ms` or `repair_ms` above 20 s | 15 | 31 (1 above 60 s) | 3 |
| wrap time `wrap_ms`, sum | 2130 s | 2128 s | 1594 s |

### B (features) against A, per mesh

- Output differs on 794 of 800 meshes (the 6 identical ones have no feature found, `features` 0/0/0/0/0).
- Triangles: median 0.67x (p5 0.29x, p95 0.99x).
- Hausdorff bound: better on 623, worse on 162, identical 15; median ratio 0.92, worst 2.13x (135220: 0.0029 -> 0.0061). 22 meshes are more
  than 1.1x worse, 369 more than 10 % better.
- Angles: min angle better on 430, worse on 321 (median ratio 1.001); the p01 angle is lower on 750 meshes (median ratio 0.971,
  7 meshes below 0.9x); worst p01 over the sample 8.92 deg against 10.61 deg. Meshes with a min angle below 5 deg: 60 (A 63).
- Time: pipeline median 1.26x (p95 1.88x, max 2.81x); the tail grows: slowest 40118 47.7 -> 89.1 s (raw 1.2M triangles).

### C (budget 300k) against A, per mesh

- The budget is active (coarsening above 1) on 352 meshes (median factor 1.17, max 3.00); on the other 448 the output is bit-identical
  to A (same hash), which doubles as a timing noise sample (below).
- On the 352 active meshes: coarsening up to 1.5 (311 meshes): triangles 0.84x, pipeline 0.79x, Hausdorff 1.075x (medians);
  1.5 to 2 (31): 0.53x, 0.43x, 1.35x; 2 to 3 (10): 0.41x, 0.24x, 1.73x.
- Hausdorff bound over all 800: worse on 307, better on 45, identical 448; 162 meshes (all of them active) are more than 1.1x worse,
  10 more than 2x, 7 more than 2.5x. Worst: **40118** 0.0044 -> 0.207 (47x; raw 1.2M -> 22.7k triangles, result 108k -> 6.1k, coarsening
  3.00, estimate 2.7M), then 37275 (9.5x, 0.0062 -> 0.059), 51909 (4.6x), 860049 (4.2x), 69261 (4.1x). Meshes with a bound above 0.05: 16 (A), 21 (C).
- Time: pipeline sum 0.78x; tail much shorter (p95 6.4 s against 10.6 s, max 24.1 s against 47.8 s; meshes above 20 s: 15 -> 3).
- The budget works on a size estimate only (3 x area / alpha^2, estimate 2.7M for 40118 against the 300k budget); it does not see what
  the coarser alpha costs in error. Why the bound of 40118 grows 47x was not diagnosed here.

## Caveats

- **Runner noise.** Each mesh runs A, B and C back to back on one runner, so the comparison between configurations is fairer than a
  comparison of absolute times across shards; absolute times are those of a shared 4-core GitHub runner and are not comparable with the
  earlier container measurements. Noise floor from the 448 pairs where A and C do the identical computation: `pipe_ms` C/A median
  1.004, p5 0.905, p95 1.12, max 1.23 (wall clock C/A p5 0.95, p95 1.05); for pipelines above 10 s (2 pairs) 0.99 to 1.02. Ratios of the size
  seen for B (1.26x median) are well outside it; the per-mesh timing of a single short pipeline is not.
- **Wall outliers are not the pipeline.** 55928 (wall 227 s while `pipe_ms` is 6 s), 498456 (160 s, 7 s), 318843 (75 s), 1514905 (73 s)
  show the same long wall time in all three configurations, so it is outside the wrap and simplification (input analysis, warm-up or
  fidelity phase); not diagnosed here. Under the 20 s rule of CLAUDE.md these need their own look (issue with a diagnosis).
- Hausdorff bounds are upper bounds of the benchmark's certified estimate (`hd_*_hi_rel`), not exact distances.
- One sample, one alpha (diagonal / 300); no statement about other alphas, other corpora or the robust mode (where `BENCH_WRAP_BUDGET` was
  already read).

## Options for the owner (no recommendation is implied by the order)

**#736, sharp-feature pass (B).**
1. Stay opt-in (status quo): no risk, the better Hausdorff bound and 0.67x triangles stay available for callers who ask.
2. On by default: the gain is size (0.67x) and a lower Hausdorff bound on 623 of 800 meshes; the price is +33 % pipeline time (sum), a
   longer tail (meshes above 20 s: 15 -> 31, slowest 89 s), a lower p01 angle on 750 meshes (median -3 %, worst 8.9 deg) and a worse
   Hausdorff bound on 162 meshes (worst 2.1x). No guarantee is lost.
3. On by default only below a size or time bound (for example skip it when the raw wrap is above some size), if the tail is the
   concern; not measured here, it needs its own run.

**#846, raw-size budget (C).**
1. Stay opt-in / off (status quo): bit-identical to today.
2. On at 300,000: pipeline time 0.78x and the tail cut to 24 s; the price is a coarser result on 352 meshes (Hausdorff bound up to 1.35x at
   coarsening 1.5 to 2, one outlier at 47x, 21 instead of 16 meshes above a bound of 0.05).
3. On with a larger budget or a cap on the coarsening factor (for example at most 1.5, which would leave 311 of the 352 active meshes
   untouched in kind and drop the 41 worst); not measured, it needs its own run on the 41 meshes above 1.5 and the 10 above 2.
4. Look at the 40118 case first (diagnosis of the 47x bound) and decide afterwards.

Both levers can be combined; the run measured them separately (B and C against A), so the combination is not measured.

## Reproduce

`gh workflow run alpha-defaults.yml` (defaults: 16 shards, `0:800`, `ABC`, 400 s); about 2 h on 6 parallel runners. Summary generator:
`python3 -I benchmarks/MeshRepairBenchmark/alpha_defaults_summary.py <merged dir>`.
