# 2D meshing: the fast oracle

Owner decision of 2026-11-04: the fast path of the oracle (`benchmarks/Meshing2Benchmark/Oracle`) should take at most 2× the mesher's
time per run. The reference path (every test exact, every scan full) is exempt. It stays as it is, and `Meshing2OracleModesTests`
compares the two paths on the catalogue, on corrupted meshes and at extreme scales.

The approach follows certifying algorithms (McConnell, Mehlhorn, Näher, Schweitzer 2011, papers only):

1. Prove a valid planar triangulation of the domain.
2. Then run the local (constrained) Delaunay test per edge.
3. Make the input-only rounding and the Q17 rules linear with filters and indexes.

Every replacement of a scan or an exact test needs an equivalence argument. Those arguments are in this note and in the code comments.

## 1. Measurement (master c0f7de6e, 2026-11-04)

Each check is timed by `G3_ORACLE_TIMING`. The laps now also cover input, vertices, domain, indices and identity, sources and filters,
and the two Q17 parts of the segment check. One run per input, on an idle machine. The full numbers and profiles are in section 5.

| set | mesher | oracle | ratio | largest checks |
|---|---|---|---|---|
| Herne (12 runs, master mesher's outputs) | 27.2 s | 163 s | 6.0× (2.0–11.3× per run) | quality 37 %, delaunay 24 % |
| corpus (406 runs) | 4.5 s | 150 s | 34× | coverage 35 %, segments 19 %, marks 11 % |

Profiles of the two worst runs:

- **Herne 373600 q30, quality.**
  - The merge-zone test spends 45 % on exact distances. The double filter in front of them only proved "farther", but all 625 000 tests
    came out "within".
  - `GridBlocked` spends 36 % recomputing every vertex's exact pixel index, for every triangle.
- **Herne 373600 q30, Delaunay.** 97 % is in the global pass's candidate checks; needles go against every vertex.
- **Corpus earcut/water M2, coverage.** It scans all domain edges per point and per boundary edge, which is O(triangles × boundary
  edges).

## 2. Steps (1) and (2): what changed, and why each gives the same answer

The verdicts are identical before and after: the Herne reports line by line, the corpus in both modes, and the catalogue rows.
`Oracle.CheckFastPathForTests` checks every index and filter below against the scan or exact test it replaces while it runs
(`Meshing2OracleFastPathCheckTests`: the catalogue, the grid fuzz in both modes, hull domains, star polygons).

### (1) The domain's edges near a point (`DomainEdgesNear`)

**Change.** A grid over the boxes of the domain's ring edges (`BoxGrid`, the oracle's own: the oracle references nothing of the
library). It answers "the edges that may lie within √d of p" and replaces five scans of `domain.Edges()`:
- the boundary probe and `NearBoundary` in Coverage;
- the hull tests of a boundary edge in Coverage;
- the hull test of an unsourced subsegment in Marks;
- the moved-vertex minimum over the hull edges.

**Argument.**
- The distance from p to a segment is at least the distance from p to the segment's box.
- The query box is p's box (an interval around the exact point) widened by √d from above: the square root of d's upper bound, times
  1 + 1e-12, plus 1e-300.
- An edge whose box (from its ends' intervals, `SegmentBox`) misses the query box is therefore farther than √d, and fails the exact
  test that every caller still runs.
- The grid returns a superset of the boxes that meet the query box. A cell index is the floor of a monotone expression, so
  overlapping intervals map to overlapping cell ranges.
- The edges come in the order of `Edges()`, so a caller taking the first match or the minimum sees what the full scan gives.
- For the minimum: it counts only when it is within the tube, and then its edge is among those within the tube.

**Marks.** The sources that cover a subsegment come from the existing grid over the source boxes widened by the tube, queried with
the box of the subsegment's first end. This is the argument `InventedVertices` already uses: a superset in ascending order, and
`MayBeNear` drops only sources certainly farther than the tube.

### (2) Quality

- **`GridBlocked`: vertices.** It asked whether any vertex lies at the centre of the circumcentre's pixel. It now buckets the vertices
  by their exact pixel index once, instead of recomputing every vertex's index for every triangle. The keys are the same exact
  integers the scan compares, and the question is existence, so order does not matter.
- **`GridBlocked`: subsegments.** A grid over the subsegments' boxes, queried with the pixel's box in doubles from outside. A subsegment
  whose box misses the pixel fails the exact box test that is kept.
- **Merge zone, the near sources.** These come from the source grid, queried with the triangle's box, and each is decided by
  `NearSource`: the existing, tested tube filter in doubles, then the exact test. This gives the same list in the same order as the
  scan of all sources.
- **Merge zone, pair-only exits.** `apexOnBoth` depends on the pair only, and now comes before the distances. All the conditions are
  exits, so their order does not change the result.
- **Merge zone, the distance filter (`ZoneFilter`, `RadiusBounds`).** It now decides both ways.
  - The zone's radius is bounded from above and from below in doubles: the square roots of the bounds of its square, times
    1 ± 1e-12.
  - The point's distance to the source (or to the apex, a segment of length 0) is computed in doubles from the doubles of the exact
    points, or within an ulp of them. It is within 1e-12 of the largest magnitude m of the exact distance, because the projection and
    the square root round by a few ulps of m. That is the slack on either side, as before.
  - Farther if dist − slack > hi; within if dist + slack < lo; otherwise the exact test decides. This holds for magnitudes in
    [1e-140, 1e140].
  - Tests (`Meshing2OracleZoneFilterTests`):
    - points at the radius times 1 ± 10⁻² … 10⁻¹⁵, on segments and points, in the frames 2^−300 … 2^400; every decision agrees with
      the exact distance, and the filter decides many cases both ways;
    - lo² ≤ r² ≤ hi² exactly, and hi / lo < 1 + 1e-11.

### Mutants (by hand, against the oracle's tests)

19 mutants on the new code. 18 die and 1 is argued equivalent below. After the review of #376, three more mutants on the margins of the domain-edge query are argued equivalent (see the box-level checks below).

Killed by the filter tests (`Meshing2OracleZoneFilterTests`):
- the within threshold always true, or taken from the upper bound;
- the farther threshold taken from the lower bound;
- lo from the upper bound, hi from the lower bound;
- the apex call site always within;
- an undecided filter taken as within, and the exact test taken always.

Killed by `Meshing2OracleFastPathCheckTests` with the check switch on:
- domain reach 0;
- the merge-zone sources queried with one vertex's box;
- the near-source test on one vertex;
- the pixel key;
- the subsegment query of a point. Its case: a steep subsegment across a corner of the circumcentre's pixel, with a grid fine enough
  to tell (`GridBlocked_SubsegmentAcrossACornerOfThePixel`).
- the minimum over the edges within distance 0. Its case: a 400-gon hull domain with a vertex 0.09 outside an edge at 45 degrees,
  tube 0.1 (`HullDomain_BoundaryVertexOffTheHullEdge`).

Argued equivalent: a mutant of the marks' source query. The code queries the grid with the full box of the subsegment's first end;
the mutant queries with only the lower corner of that box.
- A vertex's box is an interval of about 2e-13 of its magnitude around the exact point. `BoxGrid` returns every box registered in the
  cells the query meets.
- The corner query and the full-box query therefore give different answers only when the vertex's box straddles a cell boundary and a
  qualifying source's box starts inside that strip.
- The cells are at least the extent over the square root of the number of sources, many orders of magnitude wider than the strip.

**Box-level checks** (review of #376). The check switch also tests the grids' superset property on its own,
`BoxGrid.CheckSuperset`: every box that meets the query box must be in the grid's answer. GridBlocked compares the set of subsegments
the exact box test keeps, not only the boolean. The margins of the domain-edge query (`Oracle.DistanceQueryBox`) are tested directly:
- for 3 000 random segments, horizontal segments and points, with d2 set to their exact squared distance from a point;
- in the frames 2^−500 … 2^500 (where the squares do not fit doubles the query box is refused and the caller scans every edge);
- the segment's box must meet the query box (`DistanceQueryBox_MeetsEverySegmentAtExactlyTheDistance`).

A widening from d2's lower bound with a factor below 1 fails this test. The three single margins are argued equivalent:
- no factor 1 + 1e-12;
- no 1e-300;
- d2's lower bound in place of its upper.

Each is one of several layers: d2's upper bound is padded by 1e-13 relative and 1e-300 absolute, p's box by 1e-13 of its magnitude,
and the factor. Dropping one leaves the others to cover the rounding of the square root and of the subtraction, which is far below
1e-15 relative. The mutation runs agree.

**The one intended output difference.** A hull domain without any hull edge (all input points the same) whose output has triangles.
The scan's minimum over no hull edges threw out of `Oracle.Check`. Now the boundary vertices count as not moved, and the degenerate
domain is reported as `TrianglesOnDegenerate`, on both paths (`HullDomain_WithoutHullEdges_IsReportedNotThrown`).

**Cost notes for later** (no change needed now):
- The subsegment and domain-edge grids span the whole extent of their boxes, so one far-away part coarsens all the cells.
- A box over more than 256 cells (a long diagonal) is returned by every query.

Both cost time, never correctness.

## 3. Results

| set | oracle before → after | ratio | runs with mesher ≥ 100 ms: max / median |
|---|---|---|---|
| Herne | 153.6 → 111.8 s | 5.6× → 4.1× | 11.3× → 8.1× / 1.9× |
| corpus | 151.2 → 102.3 s | 36.5× → 24.7× | 103× → 92× / 8.6× (8 runs) |
| corpus, constrained | 121.0 → 87.2 s | 20.5× → 14.8× | 31× → 16× / 7.5× (14 runs) |

Herne's worst run (373600 q30): 59.2 → 42.4 s. Of that, quality goes from 18.7 to 4.9 s, and Delaunay (25 s) is what is left.

The runs where the mesher takes under 100 ms are counted apart, by absolute oracle time. They are the large polygons at M2 and M3@0,
with quadratic scans rather than constants. For example, earcut/water M2 goes from 15.0 to 7.1 s and water-huge3 M2 from 21.9 to
13.7 s.

**Stop rule:** more than 4× is left. The numbers went to the owner (coordinator 2026-11-04) before any further step.

## 4. Next (pending the owner's decision)

- **(3) Delaunay with a completion witness.** The global pass cannot simply be replaced by the local test.
  - The Delaunay lemma is about a triangulation of the convex hull, but O2 (conforming, the default with quality) asks for empty
    circumcircles against all vertices, also across holes and concave boundaries, where the output has no triangles.
  - Plan: the oracle builds its own completion of the output to the convex hull of the used vertices (independent of the mesher), and
    checks that the union is a valid triangulation of the hull. It then checks every edge locally:
    - output edges with the excuses O2 allows;
    - completion edges by Lawson's criterion, after flipping the completion only.
  - By the lemma every circumcircle of an output triangle is then empty of all vertices.
  - The argument, and what the excuses (merge zones, O11-unsplittable, grid-unsplittable) need in it, is written here before the code.
  - In the constrained-Delaunay modes (cq, M2), the local test is exact by the constrained Delaunay lemma (Chew 1989; Cheng–Dey–Shewchuk
    2012, ch. 2) once validity is proven.
- **(c) The corpus items:** segments (poly2tri/debug2: 6.8 s for 10 000 ring edges), conformity and domain.

## 5. Preparation note

The measurement plan, the profiles and the options for (3) as prepared before the work:

- The global Delaunay pass in `Delaunay()` already documents why the lemma alone does not cover O2. Options considered:
  - (a) the mesher's full hull triangulation as a certificate;
  - (b) a completion built by the oracle;
  - (c) keep the pass and index it better.

  (b) is chosen, for the oracle's independence (coordinator 2026-11-04).
- **Herne per run.** Mesher → oracle (master): 1.6 → 3.9, 1.9 → 5.6, 3.7 → 29.2, 5.3 → 59.4, 0.9 → 2.9, 1.1 → 3.0, 1.3 → 2.6, 1.7 → 4.9,
  2.8 → 12.4, 5.2 → 35.0, 0.7 → 1.9 and 1.2 → 2.6 s. Base and cq, tiles 371800 / 373600 / 381000, q20 / q30.
- **Literature:**
  - McConnell, Mehlhorn, Näher, Schweitzer, *Certifying algorithms*, Computer Science Review 5(2), 2011;
  - the constrained Delaunay lemma (Chew 1989; Cheng–Dey–Shewchuk 2012, ch. 2);
  - CGAL's `is_valid()` concept, as a black box only (no code read).

## 6. Step (3): the global Delaunay pass with a witness (owner decision 2026-11-04, A+C; argument written before the code)

Targets from the owner's decision:
- the fast path at most 2× the mesher per run where the mesher takes at least 100 ms;
- for smaller runs, at most 1 s of oracle time per run;
- stop rule: if (3) does not bring Herne to at most 2.5× in total, stop and go to grading step 2.

### 6.1 What the pass decides

`Delaunay()` runs the global pass when the milestone is M1, or when the mesh is conforming (O2) and no source is locked:
- every output triangle t is tested against every used vertex v, the vertices of the output's triangles;
- t has a violation when v lies strictly inside t's circumcircle (in-circle sign > 0) and no excuse applies (`ExcusedOffSegment`,
  `Zone`, `HiddenByGridUnsplittable`, `HiddenByO11Unsplittable`; the O11 excuses are counted per triangle);
- one report per triangle, in the order of the triangles;
- the fast path indexes the candidates with a 2-d tree, but needles and nearly degenerate triangles go against every vertex. That is
  most of the pass's time on Herne (section 1).

The local test per edge, which the plan named, cannot replace the pass by itself. The Delaunay lemma is about a triangulation of the
convex hull of the vertices, while the output triangulates only the domain, and O2 asks for empty circles across holes and concave
boundaries too. Hence a witness.

### 6.2 The witness: the oracle's own Delaunay triangulation of the used vertices

The oracle builds a triangulation W of the used vertices U itself:
- incremental insertion with exact predicates (the oracle's `Exact`, filtered); nothing of the library and nothing of the mesher's
  output beyond U;
- certified before it is used:
  - **(a) it triangulates conv(U):** every triangle positive; no directed edge in two triangles, so every edge is in at most two, in
    opposite directions; the edges in one triangle (the boundary), followed from a corner of conv(U), run along conv(U)'s edges in
    order, each on the current hull edge and forward, back to the start after the last hull edge, and that walk takes every boundary
    edge (no vertex starts two boundary edges); the vertices of W are exactly U. Exact: orientations, and coordinates compared along
    a hull edge;
  - **(b) it is Delaunay:** every edge in two triangles is locally Delaunay. The fourth vertex is not strictly inside the circumcircle
    (in-circle sign ≤ 0).

**(a) makes W a triangulation of conv(U).** Positive triangles whose edges pair up in opposite directions cover the plane with a
locally constant number of layers, away from the vertices and edges. The boundary edges lie on conv(U)'s boundary, so outside conv(U)
that number is 0. The boundary is conv(U)'s boundary, run once, so crossing it changes the number by exactly 1: it is 1 inside. A
vertex of U in the interior of another triangle or edge would put a second layer there. (A first version compared the boundary's
cross sum with the hull's, exactly, after testing every boundary edge against every hull corner: quadratic in convex position, as
the coordinator's review found. The walk is linear and gives the same conclusion.) (This is the standard degree argument of a triangulation
checker; own write-up, and [MMNS11] is cited for the approach.)

**(b) with (a) makes W Delaunay** by the Delaunay lemma ([CDS] ch. 2; [L77]): every circumcircle of a triangle of W has no vertex of
U strictly inside. If W fails (a) or (b), or cannot be built (U has fewer than three vertices not on one line, or a step limit is
reached), the pass runs as before, over all triangles. The witness is then not used at all, so a defect in building it can cost
time, never a verdict.

### 6.3 The reduction

Let S be the output triangles that are not triangles of W, compared as unordered vertex triples.
- **A triangle of the output that is in W has no vertex of U strictly inside its circumcircle** (6.2). The global pass reports
  nothing for it, and counts no O11 excuse for it, since an excuse is counted only for a vertex inside.
- **So the pass over S alone gives the same reports, in the same order (S kept in the triangles' order), and the same counts.** The
  pass on S is the existing one: the 2-d tree, the exact in-circle test, every excuse.

What S holds:
- **real violations**, which are never in W: some vertex is inside;
- **excused ones**, likewise;
- **cocircular ties**: W picks one of the possible diagonals, and an output triangle with a cocircular fourth vertex, but none inside,
  may not be in W. Common on the snap grid, where squares of pixel centres are cocircular. The pass checks them exactly and finds
  nothing, as it would.

Three conditions the step "in W, so empty" rests on (coordinator 2026-11-04), each held by the code:
1. **U is exactly the vertex set the pass tests against.** `Delaunay()` computes `used`, the vertices of the output's triangles, once,
   and gives that same array to the witness and to the pass. Unused input points and isolated vertices are in neither.
2. **The same predicate and the same strictness.** The certificate and the pass both call the oracle's filtered exact `InCircle` on
   the output's doubles. The certificate demands sign ≤ 0 for every interior edge, which gives "no vertex strictly inside"; the pass
   reports, and counts an excuse, only at sign > 0. A cocircular fourth vertex is therefore neither; an output triangle whose
   diagonal differs from W's at a tie lands in S and is checked there.
3. **Triangles are identified by their sorted vertex ids, and no two used vertices share coordinates.** Sorting removes rotation and
   orientation. Two vertices at the same point stop the build ("two vertices coincide": the walk ends at a triangle with the other
   vertex as a corner), and a degenerate triangle in W fails the certificate's positivity, so the pass then runs in full
   (`DuplicateCoordinates_FallBackToTheFullPass`).

A wrong W cannot make the reduction accept a violation, because only a certified W is used. A certified W that is still unlucky
(many ties) only makes S larger.

### 6.4 What stays and how it is checked

- **The reference path is unchanged**: all exact, the full pass.
- **`Meshing2OracleModesTests`** keeps comparing the two paths on the catalogue, on corrupted meshes and at extreme scales.
- **Check mode** (`Oracle.CheckFastPathForTests`): the full pass and the pass on S run into scratch reports, and their
  `NotDelaunayGlobal` reports (in order), the merge-zone count and the O11 count must agree. Every triangle of W is tested against
  every vertex of U (the lemma's conclusion, directly).
- **Mutants:** on the certificate (each of the conditions of (a) and (b)), on the membership test (triples), and on the fallback.
  They must die: an uncertified or wrong W must be caught by the check mode, or by a verdict difference on corrupted meshes. Two
  conditions of the walk are defensive and have no mutant-killing case: "forward" along a hull edge (a boundary edge back along a
  hull edge needs a triangle outside the hull, whose edges fail the walk anyway) and "the corner reached is the hull corner's id"
  (two used vertices at one point stop the build before the certificate).
- **Corrupted meshes and witnesses** (`Meshing2OracleDelaunayWitnessTests`):
  - a constrained Delaunay mesh with a vertex inside a circumcircle across a thin hole (the case the local test misses), judged as
    conforming, is rejected with the witness, as on the reference path;
  - a flipped edge in a Delaunay triangulation is reported for both its triangles;
  - a witness made wrong in each way the certificate tests (a triangle reversed, duplicated or dropped, an interior vertex dropped,
    an edge flipped, an ear at the hull dropped, and a second layer on the half and the whole sides of a square) is refused for that
    reason, and the verdicts stay the full pass's;
  - a witness that throws while it is built or certified falls back to the full pass (`try`/`catch`: a defect of the witness costs
    time, never a verdict);
  - invariance: random points under every similarity of `Similarity.All()` and the scales 2^-500 and 2^200 use the witness, with the
    full pass's reports; under the exact scales the report is the identity's;
  - with the certificate skipped, a witness holding the output's non-Delaunay triangles hides their violation: the certificate is
    what the reduction rests on;
  - the witness is not built from the output's triangles at all, only from U, so a mesh that is no triangulation reaches it like any
    other.

### 6.5 Cost

- **Building W:** O(|U| log |U|) expected with a randomized insertion order: rounds of doubling size over a fixed pseudo-random
  permutation, each round sorted along a Morton curve of the coordinates' ranks (the biased randomized insertion order, [ACR]; own
  code), and a visibility walk from the last new triangle for the point location.
- **The certificate:** linear (the boundary walk; a hash of the directed edges, with a mixing hash).
- **Membership:** a hash of the triples.
- **The pass:** over S only.

On a valid conforming Herne mesh, S is the ties and the excused triangles. The aim is for the pass to drop from 25 s (373600 q30) to
the cost of building W: about the mesher's own Delaunay time, a few hundred ms.

### 6.6 Sources

- [CDS] S.-W. Cheng, T. K. Dey, J. R. Shewchuk, *Delaunay Mesh Generation*, CRC Press 2012: ch. 2 (the Delaunay lemma), ch. 3
  (incremental insertion, ghost triangles, conflict sets).
- [L77] C. L. Lawson, *Software for C1 surface interpolation*, 1977 (the local Delaunay criterion).
- [ACR] N. Amenta, S. Choi, G. Rote, *Incremental constructions con BRIO*, SoCG 2003 (the insertion order in rounds).
- [MMNS11] R. M. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 5(2), 2011.
- The hash of the edge keys in the certificate: the SplitMix64 finalizer (G. L. Steele, D. Lea, C. H. Flood, *Fast splittable
  pseudorandom number generators*, OOPSLA 2014).

The keys are those of `MESHING2D_PROVENANCE.md`. The witness is the oracle's own code, written from these sources; nothing of the
mesher (`DelaunayKernel2`) or of the library is used or read for it.

### 6.7 Measured (2026-11-04, Herne, the step-1 mesher's outputs)

Oracle seconds per run, fast path, `G3_ORACLE_TIMING`, before (master after #376) → after this step. The cq runs ask for constrained
Delaunay only and have no global pass.

| run | mesher s | delaunay s | oracle s | oracle / mesher |
|---|---|---|---|---|
| base 371800 q20 | 1.6 | 0.6 → 0.7 | 3.0 → 3.3 | 1.9× → 2.1× |
| base 371800 q30 | 1.9 | 0.9 → 0.8 | 4.3 → 4.4 | 2.3× → 2.3× |
| base 373600 q20 | 3.7 | 8.9 → 6.5 | 20.4 → 18.5 | 5.6× → 5.1× |
| base 373600 q30 | 5.3 | 25.2 → 10.3 | 42.4 → 28.9 | 8.1× → 5.5× |
| base 381000 q20 | 0.9 | 0.3 → 0.5 | 1.5 → 1.5 | 1.7× → 1.7× |
| base 381000 q30 | 1.1 | 0.4 → 0.4 | 1.9 → 1.9 | 1.7× → 1.7× |
| cq 371800 q20 | 1.3 | 0.0 → 0.0 | 1.7 → 1.5 | 1.3× → 1.1× |
| cq 371800 q30 | 1.7 | 0.0 → 0.0 | 3.9 → 3.8 | 2.3× → 2.2× |
| cq 373600 q20 | 2.8 | 0.0 → 0.0 | 9.1 → 9.3 | 3.3× → 3.4× |
| cq 373600 q30 | 5.2 | 0.1 → 0.1 | 21.4 → 21.7 | 4.1× → 4.1× |
| cq 381000 q20 | 0.7 | 0.0 → 0.0 | 0.7 → 0.9 | 1.1× → 1.4× |
| cq 381000 q30 | 1.1 | 0.0 → 0.0 | 1.7 → 1.6 | 1.5× → 1.4× |

- **In total 111.8 → 97.3 s, 4.1× → 3.6× of the mesher.** The stop rule (at most 2.5×) is not met: the work goes on with grading
  step 2, as decided.
- **Where 373600 q30's delaunay time went.** The witness itself: 0.45 s to build, 0.9 s to certify, for 122721 vertices; S is 4367
  of 245104 triangles. The pass on S took 24.6 s at first, almost all in `HiddenByGridUnsplittable`: `Behind` tested every
  subsegment exactly, 14 ms per call. For a vertex it now asks the subsegment grid (21 → 3 s).
- **What remains.** 2.9 million in-circle hits of S's triangles, nearly all excused by the merge zone; `ExcusedOffSegment` runs
  first on each, by the order the counts depend on (3.2 s). The local test per edge is 1.9 s.
- **An early version's certificate took 220 s:** its edge keys `(x << 32) | y` used `long`'s own hash, x ^ y, which collides for
  every pair with the same xor. A mixing hash took it below a second.
- **Verdicts unchanged:** the twelve Herne reports and the corpus verdicts (both modes) are those of master; one corpus run below the
  judge limit now (water-huge3 M3@25, ok) only because the step-1 mesher makes fewer triangles there.
