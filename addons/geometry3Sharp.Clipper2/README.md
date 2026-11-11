# geometry3Sharp.Clipper2

Robust 2D polygon booleans, offsets, Minkowski sums and simplification for the polygon types of
[geometry3Sharp](https://github.com/HarmenGrosseDeters/geometry3Sharp) (`Polygon2d`, `GeneralPolygon2d` with
holes, `PolyLine2d`), on top of [Clipper2](https://github.com/AngusJohnson/Clipper2) by Angus Johnson
(Boost Software License 1.0). Typical uses: terrain and GIS footprints, offsetting break lines and slope
boundaries, slicing contours.

```csharp
using g3; using g3.Clipper2; using Clipper2Lib;

Clipper2Result merged = Clipper2Polygons.Union(footprints);          // also Intersection, Difference, Xor
Clipper2Result zone = Clipper2Polygons.Offset(merged.Polygons, 3.0,
    new Clipper2Options { JoinType = JoinType.Miter });
Clipper2Result free = Clipper2Polygons.Difference(new[] { parcel }, zone.Polygons);
if (!free.Ok)
    Console.WriteLine(free.Status + ": " + free.Error);
```

- Results: `Clipper2Result` with `Status` (`Success`, `InvalidInput`, `InvalidParameter`, `Failed`), `Error`,
  `Polygons` (outer rings counterclockwise, holes clockwise, islands in holes as separate polygons), `PolyLines`
  (clipped or simplified polylines) and the `Frame` used. Nothing throws on bad input.
- Precision: each call maps its inputs to an integer grid fitted to their bounds (`ClipperFrame`; origin at the
  bounds center, power-of-two scale, 2^40 grid units across the half extent plus the offset margin by default).
  Vertices are snapped or rounded to that grid (spacing about 1e-12 times the extent). Power-of-two
  scaling of the input gives the same result bit for bit; UTM coordinates work. `Clipper2Options.Frame` sets a fixed
  grid instead.
- Known limitation (Clipper2 2.0.0 itself): a Union of self-intersecting rings with exact degeneracies (a vertex on another edge, a collinear
  overlap) can return a wrong area, about 1 in 3000 random pairs of such rings on a coarse coordinate grid (1 to 2 failures in 3000 pairs); no failure was
  seen in 6000 pairs of 16-gons with generic coordinates (steps of 1/1024 and finer), and the grid precision (`PrecisionBits`) makes no difference. A repro is in
  [tools/MeshFuzzPilot/FINDINGS.md](https://github.com/HarmenGrosseDeters/geometry3Sharp/blob/master/tools/MeshFuzzPilot/FINDINGS.md) (item 5).
- Upstream status: the Clipper2 repository (AngusJohnson/Clipper2) was archived on 2026-10-28. Clipper2 continues as a
  C++ only library at [PolyClipping/Clipper2](https://github.com/PolyClipping/Clipper2) (maintainer Seth Hillibrand);
  translations to other languages are left to third parties, so the C# package 2.0.0 this adapter uses is frozen and the
  limitation above will not be fixed upstream. Whether to integrate the C# sources into this package is an open roadmap item.
- The full description is in [docs/OPERATIONS.md](https://github.com/HarmenGrosseDeters/geometry3Sharp/blob/master/docs/OPERATIONS.md#2d-polygon-booleans-and-offsets-optional-package).
