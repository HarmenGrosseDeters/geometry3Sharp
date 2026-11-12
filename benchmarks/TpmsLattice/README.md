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
self-intersection check) on the finished mesh. "table" is the cold cost of one quantile table (64^3 samples, sorted),
paid once per type and style per process.

Raw output (one run, same session, after a warm-up):

```
cores 4, divisions 16
type/style, periods, nodes, triangles, table ms, field ms, compute(no SI check) ms, SI check ms, alloc MB, gen0/1/2
Gyroid/Sheet, 2, 59319, 40532, 68.4, 8.3, 153.8, 403.2, 15.4, 2/2/2
Gyroid/Sheet, 4, 357911, 316836, 50.1, 17.5, 566.0, 490.1, 126.5, 7/7/7
Gyroid/Sheet, 6, 1092727, 1060852, 30.4, 43.2, 1075.2, 1199.6, 471.4, 8/7/6
SchwarzP/SkeletalLow, 2, 59319, 14800, 33.1, 5.2, 68.7, 19.3, 7.9, 0/0/0
SchwarzP/SkeletalLow, 4, 357911, 116288, 41.3, 19.4, 119.7, 137.3, 47.3, 0/0/0
SchwarzP/SkeletalLow, 6, 1092727, 390096, 28.1, 53.4, 412.2, 331.5, 169.0, 1/1/0
Neovius/Sheet, 2, 59319, 36944, 39.0, 2.6, 233.0, 97.3, 31.0, 0/0/0
Neovius/Sheet, 4, 357911, 278944, 31.0, 28.9, 501.5, 401.0, 173.5, 6/6/6
Neovius/Sheet, 6, 1092727, 922608, 30.6, 64.8, 1291.4, 1193.7, 552.6, 15/15/12
```

Reading (workload: gyroid / P / Neovius, 2 to 6 periods, 16 cubes per period; other workloads are not covered):

- The field is 3-5 % of `Compute` (5-65 ms against 70-1300 ms): the sin / cos evaluation is not the cost.
- Marching cubes with its vertex dictionary, the mesh build and the certificate's cheap parts dominate `Compute`;
  about 1 s per million triangles. The wall time includes GC: the large gyroid / Neovius runs show several gen1/gen2
  collections (allocation of the mesh and the hash), the P runs almost none. This was not separated further.
- The exact self-intersection check costs about as much as the rest (0.4-1.2 s for 40 k to 1.1 M triangles; a few
  hundred ms per 100 k triangles), so it is on by default but can be switched off (`VerifySelfIntersections`).
- The cold quantile table costs 30-70 ms once per type and style.
