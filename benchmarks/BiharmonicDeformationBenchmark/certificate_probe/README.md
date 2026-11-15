# Path-bound feasibility probe

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
