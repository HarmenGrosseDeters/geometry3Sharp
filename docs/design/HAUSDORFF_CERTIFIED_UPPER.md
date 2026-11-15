# MeshHausdorffDistance: a certified upper bound (`CertifiedUpper`, #742)

#738 made `LowerBound` certified (L) and kept `UpperBound` in the floating-point model of the distance queries (U0)
([HAUSDORFF_CERTIFIED_BOUNDS.md](HAUSDORFF_CERTIFIED_BOUNDS.md)). U0 is not certified because `OneSided.query` takes the
square root of `DistPoint3Triangle3.DistanceSquared`. That is a quadratic form whose terms cancel down to d², so its
absolute error is about 2^-53 E²/d, where d is the distance and E the size of the nearest triangle. Below d ≈ 1e-8 E the
error grows to 2^-26 E (numerics review of #819, F2). The upper bound can then be below the exact distance. On small
triangles 1e-9 above a unit triangle, it was below in 76 of 180 cases
(`HausdorffCertifiedUpperTests.NearSurface_CertifiedUpper_NeverBelowTheExactDistance`).

`CertifiedUpper` (owner decision 2026-11-13: opt-in, default off) makes every upper bound term hold under rounding.

## 0. Scope

#738 c3 bounds a child triangle with rounded corners: it takes the rounded triangle's bound u and adds the largest
`off` of its corners (`HausdorffRounding.TriangleUpper`). The distance is 1-Lipschitz, so this covers the exact child.

`CertifiedUpper` changes only u. Under it, u is a certified upper bound of max over x in T of d(x, B), where T is the
real triangle with the given double corners.
- **Implementation:** `TriangleDeviationBounds.Certified` (internal). The default path, `IsWithin`, the Reducer and
  `MaxDistance` are unchanged, bit for bit.
- **Notation:**
  - Up / Down are `ExactInterval.Up` / `Down`.
  - [E] is the `ExactInterval` enclosure of an expression, and Of(x) = [x, x].
  - All points are doubles, taken exactly.
- **Rounding rule (bound review M1):** every Up(...) wraps exactly one rounded operation. A longer expression is an
  `ExactInterval` evaluation, whose `Hi` is used.
- **NaN rule (M2):** a NaN anywhere in the certified path counts as +∞.
  - Maxima use `CertifiedTriangleBounds.max`, which returns +∞ for a NaN operand.
  - `up(NaN)` = +∞.
  - A NaN plane distance in the clip rejects the split.
  - B1 is NaN-safe by itself: Of(NaN) gives the whole line, and its `Hi` is +∞.

## 1. B1: a point and one target triangle (`PointTriangleUpper`)

The inputs are x, the triangle t = (V0, V1, V2), and the float closest point's barycentrics w1, w2.

**Clamp.**
- Set s = min(w1, 1), and replace s < 0 or NaN by 0. Set t' = w2, and replace t' < 0 or NaN by 0.
- If fl(s + t') < 1, keep t'. Rounding is monotone and 1 is a double, so an exact s + t' > 1 would give fl ≥ 1.
- Otherwise t' = min(t', s ≥ 1/2 ? 1 − s : Down(fl(1 − s))).
  - For s ≥ 1/2, 1 − s is exact (Sterbenz).
  - Otherwise fl(1 − s) lies within half a step of 1 − s, so one step down is below it.
  - (No TwoSum. It is not reliable on x87, net48 x86.)

So s, t' ≥ 0 and s + t' ≤ 1 hold exactly, and p = V0 + s(V1 − V0) + t'(V2 − V0) is a point of t. This is tested
exactly, `Clamp_SumAtMostOne_Exactly`.

**Bound.**
- d(x, t) ≤ |x − p| ≤ sqrt(S), where S = Hi(Σk Square(Of(xk) − [V0k + s(V1k − V0k) + t'(V2k − V0k)])).
- Math.Sqrt rounds to nearest, so B1 = Up(Math.Sqrt(S)).
- S = +∞ gives +∞. S is at least 2^-1074, because every `ExactInterval` result is widened, so B1 ≥ 2^-537.

**Tightness.** There is no cancellation. The excess over d(x, t) has two parts:
- a few ulps of the coordinates, from the enclosure;
- the float barycentrics' error, first order: about 2^-52 E / sin of the triangle's smallest angle.

Compare the 2^-26 E of U0 (`PointTriangleUpper_NeverBelowTheExactDistance` checks both sides). Any barycentrics give a
valid bound: corners, NaN, out of range.

## 2. B2: a convex piece and one candidate (`certified_max_distance`)

A piece P* is a convex polygon with true vertices x*_j. Each is stored as a double x~_j with off_j ≥ |x~_j − x*_j|; the
triangle's corners have off 0.

d(·, t) is convex, so its maximum over P* is at a vertex. It is 1-Lipschitz, so

    max over P* of d(·, t) ≤ max_j up(B1(x~_j, t) + off_j).

A point equal by value to a corner c_i with N_i == t reuses that corner's certified distance U_i. Its off_j is still
added, because a cut point at λ = 0 equals its parent by value but carries an off (review, should-fix B2).
`CertifiedMaxDistance_AddsEveryPointsOff` tests this.

## 3. B3: the plane split without exact clipping (`certified_clip`)

The plane need not be exact. Each piece's bound holds for its own points with any candidate list, so a split is
valid if both pieces are convex and together cover P*.

**Signs.** σ_j is the sign of the computed fl(n·(x~_j − o)).
- If any σ_j is NaN, the split is rejected: that vertex would be in neither piece (M2, witness (b)).
- Otherwise every vertex is in at least one piece.

**Pieces.**
- pos* takes the vertices with σ ≥ 0; neg* takes those with σ ≤ 0.
- Each edge with a strict sign change adds its cut point to both pieces.
- The cut point is c* = x*_a + λ(x*_b − x*_a), with λ = clamp(fl(da/(da − db)), 0, 1). It lies on the true edge.
- Any sign pattern is allowed. The pieces are the existing clip's, and coverage does not depend on the plane.

**Coverage, for any sign pattern** (review, should-fix B3). Let C be the transition points: the cut points and the
zero vertices. They lie on ∂P* in cyclic order, so conv(C) is in both pieces.
- P* minus conv(C) splits into caps. Each cap is bounded by a chord between consecutive transition points and by the
  boundary arc between them.
- Each arc has vertices of one sign only, so each cap is the convex hull of points of one piece.
- If only one sign occurs, that piece is all of P*.
- Each piece is a cyclic subsequence of P*'s boundary points, so it is convex.

`CertifiedClip_PiecesCoverThePiece` checks this with exact orientation tests on the true pieces: random planes, planes
through and 1e-15 off a corner.

**The stored cut point.** c~ = a + λ·(b − a) in `Vector3d` arithmetic, which is fl(a_k + fl(λ·fl(b_k − a_k))) per
component. The same λ and the same order are used for its allowance (witness (c)):

    |c~ − c*| ≤ |c~ − (x~_a + λ(x~_b − x~_a))| + |(1 − λ)(x~_a − x*_a) + λ(x~_b − x*_b)| ≤ ρ + max(off_a, off_b).

- ρ = `CutRounding`, the L1 sum over k of max(Hi_k − c~_k, c~_k − Lo_k) of the enclosure [x~_a + λ(x~_b − x~_a)].
- Each difference and each addition is rounded up by one step. L1 avoids squares, as in #738.
- Then off_c = up(ρ + max(off_a, off_b)).
- `CutRounding_CoversTheExactError` tests this exactly, at exponents from 2^-1070 to 2^400.

## 4. B4 (u1): a corner and its edges

For x in T, |x − p_i| ≤ max(e_ij, e_ik): |· − p_i| is convex, so its maximum over T is at a vertex. Then:
- e^_ij = Up(Math.Sqrt(Hi([|p_j − p_i|²]))) (`LengthUpper`);
- u1 = min_i up(U_i + max(e^_ij, e^_ik)).

## 5. B5 (u2): the largest corner distance plus the covering radius (`CoveringUpper`)

ρ_T is the largest distance from a point of T to its nearest corner. u2 = up(max U_i + cov^), with cov^ ≥ ρ_T.

**Facts.** Let l be the longest edge and R the circumradius.
- **(i) Non-acute:** ρ_T ≤ l/2 (not an equality: a flat triangle has ρ_T ≈ l/4).
  - The maxima of min_i |x − p_i| over T lie at the circumcentre, or else on ∂T.
  - For a non-acute triangle the circumcentre lies outside T or on ∂T.
  - On an edge the value is at most half that edge.
- **(ii) Always:** ρ_T ≤ l/√3.
  - Acute: ρ_T = R = l/(2 sin γ), with the largest angle γ in [60°, 90°).
  - Otherwise ρ_T ≤ l/2 by (i).
- **(iii) Always:** ρ_T ≤ R. For an acute triangle ρ_T = R. Otherwise ρ_T ≤ l/2 ≤ R, because l is a chord of the
  circumcircle.

**The bound.** The squared edges are taken as intervals q_ij = [|p_j − p_i|²].
- **Certainly not acute:** some edge has Hi(q_j + q_k − q_i) ≤ 0. The angle opposite it is ≥ 90°, so that edge is the
  longest, and cov^ = up(0.5·e^_i).
- **Otherwise:** cov^ = min(up(l^·K), R^).
  - K = 0.5773502691896258 is the nearest double to 1/√3. It is above it: 3K² ≥ 1 exactly, and the double below
    fails (`K_IsOneOverSqrt3_RoundedUp`).
  - R^ = up(Hi([e^_01·e^_12·e^_20]) / (2·den)), with den = Down(Math.Sqrt(Lo([|e1 × e2|²]))) and the cross product in
    intervals. 2·den is exact for den < MaxValue/2.
  - R^ = +∞ if den ≤ 0. This is review M1: one division and one step up, where the first version had three roundings
    under one Up.
- By (ii) and (iii) this needs no decision about acuteness.

`CoveringUpper_NotBelowTheCoveringRadius` checks it exactly, squared, on random triangles at 2^-500 to 2^500 and on
near-right ones.

## 6. B6: the cascade (`certified_upper`)

- **One nearest triangle** (n0 == n1 == n2): u = max U_i, by convexity.
- **Otherwise u0, the witness variant.** The float cascade (`witness_bound`, `polygon_bound`'s logic with float
  distances) chooses the pieces and candidates, and records the leaves of the option it adopts. Only those leaves are
  bounded with B2, and u0 = max over the leaves (review, witness (a)):
  - An option's leaves are the whole piece with its best candidate, or both sides of an adopted split, recursively.
  - Options the cascade does not adopt leave nothing. That covers an early `continue` (bi ≥ best) and a split no better
    than `best`.
  - A superset of the adopted leaves would be sound, a subset not.
  - A piece of 1 or 2 points still gets its leaf. The float cascade gave it the bound 0, which is sound only when the
    other piece covers it.
  - The early exit `best ≤ threshold` only chooses an option. Whatever it chooses is bounded with B2.
- **The result:** u = min(u0, u1, u2), each +∞ where it cannot be bounded.
- **No patch bound (owner, 2026-11-13; review M3).** `patch_bound` carries an estimated slack, not a derived one, so it
  can push u below the true maximum. Under `Certified` it is skipped in code, whatever `PatchBound` says
  (`CertifiedUpper_IgnoresThePatchBound`). Its certification is the follow-up #848.
- **Corner distances:** `OneSided.query` computes U_i as B1 of each point to the triangle the tree search returns. Any
  triangle gives an upper bound of d(x, B), and B2 reuses U_i only for that triangle. The float `dist` keeps feeding the
  computed `Lower`, which L certifies (#738).

## 7. Pruning and the reported bounds (unchanged)

Triangles are pruned against the computed `Lower`, not against L. That is sound because `OneSided.Upper =
max(Lower, stuck, heap top)` is never below the computed `Lower`:
- A pruned triangle has sup ≤ ub ≤ max(Lower, prune).
- `prune` is the first direction's computed `Lower`, which is at most that direction's `Upper`.
- So the reported max(Upper_ab, Upper_ba, L) is above every pruned triangle's sup.

A refined triangle is covered by its rounded children's bounds plus `max off` (#738 c3).

(Nit, from the review: `Converged` compares upper − lower ≤ tol in floats, so it can be one ulp off. That does not
affect the bounds.)

## 8. Scaled and shifted copies

Meshes outside the extent window, or far from the origin, are measured on copies (#731, #738). The scaling by 2^-e
rounds coordinates below 2^(e−1022) into the subnormal range, for any e > 0 on the copy path; on the offset path this
already happens with e ≤ 64.

`pow2` scales in two factors, so a coordinate can round twice; the error is at most 1.25·2^-1075 per coordinate
(review: 5·2^-1074 at n = −3 gives 2 and then 0 against an exact 0.625·2^-1074). Each copy vertex is then within
sqrt(3)·1.25·2^-1075 of the exactly scaled one. So the copies' distance may be below the meshes' (scaled) by
δ_A + δ_B < 2^-1072.

This is covered without a guard, because every certified triangle bound u has a large margin:
- **B1 has a floor:** B1 ≥ 2^-537. The sum's `ExactInterval` evaluation widens its `Hi` at least to `Up(0)` = 2^-1074.
- **So does every term built from it:** u0's leaves, u1 and u2, and `TriangleUpper` (`Up(u + off)`) are each ≥ 2^-537.
- **Each ends with one rounding up of a rounded result** (`up`, `Up(Math.Sqrt)`, `HausdorffRounding.Up`), so it lies at
  least half a step above its exact value. Half a step at 2^-537 is about 2^-590, far above 2^-1072.
- **The reported bound covers every triangle with that margin.** `UpperBound` = max(Upper_ab, Upper_ba, L), and each
  `Upper` = max(Lower, stuck, heap top). It is at least each kept triangle's u (heap top, stuck). For a pruned triangle
  it is at least `Lower` ≥ u. Either way it lies ≥ 2^-590 above that triangle's maximum, which covers δ_A + δ_B.

(Side effect of the floor: an absolute `Tolerance` below about 2^-537, in the copies' frame, cannot converge for meshes
that touch. The `CertifiedUpper` doc says so.)

## 9. Cost

Measured with `benchmarks/HausdorffBenchmark`: the 18 cases of #738 section 4, symmetric and one-sided, default against
`CertifiedUpper`, median of 7 runs after a warm-up. The table is in its README.

- **Time:** **+16 to +115%**, typically about +90%.
  - The least is +16% (spheres 6 vs 20, symmetric), then +27% to +40% on the reduced mesh of 21k triangles.
  - The most is +115% (cube against sphere, one-sided); small inputs, where the per-triangle bounds dominate, are about
    +100%.
  - Run-to-run noise in the container is about ±30%.
- **Subdivisions:** the same in every case.
- **Upper bounds:** within a few ulps of the coordinates of the default ones (1.7e-9 at UTM coordinates, where an ulp is
  9.3e-10). They are mostly higher, but lower where the default overshoots (spheres6, symmetric: 2.7e-16 lower).
- **Lower bounds:** the same, bit for bit.

The cost is the certified distances (`ExactInterval`) of the witness leaves' points and the interval evaluation of u1 and
u2 per triangle.

The owner's decision for opt-in (B) was made on the prototype's numbers (witness variant +30 to +105%). The PR reports
these.

A later PR may replace the intervals with a running-error bound in plain doubles, which measured as +16 to +76% in the
prototype. That bound is new and needs its own review.

## 10. Tests

- **`CertifiedTriangleBoundsTests`**, each piece against exact arithmetic (ExactDyadic, BigInteger):
  - K;
  - the clamp;
  - B1, at every exponent and near the surface, with its tightness;
  - NaN;
  - the cut-point allowance;
  - the clip's coverage, at depth 1 and at depth 2 with rounded parents (nonzero off);
  - `split` under arbitrary sign patterns that no plane gives (zeros, alternating signs), exactly;
  - B5;
  - the cascade against exact dyadic samples over random roofs and valleys;
  - B2's off.
- **`HausdorffCertifiedUpperTests`**, the whole run:
  - near the surface: the default is below the exact distance in a good part of the cases, the certified bound never;
  - dense exact samples;
  - folded spheres at 1.3·2^52;
  - ordinary inputs close to the default;
  - `Similarity.All()`, bit for bit at 2^k;
  - reuse, serial and fresh instances;
  - the patch bound is ignored.

Mutation: section 11.

## 11. Mutation (local, 27 mutants of the new code, against the new tests)

22 are killed, 5 survive: 4 explained below, and the fifth equivalent (the last paragraph). The second bound review's
mutants are among them: the parents' off with one end only (either end), and the cut-point rounding with one side only
(either side), killed by `CutPoint_Off_CoversBothParents` and the shifted points of `CutRounding_CoversTheExactError`.

**Surviving:**
- **`Up` after `Math.Sqrt` in B1 dropped, or flipped to `Down`** (two mutants). S's enclosure is widened by at least a step above the exact square, which
  nearly always covers sqrt's half-step rounding. A kill needs both at their extremes at once. The Up stays: the proof
  needs it.
- **The non-acute test on `Lo` instead of `Hi`.** The mutant takes an undecided, near-right triangle for a right one and
  returns e^/2. For such a triangle R exceeds l/2 by second order in the angle's distance from 90° (about ε² l). The
  rounding up of e^ covers that, so the mutant is equivalent in doubles. The `Hi` test is the derived one.
- **The `!Certified` guards on the patch bound** (in `MeshHausdorffDistance` and `UpperBound`): equivalent.
  `certified_upper` returns before the patch bound is reached, so the patch bound is skipped by construction. The guards
  keep it explicit.

**Killed** (each named in the PR):
- B1's enclosure end, the clamp's Down and its Sterbenz threshold, the edge length's end;
- B5's l/√3, the circumradius denominator, and the cut-point rounding;
- the NaN rejection of the clip, the cut points' off, their parents' off in it (dropped, or one end only), a zero vertex into
  both pieces, B2's off;
- the cut-point rounding taken on one side of the enclosure only (either side);
- the witness's adopted side j; the single-triangle maximum; u1's longer edge; u2's covering radius;
- the corner distances (`OneSided.query`), and `max`'s NaN rule.

The early `continue` of a losing option, when cleared instead, is equivalent: the option is not adopted either way.

The incremental mutation run of CI (`tests/mutation`) gates the merge (a new error bound).
