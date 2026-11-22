# Plan: membrane shapes (Helfrich energy, Willmore flow) as fairing and remeshing stress (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Priority:** 7 of 7 (parked: only after an owner go for a curvature-energy fairing operator)
- **Kind:** research study (study first, no library change)
- **Tier:** M/L (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

Smoothing and fairing: `LaplacianMeshSmoother`, `LaplacianMeshDeformer`, `MeshCurvature`; remeshing (`Remesher`, `QuadRemesher`). No curvature-energy flow exists.

## Goal

**Falsifiable question:** Would a discrete Willmore flow add an operator the library lacks, and does large curvature-driven motion break `Remesher`?

**Counts as a result:** do not start without an owner go.

Study a discrete Willmore / Helfrich flow (cell membranes, red blood cell shapes) as an opt-in fairing operator and as a stress test for remeshing under large, curvature-driven motion: stability of the discretisations in the papers, step size, conformal versus vertex-based flows, and which known equilibrium shapes (spheres, Clifford torus, biconcave discs) can serve as exact oracles.

## How to run this study (owner 2026-11-22)

- Problem first, not paper by paper: one PR for the whole plan, answering the falsifiable question above.
- Run the probe against the library's C# code. Python only for a reference value the library cannot compute.
- Search the library before building: do not re-measure what an existing class already gives (for example `SphericalFibonacciPointSet`, `MeshTopologySummary`).
- Stop rule: if the first probe finds no defect and decides nothing, write a one-paragraph stop note and stop; do not widen the study.

## Scope

- A result note `docs/research/WILLMORE_FLOW_STUDY.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Bobenko, Schröder 2005 (Discrete Willmore flow); Crane, Pinkall, Schröder 2013 (Robust fairing via conformal curvature flow); Helfrich 1973; Seifert 1997 (Configurations of fluid membranes and vesicles). Paper text only. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/WILLMORE_FLOW_STUDY.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
