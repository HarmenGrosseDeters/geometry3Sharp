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

## Sparse Cholesky (`SparseCholesky`)

Implemented from the papers and textbooks, no code taken over (not from CHOLMOD or CSparse, which are LGPL/GPL, nor from the BSD-licensed SuiteSparse AMD).

| Part | Source |
|---|---|
| Approximate minimum degree ordering on the quotient graph | P. Amestoy, T. Davis, I. Duff, *An Approximate Minimum Degree Ordering Algorithm*, SIAM J. Matrix Anal. Appl. 1996; A. George, J. Liu, *The Evolution of the Minimum Degree Ordering Algorithm*, SIAM Review 1989 |
| Symbolic factorization by the elimination tree, postordering | J. Liu, *The Role of Elimination Trees in Sparse Factorization*, SIAM J. Matrix Anal. Appl. 1990; A. George, J. Liu, *Computer Solution of Large Sparse Positive Definite Systems*, 1981 |
| Relaxed supernodes | C. Ashcraft, R. Grimes, *The Influence of Relaxed Supernode Partitions on the Multifrontal Method*, ACM TOMS 1989 |
| Left-looking supernodal numeric factorization | E. Ng, B. Peyton, *Block Sparse Cholesky Algorithms on Advanced Uniprocessor Computers*, SIAM J. Sci. Comput. 1993 |

## Benchmarks

`benchmarks/BooleanComparison` runs CGAL (GPL), Cherchi et al. (MIT) and Manifold (Apache-2.0) as separate
programs for comparison only. The driver `external/cgal_bool.cpp` links against CGAL; compiled, it falls under
the GPL and is not distributed. None of these tools is part of the library.
