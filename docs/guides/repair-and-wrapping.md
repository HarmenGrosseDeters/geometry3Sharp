# Mesh Repair, Hole Filling & Wrapping

This guide helps you choose between topology-preserving repair (`MeshAutoRepair`), guaranteed-clean fallback repair (`MeshRobustRepair`), hole fillers, winding-number solidification (`MeshWindingNumberSolidify`), alpha wrapping (`MeshAlphaWrap`), and clearance-certified passage wrapping (`PassageWrap`).

---

## Decision Tree: Which Repair or Wrap Class?

```text
Need to preserve original triangles?
├─ Yes, mostly good mesh with small holes / duplicates ──► MeshAutoRepair (PostRepairCleanup = true)
│   └─ Only want to fill boundary loops? ────────────────► MinimalHoleFill / SmoothedHoleFill / AutoHoleFill
└─ Need a guaranteed closed, manifold, self-intersection-free solid?
    ├─ From any mesh (prefer exact/minimal change, fall back only if needed) ──► MeshRobustRepair
    ├─ From a triangle soup / overlapping shells (voxelized rebuild OK) ──────► MeshWindingNumberSolidify
    ├─ Enclosing outer shell around any mesh or point cloud ──────────────────► MeshAlphaWrap
    └─ Outer wrap that must keep specified bores / channels open ─────────────► PassageWrap
```

| Scenario | Recommended class | Output characteristics |
|---|---|---|
| Small holes, cracks, duplicate vertices, degenerate triangles | `gs.MeshAutoRepair` + `gs.MeshPostRepairCleanup` | Preserves input triangles where possible; not guaranteed closed |
| Guaranteed clean solid from arbitrary mesh input | `gs.MeshRobustRepair` | Clean closed manifold solid via progressive fallback ladder |
| Fill specific boundary holes | `gs.MinimalHoleFill`, `gs.SmoothedHoleFill`, `gs.AutoHoleFill`, `PlanarHoleFiller`, `SimpleHoleFiller` | In-place hole caps; choose flat/sharp vs. smooth vs. topological |
| Rebuild soup / overlapping shells as closed solid | `gs.MeshWindingNumberSolidify` | Remeshed closed surface; features below grid cell size are lost |
| Closed watertight shell enclosing any mesh or point cloud | `gs.MeshAlphaWrap` | Closed 2-manifold self-intersection-free wrap at `Alpha`/`Offset` |
| Watertight wrap with certified open passages (bores, channels) | `gs.PassageWrap` | Chooses coarsest certified `Alpha` preserving every `PassagePin` |

---

## `MeshAutoRepair` & `MeshPostRepairCleanup`

- **Namespace & classes:** `gs.MeshAutoRepair`, `gs.MeshPostRepairCleanup`
- **Input / mutability:** Operates on the mesh and produces a compacted mesh in `repair.Mesh` (pass `new DMesh3(mesh)` to keep the original intact; always read `repair.Mesh`).
- **Guarantees & limits:** Welds coincident cracks, removes duplicate triangles, resolves bowties, fills holes, and optionally cleans up remaining degenerate triangles, folds, and inside-out components (`PostRepairCleanup = true`). Tolerances default to relative mode (`ToleranceMode = Relative`) based on the mesh bounding-box diagonal so results scale cleanly across units and UTM coordinates. The cleanup collapses a needle (a triangle with `MaxAspectRatio` above 1000) between two interior vertices onto the end that is a corner or a crease (`CreaseAngleDeg`), else onto the end that keeps the volume best (before: the midpoint, which moved corners by half the edge), and skips it if the discarded vertex would move by more than `MaxCollapseDisplacement` (default 1e-4) of the diagonal. An edge with a boundary vertex keeps that vertex, a boundary edge still collapses to its midpoint.
- **Example:** [MeshRepairExample.cs](../examples/MeshRepairExample.cs)

```csharp
using g3;
using gs;

var repair = new MeshAutoRepair(new DMesh3(mesh)) { PostRepairCleanup = true };
repair.Apply();
DMesh3 repaired = repair.Mesh;
```

---

## `MeshRobustRepair`

- **Namespace & class:** `gs.MeshRobustRepair`
- **Input / mutability:** Produces the repaired solid in `robust.Mesh` (working in a power-of-two normalized frame for scale invariance and scaling back on return).
- **Guarantees & limits:** Returns a closed, manifold, self-intersection-free mesh unless cancelled, all fallbacks fail, or a scale disparity / lost-component check triggers (`Apply()` returns `false`).
- **Example:** [MeshRepairExample.cs](../examples/MeshRepairExample.cs)

```csharp
using g3;
using gs;

var robust = new MeshRobustRepair(mesh);
if (robust.Apply()) {
    DMesh3 clean = robust.Mesh;   // verify robust.UsedSolidify && robust.SolidifyReason == ""
} else {
    Console.WriteLine($"{robust.ResultReason} (fallback: {robust.FallbackReason})");
}
```

### Progressive Fallback Ladder

1. **Clean pass-through (`PassThroughCleanParts`, default `true`):** Closed components that are clean on their own and whose bounding boxes do not touch other components are passed through untouched (`PassedThroughTriangles`, up to 64 components). An input that is clean as a whole (any number of parts, their bounding boxes may meet, eg a cavity) is returned as it is too (`ResultPath` `PassThrough`, no stage runs, so `RepairVolumeIoU` and `PhaseMilliseconds` stay empty; before, the needle cleanup of the repair could move its corners, #1337), unless `ConfigureRepair` is set: a configured tolerance (eg `MinEdgeLengthTol`) asks for a change of a clean mesh, and the repair runs.
2. **Topology-preserving repair:** Runs `MeshAutoRepair` + `MeshPostRepairCleanup`. Accepted if clean and within `MinVolumeIoU`.
3. **Exact self-intersection resolve (`UsedExactResolve`):** Runs `ExactMeshBoolean.Resolve` with `SafeRounding` (configurable via `RoundingMaxDisplacement`, `RoundingUlpFactor`, `RoundingAbsoluteTolerance`, `RoundingMaxPairTests`, `StrictRounding`). Separates touching vertices if needed (`SeparateFraction`).
4. **Winding-number solidification (`UsedSolidify`):** Rebuilds the solid via `MeshWindingNumberSolidify` when exact resolve cannot produce a clean result or loses volume (`UsedInput` when reverting to original input).
5. **Alpha wrap (`UsedAlphaWrap`):** Wraps the input via `MeshAlphaWrap` and simplifies within half the offset while keeping triangle quality `>= 0.3` (optional `AlphaWrapQualityPass` off by default).

### Important Behaviors & Diagnostics

- **Small inputs (`SmallInputWrapTriangles = 64`):** Inputs with fewer than 64 triangles derive wrap `Alpha` from median edge length (`max(diag / WrapRelativeAlpha, medianEdge / SmallInputWrapEdgeDivisor)`) to avoid generating 100k+ wrap triangles on simple shapes. Set `SmallInputWrapTriangles = 0` to always use `diag / WrapRelativeAlpha`.
- **Lost components (`MinVolumeIoU > 0`):** On multi-part inputs, `Apply()` returns `false` (with the result in `Mesh`) if a separable part is dropped (`LostFarPart`, `LostFarOpenPart`, `LostSeparatePart`).
- **Scale disparity (`ScaleDisparity`):** When tiny parts sit very far apart so that the global clean check's degenerate-area threshold (`1e-12 * diagonal^2`) engulfs real triangles, `Apply()` returns `false` with `ResultReason = ScaleDisparity` instead of producing an overly coarse wrap.
- **Volume diagnostic (`ResultVolumeIoU`):** Optional diagnostic comparing final result volume IoU against the input (`Apply()` behavior unchanged).
- **Lost-area report (`MeasureLostArea`, opt-in):** After `Apply()`, `LostArea`, `LostAreaShare`, `LostTriangleIds` and `LostAreaMaxDistance` say how much of the input's surface the result does not represent (details below). It changes no result and no status, and by itself does not tell an interior wall removed by design from a damaged result.
- **Stage reporting:** Inspect `FallbackReason`, `ResultReason`, `PhaseMilliseconds`, `ExactResolveRoundsUsed`, and `NotCleanReason(mesh)`.

### Lost-area report (`MeasureLostArea`)

`MeshRobustRepair` returns a clean solid, and a solid has no interior walls, so the repair drops input surface on purpose (an interior wall, the contact faces of touching parts, a part inside another part, an open sheet attached to a solid). The report measures how much input surface is gone. It is a report, not a refusal: `Apply()`, `ResultReason`, `Mesh` and every other output are the same with and without it (a hash of the mesh bit for bit, `MeshRobustRepairLostAreaTests`).

- **What it measures:** every input triangle is sampled at the centroids of 1, 4, 9 or 16 equal sub-triangles (16 up to 32k triangles, 9 up to 58k, 4 up to 131k, 1 above: about 500000 samples), and a triangle of more than the mean area gets more in proportion to its area (up to 1024 samples; the extra samples add up to about the base budget again), so one huge triangle among many small ones is not judged by 16 samples. The distance of each sample to the result is one nearest-triangle query (`MeshBatchQueries.NearestPointBatch`, parallel; the results do not depend on the thread count), and a sample farther than the tolerance from the result is lost. `LostArea` is the sum of the lost samples' shares of their triangles' areas, in the caller's units squared; `LostAreaShare` is `LostArea` over the input's area. Both sides of a sheet, and every face of an interior wall, count as the input has them. Triangles without area are not sampled and never listed.
- **What it reports:** `LostTriangleIds` are the ids of the triangles with more than half of their samples lost (ids of the input as given: the mesh is modified in place, so copy it first to use them); `LostAreaMaxDistance` is the largest distance of any sample to the result; `UsedLostAreaTolerance` is the tolerance used. NaN and an empty list where it is not measured (off, empty input, `Apply()` without a result). A false `Apply()` that leaves a result in `Mesh` (a lost part) is measured too. Cancelling during the report (`CancelF`) makes `Apply()` false, as a cancel in any stage does (the result stays in `Mesh`, the report is NaN).
- **Tolerance (`LostAreaTolerance`, 0 = derived):** what the stage that made the result may legitimately move the surface by, plus the query's own error. A value that is not a finite number above 0 counts as 0 (derived), as the other settings of the class that make no sense fall back to their defaults. The derived tolerance is `max(stage term, rounding bound) + 8 ulp`:
  - stage term, Solidify: 2 grid cells of the coarsest cell the contour used (`Solidify.UsedCellSize * RefineFactor`: with local refinement most of the surface is contoured at the coarse cell, `UsedCellSize` is the finest);
  - stage term, AlphaWrap: 2 offsets (`AlphaWrap.UsedOffset`; the wrap lies at the offset from the input and its simplification keeps within half of it); with a target (`WrapTargetTriangles`) the larger of 2 and `1 + WrapTargetMaxDistance` (at least 1.5) offsets;
  - rounding bound, every path (the exact resolve's bound, `MeshRoundingRepair`): the largest of `RoundingMaxDisplacement` times the diagonal of the largest component (1e-5), `RoundingUlpFactor` (256) times the unit in the last place (ulp) of the input's largest coordinate, and `RoundingAbsoluteTolerance`; it is the only term for the repair and the pass-through (set `LostAreaTolerance` if a configured repair moves more);
  - 64 ulp of the largest coordinate: the query's own error. The distance is that of the sample to the nearest point the query returns, not the query's own distance (a square root of a quadratic form that cancels for a point on the triangle: wrong by about 5e-9 times the triangle's size). What is left is the sample's rounding, the nearest point's and the difference: measured with samples on the result (spheres and sheared boxes, offsets 0 to 1e7, extents 1e-6 to 1e3) at most 2.5 ulp far from the origin and 14 ulp near it, so the term is at least 4.5 times the worst measured. It is what keeps a result that is the input at exactly 0 when the ulp term is switched off (`RoundingUlpFactor = 0`).
  - A rounding setting that is not a finite number of at least 0 (NaN, infinity, negative) counts as 0 in these terms and the others stand (the repair itself passes such a setting on: `MeshRoundingRepair` rejects every edit for a bound that is not finite). If nothing valid is left to derive a tolerance from (coordinates that are not finite), the report is not measured (NaN): never 0 lost because of an invalid setting.

  All terms scale exactly with a 2^k scaled input (the working frame, the ulp of a power of two), so the share is the same bit for bit.
- **Blind band (the report is a lower bound):** input surface closer than the tolerance to the result counts as present, so the loss it reports is a lower bound of the lost area by that band. In units of the diagonal D of the input's largest component at the defaults: Solidify about 2 / `SolidifyResolution` = 1.6 % of D at resolution 128 (3 % at 64, 6 % at 32; 4 times that where the contour is refined locally and that coarse cell is the unrefined one); AlphaWrap about 2 / 3000 = 0.067 % of D (`WrapRelativeOffset` 3000; with a coarsened wrap its coarser offset); otherwise 1e-5 of D, or the ulp and absolute terms where they are larger (far from the origin, or with a `RoundingAbsoluteTolerance`). Any removed wall, fin or contact face closer than that to a kept face reads 0. Two unit spheres 1.2 apart have 5.03 of cap surface inside each other: the exact resolve reads 5.0, the Solidify 2.6 at `SolidifyResolution` 32 (52 %), 3.3 at 48 (66 %) and 4.3 at 128 (86 %). Do not take the number as the area of what was removed; take it as at least that much. A smaller `LostAreaTolerance` shrinks the band (and then reads the stage's own movement as loss).
- **Cost:** an extra copy of the input, a tree of the result and the queries. Off by default (no extra pass, no copy, no allocation for it). `benchmarks/LostAreaBenchmark` (net10.0, shared machine, median of 7 paired runs): see its README: the report adds 40 % to the repair on a sphere with a hole of 99k triangles (300 ms), 25 % on a box with a wall, 44 % on a sphere of 1.4k and 110 % on the exact resolve of two overlapping spheres of 23k triangles (a loaded machine; 14 to 97 % on a quieter one). More than a few percent, so not the default.
- **Reading it:** a large value does not mean damage. An interior wall removed by design reads as lost area, and that is correct: a unit box with a wall of two faces of area 1 (input area 8.4) reads `LostArea` 2 (share 0.238), and the result is the box. A damaged result (a stage that rebuilt the wrong solid, as the volume check did on one mesh of #1479) reads the same, so the number alone does not tell the two apart. What does: where the lost triangles are (`LostTriangleIds`: inside the kept surface or standing apart from it) and the volume agreement (`ResultVolumeIoU` with `MeasureResultVolumeIoU`: an interior wall leaves the solid unchanged, IoU near 1; a damaged result does not). The report does not compute whether the lost area is connected to the kept surface, and does not measure area that the result added, only what it lacks.
- **Sampling:** the area is resolved to the sub-triangles (a lost region smaller than one can be missed or counted whole); on the huge-triangle test (5000 of area among 19602 unit-sheet triangles, the result covers 1012.5 of it) the estimate is within 2 % of the 3987.5. Beyond the cap of 1024 samples a triangle of more than 1024 / k^2 times the mean area has a larger share per sample. The distance is to the result's triangles: a sample on a kept surface counts as present even if the result's triangle there is a different one.

Example: [MeshRepairExample.cs](../examples/MeshRepairExample.cs) (`LostAreaShare`).

---

## Hole Filling

- **Namespace & classes:** `gs.MinimalHoleFill`, `gs.SmoothedHoleFill`, `gs.AutoHoleFill`, `g3.PlanarHoleFiller`, `g3.SimpleHoleFiller`, `g3.MeshBoundaryLoops`
- **Input / mutability:** In-place modification of `DMesh3`.
- **Example:** [HoleFillExample.cs](../examples/HoleFillExample.cs)

| Class | Best for |
|---|---|
| `gs.MinimalHoleFill` | Sharp/creased or developable holes; flips diagonals that would collide with existing outer edges |
| `gs.SmoothedHoleFill` | Smooth organic surfaces requiring approximate boundary tangent continuity (solved relative to bounds center) |
| `gs.AutoHoleFill` | General-purpose automatic hole filling across boundary loops |
| `g3.PlanarHoleFiller` | Planar holes (including nested holes, e.g., from a plane cut through a torus) |
| `g3.SimpleHoleFiller` | Fast topological triangulation of a single `EdgeLoop` without shape smoothing |

---

## `MeshWindingNumberSolidify`

- **Namespace & class:** `gs.MeshWindingNumberSolidify`
- **Input / mutability:** Read-only input; writes a new closed `DMesh3` to `Result`.
- **Guarantees & limits:** Reconstructs a closed solid from soups, overlapping parts, or large holes by contouring the generalized winding number. Features smaller than the grid cell size are lost.
- **Example:** [WindingNumberSolidifyExample.cs](../examples/WindingNumberSolidifyExample.cs)

---

## `MeshAlphaWrap`

- **Namespace & class:** `gs.MeshAlphaWrap`
- **Input / mutability:** Accepts a `DMesh3` (triangles and/or vertices) or `IEnumerable<Vector3d>` (point cloud); writes a new `DMesh3` to `Result`.
- **Guarantees & limits:** Produces a closed, oriented, 2-manifold, self-intersection-free surface strictly enclosing the input at approximately `Offset`, capturing concavities down to `Alpha` (Portaneri et al. 2022, built on exact `Delaunay3`). Supports custom spatially varying `AlphaField` (clamped to `[3 * offset, alpha]`).
- **Example:** [AlphaWrapExample.cs](../examples/AlphaWrapExample.cs)
- **Further reading:** [OpenSoupWrapBenchmark README](../../benchmarks/OpenSoupWrapBenchmark/README.md)

```csharp
using g3;
using gs;

var wrap = new MeshAlphaWrap(mesh) { RelativeAlpha = 50, RelativeOffset = 1000 };
if (wrap.Compute())
    DMesh3 result = wrap.Result;
else
    Console.WriteLine($"{wrap.Status}: {wrap.Error}");
```

### Triangle Count & Budget on Open Surfaces

Wrap triangle count scales with `surface area / Alpha^2`, not input triangle count. Open inputs (triangle soups or meshes with holes larger than a few `Alpha`s) are wrapped on **both sides** of every open sheet, roughly doubling triangle count compared to a closed solid of the same area.

Set `MaxEstimatedTriangles` (`MeshRobustRepair.WrapMaxEstimatedTriangles`, off by default) to automatically coarsen `Alpha` and `Offset` until the estimated raw triangle count (counting open components twice) fits the budget.

---

## `PassageWrap` (Clearance-Certified Wrapping)

- **Namespace & classes:** `gs.PassageWrap`, `gs.PassagePin`
- **Input / mutability:** Read-only input; writes new `DMesh3` to `Result`.
- **Guarantees & limits:** Wraps an input while guaranteeing specified bores, channels, or blind holes (`Pins`: polyline axis + radius) stay open. Computes the coarsest `Alpha` satisfying the analytical carving bound `sqrt((r - offset)^2 - alpha^2) >= pinRadius` from the certified input clearance `r`, wraps, simplifies, and verifies each pin's clearance interval `[ClearanceLow, ClearanceHigh]` and winding number (`PinResults`).
- **Example:** [AlphaWrapExample.cs](../examples/AlphaWrapExample.cs)
- **Further reading:** [PASSAGE_WRAP.md](../design/PASSAGE_WRAP.md)

If verification fails, `PassageWrap` tightens `Alpha` (`×0.8`, up to 3 retries) and returns `PassageNotKept` rather than falsely claiming success. Other statuses: `PinBlockedByInput` (pin wider than input clearance), `BudgetExceeded` (exceeds `MaxTriangles`; never coarsens past the pin constraint), and `BoundPremisesHeld = false` (analytical premises did not apply; measurement still decides).

---

## Validating & Checking Mesh Cleanliness

- **Namespace & classes:** `gs.MeshRepairMetrics`, `gs.MeshRobustRepair.NotCleanReason` / `NotCleanReasons` / `FindDefects`, `DMesh3.CheckValidity`, `g3.MeshValidation`
- **Example:** [RepairMetricsExample.cs](../examples/RepairMetricsExample.cs)

`MeshRobustRepair.NotCleanReason(mesh)` returns `""` when the mesh is closed, manifold (no bowties), has no degenerate triangles, is consistently oriented with no inside-out components, and passes the exact self-intersection check. Call `NotCleanReasons(mesh)` or `FindDefects(mesh)` to inspect all failed checks at once. For a boolean result, what the check says and does not say (the point set, removed components, contacts) is in [Booleans & Imprint](booleans-and-imprint.md#what-notcleanreasonresult-covers).
