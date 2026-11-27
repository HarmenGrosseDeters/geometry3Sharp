# TpmsLattice benchmark

Stage shares of `TpmsLattice.Compute` (design: [docs/design/TPMS_LATTICE.md](../../docs/design/TPMS_LATTICE.md) section 6).

```
dotnet run --project benchmarks/TpmsLattice -c Release -f net10.0 -- 2,4,6 16
```

Box of `periods^3` periods, 16 cubes per period, volume fraction 0.3, a 4-core Linux container, net10.0, workstation
GC. All wall times include the garbage collector; the GC counts (gen0/1/2) and the allocated MB are listed so its
share can be judged. "field" is the bare `TpmsLattice.Evaluate` at every grid node, serial, without the domain clip and
the style (the lower bound of the field cost). "compute" is the whole `Compute()` with `VerifySelfIntersections = false`
(field, clip, marching cubes with its vertex hash, `NotCleanReason` without self-intersections, volume). "SI check" is
`MeshRobustRepair.NotCleanReason(mesh, true)` alone (all other parts of the certificate plus the exact
self-intersection check) on the finished mesh. (The quantile table, 64^3 samples sorted, costs 30-70 ms once per type
and style per process; measured in an earlier run of this benchmark, not part of the output any more.)

Raw output (one run, same session, after a warm-up; the `(graded)` rows are a linear grade along z, period 1 to 2, `Grade` from
`TpmsLattice.LinearGrade`, box `periods x periods x periods` centred in x and y; with the sliver guard of #838, without which the graded rows at
4 and 6 periods end in `NotClean`):

```
cores 4, divisions 16
type/style, periods, nodes, triangles, field ms, compute(no SI check) ms, SI check ms, alloc MB, gen0/1/2
Gyroid/Sheet, 2, 59319, 40916, 7.8, 155.1, 139.8, 15.5, 2/2/2
Gyroid/Sheet, 4, 357911, 318372, 19.9, 632.7, 807.2, 126.8, 9/9/9
Gyroid/Sheet, 6, 1092727, 1064308, 53.0, 1119.9, 1121.4, 508.3, 12/12/11
SchwarzP/SkeletalLow, 2, 59319, 14800, 3.2, 56.9, 13.9, 7.9, 0/0/0
SchwarzP/SkeletalLow, 4, 357911, 116288, 29.3, 183.3, 128.5, 47.3, 0/0/0
SchwarzP/SkeletalLow, 6, 1092727, 390096, 53.2, 479.7, 362.1, 169.0, 1/1/0
Neovius/Sheet, 2, 59319, 34640, 3.0, 211.5, 91.8, 27.6, 0/0/0
Neovius/Sheet, 4, 357911, 257440, 18.1, 457.3, 464.0, 154.7, 3/3/3
Neovius/Sheet, 6, 1092727, 846576, 53.8, 1008.0, 1194.7, 525.1, 10/10/8
Gyroid(graded)/Sheet, 2, 59319, 29692, 3.1, 47.9, 32.2, 11.9, 1/1/1
Gyroid(graded)/Sheet, 4, 357911, 224776, 16.4, 283.3, 242.7, 93.6, 8/8/8
Gyroid(graded)/Sheet, 6, 1092727, 747280, 45.9, 775.6, 778.4, 334.2, 11/11/10
Gyroid(+IWP)/Sheet, 2, 59319, 40556, 8.7, 110.3, 52.9, 19.7, 3/3/3
Gyroid(+IWP)/Sheet, 4, 357911, 319028, 61.3, 433.1, 330.0, 141.8, 9/9/9
Gyroid(+IWP)/Sheet, 6, 1092727, 1070044, 147.5, 1333.0, 1166.2, 491.0, 10/10/9
```

The `(+IWP)` rows are the two-type hybrid (docs/design/TPMS_LATTICE.md section 9): gyroid sheet with `SecondType = IWP` across the plane
`x = periods / 2`, `SigmoidBlend` width 0.5; the "field" column there is both bare `Evaluate`s. The hybrid rows come from a later run of the whole benchmark than the other rows (same container; the two runs differ by the usual noise of a shared container, eg the first row 6.4 against 7.8 ms field and 151.7 against 155.1 ms compute).

Reading (workload: gyroid / P / Neovius, 2 to 6 periods, 16 cubes per period; other workloads are not covered):

- The hybrid costs about 1.15 times the constant gyroid sheet in `Compute` at 6 periods (1333 against 1157 ms; two fields and the weight at every node, the "field"
  column is 148 against 59 ms) and allocates the same (491 against 508 MB) at nearly the same triangle count. An earlier run of the hybrid, before the sliver guard was scaled by
  the phase part of the Lipschitz bound (docs/design/TPMS_LATTICE.md 9.7), took 2782 ms and 1225 MB: the unscaled guard was a larger fraction of the field's range
  than for the constant lattice and made the contouring much more expensive (it also thickened the lattice for steep weights, `SteepBlend_DoesNotThickenThePureSides`).
- The graded gyroid sheet has fewer triangles than the constant one (the period grows to 2, so the surface area per volume falls) and
  costs about 0.7 of it in `Compute` at the same node count (775.6 against 1119.9 ms at 6 periods). The cost of the phase-map delegate
  call per sample (one `Math.Log` and a few divisions) is not separated from the rest in this benchmark (the "field" column is the bare
  `Evaluate`, without the map).

- The field is about 2-20 % of `Compute` (3-90 ms against 50-1300 ms; the P runs are the high end, they have the cheapest contouring): the sin / cos evaluation is not the cost.
- Marching cubes with its vertex dictionary, the mesh build and the certificate's cheap parts dominate `Compute`;
  about 1 s per million triangles. The wall time includes GC: the large gyroid / Neovius runs show several gen1/gen2
  collections (allocation of the mesh and the hash), the P runs almost none. This was not separated further.
- The exact self-intersection check costs about as much as the rest (0.3-1.4 s for 40 k to 1.1 M triangles), so it is on by default but can be switched off (`VerifySelfIntersections`).

## `TwoLevelSheet` against marching cubes (thin walls)

```
dotnet run --project benchmarks/TpmsLattice -c Release -f net10.0 -- twolevel 4
```

Gyroid sheet in a box of 4^3 periods, `WallThickness` in cubes from resolved to a tenth of a cube; the default (marching cubes on
`|f| - t`) against `TwoLevelSheet = true` (`TwoLevelMarchingTetrahedra` on the two levels `f = +t`, `f = -t`). Both with the whole
certificate (`NotCleanReason` with the exact self-intersection check). "mean wall / asked" is `MeanWallThickness` (2 V / A of the result)
over the `WallThickness` asked for: about 1 where the wall is there, well below 1 where parts of it are lost. Compute ms: best of 3,
a 4-core Linux container, net10.0, 2026-11-22.

| divisions | wall (cubes) | method | status | components | triangles | mean wall / asked | compute ms |
|---|---|---|---|---|---|---|---|
| 8 | 2 | marching cubes | Ok | 3 | 58 780 | 1.34 | 300 |
| 8 | 2 | two-level | Ok | 1 | 152 956 | 1.31 | 252 |
| 8 | 1 | marching cubes | Ok | 1 | 94 884 | 0.58 | 196 |
| 8 | 1 | two-level | Ok | 2 | 216 512 | 1.08 | 641 |
| 8 | 0.5 | marching cubes | ContouringFailed (empty) | - | - | - | 7 |
| 8 | 0.5 | two-level | Ok | 1 | 199 044 | 1.05 | 342 |
| 8 | 0.25 | marching cubes | ContouringFailed (empty) | - | - | - | 9 |
| 8 | 0.25 | two-level | Ok | 1 | 199 044 | 1.04 | 371 |
| 8 | 0.1 | marching cubes | ContouringFailed (empty) | - | - | - | 7 |
| 8 | 0.1 | two-level | Ok | 1 | 198 276 | 1.04 | 396 |
| 16 | 3 | marching cubes | Ok | 3 | 279 964 | 1.07 | 972 |
| 16 | 3 | two-level | Ok | 3 | 756 796 | 1.05 | 2471 |
| 16 | 2 | marching cubes | Ok | 3 | 298 396 | 1.03 | 935 |
| 16 | 2 | two-level | Ok | 3 | 760 828 | 1.02 | 2369 |
| 16 | 1 | marching cubes | Ok | 1 | 368 292 | 0.67 | 800 |
| 16 | 1 | two-level | Ok | 2 | 815 552 | 1.01 | 2552 |
| 16 | 0.5 | marching cubes | NotResolved (refused, #1596: 0.8 % of the volume asked) | - | - | - | 304 |
| 16 | 0.5 | two-level | Ok | 1 | 780 420 | 0.99 | 1657 |
| 16 | 0.25 | marching cubes | NotResolved (refused, #1596: 0.03 % of the volume asked) | - | - | - | 114 |
| 16 | 0.25 | two-level | Ok | 1 | 774 276 | 1.01 | 1653 |
| 16 | 0.1 | marching cubes | ContouringFailed (empty) | - | - | - | 29 |
| 16 | 0.1 | two-level | Ok | 1 | 761 220 | 1.09 | 1573 |

(8 divisions, 3 cubes: both `InvalidOptions`, the wall is too thick for the first-order map of `WallThickness`.)

Reading (this workload: a gyroid sheet, 4 periods, 8 and 16 cubes per period; other types and domains are covered by tests, not here):

- From a wall of one cube down, marching cubes loses the wall: at one cube the mean wall is 0.58 to 0.67 of the one asked for, at half a
  cube and below the result is empty, or thousands of fragments (before #1596 reported as `Ok`, now refused as `NotResolved`: the
  volume realized is under half of the one asked, here 0.8 % and 0.03 %). The two-level result keeps the wall asked
  for (0.99 to 1.09) down to a tenth of a cube, in one or two components, and passes the same certificate.
- Where both resolve the wall (2 and 3 cubes at 16 divisions) they agree: the same number of components and the mean wall within 2 %.
- Cost: 2.5 to 2.7 times the triangles of marching cubes (five tetrahedra per cube) and 2.5 times the time of `Compute` at 16
  divisions where both give the wall (2 and 3 cubes; the exact self-intersection check and the rest of the certificate scale with the
  triangles; not separated here). The times at 8 divisions are small and uneven (252 against 300 ms at 2 cubes, 641 ms at 1 cube with the
  same grid; not investigated).
- The component counts differ in places (8 divisions, 2 cubes: 3 against 1; 1 cube: 1 against 2). At thick walls the extra components are
  small pieces that the box cuts off; this benchmark does not check the counts against a reference.

## #1577: correctly rounded sin / cos (CoreMath) with per-axis node tables

`TpmsLattice.Evaluate` uses `g3.CoreMathPort.CoreMath.Sin / Cos` (correctly rounded, the same bits on every runtime, WebAssembly included) instead of the
platform `Math.Sin / Math.Cos`. A plain drop-in costs 7 to 73 % of `Compute()` (total +24 %: the field is 8 to 10 times slower, and marching cubes evaluates every
node once for each of the up to eight cells around it). The result is the same bit for bit with a speed-up of the CoreMath variant: the grid nodes take their sines and cosines
from per-axis tables (the field is a function of three separable phases; a point that is not exactly a node, such as an edge root, takes the direct path), and a graded lattice
(`Grade` / `PhaseMap`: not separable) keeps the value of each node once (8 bytes a node, 32 MB at most: only up to 2^22 nodes per grid attempt, a larger grid evaluates directly; the benchmark rows are below the cap). Test: `TpmsAxisTableTests`.

"compute" ms of `Compute()` with `VerifySelfIntersections = false`, `dotnet run --project benchmarks/TpmsLattice -c Release -f net10.0 -- 2,4,6 16`, a 4-core Linux
container, master (804560de) and the candidate built from the same checkout and run alternately, the minimum of 4 runs each:

| row | master ms | candidate ms | change |
|---|---|---|---|
| Gyroid/Sheet, 2 periods | 140.4 | 140.0 | -0.3 % |
| Gyroid/Sheet, 4 periods | 488.2 | 446.5 | -8.5 % |
| Gyroid/Sheet, 6 periods | 1137.7 | 1036.7 | -8.9 % |
| SchwarzP/SkeletalLow, 2 periods | 54.5 | 32.6 | -40.2 % |
| SchwarzP/SkeletalLow, 4 periods | 121.6 | 92.3 | -24.1 % |
| SchwarzP/SkeletalLow, 6 periods | 373.5 | 328.7 | -12.0 % |
| Neovius/Sheet, 2 periods | 203.2 | 186.5 | -8.2 % |
| Neovius/Sheet, 4 periods | 381.3 | 381.3 | +0.0 % |
| Neovius/Sheet, 6 periods | 1120.6 | 1075.5 | -4.0 % |
| Gyroid(graded)/Sheet, 2 periods | 40.3 | 35.4 | -12.2 % |
| Gyroid(graded)/Sheet, 4 periods | 248.8 | 219.9 | -11.6 % |
| Gyroid(graded)/Sheet, 6 periods | 807.9 | 813.8 | +0.7 % |
| Gyroid(+IWP)/Sheet, 2 periods | 78.4 | 51.7 | -34.1 % |
| Gyroid(+IWP)/Sheet, 4 periods | 370.5 | 305.9 | -17.4 % |
| Gyroid(+IWP)/Sheet, 6 periods | 1277.0 | 1087.2 | -14.9 % |
| total | 6843.9 | 6234.0 | -8.9 % |

The "field" column (bare `Evaluate` at every node, serial, direct CoreMath) is 8 to 10 times slower than before (for example Gyroid 4 periods 14.6 ms to 140 ms); it is not what `Compute()` pays.

## #1682: CoreMath exp / log in the blend and the grade (the field in the determinism proof scope)

`SigmoidBlend` is `1 / (1 + CoreMath.Exp(-4 eta / width))` (was `0.5 (1 + Math.Tanh(2 eta / width))`), `LinearGrade`'s phase uses `CoreMath.Log` (was `Math.Log`),
`Math.Pow` became constants or a product, and the value minima / maxima of the field use `MathUtil.MinExact / MaxExact`. Stop rule of #1577: at most 15 % cost.
Same setup as above (`-- 2,4,6 16`, 4-core Linux container, master 4eb8b866 and the candidate run alternately, the minimum of 4 runs each, "compute" ms):

| row | master ms | candidate ms | change |
|---|---|---|---|
| Gyroid/Sheet, 2 periods | 126.0 | 139.0 | +10.3 % |
| Gyroid/Sheet, 4 periods | 459.3 | 437.1 | -4.8 % |
| Gyroid/Sheet, 6 periods | 1064.2 | 985.4 | -7.4 % |
| SchwarzP/SkeletalLow, 2 periods | 30.5 | 32.3 | +5.9 % |
| SchwarzP/SkeletalLow, 4 periods | 84.3 | 89.6 | +6.3 % |
| SchwarzP/SkeletalLow, 6 periods | 345.8 | 322.7 | -6.7 % |
| Neovius/Sheet, 2 periods | 169.5 | 169.1 | -0.2 % |
| Neovius/Sheet, 4 periods | 381.5 | 401.8 | +5.3 % |
| Neovius/Sheet, 6 periods | 1093.0 | 1039.1 | -4.9 % |
| Gyroid(graded)/Sheet, 2 periods | 34.8 | 39.7 | +14.1 % |
| Gyroid(graded)/Sheet, 4 periods | 237.4 | 237.1 | -0.1 % |
| Gyroid(graded)/Sheet, 6 periods | 820.3 | 800.3 | -2.4 % |
| Gyroid(+IWP)/Sheet, 2 periods | 64.7 | 65.4 | +1.1 % |
| Gyroid(+IWP)/Sheet, 4 periods | 287.2 | 280.4 | -2.4 % |
| Gyroid(+IWP)/Sheet, 6 periods | 1057.1 | 1012.8 | -4.2 % |
| total | 6255.6 | 6051.8 | -3.3 % |

The rows of the non-graded, non-hybrid lattices do not run the changed functions: their spread (-7 to +10 %) is the noise of this container. A
microbenchmark of the changed functions alone (5 million calls, best of 7, two runs each): the `SigmoidBlend` weight 12.3 / 13.7 ns on master, 8.6 / 8.6 ns
with `CoreMath.Exp` (faster); the `LinearGrade` map 15.0 / 16.5 ns on master, 15.6 / 15.4 ns with `CoreMath.Log` (within 4 %). Within the stop rule. The meshes of the
default and the graded lattices are the same bits as on master; the hybrid meshes change (the weights differ in the last bit at 63 % of sampled points).
