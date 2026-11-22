# Plan: optimal transport (Wasserstein) distance between meshes as a test metric (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Kind:** research study (study first, no library change)
- **Tier:** M (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

Tests and comparisons use Hausdorff and volume differences. The heat kernel is available through `MeshGeodesicDistance` and the sparse Cholesky solver.

## Goal

Measure whether an entropic Wasserstein distance computed with the heat kernel (convolutional Sinkhorn) is a usable robust mesh distance for regression tests and cross-method comparisons: cost on 10^4-10^5 vertices, determinism (bit-for-bit across runs), sensitivity to slivers versus Hausdorff, and how to make it scale-invariant over `Similarity.All()`.

## Scope

- A result note `docs/research/OPTIMAL_TRANSPORT_MESH_DISTANCE.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Solomon et al. 2015 (Convolutional Wasserstein distances); Cuturi 2013 (Sinkhorn distances); Peyré, Cuturi 2019 (Computational Optimal Transport, book). Paper text only; POT (MIT) may be cited, not run in this container. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/OPTIMAL_TRANSPORT_MESH_DISTANCE.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
