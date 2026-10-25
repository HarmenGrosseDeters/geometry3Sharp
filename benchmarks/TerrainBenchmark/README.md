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
