# HausdorffBenchmark

`MeshHausdorffDistance` with the default upper bound (the floating-point model of the distance queries) against
`CertifiedUpper` (#742, [HAUSDORFF_CERTIFIED_UPPER.md](../../docs/design/HAUSDORFF_CERTIFIED_UPPER.md)). Procedural
meshes, fixed seeds; the 18 cases of #738 (symmetric and one-sided) and a small triangle 1e-9 above a tilted one.

    dotnet run --project benchmarks/HausdorffBenchmark -c Release [-- <runs>]

The output is a markdown table: the default lower bound, the upper bound and the subdivisions (default → certified), and
the median time of `runs` (default 7) after a warm-up. Parallel (`gParallel`), container with 4 cores; run-to-run noise
about ±30 %.

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
