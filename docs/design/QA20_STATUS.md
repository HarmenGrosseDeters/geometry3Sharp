# QA 2.0: status against the done-when (step 0 of the rollout)

Status: docs only, 2026-11-12, state of master at `c013f31`. Evidence is from files, `git log`, the roadmap and
`tools/MeshFuzzPilot/FINDINGS.md`; nothing here was re-run (no SDK in the session), so a finding marked "open" is open
according to those documents, not re-measured. Part of the [QA 2.0 milestone](../roadmap/platform.md#qa-20-milestone);
rollout order in [QA20_INVENTORY.md](QA20_INVENTORY.md) section 5.

## 1. Status T1-T15

done = the task's done-when is met; partly = built, a named part is missing; open = nothing yet.

| # | Task | Status | Evidence / what is missing |
|---|---|---|---|
| T1 | Sweep table extension | done | `RobustnessSweepTests.GapOps.cs`, `.Families.cs` (R24-R35, defect and extreme-scale families), ROBUSTNESS_SWEEP.md |
| T2 | Mesh-ops fuzz harness on master | partly | `tools/MeshFuzzPilot` on master (17 targets, replay and self-test in CI); findings #451, #452, #493, #495, #586. Missing: the >= 2 h runner campaign per target (the T13 run was 10 min per target; local overnight runs are issue #624) |
| T3 | CsCheck repair / rounding | done | `RepairCampaignTests.CsCheck.cs`, `RepairCampaignFindings.cs` (F1-F3), `RepairCampaignFarPartExamples.CsCheck.cs` |
| T4 | SharpFuzz readers | done | `tools/ReaderFuzzPilot` (`ReaderTargets.cs`, `ReaderTargetsMcp.cs`), #563; campaigns #623 / #632 finished, no find |
| T5 | CsCheck 2D mesher vs oracle | done | `QualityMesh2PropertyCampaign.CsCheck.cs`, findings #496, #499, #512, #534 |
| T6 | CsCheck alpha wrap | done | `AlphaWrapCampaignTests.CsCheck.cs`; the flat-input finding is fixed |
| T7 | CsCheck remesher / reducer | done | `RemeshReduceCampaignTests.CsCheck.cs`; one open Reducer finding (section 2, F7) |
| T8 | Changed-line coverage gate | partly | `coverage.yml`, `tools/changed_line_coverage.py`; the nightly cron has not fired yet (section 4); "findings worked off" is not checked here |
| T9 | Stryker: `DMeshAABBTree3`, readers | open | no shard config for either in `tests/mutation`; adding one touches mutation config (CI-speed session owns it) |
| T10 | CsCheck Clipper2 and Z3 frame lemma | partly | `Clipper2CampaignTests.CsCheck.cs` done; no Z3 lemma for `ClipperFrame` in `tools/Z3Pilot` |
| T11 | CsCheck MarchingCubes / terrain | done | `MarchingCubesCampaignTests.CsCheck.cs`, `TerrainSlopeCampaignTests.CsCheck.cs`; two terrain findings open (F9) |
| T12 | FPTaylor hand-derived filters | partly | `tools/FPTaylorPilot` has `Orient3dLPI` ux and nx; PR #627 (draft) and issue #625 for the rest; `ExactPredicates`, `ExactTriangleIntersection` open |
| T13 | Fuzz second wave | partly | targets done (remesher, reducer, quality-mesh2, clipper2, plus alpha wrap, tree-*); campaigns 10 min each, ranked in FINDINGS.md; long runs #624 |
| T14 | Z3 termination lemmas | open | no termination lemma in `tools/Z3Pilot` (`fix_degenerate`, median split, expansion sums) |
| T15 | Stryker terrain / repair result checks / mesher | open | depends on the test-time backlog program |

Done-when clauses (platform.md): (1) fuzz harness per reader and per untrusted-geometry operation: partly (readers,
Boolean, repair, rounding, trees, remesher, reducer, mesher, Clipper2, alpha wrap have targets; no target for
MarchingCubes / terrain / `MeshMeshCut` and the upstream editing operations; long runs missing); (2) FPTaylor bound per
filter and Z3 lemma per filter or termination rule: open (T12, T14); (3) property campaign and mutation shard per
robustness-critical module: campaigns done for repair, mesher, alpha wrap, remesher / reducer, MarchingCubes, terrain,
Clipper2, Boolean; shards exist for the exact code only; (4) coverage gate nightly: partly, the cron has not fired yet.

## 2. Open findings without a bugfix issue

Only #628 (PlanarComplex nesting, PR #634) and #630 carry the `bugfix` label; every other finding was fixed in a
PR or sits in FINDINGS.md / the roadmap. Classification under [QA.md 2.5a](../QA.md#25a-supported-input-range-correct-inside-fail-gracefully-outside-owner-2026-11-09):
**in** = bug, **out** = only crash, hang or false success is fixed.

First, the three examples named by the coordinator: the `DMeshAABBTree3` ray / `IsInside` failure at radius 1e100 is
**fixed** (#493, range up to 2^1000 in QA.md); the alpha wrap "result box corners" failure at relative alpha 5-8 is
**fixed** (platform.md campaign item 3; the extent-2e-15 case is an honest `Failed` with `InvalidInput`, #527);
`MeshRobustRepair` slow inputs are still **open** (F2).

| # | Finding | Source | Class | Verdict |
|---|---|---|---|---|
| F1 | `MeshRoundingRepair` / Boolean `RoundingClean` claims clean but is not (degenerate triangles, invalid mesh, inside-out); rank 1 and 2, 41 + 56 artifacts | FINDINGS.md T13 | the replay inputs are scale < 2^-200 or > 2^200: **out**, false success | range check in `MeshRoundingRepair` was "held until #513"; #513 and #593 are merged, so re-replay the artifacts first, then a bugfix issue if one still gives a false success |
| F2 | `MeshRobustRepair` 2.7-10.8 s on 4-12 triangle inputs (wrap / solidify fallbacks) | platform.md, FINDINGS rank 8 | **in** (normal-range inputs) | performance bug: issue with the 62-byte repro; the 5 s test limit makes it binding for campaigns |
| F3 | `DualContouring.find_crossings` throws "ray direction is not normalized" (AggregateException) on finite input, found via robust-repair-soup | FINDINGS rank 3 | in (an exception on finite input; origin is a degenerate solidify step) | bug: issue with the 79-byte repro |
| F4 | `Clipper.Union` of two self-intersecting rings returns an area neither input fills (Clipper2 2.0.0) | FINDINGS item 5 | in, in the dependency | roadmap Open item (integrate the C# sources or document); not a library bug |
| F5 | `QualityMesh2` `AngleBelowBound` about 1 in 700 inputs in every quality mode | FINDINGS second wave 1 | in (extent about 1) | bug: issue; mesher is another session's area, so the issue needs the oracle check first |
| F6 | `QualityMesh2` `VertexUnused` / `SegmentMissing` with `Success` at 2^35 extents from the origin and with a coordinate 1e-119 beside extent 1 | FINDINGS second wave 2, 3 | in (documented up to 1e70): false success | bug: issue |
| F7 | `QualityMesh2` `Failed (RefinementLimit)` on a valid input: livelock for a point 1e-286 of the extent away | FINDINGS rank 7 | out (magnitude disparity), honest `Failed` but after 296 225 steps | range check or documented limit: issue only if the time is large |
| F8 | `Reducer` `PreventFoldOver` leaves a triangle slightly past vertical on noisy terrain | platform.md campaign item 5 | in | counted by the campaign (`G3_PROPERTY_STRICT=1`); issue with the 200-seed recipe |
| F9 | `TerrainSlopeDesign` `SlopeViolation` (a) closed toe with the slope on the inside, (b) `Direction.Auto` on a terrain tilting through the toe plane | platform.md campaign item 6 | in; safe failures (no wrong result) | conservative failures: issues, low severity |
| F10 | `PartitionedReducer` 2^k scale claim with components 1e-280 times the extent | FINDINGS rank 6 | out | documented limit (owner 2026-11-11); nothing to do |
| F11 | Clipper2 `Offset` result with an outer ring of area -1.7e-16 beside a 1e-12 ring | FINDINGS item 5 | out (frame resolution 1e-23) | not analysed; low |
| F12 | `MeshRobustRepair` hang at 1e176, alpha wrap false success at 1e-276 / 3e17 | FINDINGS 3, 4 | out | range checks merged (`OutsideRange`, `InvalidInput`); replay once to close |

## 3. Next items by ROI

Constraints applied: no exact files, no mutation tooling configs (T9, T15 wait for the CI-speed session), no Remesher /
Reducer / alpha wrap tests while their sessions run (T7, T6 follow-ups wait).

1. **Verify F1, F3, F5, F6, F12 on current master and open the bugfix issues** (replay of the FINDINGS repros with
   `MeshFuzzPilot`, no library change): cheapest, highest evidence; the issues are the deliverable.
2. **CsCheck / invariance for spatial structures** (inventory cell K): differential against brute force for
   `PointAABBTree3`, `MeshBatchQueries`, `WindingNumberOctree`, and a ray / `IsInside` invariance test at 2^-24 / 2^20 and
   up to the 2^1000 range; the structures are "thin" in the gap list and #451, #493, #495, #586 all came from here.
3. **T14 Z3 termination lemmas** (`DMeshAABBTree3` median split on adjacent doubles, `fix_degenerate` progress): small,
   tool-only, pins the #451 / #452 classes.
4. **Gap-list operations with thin tests** (`RegionOperator`, `VoxelSurfaceGenerator`, `ConvexHull2`,
   `QuadtreeExactQuery2d`, `MeshInsertPolygon`, `MeshTrimLoop`): sweep rows exist, a seeded property test each.
5. T10 Z3 frame lemma (2 A), T12 follow-up after #627.
6. Later, blocked: T9 / T15 (mutation configs), long runs of T2 / T13 (#624, runner).

## 4. Update after `c013f31` (2026-11-12)

What changed on GitHub after the state above (PR and issue states, not re-measured):

- Section 2: #628 is closed, fixed by #634 (merged, 2947179); #630 is closed, fixed by #632. New open bugfix issues:
  #636 (F2, `MeshRobustRepair` 9 s on a 6-vertex input), #637 (F5, `QualityMesh2` `AngleBelowBound`), #638 (F7,
  `QualityMesh2` `RefinementLimit` livelock), #644 and #645 (`ConvexHull2`), #647 (`VoxelSurfaceGenerator`).
- Section 3, open PRs: item 2, #639 (differential tests for `PointAABBTree3`, `MeshBatchQueries`,
  `WindingNumberOctree`); item 3, #641 (`DMeshAABBTree3` midpoint split) and #642 (`MeshRoundingRepair` step loops),
  T14; item 4, #646 (`ConvexHull2`, `QuadtreeExactQuery2d`) and #648 (`VoxelSurfaceGenerator`, `MeshInsertPolygon`,
  `RegionOperator`, `MeshTrimLoop`); item 5, #643 (T10 frame lemma); T12, #627 (draft).
- T8 and done-when (4) are "partly" (they were "done"): the `coverage.yml` cron has not fired yet (no scheduled run on
  2026-11-12), see [platform.md](../roadmap/platform.md#open).
