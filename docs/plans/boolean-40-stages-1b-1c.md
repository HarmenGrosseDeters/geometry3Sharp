# Plan: Exact Boolean 4.0 stages 1b and 1c

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Boolean: Exact Boolean 4.0 stages 1b and 1c"
- **Kind:** parked (on hold, owner 2026-11-18; trigger: a concrete use case for chained exact expressions / provenance)
- **Tier:** L (1b alters `SafeRounding` outputs -> owner go + rounding shards; 1c touches `mesh_ops/ExactMesh*` exact rays -> exact review + mutation gate)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Design analysis (stage 0, #1093) and Stage 1 (lazy `g3.ExactMesh` in `mesh_ops/LazyExactMesh.cs`, #1107) are merged
  ([EXACT_BOOLEAN_40_DESIGN.md](../design/EXACT_BOOLEAN_40_DESIGN.md) §§6, 8).
- Stage 1 builds an n-ary CSG expression tree over double meshes and evaluates `Round()` in a single exact pass (built-in
  shapes keep their cull; arbitrary trees compile to a postfix `WindingPredicate`).
- **Two known gaps of Stage 1** deferred to stages 1b and 1c (both on hold, owner 2026-11-18):
  - **Stage 1b:** on contact chains of 5+ steps (`mixed` family), one-pass `Round()` is less often clean than stepwise
    chaining (21 newly unclean of 108) because thin slabs accumulate before repair; prerequisite is thin-slab removal
    before `SafeRounding` (RC4a lever, respecting RC1: never empty a non-empty result).
  - **Stage 1c:** generalized operation-aware culling (Rossignac & Voelcker 1989 active zone) so an early `Intersection`
    or `Difference` shrinks the one-pass arrangement just as stepwise chaining does (without it, `iud` k=2 p90 time
    ratio is 9.1x and `mixed` p90 is up to 3.1x).

## Trigger / decision needed

Owner lifts the hold once a caller actively uses `g3.ExactMesh` on 5+ step contact chains (trigger for 1b) or
intersection-heavy CSG trees (trigger for 1c). For 1b, owner must also approve changing `SafeRounding` outputs.

## Goal

Close both Stage 1 gaps in two separate PRs: (1b) match or beat chained cleanliness on long mixed contact chains, and
(1c) bring order-balanced one-pass time to median <= 1x and p90 <= 2x of stepwise chaining per k with identical results.

## Scope

- **PR 1 (Stage 1b):** `mesh_ops/MeshRoundingRepair.cs` (thin-slab removal before repair, respecting RC1),
  `tests/geometry3Sharp.Tests/LazyExactMeshTests.cs`, `tests/geometry3Sharp.Tests/SafeRounding*Tests.cs`,
  `docs/design/EXACT_BOOLEAN_40_DESIGN.md`.
- **PR 2 (Stage 1c):** `mesh_ops/LazyExactMesh.cs` (active-zone box/expression analysis),
  `mesh_ops/ExactMeshBoolean.cs` / `mesh_ops/ExactMeshArrangement.cs` (generalized `cull_outside` using exact rays),
  `tests/geometry3Sharp.Tests/LazyExactMeshTests.cs`.

## Non-goals

No materialized provenance (stages 2-4 remain no-go); no API break on `ExactMesh` or `ExactMeshBoolean`; stage 1c must
produce results **identical** with and without the cull.

## Interface

No public API change required; optional internal/diagnostic toggle for 1c (`CullActiveZone`) to test equality with and
without culling.

## Look before you build

1. Library: `mesh_ops/LazyExactMesh.cs`, `ExactMeshBoolean.CullOutsideA` / `cull_outside` (T3a #612), RC4a (#1040,
   #1068) and RC1 (#1027) in `MeshRoundingRepair.cs`, `benchmarks/BooleanComparison` (`chain_exact.py`).
2. Literature: Rossignac & Voelcker (IBM J. Res. Dev. 1989, "Active zones in CSG for accelerating boundary evaluation,
   redundancy elimination, interference detection, and shading algorithms"); Cherchi et al. (2022) n-ary arrangements.

## Steps (one PR per stage, clean commits)

- **Stage 1b:**
  1. Re-run `chain_exact.py proc --families mixed` on current master to refresh the 21/108 baseline after #1040/#1178.
  2. Prototype thin-slab pre-removal with RC1 guard; measure on `mixed`, `abc-chained`, and T6 (ensure 0 newly unclean).
  3. Add procedural tests (invariance, reuse, determinism, RC1 non-emptying), get owner sign-off on output delta, merge.
- **Stage 1c:**
  1. Derive active-zone bounding boxes and exact-ray pruning predicate for arbitrary postfix `WindingPredicate` trees.
  2. Implement generalized cull in `ExactMeshBoolean`; prove and test bit-identical output vs cull-off on all CSG shapes.
  3. Order-balanced A/B on `abc-chained` and procedural chains (`k = 1..8`).

## Acceptance

- **1b:** `mixed` chains one-pass clean >= chained clean; 0 newly unclean on `abc-chained` and T6; RC1 holds everywhere;
  rounding mutation shards (`rounding-1..13`) incremental gate green.
- **1c:** results identical with/without cull (hashes); order-balanced median one-pass / chained <= 1.0 and p90 <= 2.0
  per k; exact-code review + incremental mutation gate (`boolean-1..7`, `arrangement-1..4`) green.

## Sources and licences

Rossignac & Voelcker (1989) paper; no code ports.

## Budget and stop rule

1 M/L session per stage. Stop 1b if thin-slab removal introduces any newly unclean result on T6 or `abc-chained`. Stop
1c if active-zone rays cost more than the skipped arrangement work on > 25 % of `abc-chained` trees.

## Risks and open questions

- Check whether post-stage-0 fixes (#1040 RC4a, #1178 zero-volume sheets) already reduced the 21/108 gap before designing
  1b.
