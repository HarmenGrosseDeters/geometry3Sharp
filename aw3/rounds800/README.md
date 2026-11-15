# 800 run for #774 (round-based carving), merge gate run after the merge

base = 9790954 (master directly before #774), head = 623c901 (master with #774 and #899).
`bench.sh`, `BENCH_REPAIR=wrappipe`, `BENCH_FIDELITY=none`, `TIMEOUT=600`, 4 cores, mesh list `list800.txt`
(sample800_2611). Both runs ran at the same time on the same machine, so the times are not comparable; only the
quality fields are evaluated.

| | base | head |
|---|---:|---:|
| closed | 800/800 | 800/800 |
| triangles < 0.5° / > 170° | 0 / 0 | 0 / 0 |
| final p01 median / mean / worst | 11.34 / 11.711 / 10.63 | 11.33 / 11.707 / 10.61 |
| final min angle median / mean / worst | 9.49 / 8.392 / 1.29 | 9.68 / 8.419 / 1.01 |
| meshes with final min < 2° / < 3° / < 5° | 3 / 15 / 72 | 3 / 17 / 61 |
| raw p01 median / worst | 20.11 / 14.23 | 20.09 / 14.04 |
| raw min worst | 0.72 | 1.01 |
| unsimplified (final triangles = raw triangles) | 0 | 0 |
| final p01 > 15 (simplified, but weakly) | 25 | 25 |

Paired per-mesh head − base: final p01 median +0.001 (mean −0.004), final min median 0.000 (mean +0.027),
raw p01 median −0.016. Head better / worse: p01 407 / 392, min 390 / 376. Largest per-mesh min angle changes −5.3° / +6.1°.
The worst final min angle (head, 1681122, 1.01°) is the raw wrap's own minimum (raw_min 1.009); base has 3.63° there.
