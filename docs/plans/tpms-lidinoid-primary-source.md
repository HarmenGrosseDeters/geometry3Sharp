# Plan: TPMS Lidinoid formula primary-source check

- **Board row:** OPEN_TOPICS.md §3 "Ready to start", row "Other: TPMS Lidinoid formula, primary-source check"
- **Kind:** ready to start (blocked only on obtaining the primary paper PDF / text)
- **Tier:** S (docs + verification only; M only if the constant / sign in `TpmsLattice.cs` actually differs and needs a fix)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- `mesh_generators/TpmsLattice.cs` lines 199-202 implement the nodal Lidinoid approximation (#835):
  ```csharp
  0.5 * (Math.Sin(2 * px) * cy * sz + Math.Sin(2 * py) * cz * sx + Math.Sin(2 * pz) * cx * sy)
      - 0.5 * (Math.Cos(2 * px) * Math.Cos(2 * py) + Math.Cos(2 * py) * Math.Cos(2 * pz) + Math.Cos(2 * pz) * Math.Cos(2 * px))
      + 0.15
  ```
  with Lipschitz bound `4 * sqrt(3)` (line 218) and max magnitude `3.15` (line 252).
- `docs/sources/meshing-and-repair.md` line 252 records the provenance gap:
  - S. Lidin & S. Larsson, *Bonnet transformation of infinite periodic minimal surfaces with hexagonal symmetry*,
    J. Chem. Soc., Faraday Trans. 86(5) (1990) 769, DOI `10.1039/FT9908600769` — bibliographic metadata checked against
    Crossref on 2026-11-15 (no end page in Crossref; earlier "769-775" unverified), **paper itself not opened**.
  - O. Al-Ketan & R. K. Abu Al-Rub, *Multifunctional mechanical metamaterials based on triply periodic minimal surface
    lattices*, Adv. Eng. Mater. 21(10) (2019) 1900524, DOI `10.1002/adem.201900524` — bibliographic metadata checked
    against Crossref, **paper itself not opened**.
  - The formula was cross-checked only against patent literature (`+0.15`) and other published implementations (`+0.3`
    without the `0.5` factors, same zero set).
- `tests/geometry3Sharp.Tests/TpmsLatticeTests.cs` tests chirality, central inversion symmetry (`f(-q) == f(q)`),
  Lipschitz bound `4`, max magnitude `3.15`, sheet/skeletal volume fraction and enclosure.

## Trigger / decision needed

Access to the primary paper(s) — either an open-access / preprint copy or a copy provided by the owner:
1. Lidin & Larsson (1990), DOI `10.1039/FT9908600769` (RSC paywalled; verify whether it defines the Lidinoid and gives
   the end page, noting Lidin 1990 actually studies the Bonnet transformation of the gyroid/Schwarz surfaces — also check
   S. Lidin, *Ring-like minimal surfaces*, J. Phys. France 51 (1990) 801-806 and M. Wohlgemuth et al., *Triply Periodic
   Bicontinuous Cubic Microdomain Morphologies by Symmetries*, Macromolecules 34(17) (2001) 6083-6089, which is the
   classic primary source for nodal level-set approximations of cubic TPMS including the Lidinoid).
2. Al-Ketan & Abu Al-Rub (2019), DOI `10.1002/adem.201900524` (verify the nodal equation and constant `+0.15` vs `+0.3`).

## Goal

Verify the exact primary source, page range, trigonometric terms and constant of the nodal Lidinoid equation and close
the unverified-attribution note in `docs/sources/meshing-and-repair.md`.

## Scope

- `docs/sources/meshing-and-repair.md` (and `docs/SOURCES.md` if referenced there)
- `docs/design/TPMS_LATTICE.md` (formula citation line)
- `docs/roadmap/research.md`, `docs/OPEN_TOPICS.md` (close row)
- Only if a discrepancy is found (and approved by owner, since changing the constant changes output):
  `mesh_generators/TpmsLattice.cs`, `tests/geometry3Sharp.Tests/TpmsLatticeTests.cs`

## Non-goals

No change to `TpmsLattice.cs` unless a primary source proves the current formula is wrong (and then only after owner go).
No changes to the other 5 TPMS types.

## Interface

None (docs-only unless a bug is found).

## Look before you build

1. Library: `mesh_generators/TpmsLattice.cs` lines 199-255, `tests/geometry3Sharp.Tests/TpmsLatticeTests.cs`,
   `docs/design/TPMS_LATTICE.md`, `docs/sources/meshing-and-repair.md`.
2. Literature to check (open-access / Crossref / institutional copy):
   - S. Lidin & S. Larsson (1990), *J. Chem. Soc., Faraday Trans.* 86(5), 769 (DOI `10.1039/FT9908600769`).
   - M. Wohlgemuth, N. Yufa, J. Hoffman, E. L. Thomas (2001), *Macromolecules* 34(17), 6083-6089 (DOI `10.1021/ma0019499`)
     — check whether the nodal Lidinoid form (`0.5*(sin2x cos y sin z + ...) - 0.5*(cos2x cos2y + ...) + 0.15`) originates
     here or in a later paper (e.g. Gandy & Klinowski / von Schnering & Nesper 1991).
   - O. Al-Ketan & R. K. Abu Al-Rub (2019), *Adv. Eng. Mater.* 21(10), 1900524 (DOI `10.1002/adem.201900524`).
   - Open-access TPMS reviews (e.g. Feng et al. 2022, *Materials & Design*; Maskery et al. 2018) to cross-check the sign
     and constant (`+0.15` for `f = 0` giving ~50 % volume fraction vs exact minimal surface area).

## Steps (one PR, clean commits)

1. Check open-access availability (arXiv, Europe PMC, PubMed Central, RSC/ACS author manuscripts) for Lidin & Larsson 1990,
   Wohlgemuth et al. 2001, and Al-Ketan & Abu Al-Rub 2019; if all are paywalled, request the PDFs from the owner.
2. Read the exact equations and page numbers in the primary source(s); verify terms, signs, `0.5` scaling, `+0.15`
   constant, and exact page range.
3. Update `docs/sources/meshing-and-repair.md` and `docs/design/TPMS_LATTICE.md` with verified primary citations and
   close the row in `docs/OPEN_TOPICS.md` and `docs/roadmap/research.md`.
4. If the formula differs from the primary source: stop and present the delta (volume fraction at `f = 0`, symmetry,
   visual/geometric impact) to the owner before touching `TpmsLattice.cs`.

## Acceptance

- `docs/sources/meshing-and-repair.md` records the verified primary source (exact authors, title, journal, pages, DOI,
  and equation number read directly from the paper).
- Docs-only PR: re-read diff, no CI required. (If `TpmsLattice.cs` changes: full local CI + `TpmsLatticeTests` +
  `TpmsCliTests`, plus updated `docs/UPSTREAM_CHANGES.md` / benchmark numbers.)

## Sources and licences

Academic papers only (formulas are mathematical facts, not copyrighted code). Cite every checked paper in
`docs/sources/meshing-and-repair.md`.

## Budget and stop rule

One short S session (< 30 min). If no open-access primary text is reachable, stop and ask the owner to supply the PDF(s)
rather than citing secondary summaries.

## Risks and open questions

- Lidin & Larsson (1990) introduced the Lidinoid via Weierstrass parameterization, likely *not* the trigonometric nodal
  approximation — the nodal form probably comes from Wohlgemuth et al. (2001) or later; splitting "discovery" vs "nodal
  approximation" citations cleanly will resolve the ambiguity.
