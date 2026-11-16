# A certified patch bound for `CertifiedUpper` (#848, design note)

Status: design and upper-bound measurement only, no library code (2026-11-16). Follows #742 / #847
([HAUSDORFF_CERTIFIED_UPPER.md](HAUSDORFF_CERTIFIED_UPPER.md), "B1" to "B6" below mean its sections) and the
bound-review finding M3.

**Result in two lines.** The patch bound can be made certified without an exact-arithmetic kernel: a frame-free sign
predicate with a *derived* running error bound (about 22 ns, measured), a conservative "not covered" on every doubtful
sign, and no margin constant. The gain on a reduced mesh against its original is 61 to 100 % fewer subdivisions and
5 to 98 % less time (12 to 98 % on the reduced rows) with the uncertified patch bound as the oracle. Recommendation: **go for the filtered-double design,
no-go for an interval-only one** (section 8). Whether it pays depends on the certified patch bound costing at most
about 1.4 to 5 times the uncertified one (section 7), so the implementation starts with a spike that measures exactly that.

## 0. What is certified today and what is not

`TriangleDeviationBounds.patch_bound(p0, p1, p2, n0, n1, n2)` bounds max over x in Q = (p0, p1, p2) of d(x, Target) by
the largest height, along Q's normal, of the target triangles under Q, at the corners of the clipped polygons. Its
argument has two kinds of step:

| Step | Kind | Today |
|---|---|---|
| Projection along n; t' = projection of a target triangle t; for y in Q' and t' there is one point z of t above y, h is linear on t | real arithmetic | exact, unchanged |
| Coverage: weights w(y) = number of positive t' of S that contain y; constant across non-boundary edges; so if no boundary edge of S meets Q' and some t' meets Q', Q' is covered | real arithmetic | exact, **unchanged here** (see the review note in section 4) |
| The frame (`ux`, `uy`, `nrm`), the projected coordinates, the orientation signs, `segment_meets_triangle` with `margin = 1e-9 * scale` | floating point | **estimated** |
| Heights at the clip points (`clip2`: Sutherland-Hodgman with `dp / (dp - dr)`) | floating point | **estimated**: slack `16 * MaxPatchSlope * shape * eps * (scale + rmax + hmax)` |

The slack is an estimate, as the comment says; `MinPatchShape` and `MaxPatchSlope` exist only to keep the estimate
plausible. This note replaces all four floating-point items by arguments that hold under rounding. The selection of S
(growth over shared edges, the box test, `MaxPatchTriangles`) stays a heuristic in doubles: **any** choice of S is
sound, because a triangle left out only adds boundary edges, and those are checked (item 2 of the comment).

## 1. Look before you build

### 1.1 The library

| Building block | Where | Use here |
|---|---|---|
| `ExactInterval` (every operation rounded to nearest, then one step out) | `math/ExactIntervals.cs` | enclosures; `Up` / `Down` helpers; the fallback for single quantities |
| `CertifiedTriangleBounds.up`, `max` (NaN gives +infinity), `LengthUpper`, `SquaredLength`, `sqrt_up`, `CutRounding` | `queries/CertifiedTriangleBounds.cs` | the norm of w; the NaN rule; the rounding rule M1 (one `Up` per rounded operation) |
| `AdaptivePredicates.Orient2d / Orient3d` (Shewchuk, exact, adaptive) | `math/AdaptivePredicates.cs` | exact signs, but only for **axis-aligned** frames (section 2); returns the sign only, not a value with a bound |
| `TriangleDeviationBounds.certified_clip` / `split` (#847 B3: any sign pattern, cut points with an allowance `off`) | `queries/TriangleDeviationBounds.cs` | the idea "a superset of the exact pieces is enough"; its allowance `off` is not needed here (section 6) |
| `MeshHausdorffDistance` `CertifiedUpper` plumbing (`HausdorffRounding.TriangleUpper` adds the child's `off`) | `mesh_ops/MeshHausdorffDistance.cs` | unchanged: the patch bound is for the triangle with the given double corners |
| `ExactPoint3`, `ExactLimbSigns`, `ExactSlopePredicates` | `math/Exact*.cs` | integer / rational kernels for the booleans, heavier than a 3x3 sign needs; not used |

There is no value-with-bound determinant in the library: `Orient3d` returns an `int`. This note adds one small helper
(section 3), not a new kernel.

### 1.2 The literature (a short check, abstracts and listings only; papers, no code)

- L. Sacht, A. Jacobson, *Cascading upper bounds for triangle soup Pompeiu-Hausdorff distance*, Computer Graphics
  Forum 43(5), 2024 (SGP 2024), [arXiv 2406.10357](https://arxiv.org/pdf/2406.10357): the cascade; no rounding analysis. The patch
  bound is the library's own extra bound.
- J. R. Shewchuk, *Adaptive precision floating-point arithmetic and fast robust geometric predicates*, Discrete &
  Computational Geometry 18, 1997, [doi 10.1007/PL00009321](https://www.doi.org/10.1007/PL00009321): the filter structure (a double evaluation, an error bound
  from the permanent, an exact fallback only when the sign is not decided). Our bound is derived afresh in section 3
  with the standard model; Shewchuk's `o3derrboundA = (7 + 56 eps) eps` is for comparison only.
- H. Brönnimann, C. Burnikel, S. Pion, *Interval arithmetic yields efficient dynamic filters for computational geometry*,
  Discrete Applied Mathematics 109, 2001, and O. Devillers, S. Pion, *Efficient exact geometric predicates for Delaunay
  triangulations*, ALENEX 2003: filter hierarchies (static, semi-static, interval, exact) and their measured costs.
  Our measurement (section 7, interval about 5 times the filtered double) is the same ordering.
- N. J. Higham, *Accuracy and Stability of Numerical Algorithms*, 2nd ed., SIAM 2002, section 3.1: the
  standard model and `gamma_n = n u / (1 - n u)` used in section 3. R. E. Moore, *Interval Analysis*, 1966 (already in
  SOURCES.md).
- Guaranteed-error simplification: J. Cohen et al.,
  *Simplification envelopes*, SIGGRAPH 1996 (offset surfaces, not a Hausdorff bound).

Nothing found that certifies a coverage-by-projection bound; the closest practice is exactly the filter hierarchy above.

## 2. Part 1: the projection frame

**Today.** `nrm` = normalized cross product, `ux`, `uy` orthonormal in doubles, projected coordinates by dot products:
every one of them is rounded, and so are the signs computed from them.

**Rigorous version: no frame is computed.** Let w be any double vector (the rounded normal of Q, `fl(e1 x e2)`;
how it was computed does not matter, it is just a given vector). Project along w: the projection
pi_w maps R^3 onto a plane transversal to w. Everything the argument needs about the projection is the sign of the
projected orientation of three points p, q, r, and that sign is a triple product on the *input doubles*:

    T(p, q, r) = det[ q - p, r - p, w ]      (positive: pi_w(p), pi_w(q), pi_w(r) counter-clockwise, given T(Q) > 0)

(the 2D area form of the projected plane is a fixed positive multiple of T, the same for every triple, which is also
why ratios of T values are exact ratios of projected signed areas, used in section 6). No projected coordinate is ever
computed, so there is no frame rounding to bound. The argument holds for any w with `T(p0, p1, p2) > 0` certified
(otherwise return +infinity: Q is thin, edge-on to w, or degenerate; this replaces `MinPatchShape`).

**Why not an axis-aligned w (exact `Orient2d`).** With w = a coordinate axis, T is a 2D orientation of two coordinates of
the input doubles and `AdaptivePredicates.Orient2d` decides it exactly, with no filter. But the bound it gives is
the height along that axis, not along the normal: it is larger by 1/cos(theta), theta the angle between the
normal and the axis, up to sqrt(3). The measurement of section 7 (column "axis-aligned") shows the loss: the subdivisions
saved drop from 61 / 90 / 99 / 94 % to 45 / 58 / 68 / 81 % on four rows, and the break-even factor k* falls
(1.4 to 0.8, 3.5 to 1.4, 19.5 to 4.2, 4.9 to 3.6). Kept as the fallback if the filter of section 3 turns out hard to review.

## 3. The sign and value of a determinant with a derived running bound

One helper `det3(r1, r2, r3)`, rows being three double vectors that are each a rounded difference of two input points
(`fl(q - p)`) or, for w, an input itself. Notation: u = 2^-53; every rounded operation `fl(a o b) = (a o b)(1 + d)`,
`|d| <= u`, unless it overflows or underflows (handled below).

**Computation** (Cramer's rule on the first row):

    m1 = r2y r3z, m2 = r2z r3y, m3 = r2z r3x, m4 = r2x r3z, m5 = r2x r3y, m6 = r2y r3x
    det_c  = r1x (m1 - m2) + r1y (m3 - m4) + r1z (m5 - m6)
    perm_c = |r1x| (|m1| + |m2|) + |r1y| (|m3| + |m4|) + |r1z| (|m5| + |m6|)

**Claim.** If no operation overflows, and A = 2^-1070 (M + 1), M the largest absolute entry of the rows, then
the exact determinant Delta of the *exact* difference rows satisfies

    | det_c - Delta |  <=  E = 10 u perm_c + A.

**Derivation.**

1. *Rounded rows.* Entry rho_ij = d_ij (1 + theta_ij), |theta_ij| <= u (the rounding of the difference; theta = 0
   for w, and for a difference that is exact). Each monomial of the determinant takes one entry from each row, so
   `| det(rho) - Delta | <= ((1 + u)^3 - 1) perm(d) <= 3.0000001 u perm(d)`, where perm(d) is the sum of the absolute monomials.
2. *Evaluation.* Every monomial of the formula passes through at most five rounded operations: one product (m), one
   subtraction inside the bracket, one product with the first-row entry, and at most two additions (the first two
   terms pass both). Hence `| det_c - det(rho) | <= gamma_5 perm(rho)` with `gamma_5 = 5u / (1 - 5u)`, and
   `perm(rho) <= (1 + u)^3 perm(d)`.
3. *Together:* `| det_c - Delta | <= (gamma_5 (1 + u)^3 + 3.0000001 u) perm(d) <= 8.001 u perm(d)`.
4. *The computed permanent.* All its terms are non-negative and it also passes through at most five roundings:
   `perm(rho) <= perm_c / (1 - gamma_5)`, so `perm(d) <= perm_c (1 + 8.1 u)`, and `| det_c - Delta | <= 8.002 u perm_c`.
5. *The constant.* 10 u is a double (10 = 101b x 2 has three significant bits) and exceeds 8.002 u by 1.99 u, which covers the one
   rounding of `10 u * perm_c` and of the sum with A (both evaluated rounded up, below).
6. *Underflow.* A difference of doubles that is subnormal is exact; a sum or difference in the subnormal range is
   exact; only the nine products can lose at most 2^-1075 each. The six inner ones are later multiplied by an entry of
   r1 (at most M). So the absolute loss is at most `(6 M + 3) 2^-1075 (1 + 10 u) < 2^-1072 (M + 1)`, and the same
   amount bounds the deficit of `perm_c`. A = 2^-1070 (M + 1) covers both with a factor 4 to spare.
7. *Overflow / NaN.* If `det_c`, `perm_c` or E is not finite, the result is "undecided" (+infinity for a value). A NaN
   never compares true.

**Sign.** `E_up = Up(Up(10 u perm_c) + A)` (each `Up` wraps one rounded operation, rule M1).
`sign = +1` if `det_c > E_up`, `-1` if `det_c < -E_up`, else *undecided*, which callers treat as "cannot show".
`Delta` has the stated sign: `|det_c - Delta| <= E <= E_up`.

**Value.** `S = Up(|det_c| + E_up)` is an upper bound of `|Delta|`, and `Lo = Down(det_c - E_up)` a lower bound of
`Delta` (rounding down is `ExactInterval.Down`).

**Assumptions.** No FMA contraction and no x87 extended precision (RyuJIT does not contract; net48 runs SSE2 on x64; the note's x86 case follows the rule of B1: one rounding per operation, which extended precision only improves, except double rounding on a store, which the count of five roundings already allows for). The helper's file should be added to the exact-code list of `.editorconfig` so that G3E0001-3 enforce it.

**Rows are reused.** `e1 = fl(p1 - p0)`, `e2 = fl(p2 - p0)` of Q, and `(b - a)`, `(c - a)` of each target triangle, are
computed once and shared by all dets using them: the analysis above holds for any rounded rows.

**Sanity check done for this note** (not a test of the library): 200 000 random rows against exact rational arithmetic,
scales 2^-30 to 2^30, differences down to 1e-16 of the coordinates, near-dependent rows: the largest observed `|det_c - Delta| / (10 u perm_c)` is 0.30
(the bound held every time; the typical slack is about 3). The same script goes into the tests of the implementation PR.

**Not claimed:** tightness of E (it is about 1.4 times Shewchuk's constant for the same shape). A doubtful sign only
costs a refused bound. The implementation must also test it against an exact evaluation (`System.Numerics.BigInteger`
or the exact kernels) on random and near-degenerate rows, as #847 tests its bounds.

## 4. Part 2: the coverage test

All tests are signs of T on input doubles. Let `sigma(p, q, r)` be the sign by section 3; undecided is never positive
or negative.

1. **Q is positive:** `T(p0, p1, p2) > 0`. Its lower bound `D_Q = Down(det_c - E_up)` is kept (section 6).
2. **t joins S** only if `T(a, b, c) > 0` (`D_t` lower bound kept). Replaces `area2 > 1e-9 tscale^2` and the slope test,
   which existed for the rounding estimate. A sliver or an almost edge-on triangle fails the certification and is
   rejected, like today's rejected triangles.
3. **The boundary edges of S** (combinatorial, from the indices, as today) must not meet Q'. Exact disjointness of a
   segment pi(a)pi(b) and the triangle Q' (both closed, convex) holds iff a separating line exists, and the separating
   axis theorem lists the candidates: the line of the segment, and the three lines of Q' (2D, two convex polygons, one of
   them degenerate). A **certified strict** separation is:
   - `sigma(a, b, p_i)` equal for i = 0, 1, 2 and nonzero (Q' strictly on one side of the line of the edge), or
   - for some edge (p_j, p_k) of Q': `sigma(p_j, p_k, a) = sigma(p_j, p_k, b) = -1` (the whole segment strictly outside
     that edge of the positive triangle).
   Strict inequalities of exact-sign quantities are exact statements, so the old `margin` is not needed (section 5).
   If neither holds, return +infinity.
4. **Witness:** some t in S has a vertex v with `sigma(p_j, p_{j+1}, v) = +1` for j = 0, 1, 2 (strictly inside Q'), or
   some corner p_i of Q has `sigma(a, b, p_i) = sigma(b, c, p_i) = sigma(c, a, p_i) = +1` for some t in S (strictly inside t').
   Then w >= 1 at a point of Q'. Replaces `hasHeight`, which was a nonempty floating-point clip.

The per-vertex values `sigma(p_j, p_{j+1}, v)` (three per distinct target vertex v) are computed once and cached;
the boundary test (3) and the witness (4) reuse them. The corner tests of (4, second form) are computed only when the
first form fails.

**Review note (not changed, but the certificate now rests on it).** The argument "w is constant across a non-boundary
edge" needs both triangles *positive* (certified now, step 2) and the edge traversed in opposite directions (`traverses`,
combinatorial). The claim for a vertex inside Q' ("closed fan covers a disc") and the connectedness of Q' minus the
finite set of vertices are real-number arguments, unchanged. The bound review should re-read them once, since the old
floating-point step could hide a mistake there and the new one does not.

**The selection of S.** S is chosen in doubles: the box test, the growth over
shared edges, `MaxPatchTriangles = 256`. None of it needs a proof: a triangle not in S contributes its edges as
boundary edges (step 3) or is not needed.

## 5. The margin

`margin = 1e-9 * scale` made the floating-point "meets" test insensitive to rounding. It is **removed**: the boundary
test of step 3 only accepts a strict, certified separation, so an edge that comes closer to Q' than the rounding of
the sign can resolve is refused; the width of the refusal zone is E of section 3 (derived, about 1e-15 of the
quantities in a well-conditioned case). There is no tolerance parameter and no scale. The old `margin` refused every edge within 1e-9 of Q',
so the new test accepts **more** in that regime; it refuses more only where E is large (ill-conditioned rows, steep targets, a nearly edge-on Q). Edges that touch Q' exactly (sign 0, e.g. a Q corner on an
original boundary edge) are refused in both versions.

## 6. Part 3: the heights at the clip points

Let the polygon K = t' intersected with Q' (the exact one). The distance bound for a point x of Q over y in K is
`|z - x|`, z the point of t above y, x the point of Q above y; z - x is parallel to w. Both are on the same line
along w, so the gap is a linear function of y on K (affine on a convex polygon), and its absolute value is at most its
value at a vertex of K.

**Gap formulas (exact, then bounded).** Let `N_Q = e1 x e2` (the exact cross product), so `D_Q = N_Q . w = T(p0,p1,p2)`.
For a point z with `z - x = kappa w`, `x` in the plane of Q: `kappa = N_Q . (z - p0) / D_Q`, and

    |z - x| = |w| |det[e1, e2, z - p0]| / D_Q          (z a vertex of t or a point on an edge of t)
    |z - x| = |w| |det[b - a, c - a, x - a]| / D_t     (x a corner of Q, z the point of t above it)

**Vertices of K.** Each is exactly one of: (V) a vertex of t' inside the closed Q'; (Q) a corner of Q' inside the closed
t'; (C) the crossing of an edge (a, b) of t' with an edge (p_j, p_k) of Q' (touching and collinear overlaps are
(V) or (Q)). A candidate list that contains **at least** these (a superset: more candidates only loosen) is the
replacement for `clip2`; no clipping is computed:

- (V) every vertex v of t with no `sigma(p_j, p_{j+1}, v) = -1`. Value: `g_V = Up(Fw * S_v)` with
  `S_v` = value bound (section 3) of `det[e1, e2, v - p0]`, and `Fw = Up(W / D_Q)`, `W = LengthUpper(0, w)` (an upper
  bound of |w|) divided by the lower bound `D_Q > 0`.
- (Q) every corner p_i of Q with no `sigma(a, b, p_i) = -1` for the three edges of t (positive triangle). Value:
  `g_Q = Up(Up(W * S_q) / D_t)` with `S_q` the value bound of `det[b - a, c - a, p_i - a]`.
- (C) every pair (t'-edge (a, b), Q'-edge (p_j, p_k)) that is *not certified disjoint*: not both of `sigma(p_j, p_k, a)`,
  `sigma(p_j, p_k, b)` equal and nonzero, and not both of `sigma(a, b, p_j)`, `sigma(a, b, p_k)` equal and nonzero.
  (A proper crossing has strictly opposite signs on both, so it is always a candidate.) Value below.

**The cut point (C).** The point z = a + lambda (b - a) is on the 3D edge of t, and projection along w is affine, so
the *same* lambda is the parameter of the crossing in the projected plane: `lambda = P_a / (P_a - P_b)` with
`P_a = T(p_j, p_k, a)`, `P_b = T(p_j, p_k, b)` (exact signed areas up to the common positive factor). The gap is linear
along the edge: `N_Q . (z - p0) = (1 - lambda) s_a + lambda s_b`, `s_a`, `s_b` the exact values of the dets of the
vertices a, b. Hence for **any** lambda in [0, 1]:

    |gap(z)| <= max(g_V(a), g_V(b))                                     (convex combination; always valid)

and, with an enclosure `lambda in [lo, hi]`, also
`|N_Q . (z - p0)| <= (1 - lo) S_a + hi S_b`. The enclosure exists when `P_a`, `P_b` are certified strictly opposite:
`P_a in [P0, P1]` with `P0 > 0`, `P_b in [R0, R1]` with `R1 < 0` (the intervals are `det_c -/+ E_up` of section 3;
the other orientation by negation). `lambda(P, R) = P / (P - R)` is increasing in both P and R on that domain
(`d/dP = -R / (P - R)^2 > 0`, `d/dR = P / (P - R)^2 > 0`), so

    lo = Down( P0 / Up(P0 - R0) ),     hi = Up( P1 / Down(P1 - R1) ),     with  P1 - R1 > 0,  P0 - R0 > 0.

Each `Up` / `Down` wraps one rounded operation (M1). The candidate's value is
`g_C = min( max(g_V(a), g_V(b)), Up(Fw * Up(Up((1 - lo)^ * S_a) + Up(hi * S_b))) )`, with `(1 - lo)^ = Up(1 - lo)`. `Math.Min` propagates NaN: a NaN in `lo`, `hi` or any value must end as +infinity (the `up` / `max` helpers of the library do that).
**Where the signs are not certified opposite** (parallel edge, rounding-sized `P`), `lo = 0`, `hi = 1`, and the first
term, `max(g_V(a), g_V(b))`, is used: **the 1 / angle amplification of the old slack becomes a looser bound, never an
unsound one**, and the cut-point allowance `off` of B3 is not needed, because no cut point is ever stored: the
bound is on the exact crossing, enclosed in `lambda`.

**Why the superset is sound.** `K` is the convex hull of its exact vertices; each exact vertex is in the candidate
list (its exact signs are never certified wrongly); each candidate's value bounds the gap at the exact vertex it
stands for; so the maximum over the list bounds the maximum over K. A candidate that stands for no exact vertex only
adds a value, which is allowed. (Same argument as B3: any sign pattern, the pieces cover.)

## 7. Part 4: the result, the cost, the expected gain

**Result.** `u_patch = max` over t in S and all candidates of their values. Then, as today, in `UpperBound`
(certified branch, after the cascade `u0, u1, u2`):

    if (!(u <= threshold)) u = Math.Min(u, CertifiedTriangleBounds.max(dmax, u_patch));    // NaN -> +infinity

`dmax` is the certified corner maximum, so the result is never below the corners' certified distances. Every bound is
rounded up per operation; the child's `off` is added by `HausdorffRounding.TriangleUpper`, unchanged. The patch bound
does not depend on the distances of the corners, so there is no new dependency on B1.

**Refusals (+infinity).** `T(Q) <= 0` undecided or not positive; a boundary edge without a certified separation; no
witness; `|S|` above `MaxPatchTriangles`; a NaN. All are as safe as today's `PositiveInfinity` returns.

**Cost model.** Per distinct target vertex 3 dets (cached), per triangle of S 1 det (orientation) plus, for triangles
that cross Q' only (not all three vertices certainly inside), up to 9 corner dets and the (C) candidates; 3 dets for the
V values. A `det3` with filter measures **22 to 26 ns** (single thread, 4096 random small triangles, `predcost`); the
interval version of the same sign (`ExactInterval` semantics) **108 to 115 ns**, `AdaptivePredicates.Orient2d` **50 to 59 ns**.
Today's uncertified call costs 13 to 29 us on the reduced rows (about 5 us on the two small rows, where the same 100 to 150 dets are 1.5 to 2 times
the call, which is why k\* 1.6 is the row to watch) ((p) divided by the number of calls; most of it is the
growth of S, the dictionary and the box tests, which stay as they are). For |S| = 30 the certified signs and values
are about 100 to 150 dets, 2.5 to 4 us with the filter, 12 to 17 us with intervals. I estimate the certified patch
bound at **1 to 2.5 times** the uncertified one with the filter, and **4 to 6 times** with intervals only (an
estimate, to be replaced by the spike, section 9).

**Upper-bound measurement.** `benchmarks/HausdorffBenchmark`, mode `patch` (`PatchOracle.cs`): a one-sided branch and
bound A -> B written out in the benchmark (the library's pieces: corner distances, 4-split by midpoints, drop when
u <= lower, stop when the largest u <= lower + tolerance, tolerance 1e-4 of the diagonal), using the library's
**certified cascade** (`Certified = true`, certified corner distances B1) and, as the oracle, the existing uncertified `patch_bound`
by reflection in `u = min(u, max(dmax, patch_bound))` whenever `u > lower`. No library code changed; the midpoints are rounded without the allowance
(counts only). Sequential, one thread (the times are not pool noise; the library's own parallel run has the same ratios).
Reduced meshes: `Reducer.ReduceToTriangleCount(n / divisor)` of procedural meshes (bumpy sphere with 5 % noise, and
a smooth sphere).

    dotnet run --project benchmarks/HausdorffBenchmark -c Release -- patch 5
    dotnet run --project benchmarks/HausdorffBenchmark -c Release -- predcost

Output (container, 4 cores, median of 5 runs; `(p)` = patch time inside; `k*` = how many times slower the
certified patch bound may be than the uncertified one before the run is no faster than without a patch bound,
`(T_without - (T_with - P)) / P`; "axis-aligned" = the oracle scaled by 1 / (largest component of Q's unit normal), the
loss of an exact `Orient2d` frame, section 2):

| case (tris A / B) | no patch: subdiv, ms | patch oracle: subdiv, ms (p) | subdiv saved, time saved | k* | axis-aligned patch: subdiv, ms (p) | subdiv saved, k* |
|---|---|---|---|---|---|---|
| reduced 1/8 of 5k bumpy (1260 / 10092) | 3797, 120 | 119, 40 (21) | 97%, 67% | 4.9 | 491, 56 (24) | 87%, 3.6 |
| reduced 1/32 of 5k bumpy (314 / 10092) | 1632, 58 | 35, 16 (11) | 98%, 72% | 4.8 | 112, 21 (13) | 93%, 3.8 |
| reduced 1/8 of 21k bumpy (5220 / 41772) | 5825, 288 | 2245, 253 (96) | 61%, 12% | 1.4 | 3229, 313 (107) | 45%, 0.8 |
| reduced 1/32 of 21k bumpy (1304 / 41772) | 6493, 264 | 632, 105 (62) | 90%, 60% | 3.5 | 2710, 227 (101) | 58%, 1.4 |
| reduced 1/16 of 21k sphere (2610 / 41772) | 135483, 3400 | 1, 82 (52) | 100%, 98% | 65.0 | 970, 127 (65) | 99%, 51.6 |
| spheres6 vs 20 (300 / 4332) | 4512, 120 | 37, 11 (6) | 99%, 90% | 19.5 | 1441, 67 (16) | 68%, 4.2 |
| bumpy5 -> bumpy6 (1452 / 588) | 145, 10 | 9, 9 (1) | 94%, 5% | 1.6 | 27, 10 (1) | 81%, 1.1 |
| coplanar grids 16 -> 17, jitter .3 (512 / 578) | 3550, 161 | 105, 17 (4) | 97%, 90% | 40.6 | 105, 17 (4) | 97%, 37.8 |

Check of the harness: the subdivisions without the patch bound are of the order of the library's own symmetric run
(reduced 1/8 of the 5k mesh: 4141 symmetric in `HausdorffBenchmark`, README; 3797 one-sided here).
The "axis-aligned" column is a model, not a measurement: it scales the oracle by 1 / max|n_i|, which is the along-axis gap only for a target
parallel to Q, and it does not model the extra refusals of edge-on triangles along an axis. It is optimistic, so the conclusion stands.

**Reading.**
- **Subdivisions:** the patch bound removes 61 to 100 % (median 97 %) of them on reduced-against-original and on
  smooth inputs: the work that costs 135k subdivisions (3.4 s) on the smooth sphere becomes one.
- **Time at the uncertified patch cost:** 5 to 98 % less (median about 70 %); the two small or rough rows (bumpy5 -> bumpy6, the 21k
  bumpy mesh at 1/8) save 5 and 12 %, (on the 21k row the patch bound is 38 % of the run; on bumpy5 -> bumpy6 other costs dominate).
- **k\*:** the break-even factor is 3.5 or more on 6 of 8 rows, 1.4 to 1.6 on the other two. At the
  estimated filtered cost (k = 1 to 2.5) the certified patch bound wins on 6 of 8 rows; on the two rows with k\* of 1.4
  and 1.6 it wins only if k stays below that. At interval cost (k = 4 to 6) it wins clearly on 3 rows, is borderline
  on 2 (k\* 4.8, 4.9) and loses on 3.
- **Axis-aligned frame:** saves less (45 to 99 % of the subdivisions) and has a lower k\* on every row (0.8 on 21k at 1/8: a loss even at k = 1). The general frame is worth its filter.
- **Caveat 1:** the oracle is tight; the certified bound is a little wider (candidate supersets, refusals).
  Vertices of Q that coincide with original vertices make some signs exactly 0 (undecided): such candidates stay in the
  superset (looser, not refused). The spike measures it.
- **Caveat 2:** one-sided, rows are procedural. Real inputs (the alpha-wrap simplification) are the mix the library's
  `PatchBound` was built for; its saving there is the reason for the uncertified one (#280).
- **Caveat 3:** only one thing needs this: `CertifiedUpper` is opt-in and no library caller sets it today
  (`MeshRobustRepair` and `AlphaWrapQualityPass` use the uncertified patch bound). The gain is for a caller who wants a
  certified Hausdorff bound of a reduced mesh, which is the case the issue names.

## 8. Recommendation

**Go**, for the **filtered-double design** of sections 2 to 6, as a spike first, then a tier-L PR (design note
reviewed, bounds review, mutation run):

- expected: 61 to 100 % fewer subdivisions and typically half the time (or better) of `CertifiedUpper` on a reduced mesh against its
  original, no worse than neutral elsewhere if the cost stays within k\*.
- **No-go for an interval-only evaluation** (k about 4 to 6: it loses on three rows, is borderline on two, and wins clearly on three).
- **No-go for the exact `Orient2d` axis frame** unless the filter proves hard to review (it loses the gain on the 21k rows).

**Stop rules for the spike** (at most one round): (a) the certified patch bound costs at most 2.5 times the
uncertified one on the rows above (`predcost` and the oracle's (p) column), and (b) the subdivisions with it are within
1.3 times those of the oracle. If either fails, stop and report: the design is sound but not worth the code.
A gate that stops calling the patch bound after a run of refusals (+infinity) is a second lever for the 21k rows; it
needs a determinism argument (parallel equals serial) before it is allowed, so it stays out of the first round.

## 9. Plan, tests, review

1. **Spike** (no PR): `det3` with the bound of section 3 against a `BigInteger` exact evaluation on random and
   near-degenerate rows; `patch_bound_certified` in a scratch branch; measure cost and subdivisions against the oracle rows.
2. **Library:** `CertifiedTriangleBounds.Det3` (value and sign), `TriangleDeviationBounds.certified_patch_bound`, used in
   `UpperBound` when `Certified`; `MeshHausdorffDistance` no longer forces `PatchBound && !certified` off (its comment goes);
   `HAUSDORFF_CERTIFIED_UPPER.md` gets the derivations of sections 3 to 6 (this note moves into it).
3. **Tests** (CLAUDE.md rules): `Det3` bound against exact (random, rows with exact zero, subnormal, 2^+-500 scales, NaN / Inf);
   sign refusals (`T = 0` exactly: the three tests return undecided, never wrong); the boundary test against an exact
   segment-triangle disjointness; the gap candidates against exact sampling of x in Q (as #847's
   `CertifiedCascade_NotBelowExactSamples`): reduced against original, smooth, bumpy, planes tilted, 1e3 offset and UTM
   offsets (coordinates far from the origin: the differences are exact there, the permanent is small, test it);
   the certified bound against the uncertified patch bound (never below it by more than rounding on covered cases, never
   below the exact sample maximum, never finite where coverage fails: open meshes, a hole under Q, a fold, inconsistent
   orientation); an invariance test over `Similarity.All()` (2^k scales bit for bit; the permanent and E scale exactly,
   `A` does not: state it, or test the exact-scale subset); a reuse test (no state in the instance: the `Dictionary`
   is local, as today); determinism (parallel equals serial: no shared state, no reduction across threads).
4. **Mutation:** the incremental run on the changed lines; hand mutants of each constant (10 u to 8 u must fail the bound-against-exact
   test, `A` removed must fail the subnormal case, the strictness of each sign test, the `max` fallback for undecided lambda).
5. **Review:** an independent bound review of sections 3 and 6 (the constants, the monotonicity of lambda, the candidate
   superset) before the PR is ready; the review checklist section 5 (subnormal / overflow) and 9 (determinism).
6. **Benchmark row** in `benchmarks/HausdorffBenchmark/README.md`: subdivisions and time with and without the patch bound
   under `CertifiedUpper` (the `patch` mode of this PR is the base).

**Open questions for the owner.** (1) Is `CertifiedUpper` on reduced meshes a use case that justifies a tier-L change
(no library caller sets it today)? (2) If the filter's constant is judged too delicate to review, accept the exact
axis-aligned variant and its smaller gain (section 2)? (3) Should a refusal gate (section 8) be allowed under
determinism, or stay out?
