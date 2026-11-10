# Slow tests: triage (2026-11-10)

Source: full run of the test suite on master 25844aaa (8765 tests, linux net10.0 Release, 4 cores, a local run of the same job as the CI's; the CI trx of master is not available to this session, so the times are of this run). Each row is a test method (`Class.Method`, all rows of a theory together) with a row of 5 s or more; `max s` is its slowest row, `sum s` all rows. These times are of a loaded run (theory rows and classes run in parallel on 4 cores): a test alone is often 1.5-3x faster; the F calls below were checked alone where noted. 46 further methods carry `Slow` but run under 5 s (list: `tools/trx_durations.py --slow-list`, "candidates to untag"); they are not in the table.

Classes: **F** the library is slow for the input size (a user-visible performance defect: fix the library, with a benchmark before/after); **T** test design (oversized input, too many seeds or repeats, repeated setup, serial loops: shrink, same power shown with mutants); **K** the run is the check (keep `Slow` with a reason; above 10 s it moves to the runner or a nightly tier); `F?` a suspicion that needs a profile; `+` a mix. **Priority**: P1 every test above 10 s (rule violation), P2 F-class 5-10 s, P3 T-class 5-10 s, P4 K; F before T at equal priority, then by seconds (sum) per CI run.

The classes come from reading the test code and its comments, not from profiles, except where "alone" is given (single test, quiet machine). Nothing here is a proven library defect yet.

## Summary

| priority | tests | sum s | F | F? / F+T | T | K |
|---|---|---|---|---|---|---|
| P1 | 50 | 2034 | 10 | 6 | 30 | 4 |
| P2 | 17 | 149 | 14 | 3 | 0 | 0 |
| P3 | 46 | 504 | 0 | 4 | 42 | 0 |
| P4 | 3 | 23 | 0 | 1 | 0 | 3 |
| all | 116 | 2710 | | | | |

Counts per class (as written in the table): T: 72, F: 24, K: 5, T (F?): 4, F?: 4, F+T: 3, T+F: 2, K/T: 1, K (T): 1.

## Library findings (F and F?) to route to the owning areas

1. **MeshWindingNumberSolidify (adaptive, res 64) on `ThinPlates(6)` (144 triangles):** `Solidify_IsDeterministic` alone 42 s for 6 Computes = 7 s per Compute; `WrapFailed_ReasonReported` (one solidify, the wrap fails at once) alone 10 s; `HiddenGapRefinementTests.NarrowParallelGap` alone 16 s for 2 rows on a 24-triangle input (the refined grid is ~160x80x160 cells: dense or sparse?). About 12 tests in MeshRobustRepair / AlphaWrap / DefectInput / ContouringQuality depend on it. Profile the phases (`PhaseMilliseconds`: octree, contour, cleanup, reduce, selfint).
2. **MeshAlphaWrap + MeshRobustRepair.SimplifyWithin** on the same thin plates (`SolidifyNotClean_AlphaWrapped` 97 s loaded, `QualityPass_OffByDefault` 85 s loaded; wrap + simplify + Hausdorff about 30-60 s per pipeline, derived).
3. **TerrainSlopeDesign.Compute:** `WavyToe_130mHigh_AtUtm` has no terrain mesh and one 48-gon toe: alone 5 s for one Compute; the 48-gon designs on ~14k terrain triangles take 3-8 s per Compute (about 12 rows). TerrainSafetyLines.Compute in Guaranteed mode: 6.4k triangles, 3+ s per Compute.
4. **Remesher.PreventSelfIntersections** on a 288-triangle thin tube (alone 7 s; `ThinTube_WithPreventSelfIntersections_Clean`).
5. Weaker (profile first): `Reducer.PreventSelfIntersections` on a 6k-triangle slab; `MeshRoundingRepair` on ~350-triangle degenerate spheres (check mode inflates); QualityMesh2.Refiner on a 4-point 0.67-degree hull case; MeshFidelityMetrics at default 50k/100k samples (~0.6 s per Compute for 1-2k triangles, probably by design).
6. Not library: TerrainGridTin (alone 4 s for 5 Computes), the Meshing2 oracle numbers (exact reference path, test-harness cost), the PolyhedralEnvelope oracle (quadratic in the triangle count: Sphere(4) 28 s, Sphere(6) 217 s loaded / 127 s alone; a bounding-box pre-filter in the test oracle fixes it).

## Top 15 by priority and seconds

| # | test | class | prio | max s | sum s |
|---|---|---|---|---|---|
| 1 | `PolyhedralEnvelopeTests+ContainsTriangleMesh1.ContainsTriangle_MatchesOracle` | T | P1 | 216.9 | 217 |
| 2 | `Meshing2OracleModesTests+ExtremeScalesSmallF38d.ExtremeScales_SameVerdictAndDetailsOnBothPaths` | T | P1 | 98.6 | 99 |
| 3 | `MeshRobustRepairTests.SolidifyNotClean_AlphaWrapped` | F | P1 | 96.7 | 97 |
| 4 | `AlphaWrapQualityPassTests.RobustRepair_QualityPass_OffByDefault_ImprovesTheWrapWhenOn` | F | P1 | 85.0 | 85 |
| 5 | `Meshing2OracleModesTests+CorruptedRowsSlow3.CorruptedRow_RejectedOnBothPaths` | T | P1 | 29.1 | 84 |
| 6 | `TerrainSlopeEarlyRegionCheckTests.Similarities` | F+T | P1 | 27.6 | 81 |
| 7 | `Meshing2OracleModesTests+CorruptedRowsSlow1.CorruptedRow_RejectedOnBothPaths` | T | P1 | 42.1 | 74 |
| 8 | `MeshRobustRepairTests.Solidify_IsDeterministic` | F | P1 | 70.2 | 70 |
| 9 | `Meshing2OracleModesTests+ExtremeScalesLargeF38d.ExtremeScales_SameVerdictAndDetailsOnBothPaths` | T | P1 | 68.5 | 68 |
| 10 | `TerrainSlopeClippedRegionTests.WavyToeNearTheOrigin_ClippedSliverRaisesTheMargin` | F | P1 | 22.8 | 67 |
| 11 | `Meshing2OracleFastPathCheckTests.CatalogueRow` | K | P1 | 13.2 | 63 |
| 12 | `Meshing2OracleModesTests+CatalogueRowsSlow3.CatalogueRow_SameVerdictOnBothPaths` | T | P1 | 17.4 | 63 |
| 13 | `TerrainSlopeBandRepairTests.WavyToeOnHighWaves_Similarities` | F | P1 | 17.4 | 61 |
| 14 | `TerrainSlopeBandRepairTests.WavyToe_Similarities` | F | P1 | 22.8 | 61 |
| 15 | `RoundingExactPairsTests.Similarities_SameResult_Pow2BitIdentical` | T | P1 | 24.2 | 52 |

## All tests (116), in priority order

| prio | test | rows | max s | sum s | class | area | input size | action |
|---|---|---|---|---|---|---|---|---|
| P1 | `MeshRobustRepairTests.SolidifyNotClean_AlphaWrapped` | 1 | 96.7 | 97 | F | MeshRobustRepair solidify+wrap+simplify | ThinPlates(6): 12 boxes, 144 tris, res 64; 3 pipelines | Profile MeshAlphaWrap and SimplifyWithin; share the wrap run |
| P1 | `AlphaWrapQualityPassTests.RobustRepair_QualityPass_OffByDefault_ImprovesTheWrapWhenOn` | 1 | 85.0 | 85 | F | MeshRobustRepair wrap+quality pass | ThinPlates(6), 2 full pipelines | Same profile; apply the pass to one simplified wrap |
| P1 | `TerrainSlopeEarlyRegionCheckTests.Similarities` | 8 | 27.6 | 81 | F+T | terrain slope | WavyOnWaves(40), ~10k terrain tris, 2 Computes per row | Early-vs-late is covered in other rows: run on 2^k frames only or coarser terrain; profile TerrainSlopeDesign.Compute |
| P1 | `MeshRobustRepairTests.Solidify_IsDeterministic` | 1 | 70.2 | 70 | F | MeshWindingNumberSolidify adaptive res 64 | 144 tris, 6 runs (alone: 42 s = 7 s per Compute) | Profile MeshWindingNumberSolidify phases; 7 s for 144 triangles is a library cost |
| P1 | `TerrainSlopeClippedRegionTests.WavyToeNearTheOrigin_ClippedSliverRaisesTheMargin` | 8 | 22.8 | 67 | F | terrain slope | WavyOnWaves(48), ~15k tris, 1 Compute (2 attempts) per row | Profile Compute on the 48-gon; coarsen terrain if the sliver case survives |
| P1 | `TerrainSlopeBandRepairTests.WavyToeOnHighWaves_Similarities` | 8 | 17.4 | 61 | F | terrain slope | WavyOnWaves(32), ~10k tris, 1 Compute per row | as above |
| P1 | `TerrainSlopeBandRepairTests.WavyToe_Similarities` | 8 | 22.8 | 61 | F | terrain slope | WavyOnWaves(48), ~14k tris, 1 Compute per row | Same 48-gon design: share reference, cut non-exact frames, profile Compute |
| P1 | `RegressionCorpusTests.Pipeline_MatchesTheStoredOutcomes` | 7 | 39.8 | 42 | F+T | repair pipelines | Defects.All(): fixed + 12 random x 2 placements per pipeline | Per-pipeline timing; robust repair is the slow row; split into rows |
| P1 | `QualityMesh2HullTests.HullSplitNearASegment_Invariance` | 8 | 10.1 | 36 | F? | QualityMesh2 refinement | 4 points, segments at 0.67 deg, ~4.5 s per row | Profile Refiner vs Oracle.Check; mesh plain input once |
| P1 | `DefectInputTests.MeshRobustRepair_DefectInput_CleanWhenReportedClean` | 32 | 11.7 | 31 | F | MeshRobustRepair | Defects.All() small meshes, SolidifyResolution 64, 1 Apply per row | Profile MeshRobustRepair.Apply per case (same code as the corpus) |
| P1 | `MeshRobustRepairTests.Solidify_StopsOnSelfIntersectingContour_FinishGivesTheFullResult` | 1 | 26.3 | 26 | F | MeshWindingNumberSolidify | ThinPlates(6), 5 runs | same profile; reuse the full run |
| P1 | `MeshRobustRepairTests.SolidifyNotClean_WrapFails_PostProcessedSolidifyUsed` | 1 | 19.7 | 20 | F | MeshRobustRepair solidify | ThinPlates(6), 2 solidify runs | same profile; cache the 'without' result |
| P1 | `RemeshSizingTests.ThinTube_WithPreventSelfIntersections_Clean` | 1 | 11.6 | 12 | F | Remesher.PreventSelfIntersections | Tube(16,4) 288 tris, 20 passes (alone: 7 s) | Profile Remesher with PreventSelfIntersections; 10 passes |
| P1 | `TerrainSlopeBandMergeTests.Repro_BandMergesAndTheirLowering` | 1 | 11.4 | 11 | F+T | terrain slope | WavyOnWaves(48), 4 Computes | Reuse the BandRepair reference; drop one scale; profile Compute |
| P1 | `PolyhedralEnvelopeTests+ContainsTriangleMesh1.ContainsTriangle_MatchesOracle` | 1 | 216.9 | 217 | T | PolyhedralEnvelope test oracle (BigInteger) | Sphere(6) 300 tris, ~1200 queries (alone: 127 s) | Box pre-filter in OracleTriangle (quadratic: mesh0 108 tris 28 s, mesh1 217 s) |
| P1 | `Meshing2OracleModesTests+ExtremeScalesSmallF38d.ExtremeScales_SameVerdictAndDetailsOnBothPaths` | 1 | 98.6 | 99 | T | Meshing2 oracle | F-38/d ~22k tris, 2^-270, exact reference path | Reduced F-38/d (<=2k tris) for scale checks |
| P1 | `Meshing2OracleModesTests+CorruptedRowsSlow3.CorruptedRow_RejectedOnBothPaths` | 5 | 29.1 | 84 | T | Meshing2 oracle | F-24/b, F-37, F-12/b, F-4/a, F-5 | same |
| P1 | `Meshing2OracleModesTests+CorruptedRowsSlow1.CorruptedRow_RejectedOnBothPaths` | 3 | 42.1 | 74 | T | Meshing2 oracle | F-39 (1105 spokes), F-4/b, F-24/a x 4 corruptions | Reuse the fast report instead of a third Oracle.Check; cache mesher output per row |
| P1 | `Meshing2OracleModesTests+ExtremeScalesLargeF38d.ExtremeScales_SameVerdictAndDetailsOnBothPaths` | 1 | 68.5 | 68 | T | Meshing2 oracle | same mesh, 2^512 | same; mesh once, share |
| P1 | `Meshing2OracleFastPathCheckTests.CatalogueRow` | 96 | 13.2 | 63 | K | Meshing2 oracle | all covered rows, serial in one class | Share mesher output; split heavy rows |
| P1 | `Meshing2OracleModesTests+CatalogueRowsSlow3.CatalogueRow_SameVerdictOnBothPaths` | 5 | 17.4 | 63 | T | Meshing2 oracle | 5 slow rows, exact reference | Cache mesher output per row |
| P1 | `RoundingExactPairsTests.Similarities_SameResult_Pow2BitIdentical` | 11 | 24.2 | 52 | T | ExactMeshBoolean + SafeRounding | GridBox 12 (~1.4k tris) or sphere-30; check mode over Similarity.All() | Grid 6 for invariance rows; check only on base and 2^k frames |
| P1 | `Meshing2OracleBehindIndexTests.UnrefinedMesh_AtM3_CircumcentreQueriesAreSupersets` | 5 | 17.6 | 47 | T | Meshing2 oracle | ~1344 points + 144 segs, 5 seeds | 300 points, 3 seeds, parallel |
| P1 | `Meshing2OracleModesTests+CorruptedRowsSlow2.CorruptedRow_RejectedOnBothPaths` | 3 | 21.9 | 44 | T | Meshing2 oracle | F-42, F-38/d, F-34 | same |
| P1 | `TerrainSlopeSnapCertificateTests.BoundHoldsSampled` | 9 | 12.4 | 44 | T | terrain slope snap | 23x25 grid (~1.1k tris), 24 Computes per row + sweep check | Split 3x4x2 grid into sub-rows; restrict the sweep check; profile which part dominates |
| P1 | `Meshing2OracleFastPathCheckTests.HullDomain_BoundaryVertexOffTheHullEdge` | 2 | 16.2 | 32 | T | Meshing2 oracle | 400-gon fan, hub degree 400 | n ~64; profile (oracle work, not library) |
| P1 | `TerrainSafetyTests.SafetyLine_Guaranteed_OutsideTheDistanceAndCertified` | 1 | 32.1 | 32 | T+F | terrain safety lines | PitTerrain 401x401 grid (320k tris), res 200, Guaranteed | Share the pit in a static Lazy, n=201; profile TerrainSafetyLines.Compute (Guaranteed) separately |
| P1 | `TriangleTriangleFilterTests.AxisPlaneFactorization_SameSignAsOrient3d` | 3 | 11.5 | 30 | T | ExactPredicates.Orient3d | 108k Orient3d per row at extreme scales | 100 per cell |
| P1 | `TerrainSlopeOneBuildTests.InsertionOrder_DoesNotChangeTheResult` | 1 | 29.0 | 29 | T | terrain slope | 14 cases x (1+5 orders) = ~84 Computes in one Fact | Theory per case (parallel classes); 2 shuffles |
| P1 | `Meshing2GoldenTests.M3_RecordedRows_AreReproduced` | 1 | 28.5 | 28 | K | Meshing2 golden | ~60 M3 rows, serial | Run rows in parallel / split by row |
| P1 | `PolyhedralEnvelopeTests+ContainsTriangleMesh0.ContainsTriangle_MatchesOracle` | 1 | 28.2 | 28 | T | PolyhedralEnvelope test oracle | Sphere(4) 108 tris, ~430 queries | same pre-filter |
| P1 | `Meshing2OracleModesTests+CatalogueRowsSlow1.CatalogueRow_SameVerdictOnBothPaths` | 3 | 14.1 | 24 | T | Meshing2 oracle | F-39, F-4/b, F-24/a | cache mesher output |
| P1 | `ConstrainedDelaunayMesh2Tests.ExtremeScales_GiveTheSameMesh` | 3 | 13.2 | 21 | T | CDT | 60 seeds at 2^-560, 2^-1000, 2^500 | Compute unscaled once; 20 seeds |
| P1 | `PolyhedralEnvelopeTests+ContainsSegmentMesh1.ContainsSegment_MatchesIntervalOracle` | 1 | 19.5 | 20 | T | PolyhedralEnvelope test oracle | Sphere(6), ~1000 segments | same pre-filter |
| P1 | `SafeRoundingDegenerateInputTests.DenseClusters_WithinTheBound_MostlyClean` | 1 | 18.4 | 18 | K | ExactMeshBoolean + SafeRounding | 198 cases on Sphere(6), counts asserted | Keep; split Cluster/FoldedCluster in 2 classes |
| P1 | `ThreadingFixesTests.RemesherPro_FullRemesh_ParallelEqualsSerialAndRepeats` | 1 | 18.1 | 18 | T | RemesherPro | NoisySphere(40) grown, 1 serial + 4 parallel | NoisySphere(20), 2 repeats; mutation check (queue order) |
| P1 | `ReducerSharedDeviationQueriesTests.Reduction_SameWithAndWithoutSharing` | 1 | 17.2 | 17 | T | Reducer MaxDeviation | 18 deviation-checked reductions of 3-4k tris | Bumpy(14), Terrain(28); split meshes |
| P1 | `RoundingExactPairsTests.SerialEqualsParallel_ComputeTwice` | 11 | 10.7 | 17 | T | ExactMeshBoolean + SafeRounding | 6 Computes per row | 2 parallel repeats |
| P1 | `WrapSimplifyQualityTests.SimplifyWithin_SliverOfTheWrap_Removed` | 1 | 16.4 | 16 | T | alpha wrap + SimplifyWithin | 44 cases, wrap recomputed per case (22x per shape) | Compute the wrap once per shape |
| P1 | `Meshing2GoldenTests.M3Constrained_RecordedRows_AreReproduced` | 1 | 15.9 | 16 | K | Meshing2 golden | ~30 constrained cases, serial | parallel / split |
| P1 | `GeodesicDistanceTests.SingleSource_RoughTerrain_NotPinned` | 1 | 15.1 | 15 | T (F?) | MeshGeodesicDistance | 501x501 grid, 500k tris | 201x201; profile only if a 250k-vertex Compute should be faster |
| P1 | `QualityMesh2PinchFreeTests.StarFuzz_ConstrainedQuality_NoPinch` | 1 | 15.0 | 15 | T | QualityMesh2 fuzz | 3000 seeds serial | 4 seed-range classes |
| P1 | `PolyhedralEnvelopeTests.ContainsTriangle_MatchesOracle_ExtremeScales` | 1 | 14.8 | 15 | T | BigInteger oracle | Sphere(4), 3 scales x 150 queries | Take(40); parallel per scale |
| P1 | `SnapRounding2Tests.Fuzz_SeparationAndSimilarity_WithRestore` | 1 | 14.3 | 14 | T | SnapRounding2 | 400 iterations, exact rational oracle | 4 parallel rows of 100 iterations |
| P1 | `QualityMesh2OracleRestoreTests.RestorePixels_MatchTheLibrarys_OnTheFuzz` | 1 | 14.0 | 14 | T | QualityMesh2 fuzz | 3000 star seeds serial, duplicates KeepsChainsApart | Share per-seed results; parallel |
| P1 | `ExactPredicatesTests.Predicates_SubnormalPartialProducts_TimesLargeFactors` | 1 | 12.9 | 13 | T | ExactPredicates vs bigint | 100,000 iterations x 4 predicates | 10k; split fixed cases and campaign |
| P1 | `TerrainSafetyTests.SafetyLine_FiveMetresBehindTheTopCrest` | 2 | 10.2 | 12 | T | terrain safety lines | PitTerrain rebuilt per row | Static Lazy pit, n=201 |
| P1 | `QualityMesh2KeepsChainsApartTests.StarPolygonFuzz_VerdictsByClass` | 1 | 11.8 | 12 | T | QualityMesh2 fuzz | 3000 seeds (same as Restore/PinchFree) | share; split by seed range |
| P1 | `MeshFidelityMetricsTests.Invariance_SimilarityAll` | 1 | 10.1 | 10 | T | MeshFidelityMetrics | 20k samples x all similarities | 5k / 1.5k samples |
| P1 | `AlphaWrapQualityPassTests.ImproveWrapQuality_CertifiedByConstruction` | 1 | 10.0 | 10 | T | alpha wrap + quality pass | 5 inputs x 4 variants, 20 Hausdorff checks | Split by input; loosen tolerance |
| P2 | `HiddenGapRefinementTests.NarrowParallelGap_IsRefinedUntilResolved` | 2 | 10.0 | 18 | F | MeshWindingNumberSolidify | 2 boxes (24 tris), cell 0.25, MaxRefineLevels 3 (alone: 16 s for 2 rows) | Profile adaptive refinement: is the refined grid dense (~160x80x160)? |
| P2 | `TerrainSlopeBandRepairTests.WavyToeOnHighWaves48_Succeeds` | 2 | 8.1 | 12 | F | terrain slope | WavyOnWaves(48), 1 Compute per row | Rows duplicate WavyToe_Similarities: merge or drop |
| P2 | `MeshRoundingRepairIncrementalTests.Checked_SameResultAsUnchecked` | 11 | 6.2 | 12 | F? | MeshRoundingRepair | ~350 tris, plain + checked per row | Profile plain run; heavy rows in own classes |
| P2 | `TerrainSlopeClippedRegionTests.WavyToe32_Succeeds` | 2 | 5.5 | 10 | F | terrain slope | WavyOnWaves(32), 1 Compute per row | Drop rows already covered by WavyToeOnHighWaves_Similarities |
| P2 | `DualContouringTests.Solidify_NarrowGap_RefinesUntilNoSelfIntersections` | 2 | 8.7 | 10 | F | MeshWindingNumberSolidify | same 24-tri 2-box mesh, MaxRefineLevels 3 | as HiddenGap |
| P2 | `MeshRobustRepairTests.WrapFailed_ReasonReported` | 1 | 9.2 | 9 | F | MeshRobustRepair solidify | ThinPlates(6), 1 solidify (alone: 10 s) | one solidify of 144 tris = 9-10 s: same library profile |
| P2 | `TerrainSlopeBandRepairTests.WavyToeNearTheOrigin_SucceedsAsAtUtm` | 1 | 8.4 | 8 | F | terrain slope | WavyOnWaves(48), 2 Computes | Profile Compute; share the UTM design |
| P2 | `HiddenGapRefinementTests.NearlyParallelGap_IsDetected` | 1 | 8.4 | 8 | F | MeshWindingNumberSolidify | 24 tris, tilted gap, MaxRefineLevels 3 | as HiddenGap |
| P2 | `ContouringQualityTests.AmbiguousFaces_RefinedInsteadOfLeftOut` | 3 | 5.3 | 8 | F | MeshWindingNumberSolidify adaptive | 144 tris, res 64, ReduceTolerance 0: 3-5 s per Compute | same solidify profile |
| P2 | `DefectFreeReduceTests.ThinSlab_PlainReducerIntersects_CheckedOneDoesNot` | 1 | 7.6 | 8 | F? | Reducer | EdgeVertices 40 slab, 2 reductions to 120 | Profile Reducer.PreventSelfIntersections; EdgeVertices 24 |
| P2 | `TerrainSafetyTests.SafetyLine_Guaranteed_GapNotCertified` | 1 | 7.3 | 7 | F | terrain safety lines | 81x41 grid (6.4k tris), 2 Computes | Profile TerrainSafetyLines.Compute Guaranteed; drop/reuse the second Compute |
| P2 | `RoundingExactPairsTests.Edits_ReuseIdsAndSearchAgain_StillTheSame` | 1 | 7.3 | 7 | F? | MeshRoundingRepair | 12 runs of Sphere(6)-based, check:true | Profile with check off; split by family |
| P2 | `TerrainSlopeBandRepairTests.Reuse` | 1 | 6.6 | 7 | F | terrain slope | WavyOnWaves(48), 2 Computes | Smaller reuse design |
| P2 | `MeshRobustRepairTests.CancelledDuringWrap_NoFinish` | 1 | 6.4 | 6 | F | MeshRobustRepair solidify (stopped) | ThinPlates(6) | same profile |
| P2 | `TerrainSlopeDesignTests.WavyToe_130mHigh_AtUtm` | 1 | 6.1 | 6 | F | terrain slope (no terrain) | 48-gon toe, 130 m, no terrain, 1 Compute (alone: 5 s) | Profile TerrainSlopeDesign.Compute (slope map / simplify / partition): the strongest terrain F candidate |
| P2 | `TerrainSlopeEarlyRegionCheckTests.Reuse` | 1 | 5.8 | 6 | F | terrain slope | WavyOnWaves(40), 2 Computes | Smaller design |
| P2 | `TerrainSlopeBandRepairTests.WavyToeOnHighWavesNearTheOrigin_OneAttemptAsAtUtm` | 1 | 5.7 | 6 | F | terrain slope | WavyOnWaves(32), 2 Computes | Profile Compute |
| P3 | `DefectFreeReduceTests.UnionQuery_SameReductionAsAQueryPerTriangle_UnderSimilarity` | 8 | 8.8 | 48 | T | Reducer.PreventSelfIntersections | slab ~6k tris x 2 checked reductions per similarity | EdgeVertices 24; exact + one inexact |
| P3 | `ReducerSpeedTests.SameReductionAsTheReference_UnderSimilarity` | 8 | 8.1 | 47 | T (F?) | Reducer | ThinSlab ~6k tris + HolyPlate(20), fast + reference per row | Slab EdgeVertices 20; profile Reducer.PreventSelfIntersections on 6k |
| P3 | `RemeshSizingTests.SameResultUnderSimilarity` | 8 | 5.2 | 27 | T | Remesher | Tube 16x4, target 0.1 grows to thousands of tris | target 0.2 if T5 tie reproduces; share unscaled run |
| P3 | `Meshing2CatalogueTests.Row_IsInItsState` | 129 | 7.0 | 24 | T | Meshing2 catalogue | 129 rows; covered rows mesh + oracle (third place) | Check state only / shared cache |
| P3 | `Meshing2OracleModesTests+CatalogueRowsSlow2.CatalogueRow_SameVerdictOnBothPaths` | 3 | 8.2 | 19 | T | Meshing2 oracle | F-42, F-38/d, F-34 | cache mesher output |
| P3 | `SafeRoundingDegenerateInputTests.Family_RepairedCleanWithinTheBound` | 7 | 5.4 | 17 | T | ExactMeshBoolean + SafeRounding | <=84 cases per family row | Split by k |
| P3 | `RemesherSelfIntersectionTests.ParallelEqualsSerial_RepeatedRuns` | 2 | 8.0 | 15 | T | Remesher / RemesherPro | 3 cases x 3 remeshes, 20 passes | Take(2), 1 repeat |
| P3 | `TerrainSlopeEarlyRegionCheckTests.ReplacementAndUnweldedTerrain_SameResult` | 2 | 6.2 | 12 | T | terrain slope | WavyOnWaves(40), 2 Computes per row | Smaller terrain |
| P3 | `TerrainSlopeEarlyRegionCheckTests.RegionFailure_SkipsTheCopy_SameResult` | 2 | 6.3 | 12 | T+F | terrain slope | WavyOnWaves(40), 2 Computes per row | Coarsen terrain |
| P3 | `QualityMesh2SubnormalTests.EveryScale_KeepsTheDomain_AndNormalScalesGiveTheSameMesh` | 2 | 6.4 | 12 | T | QualityMesh2 subnormal | 32 points, 17 scales serial | 8 boundary scales; rotated as a parallel class |
| P3 | `TerrainSlopeEarlyRegionCheckTests.FirstShape_SameResult` | 2 | 6.3 | 11 | T | terrain slope | WavyOnWaves(32), 2 Computes per row | Same early-vs-late as Similarities/RegionFailure: drop or shrink |
| P3 | `MeshFidelityMetricsTests.OuterVolumeIoU_ManyStackedLayers_KeepsTheBox` | 1 | 9.9 | 10 | T | MeshFidelityMetrics | up to 40 layers at 50k samples | SurfaceSamples 5000 |
| P3 | `AdaptiveOctreeTests.AdaptiveSolidify_SameAsUniform_WithFarFewerEvaluations` | 1 | 9.7 | 10 | T | MeshWindingNumberSolidify | 24 tris, uniform grid 192 (7M nodes) | Resolution 96 or 64; check the ratio |
| P3 | `RoundingExactPairsTests.SkippingSearch_SameAsFull_AndSkips` | 11 | 5.9 | 9 | T | MeshRoundingRepair check mode | GridBox 12, check mode | Grid 8 |
| P3 | `TerrainSlopeCatchLineTests.Reuse_AfterACancelledLocalLimitRun` | 1 | 9.0 | 9 | T | terrain slope | 20x20 grid, loop k=1..60 cancel points, up to 120 Computes | Cancel at ~12 spread points |
| P3 | `MeshRoundingRepairIncrementalTests.Searches_IncrementalAfterEdits` | 1 | 9.0 | 9 | T | MeshRoundingRepair | all Cases() in check mode, one Fact | Theory per case |
| P3 | `ExactFilterBoundTests.SegmentPoint_BoundHolds_AtWorstCorners` | 1 | 8.9 | 9 | T | BigInteger bound check | 30 iter x 256 corners x 3 | 10 iter or sample corners |
| P3 | `DefectFreeReduceTests.UnionQuery_SameReductionAsAQueryPerTriangle` | 5 | 7.3 | 9 | T | Reducer.PreventSelfIntersections | slab 6k tris etc., 3 reductions per row | smaller spheres/plates |
| P3 | `MeshFidelityMetricsTests.Curvature_PlaneAndCylinder_OpenAndDegenerate` | 1 | 8.8 | 9 | T | MeshFidelityMetrics | ~4k tris, default 50k/100k samples | VolumeSamples 0, SurfaceSamples 5000 |
| P3 | `ReducerSpeedTests.SameReductionAsTheReference` | 8 | 5.9 | 9 | T (F?) | Reducer | thin slab 6k tris -> 120 + holy plates | Slab EdgeVertices 20; profile PreventSelfIntersections |
| P3 | `MeshMinimizerTests.TimeBudget_StopsWithAFailingResult` | 1 | 8.1 | 8 | T | MeshMinimizer | 8 s wall-clock budget by design | Budget 3 s, sleep 300 ms |
| P3 | `QualityMesh2OracleRestoreTests.RestorePixels_MatchTheLibrarys_OnSegmentSoups` | 1 | 8.1 | 8 | T | QualityMesh2 fuzz | 2000 soups serial | Parallel / 1000 seeds |
| P3 | `ExactBooleanFilterTests.Orient3dFiltered_SignHoldsAtAllCornersOfTheErrorBoxes` | 1 | 7.9 | 8 | T | BigInteger corner check | ~600k 4x3 determinants | iter 1, fewer exponents or sampled masks |
| P3 | `GroundFilterTests.DeepPit_ClothReachesTheRim` | 1 | 7.9 | 8 | T | ClothSimulationFilter | 480x480 m, cloth 230k cells, >500 iterations | ClothResolution 2.0 |
| P3 | `MeshMinimizerTests.Timeouts_CountAsDifferentError` | 1 | 7.9 | 8 | T | MeshMinimizer | predicate sleeps 10 s, 500 ms timeouts | 50-100 ms timeout |
| P3 | `UVAtlasTests.Atlas_ClosedSurfaces_NoOverlap_BoundedDistortion` | 1 | 7.8 | 8 | T (F?) | UVAtlasGenerator | 4 meshes x 2 methods = 8 Computes in one Fact | Theory per mesh |
| P3 | `MeshEnvelopeTests.Fuzz_ContainedMeansWithinEpsilon_NotContainedHasAWitness` | 1 | 7.8 | 8 | T | MeshEnvelope | BumpySphere(8), 400 queries, exact-only ~20 ms each | 150 queries |
| P3 | `TerrainSlopeOracleTests.RandomNearTheLimit_AgreesWithRationals` | 7 | 5.1 | 8 | T | slope predicate oracle | 3000 triples per row, rational reference at 1e150/1e300 | 1000 iterations; split large scales |
| P3 | `RoundingDirtyTreeTests.ManySlivers_Similarities_Pow2BitIdentical` | 1 | 7.5 | 7 | T | MeshRoundingRepair | SliverBox(16,7), check mode over all similarities | Theory per similarity or 2^k frames only |
| P3 | `Meshing2SnapGateTests.SnapRow_MeetsItsBound_OnlyWithSnapping` | 4 | 6.5 | 7 | T | Meshing2 snap | 4 rows; unsnapped control runs to the Steiner limit (100000) | SteinerLimit ~MaxVertices+Count+1 in the control |
| P3 | `QualityMesh2Tests.SpikeOf084Degrees_ReachesAnO11UnsplittablePiece` | 1 | 7.3 | 7 | T | QualityMesh2 | 29 angles serial | 1-degree steps or parallel |
| P3 | `DelaunayMesh2Tests.Fuzz_EveryResultPassesTheOracle_AndItsOwnCheck` | 1 | 7.2 | 7 | T | DelaunayMesh2 fuzz | 600 seeds, each triangulated twice | Use one result; seed-range classes |
| P3 | `ThreadingFixesTests.RemesherPro_FaceAlignedProjection_IsDeterministic` | 1 | 7.1 | 7 | T | RemesherPro | NoisySphere(130) ~170k tris, 4 runs | EdgeVertices 50; build once and clone |
| P3 | `MeshFidelityMetricsTests.OuterVolumeIoU_NotConverged_NaN` | 1 | 6.9 | 7 | T | MeshFidelityMetrics | 70 layers, 64 rounds at 50k samples | SurfaceSamples 5000 |
| P3 | `ThreadingFixesTests.RemoveOccludedTriangles_PerVertex_RemovesEveryOccludedTriangle` | 1 | 6.9 | 7 | T | RemoveOccludedTriangles | 100k tris x 20 runs | 6 runs of 50k; verify with the BitArray mutation |
| P3 | `TerrainSlopeOneBuildTests.OneBuild_EqualsARebuildAtTheFinalLowering` | 1 | 6.8 | 7 | T | terrain slope | 13 cases x 2 Computes in one Fact | Theory per case |
| P3 | `MeshFidelityMetricsTests.Curvature_IdenticalAndRefinedSpheres` | 1 | 6.7 | 7 | T | MeshFidelityMetrics | icospheres up to 20k tris, 6 Computes | SurfaceSamples 5000, VolumeSamples 0 |
| P3 | `RemesherQualityTests.Primitives_NoFlatTriangles_FewerSelfIntersections` | 1 | 6.2 | 6 | T | Remesher quality | 17 primitives x 2 x 2 remeshes | Cut sizes or share |
| P3 | `AlphaWrapQualityPassTests.InvariantUnderSimilarities` | 1 | 6.2 | 6 | T | ImproveWrapQuality | BoxWrap rebuilt per test | Class fixture for BoxWrap |
| P3 | `BoxTreeOrderTests.RandomOrders_SameQueriesPairsAndSegments` | 2 | 5.9 | 6 | T | TriangleBoxTree | 19k boxes, 3 seeds | 2 seeds on the large row |
| P3 | `SafeRoundingBoundTests.Utm_CleanWithTheUlpTolerance_WithinIt` | 1 | 5.8 | 6 | T | ExactMeshBoolean + SafeRounding | 114 UTM inputs, 228 booleans in one Fact | Split by family (re-derive thresholds) |
| P3 | `MeshFidelityMetricsTests.Reuse_SecondComputeEqualsFreshInstance` | 1 | 5.6 | 6 | T | MeshFidelityMetrics | ~9 Computes at default samples | lower sample counts |
| P3 | `RemesherSelfIntersectionTests.Primitives_NoSelfIntersections` | 1 | 5.5 | 6 | T | Remesher | 6 cases x 2 x 20 passes | Theory per case |
| P3 | `PolyhedralEnvelopeTests.Guarantee_AcceptedSegmentsWithinEpsilon` | 1 | 5.5 | 5 | T | PolyhedralEnvelope guarantee | brute-force nearest triangle O(T) per sample | AABB tree or fewer segments |
| P3 | `RemesherDeterminismTests.RemesherPro_ParallelSmoothing_MatchesSequential` | 1 | 5.4 | 5 | T | RemesherPro | NoisySphere 120 ~170k tris, 4 runs | EdgeVertices ~50; clone |
| P3 | `MeshFidelityMetricsTests.Cancel_AtAnyPoint_ClearsAllOutputs` | 1 | 5.1 | 5 | T | MeshFidelityMetrics | one Compute per cancel point at 50k samples | 5k / 1k samples |
| P4 | `TerrainGridTinTests.Compute_Twice_AndRepeated_GiveTheSameResult` | 1 | 9.9 | 10 | K/T | terrain TIN | 65x65 grid, 5 Computes (alone: 4 s total, <1 s each) | Not a library problem (4 s alone); 2 new instances instead of 3 |
| P4 | `MeshFidelityMetricsTests.Determinism_ParallelEqualsSerial_RepeatedRuns` | 1 | 7.1 | 7 | K (T) | MeshFidelityMetrics | Pair ~1k tris, 6 Computes at default samples | Fewer samples (10k/20k), 2 repeats; keep serial-vs-parallel |
| P4 | `SafeRoundingDegenerateInputTests.DegenerateInput_InBooleans_CleanWithinTheBound` | 1 | 6.3 | 6 | K | ExactMeshBoolean + SafeRounding | 198 cases x 2 booleans | Keep; split by family |

## Result of the library profiles (perf session, 2026-11-10)

Class F items of the triage, profiled on master (warm, 4 cores; dotnet-trace, wait artifacts filtered out). Numbers and
benchmarks are in the PRs: #519 (Remesher), #520 (solidify octree), #548 / #557 / #576 (NotCleanReason), and
`benchmarks/{RemeshPreventBenchmark,SolidifyBenchmark,NotCleanBenchmark}/README.md`.

| item | finding | outcome |
|---|---|---|
| `Remesher.PreventSelfIntersections` on the thin tube | the triangle grid cell was the target edge length, hundreds of triangles per cell: `TriangleHashGrid.Query` 60 % of the CPU time | #519: cell at most 2x the mean edge, same result bit for bit, 3.6 -> 2.2 s warm (cold 4.6 -> 3.3 s): `ThinTube_WithPreventSelfIntersections_Clean` is below 5 s |
| `MeshWindingNumberSolidify`, thin plates, res 64 | flat profile: contour 3.5 s over six phases, reduce 0.7, cleanup 0.3, 2 GB allocated; the winding number is not the cost (20k evaluations) | #520 (octree triangle lists): the fine octree phase 579 -> 333 ms, the whole solidify only 3-5 %. The solidify tests stay slow (about 4 s per Compute); the 6-Compute determinism test needs fewer Computes |
| `MeshAlphaWrap` + `SimplifyWithin` on the plates | wrap 6 s (`refine_and_carve`: 2.4 M gate pops at about 1.8 us, flat), simplify 2 s (already profiled in `docs/roadmap/alpha-wrap-and-repair.md`) | no cheap lever; the pipeline tests share one wrap run instead |
| `NotCleanReason` on 185-200k triangle meshes (coordinator's request) | self-intersection search 55-70 % (exact predicate for every box-overlapping pair, one tree traversal per triangle), cheap checks about 25 % on one thread | #548 pair filter, #557 box-tree self-join, #576 parallel bowtie / degenerate checks: 541 -> 240 ms (wrap), 319 -> 144 ms (sphere), same pair sets |
| `MeshFidelityMetrics` at the defaults (50k / 100k samples) | 81 % winding-number classification of the volume samples; 0.8 s parallel, 2.6 s serial for 5.3k + 2.7k triangles | no lever above 20 % (a cache of the mesh winding for the second IoU: at most 10-15 %); the samples are the accuracy knob, so the tests lower them |
| `Reducer.PreventSelfIntersections` on the thin slab | check 82 % of the reduction (grid query + candidates 62-70 %, projection filter 36 %); already profiled and tuned (grid cell 3x mean edge) | no lever above 20 % with the existing building blocks |
| `MeshRoundingRepair` (exact code) | plain run at most 0.7 s on the incremental-test cases; check mode (a test mode that recomputes and compares) is about 3x | no library lever; the slow rows are check-mode overhead |
| QualityMesh2 refiner, 4-point hull | refiner 255 ms warm (31k triangles), `Oracle.Check` 1.4-1.9 s | no library lever; the time is the test oracle |
