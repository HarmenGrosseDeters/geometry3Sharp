## tail models (4 cores, timers off): per operation, median of the runs; ms is wall time with collections
| case | op | runs | base ms (range) | limbs ms (range) | change | GC pause ms base -> limbs | allocated GB base -> limbs | hash equal |
|---|---|---|---|---|---|---|---|---|
| copy_107554 | 0 | 5/5 | 26371 (25347-26808) | 25011 (24391-25347) | -5.2 % | 4649 -> 4319 | 10.49 -> 10.22 | True |
| copy_107554 | 1 | 5/5 | 16568 (16056-17505) | 16214 (15891-16860) | -2.1 % | 4368 -> 4064 | 8.51 -> 8.25 | True |
| copy_107554 | 2 | 5/5 | 16385 (15692-17680) | 16276 (16102-16756) | -0.7 % | 4376 -> 4087 | 8.49 -> 8.23 | True |
| copy_42327 | 0 | 5/5 | 29638 (29394-30964) | 29795 (28686-30208) | +0.5 % | 3309 -> 3286 | 10.17 -> 9.88 | True |
| copy_42327 | 1 | 5/5 | 22223 (21263-23342) | 23034 (22642-24749) | +3.6 % | 3233 -> 3266 | 7.77 -> 7.47 | True |
| copy_42327 | 2 | 5/5 | 23025 (21781-24458) | 22755 (22197-23130) | -1.2 % | 3556 -> 3062 | 7.81 -> 7.52 | True |

## normal strata (4 cores, timers off): the sum of the per-operation medians
| stratum | operations | base s (range of the run totals) | limbs s (range) | change | GC pause s | allocated GB | hashes equal |
|---|---|---|---|---|---|---|---|
| 20 pairs | 60 | 6.17 (6.06-6.45) | 6.17 (5.88-6.47) | +0.0 % | 0.13 -> 0.13 | 2.36 -> 2.35 | 60 / 60 |
| 13 models with a rotated copy | 39 | 4.39 (4.10-4.60) | 4.29 (4.10-4.56) | -2.2 % | 0.12 -> 0.11 | 1.48 -> 1.48 | 39 / 39 |
