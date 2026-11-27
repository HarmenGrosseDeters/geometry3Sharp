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

## Coarsening budget on a narrow gap

`--gap-budget` generates the W04-gap-2 geometry from [PR #1711](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1711): two unit boxes with a gap of `1/32`. It starts at alpha = gap / 4 and offset = gap / 40. It reports `Compute()` time, the estimate at the request, the coarsening actually applied, cleanliness, component count and whether the gap midpoint is inside the result. Validation runs outside the timed interval.

```sh
dotnet build benchmarks/OpenSoupWrapBenchmark -c Release
for run in 1 2 3 4 5; do
  dotnet benchmarks/OpenSoupWrapBenchmark/bin/Release/net10.0/OpenSoupWrapBenchmark.dll --gap-budget
done
```

Library revision `4d2c8b1c56b3888b553ea00f58fff448b59c44ef`, Linux x64, .NET 10 Release, one wrap thread. Times are the median of five runs in separate processes. Every run returned a clean mesh with the same triangle and component counts for its settings. [Raw timings and geometry checks](results/w04-gap-budget.csv)

| Estimated budget | Coarsening cap | Applied scale | Result triangles | Components | Gap midpoint inside | Median Compute |
|---:|---:|---:|---:|---:|:---:|---:|
| Off | None | 1.000 | 266,120 | 2 | No | 4.352 s |
| 50,000 | None | 3.481 | 19,286 | 1 | Yes | 0.201 s |
| 50,000 | 1.5 | 1.500 | 120,048 | 2 | No | 1.807 s |

The uncapped budget bridged the gap. With the `1.5` cap the components stayed separate, and the result exceeded the 50,000-triangle estimate budget. See the [Z3 pilot](../../tools/Z3Pilot/README.md#alpha-wrap-budget-and-coordinate-range) for the estimate's local algebra and IEEE-754 witnesses.

### Range and public API replay

`dotnet run --project benchmarks/OpenSoupWrapBenchmark -c Release -- --budget-range` builds the same two boxes and runs the public API with a 50,000 estimate budget. The unit, `2^-126` and `2^126` inputs use the same relative parameters and each produces a clean 19,136-triangle mesh. At unit size, an explicit `alpha = 2^-550` and `offset = alpha/8` produce an infinite estimate at the request. Coarsening still computes finite parameters and the wrap returns clean.

With both explicit parameters set to the smallest positive binary64 number, the scale factor overflows. Before the offset fix, the adjusted offset also overflowed and the API returned `InvalidParameters`. The fallback recovered the finite offset and produced a clean 19,684-triangle wrap, the same count as the explicit replay with equal finite alpha and offset. `AlphaCoarsening` reports positive infinity. The other five CSV rows were identical in this first comparison. The CLI enforces a separate lower bound on the request.

[Before](results/w04-budget-range.csv) · [After the offset fix](results/w04-budget-range-after.csv) · [After the weld-cell fix](results/w04-budget-range-after-weld.csv) · [Z3 boundary replays](../../tools/Z3Pilot/results/alpha_wrap_budget_2026-11-27.json)

### Weld-cell range

`dotnet run --project benchmarks/OpenSoupWrapBenchmark -c Release -- --weld-range` runs the two-box case at the origin and after an exactly representable UTM-like translation. The requests exercise a zero weld-cell width and a positive subnormal width. Previously the estimator merged unrelated corners when their cell indices saturated. With the weld-cell fix all four runs choose the same alpha and offset (about 0.026869) under the 50,000 estimate budget. All four meshes are clean. The carving yields 19,684 triangles at the origin and 19,630 in the translated frame.

In `--budget-range`, the unit and squared-alpha-underflow cases now use identical effective parameters and both produce 19,136 triangles. The extreme factor-overflow case still matches its explicit replay at 19,684 triangles. The three ordinary input-scale controls have identical CSV rows before and after the weld-cell change.

[Before the weld-cell fix](results/w04-weld-range-before.csv) · [After the weld-cell fix](results/w04-weld-range-after.csv)

### Carving under translation

`dotnet run --project benchmarks/OpenSoupWrapBenchmark -c Release -- --frame-audit` repeats the same two boxes with fixed alpha and offset and no budget. At the origin and in the UTM frame, disabling pinch and quality refinement reproduces the full runs' respective triangle counts of 19,684 and 19,630 and their carving counters. The `carve` rows retain the manifold step and extraction. All results in the audit pass the clean-mesh check, have one connected component, and contain both box centers and the gap center.

The initial box for the ordinary parameters differs by up to `1.52e-10` per axis after mapping back. The dyadic control chooses an exactly representable box margin of `1/8` in both frames. Its triangle counts still differ (15,164 at the origin and 15,136 in UTM). A separate public call capped at one Steiner point exposes the first inserted point. In the dyadic control, its mapped-back z changes from `-0.02634638046891702` to `-0.026346380468922348`. Binary64 addition of the UTM offset and subtraction back reproduce that change in the Z3 replay.

For the centered control, moving the translated input back to the origin before wrapping reproduces the origin mesh bit for bit in the local frame. Restoring the output to UTM passes the clean-mesh check. Mapping those stored output coordinates back changes each coordinate by at most `4.66e-10`. The `vertex_sample_distance` column estimates the symmetric Hausdorff distance by sampling all vertices in both directions and evaluating distances to the other surface in binary64.

[Frame audit results](results/w04-frame-audit.csv) · [Z3 replays](../../tools/Z3Pilot/results/alpha_wrap_budget_2026-11-27.json)

To check for drift after building the benchmark in Release mode, run:

```sh
dotnet run --project benchmarks/OpenSoupWrapBenchmark -c Release --no-build -- --frame-audit > /tmp/w04-frame-audit.csv
diff -u benchmarks/OpenSoupWrapBenchmark/results/w04-frame-audit.csv /tmp/w04-frame-audit.csv
```
