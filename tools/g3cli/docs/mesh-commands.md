# g3cli Mesh Commands

Detailed reference for mesh repair, exact booleans, remeshing, simplification, offsetting, wrapping, quad remeshing, tetrahedralization, UV atlases, and format conversion. See [../README.md](../README.md) for argument rules, JSON envelopes, and exit codes.

---

## `repair`

Repairs a mesh into a closed solid using `MeshRobustRepair` (`--method robust`, default) or `MeshAutoRepair` (`--method auto`).

```bash
# Default robust repair (exact resolve, then solidify / alpha wrap only if needed)
g3cli repair scan.obj scan_fixed.stl
#   -> "used_exact_resolve": true, "used_solidify": false, "fallback_reason": "self-intersections", ...

g3cli repair scan.obj scan_fixed.stl --no-exact-resolve
g3cli repair scan.obj scan_fixed.stl --method auto --tolerance 1e-5
g3cli repair scan.obj scan_fixed.stl --check-lost-components --no-alpha-wrap
g3cli repair scan.obj scan_fixed.stl --rounding-max-displacement 1e-6
```

### Robust Repair Options (`--method robust` only)

- **Pipeline switches & limits:** `--exact-resolve-max-pairs`, `--exact-resolve-rounds` (`1..64`), `--safe-rounding`, `--alpha-wrap`, `--simplify-alpha-wrap`, `--alpha-wrap-quality-pass`, `--wrap-relative-alpha` / `--wrap-relative-offset` (`1..1e6`), `--wrap-target-triangles` / `--wrap-target-factor` / `--wrap-target-max-distance`, `--check-lost-components`, `--reject-topology-change`, `--passthrough-clean-parts`, `--separate-fraction` (`(0, 1)`), `--measure-result-iou`.
- **Exact-stage SafeRounding options:** Same 7 options as `boolean` (`--rounding-max-displacement`, `--rounding-ulp-factor`, `--rounding-absolute-tolerance`, `--rounding-max-pair-tests`, `--rounding-defect-scale`, `--rounding-max-growth`, `--strict-rounding`). A failing `--strict-rounding` rejects the exact stage (`exact_resolve_reason`) and continues to fallback stages; post-separation rounding uses the relative bound only.
- **Scale disparity:** If far-apart parts make the clean-check degenerate threshold engulf real triangles, `repair` fails with `operation_failed` (`result_code: "scale_disparity"`) and writes nothing.

- **Clean input:** a `robust` input that is clean as a whole (one or more parts) is returned as it is (`result_path: PassThrough`; `volume_iou` and `phase_milliseconds` are empty) unless `--tolerance` is set.

### Additive Result Fields (`repair`)

- `result_path`: winning stage (`PassThrough`, `Repair`, `ExactResolve`, `Solidify`, `AlphaWrap`).
- `result_code` / `exact_resolve_code`: structured codes (`scale_not_finite`, `scale_disparity`, `outside_range`, `lost_far_part`, `lost_far_open_part`, `lost_separate_part`, `wrap_too_coarse`, or `null`).
- `exact_resolve_rounding` and `separated_rounding`: SafeRounding counts for the two exact-stage rounding passes.
- `genus_before` / `genus_after`: warns when exact resolve alters component genus.
- `result_volume_iou` (with `--measure-result-iou`), `robust_options`, `exact_resolve_rounds_used`, `passed_through_triangles`, `used_alpha_wrap`, `alpha_wrap_simplified`, `wrap_reason`, `alpha_wrap_quality_improved`, `alpha_wrap_target` / `alpha_wrap_target_reached` / `alpha_wrap_target_limit`, `alpha_wrap_min_angle` / `alpha_wrap_max_angle` / `alpha_wrap_share_above_170`, `used_input`, `result_reason`, `phase_milliseconds`.

---

## `boolean` & `resolve`

- `boolean` (`ExactMeshBoolean`): exact binary or n-ary (`--more "c.stl;d.stl"`) `union`, `intersection`, `difference` (first minus others), `at_least --k K`, or `xor` (odd positive winding count; parts touch along crossing curves with duplicated vertices).
- `resolve` (`ExactMeshBoolean.Resolve`): removes self-intersections, nested shells, and duplicate faces in one mesh, automatically reorienting inconsistent components by area majority.

```bash
g3cli boolean part.stl tool.stl cut.3mf --op difference
g3cli boolean --a a.obj --b open.obj --output ab.glb --op union --allow-open
g3cli boolean a.stl b.stl all.stl --op union --more "c.stl;d.stl"
g3cli boolean a.stl b.stl two.stl --op at_least --k 2 --more c.stl
g3cli boolean a.stl b.stl odd.stl --op xor --more c.stl
g3cli boolean a.stl b.stl fast.stl --op union --no-safe-rounding
g3cli resolve assembly.off solid.ply --simplify-flat-regions false
g3cli resolve site_utm.stl solid.stl --strict-rounding
g3cli resolve site_utm.stl solid.stl --rounding-ulp-factor 0 --rounding-absolute-tolerance 1e-3
```

### Open Inputs (`--allow-open`)

An input that is not closed (in the winding number sense: every edge used equally often in both directions) fails with `operation_failed` and `OpenInput` in the message, which names the inputs by index. This is the default since 2026-11-23 (before, open inputs were accepted), as for `ExactMeshBoolean.AllowOpenMeshes`. `--allow-open` accepts them and classifies them by the generalized winding number above 1/2: a deep open shell still encloses, a flat open cutting surface usually cuts nothing and the first input comes back unchanged. `resolve` repairs one mesh and takes open inputs as before.

### Orientation & SafeRounding Options

- **Orientation (`boolean`):** Closed inputs with flipped faces fail with `inconsistent_orientation` unless `--orientation area_majority` is passed; `--orientation by_nesting` orients an embedded input that is closed modulo 2 by the parity of the crossings of one exact ray per component (the solid is what the surface encloses); `--count-inside-out` reports `boolean.inside_out_cells`.
- **SafeRounding (on by default):** `--no-safe-rounding` disables repair after rounding (`boolean.rounding: null`). Tuning options: `--rounding-max-displacement` (default `1e-5` of diagonal), `--rounding-ulp-factor` (default `256`, for large/UTM coordinates), `--rounding-absolute-tolerance`, `--rounding-max-pair-tests` (default `5M`), `--rounding-defect-scale` (`50000`), `--rounding-max-growth` (`4`), `--strict-rounding` (fails with `rounding_not_clean` rather than writing defects).

### Additive Result Fields (`boolean` / `resolve`)

- `boolean.status` (`success`, `open_input`, `invalid_input`, `rounding_not_clean`, `inconsistent_orientation`), `boolean.safe_rounding`, `boolean.rounding_clean`, `boolean.contacts`, `boolean.reoriented_triangles`, `boolean.counts` (`null` on early failure).
- `boolean.rounding`: `initial_intersections`, `remaining_intersections`, `initial_false_contacts`, `remaining_false_contacts`, `vertex_removals`, `max_drift`, `max_length`, `smallest_rejected_drift`, `remaining_defect_at` (`[x, y, z]` or `null`), `scale_disparity`, `scale_not_finite`, `evaluation_limit_reached`.
- On failure (`operation_failed`): `boolean_status`, `boolean_error`, and the `boolean` object.

---

## `remesh`

Isotropic remeshing (`Remesher`, `MeshConstraintUtil`) preserving boundaries, creases, and sharp vertices.

```bash
g3cli remesh part.stl part_remeshed.obj --edge-length 0.5 --crease-angle 40 --deviation
g3cli remesh part.stl part_remeshed.obj --min-quality 0          # disable quality guard
g3cli remesh part.stl part_remeshed.obj --safe                   # min-quality 0.2 + PreventSelfIntersections
g3cli remesh part.stl part_remeshed.obj --sharp-vertex-angle 0   # disable sharp-vertex pinning
```

- `--min-quality` defaults to `0.2` in `g3cli` (set `0` to disable, e.g., for very thin walls below edge length). With it, collapses and valence flips that turn a triangle away from the surface around the edge are rejected (issue #1441: triangles facing into a smooth surface; measured on jittered spheres, not a guarantee). This changed the output of `remesh` with `--min-quality` above 0.
- `--sharp-vertex-angle` defaults to `75` degrees (`sharp_vertices_pinned` in output).
- `--safe` enables `Remesher.ApplySafePreset()` (`MinTriangleQuality = 0.2` + `PreventSelfIntersections`; reports `self_intersections_rejected`).
- Passes that would turn a closed component inside out are rolled back (`rejected_passes`); warns if target edge length exceeds `4×` estimated wall thickness.

---

## `simplify`

QEM edge-collapse simplification (`Reducer`, `PartitionedReducer`) targeting triangle count, ratio, quadric error, or edge length.

```bash
g3cli simplify scan.ply scan_small.ply --ratio 0.1 --deviation
g3cli simplify scan.ply scan_small.stl --triangles 5000
g3cli simplify big.ply big_small.ply --max-error 1e-6 --partitioned --threads 4
```

- `--partitioned` requires `--max-error` (squared distance) and reduces 8 fixed slabs in parallel deterministically across `--threads` (`used_partition: true`; falls back to serial with a warning below 20,000 triangles; does not preserve boundary shape).

---

## `offset`

Signed distance offset (`--distance > 0` outward, `< 0` inward) of a closed mesh.

```bash
# Default method: sdf_mc (MeshSignedDistanceGrid + MarchingCubes)
g3cli offset part.stl part_offset.stl --distance 0.5 --cell-size 0.1
g3cli offset part.stl part_offset.stl --distance 0.5 --relative-cell-size 0.005 --root-mode linear_edges

# Certified dual-contouring offset (gs.MeshOffset)
g3cli offset part.stl part_offset.stl --distance 0.5 --method mesh_offset
```

- **`--method sdf_mc` (default):** `--cell-size` / `--relative-cell-size` (default `0.01` of diagonal), `--max-cells`, `--root-mode` (`single_lerp`, `lerp_steps`, `bisection`, `linear_edges`). `linear_edges` fails with `offset_status: "linear_edges_precondition"` without writing on precondition violations. Always reports `enclosure_claim: "none"`.
- **`--method mesh_offset`:** Dual-contours the exact distance field (default cell `|distance| / 4`, max `|distance| / 2`; `--max-cells` up to `2^28`; refuses `--root-mode`/`--root-steps`). Reports `enclosure_claim: "band"`, `band_deviation`, `band_certified`, `uncertified_triangles`, and exact `self_intersection_free`.

---

## `wrap`

Alpha wrap (`MeshAlphaWrap`) enclosing any mesh or soup in a closed, 2-manifold surface.

```bash
g3cli wrap scan.obj wrapped.stl                                          # library defaults (diag/20, diag/600)
g3cli wrap assembly.off wrapped.stl --relative-alpha 40 --relative-offset 1000
g3cli wrap scan.obj wrapped.stl --alpha 0.5 --offset 0.02 --max-steiner-points 500000
```

`wrap_status: "point_limit_reached"` succeeds (`ok`) with a warning (valid wrap with less detail); other non-success statuses return `operation_failed` with `wrap_status`. Reports `used_alpha`, `used_offset`, counts, and phase timings.

---

## `quad`

Quad-dominant or opt-in pure-quad remeshing (`QuadRemesher`, `IntegrableFrames`, `PolyMeshIO`).

```bash
g3cli quad part.stl part.obj --edge-length 2.5
g3cli quad smooth.stl smooth_quads.obj --edge-length 2.5 --size-adaptation scale
g3cli quad part.stl part_pure.ply --edge-length 2.5 --pure-quads
```

- **Formats:** Output must be `.obj` or `.ply` (`--binary false` for ASCII PLY). Triangle-only formats fail with `unknown_format`.
- **`--edge-length`:** Defaults in CLI to `max(diagonal / 30, 2 * meanEdge)` (`used_edge_length`).
- **Opt-in field & sizing options (library defaults off / `0` / `30`):**
  - `--size-adaptation off|scale|scale_and_rotation`: runs `IntegrableFrames` for closed smooth shapes (applied only when curl drops by at least 20 %; can be combined with `--pure-quads`).
  - `--curvature-alignment <num>`: principal-curvature alignment weight (`>= 0`).
  - `--align-creases` / `--crease-angle <deg>`: align the cross field with crease edges above `crease_angle` (`0..180`).
  - `--corner-angle <deg>`: boundary corner threshold (`0..180`) for pinning boundary vertices as lattice corners.
- **`--pure-quads` (closed meshes only):** On fallback, writes the quad-dominant mesh with `pure_quads_fell_back: true`, a warning, and `pure_quad_status` naming the first failed check (open meshes give `flow_infeasible`).
- Reports `quad_status`, `size_adaptation` / `size_adaptation_applied` (`curl_before`, `curl_after`, `min_spacing`, `max_spacing`), `pure_quad_status`, `faces` (`total`, `quads`, `non_quads`, by sides), `irregular_vertices`, `closed`, `manifold`, `quad_quality` (`median`, `p95`, `p99`, `max` for corner deviation and aspect), and `timings_seconds` (including `size_adaptation`).

---

## `tetmesh`

Tetrahedral meshing (`MeshTetrahedralization`, `TetMeshMarkers`, `TetMeshWriter`).

```bash
g3cli tetmesh part.stl part.msh                              # Gmsh 4.1 (--format msh22 for Gmsh 2.2)
g3cli tetmesh part.stl part.mesh --relative-alpha 80 --deviation
```

- **Formats (writers only):** Medit `.mesh`, Gmsh `.msh`, legacy ASCII VTK `.vtk`.
- **Options:** `--relative-alpha`, `--relative-offset`, `--min-dihedral` (default `10`), `--max-radius-edge` (default `2`), `--max-circumradius`, `--target-edge-length`, `--grading-factor`, `--max-steiner-points`, `--no-improve`, `--no-markers`, `--deviation`.
- **Markers & targets:** Boundary triangle refs store `marker + 1` (nearest input group ID or triangle ID) and tet refs store `region + 1` (`0` = unmarked). `targets` reports `min_dihedral_met` and `radius_edge_met` as measured facts.

---

## `uv` & `convert`

```bash
# UV atlas (OBJ, PLY, glTF/GLB, g3mesh preserve UVs)
g3cli uv part.stl part_uv.obj
g3cli uv part.stl part_uv.glb --method charts --max-area-distortion 0.1 --max-charts 64

# Format conversion (by file extension)
g3cli convert part.3mf part.glb
g3cli convert part.obj part.stl --binary=false
```

`uv` reports `atlas_status` (`distortion_limit_exceeded` if `--max-charts` was exhausted; layout remains overlap-free).
