# M1 point-set meshing: clean-room bootstrap / public API A/B

This is an in-repository correctness and cost baseline, **not** a comparison with Triangle, Triangle.NET
or any external mesher. `BootstrapCandidate.cs` preserves the original-coordinate prototype from the
M1 spike. It starts from an input triple, splits faces/edges or expands the visible hull, and uses exact
Lawson flips with input-ID ties. Its original flip guard is retained; guard exhaustion fails the run.
No restricted implementation was used. The production mesher and existing exact arithmetic are unchanged
by this benchmark. See [the M1 contract](../../docs/design/MESHING2D_M1.md) and
[implementation sources](../../docs/SOURCES.md).

## What is measured

- A (`bootstrap`): construct the prototype and return its face array.
- B (`public-m1`): `DelaunayMesh2.Compute`, including input snapshot, mapping, owned result, neighbour
  construction, canonical ordering and exhaustive production verification, plus copying the faces for
  comparison. A does **not** provide those public-contract services. The difference is end-to-end cost,
  not an isolated algorithm speedup/regression.
- Warm up once per fixture/engine, then measure sequential runs and report medians. Collect between runs.
  Stopwatch setup, fixture generation, hashes and the independent oracle are outside the timed interval.
- net10 counts current-thread allocation using `GC.GetAllocatedBytesForCurrentThread`; net48 uses the
  AppDomain monitoring allocation counter. The latter can include runtime/background allocation and has
  runtime-dependent sampling granularity. Do not interpret the counters as interchangeable, retained
  memory, peak working set or a cross-runtime speed ranking.
- The processes are bounded to 180 seconds each by the command below. Any failure, different A/B topology,
  nondeterministic face fingerprint or invalid output exits nonzero.

Fixtures are identical across frameworks: seeded integer uniform points, integer grids (cocircular ties
and initially collinear insertion), and nearly collinear dyadic points with transverse offsets of `2^-40`.
Input SHA-256 hashes use the ordered coordinate bit patterns. Topology SHA-256 hashes use canonical CCW
input-index triples. There are no mesh assets.

The shared **untimed independent** oracle converts fixture coordinates exactly to integers in units of
`2^-40`, then uses `BigInteger` determinants, empty circumcircles, used vertices, Euler characteristic,
reciprocal edge directions, unsplit-edge checks, hull-supporting boundaries, edge crossings and a separate
monotone-chain hull area. It does not call production predicates or `IsValid`. At process startup it must
reject deliberately inverted, missing, repeated and non-Delaunay faces. It checks every warmup and sample.

## Reproduce

From the repository root, build both Windows targets, then run each separately:

```powershell
dotnet build benchmarks\Meshing2Benchmark\Meshing2Benchmark.csproj -c Release
foreach ($tfm in @('net10.0', 'net48')) {
    $exe = Join-Path $PWD "benchmarks\Meshing2Benchmark\bin\Release\$tfm\Meshing2Benchmark.exe"
    $out = Join-Path $PWD "benchmarks\Meshing2Benchmark\perf\20261029-$tfm.csv"
    $err = Join-Path $env:TEMP "meshing-benchmark-$tfm.err"
    $p = Start-Process -FilePath $exe -ArgumentList '3' -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
    $handle = $p.Handle
    if (-not $p.WaitForExit(180000)) {
        Stop-Process -Id $p.Id
        throw "Benchmark exceeded timeout: $tfm"
    }
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { throw "Benchmark failed: $tfm; inspect $err" }
}
```

The explicit handle acquisition preserves exit-code reporting with this Windows PowerShell launcher.
On non-Windows systems, the project builds only net10; run the produced executable/dll with argument `3`.
The argument sets the repetition count, not the fixture size.

## Recorded baseline: 2026-10-29

Raw output from the commands above:
[net10.0](perf/20261029-net10.0.csv) (runtime 10.0.12) and [net48](perf/20261029-net48.csv).
Both processes exited 0, with matching input/topology hashes across A/B and both frameworks.
The files include both point counts; this table selects the larger fixtures:

| Runtime | Fixture / points | A median ms | B median ms | A allocated bytes | B allocated bytes |
|---|---|---:|---:|---:|---:|
| net10 | uniform / 256 | 280.964 | 395.296 | 102920744 | 92961432 |
| net10 | grid / 256 | 723.279 | 1086.770 | 189913992 | 176345008 |
| net10 | near-collinear / 256 | 140.810 | 96.250 | 29799584 | 24519360 |
| net48 | uniform / 256 | 524.099 | 858.844 | 106756272 | 115920848 |
| net48 | grid / 256 | 1543.553 | 1676.589 | 196963896 | 223454872 |
| net48 | near-collinear / 256 | 105.277 | 263.976 | 30900960 | 29133368 |

This is a small, sequential run on a shared Windows machine. Timing noise and tiered-JIT warmup can be
substantial, particularly at the smaller size; engine order is fixed, not randomized. No percentage
speedup, stable scaling law or production throughput promise is inferred. The measured full scans and
allocation volumes motivate a separately scoped optimization task if large inputs become a requirement.
M1 correctness is supported by the independent oracle and identical topology, not by timing alone.
