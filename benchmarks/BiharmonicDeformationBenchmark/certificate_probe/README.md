# Path-bound feasibility probe

## Complete Euclidean certificate on the moving fixture

The argument in [BIHARMONIC_CERTIFICATE_PROOF.md](../../../docs/design/BIHARMONIC_CERTIFICATE_PROOF.md) connects original-coordinate interval assembly, a verified spectral gap, final-output residuals and exact absolute/relative Euclidean comparisons. `--euclidean` selects the agreed component response contract; the existing default remains the historical per-coordinate absolute check.

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40 0.5 0 scalar-audit --motion=xyz --euclidean
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 3 700 0.5 0 scalar-audit --fixture=tiny200 --wrong-known-candidate --euclidean
```

The first run passes both squared comparisons for 400 vertices / 360 free unknowns, taking 6.480 s in this single run. The second rejects solely on relative response despite satisfying the absolute position budget. Repeating the second command with `tiny150` and `lost-handle` rejects those candidates too. Raw logs are `bih-proof20.txt` and `bih-proof-{tiny150,tiny200,lost-handle}.txt`. The general theorem has no 16-vertex limit; production currently still does. This is evidence for represented procedural meshes and their SciPy candidates, not production integration or a scalable time-budget guarantee.

Controls use `10 40 0.5 0.0001 scalar-audit --motion=xyz --euclidean --perturb-axis=x` (repeat for y/z), and `10 40 1.5 0 scalar-audit --motion=xyz --euclidean` for the excessive shift. All reject; logs are `bih-proof-bad-{x,y,z,shift}.txt`. Eight Python tests pass via `python3 -m unittest discover -s benchmarks/BiharmonicDeformationBenchmark/certificate_probe -p 'test_*.py'`. In-memory mutations forcing the absolute or relative check to pass are each caught. Independent bounds review confirmed the composition, subject to the explicit single-component, ascending-vertex-ID ordering and original-data enclosure premises. `assess()` is not a standalone mesh validator and must not be called with unproven bounds or combined disconnected components.

## Trace-bound feasibility indicator

`probe.py` in `eigen` mode now prints an approximate version of the simple trace-based Cholesky error term from Rump, *Verification of Positive Definiteness* (2006), section 3, https://www.tuhh.de/ti3/paper/rump/Ru06c.pdf. With `OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/probe.py 20 0 eigen`, the indicator is 1.77e-7 against a proposed shift of 1.14; the same command with `100` yields 0.00368 against 0.0448 (about 8.2%). These are single exploratory evaluations of the SciPy-assembled matrix on this machine. The calculation uses the unshifted trace as an approximation, omits outward rounding and the underflow term, and does not establish that the sparse library's factorization satisfies the paper's assumptions. The numerical shift is not a proven lower eigenvalue. It also excludes uncertainty in original geometry assembly, the world-coordinate residual and final rounding. No positive-definiteness or position certificate follows from these numbers. The 100x100 result suggests that the simple trace bound deserves a properly rounded, independently reviewed experiment before attempting production integration.

The decimal interval pilot now also reports the maximum row sum of entry radii around the actual binary64 midpoint matrix. `OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40` reported 2.5611e-12 (upper bound rounded here for display). For a symmetric interval enclosure this bounds the spectral norm of the difference from that midpoint, conditional on the interval assembly and arithmetic being correct. The run also produced positive interval LDL pivots and its existing z-axis position bound of 2.25e-12; the radius is an additional measurement, not a separate proof of the midpoint's eigenvalue or a complete binary64 verifier.

Implementation blocker: `SparseCholesky.factorBlock` precomputes `1.0 / pivot` and multiplies by that rounded reciprocal, whereas the analyzed scalar Cholesky recurrence in Rump's paper divides by the pivot. `updateBlock` also groups products into supernode dot products. The paper's stated error constants therefore cannot simply be assumed to cover this implementation. A bounds review must derive the additional errors and account for grouping, underflow, overflow, compiler/runtime behavior and complete symbolic structure, or use a separately specified factorization for the verifier. Until then a successful factorization is not a positive-definiteness proof.

### Separate scalar feasibility kernel

Extreme fixtures use `--fixture=lost-handle`, `tiny150` or `tiny200`, matching the three-vertex production regression geometries/targets. `--wrong-known-candidate` pins the free output to its original position, reproducing the lost-motion candidate; fixed targets remain exact. For example:

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 3 700 0.5 0 scalar-audit --fixture=tiny200 --wrong-known-candidate
```

Run the same command with the other fixture names. At 40 decimal digits all three reject, but their residual intervals are extremely wide (about 1e263), which is not evidence that the verifier resolves the actual errors. At 700 digits the recorded logs `bih-extreme-*-700.txt` give bounds of approximately 1 (lost-handle), 64.3 (tiny150) and 6.43e-99 (tiny200). The first two reject, while tiny200 meets the absolute normalized 1e-8 position budget despite completely losing a tiny representable motion. This is compatible with that absolute budget but does not satisfy the existing production regression requirement of NumericalRange for tiny200. Do not relax that test or remove the production guard on the strength of this certificate. Resolving whether absolute position tolerance or relative preservation of tiny response is required is a contract decision before integration; the latter needs a separately specified error metric. No production behavior changed.

The pilot now solves and encloses residuals for all three axes. Add `--motion=xyz` for right-boundary movements `(0.04,-0.03,0.1)` in world coordinates; the default retains z-only targets. Each coordinate is rounded back to binary64 world position before its interval residual is evaluated. `--perturb-axis=x`, `y` or `z` selects the axis of the existing normalized candidate perturbation. Unknown flags and invalid axis names are rejected. Historical z-only logs below describe the older fixture-specific path.

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40 0.5 0 scalar-audit --motion=xyz
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 10 40 0.5 0.0001 scalar-audit --motion=xyz --perturb-axis=x
```

Repeat the second command with `y` and `z` for the other controls. Raw logs are `bih-xyz20.txt`, `bih-xyz10.txt` and `bih-xyz10-bad-{x,y,z}.txt`. The 20x20 run has coordinate bounds approximately 3.86e-12, 7.10e-12 and 2.25e-12, with 7.678 s total in this single run. Each 10x10 axis perturbation exits 2, with a failing bound about 0.0816. Independent review found no concrete error in the all-axis composition; two parser findings were fixed and invalid flags were checked separately. These are pilot fixtures and SciPy candidates, not library-solver acceptance or a general-mesh/production performance result.

The `scalar-audit` backend of `interval_ldl_pilot.py` now passes the freshly assembled, permuted and shifted interval matrix directly to the audited scalar kernel, then runs the existing interval residual check of the rounded world-coordinate candidate. It avoids trusting an exported matrix file for this fixture. x/y remain exactly zero displacement by the fixture assertions; this is not a general all-axis API. Independent review found no concrete defect in this composition or change to the existing indexed/scan factorization order.

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40 0.5 0 scalar-audit
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 10 40 1.5 0 scalar-audit
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 10 40 0.5 0.0001 scalar-audit
```

Raw outputs are `bih-combined20.txt`, `bih-combined10-badshift.txt` and `bih-combined10-badoutput.txt`. The first run establishes the fixture's normalized coordinate bound 2.25e-12, below 1e-8, and takes 5.893 s total (5.603 s audited factor check). The bad-shift control exits 1 before checking positions; the bad-output control exits 2 after its 0.0816 error bound exceeds tolerance. These single runs are correctness/feasibility measurements, not a five-run production timing comparison. The audit's rational per-operation work dominates; no automatic large-mesh fallback is justified. The production library's candidate, general target axes, extreme counterexamples and large-grid budget remain separate acceptance gates.

The optional `--audit` flag checks every executed recurrence product, subtraction, division and square root against an exact rational nearest-even rounding cell. Cell neighbors, zero/subnormal classification and rational decoding use the raw binary64 bits; bounds conversion directions are asserted rationally. This validates the executed arithmetic rather than assuming an unobserved rounding mode for that run. Integer arithmetic, bit serialization and the audit implementation remain trusted. It does not validate interval export provenance or output positions. Tests include ties, wrong rounded outputs/square roots, signed underflow boundaries, signed zero and subnormal neighbors:

```
python3 -m unittest discover -s benchmarks/BiharmonicDeformationBenchmark/certificate_probe -p test_scalar_cholesky_probe.py
python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/scalar_cholesky_probe.py <export.json> --audit
```

Four test methods pass. Disabling `audit_round` in memory causes the tie and wrong-output tests to fail (targeted mutation). The audit is a correctness experiment, not the proposed fast default. Before the raw-bit follow-up, one 20x20 audited run took 3.372 seconds versus 0.150 seconds without auditing; the bad-shift control still rejected. These timings concern different diagnostic modes of the same pilot and do not establish a production performance budget.

`scalar_cholesky_probe.py` implements a separate scalar sparse Cholesky recurrence with direct division, sequential product subtraction and structural fill. It uses exact rational arithmetic to construct the trace term, midpoint radii and downward diagonal shift, converting bounds outward to binary64. This avoids assuming that the production supernodal reciprocal-multiplication kernel has the paper's error constants. It is capped at 2500 free unknowns. It rejects nonfinite/subnormal arithmetic and detected product/division underflow; the omission of the underflow term is a review item, not an established portable runtime guarantee. A Python/runtime square-root rounding assumption also remains. It trusts the imported interval entries and their completeness and does not read original mesh data or check positions.

Commands (repository root):

```
python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/scalar_cholesky_probe.py <export.json>
```

Exports come from `interval_ldl_pilot.py` as described below. On this machine, single exploratory runs of the existing exports gave:

| Export fixture | Scalar shifted-matrix check | Trace correction | Matrix radius upper | Total seconds |
| --- | --- | --- | --- | --- |
| 20x20, shift 0.5 estimate | passes | 1.77304e-7 | 2.73981e-12 | 0.145 |
| 30x30, shift 0.5 estimate | passes | 2.27617e-6 | 5.62835e-12 | 0.675 |
| 50x50, shift 0.5 estimate | passes | 5.35828e-5 | 2.25915e-11 | 3.538 |
| 20x20, shift 1.5 estimate | rejects, pivot 278 | 1.77271e-7 | 2.57795e-12 | 0.155 |

Displayed corrections/radii are rounded upward for presentation. These are preliminary imported-matrix results, not release acceptance or comparable performance claims against the production solver. Independent review found that an exactly subnormal product can round to the smallest normal number and evade a result-only underflow check. The pilot now checks exact rational products and quotients at that boundary; independent re-review confirmed both signs of the reported multiplication and division cases and acceptance of exact normal boundary values. FTZ/DAZ, excess precision and the runtime rounding/sqrt contract remain unverified, so the script is not a portable certificate. Full original-data residual verification, all counterexamples, 100x100 scaling and the cold end-to-end budget are still open.

This independent pilot solves a rotated procedural grid with SciPy and recomputes its free-vertex gradient using mpmath at 70 decimal digits, from the binary64 input positions and handle targets. It evaluates the inexpensive graph-path lower bound on the smallest eigenvalue described in the design note. It is a feasibility measurement, not interval arithmetic and not a certificate; SciPy's result is not the library solver's result.

On Linux x64 with Python, NumPy 2.3.5, SciPy 1.17.0 and mpmath 1.3.0, one run each, `OPENBLAS_NUM_THREADS=1`:

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/probe.py 20
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/probe.py 100
```

| Rotated grid | Path eigenvalue lower bound | High-precision gradient 2-norm (z) | Resulting error bound estimate |
| --- | ---: | ---: | ---: |
| 20×20 | 3.09e-6 | 2.89e-12 | 9.37e-7 |
| 100×100 | 4.52e-9 | 7.58e-10 | 0.168 |

The desired normalized position error is 1e-8. These estimates exceed it, so this simple bound is not promising for these particular independently solved samples. A stronger lower bound, a more accurate candidate, or a different certified numerical method may improve matters; no such solution is established by this pilot.

## Refinement and stronger-bound pilots

The optional second argument performs that many iterative refinements: the residual is recomputed in 70-digit arithmetic, the correction is solved with the same SciPy sparse factorization, and free normalized displacements are rounded back to binary64. The optional third argument `eigen` also estimates the smallest eigenvalue using shift-invert, computes a comparison-matrix inverse-norm estimate from the numerical factors, and factors `Q - mu I` at `mu` equal to half the numerical eigenvalue estimate. These are floating-point experiments; none certifies an eigenvalue, residual, or positive definiteness. The residual is measured before final world-coordinate rounding, which is optimistic.

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/probe.py 20 3 eigen
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/probe.py 100 3 eigen
```

| Rotated grid | Gradient norm after refinement | Numerical smallest eigenvalue | Path-bound estimate of position error | Gradient divided by numerical eigenvalue |
| --- | ---: | ---: | ---: | ---: |
| 20×20 | 6.39e-13 | 2.282 | 2.07e-7 | 2.80e-13 |
| 100×100 | 1.07e-10 | 0.08954 | 2.37e-2 | 1.19e-9 |

The final column is an indicator using a numerical eigenvalue estimate, not an error certificate. The comparison-matrix inverse-norm estimates are approximately 1.16e-15 and 5.32e-210, far weaker than the path estimates. Numerically factoring the shifted systems gave positive pivots and 10,044 / 852,087 lower-factor entries respectively. The raw output records timing, permutation checks, and factor mismatch. The promising numerical shifts do not prove that the interval sparse factorization will succeed or be affordable; that is the next experiment.

## Decimal interval LDL pilot

`interval_ldl_pilot.py` constructs `Q` from exact binary64 mesh coordinates with decimal intervals, then performs a right-looking sparse LDLᵀ elimination of `Q - mu I`. Basic operations use explicitly directed decimal rounding; square-root endpoints use the correctly rounded decimal square root widened by one representable decimal. SciPy's estimate only proposes a positive binary64 `mu` and a permutation: interval pivots decide the result. The exact binary64 shift is recorded in hexadecimal. An independent review checked the interval operations, assembly, permutation and Schur updates; this is a standalone pilot, not the library implementation; the combined fixture-specific position check is described below.

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 10 40
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 30 40
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 30 80
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40 1.5
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 30 80 1.5
```

The 10×10 and 20×20 examples produced positive pivot intervals at 40 digits with `mu` at half the numerical eigenvalue estimate. The 30×30 example had an uncertain pivot at 40 digits but positive pivot intervals at 80 digits; the 80-digit factorization took roughly 4 seconds on this machine. As negative controls, shifting by 1.5 times the numerical eigenvalue estimate was rejected for both 20×20 and 30×30. See `interval-ldl*.txt` for exact shifts and raw output. The prototype scans the current entry dictionary for every pivot; its timing is not representative of a scalable sparse implementation. Precision growth and the cost at 10k/100k vertices remain open.

## Combined interval position check

After positive pivots, the pilot also forms an untrusted SciPy candidate, rounds its free positions to binary64 world coordinates, and computes an outward-rounded residual from the original binary64 geometry and targets. The certified positive shift bounds the smallest eigenvalue from below; dividing the upper residual 2-norm by that exact shift bounds the normalized position error. The fixture modifies z only: x/y targets and free candidates remain exactly at their input positions, so their exact displacement solution is zero. The script asserts this fixture condition; arbitrary target axes need their own residuals. An independent review checked the interval residual and norm bound.

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 30 80
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 40 120
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40 0.5 0.0001
```

For the measured rotated 20×20, 30×30 and 40×40 fixtures, the normalized error upper bounds are about 2.25e-12, 2.39e-11 and 1.04e-10, respectively, below the requested 1e-8. The 40×40 run required 120 decimal digits and around 19 seconds for assembly and factorization in this Python pilot; it does not establish scaling to 10k/100k. The final command perturbs one candidate by 1e-4 in normalized z and is rejected by the error bound. These checks apply to the exact recorded binary64 fixtures and trial shifts, not to the existing library solver or other meshes. Raw outputs are alongside the script.

## Column-index and binary-port probes

The interval pilot accepts `scan` or `indexed` as its fifth argument. The latter stores active entries by column instead of scanning the entire dictionary at each pivot. For 20×20 and 40×40, the recorded A/B outputs contain identical SHA-256 digests of all pivot intervals and identical residual bounds. One 40×40 run took 16.62 s for the scan and 15.88 s for the indexed factorization; this is one measurement of this Python prototype, not a general speedup. The 50×50 indexed case at 160 decimal digits had positive pivot intervals and a normalized error bound of about 4.04e-10, with about 47 s in factorization. Raw logs are `interval-ldl{20,40}-{scan,indexed}.txt` and `interval-ldl50-indexed.txt`. An independent review checked the column indexing and update order.

Passing a sixth argument exports the permuted, shifted interval matrix as JSON before elimination. `benchmarks/BiharmonicCertificateArithmetic` tests a separate binary fixed-point LDLᵀ implementation against such files. Its README states the critical distinction between proving the imported matrix positive definite and proving that the export faithfully represents the original mesh.
