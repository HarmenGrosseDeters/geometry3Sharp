# Plan: guaranteed 3D rounding experiment (Devillers et al. 2020)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Rounding/repair: guaranteed rounding experiment (Devillers et al. 2020)"
- **Kind:** parked (not now, owner 2026-11-17; trigger: need for guaranteed rounding on real inputs beyond the five RC4b cases)
- **Tier:** L (exact 3D rounding theory, grid arithmetic, bounds review; phase 1 is a 2-3 day falsification spike only)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): the phase 1 spike is done as a note, not as `DEVILLERS_ROUNDING_EXPERIMENT.md`: [SNAP_ROUNDING_3D_GUARANTEE_2026-11.md](../research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md) (upper bounds measured in `benchmarks/SnapRoundingExperiment`; the known guarantee proves no new self-intersection and a bounded displacement, but not a closed manifold surface or that no solid is lost; leaning no build). The [#1216 study](../research/ROUNDING_THIN_SOLIDS_2026-11.md) adds that it would not keep the thin solids either (no-go). Owner decision 2026-11-22 ([ROADMAP.md](../ROADMAP.md), "Decided on 2026-11-22", "Stops"): snap rounding keeps SafeRounding (the per-result certificate documented); phase 2 is not built.

## Current state

- Prior-art check: [BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](../research/BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md) §2.3
  ([exact-boolean.md](../roadmap/exact-boolean.md#after-30-owner-2026-11-16)):
  - Devillers, Lazard, Lenhart (2020 / SoCG, 3D snap rounding with topological/embedding guarantee) fits our drift bound
    and degenerate test on a grid of about `2^-19` of the diagonal when the box of all parts is <= 1.36x the diagonal of
    the largest component and `max |x|` < ~`1e10` times that diagonal (**inference not yet proven**, §2.3 of the note).
  - Open questions: output triangle blow-up (worst-case Steiner count can be huge on nearly coplanar/grazing meshes) and
    whether a local-only scheme (applied only to the dirty cluster around rounded vertices, not the whole mesh) keeps
    the guarantee.
  - Owner 2026-11-17: **not now** — on current real data, `SafeRounding` fails only on the five RC4b cases (documented
    limit, drift bound `MaxLength`), so the practical upside is tiny unless a new corpus hits rounding failures broadly.

## Trigger / decision needed

Update 2026-11-22: phase 1 is done ([spike note](../research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md)) and the owner decided to keep SafeRounding (see the plan status). The original trigger:

A corpus or caller where `SafeRounding` fails on significantly more than the 5 RC4b cases, or a certification
requirement demanding a proven intersection-free double-output guarantee rather than best-effort `SafeRounding` +
`NotCleanReason`.

## Goal

Run a time-boxed (2-3 day) falsification experiment of a local Devillers et al. (2020) grid-rounding scheme on the 5
RC4b cases plus 10 hard rotated boolean outputs, measuring output triangle blow-up, Hausdorff drift, and wall time
before designing any library code.

## Scope

- Phase 1 (falsification spike, scratch / benchmark only — no library code changes):
  - Prototype in `benchmarks/BooleanComparison/` (or a scratch tool) implementing the grid subdivision and rounding rules
    of Devillers et al. (2020) on a local dirty cluster extracted from `ExactMeshArrangement`.
  - Report committed to `docs/research/DEVILLERS_ROUNDING_EXPERIMENT.md` (with raw tables on `runs/devillers-rounding`).
- Phase 2 (only if Phase 1 passes go/no-go and owner approves): opt-in fallback inside `mesh_ops/MeshRoundingRepair.cs`
  when `SafeRounding` hits `MaxLength` (RC4b).

## Non-goals

Do not replace `SafeRounding` on clean/normal outputs; do not port GPL code (CGAL); do not commit mesh files.

## Interface

Phase 1: none. Phase 2 (conditional): opt-in `RoundingRepairMode` or fallback flag on `MeshRoundingRepair`, default off.

## Look before you build

1. Library: `BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md` §2.3, `SNAP_ROUNDING_3D.md`, `ExactMeshArrangement` exact points,
   `SnapRounding2` (2D snap rounding already in `comp_geom/SnapRounding2.cs`), RC4b reproducers (`BOOLEAN_TRIAGE_2026-11.md`).
2. Literature: Devillers, Lazard, Lenhart (2018/2020, "3D Snap Rounding"); Valque, Lévy (2023/2024) & Milenkovic /
   Fortune (earlier 3D snap rounding bounds); Hobby (1999) / Hershberger (2013) 2D snap rounding.

## Steps (Phase 1 spike, one docs PR)

1. Formalize the §2.3 grid-size / box-ratio / coordinate-range inference and check whether any precondition breaks on
   the 5 RC4b cases and rotated UTM frames.
2. Implement a minimal exact-arithmetic prototype (using `BigRational` / `BigInteger` — speed secondary in falsification)
   on the 5 RC4b clusters + 10 rotated CAD cases.
3. Measure: (a) 0 self-intersections / 0 degenerates (`NotCleanReason`), (b) output triangle multiplier vs input cluster,
   (c) max Hausdorff drift vs `MaxLength`, (d) wall time.
4. Write `docs/research/DEVILLERS_ROUNDING_EXPERIMENT.md` with go/no-go verdict for the owner.

## Acceptance

- Phase 1: falsification report with exact commands, inputs, and table (clean count, triangle blow-up factor, drift,
  time) on all 5 RC4b cases + 10 rotated cases.
- Go criteria for Phase 2: 5/5 RC4b clean within drift bound, median triangle blow-up <= 3x on the dirty cluster, max
  blow-up <= 10x, and a proof sketch that localizing to the dirty cluster preserves the global no-crossing guarantee.

## Sources and licences

Devillers, Lazard, Lenhart (2020) paper only; never read or port CGAL code. Cite in `docs/sources/exact-booleans.md`.

## Budget and stop rule

Strictly 2-3 days for Phase 1. Immediate no-go if triangle blow-up exceeds 10x on a single RC4b cluster, if localization
breaks the topological guarantee, or if grid resolution violates `double` representability on UTM coordinates.

## Risks and open questions

- Localization is the core mathematical risk: rounding vertices in a dirty cluster can move incident triangles into
  previously clean neighbours, potentially cascading across the entire mesh.
