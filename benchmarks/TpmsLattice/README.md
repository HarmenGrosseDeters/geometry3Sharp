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
```

Reading (workload: gyroid / P / Neovius, 2 to 6 periods, 16 cubes per period; other workloads are not covered):

- The graded gyroid sheet has fewer triangles than the constant one (the period grows to 2, so the surface area per volume falls) and
  costs about 0.7 of it in `Compute` at the same node count (775.6 against 1119.9 ms at 6 periods). The cost of the phase-map delegate
  call per sample (one `Math.Log` and a few divisions) is not separated from the rest in this benchmark (the "field" column is the bare
  `Evaluate`, without the map).

- The field is about 2-20 % of `Compute` (3-90 ms against 50-1300 ms; the P runs are the high end, they have the cheapest contouring): the sin / cos evaluation is not the cost.
- Marching cubes with its vertex dictionary, the mesh build and the certificate's cheap parts dominate `Compute`;
  about 1 s per million triangles. The wall time includes GC: the large gyroid / Neovius runs show several gen1/gen2
  collections (allocation of the mesh and the hash), the P runs almost none. This was not separated further.
- The exact self-intersection check costs about as much as the rest (0.3-1.4 s for 40 k to 1.1 M triangles), so it is on by default but can be switched off (`VerifySelfIntersections`).
