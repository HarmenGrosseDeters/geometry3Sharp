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
- *The side rule.* A centroid in a candidate's plane counts on the side its own piece's normal points to; both
  centroids in the plane on different sides is degenerate. Per end, so valid for p and q of different triangles.
- *The degenerate tests* (segment through an edge or vertex of M, a boundary segment touching the triangle (p, q, R),
  a ray without a value within its bound) fall back to q's own ray, which is then carried on. Unchanged.
- *The threshold carry.* `carry_bound` bounds the continuous change of w along the segment by G(box of p and q) |pq|
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
  coordinates, c = `arr.Center`, s = `ScaleExp` (`ToInputVector`). With g from `Center.Approx` (error a_g per
  coordinate, `LocalFrame.ToGrid`), p'_k = fl(g_k + c_k) 2^-s is within e_k = (a_g + u (|g_k| + |c_k|)) 2^-s of the exact
  P_k (the scaling by 2^-s is exact outside the subnormal range; c is an integer below 2^53).
- Each point (1 - l) P + l Q of the exact segment is within max(e_p, e_q) per coordinate of (1 - l) p' + l q'. So a box
  that meets the exact segment meets the double segment once expanded by pad >= max_k max(e_p,k, e_q,k).
- `segment_hits` is conservative for its rounding: it widens the parametric interval by 1e-9 (|ta| + |tb|) + 1e-12 and
  tests against the box expanded by pad (as for `front_crossings`, which passes 1e-9 |p|_1).
- pad = 1.01 max(e_p, e_q) + 1e-300. A NaN (tiny coordinates) falls back to q's own ray.

The pad is the centroids' own error, not `front_crossings`' 1e-9 |p|_1: at the knot's coordinates the relative margin
1e-9 would exceed the pieces and return many triangles that the side tests then reject.

The candidates' side and orientation tests are those of `carried_crossings`, in t1's frame (4.3), with the exact
fallback on the exact points (frame-independent).

### 4.2 Pierce candidates: the hull of t1, t2 and R

Claim: a boundary segment of M that meets the triangle (P, Q, R) is not dropped.
- P lies in the closed triangle t1, Q in t2 (exact centroids of pieces, inside their input triangles). So the triangle
  (P, Q, R) lies in H = conv(t1's corners, t2's corners, R): five points (t1 and t2 share an edge).
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

### 4.3 The frame of a cross step

The I and N tests of p -> q run in t1's frame (origin O = t1's first corner, an input point):
- q's corners are converted into t1's frame like any corner (`LocalFrame.Corner`: the exact difference to O, rounded
  once; error `LocalFrame.PointError` = 2.3e-16 max_k |x_k|). That bound holds for any exact point and any integer
  origin; only its size depends on the distance to O, which is at most diam(t1) + diam(t2) since t2 shares an edge with
  t1.
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

## 5. Clusters, order and determinism

- *Adjacency.* Pieces are adjacent when they share an output edge (the edge keys of `trace_open_winding`), now also
  across blocks. A cross step is taken only between pieces of the same input mesh. Across meshes (the intersection
  curves) the segment runs next to M's triangles there and would often fall back; the identity would hold there too.
- *Clusters.* A serial pass over the blocks in index order builds clusters by breadth-first search over the blocks'
  adjacency (input triangles of one mesh with an output edge in common), each closed when it reaches a cap of pieces
  (2^14, to be measured) and started anew from the lowest unassigned block. The partition depends only on the output,
  not on threads.
- *Tracing.* Each cluster is traced serially: references (own rays) only where the breadth-first search over its pieces
  starts a component or a step falls back; the order is fixed (lowest piece first, neighbours in edge order). Clusters
  are traced in parallel, the largest first. Each piece belongs to one cluster, so no state is shared; the result is
  deterministic for any thread count (the tests check serial against parallel and repeated runs).
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

## 8. Measurement (step 3)

The 21 models, switches as they are: classification and total before/after, the trace counts, hashes (expected equal on
the 8 fast-winding models, which this does not touch; on the 13 Antipodal ones equal except within the band, checked by
verify mode). Then with the switches lifted, the same, and for the changed hashes the reference check (direct GWN sum,
beta = infinity) before any threshold moves.

## 9. Prior art

Within-triangle carrying is L2's (EMBER-style segment tracing with the Antipodal decomposition, section 3). Carrying
across edges along a spanning forest is the same tracing on a larger graph; EMBER (Trettner et al. 2022) traces between
arbitrary reference positions. No new method.
