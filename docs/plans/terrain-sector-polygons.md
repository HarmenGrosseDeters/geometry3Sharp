# Plan: terrain sector polygons (E1 exterior distance, S1 sectors, R2 attribution)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Terrain: sector polygons E1, S1, R2"
- **Kind:** parked (owner 2026-11-16; trigger: terrain track resumes with multi-sector geotechnical / concave-pit use case)
- **Tier:** L (exact visibility graph, clipped cone envelopes, weighted-region geodesic distance; exact predicates + `terrain-*` mutation gate)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Design approved earlier (owner 2026-11-13) and then **parked on 2026-11-16** when terrain was paused
  ([TERRAIN_SECTORS.md](../design/TERRAIN_SECTORS.md)):
  - **Nothing is built yet:** `TerrainSlopeDesign.Modes.SectorPolygons` and `Modes.ExteriorDistance` exist only in the
    API sketch (`TERRAIN.md` §6), not in code.
  - Design choices already agreed (2026-11-13):
    1. **E1 (exterior distance) first:** shortest path around a closed polygonal design area (Lee & Preparata 1984: bends
       only at reflex vertices), secondary cones at reflex vertices clipped to their shadow wedges, exact orientation
       tests for visibility, conservative lower-bound heights $z_v$.
    2. **R2 attribution:** slope attributed by the sector polygon containing the wall point (smaller slope on a sector
       boundary), rather than nearest toe point (R1).
    3. **S1 (sector polygons):** weighted geodesic distance $d_\sigma(x, q)$ for piecewise-constant slope field $\sigma$
       (Mitchell & Papadimitriou 1991 weighted region problem, Snell's law refraction at sector boundaries), built as an
       $\varepsilon$-approximation within the `Tolerance` budget while keeping the exact "never steeper" guarantee.

## Trigger / decision needed

Terrain track resumes AND a real open-pit / geotechnical use case requires either concave-pit exterior distance (E1)
or polygonal geotechnical slope domains (S1 + R2). Build as two PRs: PR 1 = E1, PR 2 = S1 + R2.

## Goal

Extend `TerrainSlopeDesign` with (PR 1) exact conservative exterior-distance cones around closed toes (E1) and (PR 2)
geotechnical sector polygons with R2 location-based slope attribution (S1 + R2), preserving the exact "never steeper"
oracle guarantee.

## Scope

- `mesh_ops/TerrainSlopeDesign.cs` (and new helpers `mesh_ops/TerrainExteriorVisibility.cs`,
  `mesh_ops/TerrainSectorEnvelope.cs`)
- `mesh_ops/TerrainSlopeOracle.cs` (extend exact oracle for R2 per-sector slope verification, taking the smaller slope
  on triangles straddling a sector boundary)
- `tests/geometry3Sharp.Tests/TerrainSector*Tests.cs`
- `docs/design/TERRAIN_SECTORS.md`, `docs/design/TERRAIN.md`, `docs/OPERATIONS.md`

## Non-goals

- Default R1 mode (without exterior distance / sectors) must remain bit for bit identical.
- Clean-room rule: any 2D spatial lookup uses `spatial/*Quadtree2d*` from this repo only.

## Interface

Additive opt-in properties / modes on `TerrainSlopeDesign` (matching `TERRAIN.md` §6 sketch):
- `bool UseExteriorDistance = false` (E1)
- `IReadOnlyList<TerrainSlopeSector>? Sectors` (S1 + R2: polygon + slope angle/ratio; overlapping or invalid sectors
  return an explicit `StatusKind.InvalidSectors`)

## Look before you build

1. Library: `docs/design/TERRAIN_SECTORS.md`, `docs/design/TERRAIN.md`, `mesh_ops/TerrainSlopeDesign.cs` (lateral-limit
   barriers in the planar map §5.3 — directly reusable for E1 shadow wedges), `math/AdaptivePredicates.cs` (`Orient2d`),
   `math/CoreMath.cs` (`Tan`/`Acos`), `comp_geom/ExactTriangulation2.cs`.
2. Literature:
   - Lee & Preparata (1984), "Euclidean shortest paths in the presence of rectilinear barriers" / visibility graphs for
     polygonal obstacles.
   - Mitchell & Papadimitriou (J. ACM 1991), "The weighted region problem: Finding shortest paths through a weighted
     planar subdivision" (Snell's law refraction, $\varepsilon$-approximation).
   - Aleksandrov, Maheshwari, Sack (J. ACM 2005), practical Steiner-point $\varepsilon$-approximation for weighted
     regions (alternative to full continuous Dijkstra if needed).

## Steps (two PRs: PR 1 = E1, PR 2 = S1 + R2)

- **PR 1 (E1):**
  1. Build exact visibility graph over toe + reflex vertices using `Orient2d`; compute conservative lower-bound heights
     $z_v$ (rounded down) via Dijkstra.
  2. Emit secondary cones at reflex vertices clipped by shadow-wedge lateral barriers in the existing planar map.
  3. Test on concave U-shaped / L-shaped / spiral pits (origin + UTM `LocalFrame`): verify no cone leaks through the
     interior, exact slope oracle passes, `Similarity.All()`, determinism, reuse.
- **PR 2 (S1 + R2):**
  1. Extend exact oracle for R2 (smaller slope on boundary-straddling triangles).
  2. Implement weighted-region $\varepsilon$-approximation within `Tolerance` (discrete Snell / Steiner graph or
     window propagation, measured against brute-force fine grid oracle).
  3. Test two-sector, four-sector, and concentric-ring domains against the exact R2 oracle + invariance/determinism.

## Acceptance

- PR 1 & PR 2: 100 % pass of exact slope oracle (`TerrainSlopeOracle`) including R2 sector boundaries; over-excavation
  within `Tolerance` measured against an independent high-res grid reference; default R1 bit-identical.
- All PR tests <= 5 s; independent exact-code review + incremental mutation gate (`terrain-arith`, `terrain-kernel`,
  `terrain-overlay`) green.

## Sources and licences

Lee & Preparata (1984), Mitchell & Papadimitriou (1991), Aleksandrov et al. (2005) — papers only; no GPL code.

## Budget and stop rule

1 L session for E1; 1-2 L sessions for S1 + R2. Stop S1 if the $\varepsilon$-approximation cannot meet `Tolerance`
within 5 s on a 4-sector km-scale pit (report trade-off to owner).

## Risks and open questions

- Sector polygons crossing terrain boundaries or containing holes: define strict validation rules (non-crossing,
  closed polygons; fallback default slope outside sectors) in `TERRAIN_SECTORS.md` before coding PR 2.
