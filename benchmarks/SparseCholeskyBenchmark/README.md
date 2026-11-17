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

## Shared analysis (`CholeskyAnalysis`, 2026-11-17)

`SparseCholesky` and `SparseHermitianCholesky` share one analysis (ordering, elimination tree, structure of L, supernodes). The
tree is now found from the pattern of A (Liu's algorithm), so the structure of L is computed once, for the postordered matrix,
instead of twice. The arrays, the factor and the solutions are the same bits as before (`CholeskyAnalysisTests`, golden hashes of
the previous code; the benchmark compares the solution hashes with those of the previous code and exits with 1 on a difference).
Factorize (s), best of 3, same machine, before / after:

| matrix | before | after |
|---|---|---|
| grid2d bs=1 n=40000 | 0.17 | 0.15 |
| grid2d bs=2 n=80000 | 0.48 | 0.45 |
| grid2d bs=2 n=120000 | 0.75 | 0.65 |
| grid3d n=27000 | 0.97 | 0.89 |
| hermitian grid2d n=40000 | 0.28 | 0.27 |
| hermitian grid2d n=60000 | 0.43 | 0.42 |

`CrossFieldBenchmark h=0.017` (one thread, best of 3, s, `SparseHermitianCholesky`): sphere smoothest 1.66 -> 1.61, sphere aligned
1.80 -> 1.63, torus smoothest 2.08 -> 2.06, torus aligned 2.14 -> 1.95. The gain is small because the analysis of the 55k-vertex
system is now mostly the minimum degree ordering (0.26 s of 0.33 s; the structure of L takes 0.05 s); a faster ordering with the
same result is the next lever of the analysis.
