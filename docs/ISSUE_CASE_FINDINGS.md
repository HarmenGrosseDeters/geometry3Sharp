# Findings from the issue-tracker cases (docs/QA.md 2.11)

Each finding has an id, the test that pins it, the issue whose failure mode led to it, the input and the wrong result,
the cause as far as it was analysed, and fix ideas. Open findings are listed in the strict `KnownFailures` dictionary of
their test class: the test asserts the specific wrong outcome while listed, and fails once the case passes, so a fix
removes the entry.

## Geodesics and intrinsic triangulations

Tests: `tests/geometry3Sharp.Tests/GeodesicsIssueCasesTests.cs`.

### G1 (fixed): repeated points in `MeshGeodesicPath.Path`

- Tests: `PathPolyline_NoRepeatedPoints`, `PathPolyline_NoRepeatedPoints_InvariantUnderSimilarities`,
  `ReversedEdgePath_SameGeodesicReversed`.
- Issue: https://github.com/nmwsharp/geometry-central/issues/258 (reversed input path); found while comparing a
  path with the path of the reversed input.
- Input -> wrong result: a staircase edge path over a bumpy 12 x 12 grid, and paths from a corner of a flat 8 x 8
  grid along its diagonal. `Path` repeated the end vertex of a traced edge (and, on the closed loop, joints in the
  middle): two consecutive points about 1e-10 of the edge length apart, a zero-length segment that gives NaN tangents
  downstream. The geodesic itself (length, vertices) was right, the same in both directions.
- Cause: `IntrinsicTriangulation.TraceEdge` walks straight over the input triangles; when the edge ends exactly at a
  mesh vertex, or runs through one, the walk records the edge crossings at that vertex (up to rounding) before it
  stops, and `build_output` then appends the exact vertex position as well.
- Fix: `MeshGeodesicPath.build_output` (mesh_ops/MeshGeodesicPath.cs, around line 554) drops a point within 1e-9 of
  the edge length of the previous one; a joint keeps its exact position. Relative to the edge length, so 2^k scales
  give the same points bit for bit. With the fix reverted the three tests above fail (10 cases).

### G2 (open): tangent frames at coincident vertices

- Test: `TinyAreaTriangles_VectorTransportFinite` (case `_CoincidentVertices`).
- Issues: https://github.com/nmwsharp/potpourri3d/issues/29 and https://github.com/nmwsharp/geometry-central/issues/167.
- Input -> wrong result: a flat 10 x 10 grid whose column 5 is moved onto column 4 (coincident vertices, zero-area
  triangles), the source at a corner. `VectorHeatMethod.TransportVector` and `ComputeLogMap` are finite everywhere,
  but `Triangulation.ToWorld` of the transported vector is off by 45 to 135 degrees at every vertex of column 4 (and by
  22.5 degrees at one of column 5), where on a flat grid it must be the source vector. Slivers of width 1e-6, 1e-10,
  1e-14 (area down to 1e-16) are exact to 2 degrees.
- Cause: `IntrinsicTriangulation.GetTangentBasis` (mesh_ops/IntrinsicTriangulation.cs:657) takes e1 from the vertex'
  first input edge; at a vertex whose first edge has zero length it falls back to an arbitrary axis (line 677), and
  `tangent_wedges` (line 710) gives the zero-length edge the world angle `Atan2(0, 0) = 0`. The signposts are measured
  in the mollified intrinsic geometry, where that edge has a direction, so the mapping between signpost and world
  angles is wrong at such vertices. The intrinsic results (distances, transport in signpost coordinates) are not
  affected; only the conversion to and from 3D.
- Fix ideas: (a) in `GetTangentBasis` take e1 from the first input edge with a non-zero projected length, and in
  `tangent_wedges` skip zero-length edges (their world angle interpolated from the neighbouring corners by the
  signpost angles), keeping both sequences increasing. Small code, but it changes `ToWorld`/`FromWorld` for every
  vertex with a coincident neighbour and needs tests of the closing corner at boundary vertices; low risk otherwise.
  (b) Document that frames at coincident vertices are undefined and return NaN there (cheap, but a silent value is
  worse than the current finite but wrong one only if callers check).

### G3 (open): `TraceFailed` for short edges far from the origin

- Test: `PathPolyline_NoRepeatedPoints_InvariantUnderSimilarities` (case `_rotated_scale_1e-3_utm`).
- Input -> wrong result: the flat 8 x 8 grid of size 1 rotated, scaled by 1e-3 and moved to UTM coordinates
  (632117, 5712043, 114): edges of 1.25e-4 at coordinates of 6e6. `Compute(corner, opposite corner)` returns
  `TraceFailed` (Length is right) where the same grid at the origin, or only at UTM, or only scaled, gives `Success`.
- Cause: `build_output` (mesh_ops/MeshGeodesicPath.cs:544) accepts a traced edge if its walk ends within
  `TraceTolerance * T.Length(h)` = 1e-6 of the edge length (1.25e-10 here) of its head vertex; the coordinates
  themselves are only resolved to about 1e-9 there (one ulp of 6e6), so any walk misses by more than the tolerance.
- Fix idea: a tolerance of max(1e-6 of the edge length, a few ulps of the largest coordinate of the edge's
  triangles); `TraceTolerance` is internal and tests set it below 0 to force a failure, which must stay possible.
  Small, low risk; not done in this pass because it changes when the status is reported.

### Notes (no finding)

- The heat method has its documented discretization error on coarse meshes (geometry-central #111, potpourri3d
  #30): the 2-triangle square of size 100 gives 98.4 for an edge (not 70.7), the kite 1.75 instead of 2. Refined
  (each triangle split k x k) the errors fall to 3.5 % (edge from a boundary corner, slow: about one edge length) and
  0.5 % (kite) at k = 64; `MeshGeodesicPath` gives 100 and 2 exactly.
- A path from the Dijkstra start is a geodesic, not always the shortest one: on an open box a source on the rim gets a
  2.06 geodesic down one side where the shortest (over two sides) is 1.80; the heat method gives 1.78. As documented.
- A source 1e-9 (barycentric) from an edge makes slivers that are mollified; the exact paths then differ from |p - s|
  by up to 4e-5 (about sqrt(2 delta h) for the mollification delta 1e-7), as the mollification implies.

## Parameterization

Tests: `tests/geometry3Sharp.Tests/ParameterizationIssueCasesTests.cs`.

### P1 (open): LSCM on a low-poly cylinder collapses a boundary and reports Success

- Test: `LowPolyCylinderWithoutSeam_StatusOrBoundedDistortion` (case `_LscmAndArap`).
- Issue: https://github.com/GeometryCollective/boundary-first-flattening/issues/97.
- Input -> wrong result: an open cylinder of 8 sides and 3 rings (radius 1, height 1.8; an annulus). LSCM returns
  `Success`, but the 8 vertices of the far ring all get v = 0 (the ring collapses onto a segment): 2 flipped UV
  triangles, mean conformal distortion 2e13, maximum 7e14. ARAP, started from that map, also ends with 2 flipped
  triangles. SLIM, Tutte and BFF return `InvalidTopology`; the atlas cuts the cylinder and is fine.
- Cause (analysis): with automatic pins, `farBoundaryPair` (parameterization/LSCMParameterization.cs:127) pins two
  vertices of one boundary loop (both loops have 8 vertices: the first is taken). The free-boundary energy
  E_D - A(uv) (line 104: A is the signed area over all boundary loops, the inner loop counted negatively) is lowered by
  shrinking the hole; on a coarse cylinder the minimizer with free inner boundary is the collapsed ring. On the flat
  annulus (`LSCM_Annulus_TwoBoundaries`) the exact conformal embedding has E_C = 0 and wins.
- Fix ideas: (a) report failure (or a non-success status such as `SolverFailed`) when a success has flipped or
  zero-area UV triangles on non-degenerate surface triangles, as SLIM's guarantees are checked; cheap, but changes
  the status of results that are only locally flipped today (the docs promise nothing), so it needs a decision. (b)
  For several boundary loops, close the inner loops with virtual fan vertices for the area term (the hole's area then
  enters positively), or use the spectral conformal parameterization (Mullen, Tong, Alliez, Desbrun 2008), which has
  no pins. Medium cost, changes LSCM results on annuli.

### P2 (open): BFF Disk on two needles sharing their long edge

- Test: `LongThinTriangleInPlaneX0_ValidChart` (case `_BffDiskTwoTriangles`).
- Issue: https://github.com/jpcy/xatlas/issues/130 (a long thin triangle in the plane x = 0).
- Input -> wrong result: two triangles (0,0,0), (0,1000,0), (0,500,±0.01) in the plane x = 0 sharing the long edge (a
  quad of aspect 5e4, no interior vertex). `BoundaryFirstFlattening` with `BoundaryMode = Disk` returns `Success` with
  one flipped triangle; the four UVs lie about 1.5e6 from the origin in a sliver of 0.8 x 4155, not on a circle. The
  default mode (MinimalAreaDistortion), LSCM, ARAP, SLIM, Tutte and the atlas are fine, as is the single triangle.
- Cause (not verified in the code): the long shared edge lies opposite two angles of almost 180 degrees, so its cotan
  weight is about -2.5e4 (the short edges +5e4); with the Disk mode's Neumann-to-Dirichlet solve and the conformal
  extension on this indefinite, badly scaled Laplacian the result is garbage; the success is not checked.
- Fix ideas: (a) build BFF's Laplacian on the intrinsic Delaunay triangulation (as the geodesics do: `IntrinsicTriangulation`
  flips the long edge to the short diagonal, all weights non-negative); medium cost, changes BFF results on obtuse
  meshes, needs a benchmark A/B of the distortion. (b) Check the result (flips, finiteness, boundary on the target
  circle) and report `SolverFailed`; cheap.

### P3 (open): LSCM with automatic pins is not symmetric

- Test: `TwoIdenticalRectangularHoles_SymmetricResult` (case `_LscmAutomaticPins`).
- Issue: https://github.com/GeometryCollective/boundary-first-flattening/issues/89.
- Input -> wrong result: a gently curved 18 x 12 plate, mirror-symmetric with its triangulation, with two identical
  2 x 3 cell holes at mirrored positions. With automatic pins the LSCM map is asymmetric: distances between UVs of
  mirrored vertex pairs differ by 9.4e-5 of the UV size (0.00047 of 4.9). With pins at a mirrored pair the map is
  symmetric to 1e-9; ARAP (from LSCM), SLIM, Tutte, BFF (where they accept the patch) are symmetric to 1e-6.
- Cause: `farBoundaryPair` (parameterization/LSCMParameterization.cs:127) picks two far vertices by farthest-point
  passes from `loop[0]`, which is not a mirrored pair; on a non-developable surface the minimizer of E_C with two pins
  depends on the pins beyond a similarity (a known property of LSCM).
- Fix ideas: pin-free spectral conformal parameterization (Mullen et al. 2008; an eigenvector solve, medium cost), or
  natural boundary conditions with a symmetric choice of pins (cheap but only helps symmetric inputs). Expected
  effect small (1e-4); low priority.

### Notes (no finding)

- Closed spheres get `InvalidTopology` from every single-chart method, two discs or an annulus either
  `InvalidTopology` or a valid map, and the atlas cuts all of them into disc charts (libigl #1407, #1401).
- Unused vertices, also at 1e7, do not change the UVs (to 1e-9; libigl #343); degenerate patches either fail with a
  status or have finite UVs (libigl #2439); the orientation follows the surface for all methods, also facing -z, in
  the plane x = 0 and with given LSCM pins (libigl #91); SLIM and Tutte stay flip-free on a three-peak terrain and on
  saddles, Tutte also overlap-free (libigl #680, BFF #81, #61); every atlas chart of icospheres and thin tall boxes is
  a disc with UV area and the same texel density within a factor 2 (xatlas #146, #145); packing with rotation keeps
  the winding (xatlas #121).
