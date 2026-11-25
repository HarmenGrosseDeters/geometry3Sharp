# Plan: 2D meshing research items (Terminator, matched shells, grading placement, Chew-2)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "2D meshing: research items (Terminator, matched shells, grading placement, Chew-2)"
- **Kind:** parked (owner 2026-11-16; trigger: a real input or concrete use case that fails current quality/size rules)
- **Tier:** L (clean-room spec-driven 2D meshing under `docs/design/MESHING2D_SPEC.md`, oracle acceptance, exact predicates)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only. **Clean-room rule applies.**

## Current state

- **Clean-room status:** `QualityMesh2` / `ConstrainedDelaunayMesh2` are implemented strictly from
  [MESHING2D_SPEC.md](../design/MESHING2D_SPEC.md) and published papers (provenance checks 6b done in #295, #296, #677).
  Never read, open, or port Triangle / Triangle.NET code.
- Owner 2026-11-16 closed the `<= 2x fork` speed target and parked four research items
  ([meshing2d.md](../roadmap/meshing2d.md#open-for-the-future-2026-11-15)):
  1. **Shewchuk's Terminator with a region-based oracle excuse:** owner 2026-11-06 chose option (c) ("not built") —
     parked until a real input fails the current small-angle rule (#394, exemption up to 60°; overlaps HP 6).
  2. **Matched shells:** constrained quality mode (`Conforming = false` + diametral lenses + off-centres, #322) already
     serves as the practical answer; parked until a caller needs shell-by-shell matching.
  3. **Grading placement strategy:** parked until fewer triangles matter (e.g. 2D FE meshes with strong grading; current
     grading is #375, #380).
  4. **Chew-2 (second Delaunay refinement algorithm, Chew 1993):** no issues currently motivate it.

## Trigger / decision needed

A real user input or concrete domain workload (e.g. 2D FE solver hitting triangle count limits or an acute-angle
cascade escaping the current small-angle exemption) that fails `QualityMesh2` on master. Pick only the one sub-item
the failing case actually needs.

## Goal

Keep the four clean-room 2D meshing research ideas scoped so that, if triggered by a real input, the matching item can
be evaluated against the independent 2D meshing oracle (`benchmarks/Meshing2Benchmark/Oracle`) without violating
clean-room provenance.

## Scope

- Spec first: `docs/design/MESHING2D_SPEC.md` (add/amend rule and oracle definition before any code).
- Implementation (clean room only): `comp_geom/QualityMesh2.cs` (and helpers under `comp_geom/`).
- Oracle & tests: `benchmarks/Meshing2Benchmark/Oracle/*`, `tests/geometry3Sharp.Tests/QualityMesh2*Tests.cs`,
  `tests/geometry3Sharp.Tests/Meshing2*Tests.cs`.

## Non-goals

- **STRICT CLEAN-ROOM:** never open, read, or port `triangle.c` or Triangle.NET (or any fork of it). Any agent that has
  seen that code must not work on these items.
- No changes to default outputs unless approved by owner with full corpus + oracle comparison.

## Interface

Any new refinement strategy (Terminator, Chew-2, alternate grading placement) must be opt-in or strictly spec-backed
with zero API break vs 1.0.324.

## Look before you build

1. Library: `docs/design/MESHING2D_SPEC.md`, `docs/design/MESHING2D_SNAP.md`, `comp_geom/QualityMesh2.cs`,
   `comp_geom/ConstrainedDelaunayMesh2.cs`, `benchmarks/Meshing2Benchmark/Oracle`.
2. Literature (papers only):
   - Shewchuk (2000), "Mesh Generation for Domains with Small Angles" (SoCG 2000 — Terminator algorithm).
   - Chew (1993), "Guaranteed-Quality Mesh Generation for Curved Surfaces" (SoCG 1993 — Chew's second algorithm, 30°
     circumcenter refinement).
   - Üngör (2004/2009), "Off-centers: A new type of Steiner points for computing size-optimal quality-guaranteed
     Delaunay triangulations" (already partially used).
   - Ruppert (1995), "A Delaunay Refinement Algorithm for Quality 2-Dimensional Mesh Generation" (J. Algorithms).

## Steps (if one item is triggered; one PR per item)

1. Pin the triggering input as a procedural test case and verify the failure on current master.
2. Update `docs/design/MESHING2D_SPEC.md` and the independent oracle's acceptance predicate first (owner review).
3. Implement from the published paper only; run full catalogue (M2/M3) + corpus + oracle checks (0 oracle regressions,
   termination guaranteed, exact 2^k scale invariance, determinism, reuse).
4. Record a provenance note (confirming clean-room sources only) in `docs/sources/meshing-and-repair.md`.

## Acceptance

- Triggering input passes both `QualityMesh2` and the independent oracle (`benchmarks/Meshing2Benchmark/Oracle`).
- Zero regressions across the full catalogue and corpus; before/after vertex count, min angle, and wall time reported
  with exact commands.
- Provenance check documented (no Triangle / Triangle.NET exposure).

## Sources and licences

Published papers only (Shewchuk 2000, Chew 1993, Üngör 2004, Ruppert 1995). Strictly forbidden: Triangle (`triangle.c`,
non-commercial license) and Triangle.NET code.

## Budget and stop rule

Owner 2026-11-16 decision rule: build only what is measured, low risk, and gives >= 10 % gain (or fixes a concrete
failing input). Stop after the oracle/spec probe if the gain is below 10 % or breaks existing catalogue guarantees.

## Risks and open questions

- Changing Steiner-point placement alters golden fingerprints and grading counts across the catalogue; keep experimental
  strategies opt-in unless the owner explicitly approves re-baselining.
