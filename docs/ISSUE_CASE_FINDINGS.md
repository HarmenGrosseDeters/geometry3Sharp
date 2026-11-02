# Findings from the issue-tracker cases (docs/QA.md 2.11)

Each finding has an id, the test that pins it, the issue whose failure mode led to it, the input and the wrong result,
the cause as far as it was analysed, and fix ideas. Open findings are listed in the strict `KnownFailures` dictionary of
their test class: the test asserts the specific wrong outcome while listed, and fails once the case passes, so a fix
removes the entry.

## Mesh repair

### R1: crossing components taken for nested ones (fixed)

- **Status: fixed (fix idea 1).** In `FindInvertedComponents`, a component that would be flagged is tested for
  meeting surfaces with the components most of its samples lie in. The test is exact
  (`ExactTriangleIntersection.IntersectsBeyondShared`, behind the filter `TriangleTriangleFilter`). If the surfaces
  meet, the component has no nesting and keeps its orientation. The volume check's reference uses the same rule, so it
  moves along: there is no second inside / outside rule. The test is no longer pinned.
  Direct tests: `MeshPostRepairCleanupTests.CubeCrossingASphere_IsNotACavity` (fails on the old code),
  `CrossingPairInAShell_KeepsItsOrientation` (fails on the old code), `CrossingComponents_KeepTheirOrientation`,
  `NestedComponentsNotMeeting_AreStillNested`,
  `CavitySharingAVertexWithItsShell_IsNested`, `CavityTouchingItsShell_KeepsItsOrientation`.
- The surfaces are tested only for a component that would be flagged, against the components most of its samples
  lie in, so a correctly oriented mesh pays nothing for it.
- Limits of the fix:
  - A crossing is seen only with a component that contains most of the samples (and whose box contains the
    component's). Otherwise the component is judged without it, as before. Example: an inside-out crossing box
    whose box does not lie in the other's is a top-level shell and is flagged; flipping it gives the union.
  - Crossing components have no nesting at all, also inside a third component that encloses both (a cube poking out
    of a spherical cavity keeps its orientation, either way). Dropping only the crossing pair from the count instead
    would flag every cavity that touches its shell's wall (`InvertedComponents_TouchingBoxesAndCavity_...`,
    `InvertedComponents_ThinCavityTouchingWall_IsNotInverted`).
  - A touch counts as a meeting: a cavity on its shell's wall (a zero wall), or touching it at a point through an
    unwelded vertex. Only a vertex shared by id (a bowtie) does not count. Such a cavity keeps its orientation. The
    correct, inward one is right either way. A wrongly oriented one is flagged only where rounding (a rotation)
    turns the contact into a gap; on base it was flagged.
- Test: `MeshRepairIssueCasesTests.ThinFold_NearlyParallelOverlap_Resolved` (3 and 10 degrees).
- Issue: https://github.com/MeshInspector/MeshLib/issues/5567 (thin folds between faces that point almost the same way).
- Input: two outward boxes [-2,2] x [-2,2] x [-0.2,0.2], one of them rotated about y by 3 (or 10) degrees, in one mesh.
- Wrong result: `MeshRobustRepair` returns a clean mesh of volume 0.839 (10 degrees: 2.77) instead of the union's
  7.239: the difference A \ B. `RepairVolumeIoU` reports 1, so the volume check does not catch it. At 30 degrees the
  result is right (11.52).
- Cause: `MeshAutoRepair` with `PostRepairCleanup` runs `MeshPostRepairCleanup.FindInvertedComponents`
  (mesh_ops/MeshPostRepairCleanup.cs, about line 282). For a closed component i whose box lies inside the box of
  another closed component j, it counts j as enclosing i when the majority of a few interior samples of i lie inside j
  (`2 * votes > samples.Count`). Its comment states the assumption: "the interiors of two components are disjoint or
  nested". Crossing components break it: at 3 degrees the thin box's box lies inside the rotated one's box, most of
  its samples are inside, so it is taken for a cavity and flipped. The exact resolve then keeps the solid with a
  positive winding number, A \ B. The volume check compares with `MeshRepairMetrics.SolidReference(input)`, which
  orients the input's components by the same nesting rule, so both sides agree on the wrong solid.
- Fix ideas:
  1. Count j as enclosing i only if their surfaces do not intersect (`DMeshAABBTree3.TestIntersection` between the
     two component trees, or the exact `MeshRepairMetrics` self-intersection pairs restricted to the pair). Crossing
     components keep the sign of their own volume. Cost: small code change; one tree-tree test per candidate pair
     (only pairs with nested boxes are tested today). Risk: changes repair results on real meshes with crossing parts
     (Thingi10K assemblies), so it needs the `benchmarks/MeshRepairBenchmark` A/B on the 400 set and the holdout.
  2. Require all samples (not the majority) inside j. Cheaper, but the majority vote exists for samples near thin
     features (see the comment), so it would trade one failure for another.
  3. For the volume check, build the input's reference solid from the winding number of the input as given
     (`Unoriented`) when its components intersect: it would then reject the wrong result and fall back to the
     exact resolve of the input (which gives the union). Cost: small; risk: more fallbacks.

### R2: a large hole fill is rejected by `MeshRobustRepair`'s volume check (limitation, not pinned)

- Tests: `QuadHoleWhoseDiagonalIsAnExistingEdge`, `HoleNextToANearZeroAreaTriangle_PatchStaysLocal`,
  `SmallAndLargeModel_SmoothedFill_SameRefinement` use `MeshAutoRepair` for this reason.
- Input: an open mesh with a large hole, eg a sphere without the cap z > 0.8, or a tetrahedron without two faces.
- Result: `MeshAutoRepair` closes it cleanly, but `MeshRobustRepair` compares that with the winding number solid of
  the open input, which a large hole blurs: IoU 0.974 for the cap (below `MinVolumeIoU` 0.99), 0.397 for the
  tetrahedron. It then solidifies the input instead: remeshed, and for the tetrahedron a blob of volume 0.80 instead of
  the closed tetrahedron's 2.
- This is the documented design (the winding number solid is the reference), so it is recorded, not pinned.
- Ideas: skip or relax the volume check where the repair only added triangles inside the input's boundary loops (the
  added triangles are the fills: their solid differs from the open input's by construction); or compare only away
  from the holes. Cost: medium (the check would need the fill triangles from `MeshAutoRepair`); risk: a bad fill
  would no longer be caught by the IoU.

### Checked: Thingi10K 48950 end to end

`MeshRobustRepair.Apply()` on Thingi10K 48950 (fetched at run time from the Hugging Face mirror, not committed), on
#317 (alpha wrap B3, `RefineNonManifold`) merged with master (7b quality pass): wrap fallback, simplified and quality
pass kept, result clean, 42336 triangles, smallest angle 2.08 degrees, no triangle below 0.5 degrees (41 s; scratch
harness, angles from atan2 of cross and dot products).

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

### G2 (fixed): tangent frames at coincident vertices

- Tests: `TinyAreaTriangles_VectorTransportFinite` (case `_CoincidentVertices`),
  `CoincidentVertices_VectorTransport_UnderSimilarities`, `IntrinsicGeodesicsTests.ToWorld_WithoutZeroLengthEdges_BitIdenticalToReference`.
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
- Fix (idea (a), mesh_ops/IntrinsicTriangulation.cs, `tangent_frame` and `wedge_angles`): an input edge without a
  direction in the tangent plane (projected length below 1e-300) has no world angle of its own; it gets the world
  angle interpolated linearly in the signpost angle between the neighbouring edges that have one, cyclically over the
  closing corner (also at boundary vertices, whose closing corner is the gap). Its two corners then map as one, as if
  the edge were not there. If the first edge is such an edge, `GetTangentBasis` takes a provisional basis from the
  first edge with a direction and turns e1 to the first edge's interpolated direction, so its signpost angle 0 stays
  e1. A vertex whose neighbours all coincide keeps the arbitrary axis. Vertices without zero-length edges keep their
  angles bit for bit (`ToWorld_WithoutZeroLengthEdges_BitIdenticalToReference`: a reference copy of the old mapping
  on a bumpy grid, a sphere, a folded fan and a squeezed grid). The un-pinned case and the new theory over
  `Similarity.All()` (within 2 degrees everywhere, the transport bit for bit at 2^k) fail on the base; mutants
  without the turn of e1 or without the interpolation fail 9 of the 11 G2 and ToWorld tests. Intrinsic, VectorHeat,
  Geodesic and issue-case filters 328/328. `IntrinsicTriangulation` is fork-only (no UPSTREAM_CHANGES row). Owner
  concept: none needed (a local degenerate-input rule); the general degenerate-input handling of the intrinsic code is
  the mollification it already uses.

### G3 (fixed): `TraceFailed` for short edges far from the origin

- Test: `PathPolyline_NoRepeatedPoints_InvariantUnderSimilarities` (case `_rotated_scale_1e-3_utm`); the forced
  failure `MeshGeodesicPathTests.FailedTrace_IsReported`.
- Input -> wrong result: the flat 8 x 8 grid of size 1 rotated, scaled by 1e-3 and moved to UTM coordinates
  (632117, 5712043, 114): edges of 1.25e-4 at coordinates of 6e6. `Compute(corner, opposite corner)` returned
  `TraceFailed` (Length was right) where the same grid at the origin, or only at UTM, or only scaled, gives `Success`.
- Cause: `build_output` (mesh_ops/MeshGeodesicPath.cs, around line 560) accepted a traced edge if its walk ended
  within `TraceTolerance * T.Length(h)` = 1e-6 of the edge length of its head vertex: 5.3e-10 for the 5.3e-4 long
  diagonal edge here. The walk ends with `to_world`, a barycentric combination of the triangle's coordinates, which
  rounds to about one ulp of the coordinates (9.3e-10 at 5.7e6); the measured miss was 9.39e-10, one ulp.
- Fix: the tolerance is max(`TraceTolerance` of the length, 16 eps M), M the largest absolute coordinate of the head
  vertex (`trace_tolerance`; the rounding of `to_world` is at most about 6.1 eps M as a distance, derived in the comment). eps M is
  scaled exactly by a 2^k scale, like the length, so such scales keep the same decision; the ulp term matters only
  for edges below about 3.6e-9 of the coordinates (16 eps / 1e-6). A `TraceTolerance` below 0 still fails every trace (tests). With
  the term removed the UTM case fails; with the `< 0` guard removed `FailedTrace_IsReported` fails (its new
  one-edge path ends exactly at its vertex). A factor of 2 instead of 16 still passes (the miss is one ulp): the
  margin is the derivation's, not a tuned value.

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

### P2 (open, decided: kept): BFF Disk with the conformal extension leaves the circle on meshes without interior vertices

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
- Fix attempt (November 2026, `claude/fix-p2`, not fixed; measured with a probe on the `ParameterizationBenchmark`
  meshes at 4, 8, 30 and 60 rings and the quads above, comparing the conformal and harmonic extensions per mode):
  - The conjugate v is discretely harmonic at every interior vertex (the Neumann right-hand side is zero there), so
    the only interior-harmonic function with v = gamma.y on the boundary is the harmonic extension itself: there is
    no "conformal extension with the exact boundary". Idea (a) as written (keep the conjugate inside, gamma.y on the
    boundary) is not harmonic next to the boundary and flips triangles: Disk on the ellipsoid cap 23 (30 rings) and
    194 (60 rings) flips, on the cut-open cylinder 78 to 974 flips, where today there are none.
  - Where the boundary target pair (u, k~) is consistent the conjugate does give gamma: MinimalAreaDistortion keeps
    the boundary within 6e-5 (hemisphere, 60 rings) to 7e-4 (cap 0.8 pi) of the UV diagonal, and to 1e-12 on the
    quads without interior vertices. In Disk mode the pair is not consistent: the five rounds of the fixed point
    (u from the angles, the angles from the inscribed polygon of the lengths of u) end with u of the previous angles.
    The conjugate's boundary is then 0.6 % (ellipsoid) to 2.1 % (cylinder, at every resolution) of the diagonal off
    the circle, and on the quads 0.09 to 176 diagonals. Iterating to convergence brings the ellipsoid and cylinder to
    4e-4 to 7e-4 (the fixed point residual from 1e-2 to 1e-13 in 20 to 50 rounds), but on the quads the iteration
    diverges (z = 50: a flip after 20 rounds; z = 1: infinite UVs after 10), so it does not fix this case.
  - The BFF reference implementation (MIT, github.com/GeometryCollective/boundary-first-flattening, `Bff.cpp`, read
    to check the construction, nothing taken over) uses the conjugate only for given scale factors and the harmonic
    extension for given angles, cones and the disk (`flattenToDisk` ends in `flatten(u, ktilde, false)`). Doing the
    same here (Disk always harmonic) fixes the quad (the two-needle quad: no flip, on the circle; conformal distortion
    3.6e12, the price of a needle quad on a circle) and is exact on the circle everywhere, but changes Disk broadly:
    mean / max conformal distortion (area weighted) on the cut-open cylinder 1.029 / 2.42 -> 5.52 / 64146 (60 rings;
    its rectangle corners must open to pi), the ellipsoid cap 1.014 -> 1.021, the hemisphere and the 0.8 pi cap
    unchanged to 1e-4. It also makes `Extension` a no-op in Disk mode.
- Decision (owner, November 2026): P2 stays pinned and documented; Disk keeps the documented "boundary close to the
  target curve". The harmonic extension for Disk (the reference behaviour) is not taken: it worsens the distortion
  broadly (the cylinder above). Making the Disk iteration consistent, so that u and the angles agree at the end, is
  parked as a later research item (it would not fix the quads without interior vertices, where it diverges).

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

## Simplification and remeshing

Tests: `tests/geometry3Sharp.Tests/ReducerIssueCasesTests.cs`, `tests/geometry3Sharp.Tests/RemesherIssueCasesTests.cs`.
Open findings are pinned in the strict `KnownFailures` lists of those classes (a listed case that passes fails the
test, and while listed the test checks the specific wrong outcome).

### S1 (open): collapse chains fold the surface in the Reducer

- Tests: `ReducerIssueCasesTests.NoisyTerrain_ReducedTo20Percent_NoFoldOver_{1,2,3}`,
  `ReducerIssueCasesTests.SmallPatch_ReducedTo10_NoDuplicateTriangles_2`.
- Issue: https://github.com/zeux/meshoptimizer/issues/346 (also seen in the setting of
  https://github.com/zeux/meshoptimizer/issues/699).
- Input -> wrong result: a 40 x 40 unit grid height field (a hill of amplitude 3 plus noise of +-0.4, all normals
  up), `ReduceToTriangleCount(640)`: 1, 4 and 2 triangles (seeds 1, 2, 3) whose normal points down or lies
  horizontal. A 4 x 3 grid with random heights in [0, 0.3] (24 triangles) reduced to 10: 1 vertical triangle on
  three consecutive boundary vertices of the y = 0 border. Two kinds:
  - interior folds: normals with z = -0.45 and -0.22 (seed 2);
  - boundary walls: a triangle on three consecutive boundary vertices standing vertical (normal (0, +-1, 0) or
    (-1, 0, 0)) after an interior vertex was collapsed onto a boundary vertex.
- Cause: `MeshRefinerBase.collapse_creates_flip_or_invalid` (mesh/MeshRefinerBase.cs:122, test at line 145,
  `sign <= edge_flip_tol`) compares each new triangle's normal with the same triangle's current normal only. Each
  step may turn a normal by up to 90 degrees (or arccos(EdgeFlipTolerance)), so a chain of collapses turns it over;
  the boundary wall passes because its new normal is tilted towards the old one. `EdgeFlipTolerance = 0.7` still
  leaves 0, 1 and 2 folds (seeds 1, 2, 3).
- Fix ideas: (a) keep a normal cone (or the area-weighted input normal) per vertex, merged on collapse, and reject a
  collapse whose new triangles' normals have a non-positive dot with the cone axis of their vertices: about 30 lines in Reducer, changes results of every reduction where it rejects,
  needs an A/B on the reducer benchmarks (quality and speed) and an UPSTREAM_CHANGES row; medium risk. (b) for the
  boundary walls only: reject a collapse that creates a triangle with all three vertices on the boundary and two
  boundary edges if its normal deviates by more than 60 degrees from the removed triangles' normals: small, but it
  only covers one kind. MaxDeviation does not help (the walls are within the deviation of the input).

### S2 (open, decided: kept): Remesher smoothing and projection flip triangles without MinTriangleQuality

- Tests: `RemesherIssueCasesTests.FlatOpenFan_BoundaryFixed_NoFlippedTriangle_0_basic` / `_0_pro`,
  `RemesherIssueCasesTests.LongThinSharpParts_NoInvertedTriangles_0`.
- Issues: https://github.com/pmp-library/pmp-library/issues/158, https://github.com/BrunoLevy/geogram/issues/357.
- Input -> wrong result: a flat open fan at z = 0 (11 vertices, 9 triangles, rim radii 0.4 to 1.6 over 270 degrees)
  with `FixAllBoundaryEdges`, remeshed to edges of 0.2 (15 passes, smoothing 0.5): 10 triangles not facing +z
  (folded), the area 1.758 (Remesher) or 1.820 (RemesherPro) instead of 1.734. A 20 x 0.5 x 0.5 box remeshed to
  0.25 with its features kept: a zero-area triangle on a crease, counted as inverted, and 3 self-intersecting pairs
  (both pinned). Measured separately, not in a test: a `CappedCylinderGenerator` cylinder r = 0.1, length 10, 16
  slices, remeshed to 0.1, had one cap triangle turned over (normal dot -1, 6 self-intersecting pairs); the test's
  cylinder with rounded rim coordinates passes.
- Cause: with `MinTriangleQuality = 0` (the default, the upstream behaviour) the smoothing passes
  (`Remesher.FullSmoothPass_Buffer`, mesh/Remesher.cs:1506) and the projection do not check moves for flips, and
  `PreventNormalFlips` (off by default) only guards collapses and flips. Documented as opt-in on
  `Remesher.MinTriangleQuality`. With `MinTriangleQuality = 0.2` all three inputs pass (`_0.2` cases).
- Fix ideas: (a) turn on the flip check of smoothing moves (the part of MinTriangleQuality that rejects a move that
  flips an incident non-flat triangle) by default, independently of the quality threshold: changes default results
  of Remesher and RemesherPro (an UPSTREAM_CHANGES row, the RemeshQualityBenchmark A/B), low to medium risk. (b) make
  g3cli-like defaults (MinTriangleQuality 0.2) the library default: a larger behaviour change.
- Decision (owner, November 2026): the Remesher defaults stay those of upstream, and S2 stays pinned. For inputs like
  these the docs recommend `PreventNormalFlips` and `MinTriangleQuality` (docs/OPERATIONS.md). Turning a flip guard on
  by default would also change other results: in a prototype (`claude/fix-s2`) it regressed the hole fill on UTM
  coordinates, the UTM gear and the boundary of an open surface. A default change would be a separate item with
  those cases.

### S3 (open, decided: kept): without feature constraints the Remesher cuts the creases of a coarse part

- Test: `RemesherIssueCasesTests.CreasedPart_BoundsKept_none`.
- Issue: https://github.com/MeshInspector/MeshLib/issues/4148 (prose only).
- Input -> wrong result: an L-shaped prism ([0,2]x[0,1] union [0,1]x[0,2], height 1, 20 triangles, volume 3)
  remeshed to edges of 0.2 with projection onto the input but no constraints: the volume is 0.95 after the first pass
  and 0.73 after 15, the bounding box moves by 1 (y from 0 to 1: one leg of the L is gone), the vertex distance to
  the input is 1.23. With `PreserveBoundaryAndFeatures` the box and volume are kept exactly (`_features`).
- Cause: the valence-driven edge flips (mesh/Remesher.cs:734-749) and collapses run across the 90 degree creases
  (the flip test `flip_inverts_normals` only runs with PreventNormalFlips or MinTriangleQuality), and the projection
  onto the input then pulls the cut surface onto the nearest faces; on an input with only two triangles per face
  every edge is a crease or a diagonal, so each pass removes more.
- Fix ideas: (a) refuse flips (and collapses) whose dihedral angle exceeds a crease angle even without constraints
  (eg 60 degrees): small code, but changes default results on every creased mesh; needs the benchmarks and an
  UPSTREAM_CHANGES row; medium risk. (b) document more prominently that remeshing without
  `PreserveBoundaryAndFeatures` is only for smooth surfaces (no code risk).
- Decision (owner, November 2026): no default change (upstream behaviour kept), S3 stays pinned; the docs recommend
  feature constraints (`PreserveBoundaryAndFeatures`) and `PreventNormalFlips` for creased inputs (idea (b)).

### Fixed: bowtie vertices in the Reducer

- Tests: `ReducerIssueCasesTests.TwoConesJoinedAtTheTip_NotMerged_{200,1}`,
  `ReducerIssueCasesTests.TwoSheetsTouchingAtACorner_NotJoined_{40,1}` (a bowtie on the boundary),
  `ReducerIssueCasesTests.TwoConesJoinedAtTheTip_UnderSimilarity`. Issue: https://github.com/libigl/libigl/issues/2043.
- Was: two closed cones (32 slices, 4 rings) sharing their apex (a bowtie vertex), reduced to 200 triangles: an edge
  from one cone to the other (the apex removed and the lower fan attached to a vertex of the upper cone); reduced to
  1: also a duplicate triangle and the lower cone at volume 0. The tetrahedron test of `DMesh3.CollapseEdge` counts
  the edges of all fans of the bowtie vertex, so it lets a duplicate pair through.
- Fix (mesh/Reducer.cs, `CollapseEdge`): a bowtie vertex is kept in place and never removed; an edge between two
  bowtie vertices is not collapsed. Meshes without bowties are unchanged. The queue priority of an edge at a bowtie
  vertex is still that of the free optimum (the forced position is only flip-checked at collapse time).

### Fixed: bowtie vertices in remeshing with PreserveBoundaryAndFeatures

- Tests: `RemesherIssueCasesTests.TwoConesTipToTip_ValidOutput_{basic,pro}`,
  `RemesherIssueCasesTests.TwoConesTipToTip_UnderSimilarity`. Issue: https://github.com/CGAL/cgal/issues/8359.
- Was: the same two cones remeshed to 0.2 with `PreserveBoundaryAndFeatures`: an edge joining the cones and the apex
  moved off the origin (collapsed away / smoothed with both fans).
- Fix (mesh/MeshConstraintUtil.cs, `PreserveBoundaryAndFeatures`): bowtie vertices are pinned. Without constraints
  the Remesher still may join the fans (the same mechanism as S3: no constraint, no guard); not tested.

## Point, ray and inside queries

Tests: `tests/geometry3Sharp.Tests/SpatialQueryIssueCasesTests.cs`. Strict known failures are listed in its
`KnownFailures` with the finding id below.

### Q1 (fixed): the nearest hit among ties depended on the scale

- Test: `VerticalRaysOnAScaledMesh_HitIndependentlyOfTheScale` (also `NearestHit_AllSimilarities_MapBackToTheBaseline`).
- Issue: https://github.com/libigl/libigl/issues/2408 (vertical rays onto a scaled mesh stopped hitting below 1e-2).
- Input -> wrong result: a 5 x 5 height field (and a cube sphere of radius 2), vertical rays on a 1/4 grid, scaled by
  2^k. The hit sets and the nearest t were the same bit for bit at every 2^k (we never lost hits as libigl did), but
  at 2^-25 and below `FindNearestHitTriangle` returned another triangle of the same t for rays onto shared edges and
  vertices (at 2^-25, 16 of the 441 field rays, eg the ray onto (1, 3) gave triangle 33 instead of 31).
- Cause: `spatial/DMeshAABBTree.cs` `find_hit_triangle` kept the first triangle found with the smallest t
  (`rayt < fNearestT`), and the traversal visits the nearer child box first; the boxes are padded by the absolute
  `box_eps = 50 * MathUtil.Epsilonf` (about 6e-6, line 2345), which dominates the box entry parameters once the
  model is smaller than that, so the order and the winner of the tie changed.
- Fix: ties go to the lowest triangle id (`rayt == fNearestT && ti < tID`), the same rule as
  `MeshQueries.FindHitTriangle_LinearSearch`. Equal-t triangles are always visited (a box's entry t is at most that of
  the triangles in it), so the result no longer depends on the traversal order. That holds in exact arithmetic:
  the prune `boxT <= fNearestT + MathUtil.ZeroTolerancef` and `box_eps` are absolute, so at ray parameters above
  about 1e10 with non-dyadic geometry a rounded box entry can exceed an equal triangle t and the tie can still depend
  on the order (as in the old code, not a regression). Row in UPSTREAM_CHANGES.md.

### Q2 (fixed): the ray-triangle test was not watertight

- Test: `RayOntoTheSharedDiagonalOrVertexOfASplitSquare_Hits` (also `ExactRayTriangleTests`).
- Issue: https://github.com/mikedh/trimesh/issues/1766 (a ray onto the shared diagonal of a split square, rescaled:
  0 hits); also https://github.com/mikedh/trimesh/issues/2317 (vertex hits gave 0-n).
- Input -> wrong result (before the fix): the unit square split along the diagonal (1,0)-(0,1) into (0,1,3) and (1,2,3), scaled by
  1e-3; a vertical ray onto the diagonal point (97/98, 1/98) * 1e-3 hits neither triangle (brute force and tree: 0 hits).
  At 1e-7 four of the 97 diagonal points miss (u = 89/98, 92/98, 95/98, 97/98). At 1, 2^±30, 1e3 and UTM all hit. A
  ray exactly through a vertex can miss every triangle of its fan: the apex of the pyramid in Q3 (4 triangles) gives
  0 hits.
- Cause: `intersection/IntrRay3Triangle3.cs` `Intersects` (line 119) computes the edge functions as
  `DdQxE2 = D . ((O - V0) x E2) >= 0`, `DdE1xQ = D . (E1 x (O - V0)) >= 0` and the third edge as
  `DdQxE2 + DdE1xQ <= DdN`. The two triangles of an edge
  evaluate that edge from different corners and operands, so their rounding is independent and a ray on (or within
  rounding of) the edge can be rejected by both. The tree is not the cause: in every case it equals brute force
  (asserted).
- Fix: the hit / no-hit decision is exact (`math/ExactRayTriangle.cs`, `RayHitClosed`, used by
  `IntrRay3Triangle3.Find` / `Intersects`, every ray query of `DMeshAABBTree3`, `MeshQueries.FindHitTriangle_LinearSearch`
  and `IntersectionUtil.Intersects`): the ray is the half line from the origin through `SecondPoint` = origin +
  2^m direction (rounded once; 2^m from the exponents, so that the scaled direction is at least twice the origin's
  largest coordinate and 2^k scaling stays exact); the line meets the closed triangle iff the signs of
  `ExactPredicates.Orient3d(o, p, edge)` of its three edges are not opposite (Plücker test; all three 0: the line lies in
  the plane, no hit as upstream), and the hit is ahead iff `Orient3d(a, b, c, o)` is opposite to the crossing sign (0: the
  origin is on the triangle, t = 0 as upstream). The triangle is closed: a ray onto a shared edge or vertex hits every
  triangle there, so it never misses all of them, at any scale, also on the boundary of an open mesh (the alternative,
  counting each crossing once by symbolic perturbation, misses at a boundary vertex or edge: kept for counting, Q3). The
  ray parameter and the barycentrics are the same doubles as before for every ray that both rules hit (near the edge of a lone or boundary
  triangle a hit can become a miss or the reverse, as the exact decision says); no parallel tolerance
  any more (upstream rejected rays within about 1e-8 of the plane). Cost (`benchmarks/RayQueryBenchmark`, where
  the numbers are): tree queries +2 % to +29 % per query, and the static single-triangle `Intersects` about 4.2x
  (35 -> 146 ns), with identical decisions. In a robust repair, the ray queries of `MeshRepairOrientation` add about
  0.9 % of the CPU time. 142 of 143 results are bit-identical on the meshes where any nearest hit changed, plus
  controls. Row in UPSTREAM_CHANGES.md.

### Q3 (fixed): IsInside counted vertex and edge hits wrongly

- Test: `ParityRayThroughAVertexOrAnEdge_InsideTestStaysRight` (also `ExactRayTriangleTests`).
- Issues: https://github.com/isl-org/Open3D/issues/5105 (a parity ray along an edge of the cube, wrong sign),
  https://github.com/mikedh/trimesh/issues/85 (an edge hit counted several times: outside taken as inside),
  https://github.com/mikedh/trimesh/issues/2317.
- Input -> wrong result (before the fix), all on IsInside's own fixed ray direction D = (0.331960519038825, 0.462531727525156,
  0.822111072077288), at scales 1 and 2^±20:
  - a square pyramid with its apex exactly at D and its base around 3D, query at the origin (outside): 1 hit (the base;
    the apex gives 0 of its 4 triangles), IsInside true;
  - a pyramid with its apex at 2D, base around the origin, query at D (inside): 0 hits, IsInside false;
  - a tetrahedron whose edge (2D -+ (0.125, 0, 0)) has its midpoint exactly at 2D on the ray, query at D (inside):
    2 hits (both triangles of the edge), IsInside false.
  The fourth case (the edge on the ray from outside) happens to be right. `WindingNumber` is right in all of them
  (asserted).
- Cause: `spatial/DMeshAABBTree.cs` `IsInside` (line 1046) took the parity of `FindAllHitTriangles` along a fixed
  direction; a vertex hit counted 0 to n times and an edge hit 0 to 2 times (Q2). Its doc comment already said it "can
  be wrong if the ray hits an edge or vertex exactly" (upstream behaviour). The closed rule of the Q2 fix alone would
  count an edge hit twice.
- Fix: `IsInside` counts the crossings of a perturbed ray (`ExactRayTriangle.RayHitPerturbed`, through the internal
  `DMeshAABBTree3.CountCrossings`): the edge signs of Q2, with exact zeros decided by Simulation of Simplicity
  (Edelsbrunner, Mücke 1990): the second point moved by (ε, ε², ε³), the origin by (ε⁴, ε⁸, ε¹²); the ε terms are
  exact `Orient2d` of projections and coordinate differences, checked against an explicit perturbation in big integers.
  The perturbed line meets no edge, so a ray through an edge or a vertex crosses exactly one triangle of a closed fan,
  and a ray in a triangle's plane does not cross it: the parity is right for every closed mesh without
  self-intersections and every point off its surface, at any scale. The same crossing test now decides
  `VolumeSampleCells.ExactWinding` (one mechanism; its retry over 64 directions on a degenerate segment is gone, the
  winding numbers are exact either way). The meaning of `IsInside` (parity) is unchanged; inside / outside for open,
  overlapping or self-intersecting inputs is Phase 5 #5 (generalized winding numbers, `WindingNumber`), not this fix.
  Cost: `IsInside` 6427 -> 7564 ns on the sphere, 3402 -> 4162 ns on the terrain (about 20 %), identical answers. Row in UPSTREAM_CHANGES.md.
