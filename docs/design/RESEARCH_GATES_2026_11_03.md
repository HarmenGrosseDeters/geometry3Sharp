# Research items 1–3: independent effort gates

Owner briefs and clarification: 2026-11-03. **Each stop rule applies independently.**
Stopping brief 1 does not block briefs 2 or 3. The owner requested this research-results PR separately,
then implementation of brief 2; this is not a prerequisite code/design PR stacked under that feature.

This report records prior-art and library inspection, not an implementation or experimental acceptance pass.
Research inspected local revision `cd7fb8abba0af09d5725b60c700dfec95234e7ad`; the relevant source files were
unchanged at the report branch's master base `8364ac74499bb1fadca580c68ec38b0d99bb0803`.
No library code, mesh assets, dependency manifests or existing defaults are changed.

## Summary

All line counts and session counts below are **engineering estimates, not measurements**. Lines include tests
and benchmark additions unless shown separately. Documentation/notices and CI wall time are additional.

| Brief | Decision | Estimated library / test / benchmark lines | Estimated implementation effort |
|---|---|---|---|
| 1: foldover-free maps | Stop at effort gate for the complete brief | 400–700 / 350–550 / 80–150 | 2–3 short sessions |
| 2: affine heat log map | No infrastructure stop; bounded AffineLocal trial appears small | 120–200 / 220–300 / 60–100 | One short session if initial accuracy checks succeed |
| 3: rational-curve winding, full method | Stop at effort gate for full near/on-curve handling | 450–750 / 350–550 / 60–100 | 2–3 short sessions |
| 3: expressly permitted reduced method | Plausibly small, subject to explicit supported-domain/error contract | 200–300 / 180–240 / 40–60 | One short session |

Actual work so far: read-only research and this documentation. Implementation effort, convergence rounds,
accuracy, runtime, mutation sensitivity and API validation have **not** been measured. No before/after improvement
is claimed. Gates stayed within the approximately 30-minute research budget per brief.

## 1. Foldover-free maps

### Prior art

1. [Garanzha et al., *Foldover-free maps in 50 lines of code*, 2021](https://arxiv.org/html/2102.03069v1):
   sections 2.2–2.3 give determinant regularization, the shape/area energy and the energy-dependent epsilon
   schedule (equation 6). A nonlinear optimizer is still needed. Section 4.2's finite-untangling result has
   hypotheses; section 3.3 distinguishes inversion freedom from global injectivity. Paper only; no authors'
   repository implementation was read or ported.
2. [Rabinovich et al., *Scalable Locally Injective Mappings*, 2017](https://igl.ethz.ch/projects/slim/):
   flip-preventing optimization starts from an already locally injective map. Publication page only; no linked
   libigl implementation was opened.
3. [TinyAD documentation](https://github.com/patr-schm/TinyAD), with its
   [MIT license](https://github.com/patr-schm/TinyAD/blob/main/LICENSE) checked before reading:
   second-order AD, sparse element assembly and projected Newton are useful architectural references.
   C++17/Eigen is not a drop-in C# dependency. The linked TinyAD paper was not separately inspected.

**How others solve it:** regularized determinant energies plus nonlinear descent (Garanzha), barriers that
preserve an existing valid map (SLIM), or automated derivatives and sparse Newton assembly (TinyAD).

### Library findings and stop rationale

[ParamMesh, TriangleFrame and ConstrainedQuadratic](../../parameterization/MeshParameterization.cs) supply
indexing, boundary data, Jacobians, result diagnostics and constrained sparse linear solves. No additional sparse
linear solver is inherently necessary. No reusable BFGS/L-BFGS/Newton optimizer was found in the inspected
solver and parameterization directories. A paper-based analytic modified-Hessian implementation remains possible.

[SLIM](../../parameterization/SLIMParameterization.cs) explicitly rejects flipped/degenerate initial UVs;
its optimizer is specialized to flip-free symmetric Dirichlet energy. Tutte initialization changes the boundary
to a circle, so it cannot substitute for preserving arbitrary initial boundary coordinates.

The full brief also needs stable derivatives, an epsilon/line-search loop, fixed-boundary preservation,
best-map/failure handling, invariance/reuse tests and distortion/fallback comparisons. These make it larger than
a short demonstration of the paper's core formula. The estimate excludes the optional atlas hook.

Open issues:

- The [flipped-map fixture](../../tests/geometry3Sharp.Tests/ParameterizationFlippedMapTests.cs) includes an
  uncut annulus whose test comment describes a collapsed boundary ring. Classify actual boundary feasibility
  before promising recovery with every boundary vertex fixed; its coordinates were not measured in this gate.
- [UVAtlasGenerator](../../parameterization/UVAtlasGenerator.cs) rejects globally overlapping cone charts.
  Removing flips alone does not prove that the torus fallback disappears. Keep the existing overlap checks.
- The paper's shape/area objective is not identical to symmetric Dirichlet. The requested SLIM comparison must
  be measured, not inferred from convergence.
- If resumed, treat the new module as tier L and obtain design approval. The folded-disc two-round stop rule
  remains mandatory; **no optimizer rounds have been run**.

## 2. Affine heat log map

### Prior art

1. [Soliman and Sharp, *The Affine Heat Method*, SGP/CGF 2025](https://www.yousufsoliman.com/projects/the-affine-heat-method.html):
   the project page distinguishes a prefactorable local variant from a higher-detail cut-locus variant. The
   linked large PDF was not downloaded; this gate used the authors' description and licensed implementation.
2. [geometry-central documentation](https://geometry-central.net/surface/algorithms/vector_heat_method/):
   AffineLocal is fast and highly accurate near the source; AffineAdaptive targets higher quality. Intrinsic
   Delaunay triangulation is recommended. There is no claim of universal pointwise error domination.
3. [geometry-central implementation at 019669dd](https://github.com/nmwsharp/geometry-central/blob/019669ddabda05e0f71fa3587cfb3c1dadf19cb8/src/surface/vector_heat_method.cpp):
   inspected `ensureHaveAffineHeatSolver`, `computeLogMap_AffineLocal` and `computeLogMap_AffineAdaptive` after
   checking the [MIT license](https://github.com/nmwsharp/geometry-central/blob/019669ddabda05e0f71fa3587cfb3c1dadf19cb8/LICENSE).
   A future port must retain its notice and receive a SOURCES row.
4. [Sharp, Soliman and Crane, *The Vector Heat Method*, 2019](https://www.cs.cmu.edu/~kmcrane/Projects/VectorHeatMethod/):
   parallel transport and log-map construction provide the baseline; the local library's current log map is not
   simply the current upstream implementation.

**How others solve it:** augment tangent-frame transport with translations in homogeneous coordinates; distinguish
local and adaptive variants rather than treating near-source accuracy as a cut-locus guarantee.

### Key finding: reuse the existing solvers

Upstream builds a nonsymmetric affine matrix with `SquareSolver`, but this does **not** imply that a general sparse
LU solver must be added. Regrouping homogeneous and vector coordinates reveals a block-triangular system:

$$
T_{ij}=\begin{pmatrix}R_{ij}&-e_{ij}\\0&1\end{pmatrix},\qquad
\begin{pmatrix}A_v&B\\0&A_s\end{pmatrix}
\begin{pmatrix}u\\z\end{pmatrix}=
\begin{pmatrix}0\\\delta_s\end{pmatrix}.
$$

Solve $A_s z=\delta_s$, then $A_v u=-Bz$. The translation product $Bz$ can be accumulated over edges.
This is an algebraic deduction from the inspected implementation, **not a validated port or bitwise-equivalence claim**.
Test the coupling signs/frames against an independently assembled small system before relying on it.

[VectorHeatMethod](../../mesh_ops/VectorHeatMethod.cs) already has cached scalar/vector Cholesky factors, intrinsic
edge lengths/signposts, source-component masks, diffusion-time growth and original-vertex result mapping.
The infrastructure stop rule therefore does not trigger. A new general matrix or mesh data structure is unnecessary.
The existing clamping of negative heat weights must be documented as an adaptation from upstream.

### Bounded implementation and acceptance

- Keep `ComputeLogMap(int)` unchanged; add an overload/enum whose legacy value calls exactly that path.
- Start with AffineLocal and existing factors. Do not automatically expand into AffineAdaptive if the first
  experiments fail the requested cut-locus criterion; report the discrepancy and its estimated additional scope.
- Use [IntrinsicGeodesicsTests](../../tests/geometry3Sharp.Tests/IntrinsicGeodesicsTests.cs),
  [GeodesicsIssueCasesTests](../../tests/geometry3Sharp.Tests/GeodesicsIssueCasesTests.cs) and
  [IntrinsicBenchmark](../../benchmarks/IntrinsicBenchmark/Program.cs) building blocks.
- First compare plane, sphere and cylinder results with analytical oracles, including per-vertex regressions and
  max/mean error by radius band. Better mean error alone does not satisfy “no worse everywhere”. State the tangent
  gauge and smooth-surface versus triangulated-surface comparison. Exclude only genuinely undefined cut-locus points.
- Preserve documented NaNs in components without a source. Keep every solve on the same selected diffusion time;
  test underflow-time growth, repeated sources, A–B–A reuse, option/mesh invalidation and refined vertices.
- Require legacy bit identity, Similarity.All(), exact power-of-two scaling and mutation-sensitive translation tests.
  No new parallel path is needed. Tier M applies to this additive extension without a new numerical subsystem.

The owner has requested starting brief 2 after this report PR. This is approval to begin the bounded implementation,
not permission to weaken acceptance, alter legacy behavior, exceed the effort gate or bypass full local CI/review.

## 3. Generalized winding numbers for rational curves

### Prior art

1. [Spainhour, Gunderman and Weiss, 2024](https://arxiv.org/html/2403.17371v2): sections 4–5 use linear closure,
   integer winding and query-dependent subdivision; section 6 treats coincidence separately. Direct quadrature
   near a curve is not an adequate robust substitute.
2. [LLNL Axom winding helpers at 261057ce](https://github.com/LLNL/axom/blob/261057cec093b9c9ae0616e42cce14fde62bab30/src/axom/primal/operators/detail/winding_number_2d_impl.hpp),
   [NURBS extraction](https://github.com/LLNL/axom/blob/261057cec093b9c9ae0616e42cce14fde62bab30/src/axom/primal/geometry/NURBSCurve.hpp)
   and [Bezier representation](https://github.com/LLNL/axom/blob/261057cec093b9c9ae0616e42cce14fde62bab30/src/axom/primal/geometry/BezierCurve.hpp):
   the [root license](https://github.com/LLNL/axom/blob/261057cec093b9c9ae0616e42cce14fde62bab30/LICENSE) and each inspected
   file header identify BSD-3-Clause. A port needs original notices, SOURCES and THIRD_PARTY_NOTICES.
3. [Patrikalakis, Maekawa and Cho, section 1.4.3](https://web.mit.edu/hyperbook/Patrikalakis-Maekawa-Cho/node18.html):
   Boehm knot insertion preserves the curve; repeated insertion decomposes a spline into Bezier pieces.
   Mathematical reference only, not copied book code.

**How others solve it:** use a chord's contribution outside an enclosing region, subdivide where that certificate
fails, and handle endpoint/coincident queries separately; extract NURBS into rational Bezier pieces.

### Available data and missing work

[BezierCurve2](../../curve/BezierCurve2.cs), [NURBSCurve2](../../curve/NURBSCurve2.cs) and
[BSplineBasis](../../curve/BSplineBasis.cs) expose control points, weights and all knots. An internal homogeneous
extraction/subdivision helper can be additive. Existing sampling subdivision is not a control-hull certificate.
BezierCurve2 rejects degree 1, but [Segment2d](../../math/Segment2.cs) and degree-1 NURBS can cover linear primitives.

[IParametricCurve2d](../../curve/ICurve.cs) alone cannot certify bounds of an arbitrary implementation; support must
be explicit by curve type. Axom's rational Bezier representation requires positive weights, which the current NURBS
setters do not enforce. Validate supported inputs locally without changing existing curve APIs.

The complete method also needs convexity, winding-preserving subdivision, endpoint/tangent formulas, artificial
closure handling, valid termination and scale-relative tolerances. Do not copy Axom's absolute default tolerances
or treat a recursion cap as proof of containment.

### Open issues and permitted reduction

- The proposed fuzz bound $|w|\leq\text{number of curves}$ is not valid for arbitrary NURBS: a single curve can
  traverse a circle twice and has winding two at its center, by additivity of the winding integral. Restrict the
  generator to appropriate simple primitives or derive a degree/span-dependent bound; never clamp the answer.
- The ordinary integral is singular on the curve. A finite extension needs a stated coincidence convention;
  silently returning zero is not one. Continuity through a gap does not imply continuity across retained curves.
- A plausible reduced deliverable supports segments, polynomial Beziers and valid clamped positive-weight NURBS;
  it subdivides until a conservative enclosing-region test permits chord replacement.
- Unsupported types, coincidence, stagnation and work-limit exhaustion must be explicitly unresolved, via an
  owner-approved Try-style result or documented exception. `Contains` must not guess. General periodic/unclamped
  splines and robust on-curve behavior remain open items.
- Query-dependent winding-preserving replacement is consistent with the brief; fixed global polygonization is not.
- The reduced scope is explicitly allowed by the brief but is **not** the full near-curve acceptance pass. Preserve
  unresolved near-circle probes in results. The fuzz oracle and error-reporting contract need clarification before
  implementation. Treat the new module as tier L.

## Validation and handover

No algorithm implementation, accuracy benchmark, mutation run, build or pack was performed during these gates.
The documentation-only PR is tier S: review the diff; local CI is not required by CLAUDE.md. Full local CI remains
mandatory before pushing brief 2's library changes. Neither this report nor its PR should be merged by the worker.

The relevant-source comparison against current master produced no diff:

```powershell
git diff cd7fb8abba0af09d5725b60c700dfec95234e7ad 8364ac74499bb1fadca580c68ec38b0d99bb0803 --stat -- parameterization mesh_ops\VectorHeatMethod.cs mesh_ops\IntrinsicTriangulation.cs curve solvers tests\geometry3Sharp.Tests\IntrinsicGeodesicsTests.cs tests\geometry3Sharp.Tests\GeodesicsIssueCasesTests.cs benchmarks\IntrinsicBenchmark
```

Next: brief 2 on its own branch based on master, not on this research branch. Leave brief 1 stopped and brief 3
unstarted until its turn; record the eventual code, measurements and remaining limitations in each feature's own PR.
