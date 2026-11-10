# NotCleanBenchmark

Time of the checks behind `MeshRobustRepair.NotCleanReason` on big procedural meshes (no mesh files): a 200k-triangle
sphere, the 185k-triangle alpha wrap of the thin plates of `MeshRobustRepairTests.ThinPlates(6)`, and two shifted copies of
that wrap (20 293 real self-intersecting pairs). Per mesh: the verdict, the number and a hash of the self-intersection
pair set (it must not change between builds), and the median time of the checks.

```
dotnet run -c Release --project benchmarks/NotCleanBenchmark [repeats]
```

Linux container, 4 cores, `net10.0`, Release; the timings vary by about 10-20 % between runs.

## Lever 1: the cheap pair filter in the self-intersection search (2026-11-10)

`MeshRepairMetrics.SelfIntersectionPairs` / `HasSelfIntersections` called the exact `IntersectsBeyondShared` for every
pair of triangles whose boxes overlap. They now call `Remesher.pair_intersects`, which first runs `TriangleTriangleFilter`
(decides pairs that a projection onto an axis plane settles with `Orient2d` only) and, for neighbours sharing one vertex,
an exact plane test, and falls back to the same exact predicate. It rejects only where the exact predicate says no:
`SelfIntersectionPairsTests` compares the pair sets with a brute force over all pairs (procedural cases with coplanar,
touching and shared-edge pairs, lattice soups with zero-area triangles, similarities) and
`SelfIntersectionPairsProperty` (CsCheck) does so on random lattice soups.

| mesh | verdict | pairs (hash, before = after) | all-pairs search ms before / after | HasSelfIntersections ms before / after | NotCleanReason ms before / after |
|---|---|---|---|---|---|
| sphere, 200k triangles | "" | 0 (`DF3F619804A9`) | 264 / 178 | 273 / 164 | 326 / 203 |
| alpha wrap, 185k triangles | "" | 0 (`DF3F619804A9`) | 405 / 283 | 400 / 287 | 536 / 443 |
| two shifted wraps | "self-intersections" | 20293 (`39ED0B0AC51D`) | 1060 / 813 | 181 / 191 | 487 / 469 |

The other checks of `NotCleanReason` (closed, bowties, degenerate triangles, orientation, inside-out components) take
130-340 ms on the wrap meshes, about a quarter of the total.
