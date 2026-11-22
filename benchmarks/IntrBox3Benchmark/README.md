# IntrBox3Benchmark

The line, ray and segment against box intersection classes (`IntrLine3Box3`, `IntrRay3Box3`, `IntrSegment3Box3`, `IntrLine3AxisAlignedBox3`,
`IntrRay3AxisAlignedBox3`, shared decision `intersection/IntrExactBox3.cs`) and two callers: the box tests of `DMeshAABBTree3` ray queries
and of `DCurve3BoxTree` ray queries.

    dotnet run -c Release          # with DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 for a stable number

## Result (22 November 2026, 4 cores, ns per call, best of 9; before = master, after = this change)

| Operation | before | after |
|---|---|---|
| `IntrRay3AxisAlignedBox3.FindRayIntersectT` (hot path of `DMeshAABBTree3`) | 26 | 35 |
| `IntrRay3AxisAlignedBox3.Intersects` | 15 | 36 |
| `IntrRay3Box3.Intersects` (oriented, expand 0.01) | 17-21 | 52 |
| `IntrRay3Box3.Find` | 83-105 | 260-350 |
| `IntrRay3AxisAlignedBox3.Find` | 57-60 | 90-100 |
| `DMeshAABBTree3.FindNearestHitTriangle` (266412 triangles) | 800-1200 | 900-1200 (within the run-to-run noise) |
| `DCurve3BoxTree.SquaredDistance(ray)` (4000 segments) | 34 us | 82 us |
| `DCurve3BoxTree.FindClosestRayIntersction` | 4.8 us | 13.6 us |

The decision is now exact (hits, misses and the Point/Segment type; no `1e-8` slack, touching counts); the ends of a contact are on the
face exactly (axis-aligned) or within a few ulp. The cost is the certified error bound of each slab (division, absolute errors for the
dot products of an oriented box) that the old code did not have.
