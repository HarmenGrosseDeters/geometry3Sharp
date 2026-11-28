# 2D Meshing, 2D Polygons (Clipper2) & 3D Tetrahedralization

This guide covers exact 2D Delaunay and quality meshing (`DelaunayMesh2`, `ConstrainedDelaunayMesh2`, `QualityMesh2`), 2D polygon booleans/offsets via the optional `geometry3Sharp.Clipper2` add-on (`Clipper2Polygons`), loop nesting (`PlanarComplex`), 2D convex hulls (`ConvexHull2`), and 3D tetrahedral meshing (`MeshTetrahedralization`).

---

## 2D Delaunay & Constrained Delaunay Triangulation

- **Namespace & classes:** `g3.DelaunayMesh2`, `g3.ConstrainedDelaunayMesh2`, `g3.TriangulationMesh2`, `g3.ConstrainedMesh2`
- **Example:** [Meshing2DExample.cs](../examples/Meshing2DExample.cs)
- **Input / mutability:** Read-only points, segments, rings, holes, and regions; produces a `TriangulationMesh2` / `ConstrainedMesh2` (and `ConstrainedMesh2.ToDMesh3()` for a 2.5D `DMesh3`).
- **Guarantees & limits:** Uses exact orientation and in-circle predicates (`ExactPredicates`), producing a valid triangulation on the output `double` coordinates. Coordinates up to `1e70` are supported. Crossing segments are split (noding); intersection points are rounded to nearest `double` (so pieces can sit a few ULPs off the exact line), vertices within `2^-41 * max(|coord|, extent)` of a segment are snapped onto it, and only exact duplicate points are merged. `ConstrainedDelaunayMesh2.TryGetInteriorPoint` finds a double-precision point certified strictly inside an arbitrary closed ring (under exact even-odd parity), returning `false` when no binary64 interior point exists.
- **Further reading:** [MESHING2D_PLAN.md](../design/MESHING2D_PLAN.md)

---

## 2D Quality Meshing (`QualityMesh2`)

- **Namespace & classes:** `g3.QualityMesh2`, `g3.ConstrainedMesh2Input`, `g3.Mesh2Region`
- **Input / mutability:** Produces a refined `ConstrainedMesh2` from points, segments, rings, holes, and labeled regions.
- **Guarantees & limits:** Performs Ruppert / Shewchuk Delaunay refinement to meet a minimum angle, global or per-region maximum area, and optional sizing function, with off-centers (Üngör 2004) for poor-angle triangles and locked-segment support.
  - **Constrained vs. Conforming:** Default `Conforming = false` produces a constrained Delaunay mesh (for `MinAngle <= 25.65°`, segments are encroached only inside their diametral lens; above that, their diametral circle). `Conforming = true` makes every triangle Delaunay across constraint segments as well.
  - **Small input angles & Steiner limits:** Small angles forced by input constraints remain and are counted in `BadTriangles`. Reaching the Steiner-point budget returns a valid mesh with `SteinerLimitReached = true` (quality bounds not guaranteed).
  - **Grid snapping:** With a snap tolerance, every vertex—including Steiner points added during refinement—lies on the snap grid.
  - **2.5D export (`ConstrainedMesh2.ToDMesh3()`):** Copies vertex and triangle IDs, interpolates vertex heights into `z` (or `0` without heights), and writes region labels as triangle groups.
- **CLI:** `g3cli mesh2d` (JSON or Triangle `.poly` / `.node` / `.ele` files; see [g3cli README](../../tools/g3cli/README.md)).
- **Example:** [Meshing2DExample.cs](../examples/Meshing2DExample.cs)

---

## 2D Polygon Booleans & Offsets (`geometry3Sharp.Clipper2`)

- **Namespace & classes:** `g3.Clipper2.Clipper2Polygons`, `ClipperFrame`, `Clipper2Options`, `Clipper2Result` (optional package `addons/geometry3Sharp.Clipper2`, vendoring Clipper2 2.0.0 under Boost-1.0; core library stays dependency-free)
- **Input / mutability:** Read-only `GeneralPolygon2d`, `Polygon2d`, or `PolyLine2d` inputs; returns `Clipper2Result` with `Polygons` (CCW outer rings, CW holes, separate islands) or `PolyLines`.
- **Guarantees & limits:** Single-threaded and deterministic; never throws on bad input (`Status`: `Success`, `InvalidInput`, `InvalidParameter`, `Failed`). Each call maps inputs onto an auto-fitted integer grid (`ClipperFrame`, centered at bounds center, power-of-two scale with `PrecisionBits = 40`, about `1e-12 * extent` resolution—under 10 nm on a 10 km UTM footprint). Power-of-two input scaling produces bit-identical scaled results; features smaller than one grid spacing vanish. Pass `Clipper2Options.Frame` for a fixed grid (e.g., `Scale = 1000` for mm on meter coordinates).
- **Example:** [Clipper2Example.cs](../examples/Clipper2/Clipper2Example.cs)
- **Further reading:** [addons/geometry3Sharp.Clipper2/README.md](../../addons/geometry3Sharp.Clipper2/README.md)

```csharp
using g3;
using g3.Clipper2;

Clipper2Result merged = Clipper2Polygons.Union(footprints);
Clipper2Result zone = Clipper2Polygons.Offset(merged.Polygons, 3.0,
    new Clipper2Options { JoinType = JoinType.Miter, MiterLimit = 2 });
Clipper2Result free = Clipper2Polygons.Difference(new[] { parcel }, zone.Polygons);
if (!free.Ok)
    Console.WriteLine($"{free.Status}: {free.Error}");
```

### Supported Operations

| Method | Input & behavior | Output field |
|---|---|---|
| `Union`, `Intersection`, `Difference`, `Xor`, `Boolean` | Sets of `GeneralPolygon2d` (outer minus holes per polygon, then combined via `FillRule`: `NonZero` default or `EvenOdd`) | `Polygons` |
| `BooleanPaths` | Raw `Polygon2d` rings with orientation preserved (self-intersecting rings, fill rules) | `Polygons` |
| `ClipPolyLines` | Open `PolyLine2d`s clipped against polygons (`Intersection` = inside, `Difference` = outside) | `PolyLines` |
| `Offset` | Polygon inflation (`delta > 0`) / deflation (`delta < 0`); `JoinType` (`Round`, `Miter`, `Square`, `Bevel`), `ArcTolerance` | `Polygons` |
| `OffsetPolyLines` | Band around open/closed polylines (`delta >= 0`); `EndType` (`Round`, `Butt`, `Square`, `Joined`) | `Polygons` |
| `MinkowskiSum`, `MinkowskiDifference` | Pattern polygon around `(0, 0)` swept along a closed polygon or open path | `Polygons` |
| `Simplify`, `SimplifyPolyLines` | Removes vertices within `epsilon` of their neighbor chord; polygons are re-unioned | `Polygons` / `PolyLines` |

---

## Loop Nesting & 2D Convex Hull (Core Library)

- **Solid regions from arbitrary loops (`g3.PlanarComplex.FindSolidRegions`):** Groups outer rings and nested holes purely by containment regardless of input winding. Note that default simplification tolerance is `0.1` (absolute); set it smaller or `0` for closely spaced loops.
- **2D convex hull (`g3.ConvexHull2`):** Supports `QueryNumberType.QT_DOUBLE` (default, floating-point predicates) and `QT_INT64` (2^20 rounded grid). Exact rational mode (`QT_RATIONAL`) is not implemented. Coordinates are scaled by an exact power of two internally so small extents behave consistently.

---

## 3D Tetrahedral Meshing (`MeshTetrahedralization`)

- **Namespace & classes:** `gs.MeshTetrahedralization`, `gs.TetMeshMarkers`, `gs.TetMeshWriter`
- **Input / mutability:** Takes any triangle mesh (including soups, open or self-intersecting meshes); builds a valid volumetric tetrahedral mesh via alpha wrap, 3D Delaunay refinement, flips, and smoothing.
- **Guarantees & limits:** Exact validity check (`Valid`) on every tet. Reports target compliance (a measurement of the output, not a promise) (`Targets`) against `MinDihedral` (default `10°`) and `MaxRadiusEdge` (default `2.0`). Boundary triangles receive a marker (nearest input triangle's group ID if present, else triangle ID) and tets receive a connected-component region ID.
- **Example:** [TetMeshExample.cs](../examples/TetMeshExample.cs)
- **Further reading:** [TET_MESHING.md](../design/TET_MESHING.md), [TetMeshBenchmark README](../../benchmarks/TetMeshBenchmark/README.md)

### Limit: radius-edge next to the fixed boundary

The boundary is the alpha wrap's surface, and the refinement never splits a boundary triangle. Delaunay refinement reaches its radius-edge bound only if it may split boundary triangles that a bad tet encroaches (Shewchuk 1998; TetGen with a preserved boundary, `-Y`, makes no promise either). So `MaxRadiusEdgeRatio` (default `2`) is **not reached next to the boundary** in general: tets there whose circumcentre lies beyond the surface stay (`Counts["unrefined_bad_tets"]`, `Targets.TetsAboveRadiusEdge`, `Targets.RadiusEdgeMet`; all within half an alpha of the surface). Measured with the defaults: 0 on a sphere, 24-65 of 3.5k-11k tets on a torus, a box with a hole and a twisted bar. The smallest dihedral angle is not affected (stage 2 works on it; it has no radius-edge objective).

It gets much worse when the interior size (`TargetEdgeLength`, `SizeFunction`) is finer than the wrap's surface spacing: the interior tets shrink, the boundary triangles do not (sphere at `h` = diagonal / 40: 419 of 76k tets; at / 60: 3729 of 255k). Remedies: a finer wrap (`RelativeAlpha`), or the opt-in `BoundaryFollowsSize` below. Details and measurements: [TET_RADIUS_EDGE_AT_SURFACE_2026-11.md](../research/TET_RADIUS_EDGE_AT_SURFACE_2026-11.md).

### Opt-In Controls

- **Interior size control:** `TargetEdgeLength`, `SizeFunction`, and `GradingFactor` refine interior tets independently of the boundary wrap spacing (`RelativeAlpha`); sizes below `alpha / 4` are clamped (`steps.size_clamped_tets`).
- **Boundary follows the size (`BoundaryFollowsSize`, off by default):** With `TargetEdgeLength` or `SizeFunction`, the wrap is made once more with a local alpha of `0.6 * h` at the input surface (graded, between `max(alpha / 4, 3 * offset)` and `alpha`; with the wrap's surface angle refinement, and the half-gradation retry of `Compute()` if the dihedral target is missed), so the fixed boundary is not coarser than the interior next to it. Removes most tets above the radius-edge bound at the boundary (sphere at `h` = diagonal / 40: 419 to 2; four shapes at / 60: 13193 to 113), at 1.1-2.2x the time and 1.03-1.14x the tets. Sizes below about `max(alpha / 4, 3 * offset) / 0.6` are not followed; `Counts["boundary_size_fine_samples"]`, `Counts["boundary_size_wrap_failed"]`.
- **Surface fidelity (`MaxSurfaceDeviation`, `LocalAlphaPasses`):** Locally refines wrap `Alpha` down to `alpha / 4` (graded with slope `0.25`) where the wrap deviates from the input by more than `MaxSurfaceDeviation` (raised to at least `2 * offset`), capturing concave detail and narrow gaps with roughly half the tets of a uniform finer alpha. Requires triangle inputs; `LocalAlphaPasses` accepts `1..3` (default `1`).
- **Improvement work cap (`MaxImproveVisitsPerVertex`):** Caps bad-tet improvement visits per stage-1 vertex deterministically (counted, not timed) to bound worst-case runtime (`Counts["improve_work_cap_reached"]`).
- **Export formats (`TetMeshWriter`):** Writes Medit `.mesh`, Gmsh `.msh` (4.1 or 2.2), and legacy ASCII VTK `.vtk` (writers only; no readers). Refs store `marker + 1` and `region + 1` (`0` = unmarked).
