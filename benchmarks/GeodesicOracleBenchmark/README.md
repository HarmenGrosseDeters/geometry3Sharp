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

**Exact trim (issue #1186).** The dominance test of the pruning now finds the dominated part of an overlap exactly (GEODESICS.md A.7 note 11) instead of
bisecting to depth 6. Same machine, one process alone, default budget except the 141 row (`G3_MAXW=60000000`, so that it finishes). Before: master at
`1c52b293`; after: this change. Distances differ by at most 8.9e-16 absolute (4.3e-16 relative) over 39 runs, and the plane rows are digest-equal.

| mesh | status | before: seconds, windows (per vertex) | after: seconds, windows (per vertex) |
|---|---|---|---|
| terrain 101 x 101 | Ok | 0.66, 123453 (12.1) | 0.50, 112758 (11.1) |
| plane 101 x 101 | Ok | 0.69, 77422 (7.6) | 0.78, 77422 (7.6) |
| ridge75 101 x 101 | Ok | 0.54, 57993 (5.7) | 0.57, 57969 (5.7) |
| saddle 101 x 101 | WindowLimit | 2.61, 652865 (64.0) | 0.92, 652865 (64.0) |
| terrain 201 x 201 | Ok | 1.27, 506347 (12.5) | 1.06, 461974 (11.4) |
| plane 201 x 201 | Ok | 2.36, 314820 (7.8) | 2.30, 314820 (7.8) |
| ridge75 201 x 201 | Ok | 2.36, 235926 (5.8) | 2.31, 235852 (5.8) |
| saddle 201 x 201 | WindowLimit | 14.00, 2585665 (64.0) | 4.30, 2585665 (64.0) |
| terrain 317 x 317 | Ok | 3.23, 1273983 (12.7) | 2.65, 1162603 (11.6) |
| plane 317 x 317 | Ok | 6.30, 790653 (7.9) | 6.12, 790653 (7.9) |
| ridge75 317 x 317 | Ok | 6.19, 592678 (5.9) | 6.16, 592546 (5.9) |
| saddle 317 x 317 | WindowLimit | 42.07, 6431297 (64.0) | 13.37, 6431297 (64.0) |
| saddle 141 x 141, budget 60M | Ok | 20.06, 3718007 (187.0) | 3.94, 2394554 (120.4) |

The saddle still ends in `WindowLimit` at the default budget, by design: at the end of a run on the saddle 99 % to 100 % of the live windows are the
minimum of their edge somewhere (31, 48 and 82 essential windows per vertex at 41, 61 and 101 vertices a side), so no filter that drops only dominated
windows can bring the count below about 0.8 to 0.9 per vertex and grid width (64 per vertex at about 78 x 78 vertices instead of about 62 x 62). The run to the limit
at 317 x 317 takes 13 s instead of 42 s, under the 20 s rule. A saddle mesh beyond about 78 x 78 vertices needs an explicit `MaxWindows`.

The saddle ends in `WindowLimit` at the default of 64 windows per vertex at all three sizes (its windows per vertex grow with the grid width:
62, 91 and 119 at 61, 81 and 101 vertices a side with a budget of 30 million windows, 5.3 s at 101 x 101): a documented v1 limit
(A.7, no ICH or VTP pruning). The `WindowLimit` run at 317 x 317 takes 41 s before it gives up; the check of the prune costs it about
10 % (37.6 s to 41.6 s).

**Exact paths (`TracePath`, GEODESICS.md A.8).** `dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- path 101,201,317` (`G3_MAXW` for the budget): one Compute from the centre, then
`TracePath` to about 2000 vertices spread over the mesh. The record that makes it possible costs 32 bytes per vertex during Compute and two stores per improvement of a vertex:
Compute at 317 x 317, three runs each, before / after: plane 6.37, 6.48, 6.51 s / 6.42, 6.61, 6.44 s (digest equal), terrain 2.82, 2.84, 2.95 s / 2.79, 3.04, 2.99 s (digest equal),
saddle (default budget, WindowLimit) 14.2 s / 13.8 s; peak working set plane 261 to 262 MB / 264 to 265 MB, terrain 289 MB / 292 MB, saddle 631 MB / 634 MB (32 bytes x 100489 vertices = 3 MB).

| mesh | vertices | Compute seconds | traces | trace seconds (total) | microseconds per trace | longest path (points) | largest abs(Length - Distance) / Distance |
|---|---|---|---|---|---|---|---|
| terrain 101 x 101 | 10201 | 0.57 | 2041 | 0.044 | 22 | 120 | 1.1e-15 |
| plane 101 x 101 | 10201 | 0.72 | 2041 | 0.035 | 17 | 143 | 3.9e-15 |
| saddle 101 x 101 (budget 30M) | 10201 | 1.21 | 2041 | 0.021 | 10 | 144 | 2.5e-15 |
| terrain 201 x 201 | 40401 | 1.07 | 2021 | 0.030 | 15 | 235 | 1.6e-15 |
| plane 201 x 201 | 40401 | 2.53 | 2021 | 0.051 | 25 | 275 | 5.9e-15 |
| saddle 201 x 201 (budget 30M) | 40401 | 14.87 | 2021 | 0.044 | 22 | 278 | 3.4e-15 |
| terrain 317 x 317 | 100489 | 2.84 | 2010 | 0.113 | 56 | 359 | 1.9e-15 |
| plane 317 x 317 | 100489 | 6.16 | 2010 | 0.115 | 57 | 469 | 6.0e-15 |

A trace costs 10 to 60 microseconds, a thousandth of the Compute. The deviation grows with the number of triangles crossed (plane: about 1e-17 per crossing).
