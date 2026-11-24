# SheetExtensionProbe

Spike of [docs/design/SHEET_EXTENSION.md](../../docs/design/SHEET_EXTENSION.md) (section 10, step 2): the construction of
section 4, measured on procedural cases. The construction was built here with public members only (the spike); since the helper PR it is the library's
`gs.MeshSheetExtension` (`mesh_ops/MeshSheetExtension.cs`, [guide](../../docs/guides/booleans-and-imprint.md#a-sheet-that-ends-short-of-the-surface)) and this
project runs the library: `SheetExtension.cs` is an adapter that keeps the probe's option, report and refusal types, and the mutants of `mutants` are the
library's internal test switches (`InternalsVisibleTo`). The cases, the frames and the results are those of the spike (outcomes and volumes identical; the digits
past the 12th differ because the library's Boolean moved). Findings and the go / no-go:
[docs/research/SHEET_EXTENSION_SPIKE_2026-11.md](../../docs/research/SHEET_EXTENSION_SPIKE_2026-11.md).

```
dotnet run -c Release --project benchmarks/SheetExtensionProbe -- selftest         # every case in every frame; exit code 1 if a case does not end as expected
dotnet run -c Release --project benchmarks/SheetExtensionProbe -- cases [filter]   # the same, writes results/cases.jsonl and results/cases.md (no filter)
dotnet run -c Release --project benchmarks/SheetExtensionProbe -- mutants          # the mutation runs by hand, writes results/mutants.md
dotnet run -c Release --project benchmarks/SheetExtensionProbe -- cost             # the diagnosis, the construction, the imprint and the cut, writes results/cost.md (the last row is 20,000 sheet triangles against 49,152)
dotnet run -c Release --project benchmarks/SheetExtensionProbe -- all
dotnet run -c Release --project benchmarks/SheetExtensionProbe -- file <workpiece> <sheet> [reverse] [uncut=outside|inside]
```

- `file` reads two mesh files (any format of `StandardMeshReader`), runs the plain sheet role and the extension on them and prints one
  result row (JSON) and the report of every run (depth, length, strip area inside, exit angle, delta, U, curve count). `reverse` turns
  the sheet's orientation (the side that is kept), `uncut=` passes `UncutSide`.
- `selftest` also checks, on every single-sheet case, that a repeated run gives the same extended sheet and that a scaling by `2^-7`,
  `2^5` and `2^20` gives the same outcome and the scaled extended sheet bit for bit.
- No mesh file is committed; every case is generated in `Cases.cs` / `Geometry.cs`. The frames are the identity, translations of about
  `1e2`, `6e5` and `5.7e6`, and a rotation (every case that is expected to be continued or left unchanged and is not marked otherwise).

## Files

| file | what |
|---|---|
| `SheetExtension.cs` | the adapter to `gs.MeshSheetExtension`: `SheetExtension.TryExtend`, the options, the report and the refusal reasons of the spike mapped onto the library's (the construction itself is `mesh_ops/MeshSheetExtension.cs` now) |
| `Cases.cs`, `Geometry.cs` | the procedural cases with their analytic outcome and volume, and the generators (box, prism of a polygon, sphere, octahedron, height-field grids, welded sheets) |
| `Program.cs` | the modes, the frames, the rows and the mutants |
| `results/` | `cases.jsonl` / `cases.md` (one row per case and frame), `mutants.md`, `cost.md` of the run in the research note |

## What the construction does, and the choices the design left open

`TryExtend(sheet, workpiece, options, out report)` returns the extended sheet (the original triangles and ids unchanged, the strip
appended), the sheet itself when no rim edge meets the interior of the workpiece, or null with a named refusal and a witness.
Per run it reports the largest and mean depth, the rim length, the strip area inside the workpiece, the exit angle and delta, `U`,
the curve count and (for the cut) `U` relative to the smaller part; and the times of the construction, the verification, the imprint and the cut.

The choices, with their defaults (all options of `ExtensionOptions`):

| choice | what the probe does |
|---|---|
| continued rim | by edge: a rim edge is chosen if some part of it lies strictly inside the workpiece (ray hits along the edge, the winding number at the middle of each stretch, and a distance to the surface above `64` rounding units). A run is a chain of chosen edges joined at a vertex with `g > 0`; a vertex with `g = 0` ends a run |
| rounding unit | `2^-52` times the largest absolute coordinate of the sheet and the workpiece (so the construction is scale covariant for a scaling by `2^k`) |
| depth limit | `MaxExtension = 0.05` of the sheet's bounding-box diagonal (the owner's decision), on the Euclidean depth `g` of the first exit |
| overshoot | `o = max(o_min, min(o_max, half the distance to the ray's next hit))`, `o_max = 1 %` of the diagonal, `o_min = 1024` rounding units |
| exit | the first group of hits of the ray (equal parameters within `16` rounding units: a ray through an edge or vertex); the exit angle `alpha` is the smallest one over the group's triangles |
| `delta`, margin | `delta = max(delta_min, kappa g)`, `delta_min = 0.05 rad` (2.9 degrees); refused if `alpha <= delta + 0.05 rad`. `kappa`: the dihedral angle to each neighbour of the rim triangle over the distance of the centroids, the largest one (one row) |
| `U` | the trapezoid of `(1/2) g^2 sin(delta) sin(alpha) / sin(alpha - delta)` over the sample points of each rim edge (its two ends and the points of subdivision; 0 where `g = 0`) times the rim length |
| limit on `U` | `MaxUOverSmallerPart` defaults to none (the design leaves it open); the refusal exists as `UncertaintyAboveLimit` |
| non-manifold rim vertex | a refusal (`BowtieVertex`), no per-wedge points |
| direction at a point of subdivision | the normalized interpolation of the two end directions |
| subdivision | an outer segment that touches the workpiece (exact segment test) is split at the middle of the longest stretch of the rim edge that lies inside the workpiece between its two points; if the new point is not strictly outside, at `0.414` and `0.586` of the stretch; at most `14` levels |
| triangulation of a strip | the polygon `A, A', ..., B', B` is split by a fan from `A` up to the middle outer point and from `B` after it |
| strictly outside | the exact winding number is 0 and no triangle of the workpiece contains the point (exact `SegmentIntersectsTriangle` of a degenerate segment) |
| inverted quad | not the design's `(B' - A') . (B - A) > 0` but: every strip triangle's normal has a cosine above `1e-9` with the normal of the sheet triangle it continues (see the note, section 5) |
| curve rule | each run's strip is imprinted by the workpiece (`ExactMeshImprint`, `LabelCurves`); the curves are the connected components of `CurveEdges` (edges that share a vertex). The number of distinct `CurveId` values is reported as well and equals it in every case here |
| coplanar contact | a strip triangle with `Orient3d == 0` for all three corners against a workpiece triangle that it intersects (exact) |
| meets the sheet, itself, another tool | the self-intersecting pairs of the extended sheet (`MeshRepairMetrics.SelfIntersectionPairs`) with at least one new triangle; exact triangle tests against the other tools |
| acceptance | `ExactMeshBoolean` Difference with the roles `Solid`, `Sheet`, the other tools' roles, `AllowOpenMeshes`, and `UncutSide` as given; its refusal is `SheetRoleRefused` with `SheetReason` |

Mutations are options of the construction (`Verify` flags, `Exit`, `Overshoot`, `Rim`, `Subdivide`, `FlipQuads`); the `mutants` mode runs
each over all cases in the identity frame and lists the cases that no longer end as expected, the ones accepted wrongly, and the ones that
move to another (still acceptable) refusal.
