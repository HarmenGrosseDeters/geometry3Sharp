# Exact Boolean 3.0 T5 PR 2: carrying the open winding across input-triangle edges (design)

Status: design, for an early review of the argument before the implementation (T5 PR 2, step 1). It extends L2 of
`EXACT_BOOLEAN_TAIL_55928.md` section 3; the notation is that section's (M an open mesh, R one of its far points,
chi_R, Omega_R, I, N, the local frame, `carry_bound`).

## 1. Why: the cost count

Antipodal cost count on the 21 slow open models (Union, 4 cores, master f0d3035d, alone on the machine; aggregate
numbers only):
- Total 762 s, classification 626 s. The 8 models over the fast-winding switches (`AntipodalMaxBoundarySegments = 1000`,
  `AntipodalMaxTriangles = 100000`, so evaluated by the fast winding number) take 708 s and 595 s of it; the 13
  Antipodal models 30 s of classification.
- With the switches lifted (Antipodal + L2 for all 21), classification per model: 1087xxx about equal, 117959
  92 -> 434 s, 1005586 46 -> 74 s, 112798 40 -> 51 s, 122796 7.5 -> 46 s, 124526 4.9 -> 4.2 s. All result hashes
  differ from the fast-winding ones (expected: a different method near half-integers; the reference check is step 3).
- Profile of 1005586 with the switches lifted: the per-piece path 42.6 %, `front_crossings` (the far ray) 35.5 %,
  `antipodal_value` 6.9 %, the tracing 2.7 %.
- The cause: L2 carries only within an input triangle, and `trace_open_winding` skips blocks of one piece. Share of the
  evaluations that come from single-piece blocks (each pays a far ray): 1087141 90 %, 1005586 92 %, 112798 97 %,
  122796 82 %, 124526 90 %, 117959 98 %. Fallbacks from multi-piece blocks: 0. These models are open meshes with
  few intersections; most input triangles are not cut, and each is one piece.

Upper bound of the lever: those single-piece evaluations lose their far ray and their reference evaluation; what remains
per piece is one boundary sum (or none, by the threshold carry) and one short segment query. Estimate: classification
of 1005586 about 82 -> 15-25 s, of each 1087xxx about 80 -> 20-30 s. The switches stay as they are in this PR; moving
them is decided after step 3 (measurement and reference check).

## 2. The change in one sentence

The breadth-first tracing of L2 runs over clusters of edge-adjacent input triangles instead of single input triangles,
and a step p -> q between pieces of two different input triangles t1 (p's) and t2 (q's) uses the same identity
chi_R(q) = chi_R(p) - I(p -> q) + N(p, q, R) with candidates that are complete for a segment that leaves t1.

## 3. What the L2 argument used about "the same input triangle"

The identity itself does not: the closed polygon p -> q -> R -> p crosses M (signed) as often as it links M's boundary,
for any p, q, R in general position. `carried_crossings` already takes each end's side rule from its own piece (`sp`,
`sq`). Four parts of L2 do use it, and each is replaced:

| L2 part | why it needs one input triangle | cross step |
|---|---|---|
| crossing candidates: t's `Partners` of M (`CrossingGrid`) | a triangle of M crossed inside t meets t, so the arrangement recorded the pair | box-tree segment query (4.1) |
| pierce candidates: `pierce_prefilter` on the tetrahedron (t, R) | the triangle (p, q, R) lies in that tetrahedron | prefilter on the hull of t1, t2 and R (4.2) |
| the local frame of t | p, q and the corners are near t's origin | t1's frame, q's corners converted into it (4.3) |
| `LocalFrame.InPlane`: t0 = 0 | p and q lie in t's plane | only if t2's corners lie exactly in t1's plane (4.4) |

The rest carries over unchanged and is restated with its condition:
- *The pieces' own triangles* (review of #675: a fifth reliance). Within a triangle, the candidates are t's partners,
  never t itself. A cross segment meets t1 and t2 when they are triangles of M (the pieces' own mesh). Their side test is
  an exact zero, set without arithmetic (4.1, last paragraph); the side rule then counts the crossing next to p
  correctly.
- *The side rule.* A centroid in a candidate's plane counts on the side its own piece's normal points to; both
  centroids in the plane on different sides is degenerate. Per end, so valid for p and q of different triangles.
- *The degenerate tests* (segment through an edge or vertex of M, a boundary segment touching the triangle (p, q, R),
  a ray without a value within its bound) fall back to q's own ray, which is then carried on. Unchanged.
- *The threshold carry.* `carry_bound` (with q's centroid in t1's frame, like the crossing tests: the length from
  coordinates in two frames would be meaningless; review of #675) bounds the continuous change of w along the segment by G(box of p and q) |pq|
  (Biot-Savart: |grad w| <= G off M). Nothing in it uses the triangle; the box is the segment's. q takes
  thr(p) - crossed and the rest of p's margin where the bound fits, else its boundary sum is computed.
- *The value.* chi_R(q) + Omega_R(q) / 4pi with the bound and the acceptance condition of section 3 (|w - h| >
  bound + 2.002 roundingBound, roundingBound <= 0.99e-6), so every traced threshold is that of an Antipodal evaluation
  with the ray toward R at q's exact centroid. The error does not grow along the chain: chi is an exact integer, the
  value is evaluated afresh at each piece.

## 4. The four replacements

### 4.1 Crossing candidates: a segment query through M's box tree

Claim: every triangle U of M that the exact segment P -> Q meets (crosses, or touches: the degenerate cases) is reported
by `arr.Trees[M].QuerySegment(p', q', pad, ...)` for the doubles p', q' below.
- U meets the segment at a point X, so X lies in U's exact box. The tree's boxes are those of the exact input
  coordinates (`triangle_box` of the input points, doubles, exact).
- p', q' are the centroids in input coordinates from their approximations: Input = (g + c) 2^-s with g the grid
  coordinates, c = `arr.Center` (an integer that is exactly a double, `centerD`), s = `ScaleExp` (`ToInputVector`: input = (grid + c) 2^s). With g from
  `Center.Approx` (error a_g per coordinate, `LocalFrame.ToGrid`), p'_k = fl(g_k + fl(c_k)) 2^s is within
  e_k = (a_g + 2^-52 |c_k| + u (|g_k| + |c_k|) (1 + 2^-52)) 2^s <= (a_g + 3.4e-16 (|g_k| + |c_k|)) 2^s of the exact P_k:
  c's conversion is exact (the term 2^-52 |c_k| is kept as slack), the addition's rounding, and the scaling by 2^s exact
  outside the subnormal range (`input_segment`).
- Each point (1 - l) P + l Q of the exact segment is within max(e_p, e_q) per coordinate of (1 - l) p' + l q'. So a box
  that meets the exact segment meets the double segment once expanded by pad >= max_k max(e_p,k, e_q,k).
- `segment_hits` is conservative for its rounding: it widens the parametric interval by 1e-9 (|ta| + |tb|) + 1e-12 and
  tests against the box expanded by pad (as for `front_crossings`, which passes 1e-9 |p|_1).
- pad = 1.01 max(e_p, e_q) + 1e-300. A NaN (tiny coordinates) falls back to q's own ray.

The pad is the centroids' own error, not `front_crossings`' 1e-9 |p|_1: at the knot's coordinates the relative margin
1e-9 would exceed the pieces and return many triangles that the side tests then reject.

The candidates' side and orientation tests are those of `carried_crossings`, in t1's frame (4.3), with the exact
fallback on the exact points (frame-independent).

*The pieces' own triangles.* The query returns t1 and t2 themselves (when M is the pieces' own mesh, as for the
self-overlaps of an open operand), and their coplanar neighbours. Their side test is an exact zero, which no filter
decides: on the test knot the cross steps first computed 21.6k exact centroids against 2.5k without them. So where the
side filter fails, the side is 0 without arithmetic if U is the centroid's own input triangle, or if that triangle's
corners lie exactly in U's plane (three plane sides of input points, cached per pair). That is the exact value, so this
holds within a triangle too and changes no result; the knot's exact centroids went to 4 (`local_side`).
The side rule then applies as before: the displaced end is on the front of its own triangle, and a segment from it to a
point behind that plane crosses the triangle at its start, which is counted (the displaced start lies in front).

### 4.2 Pierce candidates: the hull of t1, t2 and R

Claim: a boundary segment of M that meets the triangle (P, Q, R) is not dropped.
- P lies in the closed triangle t1, Q in t2 (exact centroids of pieces, inside their input triangles). So the triangle
  (P, Q, R) lies in H = conv(t1's corners, t2's corners, R): the distinct corners of both and R, five points where t1
  and t2 share an edge, up to seven where they do not (review of #675: two pieces of one mesh can share an output edge
  without their input triangles sharing an edge, at a self-intersection of the mesh or a T-junction). The hull is built
  from all distinct corners, so the claim holds for any pair.
- `pierce_prefilter` generalised to a point set v[0..n-1]: every triple (a, b, c) whose plane has all other points of v
  strictly on one side, certified with the same bound as the existing orientation guard (err(x) and the margin, now for
  each of the n - 3 other points), is a supporting plane of H with H on that side. A segment with both ends strictly
  outside it by the existing bound (1.01 err(x) + 1e-9 S |n|_1) cannot meet H, nor the triangle (P, Q, R).
- A triple that is not certified is not used; using fewer planes only keeps more segments. For n = 4 this is exactly
  today's test (each face's guard is its fourth vertex), so within-triangle steps stay bit-identical.
- The bound err(x) is the one derived for `pierce_prefilter` in section 3 (point errors e_x = 2.3e-16 max_k |x_k| in grid
  coordinates, rows, normal, dot products); it does not depend on n.

Per cross step: 10 triples of five points, against M's boundary segments (133 on 55928; up to 1000 under the switch)
in doubles, cached per pair (t1, t2) and far point.

*Measured, and changed in step 2: the prefilter's cost.* Every pair scanned all of M's boundary segments, with 10 to 35
triples: on 122796 with the switches lifted (2512 segments, 1.13M cross steps) classification went 46 -> 81 s, with
26 % in `pierce_prefilter` and 30 % in converting the boundary ends into each frame. So the hull test runs only on the
segments that a projection from R lets through (`SegmentProjection`, `pierce_prefilter_projected`). The argument:
- The hull H = conv(corners, R) is the union of the segments from R to points of conv(corners). A segment that meets
  H therefore lies, at its meeting point, on a ray from R through conv(corners).
- Central projection from R onto a plane x_k = const: pi(x) = (x_i - R_i, x_j - R_j) / (x_k - R_k), for the axis k along
  which R is farthest from the box (the far points lie 4000 extents away on one axis, so every point of the box has
  x_k - R_k of one sign, far from 0). On that half-space pi maps segments to segments and convex hulls to convex hulls,
  and every point of a ray from R (other than R) to the same point. So a segment that meets H has a projection that
  meets pi(conv(corners)) = conv(pi(corners)), and its projected box meets the box of the projected corners.
- Errors: every point is a double within e_x = 2.3e-16 max_k |x_k| per coordinate of the exact one (R's too, e_R). A
  quotient n / d, with n and d computed within dn = e_x + e_R + u |n| and dd = e_x + e_R + u |d| of the exact ones, is
  within (dn + |n / d| dd) / (|d| - dd) + u |n / d| of the exact quotient. The boxes (each segment's, and the query's
  corners') are widened by 1.01 times that per coordinate, so each contains the exact projections. Where |d| <= 2 dd for
  any point, the projection is not used and every segment is a candidate.
- The segments' boxes are sorted into a uniform grid (only for speed: a box is in every cell its range covers, and a
  query tests the boxes of its cells exactly). The result is ascending, and the hull test then runs on it as before; the
  margin's S stays the maximum over all segments, so the tests are those of the whole set.
- What it can change: a segment that misses H but that the hull test kept (within its margin) is no longer a candidate.
  Its exact tests could only have given a fallback (an end on the plane of (p, q, R) outside the triangle), never a
  pierce. So results change at most by a fallback to the piece's own ray (another R), within the band. With
  `TracedAcrossEdges` off, the within-triangle candidates are computed as before.

Measured on 122796 (switches lifted): 0.3 segments per pair pass the projection on average (of 2512);
classification 81 -> 24 s, hash unchanged (77370B066B52BE19, the same as L2 without cross steps).

### 4.3 The frame of a cross step

The I and N tests of p -> q run in t1's frame (origin O = t1's first corner, an input point):
- q's corners are converted into t1's frame like any corner (`LocalFrame.Corner`: the exact difference to O, rounded
  once; error `LocalFrame.PointError` = 2.3e-16 max_k |x_k|). That bound holds for any exact point and any integer
  origin; only its size depends on the distance to O, which is at most diam(t1) + diam(t2) where t2 shares a corner
  with t1, and otherwise the distance across the shared output edge plus both diameters (the bound holds either way).
- q's centroid in t1's frame is `Center.Local` from those corners, with its bound unchanged ((e_A + e_B + e_C) / 3 *
  (1 + 1e-15) + 1.2e-16 m). It is a second `Center` of q (frame t1); q's own `Center` (frame t2) is used for its value
  and for the steps inside t2. The exact centroid P, where an exact stage needs it, is the same rational for both and is
  shared.
- orient3d of translated points equals that of the original ones (section 3), so a sign certified in t1's frame is the
  exact sign.
- M's candidates' corners and boundary ends in t1's frame: from its caches.

### 4.4 The exact zero of the pierce test

L2 sets t0 = orient3d(a, b, p, q) = 0 without arithmetic where both ends of the boundary segment a-b lie exactly in t's
plane (`LocalFrame.InPlane`), since p and q do too. For a cross step q is in t2's plane, and a, b, p in t1's plane does
not make t0 zero: with a, b on the line of the shared edge, t0 = 0 iff q lies in the plane (a, b, p) = t1's plane. So:
- if t2's three corners lie exactly in t1's plane (plane sides of input points, integers, once per pair), q does (their
  average) and `InPlane` gives the exact zero as before;
- otherwise t0 is taken from `orient3d_filtered_err_near` and the exact stage.

### 4.5 Costs found in step 2, and what was changed (each bit-identical where not said otherwise)

Profiles of the first version on the slow models (switches lifted) showed the cost per cross step, not the far rays,
dominating. Each change below keeps every hash (`profile` runs, the same hash as L2 without cross steps).

- *The candidates' plane sides before the frame* (`plane_side_abs`). The box tree returns the triangles around the
  step's corners (16 per step on 1087141), and converting each one's corners into the frame cost most of the step.
  Most have both centroids strictly on one side of their plane, so no crossing and no degenerate case. That is decided
  first on the grid approximations g (error a per coordinate, `Center.Approx`) and the plane's doubles (each within
  relative 2^-52, `PlaneApprox`): the exact value at the exact point differs from fl(A g_x + B g_y + C g_z - D) by at most
  (|A| + |B| + |C|) a (|pa_k| (1 + 2^-52) bound |A| etc.) plus the coefficients' errors and the roundings, below 1e-14 of
  the terms' magnitudes as in `plane_side_filtered`. A skip only where the general test would return at "same side".
- *The pieces' own triangles* (`interior_side`). For U = t1, the centroid p lies in U's plane and in U's relative
  interior; the step's other end q lies on the side of U's plane on which all of t2's relative interior lies, if that
  side is unique: the sides of t2's corners (a corner shared with t1 is 0; the others by `plane_side_filtered` on the
  corner's exact double, else exactly) all >= 0 with one > 0 gives +1, and so on. A piece's centroid is in its input
  triangle's relative interior: pieces have positive area (`ExactMeshArrangement` outputs no zero-area triangles). Then
  the segment meets U's plane only at p, an interior point of U, and it crosses U exactly where the displaced ends lie
  on different sides (p's on the front of its own triangle): the general test's result without the orientation tests.
  Mixed corner sides fall through to the general test.
- *Shared frames.* A block of at most 4 pieces reached by a step takes the frame of the triangle it is reached from,
  if its corners lie within 16 of its diameters of that frame's origin. The step then needs no second centroid of q,
  and the corners and the far point are converted once. Only the filters' precision depends on the origin (the bounds
  of 4.3 hold for any integer origin), so this is a heuristic for speed. `LocalFrame.InPlane` takes the triangle whose
  plane it tests (p's), not the frame's.
- *The gradient bound per cluster* (`cluster_bound`). In the near case `gradient_bound` sums over every boundary
  segment, per step. Per cluster, the segments at distance >= near_C = 1e-3 s_C of the cluster's box C (the union of its
  pieces' boxes) contribute their term at that distance, summed once; that bounds their term at any box in C (the
  distance there is at least as large, and that box's s, near and slack are at most C's, since s grows with the box's
  coordinates, which lie within C's). The others are summed per step. The bound is larger than the per-segment sum, so
  fewer thresholds may be carried; never an unsound one.
- Allocations per step (a 64-slot cache array per pair, closures, the tree query's stack, lists): removed.

## 5. Clusters, order and determinism

- *Adjacency.* Pieces are adjacent when they share an output edge (the edge keys of `trace_open_winding`), now also
  across the blocks of a cluster. The clusters join input triangles that share an input edge, but within one, two
  pieces can share an output edge without that (4.2); the hull covers them. A cross step is taken only between pieces of the same input mesh. Across meshes (the intersection
  curves) the segment runs next to M's triangles there and would often fall back; the identity would hold there too.
- *Clusters.* A serial pass over the blocks in index order builds clusters by breadth-first search over the blocks'
  adjacency (input triangles of one mesh with an input edge in common, by corner ids), each closed when it reaches a
  cap of pieces (`TraceClusterPieces` = 2^14, to be measured; the blocks queued then are released) and started anew
  from the lowest unassigned block (`trace_clusters`). The partition depends only on the output,
  not on threads.
- *Tracing.* Each cluster is traced serially: references (own rays) only where the breadth-first search over its pieces
  starts a component or a step falls back; the order is fixed (lowest piece first, neighbours in edge order). Clusters
  are traced in parallel, the largest first. Each piece belongs to one cluster, so no state is shared; the result is
  deterministic for any thread count (the tests check serial against parallel and repeated runs). The cap changes
  which piece is a reference and so its far point R; thresholds can then differ only within the band (section 3 of the
  tail-case note), and a change of the cap can change hashes there.
- Blocks of one piece are no longer skipped.

## 6. Verify mode and counts

- `VerifyTracedOpenWinding` evaluates every traced or carried piece with its own ray, as today, and records the margins.
  The A/B for this PR: cross steps on against off (the switch is internal, for tests), the same thresholds except where
  the piece's own evaluation lies within its bound of a half-integer (margin <= 2e-6 or -1).
- New counts: `l2_cross_steps`, `l2_cross_fallbacks` (degenerate, NaN), `l2_cross_candidates` (tree candidates per
  step, summed), `l2_clusters`.

## 7. Tests (step 2)

- The tree query's completeness: segments between exact rational centroids against triangles that touch or cross them,
  with the doubles moved within their bound; a mutant of the pad (the centroids' error halved, u's term dropped) must
  lose a candidate.
- The hull prefilter: for n = 4 the same result as before on random tetrahedra; for five points, segments that pierce a
  triangle (p, q, R) near a sliver face are kept; a guard failing (a point near the plane) drops the plane.
- 4.4: a cross step where a, b lie on the shared edge and t2 is not coplanar (InPlane must not give zero); a coplanar t2
  where it does.
- Procedural open meshes with mostly single-piece blocks (a cut sheet with few intersections, a fan around a hole): the
  thresholds against the reference (cross steps off), verify mode margins <= 2e-6, `Similarity.All()` bit for bit,
  serial against parallel, repeated runs, reuse.
- Mutants of the new code killed by hand; the boolean shards' incremental run where a baseline fits.

Done in step 2 (`ExactMeshBooleanTestsCrossCarry`, 22 test cases), and 20 mutants of the new code by hand:
15 killed. The survivors:
- The projection without the points' own error e_x: covered by R's error e_R, which is larger (R is 4000 extents
  from the box, so |R| >= |x|).
- The query's pad halved: the bound has about a factor 2 of slack on the test data; dropping its rounding term (3.4e-16
  (|g| + |c|)) is killed (translated far from the origin).
- The own-triangle contributions with the other sign, or the displaced end on the back: for a step between edge-adjacent,
  consistently oriented triangles (the boolean orients its inputs first) the contributions at t1 and t2 come in pairs
  of opposite sign (q behind t1 exactly where p is behind t2), so these mutants change no count. Where the pair is not
  edge-adjacent (a self-intersection), t2's corners lie on both sides of t1's plane and the general test runs.
- The shared frame without its distance condition: a heuristic for the filters' precision; the results do not depend
  on it.

## 8. Measurement (step 3)

The 21 models, switches as they are: classification and total before/after, the trace counts, hashes (expected equal on
the 8 fast-winding models, which this does not touch; on the 13 Antipodal ones equal except within the band, checked by
verify mode). Then with the switches lifted, the same, and for the changed hashes the reference check (direct GWN sum,
beta = infinity) before any threshold moves.

## 9. Prior art

Within-triangle carrying is L2's (EMBER-style segment tracing with the Antipodal decomposition, section 3). Carrying
across edges along a spanning forest is the same tracing on a larger graph; EMBER (Trettner et al. 2022) traces between
arbitrary reference positions. No new method.
