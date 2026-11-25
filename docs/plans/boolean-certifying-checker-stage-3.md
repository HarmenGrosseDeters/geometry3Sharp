# Plan: certifying boolean checker stage 3, certified user mode, exact coplanar clip coordinates

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Boolean: certifying checker stage 3, certified user mode, exact coplanar clip coordinates"
- **Kind:** parked (not scheduled; trigger: a caller requiring runtime-certified exact boolean output or exact 2D coplanar clip coordinates)
- **Tier:** L (exact predicates, `mesh_ops/ExactMesh*`, certification invariants: exact-code review + mutation gate)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): a spike answered the plan ([BOOLEAN_CERTIFIED_MODE_2026-11.md](../research/BOOLEAN_CERTIFIED_MODE_2026-11.md)); owner decision 2026-11-22 ([ROADMAP.md](../ROADMAP.md), "Decided on 2026-11-22"): options B, A and D1, C parked. Item 1 (stage 3, test-only embedding check C2) is merged (PR `#1359`); item 3 is closed by option D1 for the boolean and the 3D query ([exact-boolean.md](../roadmap/exact-boolean.md), "Exact coplanar triangle intersection"; an exact 2D clip, options D2 / D3, has no caller). Only item 2, the certified user mode (option C), remains, parked until a caller needs it.

## Current state

- Prior-art study: [BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](../research/BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md) (2026-11-17).
- **Stages 1-2 merged (#1061, [QA.md 2.4a](../QA.md)):** test-only certifying checker covering C0 (inputs and culling),
  C1 (closedness), C3 (classification), C4 (selection), and C5 (coverage); active as the correctness oracle for the
  boolean property campaigns and the Nightly corpus.
- **Not scheduled / parked** ([exact-boolean.md](../roadmap/exact-boolean.md#after-30-owner-2026-11-16)):
  1. **Stage 3 (C2a):** an independent exact recheck of the embedding (no non-shared intersections between output
     arrangement triangles, independent of the arrangement's own subdivision code).
  2. **Certified user mode:** opt-in public flag on `ExactMeshBoolean` running C0-C5 + C2a and returning a certified
     certificate/status object for downstream safety-critical / CAD-verification callers.
  3. **Exact output coordinates of coplanar 2D clipping:** exposing exact coordinates (rather than rounded doubles) from
     the coplanar 2D clipping path (currently parked with Exact Boolean 4.0 provenance).

## Trigger / decision needed

Update 2026-11-22: stage 3 and the coplanar item are done (see the plan status); the trigger below now applies to the certified user mode only.

A concrete caller / domain requirement for runtime-certified boolean outputs (e.g. regulatory/FEM certification) or
exact coplanar clip coordinates. Treat item (3) as a separate PR from (1)+(2).

## Goal

(A) Complete the independent certifying checker with embedding check C2a and expose an opt-in certified mode on
`ExactMeshBoolean`, and (B) if triggered separately, expose exact coordinates from coplanar 2D clipping.

## Scope

- **PR A (Stage 3 + certified user mode):**
  - Existing checker file(s) from #1061 (check `mesh_ops/ExactMeshBooleanCertifier.cs` or `tests/` location before coding)
  - `mesh_ops/ExactMeshBoolean.cs` (opt-in `CertifyResult` flag + `CertificationReport` output, off by default)
  - `tests/geometry3Sharp.Tests/ExactBooleanCertifierTests.cs` (negative tests injecting synthetic embedding crossings,
    wrong labels, dropped faces — every injected bug must be caught!)
- **PR B (exact coplanar clip coordinates, separate trigger):**
  - Coplanar 2D clip path in `mesh_ops/ExactMeshArrangement.cs` / `ExactTriangulation2.cs`, additive API exposing exact
    point representations (`FixedPoint3` / `ExactImplicitPoint`).

## Non-goals

Default `ExactMeshBoolean` path must remain untouched (0 % overhead when `CertifyResult == false`, bit-identical
outputs); never share the arrangement's own intersection code inside C2a (oracle independence,
`docs/design/ACCEPTANCE_STRATEGY.md`).

## Interface

Proposed opt-in API (PR A):
- `ExactMeshBoolean.CertifyExactResult` (bool, default `false`)
- `ExactMeshBoolean.Certificate` (`BooleanCertificate?`: flags `C0`, `C1`, `C2a`, `C3`, `C4`, `C5`, `AllPassed`,
  first failing witness id/reason)

## Look before you build

1. Library: #1061 certifier (`C0, C1, C3, C4, C5`), `ExactTriangleIntersection`, `TriangleBoxTree` (or an independent
   uniform grid / brute-force small-mesh oracle for C2a), `docs/design/ACCEPTANCE_STRATEGY.md`.
2. Literature: McConnell, Mehlhorn, Näher, Schweitzer (CSR 2011, "Certifying Algorithms"); Mehlhorn et al. (LEDA /
   certifying planar geometry — approach only).

## Steps (one PR for A, clean commits)

1. Measure C2a cost on T6 and procedural meshes using an independent exact triangle-triangle crossing check on the
   exact arrangement triangles (not the rounded doubles).
2. Implement C2a in the certifier and add fault-injection tests (swap a vertex, flip a face, drop a triangle, introduce
   a crossing — C2a / C1-C5 must catch every one).
3. Wire opt-in `CertifyExactResult` into `ExactMeshBoolean` (off = bit-identical, zero overhead); test invariance,
   determinism, reuse.
4. Run Nightly corpus with C2a enabled once to confirm 0 false positives.

## Acceptance

- Fault-injection suite: 100 % of synthetic embedding defects caught by C2a; 0 false alarms on T6 (294/294) and the
  property campaigns.
- Off-path bit-identical (`CertifyExactResult = false` adds 0 allocations / 0 wall time).
- Overhead of `CertifyExactResult = true` measured on T6 (before/after table + command in PR).
- Exact-code review + incremental mutation gate green.

## Sources and licences

McConnell et al. (2011) paper; no GPL code.

## Budget and stop rule

1 L session for PR A. Stop if C2a takes > 3x the boolean itself on T6 without a clean spatial-index speedup (report
overhead to the owner before exposing as user mode).

## Risks and open questions

- Independence of C2a: reusing `TriangleBoxTree` is acceptable only if the tree's conservatism is independently verified
  (e.g. exact AABB containment check per triangle), or use a separate simple grid in the certifier.
