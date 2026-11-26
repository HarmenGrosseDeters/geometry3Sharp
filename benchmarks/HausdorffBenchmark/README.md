# HausdorffBenchmark

`MeshHausdorffDistance` with the default upper bound (the floating-point model of the distance queries) against
`CertifiedUpper` (#742, [HAUSDORFF_CERTIFIED_UPPER.md](../../docs/design/HAUSDORFF_CERTIFIED_UPPER.md)). Procedural
meshes, fixed seeds; the 18 cases of #738 (symmetric and one-sided) and a small triangle 1e-9 above a tilted one.

    dotnet run --project benchmarks/HausdorffBenchmark -c Release [-- <runs>]

The output is a markdown table: the default lower bound, the upper bound and the subdivisions (default → certified), and
the median time of `runs` (default 7) after a warm-up. Parallel (`gParallel`), container with 4 cores; run-to-run noise
about ±30 %.

## Results (2026-11-15, #873: closest-point distances)

The row `coplanar grids 16/17 tol1e-12` was added: two grids of the unit square in the same plane (exact distance 0), an
absolute tolerance below the former noise floor of about 2e-9. Both tables are the output of
`dotnet run --project benchmarks/HausdorffBenchmark -c Release -- 15`, before on master 71f4f2a (with this Program.cs), after
on the branch.

Before:

| case | sym | lower (default) | upper default → certified | subdivisions | ms, median of 15 | time |
|---|---|---|---|---|---|---|
| spheres6 | yes | 0.009999999999999981 | 0.010319291623947372 → 0.010319291623947104 | 2810 → 2810 | 36.7 → 61.0 | +66% |
| spheres6-vs-20 | yes | 0.02443602955128217 | 0.024705509692912363 → 0.02470550969291279 | 4673 → 4673 | 40.7 → 72.1 | +77% |
| bumpy5-6 | yes | 0.05413995782131512 | 0.054455310063195005 → 0.05445531006319568 | 968 → 968 | 8.2 → 16.3 | +98% |
| bumpy7-8 tol1e-5 | yes | 0.05004214202349481 | 0.05004214202349494 → 0.05004214202349525 | 1478 → 1478 | 15.5 → 23.6 | +52% |
| utm spheres | yes | 0.8556205520479356 | 0.8556667363169809 → 0.8556667380043088 | 3214 → 3214 | 18.9 → 43.6 | +130% |
| reduced 1/8 | yes | 0.041023172728091646 | 0.04125140265545543 → 0.04125140265545562 | 4141 → 4141 | 49.1 → 72.2 | +47% |
| reduced 1/8 of 21k | yes | 0.04097293020860836 | 0.040972930208608385 → 0.04097293020860872 | 5972 → 5972 | 112.5 → 164.7 | +46% |
| cube-vs-sphere | yes | 0.29383355057629235 | 0.29409059777682645 → 0.29409059777682706 | 198 → 198 | 2.9 → 5.5 | +92% |
| offset 1e3 spheres | yes | 0.010000000000041082 | 0.01031028875668214 → 0.010310288756775355 | 2751 → 2751 | 11.0 → 22.9 | +108% |
| coplanar grids 16/17 tol1e-12 (converged False → False) | yes | 0 | 2.2812661703395874E-09 → 2.2820460860347577E-09 | 20490 → 20709 | 107.3 → 297.8 | +178% |
| spheres6 | no | 0.009829867796839437 | 0.010170865384566623 → 0.01017086538456702 | 2317 → 2317 | 8.0 → 16.4 | +104% |
| spheres6-vs-20 | no | 0.02441104949658992 | 0.024705509692912363 → 0.02470550969291279 | 4511 → 4511 | 24.2 → 51.4 | +112% |
| bumpy5-6 | no | 0.05374415514127856 | 0.054024359525743734 → 0.0540243595257444 | 171 → 171 | 2.2 → 4.5 | +105% |
| bumpy7-8 tol1e-5 | no | 0.04691087535210992 | 0.04691653647555985 → 0.046916536475560176 | 1273 → 1273 | 7.9 → 16.3 | +107% |
| utm spheres | no | 0.8428150952509774 | 0.8556667363169809 → 0.8556667380043088 | 2213 → 2213 | 12.8 → 25.8 | +102% |
| reduced 1/8 | no | 0.036025301350576334 | 0.03612671655193498 → 0.03612671655193544 | 4100 → 4100 | 35.8 → 58.6 | +64% |
| reduced 1/8 of 21k | no | 0.03019444159266059 | 0.03039513833499402 → 0.030395138334994326 | 5971 → 5971 | 83.7 → 129.1 | +54% |
| cube-vs-sphere | no | 0.2938335505762922 | 0.29409059777682645 → 0.29409059777682706 | 198 → 198 | 2.6 → 4.7 | +84% |
| offset 1e3 spheres | no | 0.009829867796833836 | 0.010170865384685458 → 0.010170865384867365 | 2212 → 2212 | 9.2 → 18.3 | +99% |
| coplanar grids 16/17 tol1e-12 (converged False → False) | no | 0 | 2.2812650937430355E-09 → 2.2812650937430355E-09 | 10806 → 10943 | 63.9 → 158.8 | +149% |
| 1e-9 above a triangle | no | 1.0000000200142971E-09 | 7.450580596923828E-09 → 7.450580596923828E-09 | 0 → 0 | 0.1 → 0.1 | -1% |

After:

| case | sym | lower (default) | upper default → certified | subdivisions | ms, median of 15 | time |
|---|---|---|---|---|---|---|
| spheres6 | yes | 0.010000000000000014 | 0.01031808797228534 → 0.010318087972285648 | 2734 → 2734 | 57.8 → 58.1 | +1% |
| spheres6-vs-20 | yes | 0.02443602955128217 | 0.02470550969291235 → 0.02470550969291279 | 4675 → 4675 | 46.4 → 69.2 | +49% |
| bumpy5-6 | yes | 0.05413995782131512 | 0.054455310063195005 → 0.05445531006319568 | 961 → 961 | 8.7 → 16.4 | +89% |
| bumpy7-8 tol1e-5 | yes | 0.05004214202349481 | 0.05004214202349495 → 0.05004214202349525 | 1477 → 1477 | 13.2 → 23.9 | +81% |
| utm spheres | yes | 0.8556205520479356 | 0.8612461638904246 → 0.8612461655708228 | 3210 → 3210 | 23.2 → 47.5 | +105% |
| reduced 1/8 | yes | 0.04102317272809163 | 0.04125140265545551 → 0.04125140265545562 | 4139 → 4139 | 48.1 → 72.3 | +50% |
| reduced 1/8 of 21k | yes | 0.040972930208608344 | 0.040972930208608344 → 0.04097293020860872 | 5956 → 5956 | 126.9 → 175.8 | +39% |
| cube-vs-sphere | yes | 0.29383355057629235 | 0.29409059777682645 → 0.29409059777682706 | 193 → 193 | 3.3 → 5.6 | +72% |
| offset 1e3 spheres | yes | 0.010000000000041124 | 0.010328337246004638 → 0.010328337246007539 | 2798 → 2798 | 13.0 → 23.2 | +79% |
| coplanar grids 16/17 tol1e-12 | yes | 0 | 9.993264773955323E-13 → 9.99862540492195E-13 | 30107 → 30107 | 192.3 → 500.1 | +160% |
| spheres6 | no | 0.009829867796839545 | 0.010170865384566671 → 0.01017086538456702 | 2180 → 2180 | 8.4 → 17.9 | +113% |
| spheres6-vs-20 | no | 0.024411049496589916 | 0.02470550969291235 → 0.02470550969291279 | 4512 → 4512 | 28.8 → 56.2 | +95% |
| bumpy5-6 | no | 0.05374415514127856 | 0.05402435952574376 → 0.0540243595257444 | 164 → 164 | 2.4 → 4.9 | +104% |
| bumpy7-8 tol1e-5 | no | 0.04691087535210992 | 0.04691653647555981 → 0.046916536475560176 | 1274 → 1274 | 9.0 → 16.3 | +81% |
| utm spheres | no | 0.8428150945957448 | 0.8556667370395611 → 0.855666738337926 | 2190 → 2190 | 14.5 → 28.1 | +94% |
| reduced 1/8 | no | 0.036025301350576334 | 0.03612671655193499 → 0.03612671655193544 | 4100 → 4100 | 34.6 → 59.6 | +72% |
| reduced 1/8 of 21k | no | 0.03019444159266059 | 0.03039513833499406 → 0.030395138334994326 | 5956 → 5956 | 97.2 → 137.7 | +42% |
| cube-vs-sphere | no | 0.29383355057629235 | 0.29409059777682645 → 0.29409059777682706 | 193 → 193 | 3.3 → 5.8 | +74% |
| offset 1e3 spheres | no | 0.009829867796826154 | 0.010170865384696364 → 0.010170865384871575 | 2238 → 2238 | 9.2 → 18.3 | +100% |
| coplanar grids 16/17 tol1e-12 | no | 0 | 9.993023640787794E-13 → 9.995468359243444E-13 | 15897 → 15897 | 110.9 → 254.2 | +129% |
| 1e-9 above a triangle | no | 1.0000000200142971E-09 | 1.0000000445709749E-09 → 1.0000003922959487E-09 | 0 → 0 | 0.1 → 0.1 | +1% |

Summary:
- **Coplanar:** converges, upper bound 9.99e-13 (before 2.28e-9, not converged, with either option).
- **Ordinary rows:**
  - lower and upper bounds within ulps of the coordinates, or within the tolerance where the stopping point moved (utm
    spheres, symmetric: 0.8556667 → 0.8612462, both within the relative tolerance 1e-4 of the diagonal, about 0.017);
  - subdivisions within 6%;
  - time of the default about +5 to +15%, of `CertifiedUpper` about +0 to +8% (the full point-triangle distance with its
    closest point per visited triangle), within the noise of about ±30%.

## Results (2026-11-13, #742)

The table below is the output of `dotnet run --project benchmarks/HausdorffBenchmark -c Release -- 7`.

| case | sym | lower (default) | upper default → certified | subdivisions | ms, median of 7 | time |
|---|---|---|---|---|---|---|
| spheres6 | yes | 0.009999999999999981 | 0.010319291623947372 → 0.010319291623947104 | 2810 → 2810 | 53.9 → 90.8 | +68% |
| spheres6-vs-20 | yes | 0.02443602955128217 | 0.024705509692912363 → 0.02470550969291279 | 4673 → 4673 | 71.3 → 82.5 | +16% |
| bumpy5-6 | yes | 0.05413995782131512 | 0.054455310063195005 → 0.05445531006319568 | 968 → 968 | 9.5 → 18.7 | +97% |
| bumpy7-8 tol1e-5 | yes | 0.05004214202349481 | 0.05004214202349494 → 0.05004214202349525 | 1478 → 1478 | 13.6 → 26.4 | +94% |
| utm spheres | yes | 0.8556205520479356 | 0.8556667363169809 → 0.8556667380043088 | 3214 → 3214 | 23.7 → 46.2 | +95% |
| reduced 1/8 | yes | 0.041023172728091646 | 0.04125140265545543 → 0.04125140265545562 | 4141 → 4141 | 46.6 → 76.5 | +64% |
| reduced 1/8 of 21k | yes | 0.04097293020860836 | 0.040972930208608385 → 0.04097293020860872 | 5972 → 5972 | 133.9 → 187.2 | +40% |
| cube-vs-sphere | yes | 0.29383355057629235 | 0.29409059777682645 → 0.29409059777682706 | 198 → 198 | 3.1 → 6.2 | +99% |
| offset 1e3 spheres | yes | 0.010000000000041082 | 0.01031028875668214 → 0.010310288756775355 | 2751 → 2751 | 14.8 → 28.4 | +92% |
| spheres6 | no | 0.009829867796839437 | 0.010170865384566623 → 0.01017086538456702 | 2317 → 2317 | 9.3 → 19.1 | +105% |
| spheres6-vs-20 | no | 0.02441104949658992 | 0.024705509692912363 → 0.02470550969291279 | 4511 → 4511 | 31.9 → 63.1 | +98% |
| bumpy5-6 | no | 0.05374415514127856 | 0.054024359525743734 → 0.0540243595257444 | 171 → 171 | 2.3 → 4.9 | +114% |
| bumpy7-8 tol1e-5 | no | 0.04691087535210992 | 0.04691653647555985 → 0.046916536475560176 | 1273 → 1273 | 8.9 → 16.1 | +80% |
| utm spheres | no | 0.8428150952509774 | 0.8556667363169809 → 0.8556667380043088 | 2213 → 2213 | 14.8 → 28.8 | +94% |
| reduced 1/8 | no | 0.036025301350576334 | 0.03612671655193498 → 0.03612671655193544 | 4100 → 4100 | 31.9 → 59.7 | +88% |
| reduced 1/8 of 21k | no | 0.03019444159266059 | 0.03039513833499402 → 0.030395138334994326 | 5971 → 5971 | 113.6 → 144.2 | +27% |
| cube-vs-sphere | no | 0.2938335505762922 | 0.29409059777682645 → 0.29409059777682706 | 198 → 198 | 3.0 → 6.5 | +115% |
| offset 1e3 spheres | no | 0.009829867796833836 | 0.010170865384685458 → 0.010170865384867365 | 2212 → 2212 | 11.1 → 18.8 | +69% |
| 1e-9 above a triangle | no | 1.0000000200142971E-09 | 7.450580596923828E-09 → 7.450580596923828E-09 | 0 → 0 | 0.0 → 0.1 | +106% |

Summary:
- **Time:** +16 to +115%, typically about +90%.
- **Subdivisions:** the same.
- **Lower bounds:** the same.
- **Upper bounds:** within a few ulps of the coordinates of the default ones (mostly higher, lower where the default
  overshoots).

In the last row the default upper bound happens to hold. On such inputs it is below the exact distance in about 40% of
random cases (`HausdorffCertifiedUpperTests`).

## Patch bound upper-bound check (#848)

    dotnet run --project benchmarks/HausdorffBenchmark -c Release -- patch [<runs>]
    dotnet run --project benchmarks/HausdorffBenchmark -c Release -- predcost

`patch`: how many subdivisions and how much time would `CertifiedUpper` save if the patch bound could be used? A
one-sided branch and bound written out in `PatchOracle.cs`, with the certified cascade and the existing (uncertified)
patch bound as the oracle (internals by reflection), on reduced meshes against their originals and other procedural
inputs. `predcost`: nanoseconds of the sign of a 3x3 determinant by a filtered double evaluation, by intervals and by
`AdaptivePredicates.Orient2d`. Tables, reading and the recommendation:
[docs/design/CERTIFIED_PATCH_BOUND.md](../../docs/design/CERTIFIED_PATCH_BOUND.md) section 7.

## Certified patch bound, the library's own rows (#848)

    dotnet run --project benchmarks/HausdorffBenchmark -c Release -- certpatch [<runs>]

`MeshHausdorffDistance` with `CertifiedUpper`, one-sided A -> B, tolerance 1e-4 of the diagonal, the library's parallel run, without
and with the (internal) `PatchBound` option, which uses `TriangleDeviationBounds.certified_patch_bound` under `CertifiedUpper`.
Same cases as `patch`. Container, 4 cores, median of 5 runs (the table is section 10 of
[docs/design/CERTIFIED_PATCH_BOUND.md](../../docs/design/CERTIFIED_PATCH_BOUND.md)).

| case (tris A / B) | upper without -> with patch | subdivisions without -> with | ms without -> with | time |
|---|---|---|---|---|
| reduced 1/8 of 5k bumpy (1260 / 10092) | 0.0361267 -> 0.0361267 | 4100 -> 302 | 119 -> 109 | -8% |
| reduced 1/32 of 5k bumpy (314 / 10092) | 0.0583846 -> 0.0583846 | 1748 -> 312 | 38 -> 32 | -15% |
| reduced 1/8 of 21k bumpy (5220 / 41772) | 0.0303951 -> 0.0303951 | 5956 -> 2369 | 162 -> 153 | -6% |
| reduced 1/32 of 21k bumpy (1304 / 41772) | 0.0315312 -> 0.0315312 | 6560 -> 1090 | 152 -> 110 | -28% |
| reduced 1/16 of 21k sphere (2610 / 41772) | 0.00273712 -> 0.00239283 | 135483 -> 18 | 1583 -> 84 | -95% |
| spheres6 vs 20 (300 / 4332) | 0.0247055 -> 0.024436 | 4512 -> 376 | 69 -> 22 | -68% |
| bumpy5 -> bumpy6 (1452 / 588) | 0.0540244 -> 0.0540244 | 164 -> 17 | 5 -> 6 | +5% |
| coplanar grids 16 -> 17 (jitter .3) (512 / 578) | 0.000141212 -> 1.16264E-15 | 3550 -> 105 | 65 -> 9 | -87% |

## `CertifiedPointMeshDistance` (`cpm` mode, #1649)

Certified hardware-double box (`CPM-1`) and supporting-plane (`CPM-2`) filters in `queries/CertifiedPointMeshDistance.cs`,
64 seeded queries per case:

    DOTNET_TieredPGO=0 dotnet run -c Release --project benchmarks/HausdorffBenchmark -- cpm 5

Before (`master` `04391d3b`) vs after (`perf/certified-point-mesh-distance-filter`), `.NET 10.0.15`, X64, median of 5 runs
after warm-up; all four 64-bit FNV-1a result digests are bit-for-bit identical:

| case | `LowerBound` before → after (ms) | speedup | `AtLeast(0.5*lb)` before → after (ms) | speedup | `AtLeast(1.5*lb)` before → after (ms) | speedup | digest |
|---|---:|---:|---:|---:|---:|---:|---|
| sphere 12 (1,728 tris) | 61.32 → 66.89 | 0.92× | 932.19 → 7.17 | **130.0×** | 880.62 → 1.30 | **677.4×** | `59D6DA696B8AE504` |
| sphere 24 (6,912 tris) | 213.89 → 104.05 | **2.06×** | 3999.85 → 30.83 | **129.7×** | 3874.50 → 3.38 | **1,146×** | `D19322B662AEB970` |
| bumpy 20 (4,800 tris) | 134.33 → 64.31 | **2.09×** | 2674.71 → 19.53 | **137.0×** | 2639.62 → 2.62 | **1,007×** | `1F0D8F6086D7E632` |
| utm sphere 12 (1,728 tris) | 58.79 → 39.23 | **1.50×** | 885.38 → 6.56 | **135.0×** | 905.11 → 1.40 | **646.5×** | `6EC0DB4DF5EF23F1` |

*(On `LowerBound`, the four cases range from 0.92× on `sphere 12` to 1.50–2.09× on `utm sphere 12`, `sphere 24`, and `bumpy 20`. Most of the `AtLeastDistance` gain comes from pruning non-candidate triangles via `CPM-1`/`CPM-2` combined with the immediate early return once a qualifying triangle is found.)*

