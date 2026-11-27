# Plan: parameterization P1 collapse & virtual fan vertices (research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Geodesics/param: P1 collapse, virtual fan vertices"
- **Kind:** research study (documented limitation since 2026-11-12; trigger: a UV-unwrapping caller hitting P1 boundary/annulus collapse on real charts)
- **Tier:** M/L (new UV parameterization formulation for non-convex/annular charts; study first)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Recorded in [parameterization-geodesics.md](../roadmap/parameterization-geodesics.md#open) lines 50-53, 104 and
  [ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md):
  - **P1 collapse:** LSCM on certain disc/annular charts (specifically nearly symmetric annuli / high-curvature cones)
    collapses or folds triangles. LSCM near-collapse was fixed for the pinned defect in #1054 (issue #1049, 2026-11-17),
    while the general P1 collapse on symmetric annuli remains a documented limitation.
  - **Measured and ruled out as a P1 fix (2026-11-17):** `SpectralConformalParameterization` (#1057, opt-in for disc
    charts) removes P3 pin asymmetry to `1e-13` but does **not** fix P1 (its smallest non-trivial eigenvalue pair on
    nearly symmetric annuli is nearly double, causing folds; [GEOGRAM_MEASUREMENTS_2026-11.md](../research/GEOGRAM_MEASUREMENTS_2026-11.md)).
  - **Open research leads:**
    1. **Virtual fan vertices** (closing interior hole boundaries with virtual fan triangles / weighted cotan links
       during LSCM/conformal solve so annular/multi-boundary charts do not collapse across holes, then removing the
       virtual fans).
    2. **Foldover-free untangling** (`FoldoverFreeParameterization`, Garanzha et al. 2021, #374) combined with a free
       boundary (today fixed-boundary untangling cannot fix a self-crossing boundary polygon).

## Trigger / decision needed

A UV-atlas or parameterization caller encountering P1 collapse on real annular / multi-boundary charts where
`SpectralConformalParameterization` and `SLIM` / `BFF` do not resolve it.

## Goal

Study virtual-fan hole closure and free-boundary foldover-free untangling on the P1 reproducers and the UV-atlas
corpus, measuring foldover rate, distortion (symmetric Dirichlet), and wall time before proposing any code change.

## Scope

- Phase 1 (study only): `docs/research/PARAM_P1_VIRTUAL_FANS_STUDY.md`, scratch probe against `benchmarks/UVAtlasBenchmark/`
  and the P1 fixtures in `ISSUE_CASE_FINDINGS.md`.
- Phase 2 (only after owner go): opt-in mode in `mesh_ops/LSCMParameterization.cs` (or `UVAtlas`), tests in
  `tests/geometry3Sharp.Tests/Parameterization*Tests.cs`.

## Non-goals

Never change default LSCM / `UVAtlas` output without owner sign-off and atlas A/B; never port GPL parameterization
libraries (e.g. xatlas is MIT, Geogram is BSD-3 — check headers before reading; never read CGAL GPL code).

## Interface

Phase 1: none. Phase 2 (conditional): opt-in `FillHolesWithVirtualFans` (bool, default `false`) on LSCM / spectral
conformal parameterization.

## Look before you build

1. Library: `ISSUE_CASE_FINDINGS.md` (P1 case), `GEOGRAM_MEASUREMENTS_2026-11.md`, `mesh_ops/LSCMParameterization.cs`,
   `mesh_ops/SpectralConformalParameterization.cs` (#1057), `mesh_ops/FoldoverFreeParameterization.cs` (#374),
   `mesh_ops/SLIMParameterization.cs`, `mesh_ops/BFFParameterization.cs`, `benchmarks/UVAtlasBenchmark/`.
2. Literature: Lévy et al. (SIGGRAPH 2002, LSCM); Mullen et al. (SGP 2008, Spectral Conformal Parameterization — virtual
   boundary / hole handling); Sawhney & Crane (2017, BFF); Garanzha et al. (2021, Foldover-Free Maps); Rabinovich et al.
   (2017, SLIM).

## Steps (Phase 1 study, one docs PR)

1. Reproduce every P1 case (annulus, nearly symmetric annulus, multi-hole disc) and measure current foldover / flip
   counts across LSCM, Spectral Conformal, BFF, SLIM, and `FoldoverFreeParameterization`.
2. Prototype virtual fan vertices (adding a virtual centroid per hole loop with uniform vs mean-value vs cotan weights,
   solving LSCM/SCP, stripping virtual fans, optionally following with SLIM) on the P1 cases + `UVAtlasBenchmark`.
3. Evaluate edge cases: non-planar holes, nested holes, high-valence holes, and similarity invariance (`Similarity.All()`).
4. Commit `docs/research/PARAM_P1_VIRTUAL_FANS_STUDY.md` with before/after distortion & flip table and go/no-go verdict.

## Acceptance

- Study includes reproducible command + table (flipped triangles, symmetric Dirichlet energy, boundary self-intersections,
  time) comparing baseline vs virtual fans across P1 cases and the UV-atlas corpus.

## Sources and licences

Published papers only (Lévy 2002, Mullen 2008, Sawhney & Crane 2017, Garanzha 2021, Rabinovich 2017). No GPL code.

## Budget and stop rule

1 M/L research session for Phase 1. Stop if virtual fans eliminate < 80 % of P1 folds or introduce boundary overlaps on
non-convex holes in the UV-atlas corpus.

## Risks and open questions

- A virtual fan over a non-convex hole in 3D can have negative cotan weights, which breaks SPD/harmonic injectivity:
  positive weights (Tutte/mean-value/Floater) on virtual fan edges may be required and must be tested explicitly.
