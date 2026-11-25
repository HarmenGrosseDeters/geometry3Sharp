# Exact-code bound inventory (2026-11-25)

Back to [QA Hub](README.md) · [docs/QA.md](../QA.md)

---

Which floating-point filters, error bounds and termination rules of the exact code have a machine-checked argument,
and which rest on tests or review alone (owner 2026-11-25, Refs #1572). Scope: the files of the exact sections of
`.editorconfig`, plus the drift bound of `mesh_ops/MeshRoundingRepair.cs`. Base: master 0af792a0. No library code
was changed.

This page does not repeat [ERROR_BOUND_AUDIT_2026-11.md](../research/ERROR_BOUND_AUDIT_2026-11.md) (the audit,
cited as EBA below, with its IDs EP-, AP-, EB-, AR-, RC-, CD-, SN-). It adds three things: the coverage of each
bound by every tool, whether a mutant of the bound is killed and by what, and a risk ranking of the gaps.

## 1. Coverage levels

| Level | Meaning |
|---|---|
| Z3 | A lemma in `tools/Z3Pilot`: UNSAT counterexample queries, a SAT vacuity check, a controlled SAT mutant, and a source contract that fails on a code change. Runs in `z3-pilot.yml` (ready PRs touching its paths) |
| EBA-derived | A model in `tools/ErrorBoundAudit/bounds.py`, proved monomial by monomial in exact rationals by `fpbound.py`. This is a proof, not only pinned text. Its anchors (source lines that must still be there) fail on a changed line. Runs in the `linux` job of `ci.yml` |
| EBA-checked | A `bounds.py` entry that checks numbers (a width, a range, a published constant) with hardcoded values. The anchors pin the text; the check proves only the transcribed numbers |
| FPTaylor | `tools/FPTaylorPilot`: an independent bound size for two generated filters. Not in CI (needs an OCaml build) |
| Design doc | A hand derivation in `docs/design/*.md` or in the code comment. Reviewed, not machine-checked |
| Tests | Only example, differential or adversarial tests |

A mutant is "killed by EBA anchor" when the anchor test fails on the changed text. That kill is textual: the same
change made to the anchor and the model's transcribed constant would pass, unless the derivation is coupled to the
value (EBA-derived) rather than to a hardcoded number (EBA-checked).

## 2. Findings in one paragraph

- The predicate filters of `ExactPredicates`, the Boolean's plane and orientation filters, `CertifiedDet3`, the
  arrangement's approximate predicates, the snap grid, the generated filters, the tri-tri prefilter and the
  rounding drift rule all have a machine-checked argument (EBA-derived or Z3).
- The hand mutants H5 and H6 of the test-gap audit (PR #1572) survived the library tests but are both killed in CI by
  the EBA anchor test, which that audit did not run. For H5 the kill is semantic (EBA-derived EP-2 fails for 4 u);
  for H6 it was textual only (AP-1 is EBA-checked with hardcoded 2^-100), and no .NET test failed.
- No machine-checked argument exists for the terrain prefilters, `ContinuousCollision`'s filter, the polyhedral
  envelope, `ContouringTopologyCertificate.may_meet_box`, `IntrExactBox3`, `ExactDoubleWord`, the geodesic windows and
  the Antipodal tolerance. Most of them cull without an exact recheck: a bound too small gives a wrong answer, not a
  slower one.

## 3. Inventory

Classes: **D** a too-small bound decides wrongly (no exact recheck); **F** an exact fallback follows (a too-small
bound is a wrong answer only if it lets the filter decide wrongly; a too-large one costs speed); **T** a termination
rule; **S** speed or strategy only.

### 3.1 Predicates and expansions (`math/ExactPredicates.cs`, `math/AdaptivePredicates.cs`)

| Bound | file:line | Claim | Coverage | Mutant killed by |
|---|---|---|---|---|
| Orient2d, Orient3d, InCircle, InSphere stage A (EBA EP-1..4), F | `ExactPredicates.cs:21-24` | (3, 7, 10, 16 + O(u)) u * permanent covers the filter error (with the underflow term) | EBA-derived (margins 1 + O(u)); Z3 `orient3d_bound.py` (planned, Step 2) | H5 (7 to 4 u): EBA anchor and derivation; `ExactPredicatesFilterBoundTests` (planned, Step 2). Library tests before: none |
| with_underflow, UnderflowLimit 1e-200, UnderflowUnit 2^-1066 | `ExactPredicates.cs:25-60` | the absolute term for subnormal products; dropped only where it rounds away | EBA-derived (part of EP-2..4, `with_underflow_note`); tests `ExactPredicatesTests.UnderflowTerm` | EBA anchor; the Stryker survivors of the term (mutation README, item 1) are not killed by tests |
| VolumeSumWithBound (EP-5), TryCompareAbsVolume6 slack (EP-6), F | `ExactPredicates.cs:511`, `:542` | Higham's gamma_(n+2) recursive-sum bound on top of Orient3dBound | EBA-derived for n = 1, 2, 7, 40; design doc EXACT_VOLUME_SIGN.md | EBA anchor; the H5 test below goes through this function |
| Expansion.InRange [2^-100, 2^100] (AP-1), D | `ExactPredicates.cs:701-716` | degree-4 products of the expansions and their rounding errors are normal and finite | EBA-checked (hardcoded 2^-100, 2^-152); Z3 `adaptive_range.py` (planned, Step 2, coupled to the source values) | H6 (2^-100 to 2^-250): EBA anchor (text); `ExactPredicatesRangeEdgeTests` (planned, Step 2, InCircle 1 for cocircular points at 2^-220). Library tests before: none |
| Stage B/C constants (AP-2), F | `AdaptivePredicates.cs:29-36` | Shewchuk's constants | EBA-checked against the paper (reference); CcwErrBoundB re-derived at first order | EBA anchor; stage C mutants cost only time (test-gap audit P6, P7) |
| Merge order of `sum` and `scale`, D | `AdaptivePredicates.cs:284-297` | non-overlapping, sorted output (Shewchuk's theorems) | Tests only (stage D against BigInteger) | open in the mutation README (item 6); test-gap audit P4 |
| Coincidence shortcuts, F | `ExactPredicates.cs:194-247` | repeated points give 0 | Tests | equivalent (the exact path gives 0); non-finite input: test-gap audit P3, decided 2026-11-25 |
| orient3d_int widths (EP-7) | `ExactPredicates.cs:257` | integer widths fit | EBA-checked | EBA anchor |

### 3.2 Generated predicates (`math/Generated/*.cs`, `tools/PredicateCompiler`)

| Bound | file:line | Claim | Coverage | Mutant killed by |
|---|---|---|---|---|
| Filter rules of `filter_bounds`, F | `tools/PredicateCompiler/Compiler.cs` (filter_bounds) | per-operation relative error constants, the grid gate `960 / degree - 52`, K >= 2^-62 | Z3 `filter_rules.py` (one lemma per rule, mutants, `composition_check` re-derives the generated constants) | Z3 source contract; `GeneratedPredicatesTests` checks the bound table of FIXED_WIDTH_EXACT_POINTS.md |
| Orient3dLPI / TPI filters | `math/Generated/IndirectPredicates.cs` | the generated K | Z3 (composition); FPTaylor (`orient3dlpi_*`, `orient3dtpi_*`, not in CI) | Z3 contract |
| DoublePredicates kernels, FixedPoint3 / ExactLimbSigns scratch widths | `math/Generated/DoublePredicates.cs`, `math/FixedPoint3.cs:430-435`, `:547`, `math/ExactLimbSigns.cs:25-27` | integer widths; an overrun throws | generator-checked (`GeneratedPredicatesTests`); fail-safe (a crash, not a wrong sign) | tests (crash) |

### 3.3 Exact Boolean and arrangement (`mesh_ops/ExactMesh*.cs`)

| Bound | file:line | Claim | Coverage | Mutant killed by |
|---|---|---|---|---|
| front_side / plane_side / plane_side_abs / orient3d_filtered (EB-1..5), F | `ExactMeshBoolean.cs:4237-4289`, `:5137-5168` | 1e-14 mag and Shewchuk-form bounds | EBA-derived (plane_side_abs margin 1 + 2u: nothing to spare) | EBA anchor and derivation |
| widened, negative_volume (EB-6, EB-7), D | `ExactMeshBoolean.cs:1332`, `:1824` | enclosures | EBA-derived (F1 fixed #1347) | EBA |
| Conversions, norm, boundary_bound, far_term, piece_diameter, carry_length (RC-1..8) | `ExactPoint3.cs:106`, `ExactMeshBoolean.cs:2716-3406`, `:5226` | rounding factors | EBA-derived / checked (F4 fixed #1350) | EBA |
| Antipodal sum `tol = 16 (ea + eb + 1e-15 + dir)`, `4 tol / hypot`; 20 attempts then 0.5, D, T | `ExactMeshBoolean.cs:5187-5243` | the winding of open input is decided outside the tolerance band | Design doc only ("taken generously"); tests `ExactBooleanOpenInputTestsAntipodal` | not known; no derivation to check |
| BoundaryTree, L2 carry, projection prefilters | `ExactMeshBoolean.cs` (antipodal_tree_value, carry_bound, pierce_prefilter) | analytic remainder and rounding pads | read (EBA section 5); tests | not known |
| Arrangement plane_value, orient2d_approx, order_key, merge_points, incircle_approx, along_approx, BoxGrid2 pad (AR-4..14), F | `ExactMeshArrangement.cs:809-2846` | approximate predicates with exact fallback | EBA-derived | EBA |
| planes_approx, segment_point (quotients), segment_hits slack 1e-9, F | `ExactMeshArrangement.cs` | quotient error bounds | read only (division is outside `fpbound.py`) | not known |
| Devillers-Guigue prefilter, D | `ExactMeshArrangement.cs` (apart_on_the_line) | apart iff the line segments are disjoint | Z3 `tritri_prefilter.py` (real arithmetic) | Z3 contract |
| CertifiedDet3 (CD-1) | `queries/CertifiedDet3.cs:22` | certified determinant | EBA-derived | EBA |

### 3.4 Snap rounding, rounding repair

| Bound | file:line | Claim | Coverage | Mutant killed by |
|---|---|---|---|---|
| SnapGrid.Pixel fast branch, D | `comp_geom/SnapRounding2.cs` (Pixel) | floor(x/W + 1/2) is the exact index when accepted | Z3 `pilot.py` (six FP obligations + bridges) | Z3 contract; `SnapGridPixelFilterTests` |
| CrossesInteriorFilter (SN-5), F | `SnapRounding2.cs:717` | corner filter | EBA-derived | EBA |
| OrientFilter 1e-12 * sum, F | `SnapRounding2.cs:344-353` | > 3000x Shewchuk's bound; exact fallback | Design doc (comment) | S: only speed unless shrunk 3000x |
| RoundLimit 1000 and the progress guard, T | `SnapRounding2.cs:65`, `:329-336` | each round adds a hot pixel; throws at the cap | tests | throws (loud) |
| SafeRounding drift rule (D1, reference rule), D | `mesh_ops/MeshRoundingRepair.cs:1093-1099`, `:1598-1613`, `:2003-2026` | every surface point moves at most maxLength | Z3 `drift_reference.py`, `drift_apex.py`; check mode throws on a violated bound | Z3 contract; `SafeRoundingBoundTests`. The distances are plain doubles (comment :120): a few ulps, reviewed |
| Step loops `s *= 4 / 16`, T | `MeshRoundingRepair.cs` (fix_degenerate, try_separate, fix_intersection) | s0 > 0, the loops end | Z3 `step_loops.py` | Z3 contract |
| MaxPasses 4, evaluation and pair-test budgets, T | `MeshRoundingRepair.cs:244-280`, `:671`, `:974` | stop with EvaluationLimitReached / IsClean false | tests | status (not silent) |

### 3.5 Intersection kernels (`intersection/*`, `math/ExactDoubleWord.cs`, `math/ExactRayTriangle.cs`)

| Bound | file:line | Claim | Coverage | Mutant killed by |
|---|---|---|---|---|
| IntrExactBox3 RayAabb `3U(|tn|+|tf|) + AbsErr`, TErr, Prep `6U S`, Kind, Away, D (filter) | `IntrExactBox3.cs:25-27`, `:260-265`, `:317-321`, `:361-449`, `:502-512` | certified ray/box decisions and t enclosures | Design doc BOX3_EXACT_FILTER.md; tests `IntrBox3FilterEdgeTests` (adversarial edges) | tests (not measured) |
| ExactDoubleWord Eps2 = 2^-100, ErrUp, ErrFloor, D | `ExactDoubleWord.cs:31`, `:45-48`, `:104-165` | certified double-word error (6.4x margin) | Design doc TRI3TRI3_DOUBLE_WORD.md; tests `ErrorBound_HoldsAtTheOperandErrorExtremes_*` | tests at the extremes |
| SecondPoint exponent (F2 of EBA), D | `ExactRayTriangle.cs:49-75` | the scaled point defines the same ray | read; #1348 open (range not enforced) | - |
| TriangleTriangleFilter | `intersection/TriangleTriangleFilter.cs` | exact predicates only, no float constant | - | - |
| ContinuousCollision filter `e = Up(M 2^-47 + 16 * 2^-1074)`, D | `ContinuousCollision.cs:669-694`, `:420`, `:564-588` | covers 62 M u + 21 eta (no exact fallback: a box is discarded) | Design doc CCD.md (hand table); tests `ContinuousCollisionTests` | not known |
| ContinuousCollision caps (MaxIterations, MaxTighteningIterations, AbandonAfter), T | `ContinuousCollision.cs:48`, `:66`, `:282`, `:376` | IterationLimit / Abandoned statuses; tightening stops silently with a valid bound | tests | status |

### 3.6 Envelope, geodesics, contouring certificate

| Bound | file:line | Claim | Coverage | Mutant killed by |
|---|---|---|---|---|
| PolyhedralEnvelope Theta* (9, 29, 11, 7, 20, 61 u), margins eta, muPlan, corr, delta, rho, R, D | `PolyhedralEnvelope.cs:36-42`, `:91-121`, `:207-235`, `:302-305` | the polyhedra lie inside the epsilon envelope | Design doc ENVELOPE.md 4.4; a-posteriori check `mu <= muPlan` (TooThin); tests `PolyhedralEnvelopeTests` | not known |
| Geodesic CornerAngle (Atan2 widened 2 spacings), Emit 1e-9, DiskOfBox, TransformDisk factors, D | `ExactMeshGeodesicDistance.cs:193-199`, `:396`, `:421`, `:629-646` | enclosures of the windows; Lower is a lower bound | Design doc GEODESICS.md; CornerAngle calls itself "an assumption" (correct rounding of CoreMath.Atan2); tests | not known |
| may_meet_box `err = 24 u M + 8 eta`, D (cull) | `ContouringTopologyCertificate.cs:420-432` | 22 u M + 6 eta + O(u^2 M) suffices | Code-comment derivation; tests (`may_meet_box` 10 mentions); Z3 `may_meet_box.py` (planned, Step 2) | tests (not measured) |
| first_bin / last_bin searches, T | `ContouringTopologyCertificate.cs:321-336` | exact comparisons from an estimate; no cap | review | an infinite coordinate does not end (callers reject non-finite input: not verified here) |

### 3.7 Terrain (`mesh_ops/TerrainSlope*.cs`, `math/ExactSlope*.cs`)

| Bound | file:line | Claim | Coverage | Mutant killed by |
|---|---|---|---|---|
| Approx acceptance `err <= 1e-12 (|x|+|y|)` and the corner error `e = 4e-12 (|x|+|y|) + eps` that consumes it, D (cull) | `ExactSlopeKernel.cs:328-337`, `TerrainSlopeMap.cs:476-482` | the corners are within e of the exact ones | Code comment; tests `TerrainSlopeDesignTests` (prefilter_skips); Z3 `slope_approx.py` (planned, Step 2) | tests (a scale-dependent skip was found by them once) |
| Prefilter slacks `1e-9 (...)`, near `1e-11`, trim_ray eps, D (cull) | `TerrainSlopeMap.cs:450`, `:487`, `:538-553`, `:609`, `:355-359` | no face that the exact test keeps is skipped | Code comments; EBA section 7 names them the first next derivation; tests | not known |
| Slb `cos(2 theta)(1 - 1e-6)`, reach `1.01`, box `1.05 dMax`, GradientBound `(1 + 1e-9)`, D (cull) | `TerrainSlopeDesign.cs:1389-1426`, `:2193`, `TerrainSlopeRamps.cs:381-385`, `ExactSlopeKernel.cs:111` | lower bounds of the slope, upper bounds of the reach | design docs TERRAIN.md, TERRAIN_RAMPS.md; tests | not known |
| Tolerance budget (ez, lambdaBound, facet 2^-49, MergeShare, FlipShare), D | `TerrainSlopeDesign.cs:1286-1373`, `:1933-1951`, `TerrainSlopeCatchLineBound.cs:38`, `:112-113` | the realised surface stays within the tolerance | design doc TERRAIN.md (departures); tests | not known |
| TanLowerBound (48 Taylor terms, pi tail), D | `TerrainSlope.cs:121`, `:147-170` | a proven lower bound of tan | code comment (sign of the cut term) | tests `TerrainSlopeOracleTests` |
| Loops: rounds 3, attempts 7, realization 7, k 8, tries 60, FanTolerance 400, FlipNeedles 64, onto_slope_side 64, T | `TerrainSlopeDesign.cs:453-475`, `:1118-1166`, `:1346`, `:1497`, `:2055`, `:2167`; `TerrainSlopeMap.cs:1559`; `TerrainSlopeComposite.cs:1702` | statuses or throws; FlipNeedles and onto_slope_side stop silently (an exact check follows) | tests | status; the two silent caps are followed by exact checks |

### 3.8 Termination rules elsewhere

| Rule | file:line | At the cap | Coverage |
|---|---|---|---|
| ExactTriangulation2 legalize `++guard < 10000` | `ExactTriangulation2.cs:152-169` | silent: the stack is cleared, the result may not be Delaunay | tests |
| ExactTriangulation2 constraint and flip limits | `ExactTriangulation2.cs:318-321`, `:421-424` | throws | tests |
| ExactPlanarSimplify link walk, ear clip, work queue | `ExactPlanarSimplify.cs:262-267`, `:424`, `:477-520` | null / false (no simplification) | structural |
| ExactMeshBoolean ray winding 200 attempts | `ExactMeshBoolean.cs:4712`, `:4802` | throws | tests |
| ExactMeshArrangement MaxPieces, MaxPlaneCandidates 8 | `ExactMeshArrangement.cs:1255`, `:1373`, `:2196-2217` | Budget status; equal planes left unmerged (handled as distinct) | tests |
| ExactMeshGeodesicDistance MaxWindows | `ExactMeshGeodesicDistance.cs:44`, `:368` | WindowLimit status | tests |
| DMeshAABBTree midpoint split (outside the scope, for completeness) | `spatial/DMeshAABBTree.cs` | - | Z3 `split_termination.py` |

## 4. Mutants

| Mutant | Library tests (#1572) | EBA test (CI `linux`) | After Step 2 (planned) |
|---|---|---|---|
| H5 `Orient3dBound` 7 u to 4 u | survived (522) | killed: anchor, and EP-2's derivation fails with 4 u | also killed by `ExactPredicatesFilterBoundTests` (an input with error 4.1 u (perm + \|det\|)) and by Z3 `orient3d_bound.py` (SAT) |
| H6 `MinMagnitude` 2^-100 to 2^-250 | survived (549) | killed by the anchor only (text) | killed by `ExactPredicatesRangeEdgeTests` (wrong sign) and by Z3 `adaptive_range.py` (SAT, replayed) |
| `MaxMagnitude` 2^100 to 2^250 | not run | anchor only | `InRange_IsClosed...` (boundary); no wrong sign found up to 2^250 (the overflow limit is about 2^254, see `adaptive_range.py`); Z3 SAT shows only that the proof no longer holds |
| Stryker survivors of `ExactPredicates` (74, mutation README 1) | survived | anchors catch text changes of the anchored lines | unchanged: mostly equivalent (larger bounds, guards) |

## 5. Gaps ranked by risk

Risk is how wrong a broken bound's answer is (a wrong certified result or a culled candidate is worse than a lost
speed-up) times how likely a future edit is to break it (actively tuned code, many constants, no proof that fails).

| Rank | Gap | Why |
|---|---|---|
| 1 | G1 Terrain prefilters and the Approx error coupling (3.7, rows 1-3) | cull without recheck; dozens of constants in active, speed-tuned code; a too-small slack skips a face and the design violates the slope; only tests; one such skip already happened (the 4e-12 term) |
| 2 | G2 `Expansion.InRange` (H6) | a wrong sign of a public predicate; the proof had hardcoded numbers; no test failed |
| 3 | G3 `may_meet_box` err | cull without recheck in a certificate (a wrong topology claim); a hand derivation in a comment only |
| 4 | G4 ContinuousCollision filter (2^-47, 16 eta) | a missed collision; hand table in CCD.md only; no exact fallback |
| 5 | G5 PolyhedralEnvelope Theta constants and margins | a false "inside"; eleven constants derived in ENVELOPE.md, not checked |
| 6 | G6 `Orient3dBound` and the stage A constants (H5) | a wrong sign; machine-derived (EBA), but no .NET test killed a shrink |
| 7 | G7 IntrExactBox3 filters | a wrong certified ray/box answer; derivation in BOX3_EXACT_FILTER.md; edge tests exist |
| 8 | G8 Geodesic window enclosures (CornerAngle, Emit 1e-9, disk factors) | a lower bound that is not one; one assumption (correct Atan2) stated as such |
| 9 | G9 Antipodal tolerance factor 16 and the silent 0.5 | a wrong inside/outside of open input; no derivation exists |
| 10 | G10 Merge order of `AdaptivePredicates.sum` | a wrong sign near 0; correct by Shewchuk, untested; low edit likelihood |
| 11 | ExactDoubleWord constants | 6.4x margin, tests at the operand extremes |
| 12 | `planes_approx`, `segment_point` | exact fallbacks; division outside the checker |
| 13 | ExactTriangulation2 silent 10000-flip guard | a non-Delaunay result, silently; never reached in tests |
| 14 | first_bin / last_bin without a cap | non-finite input only |
| - | SnapRounding2 OrientFilter, stage C constants, coincidence shortcuts, FixedPoint scratch | speed only, equivalent, or a crash (not a wrong answer) |

## 6. Lemmas and tests added (Step 2)

Planned: Z3 lemmas for G2 (H6), G6 (H5), G3 and the Approx coupling of G1, and .NET tests that kill H5 and H6.

## 7. Left open

To be filled at the end of Step 2.
