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
4. **Winding-number solidification (`UsedSolidify`):** Rebuilds the solid via `MeshWindingNumberSolidify` when exact resolve cannot produce a clean result or loses volume (`UsedInput` when reverting to the input: its oriented, welded copy if the input is unwelded or has flipped triangles, see "Input orientation" below).
5. **Alpha wrap (`UsedAlphaWrap`):** Wraps the input via `MeshAlphaWrap` and simplifies within half the offset while keeping triangle quality `>= 0.3` (optional `AlphaWrapQualityPass` off by default).
   - **Hole fill before the wrap (#1542, #1575):** The boundary loops of the (welded) surface that is wrapped, that are short, are the holes of a surface meant to be closed: the alpha ball passes through a hole wider than about 2 `Alpha` and wraps both sides of the sheet (a thin shell instead of the solid, twice the area, about 4 to 5 times the wrap time). They are filled first (`SimpleHoleFiller` for a triangle, `MinimalHoleFill` for larger loops), and the filled copy is wrapped. A loop is filled if its length is at most the larger of 64 `Alpha` and 8 median edge lengths, and at most a quarter of the bounding box diagonal (the rim of an open sheet, or of a bowl, is as long as the part it bounds and is never filled), and if the fill's area is at most half the area of the component the loop is on (a loose triangle is not a hole in something). An input without such a loop is wrapped as before, bit for bit. The fill costs about 0.2 % of the wrap (6 ms for 3 s on 2k triangles). What a hole fill cannot close (a crack, or a long rim) is wrapped on both sides as before.
   - **Thin shell guard:** After the wrap, a result that is a thin shell around an input that has a solid is refused: `Apply()` is `false` with `ResultReason = ThinShellWrap` (the shell stays in `Mesh`, clean as a mesh). The rule: the wrapped surface still has a boundary (a closed one is a solid, a flattened one a solid of no thickness), the wrap's volume is at most `2 * area * offset` (both sides of a sheet), and the solid of the input (the points where the magnitude of the winding number of its oriented copy exceeds 0.6, sampled on a 32^3 grid in its bounding box) has more than twice the wrap's volume (a wrap encloses its input, so it cannot, for a repair). The threshold is above the 0.5 of `MeshRepairMetrics` because a curved open sheet has a winding number just above 0.5 in the thin lens under it (0.51 for a unit plate with a sag of 0.01). Not refused: an input without a solid (a flat or slightly curved sheet, a terrain patch, a quarter-cylinder panel) and a small solid. Refused: an open surface that encloses space, such as a closed surface with holes the fill did not reach or a bowl as deep as a 0.3 cap of a sphere (winding number 0.76 inside). Limits of the rule, since an open input does not say whether it is meant as a sheet: a long open tube encloses its axis (winding number above 0.9), and a half-cylinder panel the space under it (0.69); both are refused if their wrap is thin. Measured at the defaults, the panels are solidified and never reach the wrap.

### Sharp Features on the Wrap (`AlphaWrapSharpFeatures`, `AlphaWrapSharpFeaturesOnSoups`)

When the ladder ends in the alpha wrap, `AlphaWrapSharpFeatures = true` (off by default) runs the sharp-feature pass on the raw wrap before the simplification. The pass moves vertices onto the offset surface's planes and creases near the input's creases and keeps every wrap guarantee (closed, manifold, no self-intersection, no contact with the input; [ALPHA_WRAP_FEATURE_VALIDITY.md](../design/ALPHA_WRAP_FEATURE_VALIDITY.md)).

By default the pass finds the input's creases from the dihedral angles of its edges. A triangle soup (unwelded, duplicated, flipped, T-junctions between faces) has no edge with two triangles, so the pass finds nothing there. Set `AlphaWrapSharpFeaturesOnSoups = true` as well: the creases then come from the input's unoriented varifold, a measure on position x plane that needs no connectivity ([VARIFOLD_CREASE_INPUT.md](../design/VARIFOLD_CREASE_INPUT.md)). Example: `MeshRepairExample.RobustRepairKeepingCreases`.

- **Gain:** on unwelded or flipped CAD soups at 100k triangles, the recall of the creases within one offset rises from 0.84-0.90 to the welded mesh's value (0.98-1.00).
- **Cost:** a measure of the input at 1.75 alpha spacing, about 7-21 % of the wrap's time.
- **Noisy soups:** on a jittered soup the vertices also take the varifold's denoised planes, which brings the result to what the exact creases give (recall 0.97-0.99 within one offset). A measured noise level decides this (it tells jitter from curvature), so clean flat or curved soups pay nothing.
- **Limits:** creases closer than about 3.5 alpha to a corner or to a third plane are partly missed. Faces moved apart by gaps are found within three offsets, but not placed within one. Holes wider than alpha let the wrap inside (issue #1542); the pass does not change that.

### Important Behaviors & Diagnostics

- **Small inputs (`SmallInputWrapTriangles = 64`):** Inputs with fewer than 64 triangles derive wrap `Alpha` from median edge length (`max(diag / WrapRelativeAlpha, medianEdge / SmallInputWrapEdgeDivisor)`) to avoid generating 100k+ wrap triangles on simple shapes. Set `SmallInputWrapTriangles = 0` to always use `diag / WrapRelativeAlpha`.
- **Lost components (`MinVolumeIoU > 0`):** On multi-part inputs, `Apply()` returns `false` (with the result in `Mesh`) if a separable part is dropped (`LostFarPart`, `LostFarOpenPart`, `LostSeparatePart`).
- **Scale disparity (`ScaleDisparity`):** When tiny parts sit very far apart so that the global clean check's degenerate-area threshold (`1e-12 * diagonal^2`) engulfs real triangles, `Apply()` returns `false` with `ResultReason = ScaleDisparity` instead of producing an overly coarse wrap.
- **Volume diagnostic (`ResultVolumeIoU`):** Optional diagnostic comparing final result volume IoU against the input's solid (`Apply()` behavior unchanged; for an unwelded or flipped input, the oriented copy below).
- **Input orientation (#1548):** The winding number of an unwelded soup, or of a mesh whose triangles disagree in orientation, cancels at the flipped faces, so it is no reference for the volume. If the input has such triangles, `Apply()` builds an oriented copy once: the vertices on boundary edges welded by position (within a hundredth of the shortest edge at either vertex and of the median of those, so a soup with noise on its corners is welded and a small far part keeps its vertices), the welded mesh oriented per connected component (`MeshRepairOrientation.OrientComponents`; not `MeshAutoRepair`'s own orientation, which leaves flipped triangles of a soup as they are) and then as a whole (`WindingContouring.OrientedCopy`). The volume checks (`RepairVolumeIoU`, `ResultVolumeIoU`) take their reference from it, and so do the fallbacks that used to take the raw input (exact resolve of the input, solidify, alpha wrap, `UsedInput`). A welded, consistently oriented input pays only a scan and gets the results it got before. The report fields keep their meaning; `NotCleanReason` stays a check of the result alone, and the lost-area report (`MeasureLostArea`) stays measured against the input as given (it compares surfaces, not orientation).

- **Nesting of closed shells (#1566):** The repair reads the nesting of closed shells by parity (`MeshPostRepairCleanup.FindInvertedComponents`): a shell inside an odd number of others is a cavity. A shell that is oriented outward inside another outward shell (an insert in a housing, a part modelled twice) is therefore reversed, and the result is hollow where the plain union of the shells (the nonzero winding number, the reading of `ExactMeshBoolean.Resolve`) is solid; the volume reference makes the same flip, so `RepairVolumeIoU` and `ResultVolumeIoU` cannot see it (a cube of half-size 1 with an outward cube of half-size 0.8 inside: volume 8 became 3.904 with `Apply()` true). Two outputs make it visible. `ComponentsFlippedByNesting` is the number of closed components that are outward by their own volume and that the nesting reading reverses (0 for an input without such a shell, and for a real cavity, an inward shell inside an outward one, which is read as the cavity and stays as it was). If `MinVolumeIoU` is above 0 and the solid with those shells taken for cavities differs from the plain union of the shells by more than `1 - MinVolumeIoU` (volume IoU), `Apply()` is false with `ResultReason` `NestingAmbiguous` (`"nesting ambiguous: inner shells taken as cavities"`; an earlier reason follows after `"; "`), and the result, the cavity reading, stays in `Mesh`. Only a result that follows the cavity reading is refused: a fallback that returns the union (the alpha wrap of the input is solid) keeps `Apply()` true, with the count set. On an unwelded soup (#1548) a shell counts as outward after the per component orientation, which follows the larger patch of its triangles, so a real cavity whose triangles are mostly reversed can be reported as `NestingAmbiguous` (and the outward shell with mostly reversed triangles as a cavity): the orientation of such a soup is lost, and the two readings cannot be told apart by the repair. A difference below the tolerance (an inner shell of 0.1 % of the volume) only sets the count. The input is ambiguous by itself; a caller that means the union takes it from `ExactMeshBoolean.Resolve` (or `SolidBooleanPipeline`), one that means the cavity reverses the inner shell itself. `NotCleanReason` and `MeshRepairMetrics` keep their meaning.
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
| `gs.SmoothedHoleFill` | Smooth organic surfaces requiring approximate boundary tangent continuity (solved relative to bounds center). With `ConstrainToHoleInterior` the fill is checked for folds over the seam and intersections and refilled with shorter edges, else `Apply()` returns false and the mesh is unchanged |
| `gs.AutoHoleFill` | General-purpose automatic hole filling across boundary loops |
| `g3.PlanarHoleFiller` | Planar holes (including nested holes, e.g., from a plane cut through a torus) |
| `g3.SimpleHoleFiller` | Fast topological triangulation of a single `EdgeLoop` without shape smoothing |

---

## `MeshWindingNumberSolidify`

- **Namespace & class:** `gs.MeshWindingNumberSolidify`
- **Input / mutability:** Read-only input; writes a new closed `DMesh3` to `Result`.
- **Guarantees & limits:** Reconstructs a closed solid from soups, overlapping parts, or large holes by contouring the generalized winding number. Features smaller than the grid cell size are lost.
- **Nesting of closed shells (#1609):** The input is read as its oriented copy (`WindingContouring.OrientedCopy`), which takes the nesting of closed shells by parity (as `MeshRobustRepair`, #1566): a shell inside an odd number of others is a cavity. An outward shell inside another (an insert in a housing, a part modelled twice) is reversed and the result is hollow where the plain union of the shells is solid (a cube of half-size 1 with an outward cube of half-size 0.8 inside: volume 8 became 3.904 with `Compute()` true). `ComponentsFlippedByNesting` counts those shells (0 for an input without one, and for a real cavity, an inward shell inside an outward one, which stays as it was). If `MinVolumeIoU` (default 0.99, 0 or less: no refusal) is above 0 and the two readings differ in volume by more than `1 - MinVolumeIoU`, `Compute()` is false with `ResultReason` `NestingAmbiguous` and `Result` null; below that difference it only sets the count. The other failures leave `ResultReason` empty. `WindingContouring.OrientedCopy(mesh, out int)` and `MeshRepairMetrics.ComponentsFlippedByNesting` / `NestingReadingsIoU` give the same count and the IoU of the two readings; `MeshRepairMetrics.VolumeIoU` keeps the parity reading. An input without a nested outward shell is unchanged bit for bit. Limit (as `MeshRobustRepair`, #1566): an insert in a housing with both shells inside-out is not seen (only the housing is flagged: count 0, the hollow reading, no refusal).
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

Wrapping an open or holed input costs about 4 to 5 times a closed one at the same `Alpha`: twice the area at about 2.2 times the density (the cells in the thin layer around the sheet are flat, and the points follow their shape, not the ball). `MeshRobustRepair` fills short boundary loops before its wrap (see "Hole fill before the wrap" above); a direct `MeshAlphaWrap` of a holed soup does not: weld it and fill its loops first (`MeshBoundaryLoops` with `MinimalHoleFill`) if a solid is meant. Measured on the 4k-triangle slot soup with 2.5 % of the triangles removed at `Alpha` = diagonal / 300: the wrap of the welded soup takes 14.6 s, has 1.03 M triangles and a volume of 0.027 on an area of 28.2 (the input's is 14.1); with the 101 loops filled it takes 2.1 s and has 243.6 k triangles, a volume of 2.51 and an area of 14.5 (the clean wrap: 243.5 k triangles).

**Holes the alpha ball passes through (by design, [#1542](https://github.com/HarmenGrosseDeters/geometry3Sharp/issues/1542)):** The ball enters a hole whose circumscribed circle is wider than about 2 `Alpha`, for example a dropped right isosceles grid triangle with legs of about 1.86 `Alpha` (as in the procedural soups below), whose circumradius is half its hypotenuse, about 1.32 `Alpha`. The wrap then goes through it and becomes one connected surface around both sides of the sheet, with one tunnel per hole it passes through, and it does not seal the hole.

On procedural closed shells of about 100k triangles (`RelativeAlpha = 300`, `RelativeOffset = 3000`) with such holes:
- **Holes alone** (2.5 % of the triangles dropped): about 2 times the raw triangles and 4-5 times the time of the same shell without holes.
- **With other soup defects** (jitter, gaps, duplicates, flips, T-junctions): about 4 times the triangles and 5.6-8.2 times the time.
- **Why** (from a 20k-triangle run of the same shell, with holes of about 4 `Alpha`): the wrap had about twice the input's area and about 2.2 times the triangles per area. Between the two sides only a thin layer stays inside, and the points there come from projections. The time stays linear in that work.

`MaxEstimatedTriangles` (on `MeshRobustRepair`: `WrapMaxEstimatedTriangles`), with `MaxAlphaCoarsening` (`WrapMaxAlphaCoarsening`) as a cap on the growth of `Alpha`, is the guard. It caps the estimate (3 × area / `Alpha`²), not the result:
- **What the estimate counts:** it counts a component twice only if the bounding-box diagonal of one of its boundary chains is above 4 `Alpha`. That diagonal is about 1.5 times the hole width, so only holes wider than about 2.7 `Alpha` count twice.
- **What it misses:** the narrow holes above count once, and the estimate sees neither the second side nor the denser surface. A budget of twice the closed shape's wrap therefore coarsens `Alpha` by only about 1.0-1.1, and the result can stay up to about twice the budget: about the budget with holes alone, about twice it with the other defects.
- **Setting it:** from the estimate's formula (not measured with a budget), set the budget to about half the raw triangle count you accept when such holes are expected, and check `AlphaCoarsening` and the result size.
- With very small requests, `AlphaCoarsening` can be positive infinity even on a successful wrap. Read `UsedAlpha` and `UsedOffset` for the actual parameters.
- **Side effect:** a coarser `Alpha` also closes holes and cavities narrower than about 2 `UsedAlpha`.
- **Open sheets, thin plates, sharp edges and small offsets (issue #1593):** the estimate has an offset band: the wrap goes around the rim of an open sheet or thin plate and, where the empty ball touches them from outside, around the edges of a solid, at the radius of the `Offset`, with triangles of the offset's size, and it follows a flat sheet at the distance of the `Offset` with triangles of about `Alpha` × `Offset`. `EstimatedTriangles` is `3 × (area + open area) / Alpha²` plus `EstimatedRimTriangles`: `0.6 × sheet area × max(0, Alpha / Offset − 8) / Alpha²` for the sheets (open components and, at half their area, closed components thinner than 0.2 `Alpha`: plates) plus `(1.2 × (open boundary length + rim of the thin components) + 0.15 × sharp edge length of the other components) / Offset` (sharp: a dihedral turn above 45°). A closed solid without sharp edges has no such part, and its estimate is the one it had. The tuning constants are the `static readonly` fields `MeshAlphaWrap.RimDensity`, `SharpDensity`, `SheetOffsetDensity`, `SheetOffsetRatio` and `MaxOffsetRatio` (the ratio `Alpha` / `Offset` the band is computed with at most: a subnormal offset gave an infinite or NaN band), calibrated on procedural sheets, plates, cubes and spheres at `RelativeAlpha` 5-96 and `Alpha` / `Offset` 4-50. **Changed with #1593:** `EstimatedTriangles` without a budget used to be the one-sided estimate `3 × area / Alpha²` (the open components were not looked for, to save time); it is now the same number as with a budget, counting open components twice and including the offset band, so on open input it is 2 to 8 times larger than before (a budget below it coarsens, as it did). The analysis runs on every `Compute()` and takes time in proportion to the triangle count (0.6-0.8 s for 805k triangles; `benchmarks/OpenSoupWrapBenchmark`). The thin-part test needs a consistently oriented surface: where two triangles share an edge in the same direction the component is not taken for a plate, and the sharp-edge test is on the angle between the planes, not on the orientation.
- **How good the estimate is:** result triangles divided by `EstimatedTriangles` (the estimate before in brackets, at the defaults `RelativeAlpha` 20, `RelativeOffset` 600): an open curved sheet 0.79 (7.8), a plate 1 × 1 × 1e-4 or 1e-12 0.84 (4.0), two cubes with a gap of 1e-2 at `RelativeAlpha` 5 0.96 (6.7), the same sheet at `RelativeAlpha` 48 and `RelativeOffset` 2400 (`Alpha` / `Offset` = 50) 0.82 (4.8 at 25). The estimate is an upper bound on these cases; it is above the result by a factor of up to 4 where it assumes a sheet and the part is not one (a plate of 0.01 at `Alpha` 0.059, at the edge of the thin-part test), and closed solids stay at 0.5-1.3 of it. Parts thin in two directions (a rod, a wire) are not recognised as such: a rod thinner than 0.2 `Alpha` counts as a plate, with half its surface as sheet area and a rim of at least the perimeter of the disc of that area, which is well above its two ends, so the estimate is too high there (a coarser wrap than needed), never too low. A wrap of a closed solid at `Alpha` / `Offset` of 50 with holes wider than 4 `Alpha` (open: the inside is wrapped too) was not calibrated. `MaxEstimatedTriangles` coarsens `Alpha` and `Offset` together until this estimate (bulk and band with 1 / `Alpha`², the rim with 1 / `Alpha`) fits the budget; a sheet of 512 triangles with a budget of 2000 gives a wrap of 1666 triangles (4642 before).
- **What to do:** `Result.TriangleCount` against `EstimatedTriangles` after `Compute()` remains the check for inputs outside those cases; bound the time with `MaxSteinerPoints` (a result that reaches it has box corners on it, status `PointLimitReached`). `PassageWrap.MaxTriangles` has its own estimate (the bulk and the open area, without the offset band) and is not changed by this.

To keep the alpha and seal the holes instead, fill short boundary loops on the welded input before wrapping (see [Hole Filling](#hole-filling)).

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
