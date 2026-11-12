base: 61 units, total 298.2 s, median 4.21 s, max 15.2 s; fallback calls 110.7M, 241.2 s = 80.9 % of the time; allocated 243.4 GB; units above 5 s: 20
| variant | units | total s | median s | max s | units above 5 s | speedup (total) | fallback ms per unit (median) | fallback share % | allocated GB | hashes equal to base | predicate counts equal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| today (BigInteger) | 61 | 298.2 | 4.21 | 15.2 | 20 | 1.00 | 3457 | 80.9 | 243.4 | | |
| P2 accumulator | 61 | 63.7 | 0.80 | 2.8 | 0 | 4.68 | 146 | 15.4 | 11.98 | 61 / 61 | 61 / 61 |
| P4 scaled filter + P2 | 61 | 66.0 | 0.93 | 2.5 | 0 | 4.52 | 138 | 13.2 | 11.98 | 61 / 61 | 61 / 61 |
