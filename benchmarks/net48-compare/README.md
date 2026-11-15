# net48 against net10.0 (#929, step 0)

The deployment target is .NET Framework 4.8. This directory runs the exact boolean and alpha wrap benchmarks on both runtimes on one
machine, from the same sources, with the same cases, the same output rows and the same result hashes:

| benchmark | strata | what is timed | hash |
|---|---|---|---|
| `BooleanComparison profile` | `boolean-cgal`, `boolean-thingi`, `boolean-thingi-tail` (42327, 107554), `boolean-nary` (`grid:5`, `grid:10`, the `nreal` / `narray` cases), each with all cores and with one core (affinity mask 0x1) | `ExactMeshBoolean.Compute()`, `--repeat 1` after the warm-up of `profile` (sum over cases and operations) | `hash` of the result (vertex bits and triangles) |
| `SmallInputWrapBenchmark --hash` | `wrap-small` | `MeshRobustRepair.Apply()` of the seven inputs, old and new mode | `hash` column (opt-in, the default rows are unchanged) |
| `MeshRepairBenchmark`, `BENCH_REPAIR=wrap` | `wrap-repair` (the meshes of `-WrapMeshes`, one process each) | `wrap_ms` | `after_hash` |

The library under test is the same on both: net10.0 builds the library's net10.0 target, net48 the **netstandard2.0** build (which is what
ships for .NET Framework), so the `#if NET` fast paths are missing on net48 (#929, step 2).

## Build

`-p:BenchNet48=true` adds `net48` to the three benchmark projects (without it they build for net10.0 only, as before; CI builds all
benchmarks with `-f net10.0`). The net48 build needs no Mono: the reference assemblies come from the
`Microsoft.NETFramework.ReferenceAssemblies` package. Running it needs Windows.

```powershell
dotnet build benchmarks\BooleanComparison\BooleanComparison.csproj -c Release -p:BenchNet48=true
dotnet build benchmarks\SmallInputWrapBenchmark\SmallInputWrapBenchmark.csproj -c Release -p:BenchNet48=true
dotnet build benchmarks\MeshRepairBenchmark\MeshRepairBenchmark.csproj -c Release -p:BenchNet48=true
# bin\Release\net10.0\<name>.dll (dotnet <name>.dll ...) and bin\Release\net48\<name>.exe
```

Runtime APIs that net48 lacks are behind `NetCompat.cs` (`Convert.ToHexString` / `SHA256.HashData`, `GC.GetTotalAllocatedBytes`,
`GC.GetTotalPauseDuration`, `GC.GetGCMemoryInfo`, `File.Move(.., true)`, `Math.Cbrt`, `BigInteger.GetBitLength`, `string.Split(string)`). On net10.0 each member calls
the API it replaces. On net48 there is no GC pause duration (reported as 0), the allocated bytes come from `AppDomain` monitoring and the heap
size is `GC.GetTotalMemory`: the phase times, hashes and counts are comparable between the runtimes, the GC columns are not.
`Math.Cbrt` is `Math.Pow` plus a Newton step on net48 and can differ in the last bit, so the `profile spheres:<k>` cases (random inputs scaled
by it) are not in the strata; the hash comparison covers `grid:<n>` and the file cases.

## Data (fetched at run time, nothing is committed)

`-BData` (default `%BDATA%` or `~\bdata`) holds `cases\<class>\<case>\{A,B}.off`, as for `perf/run_classes.sh`
(`docs/perf/EXACT_BOOLEAN_PERFORMANCE.md`, "How to reproduce"); the case lists are `benchmarks/BooleanComparison/perf/cases-*.txt`.

```powershell
$B = "$HOME\bdata"
# cgal: the CGAL test meshes, each with a rotated, shifted copy
python benchmarks\BooleanComparison\data\fetch.py cgal $B\cgal-src
foreach ($f in Get-ChildItem $B\cgal-src -Recurse -Include *.off,*.stl,*.obj) {   # the meshes of perf\cases-cgal.txt
    dotnet benchmarks\BooleanComparison\bin\Release\net10.0\BooleanComparison.dll export $f.FullName "$B\cases\cgal\$($f.BaseName)"
}
# Thingi10K: a case is the model id (perf\cases-thingi.txt, and the slow tail 42327 and 107554)
foreach ($id in (Get-Content benchmarks\BooleanComparison\perf\cases-thingi.txt | ForEach-Object { Split-Path -Leaf $_ }) + '42327','107554') {
    $stl = "$B\thingi-src\$id.stl"; New-Item -ItemType Directory -Force "$B\thingi-src" | Out-Null
    if (-not (Test-Path $stl)) { Invoke-WebRequest "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/$id.stl" -OutFile $stl }
    dotnet benchmarks\BooleanComparison\bin\Release\net10.0\BooleanComparison.dll export $stl "$B\cases\thingi\$id"
}
# n-ary, real inputs (cases-nreal.txt): see perf\nreal.py (BooleanComparison components / array) and the data above
# wrap-repair: a sample of Thingi10K models (about 20 are enough), the meshes are in data\sample<N>_<seed>\meshes
python benchmarks\MeshRepairBenchmark\fetch_data.py --sample 20 --seed 2611
```

(`export` writes the mesh as `A.off` and a rotated, shifted copy as `B.off`, the same on both runtimes; the cases are written once with net10.0
and read by both.) Thingi10K models carry their own licenses; do not commit them.

## Run

`run.ps1` (Windows PowerShell 5.1 or PowerShell 7) builds both targets, then for every stratum of `strata.csv` and every round runs the
runtimes one after the other, the order flipping each round (net10.0 first in odd rounds, net48 first in even ones). Default: 5 rounds,
`-Runtimes net10.0,net48`.

```powershell
benchmarks\net48-compare\run.ps1 -BData $HOME\bdata -WrapMeshes benchmarks\MeshRepairBenchmark\data\sample20_2611\meshes   # all strata, 5 rounds
benchmarks\net48-compare\run.ps1 -Stratum boolean-thingi-tail -Rounds 5 -BData $HOME\bdata                              # one stratum
benchmarks\net48-compare\run.ps1 -Stratum boolean-thingi -Runtimes net10.0,net10.0-pgo0,net48 -BData $HOME\bdata        # net10 with DOTNET_TieredPGO=0 as a third runtime
benchmarks\net48-compare\run.ps1 -Smoke                                                                                 # the CI check: tiny procedural cases, one round
```

`-Runtimes net10.0-<suffix>` is net10.0 with the variables of `-Net10Env` (`@{ DOTNET_TieredCompilation = '0' }`); `net10.0-pgo0` is built in
(`DOTNET_TieredPGO=0`). net48 has no tiered PGO. "one" thread strata run the process on core 0 (`ProcessorAffinity`); on net48
`Environment.ProcessorCount` still reports all cores, so a parallel loop sized by it runs more tasks on the one core, which is a real property of
that runtime. Close other programs, plug in the machine, and use the same power plan for all rounds.

Output in `-OutDir` (default `benchmarks\net48-compare\results\<timestamp>`, not committed):

| file | content |
|---|---|
| `runs.csv` | one row per run: `stratum,runtime,round,threads,time_ms,hash,cases,status`; the hash is the SHA-256 (16 digits) over the result hashes of all cases of the run |
| `cases.csv` | the same per case and operation: `stratum,runtime,round,threads,case,op,ms,hash` |
| `settings.json` | machine, cores, OS, SDK, the `DOTNET_*` / `COMPlus_*` variables of the run (the JIT settings) |

The script exits with 1 and prints `HASH MISMATCH` if the hashes of one stratum differ between runs or runtimes (a determinism finding,
#789 / #803 / #804), or if a run failed. Median and range per stratum and runtime over the rounds are taken from `runs.csv`, eg

```powershell
Import-Csv runs.csv | Group-Object stratum, threads, runtime | ForEach-Object {
    $t = $_.Group | ForEach-Object { [double]$_.time_ms } | Sort-Object
    '{0}: median {1} ms, range {2}-{3}' -f $_.Name, $t[[int][math]::Floor(($t.Count - 1) / 2)], $t[0], $t[-1] }
```

## Thread scaling per phase (#858)

`-Scaling` runs the stratum `scaling-b30` on both runtimes at 1, 2, 4, 6 and 12 threads, 3 rounds each (`-ThreadCounts`, `-ScalingRounds`): the 73
models of the Exact Boolean 3.0 pre-study (`benchmarks/Boolean30Prestudy/results/scaling_ids.txt`, `docs/design/EXACT_BOOLEAN_30_PRESTUDY.md`
section 1), the #385 pairing (B = A rotated 20 degrees and shifted, `BooleanComparison export`), union / intersection / difference, SafeRounding on,
one timed run per operation after the warm-up. **The whole matrix is a long, overnight run** (the pre-study's 1-core pass alone took 1086 s per
runtime, times 5 thread counts, 3 rounds and 2 runtimes, plus the 12-thread passes' own startup); nothing else may run on the machine.
For a first check use the short variant: 1 and 12 threads, 1 round.

```powershell
# data: the 73 models, then the cases thingi\<id> as above (fetch_thingi.py pins the revision)
python benchmarks\LevyClassification\fetch_thingi.py $HOME\bdata\b30-raw (Get-Content benchmarks\Boolean30Prestudy\results\scaling_ids.txt | ForEach-Object { ($_ -split ' ')[0] })
# (export every model with BooleanComparison export <raw>\<id>.stl $HOME\bdata\cases\thingi\<id>, as in the loop above)

benchmarks\net48-compare\run.ps1 -Scaling -Quick -BData $HOME\bdata                  # first check: 1 and 12 threads, 1 round
benchmarks\net48-compare\run.ps1 -Scaling -BData $HOME\bdata -OutDir D:\scaling      # the whole matrix: 1, 2, 4, 6, 12 threads, 3 rounds, both runtimes
python benchmarks\net48-compare\report.py D:\scaling [--subphases]                   # per runtime: time, speed-up and serial floor per phase and thread count
```

Threads are limited by the **process affinity mask** (cores 0..n-1, so mask 0x3F for 6 threads; `ProcessorAffinity` set right after the start) on both
runtimes, not by `DOTNET_PROCESSOR_COUNT`, which net48 does not have; the mask the process reports back is the `mask` column of `runs.csv`,
`cases.csv` and `phases.csv`, and `procs` in `cases.csv` is the `Environment.ProcessorCount` the tool saw (net10.0 follows the mask, net48 reports
all cores, so a parallel loop sized by it runs more tasks than cores there; that is part of what is measured). Logical cores 0..n-1 may be hyper-threads
of fewer physical cores: note the CPU topology with the results. A thread count above the machine's logical cores is skipped with a warning. The
result hashes of the stratum must be equal at every thread count and on both runtimes (one hash over all runs; a difference exits with 1).

`phases.csv` has one row per run, case, operation and phase (`phases_ms` of `ExactMeshBoolean`, in milliseconds). `report.py` sums a phase over
the cases and operations of a run, takes the median over the rounds, and prints per runtime and phase the time at each thread count, the speed-up
t1 / tn and the **serial floor**: t1 times the Amdahl serial fraction s fitted to all thread counts (t_n = t_1 (s + (1 - s) / n)), the time the
phase keeps with any number of cores. The phases are the exclusive top-level phases as in `analyze_scaling.py` (assembly without simplify);
`--subphases` adds `intersections.pairs`, `merge.points` and the others. A last table gives net48 against each net10.0 runtime per thread count.

## CI

The `netstandard` job (`windows-latest`) builds the three benchmarks for both targets and runs `run.ps1 -Smoke` in Windows PowerShell and
PowerShell 7: `grid:2` (n-ary boolean), the `62 B repro` of `SmallInputWrapBenchmark` (new mode) and the alpha wrap of a procedural octahedron,
one round on each runtime, failing if a hash differs between net10.0 and net48. The job runs when one of the three benchmarks or this directory
changes (`tools/ci_scope.py`, output `ps1`), and with the library tests; the Linux job runs the unit test of `report.py`. No timing is judged in CI.
