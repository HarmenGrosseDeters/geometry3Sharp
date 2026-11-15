# AxisAlignedBox3d.Contain probe (#803)

Four million calls over 4096 seeded finite random points per sample, six samples in each process (the first is warmup). `Program.cs` reports milliseconds and a checksum of the x bounds. Build the two library revisions in separate worktrees with the same SDK and Release configuration. For example, use `git worktree add --detach /tmp/g3-before 85a3a49f` and run `dotnet build /tmp/g3-before/geometry3Sharp_netstandard.csproj -c Release -f net10.0`; build this checkout in the same way. Then run:

```
bash benchmarks/BoxContainBenchmark/run_abba.sh /absolute/path/to/before/geometry3Sharp.dll /absolute/path/to/after/geometry3Sharp.dll
```

The script prints the DLL hashes, refuses identical binaries, builds the benchmark, and copies the chosen DLL before each of six processes. Set `DOTNET_ROOT` and `PATH` to the SDK when it is not installed system-wide.

```
dotnet build benchmarks/BoxContainBenchmark -c Release -p:G3Assembly=<absolute-path-to-geometry3Sharp.dll>
dotnet benchmarks/BoxContainBenchmark/bin/Release/net10.0/BoxContainBenchmark.dll
```

Run a fresh process per library, alternating before/after; copy the intended DLL to the benchmark's output directory before each process. `results-803.txt` contains the raw output of the 2026-11-14 Linux x64, SDK 10.0.403 run, before/after/after/before/before/after. The before DLL came from the `85a3a49f` build; `AxisAlignedBox3d.cs` is unchanged between that commit and base `41e6c0f0`. The after DLL was built from this change. The first sample in each process is warmup. In this small steady-state point workload the five-sample medians were 19.0–23.7 ms before and 10.3–12.4 ms after; these are not end-to-end box-tree timings. Values vary with runtime and JIT tiering, so they are not a general speedup claim.

Measured DLL SHA-256: before `2a5ec9913b5d5b9e68ebfb0f6ff8df773e9a8408c0393cca3ae0fc3de06df945`; after `4945f9892832d6e0345ec86524b6bc1319fafe0dbaef700d729c6307a25a6938`. Hashes identify the measured builds; rebuilding may produce different binaries.
