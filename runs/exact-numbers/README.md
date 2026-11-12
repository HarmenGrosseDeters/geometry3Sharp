# runs/exact-numbers

Data, scripts and local prototypes of the study "exact number representation beyond BigInteger" (owner 2026-11-12,
docs/roadmap/exact-boolean.md). Measurement only: this branch changes no library file. Start with `REPORT.md`.

- `REPORT.md`: upper bounds, prototype tables, specs, the Roslyn question, literature, the ranked plan, commands.
- `instrumentation.patch`: timers, counters and the prototype switch on master c0f8aa9 (apply in a scratch worktree).
- `Probe/`: the probe (`dump`, `list`, `micro ...`); it takes the assembly name `BooleanComparison` to read the
  library's internals. Prototypes: `Dyadic.cs` (P2, the monomial accumulator), `ExtExp.cs` (P1), `Micro.cs` (P3, P4
  and the benchmarks), `Implicit.cs` (P3b), `Construct.cs`, `NormBench.cs` (the #719 question on `antipodal_value`).
- `scripts/`: `fetch_cases.py` (Thingi10K at run time; no mesh is committed), the chains that produced the rows,
  `analyze_*.py`, `check_bounds.py` (the accumulator's limb counts and the filter constants, exact arithmetic).
- `specs/`: `dyadic.spec` (proposed `dpredicate`), `scaled_filter.spec`, `wide.spec` with its generated table.
- `results/`: raw rows (one JSON line per operation). `results/mt/` and `results/1core_pool/` are the rows measured
  with the thread pool; their path shares are not valid (REPORT.md 2.1) and they are kept only as a record.
- `data/`: micro benchmark outputs and the case lists (the paths in them are the scratch directory of the session).
