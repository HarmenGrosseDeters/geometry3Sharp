# Plan: alpha wrap feature pass K follow-ups (corners, split retries)

- **Board row:** OPEN_TOPICS.md §3 "Ready to start", row "Alpha wrap: feature pass K follow-ups (corners, split retries)"
- **Kind:** ready to start (scoped, not scheduled; board: "a PR when a caller needs sharper corners")
- **Tier:** M (outside exact code; uses exact checks via `Reducer.KeepClearOf` / `TetrahedraMeet`)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- `mesh_ops/AlphaWrapFeaturePass.cs` (630 lines, opt-in, #736, 2026-11-13): plane snap, crease snap (two planes), edge
  splits and flips on the wrap, every move exactly checked (no input crossing, swept-region test, normal-turn limit).
  Its doc comment (line 29): "Corners (three planes) are not handled."
- Sharp-feature study (owner 2026-11-12, `runs/sharp-features`; [research.md HP 4](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)):
  T1 KR edge F 0.61 -> 0.88, crease normal error 14.7° -> 4.6°, concave crease within 1 offset 0.02 -> 0.75, 20/20 clean,
  0 input crossings, 0.72x triangles, +1.1 s; procedural CAD solids 0.995 / 0.03° / 0.98.
- After the 800 run the pass stays opt-in (owner 2026-11-17). Follow-ups named: corners (vertices where three or more
  fitted planes meet) and split retries (a refused split / snap is not retried after neighbours moved).
- Tests: `AlphaWrapFeaturePassTests` (430 lines; invariance, determinism, reuse, cancel, smooth input unchanged, move
  refusals). Benchmark: `benchmarks/SharpFeatureProbe`.
- Consumer: tet P4(b) creases (`CreaseAngleDegrees`, TET_MESHING.md 17.14) uses its own path; a caller that needs
  corners is the trigger.

## Trigger / decision needed

A caller (tet meshing on CAD solids, a user request) that needs sharp corners, or the owner scheduling it. No option
question: the approach was decided with K.

## Goal

On CAD-like inputs the opt-in pass also recovers corners (3+ planes) and retries moves refused early, keeping every
guarantee of the wrap (input enclosed, no crossing, manifold, closed).

## Scope

- `mesh_ops/AlphaWrapFeaturePass.cs` (corner kind: vertex snap to the least-squares intersection point of 3+ planes,
  clamped; a second round over refused ops, bounded).
- `tests/geometry3Sharp.Tests/AlphaWrapFeaturePassTests.cs`, `benchmarks/SharpFeatureProbe` (corner metric).

## Non-goals

Default stays off; output with the option off bit for bit; no change to `AlphaWrap`, `SimplifyAlphaWrap`, Reducer.

## Interface

No new public option if corners are part of the pass; else `AlphaWrapFeaturePass.Corners` (bool, default true when the
pass runs) with doc line and test.

## Look before you build

1. Library: K's plane fit and crease snap (reuse for 3 planes), `Reducer.KeepClearOf`, `Reducer.TetrahedraMeet`,
   `MeshPrincipalCurvature` (quad stage 1) for corner detection, QEF solve from `DualContouring` (`SymmetricEigenSolver`,
   rank-revealing) for the corner point.
2. Literature: Kobbelt, Botsch, Schwanecke, Seidel, "Feature Sensitive Surface Extraction from Volume Data"
   (SIGGRAPH 2001: QEF corners); Attene et al., "Sharpen&Bend" (2005); Ju et al., dual contouring QEF (2002).

## Steps (one PR, clean commits)

1. Corner metric in SharpFeatureProbe (distance of the nearest wrap vertex to the offset-mitered corner point).
2. Corner detection (vertex with >= 3 distinct fitted planes in its ring) and QEF snap with rank truncation, exactly
   checked like the other moves.
3. Retry round: refused ops queued, one more pass after all others (bounded: one retry per op).
4. Tests: cube / L-shape / box-with-chamfer corners recovered within 1 offset of the mitered corner; guarantees
   (closed, manifold, no input crossing, finite); invariance (2^k scale, translation, rotation by 90°), determinism, reuse.

## Acceptance

- SharpFeatureProbe on the 20 T1 + 6 CAD solids (command in the PR): corners within 1 offset of the miter point on the
  CAD solids >= 90 % (before: measure); edge F / crease normal not worse; 20/20 clean; 0 input crossings; time +<= 20 %
  of the pass.
- Each new test fails with the corner step removed.

## Sources and licences

Papers above; no code ports (CGAL feature detection: approach only).

## Budget and stop rule

Upper bound first: share of CAD-solid corners currently > 1 offset off; if < 10 % of corners, stop and report.
Max 3 rounds without a measured gain.

## Risks and open questions

- Acute corners (< 60°) may need vertex splits, not snaps; out of scope unless measured.
