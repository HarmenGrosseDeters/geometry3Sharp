# Plan: biological TPMS structures as benchmark families (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Kind:** research study (study first, no library change)
- **Tier:** S/M (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

TPMS fields in `implicit/` and `tools/g3cli/TpmsCommand.cs`; wall thickness (`MeshWallThickness`); the transitions plan #1274 waits on the owner's PDFs and is not part of this study; the Lidinoid constant is an owner decision and not part of this study.

## Goal

Collect published, measured parameters of biological TPMS-like structures (gyroid photonic crystals in butterfly wing scales, trabecular bone porosity and strut thickness, cubic membranes in cells) and turn them into parameter ranges for procedural benchmark families: cell size, wall thickness, porosity, graded density. Measure which ranges our contouring and wall-thickness code handles and where it fails.

## Scope

- A result note `docs/research/TPMS_BIOLOGICAL_FAMILIES.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Saranathan et al. 2010 (Structure, function, and self-assembly of single network gyroid photonic crystals in butterfly wing scales, PNAS); Almsherqi et al. 2009 (Cubic membranes); bone morphometry reviews (open-access only). Numbers from paper text only; no images or data files committed. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/TPMS_BIOLOGICAL_FAMILIES.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
