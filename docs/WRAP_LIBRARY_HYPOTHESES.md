# Library-level hypotheses behind rejected wrap-quality trials

Status: bounded red-test investigation and the two authorized fixes completed on 2026-10-29.
Neither defect is established as the root cause of the rejected wrap-quality trials. The original red evidence
is preserved below; subsequent fix validation is recorded separately. Local checks pass; the independent
static review was retried at the owner's request and returned no material findings. Full pre-push CI and
package/API validation have not been run, so this is not a merge-ready claim.

## Scope and evidence limits

The experiments exercise a chain of reduction, spatial queries, normal/intersection checks, distance certification,
and sampled/discrete fidelity measurements. A rejected experiment does not prove that every dependency is correct,
nor that the algorithmic idea is intrinsically incapable of meeting the targets.

Conversely, none of the suspected library defects below has been demonstrated to cause a trial failure. Existing
logs report mesh-level aggregates, not the offending collapse, sample correspondence or triangle patch. Do not
attribute a failure to an individual operation from these logs. The evidence section below distinguishes newly
executed tests, existing QA work, and checks that remain unperformed.

The [benchmark record](../benchmarks/MeshRepairBenchmark/README.md) contains the original commands, measured
before/after values and immutable raw-run locations. Relevant evidence is under its ignored
`results/alpha-quality-20261029/` directory:

- `reviewed-paired-q030.json` and `reviewed-paired-local-rollback.json`: worst-element quality, curvature,
  sampled-distance and reversed-normal failures recur across different policies.
- `guarded-feature-trial-20261029/guarded-features-paired.json`: the guarded reducer still has those failure
  classes; the control and candidate also both time out on `96373`.
- `candidate-probe-20261029/summary.json` and `candidate-timeout-followup-20261029/summary.json`: some local
  triangle checks and global threshold decisions differ; finer global checks sometimes certify formerly
  inconclusive candidates. This is evidence of different stopping contracts, not evidence of an invalid bound.

No acceptance threshold, production default or historical failure classification changes because of this backlog.

## Prioritized checks

### H1 — Sample correspondence or pseudo-normal classification could amplify small fidelity changes

**Priority:** first. **Status:** no defect reproduced by the bounded planar/creased-surface tests below;
sampling sensitivity and historical sample correspondences remain unresolved.

Evidence: `fm_reversed_fwd` changes from a zero baseline to a small positive fraction on some failed cases;
`fm_max_fwd` also recurs. [MeshFidelityMetrics](../mesh_ops/MeshFidelityMetrics.cs) generates seeded,
area-stratified samples and uses nearest-point feature pseudo-normals. A fixed seed guarantees repeatability
for fixed inputs, not the same physical sample points after a retriangulation. Nearest-feature ties can also
change the selected face/edge/vertex normal. A share's sampling increment is not its statistical uncertainty.

Possible defects to exclude: inconsistent nearest-feature classification at shared edges/vertices; orientation
or tie-handling errors; a changed sampling population incorrectly interpreted as a geometric regression.
Alternative explanation: a real local fold or a correctly measured change in discretization/sampling.

Discriminating test:

1. Generate a planar patch and a creased surface procedurally; change triangle order/vertex IDs, then retriangulate
   coplanar faces without changing the represented surface or winding.
2. Query fixed physical points, including shared-edge/vertex ties and points on both sides; compare the tree query
   with exhaustive triangle queries and known analytic normals.
3. Separately compare the existing seeded sampler across prescribed seeds/sample counts. This is a diagnostic
   sensitivity study, not a replacement seed or a way to select a passing score.
4. For a real failed case, capture the offending sample, closest point, triangle IDs, feature classification and
   normal dot product; reproduce it with a small procedural fixture before changing library code.

Existing coverage to extend: `MeshFidelityMetricsTests.Determinism_ParallelEqualsSerial_RepeatedRuns`,
`Soup_SameSurfaceMeasuresAsWelded`, and the analytic plane/sphere fixtures.

### H2 — Discrete curvature and invalid-vertex handling could distort the fidelity gate

**Priority:** first, alongside H1. **Status:** no defect reproduced by the bounded analytic mixed-area,
degenerate-triangle and power-of-two scaling tests below; estimator/correspondence sensitivity remains unresolved.

Evidence: `fm_k_err`, `fm_k_w1`, `fm_h_err` and `fm_h_w1` appear in failed reports.
[MeshCurvature](../mesh_ops/MeshCurvature.cs) uses mixed areas, angle deficits and validity masks;
[MeshFidelityMetrics](../mesh_ops/MeshFidelityMetrics.cs) interpolates curvature at corresponding samples.
Changing triangulation changes a discrete curvature estimator even without a large Hausdorff change.
Small mixed areas, near-degenerate triangles and changes in the valid sample population deserve scrutiny.

Possible defects to exclude: normalization/scale errors, mishandled invalid contributions, correspondence
interpolation errors or incorrect area weights. Alternative explanation: legitimate discretization sensitivity
or genuine loss of curved/creased features. A changed discrete curvature value alone does not prove a bug.

Discriminating test: analytic planes, cylinders and spheres with regular versus deliberately skew triangulations;
check mixed-area sum, validity population, integrated angle deficit where the topology assumptions apply,
refinement behavior and similarity scaling. Compare interpolation at fixed physical correspondences separately
from sampling. Inspect the largest contributing local patches in a failed case, not only the aggregate score.

Existing coverage: `MeshCurvatureTests.Sphere_MeanAndGaussian_ConvergeUnderRefinement`,
`Plane_ZeroEverywhere_BoundaryOneSided`, `MeshFidelityMetricsTests.Curvature_IdenticalAndRefinedSpheres` and
`Curvature_PlaneAndCylinder_OpenAndDegenerate`. These reduce the likelihood of a gross formula error but do not
settle the problematic near-degenerate/correspondence cases.

### H3 — Distance-certification integration could cause conservative rejection, or conceal a shared defect

**Priority:** second. **Status:** conservative rejection observed; invalid bound not demonstrated.

[check_wrap_stage](../mesh_ops/MeshRobustRepair.cs) requires the upper bound below the acceptance threshold.
[MeshHausdorffDistance](../mesh_ops/MeshHausdorffDistance.cs) defines convergence by interval width instead.
A converged interval can straddle the threshold. Likewise, a depth-limited per-triangle check can be unproven
while a different global subdivision establishes the bound. Both behaviors can be correct.

Important limitation: the global estimator and local reducer certificate both use
[TriangleDeviationBounds](../queries/TriangleDeviationBounds.cs). Their agreement is not independent validation
of the bound implementation. Using the same bound in both a guard and its test can miss a correlated defect.

Discriminating test: analytic parallel-plane/single-triangle cases with known maxima; degenerate and near-edge
configurations; exhaustive nearest-triangle queries independent of the spatial accelerator. Verify interval
containment and threshold decisions separately. Dense sampling can disprove an upper bound by finding a witness
above it, but cannot prove a bound. Investigating or changing error bounds requires the repository's tier-L
bounds-review process; do not quietly adjust the tolerance to make a failed candidate pass.

Existing coverage: `HausdorffDistanceTests.ParallelPlanes_Offset`,
`UpperBound_ValidWithCoarseToleranceAndWithoutConvergence`, `Reuse_And_PrebuiltSpatial_SameBounds`, and
`Invariant_UnderSimilarities`.

### H4 — Incremental spatial data could become stale after a local edit

**Priority:** second if a concrete edit/query discrepancy is found. **Status:** unverified risk, no observed stale entry.

[Reducer](../mesh/Reducer.cs) and the local wrap editor update a mutable triangle grid after accepted changes.
A missed removal/reinsert or incorrect affected-triangle set could miss an intersection, or reject a valid
prospective edit. Final cleanliness checks reduce the risk of accepting bad geometry, but would not restore
an improvement discarded because of a false positive.

Discriminating test: on small procedural closed meshes, compare the incremental grid after every accepted
collapse/move/flip against a fresh rebuild and exhaustive triangle-pair checks. Also test rejected edits and
reuse, which must leave geometry and effective query results unchanged. Isolate accelerated-query completeness
from intersection-predicate correctness; using the same predicate on exhaustive pairs tests the former only.
Do not attribute the measured slowdown or fidelity errors to this hypothesis without a reproducible witness.

### H5 — Reducer queue precision or candidate bookkeeping could affect the selected sequence

**Priority:** budget contract fixed before the positional experiment. **Status:** the double-budget violation
was reproduced on net10.0 and net48 and is now covered by passing regressions; its contribution to historical
trial failures remains unproven.

[Reducer.InitializeQueue / UpdateNeighbours](../mesh/Reducer.cs) stores priorities as floats while evaluating
quadrics and the stopping threshold as doubles. Queue order and threshold decisions near ties may therefore
be sensitive to rounding. The existing collapse guards also inspect the current neighborhood after dequeue.
An alternative-position implementation must evaluate its actual selected point against the quadric budget,
not assume the dequeued QEM-optimum priority certifies every alternative point.

Discriminating test: procedurally construct edges with almost equal errors and points immediately around the
budget; assert the actual accepted position's error, repeatability, ID-order behavior and power-of-two
invariance. Keep this as an explicit contract test before altering queue logic. Tiny priority rounding alone
would not explain all measured curvature, quality and runtime failures.

## Confirmed instrumentation issue, contribution to failed trials unknown

The original actual-result path in [MeshRepairBenchmark/Program.cs](../benchmarks/MeshRepairBenchmark/Program.cs)
called `result.CheckValidity(false, FailMode.ReturnOnly)` but ignored its boolean return. `after_valid` became
false only when an exception was thrown. The candidate-probe path already assigned the return value correctly;
the actual-result path now does too.

This is a concrete reporting defect when validity returns false without throwing. It can falsely report a bad
mesh as valid; its direction is **false acceptance**, not an explanation for the current excessive runtime,
poor angles or fidelity rejections. There is no evidence here that a recorded trial mesh exercises it.
Do not silently rewrite old measurements.
The procedural source-adapter regression below reproduced `actual validity=False; after_valid=True` by compiling
the reporting block before the fix. It is not an end-to-end repair test. The subsequent fix and its passing
regression are recorded in the fix-validation section.

## Do not confuse intended behavior with defects

- `MinCollapseTriangleQuality` is a soft guard: it rejects a new triangle only when it is below the threshold
  **and** worse than the corresponding old triangle. It is not an absolute minimum-angle guarantee.
- Worst quality can change with the triangulation and with which triangles survive. A failing worst-angle gate
  does not by itself establish a broken guard. A new test should distinguish the soft per-edit contract from
  the experiment's global quality objective.
- A one-sided wrap-distance certificate neither preserves discrete curvature nor guarantees all input features
  survive. Different fidelity metrics test different properties.
- `incomplete` is not `exceeded`; a timeout is not a geometric failure. Both still prevent acceptance under the
  existing trial rules.

## Original red-test evidence (2026-10-29, before fixes)

This section records the bounded tests-only investigation (tier M), before production fixes. Fixtures are
procedural. These red tests exercised the unchanged defective paths, rather than introducing mutations and
presenting those failures as production defects. Commands/results here describe that earlier source state;
rerunning against the fixed code is expected to pass. No full-CI or push is claimed.

### New hypothesis tests

Run from the repository root with the existing xUnit/VSTest setup:

```powershell
dotnet test tests\geometry3Sharp.Tests\geometry3Sharp.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WrapFidelityHypothesisTests|FullyQualifiedName~WrapCurvatureHypothesisTests|FullyQualifiedName~ReducerQuadricBudgetTests' -v minimal
```

Final raw output: `hypothesis-combined-final.log` in this investigation's persistent session files. On each of
net10.0 and net48: 10 tests, 9 passed, 1 failed, 0 skipped; expected exit code 1. The sole failure is
`MaxQuadricError_DoubleBudgetBelowFirstError_DoesNotAcceptRoundedDownPriority`.

- [WrapFidelityHypothesisTests](../tests/geometry3Sharp.Tests/WrapFidelityHypothesisTests.cs): four passing cases.
  Planar and creased patches use both face diagonals and reversed vertex IDs. Fixed face/edge/vertex queries
  check analytic normals through the actual private fidelity adapter via reflection. Same-surface comparisons
  use seeds 1, 7 and 31; no distance or reversal regression appears within the documented `1e-7` roundoff
  tolerance. This is not exhaustive nearest-query differential coverage or a historical sample reconstruction.
- [WrapCurvatureHypothesisTests](../tests/geometry3Sharp.Tests/WrapCurvatureHypothesisTests.cs): five passing cases.
  Right, obtuse, equilateral, coincident-corner and nearly collinear triangles check analytic mixed areas,
  finite values, boundary/degenerate validity handling and angle deficits. The right/obtuse/degenerate cases
  include scales `2^-20`, `1` and `2^20`. These do not assert general retriangulation-invariance of discrete curvature.
- [ReducerQuadricBudgetTests](../tests/geometry3Sharp.Tests/ReducerQuadricBudgetTests.cs): one red case.
  A procedural closed sphere records the double quadric error at each actually accepted keep-vertex position.
  Mesh validity and closure are checked; every accepted error must stay within the configured double budget.

The reducer witness printed by that command is identical on both frameworks (decimal separators normalized here):

```text
radius=1
exactMinimum=0.01768243700117722
queuedMinimum=0.017682436853647232
budget=0.017682436927412226
acceptedError=0.01768243700117722
acceptedError=0.017682437001180773
acceptedError=0.01768243700117722
acceptedError=0.017682437001184326
Assert.All() Failure: 4 out of 4 items in the collection did not pass.
```

In the defective version, the float priority rounded down; the queue stop compared that rounded value with
the double budget, with no final double-budget check before acceptance. The current wrap path actually calls `ReduceToMaxQuadricError`
in [MeshRobustRepair](../mesh_ops/MeshRobustRepair.cs), so the contract matters to it. The witness establishes
a small budget overshoot, not an explanation for the much larger measured quality, fidelity or runtime failures.
No before/after wrap-quality or performance improvement is claimed; those comparisons have not been rerun.

### Reporting adapter red test

With the Release netstandard2.0 library already built, run in Windows PowerShell:

```powershell
& .\benchmarks\MeshRepairBenchmark\test_validity_reporting.ps1
```

[test_validity_reporting.ps1](../benchmarks/MeshRepairBenchmark/test_validity_reporting.ps1) extracts and compiles
the exact actual-result reporting block from the current benchmark source. It constructs an otherwise structurally
valid bowtie and exercises a false return without an exception. The run exits 1 as expected:

```text
source-adapter: actual validity=False; after_valid=True
REGRESSION: after_valid ignores the false CheckValidity return
```

Raw output: `hypothesis-validity-reporting.log` in the persistent session files. The reporting path is proven wrong;
neither a historical trial's invalidity nor an end-to-end repair failure is established by this adapter test.

### Existing contracts rerun

```powershell
$filter = 'FullyQualifiedName~HausdorffDistanceTests.ParallelPlanes_Offset|FullyQualifiedName~HausdorffDistanceTests.UpperBound_ValidWithCoarseToleranceAndWithoutConvergence|FullyQualifiedName~HausdorffDistanceTests.Reuse_And_PrebuiltSpatial_SameBounds|FullyQualifiedName~HausdorffDistanceTests.Invariant_UnderSimilarities|FullyQualifiedName~MeshCurvatureTests.DegenerateTriangles_FiniteAndGaussBonnetKept|FullyQualifiedName~MeshCurvatureTests.NonFiniteAndHugeCoordinates_NoNaN|FullyQualifiedName~MeshCurvatureTests.Invariance_SimilarityAll|FullyQualifiedName~MeshFidelityMetricsTests.Determinism_ParallelEqualsSerial_RepeatedRuns'
dotnet test tests\geometry3Sharp.Tests\geometry3Sharp.Tests.csproj -c Release --no-restore --filter $filter -v minimal
```

Raw output: `hypothesis-existing-contracts.log`. On each of net10.0 and net48: 8 passed, 0 failed, 0 skipped;
exit code 0. These are existing analytic, reuse, similarity and determinism controls, not a new independent proof
of every deviation bound. H3 remains unproven; H4's mutable triangle grid has not been runtime-tested here.

## Existing task and draft-PR overlap

GitHub open-issue listing on 2026-10-29 returned no open issues. The relevant existing QA work is
[draft PR #193](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/193),
`qa/differential-core-tests`, still open and draft at published head
`e559f6870d9cf5202f7f7da85ac48b4179b50ed7`. Its safeguard description is older than that head: the description
still lists defects as open that the current head's QA plan marks implemented. Inspect the head, not only the body.
The separate QA checkout was inspected read-only with:

```powershell
git -C 'D:\work\geometry3Sharp-QA' --no-pager diff 93880ed e559f6870d9cf5202f7f7da85ac48b4179b50ed7 --name-only
git -C 'D:\work\geometry3Sharp-QA' --no-pager show e559f6870d9cf5202f7f7da85ac48b4179b50ed7:docs/QA_TEST_PLAN.md
```

| Topic | Existing coverage at that head | Remaining distinction |
|---|---|---|
| H1/H3 spatial dependencies, part of H4 | T1 AABB differential tests and empty/stale-tree fixes | Not a reproduction of a failed wrap sample or an independent bounds proof |
| General mesh prerequisites | Isolated-vertex compaction, restore reuse, boundary-loop diagnostics and coplanar-contact fixes | Final local CI and production review still listed as gates |
| H2 curvature/correspondence | No matching curvature fix in the inspected PR diff | Bounded controls pass here; historical sensitivity unresolved |
| H5 float queue versus double budget | Not covered in the inspected PR diff | Red procedural witness retained here |
| Benchmark validity reporting | Not covered in the inspected PR diff | Red source-adapter witness retained here |
| H4 mutable triangle grid after edits | Not covered by the AABB stale-tree fix | Targeted differential edit/query investigation still needed |

This is a snapshot of the inspected issue list and PR head, not a claim that no other future or private task exists.
Do not duplicate the AABB work, cherry-pick it silently, or treat the draft as merged/fully validated.

## Reducer/remesher backlog cross-check (2026-10-29)

Before completing the newly authorized fixes, the existing backlog was checked again:

- [ROBUSTNESS_SWEEP R7](ROBUSTNESS_SWEEP.md): float-queue ties on symmetric inputs can change the collapse
  sequence after translation. This was explicitly reclassified as **not a bug**. Preserve approximate queue
  ordering; do not confuse sequence differences with the newly demonstrated violation of a double budget.
- [ROBUSTNESS_SWEEP R13](ROBUSTNESS_SWEEP.md): non-finite reducer input/parameters remain deferred, including
  `ReduceToMaxQuadricError(NaN)` and `ReduceToEdgeLength(NaN)`. The remesher portion was fixed in #163. This is
  adjacent to the budget guard: cover NaN-budget behavior, but do not claim that this fixes general non-finite
  mesh handling or the separate edge-length mode.
- [ROBUSTNESS_SWEEP R14](ROBUSTNESS_SWEEP.md): reducer behavior at extreme small scales remains deferred;
  absolute length and grid-cell thresholds are already identified. It is separate from budget rounding.
- [ROADMAP phase 2](ROADMAP.md#phase-2-robustness): wrap post-simplification quality, remesher
  degenerates, self-intersections, thickness-aware sizing and vertex-feature detection are already planned.
  Existing guards are partial: preserving orientation does not prevent self-intersections, and crease-edge
  detection does not preserve every sharp vertex. Use the existing procedural cases in
  [RemeshQualityBenchmark](../benchmarks/RemeshQualityBenchmark/README.md), not a duplicate broad investigation.
- [ROADMAP phase 5](ROADMAP.md#phase-5-candidates-from-recent-research) and
  [ReducerBenchmark](../benchmarks/ReducerBenchmark/README.md): faster deviation certification is already a
  research item. The exact clipping envelope prototype was measured and rejected as a drop-in performance
  replacement; it is not a ready fix for reducer runtime.
- [Terrain implementation plan](design/TERRAIN.md#11-implementation-plan) gives terrain priority over the
  planned broader remesher work. The two demonstrated defect fixes do not initiate that larger workstream.

The open-PR list still contains only draft #193 at the head recorded above; targeted open-PR searches for
`reducer` and `remesh` returned no additional matches. No new issue or duplicate implementation was started.
The authorized fix scope remains the actual-position double-budget contract and benchmark validity reporting;
all broader items stay in their existing backlog. The focused fixes and validation follow.

## Fix validation (2026-10-29)

The authorized tier-M fixes preserve public method signatures and the float queue. No exact arithmetic,
deviation bounds, remesher defaults, historical result files or acceptance thresholds were changed.

- `Reducer.DoReduce` now inspects the complete rounded budget bucket instead of rejecting a double-affordable
  candidate whose float priority rounded up. `CollapseEdge` checks the actual position's double quadric error
  after constraints/projection and before mesh mutation. Over-budget candidates are skipped, not used to end
  the whole bucket. The guard is scoped to budget-mode calls and reset with `finally`, including exceptions;
  `FastCollapsePass` and count/length targets do not inherit it.
- NaN budgets accept no collapses. This covers only that subcase of R13, not non-finite mesh input or
  `ReduceToEdgeLength(NaN)`. `AfterRefinement` projection remains outside the quadric budget contract.
- The benchmark assigns the `CheckValidity` return value. Its source-adapter regression covers false, true
  and exception results; it is still not an end-to-end repair-pipeline test.

### Red/green and regression commands

Both new checks were first observed red on the unchanged defective implementations (original evidence above).
To validate the extended budget tests, the final-position guard and corrected queue cutoff were temporarily
disabled, and the benchmark assignment was reverted. Commands:

```powershell
dotnet test tests\geometry3Sharp.Tests\geometry3Sharp.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ReducerQuadricBudgetTests' -v minimal
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks\MeshRepairBenchmark\test_validity_reporting.ps1
```

Mutation outputs: `budget-fix-mutation.log` and `validity-fix-mutation.log` in the persistent session files.
The then-current budget test set had 4 failures / 5 passes on each framework (exit 1): downward rounding,
upward rounding/equal budget, fixed-endpoint adjustment, and NaN budget. Reporting also exited 1 with
`actual validity=False; after_valid=True`. All temporary production mutations were restored. The final suite
also includes rounded-bucket continuation, exception cleanup, cross-mode reuse, repeated budget calls and
repeated runs, plus rejection invariance over `Similarity.All()` with bit-identical power-of-two results.
Reducer has no serial/parallel configuration switch; the repeated-run tests exercise its normal parallel setup.

Final combined regression command:

```powershell
$filter = 'FullyQualifiedName~ReducerQuadricBudgetTests|FullyQualifiedName~WrapFidelityHypothesisTests|FullyQualifiedName~WrapCurvatureHypothesisTests|FullyQualifiedName~DeterminismTests.Reducer_RepeatedRuns_BitIdentical|FullyQualifiedName~InvarianceTests.Reducer_SameResultUnderSimilarity|FullyQualifiedName~FeaturePreservationTests.Reducer_|FullyQualifiedName~MeshRobustRepairTests.GuardedFeatureQuadrics'
dotnet test tests\geometry3Sharp.Tests\geometry3Sharp.Tests.csproj -c Release --no-restore --filter $filter -v minimal
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks\MeshRepairBenchmark\test_validity_reporting.ps1
dotnet build benchmarks\MeshRepairBenchmark\MeshRepairBenchmark.csproj -c Release --no-restore -v minimal
```

Results:

| Command | Before fixes / mutation | Fixed source |
|---|---|---|
| Original hypothesis filter | 9 passed / 1 failed on each framework | Included in the combined regression below |
| Final combined regression filter | Not run with this final expanded filter before fixes | 34 passed / 0 failed on each of net10.0 and net48; exit 0 |
| Reporting adapter | `actual=False; after_valid=True`; exit 1 | `actual=False; after_valid=False`; valid mesh true; exception false; exit 0 |
| Benchmark build | Not part of the defect witness | Build succeeds, 0 warnings / 0 errors; exit 0 |

Raw outputs: `budget-fix-regression.log`, `validity-fix-green.log`, `validity-fix-build.log`. Existing unrelated
analyzer warnings remain in the test builds. VS Code reported no diagnostics for the changed implementation,
regression-test and adapter-script files. No full local-CI, package-validation, push, merge or
wrap-quality/performance improvement is claimed.

### Follow-up control-flow mutations and review status

A further mutation check exposed a weakness in the synthetic tied-priority test: the heap promotes later
insertions on ties, so affordable candidates could be processed before the over-budget candidates. The fixture
now inserts in reverse ID order and explicitly checks that its first candidate has the higher double cost.
No production change was needed.

With a premature `break` after a budget rejection and the `finally` reset temporarily removed, run:

```powershell
$filter = 'FullyQualifiedName~ReducerQuadricBudgetTests.RoundedTie_OverBudgetCandidateDoesNotHideAffordableCandidates|FullyQualifiedName~ReducerQuadricBudgetTests.InterruptedBudgetCall_DoesNotLeakBudgetIntoFastCollapsePass'
dotnet test tests\geometry3Sharp.Tests\geometry3Sharp.Tests.csproj -c Release --no-restore --filter $filter -v minimal
```

Both selected tests fail on each framework (2 failed / 0 passed, exit 1), recorded in
`budget-controlflow-mutation.log`. After restoring both production guards:

```powershell
dotnet test tests\geometry3Sharp.Tests\geometry3Sharp.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ReducerQuadricBudgetTests' -v minimal
```

All 10 reducer-budget tests pass on each framework (exit 0), recorded in `budget-final-strengthened.log`.
The earlier combined regression remains the evidence for unchanged adjacent functionality.

The first independent code-review attempt returned only a provider HTTP 502 failure after its automatic
retries; it produced no review result. At the owner's explicit request, a new independent review was run
against the final scoped diff and regression files. It returned no material findings.

Review limits: static inspection only, without rerunning tests, builds, mutations or package/API validation.
The new tests do not specifically exercise inline/constraint projection targets (the fixed-endpoint adjustment
is covered); the PowerShell regression exercises the extracted reporting adapter, not the full benchmark
pipeline. Existing local execution evidence is recorded above. Full pre-push CI and package/API validation
remain outstanding; no push or merge was performed.

## Next action and stop rule

The defect fixes do not establish a solution to wrap quality. Before evaluating the approved positional
experiment, create new control/candidate records with the corrected implementation and reporting, without
rewriting historical records. Broader remesher, extreme-scale and non-finite-input work stays in its existing
backlog. H1/H2 controls are reassuring only for their fixtures, and H3/H4 remain open where stated above.
Do not tune an experimental algorithm to compensate for a faulty metric, and do not relax gates to hide noise.
