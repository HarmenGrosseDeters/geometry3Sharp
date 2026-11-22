# Plan: molecular surfaces (union of balls, solvent-excluded surface) as a stress family (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Priority:** 4 of 7 (conditional: only worth it if it breaks our operators)
- **Kind:** research study (study first, no library change)
- **Tier:** M/L (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

Exact Boolean (`mesh_ops/ExactMeshBoolean.cs`), `SolidBooleanPipeline`, contouring (`MarchingCubes`, `DualContouring`, `WindingContouring`), alpha wrap. Many overlapping spheres with near-tangent contacts are not a test family today.

## Goal

**Falsifiable question:** Do molecule-like ball sets (near-tangent spheres, coplanar circle arcs) make `ExactMeshBoolean`, `SolidBooleanPipeline`, contouring or `MeshAlphaWrap` fail, refuse wrongly or fall off a performance cliff?

**Counts as a result:** an issue per failure with a minimal procedural repro; a literature summary without a run against the library does not count.

Build a procedural generator of molecule-like ball sets (random packings with realistic radii and overlaps, chains, helices) and measure three routes to the union and to the solvent-excluded surface: chained exact Boolean of sphere meshes, contouring of the exact signed distance of the union (power diagram), and wrap. Report correctness against the analytic volume and area of the union where known, degeneracies hit (near-tangent spheres, coplanar circle arcs), and time. Propose a test family for the PR tier (<= 5 s) and one for the nightly tier.

## How to run this study (owner 2026-11-22)

- Problem first, not paper by paper: one PR for the whole plan, answering the falsifiable question above.
- Run the probe against the library's C# code. Python only for a reference value the library cannot compute.
- Search the library before building: do not re-measure what an existing class already gives (for example `SphericalFibonacciPointSet`, `MeshTopologySummary`).
- Stop rule: if the first probe finds no defect and decides nothing, write a one-paragraph stop note and stop; do not widen the study.

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
