## tail models (4 cores, timers off): per operation, median of the runs; ms is wall time with collections
| case | op | runs | base ms (range) | limbs ms (range) | change | GC pause ms base -> limbs | allocated GB base -> limbs | hash equal |
|---|---|---|---|---|---|---|---|---|
| copy_107554 | 0 | 5/5 | 26417 (25798-26657) | 26214 (24076-26657) | -0.8 % | 5341 -> 4773 | 12.00 -> 10.49 | True |
| copy_107554 | 1 | 5/5 | 17089 (16466-17431) | 16372 (15638-16846) | -4.2 % | 4986 -> 4286 | 10.03 -> 8.51 | True |
| copy_107554 | 2 | 5/5 | 17386 (17213-18206) | 16572 (16100-17145) | -4.7 % | 5159 -> 4366 | 10.00 -> 8.49 | True |
| copy_42327 | 0 | 5/5 | 34476 (33528-34762) | 29638 (28603-30393) | -14.0 % | 6656 -> 3543 | 22.99 -> 10.17 | True |
| copy_42327 | 1 | 5/5 | 26908 (25654-27059) | 22319 (21699-25640) | -17.1 % | 6685 -> 3484 | 20.59 -> 7.77 | True |
| copy_42327 | 2 | 5/5 | 27001 (25557-28006) | 22857 (21794-23706) | -15.3 % | 6606 -> 3415 | 20.63 -> 7.81 | True |

## normal strata (4 cores, timers off): the sum of the per-operation medians
| stratum | operations | base s (range of the run totals) | limbs s (range) | change | GC pause s | allocated GB | hashes equal |
|---|---|---|---|---|---|---|---|
| 20 pairs | 60 | 6.32 (6.12-6.84) | 6.14 (5.95-6.51) | -2.8 % | 0.12 -> 0.12 | 2.36 -> 2.36 | 60 / 60 |
| 13 models with a rotated copy | 39 | 4.14 (4.00-4.37) | 4.22 (4.13-4.38) | +2.0 % | 0.12 -> 0.11 | 1.48 -> 1.48 | 39 / 39 |
