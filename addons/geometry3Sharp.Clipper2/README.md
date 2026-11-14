# geometry3Sharp.Clipper2

Robust 2D polygon booleans, offsets, Minkowski sums and simplification for the polygon types of
[geometry3Sharp](https://github.com/HarmenGrosseDeters/geometry3Sharp) (`Polygon2d`, `GeneralPolygon2d` with
holes, `PolyLine2d`), on top of [Clipper2](https://github.com/AngusJohnson/Clipper2) by Angus Johnson
(Boost Software License 1.0). Typical uses: terrain and GIS footprints, offsetting break lines and slope
boundaries, slicing contours.

```csharp
using g3; using g3.Clipper2;

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
- Degenerate inputs: Clipper2 2.0.0 could fill a hole of the result that touches the outer ring at a vertex (a Union of
  self-intersecting rings with a vertex exactly on another edge: about 1 in 150 random pairs of 16-gons on a coarse lattice). This
  package fixes it in its copy of the sources (FINDINGS item 5,
  [tools/MeshFuzzPilot/FINDINGS.md](https://github.com/HarmenGrosseDeters/geometry3Sharp/blob/master/tools/MeshFuzzPilot/FINDINGS.md)).
  Through this package, 1000 random pairs of 16-gons on that lattice in every operation and fill rule gave 43 wrong areas
  before the fix and 0 after. Still open (rare, listed there and in issue #756): an Intersection case in the same cleanup and
  a Union case on the default grid, both wrong before and after.
- Upstream status: the Clipper2 repository (AngusJohnson/Clipper2) was archived on 2026-10-28. Clipper2 continues as a
  C++ only library at [PolyClipping/Clipper2](https://github.com/PolyClipping/Clipper2) (maintainer Seth Hillibrand);
  translations to other languages are left to third parties, so the C# package 2.0.0 this adapter uses is frozen and the
  limitation above will not be fixed upstream. This package therefore carries the C# sources of Clipper2 2.0.0 itself
  (internal, `Clipper2Lib/`, owner decision 2026-11-12); they compute what the package computes
  (`Clipper2VendoredDifferentialTests`), apart from the fix above. Since 2.0 this package does not depend on the
  NuGet package `Clipper2` (below).
- **Breaking change in 2.0** (owner decision 2026-11-13: no dependency on the frozen Clipper2 package):
  - `ClipType`, `FillRule`, `JoinType` and `EndType` are types of this package (namespace `g3.Clipper2`), with the
    names, members and values of Clipper2's. They replace `Clipper2Lib`'s in `Clipper2Options.FillRule`, `JoinType`,
    `EndType` and in `Clipper2Polygons.Boolean`, `BooleanPaths` and `ClipPolyLines`. Code that used them with
    `using Clipper2Lib;` compiles once that line is removed (and is recompiled: the types changed); fully qualified
    uses such as `Clipper2Lib.JoinType.Miter` become `JoinType.Miter` (or `g3.Clipper2.JoinType.Miter`).
  - The package no longer brings the Clipper2 package along: code that uses Clipper2 itself (`Clipper2Lib`) needs its
    own `PackageReference` to `Clipper2`.
  - Code that also uses the Clipper2 package directly and imports both namespaces gets CS0104 ("ambiguous
    reference"). Name the type with an alias, eg `using FillRule = g3.Clipper2.FillRule;`, or qualify it.
  - `ClipperFrame`'s conversions to and from Clipper2's integer types are no longer public: `ToPoint64`,
    `ToPoint64Displacement`, `ToVector2d(Point64)`, `ToPath64` (two), `ToPaths64` (two), `ToPolygon2d(Path64)`,
    `ToPolyLine2d(Path64)`, `ToGeneralPolygons(PolyTree64)`. `Origin`, `Scale`, `Resolution`, the constructor,
    `ForBounds` and the constants stay, so a fixed grid can still be set with `Clipper2Options.Frame`.
- The full description is in [docs/OPERATIONS.md](https://github.com/HarmenGrosseDeters/geometry3Sharp/blob/master/docs/OPERATIONS.md#2d-polygon-booleans-and-offsets-optional-package).
