# Plan: `.poly` reader handling of crossing / overlapping segment groups

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "2D meshing: .poly reader keeps crossing segment groups as constraints only"
- **Kind:** parked (no current use case; trigger: a caller loading `.poly` PSLG files with crossing/overlapping segment groups who needs them split or reported instead of kept as raw constraints)
- **Tier:** M (I/O reader + PSLG validation; must add cases to `ReaderFuzzTests` per CLAUDE.md)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only. **Clean-room rule applies.**

## Current state

- Recorded in [meshing2d.md](../roadmap/meshing2d.md#open) line 98 and [platform.md](../roadmap/platform.md#stage-8-planned-follow-ups):
  - The `.poly` reader currently keeps crossing, touching, or overlapping segment groups as constraints only (relying on
    downstream `ConstrainedDelaunayMesh2` / `SnapRounding2` to resolve intersections or reject them).
  - The earlier $O(d^2)$ hub slowdown in `ReadPoly` was already fixed on 2026-11-12 via an angular sort ($O(d \log d)$:
    wheel of 20 000 spokes dropped from 5.5 s to 4.0 s).
  - The remaining parked question is how `.poly` files containing crossing/overlapping segment groups (which violate the
    strict PSLG definition) should be represented or pre-processed on read: keep as constraints only (today), split
    intersections with marker propagation, or report/flag crossing pairs cleanly.

## Trigger / decision needed

A real caller or `.poly` dataset with crossing/overlapping segment groups where keeping them unsplit as raw constraints
breaks downstream boundary markers, region labels, or callers expecting a valid PSLG. Owner decides desired policy:
opt-in `SplitCrossings` / diagnostic report vs keeping current default unchanged.

## Goal

Give callers of the `.poly` reader clear visibility and (if requested) deterministic exact splitting with marker
propagation for crossing or overlapping segment groups, without changing default behaviour on valid PSLGs.

## Scope

- `.poly` reader class (locate via `grep -rn ReadPoly` under `io/` or `comp_geom/` — check exact path before editing)
- `tests/geometry3Sharp.Tests/*Poly*Tests.cs` and `tests/geometry3Sharp.Tests/ReaderFuzzTests.cs` (CLAUDE.md rule: every
  reader change is covered by `ReaderFuzzTests`)
- `tools/g3cli/` (only if `g3cli mesh2d` exposes a `.poly` diagnostics flag)
- `docs/OPERATIONS.md`, `docs/roadmap/meshing2d.md`, `docs/OPEN_TOPICS.md`

## Non-goals

- **STRICT CLEAN-ROOM:** read only the public `.poly` file format documentation (format spec) and our own code; never
  read `triangle.c` or Triangle.NET source.
- Never silently drop segments or markers; never break valid `.poly` files (bit-identical output on non-crossing inputs).

## Interface

Proposed additive opt-in on the `.poly` reader options:
- Report crossing / overlapping segment indices in the read result (`HasCrossingSegments`, `CrossingPairCount`), and/or
- Opt-in `ResolveCrossings` (default `false`) using `SnapRounding2` or exact 2D segment arrangement with deterministic
  marker conflict rule (e.g. lowest marker id or list of markers).

## Look before you build

1. Library: `comp_geom/SnapRounding2.cs`, `comp_geom/ConstrainedDelaunayMesh2.cs`, existing `.poly` reader and
   `ReaderFuzzTests.cs`, `math/AdaptivePredicates.cs` (`Orient2d`).
2. Literature / spec: Bentley-Ottmann sweep-line segment intersection (1979) / Hobby (1999) snap rounding; public
   `.poly` ASCII format specification (nodes, segments with optional boundary markers, holes, regional attributes).

## Steps (one PR, clean commits)

1. Audit current `.poly` reader behaviour on crossing, T-junction touching, collinear-overlapping, and duplicate-opposite
   segment groups; write procedural tests pinning current vs desired behaviour.
2. Implement crossing detection (exact `Orient2d` + sweep/quadtree) and opt-in resolution / marker propagation.
3. Add fuzz inputs (malformed, crossing, overlapping, extreme-exponent `.poly` text) to `ReaderFuzzTests`.
4. Add invariance (2^k scale, translation, segment reordering), determinism, and reuse tests.

## Acceptance

- Default mode unchanged (bit-identical) on all existing `.poly` tests; crossing/overlapping groups accurately detected
  and (when opted in) split into a valid non-crossing PSLG with deterministic markers.
- `ReaderFuzzTests` green (no unhandled exceptions, no OOM/hang on malformed inputs).
- All new tests <= 5 s; full local CI green.

## Sources and licences

Public `.poly` format spec and Bentley-Ottmann (1979) paper only. Strictly no Triangle / Triangle.NET code.

## Budget and stop rule

1 M session. Stop if a crossing-resolution option duplicates `ConstrainedDelaunayMesh2`'s existing snap-rounding path
with no distinct caller benefit (in that case, report-only diagnostics suffice).

## Risks and open questions

- Boundary marker ambiguity when two segments with *different* markers overlap collinearly: must document a deterministic
  rule (e.g. keep both or take min/max marker) and test input-order independence.
