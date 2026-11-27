# Sibling bug search S1 (2026-11-25)

Back to [Research notes](README.md)

Search for further places in the library with the same fault as recent bugs (owner OK 2026-11-25). Library code, not tests;
nothing was changed in the library. Base: master 36d19843. Every candidate was tried with a procedural repro on that master
(net10.0, linux x64, glibc); a pattern match without a failing repro is listed as unconfirmed, with the reason.

## 1. Result

| Class (seed) | Confirmed | Issue |
|---|---|---|
| Platform libm in code whose result is pinned or claimed invariant (#1590) | `Floor(Math.Log(x, 2))` as an exponent: `FoldoverFreeParameterization` gives another result (14 against 27 iterations, energy 6.16 against 3.31) at UV extents 2^-29, 2^-31, 2^-39 | #1612 |
| Nesting decided by parity where a nested outward shell is possible (#1566) | `MeshWindingNumberSolidify` and `MeshWindingNumberBoolean` read an outward inner shell as a cavity (volume 7 against 8, Compute true, clean); same cause in `MeshWallThickness`, `MeshRepairMetrics` | #1609 (fix belongs to the owner: `MeshPostRepairCleanup.FindInvertedComponents` / `WindingContouring.OrientedCopy` are the shared root) |
| A refusal or failure of a callee that the caller turns into Success (#1588, #1567) | none | |
| The raw input as the reference of a check (#1548) | none beyond #1609 (its reference is the oriented copy, the same reading as the result) | |
| Success with a result that fails `NotCleanReason` or the documented contract (silent-success audit) | none beyond the two above | |

Two confirmed defects in about the time box; the three other classes were searched with tools (below) and gave no failing repro.

## 2. Class #1590: platform libm

**Searched.**
- Every `Math.Sin / Cos / Tan / Exp / Pow / Log / Log10 / Atan / Atan2 / Asin / Acos / Sinh / Cosh / Tanh / Cbrt` call outside tests,
  benchmarks, tools and the Clipper2 add-on (grep with a word boundary, so `CoreMath.Sin` does not match): 75 files. The scope of the
  analyzer G3E0004 (`.editorconfig`: `TerrainSlope*`, the geodesic family, `SparseCholesky`, `CholeskyNestedDissection`, `math/CoreMath`) has no
  direct `System.Math` transcendental call.
- `tools/DeterminismReach tier` on the master build: 78 listed types, 158 documented public types, 83 of them without a libm edge (`cha`); the
  6 documented types without an edge that are not listed (`BooleanVolumeGradient`, `ExactPoint3`, `ExactTriangleIntersection`, `Face`,
  `MeshRepairOrientation`, `SolidBooleanRouteStep`) are only a list gap, not a defect. `--why` for `IntrinsicCommonSubdivision`,
  `PolyhedralGeodesicPath`, `MeshGeodesicBezier` and `SparseCholesky`: no libm edge on `static` or `cha`, as `docs/guides/geodesics-and-uv.md` claims.
- Documentation claims of bit identity across platforms (`docs/guides`, `docs/OPERATIONS.md`): the geodesic family (`CoreMath`, checked above), the
  exact Boolean (F2, an argument is listed), the 2^k statements (a different claim: invariance on one platform).

**Confirmed.** #1612: `Math.Log(x, 2)` is not exact at powers of two, so `Math.Floor(Math.Log(extent, 2))` is the exponent minus one for 221 of the
2098 powers of two on glibc (in -60..0: -59, -58, -55, -51, -47, -39, -31, -29), and `Math.Ceiling(Math.Log(x, 2))` is the exponent plus one for k in 29, 31, 39, ...
(and `Log(2^k + 1, 2)` is exactly k for many k). `FoldoverFreeParameterization.PowerScale` normalizes the UVs and the surface by it, then runs
with absolute constants, so an exactly twice larger problem gives another result: iterations 14 against 27, energy 6.156 against 3.314, 49 of 81 UVs differ
from the 2^-28 run scaled by a power of two, at the three extents listed above and at no other of 14 measured (2^-40 .. 2^2). The set of bad extents depends on
the platform libm.

**Unconfirmed (same idiom, no result-visible effect measured).**

| Site | Use | Why not confirmed |
|---|---|---|
| mesh_ops/MeshEnvelope.cs:152, 164 (`length`, `unit`) | `e = Floor(Log(m, 2))`, then `ScaleB(v, -e)`, `ScaleB(len, e)` | a scaling by an exact power of two, undone or normalized again: bit-neutral except at overflow and subnormal edges |
| mesh_ops/QuadFlowRepair.cs:1183 (`ScaleUnit`), mesh_ops/MeshSheetExtension.cs:474 (`sc`) | the same | the same |
| mesh_generators/TpmsLattice.cs:635 | `s = 2^Ceiling(Log(maxCoord, 2))`, then `while (s < maxCoord) s *= 2` | the loop corrects only a too small `s`; an exact power of two with k >= 29 gives a twice larger `s` and `enclosureUnit`. Needs an `Enclosure` grid whose largest coordinate is exactly 2^29 or more times a cell: no input built in the time box. TpmsLattice differs between libm families already (research note DETERMINISM_CROSS_RUNTIME) |
| mesh_ops/WindingNumberOctree.cs:140 (`RequiredDepth`), mesh_ops/WindingContouring.cs:668, 709 | integer depth or level count from `Ceiling(Log(ratio, 2))` | first bad k of the ceiling is 29 (a ratio of 5.4e8 cells) on glibc: out of reach; other libms were not measured |
| approximation/PrimitiveFit3.cs:442 | RANSAC iteration count from `Log` | documented "Deterministic for a given Seed", not across platforms; a one-iteration difference changes the result only across libm families |
| spatial/QuadtreeGeometry2d.cs:359, spatial/TriangleHashGrid.cs:56, mesh_ops/MeshOffset.cs:155 | `Math.Pow(2, integer)` | exact on every libm that was measured (a power of two result) |
| `Math.Cos(angle * Deg2Rad)` thresholds (Reducer.cs:1528, MeshTopology.cs:119, TerrainSafety.cs:46, MeshAlphaWrap.cs:1590) | a comparison threshold | the threshold is a user input and not a pinned result; a one-ulp difference only moves a decision for an input exactly on the threshold |

**Where a tool or a test would catch it.**
- A grep-level analyzer rule: `Math.Floor / Ceiling / Round / (int)` applied to `Math.Log(x, 2)` or `Math.Log2` (all sites above, and a future one). Proposal for the
  coordinator, not added. The remedy is an exact exponent helper (the bits of the double; `Math.ILogB` is not in netstandard2.0).
- `DeterminismReach` reports reachability, not the use of the value. A second report line "libm result converted to an integer or a power of two" (`Floor`, `Ceiling`,
  `Round`, `conv.i4` on a value that comes from a libm call in the same method) would list these sites.
- A generic test per scale-normalizing site: the operation at an input extent of 2^k for every k in [-1074, 1023] (or a sample of the ones the libm gets wrong:
  -29, -31, -39, 29, 31, 39) against the 2^(k+1) run, bit for bit. The `Similarity.All()` set (`Harness/Invariance.cs`) contains no scale at these extents; adding a power-of-two
  extent grid to the harness catches the class for every operation with `Compute()`.

## 3. Class #1566: nesting by parity

**Searched.**
- Every use of `FindInvertedComponents` (the parity reading: `MeshPostRepairCleanup.cs:458`, a shell inside an odd number of others is a cavity):
  `MeshPostRepairCleanup.Apply` (reported: `FlippedComponents`), `WindingContouring.OrientedCopy` (used by `MeshWindingNumberSolidify`, `MeshWindingNumberBoolean`,
  `MeshRepairMetrics`, `MeshRobustRepair`), `MeshWallThickness.InvertedTriangles`, `MeshRoundingRepair` (flagged inverted candidates), g3cli `MeshReport`.
- The other parity or even-odd decisions: `MeshSignedDistanceGrid` and `CachingMeshSDF` (`ParityCount`, default), `RemoveOccludedTriangles` (`RayParity`, default),
  `MeshOffset` (exact ray parity), `ConstrainedDelaunayMesh2` and `QualityMesh2` (ring parity, D5; read from their comments, not probed); `PlanarComplex.FindSolidRegions` (documented containment, not probed).
- Probe input of #1566: an outer cube (outward, volume 8) and an inner cube (also outward, half size 0.2, 0.5, 0.8) in one mesh.

**Confirmed.** #1609: `MeshWindingNumberSolidify` (Compute true, `NotCleanReason` "") gives volume 7.936, 7.000, 3.904 for the three inner sizes (solid: 8), `MeshWindingNumberBoolean`
union with a far unit cube 8.936, 8.000, 4.904 (solid: 9, as `ExactMeshBoolean`). Same input, same reading, other callers: `MeshWallThickness` (`InscribedSphere`
minimum 0.5 against 0.667 with `OrientInvertedParts = false`), the `MeshRepairMetrics` reference. Owner area: the reading rule is the one of #1566 / #1579.

**Not defects (documented parity semantics, no hidden reference).** `MeshSignedDistanceGrid.InsideModes` and `RemoveOccludedTriangles.CalculationMode` document the
even-odd rule and offer the winding number; `MeshOffset` states "exact ray-crossing parity ... only meaningful for such meshes" (the nested outward input gives
volume 10.082 at distance 0.1, the union reading about 10.65; documented); `ConstrainedDelaunayMesh2` ring roles are given by the data; `MeshPostRepairCleanup.Apply` reports `FlippedComponents`.
`MeshAutoRepair` does not flip the inner cube (volume 9 kept).

**Where a tool or a test would catch it.**
- A harness input, "outer cube and an outward inner cube", for every public operation that takes a closed mesh and returns a solid or a measure of the solid: the result volume
  must equal the winding-number solid (8) or the operation must report the reorientation. One fixture catches #1566, #1609 and any later caller.
- A review trigger (`tools/review_triggers.py`): a new call of `FindInvertedComponents` or `OrientedCopy` outside the callers above names class 1 of the checklist.

## 4. Class #1588 / #1567: a refusal or failure that the caller ignores

**Searched.** A Roslyn scan of the library sources (not committed; a 90-line program over `Microsoft.CodeAnalysis.CSharp` from the SDK, a semantic model without the
references of the add-ons) for expression statements that discard a `bool`, an enum named `*Status`, `*Result` or `*Code`, or a `*Result` / `*Status` object: 351 sites. 32 are
`Dictionary.TryGetValue`, and most of the rest are the `DMesh3` / `MeshEditor` editing primitives (`RemoveTriangle`, `RemoveVertex`, `ReverseTriOrientation`, `SmallListSet.Remove`: the best effort
of upstream geometry3Sharp, not read one by one). The about 25 sites that discard the result of an operation of this library (`Compute`, `Apply`, `Fill`, `NearestPointBatch`) were read in context. Also a scan for a local that
takes such a result and never reads it: 3 sites. And the #1567 shape (a drop rule relative to a global size): the doc comments that name a drop or a removal of components.

| Site | What is ignored | Why it is not a defect |
|---|---|---|
| mesh_ops/MeshPlaneCut.cs:246 | `MeshBoundaryLoops.Compute()` | documented "always returns true"; `CutLoopsFailed` is set from the exception path |
| mesh_ops/MeshExtrudeFaces.cs:82 | `MeshEditor.SeparateTriangles` (`bOK` never read) | the method returns true on every path |
| mesh_ops/SolidBooleanPipeline.cs:1956, MeshRobustRepair.cs:993 | `MeshAutoRepair.Apply()` | returns false only when cancelled; the callers check closedness or `NotCleanReason` of the mesh |
| mesh_ops/SolidBooleanPipeline.cs:1962 | `MeshRobustRepair.Apply()` of the opt-in `AutoRepairThenRobust` | the mesh is accepted if `IsWindingClosed`; a refused volume or lost part (`Apply()` false with a closed mesh) would pass. Not reproduced: reaching it needs an input that `MeshAutoRepair` neither closes nor leaves self-intersecting and that the robust repair then refuses; owner area (`SolidBooleanPipeline`), listed for the owner |
| mesh_ops/PointCloudNormals.cs:356, MeshRobustRepairLostArea.cs:126 | `BatchStatus` of `NearestPointBatch` | the options have no cancellation token and the buffers are sized by the caller: the statuses `Cancelled`, `BufferTooSmall`, `MeshModified` are unreachable |
| mesh_ops/QuadRemesher.cs:278, MeshSheetExtension.cs:1260, ExactMeshBoolean.cs:733, 1063, ExactMeshImprint.cs:493, TerrainSlopeDesign.cs:662, 691 | `Compute()`, `Apply()` | the status or the diagnostic fields are read on the next lines (`cf.Success`, `op.Status`, `Rounding.IsClean`, `r.Status`) |
| mesh_ops/MeshAutoRepair.cs (about 19 internal calls), MinimalHoleFill.cs:196 (`FlipEdge`) | stages of a best-effort pipeline | the result is the mesh, checked at the end (`Mesh.IsClosed()`, documented) |
| drop rules relative to a global size: `MeshRoundingRepair.remove_tiny_components` (relative to the largest component's diagonal), `MeshRobustRepair.CollapseSlivers`, `SolidBooleanPipeline` inside-out drop (#1567) | a real small or thin part can be removed | the first two are owner decisions with a counter and a documented reason (`RemovedComponentInfos`, "a rod of length 1e8 makes a unit part tiny"); an outward plate 10 x 10 x 0.01 next to a box of half size 5, 500 and 50000 is kept by `ExactMeshBoolean` and `SolidBooleanPipeline` (volume equals the sum; at 50000 the double sum of volumes has a 2 error of its own) |

**Where a tool or a test would catch it.** The scan itself is cheap (a Roslyn analyzer, severity warning, on an expression statement of an invocation whose return type is `bool` or a `*Status` / `*Result`
enum, in `mesh_ops/`, with the allow list of the upstream editing primitives): proposed to the coordinator as a candidate for the `G3E` family, not added. A test for the pipeline shape: a stub callee that refuses
(`Apply()` false, closed mesh) in each wrapper that calls `MeshRobustRepair` or `MeshAutoRepair`.

## 5. Class #1548: the raw input as the reference

**Searched.** The uses of `OrientedCopy`, `OrientedReference`, `oriented_by_reference` (MeshRobustRepair, MeshRepairMetrics, MeshFidelityMetrics) and the checks of the operations that name
the input as their reference (`volume_check`, `ResultVolumeIoU`, `TopologyCertified`, `MeshDeviation`). After #1579 the reference of `MeshRobustRepair` is the oriented, welded copy and the nesting
reading is refused; `MeshWindingNumberSolidify.TopologyCertified` uses the oriented copy since #935. The remaining reference that shares the reading of the result is the one of #1609 (the
oriented copy flips by parity; `MeshRepairMetrics` volume IoU of the hollow result against the hollow reference is 1). No other check was found that derives its reference by the rule it checks;
`MeshDeviation` (certified interval, `Converged` dropped), `TerrainSafetyLines` Guaranteed (Success with `Certified` false), `MeshOffset` (the band is one-sided) and `IntrinsicCommonSubdivision` (area sums that can
cancel) are the unprobed leads of `docs/qa/silent-success-audit.md` section 3.8; they were not probed here either (no repro built in the time box), nor was `MeshFidelityMetrics` read beyond its comments.

## 6. Other checklist classes (what was looked at)

Only greps and the scans above; no class was searched in depth beyond sections 2 to 5.
- Class 6 (integer overflow, sizes): a grep of the casts `(int)Math.Floor / Ceiling / Round(...)` outside tests (12 sites, read): `HeightGrid.Height` is bounded by the grid before the cast, the
  `Log` ones are in section 2, the others are counts from `Sqrt` or a fraction; no overflow reachable from an input. Casts of the form `(int)(x / cell)` were not enumerated.
- Class 5 (range) and class 9 (ordering, determinism): through the `DeterminismReach` run of section 2 and the 2^k probe of #1612.
- Classes 3, 4, 7, 8, 10 to 12: not searched in this session.

## 7. Method notes for a next round

- A semantic Roslyn scan finds in minutes what a grep cannot (the return type of the callee); `tools/` has none. A candidate for a small checked-in tool.
- The 2^k grid of input extents (every binary exponent) is a cheap probe for any `Compute()` that normalizes by a power of two; it found #1612 with a 14-line loop.
