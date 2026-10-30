# Quadtree benchmark

Procedural clean-room data from [QUADTREE2D_SPEC.md §4.2](../../docs/design/QUADTREE2D_SPEC.md):
seed 1, jittered grid, 7.07-unit cells, projected origin (2539000, 5640000), random heights,
two triangles per cell, shuffled. No mesh files or old quadtree implementation are used.

## Run

```powershell
dotnet run --project benchmarks\QuadtreeBenchmark -c Release -f net10.0 -- 64 256
dotnet run --project benchmarks\QuadtreeBenchmark -c Release -f net10.0 -- 708 10000 32
dotnet build benchmarks\QuadtreeBenchmark -c Release -f net48 -p:TestNet48=true
.\benchmarks\QuadtreeBenchmark\bin\Release\net48\QuadtreeBenchmark.exe 64 256
```

Arguments: cells per axis, number of indexed queries, number of brute-force queries (defaults to
all queries). The large run uses a smaller, explicitly reported baseline prefix to bound scan time.
All baseline-prefix rectangle/segment result sets, nearest IDs and zero-tolerance triangle heights are checked
before timing for the library index and both NTS indexes; sequential rectangle/segment/nearest results are also checked.
A mismatch fails the process. Heights are rounded constructions, not exact arithmetic results.

The CSV rows compare brute-force scans with the library quadtree and public NetTopologySuite STRtree/MX-CIF Quadtree.
When the baseline uses fewer queries, `quadtree_prefix`, `strtree_prefix` and `nts_quadtree_prefix` rows measure exactly
that same prefix and call count; the corresponding unsuffixed variants separately measure all indexed queries.
Only prefix rows are directly paired with the scan. Four query samples rotate all four contenders
through every timing position, after warmup. Construction rows measure one sequential core build,
one parallel core build, one triangle-adapter build, one explicitly materialized STRtree build and one incremental NTS Quadtree build. The parallel flag
may be ignored below the implementation's parallel-work threshold. Height measurements use zero
tolerance to make the independent baseline unambiguous; adapter tolerance correctness is tested separately.

`current_thread_allocated_bytes_per_call` counts only current-thread managed allocations, not parallel
worker allocations; parallel/triangle build rows must not be compared as total build allocations.
Historical raw outputs used the shorter `allocated_bytes_per_call` header for the same thread-only counter.
It is unavailable on .NET Framework. The approximate retained core-index size is a full-GC heap delta;
input AABBs and the sequential index stay alive across the measurement. It excludes source triangles
and triangle-adapter storage and is not a peak-memory measurement.

This is a local Stopwatch diagnostic following the repository's existing benchmark convention, not
BenchmarkDotNet statistical evidence. Small samples, JIT/tiering, GC, background load and different
baseline/query counts affect timing. The spec's final BenchmarkDotNet side-by-side comparison must
run in the owner's environment; neither the incompatible old code nor its measurements are available
here. Do not treat local results as proof of the owner's performance targets.

## Public STRtree comparison contract

[NetTopologySuite 2.6.0](https://www.nuget.org/packages/NetTopologySuite/2.6.0) is pinned only in the
benchmark project. Its permissive license and clean-room boundary are listed in [SOURCES.md](../../docs/SOURCES.md).
The adapter uses public `STRtree<int>` APIs with the default node capacity. Envelopes, inserts and
explicit `Build()` are all inside construction timing; a lazy first-query build is not hidden.

* Both indexes receive identical IDs, boxes, query arrays and immutable procedural triangles.
  The quadtree snapshots input geometry; the benchmark-only STRtree adapter retains the source arrays
  for filtering/distance/height, while NTS owns copied envelopes. Its build is an envelope-index build,
  not an equivalent full triangle-snapshot adapter build. Do not compare it directly with
  `triangle_adapter` as if both included identical preprocessing or supported mutations.
* Rectangles return closed envelope-overlap result sets. Segment candidates use an expanded segment
  envelope, then the same independent 0.0002-expanded-box double clipping as the scan.
* Nearest queries use NTS's public nearest-neighbour search, then a conservatively expanded square
  query to resolve squared-distance/ID ties. That extra pass is timed; raw NTS tie behavior is unspecified.
* Heights sort containing-envelope candidates by ID and use the scan's shared orientation/barycentric
  helper. The terrain has disjoint interiors and continuous heights on shared edges. The quadtree's
  first traversal hit need not have the lowest ID; overlapping triangles with different heights are
  therefore not an equivalent first-hit comparison. No height tolerance fallback is benchmarked.
* The shared segment clip and floating-point height construction are ordinary-coordinate benchmark
  oracles, not the quadtree's extreme-coordinate robust fallbacks. Similarity, reuse, closed-boundary,
  tie and CLI/result-check tests run in the existing geometry test project/CI. They do not establish
  general numerical equivalence with the library. The STRtree adapter is serial-only; the quadtree
  also supports nested/concurrent read-only queries and exclusive insert/remove, which are not timed here.
* Net10 allocation rows include NTS traversal and the timed nearest tie pass. The core heap delta
  still measures only the quadtree, not STRtree/full-adapter/peak memory. Build rows have no contender
  rotation; fresh-process ranges matter when interpreting them.

## Public MX-CIF Quadtree comparison contract

The same pinned BSD-licensed NTS package supplies `NetTopologySuite.Index.Quadtree.Quadtree<int>`.
`NtsQuadtreeComparison` calls only public APIs; no NTS implementation source was consulted or ported.
The library quadtree and its arithmetic are unchanged by this comparison.

* NTS uses an incrementally built MX-CIF quadtree, not a loose quadtree. Construction includes envelope
  allocation, every insert and the extent/spacing calculation used by the benchmark's nearest adapter.
  The original source arrays are retained rather than snapshotted, as in the STRtree adapter.
* NTS range queries are primary filters: ancestor items may be returned without intersecting the query,
  and zero-width envelopes may be padded by NTS. The visitor checks closed overlap against the original
  item AABB before producing results. This postfilter is part of rectangle, segment and height timings.
  Segments additionally use the same expanded-box clip as the scan and STRtree.
* NTS Quadtree has no native nearest-neighbour API. The benchmark first checks a point envelope, then
  doubles square-query radii until an actual item is found. The starting radius is the global maximum
  extent divided by the square root of the item count (one unit for zero extent). The first found items
  supply a distance upper bound; a final outward-rounded square includes nearer items and distance/ID ties.
  All passes and filtering are timed. This is a range-query-based adapter, not a native nearest comparison.
* Heights use the same sorted IDs and shared scan helper as STRtree. The same restrictions on overlapping
  heights, zero tolerance, ordinary finite coordinates, numerical robustness and serial reuse apply.
* Four samples now rotate scan, library quadtree, STRtree and NTS Quadtree through every position. Older
  recorded three-contender runs below remain unchanged and are not paired with this different runner.
  Build order remains fixed; the reported retained-core heap delta does not measure either NTS index.

### Direct public Quadtree results (2026-10-29)

The machine, procedural seed and large dataset are the same as the earlier local precheck: 708 cells per
axis, 1002528 triangles, 10000 timed queries and an independently checked 32-query prefix. Three fresh
processes per runtime ran sequentially, alternating net10/net48. Each process rotated all four contenders
across four query samples. No agent-started tests or builds ran during measurement. All processes exited
successfully with `validated_queries=32; checksum=38590960340`. Only the prefix was independently checked;
the checksum is not an independent validation of every timed query.

```powershell
dotnet .\benchmarks\QuadtreeBenchmark\bin\Release\net10.0\QuadtreeBenchmark.dll 708 10000 32
.\benchmarks\QuadtreeBenchmark\bin\Release\net48\QuadtreeBenchmark.exe 708 10000 32
```

Query medians [minimum, maximum] of twelve full-query samples, microseconds per call:

| Runtime | Operation | Library quadtree | NTS STRtree | NTS MX-CIF Quadtree |
|---|---|---:|---:|---:|
| net10.0 | rectangle | 12.983 [10.208, 29.666] | 9.375 [7.129, 38.296] | 202.751 [125.277, 569.040] |
| net10.0 | segment | 25.853 [20.708, 48.015] | 45.945 [35.172, 121.531] | 232.658 [151.683, 638.492] |
| net10.0 | nearest | 7.132 [6.413, 16.426] | 18.901 [14.089, 99.443] | 373.370 [213.567, 1144.945] |
| net10.0 | height | 3.693 [2.934, 11.134] | 5.163 [3.430, 34.633] | 171.190 [117.854, 518.975] |
| net48 | rectangle | 21.493 [15.534, 47.051] | 18.124 [11.098, 52.128] | 878.813 [623.103, 1926.090] |
| net48 | segment | 55.872 [43.174, 116.143] | 71.928 [52.316, 193.680] | 1045.556 [626.291, 2138.369] |
| net48 | nearest | 15.221 [10.920, 35.224] | 55.795 [23.347, 141.605] | 1630.930 [1008.255, 4484.786] |
| net48 | height | 6.809 [5.143, 18.776] | 10.505 [6.310, 98.802] | 807.688 [590.077, 1881.836] |

Build medians [minimum, maximum] of three fresh processes, milliseconds per build:

| Runtime | Sequential core | Parallel core | Triangle adapter | NTS STRtree | NTS Quadtree |
|---|---:|---:|---:|---:|---:|
| net10.0 | 765.538 [748.214, 827.860] | 392.471 [385.806, 425.276] | 440.374 [352.989, 509.610] | 3015.469 [2525.896, 3146.283] | 1400.308 [1191.859, 2118.939] |
| net48 | 1072.366 [798.048, 2149.527] | 524.750 [448.698, 1064.289] | 1210.627 [724.264, 1309.791] | 5024.848 [4286.724, 9721.086] | 2300.359 [1760.076, 3358.122] |

The current-thread allocation ranges for net10 full-query samples, bytes per call:

| Operation | Library quadtree | NTS STRtree | NTS Quadtree |
|---|---:|---:|---:|
| rectangle | 0.000 | 0.000 | 438.336 |
| segment | 0.000 | 0.000 | 976.820 |
| nearest | 0.000 | 8090.367-8090.368 | 479.840-551.848 |
| height | 0.000 | 0.000 | 239.920 |

These counters include the adapter and NTS traversal; their causes were not isolated. Net48 counters
are unavailable. Retained **library core** bytes were 52702168 [52701160, 52704176] on net10 and
52697552 [52696696, 52698312] on net48; neither NTS index nor full triangle-adapter/peak memory is included.

For this dataset and adapter contract the library's build/query medians are below the NTS MX-CIF medians.
This does not establish superiority over other quadtrees, statistical significance, general numerical
robustness or owner performance acceptance. STRtree has the lower rectangle median in this round.
Absolute timings differ substantially from the earlier round despite byte-identical library sources;
the different runner and broad fresh-process ranges prohibit interpreting this as another optimization gain.
Nearest rows include the benchmark-owned range-search implementation for NTS Quadtree, not a native API.

Raw process records:
[net10 run 1](perf/20261029-nts-quadtree-net10.0-run1.txt),
[net10 run 2](perf/20261029-nts-quadtree-net10.0-run2.txt),
[net10 run 3](perf/20261029-nts-quadtree-net10.0-run3.txt),
[net48 run 1](perf/20261029-nts-quadtree-net48-run1.txt),
[net48 run 2](perf/20261029-nts-quadtree-net48-run2.txt),
[net48 run 3](perf/20261029-nts-quadtree-net48-run3.txt).
[Command/environment/source/binary manifest](perf/20261029-nts-quadtree-manifest.json) and
[aggregate timing/allocation/heap data](perf/20261029-nts-quadtree-summary.json) preserve the comparison evidence.

### MX-CIF comparator validation

This is a Tier-M benchmark-only follow-up. Acceptance is identical checked-prefix result sets, stable
nearest IDs, matching zero-tolerance scan heights and measured side-by-side results. Non-goals are
library/API changes, new exact arithmetic, concurrent NTS queries, mutation benchmarks and owner-only acceptance.

```powershell
dotnet build benchmarks\QuadtreeBenchmark -c Release --no-restore -t:Rebuild --verbosity minimal
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --no-build --no-restore --filter 'FullyQualifiedName~Quadtree|FullyQualifiedName~StrtreeComparisonTests' --verbosity minimal
dotnet test tests\geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true --no-restore --filter 'FullyQualifiedName~Quadtree|FullyQualifiedName~StrtreeComparisonTests' --verbosity minimal
```

The repeated net10 selection passed 99 tests and the net48 selection passed 96. The new comparator tests
cover procedural scan/library agreement, `Similarity.All()` with bit-identical mapped power-of-two heights,
serial buffer reuse, repeated builds and nearest ties, empty/far queries, ancestor false positives,
zero-extent padding, closed/tolerance segment hits, degenerate/overlapping heights and repeatable CLI checksums.
Removing the original-item postfilter failed `AncestorFalsePositivesAndPaddedPointBoundsAreFiltered`;
inverting the nearest-ID tie comparison failed `ExpandingNearestSearchHandlesEmptyFarAndTiedInputsDeterministically`.
The corresponding mutation commands used the same net10 test command without `--no-build`, selecting each
named test. Source bytes were restored in `finally`, followed by the forced rebuild above. The independent
scoped review reported no blocking findings.

The first extended net10 run (the same selection, without `--no-build`) failed
`WarmHotQueriesAllocateNoManagedMemory`: its counter reported 8160 bytes instead of zero. The isolated
command below passed, then the unchanged selection passed. The cause is not established; no test was
weakened, and these passes do not erase the initial failure or establish a green full CI.

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --no-build --no-restore --filter 'FullyQualifiedName~LooseQuadtree2dTests.WarmHotQueriesAllocateNoManagedMemory' --verbosity minimal
```

## Allocation investigation and fixture follow-up (2026-10-30)

The historical hot-query allocation failure could not be reproduced: the unchanged original test,
repeated original Spatial selections and per-operation probes passed. Neither a library allocation
nor a CLR/test-runner root cause was established. No production workaround was introduced, and the
original exact zero-byte assertion remains unchanged.

Additional dedicated-thread tests measure each core hot operation independently at small and large
index sizes, including generic geometry-distance callbacks. A positive allocation control and worker
exception propagation test validate the measurement helper. A temporary result-buffer growth mutation
was detected; production source was restored and rebuilt before final validation. Commands, observed
outputs and the standalone counter/GC-pressure reproducer are recorded in
[the allocation investigation](perf/20261030-allocation-investigation.txt). These checks strengthen
regression coverage; they do not prove that the historical failure is fixed or erase the earlier
non-zero benchmark sample.

The unrelated full-suite prism assertion was addressed by importing the deterministic rounded fixtures
and matching QA description from commit `e559f6870d9cf5202f7f7da85ac48b4179b50ed7`
(`qa/differential-core-tests`), without importing that branch's other changes. The known-void assertions
remain intact, the other observed rounding has its own clean-solid test, and exact production code is
unchanged. The earlier red CI records remain historical evidence, not the result of the corrected final
state. The neighboring prism cases passed on both test frameworks.

Final local CI commands, observed output and exit codes are recorded in
[the final CI record](perf/20261030-final-ci.txt):

* Full net10: 2,928 passed, one failure in `RobustnessSweepTests.Sweep_NonFiniteInput`
  (`LaplacianMeshSmoother`, `NaN vertex`) at its existing 20-second budget. Its isolated rerun passed.
  This does not establish a root cause, baseline attribution or a green complete CI run.
* Full net48: 2,918 passed, no failures or skips. The quadtree and imported prism cases passed in both
  complete runs, including the unchanged original allocation assertion on net10.
* CLI: 43 passed; pack/API compatibility, package smoke, net48 build and all benchmark builds exited zero.
* The final independent diff-review attempt failed before producing a review because its model provider
  returned HTTP 502. Earlier scoped reviews are not presented as a completed final review.

The remaining gates are a green full net10 run with the timing failure addressed, the independent final
review, and owner-machine timing/side-by-side and full-adapter memory acceptance. The owner authorized
publishing this work as a draft PR with the CI timing failure and missing final review explicitly open;
this is not authorization to mark it ready or merge.

Before publication, the public-quadtree manifest's console excerpts were reduced to plain strings,
removing accidentally serialized PowerShell provider/reflection metadata. The six corresponding raw
console records were losslessly converted from UTF-16 to UTF-8 for text review. Measurement values,
commands and historical source/binary hashes were preserved; no benchmarks were rerun or relabelled.

## Query optimization and public comparison (2026-10-29)

The retained change filters exact height candidates by their closed item AABB before orientation tests
and removes a duplicate degeneracy check. Tolerance fallback and robust arithmetic are unchanged.
The core source is byte-identical to the pre-optimization source; a loose-region node cache was rejected.

Same machine and procedural dataset as the precheck below: 708 cells per axis, 1002528 triangles,
10000 indexed queries and a 32-query independently checked scan prefix. Each table uses three fresh
processes per phase/runtime and three query samples per process. Before/after process order alternated
(before/after, after/before, before/after). No agent-started builds or tests ran during measurements.
The paired A/B used one frozen pre-STRtree runner, replacing only its library DLL. The separate public
comparison used the current runner, with all three contenders rotating through timing positions.

Commands executed for each frozen before/after snapshot and current comparison runner:

```powershell
dotnet "$snapshot\QuadtreeBenchmark.dll" 708 10000 32
& "$snapshot\QuadtreeBenchmark.exe" 708 10000 32
dotnet .\benchmarks\QuadtreeBenchmark\bin\Release\net10.0\QuadtreeBenchmark.dll 708 10000 32
.\benchmarks\QuadtreeBenchmark\bin\Release\net48\QuadtreeBenchmark.exe 708 10000 32
```

The first apparent height-only net10 build was stale after source rollback: its DLL hash still matched
the rejected cache candidate, and its retained heap reflected that cache. Those records were excluded
and renamed `20261029-stale-binary-*`. The following forced rebuild produced a different verified DLL;
only the affected net10 paired/public measurements were repeated. Valid net48 measurements were retained.
[Source/binary identities](perf/20261029-query-binary-manifest.json) distinguish the candidates.

```powershell
dotnet build benchmarks\QuadtreeBenchmark -c Release --no-restore -t:Rebuild --verbosity minimal
Get-FileHash -Algorithm SHA256 "$snapshot\geometry3Sharp.dll"
```

### Retained height-only change: paired A/B

Median [minimum, maximum] of nine full-query samples, microseconds per call:

| Operation | net10 before | net10 after | net48 before | net48 after |
|---|---:|---:|---:|---:|
| Rectangle | 23.917 [18.479, 30.858] | 28.115 [21.811, 40.633] | 40.845 [34.789, 56.209] | 43.535 [35.141, 56.444] |
| Segment | 46.785 [37.715, 64.998] | 53.082 [42.535, 93.132] | 127.567 [91.801, 172.227] | 121.393 [88.905, 180.827] |
| Nearest | 13.854 [9.641, 28.019] | 16.026 [10.863, 32.736] | 32.568 [24.283, 56.658] | 40.086 [31.126, 48.635] |
| Height | 14.406 [9.751, 17.294] | 7.860 [5.645, 12.676] | 21.510 [16.253, 27.540] | 14.239 [11.110, 25.244] |

Only height-query code changed. Its median improved on both runtimes, but overlapping ranges are not a
statistical confidence interval. Other query medians moved in both directions, including slower medians;
these measurements do not establish either a speedup or absence of regression in those unchanged paths.
Absolute values vary substantially from the earlier rounds. Further isolated measurements remain useful.

Build median [minimum, maximum] of three processes, milliseconds (no construction code changed):

| Build | net10 before | net10 after | net48 before | net48 after |
|---|---:|---:|---:|---:|
| Core sequential | 1291.478 [929.748, 1353.550] | 1327.757 [958.761, 1368.086] | 1954.463 [1625.745, 2264.998] | 2301.166 [1832.035, 2853.964] |
| Core parallel | 719.309 [624.774, 769.175] | 694.227 [570.864, 925.656] | 1136.006 [870.793, 1232.345] | 1430.268 [1019.975, 1535.193] |
| Triangle adapter | 714.733 [697.708, 850.174] | 835.835 [814.782, 1123.202] | 1360.642 [996.715, 1613.135] | 1570.683 [1536.149, 1995.918] |

Retained core heap delta median [minimum, maximum] of three processes, bytes:

| Runtime | Before | After |
|---|---:|---:|
| net10 | 52703320 [52702048, 52705232] | 52702112 [52700800, 52706144] |
| net48 | 52697456 [52695936, 52698976] | 52697456 [52696696, 52698216] |

All paired indexed-query allocation samples reported `0.000` current-thread bytes/call on net10.
These are normal-coordinate workload observations, not a universal allocation guarantee for robust fallbacks.

Paired raw outputs:
- net10 before: [run 1](perf/20261029-height-only-before-net10.0-run1.txt), [run 2](perf/20261029-height-only-before-net10.0-run2.txt), [run 3](perf/20261029-height-only-before-net10.0-run3.txt).
- net10 after: [run 1](perf/20261029-height-only-after-net10.0-run1.txt), [run 2](perf/20261029-height-only-after-net10.0-run2.txt), [run 3](perf/20261029-height-only-after-net10.0-run3.txt).
- net48 before: [run 1](perf/20261029-height-only-before-net48-run1.txt), [run 2](perf/20261029-height-only-before-net48-run2.txt), [run 3](perf/20261029-height-only-before-net48-run3.txt).
- net48 after: [run 1](perf/20261029-height-only-after-net48-run1.txt), [run 2](perf/20261029-height-only-after-net48-run2.txt), [run 3](perf/20261029-height-only-after-net48-run3.txt).

All paired processes ended with `validated_queries=32; checksum=9669856797`. The other indexed queries
were timed, not independently validated in those processes.

### Public STRtree with the retained implementation

Median [minimum, maximum] of nine full-query samples, microseconds per call. Both columns include their
adapter filtering/tie work; the public comparison contract above is essential to interpretation.

| Operation | net10 quadtree | net10 STRtree | net48 quadtree | net48 STRtree |
|---|---:|---:|---:|---:|
| Rectangle | 29.100 [22.765, 44.776] | 27.002 [20.262, 39.154] | 40.209 [26.054, 71.872] | 38.306 [21.758, 40.034] |
| Segment | 53.982 [45.346, 69.084] | 96.118 [81.921, 117.465] | 99.129 [79.612, 143.664] | 115.654 [84.672, 146.471] |
| Nearest | 15.260 [13.062, 18.556] | 45.621 [37.548, 82.899] | 30.468 [22.718, 44.419] | 53.770 [38.395, 62.468] |
| Height | 6.914 [5.874, 20.268] | 11.680 [10.403, 19.070] | 14.840 [11.311, 18.484] | 15.632 [13.117, 20.671] |

Rectangle medians are similar; other quadtree medians are lower in this workload, with particularly
wide/overlapping net48 segment/height ranges. This is not evidence of general index superiority.

Build median [minimum, maximum] of three processes, milliseconds:

| Build | net10 | net48 |
|---|---:|---:|
| Core sequential | 1283.126 [1115.287, 2194.147] | 1813.855 [1477.263, 2140.160] |
| Core parallel | 770.688 [732.898, 1217.598] | 815.921 [659.263, 1068.484] |
| Triangle adapter | 797.308 [796.538, 1067.077] | 1193.651 [963.062, 1332.891] |
| STRtree envelopes + inserts + Build | 5624.940 [5346.995, 6435.836] | 7581.421 [6752.188, 7603.377] |

The core build median is lower here, but the STRtree build is not equivalent triangle-snapshot
preprocessing, and build timings have no contender rotation. No private-index comparison is implied.
Public net10 nearest samples reported 8090.367–8090.388 current-thread bytes/call for STRtree including
its timed tie-resolution pass. Other public query samples reported zero except one quadtree rectangle
sample reporting 0.619; that small allocation observation was not isolated or attributed to library code.
Do not claim all public quadtree samples were allocation-free. Net48 allocation counters are unavailable.

Public raw outputs: net10 [run 1](perf/20261029-strtree-final-net10.0-run1.txt), [run 2](perf/20261029-strtree-final-net10.0-run2.txt),
[run 3](perf/20261029-strtree-final-net10.0-run3.txt); net48 [run 1](perf/20261029-strtree-final-net48-run1.txt),
[run 2](perf/20261029-strtree-final-net48-run2.txt), [run 3](perf/20261029-strtree-final-net48-run3.txt).
All six ended with `validated_queries=32; checksum=19306538526` after checking both public indexes on
that prefix. Larger query sets are only timed.

### Rejected candidate and reproduction

The earlier cached-node candidate increased retained core medians from 52705120 to 61092904 bytes on
net10 and from 52697552 to 61086064 bytes on net48. These are the first-round three-process medians,
not the retained change above. Its other query/build effects were inconsistent, so the cache was removed.
First-round files use `20261029-query-before-*`, `20261029-query-after-*` and `20261029-strtree-*`
without `final`: [net10 before](perf/20261029-query-before-net10.0-run1.txt),
[net10 candidate](perf/20261029-query-after-net10.0-run1.txt), [net48 before](perf/20261029-query-before-net48-run1.txt),
[net48 candidate](perf/20261029-query-after-net48-run1.txt); companion runs are in [perf](perf/).
The same snapshot commands above produced them. Neither they nor the stale-binary records are final results.

The [height-only source patch](perf/20261029-height-query.patch) reconstructs the pre-optimization library.
Use a separate complete source copy including the currently uncommitted additions, not a worktree from
an older HEAD that lacks the quadtree. Do not reverse the patch in the active checkout. In that copy:

```powershell
Set-Location $baselineCopy
git apply --reverse --check benchmarks\QuadtreeBenchmark\perf\20261029-height-query.patch
git apply --reverse benchmarks\QuadtreeBenchmark\perf\20261029-height-query.patch
dotnet build geometry3Sharp_netstandard.csproj -c Release --no-restore -t:Rebuild
```

For a new experiment keep one current runner binary fixed and swap only its library DLL; alternate
before/after order across fresh processes. That new runner also emits STRtree rows, unlike the frozen
runner used for the recorded paired A/B. It is a new measurement, not a literal replay of the old rows.
Owner-machine BenchmarkDotNet acceptance, further tuning and a green complete CI run remain open.

Historical records below predate the STRtree integration and retain their original scan/index runner order.

## Local performance precheck (2026-10-29)

Validation-only follow-up: no implementation tuning, no old-index inspection, no owner-machine acceptance.
Machine: Intel Core i7-8700 @ 3.20 GHz, 6 cores / 12 logical processors, Windows 11 Pro
10.0.26300; SDK 10.0.401, runtimes 10.0.12 and 4.0.30319.42000. Machine information was obtained with
`Get-CimInstance Win32_Processor`, `Get-CimInstance Win32_OperatingSystem` and `dotnet --info`.

After all local CI builds/tests finished, run three fresh processes per runtime, sequentially:

```powershell
1..3 | ForEach-Object { dotnet .\benchmarks\QuadtreeBenchmark\bin\Release\net10.0\QuadtreeBenchmark.dll 708 10000 32 }
dotnet build benchmarks\QuadtreeBenchmark -c Release -f net48 -p:TestNet48=true
1..3 | ForEach-Object { .\benchmarks\QuadtreeBenchmark\bin\Release\net48\QuadtreeBenchmark.exe 708 10000 32 }
```

Raw outputs: net10 [run 1](perf/20261029-net10-run1.txt), [run 2](perf/20261029-net10-run2.txt),
[run 3](perf/20261029-net10-run3.txt); net48 [run 1](perf/20261029-net48-run1.txt),
[run 2](perf/20261029-net48-run2.txt), [run 3](perf/20261029-net48-run3.txt).
Each run reported `items=1002528`, `validated_queries=32`, `checksum=9669856797`.
All independent baseline-prefix correctness checks passed. Queries outside that prefix are timed,
not independently validated by this benchmark.

Each process records three warmed query samples. The following are medians of the nine samples;
scan and index both use the same 32-query prefix (microseconds per call). No old-index speedup is implied.

| Operation | net10 scan (before) | net10 index (after) | net48 scan (before) | net48 index (after) |
|---|---:|---:|---:|---:|
| Rectangle | 7641.613 | 16.747 | 6661.728 | 20.391 |
| Segment | 10396.072 | 30.944 | 16727.566 | 50.319 |
| Nearest | 9227.538 | 8.466 | 14029.231 | 19.450 |
| Height | 12072.075 | 9.844 | 31955.116 | 12.219 |

For orientation only, the full 10000-query samples are compared with the spec's owner-machine targets.
Cells are formatted as median [minimum, maximum], in microseconds per call. These are different
hardware/workload samples from the owner's measurements, not pass/fail acceptance thresholds here.

| Operation | Spec target | net10 index | net48 index |
|---|---:|---:|---:|
| Rectangle | ~5.7 | 16.016 [10.692, 38.051] | 19.310 [14.952, 31.126] |
| Segment | ~21 | 31.978 [20.588, 76.762] | 44.395 [37.103, 93.358] |
| Height | ~2.5 | 6.363 [3.649, 15.553] | 9.687 [6.902, 17.086] |

Construction has one observation per process (three total). Milliseconds, median [minimum, maximum]:

| Construction | net10 | net48 |
|---|---:|---:|
| Sequential core | 586.090 [495.219, 631.126] | 1232.656 [761.083, 1364.054] |
| Parallel core | 251.867 [246.489, 277.430] | 510.363 [427.291, 609.927] |
| Triangle adapter | 289.425 [277.355, 768.870] | 644.296 [547.617, 813.375] |

The spec's ~100–260 ms triangle-build target must not be compared only with core AABB construction:
triangle-adapter construction includes snapshot/bounds work. Its median and the query medians remain
above the owner reference values on this host; tuning and a controlled owner-machine comparison are still needed.

Retained core-index heap-delta medians were 52705120 bytes (net10) and 52697488 bytes (net48).
This excludes triangle snapshots/adapter storage, so it does not certify the spec's full triangle-index
~67 MB requirement. Current-thread managed query allocations were 0 bytes/call in all net10 samples;
net48 has no allocation counter here. Robust fallback allocations and parallel worker allocations are not
certified by this ordinary-coordinate workload. Shared-host sample variability is substantial; these
Stopwatch observations are a preliminary diagnostic, not BenchmarkDotNet acceptance evidence.

## Paired-prefix observations after numerical review fixes

Commands on the shared Windows development machine:

```powershell
dotnet run --project benchmarks\QuadtreeBenchmark -c Release -f net10.0 -- 708 10000 32
dotnet build benchmarks\QuadtreeBenchmark -c Release -f net48 -p:TestNet48=true --verbosity quiet
.\benchmarks\QuadtreeBenchmark\bin\Release\net48\QuadtreeBenchmark.exe 708 10000 32
```

Both runs reported `items=1002528`, `validated_queries=32`, `checksum=9669856797`;
all independent baseline checks passed. Runtime versions were `10.0.12` and `4.0.30319.42000`.
The following measurements pair the same 32-query prefix (microseconds per call).

| Sample | Operation | net10 scan (before) | net10 index (after) | net48 scan (before) | net48 index (after) |
|---|---|---:|---:|---:|---:|
| 0 | Rectangle | 5603.975 | 17.731 | 5458.906 | 23.956 |
| 0 | Segment | 6484.387 | 36.516 | 13986.078 | 52.041 |
| 0 | Nearest | 6364.856 | 21.531 | 11045.556 | 21.719 |
| 0 | Height | 9974.503 | 21.228 | 25134.400 | 12.575 |
| 1 | Rectangle | 4670.444 | 11.791 | 5316.269 | 21.722 |
| 1 | Segment | 5735.966 | 20.519 | 13414.881 | 55.722 |
| 1 | Nearest | 6327.578 | 4.956 | 10989.978 | 10.172 |
| 1 | Height | 7272.544 | 5.828 | 23518.544 | 8.803 |
| 2 | Rectangle | 4666.275 | 16.559 | 5298.322 | 16.834 |
| 2 | Segment | 5068.922 | 31.259 | 12822.513 | 39.972 |
| 2 | Nearest | 6641.788 | 9.291 | 11410.003 | 8.919 |
| 2 | Height | 7229.553 | 10.056 | 23501.825 | 11.238 |

| Construction | net10 microseconds | net48 microseconds |
|---|---:|---:|
| Sequential core | 615812.800 | 895931.000 |
| Parallel core | 410251.200 | 534585.800 |
| Triangle adapter | 415591.500 | 530590.500 |

Current-thread query allocation measurements were `0.000` bytes/call on net10; unavailable on net48.
Retained core-index heap deltas were `52702048` and `52699072` bytes respectively. These observations
remain diagnostic, not owner-machine performance acceptance or a comparison against the forbidden old index.
The net48 `--no-restore` attempt failed with NETSDK1005 after the net10 run replaced its assets;
the explicit build/restore command above succeeded.

## Historical first-pass output (October 2026)

Before the subsequent numerical review fixes and paired-prefix timing. Windows shared development machine. Command:

```powershell
dotnet run --project benchmarks\QuadtreeBenchmark -c Release -f net10.0 -- 708 10000 32
```

Raw benchmark output (compiler warnings omitted):

```text
runtime=10.0.12; processors=12; cells=708; items=1002528; queries=10000; baseline_queries=32
operation,variant,calls,microseconds_per_call,allocated_bytes_per_call
build,sequential,1,778951.100,65267328.000
build,parallel,1,483242.700,71574416.000
index_retained_bytes=52702112
build,triangle_adapter,1,460082.500,175823728.000
sample=0
rectangle,brute_force,32,6217.669,0.000
segment,brute_force,32,9419.272,0.000
nearest,brute_force,32,9192.234,0.000
height,brute_force,32,14716.278,0.000
rectangle,quadtree,10000,15.176,0.000
segment,quadtree,10000,26.022,0.000
nearest,quadtree,10000,9.003,0.000
height,quadtree,10000,5.524,0.000
sample=1
rectangle,quadtree,10000,17.074,0.000
segment,quadtree,10000,31.443,0.000
nearest,quadtree,10000,9.982,0.000
height,quadtree,10000,11.360,0.000
rectangle,brute_force,32,14559.278,0.000
segment,brute_force,32,8853.516,0.000
nearest,brute_force,32,9578.466,0.000
height,brute_force,32,11819.359,0.000
sample=2
rectangle,brute_force,32,6391.134,0.000
segment,brute_force,32,15492.819,0.000
nearest,brute_force,32,16047.100,0.000
height,brute_force,32,16629.838,0.000
rectangle,quadtree,10000,15.536,0.000
segment,quadtree,10000,25.061,0.000
nearest,quadtree,10000,6.778,0.000
height,quadtree,10000,5.049,0.000
validated_queries=32; checksum=9636681729
```

The baseline column is the before implementation (linear scan), the quadtree column the after
implementation. Different query counts and the shared host's large sample variability prevent a
controlled speedup claim. The owner's build/height/rectangle/segment targets have **not** been met
or certified on a comparable machine; optimization and the owner-only comparison remain open.

Framework execution was also checked:

```powershell
dotnet build benchmarks\QuadtreeBenchmark -c Release -p:TestNet48=true
.\benchmarks\QuadtreeBenchmark\bin\Release\net48\QuadtreeBenchmark.exe 64 256
```

Output: build succeeded; `runtime=4.0.30319.42000`, `items=8192`,
`validated_queries=256; checksum=4343814`. Sample 2, microseconds per call:

| Operation | Before: brute force | After: quadtree |
|---|---:|---:|
| Rectangle | 52.712 | 8.127 |
| Segment | 186.834 | 25.446 |
| Nearest | 123.439 | 7.824 |
| Height | 250.960 | 7.209 |

Framework allocations are unavailable. These are diagnostic observations, not acceptance thresholds.
