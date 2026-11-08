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
