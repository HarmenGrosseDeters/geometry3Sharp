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
| Folded Tutte disc | 4 + 0 -> 0 + 0 | 4 / 5.2237076566121 | 1.305926914153025 | 54 -> 0 |
| LSCM annulus, 8 sides/3 strips | 1 + 1 -> 0 + 0 | unavailable / 686.9691923981898 | unavailable | 5 -> 0 |
| LSCM three-peak terrain, 30 x 30 cells | 19 + 0 -> 0 + 0 | 6.894915679752159 / 17.150550444535156 | 2.4874198962142553 | 2486 -> 1344 |
| Cone torus cut (UVAtlasTests ordering) | 0 + 0 -> 0 + 0 | 4.005961599952599 / 4.061251309608658 | 1.0138018571263174 | 8 -> 8 |

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
