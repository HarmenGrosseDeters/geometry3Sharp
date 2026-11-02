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
  give the same points bit for bit. `IntrinsicTriangulation.TraceEdge` (public, unchanged) documents that it can return
  such repeats. With the fix reverted 8 cases fail (`PathPolyline_NoRepeatedPoints` and 7 cases of the invariance
  theory; the G3 case fails either way; `ReversedEdgePath_SameGeodesicReversed` compares with a tolerance relative to
  the length, so it checks the reversal only). A path that passes the diagonal vertices of a grid at 1e-7 has real
  segments of 1e-10 at a scale of 2^-10: an absolute 1e-9 threshold (mutant) drops them and fails that case.

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

### P2 (open): BFF Disk with the conformal extension leaves the circle on meshes without interior vertices

- Test: `LongThinTriangleInPlaneX0_ValidChart` (case `_BffDiskTwoTriangles`).
- Issue: https://github.com/jpcy/xatlas/issues/130 (a long thin triangle in the plane x = 0).
- Input -> wrong result: two triangles (0,0,0), (0,1000,0), (0,500,±0.01) in the plane x = 0 sharing the long edge (a
  quad of aspect 5e4, no interior vertex). `BoundaryFirstFlattening` with `BoundaryMode = Disk` returns `Success` with
  one flipped triangle; the four UVs lie about 1.5e6 from the origin in a sliver of 0.8 x 4155, not on a circle. The
  default mode (MinimalAreaDistortion), LSCM, ARAP, SLIM, Tutte and the atlas are fine, as is the single triangle.
- Cause (probed on the same quad with z = ±0.01, 1, 50, 200, 400, both diagonals): two independent parts.
  (a) With the default `Extension = Conformal` the result is not cyclic at any z, also on the Delaunay (short)
  diagonal: the fourth UV is 4 % (z = 50) to 55 % (z = 200) of the radius off the circle through the other three, at
  z = 1 the UVs are 1.2e5 away. `Extension = Harmonic` gives the exact inscribed quad (off by 1e-16) at z = 1 to 400,
  also with the negative cotan weight. In the conformal branch (parameterization/BoundaryFirstFlattening.cs, the
  `else` after `if (Extension == ExtensionModes.Harmonic)`, around line 216) v comes from the Neumann solve of the
  harmonic conjugate on all vertices, boundary ones included, and only its mean is shifted to gamma.y: the boundary
  keeps v of the conjugate instead of the curve's gamma.y, which without interior vertices alone moves the corners
  off the circle. (b) At z = 0.01 the boundary curve itself is degenerate: the boundary scale factors are ±18.8
  (exp(18.8) = 1.5e8), so even the harmonic extension puts a UV at 6e6 (circle radius 3e6). The long shared edge lies
  opposite two angles of almost 180 degrees: its cotan weight w = 1/2 (cot a + cot b) is about -2.5e4, the short
  edges +2.5e4; the Neumann-to-Dirichlet solve on this indefinite, badly scaled Laplacian gives the extreme scale
  factors. On the other diagonal they are ±392 at z = 1 and overflow at z = 0.01 (finding P4).
- Fix ideas: (a) in the conformal extension, keep v = gamma.y on the boundary (or report the boundary error), so Disk
  is exactly a circle as documented for Harmonic; small code change, but it changes every Disk/conformal result near
  the boundary and needs a distortion A/B (`benchmarks/`). (b) Build BFF's Laplacian on the intrinsic Delaunay
  triangulation (`IntrinsicTriangulation`, non-negative weights); medium cost. (c) Check the result (flips, finiteness,
  boundary on the target circle) and report `SolverFailed`; cheap.

### P4 (fixed): BFF Disk threw on the needle quad with the short diagonal

- Test: `LongThinTriangleInPlaneX0_ValidChart` (the short-diagonal quad), `BffDisk_InvariantUnderSimilarities`.
- Issue: https://github.com/jpcy/xatlas/issues/130 (found by the independent review).
- Input -> wrong result: the quad of P2 triangulated (0,3,2), (3,1,2) (the Delaunay diagonal), z = ±0.01:
  `Compute()` threw an `ArithmeticException` (NaN in `Math.Sign`) in `inscribedAngles` (BoundaryFirstFlattening.cs,
  the bisection) instead of returning a status.
- Cause: the boundary scale factors of the Neumann-to-Dirichlet solve are so large (P2 (b)) that the target lengths
  exp(u) * l overflow, and the inscribed-circle bisection gets NaN.
- Fix: the Disk iteration checks the target lengths before each `inscribedAngles`; not positive and finite gives
  `SolverFailed` ("BFF Disk: boundary target lengths are not positive and finite"). With the guard reverted 9 cases
  fail (the test above and the 8 cases of the invariance theory, whose quad must give `SolverFailed` under every
  similarity; on a curved cap the Disk UVs are the same bit for bit under 2^k scales).

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
- The other methods take the needle quads, also with the short diagonal (LSCM, ARAP, SLIM, Tutte, BFF default, atlas).
- Unused vertices, also at 1e7, do not change the UVs (to 1e-9; libigl #343); degenerate patches either fail with a
  status or have finite UVs (libigl #2439); the orientation follows the surface for all methods, also facing -z, in
  the plane x = 0 and with given LSCM pins (libigl #91); SLIM and Tutte stay flip-free on a three-peak terrain and on
  saddles, Tutte also overlap-free (libigl #680, BFF #81, #61); every atlas chart of icospheres and thin tall boxes is
  a disc with UV area and the same texel density within a factor 2 (xatlas #146, #145); packing with rotation keeps
  the winding (xatlas #121).
