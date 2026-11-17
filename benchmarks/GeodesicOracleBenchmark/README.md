# GeodesicOracleBenchmark

Width, time and window count of `ExactMeshGeodesicDistance`, the certified enclosure [Lower, Upper] of the polyhedral
geodesic distance (internal; design: [GEODESICS.md](../../docs/design/GEODESICS.md) sections 6b and 6c, issue #1058).
No mesh files: procedural grids with the alternating diagonals of the tests.

```
dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- reach    # the reach meshes of GEODESICS.md 6b
dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- sweep    # domes and saddles over a curvature sweep
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
