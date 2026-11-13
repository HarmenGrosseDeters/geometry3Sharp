# Geodesic distances with accuracy bounds (study)

Status: study, docs only (2026-11-13). No library code. Owner decisions recorded in section 9; the dominance-trimming
argument of option A is in [Appendix A](#appendix-a-the-enclosure-argument-for-review), revised after its review; the
citation check is in section 3.5; the measurement that led to dropping option C is in section 4.2; the stop-rule
measurement of option A is in section 6; gates 2 and 3, the role of the oracle and the claims still to be reviewed
(A7, the withdrawn A8, A9) are in section 6a and Appendix A.5 (2026-11-13, second round).

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
A1-A6, subsumption and the terminal windows (M1); flat vertices are pseudo-sources (S1 / A5); **A7 is not shipped**.
`ExactInterval` got division and square root (correctly rounded in IEEE 754 on every target; tests at the subnormal and
overflow edges). Corner angles for the pseudo-source test use `CoreMath.Atan2` (documented as correctly rounded, at most
half a spacing; widened by two spacings). The direction error of A.2 uses the bound 2 asin z <= 2 z / sqrt(1 - z^2)
instead of `Math.Asin`, so no libm function enters and the bits are the same on every platform
(`tools/DeterminismHarness` case `geodesic.exact`). On a window limit or a too-wide position the result arrays are
NaN.

**Why A7 is not shipped (an open item: A7 with duplicate control).** The constructive continuation windows of A7 are
sound (reviewed), but on exactly flat regions they duplicate the ordinary windows: the ordinary windows already carry
the rays through flat vertices, and the continuation windows carry the same origin with the same true position, so their
distance functions are equal. Neither A6 (it needs a pending partner) nor certified dominance (equal functions) removes
such duplicates, and a per-vertex dedup of the continuations (a canonical frame per vertex and subsumption of their
disks) does not catch the duplicates of the ordinary windows. Measured: a 5 x 5 plane reached the limit of 100000
windows with 112616 continuation windows. A7 needs a duplicate-control design (for example subsumption of a continuation
by the ordinary windows on its edge side, or emitting continuations only where no ordinary window can carry the ray)
before it is built.

**Measured reach** (net10 Release, this container; S1 setting; 0 enclosure violations against the plain-double
reference that emits from every flat vertex):

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

So the oracle reaches about 10^4 vertices on terrain-like input, but only about 10^3 on exactly flat or exactly aligned
input: **short of the 10^4-vertex target of the plan** on those. Beyond, `Compute` returns `WindowLimit` with NaN arrays
(the clean way out; a test pins it on the aligned 41 x 41 ridge). The default `MaxWindows` is 300000, which keeps a
`Compute` below about 20 s (the plane 101 x 101 took 116 s to reach the former limit of 2 million).
**Looseness:** Upper is up to about 1.3% above the distance on flat regions (inner intervals rarely contain a vertex
certainly; the edge paths then bound it); Lower is within about 1e-8 relative (the re-emission margins of A5 add up
along chains of flat pseudo-sources). Tests assert the enclosure and a width bound per mesh, never equality.

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

