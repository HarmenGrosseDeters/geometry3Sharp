# Exact Boolean 3.0: a hierarchical Antipodal boundary sum (lever 1)

Status: design note (T3a, 2026-11-13). The numerics review (exact-numbers study) confirmed the three claims, with
the corrections made here. Its check script is review767/identity.py on runs/exact-numbers. No code yet. It builds on the safe norms and the run scale of #719
(#759) and on option (A) (#766). Source of the lever: the boolean extreme-runtime study (#734,
`docs/perf/EXTREME_RUNTIME_BOOLEANS.md`, lever 1; probe `benchmarks/ExtremeBooleans/probe_boundary_sum.py`).

## 1. Goal and bound

`antipodal_value` sums one term per boundary segment of the open mesh, every time it evaluates the open winding
number. Under (A) it takes the following share of the CPU samples, all threads, union with the model's copy (#734):

| model | `antipodal_value` share | wall time under (A) | classification |
|---|---|---|---|
| 117959 | 70 % | 103 s | 92 s |
| 122796 | 52 % | 17.8 s | 14.4 s |
| 1087141 | 30 % | 45 s | 32 s |

In the normal stratum, two open models are dominated by it as well: 56483 (84 % of 2.5 s) and 80105 (60 %).

The upper bound of the lever is that share, and in wall time it is capped by the classification phase. If the sum
became free, 117959 would drop to about 35 s and 122796 to about 9 s. The latter is (A)'s regression on that model:
11.2 → 17.8 s.

The probe needs, at θ = 0.25, the following terms per evaluation:
- 117959: 23 instead of 6007;
- 1087141: 26 instead of 1196.

**Requirement:** the results stay bit-identical. Only the counters and the time change. The value of the
hierarchical sum is never used directly. It is used only where its certified interval decides the same threshold that
the full sum would decide. Everywhere else, the full sum of today runs.

## 2. What the classification needs from a value

L2 and the per-piece evaluation use the Antipodal value `w` in three places:

1. **The threshold:** `threshold_generalized(snap_half(w, bound))`. A value within `bound` (at most 1e-6) of a
   half-integer h is read as h, and the tie rule decides it.
2. **L2's carry margin:** `rem = |w - h| - bound - 2e-6`. The gradient bounds (L1/L2, `far_term`) spend this margin
   along a carried path.
3. **`verify_traced` and the counts:** the counts are not part of the result.

Let a hierarchical evaluation give `w_h` with a certified bound `B_h`, so that the true value lies within `B_h` of
`w_h`. Let `h` be the half-integer nearest to `w_h`.

**Claim.** If `|w_h - h| > B_h + 2e-6`, the full sum of today gives the same threshold, and it does not snap.

**Proof.**
- The true value w* satisfies `|w* - h| > 2e-6`.
- The full sum's `w` is within its `bound` ≤ 1e-6 of w*, so `|w - h| > 1e-6 >= bound`. The full sum therefore does
  not snap.
- Both w and w_h lie on the same side of h as w*. So `ceil(w - 1/2) = ceil(w_h - 1/2)`, and the threshold is the same.
- No other half-integer lies between w and w_h either. The condition |w_h − h| > B_h + 2e-6 caps B_h itself, since h
  is the nearest half-integer, so no further limit on B_h is needed.
- B_h must bound |w_h − w*| with all roundings included, not only the truncation of the far field: every atan2, the
  normalisations, the dipole products, the stored c, r, va and A_abs, and the summation (section 3).

Then:
- **Where the claim applies:** the threshold is that of `w_h`, and the margin is `rem = |w_h - h| - B_h - 2e-6`. This
  margin is smaller than today's but sound, so fewer pieces are carried and more are evaluated directly, and the
  counts change.
- **Elsewhere:** the full sum of today runs, with its own `w`, bound, snap and margin. The decision and the margin are
  then today's, bit for bit.

The threshold of every evaluated piece is therefore that of the full sum. A carried piece keeps the threshold of its
reference, which is the same in both. Every margin is sound. So the result is the same mesh. Only the l2 counts and
the time differ.

**Status 1 and 2 (another ray, or undecided).**
- The full sum returns at the first segment, in segment order, that fails a test.
- The hierarchical sum meets its leaf segments in another order. Where any leaf term fails a test, it hands over to
  the full sum, which then decides 1 or 2 as today.
- A clustered node is accepted only far from p and away from the ray (section 3). None of its segments can fail a
  test of today:
  - "on or next to an end" needs `tol >= 0.01`, a distance of about 1e-14 of the coordinates;
  - "the ray through the segment" needs `num ≈ 0, den ≤ tol`, which the cone test excludes.
- So the hand-over sees exactly the cases where today's sum would return 1 or 2, or more. In the extra cases, today's
  sum then gives 0 and its value.
- At L2's approximated centroid (pointError > 0), the leaf tests run at the approximation, not at the exact centroid
  where the full sum runs. tree_term therefore requires 1 + a·b > 2 tol there instead of > tol: 1 + a·b moves by at most
  about tol between the two points, so the full sum's status-1 test cannot fire at the exact centroid where the tree
  admits (review of #813). The other tests are covered by fullBound: a failure of "ray through the segment" at the
  exact point leaves hypot within about 3 tol, which makes fullBound refuse.

## 3. The sum over a cluster

The boundary segments (`BoundarySegments`, pairs a → b) are ordered into chains, head to tail, as in the probe.
- A chain keeps every segment's a → b direction.
- A vertex with several outgoing segments starts another chain.
- A closed loop is one chain whose end meets its start.

Each chain gets a binary tree over its segment range, so that a node is a contiguous run a_0 → a_1 → … → a_k.

For each node, the tree stores, in the mesh's scaled grid units (FarPoints.Shift of #759):
- the centre c, the mean of its points;
- the radius r = max |a_i − c|;
- the vector area `va = 1/2 Σ (a_i − c) × (a_{i+1} − c)` over the closed polygon a_0 … a_k a_0;
- the unsigned area `A_abs = 1/2 Σ |(a_i − c) × (a_{i+1} − c)|`;
- a_0 and a_k.

The rounding of each stored quantity is bounded and carried as an absolute error. These bounds are given in the
implementation and checked in the review.

**Identity.** Write S(A, B) = 2 atan2(A·(B×x1), 1 + A·B + A·x1 + B·x1) for unit vectors A, B. This is the signed area
of the spherical triangle (A, B, x1). For the unit vectors A_i from p to a_i:

  Σ_{i<k} S(A_i, A_{i+1}) = S(A_0, A_k) + Ω(a_0 … a_k),

where Ω is the signed solid angle of the closed polygon a_0 … a_k a_0 seen from p.

**When it holds.** The identity holds exactly, not modulo 4π, when −x1 (= x0, the ray's direction) lies outside the
cap of the node's ball seen from p, with margin. The x1 side does not matter.
- Why: the two fans, the chain's triangles and the chord's triangle plus the polygon, have the same boundary. Their
  covering numbers of the sphere therefore differ by a constant. The principal values of S jump where a fan's covering
  of −x1 changes. Both are 0 at −x1 when −x1 is outside the cap, so the constant is 0.
- Numerical check of the review (numerics, #767), on 600k random chains:
  - −x1 inside the cap: 22266 of 111499 cases are off by whole turns;
  - x1 inside the cap: 0 are off.
- The study's probe tested the x1 side and left −x1 free. That explains its 0–0.3 % jumps.

**The cone test**, with u = (c − p)/d, d = |c − p| > r, and sin α = r/d:

  −x1 · u < cos α − ε.

The margin ε:
- It covers the roundings of u and of x1·u, with r rounded up and d rounded down.
- It also exceeds the angle sqrt(2·tol) at which today's "ray through the segment" test (`|num| <= tol && den <= tol`)
  can fire. So such a segment is never inside an accepted node.
- tol is the code's `16 (ea + eb + 1e-15 + directionError)`. For the node it is bounded by
  `16 (2 · 2.3e-16 (pm + M) / (d − r) + pointError terms + 1e-15 + directionError)`, where M is the node's largest L1
  norm of a point (stored).
- The implementation gives ε as such an expression, and the review checks it.

**The solid angle of the closed polygon.**
- Ω is the flux of f(q) = (q − p)/|q − p|³ through any surface spanning the polygon that does not pass through p. The
  fan of triangles (c, a_i, a_{i+1}) is one, and it lies in the ball, so p is outside it.
- The dipole term is `Ω_1 = va · f(c)`.
- The Jacobian of f at x is (I − 3 x̂x̂ᵀ)/|x|³, with spectral norm 2/|x|³. Along the segment from c to any point of
  the ball, |x| ≥ d − r. So |f(q) − f(c)| ≤ 2r/(d − r)³, and

    |Ω − Ω_1| ≤ A_abs · 2r / (d − r)³.

- In winding-number units, the node contributes `(S(A_0, A_k) + Ω_1)/(4π)` with the error
  `E = A_abs · 2r / ((d − r)³ · 4π) + rounding`.
- With A_abs ≈ r², E ≈ 2r³ / (4π (d − r)³) = (1/(2π)) (r/d)³ / (1 − r/d)³. At r/d = 0.25 that is 5.9e-3 winding
  units per node, or 0.094 (r/d)² there. This is far above 1e-6, which is why section 2 never uses w_h for a snap. The size of E only
  sets the expected fallback share.

**Accepting a node.** A node is accepted if both:
- r/d < θ, with θ = 0.25 as the starting value (the probe's error at 0.25 was at most 3.4e-4 on 117959);
- the cone test passes.

Otherwise the traversal descends into the node's children. A leaf is today's term with today's error.

## 4. Evaluation, and where it is used

```
antipodal_value_hierarchical(p, pm, x1, chi, tree, pointError, directionError):
    traverse every chain's tree with an explicit stack (no recursion), in a fixed order:
        node accepted -> add chord term + dipole; add E to B_h
        leaf          -> today's term and error; on any failed test return HANDOVER
    w_h = chi + sum / 4 pi;  B_h = errors (as today's bound) + sum of E
    return w_h, B_h
```

The three callers of `antipodal_value` change as follows:
- In L2's traced path (the reference of a cluster, an approximation, the exact centroid), the hierarchical value is
  tried first. If the claim of section 2 applies, w_h decides. Otherwise, or on HANDOVER, the full sum runs as today.
- `antipodal_front_winding` (the per-piece path) is changed the same way.
- `verify_traced` (tests) keeps the full sum.

**Cost.**
- The full sum still runs where a piece is within about B_h of a half-integer. That means pieces next to the open
  sheet, and flat parts, where the value is exactly k + 1/2.
- The probe gives the share of evaluations that would fall back; the PR measures it.
- If the share is too large, that is not a correctness issue. It only means the speed-up is smaller. A second, finer
  pass (θ/2) before the full sum is an option for the PR to measure.

**Determinism.**
- The trees are built once per open mesh, serially, before the parallel tracing, as `antipodal_segments` and the
  projections are today. Queries only read them, and the order of the traversal is fixed.
- The ray sequence is per piece: the far point of an attempt is a hash of Seed, the piece and the attempt
  (`front_crossings`). No shared generator state shifts with the number of rays rejected elsewhere.
- So parallel runs give the same results as serial ones, and repeated runs give the same results, bit for bit.

**Invariance.**
- All tests compare ratios of distances (r/d, the cone test) and errors relative to the coordinates.
- In the scaled grid of #759, an exact 2^k similarity gives the same doubles up to the exponent. So results and
  counts are bit-identical over the 2^k members of Similarity.All, as the Antipodal sum is today.

**Exact-code rules.** ExactMeshBoolean is an exact file: no FMA, no float, no new parallel loop. The tree build is
serial.

## 4a. Implementation finding: rays on which the full sum gives no value

Section 2 compares the tree's value with the full sum's value on the same ray. But the full sum can give no value on a
ray: it returns status 2 when its bound exceeds 1e-6, for example for a ray just past a segment's end.
- Today, such a ray is replaced by another one.
- After 20 rays without a value, the piece is undecided and read as outside.
- The first prototype decided such pieces by the tree. On 112798 this left 10 undecided pieces instead of 34, which is
  more correct but not bit-identical.

To keep the results identical, the tree also returns `fullBound`, an upper bound of the bound that the full sum gets
with this ray at the exact point. Its value is used only where `fullBound <= 0.999e-6`, because then the full sum gives
a value with this ray (`tree_decides`). The bound is built as follows:
- **Leaves:** today's terms, with hypot lowered by twice their tol. This covers the displacement to the exact point.
- **Segments of an accepted node:**
  - for unit vectors, num² + den² = 2 (1 + A·B)(1 + A·x1)(1 + B·x1);
  - with A and B in the cap of angular radius α, and β the smallest angle between −x1 and the cap (minus the margin),
    hypot ≥ sqrt(2 (1 + cos 2α)) (1 − cos β);
  - the |θ| of each of the node's terms is at most 2π, the bound of a principal value of 2 atan2.

With this, 112798 has its base hash again (34 undecided). The property test checks that wherever fullBound admits a
value, the full sum gives one within fullBound. It also covers rays just past a segment's end, where the full sum
fails. Loosening the gate, or dropping it, then fails the test. **Owner 2026-11-13:** strict bit-identity now. Letting the tree decide the pieces that are undecided today (24 fewer
counted as outside on 112798) is a later follow-up, open item 5 in section 6.

The tree is built only for boundaries of at least 1000 segments (`AntipodalTreeMinSegments`). Below that size, the full
sum is cheap and the tree did not pay: 124526 and 112798 have 134 and 164 segments.

## 4b. The bounds as implemented (for the review)

> **Which tree.** This section describes the tree of `ExactMeshBoolean` (`AntipodalTree`, grid points of `FarPoints.Segments`).
> The `BoundaryTree` of `spatial/ExactWindingCache.cs` (the exterior boundary of the exact winding cache; `node_query`) has
> the same node identity (chord + dipole + remainder, with R, AAbs, VaErr, M1) but a different error budget, because its
> reference is different: it must cover |chord + dipole - the computed full sum `boundary_sum_x` over the node's k = hi - lo
> edges|, and the vertices and p are the exact doubles of that full sum. So (1) there is **no** `(2 2.3e-16 M1 + δ) Len / dr²`
> displacement term: nothing is moved onto a grid; (2) the evaluation error of the full sum is charged `1e-12` per edge, for the
> k replaced edges as for leaf edges (`1e-12 * (hi - lo)`), so it grows with the chain, not with Len/dr; (3) the order in which
> the tree and the full sum add up differs: `7.5e-16 N sum|w|` (2 N u pi sum|w| with margin); (4) the cap margin is
> `eps = 1e-9 + 16 coordEps` (the `2 sqrt(2 tolNode)` term of the grid tree stands for a ray-through-segment test that
> `boundary_edge_angle_x` does not have: it decides the branch cut exactly). Each clause has a test in
> `ExactWindingCacheInternalTests` (the eps window, va's true rounding error in exact integers against `vaErrTerm`, the
> summation-order and per-edge pin on a 40006-edge boundary).

**The decision margin of `BoundaryTree` (`tree_margin`, `decide_above`).** A threshold query decides from the tree value
wH = chi + bSum only when wH is farther from iso than the margin 1.01 (bErr + 1e-9 + 4e-15 (|chi| + |bSum|)); else it uses the
full sum W = chi + `boundary_sum_x` (the value of `Evaluate`). The margin has to bound |wH - W|. With T_e the computed angle of
an edge (`boundary_edge_angle_x`) and θ_e its exact value, the terms are:

| term | covered by |
|---|---|
| order of the additions in the full sum and in the tree: N u π Σw each | `sumOrderErr` = 7.5e-16 N Σw ≥ 2 N u π Σw (6.97e-16 N Σw); tested from below on a 40006-edge boundary |
| edges the tree visits as leaves | the same T_e bit for bit in both sums: no difference but the order |
| a node's edges, approximated by chord + dipole instead of ΣT_e: the analytic remainder | `rem` = AAbs r / (d - r)³ (the Jacobian of x/‖x‖³ has norm 2/‖x‖³; remainder of the fan from c) |
| the rounding of va (the dipole moment) | `vaErrTerm` = 0.5 VaErr ‖dx‖₁ / d³; VaErr = 0.5 Σ ‖v-c‖‖w-c‖ (count + 8) 4.5e-16; tested against the true error in exact integers (slack about 2) |
| the rounding of the dipole's own evaluation and of dx = c - p | `(1e-12 + 16 coordEps) dipScale`; coordEps = 2.3e-16 (pm + M1) / (d - r) |
| the chord's evaluation error, the rounding of d, cos α and the cap test | `1e-11 + 16 coordEps`, and the cap margin eps = 1e-9 + 16 coordEps (angular) |
| the evaluation error of the k edges the node replaces in the full sum | `1e-12 (hi - lo)`: the leaf rule's 1e-12 per edge, charged for every replaced edge |
| the rounding of /2π and of chi + bSum (u relative each) | `4e-15 (|chi| + |bSum|)`, about 18 times too large |
| the rounding of the margin, and of the accumulation of err (n u relative, n visited terms) | the factor 1.01, valid while n u < 0.01 |

Each clause of this table has a model in `tools/ErrorBoundAudit/bounds.py` (BT-1 to BT-6; margins in [the audit note](../research/ERROR_BOUND_AUDIT_2026-11.md)), checked with exact rationals and against doubles; the model agrees with the hand table (the tight clauses: A1 against 1e-14, 1.013 with the proof's constants below, 1.30 with the sharper count of 16u in step 4; `sumOrderErr`, 1.064). The analytic remainder, the cap geometry and the coordinate term of `coordEps` are not modelled.

What the table rests on, and the 1e-9: the rows `1e-11 + 16 coordEps` and `1e-12 (hi - lo)` rest on **A1: for every edge
of an accepted node, and for the chord, the computed angle T_e is within 1e-14 of its exact value** (the code charges
1e-12 per edge: a margin of 100). A1 is proved below for the edges of an accepted node, which `node_query` restricts to the fast
path of `boundary_edge_angle_x` by a certificate (`fast_path_certificate`); a node that fails it is descended into, and the
leaf edges below it are evaluated by the same function as the full sum, bit for bit, so no assumption is needed for them. The
additive 1e-9 in the margin is **not part of the proof and not a term of the table**: it is a cushion on top of the proved bound
(it covers an error of up to 1e-9 in the sum, whatever its cause), and an absolute term because a winding number is
dimensionless (a solid angle over 4π) and every row is an angle. It costs no speed: a decision within 1e-9 of iso is rare,
and it falls back on the full sum, which is the reference.

**Proof of A1 for the edges of an accepted node.** Let p be the query point, the node's points within r of its centre c, d
the distance |c - p| (rounded down by 1e-15 relative), and u = a - p, v = b - p two points of the node, with la = |u|, lb = |v|.
Accepted means r < d/4, the +x ray misses the cap of the ball by more than eps = 1e-9 (angle), d lies in [2^-97, 2^99],
and the yz part of c - p exceeds r by at least 1e-9 d (`fast_path_certificate`). The rounding of the yz part itself (the
differences c - p and the square root, about 3u d) is absorbed in the 1e-9 d margin: the exact yz part exceeds r by at least 1e-9 d - 3u d.
1. *Regime.* la, lb ∈ [0.75 d, 1.25 d], so the largest component of u and of v lies in [2^-100, 2^100]; the yz part of u and v
   is at least 1e-9 d (more than 2^-100 times the largest component), so `boundary_edge_angle_x` takes the fast path: no
   special case (u, v ≠ 0 and not on the axis), no rescaling. The angle between the unit vectors U = u/la and V = v/lb is at
   most 2 asin(1/4) = 0.505, so 1 + U·V ≥ 1.87.
2. *No exact fallback.* With x_u = 1 - U_x, s_u = sqrt(1 - U_x²) (so s_u² ≤ 2 x_u), num/(la lb) = s_u s_v sin φ and
   den/(la lb) = x_u x_v + s_u s_v cos φ (φ the angle between the yz parts of u and v), and the identity for unit vectors
   U, V, W = -x̂, (U·V×W)² + (1 + U·V + V·W + W·U)² = 2 (1 + U·V)(1 + V·W)(1 + W·U), hypot(num, den) =
   la lb sqrt(2 (1 + U·V) x_u x_v) ≥ 1.93 la lb sqrt(x_u x_v). The sizes of the terms: S1 = |u_y v_z| + |u_z v_y| ≤ la lb s_u s_v
   ≤ 2 la lb sqrt(x_u x_v) (Cauchy-Schwarz) and S2 = da db + |u_y v_y| + |u_z v_z| ≤ la lb (x_u x_v + s_u s_v) ≤ 4 la lb sqrt(x_u x_v)
   (x_u x_v ≤ 2 sqrt(x_u x_v), as x_u x_v ≤ 4). The exact fallback of the branch cut needs |num| ≤ 1e-12 S1 (or S1 < 2^-1000) **and** den ≤ 1e-12 S2 (`finish_edge_angle`; below, "numErr" and "denErr" are 1e-12 S1 and 1e-12 S2).
   The yz parts of the node's points lie in a disk of radius r about the yz part of c - p, whose norm exceeds r by 1e-9 d, so r/|yz_c| ≤
   1 - 1e-9 and φ ≤ 2 asin(1 - 1e-9) ≤ π - 8.9e-5. If |φ| ≥ 1e-6, sin|φ| ≥ 1e-6 and |num| = la lb s_u s_v sin|φ| ≥ 1e-6 S1 > numErr.
   If |φ| < 1e-6, cos φ > 0.999 and den ≥ la lb (x_u x_v + 0.999 s_u s_v) ≥ 0.999 S2 > denErr. The fallback never fires (and
   the edge does not cross the branch cut, so the sign of num is the sign of the exact one: |num| ≥ 1e-6 S1 ≫ 2u S1).
3. *Rounding of the differences.* ax = fl(a.x - p.x) etc. are the exact differences with relative error ≤ u per component, so the
   computed vectors differ from u, v by ≤ u|u|, u|v| (L2). The half solid angle of the spherical triangle (U, V, W) changes by
   at most π times the angle a vertex moves, per vertex (the area swept by its two sides, each at most π, halved): ≤ 2π u =
   7e-16 in total.
4. *Rounding of num and den.* For the rounded vectors: p1, p2 are one product each: num = fl(p2 - p1) has |Δnum| ≤ 2u S1.
   yz² has ≤ 2u, la ≤ 2.5u, da = yz²/(la + ax) (ax > 0) ≤ 6.5u, da = la - ax = la + |ax| (ax ≤ 0) ≤ 3.5u: da, db ≤ 7u
   relative. dotYZ has |Δ| ≤ 2u (|u_y v_y| + |u_z v_z|), the product da db ≤ 15u, den = fl(da db + dotYZ): |Δden| ≤ 16u da db + 3u (|u_y v_y| + |u_z v_z|) ≤ 22u S2.
   With 2 and the bounds on S1, S2: |Δnum|/hypot ≤ 2u · 2 / 1.93 = 2.1u and |Δden|/hypot ≤ 22u · 4 / 1.93 = 45.6u.
5. *atan2.* |atan2(num + Δn, den + Δd) - atan2(num, den)| ≤ asin(|Δ|/hypot) ≤ (π/2) |Δ|/hypot ≤ (π/2) 47.7u = 8.3e-15, and
   the library's atan2 adds at most 2 ulp of its result, at most 8.9e-16 (the assumption on `Math.Atan2` below). Underflow: the products can be subnormal
   when a component is small, with an absolute error of at most 2.5e-324 each, against hypot ≥ 1.93 la lb sqrt(x_u x_v) ≥
   1.93 · 2.2e-59 · 3.2e-19 ≈ 1.4e-77 (la, lb ≥ 0.75 · 2^-97; x_u ≥ 1 - cos(8e-10) ≈ 3.2e-19, since every point of the node is at
   least about 8e-10 rad from the +x ray): the ratio is below 1e-246, no effect.
Total: 8.3e-15 + 0.7e-15 + 0.9e-15 = 9.9e-15 ≤ 1e-14. ∎

*Assumptions of the proof.* (i) Every `+ - * /` and `sqrt` of the fast path is IEEE binary64 round-to-nearest (a fused
multiply-add contraction only removes a rounding, so the bounds hold: each is u times a sum of absolute values of terms). The
bit-for-bit identity of the leaf edges with the full sum also assumes that the compiled `boundary_edge_angle_x` gives the same
value at every call site (the JIT may inline it differently in the tree and in the full sum; .NET does not reassociate or
contract floating-point expressions, which the exact code relies on throughout, and no leaf term of the margin depends on it:
a leaf difference would be charged as 1e-12 per edge, 100 times the 1e-14 of the proof). (ii) `Math.Atan2` returns a result within 2 ulp of the exact
value. It calls the platform's C runtime (glibc's libm documents at most 1 ulp for atan2 on x86-64; the .NET documentation
states no bound), so it is a measured assumption: `MathAtan2_IsWithinTwoUlpOfTheExactValue_OnTheArgumentsOfTheFastPath` compares
6000 argument pairs (exponents 2^-200 .. 2^200, ratios 1e-15 .. 1e15, near ±π) with an exact arctangent (a fixed-point
Taylor series over 2^-220) and asserts ≤ 2 ulp; the largest seen on the CI-type Linux x64 runner is 1.0 ulp. A platform whose
atan2 is worse fails that test and the 1e-14 test (then the 1e-12 charged per edge still holds up to 1e3 ulp).

**Scope of A1 (#1427).** Proved: for the edges of an *accepted* node and the chord, the computed angle is within 1e-14 of the exact one
(audit model BT-5 checks the arithmetic of the proof's constants: 9.87e-15; with the 16u that step 4 itself gives instead of the loose 22u: 7.7e-15). What makes the proof apply is the certificate (`fast_path_certificate`; the clause `ball_is_small`, `r < d/4`, gives the separation),
not the fast path alone: the certificate keeps the two unit vectors of an edge within 2 asin(1/4) of each other, so p is at an angular
distance of at least pi - 0.505 from every edge of the node, where `hypot(num, den) = |a||b| sqrt(2 (1 + U.V) x_u x_v)` is no smaller than
1.93 times the size of the terms (the error of num and den is then a constant times u of that size). Not claimed: any edge on the fast path.
For p near an edge, `1 + U.V` goes to 0 and the same steps 4 and 5 give an error of up to (pi / 2)(2 u S1 + 22 u S2) / hypot + 2 pi u +
2 ulp, about 8e-15 / sin(theta / 2), that is 1.6e-14 / theta, for the angular distance theta of p from the edge: unbounded as theta goes to 0. That was the
fast formula of master, and the exact zone (a sign guard) did not bound it. It is corrected by "The accuracy of the edge angle" below (`finish_edge_angle`: the rounded angle is kept only where
hypot ≥ 0.0220 (S1 + S2), error at most 1.8e-13, else the accurate evaluation; audit model BT-6 re-derives the rounded part). The leaf edges of the tree and the full sum evaluate the same function bit for bit, so the
tree's decisions do not depend on this; it is an accuracy statement about the full sum (the value of `Evaluate`) for p near an open edge.
*The rescaled path* (`boundary_edge_angle_x_extreme`: `rescale_diff_2d`, `scaled_d_factor`, `rescale_pow2`) has no proof of A1: the steps 1 to
5 are for the fast formulas (rounded differences, `da`, `db`, `den = da db + dotYZ` in doubles). The rescaled path computes `da` as
`dVal * 2^dExp` with three branches (the exact cancellation-free value, `yz2 / (2 mx)` for a relative exponent above 60, `sqrt(yz2)` below
-60, each an approximation of relative error below 2^-60 that the proof does not count), cuts the product at an exponent of 180 and the
angle at 2^-100 (`on_vertex_angle_x`), has products that may be subnormal (the proof's underflow estimate uses `la, lb >= 0.75 * 2^-97`), and
has an exact zone on rescaled products. A proof would have to give the error counts of those branches in the style of step 4, show that the
cut-offs are below the 1e-14 (the angle of a cut is below 2^-100 against 1e-14: they are), and cover the subnormal range by an absolute term.
What it would buy: a weaker certificate, without the clause `d in [2^-97, 2^99]`. Today the cost is at extreme magnitudes only. Measured on an
open grid of 636 boundary edges (503 nodes), 40 queries per scale, scaled by 2^k (exact), counts per query (a one-off measurement; the test `TreeUse_ByScale_IsUpToAboutTwoToTheNinetyAndExactAboveAndBelow` in `ExactWindingCacheInternalTestsBoundaryTree.cs` reproduces the qualitative statement on the same grid: with 8 queries per scale, nodes accepted and no leaf edge at 2^0, 2^60, 2^90, 2^-90, no node accepted and every edge evaluated at 2^150, 2^200, 2^-100, 2^-150, no tree at 2^230, 2^500, 2^-300), counts per query: k = 0 to 90 accept 15 nodes and
evaluate no leaf edge; k = 99: 14 accepted, 100 of 636 leaf edges; k = 100: 1 accepted, 618 leaf edges (almost every node fails the range
clause); k = 150 and 200: 0 accepted, all 636 edges, the cost of the full sum plus the walk; k = 230 and 500: no tree (`Build` stops at
`mMax > 1e65`); k = -90: as k = 0; k = -97: 493 leaf edges; k = -98 and -100: all 636; k = -300: no tree. So the tree is exact but of no
use outside about 2^-97 to 2^99 of the scale of the data; the range clause is the only certificate clause that fails there (the yz clause
did not fail in these runs). A tree below the range would also have to cover the squared-distance gates (`1e-140`, `1e140`) and `pm <= 1e70`.

**Test power.** The oracle test of the proved bound does not fail without the certificate: outside it the edges stay accurate in
the observed range (4000 edges behind p near the x axis and at scales 2^-130 .. 2^130, the rescaled path, have an error of at most
3e-21, since the angles are tiny), so the certificate restricts the *proof*, not an observable error, and no oracle case can
distinguish it. It is killed by the status asserts (`NodesOutsideTheFastPathCertificate_AreDescended_...`, a node behind p or at d
outside [2^-97, 2^99] must descend) and the predicate's boundary test (`FastPathCertificate_Boundaries...`), which pin each
clause. The `Math.Atan2` test is a measurement of a library function (a precondition of the proof), not a mutation target.

`ExactWindingCacheInternalTestsEdgeAngleOracle` checks the claim: `EdgesOfAcceptedNodes_HaveAnErrorBelowTheProvedBound` compares the angle of every
sampled edge of accepted nodes (and the chord) with the exact one (integers over the grid 2^-1074, exact square roots, the
cancellation-free quotient for da) and asserts ≤ 1e-14; `EdgeAngle_ErrorIsBelowTheAssumedBound_OnAdversarialEdges` takes
6300 adversarial edges outside the regime (a vertex within 1e-12 rad of the +x ray, p near the yz line of the edge, p near a
vertex, yz 1e-5 .. 1e-45 of x, large offsets, scales 2^-90 .. 2^90): the largest error is 2.5e-15. That second test is evidence
for the code outside the proof, for its families only (by construction p is not close to the edge in 3D there: an x offset of the size of
the edge, so the growth of the error near an edge, above, is not in it); the decision does not rest on it, because a node outside the certificate is not used.

**The accuracy of the edge angle (every edge, not only those of an accepted node).** `boundary_edge_angle_x(p, a, b)` returns
T = atan2(num, den) with num = a_z b_y - a_y b_z and den = da db + a_y b_y + a_z b_z of the differences a - p, b - p, da = |a - p| - (a - p)_x,
db = |b - p| - (b - p)_x. **For every p, a, b of the supported range, T differs from the exact atan2 of the exact num and den (of the exact
differences of the doubles) by at most 2e-13; wherever the sign of num decides the side of the branch cut (den ≤ 1e-12 S2, S2 below), T is on the right side of the cut: the sign of the rounded num is the exact one where |num| > 1e-12 S1 and S1 ≥ 2^-1000, and where S1 < 2^-1000 (the products of num are subnormal) the exact path decides.** The bound is 1.2e-13 on
the rounded path and 2e-15 on the accurate path below; the 2e-13 leaves a margin for the library's `Math.Atan2` (assumed within 2 ulp of the
result, as in step 5 above) and for the second-order terms. The limit cases keep their definition: p on a vertex, p on the open edge, and p on the yz line
of the edge (exact num = 0) are decided as before (`on_vertex_angle_x`, `on_open_edge_angle_x`, `edge_angle_on_yz_line_x`).

The proof of A1 above covers the edges of an accepted node, where the cancellation of den cannot happen; it does not cover an edge
for which p is near the open segment (a, b). With A = a - p, B = b - p, u = A/|A|, v = B/|B| (unit vectors), da = |A|(1 - u_x), db = |B|(1 - v_x) and
dotYZ = |A||B| (u·v - u_x v_x), so that den = |A||B| (1 - u_x - v_x + u·v) and num = ±|A||B| (u×v)_x, the identity of step 2 below gives
hypot(num, den) = |A||B| sqrt(2 (1 + u·v)(1 - u_x)(1 - v_x)): the identity of Van Oosterom and Strackee (1983), det(u, v, w)² + (1 + u·v + v·w + w·u)² = 2 (1 + u·v)(1 + v·w)(1 + w·u) for unit vectors, with w = (-1, 0, 0), where det(u, v, w) = ±(u×v)_x and 1 + u·w = 1 - u_x. The terms of den are of the size S2 ≈ |A||B| ((1 - u_x)(1 - v_x) + |u_yz||v_yz|);
den (and with it hypot(num, den) relative to S1 + S2) cancels only where 1 + u·v ≈ 0, that is where A and B are antiparallel **in 3D**, p near
the open segment (a, b) (p almost on the line of the edge is not enough: p = (-10, 0, 0), a = (0, 1, 0), b = (0, -1, 0) has the yz parts exactly antiparallel
and den = -0.9975, no cancellation; the other factors, 1 - u_x and 1 - v_x, shrink S1 and S2 as fast as hypot). There den = da db + dotYZ cancels: da db is about
|yz_a||yz_b| and dotYZ about -|yz_a||yz_b|, the rounding of the two terms is 1e-16 |yz_a||yz_b|, while hypot(num, den) = theta |a||b| sqrt(x_u x_v)
(theta the angular distance of p from the edge), so the error of the rounded angle grows like 1e-16 / theta (7e-15 at theta = 1e-2, 2e-9 at 1e-7,
0.15 rad at 1e-15, before the fix). The second reason for the accurate path is the branch cut: where den ≤ 1e-12 S2 and |num| ≤ 1e-12 S1 (p near the yz line of
the edge, the edge in front of p along +x) the sign of the rounded num is not certain, and a wrong sign is an error of 2π. The fix is a test in the one function that both the full sum and the tree use (`finish_edge_angle`), and an
accurate evaluation behind it (`accurate_edge_angle_x`).

*The test.* Let S1 = |p1| + |p2| and S2 = da db + |a_y b_y| + |a_z b_z| (p1 = a_y b_z, p2 = a_z b_y: the sizes of the terms, computed; S1 and S2 are the
quantities of the old zone test), and u = 2^-53. Rounded evaluation, each rounded difference ax, ay, ..., bz with relative error at most u:

- num: p1, p2 are one product each of two rounded differences, relative error at most 3u; num = fl(p2 - p1): |Δnum| ≤ 3u S1 + u |num| ≤ 4u S1.
- da: yz² = ay² + az² has at most 4u (two squares at 3u, one sum), |a|² = ax² + yz² at most 5u, la = sqrt(|a|²) at 3.5u. For ax > 0, da = yz²/(la + ax): la + ax at
  4.5u, the quotient at 9.5u; for ax ≤ 0, da = la + |ax| at 4.5u. So da and db have at most 9.5u. (The extreme path, `scaled_d_factor`, has the same
  bounds plus a truncation of at most 2^-59 relative where |ax| < 2^-59 |yz| and 2^-120 where |ax| > 2^60 |yz|; both are below 0.02u.)
- den: p = fl(da db) at 20u; dotYZ with an absolute error of 3u T + u |dotYZ| ≤ 4u T (T = |a_y b_y| + |a_z b_z|); den = fl(p + dotYZ):
  |Δden| ≤ 20u p + 4u T + u |den| ≤ 21.1u S2. (The 22u S2 of step 4 above is this bound with the rounding of the differences counted differently;
  22.1u S2 is used below.)

With Δ = (Δnum, Δden), |Δ| ≤ hypot(4.01u S1, 22.1u S2) ≤ 22.1u (S1 + S2). The exact (num, den) is within |Δ| of the computed pair; if |Δ| < hypot(num, den) = R,
the two vectors are at an angle of at most asin(|Δ|/R) ≤ 1.0001 |Δ|/R (for |Δ|/R ≤ 0.01), and atan2 is the angle of the vector. The code keeps the rounded
evaluation when **|num| + |den| ≥ 2^-5 (S1 + S2)**. Since R ≥ (|num| + |den|)/sqrt 2, this gives R ≥ 0.0220 (S1 + S2) (2^-5 / sqrt 2 = 0.022097), |Δ|/R ≤ 22.1 · 1.11e-16 / 0.0220 = 1.12e-13, and the
rounded angle is within 1.12e-13 of the exact one, plus 2 ulp of Atan2 (8.9e-16): 1.2e-13. The constant 2^-5 is the choice that keeps a factor 1.6 of margin
to the 2e-13 stated above and leaves the rounded path to every edge whose cancellation is less than a factor 45 (the bit-identity test samples 40000 random edges and finds more than 35000 of them on the rounded path even at the stricter 2^-4). The
comparison is `an + |den| < 2^-5 (s1 + s2)` (strict: equality would be a first-order tie that either path answers within the bound). Underflow, the accuracy of the angle: in the fast path S2 ≥ da db ≥ 2^-602 (|yz| ≥ 2^-100 |a|, 2^-100 ≤ |a| ≤ 2^100), so the absolute rounding error of the
subnormal products of num (at most 2^-1074 in all) is below 2^-470 S2 and does not touch the bound on |Δ| above; in the extreme path the rescaled yz has a component in [1, 2), S1 + S2 ≥ 1, and the truncation of `prod` below 2^-180 is at most 2^-175.
Underflow, the sign of num: the relative error 3u of a product holds while it is a normal number; below that the rounding to the subnormal grid has an absolute error of up to 2^-1075 per product
(2^-1074 for num), which is no longer below 1e-12 S1 once S1 < 2^-1074 · 10^12 ≈ 2^-1034, and a tiny num can have the wrong sign at the branch cut (reviewer's input of #1514: p = (0, 5.334489550943501e-77, 0), a = (2^-100, 6.963444032852826e-61, 1.3734963702551047e-262),
b = (2^-100, -7.374133604637322e-61, -1.4545023542892055e-262): +π from the rounded num, −π exact). The second clause therefore reads: den ≤ 1e-12 S2 and (|num| ≤ 1e-12 S1 **or S1 < 2^-1000**). For S1 ≥ 2^-1000,
2^-1074 ≤ 2^-74 S1, so the error of the rounded num is at most (4.01u + 2^-74) S1 and |num| > 1e-12 S1 fixes the sign. The decision `s1 < 2^-1000` is the one comparison against an absolute value, because the subnormal grid is absolute: a 2^k scaling that moves S1 across 2^-1000 can change the path (and the angle by at most 2e-13, both paths being within the bound); every other decision is invariant under positive 2^k scaling (all quantities scale exactly while they are normal), bit for bit. Where S1 < 2^-1000 and den > 1e-12 S2 the rounded path is kept: the angle is within 2^-470 of the exact one (a wrong sign of num there moves the angle by that much, not by 2π).

The second clause of the old zone test is kept: if den ≤ 1e-12 S2 and |num| ≤ 1e-12 S1 (the branch cut: the sign of the computed num is not certain), the evaluation is the accurate one.
Where the rounded path is taken and den ≤ 1e-12 S2, |num| > 1e-12 S1 ≫ 4u S1, so the sign of num is that of the exact num, and T is on the right side of the cut:
the sign of num is exact where it matters. Where den is clearly positive, the rounded path may carry the wrong sign of a tiny rounded num; the angle is then within 1e-13 of 0
and the bound above covers it (the sign is not claimed there).

*The accurate path.* The six differences are computed exactly (`ExactDyadic`) and placed on a common integer grid. num and dotYZ are exact integers. If num = 0 the
old exact-predicate logic decides (0 when dotYZ ≥ 0, else the exact Orient2d in xy and `EdgeSignX`, or the open-edge limit). Otherwise den = da db + dotYZ is not a polynomial;
da db is computed from integer square roots at k extra bits: la_k = isqrt(|a|² 2^2k) = |a| 2^k - ε1, ε1 in [0, 1), da_k = floor(yz² 2^2k / (la_k + ax 2^k)) for ax > 0 and la_k - ax 2^k otherwise.
Then |da_k - da 2^k| ≤ 2: for ax > 0 the denominator of the quotient is off by less than 1 against la 2^k ≥ da 2^k (la ≥ 1 on the grid), which moves the quotient by less than 1, and the floor adds at most 1.
den_k = da_k db_k + dotYZ 2^2k has |den_k - den 2^2k| ≤ 2 da_k + 2 db 2^k ≤ 2 da_k + 2 (db_k + 2) = 2 (da_k + db_k) + 4 (da_k db_k - da db 2^2k = da_k (db_k - db 2^k) + db 2^k (da_k - da 2^k)). k = 70 + max(0, bits(L) + 2 - bits(num)) with bits(L) the larger bit length of |a|, |b|
(floor of the square roots, from (bits(aa) + 1) / 2 of the squared lengths) is enough for every input, without a check: da, db ≤ 2 L < 2^bits(L) 2, so da_k + db_k ≤ 4 L 2^k + 4 and the error 2 (da_k + db_k) + 4 is at most 8 L 2^k + 12 < 2^(bits(L) + 3 + k) + 12 (the `Debug.Assert` states 2^62 (2 (da_k + db_k) + 4) ≤ |num_k|),
while |num| 2^2k 2^-62 ≥ 2^(bits(num) - 1 + 2k - 62) ≥ 2^(k + bits(L) + 9) (k ≥ 70 + bits(L) + 2 - bits(num)), 2^6 times the error even for |num| tiny against den. So the absolute error of den is at most
2^-62 |num| ≤ 2^-62 R. (An earlier version looped, raising k by 32 until this held; the derivation shows the loop never runs, so it was removed and a `Debug.Assert` states the inequality.) Both are converted to doubles with the same power of two (correct rounding by
`ExactDyadic.Quotient`: the same bits on every runtime), at most 1.2e-16 relative each, and the angle is the atan2 of the pair: the error of the angle is at most 1.0001 · 2^-62 + 1.2e-16 + 8.9e-16
(Atan2) ≤ 2e-15. If |num| underflows against |den| (a ratio below 2^-2074), its sign is kept (the smallest subnormal with the sign of num): the angle is then ±π or ±0 within 1e-600.
Cost: one evaluation with integers of a few hundred to a few thousand bits per call, about 10 us (benchmarks/ExactWindingBenchmark/README.md), only for the edges below the test. A point exactly on the yz line of the edge (exact num = 0, Orient2d) does not enter it: the sign of dotYZ is exact from the signs of the coordinate differences (the yz parts are collinear, so ay by and az bz both have the sign of the same factor), and `edge_angle_on_yz_line_x` decides the limit.

*Why no tree decision changes.* The edges of an accepted node and the chord are on the rounded path before and after, with the same bits. First clause: step 2 above gives
hypot(num, den) ≥ 1.93 la lb sqrt(x_u x_v) and S1 + S2 ≤ 6 la lb sqrt(x_u x_v), so (|num| + |den|) / (S1 + S2) ≥ hypot / (S1 + S2) ≥ 0.32; the code tests (|num| + |den|) against 2^-5 (S1 + S2) = 0.03125 (S1 + S2),
so the proved factor on the tested quantity is 0.32 / 0.03125, about 10 (it is not 0.32 against 0.0220, which is a bound on hypot, not the quantity compared; an independent review sampled
400k accepted-node configurations and found (|num| + |den|) / (S1 + S2) ≥ 0.4976). This separation comes from `ball_is_small` (r < d/4, which gives 1 + U·V ≥ 1.87 in step 1);
`fast_path_certificate` only fixes the regime (the fast path, the yz parts nonzero). The computed sums differ from the exact ones by 1e-13 relative, far below the factor 10.
Second clause (branch cut): it does not fire because step 2 shows that either |φ| ≥ 1e-6, and then |num| ≥ 1e-6 S1 > 1e-12 S1, or |φ| < 1e-6 (the yz part of the edge radial: num can be 0), and then den ≥ 0.999 S2 > 1e-12 S2;
the clause needs den ≤ 1e-12 S2 and (|num| ≤ 1e-12 S1 or S1 < 2^-1000); with S1 < 2^-1000 ≤ 2^-398 S2 (S2 ≥ 2^-602 on the fast path), |num| ≤ S1 is negligible against S2, and the first clause's bound |num| + |den| ≥ 0.32 (S1 + S2) gives den ≥ 0.3 S2 > 1e-12 S2. The leaf edges and the full sum call the same function. What changes are the edges within a factor of 45 of cancellation, where
the old value was wrong by up to O(1): the margin of the tree compares the tree with the full sum, and both now contain the improved value.

*What this means for a decision.* The winding number of an open surface jumps by 1 across the surface and is continuous around an open edge. For a point p that is **not on a triangle of the mesh**
the boundary sum Σ weight · T / 2π is within |∂M| · 2e-13 / 2π (plus the rounding of the accumulation) of the exact generalized winding number of the mesh,
and `IsInsideBatch` / `EvaluateAbove` decide `w > iso` from it: the decision is exact whenever |w_exact - iso| exceeds that error. The exact integers on closed meshes are also those of points off the surface.
For a point exactly on a triangle of the mesh the value is the principal value, see the next part.

*A point exactly on a triangle.* The winding number jumps across the surface and has no value on it; the value returned for a point p on a triangle of the mesh is the **principal value**: the limit of the mean of w over a small sphere around p. Every triangle whose plane contains p subtends exactly 0 at p in this mean (its solid angle is antisymmetric under the reflection through its plane, and the sphere is symmetric under it), so the principal value is the solid-angle sum over the triangles whose plane does not contain p. It is n + 1/2 at a point strictly inside a triangle of a closed mesh (the mean of the two sides n and n + 1), and on a flat face also on its inner edges and vertices; the dihedral fraction on a crease (0.25 on a cube edge, 0.75 on a reflex edge); the solid-angle fraction at a corner (0.125 at a cube corner); 0 for a single triangle or a flat sheet. It does not depend on the directions of the coordinate axes. It does not depend on the triangle order or the order of the corners of a triangle on a flat face and strictly inside a triangle (exact); on creases, corners and open meshes the rounded sum is taken in the order of the internal list, so the returned bits depend on the input order within the rounding (up to about 1e-16: 9 distinct bit patterns over 400 random orders at a cone apex), and a threshold exactly at such a value can flip with the order. The value at the symbolically perturbed point (the first paper's definition) is another choice: an integer on a closed mesh, but on a face it picks the side by the axes (y, then z, then x) and gives 0 or 1 at a cube corner; it was not chosen (research note `docs/research/EXACT_WINDING_ON_SURFACE_2026-11.md`). Neither source paper defines the principal value for the query: the first evaluates at the perturbed point, the second excludes points within 1e-14 of the mesh from its statistics.

*How it is computed.* The evaluation `chi + boundary sum` is the value at p_eps. For every triangle T that contains p (found by the exact predicates `Orient3d`, `Orient2d`: p strictly inside, on an edge or at a vertex of T) the evaluation contains w_T(p_eps), the winding number of the single triangle at p_eps; the correction subtracts it, so the sum over the triangles not through p remains, which is the principal value. Nothing is added back: an earlier version added `TriSolidAngle(a, b, c, p) / 4 pi` of T at p, which is exactly 0 for p in the plane of T and sits at the branch cut of the formula in floating point (+-0.5 inside the triangle by the sign of a rounded determinant, noise next to its edges), so the values on faces were off by 0.5 and varied with the triangle order. w_T(p_eps) is exact where p is strictly inside T: p_eps is infinitesimally on one side of the plane, the triangle subtends an exact hemisphere, +0.5 on the -N side and -0.5 on the +N side (the side by the exact sign of the first non-zero component of N in the order y, z, x). Where p is on an edge or at a vertex of T, w_T(p_eps) is `chi_T(p_eps)` plus the three edge angles `boundary_edge_angle_x` of T, each within 2e-13 of its exact value (limits at a vertex and on the open edge are defined exactly and evaluated within that bound), so the rounded value is within 3 x 2e-13 / 2 pi per triangle plus the rounding of the sum.

*The flat rule.* Where p is on an edge or at a vertex inside a flat face of a closed mesh, the rounded sum would leave 1e-16 noise on the exact n + 1/2 (23 of 11,182 points of the unit cube). The triangles that contain p on an edge or at a vertex are therefore collected during the evaluation and resolved by plane (`Orient3d` of the three corners). In a plane Pi, around p each triangle covers a sector (a half plane for p on an edge, the wedge of the corner at a vertex), and the signed cover c(phi) = the sum over the triangles of their orientation (+1 or -1 against the first one) times the indicator of the sector in the direction phi. Seen from p_eps, infinitesimally off Pi, a sector with its half plane or wedge subtends +-0.5 times the part of the full plane it covers, in the limit that depends on the direction of the in-plane offset of p_eps only through c(phi) at that direction; so the group's value is a single number independent of the offset iff c is constant, c = m, and then it is m copies of the whole plane: exactly m x (+0.5 on the -N side, -0.5 on the +N side), the same exact value as for a point strictly inside a triangle. c changes by +1 at the ray where a triangle's own sector starts and by -1 where it ends, whatever the orientation of the triangle (a reversed triangle starts where its sector ends in the common frame, and its indicator counts -1), so c is constant iff the multiset of start rays equals the multiset of end rays: that is necessary (a ray with unequal multiplicity is a jump) and sufficient (no jumps on the circle). The constant m is the signed number of sectors that contain the start ray of the first triangle (the lower ray included, the upper excluded). Both the ray equality (`same_ray`: `Orient2d` zero and the same side) and the containment (`ray_in_sector`) are decided by `Orient2d` in the projection along the axis of the plane's first non-zero normal component. A group whose sectors do not cover evenly (a crease, a corner, an open edge or vertex, a fan that does not close, a single triangle through p on its edge) takes the rounded sum of `tri_on_boundary_value`. A doubled sheet (a sheet and its reversed copy) has m = 0: the value is exactly 0; the same sheet twice has m = 2.

*Cost.* The grouping by plane takes one pass over the list per plane, so it is done only where the triangles through p lie in at most `MaxFlatPlanes = 8` planes (a flat face is one plane; a face that other bodies touch with a corner a few); where more planes meet (the apex of a cone has one plane per triangle) every triangle takes the rounded sum, and the work stays linear in the number of triangles through p (times at most 8 for the grouping). The rule depends on the number of planes only, not on the order of the triangles. The start and end rays of a group are sorted by their exact angle (`compare_rays`: the half plane by comparisons, inside a half plane the sign of `Orient2d`) and compared entry by entry, O(m log m) instead of the pairwise matching. Measured on a closed drum (a flat fan of k triangles on top, a cone below), one query: at the apex with k = 20000 3.4 s before, 6.9 ms after; at the fan centre 158 ms before, 13 ms after; values bit-identical on 3.4 million queries. With more than 8 planes through p the flat exactness is not provided (every triangle takes the rounded sum, within the bound); no realistic configuration needs it, a flat face is one plane.

*What stays inexact.* Creases, corners and open meshes: the principal value is real-valued there and is the rounded sum, within the number of edge angles of the triangles through p times the edge-angle bound (2e-13 / 2 pi each) plus the bound of the boundary sum of an open mesh (|boundary| x 2e-13 / 2 pi); an exact tie of a non-flat configuration (for example a crease that is exactly 0.25 or an open mesh exactly at 0.5) is not guaranteed to compare exactly. On a closed mesh the flat face is exact. A T junction (a vertex on the interior of another triangle's edge in the same plane) leaves collinear boundary edges in the cache that cancel only in exact arithmetic, so the value there is within the bound, not bit-exact. The soup path (`direct_sum`) skips a triangle whose plane contains p and sums the rounded solid angles of the others: the principal value within the rounding of that sum. In decisions the value 0.5 on a flat face of a closed mesh gives `w > 0.5` false (Outside), deterministically; reflex edges and corners are above one half.

Notation: u = 2^-53 ≈ 1.1e-16. Every quantity is in the mesh's scaled grid units, the units of `FarPoints.Segments`.
The names are those of the code: `AntipodalTree`, `antipodal_tree_value`, `tree_term`, `tree_decides`.

**Node build** (`AntipodalTree.node`; count = the run's segments + 1 points):

| quantity | computed as | why it is a bound |
|---|---|---|
| c | mean of the points | Any c serves: the fan from c spans the polygon inside the ball. Only r must refer to the stored c. |
| R | max ‖v − c‖ · (1 + 1e-14) | ‖v − c‖ has a subtraction, 3 squares, 2 sums and a root: within 4u relative, so it is rounded up. |
| AAbs | ½ Σ ‖(v − c) × (w − c)‖ · (1 + (count + 4) · 4.5e-16) | Each term is within about 6u; the sum of count + 1 terms is within (count + 1)u of the sum of sizes. The result is an upper bound. |
| VaErr | ½ Σ ‖…‖ · (count + 8) · 4.5e-16 | Per-component error of va, as for AAbs but for the signed sum. |
| Len | Σ ‖w − v‖ · (1 + (count + 4) · 4.5e-16) | An upper bound of the polygon's length. |
| M1 | max ‖v‖₁ | Exact: abs and max only. |

**Query of an accepted node** (`antipodal_tree_value`):
- d = dn (1 − 1e-15), where dn = sqrt(dx² + dy² + dz²) is within 2.5u relative. So d is a lower bound of |c − p|.
- dr = fl(d − r) can exceed the true d − r by u relative. The factor 1.01 on E covers the 3u that this causes in 1/dr³.
- Acceptance: r < θ d. Then rd = r/d is rounded up, cos α = sqrt(1 − rd²) comes out smaller, and the cap is larger,
  which is the safe direction.

**The cone test** −x1·u < cos α − ε, with:
- tolNode = 16 (2 (2.3e-16 (pm + M1) + pointError)/dr + 1e-15 + directionError). This bounds every node segment's
  `tol` of today's sum, because la, lb ≥ d − r and the L1 sizes are ≤ pm + M1.
- ε = 1e-12 + 2 sqrt(2 tolNode) + 4 (2.3e-16 M1 + δ)/dr + 2 directionError, with δ = 2.3e-16 pm + pointError, made
  of these terms:
  - **1e-12:** the roundings of u = dx/dn, of the dot product, and of cos α. These come to a few u.
  - **2 sqrt(2 tolNode):** the angle within which today's "ray through the segment" test can fire, with a factor 2 for
    margin.
  - **4 (2.3e-16 M1 + δ)/dr:** the angular displacement of the exact points and of the exact p relative to the doubles.
  - **2 directionError:** the error of x1 itself.

**The node's error E** (steradians). The code multiplies the sum by 1.01.

| term | source |
|---|---|
| AAbs · 2r / dr³ | the dipole remainder: the Jacobian of x/‖x‖³ has spectral norm 2/‖x‖³, and ‖x‖ ≥ d − r over the fan |
| (2 · 2.3e-16 M1 + δ) · Len / dr² | moving the curve, or p, by ε changes a closed curve's solid angle by at most ε ∫‖dl‖/‖x‖² ≤ ε Len/dr² (Biot–Savart); the curve's points move by their rounding, p by δ |
| √3 · VaErr / dr² | va's error times ‖f(c)‖ ≤ 1/d² |
| (Σ\|va_i dx_i\|)/(dd dn) · 2e-15 | the dipole's own roundings: 3 products, 2 sums, 1 product and 1 division, within 8u of the sum of the sizes (not of \|ω\|, which can cancel) |
| chord: `tree_term`'s termError + 2 · PortableAtan2Error | today's per-term error of the full sum |

The total is today's: err + 2.3e-16 · terms · abs, divided by 4π, plus 2.3e-16 (abs/4π + |w|).

**fullBound** (an upper bound of the bound of `antipodal_sum` with the same ray, at the exact point):
- **Leaf terms:** 4 tol/(hypot − 2 tol) + 2 PortableAtan2Error, or ∞ if hypot ≤ 2 tol.
  - At the exact point, today's tol is ≤ this tol, because it has no pointError or directionError.
  - Its (num, den) differ by at most √2 tol, so its hypot is ≥ hypot − 2 tol.
- **Node segments:**
  - For unit vectors, num² + den² = 2 (1 + A·B)(1 + A·x1)(1 + B·x1).
  - In the cap of angular radius α, the angle between A and B is ≤ 2α, so 1 + A·B ≥ 1 + cos 2α.
  - 1 + A·x1 = 1 − cos ∠(A, −x1) ≥ 1 − cos β, with β = acos(−x1·u) − asin(rd) − 1e-9 − ε. This needs β > 0;
    otherwise fullBound = ∞.
  - So hypot ≥ hmin = sqrt(2 (1 + cos 2α)) (1 − cos β) (1 − 1e-12).
  - Per segment: 4 tolU/(hmin − 2 tolU) + 2 PortableAtan2Error, with tolU = 1.01 tolNode. This is ∞ if hmin ≤ 2 tolU.
  - acos, asin and cos are runtime functions. They only set this gate, whose margins (1e-9, ε, 1e-12) are far above
    their ulp differences.
- **Σ|θ| for the summation term:** |θ| + termError per leaf, and 2π per node segment, because a principal value of
  2 atan2 is at most 2π. With 20000 segments this adds at most 2.3e-16 · 2e4 · 2π · 2e4 / 4π ≈ 4.6e-8.
- **Total:** ((errU + 2.3e-16 · N · absU)/4π + 2.3e-16 (absU/4π + |w| + bound)) · 1.01.

`tree_decides` accepts when |w − h| > bound + 2e-6 and fullBound ≤ 0.999e-6. Today's limit is 1e-6, so the full sum
gives a value with this ray, and section 2 applies.

## 5. Tests and checks (for the PR)

- **Equivalence on the hash gate.**
  - Models: the 21 open models (default under (A)), the 10 slow ones, the knot, and 55928.
  - Hashes and statuses must be identical to the base. The new counter `l2_hierarchical` and the fallback share are
    reported.
- **Property test.**
  - Inputs: random chains (closed loops, open chains, zig-zags), random p and x1, including x1 at the edge of the cone
    test.
  - The hierarchical value must be within B_h of the full sum, whenever it is used.
  - Without the −x1 side of the cone test, the whole-turn jumps must appear. This is the mutation that shows the test
    matters. A test on the x1 side alone changes no value, so a mutant dropping it would rightly survive.
- **Unit tests of the bound.** Ω of a planar polygon against the exact flux, at d/r from 1.5 to 100. The remainder must
  stay below A_abs · 2r/(d − r)³.
- **Mutations:**
  - drop the cone test (its −x1 side);
  - drop the remainder term E;
  - drop the HANDOVER on a failed leaf;
  - use w_h for the snap.
- **Similarity.All, serial against parallel, reuse.** As for #759.
- **Timing.** Before and after on 117959, 122796, 1087141, 56483 and 80105 (union, median of 3, alternating, run
  alone). Plus the fallback share.

## 6. Open points for the review

1. The margin ε of the cone test in the code: the roundings, and the sqrt(2·tol) term. The identity itself and the
   remainder bound were confirmed by the numerics review.
2. The rounding bounds of the stored node quantities and of the dipole term, in the scaled grid.
3. Whether L2's carry margin from w_h (smaller than today) loses more carried pieces than the hierarchy saves. The PR
   measures it; the decisions are unaffected either way.
4. θ and a second pass: measured, not argued.
5. **Open item (owner 2026-11-13, follow-up):** the tree could also decide pieces where the full sum gives no value on
   any ray, which today end up undecided and are read as outside. That is more correct but not bit-identical. Lever 1
   keeps the gate; the follow-up drops it after its own review and hash change.

## Sources

- J. Barnes, P. Hut, *A hierarchical O(N log N) force-calculation algorithm*, Nature 324, 1986: the opening criterion
  r/d < θ.
- G. Barill, N. Dickson, R. Schmidt, D. Levin, A. Jacobson, *Fast winding numbers for soups and clouds*, ACM TOG
  37(4), 2018: the dipole term of a cluster.
- A. Van Oosterom, J. Strackee, *The solid angle of a plane triangle*, IEEE TBME 30(2), 1983: the term S.
- M. Martens, J. Trettner, M. Bessmeltsev, *The Antipodal Method*, 2026: the boundary form of the open winding number.

The identity, the remainder bound and the use through the threshold claim are derived here and are for the review.
