# Plan: open-operand boolean speed & Lévy-style propagation (HP 8 — research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Boolean: open-operand speed, Lévy-style propagation (HP 8)"
- **Kind:** research study (correctness fixed in #528; speed parked under 2026-11-16 boolean speed stop; study / design review first)
- **Tier:** L (`mesh_ops/ExactMeshBoolean.cs`, `ExactMeshArrangement.cs`, Antipodal/GWN classification: exact review + mutation gate)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Hard problem 8 ([research.md HP 8](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)):
  - **Correctness is fixed (#528, owner 2026-11-10):** open operands are classified per piece; Antipodal Method exact
    ray parity (#539), per-patch evaluation L1 (#633), T5 PR 2 (#675), Antipodal open inputs up to 20 000 boundary
    segments (#766), and hierarchical boundary sum (#813) are merged.
  - **Remaining problem (speed only):** the open-operand cohort historically had 736 operations > 5 s; Lévy 2024-style
    label propagation across intersection curves (phase 1 #428 deferred propagation; phase 2 parallel fast-winding build
    merged in #501) was never built.
  - **Parked:** boolean speed work stopped (owner 2026-11-16); a stronger-model review of the open-operand
    classification design is the right first step if reopened.

## Trigger / decision needed

Owner lifts the boolean speed stop AND identifies an open-operand boolean workload where per-patch GWN / Antipodal
classification dominates wall time (> 20 % of boolean time after #813).

## Goal

Re-profile the open-operand cohort on current master (after #766 and #813) and design a sound label-propagation scheme
across non-boundary intersection edges (or prove why GWN continuity does / does not allow propagation without wrong
labels), before writing any library code.

## Scope

- Phase 1 (study & measurement only): profile open-operand cohort on current master (`benchmarks/BooleanComparison/`),
  write `docs/design/OPEN_OPERAND_CLASSIFICATION_REVIEW.md` (with exact soundness proof for any propagation rule).
- Phase 2 (only after owner go on Phase 1): `mesh_ops/ExactMeshBoolean.cs` (classification propagation), tests in
  `tests/geometry3Sharp.Tests/ExactMeshBoolean*Tests.cs` (bit-identical or provably equivalent classification on open
  operands).

## Non-goals

Never regress open-operand correctness (#528); never propagate integer labels across open boundary loops where GWN
varies continuously without a certified margin bound; never read `boundaryblaze` (all rights reserved) or GPL code.

## Interface

None public (internal classification pass in `ExactMeshBoolean`).

## Look before you build

1. Library: `ExactMeshBoolean.cs` (open-operand patch classification, #528, #539, #633, #766, #813),
   `docs/design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md`, `docs/design/INSIDE_OUTSIDE_OPEN_INPUTS.md`.
2. Literature: Lévy (2024/2025, exact mesh booleans — radial-fan propagation across intersection curves, Geogram BSD-3);
   Martens, Trettner, Bessmeltsev (ACM TOG 2026, Antipodal Method); Xie, Hafner, Wojtan (ACM TOG 2026, paper only —
   never read code); Jacobson et al. (2013, GWN jump conditions across oriented surface sheets).

## Steps (Phase 1 study, one docs PR)

1. **Refresh the baseline (upper bound first!):** measure how many of the 736 open-cohort ops still exceed 5 s *after*
   #766 and #813, and measure the exact share of GWN/Antipodal classification (self time vs GC). Stop here if #813
   already reduced classification below 15 %!
2. Analyze the mathematical condition for safe propagation on **open** operands: across an interior surface sheet
   (closed locally), GWN jumps by +/- 1, *but* away from the sheet GWN drifts smoothly due to boundary loops — derive
   a certified Lipschitz / solid-angle variation bound showing when a propagated label cannot cross 0.5, falling back
   to full evaluation when within the margin.
3. Produce `docs/design/OPEN_OPERAND_CLASSIFICATION_REVIEW.md` with upper bound, soundness proof, and go/no-go verdict.

## Acceptance

- Phase 1: refreshed cohort profile on master (command + raw output on `runs/`), explicit soundness argument for
  propagation on open surfaces, and quantified upper bound.
- Phase 2 (if approved): 0 classification mismatches vs current per-patch exact oracle across all open-cohort tests;
  >= 20 % wall-time reduction on open-operand tail, never slower on closed operands; exact-code review + incremental
  mutation gate (`boolean-1..7`) green.

## Sources and licences

Lévy (2024, Geogram BSD-3), Martens et al. (2026), Xie et al. (2026, **paper only — never read `boundaryblaze` code**),
Jacobson et al. (2013).

## Budget and stop rule

Phase 1: 1 L session. Stop immediately after step 1 if post-#813 open-operand classification is no longer a major
hotspot (< 15 % of open-cohort time) or if certified propagation margin covers < 30 % of patches.

## Risks and open questions

- On open meshes with large holes, GWN is not piecewise constant on cells separated by intersection curves (unlike
  closed meshes, where integer winding number is strictly constant per 3D cell): naive Lévy propagation would silently
  misclassify open operands, reintroducing the #528 bug!
