# Test-gap audit (2026-11-25)

Back to [QA Hub](README.md) · [docs/QA.md](../QA.md)

---

Consolidation phase (owner 2026-11-25). The audit looks for real gaps in the unit tests of four areas. For each area it checks
which promises have no test, which edge cases deserve one, and which existing tests are weak. It follows the stop rule of
[AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md) section 3: only gaps with a realistic failure path count, and no tests are added
just for form. Everything else stays read-only: except for the fix in section 6, no test is added or changed here. The
coordinator routes the rows to the next PR of each area.

Not repeated here: the silent-success audit (`docs/qa/silent-success-audit.md`, draft PR #1568, "success reported on a bad result":
#1566, #1567, #1548, #1479, #1465, PR #1488); the value tests for the bound and box computations of the exact Boolean
(PR #1492, backlog #1423); exact winding (#1427, PR #1500); the SN-5 bound of `SnapRounding2` (PR #1506); `Remesher.MinTriangleQuality` (#1491).

## 1. Method

1. **Contract against tests.** For each area: what the guide (`docs/guides/`) and the XML docs promise, mapped to the
   tests that would fail if the promise broke.
2. **Weak tests.** Tests that check only a status, pins so loose that a wrong result passes, inputs that never reach
   the branch a test names, duplicates.
3. **Edge-case classes** of [REVIEW_CHECKLIST.md](../REVIEW_CHECKLIST.md): degenerate, empty, single element,
   coordinates 1e8..1e15, 2^k and `Similarity.All()`, open and non-manifold input, tangent contact, determinism, reuse.
4. **Hand mutations.** Where reading suggested that a library line is not pinned, the line was broken by hand in a local
   build (never committed), and the tests of the area were run against it (net10.0 Release,
   `--filter "<area>&Category!=Nightly"`). Section 6 lists the results; a row marked **(H*n*)** was confirmed this way.
   No Stryker shard was run. The repository has no survivor lists for the predicates and fixed-width shards (PR #1458
   sets their thresholds only), so the mutation data used is the open-items list of `tests/mutation/README.md`.

Columns: **cost** is S (under 0.5 s of test time, a few lines), M (about 1-2 s, or the input must first be found),
L (no procedural input known). **Verdict** "worth it" means: the area's next PR should add it.

## 2. Exact Boolean (`ExactMeshBoolean`, `ExactMesh*`) and `SolidBooleanPipeline`

The area is tested very densely, often bit for bit. Examples: the grid step (`SolidBooleanPipelineGrid*Tests`),
`PairContactsMode` (`SolidBooleanPairContactsTests.cs:116-300`), budget and cancellation with reuse after a cancel
(`ExactMeshBooleanTestsBudget.cs:193-266`), `IntersectEach` / `DifferenceEach` equal to separate runs
(`ExactMeshBooleanNaryTestsEach.cs:69,194`), and strict refusal leaving `Result` null (`SafeRoundingBoundTests.cs:398`).
The gaps are in the pass-through of options to the reducer and in a few promises of the docs.

| Gap or weak test | Realistic failure path | Proposed test or change | Cost | Verdict |
|---|---|---|---|---|
| B1. The guide promises that dropping a thin inside-out component works "from a copy, the caller's mesh is not changed". No test compares the input after `Compute`; `ThinInsideOutLens_Compute_Twice_SameResult` checks the result only. | Without `InputGrid`, `DropThinInsideOut` gets the caller's meshes (`SolidBooleanPipeline.cs:975`); (under H2) `var copy = mesh` at `:1484` edits them in place. **(H2)**: killed, but only by `TheDroppedLens_IsNamedInTheReport`, so the promise is pinned incidentally, not directly. | In the compute-twice test: `Invariance.Difference(before, input) == ""`, and a single dropped component on the second run too. | S | worth it, low priority (pinned incidentally) |
| B2. `MaxDeviationTwoSided` has no test of its effect through the pipeline; `SolidBooleanPairContactsTests.cs:278-280` compares two runs that are both two-sided. | The flag at `SolidBooleanPipeline.cs:2031` is dropped and two-sided turns silently into one-sided. **(H3)**: the flag forced to false survives all 359 `SolidBoolean*` tests. | One-sided against two-sided on `TwoSpheres` with `MaxDeviation` set: they differ; the two-sided distance (`MeshFidelityMetrics`) from the boolean to the result is at most `MaxDeviation`. | S, ~1 s | worth it |
| B3. `MinEdgeLength` and `MaxQuadricError` are pinned loosely: `SolidBooleanPipelineTests.cs:625-638` checks only "fewer triangles", `:883-889` only that the mesh is exhausted. | A unit or scale slip in the value passed to the reducer (`:2043-2044`) still reduces something. **(H4)**: the value passed as half survived all 359 `SolidBoolean*` tests; killed by the fix of section 6. | Done for `MinEdgeLength` (section 6). Still open: the same for `MaxQuadricError` (a bound on the quadric error of the result). | S | worth it (half done) |
| B4. No serial-against-parallel test of the pipeline with a reduction; the one serial run (`SolidBooleanPipelineTests.cs:381-388`) is the sheet route without a target, `:785` only repeats parallel runs. | The reducer's parallel loops (`mesh/Reducer.cs:1339,1492,1505,1616,2279`): a reduction across threads makes the bits depend on the schedule; repeated runs see it only by chance. CLAUDE.md asks for this test. | `gParallel.SerialOnThisThread` against parallel, with `TargetFraction = 0.25`, once with `PairContactsMode = Auto` on `EdgeTouch(6)`. | S, ~1 s | worth it |
| B5. `SafeRounding = false` promises the exact vertices rounded to nearest; the only check (`ExactBooleanSurvivorTests.cs:103-106`) has dyadic coordinates, which are exact either way. | A speed change returns the filter approximations instead of `arr.PointsToVectors` (`ExactMeshBoolean.cs:6112`): 1-2 ulp off on curved inputs, invisible to the 2^k and volume checks. | Box minus sphere, `SafeRounding = false`, `ComputeExactVertices = true`: each output vertex equals `ResultExactVertices[v].ToVector3d()` bit for bit. | S | worth it |
| B6. Whole inputs far from the origin (1e8..1e15) are not tested: `Similarity.All()` reaches 5.7e6 (`Harness/Invariance.cs:59`), the property frames 2^12, `ExactBooleanFarComponentTests.cs:50` moves one small part. | At 1e12, 256 ulp is about 0.03: the ulp term of the rounding repair and the 1e-12 degenerate-area check take over and can contract real features. | Sphere union box and sphere minus box, translated by 1e9 and 1e12: clean (`NotCleanReason == ""`), volume within the rounding bound of the unit-frame result. | M, 1-2 s | worth it |
| B7. The reuse test of `ExactMeshBoolean` (`ExactBooleanReuseTests.cs:24-33`) compares the volume to 12 digits and a few counts, and never changes options between calls. | The reset block (`ExactMeshBoolean.cs:821-866`) lists about 60 fields by hand; a new diagnostic field missing there keeps stale state. | Compute with options C1 then C2 on one instance; compare all public fields and properties with a fresh C2 instance (reflection) and the mesh with `Invariance.Difference`. | S | worth it (guards future fields) |
| B8. An empty operand through `Operation` is not tested (only an empty list, `ExactMeshBooleanNaryTests.cs:464`, and the lazy route, `LazyExactMeshTests.cs:324`). | An empty A goes through `CullOutsideA` and the bounds code with zero triangles; a later change throws there. | (empty, A), (A, empty), (empty, empty) for the three operations: Success, an empty result or exactly A. | S | worth it (cheap) |
| B9. The pipeline's invariance tests (`SolidBooleanPipelineTests.cs:803-821`) scale only with `TargetFraction`, never with the absolute criteria. | A part of the reducer that does not scale with the input breaks 2^k invariance once the criterion is absolute. | 2^k frames with `MinEdgeLength * s` and `MaxDeviation * s`, bit for bit. | S | worth it, low priority (the reducer's own tests own most of it) |
| B10. `Report.InsideOutUndecided` has no test. | Reached only after 200 failed ray attempts. | none | L | not worth it: no procedural input |
| B11. `AtLeast(k <= 0)` (empty result) has no test. | `return k <= 0` changed. | none | S | not worth it: trivial code, no plausible change |
| B12. The `StrictRounding` message tests pin part of the text only. | Wording drift. | none | S | not worth it: the status is pinned, the text is a diagnostic |

**Edge-case classes.** Degenerate input: covered (`SafeRoundingDegenerateInputTests`, `DefectInputTests`). Empty: partly
(B8). Single element: covered (`ExactMeshBooleanNaryTests.cs:458`, `SolidBooleanPipelineTests.cs:727`). 1e8..1e15: gap
(B6); only 2^±400 (`ExactBooleanCoverageRound2Tests.cs:419`) and single far parts. 2^k and `Similarity.All()`: covered
(about 50 files) except B9. Non-manifold and open: covered (`ExactBooleanBowtieSplitTests`, `ExactBooleanOpenInputTests*`,
`ExactBooleanSheetTests`). Tangent and coplanar: covered (`ExactBooleanInputContactsTests`,
`ExactBooleanDeterminismTests.cs:211,229`). Determinism: covered for the boolean, the Each variants and lazy chains; gap
for the pipeline with a reduction (B4). Reuse: pipeline covered; `ExactMeshBoolean` weak (B7).

## 3. `MeshRobustRepair` and `MeshAutoRepair`

Well tested: the working frame, 2^k and `Similarity` invariance, the pass-through, the lost-part, lost-area and
scale-disparity checks, reuse, the far-part serial and parallel runs, and the wrap and solidify fallbacks with their
reasons. The gaps are an unpinned default threshold and three `MeshAutoRepair` features without any test.

| Gap or weak test | Realistic failure path | Proposed test or change | Cost | Verdict |
|---|---|---|---|---|
| R1. The default `MinVolumeIoU` (0.99) is not pinned between IoU 0.67 (`MeshRobustRepairTests.cs:499`, rejected) and 0.997 (`:674-676`, accepted). The threshold is used at `MeshRobustRepair.cs:1328` (repair check) and `:1767` (exact-resolve check). | A regression of the threshold or of the early exit of `VolumeIoUAtLeast` accepts a repair that lost 2-10 % of a connected solid; the lost-part check does not see it (no separate part). **(H1)**: the threshold lowered to 0.9 was killed by one test only, `TwoOpenCubesAt1e23_AreRepairedWithAFewHundredWrapTriangles`, which is about wrap triangles, not volume. | `RepairF` returns a box with a 4 % slab cut off (connected): `FallbackReason` "volume differs" and `UsedExactResolve`; with `MinVolumeIoU = 0.9` it is accepted. | S | worth it |
| R2. `MeshAutoRepair.RemoveMode` Interior / Occluded has no test (the only user is `benchmarks/MeshRepairBenchmark/Program.cs:337`; dispatch at `MeshAutoRepair.cs:660-669`). | A public 1.0.324 option. A swapped mode or a wrong relative `normalOffset` (`:122`) removes the wrong triangles or none. **(H9)**: Interior dispatched as Occluded survives all 1678 repair tests. | A sphere with a nested inner sphere: Interior removes the inner one, None keeps it, 2^k scales give the same count. | S | worth it |
| R3. Erosion (`ErosionIterations`, `MeshAutoRepair.cs:366-372`) has no test; the only mention asserts that it does not run (`MeshRepairSeamTests.cs:265`). | A change of the loop count or an erosion that keeps going deletes input while `Apply()` is true (documented, but its extent is not pinned). | Find a `Defects` case that reaches erosion (`OnStage`); assert at most `ErosionIterations` erosion stages, none with 0. | M (finding the input) | worth it only if a `Defects` case reaches erosion |
| R4. `DefectInputTests.cs:100-108` (`MeshAutoRepair_DefectInput`) checks only that the mesh is structurally valid: an empty or fully eroded result passes. | A tolerance bug collapses whole components (it happened: `MeshAutoRepairToleranceTests.cs:201`). | Per case: closed where the repair can close it, and at least 0.9 of the input triangles on cracks and T-junctions. | S, no extra runs | worth it |
| R5. `DefectInputTests.cs:121-124` checks `NotCleanReason` only when `ResultReason == ""`. | An unclean result (solidify, failed wrap) whose `ResultReason` does not describe it; only `MeshRobustRepairTests.cs:1235` checks the match. | `else Assert.Equal(NotCleanReason(r.Mesh), r.ResultReason)` (or `StartsWith` for the lost-part prefixes). | S, no extra runs | worth it |
| R6. `OpenMesh_ClosedBySolidifyOrRepair` (`MeshRobustRepairTests.cs:488`) accepts either path and checks status and cleanliness only. | A capped sphere silently rebuilt by the coarse solidify instead of hole-filled still passes. | Pin `ResultPath == Repair` and the volume within 1 % of the filled sphere. | S | worth it |
| R7. Cancellation through `MeshAutoRepair.Progress` (`:214`) has no test. | Only an external caller is affected; `MeshRobustRepair` does not forward `CancelF` to it (documented, `MeshRobustRepair.cs:119`). | none | S | not worth it: no library caller |
| R8. No cancel during the exact resolve or the solidify. | The cancel checks are redundant (`MeshRobustRepair.cs:1047,1081,1178` and the solidify's own): removing one still gives false. | none | S | not worth it: kills no single mutant |
| R9. `Meets` of the pass-through (`MeshRobustRepairPassThrough.cs:66-69`) is tested for touching boxes on x only. | `>=` to `>` on y or z survives, but `MeetsAny` and the whole-union `NotCleanReason` fallback (`MeshRobustRepair.cs:808-831`) still send a real contact to the repair. | none | S | not worth it |
| R10. `MeshAutoRepair` serial against parallel is not tested, only repeated runs (`MeshAutoRepairToleranceTests.cs:216`). | The parallel loop writes evidence per triangle and sums it serially (`MeshRepairOrientation.cs:369-399`). | none | S | not worth it: no cross-thread reduction |
| R11. The full `Apply()` on the solidify path is tested under 2^k shifts only (`SolidifyScaleInvarianceTests.cs:78`). | The solidify itself is covered under `Similarity.All()` (`InvarianceTests.cs:399`); the repair only adds checks on top. | none | M, 20-40 s | not worth it: the cost, and the risk is covered |

**Edge-case classes.** Empty: covered (`DefectInputTests.cs:115-117`, `MeshRobustRepairLostAreaTests.cs:168`). Single
triangle, zero area, flat, open sheet, fin, bowtie, soup, duplicates: covered by the `Defects` cases
(`Harness/Defects.cs:105-216`), but only to "clean when reported clean" (R4, R5). 1e8..1e15 and beyond: covered
(`HugeCoordinatesRepair*Tests`, `RobustRepairInputRangeTests.cs:25-74`, `MeshRobustRepairTinyInputTests.cs:94` at 1e23).
2^k and `Similarity.All()`: covered (`RobustRepairFrameTests.cs:64-101`, `RobustRepairPassThroughTests.cs:357-382`,
`InvarianceTests.cs:366`); solidify shifts only (R11). Non-manifold and open: covered (`MeshRepairSeamTests`; R6 is weak).
Tangent contact: covered (`MeshRobustRepairTests.cs:756`, `MeshRepairIssueCasesTests.cs:433`). Determinism: covered
(`RobustRepairScaleDisparityTests.cs:48`, `MeshRobustRepairTests.cs:1142,1162`). Reuse: covered (`RobustRepairFrameTests.cs:117`,
`RobustRepairPassThroughTests.cs:264`, `MeshAutoRepairToleranceTests.cs:184`).

## 4. Exact predicates and arithmetic (`math/Exact*`, `math/AdaptivePredicates.cs`, fixed-width and generated paths)

Strong: differential tests per path against `BigInteger`, thorough subnormal and underflow tests, the generated kernels
pinned with 27 hand mutants, 2^k invariance per path. The gaps are at the edges of ranges and in the size of error bounds,
where sign-agreement tests cannot see a change.

| Gap or weak test | Realistic failure path | Proposed test or change | Cost | Verdict |
|---|---|---|---|---|
| P1. The filter error bounds of `ExactPredicates` (`ExactPredicates.cs:21-24`) are not tested for size; tests check that signs agree. `Orient3dFilterFull` uses the library's own constant (`ExactPredicatesTests.UnderflowTerm.cs:13,24`); `Bound_ShortcutPreconditions_Hold` checks only B >= 2^-51. `tests/mutation/README.md:371-376`: random inputs killed none of the 74 bound survivors. | A smaller bound (a "tightening" for speed) lets the filter decide wrongly on worst-case inputs. **(H5)**: `Orient3dBound` 7 eps to 4 eps survives all 522 predicate tests. | An internal hook returning (det, bound) per filter: assert \|det_fp - det_exact\| <= bound on adversarial inputs (the `ExactFilterBoundTests` style), and a floor on the largest observed error / bound, so a bound shrunk 2x fails. | M | worth it |
| P2. The edges of the private `Expansion.in_range` (`ExactPredicates.cs:701-716`, 2^-100..2^100) are untested. The path is valid to about 2^-190, so widening the range is caught only by inputs near 2^-(190..260); existing inputs reach 2^±140 with 2-bit mantissas (`ExactPredicatesTests.cs:583`). | A retune of the range for speed: InCircle or Orient2d near 2^-250 or 2^255 gets a subnormal or overflowing product in stage D and a wrong sign. **(H6)**: `MinMagnitude` 2^-100 to 2^-250 survives all 549 predicate and expansion tests. | `in_range` true at ±2^-100 and ±2^100, false one ulp outside; near-degenerate cases with full mantissas (generator of `StageD_ExactlyDegenerate`, `:283`) at 2^±{99,100,101,150,190,250,255} against `*_bigint`. | S | worth it |
| P3. Non-finite input: the coincidence shortcuts (`ExactPredicates.cs:194,227,237,247`) return 0 for repeated infinite points before any check, while `Orient2d(inf, ...)` throws (`ExactPredicatesTests.cs:473-478`); InCircle and InSphere are not tested. The contract was not documented. | A mesh with a duplicated ±inf vertex is treated as degenerate and silently gives 0 instead of throwing. | **Decided (owner 2026-11-25):** all four exact predicates throw on NaN or ±inf, before any coincidence shortcut. Follow-up PR: the check ahead of the shortcuts, the XML docs, tests of all four with NaN, ±inf, repeated inf and inf mixed with finite values. Exact code: its own mutation gate. | S | worth it (decided) |
| P4. The merge order of `AdaptivePredicates.sum` (`:294,297`) is an open item of `tests/mutation/README.md:415-420`; no test builds cancelling expansions of very different magnitudes. | `sign()` reads the last component (`:406`); an unsorted or overlapping expansion can give a wrong sign for a total near 0. | An internal hook on `sum` and `scale`: random expansions (2^-150..2^100, exact cancellations); output sorted, non-overlapping and exact against `BigInteger`. | S | worth it |
| P5. The `fixed` shard has the lowest gate of the area (break 81, `stryker-mesh-fixed.json`), and no survivor list is in the repository. | Unknown. | Read the survivors of the latest weekly run before writing tests. | S | worth it, after the data |
| P6. `AdaptiveStages_DecideNearlyDegenerateCasesCorrectly` (`ExactPredicatesTests.cs:231-277`) bounds only how often stage D is reached; nothing asserts that stage C ever decides. | Stage C mutants cost only time; wrong C decisions are caught by the exact-zero tests (`:283`, `:350`). | none | S | not worth it: guards speed only |
| P7. The `ResultErrBound * \|det\|` term of the stage C bounds (`AdaptivePredicates.cs:65,122,196`) is not tested alone. | Only constructed worst cases reach it (at most 6 eps² against 9 eps² of the constant). | none | M | not worth it: equivalent in practice |
| P8. Permutations: one swap or rotation per predicate (`PredicateDifferentialTests.cs:53-123`); the reference point of InCircle and InSphere never moves. | Every path is exact; a permutation can only fail through a path bug, which the oracles catch. | none | S | not worth it: little extra power |
| P9. No concurrency test for the `[ThreadStatic]` workspace (`AdaptivePredicates.cs:410-412`). | It becomes a plain static. | none | S | not worth it: covered by `ExactBooleanDeterminismTests` and `Traced_SerialEqualsParallel` |
| P10. The capacity of the stage D buffers (`:420-424`) is never reached. | none | none | | not worth it: bounded by construction |

**Edge-case classes.** Exact zero: covered (`ExactPredicatesTests.cs:283,350,403`, `ExactPoint3FixedWidthTests.cs:262`,
`GeneratedPredicatesIndirectTests.cs:198`, `ExactOrient3dKernelTests.cs:60`). Near zero needing the exact stage: covered
(`PredicateDifferentialTests.cs:40-158`); the size of the bound is P1. 1e8..1e15: covered (2^40 offsets, the UTM
similarity). Range limits: covered for `orient3d_int`, the generated kernels and DoubleWord; the edge of the expansion
path is P2. Subnormals: covered (`ExactPredicatesTests.UnderflowTerm.cs:90-183`). 2^k bit for bit: covered
(`ExactOrient3dKernelTests.cs:162`, `GeneratedDoublePredicatesTests.cs:406`, `GeneratedPredicatesIndirectTests.cs:911-961`).
Non-finite: partly (P3). Permutations: partly (P8). Determinism: analyzers, `PreparedPlaneSide_IsThreadSafe`
(`ExactLimbSignsTests.cs:203`).

## 5. Remeshing and 2D meshing (`Remesher`, `RegionRemesher`, `QualityMesh2`, `ConstrainedDelaunayMesh2`, `SnapRounding2`)

Strong: an exact oracle and a 66-row failure catalogue for the 2D meshers (`Meshing2CatalogueTests.cs:28`), CsCheck
campaigns, golden hashes, invariance, reuse and parallel tests on all three 2D classes; the Remesher's opt-in guards each
have invariance, parallel and reuse tests. The gaps are public options and option combinations without any test.
(`MeshingMeshes2D` does not exist in the repository; the 2D classes above are its parts.)

| Gap or weak test | Realistic failure path | Proposed test or change | Cost | Verdict |
|---|---|---|---|---|
| M1. `QualityMesh2`: a global `MaxArea` together with a smaller per-region `MaxArea` (`QualityMesh2.cs:1005-1007`) is never tested; `FuzzDomains` sets region bounds only (`QualityMesh2Tests.cs:58-77`). | `Math.Min` becomes `Max`, and region triangles exceed their own bound. **(H7)**: `Min` to `Max` survives all 3059 `*Mesh2*` tests. | Square with global `MaxArea = 1` and a region with 0.05: every region triangle <= 0.05 (1 + 2^-40) except those in `BadTriangles`, the rest <= 1. | S | worth it |
| M2. `Remesher.TargetEdgeLengthF` returning 0, negative or infinity is documented as "the global target" (`Remesher.cs:183`); only NaN is tested (`RemeshSizingTests.cs:110`). | A field that returns 0 outside its support: if the guard at `:196` regresses, `maxLen = 0` and every edge splits on every pass. **(H8)**: the guard reduced to NaN survives all 454 `Remesh*` tests. | Extend `Field_ShorterEdgesWhereItAsks...` with 0, -1 and +inf regions that keep the global length. | S | worth it |
| M3. `ProjectionMode = Inline` has no test (paths `Remesher.cs:942-944`, `:1846-1849`, `:2046-2049`). | The collapse guards and the self-intersection check get the inline-projected position (`:730`, `:753`); a broken wiring fails no test. | Cylinder and gear of `RemesherQualityTests` with `Inline` and `ApplySafePreset()`: no self-intersecting pair, vertices on the target, 2^k frames, parallel equals serial. | M | worth it |
| M4. `VertexControlF` (`Remesher.cs:251`) and `CustomSmoothF` (`:236`) have no test (used at `:2018-2020`, `:2074`, `:2096`, `RemesherPro.cs:625,740`). | Upstream 1.0.324 API; the fork's new paths (curve smoothing, buffered reverts, RemesherPro tracked passes) could ignore `NoSmooth` / `NoProject`. | Random vertices marked `NoSmooth` / `NoProject`, Remesher and RemesherPro: their positions stay bit-exact. | S | worth it |
| M5. The defect corpus runs only the default Remesher (`DefectInputTests.cs:381-389`), never `ApplySafePreset`, `RemesherPro` or `MinTriangleQuality`. | The guarded code (self-intersection grid, rollback copy, `ClosedComponentSigns`) on a fin or an empty mesh: an exception or a non-finite result goes unseen, and the preset is the recommended setting. | A theory over `Defects.Names()` x (pro, preset). | S | worth it |
| M6. `SnapRounding2` claims a result independent of the input order (`SnapRounding2.cs:756-757`, R11 at `:626-628`); no test permutes points or segments or reverses segments. `QualityMesh2`'s permutation test runs without snapping (`MetamorphicBugHunt2Tests.cs:984-990`). | A dictionary or hash-set order, or the restore choice, leaks the input order. | `Fuzz` inputs under a random permutation and with reversed segments: the same canonical set of chain edges. | S | worth it |
| M7. `SnapRounding2.MaxDisplacement` (documented <= half a pixel per axis, `:36`, computed at `:887`) is never asserted; it only enters a golden hash (`SnapRounding2IndexTests.cs:251`). | A re-pinned hash cannot tell a right value from a wrong one. | In `Check()` (`SnapRounding2Tests.cs:76`): `MaxDisplacement == max \|centre - p\|` and <= w/2. | S | worth it (cheap) |
| M8. `RegionRemesher` has one test (`RemesherIssueCasesTests.cs:334-377`): no invariance, no check that `BaseMesh` is unchanged before `BackPropropagate`, no `QuickRemesh`. | Border vertices move, or the back-propagation loses them. | Hash `BaseMesh` before `BackPropropagate`; then closed, manifold, border vertices bit-exact, 2^k frames. | M | worth it |
| M9. `QualityMesh2.SizeFunction` returning NaN, 0, negative or infinity was undocumented and untested (`QualityMesh2.cs:44-48`). | NaN makes the comparison at `:1016` false and skips size refinement silently; 0 refines up to the Steiner limit. | **Decided (owner 2026-11-25):** `ArgumentException` on NaN, 0 or a negative value; +inf means "no size limit here". Follow-up PR: the check, a doc line, a test of each value (and +inf equal to no size function in a region). | S | worth it (decided) |
| M10. `QuadRemesherTests.cs:440-442` asserts `st == NoFaces \|\| q4.Result != null`. | Any status with a result passes. | Assert {NoFaces, Success} and `CheckResult` on Success. | S | worth it, folded into the next QuadRemesher PR |
| M11. The `SnapRounding2` fuzz feeds segments only, no rings (`SnapRounding2Tests.cs:163-183`). | Ring routing breaks unseen. | none: covered by the mesher campaign `Grid` with snapping (`QualityMesh2PropertyCampaign.CsCheck.cs:295`), `BowtieRing`, `CollapsedRing`. | | not worth it (duplicate) |
| M12. `EnableSmoothInPlace` has no test. | Documented as unguarded and order-dependent (`Remesher.cs:24,73,144,278`). | none | S | not worth it: it promises nothing to check |
| M13. `PreventSelfIntersections` beyond about 1e10: the first round's box padding is an absolute 6e-6 (`Remesher.cs:145-147`); `Similarity.All()` reaches 6e6. | A touching pair is missed at those coordinates. | An issue rather than a test. | M | not worth it as a test now (documented, rare coordinates) |

**Edge-case classes.** Degenerate: covered (`DelaunayMesh2Tests.cs:137`, `SnapRounding2Tests.cs:368,439`,
`QualityMesh2SnapDegenerateTests.cs:55`, `RemesherInversionGuardTests.cs:256`). Empty and single element: covered for
the default Remesher and `QuadRemesher` (`QuadRemesherTests.cs:399`); the guarded options are M5. 1e8..1e15: covered in
2D (to 1e70, `DelaunayMesh2Tests.cs:107,120`; 2^-1000..2^500, `ConstrainedDelaunayMesh2Tests.cs:400-406`; the 2^40 pixel
limit, `SnapRounding2Tests.cs:220`); in 3D only ±2^20 (`InvarianceTests.cs:632`) and M13. 2^k and `Similarity.All()`: covered
for every class except `RegionRemesher` (M8) and the inline mode (M3). Non-manifold and open: default Remesher only (M5).
Tangent and collinear: covered (`SnapRounding2Tests.cs:123,311`, `QualityMesh2NearTJunctionTests`). Determinism: covered
(`RemesherDeterminismTests.cs:63-92`, `SnapRounding2IndexTests.cs:143,278`, `OrderAdversaryTests.cs:379`) except the input
order under snapping (M6). Reuse: covered (`QuadRemesherTests.cs:637`, `SnapRounding2Tests.cs:206,281`, `RemesherSafePresetTests.cs:208`).

Area 5 of the brief (alpha wrap, contouring and TPMS, parameterization) was not audited: out of budget.

## 6. Hand mutations and the fix in this PR

| # | Line (library) | Mutation | Tests run | Result | Row |
|---|---|---|---|---|---|
| H1 | `mesh_ops/MeshRobustRepair.cs:1328` | `MinVolumeIoU` to `Math.Min(MinVolumeIoU, 0.9)` | `Repair`, `DefectInput`, `Invariance` (2992) | killed by 1 unrelated test | R1 |
| H2 | `mesh_ops/SolidBooleanPipeline.cs:1484` | `new DMesh3(mesh)` to `mesh` | `SolidBoolean` (359) | killed by 2 rows of one test | B1 |
| H3 | `mesh_ops/SolidBooleanPipeline.cs:2031` | `MaxDeviationTwoSided` forced to false | `SolidBoolean` (359) | survived | B2 |
| H4 | `mesh_ops/SolidBooleanPipeline.cs:2044` | `ReduceToEdgeLength(0.5 * MinEdgeLength)` | `SolidBoolean` (359) | survived; killed after the fix | B3 |
| H5 | `math/ExactPredicates.cs:22` | `Orient3dBound` (7 + ...) eps to (4 + ...) eps | `Predicate`, `ExactPredicates`, `Orient`, `Adaptive` (522) | survived | P1 |
| H6 | `math/ExactPredicates.cs:703` | `MinMagnitude` 2^-100 to 2^-250 | as H5 plus `Expansion` (549) | survived | P2 |
| H7 | `comp_geom/QualityMesh2.cs:1007` | `Math.Min` to `Math.Max` (global and region `MaxArea`) | `QualityMesh2`, `Meshing2`, `Mesh2` (3059) | survived | M1 |
| H8 | `mesh/Remesher.cs:196` | `!(t > 0) \|\| IsInfinity(t)` to `IsNaN(t)` | `Remesh` (454) | survived | M2 |
| H9 | `mesh_ops/MeshAutoRepair.cs:663` | `RemoveModes.Interior` to `RemoveModes.Occluded` | `Repair`, `DefectInput` (1678) | survived | R2 |

Every run excludes `Category=Nightly`. H5 and H6 are in the exact code: a bound shrunk to 4/7 of its value, and a range widened
by 2^150, both pass the suite. That the filters of the predicates stay correct then rests on the published proofs alone, not
on a test. H5 is also a hand mutant of the error-bound class that `tests/mutation/README.md` says random inputs cannot kill.

**The fix in this PR** (one of the at most three allowed). `SolidBooleanPipelineTests.EdgeLengthAndQuadricCriteria_Reduce`
says that it tests the edge-length criterion, but it checked only "fewer triangles". It now also asserts
`StopReason == TargetReached` and that no edge of the result is shorter than `MinEdgeLength` (0.3; on master the shortest
is 0.3006 and no collapse is blocked). Mutation check: it passes on master and fails under H4 (the shortest edge is then
about 0.15). No other test was changed: the other rows are new tests or need a decision, which belong in the area PRs.

### 6.1 The coordinator's item: a `MeshICP` timing test that fails under load

**Done** (owner decision 2026-11-25, PR `qa/cost-invariance-floor`): `TimeBound` takes `max(20 x original + 50 ms, 0.5 s)`
(`TimeFloor`), and the runs of the original and the copy are interleaved for each similarity. The line citations below are of
master before that PR.

The test is `CostInvarianceTests.SweepOperation_Time_DoesNotDependOnTheFrame(opName: "MeshICP")` (`CostInvarianceTests.cs:166`,
`Category=Nightly`). The helper `Fastest` (`:149`) times the best of 3 wall-clock runs with a `Stopwatch`. `TimeBound` (`:45`)
allows 20 times the original's best plus 50 ms. The failure seen was "MeshICP under scale 2^-24: 7 ms vs 254 ms (limit 187 ms)". It
appeared only in unfiltered local full runs on a loaded machine, as did the rows of `MeshAutoRepair` and `MeshAlphaWrap.Points`; each
passes alone.

Why it fails: for an operation of a few milliseconds the limit is set by the 50 ms slack. One preemption or GC pause of a loaded
machine in each of the 3 runs is enough to exceed it. The baseline runs first and the copies later, so load that starts in between
hits only the copies. (The three 20 s watchdogs near `MeshICP`, `IcpAndRepairGuardTests:83`, `RobustnessSweepTests:331` and
`SymmetryInvarianceTests:111`, are not affected: `Timed.Finishes` already counts the thread's CPU time.)

| Gap or weak test | Realistic failure path | Proposed test or change | Cost | Verdict |
|---|---|---|---|---|
| T1. `SweepOperation_Time_DoesNotDependOnTheFrame`: the limit of a short operation is the 50 ms slack, which is load, not cost. | A false failure under load, seen for `MeshICP`, `MeshAutoRepair` and `MeshAlphaWrap.Points`; a nightly run on a busy runner can fail the same way. | The owner's proposal: an absolute floor on the limit, `max(20 x original + 50 ms, 0.5 s)`. What it costs: a frame-dependent slowdown of a 7 ms operation is caught only above 0.5 s (70 times). The class it guards (an iteration count that grows with the frame, a hang) is usually orders of magnitude, and C1 of the same class already pins the deterministic counts. Optional, with the same power: interleave the runs (original, copy, original, copy) so that load hits both sides alike. Not proposed: thread CPU time, because several operations (wrap, repair) work on pool threads the calling thread does not count. | S | done (PR `qa/cost-invariance-floor`): the floor and the interleaving |

## 7. Top 15 (risk times cost)

Ranked by the risk (how plausible the failure path is, and what a wrong result costs) against the cost of the test.
A row confirmed by a surviving hand mutant ranks above an equal row found only by reading.

| Rank | Row | Area | Why it ranks here | Cost |
|---|---|---|---|---|
| 1 | P1 filter bounds checked for size | predicates | exact code; H5 (bound 7 eps to 4 eps) survives; a "tightening" for speed is the plausible change | M |
| 2 | P2 edges of `Expansion.in_range` | predicates | exact code; H6 (range widened by 2^150) survives | S |
| 3 | P4 merge order of `AdaptivePredicates.sum` | predicates | exact code; an open item of the mutation README; a wrong sign near 0 | S |
| 4 | R1 default `MinVolumeIoU` pinned | repair | H1 killed only by an unrelated wrap test; a lost 2-10 % passes as clean | S |
| 5 | B2 `MaxDeviationTwoSided` has an effect | boolean | H3 survives; two-sided turns silently one-sided | S |
| 6 | M1 global and region `MaxArea` | 2D meshing | H7 survives; region triangles over their bound | S |
| 7 | M2 `TargetEdgeLengthF` 0, negative, infinity | remeshing | H8 survives; a field 0 outside its support splits every edge | S |
| 8 | R2 `MeshAutoRepair.RemoveMode` | repair | H9 survives; a public 1.0.324 option without any test | S |
| 9 | B4 pipeline serial against parallel with a reduction | boolean | required by CLAUDE.md; the reducer has parallel loops | S |
| 10 | P3 non-finite input throws in all four predicates | predicates | silent 0 for repeated infinite points; decided 2026-11-25 (throw before any shortcut) | S |
| 11 | B5 `SafeRounding = false` gives the exact vertices rounded | boolean | the only check uses dyadic inputs; a speed change would pass | S |
| 12 | M5 defect corpus with the guarded Remesher options | remeshing | the recommended preset never sees a fin, an empty or a single-triangle mesh | S |
| 13 | R4 + R5 `DefectInputTests` assertions | repair | an empty result passes; an unclean result with a wrong reason passes | S |
| 14 | M6 `SnapRounding2` input order | 2D meshing | a documented claim without a test; hash order can leak | S |
| 15 | B6 whole booleans at 1e9 and 1e12 | boolean | the ulp terms dominate there; no test of a whole input | M |

Next in line: B7 (reuse with changed options), M3 and M4 (inline projection, `VertexControlF`), M7 (`MaxDisplacement`), B8 (an
empty operand), T1 (the floor of the nightly time test).
