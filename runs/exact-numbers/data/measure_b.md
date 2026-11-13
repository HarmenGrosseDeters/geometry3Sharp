## tail models (4 cores, timers off): per operation, median of the runs; ms is wall time with collections
| case | op | runs | base ms (range) | limbs ms (range) | change | GC pause ms base -> limbs | allocated GB base -> limbs | hash equal |
|---|---|---|---|---|---|---|---|---|
| copy_107554 | 0 | 5/5 | 21201 (20429-21441) | 20007 (19523-20833) | -5.6 % | 5093 -> 4307 | 10.74 -> 9.23 | True |
| copy_107554 | 1 | 5/5 | 16495 (15556-16806) | 15710 (15464-16031) | -4.8 % | 4913 -> 4215 | 10.03 -> 8.50 | True |
| copy_107554 | 2 | 5/5 | 16154 (15633-16462) | 15740 (15329-15967) | -2.6 % | 4867 -> 3982 | 9.98 -> 8.48 | True |
| copy_42327 | 0 | 5/5 | 24340 (24210-25129) | 20671 (19892-20845) | -15.1 % | 6116 -> 3273 | 20.44 -> 7.62 | True |
| copy_42327 | 1 | 5/5 | 23874 (23528-24441) | 19810 (19209-21137) | -17.0 % | 6040 -> 3015 | 20.16 -> 7.34 | True |
| copy_42327 | 2 | 5/5 | 23639 (22595-24058) | 19745 (19163-20208) | -16.5 % | 6101 -> 3233 | 20.22 -> 7.40 | True |

## normal strata (4 cores, timers off): the sum of the per-operation medians
| stratum | operations | base s (range of the run totals) | limbs s (range) | change | GC pause s | allocated GB | hashes equal |
|---|---|---|---|---|---|---|---|
| 20 pairs | 60 | 6.00 (5.83-6.25) | 5.84 (5.77-5.97) | -2.7 % | 0.11 -> 0.11 | 2.36 -> 2.36 | 60 / 60 |
| 13 models with a rotated copy | 39 | 4.15 (3.99-4.43) | 4.02 (3.83-4.17) | -3.3 % | 0.10 -> 0.10 | 1.48 -> 1.48 | 39 / 39 |

## hash gate: the 61 units of the #711 class (one run each)
61 units: hash, status and triangle count equal in 61; total 280.5 -> 288.0 s, allocated 243.4 -> 242.6 GB
