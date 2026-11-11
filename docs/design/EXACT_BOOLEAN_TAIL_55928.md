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

## 3. L2: short segments to reference points instead of far rays

The owner accepted L2 although it is not strictly bit-identical. It needs this proof, written out, and an A/B against
the recorded per-piece open winding (`RecordOpenWinding`, as in the Antipodal A/B).

**Decomposition** (the Antipodal Method, Martens, Trettner, Bessmeltsev 2026, eq. 6 and 9). Take a fixed unit direction
r. Let chi_r(x) be the signed crossings of the ray x + t r (t > 0) with M, and Omega_r(x) the boundary sum with
x1 = -r. Then

    w(x) = chi_r(x) + Omega_r(x) / 4pi

for every x off M and off the cone K_r = { y + t r : y on the boundary, t > 0 }. chi_r and Omega_r jump by opposite
amounts across K_r, where the ray passes through the boundary, so their sum is continuous.

**Difference of two points with the same r.** Let S be the strip swept by the segment [p, q] along r. Its boundary is
the closed curve p -> q -> q + infinity r -> p + infinity r -> p. Counting the signed crossings of M along that curve:

    chi_r(q) - chi_r(p) = X_M(p -> q) + N_r(p, q)

- X_M(p -> q) is the signed crossings of the segment p -> q with M.
- N_r(p, q) is the signed number of boundary segments that cross the strip S: the intersection of a closed curve with M
  changes only where the boundary passes through the surface it spans.
- Both are integers and exact. X_M is the exact segment-triangle test that `front_crossings` already makes, over the
  box tree, for a short segment. N_r is an exact test of a segment against a strip: two orient3d signs and a side test,
  on input points and the exact centroids.

So

    w(q) = w(p) + X_M(p -> q) + N_r(p, q) + (Omega_r(q) - Omega_r(p)) / 4pi

**Telescoping.** Along a chain of pieces ref = q0, q1, ..., qk = q (a BFS tree over the piece adjacency of one
connected part of the output), all with the same r:

    w(q) = w(ref) + sum_i [X_M(q_i -> q_i+1) + N_r(q_i, q_i+1)] + (Omega_r(q) - Omega_r(ref)) / 4pi

The intermediate Omega terms cancel algebraically, so only Omega_r(q) and Omega_r(ref) are computed in doubles. The
error does not grow along the chain: it is at most e(ref) + e(q), each the bound of one boundary sum of
`antipodal_front_winding`, at most 1e-6. The integers are exact.

**The threshold.**
- Let w~(q) be the L2 value with the bound e = e(ref) + e(q), snapped to the nearest half-integer when it is within e
  of one, by the same rule as `snap_half`.
- Then ceil(w~(q) - 1/2) differs from the threshold of the true w(q) only if w(q) lies within e of a half-integer
  without being on it. That is the same class of pieces where today's per-piece evaluation is not exact either: it
  snaps within its own bound, "one Seed gives one result". An exact tie (in front of a flat part of a sheet) is
  snapped by both methods and decided by the same rule.
- So L2 can differ from today only for pieces whose winding lies within 2e-6 of a half-integer but not on it, and for
  pieces where today's evaluation is "undecided" (those keep a direct evaluation).
- The A/B counts both. `antipodal_snapped` is 0 on 55928.

**Front sides and degenerate paths.**
- The endpoints lie on pieces. A piece's w is the limit from its front side, as in `front_crossings`.
- At the start of a segment, every triangle of the stack of q_i that the direction q_i+1 - q_i leaves towards its back
  counts as a crossing, with its sign. At the end, every triangle of the stack of q_i+1 that the segment reaches from
  its back counts, with its sign. A segment in the plane of the piece crosses nothing there. These are exact orient
  signs of the direction against the triangle planes.
- Ties are resolved by another path or a direct evaluation, never by perturbation:
  - a segment through an edge or vertex of M;
  - a boundary segment in the plane of the strip;
  - an endpoint on K_r.

  The piece is then evaluated directly (a full Antipodal evaluation) and becomes a new reference.
- A degenerate case triggers a direct evaluation; it is counted (`Counts["l2_direct"]`).

**Cost per piece.** One boundary sum (133 atan2 here). A short segment's crossings over the box tree: a few triangles,
against 480-890 for a far ray. A strip test over the boundary grid. Expected classification: about 60-100 s on 55928;
with L1 as the first filter (a whole patch takes its representative's threshold) about 40-80 s.

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
