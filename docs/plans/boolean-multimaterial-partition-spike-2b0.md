# Plan: Multi-Material Cellular Complexes (`2B`) & Patch Cell-Graph DFS (`4A-1`) — Go/No-Go Spike `2B-0` First (Phase 3)

- **Umbrella study:** [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (Areas 2B & 4A-1)
- **Kind:** Phase 3 — **MANDATORY Go/No-Go Spike `2B-0` first** (2–3 days). Full Tier-L implementation of `4A-1` + `2B` is explicitly **blocked on Spike `2B-0`'s verdict**.
- **Tier:** S/M for Spike `2B-0` (isolated benchmark/probe, no library edits); **L** for the subsequent unified `4A-1` + `2B` implementation (touches `mesh_ops/ExactMesh*`, requires formal `docs/design/` spec, bounds/combinatorial review, and full mutation gate).
- **Plan status:** draft, 2026-11-28 (Berlin).

## Current state

- **Why `4A-1` and `2B` are bundled together:**
  - `#1466` ([NARY_UNION_DENSE_2026-11.md](../research/NARY_UNION_DENSE_2026-11.md)) showed that per-patch ray casting (`patch_winding` → `front_crossings`) scales as $\approx 0.28 N k^3$ (64% of 4.6 s at 192 dense balls). While `SolidBooleanPipeline.UnionMany` (`4A-2`) handles dense *pure unions*, pairwise trees are **mathematically invalid** for general n-ary predicates (`AtLeast(k)`, `XOR`), unrounded `ExactMesh`, and **conformal multi-material cellular partitions (`ExactMeshPartition`)** where interior material interfaces MUST be kept.
  - A single **Patch-Level Cell-Graph DFS engine (`4A-1`)** using existing exact `ExactMeshBoolean.radial_order` / `ExactMeshCells` (operating on patches, 0–1 anchor ray per connected component, $O(1)$ incremental `int[N]` state via DFS backtracking — zero $O(C \cdot N)$ memory!) solves **both** `#1466` (bit-identical) and `2B`'s exact multi-material cell labelling in one Tier-L build.
- **The #1 technical risk (why Spike `2B-0` is mandatory):**
  - Unrounded exact cellular partition ($E_s$ + exact per-material volume intervals) is 100% conceptually airtight.
  - **Rounded `double` conformal multi-material output is hard:** `MeshRoundingRepair` operates on 2-manifold `DMesh3` and cannot be rewritten onto `NTMesh3` at reasonable cost. Our proposed solution is **Sheet-Decomposed Coupled Rounding** (pin or jointly coordinate triple-junction / quadruple-corner seam vertices across incident 2-manifold material sheets, while repairing sheet interiors with `MeshRoundingRepair`), **but its clean-rate on acute triple junctions and rotated UTM frames is unmeasured!**

## Goal (Spike `2B-0` — Executable Now)

Empirically measure whether **Sheet-Decomposed Coupled Rounding** produces clean, watertight, conformal, self-intersection-free `double` multi-material complexes across adversarial 3- and 4-material junctions — **before** committing Tier-L effort to `ExactMeshPartition`.

## Scope of Spike `2B-0` (Probe Only — Zero Library Changes)

- Isolated procedural probe in `benchmarks/` (or throwaway test harness, reverted or kept in `benchmarks/` with results logged in `docs/research/MULTIMATERIAL_ROUNDING_SPIKE_2026-11.md`):
  - Generate procedural multi-material test corpus (all 10 frames: identity, generic rotation, `2^20`, `2^-20`, UTM `1e7`, rotated UTM):
    1. **3-material triple junctions:** three overlapping boxes/cylinders/spheres meeting along a shared curve, including **acute dihedral junctions** ($5^\circ, 1^\circ, 0.1^\circ$) and **1-ulp offset coplanar faces**.
    2. **4-material quadruple corners:** 4 overlapping solids meeting at a point (Voronoi-like vertex, 2×2×2 staggered blocks, nested concentric shells with a cutting wedge).
    3. **Dense 8-material assembly:** overlapping CAD primitives with flush interfaces and thin inter-material slabs.
  - Prototype **Sheet-Decomposed Coupled Rounding**:
    - Extract exact unrounded interface sheets $S_{(m_a, m_b)}$ between adjacent material pairs `(m_a, m_b)` from `ExactMeshArrangement` + exact radial order.
    - Identify seam vertices (junction degree $\ge 3$ sheets) and evaluate two rounding policies:
      - **Policy R1 (Pinned seams):** keep rounded seam vertices fixed (or softly coupled in a single pass), run `MeshRoundingRepair` on each 2-manifold sheet respecting pinned boundary seams, then check global cross-sheet self-intersections.
      - **Policy R2 (Coupled seam move):** allow a joint displacement of a seam vertex simultaneously across all incident sheets (checked against all incident sheets' exact triangles) when R1 leaves a degenerate/crossing triangle at an acute junction.
  - Measure: clean conformal rate (`0` cross-sheet intersections, `0` degenerate triangles, exact shared seam vertices across all incident sheets), max displacement, wall time, and failure modes.

## Go / No-Go Gate After Spike `2B-0`

- **Full Go (build `4A-1` + `2B` with rounded conformal `NTMesh3` / per-material `DMesh3` output):** Policy R1 or R2 achieves $\ge 98\%$ clean conformal output on non-degenerate 3- and 4-material junctions (including rotated & UTM frames) with honest refusal (`RoundingNotClean` + witness) on sub-ulp acute wedges.
- **Scoped Go (build `4A-1` + unrounded exact `ExactMeshPartition` + per-material independent rounded solids, NO coupled non-manifold rounding):** If triple-junction rounding stalls on acute angles, ship the 100% airtight exact unrounded partition + exact per-material volume intervals + `4A-1` cell-graph DFS, and defer conformal rounded `double` non-manifold output (or route FEM consumers to `MeshTetrahedralization` directly from exact planes/sheets).
- **Stop / Park:** If even `4A-1` patch-boundary radial sorting adds unexpected overhead on dense n-ary inputs (verify `4A-1` radial-sort timing ceiling on 192-ball `dense104` during the same spike!).

## High-Level Umbrella Outline (Blocked on Spike `2B-0` Verdict)

If `2B-0` yields Go, a formal `docs/design/EXACT_MESH_PARTITION.md` spec will be written and approved before code:
1. **Stage 3A (`4A-1` — Patch-Level Cell-Graph DFS Winding Propagation):**
   - Radial sort ONLY on patch-boundary edges (`ConstraintEdges` where $\ge 2$ patches meet) via existing `ExactMeshBoolean.radial_order` / `ExactMeshCells`.
   - Zero-ray outer-hull anchor (max-X extreme vertex outside other components' AABBs) + 1 exact anchor ray per nested component.
   - Iterative deterministic DFS with a single `int[N]` state + $O(1)$ incremental `nonZeroCount` (zero $O(C \cdot N)$ memory!).
   - Activation gate (`closedMeshes.Count >= 3` and candidate rays above threshold) → `0%` overhead on binary booleans; bit-identical output on `ExactMeshBoolean` (cuts 192-ball `dense104` winding from 2,942 ms toward ~50–120 ms).
2. **Stage 3B (`2B` — `ExactMeshPartition`):**
   - Input: `IReadOnlyList<DMesh3> materials` + optional material precedence/priority rule for overlapping regions (`PriorityOrder`, `AllOverlapCombinations`, or custom `Func<int[], int>` cell classifier).
   - Output: exact cellular complex (list of cells with exact material ID & exact volume interval, list of oriented interface sheets `(cellFront, cellBack)`), plus conformal rounded export (per `2B-0` verdict) and per-material watertight `DMesh3` extraction.

## Non-goals (for Spike `2B-0`)

- No changes to `mesh_ops/` or `math/` committed during Spike `2B-0`.
- Do not rewrite `MeshRoundingRepair` on top of `NTMesh3`.

## Look before you build

1. Library: `mesh_ops/ExactMeshCells.cs`, `mesh_ops/ExactMeshBoolean.cs` (`radial_order`, `join_pieces`, `patch_winding`), `mesh_ops/MeshRoundingRepair.cs`, `mesh/NTMesh3.cs`.
2. Prior research & literature: [NARY_UNION_DENSE_2026-11.md](../research/NARY_UNION_DENSE_2026-11.md), [LEVY_CLASSIFICATION.md](../design/LEVY_CLASSIFICATION.md), Zhou et al. (SIGGRAPH 2016, *Mesh Arrangements for Solid Geometry*), Da et al. / Wojtan multi-material surface tracking (papers only; no GPL code).

## Acceptance (for Spike `2B-0`)

- Research report committed at `docs/research/MULTIMATERIAL_ROUNDING_SPIKE_2026-11.md` with reproducible commands, clean-rate table across all 3 fixture families × 10 frames (Policies R1 & R2), `4A-1` patch-boundary radial-sort timing on 192-ball `dense104`, and clear Go / Scoped-Go / Stop recommendation for the owner.

## Sources and licences

Zhou et al. (2016) paper; internal `ExactMeshCells` / `MeshRoundingRepair`; no external code ports.

## Budget and stop rule

Spike `2B-0`: 2–3 days max. Do not expand into full `ExactMeshPartition` implementation until the owner reviews the `2B-0` report and chooses Full Go vs. Scoped Go.
