# Sources: Terrain, Spatial Indexes, Analysis, Point Clouds & 2D Polygons

Back to [Sources Index](../SOURCES.md) · [sources/README.md](README.md)

---

## 2D loose quadtree (`LooseQuadtree2d`, point / segment / triangle adapters)

Clean-room implementation from [QUADTREE2D_SPEC.md](../design/QUADTREE2D_SPEC.md) and published
spatial-indexing concepts; no code copied or inspected from the owner's index, Triangle or Triangle.NET.

| Part | Source |
|---|---|
| Loose cells for extended objects; hierarchical spatial indexing | H. Samet, J. Sankaranarayanan, M. Auerbach, *Indexing methods for moving object databases*, SIGMOD 2013, [doi:10.1145/2463676.2465332](https://doi.org/10.1145/2463676.2465332); H. Samet, *Foundations of Multidimensional and Metric Data Structures*, 2006 |
| Closed segment–box interval clipping | Y.-D. Liang, B. A. Barsky, *A New Concept and Method for Line Clipping*, ACM TOG 1984 |
| Distance lower-bound pruning for nearest-neighbour search | N. Roussopoulos, S. Kelley, F. Vincent, *Nearest Neighbor Queries*, SIGMOD 1995 |
| Exact XY containment / segment intersection signs | Existing `ExactPredicates.Orient2d` (Shewchuk 1997; see below); no new orientation arithmetic filter or error bound |
| Ambiguous clipping; overflow-distance ordering; range-loss construction fallbacks | Own rational arithmetic on binary64 coordinates represented as integers in units of 2^-1074; interval comparison by cross multiplication, squared point/segment distances, affine determinants and exact squared-midpoint rounding for subnormal distances. No external implementation inspected |

The procedural benchmark compares against independent brute-force scans and the public STRtree and Quadtree APIs of
[NetTopologySuite 2.6.0](https://www.nuget.org/packages/NetTopologySuite/2.6.0). It is a pinned benchmark/test-only
NuGet dependency, not a geometry3Sharp library dependency or a source for the clean-room index.
The [release license](https://github.com/NetTopologySuite/NetTopologySuite/blob/v2.6.0/License.md) is BSD-3-Clause;
its JTS-derived content is available under the permissive BSD-style Eclipse Distribution License option.
Only public API documentation and licensing were consulted; no implementation was ported. Ordinary-coordinate
benchmark predicates and comparison limits are documented in the [benchmark README](../../benchmarks/QuadtreeBenchmark/README.md).

`benchmarks/OracleIndexPilot` also uses the public NetTopologySuite 2.6.0 STRtree
API (BSD-3-Clause, same release license above), as an isolated test/benchmark
dependency. NTS builds a point hierarchy; our traversal uses the existing oracle's
unchanged orientation filter/exact fallback and the affine maximum of orientation
over a closed rectangle to prune strictly exterior subtrees. No NTS geometric
verdict is used and no implementation is read or ported. The oracle's default
conformity searches (binary64 outputs; `G3_ORACLE_POINT_INDEX` / `G3_ORACLE_EDGE_INDEX`
`=sweep` restore the sweeps) add NTS to the independent test-oracle project, never to
the library; the compiler still enforces the oracle's lack of a geometry3Sharp reference.
The proof, controls and measurements are documented
in the [pilot README](../../benchmarks/OracleIndexPilot/README.md).

The second isolated variant uses exact endpoint convex hulls on STRtree nodes for
proper edge-crossing queries. The monotone-chain hull is own code from the standard
concept of A. M. Andrew, *Another efficient algorithm for convex hulls in two
dimensions*, IPL 9(5), 1979, [doi:10.1016/0020-0190(79)90072-3](https://doi.org/10.1016/0020-0190(79)90072-3).
Hierarchical geometric separation was considered from Gottschalk, Lin and Manocha,
*OBBTree*, SIGGRAPH 1996; Bentley and Ottmann, *Algorithms for Reporting and Counting
Geometric Intersections*, IEEE TC 1979, is the sweep alternative. Publication
metadata/abstracts were consulted; no implementation code was read or copied.
The two closed-half-plane certificates and the degenerate-hull handling are
derived in the pilot comments/README and reviewed against the unchanged exact
ProperCross predicate. A node keeps its exact hull only up to 16 vertices and
otherwise tests the corners of its closed box (a containing polygon), so the test per
node is bounded (own construction after the #411 review found O(E·h) work on convex
boundaries). The point/triangle and duplicate checks remain in force.

The large pilot mesh is generated externally from a GEOS XML test fixture at
commit `159cc1f9dba3ee820e31123af4f90a2b6e090546`; source URL and input/output
hashes are recorded in the pilot's numeric provenance. Upstream's pinned
[COPYING](https://github.com/libgeos/geos/blob/159cc1f9dba3ee820e31123af4f90a2b6e090546/COPYING)
is LGPL-2.1; no separate permissive fixture license is assumed. Fixtures and
generated geometries/meshes are not committed. Only external data is used;
GEOS implementation code is not consulted or ported.

## Minimal enclosing circle (`ContMinCircle2`)

Replaces the WildMagic port. Implemented from one paper, no code taken over: E. Welzl, *Smallest enclosing disks (balls and ellipsoids)*, 1991. The three nested loops were derived from Welzl's recursion; no paper by Seidel was consulted for them.

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

## Distances and simplification (`MeshHausdorffDistance`, `TriangleDeviationBounds`, `Reducer`, `MeshEnvelope`)

The sources named in the code:

| Part | Source |
|---|---|
| Hausdorff distance with guaranteed bounds (branch and bound over triangles, cascading upper bounds; `TriangleDeviationBounds`, also used by `Reducer.MaxDeviation` and, per triangle, by `MeshDeviation.ComputeTriangleBounds`) | L. Sacht, A. Jacobson, *Cascading upper bounds for triangle soup Pompeiu-Hausdorff distance*, SGP 2024 |
| Certified lower bound and rounding of subdivision points (`MeshHausdorffDistance`, `CertifiedPointMeshDistance`; `docs/design/HAUSDORFF_CERTIFIED_BOUNDS.md`): the error of a floating-point sum by TwoSum, exact differences by Sterbenz's lemma, an exact point-triangle squared distance in integers | D. E. Knuth, *The Art of Computer Programming*, Vol. 2, section 4.2.2 (TwoSum); P. H. Sterbenz, *Floating-Point Computation*, Prentice-Hall 1974 (the subtraction lemma); the point-triangle distance by the region classification of D. Eberly, *Distance Between Point and Triangle in 3D* (Geometric Tools, the library's `DistPoint3Triangle3` follows it), here in exact integers |
| Certified upper bound (`MeshHausdorffDistance.CertifiedUpper`, `CertifiedTriangleBounds`; `docs/design/HAUSDORFF_CERTIFIED_UPPER.md`): outward-rounded interval evaluation of the distance to a point of the triangle, of the cascade's terms and of the plane split's cut points; the covering radius bounded by the circumradius and l/sqrt(3) | interval arithmetic: R. E. Moore, *Interval Analysis*, Prentice-Hall 1966; the cascade of L. Sacht, A. Jacobson (SGP 2024, above); the circumradius and the triangle's covering radius: elementary geometry (derivations in the design note) |
| Coplanar and touching meshes (`MeshHausdorffDistance`, #873; `docs/design/HAUSDORFF_CERTIFIED_UPPER.md` section 12): distances as the length to the computed closest point, not the square root of the cancelling squared-distance form; the nearest triangle chosen so (`DMeshAABBTree3.FindNearestTriangleByClosestPoint`) | D. Eberly, *Distance Between Point and Triangle in 3D* (Geometric Tools; its later point-triangle queries take the squared length of point minus closest point); the cascade of L. Sacht, A. Jacobson (SGP 2024, above). No code read |
| Patch bound (`TriangleDeviationBounds.PatchBound`, opt-in through `MeshHausdorffDistance.PatchBound`, used by `MeshRobustRepair`'s simplification of the alpha wrap): the target triangles under a triangle, seen along its normal, bound the distance by the largest height over the covered part, if their projections cover the triangle (coverage shown by the boundary edges of a consistently oriented, positively projecting patch) | own construction, an extra bound in the cascade of Sacht and Jacobson (above); the coverage argument is the boundary of a union of oriented triangles (a planar map) |
| Certified patch bound (`TriangleDeviationBounds.certified_patch_bound`, `CertifiedDet3`; `docs/design/CERTIFIED_PATCH_BOUND.md`, #848): the sign and value of a 3x3 determinant of rounded rows with a running error bound derived in the standard model, strict certified signs for the coverage test, an enclosure of the cut parameter by monotonicity | J. R. Shewchuk, *Adaptive precision floating-point arithmetic and fast robust geometric predicates*, DCG 18, 1997 (the filter structure); N. J. Higham, *Accuracy and Stability of Numerical Algorithms*, 2nd ed., SIAM 2002, section 3.1 (the standard model, gamma_n); H. Brönnimann, C. Burnikel, S. Pion, *Interval arithmetic yields efficient dynamic filters for computational geometry*, DAM 109, 2001, and O. Devillers, S. Pion, *Efficient exact geometric predicates for Delaunay triangulations*, ALENEX 2003 (filter hierarchies); the cascade of Sacht and Jacobson (above). No code read |
| Two-sided deviation bound of the reduction (`Reducer.MaxDeviationTwoSided`, `ReducerReverseCoverage`): per input triangle the output triangles it is certified against, re-certified when a collapse changes one of them; the certificate against a candidate list uses the cascading bounds (corner maximum for one convex distance, covering radius, subdivision); `docs/design/BOUNDED_SIMPLIFICATION.md` | own construction (the induction argument is in the class documentation); the maintained correspondence between original and reduced mesh: R. Klein, G. Liebich, W. Straßer, *Mesh reduction with error control*, IEEE Visualization 1996 [from the abstract]; the bounds: L. Sacht, A. Jacobson, *Cascading upper bounds for triangle soup Pompeiu-Hausdorff distance*, SGP 2024. No code read |
| Regularized optimal collapse point (`Reducer.QuadricRegularization`) | P. Trettner, L. Kobbelt, *Fast and Robust QEF Minimization using Probabilistic Quadrics*, Computer Graphics Forum (Eurographics) 2020 |
| Reduction on fixed slabs in parallel with frozen borders and confinement (`PartitionedReducer`, `PartitionedReduction`) | own construction (the argument is in the class documentation of `PartitionedReduction`); the approach of partitioned out-of-core simplification: P. Lindstrom, *Out-of-core simplification of large polygonal models*, SIGGRAPH 2000; Ozaki, Kyota, Kanai, Eurographics PGV 2015 (found by search, not read); quadrics: M. Garland, P. Heckbert, SIGGRAPH 1997 | none (no code read) |
| Paired collapse of coincident twin edges (`Reducer.PairCoincidentVertices`, `SolidBooleanPipeline.PairContacts`): the vertices of one position are one vertex stored twice, an edge whose ends have twins collapses with its twin edge to one position, both sheets checked, the exact check taking the twins' shared position as a shared vertex | own construction (the argument is in the option's documentation: independent one-rings, so the twin's collapse is the same after the edge's, checked beforehand by `Reducer.MeshAcceptsCollapse`); quadrics of both sheets added: M. Garland, P. Heckbert, SIGGRAPH 1997; the guarded collapse: S. Gumhold, P. Borodin, R. Klein, IJSM 2003 | none (no code read) |
| Thin solids of a boolean result (`SolidBooleanPipeline.MinThickness`): the solids are the outer shells with the cavities whose probe point they contain (smallest shell; winding number), measured by 2 volume / area (the thickness of a slab or wall), volumes relative to a vertex of each component; a strict rounding refusal only for components thinner than its bound keeps the rounded result | own construction; winding number for the containment: A. Jacobson, L. Kavan, O. Sorkine-Hornung, SIGGRAPH 2013, fast evaluation: G. Barill et al., SIGGRAPH 2018 (the existing `DMeshAABBTree3.FastWindingNumber`) | none (no code read) |
| Snap of the boolean inputs to a power-of-two grid (`SolidBooleanPipeline.InputGrid`): every coordinate to the nearest multiple (ties to even), exact because the grid is a power of two, so nearly coincident faces of faceted solids become exactly coincident before the exact boolean | the fixed-precision model of GIS overlay: JTS `PrecisionModel` / `GeometryPrecisionReducer` and GEOS (documentation only); snap rounding: J. Hobby, Comput. Geom. 1999; own construction for the exact power-of-two snap | none (no code read) |
| Triangle-triangle intersection test (`ExactTriangleIntersection`) | O. Devillers, P. Guigue, *Faster triangle-triangle intersection tests*, INRIA RR-4488, 2002 |
| Filter in front of it (`TriangleTriangleFilter`): exact separation in an axis projection (an edge line of one projected triangle with the other strictly outside; at a shared vertex, corners that share no ray) | the separating axis theorem for convex polygons, eg S. Gottschalk, M. Lin, D. Manocha, *OBBTree: a hierarchical structure for rapid interference detection*, SIGGRAPH 1996. Own construction of the projection argument and the shared-vertex and shared-edge certificates, and of the axis-plane case (one triangle in an axis plane: the Orient3d determinant factors into a coordinate difference times the projected Orient2d, so the other triangle's sides are comparisons, and where it touches the plane the 2D tests decide) |
| Patch certificate for the self-intersection checks (design, `docs/design/SELF_INTERSECTION_PATCH_CERTIFICATE.md`, not implemented yet): a normal cone below 90 degrees and a star-shaped contour certify a consistently oriented disk free of self-intersections | P. Volino, N. Magnenat-Thalmann, *Efficient self-collision detection on smoothly discretized surface animations using geometrical shape regularity*, Computer Graphics Forum 1994; S. C. Schvartzman, J. Pérez, M. A. Otaduy, *Star-contours for efficient hierarchical self-collision detection*, ACM TOG (SIGGRAPH) 2010; context: S. Gumhold, P. Borodin, R. Klein, *Intersection free simplification*, IJSM 9(2), 2003; S. Curtis, R. Tamstorf, D. Manocha, *Fast collision detection for deformable models using representative-triangles*, I3D 2008. Own: the disk condition and the exact predicates that make it a certificate for a triangle mesh |
| Exact containment in a polyhedral envelope (`MeshEnvelope`): a convex prism per triangle inside triangle + epsilon ball, queries decided exactly against the union. Own construction of the prisms (slab along the normal, side half-spaces in uniform and edge normal directions with offsets that bound them by 0.6 epsilon, rounding margins) and own exact test (clipping the query triangle in its parameter plane with integer half-planes, floating-point filters first) | B. Wang, T. Schneider, Y. Hu, M. Attene, D. Panozzo, *Exact and Efficient Polyhedral Envelope Containment Check*, ACM TOG (SIGGRAPH) 2020 (idea of the envelope as a union of prisms and the exact containment check; `fast-envelope`, MIT, has an implementation, not used as a source) |

## Evaluation metrics (`MeshFidelityMetrics`, `TriangleQualityStatistics`, `MeshTopologySummary`)

Implemented from the papers and definitions (no code ported):

| Part | Source |
|---|---|
| F-score at a distance threshold (precision and recall of the surfaces within tau) | M. Tatarchenko, S. R. Richter, R. Ranftl, Z. Li, V. Koltun, T. Brox, *What Do Single-view 3D Reconstruction Networks Learn?*, CVPR 2019 |
| Normal consistency (mean \|cos\| between the normals at corresponding nearest points) | L. Mescheder, M. Oechsle, M. Niemeyer, S. Nowozin, A. Geiger, *Occupancy Networks: Learning 3D Reconstruction in Function Space*, CVPR 2019 |
| Edge Chamfer distance (Chamfer distance between the sharp edges) | Z. Chen, A. Tagliasacchi, T. Funkhouser, H. Zhang, *Neural Dual Contouring*, ACM TOG (SIGGRAPH) 2022; Z. Chen, H. Zhang, *Neural Marching Cubes*, ACM TOG (SIGGRAPH Asia) 2021 |
| Normal at the closest point: angle weighted pseudo-normal of the face, edge or vertex | J. A. Baerentzen, H. Aanaes, *Signed distance computation using the angle weighted pseudonormal*, IEEE TVCG 2005 |
| Triangle quality metrics (min / max angle, aspect ratio, scaled Jacobian, condition number) and their acceptable ranges | C. J. Stimpson, C. D. Ernst, P. Knupp, P. P. Pebay, D. Thompson, *The Verdict Geometric Quality Library*, Sandia report SAND2007-1751, 2007 (the library is BSD; implemented from the definitions); P. M. Knupp, *Algebraic mesh quality metrics*, SIAM J. Sci. Comput. 23(1), 2001 |
| Genus from the Euler characteristic and the boundary loops (chi = 2 - 2g - b) | standard (eg M. Botsch, L. Kobbelt, M. Pauly, P. Alliez, B. Levy, *Polygon Mesh Processing*, 2010, ch. 1) |
| Volume IoU of the solids (`VolumeIoU`, `OuterVolumeIoU`): inside = generalized winding number > 0.5 | A. Jacobson, L. Kavan, O. Sorkine-Hornung, *Robust Inside-Outside Segmentation using Generalized Winding Numbers*, ACM TOG 2013 (via the existing `FastWindingNumber`, Barill et al. 2018) |
| Sample cells of the volume check (`VolumeSampleCells`, `VolumeIoUAtLeast` on winding-closed meshes): an adaptive octree with inside, outside and boundary cells; a cell no triangle touches has a constant (integer) winding number, classified at its centre and counted with its exact volume (stratified sampling) | D. Meagher, *Geometric modeling using octree encoding*, Computer Graphics and Image Processing 19(2), 1982; winding numbers as above (Jacobson et al. 2013; Barill, Dickson, Schmidt, Levin, Jacobson, *Fast Winding Numbers for Soups and Clouds*, ACM TOG (SIGGRAPH) 2018); stratified sampling and the Wilson score interval: standard (W. G. Cochran, *Sampling Techniques*, 3rd ed., 1977, ch. 5; E. B. Wilson, JASA 1927); the combined interval of the ratio from the corners of two intervals and the cover argument: own reasoning ([design/VOLUME_CHECK_SAMPLING.md](../design/VOLUME_CHECK_SAMPLING.md) section 4) |
| Exact ray-triangle decisions (`ExactRayTriangle`; `IntrRay3Triangle3`, `DMeshAABBTree3` ray queries and `IsInside`, `VolumeSampleCells.ExactWinding`): the line-triangle test by the signs of the line against the three edges (Plücker coordinates, here as `Orient3d` of two line points and an edge), closed triangles for hits, Simulation of Simplicity for single-counted crossings (the ε terms derived in the file), the second ray point at a power-of-two scaled direction | J. Plücker 1865 / K. Shoemake, *Plücker coordinate tutorial*, Ray Tracing News 11(1), 1998; N. Platis, T. Theoharis, *Fast ray-tetrahedron intersection using Plücker coordinates*, JGT 8(4), 2003; H. Edelsbrunner, E. P. Mücke, *Simulation of Simplicity*, ACM TOG 1990; S. Woop, C. Benthin, I. Wald, *Watertight ray/triangle intersection*, JCGT 2(1), 2013 (the watertightness goal; its shear scheme is not used); the predicates as in Shewchuk 1997 (above) |
| Exact triangle-box overlap (`VolumeSampleCells.TriangleTouchesBox`): the separating axis theorem with the 13 axes of a triangle and a box, each decided by the signs of exact orientation predicates | T. Akenine-Möller, *Fast 3D Triangle-Box Overlap Testing*, Journal of Graphics Tools 6(1), 2001 (the axes); signs from `ExactPredicates` (Shewchuk 1997) |
| Outer faces for `OuterVolumeIoU`: the outer surface rule of `OuterSurface` (winding numbers on both sides) per face, iterated on what is left until no face is removed | own reasoning (on the winding number definitions above) |
| Discrete curvature per vertex (`MeshCurvature`): mixed Voronoi areas, mean curvature normal, Gaussian curvature by the angle deficit | M. Meyer, M. Desbrun, P. Schröder, A. H. Barr, *Discrete Differential-Geometry Operators for Triangulated 2-Manifolds*, VisMath 2002 (Springer 2003) |
| Mean curvature normal in the area gradient form (equivalent to the cotan formula, no cotangents) | M. Desbrun, M. Meyer, P. Schröder, A. H. Barr, *Implicit Fairing of Irregular Meshes using Diffusion and Curvature Flow*, SIGGRAPH 1999; U. Pinkall, K. Polthier, *Computing Discrete Minimal Surfaces and Their Conjugates*, Experimental Mathematics 1993 |
| Distance of the curvature distributions: 1-Wasserstein distance of the empirical distributions (integral of the difference of the distribution functions) | standard (eg C. Villani, *Optimal Transport: Old and New*, 2009, ch. 6; on the real line the L1 distance of the distribution functions) |

## Terrain (`TerrainDualContouring`, `TerrainGreedyTIN`, `ClothSimulationFilter`, `TerrainOffset`, `TerrainSafetyLines`, `TerrainBreakLines`, `TerrainVolumeDifference`)

| Part | Source |
|---|---|
| Ground filter (inverted point cloud, falling cloth) | W. Zhang, J. Qi, P. Wan, H. Wang, D. Xie, X. Wang, G. Yan, *An Easy-to-Use Airborne LiDAR Data Filtering Method Based on Cloth Simulation*, Remote Sensing 2016. The authors' reference implementation (CSF) is Apache-2.0 |
| Greedy insertion TIN with a maximum vertical error | M. Garland, P. S. Heckbert, *Fast Polygonal Approximation of Terrains and Height Fields*, CMU-CS-95-181, 1995 |
| 2.5D dual contouring of the top surface, quadtree merging | after Ju et al. 2002 (see above); the height field sampling, the 2D boundary QEF and the overlap removal are this library's own |
| Geodesic safety lines (`AlongSurface`) | the heat method, K. Crane, C. Weischedel, M. Wardetzky, *Geodesics in Heat*, ACM TOG 2013 (via `MeshGeodesicDistance`) |
| Offset surface, horizontal safety lines, break lines, cut and fill volumes | own methods (no external source) |

## Terrain slope design (`docs/design/TERRAIN.md`; phase 1a: `TerrainSlopeDesign`, `Slope`, `SlopeSpec`; phase 1b: `CatchLine`, the overlay and the composite)

Phase 1a (the slope surface and the oracle, `docs/design/TERRAIN_1A_PLAN.md`) and phase 1b (the overlay with the terrain,
the catch lines, the region and the composite, `docs/design/TERRAIN_1B_PLAN.md`) are implemented from these sources, no
code taken over; the rest is design. Sources the design was written from: Proprietary tools (Civil 3D, Datamine Studio OP, Maptek Vulcan) were consulted through their
public documentation only. The user's company code was not looked at.

| Part | Source |
|---|---|
| Slope surface as the envelope of slope cones with apexes on the edge (kotierte Projektion, earthworks) | descriptive geometry textbooks, eg R. Fucke, K. Kirch, H. Nickel, *Darstellende Geometrie für Ingenieure*, Hanser |
| Largest Lipschitz extension as min of cones; Hopf–Lax formula | E. J. McShane, *Extension of range of functions*, Bull. AMS 1934; H. Whitney, *Analytic extensions of differentiable functions defined in closed sets*, Trans. AMS 1934; L. C. Evans, *Partial Differential Equations*, AMS |
| Weighted Voronoi diagrams as lower envelopes of cones | F. Aurenhammer, H. Edelsbrunner, *An optimal algorithm for constructing the weighted Voronoi diagram in the plane*, Pattern Recognition 1984; S. Fortune, *A sweepline algorithm for Voronoi diagrams*, Algorithmica 1987; A. Okabe, B. Boots, K. Sugihara, S. N. Chiu, *Spatial Tessellations*, Wiley 2000 |
| Weighted straight skeletons (compared, not chosen) | O. Aichholzer, F. Aurenhammer, D. Alberts, B. Gärtner, *A novel type of skeleton for polygons*, J. UCS 1995; D. Eppstein, J. Erickson, *Raising roofs, crashing cycles, and playing pool*, DCG 1999; T. Kelly, P. Wonka, *Interactive architectural modeling with procedural extrusions*, ACM TOG 2011; T. Biedl, M. Held, S. Huber, D. Kaaser, P. Palfrader, *Weighted straight skeletons in the plane*, CGTA 48, 2015 |
| Eikonal / Fast Marching on triangle meshes (option R2) | R. Kimmel, J. A. Sethian, *Computing geodesic paths on manifolds*, PNAS 1998; J. A. Sethian, A. Vladimirsky, *Ordered upwind methods for static Hamilton–Jacobi equations*, SIAM J. Numer. Anal. 2003; J. S. B. Mitchell, C. H. Papadimitriou, *The weighted region problem*, J. ACM 1991 |
| Open-pit slope constraints | H. Lerchs, I. F. Grossmann, *Optimum design of open-pit mines*, CIM Bulletin 1965; R. Khalokakaie, *Computer-aided optimal open pit design with variable slope angles*, PhD thesis, University of Leeds 1999 (and Mining Technology 109, 2000, DOI 10.1179/mnt.2000.109.2.77; author list to be confirmed); W. Hustrulid, M. Kuchta, R. Martin, *Open Pit Mine Planning and Design*, CRC 2013 |
| Topological overlay with exact predicates, degeneracies | M. de Berg et al., *Computational Geometry*, ch. 2 (map overlay); J. O'Rourke, C.-B. Chien, T. Olson, D. Naddor, *A new linear algorithm for intersecting convex polygons*, CGIP 1982; H. Edelsbrunner, E. P. Mücke, *Simulation of Simplicity*, ACM TOG 1990 |
| Contour lofting (compared) | H. Fuchs, Z. M. Kedem, S. P. Uselton, *Optimal surface reconstruction from planar contours*, CACM 1977; E. Keppel 1975; H. N. Christiansen, T. W. Sederberg 1978; D. Meyers, S. Skinner, K. Sloan, *Surfaces from contours*, ACM TOG 1992; G. Barequet, M. Sharir 1996 |
| `ContourLoft` stage 1 (`mesh_generators/ContourLoft.cs`, [CONTOUR_LOFT.md](../design/CONTOUR_LOFT.md)): the band as a shortest monotone path in the toroidal grid graph, optimal over the start correspondence by divide and conquer over the starts; the volume cost as signed tetrahedra per band triangle; the normalization of both contours before the cost | H. Fuchs, Z. M. Kedem, S. P. Uselton, *Optimal surface reconstruction from planar contours*, CACM 20(10), 1977 (graph, minimum area, divide and conquer); E. Keppel, *Approximating complex surfaces by triangulation of contour lines*, IBM J. Res. Dev. 19(1), 1975 (maximum volume); H. N. Christiansen, T. W. Sederberg, *Conversion of complex contour line definitions into polygonal element mosaics*, SIGGRAPH 1978 (normalization; their greedy step compared, not used); own construction: admissibility as a lexicographic weight (violations, then cost), the turn-count states that exclude the non-manifold fan paths, and the region rule that always holds a path. Implemented from the papers |
| `ContourLoftStack` (stage 2, `mesh_generators/ContourLoftStack*.cs`, [CONTOUR_LOFT.md](../design/CONTOUR_LOFT.md) section 10): correspondence of contours of consecutive slices by overlap of their faces (overlap graph, one exact constrained Delaunay overlay per slab, triangles labelled by a flood fill that toggles at constrained edges); branching and merging through composite contours joined by zero-width bridges (shortest visible vertex pair, Prim's tree), the intersection pieces of the two sides at an intermediate plane when the contours are cut; holes lofted like outer contours; planar caps for contours without a counterpart; every band by the stage 1 search | A. Boissonnat, *Shape reconstruction from planar cross sections*, CVGIP 44(1), 1988 (compared: 3D Delaunay, not used); H. N. Christiansen, T. W. Sederberg 1978 and S. Ganapathy, T. G. Dennehy, *A new general triangulation method for planar contours*, SIGGRAPH 1982 (composite contour with a bridge); D. Meyers, S. Skinner, K. Sloan, *Surfaces from contours*, ACM TOG 11(3), 1992 (branching by composite contours, clefts; the ridge look); C. L. Bajaj, E. J. Coyle, K. N. Lin, *Arbitrary topology shape reconstruction from planar cross sections*, Graphical Models and Image Processing 58(6), 1996 (overlap-based correspondence, an intermediate contour for a branch); G. Barequet, M. Sharir, *Piecewise-linear interpolation between polygonal slices*, CVIU 63(2), 1996, and G. Barequet, M. T. Goodrich, A. Levi-Steiner, D. Steiner, *Contour interpolation by straight skeletons*, Graphical Models 66(4), 2004 (compared: straight skeleton, not used); S. P. Raya, J. K. Udupa, *Shape-based interpolation of multidimensional objects*, IEEE Trans. Medical Imaging 9(1), 1990 (the distance-ratio height of `LoweredSaddle`: a height over the region between nested contours, not a level set; section 10.11); own construction: the intersection decomposition of a group into pieces that lie in one contour of each side, the canonical numbering of overlay vertices by the markers of their segments, bridges kept clear of earlier bridges in the same plane. Implemented from the papers' ideas, no code read |
| Implementation of 1a: interval arithmetic with outward rounding as the filter of the exact predicates; the planar map as a half-edge structure; exact signs as in Shewchuk's predicates | R. E. Moore, *Interval Analysis*, Prentice-Hall 1966; D. E. Muller, F. P. Preparata, *Finding the intersection of two convex polyhedra*, TCS 1978 (doubly connected edge list), and de Berg et al., ch. 2; J. R. Shewchuk, *Adaptive precision floating-point arithmetic and fast robust geometric predicates*, DCG 1997. The domain is triangulated with the library's `ExactTriangulation2` (Sloan 1993, Lawson), the faces are found with `LooseQuadtree2d` (public API) |
| Implementation of 1b: the overlay of the slope surface with the terrain by vertex identities, decided by `Orient2d` with Simulation of Simplicity (the terrain shifted by (ε, ε²), the ε terms decided by sign comparisons of coordinate differences); the sign of S − T at overlay vertices by `Orient3d`; faces of two convex triangles from their mutual containment and edge crossings; the region by union-find over faces; partial triangles by the library's `ExactTriangulation2` (exact CDT) and parity labelling | H. Edelsbrunner, E. P. Mücke, *Simulation of Simplicity*, ACM TOG 1990; de Berg et al., ch. 2 (map overlay); J. O'Rourke et al. 1982 (above); R. E. Tarjan, *Efficiency of a good but not linear set union algorithm*, J. ACM 1975 (union-find); the predicates as in Shewchuk 1997 (above) |
| Phase 1c, randomized insertion with a canonical result (`docs/design/TERRAIN_1C_PLAN.md`, section 9): the pieces in a seeded random order; the labels the lexicographic minimum of (height, plane id); the map's components cut along a ray from each reflex corner (extended to the component's boundary, rays not stopped by rays, so the parts do not depend on their order), each part one convex face; the faces in the exact lexicographic order of their corners | K. L. Clarkson, P. W. Shor, *Applications of random sampling in computational geometry II*, DCG 4, 1989 (randomized incremental construction); J. M. Keil, *Polygon decomposition*, in J.-R. Sack, J. Urrutia (eds.), *Handbook of Computational Geometry*, Elsevier 2000 (convex decomposition by segments from the reflex vertices); D. E. Knuth, *The Art of Computer Programming* vol. 2, Algorithm 3.4.2 P (the shuffle); G. L. Steele, D. Lea, C. H. Flood, *Fast splittable pseudorandom number generators*, OOPSLA 2014 (SplitMix64); own construction for the tie rule and the order |
| Phase 1c, mixed cut and fill (`Direction.Auto`): the clamp max(S_fill, min(T, S_cut)) from a cut run and a fill run on the terrain, valid iff neither run fails its toe check (`ToeUndercut`, `ToeOvertopped`); one composite of both overlays, the terrain triangles both cut triangulated once with both overlays' constraints (`SlopeComposite.Partner`). Target heights: the level lines of the slope region at z = h, per triangle the segment between its edge crossings (a vertex at h counts as above), chained across shared edges | own derivation for the condition (TERRAIN.md appendix A); the level lines by marching triangles, eg M. van Kreveld, *Efficient methods for isoline extraction from a TIN*, IJGIS 10, 1996; the composite as in the 1b row above |
| The repair of the band [64·e_xy, `MinFeatureSize`) (TERRAIN.md 5.4, #350, #354): needles at near-quadruple and near-quintuple points merged by edge collapses, in passes, the cumulative move of every original position capped by the band radius, funded by the facets' slack | the cluster and cumulative-drift pattern of snap rounding: J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 13, 1999; D. Halperin, E. Packer, *Iterated snap rounding*, CGTA 23, 2002; E. Packer, *Iterated snap rounding with bounded drift*, CGTA 40, 2008; the collapse's link condition: T. K. Dey, H. Edelsbrunner, S. Guha, D. V. Nekhayev, *Topology preserving edge contraction*, Publ. Inst. Math. (Beograd) 66, 1999; own derivation of the bound (TERRAIN.md 5.4) |
| The opt-in local frame (`LocalFrame`, TERRAIN.md 5.8.1, #355): the inputs translated to a local origin, every translation checked exact by TwoSum, the outputs kept local with `UsedOrigin` | the practice of storing data relative to an origin: the LAS point cloud format's scale and offset (ASPRS LAS 1.4 specification, 2019); the relative-to-centre vertices of 3D Tiles (`CESIUM_RTC`, Cesium) and P. Cozzi, K. Ring, *3D Engine Design for Virtual Globes*, CRC 2011, ch. 5; IFC's `IfcMapConversion` (buildingSMART IFC 4.3); TwoSum: D. E. Knuth, *The Art of Computer Programming* vol. 2, 4.2.2; P. H. Sterbenz, *Floating-Point Computation*, 1974 (exact differences within a factor 2) |
| The composite's region checked as soon as it is made (TERRAIN.md 5.4 step 4, #356): a failed build skips the copy of the rest of the terrain, every decision unchanged | the filter, verify, fall-back pattern of adaptive predicates: J. R. Shewchuk, *Adaptive precision floating-point arithmetic and fast robust geometric predicates*, DCG 18, 1997; T. Bartels, V. Fisikopoulos, M. Weiser, *Fast floating-point filters for robust predicates*, arXiv 2208.00497, 2022 |
| The composite's terrain triangles next to a replaced one split at its points inside their shared edge (TERRAIN.md §11, no T-junctions) | conforming triangulations: hanging nodes removed by splitting the neighbour, as in red-green refinement (R. E. Bank, A. H. Sherman, A. Weiser, *Refinement algorithms and data structures for regular local mesh refinement*, Scientific Computing, IMACS 1983); the split by the library's exact CDT (`ExactTriangulation2`) |
| The certificate of the composite catch points' move (TERRAIN.md 5.5): the bound of a piecewise linear deviation at the vertices of the refinement, crossings as exact rational points, an interval filter before the exact evaluation | a-posteriori certification with a floating-point filter and an exact stage, as in adaptive predicates (J. R. Shewchuk, *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*, DCG 18, 1997); the extremes of a convex piecewise-linear function at the vertices of its cells (linear programming); a move below the resolution with a documented bound, as in snap rounding (J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 13, 1999) |
| The proven catch-line bound (TERRAIN.md 5.5, `CatchLine.ProvenUncertainty`): the true line in the band of the certified vertical error, per overlay face a convex band piece enclosed in interval boxes, the distance to a segment bounded at the box corners (convexity) | J. M. Snyder, *Interval analysis for computer graphics*, SIGGRAPH 1992 (enclosing level sets by interval evaluation); R. E. Moore, *Interval Analysis*, 1966 (outward rounding) |
| The `g3cli slope` command: toe lines read as GeoJSON, catch lines and target-height lines written as GeoJSON | H. Butler et al., *The GeoJSON Format*, RFC 7946, IETF 2016 (read with System.Text.Json; coordinates taken as given, projected, the `crs` member of the older 2008 GeoJSON specification passed through, not interpreted) |
| Volume per cell (compared) | D. Powell, T. Abel, *An exact general remeshing scheme applied to physically conservative voxelization*, J. Comput. Phys. 297, 2015. Code r3d: current files under a LANL / DOE notice (copy and use only), the 2015 code BSD-3 with an enhancements clause; not ported |

## Height grid readers (DGM)

`HeightGridReader` / `HeightGridWriter` / `HeightGrid` (`io/`). Format descriptions only, no code read or ported:

| Topic | Source |
|---|---|
| ESRI ASCII Grid (.asc) | ESRI, *ASCII to Raster* tool documentation (header ncols / nrows / xllcorner | xllcenter / yllcorner | yllcenter / cellsize / NODATA_value; north row first); the GDAL AAIGrid driver page describes the same format, including dx / dy |
| XYZ ASCII DGM (Geobasis NRW `dgm1_xyz`) | the open-data product description of the Land NRW DGM1 (one "x y z" point per line on a regular 1 m grid); the reader accepts any regular square lattice |
| Grid TIN with a fixed diagonal | Garland, Heckbert, *Fast Polygonal Approximation of Terrains and Height Fields*, CMU-CS-95-181, 1995 (regular grid triangulation as the full-resolution reference); the interpolation on the TIN is the library's own definition (`HeightGrid.Height`) |

## DGM to TIN with breaklines (`TerrainGridTin`)

`mesh_ops/TerrainGridTin.cs`, design in [TERRAIN_DGM_TO_TIN.md](../design/TERRAIN_DGM_TO_TIN.md). No code read or ported. The method is the library's own: error-driven rounds around the exact constrained
Delaunay mesher (`QualityMesh2`), with an exact error scan that follows from the piecewise linearity of both surfaces (the difference is linear on the cells of the overlay of two triangulations, so its maximum is at overlay
vertices; elementary). Context only: greedy insertion for terrain TINs (Garland and Heckbert 1995, see above), constrained Delaunay triangulation with breaklines (Chew 1989), and
the usual GIS distinction between constrained and conforming TINs with breaklines.

## ALKIS cadastral data (2D mesher benchmark, `benchmarks/Meshing2Benchmark/corpus/alkis_*.py`)

| Item | Source and license |
|---|---|
| Parcels (`Flurstueck`), buildings (`GebauedeBauwerk`) and land use (`Nutzung`) of the Liegenschaftskataster NRW, "Grundrissdaten vereinfacht" (GeoPackage per district, ETRS89 / UTM 32N) | Geobasis NRW, <https://www.opengeodata.nrw.de/produkte/geobasis/lk/akt/gru_vereinfacht_gpkg/>, license Datenlizenz Deutschland - Zero - Version 2.0 (dl-de/zero-2-0, <https://www.govdata.de/dl-de/zero-2-0>). Fetched at run time into a cache directory, prepared into plain text inputs, never committed. The source ("akt") changes over time: a result is reproducible per archive SHA-256, which the scripts print and write to `sample.json`. Test meshes built from it are not committed either. |

## ASCII XYZ point files (`XYZReader`, `XYZWriter`)

Own implementation of the informal format (one point per line, optional normals); there is no specification or source. The
column rule (values 4-6 are normals when there are six or more; further columns are ignored) is our decision, documented at
the reader. The PLY point cloud path is the existing `PLYReader` / `PLYWriter`.

## Point cloud normals (`PointCloudNormals`, design note `docs/design/POINT_CLOUD_RECONSTRUCTION.md`)

Implemented from the papers below; no code of any of them was read.

| Part | Source |
|---|---|
| Normal axis from a PCA of the nearest neighbours (tangent plane) | H. Hoppe, T. DeRose, T. Duchamp, J. McDonald, W. Stuetzle, *Surface reconstruction from unorganized points*, SIGGRAPH 1992, pp. 71-78 (bibliographic data checked by web search only) |
| The wrap of the cloud | `MeshAlphaWrap` (Portaneri et al. 2022, see its entry) |
| Sign by two probe points against the wrap (outer side = the probe outside the closed wrap) | own construction, not from a paper; the pairing "alpha wrap for normal orientation" was not found in the literature searched (design note section 2) |
| Spacing and noise estimates, alpha rule | own, measured on procedural torus and sphere samplings (design note section 4, `PointCloudNormalsTests`) |
| k-nearest neighbours | a k-d tree with median splits and bounded-distance pruning; textbook method (J. L. Bentley, Multidimensional binary search trees used for associative searching, CACM 1975, from memory), own code |

## Point cloud surface (`PointCloudSurface`, step C1 of the design note)

| Part | Source |
|---|---|
| Fast winding number of oriented points with area weights | G. Barill, N. G. Dickson, R. Schmidt, D. I. W. Levin, A. Jacobson, *Fast winding numbers for soups and clouds*, ACM TOG 37(4), Article 43, 2018, already in `PointAABBTree3.FastWindingNumber` (see the entry of that class); bibliographic data checked by web search only |
| Iso-surface of the winding number as the surface of an oriented cloud | the idea of using the winding number field of oriented points as the indicator follows the normal-orientation work of Xu et al. (GCNO 2023) and Barill et al.; this construction (0.5 iso-surface by marching cubes) is the standard use of a winding number field, no code taken |
| Local area of a point from the distance to its 8th neighbour (pi d^2 / 9) | own; a common density estimate, measured against a constant area in the design note (non-uniform sampling: 147 components with a constant area, 1 with the local area) |
| Blending the winding number field into the signed distance of the alpha wrap where there is no data | own construction (smoothstep over half the data radius), measured in the design note |
| Marching cubes | the library's `MarchingCubes` |

## 2D polygon booleans and offsets (`addons/geometry3Sharp.Clipper2`: `Clipper2Polygons`, `ClipperFrame`)

An optional package on top of Clipper2, whose C# 2.0.0 sources it carries (vendored, internal; since 2026-11-12, row below). The licence notices ship in the package as `THIRD-PARTY-NOTICES.txt`.

| Part | Source |
|---|---|
| Booleans, fill rules, open-path clipping, offsets (join and end types, miter limit, arc tolerance), Minkowski sums, path simplification | **Clipper2 2.0.0** by Angus Johnson, Boost Software License 1.0 (https://github.com/AngusJohnson/Clipper2; the `License.txt` in the NuGet package `Clipper2` is BSL-1.0, checked 2026-11-01). Vendored since 2026-11-12 (owner decision 2026-11-12, option (b) of [platform.md](../roadmap/platform.md#open)): the C# sources of `CSharp/Clipper2Lib` at tag `Clipper2_2.0.1`, commit `21ebba05db8894f0c7217ad35ea518080f324946` (the library files are the same as in the release commit `fbc392c` "Release ver. 2.0.0"; the csproj version is 2.0.0; there is no C# tag 2.0.0), BSL-1.0 checked in that tag (`CSharp/NuGet/License.txt`, `LICENSE`), copied to `addons/geometry3Sharp.Clipper2/Clipper2Lib/` with the licence file and the source headers; changes there are listed in its README (namespace `g3.Clipper2.Vendored`, top-level types internal). `HashCode.cs` carries its own MIT (.NET Foundation) and BSD-2 (xxHash, Yann Collet) notices. Since 2.0 of `geometry3Sharp.Clipper2` (owner 2026-11-13) the NuGet package `Clipper2` is no dependency; its public enums are its own, with Clipper2's names and values; the package stays a reference of the test project (differential tests). Not taken from the DAP fork's `Clipper2Wrapper.cs` ([audit](../design/DAP_BUGFIX_AUDIT.md#features-not-adopted-each-needs-its-own-provenance-decision)), which was not read for this |
| Fix of FINDINGS item 5 in the vendored `DoSplitOp` (a split-off triangle of the other sign is kept if it lies inside the rest of its path) | own analysis of the 2.0.0 code (FINDINGS item 5); the inside test is Clipper2's own `Path1InsidePath2`. Upstream's later change of the same function's caller (`4da1564`, issue #1067) was measured, not adopted |
| Guard of the "micro self-intersection" branch of the vendored `FixSelfIntersects` (#756: undone when it flips the ring's orientation) | own analysis of the 2.0.0 code (FINDINGS item 5, issue #756); upstream history read for the prior-art check: the branch was added in `272eb9b` (#960) and removed in `4da1564` (#1067, not adopted); the one-grid-unit bound considered first follows snap rounding (J. D. Hobby, CGTA 13, 1999; L. Guibas, D. Marimont, IJCGA 8, 1998) |
| Ports of C++ Clipper2 fixes into the vendored sources (2026-11-15, owner 2026-11-15): rounded and direction-independent intersection points and a stable intersection sort (`f774daf`), the order of edges leaving a shared vertex (`ec5f2d4`), the recursion guard of `CheckSplitOwner` (`f97d65d`); the per-path state of `ClipperOffset` (`6c7d083`) | **PolyClipping/Clipper2** (https://github.com/PolyClipping/Clipper2), the C++ continuation of Clipper2 by Seth Hillbrand after the archive, Boost Software License 1.0 (`LICENSE` of its `main`, checked 2026-11-15); translated from the commits' C++ diffs, listed in `addons/geometry3Sharp.Clipper2/Clipper2Lib/README.md`; the upstream tests of those commits are translated in `Clipper2UpstreamFixesTests` (`6c7d083`: `Clipper2OffsetStateTests`) |
| Single-point inner join of the vendored `ClipperOffset` (#1169): one miter point instead of the 3-point detour at a concave vertex where the offset edges meet inside both (the detour made the union of a dense ring O(n^2)) | own method (no external source); the join geometry is the standard raw-offset construction, cf. G. Chen, D. McMains, *Polygon Offsetting by Computing Winding Numbers*, ASME IDETC/CIE 2005; vendored change listed in `addons/geometry3Sharp.Clipper2/Clipper2Lib/README.md`, tests `Clipper2InnerJoinTests` |
| Exact area oracle of the Clipper2 tests (`ExactRegionArea`: winding numbers of the rings, vertical slabs at every vertex and crossing x, trapezoids in BigInteger rationals) | the slab (trapezoidal) decomposition of a planar arrangement: F. P. Preparata, M. I. Shamos, *Computational Geometry: An Introduction*, Springer 1985, chapter 2 (point location by the slab method); winding-number fill rules as in the row on the domain below |
| The integer grid (`ClipperFrame`: origin at the bounds center, power-of-two scale fitted to the extent plus the offset margin) | own construction, after the integer grid of `ExactMeshArrangement` |
| Filled Minkowski sum from the boundary sweep: A ⊕ B = (∂A ⊕ ∂B) ∪ (a + B) ∪ (A + b) for connected A, B | standard (eg M. de Berg et al., *Computational Geometry*, 3rd ed. 2008, ch. 13); own reasoning for the union with one copy each |

## Batch ray and point queries (`spatial/MeshBatchQueries.cs`, #448)

Design references of [BATCH_QUERIES.md](../design/BATCH_QUERIES.md); papers only, no code read or ported (Embree, Apache-2.0,
not read). `MeshBatchQueries` (merged in #448) runs the existing single queries in Morton-sorted order, in parallel.

| Topic | Source |
|---|---|
| Coherent ray packets | I. Wald, P. Slusallek, C. Benthin, M. Wagner, *Interactive Rendering with Coherent Ray Tracing*, Computer Graphics Forum 20(3), 2001, [doi:10.1111/1467-8659.00508](https://doi.org/10.1111/1467-8659.00508) |
| Ray groups (beams) | A. Reshetov, A. Soupikov, J. Hurley, *Multi-level ray tracing algorithm*, ACM TOG 24(3), 2005, [doi:10.1145/1073204.1073329](https://doi.org/10.1145/1073204.1073329); the method is patented by Intel (US 7,755,628, expired), and is not implemented |
| Wide BVH for incoherent rays | H. Dammertz, J. Hanika, A. Keller, *Shallow Bounding Volume Hierarchies for Fast SIMD Ray Tracing of Incoherent Rays*, Computer Graphics Forum 27(4), 2008, [doi:10.1111/j.1467-8659.2008.01261.x](https://doi.org/10.1111/j.1467-8659.2008.01261.x) |
| Ray sorting | K. Garanzha, C. Loop, *Fast Ray Sorting and Breadth-First Packet Traversal for GPU Ray Tracing*, Computer Graphics Forum 29(2), 2010, [doi:10.1111/j.1467-8659.2009.01598.x](https://doi.org/10.1111/j.1467-8659.2009.01598.x) |
| CPU ray tracing kernels, survey | I. Wald, S. Woop, C. Benthin, G. S. Johnson, M. Ernst, *Embree*, ACM TOG 33(4), 2014, [doi:10.1145/2601097.2601199](https://doi.org/10.1145/2601097.2601199); D. Meister et al., *A Survey on Bounding Volume Hierarchies for Ray Tracing*, Computer Graphics Forum 40(2), 2021, [doi:10.1111/cgf.142662](https://doi.org/10.1111/cgf.142662) |
| Watertight ray-triangle test | S. Woop, C. Benthin, I. Wald, *Watertight Ray/Triangle Intersection*, JCGT 2(1), 2013 (the library's exact test is a different, `Orient3d`-based decision) |
| Fast winding numbers | G. Barill, N. G. Dickson, R. Schmidt, D. I. W. Levin, A. Jacobson, *Fast Winding Numbers for Soups and Clouds*, ACM TOG 37(4), 2018, [doi:10.1145/3197517.3201337](https://doi.org/10.1145/3197517.3201337) |

## Continuous collision detection (`ContinuousCollision`, `MeshSweepCollision`, design note `docs/design/CCD.md`)

Implemented from the paper; no code of the reference implementation was read or ported (its MIT licence would allow it).

| Part | Source |
|---|---|
| Inclusion-based root finding over (t, u, v): the corner hull of a multilinear F, bisection, minimum separation, the earliest-time search | B. Wang, Z. Ferguson, T. Schneider, X. Jiang, M. Attene, D. Panozzo, *A Large-scale Benchmark and an Inclusion-based Algorithm for Continuous Collision Detection*, ACM TOG 40(5), art. 188, 2021, [doi:10.1145/3460775](https://doi.org/10.1145/3460775) (reference code Tight-Inclusion, MIT, not read) |
| Interval root finding as the fallback path | J. Snyder, *Interval analysis for computer graphics*, SIGGRAPH 1992; S. Redon, A. Kheddar, S. Coquillart, *Fast continuous collision detection between rigid bodies*, Eurographics 2002 |
| Separating directions (triangle normal, edge cross product) | the classical separating-axis directions of CCD culling (eg R. Bridson, R. Fedkiw, J. Anderson, *Robust treatment of collisions, contact and friction for cloth animation*, SIGGRAPH 2002); the test itself is own (any direction, dot products in `ExactInterval`) |
| The filter bound B1, the clipping of vertex-triangle boxes to the triangle, the dive-then-tighten search and its time-of-impact argument | own (docs/design/CCD.md sections 6 and 10) |
| Sub-step boxes in the broad-phase tree (`SubStepBoxTree`: one box per time segment in each node, a mask of the segments still overlapping) | the multi-segment motion-blur BVHs of ray tracing: L. Grünschloß, M. Stich, S. Nawaz, A. Keller, *MSBVH: An Efficient Acceleration Data Structure for Ray Traced Motion Blur*, HPG 2011; S. Woop, A. T. Áfra, C. Benthin, *STBVH: A Spatial-Temporal BVH for Efficient Multi-Segment Motion Blur*, HPG 2017; G. Zachmann, R. Weller, *Kinetic Bounding Volume Hierarchies for Deforming Objects*, VRCIA 2006 (the idea only; the tree and the mask are own, docs/design/CCD.md 10.11) |
| Benchmark data | the sample of the ToI dataset of D. Belgrod, Z. Ferguson et al., *Time of Impact Dataset for Continuous Collision Detection and a Scalable Conservative Algorithm*, arXiv:2112.06300; [Sample-Scalable-CCD-Data](https://github.com/Continuous-Collision-Detection/Sample-Scalable-CCD-Data), Apache-2.0, fetched at run time by `benchmarks/CcdBenchmark/fetch_sample.sh` (pinned commit, SHA-256 per file), never committed |
