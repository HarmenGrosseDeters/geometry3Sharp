# 2D meshing: provenance of the routines

Owner decision of 2026-11-01 (licensing hygiene for the BSL-1.0 release): every non-trivial routine of the 2D mesher names its source here, a paper and
section or a specification item. The mesher is written clean-room from [MESHING2D_SPEC.md](MESHING2D_SPEC.md), [MESHING2D_DECISIONS.md](MESHING2D_DECISIONS.md)
and the published literature; Triangle and Triangle.NET were never read by the implementing session. A separate session may compare the code with them later
and report similarities only; a flagged part is rewritten by a clean session from the spec. The stage 6b check (2026-11-02, [MESHING2D_PROVENANCE_CHECK.md](MESHING2D_PROVENANCE_CHECK.md)) found no copied
expression in the comparison with Triangle.NET, nor in the direct comparison with Triangle 1.6 (`triangle.c`) that followed it (stage 6b-2, same file).

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
- [U04] A. Üngör, *Off-centers: a new type of Steiner points for computing size-optimal quality-guaranteed Delaunay triangulations*, LATIN 2004.
- [S02] J. R. Shewchuk, *Delaunay refinement algorithms for triangular mesh generation*, CGTA 22, 2002: §3 (Ruppert, encroachment), §4.1 (concentric shells), §4 (small input angles). Diametral lenses: section 6.4 ("Terminator Chew", pp. 36-38) of the draft of May 21, 2001, read for the lens definition and its bound (owner allowed the host, 2026-11-02).
- [Ch93] L. P. Chew, *Guaranteed-quality mesh generation for curved surfaces*, SoCG 1993: the deletion of free vertices in a subsegment's diametral circle before it is split, as [S02] §6.4 describes it (the paper itself not read; the rule built is the one [S02] states).
- [CF26] `docs/research/CROSS_FIELD_GEOMETRY.md` (#368), idea 2 (keep the input-only embedding; a pinch of separate chains in a new common vertex fails it) and idea 5 (safe-grid candidate regions), read 2026-11-04 for the pinch rule. Its source, J. Hershberger, *Snap Rounding: A Cautionary Tale*, SoCG 2025, was **not read**: its host is blocked in this session, and no other route to it was tried (coordinator 2026-11-04). The idea used comes from the #368 report, and the pinch rule built is our own.
- [EU09] H. Erten, A. Üngör, *Quality triangulations with locally optimal Steiner points*, SIAM J. Sci. Comput. 31(3), 2009. The journal version, **not read** (paywalled). Read instead: [EU07], its conference version.
- [EU07] H. Erten, A. Üngör, *Triangulations with locally optimal Steiner points*, Eurographics Symposium on Geometry Processing 2007, pp. 1–10. **Read 2026-11-04** (all 10 pages). The owner supplied the PDF from people.eecs.berkeley.edu/~jrs/meshpapers/ErtenUngor.pdf, a host the coordinator allowed on 2026-11-04 (not ~jrs/triangle).
  - The petal of an edge pq: the disk through p and q in which pq subtends at least the angle bound α. Its radius is \|pq\|/(2 sin α) by the inscribed angle; the text prints "2 sin α \|pq\|", read as a typo.
  - Algorithm 1: for a bad triangle with shortest edge pq, insert the point of the petal furthest from all existing vertices. That point is a Voronoi vertex, or a Voronoi edge's intersection with the petal's boundary: types I (the off-centre) to IV (the circumcentre). It is found by a local search on the Voronoi graph.
  - Lemma 1 / Theorem 1: no shorter feature for type I up to 60°, II up to 45 / 36 / 30°, III and IV up to 30°; termination up to 30° on periodic point sets.
  - Algorithm 2: relocation of free vertices into the intersection of their link's petals.
  - Step 1's choice among candidates (`BestCandidate`) predates the reading; its candidates (on the bisector), score and floor are our own. Step 2's design takes Algorithm 1's criterion (`MESHING2D_GRADING.md` 4.3).
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
| `Builder.Output`, `RingParity`, `Xor`, `SeedCell` | the set of rings each triangle lies inside of (even-odd) by a breadth-first search from the ghosts; the innermost ring of the set decides by its role, nesting read off the sets of the real triangles (a nesting-level flood fill over the triangulation); seeds fill the cells of the constraints | spec definition of the domain (I4, I6: the domain is what is reachable without crossing a constraint, which forces a flood fill from outside and from the seeds); decisions D5 (owner 2026-11-01), D6, D16; spec I6, Q7; OGC Simple Features (ISO 19125) for role-based polygon nesting |
| `Builder.Covering`, `SegDist2`, `BuildGrid`, `Traverse` | the sources within the tube of a subsegment, through a uniform grid of the sources | decision D7, spec R8; [AW87] (cell traversal) |
| `Builder.Subsegment` | marker: the sole nonzero one, else 0, with the list; locked if any source is | decisions D7, D8 |
| `Builder.SetCrossingHeight`, `Along`, `SetHeight`, `InitHeights` | heights passed through (lowest index wins), crossings interpolated along the segment given first | spec Q9; decision D11 |
| `ConstrainedMesh2.TryLocate`, `ContainsSlot` | walk in the hull triangulation behind the mesh, smallest kept id | [DPT]; spec R7 |

## M3: `comp_geom/QualityMesh2.cs` and the refinement hooks of the builder

| Routine | What | Source |
|---|---|---|
| `QualityMesh2.Compute` | options, argument checks, the builder with a refinement hook | spec O3, O5, O7, Q3, Q11, Q13; decisions D9, D12 |
| `Refiner.Run` | queue of encroached subsegments first, then bad triangles, until both are empty or the Steiner limit; a final Delaunay check | [R95] §3; [S02] §3; spec O7 |
| `ConstrainHull` | without outer rings the hull edges become constraints (on the grid of an explicit tolerance: the exact hull edges snap-rounded with the segments, `Compute`, hull sources by `Builder.HullSegmentsFrom`; owner decision 2026-11-02, `MESHING2D_HULL_GRID.md`), each its own source, inserted as pieces (a vertex within the tolerance of a hull edge becomes a boundary vertex); what lies between such a chain and the old hull edge is outside | decision D13; spec O11, O18, F-35 |
| `Encroached`, `DotSign`, `EncroachesOn` | a vertex of a domain triangle at the edge strictly inside its diametral circle, or with lenses in its closed diametral lens; exact sign of a dot product (filter, then integers). With lenses the adjacent apex still decides: a vertex in the lens and visible from the edge is not inside the circumcircle of the adjacent triangle, so it sees the edge under a smaller angle than that apex, which then lies in the lens too | [R95] §2 (encroachment); [S02] §3; [Sh97] (filter idea) |
| `Bad`, `MinSin2`, `Area`, `LongestLocked2`, `Local` | below the angle, above the area (global or per region), above the size function at the centroid; a triangle over a locked edge need not shrink below a needle; the edges scaled by a power of two first (no underflow, the same bits at any 2^k) | spec O3, O5, O9, I7; decision D9; REVIEW_CHECKLIST 2, 5 |
| `SpansSmallAngle`, `SourcesAt` | not refining a skinny triangle whose shortest edge spans a small input angle, the angle between the two input segments below the bound, the lines meeting on both segments (an input angle), and its ends on the same side of the apex at distances from it within a factor of about 1.5 (2.24 on squared distances; about one shell); on a hull domain the hull edges count as constraints (D13: the hull is the boundary; found with CDT's issue-211, a segment at 0.096° to a hull edge), in the oracle too (`HullSources`, split at the input points on them as the mesher's hull sources are) | [S02] §4 (small input angles); [CDS] ch. 7 |
| `SplitTriangle`, `Circumcentre`, `Walk`, `EncroachedByPoint` | circumcentre insertion, or the off-centre for a triangle below the angle bound; a constrained edge on the way from the triangle, or encroached by the point, is split instead | [R95] §3; [S02] §3 |
| `InLens`, the lens rule in the `Refiner` constructor | the closed diametral lens: two disks of radius \|s\|/√3 centred on the bisector \|s\|/(2√3) from s on either side, so its boundary sees s under 120 degrees; tested as (pa·pc) < 0 and 4(pa·pc)² ≥ \|pa\|²\|pc\|² (filter with a relative margin of 2^-40, then integers). Used without `Conforming` up to arcsin(√3/4) = 25.65 degrees (sin² ≤ 3/16), the bound [S02] §6.4 proves for lenses (B ≥ 2/√3, input angles ≥ 60 degrees); circles above it (owner decision 2026-11-02, rule 3i). Before a subsegment is bisected, the free vertices in its open diametral circle that see its midpoint are deleted (`DeleteFreeInCircle`, below), as the paper's rule says. **Termination measured, not proven:** the paper's proof covers circumcentres and midpoint bisection; this mesher also inserts off-centres, splits at concentric shells next to input vertices and rounds every point to a pixel centre on the snap grid (review of the deletion PR). Measured on the catalogue, the corpus at 20/25 degrees and the Herne tiles; the Steiner limit bounds every run | [S02] §6.4; spec O2, O7 |
| `ValidGridSplit` (the pinch rule), `SharesSource`; the oracle's `GridUnsplittable` | a split point on the grid is never a vertex of refinement (id at least `firstSteiner`) at which a constrained edge of another chain (no common source with the piece) ends: the piece would run through it and the two chains would meet in that vertex (a pinch), which the input's own rounding keeps apart (owner decision 2026-11-03 on Q17, strict). Through the hot pixels of the input's rounding the chains still meet, as snap rounding routes them. Our own rule, the cheap variant (1') of the coordinator's plan: it refuses the candidate instead of checking an embedding transaction ([CF26] idea 2), so it can leave more subsegments unsplittable; the oracle recomputes splittability from the specification with the same exclusion, no broader (a pixel that is no input-only hot pixel and holds an end of another chain's subsegment; it attributes a subsegment to the piece's own chain by envelope, the mesher by source, so the oracle may demand a split the mesher refuses but never excuses one it would make). Self-checks 5c/5d at the call site | Q17; [CF26] |
| `DeleteFreeInCircle`, `InCircleAndVisible`, `SegmentsMeet` | with lenses, before the bisection of a subsegment s that happens (a split refused before its split point is found deletes nothing; the Terminator's declined splits of the paper delete nothing either): every vertex made by refinement (id at least `firstSteiner`: not an input point, crossing or hot pixel centre of snap rounding) in the open diametral circle of s (exact dot sign) whose segment to the midpoint of s meets no other constrained edge, closed (exact orientations), is deleted with the kernel's `DeleteVertex`, which refuses a vertex on a constraint (so a vertex a chain or a reroute runs through stays) and restores constrained Delaunay. A vertex at the split point itself stays. The midpoint is (pu + pw) / 2 rounded in doubles. Candidates and blocking edges come from the triangles reached from s across unconstrained edges within twice the radius of the midpoint (in a frame centred on it and scaled by a power of two; a sight line inside the circle crosses only edges that meet the circle, and searching more only costs time). Deletion happens once the split point is found and the Steiner limit allows the split. Reading of "before s is bisected": every bisection (coordinator 2026-11-03), as Chew 1993 does on a rejected circumcentre | [S02] §6.4 ("Terminator Chew", p. 37); [Ch93] |
| `OffCentre`, `HalfCot` | on the perpendicular bisector of the shortest edge pq, towards the circumcentre, |pq| cot(theta/2) / 2 from the midpoint (the triangle of pq and the point has the angle bound at the point), the circumcentre if that is closer; cot(theta/2) = (1 + cos theta) / sin theta from sin^2 theta with square roots only; in the triangle's frame scaled by a power of two (the same bits at any 2^k); on the snap grid rounded to its pixel centre. Diametral lenses ([S02] §6.4) were measured and left out under conforming Delaunay (O2, on whenever quality is) they broke O2 on 9 of 55 catalogue rows and about half the corpus runs, and f-40 at 33.75 degrees did not terminate (owner decision 2026-11-02, option a, confirmed on the measurement in `MESHING2D_REFERENCE_COMPARISON.md` section 3) | [U04]; spec O2 |
| `TriangleQueue` (worst first) | with an angle bound, bad triangles are taken in order of the sin² of their smallest angle when queued (a binary heap, ties in the order queued); a stale entry is judged as the triangle is when taken out. Our own choice, measured (`MESHING2D_GRADING.md`): processing order is a known lever in Delaunay refinement practice, but no source for this ordering was read | own |
| `BestCandidate`, `Score` | for a triangle bad by its angle: the default point (off-centre or circumcentre), the circumcentre and 6 points on the bisector of the shortest edge (from the nearer of off-centre and circumcentre to 1.3 times the circumcentre's distance); each must be inserted as it is (no constraint crossed, no encroachment, the triangle in its cavity, on the grid rounded and no constraint in its pixel); score = bad triangles in the cavity − new triangles below the bound in the fan, ties to the larger insertion radius; **floor:** never a smaller insertion radius than the default point's, and no choice when the default point does not count. Termination measured, not proven; the floor keeps the insertion-radius bound of [R95], [S02], [U04] for the chosen point | [EU09] (the idea of choosing among points); own (candidates, score, floor) |
| `SplitSegment`, `Builder.NearOtherSource` | a split point within the tolerance of another constraint is not inserted (it would merge the chains, O11; also a split point of a hull edge near a segment, not the reverse: the oracle's merge zone counts that pair, `InMergeZone`); otherwise split at the midpoint or, when exactly one end existed before refinement, at the power of two nearest to half the length, used only if it lies in (L/3, 2L/3); computed on the original input segment | [S02] §4.1 (concentric shells; the pieces in ratio at most 2:1, hence the window); spec O12 (no drift over repeated splits) |
| `Collect`, `TagFromPiece` (tag propagation) | rewritten triangles take their domain and region tag from unchanged neighbours across unconstrained edges, and from the sides of the pieces of a split segment, which keep the tags its sides had; ghosts count as outside | own, from the domain definition (spec) and decisions D5, D16 |
| `Builder.SnapshotReference`, `SetHeightOnReference` | heights of Steiner points by linear interpolation on the triangulation before refinement, located by a stochastic walk from the last hit | spec Q9; [DPT] (stochastic walk) |
| `Builder.NearVertex` | a crossing snaps to a vertex within the tolerance of it and of all sources involved | spec O11 |

## `m2.snap`: `comp_geom/SnapRounding2.cs` and `SnapTolerance` on both meshers (#277)

Implemented from the papers, after decision D14 (owner, 2026-11-01; `MESHING2D_SNAP.md`):
- **Snap rounding** on a grid of pixel width tol anchored at the origin, half-open pixels (closed left and bottom), hot pixels at input
  points and proper crossings, every segment routed through the centres of the hot pixels it touches in order along it: J. D. Hobby,
  *Practical segment intersection with finite precision output*, CGTA 1999; the properties (half a pixel per axis, topology up to
  collapse) from L. Guibas, D. Marimont, *Rounding arrangements dynamically*, IJCGA 1998, as summarised in Hershberger 2025.
- **Restore**: critical pixels (a hot pixel whose interior an output edge crosses, its ends outside), ChoosePixel with rules R1 to R3
  (spread to a free useful pixel of the segment's envelope next to the critical pixel, nearest to the segment): A. Belussi,
  S. Migliorini, M. Negri, G. Pelagatti, *Snap rounding with restore*, ACM TSAS 2016, Listings 1 and 2, Table II. Our deviations,
  each found by the fuzz test and stated in the code: every round routes all segments again on the grown set of hot pixels (their
  Proposition 4.14) instead of splitting only the critical edge, which can make the new edge cross another chain away from any hot
  pixel; only pixels not yet hot are chosen, so every round adds one and the rounds end; R2 (snap to the critical pixel) cannot occur
  in that form, since a segment crossing the critical pixel would have been routed through it, and is replaced by the nearest free
  pixel of the envelope within two pixels on the edge's span; chains are ordered by entry and then exit, so that a segment starting
  on a side two pixels share runs through them in the right order.
- **Arithmetic**: all coordinates and the pixel width on one common power of two, twice each as an integer, so pixel corners and
  centres are integers and every decision exact; the same construction as the mesher's exact crossings (`ConstrainedDelaunayMesh2`).
- **Collapses**: rings that collapse to a line or a point become segments or vanish (a ring whose edges cancel in pairs; a bowtie is
  kept), hole and region seeds that snapping swept (between an input constraint and its chain, eg inside a collapsed contour) are dropped (behaviour as in the clean-room
  answer 6, `MESHING2D_SNAP_QA.md`; no code from it), each reported in the diagnostics.
- The snapped input is meshed by the unchanged mesher; the points added for crossing and restore pixels become vertices made by the
  mesher (`ConstrainedMesh2.WithInputCount`).
- **Refinement on the grid** (`QualityMesh2.Refiner` with a `SnapGrid`; owner decision 2026-11-01, `MESHING2D_SNAP.md` section 3g):
  Ruppert's refinement (J. Ruppert, *A Delaunay refinement algorithm for quality 2-dimensional mesh generation*, J. Algorithms 1995;
  J. R. Shewchuk, *Delaunay refinement algorithms for triangular mesh generation*, CGTA 2002) with every new vertex at a pixel centre:
  the exact circumcentre rounded to its pixel, refused when the pixel holds a vertex or a constraint crosses it; subsegments split at a
  pixel centre in the envelopes of their input segments; a floor of two pixels for angle and size, as in the behaviour reported for the
  owner's fork (clean-room answer 7, behaviour only) and measured in section 3g; the refusals looked at again after each sweep with
  insertions. Grid vertices with bounded angles down to the grid's resolution: M. Bern, D. Eppstein, J. Gilbert, *Provably good mesh
  generation*, JCSS 1994 (abstract).

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
(3 + 16u)u times the sum of the magnitudes of the two products; in-circle: (10 + 96u)u times the permanent),
each used with a margin of about 3, and fall back to the exact rationals. The oracle does not call the library's predicates (it judges the
mesher). The global empty-circumcircle pass stays global: the Delaunay lemma (a triangulation whose edges are all locally Delaunay is
Delaunay; [CDS] ch. 2, [L77]) needs a triangulation of the convex hull of the vertices, while O2 asks for empty circles against all vertices
across holes and concave boundaries, where the output has no triangles; it is indexed by a 2-d tree instead.

The fast oracle (`MESHING2D_FAST_ORACLE.md`, owner 2026-11-04) follows certifying algorithms ([MMNS11]: a checker that is simpler than
the algorithm and independent of it). Its first steps only index and filter what the reference path scans, with an equivalence argument
per replacement, and `Oracle.CheckFastPathForTests` checks each one against the scan as it runs:
- `DomainEdgesNear`: a grid over the domain's edge boxes, queried with the point's box widened by the distance;
- the sources of the merge zone and of the marks: the existing source grid and tube filter;
- `GridBlocked`: vertices by exact pixel index, and a grid over the subsegments;
- `ZoneFilter` / `RadiusBounds` / `WithinZone`: the zone distance in doubles, decided both ways, with the slack argued in their docs.

All of these are own work. CGAL's `is_valid()` was considered as a concept only, and no code was read.
- [MMNS11] R. M. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 5(2), 2011.

### The `m2.snap` gates (#274)

The oracle rules for an explicit tolerance follow D14 (owner, 2026-11-01: snap rounding on a grid of pixel tol, Hobby 1999, with the restore
step of Belussi et al. 2016): every input point lands on the centre of its pixel (half-open pixels as in Hobby 1999, grid anchored at the
origin), and a surviving input slot that moved is still an input point; merged slots (aliases, Q8) do not count against a vertex bound. One rule draws on the
clean-room answers about the owner's Triangle.NET fork (`MESHING2D_SNAP_QA.md`, answer 6; behaviour only, no code): a hole marker in the
pixel of a vertex into which a contour (at least three input points) merged is dropped instead of removing the cell around it, since the fork lost
the whole domain that way. The catalogue row F-17/b tests it. Each rule has an accept and a reject self-check, each checked by mutation. With the library routine (#277): at M2 a constructed vertex
at a pixel centre within the tube of a constraint is allowed (a crossing or restore pixel of D14).
