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
| [OpenCuttingSurface](OpenCuttingSurface/README.md) | Prototype of the candidate semantics for a Difference with an open cutting surface (#1211): existing parts, sheet role, winding rules, swept tool |
| [PredicateComparison](PredicateComparison/README.md) | Accuracy and speed of `orient2d`, `incircle`, and `orient3d` implementations vs. exact reference |
| [SelfIntersectionBenchmark](SelfIntersectionBenchmark/README.md) | Cost of exact self-intersection checks and `Reducer.PreventSelfIntersections` |
| [SelfIntersectionUntangle](SelfIntersectionUntangle/README.md) | Spike: a connectivity-preserving untangling prototype against `MeshRobustRepair`, the exact resolve, the solidify and the alpha wrap on procedural self-intersecting solids |
| [SnapRoundingExperiment](SnapRoundingExperiment/README.md) | Snap-rounding prototypes and quality measurements for `SAFE_ROUNDING.md` |
| [SolidBooleanAudit](SolidBooleanAudit/README.md) | Adversarial procedural corpus and an independent oracle (ray crossings, volumes) for an end-to-end audit of `SolidBooleanPipeline` |
| [SolidBooleanBenchmark](SolidBooleanBenchmark/README.md) | `SolidBooleanPipeline` (exact boolean + guarded reduction + contact pairing) on procedural soups |
| [NeuronSkeletonProbe](NeuronSkeletonProbe/README.md) | Research probe: SWC-style branching tubes (4° forks, 1e-6 branch gaps, loops, 45-segment trees) closed by multi-mesh and tree `ExactMeshBoolean`, `SolidBooleanPipeline`, convolution-field `MarchingCubes` and `MeshAlphaWrap`, with measured mesh clearances |
| [ProceduralSphereUnionProbe](ProceduralSphereUnionProbe/README.md) | Research probe: unions of procedural ball sets (tangency, 1e-12 gaps, cavities, helices, 12–96-ball packings) by multi-mesh and tree `ExactMeshBoolean`, `SolidBooleanPipeline`, `MarchingCubes` and `MeshAlphaWrap` |

## Repair, Alpha Wrap, Remeshing & Simplification

| Benchmark | Focus |
|---|---|
| [MeshRepairBenchmark](MeshRepairBenchmark/README.md) | `MeshAutoRepair` and `MeshRobustRepair` evaluation on Thingi10K and procedural failure cases |
| [NotCleanBenchmark](NotCleanBenchmark/README.md) | Runtime of the checks behind `MeshRobustRepair.NotCleanReason` on large procedural meshes |
| [OpenSoupWrapBenchmark](OpenSoupWrapBenchmark/README.md) | Alpha-wrap size estimation on open inputs and `WrapMaxEstimatedTriangles` budget |
| [SmallInputWrapBenchmark](SmallInputWrapBenchmark/README.md) | Alpha-wrap fallback sizing on very small inputs (`SmallInputWrapTriangles`) |
| [CertifiedMedialAxisProbe](CertifiedMedialAxisProbe/README.md) | Why a certified lower bound on local feature size cannot replace SafeRounding's thin-solid criteria (crease witnesses, thin slabs) |
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
| [Meshing2HardProblems](Meshing2HardProblems/README.md) | Research spike prototypes: reference small-angle refinement rules (HP 6) and a certifying checker against the 2D meshing oracle (HP 7) |
| [OracleSourcePilot](OracleSourcePilot/README.md) | Constrained-edge source coverage diagnostics for the 2D meshing oracle |
| [TetMeshBenchmark](TetMeshBenchmark/README.md) | `MeshTetrahedralization` insertion rates, dihedral quality, size control, and crease recovery |
| [CrossFieldBenchmark](CrossFieldBenchmark/README.md) | `CrossField` solve times and smoothness on procedural closed shapes |
| [QuadRemeshBenchmark](QuadRemeshBenchmark/README.md) | `CrossField`, `PositionField`, and `QuadRemesher` stage 2 quality and timing |
| [QuadFlow3BGate](QuadFlow3BGate/README.md) | Stage 3B acceptance gate measurements for `QuadRemesher.PureQuads` |
| [QuadIntegerJumps](QuadIntegerJumps/README.md) | Research spike: integer jumps on the frozen edges of the stage 3B flow of `QuadRemesher.PureQuads` (relaxed bi-directed flow), against the gate runs |
| [QuadRemeshSpike](QuadRemeshSpike/README.md) | Early feasibility prototype for quad remeshing |
| [QuadSeamlessSeedSpike](QuadSeamlessSeedSpike/README.md) | Upper bound of a seamless-parametrization seed (cone metric, two scales) for `PositionField` and `QuadRemesher` |
| [FilletDetection](FilletDetection/README.md) | Research spike: fillet detectors (curvature, DeFillet's Voronoi rolling balls, medial balls) and sharp-edge reconstruction on procedural filleted CAD solids with a known answer |
| [QuadSheafObstruction](QuadSheafObstruction/README.md) | Research spike: sheaf-Laplacian and area-invariant diagnostics of the stage 3B fallbacks of `QuadRemesher.PureQuads` |
| [QuadFlow3BSat](QuadFlow3BSat/README.md) | Research spike: the local SAT instances of the stage 3B repair decided by a clause-learning solver (search or problem), DIMACS dump of the obstructions |

## Contouring, Offsets, Lofting & Lattices

| Benchmark | Focus |
|---|---|
| [MarchingCubesBenchmark](MarchingCubesBenchmark/README.md) | Deterministic ordered slab batching performance for `MarchingCubes` |
| [DualContouringHermite](DualContouringHermite/README.md) | Surface accuracy and evaluation cost of `DualContouring.ExactHermite` |
| [DualContouringIntersections](DualContouringIntersections/README.md) | Intersection-free dual contouring evaluation and upper-bound checks |
| [DualContouringSimplification](DualContouringSimplification/README.md) | Adaptive octree simplification triangle count vs. error for `DualContouring` |
| [ContouringCertificate](ContouringCertificate/README.md) | Stage 1–3 topology certification evaluation for contouring |
| [ContouringSmoothCertificate](ContouringSmoothCertificate/README.md) | Cost and yield of the internal topology certificate for contours of smooth implicit fields (capped TPMS functions) |
| [ContouringSubgridProbe](ContouringSubgridProbe/README.md) | Upper bounds for sharp features from distance grids, walls thinner than a cell (subgrid marching tetrahedra) and a subdivision certificate |
| [ContouringIsotopySubdivision](ContouringIsotopySubdivision/README.md) | A subdivision isotopy certificate (C0 / C1 cells, outward-rounded intervals) for `DualContouring` of implicit fields, against Theorem 5's cell test and the class P certificate |
| [ContouringTopology](ContouringTopology/README.md) | Upper-bound study for contouring topology guarantees |
| [BranchingTubeProbe](BranchingTubeProbe/README.md) | Research probe: genus of a tapered branching-tube fixture with a `1/8` or `1/128` tip gap under aligned and shifted `MarchingCubes`, strict `ExactMeshBoolean` and `MeshAlphaWrap`, with `NotCleanReason` per result |
| [MeshOffsetBenchmark](MeshOffsetBenchmark/README.md) | `MeshOffset` certified band accuracy and timing vs. SDF Marching Cubes |
| [ContourLoftBenchmark](ContourLoftBenchmark/README.md) | Scaling and quality of `ContourLoft` and `ContourLoftStack` |
| [TpmsLattice](TpmsLattice/README.md) | Stage timings for `TpmsLattice` (gyroid, Schwarz P/D, Neovius, I-WP, Lidinoid) |

## Geodesics, Parameterization & Deformation

| Benchmark | Focus |
|---|---|
| [IntrinsicBenchmark](IntrinsicBenchmark/README.md) | `IntrinsicTriangulation`, `MeshGeodesicDistance`, and `VectorHeatMethod` timings and accuracy |
| [IntrBox3Benchmark](IntrBox3Benchmark/README.md) | Ray, line and segment against box classes (exact decision) and the `DMeshAABBTree3` / `DCurve3BoxTree` ray queries that call them, before and after |
| [ExactToDoubleBenchmark](ExactToDoubleBenchmark/README.md) | `ExactPoint3.ToDouble` (num / den * 2^e, correctly rounded since #1410) in isolation and `ExactPoint3.ToVector3d`, ns per call, before and after |
| [IntrinsicIntegerCoordinates](IntrinsicIntegerCoordinates/README.md) | Research spike: integer (normal) coordinates against the traced common subdivision of `IntrinsicTriangulation` |
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
| [SparseSquareBenchmark](SparseSquareBenchmark/README.md) | `SymmetricSparseMatrix.Square` / `Multiply` timings and the product bits of repeated runs |
| [MultiscaleCholeskyProbe](MultiscaleCholeskyProbe/README.md) | Research spike: multiscale incomplete Cholesky, FSAI and approximate elimination as PCG preconditioners against the direct `SparseCholesky` |
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
| [MetamorphicBugHunt](MetamorphicBugHunt/README.md) | Metamorphic relations and operation chains (Boolean, repair, remesh, tet, wrap, contouring, 2D mesher; second hunt: `PassageWrap`, feature pass, quad, `TpmsLattice`, `ContourLoft`, readers and writers, geodesics, intrinsic triangulation) on procedural inputs with independent checks of every Success |
| [MagnitudeRobustness](MagnitudeRobustness/README.md) | Probes of the magnitude study: geodesic and intrinsic classes and `DualContouring` at 2^e, root-cause expressions, an exact prescaling wrapper and its cost |
| [TopologyOwnershipProbe](TopologyOwnershipProbe/README.md) | Ordered winding landmarks vs. Betti numbers and shell volumes as a check of where a cavity ends up after a Boolean or forced repair |
| `Shared` | Shared evaluation helpers (`FidelityFields.cs`, `UvEvaluation.cs`) reused by benchmarks and tests |
