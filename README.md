# geometry3Sharp

Boost-licensed C# library for 2D/3D geometric computing, exact mesh booleans, robust mesh repair, remeshing, meshing, parameterization, geodesics, and spatial queries.

This repository is an actively developed fork of [gradientspace/geometry3Sharp](https://github.com/gradientspace/geometry3Sharp) (`master`, NuGet `1.0.324`). It maintains public API compatibility with upstream `1.0.324` (enforced on `dotnet pack`) while targeting modern .NET (`net10.0` + `netstandard2.0`) and adding exact predicates, deterministic algorithms, tested examples, and a headless CLI/MCP layer.

> **Start here:** [Documentation Hub](docs/README.md) · [Operations Guide & Tested Examples](docs/OPERATIONS.md) · [CLI & MCP (`g3cli`)](tools/g3cli/README.md) · [Changes vs. Upstream 1.0.324](docs/UPSTREAM_CHANGES.md) · [Roadmap](docs/ROADMAP.md)

---

## Quickstart

Reference **`geometry3Sharp_netstandard.csproj`** (targets `net10.0` and `netstandard2.0`; the legacy `geometry3Sharp.csproj` is outdated and kept only for historical reference).

Most core types live in namespace `g3`, while many mesh-processing operations live in `gs`. Include both:

```csharp
using g3;
using gs;

// Read a mesh (OBJ, STL, OFF, PLY, 3MF, glTF/GLB, XYZ, g3mesh)
DMesh3 a = StandardMeshReader.ReadMesh("part_a.glb");
DMesh3 b = StandardMeshReader.ReadMesh("part_b.3mf");

// Exact mesh boolean with SafeRounding (closed, manifold, self-intersection-free)
var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference);
if (op.Compute() && op.RoundingClean)
    StandardMeshWriter.WriteMesh("result.ply", op.Result, WriteOptions.Defaults);
```

See [docs/OPERATIONS.md](docs/OPERATIONS.md) for compiled, test-verified examples covering repair, booleans, remeshing, simplification, UV atlases, geodesics, spatial queries, and terrain tools.

---

## Capabilities Overview

### Exact Geometry, Booleans & Mesh Repair
- **Exact predicates & arithmetic:** `ExactPredicates` (`Orient2d`, `Orient3d`, `InCircle`, `InSphere`) and fixed-width indirect predicates; bit-identical deterministic math (`math/CoreMath`).
- **Exact mesh booleans & imprinting:** `ExactMeshBoolean` (union, intersection, difference, n-ary winding predicates, `Resolve`, `SafeRounding`, optional exact rational coordinates), lazy exact boolean chains (`ExactMesh`), soup-to-solid pipeline (`SolidBooleanPipeline`), and exact surface imprinting (`ExactMeshImprint`).
- **Mesh repair & wrapping:** `MeshAutoRepair` + `MeshPostRepairCleanup`, guaranteed-clean `MeshRobustRepair`, `MeshWindingNumberSolidify`, `MeshWindingNumberBoolean`, `MeshAlphaWrap` (Portaneri et al. 2022, built on exact `Delaunay3`), and clearance-certified `PassageWrap`.
- **Hole filling & validation:** `MinimalHoleFill`, `SmoothedHoleFill`, `AutoHoleFill`, `PlanarHoleFiller`, `SimpleHoleFiller`, `MeshRepairMetrics`, `MeshRobustRepair.NotCleanReason`.

### Meshing, Remeshing, Contouring & Deformation
- **2D Delaunay & quality meshing:** `DelaunayMesh2`, `ConstrainedDelaunayMesh2` (segments, holes, crossing splits), and `QualityMesh2` (Ruppert/Shewchuk refinement with exact predicates and 2.5D `ToDMesh3()`).
- **3D tetrahedral & quad remeshing:** `MeshTetrahedralization` (with `TetMeshMarkers` and Medit/Gmsh/VTK `TetMeshWriter`), `QuadRemesher` (`CrossField`, `PositionField`, optional `PureQuads`, `DPolyMesh`, `PolyMeshIO`).
- **Isotropic remeshing, simplification & smoothing:** `Remesher` (with opt-in `ApplySafePreset()`), `RemesherPro`, `RegionRemesher`, `EdgeLoopRemesher`, QEM `Reducer` (certified `MaxDeviation`, `PreventSelfIntersections`, `PreventFoldOver`), `MeshEnvelope`, `MeshIterativeSmooth`, `LaplacianMeshSmoother`.
- **Mesh deformation:** `ArapMeshDeformer` (as-rigid-as-possible) and `BiharmonicMeshDeformer` (cotangent biharmonic handles), plus `LaplacianMeshDeformer`.
- **Contouring, offsets, lofting & lattices:** `MarchingCubes`, `DualContouring` (exact Hermite data, adaptive simplification, self-intersection resolution), `MeshOffset` (certified offset/inset band), `ContourLoft` / `ContourLoftStack`, and `TpmsLattice` (gyroid, Schwarz P/D, Neovius, I-WP, Lidinoid).

### Surface Processing, Geodesics & Parameterization
- **Intrinsic triangulations & geodesics:** `IntrinsicTriangulation` (intrinsic Delaunay flips, signposts, refinement, `IntrinsicCommonSubdivision`), `MeshGeodesicDistance` (heat method), `PolyhedralGeodesicDistance` / `PolyhedralGeodesicPath` (exact polyhedral shortest paths), `VectorHeatMethod` (parallel transport, log map), `MeshGeodesicPath` (FlipOut), and `MeshGeodesicBezier`.
- **UV atlases & parameterization:** `UVAtlasGenerator` (`MeshChartSegmentation`, `MeshDiscCut`, `ConeFlattening`, `UVOverlap`, `UVPacker`), `BoundaryFirstFlattening`, `LSCMParameterization`, `SpectralConformalParameterization`, `ARAPParameterization`, `SLIMParameterization`, `TutteEmbedding`, `FoldoverFreeParameterization`, and `ParameterizationDistortion`.

### Analysis, Spatial Queries, Terrain & Point Clouds
- **Spatial indexes & collision:** `DMeshAABBTree3` (watertight ray hits, exact `IsInside`, scale-free `FastWindingNumber`), `PointAABBTree3`, `MeshBatchQueries`, `LooseQuadtree2d` / `PointQuadtree2d` / `SegmentQuadtree2d` / `TriangleQuadtree2d`, `PointHashGrid2d/3d`, `DSparseGrid3`, `MeshSignedDistanceGrid`, `ContinuousCollision` / `MeshSweepCollision`.
- **Analysis & quality metrics:** `MeshHausdorffDistance`, `MeshDeviation` (signed deviation maps with certified Hausdorff bounds), `MeshWallThickness` (ray, SDF, inscribed ball), `MeshFidelityMetrics` (F-score, normal consistency, edge Chamfer, volume IoU, curvature fidelity), `MeshCurvature`, `TriangleQualityStatistics`, `MeshTopologySummary`, `PrimitiveFit3` (plane, sphere, cylinder, cone via RANSAC + least squares).
- **Terrain & point clouds:** `TerrainSlopeDesign`, `TerrainGridTin` + `HeightGridReader`/`HeightGridWriter` (DGM/DTM), `TerrainCellVolumes`, `TerrainVolumeDifference`, `TerrainDualContouring`, `TerrainSafetyLines`, `ClothSimulationFilter`, `PointCloudNormals`, and `PointCloudSurface`.

### Core Math, Curves, Solvers & Add-ons
- **Foundations:** `DMesh3`, `SimpleMesh`, `DSubmesh3`, `DPolyMesh`, `EdgeLoop`/`EdgeSpan`, `MeshEditor`, `MeshMeasurements`, `MeshTransforms`, vector/matrix/quaternion/frame structs (`Vector2d/3d`, `Frame3d/f`, `AxisAlignedBox2d/3d`, `Box2d/3d`), 2D/3D curves (`Polygon2d`, `GeneralPolygon2d`, `NURBSCurve2`, `PlanarComplex`, `DGraph2/3`), and direct/iterative linear solvers (`SparseCholesky`, `CholeskyDecomposition`, `SparseSymmetricCG`, `SingularValueDecomposition`, `SymmetricEigenSolver`).
- **2D polygon booleans & offsets (optional add-on):** [`addons/geometry3Sharp.Clipper2`](addons/geometry3Sharp.Clipper2/README.md) — robust 2D union/intersection/difference/xor, polyline clipping, offsets, and Minkowski sums on top of vendored [Clipper2](https://github.com/AngusJohnson/Clipper2) (Boost-1.0), keeping the core package dependency-free.
- **Headless CLI & MCP server:** [`tools/g3cli`](tools/g3cli/README.md) — exposes mesh, 2D, terrain, analysis, and conversion workflows as a JSON CLI and MCP server (`g3cli mcp`).

---

## Build, Targets & Tests

- **Project file:** `geometry3Sharp_netstandard.csproj`
- **Target frameworks:** `net10.0` and `netstandard2.0` (compatible with .NET Framework 4.6.1+ / 4.8 and Unity)
- **Package outputs:** includes XML documentation (`.xml`) and symbol package (`.snupkg`); `dotnet pack` enforces public API compatibility against upstream `1.0.324`.

```bash
dotnet test tests/geometry3Sharp.Tests                      # full test suite on net10.0
dotnet test tests/geometry3Sharp.Tests -f net48             # Windows: tests against netstandard2.0 build
dotnet test tests/g3cli.Tests                               # CLI and MCP server integration tests
dotnet test tests/geometry3Sharp.Clipper2.Tests             # optional Clipper2 add-on tests
dotnet pack geometry3Sharp_netstandard.csproj -c Release -o artifacts
```

GitHub Actions (`.github/workflows/ci.yml`) runs Linux (`net10.0`) and Windows (`net48`) test suites, CLI tests, packaging, and `tests/PackageSmokeTest` on every PR and push to `master`. See [docs/QA.md](docs/QA.md#5-local-setup) for local SDK setup and test tiers.

---

## Mesh & Geometry File Formats

`StandardMeshReader` and `StandardMeshWriter` select the format by file extension:

| Format | Read | Write | Notes |
|---|---|---|---|
| `.obj` | yes | yes | Materials (`.mtl`), face groups, UVs, vertex colors extension |
| `.stl` | yes | yes | ASCII and binary (`WriteOptions.bWriteBinary`) |
| `.off` | yes | yes | Fan-triangulates polygons |
| `.g3mesh` | yes | yes | Packed binary `DMesh3` (`gSerialization`) |
| `.xyz` | yes | yes | ASCII point cloud (`x y z` or `x y z nx ny nz`, vertices only) |
| `.ply` | yes | yes | ASCII & binary (LE/BE read, LE write); normals, colors, UVs, point sets; double positions by default |
| `.3mf` | yes | yes | Core spec; build items, components, transforms; converts to mm by default (`ThreeMFReader.ConvertToMillimeters`) |
| `.glb` / `.gltf` | yes | yes | glTF 2.0; node transforms applied; `GLTFWriter` centers positions to preserve precision at large (e.g. UTM) coordinates |

Additional specialized I/O classes include `PolyMeshIO` (quad/polygon OBJ & PLY), `TetMeshWriter` (Medit `.mesh`, Gmsh `.msh`, VTK `.vtk`), `HeightGridReader`/`HeightGridWriter` (XYZ & ESRI ASCII Grid DGM), and `SVGWriter` (2D geometry).

---

## Unity Interop

`geometry3Sharp` builds for `netstandard2.0` and supports transparent conversion with Unity vector/math types (`Vector2`, `Vector3`, `Quaternion`, `Ray`, `Color`, `Bounds`) by defining **`G3_USING_UNITY`** in Unity's Scripting Define Symbols. See [docs/guides/upstream-history-and-tutorials.md#unity-interop-g3_using_unity](docs/guides/upstream-history-and-tutorials.md#unity-interop-g3_using_unity) for details and code examples.

---

## Documentation Map

- **[docs/README.md](docs/README.md)** — central documentation hub by role (users, CLI/MCP, contributors, architecture).
- **[docs/OPERATIONS.md](docs/OPERATIONS.md)** — task-to-class guide, repair/boolean contracts, and compiled examples (`docs/examples/`).
- **[docs/UPSTREAM_CHANGES.md](docs/UPSTREAM_CHANGES.md)** — behavioral changes and bug fixes relative to upstream `1.0.324`.
- **[tools/g3cli/README.md](tools/g3cli/README.md)** — command-line tool and MCP server reference.
- **[addons/geometry3Sharp.Clipper2/README.md](addons/geometry3Sharp.Clipper2/README.md)** — optional 2D polygon booleans/offsets package.
- **[benchmarks/README.md](benchmarks/README.md)** — index of all 66 performance and quality benchmarks.
- **[tools/README.md](tools/README.md)** — index of analyzers, fuzzers, verification pilots, and CI tools.
- **[docs/ROADMAP.md](docs/ROADMAP.md)** & **[docs/OPEN_TOPICS.md](docs/OPEN_TOPICS.md)** — direction, milestones, and active open items.
- **[CLAUDE.md](CLAUDE.md)** & **[AGENTS.md](AGENTS.md)** — engineering rules, test requirements, and coding-agent workflow.

---

## License & Credits

Licensed under the **Boost Software License 1.0** ([LICENSE](LICENSE)).

Originally created by Ryan Schmidt ([gradientspace](http://www.gradientspace.com)), incorporating foundational ports from David Eberly's WildMagic5/GTEngine ([Geometric Tools](https://www.geometrictools.com/)) and Christopher Batty & Robert Bridson's [SDFGen](https://github.com/christopherbatty/SDFGen). See [docs/SOURCES.md](docs/SOURCES.md) for all papers and permissive sources used in this fork, and [docs/guides/upstream-history-and-tutorials.md](docs/guides/upstream-history-and-tutorials.md) for original upstream tutorials and history.
