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
| `Builder.InsertConstraints`, `InsertPiece`, `Split`, `Ahead` | each constraint piece by a walk from its first end: exact hits and vertices within the tolerance split it; a work list, no recursion | spec O10, O11, F-2 (no recursion); walk along a segment: [S93]; tolerance: decision D3 |
| `Builder.Cross`, `Intersection`, `RatioToDouble` | the crossing of two pieces computed with integers (every double is m 2^e) and rounded to nearest, ties to even; both pieces re-queued through it | decision D1; [Sh97] (the integer view of doubles); round-to-nearest-even is IEEE 754 |
| `Builder.Near`, `WithinTolerance`, `ScaledDist2`, `Scaled` | tolerance tests on power-of-two-scaled differences | decision D3; spec O11, Q14; scaling from the M2 review |
| `Builder.Output`, `Xor`, `SeedCell` | the set of rings each triangle lies inside of (even-odd) by a breadth-first search from the ghosts; seeds fill the cells of the constraints | spec definition of the domain; decisions D5, D6, D16; spec I6, Q7 |
| `Builder.Covering`, `SegDist2`, `BuildGrid`, `Traverse` | the sources within the tube of a subsegment, through a uniform grid of the sources | decision D7, spec R8; [AW87] (cell traversal) |
| `Builder.Subsegment` | marker: the sole nonzero one, else 0, with the list; locked if any source is | decisions D7, D8 |
| `Builder.SetCrossingHeight`, `Along`, `SetHeight`, `InitHeights` | heights passed through (lowest index wins), crossings interpolated along the segment given first | spec Q9; decision D11 |
| `ConstrainedMesh2.TryLocate`, `ContainsSlot` | walk in the hull triangulation behind the mesh, smallest kept id | [DPT]; spec R7 |
