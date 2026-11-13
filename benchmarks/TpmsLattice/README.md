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

Raw output (one run, same session, after a warm-up):

```
cores 4, divisions 16
type/style, periods, nodes, triangles, field ms, compute(no SI check) ms, SI check ms, alloc MB, gen0/1/2
Gyroid/Sheet, 2, 59319, 40532, 11.3, 177.5, 314.8, 15.4, 2/2/2
Gyroid/Sheet, 4, 357911, 316836, 22.5, 649.2, 599.4, 126.6, 9/9/9
Gyroid/Sheet, 6, 1092727, 1060852, 57.0, 1036.7, 1131.1, 471.4, 8/8/7
SchwarzP/SkeletalLow, 2, 59319, 14800, 3.2, 51.6, 15.1, 7.9, 0/0/0
SchwarzP/SkeletalLow, 4, 357911, 116288, 17.8, 120.5, 124.0, 47.3, 0/0/0
SchwarzP/SkeletalLow, 6, 1092727, 390096, 90.6, 420.3, 438.9, 169.0, 1/1/0
Neovius/Sheet, 2, 59319, 36944, 3.3, 211.4, 100.1, 31.0, 0/0/0
Neovius/Sheet, 4, 357911, 278944, 17.7, 457.9, 404.1, 173.4, 2/2/1
Neovius/Sheet, 6, 1092727, 922608, 49.4, 1313.1, 1365.8, 552.6, 14/14/12
```

Reading (workload: gyroid / P / Neovius, 2 to 6 periods, 16 cubes per period; other workloads are not covered):

- The field is about 2-20 % of `Compute` (3-90 ms against 50-1300 ms; the P runs are the high end, they have the cheapest contouring): the sin / cos evaluation is not the cost.
- Marching cubes with its vertex dictionary, the mesh build and the certificate's cheap parts dominate `Compute`;
  about 1 s per million triangles. The wall time includes GC: the large gyroid / Neovius runs show several gen1/gen2
  collections (allocation of the mesh and the hash), the P runs almost none. This was not separated further.
- The exact self-intersection check costs about as much as the rest (0.3-1.4 s for 40 k to 1.1 M triangles), so it is on by default but can be switched off (`VerifySelfIntersections`).
