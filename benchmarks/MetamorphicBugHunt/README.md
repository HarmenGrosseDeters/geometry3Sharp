# MetamorphicBugHunt

Harness of [docs/research/METAMORPHIC_BUG_HUNT_2026-11.md](../../docs/research/METAMORPHIC_BUG_HUNT_2026-11.md): metamorphic
relations and chains of operations on procedural inputs (no mesh files, no network), with every Success checked by code that
shares nothing with the operation under test (`Checks.cs`). It looks for wrong results that are reported as success and for
geometry that is lost without a report.

```
dotnet run --project benchmarks/MetamorphicBugHunt -c Release                  # all stages, about 5 minutes on 4 cores
dotnet run --project benchmarks/MetamorphicBugHunt -c Release -- chain repair  # some stages
dotnet run --project benchmarks/MetamorphicBugHunt -c Release -- -v findings   # -v also prints the PASS lines
G3_HUNT_SKIP_OFFSET=1 dotnet run --project benchmarks/MetamorphicBugHunt -c Release -- contour   # without MeshOffset (2.5 of the 5 minutes)
```

One line per check: `FAIL stage | case | reason`, `SLOW` for one call above 20 s, `INFO` for a result the library itself reports
as not clean or for an observation that is no defect, `STAGE` with the counts and the time of a stage, `TOTAL` at the end. The
exit code is always 0: the output is the result.

| Stage | File | What it runs |
|---|---|---|
| `boolean` | `BooleanStage.cs` | `ExactMeshBoolean` on 17 pairs (9 general, 8 with coplanar or touching faces): solid, winding samples, commutativity, permutation, 2^k bit for bit, six similarities, inclusion-exclusion, `(A∪B)−B = A−B`, `(A−B)∪(A∩B) = A`, `(A∪B)∩A = A`, `A op A`, split by a half space and join |
| `chain` | `ChainStage.cs` | each Boolean result → `MeshRobustRepair` (identity on a clean mesh, twice, next to a far part) → `Remesher` (with and without `PreventSelfIntersections`), `Reducer` (count and `MaxDeviation`), `QuadRemesher`, `MeshTetrahedralization`, and `SolidBooleanPipeline` with and without a bounded reduction |
| `deep` | `DeepChainStage.cs` | 24 random chains of up to 6 Booleans on the rounded result of the step before, checked against the composition of the original solids' winding numbers, with the whole chain at 2^10 bit for bit; `SolidBooleanPipeline` with broken inputs in every `InputRepair` mode |
| `repair` | `RepairStage.cs` | `MeshRobustRepair` on 33 procedural defects of known solids (overlaps, holes, flipped and unwelded triangles, duplicates, a cavity, parts of different size, a thin plate): solid, expected volume and winding, idempotence, 2^k, permutation, rotation, far translation |
| `wrap` | `WrapStage.cs` | `MeshAlphaWrap` and `MeshTetrahedralization` on 11 clean and broken inputs: the documented guarantees, monotony in the offset, 2^k, permutation, rotation, far translation, two far parts against one, wrap of the wrap; tet validity by an own check and the library's, reuse, serial = parallel |
| `contour` | `ContourStage.cs` | `MarchingCubes` and `DualContouring` on five distance fields (one singular, one thinner than a cell), `MeshOffset` on five solids at five distances: closed and manifold, analytic volume, 2^k, a shift by whole cells, the band, offset then inset |
| `mesh2d` | `Mesh2Stage.cs` | `QualityMesh2` on eight domains at two angle bounds: area, orientation, edge manifold, input points kept, the angle and area bounds outside `BadTriangles`, 2^k (same triangles), rotation, far translation, mirror, start vertex, ring direction, an added segment |
| `findings` | `FindingsStage.cs` | the reduced cases of the findings: `F1` is issue #1337 and fails until it is fixed, `O1` is an observation |

`results/master_dcc8e5ff.txt` is the output of the run described in the note (Linux x64, net10.0, 4 cores).

The independent checks (`Checks.cs`): closed and manifold from the directed edges and the vertex fans, volume and area from the
triangles, the Euler characteristic, the winding number as a sum of solid angles (Van Oosterom and Strackee 1983), winding
samples against a Boolean function of the inputs' winding numbers, and brute-force distances. `MeshRobustRepair.NotCleanReason`
is used as the library's own word on a mesh, next to them.

A new relation or case: add it to the stage, with `Program.Report(stage, name, problem)` (an empty problem is a pass). A check
that fires on a documented limit is an `INFO`, not a `FAIL`; say in a comment where the limit is documented.
