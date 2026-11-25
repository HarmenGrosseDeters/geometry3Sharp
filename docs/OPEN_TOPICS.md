# Open topics

One board of every open topic of geometry3Sharp, one line each, as of 2026-11-24 (Berlin). Open items only: done, closed and
rejected items are not listed. Every item in §§2–5 links to a draft plan PR (`plan/*`, holding `docs/plans/<slug>.md`,
not to be merged as is). Maintenance rule: a session that opens, finishes or changes the state of a topic updates its row
here in the same PR; history, numbers and reasons stay in [roadmap/](roadmap/) and the design notes.

## 1. In progress (a session is working on it)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Tet: deterministic work cap for `TetMeshImprover` | Part 1 merged (#1184); a split-only cap was measured and dropped (owner 2026-11-19: it leaves meshes above 20 s at floor 20) | Measure a bound over all improvement attempts or a pass budget; results to the owner before a PR | #1154 |
| Exact Boolean: winding near open edges is wrong; the on-surface value is the principal value (0.5 on a face, 0.25 on an edge, 0.125 at a corner) | PR open (#1514): accurate edge angle near an open edge | Review, CI, exact-code gates | #1514, [exact-boolean](roadmap/exact-boolean.md#board-update-2026-11-24-open-items-of-the-exact-boolean-and-the-solid-pipeline) |
| Exact Boolean: exact-winding shard survivors, perturbed-oracle tests | Draft PR open (tests only) | Kill the survivors, then the shard run; A1 near the plane of an edge is the owner's | #1500, #1427 |
| Exact Boolean: a second `ExactMeshArrangement.Compute()` throws after point merges | Draft PR open: the run state is reset before every later run | Review, CI, merge | #1518, #1512 |
| Exact Boolean: `ExactMeshArrangement` output point ids depend on the thread schedule, undocumented | Issue open | Document the contract or make the ids canonical | #1519 |
| Exact Boolean: certifying Booleans, step A (fast checker) | Draft PR open; step B in progress; options C and D parked; E after B plus a repair contract | Review and CI of A; B | #1520, [study](research/CERTIFYING_BOOLEANS_2026-11.md) |
| Terrain: slope envelope, canonical output and faster insertion (owner decision (b), 2026-11-24) | Draft PR open: stage 1 canonical rule, stage 2 planes per part and local merge | Review, CI | #1515 |
| Platform/QA/CI: `narrow_tests.py` duration lookup misses theory rows | Issue open: the estimated saving is too optimistic | Fix the lookup | #1517 |
| Platform/QA/CI: tiny `gParallel` loops run serially below a size threshold | Issue open (diagnosis in the slow-tests note) | Benchmark first, then the owner's policy | #1502, [note](research/SLOW_TESTS_2026-11.md) |

## 2. Waiting for an owner decision (options are on the table)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Exact Boolean: sheet extension, pipeline option (C) for a refused stated sheet (the helper `MeshSheetExtension` is PR #1550) | Not started; design `docs/design/SHEET_EXTENSION.md` sections 6 and 9, "Under automatic use" | After #1550: stated sheets only or also auto-routed tools; does a failed extension block the hole-fill fallback; the strip as a reference surface of the verification | #1550, [design](design/SHEET_EXTENSION.md) |
| Tet: P4(c), input-conforming CDT for clean inputs | Open; P4(a) merged, P4(b) built (#1196) | Parked (owner 2026-11-22): starts only on the owner's go | [tet](design/TET_MESHING.md), plan #1250 |
| Platform/QA/CI: delete merged / superseded branches | Decided; branches still exist | Owner deletes them (sessions cannot) | [index](ROADMAP.md#open-decisions-owner), plan #1252 |
| Exact Boolean: exact gradients of the volume and area of a Boolean result | Research note and test-only prototype (#1522); the volume gradient is exact | A public API is the owner's decision | #1522, [exact-boolean](roadmap/exact-boolean.md#board-update-2026-11-24-open-items-of-the-exact-boolean-and-the-solid-pipeline) |

## 3. Ready to start (decided or clearly scoped, nobody on it)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Alpha wrap: feature pass K follow-ups (corners, split retries) | Scoped follow-ups, not scheduled | A PR when a caller needs sharper corners | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1237 |
| Tet: P3 follow-ups | Open: boundary sizing, curvature size field, target element count | Scope with P4; one PR each | [tet](design/TET_MESHING.md), plan #1244 |
| Geodesics/param: oracle follow-ups (check) | Heat-method tests against the oracle, fragmentation lever open | Tests first; lever only with a proof | [param](roadmap/parameterization-geodesics.md#open-for-the-future-2026-11-15), plan #1238 |
| Platform/QA/CI: coverage gate red on master | `coverage.yml` `gate` red in every run since 2026-11-22 (4 runs): 18 changed lines of #1432, #1475, #1370 and #1464 without a hit; tests for 16 of them and 2 entries on the unmeasurable list (the prefilter check-mode throw of #1370) are in #1496 (merged 2026-11-24) | Verify the next `coverage.yml` run is green and close | [platform](roadmap/platform.md#open-for-the-future-2026-11-15), plan #1239 |
| Platform/QA/CI: tests above 20 s in some full runs | Diagnosed 2026-11-24 ([note](research/SLOW_TESTS_2026-11.md)): starvation of the library's tiny `gParallel` loops (Boolean property campaigns and a Remesher test run serial now; the Reducer loop and `SolidifyNotClean_AlphaWrapped` need a library decision); the other tests above 20 s are own cost of 4-8 s alone | Owner: tiny-loop policy of `gParallel` call sites; diagnose the non-`gParallel` parallel work under `MeshRobustRepair` | #698, plan #1243 |
| Platform/QA/CI: determinism stage 1 obligations O2-O5 | Done (O1-O5; O6 stays a reviewed note) | Arguments and tests in [the proof](design/DETERMINISM_PROOF.md) 5.4; the cross-runtime `compare` job now fails on a `stage1` / `stage2` difference | [proof](design/DETERMINISM_PROOF.md), plan #1240 (closed) |
| Platform/QA/CI: narrowed mutation filters in CI | Filters for 4 shards checked in | Generate after the nightly full run; renew the rest | [platform](roadmap/platform.md), plan #1242 |
| Other: TPMS Lidinoid formula, primary-source check | Citation verified, paper not opened | Read the paper, update SOURCES.md | [research](roadmap/research.md), plan #1245 |

## 4. Parked or on hold by owner decision

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Boolean: all speed work (#1064 levers, normal-cone certificate, ABC chain step 3 lead) | Stopped, owner 2026-11-16; study of the levers under the full guarantees 2026-11-22 ([note](research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md): ranked list, nothing scheduled) | New levers need the owner's go | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16), plan #1253 |
| Boolean: Exact Boolean 4.0 stages 1b and 1c | On hold, owner 2026-11-18 | A concrete use case for provenance | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16), plan #1254 |
| Boolean: EMBER lever 2 (skip self-pairs in chains) | Parked, owner 2026-11-16 | A caller that chains booleans | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16), plan #1255 |
| Boolean: certifying checker stage 3, certified user mode, exact coplanar clip coordinates | Not scheduled | A use case | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16), plan #1256, spike [BOOLEAN_CERTIFIED_MODE_2026-11.md](research/BOOLEAN_CERTIFIED_MODE_2026-11.md) (2026-11-22, options with the owner) |
| Rounding/repair: `MeshRobustRepair` F2 | Not now, owner 2026-11-17 | Owner go | [study](design/ROBUST_REPAIR_SCALE_FREE_STUDY.md), plan #1257 |
| Rounding/repair: rounding cost levers (hard problem 3 b-d, #837 lever 2) | Not scheduled under the speed stop; study 2026-11-22 ([note](research/ROUNDING_COST_2026-11.md)): bounds and options measured, options with the owner | Speed go from the owner | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1258 |
| Rounding/repair: guaranteed rounding experiment (Devillers et al. 2020) | Not now, owner 2026-11-17; spike 2026-11-22: upper bounds measured, leaning no build; owner 2026-11-22: snap rounding keeps SafeRounding ([stops](ROADMAP.md)); [#1216 study](research/ROUNDING_THIN_SOLIDS_2026-11.md) 2026-11-22: it would not keep the thin solids either, no-go | Gain on real data beyond the RC4b cases | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16), plan #1259, [spike](research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md) |
| Alpha wrap: remove `external/cgal_wrap.cpp`, then a second reference (alecjacobson/alpha-wrap) | Kept while comparisons use CGAL; second reference not now | Remove in its own PR once unused | [wrap](roadmap/alpha-wrap-and-repair.md#other-open-items), plan #1260 |
| Quad: stage 3B follow-ups (exact flow network, flow on the size field, features, boundaries) | Opt-in 3B merged (#1153); `runs/quad-folds` measured (10x/100x SAT budget 0/26, penalized re-solve 25 -> 28/54 at >2x time); owner 2026-11-20: stage 4 features first, then size-adapted flow spike; paper-based flow follow-ups on hold | Features (stage 4), size-adapted flow spike, or paper access | [quad](design/QUAD_REMESHING.md), plan #1261 |
| 2D meshing: research items (Terminator, matched shells, grading placement, Chew-2) | Parked, owner 2026-11-16 | A real input or use case | [2D](roadmap/meshing2d.md#open-for-the-future-2026-11-15), plan #1262 |
| 2D meshing: fast-oracle leftovers and stmlf | Parked; stmlf blocked on inputs | Inputs, or PR CI getting slower | [2D](roadmap/meshing2d.md#open-for-the-future-2026-11-15), plan #1263 |
| 2D meshing: `.poly` reader keeps crossing segment groups as constraints only | Parked | A use case | [2D](roadmap/meshing2d.md#open), plan #1264 |
| Geodesics/param: certified biharmonic mode | Parked, owner 2026-11-16 | Owner go; gates #980 (100k) and #976 (20 s tail) | #976, #980, plan #1265 |
| Geodesics/param: RDR stage 1b (restricted Voronoi cells) | Not now, owner 2026-11-17 | Owner go | [sources](roadmap/sources-surveys.md#open-for-the-future-2026-11-17), plan #1266 |
| Terrain: slope envelope, berms, ramps beyond R1, GeoTIFF reader, showcase | Later, owner 2026-11-17 | A terrain use case | [terrain](roadmap/terrain.md#open-for-the-future-2026-11-15), plan #1267 |
| Terrain: sector polygons E1, S1, R2 | Parked, owner 2026-11-16 | Terrain resumes | [sectors](design/TERRAIN_SECTORS.md), plan #1268 |
| Terrain: §11 performance follow-ups, RoundToGrid, internal quadtree uses | Parked until a real case or terrain resumes | Upper bound first | [terrain](roadmap/terrain.md#open-for-the-future-2026-11-15), plan #1269 |
| Platform/QA/CI: .NET 11 migration | Later, owner 2026-11-12 | Owner schedules it | [platform](roadmap/platform.md#open), plan #1271 |
| Platform/QA/CI: a third net48 test shard | Only if shard b stays the critical path | Queue wait measurement | [platform](roadmap/platform.md#open-for-the-future-2026-11-15), plan #1272 |
| Other: Remesher `RejectInvertingPasses` cheaper snapshot | Parked as owner options, 2026-11-17 | Owner picks labels + undo log or buffers | [note](design/INVERSION_GUARD_SNAPSHOT.md), plan #1273 (closed 2026-11-24, archive) |
| Other: TPMS transitions keeping wall thickness (Ren 2021, Gao 2022) | Later; papers must be provided | Owner provides the papers | [research](roadmap/research.md), plan #1274 |
| Other: batch query extras (Span overloads, early any-hit, all-hits, CLI) | Not done by owner decision | A caller | [batch](design/BATCH_QUERIES.md), plan #1275 (closed 2026-11-24, archive) |
| Other: dual contouring coarse-first construction | Idea, opt-in, not scheduled | Bounds on the implicit | [DC](design/DC_ADAPTIVE_SIMPLIFICATION.md), [bound at N = 512](research/CONTOURING_SDF_SUBGRID_2026-11.md#7b-plan-1276-coarse-first-construction-of-the-grid), plan #1276 |
| Other: unread sources (permissive libraries, PaMO, RWTH, Minkowski, Sci. Rep.) | Documented only | An item that needs one; licence / patent check | [sources](roadmap/sources-surveys.md#open-for-the-future-2026-11-17), plan #1277 |

## 5. Research / hard open problems (no known solution; a study first)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Boolean: open-operand speed, Lévy-style propagation (HP 8) | Correctness fixed (#528); speed open | Speed go; classification design review | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1279 |
| Alpha wrap: provably valid sharp-feature recovery (HP 4) | Study done 2026-11-22: validity provable without the band (checked operations), recovery only a posteriori; K at 0.945 of the oracle | Option 1 built 2026-11-22 ([validity theorem](design/ALPHA_WRAP_FEATURE_VALIDITY.md)); option 2 with #1237; option 3 not decided | [study](research/PROVABLE_SHARP_WRAP_STUDY.md), [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1280 |
| 2D meshing: small-angle cascade without a weaker guarantee (HP 6) | Exemption up to 60°; spike done 2026-11-22 ([note](research/MESHING2D_HP6_HP7_SPIKE_2026-11.md)); option 1 done: the oracle reports the excused triangles below the floor of Miller, Pav and Walkington (`BelowMpwFloorExcused`, #1358) | Owner decision on options 2 to 5 (a proven mode; none leaned on now) | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1282 (closed 2026-11-24, archive) |
| 2D meshing: oracle speed, certifying design (HP 7) | Fast oracle at its stop rule; spike done 2026-11-22 ([note](research/MESHING2D_HP6_HP7_SPIKE_2026-11.md)) | Owner decision on the note's options | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1283 (closed 2026-11-24, archive) |
| Geodesics/param: integer (normal) coordinates | Opt-in layer built 2026-11-22 (#1311, `EnableIntegerCoordinates`): flips and boundary splits; valid in 108 of 108 adversarial runs where tracing succeeds in 101 | Insertions and refinement (option 3 of the [research note](research/INTRINSIC_INTEGER_COORDINATES_2026-11.md)): on demand, with a consumer | [param](roadmap/parameterization-geodesics.md#open-for-the-future-2026-11-15), plan #1284 (closed 2026-11-24, archive) |
| Geodesics/param: P1 collapse, virtual fan vertices | Documented limitation | Study | [param](roadmap/parameterization-geodesics.md#open), plan #1285 |
| Platform/QA/CI: determinism across runtimes and CPUs (HP 11) | Stages 0-1 merged; research spike 2026-11-22: 184 harness cases, one L0 defect found (`LaplacianMeshSmoother`), stage 2 sized (165 members, 147 sites) | Owner decisions of the spike (fix, stage 2 scope, libm in the Boolean), then stage 2 and the #929 measurement | [proof](design/DETERMINISM_PROOF.md), [spike](research/DETERMINISM_CROSS_RUNTIME_2026-11.md), plan #1286 (closed 2026-11-24, archive) |
| Other: contouring certificate, thin features and options B / C | Stage 3 built; rest stopped by rule. Spike 2026-11-22 ([note](research/CONTOURING_ISOTOPY_SUBDIVISION_2026-11.md)): a composed proof for smooth fields by subdivision (its premise checked against Plantinga and Vegter's 2004 and 2007 papers); no-go for creases and for option C | Smooth fields: internal certificate built (#1392, cell shape with a proved margin #1414). Class S on box-shaped blocks: proved, measured (#1415) and decided (owner 2026-11-22): build for levels of a smooth field only, with the per-level certificate of `TwoLevelMarchingTetrahedra` without a domain: built, internal (`TwoLevelSheetCertificate`). Open limit: a lattice clipped to a domain; open: the two levels together, a public option | [note](design/CONTOURING_TOPOLOGY.md), [spike](research/CONTOURING_SDF_SUBGRID_2026-11.md), plan #1289 |
| Other: SIGGRAPH 2026 contouring (distance-grid dual contouring, subgrid marching tetrahedra) | Option 1 built (`TwoLevelMarchingTetrahedra`, `TpmsLattice.TwoLevelSheet`, owner go 2026-11-22); option 2 (certificate per level) built, internal: `TwoLevelSheetCertificate` ([design](design/CONTOURING_BLOCK_CERTIFICATE.md), section 4b) | Options 3 to 6: owner decision | [spike](research/CONTOURING_SDF_SUBGRID_2026-11.md), plan #1297 (closed 2026-11-24, archive) |
| Other: open research questions (metamorphic testing, agent APIs, showcases) | Listed, not started | Pick one with a synergy | [research](roadmap/research.md#open-research-questions), plan #1290 |
| Rounding/repair: the certified empty ball as a rounding guarantee | Closed, no build (#1521): it certifies only what SafeRounding's exact verifier certifies already | Only theory remains | [note](research/SNAP_ROUNDING_EMPTY_BALL_GUARANTEE_2026-11.md) |

## 6. Known limits and tails (documented, kept open on purpose)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Loft: `ContourLoft` stage 2 limits: cut-group saddles stay at `BranchFraction` (per-vertex heights probed, no gain), a hole with an island in a branching solid group, a terminal contour whose hole continues, the straight skeleton (Barequet 2004) not built | Documented limits, owner 2026-11-19 (topic finished: holes #1197, opt-in `LoweredSaddle` for contained groups) | None | [loft](design/CONTOUR_LOFT.md#1011-the-lowered-saddle-opt-in-loweredsaddle-the-numbers-for-cut-groups-and-what-stays-2026-11-19) |
| Repair: `fm_ms` of Thingi10K 498456 above 20 s (29.6 s after #1198; 318843 is at 13.1 s) | Documented exception, owner 2026-11-19 (option C); the levers that would cut it (FWNBeta, the stall rule) change the output | None | [README](../benchmarks/MeshRepairBenchmark/README.md#faster-winding-queries-same-bits-1009-tail), #1009 |
| Tet creases (`CreaseAngleDegrees`, opt-in): small gain on scanned meshes, 3 to 15 times the time there; no guaranteed recovery at acute creases (falls back to the rounded wrap) | Documented, kept open | Protecting balls / weighted Delaunay for a guarantee; crease-local alpha for slits | [tet](design/TET_MESHING.md) 17.14 |
| Boolean: Thingi10K 42327 with its rotated copy, 18-26 s per operation | Dropped as a documented tail, owner 2026-11-19; issue open | Regression rule only | #1047 |
| Boolean: runtime-watch tail above 20 s (eg 55928 at about 470 s) | Judged by the regression rule only | Runtime watch regression or an issue | [boolean](roadmap/exact-boolean.md#open-for-the-future-2026-11-15) |
| Boolean: `SolidBooleanPipeline` contacts limit the reduction | About 12 m triangles per contact line; `PairContactsMode` Off | A caller that needs more | [boolean](roadmap/exact-boolean.md#other-open-items) |
| Boolean: `SolidBooleanPipeline` with an open cutting surface (a sheet as Difference tool): `InputRepair` closes it into the wrong solid; a self-intersecting `MeshAutoRepair` copy is refused (`OpenInput`) | Documented limit (owner 2026-11-22: check + refuse); routing such inputs to the sheet role of #1223 instead of closing them is not built | Owner decision after #1223 | #1211, #1223 |
| Boolean: imprint slivers at 1-ulp contact | Documented limit (#1034) | None | [boolean](roadmap/exact-boolean.md#open-for-the-future-2026-11-15) |
| Rounding/repair: RC4b, the drift bound `MaxLength` | Documented limit, owner 2026-11-17 | None | [triage](research/BOOLEAN_TRIAGE_2026-11.md) |
| Rounding/repair: Reducer quadrics below 1e-280 of the extent | Only on demand | A caller | [wrap](roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15) |
| Rounding/repair: issue-tracker cases P2, S2, S3 | Pinned and documented (upstream defaults) | None | [platform](roadmap/platform.md#open) |
| Alpha wrap: wrap pipelines above 20 s (18 Thingi10K cases) | Regression rule only | Runtime watch | [wrap](roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15) |
| Alpha wrap: raw wrap pinch caps, p01 about 2° below CGAL | Documented | None | [wrap](roadmap/alpha-wrap-and-repair.md#other-open-items) |
| Tet: 38781 and 282598 stay at 9.4° / 9.5° | Rescue phase does not reach them | None | [tet](design/TET_MESHING.md) |
| Quad: missed stage 2 gates, curved open boundaries (sphere-cap; straight open-boundary bug fixed in #1179), 3B fallback in 54 % | Gate restated; size field and 3B opt-in | None | [quad](design/QUAD_REMESHING.md) |
| 2D meshing: oracle on wide-exponent inputs, 2-4.4 s per input | Documented limit | Profile, if it matters | [2D](roadmap/meshing2d.md#open-for-the-future-2026-11-15) |
| Geodesics/param: heat-method drift at rotated UTM (R16) | Diagnosed, fix deferred (#1165) | None | [sweep](ROBUSTNESS_SWEEP.md) |
| Geodesics/param: foldover-free torus fallback | Proven limit of fixed-boundary untangling | None | [param](roadmap/parameterization-geodesics.md#open) |
| Terrain: slope rows in rotated / 1e-3 / UTM frames | Limit stated; `LocalFrame` is the remedy | None | [terrain](roadmap/terrain.md#open) |
| Platform/QA/CI: robustness sweep deferred items | R16, R21, R31, S2, S10, S17-S27, S34-S37 deferred | Fix when a caller hits one | [sweep](ROBUSTNESS_SWEEP.md) |
| Platform/QA/CI: late cron runs of `mutation.yml` | Kept as they are, owner 2026-11-17 | None | [platform](roadmap/platform.md#open-for-the-future-2026-11-15) |
| Other: Remesher defaults (degenerates, self-intersections), thin walls 135363 | Defaults upstream's; safe preset opt-in | None | [wrap](roadmap/alpha-wrap-and-repair.md#other-open-items) |
| Other: CCD on the 1M-triangle ToI frame, serial above 20 s | Documented limit, owner 2026-11-16 | None | [hotspots](roadmap/hotspots.md#open-for-the-future-2026-11-15) |
| Other: `IntrRay3Box3` / `DCurve3BoxTree` ray queries 1.6 to 1.9 times slower after the exact box decision | Accepted, owner 2026-11-22 | Optional certified division-free SAT prefilter for the oriented ray (needs a Gram-deviation margin for non-orthonormal axes; estimated +20 to 30 % instead of +60 to 90 %) | [benchmark](../benchmarks/IntrBox3Benchmark/README.md) |

## 7. Owner's local runs

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Platform/QA/CI: net48 vs net10 runtime comparison | Owner's local campaign | Results, then re-measure net10-only optimizations on net48 | #929 |
| Boolean: thorough CsCheck and fuzz campaign | Owner's local campaign (not a 3.0 blocker) | Findings triaged in one bugfix round | #1038 |
