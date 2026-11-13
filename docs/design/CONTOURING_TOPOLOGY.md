# A topology certificate for the contouring (study, Phase 5 item 4 follow-up)

Status: **study and proofs; nothing built here.** Owner decision 2026-11-13 ([roadmap/research.md](../roadmap/research.md)
item 4): option A (stages 1-2) is built in its own PR; for B and C the proof first. The proofs (section 7: Theorems
1-5, 1b, and what holds for broken inputs) were written and independently reviewed on 2026-11-13. Approved as a study by the owner on
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
  (covered by proof, the rest checked and reported). The mesh side of the proof needs only what they already give:
  no self-intersections, no left-out polygon, vertices strictly inside their cells (section 7.3).
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

- What the probe does and does not say (revised with the proof of section 7): it is an indication of how many cells
  are *not simple* (several sheets, or no graph direction), not a bound of what the exact test refuses. The exact test
  for meshes (section 4.4) accepts a cell with several sheets when a grid node separates them, and refuses a cell
  with one flat sheet that bulges through a grid edge twice; neither is counted here. The share of refused cells is
  the first thing stage 1's spike has to measure.
- Along an acute edge two faces share cells at every resolution without touching inside them, which is why (a) does
  not fall faster.
- The graph test (b) alone fails in 0.00-0.03 % of the cells of clean inputs from 128 on; the variation test (c) fails
  in 1.6-15 % and its count doubles with each level, because every cell on a crease of 90 degrees or more fails it.
  A Plantinga-Vegter certificate would refine every sharp edge without end.
- If the cells counted under "(a) or (b)" were all refined two levels (16 surface cells each, as
  `MaxRefineLevels` = 2 does today): +47 % cells at 64 and +5 % at 128 on clean T1, +1 % on the CAD solids.
- Time of the probe itself at / 128 (one run, wall time including its own triangle-to-cell binning, which the octree
  already has; floating point): 2.7 s for the 26 inputs against 15.4 s for their raw solidify, 18 % in total, median
  15 % per input (5-51 %). Stratum: this set at this cell size; an exact version adds the predicate filters' cost.

## 4. Recommended approach

### 4.1 The statement

Let L be the level set (the boundary of the region above the iso value) and D the dual contouring mesh. For every
run: `TopologyCertified` (bool) and the list of **uncertified cells**. If the list is empty, D is isotopic to L. If
not, D and L agree outside the union of the listed cells in the sense of 4.2 (same crossings of every other grid
edge, face and cell; this is a statement about those cells, not an isotopy of the rest; and a node sign is checked only through a
path of checked edges to the padding, so a node surrounded by uncertified cells is not covered by it), and the list is where a user or a refinement has to look. A certificate, not an unconditional
guarantee: at a degenerate spot (a surface touching itself, a tip of angle 0, a level set through a grid node) no
finite refinement passes, in this as in every published method.

### 4.2 The grid lemma

Call a closed surface S *compatible* with the grid if (E) every grid edge meets S in at most one point, crossing
it, and no grid node lies on S; (F) on every grid face, S is a set of disjoint arcs with their end points on the
face's boundary, and no closed curve; (C) in every cell, S is a set of disjoint disks with their boundaries on the
cell's boundary. Its *pattern* is the set of crossed edges and, on a face with four crossing points, which two are
joined. **Two surfaces compatible with the same grid and the same pattern are ambient isotopic** (Theorem 1,
section 7.2, proved in full).

**The mesh side needs nothing new** (Theorem 2, section 7.3): a manifold dual contouring mesh whose cell vertices
are strictly inside their cells and whose triangles meet only in common vertices and edges is compatible, with the
sign-changing edges and the face rule as its pattern. So `SelfIntersectionFree` (exact, from the coverage of options A
and B plus the exact check of the other triangles), `NonManifoldTriangles` = 0 and the strict vertex placement are
the mesh half of the certificate, multi-patch cells included. Wang's envelopes are not needed for (E), (F), (C); they
are one way to prove that triangles do not intersect.

**What is new is the level-set side:** proving (E), (F), (C) and the pattern for L, per input class.

### 4.3 The cell test

Correction of this note's first version (found while writing the proof): a direction along which the surface is a
graph, together with (F) on the faces, does **not** give (C). Counterexample: a steep cone around the cube's
diagonal is a graph over the plane normal to the diagonal, crosses every edge at the two diagonal corners once and
every face in one arc, and meets the cube in a tube, not in two disks. What holds (Theorem 4, section 7.5): with
(E), (F) and a graph direction, every part of the surface in the cell is a disk **if the cell's boundary carries
one curve only**, or if the parts are counted and there are as many as curves.

For a polyhedral surface the graph test is not needed at all: the Euler characteristic of each part of the surface in
a cell is `triangles meeting the cell - mesh edges meeting the cell + mesh vertices in the cell` (Theorem 3, section
7.4), a part is a disk exactly if that is 1, and saddle vertices need no special case.

### 4.4 Oracles per input class

**P: winding number of a clean closed oriented mesh** (L = the input components across which w steps 0 to 1). All
exact, on the triangles the octree already hands to each cell (`WindingNumberOctree.refine`), by `Orient2d`,
`Orient3d` and coordinate comparisons on the input's and the grid's doubles only; no constructed point is ever
needed (section 7.4):

- general position: no mesh vertex on a grid plane inside the cells tested, no mesh edge through a grid edge, no
  triangle through a grid node; a violation makes the cell uncertified (or the grid is shifted, `GridOffset`);
- (E) the triangles crossing a grid edge are counted: at most one, and exactly one where the node signs differ. This
  also checks the node signs, so the certificate does not depend on the fast winding number's error (the padding
  nodes are outside the bounding box, hence outside). Today's `HiddenGap` / `HiddenWall` do the even counts in floating
  point;
- (F) the parts of the cut of the mesh with a face (triangles joined across mesh edges that cross the face) each have
  `triangles - mesh edges = 1` (an arc; 0 is a closed curve); which crossing points share a part is the pairing, to
  be compared with the face rule the mesher used;
- (C) the parts of the mesh in a cell (triangles joined across mesh edges and vertices that reach into the cell)
  each have Euler characteristic 1.

**S: implicit functions with interval extensions.** A new optional interface next to `ImplicitFunction3d`
(enclosures of the value and of the gradient over a box, on `ExactInterval`), implemented for the analytic shapes,
the TPMS families and the `min` / `max` / offset operators of `implicit/` (the gradient set of `min(f, g)` over a box
is the hull of both where both can be active). (E): the derivative along the edge excludes 0. (F): one in-plane
derivative excludes 0 (the cut is a graph over an axis of the face, so it has no closed curve), and with four
crossings the value keeps one sign on a segment that separates the two pairs. (C): a graph direction, and either one
curve on the cell's boundary or, with several, the value keeps one sign on surfaces in the cell that separate them
(Theorem 4; the separating surfaces are the test, the parts cannot be counted from enclosures); otherwise refine.
Theorem 1 is not yet proved for level sets that are not PL (section 7.2, remark 2). A plain `Func<Vector3d, double>` gets no certificate; a Lipschitz constant alone
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
`MeshRobustRepair`; the contouring is the fallback for what that path refuses. Not part of the proof of section 7.

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

- Class P check: per surface cell, the edge crossings (already computed for the Hermite data) and three counts
  (triangles, mesh edges, mesh vertices meeting the cell and each of its faces) with a union-find. The floating-point
  probe of section 3 costs a median 15 % of the raw solidify at / 128 (5-51 %), with its own binning; the exact
  tests behind floating-point filters should stay in that order, to be measured by stage 1's spike before it is built.
- Refinement: indicated by section 3 (clean T1: +47 % cells at 64, +5 % at 128; CAD solids +1 %; not a bound); on thin
  features the cost is the price of resolving them, which is the point (feature width / 2 per cell, section 2.1).
- Class S: one interval evaluation of value and gradient per surface cell and face instead of 8 point samples; the
  interval versions cost a small multiple of a point evaluation, measured in stage 3's spike before it is built.
- Class W: the Biot-Savart bound over the defect edges per cell near them; nothing for clean regions.
- No cost when the option is off; results are unchanged where nothing is refined.

## 5. Stages

| stage | what | size | gate |
|---|---|---|---|
| 0 | the lemma and the mesh side written as a full proof: **done, section 7** (2026-11-13; it corrected the cell test, section 4.3); reviewed by a second session on #749, findings worked in; open: the truth harness of section 2 as tests | S, docs and tests | the proof holds; a counterexample ends the approach |
| 1 | class P, report only: `TopologyCertified` and the uncertified cells on `WindingContouring` / `MeshWindingNumberSolidify` for clean inputs; exact (E), (F), (C) by counting (section 7.4) | M (tier L rules: exact code) | never true on a wrong result (sections 2.1, 2.2 and a fuzz run); certified share and time reported; a spike first measures the check time against the DC phases |
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
| **A. Stages 0-2** | certificate and refinement for clean closed meshes | wrong topology is detected and mostly fixed where the truth is exact; 4 of 14 clean real meshes today | S + M + S-M, exact code | the proof (section 7) needs a second reader; general position at grid planes (CAD faces on the grid) decides the certified share |
| B. A, then stage 3 | plus interval implicits | TPMS / implicit inputs get a certificate; thin features resolved or reported | + M, a new optional interface | interval functions for user implicits are opt-in; the proof (Theorem 5, section 7.8) is written and reviewed, its citations of Brown and Clarke are from memory |
| C. A, B, then stage 4 | plus broken inputs away from defects | a partial statement only: inside the certified cells the result differs from the level set by a deformation that keeps every cell in place; never "isotopic to the level set" | + M-L | provable from Theorems 1b, 3, 5 and Lemmas 9.1, 9.2 once rigorous enclosures of the winding number (interval atan2, square root) and of its gradient over a box exist (section 7.9, items 1 and 2); limit: no cell with a piece of a defect edge is ever certified, so the share of the surface it covers is unmeasured |
| D. Detection only | compare components and genus of the result with the input's (clean inputs) and report a mismatch; lower `MinRefineEdges` | cheap signal for the cases of 2.2 (misses knots and compensating errors, says nothing about where) | S | no guarantee |
| E. Nothing | document the 2-cell rule | - | 0 | silent wrong topology stays |

Recommendation: **A now (stage 0 first, as its own small step), D's mismatch report inside it as the test oracle, B
when TPMS is scheduled, C decided on stage 2's numbers.**

How others solve it: the table of section 1; in short, interval subdivision (Snyder; Plantinga & Vegter; Lin & Yap)
for smooth implicits, Delaunay refinement with a feature-size bound (Boissonnat & Oudot; CGAL's surface mesher, as
papers) for smooth surfaces with an intersection oracle, complex-cell and star-shaped tests on an adaptive grid
(Varadhan et al.) for polyhedral and CSG inputs, and nothing for winding-number fields.

## 7. Stage 0: the proof

Everything here is piecewise linear (PL). Sources: only two classical theorems are used, **(J)** the Jordan-Schoenflies
theorem in the plane (a simple closed curve in a disk bounds a disk; an arc properly embedded in a disk cuts it into
two disks) and **(A)** Alexander's theorem (J. W. Alexander 1924: a PL 2-sphere in the 3-sphere bounds a ball on
each side; so a PL disk properly embedded in a ball cuts it into two balls), plus the **cone extension**: a
homeomorphism between the boundaries of two balls (disks, arcs) extends to one between the balls, and a homeomorphism
of a ball that is the identity on its boundary is isotopic to the identity with the boundary fixed (the Alexander
trick). **Nothing is taken from Snyder, Plantinga and Vegter or Varadhan et al.**: their criteria, which this study
could read only as abstracts and through papers that restate them (section 1), are prior art for the idea of a
cell-by-cell test, not premises. The marks in section 1 stay; no step below depends on them.

### 7.1 Definitions

A **grid** is a finite complex K of convex cells that fills a box: the 3-cells are axis-aligned boxes, the 2-cells
(*faces*) rectangles, the 1-cells (*edges*) segments, the 0-cells (*nodes*) points; every cell's boundary is a union
of lower cells, and two cells meet in a union of cells. The uniform grid is one; so is the two-level grid of the
adaptive path, when a coarse cell's side next to finer cells is taken as the finer faces and edges it is made of (a
coarse cell is then a box whose boundary carries more than 6 faces). The outermost layer of cells meets no surface.

A **surface** is a closed PL 2-manifold embedded in the interior of the box. It is **compatible** with K if

- (E) it contains no node, and it meets every edge in at most one point, which is in the edge's interior;
- (F) it meets every face in a finite set of disjoint arcs, each with its two end points in the interiors of edges of
  the face and its interior in the interior of the face (no closed curve, no isolated point);
- (C) it meets every 3-cell in a finite set of disjoint disks, each with its boundary on the cell's boundary and its
  interior in the cell's interior.

By (E) a face carries 0, 2 or 4 end points (one per edge at most; an even number, since they are the ends of arcs).
The **pattern** of a compatible surface is the set of edges it meets and, for every face with 4 end points, the
partition into two pairs joined by an arc. Since the disks of a cell have their interiors inside the cell, the
surface meets the cell's boundary exactly in their boundary circles, which are made of the arcs on its faces.

### 7.2 Theorem 1: same pattern, isotopic

**Theorem 1.** Let S and S' be compatible with K, with the same pattern. Then there is a homeomorphism h of the box
onto itself that maps every cell of K onto itself and S onto S', and h is isotopic to the identity through such
homeomorphisms. In particular S and S' are ambient isotopic (h is the identity on the outer layer, so it extends by
the identity).

*Construction of h, skeleton by skeleton.*

1. Nodes: h is the identity.
2. Edges: on an edge that S meets in x, S' meets it in one point x' (same pattern); h maps the edge to itself
   piecewise linearly with the ends fixed and x to x'. On other edges h is the identity.
3. Faces: let f be a face. h is defined on its boundary, a homeomorphism of that circle which maps the end points of
   the arcs of S to those of S' and, by the same pattern, the pair of ends of each arc a of S to the pair of ends of
   one arc a' of S'. Define h on each a as a homeomorphism onto a' that agrees at the ends. By (J), k disjoint
   properly embedded arcs cut f into k + 1 disks (cut along one arc: two disks; every other arc lies in one of them;
   induct), and which arcs and which pieces of the boundary circle bound a given disk is determined by the cyclic
   order of the end points on the circle and their pairing, which h preserves. So h maps the boundary circle of each
   disk of f minus S onto the boundary circle of a disk of f minus S'; extend over the disk by a cone. Now h maps f
   onto itself and S in f onto S' in f.
4. Cells: let c be a 3-cell. h is defined on its boundary sphere and maps the circles of S there onto the circles of
   S' (step 3). Each circle g bounds exactly one disk D of S in c, and h(g) exactly one disk D' of S'; define h on D
   as a homeomorphism onto D' extending h on g (cone). By (A), m disjoint properly embedded disks cut c into m + 1
   balls (cut along one: two balls, each remaining disk in one of them; induct). A ball B of the cut, bounded by
   the disks D_1 .. D_j, meets the boundary sphere of c in the boundary of B minus j open disks, a sphere with j
   holes, which is connected; and that set contains no further circle of S in its interior, because the disk of such
   a circle starts in it, is disjoint from D_1 .. D_j and so would lie in B, which is a piece of the cut. So each
   ball meets the boundary sphere in exactly one region of the sphere minus the circles (m + 1 balls, m + 1 regions),
   and the disks on the boundary of a ball are those whose circles bound that region.
   The same holds for S', and h maps regions of the sphere to regions. So h maps the boundary sphere of each ball of
   c minus S onto the boundary sphere of a ball of c minus S'; extend by a cone.

h is well defined (each step extends the previous one), a homeomorphism on every cell, hence of the box, maps cells to
themselves and S onto S'.

*h is isotopic to the identity.* On each edge, h fixes the ends: the straight-line isotopy on the edge deforms it to
the identity; extend it over the faces and cells by cones. Then h is the identity on all edges, so on each face it
fixes the boundary: the Alexander trick deforms it to the identity on the face; extend over the cells by cones. Then h
is the identity on all faces and the Alexander trick finishes in each cell. Every stage maps cells to themselves. ∎

Remarks. (1) Only that cells are balls with boundaries made of lower cells is used, so the two-level grid is
covered. (2) The surfaces must be PL (or tame): (A) is false for wild spheres. Input meshes and the output are PL.
For a level set that is not PL (class S) Theorem 1 needs either a PL approximation by a homeomorphism that maps
cells to themselves, or the topological versions of its two tools. A shear along a graph direction, as this note's
first version of the remark proposed, does not do it: it moves points across faces (review of #749). Section 7.8
gives the second route (Theorem 5: locally flat spheres and Brown's theorems); it is not reviewed yet.

### 7.3 Theorem 2: the dual contouring mesh is compatible

Setting: a uniform grid; a sign at every node; for every cell the marching-cubes *patches*, the cycles of
sign-changing edges of the cell given by the signs and the face rule (on a face with four sign-changing edges, which
two are joined); one vertex v_p per patch p. Around a sign-changing edge e lie four cells c_1 .. c_4 in cyclic order,
c_i and c_(i+1) sharing the face f_i that contains e; let p_i be the patch of c_i through e and v_i its vertex. The
*dual polygon* Q_e is the quadrilateral v_1 v_2 v_3 v_4, triangulated either by a diagonal or as four triangles
(v_i, v_(i+1), v_e) around an *edge vertex* v_e in the interior of e. D is the union of the Q_e.

Hypotheses:

- (H1) every v_p lies strictly inside its cell (`QEFCellMargin`; `strictly_in_cell` is an exact comparison);
- (H2) no polygon is left out (`NonManifoldTriangles` = 0) and no triangle is degenerate;
- (H3) two triangles of D meet only in a common vertex or a common edge (`SelfIntersectionFree`, exact);
- (H4) two polygons share a side v_p v_q only if they are the polygons of the two edges joined, on the common face
  of the two cells, by one segment of the face rule (this is how the polygons are built; on an ambiguous face whose
  two segments join the same two patches it fails, which is the case option B's face vertices, treated below,
  remove).

**Theorem 2.** Under (H1)-(H4), D is a closed surface compatible with the grid; its pattern is the set of
sign-changing edges and the face rule.

*Proof.* Let B_e be the union of the four cells around e, a convex box, and P, P' the two grid planes through e (P
contains f_1 and f_3, P' contains f_2 and f_4). By (H1) the v_i are interior points of B_e, so Q_e, which lies in
their convex hull, lies in the interior of B_e. Three facts about the interior of B_e: the only grid edge that meets
it is e (its interior), the only faces that meet it are f_1 .. f_4, and it is cut by P and P' into the interiors of
the four cells.

*(a) The cut of one polygon.* P separates {v_1, v_4} from {v_2, v_3} and contains none of them. So P meets each
triangle of Q_e in a segment, in the single point v_e (a fan triangle on one side of P), or not at all, and Q_e in
a path from x_1, the point where the side v_1 v_2 crosses P,
to x_3 on the side v_3 v_4: with the diagonal v_1 v_3, the segments x_1 y and y x_3 with y the diagonal's point on P;
with the diagonal v_2 v_4 likewise; with an edge vertex, x_1 v_e and v_e x_3. x_1 lies in the interior of f_1 and x_3
in the interior of f_3 (the segment of two interior points of two boxes that share a face crosses it in the face's
interior). The path consists of two straight segments from the open half of P on the side of f_1 to the open half
on the side of f_3, so it meets the line of e exactly once, in a point z_e, which lies in the interior of e (it is in
the interior of B_e). The same holds for P' with a path from x_2 to x_4, and since every point of Q_e on the line of
e is on P, that path passes through the same z_e and meets the first nowhere else. Hence:

- Q_e meets the line of e, and so e, exactly in z_e;
- Q_e meets f_i exactly in an arc a_(e,i) from x_i to z_e (a segment or two), whose interior is in the interior of
  f_i;
- the two paths are properly embedded arcs of the disk Q_e that meet exactly in z_e, and their end points alternate
  on the boundary of Q_e (x_1, x_2, x_3, x_4, one on each side). The alternation, not the local shape at z_e (both
  paths may bend there, when a diagonal's point y lies on the line of e), makes them cross: the first cuts Q_e into
  two disks with x_2 and x_4 in different ones, and each half of the second is a properly embedded arc of one of
  them. So by (J) they cut Q_e into four disks
  K_(e,i), the part of Q_e in c_i, each bounded by the two *spokes* from v_i to x_(i-1) and to x_i (parts of the
  sides of Q_e) and the arcs a_(e,i-1) and a_(e,i); K_(e,i) meets the boundary of c_i exactly in those two arcs.

*(b) (E).* A grid edge e' meets D only in polygons whose block interior it meets, which is Q_(e') alone. So D meets
e' in z_(e') if e' changes sign and not at all otherwise; no node lies on D (nodes are in no block interior).

*(c) (F).* A face f meets only the polygons of its own sign-changing edges. If f has two, e and e', they are joined
by one segment of the face rule and belong to the same two patches p, q on the two sides of f, so both polygons have
the side v_p v_q, crossing f in one point x; D meets f in the arcs from z_e to x and from x to z_(e'), which by (H3)
and (H4) meet only in x (the two polygons meet only in their common side, which meets f only in x): together one
arc from z_e to z_(e'). If f has four, the face rule joins them in two pairs; each pair gives one arc as before, and
the two arcs are disjoint, because by (H4) their polygons share no side, so by (H3) they meet at most in vertices,
none of which lies on f. No other point of D lies on f. So D meets f in arcs with their ends on the sign-changing
edges, paired by the face rule.

*(d) (C).* Let c be a cell and p a patch of c with the cyclic sequence of edges e_1 .. e_k, consecutive ones joined
by a segment of the face rule on a face of c. The pieces K_j of the polygons of e_j in c are disks (a); K_j and
K_(j+1) share the spoke from v_p to the crossing point of the common side, and by (H3) and (H4) two of the K_j meet
only in such a spoke or in v_p. Disks that all contain v_p, joined cyclically along spokes from v_p and otherwise
disjoint, form a disk D_p with v_p in its interior, whose boundary is the cycle of the arcs on the faces of c, that
is the patch's cycle, and whose interior is in the interior of c (a). Disks of different patches of c are disjoint by
(H3), since they share no vertex. D meets c in nothing else. So D meets c in one disk per patch. This holds for
any number of patches: **multi-patch cells need no further condition** than (H1)-(H4).

*(e)* Every point of D has a disk neighbourhood in D (inside a cell by (d); on a face or an edge, the pieces on the
two or four sides fit along the arcs and around z_e), so D is a closed surface, and it is compatible by (b)-(d). ∎

*Face vertices and edge vertices of option B, refinement transitions.* There a polygon is a fan of triangles (cell
vertex, face vertex, edge vertex), each inside one cell, with the face vertex strictly inside a face and the edge
vertex strictly inside an edge. Then D meets a face in the segments (face vertex, edge vertex) and a cell in whole
triangles around its cell vertices, and (b)-(d) hold with x replaced by the face vertex and the block by the single
cell, under the same (H1)-(H4), where (H4) reads: the two fans through one face vertex are those of the two edges
its segment joins. This also covers a coarse cell next to finer ones (its faces and edges are the finer ones) and an
ambiguous face whose two segments join the same two patches (two face vertices, two disjoint arcs). What the proof
takes from the implementation here, to be confirmed against `DualContouring` in stage 1 and asserted in its tests:
(i) every polygon through a face that has face vertices is a fan through them (no polygon crosses such a face along a
straight side), (ii) at a transition every polygon is such a fan, (iii) a face vertex belongs to exactly one segment.

Wang's envelopes are not used. Coverage is one proof of (H3) for the covered triangles; the exact check of the rest
is another.

### 7.4 Theorem 3: the test for a clean mesh, by counting

Let S be a closed manifold triangle mesh without self-intersections (the level set of a clean input: its components
across which the winding number steps between 0 and 1; one exact signed ray-crossing count per component decides
which those are). "Triangles", "mesh edges" and "mesh vertices" below are those of L only: a component that is not part of the level set
(a shell nested in material, where w steps 1 to 2) is left out of every count. Let X be a face or a 3-cell of the
grid, closed. **General position (G)** for X: no mesh vertex on
the boundary of X (for a face: on the face), no mesh edge meets an edge of X's boundary (for a face: no mesh edge
lies in the face's plane and meets the face, or meets its boundary), no triangle contains a node of X. **A cell is
tested together with all its faces, and a face with all its edges**: the cell's own clause does not exclude a mesh
edge that crosses the interior of a face inside the face's plane, where a part could touch the cell's boundary from
inside; the face's clause does. Under (G), S meets a face in a compact
1-manifold whose boundary is on the face's edges, and a 3-cell in a compact 2-manifold whose boundary is on the
cell's boundary.

For a convex X write T(X), M(X), V(X) for the sets of triangles, mesh edges and mesh vertices that meet X.

**Lemma (Euler characteristic by counting).** The Euler characteristic of S in X is |T(X)| - |M(X)| + |V(X)|; the
same holds for each connected part, with the triangles, edges and vertices of that part. Two triangles of T(X) are
in one part exactly if they are linked by a chain of triangles of T(X) in which consecutive ones share a mesh edge
in M(X) or a mesh vertex in V(X).

*Proof.* S in X is the union of the convex sets t ∩ X for t in T(X). Since S has no self-intersections, two of them
meet exactly in e ∩ X for a common edge e, or in a common vertex in X, which gives the statement on parts. The Euler
characteristic is additive over a decomposition into the pieces (open triangle) ∩ X, (open edge) ∩ X and vertices in
X. A nonempty compact convex set has Euler characteristic 1, so by inclusion and exclusion the boundary of a
triangle meets X in a set of characteristic (its edges in M) - (its vertices in V), an open triangle contributes 1
minus that, an open edge 1 minus its ends in V, a vertex 1. Summing, with every mesh edge in two triangles and a
vertex of degree k in k triangles and k edges: |T| - 2|M| + |M| + sum over V of (k - k + 1) = |T| - |M| + |V|. ∎

**Theorem 3.** Under (G) for a cell, all its faces and all their edges:

- (E) holds on an edge exactly if at most one triangle meets it (a count of segment-triangle crossings);
- (F) holds on a face f exactly if every part of S in f has |T| - |M| = 1 (a connected compact 1-manifold is an arc,
  characteristic 1, or a closed curve, 0; no mesh vertex lies on f); two end points are joined exactly if their
  triangles are in one part;
- (C) holds in a cell c exactly if every part of S in c has |T| - |M| + |V| = 1 (a connected compact orientable
  surface with b boundary circles and genus g has characteristic 2 - 2g - b; it is 1 only for the disk).

*Predicates.* "A mesh vertex is in a box": comparisons. "A mesh edge pq crosses the rectangle x = a, y in [y0, y1],
z in [z0, z1]": comparisons of p.x, q.x with a and the signs of `Orient2d` on the (x, y) and (x, z) coordinates of p,
q and the rectangle's corners. "A triangle meets an axis-parallel segment": `Orient3d` of the triangle with the two
ends and of the segment with each triangle edge. "A triangle meets a rectangle" and "a triangle or a mesh edge meets
a box" reduce to these three. All inputs are the mesh's doubles and the grid's node coordinates as doubles (the same
ones `DualContouring` compares its vertices with); no intersection point is constructed, so **no predicate beyond
`ExactPredicates.Orient2d` / `Orient3d` is needed**, and no error bound.

### 7.5 Theorem 4: the cell test for a function (class S)

**Theorem 4.** Let c be a convex cell and S a surface that satisfies (E) and (F) on the edges and faces of c and
meets c in a compact 2-manifold with boundary on the cell's boundary. Suppose there is a direction d such that every
line parallel to d meets S in c in at most one point. Then no part of S in c is closed, every part is a disk with
holes, and if the boundary of c carries m circles of S, the parts are all disks exactly if there are m of them. In
particular: if m = 1, S meets c in one disk; and if for m > 1 there are surfaces in c that S does not meet and that
separate each circle from the others, S meets c in m disks. (For a function only this second form is a test: the
parts cannot be counted from enclosures, a certified sign on a separating surface can.)

*Proof.* The projection along d is continuous and injective on the compact set S in c, so it embeds each part in a
plane. A closed surface does not embed in a plane, and a compact connected surface with boundary in the plane is a
disk with holes. Each part has at least one boundary circle and there are m circles in all, so the parts are all
disks exactly if their number is m. A separating surface that S does not meet keeps every part on one side, so no
part has two of the circles. ∎

The condition on the lines holds if S is the level set of f and the derivative of f along d has one sign on c (for a
piecewise function: on every piece that can be active in c). The cone of section 4.3 shows that the count is needed:
there m = 2 and there is one part.

### 7.6 The certificate, and what it needs beyond the exact predicates

**Corollary.** Let L be the level set and D the mesh on the same grid and node signs. If D satisfies (H1)-(H4)
(Theorem 2), L satisfies (E), (F), (C) (Theorem 3 or 4), L meets exactly the sign-changing edges, and on every face
with four crossing points L joins them as the face rule does, then D is ambient isotopic to L (Theorem 1).

What this needs that `Orient2d`, `Orient3d` and comparisons do not already give:

1. **The face rule as data.** The pairing that `cell_patches` chose on each ambiguous face must be readable (today
   internal to `DualContouring`), to compare it with the level set's.
2. **General position.** A mesh vertex on a grid plane, a mesh edge through a grid edge, a triangle through a node:
   the test refuses the cell. Axis-aligned CAD faces on grid planes are the common case; whether the grid offset
   avoids enough of them is a measurement for stage 1's spike. A symbolic perturbation would remove the refusals; it
   is not needed for correctness.
3. **Which components are the level set.** An exact integer winding number at one point next to each component: a
   signed count of ray crossings by `Orient3d` (a ray in general position, or the antipodal method's exact form).
   The fast winding number is not used by the certificate at all.
4. **Three implementation facts of option B** ((i)-(iii) in section 7.3), to be asserted in tests.
5. **For class S only:** interval enclosures of the function and of all its generalized gradients over a box (a new
   interface on `ExactInterval`), and Theorem 5 (section 7.8, not reviewed yet).
6. **For class W:** section 7.9 says what follows from Theorems 1b, 3 and 5 and what is missing (rigorous enclosures
   of the winding number and its derivatives; nothing at defect edges).

What the certificate does not give: a bound on the distance between D and L (the existing deviation measures do
that); a statement about cells that are refused (the pattern may still agree there); termination of the refinement
at a degenerate spot.

Review (2026-11-13, a fresh session with the note only, on #749): Theorems 1-4 hold in the PL setting; its findings
are worked in (the region argument of step 4, the alternation argument of Theorem 2 (a), the rule that a cell is
tested with its faces, the counts over L's triangles only, the node-sign caveat of 4.1, the withdrawn shear argument
for class S). Still premises, not proved: the three implementation facts of option B (section 7.3 (i)-(iii)), on
which the statement for face vertices and refinement transitions rests; they become assertions in stage 1's tests.
### 7.7 Theorem 1b: the partial statement

**Theorem 1b.** Let K' be a set of 3-cells of K, and let S and S' satisfy (E), (F), (C) on the cells of K', their
faces and their edges, with the same pattern there, and let each of them be, in every cell of K', either PL (a
mesh; the cells of Theorem 3) or certified as in Theorem 5 (class S). Then there is a homeomorphism of the union of K' onto itself that
maps every cell of K' and every face and edge of such a cell onto itself and S onto S' there, and it is isotopic to
the identity through such homeomorphisms.

*Proof.* The construction and the isotopy of Theorem 1 use, for each cell, only the cell, its faces and its edges;
carry them out on the cells of K' and their faces and edges. Step 4 runs cell by cell, so K' may mix the two kinds
of cells: it uses Alexander's theorem in a cell where the surface is PL and Brown's theorems, as in Theorem 5, in a
class S cell; steps 2 and 3 need only arcs. (Section 7.9 uses exactly this mix.) ∎

This is the exact meaning of a run with uncertified cells: with K' the certified cells, the mesh and the level set
differ inside the union of K' by a deformation that keeps every cell in place. It says nothing inside the other
cells, and so nothing about the topology of the whole (a handle can hide in one uncertified cell). Only the pattern
on K' enters, that is the crossing counts of the level set and the sign changes of the mesh on the edges of K' and
the pairings on its faces, which the test compares directly; whether a node's sign is its true side does not enter
(so the node-sign caveat of section 4.1 does not affect this statement).

### 7.8 Theorem 5: level sets that are not piecewise linear (class S)

Reviewed 2026-11-13 by a fresh session (relayed by the coordinator): the theorem holds and the route is right; its
findings are worked in below (the neighbourhood argument of Lemma 5.2 by upper semicontinuity, maximum instead of
minimum, the arc at an edge point, the sign change across the arc, Lemma 5.1 for any convex set, Brown's theorems
inside the induction, the outer layer, the three parts of the interface requirement).

Setting. f is locally Lipschitz on the box (a C1 function, or `min` / `max` of such), and for a set X, G(X) denotes a
compact convex set that contains every generalized gradient of f (Clarke) at every point of X: for a C1 function the
gradients, for `min(f, g)` the hull of the gradients of both wherever both can be active. The interval interface
must return an enclosure of G(X); this is a requirement on it, with three parts: (i) which pieces of a `min` / `max`
can be active on a box is decided from enclosures over the box, never from a point value; (ii) an operator that is
not Lipschitz on the box (a square root or a power below 1 at 0, a division near 0) returns an unbounded enclosure
or fails; (iii) the sign tests on G are made on outward-rounded bounds. L is the zero set, and it does not meet the
outer layer of cells (the enclosure of f excludes 0 there; Theorem 1 extends the isotopy by the identity through
that layer). A cell is *certified* if

- (S0) f has no zero at its nodes (the enclosure of f at each node excludes 0);
- (S1) on each of its edges, with direction a: either f has no zero on the edge, or a . g has one sign for all g in
  G(edge);
- (S2) on each of its faces: either f has no zero on the face, or for one of the two axes a of the face, a . g has
  one sign for all g in G(face) ((S2'): the same with any direction a in the face's plane, given as doubles with the
  component along the face's normal exactly 0, the faces being axis-aligned; the proofs below use only
  that a is orthogonal to the face's normal; section 10 of [MESH_OFFSET_ENCLOSURE.md](MESH_OFFSET_ENCLOSURE.md)); and if the face has four crossing points, f has no zero on a segment in the face
  that separates them two and two;
- (S3) there is a direction d with d . g > 0 for all g in G(cell), and the boundary of the cell carries one circle
  of L, or f has no zero on surfaces in the cell that separate the circles from each other.

Every cell that is not certified must have no zero of f at all (the enclosure of f over it excludes 0), or be listed.

**Lemma 5.1 (graphs).** If u . g >= delta > 0 for all generalized gradients g of f at the points of a convex set U
(open or not; f is locally Lipschitz on the whole box, and the point of the mean value theorem lies on the open
segment, which is in U), then f is strictly increasing
along u in U at rate at least delta, and L in U is the graph of a Lipschitz function over the plane normal to u (on
the set of lines parallel to u that meet it). In particular L is there a surface, locally flat (the map
(x, t) -> (x, t - phi(x)) straightens it locally), with f < 0 on one side and f > 0 on the other.
*Proof.* Lebourg's mean value theorem: f(q) - f(p) = g . (q - p) for some generalized gradient g at a point of the
segment. So f(p + t u) - f(p) >= delta t, each line meets L at most once, and for two points p, q of L,
0 = g . (q - p) bounds the component of q - p along u by (sup |g| / delta) times the component across. ∎

**Lemma 5.2 (at the cell boundary).** Let c be a certified cell and p a point of L on its boundary. Then there are
directions u and u' pointing strictly into c at p, and a ball B around p, with u . g < 0 and u' . g > 0 for all
generalized gradients g of f at all points z of B. Consequently
c with {f <= 0}, and c with {f >= 0}, are near p the regions on one side of a Lipschitz graph: their boundaries are
locally flat at p, and L in c is near p a closed half disk whose edge is L on the boundary of c.
*Proof.* By (S0) p is not a node. (S1) and (S2) give the sign of a . g only for g in G(edge) or G(face), which
contains the generalized gradients at p; it carries over to a neighbourhood because the generalized gradient is
upper semicontinuous (Clarke 1983): for z close to p, every generalized gradient at z lies in any given
neighbourhood of the set of those at p. That set is compact with |a . g| >= delta > 0 on it, so there is a ball B
around p with a . g of the same sign and |a . g| >= delta / 2 for all generalized gradients at all points of B.
If p is in the interior of a face with outward normal n, let a be the direction of (S2) (an axis, or any
direction with a . n = 0, (S2')) and s that sign; take u = -s a - eps n and u' = s a - eps n (a . n = 0). Then u . n < 0 and u . g = -|a . g| - eps n . g < 0 on B for eps small, since
the gradients are bounded; likewise u'. If p is on an edge with direction a and the outward normals n_1, n_2 of the
two faces of c at it, (S1) gives the sign s of a . g, and u = -s a - eps (n_1 + n_2), u' = s a - eps (n_1 + n_2) do
it. A direction pointing strictly into a convex set at a boundary point makes its boundary a Lipschitz graph near
that point: with t the coordinate along u, c is {t >= psi} there. By Lemma 5.1 on B, f decreases strictly along u,
so {f <= 0} is {t >= phi} with phi Lipschitz. Their intersection is {t >= max(phi, psi)}, the region above a
Lipschitz graph; the same with u' for {f >= 0}.

On the graph of max(phi, psi), the part from L is where phi >= psi and the part from the boundary of c where
psi >= phi; they meet along L on the boundary of c. Near p that set is one arc through p. In a face: by (S2) a
graph over the line of the face orthogonal to a. At an edge point: by (S1) and the semicontinuity above, f is strictly monotone along a
in each of the two faces near p and changes sign on the edge at p, so in each face L is a graph over the other axis
of that face, starting at p: one arc into each face. And phi - psi changes sign across this arc, because f changes
sign across it on the boundary of c (along a in a face, along the edge by (S1) at an edge
point): on one side the boundary of c is in f < 0, where psi > phi, on the other in f > 0. So the arc cuts the
graph, a disk near p, into the part from L and the part from the boundary of c, and by (J) L in c is near p a closed
half disk. ∎

**Theorem 5.** Let every cell that L meets be certified. Then L is a closed surface, it satisfies (E), (F), (C),
and Theorem 1 (and 1b) hold with L in the place of a PL surface: L is ambient isotopic to every PL surface that is
compatible with the same pattern.

*Proof.* (E): by (S1) f is strictly monotone on an edge with a zero (Lemma 5.1 in one dimension), so the zero is
unique and f changes sign. (F): by (S2) and Lemma 5.1 in the plane of the face (Lebourg along a), L in the face is a graph over the line
of the face orthogonal to a, a 1-manifold without closed curves whose ends are on the boundary by Lemma 5.2, so a set of arcs. (C): by
Lemmas 5.1 and 5.2, L in the cell is a compact surface with boundary on the cell's boundary and every line parallel
to d meets it once at most, so Theorem 4 applies with (S3): its parts are disks.

Theorem 1 used PL in two places. (J) holds for arbitrary arcs in the plane (Schoenflies), so step 3 stands. In step
4, (A) is replaced as follows. Let D_1 .. D_m be the disks of L in c. Each closed region of c minus the disks has as
boundary a 2-sphere made of disks D_j and a part of the boundary of c. The induction of the PL case needs the
region that is cut to be a ball at each stage, so Brown's theorems are used inside it: cut c along D_1; the sphere
made of D_1 and either of the two disks its circle cuts from the boundary of c is locally flat at every point
(inside D_1 by Lemma 5.1, on the boundary of c away from L because c is convex, on the circle by Lemma 5.2), where
locally flat means locally bicollared, the sphere lying in R^3 inside the 3-sphere. A locally flat sphere in the
3-sphere is bicollared (M. Brown, *Locally flat imbeddings of topological manifolds*, 1962), and the closure of each
complementary domain of a bicollared sphere is a ball (M. Brown, *A proof of the generalized Schoenflies theorem*,
1960). So D_1 cuts c into two balls; each further disk lies in one of them and cuts it, by the same argument, into
two balls (the boundary of the ball being cut is locally flat by the previous step). So the
regions are balls, and the cone extension and the Alexander trick, which are topological, finish as before. The
same lemmas show that L is a surface near every point, so it is a closed surface. ∎

What this needs: the enclosure property of G (for `min` / `max` and other non-smooth operators, all pieces that can
be active on the box); the two theorems of Brown as cited (not re-read in this study: the statements are the
standard ones and the proof depends on them exactly as written); Lebourg's mean value theorem for locally Lipschitz
functions and the upper semicontinuity and calculus rules of the generalized gradient (F. H. Clarke, *Optimization
and Nonsmooth Analysis*, 1983, chapter 2; the reviewer and I recall Theorem 2.3.7 for the mean value theorem and
Propositions 2.1.2, 2.1.5, 2.3.3, 2.3.12 and Theorem 2.6.6 for the rest, from memory: check the numbers before they
are cited in docs/SOURCES.md). The interval tests (S0)-(S3) are
the only computations; a tangency with the grid, a level set through a node, or a point where the generalized
gradients surround 0 (a cusp, two surfaces touching) is never certified.

### 7.9 Class W (broken inputs): what follows, and what is missing

Reviewed 2026-11-13 with 7.7 (relayed by the coordinator); findings worked in: no non-manifold vertex in a certified
cell, the meaning of a transversal crossing, the closed cell, the mixed K' of Theorem 1b.

Notation: a *defect edge* is a mesh edge that is not shared by exactly two triangles running through it in opposite
directions; m_e is its net multiplicity (the signed number of triangles through it). w is the generalized winding
number.

**Lemma 9.1 (gradient bound).** Off the triangles, grad w(p) = (1 / 4 pi) times the sum over the defect edges
(those with m_e != 0 contribute) of
m_e times the integral over e of (x - p) x dx / |x - p|^3 (up to the sign convention), and this extends continuously
across the interiors of the triangles. Hence |grad w(p)| <= (1 / 4 pi) sum |m_e| min(2 / rho_e(p),
len_e / dist_e(p)^2), with rho_e the distance of p to the line of e and dist_e the distance to the segment e.
*Proof.* The solid angle of an oriented triangle has the gradient given by the line integral over its boundary (the
Biot-Savart form; Martens, Trettner, Bessmeltsev 2026, supplement; classical). Summing over the triangles, the
contributions of an edge shared by two triangles in opposite directions cancel. The integrand has norm
sin(theta) / |x - p|^2; integrated over a segment it gives (cos theta_1 - cos theta_2) / rho <= 2 / rho, and it is at
most 1 / dist^2 pointwise. The jump of w across a triangle is constant, so the gradient has no jump. ∎

**Lemma 9.2 (levels in a cell).** Let c be a convex cell, a a point of c off the triangles, and G a bound of
|grad w| on c. For every p in c off the triangles such that the segment a p crosses the triangles transversally
(through interiors of triangles only: not through a mesh edge or vertex, and not inside a triangle's plane),
w(p) = w(a) + N(a, p) + r with |r| <= G |p - a|, where N is the signed number of crossings.
*Proof.* Along the segment w is piecewise smooth with the derivative bounded by G and a jump of +-1 at each
crossing. ∎

**What follows.** Let c contain no defect edge, no intersection of two triangles that do not share an edge or a
vertex, and no non-manifold vertex (the triangles around every mesh vertex in c form a single cycle: no bowtie, no
two sheets or cones touching in one vertex while all their edges are regular), and
let an enclosure [lo, hi] of w(a) and the bound G satisfy k - 1/2 < lo - G diam(c) and hi + G diam(c) < k + 1/2 for
an integer k. Then by Lemma 9.2 the set w > 1/2 in c is the set k + N(a, p) >= 1, on the closed cell off the triangles, its
faces included (so a face shared with a neighbour without triangles gets the same side from both cells), and it
depends on the triangles
only: the level set in c is made of the sheets of the input across which k + N steps between 0 and 1. Theorem 3
needs a 2-manifold in the cell, so the vertex condition must also hold for the selected sheets alone (a selected and
an unselected sheet may share a vertex; then the cell is not certified). With that the selected sheets are a PL
surface in c and Theorem 3 applies to it (the counts over those sheets). In a cell that meets no triangle and no defect
edge, w - 1/2 is smooth and Theorem 5 applies with enclosures of grad w over the cell (a bound of the second
derivatives is one way to get them from a value at a point). With K' the cells certified in one of the two ways, Theorem 1b gives: **the mesh and the level set
differ inside the union of K' only by a deformation that keeps every cell in place.** That is the strongest
statement this approach can prove for a broken input.

**What is missing.**

1. A rigorous enclosure of w at a point. The formula uses atan2; `ExactInterval` has +, -, * only. Needed: an
   interval atan2 (or the antipodal method's ray count plus an enclosure of its boundary line integral), and an
   interval square root for Lemma 9.1's sum. Far from the input a crude bound suffices (|w| is at most the input's
   area over 4 pi dist^2), but propagating it inward with Lemma 9.2 loses G times the path length, which exceeds 1/2
   near the defects.
2. Enclosures of grad w over a box (for Theorem 5 in free-space cells), for example from a bound of the second
   derivatives of w: the same computation as Lemma 9.1 one order higher, not written.
3. **Nothing at the defects.** At a defect edge w is not locally Lipschitz (|grad w| grows like 1 / rho), so the
   setting of Theorem 5 does not hold there; and around it the gradient turns through all directions in the planes
   across it, so no graph direction exists in any cell that contains a piece of it, and (S3) fails at every size; the same
   holds where a harmonic sheet meets the input's triangles, as long as both are in one cell. These cells are listed
   at any resolution. So a broken input never gets "isotopic to the level set", only the statement above, and how
   much of the surface it covers is a measurement (the share of cells within a few cells of a defect edge).

Conclusion for option C: the partial statement is provable with Theorems 1b, 3, 5 and Lemmas 9.1, 9.2 once items 1
and 2 exist; item 3 is a limit of the approach, not a gap in the proof.
