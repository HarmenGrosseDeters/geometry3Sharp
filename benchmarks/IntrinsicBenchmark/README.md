# IntrinsicBenchmark

Benchmark of the intrinsic triangulations (`IntrinsicTriangulation`, `MeshGeodesicDistance`, `VectorHeatMethod`)
on poor meshes. It compares three ways of computing the cotan Laplacian:
- **mesh triangles**: `IntrinsicDelaunay = false`
- **intrinsic flips**: the intrinsic Delaunay triangulation (`FlipToDelaunay`)
- **refined**: the flips plus intrinsic Delaunay refinement (`IntrinsicRefine`, `DelaunayRefine`), either with
  a minimum angle of 25° or with 0°, which only splits boundary edges opposite obtuse angles

It measures:
- the error of the heat method distance against the exact distance
- the error of the vector heat log map against the true offset
- the smallest angle of the triangulation
- the number of edges with a negative cotan weight
- the time, including building the triangulation

The meshes are generated in the code; no mesh files are used.

```
dotnet run -c Release --project benchmarks/IntrinsicBenchmark
```

## Meshes
| name | mesh |
|---|---|
| plane 169° | 20 x 20 plane, rows 0.05 apart and shifted by half a column (edge length 1): every triangle has a 169° angle; the boundary rows have boundary edges opposite these angles; the left and right sides zigzag with 11° corners (small input angles that no refinement can remove) |
| ring sphere | unit sphere from 159 latitude rings of 24 vertices, alternately shifted: nearly all triangles very obtuse |
| plane 169° 10 x 10 | the same construction, 10 x 10, for the log map |

Errors are absolute (plane: edge length 1; sphere: radius 1). The plane uses the vertices with 1 ≤ x ≤ 19
(away from the zigzag sides); the source is in the middle; for the log map 1 ≤ x, y ≤ 9.

## Results
Linux container, .NET 10, Release. Times: median of 3 runs after one untimed warm-up run (JIT), including
building the triangulation (and, for the log map, the factorizations); single runs for meshes above 50000
vertices. The container is shared, so times vary by some 20%.

### Quality (heat method distance, log map)
| mesh | method | vertices (+inserted) | min angle | negative weights | mean error | max error | time before | time after |
|---|---|---|---|---|---|---|---|---|
| plane 169° | mesh triangles | 8221 | 5.7° | 7820 | 2.1847 | 8.2633 | 1782 ms | 179 ms |
| plane 169° | intrinsic flips | 8221 | 5.7° | 40 | 0.0618 | 0.8057 | 695 ms | 140 ms |
| plane 169° | refined 0° (boundary only, now the default) | 8261 (+40) | 5.7° | 0 | 0.0490 | 0.2287 | 796 ms | 145 ms |
| plane 169° | refined 25° | 18471 (+10250) | 5.7° | 0 | 0.0199 | 0.1019 | 1804 ms | 385 ms |
| ring sphere | mesh triangles | 3818 | 8.5° | 3480 | 0.6540 | 2.7176 | 162 ms | 30 ms |
| ring sphere | intrinsic flips | 3818 | 14.7° | 0 | 0.0380 | 0.0629 | 136 ms | 78 ms |
| ring sphere | refined 0° (boundary only, now the default) | 3818 | 14.7° | 0 | 0.0380 | 0.0629 | 135 ms | 64 ms |
| ring sphere | refined 25° | 5087 (+1269) | 25.2° | 0 | 0.0263 | 0.0473 | 284 ms | 74 ms |

"Before" is `MeshGeodesicDistance` with its own flip code, Jacobi iterations for the heat and conjugate
gradients for the Poisson problem; "after" builds on `IntrinsicTriangulation` and solves both directly
(`SparseCholesky`, the heat in the log domain where it underflows). The errors are the same.

| mesh | method | log map mean error | log map max error | time |
|---|---|---|---|---|
| plane 169° 10 x 10 | flips | 0.1064 | 0.8136 | 50 ms |
| plane 169° 10 x 10 | refined 25° | 0.0442 | 0.3780 | 161 ms |

### Scaling (`dotnet run -c Release --project benchmarks/IntrinsicBenchmark -- --scaling`)
Flat grids with alternating diagonals (spacing 1, Delaunay: no flips, no boundary splits) and strips of 2 x len
unit squares, source in the grid center / at the strip end; errors against the Euclidean distance. "First
Compute" includes building the triangulation and the factorizations, "further" is a Compute with the same mesh.

| mesh | vertices | first Compute before | first Compute after | further Compute before | further Compute after | max error before | max error after |
|---|---|---|---|---|---|---|---|
| grid 51 x 51 | 2601 | 132 ms | 51 ms | 71 ms | 2 ms | 0.5137 | 0.5137 |
| grid 101 x 101 | 10201 | 140 ms | 142 ms | 175 ms | 13 ms | 0.6717 | 0.6716 |
| grid 201 x 201 | 40401 | 1138 ms | 830 ms | 977 ms | 95 ms | 1.2846 | 1.2840 |
| grid 401 x 401 | 160801 | 7516 ms | 5992 ms | 6585 ms | 403 ms | 2.5211 | 2.5157 |
| strip 1000 x 2 | 3003 | 612 ms | 20 ms | 561 ms | 3 ms | 1.0840 | 0.3120 |
| strip 5000 x 2 | 15003 | 9799 ms | 256 ms | 9844 ms | 62 ms | 101.8779 | 0.3120 |
| strip 25000 x 2 | 75003 | 76915 ms | 2325 ms | 77560 ms | 379 ms | failed (Compute false) | 0.3120 |

The default boundary split costs nothing measurable on these meshes (grid 401: 5839 ms with it, 5992 ms
without; it inserts no vertices there). The vector heat log map is unchanged by this rewrite (grid 51 / 101 /
201: 55 / 420 / 1859 ms, mean error 0.38 / 0.72 / 1.39).

### Findings
- **Flips cannot fix boundary edges.** They remove every negative weight inside the plane, but the 40 boundary edges opposite 169° angles keep theirs. Splitting only those edges (0°, 40 vertices, no extra time) brings the maximum distance error from 0.81 to 0.23. This is now the default of `MeshGeodesicDistance` (`SplitObtuseBoundaryEdges`).
- **Direct solves.** The Jacobi heat iteration needed more iterations the farther the heat had to travel and failed on long meshes (a strip of 5000 edge lengths: distances wrong by up to 102; 25000: Compute false after 77 s). The Cholesky factorization with the log-domain substitution is accurate at any distance, and every further source set costs only the substitutions (10 to 200 times faster than before).
- **Refining to 25° is the most accurate.** On the plane it lowers the mean error from 0.062 to 0.020 and the maximum from 0.81 to 0.10, and it lowers the log map error by more than half. The cost is that the vertex count more than doubles and the time goes from 0.6 to 1.5 s. The log map on the smaller plane costs about the same as with the flips alone.
- **The minimum angle on the plane stays at 5.7°.** That angle is at the 11° zigzag corners, whose triangles are skipped as small input angles. Every other triangle meets the 25° bound.
- **The closed ring sphere has no boundary,** so the 0° refinement changes nothing there. At 25° the minimum angle rises from 14.7° to 25.2° and the errors drop by about a third.
