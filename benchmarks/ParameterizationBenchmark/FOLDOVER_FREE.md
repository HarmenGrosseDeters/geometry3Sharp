# Foldover-free parameterization: bounded acceptance

Paper-based implementation of Garanzha et al. 2021. Owner approved the Tier-L design, the bounded resumed
budget (not a passed original small-effort gate), and targeted hand mutations without mutation-tooling edits.
See [design](../../docs/design/FOLDOVER_FREE_MAPS.md). No authors' implementation was read or ported.

## Reproduce the quality comparison

From the repository root:

```powershell
dotnet run --project benchmarks\ParameterizationBenchmark -c Release -- foldover
```

Raw output: [perf/foldover-free.txt](perf/foldover-free.txt). Fixtures are procedural; no mesh files are stored.
The command reports method, boundary policy, status, iterations, convergence, flipped/degenerate triangles,
symmetric Dirichlet, global overlap pairs, and single-run elapsed time. Timing is observational, not a speedup
claim (no isolated-machine or repeated-trial performance study). LSCM/SLIM are the before/reference paths;
FoldoverFree is the new path with the supplied boundary unchanged bit for bit.

| Fixture/start | Flipped + degenerate before -> after | Symmetric Dirichlet: SLIM default / fixed-boundary result | Result/reference ratio | Global overlap pairs before -> after |
|---|---:|---:|---:|---:|
| Folded Tutte disc | 4 + 0 -> 0 + 0 | 4 / 5.223873291604668 | 1.305968322901167 | 54 -> 0 |
| LSCM annulus, 8 sides/3 strips | 2 + 0 -> 0 + 0 | unavailable / 7.462999479962732 | unavailable | 10 -> 0 |
| LSCM three-peak terrain, 30 x 30 cells | 19 + 0 -> 0 + 0 | 6.8949156797521605 / 16.262756504554 | 2.35865922948293 | 2486 -> 1413 |
| Cone torus cut (UVAtlasTests ordering) | 0 + 0 -> 0 + 0 | 4.005953318212518 / 4.054945456080008 | 1.0122298324458134 | 8 -> 8 |

Numbers after the review fix of 2026-11-04 (the final flip-free iterate is returned instead of the
candidate with the lowest energy at InitialEpsilon; `Converged` is now true on all four), rerun on Linux;
the LSCM starts differ slightly from the first (Windows) run (annulus start 2 + 0 flips instead of 1 + 1).
The annulus result improved most (symmetric Dirichlet 686.97 -> 7.46): the earlier rule kept an early,
near-degenerate candidate.

**The <=10% quality target is NOT met on the three-peak case.** It is also not met on the folded disc reference
comparison. SLIM's default has a free boundary, whereas this operation preserves the specified boundary;
these are reported comparisons, not equivalent constrained optimization problems. SLIM from the identical
invalid starts returns `InvalidConstraints`. Its default on the annulus returns `InvalidTopology`; no invented
finite denominator or passing quality ratio is used. The torus quality ratio meets the target but global
injectivity still fails. The brief explicitly permits reporting unfavorable comparisons; no postprocessing
SLIM optimization, boundary relaxation or default changes were introduced to force the target.

### Fixed-boundary feasibility

The raw command prints every LSCM annulus boundary vertex. The far ring is *nearly* collapsed, not exactly
collinear: its y range is approximately -5.364294121751206e-15 to 6.556747706363866e-15 in this run. A comment
about collapse is not a proof of infeasibility. The retained finite map has zero flipped/degenerate triangles,
unchanged boundary bits and no overlaps reported by UVOverlap: a feasible witness under the existing checks.
The operation separately rejects an exactly collapsed locked edge or an invalid fully locked triangle.

The three-peak result is only locally valid. Its remaining global overlaps are reported, never treated as
repair of the atlas problem. Near-collapse, solver failure, iteration exhaustion and certified infeasibility
are not interchangeable diagnoses.

### Torus fallback

The command reproduces the exact vertex/triangle order and coordinate evaluation of `UVAtlasTests.Torus()`;
cut tie-breaking depends on vertex ids. With `UVAtlasGenerator.Methods.Cones`, the stock result has one
component, zero accepted cone components, eighteen charts and one component falling back to charts. Applying
the new operation to the same cone candidate leaves eight global overlapping pairs. The counterfactual
component fallback count remains **1 -> 1**; it also fails Auto's overlap/distortion acceptance. This is
benchmark-only evaluation: no production fallback was removed and no atlas hook was added.

## Two-round folded-disc gate

Round 1 was registered before execution: fixed 5 x 5 vertex triangulated disc; Tutte start with interior
vertices 12=(2,0), 7=(-1.5,1.5); MaxIterations=200, InitialEpsilon=1, MinimumEpsilon=1e-12, backtracking cap=40.

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~FoldoverFreeTests.FoldedDisc --logger "console;verbosity=normal"
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~FoldoverFreeTests --logger "console;verbosity=detailed"
```

Round 1 passed: 4 flips / 0 degeneracies -> 0 / 0, 115 iterations, boundary-bit and independent triangle checks
passed, UVOverlap reported injective. **No second optimizer round was needed or run.** Subsequent tests exposed
and fixed an initial-map round-trip retention defect (one ulp with MaxIterations=0); this was not optimizer
tuning. Intentional hand-mutant failures are not search rounds. Failed derivatives or validation cannot be
used to reset the two-round budget.

## Mutation and regression evidence

Each hand mutation was applied alone, then restored; no mutation infrastructure changed. No parameterization
shard exists, so the owner explicitly approved this evidence rather than a falsely claimed Stryker score.
For each row, force the rebuild, then run the stated filter:

```powershell
dotnet build tests\geometry3Sharp.Tests -c Release -f net10.0 -t:Rebuild --no-restore
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --no-build --filter <filter> --logger "console;verbosity=normal"
```

| Mutant | Filter | Observed failure |
|---|---|---|
| Gradient's `2*a/chi` sign reversed | `FullyQualifiedName~FoldoverFreeTests.AnalyticGradient` | 3 derivative-oracle failures, 1 zero-Jacobian case passes |
| Equation-6 sigma floor removed | `FullyQualifiedName~FoldoverFreeTests.EpsilonSchedule` | 1 failure: expected 0.9, got 1 |
| Fixed boundary restored by arithmetic rather than original bits | `FullyQualifiedName~FoldoverFreeTests.FullyLockedTriangle\|FullyQualifiedName~FoldoverFreeTests.FoldedDisc` | 2 bitwise-preservation failures |
| Final status unconditionally set to Success | `FullyQualifiedName~FoldoverFreeTests.ReuseABA` | 1 failure: expected FlippedTriangles, got Success |

The initial attempt to use `dotnet test -t:Rebuild` was rejected by CLI argument parsing; it is NOT counted
as mutation evidence. The commands above use `dotnet build -t:Rebuild` before the test runner.

After restoration:

```powershell
dotnet build tests\geometry3Sharp.Tests -c Release -f net10.0 -t:Rebuild --no-restore
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --no-build --filter "FullyQualifiedName~FoldoverFreeTests|FullyQualifiedName~ParameterizationFlippedMapTests|FullyQualifiedName~ParameterizationIssueCasesTests" --logger "console;verbosity=normal"
```

Observed: 37 passed, zero failed. Tests cover numerical derivatives/lifted Hessian, all Similarity.All()
transforms with bitwise power-of-two results, unused outliers, reuse/A-B-A, repetitions, immutable inputs,
degenerate starts, invalid/non-finite input, unsupported topology, infeasible fixed triangles, annulus witness,
finite unsuccessful maps and the distinction between local validity and global overlap.

## Full local CI and publication blockers

The complete CLAUDE local CI list was executed, not skipped. Both library target builds, API-validated pack,
core package smoke, both net48 test builds, Clipper2 tests/pack/smoke, every benchmark project and PredicateCompiler
passed. Raw step exits are in [perf/foldover-ci.txt](perf/foldover-ci.txt).

The full core suite failed on existing tests: `GeneratedPredicatesIndirectTests.GeneratedFile_IsUpToDate`,
three `RemesherIssueCasesTests` cases (German decimal formatting differs from the pinned English strings),
and the net48 `QualityMesh2CollapsedPartsReportTests.Oracle_ManyCollapsedPointsAreCheap` timing threshold.
The CLI suite failed on the island case of `TrianglePolyCliTests.Poly_Domain_IsWhatTriangleMinusPLeaves`
(test input contains `0,75`, not the expected invariant-format number).

The generator and locale failures reproduce on untouched master `de62b200` in a temporary detached worktree;
that worktree was removed after validation. No unrelated source or generated predicate was changed. Reproduction
commands, first on the feature worktree and then on the detached baseline:

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~GeneratedPredicatesIndirectTests.GeneratedFile_IsUpToDate|FullyQualifiedName~RemesherIssueCasesTests.FlatOpenFan_BoundaryFixed_NoFlippedTriangle|FullyQualifiedName~RemesherIssueCasesTests.CreasedPart_BoundsKept" --logger "console;verbosity=normal"
dotnet test tests\g3cli.Tests -c Release --filter FullyQualifiedName~TrianglePolyCliTests.Poly_Domain_IsWhatTriangleMinusPLeaves --logger "console;verbosity=normal"
```

Baseline outputs: core selection 2 passed / 4 failed; CLI selection 7 passed / 1 failed, with the same messages.
The isolated timing-test replay on the feature passes; this does not retroactively make the full run green:

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net48 --no-build --filter FullyQualifiedName~QualityMesh2CollapsedPartsReportTests.Oracle_ManyCollapsedPointsAreCheap --logger "console;verbosity=normal"
```

The requested independent diff-only numerical reviewer failed before returning any review because its model
provider returned HTTP 502. **No independent review or bounds sign-off is claimed.** These remain publication/
readiness blockers, not permission to modify unrelated exact/meshing code or widen the agreed scope.
The owner subsequently explicitly approved publishing a **draft PR with these blockers open**; it must not
be marked ready or merged without independent numerical review. No merge is performed by this worker.
Update 2026-11-12: the PR is #374; an independent diff-only review was done on 2026-11-04 (its findings and fixes are in
the PR description) and it was merged the same day.

## Estimated versus actual scope and effort

Approved estimate: 400–700 library lines (target <=600, hard ceiling 700), 350–550 test lines, 80–150 benchmark
lines, at most three short work sessions / three hours active work, with CI and review waiting time additional.
Actual file sizes from the following commands are 306 library, 283 test and 128 comparison lines, plus one
benchmark entrypoint line. The implementation required no second optimizer round.

```powershell
(Get-Content parameterization\FoldoverFreeParameterization.cs).Count
(Get-Content tests\geometry3Sharp.Tests\FoldoverFreeTests.cs).Count
(Get-Content benchmarks\ParameterizationBenchmark\FoldoverFreeComparison.cs).Count
```

Wall-time observation (not active effort or a billing measurement):

```powershell
$start=[DateTimeOffset]::Parse('2026-11-04T01:20:10.8877485+01:00')
$now=[DateTimeOffset]::Now
$now.ToString('o')
[Math]::Round(($now-$start).TotalMinutes,2)
```

Observed at `2026-11-04T07:55:23.7789071+01:00`: 395.21 wall minutes. This includes unattended full-CI/provider
waiting and a long owner-decision pause; it must NOT be reported as active implementation time. Active time
was not separately metered. Implementation/targeted checks occurred before the waiting phase; the continuation
classified baseline failures and publishes the explicitly authorized draft, without extending optimizer scope.
The original one-short-session gate was not retroactively declared passed.

## Round-off 2026-11-12

Three open items, measured on Linux net10.0 with `dotnet run --project benchmarks/ParameterizationBenchmark -c Release -- foldover`
(raw output: [perf/foldover-free.txt](perf/foldover-free.txt); the "before" column is the run on master 1f3b6b7 and
equals the table above). Times are single runs, observational.

### 1. Stop rule (Algorithm 1, line 11)

Before, the loop ended on a vanishing Newton decrement *and* epsilon <= 1e-6, so the energy was stationary after a few
iterations (torus: from iteration ~13 on) while epsilon shrank by >= 10 % per iteration down to 1e-6. Now it stops when
no triangle is invalid and an iteration lowers F by less than `EnergyDecreaseTolerance` (1e-3, the paper's value),
both energies at that iteration's epsilon. The final flip-free iterate is still returned; `Converged` is true on all fixtures.

The bare paper rule is **not enough**: with it alone the annulus stops at the first flip-free map (iteration 14,
epsilon 0.26, min det 2.9e-3, symmetric Dirichlet **686.97** instead of 7.46; three peaks 17.15), because F(., epsilon)
is nearly flat there although the unregularized energy is far from converged. Added guard: the rule only fires when
F(x, 0) <= (1 + tol) F(x, epsilon) (for positive determinants F(x, epsilon) <= F(x, 0)), i.e. when the regularization
is negligible. This is our addition, not the paper's.

| Fixture | Iterations before -> after | Symmetric Dirichlet before -> after | ms before -> after |
|---|---:|---:|---:|
| Folded Tutte disc | 115 -> 28 | 5.223873 -> 5.223853 | 46 -> 16 |
| LSCM annulus | 132 -> 41 | 7.462999 -> 7.466767 (+0.05 %) | 35 -> 10 |
| LSCM three peaks | 169 -> 54 | 16.262757 -> 16.246811 | 629 -> 241 |
| Torus cone cut (valid start) | 132 -> 16 | 4.054945 -> 4.055168 (+0.005 %) | 294 -> 49 |

### 2. SLIM quality target: a boundary effect, not a solver effect

Ratio to free-boundary SLIM: disc 1.306, three peaks 2.36, torus 1.012. Attribution on the three-peaks case:

| Run | Symmetric Dirichlet | x SLIM (6.8949) |
|---|---:|---:|
| LSCM start, LSCM boundary fixed (the benchmark row) | 16.2468 | 2.36 |
| same, `EnergyDecreaseTolerance` 0 and 2000 iterations (no stop effect) | 16.2628 | 2.36 |
| LSCM interior, SLIM boundary fixed | 7.4649 | 1.083 |
| SLIM map as start (already valid), SLIM boundary fixed | 7.3035 | 1.059 |

(The tolerance-0 and SLIM-map-start rows come from a one-off probe, not from the committed command; the other rows are in the perf file.) On the folded disc the SLIM boundary gives 4.000000000009 (ratio 1.000000000002). So (a) the stop rule is not the cause,
(b) the gap is the prescribed boundary: LSCM's boundary is itself conformal-stretched, and a fixed boundary cannot
beat the free-boundary optimum; with the same boundary the target (<= 10 %) is met on the disc and on three peaks (1.083).
(c) A residual 4-6 % remains on three peaks even from SLIM's own map: the energy (Garanzha's shape/area energy) is
not symmetric Dirichlet. This is a real property of the method, not fixable by weighting inside the paper's energy.
The annulus has no SLIM reference (InvalidTopology). Nothing to fix in scope; the benchmark now prints
`FoldoverFree-SLIM-boundary` rows and the ratio.

### 3. Torus global-overlap fallback: a limit of fixed-boundary untangling

The cone-cut torus has one boundary loop of 432 edges. Its UV polygon, which this operation must keep bit for bit,
has **4 proper self-crossings**, and **all 8 overlapping triangle pairs touch the boundary** (`torus-boundary` line in the
raw output, test `TorusCut_LocalSuccessDoesNotClaimGlobalInjectivity`). A locally injective map of a disc is globally
injective iff its boundary curve is simple; with a self-crossing boundary no interior placement can be injective.
SLIM's own free boundary also leaves 8 pairs here. The counterfactual fallback count stays 1 -> 1. The paper (3.3) names the
same distinction (inversion freedom is not injectivity). A fix needs a different boundary (cutting or cone placement),
which is outside fixed-boundary untangling and has no paper-backed method in Garanzha et al.; not implemented.

Tests: `StopRule_*`, `EnergyDecreaseTolerance_IsValidated_AndZeroRunsLonger`. Hand mutants (killed, 2 tests each):
guard `return true` (annulus, three peaks), stop replaced by `epsilon <= 1e-6` (tolerance test, three peaks).
