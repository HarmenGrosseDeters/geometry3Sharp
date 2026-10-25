# Roadmap

## Direction

geometry3Sharp is the only broad, permissively licensed (Boost) geometry library for .NET. C# is the language
of Unity, Rhino/Grasshopper, Revit and many desktop apps, and none of them has an equivalent of libigl,
CGAL or geometry-central.

The goal of this fork: a **geometry platform that coding agents (and people) can build domain apps on**,
rather than an app. That puts some things first:

1. **Robustness over breadth.** Exact predicates, deterministic results, clear result codes instead of
   silent failures or exceptions. Every change comes with tests; regressions are measured (eg the
   Thingi10K repair benchmark).
2. **A discoverable API.** XML docs on public classes, small examples per operation, consistent patterns
   (options object, `Compute()`, result with status).
3. **A headless layer.** A CLI / MCP server that exposes the operations on mesh files
   (repair, boolean, remesh, measure, convert).
4. **Modern build, wide reach.** net10.0 plus netstandard2.0 (Unity, .NET Framework), NuGet package, CI.
5. **Licenses.** Keep the Boost license. Port only from MIT / Apache / BSD sources (eg Manifold,
   geometry-central), never from GPL code (CGAL, parts of TetWild). Sources are listed in [SOURCES.md](SOURCES.md).

Relation to upstream: stay API compatible with 1.0.324, which existing users depend on. Upstream's
`dotnet8` branch has breaking changes (it removes the float types); useful parts (eg USD) can be taken over
selectively.

## Phase 1: foundation

- [x] Targets net10.0 and netstandard2.0
- [x] CI on GitHub Actions: tests on net10.0 (Linux) and net48 (netstandard2.0 build, Windows), pack, package smoke test
- [x] Package metadata: SPDX license, README, XML docs, symbols
- [x] Mesh formats PLY, 3MF, glTF/GLB (read and write)
- [ ] Package ID and publishing (nuget.org ID `geometry3Sharp` belongs to upstream)
- [x] API docs and examples per operation ([OPERATIONS.md](OPERATIONS.md), examples run as tests)

## Phase 2: robustness

- [x] `ExactMeshBoolean` / `ExactMeshArrangement`: exact arrangement of two meshes (integer grid, intersection points
  as canonical rational points of three planes, exact constrained Delaunay retriangulation), patches classified by
  exact ray parity, coplanar overlaps by orientation. About 6 s for two meshes of 200k triangles each.
- [x] Self-intersecting, nested and duplicated inputs: exact winding numbers per patch (signed ray crossings,
  stacks of coincident triangles); generalized winding number for open inputs; touching solids split into
  manifold sheets; `Resolve(mesh)` for one mesh. Benchmark on Thingi10K: `BENCH_REPAIR=exact`, `analyze_exact.py`.
- [x] Iterated booleans with coplanar faces: new vertices inside flat regions and on straight edges between them
  are removed exactly and the regions retriangulated (`ExactPlanarSimplify`); the 10x10x10 cube grid (1999
  booleans) no longer grows exponentially.
- [ ] More than two meshes in one operation (the arrangement supports it already)
- [ ] Rounding of the output without new self-intersections (snap rounding), or exact output coordinates
- [x] Alpha wrapping (`MeshAlphaWrap`, Portaneri et al. 2022): closed, manifold, intersection-free wrap with an offset
  around any input (soups, open meshes, point clouds), on an own exact 3D Delaunay triangulation (`Delaunay3`).
  Benchmark on Thingi10K: `BENCH_REPAIR=wrap`, `analyze_wrap.py` (comparison with the solidify fallback).

| Feature | Research / port source | Why |
|---|---|---|
| Exact, robust mesh booleans and arrangements | Zhou et al. 2016 *Mesh Arrangements for Solid Geometry*; Cherchi et al. 2020/2022 (indirect predicates); Trettner et al. 2022 *EMBER*; Manifold (Apache-2.0) | The biggest gap; the existing `MeshBoolean` is not robust, the winding-number variant is voxel based. `ExactPredicates` is the base. |
| Alpha wrapping / shrink wrap | Portaneri et al. 2022 *Alpha Wrapping with an Offset* | Guaranteed watertight manifold output from any input: scan repair, 3D printing, biomedical CAD. Complements `MeshAutoRepair`. |

## Phase 3: parameterization and geodesics

- [x] Parameterization: LSCM, ARAP, SLIM (Rabinovich et al. 2017), Boundary First Flattening (Sawhney & Crane 2017);
  UV packing; distortion measures; sparse Cholesky solver (`SparseCholesky`)
- [ ] Cutting closed surfaces into discs (seams) and automatic chart segmentation; BFF with cone singularities
- [x] Intrinsic Delaunay triangulations (Sharp & Crane), for stable Laplacians and heat on poor meshes:
  `IntrinsicTriangulation` (edge flips with signposts, tracing of intrinsic edges over the input mesh,
  intrinsic mollification of degenerate triangles); `MeshGeodesicDistance.IntrinsicDelaunay`. On a plane of
  169-degree triangles the mean distance error drops from 2.2 to 0.06 edge lengths.
- [x] Vector Heat Method (Sharp, Soliman, Crane 2019), including a robust log map (beyond `MeshLocalParam`):
  `VectorHeatMethod` (transport, scalar extension, log map), on the intrinsic Delaunay triangulation,
  with the direct solver `SparseCholesky`.
- [x] Intrinsic Delaunay *refinement* (vertex insertion): `IntrinsicTriangulation.DelaunayRefine` (insertion in
  faces and on edges with signpost updates, Chew / Ruppert circumcenter refinement with a minimum angle,
  boundary edges opposite obtuse angles split, concentric shells and skipped triangles at small input
  angles, status codes); options `IntrinsicRefine` in `MeshGeodesicDistance` and `VectorHeatMethod`,
  `IntrinsicTriangulation.BuildDelaunay` for the cotan Laplacian. All cotan weights non-negative also on the
  boundary; on the plane of 169-degree triangles the mean distance error drops from 0.06 to 0.02 edge
  lengths (max 0.8 to 0.1). Common subdivision by tracing (`ExtractCommonSubdivision`, area-checked, reports
  `Inconsistent` instead of wrong faces).
  - [ ] Integer (normal) coordinates (Gillespie, Sharp, Crane 2021) for a combinatorially exact common
    subdivision; signposts with the area checks suffice for the current operations

## Phase 4: platform layer

- [x] CLI / MCP server over the operations: `tools/g3cli` (measure, repair, boolean, resolve, remesh, simplify,
  convert; JSON results with result codes; `g3cli mcp` over stdio)
- [x] Analysis building blocks: wall thickness (`MeshWallThickness`: ray, shape diameter function, inscribed ball /
  medial axis), deviation maps (`MeshDeviation`, statistics in `DeviationStatistics`), primitive fitting
  (`PrimitiveFit3`: plane, sphere, cylinder, cone; RANSAC + least squares)
- A showcase domain: terrain / GIS (in progress) and biomedical / orthotics

## Later

- Point clouds: normal orientation (Dipole 2021, iPSR 2022, GCNO 2023), screened Poisson reconstruction, fast winding numbers (Barill et al. 2018)
- Feature-preserving offsets (Zint et al. 2023)
- Quad remeshing (Instant Meshes 2015, QuadWild 2021)
- Deformation: ARAP, biharmonic, cage coordinates
- Tetrahedral meshing (fTetWild, MIT)
- TPMS / lattice structures from the implicit functions
- Sparse SDF grids (OpenVDB-like), SIMD via `System.Numerics`
- AI bridges as a separate package (ONNX Runtime): project 2D segmentation masks (SAM, DINO) from several views onto face selections, texture reprojection via ExpMap
