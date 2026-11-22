# Plan: neuron-like branching meshes from skeletons as a stress family (research study)

- **Board row:** none yet (new research topic, owner 2026-11-22: mathematics and biology topics to be checked with Codex)
- **Kind:** research study (study first, no library change)
- **Tier:** M (study and result note; any implementation is a separate PR after an owner go)
- **Plan status:** draft, 2026-11-22 (Berlin). Plan only. Marked for Codex.

## Current state

Loft and generators (`mesh_generators/GenCylGenerators.cs`, loft stage of `benchmarks/MetamorphicBugHunt`), exact Boolean, alpha wrap, contouring of convolution surfaces.

## Goal

Build a procedural generator of branching tube structures in the style of SWC neuron skeletons (soma, dendrite trees, varying radii, near-tangent branches) and compare three routes to a closed surface: union of tubes with the exact Boolean, contouring of a convolution field, and wrap. Measure correctness (closed, manifold, the expected genus and components), near-tangent failures and time; propose a PR-tier family.

## Scope

- A result note `docs/research/NEURON_SKELETON_MESHES.md`.
- Optional probe code in `benchmarks/` (procedural inputs only).
- Nothing in the library, `tests/` or the public API.

## Non-goals

No library change in this study: any code is a separate PR after an owner go, with an A/B. Papers only: never read or port GPL, LGPL, AGPL, MPL or unlicensed code (CGAL, libigl, geometry-central and other geodesic libraries, Triangle, TetGen); check a licence before opening any code. No mesh or data files committed; test inputs are procedural. A PR-tier test never fetches from the network.

## Look before you build

1. Library: search for existing building blocks first (the files named under "Current state", `docs/OPERATIONS.md`, `docs/design/README.md`, `docs/research/README.md`).
2. Literature: the sources below and a short prior-art check (AGENT_WORKFLOW.md sections 2 and 3).

## Sources and licences

Ascoli et al. 2007 (NeuroMorpho.Org, for the SWC format only; its data has mixed licences and is not used); Bloomenthal, Shoemake 1991 (Convolution surfaces); Lasserre et al. 2012 (A neuron membrane mesh representation). Paper text only; procedural inputs only. Every source read is listed in `docs/sources/<domain>.md` (CLAUDE.md licence rules).

## Done when

A result note `docs/research/NEURON_SKELETON_MESHES.md` (+ entry in `docs/research/README.md`, sources in `docs/sources/<domain>.md`) that states what was measured, on which procedural inputs, and a recommendation (build / do not build / park) with the expected cost and the guarantee; `python3 tools/check_docs.py` passes. Probe code stays in `benchmarks/` or is not committed.
