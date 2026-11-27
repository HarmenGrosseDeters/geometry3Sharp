# OpenSoupWrapBenchmark

The size estimate of the alpha wrap on open input and the budget `WrapMaxEstimatedTriangles` (issue #1113). Inputs, all procedural:
the reproducer (two overlapping unwelded `Sphere3Generator_NormalizedCube` spheres, 12 edge vertices per cube side, 1.2 apart in x,
the second shifted 0.3 in z, the axis caps whose centroid direction is within 0.5 rad of an axis removed, a soup of 1824 triangles),
the same two spheres closed, and a flat open sheet of 4 x 4 (7200 triangles).

    dotnet run --project benchmarks/OpenSoupWrapBenchmark -c Release [-- --quick]

Committed run: container with 4 cores, Release net10.0, one run (expect about +-15 % noise on the seconds; the triangle counts are
deterministic). "Before" is master 5b99848d, "after" this change.

## The estimate against the raw wrap (`MeshAlphaWrap`, relative alpha 100, offset 1/1000, no budget)

| input | estimate before | estimate after | raw triangles | raw / estimate before | raw / estimate after |
|---|---:|---:|---:|---:|---:|
| two open spheres (#1113) | 28,796 | 57,592 | 59,298 | 2.06 | 1.03 |
| two closed spheres | 38,681 | 38,681 | 14,086 | 0.36 | 0.36 |
| open sheet 4 x 4 | 15,000 | 30,000 | 31,108 | 2.07 | 1.04 |

A closed input is unchanged (the estimate is about twice the size of a closed solid, by design, see `EstimateDensity`).

## `MeshRobustRepair` at the library defaults (alpha diagonal / 300) with `WrapMaxEstimatedTriangles`, the reproducer

| budget | before: raw triangles | before: alpha / diag | before s | after: raw triangles | after: alpha / diag | after s | after: result triangles |
|---|---:|---:|---:|---:|---:|---:|---:|
| off (default) | 515,716 | 0.00333 | 26.7 | 515,716 | 0.00333 | 28.1 | 99,024 |
| 200,000 | 398,916 | 0.00379 | 18.5 | 201,388 | 0.00537 | 9.9 | 56,024 |
| 100,000 | 201,388 | 0.00537 | 9.2 | 102,146 | 0.00759 | 6.0 | 36,390 |
| 50,000 | 102,146 | 0.00759 | 5.4 | 51,918 | 0.0107 | 3.4 | 23,280 |

The default row is the same wrap bit for bit (the seconds differ by the noise of the container). A budget of N now gives about N raw
triangles on the open soup (before: about 2N), so each budget's after row is the before row of the budget 2N, at about half the
seconds of the same alpha. About 3 s (the failed solidify attempt, the exact resolve, the repair around the wrap) stay whatever the
budget is. The open sheet through `MeshRobustRepair` is unchanged (estimate 270,000 before and after): the mesh that the repair hands
to the wrap has twice the sheet's area (not diagnosed further), so the one-sided estimate was already right there.

## The open-area estimate only with a budget (follow-up of #1123)

`MeshAlphaWrap.Compute()` ran `AlphaWrapEstimate.OpenArea` on every call only to report `EstimatedTriangles`; it now runs only when
`MaxEstimatedTriangles > 0`. Without a budget `EstimatedTriangles` is the one-sided estimate of before #1123, and the wrap is the
same bit for bit (`AlphaWrapOpenEstimateTests.NoBudget_EstimateIsOneSided_WrapUnchanged`). Third table of the program: `Compute()` of a
flat welded open sheet at a coarse alpha (relative alpha 8, so that the wrap itself is small), no budget (after) against a budget of
`int.MaxValue`, which never binds but runs the estimate, as every `Compute()` did before. Container with 4 cores, Release net10.0, one run:

| input triangles | no budget (s) | budget that never binds, the old cost (s) | saved |
|---:|---:|---:|---:|
| 180,000 | 0.33 | 0.38 | 0.05 s |
| 500,000 | 0.97 | 1.04 | 0.07 s |
| 980,000 | 1.93 | 2.11 | 0.18 s |

That is about 0.2 s per million input triangles at the largest size (one run each: the 0.05 s and 0.07 s of the two smaller sizes are
within the noise of the container), less than the 1 s per million first assumed; a soup with many boundary edges is dearer (not
measured). Measurable only at this size: the other tables are unchanged.

## The offset band of the estimate (issue #1593)

`EstimatedTriangles` now includes the offset band (`EstimatedRimTriangles`: the rim of open sheets and thin plates, sharp edges, sheets at a
small offset) and the open components are counted twice also without a budget, so the third table above is obsolete: `Compute()` runs the
analysis (`AlphaWrapEstimate.Terms`) every time, with or without a budget. Same program, container with 4 cores, Release net10.0, one run each
(about +-15 % noise on the seconds), before = master 04391d3b, after = #1593 (relative alpha 100, offset 1/1000, ratio 10):

| input | estimate before | estimate after | raw triangles | raw / estimate before | raw / estimate after |
|---|---:|---:|---:|---:|---:|
| two open spheres (#1113) | 28,796 | 81,469 | 59,298 | 2.06 | 0.73 |
| two closed spheres | 38,681 | 38,681 | 14,086 | 0.36 | 0.36 |
| open sheet 4 x 4 | 15,000 | 39,394 | 31,108 | 2.07 | 0.79 |

At this ratio the estimate is now about 1.3 times the open wrap (the band and rim are calibrated at alpha / offset 4 to 50, where the sheet
measured 0.78 to 0.84; the issue's cases are at ratio 30 and more); closed input is bit for bit unchanged. `MeshRobustRepair` with a budget
(`WrapMaxEstimatedTriangles`, alpha diagonal / 300): the wrap of the open soup is about 0.71-0.76 of the budget (before 1.01-1.04), so a budget of N
now gives about 0.75 N raw triangles on these inputs (the estimate over-counts at ratio 10 by the 1.3 above), in less time:

| input | budget | before: raw triangles | before s | after: raw triangles | after s |
|---|---:|---:|---:|---:|---:|
| two open spheres | 200,000 | 201,388 | 8.1 | 151,628 | 6.4 |
| two open spheres | 100,000 | 102,146 | 4.5 | 73,854 | 3.6 |
| two open spheres | 50,000 | 51,918 | 2.6 | 35,674 | 2.2 |
| open sheet 4 x 4 | 200,000 | 199,254 | 6.3 | 160,772 | 5.5 |
| open sheet 4 x 4 | 100,000 | 100,370 | 4.1 | 79,940 | 3.6 |
| open sheet 4 x 4 | 50,000 | 50,876 | 3.1 | 39,884 | 2.7 |

The default row (no budget) is the same wrap bit for bit (515,716 and 268,024 raw triangles). The cost of the analysis, `Compute()` of a flat
open sheet at a coarse alpha, no budget (now also runs the analysis) against a budget that never binds, in seconds: 180,000 triangles 0.45 / 0.32
(before 0.31 / 0.38), 500,000 0.97 / 0.99 (0.88 / 0.95), 980,000 2.00 / 2.10 (1.89 / 1.92): about 0.1 s per million triangles more than without
the analysis (inside the noise at the two smaller sizes); `AlphaWrapEstimate.Terms` alone takes 0.6-0.8 s on a sphere of 805k triangles
(`OpenArea` 0.3-0.4 s).
