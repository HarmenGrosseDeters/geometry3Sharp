# Sources: Geodesics, Intrinsic Triangulations, UV & Solvers

Back to [Sources Index](../SOURCES.md) · [sources/README.md](README.md)

---

## Certified exact geodesic distance (`ExactMeshGeodesicDistance`, internal test oracle)

Implemented from the papers and the own claims of [docs/design/GEODESICS.md](../design/GEODESICS.md) (Appendix A); no code
taken over (Kirsanov's `geodesic`, geometry-central, CGAL were not read).

| Part | Source |
|---|---|
| Window propagation ("continuous Dijkstra"): windows on edges with an unfolded pseudo-source and its distance, propagation across faces, saddle and boundary vertices as pseudo-sources | J. S. B. Mitchell, D. M. Mount, C. H. Papadimitriou, *The discrete geodesic problem*, SIAM J. Comput. 16(4), 647-668, 1987 (not read: the host returned 404; the statements as cited by Surazhsky et al.) |
| The practical form: windows on an edge cut against each other, a priority queue by window distance, which vertices a shortest path can pass through (boundary, saddle, parabolic) | V. Surazhsky, T. Surazhsky, D. Kirsanov, S. J. Gortler, H. Hoppe, *Fast exact and approximate geodesics on meshes*, ACM TOG 24(3), 553-560, 2005 (read 2026-11-13) |
| Certified enclosure: outer and inner intervals, one-sided pruning certified over whole pieces, terminal windows, disks under rigid frame changes, subsumption, merging, re-emission with a margin | own (GEODESICS.md Appendix A, claims A1-A6, reviewed 2026-11-13) |
| Interval arithmetic with one-spacing widening (division and square root added) | R. E. Moore, *Interval Analysis*, Prentice-Hall 1966; IEEE 754 correct rounding of +, -, *, / and sqrt |
| Corner angles by the correctly rounded atan2 | CORE-MATH (CoreMathSharp port), see "Correctly rounded sin, cos, tan, atan2, acos, exp, log" below |

## Polyhedral geodesic distance in plain doubles (`PolyhedralGeodesicDistance`, public, uncertified)

Implemented from the papers and the own claims of [docs/design/GEODESICS.md](../design/GEODESICS.md) (section 6e, Appendix A.6 and
A.7); no code taken over. No ICH (Xin and Wang) and no VTP filter is used; the certified oracle is the test reference.

| Part | Source |
|---|---|
| Window propagation, pseudo-sources, the priority queue by window distance | J. S. B. Mitchell, D. M. Mount, C. H. Papadimitriou, *The discrete geodesic problem*, SIAM J. Comput. 16(4), 1987; V. Surazhsky, T. Surazhsky, D. Kirsanov, S. J. Gortler, H. Hoppe, *Fast exact and approximate geodesics on meshes*, ACM TOG 24(3), 2005 (as for the oracle above) |
| The accumulated error of a window and merging of windows of one source within a tolerance | Surazhsky et al. 2005 (approximate variant), restated in own terms (A.7) |
| Exactly flat vertices emit nothing; the ray through them is carried by the closed clips; the straight-fold and coplanar-fan test | own (A.5, A.6; the test is the oracle's `IsExactlyFlat`) |
| Realisability and coverage of the windows, the margins, snapping of a clip end to a vertex, the pruning with errors | own (A.7) |
| Exact shortest paths by backtracing (`TracePath`): the straight segment from a vertex or a point to the unfolded origin image of its window, unfolded back across the triangles, continuing at a pseudo-source with that vertex's own record; a point in a triangle from the windows of its three edge sides and its corners (A.8) | MMP 1987 (as cited by Surazhsky et al.) and Surazhsky et al. 2005 (the backtracing of a path through the windows; read 2026-11-13); the record of the winning window per vertex instead of parent links, the rule that a leg ends at any vertex it passes, the face-point candidates and the tolerance are own (A.8). No code taken over; Xin and Wang 2009 not needed |
| Trimming of the windows of an edge against each other where one is worse than the other (the window functions |x - s| + sigma cross at points found from the two origins, a window is cut where it is the worse one): the exact trim replaces a depth-limited bisection (A.7, "Exact trimming"; issue #1186) | MMP 1987 (as cited by Surazhsky et al.) and Surazhsky et al. 2005 (above) describe the trim of overlapping windows by the crossing of their distance functions; the argument that the difference has one critical point (the collinearity of x and the two origins, the triangle inequality) and the margins are own (A.7). ICH (Xin, Wang, *Improving Chen and Han's algorithm on the discrete geodesics problem*, ACM TOG 28(4), 2009) and VTP (Qin et al., ACM TOG 35(4), 2016) were not read and not used: A.7 records the measurement that on the saddle family nearly every window alive at the end is the minimum somewhere on its edge, so a filter that removes only dominated windows cannot reduce the final count |

## Intrinsic triangulations and geodesics (`IntrinsicTriangulation`, `VectorHeatMethod`, `MeshGeodesicDistance`)

Implemented from the papers, no code taken over (geometry-central, MIT, was not used as a source).

| Part | Source |
|---|---|
| Intrinsic triangulations as edge lengths, signposts (direction and length of each halfedge at its tail), flips and their signpost update, tracing intrinsic edges over the input mesh | N. Sharp, Y. Soliman, K. Crane, *Navigating Intrinsic Triangulations*, ACM TOG (SIGGRAPH) 2019 |
| Vertex insertion in faces and on edges with the signpost update, locating inserted points by tracing, intrinsic Delaunay refinement by circumcenter insertion, common subdivision by tracing the intrinsic edges | N. Sharp, Y. Soliman, K. Crane, *Navigating Intrinsic Triangulations*, ACM TOG (SIGGRAPH) 2019 |
| Delaunay refinement: circumcenters of bad triangles, encroached boundary segments split at their midpoints instead | L. P. Chew, *Guaranteed-Quality Mesh Generation for Curved Surfaces*, ACM Symposium on Computational Geometry 1993; J. Ruppert, *A Delaunay Refinement Algorithm for Quality 2-Dimensional Mesh Generation*, Journal of Algorithms 1995 |
| Small input angles: concentric shells (splits at powers of two from the corner), not refining triangles whose small angle comes from the input; a segment is encroached iff the opposite vertex of its Delaunay triangle is | J. R. Shewchuk, *Delaunay Refinement Algorithms for Triangular Mesh Generation*, Computational Geometry: Theory and Applications 2002 |
| Diametral lenses without `Conforming`, up to the angle bound arcsin(√3/4) = 25.65° proven for them (`QualityMesh2.Refiner.InLens`), with the paper's deletion of the free vertices in a subsegment's diametral circle that see its midpoint before it is bisected (`QualityMesh2.Refiner.DeleteFreeInCircle`, Chew 1993; the kernel's `DeleteVertex`); termination measured, not proven (off-centres, concentric shells and the snap grid differ from the analysed algorithm) | J. R. Shewchuk 2002 (above), section 6.4; read from the draft of May 21, 2001 |
| Oracle (`Meshing2Benchmark`): a triangle's small angle forced by an input angle phi excuses it only when its other angles are at least arctan(sin phi / (2 − cos phi)) (`ForcedByInputAngle`, `AtLeastShewchukBound`; the name is historical) | G. L. Miller, S. E. Pav, N. J. Walkington, *When and why Ruppert's algorithm works*, IJCGA 2005, as stated in its abstract (not yet checked against the full text); J. R. Shewchuk 2002 (above) proves the weaker arcsin(sin(phi/2)/√2) for the Terminator (Theorem 12, section 6.3), which the oracle does not use (corrected in #396) |
| Small-angle rule in the constrained-quality mode (`QualityMesh2.Refiner.ApexExempt`, `Conforming = false`): a subsegment encroached by a refinement vertex on exactly one source c is not split when its source and c meet exactly (`MeetKind`) at an input angle below the bound, inside the apex region (a shell subsegment ending on c, or radii from the apex within 1.5); input, crossing and multi-source vertices still encroach. A geometric approximation of the insertion-radius cure and its exceptions; termination measured, not proven (`docs/design/MESHING2D_SMALL_ANGLES.md`) | J. R. Shewchuk 2002 (above), section 6.2 (the "precise rule" and its exception for a parent that is an input vertex or lies on a nonincident segment), checked against the published text (printed pp. 29-36) |
| Oracle (`Meshing2Benchmark`, rule O-H5 (ii')): the rounding of the input alone includes the restore step of snap rounding with restore, recomputed from the paper (`RestorePixels`: rounds of routing and critical pixels, ChoosePixel's rules R1 and R3 and a widened search, all choices of a round made on one arrangement); written from the paper and MESHING2D_SNAP.md section 3b, not from `SnapRounding2`, and compared with it in tests | A. Belussi, S. Migliorini, M. Negri, G. Pelagatti, *Snap rounding with restore: an algorithm for producing robust geometric datasets*, ACM TSAS 2(1), 2016 (Table II, Proposition 4.17); J. D. Hobby 1999 (above) |
| Off-centres: a triangle below the angle bound is split on the perpendicular bisector of its shortest edge pq, where the triangle of pq and the new point has the bound at the point (|pq| cot(theta/2) / 2 from the midpoint), or at its circumcentre if that is closer (`QualityMesh2.Refiner.OffCentre`) | A. Üngör, *Off-centers: A New Type of Steiner Points for Computing Size-Optimal Quality-Guaranteed Delaunay Triangulations*, LATIN 2004 |
| Grading in `QualityMesh2` with an angle bound: the split point of a triangle bad by its angle chosen among candidates in the petal of its shortest edge (the circumcentres of nearby triangles inside it and seven points of its arc, `QualityMesh2.Refiner.PetalCandidates`; step 1's points on the bisector behind a switch) by the bad triangles it removes and creates, never with a smaller insertion radius than the off-centre or circumcentre (`QualityMesh2.Refiner.BestCandidate`); bad triangles split worst first (`TriangleQueue`, own) | H. Erten, A. Üngör, *Triangulations with Locally Optimal Steiner Points*, Eurographics Symposium on Geometry Processing 2007 (read 2026-11-04; the journal version, SIAM J. Sci. Comput. 2009, not read): the idea of choosing among points and the petal (Algorithm 1); the arc's rational parametrisation, the score and the floor own (`docs/design/MESHING2D_GRADING.md` 4.3) |
| The Meshing2 oracle's fast path (test tool, `benchmarks/Meshing2Benchmark/Oracle`): a checker independent of and simpler than the mesher, its indexes and filters each with an equivalence argument and a check against the full scan (`docs/design/MESHING2D_FAST_ORACLE.md`). Source coverage reuses the existing tube boxes and BoxGrid, with a bounded query and exact scan fallback; diagnostics and full-report controls are in `benchmarks/OracleSourcePilot` | R. M. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 2011 (the approach; the checks are own work) |
| Integer coordinates for a robust common subdivision: normal coordinates and roundabouts, their update on an edge flip (eqs. 3, 4) and on the split of an edge on an input edge (eq. 6, used for boundary edges), tracing the input edges by integer comparisons (algorithm 1), positions by unfolding. `IntrinsicTriangulation.EnableIntegerCoordinates` (`mesh_ops/IntrinsicIntegerCoordinates.cs`), implemented from the paper's text (arXiv:2106.00220v1), no code read; insertion into faces is not implemented in the library (study prototype in `benchmarks/IntrinsicIntegerCoordinates`, see `docs/research/INTRINSIC_INTEGER_COORDINATES_2026-11.md`) | M. Gillespie, N. Sharp, K. Crane, *Integer Coordinates for Intrinsic Geometry Processing*, ACM TOG (SIGGRAPH Asia) 2021 |
| Intrinsic Delaunay triangulations, non-negative cotan weights, flip algorithm | A. I. Bobenko, B. A. Springborn, *A Discrete Laplace-Beltrami Operator for Simplicial Surfaces*, Discrete & Computational Geometry 2007; C. Indermitte, T. M. Liebling, M. Troyanov, H. Clémençon, *Voronoi diagrams on piecewise flat surfaces and an application to biological growth*, Theoretical Computer Science 2001 (termination of the flips) |
| Scalar cotangent Laplacian oracle of the curvature study (`IntrinsicTriangulation.CotanLaplacian`): the stiffness matrix as a discrete Dirichlet pairing, and its difference from barycentric-mass pointwise estimates ([research/CURVATURE_CONVERGENCE_ORACLES.md](../research/CURVATURE_CONVERGENCE_ORACLES.md)) | M. Wardetzky, [*Convergence of the Cotangent Formula: An Overview*](https://ddg.math.uni-goettingen.de/pub/convergence_cotan.pdf), §§2-3 (paper text only) |
| Intrinsic mollification of degenerate triangles | N. Sharp, K. Crane, *A Laplacian for Nonmanifold Triangle Meshes*, Computer Graphics Forum (SGP) 2020 |
| Heat method for geodesic distance (also on the intrinsic triangulation). The cube-sphere distance oracle uses §4.2 and Fig. 19 of the 2013 paper. | K. Crane, C. Weischedel, M. Wardetzky, [*Geodesics in Heat*](https://arxiv.org/abs/1204.6216), ACM TOG 2013; *The Heat Method for Distance Computation*, CACM 2017 |
| Vector heat method: connection Laplacian, parallel transport, scalar extension, log map | N. Sharp, Y. Soliman, K. Crane, [*The Vector Heat Method*](https://arxiv.org/abs/1805.09170), ACM TOG 2019 (§7.3 and Fig. 13 motivate the parallel-transport oracle) |
| Geodesic paths and loops by edge flips (`MeshGeodesicPath`): FlipOut on the wedge where the path turns by less than pi, the outer arc as the new path, smallest angle first; waypoints | N. Sharp, K. Crane, *You Can Find Geodesic Paths in Triangle Meshes by Just Flipping Edges*, ACM TOG (SIGGRAPH Asia) 2020 (geometry-central, MIT, has an implementation; not used as a source) |
| Geodesic Bezier curves (`MeshGeodesicBezier`): de Casteljau's algorithm with the linear interpolation of two points replaced by the point at the same fraction of the geodesic between them, evaluated at given parameters; the geodesics by FlipOut (above), the interpolated points inserted as vertices into one shared `IntrinsicTriangulation` by edge splits. Implemented from the papers; no code ported | M. Morera, P. Carvalho, L. Velho, *Modeling on Triangulations with Geodesic Curves*, The Visual Computer 24, 2008 (geodesic de Casteljau); N. Sharp, K. Crane, *You Can Find Geodesic Paths in Triangle Meshes by Just Flipping Edges*, ACM TOG 2020 (geodesic Bezier curves from FlipOut; their reference implementation, geometry-central, MIT, was not read) |
| Log map direction from the gradient of half the squared distance against the transported reference vector | Gauss lemma (eg M. do Carmo, *Riemannian Geometry*, 1992, ch. 3); own derivation, replaces the paper's diffused radial field, which is biased by the shape of the source's one-ring |
| Heat flow far beyond the double range: the Cholesky substitutions of an M-matrix only add non-negative terms, so they run unchanged on logarithms (`SparseCholesky.SolveLog`) | Own derivation (sign pattern of the Cholesky factor of an M-matrix, eg R. A. Horn, C. R. Johnson, *Topics in Matrix Analysis*, 1991, ch. 2.5) |
| Kahan's stable formula for the triangle area from its sides | W. Kahan, *Miscalculating Area and Angles of a Needle-like Triangle*, 2014 |

The comparison with `MeshLocalParam` (the discrete exponential map, R. Schmidt, C. Grimm, B. Wyvill,
*Interactive Decal Compositing with Discrete Exponential Maps*, ACM TOG 2006) is in the tests.

## Parameterization (`LSCMParameterization`, `SpectralConformalParameterization`, `ARAPParameterization`, `SLIMParameterization`, `BoundaryFirstFlattening`, `TutteEmbedding`, `ParameterizationDistortion`, `UVPacker`)

Implemented from the papers, no code taken over.

| Part | Source |
|---|---|
| Least squares conformal maps, as conformal energy (Dirichlet energy minus signed area over the boundary) | B. Lévy, S. Petitjean, N. Ray, J. Maillot, *Least Squares Conformal Maps for Automatic Texture Atlas Generation*, ACM TOG (SIGGRAPH) 2002; M. Desbrun, M. Meyer, P. Alliez, *Intrinsic Parameterizations of Surface Meshes*, Eurographics 2002; P. Mullen, Y. Tong, P. Alliez, M. Desbrun, *Spectral Conformal Parameterization*, SGP 2008 (equivalence) |
| Spectral conformal parameterization (`SpectralConformalParameterization`): the LSCM energy without pins, minimized subject to a unit lumped boundary mass and orthogonality to the translations, ie the eigenvector of the smallest nonzero eigenvalue of A x = λ B x | P. Mullen, Y. Tong, P. Alliez, M. Desbrun, *Spectral Conformal Parameterization*, SGP 2008 |
| Its solver: inverse power iteration on one shifted Cholesky factorization, with deflation of the translations; the next eigenvalue pair estimated by a deflated Rayleigh quotient (an upper bound) to refuse a (nearly) double smallest eigenvalue; the stall rule at the rounding floor and the start from the boundary's vector area | G. H. Golub, C. F. Van Loan, *Matrix Computations*, 4th ed. 2013, Sec. 8.2 (inverse iteration) and 8.1 (Rayleigh quotient); B. N. Parlett, *The Symmetric Eigenvalue Problem*, SIAM 1998, ch. 4-5 (deflation); stop rules and start own choices |
| As-rigid-as-possible parameterization (local/global) | L. Liu, L. Zhang, Y. Xu, C. Gotsman, S. Gortler, *A Local/Global Approach to Mesh Parameterization*, SGP 2008 |
| SLIM: reweighted proxy of the symmetric Dirichlet energy | M. Rabinovich, R. Poranne, D. Panozzo, O. Sorkine-Hornung, *Scalable Locally Injective Mappings*, ACM TOG 2017 |
| SLIM step as -H^-1 grad E with the proxy matrix H (its gradient at the current map is the energy gradient), the factorization of H kept for later iterations (lagged Hessian) | Rabinovich et al. 2017 (the proxy); V. Shamanskii, *A modification of Newton's method*, Ukrainian Math. J. 1967, and C. T. Kelley, *Solving Nonlinear Equations with Newton's Method*, SIAM 2003 (chord / Shamanskii methods); when to refactorize (a step below the average decrease rate since the factorization, counting the factorization as a fixed number of steps) is an own heuristic |
| SLIM convergence test by the decrease the proxy predicts (Newton decrement) | S. Boyd, L. Vandenberghe, *Convex Optimization*, 2004, Sec. 9.5 |
| SLIM start: LSCM if it is injective (exact overlap test, `UVOverlap`), else Tutte | own choice |
| Flip-preventing maximum step of the line search | J. Smith, S. Schaefer, *Bijective Parameterization with Free Boundaries*, ACM TOG (SIGGRAPH) 2015 |
| Symmetric Dirichlet energy | J. Schreiner, A. Asirvatham, E. Praun, H. Hoppe, *Inter-Surface Mapping*, SIGGRAPH 2004; Smith & Schaefer 2015 |
| Tutte / convex combination embedding | W. T. Tutte, *How to Draw a Graph*, 1963; M. Floater, *Parametrization and smooth approximation of surface triangulations*, CAGD 1997; M. Floater, *One-to-one piecewise linear mappings over triangulations*, Math. Comp. 2003 |
| Boundary First Flattening: discrete Yamabe equation, Dirichlet-to-Neumann / Neumann-to-Dirichlet maps, closing the boundary curve with minimal length change, holomorphic extension by a harmonic conjugate | R. Sawhney, K. Crane, *Boundary First Flattening*, ACM TOG 2017 (geometry-central, MIT, has an implementation; not used as a source) |
| Minimal area distortion with u = 0 on the boundary | B. Springborn, P. Schröder, U. Pinkall, *Conformal Equivalence of Triangle Meshes*, ACM TOG (SIGGRAPH) 2008 |
| Skyline bottom-left rectangle packing | J. Jylänki, *A Thousand Ways to Pack the Bin – A Practical Approach to Two-Dimensional Rectangle Bin Packing*, 2010 |
| Packed charts checked with exact predicates (`UVOverlap`); a chart whose rotated UVs overlap is placed without the rotation, and a vertex that rounding moved across a nearby edge is moved back by a few ulps | own; the idea of keeping orientations through rounding as in snap rounding (J. Hobby, *Practical segment intersection with finite precision output*, CGTA 1999), not its algorithm |
| Minimum-area bounding rectangle of a chart | existing `ContMinBox2` (rotating calipers, WildMagic port) |
| BFF disk mode: exterior angles of the boundary polygon inscribed in a circle (inscribed angle theorem, the longest edge on the major arc if the center lies outside) | elementary geometry; own derivation |
| ARAP and SLIM with degenerate triangles: mollified cotangents and a flat layout along the longest edge (ARAP), unit springs on the current UV edges in the SLIM proxy | own adaptation of the intrinsic mollification of Sharp & Crane 2020 (see Intrinsic triangulations); the rotations as Procrustes over the edges, O. Sorkine, M. Alexa, *As-Rigid-As-Possible Surface Modeling*, SGP 2007 |

## Fixed-boundary untangling (`FoldoverFreeParameterization`)

Implemented from V. Garanzha, I. Kaporin, L. Kudryavtseva, F. Protais, N. Ray and D. Sokolov,
[*Foldover-free maps in 50 lines of code*, ACM TOG 40(4), 2021](https://arxiv.org/html/2102.03069v1),
sections 2.2–2.3 (regularized shape/area objective and equation-6 epsilon schedule), section 4.1 and Appendix A
(analytic gradient and modified Hessian), Algorithm 1 line 11 (stop rule: all determinants positive and the energy
decreases by less than 1e-3). The implementation specializes the lifted convex Hessian to 2D,
uses the existing constrained sparse Cholesky solver and bounded Armijo backtracking. Normalization, result
retention and validation are local integration choices. No authors' implementation, libigl, GPL/MPL or native
AD code was read or ported. SLIM's publication and the previously MIT-checked TinyAD README were architectural
prior art only. Conditional convergence (section 4.2) is not an iteration-bound guarantee; section 3.3's global
overlap limitation remains. See [design and limits](../design/FOLDOVER_FREE_MAPS.md).

## Seams, charts, cones, UV atlas (`MeshDiscCut`, `MeshChartSegmentation`, `ConeFlattening`, `UVOverlap`, `UVAtlasGenerator`)

Implemented from the papers, no code taken over. geometry-central (MIT) and the BFF reference implementation
(github.com/GeometryCollective/boundary-first-flattening, MIT) have cut and cone code; neither was used as a
source. CGAL's `Surface_mesh_parameterization` / seam code (GPL) was not used.

| Part | Source |
|---|---|
| Cut graph by tree-cotree decomposition: primal spanning tree, dual spanning tree of the remaining edges, the 2g leftover edges close the generator loops | D. Eppstein, *Dynamic Generators of Topologically Embedded Graphs*, SODA 2003 |
| Shortest path tree from a root and maximum dual spanning tree weighted by the loop length d(u) + l(e) + d(v): the greedy shortest system of loops | J. Erickson, K. Whittlesey, *Greedy Optimal Homotopy and Homology Generators*, SODA 2005 |
| Removing dangling branches of the cut (leaves that are not required) keeps the disc topology | J. Erickson, S. Har-Peled, *Optimally Cutting a Surface into a Disk*, Discrete & Computational Geometry 2004; X. Gu, S. Gortler, H. Hoppe, *Geometry Images*, ACM TOG (SIGGRAPH) 2002 |
| Surfaces with boundary (tree edges in the dual tree where needed), required vertices joined along the shortest path tree, the single-edge slit of genus 0, feature weighted edge costs, splitting vertices into wedges by a union-find over triangle corners | own choices, described in the code comments |
| BFF with cone singularities: scale factors with u = 0 at the cones, the cut through the cones, Dirichlet data on the cut disc | R. Sawhney, K. Crane, *Boundary First Flattening*, ACM TOG 2017 (section on cone singularities) |
| Greedy cone placement at the largest scale factor of the Yamabe equation C u = -K, first cone from the uniform curvature flow | M. Ben-Chen, C. Gotsman, G. Bunin, *Conformal Flattening by Curvature Prescription and Metric Scaling*, Computer Graphics Forum (Eurographics) 2008; B. Springborn, P. Schröder, U. Pinkall, *Conformal Equivalence of Triangle Meshes*, ACM TOG 2008 |
| Chart growing with proxies (normal, L2,1 metric) and Lloyd iterations, new charts at the worst triangle | D. Cohen-Steiner, P. Alliez, M. Desbrun, *Variational Shape Approximation*, ACM TOG (SIGGRAPH) 2004 |
| Compactness term in the growing cost | D. Julius, V. Kraevoy, A. Sheffer, *D-Charts: Quasi-Developable Mesh Segmentation*, Computer Graphics Forum (Eurographics) 2005 |
| Charts for an atlas, flattened conformally; splitting charts while the distortion is too high | B. Lévy, S. Petitjean, N. Ray, J. Maillot, *Least Squares Conformal Maps for Automatic Texture Atlas Generation*, ACM TOG (SIGGRAPH) 2002; K. Zhou, J. Snyder, B. Guo, H.-Y. Shum, *Iso-charts: Stretch-driven Mesh Parameterization using Spectral Analysis*, SGP 2004 |
| Exact overlap test of UV triangles: separating axis among the six edge lines, exact orientation predicate | separating axis theorem for convex polygons (eg C. Ericson, *Real-Time Collision Detection*, 2005); J. R. Shewchuk 1997 (predicates, see above) |

Comparison tool only (not in the library, no code taken over): xatlas (github.com/jpcy/xatlas, MIT) is run as a black box
on the GitHub-hosted runner by `benchmarks/UVAtlasBenchmark` (workflow `uvatlas-compare`, pinned commit
`f700c7790aaa030e794b52ba7791a05c085faf0c`). Only its public API is called, from `benchmarks/UVAtlasBenchmark/xatlas/xatlas_driver.cpp`
(our code). Its source is neither read into this repository nor ported; the atlases it writes are evaluated by our code
(`benchmarks/Shared/UvEvaluation.cs`) like our own.

## Sparse Cholesky (`SparseCholesky`)

Implemented from the papers and textbooks, no code taken over (not from CHOLMOD or CSparse, which are LGPL/GPL, nor from the BSD-licensed SuiteSparse AMD).

The solvers (`solvers/`, including `CholeskyNestedDissection`) have no Stryker mutation shard; their tests are not mutation-gated (`docs/qa/mutation-and-coverage.md`).

| Part | Source |
|---|---|
| Approximate minimum degree ordering on the quotient graph | P. Amestoy, T. Davis, I. Duff, *An Approximate Minimum Degree Ordering Algorithm*, SIAM J. Matrix Anal. Appl. 1996; A. George, J. Liu, *The Evolution of the Minimum Degree Ordering Algorithm*, SIAM Review 1989 |
| Opt-in patch-guided nested dissection ordering (`CholeskyOrdering.NestedDissection`, `CholeskyNestedDissection`, paper-only; no METIS/Homa/Scotch code read) | A. George, SIAM J. Numer. Anal. 1973; A. Pothen, H. Simon, K.-P. Liou, SIMAX 1990; G. Karypis, V. Kumar, SISC 1998; C. Ashcraft, J. Liu, SIMAX 1998; J. Hopcroft, R. Karp, SICOMP 1973; B. Zarebavani et al., SIGGRAPH 2026 (arXiv:2602.00898) |
| Symbolic factorization by the elimination tree, postordering (the analysis, `CholeskyAnalysis`, is shared by `SparseCholesky` and `SparseHermitianCholesky`; the tree is found from the pattern of A by Liu's row-wise algorithm with path compression, so the structure of L is computed once, for the postordered matrix) | J. Liu, *The Role of Elimination Trees in Sparse Factorization*, SIAM J. Matrix Anal. Appl. 1990; A. George, J. Liu, *Computer Solution of Large Sparse Positive Definite Systems*, 1981 |
| Relaxed supernodes | C. Ashcraft, R. Grimes, *The Influence of Relaxed Supernode Partitions on the Multifrontal Method*, ACM TOMS 1989 |
| Left-looking supernodal numeric factorization | E. Ng, B. Peyton, *Block Sparse Cholesky Algorithms on Advanced Uniprocessor Computers*, SIAM J. Sci. Comput. 1993 |

### Preconditioner prototypes (`benchmarks/MultiscaleCholeskyProbe`, not in the library)

Research probe of the spike [MULTISCALE_SPARSE_CHOLESKY_2026-11.md](../research/MULTISCALE_SPARSE_CHOLESKY_2026-11.md) (outcome: no build). Implemented from the papers only, no code read or taken over (the reference implementation of the multiscale incomplete Cholesky is GPL v3 and was never opened).

| Part | Source |
|---|---|
| Multiscale incomplete Cholesky: coarse-to-fine ordering, pattern by distance against the smaller length scale, zero fill-in factorization with a diagonal shift | J. Chen, F. Schäfer, J. Huang, M. Desbrun, *Multiscale Cholesky Preconditioning for Ill-Conditioned Problems*, ACM Trans. Graph. 40(4), 2021; ordering and pattern theory: F. Schäfer, T. J. Sullivan, H. Owhadi, *Compression, Inversion, and Approximate PCA of Dense Kernel Matrices at Near-Linear Computational Complexity*, Multiscale Model. Simul. 19(2), 2021 |
| Factorized sparse approximate inverse by one dense solve per row (the closed form of the KL minimisation, applied to the entries of A) | F. Schäfer, M. Katzfuss, H. Owhadi, *Sparse Cholesky Factorization by Kullback-Leibler Minimization*, SIAM J. Sci. Comput. 43(3), 2021; L. Yu. Kolotilina, A. Yu. Yeremin, *Factorized Sparse Approximate Inverse Preconditionings I. Theory*, SIAM J. Matrix Anal. Appl. 14(1), 1993 |
| Seeded approximate elimination (one sampled edge per neighbour of the eliminated vertex, elimination by current degree, neighbours sorted by weight) | R. Kyng, S. Sachdeva, *Approximate Gaussian Elimination for Laplacians: Fast, Sparse, and Simple*, FOCS 2016; Y. Gao, R. Kyng, D. Spielman, *Robust and Practical Solution of Laplacian Equations by Approximate Elimination*, arXiv:2303.00709, 2023 |


## Discrete conformal annulus study

The [annulus study](../research/DISCRETE_CONFORMAL_EQUIVALENCE_STUDY.md) uses the following papers. The procedural reference is derived from vertex scaling. No external code was used.

| Source | Use |
|---|---|
| B. Springborn, P. Schröder and U. Pinkall, [*Conformal Equivalence of Triangle Meshes*](https://doi.org/10.1145/1360612.1360676), ACM TOG (SIGGRAPH) 2008 | Convex Lobachevsky-Milnor energy, angle gradient and cotangent Hessian |
| A. I. Bobenko, U. Pinkall and B. A. Springborn, [*Discrete Conformal Maps and Ideal Hyperbolic Polyhedra*](https://doi.org/10.2140/gt.2015.19.2155), Geometry & Topology 19, 2015 | Hyperbolic Delaunay criterion, Ptolemy flip invariance and $C^2$ convexity |
| M. Gillespie, B. Springborn and K. Crane, [*Discrete Conformal Equivalence of Polyhedral Surfaces*](https://doi.org/10.1145/3450626.3459763), ACM TOG 40(4), 2021 | Vertex scaling, Ptolemy flips, normal-coordinate correspondence and boundary conditions |
| M. Campen, R. Capouellez, H. Shen, L. Zhu, D. Panozzo and D. Zorin, [*Efficient and Robust Discrete Conformal Equivalence with Boundary*](https://arxiv.org/abs/2104.04614), 2021 | Prescribed boundary curvature and stable flips in the symmetric double cover |
