# Exact Boolean: the tail case 55928 (design)

Status: design. The owner approved it on 2026-11-11, with the decisions on #626 of the same day (`Cancelled`: yes;
`MaxOpenWindingEvaluations`: deferred, section 5) (roadmap `exact-boolean.md`, the tail-case bullet, d9d760a1).
There is no library code yet. It is implemented as one Opus stage after the T1 Sonnet levers, because they touch the same
files and the stages run serially. The stage's exact-code review is done by a separate fresh agent that sees only the
diff.

## 1. The case and the measurements

Thingi10K 55928 is an open model of 88,353 triangles: one component of 88,348, 133 boundary edges and 32 non-manifold
edges. The case pairs it with a copy rotated by 20 degrees about (1, 1, 0) and shifted (`BooleanComparison export`).
The arrangement has 15,120,912 pieces.

- Of 524,503 intersecting pairs, 522,786 are self pairs of the two operands, and only about 1.7k pairs are between A
  and B. The case is two resolves of a crumpled model.
- Only 6,253 triangles of A are in a self pair. The 1,767 triangles nearest to the most intersected one fill a box
  0.03 across (the model is about 65 across, its edges about 0.007). They have 152k self pairs and alone give 4.5M
  pieces.
- 169 input triangles have more than 10k pieces each, at most 21,885. 13.4M pieces come from triangles with more than
  1k pieces.

Difference, 4 cores, the same container, nothing else running (`profile --repeat 1`):

| build | total | classification | triangulation | merge | result hash |
|---|---|---|---|---|---|
| 79a2c1ab (FWN per patch for open operands) | 579 s | 390 s | 162 s | 22 s | 61550C3C2F352E72 |
| master 88342701 (Antipodal per piece) | 973 s | 772 s | 172 s | 25 s | 61550C3C2F352E72 |
| this stage: L1 (e04b02f7) | 625 s | 442 s | 155 s | 24 s | 61550C3C2F352E72 |
| this stage: L2 (43990acb) | 475 s | 295 s | | | 61550C3C2F352E72 |
| this stage: L2 with the local frame (a5227166) | 454 s | 262 s | 163 s | 25 s | 61550C3C2F352E72 |
| this stage: L3 step 1, the local filter of the triangulation | 421 s | 275 s | 117 s | 24 s | 61550C3C2F352E72 |
| this stage: L2, the centroids from their corners in the frame | 425 s | 254 s | 128 s | 30 s | 61550C3C2F352E72 |
| this stage: L3 step 2, Orient's zeros from the constructions | 370 s | 224 s | 116 s | 25 s | 61550C3C2F352E72 |

The result is the same. Classification is 1.98 times slower on master. Both operands are open and have 133 boundary
segments each, so since 98def50f (open operands classified per piece) and d458662c (the Antipodal Method) every piece
gets one exact ray per open operand.

- That is 2 x 15.1M, about 30M rays, with 1.45e10 plane-side tests: about 480 per ray, and 890 in the knot alone.
- The rays are double-filtered. In the knot, `winding.orient3d` makes 3.9e9 calls and only 28k exact stages, so the
  cost is the number of triangles the ray passes, not exact arithmetic.
- Allocations: 227 GB in the triangulation (15 KB per piece), 295 GB in the classification, 61 s of GC pauses.

Patches give little here. There are 6.78M of them, 2.2 pieces per patch, because the self-intersection curves cut the
surface into small pieces.

Triangulation:
- In the knot it costs 2.4 us per piece (4 cores), which is normal. On the whole case it is 10.4 us per piece.
- Per input triangle the time grows with slope 1.36 against the pieces of the triangle (a log-log fit over the
  triangles with more than 100 pieces). The large triangles that cross the knot dominate.
- The predicates (`G3PredicateStats`, knot): `arr.orient` 116.6M calls, 5.7 % exact; `arr.incircle` 65.2M calls,
  1.8 % exact. That is about 1.7 exact stages per piece, on implicit points of high degree, which allocate BigIntegers.
- The walk's linear fallbacks are negligible: 174 in all.

Upper bound: at the median per-piece cost of the open class of the final run (classification 3.5 us, triangulation
0.5 us, 4 cores), plus merge, intersections and assembly, an operation takes about 90-130 s. The owner's bar is 5 min.

## 2. L1: open winding per patch with a certified bound (bit-identical)

**Claim.** Let M be an open operand and w its generalized winding number. Off M, w is harmonic, and its gradient is the
Biot-Savart field of the boundary curve:

    grad w(x) = (1/4pi) sum over boundary segments s of  integral over s of (y - x) x dy / |y - x|^3

This is the classical identity of the solid angle as the magnetic scalar potential of a current loop. The
constant-solid-angle surfaces use it, arXiv 2212.05913. For a straight segment s whose box is at distance at least d
from x, the term is at most min(|s| / (4 pi d^2), 1 / (2 pi d)). The second bound is the field of an infinite straight
wire.

For a box B that contains a set of points, let D_s be the distance from B to the box of s. Then for every x in B:

    |grad w(x)| <= G(B) = sum_s min(|s| / (4 pi D_s^2), 1 / (2 pi D_s))

**Patches.** A patch is the set of pieces of one mesh X joined across non-constraint edges where exactly two pieces of X
meet with the same stack, as in `classify`. Take the patch's first piece p (in piece order) and any piece q of the patch.
Let gamma be a path through the pieces of the patch, from centroid to the midpoint of the shared edge to the next
centroid, displaced infinitely close to the front side.

- gamma meets no triangle of M except where its own pieces lie on M (X = M). There the front-side limit of w is
  continuous along the patch, because no other sheet of M meets the patch except along constraint edges, which
  separate patches.
- Coplanar stacks are left out: pieces with a stack keep their own evaluation.
- Every part of gamma lies inside one piece r (centroid to edge midpoint, or edge midpoint to centroid), is at most
  diam(r) long, and lies in r's box, where |grad w| <= G(r), the bound above for r's box. So along gamma w changes by at
  most acc(gamma) = the sum over the pieces r it passes of G(r) diam(r), each piece counted on entering and on leaving.

**Rule (as implemented, `open_winding_per_patch`).**
- Evaluate p as today, which gives w_p with the bound e_p (at most 1e-6), or a snapped tie, or "undecided". If it is
  neither snapped nor undecided, margin = |w_p - h| - e_p - 2e-6, where h is the nearest half-integer.
- A Dijkstra over the patch's pieces from p, by acc, gives each piece q the smallest acc(q) over such paths. Every piece
  reached with acc(q) < margin takes the threshold of p. The search does not expand past acc >= margin.
- All other pieces are evaluated as today. That includes pieces not reached, pieces with a stack, and pieces whose box
  comes within 1e-3 of the coordinates' size of a segment box; the latter have G = infinity and are never crossed.
- All factors are rounded upwards (relative 1e-12). The boxes are widened by the error bounds of the approximations of the
  corners.

**Why it is bit-identical.**
- If acc(q) < margin, the true w(q) lies within acc(q) of the true w(p), and that lies within e_p of w_p. So the true
  w(q) is at more than 2e-6 from every half-integer, on the same side of 1/2 (and of every k + 1/2) as w_p.
- Today's per-piece evaluation of q returns a value within its bound, at most 1e-6, of w(q). It is therefore not
  snapped, and it gives the true threshold. The rule gives the same threshold.
- The exception is a q whose own evaluation would be "undecided". There are three ways that happens:
  - the centroid lies within the tolerance of a boundary segment end;
  - the centroid lies on a segment;
  - 20 rays fail in turn.

  The first two need a centroid within about 1e-7 of the coordinates' size of a segment. The 1e-3 exclusion keeps every
  decided piece far from that. The third depends on the ray and cannot be excluded by a bound. The verify mode
  (`VerifyPerPatchOpenWinding`) evaluates every decided piece as well and counts the differences; the tests assert 0.
- An undecided first piece decides nothing, because its bound is reported as -1. It is also near a segment, so its own
  G is infinite.
- Pieces next to the boundary, including the 87,883 undecided ones here, keep their own evaluation.
- An early variant used one bound per patch, G over the patch box times twice the sum of the diameters. It decided
  nothing on large patches, such as a sphere-sized patch whose box contains the hole. The Dijkstra over per-piece
  bounds decides everything away from the boundary.

**Measured upper bound** (arrangement only; patches as above with stacks left single, 7.25M patches). Evaluations per
open mesh, against 15.12M today:

| V below | mesh 0 | mesh 1 |
|---|---|---|
| 1e-4 | 10.55M | 11.55M |
| 1e-2 | 8.25M | 8.69M |
| 0.1 | 8.09M | 8.41M |
| 0.4 | 8.08M | 8.38M |

At V < 0.1 L1 is at its floor of about one evaluation per patch, because the boundary is far from almost every patch.
That is 30.2M to 16.5M evaluations (-45 %), and classification about 772 s to about 420 s, back to the level of
79a2c1ab and bit-identical. The rest needs propagation across patches, which is L2.

**Cost.** Per piece reached: G from the box of all segments (O(1)), or the sum over the segments where that box is near.
Per patch: one serial Dijkstra (a binary heap, ties by piece index, so it is deterministic). The adjacency comes from the
same edge groups that `classify` joins into patches. Measured on the knot benchmark (4 cores): 37 % of the open-winding
evaluations saved, classification 13.7 -> 9.9 s, the result hashes unchanged.

## 3. L2: carried rays instead of one far ray per piece (as implemented, `trace_open_winding`)

The owner accepted L2 although it is not strictly bit-identical. The argument and the A/B are below; on every case run so
far the thresholds are the same as the per-piece ones.

**Decomposition** (the Antipodal Method, Martens, Trettner, Bessmeltsev 2026, eq. 6 and 9, with a far point instead of a
direction). For an open mesh M and a point R outside the box of all meshes (the far points of `front_crossings`):

    w(x) = chi_R(x) + Omega_R(x) / 4pi

for x off M and off the cone swept from the boundary away from R.
- chi_R(x) is the signed crossings of the segment x -> R with M. It counts +1 where the segment passes to a triangle's
  front side, as `front_crossings` counts; beyond R the ray meets nothing.
- Omega_R(x) is the boundary sum with x1 the direction away from R (`antipodal_value`).
- chi_R and Omega_R jump by opposite amounts across the cone, so their sum is continuous there.
- A piece's value is w just in front of it, at its centroid displaced towards its normal.

**Two pieces with the same R.** The closed polygon p -> q -> R -> p crosses M (signed) as often as it links M's boundary.
That is the number of boundary segments that pierce the triangle (p, q, R), each counted with the sign -orient3d(p, q, R, b)
of its end b; the boundary is oriented as the triangles run it (`BoundarySegments`). So

    chi_R(q) = chi_R(p) - I(p -> q) + N(p, q, R)

- I is the signed crossings of the segment between the centroids, each end displaced to its piece's front side: a
  centroid in a triangle's plane counts as on the side its piece's normal points to.
- I and N are exact: plane sides and orient3d, filtered, with an exact fallback.
- The verify mode confirms the sign of N: with the opposite sign, every traced piece behind a pierce differs from its own
  evaluation.

**The tracing.** Within each input triangle, whose pieces are consecutive in the output and adjacent across their edges:
- The first piece is evaluated with its own ray (`antipodal_front_winding`, as today). This gives chi_R and R.
- The other pieces, in breadth-first order across the shared edges, take chi_R from the piece they are reached from, plus
  I - N as above. Their value is chi_R + Omega_R / 4pi with the same bound and snap as an Antipodal evaluation. Each value
  is therefore that of an Antipodal evaluation with R as the ray, and its error does not grow along the chain.
- A degenerate test falls back to the piece's own ray, which is then carried on. The degenerate tests are: a segment
  through an edge or vertex of M; both centroids in a triangle's plane on different sides; a boundary segment touching
  the triangle (p, q, R); a ray that gives no value within the bound.
- The input triangles are independent and are traced in parallel, the largest first. Each is traced serially in a fixed
  order, so the result is deterministic.

**Carrying the threshold** (the L1 bound inside L2). Where the bound of L1 on the box of p and q, times |pq|, stays
within p's margin |w - h| - e - 2e-6, q takes p's threshold minus I(p -> q), the integer jump where the segment crosses
M, and the rest of p's margin. Its boundary sum is not computed.
- This is sound for the same reason as L1: the true w(q) lies on the same side of every half-integer as w(p) - I.
- Near the boundary, G is infinite and the boundary sum is computed.
- With L2 on, L1 does not run. The direct evaluation of each patch's first piece that L1 needs costs more than the
  boundary sums it saves; on the knot, L1 + L2 took 6.9 s against 5.5 s for L2 alone. L1 remains the bit-identical mode
  with `TracedOpenWinding` off.

**Candidates.**
- *The triangles that I can count.* A triangle of M crossed by a segment inside input triangle t meets t along a
  segment through the crossing point. The arrangement records each such pair: the triangle is one of t's `Partners`,
  added under the same stripe lock as the segment. The crossing point also lies in its box and in t's.
- The candidates are therefore t's partners of mesh M, sorted, in a grid of their boxes clipped to t's box, in t's plane
  (`CrossingGrid`).
- *Not the triangles named by t's segments.* A first version took the candidates from t's own segment list, and a second
  added the other triangles' segments on t. Both missed triangles on the 55928 knot. In dense, nearly duplicate
  configurations, the unique-segment step of the arrangement keeps one of several equal segments, on both sides of a
  pair. The geometry stays right, but the name of the triangle is lost.
  - On a 441-triangle sub-ball of the knot, the second version gave 1,704 different thresholds, with margins up to 0.5;
    on the 1,767-triangle ball, 7,102 in 4.3M.
  - With the partners, both give none. So does a box-tree query of t's box, which is complete but slower: classification
    on 55928 took 443 s with it.
  - The test case "duplicate triangles" kills the first version.
- *The boundary segments that N can count.* Those not wholly outside a face of the tetrahedron of t and R, per far point,
  in doubles with a margin.

**Filters in a local frame (the tests of I and N).** The centroids of a dense region are far smaller than the model, so
in grid coordinates their differences carry little relative precision, and the double filters of the side and
orientation tests fail often; the exact stages on the rational centroids then dominate (55928: about 40 % of the samples in
BigInteger arithmetic). L2 therefore gives the filters coordinates relative to an origin O, the first corner of the input
triangle (an input point, so an integer grid point):

- Each coordinate is the exact value of x - O, rounded once: `ExactPoint3.ToDouble` takes a 64-bit truncated quotient
  (relative error below 2^-63), then one rounding to double (2^-53). That is below 2^-52 relative per coordinate, the
  input assumption of `orient3d_filtered`; coordinates below `TinyCoordinate` give NaN, which the filter refuses.
- `orient3d_filtered` certifies the sign of the exact determinant of points within that relative error of its inputs. Its
  bound already carries the input term: eu, ev, ew are 3 * 2^-52 of the inputs' magnitudes per row, 2^-52 for each of the
  two inputs plus 2^-53 for the subtraction, and they are propagated through the determinant as
  (|u| + eu)(|v| + ev)(|w| + ew) - |u||v||w|, plus Shewchuk's 1e-15 times the permanent for the evaluation.
- The exact determinant of the translated points equals that of the original ones: orient3d is a polynomial in the
  differences b - a, c - a, d - a, and O cancels exactly. So the sign certified for the local values is the sign for the
  exact points.
- The plane sides become -orient3d(U's corners, x) on local coordinates, with the same exact fallback as before (the
  plane equation on the exact point). The sign of orient3d is that of -((b - a) x (c - a)) . (d - a), the plane's normal
  dotted with the point's offset.
- Corners, far points and boundary ends are converted the same way, once per input triangle (cached).

Measured on 55928: classification 295 -> 262 s, the result hash unchanged (61550C3C2F352E72), and verify mode on the
sub-balls as before (176 triangles: 0 differences; 441 triangles: 4, each where the piece's own evaluation is
undecided). What remained was the exact centroid of every piece and its conversion, which together with the GC took 118 s
of pauses.

**Tried and not taken: filters on the centroids' approximations.** The idea was to take each centroid from its corners'
double approximations (`ApproxError`, absolute per coordinate) and compute the exact centroid only for an exact stage.
That needed `orient3d` filters with absolute input errors, and the boundary sum at the approximation with the point's
and the direction's errors in each term's tolerance.
- On the knot benchmark, classification went from 3.5 to 2.3 s, with the hash and the verify results unchanged.
- On 55928 it was slower: classification 262 -> 287 s, hash unchanged, the classification's allocations doubled
  (136 -> 266 GB).
- The cause: the knot lies at about 3e17 grid units, where the approximations of the implicit points carry errors of
  about 1e3 units; even a correctly rounded double is off by up to 32. These errors are larger than the distance between
  neighbouring centroids. Every filter on the segment between two centroids then fails, notably the pierce tests with
  the far point at about 1e21 (771k exact centroids of 999k pieces on the 441-triangle sub-ball). The local frame
  above has no such limit: it rounds the exact difference to the origin, so its error is relative to the triangle's
  size, not the model's.
- Filters on approximations can only gain where those errors are small against the pieces, which is not the case this
  stage is for.

**The centroids from their corners in the frame (implemented after the attempt above).** The approximations fail
because their errors are relative to the model. The corners' coordinates in the frame (the exact difference to the
origin, rounded once, as for the corners of the other triangles) have errors relative to the triangle. So L2 takes each
centroid from its corners there and computes the exact centroid only for an exact stage or the piece's own ray. The
bounds, per coordinate (u = 2^-53):
- *The local centroid* (`Center.Local`). The exact centroid is (A + B + C) / 3 (`centroid_point`), and each corner's
  local coordinate is within e_X = 2.3e-16 max_k |x_k| of the exact one (`LocalFrame.PointError`). The computed
  fl(fl(fl(a + b) + c) / 3) is within (e_A + e_B + e_C) / 3 + 2 u m (1 + u) / 3 + u m (1 + 2u) / 3 of the exact value,
  with m = |a_k| + |b_k| + |c_k|: the corners' errors through the exact average, the two additions' roundings divided
  by 3, the division's. That is below (e_A + e_B + e_C) / 3 + 1.01 u m; the code uses 2.3e-16 m. NaN where a corner is
  (tiny coordinates): the carried test then falls back to the piece's own ray.
- *Its grid coordinates* (`LocalFrame.ToGrid`), for the crossing grid's query and the boundary sum: g = fl(l + o) with o
  the origin rounded to double. The conversion of a BigInteger to double is taken as relative 2^-52 (not guaranteed to
  round to nearest on every runtime), so the error is e_l + 2.3e-16 max|o_k| + 1.2e-16 max_k (|l_k| + |o_k|).
- *The carry length*: |q - p| <= |l_q - l_p| + sqrt(3) (e_p + e_q), from the local coordinates (their errors are small
  against the distance), plus 1e-15 of the coordinates for the distance's rounding.
- *The boundary sum* at g with pointError = sqrt(3) e_g and the direction's error 2 pointError / |R - g|, and the
  larger limit 1e-4 with the two conditions above. On the sub-balls e_g is about 1.5 times the exact point's own
  rounding, not 15 times, so that limit is rarely needed.

**A boundary segment in the triangle's plane, and the pierce test's rows.** Counting the remaining exact centroids by
call site showed one dominant cause: on the 441-triangle sub-ball, 702k of 817k came from the pierce test
t0 = orient3d(a, b, p, q) of a boundary segment a-b against the edge p-q. Its exact value was 0 in 1.51M of 1.70M
calls: boundary segments in the input triangle's plane (its own boundary edges, those of coplanar neighbours), which
no filter can decide. Two changes:
- `LocalFrame.InPlane`: if both ends lie exactly in the triangle's plane (two plane sides of input points, small
  integers, once per segment and block), then t0 = 0 for every p, q of the triangle, without arithmetic on them. That
  is the exact value, so bit-identical.
- `orient3d_filtered_err_near(p, q, a, b)` for the other cases: the same determinant with the rows q - p, a - p, b - a
  (adding a row to another; orient3d(a, b, p, q) = orient3d(p, q, a, b), an even permutation). Hadamard's bound then
  scales with |q - p| |a - p| |b - a| instead of |a - p| |b - p|. For a short segment far from p and q the old form
  could not decide.

Measured, the 441-triangle sub-ball (Union): exact centroids 817k -> 136k of 999k pieces; the 176-triangle one:
226k -> 29k of 242k. Verify mode as before (0 differences; 4, each where the piece's own evaluation is undecided).
55928 Difference, 4 cores: classification allocations 133 -> 82 GB, all allocations 263 -> 213 GB, GC pauses
131 -> 94 s, classification 254 s (291 and 324 s in two runs of the step before, the same day); the trace counts and the
hash 61550C3C2F352E72 unchanged. Knot benchmark: hash unchanged.

Tests (`ExactMeshBooleanTestsCarriedFilters`): the new filter against exact signs at all corners of its absolute error
boxes, and that it decides where the old form fails; exactly coplanar points undecided; the local centroids and their
grid coordinates against the exact centroids, exactly; the near copy of section 4 against the pieces' own rays with
verify; and that few exact centroids are computed (`Counts["l2_exact_centroids"]`). Two bound mutants survive: the
carry length without its error term (the 1e-15 term covers it) and ToGrid without the addition's rounding (the origin's
2^-52 covers it where the conversion rounds to nearest).

**The threshold.**
- Every traced value is that of an Antipodal evaluation with another ray. Its threshold can differ from the piece's own
  evaluation only where the snap decision depends on the ray, that is, where w lies within the bound (at most 1e-6) of a
  half-integer without being on it.
- Undecided points (on or next to a segment) are undecided with every ray.
- The A/B, the verify mode (`VerifyTracedOpenWinding`), evaluates every traced or carried piece with its own ray as well
  and records the margin of each difference: -1 where the piece's own evaluation is undecided or snapped, else the
  distance of its own value to the nearest half-integer. The tests assert that no margin exceeds 2e-6.
- On the 441-triangle sub-ball of the knot: 4 differences in 931k, all where the piece's own evaluation is undecided
  (20 rays without a value) and the carried ray gives one.

**Cost per piece.** One boundary sum (133 atan2 on 55928), the crossing candidates of one grid cell, and the boundary
candidates of the tetrahedron, typically none. Measured, Difference, 4 cores: see the table in section 1.

**Prior art.**
- EMBER (Trettner et al. 2022) traces integer winding numbers along line segments to local reference positions.
- Zhou et al. 2016 propagate integer winding over the cell graph; that needs piecewise-constant winding, so not open
  inputs.
- Jacobson 2016 classifies face barycenters by the GWN, without propagation.

L2 carries the segment tracing over to the generalized winding number with the Antipodal decomposition. The search
found nothing that combines propagation with a certified bound for open meshes.

## 4. L3: triangulation

Measured: on the whole case 10.4 us per piece against 2.4 in the knot, slope 1.36 per triangle, 15 KB allocated per
piece. The plan was, in order: a profile of the expensive triangles; exact stages on implicit points without
BigInteger (fixed-width limbs); cavity insertion of the constraints (Shewchuk & Brown 2015) if the insertion is
superlinear; arrays instead of the dictionaries of `ExactTriangulation2`.

**The profile** (the arrangement alone on 55928, 181 s, sample profiler): about 55-60 % of the samples are in
BigInteger arithmetic (`Multiply` 28 %, the constructor 21 % exclusive). Inclusive: `ExactPoint3.Det3` 37 % (the
exact `Orient`) and `incircle_row` 19 % (the exact `InCircle`), all under `triangulate_touched` (81 %). The
constraint insertion is 3.5 %, the point location 3 %, the dictionaries below 3 %. The exact stages of the expensive
triangles are the cost:
- In the most expensive triangle (6,462 points, 12,670 pieces), 57 % of the Orient calls, 73 % of the InCircle calls
  and 85 % of the Orient calls of the segment phase reach the exact stage.
- On the whole case: 6.2 % and 11.7 % in the point insertion, 22.9 % of the Orient calls in the segment phase.
- The points come from three planes at coordinates of about 2^58 grid units. They exceed the fixed width (2^255 per
  component, docs/design/FIXED_WIDTH_EXACT_POINTS.md), so these stages are BigInteger.

The first filter works on the approximations in grid coordinates. Their absolute errors (about 1e3 units on 55928)
exceed the distances between the points of a dense triangle, the same limit as in section 3. The exact stages,
counted with the exact result and with a filter on coordinates relative to the triangle's first corner:

| exact stages (55928) | total | exactly 0 | decided by the local filter | neither |
|---|---|---|---|---|
| Orient | 59.1M | 20.7M | 37.5M | 1.0M |
| InCircle | 25.0M | 66 | 11.9M | 13.0M |

The local filter never gave a sign different from the exact one.

**Step 1, the local filter (`ExactMeshArrangement.LocalPoints`, bit-identical).** For each triangle, the points'
coordinates relative to its first corner o (an input point, so an integer grid point), in the triangle's projection.
Each one is the exact difference x - o, rounded once, computed when a point first needs it.
- `ExactPoint3.ToDouble` takes a 64-bit truncated quotient (relative error below 2^-63), then converts it to double:
  2^-53 when the conversion rounds to nearest, 2^-52 if it truncates, as .NET Framework may. Both are below
  2.3e-16 |l_k| per coordinate, the bound used. Values near the subnormal range or beyond 1e300 are not used.
- Orient and InCircle are polynomials in the differences of their points (the 2x2 and the lifted 3x3 determinant),
  so o cancels exactly. The sign of the local determinant is that of the exact points.
- The filters are the existing `orient2d_approx` and `incircle_approx` with these coordinates and the error bound
  e = max of the points' bounds. Their bounds take e as the absolute error per coordinate and add the evaluation's
  rounding; their derivations are at the methods, unchanged.
- The filter runs right before the BigInteger stage, after the exact-double and fixed-width stages. Those are cheaper
  than the conversion: on the knot benchmark, where those stages decide, a version before them converted 219k points
  and decided 83.

Measured, Difference, 4 cores:
- 55928: triangulation 163 -> 117 s, total 454 -> 421 s, hash 61550C3C2F352E72 unchanged. 33.9M exact stages were
  decided and 4.2M points converted. The classification (275 s) is not changed by this step; its runs spread by about
  20 s.
- The arrangement alone, two runs each: with the filter before the exact-double and fixed-width stages, triangulation
  120 and 124 s, with 45.2M decided and 7.9M converted; as implemented, 125 and 132 s. The difference is within the
  spread, and on the knot the earlier position allocated 150 MB more, so the filter stays before the BigInteger stage.
- Knot benchmark: triangulation within the noise (medians of five cold runs: 1,553 ms off, 1,455 ms on), hash
  52258C35170BF15E unchanged.

Tests (`ExactMeshBooleanTestsLocalFilter`):
- A knot and a copy rotated by 1e-12, so that every triangle meets its copy almost in its plane: the same results with
  and without the filter, and more than 5,000 decisions.
- Every decision checked against the exact stage on the densest triangles: segment ends with every point (the points
  on the segment give exact zeros), random triples and quadruples, and the Delaunay quadruples of neighbouring pieces.
- Exactly cocircular and collinear rational points beyond the fixed width, 1000 grid units from the corner, where the
  rounding of the local coordinates exceeds the circle's terms. They must stay with the exact stage; without the error
  bound the filter decides them wrongly.
- Similarity invariance, serial against parallel, reuse.

**Step 2, Orient's zeros from the constructions (`LocalPoints.CollinearByConstruction`, bit-identical).** Counted on
55928 by phase (the Orient calls past the first filter): 13.84M exact zeros in the segment phase (a candidate point on
the segment), 2.74M in the point insertion, 2.61M in the constraint insertion, 1.50M before the triangulation. Of the
segment phase's, 13.37M (97 %) lie on the segment's line by their constructions, and none of those had a non-zero
value. The test, in the triangulation's Orient right before the local filter and the BigInteger stage:
- A point lies on a plane K by construction if K is one of its planes (Planes: three; EdgePlane: its plane, and the
  planes on which both ends of its edge lie), or if it is a corner of the triangle whose plane K is
  (`ExactMeshArrangement.OnPlaneByConstruction`; plane keys canonical, merged points by their representative's key).
  It is then exactly on K: a constructed point lies on its planes, a corner on its triangle's plane, and an edge with
  both ends on K in K.
- Every point of a triangle's arrangement lies in its plane. If three of them lie on a plane K that meets that plane in
  a line, they are collinear and Orient is 0. "Meets in a line" is checked exactly, once per K and triangle: the cross
  product of the two normals is not 0. Without that check, a plane coplanar with the triangle under its own key
  (`ShareCoplanarConstructions` off, or coplanar planes not shared) would make any three points of it "collinear".
- Also three points on one of the triangle's edges (its corners, and EdgePlane points of that edge).
- The test is sufficient, not necessary: where it does not apply, the filters and the exact stage decide as before.

Measured, Difference, 4 cores:
- 55928: 9.0M zeros so decided (those that reached the BigInteger stage), triangulation 116 s (117-128 s in the runs of
  the step before), triangulation allocations 117 -> 104 GB, hash 61550C3C2F352E72 unchanged.
- Knot benchmark: triangulation allocations 1.3 -> 0.7 GB (points converted for the local filter 219k -> 176), hash
  unchanged.

Tests: the constructions' zeros against the exact stage (the segment triples of the densest triangles), a plane
coplanar with the triangle under its own key not taken as evidence, the counts. Mutants of each case killed.

**The GC and the bit sizes (measured on 55928, after step 1).**

GC upper bound, Difference, 4 cores, 263 GB allocated in both configurations. The second configuration is runtime
settings only (`DOTNET_gcServer=1`, `DOTNET_gcConcurrent=0`, `DOTNET_GCgen0size=0x40000000`):

| GC | total | classification | triangulation | GC pauses | collections gen0 / gen1 / gen2 | peak RSS |
|---|---|---|---|---|---|---|
| default (workstation, concurrent), two runs | 474 / 469 s | 324 / 291 s | 120 / 140 s | 131 / 136 s | 2470 / 1695 / 24 | 10.2 GB |
| server, not concurrent, gen0 1 GB | 358 s | 225 s | 100 s | 44 s | 211 / 170 / 42 | 10.0 GB |

So allocating nothing could save at most about 110 s of the 470.

Bit sizes of the exact points, and why the fixed width (4 limbs, 2^255 per component) does not reach them:
- Input grid coordinates: up to 56 bits. The triangles' planes: normals up to 101 bits, D up to 156.
- Points from an edge and a plane (1.20M) and from two segments (3.9k): all fixed width (X up to 192 bits, W up to 138).
- Points from three planes (2.22M): 1.13M fixed width; the rest up to 309 bits in X and 255 in W, so BigInteger.
- Piece centroids, unreduced as `centroid_point` builds them (every 50th piece): X up to 807 bits, W up to 753, most
  between 640 and 800 bits.
- So a plane side of a centroid takes about 910 bits, and its 4x4 orientation test about 3,200. In the triangulation,
  Orient on the larger points takes about 875 bits and InCircle about 3,300.

**Next, by the counts above.**
- The exact zeros of Orient: step 2 above.
- The InCircle stages that the local filter leaves (13.0M, nearly cocircular quadruples of the Delaunay flips): an
  adaptive stage, the differences to the fourth point first.
- Only then fixed-width limbs for the larger implicit points, and the dictionaries.

## 5. The fail-fast budget (additive API)

**Where it is checked.** Right after the merge of the crossings, before the triangulation (about 25 s into 55928, under
3 % of the run). The piece count is then known exactly without triangulating. A touched input triangle with i distinct
canonical points inside it and e on its edges, the corners not counted, is triangulated into 2 i + e + 1 triangles by
Euler's formula, whatever the constraints. That is O(points) from the point sets that `triangulate_touched` builds
anyway.

**API** (`ExactMeshBoolean`; fork-only, not in 1.0.324):
- `public long MaxPieces` (default 0 = unlimited). When the predicted pieces exceed it: `Status = BooleanStatus.BudgetExceeded`
  (a new enum member at the end), `Result = null`, and `Compute()` returns false.
- `public System.Threading.CancellationToken Cancellation` (default `CancellationToken.None`; **owner 2026-11-11: yes**,
  additive, after the precedent of the `Cancelled` statuses of MeshAlphaWrap / TerrainSlopeDesign and the token of
  `QualityMesh2.Compute`). It is checked between the phases and every few thousand items of the long parallel loops
  (triangulation, classification). Cancellation gives a new enum member at the end, `BooleanStatus.Cancelled`, with
  `Result = null`, and `Compute()` returns false. It never leaves the instance half-updated: a later `Compute()` starts
  from the inputs.
- **Deferred (owner 2026-11-11):** `MaxOpenWindingEvaluations`, a limit on the open-winding evaluations that L1 does not
  decide per patch, is not part of the stage. After L1 and L2 the open models of the final run are measured again. It is
  proposed only if a case remains that `MaxPieces` / `Counts["predicted_pieces"]` does not catch.
- `Counts["predicted_pieces"]` is always set. Also `Counts["l1_patch_decided"]`, `["l2_traced"]`, `["l2_direct"]`.
- The n-ary paths (`IntersectEach` / `DifferenceEach`) pass the options through `configure`. A body that exceeds the
  budget gets `BudgetExceeded` in its status slot.
- With the defaults nothing changes: the API validation of `dotnet pack` sees only additions.

## 6. Tests and gates

- **L1:**
  - an A/B flag that also evaluates every piece and asserts the same threshold. Run on the Antipodal test cases, an
    open sheet with holes near and far from the other operand, and a procedural crumpled knot (a strip folded many
    times through itself, about 1-2k triangles). No mesh files are committed.
  - Invariance over `Similarity.All()` (bit), serial = parallel, repeated runs, reuse (Compute twice).
  - Mutation targets: the margin check, the D_s threshold, the factor 2 of the path, the stack exclusion.
- **L2:** the same A/B against the recorded per-piece open winding. Its count of differing pieces is 0 on the test set,
  and on the open models of the final run a count with the margin of each difference, reported in the PR. Also:
  - pieces whose segment passes through an edge or vertex of M, or a boundary segment in the strip, must take a direct
    evaluation;
  - the telescoped error bound against an exact rational evaluation on a small case.
- **L3:** the result hash unchanged on the BooleanComparison workloads and the Lévy samples; the A/B of the CDT on
  cocircular grids.
- **Budget:**
  - predicted = actual pieces on random cases, including degenerate and coplanar ones;
  - `BudgetExceeded` with no result, `Counts` set, the instance reusable;
  - cancellation before and inside each long phase (`Cancelled`, no result, the instance reusable);
  - each test under 5 s.
- **Benchmark:** 55928 in `benchmarks/BooleanComparison` (the case directory fetched by `runs/final/fetch_thingi.py`,
  not committed), before and after per lever, Difference and Union. A procedural knot workload runs in CI time.
