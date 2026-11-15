# LevyClassification

Phase-1 probe for the classification by propagation of `ExactMeshBoolean`
([LEVY_CLASSIFICATION.md](../../docs/design/LEVY_CLASSIFICATION.md), where the method, the measurements and their
conclusions are). `Program.cs` runs `ExactMeshBoolean` on BooleanComparison case directories (`A.off`, `B.off`, as
written by `BooleanComparison export`) and writes one JSON line per operation with the phase times, the counters, the
patch count and the connected components of A and B. Public API only. No mesh files are committed; the Thingi10K
inputs are downloaded by `fetch_thingi.py`.

```sh
dotnet run -c Release --project benchmarks/LevyClassification -- [--ops union,intersection,difference] [--no-safe-rounding] <case dir>...
bash benchmarks/LevyClassification/job.sh <work dir> [sample_random.txt]   # the cohort run (default sample.txt), from the repository root
python3 benchmarks/LevyClassification/analyze.py <work dir>/out > benchmarks/LevyClassification/results/summary.md
```

The other scripts each give their usage in their header: `trace.sh` with `trace_split.py` and `lock_spin.py`
(dotnet-trace thread-time samples), `exceptions.py` (the ops above 5 s with all levers: size, time and phase mix), and the A/B jobs of the
prototypes in `prototype/` (run after `job.sh`; `phase2_ab.sh` needs two worktrees in `$S`).

`results/` holds the committed rows and summaries (`summary.md`, `random/summary.md`, `exceptions_levers.md`, the
`*.jsonl` files, `random/`, `phase2/` and `open_per_piece/`); they are records of the runs described in the design note.
