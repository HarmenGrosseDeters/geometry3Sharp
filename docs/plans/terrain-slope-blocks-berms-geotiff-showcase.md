# Plan: terrain extensions (slope envelope, 3D blocks, berms, ramps beyond R1, GeoTIFF reader, showcase)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Terrain: slope envelope, 3D blocks, berms, ramps beyond R1, GeoTIFF reader, showcase"
- **Kind:** parked ("later", owner 2026-11-17; trigger: a concrete terrain / GIS / open-pit use case)
- **Tier:** L (exact terrain slope oracle, exact overlay/daylighting, new reader -> `ReaderFuzzTests`, multiple PRs)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Terrain Phase 1 (#357, 2026-11-03) and its follow-ups are complete (`TerrainSlopeDesign`, `LocalFrame`, mixed
  cut/fill, target heights, `TerrainVolumeDifference`, `TerrainSafety`, `CoreMath.Acos`/`Tan` in `TerrainSlopeDesign`
  #1066, `ContourLoft` stage 1/2 #741/#1197, quadtree #195/#266).
- Owner 2026-11-17 parked the remaining terrain backlog as **"later"** until a concrete terrain use case arises
  ([terrain.md](../roadmap/terrain.md#open-for-the-future-2026-11-15), [ROADMAP.md](../ROADMAP.md#now)):
  1. **GeoTIFF / DGM reader** (in library + `g3cli`, first item on restart per 2026-11-03 note).
  2. **Volume per cell / 3D blocks** (2D columns first, then 3D mining blocks cut by terrain/slope surfaces, `TERRAIN.md` §9.3).
  3. **Ramps beyond R1** (access ramps with curves/switchbacks, width transitions, junctions into pits).
  4. **Slope envelope construction** (generalized multi-bench / outer envelope).
  5. **Berms and inter-ramp checks** (explicitly parked 2026-11-02/17: "not modelled in practice unless a caller asks").
  6. **Terrain showcase** (end-to-end procedural open-pit example + CLI/MCP walkthrough).

## Trigger / decision needed

A real terrain / mining / civil-engineering caller or owner decision to resume the terrain track, picking the first
sub-item to build (recommended order from 2026-11-03: GeoTIFF reader -> 2D column volumes / 3D blocks -> ramps beyond
R1 -> showcase; berms last).

## Goal

Resume the terrain track cleanly as one PR per sub-feature, preserving the exact "never steeper than specified" slope
oracle and `LocalFrame` UTM robustness.

## Scope

- **PR 1 (GeoTIFF / DGM reader):** new `io/GeoTiffReader.cs` (uncompressed/deflate float32/float64 elevation grids +
  GeoKey origin/scale -> `DMesh3` TIN or height grid), `tools/g3cli/` integration, `tests/geometry3Sharp.Tests/ReaderFuzzTests.cs`.
- **PR 2 (2D columns / 3D blocks):** `mesh_ops/TerrainBlockVolume.cs` (per-cell cut/fill columns and 3D block clipping
  against terrain + design surfaces using `ExactMeshBoolean` / `TerrainVolumeDifference`).
- **PR 3 (Ramps beyond R1):** extensions in `mesh_ops/TerrainSlopeDesign.cs` / ramp builder (curved paths, switchbacks,
  junctions, verified by the exact slope oracle).
- **PR 4 (Berms / slope envelope, only on demand):** bench/berm profiles + inter-ramp slope check in `TerrainSlopeDesign`.
- **PR 5 (Showcase):** procedural example under `docs/examples/` (compiled by `DocExamplesTests`) + `g3cli` walkthrough.

## Non-goals

- **Clean-room rule:** any quadtree interaction uses only `docs/design/QUADTREE2D_SPEC.md` and `spatial/*Quadtree2d*`
  in this repo (never the owner's old quadtree).
- Never commit real DGM1 / GeoTIFF files; all tests use procedurally generated terrain / synthetic GeoTIFF bytes.
- Never weaken the exact slope-violation oracle (`TerrainSlopeOracle`).

## Interface

Additive classes/options per PR following the `(options, Compute(), Result/Status)` pattern in `docs/OPERATIONS.md`,
with `LocalFrame` support for UTM coordinates.

## Look before you build

1. Library: `docs/design/TERRAIN.md`, `mesh_ops/TerrainSlopeDesign.cs`, `mesh_ops/TerrainVolumeDifference.cs`,
   `mesh_ops/TerrainSafety.cs`, `spatial/*Quadtree2d*`, `mesh_ops/ExactMeshBoolean.cs`, `math/CoreMath.cs`.
2. Standards / prior art: OGC GeoTIFF 1.1 specification (OGC 19-008r4, public standard — implement minimal baseline TIFF
   + GeoKey tags cleanly, no GPL GDAL code); IFC 4.3 alignment/earthworks concepts for cut/fill blocks and local frames.

## Steps (one PR per sub-item, clean commits)

1. Confirm with the owner which sub-item starts first and its target grid size / time budget.
2. Write/update the corresponding section in `docs/design/TERRAIN.md` before coding (tier L design approval).
3. Implement with procedural tests (origin + UTM `LocalFrame` + rotated frames, exact slope oracle verification,
   invariance, determinism, reuse; `ReaderFuzzTests` for GeoTIFF).
4. Re-verify terrain mutation shards (`terrain-arith`, `terrain-kernel`, `terrain-overlay`) on touched files.

## Acceptance

- Each PR: exact slope oracle passes on 100 % of valid outputs; closed/manifold/finite geometry verified; invariance,
  determinism, reuse tests <= 5 s each; `ReaderFuzzTests` green for GeoTIFF.
- Benchmarks on procedural km-scale 1 m grids (Hambach-like synthetic waves) with before/after numbers in PR.
- Incremental mutation gate on `terrain-*` shards if exact terrain files change.

## Sources and licences

OGC GeoTIFF 1.1 spec (public standard). Never port GPL libraries (GDAL / CGAL) or read the owner's old quadtree.

## Budget and stop rule

One PR-sized session per sub-item. Stop if a GeoTIFF compression/projection feature pulls in full CRS reprojection
(keep CRS out of scope: load raw grid + GeoKey affine transform into `LocalFrame` only).

## Risks and open questions

- GeoTIFF has many legacy variants (tiled, LZW/Deflate/PackBits, multi-band, NoData): scope PR 1 explicitly to
  single-band float32/float64 uncompressed + Deflate (via `System.IO.Compression`) and return a clear `UnsupportedFormat`
  status for everything else.
