# MeshMeshCutContactBenchmark

Cost of `MeshMeshCut.RefuseContact` (issue 856): the exact look for contact (Orient3d / Orient2d on the triangle pairs the AABB tree search reports)
that runs before the cut. A sphere (12 (n-1)^2 triangles) is cut by a second, offset sphere: a transversal cut, so the look finds nothing and the
cut runs. `RefuseContact = false` is the code path of the base library (no look, the cut builds its own tree); `true` builds the tree of the cut mesh
once, looks, and hands the tree to the cut (one tree, not two).

```
dotnet run --project benchmarks/MeshMeshCutContactBenchmark -c Release -f net10.0 -- <n> <runs>
```

Measured in a shared four-core container (net10.0, 21 runs each; the machine is noisy, so the minimum is the better figure, the median is shown too):

| target and cut mesh | RefuseContact | Compute() median (ms) | min (ms) |
|---|---|---|---|
| 4 332 triangles each | false (base path) | 17.1 | 16.0 |
| | true | 16.3 | 13.9 |
| 18 252 triangles each | false (base path) | 46.8 | 23.2 |
| | true | 34.8 | 21.4 |
| 74 892 triangles each | false (base path) | 67.3 | 49.8 |
| | true | 83.5 | 70.5 |

Reading: on meshes up to 20 000 triangles the look is inside the noise (the tree it builds is the one the cut would build); at 75 000 triangles it adds
about 16 to 20 ms (25 to 40 %) to a cut of 50 to 67 ms. The default is `false`, so the normal path pays nothing.
