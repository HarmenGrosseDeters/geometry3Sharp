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
  Vertices are snapped or rounded to that grid (spacing about 1e-12 times the extent). The grid is never finer than the
  doubles of the coordinates: with half extent plus margin at most 2^e and the larger |coordinate| of the bounds center
  plus half extent plus margin below 2^E, the effective resolution is max(2^(e - PrecisionBits), 2^(E - 53)), so about max(extent * 2^-40,
  |coordinate| * 2^-53) by default. The second term acts only beyond about 8192 times the extent from zero (a 2 m design
  at UTM coordinates: 2^-30 m, about 1 nm, instead of 2^-40 m); there the result vertices are grid points exactly. The
  documented tolerance (half the resolution per coordinate, plus the rounding of the bounds center) holds with this
  resolution. Power-of-two scaling of the input gives the same result bit for bit; UTM coordinates work.
  `Clipper2Options.Frame` sets a fixed grid instead.
- Degenerate inputs: Clipper2 2.0.0 could fill a hole of the result that touches the outer ring at a vertex (a Union of
  self-intersecting rings with a vertex exactly on another edge: about 1 in 150 random pairs of 16-gons on a coarse lattice). This
  package fixes it in its copy of the sources (FINDINGS item 5,
  [tools/MeshFuzzPilot/FINDINGS.md](https://github.com/HarmenGrosseDeters/geometry3Sharp/blob/master/tools/MeshFuzzPilot/FINDINGS.md)).
  Through this package, 1000 random pairs of 16-gons on that lattice in every operation and fill rule gave 43 wrong areas
  before the fix and 0 after. Two more fixes in the same output cleanup (#756, 2026-11-15) keep a twist removal from
  flipping a ring or cutting a triangle off it, and the conversion of the result tree now nests holes that touch their outer
  ring or another hole (a vertex rounded to the grid point beside the edge): through this package, 10 000 random pairs of
  16- and 24-gons on such lattices in every operation and fill rule gave 41 wrong areas of 160 000 before and 0 after.
  Since 2026-11-15 the copy also carries three fixes of the C++ Clipper2 (PolyClipping/Clipper2, rounded intersection
  points among them; Clipper2Lib/README.md), which change most results within the rounding; on that campaign: 0 wrong
  and 0 structurally broken results. A fourth fix of the C++ code (`6c7d083`) makes `OffsetPolyLines` with
  `EndType.Joined` independent of the order of the lines (a 2-point line before a longer one gave the longer one open
  ends). Far from the origin (UTM coordinates with a small scale) slivers could come out with zero or negative area
  after the rounding back to doubles: a millimetre design rotated at UTM coordinates gave 2824 of 8000 results (pairs of
  3- to 24-gons on a lattice, every operation and fill rule) with an outer ring not counterclockwise or a hole not
  clockwise. Since the frame keeps its grid no finer than the doubles (above, 2026-11-15): 0 of them. The remaining cases are diagnosed below (#955).
- API checks: the package has no `PackageValidationBaselineVersion` yet (owner 2026-11-13: the first published version of
  this package becomes the baseline). Until a version is published, a PR that changes the public API lists the breaks from a
  local strict validation against master's pack (as #851 did for 2.0).
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

## Near-degenerate integer input (#955)

The vendored engine has limits even on correctly converted integer inputs. This is a limit of this engine,
not a claim that every integer clipping algorithm must have the same behaviour. It affects nearly coincident or
collinear boundaries and intersections separated by about one integer grid unit. It is not restricted to large
coordinates: the reduced UTM counterexample uses integer coordinates within ±5,002,400. The orientation examples
use coordinates within ±683,591,589,077 (rotation at the origin) and ±562,949,953,421 (scale 1e-3).
The physical range is these integers divided by `Frame.Scale`, relative to `Frame.Origin`. No magnitude-only
threshold makes arbitrarily thin features safe. Increasing precision can change these degeneracies rather than
preserve their topology.

The exact #949 campaign uses `Random(17)`, 500 pairs, alternating ranges −2…2 and −4…4, `3 + Next(22)` vertices
per ring, all four operations and fill rules. Pair indices below are zero-based. There are three distinct outcomes:

| Case class | Count | Verdict | Evidence / pinned behaviour |
|---|---:|---|---|
| Rotated UTM, empty subgrid regions | 27 | Engine limit; bounded area loss | The exact snapped input has a positive region but the integer engine returns no region. An empty output has zero perimeter, so the old output-perimeter area allowance collapses to 1 square grid unit and omits the rounding budget of the vanished boundary. |
| Rotated UTM, pair 399, Union Negative | 1 | Engine limit | Raw engine area and adapter area are both 23,171,051,747,518 square grid units; error against the independent rational-slab reference is 28,220,407,073.5625, exceeding allowance 125,778,021.32898551. The result is wrong, not the reference. |
| Rotated z at the origin, empty subgrid regions | 35 | Engine limit; bounded area loss | Same empty-result area check. |
| Rotated z at the origin, pair 268 | 4 | Engine limit | Difference and Xor, EvenOdd and NonZero: the raw integer tree includes a non-collinear path with exactly zero signed area, with opposite-winding filled lobes. The engine's floating area reports −0.5. |
| Scale 1e-3, empty subgrid regions | 11 | Engine limit; bounded area loss | Same empty-result area check. |
| Scale 1e-3, pair 296 | 4 | Engine limit | Same four combinations: a raw engine path has zero signed area and nonzero filled area. |

An exact reference area need not equal the area after integer intersections are rounded. In particular, a vanished
sliver must retain a boundary-rounding allowance; using only the returned perimeter is not a valid bound for it.
The empty-region losses are smaller than the input perimeter in grid units; this is an area comparison, not a proof of correct topology. The exact reference value is correct for the snapped input, and the empty engine output loses that region. Pair 399 is different: it is a
nonempty region with an error well beyond that perimeter scale. The global area oracle has not been relaxed.

The eight structural cases also precede conversion back to doubles: the exact integer signed area is already zero.
The adapter keeps non-collinear zero-signed-area paths (a figure eight can have nonzero filled area); reversing such
a path cannot give it positive signed area. Consequently the usual outer-counterclockwise / hole-clockwise promise
has an exception for these rounding artefacts, and a `Success` result is not a topology certificate for subgrid
features. Dropping every zero-signed-area path would also drop genuine opposite-winding lobes, so it is not a fix.

[`Clipper2FrameClampLimitTests`](../../tests/geometry3Sharp.Clipper2.Tests/Clipper2FrameClampLimitTests.cs) supplies procedural reductions (6+6, 3+5, 4+3 and 6+3 vertices), pins the integer inputs,
executes `Clipper64` directly, and prints its output. It checks conversion separately, compares raw integer and adapter
areas, and runs the reductions over `Similarity.All2D`, with bit-for-bit polygon comparisons at powers of two.
The frame follows the resolution formula above; the clamped UTM output round-trips exactly. All 82 reported failures have individual procedural replay rows in `CampaignFailure_IsAlreadyInIntegerEngine`; each row prints its integer input and output. The engine sources,
#933's rounding port, and the decision not to port `4a3bf64` are unchanged.

The upstream [robustness documentation](https://angusj.com/clipper2/Docs/Robustness.htm),
[FAQ](https://angusj.com/clipper2/Docs/FAQ.htm), and earlier
[rounding notes](https://angusj.com/delphi/clipper/documentation/Docs/Overview/Rounding.htm) describe removal of
triangles with an edge shorter than two integer units and rounding-induced self-intersections. These are consistent
with the narrow-feature examples here; the numerical counterexamples above establish the limits of this vendored
version independently of those general descriptions. No external implementation was ported.

Reproduce the small cases and print the exact integer inputs and outputs:

```sh
dotnet test tests/geometry3Sharp.Clipper2.Tests -c Release --filter FullyQualifiedName~Clipper2FrameClampLimitTests --logger 'console;verbosity=detailed'
```

For the reduced nonempty error, Union / Negative receives exactly these integers (no frame arithmetic in the
engine). Its exact reference area is 14,267,462,008,455.473 grid squares; the returned path has area
12,523,546,880,661. A separate regular-grid winding integration checks the reference and detects missing coverage.

```text
subject: (-615145,4163310) (-223944,-5002400) (764441,-4582855)
         (-1752825,1347452) (1361624,5002399) (1752825,-4163311)
clip:    (373240,4582854) (344896,-3594471) (-764441,1766996)
output:  (1361624,5002399) (370663,3839462) (373240,4582854)
         (-195600,3174926) (-299416,3053094) (-276863,2973792)
         (-536905,2330168) (-554960,2753201) (-1752825,1347452)
         (-354344,-1947162) (-447487,235147) (359902,734701)
         (1752825,-4163311)
```

The reduced empty case, Intersection / Positive, has exact reference area 142,209.91316774074 grid squares:

```text
subject: (-508364,-2321666) (-2186542,1631872) (-1766998,643488) (2186541,2321665)
clip:    (629316,494192) (-2186542,1631872) (-1766998,643488)
output:  empty
```

The tests print the full raw output for both orientation reductions too. The zero-signed-area path at the origin
has filled (nonzero-winding) area 70,786,294,014.5 grid squares despite signed area zero; the millimetre example's
filled area is 1. Thus signed area alone cannot justify discarding either path.
