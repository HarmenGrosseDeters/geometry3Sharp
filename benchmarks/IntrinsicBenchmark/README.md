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
building the triangulation (and, for the log map, the factorizations).

| mesh | method | vertices (+inserted) | min angle | negative weights | mean error | max error | time |
|---|---|---|---|---|---|---|---|
| plane 169° | mesh triangles | 8221 | 5.7° | 7820 | 2.1847 | 8.2633 | 1349 ms |
| plane 169° | intrinsic flips | 8221 | 5.7° | 40 | 0.0618 | 0.8057 | 600 ms |
| plane 169° | refined 0° (boundary only) | 8261 (+40) | 5.7° | 0 | 0.0490 | 0.2287 | 612 ms |
| plane 169° | refined 25° | 18471 (+10250) | 5.7° | 0 | 0.0199 | 0.1019 | 1512 ms |
| ring sphere | mesh triangles | 3818 | 8.5° | 3480 | 0.6540 | 2.7176 | 129 ms |
| ring sphere | intrinsic flips | 3818 | 14.7° | 0 | 0.0380 | 0.0629 | 120 ms |
| ring sphere | refined 0° (boundary only) | 3818 | 14.7° | 0 | 0.0380 | 0.0629 | 104 ms |
| ring sphere | refined 25° | 5087 (+1269) | 25.2° | 0 | 0.0263 | 0.0473 | 231 ms |

| mesh | method | log map mean error | log map max error | time |
|---|---|---|---|---|
| plane 169° 10 x 10 | flips | 0.1064 | 0.8136 | 120 ms |
| plane 169° 10 x 10 | refined 25° | 0.0442 | 0.3780 | 128 ms |

The minimum angle and the negative weights are those of the same kind of triangulation built by
`IntrinsicTriangulation`; for the flips row `MeshGeodesicDistance` flips with its own code (which also ends in an
intrinsic Delaunay triangulation).

### Findings
- **Flips cannot fix boundary edges.** They remove every negative weight inside the plane, but the 40 boundary edges opposite 169° angles keep theirs. Splitting only those edges (0°, 40 vertices, no extra time) brings the maximum distance error from 0.81 to 0.23.
- **Refining to 25° is the most accurate.** On the plane it lowers the mean error from 0.062 to 0.020 and the maximum from 0.81 to 0.10, and it lowers the log map error by more than half. The cost is that the vertex count more than doubles and the time goes from 0.6 to 1.5 s. The log map on the smaller plane costs about the same as with the flips alone.
- **The minimum angle on the plane stays at 5.7°.** That angle is at the 11° zigzag corners, whose triangles are skipped as small input angles. Every other triangle meets the 25° bound.
- **The closed ring sphere has no boundary,** so the 0° refinement changes nothing there. At 25° the minimum angle rises from 14.7° to 25.2° and the errors drop by about a third.
