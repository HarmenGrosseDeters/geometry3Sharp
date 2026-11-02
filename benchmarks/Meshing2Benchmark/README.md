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
| `MeshCommand.cs` | The `mesh` command: our mesher on one case, written in the plain text output format. Also compiled into the test project (`Meshing2MeshCommandTests`) |
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
g3-constrained,coastline,1000,1000,998,1000,9.4,7.5,1.6,Success
g3-constrained,coastline,4000,4000,3998,4000,39.7,21.2,6.4,Success
g3-constrained,coastline,100000,100000,99998,100000,650.2,631.5,186.6,Success
g3-constrained,coastline,400000,400000,399998,400000,3171.1,3113.8,795.2,Success
g3-constrained,parcels,1013,1013,1936,1012,5.7,5.3,2.8,Success
g3-constrained,parcels,3961,3961,7744,3960,23.1,22.9,11.2,Success
g3-constrained,parcels,99905,99905,198916,99904,1008.4,988.2,297.2,Success
g3-constrained,parcels,400513,400513,799236,400512,4100.0,4075.5,1203.2,Success
g3-constrained,crossings,1000,29486,58952,57472,402.0,398.0,126.9,Success
g3-constrained,crossings,4000,490339,980650,974678,8522.0,8093.6,2116.1,Success
```

`coastline` is one ring of n points at UTM coordinates (spec 3.1: 400 000 coastline points in less than 5 s; 3.2 s here), `parcels` a grid of square rings
sharing their borders with a point in each, `crossings` n/2 random segments in a unit square (490 000 crossings at n = 4000). Linux, 4 cores, net10, idle;
net48 not measured here. The fixes of the independent review (a final Delaunay check, scaled tolerance tests, a grid of sources for the markers) cost about 0.9 s at 400 000 coastline points and 1.5 s at 400 000 parcel points (2.26 s and 2.64 s before). The crossing case allocates a lot (2 GB for 490 000 crossings): the work list copies the source lists of every piece. Not tuned yet.

How it works ([SOURCES.md](../../docs/SOURCES.md)): the M1 triangulation of all points, then each constraint piece by a walk from its first end point through the
triangulation. A vertex exactly on the piece, or within the automatic tolerance of it and of every input segment it comes from, splits it; a constrained
edge in the way is a crossing, computed exactly and rounded to the nearest double, and both pieces are re-queued through it; otherwise Sloan's flips make
the piece an edge and Lawson's flips restore the constrained Delaunay property. The domain and the region labels follow from one breadth-first pass from
the outside that tracks, per triangle, the set of rings it lies inside of (even-odd); hole and region seeds fill the cells of the constraints.

## M3: quality refinement (`g3.QualityMesh2`), 2026-11-01

Registered for M3 with the feature `m3.refine`; its rows and the M3 rows of the M2 features are covered (41 rows), golden M3 recorded for the 28 rows that are
not slow. `m3.locks`, `m3.refine-again` and `m2.snap` are not built. The slow rows (`Catalogue.SlowRows`: results of 16 000 to 200 000 vertices, mostly at
a Steiner limit) are judged by

```
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- rows --milestone M3 --seconds 90
```

which runs every covered row of the milestone, prints the oracle's verdict, the sizes and the times (about 45 minutes, mostly oracle time on the large
results); the test suite checks the slow rows only for their status unless `G3_MESH2_SLOW` is set. Known: where many segments cross at small angles
(F-4, F-5) refinement runs into the default Steiner limit (plan, section 9).

`--pending` also runs the pending rows. On master of 2026-11-01 the `m2.snap` rows show that the mesher has no explicit tolerance yet:
the candidate does not pass `SnapTolerance` on (the library has only the automatic one). F-16 (100 009 vertices, at most 2 000), F-17 (8, at most
5) and F-22/b (20 007, at most 1 000) fail; the other snap rows pass, F-15 and F-18/a only because the oracle allows a merge within the tolerance
without demanding it; both lie in the gap band of D14 (pixel width tol), where merged and unmerged pass (`Meshing2SnapGateTests`,
`docs/design/MESHING2D_SNAP.md` section 3c). F-17/b (a hole marker inside a collapsing hole) fails as well.

## Corpus: public test fixtures (2026-11-01)

The fixtures of earcut (61 polygons), delaunator (7 point sets) and poly2tri (16 polygons) are fetched at run time and never committed (sources and
licenses in `docs/design/MESHING2D_PROVENANCE.md`); polygons run through M2 and M3, point sets through M1 and M3, M3 at 0, 20 and 30 degrees:

```
python3 benchmarks/Meshing2Benchmark/corpus/fetch.py
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- corpus --seconds 120
```

Result on the head of PR #252 after the fixes below: 84 inputs, 336 runs, **332 pass the oracle, none fails or violates it**; 4 results above
`--judge-limit` (60 000 vertices) are not judged: earcut's water-huge3 at 30 degrees (114 263 vertices) and poly2tri's debug2 in M3 (a star of 10 000
points with very sharp spikes: 310 000 vertices, the Steiner limit, at every angle). Meshing time of all 336 runs: about 6 s; the oracle takes most of
the hour the run needs (poly2tri's debug2 in M2, 10 000 vertices and segments: over 30 minutes).

Found and fixed by the run (each with a regression test or self-check):

| Input | Symptom | Cause, fix |
|---|---|---|
| earcut water-huge, -huge2, -huge3 (M3) | triangles outside the domain, missing segments | a split point rounded onto another constraint through the same end at 0.84 degrees; the sides of the split piece gave stale tags. Tags from a piece only for edges with exactly its sources; no split point within the tolerance of another constraint |
| earcut water (M3) | an input vertex in no triangle | refinement split the sides of a 3.4 degree spike down to where they are within the tolerance of each other; the chains merged and cut off the tip. Same fix (`SpikeAtASmallAngle_KeepsItsApex`) |
| delaunator issue43, robustness1, robustness4 (M3) | angles below the bound, boundary edges off the hull; 100 000 vertices from 36 | points 5e-15 inside a hull edge encroached it for ever. Hull edges inserted as pieces, a vertex within the tolerance of an existing edge becomes a vertex of it (`PointsUlpsInsideAHullEdge_BecomeBoundaryVertices`) |
| earcut infinite-loop-jhl | oracle: empty mesh on a domain with area | a sliver 1e-28 wide, below the tolerance; the oracle now allows empty for a domain whose twice area is at most 2 tube perimeter (self-check 5) |
| earcut water-huge (M3 at 20, 30 degrees) | oracle: two angles below the bound at an apex of 0.84 degrees | inside the zone where the two segments are within the tube of each other no point can be placed; the oracle exempts triangles within 3 tube / sin(angle) of such an apex (self-checks 6 and 7; 8 before #265) |
| earcut eberly-6 | oracle: 10 minutes for 6 339 vertices | floating-point filters in front of the exact segment, ring and marker tests (now 5 s) |

## The oracle's fast path (2026-11-01)

The oracle decides with exact rationals. On large results that took minutes, so it now has a fast path: floating-point filters with
error bounds in front of the exact orientation, in-circle, angle, ring and distance tests (the exact test where the filter cannot decide),
spatial indexes (a 2-d tree for the global empty-circumcircle pass, which now runs at every size; boxes and y buckets for the ring and
segment tests), integer sums over a common power-of-two denominator for the area, and edge keys hashed with mixing. The reference path
(`--oracle reference`, `OracleExpect.Reference`) keeps every test exact; `Meshing2OracleModesTests` compares the verdicts of both on every
covered catalogue row, on four corruptions of each, and runs the self-checks on the reference path.

Measured on one machine (4 cores), the same build of the mesher:

| Run | before | after |
|---|---|---|
| `rows --milestone M3 --seconds 600` (41 rows), oracle time | 705 s | 48 s |
| F-40 at 40 degrees (200 032 vertices), row total | 304 s | 12 s |
| F-4/c (37 568 vertices) | 100 s | 17 s |
| earcut water-huge at 20 degrees (24 220 vertices), oracle | about 105 s | about 8 s |
| earcut water-huge at 30 degrees (49 489 vertices), oracle | about 170 s | about 10 s |

```
G3_ORACLE_TIMING=1 dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- corpus --filter 'earcut/water-huge$' --angles 30
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- rows --milestone M3 --seconds 600
```

The share per check on water-huge at 30 degrees before: conformity 55 % (classifying boundary vertices that round a few ulps outwards, then
exact distances to every ring edge), quality 32 % (an exact sine per triangle), coverage 6 %; the global pass did not run (above
`GlobalDelaunayLimit`). After, with the global pass running: conformity 3.2 s, segments 2.2 s, coverage 1.8 s, the rest below 1 s each. The
indexed global pass found violations the skipped pass had hidden: conforming Delaunay in the merge zone of a small apex, which the oracle now
excuses like the angle bound there (self-check 7).

## The golden master

`golden/m1.json` holds one row per case of the CI subset: fingerprints, counts, status, exact quality values; no times and no mesh data. The format is defined in
[docs/design/MESHING2D_GOLDEN_FORMAT.md](../../docs/design/MESHING2D_GOLDEN_FORMAT.md), so that other producers can make comparable rows without our code.

- **Checked** by `Meshing2GoldenTests` in the normal test suite, on net10 (Linux in CI, Windows locally) and net48 (Windows), and by `golden check`. A different value fails.
- **Updating:** `golden update --milestone M1 --reasons <file>`, with one line `<case><TAB><reason>` per new, changed or removed row. It refuses all of it if a case fails the oracle or the differential, or has no reason. In a pull request CI compares
  with the base branch: a changed row without a new reason fails (`golden verify-changes`). The reviewer sees the reasons in the diff; an update without a reason counts as a regression.
- **M2 and M3 have no rows yet**, because nothing has passed their gates: [plan, section 4](../../docs/design/MESHING2D_PLAN.md).
- **Rows of another producer** stay out of the repository until the owner decides: `compare --rows <file>` or `G3_MESH2_REFERENCE_ROWS`, skipped with a message if absent, not in CI.
- **Our output as a file**, to set beside a reference output: `mesh --case <id> --output <file>` runs the candidate of the golden rows (the same one and
  the same options) on a case and writes the result in the plain text output format (format, section 4), or as Triangle's `.node` / `.ele` / `.poly`
  files when `<file>` ends so (the common exchange format of 2D meshers; written from Triangle's published format description, no Triangle code).
  `mesh --input <file> --output <file>` takes an input file: the plain text input format, or Triangle `.poly` / `.node` (the domain is what
  the segments enclose, as Triangle `-p` gives it, mapping and limits in the [g3cli README](../../tools/g3cli/README.md#triangle-files-poly-node-ele);
  hole points remove holes, regional attributes are integer labels; quality options from
  `--min-angle`, `--max-area`, `--steiner-limit`). An input equal to a case (the same input_sha256, eg written by `case`) is that case: judged with
  its expectations and compared with its golden row. Any other input is judged with the default expectations of its milestone (`--milestone`, or
  inferred from its options), which know nothing a case states (a required failure, stated areas, kept or removed points), so its exit code 1 means
  "the oracle objects under default expectations", not necessarily a fault of the mesher. It prints the row's fingerprints, the oracle's verdict
  and the comparison with the golden row; exit code 0 if valid and equal (or no row), 1 if invalid or different, if the mesher threw, or if Triangle
  files were asked for a failed result (they have no failure status; none are written), 2 for wrong usage, an unreadable input or an output
  directory that does not exist. The written files read back give the golden row's fingerprints (`Meshing2MeshCommandTests`). A snap tolerance (in the text input, or `--snap-tolerance` for Triangle input) is applied by the candidates as in the golden rows (m2.snap, #277).

  ```
  dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- mesh --case f-23/angle-20 --output ours.txt
  #   triangles_fp b26c528c..., subsegments_fp b03956b9..., vertices_fp d265ee67..., oracle VALID, golden: same as the recorded row
  dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- mesh --case f-23/angle-20 --output ours.ele    # ours.node, .ele, .poly
  dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- mesh --input lake.poly --output lake_ours.ele --min-angle 30
  ```
