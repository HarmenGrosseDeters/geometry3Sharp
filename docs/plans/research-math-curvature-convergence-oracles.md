# Plan: convergence theorems of discrete curvature as test oracles (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Priority:** 2 of 7 (best chance of finding real defects)
- **Kind:** research study (study first, no library change)
- **Tier:** M (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

`MeshCurvature`, `MeshPrincipalCurvature`, the cotangent Laplacian (`LaplacianMeshSmoother`, `LaplacianMeshDeformer`), `MeshGeodesicDistance` (heat method), `VectorHeatMethod`. Their tests check single values, not the rate at which the error falls under refinement.

## Goal

**Falsifiable question:** Which of `MeshCurvature`, `MeshPrincipalCurvature`, the cotangent Laplacian, `MeshGeodesicDistance` and `VectorHeatMethod` miss their proven convergence rate on refinement families of the sphere, torus and ellipsoid? For each miss: the input, the measured rate and the cause in our code.

**Counts as a result:** an issue per defect with a minimal procedural repro, and the oracle tests proposed for the PR tier.

Turn the proven convergence results into test oracles: on procedural surfaces with known curvature (sphere, torus, ellipsoid, graph surfaces) and refinement families for which the theorems hold (and ones for which they provably fail, e.g. Schwarz lantern), the measured error must fall at the proven rate. Name which operators meet their rate and which do not, with the inputs.

## How to run this study (owner 2026-11-22)

- Problem first, not paper by paper: one PR for the whole plan, answering the falsifiable question above.
- Run the probe against the library's C# code. Python only for a reference value the library cannot compute.
- Search the library before building: do not re-measure what an existing class already gives (for example `SphericalFibonacciPointSet`, `MeshTopologySummary`).
- Stop rule: if the first probe finds no defect and decides nothing, write a one-paragraph stop note and stop; do not widen the study.

## Scope

- A result note `docs/research/CURVATURE_CONVERGENCE_ORACLES.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Hildebrandt, Polthier, Wardetzky 2006 (On the convergence of metric and geometric properties of polyhedral surfaces); Xu 2004 and Wardetzky 2006 (convergence of the cotangent Laplacian); Crane, Weischedel, Wardetzky 2013 (heat method, convergence of the distance). Paper text only. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/CURVATURE_CONVERGENCE_ORACLES.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
