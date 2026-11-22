# QuadSheafObstruction

Research spike, a prototype and not a gate: [docs/research/QUAD_SHEAF_OBSTRUCTION_2026-11.md](../../docs/research/QUAD_SHEAF_OBSTRUCTION_2026-11.md)
(the method, the reading of the numbers, the options). It asks whether a cohomology computation on the lattice offsets of the position
field predicts, before the flow and the repair of stage 3B, whether and where `QuadRemesher.PureQuads` falls back. The inputs are the 54 runs
of [QuadFlow3BGate](../QuadFlow3BGate/README.md) (9 procedural shapes x 6 variations).

```
dotnet run -c Release --project benchmarks/QuadSheafObstruction -- [only=<regex>] [radius=<x>]     # main pass
dotnet run -c Release --project benchmarks/QuadSheafObstruction -- levers=1 [only=<regex>]         # oracle over the existing levers
```

**Main pass**, per run:
- the complex: per triangle edge the integer offset of the position field and the edge vector in the cross frame (in lattice units), with the
  quarter-turn transitions between the triangle frames. It is rebuilt here from the public accessors of `PositionField` and `CrossField`
  with the formulas of `QuadFlowRepair.BuildOffsets` / `BuildTriangles`; the run checks that the two sides of every edge agree;
- before the flow: the defect mass (sum of the integer closing errors of the triangles), the curl mass (the same for the real cochain), the
  filling energy `w^T L^-1 w` of the sheaf Laplacian `L` (3 I on the diagonal, `-R^k` between neighbouring triangles; `SparseCholesky` with
  2 x 2 blocks), the largest entry of the minimal correction, the share of edges with a correction of half a cell or more, and the area and
  the folds of the relaxed (closed, real) map;
- after the attempt: the status, and the lattice area `(1/2) sum det` of the library's final integer map over the geometric area in cells.
  The map is read by reflection (`QuadRemesher.PureQuadRepair` is internal), so the spike changes no library file. If the names `tris`,
  `VP` or `Offsets` change, the two columns of the library's map print `-`;
- the summary: the ranking power (AUC) of every quantity for "fallback", and how many of the final folds lie within `radius` x s (default
  1.5) of the triangles each quantity marks, against the share of the surface within that radius.

**Lever pass** (`levers=1`): the 8 shapes (the aligned torus is the torus with the lever on) x 6 variations = 48 inputs, each with
`CurvatureAlignment` 0 and 10 and every `SizeAdaptation` mode the library accepts together with `PureQuads`; clean as in the gate
(independent check). It prints the clean count per configuration and with at least one configuration.

## Results (2026-11-22, net10.0 Release, 4-core cloud container)

`results_master.txt` / `results_master_levers.txt`: master e340ef5c. `results_pr1293.txt` / `results_pr1293_levers.txt`: the same benchmark
built against the head of PR #1293 (885b514b, the geometric sign reference of the flow and `SizeAdaptation` with `PureQuads`).

| | master e340ef5c | PR #1293 head |
|---|---|---|
| clean / fallback (54 runs) | 25 / 29 | 43 / 11 |
| AUC for "fallback": curl mass per cell | 0.65 | 0.76 |
| AUC: filling energy of the curl per cell | 0.82 | 0.97 |
| AUC: largest correction | 0.92 | 0.95 |
| rows right with one threshold (best quantity; majority class) | 46 / 54 (29) | 51 / 54 (43) |
| library map area / geometric area, clean rows: min / median | 0.79 / 0.96 | 0.10 / 0.94 |
| the same, fallback rows: min / median / max | 0.07 / 0.69 / 1.18 | 0.06 / 0.49 / 0.75 |
| clean rows with an area ratio below 0.75 | 0 / 25 | 6 / 43 |
| fallback rows with an area ratio below 0.75 | 19 / 29 | 10 / 11 |
| final folds within 1.5 s of an edge correction >= 0.5 (share of the surface there) | 19.8 % (5.5 %) | 41.9 % (5.4 %) |
| pure-quad attempts of the fallback rows / their stage 2 alone / sheaf analysis of all 54 rows (s) | 18.7 / 2.5 / 6.4 | 2.9 / 0.5 / 6.1 |
| lever pass: best single configuration / with at least one (48 inputs) | 19 / 24 | 42 / 45 |

The lever pass on master has two runs (capped cylinder at scale 1.25 and 1.5 with `CurvatureAlignment` 10) that are `Success` and fail the
independent check; with PR #1293 both are `FlippedFaces`.

The AUC and the thresholds are in sample (54 rows, 9 shapes): they describe these inputs, they are not a validated classifier.
