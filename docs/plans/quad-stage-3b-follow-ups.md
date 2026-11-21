# Plan: quad remeshing stage 3B follow-ups (exact flow network, size-field flow, features, boundaries)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Quad: stage 3B follow-ups (exact flow network, flow on the size field, features, boundaries)"
- **Kind:** parked (on hold, owner 2026-11-18: reference papers unreachable; trigger: paper access + caller need)
- **Tier:** L (integer-map / min-cost-flow repair, sharp-feature constraints, open-boundary alignment; new algorithm design)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Quad remeshing stages 1, 2, 3B, 5, and CLI/MCP are merged ([QUAD_REMESHING.md](../design/QUAD_REMESHING.md)):
  - `DPolyMesh` + OBJ/PLY I/O (#1097), `CrossField` / `MeshPrincipalCurvature` (#1098, Hermitian form #1099, parallel
    `SparseCholesky` kernels #1103), `PositionField` / `QuadRemesher` / `PolyMeshMetrics` (#1100), opt-in
    `SizeAdaptation` (`Scale` / `ScaleAndRotation`, halves irregular vertices on closed smooth shapes), opt-in
    `QuadRemesher.PureQuads` (stage 3B integer-map repair with fallback and `PureQuadStatus`, #1153), `g3cli quad`.
  - Note: PR #1229 (open, Phase 0 fixes: `PositionSeconds`, initial Cholesky `NoSolution`, projected `FlippedQuads`, doc
    sync) should settle before any quad follow-up starts.
- Open gaps parked under Stage 3B follow-ups (`QUAD_REMESHING.md` §4.5 / §5):
  1. **Exact QuadriFlow min-cost-flow network:** current stage 3B falls back to stage 2 in 54 % of cases; the exact
     minimum-cost network flow formulation of Huang et al. (2018) was unreachable during #1153.
  2. **Flow on the size field (`SizeAdaptation` + `PureQuads`):** currently returns `InvalidInput` because 3B's flow
     assumes uniform spacing.
  3. **Sharp features and open boundaries:** ragged open boundaries (stage 2/3B) and CAD sharp creases are not aligned
     in the cross/position fields or flow network.
  4. **Collapse length `0.4 -> 0.6`:** noted by owner (2026-11-19) as a separate small PR after pure-quads.

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

- PR #1229 must merge first so Phase 0 fixes (`FlippedQuads`, `NoSolution`) are in the baseline.
- Exact flow networks can still fail on non-orientable or high-curl regions: keep the honest `PureQuadStatus` fallback.
