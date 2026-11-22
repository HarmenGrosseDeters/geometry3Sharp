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
$H run --out out/c --order -1         # one run under a loop order (serial switch on): -1 backwards, n > 0 a seeded permutation
```

`run` writes `<case>.trace` and `summary.txt` (runtime, OS, architecture, processor count, then the hash and value
count of each case) into the output directory. `compare` exits with 1 if a case differs or exists on one side only.
The net48 target builds on Windows or with `-p:TestNet48=true`.

The workflow `.github/workflows/determinism.yml` runs it on Linux x64 and arm64 (net10.0, parallel twice, serial,
and with `DOTNET_PROCESSOR_COUNT` 1, 2, 3, 8 on x64), on Windows x64 (net10.0, net48, net48 as a 32-bit process), on
Windows arm64 (net10.0 and net48; a standing leg since 2026-11-22: its failure fails the workflow) and optionally macOS arm64 and x64,
then compares the traces. It is not part of the PR gate: it runs on pull requests that change
the harness, `benchmarks/CoreMathBenchmark` or the workflow, and on demand.

## Documented operations, generators and the cross-runtime probes

Added for the cross-runtime study ([DETERMINISM_CROSS_RUNTIME_2026-11.md](../../docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md)):

- **`examples.*`** (`Examples.cs`): every public static method of the compiled examples of `docs/OPERATIONS.md` (`docs/examples/*.cs`,
  compiled into the harness) on fixed arguments; a method with an enum parameter is one case per value. Arguments are chosen by type
  and parameter name (meshes and points from `Inputs`, which use only `+ - * /` and `sqrt`); a parameter the table does not know
  makes the case record `unsupported` with a note, so a new example shows up in the comparison. The results (the return value,
  `out` parameters, meshes changed in place) are traced by reflection: numbers, enums, strings as an FNV-1a hash, meshes, the `g3`
  value types by their fields, sequences element by element, other library objects by their public fields and properties in
  the order of their names, six levels deep; `BigInteger`, `decimal` and tuples are traced by value. A member is left out
  when a whole word of its name marks a timing or a counter of work (`Seconds`, `Elapsed`, `Stats`, `Bytes`, ..., or a last
  word `Time`), or when it is a string named like a message (`Error`, `Reason`, `Message`, ...); `Status`, `RMSError`,
  `TimeOfImpact` and the like are results. `PointCloudSurface.BandNodes`, documented as approximate, is left out too. What
  the trace cannot follow (another BCL object, a value below the depth limit) is counted in the section `not-traced` and
  named in a note of the run, so nothing is skipped silently.
- **`generators.*`**: every public `MeshGenerator` with a parameterless constructor, with its defaults. Six of the 18 need
  input that the defaults do not have (a curve, a polygon: the two revolve generators, `PointSplatsGenerator`,
  `TriangulatedPolygonGenerator`, `TubeGenerator`, `VerticalGeneralizedCylinderGenerator`) and record only the exception
  type; the note of the case says so.
- **`boolean.open-winding`** (`CrossRuntime.cs`): a Boolean with an open input of more than 1000 boundary segments, the only case
  that reaches the hierarchical winding number, whose admission bound calls the platform `Math.Acos / Asin / Cos`.
- **`winding.values`**: raw `DMeshAABBTree3.FastWindingNumber` / `WindingNumber` values (they end in `Math.Atan2`) and the
  inside decisions made from them.
- **`probe.libm-constfold`** (a transcendental function of a constant against the same call on a value the JIT cannot see,
  in a method compiled with full optimization and in one compiled without),
  **`probe.linq-sum`** (LINQ `Sum / Average / Min / Max` against a loop), **`probe.round-remainder`** (`Math.Round` with digits and
  midpoint modes, `%`, `IEEERemainder`).

The header of a run has a line `# machine` (vector widths, the instruction sets the JIT uses, whether the library's `Vector256`
kernels are on); the workflow also runs the cases with `DOTNET_EnableHWIntrinsic=0`, `DOTNET_EnableAVX2=0`,
`DOTNET_TieredCompilation=0` and `DOTNET_JITMinOpts=1`, which turn one machine into several. `cross_matrix.py <traces>` prints the
matrix of the note (groups of cases against pairs of configurations) from the trace directories of a run.

`adversary` lists a case of `KnownOrderDependent` (`Program.cs`: a known finding, with its reference) as `KNOWN` and does not
fail on it; it fails when such a case stops differing, so the list only shrinks. The list is empty since the fix of issue 1314
(`LaplacianMeshSmoother`, `examples.Smoothing.Laplacian`).

`scope_report.py <build log>` summarizes the G3D diagnostics of a library build with more files in the proof scope (what a
stage of the determinism proof would have to register). The static counterpart of the cases is
[`tools/DeterminismReach`](../DeterminismReach/README.md).

## Stage 1 cases

`Stage1.cs` adds the cases `stage1.*` for the exact building blocks of the determinism proof stage 1
([DETERMINISM_PROOF.md](../../docs/design/DETERMINISM_PROOF.md) 5.4): `SnapRounding2` (value-tuple keys, whose hashes are seeded per
process on .NET; each case also runs the input with its segments reversed), `ExactTriangulation2` and `TriangleBoxTree` (the parallel
build, above `ParallelBuildFrom`, on random and tied boxes). `ExactPlanarSimplify` runs inside the `boolean.*` cases.

## Schedule adversary

`adversary` is the first piece of the environment adversary of
[DETERMINISM_PROOF.md](../../docs/design/DETERMINISM_PROOF.md) (option B). The library's parallel loops (`gParallel.ForRange`,
`ForEach`, `BlockStartEnd`) run serially under `gParallel.SerialOnThisThread`; `gParallel.SerialOrderOnThisThread` then
chooses the order of each loop's items: as given, backwards, or a seeded permutation, a new one for every loop. A result
that depends on the schedule (the order of appends to a shared list, ids handed out by a counter, a reduction) changes
with that order. The per-class parallel flags (`Remesher`, `DualContouring`, `MarchingCubes`) stay on, so that these
classes take their `gParallel` loops. Every case runs in the given order, backwards and under `--seeds` permutations, in one process; the
command prints the cases whose values change and exits with 1 if there is one. Two parallel runs sample the schedules
the machine happens to produce; this explores orders that no thread pool produces. Not covered: loops that call
`Parallel.For` directly (`PartitionedReduction`, `TerrainGridTin`, the 2D quadtrees, `SafeCollections`), and the
interleaving inside one item. The tests `OrderAdversaryTests` run the same check on more inputs in the PR tier.

## Probes

`probe.*` cases use no library code. They record runtime behaviour that the specification leaves open or that changed
between .NET Framework and .NET: `Math.Min / Max` on signed zeros and NaN (`probe.minmax-zero`), `double` to integer
conversions out of range (`probe.cast-range`), `System.Random` with a seed (`probe.random-seeded`), `GetHashCode` of key
types (`probe.hash-values`; a function of the key within one runtime, not promised equal across runtimes), the enumeration order of `Dictionary` / `HashSet` after inserts, removals and refills
(`probe.hash-enumeration`), the order of equal keys after the unstable sorts (`probe.sort-ties`, `probe.sort-ties-large`).
`probe.hash-seeded` (`BigInteger`, `ValueTuple`, `string`) **differs between any two runs on .NET**, on one machine too:
these hash codes are seeded per process. It is listed by every comparison of two .NET runs; that is its result.
`probe.bigint-double` and `probe.double-bigint` (`ProbesBigInteger.cs`) record the conversions between `BigInteger` and `double`
beyond 2^53 (ties, near-ties, sticky bits beyond the top 64 bits, the overflow boundary; issue 859) and count which rule the
runtime follows: truncation toward zero on .NET 10 and .NET Framework 4.8 alike, not round-to-nearest (run 43505530553).
`probe.slope-degrees` and `terrain.slope-design*` cover the terrain slope design, which called the platform libm until
#836 (issue 802) and must now be identical on every platform.
`coremath.acos` and `coremath.tan` run the correctly rounded `CoreMath.Acos` / `CoreMath.Tan` over 4096 random arguments and the edge arguments
(the ends of the acos domain, the neighbourhood of pi / 2 and of huge arguments); unlike the `libm.*` cases they must be identical on every platform.

## Kernel and allow list (net48 / net10.0 measurement)

`kernel.*`, `ledger.*` and `boolean.rounding.*` (`Kernel.cs`) are the measurement of
[DETERMINISM_PROOF.md](../../docs/design/DETERMINISM_PROOF.md) 5.3: the exact kernel's operations and the members of its allow
list (`DeterministicApis.txt`, the G3D0009 ledger) on procedural inputs from a splitmix64 stream, and Booleans whose rounding meets
exactly coplanar or near contacts.
- **`kernel.*`:** the predicates (double, adaptive, the exact stages), `ExactPoint3`, `FixedPoint3` / `FixedArith`, `ExactLimbSigns`,
  the triangle and ray predicates, the slope predicates, `ExactInterval` / `ExactDyadic`. The inputs are random,
  nearly degenerate (a point moved by a few ulps off the exact line or plane), small integers (exact degeneracies) and repeated points,
  with exponents from subnormal to huge. A predicate that throws on an input (an overflow to NaN) records the kind of exception as its
  result.
- **`ledger.*`:** the BCL members of the list on edge inputs: `Math.Sqrt / Abs / Sign / Min / Max / BigMul`, the bit conversions, the
  `BigInteger` operations and its conversions from and to `double`.
- **`boolean.rounding.*`:** two boxes sharing a face plane (a face contact, and overlapping coplanar faces), the second rotated by a
  3-4-5 angle, at the scales 2^-30, 1 and 2^30, with and without `SafeRounding`; a sphere whose vertices lie a few ulps off a box face.

The workflow runs them with every other case, and its `compare` job prints the table of the design note at the end of its log
(`measurement_rows.py`: one row per case, the hash and value count per runtime, and whether they agree; the step "Measurement rows").
Locally, from a directory of trace directories:

```sh
python3 tools/DeterminismHarness/measurement_rows.py <traces> > rows.md
python3 -m unittest discover -s tools/DeterminismHarness -p 'test_measurement_rows.py'
```
