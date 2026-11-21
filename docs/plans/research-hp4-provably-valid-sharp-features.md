# Plan: provably valid sharp-feature recovery on alpha wrap (HP 4 — research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Alpha wrap: provably valid sharp-feature recovery (HP 4)"
- **Kind:** research study (Pass K is opt-in without a recovery proof; trigger: need for a proven sharp-feature recovery guarantee for CAD wrapping / tet meshing)
- **Tier:** L (hybrid wrap + feature recovery proof: exact containment, manifoldness, no self-intersection, angle bounds; study first)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Hard problem 4 ([research.md HP 4](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)):
  - Opt-in `AlphaWrapFeaturePass` (pass K, #736) is merged and works well empirically (T1 KR edge F 0.61 -> 0.88,
    crease normal 14.7° -> 4.6°, concave within 1 offset 0.02 -> 0.75, 20/20 clean, 0 input crossings; CAD solids
    0.995 / 0.03° / 0.98) — **but** it is heuristic hill-climbing with no termination/recovery guarantee, and stays
    opt-in after the 800-mesh run (owner 2026-11-17).
  - Similarly, Tet P4(b) (`TetFeatureSurface`, #1196) recovers 15/15 procedural CAD solids and 19/24 Thingi10K meshes
    via flips + Steiner splits, falling back to S1-R when recovery does not finish (no guarantee at acute/re-entrant
    creases or 3-plane corners).
  - What remains open as a research problem: a **provably valid** feature recovery algorithm on top of the offset wrap
    (guaranteed manifold, intersection-free, enclosing input, with proven angle bounds and guaranteed recovery of
    creases/corners above a stated feature-angle / clearance threshold).

## Trigger / decision needed

A CAD / FEM requirement where Pass K's heuristic recovery + P4(b)'s fallback are insufficient and a **provable**
recovery guarantee (e.g. protecting balls / weighted Delaunay around 1D creases + 0D corners on the offset surface) is
required.

## Goal

Design a provably valid hybrid sharp-feature recovery method (protecting balls / weighted Delaunay on the offset
surface, or certified local dual-contouring patch stitched into the wrap) with stated input preconditions, termination
proof, and go/no-go complexity estimate before any implementation.

## Scope

- Phase 1 (study only): `docs/research/PROVABLE_SHARP_WRAP_STUDY.md` comparing:
  1. **Weighted Delaunay + protecting balls** on the offset surface's crease network (Cheng-Dey-Ramos / CGAL `Mesh_3`
     1D feature protection — papers only, never read CGAL code).
  2. **Hybrid offset-DC in crease regions stitched to alpha wrap** (using our intersection-free `DualContouring` #552/#572
     only on crease bands, with exact boolean/zipper stitching).
  3. **Pass K + provable local-alpha refinement** around acute creases.
- No production code in Phase 1.

## Non-goals

Never read or port CGAL (GPL) code; do not repeat the closed #498 uniform-DC-on-winding comparison; do not weaken
`AlphaWrap`'s strict input-enclosure guarantee.

## Interface

None (study only).

## Look before you build

1. Library: `mesh_ops/AlphaWrapFeaturePass.cs` (#736), `mesh_ops/TetFeatureSurface.cs` (#1196),
   `mesh_ops/MeshAlphaWrap.cs` (P4(a) local alpha), `mesh_generators/DualContouring.cs` (#552/#572),
   `benchmarks/SharpFeatureProbe/`.
2. Literature (papers only):
   - Cheng, Dey, Ramos (2007/2010) & Cheng, Dey, Shewchuk (2012, *Delaunay Mesh Generation*, Ch. 12-13: protecting balls
     and weighted Delaunay for piecewise smooth complexes with sharp creases).
   - Portaneri et al. (ACM TOG 2022, Alpha Wrapping with an Offset).
   - Ju et al. (2002) / Schaefer et al. (2007) / Ju & Udeshi (2006) intersection-free DC.
   - Botsch & Kobbelt (2001) / Attene et al. (2005) feature-sensitive remeshing.

## Steps (Phase 1 study, one docs PR)

1. Define the exact target theorem: given a piecewise-linear input (possibly dirty/open) and offset $\delta$, what is
   the exact offset crease/corner locus (mitered offset) and under what local-feature-size / crease-angle condition can
   a method guarantee recovery?
2. Analyze weighted Delaunay protecting balls on the mitered offset creases (checking exact weighted `InSphere`
   predicate cost in `tools/PredicateCompiler`) vs local-alpha + Pass K vs hybrid DC stitching.
3. Prototype the winning candidate on the 6 CAD solids + acute V-notch + 3-plane corner + the 5 Thingi10K fallbacks of
   P4(b) (scratch probe only) to measure recovery rate, worst angle, and time.
4. Write `docs/research/PROVABLE_SHARP_WRAP_STUDY.md` with proof sketch and owner go/no-go options.

## Acceptance

- Study states exact preconditions, theorem statement, failure modes (acute/slit creases < threshold), exact predicates
  needed, and a falsification table on the 6 CAD solids + 5 P4(b) fallback meshes.

## Sources and licences

Published papers only (Cheng-Dey-Shewchuk 2012, Portaneri et al. 2022, Ju & Udeshi 2006). Strictly no CGAL code.

## Budget and stop rule

1 L research session. Stop and recommend "keep Pass K + P4(b)" if protecting-ball radius collapses on noisy Thingi10K
inputs (turning into millions of balls) or requires > 2 months of new weighted-Delaunay infrastructure for marginal gain
over Pass K + corners.

## Risks and open questions

- On dirty/noisy triangle soups (Thingi10K), defining which edges are "true sharp features" vs scan noise is inherently
  ambiguous — protecting balls around noisy edges can cause catastrophic over-refinement unless gated by clean-CAD
  feature filtering.
