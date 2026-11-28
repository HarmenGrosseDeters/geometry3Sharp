# Plan: Multi-Material Cellular Complexes (`2B`) & Patch Cell-Graph DFS (`4A-1`) — Go/No-Go Spike `2B-0` First (Phase 3)

- **Umbrella study:** [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (Areas 2B & 4A-1)
- **Kind:** Phase 3 — Spike `2B-0` **PASSED (FULL GO with 2 locked design requirements, 2026-11-28)**; ready for formal `docs/design/EXACT_MESH_PARTITION.md` spec & Tier-L implementation
- **Tier:** **L** for the unified `4A-1` + `2B` implementation (touches `mesh_ops/ExactMesh*`, requires formal `docs/design/` spec, bounds/combinatorial review, and full mutation gate).
- **Plan status:** spike-verified (**FULL GO**), awaiting owner design-spec sign-off, 2026-11-28 (Berlin). See [EXACT_BOOLEAN_SPIKES_2026-11.md](../research/EXACT_BOOLEAN_SPIKES_2026-11.md).

## Current state (Post-Spike `2B-0`)

- **Why `4A-1` and `2B` are bundled together:**
  - `#1466` ([NARY_UNION_DENSE_2026-11.md](../research/NARY_UNION_DENSE_2026-11.md)) showed per-patch ray casting scales as $\approx 0.28 N k^3$ (64% of 4.6 s at 192 dense balls), and `abc-chained` Step 3 spends `15.4%` in `classification`. While `SolidBooleanPipeline.UnionMany` (`4A-2`) handles dense *pure unions*, pairwise trees are **mathematically invalid** for general n-ary predicates (`AtLeast(k)`, `XOR`), unrounded `ExactMesh`, and **conformal multi-material cellular partitions (`ExactMeshPartition`)**.
- **Spike `2B-0` Part A (`4A-1` Cell-Graph DFS on `dense104` 12..192 balls):**
  - **Exact bit-level agreement:** `mis = 0, unv = 0` across **all 861,508 pieces** at 192 balls!
  - **Iterative DFS itself takes only `50.9 ms`** (single `int[N]` state, zero $O(C \cdot N)$ memory).
  - Curve-level deduplication cut `radial_order` calls from `265,756 → 119,200` (`5,297 ms` vs. `13,574 ms` old winding = `2.6×` faster).
  - **Critical Design Fix #1:** Unfiltered `BigInteger` `ExactMeshBoolean.radial_order` on those 119k degree-4 transverse edges dominates (`5,246 ms`). Adding a **filtered `Orient3d` fast path for degree-4 transverse edges** (1 `Orient3d` for flat cuts; 4 consecutive `Orient3d > 0` for arbitrary non-flat creases; fallback to general `radial_order` for degree $\ge 5$ or ties) is mandatory to unlock the full ~20–30× speedup!
- **Spike `2B-0` Part B (`2B` Sheet-Decomposed Coupled Rounding — 9 adversarial fixtures × 6 frames = 54 cases, audited with exact `ExactTriangleIntersection`):**
  - **RAW double rounding:** `21 / 54` clean (`38.9%`).
  - **Policy R1 (pinned-seam per-sheet `MeshRoundingRepair` with `FixedHard = true`):** **STILL `21 / 54` (`38.9%`) — FAILS ALONE** because triple-junction defects sit at the seam vertices themselves!
  - **Policy R2 (coupled seam-vertex moves across all incident sheets):** **`48 / 48` (`100.0%`) CLEAN** on every resolvable 3-, 4-, and 8-material junction — including `0.1°` acute dihedral seams, `4mat_quad_spheres` (960 tris, 5 quad corners: `33d/69x → 0d/0x` in 41 ms), `3mat_nested_cut`, `8mat_dense_boxes` (33 sheets, 329 triple edges, 48 quad corners), and **rotated UTM `5.5e6`**! Overall: **`51 / 54` (`94.4%`)**.
  - **Critical Design Fix #2:** Diagnostic dumps of the **ONLY 3 leftovers** (`3mat_1ulp_coplanar@s2e-20`, `@s2e+20`, `@UTM_rot`) proved ALL 3 contain **`shared = 3` collapsed double-faces** (two distinct sheets sharing the exact same 3 vertex IDs from a 1-ulp slab collapsing to 0 thickness under double rounding). Combinatorially unresolvable by *any* vertex displacement; `ExactMeshPartition` must either collapse sub-ulp thin cells (`BelowResolution`, mirroring `MeshRoundingRepair`) or refuse honestly with `RoundingNotClean` + `shared=3` witness.

## Verdict: FULL GO (With 2 Locked Design Requirements)

Spike `2B-0` cleared the $\ge 98\%$ clean conformal bar on all resolvable junctions (**`48 / 48 = 100.0%`** under Policy R2) and verified `mis = 0, unv = 0` on 861,508 pieces for `4A-1`. Proceeding to formal `docs/design/EXACT_MESH_PARTITION.md` spec + Tier-L implementation is **GREENLIT** with:
1. **Filtered degree-4 `Orient3d` fast path** inside `4A-1` radial sorting (keeping full `radial_order` for degree $\ge 5$ or filtered ties).
2. **Policy R2 coupled seam-vertex moves + `shared=3` thin-cell collapse / `RoundingNotClean` refusal** inside `2B` Sheet-Decomposed Coupled Rounding (dropping R1-only).

## High-Level Umbrella Outline (Ready for `docs/design/EXACT_MESH_PARTITION.md`)

A formal `docs/design/EXACT_MESH_PARTITION.md` spec will be written and approved before code:
1. **Stage 3A (`4A-1` — Patch-Level Cell-Graph DFS Winding Propagation):**
   - Radial sort ONLY on patch-boundary edges (`ConstraintEdges` where $\ge 2$ patches meet) with curve-level deduplication + **filtered degree-4 `Orient3d` fast path** (fallback to `ExactMeshBoolean.radial_order` on degree $\ge 5$ or ties).
   - Zero-ray outer-hull anchor (max-X extreme vertex outside other components' AABBs) + 1 exact anchor ray per nested component.
   - Iterative deterministic DFS with a single `int[N]` state + $O(1)$ incremental `nonZeroCount` (zero $O(C \cdot N)$ memory!).
   - Activation gate (`closedMeshes.Count >= 3` and candidate rays above threshold) → `0%` overhead on binary booleans; bit-identical output on `ExactMeshBoolean`.
2. **Stage 3B (`2B` — `ExactMeshPartition`):**
   - Input: `IReadOnlyList<DMesh3> materials` + optional material precedence/priority rule for overlapping regions (`PriorityOrder`, `AllOverlapCombinations`, or custom `Func<int[], int>` cell classifier).
   - Output: exact cellular complex (list of cells with exact material ID & exact volume interval, list of oriented interface sheets `(cellFront, cellBack)`), plus conformal rounded export (per `2B-0` verdict) and per-material watertight `DMesh3` extraction.

## Non-goals (for Spike `2B-0`)

- No changes to `mesh_ops/` or `math/` committed during Spike `2B-0`.
- Do not rewrite `MeshRoundingRepair` on top of `NTMesh3`.

## Look before you build

1. Library: `mesh_ops/ExactMeshCells.cs`, `mesh_ops/ExactMeshBoolean.cs` (`radial_order`, `join_pieces`, `patch_winding`), `mesh_ops/MeshRoundingRepair.cs`, `mesh/NTMesh3.cs`.
2. Prior research & literature: [NARY_UNION_DENSE_2026-11.md](../research/NARY_UNION_DENSE_2026-11.md), [LEVY_CLASSIFICATION.md](../design/LEVY_CLASSIFICATION.md), Zhou et al. (SIGGRAPH 2016, *Mesh Arrangements for Solid Geometry*), Da et al. / Wojtan multi-material surface tracking (papers only; no GPL code).

## Acceptance (Spike `2B-0` — SATISFIED 2026-11-28)

- Research report and Z3 formal proofs committed at [EXACT_BOOLEAN_SPIKES_2026-11.md](../research/EXACT_BOOLEAN_SPIKES_2026-11.md) and `tools/Z3Pilot/boolean_next_phase_spikes.py`, with clean-rate table across 9 adversarial fixtures × 6 frames (54 cases, Policies R1 & R2), `4A-1` patch-boundary radial-sort timing on 192-ball `dense104`, and **FULL GO** recommendation with 2 locked design fixes.

## Sources and licences

Zhou et al. (2016) paper; internal `ExactMeshCells` / `MeshRoundingRepair`; no external code ports.

## Budget and stop rule

Spike `2B-0` complete (2026-11-28). Next step before code: owner review of [EXACT_BOOLEAN_SPIKES_2026-11.md](../research/EXACT_BOOLEAN_SPIKES_2026-11.md) and formal `docs/design/EXACT_MESH_PARTITION.md` specification.
