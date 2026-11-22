# Plan: certified medial axis and local feature size (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Priority:** 3 of 7 (decides a roadmap question of the thin-solid work)
- **Kind:** research study (study first, no library change)
- **Tier:** L (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

`MeshWallThickness`, the thin-solid rules of SafeRounding (#1216, #1421), alpha wrap (`MeshAlphaWrap`, `AlphaWrapOracle`), interval arithmetic in `implicit/IntervalImplicit.cs`. MinThickness is an owner decision; this study only measures.

## Goal

**Falsifiable question:** Can a certified lower bound on the local feature size replace the fixed thin-solid threshold of SafeRounding (#1216 option B) or the wrap offset, and at what cost on the procedural thin-solid fixtures of `MeshRoundingRepairTestsThinSolids`?

**Counts as a result:** a go / no-go with the measured cost, or a proof that the bound cannot be had at that cost.

Find what can be certified about the medial axis and the local feature size of a triangle mesh with interval or exact arithmetic (lower bounds on lfs, the lambda-medial axis as a stable subset), at what cost, and whether such a bound would make the thin-solid decisions of SafeRounding or the wrap offset provable.

## How to run this study (owner 2026-11-22)

- Problem first, not paper by paper: one PR for the whole plan, answering the falsifiable question above.
- Run the probe against the library's C# code. Python only for a reference value the library cannot compute.
- Search the library before building: do not re-measure what an existing class already gives (for example `SphericalFibonacciPointSet`, `MeshTopologySummary`).
- Stop rule: if the first probe finds no defect and decides nothing, write a one-paragraph stop note and stop; do not widen the study.

## Scope

- A result note `docs/research/CERTIFIED_MEDIAL_AXIS_STUDY.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Chazal, Lieutier 2005 (The lambda-medial axis); Amenta, Bern 1999 (Surface reconstruction by Voronoi filtering, lfs); Amenta, Choi, Kolluri 2001 (The power crust); Dey 2006 (Curve and Surface Reconstruction). Paper text only; power crust and Cocone code are GPL and never read. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/CERTIFIED_MEDIAL_AXIS_STUDY.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
