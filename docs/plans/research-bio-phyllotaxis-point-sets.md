# Plan: phyllotaxis and growth patterns as deterministic point sets (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Kind:** research study (study first, no library change)
- **Tier:** S/M (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

Delaunay and 2D meshing, `Remesher`, Poisson / blue-noise sampling, the CVT question of plan #1266.

## Goal

Study Vogel's model and related phyllotactic and growth constructions (Fibonacci sphere, golden-angle spirals on discs and surfaces) as deterministic, near-uniform point sets: their spacing bounds (proven or measured), behaviour under `Similarity.All()`, and their use as procedural inputs for Delaunay, remeshing and CVT tests. Include the degenerate cases they produce on purpose (near-cocircular points on spirals).

## Scope

- A result note `docs/research/PHYLLOTAXIS_POINT_SETS.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Vogel 1979 (A better way to construct the sunflower head); Ridley 1982; Swinbank, Purser 2006 (Fibonacci grids on the sphere); González 2010 (Measurement of areas on a sphere using Fibonacci and latitude-longitude lattices). Paper text only. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/PHYLLOTAXIS_POINT_SETS.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
