# ARAP deformation benchmark (#886)

Procedural regular grid with left boundary fixed and right boundary displaced in-plane and with a sinusoidal out-of-plane component. The measured `Compute` includes validation, construction, factorization, initialization, local/global iterations and output copy; mesh generation is outside the timer. The program reports the status and checks finite output and exact handles. `MaxIterations` is a budget, so compare runs with the same budget and report whether they converged.

```
dotnet run --project benchmarks/ArapDeformationBenchmark -c Release -- 100 100 5
dotnet run --project benchmarks/ArapDeformationBenchmark -c Release -- 400 250 5
```

An optional fourth argument sets `Settings.AccelerationDepth` (default 5; 0 is the plain local/global iteration). The output also reports the final dimensionless `energy`.

Record machine, runtime, commit, command and raw output alongside results before making performance claims.

## Initial measurement

Linux x64, SDK 10.0.403, 2026-11-14, one run per size and variant; `results.txt` has the raw output. The initial packed-long edge key produced large hash collisions. The final `Index2i` key gave:

| Grid | Before | After | Five-iteration result |
| --- | ---: | ---: | --- |
| 100 x 100 | 973.4 ms | 663.8 ms | same residual and mesh hash |
| 400 x 250 | 42861.0 ms | 4135.4 ms | same residual and mesh hash |

Both runs hit the five-iteration limit; these are bounded-iteration costs, not converged deformation times. The difference is one measurement on this workload, not a general speedup claim. The mesh hash covers vertex IDs/positions and triangle IDs/indices, not mesh attributes.

To reproduce the before variant on a clean checkout of the ARAP PR head, use a separate worktree, apply `git apply --reverse --unidiff-zero benchmarks/ArapDeformationBenchmark/index2i-change.patch`, then run the two commands above. Run the after variant on the unmodified ARAP PR head. Do not apply the reverse patch to a working tree with unrelated edits.

## Acceleration (Anderson, energy safeguarded)

The plain local/global iteration is monotone in the energy but converges linearly with a rate close to 1 on these inputs; see [design/ARAP_DEFORMATION.md](../../docs/design/ARAP_DEFORMATION.md), "Convergence". Before is `AccelerationDepth = 0` (identical to the iteration of the first version of the PR), after is the default depth 5. Linux x64, 4 cores, .NET SDK 10.0.115, 2026-11-16, one run per row, same process settings; raw lines in `results.txt`. `Success` means the fixed-point residual is below 1e-8.

| Grid | Before (plain) | After (default) | Final energy |
| --- | --- | --- | --- |
| 40 x 40 (1.6k vertices) | 807 iterations, 3467 ms | 52 iterations, 526 ms | 0.0051222859 (both agree to 1e-11 relative) |
| 100 x 100 (10k vertices) | 1802 iterations, 48057 ms | 89 iterations, 2917 ms | 0.0050943440 (both agree to 1e-10 relative) |
| out-of-plane 4-vertex square (probe) | 6562 iterations | 18 iterations | same |

At a fixed small budget the cost per iteration is the same (the accepted candidate replaces the plain step's local step), and the energy is lower:

| 400 x 250 (100k vertices) | Status | Residual | Energy | Time |
| --- | --- | ---: | ---: | ---: |
| plain, 5 iterations | MaxIterations | 5.9e-4 | 0.0051444 | 3449 ms |
| default, 5 iterations | MaxIterations | 2.9e-4 | 0.0051072 | 3356 ms |
| default, 50 iterations | MaxIterations | 4.2e-6 | 0.0050887 | 17625 ms |

The 100k grid still does not reach the default tolerance within the default budget of 50 iterations (residual 4.2e-6, status `MaxIterations`, finite mesh with exact handles); it was not run to convergence at this size. Each measurement is a single run on a shared 4-core container; the iteration counts are exact, the times are indicative. The first-version rows above (packed-long versus `Index2i` key) are unchanged history.
