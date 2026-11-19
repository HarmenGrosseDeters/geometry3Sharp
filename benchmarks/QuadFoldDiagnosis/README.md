# QuadFoldDiagnosis (data branch runs/quad-folds, measurement only, not for merge)

Diagnosis of the folds of the pure-quad flow (stage 3B) on the 54 runs of `benchmarks/QuadFlow3BGate`. `QuadFlowDiag.cs` is a copy of
`mesh_ops/QuadFlowRepair.cs` with hooks (SAT round limit, flow re-solves with penalized steps, fold analysis); with the library settings it
reproduces the library bit for bit on 54 / 54 runs (status, folds, decisions, all offsets; section 0 of the output).
`results.md` is the full output (`x100=1`, 1106 s, 4-core container); `results_b_selectivity_rerun.md` is a rerun with `d=0` that adds the
selectivity line of (b). Library change: one `InternalsVisibleTo` line.

    dotnet run -c Release --project benchmarks/QuadFoldDiagnosis -- [only=<regex>] [d=0|1] [x100=0|1] [pen=<factor>] [rounds=<n>]
