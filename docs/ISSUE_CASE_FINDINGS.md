# Findings from the issue-tracker cases (docs/QA.md 2.11)

## Point, ray and inside queries

Tests: `tests/geometry3Sharp.Tests/SpatialQueryIssueCasesTests.cs`. Strict known failures are listed in its
`KnownFailures` with the finding id below.

### Q1 (fixed): the nearest hit among ties depended on the scale

- Test: `VerticalRaysOnAScaledMesh_HitIndependentlyOfTheScale` (also `NearestHit_AllSimilarities_MapBackToTheBaseline`).
- Issue: https://github.com/libigl/libigl/issues/2408 (vertical rays onto a scaled mesh stopped hitting below 1e-2).
- Input -> wrong result: a 5 x 5 height field (and a cube sphere of radius 2), vertical rays on a 1/4 grid, scaled by
  2^k. The hit sets and the nearest t were the same bit for bit at every 2^k (we never lost hits as libigl did), but
  at 2^-25 and below `FindNearestHitTriangle` returned another triangle of the same t for rays onto shared edges and
  vertices (eg at 0.01 the ray onto (4, 0.5) gave triangle 9 instead of 7).
- Cause: `spatial/DMeshAABBTree.cs` `find_hit_triangle` kept the first triangle found with the smallest t
  (`rayt < fNearestT`), and the traversal visits the nearer child box first; the boxes are padded by the absolute
  `box_eps = 50 * MathUtil.Epsilonf` (about 6e-6, line 2345), which dominates the box entry parameters once the
  model is smaller than that, so the order and the winner of the tie changed.
- Fix: ties go to the lowest triangle id (`rayt == fNearestT && ti < tID`), the same rule as
  `MeshQueries.FindHitTriangle_LinearSearch`. Equal-t triangles are always visited (a box's entry t is at most that of
  the triangles in it), so the result no longer depends on the traversal order. Row in UPSTREAM_CHANGES.md.

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
  `D . ((O - V0) x E2)`, `D . (E1 x (O - V0))` and the third as `DdN - (b1 + b2)`. The two triangles of an edge
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
