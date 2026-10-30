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

## 9. First-version implementation and verification record

Implemented in `spatial/LooseQuadtree2d.cs`, `PointQuadtree2d.cs`, `SegmentQuadtree2d.cs`,
`TriangleQuadtree2d.cs` and the internal `QuadtreeGeometry2d.cs` / `QuadtreeExactQuery2d.cs`. Bounds are loose rectangular cells
with fixed bucket/depth limits and one owner node per item. Large builds distribute the first-level
subtrees across workers; queries carry caller-local struct state. An insertion beyond the extent
rebuilds the hierarchy, so repeated outward growth is not an amortized constant-time operation.
Removal retains ID slots and does not compact historical storage.

Geometry uses the existing exact orientation predicates. Floating-point distance/interpolation paths
rescale to avoid intermediate overflow, with an allocating integer fallback for ill-conditioned
constructions. Those conditioning thresholds are fallback heuristics, not new certified predicate
error bounds. Heights must be finite or throw. See [OPERATIONS.md](../OPERATIONS.md) for usage and
[QuadtreeBenchmark](../../benchmarks/QuadtreeBenchmark/README.md) for observed A/B output.

Executed validation (October 2026):

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release --filter 'FullyQualifiedName~Quadtree' --no-restore --verbosity minimal
```

Result: net10.0 passed 56 tests; net48 passed 53 tests. This includes separately authored spec-only
acceptance tests (not edited by the implementing session), own core/adapter tests and the compiled
usage example. The runtime-only allocation checks account for the target-framework count difference.
The initial run exposed height bit-invariance and nearest-distance tie-order failures; both were
fixed and the same tests passed unchanged.

Mutation evidence (mutations restored):

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter 'FullyQualifiedName~RectangleClosedGridLinesAndThinCrossingBoxes' --no-restore --verbosity minimal
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter 'FullyQualifiedName~HeightUsesExactPrecedenceAndAffineToleranceExtrapolation|FullyQualifiedName~SegmentPointExcludesEndpointsAndStopsOnlyOnHit' --no-restore --verbosity minimal
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter 'FullyQualifiedName~NumericScalingPreservesDistancesAndRepresentableHeights' --no-restore --verbosity minimal
```

Exclusive rectangle boundaries failed all 3 selected grid cases. Starting height search with the
tolerance and stopping segment search at a non-hit failed both selected adapter tests. The new
height-overflow exception test failed on the previous infinity-returning implementation, then passed
with the guard. The combined final run above passed after all mutations were restored.

Package compatibility and smoke commands (successful):

```powershell
dotnet pack geometry3Sharp_netstandard.csproj -c Release -o "$env:TEMP\g3-quadtree-package-0f2d4ba7" -p:Version=1.0.0-ci.1 --no-restore --verbosity quiet
dotnet run --project tests\PackageSmokeTest -c Release -p:G3PackageVersion=1.0.0-ci.1 "-p:G3PackageSource=$env:TEMP\g3-quadtree-package-0f2d4ba7" -- "$env:TEMP\g3-quadtree-package-0f2d4ba7\geometry3Sharp.1.0.0-ci.1.nupkg"
```

Package validation against upstream and package smoke checks passed. Only targeted tests, the new
benchmark builds/runs and package checks were run; the full pre-push CI sequence has not been run.
No push or merge was made.

### Follow-up: independent review and range regressions

The later independent review succeeded. Its findings were repaired with closed-clipping, overflow-ordering,
projection-underflow, height-underflow and subnormal-distance regression tests. The separately authored
acceptance tests were not changed. The final static numerical follow-up found no remaining concrete issues
in the reviewed repairs; this is not a certification of correctly rounded ordinary floating-point constructions.

Closed clipping uses an exact rational fallback when the rounded entry/exit interval is ambiguous.
The reviewed absolute ambiguity threshold is `3.552713678800501e-15` (32 times binary64 unit roundoff):
scaled interval endpoints have at most 6 unit-roundoff absolute error each, so the threshold conservatively
covers both bounds. The review applies to the already rounded, saturated 0.0002-expanded box.
Range/product loss and cancellation trigger the integer geometry fallback before tiny coefficients can
be amplified by large coordinates/heights. Subnormal square-root distances round via exact squared
midpoints and nearest-even. When built-in distances overflow, exact squared distances determine nearest
order; stable IDs break only actual ties. Custom double-only metrics retain their own distance/ID ordering.

Commands establishing red/green evidence:

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release --filter 'FullyQualifiedName~QuadtreeBoundsRegressionTests' --no-restore --verbosity quiet
dotnet test tests\geometry3Sharp.Tests -c Release --filter 'FullyQualifiedName~QuadtreeNumericRegressionTests' --no-restore --verbosity quiet
dotnet test tests\geometry3Sharp.Tests -c Release --filter 'FullyQualifiedName~PositiveSubnormalSegmentDistanceDoesNotRoundToZero' --no-restore --verbosity quiet
dotnet test tests\geometry3Sharp.Tests -c Release --filter 'FullyQualifiedName~Quadtree|FullyQualifiedName~EveryExample_IsTestedAndLinked' --no-restore --verbosity quiet
```

Before the respective fixes, bounds regressions failed 4 cases per framework, range regressions failed
16 cases per framework, and the initial subnormal regression failed 1 case per framework. That last
regression was subsequently generalized as `SubnormalSegmentDistanceRoundsToNearestUnit`.
The combined final command passed 83 tests on net10.0 and 80 on net48 after the generalized regression run.
The example is now registered directly in `DocExamplesTests`, satisfying its documentation registry.

Package validation and smoke checks passed again with output directory
`$env:TEMP\g3-quadtree-reviewed-0f2d4ba7` using the same pack/run commands above with that directory.
Paired-prefix benchmarks ran on both frameworks; commands and before/after observations are in the
[benchmark README](../../benchmarks/QuadtreeBenchmark/README.md). The final subnormal-only repair does not
occur in that ordinary-coordinate benchmark workload.

A broader run was also attempted:

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release --no-restore --verbosity minimal
```

It was stopped before completion after observing a missing documentation example registration (fixed above)
and a failure in `ExactBooleanIssueCasesTests.KnownLimitation_TwoTriangularPrisms_RotatedInexactly_KeepATinyVoid`
(expected 2, actual 1). The latter is outside this task and has not been baseline-confirmed or repaired.
The full suite is **not** recorded as passed.

### Complete local CI and performance precheck (2026-10-29)

The requested scope was local CI and local performance prechecking, not owner-machine acceptance or
unrelated implementation fixes. All CI stages completed normally; the full geometry suite is **not green**.
Commands (with caller-selected log/package directories):

```powershell
dotnet build geometry3Sharp_netstandard.csproj -c Release --verbosity minimal
dotnet test tests\geometry3Sharp.Tests -c Release --logger trx --results-directory $logs\geometry-results --verbosity minimal
dotnet test tests\g3cli.Tests -c Release --logger trx --results-directory $logs\cli-results --verbosity minimal
dotnet pack geometry3Sharp_netstandard.csproj -c Release -o $packageDir -p:Version=1.0.0-ci.1 --verbosity minimal
dotnet run --project tests\PackageSmokeTest -c Release -p:G3PackageVersion=1.0.0-ci.1 -p:G3PackageSource=$packageDir -- $packageDir\geometry3Sharp.1.0.0-ci.1.nupkg
dotnet build tests\geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true --verbosity minimal
Get-ChildItem benchmarks -Recurse -Filter *.csproj | ForEach-Object { dotnet build $_.FullName -c Release --verbosity minimal }
```

| Stage | Observed result |
|---|---|
| Library build | Successful |
| Full geometry suite, net10.0 | 2901 passed, 2 failed, 2903 total |
| Full geometry suite, net48 | 2899 passed, 1 failed, 2900 total |
| CLI tests | 43 passed, 0 failed |
| Package creation and API/reader-writer smoke | Successful |
| net48 test-project build | Successful |
| Benchmark project builds | All 11 successful; additional Quadtree net48 build successful |

Every Quadtree-named case in the full-suite TRX passed (82 net10.0, 79 net48).
Full-suite failures, without an established baseline or causal attribution to this change:

- net10.0: `ExactBooleanIssueCasesTests.KnownLimitation_TwoTriangularPrisms_RotatedInexactly_KeepATinyVoid`
  expected 2, got 1 ([test](../../tests/geometry3Sharp.Tests/ExactBooleanIssueCasesTests.cs)).
- net10.0: `RobustnessSweepTests.Sweep_NonFiniteInput`, LaplacianMeshSmoother / NaN vertex,
  exceeded its 20-second limit ([test](../../tests/geometry3Sharp.Tests/RobustnessSweepTests.cs)).
- net48: `G3MeshReaderTests.HighValenceVertex_ReadsInLinearTime` failed its timing assertion,
  reporting 2292 ms ([test](../../tests/geometry3Sharp.Tests/G3MeshReaderTests.cs)).

After the timing runs, the following isolated diagnostic was executed without rebuilding:

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release --no-build --no-restore --filter 'FullyQualifiedName~KnownLimitation_TwoTriangularPrisms_RotatedInexactly_KeepATinyVoid|FullyQualifiedName~HighValenceVertex_ReadsInLinearTime|FullyQualifiedName~Sweep_NonFiniteInput' --logger trx --results-directory $logs\isolated-failures --verbosity minimal
```

It passed 73/74 cases on net10.0 and 74/74 on net48. The ExactBoolean assertion reproduced on net10.0;
both timing-related failures passed in isolation. This does not establish that they are pre-existing or
harmless flakes, and does not replace a successful full CI run. No tests were weakened.

Three fresh benchmark processes per runtime, with three warmed query samples each, completed after CI.
All six checked the same baseline prefix/checksum. Hardware, commands, raw outputs and median/range
comparisons are recorded in the [benchmark README](../../benchmarks/QuadtreeBenchmark/README.md#local-performance-precheck-2026-10-29).
The local scan/index comparison is favorable, but query and triangle-adapter build medians exceed the
owner reference values on this host. Core-only memory measurements cannot certify full-adapter memory.
Owner-machine acceptance remains open.

### First query optimization and public comparison (2026-10-29)

Tier M follow-up, scoped to query performance and a public permissively licensed benchmark reference.
The retained library change rejects exact height candidates by their closed item AABB before orientation
classification and removes a duplicate degeneracy check already performed by containment. Tolerance
fallback, robust arithmetic, traversal ordering, public API and core layout remain unchanged.
A cached loose-region candidate was rejected because it increased retained core memory without a
consistent benefit for the other queries. The core source was restored byte-for-byte.

The benchmark now compares public NetTopologySuite 2.6.0 STRtree APIs with the quadtree and scan on the
same procedural inputs. Its tests are integrated into the existing geometry test project; the dependency
is benchmark/test-only. No owner implementation, Triangle/Triangle.NET or GPL sources were read.
The independent scoped review found an ambiguous thread-allocation label; the label, CLI assertion and
comparison limitations were corrected. The reviewer confirmed no remaining concrete findings after
removal of the cached-node candidate. No new arithmetic bounds were introduced.

Final source-accurate targeted tests passed: 94 on net10.0 and 91 on net48. Commands:

```powershell
foreach ($framework in @('net10.0', 'net48')) {
    dotnet test tests\geometry3Sharp.Tests -c Release -f $framework --no-restore --filter 'FullyQualifiedName~Quadtree|FullyQualifiedName~StrtreeComparison|FullyQualifiedName~EveryExample_IsTestedAndLinked' --verbosity minimal
}
```

The four new query-preservation tests also passed on each pre-optimization library using
`dotnet vstest` with `--TestCaseFilter:FullyQualifiedName~QuadtreeQueryOptimizationTests`, temporarily
substituting the original library and restoring the test DLL byte-for-byte in `finally`. Passing on the
old implementation is intentional for a behavior-preserving optimization; these are not timing tests.
Inverting the height AABB guard and injecting a spurious STRtree result each caused the selected net10
regression test to fail. Original sources were restored before the successful targeted runs.
Net10 tested/measured DLL hashes matched. Net48 assembly-file hashes differed, but reflection checks
matched all quadtree method IL bytes and declared field signatures; the tested core layout has no cache.

Package creation, enabled API validation against 1.0.324 and the existing API/reader-writer smoke passed:

```powershell
dotnet pack geometry3Sharp_netstandard.csproj -c Release --no-restore -o $packageDir -p:Version=1.0.0-ci.2 --verbosity minimal
dotnet run --project tests\PackageSmokeTest -c Release -p:G3PackageVersion=1.0.0-ci.2 -p:G3PackageSource=$packageDir -- $packageDir\geometry3Sharp.1.0.0-ci.2.nupkg
```

The generated nuspec was separately checked to contain no NetTopologySuite dependency.
No unrelated full-suite failures were repaired, weakened or baseline-attributed, and the entire CI was
not rerun in this optimization round. Its known failures listed above remain outstanding.

Final paired height-only measurements and public STRtree measurements, including their commands,
ranges, binary identities, rejected candidate and comparison limits, are recorded in the
[benchmark README](../../benchmarks/QuadtreeBenchmark/README.md#query-optimization-and-public-comparison-2026-10-29).
An incremental net10 build initially retained the rejected cache binary after source rollback. Its
mislabeled measurements were excluded, a forced rebuild was verified by SHA256, and only the affected
net10 measurements were repeated. This is not an owner-machine acceptance or a green full-CI claim.

Outstanding before readiness at the end of this 2026-10-29 round (updated by the following record):

1. Performance tuning and the owner-only BenchmarkDotNet comparison on both frameworks. Local
   Stopwatch observations do not establish the spec's owner-machine performance targets.
2. Diagnose/fix the reproducible net10.0 ExactBoolean failure, investigate the full-suite timing failures,
   obtain a successful complete local CI run before any push, and obtain the required PR review before
   marking a PR ready. Follow-up implementation fixes need separate scope; no baseline attribution
   has been established.

Preserve clean-room restrictions, upstream compatibility, separately authored acceptance tests and the
owner-approved robust-fallback/height-overflow policy when continuing.

### Allocation investigation and first-version scope audit (2026-10-30)

The historical zero-allocation test failure could not be reproduced. The original test and its exact
zero-byte assertion remain; additional per-operation dedicated-thread measurements and positive controls
were added, and a temporary buffer-growth mutation was detected. No production allocation workaround
or unproven CLR/test-runner attribution was introduced. The
[benchmark follow-up](../../benchmarks/QuadtreeBenchmark/README.md#allocation-investigation-and-fixture-follow-up-2026-10-30)
links the commands, output and standalone reproducer.

The net10 prism-test blocker was addressed with the pinned rounded fixtures from commit
`e559f6870d9cf5202f7f7da85ac48b4179b50ed7`, preserving the limitation assertions and adding the alternate
clean-solid fixture. Only the test and its QA description were imported; exact production is unchanged.
The full net48 suite and CLI/package/smoke/build gates passed. Full net10 passed the quadtree and prism
cases but failed the existing nonfinite-smoother time budget; the isolated rerun passed. This remains
an unresolved full-CI blocker, not a proven baseline issue. The final independent review attempt failed
at the model provider before producing a review. Commands and results are in the
[final CI record](../../benchmarks/QuadtreeBenchmark/perf/20261030-final-ci.txt). After this validation,
the owner authorized a draft PR documenting the unresolved CI timing failure and unavailable final
review. These gates remain open before marking the PR ready or merging.

The first-version scope is section 8, as resolved by the decisions in section 7, not every historical
helper in section 2.3. The core/adapters and independently authored acceptance suite cover growth and
stable IDs, D-1 through D-7, rectangle/segment oracles, nearest and k-nearest, complete Euclidean proximity,
exact-first height queries, snapshots, nested/concurrent queries and similarity transformations.
Domain-specific edge snapping, z-source and neighbour/split helper policies remain caller work under Q-6;
they are approved non-goals, not missing first-version APIs. No unanswered Q-1 through Q-9 decision remains.

Owner-machine BenchmarkDotNet timing and side-by-side acceptance remain open, as does memory acceptance:
existing retained core-heap observations exclude full triangle-adapter storage and are not peak-memory
measurements. Public NTS comparisons are additional evidence, not substitutes for these owner gates.
The performance targets in section 4.2 have not been certified by local Stopwatch measurements.
