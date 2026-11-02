# Findings from the issue-tracker cases (docs/QA.md 2.11)

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

### S2 (open): Remesher smoothing and projection flip triangles without MinTriangleQuality

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

### S3 (open): without feature constraints the Remesher cuts the creases of a coarse part

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

### Q2: the ray-triangle test is not watertight

- Test: `RayOntoTheSharedDiagonalOrVertexOfASplitSquare_Hits` (known failure).
- Issue: https://github.com/mikedh/trimesh/issues/1766 (a ray onto the shared diagonal of a split square, rescaled:
  0 hits); also https://github.com/mikedh/trimesh/issues/2317 (vertex hits gave 0-n).
- Input -> wrong result: the unit square split along the diagonal (1,0)-(0,1) into (0,1,3) and (1,2,3), scaled by
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
- Fix ideas:
  - Watertight ray-triangle intersection (Woop, Benthin, Wald, JCGT 2013): axis permutation and shear to ray space,
    2D edge functions with the same operands for both triangles of an edge, a recomputation in higher precision when
    one is 0. Watertight, but a hit on an edge then counts in both triangles (fine for nearest hit, not for
    parity). Cost: rewrite of one core predicate used by every ray query (picking, IsInside, projections); needs an
    A/B benchmark (`benchmarks/PredicateComparison`) and a check of the callers that read the barycentrics.
  - Edge sides decided exactly: orient3d of the rounded ray line (O, O + D) against each edge, with the edge's vertices
    in a canonical order (lower id first) and symbolic perturbation for exact zeros, so every edge belongs to exactly
    one side. Watertight and single-counting, which also fixes Q3. Cost: adaptive predicates in the hot loop (fast
    filter, so likely small), more code; risk: changes results on edges for every caller.

### Q3: IsInside counts vertex and edge hits wrongly

- Test: `ParityRayThroughAVertexOrAnEdge_InsideTestStaysRight` (known failure).
- Issues: https://github.com/isl-org/Open3D/issues/5105 (a parity ray along an edge of the cube, wrong sign),
  https://github.com/mikedh/trimesh/issues/85 (an edge hit counted several times: outside taken as inside),
  https://github.com/mikedh/trimesh/issues/2317.
- Input -> wrong result, all on IsInside's own fixed ray direction D = (0.331960519038825, 0.462531727525156,
  0.822111072077288), at scales 1 and 2^±20:
  - a square pyramid with its apex exactly at D and its base around 3D, query at the origin (outside): 1 hit (the base;
    the apex gives 0 of its 4 triangles), IsInside true;
  - a pyramid with its apex at 2D, base around the origin, query at D (inside): 0 hits, IsInside false;
  - a tetrahedron whose edge (2D -+ (0.125, 0, 0)) has its midpoint exactly at 2D on the ray, query at D (inside):
    2 hits (both triangles of the edge), IsInside false.
  The fourth case (the edge on the ray from outside) happens to be right. `WindingNumber` is right in all of them
  (asserted).
- Cause: `spatial/DMeshAABBTree.cs` `IsInside` (line 1046) takes the parity of `FindAllHitTriangles` along a fixed
  direction; a vertex hit counts 0 to n times and an edge hit 0 to 2 times (Q2). Its doc comment already says it "can
  be wrong if the ray hits an edge or vertex exactly" (upstream behaviour).
- Fix ideas:
  - With the exact, single-counting ray test of Q2 the parity is right for closed meshes at no extra cost.
  - Without it: detect a degenerate hit (an edge function within a relative tolerance of 0, or a triangle nearly
    parallel to the ray near it) among the triangles in the boxes the ray passes, and re-cast along the next of a
    fixed list of directions. Local, but the detector must also catch rays that missed every triangle of a fan, so it
    has to scan near misses, not only hits: moderate cost, and still a tolerance.
  - Decide by `WindingNumber(p) > 0.5`: robust, but slower and a change of meaning for open, overlapping or
    self-intersecting meshes (parity vs winding), so an API behaviour change.
