# MultiscaleCholeskyProbe

Research probe of the spike [MULTISCALE_SPARSE_CHOLESKY_2026-11.md](../../docs/research/MULTISCALE_SPARSE_CHOLESKY_2026-11.md):
can an approximate (inverse) Cholesky factor, used as the preconditioner of a fixed-order conjugate gradient iteration, beat
the direct `SparseCholesky` on the systems of the library? It is a prototype for a first measurement, not a library candidate: the
preconditioners are serial, scalar and untuned, and nothing here is used by the library.

## What it measures

Procedural systems shaped like those of the library, on a bumpy cube sphere (6(k-1)^2 + 2 vertices) and a 3D grid:

| system | shape | library user |
|---|---|---|
| poisson | cotan Laplacian, one pinned vertex | heat method (second solve), parameterizations |
| heat | `M / h^2 + L`, h the mean edge length | heat method (first solve) |
| bilaplacian | `L M^-1 L` on the free vertices, two fixed caps | `BiharmonicMeshDeformer` |
| connection bs=2 | connection Laplacian (4 x angle transport) in real form, mass shift 1e-8 | `CrossField` |
| grid3d | 7-point Laplacian with jittered weights, Dirichlet walls | none (control: the 3D case of the papers) |

Per system: the direct solver with the default minimum degree ordering and with the opt-in nested dissection (`Factorize`,
entries of the factor, best of three `Solve`), and a conjugate gradient iteration with these preconditioners:

- Jacobi and IC(0) (the baselines);
- a multiscale incomplete Cholesky after Chen, Schaefer, Huang, Desbrun 2021: coarse-to-fine sampling, elimination fine to
  coarse, pattern `dist(i, j) <= rho * min(l_i, l_j)` united with the pattern of A, zero fill-in on that pattern, a diagonal
  shift (reported) when a pivot fails. Setup is given as ordering + pattern + numeric;
- a factorized sparse approximate inverse on the same pattern (one independent dense solve per row: the closed form of
  Schaefer, Katzfuss, Owhadi 2021 applied to the entries of A);
- a seeded approximate elimination after Kyng and Sachdeva 2016, on the systems that are a graph Laplacian plus a
  diagonal. Positive off-diagonals (non-Delaunay edges) are left out of the preconditioner and counted; the arm is skipped
  when they hold more than 5 % of the off-diagonal weight.

Columns: setup time, stored entries (factor or preconditioner), iterations to a relative residual of 1e-6 / 1e-8 / 1e-12,
time of the iteration to 1e-8 / 1e-12, and the true relative residual at the end. `-` means not reached in 3000 iterations.
The iteration sums in a fixed serial order; the iteration counts below repeat exactly between runs.

Caveats of the prototype: the sampling is a level-wise stand-in for the maximin ordering (level k takes every point farther
than diameter / 2^k from the points taken so far), so `rho` here is somewhat smaller in effect than the paper's; there are no
supernodes and no parallel triangular solves; only seed 1 of the approximate elimination was run.

## Run

```
dotnet run -c Release --project benchmarks/MultiscaleCholeskyProbe -- [only=<regex>] [rho=2,3,4] [k=96,192] [k3=30,48] [direct=0|1] [fsai=0|1]
```

## Results (2026-11-22)

Cloud container, 4 cores (Intel Xeon 2.1 GHz), .NET 10.0, Release. The direct solver and the FSAI setup use the 4 cores;
everything else in the probe is serial. The probe warms up every code path first. The direct `Factorize` is the best of
two on fresh instances and the direct `Solve` the best of three; the preconditioner setups and the iterations are timed
once. It is a shared machine: the same row differs by up to about 30 % between runs (and one product with A by more), so
read the timings as ratios of 1.5 and above, not as digits.

The 1e-12 column counts the iterations until the recurrence residual of the iteration reaches 1e-12; the last column is
the true residual `|A x - b| / |b|` at that point, which is larger on the badly conditioned systems.

### `k=96 k3=30 rho=2,4`

#### sphere k=96 poisson, n = 54151, nnz(A) = 0.38M, one product with A 0.62 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.31 | 3.55M | 1 | 10.5 | 2.8E-014 |
| direct ND | 0.14 | 2.51M | 1 | 6.4 | 2.3E-014 |
| PCG Jacobi | 0 | 0.05M | 917 / 1050 / 1314 | 951 / 1145 | 1.0E-012 |
| PCG IC(0) | 0.01 | 0.22M | 241 / 286 / 358 | 529 / 658 | 9.0E-013 |
| PCG approximate elimination seed 1 (105 positive off-diagonals left out) | 0.15 | 0.37M | 29 / 38 / 56 | 87 / 126 | 9.4E-013 |
| PCG multiscale IC rho=2 | 0.06 + 0.06 + 0.01 | 0.30M | 261 / 312 / 386 | 793 / 970 | 9.0E-013 |
| PCG multiscale FSAI rho=2 (max row 73, 0.02G dense flops, 4 threads) | 0.06 + 0.06 + 0.01 | 0.30M | 378 / 454 / 550 | 1245 / 1494 | 9.3E-013 |
| PCG multiscale IC rho=4 | 0.06 + 0.12 + 0.06 | 0.95M | 116 / 139 / 171 | 661 / 832 | 8.8E-013 |
| PCG multiscale FSAI rho=4 (max row 236, 0.69G dense flops, 4 threads) | 0.06 + 0.12 + 0.14 | 0.95M | 248 / 297 / 360 | 1222 / 1450 | 9.2E-013 |

#### sphere k=96 heat, n = 54152, nnz(A) = 0.38M, one product with A 0.45 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.31 | 3.57M | 1 | 11.4 | 5.3E-016 |
| direct ND | 0.15 | 2.52M | 1 | 6.3 | 5.0E-016 |
| PCG Jacobi | 0 | 0.05M | 31 / 42 / 62 | 43 / 59 | 9.6E-013 |
| PCG IC(0) | 0.01 | 0.22M | 10 / 13 / 20 | 24 / 36 | 2.7E-013 |
| PCG approximate elimination seed 1 (105 positive off-diagonals left out) | 0.08 | 0.29M | 13 / 18 / 26 | 39 / 55 | 6.5E-013 |
| PCG multiscale IC rho=2 | 0.06 + 0.06 + 0.01 | 0.30M | 10 / 14 / 21 | 31 / 44 | 3.7E-013 |
| PCG multiscale FSAI rho=2 (max row 72, 0.02G dense flops, 4 threads) | 0.06 + 0.06 + 0.01 | 0.30M | 13 / 18 / 27 | 39 / 57 | 7.0E-013 |
| PCG multiscale IC rho=4 | 0.06 + 0.10 + 0.06 | 0.95M | 6 / 8 / 11 | 32 / 44 | 9.2E-013 |
| PCG multiscale FSAI rho=4 (max row 243, 0.69G dense flops, 4 threads) | 0.06 + 0.10 + 0.11 | 0.95M | 8 / 11 / 17 | 48 / 73 | 2.3E-013 |

#### sphere k=96 bilaplacian, n = 50824, nnz(A) = 0.96M, one product with A 0.95 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.74 | 9.61M | 1 | 25.5 | 3.3E-011 |
| direct ND | 0.34 | 6.76M | 1 | 17.9 | 2.8E-011 |
| PCG Jacobi | 0 | 0.05M | - / - / - | - / - | 2.3E-001 |
| PCG IC(0) shift 0.064 | 0.02 | 0.51M | - / - / - | - / - | 1.0E-003 |
| PCG approximate elimination | not applicable: positive off-diagonals hold 24 % of the off-diagonal weight | | | | |
| PCG multiscale IC rho=2 shift 0.26 | 0.06 + 0.07 + 0.08 | 0.54M | - / - / - | - / - | 1.6E-002 |
| PCG multiscale FSAI rho=2 (max row 76, 0.05G dense flops, 4 threads) | 0.06 + 0.07 + 0.03 | 0.54M | - / - / - | - / - | 2.7E-003 |
| PCG multiscale IC rho=4 shift 0.26 | 0.06 + 0.11 + 0.15 | 0.93M | - / - / - | - / - | 1.7E-002 |
| PCG multiscale FSAI rho=4 (max row 225, 0.66G dense flops, 4 threads) | 0.06 + 0.11 + 0.17 | 0.93M | - / - / - | - / - | 4.9E-004 |

#### sphere k=96 connection bs=2, n = 108304, nnz(A) = 1.41M, one product with A 1.45 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.79 | 14.21M | 1 | 48.6 | 1.9E-014 |
| direct ND | 0.42 | 10.01M | 1 | 29.9 | 1.7E-014 |
| PCG Jacobi | 0 | 0.11M | 617 / 844 / 1174 | 2541 / 3253 | 1.0E-012 |
| PCG IC(0) | 0.02 | 0.81M | 163 / 225 / 315 | 1073 / 1650 | 9.0E-013 |
| PCG multiscale IC rho=2 | 0.09 + 0.08 + 0.02 | 1.16M | 182 / 247 / 345 | 1458 / 2001 | 9.3E-013 |
| PCG multiscale FSAI rho=2 (max row 144, 0.24G dense flops, 4 threads) | 0.09 + 0.08 + 0.07 | 1.16M | 257 / 351 / 489 | 2250 / 3146 | 9.8E-013 |
| PCG multiscale IC rho=4 | 0.09 + 0.19 + 0.31 | 3.73M | 81 / 112 / 154 | 1926 / 2488 | 9.0E-013 |
| PCG multiscale FSAI rho=4 (max row 486, 10.96G dense flops, 4 threads) | 0.09 + 0.19 + 1.88 | 3.73M | 170 / 233 / 323 | 3007 / 4154 | 9.9E-013 |

#### grid3d k=30, n = 27000, nnz(A) = 0.18M, one product with A 0.21 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.54 | 6.06M | 1 | 19.1 | 1.9E-015 |
| direct ND | 0.23 | 4.99M | 1 | 13.7 | 1.9E-015 |
| PCG Jacobi | 0 | 0.03M | 93 / 122 / 177 | 46 / 65 | 8.6E-013 |
| PCG IC(0) | 0.00 | 0.11M | 30 / 40 / 58 | 35 / 49 | 6.9E-013 |
| PCG approximate elimination seed 1 | 0.08 | 0.22M | 18 / 24 / 35 | 26 / 38 | 8.1E-013 |
| PCG multiscale IC rho=2 | 0.02 + 0.04 + 0.01 | 0.29M | 25 / 33 / 48 | 45 / 64 | 8.2E-013 |
| PCG multiscale FSAI rho=2 (max row 92, 0.04G dense flops, 4 threads) | 0.02 + 0.04 + 0.01 | 0.29M | 35 / 46 / 69 | 66 / 118 | 6.8E-013 |
| PCG multiscale IC rho=4 | 0.02 + 0.20 + 0.32 | 1.74M | 13 / 17 / 26 | 100 / 139 | 3.0E-013 |
| PCG multiscale FSAI rho=4 (max row 518, 9.78G dense flops, 4 threads) | 0.02 + 0.20 + 1.34 | 1.74M | 19 / 25 / 37 | 114 / 169 | 6.1E-013 |

### `k=96 k3= rho=8 fsai=0 direct=0`

#### sphere k=96 poisson, n = 54151, nnz(A) = 0.38M, one product with A 0.47 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| PCG Jacobi | 0 | 0.05M | 917 / 1050 / 1314 | 856 / 1059 | 1.0E-012 |
| PCG IC(0) | 0.01 | 0.22M | 241 / 286 / 358 | 498 / 621 | 9.0E-013 |
| PCG approximate elimination seed 1 (105 positive off-diagonals left out) | 0.15 | 0.37M | 29 / 38 / 56 | 79 / 117 | 9.4E-013 |
| PCG multiscale IC rho=8 | 0.07 + 0.32 + 0.57 | 3.68M | 25 / 31 / 37 | 315 / 375 | 4.1E-013 |

#### sphere k=96 heat, n = 54152, nnz(A) = 0.38M, one product with A 0.69 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| PCG Jacobi | 0 | 0.05M | 31 / 42 / 62 | 47 / 70 | 9.6E-013 |
| PCG IC(0) | 0.01 | 0.22M | 10 / 13 / 20 | 30 / 45 | 2.7E-013 |
| PCG approximate elimination seed 1 (105 positive off-diagonals left out) | 0.11 | 0.29M | 13 / 18 / 26 | 46 / 64 | 6.5E-013 |
| PCG multiscale IC rho=8 | 0.07 + 0.29 + 0.61 | 3.68M | 3 / 4 / 5 | 60 / 74 | 4.4E-013 |

#### sphere k=96 bilaplacian, n = 50824, nnz(A) = 0.96M, one product with A 1.03 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| PCG Jacobi | 0 | 0.05M | - / - / - | - / - | 2.3E-001 |
| PCG IC(0) shift 0.064 | 0.02 | 0.51M | - / - / - | - / - | 1.0E-003 |
| PCG approximate elimination | not applicable: positive off-diagonals hold 24 % of the off-diagonal weight | | | | |
| PCG multiscale IC rho=8 shift 0.004 | 0.06 + 0.29 + 1.44 | 3.42M | 2265 / 2550 / 2614 | 27352 / 28128 | 2.1E-010 |

#### sphere k=96 connection bs=2, n = 108304, nnz(A) = 1.41M, one product with A 1.43 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| PCG Jacobi | 0 | 0.11M | 617 / 844 / 1174 | 1934 / 2619 | 1.0E-012 |
| PCG IC(0) | 0.02 | 0.81M | 163 / 225 / 315 | 1002 / 1441 | 9.0E-013 |
| PCG multiscale IC rho=8 | 0.07 + 0.40 + 2.56 | 14.68M | 17 / 23 / 33 | 1590 / 2276 | 6.6E-013 |

### `k=192 k3=48 rho=4,8 fsai=0`

#### sphere k=192 poisson, n = 218887, nnz(A) = 1.53M, one product with A 1.61 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 1.60 | 18.70M | 1 | 53.9 | 5.3E-014 |
| direct ND | 0.56 | 12.17M | 1 | 29.1 | 4.3E-014 |
| PCG Jacobi | 0 | 0.22M | 1838 / 2185 / 2631 | 6592 / 7889 | 1.1E-012 |
| PCG IC(0) | 0.03 | 0.88M | 498 / 582 / 718 | 4127 / 5093 | 9.9E-013 |
| PCG approximate elimination seed 1 (219 positive off-diagonals left out) | 0.80 | 1.49M | 33 / 43 / 63 | 458 / 655 | 7.7E-013 |
| PCG multiscale IC rho=4 | 0.34 + 0.47 + 0.23 | 3.84M | 234 / 281 / 346 | 4895 / 6063 | 8.2E-013 |
| PCG multiscale IC rho=8 | 0.34 + 1.32 + 2.58 | 15.04M | 52 / 59 / 71 | 4356 / 5318 | 7.0E-013 |

#### sphere k=192 heat, n = 218888, nnz(A) = 1.53M, one product with A 1.61 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 1.68 | 19.62M | 1 | 48.8 | 5.4E-016 |
| direct ND | 0.57 | 12.07M | 1 | 30.9 | 5.0E-016 |
| PCG Jacobi | 0 | 0.22M | 33 / 44 / 67 | 140 / 216 | 7.6E-013 |
| PCG IC(0) | 0.03 | 0.88M | 10 / 13 / 20 | 109 / 161 | 7.9E-013 |
| PCG approximate elimination seed 1 (219 positive off-diagonals left out) | 0.43 | 1.18M | 13 / 18 / 27 | 182 / 263 | 5.2E-013 |
| PCG multiscale IC rho=4 | 0.31 + 0.52 + 0.21 | 3.84M | 6 / 8 / 12 | 153 / 219 | 1.5E-013 |
| PCG multiscale IC rho=8 | 0.31 + 1.27 + 2.46 | 15.04M | 3 / 4 / 6 | 266 / 405 | 7.2E-015 |

#### sphere k=192 bilaplacian, n = 205456, nnz(A) = 3.90M, one product with A 4.16 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 4.63 | 54.33M | 1 | 211.5 | 7.3E-010 |
| direct ND | 1.72 | 34.29M | 1 | 70.7 | 5.9E-010 |
| PCG Jacobi | 0 | 0.21M | - / - / - | - / - | 7.3E-001 |
| PCG IC(0) shift 0.064 | 0.08 | 2.05M | - / - / - | - / - | 4.8E-001 |
| PCG approximate elimination | not applicable: positive off-diagonals hold 24 % of the off-diagonal weight | | | | |
| PCG multiscale IC rho=4 shift 0.26 | 0.29 + 0.52 + 0.71 | 3.80M | - / - / - | - / - | 5.5E-001 |
| PCG multiscale IC rho=8 shift 0.016 | 0.29 + 1.24 + 7.21 | 14.06M | - / - / - | - / - | 9.7E-002 |

#### sphere k=192 connection bs=2, n = 437776, nnz(A) = 5.69M, one product with A 10.08 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 5.91 | 78.20M | 1 | 328.2 | 5.2E-014 |
| direct ND | 2.26 | 48.02M | 1 | 104.8 | 4.4E-014 |
| PCG Jacobi | 0 | 0.44M | 1271 / 1698 / 2360 | 22267 / 31155 | 1.0E-012 |
| PCG IC(0) | 0.10 | 3.28M | 349 / 466 / 627 | 13545 / 18065 | 9.7E-013 |
| PCG multiscale IC rho=4 | 0.34 + 0.62 + 1.05 | 15.15M | 165 / 225 / 311 | 17590 / 24265 | 9.8E-013 |
| PCG multiscale IC rho=8 | 0.34 + 1.75 + 10.48 | 59.92M | 33 / 46 / 65 | 13716 / 19293 | 6.5E-013 |

#### grid3d k=48, n = 110592, nnz(A) = 0.76M, one product with A 0.86 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 9.34 | 59.40M | 1 | 395.5 | 3.1E-015 |
| direct ND | 2.63 | 39.05M | 1 | 193.3 | 2.7E-015 |
| PCG Jacobi | 0 | 0.11M | 143 / 184 / 269 | 271 / 396 | 9.9E-013 |
| PCG IC(0) | 0.01 | 0.44M | 46 / 58 / 87 | 207 / 310 | 9.3E-013 |
| PCG approximate elimination seed 1 | 0.52 | 0.95M | 18 / 25 / 37 | 178 / 237 | 6.8E-013 |
| PCG multiscale IC rho=4 | 0.11 + 0.83 + 1.68 | 7.67M | 15 / 20 / 29 | 811 / 1124 | 8.4E-013 |
| PCG multiscale IC rho=8 | 0.11 + 4.73 + 60.76 | 51.50M | 6 / 7 / 10 | 1470 / 2115 | 8.8E-013 |

### `k=409 k3= rho=4,8 fsai=0 only=poisson`

Measured with the earlier state of the probe (short warm-up, one `Factorize`; at this size the difference is below the
noise). A repetition with the current state and the heat and grid3d systems of this size did not finish in the session's
time limit.

#### sphere k=409 poisson, n = 998785, nnz(A) = 6.99M, one product with A 12.92 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 15.09 | 114.14M | 1 | 524.2 | 4.2E-012 |
| direct ND | 4.07 | 65.46M | 1 | 149.1 | 3.3E-012 |
| PCG Jacobi | 0 | 1.00M | - / - / - | - / - | 5.4E-005 |
| PCG IC(0) | 0.16 | 4.00M | 910 / 1164 / 1520 | 54479 / 71907 | 1.7E-011 |
| PCG approximate elimination seed 1 | 5.22 | 6.82M | 39 / 51 / 75 | 3154 / 4628 | 4.3E-012 |
| PCG multiscale IC rho=4 | 1.75 + 2.25 + 1.12 | 17.48M | 485 / 609 / 752 | 64473 / 79434 | 1.2E-011 |
| PCG multiscale IC rho=8 | 1.75 + 6.38 + 12.00 | 68.49M | 111 / 127 / 158 | 40341 / 49711 | 5.5E-012 |
