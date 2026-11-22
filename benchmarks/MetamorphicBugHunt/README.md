# MetamorphicBugHunt

Harness of [docs/research/METAMORPHIC_BUG_HUNT_2026-11.md](../../docs/research/METAMORPHIC_BUG_HUNT_2026-11.md): metamorphic
relations and chains of operations on procedural inputs (no mesh files, no network), with every Success checked by code that
shares nothing with the operation under test (`Checks.cs`). It looks for wrong results that are reported as success and for
geometry that is lost without a report.

```
dotnet run --project benchmarks/MetamorphicBugHunt -c Release                  # all stages, about 6 minutes on 4 cores
dotnet run --project benchmarks/MetamorphicBugHunt -c Release -- chain repair  # some stages
dotnet run --project benchmarks/MetamorphicBugHunt -c Release -- io loft contour2 geodesic intrinsic quad wrap2 mesh2d2 tet robust findings2   # the second hunt, 75 s
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

The stages of the second hunt ([docs/research/METAMORPHIC_BUG_HUNT_2_2026-11.md](../../docs/research/METAMORPHIC_BUG_HUNT_2_2026-11.md);
all of them together take about 75 s):

| Stage | File | What it runs |
|---|---|---|
| `io` | `IoStage.cs` | every triangle format of `StandardMeshWriter` written to memory and read back: counts, closed and manifold, volume, positions bit for bit where the format stores doubles, a second round trip (the same bytes), a mesh with id gaps, 2^k (to 2^300), a flip, two meshes in one file, normals / colors / UVs / groups |
| `loft` | `LoftStage.cs` | `ContourLoft` (8 contour pairs, both costs, normalized, with and without caps) and `ContourLoftStack` (6 stacks, with and without `LoweredSaddle`): a solid, the vertex layout, reuse, start vertex, swap, reversal, mirror, contour order, upside down, a far copy, 2^k, similarities, a two-slice stack against the loft |
| `contour2` | `Contour2Stage.cs` | `MarchingCubes` and `DualContouring` (with and without `Simplify`) on 5 fields: serial = parallel, reuse, the iso value against a shifted field, 2^k, a mirror, a far copy, the negated field, every root mode; `TpmsLattice` (6 types x 3 styles): a solid inside its box, reuse, serial, 2^k, the frame, a box as domain mesh (also inside out and permuted), fractions and divisions |
| `geodesic` | `GeodesicStage.cs` | `PolyhedralGeodesicDistance` on 8 meshes: straight distance <= d <= edge path, symmetry, triangle inequality, min over the sources, traced paths, reuse, 2^k, similarities, flip, mirror, permutation, a far component, every triangle split in its plane; the heat distance within its documented band; FlipOut paths between the polyhedral distance and their initial length; `MeshGeodesicBezier`; `VectorHeatMethod` |
| `intrinsic` | `IntrinsicStage.cs` | `IntrinsicTriangulation` on 9 meshes (needles, slivers, open): area, Euler characteristic, angle sums and triangle inequalities kept by flips and insertions, Delaunay with non-negative cotan weights, traced edges, 2^k, the same Delaunay edges for a permuted / flipped / moved input; `DelaunayRefine` at 0, 25 and 30 degrees; the common subdivision (area, on the surface, closed, the same volume) |
| `quad` | `QuadStage.cs` | `QuadRemesher` on 6 meshes x 7 option sets (pure quads, size adaptation, curvature, creases, no cleanup): a manifold polygon mesh near the input, the input not modified, reuse, serial, 2^k, the pure-quad contract (success: closed quads with the input's Euler characteristic; failure: the stage 2 mesh), flipped and permuted input, a far copy |
| `wrap2` | `Wrap2Stage.cs` | `PassageWrap` on 5 passages (pins checked by winding number and sampled distance; reuse, one thread, 2^k, reversed / repeated / reordered pins, a smaller pin, permuted and flipped input, similarities, blocked pins); `AlphaWrapFeaturePass` by reflection on 10 inputs x 3 alpha / offset pairs (a clean solid, the input inside, 0.9 offsets kept, Euler characteristic, repeat, 2^k, a second pass, permuted and flipped input); 8 options of `MeshAlphaWrap` on 5 inputs |
| `mesh2d2` | `Mesh2OptionsStage.cs` | `QualityMesh2` on 5 domains x 8 option sets (`Conforming`, 33 degrees, `SizeFunction`, a Steiner limit of 5, no angle bound, `SnapTolerance`): structure (neighbours, subsegments, input points), a brute-force Delaunay test, the bounds outside `BadTriangles`, reuse, 2^k, a far copy, duplicated and shuffled points |
| `tet` | `TetStage.cs` | `MeshTetrahedralization` on 5 inputs x 9 option sets (target edge, size function, grading, surface deviation, creases, no improve, no peeling, an invalid size function): valid tets, the boundary a clean solid around the input, the volume band, `Targets` against a recount, reuse, one thread, 2^k, permuted and flipped input, a far copy |
| `robust` | `RobustStage.cs` | across the operations: a mesh with id gaps against its compact copy; a field times 2^k against the field; the input scaled by 2^-320 .. 2^400 (the scaled result bit for bit, or a failure status; an `INFO` line gives the measured range per operation) |
| `findings2` | `RobustStage.cs` | the reduced cases of the second hunt: `H2-F1` (#1375, `TpmsLattice.DomainMesh` with id gaps), `H2-F2` (#1376, `DualContouring`, a field of small values), `H2-F3` (#1377, success with another result outside the measured range); they fail until the issues are fixed. `O2`, `O3` are observations |

`results/master_dcc8e5ff.txt` is the output of the run described in the first note (Linux x64, net10.0, 4 cores),
`results/hunt2_master_42e33ebb.txt` that of the stages of the second.

The independent checks (`Checks.cs`): closed and manifold from the directed edges and the vertex fans, volume and area from the
triangles, the Euler characteristic, the winding number as a sum of solid angles (Van Oosterom and Strackee 1983), winding
samples against a Boolean function of the inputs' winding numbers, and brute-force distances. `MeshRobustRepair.NotCleanReason`
is used as the library's own word on a mesh, next to them.

A new relation or case: add it to the stage, with `Program.Report(stage, name, problem)` (an empty problem is a pass). A check
that fires on a documented limit is an `INFO`, not a `FAIL`; say in a comment where the limit is documented.
