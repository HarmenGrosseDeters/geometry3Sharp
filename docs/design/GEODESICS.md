# Geodesic distances with accuracy bounds (study)

Status: study, docs only (2026-11-13). No library code. Owner decisions recorded in section 9; the dominance-trimming
argument of option A is in [Appendix A](#appendix-a-the-enclosure-argument-for-review), revised after its review; the
citation check is in section 3.5; the measurement that led to dropping option C is in section 4.2; the stop-rule
measurement of option A is in section 6; gates 2 and 3, the role of the oracle and the claims still to be reviewed
(A7, the withdrawn A8, A9) are in section 6a and Appendix A.5 (2026-11-13, second round); A7 with duplicate control (A7') and
the bound of A9 are in section 6d and Appendix A.6 (2026-11-19).

## 1. Question

What should the library offer for geodesic distance on triangle meshes, and with which guarantee:

- an **exact** polyhedral distance (Mitchell-Mount-Papadimitriou, Chen-Han, their successors such as VTP),
- an **approximation with a proven bound** (Steiner-point graphs, fast marching, the approximate MMP),
- or the **heat method** (what we have),

and how each fits the repo's rules: determinism (parallel equals serial), 2^k invariance bit for bit over
`Similarity.All()`, exact arithmetic where it gives a proof, and tests against analytic surfaces.

"Exact" below means the **polyhedral** geodesic distance: the length of the shortest path on the piecewise flat surface
of the mesh. On a curved smooth surface (sphere) it differs from the smooth distance by the discretisation error; that is
not a defect of the algorithm and the tests must keep the two apart (section 7.1).

## 2. What the library has (search first)

Searched for `Geodesic`, `Dijkstra`, `FastMarch`, `Eikonal`, `Heat`, `MeshGeodesic*`, `Intrinsic*`, the sparse
solvers and the terrain code. The geodesic family is substantial (Phase 3, [roadmap/parameterization-geodesics.md](../roadmap/parameterization-geodesics.md)):

| Building block | File | What it gives | Guarantee today |
|---|---|---|---|
| Heat method distance | `mesh_ops/MeshGeodesicDistance.cs` (729 lines) | distance per vertex from vertex sources; `TracePath` (steepest descent); factorizations reused across sources; heat in the log domain where it underflows (`SparseCholesky.SolveLog`), so far fields work | **measured only**: documented "about 2% of the distance plus half the mean edge length", 2% plus one edge length at open boundaries; near the sources smoothed over a few edge lengths |
| Intrinsic Delaunay triangulation, mollification, refinement | `mesh_ops/IntrinsicTriangulation.cs`, `IntrinsicTriangulationRefinement.cs` | edge flips with signposts, tracing, vertex insertion (Chew / Ruppert, min angle), boundary edges opposite obtuse angles split by default; non-negative cotan weights | the triangulation is exact in the intrinsic sense (same metric); the Laplacian's quality is measured |
| Geodesic paths, loops, waypoints (FlipOut) | `mesh_ops/MeshGeodesicPath.cs` | a **locally shortest** path between two vertices (straight in the intrinsic metric, turns at least pi at vertices) and its `Length` | its length is the length of a real path on the surface, so **an upper bound** of the distance (up to rounding); it is the global shortest only if started in the right homotopy class (the default start is a Dijkstra edge path, which is not guaranteed) |
| Geodesic Bézier curves | `mesh_ops/MeshGeodesicBezier.cs` | de Casteljau with FlipOut | measured |
| Vector heat method, log map | `mesh_ops/VectorHeatMethod.cs` | transport, scalar extension, log map | measured (first order; AFFINE_LOGMAP_GATE.md) |
| Discrete exponential map | `mesh_ops/MeshLocalParam.cs` (upstream) | local polar coordinates, Dijkstra-like propagation | measured |
| Dijkstra on graphs | `core/DijkstraGraphDistance.cs` (upstream, `float`) | edge-graph distances | an upper bound (edge paths zigzag) |
| Steiner-point Dijkstra | `mesh_ops/TerrainSafety.cs` (`TerrainSafetyLines`, `Guaranteed` mode, lines ~819-830) | Dijkstra over the vertices and `SteinerPoints` **uniform** points per edge, straight through each triangle; doubled up to `MaxSteinerPoints` while not certified | **certified upper bound** (real surface paths); no lower bound other than the 3D distance; no a-priori error bound (uniform points) |
| Solvers | `solvers/SparseCholesky.cs`, `SparseSymmetricCG.cs` | direct and iterative | |
| Exact arithmetic | `math/ExactIntervals.cs` (`ExactInterval`: round to nearest, then widened by one ulp; `ExactDyadic`), `math/AdaptivePredicates.cs`, `math/ExactPredicates.cs`, `math/CoreMath` | proofs of signs with intervals; exact dyadic sums and products | `ExactInterval` has `+ - *` and `Square`; no `/` and no `sqrt` yet (both are correctly rounded in IEEE 754, so the one-ulp widening covers them the same way) |

Tests and tools that exist: `GeodesicDistanceTests` (plane, open boundary, cylinder, terrain, multiple sources, UTM,
log-domain strips, bowties, factorization reuse, 2^k bits of the log-domain solve), `IntrinsicGeodesicsTests`,
`IntrinsicRefinementTests`, `IntrinsicScaleInvarianceTests`, `GeodesicsIssueCasesTests` (G1-G3, from
geometry-central / potpourri3d issues), `MeshGeodesicPathTests`, `MeshGeodesicBezierTests`,
`InvarianceTests.GeodesicDistance_ScalesWithTheInput` (2^k bit identity), the `geodesic.*` cases of
`tools/DeterminismHarness` (bit-identical on linux x64, windows x64, arm64 with `CoreMath`, DETERMINISM.md), the CLI
`GeodesicQueryCommands`, `benchmarks/IntrinsicBenchmark` (heat error against the analytic distance on obtuse planes and
a ring sphere; scaling up to 160801 vertices: first Compute 5.4 s, further Compute 147 ms on the 401 x 401 grid).
No parallel loop exists in the geodesic code (grep `Parallel`: none), so there is no determinism risk to inherit.

**What is missing.** Nothing in the library computes the exact polyhedral distance, and nothing gives a **lower** bound
better than the straight 3D distance. So:

1. no oracle: every heat test compares with an analytic smooth distance, which exists only for planes, cylinders and
   spheres; on bumpy or terrain meshes the tests can only check orderings (`HillyTerrain_BetweenStraightLineAndEdgePaths`);
2. `TerrainSafetyLines.AlongSurface` is documented as **not guaranteed**: the heat method overestimates by up to 1-2 m
   next to real crests (open pit, 1 m DGM), which puts the safety line that much too close. `Guaranteed` falls back to
   the 3D distance, which is conservative where the surface path bends;
3. no a-priori bound on any approximation in the library.

## 3. Prior art

Citations marked (m) were first recalled from memory. The bibliographic data (authors, venue, volume, pages) was then
checked by web search on 2026-11-13 (section 3.5). The full texts were not read: the hosts of the open PDFs are not
reachable from this container. Statements about a paper's content that remain unverified keep the mark (m); they must
be checked against the papers before the first code PR, as SOURCES.md requires.

### 3.1 Exact polyhedral distance

- J. S. B. Mitchell, D. M. Mount, C. H. Papadimitriou, *The discrete geodesic problem*, SIAM J. Comput. 16(4),
  647-668, 1987, DOI 10.1137/0216045. "Continuous Dijkstra": **windows** on edges (an interval of the edge, the unfolded position of a
  pseudo-source in the edge's plane and its distance offset), propagated across triangles in order of distance;
  overlapping windows trimmed to the part where each is closer; vertices with angle sum above 2 pi (saddles) and
  boundary vertices become pseudo-sources (m: the treatment of boundary vertices). O(n^2 log n) time, O(n^2) space
  worst case (O(n^2) windows, each handled in O(log n) through the priority queue; the OCR'd abstract of one online copy
  reads "O(n log n)", which secondary sources treat as a transcription error); distance queries afterwards in O(log n).
- J. Chen, Y. Han, *Shortest paths on a polyhedron*, SoCG 1990; journal version *Shortest paths on a polyhedron,
  Part I: computing shortest paths*, Int. J. Comput. Geom. Appl. 6(2), 127-144, 1996, DOI 10.1142/S0218195996000095.
  A tree of windows, "one angle one split": at most one child per window survives per vertex; O(n^2) time, O(n) space.
- V. Surazhsky, T. Surazhsky, D. Kirsanov, S. J. Gortler, H. Hoppe, *Fast exact and approximate geodesics on meshes*,
  ACM TOG 24(3) (SIGGRAPH 2005), 553-560, DOI 10.1145/1073204.1073228. The practical MMP: window trimming by solving a quadratic for the
  intersection point of two windows' distance functions, a priority queue by the window's minimum distance; measured
  that the exact algorithm runs far below its worst case (secondary sources: about O(n^1.5 log n) on real meshes). An
  **approximate** variant merges adjacent windows; the abstract claims "bounded error" and the paper reports O(n log n)
  run time even for small thresholds; a later paper measured about 0.1% relative error. A *lower-bound property* of the
  approximation prunes the exact frontier for point-to-point queries. Read 2026-11-13 (section 4): the error is an
  accumulated per-window value xi' = max(xi0, xi1) + DeltaD, where DeltaD is the largest change of the distance function
  over the merged window; a merge is allowed only if xi' / D(p) <= eps_rel at that point (plus a local bound); the
  paper calls the accumulation "rather conservative". The approximate distance is proven to be a lower bound of the
  exact one (their Appendix A). Overlapping windows are cut at the single solution of a quadratic (section 3.3);
  pseudo-source windows go only into the region not already "illuminated" (section 3.2).
- S.-Q. Xin, G.-J. Wang, *Improving Chen and Han's algorithm on the discrete geodesic problem*, ACM TOG 28(4),
  104:1-104:8, 2009, DOI 10.1145/1559755.1559761. ICH: Chen-Han with a priority queue and a filter that discards a window if a vertex already has a smaller
  distance to the window's region; faster than MMP with considerably less space (the abstract); O(n^2 log n) worst case (m). CGAL's
  `Surface_mesh_shortest_path` is based on it (m; GPL, approach cited only).
- Y. Qin, X. Han, H. Yu, Y. Yu, J. Zhang, *Fast and exact discrete geodesic computation based on triangle-oriented
  wavefront propagation*, ACM TOG 35(4) (SIGGRAPH 2016), DOI 10.1145/2897824.2925930 (the author list of the first
  draft of this note was wrong). VTP: triangle-oriented region growing, window pruning synchronised across the edges of
  a triangle; the rules rest on Proposition A.1 of its appendix, eight observations (a)-(h), each a strict inequality
  between path lengths at one point (eg where a ray of one window meets the line from the other window's source), by
  planar geometry (read 2026-11-13; restated in interval form in Appendix A.5, A9); the abstract reports 4-15 times faster than MMP and ICH, 2-4 times faster
  than FWP-MMP / FWP-CH, the least memory, O(n^2) time. A replicability review measured 4.8 s on a 474K-triangle mesh,
  matching the paper.
- P. Trettner, D. Bommes, L. Kobbelt, *Geodesic distance computation via virtual source propagation*, Computer Graphics
  Forum 40(5), 247-260 (SGP 2021), DOI 10.1111/cgf.14371. **Approximate**, not exact (corrected from the first draft):
  propagates virtual sources (GSP), also on triangle soups, point clouds and line sources; better run time and accuracy
  than earlier approximate methods on about 10000 meshes; code MIT (not read). Belongs to section 3.2.
- Theory: S. Kapoor, *Efficient computation of geodesic shortest paths*, STOC 1999, announces O(n log^2 n); its
  details have never been published (surveys up to 2025 say so). Y. Schreiber, M. Sharir, *An optimal-time algorithm
  for shortest paths on a convex polytope in three dimensions*, DCG 39(1-3), 500-579, 2008, DOI
  10.1007/s00454-007-9031-0: O(n log n) time and space, convex polytopes only. Neither is practical.
- Implementations (approach only, source not read): D. Kirsanov's `geodesic` (MMP after Surazhsky et al., originally on
  code.google.com; MIT according to the pygeodesic wrapper, secondhand, to be checked before anyone reads it), wrapped by libigl (MPL, paper only) and pygeodesic; geometry-central (MIT) has a
  polyhedral exact geodesics module (m) and uses the heat method by default; CGAL (GPL, never read).

### 3.2 Approximations with a bound

- R. Kimmel, J. A. Sethian, *Computing geodesic paths on manifolds*, PNAS 95(15), 8431-8435, 1998, DOI
  10.1073/pnas.95.15.8431. Fast marching
  on triangulated domains, O(n log n); first order; **obtuse triangles** break the causality of the update and are
  handled by unfolding a virtual edge into a neighbouring triangle. Convergence to the viscosity solution, no explicit
  error constant for a given mesh. Already cited in SOURCES.md for the terrain option R2.
- J. A. Sethian, A. Vladimirsky, *Ordered upwind methods for static Hamilton-Jacobi equations: theory and algorithms*,
  SIAM J. Numer. Anal. 41(1), 325-363, 2003: the anisotropic generalisation, O(M log M), convergence to the viscosity
  solution proven.
- L. Aleksandrov, M. Lanthier, A. Maheshwari, J.-R. Sack, *An epsilon-approximation algorithm for weighted shortest
  paths on polyhedral surfaces*, SWAT 1998 (m); L. Aleksandrov, A. Maheshwari, J.-R. Sack, *Determining approximate
  shortest paths on weighted polyhedral surfaces*, J. ACM 52(1), 25-53, 2005, DOI 10.1145/1044731.1044733. Steiner
  points **inside the faces on the angle bisectors** (corrected from the first draft, which put them on the edges), in a
  **geometric progression** away from each vertex (ratio set by epsilon and the smallest angle at the vertex), a graph
  between the points of each triangle, Dijkstra: a **proven (1 + epsilon)** approximation with
  O(n / sqrt(epsilon) log(1/epsilon)) Steiner points (m: the exact count and its constants, which depend on the mesh's
  angles). M. Lanthier, A. Maheshwari, J.-R. Sack, *Approximating shortest paths on weighted polyhedral surfaces*,
  Algorithmica 30, 2001, DOI 10.1007/s00453-001-0027-5: uniform points, an additive bound (m: the bound's form).
  Survey: P. Bose, A. Maheshwari, C. Shu, S. Wuhrer, *A survey of geodesic paths on 3D surfaces*, CGTA 44(9),
  486-498, 2011, DOI 10.1016/j.comgeo.2011.05.006.
- S.-Q. Xin, X. Ying, Y. He, *Constant-time all-pairs geodesic distance query on triangle meshes*, I3D 2012 (m), and X.
  Ying, X. Wang, Y. He, *Saddle vertex graph (SVG): a novel solution to the discrete geodesic problem*, ACM TOG 32(6),
  170, 2013, DOI 10.1145/2508363.2508379: precomputed graphs for fast repeated queries;
  exact with an unbounded graph, approximate with a degree limit (measured).

### 3.3 The heat method and intrinsic triangulations

- K. Crane, C. Weischedel, M. Wardetzky, *Geodesics in heat*, ACM TOG 32(5), 152, 2013; *The heat method for distance
  computation*, CACM 60(11), 90-99, 2017 (cited in SOURCES.md). Varadhan's formula motivates it; the discrete method is
  first order in practice with t = h^2; **no a-priori error bound for a given mesh** (m). Our implementation follows it.
- N. Sharp, Y. Soliman, K. Crane, *Navigating intrinsic triangulations*, ACM TOG 38(4), 2019; N. Sharp, K. Crane,
  *A Laplacian for nonmanifold triangle meshes*, CGF 39(5) (SGP 2020); N. Sharp, K. Crane, *You can find geodesic paths
  in triangle meshes by just flipping edges*, ACM TOG 39(6), 2020 (all in SOURCES.md, implemented). Intrinsic Delaunay
  removes negative cotan weights (the "obtuse triangle" problem of the heat method); FlipOut gives exact locally shortest
  paths, which are an upper bound of the distance.
- Survey: K. Crane, M. Livesu, E. Puppo, Y. Qin, *A survey of algorithms for geodesic paths and distances*,
  arXiv:2007.10430, 2020: practical algorithms and their behaviour on real data (m: its specific comparisons).
- A. Belyaev, P.-A. Fayolle, *On variational and PDE-based distance function approximations*, CGF 34(8), 2015 (m), and
  later work: the distance as the largest (viscosity) subsolution of the eikonal equation, computed by convex
  optimisation. Relevant to the lower bound of section 4.2.

### 3.4 Robustness

- The exact algorithms are exact in real arithmetic. In floating point the known failure points are: window trimming
  near the intersection of two nearly equal distance functions (the quadratic is ill conditioned), windows of near-zero
  width (dropped or kept inconsistently), pseudo-sources at saddle and boundary vertices, needle and cap triangles
  where the unfolding is ill conditioned. Surazhsky et al. and Xin-Wang mention tolerances; no paper I recall gives a
  rounding-error analysis (m).
- Obtuse triangles: a problem for FMM (causality) and for the cotan Laplacian of the heat method (negative weights,
  solved here by intrinsic Delaunay and the boundary splits); **not** a problem in principle for window propagation,
  which only unfolds triangles.
- Exactness in the strict sense is out of reach: the polyhedral distance is a sum of square roots of algebraic numbers
  whose degree grows with the number of unfoldings (each unfolded vertex position needs a square root), and comparing
  two such sums is the **sum-of-square-roots problem**, with no known polynomial bound (M. R. Garey, R. L. Graham, D.
  S. Johnson, *Some NP-complete geometric problems*, STOC 1976, 10-22, DOI 10.1145/800113.803626; the paper discusses
  why membership in NP of the Euclidean problems is in question; the name "sum of square roots" for it is later usage). Expression trees with separation bounds (C.
  Burnikel, S. Funke, K. Mehlhorn, S. Schirra, S. Schmitt, *A separation bound for real algebraic expressions*,
  ESA 2001; Algorithmica 55, 14-28, 2009, DOI 10.1007/s00453-007-9132-4; the approach of LEDA / CORE, paper only) would decide every comparison exactly, at a cost that
  grows with the expression depth, ie with the path length in triangles. Not practical for a distance field.

### 3.5 Citation check (2026-11-13)

Checked by web search (bibliographic records, abstracts, secondary citations); full texts not read (the PDF hosts
are blocked here, not circumvented). Corrections made: the VTP author list; Trettner et al. 2021 is approximate, not
exact; Aleksandrov-Maheshwari-Sack place the Steiner points on the angle bisectors inside the faces; Kapoor's details
are unpublished. Confirmed: MMP, Chen-Han, Surazhsky et al., Xin-Wang, Qin et al., Trettner et al., Schreiber-Sharir,
Kimmel-Sethian, Sethian-Vladimirsky, Aleksandrov-Maheshwari-Sack (venue), Lanthier et al. (venue), Bose et al., SVG,
the Crane et al. survey, Garey-Graham-Johnson, Burnikel et al. Still (m): the error bound of the approximate MMP, the
treatment of boundary vertices in MMP, VTP's pruning rules, ICH's worst case, the AMS point count and constants, the
Lanthier bound's form, Xin-Ying-He 2012, Belyaev-Fayolle 2015, the heat method's convergence statement. Before code:
read MMP sections 3-4 (the lemma that shortest paths pass only through saddle and boundary vertices, used in Appendix
A) and VTP's pruning section, from a copy the owner provides or a reachable host.

**Second round (2026-11-13), full texts.** The owner allowed the paper hosts. Read as text (untrusted data, nothing
executed): Surazhsky et al. 2005 (hhoppe.com) and Qin et al. 2016 (eprints.bournemouth.ac.uk). Not reachable: MMP 1987
(www.cs.umd.edu/~mount/Papers/mmp-sicomp-87.pdf: HTTP 404, also with %7E), the AMS technical report (carleton.ca
TR-99-10: HTTP 404), hub.hku.hk (HTTP 403), no open copy of Xin-Wang 2009; nothing worked around. Resolved: the error
bound of the approximate MMP (section 3.1); VTP's pruning rules (section 3.1, Appendix A.5); the vertices a shortest
path can pass through, as Surazhsky et al. cite MMP: "(1) boundary vertices, (2) saddle vertices which are vertices
with total angle greater than 2 pi [...], and (3) parabolic vertices whose total angle equals 2 pi. Parabolic vertices
typically do not require special treatment since their neighborhoods unfold isometrically" (their section 3.1). This
confirms review point S1 (>= 2 pi) and is the basis of A7. Still (m), unread: MMP's own statement and its boundary
case, ICH's Theorem 3.2 and worst case, the AMS point count and constants, the Lanthier bound's form, Xin-Ying-He 2012,
Belyaev-Fayolle 2015, the heat method's convergence statement. Nothing that depends on them goes into library code.

How others solve it: geometry-central / potpourri3d and libigl offer the heat method as the default and an MMP
implementation as the exact option (m); CGAL offers ICH (m); none of them, to my knowledge, certifies the rounding of the
exact method (m).

## 4. What can be proven with our arithmetic

### 4.1 A certified enclosure of the polyhedral distance (window propagation in interval arithmetic)

The usable form of "exact" is an **enclosure**: for each vertex an interval [Lower, Upper] of doubles that provably
contains the polyhedral distance, computed by window propagation (MMP / VTP) in `ExactInterval` arithmetic (extended by
`/` and `sqrt`, both correctly rounded in IEEE 754, so the existing one-ulp widening covers them). The only other
function is the corner angle for the pseudo-source test (angle sum at least 2 pi), from `CoreMath.Atan2`, which is
correctly rounded and so enclosed the same way; no `System.Math` function and no libm dependence.

The argument is written out, self-contained, in [Appendix A](#appendix-a-the-enclosure-argument-for-review). In short:
each window keeps an **outer** interval (lower side) and an **inner** interval (upper side, certainly realised by a
real path). Upper is sound because inner intervals are realised. Lower is sound because every pruning test has the
form "this window's lower bound exceeds an upper bound of a real path", certified by an interval evaluation over the
whole removed piece (an end-point or bisector test is not enough), which can never hold at a point of a true shortest
path, so the window chain carrying a shortest path is never cut. Windows on boundary edges are kept as terminal
windows that record their edge ends. The ICH / VTP vertex filters are lemma-based, not of this form; they enter the
certified version only with their lemma proved in interval form (Appendix A, A4). The lower side needs no visibility at all,
only the triangle inequality in the unfolding. The width of [Lower, Upper] is measured, not bounded a priori; a
`MaxWidth` check reports a vertex whose enclosure is wider than a tolerance relative to its distance.

Determinism: one priority queue, serial. Ties in the queue are broken by a total order (distance bound, then window id
by creation order), so repeated runs and platforms give the same bits (only `+ - * / sqrt` and comparisons; .NET does
not contract to FMA; DETERMINISM.md). 2^k invariance: every operation, the one-ulp widening included, scales exactly
under 2^k (away from overflow and the subnormal range), so Lower and Upper are bit-identical up to the factor 2^k, and
the combinatorics (which windows survive) are identical. Rotations and translations change the edge lengths by rounding
only: the enclosures of the transformed mesh and of the original must overlap (a test that a bound is wrong would
catch).

Cost: the plain MMP / VTP in doubles, times the interval overhead (two roundings and widenings per operation, about 2 to
4 times the plain arithmetic), times the windows kept because dominance was uncertain (expected few: uncertain only
where two windows agree within a few ulps). Surazhsky et al. measure about O(n^1.5) windows (m); VTP is reported several
times faster (m). Upper-bound estimate for the use cases here: terrain bands of TerrainSafety (10^4 to 10^5 vertices),
test meshes (10^3 to 10^4): if the plain VTP runs in about 0.1-1 s at 10^5 vertices as reported for its era (m), the
interval version is about 0.5-5 s, which fits the 20 s per operation rule but **must be measured first** (section 6,
stop rule). The replicability review of VTP (4.8 s on 474K triangles, section 3.1) is consistent with this estimate. Memory: windows per edge grow like n^0.5 in practice (m); at 10^5 vertices a few 10^7 windows would be
too much, so the first step measures the window count on procedural terrain and the 169-degree plane.

### 4.2 Proven two-sided bounds without the exact method

- **Lower bound by Lipschitz check (own): proven, but measured too weak (option C stopped, 2026-11-13).** A
  piecewise linear function phi on the mesh with phi <= 0 at the sources and |grad phi| <= 1 in every triangle (in the
  triangle's own flat metric) is at most the polyhedral distance everywhere: along any surface path phi changes by at
  most the path length. So any field is turned into a certified lower bound by `phi / max(1, max_T |grad phi|_T)`, with
  |grad phi|_T = |d2 e1 - d1 e2| / |e1 x e2| (e1, e2 the edges from corner 0, d1, d2 the value differences) evaluated
  in interval arithmetic. The upper-bound probe before any code (throwaway, not committed; `MeshGeodesicDistance` with
  default settings, max per-triangle |grad| of its field on the input triangles):

  | mesh | max \|grad\| | p99.9 | triangles above 1 |
  |---|---|---|---|
  | plane 101 x 101, source in the centre | 1.100 | 1.100 | 44.9% |
  | plane 101 x 101, source at a corner | 1.178 | 1.140 | 46.3% |
  | bumpy terrain 101 x 101 | 1.147 | 1.096 | 17.7% |
  | ridge with 75 degree faces, line source | 1.417 | 1.117 | 51.7% |
  | sphere (normalized cube, 40 edge vertices) | 1.713 | 1.169 | 12.3% |
  | noisy plane 81 x 81 (vertices moved by up to 0.15 h) | 27.15 | 1.250 | 39.8% |

  The global factor therefore costs 10% to 96% of the distance everywhere: useless for `TerrainSafety`. It is a
  **limit of the certificate, not of the heat method**: the PL interpolant of the *exact* Euclidean distance on the same
  plane also violates it (max |grad| 1.099 over all triangles, 1.059 beyond 5 h, 1.017 beyond 20 h, 1.008 beyond 45 h:
  an excess of about 0.35 h / r). A naive local repair (lower the vertices of violating triangles towards the
  triangle's minimum, repeat, certify) reaches max |grad| 1.000000 in 15-160 ms but loses 70-86% of the distance (the
  lowering cascades). The best such certificate is the largest PL subsolution below the field (the distance as the
  largest subsolution: Belyaev and Fayolle, section 3.3; a capped fast-marching or convex-optimisation sweep); from the
  measured 0.35 h / r excess its loss would be about the heat error plus 0.35 h ln(r / h), a few h at r = 50 h
  (estimate, not measured). Prior-art check (2 searches): no paper found that certifies a lower bound this way on
  triangle meshes; landmark lower bounds (triangle inequality, VMV 2015) need exact distances to the landmarks. The
  proper lower bound is option A's Lower. **Option C dropped** (owner, 2026-11-13; section 9).
- **Upper bound by real paths:** the Steiner-point Dijkstra that `TerrainSafety` already has, or FlipOut's length for a
  pair. Proven, a-priori loose.
- **(1 + epsilon) by Aleksandrov-Maheshwari-Sack:** with the paper's geometric Steiner point placement, the graph
  distance D satisfies d <= D <= (1 + epsilon) d, so [D / (1 + epsilon), D] is a proven enclosure, if the placement
  follows the paper exactly (the constants depend on the smallest angle per vertex; needles force many points). Cost
  O(n / sqrt(epsilon) log(1/epsilon)) graph nodes and about (points per edge)^2 graph edges per triangle; for epsilon =
  1% that is tens of points per edge. Deterministic (Dijkstra with a total order on ties), 2^k-invariant like 4.1.

### 4.3 What stays measured only

- The heat method: no a-priori bound. The a-posteriori Lipschitz lower bound of 4.2 is proven but too weak to use;
  against the oracle of 4.1 its error becomes measurable on any mesh, not only on analytic ones. First numbers from the
  plain-double exact probe of section 6, corrected 2026-11-13 (`MeshGeodesicDistance` with default settings, one source in the centre or
  near the ridge; errors in mesh units, edge length h):

  | mesh | vertices | h | heat - exact, mean \|.\| | max \|.\| |
  |---|---|---|---|---|
  | terrain (sum of sines, amplitude up to 8) 101 x 101 | 10201 | 1 | 0.37 | 3.42 |
  | the same terrain 201 x 201 | 40401 | 1 | 1.07 | 5.12 |
  | ridge with 75 degree faces 161 x 161 | 25921 | 0.25 | 1.60 | 3.53 |

  This quantifies the documented "not guaranteed" gap of `TerrainSafety.AlongSurface` (raw grids, not its remeshed
  band; a benchmark on the TerrainSafety inputs follows with the oracle).
- Fast marching: a first-order monotone scheme; convergence is proven, a bound for a given mesh is not (m).
- The approximate MMP of Surazhsky et al.: "bounded error" per its abstract; the form of the bound is to be checked in
  the paper (section 3.5).

## 5. Options

| # | Option | Gives | Proven | Cost | Tier |
|---|---|---|---|---|---|
| A | **Exact window propagation with a certified enclosure** (MMP after Surazhsky et al. 2005 with the VTP / Xin-Wang vertex filter, in `ExactInterval`), new class, vertex sources first | [Lower, Upper] per vertex, practically exact; an oracle for every other method; `TerrainSafety.AlongSurface` could become guaranteed | yes (enclosure argument 4.1, own, to be reviewed) | ~1500-2500 lines with tests; risk in the window bookkeeping (saddles, boundaries, degenerate windows) | L |
| B | **(1 + epsilon) Steiner graph** (Aleksandrov-Maheshwari-Sack 2005), generalising the `TerrainSafety` Steiner Dijkstra | enclosure [D / (1+eps), D] | yes (the paper's theorem, if the placement is faithful) | ~600-900 lines; many graph nodes for small eps or small angles | M |
| C | **Heat method plus certificates**: the Lipschitz lower bound of 4.2 as an optional output of `MeshGeodesicDistance`; the Steiner or FlipOut upper bound | a proven lower bound, a measured accuracy | lower bound yes, accuracy no | ~200-300 lines; **dropped after the probe of 4.2**: the global factor loses 10-96%; the optimal variant (largest PL subsolution) is tier M, still loses a few h, a possible later research item | S (M) |
| D | Fast marching (Kimmel-Sethian) | a first-order field | no | ~600 lines; obtuse unfolding; dominated by the heat method we have (similar accuracy, no factorization reuse, intrinsic Delaunay already fixes obtuse cases for heat) | M |
| E | Exact with expression trees / separation bounds | truly exact comparisons | yes | cost grows with path length; not practical (3.4) | L+ |

Not recommended: D (no bound, no gain over the existing heat method), E (cost), and Surazhsky's approximate merge as a
"bounded" method until its bound is checked.

## 6. Recommendation

**Option A**, staged in one feature PR (clean commits), after the owner approves this note:

1. **Probe and stop rule first**: a plain-double window propagation on procedural meshes, measuring windows per edge,
   peak memory and time. Stop rule: if the 10^5-vertex terrain needs more than 10 s in doubles or more than 2 GB, stop
   and fall back to option B. **Done 2026-11-13** (throwaway probe, not committed; MMP-style one-sided trimming on each
   edge side, trimming against the edge-end vertices, pseudo-sources emitted into every incident face; no VTP / ICH
   filter, no compact storage, so pessimistic).
   **Corrected 2026-11-13:** the first probe classified each piece between two computed roots of the pair trimming by
   its midpoint sign alone, with roots from an unstable quadratic formula, and was up to 1.08e-2 too high against an
   untrimmed reference (41 x 41 terrain; the first check had covered only 21 x 21). Fixed by stable roots and by
   removing a piece only if the signs at both of its ends and at its midpoint agree; all variants then agree with the
   untrimmed reference to 7e-15. The numbers of the stop rule, from the corrected probe (which also merges adjacent
   windows with the same origin and position, and tests whole pieces against the third vertex of the face beyond; net10
   Release, this container; managed memory from the GC):

   | mesh | vertices | time | windows per edge | managed memory |
   |---|---|---|---|---|
   | terrain 317 x 317, centre / corner source | 100489 | 3.25 / 2.97 s | 4.6 | about 250 / 280 MB |
   | rough terrain 317 x 317 (z noise +-0.3 h) | 100489 | 3.3 s | 4.5 | 240 MB |

   The plane and ridge rows of the first version of this table came from the defective probe; the gate-2 table of the
   next change replaces them.

   The stop rule passes on its defined case (the 10^5 terrain); the steep ridge does not count against it
   (coordinator, 2026-11-13). Flat regions are the expensive case (many tying windows from one pseudo-source).
   Gates before any library code: **gate 2**, with the VTP filter and compact window storage still in plain doubles,
   the 10^5 ridge at most 10 s and 2 GB (the plane 101 x 101 reported too); **gate 3**, in the interval version, the
   10^5 terrain at most 10 s and the ridge at most 20 s. The interval version uses only filters proven in interval form
   (A4).
2. The interval version with the enclosure (4.1), `Status` (`Ok`, `NonManifoldInput`, `DegenerateTriangles`,
   `EnclosureTooWide`, `WindowLimit`), `MaxWindows`, vertex sources; results `Lower`, `Upper` (and `Distance` as their
   midpoint for convenience). Serial only; no parallel loop. Exact-code rules (no FMA, no `float`) apply if the file is
   added to the exact list in `.editorconfig`; it should be, since its proof depends on them.
3. Use it as the **oracle** in the heat, FlipOut and vector heat tests on non-analytic meshes, and benchmark the heat
   method's true error in `IntrinsicBenchmark`.
4. Later, separate decision: `TerrainSafety.AlongSurface` on the enclosure, which would close the documented "not
   guaranteed" gap. The safe side is **Lower**: a point is certainly at least SafetyDistance away along the surface if
   Lower >= SafetyDistance (the first draft said Upper; corrected).

Option C was dropped after its probe (4.2; section 9, decision 5).
Option B is the fallback if the stop rule fires, and the better choice if many sources and huge meshes matter more
than tightness.

## 6a. Gates 2 and 3 (2026-11-13, throwaway probes, not committed)

**Gate 2** (plain doubles; the 10^5 ridge at most 10 s and 2 GB). The probe of section 6 plus: merging of adjacent
windows with the same origin and position (fragmentation on the plane from 8.6 to 1.0 windows per edge), a whole-piece
test against the third vertex of the face beyond (the certified form of A4, not the lemma-based ICH / VTP filter).
Exactness checked against a reference that emits from every flat vertex and neither merges nor uses the third vertex:

| mesh | vertices | time | windows per edge | memory | result |
|---|---|---|---|---|---|
| plane 101 x 101 | 10201 | 0.15 s | 1.0 | 25 MB | exact (1e-13) |
| terrain 317 x 317, centre / corner source | 100489 | 3.25 / 2.97 s | 4.6 | 250 / 280 MB | exact |
| rough terrain 317 x 317 | 100489 | 3.3 s | 4.5 | 240 MB | exact |
| ridge (75 degree faces), xy jitter 1e-3 h, 317 x 317 | 100489 | 6.6 s | 14.4 | 630 MB, peak 1.2 GB | exact (4e-9 at 161 x 161): **passes** |
| ridge, exactly aligned with the grid, 317 x 317 | 100489 | 5.0 s | 10.0 | 530 MB | **not exact**: up to 9.75e-3 off at 161 x 161 |
| the same with every flat vertex emitting | 100489 | 281 s | 199 | 5.9 GB | exact: **fails** the gate |

Cause of the aligned row: the source lies on a grid row, so rays run exactly through rows of flat vertices on the fold
lines; in plain doubles only emission from flat vertices (review S1) recovers them, at the cost above. The coordinator
counted gate 2 as passed for non-degenerate data and made the aligned ridge an explicit case of gate 3.

**Gate 3** (the interval prototype: outward-rounded intervals, disk positions (A.2), outer and inner intervals,
terminal windows (M1), whole-piece certified pruning (A4), merging (A6), flat vertices emitting or not (A7)).
Correctness: zero enclosure violations against the reference on the split square, plane, terrain, saddle (41 x 41) and
the aligned ridge (81 x 81), in both settings; without flat-vertex emission the Lower gap on the aligned ridge is
3.9e-12. Cost after the one optimisation step (a) (dead-window compaction per edge side, an overlap pre-filter, merging
only nearly identical windows, otherwise subsumption; net10 Release, flat vertices not emitting):

| mesh | vertices | before (a) | after (a) | windows per edge | target (information) |
|---|---|---|---|---|---|
| terrain 101 x 101 | 10201 | 2.1 s | 1.3 s | 4.3 | |
| terrain 201 x 201 | 40401 | 8.0 s | 6.7 s | 4.3 | |
| terrain 317 x 317 | 100489 | | 18.8 s | 4.4 | 10 s: missed |
| ridge aligned 81 x 81 | 6561 | 16.2 s | 16.4 s | 37.5 | 2 s: missed |

About 14 us per window on the terrains (about 5 times the double probe), spent in the certified pair and vertex tests;
on the aligned ridge the flat regions produce ties (equal distance functions from different windows) that no certified
test removes, so the window count and the list scans (20.7M) dominate. Upper is loose on flat regions (up to 13%
relative on the ridge, 6% on the plane: inner intervals rarely contain a vertex certainly); Lower is tight.

**Decision (coordinator, 2026-11-13):** the certified interval version is a **test oracle for meshes up to about 10^4
vertices** (decision 2's role); the 10^5 targets move to a later public class and are no longer a gate-3 criterion; no
structural redesign of the flat-region ties without a new decision: it is an **open item, not started**. Before any library code, A7 and the replacement of
the grazing rule (Appendix A.5) are reviewed; A8 is withdrawn.

## 6b. The test oracle in the library (2026-11-13)

`mesh_ops/ExactMeshGeodesicDistance.cs`, **internal**, serial, under the exact-code rules (the `mesh_ops/ExactMesh*`
pattern of `.editorconfig`: a deliberate `Math.FusedMultiplyAdd` fails the build with RS0030 and G3E0003). It implements
A1-A6, subsumption and the terminal windows (M1); flat vertices are pseudo-sources (S1 / A5); **A7 itself (continuation windows) is not shipped; its duplicate-free form A7' is (2026-11-19, section 6d): an exactly flat interior vertex is of kind 0 and emits nothing**.
`ExactInterval` got division and square root (correctly rounded in IEEE 754 on every target; tests at the subnormal and
overflow edges). Corner angles for the pseudo-source test use `CoreMath.Atan2`, widened by two spacings. Its correct
rounding (at most half a spacing) is an assumption: an empirical claim of the port, backed by bit-exact tests against a
BigInteger reference (`docs/SOURCES.md`), not a proof; the widening leaves a margin of 1.5 spacings beyond it. The direction error of A.2 uses the bound 2 asin z <= 2 z / sqrt(1 - z^2)
instead of `Math.Asin`, so no libm function enters and the bits are the same on every platform
(`tools/DeterminismHarness` case `geodesic.exact`); the file is on the G3E0004 list of `.editorconfig`, so a platform
libm call fails the build. The result arrays are NaN until a successful run (`Status` is `NotComputed` meanwhile), so
a window limit, a too-wide position or an exception never leaves values that look valid; a vertex without triangles
stays NaN. Review hardenings (2026-11-13): pruning drops only empty pieces (a short piece is no certified drop); the
merge thresholds of A6 are relative to the mesh's length scale, so 2^k scalings merge the same windows (a test asserts
merges on a plane over `Similarity.All()`); the disk of a box uses the sum of the half widths (their squares can
underflow). The steps' bounds have direct tests against exact references (`ExactDyadic`): the disk of a box, the frame
change of a disk with adversarially wide boxes at the scales 1, 1e-150 and 1e150, and the corner angles at known and
hard atan2 arguments. `MaxWindows` is checked between propagation steps, so `Windows` can end slightly above it.

**Exact-code review (2026-11-15).** A child window's outer interval came from the values of `Mu` (the crossing of the
ray from the source through (t, 0) with the next edge) at the ends of its clip [t0, t1], which assumes `Mu` monotone in
t. With the source within about its disk radius of the edge line, the clip end t1 = xc.Hi can lie past the ray
parallel to the next edge; `Mu` then changes branch inside the clip, its end values stay finite, and the outer interval
shrank to about [0, 0] while the true crossings covered the edge: `Lower` came out above the distance (a five-vertex
sliver mesh, h from 1e-8 to 1e-14; the S2 rule "a position not certainly strictly behind the edge's line gives the whole
reachable edge" was missing, and S2 alone would not have been enough). Fix: `Mu`'s denominator, which is affine in t,
is evaluated with t as the interval [t0, t1]; only if it certainly keeps its sign over the box and the clip are the end
values used (their hull), otherwise the outer interval is the whole edge and the inner one empty. On thin slivers the interval of the denominator can straddle 0 only through its widening, while the true one keeps its sign; such a child then also falls back to the whole edge, which loses precision (a wider `Lower`/`Upper` gap downstream) but not soundness. Tests: the reviewed
mesh and its mirror (the right child), and a random family of slivers (heights 1e-6 to 1e-15 and a few ulps, sources
beyond the sliver, obtuse faces) against the straight distance, which is the geodesic distance there. Also: `Status`
is reset before each run; a vertex with a finite `Upper` but no lower bound ends the run with `LowerBoundMissing`
instead of a silent NaN; a fan walk that stopped early never classifies a vertex as convex; the correct rounding of
`CoreMath.Atan2` is stated as the assumption it is (empirical, backed by bit-exact tests, not a proof).

**Why A7 is not shipped as continuation windows (the open item was closed by A7', section 6d).** The constructive continuation windows of A7 are
sound (reviewed), but on exactly flat regions they duplicate the ordinary windows: the ordinary windows already carry
the rays through flat vertices, and the continuation windows carry the same origin with the same true position, so their
distance functions are equal. Neither A6 (it needs a pending partner) nor certified dominance (equal functions) removes
such duplicates, and a per-vertex dedup of the continuations (a canonical frame per vertex and subsumption of their
disks) does not catch the duplicates of the ordinary windows. Measured: a 5 x 5 plane reached the limit of 100000
windows with 112616 continuation windows. Section 6d: the duplicates show that the ordinary windows carry the ray, so the
duplicate-free form needs no continuation windows at all (A7', Appendix A.6).

**Measured reach** (net10 Release, this container; S1 setting; 0 enclosure violations against the plain-double
reference that emits from every flat vertex). The first table is that of the first version (2026-11-13); after the A6
rule of section 6c (#1058) the reach is that of the second table (`benchmarks/GeodesicOracleBenchmark reach`, centre
sources, the ridge's source as in the tests; largest (Upper - Lower) / Lower over the vertices):

| mesh | vertices | time | windows per edge | largest (Upper - Lower) / max(1, d) | Lower gap to the reference |
|---|---|---|---|---|---|
| plane 41 x 41 | 1681 | 3.3 s | 17 | 1.3e-2 | 9.5e-9 |
| terrain 41 x 41 | 1681 | 0.5 s | 3.9 | 5.7e-9 | 5.7e-9 |
| saddle 41 x 41 | 1681 | 2.1 s | 13 | 9.5e-9 | 9.5e-9 |
| aligned ridge 21 x 21 | 441 | 1.2 s | 10 | 6.3e-3 | 8e-9 |
| aligned ridge 31 x 31 | 961 | 1.6 s | 17 | | |
| terrain 101 x 101 | 10201 | 4.0 s | 4.3 | | |
| plane 101 x 101 | 10201 | window limit | | | |
| aligned ridge 41 x 41 | 1681 | window limit | | | |

| mesh (benchmark) | vertices | before #1058: time, windows, width | after: time, windows, width |
|---|---|---|---|
| plane 41 x 41 | 1681 | 5.5 s, 126803, 8.0e-2 | 3.0 s, 59229, 9.5e-9 |
| terrain 41 x 41 | 1681 | 0.4 s, 18835, 5.9e-9 | 0.5 s, 18835, 5.9e-9 |
| saddle 0.3 (x^2 - y^2) 41 x 41 | 1681 | 2.1 s, 63649, 9.5e-9 | 2.1 s, 63649, 9.5e-9 |
| terrain 101 x 101 | 10201 | 2.6 s, 125155, 1.4e-8 | 2.9 s, 125155, 1.4e-8 |
| plane, saddle 101 x 101 | 10201 | window limit (12-14 s) | window limit (14-16 s) |
| aligned ridge 21 x 21 | 441 | 0.7 s, 19594, 1.7e-1 | 0.3 s, 10588, 8.0e-9 |
| aligned ridge 31 x 31 | 961 | 2.8 s, 77247, 8.0e-2 | 1.5 s, 39852, 1.4e-8 |
| aligned ridge 41 x 41 | 1681 | 13.9 s, 286137, 3.8e-2 | 5.7 s, 97650, 1.9e-8 |

So the oracle reaches about 10^4 vertices on terrain-like input, but only about 2 x 10^3 on exactly flat or exactly
aligned input: **short of the 10^4-vertex target of the plan** on those. Beyond, `Compute` returns `WindowLimit` with NaN
arrays (the clean way out; a test pins it on the aligned 41 x 41 ridge with a limit of 60000). The default `MaxWindows`
is 300000, which keeps a `Compute` below about 20 s (the plane 101 x 101 took 116 s to reach the former limit of 2
million).
**Width:** see section 6c: (Upper - Lower) / Lower is at most about 2e-8 on every procedural surface class measured
(Lower within about 1e-8 relative of the distance: the re-emission margins of A5 add up along chains of flat
pseudo-sources), so Upper is as tight. Before #1058 Upper was up to 8.2% above the distance on flat and nearly flat
input; the 1.3% first stated here was the plane from a corner source. Tests assert the enclosure and a width bound
per mesh, never equality.

### 6c. The width per surface class and the A6 rule (#1058, 2026-11-17)

**Question.** The class documentation stated "Upper up to about 1.3% above the distance on flat regions"; the gate
follow-up tests of #1051 found 4% on a nearly flat convex dome. The enclosure Lower <= d <= Upper was valid; the
question was the width per surface class and whether a cheap, provably safe lever tightens Upper.

**Measurement** (`benchmarks/GeodesicOracleBenchmark sweep`): grids over [-1, 1]^2 with the alternating diagonals of
the tests, domes -k (x^2 + y^2) and saddles k (x^2 - y^2), k in {0, 1e-8, 1e-6, 1e-5, 1e-4, 1e-2, 0.3}, 11 x 11, 21 x 21
and 41 x 41 vertices, sources at the centre, an edge midpoint and a corner (9 runs per row). The distance d is
enclosed by the oracle's own Lower, which is certified (Lower <= d) and tight (within about 1e-8 of the closed form on
the plane and the ridge, and of Upper after the change), so (Upper - Lower) / Lower bounds the gap
(Upper - d) / d from above and equals it to about 1e-8. Largest over the 9 runs:

| surface | k | before: width, slowest run, windows (sum) | after: width, slowest run, windows (sum) |
|---|---|---|---|
| dome / saddle | 0 (a plane) | 8.0e-2, 17 s, 712914 | 1.9e-8, 6.9 s, 310874 |
| dome | 1e-8 | 8.0e-2, 16 s, 714431 | 1.9e-8, 7.2 s, 310872 |
| dome | 1e-6 | 8.2e-2, 8.5 s, 525527 | 1.9e-8, 3.8 s, 206501 |
| dome | 1e-5 | 5.3e-2 (two runs at the window limit, 35 s), 914812 | 1.9e-8, 0.9 s, 123720 |
| dome | 1e-4, 1e-2, 0.3 | 1.9e-8 | 1.9e-8 (the same windows within 0.5%) |
| saddle | 1e-8 | 8.0e-2, 17 s, 717088 | 1.9e-8, 7.3 s, 310861 |
| saddle | 1e-6 | 8.0e-2, 18 s, 696689 | 1.9e-8, 6.9 s, 309721 |
| saddle | 1e-5 | 4.9e-3, 10 s, 415405 | 1.9e-8, 7.5 s, 341794 |
| saddle | 1e-4, 1e-2, 0.3 | 1.9e-8 | 1.9e-8 (the same windows) |

The gap was bimodal: not driven by curvature, edge length against the curvature radius or path length as such, but by
whether vertices emit. Where the interior vertices are flat (exactly flat inputs) or only nearly convex (domes with k up
to about 1e-5, whose interior vertices are certainly convex, so they never emit, A5), the source's windows travel far,
and the width did not shrink with the grid (8% from 11 x 11 on with a centre source, 1.3-8% from the other
positions). Curved inputs (k >= 1e-4) were tight before.

**Cause** (traced window by window on the 21 x 21 dome, k = 1e-6): Upper of a vertex comes from a window whose inner
interval (the part realised by a real path, A1) covers the vertex's ray, otherwise from edge paths (which zigzag by up to
8% on these grids). A window is split at the opposite vertex of each face into two children whose inner intervals meet
at that vertex's ray. Where the two halves' rays meet again on a later edge side (around a convex or flat vertex the
unfoldings of both sides coincide up to the disk radii), A6 merged them, as the same origin with nearly the same
position, and kept the inner interval of only one, as A6 requires: the union is not realised by one witness (a ray from
one half's witness through the other half's inner interval passes the vertex on the wrong side). So the second half
of every fan lost its realised paths, and the vertices behind it fell back to edge paths. The degenerate projections of
S2 (the child of a ray through an edge end along a grid line, whose `Mu` denominator straddles 0) also give empty inner
intervals, but on 1e-15-wide clips; they cost nothing measurable.

**Building blocks and prior art.** The library has no other realised-path bound to reuse here (the edge path pass and
the Steiner Dijkstra of `TerrainSafety` are the zigzag fallback; FlipOut needs a start path). The exact algorithms of
MMP and Surazhsky et al. 2005 do not merge windows (their approximate variant merges with a bound on the distance error,
not on a realised path), and Xin-Wang's filter drops windows instead; no prior work carries certified inner intervals, so
the lever is a restriction of this note's own A6. Upper bound of the gain before the change: the whole measured gap
(Upper can at most reach d), 8.2% on these inputs, nothing on the curved ones.

**Levers tried.** (1) Keep the longer of the two inner intervals in a merge: the dome k = 1e-6 went from 8.2% to 3.7%,
the plane to 1.2%. (2) Compute the child's inner interval over its inner sub-clip when the outer clip's denominator is
uncertain (S2): no effect (those clips are degenerate), not kept. (3) Record the direct bounds of the dropped inner
interval (its edge ends and the opposite vertex) before the merge: no effect, not kept. (4) **Merge only when at most one
partner has a nonempty inner interval, and keep that one**: Upper is tight everywhere above, and the window counts drop
by a factor of 2 to 7 on flat and nearly flat input (tighter vertex bounds prune more in A4), so the change is cheaper,
not dearer. Merging only windows without inner intervals keeps the width but costs 21-26% more windows (plane 41 x 41:
74874 against 59229); keeping a merge when one inner interval contains the other made no measured difference and was
dropped for the simpler rule.

**Soundness of rule (4)** (A6 as amended in Appendix A): skipping a merge removes nothing from any window, so A3's chain
and every outer interval stay as they were (Lower unchanged in kind), and two windows with the same function are only a
cost, which the measured counts above bound for these inputs; the windows limit still ends a run cleanly. A merge that
happens keeps one nonempty inner interval: b's if it has one, otherwise w's, clipped to the merged outer interval. w's
inner interval is realised from w's witness s*_w and path P*_w (A1); s*_w lies in w's disk, which lies in the merged disk
(the merged radius is at least the centre distance plus w's radius); sigma+ of the merge is at least w's; a subset of a
realised set is realised (A1, last line); and the children of the merged window compute their inner intervals for every
position of the merged disk, so they are realised from s*_w. An empty interval (I0 > I1) stays empty under the clip.

**Tests** (`ExactMeshGeodesicDistanceTests`): the width bound 1e-7 relative on twelve members of the family (21 x 21:
flat, nearly flat and curved domes and saddles, every source position), on the plane (0.08 before) and on the aligned
ridge 21 x 21 (0.2 before), each against the 3D distance and an edge path, and on the plane against the closed form; the
nearly flat dome over `Similarity.All()` (exact scales bit for bit, the windows and merges equal, every frame still
narrow) with a reuse check; a window budget on the ridge that kills the rule "merge only without inner intervals".
Mutants checked by hand: without rule (4) 16 tests fail, without keeping w's inner interval 17, with the
inner-free-only rule 1.

## 6d. A7 with duplicate control and A9: a design (2026-11-19, docs only; the probe is throwaway, not committed)

Task (owner 2026-11-19, roadmap item 2): restate A7 with duplicate control and A9, with the correctness argument and an upper
bound of what they give on the oracle's flat and aligned inputs. No library code before the argument is reviewed.

**Finding: the continuation windows are not needed, the ordinary windows already carry the ray.** The duplicates of
section 6b are not a defect of an implementation that duplicate control could repair; they are the proof that nothing has
to be built. `Child` clips with closed intervals and encloses every ray of a clip, so the ray through a vertex p is a
boundary ray of the children on both sides of p, and it stays inside the closed outer interval of every window that
goes around p's fan (the children on the edges (p, y_i) have outer intervals that start at p). In an exactly flat fan
the unfolding is single-valued, so the window that reaches the face behind p carries the straight continuation. What
S1 / A5 pay for is only the *pseudo-source* of such a vertex: its emission of whole-edge windows, which on a flat region
equal the true windows on the one ray through p and are strictly longer elsewhere, so no certified dominance (A4)
removes them. Duplicate control is therefore by construction: **A7' (below) = an exactly flat interior vertex is not a
pseudo-source and has no continuation windows.** The argument is Appendix A.6.

**Rule A7'.** (1) `VertexKind` returns 0 for an interior manifold vertex that the exact flatness test of A.5 accepts
(coplanar fan with winding 1, or a straight fold; exact predicates on the double coordinates, no tolerance); every other
vertex that is not certainly convex stays a pseudo-source. (2) No continuation windows. (3) No guard: the closure argument (A.6) rests on strict widening of every interval operation, not on a special case. (4) Upper is unchanged (edge paths and inner intervals; the final Dijkstra over edges replaces the
neighbour bounds the flat vertex's emission gave).

**Probe** (the A7' rule with a tolerance in place of the exact test, `kind` set to 0 for the flat vertices; net10 Release,
this container, one thread; the same binary and run for both columns; meshes of `GeodesicOracleBenchmark`; `MaxWindows`
raised for the right column). Enclosure checked against the closed forms (plane: Euclid; roof: the developable
unfolding): 0 violations on either side in every row, and the new and the old enclosures overlap at every vertex.

| mesh | vertices | now (S1): time, windows | A7': time, windows | windows per edge side now -> A7' (live) |
|---|---|---|---|---|
| plane 41 x 41, centre | 1681 | 2.9 s, 59229 | 0.29 s, 17655 | 10.7 -> 3.6 |
| plane 41 x 41, corner | 1681 | 7.9 s, 120164 | 0.97 s, 36518 | 21.8 -> 7.7 |
| roof, slope 4, 41 x 41 | 1681 | 6.1 s, 98585 | 0.46 s, 32069 | 16.6 -> 6.6 |
| roof, slope 4, 61 x 61 | 3721 | 29.2 s, 382098 | 1.3 s, 104920 | 28.9 -> 9.7 |
| plane 101 x 101, centre / corner | 10201 | window limit | 3.7 s, 260k / 13.8 s, 532k | 8.6 / 17.8 |
| roof, slope 4, 101 x 101 | 10201 | window limit | 5.5 s, 481k | 16.0 |
| plane 201 x 201, centre / corner | 40401 | | 23 s, 2.0M / 110 s, 4.2M | |
| roof 75 degrees, 41 x 41 (implementation, exact test) | 1681 | 5.6 s, 97650 | 0.45 s, 32110 | 16.5 -> 6.6 |

Three limits of the bound:

1. **What the exact test accepts.** (Corrected at the implementation, 2026-11-19: the first version of this note counted only
   the coplanar fans of A.5 (i) and concluded that the 75 degree ridge gains little.) The benchmark's ridge has z = tan(75
   degrees) * c in doubles, so its slope fans are not exactly coplanar (114 of 361 interior fans at 21 x 21 pass `Orient3d` = 0),
   but z depends on x only: every vertex has two neighbours on a line along y and each half is coplanar with that line, so
   every vertex is an exactly flat **straight fold** (A.5 (ii)) and the exact test accepts all of them. Measured with the
   implementation: the 75 degree ridge 41 x 41 takes 0.45 s and 32110 windows (5.6 s and 97650 with the flat vertices emitting),
   the slope-4 ridge (the exactly aligned test member of the review) 0.43 s. What the exact test still rejects are
   vertices flat only up to rounding, eg a rotated or UTM-shifted copy of a plane: they stay pseudo-sources (a true sum of
   2 pi + 1e-16 allows a bend; skipping them would need cone-limited emission, the duplicate-prone A7 of A.5).
2. **Reach.** The windows per edge side still grow linearly with the grid width (3.6, 8.6 and 17 live windows per side at
   41, 101 and 201 for the plane from the centre). The cause is fragmentation, not duplicates of distinct functions: the
   live windows of a side hold **1.0 distinct (origin, position) per side** on the plane and the roof from one
   source (1.6-1.9 from a corner source or with other pseudo-sources). A7' alone reaches 10^4 vertices in 4-14 s (plane) and 5.5 s (roof) and 4 x 10^4
   in 23-110 s (about 11 us per window, 120 bytes per window); the extrapolation to the plane 317 x 317 (10^5 vertices) is
   8M windows from the centre, about 90 s and 1 GB: **10 times short of the target of 6a**. Merging adjacent windows of the same
   origin and position when the first one is already propagated (A6 merges only a pending partner; the lever is a
   propagation that is delayed until adjacent partners arrived, or a merge that extends the propagated window by the
   partner's piece and queues only that piece) would bring the windows per side toward the 1.0-1.9 distinct ones: a bound
   of 8-16 times fewer windows at 10^4 vertices and the growth in n removed. It is a separate claim (A6 for a propagated
   window needs the argument that the children of the extension are exactly what the partner's own children would
   have been), not part of A7'.
3. **Tightness.** Without the re-emission margins of A5 the enclosure is tighter on the flat inputs: (Upper - Lower) /
   Lower 1e-12 instead of 1e-8 from the centre; from a corner source 5e-8 at 101 x 101 and 1e-7 at 201 x 201 (the chain of
   margins of the corner's other emissions). The new and the old enclosures differ in the last digits (Lower moves by up to
   5e-7 on a roof of distance 25, 2e-8 relative), so **the tests against the oracle cannot demand the same bits**: they
   demand overlapping enclosures, the closed forms, and a width bound.

**A9.** Upper bound of the gain, measured as the share of live windows that another live window of the same edge side
beats at five sample points of their whole piece (an optimistic stand-in: the final windows are compared, the
dominating window is taken as valid although its inner interval may not cover the point, and the order of creation is
ignored): plane and roof from one source 0 % (all windows are one origin and one position: equal functions, strict
inequality never holds), terrain 101 x 101 17.8 % (21295 of 119296), saddle 41 x 41 44.4 % (28030 of 63209), plane from
a corner 0.4 %. So A9 gives **nothing on the flat and aligned inputs**, at most 1.2 times on terrain and 1.8 times on the
saddle (the descendants of a dominated window are dominated too, so the fraction bounds the saving of an online rule from
above, up to the rule's own cost). Each of the eight observations needs its own reviewed interval form (A.5) and the
dominating side must be restricted to inner intervals, which the probe did not do, so the real gain is lower.

**Implemented (2026-11-19, the code PR after this note).** `VertexKind` returns 0 for exactly flat interior vertices; measured with
`GeodesicOracleBenchmark reach` (net10 Release, this container; flat vertices emitting, then A7'): plane 41 x 41 3.2 s, 59229 windows ->
0.6 s, 17655; plane 101 x 101 71 s, 988852 windows -> 3.4 s, 260274 (it reached the window limit of 300000 before); aligned ridge
41 x 41 5.6 s, 97650 -> 0.45 s, 32110; slope-4 ridge 101 x 101 (window limit at 2M windows, 194 s) -> 5.6 s, 480529; terrain and
saddle unchanged (no flat vertices); (Upper - Lower) / Lower 1e-12 instead of 1e-8 on those inputs. The A.6 review's
conditions are met (invariant form, no guard G).

**Leaning (before the implementation).** Build A7' (a small change: `VertexKind`, the benchmark input; the argument A.6 to review first);
it gives the factor 10 to 20 on the flat and aligned inputs at 2 x 10^3 vertices, takes the plane and the ridge to 10^4
vertices, and makes the oracle's flat members cheap enough for the tests (the 5 s rule). Do **not** build A9 now: 0 on
the inputs of the task, at most 1.2-1.8 elsewhere, eight claims to review. Treat the fragmentation lever as the next
item if the 10^5 class is to be certified; for the **public class** the data of 6a say it should be the plain-double
propagation (3.3 s on the 317 x 317 terrain, 6.6 s on the jittered ridge) with the certified oracle as its test reference,
not the interval version (18.8 s on the terrain); this is a decision for step 2b.

## 6e. The public class for 10^5 vertices: a design (2026-11-19, docs only, no code; sketch accepted by the coordinator)

Decisions (coordinator 2026-11-19): the name `PolyhedralGeodesicDistance`; **uncertified** with a measured tolerance, the
class docs saying "measured on the tested families, not a certified bound" and pointing to the certified oracle for
certified results, the tolerance in the docs being the one the tests enforce, no looser; no `Certified` mode in v1; this
note and Appendix A.7 first, reviewed independently (done once, 2026-11-19: both blockers and the should-fix items are worked in
below), and code only after the note is merged; the memory bound in the docs and a test of it.

```csharp
public enum PolyhedralGeodesicStatus { NotComputed, Ok, NoSource, NonManifoldInput, DegenerateTriangles, WindowLimit, Cancelled }
public sealed class PolyhedralGeodesicDistance {
  public DMesh3 Mesh { get; }
  public double MaxDistance = double.PositiveInfinity;  // no window is created beyond it; vertices farther away stay NaN
  public long   MaxWindows = 0;                          // 0: 64 x the vertex count; checked between steps
  public Func<bool> CancelRequested;                     // optional, polled between steps
  public PolyhedralGeodesicStatus Status { get; }
  public double[] Distance { get; }                      // per vertex id; NaN for unused ids, unreached vertices, and every vertex unless Status is Ok
  public int[] NearestSource { get; }                    // the source that realises Distance (lowest id at an exact tie), -1 where NaN
  public long Windows { get; }                           // created by the last Compute
  public PolyhedralGeodesicDistance(DMesh3 mesh);
  public PolyhedralGeodesicStatus Compute(IEnumerable<int> sourceVertices);
}
```

v1 has vertex sources only, is serial and deterministic (every operation IEEE double, no FMA; the bits are the same on every
platform and 2^k scalings give the same bits away from overflow and the subnormal range), and can be reused.
- **Statuses.** As the oracle's, without `PositionTooWide` and `LowerBoundMissing` (no disks, no lower bound), with `Cancelled`
  added. `Compute` sets `Status` to `NotComputed` first and fills `Distance` and `NearestSource` with NaN and -1, so an exception
  or a limit never leaves values that look valid. `DegenerateTriangles` uses the oracle's interval area test (`area2.Lo > 0`), not
  a plain `area2 > 0`.
- **`MaxDistance`.** A NaN or negative value throws `ArgumentOutOfRangeException` from `Compute`; 0 computes the sources only;
  vertices with a distance above it are NaN.
- **`NearestSource`.** Each pseudo-source origin carries its root source (a table indexed by origin vertex, set at each emission,
  so a window of an earlier emission with another root reports the later root: the reported source is then within the error xi
  of the nearest, documented); at an exact tie the lowest source id wins; the tests accept either source at an exact tie.
- **Docs.** The class docs refer to `MeshGeodesicDistance` (the heat method: smooth, approximate, any size) for the cheap
  approximate distance and to the certified oracle for certified results.
- **Memory.** A window is a 64-byte struct (layout in A.7); a run holds at most `MaxWindows` + one step's windows of them, plus
  per window a 4-byte link of its edge side's list (an intrusive list, no `List` doubling) and an entry of the queue, an
  **indexed** heap with one entry per pending window (a merge decreases the key of b's entry, so lazy re-inserts do not exist and
  the queue is bounded by the windows); the heap's array may double (24-byte entries, up to twice the entries). The windows live
  in chunks of min(65536, `MaxWindows` + 2 x the largest valence) structs, so nothing is copied when the store grows and a
  small `MaxWindows` allocates a small chunk. The bound stated in the class docs: at most **120 bytes** x (`MaxWindows` + 2 x the
  largest valence) (64 + 4 + 2 x 24) plus about **57 bytes per vertex** (33 for the per-vertex data: `Distance`, the estimate
  U and the error xi_u of the vertex test, the kind, the root; and 24 for the heads of the edge side lists, an array by edge
  id, no `Dictionary`). A window's state (pending, propagated, dead) is the sign bit of its side index. The test runs
  single-threaded: it allocates a mesh with a small `MaxWindows`, checks `WindowLimit` and NaN arrays,
  `Windows <= MaxWindows + 2 x max valence` (one step creates at most two children or one emission), and the allocated bytes
  (`GC.GetAllocatedBytesForCurrentThread` before and after) against the bound. The default of 64 windows per vertex (770 MB
  at 10^5 vertices by the bound above) must be enough for the target
  families, which the benchmark checks: the oracle with A7' creates 47-52 windows per vertex at 101 x 101 and the number grows
  with the grid width, the class with merging should stay below it (A.7). An addition to the API only (`dotnet pack`).

**Accuracy contract.** The class docs state, per family, what was measured, not one formula: (a) on the families with a closed
form (planes, folds, valleys, ridges, cones, L-shapes) the largest |Distance - d| over all vertices, relative to d and to the
extent; (b) on the other families (terrain, saddle, domes, noisy sphere) the largest distance of `Distance` from the oracle's
enclosure [Lower, Upper], whose own width is up to about 2e-8 relative there, so the agreement claimed on those families is
that, not the smaller figures of (a). The numbers are the constants of the tests, and the class docs quote exactly them. The
model of A.7 (K frame changes and merges, each moving a stored position by a bounded amount, K the number of faces a path
crosses) is the rationale for the form, not a bound: K is not a property of the mesh, and an absolute term in the extent
makes any claim weak for small d, which the docs say. The first measurement may show that a figure of 1e-9 relative is
reachable; it is not promised before it is measured.

**Tests against the oracle** (the 5 s rule): PR tests use meshes of 41-61 vertices a side (the oracle takes 0.1-3 s there); the
comparisons at about 10^4 vertices (the oracle takes 3.4-14 s) are `[Trait("Category", "Nightly")]`, with the reason (the oracle's
run is the check) in the test; the 317 x 317 closed forms and the timings are the benchmark's. Families: those of the
oracle's tests, a noisy sphere, several sources, a cone with a sharp convex apex against its closed-form unfolding, and the oracle's
sliver family (the 2026-11-15 `Mu` fix) against the straight distance, a collinear chain of flat vertices ending at a convex bend
(the along-the-edge windows, A.7) against its closed form, and a mesh on which one origin is unfolded at two places (a tube, a loop
around a hole) for the position check of the merge. Always, on every mesh: Distance >= the 3D Euclidean
distance - tol (a polyhedral path is never shorter than the straight line: this catches a window that is not realised, A.7).
Invariance over `Similarity.All()` (2^k bit equality, tolerance overlap for rotated copies), reuse, repeated-run determinism, the
statuses, `MaxDistance`, the cancel poll, the memory test. Mutants by hand (the by-hand list is in A.7), and the incremental run.

**Determinism and the build rules.** The convexity test of the classification uses `CoreMath.Atan2`, so the new file gets
its `.editorconfig` entry (G3E0004: no platform libm), a `tools/DeterminismHarness` case. **No `DeterministicApis.txt` entry (decided with the code PR):** that list and the analyzers G3D0001-9 are the closed
world of the exact kernel and its building blocks (the files named in the second `.editorconfig` section), which the class is not part of: it is serial, uses only
IEEE +, -, *, / and `Math.Sqrt`, `Math.Min` and `Math.Max`, no parallel loop and no transcendental function, and its bits are checked by the harness case and by
the invariance and determinism tests. The classification reuses the oracle's `VertexKind` (the certified interval test of the angle sum, `CoreMath.Atan2`, and
the exact flatness test): kind 0 only if certainly convex or exactly flat, so the coordinate size does not enter a margin (a
margin in radians would be wrong by the ratio of the coordinates to the edge lengths, 1e-10 rad at UTM scale).

**Targets** (net10 Release, the cloud reference machine; benchmark README with before and after): 317 x 317 terrain 5 s and
1 GB (the double prototype of 6a: 3.3 s, 250 MB; that prototype also had the third-vertex test, which the class has, A.7),
plane and the slope-4 ridge 10 s. **Methods and sources:** MMP 1987 and Surazhsky et al. 2005 (windows, priority queue,
pseudo-sources, the accumulated error of a window), the exact flatness test of A.5 for A7' (flat vertices emit nothing),
merging of pending windows of one origin (A.7) and the plain pruning of A.7; **no ICH (Xin and Wang) and no VTP filters**, no code
ported (if a filter of Xin and Wang is added later it is listed in `docs/SOURCES.md` then). The sources go into `docs/SOURCES.md`
with the code PR. **Not in v1:** surface-point sources, `TracePath`, parallel runs, a `g3cli` option, a certified mode.

## 7. Test plan (repo style)

### 7.1 Analytic oracles for the polyhedral distance

- **Plane** (grids with alternating diagonals, the 169-degree plane, random Delaunay and non-Delaunay triangulations):
  the polyhedral distance is Euclidean (convex domain): `Lower <= |p - q| <= Upper`, width below 1e-12 relative. The
  169-degree plane checks that obtuse triangles do not matter.
- **Prism around a cylinder** (an inscribed n-gon prism without caps): developable, so the distance equals the
  Euclidean distance in the unrolled strip, the minimum over the wraps (`k * n * chord`): an exact closed form for the
  polyhedral distance, including cut loci (the half-way line behind the source).
- **Cone** (developable apart from the apex) and the **regular tetrahedron / cube** (closed-form unfoldings for pairs of
  vertices; the cube corner distances are known): saddle-free vertices with angle defect, paths over edges.
- **Unit square split by its diagonal** from (0, 0) to (1, 1), source (0, 0) (review M1): every vertex must get a
  finite Lower and the Euclidean distance; it fails without terminal windows on boundary edges.
- **Sphere** (inscribed **convex** mesh, vertices on the sphere; the triangulation must be the convex hull of the
  points: the library's cube-grid sphere generator has reflex edges, 4332 of them at 40 edge vertices, and there the
  exact distance exceeds the arc by up to 2.3e-6 in the probe, legitimately): the nearest-point projection onto the
  convex polyhedron is
  1-Lipschitz and maps the great-circle arc onto a surface path between the same vertices, so
  `chord <= d_poly <= arc` (own argument): a proven bracket on a curved surface without an exact closed form. The
  enclosure must lie in it; the heat method too (it does not, by about 2% plus h/2, which the test measures, not asserts).
- **Saddles and boundaries**: a monkey saddle and an L-shaped planar domain (the shortest path bends at the reflex
  corner: distance = |p - c| + |c - q|, closed form), a plane with a square hole.

### 7.2 Property tests (any procedural mesh: bumpy grids, terrain, noisy spheres)

- Lower bound: `Lower >= |p - q|` (3D distance). Upper bound: `Upper <= FlipOut length` for the same pair (a real path),
  and `Upper <= Steiner Dijkstra` (TerrainSafety's graph).
- Edge Lipschitz: `|d(u) - d(v)| <= len(uv)` within the enclosures; triangle inequality over three sources; symmetry
  `d(a, b) = d(b, a)` within the enclosures.
- Multiple sources: the result equals the minimum of the single-source results (bit for bit: the same windows win).

### 7.3 Repo rules

- **Invariance** over `Similarity.All()` (`tests/geometry3Sharp.Tests/Harness/Invariance.cs`): 2^k scales give Lower
  and Upper bit for bit times 2^k and the same window counts; other similarities give overlapping enclosures.
- **Reuse**: `Compute` twice on one instance (same sources, then other sources, then the first again): identical bits.
- **Determinism**: serial only, so repeated runs bit-identical; a `geodesic.exact` case in `tools/DeterminismHarness`
  (only `+ - * / sqrt`: expected bit-identical across platforms without `CoreMath`).
- **Success paths check the result**: finite, `Lower <= Upper`, width bounded, status `Ok`; failure paths their status
  (non-manifold, degenerate, window limit).
- **Mutation**: a new shard for the file (exact-code rules, so it belongs with the exact shards); mutants the tests must
  kill: trimming without the certainty check (an enclosure that misses the true value on the prism and the L-domain),
  the inner and outer intervals swapped, a saddle vertex not made a pseudo-source (the monkey saddle), a boundary vertex
  not made a pseudo-source (the L-domain), the tie order changed (the reuse and determinism tests); per claim of
  Appendix A: a pruning comparison made non-strict or against an outer-only window (A4), the child's inner interval
  computed from the box midpoint instead of for every s in the box (A1), no re-emission after Lower(p) decreases (A5),
  a merged window keeping the union of the inner intervals (A6), merging by origin alone without overlapping positions
  (A6, S6), no terminal windows on boundary edges (the split square, M1), an end-point instead of a whole-piece test in
  a pruning rule (A4, M2), a threshold of 2 pi + eps instead of sum+ < 2 * Math.PI in the pseudo-source test (A5, S1:
  a flat vertex on the path). A mutant that only costs time (eg a pseudo-source at
  every vertex) is equivalent and is listed as such.
- **Test time**: meshes of at most a few thousand vertices; each test well under 1 s; the 10^5 measurements stay in the
  benchmark.
- **New tests on base**: the oracle tests fail on base by construction (the class does not exist); heat tests that
  switch to the oracle keep their old assertions too.

## 8. Risks

- Window bookkeeping is the classic source of bugs in exact geodesics (saddles, boundary vertices, windows of zero
  width, windows reaching a vertex exactly). The enclosure argument turns most rounding issues into a wider interval,
  not a wrong one, but only if it is implemented as argued; the review must check it line by line, like the exact
  boolean.
- Memory and time grow faster than linearly (n^1.5 measured in the literature); the stop rule and `MaxWindows` contain
  it.
- Degenerate triangles: the polyhedral distance is defined on the input mesh; zero-area triangles make the unfolding
  undefined. Either fail with a status or work on the mollified intrinsic metric (then it is the exact distance of a
  slightly different surface; the note of the code PR must say which).
- The enclosure width on needles may be too wide to be useful; measured, reported by status.
- Several citations above are from memory (m) and must be checked; in particular the bound of the approximate MMP and
  the constants of Aleksandrov-Maheshwari-Sack.

## 9. Decisions

Owner decisions on the first draft (2026-11-13, via the coordinator):

1. Option A with the stop rule of section 6. Before any code: the citations are checked (section 3.5, done as far as
   the network allows) and the dominance-trimming argument gets an independent review (Appendix A).
2. A test oracle first; a public class only after the review and the measurement.
3. The file goes under the exact-code rules.
4. Degenerate triangles fail with a status for now.
5. Option C: first "now, as a small separate PR"; after the probe of section 4.2 (the planned certificate loses
   10-96% of the distance) **dropped** (owner, 2026-11-13, option (a) of three). Option A's Lower is the lower bound
   that `TerrainSafety.AlongSurface` will use. Not planned, a possible later research item: the largest PL subsolution
   below the heat field (option (b)), tier M; risks: still a loss of a few h by the estimate of 4.2 (not measured), no
   prior art for the certificate on triangle meshes, and its sweep is a fast-marching-style update whose behaviour on
   obtuse triangles (section 3.4) is the known weak point.
6. No fast marching as a distance method.

Order: the citation check and the argument section, then the measurement step of A (done, section 6), then gate 2.

Review of Appendix A (2026-11-13, routed by the coordinator): the structure is sound (Upper realised; Lower survives
one-sided pruning and needs no visibility). Worked in: M1 terminal windows on boundary edges; M2 pruning certified over
whole sub-intervals, ICH / VTP filters excluded from the certified version until their lemma is proved; S1 >= 2 pi and
the threshold 2 * Math.PI; S2 degenerate projections; S3 the atan2 argument; S4 intrinsic positions, the wrapping
effect measured in stage 1; S5 re-emission or tightening on a decrease of Upper(p); S6 merging needs overlapping
positions; S7 emission into every incident face; S8 open point 3 covered; S9 2^k away from overflow / subnormals, the
rotation test kept.

Second round (2026-11-13): gate 2 passed for non-degenerate data, the aligned ridge moved to gate 3; gate 3 decided
as (c) the oracle up to about 10^4 vertices plus (a) one optimisation step (section 6a); A7 in constructive form and A9
(VTP's rules in interval form) for review; A8 withdrawn (Appendix A.5).

Appendix A holds the claims A1-A6 and the remaining assumptions. A claim added for a borrowed filter gets its own short
review.

## Appendix A: the enclosure argument (for review)

Self-contained statement of why option A's [Lower, Upper] contains the polyhedral distance. The reviewer needs only
this appendix; the algorithm details beyond what is stated here are free choices that do not affect soundness.
Revised after the review of 2026-11-13 (must-fixes M1, M2 and should-fixes S1-S9, marked in the text).

### A.1 Setting

- M: a manifold triangle mesh, possibly with boundary; every triangle certainly non-degenerate (its doubled area
  enclosed away from 0; otherwise Compute fails with a status, owner decision 4). Vertex coordinates are doubles; the
  true edge lengths l_e = |p_a - p_b| are real numbers, enclosed in intervals.
- S: the source vertices. d(x): the polyhedral distance from S to a point x of M (the infimum of the lengths of surface
  paths), the quantity to enclose at the vertices.
- Interval arithmetic: every computed quantity q is an interval [q-, q+] that contains the real value computed by the
  same formula from the true inputs (inclusion property of `ExactInterval`: + - * / sqrt rounded to nearest and widened
  by one ulp). Corner angles: atan2(|u x v|, u . v) with `CoreMath.Atan2` (correctly rounded) widened by one ulp,
  evaluated at the corners of the argument box. This is valid because the box lies in the open upper half plane
  (|u x v| > 0 for a non-degenerate triangle), so it does not contain the origin, and the set of directions of a
  convex box not containing the origin is an arc whose ends are attained at corners; within (0, pi) there is no branch
  cut. (S3: the first draft argued by monotonicity in each argument, which is false in y.)
- Fact F (MMP 1987 [m]: MMP itself was not read; the statement is as Surazhsky et al. 2005, section 3.1, cite it,
  including the parabolic vertices, read 2026-11-13): a shortest path from S to x is a polyline that is straight inside
  every face and across every edge (straight in the unfolding of two adjacent faces), and passes through a vertex other
  than its ends only if the vertex is a boundary vertex or has angle sum **>= 2 pi** (S1: a flat vertex, angle sum
  exactly 2 pi, can lie on a shortest path).

### A.2 Data

A **window** w lives on one side of an edge e and carries:

- an origin o(w): a source or a pseudo-source vertex, with an interval [sigma-, sigma+] for d(o);
- the unfolded origin as a **disk** (S4, revised 2026-11-13): a centre c (doubles, in the frame of e) and a radius r
  with the true unfolded position s* within distance r of c. The first draft proposed an intrinsic form (intervals for
  the distances to the two ends of e, the position rebuilt by the law of cosines); measured in the prototype it is
  unusable: rebuilding a position from two distances amplifies the width by about 2d / L per unfolding (d the distance
  to the source, L the edge length), eg +-1e-8 after a few steps at d = 5, then unbounded. A box carried through
  rotated frames grows by up to sqrt 2 per face (the wrapping effect). A disk does not: a rigid motion, a reflection
  included, maps a disk onto a disk of the same radius. Per frame change (from edge e to edge e' of the face beyond,
  e' with ends P, Q whose positions are interval boxes in the frame of e): the new centre is computed in doubles,
  c' = R (c - p) with p, q the midpoints of the boxes and R the rotation (and reflection, so that the old face lies on
  y < 0) taking the direction of q - p to +x. With delta_P, delta_Q the half diagonals of the two boxes (the true
  P*, Q* lie within delta_P, delta_Q of p, q), the direction error is at most
  alpha = 2 asin((delta_P + delta_Q) / (2 (|PQ| - delta_P - delta_Q))) (plus the rounding of the direction, 8 ulp(|PQ|) /
  |PQ|), which needs delta_P + delta_Q < |PQ| (else the step fails with a status); its first-order form
  (delta_P + delta_Q) / |PQ| may be used only with a margin for delta_P + delta_Q << |PQ|. Then
  r' = r + delta_P + (|c - p| + delta_P + r) alpha + 16 (ulp terms), every term rounded upwards: the true image of s*
  differs from c' by at most r (the old radius, preserved by the motion), delta_P (the shifted origin), the rotation
  error alpha acting on |s* - P*| <= |c - p| + delta_P + r, and the rounding of the computed centre. (The review wrote
  (|c - p| + delta_P) alpha; the r alpha term is added here since r is not negligible against |c - p| in general.) (Review of #807, S3: the first form lacked the cross term alpha delta_P and used box widths; the prototype's
  widths were conservative but its cross term was missing.) So r grows additively by about (|c| + L) 1e-15 per step,
  not multiplicatively. Measured on long bumpy strips (250, 1000 and 4000 x 3 vertices, unit spacing): the
  radius relative to the distance rises to 1.2e-12 and then stays there up to 4000 edge lengths (by distance, log2
  buckets: 4e-14, 8e-14, 1.4e-13, 2.5e-13, 5e-13, 1e-12, 1.1e-12, 1.2e-12, then 1.2e-12 to the end);
- an outer interval O(w) and an inner interval I(w) of e (parameter intervals, clipped to e; I may be empty);
- whether e is a boundary edge on w's side (a **terminal** window, M1): terminal windows are recorded like any other
  (edge ends, Lower, Upper, the pseudo-source trigger) but never propagated.

Lower and upper values of w on a set J of e: L_w(J) = (|x - s| + sigma)- and U_w(J) = (|x - s| + sigma)+, evaluated
in interval arithmetic over x in J and the reconstructed position s.

Lower(v) = the minimum of L_w({v}) over all windows (terminal ones included) whose outer interval contains the vertex v
as an edge end, and 0 if v is in S. Upper(v) = the minimum of U_w({v}) over windows whose inner interval contains v,
and of Upper(u) + l_uv+ over the neighbours u (an edge path is a real path), iterated to a fixed point (Dijkstra on
these values), so Upper is finite on every component with a source.

### A.3 Claims

- **A1 (realised inner intervals).** For every window w there are a real point s* in the reconstructed position set of
  w and a real path P* from S to o(w) of length at most sigma+ such that for every x in I(w) the segment from s* to x
  lies in the unfolded strip of w's history. Then P* followed by that segment is a surface path to x of length at most
  U_w({x}).
  *Initialisation:* a source o in face F: s* = o, sigma = [0, 0], a window on the opposite edge with I = the whole edge
  (a face is convex); terminal if that edge is a boundary edge (M1: eg the unit square split by its diagonal from
  (0, 0) to (1, 1), source (0, 0): the opposite edges of both faces are boundary edges, and without terminal windows
  Lower would be +inf at (1, 0) and (0, 1)).
  *Propagation* across the face F' beyond e into its two other edges: the child's inner interval on an edge e' of F'
  is the set of points that lie on a ray from s through I(w) **for every s in the position set**, computed with
  outward rounding and clipped to e'. The child keeps s* and P*.
  *Degenerate projections (S2):* when the position set is not certainly strictly on the far side of e's line, or a
  denominator of the projection contains 0, the child's inner interval is empty (and its outer interval is the whole
  reachable edge, A3). When it is uncertain on which side of the opposite vertex of F' a ray passes (the split of w
  between the two edges), the inner intervals are kept only where the side is certain.
  *Pseudo-sources:* a pseudo-source at vertex p gets sigma+ = Upper(p) at its creation, which by A2 is at least the
  length of a real path P* to p; its windows start at s* = p. Upper(p) may still be +inf then; when it decreases, p is
  re-emitted or its windows' sigma+ tightened (S5; this affects the width only, not soundness).
  *Inner intervals may be shrunk by any rule* (heuristic trimming included): a subset of a realised set is realised.
- **A2 (Upper).** Upper(v) >= d(v). From A1 and from edge paths being real paths.
- **A3 (outer coverage of a shortest path).** Let gamma be a shortest path from S to a vertex v, crossing edges at
  y_1, ..., y_k between its pseudo-source vertices p_0 (a source), ..., p_m. Claim: for every crossing y_i after p_j
  there is a window w with o(w) = p_j, y_i in O(w) and L_w({y_i}) <= d(y_i); and at each p_j, Lower(p_j) <= d(p_j).
  *Lower side needs no visibility:* for the window of gamma's segment, the position set of w contains the true
  unfolding s* of p_j along gamma's faces; gamma's piece from p_j to y_i is the straight segment in that unfolding
  (F), so its length is |y_i - s*| and d(y_i) = d(p_j) + |y_i - s*| >= sigma- + |y_i - s*| >= L_w({y_i}), given
  sigma- <= d(p_j).
  *Propagation:* the child's outer interval on e' contains every point on a ray from any s in the position set through
  any point of O(w) (outward-rounded interval evaluation of the projection, clipped to e'; the whole reachable edge or
  both edges in the degenerate cases of S2). gamma continues straight from s* through y_i to y_{i+1}, so y_{i+1} is in
  the child's outer interval. At the last crossing before p_{j+1} (or v), the edge end p_{j+1} is reached: O(w)
  contains it (outer intervals are closed and include edge ends they reach), so Lower(p_{j+1}) <= L_w({p_{j+1}}) <=
  d(p_{j+1}). If the edge beyond is a boundary edge, the child is a terminal window and still records its edge ends
  (M1; also the case where p_{j+1} has only boundary edges besides e).
- **A4 (safe pruning, M2).** Any rule that removes a sub-interval J from O(w), or a whole window (J = O(w)), must
  certify over all of J, by an interval evaluation over J:
  inf_J L_w > sup_J B, where B(x) is a certified upper bound of the length of a real path to x, ie either U_{w2}(J) for
  a window w2 with J inside I(w2) (MMP trimming, made one-sided), or Upper(u) + |u - x| for a vertex u of a face
  containing J (evaluated as Upper(u) + sup_{x in J} |u - x|, the segment from u to x lying in that face). The
  inequality is strict between computed bounds. A pointwise test at the ends of J or at a computed bisector point is
  **not** enough: |x - s| - |x - s2| is not monotone on J. In MMP trimming the computed bisector is only a guide: each
  side of it is removed from the dominated window only as far as the interval test over that piece succeeds (split the
  piece if needed); an uncertain band around the bisector stays in both outer intervals.
  Such a rule never removes gamma's crossing y_i from gamma's window: there L_w({y_i}) <= d(y_i) and every real path to
  y_i has length >= d(y_i), so inf_J L_w <= L_w({y_i}) <= d(y_i) <= sup_J B for any J containing y_i, and the
  condition fails. Hence A3's chain survives every pruning.
  The vertex filters of ICH and VTP are **not** of this form: they drop a whole window on a test at its ends, justified
  by a lemma of the respective paper. They may be used in the plain-double measurement probe, but in the certified
  version only after the lemma is proved in interval form as its own claim and reviewed (coordinator, 2026-11-13).
- **A5 (pseudo-source test and re-emission).** A vertex is made a pseudo-source unless it is not on the boundary and
  its angle sum is certainly below 2 pi, ie sum+ < 2 * Math.PI (S1: Math.PI is the double below pi, so 2 * Math.PI,
  an exact doubling, is below 2 pi, and sum+ < 2 * Math.PI implies sum < 2 pi; a flat or uncertain vertex becomes a
  pseudo-source). Extra pseudo-sources cost time only; missing ones would break A3 at a vertex of gamma. A
  pseudo-source emits into **every** incident face, including those behind a saddle (S7; the exact algorithms emit only
  into the shadow region; a certified shadow test is a later optimisation with its own claim). Its windows use sigma- =
  Lower(p) at emission. If Lower(p) later decreases, p is emitted again with the new value (label correcting), so the
  final sigma- of the windows of the chain is <= d(p). The queue order (by window minimum lower value, ties by creation
  id) affects only cost and determinism, not soundness. A limit on re-emissions and windows ends with a status, never
  with a wrong bound.
- **A6 (merging duplicates).** Two windows on the same edge side with the same origin, overlapping outer intervals
  **and** overlapping position sets and sigma intervals (S6: the same origin alone is not enough, since a saddle loop
  can unfold the same vertex to two different places) arise when two face sequences unfold the same pseudo-source to
  the same place, eg on a plane. They may be merged into one with the hull of their position sets and sigma intervals,
  outer = the union, inner = the inner interval of one of them. A3: the merged L is at most both, and its outer
  propagation covers both rays. A1: the kept inner interval keeps its witness s*, which lies in the hull, and the
  child's inner interval is computed for every s in the hull, so it is realised from s*. Without merging, such
  duplicates are never certainly dominated (equal functions) and would double at every step.
  *Amended (#1058, section 6c):* they are merged only when at most one of them has a nonempty inner interval, and the
  kept inner interval is that one (clipped to the merged outer interval). Two nonempty inner intervals, eg the two
  halves of a fan split at a vertex whose rays meet again, stay two windows: dropping one cost Upper up to 8%, and
  skipping a merge removes nothing, so it needs no argument beyond the cost, which section 6c measures.

**Conclusion.** Lower(v) <= d(v) (A3, A4, A5) and d(v) <= Upper(v) (A1, A2), for every vertex v of a component with a
source. Both bounds are deterministic (serial, total order) and, away from overflow and the subnormal range,
bit-identical up to the factor 2^k under 2^k scalings (every operation, the one-ulp widening and the correctly rounded
atan2 of scaled arguments included, scales exactly) (S9). Rotations are not bit-identical: the separate test on the
rotated 169-degree plane checks that the enclosures overlap.

### A.4 Assumptions and open points for the reviewer

1. Fact F for boundary vertices and the exact statement of MMP's lemma (to be read in MMP 1987; section 3.5).
2. A3 needs the window of gamma's segment to be created at all: the initialisation from each source into every
   incident face and the emission of every pseudo-source into every incident face (A5, S7).
3. Points on edge ends and boundary edges are covered by the terminal windows (M1) and by the pseudo-source rule for
   flat vertices (S1); gamma through a vertex that is certainly convex and not on the boundary is impossible by F (S8).
4. Termination and cost are not part of the argument (limits with a status). Whether the uncertain regions stay small
   (they should: two windows with different origins are within rounding of each other only near a bisector point) and
   how much the positions widen along long strips (S4) were measured in stage 1: about 1.2e-12 relative, saturating
   (A.2).
5. Non-manifold input and degenerate triangles are refused with a status; the mollified metric is not used (owner
   decision 4).

### A.5 Second round (2026-11-13): flat vertices, ties, grazing rays, borrowed filters (for review)

Found while building the gate-3 prototype. A7 and A9 are proposed claims for the independent review; A8 is withdrawn.
The prototype implemented an earlier form (an angle-sum stand-in for flatness, the implicit closure below, a sliver
skip and A8); it was exact on every test mesh, but that is a measurement, not the argument.

- **A7 (exactly flat vertices need not be pseudo-sources), constructive form.** (Reviewed; **not shipped**: the
  continuation windows duplicate the ordinary windows on flat regions, section 6b; an open item.) S1 / A5 make every vertex that is not
  certainly convex a pseudo-source; an enclosed angle sum of exactly 2 pi is never certainly below 2 pi, so every flat
  vertex would emit (on the aligned ridge in plain doubles: 281 s instead of 5 s). Proposed instead:
  *Exact flatness test* (decides angle sum = 2 pi exactly, without angles; all predicates exact, on the double
  coordinates). Let n_0, ..., n_{k-1} be p's neighbours in fan order (n_k = n_0), at an interior vertex.
  (i) *Coplanar fan:* every neighbour lies on the plane through p and two non-collinear neighbours (`Orient3d` = 0);
  project onto a coordinate plane on which that plane projects regularly (a plane whose `Orient2d` of p and the two
  neighbours is non-zero). Require every consecutive sector to turn the same way, `Orient2d(p, n_i, n_{i+1}) > 0` (each
  sector angle in (0, pi)), and count the **winding**: with the reference ray from p through n_0, count the sectors i
  that reach or pass the ray, ie `Orient2d(p, n_i, n_0) > 0` and (`Orient2d(p, n_0, n_{i+1}) > 0`, or n_{i+1} on the
  ray: `Orient2d(p, n_0, n_{i+1}) = 0` with n_{i+1} - p in the direction of n_0 - p), the last sector (back to n_0)
  included. The angle sum is 2 pi times that count; require exactly 1. One sign alone does not suffice (review of
  #807, M1): eight sectors of 90 degrees turn the same way and wrap twice (4 pi, a saddle).
  (ii) *Straight fold:* two neighbours a = n_0 and b = n_j lie on a line through p on opposite sides (`Orient3d` and
  `Orient2d` collinearity exact, and (a - p).(b - p) < 0, exact); each half (n_0, ..., n_j and n_j, ..., n_k) is
  coplanar with the line ab, and within its own plane every intermediate neighbour lies strictly on one side of the
  line ab (constant non-zero sign of `Orient2d` against the fold line) with all consecutive sectors turning the same
  way. Then each half goes from ray pa to ray pb without crossing the fold line, ie spans exactly pi, and the total is
  2 pi.
  Any other vertex that is not certainly convex stays a pseudo-source (S1).
  *Continuation window.* When a window's ray through an exactly flat vertex p is possible (p lies in the outer
  interval's image, ie the split point or an edge end of the window's propagation is uncertain about p), unfold p's fan
  into the plane of the current face: by flatness the unfolding is single-valued (going around either side gives the
  same positions). The straight continuations of the rays from the disk (c, r) through p form a cone with apex p; with
  outward rounding it can meet the sectors of several fan faces (review of #807, S1). Create, in **every** fan face
  whose sector the outward-rounded cone meets, a window on the edge opposite p with the same origin and sigma, its disk
  transformed into that edge's frame, outer = the outward-rounded image of the cone in that face, inner empty.
  *Proof sketch.* A shortest path gamma through an interior vertex p with angle sum exactly 2 pi is straight in the flat
  unfolding of p's fan (review of #807, S2; Fact F is the context, MMP [m]): the two branches of gamma at p split the
  fan into two sides whose angles sum to 2 pi; if one side had an angle below pi, that side unfolds into a planar
  sector of angle below pi, and two points of gamma near p, one on each branch, are joined by a chord inside that
  sector which is strictly shorter than the path through p, contradicting gamma being shortest; so both sides are at
  least pi, hence exactly pi, and gamma is a straight line through p in the unfolding; its piece before p is carried by a window w whose outer image contains p (A3), its unfolded origin s* lies
  in w's disk, and its continuation beyond p is the ray from s* through p, which lies in the cone and so hits the edge
  opposite p in F_k inside the continuation window's outer interval; the lower bound there is at most d by the same
  triangle-inequality argument as A3. So A3's chain continues past p without emission from p. Windows that pass p on
  either side are unchanged; the continuation window only covers the ray through p itself.
  *Grazing rays (replaces the sliver skip).* The case where a window's end ray runs exactly along the next edge (the
  source in line with an edge PQ, the ray passing through P and running along PQ) is the ray through the vertex P (and,
  at the far end, Q); it is covered by the continuation window at P (and Q) when they are flat, and by emission when
  they are pseudo-sources. The prototype's skip of such slivers is withdrawn: it cut the sweep of windows around P on
  one side, and nothing proved that the other side's sweep reaches the exit face.
- **A8 (tie slack, Lower - F Delta_max): withdrawn.** The prototype dropped a piece of a new window w where an
  inner-valid window b was better by at most Delta and subtracted F Delta_max from every Lower (F faces, since a
  shortest path crosses each face at most once). That is not sound: the drop removes w's piece at gamma's crossing
  y_i, and b's rays leave y_i in another direction, so b's descendants need not cover gamma's continuation; the real
  path "b to y_i, then along gamma" is carried by no window. The face count bounds the number of drops, not this loss of
  coverage. Ties are resolved only by subsumption (b's disk contains w's, sigma_b contains sigma_w, the outer pieces
  inside b's: then L_b <= L_w pointwise and b's propagation covers w's rays) or by merging into a pending window (A6).
  The subsumption drop is certified: |c_w - c_b| + r_w <= r_b with the left side rounded upwards, and the containments
  sigma_b- <= sigma_w-, sigma_w+ <= sigma_b+, b.o0 <= piece start, piece end <= b.o1 as exact comparisons of doubles.
  In the A7 setting the prototype needed almost no tie drops (0 on every test mesh, 8 on the 317 x 317 terrain).
  Cost of withdrawing A8, measured on the 317 x 317 terrain (net10 Release, same prototype, tie slack 0 against
  1e-12): 1320225 instead of 1320199 windows (+26), 21.5 s instead of 20.5 s (within the run-to-run variation), and a
  much tighter enclosure (largest relative width 4.0e-8 instead of 2.5e-5, since the F Delta_max debt is gone). Drops
  allowed from now on: certified dominance (A4); subsumption, which needs all three of the certified disk containment
  |c_w - c_b| + r_w <= r_b (left side rounded upwards), sigma_b containing sigma_w, and w's outer pieces lying inside
  b's outer interval (exact comparisons); and A6 merging. An accepted A9 rule would be a **fourth** kind of drop: it is
  lemma-based (a VTP observation), not an instance of A4, and is used only after its own review.
- **A9 (VTP's pruning in interval form), a proof sketch, unreviewed.** Qin et al. 2016, Proposition A.1, prove eight observations (a)-(h): with
  windows w0, w1, sources p, q and pseudo-source distances sigma0, sigma1 in one plane, w1 is redundant if a strict
  inequality between two path lengths holds at one constructed point, eg (a): where the line pK meets the upper ray of
  w1 at K, if sigma0 + |pK| < sigma1 + |qK|. The observations are statements about real geometry. Restated: a rule
  may remove w1 (or the sub-window named) only if (1) every geometric precondition (that the line meets the ray, the
  order of points along an edge, a point lying on a segment's extension) is certified for all positions in the disks
  and all sigma in their intervals, by outward-rounded evaluation, and (2) the strict inequality holds between the upper
  enclosure of the left side and the lower enclosure of the right side. Then it holds for the true positions, and the
  real-valued observation applies to them. Conditions for soundness in our setting (review of #807, M2):
  *The dominating side must be realised geometry.* Its path length is enclosed with sigma0+ and over w0's whole disk
  (so it bounds the path from the witness s0* of A1), and every precondition about w0's rays (where a ray of w0 meets
  an edge, a line or another ray) is certified on w0's **inner** interval I(w0), not on O(w0): only rays through I(w0)
  are real surface paths. *The dominated side* is enclosed with sigma1- and over w1's whole disk. That is enough: the
  windows on a chain of A3 come from the final emission of their origin, so sigma1- <= d(o1) <= sigma1+ (A5), and a
  window of an earlier (stale) emission lies on no chain, so removing it is harmless. The removed set is w1's outer
  interval (or the named sub-window of it), which meets A4's whole-piece requirement because the observation concludes
  redundancy of the whole (sub-)window.
  *Template for each observation used* (one claim each, reviewed before use): the observation's statement; its
  geometric preconditions written as predicates over the disks, with w0's rays restricted to I(w0); the inequality as
  (left side)+ over w0's disk with sigma0+ < (right side)- over w1's disk with sigma1-; the removed set.
  *Observation (a), written out:* K is constructed on the upper ray of w1 (the ray from w1's source through w1's upper
  end); preconditions: for every q in w1's disk, K lies on that ray (beyond the edge, in the face where the observation
  is applied), and the segment from w0's source to K is a ray of w0 through a point of I(w0) for every p in w0's disk
  (the point where it crosses w0's edge lies in I(w0), certified), so p -> K is a real path for the witness; inequality:
  (sigma0 + |pK|)+ over w0's disk < (sigma1 + |qK|)- over w1's disk. Observations (b)-(h) each need their written
  interval form under this rule in their own reviewed claim before use; none is used before then.

### A.6 A7' (2026-11-19, for review): an exactly flat vertex needs neither emission nor continuation windows

Replaces the constructive form of A7 (A.5), which is sound but duplicates the ordinary windows (section 6b). The exact
flatness test and the proof that a shortest path through an exactly flat interior vertex p is straight in the unfolding of
p's fan are those of A.5 and are not repeated. What is new is the claim that the **ordinary propagation already
carries the straight continuation**, so p can be of kind 0 (no emission).

*Setting.* p is an interior manifold vertex accepted by the exact flatness test (angle sum exactly 2 pi). A window
W on an edge e of the face F = (a, b, p) with p in its outer image: the ray rho from a point s of W's disk through p
meets the closed edge e at x in O(W) (closed). Let rho' be its continuation beyond p, straight in the unfolding of p's fan
(by flatness the unfolding is single-valued; positions of s in the frame of any edge of any fan face follow from W's
disk by the rigid frame changes of A.2 along the walk, whichever way round p the walk goes). Let F_k be the fan face whose
closed sector at p contains rho' (two faces if rho' runs along an edge; take either), and e_k its edge opposite p.

*Invariant.* A *walk window* is a window W (before any drop of A4, subsumption or A6) on an edge f incident to p or
opposite p, with: origin o and sigma enclosing d(o); a disk that contains the unfolded position s* in f's frame, with s*
in the closed back half-plane of f (A.2, A.3); and a closed outer interval O(W) that contains t_p, the parameter of the
crossing of the ray from s* through p with f (for an edge incident to p: the end p itself, t_p = 0 or L), or, for
the window on the edge opposite p, the crossing of the continuation rho'. The claim: *for every shortest path gamma that
passes through exactly flat interior vertices, and every such vertex p on it, the arrival of gamma at p establishes the
invariant for a window on an edge of p's fan, the invariant is preserved around the fan, and it ends in a window on the
edge e_k opposite p that satisfies A3's invariant for gamma's continuation.* Induction along gamma's flat vertices (collinear
flat chains are everywhere on lattice planes, so a window of the walk of p can be the arrival window of the next
flat vertex q; the arrival types below cover that): A3's chain continues past every flat vertex without a pseudo-source,
with A3's triangle-inequality argument for the lower bound.

*Arrival types.* gamma reaches p (i) as the third vertex beyond the edge e of a window W of the chain (the ordinary case:
p in W's outer image, `Propagate`'s `xc`); (ii) along an edge (q, p), after a pseudo-source, the source or a flat vertex
q: q's emission or the source's windows are whole-edge windows on the edges opposite q, and the window on the edge
(q, p) of a face at q has the whole edge as outer interval (the D = 0 case of `Child`: the rays from a source on the
line of the edge are parallel to it), so t_p = L is in O(W); for a flat q reached by a walk the same holds by the
invariant at q; (iii) p as an edge end of a window's outer interval, ie the ray through p is the end ray of W's cone:
the child whose clip contains that end is created as in (a).

*Proof sketch.* (a) *First step, type (i).* Propagate computes xc, an enclosure of the crossing of the ray through p
with e; the true crossing t_p lies in xc and in O(W) = [O0, O1]. If xc.Hi > O0 the child on the edge (a, p) is created with
the clip [O0, min(O1, xc.Hi)], which contains t_p; otherwise xc.Hi <= O0 and t_p >= O0 force t_p = O0 = xc.Hi, and then
xc.Lo < O1 (W alive), so the child on (p, b) is created with the clip [max(O0, xc.Lo), O1], which contains t_p as its
end. Either way one child, on an edge (p, y_1) of F, has a clip of positive width that contains t_p. (b) *The child
is created, by strict widening.* For a fixed source position the crossing of the rays through the clip's points with
the line of the next edge is strictly monotone in the clip parameter wherever the denominator D of Mu keeps its sign, so a clip of
positive width gives a crossing set of positive width. `Child` computes an enclosure of it (the hull of Mu at the clip
ends, with D certified on the box and the clip, otherwise the whole edge): every interval operation rounds outward,
so the computed enclosure contains the true set and is at least as wide, and the disks never have radius zero, so
`u1 > u0` holds. The set contains the crossing 0 (or L) of the ray through p, as that ray meets the line of (p, y_1) at
its end p. The case D = 0 (the grazing ray of A.5) takes the whole edge. No special case for a collapsing enclosure
is needed or wanted: a window of one spacing would be a window that no argument supports. (c) *Around p.* The window
on (p, y_1) has p at one end of its outer interval (invariant), of positive width, and (a), (b) apply to its propagation
into the next fan face F_1 = (p, y_1, y_2): the child on (p, y_2) has a clip that contains the end of e_1 at p. The walk
goes on face by face, with the disk carried by A.2; the invariant holds at every step. (d) *End of the walk.* In the
unfolding of the fan the walk turns by the sector angles, and the straight continuation rho' lies in the closed sector
of F_k after a total of at most pi (either way round: the two sides of the branches are exactly pi each). In F_k the
window on (p, y_k) splits at the ray through the third vertex y_{k+1}; the child whose clip contains t_p on the side
of the true crossing is the child on the edge e_k opposite p (the one that sits beyond the ray through y_{k+1}'s side
of rho'), by the same case split as (a), and its outer interval contains the crossing of rho' with e_k, which is
the soundness of `Child` (an enclosure of all rays of the clip over all positions of the disk, A3). If rho' runs along an edge
(p, y_j), the walk ends on that edge: its window has the whole-edge outer interval by D = 0 (b) and carries the ray to
y_j by A3's vertex rule (`SetLower` of the third vertex, then the children at y_j). []

*Windows beyond the walk.* Where the walk's turning exceeds pi (a child created past F_k, theta > pi) the windows have
empty inner intervals (their rays are not rays of a real path) and only add terms to the Lower minimum: harmless, and
the same as the windows around a vertex today.

*Assumptions and open points for the reviewer.* (1) The claim needs the ray through p to be an end ray of the closed clips
of both children of a window; this is the strict-enclosure argument of (b) (every operation widens), not a measurement; the
probe of 6d dropped no child by `u1 <= u0` in any run, from 2 x 10^3 to 4 x 10^6 windows, consistent with it.
(2) A window of the walk is dropped only by certified A4 dominance or by subsumption. The point each protects: for a
walk window the point t_p, with L_w(t_p) <= d(p) (it needs sigma- <= d(o), A5's invariant); for W_k the crossing of
rho' with e_k. Subsumption is a same-origin rule: a subsuming window has the same origin, a disk containing w's, and its
outer interval contains w's piece, so it carries t_p as well and its lower function is at most w's on the piece. Windows
of other origins remove a piece only by certified A4 dominance, whose strict inequality at every point of the piece
includes t_p and the crossing of rho': the lower bound there stays at most d. (3) The exact flatness test is the one of
A.5 (reviewed); an angle sum of 2 pi + 1e-16 is **not** flat and stays a pseudo-source, so nothing is claimed for nearly flat
vertices. Flat boundary vertices stay pseudo-sources (S1: a boundary vertex is never kind 0), as does a flat vertex that is a source.

### A.7 The plain-double class (2026-11-19, second version after the independent review; for review)

The class of section 6e is the window propagation of A1-A6 without intervals: a window keeps its edge side, its outer parameter
interval [b0, b1] on the edge, the origin, sigma, and the unfolded position (sx, sy) of the origin in the frame of the edge, in
doubles; no inner intervals, no disks. Layout, 64 bytes: edge side 4, origin 4, b0 and b1 16, sx and sy 16, sigma 8, xi 8 (the
accumulated distance error of the window, after Surazhsky et al. 2005: xi' = max(xi_0, xi_1) + the error added by this step, and
at a merge the larger), e 8 (the error radius of the outer ends, below), exactly 64 (the origin's root source is in a table by origin
vertex; the sign bit of the side index is the pending, propagated, dead flag; the vertex test's per-vertex error xi_u is an array). The class is **sound in two halves**, which the first version of this note mixed up:

- **Coverage C:** for the shortest path gamma to a vertex v there is a window on gamma's chain whose outer interval contains gamma's
  crossing and whose function f_w(x) = sigma + |x - (sx, sy)| at the crossing is at most d(v) + xi_w.
- **Realisability R:** for every window w and every x in its outer interval, f_w(x) >= d(x) - xi_w, where d(x) is the polyhedral distance
  of the point x of the edge. `Distance[v]` is the minimum of f_w at v over the windows that reach v, so R is what keeps it from
  undercutting d, and C is what keeps it from overshooting. "Opening an interval only adds windows" is not enough here
  (the first version said it): an added piece on rays that do not exist can undercut d.

R survives a margin opening: f_w is 1-Lipschitz in x, and so is d along the edge, so an interval opened by m beyond a realised
piece has f_w(x) >= d(x) - xi_w - 2 m there; the opening adds 2 m to xi. Openings are therefore allowed only by a margin m
around realised rays, never to a whole edge on the rays' account, with the exceptions of the next paragraphs.

**The ends and the split (replaces the closure margin of the first version, B1).** The ends O0, O1 of a window come from the previous
step's `Mu` and frame change, and are rounded too; if O1 comes out short, the test "xc in [O0, O1]" fails and the ray through the
next flat vertex is lost (the gate-2 failure on the aligned ridge: 9.75e-3). Rules:
1. *Snapping.* The end of a child's clip that is the split ray (the ray from the origin through the third vertex p) maps to the
   vertex p exactly: the child on the edge (a, p) gets the end 0 or L assigned, not computed from `Mu`. Every ray through p meets
   the line of the next edge at p, so this is the true value and no error analysis is needed for it: A.6 (a)-(c) hold for the
   ray through p. The ends where the clip meets O0 or O1 are carried as computed.
2. *Margin on interior ends.* Each window carries e, an error radius of its ends (it matters only at O0 and O1: the code clamps
   a computed `Mu` beyond an edge end to that end). e_child = e_parent x g + the rounding of `Mu` + the rounding of the frame change.
   (a) g is the largest |dMu/dt| over the clip: Mu is monotone where its denominator D keeps its sign, so g is taken at the end where
   |D| is smaller, g = |det| / min(|D(t0)|, |D(t1)|)^2 scaled to the edge length; this is first order, so when e x |dD/dt| is comparable to min |D|
   the interval of D over [t - e, t + e] is evaluated, and if it reaches 0 rule 3 applies. (b) `Mu`'s own rounding near a small D:
   eps (|N| + |Mu| |D|) / |D| (N the numerator), a term of its own. (c) The frame-change rounding is c eps x the largest coordinate
   magnitude, not the extent: the mesh is recentred once on the centre of its bounding box (a translation, applied the same way to every
   input, so 2^k scalings stay bit-exact), and the extent is then the right scale; a mesh far from the origin (UTM) is
   thereby no worse than one at the origin, up to the rounding of the recentring (c eps x the original magnitude, once). The split test is
   xc in [O0 - e, O1 + e] and the children's ends are opened by e. The new window's xi grows by 2 e (the Lipschitz argument above).
3. *D uncertain.* If the sign of D is not certain at a clip end (within e of 0), that end maps to the edge end the rays escape toward
   (the vertex end on the side where D changes sign), not to the whole edge.
4. *The whole edge is allowed only* when the origin is a vertex of the face being entered (an emission: the rays from a vertex to the
   opposite edge are real) or for the ray along an edge from its end (A.6 (d): the edge itself is a path). If sy >= -xi (the origin on
   or near the line of the edge, behind or on it; the position error is xi, a distance, not e, a parameter error) the window is the
   along-the-edge case of rule 4, not a general one. **An along-the-edge window** has the value sigma + |s - q| + |x - q|, with q the
   end vertex it comes from (the ray along the edge is the path to q, then along the edge), and it inherits xi from the window that reached q;
   its only children are the far end vertex (continuing the walk if that vertex is flat, A.6 (d)) and never whole-edge windows in the adjacent faces.

The by-hand mutants: no snapping (kills: aligned ridge, L-shape), e = 0 (the same), D handled by the whole edge (kills: the sliver
family and the cone), delta = 0 (a window budget on the plane), no position check or a large delta (the mesh with one origin
unfolded at two places), the minimum of the pruning at the piece ends instead of the nearest point (a two-source plane), xi_b + xi_w
dropped from the pruning margin, the vertex test without xi_u, the along-the-edge window with whole-edge children (the collinear
chain ending at a convex bend).

**Fragmentation (measured, 2026-11-19; the oracle, A7', centre source).** Pairs of windows of one origin whose outer intervals touch
on one edge side, and why they stayed two windows (a probe in `Add`, not committed):

| mesh | pairs | partner already propagated | different position or sigma | both with an inner interval (the #1058 rule) | merged |
|---|---|---|---|---|---|
| plane 41 x 41 | 13785 | 999 (7 %) | 0 | 11792 (86 %) | 994 |
| plane 101 x 101 | 236161 | 7978 (3 %) | 0 | 222112 (94 %) | 6071 |
| ridge, 75 degrees, 41 x 41 | 27445 | 1784 (7 %) | 0 | 25346 (92 %) | 315 |
| ridge, slope 4, 101 x 101 | 452321 | 12058 (3 %) | 0 | 437740 (97 %) | 2523 |

The oracle's fragmentation is the inner-interval rule of 6c, not propagated partners and not positions (its position test is the
oracle's disk test, not the class's delta: the class's criterion is measured again with the code, below). The class has no inner
intervals, so the rule does not exist there. The gate-2 probe with merging measured 1.0 windows per **edge** on the plane
(6a: 8.6 to 1.0) but 10.0-14.4 per edge on the ridges (6a table; an edge has two sides, the oracle numbers above are per side).
So the merge of this note is not a proof that the ridge fragmentation disappears: the lever "merge with an already propagated
window" (3-7 % of the pairs) is **not needed for the targets** if the benchmark meets the time and memory targets of 6e; if it does
not, the lever is reopened with this table as its reference, the trigger being the time and memory targets, not a number of windows.

**Merging.** A new window w and a pending window b on one edge side with the same origin, intervals overlapping or touching,
|c_w - c_b| <= delta and |sigma_w - sigma_b| <= delta, merge: b's interval grows to the union, b keeps its own position and sigma (no
averaging, so on one side b never moves: no chain on a side). The merge is **charged** with the computed differences:
xi_b = max(xi_b, xi_w + |c_w - c_b| + |sigma_w - sigma_b|), and the end error e_b = max(e_b, e_w) + the shift of an end that a
position change of |c_w - c_b| causes (the similar-triangles factor of the ray between the two edges, evaluated at the ends of the
clip; its form is fixed with the code and tested). Pointwise |f_b(x) - f_w(x)| <= |c_w - c_b| + |sigma_w - sigma_b| <= 2 delta by the
triangle inequality. A merge is refused when the merged xi would exceed the tested tolerance (about 1e-9 d: a cap tied to the contract,
not a fixed 1e-10 extent, which a path of about 50 crossings would reach and so bring back the fragmentation of 6a); with no
cap the model's bound would be the only guard. After the union b's key is decreased in the indexed heap (one entry per pending
window: there are no stale entries), and the merge scan repeats while it merges, over the side's list in ascending window number
(a deterministic order for the cascades). **delta** is fixed by measurement in the code PR: the distribution of
|c_w - c_b| over the touching same-origin pairs at 317 x 317 on the plane, the ridge and the terrain (the oracle's disk radii reach 1.2e-12 d
by A.2, so duplicates of a far origin can be 1e-12 extent apart, and a delta of exactly 1e-12 extent may not catch them), and the
gap to the distinct images of one origin (unfolded around a vertex, a tube, a loop around a hole: at least an edge length apart). A
delta too large costs only accuracy (2 delta per merge, charged to xi); a delta too small costs windows.

**Error model (rationale of the tolerance form, not a bound).** A window on the chain carries xi = the sum of the rounding of
its frame changes (c eps extent each), of its merges (the computed differences, at most 2 delta each) and of its margin openings (2 e each); the accumulation of xi
(the max plus the step of Surazhsky, or the sum along the chain) agree to first order, and the code uses the form of the merge above. A path crosses K faces, so
|Distance - d| <= K (2 delta + c eps extent + 2 e) up to the last rounding. K is not bounded a priori, c is not derived; the class docs
quote the measured figures (6e), not this.

**Pruning.** For each overlap of a piece of w with the interval of b (the pieces: w's interval cut by b's), the piece is dropped if
(max over the piece of f_b) + xi_b + xi_w + 4 eps extent < (min over the piece of f_w): the maximum of f_b is at an end point and the
minimum of f_w at the point of the piece nearest to (sx, sy), because |x - c| + sigma is convex in x. If the test fails the piece is
bisected to depth 6 (as `Dominated` of A4) and the halves tested. With the margins xi_b + xi_w a drop happens only when
b's function, corrected by its error, is below w's, corrected by its error, over the whole piece: the chain through b covers what w
covered, to within the errors that C already charges. (A near-tie drop loses coverage only if the margins are smaller than the
errors; the A8 failure of A.5 was a tie slack subtracted from Lower, a different mechanism.) **The vertex test** of 6a (the
gate-2 baseline of 3.3 s had it) is part of the class: a piece of w is dropped if min f_w over it > U(u) + max |u - x| + xi_w + xi_u
+ 4 eps extent for u an end of the edge or the third vertex, with U(u) the current best `Distance` estimate of u; by R,
d(u) <= U(u) + xi_u, so a real path of that length exists up to the errors. No lemma-based rule (ICH, VTP) in v1.

**Implementation notes (2026-11-19, the code PR; where the code differs from the text above, the code is right).**
1. *The end error is an angle.* The recurrence e' = e x g of rule 2 grew exponentially on lattices (the end error reached 1e-7 extent on a
   25 x 25 plane: g > 1 at every hop, and a doubling cap did not help). The window carries an **angular** error E (radians) of its rays and
   the error of an end on its edge is E R^2 / h (R: the distance of the origin from the end point, h: its distance from the edge's line, floored
   at 16 eps extent). A hop adds the rounding, 16 eps + 4 cEps / R_p (R_p: the distance of the origin from the nearer clip end). Rays nearly along an
   edge are badly conditioned (h small) and get wide ends, which only widens clips. This is rule 2's content without its exponential.
2. *No recentring.* Every quantity the class uses is a difference of coordinates or a length derived from differences (accurate relative to
   itself), so the magnitude of the coordinates does not enter; the shifted copies of the similarity tests agree (rule 2 (c) is not needed).
3. *The clips are closed only at exactly flat vertices.* At every other vertex the plain clips (strict inequalities, no margin, the split end still
   mapped to the vertex exactly): the tiny windows at the vertex ends of the closed clips tripled the windows on lattices where every vertex is flat
   (4115 of 6580 windows on a 21 x 21 plane), and they are needed only for the ray through a flat vertex (A.6). `ExactMeshGeodesicDistance`
   got an internal `IsFlatVertex` for this (the exact flatness test, whatever the run's settings).
4. *The cap on xi at a merge is a safeguard, 1e-6 extent.* At 1e-9 extent the merging stopped after about 70 crossings on the plane (xi had grown
   to 2e-9 through the charges of the openings) and the windows per vertex grew from 7 to 20 and then without bound; the accuracy is what the tests
   enforce, the cap does not bound it.
5. *delta = 1e-12 extent, measured.* On the plane, the ridges and the terrain, every touching pair of one origin was either within delta (merged) or
   at least 3.9e-5 extent apart (the largest distance below 1e-6 extent was 0 on the planes and the ridges, 3.9e-5 on the 41 x 41 terrain: the
   windows of one origin at a different image).
6. *The vertex test is in; ICH and VTP are not.* The saddle 101 x 101 (0.3 (x^2 - y^2)) reaches the default of 64 windows per vertex and ends with
   `WindowLimit`: a mesh whose pseudo-sources are dense needs the stronger pruning of the lemma-based filters, which v1 leaves out (the oracle
   needs 120 windows per vertex there too). The target families (terrain, plane, the ridges) stay below 40 per vertex (the benchmark numbers in
   `benchmarks/GeodesicOracleBenchmark/README.md`).

7. *The dominance recursion stops early (the performance fix of the code PR).* `Dominated` recursed to depth 6 into every piece (127 calls
   per piece and per overlapping window, 824 million calls on a 201 x 201 plane). A piece [l, r] is now left when
   `FMax(a, l, r) <= FMin(b, l, r) + m`: a sub-piece that passes the test needs `FMin(a, sub) > FMax(b, sub) + m`, and `FMin(a, sub) <= FMax(a, l, r)`
   and `FMax(b, sub) >= FMin(b, l, r)` because `Hyp(x - sx, sy)` is monotone in |x - sx| in floating point (every step, the subtraction, the
   squares, the sum and the square root, is monotone), so no sub-piece can pass. The pieces are the same bit for bit (checked by the benchmark's digest over
   all five families at 101, 201 and 317 vertices a side, and by the pins of the window and merge counts). Plane 317 x 317: 24.5 s to 6.1 s; the
   terrain is unchanged at 3.3 s. A second finding of the profile: a debug log of the first version grew a string by concatenation (6 GB allocated
   at 317 x 317); it is gone.
8. *Hand mutants (outcome).* Each mutant of the list above is run against the PR tests. Killed: no snapping of both ends, e = 0, delta = 0
   (the window budget), no position check and a huge delta (the jittered plane), no sigma check, no xi cap, the minimum of the pruning at the
   piece ends, xi_w and xi_b dropped from the dominance margin (each alone and both), from the cut of b's end, the vertex test without xi_u or
   without xi_w, the along-the-edge window as the whole edge, all flat flags false, the sign of either margin flipped. Most of these change the
   window count, not the distances beyond about 1e-15 (the margins are a few cEps), so the kill is the **pinned window and merge counts** of
   `Pruning_WindowAndMergeCounts_ArePinned` (thin slivers, rotated and jittered grids put near ties on both sides of a margin). Two
   survive: (a) snapping of the clip's upper end only (`snap1`): it differs only on a strip of height 1e-8 with five rows (a mesh on which the class leaves
   vertices unreached, item 10), where no result is to be pinned; (b) the escape direction when D is uncertain always `+MaxValue`: the branch
   (a clip end ray parallel to the child edge within the rounding of D) was **not reached by any input of 129 test and benchmark meshes, 3000
   lattice meshes with random diagonals, shears and scalings, and 6000 random integer strips**, in which exact parallelism is common, so the
   mutant is invisible; the branch is the defensive fallback of rule 3 and stays.
9. *Accuracy (measured).* Against the oracle's enclosure the deviation is exactly 0 (the result lies inside [Lower, Upper]) on 12 families at 31 and 41
   vertices a side (and the four 10^4 comparisons of the nightly tier pass at the same tolerance); against closed forms and the straight line the
   largest deviation is 2.4e-15 relative. The tests enforce 5e-15 d + 5e-16 extent (twice the measurement, was 1e-9 and 1e-12, unmeasured), and the class docs quote
   exactly these.
10. *Known limit: thin slivers.* A strip of height 1e-6 (and 1e-7 .. 1e-8) with five rows of 21 vertices leaves 2-7 of 105 vertices unreached with
    status Ok (aspect ratios of 10^5 to 10^7; heights 1e-9 to 1e-12 and three rows are fine). The class docs state it. A fix needs a decision (a new status
    value, or a fall back to the oracle's intervals), which is the owner's.

*Open points for the reviewer.* (1) Snapping (rule 1) is exact for rays through p; e matters only at the interior ends O0, O1 (with
`Mu` clamped to the edge ends); is rule 2's e (second-order D test, the rounding term of `Mu`, recentring) enough for rays near, but not
through, a flat vertex, which A.6 does not need but the tolerance does? (2) R for the exceptions of rule 4: the along-the-edge window and
the emission are realised; is the case "origin on the line of the edge, beyond the end" covered by sy >= -xi and the realisability of
the path to the end vertex? (3) The cap on xi at a merge, set against the tested tolerance, and the dependence of delta on the
measured distribution.
