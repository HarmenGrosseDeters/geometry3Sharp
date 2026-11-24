# g3cli — Headless CLI & MCP Server for geometry3Sharp

`g3cli` exposes the core `geometry3Sharp` mesh, 2D, terrain, and analysis operations on files as subcommands with structured JSON output—and, via `g3cli mcp`, as tools of a stdio Model Context Protocol (MCP) server for coding agents and scripts.

---

## Command Reference Index

Detailed flags, input schemas, additive JSON fields, and worked examples are organized by domain:

- **[Mesh Commands](docs/mesh-commands.md)** — `repair`, `boolean`, `resolve`, `remesh`, `simplify`, `offset`, `wrap`, `quad`, `tetmesh`, `uv`, `convert`
- **[Analysis & Query Commands](docs/analysis-commands.md)** — `measure`, `fidelity`, `quality`, `curvature`, `deviation`, `thickness`, `fit`, `geodesic`, `query3d`
- **[2D, Terrain & Lattice Commands](docs/2d-and-terrain.md)** — `mesh2d`, `query2d`, `polygon2d`, `polygon_solids`, `hull2d`, `dgm`, `dgm_tin`, `slope`, `cell_volumes`, `tpms`
- **[MCP Server Reference](docs/mcp-reference.md)** — `g3cli mcp` stdio setup, Claude Code & JSON config, tool schemas, raw JSON-RPC examples

| Command | Summary | Library classes | Reference |
|---|---|---|---|
| `measure` | Counts, components, closedness, volume, area, genus, self-intersections, Hausdorff | `MeshMeasurements`, `MeshRepairMetrics`, `MeshHausdorffDistance` | [Analysis](docs/analysis-commands.md#measure) |
| `repair` | Robust (`--method robust`, default) or auto mesh repair into a clean solid | `MeshRobustRepair`, `MeshAutoRepair` | [Mesh](docs/mesh-commands.md#repair) |
| `boolean` | Exact binary/n-ary union, intersection, difference, `at_least`, `xor` + SafeRounding | `ExactMeshBoolean`, `MeshRoundingRepair` | [Mesh](docs/mesh-commands.md#boolean--resolve) |
| `resolve` | Resolve self-intersections, nested shells, and duplicate faces in one mesh | `ExactMeshBoolean.Resolve` | [Mesh](docs/mesh-commands.md#boolean--resolve) |
| `remesh` | Isotropic remeshing preserving boundaries, creases, sharp vertices (`--safe`) | `Remesher`, `MeshConstraintUtil` | [Mesh](docs/mesh-commands.md#remesh) |
| `simplify` | QEM simplification by count, ratio, error, or edge length (`--partitioned`) | `Reducer`, `PartitionedReducer` | [Mesh](docs/mesh-commands.md#simplify) |
| `offset` | Signed distance offset surface (`sdf_mc` or certified `--method mesh_offset`) | `MeshSignedDistanceGrid`, `MarchingCubes`, `MeshOffset` | [Mesh](docs/mesh-commands.md#offset) |
| `wrap` | Closed, 2-manifold alpha wrap around any mesh or triangle soup | `MeshAlphaWrap` | [Mesh](docs/mesh-commands.md#wrap) |
| `quad` | Quad-dominant or opt-in `--pure-quads` remeshing (OBJ / PLY output) | `QuadRemesher`, `PolyMeshMetrics`, `PolyMeshIO` | [Mesh](docs/mesh-commands.md#quad) |
| `tetmesh` | Tetrahedral meshing with markers & quality targets (Medit / Gmsh / VTK) | `MeshTetrahedralization`, `TetMeshMarkers`, `TetMeshWriter` | [Mesh](docs/mesh-commands.md#tetmesh) |
| `uv` | UV atlas generation (cones/charts, BFF, packing, exact overlap check) | `UVAtlasGenerator`, `MeshDiscCut`, `ConeFlattening` | [Mesh](docs/mesh-commands.md#uv--convert) |
| `convert` | Format conversion between STL, OBJ, OFF, PLY, 3MF, glTF/GLB, g3mesh | `StandardMeshReader`, `StandardMeshWriter` | [Mesh](docs/mesh-commands.md#uv--convert) |
| `fidelity` | F-score, Chamfer, normal consistency, edge recall, outer IoU, curvature, topology | `MeshFidelityMetrics` | [Analysis](docs/analysis-commands.md#fidelity-quality--curvature) |
| `quality` | Verdict triangle quality percentiles and topology summary | `TriangleQualityStatistics`, `MeshTopologySummary` | [Analysis](docs/analysis-commands.md#fidelity-quality--curvature) |
| `curvature` | Discrete mean (`H`) and Gaussian (`K`) curvature statistics + CSV/JSON export | `MeshCurvature` | [Analysis](docs/analysis-commands.md#fidelity-quality--curvature) |
| `deviation` | Signed/unsigned vertex deviation statistics + optional `--certified` Hausdorff | `MeshDeviation`, `DeviationStatistics` | [Analysis](docs/analysis-commands.md#deviation-thickness--fit) |
| `thickness` | Wall thickness (`ray`, `shape_diameter`, `inscribed_sphere`) & thin triangles | `MeshWallThickness` | [Analysis](docs/analysis-commands.md#deviation-thickness--fit) |
| `fit` | RANSAC + LM fit of plane, sphere, cylinder, cone, or `--primitive all` | `PrimitiveFit3` | [Analysis](docs/analysis-commands.md#deviation-thickness--fit) |
| `geodesic` | Heat geodesic distances (`--intrinsic-refine`, `--trace-from`) & FlipOut paths | `MeshGeodesicDistance`, `MeshGeodesicPath` | [Analysis](docs/analysis-commands.md#geodesic--query3d) |
| `query3d` | Batch ray hits, nearest points, inside tests, pinhole `--camera` depth images | `MeshBatchQueries`, `DMeshAABBTree3` | [Analysis](docs/analysis-commands.md#geodesic--query3d) |
| `mesh2d` | 2D constrained Delaunay or quality mesh from JSON or Triangle `.poly`/`.node` | `QualityMesh2`, `ConstrainedDelaunayMesh2` | [2D & Terrain](docs/2d-and-terrain.md#mesh2d) |
| `query2d` | 2D terrain heights, rectangle hits, and k-nearest triangles | `TriangleQuadtree2d` | [2D & Terrain](docs/2d-and-terrain.md#query2d) |
| `polygon2d` | 2D GeoJSON polygon/line booleans, offsets, simplification, Minkowski sums | `Clipper2Polygons` (`geometry3Sharp.Clipper2`) | [2D & Terrain](docs/2d-and-terrain.md#polygon2d-polygon_solids--hull2d) |
| `polygon_solids` | Nest arbitrary GeoJSON loops into polygons with holes | `PlanarComplex.FindSolidRegions` | [2D & Terrain](docs/2d-and-terrain.md#polygon2d-polygon_solids--hull2d) |
| `hull2d` | 2D convex hull of points or mesh XY vertices (`double` or `int64`) | `ConvexHull2` | [2D & Terrain](docs/2d-and-terrain.md#polygon2d-polygon_solids--hull2d) |
| `dgm` / `dgm_tin` | Read/write XYZ/ESRI height grids and build error-certified TINs with breaklines | `HeightGridReader`/`Writer`, `TerrainGridTin` | [2D & Terrain](docs/2d-and-terrain.md#height-grids-dgm-dgm_tin) |
| `slope` | Certified cut/fill slope design from GeoJSON toes to a terrain TIN | `TerrainSlopeDesign`, `SlopeToe`, `SlopeSpec` | [2D & Terrain](docs/2d-and-terrain.md#slope-design-input-slope) |
| `cell_volumes` | Cut/fill volumes per regular XY lattice cell (and elevation band, `--bands`) between two terrains | `TerrainCellVolumes` | [2D & Terrain](docs/2d-and-terrain.md#cell_volumes--tpms) |
| `tpms` | Certified TPMS lattices (gyroid, Schwarz, Neovius, I-WP, Lidinoid) | `TpmsLattice` | [2D & Terrain](docs/2d-and-terrain.md#cell_volumes--tpms) |

---

## Build and Run

```bash
dotnet build tools/g3cli -c Release
dotnet tools/g3cli/bin/Release/net10.0/g3cli.dll help
# or
dotnet run --project tools/g3cli -c Release -- measure part.stl
```

`g3cli help` lists all commands, result codes, and formats; `g3cli help <command>` (or `<command> --help`) prints every parameter with its type and default.

---

## Arguments

- Positional arguments follow the order shown in `help <command>`; all arguments may also be passed by name (`--name value` or `--name=value`).
- `-` and `_` are interchangeable (`--edge-length` equals `--edge_length`).
- Boolean flags accept `--flag` (`true`), `--flag=false`, or `--no-flag`.
- `--compact` emits single-line JSON (useful for piping into `jq`).
- Multi-mesh input files are merged into one mesh; output format is inferred from file extension.

---

## Output and Result Codes

Every invocation writes exactly one JSON object to `stdout` (any diagnostic logging goes to `stderr`) containing `status` (`ok` / `error`), `code`, and on failure `message`. Argument types and output formats are validated before reading inputs; failed commands never write an output file.

| `code` | Exit code | Meaning |
|---|---|---|
| `ok` | `0` | Success |
| `operation_failed` | `1` | Operation ran but produced no valid result (e.g., an open mesh without `--allow-open`) |
| `empty_mesh` | `1` | Input contains no triangles (or no valid grid cells) |
| `invalid_arguments` | `2` | Missing, unknown, malformed, or out-of-range argument |
| `unknown_command` | `2` | Unrecognized subcommand |
| `read_error` | `3` | Input file missing, unreadable, or unparseable |
| `unknown_format` | `3` | Unsupported input or output file extension |
| `write_error` | `4` | Output file could not be written (e.g., missing directory) |
| `internal_error` | `70` | Unexpected exception |

### Standard `mesh` Summary Schema

Commands that inspect or modify a mesh return the same `mesh` object (`input_mesh` for the input before modification):

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

- `volume` is `null` unless `closed == true`; `genus` is `null` unless the mesh is a single closed manifold component.
- `clean`, `not_clean_reason` (first failed check), and `not_clean_reasons` (all failed checks) mirror `MeshRobustRepair.NotCleanReason`.
- `winding_closed` is `true` when every edge is balanced in both directions (enabling exact winding numbers in `boolean`).
- `self_intersections` is exact and skipped (`null`) when `--no-self-intersections` is passed.

---

## Quickstart Examples

```bash
# Measure & script integration
g3cli measure part.stl --compact | jq '.mesh.volume'

# Repair into a guaranteed-clean closed solid
g3cli repair scan.obj scan_fixed.stl

# Exact boolean difference (SafeRounding on by default)
g3cli boolean part.stl tool.stl cut.3mf --op difference

# Remesh preserving sharp features, or quad-remesh to OBJ
g3cli remesh part.stl part_remeshed.obj --edge-length 0.5 --crease-angle 40 --deviation
g3cli quad part.stl part_quad.obj --edge-length 2.5 --pure-quads

# 2D quality mesh from JSON or Triangle .poly
g3cli mesh2d field.json field.obj --min-angle 30 --max-area 50

# Certified DGM-to-TIN and slope design
g3cli dgm_tin dgm.asc dgm_tin.ply --epsilon 0.05 --breaklines benches.geojson
g3cli slope dgm_tin.ply pit_toe.geojson pit_composite.ply --tolerance 0.05 --catch-lines pit_catch.geojson
```

### Triangle files (`.poly`, `.node`, `.ele`)

Full mapping rules, limits, and worked examples for Triangle exchange files are documented in [docs/2d-and-terrain.md#triangle-files-poly-node-ele](docs/2d-and-terrain.md#triangle-files-poly-node-ele).

---

## MCP Server

Start the stdio MCP server with `g3cli mcp` (Claude Code: `claude mcp add g3 -- dotnet /path/to/g3cli.dll mcp`). See [docs/mcp-reference.md](docs/mcp-reference.md) for full configuration, protocol versions, and JSON-RPC examples.

---

## Tests

```bash
dotnet test tests/g3cli.Tests
```

`tests/g3cli.Tests` tests every subcommand on procedural meshes, 2D domains, and terrains—including format round-trips, quality bounds, `query2d` vs. brute force, fuzzed `mesh2d` JSON, error/exit codes, and the MCP server both in-process and as a child stdio process.
