# Sources

geometry3Sharp is under the Boost Software License. New code in this fork is written from published papers;
no code is taken over from other libraries unless it is under a compatible permissive license and named here.
Code under the GPL or LGPL (eg CGAL) is never used as a source.

## Exact mesh booleans (`ExactMeshBoolean`, `ExactMeshArrangement`, `ExactTriangulation2`, `ExactPoint3`, `ExactPredicates`)

Implemented from the papers, no code taken over.

| Part | Source |
|---|---|
| Exact arrangement of the triangles, classification by winding numbers per patch | Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh Arrangements for Solid Geometry*, ACM TOG (SIGGRAPH) 2016 |
| Implicit intersection points (edge crossing a plane, three planes) with floating-point filters, exact evaluation only when needed | M. Attene, *Indirect Predicates for Geometric Constructions*, Computer-Aided Design 2020; G. Cherchi, M. Livesu, R. Scateni, M. Attene, *Fast and Robust Mesh Arrangements using Floating-point Arithmetic*, ACM TOG (SIGGRAPH Asia) 2020 |
| Booleans on arrangements | G. Cherchi, F. Pellacini, M. Attene, M. Livesu, *Interactive and Robust Mesh Booleans*, ACM TOG (SIGGRAPH Asia) 2022 |
| Expansion arithmetic, error bounds and adaptive stages B, C, D of orient2d / orient3d / incircle (`AdaptivePredicates`, own implementation from the paper, not from `predicates.c` or Triangle.NET) | J. R. Shewchuk, *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*, Discrete & Computational Geometry 1997 |
| Constrained Delaunay triangulation (constraint insertion by flips) | S. W. Sloan, *A fast algorithm for generating constrained Delaunay triangulations*, Computers & Structures 1993; C. L. Lawson 1977 (flips) |
| Generalized winding number for open meshes | A. Jacobson, L. Kavan, O. Sorkine-Hornung, *Robust Inside-Outside Segmentation using Generalized Winding Numbers*, ACM TOG 2013 (via the existing `FastWindingNumber`, Barill et al. 2018) |

The error bounds of the implicit points' approximations (`segment_point`, `plane_value`, `planes_approx`) and of
the filters on them are derived in the code comments.

## Intrinsic triangulations and geodesics (`IntrinsicTriangulation`, `VectorHeatMethod`, `MeshGeodesicDistance`, `SparseCholesky`)

Implemented from the papers, no code taken over (geometry-central, MIT, was not used as a source).

| Part | Source |
|---|---|
| Intrinsic triangulations as edge lengths, signposts (direction and length of each halfedge at its tail), flips and their signpost update, tracing intrinsic edges over the input mesh | N. Sharp, Y. Soliman, K. Crane, *Navigating Intrinsic Triangulations*, ACM TOG (SIGGRAPH) 2019 |
| Intrinsic Delaunay triangulations, non-negative cotan weights, flip algorithm | A. I. Bobenko, B. A. Springborn, *A Discrete Laplace-Beltrami Operator for Simplicial Surfaces*, Discrete & Computational Geometry 2007; C. Indermitte, T. M. Liebling, M. Troyanov, H. Clémençon, *Voronoi diagrams on piecewise flat surfaces and an application to biological growth*, Theoretical Computer Science 2001 (termination of the flips) |
| Intrinsic mollification of degenerate triangles | N. Sharp, K. Crane, *A Laplacian for Nonmanifold Triangle Meshes*, Computer Graphics Forum (SGP) 2020 |
| Heat method for geodesic distance (also on the intrinsic triangulation) | K. Crane, C. Weischedel, M. Wardetzky, *Geodesics in Heat*, ACM TOG 2013; *The Heat Method for Distance Computation*, CACM 2017 |
| Vector heat method: connection Laplacian, parallel transport, scalar extension, log map | N. Sharp, Y. Soliman, K. Crane, *The Vector Heat Method*, ACM TOG 2019 |
| Log map direction from the gradient of half the squared distance against the transported reference vector | Gauss lemma (eg M. do Carmo, *Riemannian Geometry*, 1992, ch. 3); own derivation, replaces the paper's diffused radial field, which is biased by the shape of the source's one-ring |
| Kahan's stable formula for the triangle area from its sides | W. Kahan, *Miscalculating Area and Angles of a Needle-like Triangle*, 2014 |
| Reverse Cuthill-McKee ordering, envelope (skyline) Cholesky | E. Cuthill, J. McKee, *Reducing the bandwidth of sparse symmetric matrices*, ACM 1969; A. George, J. W. H. Liu, *Computer Solution of Large Sparse Positive Definite Systems*, 1981 |

The comparison with `MeshLocalParam` (the discrete exponential map, R. Schmidt, C. Grimm, B. Wyvill,
*Interactive Decal Compositing with Discrete Exponential Maps*, ACM TOG 2006) is in the tests.

## Benchmarks

`benchmarks/BooleanComparison` runs CGAL (GPL), Cherchi et al. (MIT) and Manifold (Apache-2.0) as separate
programs for comparison only. The driver `external/cgal_bool.cpp` links against CGAL; compiled, it falls under
the GPL and is not distributed. None of these tools is part of the library.
