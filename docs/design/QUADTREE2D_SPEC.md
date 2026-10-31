# 2D Spatial Index (Quadtree) — Behavioural Specification

Status: owner decisions and first-version implementation brief approved (October 2026). Written as the *specification side* of a
clean-room process. The owner has an existing 2D quadtree whose origin is under a licence that is
not compatible with this library (CPOL), and which lives in an employer code base. This document
describes **observable behaviour, test situations and performance targets only**. It says nothing
about how the existing code is structured or works internally. The new implementation must be
written from the published literature on spatial indexing (region quadtrees, loose quadtrees,
bounding-volume / R-tree style indexes, segment–box clipping), not from the existing code.

After the new implementation exists, both will be run side by side on the same data (section 5).

Conventions:

* "Item" = an object stored in the index, represented by its axis-aligned bounding box (AABB).
  Item kinds used by the owner: **triangles** (3 points with x, y, z), **edges** (2 points with
  x, y, z), **points** (x, y, z).
* All geometry is 2D in x/y; z is carried along and only used for interpolation results.
* Two boxes intersect if and only if their x-ranges overlap and their y-ranges overlap, with
  closed intervals (shared boundaries count).
* Priority labels: **[USED]** = used by the owner's code; **[TESTED]** = covered by the owner's
  tests; **[?]** = see open questions.

---

## 1. Core index

### 1.1 Construction [USED]

* Build from a list of items in one call (bulk build). Items keep a stable integer id = their
  position in the input list; later inserted items get the next ids.
* The index extent is the AABB of all initial items.
* Empty input → valid empty index; every query returns nothing.
* Degenerate input must work: a single item; all items identical; all items on one line
  (zero-area extent); very small extents (e.g. smaller than 1 × 1 and smaller than 20 × 20 units); very elongated extent (e.g. 20 km × 5 km);
  projected coordinates (UTM x ≈ 3e5–8e5 or Gauss-Krüger x ≈ 2.5e6, y ≈ 5.6e6–5.8e6) and negative coordinates.
* Duplicate items (same coordinates, different objects) are distinct items; queries must return
  each of them (results are compared by object identity, not by coordinates).
* Bulk build should use multiple cores for large inputs (e.g. hundreds of thousands of items) and
  must give the same query results as a sequential build.

### 1.2 Rectangle query [USED]

* Input: an axis-aligned rectangle (may have zero width and/or height).
* Output: **exactly** the items whose AABB intersects the rectangle (inclusive). No misses, no
  false positives beyond the AABB test.
* Must be correct for rectangles lying exactly on 'round' coordinates (integer and half-integer values, the extent's centre and quarter lines, multiples of powers of two), on the
  index border, covering everything, and completely outside.
* Result order is unspecified.
* A reusable result buffer overload should exist so repeated queries do not allocate (owner calls
  it 10 000s of times in loops).

### 1.3 Segment (line) query [USED]

* Input: a segment a–b (may have length 0, may be axis-parallel, either direction).
* Output: a **conservative** candidate set: every item whose AABB is touched by the segment must
  be returned. Items whose AABB lies within 2e-4 units of the segment may also be returned; items
  farther away must not be.
* Reference definition for tests: segment–box clipping (e.g. Liang–Barsky) against the item AABB
  enlarged by 2e-4.

### 1.4 Insert [USED]

* Insert one item after construction. It is found by all subsequent queries.
* An item outside the index extent (more than 0.01 units beyond it on any side, also if only one
  side is outside) → error; the index must stay **unchanged** (no partially inserted state, ids of
  later items not shifted). Items within 0.01 beyond the extent are accepted and found.
* Insert into an empty index → error (extent is empty). See Q-2 on whether a growing extent is
  wanted instead.

### 1.5 Remove [USED]

* Remove by item (first item equal to the given one in insertion order) or by id.
* Removing an item that is not present, or removing the same item twice → no change, no error.
* Count decreases only for actual removals.
* After removal the item is never returned again; ids of other items do not change.
* Remove everything, then insert new items → index works normally.

### 1.6 Traversal with early stop [USED]

* A generic search: the caller supplies (a) a test "can this region contain a hit?" and (b) a
  per-item test that may report "hit, stop now". The search visits only regions that pass (a) and
  stops at the first item for which (b) asks to stop.
* With "stop on first hit", exactly one result is returned if any exists, and it must be one of
  the results of the full query; which one is unspecified.
* The per-item callback mechanism must not allocate per query.

### 1.7 Properties [USED]

* Number of live items, extent rectangle, construction settings.

### 1.8 Thread safety [USED]

* Concurrent **queries** on one index from many threads must be safe and give the same results as
  sequential queries, including nested queries (a query started from inside another query's
  callback on the same thread).
* Insert/Remove are not required to be concurrent with queries.

---

## 2. Item-specific queries

These are convenience queries on top of the core index. Their per-item geometric rules are part of
the specification; where the exact rule is still open, it is marked Q.

### 2.1 Triangle index: height at a point [USED]

* Height query: given a point p and a tolerance (default 0.01), returns the linearly interpolated z
  of a triangle that contains p, or "none".
* Containment is decided on **barycentric coordinates** (relative to the triangle, not absolute
  distance): let (u, v) be the barycentric weights of p for the 2nd and 3rd vertex,
  * *exact hit*: u ≥ −1e-9, v ≥ −1e-9, u + v ≤ 1 + 1e-9, u ≤ 1 + 1e-9, v ≤ 1 + 1e-9;
  * *tolerance hit*: the same with 1e-9 replaced by the tolerance (default 0.01).
* Degenerate (zero-area) triangles never produce a hit.
* If any exact hit exists, the result comes from an exact hit; only if there is none, a tolerance
  hit is used.
* Multi-height query (optionally stopping after the first result): all exact-hit heights, or if
  none, all tolerance-hit heights.
  Overlapping triangles (e.g. two surfaces) give several heights.
* Tolerance hits are guaranteed for triangles whose AABB is within 0.001 units of p; triangles
  farther away may be ignored even if p is within the barycentric tolerance (see Q-9).
* Point exactly on a shared vertex or edge of several triangles → at least one height (when stopping after the first result
  exactly one), all equal for a consistent TIN.
* Point outside all triangles (beyond tolerance) → none.

### 2.2 Triangle index: relevant triangles at a point [USED]

* Returns (triangle, height) for every triangle containing p within an absolute-or-relative
  tolerance `eps` (default 1e-9) — see Q-3 for the exact containment rule; when stopping
  after the first result only one.
* With a positive tolerance, points slightly outside a triangle are reported.

### 2.3 Edge index [USED]

Let the index hold edges; let `eq(p, q, eps)` denote "points p and q coincide in x/y within eps"
(definition: see Q-4).

| Query | Result |
|-------|--------|
| identical edges (e) | All stored edges equal to e **in either direction**, compared with a fixed tolerance (see Q-5). |
| neighbour points (p, eps) | For every stored edge whose **start point** coincides with p: its end point. |
| points near (p, eps) | Every (edge, endpoint) pair where the edge's start or end point coincides with p (both endpoints can be reported for the same edge only if both coincide). |
| relevant edges (e, eps; options: snap tolerance eps2, default eps; "also report touching hits", default off; "take z from the query edge", default on) | Edges intersecting e, with the intersection point. Identical edges (either direction) are never reported. Without "touching hits": edges that only touch e at an endpoint (an endpoint of e coincides with an endpoint of the other edge, or the intersection coincides with any endpoint within eps) are **not** reported. With "touching hits": such touching edges are reported too; if an endpoint of e coincides with an endpoint of the other edge, the reported point is that endpoint of e itself. If the intersection lies within eps2 of an existing endpoint, the reported point has that endpoint's x/y; its z comes from the query edge (default) or from the other edge, linearly interpolated along that edge. |
| intersections (e, eps) | All edges whose segment intersects e (including touching at endpoints), except identical edges, with the intersection point (z interpolated along e). |
| split edges (p, eps) | Edges that p lies on (within eps), excluding edges that have p as an endpoint. |
| split edges projected (p, eps) | Edges whose perpendicular projection of p falls within the edge and whose distance to p is ≤ eps, excluding edges with p as an endpoint. With "stop after the first result", stop only at a real hit (distance ≤ eps), never at the first candidate. |

Completeness: point-based edge queries are complete for edges whose AABB lies within
max(0.001, 10·eps) of p; edge-based queries are complete for edges whose AABB lies within eps of
the query edge's AABB (see Q-9).

Worked example (owner's synthetic test, must hold exactly; all variants of the index):

* Edges: A (0,0,z0)→(10,0,z10); B (5,−5,z100)→(5,5,z200); C (10,0,z10)→(10,10,z20);
  D (3,−2,z50)→(3,0.0005,z60); E (10,0,z10)→(0,0,z0) (A reversed); F (20,20)→(21,21).
* relevant edges(A, 0.001) = {B} with point (5, 0, z 5); with z from the other edge: z 150.
* relevant edges(A, 0.001, touching hits) = {B, C, D}: B at (5,0,5); C at A's end point (the very same
  point object); D at (3, 0.0005) with z 3 (z from A) — or D's end point itself when z is taken
  from the other edge.
* intersections(A, 0.001) = {B (5,0,5), C (10,0,10), D (x = 3, y ≈ 0 within 0.001)}.
* split edges((5,0), 0.001) = {A, B, E}; at (10,0) → empty; far away → empty.
* split edges projected((5, 0.0004), 0.001) = {A, B, E}; at (5, 0.01) → {B}; at (10,0) → empty.
* neighbour points((10,0), 0.001) = {end of C, end of E}.
* points near((10,0), 0.001) = {(A, A.end), (C, C.start), (E, E.start)}.
* identical edges(A) = {A, E}; identical edges(B) = {B}.

### 2.4 Point index [USED]

* `neighbour points(p, eps)`: all stored points coinciding with p within eps (Q-4).
  Example: stored (1,1), (1.0005,1), (1,1) [duplicate object], (5,5), (1.01,1); query (1,1),
  eps 0.001 → the first three; query (3,3) → empty.
* Many identical points (e.g. a quarter of all points on a few positions) must work.

---

## 3. Known defects of the existing implementation (must not be reproduced)

| # | Situation | Wrong behaviour | Expected |
|---|-----------|-----------------|----------|
| D-1 | Rectangle query whose border lies exactly on integer or half-integer grid lines of a regular grid TIN. Generator: regular triangulated grid with integer (or half-integer) coordinates, rectangles on grid lines; also at UTM offsets and with negative coordinates. | Items whose AABB only touches the rectangle border from outside were missing (241 of 768 queries on an integer grid TIN). | Exact AABB result. |
| D-2 | Insert of an item outside the extent. | Count / ids affected although the insert was rejected; all later ids shifted; later items not found or wrong ones returned. | Reject, index unchanged. |
| D-3 | "Split edges projected" with stop-on-first-hit, where the first candidate edge is near p but farther than eps from it. | Search stopped at that non-hit; real hits missed. | Stop only at a real hit. |
| D-4 | Nested queries on the same thread (a query inside another query's callback). | Interfered with each other. | Independent. |
| D-5 | Large index, small queries. | Bytes allocated per query grew with index size (large allocations even for tiny queries). | Allocation independent of index size (zero with a reused buffer). |
| D-6 | Long thin triangles crossing many axis-aligned 'round' lines (the extent's centre and quarter lines, multiples of various powers of two), queried with small rectangles that only hit one end. | (Risk tested by the owner.) | Found from both sides. |
| D-7 | Removing the same item twice, in particular a large item spanning much of the extent or crossing its centre lines. | (Risk tested.) | Second removal is a no-op; count correct. |

---

## 4. Non-functional requirements

### 4.1 Data sizes

* Typical: TINs with ~1 000 000 triangles and ~1 500 000 edges over 5 × 5 km to 20 × 20 km
  (terrain models, laser-scan derived), UTM/Gauss-Krüger coordinates.

### 4.2 Performance targets (owner's existing implementation, fastest configuration; same machine for both)

Benchmark data set (synthetic, owner-written, reproduce exactly):

* Grid of 708 × 708 cells, cell size 7.07, origin (2 539 000, 5 640 000). Interior grid points
  jittered uniformly by ±0.15 · cell in x and y (border points not jittered); z uniform in [0,100).
  Two triangles per cell: (p[i,j], p[i+1,j], p[i+1,j+1]) and (p[i,j], p[i+1,j+1], p[i,j+1]).
  ~1 002 528 triangles, shuffled. Seeded random generator (seed 1); exact reproduction of the
  random stream is not required, the distribution is.
* 10 000 query points uniform over the grid extent; rectangles 20 × 20 anchored at those points;
  segments from each point to a point offset by uniform ±100 in x and y.

| Operation | Target (per call) |
|-----------|------------------|
| Bulk build, 1 M triangles | ≤ ~100–260 ms |
| Height at a point | ≤ ~2.5 µs |
| Rectangle query 20 × 20 | ≤ ~5.7 µs |
| Segment query (≤ ~140 units long) | ≤ ~21 µs |
| Memory for 1 M triangles | ≤ ~67 MB (index only) |

(Measured by the owner on their hardware; a simple reference implementation took ~70–140 µs per
height query. Treat these numbers as the bar to meet or beat in the side-by-side comparison, not as absolute
limits.)

### 4.3 Platforms

* .NET Framework 4.8 and .NET 8+.
* No allocation per query in the hot paths (height, rectangle with reused buffer, segment with
  reused buffer).

### 4.4 Determinism

* Same input → same results (as sets). Order of results may differ between implementations and is
  not part of the contract.

---

## 5. Side-by-side comparison (plan)

1. A small adapter interface over both implementations: build(items), rectangle query, segment
   query, height, the edge and point queries of section 2, insert, remove, count.
2. Correctness: for every query of a scenario, both result **sets** must be equal (by object
   identity). For the segment query, compare against the reference definition of 1.3 instead,
   since the 2e-4 margin may differ.
3. Scenarios: the benchmark grid of 4.2; random triangles (mostly small, some large, some
   degenerate, some duplicated, some exactly on the extent's centre lines); an integer grid TIN
   (D-1) at offsets 0, UTM and negative; long thin triangles over 'round' lines (D-6);
   insert/remove sequences (every third item removed, then re-inserted); parallel queries;
   the worked example of 2.3.
4. Performance: BenchmarkDotNet, both implementations in the same process run, alternating runs,
   on .NET Framework 4.8 and .NET 8, on the owner's machine. Report build time, per-query time and
   allocated bytes.
5. The existing implementation can only be built in the owner's environment; the comparison
   therefore runs there.

---

## 6. Open questions for the owner

* **Q-1** Should the new index expose more than one structure or tuning option, or is one
  well-performing variant enough?
* **Q-2** Fixed extent (insert outside → error, as today) or an extent that grows on insert?
* **Q-3** "Relevant triangles": is the tolerance meant in barycentric (relative) or absolute
  distance terms? (The current rule is not precisely documented.)
* **Q-4** Point coincidence `eq(p, q, eps)`: per axis (|dx| ≤ eps and |dy| ≤ eps) or Euclidean
  distance ≤ eps? Inclusive?
* **Q-5** "Identical edges": which tolerance (today edges count as identical when their coordinates agree at a resolution of about 0.01; the exact rule is to be decided)?
* **Q-6** Are the item-specific queries (section 2) wanted in geometry3Sharp at all, or only the
  core index (section 1) plus height interpolation?
* **Q-7** Which item types should be supported natively (triangles, edges, points, arbitrary
  AABBs)? geometry3Sharp already has 2D spatial structures; should the new index replace,
  complement or reuse them?
* **Q-8** Nearest-neighbour or k-nearest queries: not provided today; wanted?
* **Q-9** Should tolerance/proximity queries be complete for every item satisfying the geometric
  rule, instead of only within the windows stated in 2.1 and 2.3?

---

## 7. Owner decisions (October 2026)

These answer the open questions of section 6. Where a decision conflicts with an earlier section, the
decision wins.

| Q | Decision |
|---|----------|
| Q-1 | **One structure: a loose quadtree** (the owner measured it as the faster variant). No further tuning options. |
| Q-2 | The extent **grows** on insert; inserting into an empty index works. The error case of 1.4 drops out; D-2 still applies in the sense that a failed operation leaves the index unchanged. |
| Q-3 | Tolerances are **absolute distances** in coordinate units (distance from p to the triangle in x/y). "Inside the triangle" (exact hit) is decided with **exact orientation predicates**, not a 1e-9 barycentric threshold. This also applies to the height query of 2.1; the side-by-side comparison checks tolerance cases against this definition instead of the old implementation. |
| Q-4 | Point coincidence is **Euclidean** distance ≤ eps, inclusive (rotation invariant). |
| Q-5 | Identical edges: both endpoints coincide per Q-4, in either direction; the tolerance is a parameter. |
| Q-6 | In geometry3Sharp: the core index (section 1), the height query, and **general** edge and point queries (segment intersections, point on segment, nearby points). Domain-specific variants (eg "relevant edges" with touching hits and z source options) stay with the caller, built on the traversal of 1.6; the worked example of 2.3 becomes a test of that helper layer. |
| Q-7 | The index **complements** the existing structures (`PointHashGrid2d`, `SegmentHashGrid2d`, `TriangleBinsGrid2d`, `Polygon2dBoxTree`); nothing is replaced. It stores AABBs with integer ids; triangles, edges and points are thin adapters (eg over `DMesh3`, `DGraph2`, point lists). |
| Q-8 | Nearest-neighbour and k-nearest queries are **in the first version**. |
| Q-9 | Tolerance and proximity queries are **complete** for every item satisfying the geometric rule; the windows stated in 2.1 and 2.3 drop out. |

## 8. Approved first-version implementation brief

Tier L. Implement the core and point, segment and triangle adapters in this topic; do not replace
existing indexes, modify exact arithmetic, use the old implementation, or implement domain-specific
edge helpers. Sources are this specification and published loose-quadtree / spatial-search literature.

* `LooseQuadtree2d` owns AABB snapshots with stable, monotonically increasing insertion IDs.
  Construction accepts `IReadOnlyList<AxisAlignedBox2d>` and `bool parallelBuild = true` (execution
  policy, not a structure tuning option). The empty constructor is supported. Fixed internal
  looseness, bucket capacity and depth limit are exposed as read-only construction properties.
* `Count`, `Extent`, `Insert(box) -> int`, `Remove(int) -> bool`, `Remove(box) -> bool`,
  `GetBounds(int) -> AxisAlignedBox2d`, `ContainsId(int) -> bool`. Extent grows on insertion;
  removal does not shrink it. Value removal removes the first matching live snapshot.
* `QueryRectangle(box, List<int>)` and `QuerySegment(a, b, List<int>)` clear the supplied result
  list and fill it with IDs. Segment broad-phase uses the closed AABB enlarged by 0.0002.
* `Traverse<TVisitor>(ref TVisitor)` where `TVisitor : struct, IQuadtreeVisitor2d` uses
  `bool TestRegion(AxisAlignedBox2d)` and `bool Visit(int)`; `Visit` returning true stops traversal.
  Traversal returns true iff stopped. No shared query scratch state; nested queries are supported.
* `FindNearest(point, double maxDistance = double.PositiveInfinity) -> int` returns -1 if empty
  or no hit; `FindNearestK(point, int k, List<int>, double maxDistance = double.PositiveInfinity)`
  returns distance-ordered IDs, ties by ID. Core distance is Euclidean distance to the closed AABB.
  Generic overloads accept `ref TDistance` before the optional maximum distance, constrained to
  `struct, IQuadtreeDistance2d` with `double Distance(int id)` for adapter geometry distances;
  this distance must be at least the point-to-AABB distance. k = 0 clears results; negative k errors.
* `PointQuadtree2d`, `SegmentQuadtree2d`, `TriangleQuadtree2d` own snapshots supplied respectively
  as `IReadOnlyList<Vector3d>`, `IReadOnlyList<Segment3d>`, `IReadOnlyList<Triangle3d>` (optional
  parallel-build flag). They expose Count, Extent, GetItem(id), Insert(item), Remove(id), Remove(item),
  ContainsId(id), core rectangle/segment queries and traversal, and geometry-distance nearest queries.
  They do not automatically follow edits to source meshes or lists.
* Point adapter: `QueryNearby(Vector2d point, double tolerance, List<int> results)`.
  Segment adapter: `QueryPoint(Vector2d point, double tolerance, List<int> results,
  bool excludeEndpoints = false, bool stopAfterFirst = false)` and
  `QueryIntersections(Vector2d a, Vector2d b, List<int> results)`; intersections include touching,
  collinear overlap, duplicate and zero-length segments. Endpoint exclusion uses the same tolerance.
  Callers obtain geometry via GetItem; domain-specific z/snapping policies remain outside this API.
* Triangle adapter: `QueryHeights(Vector2d point, List<QuadtreeHeightHit2d> results,
  double tolerance = 0.01, bool stopAfterFirst = false)` and
  `TryGetHeight(Vector2d point, out double height, double tolerance = 0.01)`.
  Hits have `int Id`, `double Height`. Exact containment uses existing exact orientation predicates;
  exact hits take precedence over all tolerance hits. Tolerance is absolute XY distance to the
  triangle and is complete. Degenerate triangles never yield heights. Tolerance hits extrapolate
  the affine height plane (not a clamped boundary height).
* Reject non-finite input coordinates/bounds, inverted boxes, negative/NaN tolerances and invalid
  query arguments before modifying index state. Positive infinity is allowed only as nearest-query
  maximum distance. Failed insertion does not consume an ID. Invalid GetItem/GetBounds IDs throw.
* Query lists belong to the caller and must not be shared between concurrent queries. With sufficient
  list capacity, hot rectangle, segment, nearby-point and ordinary height queries allocate no managed memory.
  Owner clarification: robust numeric fallbacks for extreme or nearly degenerate geometry may allocate;
  preserving correctness takes precedence over zero-allocation in these cases. A height outside the finite
  double range throws `ArithmeticException`, never returns a successful non-finite height. On a query
  exception a caller-owned result buffer may contain partial results; the index is unchanged.
  Geometry-distance interpolation must not claim exact construction; orientation classification is exact.

Acceptance: independent spec-derived tests for D-1, D-2, D-4 through D-7; D-3 via segment point
queries; brute-force query-set and nearest oracles; growth/removal, degenerate and duplicate data;
parallel/serial and nested queries; all applicable Similarity.All() transformations (XY projection
for the 2D domain, distances scaled); mutation checks. Benchmark procedural data from section 4.2
against a brute-force baseline, recording commands and observed outputs, without claiming the owner's
hardware targets or side-by-side compatibility have been verified. Review spatial pruning and numeric
bounds independently. Stop after the complete first version, tests, benchmark and independent review;
no push or merge is part of this task.

## 9. Implementation record

`spatial/LooseQuadtree2d.cs`, `PointQuadtree2d.cs`, `SegmentQuadtree2d.cs`, `TriangleQuadtree2d.cs`, and the internal `QuadtreeGeometry2d.cs` and `QuadtreeExactQuery2d.cs`. Sources: [SOURCES.md](../SOURCES.md). Usage: [OPERATIONS.md](../OPERATIONS.md). Numbers: [benchmark README](../../benchmarks/QuadtreeBenchmark/README.md).

### Where the code differs from section 8, and why

Independent reviews on 2026-10-31 found these. The ones of the first review were reproduced in C# before the change, and for the second review a test now holds each one.

| Section 8 | Now | Why |
|---|---|---|
| `SegmentQuadtree2d` owns `Segment3d` snapshots | It owns endpoints. Also built from vertices and edges; `Insert(a, b)`, `Remove(a, b)`, `GetEndpoints`. `Segment3d` stays accepted | `Segment3d` keeps a center, a normalized direction and an extent, so `P0` and `P1` are rounded, depending on z too. On a jittered UTM grid, 43 % of the segments missed a point query at their own start vertex. `GetItem` now rebuilds a `Segment3d` from the endpoints, so it can differ in the last bits, and `Remove` accepts it. It throws `ArithmeticException` for endpoints a `Segment3d` cannot hold: a difference above about 1e154, or a length too small to normalize. A `Segment3d` input keeps its rounded endpoints: only building from endpoints gives exact ones |
| Fixed internal depth limit | 64, was 32 | One far outlier flattened the tree: 100000 items visited instead of 15. Above an extent ratio of about 2^64 this still happens, with right results |
| Regions recursively quartered | A cell more than twice as long as wide is cut across its length only | One row of a TIN stayed at the root, as cells kept the shape of the extent |
| Extent grows on insertion | `Extent` still reports the items' bounds, but the root region grows geometrically | Every insert beyond the extent rebuilt the tree: 40000 sorted points took 90 s. A tree grown this way has a root larger than `Extent` |
| Tolerance is absolute and complete | Exact at the tolerance: exact squares decide where the rounded distance is within its error of it. That error follows the size of the cancelled terms, not only the tolerance | A point at distance exactly 3 was refused at tolerance 3, and points near long segments were misjudged both ways. The nearest order of nearly collinear segments stays rounded |
| Ties by ID | Nearest pruning leaves 1e-15 of room | A rounded item distance can fall below the rounded distance to its box, and the tie then went by visiting order. Ties are between computed distances, so geometric ties can still differ in rounding |
| "a triangle within tolerance" (2.1) | `TryGetHeight` and `stopAfterFirst` take the nearest triangle within tolerance, the lowest ID on a tie | The result depended on the traversal, so on how the tree was built |
| "all equal for a consistent TIN" (2.1) | Equal at shared vertices. On a shared edge, heights agree up to rounding | This cannot hold for rounded interpolation |

The core rejects nothing new. Removal by value and by ID no longer scans. A cell of equal items is not split, as cutting cannot separate them. Points on a diagonal of a grid TIN no longer take the exact path (17 us and 8 KB per call before), unless the z difference dwarfs the height, as next to a spike.

### Limits that stay

- An exact hit is the first found in the traversal. For overlapping surfaces, ask `QueryHeights`.
- IDs are never reused and removal does not compact. A long insert and remove loop grows memory, and `Extent` never shrinks, even after removing an outlier.
- Mutating the tree during a traversal is not supported and not detected.
- The margin of 2e-4 and the default tolerance of 0.01 are absolute (owner decision, Q-3): results at these thresholds change with scale, unlike the rest of the library. Tests compare them against the definition instead of bit for bit.
- Tolerance hits extrapolate the plane of the triangle (section 8), so a point next to a sliver can get a height far outside its range: `(0,0,0), (10,0,0), (5,1e-6,100)` at `(5, 0.01)` gives about 1e6. Open for the owner: clamping, or a slope guard.
- Triangles thinner than a relative determinant of 1e-9 take the exact path. Above that, a rounded height keeps an error that grows towards the threshold.
- Only the four quadrants of the root build in parallel, so data in one quadrant builds serially.

### Verification

The tests cover the D cases, brute-force oracles for all queries, random operation sequences against a scan (`QuadtreeStateStressTests`), and `Similarity.All()`. Records of the abandoned sessions were dropped, as they described older code. The state of the gates is in the PR. Open for the owner: the timing, memory and side-by-side comparison of section 5.
