# Plan: TPMS hybrid transitions keeping wall thickness (Ren 2021, Gao 2022)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Other: TPMS transitions keeping wall thickness (Ren 2021, Gao 2022)"
- **Kind:** parked ("later", owner 2026-11-15; trigger: owner supplies the Elsevier papers Ren et al. 2021 & Gao et al. 2022 + additive-manufacturing use case)
- **Tier:** M (new TPMS blend / transition formulas + Lipschitz / conservative-MC enclosure bound derivation + tests)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- `mesh_generators/TpmsLattice.cs` supports 6 TPMS types (Gyroid, Schwarz P/D, Neovius, I-WP, Lidinoid), sheet /
  skeletal styles, `Enclosure` mode (#750, #833, #838), graded cell size (`Grade` / `PhaseMap`, #860), CLI/MCP `tpms`
  (#879), and **two-type hybrid across a plane** (`SecondType`, `Blend`, `SigmoidBlend`, #900/#907/#918, from Fang et
  al. 2026 open-access Sci. Rep. 16:17947, CC BY 4.0).
- Parked follow-ups from [research.md](../roadmap/research.md) lines 182-184 and
  [docs/sources/meshing-and-repair.md](../sources/meshing-and-repair.md) line 258:
  1. **Wall-thickness-preserving transition** between two TPMS types (naive sigmoid blend `w*f1 + (1-w)*f2` thins or
     pinches sheet walls inside the transition band because `|∇(w f1 + (1-w) f2)|` drops where gradients cancel).
     References cited by Fang et al. (2026) but paywalled (Elsevier, not yet read):
     - Ren et al. (2021), *Materials & Design* 210, 110062.
     - Gao et al. (2022), *Materials & Design* 221, 110995.
  2. **Checkerboard weight** and **hybrid of different periods** (both dropped from the first hybrid PR #907).

## Trigger / decision needed

1. Owner provides PDF copies of Ren et al. (2021) and Gao et al. (2022) (or approves an alternative open-access
   gradient-normalized SDF transition formulation).
2. A concrete additive-manufacturing / lattice caller needing constant wall thickness across hybrid transitions or
   multi-period / checkerboard hybrids.

## Goal

Add an opt-in wall-thickness-preserving transition mode (and optionally checkerboard / multi-period hybrid support) to
`TpmsLattice`, with a proven Lipschitz bound so `Enclosure` mode remains rigorously conservative.

## Scope

- `docs/design/TPMS_LATTICE.md` (new §10: transition formula, Lipschitz bound derivation, worst-case enclosure analysis)
- `mesh_generators/TpmsLattice.cs` (additive opt-in transition mode / weights; existing `SigmoidBlend` bit-identical)
- `tools/g3cli/` (expose opt-in CLI/MCP flag if promoted)
- `tests/geometry3Sharp.Tests/TpmsLatticeTests.cs` (wall-thickness uniformity test across the transition zone,
  Lipschitz bound numerical check, worst-case conservative-MC enclosure test, invariance, determinism, reuse)
- `benchmarks/TpmsLattice/`, `docs/sources/meshing-and-repair.md`

## Non-goals

Never break existing `SigmoidBlend` or single-type `TpmsLattice` outputs (bit-identical when new options are off);
never weaken the conservative-MC `Enclosure` guarantee.

## Interface

Proposed additive opt-in on `TpmsLattice`:
- `TpmsTransitionMode` enum (`Standard` = existing default, `ConstantWallThickness`) or gradient-normalized style blend;
- Optional `SecondPeriod` (default 0 = match `Period`) and `CheckerboardBlend` weight helper.

## Look before you build

1. Library: `mesh_generators/TpmsLattice.cs` (lines 193-260, existing `SigmoidBlend` + Lipschitz bounds),
   `docs/design/TPMS_LATTICE.md` §9, `docs/design/CONSERVATIVE_MC_AUDIT.md`, `TpmsLatticeTests.cs`.
2. Literature:
   - Ren et al. (2021), *Mater. Des.* 210, 110062 (paywalled — provide before build).
   - Gao et al. (2022), *Mater. Des.* 221, 110995 (paywalled — provide before build).
   - Fang et al. (2026), *Sci. Rep.* 16, 17947 (CC BY 4.0, already read).
   - Taubin (1994) / signed-distance gradient normalization `f / |∇f|` as standard open fallback if needed.

## Steps (one PR, clean commits)

1. Read Ren et al. (2021) and Gao et al. (2022); document the exact equations and derive a sound upper bound on `|∇g|`
   for conservative Marching Cubes (`Enclosure` mode) in `docs/design/TPMS_LATTICE.md` §10.
2. Implement opt-in transition mode (and multi-period / checkerboard if approved) in `TpmsLattice.cs`.
3. Measure actual minimum/maximum sheet wall thickness across the transition plane on all 15 unordered pairs of the 6
   TPMS types vs `Standard` sigmoid blend.
4. Add unit tests (wall thickness within target band, no disconnected pinches, conservative `Enclosure` holds,
   `Similarity.All()`, determinism, reuse, hand mutants).

## Acceptance

- Existing `Standard` hybrid outputs bit-identical.
- Wall thickness across the transition zone stays within +/- 15 % of target (before/after table on Gyroid-IWP,
  Gyroid-SchwarzP, SchwarzD-Lidinoid in `TPMS_LATTICE.md` and PR).
- Conservative `Enclosure` worst-case test passes (exact containment of the true level-set solid); all outputs closed,
  manifold, finite; every test <= 5 s.

## Sources and licences

Academic papers only (formulas are mathematical facts; no external code ported). Record exact verified citations in
`docs/sources/meshing-and-repair.md`.

## Budget and stop rule

1 M session once PDFs are supplied. Stop if no closed-form Lipschitz bound exists for the paper's formula (either bound
`1 / |∇f|` with a regularizer `sqrt(|∇f|^2 + eps^2)` or report to owner that `Enclosure` is incompatible with that mode).

## Risks and open questions

- Gradient normalization `f / sqrt(|∇f|^2 + eps^2)` can create sharp kinks near saddle points (`∇f = 0`) where `|∇g|`
  spikes, inflating the conservative-MC padding: derive and measure the padding blow-up before coding.
