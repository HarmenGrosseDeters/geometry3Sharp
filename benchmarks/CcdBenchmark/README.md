# CcdBenchmark

Benchmark of `ContinuousCollision` and `MeshSweepCollision` (design note `docs/design/CCD.md`).

```
dotnet run --project benchmarks/CcdBenchmark -c Release -- synthetic
benchmarks/CcdBenchmark/fetch_sample.sh /tmp/ccdsample          # outside the repository, about 216 MB
dotnet run --project benchmarks/CcdBenchmark -c Release -- sample /tmp/ccdsample [minSeparation]
dotnet run --project benchmarks/CcdBenchmark -c Release -- sweep /tmp/ccdsample [minSeparation]
```

`sweep`: the self sweep of each sample scene's two frames (`frames/*.ply`, also fetched by `fetch_sample.sh`), serial and
parallel, with the time of the preparation and of the pair search and solve (`G3_SWEEP_SCENES`, `G3_SWEEP_BLOCK`,
`G3_SWEEP_TASKS` select scenes and job sizes for experiments, `G3_SWEEP_SUBSTEPS` the sub-steps, `G3_SWEEP_TREE_DOUBLES`
the budget of the sub-step box tree, 0 for the filter on the whole-step pairs; `candidates_ms` is the candidate search).

`synthetic`: the stage-1 probe's queries (CCD.md 7.1) through the library, on the filter and the interval path, and sphere
sweeps (A moving into B) with and without a minimum separation. `sample`: the Apache-2.0 sample of the ToI dataset
(Belgrod, Ferguson et al.; `Sample-Scalable-CCD-Data` at a pinned commit, SHA-256 per file in `sample_files.sha256`) against
its ground truth (`mma_bool`); exit code 1 on a false negative (minimum separation 0). The data is never committed.

## Results 2026-11-13 (4-core Xeon @ 2.1 GHz container, .NET 10, serial unless noted)

Raw: `results-synthetic-2026-11-13.txt`, `results-sample-2026-11-13.csv` (head of #793 before this README; the library
builds of 06:36-06:38 UTC ran beside the cloth-ball interval rows, which may be slower by that).

ToI sample, minimum separation 0, filter path:

| scene | file | queries | colliding | false neg. | false pos. | µs / query | µs max | boxes mean | boxes max |
|---|---|---|---|---|---|---|---|---|---|
| armadillo-rollers | 326ee | 19313 | 19308 | 0 | 0 | 153 | 15939 | 408 | 6425 |
| armadillo-rollers | 326vf | 4652 | 4650 | 0 | 0 | 197 | 4688 | 398 | 2749 |
| cloth-ball | 92ee | 94825 | 94822 | 0 | 0 | 122 | 4833 | 378 | 13459 |
| cloth-ball | 92vf | 19034 | 19032 | 0 | 0 | 158 | 5435 | 430 | 7506 |
| cloth-funnel | 227ee | 263 | 107 | 0 | 0 | 2365 | 270748 | 6073 | 775689 |
| cloth-funnel | 227vf | 92 | 27 | 0 | 0 | 30 | 159 | 89 | 435 |
| n-body-simulation | 18ee | 41036 | 41031 | 0 | 2 | 105 | 4194 | 291 | 3209 |
| n-body-simulation | 18vf | 9460 | 9459 | 0 | 0 | 115 | 4355 | 278 | 1456 |
| puffer-ball | 20ee | 61844 | 60894 | 0 | 0 | 131 | 11600 | 354 | 18479 |
| puffer-ball | 20vf | 16162 | 15803 | 0 | 0 | 121 | 2368 | 344 | 2227 |
| rod-twist | 3036ee | 4370 | 4179 | 0 | 0 | 276 | 9487 | 691 | 27908 |
| rod-twist | 3036vf | 801 | 765 | 0 | 0 | 197 | 5171 | 563 | 13226 |

Total 271 852 queries: **0 false negatives**, 2 false positives, 132 µs per query on average. The interval path gives the same
verdicts at 3-4x the time. Almost all of these queries collide (the dataset holds the broad-phase candidates of contact-rich
steps), so the cost is that of the near-contact case; one cloth-funnel edge-edge query needs 0.27 s (775 689 boxes), a
degenerate case to look at (CCD.md 10.5).

Synthetic (µs per query, boxes per query; offset 0 and 632117.25 alike):

| queries | VF filter | VF interval | EE filter | EE interval |
|---|---|---|---|---|
| near contact | 52-66 µs, 155 | 175-179 µs | 28-30 µs, 91 | 199-204 µs |
| random candidates | 0.8-1.1 µs, 2.9 | 3.1-3.4 µs | 2.2-2.3 µs, 5.6 | 5.2-6.6 µs |

Sphere sweeps (two spheres, A moving into B, `MaxTighteningIterations` 1024):

| triangles each | ms | candidate pairs | serial | parallel (4 cores) |
|---|---|---|---|---|
| 588 | 0 | 1690 | 46 ms | 14 ms |
| 588 | 0.01 | 2045 | 268 ms | 76 ms |
| 2700 | 0 | 7658 | 174 ms | 55 ms |
| 2700 | 0.01 | 11371 | 1.5 s | 0.39 s |
| 11532 | 0 | 37332 | 0.88 s | 0.24 s |
| 11532 | 0.01 | 63373 | 8.4 s | 2.1 s |

The tightening budget (256 / 1024 / 4096) changes the sweep's earliest time of impact by at most 1.5e-3 at 588 and 2700 triangles
and not at all at 11532, at 0.5x / 1x / 1.4x the time (with a minimum separation; without one it is not used).

## Nearly parallel edges (follow-up, 2026-11-13, CCD.md 10.8)

Raw: `results-sample-2026-11-13b.csv` (same container type, evening; the absolute times of this run are about 40 % above
the morning run on every row, also on the unchanged vertex-triangle path: an A/B of `synthetic` with master and this
branch alternating in one job gave equal times within 5 %, eg VF near 73.9-82.4 µs master vs 71.5-76.9 µs branch, EE near
42.8-45.1 vs 42.6-48.7, so the shift is the machine).

| file | µs / query | µs max | boxes mean | boxes max |
|---|---|---|---|---|
| cloth-funnel 227ee, before | 2365 | 270748 | 6073 | 775689 |
| cloth-funnel 227ee, after | 67 | 1620 | 150 | 3261 |

All other files: the same box counts as before (puffer-ball 20ee and rod-twist 3036ee slightly fewer: max 18479 -> 9164 and
27908 -> 12856), the same verdicts: 271 852 queries, 0 false negatives, 2 false positives.

After the review of #845 (`results-sample-2026-11-13c.csv`, same evening machine): edge-edge pairs that are not nearly
parallel are also split along their cross product, on every other level (CCD.md 10.8, with the variants measured).

| file | µs / query | µs max | boxes mean | boxes max |
|---|---|---|---|---|
| armadillo-rollers 326ee | 109 (204 before the PR) | 19111 | 209 (408) | 3370 (6425) |
| cloth-ball 92ee | 104 (191) | 5919 | 193 (378) | 3305 (13459) |
| cloth-funnel 227ee | 58 (2365 in the morning run) | 971 | 114 (6073) | 1495 (775689) |
| n-body-simulation 18ee | 78 (146) | 1512 | 153 (291) | 1856 (3209) |
| puffer-ball 20ee | 97 (183) | 3404 | 188 (352) | 3296 (9164) |
| rod-twist 3036ee | 225 (363) | 13594 | 405 (678) | 20849 (12856) |

Vertex-triangle rows unchanged (the same box counts). All 271 852 queries: 0 false negatives, 2 false positives, 116 µs
and 223 boxes per query on average (186 µs and 365 boxes before the PR, same machine).

## Parallel broad phase (2026-11-13, CCD.md 10.9)

Raw: `results-sweep-ab-2026-11-13.csv` (master at 2ccb989c vs this branch, two alternating rounds in one job, parallel,
4 cores; `results-sweep-ab-fused-2026-11-13.csv`: the rejected fused-jobs variant against master before #845):

| scene | triangles | candidate pairs | master | batches |
|---|---|---|---|---|
| armadillo-rollers | 24 242 | 7 099 982 | 2.94-2.99 s | 2.32-2.40 s |
| cloth-ball | 92 230 | 6 852 873 | 6.85-7.17 s | 6.00-6.24 s |
| cloth-funnel | 18 484 | 41 089 | 0.088-0.099 s | 0.061-0.063 s |
| n-body-simulation | 146 480 | 31 763 261 | 9.77-9.94 s | 7.13-7.40 s |
| rod-twist | 79 864 | 2 489 604 | 2.37-2.39 s | 1.95-1.99 s |
| puffer-ball | 1 064 216 | 249 805 425 | out of memory | 40.0 s (serial 112 s) |

Serial times are unchanged within noise; candidates, contacts and the earliest time are the same as on master.

## Sub-steps (2026-11-14, CCD.md 10.10, #866)

Raw: `results-sweep-substeps-2026-11-14.csv` (`G3_SWEEP_SUBSTEPS=1` and `4`, one run each after a warm-up, 4 cores, the
final code of the PR). SubSteps = 1 is the broad phase of 10.9 without the per-query sorts.

| scene | candidates k=1 | candidates k=4 | parallel k=1 | parallel k=4 | serial k=1 | serial k=4 |
|---|---|---|---|---|---|---|
| armadillo-rollers | 7 099 982 | 1 127 562 | 1.87 s | 1.59 s | 6.48 s | 5.38 s |
| cloth-ball | 6 852 873 | 2 450 850 | 5.56 s | 5.73 s | 20.8 s | 20.3 s |
| cloth-funnel | 41 089 | 26 144 | 0.052 s | 0.061 s | 0.104 s | 0.104 s |
| n-body-simulation | 31 763 261 | 4 832 461 | 5.67 s | 4.88 s | 18.5 s | 11.7 s |
| rod-twist | 2 489 604 | 1 823 450 | 2.23 s | 1.90 s | 7.18 s | 6.79 s |
| puffer-ball | 249 805 425 | 24 953 688 | 32.6 s | 19.9 s | 93.8 s | 50.7 s |

The earliest time of impact and the contacts are the same for k = 1 and 4 in every scene. The container's speed varies
by about 15 % between runs: over the runs during this PR (not all of the same code), puffer-ball took 28.1-33.2 s at k = 1 and
18.2-20.5 s at k = 4 (parallel), the other scenes moved by up to 0.5 s. On master it took 40.0 s (10.9).

Where the puffer-ball time goes at k = 4 (thread time, instrumented run): the self edge pairs of the whole-step tree
(186M, filtered to 19M) about half, the vertex-triangle candidates (75M to 6M) a fifth, the narrow phase of the 25M
candidates the rest; without tightening (`MaxTighteningIterations = 0`) the sweep is only about 1 s faster. k = 8 is
slower (23.9 s: 8.4M candidates, but the filter and the hulls cost more).

## Sub-step box tree (2026-11-15, CCD.md 10.11, #866)

Raw: `results-sweep-tree-2026-11-15.csv`. One build, two runs of `sweep` one after the other (one Compute each after a
warm-up, 4 cores): `G3_SWEEP_TREE_DOUBLES=0` (prefix `old`: the sub-step filter on the pairs of the whole-step trees,
the path of 10.10 and master) and the default (prefix `tree`: the SubStepBoxTrees). SubSteps = 4. `candidates` is the wall time
of collecting the candidates (the tree searches and the filter; `CandidateSeconds`), part of the total.

| scene | candidates | serial old | serial tree | parallel old | parallel tree | candidates serial old → tree |
|---|---|---|---|---|---|---|
| armadillo-rollers | 1 127 562 | 5.09 s | 4.26 s | 1.72 s | 1.27 s | 0.92 → 0.35 s |
| cloth-ball | 2 450 850 | 18.3 s | 17.5 s | 5.21 s | 5.19 s | 1.46 → 1.06 s |
| cloth-funnel | 26 144 | 0.111 s | 0.186 s | 0.076 s | 0.101 s | 0.039 → 0.098 s |
| n-body-simulation | 4 832 461 | 11.1 s | 8.54 s | 3.97 s | 2.65 s | 3.44 → 1.24 s |
| rod-twist | 1 823 450 | 5.92 s | 5.76 s | 1.78 s | 1.81 s | 0.48 → 0.62 s |
| puffer-ball | 24 953 688 | 46.8 s | 32.7 s | 19.1 s | 10.6 s | 27.0 → 10.8 s |

The candidate count, status, earliest time of impact and number of contacts are the same on both paths in every scene
(the CSV records these; the pairs themselves are compared by `SubStepTree_EqualsFilterOnWholeStepPairs`). Over the runs of
this PR puffer-ball took 29.9-32.7 s serial and 10.6-10.9 s parallel with the tree, 46.6-46.8 s and 16.5-19.7 s on the
old path (the container varies by about 15 %).

- puffer-ball: the candidate search goes from 27.0 s to 10.8 s serial; the preparation from 1.2 s to 2.5 s serial (the
  trees' sub-step boxes, leaves and nodes about 1.5 * 6 (k + 1) doubles per triangle and edge; about 1 GB at k = 4). What is left serial is the
  narrow phase, about 18.4 s for the 25M pairs (0.74 µs per pair): the floor of the serial sweep, #940.
- cloth-funnel (small motion, 18k triangles) is slower by 75 ms serial and 25 ms parallel: the sub-step tests per node
  cull little there. A size threshold does not separate it from armadillo-rollers (24k triangles, 0.8 s faster), so
  there is none.
- k = 8 with the tree (budget raised for the run): 8.4M candidates, 29.9 s serial and 9.8 s parallel; the pairs left
  are harder (about 1.65 µs per pair), so more sub-steps do not lower the serial floor.

## Narrow-phase pre-filter (2026-11-16, CCD.md 10.12, #940)

Raw: `results-sweep-prefilter-2026-11-16.csv`. One job, master at `6819dfe` (variant `master`) and this branch (`branch`) built
side by side, run one after the other (round 1: all scenes; round 2: puffer-ball again), one `Compute` each after a warm-up,
4 cores, SubSteps = 4, sub-step tree. `candidates_ms` of the branch includes the first-box test of each candidate (the pairs are
dropped in the job that finds them); `pairs_ms` includes `candidates_ms`.

| scene | candidates | serial master | serial branch | parallel master | parallel branch |
|---|---|---|---|---|---|
| armadillo-rollers | 1 127 562 | 4.44 s | 2.34 s | 1.35 s | 0.87 s |
| cloth-ball | 2 450 850 | 20.7 s | 6.74 s | 5.48 s | 2.02 s |
| cloth-funnel | 26 144 | 0.183 s | 0.166 s | 0.088 s | 0.084 s |
| n-body-simulation | 4 832 461 | 9.36 s | 3.77 s | 2.75 s | 1.58 s |
| rod-twist | 1 823 450 | 6.71 s | 4.03 s | 1.99 s | 1.47 s |
| puffer-ball (round 1 / 2) | 24 953 688 | 33.9 / 34.9 s | 23.5 / 24.9 s | 11.0 / 11.4 s | 8.8 / 10.4 s |

The candidate count, status, earliest time of impact and number of contacts are equal in every row (the CSV; the contacts
and their results are compared by the tests). The container varies by about 10-15 % between runs.

Where the puffer-ball time goes (serial, instrumented runs of the same code, timers inflate by about 1.5 s):
- master: `Solve` 18.9 s, of which the 23.9M pairs that end at the first box 7.1 s (0.30 µs each) and the 458k pairs with seven or more boxes
  11.1 s (24 µs each); the gathers of the points 1.7 s. A time limit at the earliest contact, as an experiment, took `Solve` to 9.9 s.
- branch: preparation 3.5 s, candidates (tree search and first-box test) 14.1-14.9 s, of which the first-box test and the gathers about 3 s,
  survivors (1.1M pairs) about 5 s.
The serial sweep of the 1M-triangle frame stays above 20 s (23.5-24.9 s): #866 and #940 stay open.

## Fast separating-plane filter (`CCD-1`) and linear Bézier `t`-clipping (2026-11-26, CCD.md 10.13)

Command (same build flags, single-threaded except the `parallel` column of the sphere sweeps):

```
DOTNET_TieredPGO=0 dotnet run -c Release --no-build --project benchmarks/CcdBenchmark -- synthetic
```

Machine: Intel Xeon @ 2.30 GHz (2 CPUs), .NET 10.0.115. Before: `origin/master` (`f90503ba`); after: `CCD-1` range-guarded
hardware-double separating-plane filter (`separated_along`) plus 3-pass Sederberg-Nishita (1990) linear Bézier `t`-clipping
(`UpdateTClip` in `evaluate`).

Synthetic queries (`n = 20 000` per row, filter path; collision counts identical in every row):

| kind | offset | class | before (µs / query, boxes) | after (µs / query, boxes) | speedup | collisions |
|---|---|---|---|---|---|---|
| VF | 0 | near | 131.32 µs, 155.7 | 46.16 µs, 55.6 | 2.84x | 10420 |
| VF | 632117.25 | near | 129.62 µs, 155.3 | 39.54 µs, 53.4 | 3.28x | 10432 |
| EE | 0 | near | 67.47 µs, 73.4 | 32.15 µs, 39.4 | 2.10x | 8190 |
| EE | 632117.25 | near | 74.27 µs, 73.4 | 30.46 µs, 38.4 | 2.44x | 8156 |
| VF | 0 | random | 2.00 µs, 2.9 | 0.81 µs, 1.6 | 2.47x | 94 |
| VF | 632117.25 | random | 2.24 µs, 2.9 | 0.79 µs, 1.5 | 2.84x | 90 |
| EE | 0 | random | 3.09 µs, 3.6 | 1.90 µs, 2.6 | 1.63x | 264 |
| EE | 632117.25 | random | 3.12 µs, 3.5 | 1.91 µs, 2.5 | 1.63x | 258 |

Sphere sweeps (`MaxTighteningIterations = 1024`, default; 2 cores for `parallel`):

| triangles each | ms | candidates | serial before → after | parallel before → after | mean boxes before → after |
|---|---|---|---|---|---|
| 588 | 0 | 1525 | 96.1 → 48.2 ms (1.99x) | 51.0 → 29.8 ms (1.71x) | 65.7 → 33.0 |
| 588 | 0.01 | 1959 | 544.6 → 314.7 ms (1.73x) | 286.2 → 160.5 ms (1.78x) | 302.8 → 161.7 |
| 2700 | 0 | 6397 | 443.3 → 210.4 ms (2.11x) | 237.0 → 112.3 ms (2.11x) | 70.9 → 34.8 |
| 2700 | 0.01 | 9816 | 3251.7 → 1927.8 ms (1.69x) | 1703.1 → 996.5 ms (1.71x) | 369.7 → 205.0 |
| 11532 | 0 | 27854 | 1852.8 → 831.3 ms (2.23x) | 968.5 → 453.4 ms (2.14x) | 68.7 → 32.4 |
| 11532 | 0.01 | 50408 | 17765.9 → 10780.5 ms (1.65x) | 9315.3 → 5652.1 ms (1.65x) | 405.0 → 231.3 |

At `MaxTighteningIterations = 4096` (11 532 triangles, `ms = 0.01`), serial time drops from `29 475.7 ms` to `12 657.8 ms`
(2.33x) and the `earliest_near - earliest_toi` gap closes from `1.03e-4` to `0`. Because `t`-clipping narrows box time
intervals continuously rather than strictly at binary midpoints, `TimeOfImpact`, `NearContactTime`, and `Iterations` are not
bit-identical to master, while G1/G2/G3 guarantees, `NoCollision` verdicts, and `2^k` scale invariance hold identically.
