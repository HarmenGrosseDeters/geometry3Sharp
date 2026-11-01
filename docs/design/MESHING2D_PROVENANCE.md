# 2D meshing: provenance of the routines

Owner decision of 2026-11-01 (licensing hygiene for the BSL-1.0 release): every non-trivial routine of the 2D mesher names its source here, a paper and
section or a specification item. The mesher is written clean-room from [MESHING2D_SPEC.md](MESHING2D_SPEC.md), [MESHING2D_DECISIONS.md](MESHING2D_DECISIONS.md)
and the published literature; Triangle and Triangle.NET were never read by the implementing session. A separate session may compare the code with them later
and report similarities only; a flagged part is rewritten by a clean session from the spec.

Expression rules followed: the data structure is a plain triangle array with one twin index per half-edge (half-edge `3t + k` from corner `k` to corner `k + 1`
of triangle `t`, `Next(e)` by arithmetic on that index); no orientation is packed into index bits, no successor tables, no names or decomposition taken from an
existing implementation. Papers are cited where they force a choice.

Literature (full references in [SOURCES.md](../SOURCES.md), section 2D meshing):
- [BW] A. Bowyer, *Computing Dirichlet tessellations*, Comput. J. 24(2), 1981; D. F. Watson, *Computing the n-dimensional Delaunay tessellation*, Comput. J. 24(2), 1981.
- [CDS] S.-W. Cheng, T. K. Dey, J. R. Shewchuk, *Delaunay Mesh Generation*, CRC Press 2012: ch. 2 (triangulations, the Delaunay lemma), ch. 3 (incremental insertion, ghost triangles, conflict sets), ch. 6 (Ruppert, encroachment), ch. 7 (small angles).
- [ACR] N. Amenta, S. Choi, G. Rote, *Incremental constructions con BRIO*, SoCG 2003.
- [DPT] O. Devillers, S. Pion, M. Teillaud, *Walking in a triangulation*, IJFCS 13(2), 2002 (visibility and stochastic walk).
- [MSZ] E. P. Mücke, I. Saias, B. Zhu, *Fast randomized point location without preprocessing*, SoCG 1996 (jump and walk).
- [L77] C. L. Lawson, *Software for C1 surface interpolation*, 1977 (edge flips, local Delaunay criterion).
- [LL86] D. T. Lee, A. K. Lin, *Generalized Delaunay triangulations for planar graphs*, DCG 1, 1986; [C89] L. P. Chew, *Constrained Delaunay triangulations*, Algorithmica 4, 1989.
- [S93] S. W. Sloan, *A fast algorithm for generating constrained Delaunay triangulations*, Computers & Structures 47(3), 1993 (segment recovery by flips).
- [R95] J. Ruppert, *A Delaunay refinement algorithm for quality 2-dimensional mesh generation*, J. Algorithms 18(3), 1995.
- [S02] J. R. Shewchuk, *Delaunay refinement algorithms for triangular mesh generation*, CGTA 22, 2002: §3 (Ruppert, encroachment), §4.1 (concentric shells), §4 (small input angles).
- [AW87] J. Amanatides, A. Woo, *A fast voxel traversal algorithm for ray tracing*, Eurographics 1987.
- [H91] D. Hilbert 1891; [B71] A. R. Butz, IEEE Trans. Computers 1971 (Hilbert index).
- [Sh97] J. R. Shewchuk, *Adaptive precision floating-point arithmetic and fast robust geometric predicates*, DCG 18, 1997 (the library's `ExactPredicates`).

## M1: `comp_geom/DelaunayKernel2.cs`, `DelaunayMesh2.cs`, `TriangulationMesh2.cs`, `Triangulation2Result.cs`

| Routine | What | Source |
|---|---|---|
| `DelaunayKernel2` storage, `SetTriangle`, `Link`, `Next` | triangle array, twin per half-edge | [CDS] ch. 3 (triangle-based representation, described generically); spec R5 (neighbours) |
| `InsertionOrder`, `GridRanks`, `SortTies`, `Mix` | BRIO rounds by a hash of the index, each round along a Hilbert curve over coordinate ranks; ties by index | [ACR] (rounds); [H91], [B71] (curve); ranks: own, from the M1 review (a fixed grid collapses with outliers); SplitMix64 finalizer (Steele et al. 2014) |
| `HilbertIndex` | position on the Hilbert curve of a 2^16 grid | [B71] (the standard rotate-and-reflect conversion) |
| `Build`, `CreateFirst` | first non-degenerate triangle plus three ghosts, then insertion in order | [CDS] ch. 3.4 (ghost triangles instead of a bounding triangle); plan section 2 (no invented coordinates) |
| `Insert`, `Locate`, `LocateByScan` | visibility walk from the last insertion with a randomized edge order; exact duplicates detected at the end of the walk | [DPT] (stochastic visibility walk); spec Q8, D17 (duplicates) |
| `Conflict`, `GhostConflict` | in-circle test; for a ghost, strictly beyond its hull edge or in its open segment | [CDS] ch. 3.4 (the ghost's circumcircle as a half-plane limit) |
| `Cavity` | Bowyer-Watson: conflict region by search, boundary re-triangulated with the new point; k triangles become k + 2 | [BW]; [CDS] ch. 3.3 |
| `CanonicalizeTies`, `Fan`, `Find`, `Union` | verification (orientation, twins, local Delaunay of every edge) and decision D18: each cocircular Delaunay cell re-triangulated as a fan from its smallest input index | [L77] and [CDS] ch. 2 (local Delaunay implies Delaunay); D18; union-find is textbook |
| `HullIsConvex`, `HullEdge`, `NextGhost` | the ghosts form one cycle, the hull turns one way and winds once | [CDS] ch. 2 (convex hull of a triangulation); winding by counting lexicographic minima: own |
| `DelaunayMesh2.Compute`, `Publish`, `Extract`, `CheckCoordinate` | input checks (finite, 1e70), -0 to +0, renumbering for locality, canonical rotation of each triangle | spec 1.1 (validation), 1.4, Q10, D15; F-61 (signed zero) |
| `TriangulationMesh2.TryLocate`, `Smallest`, `Contains` | jump to the nearest of n^(1/3) sampled triangles, walk, smallest id among the containing triangles | [MSZ]; [DPT]; spec R7, F-62 |
| `TriangulationMesh2.IsValid` | linear exact check: orientation, mutual neighbours, local Delaunay, one convex boundary cycle | [CDS] ch. 2 (an orientation-preserving, edge-to-edge triangulation with a simple convex boundary is an embedding; Delaunay lemma); spec R9 |
| `Edges`, `IncidentTriangles`, `Incident` | enumeration of edges and of the star of a vertex | spec R5, R6 |

## M2: `comp_geom/DelaunayKernel2.Constraints.cs`, `ConstrainedDelaunayMesh2.cs`, `ConstrainedMesh2.cs`, `ConstrainedMesh2Types.cs`

| Routine | What | Source |
|---|---|---|
| `BeginConstraints`, `Touch`, `NewVertex`, `NewSlots`, `Join` | constraint id per half-edge, a leaving half-edge per vertex, growth | [LL86], [C89] (constrained triangulation); spec R4 |
| `LocatePoint` | the walk of M1 for coordinates | [DPT] |
| `InsertPoint`, `SplitTriangle`, `SplitEdge`, `Legalize`, `Flip` | insertion by a 1-to-3 or 2-to-4 split and Lawson's flips that never cross a constraint; ghosts take part | [L77]; [CDS] ch. 3 (flip-based insertion with ghosts), [C89] (the constrained Delaunay criterion) |
| `FindEdge`, `Star`, `Prev` | rotation around a vertex through twins | textbook half-edge navigation |
| `RecoverEdge`, `RestoreDelaunay`, `NonDelaunayEdges` | Sloan's segment recovery: flip crossed edges whose quadrilateral is convex until the segment is an edge, then Lawson's flips over the new edges; a final linear check | [S93]; [L77]; the final check from the M2 review |
| `Builder.InsertConstraints`, `InsertPiece`, `Split`, `Ahead` | each constraint piece by a walk from its first end: exact hits and vertices within the tolerance split it, also the apexes at an edge that exists already; a work list, no recursion | spec O10, O11, F-2 (no recursion); walk along a segment: [S93]; tolerance: decision D3 |
| `Builder.Cross`, `Intersection`, `RatioToDouble` | the crossing of two pieces computed with integers (every double is m 2^e) and rounded to nearest, ties to even; both pieces re-queued through it | decision D1; [Sh97] (the integer view of doubles); round-to-nearest-even is IEEE 754 |
| `Builder.Near`, `WithinTolerance`, `ScaledDist2`, `Scaled` | tolerance tests on power-of-two-scaled differences | decision D3; spec O11, Q14; scaling from the M2 review |
| `Builder.Output`, `RingParity`, `Xor`, `SeedCell` | the set of rings each triangle lies inside of (even-odd) by a breadth-first search from the ghosts; the innermost ring of the set decides by its role, nesting read off the sets of the real triangles (a nesting-level flood fill over the triangulation); seeds fill the cells of the constraints | spec definition of the domain; decisions D5 (owner 2026-11-01), D6, D16; spec I6, Q7; OGC Simple Features (ISO 19125) for role-based polygon nesting |
| `Builder.Covering`, `SegDist2`, `BuildGrid`, `Traverse` | the sources within the tube of a subsegment, through a uniform grid of the sources | decision D7, spec R8; [AW87] (cell traversal) |
| `Builder.Subsegment` | marker: the sole nonzero one, else 0, with the list; locked if any source is | decisions D7, D8 |
| `Builder.SetCrossingHeight`, `Along`, `SetHeight`, `InitHeights` | heights passed through (lowest index wins), crossings interpolated along the segment given first | spec Q9; decision D11 |
| `ConstrainedMesh2.TryLocate`, `ContainsSlot` | walk in the hull triangulation behind the mesh, smallest kept id | [DPT]; spec R7 |

## M3: `comp_geom/QualityMesh2.cs` and the refinement hooks of the builder

| Routine | What | Source |
|---|---|---|
| `QualityMesh2.Compute` | options, argument checks, the builder with a refinement hook | spec O3, O5, O7, Q3, Q11, Q13; decisions D9, D12 |
| `Refiner.Run` | queue of encroached subsegments first, then bad triangles, until both are empty or the Steiner limit; a final Delaunay check | [R95] §3; [S02] §3; spec O7 |
| `ConstrainHull` | without outer rings the hull edges become constraints, each its own source, inserted as pieces (a vertex within the tolerance of a hull edge becomes a boundary vertex); what lies between such a chain and the old hull edge is outside | decision D13; spec O11, O18, F-35 |
| `Encroached`, `DotSign` | a vertex of a domain triangle at the edge strictly inside its diametral circle; exact sign of a dot product (filter, then integers) | [R95] §2 (encroachment); [S02] §3; [Sh97] (filter idea) |
| `Bad`, `MinSin2`, `Area`, `LongestLocked2`, `Local` | below the angle, above the area (global or per region), above the size function at the centroid; a triangle over a locked edge need not shrink below a needle; the edges scaled by a power of two first (no underflow, the same bits at any 2^k) | spec O3, O5, O9, I7; decision D9; REVIEW_CHECKLIST 2, 5 |
| `SpansSmallAngle`, `SourcesAt` | not refining a skinny triangle whose shortest edge spans a small input angle, its ends at distances from the apex within a factor 1.5 (about one shell) | [S02] §4 (small input angles); [CDS] ch. 7 |
| `SplitTriangle`, `Circumcentre`, `Walk`, `EncroachedByPoint` | circumcentre insertion; a constrained edge on the way from the triangle, or encroached by the circumcentre, is split instead | [R95] §3; [S02] §3 |
| `SplitSegment`, `Builder.NearOtherSource` | a split point within the tolerance of another constraint is not inserted (it would merge the chains, O11); otherwise split at the midpoint or, next to a vertex that existed before refinement, at the power of two nearest to the middle; computed on the original input segment | [S02] §4.1 (concentric shells); spec O12 (no drift over repeated splits) |
| `Collect`, `TagFromPiece` (tag propagation) | rewritten triangles take their domain and region tag from unchanged neighbours across unconstrained edges, and from the sides of the pieces of a split segment, which keep the tags its sides had; ghosts count as outside | own, from the domain definition (spec) and decisions D5, D16 |
| `Builder.SnapshotReference`, `SetHeightOnReference` | heights of Steiner points by linear interpolation on the triangulation before refinement, located by a stochastic walk from the last hit | spec Q9; [DPT] (stochastic walk) |
| `Builder.NearVertex` | a crossing snaps to a vertex within the tolerance of it and of all sources involved | spec O11 |

## Corpus: public test inputs (data only)

The corpus run (`Meshing2Benchmark corpus`, fetched by `benchmarks/Meshing2Benchmark/corpus/fetch.py` at run time into the git-ignored
`corpus/data`) meshes the test fixtures of other projects. They are data: no code of these projects is read or used, their algorithms are no
source of the mesher, and nothing is committed. Each license is downloaded next to the data.

| Source | What | License | URL |
|---|---|---|---|
| mapbox/earcut | 61 polygons with holes from `test/fixtures` (listed in `test/expected.json`): water, buildings, self-touching and degenerate rings | ISC | https://github.com/mapbox/earcut |
| mapbox/delaunator | 7 point sets from `test/fixtures`: robustness1-4, issue43, issue44, ukraine | ISC | https://github.com/mapbox/delaunator |
| jhasse/poly2tri | 16 polygons from `testbed/data` (one ring per file), among them sketchup, nazca_monkey, debug2 | BSD-3-Clause | https://github.com/jhasse/poly2tri |

## The oracle (test tool, `benchmarks/Meshing2Benchmark/Oracle`)

Not part of the library; listed because its bounds decide verdicts. The fast path's filters use the static error bounds of [Sh97] (orientation:
ccwerrboundA = (3 + 16u)u times the sum of the magnitudes of the two products; in-circle: iccerrboundA = (10 + 96u)u times the permanent),
each used with a margin of about 3, and fall back to the exact rationals. The oracle does not call the library's predicates (it judges the
mesher). The global empty-circumcircle pass stays global: the Delaunay lemma (a triangulation whose edges are all locally Delaunay is
Delaunay; [CDS] ch. 2, [L77]) needs a triangulation of the convex hull of the vertices, while O2 asks for empty circles against all vertices
across holes and concave boundaries, where the output has no triangles; it is indexed by a 2-d tree instead.

### The `m2.snap` gates (#274)

The oracle rules for an explicit tolerance follow D14 (owner, 2026-11-01: snap rounding on a grid of pixel tol, Hobby 1999, with the restore
step of Belussi et al. 2016): an input point may move by at most tol/2 per axis, onto its pixel centre or the vertex it merges into, and a
surviving input slot that moved is still an input point; merged slots (aliases, Q8) do not count against a vertex bound. One rule draws on the
clean-room answers about the owner's Triangle.NET fork (`MESHING2D_SNAP_QA.md`, answer 6; behaviour only, no code): a hole marker within one
pixel of an input point that merged into another is dropped instead of removing the cell around the collapsed contour, since the fork lost
the whole domain that way. The catalogue row F-17/b tests it. Each rule has an accept and a reject self-check, each checked by mutation.
