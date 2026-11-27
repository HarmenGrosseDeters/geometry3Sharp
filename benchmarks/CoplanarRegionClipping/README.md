# CoplanarRegionClipping

The doubling series of #1630 (stage 3 of [docs/design/COPLANAR_REGION_CLIPPING.md](../../docs/design/COPLANAR_REGION_CLIPPING.md)): the union of a capped cylinder
(the ring of 4 K - 2 points on the rational circle, the caps are fans from the cap centres) and a copy shifted along x, with the old rule and with the internal switch
`ExactMeshBoolean.RegionClipping`, and a case without a coplanar cluster.

```
dotnet run --project benchmarks/CoplanarRegionClipping -c Release -- [--old-all] [--rings 61,121,241,481,961] [--reps 3]
```

The old rule takes 27.5 s at ring 481 (and much more at 961): it runs up to ring 241 unless `--old-all`. Median of 3 runs after a warm-up run (the first run of a
process is JIT: 2 to 3 times slower at ring 481), 4 cores, net10.0, 2026-11-27.

## Results

Case S, a shift of 2 ulps (the contact of #1630):

| ring | pieces old | pieces region | ms old | ms region | growth region | candidate pairs | intersecting pairs |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 61 | 7,728 | 1,232 | 398 | 158 | - | 6,128 | 2,470 |
| 121 | 29,808 | 2,432 | 1,113 | 182 | x1.2 | 23,048 | 8,530 |
| 241 | 117,168 | 4,832 | 5,224 | 477 | x2.6 | 89,288 | 31,450 |
| 481 | - (27.5 s run) | 9,632 | 27,529 | 1,401 | x2.9 | 351,368 | 120,490 |
| 961 | - | 19,232 | - | 5,267 | x3.8 | 1,393,928 | 471,370 |

Case R, a shift of 0.25:

| ring | pieces old | pieces region | ms old | ms region | growth region | candidate pairs | intersecting pairs |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 61 | 6,064 | 1,280 | 63 | 25 | - | 5,464 | 2,018 |
| 121 | 22,480 | 2,528 | 282 | 58 | x2.4 | 20,376 | 6,626 |
| 241 | 86,448 | 5,040 | 1,286 | 177 | x3.0 | 77,856 | 23,614 |
| 481 | - | 10,064 | - | 778 | x4.4 | 306,336 | 88,870 |
| 961 | - | 20,096 | - | 2,454 | x3.2 | 1,210,632 | 344,354 |

No coplanar cluster (two perpendicular capped cylinders, 128 slices): old 12 ms, region 13 ms, the same 2,764 pieces and 512 result triangles.

## Reading

The pieces are linear (x2.0 per doubling): that was the goal of the rule. The time is not: it grows about x3 to x4 per doubling, with the **intersecting coplanar pairs**,
which grow x3.8 to x3.9. In these inputs the two apexes of the fans are 2 ulps (or 0.25) apart, so the thin wedges of one fan cross almost every wedge of the other near
the centre: the pairs are really quadratic, and each is clipped (six clips) and recorded as coplanar, although the pieces they would cut are not cut any more (the flat edges
are ghosts). What is left is the pair stage (`intersections.intersect`, 0.7 s of 1.4 s at ring 481) and the ghost handling in `prepare_touched`.

## Upper bound for a linear pair stage

A scratch probe (not committed) that skips every coplanar pair of different meshes whose triangle ids differ by more than 8 (only the neighbouring wedges are clipped; the results
are invalid, only the time means something): case S takes 466 ms at ring 481 (1,401 ms) and 1,338 ms at ring 961 (5,267 ms), with 5,770 and 11,530 intersecting pairs
(linear). So the coplanar pair stage is about two thirds of the time at ring 481 and three quarters at ring 961: a linear pair stage could save at most that. The
case R probe is not meaningful (its overlaps are real, the invalid results change the later stages).
