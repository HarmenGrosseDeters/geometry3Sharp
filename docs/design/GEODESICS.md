# Geodesic distances with accuracy bounds (study)

Status: study, docs only (2026-11-13). No library code. Decisions for the owner at the end.

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

Citations marked (m) are recalled from memory, not re-read for this note; they must be checked against the papers
before the first code PR, as SOURCES.md requires.

### 3.1 Exact polyhedral distance

- J. S. B. Mitchell, D. M. Mount, C. H. Papadimitriou, *The discrete geodesic problem*, SIAM J. Comput. 16(4),
  647-668, 1987 (m). "Continuous Dijkstra": **windows** on edges (an interval of the edge, the unfolded position of a
  pseudo-source in the edge's plane and its distance offset), propagated across triangles in order of distance;
  overlapping windows trimmed to the part where each is closer; vertices with angle sum above 2 pi (saddles) and
  boundary vertices become pseudo-sources. O(n^2 log n) time, O(n^2) space worst case.
- J. Chen, Y. Han, *Shortest paths on a polyhedron*, SoCG 1990; Int. J. Comput. Geom. Appl. 6(2), 127-144, 1996 (m).
  A tree of windows, "one angle one split": at most one child per window survives per vertex; O(n^2) time, O(n) space.
- V. Surazhsky, T. Surazhsky, D. Kirsanov, S. J. Gortler, H. Hoppe, *Fast exact and approximate geodesics on meshes*,
  ACM TOG 24(3) (SIGGRAPH 2005), 553-560 (m). The practical MMP: window trimming by solving a quadratic for the
  intersection point of two windows' distance functions, a priority queue by the window's minimum distance; measured
  about O(n^1.5) windows and run time. An **approximate** variant merges adjacent windows when the merged window
  changes the distance by less than a threshold; the paper reports small errors and large speedups. Whether the
  per-merge threshold gives a proven bound on the final distance (merges accumulate along a path) is to be checked in
  the paper (m).
- S.-Q. Xin, G.-J. Wang, *Improving Chen and Han's algorithm on the discrete geodesic problem*, ACM TOG 28(4), 104,
  2009 (m). ICH: Chen-Han with a priority queue and a filter that discards a window if a vertex already has a smaller
  distance to the window's region; much faster than CH in practice, O(n^2 log n) worst case, O(n) space. CGAL's
  `Surface_mesh_shortest_path` is based on it (m; GPL, approach cited only).
- Y. Qin, X. Han, H. Fang, S.-Q. Xin, Y. He, *Fast and exact discrete geodesic computation based on triangle-oriented
  wavefront propagation*, ACM TOG 35(4) (SIGGRAPH 2016), 125 (m). VTP: windows propagated triangle by triangle with
  pruning by the vertex distances of the triangle (a window that cannot beat the current vertex distances plus the edge
  lengths is dropped); reported several times faster than MMP and ICH and lighter in memory.
- P. Trettner, D. Bommes, L. Kobbelt, *Geodesic distance computation via virtual source propagation*, Computer Graphics
  Forum 40(5) (SGP 2021) (m). VSP: propagates virtual sources rather than windows; reported faster than VTP on large
  meshes. Newer, fewer independent implementations.
- Theory: S. Kapoor, STOC 1999, claims O(n log^2 n) (the proof is considered incomplete); Y. Schreiber, M. Sharir,
  *An optimal-time algorithm for shortest paths on a convex polytope in three dimensions*, DCG 39, 2008, O(n log n)
  for convex polytopes (m). Neither is practical.
- Implementations (approach only, source not read): D. Kirsanov's `geodesic` (MMP after Surazhsky et al.; licence to be
  checked before anyone reads it), wrapped by libigl (MPL, paper only) and pygeodesic; geometry-central (MIT) has a
  polyhedral exact geodesics module (m) and uses the heat method by default; CGAL (GPL, never read).

### 3.2 Approximations with a bound

- R. Kimmel, J. A. Sethian, *Computing geodesic paths on manifolds*, PNAS 95(15), 8431-8435, 1998 (m). Fast marching
  on triangulated domains, O(n log n); first order; **obtuse triangles** break the causality of the update and are
  handled by unfolding a virtual edge into a neighbouring triangle. Convergence to the viscosity solution, no explicit
  error constant for a given mesh. Already cited in SOURCES.md for the terrain option R2.
- J. A. Sethian, A. Vladimirsky, *Ordered upwind methods for static Hamilton-Jacobi equations*, SIAM J. Numer. Anal.
  41(1), 2003 (m): the anisotropic generalisation; first order, monotone.
- L. Aleksandrov, M. Lanthier, A. Maheshwari, J.-R. Sack, *An epsilon-approximation algorithm for weighted shortest
  paths on polyhedral surfaces*, SWAT 1998; L. Aleksandrov, A. Maheshwari, J.-R. Sack, *Determining approximate shortest
  paths on weighted polyhedral surfaces*, J. ACM 52(1), 25-53, 2005 (m). Steiner points on the edges in a
  **geometric progression** away from each vertex (ratio set by epsilon and the smallest angle at the vertex), a graph
  between the points of each triangle, Dijkstra: a **proven (1 + epsilon)** approximation with
  O(n / sqrt(epsilon) log(1/epsilon)) Steiner points (constants depend on the mesh's angles). M. Lanthier, A.
  Maheshwari, J.-R. Sack, *Approximating shortest paths on weighted polyhedral surfaces*, Algorithmica 30, 2001 (m):
  uniform points, an additive bound. Survey: P. Bose, A. Maheshwari, C. Shu, S. Wuhrer, *A survey of geodesic paths on
  3D surfaces*, CGTA 44(9), 2011 (m).
- S.-Q. Xin, X. Ying, Y. He, *Constant-time all-pairs geodesic distance query on triangle meshes*, I3D 2012, and X. Ying,
  X. Wang, Y. He, *Saddle vertex graph (SVG)*, ACM TOG 32(6), 2013 (m): precomputed graphs for fast repeated queries;
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
  arXiv:2007.10430, 2020 (m): compares MMP, ICH, VTP, FMM, heat, SVG on common meshes; VTP among the fastest exact ones.

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
  S. Johnson, *Some NP-complete geometric problems*, STOC 1976 (m)). Expression trees with separation bounds (C.
  Burnikel, S. Funke, K. Mehlhorn, S. Schirra, S. Schmitt, *A separation bound for real algebraic expressions*,
  Algorithmica 2009 (m); the approach of LEDA / CORE, paper only) would decide every comparison exactly, at a cost that
  grows with the expression depth, ie with the path length in triangles. Not practical for a distance field.

How others solve it: geometry-central / potpourri3d and libigl offer the heat method as the default and an MMP
implementation as the exact option (m); CGAL offers ICH (m); none of them, to my knowledge, certifies the rounding of the
exact method (m).

## 4. What can be proven with our arithmetic

### 4.1 A certified enclosure of the polyhedral distance (window propagation in interval arithmetic)

The usable form of "exact" is an **enclosure**: for each vertex an interval [Lower, Upper] of doubles that provably
contains the polyhedral distance, computed by window propagation (MMP / VTP) in `ExactInterval` arithmetic (extended by
`/` and `sqrt`, both correctly rounded in IEEE 754, so the existing one-ulp widening covers them; no `Math` functions
other than `Sqrt` are needed, so no libm dependence). The argument (own, to be written out and reviewed in the design
step of the code PR):

1. Every window stands for a set of real surface paths: a straight segment in the unfolding of a triangle strip from a
   pseudo-source, plus the pseudo-source's distance. Keep two bounds per window: an **inner** interval of the edge
   (certainly visible through the strip) and an **outer** one (contains the exact window).
2. Upper: for a point x in a window's inner interval the path exists, so `Hi(|x - s| + sigma)` is an upper bound of the
   distance. The minimum over windows of these bounds is an upper bound.
3. Lower: the true shortest path to x crosses the edge in some window of the exact algorithm. If windows are trimmed or
   dropped **only where the dominance is certain** (the other window's upper bound is below this window's lower bound
   on the whole part removed, and the other window is inner-valid there), the window carrying the shortest path (or one
   dominating it) survives, and `Lo(|x - s| + sigma)` over its outer interval is at most the distance. The minimum of
   the lower bounds over the windows is a lower bound. Extra windows that rounding forced us to keep, and outer
   intervals that reach slightly beyond the exact window, can only lower that minimum, so it stays a lower bound; they
   cost time and loosen the bound by about the overshoot (a straight line in the unfolding is never longer than the
   real path through the window's end point).
4. Width: the enclosure widens by a few ulps per operation along each unfolding chain, and more where the unfolding is
   ill conditioned (needles). That is measured, not bounded a priori; a `MaxWidth` check with a status reports a
   vertex whose enclosure is wider than a tolerance relative to the distance. Upper equals Lower up to the width, so for
   all practical purposes it is the exact distance, with a proof that says how close.

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
stop rule). Memory: windows per edge grow like n^0.5 in practice (m); at 10^5 vertices a few 10^7 windows would be
too much, so the first step measures the window count on procedural terrain and the 169-degree plane.

### 4.2 Proven two-sided bounds without the exact method

- **Lower bound by Lipschitz check (own, simple).** A piecewise linear function phi on the mesh with phi <= 0 at the
  sources and |grad phi| <= 1 in every triangle (in the triangle's own flat metric) is at most the polyhedral distance
  everywhere: along any surface path phi changes by at most the path length. So any field (the heat method's, scaled)
  is turned into a certified lower bound by `phi / max(1, max_T |grad phi|_T)`, the gradient bound evaluated in
  interval arithmetic. Cheap (one pass), but loose where one triangle has a large gradient (near sources, cut loci);
  measured.
- **Upper bound by real paths:** the Steiner-point Dijkstra that `TerrainSafety` already has, or FlipOut's length for a
  pair. Proven, a-priori loose.
- **(1 + epsilon) by Aleksandrov-Maheshwari-Sack:** with the paper's geometric Steiner point placement, the graph
  distance D satisfies d <= D <= (1 + epsilon) d, so [D / (1 + epsilon), D] is a proven enclosure, if the placement
  follows the paper exactly (the constants depend on the smallest angle per vertex; needles force many points). Cost
  O(n / sqrt(epsilon) log(1/epsilon)) graph nodes and about (points per edge)^2 graph edges per triangle; for epsilon =
  1% that is tens of points per edge. Deterministic (Dijkstra with a total order on ties), 2^k-invariant like 4.1.

### 4.3 What stays measured only

- The heat method: no a-priori bound. It can carry the a-posteriori Lipschitz lower bound of 4.2, and against an oracle
  of 4.1 its error becomes measurable on any mesh, not only on analytic ones.
- Fast marching: a first-order monotone scheme; convergence is proven, a bound for a given mesh is not (m).
- The approximate MMP of Surazhsky et al.: a per-merge error threshold; a global bound to be checked in the paper.

## 5. Options

| # | Option | Gives | Proven | Cost | Tier |
|---|---|---|---|---|---|
| A | **Exact window propagation with a certified enclosure** (MMP after Surazhsky et al. 2005 with the VTP / Xin-Wang vertex filter, in `ExactInterval`), new class, vertex sources first | [Lower, Upper] per vertex, practically exact; an oracle for every other method; `TerrainSafety.AlongSurface` could become guaranteed | yes (enclosure argument 4.1, own, to be reviewed) | ~1500-2500 lines with tests; risk in the window bookkeeping (saddles, boundaries, degenerate windows) | L |
| B | **(1 + epsilon) Steiner graph** (Aleksandrov-Maheshwari-Sack 2005), generalising the `TerrainSafety` Steiner Dijkstra | enclosure [D / (1+eps), D] | yes (the paper's theorem, if the placement is faithful) | ~600-900 lines; many graph nodes for small eps or small angles | M |
| C | **Heat method plus certificates**: the Lipschitz lower bound of 4.2 as an optional output of `MeshGeodesicDistance`; the Steiner or FlipOut upper bound | a proven lower bound, a measured accuracy | lower bound yes, accuracy no | ~200-300 lines | S |
| D | Fast marching (Kimmel-Sethian) | a first-order field | no | ~600 lines; obtuse unfolding; dominated by the heat method we have (similar accuracy, no factorization reuse, intrinsic Delaunay already fixes obtuse cases for heat) | M |
| E | Exact with expression trees / separation bounds | truly exact comparisons | yes | cost grows with path length; not practical (3.4) | L+ |

Not recommended: D (no bound, no gain over the existing heat method), E (cost), and Surazhsky's approximate merge as a
"bounded" method until its bound is checked.

## 6. Recommendation

**Option A**, staged in one feature PR (clean commits), after the owner approves this note:

1. **Probe and stop rule first** (part of the PR's first commit, not a separate PR): a plain-double window propagation
   on procedural meshes (169-degree plane, ring sphere, bumpy terrain 10^4 and 10^5 vertices), measuring windows per
   edge, peak memory and time. Stop rule: if the 10^5-vertex terrain needs more than 10 s in doubles or more than 2 GB,
   stop and fall back to option B (report numbers to the coordinator).
2. The interval version with the enclosure (4.1), `Status` (`Ok`, `NonManifoldInput`, `DegenerateTriangles`,
   `EnclosureTooWide`, `WindowLimit`), `MaxWindows`, vertex sources; results `Lower`, `Upper` (and `Distance` as their
   midpoint for convenience). Serial only; no parallel loop. Exact-code rules (no FMA, no `float`) apply if the file is
   added to the exact list in `.editorconfig`; it should be, since its proof depends on them.
3. Use it as the **oracle** in the heat, FlipOut and vector heat tests on non-analytic meshes, and benchmark the heat
   method's true error in `IntrinsicBenchmark`.
4. Later, separate decision: `TerrainSafety.AlongSurface` on the enclosure (Upper for the safe side), which would close
   the documented "not guaranteed" gap.

Option C is cheap and independent; it can come any time as an S task. Option B is the fallback if the stop rule fires,
and the better choice if many sources and huge meshes matter more than tightness.

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
- **Sphere** (inscribed convex mesh, vertices on the sphere): the nearest-point projection onto the convex polyhedron is
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
  not made a pseudo-source (the L-domain), the tie order changed (the reuse and determinism tests).
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

## 9. Decisions for the owner

1. Approve **option A** (exact window propagation with a certified [Lower, Upper] enclosure) as the next geodesics item,
   tier L, with the stop rule of section 6 (fallback: option B).
2. Public API or test oracle first? Recommendation: public class (it answers a real gap, TerrainSafety AlongSurface),
   marked experimental in its doc comment until the 10^5-vertex numbers are in.
3. Put the new file under the exact-code rules (`.editorconfig` list, analyzers G3E0001-3, mutation shard)?
   Recommendation: yes; its proof depends on them.
4. Degenerate triangles: fail with a status, or compute on the mollified intrinsic metric? Recommendation: status first.
5. Option C (Lipschitz lower bound for the heat method) as a separate S task: yes or later.
6. Confirm that fast marching (option D) is not pursued.
