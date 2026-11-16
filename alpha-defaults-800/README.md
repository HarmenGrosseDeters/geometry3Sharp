# alpha-defaults-800: the wrap defaults on the 800-mesh sample

Raw data only (no meshes; Thingi10K is fetched at run time). Measures the two owner default decisions on the 800 wrap
sample: the sharp-feature pass (`BENCH_WRAP_FEATURES=1`, #736) and the raw-size budget (`BENCH_WRAP_BUDGET=300000`, #846),
against the library defaults. Library: master `2014e650`, unchanged.

## Configurations (same 800 meshes, interleaved per mesh)

- A: library defaults. `BENCH_REPAIR=wrappipe`, alpha = diagonal / 300, offset = diagonal / 3000, full fidelity
  (`BENCH_FIDELITY` unset), warm-up soup (default), `TIMEOUT=400`.
- B: A plus `BENCH_WRAP_FEATURES=1`.
- C: A plus `BENCH_WRAP_BUDGET=300000` (`MeshAlphaWrap.MaxEstimatedTriangles`).

The same settings as the earlier 800 runs (`runs/747-800`, `runs/alpha-wrap-3` `aw3/f800/drive.sh`, which used
`BENCH_FIDELITY=hd`; this run uses full fidelity as asked).

## Bench fix needed for C (the only change to the benchmark)

On master, `BENCH_WRAP_BUDGET` was read only by the robust mode (`Program.cs`, the `robust` object). The `wrappipe` path
builds its `MeshAlphaWrap` itself and never set `MaxEstimatedTriangles`, so C would have been identical to A. The fix
sets `wrap.MaxEstimatedTriangles` from `BENCH_WRAP_BUDGET` in `WrapPipeline`, and writes `wrap_estimate` and
`wrap_coarsening` (`EstimatedTriangles`, `AlphaCoarsening`) into the row. It is in
`bench_wrap_budget_and_coarsening.patch` (applied to `benchmarks/MeshRepairBenchmark/Program.cs` on master, not
committed to master). With the variable unset the output of A is bit-identical to master's (checked on mesh 100040:
same raw triangles, output triangles, angles, Hausdorff bounds, IoU and estimate).

## Commands

```
git worktree add -b runs/alpha-defaults-800 <dir> 2014e6505a0f91a5f3476c9bf0125b84e5c2d403
git apply alpha-defaults-800/bench_wrap_budget_and_coarsening.patch      # in the master checkout, for the bench only
dotnet build benchmarks/MeshRepairBenchmark -c Release                    # dotnet-sdk-10.0
python3 benchmarks/MeshRepairBenchmark/fetch_data.py --sample 800         # sample800_2611 (seed 2611), 1.1 GB, not committed
alpha-defaults-800/run800.sh                                              # the loop (resumable, pushes every 100 meshes)
python3 -I alpha-defaults-800/summarize.py > alpha-defaults-800/summary.md
```

`run800.sh` runs each configuration as
`env -u BENCH_WRAP_FEATURES -u BENCH_WRAP_BUDGET -u BENCH_FIDELITY [config env] BENCH_REPAIR=wrappipe TIMEOUT=400 benchmarks/MeshRepairBenchmark/run.sh <mesh>`
(config env: none for A, `BENCH_WRAP_FEATURES=1` for B, `BENCH_WRAP_BUDGET=300000` for C), one process at a time.

## Files

- `list800.txt`: the 800 mesh ids (sample800_2611, the list of `runs/747-800`), mesh file = `<id>.stl`.
- `raw/A.jsonl`, `raw/B.jsonl`, `raw/C.jsonl`: one JSON line per mesh, the benchmark's full output (all columns);
  a line `{"file":..,"status":"timeout_or_crash"}` is a run without output (timeout if wall >= 400 s, else crash).
- `wall.tsv`: `<file>	<config>	<wall seconds>` per process.
- `summarize.py`: the summary; `summary.md`: its output.
- `run800.sh`: the runner.
