# CLI / MCP update: what g3cli does not expose yet

Status: approved by the owner 2026-11-12 as proposed (step 1 of the queued CLI / MCP update, owner 2026-11-08), with the
decisions in section 11. Tier S (docs only). Each command is public surface, so every entry below is a decision for the owner.
Basis: `master` at b25eb46 (2026-11-12), compared with `tools/g3cli` (`Name = "..."` of every `Command`, their
`Param` lists) and the public classes of the library and of the Clipper2 addon.

Conventions for all new commands (existing ones, [tools/g3cli/README.md](../../tools/g3cli/README.md)): one JSON
object with `status`, `code`, `warnings`, `elapsed_ms`; snake_case options, the same `Param` list for the CLI and the
MCP tool; failures are result codes, never stack traces; no output file on failure; large inputs go through files,
inline lists are capped (10 000 items), per-item lists are cut at `--max-results` (default 100) with the counts exact.
Every new option gets a test in `tests/g3cli.Tests` and a line in the README; a new input format goes into
`ReaderFuzzTests` (and `McpFuzzReplayTests` where the MCP path reads it).

## 0. Already exposed (no new command; only the gaps are listed)

The task list names several items that exist since the Phase 4 work. Verified against the code:

| Item of the brief | Exposed as | Gap (section) |
|---|---|---|
| analysis blocks | `quality`, `deviation`, `thickness`, `fit`, `measure` | curvature, tet quality (4) |
| fidelity metrics | `fidelity` (thresholds, samples, sharp edge angle, outer surface, curvature, seed) | `outer_offset`, `sharp_edge_min_height` (2) |
| geodesics | `geodesic` (heat method, path, through, closed, `intrinsic_delaunay`, `time_factor`) | refinement and tracing options (3) |
| batch queries raycast / inside | `query3d` (one command: rays, any-hit, nearest, inside crossings / winding, `max_distance`, `parallel`) | file output, auto method, camera (5) |
| 2D booleans via Clipper2 | `polygon2d` (union, intersection, difference, xor, offset) | line clipping, line offset, Minkowski, simplify (6) |
| Meshing2D heights | `mesh2d` (`heights` in the JSON, `--poly-heights`) | none found |

## 1. Marching cubes: `LinearEdges` and the conservative enclosure

**Library**: `MarchingCubes` / `MarchingCubesPro` with `RootMode = LinearEdges`
([CONSERVATIVE_MC_AUDIT.md](CONSERVATIVE_MC_AUDIT.md)), `MeshSignedDistanceGrid`, `DenseGridTrilinearImplicit`.
There is no command that runs marching cubes at all. The input of MC is a field, so the CLI form is an **offset
surface of a mesh**, the use case of `docs/examples/OffsetSurfaceExample.cs`.

Proposed command **`offset`** (priority **P2**):

```
offset <input> <output> --distance d [--cell-size h | --relative-cell-size r] [--root-mode single_lerp|lerp_steps|bisection|linear_edges]
       [--root-steps n] [--padding p] [--max-cells n] [--parallel true]
```

- Pipeline: `MeshSignedDistanceGrid` (exact band wide enough for `distance`) → `DenseGridTrilinearImplicit` →
  `MarchingCubes` at `IsoValue = distance`; the bounds are the mesh bounds expanded by `max(distance, 0) + 2 cell sizes`.
  `--max-cells` (default 64 million) refuses a grid that is too large with `invalid_arguments` before any work.
- `--root-mode linear_edges` throws in the library (`InvalidOperationException` for a crossing on a corner or a
  collapsed triangle, `ArgumentException` for non-finite samples): the command maps that to `operation_failed` with
  `offset_status` `linear_edges_precondition` and writes nothing (the partial mesh must not be used).
- Result: `{ "distance": d, "cell_size": h, "cells": [nx, ny, nz], "root_mode": "linear_edges", "triangles": n, "vertices": n,
  "mesh": {…}, "input_mesh": {…}, "enclosure_claim": "none" }`.
- **No enclosure guarantee is claimed.** The audit states that the conditional enclosure recipe (exact 1-Lipschitz
  samples, normalized coordinates in [-1, 1], exact grid nodes, high samples on the extraction boundary, no FMA) does
  not hold for an unquantified approximate mesh SDF ("Do not … claim a bound for unquantified approximate mesh-SDFs").
  The command therefore exposes the mode as an option only, documents the preconditions, and reports
  `enclosure_claim: "none"`. A command that promises "the offset surface encloses the input" needs a separate
  decision (a quantified SDF error and a padded level); see open question Q1.
- Related, smaller: the voxel surface (`VoxelSurfaceGenerator`, `MaxMeshElementCount`) belongs to a voxelization
  command, **`voxelize`** (priority **P3**): `voxelize <input> <output> --cell-size h [--cap-at-boundary] [--skip-interior-faces]
  [--max-mesh-elements n]`; the library splits the surface into several meshes once a mesh reaches the element count, so the
  result lists `parts` (one entry per mesh: triangles, vertices) and the output is one file per part
  (`<name>_0.obj`, …) or, with a format that holds several meshes (glTF), one file. `max_mesh_elements` below 12 is
  `invalid_arguments` (a voxel face needs 4 vertices, 2 triangles).
  The library has no mesh-to-voxel function in the shipped classes (`Bitmap3` is the grid; the PR would fill it from the inside test of `DMeshAABBTree3`, or from the SDF sign), so this command needs a design step first. `max_mesh_elements` below 12 is `invalid_arguments`.

## 2. Fidelity: two missing options (P2, small)

`fidelity` lacks `MeshFidelityMetrics.OuterOffset` (default 1e-5) and `SharpEdgeMinHeight` (default 1e-5). Add
`--outer-offset` and `--sharp-edge-min-height` (numbers, relative to `size` like the other lengths), defaults = library
defaults; the result gets `used_outer_offset` and `used_sharp_edge_min_height` next to the other used values.
No new command. One PR together with the geodesic options (3) is fine ("analysis options").

## 3. Geodesics: options (P3, small)

`geodesic` lacks, for distance mode: `--intrinsic-refine`, `--refine-min-angle` (25), `--refine-max-insertions`
(0 = unlimited), `--split-obtuse-boundary-edges` (true), `--max-iterations` (20000); for path mode: `--angle-tolerance` (1e-6),
`--max-iterations` (0 = library default). Distance mode additionally `--trace-from <vertex|x,y,z>` returning the
gradient-descent path (`TracePath`) as `trace: [[x, y, z], …]` cut at `--max-results`. Result adds `intrinsic_flips`, and with
refinement the insertion counts of `IntrinsicRefinementResult`. No new command.

## 4. New analysis commands (P2 curvature, P3 tet quality)

Not in the brief's list explicitly ("analysis blocks"); these are the public analysis classes without a command:

- **`curvature`** (P2; `MeshCurvature.Compute`): `curvature <input> [--output curvature.csv] [--percentiles 1,5,50,95,99]`
  → `{ "valid_vertices", "boundary_vertices", "bowtie_vertices", "degenerate_triangles", "mean": {min, max, mean, rms, percentiles},
  "gaussian": {…}, "angle_deficit_total", "total_absolute_mean_curvature" }`; `--output` CSV (`vertex,mean,gaussian,area,valid`) or JSON by
  extension. Curvatures are `H` and `K` in mesh units; add `h_size` and `k_size2` (times `size`, `size²`) as `fidelity` does.
  Codes: `ok`, `empty_mesh`, `read_error`; invalid vertices are counted, not an error.
- **`tetquality`** (P3; `TetMeshQuality`): only if a tet mesh input format exists (it does not: the readers produce triangle
  meshes). Not proposed until a `.tet` / TetGen-style reader is wanted (would need `ReaderFuzzTests`). Listed so it is not forgotten.

## 5. Batch queries: file output, auto method, camera (P1)

`query3d` realizes the `raycast` / `inside` shape of [BATCH_QUERIES.md](BATCH_QUERIES.md) section 5 as one command with
inline lists (`ox,oy,oz,dx,dy,dz;…` or `@file`). Open from that design:

1. `--output <file.csv|json>`: the full per-item results (CSV `ray,triangle,t,x,y,z,front_face` / `point,inside,…`); the JSON result keeps the
   counts and the first `max_results`. P1: the design says "fully written to `--output`", and inline results cut at 100 are unusable for a
   depth image or an inside map.
2. `--inside auto` as a third value of `--inside` (the design's default method): crossings if the mesh is closed and manifold (as
   `measure`), else winding number; the result states `method_used` and warns for an open mesh. The crossings test on an open mesh
   has no meaning, so the user should not have to know. Additive: the option stays absent = no inside test.
3. `--rays` files as JSON (`[{"origin":…,"direction":…}]`) in addition to the line format; `--normalize` (default true: the library
   needs unit directions and reports `NotNormalized` per ray).
4. `--camera "px,py,pz;tx,ty,tz;fov;w;h"` pinhole ray generation with a depth image output (`--depth-output depth.pgm|csv`): P2, stage 3 of the design.
5. `--mode all` (all hits along a ray): the library has none yet (decided: nearest and any-hit first, all-hits later). Not proposed
   until a library change exists; library work is not part of this update.

## 6. Polygon operations of the Clipper2 addon (P2)

`polygon2d` covers `Union / Intersection / Difference / Xor / Offset`. The addon also has `ClipPolyLines`, `OffsetPolyLines`,
`MinkowskiSum` / `MinkowskiDifference`, `Simplify`, `SimplifyPolyLines`. Proposed, as new `--op` values of `polygon2d` (same
options, same result block, same GeoJSON frame):

| `--op` | Input | Extra options | Result |
|---|---|---|---|
| `clip_lines` | subject: GeoJSON `LineString` / `MultiLineString`; clip: polygons | `--clip-type intersection|difference` (default intersection) | `result.lines`, `result.length` |
| `offset_lines` | `LineString` / `MultiLineString` | `--delta`, `--join`, `--end-type round|square|butt|joined` | the offset polygons as for `offset` |
| `simplify` | polygons (or lines with a LineString subject) | `--epsilon e` (units) | polygons, counts of removed vertices |
| `minkowski_sum` / `minkowski_difference` | pattern: one polygon outer ring (`--pattern` GeoJSON); path: subject ring or line | `--path-closed` (true for polygons) | polygons |

Priority **P2** for `simplify` and `clip_lines` (map data pipelines), **P3** for Minkowski. Codes as today (`read_error` for a bad
GeoJSON, `invalid_arguments` for a missing `--epsilon`, `operation_failed` with the addon's `Clipper2Status`). The Union limitation
with degenerate self-intersecting rings (FINDINGS item 5, platform.md "Open") is a property of the pinned package; no change here.

## 7. PlanarComplex solids (P2)

`PlanarComplex.FindSolidRegions(FindSolidsOptions)` (`SimplifyDeviationTolerance`, `SimplifyRelativeTolerance` since #658,
`WantCurveSolids`, `TrustOrientations`, `AllowOverlappingHoles`) is the library's way from a loop soup to polygons with holes;
no command uses it. Proposed **`polygon_solids`** (input: GeoJSON `Polygon` / `MultiPolygon` / `LineString` rings, or a mesh2d-style ring list):

```
polygon_solids <input> [--output solids.geojson] [--simplify-tolerance t] [--relative-tolerance r] [--trust-orientations]
               [--allow-overlapping-holes] [--curve-solids] [--return-geojson]
```

- Result: `{ "solids": n, "holes": n, "area": a, "holes_area": a, "used_tolerance": {"absolute": t, "relative": r}, "loops": {"input": n, "valid": n, "dropped": n}, "bounds": {…} }`.
- Semantics fixed from the XML doc: `--relative-tolerance` ≤ 0 or NaN is off; the effective tolerance of a loop is the smaller of the
  two when both are given; defaults as `FindSolidsOptions.Default` (absolute 0.1 in input units). `--simplify-tolerance` default 0 in
  the CLI is a **deviation from the library default** (0.1) and would be surprising; keep the library default and say so in the help.
- Codes: `ok`, `read_error`, `invalid_arguments`, `empty_mesh` never (not a mesh); inputs without a closed loop → `operation_failed`
  with `solids: 0`.
- Priority **P2**: it makes the new relative tolerance reachable. Small (one file, about 200 lines with tests).

## 8. ConvexHull2 modes (P3)

`ConvexHull2(vertices, epsilon, QueryNumberType)` supports two query types: `QT_DOUBLE` (floating point, not robust in all cases)
and `QT_INT64` (points scaled to [0, 2^20]^2, fast, loses precision). `QT_INTEGER`, `QT_RATIONAL` and `QT_FILTERED` throw
`NotImplementedException` (`comp_geom/ConvexHull2.cs`), so there is **no exact mode** to expose. The degenerate outputs are
`Dimension` 0, 1 or 2 (`Get1DHullInfo` for collinear input, `epsilon` only decides that); a non-finite coordinate throws
`ArgumentException`. No command today.

Proposed **`hull2d`**: `hull2d <points.csv|geojson|mesh> [--query double|int64] [--epsilon e] [--output hull.geojson]`
→ `{ "dimension": 2, "hull_vertices": n, "area": a, "perimeter": p, "indices": [..], "polygon": [[x,y],...], "query": "double" }`;
`dimension` 1 gives the segment end points, 0 one point; non-finite input is `invalid_arguments`. The default is `double`; `int64`
must say in its help that it rounds the points to a 2^20 grid of their bounding square (the hull is of the rounded points, so
`indices` can differ from `double` near collinear points). The command only calls the public constructor (ConvexHull2 belongs to
another session; no library change). If an exact mode is wanted, that is library work first (Q3). Priority **P3**.

## 9. Repair and boolean: the options and result codes of the last months (P1)

Compared with the `MeshRobustRepair` / `ExactMeshBoolean` fields, `repair` lacks (`boolean` see below):

`repair` (`robust` method) — library fields without an option:

| Option | Library field | Default |
|---|---|---|
| `--exact-resolve-rounds` | `ExactResolveRounds` | 4 |
| `--wrap-relative-alpha`, `--wrap-relative-offset` | `WrapRelativeAlpha`, `WrapRelativeOffset` | 300, 3000 |
| `--alpha-wrap-quality-pass` | `AlphaWrapQualityPass` | false |
| `--passthrough-clean-parts` | `PassThroughCleanParts` | true |
| `--separate-fraction` | `SeparateFraction` | 1e-4 |

Result: add `exact_resolve_rounds_used`, `alpha_wrap_triangles`, `alpha_wrap_simplified`, `alpha_wrap_quality_improved`,
`passed_through_triangles`, `phase_milliseconds` (object), and the wrap angle fields (`alpha_wrap_min_angle`, `alpha_wrap_max_angle`,
`alpha_wrap_share_above_170`), each `null` when NaN. Of the result-reason constants (`WrapTooCoarse`, `LostFarPart`, `ScaleNotFinite`, `OutsideRange`,
`ScaleDisparity`) `ReasonCode` maps only `scale_not_finite` and `scale_disparity`; add stable codes `wrap_too_coarse`,
`lost_far_part`, `outside_range`. Existing output stays unchanged (additive only).

`boolean`: no gap found. `Seed` only picks random ray directions ("the result does not depend on it"), so an option would be
dead surface; the counters (`intersecting_pairs`, `self_intersecting_pairs`, `patches`, `non_manifold_edges`,
`non_manifold_triangles`, `removed_vertices`, `counts`) and the statuses (`open_input`, `invalid_input`, `rounding_not_clean`)
are reported already. Not proposed: `ComputeExactVertices` (exact coordinates in the JSON are large) and a general
`WindingPredicate` (arbitrary code does not fit a command line; `at_least` and `xor` cover the named cases). The ready part of
the "new boolean options" in the brief is therefore only the repair side; if the owner knows a boolean option added after
b25eb46 that I missed, it comes in with PR A.

Priority **P1**: these are options of commands that agents already call, the cost is a few `Param` lines and tests per option, and
the sessions that added the library options (Alpha Wrap 2.0, SafeRounding) expect them reachable.

## 10. Proposed PR sequence and priorities

| Priority | PR | Contents | Size |
|---|---|---|---|
| P1 | A | `repair` / `boolean` options and result fields (9) | S |
| P1 | B | `query3d` output file, `inside auto`, JSON ray file (5.1-5.3) | S-M |
| P2 | C | `polygon_solids` (7) | M |
| P2 | D | `polygon2d` ops `simplify`, `clip_lines`, `offset_lines` (6) | M |
| P2 | E | `curvature` (4), `fidelity` / `geodesic` options (2, 3) | M |
| P2 | F | `offset` with `--root-mode` (1), after Q1 | M |
| P3 | G | `hull2d` (8), Minkowski (6), `voxelize` (1), `query3d --camera` (5.4) | each S-M |

Each PR: tests in `tests/g3cli.Tests` (`McpTests` for the MCP schema and call, `CliTests`-style for the command, `McpFuzzReplayTests` /
`ReaderFuzzTests` where an input format is read), `tools/g3cli/README.md`, `docs/OPERATIONS.md`, and the roadmap line
(`docs/roadmap/platform.md`) in the same PR; draft first, full local list of CLAUDE.md before ready, independent review.
`CliCoverageTests` checks that every command and `Param` has a test; no mesh files are committed (generated meshes only).

## 11. Decisions (owner 2026-11-12) and the questions they answer

- **Q1** `offset` exposes `linear_edges` as an option only, `enclosure_claim: "none"`; no certified offset.
- **Q2** `polygon_solids` keeps the library default (absolute 0.1), stated in the help.
- **Q3** `hull2d` exposes `double` and `int64`.
- **Q4** Order: PR A, PR B, then C to G as in section 10.
- **Q5** `tetquality` and `--mode all` stay out of this update.

The questions as asked:

- **Q1** `offset`: expose `linear_edges` as an option without an enclosure claim (proposed), or first define a certified offset
  (a quantified SDF error bound, a padded level) as a library step? The second is library work with a bounds review (tier L).
- **Q2** `polygon_solids` default tolerance: keep the library's absolute 0.1 (proposed, may surprise on metre-scale data) or default
  to 0 / relative in the CLI only?
- **Q3** `hull2d`: expose `double` and `int64` (proposed), or wait for an exact `ConvexHull2` mode (library work, another session)?
- **Q4** Priorities: P1 = A, B; confirm or reorder.
- **Q5** `tetquality` and `--mode all` wait for a reader / a library change: confirm they stay out of this update.

## How others solve it

- MCP tool surfaces of geometry servers keep one tool per operation with typed arguments and a structured result, large payloads by
  file path (the MCP specification 2025-06-18, tools and `structuredContent`); same as the existing g3cli.
- Geometry libraries let the caller choose the number type of a predicate (exact vs. filtered vs. floating point; Shewchuk 1997,
  adaptive predicates): `--query double|int64` mirrors `ConvexHull2`'s two working query types (approach only).
- Offset surfaces by marching cubes on a signed distance field (Bloomenthal, *Polygonization of implicit surfaces*; Jacobson,
  *A Sharp Conservative Offset for Marching Cubes*, the source of the audit) state their guarantees per field class, which is why
  `offset` claims nothing for an approximate mesh SDF.
- Depth images by a pinhole camera over batched ray queries: renderers' ray-generation convention (one ray per pixel centre).
