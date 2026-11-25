# Candidate pairs of the self-intersection checks: upper bounds (November 2026)

Follow-up of [TRI_TRI_FILTER_UPPER_BOUND_2026-11.md](TRI_TRI_FILTER_UPPER_BOUND_2026-11.md) (owner, via the
coordinator, 2026-11-24: a stop ends the design, not the search). That note found that the self-intersection checks
of the Reducer and the Remesher (`PreventSelfIntersections`) spend their time in `TriangleTriangleFilter` itself, not
in the exact test. This note bounds three candidates on the same stratum (the 11 Reducer and 4 Remesher-with-prevention
cases of `SelfIntersectionBenchmark`, procedural, serial):
- (a) fewer candidate pairs;
- (b) a plane-side pretest of the non-shared corners of neighbours;
- (c) a cheaper filter call.

**Verdict.**
- (b) is built in this PR (`TriangleTriangleFilter.BeyondSharedNeighbours`). It needs no new error bound and gives
  bit-identical results. It saves 16 % on the Reducer box grid and about 5 % on the bumpy sphere. On every other row the
  change is within the noise of this machine, ±10 %: rows that never call the filter moved by as much.
- (c) is below 10 % in place on every row, in all three forms: all_finite, per-triangle hoisting, and a semi-static 2D
  edge test. Stopped.
- (a) clears 10 % on four Reducer rows in place (10-41 %). It needs a new error bound (a cull of non-neighbour pairs
  with precomputed edge lines of the new triangle), which is more than a day of work. Left to the coordinator.

## Method

1. Probe: `benchmarks/SelfIntersectionBenchmark/probe/candidate_probe.patch`, never merged, against master fa06338.
   - It times `TriangleTriangleFilter.BeyondShared` by call site (Reducer: new triangles among themselves, new against
     the grid; Remesher: new among themselves, against the grid, `fold_count`) and by the number of shared corners.
   - It samples about 300 000 filter inputs per case, spread evenly over the run.
2. On the sample (`probe/CandidateProbeRun.cs`, `-p:CandidateProbe=true`): per-pair micro timings of the filter, of each
   candidate and of the combinations, multiplied by the call counts of the cell. Summed by `probe/sum_candidates.py`.
3. The micro timings run the code in a tight loop over the sample, so they overstate what latency-bound code costs in
   place. Every candidate that looked worth it was therefore also bounded in place: a measurement-only build of the
   whole benchmark, removing or faking the step, with the fingerprints checked (all the same).
   - `DOTNET_TieredCompilation=0` for the probe.
   - Reducer and Remesher with `gParallel.SerialOnThisThread`.
   - Cloud machine (Intel Xeon @ 2.10 GHz, 4 cores), net10.0 Release.
   - Raw output: `probe/results/`.

## Where the filter's time goes (probe)

Per case: the filter's share of the caller's net time (micro timings times calls), and the micro bounds:
- (b) the pretest in front of the filter, for every pair and for neighbours only (positive: faster);
- (a) the filter time of the pairs without a shared corner (all Separate here): the ceiling of culling them for free;
- (c) an ideal 2D separation test for those pairs with A's edge lines precomputed (no error bound);
- the filter's preamble before its first `Orient2d`: finiteness, shared corners, common axis plane, A's normal and axis.

| case | net ms | filter (micro) | (b) pretest all | (b) neighbours only | (a) unshared pairs' filter time | (c) ideal 2D unshared | preamble share |
|---|---:|---:|---:|---:|---:|---:|---:|
| reducer_harness_plate50_jitter0.5 | 4176 | 70.2 % | -13.1 % | -1.6 % | 59.2 % | 38.8 % | 35.1 % |
| reducer_harness_tilt_plate50_jitter0.5 | 2606 | 52.6 % | +0.5 % | +3.3 % | 11.4 % | 8.4 % | 23.4 % |
| reducer_harness_tilt_plate50 | 3904 | 49.9 % | -6.1 % | +5.3 % | 21.1 % | 15.4 % | 21.2 % |
| reducer_harness_plate100 | 449 | 35.1 % | -3.4 % | -2.3 % | 7.5 % | 5.1 % | 13.3 % |
| reducer_jitter_plate_z0 | 90 | 31.4 % | -4.0 % | -3.1 % | 4.8 % | 3.4 % | 15.1 % |
| reducer_jitter_plate_z0_to50 | 108 | 27.9 % | -4.3 % | -3.5 % | 4.4 % | 3.1 % | 12.0 % |
| reducer_jitter_plate_tilted_to50 | 3432 | 54.1 % | +1.6 % | +4.0 % | 15.8 % | 12.2 % | 24.7 % |
| reducer_jitter_plate_tilted | 3479 | 52.9 % | +1.6 % | +4.1 % | 15.2 % | 11.9 % | 23.9 % |
| reducer_integer_plate | 494 | 31.1 % | -4.3 % | -3.2 % | 6.8 % | 5.1 % | 13.2 % |
| reducer_bumpy_sphere | 339 | 21.9 % | +12.2 % | +11.9 % | 1.0 % | 0.8 % | 9.0 % |
| reducer_box_grid | 2444 | 40.9 % | +16.0 % | +16.9 % | 7.5 % | 5.7 % | 15.5 % |
| remesher_bumpy_sphere_si | 2629 | 15.9 % | +8.2 % | +6.3 % | 4.5 % | 3.1 % | 6.6 % |
| remesher_jitter_plate_z0_si | 583 | 14.0 % | -1.9 % | -1.1 % | 5.2 % | 3.7 % | 5.9 % |
| remesher_jitter_plate_tilted_si | 397 | 20.1 % | +0.1 % | +0.5 % | 5.0 % | 3.5 % | 8.1 % |
| remesher_box_grid_si | 1192 | 19.4 % | -2.4 % | -1.2 % | 8.0 % | 6.0 % | 6.7 % |

Most calls on the flat and tilted plates are pairs without a shared corner from the grid query: 28.6 M of the
33.0 M on harness plate 50 with jitter, about 12 400 per collapse. The reduced plates grow long slivers whose closed
boxes overlap much of the mesh, so these pairs pass the box query honestly, and the 2D test of the filter separates
them all. On curved meshes most calls are neighbours (one or two shared corners).

## In place

The micro bounds above are upper bounds in a different sense: in the full run, out-of-order execution hides much of the
latency the micro loop measures. Measured in place (whole benchmark, `runs=5`, interleaved, minimum; fingerprints the
same):

| measurement-only build | harness plate 50, jitter | harness tilted plate 50 | jitter plate tilted | box grid |
|---|---:|---:|---:|---:|
| (c1) `all_finite` removed | within noise (2865-3235 vs 3056-3147 ms) | | within noise | |
| (c2) the projection axis fixed (no cross product per pair) | | within noise | within noise | |
| (c3) the edge tests by plain double signs (no filter, no bound: the floor of any semi-static 2D test) | -4.5 % | -1 to -3 % | within noise | |
| (a) pairs without a shared corner answered Separate without a test (the floor of any cull) | -41 % | -16 % | -11 % | -10 to -13 % |

(c) below 10 % in place on every row: stopped. (c1), the balanced sum of `all_finite`, measured 5 % in the micro loop and
0 in place.

(a) is what remains. The cost of a non-neighbour pair is the call itself: the shared-corner loop, the finiteness and
axis-plane checks, two orientation `Orient2d` calls and the projections. The edge arithmetic is only a small part of
it. A cull in the Reducer's grid callback could avoid that cost. It would compute the new triangle's projection axis
and edge lines once per collapse and reject a candidate whose corners lie beyond one edge line by more than a static
bound, leaving the rest to the filter. That needs a new error bound with a written proof and the exact-code review,
which is more than a day of work. Its in-place ceiling is the last row: 41, 16, 11 and 10-13 % on four Reducer rows,
below 10 % on the others and on the Remesher (where unshared pairs are 4.5-8 % in the probe).

## (b), built

`TriangleTriangleFilter.BeyondSharedNeighbours`, opted in only by the Reducer (through
`TriangleTriangleFilter.IntersectsBeyondSharedNeighbours`) and by `Remesher.pair_intersects`. For neighbours not in one
common axis plane, it runs `NeighbourPlaneSides` before the projection. The other callers of `TriangleTriangleFilter`
keep the old path (review of #1544): `IntersectsBeyondShared` (`TetMeshImprover`, `MeshPostRepairCleanup`,
`AlphaWrapFeaturePass`, `AlphaWrapQualityPass`, `MeshRoundingRepair`) and `BeyondShared` (`ExactMeshArrangement`,
`MeshRoundingRepair`'s check mode), so their decisions and the rounding-dependent pins of `MeshRoundingRepair` do not
change.

How `NeighbourPlaneSides` decides:
- The non-shared corners of one triangle strictly on one side of the other's plane give Separate. A point of the first
  triangle is a convex combination of its corners, and its signed distance to the plane is the same combination of the
  corners' distances. That is 0 only at the shared vertex or on the shared edge, so the triangles meet at most there.
- Each sign is the one `ExactPredicates.Orient3d(P.V0, P.V1, P.V2, q)` returns: the same `Orient3dFilter` on the same
  differences, and 0 (not decided) where the filter does not prove it. So no error bound is added.
- Both triangles must be proven non-degenerate, because the exact test with shared vertices assumes it, and its answer
  is what the pair gets today. A plane is proven when the filter proves some corner off it; a degenerate triangle has
  `Orient3d` exactly 0 for every point, so it is never proven. With one shared vertex, the triangle whose plane decided
  is proven by the deciding signs, and the other one needs one corner proven off its plane. With a shared edge, the far
  corner of B off A's plane is off the line of the edge, so B is not degenerate either. (The review of #1544 found that
  without this guard a collinear or point-like A was answered Separate where the exact test answers true: 15 780 of
  400 000 such pairs. Separate is the geometric answer there, but the exact test's collinear branch is outside its
  precondition, and this PR does not change it.)
- Non-finite input gives NaN or infinite permanents, which fail the filter's comparisons.

`ExactMeshArrangement` keeps the plain `BeyondShared`, because it has made these plane tests itself just before
(`one_side_except_shared`).

Two choices came from the measurements:
- In one common axis plane every `Orient3d` is 0, so the pretest is skipped there. Without that skip the flat harness
  plate was 13-21 % slower.
- The pretest is not inlined into `by_projection`. Inlined, the pairs without shared corners got slower.

Before and after (`reducer remesher runs=3`, two interleaved runs of each build, minimum; results the same in every
run):

| case | before ms (each run) | after ms (each run) | change (min) | result |
|---|---:|---:|---:|---|
| reducer_harness_plate50_jitter0.5 | 2973, 3001 | 2902, 2928 | -2.4 % | same |
| reducer_harness_tilt_plate50_jitter0.5 | 2084, 2097 | 2216, 2280 | +6.3 % | same |
| reducer_harness_tilt_plate50 | 2790, 2778 | 2928, 2982 | +5.4 % | same |
| reducer_harness_plate100 | 358, 351 | 373, 384 | +6.3 % | same |
| reducer_jitter_plate_z0 | 70, 71 | 73, 71 | +1.4 % | same |
| reducer_jitter_plate_z0_to50 | 77, 78 | 78, 79 | +1.3 % | same |
| reducer_jitter_plate_tilted_to50 | 2562, 2480 | 2653, 2763 | +7.0 % | same |
| reducer_jitter_plate_tilted | 2561, 2590 | 2693, 2908 | +5.2 % | same |
| reducer_integer_plate | 362, 347 | 345, 398 | -0.6 % | same |
| reducer_bumpy_sphere | 253, 247 | 241, 240 | -2.8 % | same |
| reducer_box_grid | 1797, 1793 | 1592, 1491 | -16.8 % | same |
| remesher_bumpy_sphere | 138, 127 | 140, 131 | +3.1 % | same |
| remesher_jitter_plate_z0 | 44, 42 | 43, 42 | +0.0 % | same |
| remesher_jitter_plate_tilted | 44, 43 | 45, 52 | +4.7 % | same |
| remesher_box_grid | 65, 52 | 53, 68 | +1.9 % | same |
| remesher_bumpy_sphere_si | 1082, 1112 | 1159, 1213 | +7.1 % | same |
| remesher_jitter_plate_z0_si | 200, 215 | 208, 208 | +4.0 % | same |
| remesher_jitter_plate_tilted_si | 205, 207 | 232, 247 | +13.2 % | same |
| remesher_box_grid_si | 376, 374 | 384, 411 | +2.7 % | same |

Re-run of the Remesher rows with prevention (`remesher runs=5 only=_si`, 4 interleaved runs, minimum): bumpy sphere 1091 -> 1072 ms, jitter plate z = 0 200 -> 202, jitter plate tilted 212 -> 220 (+3.8 %), box grid 380 -> 365.

(After the review's degenerate guard; the table before it is in the history of #1544.) The Remesher rows without
prevention never call the filter, so their movement is the noise of this machine (up to ±10 % in earlier runs). The
row that gains is the one of the probe, the box grid (perpendicular faces meeting at a vertex); the bumpy sphere gains
3 % (micro bound +12 %: the in-place effect is again smaller than the micro loop's). The tilted plates are 5-7 %
slower, within the noise of the earlier runs; jitter plate tilted with prevention measured +13 % once and +3.8 % on
the interleaved re-run. No row is slower than 10 %.

The degenerate guard first proved the other plane by the other triangle's corners, which cost the tilted plates 10-15 %:
there the corners are too close to the plane for the filter, so the pairs lost their decision. The exact
`IsDegenerate` (floating-point prefilter first) costs nothing measurable.

Tests (`TriangleTriangleFilterTests`, `NeighbourPlaneSides_*`):
- Agreement with `ExactTriangleIntersection.IntersectsBeyondShared` on integer-grid neighbours (touching corners, folds,
  coplanar ones never decided), on random and nearly coplanar ones, at 2^-500, 2^-200, 2^300, 2^500 and with subnormal
  differences (the filter's underflow and overflow guards leave those undecided), and over `Similarity.All()`, with the
  same decisions under the exact scalings.
- The single cases: the second direction, a shared edge, degenerate triangles (collinear and point-like, the review's
  case), non-finite corners, a repeated id, the shared corner at the last index.
- The equivalence test: on neighbours, slivers, planar pairs and degenerate pairs at 2^0 and 2^±40, the answers of the
  Reducer's and the Remesher's paths are bit for bit those of the old paths.
- Hand mutants of the pretest (after the review): 15 tried, all killed but one equivalent one (a shared edge accepting
  "some corner proven" instead of "all on one side": with one non-shared corner the two are the same). Among them, both
  degenerate guards dropped, and each of the three ids dropped from the shared-corner check.

## Literature and library

- The plane-rejection step is the first step of T. Möller, *A fast triangle-triangle intersection test* (1997) and of
  Devillers and Guigue (2002), restricted here to the corners that are not shared.
- In the library, `ExactMeshArrangement.one_side_except_shared` and `Remesher.beyond_plane` do the same test with exact
  `Orient3d`. The second ran after the filter; it stays as the exact fallback.
- For the open candidate (a): semi-static filters with per-object precomputation (Shewchuk 1997; Meyer and Pion, FPG,
  2008) and the 2D separating-axis test of the edge normals.
- Sources in [docs/sources/exact-arithmetic-and-booleans.md](../sources/exact-arithmetic-and-booleans.md).
