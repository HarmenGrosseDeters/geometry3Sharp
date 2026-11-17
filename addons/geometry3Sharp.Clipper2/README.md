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

## Grid resolution and intersection precision (#955)

The #949 campaign uses `Random(17)`, 500 pairs, alternating ranges −2…2 and −4…4,
`3 + Next(22)` vertices per ring, all four operations and fill rules. Pair indices are zero-based.
The remaining 82 failures have different causes:

| Case class | Count | Verdict | Evidence / regression |
|---|---:|---|---|
| Rotated UTM, empty regions | 27 | Expected grid rounding; oracle allowance defect | Subgrid slivers disappear when intersections are rounded. Scaling the same integer inputs by 65536 recovers reference area within 0.5%. |
| Rotated UTM, pair 399, Union Negative | 1 | Resolution-dependent engine error under the UTM clamp | Raw engine and adapter area are 23,171,051,747,518 grid squares; error 28,220,407,073.5625. At x16 integer resolution relative error falls to about 6e-9. |
| Rotated z, empty regions | 35 | Expected grid rounding; oracle allowance defect | Same x65536 recovery, within 0.5% of the exact snapped-input reference. |
| Rotated z, pair 268 | 4 | Precision-dependent engine arithmetic at the 40-bit adapter default | Difference/Xor × EvenOdd/NonZero produce a zero-signed-area ring at 40 bits; the same inputs are well-formed at PrecisionBits=30. |
| Scale 1e-3, empty regions | 11 | Expected grid rounding; oracle allowance defect | Same x65536 recovery. |
| Scale 1e-3, pair 296 | 4 | Precision-dependent engine arithmetic at the 40-bit adapter default | Same four combinations; well-formed at 30 bits. |

The exact area reference is correct for the snapped integer inputs. The 73 empty results are expected fixed-grid
rounding, not an intrinsic engine limit. `Clipper2DegenerateUnionTests.Check` previously allowed only the returned
perimeter plus one grid square: for an empty result this becomes one, ignoring the vanished boundary. The oracle
now adds the L1 input perimeter only when the output is empty. Nonempty results retain the original output-perimeter
allowance, including conversion rounding; the coarse-grid checks likewise use only the returned boundary.
Negative controls reject a completely dropped 9x9 region (area 81, allowance 73), and a lost 64x64 component
beside a retained 1000x1000 square (loss 4096, allowance 4001). This area allowance is not a topology certificate; the tests
separately require recovery of the lost regions at x65536, and still reject pair 399's much larger error.

At x1024 all 73 lost regions return, but exact BigInteger output-area sums give up to 6.24% relative error
(UTM pair 137: reference 62.0276790256776, recovered 65.89564275741577). The tests bound this by 7%,
then require at most 0.5% at x65536. Thus the smaller scale is not claimed to recover every reference to 1e-3.
At x65536 coordinates reach about 2^55 and double intersection arithmetic is inexact: recovery is empirical
evidence, not the exact reference (which remains the independent rational-slab oracle).
These integer-only refinements diagnose rounding; they do not bypass the adapter's world-coordinate clamp.

The eight orientation failures depend on the adapter's grid choice. At the 40-bit default the integers are about
2^39. In `Clipper.Core.cs`, `GetLineIntersectPt` computes the determinant and intersection parameter in double;
products can reach about 2^80. Products of integer differences need not be exact once they exceed 2^53 (roughly
26 bits per difference). This is not a magnitude-only failure threshold: conditioning and degeneracies matter,
and even 30-bit calculations need not be exact. On these cases the 30-bit grid removes the zero-area artefacts;
it is not a universal robustness guarantee. The reduced examples and all eight original rows test this explicitly.
The raw integer rings already have zero signed area before conversion back to doubles, with nonempty opposite-winding
lobes; reversing them cannot establish the usual outer-counterclockwise / hole-clockwise property.

The default remains 40 bits. Coarsening trades these artefacts for additional geometric loss: the procedural
`CoarserGridLosesAResolvableThinComponent` test preserves a component of width 2^-35 beside a unit square at the
default, but loses it at 30 bits. Changing the default or imposing a cap is an output-policy decision for the owner;
this PR does neither. Use `PrecisionBits` to make that tradeoff explicitly for the application.

Pair 399 is also resolution-dependent, not an unavoidable property of every integer clipper. Its error exceeds
100 times the input perimeter, while the same integers scaled by 16 recover the reference within 0.0001% (the original
pair's measured relative error is about 6e-9). The UTM frame is clamped by #949 to world-coordinate double spacing:
a finer default grid would sacrifice its exact grid-to-world round-trip guarantee. Thus the error remains with
this engine and the current clamp policy. The reduced counterexample fits within ±5,002,400 integer units, so a
large-coordinate-only explanation is insufficient for this separate case.

[`Clipper2PrecisionTests`](../../tests/geometry3Sharp.Clipper2.Tests/Clipper2PrecisionTests.cs) supplies procedural
reductions (6+6, 3+5, 4+3 and 6+3 vertices), pins integer inputs, prints raw engine output, and compares complete
engine paths with mapped-back adapter paths. It checks each input's snap round-trip error is at most Resolution/2
per coordinate. Reductions run over `Similarity.All2D`, check 30-bit well-formedness or area recovery as appropriate,
and compare polygon output bit for bit at power-of-two scales. All 82 original rows are replayed individually in
`CampaignPrecisionBehaviour`. The frame, default precision, engine sources, #933 port and decision not to port
`4a3bf64` remain unchanged.

The upstream [robustness documentation](https://angusj.com/clipper2/Docs/Robustness.htm),
[FAQ](https://angusj.com/clipper2/Docs/FAQ.htm), and earlier
[rounding notes](https://angusj.com/delphi/clipper/documentation/Docs/Overview/Rounding.htm) describe removal of
triangles with an edge shorter than two integer units and rounding-induced self-intersections. These are consistent
with the narrow-feature examples here; the recovery and precision tests distinguish grid rounding from resolution-dependent engine errors. No external implementation was ported.

Reproduce the small cases and print the exact integer inputs and outputs:

```sh
dotnet test tests/geometry3Sharp.Clipper2.Tests -c Release --filter FullyQualifiedName~Clipper2PrecisionTests --logger 'console;verbosity=detailed'
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
