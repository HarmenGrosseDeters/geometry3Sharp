# Plan: quad remeshing stage 3B follow-ups (exact flow network, size-field flow, features, boundaries)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Quad: stage 3B follow-ups (exact flow network, flow on the size field, features, boundaries)"
- **Kind:** parked (on hold, owner 2026-11-18: reference papers unreachable; trigger: paper access + caller need)
- **Tier:** L (integer-map / min-cost-flow repair, sharp-feature constraints, open-boundary alignment; new algorithm design)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): PR #1229 (Phase 0 fixes) is merged; item 4 (collapse length 0.6) is merged (#1173, default `CollapseLength = 0.6`); item 2 is unblocked (`SizeAdaptation` with `PureQuads` no longer `InvalidInput`, QUAD_REMESHING.md 4.4a); the 3B fallback rate on `QuadFlow3BGate` fell from 54 % to 20 % (uniform) and 11 % (`ScaleAndRotation`) by flow fixes, not by the exact network of item 1; for item 3, stage 4 (`PreserveCreases`, quad-dominant) is built, and feature curves and open boundaries as fixed lines of the 3B flow were measured and stopped as a documented limit (4.7). See [QUAD_REMESHING.md](../design/QUAD_REMESHING.md) sections 4.4a, 4.4d, 4.6, 4.7.

## Current state

- Quad remeshing stages 1, 2, 3B, 5, and CLI/MCP are merged ([QUAD_REMESHING.md](../design/QUAD_REMESHING.md)):
  - `DPolyMesh` + OBJ/PLY I/O (#1097), `CrossField` / `MeshPrincipalCurvature` (#1098, Hermitian form #1099, parallel
    `SparseCholesky` kernels #1103), `PositionField` / `QuadRemesher` / `PolyMeshMetrics` (#1100), opt-in
    `SizeAdaptation` (`Scale` / `ScaleAndRotation`, halves irregular vertices on closed smooth shapes), opt-in
    `QuadRemesher.PureQuads` (stage 3B integer-map repair with fallback and `PureQuadStatus`, #1153), `g3cli quad`.
  - Note: PR #1229 (Phase 0 fixes: `PositionSeconds`, initial Cholesky `NoSolution`, projected `FlippedQuads`, doc
    sync) is merged (2026-11-22).
- Open gaps parked under Stage 3B follow-ups (`QUAD_REMESHING.md` §4.5 / §5):
  1. **Exact QuadriFlow min-cost-flow network:** stage 3B fell back to stage 2 in 54 % of cases at #1153; after the
     flow and sizing update (2026-11-21, QUAD_REMESHING.md 4.4a) it is 20 % uniform and 11 % with `ScaleAndRotation`
     (0 undetected bad), plus an internal second attempt with the fixed variables freed (4.4d). The exact
     minimum-cost network flow formulation of Huang et al. (2018) was unreachable during #1153 and is not built.
  2. **Flow on the size field (`SizeAdaptation` + `PureQuads`):** the `InvalidInput` block is lifted (2026-11-21,
     local spacing in `BuildOffsets` / `SolvePositions`, QUAD_REMESHING.md 4.4a); the target vectors are only
     approximate where the spacing gradient is large.
  3. **Sharp features and open boundaries:** stage 4 (`PreserveCreases`, quad-dominant, opt-in) meets the sharp CAD
     feature gates (QUAD_REMESHING.md 4.6). In the 3B flow, open boundaries and crease curves as fixed lattice lines
     were measured on 2026-11-22 and stopped (4.7, documented limit): `PureQuads` stays closed meshes only and is
     exclusive with `PreserveCreases`; a new attempt needs a new owner go with its own upper-bound study.
  4. **Collapse length `0.4 -> 0.6`:** done, merged in #1173 (owner approved 2026-11-19; default `CollapseLength = 0.6`).

## Trigger / decision needed

Owner provides access to the unreachable papers (Huang et al. 2018 QuadriFlow full text, plus any boundary/feature
quad-flow references) and prioritizes which sub-item to build first (recommended order: (4) collapse length tuning as
a tiny M PR, then (1) exact flow network, then (3) features/boundaries, then (2) size-field flow).

## Goal

Raise `QuadRemesher.PureQuads` success rate substantially above 46 % (target >= 85 % on the 8-shape benchmark), support
sharp creases and clean open boundaries, and optionally combine `SizeAdaptation` with `PureQuads`.

## Scope

- `mesh_ops/QuadRemesher.cs`, `mesh_ops/PositionField.cs`, `mesh_ops/CrossField.cs`
- New flow/feature solver helper(s) under `mesh_ops/` (e.g. `QuadFlowRepair.cs`)
- `tests/geometry3Sharp.Tests/QuadRemesher*Tests.cs`
- `benchmarks/QuadRemeshingBenchmark/` (8-shape procedural suite + Thingi10K subset)
- `docs/design/QUAD_REMESHING.md`, `docs/sources/meshing-and-repair.md`

## Non-goals

Default quad-dominant path (`PureQuads = false`, `SizeAdaptation = Off`) must remain bit-identical unless explicitly
changed with owner approval (such as collapse length 0.4 -> 0.6). Do **not** pursue MIQ / QEx / QuadWild (GPL-only
references or ILP solvers, decided no-go in `QUAD_REMESHING.md` §5).

## Interface

Additive opt-in properties on `QuadRemesher` (e.g. `FeatureAngleDegrees`, `AlignBoundaries`) and lifting the
`SizeAdaptation` + `PureQuads` `InvalidInput` rejection once (2) is built; `PureQuadStatus` extended only if needed.

## Look before you build

1. Library: `CrossField`, `PositionField`, `QuadRemesher`, `DPolyMesh`, `PolyMeshMetrics`, `SparseCholesky`,
   `MeshPrincipalCurvature`, `QUAD_REMESHING.md` §§4-5.
2. Literature: Huang et al. (SGP 2018, "QuadriFlow: A Scalable and Robust Method for Quadrangulation" — MIT code exists
   on GitHub, verify exact license header per file before reading); Jakob et al. (SIGGRAPH Asia 2015, Instant Meshes,
   BSD-3); Knöppel et al. (SIGGRAPH 2013). Never read libQEx / MIQ / QuadWild GPL code.

## Steps (one PR per sub-feature, clean commits)

1. Read Huang et al. (2018) full text (and verify MIT license if inspecting reference formulation); document the exact
   min-cost-flow + SAT/flip network in `QUAD_REMESHING.md` §4.6.
2. **PR A (exact flow network):** upgrade 3B's flow solver; measure fallback rate (before: 54 %) and irregular vertex
   ratio on the 8 shapes + 20 Thingi10K meshes.
3. **PR B (boundaries & sharp features):** constrain boundary tangents and dihedral creases > `FeatureAngleDegrees` in
   `CrossField` and `PositionField`, plus boundary-aligned extraction.
4. **PR C (`SizeAdaptation` + `PureQuads`):** rescale flow capacities/costs by local spacing factor; test on closed
   smooth shapes.

## Acceptance

- PR A: `PureQuads` fallback rate drops from 54 % to <= 15 % on the benchmark suite; 0 flipped quads (projected check),
  all outputs closed/manifold/finite, Hausdorff <= 0.5 s; invariance (`Similarity.All()`), determinism, reuse tests
  green; before/after table + command in `QUAD_REMESHING.md`.
- PR B/C: feature edge alignment + clean open-boundary metrics pinned by procedural tests (filleted box, sphere-cap,
  open-patch) that fail on current code.

## Sources and licences

Huang et al. (2018, paper; verify MIT license before reading any reference code), Jakob et al. (2015, paper/BSD-3),
Knöppel et al. (2013, paper). Never GPL (QEx, MIQ, CoMISo). Cite in `docs/sources/meshing-and-repair.md`.

## Budget and stop rule

1 L session per PR. Stop PR A if the exact flow network does not cut the 54 % fallback rate by at least half or takes
> 20 s on the 110k `big-sphere`.

## Risks and open questions

- PR #1229 (Phase 0 fixes `FlippedQuads`, `NoSolution`) is merged, so they are in the baseline.
- Exact flow networks can still fail on non-orientable or high-curl regions: keep the honest `PureQuadStatus` fallback.
