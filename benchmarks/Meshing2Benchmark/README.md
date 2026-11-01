# Meshing2Benchmark: the acceptance gates of the 2D mesher

This is the working form of [docs/design/ACCEPTANCE_STRATEGY.md](../../docs/design/ACCEPTANCE_STRATEGY.md) for the 2D mesher: an oracle by definition, a differential against
NetTopologySuite, and a golden master. Where to start: [docs/design/MESHING2D_PLAN.md](../../docs/design/MESHING2D_PLAN.md). It holds no mesher: what it judges is registered in
`Candidates/CandidateRegistry.cs`: for M1 the production `g3.DelaunayMesh2`.

```
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- <command>
```

Run it without a command for the list. Every command first runs the oracle's self-check and stops with exit code 3 if that fails.

## Layout

| Path | What |
|---|---|
| `Oracle/` | Its own project, without a reference to the library, so the compiler proves it shares no code with what it judges. Built for netstandard2.0, used by this benchmark and by the test suite |
| `Oracle/Exact.cs` | `BigInteger` predicates by definition: orientation, in-circle, areas, distances, hull, exact sin² of the smallest angle |
| `Oracle/Domain.cs` | The region to mesh, from the rings alone, with its exact area by slab decomposition |
| `Oracle/Oracle.cs` | The verdict: validity, identity, coverage, chains of segments, constrained or plain Delaunay, bounds |
| `Oracle/SelfCheck.cs` | Hand values, correct results accepted, every kind of broken result rejected as that kind, and regressions of what the oracle's independent review found |
| `Oracle/Cases.cs` | The M1 cases: procedural, integer or dyadic, without trigonometry |
| `Oracle/Catalogue.cs` | The F cases of the specification as procedural fixtures, one state per row ([table](../../docs/design/MESHING2D_CATALOGUE.md)) |
| `Oracle/Golden.cs`, `golden/` | Stored rows, recomputed and compared; recording and its rules |
| `Candidates/` | What is judged. Also compiled into the test project |
| `NtsReference.cs` | NetTopologySuite 2.6.0 (BSD-3-Clause) as a black box. This project only, never a dependency of the library |

## The pilot: M1 against NetTopologySuite (2026-10-31)

```
$ dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- differential --larger
oracle self-check: 153 checks passed
m1/tiny-64: ReferenceInvalid: NotDelaunayLocal: edge 2-3 is not locally Delaunay against 62; ...
m1/subnormal-32: ReferenceThrew: LocateFailureException: Locate failed to converge ...
m1/mixed-48: ReferenceThrew: LocateFailureException: Locate failed to converge ...
m1/near-cocircular: ReferenceInvalid: NotDelaunayLocal: edge 2-0 is not locally Delaunay against 3; ...
differential pr200-prototype against nts-2.0.0.0: SameTriangles 24, BothValid 7, ReferenceInvalid 2, ReferenceThrew 2
```

- **24 of 24 comparable cases in general position have the same triangle set**, and 7 of 7 tied cases are both valid with equal counts.
- **In four cases the reference is at fault, not the candidate.** At 2^-560 and on near-cocircular input NetTopologySuite's in-circle test gives a wrong sign, which the oracle shows; on subnormal and mixed magnitudes it
  throws. That is outside what it promises, and it is why the oracle comes first. It only excludes those inputs from the comparison. It does not count against the candidate.
- **The stop rule of the strategy did not trigger.** The reference is 50 lines and adds about a second to a run, far less than the benchmark work it replaces, and it is usable: it agrees wherever its arithmetic suffices. What it does not catch is anything
  the kernel and NetTopologySuite share; the kernel's speed is measured elsewhere (plan, section 2).
- What it replaced: the oracle of #200's benchmark, which truncated coordinates to a 2^-40 grid (`new BigInteger(p.x * 2^40)`), so it could not judge anything finer, and which had a self-test of five broken results.

## M1: the production kernel (`g3.DelaunayMesh2`), 2026-11-01

The kernel of PR #200 is replaced. The gates on the new one: oracle self-check, differential, golden check, catalogue and operation tests all pass
(`ci`, `dotnet test ... --filter "FullyQualifiedName~Meshing2|FullyQualifiedName~DelaunayMesh2"`). The golden rows were re-recorded with the new producer;
their fingerprints did not change, the six tied rows included.

```
$ dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- differential --larger
differential g3-delaunay against nts-2.0.0.0: SameTriangles 24, BothValid 7, ReferenceInvalid 2, ReferenceThrew 2
```

**Performance** (spec 3.1: 100 000 points in about 0.1 s, 1 000 000 in about 1.3 to 2.4 s). The kernel alone, median of 5 runs after a warm-up, allocated bytes
of one run; one result per size checked by `TriangulationMesh2.IsValid` (exact, linear). Linux container, 4 cores, nothing else running, net10:

```
$ dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- perf --repeats 5
runtime .NET 10.0.14, 4 cores, x64
producer,kind,n,triangles,median_ms,min_ms,allocated_mb,valid
g3-delaunay,uniform,1000,1982,2.7,2.6,0.3,yes
g3-delaunay,uniform,4096,8169,10.0,9.8,1.2,yes
g3-delaunay,uniform,65536,131047,133.4,120.9,19.6,yes
g3-delaunay,uniform,1000000,1999963,2047.3,1904.8,313.2,yes
g3-delaunay,grid,1000,1898,2.6,2.5,0.3,yes
g3-delaunay,grid,4096,7938,9.9,9.7,1.4,yes
g3-delaunay,grid,65536,130050,152.0,145.4,22.1,yes
g3-delaunay,grid,1000000,1996002,2704.1,2647.6,351.2,yes
g3-delaunay,utm,1000,1982,2.1,1.7,0.3,yes
g3-delaunay,utm,4096,8169,6.7,6.2,1.2,yes
g3-delaunay,utm,65536,131047,125.2,112.5,19.6,yes
g3-delaunay,utm,1000000,1999844,2043.9,1926.9,351.3,yes
g3-delaunay,clustered,1000,1982,1.5,1.5,0.3,yes
g3-delaunay,clustered,4096,8168,6.5,6.3,1.2,yes
g3-delaunay,clustered,65536,131044,115.4,114.1,19.6,yes
g3-delaunay,clustered,1000000,1999954,2060.2,1940.3,313.2,yes
```

`uniform` is [0,1)², `utm` a 1 km tile at UTM northing on a 1 cm grid, `grid` an integer lattice (every cell cocircular, so the tie rule runs everywhere),
`clustered` half the points in tight clusters. The insertion order over coordinate ranks (after the M1 review) costs about 0.2 to 0.3 s at a million points against a
Hilbert grid over the bounding box (1.76 s uniform, 2.51 s grid before), and removes the quadratic build of an input with one far outlier (200 000
points: 72 million walk steps before, about 12 per point now, test `InsertionOrder_KeepsTheWalksShort_OnEveryDistribution`). Coordinates spread
over hundreds of decades on both axes still walk about sqrt(n) per point. Memory: about 310 to 350 MB allocated for a million points (2 million triangles), against the spec's
"about 100 MB per 100 000 triangles today".

**Against PR #200**, same harness, same machine, measured with a `perf --prototype` option that timed `Prototype200Candidate.Run` and was removed with
that file (to reproduce: take `Candidates/Prototype200Candidate.cs` from commit 5c0a946 and time it on the same points). One run each, with other work
on the machine, so only the order of magnitude counts:

```
producer,kind,n,triangles,median_ms,min_ms,allocated_mb,valid
pr200-prototype,uniform,1000,1982,3746.1,3746.1,1381.3,-
pr200-prototype,uniform,4096,8169,86257.6,86257.6,23648.8,-
pr200-prototype,grid,1000,1898,11093.5,11093.5,4967.1,-
pr200-prototype,grid,4096,7938,456248.1,456248.1,162636.8,-
```

At 4096 uniform points: 86 s and 23.6 GB before, 8 ms and 1.1 MB now. At 1000: 3.7 s and 1.4 GB before, 2 ms and 0.3 MB now.

**net48** could not be measured here (Linux container, no .NET Framework runtime): the library and the tests build for it; the timing on
Windows is open.

How it works ([SOURCES.md](../../docs/SOURCES.md), 2D meshing): Bowyer-Watson cavities on a triangulation closed by ghost triangles (no supertriangle,
no invented coordinates); BRIO rounds along a Hilbert curve, renumbered in that order for cache locality; a visibility walk from the last insertion;
exact predicates for every decision. Afterwards one linear pass verifies orientation, adjacency, the local Delaunay property and the convex hull and
re-triangulates each cocircular Delaunay cell as a fan from its smallest input index (decision D18): the output does not depend on the insertion order.
That pass costs about 20 % of the time.

## M2: constrained Delaunay (`g3.ConstrainedDelaunayMesh2`), 2026-11-01

Registered for M2 with the features `m2.cdt`, `m2.domain`, `m2.regions`, `m2.noding`: their 26 catalogue rows of milestone M2 are covered and their golden rows
recorded (`golden/m2.json`); rows of these features that need refinement (milestone M3) wait for M3, and `m2.snap` (explicit tolerance) is not built yet.

```
$ dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- perf --m2 --repeats 3 --sizes 1000,4000,100000,400000
runtime .NET 10.0.14, 4 cores
producer,kind,n,vertices,triangles,subsegments,median_ms,min_ms,allocated_mb,status
g3-constrained,coastline,1000,1000,998,1000,6.4,5.9,1.4,Success
g3-constrained,coastline,4000,4000,3998,4000,30.1,26.5,5.7,Success
g3-constrained,coastline,100000,100000,99998,100000,517.7,478.9,165.8,Success
g3-constrained,coastline,400000,400000,399998,400000,2259.5,2240.2,708.9,Success
g3-constrained,parcels,1013,1013,1936,1012,4.1,3.9,2.3,Success
g3-constrained,parcels,3961,3961,7744,3960,16.0,15.8,9.3,Success
g3-constrained,parcels,99905,99905,198916,99904,675.4,633.3,245.3,Success
g3-constrained,parcels,400513,400513,799236,400512,2639.1,2496.3,994.7,Success
g3-constrained,crossings,1000,29486,58952,57472,313.1,264.3,120.5,Success
g3-constrained,crossings,4000,490339,980650,974678,6390.3,5943.2,2025.4,Success
```

`coastline` is one ring of n points at UTM coordinates (spec 3.1: 400 000 coastline points in less than 5 s; 2.3 s here), `parcels` a grid of square rings
sharing their borders with a point in each, `crossings` n/2 random segments in a unit square (490 000 crossings at n = 4000). Linux, 4 cores, net10, idle;
net48 not measured here. The crossing case allocates a lot (2 GB for 490 000 crossings): the work list copies the source lists of every piece. Not tuned yet.

How it works ([SOURCES.md](../../docs/SOURCES.md)): the M1 triangulation of all points, then each constraint piece by a walk from its first end point through the
triangulation. A vertex exactly on the piece, or within the automatic tolerance of it and of every input segment it comes from, splits it; a constrained
edge in the way is a crossing, computed exactly and rounded to the nearest double, and both pieces are re-queued through it; otherwise Sloan's flips make
the piece an edge and Lawson's flips restore the constrained Delaunay property. The domain and the region labels follow from one breadth-first pass from
the outside that tracks, per triangle, the set of rings it lies inside of (even-odd); hole and region seeds fill the cells of the constraints.

## The golden master

`golden/m1.json` holds one row per case of the CI subset: fingerprints, counts, status, exact quality values; no times and no mesh data. The format is defined in
[docs/design/MESHING2D_GOLDEN_FORMAT.md](../../docs/design/MESHING2D_GOLDEN_FORMAT.md), so that other producers can make comparable rows without our code.

- **Checked** by `Meshing2GoldenTests` in the normal test suite, on net10 (Linux in CI, Windows locally) and net48 (Windows), and by `golden check`. A different value fails.
- **Updating:** `golden update --milestone M1 --reasons <file>`, with one line `<case><TAB><reason>` per new, changed or removed row. It refuses all of it if a case fails the oracle or the differential, or has no reason. In a pull request CI compares
  with the base branch: a changed row without a new reason fails (`golden verify-changes`). The reviewer sees the reasons in the diff; an update without a reason counts as a regression.
- **M2 and M3 have no rows yet**, because nothing has passed their gates: [plan, section 4](../../docs/design/MESHING2D_PLAN.md).
- **Rows of another producer** stay out of the repository until the owner decides: `compare --rows <file>` or `G3_MESH2_REFERENCE_ROWS`, skipped with a message if absent, not in CI.
