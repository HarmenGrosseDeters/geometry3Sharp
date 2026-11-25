# g3cli 2D, Terrain & Lattice Commands

Detailed reference for 2D meshing (`mesh2d`), 2D quadtree queries (`query2d`), 2D polygons and hulls (`polygon2d`, `polygon_solids`, `hull2d`), height grids (`dgm`, `dgm_tin`), slope design (`slope`), cut/fill cell volumes (`cell_volumes`), and TPMS lattices (`tpms`). See [../README.md](../README.md) for common arguments and JSON envelopes.

---

## `mesh2d`

Builds a 2D constrained Delaunay triangulation (`--quality false`, `ConstrainedDelaunayMesh2`) or refined quality mesh (`QualityMesh2`) from JSON or Triangle `.poly`/`.node` files.

```bash
g3cli mesh2d field.json field.obj --min-angle 30 --max-area 50
g3cli mesh2d --geometry '{"points":[[0,0],[4,0],[4,3],[0,3]],"segments":[[0,2]]}' --output plain.off --quality false
g3cli mesh2d lake.poly lake.ele --min-angle 30 --max-area 2
g3cli mesh2d parcels.poly parcels.obj --snap-tolerance 0.01
```

### JSON Input Schema (`mesh2d`)

Only `points` is required; vertex indices are 0-based:

```json
{
  "points": [[0,0], [100,0], [100,60], [0,60], [40,20], [60,20], [60,40], [40,40], [70,0], [70,60]],
  "heights": [10, 20, 26, 16, 14, 16, 18, 16, 17, 23],
  "rings": [[0, 1, 2, 3], {"vertices": [4, 5, 6, 7], "hole": true, "marker": 0}],
  "segments": [[0, 2], {"a": 8, "b": 9, "marker": 5, "locked": false}],
  "hole_seeds": [[50, 30]],
  "regions": [{"seed": [10, 10], "label": 1}, {"ring": [1, 2, 9, 8], "label": 2, "max_area": 5}]
}
```

- `rings`: outer boundaries or holes (`"hole": true`, even-odd rule; without outer rings, domain is the convex hull).
- `segments`: constraints (`"locked": true` prevents splitting during refinement).
- `regions`: labeled by `seed` or `ring` (last wins on overlap) with optional per-region `max_area`.
- `heights`: one `z` per point, interpolated onto Steiner vertices (defaults to `0`).
- **Options:** `--quality false`, `--min-angle`, `--max-area`, `--steiner-limit`, `--conforming true`, `--resolve-crossings false`, `--snap-tolerance t`, `--poly-heights`.
- **Output & additive fields:** Mesh lies in XY counterclockwise with labels as triangle groups (OBJ `g mmGroup<label>`). Reports `triangles`, `vertices`, `added_vertices`, `smallest_angle`, `largest_area`, `area`, `area_relative`, `bad_triangles`, `bad_triangle_ids` (first 100), `steiner_limit_reached`, `conforming`, `resolve_crossings`, `steiner_limit`, `labels`, and `diagnostics_count` (details in `warnings`).

### Triangle files (`.poly`, `.node`, `.ele`)

Supported by file extension (clean-room implementation from Triangle's published format spec). An output ending in `.node`, `.ele`, or `.poly` writes all three files (numbered from 1).

| `.poly` section | Mapping to `mesh2d` input |
|---|---|
| Vertices (0- or 1-based; `#` comments) | `points`; first vertex attribute becomes height with `--poly-heights` |
| `0` vertices in `.poly` | Reads vertices from sibling `.node` file |
| Segments with markers | Constraint segments with markers |
| Enclosed segment domain (Triangle `-p`) | Each connected segment component (minus bridges/dangling chains) adds its outer-face boundary as an outer ring (`rings_from_segments`) |
| Hole points | `hole_seeds` removing constrained pockets |
| Regional attributes & area constraints | Seed `regions` (integer attribute = label; negative area = no max area) |

- **Limits:** Crossing, touching, or overlapping segment groups are not plane graphs and stay as constraints only (reported in `warnings`; without rings, domain falls back to convex hull). Exact orientation predicates make parsing scale- and UTM-invariant. Second-order triangles and `.edge`/`.neigh` files are not read.

---

## `query2d`

Queries heights, rectangle hits, and k-nearest triangles on a 2.5D terrain mesh (`TriangleQuadtree2d`).

```bash
g3cli query2d field.obj --points "10,10;50,30;85,30" --rect "65,25,75,35" --nearest 2
```

For each point, reports `height` and `triangle` from the lowest-ID containing triangle (`exact: true`, all containing triangles listed in `hits`), or from the nearest triangle within `--tolerance` (`exact: false`, or `null` if none).

---

## `polygon2d`, `polygon_solids` & `hull2d`

### `polygon2d` (`Clipper2Polygons`)

GeoJSON `Polygon`/`MultiPolygon` (or `LineString`/`MultiLineString`) in, GeoJSON file (`--output`) or inline (`--return-geojson`) out.

```bash
g3cli polygon2d parcel.geojson building.geojson --op difference --output free.geojson
g3cli polygon2d footprints.geojson --op union
g3cli polygon2d zone.geojson --op offset --delta 3 --join miter --miter-limit 2 --return-geojson
g3cli polygon2d roads.geojson district.geojson --op clip_lines --clip-type intersection --output inside.geojson
g3cli polygon2d route.geojson --op offset_lines --delta 15 --end-type butt --output corridor.geojson
g3cli polygon2d parcel.geojson --op simplify --epsilon 0.05
g3cli polygon2d footprint.geojson --pattern robot.geojson --op minkowski_sum --output clearance.geojson
g3cli polygon2d route.geojson --pattern robot.geojson --op minkowski_sum --path-closed=false --output swept.geojson
```

- **Ops:** `union`, `intersection`, `difference`, `xor`, `offset` (`--join round|miter|square|bevel`), `simplify`, `simplify_lines`, `clip_lines` (`--clip-type intersection|difference`), `offset_lines` (`--end-type round|square|butt|joined`, `delta > 0`), `minkowski_sum`, `minkowski_difference` (`--pattern` / `--pattern-geojson`, max `100,000` pattern×path vertex pairs; `--preserve-collinear` refused).
- Empty results succeed (`ok`, 0 polygons); unusable extents fail with `operation_failed` and `clipper_status`.

### `polygon_solids` (`PlanarComplex.FindSolidRegions`)

Groups rings of GeoJSON `Polygon`/`MultiPolygon` and closed `LineString`s into nested polygons with holes (max `4,000` loops; open lines counted and ignored; duplicates removed).

```bash
g3cli polygon_solids loops.geojson solids.geojson --simplify-tolerance 0 --relative-tolerance 1e-4
```

> **Watch default tolerance:** `--simplify-tolerance` defaults to `0.1` (absolute input units). Pass `0` or a small value with `--relative-tolerance` for closely spaced loops.

### `hull2d` (`ConvexHull2`)

Computes the 2D convex hull of `--points` (`"x,y;x,y"`, `@file`, or JSON `[[x,y],...]`) or a mesh's XY vertices (max `50,000` points).

```bash
g3cli hull2d --points "0,0;4,0;4,3;0,3;2,1" --return-geojson
g3cli hull2d part.stl --output hull.geojson --query int64
```

Supports `--query double` (default) and `int64` (2^20 rounded grid; no exact mode). Reports `dimension` (`0`/`1`/`2`), CCW vertices with `indices`, `area`, and `perimeter`.

---

## Height Grids (`dgm`, `dgm_tin`)

Reads regular square-lattice XYZ ASCII or ESRI ASCII Grid (`.asc`) files (`--format`, `--no-data`). Invalid grids fail with `read_error` and `grid_status` (`bad_line`, `not_on_grid`, `non_square_cells`, …).

```bash
g3cli dgm dgm.asc dgm_copy.xyz --mesh dgm_grid.ply
g3cli dgm_tin dgm.asc dgm_tin.ply --epsilon 0.05 --breaklines benches.geojson
```

- `dgm_tin` (`TerrainGridTin`) guarantees exact maximum vertical deviation `surface_error <= epsilon` and preserves GeoJSON breaklines (`--breaklines` / `--breaklines-geojson`, `LineString`/`MultiLineString`/`Polygon`).
- With `--breakline-heights input`, breaklines follow their input `z` values; deviating further than `epsilon` fails with `operation_failed` (`tin_status: "breakline_height_deviates"`, `breakline_error`).

---

## Slope Design Input (`slope`)

Certified cut/fill slope design (`TerrainSlopeDesign`) from GeoJSON toes (`LineString` or `Polygon` with `[x, y, z]` coordinates) to a terrain TIN.

```bash
g3cli slope dgm_tin.ply pit_toe.geojson pit_composite.ply --tolerance 0.05 --catch-lines pit_catch.geojson --region pit_region.ply
g3cli slope dgm_tin.ply dump_toe.geojson dump_part.g3mesh --tolerance 0.05 --direction fill --composite replacement --target-heights 120,110 --target-lines benches.geojson
g3cli slope dgm_tin.ply pit_toe.geojson pit_local.g3mesh --tolerance 0.05 --local-frame
```

### Toe GeoJSON Format

```json
{
  "type": "FeatureCollection",
  "crs": { "type": "name", "properties": { "name": "EPSG:25832" } },
  "features": [
    {
      "type": "Feature",
      "properties": { "side": "outside", "slope": { "degrees": 45 } },
      "geometry": { "type": "Polygon", "coordinates": [[[328300,5642800,-320],[328400,5642800,-320],[328400,5642900,-320],[328300,5642900,-320],[328300,5642800,-320]]] }
    },
    {
      "type": "Feature",
      "properties": { "side": "left", "cut_slope": 1.5, "fill_slope": 2 },
      "geometry": { "type": "LineString", "coordinates": [[328500,5642800,-300],[328600,5642850,-300]] }
    }
  ]
}
```

- **Coordinates:** Used as given in terrain units (no WGS84 reprojection); `crs` is passed through to outputs.
- **Properties:** `side` (`left`/`right` for `LineString`, `inside`/`outside` for `Polygon`; fallback `--side`), `slope` or `cut_slope`/`fill_slope` or `segment_slopes` (`n` for `1:n`, or `{"ratio":n}`, `{"percent":p}`, `{"degrees":a}`, `{"rise":s}`; fallback `--slope`).
- **Outputs & options:** `--composite full|replacement`, `--surface`, `--region`, `--catch-lines`, `--target-heights`, `--target-lines`, `--max-fan-directions` (default `512`, range `16..4096`), `--local-frame` (computes and outputs in local origin `used_origin` for micrometer-scale feature precision at UTM).
- **Result codes:** `success` and `no_daylight` (`complete: false` + warning) return `ok`. Uncertifiable designs fail with `operation_failed` (never writes violating slopes); bad toes/terrain fail with `read_error` (`invalid_arguments` for inline `--toes-geojson`). Error payloads include `design_status`, `status_kind`, `design_message`, `toe_problems`, and `problem_triangles`.

---

## `cell_volumes` & `tpms`

### `cell_volumes` (`TerrainCellVolumes`)

Computes deterministic cut/fill volumes per regular XY lattice cell (`--cell-size`, up to `2^20` cells) between two terrain TINs, exporting per-cell CSV and exact totals matching `TerrainVolumeDifference`.

With `--bands z0,z1,...` (strictly increasing, `-inf` / `inf` allowed at the ends) the volume is also given per 3D block: the part of the column between the surfaces with z inside each band `[z_k, z_k+1]`, exact up to rounding (`TerrainCellVolumes.BandBoundaries`). The result gets `bands` (cut and fill per band, `null` for an infinite `z_min` / `z_max`; the CSV writes `Infinity` / `-Infinity`), and `--blocks_output file.csv` writes one row per block (`band, z_min, z_max, ix, iy, x_min, y_min, cut_volume, fill_volume, net_volume`). Cells times bands are at most `2^20`. Heights outside the outer boundaries are not counted; with infinite ends the bands add up to the 2D cell volumes.

```bash
g3cli cell_volumes before.obj after.obj --cell-size 10 --bands -inf,100,110,120,inf --blocks_output blocks.csv
```

### `tpms` (`TpmsLattice`)

Generates certified closed, manifold, self-intersection-free TPMS lattices (`gyroid`, `schwarz_p`, `schwarz_d`, `neovius`, `iwp`, `lidinoid`) as `sheet` or `skeletal` solids inside a box (`--box-min`/`--box-max`) or closed `--domain` mesh.

```bash
g3cli tpms lattice.stl --type gyroid --style sheet --volume-fraction 0.3 --box-max 4,4,4 --divisions 16
g3cli tpms hybrid.stl --type gyroid --second-type iwp --blend 0,2,0.5 --box-max 4,2,2 --divisions 16
```

The check runs on the written coordinates, after `--origin` is applied (very far origins, where an ulp approaches the cube, give `tpms_status` `not_clean` or `self_intersecting` rather than a defective mesh; no file is written then).

Supports `--wall-thickness`, `--cell-size`, `--origin`, `--grade`, `--second-type` + `--blend 'axis,offset,width'`, `--enclosure` (dyadic grid), and `--verify-self-intersections false` (warns with `self_intersection_verified: false`).

A result that is Ok but not the lattice asked for carries the library's message in `warnings` (prefix `tpms: `): an `--enclosure` result with a realized volume fraction above 1 (the margin is larger than the voids: the solid box), and a sheet whose realized fraction is below half of the requested one (a wall thinner than about two cubes). `tpms_status` stays `ok`.
