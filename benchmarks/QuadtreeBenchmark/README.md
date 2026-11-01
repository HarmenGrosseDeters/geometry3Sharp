# Quadtree benchmark

Procedural data from [QUADTREE2D_SPEC.md §4.2](../../docs/design/QUADTREE2D_SPEC.md): seed 1, jittered grid, 7.07-unit cells, projected origin (2539000, 5640000), random heights, two triangles per cell, shuffled. No mesh files, and no old quadtree implementation.

## Run

```powershell
dotnet run --project benchmarks\QuadtreeBenchmark -c Release -f net10.0 -- 64 256
dotnet run --project benchmarks\QuadtreeBenchmark -c Release -f net10.0 -- 708 10000 32
dotnet build benchmarks\QuadtreeBenchmark -c Release -f net48 -p:TestNet48=true
.\benchmarks\QuadtreeBenchmark\bin\Release\net48\QuadtreeBenchmark.exe 708 10000 32
```

Arguments: cells per axis, indexed queries, brute-force queries (default: all). The large run checks a prefix of 32 queries against the scan, as a scan of 1M items is slow.

For that prefix, the rectangle and segment result sets, nearest IDs and zero-tolerance heights of the library index and both NetTopologySuite indexes are compared with the scan, and a mismatch fails the process. Queries beyond the prefix are timed, not checked. Correctness at scale is covered by the tests in `tests/geometry3Sharp.Tests/Spatial`.

It prints CSV: one row per contender and operation. `triangle_over` builds without a copy of the triangles, and `parallel_far_point` builds the same boxes plus one box 1e6 away in x and y. `_prefix` rows measure the same queries as the scan and are the only rows directly comparable with it. Four query samples rotate the contenders through every timing position.

## What is compared

- **Scan:** independent brute force.
- **NetTopologySuite 2.6.0** (BSD-3-Clause, benchmark and test only, see [SOURCES.md](../../docs/SOURCES.md)): `STRtree` and the MX-CIF `Quadtree`, through public APIs.
- The NetTopologySuite builds include all envelopes and inserts, and `STRtree` an explicit `Build()`. They keep the source arrays, so they are not equivalent to `TriangleQuadtree2d`, which copies its triangles, unless built with `TriangleQuadtree2d.Over`.
- NetTopologySuite has no tie rule for nearest items, and its `Quadtree` has no nearest query: both are built from range queries here, and that time counts. So its nearest rows do not measure a native operation.
- Its range queries are primary filters: results are filtered afterwards, and that time counts.
- Heights have zero tolerance and use one shared helper, on a terrain with disjoint interiors. Row `height_tolerance` is the library's only: points 0.0005 outside the boundary of that terrain, at the default tolerance, so each pays for the search for an exact hit first.

## How to read the numbers

This is a Stopwatch diagnostic, not BenchmarkDotNet evidence. Run it alone on the machine. Allocation is per current thread: it misses parallel workers, and .NET Framework has no counter. `index_retained_bytes` is a heap delta of the core only: no triangle storage, and not a peak.

The targets of the spec's §4.2 were measured by the owner on the old index and their own hardware, so a run here says nothing against them. That comparison runs on the owner's machine (§5).

## Cost of the structure

Counted rather than timed, so exact. The two rows on the 1M grid are a one-off count on the benchmark's grid; `QuadtreeReviewRegressionTests` holds the others and a limit for point queries on a smaller grid. What one small query looks at:

| Data | Before | Now |
|---|---:|---:|
| Point query on the 1M triangle grid, regions and items tested | 110 and 35 | 39 and 6 |
| Rectangle 20 x 20 on the same grid, regions and items tested (30 hits) | 137 and 96 | 62 and 41 |
| One row of a TIN, extent 100000 x 1, items of 100000 tested | 100000 | below 64 |
| 20 km x 20 km, one point at (1e15, 1e15), items of 100000 tested | 100000 | at most 8 times the count without the point |

Inserting points in sorted order into an empty tree, on the development machine: 40000 points took 90 s, when every insert beyond the extent rebuilt the tree. `InsertingInSortedOrder_DoesNotRebuildEachTime` now requires 20000 points on a line and 20000 on a diagonal in under 5 s.

## Ideas not pursued

None of these is measured, apart from the first. Each would have to show its gain here first.

- Reusing the build arrays between builds. Measured on .NET 10, 30 builds of `Over` one after another: about 80 ms each with a full collection most times, against 75 to 79 ms after a forced one. So about 5 ms a build, and a static buffer would hold on to the memory.
- Storing the items in tree order, so that a query reads neighbouring memory. Queries would touch fewer cache lines, and removal gets harder.
- A build by Morton order instead of cutting from the top. It may cost less on uniform data, and loose placement of large items does not map onto it directly.
- Tuning the looseness. The present value was not searched for.
- `Over` for other forms of input, such as vertices with index triples. Only `Triangle3d[]` exists so far.

## Results

`708 10000 8` on an i7-8700 with 12 logical processors, alone on the machine: 1,002,528 triangles, medians over 3 processes (of all samples, for queries). Earlier records of the abandoned sessions were dropped: they described older code and were never re-measured.

Each build runs once per process, so `sequential`, the first, also pays for the code not being optimized yet. On .NET 10 it took about 260 ms in a warmed process, and the others changed little.

| Build, ms | .NET 10 | .NET Framework 4.8 |
|---|---:|---:|
| `sequential` | 370 | 343 |
| `parallel` | 91 | 92 |
| `parallel_far_point` | 135 | 152 |
| `triangle_adapter` | 96 | 101 |
| `triangle_over` | 80 | 91 |
| `strtree` | 1494 | 2130 |
| `nts_quadtree` | 706 | 734 |

| Query, us per call | Library .NET 10 | `strtree` .NET 10 | Library 4.8 | `strtree` 4.8 |
|---|---:|---:|---:|---:|
| Height, zero tolerance | 0.82 | 2.78 | 0.90 | 4.21 |
| Height beside the boundary, default tolerance | 0.77 | | 1.14 | |
| Rectangle 20 x 20 | 3.63 | 6.17 | 4.09 | 9.18 |
| Segment | 7.58 | 29.44 | 12.04 | 36.46 |
| Nearest | 1.19 | 11.35 | 1.41 | 15.94 |

A cell splits above 8 items, or above 16 if they fill at most a quarter of the cell in both directions, as cutting such a cluster adds a chain of nodes before it separates them. Inserts try to split at 9, 17 and each power of two, so a cell can hold more until the next of these. Measured at 4, 6, 8, 12, 16, 24 and 32: 8 is the best for queries, 4 and 6 gain nothing and cost build time (4 also memory), and against 16 it is about 30 % faster in rectangle queries on .NET 10 and 25 % on 4.8, 37 % and 26 % in heights, for about 5 % more build time.

`index_retained_bytes` of the core: 64.7 MB on both runtimes, with no triangles. The triangles add 72 bytes each in `TriangleQuadtree2d`, and nothing in `Over`, which reads the caller's array.
