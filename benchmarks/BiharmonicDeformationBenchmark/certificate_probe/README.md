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
