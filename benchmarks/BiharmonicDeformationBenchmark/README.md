# Biharmonic displacement benchmark (#886)

`BiharmonicMeshDeformer.Compute` on a procedural grid on `[0,1]^2` (two triangles per cell): the left column is fixed,
the right column is moved by `(0.1, 0, 0.05 sin(2 pi y))`. Method and limits: [design note](../../docs/design/BIHARMONIC_DEFORMATION.md).

```
dotnet run --project benchmarks/BiharmonicDeformationBenchmark -c Release -- <nx> <ny> [warm runs, default 5]
```

The first `Compute` of the process is the cold run (JIT included); the median is over the warm runs that follow on
the same instance (the factorization is repeated on every call, so a warm run is not cheaper by caching).

Results (cloud container, 4 cores; the solver is serial; one process per row):

| Vertices | Triangles | Handles | Cold ms | Warm median ms (5 runs) | Allocated MB (cold) | Peak working set MB | Residual |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 000 (100 x 100) | 19 602 | 200 | 760 | 258 | 93 | 182 | 1.5e-15 |
| 99 856 (316 x 316) | 198 450 | 632 | 6 958 | 4 545 | 1 063 | 1 628 | 2.5e-15 |

Timings on a shared machine vary by tens of percent; the 100k run is well under the 20 s limit for a library operation.
