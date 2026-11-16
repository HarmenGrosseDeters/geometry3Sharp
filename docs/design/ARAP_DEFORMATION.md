# ARAP deformation (#886)

ARAP moves mesh vertices toward positional handles while minimizing the discrepancy between each rest edge and its independently rotated deformed edge. The local/global scheme is based on Sorkine and Alexa, *As-Rigid-As-Possible Surface Modeling* (2007), https://doi.org/10.2312/SGP/SGP07/109-116. The implementation is developed from the paper, without libigl source.

## Existing building blocks

`LaplacianMeshDeformer` supplies examples of noncompact vertex-ID mapping and sparse solves, but its soft constraints and normal equations do not implement ARAP's hard constrained local/global system. `SparseCholesky` factors a fixed symmetric positive definite matrix and solves multiple right-hand sides; the caller must establish a constraint in each connected component, as `IsValid` alone does not reliably identify a singular matrix. `FastQuaternionSVD` can provide proper rotation factors, but defaults to an approximate iteration. `MeshWeights.CotanCentroid` computes cotangents for smoothing, not reusable symmetric edge weights, and its degeneracy checks need not be suitable for a deformation solver.

A reproducible feasibility probe is in `benchmarks/ArapDeformationProbe`. On its regular and planar sample, the four-iteration SVD produced a proper rotation with max element error about 3.4e-16 or less. It returned a non-identity proper rotation for zero covariance, where every rotation is equally optimal. Rank-one covariance also has non-unique rotations; a large difference from one chosen reference there is not by itself an error. A fixed 2x2 anchored system solved as expected. These few samples do not establish correctness, convergence, scaling invariance or suitability at 100k vertices.

## Implementation contract

- Treat input vertex positions and topology as rest state. Handles map existing vertex IDs to finite targets; impose them as exact Dirichlet constraints. Do not mutate the input mesh. A successful result owns a copy preserving the original vertex IDs and topology. If the input has vertex normals, output normals are recomputed from the deformed faces; custom input normals are not preserved. Meshes without vertex normals stay without them.
- Validate finite coordinates, triangle geometry and handles; reject an unanchored connected component before factorization. Invalid input and numerical failure return an explicit status and no result. Reaching the iteration limit returns an explicit non-converged status and the finite last iterate with exact handles. Specify behavior for all vertices constrained, empty meshes and noncompact IDs.
- Precompute symmetric cotangent edge weights and the reduced Laplacian from rest positions. With fixed handles, factor the reduced matrix once; per iteration reuse it for the three coordinates. A negative sum of cotangents is explicitly clamped to zero: the paper's spokes energy with signed weights can otherwise prefer non-rigid solutions. This is a stated weighting variant, not the paper's unmodified cotangent rule. The CGAL surface-deformation manual describes the same mathematical remedy for ARAP (documentation only; no code read). A separate implementation could offer a different energy if this changes quality on a given mesh. Zero-weight edges are excluded from covariance scaling and the reduced system. Each connected component of the positive-weight graph must have a handle; otherwise the solve reports an unanchored component. Poorly conditioned systems must be reported if factorization fails.
- For each vertex form its weighted rest/deformed covariance, obtain a proper rotation, then solve the global step from the paper. Normalize the covariance before the SVD to avoid scale-dependent absolute thresholds. Zero covariance has no preferred rotation: choose a deterministic previous rotation (identity initially), not the arbitrary SVD result. Rank-deficient covariance can have multiple optimal rotations; the deterministic quaternion solver selects one, and zero covariance retains the previous rotation. Continuity across rank changes is not guaranteed.
- Return iteration count, convergence status and the maximum vertex step normalized to each component scale. This is a stopping heuristic, not a linear-system residual or proof of stationarity; use a deterministic serial path first.

## Acceptance and open gates

Procedural input only. Check identity, translation, rigid rotation with sufficiently constraining handles, local deformation, invalid input, connected components, boundary/obtuse and near-degenerate triangles, noncompact IDs, repeated computation and invariance across `Similarity.All()`. The two power-of-two scales are checked bitwise as required by #886. Check finite output, handle positions and local rigidity, not just success. Benchmark setup and per-iteration costs at 10k and 100k vertices. No parallel path until serial correctness is established.

The solver uses 20 quaternion iterations and independently tests the resulting rotation against a general SVD, including rank-deficient cases. Biharmonic deformation is a separate class (`BiharmonicMeshDeformer`, #897).

## Numerical range

`NumericalRange` is reported only where a value is actually lost, following the rule of #897: a non-finite value, a component flushed to zero by normalization, a cotangent dot product whose every nonzero contribution underflowed (the weight would silently become zero), and a handle offset that is not finite. A product that underflows next to larger terms, or a term absorbed by rounding, is not an error: the sum is as accurate as any floating-point dot product, and the covariance is normalized by its largest entry before the SVD. Meshes rotated off the coordinate axes (right angles with rounding-noise cotangents) therefore work at any size; `RotatedCylinder_RigidHandleMotion_IsReproduced` and `JitteredIrregularSurface_RigidHandleMotion_IsReproduced` cover them with more than 16 vertices, and `FlushedProductNextToLargeTerms_IsNotRejected` covers the relaxed check (the old code returned `NumericalRange` there).

The two shared helpers of `BiharmonicMeshDeformer` (`MaxAbs`, `Length`, `Area`, the dot product) are private statics of that class; `ArapMeshDeformer` keeps its own small copies instead of editing a merged class. Both use `SparseCholesky` and the same per-component normalization and handle validation.

## Convergence

The energy `E(p) = min_R sum_i sum_j w_ij |(p_i - p_j) - R_i (q_i - q_j)|^2` (both cells of every edge, nonnegative weights) is evaluated at every iterate. The local/global iteration is monotone in `E` (the local step minimizes over the proper rotations (`det = +1`) up to the accuracy of the 20-iteration quaternion SVD: `FastQuaternionSVD` returns two proper rotations, so a reflecting covariance gets the best proper rotation, checked against a general SVD in `LocalRotation_OptimalScoreAgreesWithIndependentSvd`); a probe of the 4-vertex case showed no increase over 5000 iterations.

The slow case is therefore not a defect of the rotation fit or of the tolerance. `Residual` is the maximum step of the plain local/global map `G` from the current iterate, normalized per component scale, i.e. the fixed-point residual `|G(p) - p|`, and the tolerance is relative and similarity invariant. The plain iteration converges linearly with a rate close to 1 where the energy is nearly flat (a flat sheet bent out of plane has almost isometric, zero-energy directions), and a step of 1e-8 then does not bound the position error by 1e-8 (error is about step / (1 - rate)). Raising the iteration limit does not change this.

The default solver now accelerates the fixed-point iteration with Anderson acceleration (Walker and Ni 2011) safeguarded by the energy as in Peng et al., *Anderson Acceleration for Geometry Optimization and Physics Simulation*, ACM TOG 37(4), 2018: a candidate that does not lower `E` is discarded, the history is cleared and the plain step is taken, so `E` never increases (`Energy_NeverIncreasesWithTheIterationBudget`). `Settings.AccelerationDepth = 0` selects the plain iteration of Sorkine and Alexa; the default is 5 (at most 32). Only free coordinates are mixed, affinely, so handles stay exact. A candidate is evaluated by one local step, which the next iteration needs anyway; a rejected candidate costs one extra local step. A candidate whose local step leaves the numerical range is rejected like one that raises the energy, and a rejected trial does not change the stored rotations. The search of the library found no other nonlinear solver to reuse; Anderson acceleration is standard for this iteration (prior art: Peng 2018, and the quasi-Newton / SLIM solvers of Liu et al. 2017 for the same energy family, not implemented).

Measured (`benchmarks/ArapDeformationBenchmark/README.md`): the out-of-plane square needs 6562 iterations plain and 18 accelerated (`OutOfPlaneSquare_ConvergesWithinTheDefaultBudget`); the benchmark grid 807 and 52. The converged energies agree to 1e-9 relative. `Success` still means that the fixed-point residual is below the tolerance, not a proof of stationarity or of a global minimum (ARAP is nonconvex); `MaxIterations` is reported honestly with the finite last iterate, whose energy is in `ResultInfo.Energy`. The energy is computed in the per-component normalized coordinates, so components of different size count equally; for one component it equals the world-space energy divided by twice the weighted sum of squared rest edge lengths (`Energy_MatchesTheIndependentEnergy`). An energy that overflows for finite input is `NumericalRange`.

## Regression check

Removing the nonnegative clamp (`Math.Max(0, pair.Value)` -> `pair.Value`) makes `NegativeCotanWeight_WithFreeEndpoint_BeatsIndependentCandidate` fail: its independent nonnegative energy rises from 0.09997460364694799 to 7.175916903429789. Command: `dotnet test tests/geometry3Sharp.Tests -c Release --filter FullyQualifiedName~NegativeCotanWeight_WithFreeEndpoint`. Targeted mutations of the acceleration (energy safeguard removed, candidate never used, rotation replaced by the identity, restored underflow check) each fail at least one test of `ArapMeshDeformerTests`; this is not a full mutation campaign.

## Iteration probe

A 4-vertex square with three fixed handles, one moved by 0.2 out of plane, needed 6562 plain iterations to reach the default tolerance (maximum step 6.14e-5 after 500 and 9.06e-8 after 5000), and 18 with the default acceleration; the same square with an in-plane 0.2 move needed 18 plain iterations. Command: `dotnet run --project benchmarks/ArapDeformationProbe -c Release`; raw output: `benchmarks/ArapDeformationProbe/results.txt`. The energy of the plain iteration decreased monotonically throughout (5.63e-4 to 5.16e-4 in the unnormalized probe), so the slowness is the linear rate of the local/global map, not an error.

## Scaling, warm start and large twists (measured 2026-11-16)

Measured with `benchmarks/ArapDeformationBenchmark` (`<nx> <ny> <iterations> [depth]`; flat grid, left column fixed, right column moved by (0.1, 0, 0.05 sin)), default tolerance 1e-8, depth 5, net10.0, one run each. The warm-start runs used a local, unpublished hook that started the iteration from the `BiharmonicMeshDeformer` result (#897) of the same handles instead of the harmonic interpolation; no such setting exists and none is proposed.

| Grid | Start | Iterations to 1e-8 | ARAP time | Biharmonic time |
|---|---|---|---|---|
| 100x100 (10k) | rest | 89 | 2.07 s | - |
| 100x100 (10k) | biharmonic | 87 | 1.92 s | 0.48 s |
| 400x250 (100k) | rest | 143 | 33.3 s | - |
| 400x250 (100k) | biharmonic | 116 | 26.8 s | 3.7 s |

- The default limit of 50 stops at a residual of 4.2e-6 on the 100k grid (1.9e-6 on the 10k grid); its energy is within 1.8e-5 (10k: 8e-7) relative of the converged one, so the stop is cosmetic for grids. A limit of 200 or 500 reaches the tolerance (143 iterations).
- The plain iteration (depth 0) on the 10k grid is still at a residual of 5.5e-6 after 500 iterations.
- The warm start saves 1.2x iterations at 100k and nothing at 10k; with the biharmonic solve it is 30.5 s against 33.3 s. It is not worth a setting. Iterations grow slowly with size (89 to 143 for 10x the vertices); the time per iteration grows about 16x, which is the sparse Cholesky. The grid numbers do not call for a multigrid or hierarchical solver.

Large rotations (closed bar, length 4, radius 0.35, 24 x 49 rings and two cap centres, 1178 vertices; the bottom 4 rings and cap centre fixed, the top 4 rings and cap centre rotated about the bar axis; probe not in the repository). Iterations to the tolerance, accelerated (depth 5) / plain / accelerated from the biharmonic start:

| Twist | depth 5 | plain | biharmonic start | Energy at the stop (depth 5 / plain) | Mid radius (depth 5 / plain) |
|---|---|---|---|---|---|
| 45 deg | 27 | 267 | 24 | 0.001238 / 0.001238 | 0.349 / 0.349 |
| 90 deg | 35 | 301 | 31 | 0.004893 / 0.004893 | 0.345 / 0.345 |
| 135 deg | 39 | 1321 | 36 | 0.01079 / 0.00960 | 0.338 / 0.116 |
| 180 deg | 3854 | 5000 not reached (residual 1.2e-5) | 1000 not reached | 0.00792 / 0.00793 | 0.150 / 0.129 |

At 180 deg the accelerated run is at a residual of 7.9e-4 after 50 iterations (energy 0.0154, mid radius 0.347), 3.4e-4 after 200 (0.00829, 0.282) and 2.4e-5 after 1000 (0.00794, 0.104).

Findings:
1. The necked state is real, not an artefact of the iteration: the plain and the accelerated iteration both slide to it and the energy falls (180 deg: 0.0154 at the start of the slide to 0.0079). It is a lower ARAP energy than the straight twisted bar.
2. At 135 deg the motion criterion stops on a plateau: depth 5 reports `Success` after 39 iterations at energy 0.01079 (mid radius 0.338), while the plain iteration continues to the necked state with energy 0.00960 (mid radius 0.116). `Success` is the fixed-point residual criterion, not a minimum, for large twists.
3. The default of 50 iterations is tight from about a 180 deg twist (the bar is still sliding); for grids it is cosmetic (above). The biharmonic start does not help here (a linear interpolation of the displacements shears the bar): 180 deg is still at the limit after 1000 iterations, and 36 against 39 iterations at 135 deg.

No default changes are proposed; they would be an owner decision.

## Prepare, Solve and initial positions

Two additive APIs (no change of the output of `Compute`, checked by identical mesh hashes against master, `benchmarks/ArapDeformationBenchmark/README.md`):

- `Prepare(handleIds)` returns a `Preparation` with everything that depends only on the mesh and on which vertices are handles: validation, the per-component normalization, the cotangent weights, the anchoring checks and the sparse Cholesky factorization of the free-vertex Laplacian. `Solve(preparation, handlePositions[, initialPositions])` moves the handles and runs the local/global iteration; `Compute(handles)` is `Prepare` plus `Solve` with the order of the failure statuses of the single pass (the handle positions are checked before the factorization), so `Solve` equals a fresh `Compute` bit for bit (`PrepareSolve_EqualsAFreshCompute_BitForBit_ForEveryHandlePosition`). A `Solve` reads the preparation only, so concurrent solves on one preparation are allowed (`PrepareSolve_ConcurrentSolvesOnOnePreparation_EqualTheSerialOnes`). A preparation is tied to the mesh object and its `ShapeTimestamp`; `Solve` after an edit, with other handle ids, or with invalid `Options` is `InvalidInput` (and gives no mesh). The saving is small, see the measurement below.
- `Compute(handles, initialPositions)` and `Solve(..., initialPositions)` start the iteration from the given positions (world coordinates, keyed by vertex id) instead of the harmonic interpolation of the handle displacements. Every free vertex needs a finite entry, ids that are not vertices are rejected, entries for handle vertices are ignored (so a previous result can be passed whole). The start is a start, not a constraint: the safeguarded iteration never raises the energy from it. A start whose normalized coordinates overflow is `NumericalRange`.

Measured (Linux x64, 2026-11-16, raw output in the benchmark README): the setup that `Prepare` saves is about 1 % of a solve (10k: 22 ms against 1.9 s; 100k: 0.5 s against 40 s), so a 30-step drag on 10k vertices takes 55.0 s with reuse against 57.2 s with fresh computes; starting every step from the previous result saves 12 % (10k) and 15 % (100k) of the iterations, because the convergence is linear. The reason to use the API is the interface, not speed: a handle drag that keeps one preparation and the previous result.

Incremental twist (`benchmarks/ArapIncrementalTwist`, the 1178-vertex bar, 10-degree steps, each from the previous result, budget 1000, depth 5):

| Twist | Incremental iterations (energy, mid radius) | Cold jump iterations (energy, mid radius) |
| ---: | --- | --- |
| 90 | 23 (0.004893, 0.345) | 35 (0.004893, 0.345) |
| 120 | 29 (0.008591, 0.341) | 35 (0.008591, 0.341) |
| 140 | 27 (0.011576, 0.337) | 48 (0.011576, 0.337) |
| 150 | 319 (0.011133, 0.061) | 60 (0.013213, 0.335) |
| 160 | 84 (0.012114, 0.027) | 286 (0.012114, 0.012) |
| 170 | 142 (0.013050, 0.038) | 1000, `MaxIterations` (0.007919, 0.085) |
| 180 | 1000, `MaxIterations` (0.013928, 0.078) | 1000, `MaxIterations` (0.007936, 0.104) |

Up to 140 degrees the incremental path stays on the round branch (radius 0.350 to 0.337) with 22 to 29 iterations per 10-degree step, against 22 to 48 for the cold jump to the same angle; both reach the same state to the last digit. At 150 degrees it does not stay on the branch: the bar necks (mid radius 0.061) at a lower energy than the round state the cold solve still holds there (0.0111 against 0.0132), the same transition as in the twist findings above, now one step earlier than the cold solve. The incremental path therefore gives no protection against it, and neither the incremental nor the cold path is a minimum at 170 and 180 degrees (both hit the budget). The sum of the incremental iterations of the 18 steps is 1885 against 2773 for the cold jumps of the same angles.

