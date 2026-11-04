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

19 mutants on the new code. 18 die and 1 is argued equivalent below.

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

Argued equivalent: the marks' source query with the corner of the vertex's box instead of the box.
- A vertex's box is an interval of about 2e-13 of its magnitude around the exact point. `BoxGrid` returns every box registered in the
  cells the query meets.
- The corner and the box therefore give different answers only when the box straddles a cell boundary and a qualifying source's box
  starts inside that strip.
- The cells are at least the extent over the square root of the number of sources, many orders of magnitude wider than the strip.

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
