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

## The library API (stage 2): `MeshBatchQueries` against a loop of single calls

`dotnet run --project benchmarks/BatchQueryBenchmark -c Release -- 256 40 30,92,290 api` (raw: `results-api-2026-11-09.csv`),
4-core container, one run, median of 3 rounds, about ±20 % noise. "Before" is the loop of single calls (`FindNearestHitTriangle` +
`MeshQueries.TriangleIntersection(...).RayParameter`; `FindNearestTriangle` + `MeshQueries.TriangleDistance`), "after" is
`MeshBatchQueries.IntersectBatch` / `NearestPointBatch` with default options (parallel, sorted) and serial. Speedups over the loop; the
`check` column of the CSV compares every result bit for bit with the loop (triangle, ray parameter, point, barycentrics, distance):
`equal` in every row.

| triangles | query | workload | loop ns | serial, unsorted | serial, sorted | parallel, sorted |
|---:|---|---|---:|---:|---:|---:|
| 10092 | IntersectBatch | camera | 1947 | 1.07x | 0.95x | 2.55x |
| 10092 | IntersectBatch | camera-shuffled | 2376 | 1.06x | 1.27x | 3.61x |
| 10092 | IntersectBatch | random | 3757 | 1.04x | 1.04x | 3.01x |
| 10092 | NearestPointBatch | grid | 8174 | 1.09x | 1.23x | 3.83x |
| 10092 | NearestPointBatch | grid-shuffled | 9002 | 1.04x | 1.32x | 4.26x |
| 10092 | NearestPointBatch | random | 8996 | 1.07x | 1.36x | 3.98x |
| 99372 | IntersectBatch | camera | 2501 | 1.21x | 1.19x | 3.60x |
| 99372 | IntersectBatch | camera-shuffled | 2876 | 1.05x | 1.30x | 4.11x |
| 99372 | IntersectBatch | random | 4863 | 0.94x | 1.12x | 3.18x |
| 99372 | NearestPointBatch | grid | 22782 | 1.02x | 1.08x | 3.59x |
| 99372 | NearestPointBatch | grid-shuffled | 27177 | 0.99x | 1.29x | 4.45x |
| 99372 | NearestPointBatch | random | 28390 | 1.01x | 1.31x | 4.54x |
| 1002252 | IntersectBatch | camera | 2735 | 1.08x | 1.06x | 3.16x |
| 1002252 | IntersectBatch | camera-shuffled | 4247 | 1.00x | 1.47x | 4.59x |
| 1002252 | IntersectBatch | random | 8206 | 1.05x | 1.24x | 4.14x |
| 1002252 | NearestPointBatch | grid | 78989 | 0.99x | 1.10x | 3.79x |
| 1002252 | NearestPointBatch | grid-shuffled | 113838 | 1.02x | 1.60x | 5.46x |
| 1002252 | NearestPointBatch | random | 114737 | 1.01x | 1.57x | 5.73x |
