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

## Analysis (`MeshWallThickness`, `MeshDeviation`, `PrimitiveFit3`)

Implemented from the papers, no code taken over.

| Part | Source |
|---|---|
| Shape diameter function (cone of rays, median / standard deviation filter, weights 1/angle) | L. Shapira, A. Shamir, D. Cohen-Or, *Consistent mesh partitioning and skeletonisation using the shape diameter function*, The Visual Computer 2008 |
| Largest inscribed ball per surface point (shrinking ball iteration) | J. Ma, S. W. Bai, X. Hu, Y. J. Kim, *3D medial axis point approximation using nearest neighbors and the normal field*, The Visual Computer 2012 |
| Sign of the distance to a mesh from the angle weighted pseudo-normal of the closest feature | J. A. Baerentzen, H. Aanaes, *Signed distance computation using the angle weighted pseudonormal*, IEEE TVCG 2005 |
| Sign from the generalized winding number | A. Jacobson, L. Kavan, O. Sorkine-Hornung 2013 (see above), via `FastWindingNumber` |
| RANSAC | M. A. Fischler, R. C. Bolles, *Random sample consensus*, Communications of the ACM 1981 |
| Truncated quadratic (MSAC) scoring | P. H. S. Torr, A. Zisserman, *MLESAC: A new robust estimator with application to estimating image geometry*, CVIU 2000 |
| Minimal samples with normals (cylinder from 2 points, cone from 3 points), normal deviation test | R. Schnabel, R. Wahl, R. Klein, *Efficient RANSAC for point-cloud shape detection*, Computer Graphics Forum 2007 |
| Algebraic circle / sphere fit as start value | I. Kasa, *A circle fitting procedure and its error analysis*, IEEE Trans. Instrumentation and Measurement 1976 |
| Levenberg-Marquardt on the orthogonal distances | K. Levenberg 1944; D. W. Marquardt, *An algorithm for least-squares estimation of nonlinear parameters*, SIAM J. Appl. Math. 1963 |
| Normal estimation by PCA of the nearest neighbors | H. Hoppe, T. DeRose, T. Duchamp, J. McDonald, W. Stuetzle, *Surface reconstruction from unorganized points*, SIGGRAPH 1992 |

## Benchmarks

`benchmarks/BooleanComparison` runs CGAL (GPL), Cherchi et al. (MIT) and Manifold (Apache-2.0) as separate
programs for comparison only. The driver `external/cgal_bool.cpp` links against CGAL; compiled, it falls under
the GPL and is not distributed. None of these tools is part of the library.
