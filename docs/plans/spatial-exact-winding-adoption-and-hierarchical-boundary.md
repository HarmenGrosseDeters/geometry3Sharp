# Plan: adopt exact winding in FWN callers and add hierarchical boundary sum to ExactWindingCache

- **Board row:** Not yet listed in [OPEN_TOPICS.md](../OPEN_TOPICS.md) (direct follow-up to PR `#1294` and SIGGRAPH 2026 audit PR `#1292`)
- **Kind:** high-ROI follow-up (opt-in exact winding on `FastWindingNumber` callers + hierarchical boundary acceleration in `ExactWindingCache` for open meshes with large `|∂M|`; changing existing FWN defaults changes output bits and requires an owner decision)
- **Tier:** M/L (Part A is M: opt-in exact winding wiring; Part B is L if certified error bounds are added to `ExactWindingCache`'s boundary hierarchy)
- **Plan status:** draft, 2026-11-21 (Berlin); updated 2026-11-22 (Berlin): PR `#1294` merged on 2026-11-22; Parts A and B are implemented in open PR `#1298`. The plan closes when `#1298` merges.

## Current state

- PR `#1294` (merged 2026-11-22) replaces the legacy Jacobson `WindingCache` in [`DMeshAABBTree3.WindingNumber`](../../spatial/DMeshAABBTree.cs) with an exact ray-plus-boundary `ExactWindingCache` (Antipodal / FEWN decomposition with exact `Orient2d`/`Orient3d` symbolic perturbation) and adds `+x` row-ray reuse to [`MeshWindingNumberGrid`](../../spatial/MeshWindingNumberGrid.cs).
- Measured on PR `#1294` (`ExactWindingBenchmark`, 20k-triangle sphere, 64³ grid):
  - **Closed point queries:** `0.41 ms` exact vs. `2.10 ms` `FastWindingNumber` (**5.1x faster** and exact, with **1.8x faster** cache build and zero dipole error vs. `6.48e-2` FWN error).
  - **Unwelded closed STL soup point queries:** `0.40 ms` exact vs. `1.76 ms` FWN (**4.4x faster** and exact).
  - **Closed `MeshWindingNumberGrid` `FullGrid` 64³:** `9.2 ms` exact row-ray vs. `420.9 ms` FWN (**45.8x faster** and exact).
  - **Open 20k sphere (`|∂M| = 204`):** `5.34 ms` exact vs. `1.41 ms` FWN because `ExactWindingCache.boundary_sum_x` currently loops linearly in `O(|∂M|)`.
- Meanwhile, high-level callers still hardcode `FastWindingNumber` (Barill dipole approximation, `FWNBeta = 2`):
  - [`MeshWindingNumberSolidify.cs`](../../mesh_ops/MeshWindingNumberSolidify.cs) sets `mwn.UseFastWindingNumber = true` even for closed inputs (`45.8x` slower than exact row-ray at 64³).
  - [`WindingContouring.cs`](../../mesh_ops/WindingContouring.cs), [`WindingNumberOctree.cs`](../../mesh_ops/WindingNumberOctree.cs), [`MeshBatchQueries.cs`](../../spatial/MeshBatchQueries.cs), [`MeshDeviation.cs`](../../mesh_ops/MeshDeviation.cs), and [`RemoveOccludedTriangles.cs`](../../mesh_ops/RemoveOccludedTriangles.cs) all call `FastWindingNumber(p)` unconditionally, paying dipole cache build and near-surface approximation error (up to `1.15` in FEWN Table 1 near large triangles) even when `|∂M| == 0`.
- For open meshes with large `|∂M|` (e.g. `1,000–10,000` boundary edges on scanned terrains or open CAD sheets), the repo already proved in [EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md](../design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md) that hierarchical dipole clustering of exterior boundary edges cuts `6,007` boundary terms to `23` (at `theta = 0.25`) with a certified error bound.

## Trigger / decision needed

- **Prerequisite:** done; PR `#1294` merged on 2026-11-22.
- **Implementation:** open PR `#1298` carries Part A and Part B (certified `BoundaryTree` in `ExactWindingCache`); review and merge of `#1298` is what remains.
- **Part A (opt-in exact winding on callers):** in PR `#1298`; preserves default `FastWindingNumber` hashes (`SolidifyBenchmark`, `MeshRepairBenchmark`).
- **Part B (hierarchical boundary sum in `ExactWindingCache`):** Decide whether to expose (i) certified exact threshold query `IsInsideExactWinding(p, iso)` (bit-identical to exact `WindingNumber(p) > iso` via certified interval bound, reusing the proof in [EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md](../design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md)), and/or (ii) an opt-in approximate boundary tolerance `FastExactWinding` for large `|∂M|`.

## Goal

1. **Part A:** Expose opt-in exact winding mode on `WindingContouring`, `MeshWindingNumberSolidify`, `MeshBatchQueries`, and `MeshDeviation` so callers on closed and small-boundary meshes get PR `#1294`'s **5x–45x speedup** and exact winding without Barill dipole error (while keeping default FWN outputs bit-identical).
2. **Part B:** Accelerate open-mesh `ExactWindingCache` queries with large exterior boundaries (`|∂M| >= 500`) using a hierarchical boundary tree with certified bounds (and measure `#1009` `OuterFaces` `|∂M|` per round as a diagnostic).

## Scope

- `spatial/ExactWindingCache.cs` (hierarchical boundary clustering for certified threshold queries `EvaluateThreshold(p, iso)` and/or tolerance-bounded boundary sums when `|∂M|` is large; keep exact `Evaluate(p)` bit-identical)
- `mesh_ops/WindingContouring.cs`, `mesh_ops/WindingNumberOctree.cs`, `mesh_ops/MeshWindingNumberSolidify.cs` (opt-in `UseExactWinding` flag; `MeshWindingNumberSolidify` dense fallback uses exact row-ray grid when `UseExactWinding` is enabled)
- `spatial/MeshBatchQueries.cs` (optional `InsideMethod.ExactWindingNumber` or opt-in flag; note `InsideMethod.Crossings` already exists for closed meshes)
- `mesh_ops/MeshDeviation.cs` (opt-in exact winding sign)
- `tests/geometry3Sharp.Tests/` (parity, exactness near large triangles where FWN errs, closed/open/unwelded/soup tests, similarity invariance, reuse, determinism, mutation checks)
- `benchmarks/ExactWindingBenchmark/` and `benchmarks/SolidifyBenchmark/` (before/after numbers on closed, small-boundary, and large-boundary `|∂M| = 2,000–6,000` open meshes)

## Non-goals

- Do **not** alter default `FastWindingNumber` output hashes in `SolidifyBenchmark` or `MeshRepairBenchmark` without an explicit owner decision.
- Do **not** change `ExactMeshBoolean.cs` or `SolidBooleanPipeline.cs` (boolean speed work is stopped per [OPEN_TOPICS.md](../OPEN_TOPICS.md#4-parked-or-on-hold-by-owner-decision)).
- Do **not** read or port unlicensed external code (`BoundaryBlaze`).

## Interface

Additive, `1.0.324`-compatible opt-ins:
- `WindingContouring` / `MeshWindingNumberSolidify`: `public bool UseExactWinding = false;` (when `true`, uses `DMeshAABBTree3.WindingNumber` / `ExactWindingCache` and exact row-ray `MeshWindingNumberGrid` instead of `FastWindingNumber`).
- `MeshBatchQueries`: additive `InsideMethod.ExactWindingNumber` (works on closed, open, unwelded, and non-manifold inputs without FWN dipole error).
- `ExactWindingCache` (internal): certified hierarchical boundary threshold evaluation `EvaluateAbove(Vector3d p, double iso)` that falls back to the full exact boundary sum whenever `|w_h - iso| <= B_h`.

## Look before you build

1. Library: `spatial/ExactWindingCache.cs` (PR `#1294`), `mesh_ops/ExactMeshBoolean.cs` (`AntipodalTree` hierarchical boundary clustering), [EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md](../design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md), `mesh_ops/WindingContouring.cs`, `mesh_ops/WindingNumberOctree.cs`, `mesh_ops/MeshWindingNumberSolidify.cs`, `spatial/MeshBatchQueries.cs`, `mesh_ops/MeshDeviation.cs`.
2. Literature: Martens, Trettner & Bessmeltsev (SIGGRAPH / ACM TOG 2026, *The Antipodal Method*); Xie, Hafner & Wojtan (SIGGRAPH / ACM TOG 2026, *Fast and Exact Winding Numbers for Triangle Meshes*, paper only); Barill et al. (SIGGRAPH 2018, *Fast Winding Numbers for Soups and Clouds*).

## Steps (one PR per part, or clean commits in one PR after `#1294` merges)

1. **Part A (caller adoption):** Add opt-in `UseExactWinding` to `WindingContouring` / `WindingNumberOctree` / `MeshWindingNumberSolidify` / `MeshDeviation` and `InsideMethod.ExactWindingNumber` to `MeshBatchQueries`; benchmark closed + small-boundary solidify/contouring/batch workloads and verify default hashes are unchanged.
2. **Part B (hierarchical boundary sum):** Build a flat BVH over `ExactWindingCache.boundary[]` with certified dipole/solid-angle error bounds (following [EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md](../design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md)) for threshold queries (`> iso`) when `|∂M| >= 500`; verify bit-identical decisions against full exact `WindingNumber(p) > iso` on adversarial near-iso queries.
3. **Diagnostic (`#1009`):** Measure `|∂M|` across peeling rounds on `MeshFidelityMetrics.OuterFaces` procedural/repro shapes to report whether hierarchical exact winding is a viable owner option for `#1009`.

## Acceptance

- Default modes (`UseExactWinding = false`, `InsideMethod.WindingNumber`) stay bit-identical to `SolidifyBenchmark` and existing test hashes.
- Closed 20k `MeshWindingNumberSolidify` dense grid & `WindingContouring` with `UseExactWinding = true`: **>= 4x faster** than FWN on closed inputs, zero dipole error on large-triangle test cases where FWN fails (`|w_FWN - w_exact| > 0.2`), closed/manifold/finite outputs.
- Open 50k mesh with `|∂M| >= 2,000` (e.g. open heightfield/shell): certified hierarchical threshold queries match exact `WindingNumber(p) > 0.5` on **100%** of tested points (including points within `1e-9` of `0.5`) and achieve **>= 5x** speedup over linear `O(|∂M|)` boundary summation.
- Full local CI green (`dotnet pack` `1.0.324` API validation, `net48` build, tests <= 5 s).

## Sources and licences

- Martens et al. (2026, MIT / CC BY 4.0), Xie et al. (2026, paper CC BY 4.0 only — never read unlicensed repo), Barill et al. (2018), internal design note [EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md](../design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md).

## Budget and stop rule

1 M session for Part A; 1 M/L session (with bounds review) for Part B. Stop Part B if hierarchical boundary clustering yields `< 3x` speedup on `|∂M| = 2,000` open meshes after including tree build time.

## Risks and open questions

- **Bounds soundness in Part B:** Any floating-point error bound `B_h` used to certify `w > iso` must include rounding error (not only truncation error) so it falls back to the exact sum near `iso`; requires a bounds review (Tier L) just like [EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md](../design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md).
- **Soup inputs (`|∂M| = Theta(F)`):** Callers enabling `UseExactWinding` on disconnected triangle soups will hit `ExactWindingCache`'s direct-sum fallback (`O(F)` per query); document clearly that `FastWindingNumber` remains the right choice for fragmented triangle soups.
