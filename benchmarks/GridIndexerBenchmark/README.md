# ShiftGridIndexer3.ToGrid probe (#804)

Four million calls per sample over 4096 seeded finite points; each process runs six samples (the first is warmup). Build the two library revisions in separate worktrees with the same SDK and Release configuration. For example, use `git worktree add --detach /tmp/g3-before 85a3a49f` and run `dotnet build /tmp/g3-before/geometry3Sharp_netstandard.csproj -c Release -f net10.0`; build this checkout in the same way. Then run:

```
bash benchmarks/GridIndexerBenchmark/run_abba.sh /absolute/path/to/before/geometry3Sharp.dll /absolute/path/to/after/geometry3Sharp.dll
```

The script prints the DLL hashes, refuses identical binaries, builds the benchmark, and copies the chosen DLL before each of six processes. Set `DOTNET_ROOT` and `PATH` to the SDK when it is not installed system-wide.

```
dotnet build benchmarks/GridIndexerBenchmark -c Release -p:G3Assembly=<absolute-path-to-geometry3Sharp.dll>
dotnet benchmarks/GridIndexerBenchmark/bin/Release/net10.0/GridIndexerBenchmark.dll
```

Copy the intended DLL to the benchmark output directory before each fresh process. `results-804.txt` records the raw output of the 2026-11-14 Linux x64 run (SDK 10.0.403), alternating before/after/after/before/before/after. The before DLL was built at `85a3a49f`; `spatial/GridIndexing.cs` was unchanged through base `41e6c0f0`. The after DLL was built from this change. Excluding each first warmup, per-process medians are 21.1–22.2 ms before and 20.8–21.5 ms after. This is one microbenchmark on net10, not an end-to-end or net48 measurement.

Measured DLL SHA-256: before `2a5ec9913b5d5b9e68ebfb0f6ff8df773e9a8408c0393cca3ae0fc3de06df945`; after `3f4d129983a4e377500d09ac1c9938a2a5e9c21234fd161a7fcb4b1286376264`. Hashes identify the measured builds; rebuilding may produce different binaries.
