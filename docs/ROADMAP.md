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
- [x] More than two meshes in one operation: n-ary union / intersection / difference and any predicate on the
  winding numbers of all inputs (Zhou et al. 2016, eg `AtLeast(k)`), result code `Status`; only meshes with
  overlapping boxes are intersected, and only the meshes whose box contains a patch get a winding number ray.
  The 10x10x10 cube grid with voids (2000 meshes) in one pass: 27 s instead of 1999 pairwise booleans
  (`BooleanComparison nary`); CLI `g3cli boolean --more ... --op at_least --k 2`.
- [x] Rounding of the output without new self-intersections, or exact output coordinates: `ExactMeshBoolean.SafeRounding`
  (`MeshRoundingRepair`: after rounding, local edits checked with exact predicates on the doubles, after Milenkovic &
  Sacks 2019; the result is verified free of self-intersections and slivers, or the remaining ones are counted) and
  `ComputeExactVertices` (exact rational coordinates). `MeshRobustRepair` uses it in its exact stage: Thingi10K
  `robust` 265 → 270 repaired (same build, with and without).
- [x] Alpha wrapping (`MeshAlphaWrap`, Portaneri et al. 2022): closed, manifold, intersection-free wrap with an offset
  around any input (soups, open meshes, point clouds), on an own exact 3D Delaunay triangulation (`Delaunay3`).
  Benchmark on Thingi10K: `BENCH_REPAIR=wrap`, `analyze_wrap.py` (comparison with the solidify fallback).
- [ ] Remeshers must not leave degenerate triangles. `Remesher` and `RemesherPro` (`g3cli remesh` settings) leave
  degenerate triangles or self-intersections on 18 of 20 clean Thingi10K meshes, 99% of the triangles with q < 0.1 at
  feature constraints (`benchmarks/RemeshBenchmark`). `RemesherPro` (8 x `RemeshIteration` and a full pass without
  smoothing) leaves a zero-area triangle in the band of `TerrainSafetyTests.SafetyLine_AlongSurface_BandIsOneCleanPiece`,
  where `Remesher` leaves none. An opt-in quality pass at the constraints is in progress (curve vertices smoothed
  along their curve, guarded vertex moves, flips of poor pairs, no splits or collapses that create flat triangles).

| Feature | Research / port source | Why |
|---|---|---|
| Exact, robust mesh booleans and arrangements | Zhou et al. 2016 *Mesh Arrangements for Solid Geometry*; Cherchi et al. 2020/2022 (indirect predicates); Trettner et al. 2022 *EMBER*; Manifold (Apache-2.0) | The biggest gap; the existing `MeshBoolean` is not robust, the winding-number variant is voxel based. `ExactPredicates` is the base. |
| Alpha wrapping / shrink wrap | Portaneri et al. 2022 *Alpha Wrapping with an Offset* | Guaranteed watertight manifold output from any input: scan repair, 3D printing, biomedical CAD. Complements `MeshAutoRepair`. |

## Phase 3: parameterization and geodesics

- [x] Parameterization: LSCM, ARAP, SLIM (Rabinovich et al. 2017), Boundary First Flattening (Sawhney & Crane 2017);
  UV packing; distortion measures; sparse Cholesky solver (`SparseCholesky`)
- [x] Cutting closed surfaces into discs (seams) and automatic chart segmentation; BFF with cone singularities:
  `MeshDiscCut` (tree-cotree cut graph with shortest loops, Eppstein; Erickson & Whittlesey; any genus, boundaries,
  components, bowties; vertices split along the seams), `MeshChartSegmentation` (normal-based region growing with
  compactness, VSA / D-Charts style), `ConeFlattening` (BFF with greedily placed cones; a cube's corners become cones
  and it unfolds without distortion), `UVOverlap` (exact overlap test), and the pipeline `UVAtlasGenerator`
  (cones where overlap-free, else charts split until within the distortion limits, packing, status code);
  `g3cli uv`. Cones are not guaranteed globally injective (eg on a torus); the pipeline then falls back to charts.
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
  lengths (max 0.8 to 0.1; `benchmarks/IntrinsicBenchmark`). Common subdivision by tracing (`ExtractCommonSubdivision`, area-checked, reports
  `Inconsistent` instead of wrong faces).
  - [ ] Integer (normal) coordinates (Gillespie, Sharp, Crane 2021) for a combinatorially exact common
    subdivision; signposts with the area checks suffice for the current operations

## Phase 4: platform layer

- [x] CLI / MCP server over the operations: `tools/g3cli` (measure, repair, boolean, resolve, remesh, simplify, uv,
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
- Tetrahedral meshing: see Phase 5 (constrained Delaunay tetrahedrization). fTetWild is MPL-2.0 (file-level
  copyleft), not MIT: implement from the paper (Hu et al. 2020) rather than port
- TPMS / lattice structures from the implicit functions
- Sparse SDF grids (OpenVDB-like), SIMD via `System.Numerics`
- AI bridges as a separate package (ONNX Runtime): project 2D segmentation masks (SAM, DINO) from several views onto face selections, texture reprojection via ExpMap

## Phase 5: candidates from recent research

Checked against what the library already has (exact booleans, alpha wrap, intrinsic triangulations, BFF / SLIM,
vector heat). Ordered by priority: effort against what it builds on and what it unlocks. Licenses checked at the
source repositories (October 2026); "paper only" means the reference code may not be ported.

| # | Feature | Research / port source | License of reference code | Why / builds on |
|---|---|---|---|---|
| 1 | Geodesic paths, loops and Bézier curves on surfaces (FlipOut). **Done:** paths, waypoints, loops (`MeshGeodesicPath`); Bézier curves open | Sharp & Crane 2020 *You Can Find Geodesic Paths in Triangle Meshes by Just Flipping Edges*; geometry-central | MIT | Almost entirely on `IntrinsicTriangulation`; cut lines (orthotics), UV seams, curves on surfaces |
| 2 | Polyhedral envelope containment (exact, rounding-proof containment predicate) | Wang, Schneider, Hu, Attene, Panozzo 2020 *Exact and Efficient Polyhedral Envelope Containment Check*; `fast-envelope` | MIT | On `ExactPredicates` / `AdaptivePredicates`. The `Reducer` already guarantees a one-sided bound (`MaxDeviation`, certified by `TriangleDeviationBounds`), so the gain is speed and exactness of that check, not a new guarantee: benchmark against `MaxDeviation` before adopting |
| 3 | ~~Robust 2D polygon booleans and offsets: port Clipper2~~ Not planned: Clipper2 (Angus Johnson, Boost-1.0) has an official C# version on NuGet; use it directly (at most a thin adapter to `GeneralPolygon2d`). A port would only duplicate it. Straight skeleton / 2D medial axis from papers if needed (CGAL is GPL) | Clipper2 | Boost-1.0 | |
| 4 | Constrained Delaunay tetrahedrization, then tetrahedral meshing | Diazzi, Panozzo, Vaxman, Attene 2023 *Constrained Delaunay Tetrahedrization: A Robust and Practical Approach*; Hu et al. 2020 *Fast Tetrahedral Meshing in the Wild* | CDT: GPL / LGPL, paper only; fTetWild: MPL-2.0, paper only | On `Delaunay3` and the implicit points of `ExactMeshArrangement` |
| 5 | Generalized signed distance and winding numbers for open / broken inputs | Feng & Crane 2024 *A Heat Method for Generalized Signed Distance*; Feng, Gillespie, Crane 2023 *Winding Numbers on Discrete Surfaces* | paper | Complements `MeshGeodesicDistance`, `VectorHeatMethod` and the repair stages |
| 6 | Grid-free PDE solving (Monte Carlo geometry processing) | Sawhney & Crane 2020 *Monte Carlo Geometry Processing* (walk on spheres); Sawhney et al. 2023 *Walk on Stars*; Zombie | MIT | Needs only closest-point and ray queries (`DMeshAABBTree`, winding numbers); works on dirty geometry |
| 7 | Foldover-free (injective) maps | Garanzha et al. 2021 *Foldover-free maps in 50 lines of code* | paper | Closes the gap noted in Phase 3: cone parameterizations are not guaranteed globally injective |
| 8 | Continuous collision detection | Wang et al. 2021 *A Large Scale Benchmark and an Inclusion-Based Algorithm for Continuous Collision Detection* (Tight-Inclusion); Li et al. 2020 *IPC* | Tight-Inclusion: MIT | Conservative, provably correct CCD; Unity, robotics |
| 9 | Simplification with intrinsic error metrics | Liu et al. 2023 *Surface Simplification using Intrinsic Error Metrics* | paper | On the intrinsic triangulation infrastructure |
| 10 | Mesh from coarse / sparse SDFs | Sellán et al. 2023 *Reach for the Spheres*; 2024 *Reach for the Arcs* | paper | With the sparse SDF grids and TPMS items under Later |

## Open research questions

Problems where the library already has a head start and that could be researched (and published) here:

- **Practical 3D snap rounding with guarantees.** `SafeRounding` (after Milenkovic & Sacks 2019) verifies and repairs;
  open is a rounding that *guarantees* no new self-intersections with bounded displacement and practical running time
  (Devillers et al. 2018 is impractical). Thingi10K as the benchmark.
- **An envelope-guaranteed exact pipeline.** Boolean → rounding → simplification / remeshing with every stage
  provably inside an ε-envelope and free of self-intersections (needs Phase 5 #2).
- **Metamorphic testing and fuzzing of geometry kernels.** Systematize the invariance tests over `Similarity.All()`,
  the defect generators and the differential predicate tests into a method and benchmark, applicable to other
  kernels as black boxes (without porting their code).
- **Bit-for-bit determinism across platforms.** net48 / net10, x64 / ARM, FMA, parallel paths: reproducible geometry
  for CAD and certification (eg medical devices).
- **Exact or certified intrinsic triangulations.** Integer coordinates (Gillespie, Sharp, Crane 2021, Phase 3) combined
  with exact predicates, so that signposts never fail silently.
- **Monte Carlo PDEs directly on polygon soups.** Walk on stars with generalized winding numbers instead of a clean
  inside / outside test, without a prior repair (needs Phase 5 #5, #6).
- **Geometry APIs for coding agents.** A benchmark of mesh / CAD tasks solved by agents through `g3cli` / MCP, and
  which API patterns (result codes, `NotCleanReason`, options objects) actually help them.
- **Showcase domains with error bounds.** Terrain: DEM → TIN with guaranteed error, exact cut / fill volumes.
  Orthotics: geodesic cut lines, wall thickness, feature-preserving offsets.
