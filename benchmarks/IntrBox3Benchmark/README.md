# IntrBox3Benchmark

The line, ray and segment against box intersection classes (`IntrLine3Box3`, `IntrRay3Box3`, `IntrSegment3Box3`, `IntrLine3AxisAlignedBox3`,
`IntrRay3AxisAlignedBox3`, shared decision `intersection/IntrExactBox3.cs`) and two callers: the box tests of `DMeshAABBTree3` ray queries
and of `DCurve3BoxTree` ray queries.

    dotnet run -c Release          # with DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 for a stable number

## Result (23 November 2026, 4 cores, ns per call, best of 9, two runs; before = master, after = this change)

| Operation | before | after |
|---|---|---|
| `IntrRay3AxisAlignedBox3.FindRayIntersectT` (exact; the tree uses `RayAabbMay`, below) | 26 | 41-53 |
| `IntrRay3AxisAlignedBox3.Intersects` | 15 | 41-55 |
| `IntrRay3Box3.Intersects` (oriented, expand 0.01), generic rays | 17 | 37-50 |
| `IntrRay3Box3.Find` | 83-105 | 224-280 |
| `IntrRay3AxisAlignedBox3.Find` | 57-60 | 93-97 |
| `DMeshAABBTree3.FindNearestHitTriangle` (266412 triangles, conservative form `RayAabbMay`) | 840-1070 | 724-874 |
| `DCurve3BoxTree.SquaredDistance(ray)` (4000 segments) | 34 us | 52 us |
| `DCurve3BoxTree.FindClosestRayIntersction` | 4.8 us | 8.7 us |

The two runs differ by up to 25 % on this machine (the second is the faster one); the ratios, not the single numbers, carry the result.

### Looking for a faster path (owner request, 23 November 2026)

- Done: one finiteness test per oriented filter (the sum of the 12 values of the three slabs and one test of the three extents) replaces the sum of 22 inputs and, since the review, the test per slab; an axis-parallel segment of an axis-aligned box has no error in the slabs with `d1 == d0` (before, `eQ = U |q| + AbsErr` sent every such segment to the exact route; it is only reachable through the internal `Find`, the public segment class takes an oriented box, whose `Prep` already had exact zeros). The effect on the numbers above is inside the run-to-run spread.
- `IntrRay3Box3.Find`: every hit takes the exact route (all 9770 hits of the benchmark's 10 passes): the ends of an oriented contact are certified to 6 u only when `P` does not cancel
  (`TErr` carries the error of the three dot products, the quotient `t = (face - P) / Q` is as large as `|x| + |y| + |z|` allows), and a result rounded once from the exact contact is the contract of `Point0` and
  `RayParam0`. **Measured, not built (24 November 2026):** (a) the upper bound: with the exact route switched off (wrong ends, timing only) `Find` takes 117-127 ns instead of 224-280 ns, so the best case
  is about 2x; (b) a double-word route for `P`, `Q`, `t` (the existing `DoubleWord`, as in `IntrTriangle3Triangle3.Find`) costs 290-360 ns for one slab (three Dekker TwoProducts each for `P` and `Q`, the
  division, `RoundsTo`), more than the whole BigInteger exact part of `Find` (about 110-150 ns for both ends and the points, the numbers are small). The double-word route is slower than what it would replace.
- `DCurve3BoxTree`: the cost is the certified bound of the oriented slabs in `IntrRay3Box3.Intersects`, called per box. **Measured, not built:** the cheapest conceivable culling form (the three `Prep` and the
  `Away` tests only, "may hit" for everything else, no divisions) costs 27 ns per call (37 ns now) but culls far less (23730 hits of the benchmark's rays against 1117), so `SquaredDistance(ray)` goes from 51 to 58 us
  and `FindClosestRayIntersction` from 8.5 to 19 us: slower. A culling form that culls as well as `Intersects` needs the slab bounds with their divisions, which is what `Intersects` computes.

The decision is now exact (hits, misses and the Point/Segment type; no `1e-8` slack, touching counts); the ends of a contact are on the
face exactly (axis-aligned) or within a few ulp. `DMeshAABBTree3` culls with the conservative form (`RayAabbMay`: false only for a certified miss, never an exact computation), which is cheaper than the old clip chain. The cost of the oriented ray is the certified error bound of each slab (division, absolute errors for the
dot products of an oriented box) that the old code did not have.
