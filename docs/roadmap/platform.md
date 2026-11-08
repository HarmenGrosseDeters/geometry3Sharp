# Platform layer and release (Phase 4)

Part of the [roadmap](../ROADMAP.md).

- **Scope:** the CLI / MCP server `g3cli`, analysis building blocks and evaluation metrics, verifiable agent work
  (machine checks, mutation shards, property tests), the CLI / MCP docs for the new operations, the Clipper2 adapter,
  the DAP audit, the upstream ledger, the test suite on other locales, and the release.
- **Status (2026-11-08):** the CLI / MCP and its docs for the new operations are done (#284, #291, #299); the
  issue-tracker fix list is complete (2026-11-03). Open: the upstream ledger, the release, a few small items.
- **Related:** [OPERATIONS.md](../OPERATIONS.md), [QA.md](../QA.md), [UPSTREAM_CHANGES.md](../UPSTREAM_CHANGES.md),
  [ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md), [design/DAP_BUGFIX_AUDIT.md](../design/DAP_BUGFIX_AUDIT.md),
  [design/UPSTREAM_LEDGER_PLAN.md](../design/UPSTREAM_LEDGER_PLAN.md), [tools/g3cli](../../tools/g3cli/README.md).

### Open

- **Mutation-run cost analysis** (owner 2026-11-08, a Sonnet session after #403's split has run, analysis only):
  from the Stryker reports and logs, measure the time cost of timeout mutants (31-43 % of mutants on the arrangement
  shards; timeout factor), the slow covering tests per shard (which add time without killing a mutant exclusively),
  mutators that only produce equivalent or worthless mutants (strings, statistics counters), and how to key each
  shard's cache only on its own configuration (today every `mutation.yml` change reruns all shards in full); also
  dynamic shard splitting by measured time. Output: levers with expected savings; changes only after the owner's
  decision, since every gate depends on this tooling.
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
