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
  - the |θ| of the node's terms sum to at most π Len/(d − r).

With this, 112798 has its base hash again (34 undecided). The property test checks that wherever fullBound admits a
value, the full sum gives one within fullBound. It also covers rays just past a segment's end, where the full sum
fails. Loosening the gate, or dropping it, then fails the test. **Owner 2026-11-13:** strict bit-identity now. Letting the tree decide the pieces that are undecided today (24 fewer
counted as outside on 112798) is a later follow-up, open item 5 in section 6.

The tree is built only for boundaries of at least 1000 segments (`AntipodalTreeMinSegments`). Below that size, the full
sum is cheap and the tree did not pay: 124526 and 112798 have 134 and 164 segments.

## 4b. The bounds as implemented (for the review)

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
