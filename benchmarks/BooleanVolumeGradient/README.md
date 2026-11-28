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
| spheres n=16 | Union | 6428 | 51.8 ms | 46.0 ms | 22.7 ms | 44 % |
| spheres n=16 | Intersection | 3280 | 19.2 ms | 18.2 ms | 11.1 ms | 58 % |
| spheres n=16 | Difference | 5228 | 24.0 ms | 20.7 ms | 20.7 ms | 86 % |
| spheres n=32 | Union | 25196 | 61.4 ms | 59.8 ms | 86.9 ms | 142 % |
| spheres n=32 | Intersection | 10932 | 53.7 ms | 43.4 ms | 41.9 ms | 78 % |
| spheres n=32 | Difference | 19578 | 65.5 ms | 57.0 ms | 69.0 ms | 106 % |
| boxes, flush faces | Union / Intersection / Difference | 58-68 | 0.9 ms | 0.8 ms | 0.1-0.2 ms | 15-20 % |

What the numbers show:
- **Gradient:** about 3-4 µs per kept piece, serial. The time goes into the exact local frames:
  - each piece's three corners relative to its triangle's first corner, as exact BigInteger differences, each converted
    once with `ExactPoint3.ToDouble`;
  - the interval terms.
- **Why the local frames.** An earlier version evaluated in grid units relative to the center, at about 1.2 µs per
  piece. Its bounds became infinite for inputs that span many magnitudes, for example a unit box with a 2^-200 box in
  one boolean (exact review of #1535). The local frames keep every bound relative to its own entry. The test is
  `InputsOfVeryDifferentMagnitudes_StayWithinFiniteRelativeBounds_OfTheOracle`.
- **Cheaper variants tried:**
  - five exact differences per piece instead of three: about 1.5 times slower than the version kept;
  - `ExactPoint3.ApproxVector3d` in the old grid-unit evaluation: 15-30 % faster, within the noise, and it would have
    added a second bound to prove.
- **RetainClassification:** its effect on the boolean's time is not visible in the noise (−20 % to +5 % per row, either
  sign). The extra work is a clone of two bool arrays and of the winding vectors of the stacked pieces only.

## The default path before / after (2026-11-24)

Raw output: [default-ab-2026-11-24.txt](default-ab-2026-11-24.txt).
- "before" is master at 7678782.
- "after" is the same tree with this change.
- Each value is the median of 21 runs. Four rounds were run in alternation.

| input | op | before, 4 rounds (ms) | after, 4 rounds (ms) | median change | allocated before (MB) | allocated after (MB) |
|---|---|---|---|---|---|---|
| spheres n=16 | Union | 43.47 / 44.72 / 47.89 / 45.73 | 41.43 / 43.48 / 56.58 / 41.16 | −6.1 % | 11.69-11.78 | 11.71-11.80 |
| spheres n=16 | Intersection | 26.38 / 25.06 / 29.35 / 26.49 | 23.86 / 26.00 / 27.80 / 23.98 | −5.5 % | 9.97-10.04 | 9.96-10.05 |
| spheres n=16 | Difference | 26.26 / 24.32 / 26.19 / 24.14 | 24.37 / 25.13 / 22.92 / 22.49 | −6.4 % | 11.70-11.77 | 11.69-11.77 |
| spheres n=32 | Union | 56.83 / 56.95 / 59.23 / 57.35 | 60.60 / 63.24 / 60.69 / 62.36 | +7.7 % | 39.11-39.40 | 39.09-39.40 |
| spheres n=32 | Intersection | 47.06 / 48.66 / 50.43 / 50.68 | 45.03 / 49.01 / 46.64 / 51.88 | −3.5 % | 32.11-32.27 | 32.10-32.29 |
| spheres n=32 | Difference | 60.33 / 58.53 / 56.94 / 58.67 | 60.18 / 53.57 / 56.58 / 65.69 | −0.4 % | 38.94-39.22 | 38.98-39.29 |
| boxes, flush faces | Union | 0.90 / 0.84 / 0.79 / 1.33 | 0.89 / 1.19 / 0.87 / 1.16 | +17.8 % | 0.70-0.72 | 0.70 |
| boxes, flush faces | Intersection | 0.96 / 0.72 / 0.78 / 0.81 | 0.81 / 1.19 / 0.78 / 1.27 | +25.8 % | 0.68 | 0.68 |
| boxes, flush faces | Difference | 0.98 / 0.92 / 1.12 / 0.81 | 1.30 / 0.74 / 1.10 / 1.27 | +24.7 % | 0.69-0.70 | 0.69-0.70 |
| sum of medians | | 264.8 | 262.0 | −1.1 % | | |

**Reading.** The noise on this shared cloud machine is ±15 % per row between identical runs: in "before" alone, one
case ranged from 0.79 to 1.33 ms, and the sum of the medians moved by −1.1 % with the same code per piece. That is an order of magnitude above the 1 % rule, so this measurement can neither
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

## Uncut input-frame and plane-origin volume fast paths (2026-11-28, .NET 10.0.115, 2× Intel Xeon @ 2.30 GHz)

Paired same-machine run against `origin/master` (`2959dfaa`), command:

```
DOTNET_TieredPGO=0 dotnet run -c Release --no-build --project benchmarks/BooleanVolumeGradient -- gradient 11
```

| input | op | pieces | gradient before | gradient after | speedup | gradient / boolean (before → after) |
|---|---|---:|---:|---:|---:|---|
| spheres n=16 | Union | 6428 | 43.2 ms | 8.8 ms | 4.9× (−79.6 %) | 87.4 % → 18.3 % |
| spheres n=16 | Intersection | 3280 | 24.0 ms | 8.9 ms | 2.7× (−62.9 %) | 80.7 % → 30.4 % |
| spheres n=16 | Difference | 5228 | 41.6 ms | 8.0 ms | 5.2× (−80.8 %) | 95.6 % → 19.9 % |
| spheres n=32 | Union | 25196 | 189.7 ms | 24.2 ms | 7.8× (−87.2 %) | 157.3 % → 21.2 % |
| spheres n=32 | Intersection | 10932 | 106.5 ms | 18.0 ms | 5.9× (−83.1 %) | 110.8 % → 19.1 % |
| spheres n=32 | Difference | 19578 | 190.2 ms | 24.9 ms | 7.6× (−86.9 %) | 154.4 % → 20.4 % |
| boxes, flush faces | Union / Int / Diff | 58–68 | 0.2–0.3 ms | 0.2 ms | ~1.5× | 22.7–25.5 % → 15.7–17.1 % |

What changed:
- **Uncut kept pieces (85–92 % of kept pieces on `spheres n=32`):** when an output piece is a cyclic permutation of its
  source input triangle's corners, its exact centroid barycentric coordinates are $(1/3, 1/3, 1/3)$ and its exact edge
  differences use Knuth's `TwoDiff` in IEEE 754 binary64 (with `ExactInterval` outward rounding and `BigInteger` fallback
  outside `2^1020` or at subnormals). Each corner receives $s_P \frac{1}{6}(V_1 - V_0) \times (V_2 - V_0)$ directly without
  allocating a `Tri` or touching `BigInteger`.
- **Result-vertex volume origin:** Pass 1 picks an actual input vertex $O$ of a kept piece and bounds `E_vol` over every
  kept piece's vertex ($V_0$ for uncut pieces, `ot.A` for cut pieces) relative to $O$, using `TwoDiff` for input vertices
  and cached exact points only for constructed cut vertices. Far-from-origin offsets (e.g. UTM) cancel and large clipping
  triangles do not inflate `E_vol` or `VolumeBound` (with `BigInteger` fallback outside the `TwoDiff` range or when no
  kept piece has an input vertex at `ot.A`).
- **Cut pieces & scaling:** input-to-input differences in `tri()`/`diff()` reuse `TwoDiff`, `enclose()` fast-paths exact
  $\le 53$-bit integers, and `scale()` fast-paths strictly-normal ($> 2^{-1022}$) power-of-two multiplies. `ExactMeshBoolean` is untouched.

