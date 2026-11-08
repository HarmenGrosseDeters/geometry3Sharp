# BatchQueryBenchmark

Upper bound for batch ray and point queries ([BATCH_QUERIES.md](../../docs/design/BATCH_QUERIES.md)). Prototype
only: no library code changed, public API of g3 only (`DMeshAABBTree3`, `IntrRay3Triangle3`, `ExactPredicates`,
`gParallel`). Procedural sphere meshes (`Sphere3Generator_NormalizedCube`, about 10k / 100k / 1M triangles), fixed seeds.

    dotnet run --project benchmarks/BatchQueryBenchmark -c Release [-- <ray side> <point side> <edge vertices,...>]

The defaults of the committed run were `256 40` (65 536 rays, 64 000 points) on the three sizes (`30,92,290`).
Output is CSV (`results-2026-11-08.csv`: median of 3 rounds after a warm-up, ns per query, container with 4 cores,
single run, expect about ±20 % noise).

Modes:

| mode | what |
|---|---|
| `a-loop-library` | today: `tree.FindNearestHitTriangle` / `tree.IsInside` per call in input order (baseline) |
| `b1-library-sorted-serial` / `-parallel` | batching over the existing `DMeshAABBTree3`, public API only: Morton-sort the queries (6D for rays, 3D for points), run the same single-call query in that order, write by index; parallel with `gParallel.BlockStartEnd` |
| `b2-ownbvh-*` | a separate flat BVH of this benchmark (Morton-ordered triangles, leaves of 4 as SoA, reused stack, ray second point once per ray). A certified-miss filter drops triangles; every other triangle goes to the library's exact `IntrRay3Triangle3.Intersects`, so the hit set is the library's. `-scalar` / `-simd`: the filter per lane or as `Vector256<double>` over the 4 triangles of a leaf (no FMA) |
| `sort-only` | cost of computing the keys and sorting (included in every b mode) |
| `FastWindingNumber>0.5` | library fast winding number, loop vs parallel |
| `NearestPoint` | (`-- 256 40 30,92,290 nearest`) `FindNearestTriangle` + `MeshQueries.TriangleDistance` per point: loop vs b1 sorted serial / parallel, compared bit for bit (triangle, point, distance) |

The `check` column compares with the baseline bit for bit (hit triangle ids; inside flags): `equal`, plus
`par=ser` (parallel equals serial) and `simd=scalar`. Every row of the committed run is equal.

Not certified, timing only: the ray-box padding (1e-9 of the box size) and the filter bound (the library's
`Orient3d` bound without its underflow terms). IsInside in b2 counts crossings in general position only (no symbolic
perturbation), which is exact for the generic points used here. In a library version both would be derived and reviewed.
