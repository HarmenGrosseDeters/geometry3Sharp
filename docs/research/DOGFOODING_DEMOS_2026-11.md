# Dogfooding with browser demos (2026-11)

Back to [Research index](README.md)

Owner decision 2026-11-25 (consolidation phase): build interactive browser demos of the library as dogfooding, to find out
how good the quality is when the library is used the way a customer uses it. The demos are the means; this list is the
deliverable. Stage 1: the skeleton and three demos (exact Boolean, robust repair, remeshing) in
[demos/G3Demos](../../demos/G3Demos/README.md), a standalone Blazor WebAssembly app on the library's `netstandard2.0`
build, drawn with three.js.

Method: every demo shows the checks a customer would run on each result (closed, manifold, finite,
`MeshRobustRepair.NotCleanReasons`, the operation's own report, volume against a known volume, times). The 127 cases of
[e2e/cases.json](../../demos/G3Demos/e2e/cases.json) (each knob alone and in combination) ran through the demo code
(`Probe.RunCase`, the code path of the UI) natively on the `netstandard2.0` and the `net10.0` build, and the 121 of them
that take less than 10 s natively also in headless Chromium, interpreted and AOT-compiled. Each line carries a hash of
the input and of the result, so a platform difference of the input is told apart from one of the operation. The
findings were not fixed here; the coordinator files and routes them.

Severity: **high** = a wrong or silently bad result reported as success; **medium** = a refusal or report that misleads,
a performance cliff, or friction that costs a customer real time; **low** = documentation and polish.

## Summary

| # | Severity | Area | Finding |
|---|---|---|---|
| 1 | high | `MeshRobustRepair` | A closed welded solid with some triangles flipped comes back clean with 10 % (flip every 3rd) or 0.05 % (every 2nd) of its volume; `Apply()` true, `ResultReason` empty. `MeshAutoRepair` alone repairs it exactly. |
| 2 | high | `MeshRobustRepair` | A soup with flipped triangles and holes ends in a hollow alpha wrap (1.5 % of the volume, 4x the area), `Apply()` true, empty reasons; 30-50 s on 1k-2k triangles (1M-triangle raw wrap). |
| 3 | medium | `MeshRobustRepair` | Soups with flipped triangles end in `Solidify` with 25-50 % of the volume (flip every 4th, with or without holes), or in a slow alpha wrap (4-8 s for 1k-1.6k triangles); reasons stay empty. |
| 4 | medium | `SolidBooleanPipeline` | `Status == Success` with `Report.Clean == false` in normal cases (empty intersection of touching boxes; sphere touching a box). Neither the status nor the report tells a customer whether that is fine. |
| 5 | medium | `ExactMeshBoolean`, pipeline | An intersection of solids that overlap by 1e-12 returns an empty mesh as clean success; the thin slab (about 9000 ulps thick) is only listed in `Rounding.RemovedComponentInfos`. |
| 6 | medium | WebAssembly | Same input bits, other result: the `Solidify` path of `MeshRobustRepair` gives another mesh in the browser (interpreted and AOT agree with each other, not with native). Native `netstandard2.0` and `net10.0` agree bit for bit on all 121 cases. |
| 7 | medium | WebAssembly | Interpreted WebAssembly (the Blazor default) is 9.5x slower than native at the median, up to 44x; repair (exact code) 20x, remesh 13x, Boolean 6.6x. AOT: 1.9x at the median, up to 7.6x, at 2.3x the download (39 MB against 17 MB). |
| 8 | medium | `Remesher` | With `PreventNormalFlips` alone, a remesh stopped before it converges (coarse box, 1 % target, 10 passes) has 807 inverted triangles and self-intersections, and nothing in the remesher reports it. |
| 9 | medium | `Remesher` | `MinTriangleQuality = 0.2` (safe preset) is not a floor: 11013 triangles below quality 0.1, minimum 1.3e-5 (same case); documented, but the name reads as a guarantee. |
| 10 | medium | `Remesher` | The safe preset costs 6-17x (25 s natively for a 49k-triangle box, 30 passes); in the browser a single remesh of a 12-triangle box at 4 % takes 9 s. |
| 11 | low | `SolidBooleanPipeline` | Near-coplanar faces (tilt 1e-9 to 1e-15 rad) leave a clean result with a sliver wedge of the exact solid, thinner than 1e-9 (area +0.18 of 6.36); default `MinThickness = 0` keeps it. |
| 12 | low | docs | The remeshing guide's first snippet does not compile (`PreserveBoundaryAndFeatures(remesher, mesh, creaseAngleDeg: 40)`, `PinSharpVertices(remesher, mesh, minAngleDeficitDeg: 75)`). |
| 13 | low | API | No one-call volume: `MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x`. |
| 14 | low | build | The library project compiles every `*.cs` below the repository root; a demo or sample folder needs an exclusion in `geometry3Sharp_netstandard.csproj` (a gate file). |
| 15 | low | XML docs | The `netstandard2.0` build warns CS1570 (badly formed XML) in `mesh_ops/ExactMeshBoolean.cs` (summary near line 3496) and `implicit/IntervalImplicit.cs:93`: IDE tooltips of those members break. |
| 16 | low | WebAssembly | `Math.Sin` / `Math.Cos` give other bits in the browser: a procedural torus built with them differs from the native one (16 of 121 inputs). Not the library's, but every customer who builds inputs this way sees "different results" across platforms. |

What held: the exact Boolean passed the other 59 of its 65 cases in both engines, interpreted and AOT, and the `2^k` invariance
(2^-20, 2^20, 2^30, bit for bit), inclusion-exclusion (residual at most 4e-14 without reduction, 7e-10 at a 1e-6 rad tilt), the analytic box volumes
(relative error at most 4e-8, on a 3.6e-10 slab; except finding 5), tangent gaps down to 1e-12 and near-coplanar tilts down to 1e-15 rad.
`MeshRobustRepair` handled holes, duplicates, an overlapping second shell, crossing fins and jitter alone exactly or within
0.06 % of the volume. The remesher with the safe preset left no inverted triangle and no self-intersection in any case.

## 1. `MeshRobustRepair` loses most of a solid with flipped triangles (high)

```csharp
DMesh3 box = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.4, 0.3)), EdgeVertices = 10 }.Generate().MakeDMesh();
int k = 0;
foreach (int tid in box.TriangleIndices().ToList())
    if (++k % 3 == 0) box.ReverseTriOrientation(tid);
// closed, welded; NotCleanReasons: "inconsistent orientation"
var robust = new MeshRobustRepair(box);
bool ok = robust.Apply();
```

- **Expected:** volume 0.48 (the box), or a refusal. `new MeshAutoRepair(new DMesh3(box)) { PostRepairCleanup = true }`
  alone gives exactly 0.48 and a clean mesh.
- **Actual:** `ok == true`, `ResultPath == Solidify`, `ResultReason == ""`, `NotCleanReason(robust.Mesh) == ""`, volume
  0.0477 (9.9 %), 62750 triangles. `FallbackReason == "volume differs from the input"`, `RepairVolumeIoU == 0.10`. Every
  2nd triangle flipped: volume 0.00024 (0.05 %); every 4th: 0.25 (52 %). Same on the sphere and, through the alpha wrap,
  less so on the torus.
- **Diagnosis (from the report fields):** the repair stage orients the box correctly, but its result is then compared
  with the volume of the input, which the flipped triangles have made meaningless (0.16 here), and rejected; the later
  stages take the winding number of the flipped input as the truth. The low `RepairVolumeIoU` of the accepted result
  (0.10) does not turn into a reason or a `false`.
- **Where:** `mesh_ops/MeshRobustRepair.cs` (the volume check of the repair stage, the acceptance of `Solidify`).

## 2. Flipped soup with holes and overlaps: a hollow wrap after 30-50 s (high)

```csharp
// demos/G3Demos/Demos/RepairDemo.cs
DMesh3 soup = RepairDemo.MakeSoup(new RepairParams { Shape = "box", FlipEvery = 4, Holes = 8, JitterExp = 7,
    DuplicatePercent = 10, OverlappingShell = true, Fins = 3 }, out _, out double known);   // 2132 triangles, known 0.747
var robust = new MeshRobustRepair(soup);
bool ok = robust.Apply();
```

- **Expected:** a solid near 0.747, or a refusal with a reason.
- **Actual:** `ok == true`, `ResultPath == AlphaWrap`, `ResultReason == WrapReason == ""`, clean, volume 0.0109 (1.5 %),
  area 15.1 (about 4x): the wrap went in through the holes and wraps the surface as a thin shell (the limit noted for
  issue #1542 in the repair guide). 50.1 s natively (wrap 26.5 s, its simplification 19.2 s, a raw wrap of 1011816
  triangles at alpha 0.0066 for a 2132-triangle input). Without the jitter the same (46.5 s); without the holes 9.9 s and
  the right volume; without the flips 0.9 s and the right volume. The same on the sphere (35 s) and the torus (31 s).
- **Severity:** high for the silent result, and above the 20 s rule for one operation on one input (CLAUDE.md). In
  the browser this is minutes (section 7).
- **Ask:** a volume or IoU check of the wrap against the input's solid (the hollow shell has 1.5 % of it), and a size of
  the raw wrap that follows the input, not only `WrapRelativeAlpha`.

## 3. Soups with flipped triangles (medium)

`RepairParams { Shape = "box", FlipEvery = 4, Holes = 8 }` (unwelded, 964 triangles): `Solidify`, volume 0.224 of 0.48,
`FallbackReason == "degenerate triangles"`, `RepairVolumeIoU == 0.05`, `Apply()` true; welded the same volume. Sphere:
0.28 of 0.52. Torus with every 2nd triangle flipped: alpha wrap, volume right within 0.66 %, 3.6 s for 1600 triangles. An
unwelded soup has no orientation to restore from its edges, so this may be the limit of the method; then a customer
needs the refusal or the low IoU as a reason, not a clean success.

## 4. `SolidBooleanPipeline`: `Success` with `Report.Clean == false` (medium)

```csharp
DMesh3 a = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5)), EdgeVertices = 6 }.Generate().MakeDMesh();
DMesh3 b = new GridBox3Generator() { Box = new Box3d(new Vector3d(0.85, 0.1, 0.2), new Vector3d(0.35, 0.3, 0.3)), EdgeVertices = 6 }.Generate().MakeDMesh();
var p = new SolidBooleanPipeline(a, b, ExactMeshBoolean.Operations.Intersection);   // the boxes touch at x = 0.5
p.Compute();   // true, Status Success, Result has 0 triangles, Report.NotCleanReason == "empty"
```

A sphere touching the box at one vertex (`ShapeB = "sphere"`, `Overlap = 0`, `Resolution = 7`), Union: `Success`,
`Report.NotCleanReason == "self-intersections"` (the contact). Both are documented (`RoundingClean` exempts contacts;
`NotCleanReason` counts them), and both results are right. The friction: a customer's natural check is `Compute() &&
Report.Clean`, and it fails on a correct empty result and on a correct touching result, with nothing that tells these
apart from a broken one. Ask: say in the report that the result is empty by the exact boolean, and that the only defects
are contacts of the exact solid (`OnlyContacts` exists; surface it next to `Clean` in the guide's first snippet).

## 5. A thin real intersection disappears (medium)

Boxes overlapping by 1e-12 along x (`Overlap = 0, GapSign = -1, GapExp = 12`), Intersection: the exact result is a slab
0.6 x 0.6 x 1e-12 (about 9000 ulps of the coordinate 0.5); both engines return an empty mesh as success, the slab only in
`Rounding.RemovedComponents == 1`. At 1e-9 the slab is kept (volume within 2e-8). This follows the documented rule (a
removal thinner than 1e-3 of the rounding bound leaves the result clean), but the empty result is then reported exactly
like finding 4's correct empty result. Ask: a report field that says "the exact result was not empty".

## 6 and 7. WebAssembly: results and speed

121 cases (those under 10 s natively), the same code, four runtimes on one machine (4 cores):

| Runtime | Inputs as native | Results as native | Time / native, median (max) | Boolean | Repair | Remesh |
|---|---|---|---|---|---|---|
| native `netstandard2.0` | reference | reference | 1 | | | |
| native `net10.0` | 121 | 121 | 0.97 (1.6) | 0.89 | 1.01 | 1.10 |
| browser, interpreted (Blazor default) | 105 | 103 of 105 | 9.5 (44) | 6.6 | 20 | 13 |
| browser, AOT (`RunAOTCompilation`) | 105 | 103 of 105 | 1.9 (7.6) | 1.4 | 4.0 | 3.3 |

- **Results:** the 16 other inputs are the torus cases (finding 16). Of the identical inputs, two results differ, both on
  the `Solidify` path of `MeshRobustRepair` (`repair/sphere/soup-jitter1e-3`: 19542 against 19558 triangles, volume
  0.516805 against 0.516804; `repair/box/welded-flip3`). Interpreted and AOT agree with each other. Natively the result
  does not change with 1, 2 or 4 processors. Likely cause: `DMeshAABBTree3.FastWindingNumber` uses `Math.Atan2` (and a
  vector kernel on .NET 8+), which the determinism proof lists as an assumption of layer L1 (x64 CLR and CoreCLR); the
  browser runtime is outside L1. Ask: name WebAssembly in the determinism scope (outside, or a `CoreMath` atan2 in the
  winding number).
- **Speed:** a customer who ships the default Blazor build gets the 9.5x; the exact code is hit hardest (repair 20x,
  up to 44x on small repairs). In the UI: the default remesh of a 12-triangle box (safe
  preset, 20 passes, 3118 triangles) takes 9 s interpreted. AOT costs a 39 MB instead of 17 MB `wwwroot` and a 4 min
  publish. WebAssembly is single threaded here: the parallel paths gain nothing. Ask: a line in the getting-started guide
  (AOT, and which operations are heavy).

## 8 to 10. `Remesher` (medium)

```csharp
DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.35, 0.25)) }.Generate().MakeDMesh();
var input = new DMesh3(box);
var r = new Remesher(box);
r.SetTargetEdgeLength(0.01 * input.GetBounds().DiagonalLength);
r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 40);
MeshConstraintUtil.PinSharpVertices(r, 75);
r.PreventNormalFlips = true;
for (int i = 0; i < 10; ++i) r.BasicRemeshPass();
```

- **8:** 103946 triangles, `NotCleanReasons == "self-intersections"`, 807 triangles facing against the input, 30069 with
  quality below 0.1; `InvertingPassesRejected == 0` and no other counter says anything. With 20 passes at a 2 % target the
  same setup is clean: the 10-pass state is a remesh that has not converged, and the passes needed grow with the ratio of
  input to target edge length. Expected: either a guard or a report (a convergence or inverted-triangle count).
- **9:** with `ApplySafePreset()` the same case is clean without inverted triangles, but 11013 triangles have quality
  below 0.1 (minimum 1.3e-5) at `MinTriangleQuality == 0.2`; 30 passes: minimum 0.082. The summary of
  `MinTriangleQuality` says splits and projection are not guarded; the name still reads as a floor.
- **10:** the safe preset costs 6-17x the time of `PreventNormalFlips` alone (box 2 %: 3.2 s against 0.5 s; box 1 %, 30
  passes: 25.4 s natively, above the 20 s rule).

## 11. Near-coplanar slivers are kept (low)

Difference of the box and a box with a coplanar top face tilted by 1e-9 rad: volume 0.892 as for no tilt, area 6.54 instead
of 6.36: a wedge of A of up to 6e-10 thickness stays on the top face (correct for the exact solid; the rounding keeps thin
pieces by design). `SolidBooleanPipeline.MinThickness` (default 0) removes it; a customer meets the sliver first.

## 12 to 16. Documentation and API (low)

- **12:** [remeshing-and-deformation.md](../guides/remeshing-and-deformation.md), first snippet: the overloads are
  `PreserveBoundaryAndFeatures(Remesher r, double creaseAngle = 30, double cornerAngle = -1)` and
  `PinSharpVertices(Remesher r, double minAngleDeficit = 75)`; the snippet passes the mesh and named arguments
  `creaseAngleDeg`, `minAngleDeficitDeg` that do not exist.
- **13:** `MeshMeasurements` has no `Volume(DMesh3)`; every demo needs the three-argument `VolumeArea` call.
- **14:** `geometry3Sharp_netstandard.csproj` compiles every `*.cs` below the root except the removed folders; this PR adds
  `demos\**` there (a gate file, it needs the coordinator's label).
- **15:** CS1570 in the `netstandard2.0` build (`<=` in XML summaries of `mesh_ops/ExactMeshBoolean.cs` near line 3496 and
  `implicit/IntervalImplicit.cs:93`).
- **16:** see section 6.

## Stage 2: alpha wrap (2026-11-25)

New demo tab "Alpha wrap" (`Demos/WrapDemo.cs`): `MeshAlphaWrap` on a sphere with single-triangle holes, an open curved
sheet, a point cloud on a torus (with noise), a thin plate (thickness down to 1e-12), two cubes with a gap (down to 1e-12),
and `PassageWrap` on a block with a bore and a pin. The checks: status, `UsedAlpha` / `UsedOffset`, `AlphaCoarsening`,
`EstimatedTriangles` against the result, the result checks, every input point inside the wrap (winding number above
1/2), the distance of the wrap's vertices to the input against the offset, shells and nested shells, and per pin the
certified clearance. 45 cases (`wrap/*` in `e2e/cases.json`).

The demo's procedural inputs no longer use `Math.Sin` / `Math.Cos` (finding 16): circles come from the rational
parametrization `Shapes.Circle` (only + - * /), and the sheet is polynomial. With that, all 164 cases below 10 s natively
have the same input bits in the browser.

| Result | Native (master fa6f0c7) | Browser (interpreted) |
|---|---|---|
| `MeshAlphaWrap` and `PassageWrap`, 43 cases | every guarantee held: clean, every input point inside, minimum distance to the input = offset (`PassageWrap` after its simplification: 0.91-0.97 offset, still outside the input), pins certified, `PinBlockedByInput` with a usable reason, 2^k bit for bit at 2^-20 and 2^20 | the same bits as native in all 43; 8.0x the time at the median (1.2-15.7x) |
| all 164 cases | | 162 bit-identical; the 2 others are the `Solidify` path (#1577, now also on the torus) |

Findings:

- **W1 (medium, #1593):** `EstimatedTriangles` misses the result by 4-8x for open sheets (7.8x), thin plates (4x), and an
  alpha / offset ratio of 25 (4.35x: 547k triangles and 12.9 s from a 1.5k-triangle sphere). So `MaxEstimatedTriangles`,
  `WrapMaxEstimatedTriangles` and `PassageWrap.MaxTriangles` do not bound the size there (sheet with a budget of 2000: 4674
  triangles).
- **W2 (medium, #1575, #1542):** a hollow wrap is one connected shell with tunnels through the holes (sphere with 6 holes at
  `RelativeAlpha = 200`: one shell, 3.4 % of the volume). Counting nested shells does not detect it; `volume / (area x
  offset)` near 1 does. `MeshAlphaWrap` reports nothing; the issue lists the options.
- **W3 (low, research note only):** a point cloud sparser than alpha (50 or 200 points on the torus) returns `Success`
  with 22 or 2 shells and 0.2-1.5 % of the solid's volume, a wrap around each point or a hollow torus. That is the
  documented meaning of alpha, but nothing hints at it (eg the ratio of the median point spacing to alpha).

Re-checked on master after the #1548 fix: findings 1 and 3 are fixed (every flipped repro exact, 0.1-0.24 s). Finding 2
(#1575) is fixed for the box, the torus and the dense sphere, but not for the default sphere (alpha wrap, 1.3 % of the
volume, 35 s).

## Stage 2b: contouring and TPMS (2026-11-25)

New demo tab "Contouring & TPMS" (`Demos/ContourDemo.cs`), each with an analytic or self-reported reference:
- `TpmsLattice` (6 types x 3 styles, volume fraction, divisions, `Enclosure`) against its `RealizedVolumeFraction` and the
  measured volume of the mesh;
- `MeshOffset` of the box 1 x 0.7 x 0.5 (rotated, cell size, inset and offset) against the exact offset volume (the box
  plus its Minkowski rounding) and the exact inset box;
- `DualContouring` of the exact box SDF (sharp, must be exact) and of a sphere, `MarchingCubes` of the sphere, on aligned and
  shifted grids.

78 cases (`contour/*`). What held: every result that `MeshOffset` returned is clean, certified (`BandCertified`) and
within 0.25 % of the analytic offset volume at the default cell (0.06 % at d = 0.1, 0.002 % at a fine cell), with
exact insets of the axis-aligned box (0.03 % rotated); every TPMS
type and style is clean and its `RealizedVolumeFraction` is the measured one; 2^k invariance held bit for bit in all
modes; the DualContouring box is exact on shifted grids.

Findings:

- **C1 (medium, #1596):** `TpmsLattice` with `Enclosure = true` on a Gyroid sheet returns the solid (enlarged) box
  (volume 9.35 of a 2.1^3 box, `RealizedVolumeFraction` 1.17), `Status == Ok`. And a sheet wall thinner than about one
  cube loses most of the material with `Status == Ok` (requested 0.1, realized 0.0088; `TwoLevelSheet` gives 0.101).
- **C2 (medium, #1597):** `MeshOffset` insets that leave a part of 0.01-0.03 (the box inset by 0.235-0.245) return `Empty`,
  documented as "an inset beyond the thickness of the part"; the part exists and `RelativeCellSize = 0.05` returns it exactly.
- **C3 (medium, #1598):** `DualContouring` with exact Hermite data places one edge of a box whose faces lie on grid planes
  0.08 cells inside (two corners at x = -0.496 instead of -0.5): 12 clean triangles, volume -0.2 %.
- **C4 (medium, #1599):** `MarchingCubes` of a plain sphere has near-zero-area triangles in every root mode (36 of 896 at
  cube 0.1), so it fails `NotCleanReasons` ("degenerate triangles").
- **C5 (low, comment on #1577):** `TpmsLattice` meshes differ in the browser in 15 of 30 cases (same counts and volumes
  to 10 digits; `Math.Sin` / `Math.Cos` in the field; not in the determinism scope). `MeshOffset`, `DualContouring` and
  `MarchingCubes` gave the same bits.
- **C6 (low, research note only):** `MeshOffset` by a small distance (0.001 on the unit box) returns `GridTooLarge` with no
  message: the default cell (0.25 d) needs 6e10 nodes; a customer has to find `CellSize` or `RelativeCellSize` alone.

Browser: 73 cases, 17x native at the median; `MeshOffset` (exact certification) 50-72x interpreted.

## Stage 2 proposal (ranked by expected findings; 1 and 2 done above)

1. **Alpha wrap** (`MeshAlphaWrap`, `PassageWrap`): findings 2 and 3 already end there; holes against alpha, thin parts,
   point clouds, the target triangle count, and its time on small inputs (the 1M-triangle raw wrap) in the browser.
2. **Contouring / TPMS** (`MarchingCubesPro`, `AdaptiveDualContouring`, `TpmsLattice`): many parameters, analytic
   references (sphere, gyroid volume fraction), topology checks; uses `FastWindingNumber`-like float paths, so platform
   differences (finding 6) are likely.
3. **Terrain**: large grids, the known libm site in `TerrainSlope*` (#802) would show up in the browser.
4. **Parameterization**: fewer silent-failure modes (distortion is visible); lowest expected yield.

CI (coordinator 2026-11-25): the linux job builds the demo (`dotnet build demos/G3Demos -c Release`, next to the benchmarks),
so a library API change that breaks it shows there; `tools/ci_scope.py` treats `demos/` as a tool directory (a demos-only
change runs no library suite). The browser check stays local (it needs three.js from npm and a browser).
