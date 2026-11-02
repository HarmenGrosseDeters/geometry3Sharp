# Literature review: the cost of the guarantees in `Reducer` and the remeshers

Status: research report (tier S/M, report only, 2026-11-02). No library code was changed. Every recommendation below is
either "measure an upper bound first" or "not applicable"; nothing is to be built from this document without the
owner's decision and its own task brief.

## 0. Scope, method, and what could not be read

The five bottlenecks, with the numbers from the sources named:

| # | Bottleneck | Measured | Source in the repo |
|---|---|---|---|
| 1 | `Reducer.MaxDeviation` certification (`TriangleDeviationBounds.IsWithin`, incl. the patch bound of #280) | 90-98 % of a reduction; about 700k `IsWithin` calls, about 7 per collapse, 7-73 µs each | ROADMAP hotspot row; [ENVELOPE.md](ENVELOPE.md) §6 "Follow-up spikes" |
| 2 | `Reducer.PreventSelfIntersections` on alpha-wrap meshes | up to about 70 s; 73 % in `TriangleHashGrid.Query` | `benchmarks/MeshRepairBenchmark/README.md`; [SELF_INTERSECTION_PATCH_CERTIFICATE.md](SELF_INTERSECTION_PATCH_CERTIFICATE.md) (stopped) |
| 3 | `Remesher.PreventSelfIntersections` | about 17x the remesh time; pair-result caching closed (at most 45 ms of 13.2 s) | ROADMAP "Remeshers produce self-intersections"; `benchmarks/SelfIntersectionBenchmark/README.md` "Measured and closed" |
| 4 | Quality vs fidelity after the alpha-wrap simplification (stage 7b) | min angle of the 1 %-worst triangles about 3.8° | ROADMAP stage 7b |
| 5 | Thin walls folding through each other when remeshing far above the wall thickness | dozens to hundreds of pairs on the tubes without the check | ROADMAP "Remeshing far above the wall thickness" |

Method: web search for each named paper and for follow-ups, reading of abstracts, project pages and secondary
descriptions, and the repo's own design documents and code (`mesh/Reducer.cs`, `mesh/Remesher.cs`,
`queries/TriangleDeviationBounds.cs`, `spatial/TriangleHashGrid.cs`).

**Access limits (stated, not circumvented).** The first session could read no full text (`mgarland.org` and
`www.academia.edu` were blocked). A follow-up session (2026-11-02) read two full texts:
- Zelinka & Garland 2002, *Permission Grids* (author PDF, `mgarland.org/papers/pgrid.pdf`, 25 pages);
- Hu et al. 2017 (arXiv:1611.02147v1, the preprint of the TVCG paper; the TVCG version was not compared).

Still blocked for the follow-up session (the proxy answered 403): `www.academia.edu` (Gumhold et al. 2003),
`peterwonka.net` (Wang et al. 2019), `igl.ethz.ch` and `cims.nyu.edu` (Harmon et al. 2011), `www.cs.ubc.ca`
(El Topo), `www.cs.unc.edu` / `gamma.cs.unc.edu` (Cohen et al. 1996), and the author hosts tried for Teschner et al.
2003. No other route was tried. The statements about those papers still come from abstracts, project pages, index
records or citing papers, and keep their *(full text needed)* marks. License statements marked "LICENSE file checked"
come from the raw LICENSE file of the named repository; the others still come from project pages or package indexes.
Everything still marked *(full text needed)* must be checked before an implementation brief is written.

A second follow-up session (2026-11-02, with `peterwonka.net`, `igl.ethz.ch`, `cims.nyu.edu`, `cs.ubc.ca`,
`cs.unc.edu` and `api.crossref.org` allowed) read:
- Harmon et al. 2011, full text (author PDF, `igl.ethz.ch/projects/contact-aware-modeling/CAGM.pdf`, 10 pages);
- El Topo's `license.txt` and `readme.md` (`raw.githubusercontent.com/tysonbrochu/eltopo/master/`);
- the Crossref records of Wang et al. 2019, Gumhold et al. 2003, Brochu & Bridson 2009, Brochu et al. 2012 and
  Harmon et al. 2011 (citations below confirmed against them).

Still not readable in the second follow-up: `cs.ubc.ca` and `cs.unc.edu` answer every path with a redirect to
`www.cs.ubc.ca` / `www.cs.unc.edu`, which the proxy still blocks (El Topo paper, Cohen et al. 1996);
`peterwonka.net` answers 406 from its Mod Security filter (a bot check, not circumvented; Wang et al. 2019); no
allowed host has Gumhold et al. 2003 (`www.academia.edu` not retried).

Licensing rules applied (CLAUDE.md, AGENTS.md): implement from papers; port only MIT / BSD / Apache; never GPL
(CGAL); MPL-2.0 (TetWild, fTetWild, Sacht & Jacobson's code) not ported.

## 1. Bottleneck 1: `MaxDeviation` certification

### 1.1 What is in place and what was already tried

`collapse_within_deviation` (Reducer.cs) does: one nearest query of the new vertex, cheap point samples (centroid and
edge midpoints) as early rejects, then `IsWithin` on every new triangle (branch and bound after Sacht & Jacobson 2024,
plus the #280 patch bound). Tried and closed (ENVELOPE.md §6): exact polyhedral envelope (Wang et al. 2020; accepts
8-17 % of what `IsWithin` certifies, about 5x slower), exactness of `IsWithin` (changes no decision), triangle-level
cache (0-0.3 % repeats), local nearest queries, a hash grid of cell ε, splitting the query triangles. Open: shared tree
queries per collapse (upper bound 10-20 %), and a carried bound per triangle (owner: not built).

### 1.2 Methods from the literature

**Simplification envelopes** — J. Cohen, A. Varshney, D. Manocha, G. Turk, H. Weber, P. Agarwal, F. Brooks,
W. Wright, *Simplification Envelopes*, SIGGRAPH 1996, pp. 119-128, doi:10.1145/237170.237220.
- Two offset surfaces at ±ε, constructed so that they do not self-intersect; simplification runs inside the volume
  between them, which guarantees the one-sided deviation and keeps the genus.
- Assumptions: an orientable manifold input; the offsets are built by moving vertices along normals and shrinking the
  offset locally where it would self-intersect, so the available tolerance is smaller than ε where the surface is
  curved or thin (abstract-level; *(full text needed)* for the exact construction). Zelinka & Garland 2002 (§3.1.4,
  full text read) describe it the same way: per-vertex offsets along the normals, new faces intersected with the
  envelopes; input-sensitive (each envelope is the size of the input), manifold and orientable input only, no
  topology change; 178 s vs 19 s for the permission grid on the bunny at ε = 1 %.
- Cost vs ours: it is the predecessor of the polyhedral envelope; containment tests against the offset surfaces are of
  the same kind as the ε/√3 polyhedra that were measured and stopped, and the envelope is locally thinner than ε.
- **Not applicable**: same failure mode as the stopped exact envelope (acceptance), and it needs a manifold input,
  which the alpha-wrap pipeline has but the general `Reducer` does not.

**Permission grids** — S. Zelinka, M. Garland, *Permission Grids: Practical, Error-Bounded Simplification*, ACM TOG
21(2):207-229, April 2002 (pages per DBLP; the author PDF numbers them 1-25). Full text read.
- A uniform binary grid (1 bit per voxel, a full grid over the bounding box) discretizing the tolerance volume. A
  voxel is "occupiable" only if it lies entirely within ε of the input. Every triangle the simplifier generates is
  rasterized into the grid; if it touches an empty voxel, the operation is rejected. Works with any simplifier,
  tolerates any degeneracy (non-manifold, T-junctions, self-intersections, non-orientable: input triangles are
  voxelized independently), allows topology change, and the tolerance can be grown during simplification by dilating
  the occupiable set by one voxel (6-neighbourhood; conservative, L1 instead of L2) for an error-bounded LOD
  hierarchy. Boundary and feature curves get a second grid built from the boundary edges.
- Marking rule (§4.2): the α-prism (Minkowski sum of the triangle and a ball of radius α) of **each input triangle**
  is voxelized eagerly before simplification (Dachille & Kaufman's scan-line voxelization), with the distance taken at
  the voxel centre and α = ε − h·√3/2, so that the whole voxel is within ε. Corner tests are named as an alternative.
- Resolution (§4.1): h ≤ ε/√3 is needed for a voxel through which the surface passes to be occupiable at all;
  the paper's precision parameter is ρ = ε/h, with ρ = 4 as the default ("a good trade-off"). Memory limits the
  smallest ε: about 0.155 % of the diagonal for 256 MB in the worst case; the practical limit in their tests was about
  0.15 %. Octrees / sparse grids are named as future work, not implemented (about a tenth of the cells are occupiable at
  1 %).
- Triangle test (§4.3): 2D scan-line rasterization in the dominant projection plane, each pixel lifted to a short
  column of voxels; "small care" adds the neighbouring voxels for floating-point imprecision (up to 3 per column).
  The paper gives no formal rounding argument for this step.
- Integration (§4.4): with QSlim, the collapse is performed, all triangles of its neighbourhood are tested, and the
  collapse is rolled back on failure; rejected edges are re-queued when a neighbouring collapse changes their cost.
- Guarantee: one-sided (simplified → input) within ε. A two-sided variant (§4.2.1, proof in the appendix) needs
  ε/√3 during voxelization, clearing voxels that touch the medial axis, and a simplifier that keeps topology; not
  implemented by the authors.
- Cost: on a 933 MHz PIII with ρ = 4, ε = 1 %: 19.2 s vs 4.7 s for plain QSlim (bunny, 69k faces), 219 s vs 97 s
  (Buddha, 1.1M faces), about 2-4x the QSlim time and up to 10x faster than Simplification Envelopes (178 s on the
  bunny). Time grows as ε shrinks (horse, 97k faces: 19 s at 2 %, 89 s at 0.175 %).
- The idea in our terms: the paper's rule is the same inequality, d(c) + h·√3/2 ≤ ε for a voxel of size h with centre
  c. **Our variant differs** in where d(c) comes from: one nearest-distance query per cell, filled lazily when a
  triangle first touches it, instead of the eager α-prism voxelization of every input triangle. A triangle whose
  overlapped cells are all certified is within ε. The acceptance loss is the shell of width about h·√3 at the boundary of the tolerance volume, which
  **shrinks with h**, unlike the fixed ε/√3 of the polyhedral envelope; on the other hand the `Reducer` pushes
  triangles to the tolerance, so the triangles near ε will still fall through to `IsWithin`.
- Why it could pay where the triangle cache did not: triangles repeat in 0-0.3 % of the calls, but **cells repeat**:
  every later collapse over the same area overlaps the same cells, so a lazily filled hash map of cell decisions turns
  most per-collapse work into lookups. The cost moves to filling the cells: about (surface area)/h² near-surface cells
  each needing one nearest query. Rough count for a surface of area about diag²: h = ε/4 gives about 16 · (diag/ε)²
  cells, so 1.6e7 at ε = 0.001·diag (worse than the 700k `IsWithin` calls of about 10 queries each), 1.8e6 at
  0.003·diag and 1.6e5 at 0.01·diag. So it can only pay at the coarser tolerances, or with an adaptive (octree)
  version: large cells deep inside the tolerance volume, small ones only near its boundary.
- Fit with the certified approach: good. The decision is a sufficient certificate (a filter in front of `IsWithin`,
  never a reject), and its only floating-point step is d(c) with a rounding margin, the same kind of bound
  `TriangleDeviationBounds` already uses; the triangle-box overlap must be conservative (closed boxes, or the exact
  SAT of the triangle-box test with an outward margin). It changes results only where `IsWithin`'s depth limit
  rejects a triangle the grid accepts.
- License: paper only (no reference code known; the authors' QSlim integration was not published with the paper).
- The full text confirms the cell-count estimate: the paper's own practical floor (about 0.15 % of the diagonal for a
  uniform grid at ρ = 4) lies above our 0.001·diag setting, and the octree variant is untested in the paper.
- **Recommendation: measure an upper bound first.** In the `ReducerBenchmark spike` setting (bumpy sphere, terrain;
  ε = 0.001 / 0.003 / 0.01 of the diagonal): for h ∈ {ε/2, ε/4, ε/8} and an octree variant, count the `IsWithin`
  calls the grid would certify, the number of distinct cells filled, and the time of filling + lookups vs the time of
  the certified calls. Build only if it removes a clear majority of the check time at 0.003 (the 7b / wrap tolerance
  range), since at 0.001 the cell count already rules out the uniform grid.

**Error and tolerance volumes** — A. Guéziec, *Locally Toleranced Surface Simplification*, IEEE TVCG 5(2):168-189,
1999, doi:10.1109/2945.773810 (earlier: *Surface Simplification Inside a Tolerance Volume*, IBM report 1996).
- An error value per vertex, linearly blended to an error volume (union of balls of linearly varying radius), carried
  forward through each operation; a tolerance volume forbids errors above a local tolerance. Cost local to a vertex
  star, linear or O(n log n) overall.
- Guarantee: one-sided, conservative; the carried bound grows with every operation.
- This is the "carried bound per triangle" of ENVELOPE.md §6, which the spike simulated (1.6-9.6 % certified at 0.001,
  77-85 % at 0.01) and the owner decided not to build.
- **Not applicable** (owner decision stands; nothing new in the method).

**Hausdorff-controlled reduction** — R. Klein, G. Liebich, W. Straßer, *Mesh Reduction with Error Control*, IEEE
Visualization 1996, pp. 311-318, doi:10.1109/VISUAL.1996.568124; H. Borouchaki, P. J. Frey, *Simplification of
surface mesh using Hausdorff envelope*, Computer Methods in Applied Mechanics and Engineering 194 (2005)
*(volume and pages from the index record only; full text needed)*.
- Klein et al. use the Hausdorff distance between the original and the reduced mesh as the error, tracking
  correspondences so that it is evaluated locally; Borouchaki & Frey estimate it per edge collapse.
- Guarantee: Klein et al. claim a guaranteed user-defined error (the mechanism, exact or sampled, is
  *(full text needed)*; Zelinka & Garland 2002 §3.1.3 class it under local reprojection and say it enforces the
  two-sided Hausdorff distance "with significant computational effort"); Borouchaki & Frey use an estimate, not a
  bound.
- Cost vs ours: `IsWithin` with Sacht & Jacobson's cascading bounds is the newer, certified form of the same check.
- **Not applicable**.

**BVH-pair culling for Hausdorff distance** — M. Tang, M. Lee, Y. J. Kim, *Interactive Hausdorff Distance
Computation for General Polygonal Models*, ACM TOG 28(3), article 74 (SIGGRAPH 2009), doi:10.1145/1531326.1531380;
L. Sacht, A. Jacobson, *Cascading upper bounds for triangle soup Pompeiu-Hausdorff distance*, SGP 2024 (already
implemented, see SOURCES.md).
- Bounds on the distance discard polygon pairs of both models in turn. For one reduced triangle against the input
  BVH, this is what `IsWithin`'s branch and bound already does. The spike's conclusion ("`IsWithin` is close to the
  state of the art") is consistent with this literature.
- **Not applicable** beyond what exists.

**Fewer checks per wall-clock second: parallel and speculative certification.**
- Parallel decimation in the literature works on independent sets of collapses (no two collapses share a
  neighbourhood): P. Franc, V. Skala, *Parallel Triangular Mesh Reduction*, ALGORITMY 2000 (greedy independent set by
  importance); GPU variants with QEM and independent regions surveyed in S. Oh et al., *PaMO*, CGF 44(7) 2025
  (arXiv 2509.05595). Independent sets cover only a small share of the mesh per round (8-9 % of the edges in one
  secondary report), and they change the collapse order, so **the results change**.
- Today the parallelism is inside one collapse (`gParallel.ForRange` over its about 7 triangles), which is
  fine-grained. A result-preserving alternative, not in the papers found: speculative certification of the next k
  queue entries whose 2-ring neighbourhoods are disjoint from each other and from the collapse being applied, in
  parallel; each answer is used only if, when the entry reaches the top of the queue in serial order, its
  neighbourhood (vertex positions and triangles) is unchanged and its collapse point is the same. Then every decision
  is the one the serial code makes, and the results stay bit-identical (the determinism rule of CLAUDE.md holds by
  construction).
- **Recommendation: measure an upper bound first** (the owner's file, `Reducer.cs`): in a subclass or an
  instrumented copy, for k ∈ {8, 32}, the share of popped collapses whose certification would have been available
  from speculation (neighbourhood unchanged), and the wasted certifications. The bound on the wall-clock gain is about
  min(cores, 1/(1-share)) on the 92-97 % share. This is the lever the spike pointed to ("cut the number of checks per
  collapse, or their latency"), and it composes with any cheaper check.

### 1.3 Ranking for bottleneck 1

1. Speculative parallel certification (bit-identical, potentially several times on multi-core machines): upper bound
   first.
2. Adaptive permission grid as an accept filter: upper bound first; likely only at 0.003·diag and coarser.
3. Shared tree queries per collapse (already being tried, 10-20 %).
4. Envelopes, error volumes, Hausdorff-estimate methods: not applicable.

## 2. Bottleneck 2: `Reducer.PreventSelfIntersections` (the candidate search)

### 2.1 What the code does

`collapse_creates_intersection` builds the about 10 new triangles of the collapse, tests them among themselves, and
for **each** new triangle queries `TriangleHashGrid` with its box (a hierarchical grid: level 0 at 1.5 × the mean
edge length, coarser levels for triangles that grew; every query visits every level). All new triangles share the
kept vertex, so their boxes overlap heavily, and the same cells and candidates are visited up to about 10 times per
collapse.

### 2.2 Methods from the literature

**Intersection free simplification** — S. Gumhold, P. Borodin, R. Klein, *Intersection Free Simplification*,
International Journal of Shape Modeling 9(2):155-176, December 2003, doi:10.1142/S0218654303000097 (Crossref
confirmed).
- Pair contraction with a priority queue and a spatial search structure; the collision test of the simplices affected
  by a contraction is done lazily for the contraction about to be applied. This is our design. Which search structure
  and which per-query costs they report is *(full text needed; no allowed host has it, `www.academia.edu` blocked in the
  first follow-up session)*.
- **Already adopted** (the `Reducer`'s check follows the same scheme).

**Spatial hashing** — M. Teschner, B. Heidelberger, M. Müller, D. Pomeranets, M. Gross, *Optimized Spatial Hashing
for Collision Detection of Deformable Objects*, VMV 2003.
- A uniform hashed grid; the cell size is the parameter that matters most, and it should be about the size of the
  primitives (secondary sources; *(full text needed)* for the exact rule). Too large: long cell lists; too small: each
  primitive spans many cells.
- For the `Reducer`, whose triangles grow by orders of magnitude, a single cell size cannot fit; the hierarchical
  grid (`TriangleHashGrid`'s levels) is the standard fix, but its query visits every level.

**Dynamic bounding volume trees** — T. Larsson, T. Akenine-Möller, *A dynamic bounding volume hierarchy for
generalized collision detection*, VRIPHYS 2005 (refit and node invalidation; Larsson's 2009 dissertation *Adaptive
Bounding Volume Hierarchies for Efficient Collision Queries* covers refitting for deforming meshes); G. van den
Bergen, *Efficient collision detection of complex deformable models using AABB trees*, Journal of Graphics Tools
2(4), 1997 (refit); the incremental fat-AABB tree of Box2D (`b2DynamicTree`, E. Catto; MIT since 2.4, zlib-style
before: both permissive): leaves are enlarged by a margin so small moves need no update; a leaf that leaves its fat
box is removed and reinserted, the sibling chosen by a surface-area (perimeter) cost.
- A dynamic tree adapts to triangles of any size without levels; a query costs O(log n + output). Refit-only BVHs
  do not fit edge collapse (triangles are removed and created), the insert/remove tree does.
- Fit: the broad phase decides nothing exact (closed boxes, conservative), so any structure is allowed; results stay
  bit-identical as long as the set of candidates tested is a superset of the intersecting ones and the order of the
  pair tests does not change the outcome (it does not: the check is "any pair intersects").
- License: Box2D: the LICENSE file on the `main` branch of `erincatto/box2d` is MIT (Copyright (c) 2022 Erin Catto;
  LICENSE file checked). That older versions were zlib-style comes from secondary sources. Implementing from the
  description is enough.

**Clearance (proximity) certificates.** For a region R (the old one-rings of a and b) let C be a lower bound of the
distance from R to every triangle that shares no vertex with R. If every point of the new triangles lies within
distance < C of R, no new triangle can touch such a triangle (a point p of T has d(p, R) ≥ d(T, R) ≥ C). For an edge
collapse the new triangles' points lie within max(|newPos − a|, |newPos − b|) of their old counterparts. This is the
reasoning of conservative advancement (B. Mirtich, *Impulse-based dynamic simulation of rigid body systems*, PhD
thesis, UC Berkeley 1996) and of proximity-based collision handling (R. Bridson, R. Fedkiw, J. Anderson, *Robust
treatment of collisions, contact and friction for cloth animation*, SIGGRAPH 2002), applied to a mesh edit.
- In the `Reducer`, collapses move vertices by up to an edge length, and C is computed by a nearest query, which is
  the same kind of query as the grid search; it pays only if C is reused across several evaluations of the same
  vertex (popped, rejected, re-queued) and stays valid (it decreases only when geometry near R changes). Its value on
  alpha-wrap meshes, whose clearance is of the order of the wrap offset, is unknown.

**Patch certificates** (Volino & Magnenat-Thalmann 1994; Schvartzman, Pérez, Otaduy 2010): tried and stopped
([SELF_INTERSECTION_PATCH_CERTIFICATE.md](SELF_INTERSECTION_PATCH_CERTIFICATE.md)); they remove pair tests, not the
grid query.

### 2.3 Recommendations for bottleneck 2

1. **Measure first (fits the ROADMAP's planned measurement)**: per collapse, cells visited per level, triangles per
   cell, the number of *distinct* candidates vs candidates returned summed over the about 10 per-triangle queries, and
   whether the 73 % "in `Query`" is exclusive of the pair tests run in its callback (the callback calls
   `TriangleTriangleFilter`). The ratio summed / distinct is the upper bound of the next item.
2. **One query per collapse** with the union box of the new triangles (or the box of R grown by the displacement),
   then the per-triangle box test against each candidate before the pair test: bit-identical decisions; it removes
   the repeated cell visits. Cheap to build once item 1 shows a ratio clearly above 1.
3. **Dynamic fat-AABB tree** in place of the hierarchical grid, as an A/B in `SelfIntersectionBenchmark` and the
   wrap cases: only if item 1 shows that the coarse levels (visited by every query) or long cell lists dominate.
4. Clearance reuse: upper bound only (share of collapses with displacement < C, and how often C stays valid); likely
   weak in the `Reducer`, stronger in the remesher (section 3).

## 3. Bottleneck 3: `Remesher.PreventSelfIntersections`

### 3.1 Methods from the literature

**El Topo** — T. Brochu, R. Bridson, *Robust Topological Operations for Dynamic Explicit Surfaces*, SIAM J. Sci.
Comput. 31(4):2472-2493, 2009, doi:10.1137/080737617 (Crossref confirmed). Every mesh operation (split, collapse, flip,
smoothing) is an atomic edit cancelled if
it would introduce an intersection; vertex motion is checked by continuous collision detection (CCD), new triangles
statically; the broad phase is an acceleration grid of element boxes. The intersection-free invariant is what later
multimaterial front tracking relies on (F. Da, C. Batty, E. Grinspun, *Multimaterial Mesh-Based Surface Tracking*,
SIGGRAPH 2014 / arXiv 1306.3113 for the front-tracking variant). Exact CCD for El Topo: T. Brochu, E. Edwards,
R. Bridson, *Efficient Geometrically Exact Continuous Collision Detection*, ACM TOG 31(4) (SIGGRAPH 2012),
doi:10.1145/2185520.2185592.
- License: **BSD 2-Clause** ("Copyright (c) 2009-2018, Tyson Brochu and Robert Bridson", `license.txt` on `master`
  of `github.com/tysonbrochu/eltopo`; LICENSE file checked; the earlier 404s were for `LICENSE`/`README`, the files
  are named `license.txt` and `readme.md`). This replaces the "public domain" of the UBC project page, which could not
  be re-read. Porting would be allowed under the project rules (BSD), with the copyright notice kept; the paper is
  enough for what section 3 needs.
- Cost: they report that the checks are the main cost of robust operations; the paper's own numbers are
  *(full text needed; `www.cs.ubc.ca` still blocked)*.

**Interference-aware modelling and minimum separation** — D. Harmon, D. Panozzo, O. Sorkine, D. Zorin,
*Interference-Aware Geometric Modeling*, ACM TOG 30(6), article 137 (SIGGRAPH Asia 2011),
doi:10.1145/2070781.2024171 (Crossref confirmed; Crossref gives pages 1-10, not the article number). Full text read:
- Collision *response* for interactive modelling: an edit is a linear trajectory per vertex; CCD (Provot's cubics,
  broad phase Self-CCD or the Teschner et al. 2003 hash grid, "complete interchangeability") finds the interference;
  one space-time interference volume constraint is driven to zero by a projection step in the modelling subspace.
  Needs an intersection-free start and fails on zero-area triangles and zero-length edges (their Limitations).
- Minimum separation (§5.2): the CCD time is replaced by the time at which a vertex comes within a fixed, global
  distance h of the triangle's *plane* (roots of the degree-6 polynomial ((x3-x0)·n)² - h²‖n‖²,
  n = (x1-x0)×(x2-x0)), then a projection test that the point lies inside the triangle; edge-edge is built analogously from Provot's
  cubic. Cost: "the increased proximity increases the amount of primitives that make it to low-level intersection
  tests" (no number given).
- Numerical margin: they return max(0, t_i - 1/100) to absorb root-finding error; a heuristic, not a bound.
- Cost: detection is 51 % of the runtime; 1.1-11.8 ms per iteration and 76-917 ms per edit on 5.8K-38K vertices,
  single-threaded (Table 1).
- No reuse of distances or clearance between edits; the clearance filter of this section is not in the paper.

Tight Inclusion CCD (B. Wang, Z. Ferguson,
T. Schneider, X. Jiang, M. Attene, D. Panozzo, *A Large Scale Benchmark and an Inclusion-Based Algorithm for
Continuous Collision Detection*, ACM TOG 40(5), 2021): conservative CCD with minimum separation. License: MIT
(`Continuous-Collision-Detection/Tight-Inclusion`, `main` branch, Copyright (c) 2020 the six authors; LICENSE file
checked).

**Clearance filter** (the lemma of section 2.2). It fits the remesher much better than the `Reducer`:
- smoothing / tangential relocation moves are small against the local spacing, and a split's new vertex lies on the
  old edge (|Δ| = 0 before projection, then the projection distance);
- one clearance per vertex per pass (a nearest query excluding the one-ring, against the grid or a BVH snapshot of
  the pass start), lowered conservatively by the largest displacement of moved vertices since (or recomputed for the
  few vertices near moved ones);
- what remains to test exactly: the new triangles among themselves and against triangles sharing a vertex with R,
  which are few and local.
- Rounding: C must be a certified lower bound (the floating distance minus its error bound, or a relative safety
  margin with a stated bound), and it is used only to *skip* tests, never to report an intersection, so the
  guarantee "the result has no new intersection" is unchanged and the decisions stay identical (a skipped test would
  have answered "no intersection").

**BVH refit vs uniform grid for local queries.** For near-uniform edge lengths (the remesher's target), the uniform
hash grid with a cell of about the primitive size is the standard (Teschner et al. 2003); BVH refit
(van den Bergen 1997, Larsson & Akenine-Möller 2005) is designed for deforming meshes with fixed connectivity, which
remeshing does not have. No change expected from swapping the structure.

### 3.2 Recommendations for bottleneck 3

1. **Clearance filter: measure the upper bound first** (the remesher session is profiling): share of
   smoothing / projection / split checks with |Δ| < C, and the share of the check time they take. Build only if that
   share of time is large; the decisions are bit-identical by construction.
2. One query per operation with the union box (as 2.3 item 2): the remesher's per-query candidate count is smaller
   (about 36), so a smaller gain; same measurement.
3. CCD (El Topo, Tight Inclusion): **not applicable** now; see section 5.
4. Pair-result caching: closed (measured, SelfIntersectionBenchmark README).

## 4. Bottleneck 4: quality vs fidelity after the alpha-wrap simplification (7b)

### 4.1 Methods from the literature

**Error-bounded remeshing with min-angle improvement** — K. Hu, D.-M. Yan, D. Bommes, P. Alliez, B. Benes,
*Error-Bounded and Feature Preserving Surface Remeshing with Minimal Angle Improvement*, IEEE TVCG
23(12):2560-2573, 2017, doi:10.1109/TVCG.2016.2632720, arXiv:1611.02147.
Full text read (arXiv:1611.02147v1).
- Input: a 2-manifold mesh, an error bound δ (two-sided Hausdorff), a minimal angle θ, and a vertex budget N. δ is a
  hard constraint; θ and N are goals (the requested mesh may not exist).
- Operators: **edge collapse, edge split and vertex relocation only. There are no edge flips**: a flip is emulated by
  a split followed by a collapse, on purpose, because flips "tend to destroy sharp creases and would require
  additional nontrivial checks". Every collapse and split is followed by a relocation of the new vertex.
- Three phases (Alg. 1):
  1. Initial simplification (Alg. 2): collapses only, a priority queue over all halfedges by increasing
     l(h)·(θ1(h) + θ2(h))/2 (length times the mean of the two opposite angles). Reduces the vertex count by about 20 %
     more, at 2-3x the time; can be switched off.
  2. Greedy angle improvement (Alg. 3): a priority queue of all angles below θ; pop the smallest θmin and try, first
     success wins: (i) collapse the halfedge opposite θmin; (ii) relocate each of the triangle's three vertices; (iii)
     only if both fail, follow the longest-side propagation path (Rivara: move to the longest edge of the two adjacent
     triangles until it no longer grows or hits the boundary) and split that edge. The split needs only validity, not
     an angle gain. The loop runs while the queue is non-empty **and** the vertex count is below N.
  3. Final relocation (Alg. 4): a FIFO queue of all vertices; relocate if the angle gain is at least Δθ = 0.1°, then
     push the one-ring.
- Acceptance test of every operator: the link condition (collapse); no fold-over (no triangle normal reverses its
  orientation); the approximation error stays below δ; no new interior angle below the current θmin; the mesh stays
  2-manifold. **There is no self-intersection test.**
- Error check (§4): **an estimate, not a bound.** Stratified samples (about 10 per facet, Lloyd-relaxed, plus edge and
  vertex samples). Direction output → input: the modified one-ring is re-sampled and the exact point-to-surface
  distance of each sample is taken (an underestimate between samples; the gap is bounded by the sample spacing, which
  is not added). Direction input → output: each input sample keeps a link to its closest output triangle, and only
  links into the one-ring plus one more ring are updated (can overestimate; the paper reports > 99.9 % of the global
  nearest points found with a one-ring patch). Our `IsWithin` gives a certified one-sided (output → input) bound and
  replaces the first direction directly; the paper's second direction has no counterpart in the `Reducer`.
- Features (§5), without tagging: a feature intensity F(v) = (τ(|K(v)|) + 1)(τ(E(v)) + 1) − 1, with K the angle
  defect, E the largest unsigned dihedral angle at an incident edge, and τ(x) = min(π, 2x). Collapse position: the
  endpoint with the larger F, or the midpoint if |F(vi) − F(vj)| < 0.15·max. Split position: the midpoint. Relocation
  start: v is classified by the number of neighbours with F(vi) ≥ ζ·F(v) (ζ = 0.5), counted only over edges with
  |D(e)| + 1 ≥ ζ(E(v) + 1): none → feature vertex (stays), about two → crease vertex (CVT barycentre of its two crease
  neighbours), all → smooth (CVT barycentre of the one-ring). Then two iterations of a weighted Hausdorff minimization
  (Lawson / Winkler et al. weights, times the Voronoi area and F of each sample), step λ = 0.9.
- Termination (§6.9): **not guaranteed.** Above 35° there is no convergence guarantee; at θ = 60° the algorithm "runs
  into infinite loops or generates degenerated edges" (best reached about 40-41°). θ = 40° is reached on their models
  but much more slowly (many splits).
- Fit: the 7b setting (fidelity first, then quality, with the vertex count as a budget). Lessons for 7b: (a) the pass
  needs splits (and accepts a moderate count increase); collapse and relocation alone cannot lift the worst angles
  under a tight bound; (b) work is ordered by the worst angle (a priority queue), not by sweeps; (c) the operator order
  is collapse → relocate → split, and flips can be left out (or kept only where no crease is involved); (d) an explicit
  stop is needed: the vertex budget, plus an attempt counter per popped angle or an operation budget, since the
  greedy loop can cycle; (e) θ around 30-35° is the realistic target.
- License: paper only. The authors' implementation is built on CGAL (GPL): no reference code is to be read.
  **Recommendation: adopt the structure** for the 7b quality pass, with every operation guarded by `IsWithin` at the
  wrap tolerance and by the self-intersection check (already the 7b plan; the details above were sent to the 7b
  session).

**Remeshing without large and small angles** — Y. Wang, D.-M. Yan, X. Liu, C. Tang, J. Guo, X. Zhang, P. Wonka,
*Isotropic Surface Remeshing without Large and Small Angles*, IEEE TVCG 25(7):2430-2442, 2019,
doi:10.1109/TVCG.2018.2837115 (Crossref lists all seven authors, X. Zhang = Xiaopeng Zhang; the IEEE record omits
him; full text not read: `peterwonka.net` answers 406 from its Mod Security filter; *(full text needed)*).
Vertex insertion removes obtuse angles, vertex removal improves small angles; runs as a post-process after any remesher at the speed of real-time adaptive
remeshing. **Recommendation: candidate operators** for the same pass (insertion / removal with the same guards); no
fidelity bound of its own.

**Curvature-adaptive isotropic remeshing** — M. Dunyach, D. Vanderhaeghe, L. Barthe, M. Botsch, *Adaptive Remeshing
for Real-Time Mesh Deformation*, Eurographics 2013 Short Papers, pp. 29-32, doi:10.2312/conf/EG2013/short/029-032.
A per-edge target length (from curvature) in the Botsch-Kobbelt loop. For 7b: a curvature sizing keeps the
deviation small where the wrap is curved, so fewer quality operations are refused by the deviation guard.
**Recommendation: measure first** (only if the 7b diagnosis shows that the refused operations sit in curved
regions; the library's remesher already has `TargetEdgeLengthF`).

**Guarded low-poly pipelines** — Z. Chen, Z. Pan, K. Wu, E. Vouga, X. Gao, *Robust Low-Poly Meshing for General 3D
Models*, ACM TOG 42(4), 2023 (SIGGRAPH), doi:10.1145/3592396 (note: not Panozzo, as the task brief had it): a
feature-preserving offset surface that is watertight, manifold and intersection-free, then progressive
simplification and a flow towards the target, keeping the guarantees. S. Oh et al., *PaMO: Parallel Mesh
Optimization for Intersection-Free Low-Poly Modeling on the GPU*, CGF 44(7) (Pacific Graphics 2025),
arXiv:2509.05595: parallel remeshing, intersection-free simplification, and a "safe projection" that minimizes the
distance to the input and a quality loss under an intersection-free constraint (GPU Newton solver). The same
architecture as our alpha wrap + `Reducer` + 7b; PaMO's safe projection is an optimization-based variant of the
relocation step. Code licenses not checked (Tencent holds related patents on the low-poly line of work:
US 12,094,060 and US 12,333,653, scope not checked). **Not applicable** as a port; confirms the design.

## 5. Bottleneck 5: thin walls folding through each other

### 5.1 Methods from the literature

- **Sizing below the wall thickness** (exists: `RemeshSizing.FromWallThickness`, `MeshWallThickness`): the
  local-feature-size sizing of Botsch et al., *Polygon Mesh Processing*, ch. 6; Dunyach et al. 2013 for the
  curvature part.
- **Minimum separation** (Harmon et al. 2011, a fixed global h inside CCD; Tight Inclusion's minimum separation,
  Wang et al. 2021): instead of
  forbidding only intersections, forbid that an operation brings non-adjacent geometry closer than δ. A δ tied to
  the local thickness and the comparison with C − δ are our proposal, not from these papers. With the
  clearance machinery of section 3 this is nearly free (it is the same comparison with C − δ), and it stops walls
  from approaching each other before they fold. A δ above the local wall thickness would block every operation near
  the wall, so δ is a fraction of the local thickness: a safeguard against near-contact, not a replacement for the
  sizing.
- **CCD per operation** (El Topo; Brochu et al. 2012 exact CCD; Tight Inclusion). A static end-state check can miss
  a move that passes through a wall only if the end state has no intersection, i.e. the moving star swallows or
  releases a whole component or sheet piece (a change of nesting); otherwise the star, still attached to its link on
  the original side, intersects the wall in the end state and the static check sees it. The pass-level
  `RejectInvertingPasses` already catches inside-out components.
- **Contact-aware flows** — L. Sacht, E. Vouga, A. Jacobson, *Nested Cages*, ACM TOG 34(6) (SIGGRAPH Asia 2015):
  decimation, flow and contact-aware optimization keep coarse layers nested without intersections; relevant as a
  model for "push walls apart before coarsening", not as a remeshing step.

### 5.2 Recommendations for bottleneck 5

1. Keep the thickness sizing as the main remedy (adopted).
2. **Measure first** whether any end state accepted by the static check has a changed nesting (signed volume /
   winding number per component before and after a pass, on the tubes of `RemeshQualityBenchmark`). CCD only if that
   finds cases: **not applicable** until then (ROADMAP item already says this; the literature agrees that CCD is the
   tool when motion matters, and our output is a static mesh).
3. Minimum separation as an opt-in on top of the clearance filter: **after** the clearance filter exists, measure
   whether it reduces the folds that the check now blocks (and so the operations refused near walls).

## 6. Summary

| # | Method | Guarantee kept | Expected gain | Recommendation |
|---|---|---|---|---|
| 1 | Speculative parallel certification of independent queue entries | yes, bit-identical | up to ~cores × on 92-97 % of the time, minus wasted speculation | measure upper bound first |
| 1 | Adaptive permission grid (Zelinka & Garland 2002) as accept filter | yes (sufficient certificate) | large at ε ≥ 0.003·diag if cells repeat; none at 0.001 | measure upper bound first |
| 1 | Envelopes (Cohen 1996), error volumes (Guéziec 1999), Hausdorff estimates (Klein 1996, Borouchaki & Frey 2005) | — | — | not applicable |
| 2 | One grid query per collapse (union box) | yes, bit-identical | ≤ summed/distinct candidate ratio of the query part | measure (the planned 7b-session measurement), then adopt |
| 2 | Dynamic fat-AABB tree (Box2D-style; Larsson & Akenine-Möller) | yes, bit-identical | removes per-level visits | A/B only if the measurement shows level visits dominate |
| 3 | Clearance filter (conservative advancement / proximity) | yes, bit-identical | the share of moves with ∣Δ∣ < C | measure upper bound first (remesher session) |
| 3, 5 | CCD (El Topo, Brochu 2012, Tight Inclusion) | stronger (motion) | cost, no gain for a static result | not applicable until a nesting change is shown |
| 4 | Hu et al. 2017 structure (worst-angle queue, collapse → relocate → split, no flips, hard error bound, explicit stop) | with `IsWithin` | lifts min angles where splits are allowed | adopt in 7b |
| 4 | Wang et al. 2019 insertion / removal operators | with `IsWithin` | obtuse and small angles | candidate operators in 7b |
| 5 | Minimum separation (Harmon 2011) | stronger than no-intersection | fewer near-contacts | after the clearance filter, measure |

The recommended sources are listed in [SOURCES.md](../SOURCES.md) under "Research review: reducer and remesher
guarantees (not implemented)".
