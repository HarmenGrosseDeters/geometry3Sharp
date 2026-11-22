# Plan: discrete conformal equivalence on intrinsic triangulations (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Priority:** 1 of 7 (highest: an open P1 problem, #1285)
- **Kind:** research study (study first, no library change)
- **Tier:** M/L (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

Parameterization today: `LSCMParameterization`, `SpectralConformalParameterization`, BFF, SLIM, `FoldoverFreeParameterization`; P1 collapse on near-symmetric annuli is open (plan #1285). Intrinsic triangulations with integer (normal) coordinates are on master (`mesh_ops/IntrinsicTriangulation.cs`, `IntrinsicIntegerCoordinates.cs`, `IntrinsicCommonSubdivision.cs`).

## Goal

**Falsifiable question:** On procedural versions of the P1 collapse cases of #1285, does discrete conformal equivalence with intrinsic Delaunay flips, built on our `IntrinsicTriangulation` stack, give a foldover-free map where today's method collapses? Which steps (flip predicate, Ptolemy update) decide exactly, and where does double arithmetic fail?

**Counts as a result:** a C# prototype in `benchmarks/` run on those cases, with the answer yes / no / partly and the measured cases.

Decide whether discrete conformal equivalence with intrinsic Delaunay flips (a convex energy with a unique minimiser, so a parameterization with a proof of existence and no foldovers) fits our intrinsic data structures, which steps can be decided exactly (flip predicates, the Ptolemy update), and whether it solves the P1 cases of #1285.

## How to run this study (owner 2026-11-22)

- Problem first, not paper by paper: one PR for the whole plan, answering the falsifiable question above.
- Run the probe against the library's C# code. Python only for a reference value the library cannot compute.
- Search the library before building: do not re-measure what an existing class already gives (for example `SphericalFibonacciPointSet`, `MeshTopologySummary`).
- Stop rule: if the first probe finds no defect and decides nothing, write a one-paragraph stop note and stop; do not widen the study.

## Scope

- A result note `docs/research/DISCRETE_CONFORMAL_EQUIVALENCE_STUDY.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Springborn, Schröder, Pinkall 2008 (Conformal equivalence of triangle meshes); Gillespie, Springborn, Crane 2021 (Discrete conformal equivalence of polyhedral surfaces); Bobenko, Pinkall, Springborn 2015 (Discrete conformal maps and ideal hyperbolic polyhedra). Paper text only; the authors' code is not read unless its licence is MIT/Apache/BSD. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/DISCRETE_CONFORMAL_EQUIVALENCE_STUDY.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
