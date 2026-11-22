# g3cli Analysis & Query Commands

Detailed reference for mesh measurement, fidelity comparison, quality, curvature, deviation, wall thickness, primitive fitting, geodesics, and 3D batch queries. See [../README.md](../README.md) for common arguments and JSON envelopes.

---

## `measure`

Computes mesh topology, bounds, volume, area, genus, bowties, degenerate/inverted parts, exact self-intersections (`--no-self-intersections` to skip), and optional certified Hausdorff distance (`--compare`).

```bash
g3cli measure bunny.obj
g3cli measure original.stl --compare simplified.stl --tolerance 1e-4
g3cli measure part.stl --compact | jq '.mesh.volume'
```

With `--compare`, `distance` equals the certified upper bound within `--tolerance` of the lower bound.

---

## `fidelity`, `quality` & `curvature`

### `fidelity` (`MeshFidelityMetrics`)

Compares a mesh against a reference across precision/recall/F-score, Chamfer distance, normal consistency & reversed share, sharp-edge Chamfer/recall, volume IoU (including outer surface), curvature agreement, triangle quality, and topology changes.

```bash
g3cli fidelity original.stl simplified.stl --relative-thresholds 0.001,0.005,0.01
g3cli fidelity original.stl simplified.stl --surface-samples 200000 --no-curvature --seed 7
```

- Sampling is deterministic for `--seed`. Distances and lengths are normalized by reported `size` (reference diagonal); curvatures are `H * size` and `K * size^2`; angles are in degrees.
- `null` means unavailable (`NaN`); `"infinity"` denotes infinite metrics (e.g., `edge_chamfer` when only one mesh has sharp edges).
- Options used are echoed in `options` (including `--outer-offset` and `--sharp-edge-min-height`).

### `quality` (`TriangleQualityStatistics`, `MeshTopologySummary`)

```bash
g3cli quality simplified.stl
#   -> "quality": { "min_angle": { "worst": ..., "worst_1_percent": ..., "median": ... }, ... },
#      "topology": { "components": 1, "genus": 0, ... }
```

### `curvature` (`MeshCurvature`)

Computes discrete mean (`H`) and Gaussian (`K`) curvature per vertex (Meyer et al. mixed Voronoi areas, cotan mean curvature normal, angle deficit) as statistics over valid vertices (raw and size-scaled). Optional `--output` writes every vertex to CSV or JSON.

```bash
g3cli curvature part.stl --output curvature.csv
#   -> "valid_vertices": 5120, "mean_curvature": {...}, "gaussian_curvature": {...}, "angle_deficit_total": 12.566
```

---

## `deviation`, `thickness` & `fit`

### `deviation` (`MeshDeviation`, `DeviationStatistics`)

Computes signed (`--sign pseudo_normal` / `winding_number` / `exact_winding_number`, positive outside reference) or unsigned vertex distance statistics (`mean`, `rms`, `max_absolute`, percentiles). `--certified` adds certified one-sided Hausdorff bounds (`hausdorff_lower_bound`, `hausdorff_upper_bound`).

```bash
g3cli deviation simplified.stl original.stl --certified
```

### `thickness` (`MeshWallThickness`)

Computes wall thickness of a closed mesh via `--method ray`, `shape_diameter`, or `inscribed_sphere`. Reports triangles with no hit within `--max-distance`, and with `--min-thickness` lists thin triangle counts, area share, and IDs (truncated at `--max-results`).

```bash
g3cli thickness part.stl --method ray --min-thickness 0.8 --max-results 20
```

### `fit` (`PrimitiveFit3`)

Fits `plane`, `sphere`, `cylinder`, `cone`, or `--primitive all` to mesh vertices via deterministic RANSAC + least squares. Returns `operation_failed` with `fit_result` (`too_few_points`, `rejected_by_limits`, …) when no model is found.

```bash
g3cli fit pipe.stl --primitive cylinder --inlier-distance 0.05
#   -> "cylinder": { "center": [...], "axis_direction": [...], "radius": 12.5, "height": 80 }, "inliers": 4120, "rms_error": ...
g3cli fit unknown.stl --primitive all
```

---

## `geodesic` & `query3d`

### `geodesic` (`MeshGeodesicDistance`, `MeshGeodesicPath`)

Computes heat-method geodesic distances from `--sources` (with optional `--targets`, `--intrinsic-refine`, `--refine-min-angle`, and `--trace-from` steepest descent) or FlipOut geodesic paths (`--from` / `--to`, or `--through` with optional `--closed` and `--max-iterations`). Vertex IDs are 0-based as read; list lengths in JSON respect `--max-results`.

```bash
g3cli geodesic terrain.ply --sources 0 --targets 120,4711
g3cli geodesic part.ply --from 12 --to 3401 --max-iterations 500
g3cli geodesic part.ply --through 12,800,3401 --closed
g3cli geodesic scan.ply --sources 0 --intrinsic-refine --refine-min-angle 25 --trace-from 4711
```

A path between disconnected components fails with `operation_failed` and `path_status: "no_path"`.

### `query3d` (`MeshBatchQueries`, `DMeshAABBTree3`)

Runs deterministic batch ray-hit, nearest-point, inside-test, and pinhole camera depth queries.

```bash
# Inline or @file rays ('ox,oy,oz,dx,dy,dz;...') and points ('x,y,z;...')
g3cli query3d part.stl --rays "0,0,-10,0,0,1;5,5,-10,0,0,1" --points "0,0,0;50,0,0" --nearest --inside crossings
g3cli query3d part.stl --rays @rays.txt --any-hit --max-distance 100

# JSON arrays + full CSV/JSON export (not truncated by --max-results)
g3cli query3d part.stl --rays @rays.json --output hits.csv --max-results 10
g3cli query3d part.stl --points @points.json --inside auto --output inside.csv

# Pinhole camera distance image ('px,py,pz;tx,ty,tz;vertical_fov_deg;width;height')
g3cli query3d part.stl --camera "0,-300,80;0,0,40;45;640;480" --depth-output depth.pgm
```

- **Input formats:** Semicolon-delimited text, `@file` (one entry per line), or JSON (`[{"origin":[x,y,z],"direction":[dx,dy,dz]}]`, `[[ox,oy,oz,dx,dy,dz]]`, `[[x,y,z]]`). Ray directions are normalized by default (`--normalize=false` reports `not_normalized` on non-unit directions).
- **Inside methods (`--inside`):** `crossings` (exact parity on closed meshes without self-intersections), `winding_number` (any mesh), or `auto` (chooses `crossings` if closed without boundary, else `winding_number`, reported in `points.method_used`).
- **Camera & depth output:** Fires one ray per pixel center row-by-row from top-left (`--up` defaults to `+z`, falling back to `+y`/`+x` if parallel to view). `--depth-output` writes ray-hit distance (not z-axis depth) as 16-bit ASCII `.pgm` (brighter = nearer, `0` = miss) or `.csv`; `--output` includes `pixel_x` / `pixel_y`.
