# Brief 1: fixed-boundary foldover-free maps

Date: 2026-11-04. Tier L. **Owner approved the design and bounded budget exception, and explicitly accepted
hand mutations without mutation-tooling changes, at the design checkpoint.**

## Scope, isolation and budget

One additive operation, `FoldoverFreeParameterization(DMesh3 mesh, IList<Vector2d> startUV)` with
`Compute()` returning `ParameterizationResult`. All boundary vertices are fixed; no free-boundary mode,
extra interior pins, automatic cutting, atlas/cone hook, native dependency, general optimizer or parallel path.
Existing defaults and upstream 1.0.324 compatibility remain unchanged. Briefs 2 and 3 are outside this work.

Dedicated branch `feature/foldover-free-maps`, created in this isolated worktree from fetched `origin/master`
at `de62b200` (`git fetch origin; git log -1 --oneline origin/master; git switch -c feature/foldover-free-maps origin/master`).
The original checkout, other worktrees and research PR #364 are not modified. No merges by this worker.

**Requested budget exception, not a passed small-effort gate:** at most three short work sessions and three
hours of active implementation/validation/reporting work after approval, plus machine waiting time for full CI
and independent review. Active diagnosis/review fixes count toward the cap. Estimated scope remains
400–700 library lines, 350–550 test lines and 80–150 benchmark lines; target <=600 library lines, hard ceiling
700 pending this approval. These are estimates, not measured results. Stop and report before exceeding the cap;
no fourth session, optimizer replacement or scope expansion without another decision.

## Prior art and local building blocks

- [Garanzha et al. 2021, paper](https://arxiv.org/html/2102.03069v1), sections 2.2/2.3, equation 6,
  section 4.1/Appendix A: regularized determinant energy, analytic gradient and modified positive-definite
  Hessian. Section 4.2 proves conditional finite untangling for an idealized optimizer, not this bounded solver.
  Section 3.3 distinguishes inversion freedom from global injectivity and discusses conditioning.
- [SLIM publication](https://igl.ethz.ch/projects/slim/): barrier/proxy optimization preserves an already
  locally injective map; it is not an inverted-start untangler. Publication only, no linked implementation.
- TinyAD's previously MIT-checked README describes element-wise differentiation and sparse Newton assembly;
  useful architecture, not a managed dependency. No additional TinyAD or authors' implementation code read.

**How others solve it:** determinant regularization plus nonlinear descent (Garanzha), flip-preserving barriers
(SLIM), or per-element automatic differentiation and sparse assembly (TinyAD). Use the first approach and the
existing solver; do not build BFGS or AD infrastructure.

Local reads on the fetched baseline confirm [ParamMesh, TriangleFrame and ConstrainedQuadratic](../../parameterization/MeshParameterization.cs)
provide topology/indexing, Jacobians, fixed-variable elimination and sparse Cholesky solves.
[SLIM](../../parameterization/SLIMParameterization.cs) rejects flipped starts; Tutte changes boundary targets.
`ParamMesh.MakeResult` alone does not reject inverted triangles. [UVOverlap](../../parameterization/UVOverlap.cs)
remains the separate overlap checker; none of its exact dependencies is changed. ROADMAP Phase 3's torus
fallback is a global-injectivity issue, not proof that this new method will remove it.

## Numerical design

1. Snapshot the supplied UVs per used vertex on every Compute; do not change the input mesh or list. Validate
   missing/non-finite coordinates, topology, options and all surface triangles. Reject degenerate surface
   triangles rather than silently skipping them. Accept one connected orientable open component, including
   multiple boundary loops; unsupported topology gets `InvalidTopology`. No assertion that every accepted
   fixed boundary admits a solution.
2. Work on a private normalized mesh/UV representation. Use a deterministic used-vertex origin and power-of-two
   scales from used geometry and UV extents; unused outliers must not affect normalization. Construct triangle
   frames after normalization, not from overflowing original coordinates. Zero UV extent uses a finite unit
   scale so collapsed starts can be diagnosed. Check subtraction/scaling, areas, determinants, objective,
   derivatives, directions and restored output for representability; fail diagnostically rather than clamp.
   The review must cover mixed exponents and normalization thresholds. Exact arithmetic is not an escape hatch.
3. In 2D use the area-weighted mean of
   `phi(J, epsilon) = (||J||_F^2 + lambda * (det(J)^2 + 1)) / chi(det(J), epsilon)`, with fixed `lambda = 1`.
   `chi(D,e) = (D + hypot(D,e))/2`; for negative D evaluate its rationalized equivalent without subtractive
   cancellation. Use scaled hypot and staged products; detect underflow rather than divide by a lost chi.
4. Derive the analytic gradient and the paper's modified Hessian via section 4.1's convex lifted function
   `Phi(a,D)`, linearizing chi at the current determinant and omitting second derivatives of chi and det.
   Assemble per-triangle contributions into `ConstrainedQuadratic` with interleaved u/v unknowns and zero
   displacements for fixed vertices. Solve `H+ * delta = -gradient`. No eigenvalue projection, generic optimizer,
   numerical differentiation in production, hidden random perturbation or fallback solver.
5. Deterministic finite Armijo backtracking at fixed epsilon; reject non-finite candidates. Start epsilon at 1
   and update using equation 6, with sigma = max(0.1, 1 - newEnergy/oldEnergy), both energies at the SAME epsilon.
   A finite positive epsilon floor reports stalling if invalid triangles remain. Proposed public controls are
   `MaxIterations`, `InitialEpsilon`, `MinimumEpsilon`; schedule is equation 6 only. Proposed initial iteration
   cap is 200 and backtracking cap 40; these are bounded starting choices, not validated defaults.
6. Keep the best finite accepted map (including the start), ordered by invalid-triangle count, then energy
   evaluated at the common initial epsilon. Never compare objective values across changing epsilon as a
   best-map criterion. Preserve a validated flip-free candidate once found. Continue bounded quality descent;
   no automatic SLIM polish to manufacture a distortion target.
7. Restore boundary coordinates by copying the original doubles, not by inverse normalization. Validate the
   restored map, including all finite UVs and distortion coverage, before Success. Success requires zero
   flipped AND zero degenerate UV triangles; it is a local result, not a global-injectivity claim.
   Iteration exhaustion/stalling with invalid triangles returns `FlippedTriangles`, finite best UVs and
   diagnostics. Invalid input/constraints and factorization failures retain their distinct statuses; they
   never become Success. `Converged` means the stopping criterion was met, not merely that a map was retained.

Obvious infeasibility (e.g. coincident endpoints of a locked boundary edge, or an invalid fully locked triangle)
is `InvalidConstraints`, with a reason. A near-collapse or an unsuccessful solve is NOT a proof of infeasibility.
No general boundary-feasibility solver is included. Fixed-boundary values are never moved to obtain success.

## Acceptance and the two-round stop rule

Before the first folded-disc optimization run, record round 1, its code revision/diff, parameters and exact
command. A round is one bounded optimizer configuration evaluated on the frozen procedural folded-disc fixture,
not one nonlinear iteration. Round 2 is at most one evidence-led corrective configuration/algorithm revision;
all optimizer retuning after a failed round consumes it. Do not weaken the fixture, move the boundary or reset
round counting. Compilation-only repairs and unrelated validation do not count as optimizer success.
**If both rounds fail to reach zero flips/degeneracies, stop implementation and report commands, parameters,
iteration counts, final flips/degeneracies and the relevant paper limitation.** No third optimizer search.

- Disc: generate Tutte UVs, deliberately move interior vertices across edges; prove the start is invalid, then
  check every returned triangle and bitwise-identical boundary. Also test degenerate interior starts.
- Kernel oracle: finite-difference gradient checks on regular, inverted and degenerate Jacobians; independent
  evaluation of the lifted quadratic form for the modified Hessian and descent. Do not compare the modified
  Hessian to the full true Hessian. Hand mutants cover derivative sign, locked-boundary handling, epsilon
  update and final-success validation; force rebuild after mutation and restoration.
- Reuse twice and A/B/A input/options versus fresh instances; deterministic repetitions; input immutability;
  `Similarity.All()` with bit-identical power-of-two behavior and appropriate other-transform tolerances;
  invalid/missing/non-finite UVs, unsupported meshes, fixed infeasibility, all-fixed valid triangles, finite
  retained failure maps, exhausted iterations and representability/conditioning failures.
- Recreate relevant procedural `ParameterizationFlippedMapTests` / `ParameterizationIssueCasesTests` inputs.
  Measure actual LSCM boundary values, especially the uncut annulus; classify proven infeasible, feasible or
  undetermined constraints explicitly. Compare SLIM from the SAME invalid start (including its rejection),
  and its normal free-boundary baseline separately. Report flips, degeneracies, symmetric Dirichlet and
  boundary policy. The <=10% target is empirical; unfavorable cases remain visible, not weakened tests.
- Reproduce ROADMAP's cone/torus cut case in benchmark-only instrumentation. Record stock fallback count,
  repaired cone candidate acceptance under the existing overlap AND distortion checks, and counterfactual
  fallback count. Report local flips and global overlap separately. The production atlas stays unchanged.

## Files and verification

Implementation: `parameterization/FoldoverFreeParameterization.cs`; tests:
`tests/geometry3Sharp.Tests/FoldoverFreeTests.cs`; small comparison modes in the existing
`ParameterizationBenchmark` / `UVAtlasBenchmark`. Documentation: this design, SOURCES, OPERATIONS and the directly
relevant ROADMAP status. No mesh assets, restricted arithmetic, meshing, terrain or mutation-tooling edits.

**Coverage finding requiring explicit acknowledgement:** current mutation configs/workflow contain no
parameterization shard or parameterization test filter/path. A renamed FoldoverFree test cannot legitimately
satisfy a nonexistent shard. Proposed resolution is targeted hand-mutation evidence for this module, leaving
all existing shards/tooling untouched; do not claim Stryker coverage. If an automated shard is required instead,
stop for coordinator-owned tooling work rather than edit it here.

Run the smallest focused tests/benchmarks first. Before EVERY implementation push, run the FULL CLAUDE local
CI list: both core/CLI suites; core pack/API validation and smoke; net48 tests build; Clipper2 suite, pack,
smoke and net48 build; every benchmark build; PredicateCompiler build; verify net10.0/netstandard2.0 coverage.
Before ready: independent diff-only review using REVIEW_CHECKLIST, explicitly including numerical bounds and
normalization, fixes and affected revalidation. One feature PR against master, no design-only sub-PR. Include
approval, prior art, estimated versus actual active effort, raw commands/results, all acceptance states and
open limitations. Watch/fix CI; coordinator reviews/merges. If a stop rule/budget is hit, report concrete evidence
instead of claiming a completed feature.

## Checkpoint state

Owner approved the numerical design, bounded budget exception, always-fixed-boundary/no-hook scope and
hand-mutation-only coverage for this unsharded module. The serial implementation and acceptance tests are now
present. Round 1 passed without optimizer retuning; no round 2 was used. Four intentional hand mutations were
killed and restored. See the [commands and raw acceptance results](../../benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md).
The quality target is not universally met and the torus fallback remains; neither is hidden by changing defaults.
Full local CI and independent diff-only bounds review are required before implementation publication/readiness.

Implementation detail settled within the approved design: after power-of-two surface range preconditioning,
normalize the private mesh to unit total area. This removes residual scale dependence for non-power-of-two
similarities. Returned Energy uses InitialEpsilon so retained candidates remain comparable; Converged is false
if the returned best candidate differs from the converged iterate. The original map is returned without a
round trip when no better candidate was retained.
