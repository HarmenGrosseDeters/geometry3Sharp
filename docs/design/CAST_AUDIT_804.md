# Audit of `double` to integer casts (R5, issue 804)

Rule R5 of [DETERMINISM_PROOF.md](DETERMINISM_PROOF.md) section 5: a cast of NaN or an out-of-range `double` to an integer
type is unspecified in C#. .NET 9 and later saturate (NaN gives 0); .NET Framework 4.8 x64 gives the "integer
indefinite" value (`int.MinValue`, `long.MinValue`). Inside the range the casts agree, so the audit concerns non-finite
and extreme input.

## Method

The analyzer G3D0006 (every conversion of a `double` to an integer type, `tools/analyzers/G3ExactCodeAnalyzers`) was run
over the whole library once, with its severity raised from `none` to `warning` in `.editorconfig` for all files (not
committed): 193 source lines in 73 files. Each site was read and put in one class:

- **guarded**: a finiteness or range test as doubles, dominating the cast, that NaN also fails;
- **in range**: the value is bounded by construction (no input coordinate reaches it);
- **converted**: the cast became `MathUtil.ToIntSaturating` / `ToLongSaturating` / `FloorToIntSaturating` /
  `FloorToLongSaturating` (NaN gives 0, out of range saturates, as .NET 9 and later). On .NET 9 and later these are the
  plain cast, so results on net10.0 do not change; on net48 the value is the net10 value instead of `int.MinValue`.
  Inside the valid range they are the cast.

A conversion changes the value only for NaN and out-of-range input, which the plain cast leaves unspecified. The
exact files (`math/Exact*`, `math/AdaptivePredicates.cs`, `mesh_ops/ExactMesh*`) were audited, not edited: all their
sites are guarded or bounded (rows below). Not covered by G3D0006 (it looks at `double` only): `float` to integer casts
(`float` NaN behaves the same; the sites outside `color/` and `Vector3f` conversions were not enumerated).

## Result

| file | sites | class | what the cast computes |
|---|---|---|---|
| `approximation/PrimitiveFit3.cs` | 4 | **converted** | RANSAC iteration count, the normal-estimation hash grid (`cell`, `maxRing`) from the point cloud |
| `color/ColorHSV.cs` | 1 | **converted** | sector of the hue (`h / 60`) |
| `color/Colorb.cs` | 1 | **converted** | float colour channel to byte: `Clamp` after the cast hid NaN, not +Inf (net10 255, net48 0) |
| `comp_geom/QualityMesh2.cs` | 1 | **converted** | sample count `Ceiling(2 span / grid.W)` |
| `comp_geom/SnapRounding2.cs` | 2 | guarded | `Round` of grid steps on `long` differences, and `f < r` before the cast (exact scope; registered by the earlier audit, #871) |
| `comp_geom/SphericalFibonacciPointSet.cs` | 1 | **converted** | index of the nearest point (a `double` loop variable) |
| `comp_geom/TriangulationMesh2.cs` | 1 | **converted** | sample count `Pow(nt, 1/3)` |
| `containment/TilingUtil.cs` | 4 | **converted** | tile counts `bounds / element size` |
| `core/DijkstraGraphDistance.cs` | 1 | **converted** | seed id from the x of a `Vector2d` pair |
| `core/Snapping.cs` | 1 | **converted** | increment count `value / increment` |
| `curve/Arc2.cs` | 2 | **converted** | quadrant index of the start and end angle |
| `curve/BSplineBasis.cs` | 1 | **converted** | knot span of `t` |
| `curve/CurveGenerators.cs` | 1 | **converted** | step count `length / step` (the `Clamp` after the cast hid only NaN) |
| `curve/CurveResampler.cs` | 2 | **converted** | split count `length / max edge length` |
| `curve/CurveSampler2.cs` | 3 | **converted** | sample count `length / spacing` (followed by `Max(.., 2)`) |
| `curve/CurveUtils2.cs` | 1 | **converted** | split count `Ceiling(length / target)` |
| `curve/PolyLine2d.cs` | 2 | **converted** | edge index of the parameter `t` (a clamp after the cast hid NaN, not huge `t`) |
| `curve/Polygon2d.cs` | 2 | **converted** | edge index of the parameter `t` |
| `implicit/CachingGridImplicit3d.cs` | 6 | **converted** | cell of a query point; the range test `x0 + 1 >= ni` wrapped around for a saturated `int.MaxValue` (a real defect, fixed: compares `x0 >= ni - 1`) |
| `implicit/CachingMeshSDF.cs` | 11 | **converted** | triangle/grid overlap ranges, crossing interval, cell of a query point (same wrap-around test fixed) |
| `implicit/GridImplicits3d.cs` | 6 | **converted** | cell of a query point after a range test that NaN passes |
| `implicit/MarchingQuads.cs` | 2 | **converted** | cell of a seed point |
| `io/GLTFReader.cs` | 3 | **converted** | accessor values read as `double` to indices |
| `io/HeightGrid.cs` | 3 | guarded | `Window` and `Height` test NaN and the range as doubles before the cast |
| `io/HeightGridReader.cs` | 3 | **converted** | lattice position and counts from file values |
| `io/PLYReader.cs` | 1 | guarded | `v >= 0 && v <= int.MaxValue && v == Floor(v)` before the cast |
| `io/PLYWriter.cs` | 1 | **converted** | float colour channel to byte |
| `math/AxisAlignedBox2i.cs` | 2 | **converted** | `Diagonal`, `Distance` (square root of an overflowed int sum is NaN) |
| `math/AxisAlignedBox3i.cs` | 2 | **converted** | `DiagonalLength`, `Distance` |
| `math/CoreMath/CoreMathCos.cs` | 1 | in range | libm port: the reduced argument index and the exponent are bounded by the algorithm (no input coordinate) |
| `math/CoreMath/CoreMathExp.cs` | 2 | in range | libm port: `t` is clamped by the overflow tests above |
| `math/CoreMath/CoreMathSin.cs` | 1 | in range | libm port: bounded reduction index |
| `math/IndexTypes.cs` | 3 | **converted** | `Length` of `Index2i / 3i / 4i` |
| `math/MathUtil.cs` | 8 | helper | the helpers themselves |
| `math/Query2Integer.cs` | 8 | **converted** | `Vector2d` with integer coordinates to `long` (the exact integer predicates of a `Vector2d` input) |
| `math/Vector3i.cs` | 2 | **converted** | explicit conversions from `Vector3f / Vector3d` |
| `mesh_generators/DualContouring.cs` | 1 | guarded | `!(ni * nj * nk <= MaxGridNodes)` throws before (NaN too) |
| `mesh_generators/MarchingCubes.cs` | 1 | guarded | `IsNaN / IsInfinity` of the bounds and `cells >= int.MaxValue` throw before |
| `mesh_generators/MeshGenerators.cs` | 2 | **converted** | rounded grid coordinates |
| `mesh_generators/TpmsLattice.cs` | 4 | **converted** | knot of the quantile table, level lookup (`fraction` NaN), cell counts, evaluated-cell statistic |
| `mesh_ops/AlphaWrapFeaturePass.cs` | 2 | **converted** | segment sample count, cell of a point |
| `mesh_ops/AlphaWrapQualityPass.cs` | 2 | **converted** | triangle budget, protected cell |
| `mesh_ops/ClothSimulationFilter.cs` | 5 | **converted** | cloth grid size (range-checked as doubles before), cell of a point, iteration budget |
| `mesh_ops/ContouringTopologyCertificate.cs` | 1 | guarded | `m > -1e9 && m < 1e9` before the cast |
| `mesh_ops/ExactMeshArrangement.cs` | 3 | guarded / in range | `sqrt(count)` of an int; Morton `quantize`: `!(f > 0) ? 0` (NaN to 0); the 21-bit clamp (`t >= 0 ? (t < 2097151 ? ..)`). Exact scope: not touched |
| `mesh_ops/ExactMeshBoolean.cs` | 5 | guarded / in range | `sqrt` of a count; the cell clamps test NaN and the range (`!(c >= 0) ? 0`, `IsNaN(t) || t < 0`); `g` clamped to +-1e6 with NaN to 0. Exact scope: not touched |
| `mesh_ops/ExactTriangulation2.cs` | 2 | in range | `sqrt` of a triangle count |
| `mesh_ops/IntrinsicTriangulationRefinement.cs` | 1 | guarded | `IsNaN(lg) ? NaN : .. Ldexp(1, (int)Max(-3000, Min(3000, lg)))` |
| `mesh_ops/MeshAlphaWrap.cs` | 2 | **converted** | timing statistics `te * cpu / cpuEval` (0 / 0 gives NaN) |
| `mesh_ops/MeshDeviation.cs` | 1 | **converted** | histogram bin of a value |
| `mesh_ops/MeshEnvelope.cs` | 2 | in range | `Floor(Log2(m))` of a finite `m > 0` is in [-1075, 1024] |
| `mesh_ops/MeshOffset.cs` | 1 | guarded | `!(nxd * nyd * nzd <= ..)` and `Max <= int.MaxValue` return `GridTooLarge` before (NaN too) |
| `mesh_ops/PartitionedReduction.cs` | 1 | **converted** | split position |
| `mesh_ops/PlanarHoleFiller.cs` | 1 | **converted** | fill grid size `planeW / scale / edge length` |
| `mesh_ops/PlanarSpansFiller.cs` | 1 | **converted** | fill grid size |
| `mesh_ops/PointCloudSurface.cs` | 2 | **converted** | lattice node counts and memo key |
| `mesh_ops/TerrainDualContouring.cs` | 9 | **converted** | grid size, point bins, cell ranges of an edge |
| `mesh_ops/TerrainGreedyTIN.cs` | 2 | **converted** | integer box and grid index of a point |
| `mesh_ops/TerrainGridTin.cs` | 16 | **converted** | cells of grid-space coordinates after `Min(.., nx - 2)` (that bound does not catch NaN or a negative saturation) |
| `mesh_ops/TerrainOffset.cs` | 1 | **converted** | step budget `depth / (0.25 d)` |
| `mesh_ops/TerrainSafety.cs` | 5 | **converted** | sample counts along a line, hash cell of a point |
| `mesh_ops/TerrainSlopeDesign.cs` | 2 | guarded | `BinOf`: `!(d > 0) ? 0 : (int)Min(bins, ..)`; `FanDirections`: `n <= max ? (int)n : -1` (the status of #804 comment 1; the site `:1090` of the issue no longer casts) |
| `mesh_ops/TerrainVolumeDifference.cs` | 1 | **converted** | lattice window (`Max(f, 0)` / `Min(f, n)` clamps do not catch NaN) |
| `mesh_ops/TetMeshImprover.cs` | 4 | **converted** | hash-grid cells and counts of the tetrahedra |
| `mesh_ops/TriangleQualityStatistics.cs` | 1 | in range | `q * (n - 1)` with the constants q in [0, 1] |
| `mesh_ops/WindingContouring.cs` | 5 | **converted** | refinement depth `Log2`, `long` cell loop bounds |
| `mesh_ops/WindingNumberOctree.cs` | 1 | **converted** | tree depth `Log2(n)` |
| `parameterization/UVOverlap.cs` | 4 | **converted** | overlap grid size and cell of a UV |
| `spatial/HashGridCells.cs` | 2 | guarded | `!(a > int.MinValue && b < int.MaxValue && a <= b)` returns false before (NaN too) |
| `spatial/MeshBatchQueries.cs` | 1 | **converted** | 10-bit Morton quantisation (`u` clamped to [0,1] before) |
| `spatial/SamplingGridSize.cs` | 1 | guarded | `!(cells >= 0) || cells >= maxSamples` returns invalid before (NaN too) |
| `spatial/SubStepBoxTree.cs` | 1 | guarded | `t >= 0 ? (t < cells ? (uint)t : cells) : 0u`: NaN gives 0 |
| `spatial/TriangleHashGrid.cs` | 1 | guarded | `!(a >= -Bias + 1 && b < Bias - 1 && a <= b)` returns false before (NaN too) |

Converted by earlier PRs of this issue (not in the analyzer run above, which sees only the remaining casts):

| file | sites | class | what the cast computes |
|---|---|---|---|
| `spatial/GridIndexing.cs`, `GridIndexing2.cs` | 15 | converted (#891) | the scale / shift / frame grid indexers: `FloorToIntSaturating` |
| `spatial/MeshSignedDistanceGrid.cs` | 6 | converted (#804, earlier) | j / k ranges and the crossing interval: `ToIntSaturating` |
| `comp_geom/ConstrainedDelaunayMesh2.cs` | 4 | converted (earlier) | the `long` cell keys |
| `mesh_generators/MarchingCubesPro.cs` | 5 | converted (earlier) | cell counts and the cell of a position |
| `spatial/TriangleBinsGrid2d.cs` | 2 | converted (earlier) | bin counts (R36) |
| `math/AxisAlignedBox2i.cs`, `AxisAlignedBox3i.cs` (`Contain`) | 12 | converted (earlier) | `FloorToIntSaturating` |
| `mesh_ops/TerrainSlopeComposite.cs`, `TerrainSlopeMap.cs` | 2 | converted (earlier) | the `long` merge cells: `FloorToLongSaturating` |
| `mesh_ops/ExactMeshBoolean.cs` (`CrossingGrid.cell`) | 1 | converted (earlier) | NaN gives cell 0 |

## Findings

- `implicit/CachingGridImplicit3d.cs` and `CachingMeshSDF.cs`: the range test `(x0 + 1) >= ni` wraps around when the cell
  of a huge coordinate saturates to `int.MaxValue`; the test passed and the grid access threw `IndexOutOfRangeException` on
  every runtime (net10 included). Fixed (`x0 >= ni - 1`); the test feeds `1e300`.
- After a saturating conversion, `+ 1` (and the like) on the result can still wrap: `TerrainDualContouring` (`NX`, `NY`, point
  bins), `PrimitiveFit3` (`maxRing`), `ClothSimulationFilter` (guarded by a double test before), `TilingUtil` (`+Inf` bounds
  give `int.MaxValue` tiles on net10, memory exhaustion; net48 gave 1). These behave the same on every runtime now; they are
  input-validation questions (status code or exception), not runtime differences, and stay open.
- The sites that clamp after the cast (`Colorb`, `PLYWriter`, `PolyLine2d`, `CurveGenerators`) hid NaN but not `+Inf` or a
  huge value (net10 255, net48 0).

## Tests

`SaturatingCastTests` (the helpers and the exact-scope sites, earlier) and `SaturatingCastSitesTests` (this PR): per
converted site with a public or cheap driver, NaN, an infinity and `1e300` with the stated result. On net10.0 they pass
before and after (the plain cast gives the same); they decide on the net48 test job. Checked locally by an emulated
net48: compiling the helpers' `#else` branch and replacing its NaN / overflow results by `int.MinValue` fails these tests.
