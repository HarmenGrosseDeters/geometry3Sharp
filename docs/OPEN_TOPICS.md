# Open topics

One board of every open topic of geometry3Sharp, one line each, as of 2026-11-19 (Berlin). Open items only: done, closed and
rejected items are not listed. Maintenance rule: a session that opens, finishes or changes the state of a topic updates its row
here in the same PR; history, numbers and reasons stay in [roadmap/](roadmap/) and the design notes.

## 1. In progress (a session is working on it)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Boolean: lever 1, coplanar pre-decision of corner / edge pairs | PR open, re-measured against the 10 % speed bar | Merge only if the bar is met | #1190 |
| Rounding/repair: exact zero-volume check for undecided sheets | Session running | PR with the exact check (p0029, dui p0019/21/22) | #1191 |
| Tet: deterministic work cap for `TetMeshImprover` | Being measured; part 1 merged (#1184) | Cap at SliverDihedralDegrees 20 / 30, within the time rule | #1154 |
| Tet: opt-in `MaxDihedralDegrees` (Stellar op 4) | PR open | Review and merge; default output unchanged | #1189 |
| Tet: P4(b) creases via S1-F | Phase 1 study | Study report, then a go / no-go | [tet](design/TET_MESHING.md) |
| Repair: `fm_ms` above 20 s on Thingi10K 318843 (20.5 s) and 498456 (46.7 s) | Study started 2026-11-19 (owner), #1009 reopened | Measured plan on #1009, then a bit-identical PR | [#1009](https://github.com/HarmenGrosseDeters/geometry3Sharp/issues/1009) |
| Geodesics/param: exact geodesic paths from `PolyhedralGeodesicDistance` (`TracePath`) | API approved 2026-11-19, being built (the #1186 trim is merged, #1193) | PR | #1 |

## 2. Waiting for an owner decision (options are on the table)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Rounding/repair: Thingi10K numbers for the #1096 cases | Procedural answer merged; real meshes not run | Owner: run `ResultVolumeIoU` on a runner or not | [repair](roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15) |
| Rounding/repair: open inputs, inside / outside across patches | Measured, leaning no-go | Owner: name a use case for an opt-in large-hole label source | [note](design/INSIDE_OUTSIDE_OPEN_INPUTS.md) |
| Alpha wrap: raw-size budget on by default | Off by default, recipe known (300k, coarsening 1.2) | Owner: want it on? Then one 800 run first | [wrap](roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15) |
| Tet: lower limit on `MaxRadiusEdgeRatio` | Work bound built; library limit not built | Owner: 1.5, 1.3 or none | [tet](design/TET_MESHING.md) |
| Tet: P4(c), input-conforming CDT for clean inputs | Open; P4(a) merged, P4(b) in study | Owner: P4 choice after the S1-F study | [tet](design/TET_MESHING.md) |
| Tet: 839690 at alpha / 20 needs a boundary change | Beyond the slide cap | Owner: allow a larger boundary move or not | [tet](design/TET_MESHING.md) |
| Platform/QA/CI: delete merged / superseded branches | Decided; branches still exist | Owner deletes them (sessions cannot) | [index](ROADMAP.md#open-decisions-owner) |

## 3. Ready to start (decided or clearly scoped, nobody on it)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Alpha wrap: feature pass K follow-ups (corners, split retries) | Scoped follow-ups, not scheduled | A PR when a caller needs sharper corners | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model) |
| Tet: P3 follow-ups | Open: boundary sizing, curvature size field, target element count | Scope with P4; one PR each | [tet](design/TET_MESHING.md) |
| Geodesics/param: oracle follow-ups (check) | Heat-method tests against the oracle, fragmentation lever open | Tests first; lever only with a proof | [param](roadmap/parameterization-geodesics.md#open-for-the-future-2026-11-15) |
| Platform/QA/CI: coverage gate red on master | `coverage.yml` failed in every run 2026-11-18/19 | Diagnose the uncovered lines, add tests | [platform](roadmap/platform.md#open-for-the-future-2026-11-15) |
| Platform/QA/CI: tests above 20 s in some full runs | Open; thread-pool starvation suspected | Diagnose per test, split or fix | #698 |
| Platform/QA/CI: determinism proof stage 2 | Unblocked (3.0 done); planned as one L PR | `ExactMeshArrangement`, `ExactMeshBoolean` as a closed world | [proof](design/DETERMINISM_PROOF.md) |
| Platform/QA/CI: determinism stage 1 obligations O2-O5 | Open (O1 done) | Small fixes or arguments, with stage 2 | [proof](design/DETERMINISM_PROOF.md) |
| Platform/QA/CI: narrowed mutation filters in CI | Filters for 4 shards checked in | Generate after the nightly full run; renew the rest | [platform](roadmap/platform.md) |
| Other: TPMS Lidinoid formula, primary-source check | Citation verified, paper not opened | Read the paper, update SOURCES.md | [research](roadmap/research.md) |

## 4. Parked or on hold by owner decision

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Boolean: all speed work (#1064 levers, normal-cone certificate, ABC chain step 3 lead) | Stopped, owner 2026-11-16 | New levers need the owner's go | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16) |
| Boolean: Exact Boolean 4.0 stages 1b and 1c | On hold, owner 2026-11-18 | A concrete use case for provenance | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16) |
| Boolean: EMBER lever 2 (skip self-pairs in chains) | Parked, owner 2026-11-16 | A caller that chains booleans | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16) |
| Boolean: certifying checker stage 3, certified user mode, exact coplanar clip coordinates | Not scheduled | A use case | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16) |
| Rounding/repair: `MeshRobustRepair` F2 | Not now, owner 2026-11-17 | Owner go | [study](design/ROBUST_REPAIR_SCALE_FREE_STUDY.md) |
| Rounding/repair: rounding cost levers (hard problem 3 b-d, #837 lever 2) | Not scheduled under the speed stop | Speed go from the owner | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model) |
| Rounding/repair: guaranteed rounding experiment (Devillers et al. 2020) | Not now, owner 2026-11-17 | Gain on real data beyond the RC4b cases | [boolean](roadmap/exact-boolean.md#after-30-owner-2026-11-16) |
| Alpha wrap: remove `external/cgal_wrap.cpp`, then a second reference (alecjacobson/alpha-wrap) | Kept while comparisons use CGAL; second reference not now | Remove in its own PR once unused | [wrap](roadmap/alpha-wrap-and-repair.md#other-open-items) |
| Quad: stage 3B follow-ups (exact flow network, flow on the size field, features, boundaries) | Opt-in 3B merged (#1153); follow-ups on hold, papers unreachable | Paper access | [quad](design/QUAD_REMESHING.md) |
| 2D meshing: research items (Terminator, matched shells, grading placement, Chew-2) | Parked, owner 2026-11-16 | A real input or use case | [2D](roadmap/meshing2d.md#open-for-the-future-2026-11-15) |
| 2D meshing: fast-oracle leftovers and stmlf | Parked; stmlf blocked on inputs | Inputs, or PR CI getting slower | [2D](roadmap/meshing2d.md#open-for-the-future-2026-11-15) |
| 2D meshing: `.poly` reader keeps crossing segment groups as constraints only | Parked | A use case | [2D](roadmap/meshing2d.md#open) |
| Geodesics/param: certified biharmonic mode | Parked, owner 2026-11-16 | Owner go; gates #980 (100k) and #976 (20 s tail) | #976, #980 |
| Geodesics/param: RDR stage 1b (restricted Voronoi cells) | Not now, owner 2026-11-17 | Owner go | [sources](roadmap/sources-surveys.md#open-for-the-future-2026-11-17) |
| Terrain: slope envelope, 3D blocks, berms, ramps beyond R1, GeoTIFF reader, showcase | Later, owner 2026-11-17 | A terrain use case | [terrain](roadmap/terrain.md#open-for-the-future-2026-11-15) |
| Terrain: sector polygons E1, S1, R2 | Parked, owner 2026-11-16 | Terrain resumes | [sectors](design/TERRAIN_SECTORS.md) |
| Terrain: §11 performance follow-ups, RoundToGrid, internal quadtree uses | Parked until a real case or terrain resumes | Upper bound first | [terrain](roadmap/terrain.md#open-for-the-future-2026-11-15) |
| Platform/QA/CI: NativeAOT package | No, owner 2026-11-17 | None | [index](ROADMAP.md#now) |
| Platform/QA/CI: .NET 11 migration | Later, owner 2026-11-12 | Owner schedules it | [platform](roadmap/platform.md#open) |
| Platform/QA/CI: a third net48 test shard | Only if shard b stays the critical path | Queue wait measurement | [platform](roadmap/platform.md#open-for-the-future-2026-11-15) |
| Other: Remesher `RejectInvertingPasses` cheaper snapshot | Parked as owner options, 2026-11-17 | Owner picks labels + undo log or buffers | [note](design/INVERSION_GUARD_SNAPSHOT.md) |
| Other: TPMS transitions keeping wall thickness (Ren 2021, Gao 2022) | Later; papers must be provided | Owner provides the papers | [research](roadmap/research.md) |
| Other: batch query extras (Span overloads, early any-hit, all-hits, CLI) | Not done by owner decision | A caller | [batch](design/BATCH_QUERIES.md) |
| Other: dual contouring coarse-first construction | Idea, opt-in, not scheduled | Bounds on the implicit | [DC](design/DC_ADAPTIVE_SIMPLIFICATION.md) |
| Other: unread sources (permissive libraries, PaMO, RWTH, Minkowski, Sci. Rep.) | Documented only | An item that needs one; licence / patent check | [sources](roadmap/sources-surveys.md#open-for-the-future-2026-11-17) |

## 5. Research / hard open problems (no known solution; a study first)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Rounding/repair: practical 3D snap rounding with a guarantee (HP 1) | No guarantee known; plateau variant a no-go | Study only on a new lead | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model) |
| Boolean: open-operand speed, Lévy-style propagation (HP 8) | Correctness fixed (#528); speed open | Speed go; classification design review | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model) |
| Alpha wrap: provably valid sharp-feature recovery (HP 4) | Pass K opt-in; no proof | Study of a hybrid with guarantees | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model) |
| Tet: proven dihedral floor on 100 % of inputs (HP 5, P5(b)) | 498 of 500 at default; no bound | Lattice method only if a proof is required | [tet](design/TET_MESHING.md) |
| 2D meshing: small-angle cascade without a weaker guarantee (HP 6) | Exemption up to 60° | Provably tight region excuse, or a new rule | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model) |
| 2D meshing: oracle speed, certifying design (HP 7) | Fast oracle at its stop rule | Certifying-algorithm design per part | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model) |
| Geodesics/param: integer (normal) coordinates | Not needed by current operations | Study when an exact common subdivision is needed | [param](roadmap/parameterization-geodesics.md#open-for-the-future-2026-11-15) |
| Geodesics/param: P1 collapse, virtual fan vertices | Documented limitation | Study | [param](roadmap/parameterization-geodesics.md#open) |
| Platform/QA/CI: determinism across runtimes and CPUs (HP 11) | Stages 0-1 merged | Stage 2, then the #929 measurement | [proof](design/DETERMINISM_PROOF.md) |
| Platform/QA/CI: golden masters across runtimes vs bit-for-bit | Lead untried | Study | [hotspots](roadmap/hotspots.md#open-for-the-future-2026-11-15) |
| Other: envelope-guaranteed simplification, the paper's algorithm (HP 10) | Envelope stopped 2026-11-01; Phase 5 #2 open | A cheap certified per-collapse bound | [research](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model) |
| Other: contouring certificate, thin features and options B / C | Stage 3 built; rest stopped by rule | Reviewed proof before any build | [note](design/CONTOURING_TOPOLOGY.md) |
| Other: open research questions (metamorphic testing, agent APIs, showcases) | Listed, not started | Pick one with a synergy | [research](roadmap/research.md#open-research-questions) |

## 6. Known limits and tails (documented, kept open on purpose)

| Topic | State | Next step / waiting on | Link |
|---|---|---|---|
| Loft: `ContourLoft` stage 2 limits: cut-group saddles stay at `BranchFraction` (per-vertex heights probed, no gain), a hole with an island in a branching solid group, a terminal contour whose hole continues, the straight skeleton (Barequet 2004) not built | Documented limits, owner 2026-11-19 (topic finished: holes #1197, opt-in `LoweredSaddle` for contained groups) | None | [loft](design/CONTOUR_LOFT.md#1011-the-lowered-saddle-opt-in-loweredsaddle-the-numbers-for-cut-groups-and-what-stays-2026-11-19) |
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
| Quad: missed stage 2 gates, ragged open boundary, 3B fallback in 54 % | Gate restated; size field and 3B opt-in | None | [quad](design/QUAD_REMESHING.md) |
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
