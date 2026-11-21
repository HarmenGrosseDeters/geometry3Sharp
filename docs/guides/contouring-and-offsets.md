# Contouring, Offset Surfaces, Lofting & TPMS Lattices

This guide covers implicit iso-surface extraction (`MarchingCubes`, `DualContouring`, `MarchingCubesPro`), certified mesh offsetting (`MeshOffset`), contour lofting (`ContourLoft`, `ContourLoftStack`), and triply periodic minimal surface lattices (`TpmsLattice`).

---

## Choosing a Contouring or Offset Class

| Goal | Recommended class | Key characteristics |
|---|---|---|
| Closed iso-surface with vertices on grid edges, deterministic in parallel | `g3.MarchingCubes` | Ordered slab batching (parallel matches serial bit-for-bit); no dense-array limit |
| Iso-surface preserving sharp edges and corners (QEF vertices), sparse or adaptive | `g3.DualContouring` | Exact Hermite bisection by default, intersection-free face fans, exact self-intersection resolution |
| Surface-following iso-surface from seed points only | `gs.MarchingCubesPro` | `GenerateContinuation` visits only cells near the surface (maintained for bug fixes only) |
| Certified offset or inset of a closed, clean mesh | `gs.MeshOffset` | Dual contouring of exact distance field, proven band (`BandCertified`), sharp creases, exact self-intersection check |
| Simple voxelized SDF offset of a mesh | `g3.MeshSignedDistanceGrid` + `g3.MarchingCubes` | Regular-grid offset ([OffsetSurfaceExample.cs](../examples/OffsetSurfaceExample.cs)); no certified enclosure |
| Triangulated loft between two or more planar slice contours | `g3.ContourLoft`, `g3.ContourLoftStack` | Optimal triangulated bands, optional slope admissibility, branching/merging stacks, exact self-intersection check |
| TPMS lattice (gyroid, Schwarz P/D, Neovius, I-WP, Lidinoid) | `gs.TpmsLattice` | Sheet or skeletal solid in a box or closed mesh domain, graded/hybrid cells, certified clean |

---

## `MarchingCubes` & `MarchingCubesPro`

- **Namespace & classes:** `g3.MarchingCubes`, `gs.MarchingCubesPro`, `g3.MeshSignedDistanceGrid`, `g3.DenseGridTrilinearImplicit`
- **Input / mutability:** Evaluates a `BoundedImplicitFunction3d`; outputs a new `DMesh3` in `Mesh`.
- **Guarantees & limits:** `MarchingCubes.Generate` evaluates slabs in parallel and publishes vertices/triangles in deterministic serial `z/y/x` order, matching serial output bit-for-bit for deterministic thread-safe fields. Validates finite positive `CubeSize` and `int`-representable axis counts (`ArgumentException` before evaluation).
- **Example:** [OffsetSurfaceExample.cs](../examples/OffsetSurfaceExample.cs)
- **Further reading:** [CONSERVATIVE_MC_AUDIT.md](../design/CONSERVATIVE_MC_AUDIT.md)

### `RootMode = LinearEdges`

Standard root modes (`SingleLerp`, `LerpSteps`, `Bisection`) snap near-zero values to grid corners. `RootMode = LinearEdges` performs a single unmodified linear interpolation per edge without nudges or skipped triangles:

- Requires finite samples, no sample exactly on the iso-level, and crossings strictly inside grid edges.
- Throws `InvalidOperationException` (or `ArgumentException` for non-finite values or `MarchingCubesPro` grids above `65535` cells per axis) rather than silently repairing a violation.
- Serves as the building block for conditional conservative enclosures (see [CONSERVATIVE_MC_AUDIT.md](../design/CONSERVATIVE_MC_AUDIT.md)).

---

## `DualContouring`

- **Namespace & class:** `g3.DualContouring`
- **Input / mutability:** Evaluates an `Implicit` field (or custom `HermiteF` / sparse `NodeValueF`); outputs a new `DMesh3`.
- **Guarantees & limits:** Places QEF vertices inside cells to reproduce sharp creases and corners at coarser grids than `MarchingCubes`.
  - **`ExactHermite` (default `true`):** Bisects edge crossings (60 steps on `Implicit`), evaluates exact gradients, and uses `QEFEigenTolerance = 0.02`. Set `ExactHermite = false` with `QEFEigenTolerance = 0.1` for faster linear-interpolated Hermite data.
  - **Ambiguous faces & adaptive cells:** Inserts face vertices to fan polygons so every triangle stays inside a single cell; avoids degenerate quad splits along crease lines (`DegenerateSplitsAvoided`).
  - **`ResolveSelfIntersections` (default `true`):** Checks the output exactly for self-intersections and repairs if needed (`SelfIntersectionFree`).
- **Further reading:** [DC_INTERSECTION_FREE.md](../design/DC_INTERSECTION_FREE.md), [DualContouringHermite README](../../benchmarks/DualContouringHermite/README.md), [DualContouringIntersections README](../../benchmarks/DualContouringIntersections/README.md)
- **Example:** [DualContouringAndOffsetExample.cs](../examples/DualContouringAndOffsetExample.cs)

---

## Certified Mesh Offset (`MeshOffset`)

- **Namespace & class:** `gs.MeshOffset`
- **Input / mutability:** Takes a closed, clean `DMesh3`; outputs a new offset (`Distance > 0`) or inset (`Distance < 0`) `DMesh3`.
- **Guarantees & limits:** Dual-contours the exact signed distance field on a grid of `RelativeCellSize * |Distance|` (default `1/4`, maximum `1/2`), keeping inner voids and sharp creases where offset sheets meet.
  - Closed, manifold, and scale-free (bit-identical under power-of-two scaling).
  - Exact self-intersection decision reported in `SelfIntersectionFree`.
  - Certified one-sided band: every output point is proven within `BandDeviation` of signed distance `Distance`, checked against `BandTolerance` (default half a cell) in `BandCertified`.
  - `MeshOffset.CertifyBand` can certify the distance band of *any* offset mesh (such as a `MarchingCubes` or `MeshAlphaWrap` result).
  - For open or broken inputs, repair first (`MeshRobustRepair`) or use `MeshAlphaWrap` (outward only).
- **Example:** [DualContouringAndOffsetExample.cs](../examples/DualContouringAndOffsetExample.cs)
- **Further reading:** [MESH_OFFSET.md](../design/MESH_OFFSET.md), [MeshOffsetBenchmark README](../../benchmarks/MeshOffsetBenchmark/README.md)

---

## Contour Lofting (`ContourLoft`, `ContourLoftStack`)

### Single-Pair Band Lofting (`ContourLoft`)

- **Namespace & classes:** `g3.ContourLoft`, `ContourLoftResult`, `ContourLoftTriangle`
- **Input / mutability:** Two closed contours in parallel planes; outputs a triangulated band (optionally capped via `QualityMesh2` into a closed solid).
- **Guarantees & limits:** Finds the globally optimal band over all start correspondences (minimum area, maximum volume, or custom cost), with optional per-triangle admissibility checks (such as `ContourLoft.NotSteeperThan(spec)` for exact slope certification). Returns `NoAdmissibleBand` with the exact minimum violation count when no admissible band exists, and verifies freedom from self-intersections (`SelfIntersecting`).
- **Limits:** `m * n <= MaxCells` (`2^22` default); scale-invariant under power-of-two scaling; `NormalizeForCost` helps for horizontally offset similar contours; planar caps require coordinates below `1e70` and height-to-width ratio below `~1e5` (otherwise `CapFailed`).

### Multi-Slice, Multi-Contour Stacks (`ContourLoftStack`)

- **Namespace & classes:** `g3.ContourLoftStack`, `ContourLoftStackResult`, `ContourLoftStackStatus`, `ContourLoftGroupInfo`, `ContourLoftGroupKind`
- **Input / mutability:** Stack of parallel slices with arbitrary outer contours and holes; outputs one closed, manifold, outward-oriented mesh (`CapEnds`), verified exact-self-intersection-free (never returns a self-intersecting mesh).
- **How branching & merging work:** Overlapping contours across consecutive slices are matched via exact constrained Delaunay overlay. One-to-one pairs form stage-1 bands; one-to-many branches and many-to-one merges are tiled through composite contours joined by zero-width bridges at an intermediate plane (`BranchFraction`, default `0.5`). Unmatched contours cap off (appear/vanish), and holes loft into tunnels.
- **Saddle geometry:** By default, bridged branches produce a ridge/crease at the bridge plane height (Meyers, Skinner & Sloan 1992). Enable opt-in `LoweredSaddle` (for contained groups without holes) to build a lowered roof over the crotch region instead.
- **Statuses:** `InvalidInput`, `CrossingContours`, `Unsupported` (island inside a hole in a branching/merging group, or a contour ending while its hole continues), `BridgeNotFound`, `NoAdmissibleBand`, `BandFailed`, `SelfIntersecting`, `CapFailed`, `InvalidCost`, `TooLarge`. Serial and deterministic.
- **Example:** [ContourLoftExample.cs](../examples/ContourLoftExample.cs)
- **Further reading:** [CONTOUR_LOFT.md](../design/CONTOUR_LOFT.md), [ContourLoftBenchmark README](../../benchmarks/ContourLoftBenchmark/README.md)

---

## TPMS Lattices (`TpmsLattice`)

- **Namespace & class:** `g3.TpmsLattice`
- **Input / mutability:** Generates a new closed, manifold `DMesh3` inside an axis-aligned box or closed mesh domain.
- **Capabilities:** Supports gyroid, Schwarz P, Schwarz D, Neovius, I-WP, and Lidinoid surfaces as sheet or skeletal solids, sized by volume fraction or sheet wall thickness. Supports linear period grading, hybrid blending of two lattice types across a plane with a sigmoid transition, and dyadic enclosure grids.
- **Guarantees:** Returns `Ok` only when the generated mesh is closed, manifold, and passes the exact self-intersection check (unless self-intersection verification is explicitly disabled).
- **Example:** [TpmsLatticeExample.cs](../examples/TpmsLatticeExample.cs)
- **Further reading:** [TPMS_LATTICE.md](../design/TPMS_LATTICE.md), [TPMS_GRADED_CELL.md](../design/TPMS_GRADED_CELL.md)
