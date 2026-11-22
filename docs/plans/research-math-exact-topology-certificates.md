# Plan: exact topology certificates (Betti numbers, discrete Morse theory) as Boolean and repair oracles (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Kind:** research study (study first, no library change)
- **Tier:** M (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

`MeshTopologySummary` gives the Euler characteristic and genus; `ContouringTopologyCertificate` certifies contour topology. Boolean and repair tests check closed / manifold / NotCleanReason, but not the Betti numbers (components, handles, cavities) of the result against the expected ones.

## Goal

Design an exact, integer-only computation of b0, b1, b2 of a closed mesh (and of a solid with cavities via nesting), check it against Euler / Alexander duality, and measure which Boolean and repair test families gain a stronger oracle (e.g. a Difference that must create exactly one cavity). Note what it cannot see (e.g. knotting).

## Scope

- A result note `docs/research/EXACT_TOPOLOGY_CERTIFICATES.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Edelsbrunner, Harer 2010 (Computational Topology); Forman 1998 (Morse theory for cell complexes); Delfinado, Edelsbrunner 1995 (an incremental algorithm for Betti numbers of simplicial complexes). Paper and book text only. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/EXACT_TOPOLOGY_CERTIFICATES.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
