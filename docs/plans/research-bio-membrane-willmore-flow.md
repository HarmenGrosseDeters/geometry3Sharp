# Plan: membrane shapes (Helfrich energy, Willmore flow) as fairing and remeshing stress (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Kind:** research study (study first, no library change)
- **Tier:** M/L (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

Smoothing and fairing: `LaplacianMeshSmoother`, `LaplacianMeshDeformer`, `MeshCurvature`; remeshing (`Remesher`, `QuadRemesher`). No curvature-energy flow exists.

## Goal

Study a discrete Willmore / Helfrich flow (cell membranes, red blood cell shapes) as an opt-in fairing operator and as a stress test for remeshing under large, curvature-driven motion: stability of the discretisations in the papers, step size, conformal versus vertex-based flows, and which known equilibrium shapes (spheres, Clifford torus, biconcave discs) can serve as exact oracles.

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
