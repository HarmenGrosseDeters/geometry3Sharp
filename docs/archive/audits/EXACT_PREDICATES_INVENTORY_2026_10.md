# Exact Geometric Predicates — Inventory (October 2026)

> **Historical Record (Status: Archived, 2026-10; translated from German `docs/PREDICATES.md`):**
> This page is an October 2026 snapshot. The `predicates-consolidation` branch has long been merged, and the timings below were not re-measured.
> **Updates since October 2026:** Outside the expansion range, `Orient2d`, `Orient3d`, and `InCircle` now use generated stack-allocated monomial kernels in `math/Generated/DoublePredicates.cs` ([EXACT_FALLBACK_MONOMIALS.md](../../design/EXACT_FALLBACK_MONOMIALS.md)) rather than `BigInteger`. Open points 1, 2, and 5 of Section 6 concern external repositories (`triangle.net` fork and `Triangle.Net-Old-Custom`). Point 3 still applies: legacy `Query2d`, `QueryTuple2d`, and `PrimalQuery2d` remain in `ConvexHull2`, `MeshInsertUVPolyCurve`, and `IntrTriangle3Triangle3`.

Overview of all `orient2d`, `orient3d`, and `incircle` implementations across three codebases—`geometry3Sharp` (`g3`), the Triangle.NET fork (`HarmenGrosseDeters/triangle.net`), and the "MeshFunctions" wrapper (`HarmenGrosseDeters/Triangle.Net-Old-Custom`): what exists, what is guaranteed, where the limits lie, who uses what, how each is tested, and which licenses apply. Open items are listed at the end.

Snapshot state: `g3` branch `predicates-consolidation` (`volumetric-booleans` `f5c7f38` + this document, harness, Stage D, and the `Orient2d` underflow filter fix), Triangle.NET fork `master` `01af2aa` and open PR #5 (`claude/awesome-pascal-eex2py`, `0b1b934`). `Triangle.Net-Old-Custom` could not be read in that session (private repository without access granted); details for it come from the preceding session and are marked accordingly.

---

## Summary

| | g3 `ExactPredicates` | Triangle.NET `RobustPredicates` | Old-Custom `Primitives` (`main`) | Old-Custom `claude/fallback-adapter` |
|---|---|---|---|---|
| Predicates | `orient2d`, `orient3d`, `incircle` | `orient2d` (`CounterClockwise`), `incircle` (`InCircle`/`NonRegular`) | `orient2d`, `incircle` | Same as left |
| Return type | Sign (`int`) | Determinant (`double`: exact sign, approximate magnitude) | `double` | `double` |
| Exact in `~2^±100` | Yes (Stages A–D) | Yes (Shewchuk Stages A–D) | **No** (`decimal`) | Yes (delegates to Triangle.NET) |
| Exact on underflow/overflow | Yes (`BigInteger`; later monomial kernels) | **No** on `master`; yes with PR #5 | No | **No** (yes once PR #5 merges) |
| Provenance | Original implementation from paper | Port of `predicates.c` parts from `triangle.c` | Old Triangle.NET | — |
| License | Boost | Triangle license (Shewchuk) / MIT (Triangle.NET) | Same | Same |

**Conclusion:** `g3` returns the exact sign for all finite `double` inputs (after fixing the `Orient2d` filter for subnormal products; see Section 1), as does Triangle.NET with PR #5. Triangle.NET is exact in the normal coordinate range, but fails for very small coordinates (measured `1e-160` and `1e-300`: products underflow to subnormal or `0`) and very large coordinates (`1e200`: products overflow; `incircle` already fails at `1e150` due to degree-4 terms). Old-Custom `main` already produces wrong signs at normal coordinates.

**PR #5 in the Triangle.NET fork** ("Predicates: exact also on underflow and overflow") closes Triangle.NET's gap using the same range check + `BigInteger` strategy as `g3`. Measured in the harness: 0 errors across all scenarios; 9 of 11 new tests fail on `master` and all 20/20 predicate tests pass with PR #5 (see Section 2).

---

## 1. geometry3Sharp

### `math/ExactPredicates.cs` — Public API

```csharp
int ExactPredicates.Orient2d(Vector2d a, Vector2d b, Vector2d c)
int ExactPredicates.Orient3d(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
int ExactPredicates.InCircle(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
```

**Sign conventions:**
- `Orient2d`: `+1` when `(a, b, c)` are counterclockwise (`= Shewchuk orient2d`).
- `Orient3d`: `+1` when `d` lies on the side toward which the normal of counterclockwise triangle `(a, b, c)` points, i.e., `sign(((b - a) × (c - a)) · (d - a))`. **This is the opposite of Shewchuk's `orient3d`** (where `> 0` means `d` lies *below* the plane). Internally, calling `AdaptivePredicates.Orient3d` negates the sign.
- `InCircle`: `+1` when `d` lies inside the circle through counterclockwise `(a, b, c)` (`= Shewchuk incircle`); reversed for clockwise input.

**Evaluation pipeline (all three predicates):**
1. **Stage A (floating-point filter):** Determinant in `double` with Shewchuk's error bounds (`(3 + 16ε)ε`, `(7 + 56ε)ε`, `(10 + 96ε)ε` times the permanent). All three decide only when the permanent (for `Orient2d`, `|l| + |r|`) exceeds `1e-200` (`UnderflowLimit`), since subnormal products invalidate relative bounds. Overflow produces `∞`/`NaN`, making `det > bound` false and falling through to Stage 2.
   *Fixed:* `Orient2d` previously lacked the underflow check. For exactly collinear points around `1e-154 .. 1e-170`, the bound rounded to `0` while rounding errors left a subnormal `det` (`5e-324`), causing the filter to return `±1` instead of `0` (16 of 20,000 cases in the `2^k` scenario at scale `1e-160`; pinned by `Orient2d_SubnormalProducts`).
2. **Exact path (`*_exact`):** Equal points return `0`. If all coordinates are `0` or in `[2^-100, 2^100]` (`Expansion.InRange`), evaluates `AdaptivePredicates` (Shewchuk Stages B, C, and exact Stage D). Otherwise falls back to exact integer arithmetic (`*_bigint`, aligning `m · 2^e` onto a common exponent; updated in 2026-11 to stack-allocated monomial kernels in `math/Generated/DoublePredicates.cs`, keeping `*_bigint` as test reference and for `InSphere`).
3. `NaN` or `±∞` throws `ArgumentException` in the exact path (`InfiniteCoordinates_Throw`).

`Expansion.TwoProduct` intentionally avoids FMA (`Math.FusedMultiplyAdd` does not exist on `netstandard2.0` / `net48`); Dekker splitting with `2^27 + 1` is exact as long as no overflow or underflow occurs, guaranteed by `InRange`.

### `math/AdaptivePredicates.cs` — Stages B, C, and D (Internal)

- **Stage B:** Exact determinant from the *rounded* differences (expansion of a few dozen doubles) with Shewchuk bound `ccwerrboundB`, etc. When all differences are exact (tails = 0), Stage B decides directly.
- **Stage C:** First-order error terms from difference rounding errors, with bound `...errboundC`.
- **Stage D:** Stage B's expansion plus **all** remaining rounding-error terms—giving the exact determinant. For each Laplace term with exact difference `x' = x + xtail`, each 2×2 minor is `M' = M + T` (`cross_tail`, ≤ 12 components). `orient3d`: `z'·M' - z·M = z·T + ztail·M'`. `incircle`: with exact lift `L' = L + Lt`, `Lt = xtail(2x + xtail) + ytail(2y + ytail)`, `L'·M' - L·M = L·T + Lt·M'`. `orient2d`: `B + T`. Zero tails are skipped so expansions stay small (`incircle` ≤ 1,152 components, typically far fewer). Written from Shewchuk's paper rather than `predicates.c` or Triangle.NET; term grouping differs from Shewchuk's code.
- Replaces the earlier O(n²) full expansion (`Expansion.Terms`, ~1,500 terms for `incircle`).
- Uses Shewchuk's sign convention (so `Orient3d` is negated by `ExactPredicates`).
- Fixed-size `[ThreadStatic]` workspace arrays per thread.

### Other Predicates in `g3`

| Location | Description | Relationship to `ExactPredicates` |
|---|---|---|
| `mesh_ops/ExactMeshArrangement.cs` (`Orient`, `InCircle`) | `orient2d`/`incircle` on **rational** homogeneous `ExactPoint3` projections with approximate filter (`orient2d_approx`) and exact `BigInteger` `Det3`/`Det4` | Distinct because inputs are implicit intersection points, not `double`s |
| `ExactMeshArrangement` (input triangles, degeneracy) | `ExactPredicates.Orient3d` / `Orient2d` on input coordinates | Uses central predicates |
| `mesh_ops/ExactMeshBoolean.cs` (`orient3d(ExactPoint3…)`) | Sign of 4×4 homogeneous determinant (ray test) | Distinct (rational points), via `ExactMeshArrangement.Det4` |
| `mesh_ops/ExactTriangulation2.cs` | CDT taking `orient`/`incircle` delegates | Uses caller-supplied delegates |
| `intersection/ExactTriangleIntersection.cs` | Triangle-triangle tests | Uses `ExactPredicates` |
| `mesh_ops/TerrainSafety.cs`, `TerrainVolumeDifference.cs` | Side and overlap tests | Uses `ExactPredicates` |
| `math/Query2.cs` (`Query2d`), `QueryTuple2d.cs`, `PrimalQuery2d.cs` | Legacy WildMagic/GTEngine ports in `double` **without** exact fallback | Non-exact; used by `ConvexHull2`, `IntrTriangle3Triangle3`, `MeshInsertUVPolyCurve` |
| `math/Query2Integer.cs` (`Query2Int64`) | Integer version casting to `long` | Exact only for small integer inputs (overflows past `~2^31`) |
| `math/MathUtil.IsLeft` | Unfiltered `double` | Non-exact |

Thus `g3` has **one** `double` exact-predicate implementation (`ExactPredicates` + `AdaptivePredicates` + `Expansion`).

### Tests

`tests/geometry3Sharp.Tests/ExactPredicatesTests.cs` (all green on `net10.0`):
- Against `BigInteger`: `Orient_MatchesExactIntegerArithmetic_NearDegenerate`, `ExpansionArithmetic_MatchesBigInteger`, `ExpansionArithmetic_Cocircular`, `TwoProduct_IsExact`.
- Scales: `Orient_TinyCoordinates`, `Orient3d_SubnormalProducts`, `Predicates_NearDegenerate_ScaledIntoTheSubnormalRange`, `Orient_ExpansionPath_CoplanarAndNearCoplanar_AtManyScales`.
- Adaptive stages: `AdaptiveStages_DecideNearlyDegenerateCasesCorrectly` (Stage D needed in `< 10%` of cases; also tests Stage D standalone without B/C bounds), `AdaptiveStages_ExactDifferences_DecideDegenerateCases`.
- Stage D: `StageD_ExactlyDegenerate_WithInexactDifferences`, `StageD_Cocircular_WithInexactDifferences` (Gaussian-prime integer circles with `R^2` in `[2^105, 2^106)`), `StageD_MatchesBigInteger_HardValues`. Mutation-checked: omitting any of the 12 Stage D term groups fails at least one test.
- `Orient2d_SubnormalProducts`, basic cases, `InfiniteCoordinates_Throw`, and `TriTri_*`.
- Plus `ExactImplicitPointTests.cs` and `ExactBooleanBuildingBlockTests.cs` for rational arrangement predicates.

---

## 2. Triangle.NET Fork (`HarmenGrosseDeters/triangle.net`)

`src/Triangle/RobustPredicates.cs` (implements `IPredicates`):
- `CounterClockwise` + `CounterClockwiseAdapt` (Stages A–D), `InCircle` + `InCircleAdapt` (Stages A–D), `NonRegular` (= `InCircle`), `FindCircumcenter` (construction, not predicate).
- Essentially 1:1 from `triangle.c` (macros expanded via `CL /P`), with bounds checks in `FastExpansionSumZeroElim`, B-array length 5, local workspaces, non-atomic `Statistic` counters, and `Behavior.NoExact`.
- No `orient3d`.
- **No underflow/overflow handling on `master`:** around `1e-160`, products underflow to subnormal/0; at large coordinates, `∞`/`NaN` arise (`Math.Sign(NaN)` throws).
- Returns determinant as `double`; some callers (`QualityMesher`, `Dwyer`, `ConstraintMesher`) inspect magnitude as well as sign.

**PR #5 (`0b1b934`, open at audit time):** fixes underflow/overflow by guarding the `CounterClockwise` and `InCircle` filters (`detsum >= 1e-200`, differences in `[2^-200, 2^200]`), restricting adaptive stages to `[2^-100, 2^100]`, falling back to `BigInteger` (`CounterClockwiseExact` / `InCircleExact`), and returning finite `det` when its sign matches else `±1`. Added `ExtremeCoordinatesGiveTheExactSign` and `MixedMagnitudesGiveTheExactSign`. Measured 0 errors across all harness scenarios; license-clean (reuses idea only).

---

## 3. Old-Custom "MeshFunctions" (`HarmenGrosseDeters/Triangle.Net-Old-Custom`)

*Not re-verified in the October 2026 session (no repo access); recorded from the preceding session:*
- `TriangleLib/Primitives.cs` on `main` re-evaluates uncertain cases in `decimal`—**not exact**, since `(decimal)double` keeps ~15 significant digits and predicate products require more than 28 digits (measured 9–29% wrong signs on near-degenerate normal-scale inputs).
- Branch `claude/fallback-adapter` (`4bfefd0`) delegates uncertain cases via `extern alias neu` to the Triangle.NET fork's `RobustPredicates`, giving 0 errors at normal scale and sharing Triangle.NET's extreme-scale errors until PR #5 merges. Its code comment claiming an integer path was inaccurate prior to PR #5. Targets `net48;net8.0-windows`.

---

## 4. Measurements

Run with `benchmarks/PredicateComparison` (20,000 cases per row; reference: exact `BigInteger` determinant of the actual `double` values; exceptions and `NaN` count as errors). Columns: `g3 | naive double | Triangle.NET master`.

| Scenario | `orient2d` | `incircle` | `orient3d` (`g3 \| naive`) |
|---|---|---|---|
| Near-degenerate, scale `1` | `0 \| 1210 \| 0` | `0 \| 2012 \| 0` | `0 \| 2106` |
| Near-degenerate, `1e6` | `0 \| 1218 \| 0` | `0 \| 1988 \| 0` | `0 \| 2084` |
| Near-degenerate, `1e-10` | `0 \| 1164 \| 0` | `0 \| 1959 \| 0` | `0 \| 2099` |
| Near-degenerate, `1e-160` | `0 \| 20000 \| 20000` | `0 \| 20000 \| 20000` | `0 \| 20000` |
| Near-degenerate, `1e-300` | `0 \| 20000 \| 20000` | `0 \| 20000 \| 20000` | `0 \| 20000` |
| Near-degenerate, `1e150` | `0 \| 1194 \| 0` | `0 \| 20000 \| 20000` | `0 \| 20000` |
| Near-degenerate, `1e200` | `0 \| 20000 \| 20000` | `0 \| 20000 \| 20000` | `0 \| 20000` |
| Grid exact degenerate, `1` | `0 \| 0 \| 0` | `0 \| 0 \| 0` | `0 \| 0` |
| Grid, `1e6` | `0 \| 0 \| 0` | `0 \| 9232 \| 0` | `0 \| 5877` |
| Grid, `1e-160` | `0 \| 18095 \| 18095` | `0 \| 18944 \| 18944` | `0 \| 19691` |
| Grid, `1e150` | `0 \| 3017 \| 0` | `0 \| 20000 \| 20000` | `0 \| 19843` |
| Kettner grid 141², `1` | `0 \| 5980 \| 0` | `0 \| 1378 \| 0` | `0 \| 3032` |
| Kettner grid, `1e-160` | `0 \| 19740 \| 19740` | `0 \| 19880 \| 19880` | `0 \| 19740` |
| Kettner grid, `1e200` | `0 \| 19881 \| 19881` | `0 \| 19881 \| 19881` | `0 \| 19881` |

With Triangle.NET PR #5 and updated `g3`: **0 errors across all 72 harness rows**, including "exact degenerate, inexact differences (`2^k`)" (where naive `double` at scale 1 fails 7,864 / 12,981 / 16,196 of 20,000 cases, and unfixed `g3` failed 16 cases at `1e-160`). Prior session numbers for Old-Custom `main` at scale 1: near-degenerate 1,853 (`orient2d`) / 5,820 (`incircle`); Kettner grid 614 / 5,693.

Time for 200,000 calls (ms, `net10.0`, single core, ±30% noise):

| Implementation | `orient2d` gen. | Near-collinear | Exact degen. (`2^k`) | `incircle` gen. | Near-cocircular | Exact degen. (`2^k`) | `orient3d` gen. | Near-coplanar | Exact degen. (`2^k`) |
|---|---|---|---|---|---|---|---|---|---|
| `g3` before Stage D | 6 | 9 | 81 | 2 | 152–172 | 5225–5619 | 2 | 49–58 | 1191–1294 |
| `g3` with Stage D | 2–7 | 4–14 | 41–57 | 2–3 | 154–175 | 434–621 | 2–4 | 49–63 | 334–396 |
| Triangle.NET PR #5 | 12 | 12 | 60 | 11 | 192 | 1660 | — | — | — |
| Naive `double` | 2 | 1 | 1 | 1 | 2 | 2 | 2 | 2 | 2 |

Stage D accelerates cases Stage C cannot decide (exact degenerate with inexact differences): `incircle` ~10× faster, `orient3d` ~3.5× faster. Near-degenerate cases (±4 ULP) already finish in Stages B/C. (Triangle.NET allocates three `Point` objects per call in the harness, accounting for part of the general-position overhead.)

---

## 5. Licensing

- `g3` is under the Boost Software License. `ExactPredicates`, `AdaptivePredicates` (including Stage D), and `Expansion` were written from Shewchuk (1997) without reading or copying `predicates.c`, `triangle.c`, or Triangle.NET (see [docs/SOURCES.md](../../SOURCES.md)).
- Shewchuk's `predicates.c` is public domain, whereas `triangle.c` (and code expanded from it in Triangle.NET's `RobustPredicates.cs`) is under Shewchuk's Triangle license (no commercial use without permission). **Nothing may ever be copied from Triangle.NET into `g3`.** Conversely, Boost-licensed code from `g3` may be used in Triangle.NET with its license notice.

---

## 6. Open Points (As of October 2026)

1. **Triangle.NET fork underflow/overflow:** Addressed by PR #5 (reviewed in Section 2); awaiting merge. Optional future addition: `orient3d`.
2. **Old-Custom:** Once PR #5 merges into the Triangle.NET fork, the integer-path comment matches and `claude/fallback-adapter` should be rebuilt against it.
3. **`g3` legacy non-exact queries:** No duplicates exist among exact `double` predicates. Candidates for future exact migration: `Query2d`, `QueryTuple2d`, and `PrimalQuery2d` in `ConvexHull2`, `IntrTriangle3Triangle3`, and `MeshInsertUVPolyCurve` (behavior-changing, separate scope).
4. **`g3` Stage D:** Completed (Sections 1 & 4), uncovering and fixing the `Orient2d` subnormal-product filter bug.
5. **Harness as unit tests:** Extreme-scale scenarios are already unit-tested in `g3`; add matching NUnit tests to the Triangle.NET fork alongside PR #5.
