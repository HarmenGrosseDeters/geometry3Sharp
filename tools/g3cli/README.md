# g3cli: mesh operations as CLI and MCP server

`g3cli` is the headless layer of this fork (phase 4 of the roadmap, [docs/roadmap/platform.md](../../docs/roadmap/platform.md)): the
geometry3Sharp mesh operations on mesh files, as subcommands with JSON output and, with `g3cli mcp`, as the
tools of an MCP server (stdio), for coding agents and scripts.

| Command | Operation | Library classes |
|---|---|---|
| `measure` | counts, components, closedness, volume, area, bounds, genus, bowties, degenerate and inside-out parts, self-intersections (exact), Hausdorff distance to a second mesh | `MeshMeasurements`, `MeshRepairMetrics`, `MeshHausdorffDistance` |
| `repair` | repair into a closed solid (`--method robust`, default, or `auto`); with the exact resolve, `genus_before` / `genus_after` and a warning when it changed the genus of a component (self-overlapping layers form tunnels); the fallbacks can be tuned (`--exact-resolve-max-pairs`, `--safe-rounding`, `--alpha-wrap`, `--simplify-alpha-wrap`, `--check-lost-components`, `--reject-topology-change`, and the SafeRounding settings as for `boolean`: `--rounding-max-displacement`, `--rounding-ulp-factor`, `--rounding-absolute-tolerance`, `--rounding-max-pair-tests`, `--strict-rounding`) and are reported (`used_alpha_wrap`, `wrap_reason`, `result_reason`, `exact_resolve_rounding` and `separated_rounding`: the two rounding repairs of the exact stage, ...) | `MeshRobustRepair`, `MeshAutoRepair` |
| `boolean` | union, intersection, difference (a minus the others), at least k of n, xor (winding number positive in an odd number of the inputs; for three, `(a xor b) xor c`; its parts touch along the crossing curves); two or more meshes (`--more`) in one exact operation; the result is repaired after the rounding (`--safe-rounding`, on by default as in the library), `boolean.rounding_clean` tells whether it is verified free of self-intersections and slivers, `boolean.rounding` has the repair's counts (`null` with `--no-safe-rounding`) | `ExactMeshBoolean`, `MeshRoundingRepair` |
| `resolve` | boundary of the solid of one mesh: removes self-intersections, nested shells, duplicate faces; `--safe-rounding`, `--simplify-flat-regions` as for `boolean` | `ExactMeshBoolean.Resolve` |
| `remesh` | isotropic remeshing to a target edge length, boundary and sharp edges kept, sharp points with `--sharp-vertex-angle` (`sharp_vertices_pinned`); passes that would turn a closed part inside out are rolled back (`rejected_passes`), a warning if the edge length is more than 4 times the estimated wall thickness | `Remesher`, `MeshConstraintUtil` |
| `simplify` | quadric edge collapse to a triangle count, ratio, error or edge length | `Reducer` |
| `uv` | UV atlas: seams / cones / charts, BFF, packing, exact overlap test, distortion limits | `UVAtlasGenerator`, `MeshDiscCut`, `ConeFlattening` |
| `convert` | STL, OBJ, OFF, PLY, 3MF, glTF/GLB, g3mesh (by file extension) | `StandardMeshReader`, `StandardMeshWriter` |
| `mesh2d` | 2D constrained Delaunay triangulation (`--quality false`) or quality mesh (minimum angle, maximum area, per-region areas, Steiner point limit) of points, segments, rings, holes and labelled regions given as JSON (a file or inline `--geometry`) or as Triangle `.poly` / `.node` files; writes the mesh in the XY plane, z = heights, or Triangle `.node` / `.ele` / `.poly` files | `QualityMesh2`, `ConstrainedDelaunayMesh2` |
| `slope` | slope design from toe lines (GeoJSON) to a terrain (a TIN mesh file): cut, fill or mixed (`--direction auto`), never steeper than the given slopes (every triangle checked exactly); the composite terrain or only its replacement, the slope surface and region, catch lines and target-height level lines (GeoJSON), cut / fill volumes and areas; `--local-frame` for finer guaranteed features at UTM (local outputs plus `used_origin`) | `TerrainSlopeDesign`, `SlopeToe`, `SlopeSpec` |
| `query2d` | heights at XY points (the containing triangle of the lowest id, `exact` true, or the nearest within `--tolerance`, `exact` false; all containing triangles in `hits`), triangles in a rectangle, nearest triangles of a mesh seen from above (a terrain) | `TriangleQuadtree2d` |
| `fidelity` | compare a mesh with a reference: precision / recall / F-score at relative distance thresholds, Chamfer distance, normal consistency and reversed normals, edge Chamfer and edge recall on sharp edges, volume IoU (also of the outer surface), curvature agreement, triangle quality of both meshes, topology changes | `MeshFidelityMetrics` |
| `quality` | triangle quality (min / max angle, aspect ratio, scaled Jacobian, condition: worst, worst 1 % / 5 %, median) and topology (components, boundary loops, Euler characteristic, genus) of one mesh | `TriangleQualityStatistics`, `MeshTopologySummary` |
| `deviation` | deviation map of a mesh against a reference: signed (`--sign pseudo_normal` / `winding_number`) or unsigned vertex distances as statistics (mean, rms, median, percentiles), with `--certified` the certified one-sided Hausdorff distance as lower and upper bound | `MeshDeviation`, `DeviationStatistics` |
| `thickness` | wall thickness of a closed mesh (`--method ray` / `shape_diameter` / `inscribed_sphere`) as statistics; with `--min-thickness` the thin triangles (count, area share, ids), vertices | `MeshWallThickness` |
| `fit` | plane, sphere, cylinder or cone fitted to the vertices (RANSAC + least squares, deterministic for a seed; `--primitive all` fits every one): parameters, inliers, RMS and maximum error | `PrimitiveFit3` |
| `polygon2d` | robust 2D polygon booleans (`--op union` / `intersection` / `difference` / `xor`) and offsets (`--op offset --delta d --join round/miter/square/bevel`) on GeoJSON polygons (Polygon, MultiPolygon, with holes) with the optional Clipper2 adapter; the result as GeoJSON (a file, or inline with `--return-geojson`), counts, areas and the integer grid used | `Clipper2Polygons` (geometry3Sharp.Clipper2) |

## Build and run

```
dotnet build tools/g3cli -c Release
dotnet tools/g3cli/bin/Release/net10.0/g3cli.dll help
# or
dotnet run --project tools/g3cli -c Release -- measure part.stl
```

`g3cli help` lists the commands, result codes and formats; `g3cli help <command>` (or `<command> --help`)
lists the arguments with types and defaults.

## Arguments

Inputs and the output are positional (in the order of `help <command>`) or named. Options are
`--name value` or `--name=value`; `-` and `_` are the same (`--edge-length` = `--edge_length`). Boolean
options: `--deviation` (true), `--binary=false`, `--no-self-intersections`. `--compact` prints the JSON on one
line. All meshes in an input file are merged into one mesh; the output format follows the extension.

## Output and result codes

Every call prints exactly one JSON object on stdout (anything the library logs goes to stderr), with
`status` (`ok` / `error`), `code` and, on errors, `message`. Failures are result codes, not exceptions or
stack traces:

| `code` | exit code | meaning |
|---|---|---|
| `ok` | 0 | |
| `operation_failed` | 1 | the operation ran but gave no result (eg boolean with `--allow-open=false` on an open mesh) |
| `empty_mesh` | 1 | an input has no triangles |
| `invalid_arguments` | 2 | missing, unknown or malformed argument, value out of range |
| `unknown_command` | 2 | |
| `read_error` | 3 | input missing, unreadable or not parseable |
| `unknown_format` | 3 | extension of an input or output is not a supported format |
| `write_error` | 4 | output could not be written (eg directory missing) |
| `internal_error` | 70 | unexpected exception (a bug, please report) |

Argument types and the output format are checked before an input is read, so a bad call fails fast; no output
file is written when a command fails.

The `mesh` object (`input_mesh` for the input where a command changes the mesh) has the same fields everywhere:

```
g3cli measure bunny.obj
```
```json
{
  "command": "measure",
  "status": "ok",
  "code": "ok",
  "input": "bunny.obj",
  "mesh": {
    "vertices": 5546, "triangles": 10934, "edges": 16499, "components": 10,
    "boundary_edges": 196, "bowtie_vertices": 23,
    "closed": false, "winding_closed": false, "degenerate_triangles": 0,
    "volume": null, "area": 5436.698190542593,
    "bounds": { "min": [-25.9368, -0.0312, -19.8223], "max": [25.9271, 49.9688, 19.8408],
                "size": [51.8639, 50.0, 39.6631], "diagonal": 82.2376 },
    "genus": null, "inconsistent_orientation_edges": 0, "inverted_components": 0, "self_intersections": 0,
    "clean": false, "not_clean_reason": "not closed", "not_clean_reasons": "not closed, bowties"
  },
  "warnings": [],
  "elapsed_ms": 300
}
```

`volume` is `null` unless the mesh is closed, `genus` unless it is a closed, connected manifold. `clean` and
`not_clean_reason` are the checks of `MeshRobustRepair` (closed, no bowties, no degenerate triangles, consistently
oriented, no inside-out components, no self-intersections; `inconsistent_orientation_edges`: interior edges whose two
triangles run the same way). `not_clean_reason` is the first failed check, `not_clean_reasons` all of them, separated by
", " (`null` if none failed; self-intersections only if checked). `winding_closed`: every edge used equally often in both directions, then the
winding numbers of `boolean` are exact. The self-intersection count is exact and the expensive part on big
meshes; `--no-self-intersections` skips it (`null`).

## Examples

```
# repair: robust repair; self-intersections resolved exactly, winding-number solidify only if that is not clean
g3cli repair scan.obj scan_fixed.stl
#   -> "used_exact_resolve": true, "used_solidify": false, "fallback_reason": "self-intersections", "mesh": { "closed": true, "clean": true, ... }
g3cli repair scan.obj scan_fixed.stl --no-exact-resolve     # solidify fallback only (as before)

# keep the input triangles (MeshAutoRepair only)
g3cli repair scan.obj scan_fixed.stl --method auto --tolerance 1e-5

# exact booleans
g3cli boolean part.stl tool.stl cut.3mf --op difference
g3cli boolean --a a.obj --b b.obj --output ab.glb --op union --allow-open=false
# more than two meshes in one operation (one arrangement): files separated by ';'
g3cli boolean a.stl b.stl all.stl --op union --more "c.stl;d.stl"
g3cli boolean a.stl b.stl two.stl --op at_least --k 2 --more c.stl

g3cli boolean a.stl b.stl odd.stl --op xor --more c.stl              # inside an odd number of the inputs
g3cli boolean a.stl b.stl fast.stl --op union --no-safe-rounding     # plain rounding, faster
g3cli resolve site_utm.stl solid.stl --strict-rounding                # fail (RoundingNotClean) instead of writing defects
g3cli repair scan.obj fixed.stl --rounding-max-displacement 1e-6       # the exact stage's rounding repairs move less (default 1e-5 of the diagonal)

# self-intersecting / overlapping shells in one file -> one clean solid
g3cli resolve assembly.off solid.ply

# 2D quality mesh of a domain given as JSON (file or inline), then heights sampled on it
g3cli mesh2d field.json field.obj --min-angle 30 --max-area 50
g3cli mesh2d --geometry '{"points": [[0,0],[4,0],[4,4],[0,4]], "heights": [0,4,8,4]}' --output tin.obj --max-area 0.5
# Triangle's files: a .poly in, .node / .ele / .poly out (the domain is what segments enclose, as Triangle -p; see below)
g3cli mesh2d lake.poly lake.ele --min-angle 30 --max-area 2
g3cli mesh2d parcels.poly parcels.obj --snap-tolerance 0.01        # snap rounding to a 1 cm grid first (m2.snap)
g3cli query2d tin.obj --points "1,1;3,2.5" --rect "0,0,1,1" --nearest 2

# slope design: a pit toe (GeoJSON) cut into a TIN (the slopes in the GeoJSON), the composite terrain and the catch line
g3cli slope dgm_tin.ply pit_toe.geojson pit_composite.ply --tolerance 0.05 --catch-lines pit_catch.geojson
#   -> "design_status": "success", "complete": true, "cut_volume": ..., "catch_lines": [{"points": 812, "closed": true, ...}], "min_feature_size": 0.0149, ...
# only the replaced triangles, a dump in fill, bench levels at 120 and 110 m
g3cli slope dgm_tin.ply dump_toe.geojson dump_part.g3mesh --tolerance 0.05 --direction fill --composite replacement --target-heights 120,110 --target-lines benches.geojson
# at UTM: compute in a local frame (outputs local; add used_origin to x and y)
g3cli slope dgm_tin.ply pit_toe.geojson pit_local.g3mesh --tolerance 0.05 --local-frame

# fidelity of a simplified mesh against the original (sampled with a fixed seed, so reproducible; distances are relative to "size")
g3cli fidelity original.stl simplified.stl --relative-thresholds 0.001,0.005,0.01
#   -> "f_score": [0.93, 0.99, 1.0], "chamfer": 0.0007, "normal_consistency": 0.98, "edge_recall": [...], "volume_iou": 0.996,
#      "outer": {...}, "curvature": {...}, "mesh_quality": {...}, "topology_changes": ["components_lost"]
g3cli fidelity original.stl simplified.stl --surface-samples 200000 --no-curvature --seed 7
# in the result: distances and lengths are relative to "size" (mesh units), curvatures are H*size and K*size^2, angles in degrees;
# null = not available (NaN), the string "infinity" = infinite (eg edge_chamfer if only one mesh has sharp edges, the aspect ratio of a zero-area triangle)

# quality and topology of one mesh
g3cli quality part.stl
#   -> "quality": { "min_angle": { "worst": 3.1, "worst_1_percent": 11.2, "median": 44.0 }, ... }, "topology": { "components": 1, "genus": 0, ... }

# deviation of a simplified mesh from the original: statistics only (positive = outside the reference's solid), certified Hausdorff bound
g3cli deviation simplified.stl original.stl --certified
#   -> "vertex_statistics": { "mean": ..., "rms": ..., "max_absolute": ..., "absolute_p95": ... }, "hausdorff_lower_bound": ..., "hausdorff_upper_bound": ...

# wall thickness: triangles thinner than 0.8 (mesh units), by the shape diameter function
g3cli thickness part.stl --min-thickness 0.8
#   -> "statistics": { "min": ..., "median": ... }, "thin_triangles": 120, "thin_area_share": 0.03, "thin_triangle_ids": [...]

# primitive fit: a cylinder (a failed fit is operation_failed with "fit_result"), or every primitive for comparison
g3cli fit pipe.stl --primitive cylinder --inlier-distance 0.05
g3cli fit unknown.stl --primitive all
#   -> "fits": [ { "primitive": "plane", "inlier_share": 0.4, "rms_error": ... }, { "primitive": "sphere", ... }, ... ]

# 2D polygons (GeoJSON Polygon / MultiPolygon, x and y): booleans and offsets with Clipper2; the result is a GeoJSON file or inline
g3cli polygon2d parcel.geojson building.geojson free.geojson --op difference
#   -> "result": { "polygons": 2, "holes": 0, "area": 812.5, "bounds": {...} }, "frame": { "resolution": 1.1e-11, ... }
g3cli polygon2d footprints.geojson --op union                       # merges the overlapping polygons of one file
g3cli polygon2d zone.geojson --op offset --delta 3 --join miter --miter-limit 2
g3cli polygon2d --subject-geojson '{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}' --op offset --delta -1 --return-geojson

# remesh to edge length 0.5, keep edges sharper than 40 degrees, report the deviation
g3cli remesh part.stl part_remeshed.obj --edge-length 0.5 --crease-angle 40 --deviation
# the guard against poor and flat triangles is on by default (--min-quality 0.2, Remesher.MinTriangleQuality;
# benchmarks/RemeshQualityBenchmark: on 20 Thingi10K meshes 87% fewer flat triangles, 84% fewer self-intersecting
# pairs, F-score and edge recall slightly better, about 1.5x the time). 0 restores the unguarded remesher, eg for
# thin walls far below the edge length, where the guard makes the result worse
g3cli remesh part.stl part_remeshed.obj --min-quality 0
# points that are no junction of creases, eg the apex of a cone with many slices, are kept by default
# (--sharp-vertex-angle 75, MeshConstraintUtil.PinSharpVertices: vertices with an absolute angle deficit of at least 75
# degrees are pinned, reported in sharp_vertices_pinned; benchmarks/RemeshQualityBenchmark); 0 turns it off
g3cli remesh part.stl part_remeshed.obj --sharp-vertex-angle 0

# simplify to 10% of the triangles, or to at most 5000
g3cli simplify scan.ply scan_small.ply --ratio 0.1 --deviation
g3cli simplify scan.ply scan_small.stl --triangles 5000

# UV atlas (OBJ, PLY, glTF/GLB, g3mesh keep the UVs); atlas_status "distortion_limit_exceeded" if max_charts was not enough
g3cli uv part.stl part_uv.obj
g3cli uv part.stl part_uv.glb --method charts --max-area-distortion 0.1 --max-charts 64

# Hausdorff distance (certified: distance = upper bound, within the tolerance of the lower bound)
g3cli measure original.stl --compare simplified.stl --tolerance 1e-4

# convert
g3cli convert part.3mf part.glb
g3cli convert part.obj part.stl --binary=false

# scripts: use the exit code and parse stdout
g3cli measure part.stl --compact | jq '.mesh.volume'
```

## 2D meshing input (`mesh2d`)

The input is a JSON object; only `points` is required, indices refer to `points` (0-based):

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

`rings` are outer boundaries or holes (even-odd inside; without outer rings the domain is the convex hull of the points),
`segments` constraints (a locked one is never split by refinement), `hole_seeds` points in holes without a ring,
`regions` labelled by a ring or a seed point (where they overlap, the last wins), with an optional maximum area,
`heights` one z per point (interpolated at new vertices). Unknown keys, wrong types and bad indices fail with
`invalid_arguments` (inline `--geometry`) or `read_error` (a file). The written mesh lies in the XY plane, oriented
counterclockwise, the labels are its triangle groups (OBJ: `g mmGroup<label>`); only vertices of triangles are written.
The result reports `triangles`, `vertices`, `added_vertices` (Steiner and crossing points), `smallest_angle`, `largest_area`, `area` (areas below the double range, at coordinates around 1e-160 and smaller, are 0), `bad_triangles`
(below the angle bound, near locked segments or small input angles), `steiner_limit_reached`, `labels` (triangles per
label) and the diagnostics of the mesher as `warnings`. Crossing constraints with `--resolve-crossings false`, and inputs
without a triangle (collinear points), fail with `operation_failed` and the coordinate of the problem.

### Triangle files (`.poly`, `.node`, `.ele`)

Read and written from Triangle's published file format description (no Triangle code; [SOURCES](../../docs/SOURCES.md)), by the
extension of `input` and `output`. How a `.poly` maps to the mesher's input:

| `.poly` | mesher input |
|---|---|
| vertices, numbered from 0 or 1 (the first one decides); `#` comments | points; the first vertex attribute is the height with `--poly-heights`, other attributes and vertex markers are ignored |
| 0 vertices | the vertices of the `.node` file of the same name |
| segments with markers | segments with markers |
| what the segments enclose (Triangle `-p` eats triangles from the convex hull until a segment stops it) | each connected group of segments, without its bridges and dangling chains, adds the boundary of its outer face as an outer ring (`rings_from_segments`): nested loops, chords and loops that share a vertex give what `-p` gives |
| hole points | hole seeds: each removes the region of the constraints around it |
| regional attributes and area constraints | seed regions; the attribute must be an integer (the label), a negative area means none |

Limits: a group of segments that cross, touch (an end point on another segment) or overlap is no plane graph here, as Triangle
would split it at the crossings first; it stays constraints only and does not bound the domain, and without any ring the domain is
the convex hull (as `-c`). Both cases are reported in `warnings`. The construction is exact (orientation tests, no angles or
areas), so it does not depend on the scale or on the offset of the coordinates (eg UTM). A vertex with d segments costs about d²
index comparisons (20 000 segments at one vertex: about 6 s to read). Second-order triangles and `.edge` / `.neigh` files are not read.
Written: `<name>.node` (boundary marker: the subsegment marker at the vertex, 1 for unmarked boundaries), `<name>.ele` (labels as
the triangle attribute when there are regions), `<name>.poly` (no vertices, the subsegments with markers, the input's hole points
and seed regions; regions given by ring have no `.poly` form, a warning says so), all numbered from 1.

## Slope design input (`slope`)

The terrain is a TIN in any mesh format (a height field: only the part under the slope surface must be one). The toes are
GeoJSON (RFC 7946): a `FeatureCollection`, a `Feature` or a bare geometry; each toe a `LineString` (an open toe) or a
`Polygon` (a closed toe: its outer ring, no holes), with `[x, y, z]` positions (the toe's height is needed):

```json
{"type": "FeatureCollection", "crs": {"type": "name", "properties": {"name": "EPSG:25832"}},
 "features": [
  {"type": "Feature", "properties": {"side": "outside", "slope": {"degrees": 45}},
   "geometry": {"type": "Polygon", "coordinates": [[[328300, 5642800, -320], [328400, 5642800, -320],
     [328400, 5642900, -320], [328300, 5642900, -320], [328300, 5642800, -320]]]}},
  {"type": "Feature", "properties": {"side": "left", "cut_slope": 1.5, "fill_slope": 2},
   "geometry": {"type": "LineString", "coordinates": [[328500, 5642800, -300], [328600, 5642850, -300]]}}]}
```

- **Coordinates are taken as given**: projected, in the units of the terrain (metres), no reprojection. RFC 7946 speaks of
  WGS84; that does not apply here. A `crs` member is not interpreted: it is passed through to the report and to the GeoJSON
  outputs.
- **`side`**: the slope's side of the toe line in its direction (`left`, `right`), or `inside` / `outside` for a closed
  toe. Required (or `--side` for all toes without one).
- **Slopes**: `slope` for cut and fill, or `cut_slope` and `fill_slope`, or `segment_slopes` (one per segment, for cut and
  fill). A slope is a number n (1:n, n horizontal per 1 vertical), or `{"ratio": n}`, `{"percent": p}`, `{"degrees": a}`,
  `{"rise": s}`. `--slope` (eg `degrees:40`) for toes without one.
- **Outputs**: `output` is the composite (the whole terrain with the slope region) or, with `--composite replacement`, only
  the part that replaces terrain triangles (`replaced_terrain_triangles` in the report); `--surface` and `--region` write
  the slope surface and the slope region; `--catch-lines` and `--target-lines` write GeoJSON `LineString`s (closed lines
  repeat their first position), with `start_reason` / `end_reason`, `max_uncertainty` and `tangential_segments`, or
  `height`.
- **The report**: `design_status` and `status_kind` (TerrainSlopeDesign's), `complete`, the volumes and areas
  (`cut_volume`, `fill_volume`, `cut_area`, `fill_area`, `volume`, `plan_area`, `slope_area`), the catch lines,
  `min_feature_size` (the smallest feature the result guarantees, about 1.5 cm at UTM), `slope_margin`, `toe_lowering`,
  `attempts`, `realizations`, `phase_seconds`, the output mesh's metrics.
- **Result codes**: `success` and `no_daylight` (a partial result, `complete` false, with a warning) are `ok`. A design the
  library cannot certify (`slope_violation`, `toe_undercut`, `composite_failed`, ...) fails with `operation_failed`, and an
  invalid toe or terrain (`invalid_input`, `not_height_field`) with `read_error` (`invalid_arguments` for inline
  `--toes-geojson`); the error result carries `design_status`, `status_kind`, `design_message`, `toe_problems`
  ([toe, segment] pairs) and `problem_triangles`. No violating result is ever written.
- **`--local-frame`**: the design is computed on the inputs translated to a local origin near the toes, so the derived sizes
  follow from local coordinates (`min_feature_size` about 2e-6 m instead of 1.5 cm at UTM). **Every output is then local**:
  the meshes, the catch and target lines (whose GeoJSON also carries `used_origin`). Add `used_origin` to x and y for the
  input's frame; that rounds each coordinate by up to half an ulp and is not covered by the exact checks.
  `local_frame_fallback` true: a coordinate could not be translated exactly, and the design ran in the input's frame.

## MCP server

`g3cli mcp` serves the Model Context Protocol over stdio (newline-delimited JSON-RPC 2.0, protocol versions
2025-06-18, 2025-03-26, 2024-11-05). The tools are the commands above with the same arguments (snake_case
names, JSON types, `inputSchema` from the same parameter definitions). A tool result carries the JSON result
both as `structuredContent` and as text; `isError` is true when `code` is not `ok`. Relative paths are
relative to the server's working directory.

Claude Code:

```
dotnet build tools/g3cli -c Release
claude mcp add g3 -- dotnet /path/to/geometry3Sharp/tools/g3cli/bin/Release/net10.0/g3cli.dll mcp
```

Other clients (eg `mcpServers` in a JSON config):

```json
{
  "mcpServers": {
    "g3": { "command": "dotnet", "args": ["/path/to/tools/g3cli/bin/Release/net10.0/g3cli.dll", "mcp"] }
  }
}
```

By hand:

```
$ printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"boolean","arguments":{"a":"a.stl","b":"b.stl","output":"u.stl","op":"union"}}}' \
  | dotnet g3cli.dll mcp
```

Requests are handled one after another; long operations (repair with solidify, booleans of big meshes) block
the server until they finish.

## Tests

`tests/g3cli.Tests` runs every command on small generated meshes (cube, shifted cube, open cube, overlapping
cubes, sphere) and generated 2D domains and terrains: format round trips, quality bounds and labels of `mesh2d`, heights
and box queries of `query2d` against brute force, fuzzed `mesh2d` JSON, expected volumes of booleans and repairs, error cases with their result
and exit codes, the MCP protocol in process and the real executable as a child process (stdio). CI runs it
on every push.

```
dotnet test tests/g3cli.Tests
```
