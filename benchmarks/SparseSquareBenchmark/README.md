# SparseSquareBenchmark

`SymmetricSparseMatrix.Square` / `Multiply` (parallel) and `SquarePackedParallel` on the weighted Laplacian of a sphere mesh
(N = 3176, `res=24`). Prints the best time of `reps` runs and the number of distinct hashes of the product applied to a vector in
8 runs; it must be 1 (the exit code is 1 otherwise).

```
dotnet run -c Release --project benchmarks/SparseSquareBenchmark -- [reps=5] [res=24]
```

## Rows inserted in order (2026-11-22, sibling of issue 1314)

The parallel loops of `SymmetricSparseMatrix.Square` and `Multiply` inserted the entries into the dictionary under `lock(R)`, in the
order the loop ran its rows; `Multiply(double[], double[])` adds the terms of a product in the order of the dictionary. Now each row
is computed into its own slot and the rows are inserted in order. 4-core container, `net10.0` Release, best of 5:

| | before | after |
|---|---|---|
| `Square(true)` | 64.8 ms | 45.7 ms |
| `Multiply(m, ref r, true)` | 38.0 ms | 20.3 ms |
| `Square(false)` (unchanged) | 25.0 ms | 28.7 ms |
| `SquarePackedParallel` (unchanged) | 6.2 ms | 4.6 ms |
| distinct product hashes in 8 runs, `Square` / `Multiply` | 8 / 8 | 1 / 1 |

(The timing noise between runs is about 20%; the point is that the lock is gone and the bits are fixed.)
