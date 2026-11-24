# BooleanVolumeGradient

This benchmark measures three things:
- the cost of `BooleanVolumeGradient` (docs/guides/booleans-and-imprint.md, "Volume Gradient");
- the boolean run with `ExactMeshBoolean.RetainClassification`;
- the default boolean path before and after the change. The rule is at most 1 % slower.

The inputs are procedural:
- two cube-spheres (`Sphere3Generator_NormalizedCube`, 16 and 32 edge vertices), the second one scaled and shifted;
- a box with flush faces (one contact region).

Every timing is the median of repeated runs, after one warm-up run.

```
dotnet run --project benchmarks/BooleanVolumeGradient -c Release -- gradient 11
dotnet run --project benchmarks/BooleanVolumeGradient -c Release -- default 21
```

For a before/after comparison, run `default` mode in two trees:
- "before": master, built with `-p:G3Before=true`. That build leaves out the gradient mode, which needs the new API.
- "after": master with this change.

Run the two built executables in alternation, three rounds each.

## Gradient cost (2026-11-24, cloud container, 4 cores)

Raw output: [gradient-2026-11-24.txt](gradient-2026-11-24.txt), from `gradient 11`.

| input | op | pieces | boolean | with RetainClassification | gradient | gradient / boolean |
|---|---|---|---|---|---|---|
| spheres n=16 | Union | 6428 | 55.6 ms | 47.7 ms | 17.8 ms | 32 % |
| spheres n=16 | Intersection | 3280 | 22.9 ms | 27.1 ms | 4.4 ms | 19 % |
| spheres n=16 | Difference | 5228 | 26.2 ms | 27.3 ms | 7.3 ms | 28 % |
| spheres n=32 | Union | 25196 | 61.1 ms | 59.6 ms | 29.7 ms | 49 % |
| spheres n=32 | Intersection | 10932 | 49.4 ms | 46.6 ms | 17.1 ms | 35 % |
| spheres n=32 | Difference | 19578 | 61.6 ms | 57.8 ms | 27.5 ms | 45 % |
| boxes, flush faces | Union / Intersection / Difference | 58-68 | 0.9-1.1 ms | 0.8-0.9 ms | 0.2-0.3 ms | 25-30 % |

What the numbers show:
- **Gradient:** about 1.1 µs per kept piece, serial. Most of the time goes into the conversion of the exact corners with
  `ExactPoint3.ToDouble` and the interval terms.
- **A cheaper conversion was tried and dropped.** It used `ExactPoint3.ApproxVector3d` with its documented bound, saved
  about 15-30 %, and that is within the noise. It would also have added a second bound to prove.
- **RetainClassification:** its effect on the boolean's time is not visible in the noise (−17 % to +18 % per row, either
  sign). The extra work is a clone of two bool arrays and of the winding vectors of the stacked pieces only.

## The default path before / after (2026-11-24)

Raw output: [default-ab-2026-11-24.txt](default-ab-2026-11-24.txt).
- "before" is master at ecd6f8e.
- "after" is the same tree with this change: the `ExactMeshBoolean.cs` diff plus `mesh_ops/BooleanVolumeGradient.cs`.
- Each value is the median of 21 runs. Three rounds were run in alternation.

| input | op | before, 3 rounds (ms) | after, 3 rounds (ms) | median change | allocated before (MB) | allocated after (MB) |
|---|---|---|---|---|---|---|
| spheres n=16 | Union | 55.01 / 42.13 / 42.25 | 42.73 / 47.68 / 40.23 | +1.1 % | 11.70-11.78 | 11.70-11.77 |
| spheres n=16 | Intersection | 33.80 / 24.95 / 21.84 | 24.77 / 28.27 / 29.11 | +13.3 % | 9.98-10.05 | 9.97-10.04 |
| spheres n=16 | Difference | 26.46 / 23.75 / 22.30 | 25.90 / 22.96 / 25.14 | +5.9 % | 11.68-11.76 | 11.70-11.78 |
| spheres n=32 | Union | 58.82 / 59.50 / 60.37 | 60.81 / 59.25 / 61.43 | +2.2 % | 39.13-39.41 | 39.11-39.43 |
| spheres n=32 | Intersection | 47.80 / 46.73 / 53.70 | 48.84 / 44.94 / 48.38 | +1.2 % | 32.12-32.29 | 32.09-32.29 |
| spheres n=32 | Difference | 57.28 / 54.72 / 61.89 | 57.36 / 56.25 / 60.77 | +0.1 % | 38.97-39.30 | 38.99-39.25 |
| boxes, flush faces | Union | 0.96 / 0.89 / 1.43 | 0.80 / 1.11 / 0.79 | −16.7 % | 0.70 | 0.70 |
| boxes, flush faces | Intersection | 0.79 / 0.88 / 1.33 | 0.72 / 1.13 / 0.83 | −5.7 % | 0.68 | 0.68 |
| boxes, flush faces | Difference | 1.12 / 0.97 / 0.89 | 0.78 / 0.89 / 1.23 | −8.2 % | 0.69-0.70 | 0.69-0.70 |
| sum of medians | | 258.3 | 265.2 | +2.7 % | | |

**Reading.** The noise on this shared cloud machine is ±15 % per row between identical runs: in "before" alone, the same
case ranged from 21.84 to 33.80 ms. That is an order of magnitude above the 1 % rule, so this measurement can neither
confirm nor refute it.

A pinned run (one core, `DOTNET_PROCESSOR_COUNT=1`) was noisier still, with +-20 % between rounds of the same build.

What is exact:
- **The allocations are the same.** The min-max ranges of the two trees overlap in every row. The run-to-run spread comes
  from the parallel schedule.
- **The code of the default path is the same per piece.** With `RetainClassification = false`:
  - `front` stays null, so the per-piece test `front != null && ...` exits at its first operand, exactly as the old
    `front != null` did;
  - the other changes are three bool tests per `Compute()`.

A 1 % timing verdict needs a quiet machine. The job: build both trees as above, then run each executable 5 times in
alternation with `default 31`, and compare the medians per row.
