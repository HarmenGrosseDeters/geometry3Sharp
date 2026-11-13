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
$H adversary --seeds 5                # every case under adversarial loop orders, compared with the serial run (below)
$H run --out out/c --order -1         # one run under a loop order (implies --serial): -1 backwards, n > 0 a seeded permutation
```

`run` writes `<case>.trace` and `summary.txt` (runtime, OS, architecture, processor count, then the hash and value
count of each case) into the output directory. `compare` exits with 1 if a case differs or exists on one side only.
The net48 target builds on Windows or with `-p:TestNet48=true`.

The workflow `.github/workflows/determinism.yml` runs it on Linux x64 and arm64 (net10.0, parallel twice, serial,
and with `DOTNET_PROCESSOR_COUNT` 1, 2, 3, 8 on x64), on Windows (net10.0, net48, net48 as a 32-bit process) and
optionally macOS arm64, then compares the traces. It is not part of the PR gate: it runs on pull requests that change
the harness, `benchmarks/CoreMathBenchmark` or the workflow, and on demand.

## Schedule adversary

`adversary` is the first piece of the environment adversary of
[DETERMINISM_PROOF.md](../../docs/design/DETERMINISM_PROOF.md) (option B). The library's parallel loops (`gParallel.ForRange`,
`ForEach`, `BlockStartEnd`) run serially under `gParallel.SerialOnThisThread`; `gParallel.SerialOrderOnThisThread` then
chooses the order of each loop's items: as given, backwards, or a seeded permutation, a new one for every loop. A result
that depends on the schedule (the order of appends to a shared list, ids handed out by a counter, a reduction) changes
with that order. Every case runs in the given order, backwards and under `--seeds` permutations, in one process; the
command prints the cases whose values change and exits with 1 if there is one. Two parallel runs sample the schedules
the machine happens to produce; this explores orders that no thread pool produces. Not covered: loops that call
`Parallel.For` directly (`PartitionedReduction`, `TerrainGridTin`, the 2D quadtrees, `SafeCollections`), and the
interleaving inside one item. The tests `OrderAdversaryTests` run the same check on more inputs in the PR tier.

## Probes

`probe.*` cases use no library code. They record runtime behaviour that the specification leaves open or that changed
between .NET Framework and .NET: `Math.Min / Max` on signed zeros and NaN (`probe.minmax-zero`), `double` to integer
conversions out of range (`probe.cast-range`), `System.Random` with a seed (`probe.random-seeded`), `GetHashCode` of key
types (`probe.hash-values`), the enumeration order of `Dictionary` / `HashSet` after inserts, removals and refills
(`probe.hash-enumeration`), the order of equal keys after the unstable sorts (`probe.sort-ties`, `probe.sort-ties-large`).
`probe.hash-seeded` (`BigInteger`, `ValueTuple`, `string`) **differs between any two runs on .NET**, on one machine too:
these hash codes are seeded per process. It is listed by every comparison of two .NET runs; that is its result.
`probe.slope-degrees` and `terrain.slope-design*` cover the platform libm calls in `TerrainSlope*`.
