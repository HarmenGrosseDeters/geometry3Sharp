# Sources

geometry3Sharp is under the Boost Software License. New code in this fork is written from published papers;
no code is taken over from other libraries unless it is under a compatible permissive license and named here.
Code under the GPL or LGPL (eg CGAL) is never used as a source.

## Exact mesh booleans (`ExactMeshBoolean`, `ExactMeshArrangement`, `ExactPlanarSimplify`, `ExactTriangulation2`, `ExactPoint3`, `ExactPredicates`)

Implemented from the papers, no code taken over.

| Part | Source |
|---|---|
| Exact arrangement of the triangles, classification by winding numbers per patch; operations on any number of meshes as a predicate on the vector of their winding numbers (n-ary union / intersection, "at least k of n") | Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh Arrangements for Solid Geometry*, ACM TOG (SIGGRAPH) 2016 |
| Implicit intersection points (edge crossing a plane, three planes) with floating-point filters, exact evaluation only when needed | M. Attene, *Indirect Predicates for Geometric Constructions*, Computer-Aided Design 2020; G. Cherchi, M. Livesu, R. Scateni, M. Attene, *Fast and Robust Mesh Arrangements using Floating-point Arithmetic*, ACM TOG (SIGGRAPH Asia) 2020 |
| Booleans on arrangements | G. Cherchi, F. Pellacini, M. Attene, M. Livesu, *Interactive and Robust Mesh Booleans*, ACM TOG (SIGGRAPH Asia) 2022 |
| Expansion arithmetic, error bounds and adaptive stages B, C, D of orient2d / orient3d / incircle (`AdaptivePredicates`, own implementation from the paper, not from `predicates.c` or Triangle.NET) | J. R. Shewchuk, *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*, Discrete & Computational Geometry 1997 |
| Constrained Delaunay triangulation (constraint insertion by flips) | S. W. Sloan, *A fast algorithm for generating constrained Delaunay triangulations*, Computers & Structures 1993; C. L. Lawson 1977 (flips) |
| Removal of unneeded vertices in flat regions (`ExactPlanarSimplify`): ear clipping of the hole, Lawson flips | G. H. Meisters, *Polygons have ears*, American Mathematical Monthly 1975 (ear clipping); C. L. Lawson, *Software for C1 surface interpolation*, 1977 (flips towards Delaunay) |
| Generalized winding number for open meshes | A. Jacobson, L. Kavan, O. Sorkine-Hornung, *Robust Inside-Outside Segmentation using Generalized Winding Numbers*, ACM TOG 2013 (via the existing `FastWindingNumber`, Barill et al. 2018) |

The error bounds of the implicit points' approximations (`segment_point`, `plane_value`, `planes_approx`) and of
the filters on them are derived in the code comments.

## Rounding the exact output (`MeshRoundingRepair`, `ExactMeshBoolean.SafeRounding`, `ExactMeshBoolean.ComputeExactVertices`)

Implemented from the papers, no code taken over. Read for the design (what the exact methods do with their output):

| Part | Source |
|---|---|
| Local edits after rounding, each accepted only if it creates no intersection: contraction of short edges (link condition), flips of skinny triangles; removal of components thinner than the rounding distance; the d-separation argument (why rounding a separated mesh is safe) | V. Milenkovic, E. Sacks, *Geometric rounding and feature separation in meshes*, Computer-Aided Design 108, 2019 (arXiv 1805.03252). Their expansion and optimization stages (linear programs that separate the remaining close features before rounding) are not implemented; instead single vertices are moved by tiny amounts after rounding, accepted under the same rule |
| Needles collapsed, caps flipped | M. Botsch, L. Kobbelt, *A Robust Procedure to Eliminate Degenerate Faces from Triangle Meshes*, VMV 2001 |
| Snap rounding in 2D: hot pixels, iterated snap rounding (each vertex half a pixel from every non-incident edge) | J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 1999; D. Halperin, E. Packer, *Iterated Snap Rounding*, CGTA 23(2), 2002. Background only: the 2D methods have no direct 3D counterpart |
| Rounding polyhedral meshes in 3D with guarantees (Hausdorff distance at most 3/2 grid units, no new intersections; worst case O(n^13) size) | O. Devillers, S. Lazard, W. Lenhart, *Rounding meshes in 3D*, Discrete & Computational Geometry 64, 2020 (SoCG 2018: *3D snap rounding*); S. Fortune, *Vertex-rounding a three-dimensional polyhedral subdivision*, DCG 1999. Not implemented: the output size makes them impractical for meshes of 10^5 triangles |
| How the exact arrangements handle their output: rational coordinates kept, or rounded to double (which can create intersections; Zhou et al. round and resolve again, as `MeshRobustRepair` did before) | Q. Zhou et al. 2016, G. Cherchi et al. 2020 / 2022 (see above); G. Trettner, J. Nehring-Wirxel, L. Kobbelt, *EMBER: Exact Mesh Booleans via Efficient & Robust Local Arrangements*, ACM TOG (SIGGRAPH) 2022 (plane-based, exact homogeneous output) |
| Manifold (Apache-2.0) | its documentation only (removes degenerate triangles and unneeded vertices when building a manifold, with a tolerance); no code read or taken over |

The exact output coordinates (`ResultExactVertices`) are the rational points of the arrangement mapped back from the
integer grid (`ExactMeshArrangement.ToInputExact`).

## Intrinsic triangulations and geodesics (`IntrinsicTriangulation`, `VectorHeatMethod`, `MeshGeodesicDistance`)

Implemented from the papers, no code taken over (geometry-central, MIT, was not used as a source).

| Part | Source |
|---|---|
| Intrinsic triangulations as edge lengths, signposts (direction and length of each halfedge at its tail), flips and their signpost update, tracing intrinsic edges over the input mesh | N. Sharp, Y. Soliman, K. Crane, *Navigating Intrinsic Triangulations*, ACM TOG (SIGGRAPH) 2019 |
| Vertex insertion in faces and on edges with the signpost update, locating inserted points by tracing, intrinsic Delaunay refinement by circumcenter insertion, common subdivision by tracing the intrinsic edges | N. Sharp, Y. Soliman, K. Crane, *Navigating Intrinsic Triangulations*, ACM TOG (SIGGRAPH) 2019 |
| Delaunay refinement: circumcenters of bad triangles, encroached boundary segments split at their midpoints instead | L. P. Chew, *Guaranteed-Quality Mesh Generation for Curved Surfaces*, ACM Symposium on Computational Geometry 1993; J. Ruppert, *A Delaunay Refinement Algorithm for Quality 2-Dimensional Mesh Generation*, Journal of Algorithms 1995 |
| Small input angles: concentric shells (splits at powers of two from the corner), not refining triangles whose small angle comes from the input; a segment is encroached iff the opposite vertex of its Delaunay triangle is | J. R. Shewchuk, *Delaunay Refinement Algorithms for Triangular Mesh Generation*, Computational Geometry: Theory and Applications 2002 |
| Integer coordinates for a robust common subdivision (not implemented; why the tracing suffices is in OPERATIONS.md) | M. Gillespie, N. Sharp, K. Crane, *Integer Coordinates for Intrinsic Geometry Processing*, ACM TOG (SIGGRAPH Asia) 2021 |
| Intrinsic Delaunay triangulations, non-negative cotan weights, flip algorithm | A. I. Bobenko, B. A. Springborn, *A Discrete Laplace-Beltrami Operator for Simplicial Surfaces*, Discrete & Computational Geometry 2007; C. Indermitte, T. M. Liebling, M. Troyanov, H. Clémençon, *Voronoi diagrams on piecewise flat surfaces and an application to biological growth*, Theoretical Computer Science 2001 (termination of the flips) |
| Intrinsic mollification of degenerate triangles | N. Sharp, K. Crane, *A Laplacian for Nonmanifold Triangle Meshes*, Computer Graphics Forum (SGP) 2020 |
| Heat method for geodesic distance (also on the intrinsic triangulation) | K. Crane, C. Weischedel, M. Wardetzky, *Geodesics in Heat*, ACM TOG 2013; *The Heat Method for Distance Computation*, CACM 2017 |
| Vector heat method: connection Laplacian, parallel transport, scalar extension, log map | N. Sharp, Y. Soliman, K. Crane, *The Vector Heat Method*, ACM TOG 2019 |
| Log map direction from the gradient of half the squared distance against the transported reference vector | Gauss lemma (eg M. do Carmo, *Riemannian Geometry*, 1992, ch. 3); own derivation, replaces the paper's diffused radial field, which is biased by the shape of the source's one-ring |
| Kahan's stable formula for the triangle area from its sides | W. Kahan, *Miscalculating Area and Angles of a Needle-like Triangle*, 2014 |

The comparison with `MeshLocalParam` (the discrete exponential map, R. Schmidt, C. Grimm, B. Wyvill,
*Interactive Decal Compositing with Discrete Exponential Maps*, ACM TOG 2006) is in the tests.

## Alpha wrapping (`MeshAlphaWrap`, `Delaunay3`, `ExactPredicates.InSphere`)

Implemented from the papers, no code taken over. CGAL's implementation (`Alpha_wrap_3`, GPL) was not used.

| Part | Source |
|---|---|
| Alpha wrapping: carving a Delaunay triangulation from outside through facets an alpha ball fits through, Steiner points on the offset surface where the dual Voronoi edge crosses it (rule 1) or where a cell intersects the input (projection of the circumcenter, rule 2), manifold post-processing by moving outside cells to the inside | C. Portaneri, M. Rouxel-Labbé, M. Hemmer, D. Cohen-Steiner, P. Alliez, *Alpha Wrapping with an Offset*, ACM TOG (SIGGRAPH) 2022 |
| Incremental Delaunay triangulation by cavities | A. Bowyer, *Computing Dirichlet tessellations*, The Computer Journal 1981; D. F. Watson, *Computing the n-dimensional Delaunay tessellation with application to Voronoi polytopes*, The Computer Journal 1981 |
| Point location by a stochastic walk | O. Devillers, S. Pion, M. Teillaud, *Walking in a triangulation*, IJFCS 2002 |
| InSphere determinant and its floating-point error bound (filter; the uncertain cases are evaluated in integer arithmetic) | J. R. Shewchuk 1997 (see above) |
| Closest point on a triangle (Voronoi regions), separating axis test of tetrahedron and triangle | C. Ericson, *Real-Time Collision Detection*, 2005 |

Choices described in the code: traversability by the circumradius of the facet (an alpha ball passes through
its circumcircle; the variant with the smallest empty circumscribing ball was tried and refines thin sheets
down to the scale of the offset), the order of the gates (largest facet first), labels of new cells (inside) and the removal of
enclosed outside pockets, the manifold step (around a non-manifold edge or vertex the outside part connected
to the box stays outside), and the contact margin of the intersection test (1e-6 of the offset).

## Parameterization (`LSCMParameterization`, `ARAPParameterization`, `SLIMParameterization`, `BoundaryFirstFlattening`, `TutteEmbedding`, `ParameterizationDistortion`, `UVPacker`)

Implemented from the papers, no code taken over.

| Part | Source |
|---|---|
| Least squares conformal maps, as conformal energy (Dirichlet energy minus signed area over the boundary) | B. Lévy, S. Petitjean, N. Ray, J. Maillot, *Least Squares Conformal Maps for Automatic Texture Atlas Generation*, ACM TOG (SIGGRAPH) 2002; M. Desbrun, M. Meyer, P. Alliez, *Intrinsic Parameterizations of Surface Meshes*, Eurographics 2002; P. Mullen, Y. Tong, P. Alliez, M. Desbrun, *Spectral Conformal Parameterization*, SGP 2008 (equivalence) |
| As-rigid-as-possible parameterization (local/global) | L. Liu, L. Zhang, Y. Xu, C. Gotsman, S. Gortler, *A Local/Global Approach to Mesh Parameterization*, SGP 2008 |
| SLIM: reweighted proxy of the symmetric Dirichlet energy | M. Rabinovich, R. Poranne, D. Panozzo, O. Sorkine-Hornung, *Scalable Locally Injective Mappings*, ACM TOG 2017 |
| Flip-preventing maximum step of the line search | J. Smith, S. Schaefer, *Bijective Parameterization with Free Boundaries*, ACM TOG (SIGGRAPH) 2015 |
| Symmetric Dirichlet energy | J. Schreiner, A. Asirvatham, E. Praun, H. Hoppe, *Inter-Surface Mapping*, SIGGRAPH 2004; Smith & Schaefer 2015 |
| Tutte / convex combination embedding | W. T. Tutte, *How to Draw a Graph*, 1963; M. Floater, *Parametrization and smooth approximation of surface triangulations*, CAGD 1997; M. Floater, *One-to-one piecewise linear mappings over triangulations*, Math. Comp. 2003 |
| Boundary First Flattening: discrete Yamabe equation, Dirichlet-to-Neumann / Neumann-to-Dirichlet maps, closing the boundary curve with minimal length change, holomorphic extension by a harmonic conjugate | R. Sawhney, K. Crane, *Boundary First Flattening*, ACM TOG 2017 (geometry-central, MIT, has an implementation; not used as a source) |
| Minimal area distortion with u = 0 on the boundary | B. Springborn, P. Schröder, U. Pinkall, *Conformal Equivalence of Triangle Meshes*, ACM TOG (SIGGRAPH) 2008 |
| Skyline bottom-left rectangle packing | J. Jylänki, *A Thousand Ways to Pack the Bin – A Practical Approach to Two-Dimensional Rectangle Bin Packing*, 2010 |
| Minimum-area bounding rectangle of a chart | existing `ContMinBox2` (rotating calipers, WildMagic port) |

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

## Sparse Cholesky (`SparseCholesky`)

Implemented from the papers and textbooks, no code taken over (not from CHOLMOD or CSparse, which are LGPL/GPL, nor from the BSD-licensed SuiteSparse AMD).

| Part | Source |
|---|---|
| Approximate minimum degree ordering on the quotient graph | P. Amestoy, T. Davis, I. Duff, *An Approximate Minimum Degree Ordering Algorithm*, SIAM J. Matrix Anal. Appl. 1996; A. George, J. Liu, *The Evolution of the Minimum Degree Ordering Algorithm*, SIAM Review 1989 |
| Symbolic factorization by the elimination tree, postordering | J. Liu, *The Role of Elimination Trees in Sparse Factorization*, SIAM J. Matrix Anal. Appl. 1990; A. George, J. Liu, *Computer Solution of Large Sparse Positive Definite Systems*, 1981 |
| Relaxed supernodes | C. Ashcraft, R. Grimes, *The Influence of Relaxed Supernode Partitions on the Multifrontal Method*, ACM TOMS 1989 |
| Left-looking supernodal numeric factorization | E. Ng, B. Peyton, *Block Sparse Cholesky Algorithms on Advanced Uniprocessor Computers*, SIAM J. Sci. Comput. 1993 |

## Analysis (`MeshWallThickness`, `MeshDeviation`, `PrimitiveFit3`)

Implemented from the papers, no code taken over.

| Part | Source |
|---|---|
| Shape diameter function (cone of rays, median / standard deviation filter, weights 1/angle) | L. Shapira, A. Shamir, D. Cohen-Or, *Consistent mesh partitioning and skeletonisation using the shape diameter function*, The Visual Computer 2008 |
| Largest inscribed ball per surface point (shrinking ball iteration) | J. Ma, S. W. Bai, X. Hu, Y. J. Kim, *3D medial axis point approximation using nearest neighbors and the normal field*, The Visual Computer 2012 |
| Separation angle condition for medial balls (contacts must be seen at a large angle from the center) | T. K. Dey, W. Zhao, *Approximate medial axis as a Voronoi subcomplex*, Computer-Aided Design 2004 |
| Watertight ray-triangle test (shear into the ray frame, shared edge functions) | S. Woop, C. Benthin, I. Wald, *Watertight Ray/Triangle Intersection*, Journal of Computer Graphics Techniques 2013 |
| Sign of the distance to a mesh from the angle weighted pseudo-normal of the closest feature | J. A. Baerentzen, H. Aanaes, *Signed distance computation using the angle weighted pseudonormal*, IEEE TVCG 2005 |
| Sign from the generalized winding number | A. Jacobson, L. Kavan, O. Sorkine-Hornung 2013 (see above), via `FastWindingNumber` |
| Nesting of closed components for the inverted-component test (`MeshPostRepairCleanup.FindInvertedComponents`, used via `MeshWallThickness.InvertedTriangles`): winding number of each candidate enclosing component at points moved slightly inside the component (relative to its size), majority of several samples | own reasoning; winding number as above |
| RANSAC | M. A. Fischler, R. C. Bolles, *Random sample consensus*, Communications of the ACM 1981 |
| Local optimization of every new best model (LO-RANSAC) | O. Chum, J. Matas, J. Kittler, *Locally optimized RANSAC*, DAGM 2003 |
| Bail-out pre-test of candidates on a fixed random subset | D. P. Capel, *An effective bail-out test for RANSAC consensus scoring*, BMVC 2005 |
| Truncated quadratic (MSAC) scoring | P. H. S. Torr, A. Zisserman, *MLESAC: A new robust estimator with application to estimating image geometry*, CVIU 2000 |
| Minimal samples with normals (cylinder from 2 points, cone from 3 points), normal deviation test | R. Schnabel, R. Wahl, R. Klein, *Efficient RANSAC for point-cloud shape detection*, Computer Graphics Forum 2007 |
| Algebraic circle / sphere fit as start value | I. Kasa, *A circle fitting procedure and its error analysis*, IEEE Trans. Instrumentation and Measurement 1976 |
| Levenberg-Marquardt on the orthogonal distances | K. Levenberg 1944; D. W. Marquardt, *An algorithm for least-squares estimation of nonlinear parameters*, SIAM J. Appl. Math. 1963 |
| Normal estimation by PCA of the nearest neighbors | H. Hoppe, T. DeRose, T. Duchamp, J. McDonald, W. Stuetzle, *Surface reconstruction from unorganized points*, SIGGRAPH 1992 |
| Iteratively reweighted least squares with Cauchy weights (robust local planes) | P. W. Holland, R. E. Welsch, *Robust regression using iteratively reweighted least-squares*, Communications in Statistics 1977 |

## Distances and simplification (`MeshHausdorffDistance`, `TriangleDeviationBounds`, `Reducer`)

The sources named in the code:

| Part | Source |
|---|---|
| Hausdorff distance with guaranteed bounds (branch and bound over triangles, cascading upper bounds; `TriangleDeviationBounds`, also used by `Reducer.MaxDeviation`) | L. Sacht, A. Jacobson, *Cascading upper bounds for triangle soup Pompeiu-Hausdorff distance*, SGP 2024 |
| Regularized optimal collapse point (`Reducer.QuadricRegularization`) | P. Trettner, L. Kobbelt, *Fast and Robust QEF Minimization using Probabilistic Quadrics*, Computer Graphics Forum (Eurographics) 2020 |
| Triangle-triangle intersection test (`ExactTriangleIntersection`) | O. Devillers, P. Guigue, *Faster triangle-triangle intersection tests*, INRIA RR-4488, 2002 |

## Dual contouring and winding number solids (`DualContouring`, `WindingNumberOctree`, `MeshWindingNumberSolidify`, `MeshWindingNumberBoolean`)

| Part | Source |
|---|---|
| Dual contouring with QEF vertices from Hermite data | T. Ju, F. Losasso, S. Schaefer, J. Warren, *Dual Contouring of Hermite Data*, ACM TOG (SIGGRAPH) 2002 |
| One vertex per surface patch of a cell (manifold dual contouring) | S. Schaefer, T. Ju, J. Warren, *Manifold Dual Contouring*, IEEE TVCG 2007 |
| Refinement at ambiguous faces | R. Zint, R. Grosso, P. Guertler, *Resolving non-manifoldness on meshes from dual marching cubes*, 2022 |
| Fast winding numbers of the input (inside test of the solids) | G. Barill, N. Dickson, R. Schmidt, D. I. W. Levin, A. Jacobson, *Fast Winding Numbers for Soups and Clouds*, ACM TOG (SIGGRAPH) 2018 (existing `FastWindingNumber`) |

## Terrain (`TerrainDualContouring`, `TerrainGreedyTIN`, `ClothSimulationFilter`, `TerrainOffset`, `TerrainSafetyLines`, `TerrainBreakLines`, `TerrainVolumeDifference`)

| Part | Source |
|---|---|
| Ground filter (inverted point cloud, falling cloth) | W. Zhang, J. Qi, P. Wan, H. Wang, D. Xie, X. Wang, G. Yan, *An Easy-to-Use Airborne LiDAR Data Filtering Method Based on Cloth Simulation*, Remote Sensing 2016. The authors' reference implementation (CSF) is Apache-2.0 |
| Greedy insertion TIN with a maximum vertical error | M. Garland, P. S. Heckbert, *Fast Polygonal Approximation of Terrains and Height Fields*, CMU-CS-95-181, 1995 |
| 2.5D dual contouring of the top surface, quadtree merging | after Ju et al. 2002 (see above); the height field sampling, the 2D boundary QEF and the overlap removal are this library's own |
| Geodesic safety lines (`AlongSurface`) | the heat method, K. Crane, C. Weischedel, M. Wardetzky, *Geodesics in Heat*, ACM TOG 2013 (via `MeshGeodesicDistance`) |
| Offset surface, horizontal safety lines, break lines, cut and fill volumes | own methods (no external source) |

## Benchmarks

`benchmarks/BooleanComparison` runs CGAL (GPL), Cherchi et al. (MIT) and Manifold (Apache-2.0) as separate
programs for comparison only. The driver `external/cgal_bool.cpp` links against CGAL; compiled, it falls under
the GPL and is not distributed. None of these tools is part of the library.

`benchmarks/MeshRepairBenchmark/external/cgal_wrap.cpp` runs CGAL's alpha wrap (`CGAL::alpha_wrap_3`, GPL) the
same way, for the comparison with `MeshAlphaWrap` (`BENCH_REPAIR=cgalwrap`). It is written against CGAL's
documented API; CGAL's implementation was not looked at. Compiled, it falls under the GPL and is not distributed.

Data classes of the comparison (only downloaded at run time by `benchmarks/BooleanComparison/data/fetch.py`
at fixed commits; no mesh files are in the repository):

| class | source | license |
|---|---|---|
| `cgal` | CGAL test and example meshes, `Data/data/meshes` of https://github.com/CGAL/cgal, tag v6.0.1 (commit 50cfbde) | the data has no license of its own; CGAL's code is GPL/LGPL. Used only by the external comparison, nothing of it is part of the repository or the library |
| `cherchi` | test meshes of https://github.com/gcherchi/InteractiveAndRobustMeshBooleans, commit 7bd6c26 | MIT (repository); the models themselves (Stanford bunny and armadillo, fertility, cow, cactus) originate from public model collections with their own terms, which the repository does not restate |
| `synthetic`, `sequence` | generated by `BooleanComparison synth` (boxes, spheres, cube grids) | part of this repository (Boost) |
| `thingi10k` | Thingi10K selection of `benchmarks/MeshRepairBenchmark` | individual licenses per model |

The Open Boolean Benchmark (https://github.com/Open-Boolean-Benchmark/boolean-benchmark-runners, MIT) defines
the request/result protocol that `BooleanComparison obb` implements; `obb-runner/` follows its runner layout.
No code of the runners repository was taken over. Its benchmark cases (Nefertiti, dental scans, iterated cube
grid) are not published; `cube_grid_*` of the `sequence` class is a case of our own modelled after the
description of the iterated cube grid.
