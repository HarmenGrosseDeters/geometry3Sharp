# Plan: lower limit on `MeshTetrahedralization.MaxRadiusEdgeRatio` (owner decision)

- **Board row:** OPEN_TOPICS.md §2 "Waiting for an owner decision", row "Tet: lower limit on MaxRadiusEdgeRatio"
- **Kind:** owner decision
- **Tier:** S/M (small guard + tests if a limit is set; S docs-only if "none")
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Measured in [TET_MESHING.md §17.10](../design/TET_MESHING.md) (issue #1138, 2026-11-19):
  - Default `MaxRadiusEdgeRatio` is `2.0`. The CLI (`g3cli tetmesh --max-radius-edge`) already enforces `[1.5, 1000]`.
  - On 4 procedural shapes × 2 alphas (`12` and `40`): ratio `2.0` and `1.5` terminate on 8/8 runs (<= 5 trial insertions
    per wrap vertex); `1.3` terminates on 6/8 (box/12 and cylinder/12 run away); `1.2` on 4/8; `1.1` on 2/8; `1.0` on
    0/8 (120 s hang before the work bound).
  - Reason: Shewchuk (1998) — radius-edge bounds below 2 require non-acute/non-right input angles.
  - Option (a) is already built (`MaxTrialInsertionsPerWrapVertex = 48`, default on): runaways stop cleanly in ~1.7-5 s
    with `Status == PointLimitReached`, valid output, `Counts["work_limit_reached"] = 1`, and `Targets.RadiusEdgeMet == false`.
  - Option (b) (a library lower limit on `MaxRadiusEdgeRatio`) is open for the owner: `1.5`, `1.3`, or `none`.

## Trigger / decision needed

Owner chooses the library lower limit on `MeshTetrahedralization.MaxRadiusEdgeRatio`:

| Option | Effect | Cost / risk |
|---|---|---|
| A. **None — keep the work bound (`MaxTrialInsertionsPerWrapVertex`) only (leaning)** | Matches the math (the termination threshold is shape- and alpha-dependent: sphere ends at 1.1, box/12 fails at 1.3); no hang thanks to option (a); CLI stays clamped at 1.5 | Callers passing 1.0-1.3 in C# get a `PointLimitReached` mesh with many unrefined bad tets after ~2-5 s |
| B. **Lower limit `1.5` (return invalid-parameter status or clamp + document)** | Matches `g3cli tetmesh` (`[1.5, 1000]`); all 8 measured runs terminate cleanly | Rejects valid sphere/torus runs at 1.2-1.3; changes behaviour for callers passing < 1.5 |
| C. **Lower limit `1.3`** | Stops the clear runaway zone (<= 1.2) while allowing 1.3 on smooth/fine inputs | Still hits the work bound on box/12 and cylinder/12; magic number without a theorem behind it |

How others solve it: TetGen (Si, ACM TOMS 2015) defaults `-q` to `2.0` and allows any value > 0, warning in docs that
values below `~1.2-1.4` may not terminate (relying on `-S` Steiner-point limit as the safety stop); CGAL `Mesh_3`
(Jamin et al., ACM TOMS 2015) accepts any `cell_radius_edge_ratio` and documents the theoretical termination bound of
`2.0` (Shewchuk 1998); Gmsh / Netgen expose quality targets with iteration/point caps rather than hard-rejecting tight
ratios; `g3cli` clamps at `1.5` as an untrusted-input guardrail.

## Goal

Settle whether `MeshTetrahedralization` keeps `MaxRadiusEdgeRatio` unrestricted (guarded by the work bound) or enforces
a lower limit (`1.5` or `1.3`).

## Scope

- Option A: `docs/OPEN_TOPICS.md`, `docs/design/TET_MESHING.md` §17.10, XML doc on `MeshTetrahedralization.MaxRadiusEdgeRatio`.
- Option B/C: `mesh_ops/MeshTetrahedralization.cs` (validation/clamping), `tests/geometry3Sharp.Tests/TetRefinementWorkBoundTests.cs`,
  `docs/design/TET_MESHING.md`, `docs/OPERATIONS.md`, `docs/OPEN_TOPICS.md`.

## Non-goals

No change to `MaxTrialInsertionsPerWrapVertex`, no change to default `2.0` output (bit-identical), no change to `g3cli`
limit `[1.5, 1000]`.

## Interface

If B/C: decide clamp vs fail-fast (`InvalidInput` status) — fail-fast is cleaner than silent clamping (matches
`CLAUDE.md` / `ROADMAP.md` "clear result codes instead of silent failures").

## Look before you build

1. Library: `MeshTetrahedralization.MaxRadiusEdgeRatio`, `TetRefinementWorkBoundTests` (#1138), `tools/g3cli/TetMeshCommand.cs`.
2. Literature: Shewchuk (1998), "Tetrahedral Mesh Generation by Delaunay Refinement"; Cheng-Dey-Shewchuk (2012).

## Steps (one PR, clean commits)

1. Owner picks A, B (1.5), or C (1.3) — and if B/C, fail-fast vs clamp.
2. If A: update XML doc ("values below 1.5 may hit `MaxTrialInsertionsPerWrapVertex`") and close the row.
3. If B/C: add validation + unit tests (boundary values `limit - eps`, `limit`, `NaN`, `0`, `<= 0` = off if applicable)
   + reuse/determinism tests + update `TetRefinementWorkBoundTests`.

## Acceptance

- Option A: docs-only diff reviewed.
- Option B/C: new tests fail on old code; `DefaultPathAndRadiusEdge15_*` bit-identical; full local CI green.

## Sources and licences

Shewchuk (1998), Si (2015), Jamin et al. (2015) — papers/docs only; no code ports.

## Budget and stop rule

One short session (< 1 h).

## Risks and open questions

- If B/C rejects < 1.5, `TetRefinementWorkBoundTests` currently uses ratios 1.0-1.3 to exercise the work bound; those
  tests would need an internal bypass or a shape-runaway fixture at 1.5 to keep testing the work bound.
