# SolidifyBenchmark

Time and result hash of `MeshWindingNumberSolidify` (adaptive dual contouring, the default path of
`MeshRobustRepair`) on procedural inputs (no mesh files). A speedup of this operation must give the same mesh bit for
bit, so each row carries a hash of the result (vertex coordinates as raw bits, triangles in index order); a run whose
repeats differ stops with an error, and two builds are compared by their hash columns.

```
dotnet run -c Release --project benchmarks/SolidifyBenchmark [repeats]
```

The first run of a case is cold (JIT, caches) and is reported separately; the median of the others is the warm time.
Linux container, 4 cores, `net10.0`, Release; timings vary by about 10 % between runs.

Cases: the 12 thin overlapping plates of `MeshRobustRepairTests.ThinPlates(6)` (144 triangles, about one cell thick at
resolution 64; the input of about 12 slow tests), and the two boxes with a gap narrower than a cell of
`HiddenGapRefinementTests` (24 triangles, three refinement levels, results of 200k triangles).

## Octree refinement with triangle lists (2026-11-10)

`WindingNumberOctree.refine` asked the AABB tree of the whole input, for every octree node, whether a triangle
overlaps the node. A child now tests only the triangles that overlap its parent (a triangle that misses the parent's
enlarged box cannot touch a child's; the enlarged child lies inside the enlarged parent with a margin of 5e-7 of the
parent's size). Same test, same answers: the hashes are identical.

| case | hash (before = after) | warm ms before | warm ms after | octree.fine ms before | octree.fine ms after |
|---|---|---|---|---|---|
| thin plates, res 64 | `484DE4C999F07556` | 4490 | 4093 | 537 | 285 |
| thin plates, res 64, no reduce | `51347B5655F8B10A` | 3775 | 3522 | 529 | 221 |
| thin plates, res 64, stop on self-int. | `85660329FEAF2E5D` | 3562 | 3337 | 505 | 204 |
| thin plates (seed 3), res 48 | `F9A4ED526C365153` | 3548 | 3361 | 104 | 87 |
| gap 0.3 cells, 3 levels | `FCA445BA69F9AF43` | 2985 | 2977 | 291 | 315 |
| gap 0.45 cells, tilted 0.5 deg | `FE229999618AE111` | 3069 | 2982 | 288 | 186 |
| sphere, res 40 | `8A2D9E9605205A17` | 40 | 42 | 0 | 0 |

Where the rest of a 4.5 s solidify of the plates goes (`PhaseMilliseconds`, one run): contour 3.5 s (octree 0.6,
crossings 0.6, hidden-gap ray tests 0.8, hermite data 0.3, vertices 0.3, self-intersection resolve 0.9), cleanup 0.3,
reduce 0.7. The profile is flat: no single function above 6 % of the CPU time, and 2 GB are allocated per call.
