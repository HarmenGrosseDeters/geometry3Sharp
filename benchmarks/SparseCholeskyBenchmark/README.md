# SparseCholeskyBenchmark

`SparseCholesky.Factorize()` (minimum-degree ordering, symbolic part, numeric part) and `Solve()` on procedural symmetric
positive definite matrices with the structure of mesh systems: a triangulated grid with 1 to 3 unknowns per node (`bs=2` is the
shape of the interleaved 2n x 2n `CrossField` system) and a 3D grid. The last column is a hash of the solution vector; it must be
the same before and after a change of the kernels and on every machine (the benchmark compares it with the values below and exits with 1 on a difference) (the dense kernels give the same bits as the scalar code,
`SparseCholeskyKernelTests`).

```
dotnet run -c Release --project benchmarks/SparseCholeskyBenchmark -- [only=<regex>] [reps=3]
```

## Results (2026-11-17, net10.0 Release, one thread, 4-core Xeon at 2.1 GHz in a cloud container, best of 3)

Scalar dot kernels (master at da564d55) against the vector kernels (`SparseCholeskyKernels`: the four partial sums of
the scalar dot in the four lanes of a `Vector256`, four dot products at once in the update and the dense factorization of the
supernodes). `Solve` is unchanged (its loops are one sequential sum per entry).

| matrix | nnz(L) | Factorize before (s) | Factorize after (s) | Solve before / after (ms) | solution hash (both) |
|---|---|---|---|---|---|
| grid2d bs=1 n=40000 | 1.6M | 0.22 | 0.19 | 6.3 / 6.3 | 5af59d2818b8f21d |
| grid2d bs=2 n=80000 (CrossField-like) | 6.3M | 0.84 | 0.52 | 32.9 / 33.5 | ce7f838b19a99e97 |
| grid2d bs=2 n=120000 (CrossField-like) | 9.9M | 1.29 | 0.79 | 51.2 / 53.0 | 34e470467305cff4 |
| grid3d n=27000 | 6.1M | 1.99 | 1.04 | 43.7 / 45.1 | 641b48317c4b9c92 |

Factorize includes the serial analysis (ordering and symbolic part, about 0.4 s per 100k unknowns), which the kernels do not touch. End to end, `CrossFieldBenchmark h=0.017` (one thread, `reps=1`):

| shape | field | V | before (s) | after (s) |
|---|---|---|---|---|
| sphere | smoothest | 54944 | 5.3 to 5.5 | 3.0 |
| sphere | aligned s=1 | 54944 | 5.5 to 6.0 | 3.1 |
| torus | smoothest | 60712 | 5.9 to 6.3 | 3.6 |
| torus | aligned s=1 | 60712 | 5.6 to 6.1 | 3.6 |

(The field, the eigenvalues and the singular faces are unchanged.)
