# CoreMathBenchmark

Cost of the correctly rounded `g3.CoreMathPort.CoreMath` (sin, cos, atan2, exp, log and the fused multiply-add)
against `System.Math`, in ns per call, on 2^16 seeded random arguments. On net10.0 `CoreMath.FusedMultiplyAdd` is
the hardware `Math.FusedMultiplyAdd`; on net48 (the netstandard2.0 build) it is the software version.

```sh
dotnet run -c Release --project benchmarks/CoreMathBenchmark
```

No arguments; it prints the runtime and a Markdown table. The net48 target builds on Windows only. No results are
committed here: the determinism workflow (`.github/workflows/determinism.yml`, see
[tools/DeterminismHarness](../../tools/DeterminismHarness/README.md)) runs it on each of its runners and prints the
table in the job log.
