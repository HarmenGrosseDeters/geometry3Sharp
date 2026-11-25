# Plan: EMBER lever 2 — skip self-pairs of certified operands in chained booleans

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Boolean: EMBER lever 2 (skip self-pairs in chains)"
- **Kind:** parked (owner 2026-11-16; trigger: a concrete caller that chains booleans and needs ~10-15 % more speed)
- **Tier:** L (touches `mesh_ops/ExactMeshArrangement.cs` / `ExactMeshBoolean.cs`: exact-code review + mutation gate)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): step 1 (the upper bound before any API code) is measured for T6-like steps and seam inputs, not on ABC chains at 12 threads ([EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md](../research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md) sections 5 and 8a; measurement only). Still parked under the boolean speed stop (owner 2026-11-16).

## Current state

- Measured in Probe A (#967, §7.1; [exact-boolean.md](../roadmap/exact-boolean.md#after-30-owner-2026-11-16)):
  - At 1 thread, operand self-joins take 10.6 s of the 12.1 s pairs phase; skipping them has an extrapolated upper bound
    of **about −15 %** of total boolean time at 12 threads on chained booleans, and **0 %** on single booleans of raw
    untrusted inputs (such as T6).
  - Re-confirmed in [PLANE_BASED_KERNEL_STUDY_2026-11.md](../research/PLANE_BASED_KERNEL_STUDY_2026-11.md) (2026-11-19):
    self-join is ~19 % of the boolean under the quality bar.
  - Safety invariant (owner 2026-11-16): the "self-intersection-free" trust flag must **never** come from an unverified
    caller bool — only from the library's own certified output (`NotCleanReason == None`) or an explicit prepared-operand
    handle produced by the library.
- **Measured 2026-11-22** ([EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md](../research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md),
  section 5): at most −24 % at 1 thread and −15 % at 4 for a T6-like step with two certified operands, −20 % / −9 % on
  the seam union, 0 on raw single booleans; the number of pieces is the same with and without the self-join on all 101
  clean pairs; a content stamp costs 0.8 % (64-bit mix) to 2.0 % (SHA-256) of the self-join, so the content-hash variant
  is affordable and no defensive copy of the mesh is needed. Not measured: ABC chains at 12 threads.

## Trigger / decision needed

A real workflow or caller chaining many sequential `ExactMeshBoolean` operations (where `g3.ExactMesh` single-pass is
not applicable, e.g., interactive step-by-step CAD editing) plus owner lifting the boolean speed stop.

## Goal

Skip the operand self-intersection join inside `ExactMeshArrangement` for library-certified self-intersection-free
operands, yielding bit-identical results and <= −10 % wall time on chained booleans.

## Scope

- New immutable/stamped wrapper or internal certificate token (e.g. `CertifiedSolidOperand` or `PreparedBooleanOperand`,
  verifying `NotCleanReason(mesh, false) == None` and storing a content hash or owning an immutable copy so caller
  mutation cannot spoof it).
- `mesh_ops/ExactMeshArrangement.cs` (skip `sourceA == sourceB` pair tests for certified operands).
- `mesh_ops/ExactMeshBoolean.cs` (overload / property accepting certified operands, plus optional export of a certified
  result when `RoundingClean` and `NotCleanReason == None`).
- `tests/geometry3Sharp.Tests/ExactMeshBoolean*Tests.cs` (equality bit for bit with uncertified path; mutation-tamper
  safety; invariance; determinism; reuse).
- `benchmarks/BooleanComparison/` (ABC chain A/B at 1, 4, 12 threads).

## Non-goals

Never accept an unverified `bool AssumeClean` parameter from callers; no change to single-boolean default path (bit for
bit identical); no EMBER octree/BSP kernel rewrite (shelved/no-go).

## Interface

Proposed additive API:
- `public sealed class PreparedBooleanMesh` (in `g3`): created via `PreparedBooleanMesh.TryPrepare(DMesh3 mesh, ...)`
  (copies mesh and certifies `NotCleanReason == None` and closed/manifold) or returned by `ExactMeshBoolean.ToPreparedResult()`
  when clean.
- `ExactMeshBoolean` accepts `PreparedBooleanMesh` operands (reusing the existing arrangement with self-pairs skipped for
  each prepared input).

## Look before you build

1. Library: `ExactMeshArrangement.CrossVisit` / pair-finding phase, `ExactMeshBoolean.NotCleanReason`,
   `MeshRepairMetrics`, Probe A (#967), `LazyExactMesh` (#1107 — compare first: single-pass `ExactMesh` may already
   dominate for non-interactive chains).
2. Literature: Trettner, Nehring-Wirxel, Kobbelt (ACM TOG 2022, EMBER: skipping intra-operand pairs for valid solids);
   Cherchi et al. (2022).

## Steps (one PR, clean commits)

1. Measure actual 12-thread ABC-chain upper bound (skip self-pairs on steps >= 2 in a scratch probe, order-balanced,
   median of 5) before writing API code.
2. Design `PreparedBooleanMesh` (immutable defensive copy so caller edits cannot invalidate the certificate) and wire
   self-pair skipping into `ExactMeshArrangement`.
3. Add tests: bit-identical output vs raw `DMesh3` across ABC-like chains, failure when preparing a self-intersecting
   mesh, invariance (`Similarity.All()`), determinism, reuse; check a hand mutant (skip flag ignored -> benchmark/counter
   test fails; skip applied to dirty mesh -> rejected).

## Acceptance

- Bit-identical outputs (mesh hashes) on all chained operations vs uncertified path.
- Order-balanced A/B on `abc-chained` at 1 and 4 (and 12 if owner runs) threads: never slower on any step; >= 10 % gain
  on chained steps >= 2 (command + output in PR).
- Exact-code review + incremental mutation gate (`arrangement-1..4`, `boolean-1..7`) above thresholds.

## Sources and licences

Trettner et al. (2022) paper (approach only; no GPL/non-permissive code).

## Budget and stop rule

Upper-bound probe: 1 h. Stop if measured 4/12-thread chain gain is < 8 % (below effort threshold for exact code) or if
copying the mesh for tamper-proofing eats > 30 % of the savings.

## Risks and open questions

- Compare against `LazyExactMesh` (#1107): if interactive callers can re-evaluate an `ExactMesh` tree instead of
  chaining rounded doubles, EMBER lever 2 is unnecessary.
