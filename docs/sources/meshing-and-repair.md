# Sources: Meshing, Remeshing, Repair, Deformation & Implicits

Back to [Sources Index](../SOURCES.md) · [sources/README.md](README.md)

---

## Surface deformation (`ArapMeshDeformer`)

| Item | Source |
|---|---|
| ARAP surface energy and local/global iterations | O. Sorkine, M. Alexa, *As-Rigid-As-Possible Surface Modeling*, SGP 2007, [DOI](https://doi.org/10.2312/SGP/SGP07/109-116); implemented from the paper. The existing library `SparseCholesky`, `FastQuaternionSVD` and exact orientation predicates are reused. Negative cotangent sums are clamped to zero as an explicit weighting variant; [CGAL's surface deformation manual](https://doc.cgal.org/latest/Surface_mesh_deformation/index.html) describes this remedy (documentation only; no CGAL code read). No libigl source was read or ported. |
| Anderson acceleration of the local/global fixed-point iteration, energy safeguard | H. F. Walker, P. Ni, *Anderson Acceleration for Fixed-Point Iterations*, SIAM J. Numer. Anal. 49(5), 2011, [doi:10.1137/10078356X](https://doi.org/10.1137/10078356X); Y. Peng, B. Deng, J. Zhang, F. Geng, W. Qin, L. Liu, *Anderson Acceleration for Geometry Optimization and Physics Simulation*, ACM TOG 37(4), 2018, [doi:10.1145/3197517.3201290](https://doi.org/10.1145/3197517.3201290); implemented from the papers (no code read). |

## Biharmonic displacement deformation (`BiharmonicMeshDeformer`)

Implemented from the papers; no libigl source (MPL) or other solver code was read or ported. The solve uses the existing `SparseCholesky`.

| Part | Source |
|---|---|
| Squared-Laplacian displacement energy `L^T M^-1 L` with a lumped mass matrix and equality constraints (sections 3.1 and 3.4; the paper's bounds, skinning weights and affine handles are not implemented) | A. Jacobson, I. Baran, J. Popović, O. Sorkine, *Bounded Biharmonic Weights for Real-Time Deformation*, ACM Trans. Graph. 30(4), 2011, [doi:10.1145/1964921.1964973](https://doi.org/10.1145/1964921.1964973) |
| Cotangent stiffness matrix of a triangle mesh | U. Pinkall, K. Polthier, *Computing Discrete Minimal Surfaces and Their Conjugates*, Experimental Mathematics 2(1), 1993, [doi:10.1080/10586458.1993.10504266](https://doi.org/10.1080/10586458.1993.10504266) |
| Barycentric (one third of each incident triangle) lumped mass | M. Meyer, M. Desbrun, P. Schröder, A. H. Barr, *Discrete Differential-Geometry Operators for Triangulated 2-Manifolds*, VisMath 2002 (Springer, 2003), [doi:10.1007/978-3-662-05105-4_2](https://doi.org/10.1007/978-3-662-05105-4_2) |
| Biharmonic energy as the thin-plate / membrane minimizer with point constraints (the continuous background) | M. Botsch, O. Sorkine, *On Linear Variational Surface Deformation Methods*, IEEE TVCG 14(1), 2008, [doi:10.1109/TVCG.2007.1054](https://doi.org/10.1109/TVCG.2007.1054) |

The parked certified-accuracy mode (not in this tree, branch `claude/biharmonic-certificate-parked`) also cited S. M. Rump, *Verification of Positive Definiteness*, BIT 46 (2006), and Diaconis / Stroock and Sinclair for path-congestion eigenvalue bounds; none of that is in the library.

## Alpha wrapping (`MeshAlphaWrap`, `Delaunay3`, `ExactPredicates.InSphere`)

Implemented from the papers, no code taken over. CGAL's implementation (`Alpha_wrap_3`, GPL) was not used.

| Part | Source |
|---|---|
| Alpha wrapping: carving a Delaunay triangulation from outside through facets an alpha ball fits through, Steiner points on the offset surface where the dual Voronoi edge crosses it (rule 1) or where a cell intersects the input (projection of the circumcenter, rule 2), manifold post-processing by moving outside cells to the inside | C. Portaneri, M. Rouxel-Labbé, M. Hemmer, D. Cohen-Steiner, P. Alliez, *Alpha Wrapping with an Offset*, ACM TOG 41(4) (SIGGRAPH) 2022, doi:10.1145/3528223.3530152; author version <https://inria.hal.science/hal-03688637> (reading notes: [design/ALPHA_WRAP_PAPER_NOTES.md](../design/ALPHA_WRAP_PAPER_NOTES.md)) |
| Passage-driven alpha wrap (`PassageWrap`): the clearance bound sqrt((r - offset)^2 - alpha^2) of a wrap from a pin axis, and the open-passage condition alpha + offset < r | own derivation, no source: a barycentric identity (sum l_i \|p_i - m\|^2 = \|q - m\|^2 + sum l_i \|p_i - q\|^2) applied to the facets of the carved wrap (Phi(f) <= alpha at termination, vertices within the offset of the input, paper notes section 2); proof and preconditions in [design/PASSAGE_WRAP.md](../design/PASSAGE_WRAP.md). The a posteriori distance of a polyline to a mesh: branch and bound on the 1-Lipschitz distance function (interval lower bound (fa + fb - L) / 2), the standard Lipschitz global minimisation of P. Hansen, B. Jaumard, S.-H. Lu, *Global optimization of univariate Lipschitz functions*, Math. Programming 55, 1992 |
| Pulled pinch points (`MeshAlphaWrap.PullPinchPoints`, opt-in): where the offset projection of an outside cell's circumcenter lies less than the separation inside its circumsphere, the point on the segment from the circumcenter to that projection exactly the separation inside the sphere (empty ball: the separation holds), used if it is at most 2 offsets from the input; refinement at the spots where the carved surface is not a disk instead of moving cells inside | a point inside the circumball closer than its center, chosen to keep a separation: A. Üngör, *Off-centers: a new type of Steiner points for computing size-optimal quality-guaranteed Delaunay triangulations*, LATIN 2004 (CGTA 42, 2009); refinement where the restricted Delaunay surface is not a manifold: J.-D. Boissonnat, S. Oudot, *Provably Good Sampling and Meshing of Surfaces*, Graphical Models 2005; S.-W. Cheng, T. K. Dey, E. A. Ramos, *Delaunay refinement for piecewise smooth complexes*, SODA 2007; own construction otherwise ([design/ALPHA_WRAP_MANIFOLD_QUALITY.md](../design/ALPHA_WRAP_MANIFOLD_QUALITY.md)) |
| Incremental Delaunay triangulation by cavities | A. Bowyer, *Computing Dirichlet tessellations*, The Computer Journal 1981; D. F. Watson, *Computing the n-dimensional Delaunay tessellation with application to Voronoi polytopes*, The Computer Journal 1981 |
| Round-based parallel carving (Alpha Wrap 3.0 lever (B)): gates taken in priority order, evaluated in parallel against the round's state, committed in priority order where their read set misses the writes kept before them (the others deferred), insertions planned read only, ids assigned serially, writes applied in parallel; the result independent of the thread count | G. E. Blelloch, J. T. Fineman, P. B. Gibbons, J. Shun, *Internally deterministic parallel algorithms can be fast*, PPoPP 2012 (deterministic reservations: reserve in priority order, commit the winners; Delaunay refinement among the examples); independent cavities in parallel Delaunay refinement: A. N. Chernikov, N. P. Chrisochoides, *Three-dimensional Delaunay refinement for multi-core processors*, ICS 2008; the guarantee and determinism argument: [design/ALPHA_WRAP_ROUNDS.md](../design/ALPHA_WRAP_ROUNDS.md) |
| Point location by a stochastic walk | O. Devillers, S. Pion, M. Teillaud, *Walking in a triangulation*, IJFCS 2002 |
| InSphere determinant and its floating-point error bound (filter; the uncertain cases are evaluated in integer arithmetic) | J. R. Shewchuk 1997 (see above) |
| Closest point on a triangle (Voronoi regions), separating axis test of tetrahedron and triangle | C. Ericson, *Real-Time Collision Detection*, 2005 |
| Sharp-feature pass on the raw wrap (`AlphaWrapFeaturePass`, Alpha Wrap 3.0 lever 6): vertices onto the offset surface's planes, feature samples where neighbouring planes disagree (the plane pair's intersection line), edges split and flipped along the feature; the miter line of an offset crease, edge + offset (n1 + n2) / (1 + n1 . n2); every operation checked exactly (no self-intersection, no contact with the input, no input in the swept tetrahedra) | L. Kobbelt, M. Botsch, U. Schwanecke, H.-P. Seidel, *Feature sensitive surface extraction from volume data*, SIGGRAPH 2001 (feature samples from normal disagreement, edge flips along features), moved from grid cells to the wrap's triangles; the sharp-feature study (runs/sharp-features, variant K) |
| Oracle pruning (Alpha Wrap 3.0 L1): a face plane of the tetrahedron against a BVH node box by its lowest / highest corner along the normal; segment against node boxes by slabs with the reciprocals of the direction computed once per query | N. Greene, *Detecting intersection of a rectangular solid and a convex polyhedron*, Graphics Gems IV, 1994; A. Williams, S. Barrus, R. K. Morley, P. Shirley, *An efficient and robust ray-box intersection algorithm*, J. Graphics Tools 10(1), 2005 |

Choices described in the code: traversability by the paper's Phi(f), the smallest empty ball through the facet's
vertices (Definition 2, Fig. 23; Alpha Wrap 2.0. Before, the circumradius of the facet, which refines thin sheets
less; Phi(f) doubles the triangles on a sheet of thickness 0, as CGAL's wrap does there, and the quality refinement needs
it), the order of the gates (largest facet first), labels of new cells (inside) and the removal of
enclosed outside pockets, the manifold step (around a non-manifold edge or vertex the outside part connected
to the box stays outside), and the contact margin of the intersection test (1e-6 of the offset).

Rule 2 projects the cell's own circumcenter, not the end of the clamped dual edge, and keeps the point only if it lies
strictly inside the cell's circumsphere (exact `InSphere`). Then every Steiner point is at least
min(offset, alpha - offset) from the other vertices (up to rounding and the contact margin), which bounds the angles of
the result triangles with a circumradius of at most alpha (asin(offset / (2 alpha)) for an offset below alpha / 2) (derivation in the `MeshAlphaWrap` class doc and at `rule_projection`; the guarantees of the paper
do not include the triangle shape). This is the separation argument of Delaunay refinement, where points are inserted at the centers of empty
balls: L. P. Chew, *Guaranteed-Quality Mesh Generation for Curved Surfaces*, SoCG 1993; J.-D. Boissonnat, S. Oudot,
*Provably Good Sampling and Meshing of Surfaces*, Graphical Models 2005; S.-W. Cheng, T. K. Dey, J. R. Shewchuk,
*Delaunay Mesh Generation*, CRC Press 2012 (the radius-edge ratio and the minimum angle of a triangle).

The refinement at non-manifold spots (`RefineNonManifold`) is our own: the paper leaves pinches in place (Section 8).
Before the manifold step, it inserts offset-surface points into the outside cells that the manifold step would move
inside, with the same separation guards, and carves again. The idea that a dense enough offset sample makes the
restricted Delaunay surface a manifold is from J.-D. Boissonnat, S. Oudot 2005 (above). Measurements are in
`docs/design/ALPHA_WRAP_PAPER_NOTES.md` section 6.

The quality refinement of the result facets (`RefineMinAngleDegrees`) is the quality rule of Chew 1993 and Boissonnat,
Oudot 2005 (above): a facet with a smallest angle below the bound gets the center of its surface Delaunay ball (the
crossing of its dual Voronoi edge with the offset surface), here otherwise the projection of its inside cell's
circumcenter (as rule 2), each only with the separation min(offset, alpha - offset). Measurements are in
`docs/design/ALPHA_WRAP_PAPER_NOTES.md` section 7.

## Alpha wrap quality pass (`AlphaWrapQualityPass`, `MeshRobustRepair.AlphaWrapQualityPass`)

Implemented from the papers, no code taken over (Hu et al.'s implementation builds on CGAL, GPL; it was not looked
at). Design and measurements: [design/ALPHA_WRAP_QUALITY.md](../design/ALPHA_WRAP_QUALITY.md).

| Part | Source |
|---|---|
| Worst-first local operations (split, collapse, relocation) that must not create an angle below the current minimum, no flips, feature vertices fixed (`WorstFirst`, `EnableCollapses`, `PinCreaseVerticesDegrees`, `FollowReference`) | K. Hu, D.-M. Yan, D. Bommes, P. Alliez, B. Benes, *Error-Bounded and Feature Preserving Surface Remeshing with Minimal Angle Improvement*, IEEE TVCG 2017 |
| Caps (large angles) spoil the gradient and the normal (cap splits and cap flips) | I. Babuška, A. K. Aziz, *On the angle condition in the finite element method*, SIAM J. Numer. Anal. 1976; J.-M. Morvan, B. Thibert, *Approximation of the normal vector field and the area of a smooth surface*, Discrete Comput. Geom. 2004 |
| Quality measures (min and max angle reported separately) | J. R. Shewchuk, *What is a good linear finite element?*, 2002 |
| The guard of every operation: `TriangleDeviationBounds.IsWithin` with the patch bound, the exact self-intersection test, the final one-sided Hausdorff certification | this library (#280, `MeshHausdorffDistance`) |

## Tetrahedral meshing (`MeshTetrahedralization`, `Delaunay3.RefinementSupport`, `docs/design/TET_MESHING.md`)

Papers only: TetGen (AGPL), fTetWild / TetWild (MPL), the CDT of Diazzi et al. (GPL / LGPL), CGAL (GPL) are not to be read or ported.
fTetWild is run as a black box only (`.github/workflows/tet-compare.yml`; README build steps and `--help`, no source read).

| Implemented (stage 1) | Source |
|---|---|
| Circumcentre insertion for tets above a radius-edge bound and a size bound, refinement order by a queue | J. R. Shewchuk, *Tetrahedral mesh generation by Delaunay refinement*, SoCG 1998; S.-W. Cheng, T. K. Dey, J. R. Shewchuk, *Delaunay Mesh Generation*, CRC 2012 (DelTetPLC) |
| The floor (no point closer than a fixed distance, so the vertices are a packing and the refinement terminates) | the separation argument of L. P. Chew 1993 and J. Ruppert 1995 (as in the alpha wrap's own refinement, above) |
| Local alpha (`MeshAlphaWrap.AlphaField`, `MeshTetrahedralization.MaxSurfaceDeviation`, TET_MESHING.md 17.13): the graded size function (alpha at a point = the smallest alpha_j + slope * distance over samples; a size field with a limited slope) and the refinement driven by the measured distance of the input to the previous wrap (an a posteriori indicator, adaptive refinement) | P.-O. Persson, *Mesh size functions for implicit geometries and PDE-based gradient limiting*, Engineering with Computers 22, 2006 (the idea of gradient limiting; implemented from it, no code read); the carving criterion itself is Portaneri et al. 2022 section 8 "Spatially-varying parameters" (future work there; ALPHA_WRAP_PAPER_NOTES.md) |
| Sliver-avoiding point choice: a point near the circumcentre whose new tets have no sliver | X.-Y. Li, S.-H. Teng, *Generating well-shaped Delaunay meshes in 3D*, SODA 2001 (fixed candidate directions here instead of random points) |
| Stage 2 (`TetMeshImprover`): hill climbing on the smallest dihedral angle by edge removal (best ring triangulation by dynamic programming, the 3-2 flip as m = 3), face removal (2-3 flip) and vertex smoothing | B. M. Klingner, J. R. Shewchuk, *Aggressive tetrahedral mesh improvement*, IMR 2007 (paper only; Stellar not read); the ring triangulation by dynamic programming as in J. R. Shewchuk, *Two discrete optimization algorithms for the topological improvement of tetrahedral meshes* (unpublished manuscript, 2002) |
| Boundary vertex moves with exact checks (swept region against the input, moved boundary triangles against the boundary) | the library's own, as the sharp-feature pass's sweep test (`AlphaWrapFeaturePass`, #736) |
| The fixed surface (insertions whose cavity reaches the surface undone, on exact labels), peeling of boundary slivers (a 2-2 surface flip) with the exact input test | the library's own; the input test uses `ExactTriangleIntersection` (Devillers and Guigue 2002, above) |
| S1-F crease recovery (`TetFeatureSurface`, TET_MESHING.md 17.14): the featured wrap surface made Delaunay conforming by surface edge flips (an edge flips to the Delaunay one across a flat quad), then Steiner points on missing edges (midpoints) and missing triangles (circumcentre in the plane, longest-edge midpoint otherwise), the encroachment idea of Delaunay refinement, with a budget and a fallback to the rounded wrap instead of a guaranteed protecting-ball recovery; cells labelled by a flood fill that never crosses a surface triangle | J. R. Shewchuk, *Tetrahedral mesh generation by Delaunay refinement*, SoCG 1998; S.-W. Cheng, T. K. Dey, J. R. Shewchuk, *Delaunay Mesh Generation*, CRC 2012; H. Edelsbrunner, N. R. Shah, *Triangulating topological spaces*, SoCG 1994 / IJCGA 1997 (restricted Delaunay); H. Si, K. Gaertner, *Meshing piecewise linear complexes by constrained Delaunay tetrahedralizations*, IMR 2005 (Steiner points for boundary recovery; the idea only) |

| Tet mesh files (`TetMeshWriter`, stage P2; writers only, no readers) | Source |
|---|---|
| Medit `.mesh` ASCII (version 2: Vertices, Triangles, Tetrahedra with references, 1-based, End) | the Medit / libMeshb file format documentation (format only; the libMeshb code was not read) |
| Gmsh MSH 4.1 ASCII (Entities, Nodes and Elements blocks per entity, physical tags) and MSH 2.2 ASCII (Nodes, Elements with tags, element types 2 and 4) | the Gmsh reference manual, chapter "Gmsh file formats" (format only; Gmsh, meshio and TetGen code was not read) |
| VTK legacy ASCII, `DATASET UNSTRUCTURED_GRID`, cell types 10 (tetrahedron) and 5 (triangle), `CELL_DATA` scalars | Kitware, *VTK File Formats* (the legacy format description) |
| Marker and region semantics (the nearest input triangle by `MeshBatchQueries.NearestPointBatch`; regions as connected components by shared facets, union-find) | the library's own; union-find: R. E. Tarjan, *Efficiency of a good but not linear set union algorithm*, J. ACM 22(2), 1975 |


| Read for the design |  |
|---|---|
| Delaunay refinement of tetrahedra (radius-edge bound, encroachment) | J. R. Shewchuk, *Tetrahedral mesh generation by Delaunay refinement*, SoCG 1998; S.-W. Cheng, T. K. Dey, J. R. Shewchuk, *Delaunay Mesh Generation*, CRC 2012 |
| Constrained Delaunay tetrahedralization, existence and recovery | J. R. Shewchuk, *A condition guaranteeing the existence of higher-dimensional constrained Delaunay triangulations*, SoCG 1998; *Updating and constructing constrained Delaunay and constrained regular triangulations by flips*, SoCG 2003; H. Si, K. Gärtner, *Meshing piecewise linear complexes by constrained Delaunay tetrahedralizations*, IMR 2005; H. Si, *TetGen*, ACM TOMS 41(2), 2015; M. Diazzi, D. Panozzo, A. Vaxman, M. Attene, *Constrained Delaunay tetrahedrization: a robust and practical approach*, ACM TOG 2023 (abstract read) |
| Small input angles | S.-W. Cheng, S.-H. Poon, *Three-dimensional Delaunay mesh generation*, DCG 2006; S. Pav, N. Walkington, *Robust three dimensional Delaunay refinement*, IMR 2004 |
| Restricted Delaunay for smooth surfaces | J.-D. Boissonnat, S. Oudot 2005; S. Oudot, L. Rineau, M. Yvinec, *Meshing volumes bounded by smooth surfaces*, IMR 2005 |
| Stage 2 additions (`TetMeshImprover`, TET_MESHING.md section 16): edge splits with roll-back (interior edges, boundary edges in place, the new vertex smoothed, undone unless the neighbourhood's worst angle rises) and contraction of an interior vertex into a neighbour under the link condition; the shape classes of the diagnosis | Vertex insertion with roll-back: B. M. Klingner, J. R. Shewchuk, *Aggressive tetrahedral mesh improvement*, IMR 2007 (paper only; Stellar not read). Link condition: T. K. Dey, H. Edelsbrunner, S. Guha, D. V. Nekhayev, *Topology preserving edge contraction*, Publ. Inst. Math. (Beograd) 66, 1999. Shape classes: S.-W. Cheng, T. K. Dey, H. Edelsbrunner, M. A. Facello, S.-H. Teng, *Sliver exudation*, J. ACM 2000 |
| Stage 2 rescue phase (`TetMeshImprover`, TET_MESHING.md section 17.11): smoothing along the active-set direction, the least-norm point of the convex hull of the near-worst tets' gradients (Gilbert's iteration) | L. A. Freitag, M. T. Jones, P. E. Plassmann, *An efficient parallel algorithm for mesh smoothing*, IMR 1995; E. G. Gilbert, *An iterative procedure for computing the minimum of a quadratic form on a convex set*, SIAM J. Control 4, 1966 |
| Stage 2 option `MaxDihedralDegrees` (`TetMeshImprover`): the objective min(smallest dihedral, F + G - largest dihedral) on both ends of the dihedral range | B. M. Klingner, *Tetrahedral Mesh Improvement*, PhD thesis, UC Berkeley, 2009, ch. 3-5 (paper only; Stellar not read); the guard and the staged first phase are our own |
| Sliver removal | S.-W. Cheng, T. K. Dey, H. Edelsbrunner, M. A. Facello, S.-H. Teng, *Sliver exudation*, J. ACM 2000; J. Tournois, R. Srinivasan, P. Alliez, *Perturbing slivers in 3D Delaunay meshes*, IMR 2009; J. Tournois, C. Wormser, P. Alliez, M. Desbrun, *Interleaving Delaunay refinement and optimization for practical isotropic tetrahedron mesh generation*, ACM TOG 28(3), 2009; B. M. Klingner, J. R. Shewchuk, *Aggressive tetrahedral mesh improvement*, IMR 2007; J. R. Shewchuk, *Two discrete optimization algorithms for the topological improvement of tetrahedral meshes* |
| Envelope and lattice approaches | Y. Hu et al., *Tetrahedral meshing in the wild*, ACM TOG 37(4), 2018; Y. Hu, T. Schneider, B. Wang, D. Zorin, D. Panozzo, *Fast tetrahedral meshing in the wild*, ACM TOG 39(4), 2020 (arXiv 1908.03581, abstract read); F. Labelle, J. R. Shewchuk, *Isosurface stuffing: fast tetrahedral meshes with good dihedral angles*, ACM TOG 26(3), 2007 |

## Cross field and principal curvature (`CrossField`, `MeshPrincipalCurvature`)

Implemented from the papers only (owner 2026-11-17: no code of Instant Meshes, QuadriFlow, QuadWild, libigl, Geogram's quad
code or any other implementation was read). Stage 1 of [design/QUAD_REMESHING.md](../design/QUAD_REMESHING.md).

| Part | Source |
|---|---|
| The smoothest 4-RoSy field as the smallest eigenvector of the Hermitian connection Laplacian against the vertex areas (vertex based discretization: tangent frames, discrete Levi-Civita transport, cotangent weights, singularity index per face; Poincare-Hopf) | F. Knöppel, K. Crane, U. Pinkall, P. Schröder, *Globally Optimal Direction Fields*, ACM TOG 32(4), SIGGRAPH 2013 |
| Alignment to boundary and crease edges (a linear problem with prescribed unit values, as in the paper's constraints) and to curvature directions (a soft term in the eigenproblem). The formulation is ours: the weights (K1 - K2)^2 a^2 M, the unknown t without mass eliminated by a rank one update, the gauge of the phase | own choice, documented in `CrossField` |
| Its solver: the Lanczos method with full reorthogonalization and restarts on (K + eps M)^-1 M, which is self-adjoint for the M inner product; the Sherman-Morrison formula for the rank one update | G. H. Golub, C. F. Van Loan, *Matrix Computations*, 4th ed. 2013, Sec. 10.1 (Lanczos), Sec. 2.1.4 (Sherman-Morrison); the dense eigenproblem of the tridiagonal matrix by `SymmetricEigenSolver` |
| Principal curvatures and directions per vertex: the second fundamental form per triangle by least squares over the edges (differences of the vertex normals), rotated into the vertex frame, averaged with Voronoi corner areas | S. Rusinkiewicz, *Estimating Curvatures and Their Derivatives on Triangle Meshes*, 3DPVT 2004; M. Meyer, M. Desbrun, P. Schröder, A. H. Barr, *Discrete Differential-Geometry Operators for Triangulated 2-Manifolds*, VisMath 2002 (Voronoi areas); N. Max, *Weights for Computing Vertex Normals from Facet Normals*, J. Graphics Tools 1999 (angle weighted normals) |
| Smoothing of the curvature tensors over the one-ring (against noise) | own choice |
| `SparseHermitianCholesky` (the field matrix as a complex Hermitian system of n unknowns): the analysis of `SparseCholesky` (approximate minimum degree, elimination tree, relaxed supernodes) and a left-looking supernodal Cholesky in complex arithmetic | A. George, J. Liu, *The Evolution of the Minimum Degree Ordering Algorithm*, SIAM Review 1989; P. Amestoy, T. Davis, I. Duff, *An Approximate Minimum Degree Ordering Algorithm*, SIAM J. Matrix Anal. Appl. 1996; E. Ng, B. Peyton, *Block sparse Cholesky algorithms on advanced uniprocessor computers*, SIAM J. Sci. Comput. 1993; C. Ashcraft, R. Grimes, *The influence of relaxed supernode partitions on the multifrontal method*, ACM TOMS 1989. No CHOLMOD / SuiteSparse / Eigen / MUMPS / PARDISO code read or taken over |

## Position field and quad extraction (`PositionField`, `QuadRemesher`, `PolyMeshMetrics`)

Implemented from the paper only (owner 2026-11-17: the Instant Meshes code has an Enhancements clause in its licence that the owner
did not accept; no code of Instant Meshes, QuadriFlow, QuadWild or libigl was read). Stage 2 of
[design/QUAD_REMESHING.md](../design/QUAD_REMESHING.md); the derivations and the own choices are in section 3.1 and 4.2 there.

| Part | Source |
|---|---|
| Position field (a point per vertex that stands for a lattice with the orientation field), compatibility of two lattices (rotation of the 4-RoSy, the lattice points near a common point, rounding to the lattice of each vertex), Gauss-Seidel smoothing on a multiresolution hierarchy, extraction from the integer offsets of the edges (merge of equal lattice points, unit offsets are edges, faces traced in the tangent planes) and its cleanup | M. Jakob, M. Tarini, D. Panozzo, O. Sorkine-Hornung, *Instant Field-Aligned Meshes*, ACM TOG 34(6) 189, SIGGRAPH Asia 2015 |
| The hierarchy (greedy matching of the closest neighbours, area weighted position, normal and orientation), the point closest to the edge midpoint on both tangent planes and its regularization, the mean misfit as the Gauss-Seidel step, boundary vertices on a lattice line and boundary corners as lattice points | own choices (QUAD_REMESHING.md 3.1) |
| Deterministic parallel Gauss-Seidel: a fixed greedy colouring in vertex order, the colours one after the other, so that serial and parallel runs execute the same updates | multi-colour Gauss-Seidel, standard (eg Y. Saad, *Iterative Methods for Sparse Linear Systems*, 2nd ed. 2003, section 12.4) |
| The quality numbers of `PolyMeshMetrics` (face mix, valence, corner angles, manifold polygon mesh) | QUAD_REMESHING.md section 1 |
| Size field and rotation of the cross field that make the lattice frames close up (`IntegrableFrames`, `QuadRemesher.SizeAdaptation`): the idea of a free scale and rotation of the frames in the global parametrization, with the integrability of a coframe (d(e^-sigma theta_k) = 0) and Gauss-Bonnet for the floor of a uniform spacing. The formulation (the per-triangle residual w in lattice units, Gauss-Newton with sparse Cholesky and a mean-zero gauge, backtracking, the position field with a spacing per vertex) is ours, written from the papers' idea, no code read | D. Bommes, H. Zimmer, L. Kobbelt, *Mixed-Integer Quadrangulation*, ACM TOG 28(3) 77, SIGGRAPH 2009 (idea only); for the damping of the normal equations: K. Levenberg, *A method for the solution of certain non-linear problems in least squares*, Quart. Appl. Math. 2, 1944 |

## Pure quads (`QuadRemesher.PureQuads`, `QuadFlowRepair`, `MinCostFlow`, `GroupSatSolver`)

Stage 3B of [design/QUAD_REMESHING.md](../design/QUAD_REMESHING.md), implemented from the papers and textbooks only (owner 2026-11-19: no
code of QuadriFlow, Instant Meshes, QuadWild, LEMON, MiniSat or MapleSAT, and no other quad meshing, flow or SAT solver code, was read).

| Part | Source |
|---|---|
| The repair of the position field's integer offsets: the integer variable per edge in the lattice frame of each triangle with the frame transition, the regularity constraint (the three offsets of a triangle sum to zero) as a network flow after a breadth-first rotation of the equations, the capacity growing until the flow is full, subdivision of edges with a large offset, the greedy move of vertices, the local SAT repair of inverted triangles, the position solve over the clusters of zero offsets, and the extraction of quads by pairing triangles over their hypotenuses (sections 3.2-3.6) | J. Huang, Y. Zhou, M. Niessner, J. R. Shewchuk, L. J. Guibas, *QuadriFlow: A Scalable and Robust Method for Quadrangulation*, Computer Graphics Forum 37(5), 2018 (the paper only) |
| `MinCostFlow`: successive shortest paths with node potentials, Dijkstra with early termination at the first node with a deficit (the unsettled nodes keep their potential relative to the distance of that node) per source node of the excess network | R. K. Ahuja, T. L. Magnanti, J. B. Orlin, *Network Flows: Theory, Algorithms, and Applications*, Prentice Hall 1993, section 9.7 (successive shortest path algorithm; node potentials and reduced costs as defined there) |
| The sign-aware unit cost of a step of the flow (the marginal increase of the distance to the real offset, two tiers, times 100 when a step grows the offset against the sign of the real offset): own choice; it softens the frame-aligned orientation constraints of the integer-grid map | M. Bommes, M. Campen, H.-C. Ebke, P. Alliez, L. Kobbelt, *Integer-Grid Maps for Reliable Quad Meshing*, ACM TOG 32(4) 98, SIGGRAPH 2013 |
| `GroupSatSolver`: unit propagation over watched literals and chronological backtracking with "exactly one" variable groups, a decision count as the budget (never a clock) | M. Davis, H. Putnam, *A computing procedure for quantification theory*, JACM 7(3), 1960; M. Davis, G. Logemann, D. Loveland, *A machine program for theorem-proving*, CACM 5(7), 1962; M. Moskewicz, C. Madigan, Y. Zhao, L. Zhang, S. Malik, *Chaff: Engineering an Efficient SAT Solver*, DAC 2001 (the two watched literals; from the description) |
| The implication encoding of the triangle constraints (the last mutable offset of a triangle is determined by the others), the total decision budget of 300k, the priority-queue subdivision with the order (largest offset, triangle, slot), the exact integer fold count (det < 0), the contracted position solve, the fallback to the stage 2 mesh | own choices (benchmarks/QuadFlow3BGate/README.md, QUAD_REMESHING.md 4.4) |

## Seamless-parametrization seed for quad remeshing (research spike, `benchmarks/QuadSeamlessSeedSpike`; not library code)

Research note [research/QUAD_SEAMLESS_SEED_2026-11.md](../research/QUAD_SEAMLESS_SEED_2026-11.md). Papers only: the code released with the two
2025 papers is AGPL-3.0-or-later (Corman, Crane) and GPL-3.0 / MPL-2.0 (Capouellez et al.); only the README and the licence file names were read,
no source file. No code of QuadriFlow, Instant Meshes, QuadWild, CoMISo, libigl, geometry-central or any other implementation was read.

| Part | Source |
|---|---|
| The rectangular model studied (two axis scales and a frame rotation; the conformal case as its `v = 0` member); the benchmark's two-scale least squares is our own formulation of that freedom, not the paper's constrained solve | E. Corman, K. Crane, *Rectangular Surface Parameterization*, ACM TOG 44(4), SIGGRAPH 2025, DOI 10.1145/3731176 (paper and supplement) |
| Penner coordinates, cone and holonomy constraints, feature alignment (studied, not implemented) | R. Capouellez, R. Singh, M. Heistermann, D. Bommes, D. Zorin, *Feature-Aligned Parametrization in Penner Coordinates*, ACM TOG 44(4), SIGGRAPH 2025, DOI 10.1145/3731216; R. Capouellez, D. Zorin, *Seamless Parametrization in Penner Coordinates*, ACM TOG 43(4), 2024, DOI 10.1145/3658202; *Metric Optimization in Penner Coordinates*, ACM TOG 42(6), 2023, DOI 10.1145/3618394 |
| The benchmark's oracle: a discrete conformal metric with prescribed cone angles by Newton's method on the convex energy (gradient: target angle minus angle sum; Hessian: the cotangent Laplacian of the scaled metric; a broken triangle counts with the angles pi, 0, 0), fixed connectivity | B. Springborn, P. Schröder, U. Pinkall, *Conformal Equivalence of Triangle Meshes*, ACM TOG 27(3), SIGGRAPH 2008 (from the paper, cited from memory) |
| The cones from the field's singular faces, the parallel cross field of the flat metric by a breadth-first walk, the transfer to a spacing and a rotation per vertex, the spacing clamp | own choices (the benchmark README) |

## Polygon mesh (`DPolyMesh`, `PolyMeshIO`; quad remeshing stage 5, [design/QUAD_REMESHING.md](../design/QUAD_REMESHING.md))

Written from the format descriptions and the textbook method; no code from any other library was read or taken.

| Topic | Source |
|---|---|
| Face triangulation (`DPolyMesh.TriangulateFace`): ear clipping in the plane of the Newell normal; the existence of an ear in every simple polygon | G. H. Meisters, *Polygons have ears*, American Mathematical Monthly 82(6), 648-651, 1975; the Newell normal of a polygon (sum of cross products of its edge vectors) as in the standard computer graphics texts. The scan order, tie rules and fallbacks are our own and stated in the class remarks |
| PLY with polygon faces (`list uchar int vertex_indices`, an extra `face_group` property) | G. Turk, *The PLY Polygon File Format*, Stanford University, 1994; P. Bourke, [PLY format notes](http://paulbourke.net/dataformats/ply/) |
| OBJ polygon faces (`f` with 3 or more vertices, `v/vt/vn` forms, negative indices, `g` groups) | Wavefront OBJ format as described in P. Bourke, [OBJ notes](http://paulbourke.net/dataformats/obj/) |

## 2D meshing (`DelaunayMesh2`, `TriangulationMesh2`, `ConstrainedDelaunayMesh2`)

Clean room ([MESHING2D_SPEC.md](../design/MESHING2D_SPEC.md)): implemented from the papers below. Triangle and Triangle.NET were not looked at.

| Part | Source |
|---|---|
| Incremental Delaunay triangulation by cavities | A. Bowyer, *Computing Dirichlet tessellations*, The Computer Journal 1981; D. F. Watson, *Computing the n-dimensional Delaunay tessellation with application to Voronoi polytopes*, The Computer Journal 1981 |
| Ghost triangles at a vertex at infinity instead of a supertriangle; their conflict rule | S.-W. Cheng, T. K. Dey, J. R. Shewchuk, *Delaunay Mesh Generation*, CRC Press 2012, chapter 3 |
| Insertion order: biased randomized insertion rounds, each along a space-filling curve | N. Amenta, S. Choi, G. Rote, *Incremental constructions con BRIO*, SoCG 2003; D. Hilbert, *Über die stetige Abbildung einer Linie auf ein Flächenstück*, Math. Annalen 1891; A. R. Butz, *Alternative algorithm for Hilbert's space-filling curve*, IEEE Trans. Computers 1971 |
| Point location by a visibility walk with a randomized edge order; jump-and-walk for queries | O. Devillers, S. Pion, M. Teillaud, *Walking in a triangulation*, IJFCS 2002; E. P. Mücke, I. Saias, B. Zhu, *Fast randomized point location without preprocessing in two- and three-dimensional Delaunay triangulations*, SoCG 1996 |
| Local Delaunay criterion (verification); the Delaunay subdivision and its cells with cocircular points (tie rule) | C. L. Lawson, *Software for C1 surface interpolation*, 1977; B. Delaunay, *Sur la sphère vide*, 1934 |
| Constrained Delaunay triangulation: definition and local criterion | D. T. Lee, A. K. Lin, *Generalized Delaunay triangulations for planar graphs*, DCG 1986; L. P. Chew, *Constrained Delaunay triangulations*, Algorithmica 1989 |
| Point insertion by splitting and flips that do not cross constraints; restoring the property after a segment | C. L. Lawson 1977 (see above) |
| Segment recovery: walk along the segment, flip the crossed edges until it is an edge | S. W. Sloan, *A fast algorithm for generating constrained Delaunay triangulations*, Computers & Structures 1993 |
| Noding: crossings computed exactly with integers (every double is an integer times a power of two), rounded to nearest | Shewchuk 1997 (see above) for the integer view of doubles; the rounding of the output to doubles is decision D1 of MESHING2D_DECISIONS.md |
| Domain by the even-odd rule within a ring; cells of the constraints for seeds | the domain definition of MESHING2D_SPEC.md and decisions D5, D6, D16 |
| Exact predicates | `ExactPredicates.Orient2d`, `InCircle` (see above) |
| Pseudorandom numbers (insertion rounds, walk) | SplitMix64 (Steele, Lea, Flood, OOPSLA 2014); xorshift (G. Marsaglia, *Xorshift RNGs*, J. Stat. Software 2003) |
| Domain by role-based nesting (D5, owner 2026-11-01) | OGC, *Simple Feature Access, Part 1: Common Architecture* (OGC 06-103r4, ISO 19125-1:2004), Polygon and MultiPolygon; ESRI, *Shapefile Technical Description*, 1998 (polygon rings); even-odd and nonzero fill rules (Adobe, *PostScript Language Reference*; W3C, *SVG* `fill-rule`); A. Johnson, Clipper2 documentation, FillRule |
| Literature check of the decisions (MESHING2D_DECISIONS.md) | C. K. Yap, *Towards exact geometric computation*, CGTA 1997; L. Guibas, D. Salesin, J. Stolfi, *Epsilon geometry*, SoCG 1989; D. H. Greene, F. F. Yao, *Finite-resolution computational geometry*, FOCS 1986; V. Milenkovic, *Verifiable implementations of geometric algorithms using finite precision arithmetic*, Artificial Intelligence 1988; J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 1999; D. Halperin, E. Packer, *Iterated snap rounding*, CGTA 2002; H. Edelsbrunner, E. P. Mücke, *Simulation of Simplicity*, ACM TOG 1990; O. Devillers, M. Teillaud, *Perturbations for Delaunay and weighted Delaunay 3D triangulations*, CGTA 2011; P.-O. Persson, G. Strang, *A simple mesh generator in MATLAB*, SIAM Review 2004; M. de Berg, O. Cheong, M. van Kreveld, M. Overmars, *Computational Geometry*, 3rd ed. 2008; M. Heller, *Triangulation algorithms for adaptive terrain modeling*, SDH 1990 |
| `m2.snap` (`comp_geom/SnapRounding2.cs`, implemented from Hobby 1999 and Belussi et al. 2016, see MESHING2D_PROVENANCE.md); literature check in MESHING2D_SNAP.md | J. D. Hobby 1999 and D. Halperin, E. Packer 2002 (see above); L. Guibas, D. Marimont, *Rounding arrangements dynamically*, IJCGA 1998; M. Goodrich, L. Guibas, J. Hershberger, P. Tanenbaum, *Snap rounding line segments efficiently in two and three dimensions*, SoCG 1997; M. de Berg, D. Halperin, M. Overmars, *An intersection-sensitive algorithm for snap rounding*, CGTA 2007; E. Packer, *Iterated snap rounding with bounded drift*, CGTA 2008; J. Hershberger, *Stable snap rounding*, CGTA 2013; J. Hershberger, *Snap rounding: a cautionary tale*, SoCG 2025; A. Belussi, S. Migliorini, M. Negri, G. Pelagatti, *Snap rounding with restore*, ACM TSAS 2016; E. Packer, *Extending the power of snap rounding variants*, CCCG 2007; V. Milenkovic, *Shortest path geometric rounding*, Algorithmica 2000 |
| `m2.snap` refinement on the grid (`QualityMesh2` with `SnapTolerance`; MESHING2D_SNAP.md section 3g, MESHING2D_PROVENANCE.md) | J. Ruppert 1995 and J. R. Shewchuk, *Delaunay refinement algorithms for triangular mesh generation*, CGTA 2002 (refinement); M. Bern, D. Eppstein, J. Gilbert, *Provably good mesh generation*, JCSS 1994 (grid vertices, abstract); M. Diazzi, D. Panozzo, A. Vaxman, M. Attene, *Constrained Delaunay tetrahedrization: a robust and practical approach*, 2023 (why rounding breaks the proofs, abstract); A. Üngör, *Off-centers*, LATIN 2004 (considered, not built) |
| Triangle's .node / .poly / .ele files (`tools/g3cli/TrianglePolyFormat.cs`: g3cli `mesh2d`, Meshing2Benchmark `mesh`; not in the library) | The published file format descriptions only: J. R. Shewchuk, *Triangle* file formats, https://www.cs.cmu.edu/~quake/triangle.node.html, triangle.poly.html, triangle.ele.html (layout of the files; what -p does with segments and holes). No code of Triangle or Triangle.NET read. The domain of a .poly is built from our own rings: per connected group of segments without bridges (Tarjan, *Depth-first search and linear graph algorithms*, SIAM J. Computing 1972), the outer face of the plane graph, traced by angular order, becomes an outer ring (own construction); crossings found with `SegmentQuadtree2d.QueryIntersections` |
| Behavioural answers on snap rounding (MESHING2D_SNAP_QA.md; behaviour and test inputs only, no code) | The owner's own commits in his fork of Triangle.NET (https://github.com/HarmenGrosseDeters/Triangle.NET, ccee980..504ab65), read only by a separate clean-room comparison session, answered in specification terms and checked by the coordinator; the implementing session never read the fork |

## Historical meshing recheck tools and data

The standalone Python preparation tool in `benchmarks/Meshing2Benchmark/rechecks`
uses installed public parsing APIs only. It does not supply meshing algorithms or
dependencies to geometry3Sharp, and no implementation is copied or inspected.

| Item | Use and license evidence |
|---|---|
| Shapely 2.1.2 | WKT/WKB parsing only; [BSD-3-Clause license at tag 2.1.2](https://github.com/shapely/shapely/blob/2.1.2/LICENSE.txt). Its binary wheel bundles GEOS under LGPL-2.1; the installed wheel includes `LICENSE_GEOS`. GEOS is run as a tool dependency; its implementation is not read, ported, vendored or linked into this library. |
| NumPy 2.5.4 | Pinned Shapely dependency; installed distribution's `licenses/LICENSE.txt` records BSD-3-Clause for NumPy and bundled-component notices (also 0BSD, MIT, Zlib and CC0-1.0). This is a separate Python tool environment. |
| GEOS XML test data | Five XML fixtures fetched at commit `159cc1f9dba3ee820e31123af4f90a2b6e090546`; URLs and SHA-256 values are in `rechecks/historical-exceptions.json`. The pinned upstream [COPYING](https://github.com/libgeos/geos/blob/159cc1f9dba3ee820e31123af4f90a2b6e090546/COPYING) provides LGPL-2.1; no separate permissive grant is assumed for these fixtures. Only external data is parsed at run time. Neither the fixtures nor converted geometries or meshes are redistributed in this repository. |

The committed manifest and reports contain source identifiers, hashes and numeric
results. Reference-producer implementations, including Triangle/Triangle.NET and
the owner's fork, remain outside the clean-room session.

## Dual contouring and winding number solids (`DualContouring`, `WindingNumberOctree`, `MeshWindingNumberSolidify`, `MeshWindingNumberBoolean`)

| Part | Source |
|---|---|
| Dual contouring with QEF vertices from Hermite data | T. Ju, F. Losasso, S. Schaefer, J. Warren, *Dual Contouring of Hermite Data*, ACM TOG (SIGGRAPH) 2002 |
| One vertex per surface patch of a cell (manifold dual contouring) | S. Schaefer, T. Ju, J. Warren, *Manifold Dual Contouring*, IEEE TVCG 2007 |
| Adaptive simplification (`AdaptiveDualContouring`, `docs/design/DC_ADAPTIVE_SIMPLIFICATION.md`): QEFs merged by union of the Hermite crossings, octree vertex clustering with a connected-component (one vertex per sheet) rule, merge test on the largest plane distance, topology and self-intersection check with take-back of clusters; own check of the combinatorial manifold condition | T. Ju, F. Losasso, S. Schaefer, J. Warren, *Dual Contouring of Hermite Data*, SIGGRAPH 2002 (octree simplification, mergeable QEFs); S. Schaefer, T. Ju, J. Warren, *Manifold Dual Contouring*, IEEE TVCG 2007 (clustering with a manifold criterion); T. Ju, T. Udeshi, *Intersection-free contouring on an octree grid*, Pacific Graphics 2006 (through Wang's summary above); the 2024 octree vs k-d tree comparison of manifold dual contouring in Informatica (abstract only). Papers only, no code read |
| Refinement at ambiguous faces | R. Zint, R. Grosso, P. Guertler, *Resolving non-manifoldness on meshes from dual marching cubes*, 2022 |
| Envelope triangulation of `DualContouring` (forced diagonals, edge vertices, the check of the uncovered triangles only), the measurement hook (`DualContouring.CollectDiagnostics`, `benchmarks/DualContouringIntersections`) and the options of `docs/design/DC_INTERSECTION_FREE.md` | C. C. L. Wang, *Intersection-free dual contouring on uniform grids: an approach based on convex/concave analysis*, technical report, The Chinese University of Hong Kong (read in full); T. Ju, T. Udeshi, *Intersection-free contouring on an octree grid*, Pacific Graphics 2006 (through Wang's summary); J. Manson, S. Schaefer, *Isosurfaces over simplicial partitions of multiresolution grids*, CGF 2010 [abstract]. No code read |
| Topology certificate of the contouring (`ContouringTopologyCertificate`, `ContouringLevelSet`, `benchmarks/ContouringCertificate`; the proof in `docs/design/CONTOURING_TOPOLOGY.md`, section 7) | J. W. Alexander, *On the subdivision of 3-space by a polyhedron*, Proc. Natl. Acad. Sci. 10, 1924 (a PL 2-sphere in the 3-sphere bounds a ball on each side; with the cone extension and the Alexander trick, Theorem 1); the Jordan-Schoenflies theorem in the plane (any topology text, eg E. E. Moise, *Geometric Topology in Dimensions 2 and 3*, Springer 1977). The Euler characteristic by counting (Theorem 3) is inclusion and exclusion over convex pieces. The filter of `may_meet_box`: the dot-product error bound of N. J. Higham, *Accuracy and Stability of Numerical Algorithms*, SIAM 2002, section 3.1. Prior art, background only (no premise of the proof): J. M. Snyder, *Interval analysis for computer graphics*, SIGGRAPH 1992; S. Plantinga, G. Vegter, *Isotopic approximation of implicit curves and surfaces*, SGP 2004; G. Varadhan, S. Krishnan, T. V. N. Sriram, D. Manocha, *Topology preserving surface extraction using adaptive subdivision*, SGP 2004 (the table of the design note, section 1). Theorem 6 (blocks of cells, section 7.10, proof only): the Euler characteristic as a valuation on compact polyhedra (H. Hadwiger, *Vorlesungen über Inhalt, Oberfläche und Isoperimetrie*, Springer 1957), χ(∂M) = 2χ(M) for a compact 3-manifold (Poincaré duality, any algebraic topology text, eg A. Hatcher, *Algebraic Topology*, 2002), the classification of compact surfaces; the finger move as the picture only. The growth of the blocks (stage 3, section 7.12): a dilation checked by the ball test and the two counts, no source; prior art, background only: G. Bertrand, G. Malandain, *A new characterization of three-dimensional simple points*, Pattern Recognit. Lett. 15(2), 1994; Z. Aktouf, G. Bertrand, L. Perroton, *A three-dimensional holes closing algorithm*, Pattern Recognit. Lett. 23(5), 2002 (abstract). No code read |
| Measurement prototype `benchmarks/ContouringSubgridProbe` (no library code; `docs/research/CONTOURING_SDF_SUBGRID_2026-11.md`): a restricted subgrid marching tetrahedra, a local empty-sphere Hermite estimator, a cell count of an interval subdivision | H. Baktash, M. Gillespie, K. Crane, *Subgrid Marching Tetrahedra*, ACM TOG 2026 (arXiv 2606.00454; paper text only, own fills); X. Carrera, N. Wang, C. Batty, O. Stein, S. Sellán, *Dual Contouring of Signed Distance Data*, SIGGRAPH 2026 (arXiv 2604.00157; paper text only, its code builds on libigl and was not opened; only the empty-sphere reading of a sample is used); S. Plantinga, G. Vegter, *Isotopic implicit surface meshing*, SGP 2004, as restated by M. Burr, W. Byrd, *Certified simultaneous isotopic approximation of curves via subdivision*, arXiv 2302.04908 (the two cell tests) |
| Fast winding numbers of the input (inside test of the solids) | G. Barill, N. Dickson, R. Schmidt, D. I. W. Levin, A. Jacobson, *Fast Winding Numbers for Soups and Clouds*, ACM TOG (SIGGRAPH) 2018 (existing `FastWindingNumber`) |

## Mesh offset (`MeshOffset`, stage A1 implemented; design note [design/MESH_OFFSET.md](../design/MESH_OFFSET.md))

Papers only; no code of these works was read (owner / coordinator rule 2026-11-13). Marks as in the design note:
details recalled from memory there are checked before the implementation cites them.

| Part | Source |
|---|---|
| Offset and inset of a solid as Minkowski sum / difference with a ball (definitions) | J. R. Rossignac, A. A. G. Requicha, *Offsetting operations in solid modelling*, Computer Aided Geometric Design 3(2), 1986 |
| Topology and two-sided Hausdorff bound of a contoured distance field by per-cell tests on an adaptive grid (prior art of the certificate) | G. Varadhan, D. Manocha, *Accurate Minkowski sum approximation of polyhedral models*, Pacific Graphics 2004; Graphical Models 68(4), 2006, doi:10.1016/j.gmod.2005.11.003 |
| Offset by dual contouring on a topology-adapted octree (prior art; manifold output) | D. Zint, N. Maruani, M. Rouxel-Labbé, P. Alliez, *Feature-Preserving Offset Mesh Generation from Topology-Adapted Octrees*, Computer Graphics Forum 42(5) (SGP 2023), doi:10.1111/cgf.14906 |
| Offsets by combinatorial insertion in a tetrahedral mesh (prior art; not followed) | D. Zint, Z. Chen, Y. Zhu, D. Zorin, T. Schneider, D. Panozzo, *Topological Offsets*, ACM TOG (SIGGRAPH 2025), arXiv:2407.07725 |
| Intersection-free contouring of a narrow-band signed distance by convex / concave analysis (the criterion of `DualContouring` option A) | S. Liu, C. C. L. Wang, *Fast Intersection-Free Offset Surface Generation From Freeform Models With Triangular Meshes*, IEEE Trans. Automation Science and Engineering 8(2), 2011 |
| Volumetric offsets with feature-sensitive extraction (prior art) | D. Pavić, L. Kobbelt, *High-Resolution Volumetric Computation of Offset Surfaces with Feature Preservation*, Computer Graphics Forum 27(2) (Eurographics 2008) |
| Generalized gradient of a locally Lipschitz function (the distance-field enclosure of stage A2) | F. H. Clarke, *Optimization and Nonsmooth Analysis*, Wiley 1983, chapter 2 |
| Exact polytope Minkowski sums (the test oracle's idea; CGAL is GPL and not read) | P. Hachenberger, *Exact Minkowski sums of polyhedra and exact and efficient decomposition of polyhedra into convex pieces*, Algorithmica 55, 2009, doi:10.1007/s00453-008-9219-6 |
| Band certificate (`MeshOffset.CertifyBand`): the 1-Lipschitz bound of the signed distance over a triangle, the distance enclosed by interval arithmetic (an explicit point of the nearest triangle above, the support bound w . (c - x) / \|w\| below), splits by dyadic barycentric coordinates | own construction (design note section 10); the interval rule of `ExactInterval` (one ulp per operation); dual contouring as in its own entry (Ju et al. 2002, Schaefer et al. 2007) |
| Thread scaling of the boolean and the rounding, lever list (`docs/perf/BOOLEAN_THREAD_SCALING.md`, analysis only) | Prior-art check, abstracts and search snippets only, no code read: V. Rokos, G. J. Gorman, P. H. J. Kelly, *A thread-parallel algorithm for anisotropic mesh adaptation*, arXiv 1308.2480 (independent sets by colouring, deferred commits); *PaMO: Parallel Mesh Optimization for Intersection-Free Low-Poly Modeling on the GPU*, arXiv 2509.05595, 2025 (independent regions, intersection check, undo); M. T. Goodrich, J. Hershberger, T. Tanenbaum, output-sensitive snap rounding, SoCG 1997 (no parallel snap rounding paper found); EMBER (above) for the parallel design. Amdahl's law for the bounds. No code taken over |
| Plane-based (EMBER-style) kernel study (`docs/research/PLANE_BASED_KERNEL_STUDY_2026-11.md`, analysis only) | Papers only, no code read: J. Nehring-Wirxel, P. Trettner, L. Kobbelt, *Fast exact booleans for iterated CSG using octree-embedded BSPs*, CAD 135, 2021, arXiv 2103.02486 (read in HTML, sections 1-5: fixed-width 128-256-bit plane arithmetic, quantized input, no floating-point filters, Table 1 cycle counts); P. Trettner, J. Nehring-Wirxel, L. Kobbelt, *EMBER*, ACM TOG 41(4), 2022 (not reachable; numbers from EXACT_BOOLEAN_30_PRESTUDY.md) |

## TPMS lattices (`TpmsLattice`)

Implemented from the published formulas and papers; no code taken from any library. Design:
[design/TPMS_LATTICE.md](../design/TPMS_LATTICE.md).

| Part | Source |
|---|---|
| Gyroid | A. H. Schoen, *Infinite periodic minimal surfaces without self-intersections*, NASA Technical Note D-5541, 1970 |
| Schwarz P and D | H. A. Schwarz, *Gesammelte Mathematische Abhandlungen* (vol. 1), 1890 |
| Lidinoid (discovery) and its nodal approximation | S. Lidin, S. Larsson, *Bonnet transformation of infinite periodic minimal surfaces with hexagonal symmetry*, J. Chem. Soc., Faraday Trans. 86(5) (1990) 769, DOI 10.1039/FT9908600769 (title, authors Sven Lidin and Stefan Larsson, journal, volume, issue, first page and year checked against the Crossref record on 2026-11-15; the Crossref record gives no end page, the earlier "769-775" is unverified; the paper itself was not opened, so that it defines the Lidinoid is the common attribution, not checked here); the trigonometric form used (below) was cross-checked against other published forms by the reviewer: the same terms with the constant +0.15 appear in patent literature; implementations that omit the two factors 0.5 use +0.3, which is the same zero set. The Al-Ketan, Abu Al-Rub paper was not opened either (its bibliographic data are checked against Crossref, below); the nodal Lidinoid formula and its constant are therefore not verified against a primary source, only against the other published forms named above |
| Neovius surface | E. R. Neovius, *Bestimmung zweier speciellen periodischen Minimalflaechen*, 1883 |
| Nodal (trigonometric) approximations of P, G, D and I-WP | P. J. F. Gandy, S. Bardhan, A. L. Mackay, J. Klinowski, *Nodal surface approximations to the P, G, D and I-WP triply periodic minimal surfaces*, Chem. Phys. Lett. 336(3-4) (2001) 187-195, DOI 10.1016/S0009-2614(00)01418-4 (bibliographic data checked against the Crossref record on 2026-11-15; the paper was not opened); the Neovius form as in O. Al-Ketan, R. K. Abu Al-Rub below |
| Sheet and skeletal ("solid network") lattices, level set offset, grading by a position dependent level | O. Al-Ketan, R. K. Abu Al-Rub, *Multifunctional mechanical metamaterials based on triply periodic minimal surface lattices*, Adv. Eng. Mater. 21(10) (2019) article 1900524, DOI 10.1002/adem.201900524 (bibliographic data checked against the Crossref record on 2026-11-15, which lists no page range; the paper was not opened) |
| Marching cubes | W. E. Lorensen, H. E. Cline, *Marching cubes: a high resolution 3D surface construction algorithm*, SIGGRAPH 1987 |
| Enclosure of the exact solid by the contoured one (level pad, 1-Lipschitz field) | A. Jacobson, *A Sharp Conservative Offset for Marching Cubes*, arXiv:2609.13430 (see [design/CONSERVATIVE_MC_AUDIT.md](../design/CONSERVATIVE_MC_AUDIT.md)) |
| Two-type hybrid across a plane (`SecondType`, `Blend`, `SigmoidBlend`): the sigmoid weight `1 / (1 + exp(-k eta))` blending two TPMS fields in place; the blend of style fields with per-type levels, its Lipschitz bound and error term are our own derivation (design section 9) | Y. Fang, S. Yan, X. Xie, H. Wang, Y. Ke, X. Zhang, Z. Li, *In-plane hybrid design and mechanical properties of Ti-6Al-4V TPMS structures*, Sci. Rep. 16, 17947 (2026), DOI 10.1038/s41598-026-49318-0, CC BY 4.0 (eq. 2 to 6 read from the open-access text; their references Ren et al. 2021, Mater. Des. 210, 110062 and Gao et al. 2022, Mater. Des. 221, 110995 were not read); implemented from the formulas, nothing ported |

## Remesher quality safeguards (`Remesher.MinTriangleQuality`, also in `RemesherPro`)

Own design from the textbook procedures, no code taken over.

| Part | Source |
|---|---|
| Vertices on feature curves relocated only along the curve; tangential, quality-preserving relocation in isotropic remeshing | M. Botsch, L. Kobbelt, *A Remeshing Approach to Multiresolution Modeling*, SGP 2004; M. Botsch, L. Kobbelt, M. Pauly, P. Alliez, B. Lévy, *Polygon Mesh Processing*, AK Peters 2010, ch. 6 |
| Caps removed by flipping their long edge; needles by collapsing | M. Botsch, L. Kobbelt, *A Robust Procedure to Eliminate Degenerate Faces from Triangle Meshes*, VMV 2001 |

The rest (moves, collapses and flips rejected if they flip a normal or create a flat triangle, no splits of an edge
with a vertex on it, buffered projection for a deterministic result) follows from the failure analysis on Thingi10K
in `docs/roadmap/alpha-wrap-and-repair.md` (Phase 2 items).

`Remesher.PreventSelfIntersections`: own design, the per-operation check of `Reducer.PreventSelfIntersections`
(triangle hash grid, the exact `ExactTriangleIntersection.IntersectsBeyondShared`) extended to flips, splits and the
buffered vertex moves; no external source.

The feature-fold check of `Remesher.MinTriangleQuality` (#640, issue #621: a flip or collapse at a feature curve that folds a
triangle onto a flat cap without flipping a normal is rejected, by the exact triangle test against the triangles that face away
around its feature corners): own design, no external source.

`Remesher.TargetEdgeLengthF` / `RemeshSizing.FromWallThickness`: sizing below the local feature size, as in
isotropic remeshing with a sizing field (Botsch, Kobbelt, Pauly, Alliez, Lévy, *Polygon Mesh Processing*, ch. 6); the
thickness is `MeshWallThickness`'s inscribed sphere (Ma, Bai, Hu, Kim 2012). Own design otherwise.

`MeshConstraintUtil.PinSharpVertices`: the angle deficit of `MeshCurvature` (Meyer, Desbrun, Schröder, Barr 2003, eq. 9)
as the measure of a vertex feature; the threshold is own design.

## Research review: reducer and remesher guarantees (not implemented)

Sources recommended by [design/RESEARCH_REDUCER_REMESHER.md](../design/RESEARCH_REDUCER_REMESHER.md) for upper-bound
measurements or for stage 7b. Nothing is implemented from them yet; an implementation adds its row to the section of
the class it changes. Full texts were not read in the review session (host blocks), so each needs a reading before
an implementation brief.

| Candidate | Source | Reference code / license |
|---|---|---|
| Cell-based accept filter for `MaxDeviation` (permission grid, adaptive variant) | S. Zelinka, M. Garland, *Permission Grids: Practical, Error-Bounded Simplification*, ACM TOG 21(2):207-229, 2002 | none known; paper only |
| Lazy per-contraction self-intersection test (the existing `Reducer` scheme) | S. Gumhold, P. Borodin, R. Klein, *Intersection Free Simplification*, IJSM 9(2):155-176, 2003, doi:10.1142/S0218654303000097 | none; paper only |
| Broad phase: grid cell size vs primitive size | M. Teschner, B. Heidelberger, M. Müller, D. Pomeranets, M. Gross, *Optimized Spatial Hashing for Collision Detection of Deformable Objects*, VMV 2003 | paper only |
| Broad phase: dynamic BVH with refit / insert-remove, fat boxes | T. Larsson, T. Akenine-Möller, *A dynamic bounding volume hierarchy for generalized collision detection*, VRIPHYS 2005; G. van den Bergen, *Efficient collision detection of complex deformable models using AABB trees*, JGT 2(4), 1997; Box2D `b2DynamicTree` (E. Catto) | Box2D: MIT on `main` (LICENSE file checked); zlib-style before 2.4 per secondary sources, to be confirmed for an older version if one is read |
| Clearance filter (skip pair tests when displacement < certified clearance) | B. Mirtich, *Impulse-based Dynamic Simulation of Rigid Body Systems*, PhD thesis, UC Berkeley 1996 (conservative advancement); R. Bridson, R. Fedkiw, J. Anderson, *Robust Treatment of Collisions, Contact and Friction for Cloth Animation*, SIGGRAPH 2002 | papers only |
| Intersection-free local mesh operations, CCD, swept-tetrahedron test for flips (reference for the remesher check; CCD not recommended now) | T. Brochu, R. Bridson, *Robust Topological Operations for Dynamic Explicit Surfaces*, SIAM J. Sci. Comput. 31(4):2472-2493, 2009; T. Brochu, E. Edwards, R. Bridson, *Efficient Geometrically Exact Continuous Collision Detection*, ACM TOG 31(4), 2012; B. Wang, Z. Ferguson, T. Schneider, X. Jiang, M. Attene, D. Panozzo, *A Large Scale Benchmark and an Inclusion-Based Algorithm for Continuous Collision Detection*, ACM TOG 40(5), 2021 | El Topo: BSD 2-Clause (`license.txt` on `master` of `tysonbrochu/eltopo` checked; its UBC project page and the paper's §5 had said "public domain"); Tight Inclusion: MIT (LICENSE file checked) |
| Minimum separation between surfaces | D. Harmon, D. Panozzo, O. Sorkine, D. Zorin, *Interference-Aware Geometric Modeling*, ACM TOG 30(6):137, 2011 | paper only |
| Stage 7b quality pass: worst-angle queue, collapse / relocation / split (no flips) under a hard error bound | K. Hu, D.-M. Yan, D. Bommes, P. Alliez, B. Benes, *Error-Bounded and Feature Preserving Surface Remeshing with Minimal Angle Improvement*, IEEE TVCG 23(12):2560-2573, 2017, doi:10.1109/TVCG.2016.2632720 | paper only; their implementation is built on CGAL (GPL), not to be read (our guard is `IsWithin`, not their sampled estimate) |
| Stage 7b operators against obtuse / small angles | Y. Wang, D.-M. Yan, X. Liu, C. Tang, J. Guo, X. Zhang, P. Wonka, *Isotropic Surface Remeshing without Large and Small Angles*, IEEE TVCG 25(7):2430-2442, 2019, doi:10.1109/TVCG.2018.2837115 | paper only |
| Curvature-adaptive sizing | M. Dunyach, D. Vanderhaeghe, L. Barthe, M. Botsch, *Adaptive Remeshing for Real-Time Mesh Deformation*, Eurographics 2013 Short Papers, pp. 29-32 | paper only |
| Parallel decimation by independent sets (background for speculative certification) | P. Franc, V. Skala, *Parallel Triangular Mesh Reduction*, ALGORITMY 2000; S. Oh et al., *PaMO: Parallel Mesh Optimization for Intersection-Free Low-Poly Modeling on the GPU*, CGF 44(7), 2025 | PaMO code license not checked; not a port candidate |
| Reducer / Remesher 2.0 proposal ([design/REDUCER_REMESHER_20.md](../design/REDUCER_REMESHER_20.md)): QEM variants and parallel / partitioned simplification (approach only, not implemented) | M. Garland, P. Heckbert, *Surface simplification using quadric error metrics*, SIGGRAPH 1997; P. Lindstrom, G. Turk, *Fast and memory efficient polygonal simplification*, IEEE Visualization 1998; H. Hoppe, *New quadric metric for simplifying meshes with appearance attributes*, IEEE Visualization 1999; P. Lindstrom, *Out-of-core simplification of large polygonal models*, SIGGRAPH 2000; M. Franc, V. Skala, *Parallel triangular mesh decimation without sorting*, SCCG 2001; Ozaki, Kyota, Kanai, partition-based out-of-core QEM simplification, Eurographics PGV 2015 | papers only, citations of the last four from memory or a search hit: verify before use |
| Same: isotropic remeshing and feature-aware decimation | P. Alliez, E. Colin de Verdière, O. Devillers, M. Isenburg, *Isotropic surface remeshing*, SMI 2003; S. Valette, J.-M. Chassery, *Approximated centroidal Voronoi diagrams for uniform polygonal mesh coarsening*, Eurographics 2004; D. Salinas, F. Lafarge, P. Alliez, *Structure-aware mesh decimation*, CGF 2015 | papers only, verify before use |
| Same: implementations whose approach may be read | meshoptimizer (A. Kapoulkine; MIT, checked by search 2026-11-09); geogram (B. Lévy; BSD-3-Clause, checked by search); Fast-Quadric-Mesh-Simplification (MIT) and OpenMesh (BSD-3): licences from memory, check the file before reading. Not to be read or ported: CGAL, TetWild, libigl (MPL-2.0), Blender, MeshLab, Instant Meshes (BSD-3 plus an Enhancements clause, checked 2026-11-17; owner 2026-11-17: quad remeshing is implemented from the papers only, [design/QUAD_REMESHING.md](../design/QUAD_REMESHING.md)); pmp-library not checked | approach only |
