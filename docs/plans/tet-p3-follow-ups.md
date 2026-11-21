# Plan: tet meshing P3 follow-ups (boundary sizing, curvature size field, target element count)

- **Board row:** OPEN_TOPICS.md §3 "Ready to start", row "Tet: P3 follow-ups"
- **Kind:** ready to start (proposed as three separate PRs: P3-A, P3-B, P3-C; board says "one PR each")
- **Tier:** M per PR (opt-in size-field / budget wrappers around `MeshTetrahedralization`; default path bit for bit)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Stage P3 (interior size control, #1155, [TET_MESHING.md §17.12](../design/TET_MESHING.md)) is merged:
  `MeshTetrahedralization.TargetEdgeLength`, `SizeFunction`, `GradingFactor`, `MinSizeFactor` (default 0.25 of `UsedAlpha`),
  `InvalidSizeSamples`, `Counts["size_clamped_tets"]`; `g3cli tetmesh --target-edge-length --grading-factor`.
- Default path bit-identical (`TetRefinementWorkBoundTests`, 16 pinned fingerprints).
- P4(a) (`MaxSurfaceDeviation`, local alpha, TET_MESHING.md §17.13) is built: it refines the wrap locally where surface
  deviation exceeds the threshold (fidelity of uniform /80 at about half the tets).
- Open from §17.12 line 1238:
  1. **P3-A (boundary sizing):** today `TargetEdgeLength` / `SizeFunction` refine the interior only; surface tets stay at
     the wrap's resolution (`alpha`), so asking for `h < alpha` leaves `unrefined_bad_tets` at the surface (e.g. 2 281 of
     13 923 on the box at `h = alpha / 2`).
  2. **P3-B (curvature / local feature size field):** `GradingFactor` grades only by distance to the wrap surface, not by
     surface curvature or local feature size.
  3. **P3-C (target element count):** no opt-in budget that chooses `h` / `alpha` to land near a requested tet count.
- Overlap check: #1154 (deterministic work cap for `TetMeshImprover`, §1 in progress) touches stage 2; P3-A/B/C act
  before or inside stage 1 (wrap + Delaunay refinement) and stay disjoint from #1154.

## Trigger / decision needed

Scope each follow-up with P4 (owner decides whether P3-A is subsumed by P4(a)'s local alpha + P4(b) creases, or whether
explicit user-driven boundary sizing is wanted). No blocker on P3-B or P3-C once scheduled.

## Goal

Three opt-in size-control extensions (one PR each): (A) surface-respecting sizing when `h` is finer than the wrap,
(B) an automatic curvature / feature-size field, and (C) an opt-in target tet count, each leaving the default output bit
for bit unchanged.

## Scope

- `mesh_ops/MeshTetrahedralization.cs` (options, wiring to wrap local alpha / size function / count solver)
- `mesh_ops/MeshAlphaWrap.cs` (P3-A only: accept an optional per-point / uniform local alpha cap from the size function,
  reusing P4(a)'s local-alpha machinery)
- `mesh_ops/MeshPrincipalCurvature.cs` (read-only building block for P3-B)
- `tools/g3cli/` (CLI flags + MCP schema entries if exposed, with tests)
- `tests/geometry3Sharp.Tests/TetMeshSizeControlTests.cs` (or per-feature test files)
- `benchmarks/TetMeshBenchmark/` (P3-A/B/C subcommands + README numbers)
- `docs/design/TET_MESHING.md` (new §17.15+), `docs/OPEN_TOPICS.md`, `docs/OPERATIONS.md`

## Non-goals

- Default output (all new options off) must stay bit for bit identical (`DefaultPathAndRadiusEdge15_*` and
  `OffValuesOfEveryOption_GiveTheDefaultResultBitForBit`).
- No coarsening below the wrap resolution unless explicitly part of P3-C's alpha choice.
- No change to `TetMeshImprover` (#1154) or exact predicates.

## Interface

Proposed opt-in properties on `MeshTetrahedralization` (all off by default; final names reviewed per PR):
- P3-A: `bool SizeBoundary = false` (when true and `TargetEdgeLength` / `SizeFunction` is set, local wrap alpha is capped
  at the requested size, clamped by `MinSizeFactor * alpha`).
- P3-B: `double CurvatureSizingFactor = 0` (<= 0 / NaN / inf = off; combines curvature radius `1 / k_max` on the wrap
  surface with `GradingFactor` into the interior, clamped by `MinSizeFactor * alpha`).
- P3-C: `int TargetTetCount = 0` (<= 0 = off; estimates `h` from `volume / (0.12 * 1.43 * TargetTetCount)` per §17.12,
  adjusts `alpha` if needed, at most one retry if off by > 2x; never silently exceeds `MaxSteinerPoints`).

## Look before you build

1. Library: P4(a) local alpha (`MaxSurfaceDeviation` in `MeshTetrahedralization` / `MeshAlphaWrap`), P3 size machinery
   and `CallBudget` tests (`TetMeshSizeControlTests`), `MeshPrincipalCurvature` (#1098, quad stage 1), wrap target
   triangle count (#1106) as pattern for P3-C.
2. Literature: Persson (2006), "Mesh size functions for implicit geometries and PDE-based gradient limiting" (Eng. with
   Comput. 22:95-109) for curvature + Lipschitz-graded size fields; Alliez et al. (2005), variational / graded tet sizing;
   Gmsh / TetGen (approach only; TetGen is AGPL - never read its code).

## Steps (one PR per sub-item, clean commits)

- **PR 1 (P3-A, boundary sizing):** wire `SizeBoundary` into P4(a)'s local alpha; verify termination + work-bound credit;
  pin `unrefined_bad_tets` drop on box/sphere at `h = alpha / 2`; invariance / reuse / determinism / off-bit-identical tests.
- **PR 2 (P3-B, curvature size field):** compute principal curvature on the wrap (or smooth input), clamp and grade into
  the interior (using the existing AABB distance or Lipschitz smoothing); measure on procedural cylinder/torus/sphere +
  subset39; tests including smooth sphere (uniform) vs sharp-fillet box (refined at fillet).
- **PR 3 (P3-C, target element count):** invert §17.12's empirical `1.43 * vol / (0.12 h^3)` formula with a coarse/fine
  alpha cap and at most one refinement/retry pass; test on box/sphere/cylinder/torus across 3 targets (e.g. 5k, 20k, 80k)
  within factor 2.

## Acceptance

- Off-path bit-identical: `TetRefinementWorkBoundTests` and `OffValuesOfEveryOption_GiveTheDefaultResultBitForBit` green.
- All new tests <= 5 s each (use `CallBudget`, not wall-clock `CancelF`), checking exact tet validity (`TetFiles.Evaluate` /
  positive signed volumes, manifold boundary, closed, finite, `MinDihedral >= 10°` on procedural shapes).
- Invariance (`Similarity.All()`), determinism (serial == parallel, repeated runs), reuse (on -> off -> on), and killed
  hand mutants for each new option.
- Benchmark `TetMeshBenchmark` before/after table per PR in `TET_MESHING.md`.
- Mutation shards: none gated (no exact arithmetic change); check `tests/mutation` configs if any tet shard is added later.

## Sources and licences

Persson 2006 (paper); no code ports; never read TetGen (AGPL) or CGAL code.

## Budget and stop rule

One M session per PR (3 PRs total). P3-A stop rule: if P4(a)'s local alpha cannot honor a uniform `h = alpha / 2` without
> 2x wrap time, report to the owner before redesigning the wrap. P3-C stop rule: at most 1 retry; if count misses by
> 2x on > 20 % of subset39, document the bound rather than adding an iterative loop.

## Risks and open questions

- Overlap with P4(a) (`MaxSurfaceDeviation`): clarify with the owner how `SizeBoundary` interacts when both are set (min
  local alpha wins).
- Curvature noise on coarse wraps (P3-B): discrete curvature on a coarse wrap may oscillate; measure on the 39-mesh subset
  before picking smoothing iterations.
