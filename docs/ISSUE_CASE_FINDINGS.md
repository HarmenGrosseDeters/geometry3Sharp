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
