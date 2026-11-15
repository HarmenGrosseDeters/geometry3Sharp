# ARAP deformation benchmark (#886)

Procedural regular grid with left boundary fixed and right boundary displaced in-plane and with a sinusoidal out-of-plane component. The measured `Compute` includes validation, construction, factorization, initialization, local/global iterations and output copy; mesh generation is outside the timer. The program reports the status and checks finite output and exact handles. `MaxIterations` is a budget, so compare runs with the same budget and report whether they converged.

```
dotnet run --project benchmarks/ArapDeformationBenchmark -c Release -- 100 100 5
dotnet run --project benchmarks/ArapDeformationBenchmark -c Release -- 400 250 5
```

Record machine, runtime, commit, command and raw output alongside results before making performance claims.

## Initial measurement

Linux x64, SDK 10.0.403, 2026-11-14, one run per size and variant; `results.txt` has the raw output. The initial packed-long edge key produced large hash collisions. The final `Index2i` key gave:

| Grid | Before | After | Five-iteration result |
| --- | ---: | ---: | --- |
| 100 x 100 | 973.4 ms | 663.8 ms | same residual and mesh hash |
| 400 x 250 | 42861.0 ms | 4135.4 ms | same residual and mesh hash |

Both runs hit the five-iteration limit; these are bounded-iteration costs, not converged deformation times. The difference is one measurement on this workload, not a general speedup claim. The mesh hash covers vertex IDs/positions and triangle IDs/indices, not mesh attributes.

To reproduce the before variant on a clean checkout of the ARAP PR head, use a separate worktree, apply `git apply --reverse --unidiff-zero benchmarks/ArapDeformationBenchmark/index2i-change.patch`, then run the two commands above. Run the after variant on the unmodified ARAP PR head. Do not apply the reverse patch to a working tree with unrelated edits.
