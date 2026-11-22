# Plan: practical 3D snap rounding with a guarantee (HP 1 — research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Rounding/repair: practical 3D snap rounding with a guarantee (HP 1)"
- **Kind:** research study (no known solution; trigger: a genuinely new mathematical / algorithmic lead beyond closed studies)
- **Tier:** L (exact 3D geometry proof, Z3 lemma checking, rounding semantics; study note first — no production code)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): the phase 1 study is done ([SNAP_ROUNDING_3D_GUARANTEE_2026-11.md](../research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md), measurement in `benchmarks/SnapRoundingExperiment`): the only known guarantee (Devillers, Lazard and Lenhart) does not give clean results or keep more solids; leaning no build. Only the owner decision remains; a new study only on a new lead.

## Current state

- Hard problem 1 ([research.md HP 1](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model),
  [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md), #721 closed 2026-11-15):
  - **Already tried & settled:**
    - `SafeRounding` + D1 (#513) + single-vertex defect-removing position search (#755): clean on nearly all real
      boolean outputs (only 5 RC4b cases remain).
    - Valque-Lazard option B: measured and closed (#313).
    - Empty-ball filter: closed (only 8 % of pair tests); directional separating-axis prefilter: −1.6 % real A/B, not built.
    - Plateau variant (chains of defect-neutral moves, checked with Z3 `tools/Z3Pilot/plateau_search.py`): **no-go**
      (owner 2026-11-15; fixed only 1 of 16 239 cohort ops).
    - Devillers et al. (2018/2020) local experiment: tracked separately in §4 (`repair-guaranteed-rounding-devillers-2020`,
      not now, owner 2026-11-17).
  - Literature consensus (e.g. Diazzi et al. 2023): practical 3D snap rounding with bounded drift, small output size,
    and guaranteed freedom from self-intersections remains an open problem in computational geometry.

## Trigger / decision needed

Update 2026-11-22: the study is done; what remains is the owner decision on the [research note](../research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md). The trigger for a further study:

A genuinely new published paper or theoretical lead (not repeating #313, #721, or Devillers 2020), plus owner approval
for a time-boxed study on a strong reasoning model.

## Goal

Evaluate any new theoretical lead for guaranteed 3D snap rounding via formal counterexample search (Z3) and a
falsification probe on the 5 RC4b cases + 19 synthetic leftovers before writing any library code.

## Scope

- Study only: `docs/research/SNAP_ROUNDING_3D_<LEAD>_STUDY.md`
- Z3 bounded-configuration lemma/counterexample scripts under `tools/Z3Pilot/` (following `plateau_search.py`)
- No library code changes during the study phase.

## Non-goals

Do not re-run closed experiments (Valque-Lazard #313, empty-ball / directional prefilter #721, plateau search #721);
never port GPL code.

## Interface

None (study only).

## Look before you build

1. Library: `docs/design/SNAP_ROUNDING_3D.md`, `docs/design/SAFE_ROUNDING.md` §9, `docs/research/BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md`,
   `tools/Z3Pilot/plateau_search.py`, RC4b reproducers (`BOOLEAN_TRIAGE_2026-11.md`).
2. Literature: Milenkovic & Sacks (2019), Devillers, Lazard, Lenhart (2018/2020), Valque & Lazard, Diazzi et al. (2023),
   new SoCG / SIGGRAPH / ACM TOG publications since 2025.

## Steps (study PR only, once a new lead exists)

1. Verify the new lead is distinct from every closed item in `SNAP_ROUNDING_3D.md` and `BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md`.
2. Encode the candidate rounding rule's local invariant in `tools/Z3Pilot/` and search for 2-3 triangle counterexamples
   (new crossing, inverted/degenerate face, drift bound violation, non-termination).
3. If Z3 finds no local counterexample, run a falsification prototype on the 5 RC4b cases + 19 generated leftovers,
   measuring clean rate, triangle blow-up, Hausdorff drift, and wall time.
4. Commit the study with explicit go/no-go recommendation and owner options.

## Acceptance

- Study document includes Z3 verification script + mutant checks (broken rule must be SAT), falsification table on the
  5 RC4b cases + 19 leftovers, and explicit comparison against Devillers (2020) and `SafeRounding`.
- Zero unbacked claims; every number names its command.

## Sources and licences

Published papers only; Z3 (MIT) in `tools/Z3Pilot/`. Never GPL code.

## Budget and stop rule

Time-boxed 1 L research session. Immediate no-go if Z3 finds a local counterexample that breaks the guarantee, if
triangle blow-up exceeds 10x, or if the new lead fixes fewer than 3 of the 5 RC4b cases.

## Risks and open questions

- A global termination / non-intersection proof over cascading 3D edits is beyond bounded Z3 checking and requires a
  human-reviewable mathematical proof before any Tier L implementation PR.
