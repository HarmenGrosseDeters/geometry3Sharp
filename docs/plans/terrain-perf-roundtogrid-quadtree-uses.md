# Plan: terrain §11 performance follow-ups, `RoundToGrid`, and internal quadtree uses

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Terrain: §11 performance follow-ups, RoundToGrid, internal quadtree uses"
- **Kind:** parked (until a real slow terrain case arises or the terrain track resumes; upper bound first!)
- **Tier:** M/L (`RoundToGrid` uses exact checks + `SnapRounding2` -> tier L; quadtree adoption and §11 perf -> M/L)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only. **Clean-room rule applies to quadtree code.**

## Current state

Three parked terrain items from [terrain.md](../roadmap/terrain.md#open-for-the-future-2026-11-15) and
[ROADMAP.md](../ROADMAP.md#now):

1. **§11 performance follow-ups (`TERRAIN.md` §11):**
   - Octagon 1 m replacement regressed from 6.1 -> 8.8 s in #318 due to a second realization.
   - Combinatorial zeros of C and band-only overlay (avoiding full-terrain overlay work outside the catch-line band).
   - `LocalFrame` positions-only copy (today translates in place with bitwise restore via `VerticesBuffer`, falling back
     on `-0.0` terrains, #355).
2. **`RoundToGrid(origin, resolution)`** (approved in concept 2026-11-03 before the pause, not yet built):
   - Optional output step: iterated snap rounding to a target grid (Hobby 1999; Halperin & Packer 2002; Hershberger 2008)
     using `SnapRounding2` + fixed-width exact points, then re-running the exact slope/support checks with a clear status.
   - Required first step: an upper-bound probe measuring how often rounding to 1 mm at UTM breaks a slope check.
3. **Internal quadtree uses** (clean-room `SegmentQuadtree2d` / `TriangleQuadtree2d` already merged in #195/#266):
   - Replace `TerrainSafety`'s segment midpoint grid (`PointHashGrid2d`, query radius widened by half longest segment)
     with `SegmentQuadtree2d`.
   - Replace `TerrainVolumeDifference`'s height check (`DMeshAABBTree3`) with `TriangleQuadtree2d`.
   - Required first step: upper-bound measurement on procedural inputs including a long-segment worst case; PR only above
     the 10 % bar.

## Trigger / decision needed

Terrain track resumes or a user hits a slow terrain workload (> 5-10 s on a km-scale grid) or needs grid-snapped UTM
outputs (`RoundToGrid`). Treat as three separate PRs, each gated on its upper-bound probe.

## Goal

(A) Measure and ship only the terrain perf & quadtree-adoption levers exceeding 10 % speedup with bit-identical results,
and (B) build opt-in `RoundToGrid` if the 1 mm UTM probe shows practical survival rates.

## Scope

- **PR A (internal quadtree uses):** `mesh_ops/TerrainSafety.cs`, `mesh_ops/TerrainVolumeDifference.cs`,
  `tests/geometry3Sharp.Tests/TerrainSafetyTests.cs`, `TerrainVolumeDifferenceTests.cs`, `benchmarks/TerrainBenchmark/`.
- **PR B (§11 perf):** `mesh_ops/TerrainSlopeDesign.cs` (second realization diagnosis, band-only overlay, `-0.0`
  positions-only `LocalFrame` copy).
- **PR C (`RoundToGrid`):** new `mesh_ops/TerrainRoundToGrid.cs` (using `comp_geom/SnapRounding2.cs`), tests against
  exact slope oracle at origin and UTM.

## Non-goals

- **Clean-room rule:** read only `docs/design/QUADTREE2D_SPEC.md` and `spatial/*Quadtree2d*` in this repo; never open
  the owner's old quadtree.
- Do not touch `Arrangement2d.find_intersecting_edges` or `MeshInsertUVPolyCurve` bin grid (explicitly marked "not now").
- PR A and PR B must keep default outputs bit-identical.

## Interface

- PR A / PR B: no public API change.
- PR C: additive opt-in `TerrainSlopeResult.RoundToGrid(Vector2d origin, double resolution)` returning a rounded mesh +
  exact slope/support check status (never silently violating the slope oracle).

## Look before you build

1. Library: `spatial/SegmentQuadtree2d.cs`, `spatial/TriangleQuadtree2d.cs`, `comp_geom/SnapRounding2.cs`,
   `mesh_ops/TerrainSafety.cs`, `mesh_ops/TerrainVolumeDifference.cs`, `mesh_ops/TerrainSlopeDesign.cs`.
2. Literature: Hobby (1999), Halperin & Packer (2002), Hershberger (2008/2013) for stable iterated snap rounding; OGC /
   JTS fixed precision model + snap-rounding noder.

## Steps (probe first, then one PR per winning item)

1. **Probes (no library PR yet):**
   - Quadtree probe: measure `PointHashGrid2d` share in `TerrainSafety` (normal + long-segment case) and `DMeshAABBTree3`
     share in `TerrainVolumeDifference` (1M-triangle TIN).
   - §11 probe: profile octagon 1 m replacement (why the second realization triggers) and overlay share outside the band.
   - `RoundToGrid` probe: snap Phase-1 UTM outputs to 1 mm / 1 cm grids and count slope-check violations.
2. Build only items passing the 10 % / viability bar; verify bit-identical outputs (PR A, B) and exact oracle checks
   (PR C), plus invariance, determinism, reuse tests.

## Acceptance

- PR A & B: bit-identical outputs on all existing terrain tests; order-balanced median-of-5 A/B >= 10 % faster and never
  slower on normal or worst-case inputs (command + output in PR).
- PR C: rounded coordinates lie strictly on `origin + k * resolution`; exact slope/support status honestly reported;
  invariance, determinism, reuse tests <= 5 s; `terrain-*` incremental mutation gate green if exact files change.

## Sources and licences

Hobby (1999), Halperin & Packer (2002), Hershberger (2008) — papers only. Strictly no owner old quadtree, no GPL code.

## Budget and stop rule

Probes: 0.5 day total. Drop any perf/quadtree lever below 10 % gain (or slower on any stratum). Drop `RoundToGrid` if
> 50 % of realistic UTM slopes fail the exact check at 1 mm without a slope-slack budget.

## Risks and open questions

- Replacing `DMeshAABBTree3` with `TriangleQuadtree2d` in `TerrainVolumeDifference`: verify tie-breaking on shared edges
  and vertical triangles so height queries remain bit-identical.
