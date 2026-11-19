# Clipper2Benchmark

Offsets of the Clipper2 addon on procedural inputs (no files): a smooth ring of n vertices (`Inputs.Star`: radius 10,
amplitude 0.1, 12 waves, delta 0.4, so the vertex spacing is far below delta) and 30 x 30 / 100 x 100 grids of rotated
hexagons (delta 0.05). Issue #1169.

```
dotnet run --project benchmarks/Clipper2Benchmark -c Release -- [reps] [n ...]
```

Prints the median time of each offset over `reps` runs and a digest of the result (equal digests: equal results bit for
bit). Run on the base and on the branch.

## #1169: single-point inner join (4-core Xeon, net10.0, one run per base row, median of 5 per branch row)

| op | base ms | branch ms | digest |
|---|---|---|---|
| Offset round ring n=1000 | 43.2 | 4.2 | differs (rounding) |
| Offset miter ring n=1000 | 33.9 | 2.8 | differs |
| Offset round ring n=20000 | 482.5 | 45.8 | differs |
| Offset miter ring n=20000 | 343.5 | 21.7 | differs |
| Offset round ring n=100000 | 20423 | 177 | differs |
| Offset miter ring n=100000 | 16250 | 70 | differs |
| Offset round / miter grid 30x30 | 112.6 / 14.5 | 89.5 / 13.6 | equal |
| Offset round / miter grid 100x100 | 3175 / 363 | 3002 / 372 | equal |

The ring offsets differ from the base by the rounding of the miter point (at most 3 grid units, `Clipper2InnerJoinTests`);
the grids have no concave join with a long enough edge, so they are bit for bit the same. The time of the ring offset was
the final union (the raw offset takes 10 ms at n = 100 000): 2.4 n raw vertices, only 0.6 n crossings, but the active edge
list grew with n (average 17 / 74 / 289 / 717 edges at n = 1k / 5k / 20k / 50k) because each concave vertex left a
tooth of length delta; every scanbeam costs the list length.
