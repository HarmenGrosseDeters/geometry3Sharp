# Plan: Thingi10K 839690 at alpha / 20 — boundary change decision (owner decision)

- **Board row:** OPEN_TOPICS.md §2 "Waiting for an owner decision", row "Tet: 839690 at alpha / 20 needs a boundary change"
- **Kind:** owner decision
- **Tier:** S (if closed as resolved by P1 / known limit); M (if opt-in larger boundary slide cap is built)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Measured in [TET_MESHING.md §§16.3, 17.2, 17.8, 17.11](../design/TET_MESHING.md):
  - At the **old default** (`RelativeAlpha = 20`), Thingi10K `839690` stayed at **6.11°** worst dihedral (4.6 s) because
    it has 13 to 60 all-boundary wedges and needles in a thin part, pinned by the `0.5 * offset` boundary slide cap and
    having rings > 8 tets around their long edges.
  - At the **current default** (`RelativeAlpha = 40`, merged in P1 #1073, 2026-11-17), **839690 already reaches >= 10°
    in 0.68 s** (40 of 40 on the subset reach >= 10°; on the 500-mesh sample, 498 of 500 reach >= 10° after the rescue
    phase #1072, leaving only `38781` at 9.4° and `282598` at 9.5°, which are documented limits in §6).
  - Thus this decision only matters for callers who explicitly request coarse `RelativeAlpha = 20` (or who want a
    relaxable boundary slide cap for coarse wraps).

## Trigger / decision needed

Owner chooses between:

| Option | Effect | Cost / risk |
|---|---|---|
| A. **Close / keep 0.5-offset slide cap (leaning)** | P1 (`alpha / 40`) already solved 839690 (10°, 0.68 s); boundary stays strictly within the documented 0.5-offset cap of the peeled surface (§16.4) | Zero code cost; coarse `alpha / 20` calls on thin-part meshes like 839690 remain at ~6° (documented limit) |
| B. **Opt-in `MaxBoundarySlideOffsets` property (default 0.5)** | Lets callers who need coarse `alpha / 20` with higher dihedral angles relax the cap (e.g. 1.0 or 2.0 offsets), keeping exact `KeepClearOf` input non-crossing | Small M PR; relaxes geometric fidelity on boundary when opted in; still must be tested on 839690 to verify if 1.0-2.0 offsets actually lifts 6.11° -> 10° |
| C. **Allow boundary edge flips / cavity insertion in `TetMeshImprover` (Klingner & Shewchuk 2007)** | Modifies boundary topology on coarse wraps to eliminate all-boundary wedges | Medium-high complexity; changes wrap boundary topology and risks breaking boundary-marker mapping / surface fidelity |

How others solve it: Klingner & Shewchuk (IMR 2007, "Aggressive Tetrahedral Mesh Improvement") allow constrained
boundary vertex smoothing and boundary flips within a surface-deviation tolerance; fTetWild (Hu et al. 2020) slides and
remeshes boundary vertices freely inside an explicit epsilon envelope (`1e-3 * diag`); CGAL `Mesh_3` (`perturb_mesh_3`,
`exude_mesh_3`, Jamin et al. 2015) keeps boundary vertices constrained to the input oracle surface and relies on finer
sizing / sliver exudation rather than detaching from the surface; TetGen (Si 2015) keeps the input boundary fixed and
recommends finer surface discretization when boundary slivers occur.

## Goal

Record whether 839690 at coarse `alpha / 20` is closed by P1's `alpha / 40` default or gets an opt-in boundary slide
parameter.

## Scope

- Option A: `docs/OPEN_TOPICS.md` (close row or note under §6), `docs/design/TET_MESHING.md` §16.3 / §17.11.
- Option B: `mesh_ops/TetMeshImprover.cs`, `mesh_ops/MeshTetrahedralization.cs` (`MaxBoundarySlideOffsets`, default 0.5),
  `tests/geometry3Sharp.Tests/TetMeshingStage2Tests.cs`, `benchmarks/TetMeshBenchmark/`.

## Non-goals

Never allow a boundary vertex move to cross or touch the input mesh (`Reducer.KeepClearOf` / `TetrahedraMeet` remains
hard); never change default `0.5` output (bit-identical).

## Interface

Option B only: `MeshTetrahedralization.MaxBoundarySlideOffsets` (double, default `0.5`; `0` = no boundary sliding;
negative/NaN = invalid or off, consistent with P3 conventions).

## Look before you build

1. Library: `TetMeshImprover` boundary slide cap (§16.4), `Reducer.KeepClearOf`, `TetMeshingStage2Tests`,
   `TetRefinementWorkBoundTests` (bit-identical default pins).
2. Literature: Klingner & Shewchuk (IMR 2007); Freitag, Jones, Plassmann (IMR 1995, already used in rescue phase).

## Steps (one PR, clean commits)

1. Owner picks A, B, or C.
2. If A: record decision in `TET_MESHING.md` and remove row from `OPEN_TOPICS.md` §2.
3. If B: probe 839690 at `alpha / 20` with cap `1.0`, `1.5`, `2.0` offsets first (upper-bound check — if it does not
   reach 10°, stop and report); then wire option, pin default bit-identical, add tests (clamped slide, no input contact,
   invariance, determinism, reuse).

## Acceptance

- Option A: docs diff reviewed.
- Option B: before/after table on 839690 (`alpha / 20`) and the 40-mesh subset; default `0.5` bit-identical; new tests
  fail when the cap is ignored; 0 input crossings, 100 % exact tet validity.

## Sources and licences

Klingner & Shewchuk (2007) paper; no code ports.

## Budget and stop rule

Option A: 5 min. Option B: 15-min probe on 839690 first; if `MaxBoundarySlideOffsets = 2.0` still leaves 839690 below
9°, stop and report (needs topology change, not just slide cap).

## Risks and open questions

- Note that `839690` is already solved at the default `alpha / 40` — verify with the owner whether coarse `alpha / 20`
  is still a target use case at all.
