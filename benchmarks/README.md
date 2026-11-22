# Benchmarks

Performance, quality, and acceptance-gate benchmarks for `geometry3Sharp`. Most benchmarks generate procedural inputs (no committed mesh files) and record measured before/after numbers in their own `README.md`.

Run any benchmark in Release mode:

```bash
dotnet run --project benchmarks/<BenchmarkName> -c Release
```

For cross-cutting performance analyses and profiling write-ups, see [docs/perf/README.md](../docs/perf/README.md).

---

## Exact Booleans, Predicates & Rounding

| Benchmark | Focus |
|---|---|
| [BooleanComparison](BooleanComparison/README.md) | `ExactMeshBoolean` vs. external boolean engines (CGAL, Geogram, Manifold) across Thingi10K & ABC pairs |
| [Boolean30Prestudy](Boolean30Prestudy/README.md) | Stage profiling and measurement probes for Exact Boolean 3.0 |
| [ExactImprint](ExactImprint/README.md) | Cost of `ExactMeshImprint` compared to `ExactMeshBoolean` on procedural pairs |
| [ExtremeBooleans](ExtremeBooleans/README.md) | Reproduction scripts and profiling for extreme-runtime boolean pairs |
| [LevyClassification](LevyClassification/README.md) | Phase-1 propagation classification probe for `ExactMeshBoolean` |
| [MeshMeshCutContactBenchmark](MeshMeshCutContactBenchmark/README.md) | Cost of exact contact detection in `MeshMeshCut.RefuseContact` |
| [PredicateComparison](PredicateComparison/README.md) | Accuracy and speed of `orient2d`, `incircle`, and `orient3d` implementations vs. exact reference |
| [SelfIntersectionBenchmark](SelfIntersectionBenchmark/README.md) | Cost of exact self-intersection checks and `Reducer.PreventSelfIntersections` |
| [SnapRoundingExperiment](SnapRoundingExperiment/README.md) | Snap-rounding prototypes and quality measurements for `SAFE_ROUNDING.md` |
| [SolidBooleanBenchmark](SolidBooleanBenchmark/README.md) | `SolidBooleanPipeline` (exact boolean + guarded reduction + contact pairing) on procedural soups |

## Repair, Alpha Wrap, Remeshing & Simplification

| Benchmark | Focus |
|---|---|
| [MeshRepairBenchmark](MeshRepairBenchmark/README.md) | `MeshAutoRepair` and `MeshRobustRepair` evaluation on Thingi10K and procedural failure cases |
| [NotCleanBenchmark](NotCleanBenchmark/README.md) | Runtime of the checks behind `MeshRobustRepair.NotCleanReason` on large procedural meshes |
| [OpenSoupWrapBenchmark](OpenSoupWrapBenchmark/README.md) | Alpha-wrap size estimation on open inputs and `WrapMaxEstimatedTriangles` budget |
| [SmallInputWrapBenchmark](SmallInputWrapBenchmark/README.md) | Alpha-wrap fallback sizing on very small inputs (`SmallInputWrapTriangles`) |
| [SharpFeatureProbe](SharpFeatureProbe/README.md) | Measure-first probe for sharp-feature recovery in `MeshAlphaWrap` |
| [SolidifyBenchmark](SolidifyBenchmark/README.md) | Time and result hashes for `MeshWindingNumberSolidify` |
| [ReducerBenchmark](ReducerBenchmark/README.md) | Cost of certified deviation bounds (`Reducer.MaxDeviation`, `TriangleDeviationBounds`) and fold-over checks |
| [ReducerRemesherBaseline](ReducerRemesherBaseline/README.md) | Milestone baseline harness for `Reducer` and `Remesher` |
| [RemeshBenchmark](RemeshBenchmark/README.md) | `Remesher` vs. `RemesherPro` under internal library caller settings |
| [RemeshPreventBenchmark](RemeshPreventBenchmark/README.md) | Cost and result hashes of `Remesher.PreventSelfIntersections` |
| [RemeshQualityBenchmark](RemeshQualityBenchmark/README.md) | Quality and timing impact of `Remesher.MinTriangleQuality` and `ApplySafePreset()` |
| [RestrictedDelaunaySpike](RestrictedDelaunaySpike/README.md) | Feasibility spike for restricted Delaunay certified remeshing |
| [ReferenceParts](ReferenceParts/README.md) | Functional CAD-style part suite measuring cumulative error across chained operations |

## 2D Meshing, Tetrahedral & Quad Meshing

| Benchmark | Focus |
|---|---|
| [Meshing2Benchmark](Meshing2Benchmark/README.md) | Acceptance gates, golden rows, and ALKIS/Herne corpora for `DelaunayMesh2` / `ConstrainedDelaunayMesh2` / `QualityMesh2` |
| [OracleIndexPilot](OracleIndexPilot/README.md) | Certified STRtree spatial index performance for the independent 2D meshing oracle |
| [OracleSourcePilot](OracleSourcePilot/README.md) | Constrained-edge source coverage diagnostics for the 2D meshing oracle |
| [TetMeshBenchmark](TetMeshBenchmark/README.md) | `MeshTetrahedralization` insertion rates, dihedral quality, size control, and crease recovery |
| [CrossFieldBenchmark](CrossFieldBenchmark/README.md) | `CrossField` solve times and smoothness on procedural closed shapes |
| [QuadRemeshBenchmark](QuadRemeshBenchmark/README.md) | `CrossField`, `PositionField`, and `QuadRemesher` stage 2 quality and timing |
| [QuadFlow3BGate](QuadFlow3BGate/README.md) | Stage 3B acceptance gate measurements for `QuadRemesher.PureQuads` |
| [QuadRemeshSpike](QuadRemeshSpike/README.md) | Early feasibility prototype for quad remeshing |

## Contouring, Offsets, Lofting & Lattices

| Benchmark | Focus |
|---|---|
| [MarchingCubesBenchmark](MarchingCubesBenchmark/README.md) | Deterministic ordered slab batching performance for `MarchingCubes` |
| [DualContouringHermite](DualContouringHermite/README.md) | Surface accuracy and evaluation cost of `DualContouring.ExactHermite` |
| [DualContouringIntersections](DualContouringIntersections/README.md) | Intersection-free dual contouring evaluation and upper-bound checks |
| [DualContouringSimplification](DualContouringSimplification/README.md) | Adaptive octree simplification triangle count vs. error for `DualContouring` |
| [ContouringCertificate](ContouringCertificate/README.md) | Stage 1–3 topology certification evaluation for contouring |
| [ContouringSubgridProbe](ContouringSubgridProbe/README.md) | Upper bounds for sharp features from distance grids, walls thinner than a cell (subgrid marching tetrahedra) and a subdivision certificate |
| [ContouringTopology](ContouringTopology/README.md) | Upper-bound study for contouring topology guarantees |
| [MeshOffsetBenchmark](MeshOffsetBenchmark/README.md) | `MeshOffset` certified band accuracy and timing vs. SDF Marching Cubes |
| [ContourLoftBenchmark](ContourLoftBenchmark/README.md) | Scaling and quality of `ContourLoft` and `ContourLoftStack` |
| [TpmsLattice](TpmsLattice/README.md) | Stage timings for `TpmsLattice` (gyroid, Schwarz P/D, Neovius, I-WP, Lidinoid) |

## Geodesics, Parameterization & Deformation

| Benchmark | Focus |
|---|---|
| [IntrinsicBenchmark](IntrinsicBenchmark/README.md) | `IntrinsicTriangulation`, `MeshGeodesicDistance`, and `VectorHeatMethod` timings and accuracy |
| [GeodesicOracleBenchmark](GeodesicOracleBenchmark/README.md) | Window counts, interval width, and timing of `ExactMeshGeodesicDistance` and `PolyhedralGeodesicDistance` |
| [ParameterizationBenchmark](ParameterizationBenchmark/README.md) | Timings and distortion metrics for LSCM, BFF, ARAP, SLIM, and Tutte parameterizations |
| [UVAtlasBenchmark](UVAtlasBenchmark/README.md) | `UVAtlasGenerator` quality, packing efficiency, and comparison with `xatlas` |
| [ArapDeformationBenchmark](ArapDeformationBenchmark/README.md) | `ArapMeshDeformer` convergence and runtime on procedural grids |
| [ArapDeformationProbe](ArapDeformationProbe/README.md) | Micro-probe for ARAP covariance SVD building blocks |
| `ArapIncrementalTwist` | Incremental twist stress test for `ArapMeshDeformer` (`Program.cs`) |
| [BiharmonicDeformationBenchmark](BiharmonicDeformationBenchmark/README.md) | `BiharmonicMeshDeformer` runtime and accuracy on procedural grids |

## Spatial Queries, Terrain, 2D Polygons, Solvers & Core

| Benchmark | Focus |
|---|---|
| [BatchQueryBenchmark](BatchQueryBenchmark/README.md) | Throughput of `MeshBatchQueries` (rays, nearest points, inside tests) |
| [RayQueryBenchmark](RayQueryBenchmark/README.md) | Per-ray timing of `DMeshAABBTree3.FindNearestHitTriangle` and exact `IsInside` |
| [ExactWindingBenchmark](ExactWindingBenchmark/README.md) | Before/after build, query, and grid timings of `ExactWindingCache` (`DMeshAABBTree3.WindingNumber` & `MeshWindingNumberGrid`) |
| [FastWindingCacheBenchmark](FastWindingCacheBenchmark/README.md) | Cache build and query timing for `DMeshAABBTree3.FastWindingNumber` |
| [HausdorffBenchmark](HausdorffBenchmark/README.md) | `MeshHausdorffDistance` default vs. certified upper bounds |
| [CcdBenchmark](CcdBenchmark/README.md) | `ContinuousCollision` and `MeshSweepCollision` throughput and interval-path scaling |
| [QuadtreeBenchmark](QuadtreeBenchmark/README.md) | `LooseQuadtree2d` / `PointQuadtree2d` / `SegmentQuadtree2d` / `TriangleQuadtree2d` queries |
| [TerrainBenchmark](TerrainBenchmark/README.md) | Terrain slope design, DGM-to-TIN, offsets, safety lines, and cell volumes on open geodata |
| [Clipper2Benchmark](Clipper2Benchmark/README.md) | 2D polygon offsets and booleans via `addons/geometry3Sharp.Clipper2` |
| [SparseCholeskyBenchmark](SparseCholeskyBenchmark/README.md) | `SparseCholesky` AMD ordering, symbolic/numeric factorization, and solve timings |
| [CoreMathBenchmark](CoreMathBenchmark/README.md) | Cost of correctly rounded `g3.CoreMathPort.CoreMath` elementary functions |
| [BoxContainBenchmark](BoxContainBenchmark/README.md) | Throughput micro-benchmark for `AxisAlignedBox3d.Contain` |
| [GridIndexerBenchmark](GridIndexerBenchmark/README.md) | Throughput probe for `ShiftGridIndexer3.ToGrid` |
| [LongHashAudit](LongHashAudit/README.md) | Hash collision and distribution audit for `long`-keyed tables outside booleans |
| [TextFormatBenchmark](TextFormatBenchmark/README.md) | Coordinate formatting throughput of `MeshIOUtil.FormatDouble` / `FormatFloat` (`ShortestDecimal`) |

## Cross-Runtime, Verification & Shared Helpers

| Directory | Focus |
|---|---|
| [net48-compare](net48-compare/README.md) | Side-by-side runtime comparison of `.NET Framework 4.8` vs. `.NET 10.0` |
| [MetamorphicFalsification](MetamorphicFalsification/README.md) | Metamorphic relation falsification runs over core geometry operations |
| `Shared` | Shared evaluation helpers (`FidelityFields.cs`, `UvEvaluation.cs`) reused by benchmarks and tests |
