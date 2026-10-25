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

## Benchmarks

`benchmarks/BooleanComparison` runs CGAL (GPL), Cherchi et al. (MIT) and Manifold (Apache-2.0) as separate
programs for comparison only. The driver `external/cgal_bool.cpp` links against CGAL; compiled, it falls under
the GPL and is not distributed. None of these tools is part of the library.
