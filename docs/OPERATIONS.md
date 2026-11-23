# Operations Guide & Master Index

A task-oriented index of geometry3Sharp operations: which guide to read, which class to call, and which compiled example demonstrates it.

- **Compiled, test-verified examples:** Every file in [examples/](examples/) is compiled into the test suite and checked by `tests/geometry3Sharp.Tests/DocExamplesTests.cs` against its documented guarantees.
- **Namespaces:** Core types live in `g3`; many mesh-processing classes live in `gs`. Include both: `using g3; using gs;`.
- **Headless CLI & MCP:** Run operations on files with JSON output or via MCP (`g3cli mcp`) using [tools/g3cli](../tools/g3cli/README.md).
- **API reference:** XML documentation ships in the NuGet package (`.xml`) alongside the assemblies.

---

## Topic Guides

| Guide | Topics covered |
|---|---|
| [Getting Started](guides/getting-started.md) | Project file (`geometry3Sharp_netstandard.csproj`), targets, `g3` vs. `gs`, `DMesh3` conventions, Unity interop |
| [File Formats & I/O](guides/file-formats.md) | OBJ, STL, OFF, PLY, 3MF, glTF/GLB, XYZ, g3mesh, UTM precision, `PolyMeshIO`, `TetMeshWriter`, `HeightGridReader` |
| [Booleans & Imprinting](guides/booleans-and-imprint.md) | `ExactMeshBoolean`, `ExactMesh` chains, `SolidBooleanPipeline`, `ExactMeshImprint`, validation checklist |
| [Repair & Wrapping](guides/repair-and-wrapping.md) | `MeshAutoRepair`, `MeshRobustRepair`, hole fillers, `MeshWindingNumberSolidify`, `MeshAlphaWrap`, `PassageWrap` |
| [Remeshing & Deformation](guides/remeshing-and-deformation.md) | `Remesher`/`Pro`, `Reducer`, `PartitionedReducer`, `MeshEnvelope`, `QuadRemesher`, smoothing, ARAP, Biharmonic, `MeshPlaneCut` |
| [2D & Tetrahedral Meshing](guides/meshing-2d-and-tet.md) | `DelaunayMesh2`, `ConstrainedDelaunayMesh2`, `QualityMesh2`, `Clipper2Polygons`, `PlanarComplex`, `MeshTetrahedralization` |
| [Contouring & Offsets](guides/contouring-and-offsets.md) | `MarchingCubes`, `DualContouring`, `MeshOffset`, `ContourLoft`, `ContourLoftStack`, `TpmsLattice` |
| [Geodesics & UV](guides/geodesics-and-uv.md) | `IntrinsicTriangulation`, heat & exact polyhedral geodesics, `VectorHeatMethod`, `MeshGeodesicPath`, `UVAtlasGenerator`, parameterizers |
| [Analysis & Queries](guides/analysis-and-queries.md) | `DMeshAABBTree3`, `MeshBatchQueries`, 2D loose quadtrees, Hausdorff, deviation, fidelity, curvature, wall thickness, `PrimitiveFit3`, CCD |
| [Terrain & Point Clouds](guides/terrain-and-pointclouds.md) | `HeightGrid`, `TerrainGridTin`, `TerrainSlopeDesign`, `TerrainCellVolumes`, `ClothSimulationFilter`, `PointCloudNormals`, `PointCloudSurface` |

---

## Operations

| Task | Main classes | Guide | Example |
|---|---|---|---|
| Build meshes, procedural generators | `DMesh3`, `TrivialBox3Generator`, `GridBox3Generator`, `Sphere3Generator_NormalizedCube`, `CappedCylinderGenerator` | [Getting Started](guides/getting-started.md) | [MeshConstructionExample](examples/MeshConstructionExample.cs) |
| Read / write OBJ, STL, OFF, PLY, 3MF, glTF/GLB, XYZ | `StandardMeshReader`, `StandardMeshWriter`, `PLYReader`/`Writer`, `ThreeMFReader`/`Writer`, `GLTFReader`/`Writer` | [File Formats](guides/file-formats.md) | [MeshIOExample](examples/MeshIOExample.cs) |
| Exact booleans (binary, n-ary, winding predicates, resolve) | `ExactMeshBoolean`, `ExactMeshBoolean.AtLeast`, `ExactMeshBoolean.Resolve` | [Booleans](guides/booleans-and-imprint.md) | [ExactMeshBooleanExample](examples/ExactMeshBooleanExample.cs) |
| Lazy exact boolean chains (single final rounding) | `ExactMesh` (`FromDMesh`, `Union`, `Difference`, `Intersection`, `Round`) | [Booleans](guides/booleans-and-imprint.md) | [LazyExactMeshExample](examples/LazyExactMeshExample.cs) |
| Soup-to-solid boolean + reduction pipeline | `gs.SolidBooleanPipeline`, `gs.SolidBooleanReport` | [Booleans](guides/booleans-and-imprint.md) | [SolidBooleanPipelineExample](examples/SolidBooleanPipelineExample.cs) |
| Extruded cut with an open sheet (a sheet that ends inside the solid) | `MeshExtrudeMesh` + `ExactMeshBoolean` (a recipe, no own class) | [Booleans](guides/booleans-and-imprint.md) | [SweptCutExample](examples/SweptCutExample.cs) |
| Exact surface imprinting (without classification) | `ExactMeshImprint` | [Booleans](guides/booleans-and-imprint.md) | [SolidBooleanPipelineExample](examples/SolidBooleanPipelineExample.cs) |
| Repair (topology-preserving or guaranteed clean) | `gs.MeshAutoRepair`, `gs.MeshPostRepairCleanup`, `gs.MeshRobustRepair` | [Repair & Wrapping](guides/repair-and-wrapping.md) | [MeshRepairExample](examples/MeshRepairExample.cs) |
| Fill boundary holes | `gs.MinimalHoleFill`, `gs.SmoothedHoleFill`, `gs.AutoHoleFill`, `PlanarHoleFiller`, `SimpleHoleFiller` | [Repair & Wrapping](guides/repair-and-wrapping.md) | [HoleFillExample](examples/HoleFillExample.cs) |
| Check mesh cleanliness & defects | `gs.MeshRepairMetrics`, `MeshRobustRepair.NotCleanReason`, `DMesh3.CheckValidity` | [Repair & Wrapping](guides/repair-and-wrapping.md) | [RepairMetricsExample](examples/RepairMetricsExample.cs) |
| Rebuild soup / broken mesh as closed solid | `gs.MeshWindingNumberSolidify`, `gs.MeshWindingNumberBoolean` | [Repair & Wrapping](guides/repair-and-wrapping.md) | [WindingNumberSolidifyExample](examples/WindingNumberSolidifyExample.cs) |
| Alpha wrapping & clearance-certified passage wrapping | `gs.MeshAlphaWrap`, `gs.PassageWrap` | [Repair & Wrapping](guides/repair-and-wrapping.md) | [AlphaWrapExample](examples/AlphaWrapExample.cs) |
| Isotropic triangle remeshing | `Remesher`, `gs.RemesherPro`, `MeshConstraintUtil`, `MeshProjectionTarget` | [Remeshing](guides/remeshing-and-deformation.md) | [RemeshExample](examples/RemeshExample.cs) |
| Simplification with certified deviation & envelopes | `Reducer`, `gs.PartitionedReducer`, `gs.MeshEnvelope` | [Remeshing](guides/remeshing-and-deformation.md) | [SimplificationExample](examples/SimplificationExample.cs) |
| Quad-dominant & pure-quad remeshing | `gs.QuadRemesher`, `DPolyMesh`, `PolyMeshIO` | [Remeshing](guides/remeshing-and-deformation.md) | [QuadRemeshExample](examples/QuadRemeshExample.cs) |
| Surface smoothing | `MeshIterativeSmooth`, `LaplacianMeshSmoother` | [Remeshing](guides/remeshing-and-deformation.md) | [SmoothingExample](examples/SmoothingExample.cs) |
| Handle-based mesh deformation | `ArapMeshDeformer`, `BiharmonicMeshDeformer`, `LaplacianMeshDeformer` | [Remeshing](guides/remeshing-and-deformation.md) | [DeformationExample](examples/DeformationExample.cs) |
| Plane cut & cap | `MeshPlaneCut` | [Remeshing](guides/remeshing-and-deformation.md) | [PlaneCutExample](examples/PlaneCutExample.cs) |
| 2D Delaunay, constrained & quality meshing | `DelaunayMesh2`, `ConstrainedDelaunayMesh2`, `QualityMesh2`, `ConstrainedMesh2` | [2D & Tet Meshing](guides/meshing-2d-and-tet.md) | [Meshing2DExample](examples/Meshing2DExample.cs) |
| 2D polygon booleans, offsets, Minkowski (optional add-on) | `g3.Clipper2.Clipper2Polygons`, `ClipperFrame`, `Clipper2Options` | [2D & Tet Meshing](guides/meshing-2d-and-tet.md) | [Clipper2Example](examples/Clipper2/Clipper2Example.cs) |
| 3D tetrahedral meshing | `gs.MeshTetrahedralization`, `gs.TetMeshMarkers`, `gs.TetMeshWriter` | [2D & Tet Meshing](guides/meshing-2d-and-tet.md) | [TetMeshExample](examples/TetMeshExample.cs) |
| Iso-surface contouring (MC & Dual Contouring) | `MarchingCubes`, `DualContouring`, `gs.MarchingCubesPro`, `MeshSignedDistanceGrid` | [Contouring & Offsets](guides/contouring-and-offsets.md) | [OffsetSurfaceExample](examples/OffsetSurfaceExample.cs), [DualContouringAndOffsetExample](examples/DualContouringAndOffsetExample.cs) |
| Certified mesh offset / inset | `gs.MeshOffset` | [Contouring & Offsets](guides/contouring-and-offsets.md) | [DualContouringAndOffsetExample](examples/DualContouringAndOffsetExample.cs) |
| Contour lofting (single pair & multi-slice stacks) | `ContourLoft`, `ContourLoftStack` | [Contouring & Offsets](guides/contouring-and-offsets.md) | [ContourLoftExample](examples/ContourLoftExample.cs) |
| Walls thinner than a grid cell (shells, sheets) | `TwoLevelMarchingTetrahedra` | [Contouring & Offsets](guides/contouring-and-offsets.md) | [TwoLevelMarchingTetrahedraExample](examples/TwoLevelMarchingTetrahedraExample.cs) |
| TPMS lattices (gyroid, Schwarz, Neovius, I-WP, Lidinoid) | `TpmsLattice` | [Contouring & Offsets](guides/contouring-and-offsets.md) | [TpmsLatticeExample](examples/TpmsLatticeExample.cs) |
| Hausdorff & heat geodesic distances | `gs.MeshHausdorffDistance`, `gs.MeshGeodesicDistance` | [Geodesics & UV](guides/geodesics-and-uv.md) | [DistancesExample](examples/DistancesExample.cs) |
| Exact polyhedral geodesics & shortest paths | `gs.PolyhedralGeodesicDistance`, `gs.PolyhedralGeodesicPath` | [Geodesics & UV](guides/geodesics-and-uv.md) | — |
| Intrinsic triangulations, geodesic paths & Bezier curves | `gs.IntrinsicTriangulation`, `gs.VectorHeatMethod`, `gs.MeshGeodesicPath`, `gs.MeshGeodesicBezier` | [Geodesics & UV](guides/geodesics-and-uv.md) | [IntrinsicTriangulationExample](examples/IntrinsicTriangulationExample.cs) |
| UV atlases, disc cuts, parameterizations & UV untangling | `UVAtlasGenerator`, `MeshDiscCut`, `BoundaryFirstFlattening`, `LSCM`/`ARAP`/`SLIM`/`FoldoverFreeParameterization` | [Geodesics & UV](guides/geodesics-and-uv.md) | [UVAtlasExample](examples/UVAtlasExample.cs) |
| Volume, area, bounds, edge stats, genus | `MeshMeasurements`, `MeshQueries` | [Analysis & Queries](guides/analysis-and-queries.md) | [MeasurementsExample](examples/MeasurementsExample.cs) |
| 3D spatial tree, ray hits, inside tests, batch queries | `DMeshAABBTree3`, `MeshBatchQueries` | [Analysis & Queries](guides/analysis-and-queries.md) | [SpatialQueriesExample](examples/SpatialQueriesExample.cs), [BatchQueriesAndCcdExample](examples/BatchQueriesAndCcdExample.cs) |
| 3D nearest neighbors among points | `PointHashGrid3d<T>`, `PointAABBTree3` | [Analysis & Queries](guides/analysis-and-queries.md) | [PointQueriesExample](examples/PointQueriesExample.cs) |
| 2D loose quadtrees & terrain height queries | `LooseQuadtree2d`, `PointQuadtree2d`, `SegmentQuadtree2d`, `TriangleQuadtree2d` | [Analysis & Queries](guides/analysis-and-queries.md) | [QuadtreeQueriesExample](examples/QuadtreeQueriesExample.cs) |
| Mesh comparison, fidelity, curvature, quality & topology | `gs.MeshFidelityMetrics`, `gs.MeshCurvature`, `gs.TriangleQualityStatistics`, `gs.MeshTopologySummary` | [Analysis & Queries](guides/analysis-and-queries.md) | [FidelityMetricsExample](examples/FidelityMetricsExample.cs) |
| Deviation maps, wall thickness, primitive fit, CCD | `gs.MeshDeviation`, `gs.MeshWallThickness`, `PrimitiveFit3`, `ContinuousCollision` | [Analysis & Queries](guides/analysis-and-queries.md) | [AnalysisBuildingBlocksExample](examples/AnalysisBuildingBlocksExample.cs), [BatchQueriesAndCcdExample](examples/BatchQueriesAndCcdExample.cs) |
| Certified terrain slope design | `gs.TerrainSlopeDesign`, `SlopeToe`, `SlopeSpec`, `SlopeRamp` | [Terrain & Point Clouds](guides/terrain-and-pointclouds.md) | [TerrainSlopeExample](examples/TerrainSlopeExample.cs) |
| Height grids (DGM/DTM), certified TINs & cell volumes | `HeightGridReader`, `gs.TerrainGridTin`, `gs.TerrainCellVolumes`, `gs.ClothSimulationFilter` | [Terrain & Point Clouds](guides/terrain-and-pointclouds.md) | [TerrainToolsExample](examples/TerrainToolsExample.cs) |
| Point cloud normals & surface reconstruction | `gs.PointCloudNormals`, `gs.PointCloudSurface`, `gs.MeshAlphaWrap` | [Terrain & Point Clouds](guides/terrain-and-pointclouds.md) | [PointCloudExample](examples/PointCloudExample.cs) |

---

## Quick Routing

### Which repair or boolean class?

- **Booleans & imprinting:** See [Booleans & Surface Imprinting](guides/booleans-and-imprint.md) (`ExactMeshBoolean`, `ExactMesh`, `SolidBooleanPipeline`, `ExactMeshImprint`, and the 7-step solid validation checklist).
- **Repair & wrapping:** See [Mesh Repair, Hole Filling & Wrapping](guides/repair-and-wrapping.md) (decision tree covering `MeshAutoRepair`, `MeshRobustRepair`, `MeshWindingNumberSolidify`, `MeshAlphaWrap`, and `PassageWrap`).

### Booleans

Full usage, `SafeRounding` contracts, `SolidBooleanPipeline`, and n-ary operations are documented in [guides/booleans-and-imprint.md](guides/booleans-and-imprint.md).

### 2D polygon booleans and offsets (optional package)

Full documentation for `geometry3Sharp.Clipper2` (`Clipper2Polygons`, `ClipperFrame`, `Clipper2Options`, `Clipper2Result`) is in [guides/meshing-2d-and-tet.md#2d-polygon-booleans--offsets-geometry3sharpclipper2](guides/meshing-2d-and-tet.md#2d-polygon-booleans--offsets-geometry3sharpclipper2) and [addons/geometry3Sharp.Clipper2/README.md](../addons/geometry3Sharp.Clipper2/README.md).

### Command line and MCP (g3cli)

All headless CLI commands (`measure`, `repair`, `boolean`, `resolve`, `remesh`, `simplify`, `uv`, `convert`, `mesh2d`, `slope`, `dgm`, `dgm_tin`, `cell_volumes`, `query2d`, `fidelity`, `quality`, `curvature`, `offset`, `wrap`, `tetmesh`, `quad`, `tpms`, `geodesic`, `query3d`, `deviation`, `thickness`, `fit`, `polygon2d`, `polygon_solids`, `hull2d`) and the MCP server (`g3cli mcp`) are documented in [tools/g3cli/README.md](../tools/g3cli/README.md).

---

## Adding an example

1. Add `docs/examples/<Name>Example.cs`: a `public static class` in namespace `g3.Examples` with plain library calls and short comments (compiled into the test project only, not into the library).
2. Add a `[Fact]` test `<Name>Example_<WhatItChecks>` to `tests/geometry3Sharp.Tests/DocExamplesTests.cs` that runs the example and asserts the documented behavior.
3. Link `examples/<Name>Example.cs` from the Operations table above and from the corresponding guide in `docs/guides/`. `DocExamplesTests.EveryExample_IsTestedAndLinked` enforces steps 2 and 3.
