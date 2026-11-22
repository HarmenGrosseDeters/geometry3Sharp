# Plan: boolean speed work (#1064 levers, normal-cone certificate, ABC chain step 3 lead)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Boolean: all speed work (#1064 levers, normal-cone certificate, ABC chain step 3 lead)"
- **Kind:** parked (trigger needed: owner lifts the 2026-11-16 boolean speed stop)
- **Tier:** L (touches `mesh_ops/ExactMesh*` / exact predicates / error bounds: bounds review + incremental mutation gate)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): a study of the levers under the full guarantees is done ([EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md](../research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md): ranked list, measurement only, nothing scheduled); of this plan's leads it bounds lead 2 and leaves leads 1 and 3 unmeasured (section 8a). Levers 3x and 1a were stopped by the owner on 2026-11-22 (measured −2 % / 0 %, PR `#1323`). Still parked; new levers need the owner's go.

## Current state

- Exact Boolean 3.0 is complete (2026-11-18) and in **maintenance mode** (owner 2026-11-16): at 12 threads T6 is 1.43x
  CGAL summed (1.14x without rounding; 294/294 clean vs CGAL 49 unclean; 0.36x-0.46x Geogram). All boolean speed work
  is stopped until the owner gives a new go ([exact-boolean.md](../roadmap/exact-boolean.md#after-30-owner-2026-11-16)).
- Three parked leads grouped in this row:
  1. **#1064 levers:** #1064 is merged; on Thingi10K `42327` (dropped as a documented tail, #1047, owner 2026-11-19:
     union 18-26 s, max 20.02 s in 1 of 10 processes), two unbuilt levers remain: centroid without `AddPoint` /
     per-region stacks (~1.5 s) and an exact `PlaneSide` kernel.
  2. **Normal-cone patch certificate for the operand self-join** ([SELF_INTERSECTION_PATCH_CERTIFICATE.md](../design/SELF_INTERSECTION_PATCH_CERTIFICATE.md)):
     Volino & Magnenat-Thalmann (1994) normal-cone culling for self-intersections; estimated −7 to −10 % at 12 threads
     (unmeasured; at or below the 10 % stop bar), needs new exact error bounds, and the same certificate was slower in
     the Reducer.
     Bound 2026-11-22 ([speed levers study](../research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md) section 8a): on T6
     the self-join is 5665 ms per pass, of which 1399 ms are traversal, so a free certificate gives at most −18 % at 1
     thread and −11 % at 4 over the 294 booleans (seam union at most −11 % at 1 thread); verdict "parked" stands.
  3. **ABC chain step 3 lead** ([GEOGRAM_BOOLEAN_COMPARISON_2026-11.md](../research/GEOGRAM_BOOLEAN_COMPARISON_2026-11.md)):
     on step 3 of ABC chains (operands sharing A's surface), ours is 1.1-2.2x slower than Geogram and CGAL; undiagnosed.
     The 2026-11-22 study did not measure it (the ABC data was not fetched); its hypothesis (a step-3 operand pays the
     full self-join, trees and rounding again, as on the seam stratum) and first probe (`seam-levers --cases`) are in
     section 8a.

## Trigger / decision needed

Owner explicitly reopens boolean speed work (e.g., a workload or user benchmark where one of these three dominates) and
picks which lead gets an upper-bound probe first.

## Goal

Re-evaluate the three parked speed leads with upper-bound measurements first, and build only levers that are bit-identical,
never slower (normal stratum and tail separately, median of 5), and give a meaningful gain per exact-code review cost.

## Scope

- `mesh_ops/ExactMeshArrangement.cs`, `mesh_ops/ExactMeshBoolean.cs`
- `math/AdaptivePredicates.cs` / `tools/PredicateCompiler` (only if exact `PlaneSide` or normal-cone error bounds are built)
- `benchmarks/BooleanComparison/` (order-balanced A/B on T6, ABC chains, and `42327`)
- `tests/geometry3Sharp.Tests/ExactMesh*Tests.cs`

## Non-goals

No output change (bit for bit on T6, ABC chains, `42327`, and `ExactBooleanDeterminismTests`); no new kernel architecture
(EMBER / plane-based kernel is a decided no-go, 2026-11-19).

## Interface

None public (internal kernels / culling paths only).

## Look before you build

1. Library: `ExactMeshArrangement` (`TriangleBoxTree`, `CrossVisit`, `AddPoint`), `tools/PredicateCompiler`,
   `docs/design/SELF_INTERSECTION_PATCH_CERTIFICATE.md`, `docs/perf/EXACT_BOOLEAN_PERFORMANCE.md`.
2. Literature: Volino & Magnenat-Thalmann (CGF 1994, normal-cone self-intersection culling); Cherchi et al. (SIGGRAPH
   Asia 2020/2022, indirect predicates); Lévy (2024, exact mesh booleans).

## Steps (one PR per accepted lever, clean commits)

1. **Diagnosis / upper-bound probes (no library PR yet):**
   - ABC step 3: profile on 10 ABC step-3 pairs (`BooleanComparison`) to measure share of self-join vs coplanar vs
     classification vs rounding (state stratum and self-time vs GC).
   - Normal-cone: micro-probe upper bound of skippable self-join pairs on T6 at 12 threads.
   - #1064: measure `PlaneSide` + stack-centroid share on T6 normal stratum (not just `42327`).
2. Present upper bounds to the owner; proceed only on levers above cost/benefit bar.
3. Implement accepted lever (bit-identical), generate/review any new error bounds, run full local CI + A/B benchmark
   (normal + tail, median of 5) + independent exact-code review + incremental mutation gate.

## Acceptance

- Bit-identical outputs on 294/294 T6 operations and all ABC chain steps (hashes compared).
- Never slower on either normal stratum or slow tail (median of 5, order-balanced, command + raw output in PR).
- Exact-code review (`docs/REVIEW_CHECKLIST.md`, bounds checked) + incremental mutation gate on `arrangement-1..4`,
  `boolean-1..7` (and `exact-arithmetic` / `generated` if predicates change) above thresholds.

## Sources and licences

Volino & Magnenat-Thalmann (1994) paper; Cherchi et al. (2020/2022) papers; no GPL code.

## Budget and stop rule

Upper-bound probe: 1 short session. Stop rule (AGENT_WORKFLOW §8): drop any lever whose upper bound is below
measurement noise or dominated, or whose review + mutation cost outweighs the gain.

## Risks and open questions

- Coplanar / shared-surface cost on ABC step 3 may be inherent to exact triangulation of overlapping faces rather than a
  cullable overhead; the profile decides before any code is written.
