# Biharmonic implementation plan

Status: implementation authorized; bounded dense/sparse production accuracy mode and standalone verification pilots in progress. The opt-in mode supports a dense solve up to 16 vertices or sparse verification up to 400 vertices in one active component, plus the proven zero-handle-motion path. Default representation-loss guards remain. This document records the direction of #897. The existing pilots are evidence for particular inputs, not release acceptance for general meshes.

Update: the opt-in strict production mode now includes an independently reviewed sparse reference certificate for one active component with 17–400 vertices, 2000 triangles, 256 fractional bits and explicit integer/fill/update caps. The rotated 20x20 production candidate is now generated privately, certified from original geometry and final output, then returned; the independent .NET pilot confirms the same candidate. Default numerical guards remain. 24 focused tests pass. Larger workloads, complete mutation/CI readiness, cache reuse and the cold performance budget remain open; this is not a scalable completion claim.

The complete sufficient proof is now in [BIHARMONIC_CERTIFICATE_PROOF.md](BIHARMONIC_CERTIFICATE_PROOF.md). The combined audited pilot passes the agreed absolute and relative Euclidean checks on a moving 400-vertex fixture and rejects the historical wrong tiny200 candidate. This closes the proof-composition gap for the represented pilot; production integration, general mesh validation and the large-mesh cost budget remain open.

Owner authorized implementation on 2026-11-15, subject to stopping on unresolved implementation questions. The numerical-domain investigation below resolves the previously offered binary choice: retain normal rotated meshes as an acceptance target and investigate a bounded sensitivity filter; do not weaken the extreme-case gates. Its performance and solver applicability remain feasibility gates, not established results.

Prototype progress: a separate scalar verification recurrence avoids transferring constants to the production supernodal solver. An exact rational per-operation rounding audit and an in-memory connection to original-data interval assembly and the rounded-output residual now pass the rotated 20x20 z-motion fixture and reject bad-shift/bad-output controls. Independent review found no concrete defect in the composition. This run took 5.893 s; it does not meet a practical large-mesh default goal. See the certificate probe README and raw logs. The investigation below records the earlier rationale; the prototype does not resolve general inputs, production candidates or the cold performance budget.

Contract blocker from the extreme-fixture probe: at 700 decimal digits, a candidate that loses the tiny200 response entirely still satisfies the proposed absolute normalized position budget (bound 6.43e-99 versus 1e-8). The production regression explicitly requires NumericalRange. Preserve its rejection guard pending a decision about absolute position error versus relative preservation of tiny motion; an absolute certificate alone cannot justify removing it. Lost-handle and tiny150 wrong candidates reject. Raw commands/results are in the probe README.

## Accuracy contract and historical conceptual alternatives

Owner decision: on 2026-11-15, use absolute Euclidean position error per free vertex together with relative Euclidean free-response error per connected component. Do not use independent per-axis relative thresholds, which depend on the coordinate frame. This supersedes the per-axis option below. The owner authorized implementation; all claims still require their stated bounds and tests.

### Rotation-independent sufficient check

For a component, let each candidate free displacement be the exact difference between its two binary64 world coordinates. Let D be the largest Euclidean length of these displacements. Let E be a proven upper bound on the Euclidean position error of every free vertex, and S the component rest scale specified below. Then the maximum exact response is at least max(D-E,0). A sufficient strict acceptance test is E <= PositionTolerance*S and E*(1+ResponseTolerance) <= ResponseTolerance*D. Both are required. This is a component-wide norm condition, not a relative guarantee for every individual vertex. If D=0 and E>0, the second inequality fails. An exact-zero response can be handled separately only with a proof.

One safe bridge from the existing dense interval fallback is conservative: its `PositionErrorBound` B bounds the absolute coordinate error divided by that fallback component's binary64 normalization s. Hence E <= sqrt(3)*B*s. Avoid square roots and floating-point comparison rounding by squaring nonnegative quantities and comparing exact dyadics: 3*(B*s)^2 <= PositionTolerance^2*S^2 and 3*(B*s)^2*(1+ResponseTolerance)^2 <= ResponseTolerance^2*D^2. Compute D^2 from exact dyadic differences of the final rounded world positions and original binary64 positions; take the maximum over free vertices in the component. Define S² as the maximum squared Euclidean distance from the component's lowest vertex ID, computed exactly from original binary64 coordinates; do not silently substitute the fallback's rounded s for S. Use exactly the same binary64 s and root as the fallback for B*s. The fallback returns a global maximum B, so applying it to each component is conservative. The norm definition is rotation-independent, but interval widths and acceptance may vary after floating-point rotation. Validate finite positive tolerances and all finite factors before exact conversion; bound calculation and resource exhaustion remain fail-closed. Fully fixed components have zero error and need no response solve. This bridge can be overly pessimistic and does not remove the 16-vertex limit.

This is a proposed bound for independent review, not a production claim. Exact-zero displacement with B>0 cannot pass the relative inequality; it needs an independently justified algebraic zero path or an explicit accuracy failure. The existing eight-ULP fallback acceptance remains separate: the stricter test uses the actual reported bound B and does not add any hidden ULP allowance to the new tolerances. Ordinary unverified candidates must not use this check without a proven B. Release requires the large-mesh verifier and ordinary rotated-grid gates, not just this small-mesh bridge.

The owner requested conceptual work before further implementation. Treat the following as a proposed contract and architecture, not as completed production acceptance or an authorization to weaken tests.

### Absolute accuracy and preservation of response

Two distinct requirements caused the tiny200 conflict: proximity to the rest-scale minimizer and preservation of a response far smaller than that scale. An absolute position certificate only answers the first. Keep the existing strict regression while specifying the second explicitly.

A candidate additional rule is relative accuracy of the free-vertex displacement per connected component and coordinate axis. Let d_hat be the maximum absolute candidate free displacement on that axis, evaluated from final output minus original position without losing subtraction information. Let E enclose the maximum coordinate error relative to the exact minimizer. Then max(d_hat - E, 0) is a lower bound on the exact maximum response. Requiring E <= ResponseTolerance * max(d_hat - E, 0), in addition to E <= PositionTolerance * restScale, is a sufficient relative-response test. All comparisons and displacement/scale calculations need directed bounds; use a lower bound for d_hat on the right-hand side. Never substitute the largest handle movement for this response scale: that is precisely the huge quantity hiding tiny200's lost response.

The all-zero candidate of tiny200 cannot pass this relative test with nonzero E; a sufficiently accurate nonzero candidate can. This does not promise relative accuracy for every individual vertex: a large response elsewhere on the same component/axis can hide a small local one. If local response preservation is intended, require per-vertex bounds and scales instead, at greater cost and with more zero-response failures. This scope must be named in the public contract rather than inferred from a few examples.

Implementation precision: express d_hat and E in identical units. Round d_hat downward and E upward, the lower response max(d_hat_low - E_up, 0) downward, and the tolerance product downward, or compare exactly. Nonfinite bounds are inconclusive. An upper error bound is never a positive lower bound on the actual error.

Zero response needs a separate policy. Exact zero handles on an axis imply exact zero displacement everywhere on that connected component, by uniqueness of the anchored SPD system. That axis can be copied and justified algebraically. Nonzero handles can also produce exact zero free response through cancellation; the relative test may remain inconclusive there. Return AccuracyNotEstablished, or prove the zero separately. Do not introduce a hidden epsilon floor. Representability can likewise make a requested relative accuracy unattainable; report that fact instead of relaxing it. Expose the absolute and relative assessments separately, and only call a strict request accepted when both requested conditions pass.

No numerical tolerance can be selected to enforce this by geometry alone. The ordinary rotated fixtures must be checked against the chosen response rule before choosing defaults. Keep RelativeTolerance as numerical BERR tolerance; introduce separate accuracy tolerances only with an implemented mode and tests. Current 1e-8 fixture accuracy is not a measured default for the new relative rule.

### Practical verifier and amortized work

Use the production supernodal solver only to propose a candidate. A separate scalar verifier avoids changing that solver or assuming it matches a different rounding analysis. Its symbolic structure and a proven geometric eigenvalue lower bound depend on rest geometry and handle IDs, so they are candidates for preparation-time reuse. Targets and final-coordinate residuals must be checked on every solve. Geometry, topology, handle-set or matrix-option changes invalidate preparation; use an immutable prepared snapshot unless mesh versioning provides a complete, audited contract.

The rational per-operation audit is a reference tool. Repeating it for every production call is not a credible fast default given the measured 20x20 cost. A deployable binary64 verification kernel needs a restricted, explicitly supported IEEE execution contract or checked arithmetic with rigorous local outward error tracking; either needs bounds review. Startup probes can catch unsupported configurations but cannot prove all operations correct. For unsupported runtimes or exhausted budgets, return NotEstablished; do not claim a portable proof from a successful probe. A small multiprecision reference remains useful for negative controls and differential checks.

The proposed strict response mode requires its internal acceptance filter for every candidate, with only the geometry-dependent work reusable. It must not be confused with an optional public CertifiedPositions result. If ordinary numerical mode omits that filter, it must retain the existing representation-loss guards until a separately reviewed replacement covers those cases; merely offering optional certification is insufficient.

Account for the extra scalar factorization as preparation cost, not as free validation. Report cold preparation plus solve and warm target-only solve separately. If the full cold path exceeds the 15% comparison budget, choose explicitly between a higher cold budget, an opt-in prepared/certified mode, or a different verification algorithm. Caching does not solve the cold regression. The present measurements do not settle that choice and do not justify SIMD work.

### Bounded next experiments after contract review

1. Apply the proposed response rule to tiny150/tiny200/lost-handle, exact-zero axes, a cancellation-to-zero fixture and ordinary rotated fixtures. Check both accurately computed and historical wrong candidates with independent references. Determine whether component/axis or per-vertex relative accuracy is actually required.
2. Prototype only the prepared geometric bound and a per-target original-data residual, measuring complete cold/warm cost. Preserve resource limits for symbolic fill, operations, precision and memory; an assembly contribution limit alone does not bound factor fill.
3. Integrate into the public deformer only after accuracy semantics, supported runtime arithmetic and the performance budget have explicit acceptance criteria. Keep the current guards until their replacement passes all counterexamples. A prototype success is not grounds to remove them.

## Numerical-domain investigation (2026-11-15)

The previous question conflated a release target with a numerical acceptance rule. Normal rotated workloads remain release targets. An individual candidate may be rejected when verification is inconclusive; persistent rejection of those workloads prevents release rather than redefining them as unsupported.

Local loss is not itself an error criterion: a tiny discarded contribution may be harmless, while multiplying it by a huge prescribed displacement can make its effect material. Compensated sums improve assembly but cannot establish how much a remaining error affects the minimizer. A small backward error for the rounded system also misses an error already introduced while constructing that system. No impossibility claim is made about cheap checks in general; none has yet been validated for this implementation.

Use a bounded, a posteriori sensitivity filter as the next feasibility experiment. In consistent normalized component units, let A and b denote the exact original-input free system, and x the displacement represented by the final rounded world positions. Enclose r = b - A*x using original coordinates and targets, including normalization, handle subtraction and assembly errors. With a proven positive lower bound mu on lambda_min(A), an upper residual norm divided by mu bounds the displacement error in the 2-norm and therefore each coordinate. Check all three axes separately against the position budget. Apply the bound per connected component, and account for final world rounding through x itself. This is a sufficient condition, not a necessary one; failure to prove it is inconclusive.

This filter is mathematically a certificate even if used internally to admit a numerical candidate. It must not be sold as a free diagnostic or implicitly satisfy a separately requested public certificate with different tolerance or rounding allowances. Keep the numerical BERR threshold separate from this position budget; do not silently reinterpret RelativeTolerance for both purposes. Until the position-budget API is settled in implementation review, the experiment uses the explicitly stated normalized 1e-8 fixture budget only.

Candidate verifier: Rump, *Verification of Positive Definiteness*, BIT 46 (2006), 433–452, https://www.tuhh.de/ti3/paper/rump/Ru06c.pdf. It gives sufficient positive-definiteness criteria using ordinary floating-point Cholesky with rigorous rounding/underflow allowances, and discusses interval matrix input. This suggests testing a verified shifted-matrix approach before porting the expensive multiprecision factorization. The paper is a mathematical source, not a code port.

If a symmetric interval enclosure of A is available, its midpoint B and a rigorously computed radius-norm upper bound rho provide one possible bridge: a verified lower eigenvalue bound nu for B yields mu = nu - rho for A, only when positive. All shift subtraction, radius norms and bound computations require outward error accounting. An estimated eigenvalue may propose a shift; successful ordinary Cholesky or SparseCholesky.IsValid alone proves nothing. This outline is not an implementation specification or a reviewed new bound.

Existing SparseCholesky uses a supernodal factorization and a relative pivot refusal threshold. Establish that the paper's arithmetic assumptions cover its actual kernels before reusing them for verification. Avoid dense inverses and dense factor products. Interval original-data assembly/residual work, symbolic fill and any extra factorization count toward total cost. Cache geometry-only verification only after the same rest-state invalidation audit as factor reuse.

Feasibility sequence: (1) specify and independently review the directed bounds and solver assumptions; (2) test the shifted binary64 verification on the existing 20x20 and 100x100 rotated fixtures and every extreme counterexample, with independent small references and bad-shift/bad-output controls; (3) measure complete cost against the plan's cold budget. Existing graph-path and absolute inverse-factor probes were too pessimistic on these fixtures, so repeating them is not the next step. Do not extrapolate the multiprecision 50x50 results to larger inputs.

No performance result for this new verifier exists yet. If it is inconclusive or exceeds the budget, stop with that measured blocker. The choices then concern scope/budget or a different verified method; accepting known wrong outputs is not an option. SIMD does not resolve this decision.

## Goal and scope

Deliver a practical DMesh3 biharmonic displacement solve with hard positional handles, useful numerical diagnostics and explicit numerical limits. Preserve Q = L^T M^-1 L, barycentric masses, input ownership and topology. Signed cotangent sums are valid for this squared energy; do not copy ARAP's clamp into it.

Main scope: BiharmonicMeshDeformer, tests, benchmark, design and sources. Reuse SparseCholesky. Change its internals only for a measured need and test affected consumers. Preserve public compatibility with 1.0.324. ARAP remains in #896: accelerating its local/global iterations requires a separate energy-preserving study.

The numerical deformer is the first deliverable. General exact arithmetic, a new sparse solver, SIMD/AVX2, bounded skinning weights and unconditional 100k certification are outside this first delivery. Certification is an explicitly requested, separately gated extension rather than an automatic expensive retry.

Risk tier: M for numerical changes, L for new arithmetic or proven bounds. Owner review of this contract precedes implementation; independent review and bounds review apply before readiness.

## Accuracy contract and proposed API

Separate three questions: whether the output is finite and meets the hard handles; whether it solves the floating-point system satisfactorily; whether its distance from the exact original-input minimizer has been proven small.

Numerical success answers the first two. It does not imply a forward-error bound, local shape preservation, injectivity or rigid rotation from a subset of point handles. A small residual and a condition estimate cannot silently replace a proof.

Proposed additive API decisions, finalized in the first implementation commit:

- Numerical verification is the default. Expose an optional CertifiedPositions mode only once its own gates pass; do not advertise a permanently unavailable feature.
- Retain RelativeTolerance's default 1e-8, explicitly defining it as the numerical backward-error threshold. Introduce a separate position tolerance only with certification.
- Add a separate accuracy assessment: NotCertified, Certified or NotEstablished. Converged describes numerical solve success; a strict certificate request has a distinct accepted/rejected result.
- Keep draft fields compatible or explicitly deprecate them. In particular PositionErrorBound = 0 on an unverified solve must not look like a zero-error proof; prefer an unavailable value plus an accuracy status.
- Ordinary success reports finite backward error and refinement count. A condition estimate is named Estimate, never Bound. Certification reports its own position bound and resources.
- Distinguish invalid/degenerate input, unanchored component, numerical range, failed solve, resource limit and requested accuracy not established. If a finite numerical candidate is retained after failed certification, expose it explicitly; it must never masquerade as certified success.

Acceptance tolerances on the tested workload do not promise the same forward error for arbitrary ill-conditioned inputs.

## Numerical implementation

### Input representation

Validate finite coordinates/targets, IDs and original triangles; require an anchor in every component. Copy fully fixed components directly after validation. Normalize geometry per active component and handle motion separately. Preserve deterministic iteration order and noncompact IDs.

Retain original coordinates and targets. Completely lost nonzero target differences, overflow and lost components during scaling cannot be accepted solely because a rounded residual is zero. Preserve the existing tiny-motion-on-large-mesh success case.

### Assembly and admissible numerical range

Replace the current immediate failure on every rounded-away summand with deterministic compensated accumulation and explicit diagnostics in cotangent dot products, masses, Laplacian rows, Q and right-hand sides. Check compensation preconditions and product overflow/underflow: compensation cannot recover an already underflowed product. Preserve structural entries independently of rounded zeros.

This alone does not solve the amplified tiny-coupling counterexamples. Before removing rejection, derive an inexpensive, documented numerical eligibility check from the original-data representation and error budget. It must admit ordinary rotations while rejecting or escalating the known extreme cases. No arbitrary angle, coordinate-ratio or small-weight threshold is permitted just to make tests pass.

Go/no-go: if an inexpensive justified check cannot satisfy both requirements, that region remains NumericalRange or requires certification. Changing the meaning of Success alone is not a fix for a known wrong result. The supported numerical domain and the rationale for its limits must be reviewable before this implementation step proceeds.

### Solve and diagnostics

Factor Q_ff once per Compute and solve its three right-hand sides. Evaluate per-axis componentwise backward error:

    berr = max_i |b_i - (A*x)_i| / (|b_i| + sum_j |A_ij|*|x_j|)

Use zero for a zero numerator and denominator; reject a nonzero numerator with zero denominator. Accumulate without overflow and without an absolute epsilon floor. Evaluate against retained assembled rows, not factor entries. Supplement it with a compensated original-data stationarity/range diagnostic and state its rounding limitations. If using a factorized matrix-free operator, document its absolute-contribution denominator instead of incorrectly labeling it componentwise BERR.

Allow at most three correction solves with the same factors. Improve residual accumulation, retain the best candidate, and stop on tolerance, no representable change or no decrease. Reject nonfinite/worsening corrections. Refinement does not certify original-data accuracy.

Impose exact handles and re-evaluate relevant diagnostics on the final rounded world-coordinate output, not just a more accurate intermediate. Check finite output, unchanged topology and the existing normals contract.

## Optional certified positions

Do not automatically promote the pilot to every Compute. Define an explicit request with position tolerance and precision/operation/memory limits. Failed certification yields NotEstablished; never downgrade silently to certified success.

A production verifier reads DMesh3 coordinates, triangles and targets directly. It independently assembles interval L/M/Q, validates structural completeness, proves Q_ff - mu I positive definite through finite positive interval pivots and bounds all-axis residuals of the rounded output. Estimates may propose shifts/orderings but cannot validate them. Any absolute or ULP allowance is explicit; proposed default is zero extra ULP allowance, so representability may prevent certification.

Define the position tolerance as a per-coordinate world-space bound (point infinity norm): each absolute coordinate error must be at most PositionTolerance times its rest-component scale. The scale is the maximum absolute coordinate difference from the component's lowest existing vertex ID, evaluated safely from original rest positions. This does not claim the same tolerance in 3D Euclidean norm, where three such bounds imply at most sqrt(3) times that value. Fully fixed components need no solve certificate. Zero-scale free triangle components are degenerate; nonrepresentable/subnormal normalization must be handled by safe scaling or return NumericalRange, never by an arbitrary unit-scale substitution. No hidden float conversion or ULP allowance may enlarge the stated bound.

Reuse an immutable or versioned internal SparseCholesky permutation/pattern snapshot only after completeness checks. Its double factors and IsValid are not a certificate. New multiprecision arithmetic needs a reviewed specification, meaningful negative controls and bounded work. The current benchmark implementation remains a prototype.

Profile assembly, fill, factorization, precision retries, residuals, allocation and GC before porting. If certification is impractical, retain it as research/tooling and ship only a numerical mode that passes its own gates. Successful small pilots do not establish large-mesh feasibility.

## Verification matrix

- Independent square answer 3/4; identity, translation, all-fixed rigid targets, finite output, topology and input ownership.
- Similarity.All(), bit-identical power-of-two scales, noncompact IDs, disconnected components, repeated Compute and changed handles. Exercise local motion on all three axes.
- Ordinary rotated grids at 4x4, 20x20 and 100x100, split between tests and benchmarks. No expected NumericalRange within the admitted normal-input domain.
- Keep every discovered counterexample: tiny cotangent products, lost diagonal additions, huge targets erasing original coordinates, tiny motions on huge geometry, fixed extreme components and overflowed residual denominators. Require either independent-reference accuracy or an explicit range/accuracy failure; do not weaken the assertions.
- Small independent high-precision references check actual coordinates as well as BERR. Include ill-conditioned examples demonstrating that small BERR is insufficient.
- Mutations: remove inverse mass, flip RHS sign, erase a tiny coupling, omit an axis/handle check, accept an excessive shift. Each relevant test must catch the defect.
- Certification additionally tests insufficient precision, omitted structural entries, permutations, uncertain pivots, resource limits and final world rounding. Hashes identify artifacts, not mesh completeness.
- Unit tests target under five seconds each; long reference campaigns stay in benchmarks or scheduled jobs. Follow CLAUDE.md target builds, API validation, local checks and mutation/review gates.

## Performance, reuse and SIMD

Measure validation, normalization, L/M assembly, Q assembly, ordering, factorization, three solves, refinement, final validation/normals and copying separately. Record total time, allocation and peak RSS on the same machine/runtime. Include 10k/100k axis-aligned and rotated grids, an irregular/obtuse mesh and a high-degree fan. A rejected solve is not a fast benchmark result.

For the normal path, record five sequential runs after warm-up with median and range. Proposed regression budget: at most 15% median cold-Compute regression against a comparable implementation on the same fixture with equivalent accuracy, unless an independently reviewed correctness requirement explains it. The earlier single 4.19 s run is not a rotated baseline or a universal budget. Previously rejected inputs require independent-reference quality checks and absolute timings.

Here cold means a warmed-up process/JIT but a new deformer and a full uncached rest assembly/factorization for each timed Compute; procedural input generation is outside the timer. Warm reuse means the same prepared rest state and handle IDs with new targets, recording the first preparation cost separately. Keep these measurements separate and validate equivalent final outputs before comparing timings.

After correctness, the first optimization candidate is factor/rest-assembly reuse for unchanged geometry and handle IDs. Targets change the RHS. Cache by mesh identity, geometry version, canonical handle IDs and matrix-affecting options; audit ShapeTimestamp coverage, mutable fields, edits and version wrap. If timestamps are not a complete contract, use an explicitly prepared immutable rest snapshot. Check cold/warm bit identity and invalidation. Concurrent mutation is unsupported unless designed separately.

SIMD/AVX2 is conditional on profiling: proceed only when a vectorizable kernel accounts for at least 20% of measured end-to-end time and the theoretical improvement exceeds observed noise. Require scalar fallback, ISA dispatch, deterministic results, no accidental FMA/reduction-order changes and no tolerance widening. Sparse indexing and BigInteger intervals are not assumed to benefit. Kernel speed alone is not an end-to-end result. Matrix-free solvers or ARAP rotation batching need separate briefs.

## Delivery and stop rules

1. Review accuracy/status semantics and derive the cheap numerical-domain check. Make the normal/extreme-case decision concrete before changing behavior.
2. Correct assembly, residual/refinement and ordinary rotated-input behavior. Run regressions, mutations and independent review. If admissibility cannot be justified, record the mechanism still missing rather than claiming completion.
3. Profile cold/warm workloads. Add caching only when justified; no SIMD before its gate passes.
4. Evaluate optional certification separately. Allow at most two precision/budget experiments without measured gain or a new diagnosis. Improve ordering/error analysis before blindly increasing input size.
5. Reconcile #897 with master and #896 documentation, re-run combined checks and describe final behavior in the PR. Stay Draft until correctness, resource behavior, CI and independent review pass. The owner handles merging.

Keep reviewable commits in the feature PR. Research artifacts may remain under benchmarks but must not obscure the public contract. Ordinary supported rotations rejected or known silently wrong successes remain release blockers.

## Building blocks and prior art

Reviewed library building blocks: LaplacianMeshDeformer/LaplacianMeshSmoother, SparseCholesky, DMesh3.ShapeTimestamp, ExactInterval/ExactDyadic and both benchmark pilots. Specialized CoreMath TwoSum helpers have preconditions and are not automatically general mesh accumulators.

- Jacobson et al., Bounded Biharmonic Weights for Real-Time Deformation (2011), https://doi.org/10.1145/1964921.1964973 — energy discretization; this feature uses unbounded displacements.
- Sorkine and Alexa, As-Rigid-As-Possible Surface Modeling (2007), https://doi.org/10.2312/SGP/SGP07/109-116 — separate ARAP energy/iteration contract.
- LAPACK DPOCON documentation, https://netlib.org/lapack/explore-html/d3/dfd/group__pocon_gaadb1e19663b71521d30b5b5bbe3091f8.html — reciprocal condition estimates are explicitly estimates.
- Intel oneMKL PORFS documentation, https://www.intel.com/content/www/us/en/docs/onemkl/developer-reference-c/2026-2/porfs.html — distinguishes backward error, estimated forward error and refinement.

These are planning references, not proposed dependencies or source-code ports. Clean-room restrictions remain unchanged.
