# Sources

geometry3Sharp is under the Boost Software License. New code in this fork is written from published papers;
no code is taken over from other libraries unless it is under a compatible permissive license and named here.
Code under the GPL or LGPL (eg CGAL) is never used as a source.

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

## 2D loose quadtree (`LooseQuadtree2d`, point / segment / triangle adapters)

Clean-room implementation from [QUADTREE2D_SPEC.md](design/QUADTREE2D_SPEC.md) and published
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
benchmark predicates and comparison limits are documented in the [benchmark README](../benchmarks/QuadtreeBenchmark/README.md).

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
in the [pilot README](../benchmarks/OracleIndexPilot/README.md).

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

## Exact mesh booleans (`ExactMeshBoolean`, `ExactMeshArrangement`, `ExactPlanarSimplify`, `ExactTriangulation2`, `ExactPoint3`, `ExactPredicates`)

Implemented from the papers, no code taken over.

| Part | Source |
|---|---|
| Exact arrangement of the triangles, classification by winding numbers per patch; operations on any number of meshes as a predicate on the vector of their winding numbers (n-ary union / intersection, "at least k of n") | Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh Arrangements for Solid Geometry*, ACM TOG (SIGGRAPH) 2016 |
| Implicit intersection points (edge crossing a plane, three planes) with floating-point filters, exact evaluation only when needed | M. Attene, *Indirect Predicates for Geometric Constructions*, Computer-Aided Design 2020; G. Cherchi, M. Livesu, R. Scateni, M. Attene, *Fast and Robust Mesh Arrangements using Floating-point Arithmetic*, ACM TOG (SIGGRAPH Asia) 2020 |
| Booleans on arrangements | G. Cherchi, F. Pellacini, M. Attene, M. Livesu, *Interactive and Robust Mesh Booleans*, ACM TOG (SIGGRAPH Asia) 2022 |
| Expansion arithmetic, error bounds and adaptive stages B, C, D of orient2d / orient3d / incircle (`AdaptivePredicates`, own implementation from the paper, not from `predicates.c` or Triangle.NET) | J. R. Shewchuk, *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*, Discrete & Computational Geometry 1997 |
| Constrained Delaunay triangulation (constraint insertion by flips) | S. W. Sloan, *A fast algorithm for generating constrained Delaunay triangulations*, Computers & Structures 1993; C. L. Lawson 1977 (flips) |
| Vertex deletion in the constrained Delaunay kernel (`DelaunayKernel2.DeleteVertex`): ear clipping of the star's link, Lawson flips inside it | O. Devillers, *On deletion in Delaunay triangulations*, IJCGA 12, 2002 (arXiv cs/9907023, 1999): only the star changes; G. H. Meisters, *Polygons have ears*, 1975; C. L. Lawson 1977 and, for constraints, D. T. Lee, A. K. Lin 1986, L. P. Chew 1989 (the flip algorithm reaches the constrained Delaunay triangulation) |
| Removal of unneeded vertices in flat regions (`ExactPlanarSimplify`): ear clipping of the hole, Lawson flips | G. H. Meisters, *Polygons have ears*, American Mathematical Monthly 1975 (ear clipping); C. L. Lawson, *Software for C1 surface interpolation*, 1977 (flips towards Delaunay) |
| Box tree of the pair searches (`TriangleBoxTree`): above 4096 boxes sorted along a Morton curve of their centres (coordinates over the centres' box, or their ranks per axis where one far box would squeeze the others), each range split where the highest differing bit of the codes changes; small trees keep the median split (no code taken over; the layout of Geogram's `MeshFacetsAABB` was read, BSD-3) | C. Lauterbach, M. Garland, S. Sengupta, D. Luebke, D. Manocha, *Fast BVH Construction on GPUs*, Computer Graphics Forum (Eurographics) 2009; T. Karras, *Maximizing Parallelism in the Construction of BVHs, Octrees, and k-d Trees*, HPG 2012 (the split at the highest differing bit); B. Lévy, Geogram `mesh_AABB` (Inria, BSD-3) |
| Candidate triangle pairs in the proper case (`ExactMeshArrangement.apart_on_the_line`): decided by orientation signs alone before any point is constructed (docs/design/TRITRI_PREFILTER.md) | O. Devillers, P. Guigue, *Faster Triangle-Triangle Intersection Tests*, INRIA Research Report RR-4488, 2002 (J. Graphics Tools 8(1), 2003) |
| Generalized winding number for open meshes | A. Jacobson, L. Kavan, O. Sorkine-Hornung, *Robust Inside-Outside Segmentation using Generalized Winding Numbers*, ACM TOG 2013 (via the existing `FastWindingNumber`, Barill et al. 2018) |
| Open inputs where an open boundary lies on an edge that other pieces meet (`ConflictEdges`): the jumps of the winding numbers around the edge do not add up to zero, so no labelling of the regions around it fits all pieces (the input is not PWN there, the condition of Zhou et al. 2016); pieces that run the edge the same way are not joined | own reasoning |

The error bounds of the implicit points' approximations (`segment_point`, `plane_value`, `planes_approx`) and of
the filters on them are derived in the code comments.

The bit bounds of the fixed-width predicates (`FixedArith.Orient`, `FixedArith.InCircle`) are derived by
`tools/PredicateCompiler` (build-time tool, not shipped), own code: magnitude bounds over exact
integers. The idea of deriving the bounds of a predicate from its formula by machine follows A. Meyer, S. Pion,
*FPG: A code generator for fast and certified geometric predicates*, RNC 2008 (idea only, no code read).

The generated indirect predicates (`math/Generated/IndirectPredicates.cs`, orient3d of line-plane and three-plane
points without constructing them) follow M. Attene, *Indirect Predicates for Geometric Constructions*,
Computer-Aided Design 2020, and G. Cherchi, M. Livesu, R. Scateni, M. Attene, *Fast and Robust Mesh Arrangements
using Floating-point Arithmetic*, ACM TOG 2020 (the method from the papers; no code read). Generated by
`tools/PredicateCompiler`, own code.

The polyhedral envelope (`docs/design/ENVELOPE.md`; `mesh_ops/PolyhedralEnvelope.cs`, internal, own code) follows B. Wang, T. Schneider, Y. Hu,
M. Attene, D. Panozzo, *Exact and Efficient Polyhedral Envelope Containment Check*, ACM TOG 39(4), 2020; its reference
code `fast-envelope` (MIT) serves as a reference only; Geogram and the predicate libraries bundled with it were not
read for it. Since 2026-11-03 geogram's own BSD-3 code may be read and ported under the rules in [roadmap/sources-surveys.md](roadmap/sources-surveys.md#geogram-as-a-source-owner-2026-11-03) ("Geogram as a source").

## Rounding the exact output (`MeshRoundingRepair`, `ExactMeshBoolean.SafeRounding`, `ExactMeshBoolean.ComputeExactVertices`)

Implemented from the papers, no code taken over. Read for the design (what the exact methods do with their output):

| Part | Source |
|---|---|
| Local edits after rounding, each accepted only if it creates no intersection: contraction of short edges (link condition), flips of skinny triangles; removal of components thinner than the rounding distance; the d-separation argument (why rounding a separated mesh is safe) | V. Milenkovic, E. Sacks, *Geometric rounding and feature separation in meshes*, Computer-Aided Design 108, 2019 (arXiv 1805.03252). Their expansion and optimization stages (linear programs that separate the remaining close features before rounding) are not implemented in the default; instead single vertices are moved by tiny amounts after rounding, accepted under the same rule (a prototype of the expansion, after rounding, is the next row) |
| Contacts made by the rounding (`MeshRoundingRepair`, with the exact point id per vertex from `ExactMeshBoolean`): vertices of different exact points at one double position, separated after the passes by moving one of them a few ulps towards its neighbours or along its normal, accepted by the same exact rule as every edit | own construction; the idea that rounding must not create contacts the exact arrangement does not have is the feature separation of Milenkovic and Sacks 2019 (above); the contract for contacts the exact solid has (duplicated vertices, manifold in memory) is that of Manifold (Apache-2.0, documentation only: duplicate vertices allowed as a geometric property) |
| Separation prototype (option A of the snap-rounding experiment, measured and closed, on the branch `claude/snap-rounding-A-archive` only, `MeshRoundingRepair.Separate`): sequential linear programs over the vertices around a defect, slab constraints between vertex-disjoint near pairs, Δ-box trust region, maximize separation then minimize displacement; the exact d-separation certificate of the experiment (`tests/.../Harness/SeparationCertificate.cs`) | Milenkovic and Sacks 2019 (above), section 4 (expansion) [read in full, arXiv]. The area rows (each triangle around a defect kept above the degenerate area along a reference normal) after L. A. Freitag, P. Plassmann, *Local optimization-based simplicial mesh untangling and improvement*, IJNME 49, 2000 (the idea of maximizing the smallest signed area by a linear program; cited by Milenkovic and Sacks; paper not re-read in this session). One slack per row instead of one common s: own choice. No code exists to take over |
| Small dense LP solver (`math/DenseSimplex.cs`, with option A on the branch `claude/snap-rounding-A-archive` only): tableau simplex, slack basis, Dantzig's rule, Bland's rule against cycling | V. Chvátal, *Linear Programming*, Freeman 1983, chapters 2-3 (from memory, the cycling example of the tests is his); R. G. Bland, *New finite pivoting rules for the simplex method*, Mathematics of Operations Research 2(2), 1977. Textbook method, no code taken over. Considered and not needed (the result is checked exactly afterwards): D. Applegate, W. Cook, S. Dash, D. Espinoza, *Exact solutions to linear programming problems*, ORL 35, 2007; A. Gleixner, D. Steffy, K. Wolter, *Iterative refinement for linear programming*, INFORMS JoC 28, 2016; A. Neumaier, O. Shcherbina, *Safe bounds in linear and mixed-integer linear programming*, Math. Prog. 99, 2004 (references from the handoff, not checked here) |
| Snap fallback prototype (option B, `benchmarks/SnapRoundingExperiment/SnapFallback.cs`): vertices of the triangles still defective snapped to a grid, resolved again, iterated | L. Valque, S. Lazard, *Resolving self-intersections in 3D meshes while preserving floating-point coordinates*, CGF 44(5), 2025, and L. Valque, *3D Snap rounding*, thesis, Université de Lorraine, 2024 (the first version from the abstracts; the full texts were read by other sessions, SAFE_ROUNDING.md section 7, and the paper's fixed grid with hot voxels was added from those notes). CGAL's implementation is GPL and was not read |
| Needles collapsed, caps flipped | M. Botsch, L. Kobbelt, *A Robust Procedure to Eliminate Degenerate Faces from Triangle Meshes*, VMV 2001 |
| Strip zip (a strip of degenerate triangles and slivers along a line removed, both sides merged into one chain, bound 2 w) | Own construction, generalizing the zip of two caps; no source found. Related: M. Attene, *A lightweight approach to repairing digitized polygon meshes*, The Visual Computer 26(11), 2010 (removes growing regions of bad elements and fills the gaps, heuristic, no displacement bound; abstract only, MeshFix's GPL code not read); CGAL's `remove_almost_degenerate_faces` (needles collapsed, caps flipped; documentation only, GPL code not read) |
| Snap rounding in 2D: hot pixels, iterated snap rounding (each vertex half a pixel from every non-incident edge) | J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 1999; D. Halperin, E. Packer, *Iterated Snap Rounding*, CGTA 23(2), 2002. Background only: the 2D methods have no direct 3D counterpart |
| Rounding polyhedral meshes in 3D with guarantees (Hausdorff distance at most 3/2 grid units, no new intersections; worst case O(n^13) size) | O. Devillers, S. Lazard, W. Lenhart, *Rounding meshes in 3D*, Discrete & Computational Geometry 64, 2020 (SoCG 2018: *3D snap rounding*); S. Fortune, *Vertex-rounding a three-dimensional polyhedral subdivision*, DCG 1999. Not implemented: the output size makes them impractical for meshes of 10^5 triangles |
| Iterative snap rounding of the vertices of intersecting triangles (and of their hot voxels) to a scaled integer grid, re-subdivided until no intersection remains (option B of `docs/design/SAFE_ROUNDING.md`, section 7; not implemented) | L. Valque, *3D Snap rounding*, PhD thesis, Université de Lorraine, 2024, HAL tel-05016163 (read in full, chapters 12–17); S. Lazard, L. Valque, *Resolving self-intersections in 3D meshes while preserving floating-point coordinates*, Computer Graphics Forum 44(5), 2025, DOI 10.1111/cgf.70197, preprint HAL hal-04907149 (abstract only, the full text was not reachable). CGAL's implementation (GPL) not read |
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
| Diametral lenses without `Conforming`, up to the angle bound arcsin(√3/4) = 25.65° proven for them (`QualityMesh2.Refiner.InLens`), with the paper's deletion of the free vertices in a subsegment's diametral circle that see its midpoint before it is bisected (`QualityMesh2.Refiner.DeleteFreeInCircle`, Chew 1993; the kernel's `DeleteVertex`); termination measured, not proven (off-centres, concentric shells and the snap grid differ from the analysed algorithm) | J. R. Shewchuk 2002 (above), section 6.4; read from the draft of May 21, 2001 |
| Oracle (`Meshing2Benchmark`): a triangle's small angle forced by an input angle phi excuses it only when its other angles are at least arctan(sin phi / (2 − cos phi)) (`ForcedByInputAngle`, `AtLeastShewchukBound`; the name is historical) | G. L. Miller, S. E. Pav, N. J. Walkington, *When and why Ruppert's algorithm works*, IJCGA 2005, as stated in its abstract (not yet checked against the full text); J. R. Shewchuk 2002 (above) proves the weaker arcsin(sin(phi/2)/√2) for the Terminator (Theorem 12, section 6.3), which the oracle does not use (corrected in #396) |
| Small-angle rule in the constrained-quality mode (`QualityMesh2.Refiner.ApexExempt`, `Conforming = false`): a subsegment encroached by a refinement vertex on exactly one source c is not split when its source and c meet exactly (`MeetKind`) at an input angle below the bound, inside the apex region (a shell subsegment ending on c, or radii from the apex within 1.5); input, crossing and multi-source vertices still encroach. A geometric approximation of the insertion-radius cure and its exceptions; termination measured, not proven (`docs/design/MESHING2D_SMALL_ANGLES.md`) | J. R. Shewchuk 2002 (above), section 6.2 (the "precise rule" and its exception for a parent that is an input vertex or lies on a nonincident segment), checked against the published text (printed pp. 29-36) |
| Oracle (`Meshing2Benchmark`, rule O-H5 (ii')): the rounding of the input alone includes the restore step of snap rounding with restore, recomputed from the paper (`RestorePixels`: rounds of routing and critical pixels, ChoosePixel's rules R1 and R3 and a widened search, all choices of a round made on one arrangement); written from the paper and MESHING2D_SNAP.md section 3b, not from `SnapRounding2`, and compared with it in tests | A. Belussi, S. Migliorini, M. Negri, G. Pelagatti, *Snap rounding with restore: an algorithm for producing robust geometric datasets*, ACM TSAS 2(1), 2016 (Table II, Proposition 4.17); J. D. Hobby 1999 (above) |
| Off-centres: a triangle below the angle bound is split on the perpendicular bisector of its shortest edge pq, where the triangle of pq and the new point has the bound at the point (|pq| cot(theta/2) / 2 from the midpoint), or at its circumcentre if that is closer (`QualityMesh2.Refiner.OffCentre`) | A. Üngör, *Off-centers: A New Type of Steiner Points for Computing Size-Optimal Quality-Guaranteed Delaunay Triangulations*, LATIN 2004 |
| Grading in `QualityMesh2` with an angle bound: the split point of a triangle bad by its angle chosen among candidates in the petal of its shortest edge (the circumcentres of nearby triangles inside it and seven points of its arc, `QualityMesh2.Refiner.PetalCandidates`; step 1's points on the bisector behind a switch) by the bad triangles it removes and creates, never with a smaller insertion radius than the off-centre or circumcentre (`QualityMesh2.Refiner.BestCandidate`); bad triangles split worst first (`TriangleQueue`, own) | H. Erten, A. Üngör, *Triangulations with Locally Optimal Steiner Points*, Eurographics Symposium on Geometry Processing 2007 (read 2026-11-04; the journal version, SIAM J. Sci. Comput. 2009, not read): the idea of choosing among points and the petal (Algorithm 1); the arc's rational parametrisation, the score and the floor own (`docs/design/MESHING2D_GRADING.md` 4.3) |
| The Meshing2 oracle's fast path (test tool, `benchmarks/Meshing2Benchmark/Oracle`): a checker independent of and simpler than the mesher, its indexes and filters each with an equivalence argument and a check against the full scan (`docs/design/MESHING2D_FAST_ORACLE.md`). Source coverage reuses the existing tube boxes and BoxGrid, with a bounded query and exact scan fallback; diagnostics and full-report controls are in `benchmarks/OracleSourcePilot` | R. M. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 2011 (the approach; the checks are own work) |
| Integer coordinates for a robust common subdivision (not implemented; why the tracing suffices is in OPERATIONS.md) | M. Gillespie, N. Sharp, K. Crane, *Integer Coordinates for Intrinsic Geometry Processing*, ACM TOG (SIGGRAPH Asia) 2021 |
| Intrinsic Delaunay triangulations, non-negative cotan weights, flip algorithm | A. I. Bobenko, B. A. Springborn, *A Discrete Laplace-Beltrami Operator for Simplicial Surfaces*, Discrete & Computational Geometry 2007; C. Indermitte, T. M. Liebling, M. Troyanov, H. Clémençon, *Voronoi diagrams on piecewise flat surfaces and an application to biological growth*, Theoretical Computer Science 2001 (termination of the flips) |
| Intrinsic mollification of degenerate triangles | N. Sharp, K. Crane, *A Laplacian for Nonmanifold Triangle Meshes*, Computer Graphics Forum (SGP) 2020 |
| Heat method for geodesic distance (also on the intrinsic triangulation) | K. Crane, C. Weischedel, M. Wardetzky, *Geodesics in Heat*, ACM TOG 2013; *The Heat Method for Distance Computation*, CACM 2017 |
| Vector heat method: connection Laplacian, parallel transport, scalar extension, log map | N. Sharp, Y. Soliman, K. Crane, *The Vector Heat Method*, ACM TOG 2019 |
| Geodesic paths and loops by edge flips (`MeshGeodesicPath`): FlipOut on the wedge where the path turns by less than pi, the outer arc as the new path, smallest angle first; waypoints | N. Sharp, K. Crane, *You Can Find Geodesic Paths in Triangle Meshes by Just Flipping Edges*, ACM TOG (SIGGRAPH Asia) 2020 (geometry-central, MIT, has an implementation; not used as a source) |
| Log map direction from the gradient of half the squared distance against the transported reference vector | Gauss lemma (eg M. do Carmo, *Riemannian Geometry*, 1992, ch. 3); own derivation, replaces the paper's diffused radial field, which is biased by the shape of the source's one-ring |
| Heat flow far beyond the double range: the Cholesky substitutions of an M-matrix only add non-negative terms, so they run unchanged on logarithms (`SparseCholesky.SolveLog`) | Own derivation (sign pattern of the Cholesky factor of an M-matrix, eg R. A. Horn, C. R. Johnson, *Topics in Matrix Analysis*, 1991, ch. 2.5) |
| Kahan's stable formula for the triangle area from its sides | W. Kahan, *Miscalculating Area and Angles of a Needle-like Triangle*, 2014 |

The comparison with `MeshLocalParam` (the discrete exponential map, R. Schmidt, C. Grimm, B. Wyvill,
*Interactive Decal Compositing with Discrete Exponential Maps*, ACM TOG 2006) is in the tests.

## Alpha wrapping (`MeshAlphaWrap`, `Delaunay3`, `ExactPredicates.InSphere`)

Implemented from the papers, no code taken over. CGAL's implementation (`Alpha_wrap_3`, GPL) was not used.

| Part | Source |
|---|---|
| Alpha wrapping: carving a Delaunay triangulation from outside through facets an alpha ball fits through, Steiner points on the offset surface where the dual Voronoi edge crosses it (rule 1) or where a cell intersects the input (projection of the circumcenter, rule 2), manifold post-processing by moving outside cells to the inside | C. Portaneri, M. Rouxel-Labbé, M. Hemmer, D. Cohen-Steiner, P. Alliez, *Alpha Wrapping with an Offset*, ACM TOG 41(4) (SIGGRAPH) 2022, doi:10.1145/3528223.3530152; author version <https://inria.hal.science/hal-03688637> (reading notes: [design/ALPHA_WRAP_PAPER_NOTES.md](design/ALPHA_WRAP_PAPER_NOTES.md)) |
| Incremental Delaunay triangulation by cavities | A. Bowyer, *Computing Dirichlet tessellations*, The Computer Journal 1981; D. F. Watson, *Computing the n-dimensional Delaunay tessellation with application to Voronoi polytopes*, The Computer Journal 1981 |
| Point location by a stochastic walk | O. Devillers, S. Pion, M. Teillaud, *Walking in a triangulation*, IJFCS 2002 |
| InSphere determinant and its floating-point error bound (filter; the uncertain cases are evaluated in integer arithmetic) | J. R. Shewchuk 1997 (see above) |
| Closest point on a triangle (Voronoi regions), separating axis test of tetrahedron and triangle | C. Ericson, *Real-Time Collision Detection*, 2005 |

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
at). Design and measurements: [design/ALPHA_WRAP_QUALITY.md](design/ALPHA_WRAP_QUALITY.md).

| Part | Source |
|---|---|
| Worst-first local operations (split, collapse, relocation) that must not create an angle below the current minimum, no flips, feature vertices fixed (`WorstFirst`, `EnableCollapses`, `PinCreaseVerticesDegrees`, `FollowReference`) | K. Hu, D.-M. Yan, D. Bommes, P. Alliez, B. Benes, *Error-Bounded and Feature Preserving Surface Remeshing with Minimal Angle Improvement*, IEEE TVCG 2017 |
| Caps (large angles) spoil the gradient and the normal (cap splits and cap flips) | I. Babuška, A. K. Aziz, *On the angle condition in the finite element method*, SIAM J. Numer. Anal. 1976; J.-M. Morvan, B. Thibert, *Approximation of the normal vector field and the area of a smooth surface*, Discrete Comput. Geom. 2004 |
| Quality measures (min and max angle reported separately) | J. R. Shewchuk, *What is a good linear finite element?*, 2002 |
| The guard of every operation: `TriangleDeviationBounds.IsWithin` with the patch bound, the exact self-intersection test, the final one-sided Hausdorff certification | this library (#280, `MeshHausdorffDistance`) |
## Parameterization (`LSCMParameterization`, `ARAPParameterization`, `SLIMParameterization`, `BoundaryFirstFlattening`, `TutteEmbedding`, `ParameterizationDistortion`, `UVPacker`)

Implemented from the papers, no code taken over.

| Part | Source |
|---|---|
| Least squares conformal maps, as conformal energy (Dirichlet energy minus signed area over the boundary) | B. Lévy, S. Petitjean, N. Ray, J. Maillot, *Least Squares Conformal Maps for Automatic Texture Atlas Generation*, ACM TOG (SIGGRAPH) 2002; M. Desbrun, M. Meyer, P. Alliez, *Intrinsic Parameterizations of Surface Meshes*, Eurographics 2002; P. Mullen, Y. Tong, P. Alliez, M. Desbrun, *Spectral Conformal Parameterization*, SGP 2008 (equivalence) |
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
(analytic gradient and modified Hessian). The implementation specializes the lifted convex Hessian to 2D,
uses the existing constrained sparse Cholesky solver and bounded Armijo backtracking. Normalization, result
retention and validation are local integration choices. No authors' implementation, libigl, GPL/MPL or native
AD code was read or ported. SLIM's publication and the previously MIT-checked TinyAD README were architectural
prior art only. Conditional convergence (section 4.2) is not an iteration-bound guarantee; section 3.3's global
overlap limitation remains. See [design and limits](design/FOLDOVER_FREE_MAPS.md).

## Minimal enclosing circle (`ContMinCircle2`)

Replaces the WildMagic port. Implemented from one paper, no code taken over: E. Welzl, *Smallest enclosing disks (balls and ellipsoids)*, 1991. The three nested loops were derived from Welzl's recursion; no paper by Seidel was consulted for them.

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

## Distances and simplification (`MeshHausdorffDistance`, `TriangleDeviationBounds`, `Reducer`, `MeshEnvelope`)

The sources named in the code:

| Part | Source |
|---|---|
| Hausdorff distance with guaranteed bounds (branch and bound over triangles, cascading upper bounds; `TriangleDeviationBounds`, also used by `Reducer.MaxDeviation` and, per triangle, by `MeshDeviation.ComputeTriangleBounds`) | L. Sacht, A. Jacobson, *Cascading upper bounds for triangle soup Pompeiu-Hausdorff distance*, SGP 2024 |
| Patch bound (`TriangleDeviationBounds.PatchBound`, opt-in through `MeshHausdorffDistance.PatchBound`, used by `MeshRobustRepair`'s simplification of the alpha wrap): the target triangles under a triangle, seen along its normal, bound the distance by the largest height over the covered part, if their projections cover the triangle (coverage shown by the boundary edges of a consistently oriented, positively projecting patch) | own construction, an extra bound in the cascade of Sacht and Jacobson (above); the coverage argument is the boundary of a union of oriented triangles (a planar map) |
| Regularized optimal collapse point (`Reducer.QuadricRegularization`) | P. Trettner, L. Kobbelt, *Fast and Robust QEF Minimization using Probabilistic Quadrics*, Computer Graphics Forum (Eurographics) 2020 |
| Reduction on fixed slabs in parallel with frozen borders and confinement (`PartitionedReducer`, `PartitionedReduction`) | own construction (the argument is in the class documentation of `PartitionedReduction`); the approach of partitioned out-of-core simplification: P. Lindstrom, *Out-of-core simplification of large polygonal models*, SIGGRAPH 2000; Ozaki, Kyota, Kanai, Eurographics PGV 2015 (found by search, not read); quadrics: M. Garland, P. Heckbert, SIGGRAPH 1997 | none (no code read) |
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
| Sample cells of the volume check (`VolumeSampleCells`, `VolumeIoUAtLeast` on winding-closed meshes): an adaptive octree with inside, outside and boundary cells; a cell no triangle touches has a constant (integer) winding number, classified at its centre and counted with its exact volume (stratified sampling) | D. Meagher, *Geometric modeling using octree encoding*, Computer Graphics and Image Processing 19(2), 1982; winding numbers as above (Jacobson et al. 2013; Barill, Dickson, Schmidt, Levin, Jacobson, *Fast Winding Numbers for Soups and Clouds*, ACM TOG (SIGGRAPH) 2018); stratified sampling and the Wilson score interval: standard (W. G. Cochran, *Sampling Techniques*, 3rd ed., 1977, ch. 5; E. B. Wilson, JASA 1927); the combined interval of the ratio from the corners of two intervals and the cover argument: own reasoning ([design/VOLUME_CHECK_SAMPLING.md](design/VOLUME_CHECK_SAMPLING.md) section 4) |
| Exact ray-triangle decisions (`ExactRayTriangle`; `IntrRay3Triangle3`, `DMeshAABBTree3` ray queries and `IsInside`, `VolumeSampleCells.ExactWinding`): the line-triangle test by the signs of the line against the three edges (Plücker coordinates, here as `Orient3d` of two line points and an edge), closed triangles for hits, Simulation of Simplicity for single-counted crossings (the ε terms derived in the file), the second ray point at a power-of-two scaled direction | J. Plücker 1865 / K. Shoemake, *Plücker coordinate tutorial*, Ray Tracing News 11(1), 1998; N. Platis, T. Theoharis, *Fast ray-tetrahedron intersection using Plücker coordinates*, JGT 8(4), 2003; H. Edelsbrunner, E. P. Mücke, *Simulation of Simplicity*, ACM TOG 1990; S. Woop, C. Benthin, I. Wald, *Watertight ray/triangle intersection*, JCGT 2(1), 2013 (the watertightness goal; its shear scheme is not used); the predicates as in Shewchuk 1997 (above) |
| Exact triangle-box overlap (`VolumeSampleCells.TriangleTouchesBox`): the separating axis theorem with the 13 axes of a triangle and a box, each decided by the signs of exact orientation predicates | T. Akenine-Möller, *Fast 3D Triangle-Box Overlap Testing*, Journal of Graphics Tools 6(1), 2001 (the axes); signs from `ExactPredicates` (Shewchuk 1997) |
| Outer faces for `OuterVolumeIoU`: the outer surface rule of `OuterSurface` (winding numbers on both sides) per face, iterated on what is left until no face is removed | own reasoning (on the winding number definitions above) |
| Discrete curvature per vertex (`MeshCurvature`): mixed Voronoi areas, mean curvature normal, Gaussian curvature by the angle deficit | M. Meyer, M. Desbrun, P. Schröder, A. H. Barr, *Discrete Differential-Geometry Operators for Triangulated 2-Manifolds*, VisMath 2002 (Springer 2003) |
| Mean curvature normal in the area gradient form (equivalent to the cotan formula, no cotangents) | M. Desbrun, M. Meyer, P. Schröder, A. H. Barr, *Implicit Fairing of Irregular Meshes using Diffusion and Curvature Flow*, SIGGRAPH 1999; U. Pinkall, K. Polthier, *Computing Discrete Minimal Surfaces and Their Conjugates*, Experimental Mathematics 1993 |
| Distance of the curvature distributions: 1-Wasserstein distance of the empirical distributions (integral of the difference of the distribution functions) | standard (eg C. Villani, *Optimal Transport: Old and New*, 2009, ch. 6; on the real line the L1 distance of the distribution functions) |

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
| Implementation of 1a: interval arithmetic with outward rounding as the filter of the exact predicates; the planar map as a half-edge structure; exact signs as in Shewchuk's predicates | R. E. Moore, *Interval Analysis*, Prentice-Hall 1966; D. E. Muller, F. P. Preparata, *Finding the intersection of two convex polyhedra*, TCS 1978 (doubly connected edge list), and de Berg et al., ch. 2; J. R. Shewchuk, *Adaptive precision floating-point arithmetic and fast robust geometric predicates*, DCG 1997. The domain is triangulated with the library's `ExactTriangulation2` (Sloan 1993, Lawson), the faces are found with `LooseQuadtree2d` (public API) |
| Implementation of 1b: the overlay of the slope surface with the terrain by vertex identities, decided by `Orient2d` with Simulation of Simplicity (the terrain shifted by (ε, ε²), the ε terms decided by sign comparisons of coordinate differences); the sign of S − T at overlay vertices by `Orient3d`; faces of two convex triangles from their mutual containment and edge crossings; the region by union-find over faces; partial triangles by the library's `ExactTriangulation2` (exact CDT) and parity labelling | H. Edelsbrunner, E. P. Mücke, *Simulation of Simplicity*, ACM TOG 1990; de Berg et al., ch. 2 (map overlay); J. O'Rourke et al. 1982 (above); R. E. Tarjan, *Efficiency of a good but not linear set union algorithm*, J. ACM 1975 (union-find); the predicates as in Shewchuk 1997 (above) |
| Phase 1c, randomized insertion with a canonical result (`docs/design/TERRAIN_1C_PLAN.md`, section 9): the pieces in a seeded random order; the labels the lexicographic minimum of (height, plane id); the map's components cut along a ray from each reflex corner (extended to the component's boundary, rays not stopped by rays, so the parts do not depend on their order), each part one convex face; the faces in the exact lexicographic order of their corners | K. L. Clarkson, P. W. Shor, *Applications of random sampling in computational geometry II*, DCG 4, 1989 (randomized incremental construction); J. M. Keil, *Polygon decomposition*, in J.-R. Sack, J. Urrutia (eds.), *Handbook of Computational Geometry*, Elsevier 2000 (convex decomposition by segments from the reflex vertices); D. E. Knuth, *The Art of Computer Programming* vol. 2, Algorithm 3.4.2 P (the shuffle); G. L. Steele, D. Lea, C. H. Flood, *Fast splittable pseudorandom number generators*, OOPSLA 2014 (SplitMix64); own construction for the tie rule and the order |
| Phase 1c, mixed cut and fill (`Direction.Auto`): the clamp max(S_fill, min(T, S_cut)) from a cut run and a fill run on the terrain, valid iff neither run fails its toe check (`ToeUndercut`, `ToeOvertopped`); one composite of both overlays, the terrain triangles both cut triangulated once with both overlays' constraints (`SlopeComposite.Partner`). Target heights: the level lines of the slope region at z = h, per triangle the segment between its edge crossings (a vertex at h counts as above), chained across shared edges | own derivation for the condition (TERRAIN.md appendix A); the level lines by marching triangles, eg M. van Kreveld, *Efficient methods for isoline extraction from a TIN*, IJGIS 10, 1996; the composite as in the 1b row above |
| The repair of the band [64·e_xy, `MinFeatureSize`) (TERRAIN.md 5.4, #350, #354): needles at near-quadruple and near-quintuple points merged by edge collapses, in passes, the cumulative move of every original position capped by the band radius, funded by the facets' slack | the cluster and cumulative-drift pattern of snap rounding: J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 13, 1999; D. Halperin, E. Packer, *Iterated snap rounding*, CGTA 23, 2002; E. Packer, *Iterated snap rounding with bounded drift*, CGTA 40, 2008; the collapse's link condition: T. K. Dey, H. Edelsbrunner, S. Guha, D. V. Nekhayev, *Topology preserving edge contraction*, Publ. Inst. Math. (Beograd) 66, 1999; own derivation of the bound (TERRAIN.md 5.4) |
| The opt-in local frame (`LocalFrame`, TERRAIN.md 5.8.1, #355): the inputs translated to a local origin, every translation checked exact by TwoSum, the outputs kept local with `UsedOrigin` | the practice of storing data relative to an origin: the LAS point cloud format's scale and offset (ASPRS LAS 1.4 specification, 2019); the relative-to-centre vertices of 3D Tiles (`CESIUM_RTC`, Cesium) and P. Cozzi, K. Ring, *3D Engine Design for Virtual Globes*, CRC 2011, ch. 5; IFC's `IfcMapConversion` (buildingSMART IFC 4.3); TwoSum: D. E. Knuth, *The Art of Computer Programming* vol. 2, 4.2.2; P. H. Sterbenz, *Floating-Point Computation*, 1974 (exact differences within a factor 2) |
| The composite's region checked as soon as it is made (TERRAIN.md 5.4 step 4, #356): a failed build skips the copy of the rest of the terrain, every decision unchanged | the filter, verify, fall-back pattern of adaptive predicates: J. R. Shewchuk, *Adaptive precision floating-point arithmetic and fast robust geometric predicates*, DCG 18, 1997; T. Bartels, V. Fisikopoulos, M. Weiser, *Fast floating-point filters for robust predicates*, arXiv 2208.00497, 2022 |
| The composite's terrain triangles next to a replaced one split at its points inside their shared edge (TERRAIN.md §11, no T-junctions) | conforming triangulations: hanging nodes removed by splitting the neighbour, as in red-green refinement (R. E. Bank, A. H. Sherman, A. Weiser, *Refinement algorithms and data structures for regular local mesh refinement*, Scientific Computing, IMACS 1983); the split by the library's exact CDT (`ExactTriangulation2`) |
| The certificate of the composite catch points' move (TERRAIN.md 5.5): the bound of a piecewise linear deviation at the vertices of the refinement, crossings as exact rational points, an interval filter before the exact evaluation | a-posteriori certification with a floating-point filter and an exact stage, as in adaptive predicates (J. R. Shewchuk, *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*, DCG 18, 1997); the extremes of a convex piecewise-linear function at the vertices of its cells (linear programming); a move below the resolution with a documented bound, as in snap rounding (J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 13, 1999) |
| The `g3cli slope` command: toe lines read as GeoJSON, catch lines and target-height lines written as GeoJSON | H. Butler et al., *The GeoJSON Format*, RFC 7946, IETF 2016 (read with System.Text.Json; coordinates taken as given, projected, the `crs` member of the older 2008 GeoJSON specification passed through, not interpreted) |
| Volume per cell (compared) | D. Powell, T. Abel, *An exact general remeshing scheme applied to physically conservative voxelization*, J. Comput. Phys. 297, 2015. Code r3d: current files under a LANL / DOE notice (copy and use only), the 2015 code BSD-3 with an enhancements clause; not ported |

## 2D polygon booleans and offsets (`addons/geometry3Sharp.Clipper2`: `Clipper2Polygons`, `ClipperFrame`)

An optional package that calls a library; no code taken over.

| Part | Source |
|---|---|
| Booleans, fill rules, open-path clipping, offsets (join and end types, miter limit, arc tolerance), Minkowski sums, path simplification | **Clipper2 2.0.0** by Angus Johnson, Boost Software License 1.0 (https://github.com/AngusJohnson/Clipper2; the `License.txt` in the NuGet package `Clipper2` is BSL-1.0, checked 2026-11-01). A package dependency of `geometry3Sharp.Clipper2`, not of `geometry3Sharp`. Only its public API is used. Not taken from the DAP fork's `Clipper2Wrapper.cs` ([audit](design/DAP_BUGFIX_AUDIT.md#features-not-adopted-each-needs-its-own-provenance-decision)), which was not read for this |
| The integer grid (`ClipperFrame`: origin at the bounds center, power-of-two scale fitted to the extent plus the offset margin) | own construction, after the integer grid of `ExactMeshArrangement` |
| Filled Minkowski sum from the boundary sweep: A ⊕ B = (∂A ⊕ ∂B) ∪ (a + B) ∪ (A + b) for connected A, B | standard (eg M. de Berg et al., *Computational Geometry*, 3rd ed. 2008, ch. 13); own reasoning for the union with one copy each |

## Test harness (`tests/geometry3Sharp.Tests/Harness/`)

Implemented from the paper, no code taken over.

| Part | Source |
|---|---|
| Test-case minimization (`DeltaDebug.Minimize`, `MeshMinimizer`): ddmin over components, triangles, unused vertices and the bytes or lines of reader inputs | A. Zeller, R. Hildebrandt, *Simplifying and Isolating Failure-Inducing Input*, IEEE Transactions on Software Engineering 28(2), 2002 |
| Boolean oracles (`BooleanOracle`): solid angle of a triangle for the winding number probes | A. Van Oosterom, J. Strackee, *The Solid Angle of a Plane Triangle*, IEEE Transactions on Biomedical Engineering 30(2), 1983 |
| Analytic box reference, exact dyadic volumes and areas, representation changes (`BooleanOracle`) | own construction (docs/design/BOOLEAN_PROPERTY_TESTS.md) |

Test-only package: **CsCheck 4.9.1** (Apache-2.0, https://github.com/AnthonyLloyd/CsCheck, NuGet metadata checked),
referenced by `tests/geometry3Sharp.Tests` on net10.0 only with `PrivateAssets="all"`, for generation and shrinking
in `ExactBooleanPropertyTests.CsCheck.cs`. No code is copied from it and it is not a dependency of the library
package. 4.9.1 (2026-09-17, targets net8.0) rather than 5.0.0, which was a week old when it was added (2026-10-24).

## 2D meshing (`DelaunayMesh2`, `TriangulationMesh2`, `ConstrainedDelaunayMesh2`)

Clean room ([MESHING2D_SPEC.md](design/MESHING2D_SPEC.md)): implemented from the papers below. Triangle and Triangle.NET were not looked at.

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

## Build-time analyzers (`BannedSymbols.txt`, `tools/analyzers/G3ExactCodeAnalyzers`)

Build-time only, not shipped in the package. `Microsoft.CodeAnalysis.BannedApiAnalyzers` (MIT, NuGet) enforces the
banned APIs in the exact code; the analyzers G3E0001-3 (float, parallel loops, FMA) are own code on the Roslyn API (`Microsoft.CodeAnalysis.CSharp`,
MIT).

## Benchmarks

`benchmarks/Meshing2Benchmark` is the acceptance harness of the 2D mesher (`docs/design/MESHING2D_PLAN.md`). NetTopologySuite 2.6.0
(BSD-3-Clause) is its black-box reference for the M1 differential, in that project only. Its oracle (`Oracle/`) is written from
definitions: empty circumcircles and the local Delaunay criterion (B. Delaunay 1934; C. L. Lawson 1977; D. T. Lee, A. K. Lin,
*Generalized Delaunay triangulation for planar graphs*, 1986; L. P. Chew, *Constrained Delaunay triangulations*, 1989), and
monotone chain hulls (A. M. Andrew 1979). Its random source is SplitMix64 (G. L. Steele, D. Lea, C. H. Flood, *Fast splittable
pseudorandom number generators*, OOPSLA 2014). Triangle and Triangle.NET were not looked at; `MESHING2D_GOLDEN_FORMAT.md` defines how a separate session may compare with them as black boxes.

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
| `thingi-pairs`, `thingi-pairs-size` | pairs of Thingi10K models (https://huggingface.co/datasets/Thingi10K/Thingi10K, metadata and raw meshes), fetched at run time by `data/thingi_pairs.py`; only the lists of ids, seeds and transforms are committed. Protocol after Trettner, Nehring-Wirxel, Kobbelt, *EMBER*, SIGGRAPH 2022 (from the paper only; no EMBER code) | individual licenses per model (mostly Creative Commons); the lists are part of this repository (Boost) |

The Open Boolean Benchmark (https://github.com/Open-Boolean-Benchmark/boolean-benchmark-runners, MIT) defines
the request/result protocol that `BooleanComparison obb` implements; `obb-runner/` follows its runner layout.
No code of the runners repository was taken over. Its benchmark cases (Nefertiti, dental scans, iterated cube
grid) are not published; `cube_grid_*` of the `sequence` class is a case of our own modelled after the
description of the iterated cube grid.

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

`Remesher.TargetEdgeLengthF` / `RemeshSizing.FromWallThickness`: sizing below the local feature size, as in
isotropic remeshing with a sizing field (Botsch, Kobbelt, Pauly, Alliez, Lévy, *Polygon Mesh Processing*, ch. 6); the
thickness is `MeshWallThickness`'s inscribed sphere (Ma, Bai, Hu, Kim 2012). Own design otherwise.

`MeshConstraintUtil.PinSharpVertices`: the angle deficit of `MeshCurvature` (Meyer, Desbrun, Schröder, Barr 2003, eq. 9)
as the measure of a vertex feature; the threshold is own design.

## Research review: reducer and remesher guarantees (not implemented)

Sources recommended by [design/RESEARCH_REDUCER_REMESHER.md](design/RESEARCH_REDUCER_REMESHER.md) for upper-bound
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
| Reducer / Remesher 2.0 proposal ([design/REDUCER_REMESHER_20.md](design/REDUCER_REMESHER_20.md)): QEM variants and parallel / partitioned simplification (approach only, not implemented) | M. Garland, P. Heckbert, *Surface simplification using quadric error metrics*, SIGGRAPH 1997; P. Lindstrom, G. Turk, *Fast and memory efficient polygonal simplification*, IEEE Visualization 1998; H. Hoppe, *New quadric metric for simplifying meshes with appearance attributes*, IEEE Visualization 1999; P. Lindstrom, *Out-of-core simplification of large polygonal models*, SIGGRAPH 2000; M. Franc, V. Skala, *Parallel triangular mesh decimation without sorting*, SCCG 2001; Ozaki, Kyota, Kanai, partition-based out-of-core QEM simplification, Eurographics PGV 2015 | papers only, citations of the last four from memory or a search hit: verify before use |
| Same: isotropic remeshing and feature-aware decimation | P. Alliez, E. Colin de Verdière, O. Devillers, M. Isenburg, *Isotropic surface remeshing*, SMI 2003; S. Valette, J.-M. Chassery, *Approximated centroidal Voronoi diagrams for uniform polygonal mesh coarsening*, Eurographics 2004; D. Salinas, F. Lafarge, P. Alliez, *Structure-aware mesh decimation*, CGF 2015 | papers only, verify before use |
| Same: implementations whose approach may be read | meshoptimizer (A. Kapoulkine; MIT, checked by search 2026-11-09); geogram (B. Lévy; BSD-3-Clause, checked by search); Fast-Quadric-Mesh-Simplification (MIT), OpenMesh and Instant Meshes (BSD-3): licences from memory, check the file before reading. Not to be read or ported: CGAL, TetWild, libigl (MPL-2.0), Blender, MeshLab; pmp-library not checked | approach only |

## Deterministic parallel output

`DSubmesh3Set`, `MeshSpatialSort` and `MarchingCubes` use indexed worker-local results followed by ordered
publication. Existing building block: `gParallel.ForRange` (including its serial test hook). Reference for preserving
source order independently of scheduling: Microsoft Learn,
[Order Preservation in PLINQ](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/order-preservation-in-plinq),
[thread-local loop state](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/how-to-write-a-parallel-for-loop-with-thread-local-variables)
and [parallelism pitfalls](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/potential-pitfalls-in-data-and-task-parallelism)
(shared writes, ordering and nested parallelism).
This is an independently written ordering fix, not a code port; `MarchingCubes` retains its existing polygonization
and uses bounded slab batches rather than buffering the entire volume.

## Cross-field research

The [research report](research/CROSS_FIELD_GEOMETRY.md) records opened sources, reading depth and license checks
(papers and documentation only; no external implementation code was read). New sources it adds to the open items:

| Topic | Source | Status |
|---|---|---|
| Separation and rounding with a displacement budget | V. Milenkovic, E. Sacks, *Geometric Rounding and Feature Separation in Meshes*, Computer-Aided Design 108, 2019, [preprint](https://arxiv.org/abs/1805.03252); LAMMPS developers, [Neighbor lists](https://docs.lammps.org/Developer_par_neigh.html) (Verlet skins) | paper / documentation only |
| Incidences and pinches missed by snap rounding in practice | J. Hershberger, *Snap Rounding: A Cautionary Tale*, SoCG 2025, [paper](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.SoCG.2025.57) | paper only; background for the Q17 pinch fix |
| Certified convex free regions | R. Deits, R. Tedrake, *Computing Large Convex Regions of Obstacle-Free Space Through Semidefinite Programming*, WAFR 2014, [paper](https://groups.csail.mit.edu/robotics-center/public_papers/Deits14.pdf) | paper only |

## Forks of geometry3Sharp

| Fork | Taken over |
|---|---|
| [Digital-Production-Aachen/DAPComputationalGeometry](https://github.com/Digital-Production-Aachen/DAPComputationalGeometry) (Boost-1.0), delta `8f185f1`..`158507b` | Nothing: audited 2026-11-01, no bug fix applies to this code ([audit result](design/DAP_BUGFIX_AUDIT.md#audit-result-2026-11-01)). A later port of one of its features is listed here with the DAP commit and file |

## SMT verification pilot (test/tool only)

`tools/Z3Pilot` uses `z3-solver` 4.15.4.0 (Z3 4.15.4, MIT), only as a pinned
Python verification-tool dependency. Its native library is not distributed with
geometry3Sharp and no library/NuGet dependency is added. Modeling uses the
[SMT-LIB FloatingPoint specification](https://smt-lib.org/theories-FloatingPoint.shtml)
and [Z3's public Python API](https://z3prover.github.io/api/html/namespacez3py.html).
The [pilot README](../tools/Z3Pilot/README.md) states proof scope, mathematical
bridges, native replay and the UNKNOWN limitation of the monolithic attempt.
Pilot 2 (`tools/Z3Pilot/filter_rules.py`, the error-bound rules of `tools/PredicateCompiler`) uses the standard
model of floating-point arithmetic and its variant `fl(s) = s / (1 + d)` from N. J. Higham, *Accuracy and Stability
of Numerical Algorithms*, 2nd ed., SIAM 2002, section 2.2, and the form of the filter bounds (`|e~ - e| <= c m~`) of
J. R. Shewchuk 1997 (see above); results only, no code.

- Alec Jacobson, *A Sharp Conservative Offset for Marching Cubes*,
  [arXiv:2609.13430v1](https://arxiv.org/html/2609.13430v1), 2026, sections 1, 3,
  3.1. Paper-only mathematical source for the `LinearEdges` adapter and
  `tools/Z3Pilot/conservative_mc.py`; no external MC code ported.

## FPTaylor setup (test/tool only)

[tools/FPTaylorPilot](../tools/FPTaylorPilot/README.md) installs FPTaylor b5a77cae (MIT) from its
[official repository](https://github.com/soarlab/FPTaylor) for local analysis. It uses OCaml 4.14.2 and
Num 1.5, each LGPL-2.1 with the OCaml linking exception, as local build/runtime dependencies; these are not
ported or distributed with geometry3Sharp. Background: Solovyev et al., [*Rigorous Estimation of
Floating-Point Round-off Errors with Symbolic Taylor Expansions*](https://soarlab.org/2015/04/fm2015-sjrg/),
FM 2015. The smoke example does not certify any geometry predicate.

## Coverage-guided fuzzing of the readers and the mesh operations (test/tool only)

[ReaderFuzzPilot](../tools/ReaderFuzzPilot/README.md) and [MeshFuzzPilot](../tools/MeshFuzzPilot/README.md) (the
mesh operations) use [SharpFuzz 2.2.0](https://github.com/Metalnem/sharpfuzz)
(MIT) and [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet) (MIT; libFuzzer Apache-2.0 with
LLVM exceptions) as optional test tools. Integration follows their public documentation; no external code is
ported into geometry3Sharp and no dependency is added to the library package.

## Batch ray and point queries (design note; the library PR follows)

Design references of [BATCH_QUERIES.md](design/BATCH_QUERIES.md); papers only, no code read or ported (Embree, Apache-2.0,
not read). The planned `MeshBatchQueries` runs the existing single queries in Morton-sorted order, in parallel.

| Topic | Source |
|---|---|
| Coherent ray packets | I. Wald, P. Slusallek, C. Benthin, M. Wagner, *Interactive Rendering with Coherent Ray Tracing*, Computer Graphics Forum 20(3), 2001, [doi:10.1111/1467-8659.00508](https://doi.org/10.1111/1467-8659.00508) |
| Ray groups (beams) | A. Reshetov, A. Soupikov, J. Hurley, *Multi-level ray tracing algorithm*, ACM TOG 24(3), 2005, [doi:10.1145/1073204.1073329](https://doi.org/10.1145/1073204.1073329); the method is patented by Intel (US 7,755,628, expired), and is not implemented |
| Wide BVH for incoherent rays | H. Dammertz, J. Hanika, A. Keller, *Shallow Bounding Volume Hierarchies for Fast SIMD Ray Tracing of Incoherent Rays*, Computer Graphics Forum 27(4), 2008, [doi:10.1111/j.1467-8659.2008.01261.x](https://doi.org/10.1111/j.1467-8659.2008.01261.x) |
| Ray sorting | K. Garanzha, C. Loop, *Fast Ray Sorting and Breadth-First Packet Traversal for GPU Ray Tracing*, Computer Graphics Forum 29(2), 2010, [doi:10.1111/j.1467-8659.2009.01598.x](https://doi.org/10.1111/j.1467-8659.2009.01598.x) |
| CPU ray tracing kernels, survey | I. Wald, S. Woop, C. Benthin, G. S. Johnson, M. Ernst, *Embree*, ACM TOG 33(4), 2014, [doi:10.1145/2601097.2601199](https://doi.org/10.1145/2601097.2601199); D. Meister et al., *A Survey on Bounding Volume Hierarchies for Ray Tracing*, Computer Graphics Forum 40(2), 2021, [doi:10.1111/cgf.142662](https://doi.org/10.1111/cgf.142662) |
| Watertight ray-triangle test | S. Woop, C. Benthin, I. Wald, *Watertight Ray/Triangle Intersection*, JCGT 2(1), 2013 (the library's exact test is a different, `Orient3d`-based decision) |
| Fast winding numbers | G. Barill, N. G. Dickson, R. Schmidt, D. I. W. Levin, A. Jacobson, *Fast Winding Numbers for Soups and Clouds*, ACM TOG 37(4), 2018, [doi:10.1145/3197517.3201337](https://doi.org/10.1145/3197517.3201337) |

## Tetrahedral meshing (design only, `docs/design/TET_MESHING.md`; no code yet)

Papers only: TetGen (AGPL), fTetWild / TetWild (MPL), the CDT of Diazzi et al. (GPL / LGPL), CGAL (GPL) are not to be read or ported.

| Topic | Source |
|---|---|
| Delaunay refinement of tetrahedra (radius-edge bound, encroachment) | J. R. Shewchuk, *Tetrahedral mesh generation by Delaunay refinement*, SoCG 1998; S.-W. Cheng, T. K. Dey, J. R. Shewchuk, *Delaunay Mesh Generation*, CRC 2012 |
| Constrained Delaunay tetrahedralization, existence and recovery | J. R. Shewchuk, *A condition guaranteeing the existence of higher-dimensional constrained Delaunay triangulations*, SoCG 1998; *Updating and constructing constrained Delaunay and constrained regular triangulations by flips*, SoCG 2003; H. Si, K. Gärtner, *Meshing piecewise linear complexes by constrained Delaunay tetrahedralizations*, IMR 2005; H. Si, *TetGen*, ACM TOMS 41(2), 2015; M. Diazzi, D. Panozzo, A. Vaxman, M. Attene, *Constrained Delaunay tetrahedrization: a robust and practical approach*, ACM TOG 2023 (abstract read) |
| Small input angles | S.-W. Cheng, S.-H. Poon, *Three-dimensional Delaunay mesh generation*, DCG 2006; S. Pav, N. Walkington, *Robust three dimensional Delaunay refinement*, IMR 2004 |
| Restricted Delaunay for smooth surfaces | J.-D. Boissonnat, S. Oudot 2005; S. Oudot, L. Rineau, M. Yvinec, *Meshing volumes bounded by smooth surfaces*, IMR 2005 |
| Sliver removal | S.-W. Cheng, T. K. Dey, H. Edelsbrunner, M. A. Facello, S.-H. Teng, *Sliver exudation*, J. ACM 2000; J. Tournois, R. Srinivasan, P. Alliez, *Perturbing slivers in 3D Delaunay meshes*, IMR 2009; J. Tournois, C. Wormser, P. Alliez, M. Desbrun, *Interleaving Delaunay refinement and optimization for practical isotropic tetrahedron mesh generation*, ACM TOG 28(3), 2009; B. M. Klingner, J. R. Shewchuk, *Aggressive tetrahedral mesh improvement*, IMR 2007; J. R. Shewchuk, *Two discrete optimization algorithms for the topological improvement of tetrahedral meshes* |
| Envelope and lattice approaches | Y. Hu et al., *Tetrahedral meshing in the wild*, ACM TOG 37(4), 2018; Y. Hu, T. Schneider, B. Wang, D. Zorin, D. Panozzo, *Fast tetrahedral meshing in the wild*, ACM TOG 39(4), 2020 (arXiv 1908.03581, abstract read); F. Labelle, J. R. Shewchuk, *Isosurface stuffing: fast tetrahedral meshes with good dihedral angles*, ACM TOG 26(3), 2007 |
## Height grid readers (DGM)

`HeightGridReader` / `HeightGridWriter` / `HeightGrid` (`io/`). Format descriptions only, no code read or ported:

| Topic | Source |
|---|---|
| ESRI ASCII Grid (.asc) | ESRI, *ASCII to Raster* tool documentation (header ncols / nrows / xllcorner | xllcenter / yllcorner | yllcenter / cellsize / NODATA_value; north row first); the GDAL AAIGrid driver page describes the same format, including dx / dy |
| XYZ ASCII DGM (Geobasis NRW `dgm1_xyz`) | the open-data product description of the Land NRW DGM1 (one "x y z" point per line on a regular 1 m grid); the reader accepts any regular square lattice |
| Grid TIN with a fixed diagonal | Garland, Heckbert, *Fast Polygonal Approximation of Terrains and Height Fields*, CMU-CS-95-181, 1995 (regular grid triangulation as the full-resolution reference); the interpolation on the TIN is the library's own definition (`HeightGrid.Height`) |

## Sharp-feature probe (`benchmarks/SharpFeatureProbe`)

| Method | Source |
|---|---|
| QEF vertex from Hermite data (planes of the input around a vertex, singular values cut off) | T. Ju, F. Losasso, S. Schaefer, J. Warren, *Dual Contouring of Hermite Data*, SIGGRAPH 2002; S. Schaefer, T. Ju, J. Warren, *Manifold Dual Contouring*, IEEE TVCG 2007 |
| Feature sensitive extraction (read for the prior-art line only) | L. Kobbelt, M. Botsch, U. Schwanecke, H.-P. Seidel, *Feature Sensitive Surface Extraction from Volume Data*, SIGGRAPH 2001 |

## DGM to TIN with breaklines (`TerrainGridTin`)

`mesh_ops/TerrainGridTin.cs`, design in [TERRAIN_DGM_TO_TIN.md](design/TERRAIN_DGM_TO_TIN.md). No code read or ported. The method is the library's own: error-driven rounds around the exact constrained
Delaunay mesher (`QualityMesh2`), with an exact error scan that follows from the piecewise linearity of both surfaces (the difference is linear on the cells of the overlay of two triangulations, so its maximum is at overlay
vertices; elementary). Context only: greedy insertion for terrain TINs (Garland and Heckbert 1995, see above), constrained Delaunay triangulation with breaklines (Chew 1989), and
the usual GIS distinction between constrained and conforming TINs with breaklines.
