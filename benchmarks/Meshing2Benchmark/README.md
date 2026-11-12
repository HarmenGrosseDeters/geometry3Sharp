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
Update 2026-11-12: `m2.snap` is built since #277 (2026-11-02); its rows are covered (`Catalogue.CoveredFeatures`).

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
not slow. `m3.locks`, `m3.refine-again` and `m2.snap` are not built (update 2026-11-12: `m2.snap` since #277; the other two are still pending). The slow rows (`Catalogue.SlowRows`: results of 16 000 to 200 000 vertices, mostly at
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
Update 2026-11-12: since #277 the candidates pass `SnapTolerance` on and the `m2.snap` rows are covered.

## Corpus: public test fixtures (2026-11-01)

The fixtures of earcut (61 polygons), delaunator (7 point sets) and poly2tri (16 polygons) are fetched at run time and never committed (sources and
licenses in `docs/design/MESHING2D_PROVENANCE.md`); polygons run through M2 and M3, point sets through M1 and M3, M3 at 0, 20, 25 and 30 degrees (25 since the O11-unsplittable finding below):

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
| earcut water-huge (M3 at 25 degrees; 0, 20 and 30 never ran 25) | oracle: a vertex in a circumcircle across a leg of the 0.84 degree corner | the leg's first piece (2^-22) has its midpoint 1.75e-9 from the other leg, within the mesher's tolerance (1.98e-9): O11 forbids the split point, so conforming Delaunay cannot be reached across it. The oracle mirrors the grid's three unsplittable exemptions for such O11-unsplittable pieces, counted per report (self-checks 7b to 7d, `SpikeOf084Degrees_*`). With it: 84 inputs, 330 M3 runs pass, 6 unjudged; excused once (water-huge at 25, 1 global), none in the catalogue rows |
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

### Oracle speed (2026-11-02)

The reference session's timings (`mesh` including its oracle) were dominated by the oracle, not the mesher (`mesh --no-oracle`, #319).
Profiled hot spots, all on the fast path only (the reference path keeps every full scan):

- **AngleBound's merge zone** (`InMergeZone`):
  - source directions in doubles once per source, not per pair and triangle;
  - pairs whose sine is surely above the bound are skipped before the exact test;
  - each pair's sine, zone and apex are memoized;
  - a double filter runs before the exact distance to the apex's source.
- **Segments:**
  - grids over the vertices and the ring edges replace the per-source scans;
  - orientation and distance filters in doubles run before the exact crossing and tube tests (`InTube`, `SurelyOneSide`).
- **Coverage:** `LocateExact` uses a grid over the triangles. The probes beyond the boundary edges scanned every triangle.

The grid is the oracle's own small uniform grid (`BoxGrid`), not the library's quadtrees. The oracle references nothing of
geometry3Sharp, so the compiler proves it shares no code with what it judges (owner decision 2026-11-02).

The changes keep the outcome exactly:
- **The grids:** they return a superset of the full scan's candidates, in ascending order, so the lists, the first match, the BFS path
  and the reported end moves are the same.
- **The double filters:** they only skip tests that are surely false, with error bounds of 1e-12 of the coordinates' magnitude.
  They run only for magnitudes in [1e-140, 1e140], with directions normalised first. The review found an overflow at 1.3e154 and
  an underflow of a product of squared lengths at 2^-270. `ExtremeScales_SameVerdictAndDetailsOnBothPaths` compares fast and
  reference, the detail strings included, at 2^-300 to 2^512.
- **Checked:** the corpus (84 inputs, 420 runs at M1/M2 and M3 at 0/20/25/30) gives the same verdicts and details, merge-zone and
  O11 counts included. The 55 catalogue M3 rows give the same output. `Meshing2OracleModesTests` (fast against reference) passes, and
  the self-check passes on both paths.

Oracle time only (`oracle --input --output`, one process each, this 4-core container):

| Output | before | after |
|---|---|---|
| Herne 373600 q20, snap 0.01 (94 238 triangles) | 69.2 s | 14.7 s |
| Herne 373600 q30, snap 0.01 (241 626 triangles) | 100.7 s | 27.2 s |
| poly2tri debug2, M2 (9 998 triangles) | 66.0 s | 12.3 s |
| earcut water-huge3, M2 (15 500 triangles) | 9.7 s | 6.4 s |
| earcut eberly-6, M2 | 3.4 s | 2.1 s |
| Herne 381000 q20, snap 0.01 (5 656 triangles) | 2.2 s | 2.6 s (noise) |

### Oracle speed: the tube of the sources (2026-11-03)

GEOS's TestOverlay-shapely-829/1 (M2, 935 points, 1 112 segments with many crossings: 37 342 vertices, 74 273 triangles) took the
mesher 2.2 s and the oracle more than 600 s in the reference run. The time went into two checks, each testing a vertex exactly against
the tube of every source: `InventedVertices` (every vertex that is no input point, against all sources) and `Marks` (both ends of every
subsegment). Every rational operation reduces by a GCD. On the fast path only:

- **A grid over the source boxes** (widened by the tube) in `InventedVertices`, the same `BoxGrid` as above. It returns a superset in
  ascending order, so the list of near sources is the same.
- **A double filter before the exact test** (`TubeFilter`), for both checks. It decides "certainly farther" when the distance to the
  source's line, bounded from below, exceeds the tube. It decides "certainly within" when the foot certainly lies on the source and the
  distance to the line, bounded from above, is below the tube. The error bounds are those of the orientation filter (ccwerrboundA, used
  as 1e-15 of the sum of the products). Products that underflow, and subnormal squares, leave it undecided.

The self-checks hold the filter's boundaries:
- the tube's two sides on a diagonal source;
- a vertex on a source's extension just beyond its end, inside its box (the foot condition);
- the error band, where the filter must not decide;
- exact zeros on axis-parallel sources.

They run on both paths. Each unsound mutation of the filter's conditions makes one of them fail. The review's mutants are covered
too: the foot's lower end, the exact-zero shortcut and the cross product's error in the upper bound. A fuzz test,
`Meshing2OracleTubeFilterTests`, compares the filter with the exact distance at the tube's boundary in the frames 2^-300 to 2^512.

Oracle time only (`oracle --input --output`, one process each, this 4-core container):

| Output | before | after |
|---|---|---|
| GEOS shapely-829/1, M2 (74 273 triangles), total | 714 s | 20 s |
| of it InventedVertices | 540 s | 0.7 s |
| of it Marks | 158 s | 0.6 s |

`Segments` (13.6 s here) keeps its own filters (`InTube`, with a far side only).

### Conformity searches: certified STRtree pruning (2026-11-08, #411)

On the fast path (binary64 output) `Conformity` finds the used vertices that touch a triangle and the properly crossing edge pairs with
NetTopologySuite STRtrees (an oracle-only dependency) and certified pruning: a point node is skipped when the affine maximum of a triangle
edge's orientation over its closed box is negative; an edge node when its containing polygon (the exact endpoint hull up to 16 vertices,
else its box corners) lies on one closed side of the query line, or the query segment on the closed outer side of a polygon edge. The leaf
tests are the oracle's unchanged `OrientV` filter with the exact fallback. The reference path keeps the sweep (`BoxPairs`). Proofs,
controls and measurements: [OracleIndexPilot](../OracleIndexPilot/README.md).

What it costs and gains (edge component, one 2-CPU container): where edge boxes overlap massively the sweep is quadratic and the hull
search wins (a 301 453-triangle former timeout: 5.9 s against 1.7 s for 4096 sampled queries; a comb of 8000 diagonal chains: 6.4 s
against 1.4 s); on convex boundaries, where few nodes can be pruned, it stays about 3-4 times slower than the sweep (a 60 000-point
lens: 0.25 s against 0.93 s), growing like n log n.

| Variable | Values | Default |
|---|---|---|
| `G3_ORACLE_POINT_INDEX` | `strtree`, `sweep` | `strtree` |
| `G3_ORACLE_EDGE_INDEX` | `strtree-hulls`, `sweep` | `strtree-hulls` |

Unset, empty or blank means the default; case and surrounding blanks do not matter. Any other value stops the command at its start
(exit code 2) and `Oracle.Check` before any work, with the variable, the value and the accepted values. With
`Oracle.CheckFastPathForTests` (the oracle tests) both searches are compared with the sweep pair for pair
(`Meshing2OracleStrtreeTests`, `Meshing2OracleFastPathCheckTests`).

`G3_ORACLE_TIMING` laps: the former `conformity` lap is now `conformity: point-triangle`, `conformity: edge-crossings` and
`conformity: remaining` (duplicate and unused vertices, the domain). `recheck.py` reads lap names generically (`ORACLE <name> <ms> ms`).

## ALKIS tiles: fetch, prepare, sample (2026-11-12)

Real cadastral data, fetched at run time from the original source and never committed: Geobasis NRW, `opengeodata.nrw.de`, product
"Grundrissdaten vereinfacht" (`produkte/geobasis/lk/akt/gru_vereinfacht_gpkg`, licence dl-de/zero-2-0). The host delivers one GeoPackage per
district (Kreis or kreisfreie Stadt, 32 to 263 MB zipped), not per tile; the layers used are `Flurstueck` (parcels) and `GebauedeBauwerk`
(buildings), `Nutzung` (land use) only to classify tiles in the sample. Scripts in `corpus/` (Python 3 standard library only):

| Script | Does |
|---|---|
| `alkis_tiles.py` | downloads a district (cache dir, archive SHA-256 printed), prepares tiles; `--herne` = the three Herne tiles of `herne_ratchet.py` |
| `alkis_sample.py` | the stratified, seeded sample (`--plan`, `--draw`), see its docstring for the rule |
| `alkis_run.py` | runs `mesh` (mesher, then the oracle) per input, one process each, resumable `results.jsonl`, flags SLOW / SLOW-ORACLE / STEINER / INVALID / CRASH / TIMEOUT |

Preparation of a tile (fixed, the same for Herne and the sample): a square of 200 m whose south-west corner is the tile name
(`easting_northing`, UTM 32N); the whole boundary rings (not clipped) of every parcel and building whose bounding box intersects the
tile; one point per coordinate pair and one segment per point pair; no rings, holes or regions (the domain is the convex hull); coordinates
as in the source, `snap_tolerance 0.01`, `steiner_limit 200000`, `resolve_crossings 1`. Files: `m2_<tile>s.txt` (M2), `base_<tile>_q<20|30>s.txt`
(`conforming 1`), `cq_<tile>_q<20|30>s.txt` (`mode constrained`). The tile size and the "not clipped" rule are not written in older notes; they
were read off the tile names, `MESHING2D_SPEC.md` (100 to 200 m tiles) and `MESHING2D_HULL_GRID.md` (Herne points outside the tile), and checked
against the recorded constrained-mode counts (below).

**Reproducibility.** The host's "akt" data change over time, so a sample or a Herne result is reproducible per archive SHA-256: `alkis_tiles.py` prints it, and `alkis_sample.py` writes the archives (file, size, SHA-256) and the tile list into `sample.json`. `--only` and `--first` select from the full draw and never change it (`test_alkis_sample.py`, offline, in CI).

**Herne, fetched and prepared by `alkis_tiles.py --herne` (archive sha256 `1d92eb85...39232`, 2026-11-12, master `2b26172`).** All 12 runs of
`herne_ratchet.py` are VALID, RefinementPinch 0, exit 0. Input sizes: 371800 474 points / 513 segments, 373600 1 713 / 1 925, 381000 323 / 333.

| Run | triangles now | recorded (document) |
|---|---|---|
| cq 371800 q20 / q30 | 7 502 / 24 006 | 7 506 / 24 433 (SMALL_ANGLES) |
| cq 373600 q20 / q30 | 57 085 / 153 886 | 57 113 / 154 284 (SMALL_ANGLES) |
| cq 381000 q20 / q30 | 3 051 / 8 221 | 3 053 / 8 315 (SMALL_ANGLES) |
| base 371800 q20 / q30 | 13 315 / 31 145 | 20 912 / 49 835 (GRADING, 2026-11-04) |
| base 373600 q20 / q30 | 83 933 / 157 954 | 91 827 / 206 700 (GRADING); 241 626 at q30 (2026-11-03) |
| base 381000 q20 / q30 | 3 740 / 8 290 | 5 384 / 12 990 (GRADING) |

The constrained-mode counts agree within 1.7 %, so the input is the same. The base counts are lower than the older tables because those predate the
small-angle rule in the conforming mode (`MESHING2D_SMALL_ANGLES_CONFORMING.md`, 2026-11-08); these numbers are the new reference. Not tuned.

### ALKIS sample run (2026-11-12)

41 tiles of 200 m from 11 districts, drawn by `corpus/alkis_sample.py` (seed `g3-alkis-2026-11-12`, 10 strata, rule fixed before the first run),
5 runs per tile (M2; M3 default mode q20 and q30; M3 constrained mode q20 and q30), 205 runs, snap 0.01 m, Steiner limit 200 000. Master
`7668b57` plus this branch, 4-core container, one process at a time, one run each (JIT included; times are indicative). Commands:
`alkis_sample.py --draw --cache <dir> --out <dir>`, then `alkis_run.py --sample <dir>`. Fetch: 1.7 GB of district archives (the host serves
no single tiles). Wall time 2 998 s, of which the mesher 217 s and the oracle 2 374 s.

**Result: 198 of 205 runs VALID, 7 INVALID. No crash, no hang (longest run 128 s, the oracle), no mesher failure, no Steiner-limit hit, no mesher run
above 20 s (longest 4.0 s).** The oracle took more than 20 s on 39 runs (the dense tiles of the strata tiny and centre, up to 127 s at 128 009 triangles).
A silent drop (an unused input vertex, an uncovered segment) is a verdict of the oracle and would show as INVALID. Cells: triangles / mesher time / oracle time.

| stratum | district | tile | points / segments | M2 | base q20 | base q30 | cq q20 | cq q30 |
|---|---|---|---|---|---|---|---|---|
| tiny | Köln | 349200_5653200 | 2204 / 2652 | 4540 / 375 ms / 6.8 s | 55809 / 2829 ms / 42.0 s | 127856 / 3476 ms / 54.9 s | 28285 / 1900 ms / 27.2 s | 92026 / 2585 ms / 45.9 s |
| tiny | Düsseldorf | 345800_5683600 | 1825 / 2134 | 3791 / 465 ms / 8.1 s | 54836 / 2283 ms / 39.2 s | 114648 / 3576 ms / 49.6 s | 37217 / 2399 ms / 25.0 s | 101909 / 3069 ms / 40.2 s |
| tiny | Duisburg | 346800_5695800 | 1432 / 1821 | 2919 / 336 ms / 2.7 s | 31652 / 1480 ms / 20.8 s | 73794 / 2563 ms / 31.6 s | 24177 / 1634 ms / 18.7 s | 69247 / 2062 ms / 31.5 s |
| tiny | Dortmund | 400000_5703800 | 1869 / 2184 | 3751 / 351 ms / 5.0 s | 29410 / 1456 ms / 23.6 s | 68260 / 2247 ms / 34.1 s | 21522 / 1599 ms / 17.8 s | 63151 / 1957 ms / 32.7 s |
| huge | Euskirchen | 332400_5586800 | 165 / 169 | 311 / 87 ms / 0.2 s | **INVALID** | 34337 / 2071 ms / 6.7 s | 10735 / 892 ms / 3.3 s | 26403 / 1404 ms / 5.2 s |
| huge | Siegen-Wittgenstein | 437400_5619400 | 903 / 947 | 1787 / 212 ms / 0.9 s | 11689 / 834 ms / 4.2 s | 25621 / 1505 ms / 9.2 s | 9271 / 844 ms / 3.9 s | 24574 / 1251 ms / 9.1 s |
| huge | Höxter | 507600_5728000 | 632 / 674 | 1258 / 211 ms / 0.8 s | 6392 / 548 ms / 2.2 s | 14824 / 958 ms / 4.1 s | 5203 / 536 ms / 2.0 s | 13781 / 866 ms / 3.5 s |
| huge | Borken | 345400_5739200 | 287 / 300 | 562 / 163 ms / 0.2 s | 28973 / 1405 ms / 7.1 s | 54073 / 1801 ms / 10.9 s | 19080 / 1536 ms / 5.2 s | 50601 / 1775 ms / 10.2 s |
| mining | Rhein-Erft-Kreis | 329800_5643000 | 277 / 291 | 542 / 154 ms / 0.3 s | 4251 / 393 ms / 1.7 s | 7485 / 517 ms / 2.0 s | 2948 / 351 ms / 1.0 s | 7471 / 549 ms / 1.9 s |
| mining | Düren | 315200_5639000 | 159 / 179 | 306 / 115 ms / 0.2 s | 17471 / 1065 ms / 4.7 s | **INVALID** | 11738 / 1016 ms / 3.7 s | 28438 / 1476 ms / 6.6 s |
| mining | Rhein-Erft-Kreis | 331000_5641400 | 138 / 146 | 261 / 117 ms / 0.2 s | 3839 / 356 ms / 1.2 s | 6815 / 508 ms / 1.8 s | 2560 / 350 ms / 0.7 s | 6731 / 482 ms / 1.3 s |
| mining | Düren | 326600_5640000 | 189 / 200 | 367 / 120 ms / 0.2 s | 3940 / 468 ms / 1.4 s | 8755 / 675 ms / 2.1 s | 2677 / 405 ms / 0.8 s | 7248 / 514 ms / 1.6 s |
| river | Duisburg | 345600_5702400 | 366 / 399 | 719 / 184 ms / 0.4 s | 9727 / 677 ms / 2.6 s | 21299 / 978 ms / 4.4 s | 7397 / 699 ms / 1.8 s | 21246 / 1264 ms / 4.2 s |
| river | Köln | 362400_5639600 | 501 / 516 | 982 / 191 ms / 0.6 s | 13809 / 961 ms / 5.0 s | 30287 / 1649 ms / 10.9 s | 9698 / 777 ms / 3.4 s | 26623 / 1521 ms / 8.0 s |
| river | Düsseldorf | 352400_5667400 | 453 / 458 | 882 / 174 ms / 0.5 s | 7007 / 553 ms / 2.2 s | 17804 / 1112 ms / 4.5 s | 5636 / 543 ms / 1.8 s | 15462 / 858 ms / 3.4 s |
| river | Dortmund | 397000_5712600 | 362 / 396 | 695 / 173 ms / 0.4 s | 9084 / 754 ms / 2.9 s | 18935 / 937 ms / 5.5 s | 6627 / 640 ms / 2.1 s | 17178 / 883 ms / 4.6 s |
| industrial | Duisburg | 339000_5699200 | 979 / 1019 | 1991 / 256 ms / 2.1 s | 17992 / 1095 ms / 6.3 s | 38334 / 1781 ms / 10.1 s | 13061 / 944 ms / 5.2 s | 38276 / 1322 ms / 9.4 s |
| industrial | Dortmund | 402200_5707000 | 412 / 461 | 808 / 182 ms / 0.4 s | 4996 / 423 ms / 1.7 s | 12025 / 716 ms / 2.7 s | 4012 / 423 ms / 1.2 s | 11914 / 721 ms / 2.5 s |
| industrial | Rhein-Erft-Kreis | 351400_5637800 | 1127 / 1211 | 2238 / 265 ms / 2.4 s | 11829 / 767 ms / 5.3 s | 29719 / 1237 ms / 11.8 s | 9756 / 742 ms / 4.7 s | 28035 / 1054 ms / 11.3 s |
| industrial | Duisburg | 341000_5695000 | 1127 / 1142 | 2062 / 278 ms / 2.5 s | 11966 / 807 ms / 6.3 s | 28834 / 1252 ms / 10.3 s | 9927 / 899 ms / 5.2 s | 25874 / 1147 ms / 9.4 s |
| forest | Euskirchen | 331200_5609600 | 689 / 699 | 1356 / 219 ms / 0.6 s | 8386 / 647 ms / 2.4 s | 16539 / 950 ms / 4.6 s | 6140 / 521 ms / 1.9 s | 15831 / 963 ms / 4.2 s |
| forest | Siegen-Wittgenstein | 459400_5642800 | 960 / 1030 | 1906 / 245 ms / 1.2 s | 10795 / 687 ms / 4.2 s | 24426 / 1291 ms / 8.3 s | 8485 / 762 ms / 3.3 s | 23795 / 1077 ms / 8.1 s |
| forest | Höxter | 528800_5721000 | 1316 / 1426 | 2649 / 297 ms / 2.0 s | 16774 / 1011 ms / 9.4 s | 38108 / 1818 ms / 20.2 s | 11605 / 910 ms / 6.1 s | 34500 / 1416 ms / 17.6 s |
| forest | Düren | 320400_5617000 | 1003 / 1024 | 1983 / 220 ms / 1.0 s | 11731 / 780 ms / 3.8 s | 26585 / 1436 ms / 8.0 s | 9546 / 737 ms / 2.8 s | 26170 / 1470 ms / 8.1 s |
| farmland | Borken | 342800_5750200 | 260 / 271 | 483 / 121 ms / 0.4 s | 8962 / 729 ms / 3.0 s | 17631 / 1133 ms / 4.6 s | 5782 / 597 ms / 2.0 s | 15729 / 860 ms / 3.7 s |
| farmland | Höxter | 518000_5712800 | 157 / 162 | 299 / 101 ms / 0.2 s | 4062 / 333 ms / 1.0 s | 9806 / 610 ms / 2.1 s | 2599 / 315 ms / 0.7 s | 7415 / 545 ms / 1.6 s |
| farmland | Düren | 315000_5647000 | 75 / 80 | 127 / 80 ms / 0.1 s | 3534 / 349 ms / 1.1 s | 10209 / 738 ms / 2.9 s | 3140 / 387 ms / 1.1 s | 7518 / 522 ms / 1.6 s |
| farmland | Euskirchen | 336800_5606000 | 160 / 173 | 301 / 106 ms / 0.2 s | 3265 / 320 ms / 1.1 s | 8425 / 580 ms / 2.2 s | 1950 / 309 ms / 0.8 s | 5069 / 439 ms / 1.4 s |
| village | Euskirchen | 342400_5600000 | 694 / 740 | 1373 / 199 ms / 0.7 s | 6009 / 453 ms / 2.0 s | 13975 / 735 ms / 4.0 s | 4872 / 469 ms / 1.6 s | 13779 / 653 ms / 3.7 s |
| village | Höxter | 504400_5723200 | 611 / 636 | 1204 / 167 ms / 0.6 s | 7020 / 481 ms / 2.2 s | 15402 / 921 ms / 5.0 s | 5248 / 498 ms / 1.7 s | 14218 / 842 ms / 4.3 s |
| village | Borken | 322000_5745000 | 593 / 626 | 1177 / 184 ms / 1.0 s | 4482 / 387 ms / 1.9 s | 10560 / 711 ms / 3.1 s | 3848 / 412 ms / 1.5 s | 10503 / 719 ms / 2.9 s |
| village | Siegen-Wittgenstein | 424200_5636600 | 648 / 699 | 1282 / 219 ms / 0.7 s | 7572 / 559 ms / 2.6 s | 16088 / 861 ms / 4.7 s | 5664 / 513 ms / 1.7 s | 15975 / 825 ms / 4.4 s |
| suburb | Bielefeld | 468800_5763400 | 1062 / 1194 | 2143 / 351 ms / 1.9 s | 12129 / 915 ms / 6.0 s | 29242 / 1260 ms / 12.2 s | 10048 / 887 ms / 4.3 s | 27838 / 1637 ms / 10.7 s |
| suburb | Dortmund | 392600_5714800 | 1170 / 1254 | 2392 / 242 ms / 3.0 s | 55265 / 2015 ms / 21.7 s | 119560 / 2814 ms / 36.3 s | 32644 / 1473 ms / 13.1 s | 108303 / 2523 ms / 32.1 s |
| suburb | Köln | 357800_5637800 | 1000 / 1084 | 1994 / 253 ms / 1.8 s | 11974 / 793 ms / 5.8 s | 27983 / 1203 ms / 10.0 s | 9539 / 891 ms / 4.8 s | 26894 / 1139 ms / 9.6 s |
| suburb | Düsseldorf | 346200_5689200 | 1246 / 1354 | 2538 / 388 ms / 2.4 s | 35603 / 1960 ms / 18.4 s | 77719 / 2486 ms / 30.5 s | 22926 / 1349 ms / 12.3 s | 69389 / 2265 ms / 26.5 s |
| centre | Köln | 356800_5650800 | 2325 / 2660 | 4745 / 534 ms / 10.9 s | 38216 / 2190 ms / 35.0 s | 87456 / 2727 ms / 44.7 s | 27286 / 1762 ms / 28.1 s | 76179 / 2668 ms / 42.0 s |
| centre | Düsseldorf | 343600_5678000 | 4028 / 4270 | 7246 / 699 ms / 59.9 s | **INVALID** | **INVALID** | 44015 / 2318 ms / 99.3 s | 112869 / 3813 ms / 118.4 s |
| centre | Dortmund | 400000_5704200 | 1888 / 2078 | 3793 / 411 ms / 6.1 s | 33791 / 1679 ms / 24.5 s | 74198 / 2634 ms / 35.1 s | 18598 / 1518 ms / 15.4 s | 69963 / 2659 ms / 33.0 s |
| centre | Köln | 366200_5642600 | 1911 / 2055 | 3807 / 377 ms / 4.8 s | **INVALID** | **INVALID** | 20568 / 1573 ms / 17.0 s | **INVALID** |
| centre | Düsseldorf | 349600_5675600 | 1911 / 2125 | 3863 / 439 ms / 7.1 s | 42061 / 2438 ms / 27.6 s | 96429 / 2885 ms / 40.2 s | 27321 / 1820 ms / 21.4 s | 81378 / 2456 ms / 35.4 s |

**Findings** (every INVALID; minimized by delta debugging over the segments, small enough to post in an issue):

| Run | Verdict | Minimal input | Issue |
|---|---|---|---|
| default q20, Euskirchen 332400_5586800 (forest, 3 huge parcels) | AngleBelowBound (q30 and constrained mode VALID) | 7 points, 4 segments, nearly collinear disjoint pieces of a hull diagonal | #723 (fixed for this input by #724) |
| default q30, Rhein-Erft 315200_5639000 (mining) | NotDelaunayLocal / NotDelaunayGlobal (a vertex inside a circumcircle) | 6 points, 4 segments (`min_angle_deg 30`, `conforming 1`) | #728 |
| default q20 and q30, Düsseldorf 343600_5678000 and Köln 366200_5642600 (centre; 4 runs) | NotDelaunayLocal / Global, the same signature as the mining run | not minimized (25 000 to 128 000 triangles) | #728 |
| constrained q30, Köln 366200_5642600 | AngleBelowBound, below 30 degrees | 6 points, 3 segments (`mode constrained`) | #729 |

The oracle time is a finding of its own, not a mesher defect: 39 runs above 20 s, up to 127 s (default q30, Düsseldorf 343600_5678000). An independent exact check of the output of the mining input (#728) finds the same non-Delaunay edge as the oracle.

Reproduce: write the tile with `alkis_tiles.py --district <key> --tile <tile>` (or take the minimal inputs from the issues) and run
`dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- mesh --input <file> --output <out> --milestone M3`.

**Scheduled job.** `.github/workflows/nightly.yml`, job `alkis`: the first tile of each stratum (10 tiles, 50 runs, about 11 minutes). `alkis_run.py --known
alkis_known.json` exits 1 on a failing run that is not listed and on a listed run that now passes (a ratchet like `herne_ratchet.py`). The rest of the sample runs by hand.

## Herne ratchet and the snapped-area report (2026-11-03)

`herne_ratchet.py` meshes the 12 Herne runs (prepared ALKIS tiles, snap 0.01 m, not in the repository; `--inputs <dir>`), judges them
at M3, and exits non-zero unless every run is VALID and its RefinementPinch count (a known violation of the strict decision on Q17,
listed by the oracle without failing; `docs/design/MESHING2D_HULL_GRID.md` section 8) equals its limit exactly. A run with fewer
lowers its limit in the script with the change that removed them. `--snapped-area` adds the mesh area minus the snapped domain's
(exact, px²); CI does not run it, and `oracle`, `rows` and `corpus` take the same switch.

| Run (q20 / q30) | RefinementPinch | mesh - snapped area, px² | oracle, #362 → this change, s | with --snapped-area, s |
|---|---|---|---|---|
| base 371800 | 215 / 217 | 0 / 0 | 4.3 → 4.2 / 6.0 → 6.4 | 6.9 / 12.4 |
| base 373600 | 64 / 66 | 0.5 / 0.5 | 21.1 → 22.4 / 49.6 → 51.6 | 48.6 / 76.5 |
| base 381000 | 21 / 21 | 21.5 / 24.5 | 3.2 → 2.9 / 3.5 → 4.0 | 3.6 / 4.5 |
| cq 371800 | 85 / 217 | 0 / 0 | 3.2 → 3.5 / 5.7 → 5.5 | 4.6 / 12.2 |
| cq 373600 | 44 / 66 | 0 / 0.5 | 11.2 → 10.1 / 32.9 → 29.0 | 33.0 / 58.0 |
| cq 381000 | 11 / 21 | 7.5 / 24.5 | 2.8 → 2.3 / 3.3 → 3.3 | 2.5 / 4.2 |
| total | | | 146.9 → 145.2 | 267.0 |

One run each, one process at a time, this 4-core container; the oracle's new rules cost 0 to 10 % per run (noise included), the
report 2.3 to 3.6 times on the large runs.

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
  files were asked for a failed result (they have no failure status; none are written), 2 for wrong usage, an unreadable input, an output
  directory that does not exist, or an option or input the mesher refuses (eg a snap tolerance too small for the coordinates, or a snap
  tolerance with `--milestone M1`, whose Delaunay mesher has none). The written files read back give the golden row's fingerprints (`Meshing2MeshCommandTests`). A snap tolerance (in the text input, or `--snap-tolerance` for Triangle input) is applied by the candidates as in the golden rows (m2.snap, #277).

  ```
  dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- mesh --case f-23/angle-20 --output ours.txt
  #   triangles_fp b26c528c..., subsegments_fp b03956b9..., vertices_fp d265ee67..., oracle VALID, golden: same as the recorded row
  dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- mesh --case f-23/angle-20 --output ours.ele    # ours.node, .ele, .poly
  dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- mesh --input lake.poly --output lake_ours.ele --min-angle 30
  ```

## Constrained-quality golden gate

The default M3 golden file and `golden/m3-constrained.json` are separate suites.
The constrained suite contains all 52 covered, non-slow M3 catalogue cases that
do not explicitly require Conforming. It creates fresh canonical inputs with
`mode constrained`, so their hashes differ from the default inputs. F-38/a–e run
with the angle check enabled, as in #394. The independent oracle must accept each
result before it is recorded, and it is rerun on every check; matching a stored
fingerprint alone cannot excuse an invalid output.

```sh
dotnet run --project benchmarks/Meshing2Benchmark -c Release -- golden check --milestone M3 --mode constrained
dotnet run --project benchmarks/Meshing2Benchmark -c Release -- golden update --milestone M3 --mode constrained --reasons /tmp/reasons.tsv
```

Updates require a per-row reason, just like the default suite. `--mode constrained`
requires M3; a missing or unknown mode value is a usage error. `ci` checks this
suite as well as default M1/M2/M3. `Meshing2GoldenTests` runs it on net10.0 and
net48, including negative mode selection and fresh-instance isolation controls.
No default golden row or production mesher is changed by this addition.

Two rows intentionally record a reached Steiner limit (f-12/b and f-37); their
limit/bad-triangle fields are protected too. An oracle-valid limited output is
not a claim that every requested quality bound was achieved.

The [reference recheck tools](rechecks/README.md) separately regenerate historical
exceptions against pinned public inputs. They retain failures/timeouts and do not
promote reference rows into golden truth.

## The quality mesher on jittered parcels: pair-key hashing (2026-11-10, QA 2.0 T5)

```
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- perf --quality --sizes 30,60,90,120,240
```

A side x side grid of square rings sharing their borders, the inner corners jittered by a quarter cell, `QualityMesh2` at 30 degrees, the mesher alone,
one run after the warm-up, with a snap pixel of 1/64 cell (`parcels-snap`) and without (`parcels`). Linux container, 4 cores, net10. This is the proxy for
the Herne tiles, which are not in the repository.

The 64-bit pair keys of the refinement (source pairs of the small-angle cache, unsplittable pieces, grid cells) used the default hash of a long, lo ^ hi, which for
(a << 32) | c is a ^ c: neighbouring ids fall on a few dozen values, so 200 000 pairs made one chain per lookup. 20 of the 22 seconds of the 56 000 triangle snap run
were in `SpansSmallAngle` (profile by stopwatches around the phases of `Compute`, `Bad` and `SpansSmallAngle`; `SplitSegment` itself was 1 %). The pair keys now use the
shared `EdgeKeyComparer`; the meshes are the same (same triangle counts, the golden rows unchanged).

| side | triangles (snap) | before ms | after ms | triangles (no snap) | before ms | after ms |
|---|---|---|---|---|---|---|
| 30 | 3 429 | 340 | 274 | 3 352 | 53 | 51 |
| 60 | 13 610 | 890 | 384 | 13 760 | 553 | 87 |
| 90 | 31 684 | 5 540 | 821 | 31 833 | 3 858 | 252 |
| 120 | 56 032 | 22 279 | 1 612 | 54 329 | 17 969 | 298 |
| 240 | 219 395 | not run | 7 823 | 226 612 | not run | 2 081 |

The triangle counts before the fix are the same (3 429, 13 610, 31 684, 56 032 with snap; 3 352, 13 760, 31 833, 54 329 without). After the fix the no-snap run is near linear (4.2x the triangles, 7x the time from 120 to 240); the snap run grows 3.9x the triangles in 4.9x the time, the rest
is not the hash (snap rounding itself, 1.5 s at 56 000 triangles in the profile): a follow-up.
