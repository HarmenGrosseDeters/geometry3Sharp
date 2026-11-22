# Plan: molecular surfaces (union of balls, solvent-excluded surface) as a stress family (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Kind:** research study (study first, no library change)
- **Tier:** M/L (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

Exact Boolean (`mesh_ops/ExactMeshBoolean.cs`), `SolidBooleanPipeline`, contouring (`MarchingCubes`, `DualContouring`, `WindingContouring`), alpha wrap. Many overlapping spheres with near-tangent contacts are not a test family today.

## Goal

Build a procedural generator of molecule-like ball sets (random packings with realistic radii and overlaps, chains, helices) and measure three routes to the union and to the solvent-excluded surface: chained exact Boolean of sphere meshes, contouring of the exact signed distance of the union (power diagram), and wrap. Report correctness against the analytic volume and area of the union where known, degeneracies hit (near-tangent spheres, coplanar circle arcs), and time. Propose a test family for the PR tier (<= 5 s) and one for the nightly tier.

## Scope

- A result note `docs/research/MOLECULAR_SURFACES_STUDY.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Connolly 1983 (Analytical molecular surface calculation); Edelsbrunner 1995 (The union of balls and its dual shape); Edelsbrunner, Mücke 1994 (Three-dimensional alpha shapes); Sanner, Olson, Spehner 1996 (MSMS, paper only; its code has a restrictive licence). Real PDB structures only fetched at run time on a runner, never committed; the PR tier uses procedural sets only. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/MOLECULAR_SURFACES_STUDY.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
