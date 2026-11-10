# Intersection-free dual contouring (Phase 5 item 4, hard open problem 13)

Status: **decided (owner 2026-11-10): option A, then option B, one PR each** (section 4). **Options A and B are
implemented** (sections 5 and 6). Before A, nothing here was implemented
yet except a measurement hook
(`DualContouring.CollectDiagnostics`, internal, off by default) and the benchmark
[`benchmarks/DualContouringIntersections`](../../benchmarks/DualContouringIntersections/README.md).

Goal (owner 2026-11-10): a contouring that is self-intersection-free by construction or by a local exact repair, keeps
the QEF sharpness where it can and reports where it cannot. Users: `DualContouring` (implicit and TPMS inputs),
`WindingContouring` (`MeshWindingNumberSolidify` / `MeshWindingNumberBoolean`, the repair path), sharp features for
the alpha wrap (lever 6 variant B) and so tet meshing stage 1.

## 1. What the library does today

- `DualContouring` (manifold DC: one QEF vertex per marching-cubes patch of a cell, Schaefer, Ju & Warren 2007;
  Nielson 2004 is the same dual of the MC patches) already keeps every vertex strictly inside its cell: the QEF is
  minimised over the cell box shrunk by `QEFCellMargin` (1 % of the cell). Measured: 0 of 4.2 M vertices outside
  their cell (section 2), so "how far QEF vertices leave their cells" is 0 by construction.
- Quads are split along the diagonal whose two triangle normals agree best (`append_quad`); a quad that would create a
  non-manifold edge (an ambiguous face where both cells have one patch) is left out (`NonManifoldTriangles`, a hole;
  `WindingContouring` fills such holes afterwards, unchecked).
- `ResolveSelfIntersections` (default on): an exact check of all triangles (`ExactTriangleIntersection` over an AABB
  tree, parallel), then up to 10 rounds that move the vertices of intersecting pairs toward fallbacks (the rank-1 QEF
  point, the ring centroid at refinement transitions, finally the cell centre or crossing centroid). It reports
  `SelfIntersectionsFound / Left` and `SelfIntersectionFree`. It is a check after the fact with a heuristic repair: no
  argument that 10 rounds suffice, and it moves sharp vertices.

## 2. Upper bound (measured, `benchmarks/DualContouringIntersections`)

Inputs: the lever 6 set (20 T1 Thingi10K meshes of #498 and the 6 procedural CAD solids, through
`MeshWindingNumberSolidify`, adaptive, cell = diagonal / 64 and / 128) and 6 implicit shapes on a dense grid (rotated
box, box minus sphere, union of two boxes with concave creases, two plates with a sub-cell gap, a cone tip, a gyroid
sheet), each under 20 random rotations and grid shifts at 24 / 48 / 96 cells across. Counts are of the last `Generate` of a run (the adaptive path can run several); times come from separate
runs with the hook off (`--timing`). Command and tables:
the benchmark README.

| input | res | runs | raw SI runs | SI pairs | left after repair (runs) | final SI (runs) | non-manifold tris (runs) | moved / vertices | multi-patch polys | 1 / 2 concave (of uniform single-patch quads) | SI triangles in / out / multi-patch / transition | covered / all pairs |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 9 | 149 | 0 | 0 | 0 (0) | 149 / 161019 | 0.65 % | 2.27 % / 0.020 % | 20 / 0 / 74 / 40 | 0 / 149 |
| T1 | 128 | 20 | 6 | 101 | 0 | 1 | 12 (1) | 110 / 432218 | 0.28 % | 1.86 % / 0.006 % | 14 / 2 / 77 / 20 | 0 / 101 |
| imp:boxminussphere | 24 | 20 | 5 | 32 | 0 | 0 | 0 (0) | 29 / 23458 | 2.33 % | 6.33 % / 0.061 % | 1 / 2 / 29 / 0 | 0 / 32 |
| imp:boxminussphere | 48 | 20 | 4 | 30 | 0 | 0 | 0 (0) | 29 / 95682 | 1.08 % | 3.17 % / 0.037 % | 0 / 0 / 27 / 0 | 0 / 30 |
| imp:boxminussphere | 96 | 20 | 11 | 74 | 0 | 0 | 0 (0) | 67 / 387456 | 0.52 % | 1.63 % / 0.015 % | 2 / 1 / 70 / 0 | 0 / 74 |
| imp:cone | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 14 / 10976 | 1.86 % | 7.93 % / 0.028 % | 0 / 0 / 14 / 0 | 0 / 14 |
| imp:cone | 48 | 20 | 7 | 64 | 0 | 0 | 0 (0) | 60 / 44614 | 1.05 % | 3.86 % / 0.018 % | 0 / 0 / 60 / 0 | 0 / 64 |
| imp:cone | 96 | 20 | 8 | 131 | 0 | 0 | 0 (0) | 73 / 179628 | 0.46 % | 2.04 % / 0.015 % | 2 / 1 / 88 / 0 | 0 / 131 |
| imp:gyroid | 24 | 20 | 20 | 906 | 0 | 0 | 0 (0) | 926 / 56988 | 81.18 % | 18.20 % / 0.809 % | 16 / 25 / 868 / 0 | 0 / 906 |
| imp:gyroid | 48 | 20 | 20 | 1857 | 0 | 0 | 1708 (20) | 1616 / 283302 | 69.87 % | 6.84 % / 0.135 % | 13 / 16 / 1851 / 0 | 0 / 1857 |
| imp:gyroid | 96 | 20 | 20 | 4055 | 0 | 0 | 96 (13) | 2426 / 1190410 | 3.62 % | 6.15 % / 0.045 % | 21 / 17 / 2893 / 0 | 0 / 4055 |
| imp:rotbox | 24 | 20 | 2 | 12 | 0 | 0 | 0 (0) | 10 / 14924 | 2.32 % | 5.96 % / 0.103 % | 0 / 0 / 10 / 0 | 0 / 12 |
| imp:rotbox | 48 | 20 | 1 | 3 | 0 | 0 | 0 (0) | 4 / 61060 | 1.16 % | 3.09 % / 0.032 % | 0 / 0 / 4 / 0 | 0 / 3 |
| imp:rotbox | 96 | 20 | 6 | 30 | 0 | 0 | 0 (0) | 32 / 247330 | 0.63 % | 1.51 % / 0.015 % | 0 / 0 / 33 / 0 | 0 / 30 |
| imp:slab | 24 | 20 | 7 | 60 | 0 | 0 | 0 (0) | 59 / 15418 | 55.30 % | 13.18 % / 0.377 % | 0 / 0 / 65 / 0 | 0 / 60 |
| imp:slab | 48 | 20 | 18 | 549 | 0 | 0 | 10104 (18) | 534 / 85814 | 50.13 % | 4.65 % / 0.137 % | 2 / 4 / 537 / 0 | 0 / 549 |
| imp:slab | 96 | 20 | 4 | 27 | 0 | 0 | 0 (0) | 24 / 409052 | 18.00 % | 2.25 % / 0.017 % | 2 / 2 / 21 / 0 | 0 / 27 |
| imp:twoboxes | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 15 / 14532 | 3.85 % | 10.94 % / 0.122 % | 0 / 0 / 14 / 0 | 0 / 14 |
| imp:twoboxes | 48 | 20 | 5 | 21 | 0 | 0 | 0 (0) | 23 / 60186 | 1.84 % | 5.50 % / 0.058 % | 2 / 2 / 20 / 0 | 0 / 21 |
| imp:twoboxes | 96 | 20 | 3 | 30 | 0 | 0 | 0 (0) | 26 / 246250 | 0.93 % | 2.99 % / 0.030 % | 0 / 0 / 25 / 0 | 0 / 30 |
| mesh:procedural | 64 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 41400 | 0.00 % | 0.10 % / 0.000 % | 0 / 0 / 0 / 0 | 0 / 0 |
| mesh:procedural | 128 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 164508 | 0.00 % | 0.04 % / 0.000 % | 0 / 0 / 0 / 0 | 0 / 0 |

vertices not strictly inside their cell: 0 of 4226225; quads with 3 or 4 concave vertices: 0
stages of 116871.stl at 128: final SI 63, contour (with hole fill) 63, after cleanup 63, non-manifold triangles left out 12, last DC pass SI left 0

| input | res | runs | total s | DC phases s | check + repair s | share of DC | share of total |
|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 9.1 | 4.5 | 1.4 | 30 % | 15 % |
| T1 | 128 | 20 | 24.4 | 13.1 | 4.0 | 30 % | 16 % |
| imp:boxminussphere | 24 | 20 | 0.3 | 0.2 | 0.1 | 46 % | 40 % |
| imp:boxminussphere | 48 | 20 | 1.0 | 1.0 | 0.4 | 36 % | 34 % |
| imp:boxminussphere | 96 | 20 | 4.1 | 4.0 | 1.8 | 43 % | 43 % |
| imp:cone | 24 | 20 | 0.1 | 0.1 | 0.0 | 51 % | 39 % |
| imp:cone | 48 | 20 | 0.4 | 0.3 | 0.2 | 51 % | 45 % |
| imp:cone | 96 | 20 | 1.7 | 1.6 | 0.7 | 45 % | 43 % |
| imp:gyroid | 24 | 20 | 0.6 | 0.6 | 0.2 | 41 % | 39 % |
| imp:gyroid | 48 | 20 | 2.9 | 2.9 | 1.4 | 48 % | 47 % |
| imp:gyroid | 96 | 20 | 13.5 | 13.5 | 6.0 | 45 % | 44 % |
| imp:rotbox | 24 | 20 | 0.3 | 0.2 | 0.1 | 41 % | 37 % |
| imp:rotbox | 48 | 20 | 0.7 | 0.7 | 0.3 | 41 % | 38 % |
| imp:rotbox | 96 | 20 | 3.6 | 3.5 | 1.2 | 34 % | 33 % |
| imp:slab | 24 | 20 | 0.1 | 0.1 | 0.1 | 64 % | 52 % |
| imp:slab | 48 | 20 | 0.8 | 0.8 | 0.5 | 57 % | 53 % |
| imp:slab | 96 | 20 | 4.3 | 4.2 | 1.7 | 41 % | 41 % |
| imp:twoboxes | 24 | 20 | 0.2 | 0.2 | 0.1 | 30 % | 26 % |
| imp:twoboxes | 48 | 20 | 0.5 | 0.5 | 0.2 | 51 % | 45 % |
| imp:twoboxes | 96 | 20 | 2.7 | 2.7 | 0.9 | 35 % | 35 % |
| mesh:procedural | 64 | 6 | 2.4 | 1.3 | 0.6 | 47 % | 25 % |
| mesh:procedural | 128 | 6 | 7.8 | 4.6 | 1.7 | 36 % | 21 % |

Findings:

1. **Self-intersections before the repair are common, after it rare.** Raw DC intersects in 15 of 52 winding runs (250 pairs) and in 147 of 360 implicit runs (7909 pairs; every gyroid run). The repair
   left none in any of the 412 runs; it moved 6226 of 4.2 M vertices (0.03-1.6 % per family, most on thin sheets) off their QEF point,
   i.e. the sharpness loss of today's repair is small.
2. **The one self-intersecting result of #498 (116871) comes from the hole fill, not from the repair.** At
   diagonal / 128 the last DC pass is intersection-free, but 12 non-manifold triangles were left out and
   `FillSmallHoles` closed the holes with 63 intersecting pairs (`stage_si_contour` = 63 before cleanup and reduction).
   Left-out non-manifold quads are also the larger defect on thin sheets: up to 10104 triangles left out (slab at
   48 cells) and 1708 (gyroid), i.e. dense DC returns open meshes there (reported only in `NonManifoldTriangles`).
3. **Where the intersections are.** Each triangle of an intersecting pair was classified by the polygon it comes
   from: a quad of four single-patch cells of the same size, with the triangle inside Wang's envelope (section 3.2) or
   outside it; a polygon touching a multi-patch cell; a refinement-transition polygon. Over all runs the intersecting triangles are 95 in-envelope, 72 out-of-envelope, 6780 multi-patch and 60 transition triangles (T1: 34 / 2 / 151 / 60).
   **No intersecting pair had both triangles inside their envelopes** (0 of 8159 pairs), as the criterion predicts.
   Every intersection involves a multi-patch or transition polygon or a quad split against the criterion.
4. **Cost.** With the hook off, the exact check plus repair (`ResolveMilliseconds`) is 30 % of the dual
   contouring time on T1 (15-16 % of the whole `MeshWindingNumberSolidify`, where the octree and the winding numbers
   dominate) and 30-64 % of dense `DualContouring` (median about 44 %). It checks every triangle, although only the
   triangles not covered by the criterion (multi-patch, transition, against-criterion: 1-5 % on CAD-like inputs, up to
   80 % on thin sheets at low resolution) can intersect.
5. **Wang's criterion is cheap to meet.** Of the uniform single-patch quads (four cells of one size, one patch each),
   0.04-0.1 % on the procedural CAD solids, 1.5-8 % on T1 and the smooth or CAD-like implicits at 48-96 cells, and up to
   11-18 % at 24 cells (twoboxes, slab, gyroid) have one concave envelope vertex, so the diagonal through it is forced;
   0-0.07 % have two (where an edge vertex is needed) except on thin sheets and the coarsest grids (rotbox / twoboxes at
   24: 0.10-0.12 %, slab and gyroid at 24-48: 0.14-0.8 %); 3 or 4 never occur (Wang, Remark 3).

So the measurable gains on this set are: a guarantee instead of a heuristic, closed (hole-free) results at ambiguous
faces, no unchecked hole fill, and part of the check time: if the check shrinks to the uncovered triangles, at most
about 1.2x for `MeshWindingNumberSolidify` and 1.3-2x for dense `DualContouring` (Amdahl on the shares above). The
count of self-intersecting final results is small today (1 of 52 winding runs, 0 of 360 implicit runs), so the case
for building rests on the guarantee and the holes; the speed is a side gain, not the reason.

### 2.1 Separate finding: the hole fill after dual contouring is unchecked

`WindingContouring.finish` (adaptive path) and `MeshWindingNumberSolidify` (non-adaptive DC path) call
`WindingContouring.FillSmallHoles` when DC left out non-manifold quads (`NonManifoldTriangles > 0`). The fill
(`MinimalHoleFill`, then `SimpleHoleFiller`, then a fan) is not checked for self-intersections. On 116871 at
diagonal / 128 the last DC pass was intersection-free, 12 triangles were left out, and the filled contour had 63
intersecting pairs, which cleanup and reduction kept (`stage_si_contour` = `stage_si_cleanup` = 63). The flag is
honest (`WindingContouring.SelfIntersectionFree` is false whenever holes were filled), but `Compute()` returns true
and only callers with `StopIfSelfIntersecting` check the result. Option B removes the cause (no left-out quads). If A
ships first, the fill must not pass as clean: A checks the fill triangles exactly against the mesh and reports the
count (or keeps the hole and reports it), so a filled contour with new self-intersections is visible in the outputs.
The non-adaptive call in `MeshWindingNumberSolidify` belongs to another session's file; it gets the same check through
a shared helper in `WindingContouring` or a note to that session.

## 3. How others solve it (prior art, papers only)

### 3.1 Ju & Udeshi 2006 (octree, hybrid primal/dual)

T. Ju, T. Udeshi, *Intersection-free contouring on an octree grid*, Pacific Graphics 2006 (read through Wang's summary;
the paper's host is not reachable from the session). Around every sign-changing grid edge e, a fan of up to 8
triangles (edge vertex on e, face vertex on each face around e, cell vertex in each cell). Theorem: the result is
intersection-free if every cell, face and edge vertex lies strictly inside its cell, face and edge; the triangles of
each edge lie in an "envelope" (a union of tetrahedra (cell vertex, face vertex, e)), and the envelopes of different
edges are disjoint. Works across octree levels (the face vertices sit on the smaller cell's face). Cost: up to 4x the
triangles of DC; rules based on polygon-edge intersection tests drop face / edge vertices where safe.

### 3.2 Wang (uniform grids, convex/concave analysis)

C. C. L. Wang, *Intersection-free dual contouring on uniform grids: an approach based on convex/concave analysis*,
technical report, CUHK (about 2009; read in full). On a uniform grid the face vertex is not needed: with the cell
vertices strictly inside their cells, the segment v_p v_q of two face neighbours crosses their common face inside it.
Envelope of e: the 4 tetrahedra (v_i, v_i+1, e+, e-) of the cell vertices around e; envelopes of different edges are
disjoint (they are a sub-partition of the cells). Per vertex two orient3d signs decide whether it is a *concave
envelope vertex* (v_p below the oriented triangles (v_s, v_q, e+) or (v_q, v_s, e-)):
- none concave: both diagonals lie in the envelope (sharp-feature diagonal rules may choose);
- one concave: only the diagonal through it does;
- two (always neighbours): insert an edge vertex on e, four triangles (v_i, v_i+1, v_e), each inside its tetrahedron.
Only orient3d signs on the actual double coordinates, so it is exact with `ExactPredicates.Orient3d`. Wang's
limitation: sharpness is bounded by vertices kept in their cells (we already pay that).

### 3.3 Others

- Schaefer, Ju & Warren 2007 (manifold DC) and Nielson 2004 (dual marching cubes): manifold, no geometric guarantee.
- J. Manson, S. Schaefer, *Isosurfaces over simplicial partitions of multiresolution grids*, CGF (Eurographics) 2010:
  intersection-free and manifold on octrees by contouring a simplicial partition with vertices restricted to their
  simplices [abstract only; paper host unreachable].
- Kobbelt et al. 2001 (extended MC, feature vertex fans inside cells): primal, no inter-cell guarantee stated.
- Learned or differentiable DC (Neural DC 2022, FlexiCubes 2023, Self-Supervised DC 2024, occupancy-based DC 2024):
  report fewer intersections empirically, no guarantee; ideas only.
- Zint, Grosso, Guertler 2022 (refine at ambiguous faces; already used for `NonManifoldCells`).

Nothing found that guarantees both intersection-free output and QEF sharpness inside multi-patch cells; the
guarantees above assume one vertex per cell (Ju & Udeshi, Wang) or give up the free QEF vertex (Manson & Schaefer).

## 4. Options

Cover = a triangle that lies, by an exact argument, in a region disjoint from every other polygon's region.

| | what | guarantee | sharpness | cost to build | runtime |
|---|---|---|---|---|---|
| **A. Certify + local check** | keep today's vertices; split each single-patch same-size quad by Wang's criterion (forced diagonal; edge vertex at the Hermite point, snapped strictly inside e, for two concave); the exact check runs only on uncovered triangles (multi-patch, transition, exactly coplanar tests) against the tree; today's repair only for those, re-classifying the quads around moved vertices | covered triangles by proof; the rest checked exactly, repair still heuristic (reported) | as today, plus a forced diagonal on 0.04-8 % of quads (up to 18 % on coarse grids and thin sheets; may cut a crease the normal-based choice kept) | S-M (DualContouring only; no API change) | check cost ~ uncovered share: at most ~1.2x faster solidify, 1.3-2x dense DC |
| **B. A + Ju-Udeshi faces** | as A, and faces next to multi-patch cells or refinement transitions get face vertices (one per face segment), so each such cell's triangles are fans from its vertices inside the cell; ambiguous faces then give two separate face vertices and no non-manifold edge (no holes, no unchecked fill) | everything covered except fans of different patches inside one multi-patch cell, which are checked exactly within the cell (a few triangles) | QEF vertices kept; extra face vertices only at those faces (a few % of triangles) | M | as A; closes the `NonManifoldTriangles` holes |
| C. Full hybrid everywhere | Ju & Udeshi on every edge, then drop vertices by the criterion | as B | as B | M-L | 1-4x triangles before dropping |
| D. Primal fallback | multi-patch cells contoured by marching tetrahedra inside the cell | covers multi-patch cells too, but the face polylines must match the neighbours (needs B's face vertices) and positions on interior simplex edges round off them | QEF lost in those cells | L | small |
| E. Keep today, document | no change | none (heuristic, never failed in 412 runs here) | as today | 0 | as today |

Decision (owner 2026-11-10): **A, then B**, as recommended. Recommendation: **A, then B** (two PRs, one feature each). A is small, exact and does not cost time (the check becomes
local, so the guarantee is cheaper than today's after-the-fact check) and gives the per-polygon classification B needs.
B removes the holes (the source of the one bad #498 result) and the transition cases. The multi-patch interior stays
"checked exactly, repaired heuristically, reported" (`UncoveredTriangles`, `SelfIntersectionsLeft`): option D or a
proof for it is the remaining open problem 13, and only worth it if the reported counts show it matters.
`WindingContouring` post-processing (cleanup, reduction) keeps its own checks; with B the unchecked hole fill goes
away for DC holes.

Acceptance for A (proposed): bit-identical output where today's DC already chooses the criterion's diagonal and
nothing intersects; 0 self-intersections and `SelfIntersectionFree` on all benchmark runs; covered + uncovered
counts reported; check time down by at least the uncovered share; invariance over `Similarity.All()` (2^k bit for
bit), reuse, parallel = serial; tests that fail on the old code (eg a quad whose normal-based diagonal leaves the
envelope and intersects a neighbour, built by hand); the hole fill of section 2.1 checked and reported; benchmark
before / after with this benchmark.

## 5. Option A, implemented

`DualContouring.Generate` classifies every dual quad: four single-patch cells of one size whose vertices are strictly
inside their cells (exact comparisons with the cell corners as computed for the grid). For those it decides Wang's
criterion with `ExactPredicates.Orient3d` (`ConcaveEnvelopeVertices`):
- no concave vertex: today's normal-based diagonal (both are covered), so the output is unchanged there;
- one: the diagonal through it (`ForcedDiagonals`);
- two, or an exactly coplanar test: an edge vertex on the grid edge (`EdgeVertices`), at the edge's surface point
  along it (the coordinates across the edge are the nodes', so it lies exactly on the segment; strictly between the
  end nodes, else the midpoint), and four triangles (v_i, v_i+1, v_e), each inside its tetrahedron
  (v_i, v_i+1, e+, e-).

These triangles are covered (`CoveredTriangles`). The exact self-intersection check starts from the others only
(`CheckedTriangles`: multi-patch, transition and degenerate polygons, vertices not strictly in their cells), each
against the whole mesh; after a repair move, the triangles around the moved vertices are checked again, as before, so
every triangle that is not checked is covered at its current position. `SelfIntersectionFree` keeps its meaning.

`WindingContouring` checks the hole fill exactly (`FinishHoles` / `FillSmallHolesChecked`: the triangles the fill
added or changed, including those at the bowtie vertices it splits, whose coincident copies touch, are checked against
the whole mesh, so the count agrees with `MeshRepairMetrics.SelfIntersections`; `HoleFillIntersections`), and its `SelfIntersectionFree` is true only if the fill added none
(holes the fill could not close may remain). Not in this PR: the non-adaptive call in `MeshWindingNumberSolidify`
still calls the unchecked `FillSmallHoles` (its `knownFree` stays false there, so nothing is reported wrongly), and
`Compute()` still returns true after a fill with intersections; both belong to another session's file and switch to
`FinishHoles` there.

Result on the same 412 runs (`benchmarks/DualContouringIntersections`, A/B with the #541 head as baseline, two
repetitions each, alternating, hook off; medians per input):

| input | res | runs | total s base / cand | ratio | DC s base / cand | ratio | check + repair s base / cand | ratio |
|---|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 8.8 / 8.2 | 0.92 | 4.5 / 3.6 | 0.79 | 1.25 / 0.20 | 0.16 |
| T1 | 128 | 20 | 23.9 / 21.9 | 0.92 | 12.7 / 10.5 | 0.82 | 4.14 / 0.87 | 0.21 |
| imp:boxminussphere | 24 | 20 | 0.2 / 0.1 | 0.50 | 0.2 / 0.1 | 0.42 | 0.11 / 0.01 | 0.14 |
| imp:boxminussphere | 48 | 20 | 0.9 / 0.7 | 0.74 | 0.9 / 0.6 | 0.73 | 0.35 / 0.07 | 0.22 |
| imp:boxminussphere | 96 | 20 | 3.9 / 3.1 | 0.79 | 3.8 / 3.0 | 0.79 | 1.67 / 0.39 | 0.23 |
| imp:cone | 24 | 20 | 0.1 / 0.1 | 0.59 | 0.1 / 0.0 | 0.41 | 0.04 / 0.00 | 0.06 |
| imp:cone | 48 | 20 | 0.4 / 0.3 | 0.86 | 0.3 / 0.3 | 0.82 | 0.15 / 0.03 | 0.20 |
| imp:cone | 96 | 20 | 1.6 / 1.3 | 0.80 | 1.5 / 1.2 | 0.79 | 0.71 / 0.17 | 0.24 |
| imp:gyroid | 24 | 20 | 0.7 / 0.4 | 0.68 | 0.6 / 0.4 | 0.66 | 0.23 / 0.22 | 0.93 |
| imp:gyroid | 48 | 20 | 2.9 / 3.4 | 1.19 | 2.8 / 3.4 | 1.19 | 1.39 / 1.39 | 0.99 |
| imp:gyroid | 96 | 20 | 14.1 / 11.4 | 0.81 | 14.0 / 11.3 | 0.81 | 6.30 / 2.73 | 0.43 |
| imp:rotbox | 24 | 20 | 0.2 / 0.2 | 0.87 | 0.2 / 0.1 | 0.85 | 0.08 / 0.02 | 0.23 |
| imp:rotbox | 48 | 20 | 0.7 / 0.6 | 0.80 | 0.7 / 0.6 | 0.79 | 0.30 / 0.07 | 0.23 |
| imp:rotbox | 96 | 20 | 2.8 / 2.4 | 0.88 | 2.7 / 2.4 | 0.88 | 1.14 / 0.36 | 0.31 |
| imp:slab | 24 | 20 | 0.1 / 0.2 | 1.12 | 0.1 / 0.1 | 1.15 | 0.07 / 0.04 | 0.63 |
| imp:slab | 48 | 20 | 1.1 / 0.9 | 0.90 | 1.0 / 0.9 | 0.89 | 0.51 / 0.38 | 0.76 |
| imp:slab | 96 | 20 | 3.9 / 3.3 | 0.84 | 3.9 / 3.3 | 0.84 | 1.72 / 0.70 | 0.41 |
| imp:twoboxes | 24 | 20 | 0.1 / 0.1 | 0.75 | 0.1 / 0.1 | 0.66 | 0.06 / 0.00 | 0.07 |
| imp:twoboxes | 48 | 20 | 0.7 / 0.4 | 0.66 | 0.6 / 0.4 | 0.65 | 0.21 / 0.05 | 0.23 |
| imp:twoboxes | 96 | 20 | 2.5 / 2.2 | 0.87 | 2.4 / 2.1 | 0.87 | 1.07 / 0.24 | 0.23 |
| mesh:procedural | 64 | 6 | 2.4 / 2.1 | 0.87 | 1.2 / 0.9 | 0.74 | 0.55 / 0.08 | 0.15 |
| mesh:procedural | 128 | 6 | 8.2 / 6.3 | 0.77 | 4.9 / 3.4 | 0.69 | 1.74 / 0.14 | 0.08 |

- 7.59 M of 8.45 M triangles (90 %) are covered; 135887 diagonals forced, 1278 edge vertices. 0 intersecting pairs
  between two covered triangles (8053 pairs, all triangles checked with the hook on, covered ones included).
- The check and repair take 6-31 % of their former time on CAD-like and smooth inputs (3-16x faster) and 41-99 % on
  thin sheets (gyroid, slab), where most triangles stay uncovered (multi-patch). Dual contouring as a whole takes
  0.41-0.88 of its former time; the T1 solidify 0.92 (the bound was about 1.2x). The two rows above 1 (gyroid 48,
  slab 24) are within the noise of their 0.1-3 s totals (their check time did not change).
- Unchanged: 0 self-intersections left after the repair in all 412 runs; the one bad final result (116871 at 128) is
  the hole fill, now reported (`HoleFillIntersections`, `SelfIntersectionFree` false); option B removes its cause.

## 6. Option B, implemented

Face vertices (Ju & Udeshi 2006) on the faces where the dual polygons cannot be covered or cannot stay manifold:
- faces between cells of different size (refinement transitions; the face is the smaller cell's face, inside the
  coarse cell's face);
- ambiguous faces between two single-patch cells: both segments of the face would join the same two cell vertices,
  so dual contouring left out the quads (`NonManifoldTriangles`) and the callers filled the holes.

A face vertex belongs to one segment of its face (on an ambiguous face, each segment cuts off one inside corner,
the face rule of `cell_patches`), lies exactly on the face plane and strictly inside the face, and on an ambiguous face
strictly on its corner's side of the diagonal through the two other corners, so the fans of the two segments are
disjoint. Placement, in order: the in-face QEF point of the segment's Hermite data (the two tangent lines in the face
plane; at a crease through the face, on the surface); else that point clamped into the face, the QEF minimum on the
bounding diagonal, the closest allowed point toward the mass point, the dual crossing of the segment between the two
cell vertices, the midpoint of the segment's edge points. Those not at the QEF point are counted
(`ClampedFaceVertices`, `ClampedFaceCells`). A polygon with a face vertex becomes a fan around an edge vertex on its
grid edge (at the cut of the edge with the Hermite tangent plane, strictly between the end nodes): each triangle
(cell vertex, face vertex, edge vertex) lies in the tetrahedron (cell vertex, face, edge) inside one cell, and a dual
face of the same polygon gives (v_p, v_q, v_e) inside Wang's tetrahedron. The fan is covered if all its cell vertices
have one patch and are strictly inside their cells.

Not next to multi-patch cells: a first version put face vertices there too; they cost sharpness (the in-face QEF
point was often outside the allowed region; `ContouringQualityTests.RotatedBox_SharpCornersAndExactVolume` failed with
a vertex 0.31 cells off the input, against 0.14 with A) and gave no guarantee, since the
fans of different patches in one cell are checked anyway. The multi-patch interior stays checked exactly and repaired
heuristically, reported (`CheckedTriangles`, `SelfIntersectionsLeft`).

Result on the 412 runs (diagnostics, hook on):

| input | res | runs | raw SI runs | SI pairs | left after repair (runs) | final SI (runs) | non-manifold tris (runs) | moved / vertices | multi-patch polys | 1 / 2 concave (of uniform single-patch quads) | SI triangles in / out / multi-patch / transition | covered / all pairs |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 4 | 121 | 0 | 0 | 0 (0) | 76 / 165010 | 0.63 % | 2.26 % / 0.021 % | 0 / 0 / 74 / 0 | 0 / 121 |
| T1 | 128 | 20 | 5 | 74 | 0 | 0 | 0 (0) | 65 / 435227 | 0.28 % | 1.86 % / 0.006 % | 0 / 0 / 77 / 0 | 0 / 74 |
| imp:boxminussphere | 24 | 20 | 4 | 29 | 0 | 0 | 0 (0) | 27 / 23472 | 2.33 % | 6.33 % / 0.061 % | 0 / 0 / 28 / 0 | 0 / 29 |
| imp:boxminussphere | 48 | 20 | 4 | 30 | 0 | 0 | 0 (0) | 29 / 95717 | 1.08 % | 3.17 % / 0.037 % | 0 / 0 / 27 / 0 | 0 / 30 |
| imp:boxminussphere | 96 | 20 | 10 | 72 | 0 | 0 | 0 (0) | 64 / 387514 | 0.52 % | 1.63 % / 0.015 % | 0 / 0 / 70 / 0 | 0 / 72 |
| imp:cone | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 14 / 10979 | 1.86 % | 7.93 % / 0.028 % | 0 / 0 / 14 / 0 | 0 / 14 |
| imp:cone | 48 | 20 | 7 | 64 | 0 | 0 | 0 (0) | 60 / 44622 | 1.05 % | 3.86 % / 0.018 % | 0 / 0 / 60 / 0 | 0 / 64 |
| imp:cone | 96 | 20 | 8 | 129 | 0 | 0 | 0 (0) | 68 / 179654 | 0.46 % | 2.04 % / 0.015 % | 0 / 0 / 88 / 0 | 0 / 129 |
| imp:gyroid | 24 | 20 | 20 | 869 | 0 | 0 | 0 (0) | 868 / 57062 | 81.18 % | 18.20 % / 0.809 % | 1 / 0 / 860 / 0 | 0 / 869 |
| imp:gyroid | 48 | 20 | 20 | 1836 | 0 | 0 | 0 (0) | 1583 / 285980 | 69.87 % | 6.84 % / 0.135 % | 5 / 0 / 1844 / 0 | 0 / 1836 |
| imp:gyroid | 96 | 20 | 20 | 4026 | 0 | 0 | 0 (0) | 2381 / 1191069 | 3.62 % | 6.15 % / 0.045 % | 1 / 0 / 2889 / 0 | 0 / 4026 |
| imp:rotbox | 24 | 20 | 2 | 12 | 0 | 0 | 0 (0) | 10 / 14939 | 2.32 % | 5.96 % / 0.103 % | 0 / 0 / 10 / 0 | 0 / 12 |
| imp:rotbox | 48 | 20 | 1 | 3 | 0 | 0 | 0 (0) | 4 / 61079 | 1.16 % | 3.09 % / 0.032 % | 0 / 0 / 4 / 0 | 0 / 3 |
| imp:rotbox | 96 | 20 | 6 | 30 | 0 | 0 | 0 (0) | 32 / 247366 | 0.63 % | 1.51 % / 0.015 % | 0 / 0 / 33 / 0 | 0 / 30 |
| imp:slab | 24 | 20 | 7 | 60 | 0 | 0 | 0 (0) | 59 / 15443 | 55.30 % | 13.18 % / 0.377 % | 0 / 0 / 65 / 0 | 0 / 60 |
| imp:slab | 48 | 20 | 18 | 548 | 0 | 0 | 0 (0) | 528 / 101538 | 50.13 % | 4.65 % / 0.137 % | 2 / 0 / 537 / 0 | 0 / 548 |
| imp:slab | 96 | 20 | 3 | 24 | 0 | 0 | 0 (0) | 20 / 409110 | 18.00 % | 2.25 % / 0.017 % | 0 / 0 / 21 / 0 | 0 / 24 |
| imp:twoboxes | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 15 / 14549 | 3.85 % | 10.94 % / 0.122 % | 0 / 0 / 14 / 0 | 0 / 14 |
| imp:twoboxes | 48 | 20 | 5 | 18 | 0 | 0 | 0 (0) | 18 / 60220 | 1.84 % | 5.50 % / 0.058 % | 0 / 0 / 20 / 0 | 0 / 18 |
| imp:twoboxes | 96 | 20 | 3 | 30 | 0 | 0 | 0 (0) | 26 / 246323 | 0.93 % | 2.99 % / 0.030 % | 0 / 0 / 25 / 0 | 0 / 30 |
| mesh:procedural | 64 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 41418 | 0.00 % | 0.10 % / 0.000 % | 0 / 0 / 0 / 0 | 0 / 0 |
| mesh:procedural | 128 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 164518 | 0.00 % | 0.04 % / 0.000 % | 0 / 0 / 0 / 0 | 0 / 0 |

vertices not strictly inside their cell: 0 of 4252809; quads with 3 or 4 concave vertices: 0

- `NonManifoldTriangles` 0 in all runs (A: 11920 triangles left out in 52 runs); final self-intersecting results 0
  (A: 1, 116871, from the hole fill); intersecting triangles of transition polygons 0 (A: 60); 0 covered pairs.
- 9959 face vertices, 6287 of them clamped (mostly thin sheets: slab, gyroid at 48 cells), 17062 edge vertices;
  triangles +0.1 %. The refinement transition test (a sphere with random refined regions) is covered completely:
  `CheckedTriangles` 0.

Time (A/B against master with option A, hook off, two repetitions alternating):

| input | res | runs | total s base / cand | ratio | DC s base / cand | ratio | check + repair s base / cand | ratio |
|---|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 7.4 / 7.4 | 1.00 | 3.3 / 3.3 | 1.00 | 0.18 / 0.17 | 0.93 |
| T1 | 128 | 20 | 19.2 / 20.7 | 1.08 | 9.4 / 10.0 | 1.07 | 0.82 / 0.50 | 0.62 |
| imp:boxminussphere | 24 | 20 | 0.1 / 0.1 | 1.30 | 0.1 / 0.1 | 1.46 | 0.01 / 0.01 | 0.89 |
| imp:boxminussphere | 48 | 20 | 0.6 / 0.6 | 1.00 | 0.6 / 0.6 | 1.00 | 0.07 / 0.07 | 1.11 |
| imp:boxminussphere | 96 | 20 | 2.7 / 2.8 | 1.04 | 2.6 / 2.8 | 1.04 | 0.40 / 0.36 | 0.91 |
| imp:cone | 24 | 20 | 0.0 / 0.0 | 0.96 | 0.0 / 0.0 | 0.80 | 0.00 / 0.00 | 0.33 |
| imp:cone | 48 | 20 | 0.2 / 0.3 | 1.23 | 0.2 / 0.2 | 1.34 | 0.03 / 0.03 | 1.13 |
| imp:cone | 96 | 20 | 1.2 / 1.2 | 1.01 | 1.1 / 1.1 | 1.01 | 0.15 / 0.14 | 0.91 |
| imp:gyroid | 24 | 20 | 0.4 / 0.4 | 1.06 | 0.4 / 0.4 | 1.09 | 0.20 / 0.21 | 1.06 |
| imp:gyroid | 48 | 20 | 2.8 / 2.9 | 1.05 | 2.7 / 2.9 | 1.05 | 1.26 / 1.29 | 1.03 |
| imp:gyroid | 96 | 20 | 9.6 / 9.9 | 1.04 | 9.5 / 9.9 | 1.04 | 2.21 / 2.25 | 1.02 |
| imp:rotbox | 24 | 20 | 0.1 / 0.2 | 1.33 | 0.1 / 0.1 | 1.38 | 0.01 / 0.01 | 1.06 |
| imp:rotbox | 48 | 20 | 0.4 / 0.5 | 1.25 | 0.4 / 0.5 | 1.28 | 0.07 / 0.07 | 1.01 |
| imp:rotbox | 96 | 20 | 2.0 / 2.2 | 1.10 | 1.9 / 2.1 | 1.10 | 0.29 / 0.31 | 1.09 |
| imp:slab | 24 | 20 | 0.1 / 0.1 | 1.27 | 0.1 / 0.1 | 1.35 | 0.04 / 0.05 | 1.05 |
| imp:slab | 48 | 20 | 0.8 / 1.0 | 1.18 | 0.8 / 0.9 | 1.19 | 0.35 / 0.42 | 1.20 |
| imp:slab | 96 | 20 | 3.1 / 3.0 | 0.98 | 3.0 / 2.9 | 0.98 | 0.68 / 0.62 | 0.91 |
| imp:twoboxes | 24 | 20 | 0.1 / 0.1 | 0.91 | 0.1 / 0.1 | 0.88 | 0.00 / 0.01 | 4.14 |
| imp:twoboxes | 48 | 20 | 0.4 / 0.3 | 0.75 | 0.4 / 0.3 | 0.71 | 0.07 / 0.04 | 0.59 |
| imp:twoboxes | 96 | 20 | 2.0 / 2.0 | 1.02 | 1.9 / 2.0 | 1.01 | 0.25 / 0.21 | 0.84 |
| mesh:procedural | 64 | 6 | 1.8 / 2.0 | 1.14 | 0.8 / 1.0 | 1.24 | 0.07 / 0.07 | 1.02 |
| mesh:procedural | 128 | 6 | 5.5 / 5.8 | 1.04 | 2.9 / 3.5 | 1.19 | 0.12 / 0.13 | 1.08 |

Dual contouring takes 0-10 % longer on T1 and the implicit shapes at 96 cells (the face test of every polygon), up to
24 % on the six procedural solids, where dual contouring is a small part (runs under a second are noisy); the T1
solidify as a whole is within -14 % to +8 % (less refinement for left-out quads; the second run is in the benchmark
README).

Sharpness, the refinement alternative (owner's question; measured with an internal switch, then removed; table in the
benchmark README): refining around clamped face vertices in the adaptive path cost about 10 % time and 3 % triangles on
T1 and changed no deviation measurably (RotatedBox 0.143 cells and 16 vertices above 0.02 cells with A, B and B
refining; T1 maximum and counts within 2 %). 6287 of the 9959 face vertices are clamped, mostly on thin sheets.
**Decision (owner 2026-11-10): option B without refinement.** A QEF minimised over the allowed region (convex: the face
rectangle, on an ambiguous face the triangle on the corner's side) instead of the clamp was not built: the deviation is
already at A's level; the clamp chain already takes the QEF minimum on the bounding diagonal of an ambiguous face.

Prior art refresh (2026-11-10, papers only): Occupancy-Based Dual Contouring (J. Hwang, M. Sung, SIGGRAPH Asia 2024,
arXiv 2409.13418; abstract and project page) is learning-free, builds on manifold DC, finds edge points by binary
search and uses Ju and Udeshi's quad splitting to reduce self-intersections, without a guarantee. Nothing newer with
a guarantee on vertex placement was found ("dual contouring intersection-free guarantee vertex placement 2024 2025");
a 2026 preprint on subgrid marching tetrahedra (arXiv 2606.00454) was found by title only, not read.
