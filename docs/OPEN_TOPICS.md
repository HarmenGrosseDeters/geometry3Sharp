# Open topics

One board of every open topic of geometry3Sharp, one line each, as of 2026-11-22 (Berlin). Open items only: done, closed and
rejected items are not listed. Every item in §§2–5 links to a draft plan PR (`plan/*`, holding `docs/plans/<slug>.md`,
not to be merged as is). Maintenance rule: a session that opens, finishes or changes the state of a topic updates its row
here in the same PR; history, numbers and reasons stay in [roadmap/](roadmap/) and the design notes.

## 1. In progress (a session is working on it)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Rounding/repair: exact zero-volume check for undecided sheets | PR open | CI, both incremental mutation runs (exact-arithmetic, rounding-9), abc-chained outcome diff, then merge | #1195 (#1191) |
| Tet: deterministic work cap for `TetMeshImprover` | Part 1 merged (#1184); a split-only cap was measured and dropped (owner 2026-11-19: it leaves meshes above 20 s at floor 20) | Measure a bound over all improvement attempts or a pass budget; results to the owner before a PR | #1154 |

## 2. Waiting for an owner decision (options are on the table)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Tet: P4(c), input-conforming CDT for clean inputs | Open; P4(a) merged, P4(b) built (#1196) | Parked (owner 2026-11-22): starts only on the owner's go | [tet](design/TET_MESHING.md), plan #1250 |
| Platform/QA/CI: delete merged / superseded branches | Decided; branches still exist | Owner deletes them (sessions cannot) | [index](ROADMAP.md#open-decisions-owner), plan #1252 |

## 3. Ready to start (decided or clearly scoped, nobody on it)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Rounding/repair: the removal rule for thin and tiny components (#1216) | Decided 2026-11-22 (owner): rule (d1), K = 4 ulps, factor 2; a removal of only components below K ulps stays clean and is reported, any other removal makes the result not clean; every removed component is reported (count, volume, thickness). Study merged with #1397 | Build as a separate tier-L PR (outputs change: the 263 hashes, the cohorts, the rounding shards), after #1382 | [study](research/ROUNDING_THIN_SOLIDS_2026-11.md), #1216, [audit](research/SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md) |
| Alpha wrap: feature pass K follow-ups (corners, split retries) | Scoped follow-ups, not scheduled | A PR when a caller needs sharper corners | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1237 |
| Tet: P3 follow-ups | Open: boundary sizing, curvature size field, target element count | Scope with P4; one PR each | [tet](design/TET_MESHING.md), plan #1244 |
| Geodesics/param: oracle follow-ups (check) | Heat-method tests against the oracle, fragmentation lever open | Tests first; lever only with a proof | [param](roadmap/parameterization-geodesics.md#open-for-the-future-2026-11-15), plan #1238 |
| Platform/QA/CI: coverage gate red on master | `coverage.yml` failed in every run 2026-11-18/19 (last 4 runs through 2026-11-21 green — verify & close) | Diagnose the uncovered lines, add tests | [platform](roadmap/platform.md#open-for-the-future-2026-11-15), plan #1239 |
| Platform/QA/CI: tests above 20 s in some full runs | Open; thread-pool starvation suspected | Diagnose per test, split or fix | #698, plan #1243 |
| Platform/QA/CI: determinism proof stage 2 | Unblocked (3.0 done); planned as one L PR | `ExactMeshArrangement`, `ExactMeshBoolean` as a closed world | [proof](design/DETERMINISM_PROOF.md), plan #1241 |
| Platform/QA/CI: determinism stage 1 obligations O2-O5 | Open (O1 done) | Small fixes or arguments, with stage 2 | [proof](design/DETERMINISM_PROOF.md), plan #1240 |
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
| Rounding/repair: rounding cost levers (hard problem 3 b-d, #837 lever 2) | Not scheduled under the speed stop | Speed go from the owner | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1258 |
| Rounding/repair: guaranteed rounding experiment (Devillers et al. 2020) | Not now, owner 2026-11-17; spike 2026-11-22: upper bounds measured, leaning no build (owner decision open); [#1216 study](research/ROUNDING_THIN_SOLIDS_2026-11.md) 2026-11-22: it would not keep the thin solids either, no-go | Gain on real data beyond the RC4b cases | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16), plan #1259, [spike](research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md) |
| Alpha wrap: remove `external/cgal_wrap.cpp`, then a second reference (alecjacobson/alpha-wrap) | Kept while comparisons use CGAL; second reference not now | Remove in its own PR once unused | [wrap](roadmap/alpha-wrap-and-repair.md#other-open-items), plan #1260 |
| Quad: stage 3B follow-ups (exact flow network, flow on the size field, features, boundaries) | Opt-in 3B merged (#1153); `runs/quad-folds` measured (10x/100x SAT budget 0/26, penalized re-solve 25 -> 28/54 at >2x time); owner 2026-11-20: stage 4 features first, then size-adapted flow spike; paper-based flow follow-ups on hold | Features (stage 4), size-adapted flow spike, or paper access | [quad](design/QUAD_REMESHING.md), plan #1261 |
| 2D meshing: research items (Terminator, matched shells, grading placement, Chew-2) | Parked, owner 2026-11-16 | A real input or use case | [2D](roadmap/meshing2d.md#open-for-the-future-2026-11-15), plan #1262 |
| 2D meshing: fast-oracle leftovers and stmlf | Parked; stmlf blocked on inputs | Inputs, or PR CI getting slower | [2D](roadmap/meshing2d.md#open-for-the-future-2026-11-15), plan #1263 |
| 2D meshing: `.poly` reader keeps crossing segment groups as constraints only | Parked | A use case | [2D](roadmap/meshing2d.md#open), plan #1264 |
| Geodesics/param: certified biharmonic mode | Parked, owner 2026-11-16 | Owner go; gates #980 (100k) and #976 (20 s tail) | #976, #980, plan #1265 |
| Geodesics/param: RDR stage 1b (restricted Voronoi cells) | Not now, owner 2026-11-17 | Owner go | [sources](roadmap/sources-surveys.md#open-for-the-future-2026-11-17), plan #1266 |
| Terrain: slope envelope, 3D blocks, berms, ramps beyond R1, GeoTIFF reader, showcase | Later, owner 2026-11-17 | A terrain use case | [terrain](roadmap/terrain.md#open-for-the-future-2026-11-15), plan #1267 |
| Terrain: sector polygons E1, S1, R2 | Parked, owner 2026-11-16 | Terrain resumes | [sectors](design/TERRAIN_SECTORS.md), plan #1268 |
| Terrain: §11 performance follow-ups, RoundToGrid, internal quadtree uses | Parked until a real case or terrain resumes | Upper bound first | [terrain](roadmap/terrain.md#open-for-the-future-2026-11-15), plan #1269 |
| Platform/QA/CI: NativeAOT package | No, owner 2026-11-17 | None | [index](ROADMAP.md#now), plan #1270 |
| Platform/QA/CI: .NET 11 migration | Later, owner 2026-11-12 | Owner schedules it | [platform](roadmap/platform.md#open), plan #1271 |
| Platform/QA/CI: a third net48 test shard | Only if shard b stays the critical path | Queue wait measurement | [platform](roadmap/platform.md#open-for-the-future-2026-11-15), plan #1272 |
| Other: Remesher `RejectInvertingPasses` cheaper snapshot | Parked as owner options, 2026-11-17 | Owner picks labels + undo log or buffers | [note](design/INVERSION_GUARD_SNAPSHOT.md), plan #1273 |
| Other: TPMS transitions keeping wall thickness (Ren 2021, Gao 2022) | Later; papers must be provided | Owner provides the papers | [research](roadmap/research.md), plan #1274 |
| Other: batch query extras (Span overloads, early any-hit, all-hits, CLI) | Not done by owner decision | A caller | [batch](design/BATCH_QUERIES.md), plan #1275 |
| Other: dual contouring coarse-first construction | Idea, opt-in, not scheduled | Bounds on the implicit | [DC](design/DC_ADAPTIVE_SIMPLIFICATION.md), [bound at N = 512](research/CONTOURING_SDF_SUBGRID_2026-11.md#7b-plan-1276-coarse-first-construction-of-the-grid), plan #1276 |
| Other: unread sources (permissive libraries, PaMO, RWTH, Minkowski, Sci. Rep.) | Documented only | An item that needs one; licence / patent check | [sources](roadmap/sources-surveys.md#open-for-the-future-2026-11-17), plan #1277 |

## 5. Research / hard open problems (no known solution; a study first)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Rounding/repair: practical 3D snap rounding with a guarantee (HP 1) | No guarantee known; plateau variant a no-go; spike 2026-11-22 ([note](research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md)): the known guarantee does not give clean results or keep more solids, leaning no build | Study only on a new lead | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1278 |
| Boolean: open-operand speed, Lévy-style propagation (HP 8) | Correctness fixed (#528); speed open | Speed go; classification design review | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1279 |
| Alpha wrap: provably valid sharp-feature recovery (HP 4) | Study done 2026-11-22: validity provable without the band (checked operations), recovery only a posteriori; K at 0.945 of the oracle | Option 1 built 2026-11-22 ([validity theorem](design/ALPHA_WRAP_FEATURE_VALIDITY.md)); option 2 with #1237; option 3 not decided | [study](research/PROVABLE_SHARP_WRAP_STUDY.md), [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1280 |
| Tet: proven dihedral floor on 100 % of inputs (HP 5, P5(b)) | 498 of 500 at default; no bound. Spike 2026-11-22: a uniform lattice proves 9.31° (10.78° per tet, validity then checked per run) with a rounded boundary, at 10 / 69 times the tets still 6.5 / 1.8 times farther from the input; chamfering gives no dihedral bound | Owner: stop (leaning) or an opt-in lattice mesher | [tet](design/TET_MESHING.md), [spike](research/TET_DIHEDRAL_FLOOR_SPIKE_2026-11.md), plan #1281 |
| 2D meshing: small-angle cascade without a weaker guarantee (HP 6) | Exemption up to 60°; spike done 2026-11-22 ([note](research/MESHING2D_HP6_HP7_SPIKE_2026-11.md)) | Owner decision on the note's options | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1282 |
| 2D meshing: oracle speed, certifying design (HP 7) | Fast oracle at its stop rule; spike done 2026-11-22 ([note](research/MESHING2D_HP6_HP7_SPIKE_2026-11.md)) | Owner decision on the note's options | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1283 |
| Geodesics/param: integer (normal) coordinates | Spike done 2026-11-22: traced subdivision fails in 6 % (flips) / 15 % (refined) of adversarial runs, integer prototype in none | Owner decision on the options of the [research note](research/INTRINSIC_INTEGER_COORDINATES_2026-11.md) | [param](roadmap/parameterization-geodesics.md#open-for-the-future-2026-11-15), plan #1284 |
| Geodesics/param: P1 collapse, virtual fan vertices | Documented limitation | Study | [param](roadmap/parameterization-geodesics.md#open), plan #1285 |
| Platform/QA/CI: determinism across runtimes and CPUs (HP 11) | Stages 0-1 merged; research spike 2026-11-22: 184 harness cases, one L0 defect found (`LaplacianMeshSmoother`), stage 2 sized (165 members, 147 sites) | Owner decisions of the spike (fix, stage 2 scope, libm in the Boolean), then stage 2 and the #929 measurement | [proof](design/DETERMINISM_PROOF.md), [spike](research/DETERMINISM_CROSS_RUNTIME_2026-11.md), plan #1286 |
| Platform/QA/CI: golden masters across runtimes vs bit-for-bit | Studied 2026-11-22: two tiers proposed, the tier decided by static libm reachability | Owner decision on the policy | [spike](research/DETERMINISM_CROSS_RUNTIME_2026-11.md) section 6, [hotspots](roadmap/hotspots.md#open-for-the-future-2026-11-15), plan #1287 |
| Other: envelope-guaranteed simplification, the paper's algorithm (HP 10) | Envelope stopped 2026-11-01; spike 2026-11-22 ([note](research/ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md)): guaranteed, but 1.1-1.9x the triangles and mostly 5-98x the time of `MaxDeviation`; leaning no-go | Owner decision on the note's options | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1288 |
| Other: contouring certificate, thin features and options B / C | Stage 3 built; rest stopped by rule | Reviewed proof before any build | [note](design/CONTOURING_TOPOLOGY.md), [spike](research/CONTOURING_SDF_SUBGRID_2026-11.md), plan #1289 |
| Other: SIGGRAPH 2026 contouring (distance-grid dual contouring, subgrid marching tetrahedra) | Option 1 built (`TwoLevelMarchingTetrahedra`, `TpmsLattice.TwoLevelSheet`, owner go 2026-11-22); option 2 (certificate per level) next | Options 3 to 6: owner decision | [spike](research/CONTOURING_SDF_SUBGRID_2026-11.md), plan #1297 |
| Other: open research questions (metamorphic testing, agent APIs, showcases) | Listed, not started | Pick one with a synergy | [research](roadmap/research.md#open-research-questions), plan #1290 |

## 6. Known limits and tails (documented, kept open on purpose)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Loft: `ContourLoft` stage 2 limits: cut-group saddles stay at `BranchFraction` (per-vertex heights probed, no gain), a hole with an island in a branching solid group, a terminal contour whose hole continues, the straight skeleton (Barequet 2004) not built | Documented limits, owner 2026-11-19 (topic finished: holes #1197, opt-in `LoweredSaddle` for contained groups) | None | [loft](design/CONTOUR_LOFT.md#1011-the-lowered-saddle-opt-in-loweredsaddle-the-numbers-for-cut-groups-and-what-stays-2026-11-19) |
| Repair: `fm_ms` of Thingi10K 498456 above 20 s (29.6 s after #1198; 318843 is at 13.1 s) | Documented exception, owner 2026-11-19 (option C); the levers that would cut it (FWNBeta, the stall rule) change the output | None | [README](../benchmarks/MeshRepairBenchmark/README.md#faster-winding-queries-same-bits-1009-tail), #1009 |
| Tet creases (`CreaseAngleDegrees`, opt-in): small gain on scanned meshes, 3 to 15 times the time there; no guaranteed recovery at acute creases (falls back to the rounded wrap) | Documented, kept open | Protecting balls / weighted Delaunay for a guarantee; crease-local alpha for slits | [tet](design/TET_MESHING.md) 17.14 |
| Boolean: Thingi10K 42327 with its rotated copy, 18-26 s per operation | Dropped as a documented tail, owner 2026-11-19; issue open | Regression rule only | #1047 |
| Boolean: runtime-watch tail above 20 s (eg 55928 at about 470 s) | Judged by the regression rule only | Runtime watch regression or an issue | [boolean](roadmap/exact-boolean.md#open-for-the-future-2026-11-15) |
| Boolean: `SolidBooleanPipeline` contacts limit the reduction | About 12 m triangles per contact line; `PairContactsMode` Off | A caller that needs more | [boolean](roadmap/exact-boolean.md#other-open-items) |
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

## 7. Owner's local runs

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Platform/QA/CI: net48 vs net10 runtime comparison | Owner's local campaign | Results, then re-measure net10-only optimizations on net48 | #929 |
| Boolean: thorough CsCheck and fuzz campaign | Owner's local campaign (not a 3.0 blocker) | Findings triaged in one bugfix round | #1038 |
