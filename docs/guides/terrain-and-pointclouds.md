# Terrain Engineering & Point Cloud Processing

This guide covers height grids (DGM/DTM) and error-certified TIN generation (`HeightGrid`, `TerrainGridTin`), certified slope design (`TerrainSlopeDesign`), cut/fill cell volumes (`TerrainCellVolumes`, `TerrainVolumeDifference`), terrain safety lines and dual contouring, LiDAR ground filtering (`ClothSimulationFilter`), and point cloud normal estimation and surface reconstruction (`PointCloudNormals`, `PointCloudSurface`).

---

## Height Grids (DGM / DTM) & Certified TINs

- **Namespace & classes:** `g3.HeightGrid`, `g3.HeightGridReader`, `g3.HeightGridWriter`, `gs.TerrainGridTin`, `gs.TerrainBreakline`
- **Example:** [TerrainToolsExample.cs](../examples/TerrainToolsExample.cs)
- **Input / mutability:** Reads regular-lattice XYZ ASCII and ESRI ASCII Grid (`.asc`) rasters with no-data gaps (status codes instead of exceptions).
- **Guarantees & limits:**
  - `HeightGrid.BuildMesh` builds the full 2-triangles-per-cell grid TIN.
  - `TerrainGridTin` produces a drastically lighter TIN with a **certified exact vertical error**: the exact maximum distance between the TIN and the grid surface is at most `Epsilon` (reported in `SurfaceError`, including output world-coordinate rounding).
  - Breaklines (`TerrainBreakline`) are preserved as edge chains using either grid heights or surveyed input `z` heights (`TerrainBreaklineHeights`, `BreaklineError`); no-data cells are omitted.
- **CLI:** `g3cli dgm`, `g3cli dgm_tin` ([g3cli README](../../tools/g3cli/README.md)).
- **Further reading:** [TERRAIN_DGM_TO_TIN.md](../design/TERRAIN_DGM_TO_TIN.md)

---

## Certified Slope Design (`TerrainSlopeDesign`)

- **Namespace & classes:** `gs.TerrainSlopeDesign`, `SlopeToe`, `SlopeSpec`, `SlopePiece`, `SlopeRamp`, `CatchLine`, `TargetHeightLine`
- **Input / mutability:** Takes toe lines (`SlopeToe`) and a terrain `DMesh3` height field; generates slope surface, catch lines, target-height bench lines, volumes/areas, and composite terrain.
- **Guarantees & limits:**
  - **Never steeper than specified:** Every output triangle is verified exactly against its slope and supporting toe. Any uncertifiable design fails with `SlopeViolation` (`StatusKinds.NotCertified`) rather than returning an invalid slope.
  - **Tolerance-certified composite:** Composite stays within `Tolerance` (required; no library default) of the theoretical cut/fill clamp (`max(S_fill, min(T, S_cut))`), except for catch-point step shifts bounded by `2 * UsedMinFeatureSize` (certified per composite).
  - **Catch-line accuracy:** Reports both local `CatchLine.Uncertainty` and certified `CatchLine.ProvenUncertainty` (checked against `RequiredCatchLineAccuracy`), plus `CatchLineBandWithoutLine` where slope and terrain approach within the uncertainty band away from the catch line.
  - **Determinism:** Independent of toe-piece ordering; single-threaded; bit-identical under power-of-two scaling.
- **Example:** [TerrainSlopeExample.cs](../examples/TerrainSlopeExample.cs)
- **Further reading:** [TERRAIN.md](../design/TERRAIN.md), [TERRAIN_RAMPS.md](../design/TERRAIN_RAMPS.md)

```csharp
using g3;
using gs;

var design = new TerrainSlopeDesign {
    Toes = { SlopeToe.Constant(toePoints, closed: true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)) },
    Terrain = tin,
    Tolerance = 0.05,        // required accuracy in meters
    BuildComposite = true
};
design.Compute();            // check Status (NoDaylight returns false with a partial result)
if (design.Status != TerrainSlopeDesign.Statuses.Success
    && design.Status != TerrainSlopeDesign.Statuses.NoDaylight)
    Console.WriteLine($"{design.Status} ({design.StatusKind}): {design.Message}");
```

### Key Options & UTM Local Frame

- **Slope directions & specs:** Cut, fill, or mixed `Directions.Auto`. Specify slopes via `SlopeSpec.Ratio(n)` (`1:n`), `Percent`, `Degrees`, or `Rise` (open toes use `Left`/`Right` sides with lateral limits; closed toes use `Inside`/`Outside`).
- **Bench levels & ramps:** `TargetHeights` extracts level lines (`TargetHeightLines`); `Ramps` cuts access ramps into cut slopes (`SlopeRamp`, cut-only in phase R1).
- **Composite modes:** `CompositeKinds.Full` (default) outputs the entire modified terrain; `CompositeKinds.Replacement` outputs only the replaced patch along with `ReplacedTerrainTriangles` and `CompositeTerrainVertices`.
- **UTM coordinates & `LocalFrame`:** Because output vertices round to world coordinates, `UsedMinFeatureSize` scales with coordinate magnitude (~1.5 cm at UTM with default `SlopeMargin`, micrometers near origin). Enable `LocalFrame = true` to compute in a local origin near the toes (`UsedOrigin`); all outputs are returned in that local frame (add `UsedOrigin` to XY to map back to world coordinates, noting half-ULP rounding is outside exact checks). While `Compute()` runs with `LocalFrame = true`, `Terrain` is translated in place and restored bit-for-bit on return (do not share that `Terrain` across concurrent threads).
- **Fan-direction cap (`MaxFanDirections`, default `512`):** Rejects excessively tight `Tolerance` values requiring too many cone facets per corner (`InvalidInput`, naming a valid `Tolerance`).

### Status Reference

| Status | `StatusKind` | Meaning & action |
|---|---|---|
| `Success` | `None` | Complete, certified result |
| `NoDaylight` | `None` | Slope did not reach terrain everywhere (`IsComplete = false`); partial result kept (`EndReasons.FootprintBoundary`, `HeightLimit`). Extend terrain or set `HeightLimit` |
| `InvalidInput`, `NotHeightField` | `InvalidInput` | Invalid toe/slope/parameter (`ToeProblems`) or non-height-field terrain under slope |
| `ToeSteeperThanSlope`, `ToeUndercut`, `ToeOvertopped`, `RampNotOnWall` | `GeometricallyInfeasible` / `UnsupportedByMethod` | No surface meets the spec (`ToeProblems` / `RampProblems` name offending segments) |
| `NotImplemented` | `UnsupportedByMethod` | Varying slope along one segment, non-cut ramp, or unsupported ramp turn |
| `SlopeViolation`, `CatchLineAccuracyNotReached` | `NotCertified` | Could not certify within `Tolerance` or `RequiredCatchLineAccuracy`; widen `Tolerance` or enable `LocalFrame` at UTM |
| `Cancelled` | `Cancelled` | `CancelF` returned `true`; no result |
| `CompositeFailed`, `Failed` | `Internal` | Internal failure (`CompositeFailed` still keeps `SlopeSurface` and `CatchLines`) |

---

## Cut/Fill Cell Volumes, Terrain Tools & LiDAR Ground Filtering

| Task | Classes | Notes |
|---|---|---|
| Cut/fill volumes per regular XY lattice cell | `gs.TerrainCellVolumes`, `gs.TerrainVolumeDifference` | Exact up to rounding; cell sums match whole-footprint `TerrainVolumeDifference`; reports outside-lattice remainder; deterministic |
| Terrain dual contouring & safety lines | `gs.TerrainDualContouring`, `gs.TerrainSafetyLines` | Deterministic overlap removal; bounded safety-line certification (`MaxCertifySteps`, `MaxSourcePoints`, `MaxRefinedTriangles`) |
| LiDAR ground classification | `gs.ClothSimulationFilter` | Separates ground vs. non-ground points from 3D point clouds (Zhang et al. 2016) |

- **Example:** [TerrainToolsExample.cs](../examples/TerrainToolsExample.cs)

---

## Point Cloud Processing

Read and write point sets via `StandardMeshReader` / `StandardMeshWriter` using `.xyz` (`x y z` or `x y z nx ny nz`) or face-less `.ply` (loads as a vertex-only `DMesh3`).

- **Example:** [PointCloudExample.cs](../examples/PointCloudExample.cs)
- **Further reading:** [POINT_CLOUD_RECONSTRUCTION.md](../design/POINT_CLOUD_RECONSTRUCTION.md)

### 1. Alpha Wrapping Point Clouds (`MeshAlphaWrap`)

`new MeshAlphaWrap(points)` (or a vertex-only `DMesh3`) produces a closed, oriented, 2-manifold, self-intersection-free shell enclosing every point. It is not a detail reconstruction: noise below `Alpha` is smoothed over and gaps narrower than an `Alpha` ball are bridged.

- **Rule of thumb:** Choose `Alpha >= 2.5 * samplingSpacing` and `>= 4 * noiseSigma` to track the surface cleanly without spawning interior layers or cavities.

### 2. Outward Normal Estimation (`PointCloudNormals`)

- **Namespace & class:** `gs.PointCloudNormals`
- **How it works:** Computes unoriented axes via 16-nearest-neighbor PCA, then resolves outward sign using two probe points at `1.5 * (Alpha + Offset)` against an alpha wrap of the cloud (`Alpha` auto-selected from spacing/noise and widened up to 3× if a hole leaks inside).
- **Diagnostics:** Reports `Confident[i]`, `ConfidentFraction`, `Spacing`, `NoiseSigma`, `DegenerateCount`, and `Status` (`AlphaBelowCondition`, `AmbiguousOrientation` for open sheets/thin walls, `DegenerateInput`, `WrapFailed`). Exact duplicate points are merged.

### 3. Detailed Surface Reconstruction (`PointCloudSurface`)

- **Namespace & class:** `gs.PointCloudSurface`
- **How it works:** Estimates outward normals with `PointCloudNormals`, extracts the `0.5` iso-surface of the density-weighted fast winding number via `MarchingCubes` (`CubeSize` default `spacing / 2.5`) near the points, and seamlessly switches to the alpha wrap's signed distance across holes/gaps (`HoleFactor = 1.5` spacings) so holes close watertight.
- **Filtering & limits:** Removes small noise bubbles below `MinComponentAreaFraction` (`ComponentsRemoved`, `RemovedAreaFraction`). Returns `Success` for a single closed manifold component covering the points (`DataDropped` if `> MaxUncoveredFraction = 1%` of points sit farther than `CoverFactor = 4` spacings away). Memory scales at ~50 bytes per band node (`MaxGridNodes = 20_000_000`, ~1 GB).
