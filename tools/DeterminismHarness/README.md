# DeterminismHarness

Bit-for-bit determinism across runtimes ([DETERMINISM.md](../../docs/design/DETERMINISM.md)): fixed procedural inputs
(`Inputs.cs`) run through the main operations (`Cases.cs`), and the values of each result (integers, doubles as raw bits) go to a
trace. Two trace directories are then compared value by value.

```sh
dotnet build tools/DeterminismHarness -c Release -f net10.0
H="dotnet tools/DeterminismHarness/bin/Release/net10.0/DeterminismHarness.dll"
$H run --out out/a                    # parallel; --serial runs the library's parallel loops in order on one thread
$H run --out out/b --filter <name>    # only the cases whose name contains the substring
$H compare out/a out/b                # first diverging value of every case that differs; --brief for one line each
```

`run` writes `<case>.trace` and `summary.txt` (runtime, OS, architecture, processor count, then the hash and value
count of each case) into the output directory. `compare` exits with 1 if a case differs or exists on one side only.
The net48 target builds on Windows or with `-p:TestNet48=true`.

The workflow `.github/workflows/determinism.yml` runs it on Linux x64 and arm64 (net10.0, parallel twice, serial,
and with `DOTNET_PROCESSOR_COUNT` 1, 2, 3, 8 on x64), on Windows (net10.0, net48, net48 as a 32-bit process) and
optionally macOS arm64, then compares the traces. It is not part of the PR gate: it runs on pull requests that change
the harness, `benchmarks/CoreMathBenchmark` or the workflow, and on demand.
