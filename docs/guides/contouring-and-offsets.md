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
| Wall around a level set, thinner than a grid cell (shell, sheet) | `g3.TwoLevelMarchingTetrahedra` | Both sides kept at any thickness, no self-intersections by construction, node values only; about 2.5x the triangles of marching cubes |
| TPMS lattice (gyroid, Schwarz P/D, Neovius, I-WP, Lidinoid) | `g3.TpmsLattice` | Sheet or skeletal solid in a box or closed mesh domain, graded/hybrid cells, certified clean |

---

## `MarchingCubes` & `MarchingCubesPro`

- **Namespace & classes:** `g3.MarchingCubes`, `gs.MarchingCubesPro`, `g3.MeshSignedDistanceGrid`, `g3.DenseGridTrilinearImplicit`
- **Input / mutability:** Evaluates a `BoundedImplicitFunction3d`; outputs a new `DMesh3` in `Mesh`.
- **Guarantees & limits:** `MarchingCubes.Generate` evaluates slabs in parallel and publishes vertices/triangles in deterministic serial `z/y/x` order, matching serial output bit-for-bit for deterministic thread-safe fields. Validates finite positive `CubeSize` and `int`-representable axis counts (`ArgumentException` before evaluation).
- **Example:** [OffsetSurfaceExample.cs](../examples/OffsetSurfaceExample.cs)
- **Further reading:** [CONSERVATIVE_MC_AUDIT.md](../design/CONSERVATIVE_MC_AUDIT.md)

### Slivers at grid corners (`SingleLerp`, `LerpSteps`, `Bisection`)

A crossing within `1e-5` of the field change along an edge of a grid corner is pulled to `1e-6 * CubeSize` from that corner, so the vertices around a corner stay distinct and the mesh manifold (`find_iso`). Where the surface passes through or next to a grid corner (a sphere or a box on the grid) the triangles around it are slivers: edges of about `1e-6 * CubeSize`, area about `1e-12 * CubeSize^2`. That is below the degenerate area of `MeshRobustRepair.NotCleanReason` (`1e-12` of the squared bounds diagonal) when `CubeSize` is small against the bounds, so a plain sphere can report `degenerate triangles` although the mesh is closed and manifold (issue #1599).

- The slivers are not welded: their vertices differ, so a weld would move the surface by up to `1e-6 * CubeSize`.
- Check the result with `NotCleanReason` and repair it (`MeshRobustRepair`) if a clean mesh is required, or use `RootMode = LinearEdges` (below), which refuses such inputs instead of creating slivers.

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
  - **Scale of the field (#1376):** With `ExactHermite` (dense mode), the size of the values and of the geometry is not part of the result: `c * f` has the contour of `f` (the same bits for `c = 2^k`, otherwise up to rounding), and a level set whose gradient is far below 2.2e-16 (eg `exp(-40 r^2)` at the level 1e-30) is contoured. A dense grid whose values are not normal floats (nonzero below 2^-126 or above about 2^127, eg `c * f` for `c = 2^-200`, an iso value of 1e-100, +inf outside the domain or a Gaussian's tail) is stored times a power of two taken from the values at the sign-changing edges, so the bits stay the same under a value scale. A grid of normal floats is not touched, and a field whose values at the surface are normal floats keeps its output; it can differ from before only where those values were subnormal or clamped. The extra cost is one more double per node in the slabs that hold such a value. Limits: values of one surface that differ by more than 2^126 lose the smaller ones, and in sparse mode the node values stay `float` (below 1.4e-45 a value is 0, above 1.7e38 it is clamped); `ExactHermite = false` keeps its absolute tests and its output, and is for fields and geometry of ordinary size.
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
  - **Thin parts (#1597):** the field is sampled at the grid nodes only, so an inset that leaves a part thinner than about a cell (`RelativeCellSize * |Distance|`) can have no node inside it. `Status.Empty` now means that nothing is left, proven from the distances at the nodes (no node of the input is as far from its surface as `|Distance|` less 0.866 cells). Where the nodes cannot exclude a part, `Compute()` halves the cell (8 times the nodes) up to `MaxCellRefinements` times (default 2, a negative value acts as 0) within `MaxGridNodes` and `MaxRefinementNodes` (default 4 million nodes: a refinement whose grid is larger is not run) and only then; only the all-empty case is refined (a thin part lost next to a thick one that is found stays `Success`); every other input runs the first grid and gives the mesh it gave before, bit for bit. If the part is still not found the status is `Status.NotResolved` with `SuggestedCellSize` (a quarter of the last cell): that does not prove that the part is gone. `RefinementLimited` tells that a grid limit stopped it (then raise `MaxGridNodes`/`MaxRefinementNodes`; the suggested cell may need them). **Worst-case cost:** the first grid plus refinements of 8x and 64x its nodes, bounded by `MaxRefinementNodes`: with the default a first grid of up to 500000 nodes is refined once (about 1 s for the near-limit empty case of 0.4 million nodes, 12.9 s without the budget); the repro needs about 130k nodes. **A former `Empty` can therefore be `Success` (the box 1 × 0.7 × 0.5 inset by 0.235 to 0.245: found at the default cell, volume exact, 24-310 ms instead of 6 ms and about 130k nodes instead of 5k for the 0.01 thick case) or `NotResolved`; near the limit (an inset within 0.866 cells of the largest inscribed radius of a part that is really gone) the bound cannot tell, the refinements are spent and the status is `NotResolved`, `Empty` after enough of them.** `MaxCellRefinements = 0` runs the first grid only (`NotResolved` instead of `Empty` where a part may be left).
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
- **Certificate on the returned coordinates:** the check runs after the transform to world coordinates (`Origin + Rotation * v`), on the mesh that is returned. Its rounding moves a vertex by up to about an ulp of its world coordinates; far from the origin, where that ulp approaches the cube (`CellSize / Divisions`), triangles collapse or cross and the result is `NotClean` or `SelfIntersecting` instead of `Ok` (#1569: `Origin` near `1e13` with 32 cubes per unit period). No a-priori bound replaces the check: it would need the smallest separation of non-adjacent triangles, which the exact check does not compute. The certificate runs once per grid attempt, as before, with no extra pass, so a run whose first grid is clean costs nothing extra. Because the returned mesh is what is certified, the grid-shift retry on degenerate triangles can now also fire on degeneracy that the rounding causes (a far origin then tries every shift before `NotClean`), and a lattice-frame sliver that the rounding makes non-degenerate is accepted without a retry; volume and wall thickness are measured in the lattice frame, before the transform.
- **Thin sheet walls:** marching cubes needs a wall of about two cubes. `TwoLevelSheet = true` (Sheet style, opt-in, default off) contours the two sides of the wall as two levels of the surface function with `TwoLevelMarchingTetrahedra` (below): walls down to a small fraction of a cube stay connected. Not with `Enclosure` or a hybrid. Without it a thin Sheet loses material (a realized fraction of 1-10 % of the request at a wall under a cube): a plain Sheet (marching cubes, constant fraction, no hybrid) that realizes less than `TpmsLattice.SheetRefusalRatio` (0.5) of the requested fraction returns `Status = NotResolved` and no mesh (`RealizedVolumeFraction` and `Message` stay set); before #1596 this was `Ok` with the thin result.
- **Enclosure margin:** the enclosure level is half a cube diagonal plus the proven pad (about 0.87 h in the normalised field), so the solid grows by a margin that falls with h, and the result fills the box grown by `IsoValueUsed` on every side. When it fills at least `TpmsLattice.EnclosureRefusalFraction` (0.99) of that grown box (volume / grown-box volume, independent of the box size; `RealizedVolumeFraction` is relative to the box itself and can be above 1 for a lattice that is still fine), the margin has swallowed the voids and the result is the solid box, not a lattice: `Status = NotResolved`, no mesh, `Message` names `MinDivisionsForEnclosure` (before #1596 this was `Ok` with the box). `EnclosureFractionEstimate(divisions)` gives that filled part without contouring and `MinDivisionsForEnclosure()` the smallest power-of-two `Divisions` whose estimate is at most `EnclosureEstimateTarget` (0.9): an estimate (the periodic average of the lattice, within about 0.02 below to 0.08 above the contoured value on the cases tested; a Gyroid sheet of 30 % at 16 / 32 / 64 divisions fills 1.00 / 0.70 / 0.47), NaN or 0 for a hybrid, a graded cell size, `VolumeFractionAt`, a `DomainMesh` or a cube that is not dyadic. A refusal turns a previous `Ok` into `NotResolved` for these inputs; raise `Divisions` (or use `TwoLevelSheet` for a sheet).
- **Example:** [TpmsLatticeExample.cs](../examples/TpmsLatticeExample.cs)
- **Same bits on every runtime:** the field uses the correctly rounded `CoreMath` sine and cosine, not the platform libm (#1577, #1590), so a lattice has the same vertex bits in the browser (WebAssembly), on Windows and on Linux. The grid nodes read their sines and cosines from per-axis tables; that is only a speed-up (`TpmsAxisTableTests` compare it with the direct path bit for bit).
- **Further reading:** [TPMS_LATTICE.md](../design/TPMS_LATTICE.md), [TPMS_GRADED_CELL.md](../design/TPMS_GRADED_CELL.md)

---

## Walls Thinner Than a Cell (`TwoLevelMarchingTetrahedra`)

- **Namespace & class:** `g3.TwoLevelMarchingTetrahedra`
- **Input / mutability:** A field `Field` (g), a half width `HalfWidth` (w, or `HalfWidthF` per position), an optional `Domain` field (d, negative inside), `Bounds` and `CellSize`. Generates a new `DMesh3` of the solid `{|g| <= w}` cut to `{d <= 0}`.
- **When to use:** The wall is thinner than about two grid cells (sheet lattices, shells, the band between two offsets of a smooth surface). `MarchingCubes` or `DualContouring` on the single field `|g| - w` see a wall only where a grid node falls inside it and return fragments.
- **How it works:** The two sides are contoured as the two levels `g = +w` and `g = -w` on a grid of five tetrahedra per cube, with g, w and d interpolated linearly in each tetrahedron. The result is the exact boundary of the solid of those piecewise linear functions.
- **Guarantees:** In a tetrahedron the two sides are planes that cannot meet, so both are kept at any thickness above zero that the field's values can carry (a half width below their rounding throws); pieces of different tetrahedra have disjoint interiors, so the mesh has no self-intersections by construction; it is closed where the solid stays inside `Bounds` (with `SliverGuard` on). These are arguments about the construction; `MeshRobustRepair.NotCleanReason` is the certificate of a result. One field evaluation per grid node. Parallel field evaluation equals serial bit for bit; power-of-two scales give the same mesh bit for bit.
- **Limits:** No topology guarantee against the true level sets (it sees what the grid resolves of g, not of the wall). Triangles can be slivers where the wall is thinner than about 1/500 of a cell at a rim; `SliverGuard` (planes kept off grid nodes) and `RimGuard` (the domain's cut kept off the wall's crossings on grid edges) remove the other cases by moving node values slightly, so check `MeshRobustRepair.NotCleanReason` where a clean mesh is required. About 2.5 times the triangles of marching cubes on the same grid.
- **Example:** [TwoLevelMarchingTetrahedraExample.cs](../examples/TwoLevelMarchingTetrahedraExample.cs)
- **Further reading:** [CONTOURING_SDF_SUBGRID_2026-11.md](../research/CONTOURING_SDF_SUBGRID_2026-11.md) (measurements), [TPMS_LATTICE.md](../design/TPMS_LATTICE.md) section 10, [benchmarks/TpmsLattice](../../benchmarks/TpmsLattice/README.md)
