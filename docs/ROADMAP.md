# Roadmap

Open topics at a glance: [OPEN_TOPICS.md](OPEN_TOPICS.md) (one line per open item; this page and roadmap/ hold the history and details).

## How to use this file

- This index holds status only: the direction, what runs now, what waits on the owner, what is queued, and what is
  done, one to three lines per item with a link.
- Details, numbers and history live in the subpages under [roadmap/](roadmap/). Each subpage starts with a short
  header, then "Open" (what is still open, with owner decisions and dates), then "Done / history" (the record).
- Decisions are recorded with their date and "owner", eg "owner 2026-11-07".
- When an item changes status, update its line here and its subpage in the same PR. Keep status text in the PR,
  not in the roadmap (see [Session limits](#session-limits)).

Subpages: [exact-boolean.md](roadmap/exact-boolean.md), [meshing2d.md](roadmap/meshing2d.md),
[terrain.md](roadmap/terrain.md), [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md),
[parameterization-geodesics.md](roadmap/parameterization-geodesics.md), [platform.md](roadmap/platform.md),
[sources-surveys.md](roadmap/sources-surveys.md), [research.md](roadmap/research.md),
[hotspots.md](roadmap/hotspots.md), [history.md](roadmap/history.md).

## Direction

geometry3Sharp is the only broad, permissively licensed (Boost) geometry library for .NET. C# is the language
of Unity, Rhino/Grasshopper, Revit and many desktop apps, and none of them has an equivalent of libigl,
CGAL or geometry-central.

The goal of this fork: a **geometry platform that coding agents (and people) can build domain apps on**,
rather than an app. That puts some things first:

1. **Robustness over breadth.** Exact predicates, deterministic results, clear result codes instead of
   silent failures or exceptions. Every change comes with tests; regressions are measured (eg the
   Thingi10K repair benchmark).
2. **A discoverable API.** XML docs on public classes, small examples per operation, consistent patterns
   (options object, `Compute()`, result with status).
3. **A headless layer.** A CLI / MCP server that exposes the operations on mesh files
   (repair, boolean, remesh, measure, convert).
4. **Modern build, wide reach.** net10.0 plus netstandard2.0 (Unity, .NET Framework), CI. No public package is
   planned (owner 2026-11-16).
5. **Licenses.** Keep the Boost license. Port only from MIT / Apache / BSD sources (eg Manifold,
   geometry-central), never from GPL code (CGAL, parts of TetWild). Sources are listed in [SOURCES.md](SOURCES.md).

**Depth over breadth (owner 2026-11-10):** round off the existing features first; a new topic only with a synergy
with an existing one. Must-win hard problems (stronger-model investigations): scale-free robust repair, rounding cost
on large results (with the 3D snap-rounding leads), a proof method for determinism
([research.md](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)).

Relation to upstream: stay API compatible with 1.0.324, which existing users depend on. Upstream's
`dotnet8` branch has breaking changes (it removes the float types); useful parts (eg USD) can be taken over
selectively.

## Now
Status on 2026-11-18 (afternoon, Berlin), after the merges up to #1127 (the docs freshness audit was #1079-#1083; stale items corrected 2026-11-19). Each subpage ends with an "Open for
the future" list, ordered by priority; this section names only what is in flight.

- **Exact Boolean 3.0: complete (2026-11-18).** Every touched mutation shard has a full run above its threshold on master, and no
  mutated file changed after its run: arrangement-1..4, boolean-1/3..7 and rounding-1..12 in run 44217581261 (3ee7afea);
  rounding-13 (tests of #1125) and boolean-2 in 44295650298 (739e0458); terrain-overlay in 44267014937 (5b99848d);
  exact-arithmetic, imprint, planar and hausdorff in 44313345652 (822ac229); envelope-3, terrain-arith and terrain-kernel in
  44136162368; geodesic-1/2 in 44104242651; uv-* in 44092331961; the other envelope shards, fixed and generated in 44013112256.
  The boolean CsCheck families and fuzz targets of #1038 are merged (#1120). Exact booleans are now in maintenance mode (owner 2026-11-16);
  the scope freeze of the exact code (tests only, 2026-11-18) ends with this status.
  [exact-boolean.md](roadmap/exact-boolean.md#after-30-owner-2026-11-16)
- **Merged on 2026-11-18:** `SolidBooleanPipeline.PairContactsMode` Off / On / Auto (#1122; Auto keeps every clean result bit for bit,
  its second pass costs 25-50 % of the pipeline time on the contact cases, so the default stays Off per the owner's 25 % bound);
  the alpha wrap estimate counts open input twice, so a budget means the same on open input (#1123, #1113 options 2+3, owner
  2026-11-18; computed only with a budget, #1127); the changed-line coverage gate has tests and five reviewed allow-list
  entries (#1124, owner label 2026-11-18); #1118 (#1121), #1074 (#1126) and #1102 (#1125) are fixed in tests. #1096 and
  #1104 are closed: answered on master by class E option (a) (#1114), the other classes are documented behaviour pinned by tests.
- **Exact Boolean 4.0** (owner 2026-11-17): stage 0, the design analysis of exact output with provenance (chained
  operations), is merged (#1093; verdict: stage 1 only, no materialized provenance). Stage 1 goes ahead as option A, an opt-in
  API (a lazy `ExactMesh` expression with one exact pass, merged #1107); stages 1b and 1c are on hold (owner 2026-11-18).
  [exact-boolean.md](roadmap/exact-boolean.md#open-for-the-future-2026-11-15)
- **Boolean result classes** (owner 2026-11-17): issue #1104 with procedural cases of the solid workflow (PR #1105, merged: pins
  the classes and documents validating boolean results; the issue is closed, 2026-11-18). Class E: option (a), an additive final-result volume IoU diagnostic in
  `MeshRobustRepair` (`ResultVolumeIoU`, opt-in, `Apply()` unchanged), done with the answer to #1096 (merged #1114, #1096 closed; [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md)). The opt-in helper
  for "exact boolean on triangle soups -> clean closed low-poly solid" is `SolidBooleanPipeline` (#1112, below).
- **Boolean robustness round** (owner 2026-11-16/17): RC1 #1027, RC2 #1021 / #1063, RC3 #1031 and RC4a #1040 are merged;
  the pass-through of RC4a's budget (#1068) is merged; RC4b is a documented limit; no budget raise below D = 50000;
  #1067 (p0008: an untouched zero-volume sheet survived the component removal; #1152, #1170, owner decision 2026-11-19) is fixed in the removal (merged #1178, #1067 closed 2026-11-19); sheets whose floating-point margin cannot decide (iud p0029, dui p0019/21/22) are decided exactly for the computed area (#1191, [EXACT_VOLUME_SIGN.md](design/EXACT_VOLUME_SIGN.md)); open: the owner's local campaigns (#1038). The certifying checker is merged (#1061).
  [BOOLEAN_TRIAGE_2026-11.md](research/BOOLEAN_TRIAGE_2026-11.md)
- **Solid boolean pipeline** (owner 2026-11-17): the opt-in `SolidBooleanPipeline` (exact boolean of soups with `AllowOpenMeshes = false`
  and `StrictRounding = true`, then a guarded reduction, a final `NotCleanReason` and a report) is merged (#1112); 26 of 26 reduced
  results without contacts are clean, contacts limit how far a reduction goes (a contact line of m vertices holds about 12 m
  triangles); the opt-in `PairContacts` (`Reducer.PairCoincidentVertices`) collapses the twin edges of a contact line together
  (two edge-touching boxes: 8 triangles instead of 48-144). [exact-boolean.md](roadmap/exact-boolean.md#other-open-items), [benchmark](../benchmarks/SolidBooleanBenchmark/README.md)
- **Exact imprint / deprecation:** done (owner 2026-11-17): `Obsolete` on the float `MeshMeshCut` / `MeshBoolean` (#1037 merged).
- **Tet meshing:** defaults on (#1065); the production study is merged (#1071, stages P1-P5). P1 (owner 2026-11-17):
  default alpha diag/40 as a documented exception to the 17.5 rule (494 of 500 sample meshes with every tet >= 10 deg,
  1.30x time), merged (#1073); the 6 meshes below went to the lever #1072 (closed: the rescue phase, on by default, lifts 4 of them). P2 (output / API) is merged. P3 (interior size control,
  owner 2026-11-19: `TargetEdgeLength`, `SizeFunction`, `GradingFactor`, opt-in, default output unchanged; TET_MESHING.md 17.12) is merged (#1155); boundary fidelity is P4: (a) the local alpha (opt-in `MaxSurfaceDeviation`, owner 2026-11-19, TET_MESHING.md 17.13: the fidelity of uniform /80 at about half its tets) is built, (b) S1-F creases (opt-in `CreaseAngleDegrees`, TET_MESHING.md 17.14: concave creases 3 to 6 times closer on CAD solids, a small gain on scanned meshes, fallback to S1-R when the recovery does not finish) is built, (c) option (b) is open.
- **Remeshers:** the Phase 2 study is merged (#1075); an opt-in safe preset (quality 0.2 + `PreventSelfIntersections`,
  defaults unchanged, owner 2026-11-17) is merged (#1085, `Remesher.ApplySafePreset`, `g3cli remesh --safe`); the inversion guard study #1070 closed hard problem 14 (options
  parked).
- **Quad remeshing** (study #1078, owner 2026-11-17): stage 5 (`DPolyMesh` with OBJ / PLY writers and readers, #1097) and stage 1 (`CrossField`, `MeshPrincipalCurvature`, #1098, merged with the speed gate open) are merged; the speed
  gate is then met on the sphere and torus by the Hermitian n x n form (#1099, done) and the parallel loops; the owner's next lever is a
  parallel `SparseCholesky` with an upper-bound check first, bit for bit, existing callers unchanged (#1103, bit-identical vector
  kernels, merged; more in progress). Stage 2 (`PositionField`, `QuadRemesher`, `PolyMeshMetrics`, #1100) merged as quad-dominant with the
  missed gates (irregular vertices 6 to 12 x the singularities, quads below 95 % on 4 of 8 shapes) recorded open; the irregular gate is restated
  relative to the curl of the field (owner 2026-11-19, [QUAD_REMESHING.md 4.5](design/QUAD_REMESHING.md)) and an opt-in size field (`SizeAdaptation`) halves
  the irregular vertices of closed smooth shapes; stage 3B (opt-in `QuadRemesher.PureQuads`, the
  integer-map repair; fallback to the stage 2 mesh with a `PureQuadStatus`, #1153) is merged. Follow-up stage 3B improvements (geometric sign
  reference, cost-guided fixed-edge balancing, and `SizeAdaptation` support) lift `QuadFlow3BGate` from 25/54 to 43/54 uniform and 48/54 with
  `ScaleAndRotation` (0 undetected bad). `g3cli quad` and its MCP tool `quad` (OBJ / PLY output, `--pure-quads`,
  fallback reported in the result, library defaults kept; the edge length has no library default, the CLI takes the larger of 1/30 of the diagonal and twice the mean edge) are done.
  [QUAD_REMESHING.md](design/QUAD_REMESHING.md)
- **Repair:** FinishHoles coverage merged (#1069); `PullPinchPoints` stays opt-in (#1045, owner 2026-11-17); the
  scale-free repair study (hard problem 9, #1095) is merged: no-go for a larger effort. Owner 2026-11-17: F1 go, done (#1101,
  `MeshRobustRepair` reports a lost separable part: `LostFarPart`, new `LostFarOpenPart`, `LostSeparatePart`); F2 not now.
- **Alpha Wrap 3.0:** done. Lever 6 K stays opt-in (#736), the raw-size budget stays off with the opt-in
  `MaxAlphaCoarsening` (#1014) (owner 2026-11-17). Done (owner 2026-11-17): an opt-in target triangle count that keeps the
  guarantees, defaults unchanged, the budget default stays off (merged #1106). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15)
- **Dual contouring:** exact Hermite data and octree simplification on by default (#991, #995, #1003); the
  `SymmetricEigenSolver` allocation fix is merged (3a256c7b).
- **Determinism:** stage 1 (the exact building blocks as a closed world) is merged (#1076); O1 done (owner 2026-11-17, #1092:
  the `SnapRounding2.Diagnostics` text is culture-invariant); the guard-limit int overflow in `ExactTriangulation2` is fixed (#1086).
- **Platform and CI:** #999 (xatlas report) merged; the UV parameterization mutation shards are merged (#1006); the
  uncovered lines of the nightly changed-line coverage gate got tests (#1089; the next nightly run shows whether it is green); the flaky
  allocation test #1074 now explains its own failure (#1090); the gate-change check (#1087, observe mode) and the diff triggers of the
  review checklist (#1088, `tools/review_triggers.py`) are merged; #1009: outer face rounds incremental by default since 2026-11-19, FastWindingNumber 21 to 34 % faster with the same bits; known limit (owner 2026-11-19, option C): `fm_ms` of Thingi10K 498456 stays above 20 s (29.6 s, [README](../benchmarks/MeshRepairBenchmark/README.md#faster-winding-queries-same-bits-1009-tail)); tests above 20 s in full runs only (#698) open; robustness sweep remainders R13, R15
  (extreme field values) partly fixed, S10 deferred ([ROBUSTNESS_SWEEP.md](ROBUSTNESS_SWEEP.md)). Late cron runs of `mutation.yml` stay as they are (owner 2026-11-17).
  [platform.md](roadmap/platform.md#open-for-the-future-2026-11-15)
- **Clipper2:** done (#1059 merged: the input-boundary allowance only for empty outputs, #955 closed).
- **Parameterization:** spectral conformal parameterization opt-in (#1057); RDR stage 1b not now (owner 2026-11-17).
- **Not now (owner 2026-11-17):** a native (NativeAOT) package (spike #1052); terrain work (the plan: slope envelope, 3D blocks, berms, GeoTIFF,
  showcase) "later"; internal quadtree uses (`TerrainSafety`, `TerrainVolumeDifference`) wait for terrain.
- **Owner's local runs:** #929 (net48 vs net10), #1038 open.

## Open decisions (owner)

Checked on 2026-11-17 (night).

- **Branch deletion (decided, owner action pending):** the coordinator lists the candidates (branches whose PRs are merged
  or closed, plus `claude/int256-spike`, `claude/collinear-shortcut` and `claude/geogram-boolean-comparison-wip`), and the
  owner deletes them (owner 2026-11-17); sessions cannot delete branches; `runs/*` branches hold run data and stay.
  [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- **#1064 levers** (#1064 is merged; if the 20 s rule needs more on 42327: union max 20.02 s in 1 of 10 processes): the centroid without
  `AddPoint` / per-region stacks (~1.5 s) and an exact `PlaneSide` kernel; not built under the speed stop (#1047 dropped as a documented tail, owner 2026-11-19).

Decided on 2026-11-17 (owner): split pinches stay "not clean" in `NotCleanReason` (only the benchmark reports them as
their own class); the cleanup never empties a non-empty result (RC1); the inside-out check gets an exact volume sign
(RC3); #846 stays off; #986 option (A); `Obsolete` on `MeshMeshCut` / `MeshBoolean`; tet P1 default diag/40; the
terrain-overlay break 75 with honest timeouts; late cron runs kept; Boolean 4.0 stage 0 started; no diagnosis of the
step-3 lead; hard problem 4: lever 6 K stays opt-in (#736; a provably valid feature recovery stays open in research.md), hard problem 8 is fixed (#528), only speed remains there; terrain: the slope envelope construction and 3D blocks are later too
(with berms, GeoTIFF and the showcase); `SnapRounding2.Diagnostics` text is culture-invariant (O1, done, #1092); tet 16.2: the boundary
bound is restated against the peeled surface (no code change, P1 494/500 kept); the gate-change check (#1087, observe mode) and its label (#1094): the coordinator labels a change that keeps or tightens a check after reviewing the gate-file diff, the owner one that loosens a check or changes the gate check or the rule files, workers never set it, and a push after the label needs a fresh review ([QA.md](QA.md) 2.8a);
MeshRobustRepair F1 go (done, #1101), F2 not now; Exact Boolean 4.0 stage 1 as option A, an opt-in API (merged #1107; 1b and 1c on hold, owner 2026-11-18); quad remeshing: stage 1 merged with the speed gate open (#1098), then a parallel `SparseCholesky` with an upper-bound check first (bit for bit, existing callers unchanged; #1103), the Hermitian form (#1099, done), stage 2 merged as quad-dominant with the missed gates open (#1100), stage 3B merged as opt-in (#1153), its follow-ups wait for the papers (owner 2026-11-18: unreachable); alpha wrap: an opt-in target triangle count keeping the guarantees, defaults unchanged, budget default off (merged #1106); boolean result classes: issue #1104 with procedural cases (PR #1105), class E option (a), an additive final-result volume IoU diagnostic in `MeshRobustRepair` with `Apply()` unchanged, with #1096 after the wrap target-count work; an opt-in helper for "exact boolean on triangle soups -> clean closed low-poly solid" (merged, #1112).

Standing rules (not pending): a Phase 5 item that is not small goes to the owner first (owner 2026-11-03); each port
from geogram (BSD-3, may be read and ported, owner 2026-11-03) or another permissive library is a decision with an A/B
([sources-surveys.md](roadmap/sources-surveys.md#open)).

## Next / queued

In order:
- Tet P2 (output / API): merged (TET_MESHING.md 17.9); #1073 is merged.
- After 3.0 (complete 2026-11-18): the thingi-pairs baseline and final runs are done (2026-11-19; no outcome regression, p0988 intersection +54 % (3961 -> 6097 ms), the final run built four master commits, [results](../benchmarks/BooleanComparison/README.md#results-of-the-two-full-runs-2026-11-18--2026-11-19)); #1047 (one Thingi10K pair at 18-26 s per operation, in the classification) and #1158 (one Thingi10K size pair at 21-35 s per operation, in `assembly.simplify`): a measured diagnosis first; #1158 closed (#1183), #1047 dropped as a documented tail (owner 2026-11-19, the issue stays open).

## Order after Exact Boolean 3.0 (owner 2026-11-15)

1. **Finish what runs.** Exact Boolean 3.0: complete on 2026-11-18 (#962, #967, #1002, T6
   closed in #1013; the completion gate's runs are listed under "Now"). Alpha Wrap 3.0 is done (the
   adaptive-alpha spike is a no-go, #924). Clipper2 is done (#914, #933, #938, #949, #1059; #955 closed).
2. **In parallel:** the contouring topology certificate, option A: stages 1-3 merged (#779, #925, #935, #946;
   certified runs 4 -> 10 of 14 at / 128, never wrong); refinement for thin features deferred (#944).
3. **Rounding** (hard problems 1 and 3): the plateau variant of the position search is a no-go
   (owner 2026-11-15, no spike; #721 closed, it stays a design).
4. **Scale-free robust repair** (hard problem 9): the study is merged (#1095): no-go for a larger effort; F1 done (#1101), F2 not now (owner 2026-11-17).
5. **Tet meshing:** stages 1 and 2 merged, defaults on (#1065), production plan P1-P5 (#1071); P1 merged (#1073), P2, P3 (size control, 17.12) built. Sharp
   features (hard problem 4): lever 6 K (#736, opt-in); a provably valid feature recovery stays open.

Alongside, without dependencies: the determinism proof (hard problem 11: the analyzer #906, then the net48 / net10
measurement; stage 1, the exact building blocks, merged in #1076: DETERMINISM_PROOF.md 5.4); a quality-aware manifold step in the alpha wrap (diagnosis in #919, closed as valid
output). Last, by "depth over breadth" (owner 2026-11-10):
CCD (no caller in the library), point clouds, deformation, `ContourLoft`.

## Later / research

- Exact booleans after 3.0 (owner 2026-11-16): maintenance mode; the exact imprint is the only new
  feature; everything else is parked or dropped, see the triage in [exact-boolean.md](roadmap/exact-boolean.md#after-30-owner-2026-11-16).
- Meshing2D research: Shewchuk's Terminator with a region-based oracle excuse (owner 2026-11-06, option c: not built),
  matched shells, the grading placement strategy, parked until a use case; the <= 2x fork speed target is closed (owner
  2026-11-16). [meshing2d.md](roadmap/meshing2d.md#open)
- Remeshers: Reducer / Remesher 2.0 and its follow-ups are closed (owner 2026-11-12); the Phase 2 study (#1075) and the
  opt-in safe preset followed (2026-11-17); thin walls (walls folding, 135363) stay open. [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#open)
- Later list (point clouds, offsets, quad remeshing, deformation, tetrahedral meshing,
  TPMS, sparse SDF grids, batch ray queries, AI bridges): [research.md](roadmap/research.md#later)
- Phase 5 candidates from recent research: [research.md](roadmap/research.md#phase-5-candidates-from-recent-research)
- Open research questions (3D snap rounding, envelope pipeline, metamorphic testing, determinism, and more):
  [research.md](roadmap/research.md#open-research-questions)
- Intersection-free dual contouring (owner 2026-11-10: moved up, ahead of collision detection and tet meshing; options A
  and B merged in #552 and #572):
  [research.md](roadmap/research.md#open)
- Adaptive simplification of dual contouring (owner 2026-11-16: `DualContouring.Simplify`, default on with opt-out; `AdaptiveDualContouring`,
  #995 and the default-on PR): [design/DC_ADAPTIVE_SIMPLIFICATION.md](design/DC_ADAPTIVE_SIMPLIFICATION.md). Open idea, opt-in, not scheduled: a
  coarse-first construction (skip the fine cells of flat regions, no dense grid) needs bounds on the implicit (a Lipschitz constant or interval
  evaluation); measured upper bound of the sparse grid alone: 8-15 % of the time.
- Hard open problems, candidates for a selective investigation with the stronger Fable model (owner 2026-11-09):
  [research.md](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)
- SMT checks with Z3 (pilot 1 merged in #400; pilot 2 steps (1) and (2) merged in #416 and #407): [research.md](roadmap/research.md#smt-checks-with-z3)
- Mesh-kernel ideas and EMBER (EMBER / octree-BSP architecture shelved, owner 2026-11-17): [exact-boolean.md](roadmap/exact-boolean.md#external-boolean-corpora-2026-11-04); re-examined 2026-11-19 in [PLANE_BASED_KERNEL_STUDY_2026-11.md](research/PLANE_BASED_KERNEL_STUDY_2026-11.md) (no-go, owner 2026-11-19)
- Geogram and other permissive libraries: [sources-surveys.md](roadmap/sources-surveys.md)
- RWTH Aachen papers and award-winning papers: [sources-surveys.md](roadmap/sources-surveys.md)
- Shared hotspots and leads for open tasks: [hotspots.md](roadmap/hotspots.md)

## Done

- Phase 1, foundation (targets, CI, package metadata, formats, API docs). [history.md](roadmap/history.md#phase-1-foundation)
- Takeover of the abandoned work, stages 0 to 3 (2026-10-31). [history.md](roadmap/history.md#stages-0-to-3)
- Phase 2, exact booleans: arrangement, winding numbers, n-ary, SafeRounding output. [exact-boolean.md](roadmap/exact-boolean.md#phase-2-items-on-exact-booleans)
- Milestone "ExactMeshBoolean 1.0" (2026-11-01: #223, #244, #248, #249, #251, #253). [exact-boolean.md](roadmap/exact-boolean.md#stage-r-saferounding)
- Milestone "Exact Boolean 2.0" (2026-11-11); D1 (#513) counted as Exact Boolean 2.1 (owner 2026-11-12). [exact-boolean.md](roadmap/exact-boolean.md#exact-boolean-20-milestone)
- Milestones "Alpha Wrap 2.0" (owner 2026-11-09) and "Reducer / Remesher 2.0" (2026-11-09).
  [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#alpha-wrap-20-milestone)
- Snap-rounding experiment measured and closed (#313, 2026-11-02). [exact-boolean.md](roadmap/exact-boolean.md#stage-r-saferounding)
- Envelope containment stopped at its stop criterion (owner 2026-11-01; #263, #268). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#stage-7-envelope-containment)
- Alpha wrap quality, stage 7b (#292, 2026-11-02); raw-wrap fixes (#311, #317); wrap resolution (#280); volume
  check (#305). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#stage-7b-alpha-wrap-quality)
- Phase 3, parameterization and geodesics. [parameterization-geodesics.md](roadmap/parameterization-geodesics.md#done--history)
- Phase 4, CLI / MCP, analysis building blocks, evaluation metrics; CLI / MCP docs for the new operations (2026-11-02). [platform.md](roadmap/platform.md#phase-4-items-cli--mcp-analysis-building-blocks-evaluation-metrics)
- Boolean property tests, DAP audit (nothing to adopt), Clipper2 adapter (#243). [platform.md](roadmap/platform.md#stage-8-planned-follow-ups)
- Issue-tracker fix list complete (2026-11-03). [platform.md](roadmap/platform.md#quality-bar-when-its-done)
- Meshing2D M2 (#233), M3 (#252), `m2.snap` (#277), provenance check 6b (#295, #296), Q17 (#358), fast oracle (#376,
  #379), grading (#375, #380), run A (#390), small-angle rule (#394). [meshing2d.md](roadmap/meshing2d.md#done--history)
- Quadtree (#195, #266): owner-machine side-by-side comparison done (2026-11-08): always faster than or as fast as
  the old quadtree. [terrain.md](roadmap/terrain.md#stage-5-quadtree)
- Terrain phase 1 (#357, 2026-11-03). [terrain.md](roadmap/terrain.md#terrain-session-record-2026-11-01-to-2026-11-03)
- Phase 5: geodesic paths and loops (#1), foldover-free maps (#7, #374); inside / outside for open inputs stopped
  (#331); the affine heat method stopped at its gate (#371). [research.md](roadmap/research.md#phase-5-candidates-from-recent-research)
- Test suite on German Windows (#383). [platform.md](roadmap/platform.md#test-suite-on-german-windows)
- November 2026 (2026-11-12 to 2026-11-15): CLI / MCP update (#707), Clipper2 vendored (#751, #851, #874), FPTaylor T12
  (#627), runtime watch (#726), determinism stage 0 (#871), monomial fallback (#769), CCD (#778, #793, #872), tet
  stages 1-2 (#775, #821), TPMS (#838, #879), `MeshOffset` (#799). [history.md](roadmap/history.md)

## Session limits

- At most 5 implementation sessions at once in the cloud, of which at most 2 large (tier L; [AGENT_WORKFLOW.md](AGENT_WORKFLOW.md)
  section 6), plus **one review session** that only reviews (independent reviews with REVIEW_CHECKLIST.md, bounds
  reviews, re-running numbers) and writes no production code. The owner's local work does not count. The limit
  applies to new starts: when sessions finish, they are not replaced until the count is below it. Each session has
  its own worktree and one PR-sized topic.
- **Exception (owner 2026-11-12):** three extra medium Sonnet sessions (CLI / MCP update, test-time backlog,
  short pipeline topics: fast-winding cache lever 3, DualContouring allocations, the
  issue-tracker follow-ups (flip-guard and P1, both closed), then the parked `.poly` reader / mesh2d area items after the CLI queue) on top of the limit;
  no further starts until the count is back below it.
- **Suspended (owner 2026-11-12, after the weekly reset):** the limit above is out of force until the owner sets it
  again; the coordinator still starts one PR-sized topic per session and keeps parallel sessions off the same files.
- The bottleneck is review, serial merges and the mutation runners (a full shard takes hours), not sessions: a
  session whose PR waits for review does not start the next PR-sized topic in a mutated file.
- Parallel sessions must not change the same production files. Quadtree (`spatial/`) and meshing (`comp_geom/`) are
  disjoint in code; they ran at the same time because the owner worked on the quadtree locally and the review session
  took the meshing reviews.
- Merges are serial, because many PRs touch `docs/ROADMAP.md`, `docs/QA.md`, `docs/SOURCES.md`, `docs/OPERATIONS.md`
  and `ExactBooleanIssueCasesTests.cs`. Rebase the next PR and re-run CI after each merge. Keep status text in the
  PR, not in the roadmap.
- Full local CI and every timing or memory measurement run alone on the machine: the sweep's time limits react to
  load, and measurements taken next to other work are not comparable.
