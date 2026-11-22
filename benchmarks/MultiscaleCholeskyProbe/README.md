# MultiscaleCholeskyProbe

Research probe of the spike [MULTISCALE_SPARSE_CHOLESKY_2026-11.md](../../docs/research/MULTISCALE_SPARSE_CHOLESKY_2026-11.md):
can an approximate (inverse) Cholesky factor, used as the preconditioner of a fixed-order conjugate gradient iteration, beat
the direct `SparseCholesky` on the systems of the library? It is a prototype for an upper bound, not a library candidate: the
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
everything else in the probe is serial. Timings of a shared machine: read them to about 20 %.

### `k=96 k3=30 rho=2,4`

#### sphere k=96 poisson, n = 54151, nnz(A) = 0.38M, one product with A 0.45 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.36 | 3.55M | 1 | 9.4 | 2.8E-014 |
| direct ND | 0.37 | 2.51M | 1 | 4.5 | 2.3E-014 |
| PCG Jacobi | 0 | 0.05M | 917 / 1050 / 1314 | 922 / 1132 | 1.0E-012 |
| PCG IC(0) | 0.01 | 0.22M | 241 / 286 / 358 | 530 / 662 | 9.0E-013 |
| PCG approximate elimination seed 1 (105 positive off-diagonals left out) | 0.17 | 0.37M | 29 / 38 / 56 | 85 / 124 | 9.4E-013 |
| PCG multiscale IC rho=2 | 0.08 + 0.06 + 0.01 | 0.30M | 261 / 312 / 386 | 640 / 784 | 9.0E-013 |
| PCG multiscale FSAI rho=2 (max row 73, 0.02G dense flops, 4 threads) | 0.08 + 0.06 + 0.03 | 0.30M | 378 / 454 / 550 | 992 / 1186 | 9.3E-013 |
| PCG multiscale IC rho=4 | 0.08 + 0.10 + 0.05 | 0.95M | 116 / 139 / 171 | 483 / 596 | 8.8E-013 |
| PCG multiscale FSAI rho=4 (max row 236, 0.69G dense flops, 4 threads) | 0.08 + 0.10 + 0.11 | 0.95M | 248 / 297 / 360 | 1046 / 1261 | 9.2E-013 |

#### sphere k=96 heat, n = 54152, nnz(A) = 0.38M, one product with A 0.64 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.29 | 3.57M | 1 | 6.8 | 5.3E-016 |
| direct ND | 0.41 | 2.52M | 1 | 4.8 | 5.0E-016 |
| PCG Jacobi | 0 | 0.05M | 31 / 42 / 62 | 34 / 49 | 9.6E-013 |
| PCG IC(0) | 0.01 | 0.22M | 10 / 13 / 20 | 22 / 34 | 2.7E-013 |
| PCG approximate elimination seed 1 (105 positive off-diagonals left out) | 0.08 | 0.29M | 13 / 18 / 26 | 39 / 55 | 6.5E-013 |
| PCG multiscale IC rho=2 | 0.07 + 0.06 + 0.01 | 0.30M | 10 / 14 / 21 | 28 / 42 | 3.7E-013 |
| PCG multiscale FSAI rho=2 (max row 72, 0.02G dense flops, 4 threads) | 0.07 + 0.06 + 0.01 | 0.30M | 13 / 18 / 27 | 37 / 56 | 7.0E-013 |
| PCG multiscale IC rho=4 | 0.07 + 0.10 + 0.05 | 0.95M | 6 / 8 / 11 | 29 / 39 | 9.2E-013 |
| PCG multiscale FSAI rho=4 (max row 243, 0.69G dense flops, 4 threads) | 0.07 + 0.10 + 0.10 | 0.95M | 8 / 11 / 17 | 42 / 64 | 2.3E-013 |

#### sphere k=96 bilaplacian, n = 50824, nnz(A) = 0.96M, one product with A 1.04 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.78 | 9.61M | 1 | 24.4 | 3.3E-011 |
| direct ND | 0.35 | 6.76M | 1 | 13.9 | 2.8E-011 |
| PCG Jacobi | 0 | 0.05M | - / - / - | - / - | 2.3E-001 |
| PCG IC(0) shift 0.064 | 0.02 | 0.51M | - / - / - | - / - | 1.0E-003 |
| PCG approximate elimination | not applicable: positive off-diagonals hold 24 % of the off-diagonal weight | | | | |
| PCG multiscale IC rho=2 shift 0.26 | 0.07 + 0.07 + 0.08 | 0.54M | - / - / - | - / - | 1.6E-002 |
| PCG multiscale FSAI rho=2 (max row 76, 0.05G dense flops, 4 threads) | 0.07 + 0.07 + 0.03 | 0.54M | - / - / - | - / - | 2.7E-003 |
| PCG multiscale IC rho=4 shift 0.26 | 0.07 + 0.11 + 0.16 | 0.93M | - / - / - | - / - | 1.7E-002 |
| PCG multiscale FSAI rho=4 (max row 225, 0.66G dense flops, 4 threads) | 0.07 + 0.11 + 0.14 | 0.93M | - / - / - | - / - | 4.9E-004 |

#### sphere k=96 connection bs=2, n = 108304, nnz(A) = 1.41M, one product with A 1.50 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.79 | 14.21M | 1 | 35.7 | 1.9E-014 |
| direct ND | 0.39 | 10.01M | 1 | 26.4 | 1.7E-014 |
| PCG Jacobi | 0 | 0.11M | 617 / 844 / 1174 | 2017 / 2753 | 1.0E-012 |
| PCG IC(0) | 0.02 | 0.81M | 163 / 225 / 315 | 1064 / 1502 | 9.0E-013 |
| PCG multiscale IC rho=2 | 0.06 + 0.07 + 0.02 | 1.16M | 182 / 247 / 345 | 1288 / 1796 | 9.3E-013 |
| PCG multiscale FSAI rho=2 (max row 144, 0.24G dense flops, 4 threads) | 0.06 + 0.07 + 0.06 | 1.16M | 257 / 351 / 489 | 2019 / 2751 | 9.8E-013 |
| PCG multiscale IC rho=4 | 0.06 + 0.13 + 0.24 | 3.73M | 81 / 112 / 154 | 1248 / 1778 | 9.0E-013 |
| PCG multiscale FSAI rho=4 (max row 486, 10.96G dense flops, 4 threads) | 0.06 + 0.13 + 1.32 | 3.73M | 170 / 233 / 323 | 2761 / 3749 | 9.9E-013 |

#### grid3d k=30, n = 27000, nnz(A) = 0.18M, one product with A 0.19 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 0.51 | 6.06M | 1 | 18.6 | 1.9E-015 |
| direct ND | 0.21 | 4.99M | 1 | 12.4 | 1.9E-015 |
| PCG Jacobi | 0 | 0.03M | 93 / 122 / 177 | 46 / 67 | 8.6E-013 |
| PCG IC(0) | 0.00 | 0.11M | 30 / 40 / 58 | 31 / 45 | 6.9E-013 |
| PCG approximate elimination seed 1 | 0.07 | 0.22M | 18 / 24 / 35 | 27 / 41 | 8.1E-013 |
| PCG multiscale IC rho=2 | 0.02 + 0.03 + 0.01 | 0.29M | 25 / 33 / 48 | 43 / 67 | 8.2E-013 |
| PCG multiscale FSAI rho=2 (max row 92, 0.04G dense flops, 4 threads) | 0.02 + 0.03 + 0.01 | 0.29M | 35 / 46 / 69 | 77 / 117 | 6.8E-013 |
| PCG multiscale IC rho=4 | 0.02 + 0.17 + 0.32 | 1.74M | 13 / 17 / 26 | 74 / 113 | 3.0E-013 |
| PCG multiscale FSAI rho=4 (max row 518, 9.78G dense flops, 4 threads) | 0.02 + 0.17 + 1.13 | 1.74M | 19 / 25 / 37 | 112 / 170 | 6.1E-013 |

### `k=96 k3= rho=8 fsai=0 direct=0`

#### sphere k=96 poisson, n = 54151, nnz(A) = 0.38M, one product with A 0.45 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| PCG Jacobi | 0 | 0.05M | 917 / 1050 / 1314 | 893 / 1104 | 1.0E-012 |
| PCG IC(0) | 0.01 | 0.22M | 241 / 286 / 358 | 494 / 615 | 9.0E-013 |
| PCG approximate elimination seed 1 (105 positive off-diagonals left out) | 0.17 | 0.37M | 29 / 38 / 56 | 88 / 130 | 9.4E-013 |
| PCG multiscale IC rho=8 | 0.08 + 0.29 + 0.56 | 3.68M | 25 / 31 / 37 | 276 / 327 | 4.1E-013 |

#### sphere k=96 heat, n = 54152, nnz(A) = 0.38M, one product with A 0.39 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| PCG Jacobi | 0 | 0.05M | 31 / 42 / 62 | 35 / 50 | 9.6E-013 |
| PCG IC(0) | 0.01 | 0.22M | 10 / 13 / 20 | 22 / 35 | 2.7E-013 |
| PCG approximate elimination seed 1 (105 positive off-diagonals left out) | 0.07 | 0.29M | 13 / 18 / 26 | 38 / 54 | 6.5E-013 |
| PCG multiscale IC rho=8 | 0.07 + 0.26 + 0.53 | 3.68M | 3 / 4 / 5 | 44 / 53 | 4.4E-013 |

#### sphere k=96 bilaplacian, n = 50824, nnz(A) = 0.96M, one product with A 0.97 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| PCG Jacobi | 0 | 0.05M | - / - / - | - / - | 2.3E-001 |
| PCG IC(0) shift 0.064 | 0.02 | 0.51M | - / - / - | - / - | 1.0E-003 |
| PCG approximate elimination | not applicable: positive off-diagonals hold 24 % of the off-diagonal weight | | | | |
| PCG multiscale IC rho=8 shift 0.004 | 0.07 + 0.29 + 1.44 | 3.42M | 2265 / 2550 / 2614 | 22932 / 23500 | 2.1E-010 |

#### sphere k=96 connection bs=2, n = 108304, nnz(A) = 1.41M, one product with A 1.43 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| PCG Jacobi | 0 | 0.11M | 617 / 844 / 1174 | 1968 / 2713 | 1.0E-012 |
| PCG IC(0) | 0.02 | 0.81M | 163 / 225 / 315 | 963 / 1361 | 9.0E-013 |
| PCG multiscale IC rho=8 | 0.07 + 0.46 + 2.49 | 14.68M | 17 / 23 / 33 | 1513 / 2161 | 6.6E-013 |

### `k=192 k3=48 rho=4,8 fsai=0`

#### sphere k=192 poisson, n = 218887, nnz(A) = 1.53M, one product with A 1.84 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 1.77 | 18.70M | 1 | 44.4 | 5.3E-014 |
| direct ND | 1.04 | 12.17M | 1 | 28.9 | 4.3E-014 |
| PCG Jacobi | 0 | 0.22M | 1838 / 2185 / 2631 | 7181 / 8685 | 1.1E-012 |
| PCG IC(0) | 0.03 | 0.88M | 498 / 582 / 718 | 4251 / 5266 | 9.9E-013 |
| PCG approximate elimination seed 1 (219 positive off-diagonals left out) | 0.76 | 1.49M | 33 / 43 / 63 | 418 / 604 | 7.7E-013 |
| PCG multiscale IC rho=4 | 0.43 + 0.55 + 0.23 | 3.84M | 234 / 281 / 346 | 5421 / 6551 | 8.2E-013 |
| PCG multiscale IC rho=8 | 0.43 + 1.33 + 2.47 | 15.04M | 52 / 59 / 71 | 4187 / 5013 | 7.0E-013 |

#### sphere k=192 heat, n = 218888, nnz(A) = 1.53M, one product with A 1.78 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 1.60 | 19.62M | 1 | 48.5 | 5.4E-016 |
| direct ND | 0.69 | 12.07M | 1 | 26.9 | 5.0E-016 |
| PCG Jacobi | 0 | 0.22M | 33 / 44 / 67 | 137 / 211 | 7.6E-013 |
| PCG IC(0) | 0.03 | 0.88M | 10 / 13 / 20 | 97 / 145 | 7.9E-013 |
| PCG approximate elimination seed 1 (219 positive off-diagonals left out) | 0.39 | 1.18M | 13 / 18 / 27 | 158 / 235 | 5.2E-013 |
| PCG multiscale IC rho=4 | 0.30 + 0.45 + 0.21 | 3.84M | 6 / 8 / 12 | 140 / 202 | 1.5E-013 |
| PCG multiscale IC rho=8 | 0.30 + 1.29 + 2.27 | 15.04M | 3 / 4 / 6 | 246 / 367 | 7.2E-015 |

#### sphere k=192 bilaplacian, n = 205456, nnz(A) = 3.90M, one product with A 3.83 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 4.99 | 54.33M | 1 | 214.3 | 7.3E-010 |
| direct ND | 1.69 | 34.29M | 1 | 86.1 | 5.9E-010 |
| PCG Jacobi | 0 | 0.21M | - / - / - | - / - | 7.3E-001 |
| PCG IC(0) shift 0.064 | 0.07 | 2.05M | - / - / - | - / - | 4.8E-001 |
| PCG approximate elimination | not applicable: positive off-diagonals hold 24 % of the off-diagonal weight | | | | |
| PCG multiscale IC rho=4 shift 0.26 | 0.29 + 0.47 + 0.70 | 3.80M | - / - / - | - / - | 5.5E-001 |
| PCG multiscale IC rho=8 shift 0.016 | 0.29 + 1.17 + 7.14 | 14.06M | - / - / - | - / - | 9.7E-002 |

#### sphere k=192 connection bs=2, n = 437776, nnz(A) = 5.69M, one product with A 5.88 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 5.64 | 78.20M | 1 | 296.4 | 5.2E-014 |
| direct ND | 2.12 | 48.02M | 1 | 100.4 | 4.4E-014 |
| PCG Jacobi | 0 | 0.44M | 1271 / 1698 / 2360 | 17253 / 24614 | 1.0E-012 |
| PCG IC(0) | 0.08 | 3.28M | 349 / 466 / 627 | 11528 / 15433 | 9.7E-013 |
| PCG multiscale IC rho=4 | 0.31 + 0.69 + 0.97 | 15.15M | 165 / 225 / 311 | 16631 / 23241 | 9.8E-013 |
| PCG multiscale IC rho=8 | 0.31 + 1.95 + 10.81 | 59.92M | 33 / 46 / 65 | 13518 / 19024 | 6.5E-013 |

#### grid3d k=48, n = 110592, nnz(A) = 0.76M, one product with A 0.80 ms

| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |
|---|---|---|---|---|---|
| direct AMD | 8.68 | 59.40M | 1 | 389.7 | 3.1E-015 |
| direct ND | 2.73 | 39.05M | 1 | 189.4 | 2.7E-015 |
| PCG Jacobi | 0 | 0.11M | 143 / 184 / 269 | 346 / 483 | 9.9E-013 |
| PCG IC(0) | 0.01 | 0.44M | 46 / 58 / 87 | 219 / 319 | 9.3E-013 |
| PCG approximate elimination seed 1 | 0.49 | 0.95M | 18 / 25 / 37 | 142 / 213 | 6.8E-013 |
| PCG multiscale IC rho=4 | 0.14 + 0.88 + 1.55 | 7.67M | 15 / 20 / 29 | 621 / 883 | 8.4E-013 |
| PCG multiscale IC rho=8 | 0.14 + 4.76 + 58.33 | 51.50M | 6 / 7 / 10 | 1406 / 2084 | 8.8E-013 |

### `k=409 k3=64 rho=4,8 fsai=0 "only=poisson|heat|grid3d"`

The run was stopped by the session's time limit after its first table (the plain PCG baselines take minutes at this size);
the heat and grid3d tables of this size were not measured.

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
