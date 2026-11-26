# CoreMathBenchmark

Cost of the correctly rounded `g3.CoreMathPort.CoreMath` (sin, cos, tan, atan2, acos, exp, log and the fused multiply-add; the composed `AcosByAtan2`, which no library code uses any more, for comparison)
against `System.Math`, in ns per call, on 2^16 seeded random arguments. On net10.0 `CoreMath.FusedMultiplyAdd` is
the hardware `Math.FusedMultiplyAdd`; on net48 (the netstandard2.0 build) it is the software version.

```sh
dotnet run -c Release --project benchmarks/CoreMathBenchmark
```

No arguments; it prints the runtime and a Markdown table. The net48 target builds on Windows only. No results are
committed here: the determinism workflow (`.github/workflows/determinism.yml`, see
[tools/DeterminismHarness](../../tools/DeterminismHarness/README.md)) runs it on each of its runners and prints the
table in the job log.

## ExactInterval (`interval` mode, #1646)

Microbenchmarks of `ExactInterval.Up` / `Down` and an interval arithmetic kernel, plus end-to-end
`ExactMeshGeodesicDistance` runs (the heaviest `ExactInterval` caller in the library):

```sh
DOTNET_TieredPGO=0 dotnet run -c Release --project benchmarks/CoreMathBenchmark -- interval
```

Before (`master` `04391d3b`, `BitConverter` `Up`/`Down`) vs after (`perf/exact-interval-rump-succ-pred`, Rump et al. 2009 Theorem 3.1), `.NET 10.0.15`, X64, medians of 7 (microbenchmarks) / 5 (`plane`, `terrain`) / 3 (`saddle`):

| benchmark | before median (ms) | after median (ms) | speedup | before ns/op | after ns/op |
|---|---:|---:|---:|---:|---:|
| `ExactInterval.Up` (normal, 4096 × 20k) | 401.2 | 306.5 | **1.31×** | 4.90 | 3.74 |
| `ExactInterval.Up + Down` (mixed, 4096 × 20k) | 826.6 | 527.4 | **1.57×** | 5.05 | 3.22 |
| `ExactInterval` kernel `sqrt((a-b)^2+b^2)/b` (4096 × 4k) | 1858.1 | 1525.2 | **1.22×** | 113.41 | 93.09 |
| `ExactMeshGeodesicDistance` plane 41×41 (end-to-end) | 446.1 | 348.7 | **1.28×** | - | - |
| `ExactMeshGeodesicDistance` terrain 41×41 (end-to-end) | 939.8 | 662.8 | **1.42×** | - | - |
| `ExactMeshGeodesicDistance` saddle 41×41 (end-to-end) | 4879.8 | 3377.9 | **1.44×** | - | - |
