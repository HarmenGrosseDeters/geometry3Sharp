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
- **Guarantees & limits:** Welds coincident cracks, removes duplicate triangles, resolves bowties, fills holes, and optionally cleans up remaining degenerate triangles, folds, and inside-out components (`PostRepairCleanup = true`). Tolerances default to relative mode (`ToleranceMode = Relative`) based on the mesh bounding-box diagonal so results scale cleanly across units and UTM coordinates.
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

1. **Clean pass-through (`PassThroughCleanParts`, default `true`):** Closed components that are clean on their own and whose bounding boxes do not touch other components are passed through untouched (`PassedThroughTriangles`, up to 64 components).
2. **Topology-preserving repair:** Runs `MeshAutoRepair` + `MeshPostRepairCleanup`. Accepted if clean and within `MinVolumeIoU`.
3. **Exact self-intersection resolve (`UsedExactResolve`):** Runs `ExactMeshBoolean.Resolve` with `SafeRounding` (configurable via `RoundingMaxDisplacement`, `RoundingUlpFactor`, `RoundingAbsoluteTolerance`, `RoundingMaxPairTests`, `StrictRounding`). Separates touching vertices if needed (`SeparateFraction`).
4. **Winding-number solidification (`UsedSolidify`):** Rebuilds the solid via `MeshWindingNumberSolidify` when exact resolve cannot produce a clean result or loses volume (`UsedInput` when reverting to original input).
5. **Alpha wrap (`UsedAlphaWrap`):** Wraps the input via `MeshAlphaWrap` and simplifies within half the offset while keeping triangle quality `>= 0.3` (optional `AlphaWrapQualityPass` off by default).

### Important Behaviors & Diagnostics

- **Small inputs (`SmallInputWrapTriangles = 64`):** Inputs with fewer than 64 triangles derive wrap `Alpha` from median edge length (`max(diag / WrapRelativeAlpha, medianEdge / SmallInputWrapEdgeDivisor)`) to avoid generating 100k+ wrap triangles on simple shapes. Set `SmallInputWrapTriangles = 0` to always use `diag / WrapRelativeAlpha`.
- **Lost components (`MinVolumeIoU > 0`):** On multi-part inputs, `Apply()` returns `false` (with the result in `Mesh`) if a separable part is dropped (`LostFarPart`, `LostFarOpenPart`, `LostSeparatePart`).
- **Scale disparity (`ScaleDisparity`):** When tiny parts sit very far apart so that the global clean check's degenerate-area threshold (`1e-12 * diagonal^2`) engulfs real triangles, `Apply()` returns `false` with `ResultReason = ScaleDisparity` instead of producing an overly coarse wrap.
- **Volume diagnostic (`ResultVolumeIoU`):** Optional diagnostic comparing final result volume IoU against the input (`Apply()` behavior unchanged).
- **Stage reporting:** Inspect `FallbackReason`, `ResultReason`, `PhaseMilliseconds`, `ExactResolveRoundsUsed`, and `NotCleanReason(mesh)`.

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

`MeshRobustRepair.NotCleanReason(mesh)` returns `""` when the mesh is closed, manifold (no bowties), has no degenerate triangles, is consistently oriented with no inside-out components, and passes the exact self-intersection check. Call `NotCleanReasons(mesh)` or `FindDefects(mesh)` to inspect all failed checks at once.
