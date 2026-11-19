# GeodesicOracleBenchmark

Width, time and window count of `ExactMeshGeodesicDistance` (modes `reach` and `sweep`; the mode `poly` is at the end), the certified enclosure [Lower, Upper] of the polyhedral
geodesic distance (internal; design: [GEODESICS.md](../../docs/design/GEODESICS.md) sections 6b and 6c, issue #1058).
No mesh files: procedural grids with the alternating diagonals of the tests.

```
dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- reach    # the reach meshes of GEODESICS.md 6b
dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- sweep    # domes and saddles over a curvature sweep
dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- poly 101,201,317   # the public PolyhedralGeodesicDistance, no oracle
```

Each row is one `Compute` from one source: its status, wall seconds, windows created and the largest
(Upper - Lower) / Lower over the other vertices (Lower is certified and tight, so this bounds the gap of Upper to the
distance).

Results of 2026-11-17 (a cloud container, single runs, .NET 10), before and after the A6 rule of #1058 (merge two
windows only when at most one has a nonempty inner interval):

| mesh | vertices | before: seconds, windows, width | after: seconds, windows, width |
|---|---|---|---|
| plane 41 x 41, centre | 1681 | 5.49, 126803, 8.0e-2 | 2.95, 59229, 9.5e-9 |
| terrain 41 x 41, centre | 1681 | 0.44, 18835, 5.9e-9 | 0.45, 18835, 5.9e-9 |
| saddle 0.3 (x^2 - y^2) 41 x 41, centre | 1681 | 2.14, 63649, 9.5e-9 | 2.09, 63649, 9.5e-9 |
| plane 101 x 101, centre | 10201 | window limit (12.4 s) | window limit (16.1 s) |
| terrain 101 x 101, centre | 10201 | 2.59, 125155, 1.4e-8 | 2.89, 125155, 1.4e-8 |
| saddle 0.3 (x^2 - y^2) 101 x 101, centre | 10201 | window limit (14.1 s) | window limit (13.6 s) |
| aligned ridge 21 x 21 | 441 | 0.66, 19594, 1.7e-1 | 0.33, 10588, 8.0e-9 |
| aligned ridge 31 x 31 | 961 | 2.82, 77247, 8.0e-2 | 1.48, 39852, 1.4e-8 |
| aligned ridge 41 x 41 | 1681 | 13.89, 286137, 3.8e-2 | 5.65, 97650, 1.9e-8 |

The sweep (domes -k (x^2 + y^2) and saddles k (x^2 - y^2) over [-1, 1]^2, 11, 21 and 41 vertices a side, centre, edge
and corner sources) is summarised per surface class in GEODESICS.md 6c: before, 0.5-8.2% on flat and nearly flat input
(k <= 1e-5) and 1.9e-8 on curved input; after, at most 1.9e-8 on all of them, with 2-7 times fewer windows on the flat
and nearly flat ones and the same windows on the curved ones. Before, the dome with k = 1e-5 (41 x 41, centre and edge
sources) ran 23-35 s into the window limit; after, 0.2-0.4 s.
Single runs on a shared container: read the windows and the ratios, not the milliseconds.

## PolyhedralGeodesicDistance (mode `poly`)

The public plain-double class of GEODESICS.md 6e (A.7): one `Compute` from the centre vertex (the ridges: the vertex 3 columns
from the left edge of the middle row), time, windows, bytes allocated and the peak working set of the process, no oracle. Families:
`terrain` (the test terrain), `plane`, `ridge4` (the exactly flat slope-4 ridge), `ridge75`, `saddle` (0.3 (x^2 - y^2)). Grids of
101, 201 and 317 vertices a side; the rows are single runs of 2026-11-19 (.NET 10, a 4-core cloud container, one process alone on
the machine, so read the ratios). The result digest is a hash over the bits of `Distance`, `NearestSource` and the window count:
"digest equal" says the run after is bit for bit the run before.

Before: the first working version (a debug log in `Add` that grew a string by concatenation, so 6 GB were allocated at 317 x 317, and a
dominance test that recursed to depth 6 into every piece). After: the log is gone, and the recursion stops at a piece when
`FMax(a) <= FMin(b) + m` (no sub-piece can then pass the test, because every step of `Hyp` is monotone also in floating point). Targets
(6e): terrain 317 x 317 within 5 s and 1 GB, plane and the ridge within 10 s.

| mesh | vertices | status | windows (per vertex) | before: seconds, allocated MB, peak working set MB | after: seconds, allocated MB, peak working set MB | digest equal |
|---|---|---|---|---|---|---|
| terrain 101 x 101 | 10201 | Ok | 123453 (12.1) | 0.69, 27, 70 | 0.50, 27, 73 | yes |
| plane 101 x 101 | 10201 | Ok | 77422 (7.6) | 1.19, 32, 101 | 0.74, 27, 87 | yes |
| ridge4 101 x 101 | 10201 | Ok | 58000 (5.7) | 1.04, 27, 125 | 0.68, 22, 108 | yes |
| ridge75 101 x 101 | 10201 | Ok | 57993 (5.7) | 1.02, 27, 141 | 0.58, 22, 124 | yes |
| saddle 101 x 101 | 10201 | WindowLimit | 652865 (64.0) | 2.35, 59, 181 | 2.63, 59, 164 | yes |
| terrain 201 x 201 | 40401 | Ok | 506347 (12.5) | 1.33, 99, 235 | 1.25, 99, 253 | yes |
| plane 201 x 201 | 40401 | Ok | 314820 (7.8) | 6.54, 400, 386 | 2.35, 96, 279 | yes |
| ridge4 201 x 201 | 40401 | Ok | 235936 (5.8) | 6.44, 416, 386 | 2.31, 91, 316 | yes |
| ridge75 201 x 201 | 40401 | Ok | 235926 (5.8) | 6.67, 417, 386 | 2.11, 91, 337 | yes |
| saddle 201 x 201 | 40401 | WindowLimit | 2585665 (64.0) | 11.29, 235, 386 | 13.27, 235, 407 | yes |
| terrain 317 x 317 | 100489 | Ok | 1273983 (12.7) | 3.23, 249, 484 | 3.33, 248, 469 | yes |
| plane 317 x 317 | 100489 | Ok | 790653 (7.9) | 24.46, 6236, 963 | 6.12, 242, 527 | yes |
| ridge4 317 x 317 | 100489 | Ok | 592690 (5.9) | 23.93, 5481, 963 | 5.93, 228, 545 | yes |
| ridge75 317 x 317 | 100489 | Ok | 592678 (5.9) | 24.10, 5515, 963 | 5.73, 228, 568 | yes |
| saddle 317 x 317 | 100489 | WindowLimit | 6431297 (64.0) | 37.55, 579, 963 | 41.56, 579, 656 | yes |

The saddle ends in `WindowLimit` at the default of 64 windows per vertex at all three sizes (its windows per vertex grow with the grid width:
62, 91 and 119 at 61, 81 and 101 vertices a side with a budget of 30 million windows, 5.3 s at 101 x 101): a documented v1 limit
(A.7, no ICH or VTP pruning). The `WindowLimit` run at 317 x 317 takes 41 s before it gives up; the check of the prune costs it about
10 % (37.6 s to 41.6 s).
