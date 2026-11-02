# TerrainBenchmark

Benchmark of the terrain functions of geometry3Sharp on real, openly licensed geodata of an open-pit mine:
TIN volumes between surveys, safety lines at crests, terrain offsets, dual contouring of TINs and point
clouds, the ground filter, geodesic distances and the mesh repair on terrain TINs.

## Data
Geobasis NRW (North Rhine-Westphalia, Germany), <https://www.opengeodata.nrw.de/produkte/geobasis/hm/>,
license **Datenlizenz Deutschland – Zero – Version 2.0** (dl-de/zero-2-0, <https://www.govdata.de/dl-de/zero-2-0>).
ETRS89 / UTM zone 32N (EPSG:25832), heights DHHN2016. No geodata is committed; `fetch_data.py` downloads
fixed tiles (sha256 in `data/files.sha256`) and converts them.

| name | product | content |
|---|---|---|
| `dgm2022` | DGM1 (GeoTIFF), airborne laser scan 2022 | 1 m terrain grid, 3 x 4 km of the Hambach lignite mine (E 326–329 km, N 5640–5644 km): cut faces of the advancing pit and the internal dump |
| `bdom2025`, `bdom2025_1m` | bDOM50 (LAZ), aerial images 2025 | 0.5 m image-based surface model of the same area; `_1m` resampled to the DGM nodes. In the active pit (bare faces and benches) surface = terrain, so DGM 2022 vs bDOM 2025 are three years of real excavation and dumping (about 20 Mio. m³ cut per km²) |
| `3dm_322_5642`, `3dm_327_5641` | 3D-Messdaten (LAZ), laser scan 2022 | classified point clouds (9–12M points per km²): the pit edge with the remains of the Hambach forest, and the pit interior |

Note: in these clouds class 2 is ground, class 1 unclassified (vegetation, buildings, vehicles), class 20
is mostly near the ground (median 0.1–0.3 m above the DGM) with a tail up to 20 m, class 26 synthetic points.

## Usage
```
pip install numpy tifffile imagecodecs laspy lazrs
python3 fetch_data.py                 # ~0.9 GB downloads, ~0.8 GB converted
BENCH_P=1 ./run_all.sh all            # all cases, results/all.jsonl (the 24M+24M triangle cases need ~10 GB)
./run_all.sh safety '^safety'         # only cases matching a regex
python3 analyze.py results/all.jsonl  # tables; volumes against the numpy reference
./run.sh tdc grid=dgm2022 win=326000,5641000,327000,5642000 coords=local   # a single case
```
Each case runs in its own process with a timeout (`TIMEOUT`, default 1800 s) and prints one JSON line
(timings, peak memory, results, checks); exceptions, timeouts and crashes are recorded too. `coords=utm`
keeps the UTM coordinates (~3.3e5 / 5.6e6), `coords=local` shifts everything to the window corner.

## Checks (independent of the code under test)
- **Volumes** (`reference.py`, numpy): on a common grid both TINs (cells split along the same diagonal)
  have the same triangles, the difference is linear per triangle, and its positive and negative parts are
  integrated in closed form (exact). Different grids (1 m DGM vs 0.5 m bDOM, offset by 0.25 m): both TINs
  sampled on a 0.125 m grid (midpoint rule; 0.0625 m changes cut by 5e-8, fill by 4e-5). Also the plain
  DEM of difference (sum of height differences x cell area) for comparison.
- **Surface distances** (`Checks.SurfaceDijkstra`): shortest paths on the grid TIN over 32 directions,
  each link measured exactly along the TIN: surface paths, so upper bounds of the geodesic distance (about
  1% long). Between nodes the bound goes through the cell corners (`SurfaceBound`); linear interpolation of
  the node values is no bound (too small at ridges of the distance field). The straight 3D distance is a
  lower bound.
- **Offsets**: exact point-triangle distance of the result vertices to the grid TIN.
- **Repair**: still a height field (XY overlaps, exact predicates), the same top surface (vertical rays on
  200 x 200 samples against the grid TIN), boundary, closedness, down-facing area.
- **Ground filter**: confusion against the LAS classes (ground 2 vs non-ground 1) and against the DGM
  (accepted points more than 0.5 m above it).

## Results (4 cores, 16 GB; geometry3Sharp after the fixes below)

### TerrainVolumeDifference
| case | triangles | time | peak memory | error cut / fill |
|---|---|---|---|---|
| DGM 2022 vs bDOM 2025 on the DGM grid, 1 km² | 2M + 2M | 17 s | 1.0 GB | 1e-14 / 8e-15 (exact) |
| same, 4 km² | 8M + 8M | 69 s (31 s without the height-field check) | 3.9 GB | 6e-14 |
| same, 12 km² | 24M + 24M | 177 s | 9.4 GB | 4e-13 / 1e-14 |
| DGM 1 m vs bDOM 0.5 m (different grids), 1 km² | 2M + 8M | 29 s | 2.9 GB | 4e-7 / 1e-4 (sampling) |
| same, 4 km² | 8M + 32M | 116 s | 9.9 GB | 3e-7 / 1e-4 |
| both simplified with TerrainDualContouring, tol 0.05 / 0.2 m, 1 km² | 1.5M / 0.4M | 3.7 / 1.0 s | | 1e-6 / 6e-5 (cut), 0.2% / 1% (the small fill) |

UTM and local coordinates give identical volumes. The 12 km² window: 144 Mio. m³ cut, 129 Mio. m³ fill.
The plain DEM of difference differs from the TIN volume by 0.02–0.16% (half cells at the window border).

### Repair of terrain TINs (500 x 500 m, 0.5M triangles)
Variants: the clean grid TIN; 2 x 2 tiles not welded (seams); tiles overlapping by 2 cells; 16 no-data holes.

| method | result |
|---|---|
| `MeshAutoRepair`, also with `PostRepairCleanup` | closes the outer boundary with a lid: a closed solid, 1.2–1.4M triangle pairs overlap in XY, the lid lies up to 19–23 m above the pit floor (1 km²: 38 m) |
| `MeshRobustRepair` | not clean (self-intersections), falls back to solidify: a closed coarse solid, top surface off by up to 4–23 m |
| `TerrainDualContouring` (rebuild from vertical rays) | seams welded, overlaps removed, holes kept as holes; a height field again, top surface within 0.24 m (p50 0.7 mm), 4–5 s |

`MeshAutoRepair` / `MeshRobustRepair` repair solids; an open terrain TIN always comes back closed. For terrain
use `TerrainDualContouring` (it also handles soups and overlapping survey files).

### TerrainDualContouring (vertical error at the DGM nodes)
| input | time | memory | triangles | RMS / p99 / max |
|---|---|---|---|---|
| DGM 1 km² (2M triangles), tol 0.05 m | 15 s | 1.1 GB | 0.82M | 2.6 / 8.4 cm / 1.0 m |
| same, tol 0.2 m | 13 s | 0.8 GB | 0.19M | 7.9 / 28 cm / 1.9 m |
| DGM 12 km² (24M triangles) | 300 s | 10 GB | 11M | 2.6 / 8.4 cm / 1.4 m |
| ground points of the laser scan, 1 km² (7.5–9.4M points) | 11–12 s | 2.2 GB | 1.2–1.4M | 3.0–4.6 / 11–16 cm |

The maxima are at vertical walls and conveyor/equipment edges. No XY overlaps, no NaN, UTM = local.

### TerrainGreedyTIN (greedy insertion, Garland and Heckbert 1995) vs TerrainDualContouring
Same DGM, vertical error at every grid node (rays against the result):

| method | area | triangles | max error | p99 | time | memory |
|---|---|---|---|---|---|---|
| TerrainGreedyTIN, MaxError 0.05 m | 1 km² | 0.76M | **0.05 m (guaranteed)** | 4.8 cm | 3.2 s | 0.55 GB |
| TerrainDualContouring, Tolerance 0.05 m (RMS) | 1 km² | 0.82M | 1.0 m | 8.4 cm | 15 s | 1.0 GB |
| TerrainGreedyTIN, MaxError 0.2 m | 1 km² | 0.20M | **0.20 m (guaranteed)** | 18 cm | 1.4 s | 0.38 GB |
| TerrainDualContouring, Tolerance 0.2 m | 1 km² | 0.19M | 1.9 m | 28 cm | 13 s | 0.8 GB |
| TerrainDualContouring, Tolerance 0.2 + MaxError 0.2 m | 1 km² | 0.32M | 0.92 m (walls finer than a cell) | 17 cm | 55 s | 1.2 GB |
| TerrainGreedyTIN, MaxError 0.5 m | 1 km² | 0.065M | 0.5 m | 42 cm | 0.7 s | 0.32 GB |
| TerrainGreedyTIN, MaxError 0.05 / 0.2 m | 12 km² | 9.9M / 2.8M | 0.05 / 0.2 m | | 56 / 16 s | 4.6 / 1.5 GB |
| TerrainGreedyTIN + RemoveVertices, MaxError 0.05 m | 1 km² | **0.52M** (-31%) | 0.05 m (guaranteed) | 4.8 cm | 21 s | 0.68 GB |
| TerrainGreedyTIN + RemoveVertices, MaxError 0.2 m | 1 km² | **0.12M** (-40%) | 0.20 m (guaranteed) | 18 cm | 5.8 s | 0.38 GB |
| TerrainGreedyTIN + RemoveVertices, MaxError 0.5 m | 1 km² | **0.036M** (-46%) | 0.5 m (guaranteed) | 43 cm | 2.7 s | 0.32 GB |

RemoveVertices removes vertices again where the retriangulated hole stays within MaxError (31–46% fewer
triangles here, 94% on synthetic benches). Data-dependent flips during the insertion (Garland and
Heckbert's variant) gave more triangles, not fewer, in all criteria tried (max error, sum of squares).
Seeding the insertion with the crests and toes of TerrainBreakLines (77k or, above 40 degrees, 6k nodes)
gave the same triangle count after RemoveVertices (119.7k–122.5k vs 119.6k at 0.2 m): the removal already
aligns the triangles with the break lines.
For a height grid, greedy insertion is 5–10x faster, needs less memory and guarantees the maximum error
at every node (the result is Delaunay in XY, so a height field by construction). TerrainDualContouring
remains the tool for broken meshes (soups, overlapping survey files) and point clouds.


### Fidelity of the simplified TINs (`gtin ... fidelity=1`, `MeshFidelityMetrics`)
The TerrainGreedyTIN results of the 1 km² window against the TIN of all 1M grid nodes (`fm_*` fields of
`benchmarks/Shared/FidelityFields.cs`, 50k samples; open terrain, so no volume IoU). Relative to the window's size
(1423 m): H * size and K * size² (`MeshCurvature`, Meyer et al. 2003), errors at corresponding points (mean of
both directions), W1 = 1-Wasserstein distance of the curvature distributions, totals Σ|H| A / size and Σ|angle
deficit| of the Valid vertices (grid TIN: 59.0 and 12844):

| MaxError | RemoveVertices | triangles | F 0.1 % | normal consistency | edge recall 0.5 % | H error | H W1 | K W1 | Σ\|H\| A / size | Σ\|deficit\| |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0.05 m | | 758k | 1.000 | 0.9962 | 0.999 | 40.7 | 9.7 | 2831 | 54.2 | 10496 |
| 0.05 m | yes | 525k | 1.000 | 0.9940 | 1.000 | 54.3 | 4.2 | 3774 | 60.2 | 9815 |
| 0.2 m | | 200k | 1.000 | 0.9927 | 0.998 | 61.0 | 34.0 | 9353 | 37.6 | 5772 |
| 0.2 m | yes | 120k | 1.000 | 0.9896 | 0.998 | 67.9 | 30.4 | 10161 | 40.1 | 4913 |
| 0.5 m | | 65k | 1.000 | 0.9885 | 0.976 | 71.8 | 50.5 | 13200 | 25.1 | 2644 |
| 0.5 m | yes | 36k | 1.000 | 0.9857 | 0.976 | 74.9 | 49.6 | 13902 | 25.2 | 2074 |
| 1 m | | 25k | 1.000 | 0.9836 | 0.912 | 76.4 | 60.8 | 15094 | 17.2 | 1181 |
| 1 m | yes | 12k | 1.000 | 0.9805 | 0.913 | 78.2 | 61.0 | 15561 | 16.4 | 798 |

- The F-score cannot tell the TINs apart: at 0.1 % of the size (1.4 m) every one is within the threshold. The
  curvature measures order them by MaxError, as do normal consistency and edge recall at a lower resolution.
- The pointwise H error is about the mean |H| of the grid TIN (82.6) already at 0.05 m: the 1 m laser scan grid
  carries curvature noise at the node spacing, which any simplification removes. The distribution distance and
  the totals show what is kept: the share of the grid's total |H| (92 % at 0.05 m, 29 % at 1 m) and of its total
  |angle deficit| (82 %, 9 %).
- RemoveVertices at 0.05 m keeps the curvature distribution better (H W1 4.2 instead of 9.7, total |H| 102 % of
  the grid's) at 31 % fewer triangles; at 0.2 m and above it is about the same.
- `coords=local` (0.2 m) gives the same values as UTM up to 1e-8 relative (H error 60.973698924 vs 60.973698712).
- The metrics take 16-35 s per case (50k samples, a 2M triangle reference).

### TerrainOffset (250 m crop, d = ±5, 20 m; 500 m crop, d = 10 m)
The distance of the result vertices to the terrain is |d| within -9 / +19 cm (p01 / p99: -3 / +6 cm), none on
the wrong side; 15–24 s for 250 x 250 m, 115 s for 500 x 500 m.

### TerrainSafetyLines (crests from TerrainBreakLines on the DGM, 3.4 km of crests in 250 x 250 m)
| mode | d | time | too close (along the surface) | distance - d: p01 / p50 |
|---|---|---|---|---|
| Horizontal | 5 m | 2.4 s | – (horizontal distance exact to 1e-9 m) | |
| AlongSurface (heat method) | 5 m | 11 s | 11% of the line points | -0.17 / +0.20 m |
| AlongSurface | 10 m (500 m crop) | 13 s | 8% | -0.34 / +0.50 m |
| Guaranteed | 5 m | 66 s | none | +0.01 / +0.11 m |
| Guaranteed | 10 / 20 m (500 m crop) | 266 / 108 s | none | +0.02 / +0.17, +0.03 / +0.19 m |

Guaranteed lies at the straight 3D distance (a lower bound) for certain; its certificate (upper bounds of
the excess, whole segments) is within 0.2 m for 96% (d = 5 m) to 76–82% (d = 10–20 m) of the samples, up to
0.9 m where the path along the terrain bends over another bench. AlongSurface is a best estimate, not a
bound: use Guaranteed for safety lines.

### MeshGeodesicDistance (heat method) vs surface Dijkstra
| case | vertices | time | relative error p01 / p50 / p99 |
|---|---|---|---|
| one source, 250 m / 500 m / 1 km | 62k / 250k / 1M | 3.6 / 19 / 210 s | -3.5 / -1.1 / +0.1% … -2.6 / -0.9 / +0.2% |
| 3100 crest vertices as sources, 250 m | 62k | 4.5 s | -2.9 / +2.5 / +11% |

### ClothSimulationFilter (1 km² laser scan tiles)
| tile | points | time | type I / type II | kappa | accepted > 0.5 m above the DGM |
|---|---|---|---|---|---|
| pit edge with forest | 12.1M | 10 s | 0.54% / 0.04% | 0.986 | 0.5% |
| same, cloth 0.5 m | 12.1M | 63 s | 0.004% / 0.63% | 0.996 | 1.2% |
| pit interior (benches, equipment) | 8.8M | 11 s | 1.8% / 1.1% | 0.59 (few non-ground points) | 0.4% |

1.0–1.3 GB; UTM = local.

## Library defects found with this benchmark (fixed, each with a test failing on the old code)
1. `MeshGeodesicDistance` stalled (Compute false after 140 s) on meshes with caps, eg the remeshed band of
   AlongSurface safety lines along real crests: now on the intrinsic Delaunay triangulation (15 s).
2. `MeshGeodesicDistance` with several sources put the surroundings of all but one source up to 5 m too
   far (AlongSurface safety lines too close to the crest): phi is now 0 at every source; a single source is
   not pinned (pinning one vertex made it up to 11% too short near the source on rough terrain).
3. `TerrainDualContouring` produced triangles overlapping in XY (24 in 11M, 46 from a point cloud), not a
   height field, so the volume of a simplified survey failed: overlapping merged cells are split.
4. New: `TerrainSafetyLines.Modes.Guaranteed` with a certified excess (the heat method has no direction of
   its error).

### TerrainSlopeDesign (`slope`, terrain phase 1a)

Procedural toes, no data needed: `dotnet bin/Release/net10.0/TerrainBenchmark.dll slope [toe=ngon|lshape|wavy] [n=24]
[r=200] [h=60] [ratio=1] [tol=0.01] [coords=utm|local]` (closed toe, slope outside, up to h above the toe). Printed: the
time per phase (`phase_*_s`, summed over the builds), the builds (`attempts`), the kernel's predicate calls and exact
fallbacks, the toe lowering, and the oracle (`Slope.IsSupportedBy`) time per output triangle. 4 cores, 2026-11-01,
`Tolerance` 0.01, slope 1:1:

| case | triangles | builds | time | insert / simplify | predicates (exact) | toe lowering | oracle |
|---|---|---|---|---|---|---|---|
| 24-gon r 20 m, h 6 m, local | 461 | 2 | 1.36 s | 0.62 / 0.52 s | 2.19M (3.7 %) | 1.3e-10 m | 0.95 µs |
| 24-gon r 200 m, h 60 m, local | 1230 | 2 | 8.6 s | 4.9 / 3.5 s | 36.2M (1.6 %) | 5.7e-10 m | 0.92 µs |
| 24-gon r 200 m, h 60 m, UTM | 1222 | 2 | 9.0 s | 5.0 / 3.8 s | 36.2M (2.7 %) | 4.7e-8 m | 1.2 µs |
| L shape 200 m, h 30 m, UTM | 548 | 1 | 0.62 s | 0.25 / 0.19 s | 0.39M (3.9 %) | 7.5e-9 m | 1.6 µs |
| wavy 64-gon r 100 m (toe ±4 m), h 30 m, UTM | 14577 | 3 | 43 s | 17 / 26 s | 79.5M (7.5 %) | 5.9e-6 m | 1.6 µs |

Constructions (vertex positions, heights) are all exact and correctly rounded: 2k to 290k per run, against millions of
predicates. The toe lowering is measured: the first build lowers by the rounding of one vertex (4 e_z), and a build whose
support check fails is repeated with 4 x the largest measured excess, so most cases take 2 builds. A derived start (the
a-priori bound e_z (1 + 4 D / h) per triangle) was considered: it needs the altitudes h of the rounded output triangles,
which exist only after the build, and the build depends on λ, so it is no proven bound before the first build; the
second build (about 2x the time, 4.8 s -> 9.0 s in the UTM case) is kept, and avoiding it is a candidate for 1c.

Before / after in this PR, 24-gon r 200 m, h 60 m, UTM, one build each (the toe lowering then fixed at 514 e_z): 9.2 s
at 7e8d251 (simplify 6.1 s, 1.45M exact of 19.2M predicates); 4.8 s with failed merges not retried and the apex shortcut
(simplify 2.1 s, 0.49M of 18.1M). An earlier note of "> 30 min" here was wrong: that run was one command with four
cases in a row, stopped as a whole after 30 min. Open for 1c: the uneven toe (3 builds), the predicate count per
output triangle (15000 to 30000), the km-scale cases of TERRAIN.md section 8.


### TerrainSlopeDesign with a terrain (`slope ... terrain=wave`, terrain phase 1b)

The same toes in a grid terrain h above the toe with a wave of `amp` (default 0.2 h), grid spacing `tgrid`:
`dotnet bin/Release/net10.0/TerrainBenchmark.dll slope n=24 r=200 h=60 coords=utm terrain=wave tgrid=5 [composite=1]`.
`HeightLimit` comes from the terrain (its highest vertex + `Tolerance`). Printed in addition: the overlay and composite
phases, the catch lines and points, the composite and slope-region triangles, `XYOverlaps` of the composite and the
oracle on every slope-region triangle. 4 cores, 2026-11-02, `Tolerance` 0.01, slope 1:1, 24-gon:

| case | terrain tris | builds | time | overlay / composite | catch points | region / composite tris | overlaps | oracle | toe lowering |
|---|---|---|---|---|---|---|---|---|---|
| r 20 m, h 6 m, tgrid 1, local | 7688 | 3 | 2.9 s | 0.08 / 0.41 s | 606 (1 closed line) | 1157 / 7993 | 0 | 1157 / 1157 | 2.2e-11 m |
| r 20 m, h 6 m, tgrid 1, UTM | 7688 | 2 | 2.6 s | 0.09 / 0.43 s | 608 (1 closed) | 1205 / 8044 | 0 | 1205 / 1205 | 7.5e-9 m |
| r 200 m, h 60 m, tgrid 5, UTM | 28322 | 4 | 29.6 s | 0.27 / 0.91 s | 1282 (1 closed) | 3129 / 26384 | 0 | 3129 / 3129 | 1.6e-6 m |

The overlay and the composite are a few percent of the time; the rest is the slope surface of 1a, built up to the
terrain's highest point. The number of builds is 1a's at that height and does not come from the composite here: with
`composite=0` the last case also takes 4 builds (27.5 s), and 1a alone takes 2 to 3 builds for heights between 71.9 and
72.2 m (12.6 to 18.6 s). Where clipped slope triangles fail the support check, the composite raises the measured toe
lowering in the same loop (a build each). Avoiding repeated builds stays a 1c item.

### Terrain 1c, idea A: one build per margin, the toe lowering by re-realization

`TERRAIN_1C_PLAN.md`, idea A: the map is built once per margin δ from the specified toe, and a larger toe lowering λ
only realizes it again (z lowered by λ, exactly rounded), since lowering every plane by λ does not change any line of
two planes. Same machine, 2026-11-02, before = #285 (1b) head d38b030e, after = idea A; same commands as above
(`Tolerance` 0.01, 1:1):

| case | before | after | speedup | builds before / after | triangles |
|---|---|---|---|---|---|
| wavy 64-gon r 100 m (toe ±4 m), h 30 m, UTM | 44.6 s | 30.9 s | 1.44× | 3 / 2 | 14577 / 14577 |
| 24-gon r 200 m, h 60 m, UTM | 9.7 s | 5.2 s | 1.89× | 2 / 1 | 1222 / 1222 |
| 24-gon r 20 m, h 6 m, local | 1.39 s | 1.00 s | 1.39× | 2 / 1 | 461 / 461 |
| L shape 200 m, h 30 m, UTM | 0.93 s | 0.71 s | 1.31× | 3 / 2 | 567 / 567 |
| 24-gon r 200 m, h 60 m, UTM, `terrain=wave tgrid=5` | 36.4 s | 17.0 s | 2.14× | 5 / 2 | 2281 / 2281 |

The upper bound of the plan for the wavy case was 2.8× (one build left); 1.44× is reached because its second build is
a margin raise (a slope failure), which still needs a new map. The results are bit for bit those of a new build at
the final lowering (`TerrainSlopeOneBuildTests`, 19 cases: 20 builds saved, 14 with a raised lowering). The level line
of the cap now lies at H − λ (the map is lowered as a whole, the cap with it, so its lines stay where they are).

### Slope design on the Hambach DGM (`slope ... terrain=dgm`)

The DGM1 2022 of `fetch_data.py` (only the 12 DGM tiles are needed: `dgm2022`, 3 x 4 km, no gaps, z −325 to +103 m) as
the terrain: `slope ... cx=<E> cy=<N> cz=<toe z> terrain=dgm [grid=dgm2022] [tstride=1] [reach=400]`, the grid nodes
within `reach` of the toe, every `tstride`-th. A hypothetical new pit floor below the deepest part of today's pit
(E 328375, N 5642875, floor around −300 m), cut slope up to today's terrain, `Tolerance` 0.05 unless noted, 4 cores,
2026-11-02, with idea A of 1c:

| toe | grid | terrain tris | time | 1a / overlay / composite | builds | catch lines (points) | composite tris | overlaps | O1 | λ | status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| octagon r 100 m at −330 m, 1:1.5 | 4 m | 124500 | 4.3 s | 2.2 / 0.58 / 0.85 s | 1 | 1 closed (967) | 122830 | 0 | 1511 / 1511 | 8.6e-8 m | Success |
| the same | 1 m | 2417800 | 43.6 s | 2.7 / 21.9 / 14.7 s | 1 | 1 closed (2651) | 2352484 | 0 | 3200 / 3200 | 3.7e-7 m | Success |
| L shape 120 m at −335 m, 1:2 | 2 m | 612180 | 12.5 s | 2.1 / 5.5 / 3.4 s | 1 | 2 closed (3049) | 542894 | 0 | 3319 / 3319 | 5.0e-6 m | Success |
| wavy 48-gon r 150 m (toe z ±4 m) at −340 m, 1:1.5 | 2 m | 749070 | 135 s | 132 s in 1a | 1 | – | – | – | – | 5.0e-9 m | **Failed** (non-manifold realization) |
| the same, after the fix of d744efb8 | 2 m | 749070 | 966 s | 926 / 20 / 9.3 s | 3 (δ 6.4e-5) | 1 closed (7523) | 960980 | 0 | 253093 / 253093 | 4.0e-5 m | Success, peak 5.1 GB |
| the same, with the needle flips | 2 m | 749070 | 157 s | 137 / 10.4 / 5.7 s | 1 | 1 closed (5105) | 794379 | 0 | 89355 / 89355 | 3.8e-6 m | Success, peak 2.9 GB |
| the same, `Tolerance` 0.2 | 2 m | 749070 | 92 s | 90 s in 1a | 3 | – | – | – | – | 1.1e-2 m | **SlopeViolation** (13 triangles at δ 5e-4) |

Without the terrain, the wavy toe succeeded up to h 60 m (12 s) and failed with `SlopeViolation` (1 triangle) at
h 100 m. After d744efb8 (two realization defects: needles from edge collapses next to collinear vertices, and faces
narrower than the rounding) h 100 m takes one build (24 s, a unit test now) and the full case succeeds, but its margin
was still raised twice, and the smaller facet share left a smaller facet angle, so 16 min and 253k slope triangles. The
cause: faces of the map between two creases that meet at an angle of 1e-12 (nearly coplanar tangent planes of
neighbouring apexes), which can only be triangulated into needles 90 m long and 1e-10 m wide; flipping needles thinner
than 64 ulps across their sides removes them (h 130 m: one build, 44 s instead of 3 builds and 157 s, a unit test).
The full case then takes one build, 157 s; what is left is the slope surface itself (insert 50 s, simplify 87 s),
ideas B to D of the 1c plan. Open, for 1c: the uneven toe at large heights (230 m here, reach about 350 m) fails in 1a, once with a
non-manifold realization that is reported as `Failed`, not caught by the check; and on the 1 m grid the overlay
(22 s) and the composite (15 s) dominate, which the band-only overlay of 5.5 addresses. Peak memory 3.1 GB on the 1 m grid.

After the local height limit (the cap at the highest terrain near the toe, raised while the region reaches it), the
overlay restricted to the terrain triangles within the box of the slope surface, and the terrain check reduced to
finiteness (2026-11-02, the same machine; phase times summed over both builds):

```
dotnet bin/Release/net10.0/TerrainBenchmark.dll slope <toe> cx=328375.25 cy=<N> cz=<toe z> ratio=1.5 tol=0.05 terrain=dgm reach=450 [tstride=1|2] [composite=0]
```

| toe | grid | terrain tris | time | terrain check / 1a / overlay / composite | builds | catch lines (points) | composite tris | overlaps | O1 | λ | peak |
|---|---|---|---|---|---|---|---|---|---|---|---|
| one edge, 200 m at −320 m (N 5642700.5), catch line only | 1 m | 1978200 | **0.82 s** | 0.06 / 0.02 / 0.42 / – s | 2 | 1 (426) | – | – | – | 5.0e-9 m | 0.48 GB |
| the same, with the composite | 1 m | 1978200 | 5.6 s | 0.08 / 0.02 / 0.46 / 4.6 s | 2 | 1 (426) | 1972425 | 0 | 421 / 421 | 5.0e-9 m | 1.06 GB |
| octagon r 100 m at −330 m | 1 m | 2417800 | 24.4 s (43.6) | 0.08 / 1.8 / 9.9 / 12.2 s | 2 | 1 closed (2548) | 2352098 | 0 | 2920 / 2920 | 1.2e-7 m | 1.74 GB (3.1) |
| L shape 120 m at −335 m | 2 m | 612180 | 6.7 s | 0.03 / 1.2 / 2.5 / 2.7 s | 2 | 2 closed (2089) | 573032 | 0 | 2370 / 2370 | 2.4e-6 m | 0.67 GB |
| wavy 48-gon r 150 m at −340 m | 2 m | 749070 | 87.6 s (157) | 0.04 / 74.3 / 7.0 / 3.8 s | 2 | 1 closed (4357) | 759911 | 0 | 55769 / 55769 | 1.4e-6 m | 1.55 GB (2.9) |

(Earlier values in parentheses. All rows at 1:1.5: the L shape row above at 1:2 is not comparable. Two builds: the
local cap was raised once.) One toe edge on the 1 m grid now gives its catch line in 0.82 s (before: 4.3 s, of which
the terrain check 2.8 s and the overlay index 1.1 s); with the composite it takes 5.6 s, nearly all of it copying
the 2 million untouched terrain triangles into the composite. This phase split is the baseline for the band-only
overlay and composite (5.5 of the 1c plan); the peak of 3.1 GB on the 1 m grid is its memory target. On the wavy
toe the slope surface itself (insert and simplify, 74 s) is what remains, ideas B to D.