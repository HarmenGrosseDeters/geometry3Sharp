# Platform layer and release (Phase 4)

Part of the [roadmap](../ROADMAP.md).

- **Scope:** the CLI / MCP server `g3cli`, analysis building blocks and evaluation metrics, verifiable agent work
  (machine checks, mutation shards, property tests), the CLI / MCP docs for the new operations, the Clipper2 adapter,
  the DAP audit, the upstream ledger, the test suite on other locales, and the release.
- **Status (2026-11-08):** the CLI / MCP and its docs for the new operations are done (#284, #291, #299); the
  issue-tracker fix list is complete (2026-11-03). Open: the upstream ledger, the release, a few small items.
- **Mutation tooling follow-ups (owner 2026-11-08, for the analyst's follow-up PR):** (1) make the coverage pass
  visible: check whether `--verbosity debug` / `trace` logs one line per test during capture and let the ticker count
  it (today the pass is silent for up to ~200 min); (2) upload the Stryker logs as an artifact with `if: always()`,
  so a shard cancelled at the time limit still shows which mutants ran and how long each took; (3) stop a shard early
  when the ticker's projection after the capture is clearly over the limit, and report it; (4) test whether the
  `dots` reporter prints without a terminal (a cheap per-mutant stream next to the ticker). Done in PR #445 (draft, measured 8 November 2026): (1) `--verbosity debug` / `trace` log no line per test during capture
  (a 51-test filter: the capture test host logs `InProgressTests is null` / `Sending test run statistics` lines in batches,
  20 and 6 of them after 10 min), so the ticker shows elapsed time only; (2) the Stryker logs are uploaded with `if: always()`
  (`mutation-logs-mesh-<shard>`, 7 days); (3) `run_stryker.sh` stops a shard early when the projection after the capture
  exceeds the limit by 15 % on two ticks in a row (exit 3, `StrykerOutput/stopped-early`, step summary); (4) `dots` prints
  nothing without a terminal. Open from the analysis: per-part reduced test filters (`exclusive_kills.py`), a `gate` mode
  that fingerprints without thresholds, a two-tier test split, a timeout A/B (`timeout-ratio`), tests for the envelope
  survivors that look real, baseline import through a data branch.
- **#403 merged (2026-11-09, 1fad195d):** its last run was green on all 14 shards within 350 min (rounding parts
  145-294 min, margin >= 56 min). Master baselines dispatched for the 14 non-rounding shards; the rounding baselines are
  seeded after the integration PR (it changes the rounding code, so earlier baselines would be stale at once).
- **JOB-1 on the owner's machine (2026-11-09, runs/mutation-local e4137017):** label A (part 4, 174 tests) is not a kill
  measurement: all 282 mutants Timeout in about 5 s of mutant phase (the Windows test runner most likely failed; the log
  was overwritten). Its capture took 6 h 34 min, about 2x CI. F1 has 171 tests, so a reduced filter barely shortens
  capture; B2 dropped. Decision: no kill measurements on the owner's machine; kills only in CI.
- **Combined-gate filters (analyst, 2026-11-09):** measured on integration/boolean-2.0 (d087c518), uninstrumented: the
  8-term filter 147 tests / 12 s, the 19-term filter 229 tests / 17-18 s (+56 % tests, +45-50 % time). Scaled from the
  #403 run, the 19-term filter fits the 350-min limit only on rounding-7 (rounding-1 ~430 min, -2/-5 ~340 min). Gate: all
  7 rounding parts with the 8-term filter (known times 145-294 min), the 19-term filter at most on rounding-7, plus the
  touched non-rounding shards. A kill signal of the 19-term tests on all parts would need ~11 rounding parts (owner
  decision, not needed for the gate).
- **CLI / MCP update (owner 2026-11-08, queued):** starts after the Meshing2D bundle and the Exact Boolean 2.0 and
  Alpha Wrap 2.0 milestones. A Sonnet session (not Haiku: each command is public surface) first lists the public
  operations `g3cli` does not expose (analysis blocks, fidelity metrics, geodesics, MarchingCubes `LinearEdges` /
  conservative enclosure, 2D booleans via the Clipper2 addon, the new repair / boolean options and result codes) with
  proposed command names, options and result JSON for the owner; then small PRs with `g3cli.Tests` and docs.
- **Related:** [OPERATIONS.md](../OPERATIONS.md), [QA.md](../QA.md), [UPSTREAM_CHANGES.md](../UPSTREAM_CHANGES.md),
  [ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md), [design/DAP_BUGFIX_AUDIT.md](../design/DAP_BUGFIX_AUDIT.md),
  [design/UPSTREAM_LEDGER_PLAN.md](../design/UPSTREAM_LEDGER_PLAN.md), [tools/g3cli](../../tools/g3cli/README.md).

### Open

- **Mutation-run cost analysis** (owner 2026-11-08, done; Sonnet session session_01AaGT59x9M1JVRLVyu3UVMh): a full run is
  >= 29 runner-hours; timeouts are ~55-75 % of testing time on the arrangement shards (a timeout pays 1.5 x the sum of
  its covering tests + 10 s); coverage capture costs 56-71 min per shard and is repeated per shard; every mutation.yml
  edit reruns all shards because select_shards.py forces full mode; rounding and envelope never had a master baseline.
  Owner decision: after #403 merges, (1) region-based full-run selection in select_shards.py + a BASELINE_FORMAT
  constant in the cache key, (3) suggest_split.py (offsets from baseline weights, shards <= ~150 min), (4) a progress
  line; seed master baselines for the new / never-seeded shards by workflow_dispatch right after #403. Later, with
  per-mutant data: two-tier tests / dropping test classes without exclusive kills; timeout ratio 1.1 / 5 s only after
  an A/B with identical survivor sets; larger runners are the owner's cost call.
- **Release:** no public package until the library "just works"; package ID and publishing open (the nuget.org ID
  `geometry3Sharp` belongs to upstream).
- **Hard cases from issue trackers:** the fix list is complete (2026-11-03). Open, pinned and documented as decided:
  P2 (Disk iteration, parked as research), S2 and S3 (upstream defaults kept). Follow-ups: the ray batch API
  ([research.md](research.md#later)), the regressions a remesher flip-guard default would cause, the P1 collapse.
- **Upstream deviation ledger** (task 6, E.5): open, see [below](#plans-fact-checked-2026-10-30). Contact with rms80
  is prepared, not initiated.
- **Verifiable agent work** (QA.md 4a): the parent item is still open; the other items go with the modules they touch
  (status unclear, check).
- **Two wall-clock tests** (`QualityMesh2CollapsedPartsReportTests.Oracle_ManyCollapsedPointsAreCheap`,
  `Predicates_MatchTheOracle_AtAllSizes`): closed 2026-11-08, not reproduced (owner, local): on current master the full
  suite passed all 7866 tests and ten isolated runs of both passed. The oracle test already sits in the
  `Meshing2OracleTiming` collection (parallelization disabled, guarded by `RobustnessSweepRemainingTests`); the
  predicate test has no time budget. Reopen only with the exact failing assertion and invocation.
- **Parked:** the `.poly` reader keeps crossing / touching / overlapping segment groups as constraints only, and a
  vertex with d segments costs about d² comparisons; mesh2d report areas underflow to 0 below coordinates of about
  1e-160 (see [stage 8](#stage-8-planned-follow-ups)).

### Done / history

#### Stage 8: planned follow-ups

Boolean property tests, DAP audit and honest terrain metrics, next to any stage from 4 on; the upstream ledger last, because it records what is merged. Terrain phase 1 after stage 5. After this wave: **CLI / MCP and docs for the new operations** (quadtree, mesher, n-ary and SafeRounding booleans in `g3cli`, examples in OPERATIONS.md), so that agents can use what was built (Direction, points 2 and 3). Candidate: a **Clipper2 adapter** as an optional package (`geometry3Sharp.Clipper2`; Clipper2 is Boost-licensed): robust integer 2D polygon booleans and offsets for terrain / GIS footprints and slicing, written against Clipper2's public API, not taken from the DAP fork; the core stays dependency-free and API compatible. Decide after the DAP audit's provenance list.

Done: property tests, DAP audit (nothing to adopt), Clipper2 adapter (#243). The CLI / MCP docs session waits until quadtree, mesher M2 / M3, n-ary and SafeRounding are merged, so that it documents the finished state in one pass. **CLI / MCP done 2026-11-02** (#284, #291, #299: mesh2d, query2d, xor, Triangle .poly/.node/.ele, snap and SafeRounding options, Meshing2Benchmark `mesh`). Parked: the `.poly` reader keeps crossing / touching / overlapping segment groups as constraints only (Triangle nodes them first), and a vertex with d segments costs about d² comparisons (20k: about 6 s); mesh2d report areas underflow to 0 below coordinates of about 1e-160

Tier: S, M. Parallel: up to 2 sessions

#### Phase 4 items: CLI / MCP, analysis building blocks, evaluation metrics

- [x] CLI / MCP server over the operations: `tools/g3cli` (measure, repair, boolean, resolve, remesh, simplify, uv,
  convert; JSON results with result codes; `g3cli mcp` over stdio)
- [x] Analysis building blocks: wall thickness (`MeshWallThickness`: ray, shape diameter function, inscribed ball /
  medial axis), deviation maps (`MeshDeviation`, statistics in `DeviationStatistics`), primitive fitting
  (`PrimitiveFit3`: plane, sphere, cylinder, cone; RANSAC + least squares)
- [x] Evaluation metrics, so that optimization decisions do not rest on Hausdorff distance and volume IoU alone (they
  miss lost features, reversed patches and topology changes): `MeshFidelityMetrics` (F-score at relative thresholds,
  Chamfer distance, normal consistency and the share of reversed normals, Edge Chamfer distance and edge
  precision / recall on sharp edges), `TriangleQualityStatistics` (Verdict metrics as distributions),
  `MeshTopologySummary` (components, Euler characteristic, genus, boundary loops, changes).
  - [x] In the benchmarks: MeshRepairBenchmark (`fm_*` fields, `analyze_fidelity.py` columns and flags, baseline on
    Thingi10K and Objaverse with the outer surface measures), RemeshBenchmark, UVAtlasBenchmark (UV quality, charts
    that are not discs)
  - [x] Volume IoU restricted to the outer surface: `MeshFidelityMetrics.OuterVolumeIoU` (solid of the reference's outer
    faces, classified in rounds, innermost interior faces first), next to `VolumeIoU`; `fm_oviou` in the benchmarks.
    Result on Objaverse (300 objects): the repaired count stays 65, and under the outer surface Hausdorff criterion it
    drops 121 → 115; the IoU misses are real volume losses, not interior faces (benchmarks/MeshRepairBenchmark README).
    The winding number IoU stays the criterion
  - [x] Curvature fidelity: `MeshCurvature` (Meyer et al. 2003: mixed areas, mean curvature normal, angle deficit;
    degenerate, boundary and bowtie vertices handled) and in `MeshFidelityMetrics` the curvature errors at corresponding
    points, the 1-Wasserstein distance of the distributions and the total absolute curvatures (`fm_h_*`, `fm_k_*`), in
    MeshRepairBenchmark, RemeshBenchmark and TerrainBenchmark (Hambach DGM1: the F-score is 1 for every TerrainGreedyTIN
    result, the curvature measures order them by MaxError)

#### Verifiable agent work

- [ ] Verifiable agent work ([QA.md 4a](../QA.md#4a-making-agent-work-verifiable)): first the machine checks (new tests fail
  on old code, package validation against 1.0.324, banned APIs in exact code), together with a mutation shard per new
  robustness-critical module (the runs on PRs are incremental: tests/mutation/README.md). Acceptance
  against references for the 2D meshing ([strategy](../design/ACCEPTANCE_STRATEGY.md)). The other items go with the modules they touch.
  - [x] Boolean metamorphic and property tests ([implementation plan](../design/BOOLEAN_PROPERTY_TESTS.md), QA §4a
    items 9 and 10): volume identities, partition reconstruction, commutativity, vertex/triangle permutation and
    conforming subdivision; CsCheck campaigns with shrinking and replay on net10.0, the fixed corpus also on net48;
    analytic box oracle. Finding: `SafeRounding` moves exact vertices of pieces below its degenerate threshold
    (`ExactBooleanPropertyFindings`, skipped until fixed in its own tier-L PR).
  - [ ] CsCheck property campaigns beyond the boolean (owner 2026-11-08; after the exact-boolean merge queue; one
    bounded Sonnet task per area, test-only, findings go as bugfixes to the owning session), in this order:
    1. Repair and rounding (`MeshRobustRepair`, `MeshRoundingRepair`): a generator of multi-component inputs with
       very different sizes and distances (it would have found #420 and #425); properties: no defect the input does
       not have, `Apply() == true` implies clean by `NotCleanReason`, every vertex displacement <= MaxDrift, 2^k
       scale invariance.
    2. 2D mesher (`QualityMesh2`) against the independent oracle (fast since #411 / #427): random polygons with
       holes, near-parallel and near-touching segments; properties: input segments are unions of output edges,
       angle bounds, conformity.
    3. Alpha wrap on random soups: closed, manifold, no self-intersection, encloses the input, distance band,
       deterministic; shrinking to minimal pinch / cap cases.
    4. Smaller: `MarchingCubes` `LinearEdges` enclosure on random SDF compositions; terrain metamorphic relations
       (never steeper, cut + fill consistent).
    Rules: campaigns get their own test category that the Stryker test filters exclude (no extra time per mutant);
    CI runs a small seeded campaign, large campaigns run locally or on a Haiku runner; every failing seed becomes a
    fixed example test (QA.md 2.4).
  - [ ] Further test tools (owner 2026-11-08, not scheduled; one bounded task each):
    - Changed-line coverage gate (QA.md item 8) with coverlet (MIT): new or changed lines must be executed by tests;
      cheap, runs in normal CI, catches part of what the mutation runs find hours later.
    - FPTaylor (MIT, QA.md item 12): new floating-point filter bounds also derived by the tool and compared with
      the hand derivation (complements Z3, which checks logical rules).
    - Coverage-guided fuzzing of the readers with SharpFuzz (MIT) + libFuzzer, beyond the blind `ReaderFuzzTests`;
      long runs on a runner or the owner's machine, not in CI; every crash becomes a fixed example test. An initial
      STL pilot and replay workflow are in [ReaderFuzzPilot](../../tools/ReaderFuzzPilot/README.md); other readers remain open.

#### Test suite on German Windows

**Test suite on German Windows (2026-11-04):** merged in #383 (test fixtures and pinned messages formatted invariantly, CRLF-normalized generated-file check, de-DE / en-US regression tests; the production .poly/.node/.ele writers were already invariant). CI shards rebalanced in #381 (envelope tests to net48 shard b: a 39 min, b 30 min; the mesher tests are the bulk of a). Open, small: `QualityMesh2CollapsedPartsReportTests.Oracle_ManyCollapsedPointsAreCheap` and `Predicates_MatchTheOracle_AtAllSizes` fail only in a full local run (pass in isolation): wall-clock budgets under CPU contention, the same class as the oracle timing test fixed with a DisableParallelization collection; check whether they belong there.

#### Plans fact-checked 2026-10-30

Plans, fact-checked on 2026-10-30; each states the owner decisions it still needs. The implementation or audit work remains open.
Tasks 4 to 6 can start independently once those are made; for task 4 they are made.

- [x] **4. DAP bug fixes (E.2):** [audit and adoption plan](../design/DAP_BUGFIX_AUDIT.md). Audited 2026-11-01: the
  delta from `8f185f1` to `a2a4b9f` (and on to `158507b`) has no bug fix that applies here; it has formatting, API
  breaks, behavior changes and features, plus defects of its own that the [audit result](../design/DAP_BUGFIX_AUDIT.md#audit-result-2026-11-01)
  lists so later feature ports avoid them. New features remain blocked on individual provenance.

Task 5 (honest terrain metrics) is on [terrain.md](terrain.md#task-5-honest-terrain-metrics).

- [ ] **6. Upstream deviation ledger (E.5):** [documentation plan](../design/UPSTREAM_LEDGER_PLAN.md). Create
  `docs/UPSTREAM.md` with pinned baselines, implemented additions, behavior/API differences and source/test/commit
  evidence. Complement the existing [migration notes](../UPSTREAM_CHANGES.md); do not include uncommitted work as
  shipped. Prepare, but do not initiate, later contact with rms80.

#### Release: when it's done

No public package until the library "just works": the operations give correct, documented results on hard inputs
without tuning, and it compares with the leading geometry processing libraries (eg the top of
[github.com/topics/geometry-processing](https://github.com/topics/geometry-processing): libigl, geogram, PMP,
cinolib, geometry-central, Manifold) on the operations it shares with them. Publishing a package makes us a
maintainer with users to support; that pays off only then.

- [ ] Hard cases from the issue trackers of those libraries reproduced as tests here (docs/QA.md 2.11), and passing. Booleans done (`ExactBooleanIssueCasesTests`); repair and alpha wrap merged in #326 (24 failure modes, MinimalHoleFill fix); groups 2-4 merged in #330 (2026-11-02); P3 by design (#349); Q2/Q3 (watertight exact ray-triangle: repair CPU +0.9 %, tree queries +2-29 %, single Intersects 4.2x, 260/260 boolean hashes) in #341 G3 (relative path-trace tolerance) in #343 and P1 (FlippedTriangles status, degenerate-UV rule relative to the surface triangle; UVAtlas A/B identical) in #346 and G2 in #347 (2026-11-03). **The fix list is complete** (2026-11-03): fixed R1, S1, G1-G3, P1, P4, Q1-Q3 and the Reducer and constraint bowties; P3 by design; R2 a limitation; P2 (Disk iteration, parked as research), S2 and S3 (upstream defaults kept, OPERATIONS.md recommends the guards) open, pinned and documented as decided. Follow-ups: the ray batch API (Later), the regressions a remesher flip-guard default would cause, the P1 collapse itself (virtual fan vertices or spectral conformal); the other operations started 2026-11-02 in a QA session (repair and alpha wrap, Reducer / Remesher, geodesics and parameterization, queries; mesher and terrain failure modes collected only). Findings in [ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md); owner 2026-11-02: every finding is fixed (correct first, then fast), distributed: QA session Q2+Q3 (exact ray/triangle crossings, IsInside), G3, P1, then G2, P2, S2, S3, P3; 7b session R1 (orientation rule for crossing components; merged in #333 on 2026-11-03: crossing components keep their orientation, A/B 3 inputs now accepted exactly instead of falling back, controls 1.008×), then S1 merged in #336 (2026-11-03; Reducer fold-over guard from area-weighted original normals plus a boundary ear rule, owner: PreventFoldOver default on, UPSTREAM_CHANGES row; noisy terrains 20/110 → 7/0 folds/walls, time on/off 1.00-1.14x, with MaxDeviation 1.01-1.02x)
- [ ] Package ID and publishing (nuget.org ID `geometry3Sharp` belongs to upstream); the pack and package smoke
  test already run in CI
