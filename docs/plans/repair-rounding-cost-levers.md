# Plan: rounding cost levers (hard problem 3 b-d, #837 lever 2)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Rounding/repair: rounding cost levers (hard problem 3 b-d, #837 lever 2)"
- **Kind:** parked (not scheduled under the 2026-11-16 boolean speed stop; trigger: speed go from the owner)
- **Tier:** L (`mesh_ops/MeshRoundingRepair.cs` / `TriangleBoxTree`: exact-code review + incremental mutation gate on `rounding-1..13`)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): levers (b) to (d) are measured (owner go for a study, no library code): [ROUNDING_COST_2026-11.md](../research/ROUNDING_COST_2026-11.md) and, for lever (d) (= lever 1a), [EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md](../research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md) section 8a. Lever 1a was then built as a prototype and measured at 0 % on the seam union (PR `#1323`, section 9a of the speed levers note; prototype PR `#1322` closed); the owner stopped it on 2026-11-22 ([ROADMAP.md](../ROADMAP.md), "Decided on 2026-11-22", "Stops"). #837 lever 2 is not measured. Still parked under the speed stop; the options of the rounding cost note are with the owner.

## Current state

- Measured in [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md) (2026-11-12) and
  [research.md HP 3](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model):
  - Rounding is 1-69 % (median 23 %) of a boolean on 33 rotated operations (519511 union: 4.7 of 17.0 s); overall
    rounding is ~20 % of T6 at 12 threads.
  - Sub-linear rounding / certified clearances were studied and answered **no** (§§5.1-5.4).
  - Lever (a) is done (#725: passes stop proposing at the evaluation limit, rounding −33 %, booleans −9 %, 33/33 hashes
    identical). #837 lever 1 and `rebuild_tree` reuse were measured and dropped.
  - **Four parked levers remain:**
    - **(b)** Box-neighbour enumeration in the first search on results with long thin tilted triangles (20-355 box
      neighbours per query triangle; 59-98 % of rounding on clean large results `519511`, `1517923`, `668056`, 22-43 %
      on `1063860`) — tree quality / tighter bounds (e.g. OBB/k-DOP or SAH/split-on-long-triangles in `TriangleBoxTree`).
    - **(c)** Cost of `near` inside edit evaluations (14-56 % of rounding where edits are evaluated — undiagnosed).
    - **(d)** Tree build and setup over *all* triangles even when only a small fraction of vertices were rounded.
    - **#837 lever 2:** parked specifically for the rotated-CAD class.
  - **Measured 2026-11-22** ([ROUNDING_COST_2026-11.md](../research/ROUNDING_COST_2026-11.md), 33 rotated Thingi10K
    operations plus 15 procedural ones, 4 cores): (b) to (d) together are bounded by 16 % of the summed booleans on the 33
    operations (23 % on all 48). (b): the one candidate that pays is cached separating axes in the first search (−3.5 %
    of the 48 operations, 48 of 48 hashes the same); tighter volumes do not pay. (c): keeping the boxes of the linear
    list in `near` takes 17.7 % off the rounding of the 24 operations with many evaluations (about −3.3 % of the 48).
    (d): no lever beyond lever 1a (tree and setup 1.5-2.0 % of the sets). A slow box tree on model 102375 is a new
    finding (option 3 of the note, diagnosis first).
  - Tier correction from the speed levers study (section 8a): `MeshRoundingRepair.cs` is not in the exact-file list of
    `.editorconfig`, so a lever there alone is tier M with the rounding mutation shards; `TriangleBoxTree` (in
    `ExactMeshArrangement.cs`) stays exact code.

## Trigger / decision needed

Owner lifts the speed stop for rounding (e.g., rounding becomes the dominant bottleneck in a real workload or on a new
tail corpus). Pick one lever at a time in order of upper-bound gain per effort.

## Goal

Cut `SafeRounding` wall time on large clean results (b, d) and rotated dirty results (c, #837 lever 2) with bit-identical
outputs and zero regression on normal operations.

## Scope

- `mesh_ops/MeshRoundingRepair.cs` (`SafeRounding`, first search, `near` query, setup)
- `mesh_ops/ExactMeshArrangement.cs` (`TriangleBoxTree` — only for (b) if tree bounds/splitting improve without slowing
  arrangement)
- `tests/geometry3Sharp.Tests/SafeRounding*Tests.cs`, `Rounding*Tests.cs`
- `benchmarks/BooleanComparison/` (T6, 33 rotated ops, `519511`, `1517923`, `668056`, `1063860`)

## Non-goals

No output change (bit-identical hashes across all 33 rotated ops and 294 T6 ops); no change to drift bounds or
cleanliness guarantees; no plateau snap-rounding variant (no-go, 2026-11-15).

## Interface

None public (internal spatial queries / setup optimizations only).

## Look before you build

1. Library: `SNAP_ROUNDING_3D.md` §§5.1-5.5, #837 upper-bound measurements, `TriangleBoxTree`, `MeshRoundingRepair.cs`
   dirty tree (#397) and D1 (#513).
2. Literature: Stich, Friedrich, Dietrich (HPG 2009, "Spatial Splits in Bounding Volume Hierarchies" — SBVH reduces
   long-diagonal-triangle AABB overlap by 3-10x); Klosowski et al. (1998, k-DOPs for tight bounding of tilted faces).

## Steps (one PR per winning lever, clean commits)

1. **Upper-bound probes (no code PR yet):**
   - Measure (b): count candidate pairs eliminated by a 6-plane / 14-DOP or spatial split on `519511` / `1517923` /
     `668056` and compute max wall-time reduction.
   - Measure (c): profile `near` on the 33 rotated ops (self time vs GC/allocations; state stratum per AGENT_WORKFLOW §3).
   - Measure (d): measure share of full-mesh tree build on T6 ops where < 5 % of triangles touch rounded vertices.
2. Rank levers by gain / review effort; get owner go on the top lever.
3. Build bit-identical, verify hashes on T6 + 33 rotated ops + `Rounding*Tests`, run order-balanced median-of-5 A/B on
   both normal stratum and slow tail, independent review + incremental mutation gate (`rounding-1..13`).

## Acceptance

- 100 % bit-identical output hashes on T6 (294/294) and the 33 rotated operations.
- Never slower on normal stratum or slow tail (median of 5, command + raw output in PR); measurable gain (>= 10 % of
  rounding on the targeted class).
- Incremental mutation gate (`rounding-1..13`) and exact-code review green.

## Sources and licences

Stich et al. (2009), Klosowski et al. (1998) — papers only; no code ports.

## Budget and stop rule

Upper-bound probes: 0.5 day. Stop any lever whose measured upper bound is < 10 % of rounding time or whose build cost
regresses normal-stratum ops (AGENT_WORKFLOW §8 "never slower and gains somewhere").

## Risks and open questions

- Changing `TriangleBoxTree` for (b) affects `ExactMeshArrangement` and determinism stage 1 (`TriangleBoxTree` is in
  determinism scope!): keep any SBVH / k-DOP variant local to rounding or verify `BoxTreeParallelBuildTests` and
  `DeterminismStage1Tests`.
