# 2D Meshing: Behavioural Specification

Status: draft for review by the repository owner. Written as the *specification side* of a
clean-room process. It describes **observable behaviour only**: what goes in, what must come
out, which inputs are known to be hard, and what the correct result is. It deliberately says
nothing about how any existing implementation works or how any defect was fixed. The
implementation must be derived from the published literature (Delaunay triangulation,
constrained and conforming Delaunay triangulation, Delaunay refinement, snap rounding, exact
geometric predicates, etc.), not from this document's sources.

Commit references for the entries in section 2 are kept in a separate owner-only mapping
(F-number → commit) and are intentionally not part of this document.

Conventions:

* "Domain" = the region to be meshed (bounded by outer boundary segments, minus holes).
* "Segment" = an input constraint edge. "Subsegment" = a piece of a segment in the output.
* "Input vertex" = a point given by the caller. "Steiner point" = a vertex created by the mesher.
* "Valid mesh" (used throughout):
  * every triangle has strictly positive orientation under an exact orientation test;
  * triangles meet edge-to-edge (no T-junctions, no overlaps, no gaps);
  * neighbour relations are symmetric;
  * the union of triangles equals the domain (area check);
  * every input segment is the union of output subsegments (within the documented snap
    tolerance);
  * all coordinates are finite.
* Priority labels in section 1:
  * **[USED]**: used by the owner's production code path;
  * **[TESTED]**: not called by the production path but covered by the forks' tests and
    considered important there;
  * **[?]**: cannot be judged, see section 4.

---

## 1. Feature scope

### 1.1 Inputs

| # | Input | Priority | Notes |
|---|-------|----------|-------|
| I1 | Point list (x, y) with a caller-supplied integer id per point | [USED] | The caller carries the height (z) via the id, not the mesher. Ids are 0..n-1 in the production path; arbitrary distinct ids (eg database keys) must also work [TESTED]. Duplicate ids must not break meshing. |
| I2 | Segments between points, each with an integer marker/label | [USED] | Production builds segments from polylines: one or more **outer rings**, any number of **breaklines** (open polylines, may cross anything), and **inner rings** (holes). Each ring/breakline gets its own marker. |
| I3 | Closed rings given in GIS convention (first point repeated at the end), either orientation | [USED] | Must be accepted; the duplicate closing point is not an error. |
| I4 | Hole seed points (a point inside each hole) | [USED] | Production computes one seed per inner ring itself. The library should also offer a robust "find a point strictly inside this ring" helper [TESTED], because seed placement fails in practice (see F-49, F-50). |
| I5 | Holes given as rings without an explicit seed | [TESTED] | The library computes the seed. |
| I6 | Regional attributes: seed point + integer label (+ optional max area) per region | [TESTED] | Output triangles carry the label of their region. If two seeds fall in the same region, the **last one wins**, as in the original C program. Seeds outside the domain or inside a hole are ignored. |
| I7 | Per-region maximum area | [TESTED] | Not used by production. |
| I8 | Per-triangle area constraint set on an existing mesh, then refine | [TESTED] | Includes meshes built from point sets only. |
| I9 | Polygon objects can be triangulated more than once; a run must not corrupt the input | [TESTED] | |
| I10 | Vertex attributes beyond x/y (eg z) | [?] | Production does not pass them to the mesher. |

Input validation (all [USED]):

* NaN or ±Infinity coordinates are rejected with an argument error naming the problem (never a
  crash deep inside, never a hang).
* Coordinates beyond the range where exact predicates can be evaluated (order 1e70) are rejected
  with an argument error. Inside that range all magnitudes must work, including very small ones
  (1e-160) and mixed magnitudes.
* Fewer than three distinct points, all points identical, or all points collinear: **empty mesh**
  (zero triangles), no exception, no hang, for every triangulation mode.
* A segment whose endpoint is not in the point list: argument error that names the missing
  endpoint.
* Zero-length segments (both endpoints equal, or equal within tolerance) are ignored, no error.
* Duplicate and reversed duplicate segments are treated as one.
* Hole rings without interior (collinear, fewer than three distinct points, folded back on
  themselves) remove nothing; their edges are kept as ordinary segments.

### 1.2 Options

| # | Option | Priority | Expected behaviour |
|---|--------|----------|--------------------|
| O1 | Constrained Delaunay triangulation (no refinement) | [USED] | Used for the "reference mesh" from which the caller interpolates heights of Steiner points. Must be produced for the same input with the same snapping as the refined mesh. |
| O2 | Conforming Delaunay | [USED] (always on when quality is on) | Every output triangle satisfies the empty-circumcircle property with respect to all vertices; segments are split as needed. A vertex placed on a segment may lie a few ulps off it; tiny in-circle violations caused only by that (collinear within about 8 ulps) are acceptable. |
| O3 | Minimum angle (degrees) | [USED] | Default 20°; production uses 20–33°. Guarantee: every triangle has all angles ≥ the bound, **except** triangles whose small angle is forced by the input (two segments meeting at an angle below the bound, or a strip narrower than the snap/grid resolution), and except when the Steiner limit stopped refinement. Values up to about 38° must terminate on ordinary input; any value (eg 40°) must terminate when a Steiner limit is set. Invalid values (< 0 or ≥ 60) must not silently disable *other* constraints. |
| O4 | Maximum angle | [TESTED] | Enforced also when it is the only constraint. Combined with min angle and with holes and internal segments. Invalid values must not disable the other constraints. |
| O5 | Global maximum triangle area | [TESTED] | Every triangle area ≤ bound, except triangles that cannot shrink because their boundary segments must not be split (the interior triangles must still meet the bound). |
| O6 | User-defined refinement predicate (callback "is this triangle too big / bad?", eg max edge length) | [TESTED] | Refine until no triangle fails the predicate; must not throw precision errors on UTM-scale survey points. |
| O7 | Steiner point limit | [USED] | Hard cap on inserted points. Production default: 10 × (points + segments) + 100 000. When the limit is reached, refinement stops and returns a **valid** mesh (quality not guaranteed). The limit counts every Steiner point created during the run, including any that do not appear in the final mesh. |
| O8 | Segment splitting policy: (0) all segments may be split, (1) all but the outer boundary, (2) none | [USED] | Must still produce a valid mesh; the angle guarantee is lifted where splitting is forbidden. |
| O9 | Per-segment split permission (callback or set of markers, "locked" segments, eg terrain breaklines) | [USED] (in production, rule 3: breaklines locked, rings splittable) | Locked segments keep exactly their input vertices; they are split **only** at intersections with other segments. Triangles near locked segments must still meet the area and angle bounds wherever that is possible without splitting the locked segment. |
| O10 | Automatic resolution of crossing segments ("noding"), on by default | [USED] | Crossing, touching and overlapping segments are split at their intersection, so that the output contains every input segment as a chain of subsegments. Must be switchable off; when it is off and the input has crossings, the mesher must **fail loudly** (exception), never return an invalid mesh. |
| O11 | Snap tolerance in coordinate units (0 = automatic) | [USED] | Production default = output rounding grid (0.01 = 1 cm). Points closer than the tolerance are merged; a point within the tolerance of a segment becomes a vertex of that segment; nearly coincident lines (double-digitised shared borders, building wall along a parcel border) are merged. Geometry moves by at most the tolerance; gaps larger than the tolerance are kept. Automatic mode uses a tolerance relative to the coordinate magnitude (about 5 µm at UTM scale). Where needed to produce a valid mesh, geometry may move by at most about 1.4 mm at UTM scale (the implementation must document the relative bound). Exact duplicates are always merged. |
| O12 | Output grid ("snap rounding" to a grid size, eg 0.01) | [USED] (default on in production) | **All** output vertices (input and Steiner) lie exactly on the grid (the same value as decimal rounding to n places). After the caller rounds coordinates to the grid nothing changes, so no triangle can flip. Locked segments keep their vertices. Steiner points placed on a segment must stay on the original input segment within the grid resolution (errors must not accumulate over repeated splits). The minimum-angle guarantee does not apply to triangles whose shortest edge is shorter than about 2 grid cells; refinement must terminate with a grid set. |
| O13 | Wedge tolerance (merge thin wedges) | [USED] (default 3 × snap tolerance) | A segment that leaves another segment at an angle below about 10° and whose far end is within the tolerance of it is merged into it. Short segments at large angles within the tolerance are **not** merged (they are real small features). Segments farther away than the tolerance are not merged. Chains of slowly drifting polylines are merged, but no segment may be moved more than the tolerance from its **original** input position. |
| O14 | Steiner point reduction after refinement | [USED] (optional, default off) | Remove free Steiner points (not input points, not on segments) if the remaining triangles still meet all constraints. Input vertices, segments and points on segments never change. The number of triangles below the minimum angle must not increase. Disabled when a user predicate (O6) is set. Deterministic. |
| O15 | Steiner point smoothing (n passes) after refinement | [USED] (optional, default 0) | Move free Steiner points to improve triangle quality: never makes the mesh quality worse, the smallest angle never decreases, the grid is respected, deterministic. |
| O16 | Mesh smoothing of an existing mesh (Voronoi-centroid style, n iterations) | [TESTED] | Must work far from the origin (1e6 offsets), with duplicate input points and with free-standing segments. Cancellation must leave the mesh unchanged. Smoothing a second mesh must not change a mesh that was smoothed earlier. The stop criterion must be scale-independent. |
| O17 | Refine an existing mesh again (possibly with stricter options) | [TESTED] | Repeated refinement stays valid. Refining a point-set mesh keeps its convex hull (the domain must not grow). Refinement uses the same configuration (including predicates) as the original mesh. |
| O18 | Convex hull mode (mesh the convex hull when no outer boundary is given) | [TESTED] | |
| O19 | Choice of triangulation algorithm (divide-and-conquer, sweep, incremental) | [TESTED] | Only the default is used. All algorithms must give the **same** Delaunay triangulation (up to cocircular ties chosen consistently) and the same counts on degenerate inputs. |
| O20 | Cancellation token | [TESTED] | Triangulate, refine and smooth honour cancellation by throwing an operation-cancelled error. |
| O21 | Exact arithmetic switch | [USED] in the old library; must **not** exist as "off" | Turning exact predicates off was a main source of silent failures (F-3). The new implementation should always use exact predicates. |
| O22 | Repeated runs in the same process/thread | [USED] (performance matters) | Each run gives bit-identical results to a first run; no state is carried over between runs. Fast repeated runs (many tiles per thread) are desired. |
| O23 | Structured (regular grid) mesh of a rectangle | [TESTED] | n×m cells give 2nm triangles, boundary marked. |
| O24 | Terrain simplification: choose a subset of dense terrain points (with optional breaklines) such that any Delaunay triangulation of the subset reproduces all points within a vertical error bound | [TESTED] | See F-63. Production usage unknown, so also [?]. |
| O25 | Voronoi diagram output (standard and bounded) | [?] | Not used by the production code path. Covered by tests: consistency, unique edge ids, the bounded diagram is only valid on conforming meshes, duplicate vertices are skipped, the caller's bounding box is not modified. |
| O26 | Renumbering for bandwidth reduction (reverse Cuthill–McKee) | [?] | Not used by production. |
| O27 | File I/O (.node/.poly/.ele, MFEM) | [?] | Not used by production; out of scope unless requested. |

### 1.3 Outputs and guarantees

| # | Output | Priority |
|---|--------|----------|
| R1 | Vertex list: id, x, y, boundary marker/label, flag "input vs. Steiner" | [USED] |
| R2 | Input vertex ids preserved **unchanged** in the output, also when intersections are created. New vertices get ids that do not collide with input ids. Production assumes new ids ≥ number of input points; any unique scheme is fine if documented. | [USED] |
| R3 | Triangle list: id, three vertex ids, counter-clockwise, region label | [USED] |
| R4 | Subsegment list: two vertex ids, the marker of the originating input segment, and the one or two adjacent triangles | [USED] |
| R5 | Triangle–triangle neighbour information; iteration over the triangles, edges and neighbours around a vertex. This includes nested iteration, and vertices that are duplicates and therefore not in the mesh (empty result, no crash). | [TESTED] |
| R6 | Unique triangle ids, also after refining or renumbering; edge enumeration visits each edge exactly once | [TESTED] |
| R7 | Point location: given (x, y), return the containing triangle, including points exactly on interior edges or vertices | [USED] (production interpolates heights of Steiner points in the reference mesh) |
| R8 | Marker propagation: a vertex merged by snapping keeps the label of the merged-away vertex if it had none. A subsegment covered by several coinciding input segments carries the non-zero marker; the result must not depend on whether noding was needed. | [USED] (markers identify locked breaklines) |
| R9 | Mesh validator: consistency, orientation, Delaunay checks | [USED] (production re-checks every result) |
| R10 | Statistics the caller needs: number of inserted points; list of triangles below the minimum angle (the caller can also compute this) | [USED] |
| R11 | Conversion to a half-edge structure, including pinch vertices (two boundary edges leaving one vertex) | [TESTED] |

Global guarantees (all [USED]):

* **Never return an invalid mesh silently.** Either a valid mesh or an exception.
* **Always terminate.** No infinite loop, no unbounded memory growth, no stack overflow (a stack
  overflow kills the whole process in .NET and cannot be caught).
* Identical input and identical options give bit-identical output (see 3.3).
* Thread safety: independent meshes may be computed concurrently on different threads.

### 1.4 Failure behaviour

* Invalid input (NaN, ±Inf, out-of-range coordinates, a missing segment endpoint): argument error,
  before any work.
* Degenerate input (no area): empty mesh.
* Crossing segments with noding disabled: exception.
* Inputs with many crossings at tiny angles: still a valid mesh in which no geometry moves by
  more than the maximum documented tolerance; an exception only if that is impossible. Never a hang.
* Steiner limit reached: valid mesh, refinement stopped. The caller can detect this via the number
  of inserted points.
* Refinement cannot improve a triangle because of floating-point precision limits (segment pieces
  too short to split, needles): that triangle stays as it is; no exception, no inverted triangle.
* Cancellation: operation-cancelled error; the input objects are unchanged.
* Desired (from the owner's old library): errors should carry the coordinate where the problem
  occurred and a category. One exception type with a kind enum is suggested.

---

## 2. Failure catalogue

Each entry gives a title, the situation (generatable procedurally), the wrong behaviour before,
the expected behaviour, and severity / frequency. All synthetic coordinates below were written by
the owner in tests; none come from real data. "Square S" means the axis-aligned square [0,10]×[0,10]
as outer boundary.

Severity: *C* = critical, *H* = high, *M* = medium, *L* = low.

### 2.1 Crossing and touching segments

**F-1 Three crossing segments, silent invalid mesh**
- Situation: square S plus three segments with integer endpoints whose three pairwise
  intersections form a tiny triangle, eg:
  - (2,4)-(4,8), (1,5)-(6,6), (1,7)-(5,3);
  - (2,5)-(9,5), (6,3)-(10,10), (8,7)-(5,0);
  - (5,0)-(1,10), (7,7)-(0,7), (3,3)-(8,8).
- Generator: random integer segments in [0,10]², 15 per input. About 20 % of seeds hit this or
  F-2/F-3.
- Before: a mesh with inverted triangles, no exception.
- Expected: valid mesh, area 100, all segments present.
- Severity C (silent corruption). Frequent with crossing breaklines: about 14 % of random integer
  inputs, and 1–2 % even of non-crossing real breakline sets at Gauss-Krüger scale in the old
  library (see F-3).

**F-2 Five crossing segments, stack overflow**
- Situation: square S plus (9,3)-(2,0), (7,2)-(1,8), (7,3)-(3,6), (10,6)-(9,1), (10,10)-(8,2).
- Before: the process crashed (stack overflow); the original C program hangs.
- Expected: valid mesh.
- Severity C. Rare per input but fatal for a server process.

**F-3 Crossing segments throw an exception**
- Situation:
  - square S plus (9,5)-(5,7), (7,7)-(4,1), (6,5)-(3,10);
  - also three segments with coordinates of magnitude 1e3–1e4, where two endpoints differ only in
    the 14th significant digit and one segment passes through that near-duplicate point. Mesh the
    convex hull, with all three segment splitting policies and min angle 0/20/30.
- Before: exception.
- Expected: valid mesh containing all segments.
- Severity H.
- The old library additionally, by the owner's own measurement (100 random inputs per row), with
  exact arithmetic switched off as in production:
  - 15 integer segments: 67 ok, 22 exceptions, 10 silently invalid, 1 endless loop;
  - with a UTM offset of 3.5e5: similar;
  - non-crossing breaklines plus 300 points at 5.6e6: 2 % silently invalid.

**F-4 Random integer segments, quality refinement**
- Situation:
  - square S plus 40 random integer segments, min angle 25°, 20 seeds;
  - 100 segments in a 20×20 square at 20°;
  - 200 segments on a 50-grid at 20°.
- Before: a crash during quality refinement, or a mesh with inverted triangles.
- Expected: valid, all segments within 1e-7, exact area.
- Severity C.

**F-5 Random crossing segments at 30°: exception**
- Situation: square S, 40 random integer segments, min angle 30°. Certain seeds trigger it, eg
  172, 173 and 176 of a standard seeded generator; exact reproduction depends on the generator.
- Before: exception.
- Expected: valid mesh.
- Severity H.

**F-6 Segment through input points, or nearly through them**
- Situation:
  - square [0,4]², points (1,1) and (2,2), segment (0.5,0.5)-(3.5,3.5);
  - two crossing diagonals with an extra point at (2, 2+ε), ε ∈ {1e-8, 1e-12, 1e-15}, min angle 20°.
- Expected: valid mesh, the segment recovered through the points.
- Severity H if broken.

**F-7 Overlapping collinear segments; self-overlapping contour; spikes**
- Situation, each at min angle 20°:
  - (1,5)-(6,5) and (4,5)-(9,5);
  - a contour that runs back along itself: (10,10)→(5,10)→(8,10)→(0,10);
  - a spike (10,5)→(15,5)→(10,5);
  - duplicate and reversed duplicate segments;
  - a self-intersecting "bowtie" contour (0,0),(1,1),(1,0),(0,1) (area 0.5).
- Expected: valid, correct area.
- Severity M.

**F-8 Zero-length segments mixed with crossings**
- Situation: square S, two degenerate segments (3,3)-(3,3) and (5,5)-(5,5), two crossing
  diagonals, and a segment of length 1e-14.
- Expected: valid, area 100.
- Severity L.

**F-9 Noding disabled must fail loudly**
- Situation: the F-1 input with crossing resolution switched off.
- Expected: exception.
- Severity C if an invalid mesh is returned instead.

### 2.2 Near-coincident geometry, snapping and tolerances

**F-10 Vertex nearly on a segment**
- Situation: square S, segment (1,5)-(9,5), point (5, 5+ε), ε ∈ {1e-6, 1e-10, 1e-14}, min angle 20°.
- Expected: valid. Within the automatic tolerance the segment runs through the point, and the point
  is not moved.
- Severity H.

**F-11 Nearly duplicate vertices**
- Situation: unit square with points (0.5,0.5), (0.5+ε,0.5), (0.5,0.5+ε),
  ε ∈ {1e-6, 1e-10, 1e-13, 1e-15}, min angle 20°.
- Expected: valid, area 1.
- Severity M.

**F-12 Adjacent polygons with a double-digitised shared border**
- Situation: two polygons share a wavy border x = 10 + sin(i), y = i (i = 0..10). The second
  copy's vertices are perturbed by uniform noise of ±ε/2, ε ∈ {1e-6, 1e-9, 1e-12}. Outer extents
  [0,20]×[0,10].
- Before: with a min angle the refinement practically never terminates, because a sliver of
  width ε needs about length/ε triangles (more than 30 s, 400 000 triangles at 200 000 Steiner
  points).
- Expected: with the automatic tolerance the ε = 1e-12 borders are merged and quality meshing
  finishes in about 30 ms. For larger ε the result is valid and the Steiner limit stops it.
- Severity H (very common in cadastral and GIS data).

**F-13 Micro-rings with coordinate noise**
- Situation:
  - five rings of five points each; all points of a ring lie within 1e-13 of a centre;
  - the centres are 0.001–0.002 apart, in degree range (eg centred near (-90, 40));
  - a frame 0.03 wide; min angle 20°, Steiner limit 20 000.
- Before: did not terminate.
- Expected: valid mesh, area 0.03².
- Severity H.

**F-14 Stacked nearly identical squares**
- Situation:
  - 40 copies of a 2×2 square near (600,600); copy k is shifted by k·d and rotated by
    0.8 + 0.1·k·d rad;
  - d ∈ {1e-10, 1e-8, 1e-6, 1e-5, 1e-4}; frame [595,605]².
- Before: exception or an excessive vertex count.
- Expected: valid mesh, area 100 ± 1e-9.
- Severity H.

**F-15 Snap cycle with a large tolerance**
- Situation: tolerance 0.01; points A=(0,0), B=(0.010,0.019), C=(0,0.020); segments A-B and A-C;
  frame [-1,1]². C lies within the tolerance of A-B, and B lies within the tolerance of A-C.
- Before: exception.
- Expected: valid mesh, area 4.
- Severity H (found in real cadastral data at 1 cm tolerance).

**F-16 Near-collinear segments with a third segment crossing close to their shared endpoint**
- Situation:
  - two segments share an endpoint S at an angle of about 0.0002°: a point B of one lies about
    0.02 mm from the other, about 4–5 m from S;
  - a third segment passes S at about 1 mm and crosses both near S;
  - UTM-scale coordinates (about 3.8e5, 5.7e6), tolerance 0.01, min angle 20°, Steiner limit 100 000.
- Before: the near-collinear pair stayed separate, and refinement spent about 97 000 Steiner points
  in the wedge. One real tile had 203 175 triangles, 154 870 of them below 20°.
- Expected: the two segments share the piece S-B (no tiny angle at S); fewer than 2 000 vertices.
- Severity H.

**F-17 Snap tolerance larger than a hole**
- Situation: square S with a hole smaller than the snap tolerance, so the hole collapses.
- Before: the hole seed remained and removed the whole domain (empty mesh).
- Expected: the collapsed hole removes nothing; area about 100.
- Severity M.

**F-18 Building wall along a parcel border (sliver vs. snap)**
- Situation:
  - a parcel 30×20 at a UTM-like offset (3.7e5, 5.71e6);
  - a building 10×8 inside whose bottom wall is 5 mm (or 50 mm) above the parcel border;
  - min angle 20°, Steiner limit 50 000.
- Expected:
  - tolerance 0.01 merges the 5 mm wall into the border; the area changes by at most
    perimeter × tolerance, with fewer than ¼ of the triangles of the unmerged mesh;
  - a 50 mm gap is kept, with both wall vertices present.
- Severity H (performance and quality).

**F-19 Thin wedges**
- Situation: border (0,0)-(20,0) and a wall from (5,0) to (15,o), then up to (15,5).
- Expected:
  - o = 0.025 with snap 0.01 and wedge tolerance 0.03 at 30°: no triangle below 30°, and the vertex
    (15,0.025) is still present;
  - o = 0.05: not merged;
  - a short segment (5,0)-(5.02,0.025) at a large angle: not merged;
  - a polyline from (2,0) with vertices (2+2i, 0.003·i), i = 1..8: merged completely, no bad
    triangles;
  - with slope 0.0075·i: the border (0,0)-(20,0) must not be dragged more than 0.03 from its
    original position.
- Severity M.

**F-20 Noding that does not converge at any tolerance (many crossings at tiny angles)**
- Situation: long edges cross a densely sampled polyline many times at angles near 0° (public CDT
  test input "issue-211" of the artem-ogre/CDT project).
- Before: exception; an intermediate attempt raised the vertex count about 50-fold.
- Expected: valid mesh via snap rounding; the vertex count stays proportional to the input.
- Severity M.

**F-21 Noding long parallel stripes is quadratic**
- Situation: 20 000 long parallel horizontal segments that overlap in x.
- Before: about 5 s in total, of which meshing alone needs about 0.4 s.
- Expected: subquadratic, well under 1 s.
- Severity M (performance).

**F-22 Nearly coincident back-and-forth border**
- Situation: a polyline in degrees runs about 5 km from A to B and returns to C. C lies 5.7e-10°
  (about 0.06 mm) from A-B, with an angle of about 6.5e-7° at B. Min angle 20°, Steiner limit 20 000.
- Expected:
  - without snapping, the Steiner limit stops refinement and the mesh is valid;
  - with tolerance 1e-9 (degrees) the sliver disappears: no bad triangles, the limit is not reached.
- Severity M.

### 2.3 Quality refinement

**F-23 Bad triangles remain in the output (max area / min angle violated)**
- Situation: square [0,10]² with max area 0.5 and min angle 0/20/30.
- Before:
  - 9 triangles larger than 0.5 remained;
  - on a real parcel tile at 30°, 618 triangles below the angle instead of about 9, and 20× slower.
- Expected: no triangle above the max area; triangles below the minimum angle only at small input
  angles.
- Severity C (guarantee broken silently).

**F-24 Region area constraints make refinement oscillate**
- Situation:
  - square [0,10]², region seeds (7,2) with label 1 and area 0.1, and (2,7) with label 2 and area
    2.0, with and without the diagonal segment (0,0)-(10,10);
  - min angle 25°, variable area on, Steiner limit 100 000;
  - also a square with an inner square [2,5]² as a second region, areas 0.05 / 0.5.
- Before: exception, or cycling up to the Steiner limit and returning a coarse mesh (5 triangles).
- Expected: all area bounds met per region, no triangle below 25°, fewer than 5 000 vertices.
- Severity H.

**F-25 Unsplittable segments leave bad triangles**
- Situation: rectangle 10×20 (also 10×30), no segment splitting, max area 1.73.
- Before: the 10×20 rectangle gave 8 triangles.
- Expected: at least 10·h/1.73 triangles; every triangle not touching the boundary meets the bound;
  exact area. The same holds for locked breaklines.
- Severity H.

**F-26 Precision limit at needle-shaped regions**
- Situation: two needle triangles (101,99)-(101,1e6)-(102,1e6) and (100,100)-(1e6,110)-(1e6,100)
  inside a frame of size 1.2e6 starting at (-1e5,-1e5); min angle 20°, Steiner limit 10 000.
- Before: silently inverted triangles, or an exception.
- Expected: valid mesh; pieces that cannot be split are left as they are.
- Severity C.

**F-27 Needle refinement is quadratic**
- Situation: the F-26 input with Steiner limit 60 000.
- Before: 78–88 s.
- Expected: more than 50 000 vertices in about 2 s (release build).
- Severity M (performance; a real public test case behaves the same way).

**F-28 A max angle alone is ignored**
- Situation: any polygon with only a maximum angle constraint (eg 100°).
- Before: no refinement at all.
- Expected: no angle above the bound.
- Severity M.

**F-29 Refining again with a max angle crashes**
- Situation: refine a mesh without a max angle, then refine again with a max angle.
- Before: crash.
- Expected: works.
- Severity M.

**F-30 An invalid angle silently disables all constraints**
- Situation: max angle < 60 or > 180 (or an invalid min angle) together with a max area.
- Before: the whole refinement was skipped and the max area ignored (only a log warning).
- Expected: the invalid value is rejected or ignored individually; the other constraints are
  applied.
- Severity M.

**F-31 Constraints not met with restricted segment splitting**
- Situation: random polygons with segment splitting policy 1 or 2, min/max angle and area
  constraints.
- Before: triangles that were too large or bad remained.
- Expected: all constraints met where achievable.
- Severity M.

**F-32 Extreme edge lengths during refinement**
- Situation: triangles with extremely short edges (down to a subnormal squared length) or extremely
  long edges during refinement.
- Before: crash.
- Expected: valid mesh, no crash.
- Severity L.

**F-33 Area constraints on point-set meshes ignored**
- Situation: triangulate a point set (or a square plus two points), set a per-triangle area of
  0.002 around two vertices, and refine with variable area.
- Before: ignored.
- Expected: applied.
- Severity L.

**F-34 Area-only refinement 10× too slow**
- Situation: unit square, max area 0.001.
- Before: 148 ms.
- Expected: about 14 ms, with the same triangle count.
- Severity L (performance).

**F-35 Refining a point-set mesh grows the domain or ignores the configuration**
- Situation: a random point set triangulated without segments, then refined with quality.
- Before: Steiner points outside the convex hull (the domain grew), and custom predicates replaced
  by defaults.
- Expected: the convex hull is the boundary, and the configuration is kept.
- Severity M.

**F-36 Old library: Steiner points outside the domain with locked breaklines**
- Situation: square of 100 m, 10–60 points, 2–7 crossing locked breaklines, min angle 20°.
- Before: about 30 % of results had new vertices up to 2 m **outside** the outer ring, so heights
  could not be interpolated.
- Expected: no vertex outside the domain.
- Severity C.

**F-37 Old library: refinement never terminates**
- Situation: parallel breaklines 0.1 mm apart, min angle 20° without a limit; 4 % of random 30° runs.
- Expected: always terminates (Steiner limit, and no slivers after snapping).
- Severity H.

**F-38 Small input angles**
- Situation:
  - triangle (0,0),(1,0),(cos a, sin a), a ∈ {1°, 0.1°, 0.01°}, min angle 30°;
  - a fan of 20 segments from the origin with 0.001 rad spacing, min angle 25°;
  - a star with spike tip angles 1e-3 … 1e-15 rad, at scales 1, UTM and 1e-6. Bases closer than
    the precision merge into a segment.
- Expected: terminates, valid.
- Severity H if it hangs.

**F-39 High-degree input vertex**
- Situation: a centre point connected by segments to 300/600 points on a circle of radius 10, min
  angle 25°, max area 1.
- Expected: valid, no triangle larger than 1, terminates in less than 30 s.
- Severity M.

**F-40 High minimum angle**
- Situation: square with 30 random points, min angle 33.8° / 34.5° / 40°, Steiner limit 200 000.
- Expected: terminates with a valid mesh.
- Severity M.

**F-41 Thin rectangle with max angle**
- Situation: rectangle 100 × 0.01, min angle 20°, max angle 100°.
- Expected: valid, area 1.
- Severity L.

**F-42 Unbounded memory during refinement**
- Situation: certain refinement inputs ran out of memory.
- Expected: bounded memory use, a valid mesh returned.
- Severity M.

### 2.4 Holes and regions

**F-43 Touching holes; a hole touching the outer boundary; a hole seed on the boundary; a thin hole**
- Situation and expected area, each in square S:
  - holes [2,4]×[2,4] and [4,6]×[2,4], touching along an edge, at 25°: area 92;
  - hole (0,4),(3,4),(3,6),(0,6) on the outer boundary, seed (1,5): area 94;
  - hole [2,4]² with seed (2,3) exactly on its boundary: area 96;
  - a hole of 6 × 1e-5: area 100 − 6e-5.
- Expected: correct areas, valid.
- Severity H.

**F-44 Hole in a hole; holes and regions outside the domain**
- Situation: square S with
  - hole [2,8]² with seed (3,3);
  - square [4,6]² with seed (5,5) inside the hole;
  - a hole seed (20,20) outside the domain;
  - a region seed inside a hole;
  - region (1,1) with label 5.
  Min angle 20°, all algorithms.
- Expected: area 64, all triangles labelled 5.
- Severity M.

**F-45 Two region seeds in one region**
- Expected: the label and area of the last seed win.
- Severity L.

**F-46 A hole leaks when segments cross the hole** (known limitation, still open)
- Situation: a segment lying inside a hole separates part of it; hole removal stops at segments.
- Current behaviour: part of the hole remains meshed.
- Expected (desired): see question Q7.
- Old library: when crossing breaklines opened an inner ring, hole removal leaked into the
  surrounding domain and removed valid triangles (the owner had a workaround that re-added them).
- Severity H.

**F-47 Concentric circles**
- Situation: two concentric circles with different vertex counts.
- Expected:
  - inner circle as a hole: no triangle inside, exact area;
  - inner circle not a hole: no triangle crosses it.
- Severity M.

**F-48 Degenerate hole ring**
- Situation: hole rings (2,2),(5,5),(8,8); (2,2),(8,8),(2,2); and (2,2),(8,8),(5,5),(2,2) in square
  S, min angle 25°.
- Before: exception when adding.
- Expected: removes nothing, the edges are kept as segments, area 100, vertex (8,8) present.
- Severity M (public fixtures "water-huge" and "issue83" of earcut).

**F-49 The hole seed lands on the hole boundary at UTM scale**
- Situation: triangular holes of random shape at UTM-like coordinates (about 5e5, 5.7e6); the
  library computes the seed.
- Before: in 13 of 154 random cases the seed was numerically on the boundary. When it rounded
  outward, the whole surrounding mesh was removed, silently.
- Expected: the seed is strictly inside, with a margin robust to rounding; correct area.
- Severity C.

**F-50 Interior point of thin or small contours**
- Situation:
  - a very thin rectangle (width orders of magnitude below its length);
  - valid obtuse triangles at scales 1e-3 and 1e6;
  - random star polygons at several scales.
- Before: a generic exception, or a point on the contour.
- Expected: a point strictly inside, scale invariant.
- Severity M.

### 2.5 Exact arithmetic and numerical range

**F-51 Coordinates of extreme magnitude**
- Situation: nearly collinear points (last coordinate moved by a few ulps) and nearly cocircular
  points, scaled to 1e-160 … 1e-100 and 1e100 … 1e160, and mixed magnitudes.
- Before: a wrong orientation or in-circle sign in about half of the cases, or NaN.
- Expected: the exact sign. Input beyond about 1e70 is rejected.
- Severity M (rare in GIS, but must not be silent).

**F-52 Crash on nearly collinear points of very different magnitude, or infinite coordinates**
- Situation: nearly collinear points of very different magnitude; infinite coordinates on segment
  endpoints.
- Before: crash.
- Expected: a correct result, or an argument error for infinite coordinates.
- Severity M.

**F-53 Hard predicate cases**
- Situation: 1 000 published near-degenerate orient2d and in-circle cases each (Nanevski, Blelloch,
  Harper, ICFP 2001; public fixtures of mourner/robust-predicates), values up to 1e33.
- Expected: all signs correct.
- Severity H.

**F-54 Old library: wrong predicate signs**
- Situation: 200 000 nearly cocircular cases at UTM scale and near zero.
- Before: 39 (UTM) and 4 072 (near 0) wrong in-circle signs, and 89 wrong orientation signs.
- Expected: 0.
- Severity H.

**F-55 Large coordinates with millimetre features**
- Situation: a square of size 10 at offset 1e5, 1e7 and 1e9, with 30 random segments of length
  about 1.1 mm, min angle 20°.
- Expected: valid, area 100 ± 1e-6.
- Severity C if broken (typical UTM/Gauss-Krüger data).

**F-56 Sweep-based triangulation not exactly Delaunay**
- Situation: 10 random points on the unit circle (seeds 1–5); 500 points on a circle at offset 0
  and 1e6.
- Before: Delaunay violations of about 1e-16.
- Expected: exact Delaunay for every algorithm.
- Severity L.

**F-57 Identical points**
- Situation: 5 copies of (1,1).
- Before: stack overflow (default algorithm) or exception (sweep).
- Expected: empty mesh.
- Severity H (process crash).

### 2.6 Concurrency, identity and object reuse

**F-58 Parallel runs differ from sequential runs**
- Situation:
  - 200 parallel triangulations of 300 random integer points in [0,40]² with min angle 25°;
  - 4 threads × 5 000 nearly cocircular in-circle tests.
- Before: more than 100 000 wrong signs. In the old library, 202 of 280 parallel tile runs
  differed from the sequential runs.
- Expected: 0 differences, identical to the sequential results.
- Severity C for the owner (parallel tile meshing).

**F-59 Ids**
- Situation:
  - points with ids 1, 2, 2, 3;
  - mesh vertices plus a new point with id 0;
  - inputs with database-style ids;
  - crossings that create new vertices.
- Before: a duplicate-key exception; or **all** user ids silently renumbered as soon as two
  intersections were created.
- Expected: user ids preserved, new ids unique.
- Severity H (the owner relies on ids for heights).

**F-60 Vertex objects reused from another mesh**
- Situation: build a mesh, then build a second mesh that contains a duplicate of a vertex object
  from the first.
- Before: building the second mesh modified the first.
- Expected: independent meshes.
- Severity M.

**F-61 Equality and hashing of points**
- Expected: two points with equal coordinates (including +0/−0) are equal and hash equally.
- Severity M.

**F-62 Point location misses points on interior edges**
- Situation: query points exactly on (or within rounding of) an interior edge of a mesh.
- Before: no triangle found.
- Expected: one of the adjacent triangles.
- Severity H for the owner (height interpolation).

**F-63 Terrain simplification bound violated**
- Situation: a regular 1 m terrain grid (cocircular points), error bound 0.05–0.2.
- Before: in flat areas a rebuilt triangulation picked other diagonals, giving errors above 2 m;
  rare bound violations.
- Expected: the bound holds for every Delaunay triangulation of the selection.
- Severity M.

**F-64 Smoothing bugs**
- Situation:
  - smoothing at offset 1e6: square of 10 with segment (1,1)-(9,1.000001), conforming, 25°,
    10 iterations;
  - smoothing with a duplicate input point;
  - smoothing with a free-standing segment;
  - a bounded Voronoi diagram after smoothing (20 random points);
  - reusing the smoother;
  - cancelling midway.
- Before: hangs or NaN, a corrupted earlier mesh, half-moved vertices.
- Expected: valid, finite, consistent.
- Severity H when smoothing is used.

### 2.7 Rounding of the output

**F-65 Rounding the output flips triangles**
- Situation: square S with 8 pairs of nearly coincident segments, (0.5, y)-(9.5, y + 0.004(i+1))
  and (0.5, y+0.003)-(9.5, y+0.002), with y = 2 + 0.7i; min angle 30°; grid 0.01.
- Before: rounding a full-precision mesh to 2 decimals produced flat or inverted triangles (about
  1 % on real cadastral tiles).
- Expected:
  - with the grid option, 0 flipped triangles after rounding;
  - with a grid, segment pieces that cannot be split on the grid must not leave large bad
    triangles nearby. No triangle below the angle may have a shortest edge longer than 20 grid
    cells away from segments.
- Severity C for the owner (silent corruption after export).

### 2.8 Public test fixtures

**F-66 Public polygon fixtures**
- Situation: fixtures of mapbox/earcut (bad-hole, earcut, infinite-loop-jhl, issue17, issue83,
  issue142, self-tangent-3/4, touching-holes6, water, water-huge, water-huge2/3), mapbox/delaunator
  (issue43, robustness1–4) and jhasse/poly2tri (sketchup). Each has all lines as segments inside a
  frame, at 0°, 20° and 30°, Steiner limit 20 000.
- Before (original library): 31 exceptions, 2 endless loops and 2 inconsistent meshes out of 348 runs.
- Expected: all valid.
- Severity H.
- These fixtures are public and can be fetched from those projects directly.

---

## 3. Non-functional requirements

### 3.1 Sizes and performance (as measured on the forks; targets, not hard limits)

* Production unit: a 100–200 m tile of cadastral data (parcels, buildings, land use) in UTM
  coordinates, cm precision, min angle 20–30°, grid 1 cm, wedge 3 cm.
  * Typical: 10 000–110 000 output triangles per tile; 15–900 ms per tile.
  * 201 tiles of one city take 60–100 s in total.
  * Many tiles are meshed in parallel (one thread per tile).
* Terrain: a 1 m elevation grid (up to 1 000 000 points per km²) plus breaklines, simplified to
  35 000–270 000 points before quality meshing (1–4 s).
* Delaunay of point sets: 1 000 000 points in about 1.3–2.4 s; 100 000 in about 0.1 s.
* Constrained Delaunay of 400 000 coastline points in less than 5 s; quality meshing of the same
  at 20° in about 11 s (1.7 M triangles).
* Memory: on the order of 100 MB per 100 000 triangles today; lower is welcome.
* Must run on .NET Framework 4.8 and .NET 8+ (the owner's production uses .NET Framework 4.8).
* The overhead of robustness features on ordinary data should stay within about 25 %.

### 3.2 Coordinate magnitudes

* Projected coordinates: UTM (x about 3e5–8e5, y about 5.6e6–5.8e6) and Gauss-Krüger (about
  5.6e6), with features of 1 mm–1 cm.
* Geographic degrees, with features down to 1e-9°.
* Test range: offsets of 1e-6 … 1e9. Exact predicates must hold up to the documented rejection limit.
* The caller rounds the output to 2 decimals horizontally and 3 vertically, hence the grid option.

### 3.3 Determinism

* The same input and the same options give bit-identical output (vertex coordinates, triangles,
  subsegments), independent of the thread, the process, and whether storage was reused. The
  owner's test suite compares an order-independent fingerprint of all triangles and subsegments.
* Triangulation of cocircular points (grids) must choose ties consistently.
* No randomness unless seeded deterministically.

### 3.4 Testing expectations

* Invariant checks after every test:
  * valid mesh (see conventions);
  * (constrained / conforming) Delaunay with an exact in-circle test;
  * area against the unrefined triangulation;
  * all input segments present, locked segments unsplit;
  * minimum angle where guaranteed, maximum area.
* Crash and hang cases must run in a child process with a timeout.
* External corpora the owner used (all public): GEOS XML robustness tests (2 156 cases), CDT test
  inputs (artem-ogre/CDT), CGAL polygon repair / straight skeleton data, Natural Earth, and open
  cadastral data of North Rhine-Westphalia (DL-DE Zero 2.0). Result to beat: all valid.

---

## 4. Open questions for the owner

* **Q1** Voronoi output (standard / bounded): needed in geometry3Sharp, or out of scope?
* **Q2** Regional attributes and per-region area constraints: needed? They are not used by the
  production path, only by tests.
* **Q3** User-defined refinement predicate and maximum angle: needed?
* **Q4** Mesh smoothing of a finished mesh (as opposed to Steiner-point smoothing): needed?
* **Q5** Terrain simplification (greedy TIN selection with an error bound): part of this work, or
  separate?
* **Q6** Choice of triangulation algorithms: is one robust default enough?
* **Q7** Holes with segments inside them (F-46): should a hole remove everything inside its ring
  regardless of interior segments (ring-based semantics), or keep seed-and-spread semantics?
  Should holes and regions be identifiable by ring instead of by seed point?
* **Q8** Id scheme for new vertices: must they be ≥ the number of input points (the current
  production assumption), or is any unique scheme fine?
* **Q9** Height (z) handling: keep it in the caller (reference mesh + point location), or should
  the mesher carry z and interpolate it linearly for Steiner points along segments and in triangles?
* **Q10** Error reporting: one exception type with a kind enum and the problem coordinate?
* **Q11** Should the list of triangles below the minimum angle and a "Steiner limit reached" flag
  be returned explicitly?
* **Q12** File formats (.poly/.node/.ele, MFEM) and bandwidth renumbering: out of scope?
* **Q13** Default Steiner limit: adopt the production formula 10 × (points + segments) + 100 000?
* **Q14** Default snap tolerance when the caller gives none: an automatic relative tolerance (µm
  at UTM scale), or must the caller always pass the data precision?
* **Q15** Is .NET Framework 4.8 still a hard requirement for geometry3Sharp?
* **Q16** The issue tracker of the public fork could not be read in this session (access
  restricted); upstream issues are covered via the fork's own documentation. Are there further bug
  reports that should be added?

---

## 5. Owner decisions (October 2026)

These answer the open questions of section 4. Where a decision conflicts with an earlier section, the
decision wins.

| Q | Decision |
|---|----------|
| Q1 | Voronoi output (O25) is out of scope for the first version. A bounded Voronoi diagram may be derived from the finished mesh later. |
| Q2 | Regional attributes and per-region maximum area (I6) are **in scope** (also needed for geology layer labels). |
| Q3 | No arbitrary user predicate and no maximum-angle bound. Instead, a **size function** (target triangle size at a location) is in scope. A minimum angle θ already bounds the largest angle by 180° − 2θ. |
| Q4 | Smoothing of a finished mesh (O16) is out of scope for the first version; Steiner-point smoothing (O15) stays. |
| Q5 | Terrain simplification (O24) is **separate** from this work; it belongs to `TerrainGreedyTIN` (roadmap C2(D)), and F-63 becomes a test case there. |
| Q6 | **One** robust triangulation algorithm, no algorithm choice option (O19 drops out). |
| Q7 | **Ring-based hole semantics**: a hole removes everything inside its ring, including segments inside it (fixes F-46). Holes and regions are identified by their ring; seed points remain available as an additional option. |
| Q8 | Input point i stays vertex i; new vertices get ids ≥ the number of input points. Vertices merged by snapping are reported in a mapping from input index to output vertex. |
| Q9 | The mesher **optionally carries z**: Steiner points get z by linear interpolation on the constrained triangulation of the **input** (the reference mesh of O1), never on the evolving mesh, so the result does not depend on insertion order. |
| Q10 | Errors are reported as a **status enum plus a result object** carrying the failure kind and the problem coordinate, like the library's other operations (`BooleanStatus`, `WrapStatus`, …). Exceptions only for invalid arguments and cancellation. Where earlier sections say "exception" or "fail loudly", read "a failure status, never an invalid mesh". |
| Q11 | **Yes**: the result explicitly lists the triangles below the minimum angle and flags "Steiner limit reached". |
| Q12 | File formats (O27) and bandwidth renumbering (O26) are out of scope. |
| Q13 | Default Steiner limit = 10 × (points + segments) + 100 000, overridable. |
| Q14 | Default snap tolerance = **automatic, relative** (µm range at UTM scale), only to absorb numerical noise. Snapping to the data precision (eg 1 cm) is opt-in via O11. |
| Q15 | .NET Framework 4.8 is a **hard requirement** (it may be slower there). |
| Q16 | No further bug reports for now; later ones are added as failure descriptions only. |
