# CcdBenchmark

Benchmark of `ContinuousCollision` and `MeshSweepCollision` (design note `docs/design/CCD.md`).

```
dotnet run --project benchmarks/CcdBenchmark -c Release -- synthetic
benchmarks/CcdBenchmark/fetch_sample.sh /tmp/ccdsample          # outside the repository, about 157 MB
dotnet run --project benchmarks/CcdBenchmark -c Release -- sample /tmp/ccdsample [minSeparation]
```

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
