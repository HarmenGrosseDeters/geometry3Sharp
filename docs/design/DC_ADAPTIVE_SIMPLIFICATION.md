# Adaptive simplification of dual contouring (`AdaptiveDualContouring`)

Status: `AdaptiveDualContouring` merged in #995 (opt-in). Owner decision 2026-11-16: the simplification is the default of `DualContouring` (`Simplify`, opt-out); section 6.

Goal: far fewer triangles than uniform dual contouring at the same accuracy. Where one vertex still fits the surface
within a tolerance, the cells of an aligned 2^L block merge into one vertex, so flat regions get large triangles and
fine cells stay at edges, corners and curved parts.

## 1. Look before you build

Library (searched): `DualContouring` (uniform grid, per-patch vertices, exact self-intersection check and repair, the
QEF solver `solve_qef` that keeps the vertex in a box), `SetRefinement` (two cell sizes only: coarse and a factor 2^k
finer, so it cannot express a multi-level octree), `WindingNumberOctree` (all leaves finest), `VolumeSampleCells`. None
merges cells by a QEF error. The transition logic of `SetRefinement` is not reused: see section 3, the clustering
needs none.

Literature (papers only, no code read):

- Ju, Losasso, Schaefer, Warren 2002: octree contouring, mergeable QEFs, collapse when the error is below a tolerance and
  the collapsed cell passes a topology test on the corner signs.
- Schaefer, Ju, Warren 2007, *Manifold Dual Contouring*: simplification as vertex clustering, one vertex per connected
  surface patch of a cluster, a criterion that keeps the result closed and 2-manifold. Their section on simplification
  shows that merging by the 2002 test alone can create non-manifold edges.
- Ju, Udeshi 2006: intersection-free contouring on an octree (the reason to check intersections exactly).
- Newer: Informatica 2024 compares octree and k-d tree manifold dual contouring over error thresholds (same
  clustering scheme). Nothing found that replaces the above.

## 2. Upper bound (measured before building)

Ideal bottom-up octree merge on the exact-Hermite data, QEF vertex inside the block, all plane distances at most `tol`
cells, no topology test, no transition cost; triangles are about twice the leaves. Surface cells of the uniform grid /
leaves at tol 0.1 (0.25) cells:

| shape | N=32 | N=64 | N=128 |
|---|---|---|---|
| CSG part | 3.7x (4.3x) | 7.0x (9.2x) | 10x (11.6x) |
| L bracket | 4.9x (6.2x) | 12x (17.6x) | 31x (48x) |
| sphere | 3.9x (8.6x) | 7.9x (15.7x) | 17x (51x) |
| torus | 1.2x (3.3x) | 2.4x (5.6x) | 4.5x (14.6x) |

Far above the 2x stop rule on the CSG part. The real numbers (with the topology test, the 2:1 effects of the
clustering and the self-intersection repair) are in `benchmarks/DualContouringSimplification/README.md`.

## 3. Design: vertex clustering of the uniform contour

Not a new octree contourer. The class contours uniformly (`DualContouring`, so the manifold per-patch vertices, ambiguous
face fans and the exact repair are inherited) and then clusters the vertices of the result by the octree of the finest
grid (Schaefer et al. 2007 do the same on their octree). The result is a quotient of a closed manifold mesh, so there are
no cracks and no transitions between cell sizes to build: triangles whose vertices fall into one cluster vanish, the
others connect cluster vertices.

- **Octree.** Block (level L, i, j, k) holds the grid cells with min corner in [i 2^L, (i+1) 2^L) x ... Every contour vertex
  belongs to the block of its cell. Vertices without a QEF (face and edge vertices of fans and envelope triangulation)
  are never merged.
- **Components.** In a block the vertices fall into connected components of the contour graph (edges with both ends in
  the block). Each component is a candidate cluster: two sheets through one block stay two clusters (Schaefer 2007).
  A component at level L is a union of components at level L-1.
- **Mergeable QEF.** The data of a component is the union of the Hermite crossings (point, unit normal; each grid edge
  crossing once, not once per cell) of its vertices. Its QEF `A^T A, A^T b, b^T b` and mass point are the sums, so
  merging children costs the sum of the crossing lists (the union avoids counting a crossing shared by four cells
  four times). The vertex is the QEF minimum inside the block box shrunk by `QEFCellMargin` (the solver of
  `DualContouring`, truncated eigenvalues).
- **Merge test (bottom up).** A component merges if (1) all its child components merged (octree: parent only over leaves),
  (2) the vertex lies inside the block, (3) every Hermite plane of the component is within `Tolerance` finest cells of
  the vertex (max, not sum: it bounds the distance to the Hermite planes and does not grow with the number of crossings).
- **Topology test.** After the local test the quotient mesh is built and checked combinatorially around the clusters:
  every edge in exactly two triangles (a triangle that would give an edge a third one is left out and shows as a hole),
  every vertex link one cycle, no triangle below the degenerate area of `NotCleanReason` (three cluster vertices on one
  crease would give a zero-area triangle). Components of offending vertices
  lose a level (their cap), and the pass repeats. This is the global form of Ju 2002's corner-sign test and Schaefer's
  manifold criterion; it is exact, not conservative, so it keeps more merges than a local rule.
- **Self-intersections.** The quotient is checked exactly (`ExactTriangleIntersection`, AABB tree, as the base). Merged
  clusters in intersecting pairs lose a level; repeat. A result is returned only when the check is clean or all
  remaining pairs involve only unmerged vertices (inherited from the base, which reports `BaseSelfIntersectionFree`).
- **Termination.** Every repair step lowers the cap of at least one cluster, caps are bounded below by 0 (the base
  mesh), so it ends (after 200 rounds, which was not seen, it returns to the uniform contour). `Tolerance = 0` merges nothing: the result equals the uniform contour of the base (same vertices,
  same triangles).

## 4. Guarantees and what is only reported

Guaranteed (checked by the algorithm, tests use `MeshRobustRepair.NotCleanReason`): the result is a closed, 2-manifold
triangle mesh if the base contour is; vertices are finite; no merged vertex takes part in a self-intersection
(`SelfIntersectionFree` is true iff the base was). Within-tolerance accuracy is guaranteed against the Hermite data
(max plane distance of a merged cluster at most `Tolerance` cells), not against the surface: the distance to the true
surface is measured (benchmark, tests with analytic shapes), it is small where the Hermite planes are close to the
surface (curvature adds a sagitta of about block^2 / 8R). The topology of the base is kept, not improved.
Reported: base results (`NonManifoldTriangles`, `BaseSelfIntersectionFree`), merges per level, rounds of repair.

## 5. Cost and limits

The base contour is uniform, so time and memory scale with the finest grid, not with the output (the simplifier keeps about 60 bytes per vertex per level up to `MaxLevel`; simplification adds
roughly 20-60 % to the contour time, see the benchmark). A top-down sparse variant (evaluate coarse blocks first, refine
only where the error is large) would save the dense sampling and is the follow-up; it needs the multi-level transition
logic that `SetRefinement` does not have.
`Tolerance` is in finest cells (scale invariant: a power-of-two scale of the input gives the same result bit for bit).
Opt-in: `DualContouring` itself is unchanged except for an internal recording hook (`RecordVertexCrossings`) and
`solve_qef` being internal.

## 6. Default on (`DualContouring.Simplify`)

`DualContouring.Generate` runs `AdaptiveDualContouring` (inner contour with `Simplify` false) when `Simplify` is true
(default), and the uniform contour as before otherwise. It applies only to the dense mode with `Implicit`, `ExactHermite`,
`ResolveSelfIntersections` and none of `HermiteF`, sparse mode (`CandidateCells`), refinement, hidden gap / wall functions or
recording; so `MeshOffset`, `MeshWindingNumberSolidify`, `WindingContouring` (own Hermite data or sparse) and everything the
command line reaches through them give the same output as before (they also set `Simplify = false` explicitly). With `Simplify`
false the output is bit for bit the former one (digest test on four shapes, taken on master before the change).
Tests that inspect the uniform contour (internal accessors, certificates, face vertices, accuracy of the unsimplified contour)
set `Simplify = false`. `SimplifyTolerance` (0.25 finest cells) and `SimplifyMaxLevel` (6) are the two parameters.

Cost. A sparse top-down construction was measured first (AGENT_WORKFLOW 3): at N = 256, tolerance 0.25, grid sampling and edge
crossings are 8-15 % of the time, the vertex QEFs 36-40 %, the exact check of the uniform contour 17-24 %, the clustering 26-31 %.
A sparse grid alone saves at most 8-15 %; skipping the fine cells of flat regions needs bounds on the implicit and is not part of
this default (a possible opt-in, roadmap). What was done instead: the exact self-intersection check runs once on the (30x
smaller) final mesh instead of on the uniform contour first; if it finds intersections that no merged vertex is part of (the
uniform contour had its own), the uniform contour is repaired as before and the merge runs again, so a returned mesh is always
the result of an exact check (measured: a gyroid at N = 48, test `Gyroid_ResultHasNoSelfIntersections`). The component setup of the
clustering allocates lists only for components of several children. Time at N = 256: uniform plus 32-43 % (was 64-76 % in #995; benchmark README), N = 128: plus 7-85 %.

Orientation. A needle of three nearly collinear cluster vertices can turn over without intersecting anything (six triangles on the
sphere at cell 0.1, found by `DualContouringTests.Sphere_IsClosedManifoldAndOrientedOutwards`); the assembly rejects a triangle
whose normal turns against the uniform contour's.

Triangle quality is measured in the benchmark README (smallest angle: 1st percentile, mean, share below 5 and 10 degrees). The
simplified contour has a lower mean smallest angle (about 35 against 41 degrees) and more slivers at creases (CSG part: 4.5 % of
the triangles below 10 degrees at tolerance 0.25 against 0.4 %); smooth shapes are close to the uniform contour.
