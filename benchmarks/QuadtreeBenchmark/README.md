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

It prints CSV: one row per contender and operation. `_prefix` rows measure the same queries as the scan and are the only rows directly comparable with it. Four query samples rotate the contenders through every timing position.

## What is compared

- **Scan:** independent brute force.
- **NetTopologySuite 2.6.0** (BSD-3-Clause, benchmark and test only, see [SOURCES.md](../../docs/SOURCES.md)): `STRtree` and the MX-CIF `Quadtree`, through public APIs.
- The NetTopologySuite builds include all envelopes and inserts, and `STRtree` an explicit `Build()`. They keep the source arrays, so they are not equivalent to `TriangleQuadtree2d`, which copies its triangles.
- NetTopologySuite has no tie rule for nearest items, and its `Quadtree` has no nearest query: both are built from range queries here, and that time counts. So its nearest rows do not measure a native operation.
- Its range queries are primary filters: results are filtered afterwards, and that time counts.
- Heights have zero tolerance and use one shared helper, on a terrain with disjoint interiors.

## How to read the numbers

This is a Stopwatch diagnostic, not BenchmarkDotNet evidence. Run it alone on the machine. Allocation is per current thread: it misses parallel workers, and .NET Framework has no counter. `index_retained_bytes` is a heap delta of the core only: no triangle storage, and not a peak.

The targets of the spec's §4.2 were measured by the owner on the old index and their own hardware, so a run here says nothing against them. That comparison runs on the owner's machine (§5).

## Cost of the structure

Counted rather than timed, so exact: `QuadtreeReviewRegressionTests`. Items visited by one small query, out of 100000:

| Data | Before | Now |
|---|---:|---:|
| One row of a TIN, extent 100000 x 1 | 100000 | below 64 |
| 20 km x 20 km, one point at (1e15, 1e15) | 100000 | at most 8 times the count without the point |

Inserting points in sorted order into an empty tree, on the development machine: 40000 points took 90 s, when every insert beyond the extent rebuilt the tree. `InsertingInSortedOrder_DoesNotRebuildEachTime` now requires 20000 points on a line and 20000 on a diagonal in under 5 s.

## Results

<!-- Numbers of the current code go here, measured alone on the machine: see the PR. -->
Not measured yet for the current code. Earlier records of the abandoned sessions were dropped: they described older code and were never re-measured.
