# Plan: contouring topology certificate — thin features and options B / C (research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Other: contouring certificate, thin features and options B / C"
- **Kind:** research study (Stage 3 built; rest stopped by stop rule #944; trigger: reviewed mathematical proof before any build)
- **Tier:** L (certified topology / isotopic isosurfacing proof, interval arithmetic / Plantinga-Vegter / Boissonnat-Oudot; proof review first)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Contouring topology certificate Option A stages 1-3 are merged (#749, #779, #925, #935, #946;
  [CONTOURING_TOPOLOGY.md](../design/CONTOURING_TOPOLOGY.md)):
  - Certified runs on T1 clean at `/128`: **4 -> 10 of 14** (at `/64`: 2 -> 12 of 14), CAD solids 6/6, **0 wrong and
    certified** (never falsely certifies a topologically wrong mesh).
  - Certificate overhead: 25-28 % of contouring time at `/128` (33-37 % at `/64`).
  - Left uncertified on clean T1 at `/128`: `104640` (a thin tube — the one right run left uncertified), plus the 3
    genuinely wrong runs (`116871`, `472011`, `79961`).
- Issue #944 closed (2026-11-17): adaptive refinement for thin features and Options B / C were **stopped by the stop
  rule**; no code may be built without a reviewed mathematical proof first.

## Trigger / decision needed

A caller requiring `WindingContouring` / `DualContouring` as a **100 % topology-certified repair path** on thin tubes /
thin walls (like `104640` where Option A stage 3 leaves a tube uncertified) AND a reviewed mathematical proof for
Option B (local adaptive refinement guided by uncertified blocks with isotopic termination) or Option C (certified
Delaunay surface meshing after Boissonnat & Oudot 2005).

## Goal

Produce a mathematically reviewed proof and feasibility study for certifying thin features (resolving `104640`-class
tubes and refining the wrong runs `116871`, `472011`, `79961` until certified or explicitly hitting a resolution floor)
under Option A+refinement, Option B, or Option C.

## Scope

- Phase 1 (proof & study only — zero production code): append a new section to `docs/design/CONTOURING_TOPOLOGY.md` (or
  new `docs/research/CONTOURING_THIN_FEATURES_PROOF.md`) containing:
  1. Complete isotopic-equivalence proof for adaptive octree subdivision of uncertified blocks (handling hanging nodes /
     T-junction transitions between refined and coarse cells without breaking the block-boundary sign/topology theorem!).
  2. Comparison with Option B (interval-arithmetic Plantinga & Vegter 2004 isotopic meshing on octrees) and Option C
     (Boissonnat & Oudot 2005 restricted Delaunay surface meshing).
  3. Failure modes on non-smooth / sharp / zero-thickness inputs where no finite resolution can satisfy a smooth-level-set
     gradient condition.

## Non-goals

Never weaken the soundness of Stage 1-3 (`wrong and certified` must stay strictly **0**); never read or port CGAL
`Surface_mesher` (GPL — Boissonnat & Oudot papers only); never write production code before the proof passes independent
review.

## Interface

None (proof / study only).

## Look before you build

1. Library: `docs/design/CONTOURING_TOPOLOGY.md` (stages 1-3, options A/B/C, block-growth proof),
   `mesh_ops/WindingContouring.cs`, `mesh_generators/DualContouring.cs`, `benchmarks/ContouringCertificate/`.
2. Literature (papers only):
   - Plantinga & Vegter (SGP 2004 / J. Symbolic Computation 2007), "Isotopic Approximation of Implicit Curves and
     Surfaces" (interval-arithmetic gradient/sign test on balanced octrees).
   - Boissonnat & Oudot (SGP 2005), "Provably Good Sampling and Meshing of Surfaces" (loose ε-sample / restricted
     Delaunay with topological guarantees).
   - Dey & Levine (2008), "DelIso: Guaranteed Quality Isotopic Isosurface Meshing".
   - Snyder (SIGGRAPH 1992), interval analysis for computer graphics.

## Steps (Phase 1 proof & study PR)

1. Diagnose `104640` (thin tube at `/128`) and the 3 wrong runs (`116871`, `472011`, `79961`) to determine the exact
   reason each fails Stage 3 certification (resolution too coarse for thin sheet vs non-ball handle inside a single
   cell vs winding-number gradient oscillation near sharp creases).
2. Write the formal proof for Option A + local subdivision (proving that refining only uncertified blocks to depth
   $d + k$ with 2:1 balancing and valid transition faces yields a global ambient isotopy when every refined block
   certifies).
3. Compare cost and coverage against Option B (Plantinga-Vegter) and Option C (Boissonnat-Oudot) on sharp/piecewise-linear
   winding fields (note: Plantinga-Vegter assumes $C^1$ non-zero gradient intervals, which fail at sharp polyhedral
   edges/corners!).
4. Submit proof for independent mathematical review before proposing any code PR.

## Acceptance

- Reviewed proof document with explicit hypotheses (smooth vs piecewise-linear winding field), counterexamples where
  hypotheses fail (sharp creases / zero-thickness sheets), and a concrete go/no-go decision table for Options A+refine,
  B, and C.

## Sources and licences

Plantinga & Vegter (2004/2007), Boissonnat & Oudot (2005), Dey & Levine (2008) — papers only. Strictly no CGAL code.

## Budget and stop rule

1 L research session. Stop and close as "Stage 3 is the practical limit for piecewise-linear inputs" if sharp polyhedral
edges inherently violate the gradient-interval hypothesis of Options B/C on > 50 % of CAD/Thingi10K meshes.

## Risks and open questions

- Winding-number fields of triangle meshes are harmonic away from the mesh but discontinuous across the mesh itself:
  standard smooth-SDF isotopic theorems (Plantinga-Vegter) do not directly apply on the surface without smoothing or
  exact geometric-cell intersection reasoning.
