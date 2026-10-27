# g3cli: mesh operations as CLI and MCP server

`g3cli` is the headless layer of this fork (phase 4 of [docs/ROADMAP.md](../../docs/ROADMAP.md)): the
geometry3Sharp mesh operations on mesh files, as subcommands with JSON output and, with `g3cli mcp`, as the
tools of an MCP server (stdio), for coding agents and scripts.

| Command | Operation | Library classes |
|---|---|---|
| `measure` | counts, components, closedness, volume, area, bounds, genus, bowties, degenerate and inside-out parts, self-intersections (exact), Hausdorff distance to a second mesh | `MeshMeasurements`, `MeshRepairMetrics`, `MeshHausdorffDistance` |
| `repair` | repair into a closed solid (`--method robust`, default, or `auto`) | `MeshRobustRepair`, `MeshAutoRepair` |
| `boolean` | union, intersection, difference (a minus the others), at least k of n; two or more meshes (`--more`) in one exact operation | `ExactMeshBoolean` |
| `resolve` | boundary of the solid of one mesh: removes self-intersections, nested shells, duplicate faces | `ExactMeshBoolean.Resolve` |
| `remesh` | isotropic remeshing to a target edge length, boundary and sharp edges kept; passes that would turn a closed part inside out are rolled back (`rejected_passes`), a warning if the edge length is more than 4 times the estimated wall thickness | `Remesher`, `MeshConstraintUtil` |
| `simplify` | quadric edge collapse to a triangle count, ratio, error or edge length | `Reducer` |
| `uv` | UV atlas: seams / cones / charts, BFF, packing, exact overlap test, distortion limits | `UVAtlasGenerator`, `MeshDiscCut`, `ConeFlattening` |
| `convert` | STL, OBJ, OFF, PLY, 3MF, glTF/GLB, g3mesh (by file extension) | `StandardMeshReader`, `StandardMeshWriter` |

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

# self-intersecting / overlapping shells in one file -> one clean solid
g3cli resolve assembly.off solid.ply

# remesh to edge length 0.5, keep edges sharper than 40 degrees, report the deviation
g3cli remesh part.stl part_remeshed.obj --edge-length 0.5 --crease-angle 40 --deviation

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
cubes, sphere): format round trips, expected volumes of booleans and repairs, error cases with their result
and exit codes, the MCP protocol in process and the real executable as a child process (stdio). CI runs it
on every push.

```
dotnet test tests/g3cli.Tests
```
