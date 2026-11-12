# A topology certificate for the contouring (study, Phase 5 item 4 follow-up)

Status: **study only, nothing built; waits for the owner's decision** (section 6). Approved as a study by the owner on
2026-11-12. No library code changed; the measurement is
[`benchmarks/ContouringTopology`](../../benchmarks/ContouringTopology/README.md) (public API only).

Question: can `DualContouring` / `WindingContouring` (with the intersection-free options A and B,
[DC_INTERSECTION_FREE.md](DC_INTERSECTION_FREE.md)) guarantee that the output has the topology of the level set
(isotopic, or at least homeomorphic), and at what cost?

Short answer:

- **Today the topology is wrong often enough to matter**, and without any signal: every shape with a feature below
  about 1.5 cells (section 2), 2-3 of 20 clean lever-6 inputs at every cell size from diagonal / 64 to / 256 (4 of the 14 clean real meshes at one
  of them at least), sharp tips at every
  resolution.
- **An unconditional guarantee does not exist** for these inputs in the literature (section 1): every method needs a
  regular level set and smoothness or a feature-size bound, none covers winding-number fields, and all of them refine
  without end at a degenerate spot.
- **A certificate is within reach**: per grid cell an exact test that the level set crosses the cell the way the mesh
  does; cells that fail are refined up to a limit and then reported. The output is proven isotopic to the level set when
  no cell is reported, and the reported cells say where it is not proven. This is the shape of options A and B
  (covered by proof, the rest checked and reported), and it reuses their coverage for the mesh side of the proof.
- Recommended order: clean closed meshes first (exact, cheapest, the repair use), then interval implicits (TPMS),
  then broken inputs away from their defects. For broken inputs the statement can only be "the topology changed
  nowhere except within a stated distance of the defects": their level set is not smooth at the input and has no
  meaning of its own near a hole.

## 1. Prior art (papers only)

Read status per source: **R** read in this study, **S** from a paper that restates it, **A** abstract only,
**M** from memory, not re-read (several paper hosts are not reachable from the session; M and A items need a check
before code is built on them). CGAL appears as its papers only.

| method | guarantee | assumptions | per-step test | cost / termination | on a winding-number field |
|---|---|---|---|---|---|
| Snyder, *Interval analysis for computer graphics*, SIGGRAPH 1992 (A, criterion M) | the mesh has the topology of the surface in every cell | inclusion functions for f and its derivatives; regular level set | global parameterizability: an interval partial derivative excludes 0 over the box (the surface is a graph over a coordinate plane), recursively on faces and edges | subdivision; incomplete: needs the right topology per cell, so it does not stop at a tangency with the grid (S: Burr, Choi, Galehouse, Yap) | not applicable as is: no gradient at the input surface |
| Plantinga & Vegter, *Isotopic approximation of implicit curves and surfaces*, SGP 2004; Visual Computer 2007 (S: Burr et al. arXiv 1102.5463, Cucker et al. arXiv 2004.06879) | ambient isotopy of the whole surface (not per cell); no Hausdorff bound unless refined further | f is C1, 0 a regular value, bounded zero set, interval functions for f and grad f | subdivide a box until 0 is not in f(B), or 0 is not in the interval inner product of grad f(B) with itself (no two gradients in B orthogonal); octree balanced 2:1; marching-cubes-like mesh with edge vertices | no bound in the paper; later: exponential worst case (Burr, Gao, Tsigaridas 2017, S), polynomial on average and smoothed (Cucker, Ergür, Tonelli-Cueto, DCG 2022, R); never stops at a singular point | fails at every crease: measured, the variation test fails in 1.6-15 % of the cells of clean meshes and the count doubles per level (section 3) |
| Lin & Yap, *Adaptive isotopic approximation of nonsingular curves*, DCG 2011; Lin, Yap, Yu, surfaces, CAD 2013 (A) | isotopy | as Plantinga-Vegter | Snyder's cheaper test (one partial derivative excludes 0) with Plantinga-Vegter's non-local argument | fewer boxes than either | as above |
| Boissonnat & Oudot, *Provably good sampling and meshing of surfaces*, Graphical Models 2005 (R: its 2003 report; Cheng, Dey, Shewchuk, *Delaunay Mesh Generation*, ch. 13) | restricted Delaunay triangulation homeomorphic and isotopic to the surface, Hausdorff O(eps^2 lfs), angle bounds | C2 surface (positive local feature size); an oracle for segment-surface intersections; a lower bound of the distance to the medial axis | refine a facet whose surface Delaunay ball is larger than 0.09 lfs | O(1 / h^2) points; needs a 3D Delaunay triangulation, a different mesher | lfs is 0 at every crease; the Lipschitz-surface version (SoCG 2006, A) replaces lfs by a Lipschitz radius, which is 0 at a sharp tip |
| Cheng, Dey, Ramos, Ray, SoCG 2004 (S); Cheng, Dey, Levine, DelPSC, 2007 (M) | homeomorphism without knowing lfs; DelPSC handles piecewise smooth complexes | critical / silhouette point computations; DelPSC: topological-disk tests, correct only below an unknown scale | topological ball property | Delaunay refinement | closest in spirit for creases, but a different mesher and no bound on the scale |
| Varadhan, Krishnan, Sriram, Manocha, *Topology preserving surface extraction using adaptive subdivision*, SGP 2004 (A, definitions M) | same genus and connectedness as the exact surface; the journal version claims a Hausdorff bound | the two tests must be computable for the input class (shown for polyhedral and CSG Booleans, Minkowski sums, algebraic primitives) | *complex cell* (an edge crossed more than once, a face or cell crossing the corner signs do not show, an ambiguous configuration) and *star-shaped* (the surface in the cell and on each face has a kernel), by distance bounds, linear programming, intervals | adaptive subdivision until every cell passes | the only one stated for polyhedral inputs and a grid mesher; this note's local test is its exact variant |
| Stander & Hart 1997 (M); Boissonnat, Cohen-Steiner, Vegter, *Isotopic implicit surface meshing*, DCG 2008 (A) | isotopy | C2 Morse function, all critical points found | Morse theory | no bound | not applicable (critical points of a harmonic field with singular edges) |
| Chernyaev 1995, Lewiner et al. 2003, Nielson 2003, Custodio et al. 2013; Etiene et al. 2012 (A) | homeomorphic to the level set of the *trilinear interpolant* of the samples | none | case tables | none | says nothing about the sampled function |
| Chazal & Cohen-Steiner, *A condition for isotopic approximation*, 2004 / Graphical Models 2005 (A); used by Mandad, Cohen-Steiner, Alliez, SIGGRAPH 2015 | two homeomorphic surfaces are isotopic if one lies in a topological thickening of the other and separates its two sides | the thickening must be known to be one (S x [0,1]) | an after-the-fact check | cheap once an envelope exists | a second, independent oracle for clean meshes (`PolyhedralEnvelope`), section 4.6 |
| Sharp & Jacobson, *Spelunking the deep*, TOG 2022 (R) | no cell with surface is pruned; no topology statement | range analysis (interval, affine) of the function | - | - | - |
| Persistence: Cohen-Steiner, Edelsbrunner, Harer 2007 (M); Bendich, Edelsbrunner, Kerber 2010 (A) | homology classes with persistence above twice the sampling error are real | a sup-norm error bound of the samples | cubical persistence | near linear | homology only; the error bound does not exist at the jump |

The winding-number setting:

- Jacobson, Kavan, Sorkine-Hornung 2013 (R): w is harmonic away from the input and jumps by 1 across a facet; for
  open or non-manifold inputs it is smooth elsewhere and degrades near the open boundaries. No gradient bound.
- Barill et al. 2018 (A): the tree approximation is controlled by beta and the expansion order, measured, with no
  proven bound found; Spainhour, Whitlock, Weiss 2026 (R, arXiv 2605.19200) say explicitly that rounding decisions
  near half-integers are not guaranteed. The library's `FastWindingNumber` documents errors up to about 0.1 and has
  no bound either.
- Martens, Trettner, Bessmeltsev, *The Antipodal Method*, TOG 2026 (R, arXiv 2605.01536; the library's
  LEVY_CLASSIFICATION_WINDING2026 note): exact w as signed ray crossings plus a line integral over the boundary; its
  supplement gives the gradient of w as a sum over the boundary edges only (the Biot-Savart form). So the gradient is
  0 off the surface of a closed oriented mesh, grows like 1 / distance to an open, non-manifold or inconsistently
  oriented edge (a *defect edge*), and has no global Lipschitz constant.
- **Nothing found** that proves the topology of a surface extracted from winding numbers, a rigorous error bound of a
  tree winding number, or isotopy for general or neural implicits beyond the Plantinga-Vegter family (terms searched:
  isotopic / topology guarantee with neural implicit, interval arithmetic, Lipschitz, winding number, dual contouring,
  2015-2026).

What this means for the three kinds of input:

1. **Smooth implicit with interval functions (TPMS, analytic shapes):** Plantinga-Vegter / Snyder apply as published.
   With `min` / `max` (CSG, sharp features, the reason dual contouring is used) the function is only Lipschitz; the
   graph test still makes sense with the set of all gradients of the active pieces (section 4.3), the published proofs
   do not cover it.
2. **Winding number of a clean closed mesh:** the level set *is* the input surface. No field theory is needed and
   none applies; the tests become exact predicates on the triangles in a cell (Varadhan's setting).
3. **Winding number of a broken mesh:** w mod 1 is smooth everywhere except on the defect edges; the level set is
   made of input triangles and smooth harmonic sheets that end on defect edges. No published method covers the
   neighbourhood of a defect edge.

## 2. Upper bound: how often is today's topology wrong?

`benchmarks/ContouringTopology` (commands in its README; master 1f3b6b7, 4 cores, 2026-11-12). Topology = the number
of components and the genus of each (`MeshTopologySummary`), which for closed orientable surfaces is the same
information as the Betti numbers. Equal counts do not prove isotopy (a knotted or linked result would pass), so every
count below is a **lower bound** of the true error rate. These are counts of results, not time shares.

### 2.1 Implicit shapes, dense `DualContouring` (truth by construction)

Six shapes with one feature of known width (the tube of a torus, a tunnel through a ball, the gap between two balls, a
rod joining two balls, the wall of a hollow ball, nine holes in a plate), the width given in cells, 20 random
rotations and grid shifts each, at 48 and 96 cells across (1920 runs; the two resolutions agree within 3 runs per
row):

| feature width / cell | runs | wrong topology |
|---|---|---|
| 0.25 | 240 | 240 (100 %) |
| 0.5 | 240 | 240 (100 %) |
| 0.75 | 240 | 214 (89 %) |
| 1 | 240 | 198 (82 %) |
| 1.5 | 240 | 50 (21 %) |
| 2 | 240 | 0 |
| 3 | 240 | 0 |
| 4 | 240 | 0 |

All 1920 results are closed and free of self-intersections; nothing in the outputs says which are wrong. Typical wrong
results: a rod in 2-11 pieces, a hollow ball's wall as up to 2500 fragments or one surface of genus 1495, two balls
merged.

The shapes of `DualContouringIntersections` (20 seeds each, 24 / 48 / 96 cells across):

| shape | truth | wrong at 24 | at 48 | at 96 | cause |
|---|---|---|---|---|---|
| sphere, rotated box | 1 component, genus 0 | 0 | 0 | 0 | |
| union of two boxes | 1, genus 0 | 0 | 0 | 1 | an extra component |
| cone with a sharp tip | 1, genus 0 | 2 | 5 | 3 | the tip is thinner than a cell at every resolution and splits off |
| box minus sphere | 1, genus 0 (agrees at 192 and 288) | 3 | 2 | 1 | a thin sliver |
| two plates 0.05 thick, 0.03 apart | 2, genus 0 | 20 | 20 | 1 | gap 0.3 / 0.7 / 1.3 cells |
| gyroid sheet in a ball | not known | - | - | - | the reference does not converge: three rotations of the same shape give genus 64, 71 and 69-70 at 192 and 288 cells |

So a sharp tip or an acute edge breaks the topology at any resolution with some probability, and for the gyroid
(the TPMS use) even 288 cells across give no trustworthy reference: without a certificate nobody can tell.

### 2.2 The lever-6 set, `MeshWindingNumberSolidify` (adaptive, default settings)

The 20 T1 Thingi10K meshes of #498 and the 6 procedural CAD solids, cell = diagonal / 64, 128, 256 (and 512 as the
reference of the inputs that are not clean). 20 inputs are clean (closed, oriented, no self-intersections, no
bowties): for them the truth is exact, the topology of the input components across which the exact winding number
steps between 0 and 1. For the 6 others the finest run is a reference, not a truth.

| input class | cell | runs | raw contour differs | default result differs |
|---|---|---|---|---|
| clean, exact truth (14 T1 + 6 procedural) | diagonal / 64 | 20 | 2 | 2 |
| | / 128 | 20 | 3 | 3 |
| | / 256 | 20 | 2 | 2 |
| not clean, against the run at / 512 (6 T1) | / 64 | 6 | 4 | 4 |
| | / 128 | 6 | 4 | 4 |
| | / 256 | 6 | 3 | 3 |

The wrong results on clean inputs (truth -> result; all closed and free of self-intersections):

| input | truth | / 64 | / 128 | / 256 | hidden gap + wall edges reported (64 / 128 / 256) |
|---|---|---|---|---|---|
| 116871 | 3 components, genus 3 | 1, genus 0 | 3, genus 1 | 6, genus 4 | 0 / 1 / 0 |
| 472011 | 1, genus 0 | right | 1, genus 2 | 1, genus 3 | 3 / 5 / 1 |
| 79961 | 1, genus 6 | right | 1, genus 7 | right | 0 / 1 / 0 |
| 96773 | 1, genus 9 | 10, genus 8 | right | right | 0 / 0 / 0 (1337 cells refined) |

- 4 of the 14 clean T1 meshes are wrong at one resolution at least, 7 of their 42 runs; the 6 procedural solids are
  right in all 18 runs. Spurious handles appear (472011, 79961) as well as lost ones.
- Cleanup and reduction change nothing here: the default result differs wherever the raw contour does (at / 512 once
  more: 66112 loses a component in the cleanup).
- The inputs that are not clean do not converge: 1038438 has genus 1, 5, 42, 53 at 64, 128, 256, 512; three of the six
  still change between 256 and 512. For them "the topology of the level set" is not known from any run.
- The hidden gap / wall counter is non-zero in 4 of the 7 wrong clean runs (below `MinRefineEdges`, so nothing was
  refined), 0 in 3, and non-zero in 1 right run.
- Time (raw contour, one run each, not a timing benchmark): / 128 0.1-1.9 s, / 256 0.7-8.8 s per input.

### 2.3 What the bound says

- The guarantee changes results in practice: in the stratum "feature below 2 cells" nearly always, in the stratum
  "clean real meshes" in 7 of 42 runs (4 of 14 inputs), in the stratum "blocky CAD solids"
  never (0 of 18).
- Finer grids alone do not fix it (116871 is wrong at / 64, / 128 and / 256 in three different ways, 472011 at
  / 128 and / 256 but not at / 64).
- Today's hidden-gap / hidden-wall detector (`HiddenGapEdges`, `HiddenWallEdges`: grid edges the surface crosses
  twice) already sees part of it, but refines only from `MinRefineEdges` = 8 edges on and misses the other part.

## 3. Cost probe: how many cells would fail a local test?

A floating-point probe (`probe:` inputs, `Probe.cs`; not a certificate): on a uniform grid, for every cell the input
surface passes through, (a) the number of connected sheets of the surface in the cell, (b) whether a direction d
with d . n > 0 for all its triangle normals exists (the surface is a graph over a plane: Snyder's test for an
arbitrary direction), (c) Plantinga-Vegter's test (every pair of normals has a positive inner product). Shares of
the surface cells; stratum: the lever-6 set, all triangles of the input (for inputs that are not clean this includes
interior triangles that are not on the level set):

| set | cell | inputs | surface cells | (a) more than one sheet | (b) no graph direction | (a) or (b) | (c) variation test fails |
|---|---|---|---|---|---|---|---|
| procedural | / 64 | 6 | 41484 | 0.05 % | 0.00 % | 0.05 % | 6.96 % |
| | / 128 | 6 | 165060 | 0.03 % | 0.00 % | 0.03 % | 3.51 % |
| | / 256 | 6 | 663810 | 0.01 % | 0.00 % | 0.01 % | 1.76 % |
| | / 512 | 6 | 2653189 | 0.01 % | 0.00 % | 0.01 % | 0.88 % |
| T1 clean | / 64 | 14 | 64487 | 2.71 % | 1.93 % | 2.95 % | 15.03 % |
| | / 128 | 14 | 265588 | 0.32 % | 0.03 % | 0.33 % | 6.51 % |
| | / 256 | 14 | 1063774 | 0.22 % | 0.01 % | 0.22 % | 3.27 % |
| | / 512 | 14 | 4258204 | 0.10 % | 0.00 % | 0.10 % | 1.61 % |
| T1 not clean | / 64 | 6 | 40756 | 15.41 % | 12.42 % | 17.38 % | 27.40 % |
| | / 128 | 6 | 169149 | 12.37 % | 9.88 % | 12.92 % | 18.53 % |
| | / 256 | 6 | 729126 | 10.35 % | 7.79 % | 10.49 % | 13.38 % |
| | / 512 | 6 | 2942184 | 9.43 % | 7.81 % | 9.47 % | 10.93 % |

- (a) or (b) failing is an **upper bound** of what an exact test would refuse: a cell with two sheets is fine for
  manifold dual contouring when a grid node separates them (it has one vertex per sheet). Along an acute edge two
  faces share cells at every resolution without touching inside them, which is why the count does not fall faster.
- The graph test (b) alone fails in 0.00-0.03 % of the cells of clean inputs from 128 on; the variation test (c) fails
  in 1.6-15 % and its count doubles with each level, because every cell on a crease of 90 degrees or more fails it.
  A Plantinga-Vegter certificate would refine every sharp edge without end; the graph test does not.
- Cost of refining the failing cells two levels (16 surface cells each, as `MaxRefineLevels` = 2 does today): at
  most +47 % cells at 64 and +5 % at 128 on clean T1, +1 % on the CAD solids.
- Time of the probe itself at / 128 (one run, wall time including its own triangle-to-cell binning, which the octree
  already has; floating point): 2.7 s for the 26 inputs against 15.4 s for their raw solidify, 18 % in total, median
  15 % per input (5-51 %). Stratum: this set at this cell size; an exact version adds the predicate filters' cost.

## 4. Recommended approach

### 4.1 The statement

Let L be the level set (the boundary of the region above the iso value) and D the dual contouring mesh. For every
run: `TopologyCertified` (bool) and the list of **uncertified cells**. If the list is empty, D is isotopic to L. If
not, D and L agree outside the union of the listed cells in the sense of 4.2 (same crossings of every other grid
edge, face and cell), and the list is where a user or a refinement has to look. A certificate, not an unconditional
guarantee: at a degenerate spot (a surface touching itself, a tip of angle 0, a level set through a grid node) no
finite refinement passes, in this as in every published method.

### 4.2 The grid lemma (proof outline)

Call a closed surface S *compatible* with the grid and a sign pattern of the grid nodes if

- (E) no grid node lies on S, and every grid edge meets S in at most one point, crossing it: exactly the edges whose
  end signs differ;
- (F) on every grid face, S is a set of disjoint arcs, each joining two crossing points on the face's boundary, and
  no closed curve; with four crossing points, the pairing is the one named by the face rule;
- (C) in every cell, S is a set of disjoint disks, each bounded by one cycle of the arcs on the cell's boundary.

**Lemma.** Two surfaces compatible with the same grid, signs and pairings are ambient isotopic.
Outline: move the crossing points along their edges (one point per edge on both). On each face the arcs are disjoint,
join the same end points in the same pairing, and there are no loops, so they are isotopic in the face with the end
points fixed. In each cell both surfaces are now disjoint disks properly embedded in a ball with the same boundary
curves; such disks are isotopic with the boundary fixed (Alexander's theorem for tame disks in a ball). The pieces
agree on shared faces, so they glue to an isotopy of space. For the two-level grid of the adaptive path, faces and
edges are those of the finer side. This cell-by-cell argument is Snyder's and Varadhan's; it is standard, but it has
to be written out in full before code is built on it (stage 0).

**The mesh side is already there.** A dual polygon that options A and B count as covered lies in Wang's tetrahedra
(cell vertex, cell vertex, e+, e-) or in the fans of B's face and edge vertices. Then D crosses the grid edge e once,
crosses each face between two cells along one arc through the point where the segment of the two cell vertices meets
it, and is a cone from the cell vertex inside a single-patch cell: D satisfies E, F and C wherever its triangles are
covered. So `CoveredTriangles` is the mesh half of the certificate; a multi-patch cell needs the same three checks on
its few triangles (exact, by `ExactPredicates.Orient3d`) or is listed.

**What is new is the level-set side:** proving E, F and C for L. That needs an oracle per input class.

### 4.3 The cell test that works for creases

(C) follows from two cheaper facts: (F) holds on the six faces, and a direction d exists along which the function is
strictly monotone in the cell. Then L in the cell is a graph over the plane normal to d; a graph has no closed
component, and a component with a hole would leave the cell through a face in a closed curve, which (F) excludes. So
every component is a disk. For a smooth f the test is d . grad f > 0 on the interval gradient; for a piecewise
function (`min` / `max`, a polyhedron) on the gradients or normals of all pieces in the cell. A box corner passes
with d = (1, 1, 1), a cone tip with its axis, any convex or concave edge with its bisector; the variation test of
Plantinga and Vegter fails on all of them. A vertex whose normals surround it (a saddle with more than 360 degrees)
has no such direction; there the fallback is the star-shaped test (a point that sees the whole patch from the
positive side: a linear program in three variables) or the Euler characteristic of the clipped patch.

### 4.4 Oracles per input class

**P: winding number of a clean closed oriented mesh** (L = the input components across which w steps 0 to 1). All
exact, on the triangles the octree already hands to each cell (`WindingNumberOctree.refine`):

- (E) crossings of a grid edge with the triangles, exactly (segment against triangle by `Orient3d`; the edge is
  axis-parallel and its coordinates are doubles, so most signs are coordinate comparisons). Today's `HiddenGap` /
  `HiddenWall` and `MaxEdgeHits` do this in floating point.
- Node signs: the parity of the exact crossings along edges fixes every node's sign from one exact evaluation far
  outside, so the certificate does not depend on the fast winding number's error (no bound exists for it).
- (F) walk the cut of the triangles with the face's plane from each crossing point to the next through triangle
  adjacency; a cut not reached from a crossing is a closed curve. Predicates on points defined by an edge and a
  plane: the indirect predicates of [INDIRECT_PREDICATES.md](INDIRECT_PREDICATES.md).
- (C) per sheet in the cell: the graph test as an exact sign test of a candidate d against the triangle normals
  (candidates: the sum of the normals, then a few perceptron steps; a failed search is "uncertified", never wrong);
  fallback as in 4.3. The exact 3D LP feasibility solver that [exact-boolean.md](../roadmap/exact-boolean.md) lists
  as a candidate building block would make the search complete.

**S: implicit functions with interval extensions.** A new optional interface next to `ImplicitFunction3d`
(enclosures of the value and of the gradient over a box, on `ExactInterval`), implemented for the analytic shapes,
the TPMS families and the `min` / `max` / offset operators of `implicit/` (the gradient set of `min(f, g)` over a box
is the hull of both where both can be active). (E): the derivative along the edge excludes 0, or the value excludes
the iso value. (F): one in-plane derivative excludes 0, and with four crossings the value keeps one sign on a
separating segment. (C): as 4.3. A plain `Func<Vector3d, double>` gets no certificate; a Lipschitz constant alone
(Kalra & Barr 1989) can exclude cells but cannot prove a crossing pattern.

**W: winding number of a broken mesh.** Away from defect edges and from self-intersections the level set is locally
the input surface or nothing, and class P applies once the integer levels on both sides are certified: one exact
evaluation of w at an anchor (the library's exact `WindingNumber`, or the antipodal method) plus the bound
|grad w| <= sum over the defect edges of 1 / (2 pi distance), from a tree over the defect edges (usually few against
the triangle count). Free-space cells crossed by a harmonic sheet are class S with that gradient enclosure. Cells
within a stated distance of a defect edge, and cells where a harmonic sheet meets input triangles, are listed. The
statement for a repair is then: *the result has the input's topology except inside the listed cells, all within
distance r of the defects*. A closed but self-intersecting input has no defect edges; its level set is the outer
boundary of the arrangement, which the exact resolve already computes exactly and which is the first choice in
`MeshRobustRepair`; the contouring is the fallback for what that path refuses.

### 4.5 Refinement

Uncertified cells are refined like hidden gaps today (`ContourRefined`, `LocalRefinement`, `MaxRefineLevels`), with
no minimum count; what is left at the limit is reported. This subsumes the hidden-gap / hidden-wall detector (a
violated (E)). It needs no balanced octree: the lemma is stated on the two-level grid B's face vertices already
handle.

### 4.6 Tests and oracles

- Truth by construction (the shapes of section 2.1) and the exact truth of clean meshes (2.2): the certificate must
  never be true on a wrong result (this is the mutation target), and the certified share is the quality measure.
- An independent oracle for clean meshes: Chazal and Cohen-Steiner's condition with `PolyhedralEnvelope` (D inside
  the input's envelope, equal genus per component), on the procedural solids where the envelope is known to be a
  thickening.
- Invariance over `Similarity.All()` (the certificate is combinatorial: bit for bit under 2^k), reuse, parallel equals
  serial; the exact tests fall under the exact-code rules (no FMA, no `float`, a reviewed parallel loop).

### 4.7 Cost against today's contouring

- Class P check: per surface cell, the edge crossings (already computed for the Hermite data), one face walk per
  face with crossings and one normal test over the cell's triangles. The floating-point
  probe of section 3 costs a median 15 % of the raw solidify at / 128 (5-51 %), with its own binning; the exact
  tests behind floating-point filters should stay in that order, to be measured by stage 1's spike before it is built.
- Refinement: bounded by section 3 (clean T1: at most +47 % cells at 64, +5 % at 128; CAD solids +1 %); on thin
  features the cost is the price of resolving them, which is the point (feature width / 2 per cell, section 2.1).
- Class S: one interval evaluation of value and gradient per surface cell and face instead of 8 point samples; the
  interval versions cost a small multiple of a point evaluation, measured in stage 3's spike before it is built.
- Class W: the Biot-Savart bound over the defect edges per cell near them; nothing for clean regions.
- No cost when the option is off; results are unchanged where nothing is refined.

## 5. Stages

| stage | what | size | gate |
|---|---|---|---|
| 0 | the lemma and the mesh side written as a full proof (review by a second session), the M and A sources of section 1 re-read; the truth harness of section 2 as tests | S, docs and tests | the proof holds; a counterexample ends the approach |
| 1 | class P, report only: `TopologyCertified` and the uncertified cells on `WindingContouring` / `MeshWindingNumberSolidify` for clean inputs; exact (E), (F), (C) | M (tier L rules: exact code) | never true on a wrong result (sections 2.1, 2.2 and a fuzz run); certified share and time reported; a spike first measures the check time against the DC phases |
| 2 | refinement driven by the uncertified cells; replaces the `MinRefineEdges` rule | S-M | the three wrong clean inputs of 2.2 become right or stay reported; cells and time within the bound of section 3 |
| 3 | class S: the interval interface, the built-in shapes and operators, the certificate in `DualContouring` (dense and two-level) | M | the families of 2.1 certified from 2 cells on; the gyroid gets a certified reference |
| 4 | class W: defect edges, the gradient bound, "topology changed only near the defects" | M-L, research | decided after stage 2 on the 6 inputs that are not clean and the 800 run's solidify fallbacks |
| 5 | optional: the non-local relaxation of Plantinga-Vegter / Lin-Yap (fewer refinements at tangencies) | M | only if stage 2 / 3 show refinement chains that matter |

Stages 0-2 make `WindingContouring` a certified path for clean inputs (the alpha wrap's sharp-feature variant and
the repair of meshes whose defects are elsewhere); stage 3 serves TPMS and implicit inputs; stage 4 is the repair
claim proper and the least certain.

## 6. Options for the owner

| | what | gain | cost | risk |
|---|---|---|---|---|
| **A. Stages 0-2** | certificate and refinement for clean closed meshes | wrong topology is detected and mostly fixed where the truth is exact; 4 of 14 clean real meshes today | S + M + S-M, exact code | the proof (stage 0) is standard; the face walk is the fiddly part |
| B. A, then stage 3 | plus interval implicits | TPMS / implicit inputs get a certificate; thin features resolved or reported | + M, a new optional interface | interval functions for user implicits are opt-in |
| C. A, B, then stage 4 | plus broken inputs away from defects | the repair statement | + M-L | research; may end as "report only" |
| D. Detection only | compare components and genus of the result with the input's (clean inputs) and report a mismatch; lower `MinRefineEdges` | cheap signal for the cases of 2.2 (misses knots and compensating errors, says nothing about where) | S | no guarantee |
| E. Nothing | document the 2-cell rule | - | 0 | silent wrong topology stays |

Recommendation: **A now (stage 0 first, as its own small step), D's mismatch report inside it as the test oracle, B
when TPMS is scheduled, C decided on stage 2's numbers.**

How others solve it: the table of section 1; in short, interval subdivision (Snyder; Plantinga & Vegter; Lin & Yap)
for smooth implicits, Delaunay refinement with a feature-size bound (Boissonnat & Oudot; CGAL's surface mesher, as
papers) for smooth surfaces with an intersection oracle, complex-cell and star-shaped tests on an adaptive grid
(Varadhan et al.) for polyhedral and CSG inputs, and nothing for winding-number fields.
