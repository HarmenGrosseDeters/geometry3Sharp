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
candidates of the tetrahedron, typically none. Measured, Difference, 4 cores: see the table in section 7.

**Prior art.**
- EMBER (Trettner et al. 2022) traces integer winding numbers along line segments to local reference positions.
- Zhou et al. 2016 propagate integer winding over the cell graph; that needs piecewise-constant winding, so not open
  inputs.
- Jacobson 2016 classifies face barycenters by the GWN, without propagation.

L2 carries the segment tracing over to the generalized winding number with the Antipodal decomposition. The search
found nothing that combines propagation with a certified bound for open meshes.

## 4. L3: triangulation

Measured: on the whole case 10.4 us per piece against 2.4 in the knot, slope 1.36 per triangle, 15 KB allocated per
piece. In order:

1. A profile of the 200 most expensive triangles (time, predicate calls, exact stages and allocations per triangle),
   to separate the exact stages, the constraint insertion and the dictionaries.
2. The exact stages on implicit points without BigInteger allocations: fixed-width limbs, as `FixedPoint3` does for
   rational points (docs/design/FIXED_WIDTH_EXACT_POINTS.md, bound table). This is bit-identical, because the predicate
   values are exact.
3. If the constraint insertion is superlinear on the large triangles (flips with a queue, O(k^2) per long
   constraint): insertion by cavity retriangulation (Shewchuk & Brown 2015, "Fast segment insertion and incremental construction of constrained Delaunay triangulations"; Anglada 1997). This is bit-identical where the
   constrained Delaunay triangulation is unique. With four cocircular points the tie-breaking can permute the pieces;
   the A/B shows whether it does.
4. Arrays instead of the `edgeTri` / `vertexTri` dictionaries of `ExactTriangulation2`. This is bit-identical: the same
   operations in the same order.

Estimated: 172 s to about 40-80 s on 55928.

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
