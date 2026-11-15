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

Candidates, status, earliest time of impact and contacts are the same on both paths in every scene. Over the runs of
this PR puffer-ball took 29.9-32.7 s serial and 10.6-10.9 s parallel with the tree, 46.6-46.8 s and 16.5-19.7 s on the
old path (the container varies by about 15 %).

- puffer-ball: the candidate search goes from 27.0 s to 10.8 s serial; the preparation from 1.2 s to 2.5 s serial (the
  trees' sub-step boxes, 6 (k + 1) doubles per triangle and edge; about 0.6 GB at k = 4). What is left serial is the
  narrow phase, about 18.4 s for the 25M pairs (0.74 µs per pair): the floor of the serial sweep, #940.
- cloth-funnel (small motion, 18k triangles) is slower by 75 ms serial and 25 ms parallel: the sub-step tests per node
  cull little there. A size threshold does not separate it from armadillo-rollers (24k triangles, 0.8 s faster), so
  there is none.
- k = 8 with the tree (budget raised for the run): 8.4M candidates, 29.9 s serial and 9.8 s parallel; the pairs left
  are harder (about 1.65 µs per pair), so more sub-steps do not lower the serial floor.
