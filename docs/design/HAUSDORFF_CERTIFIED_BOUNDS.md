# MeshHausdorffDistance: rounded subdivision points and the certified lower bound (#738)

Status: implemented (#738). Owner decision 2026-11-12: L + U0 (below). The fully certified upper bound is #742 (U2).

## 1. The defect
`MeshHausdorffDistance` refines triangles of A by midpoint subdivision. A midpoint `m = fl(0.5 * fl(a + b))` is not
representable when the coordinates are large compared with the triangles. Example: z ≈ 1.3·2^52, where an ulp is 1, and
spheres of radius 1, which are folded onto the levels z = -1, 0, 1. There, mid(-1, 0) = -0.5 rounds onto a corner.

- **Non-termination.** A child triangle is bit-identical to its parent. Its bound never drops, and each pop pushes two
  copies back. The run ended at `MaxSubdivisions`: minutes and GBs at the default of 20M.
- **Wrong lower bound.** The rounded points lie off the triangles of A, by up to 0.5. Their distances are not distances of
  points of A. The reported lower bound was 0.1834. The true distance is 0.00993, measured on the same meshes after an
  exact shift.

## 2. The fix
### (a) Exact per-axis shift on copies
**When.** The copy path (#731) also runs when the largest coordinate exceeds 2^24 times the extent
(`MaxPlainOffsetExponent`). UTM coordinates, 2^16 times a 100 m tile, stay on the plain path.

**The shift.** For each axis, with lo, hi the smallest and largest coordinate of the used vertices:
- if 0 < lo and hi ≤ 2·lo, shift by c = lo;
- if hi < 0 and lo ≥ 2·hi, shift by c = hi;
- otherwise c = 0.

**Exactness.** Sterbenz: for lo/2 ≤ v ≤ 2·lo, v − lo is exact. Every coordinate v of the axis lies in [lo, hi] ⊂ [lo, 2lo].

**Size after the shift.** If an axis cannot be shifted, its span already exceeds max/2: when lo > 0 and hi > 2lo, then
hi − lo > hi/2. So every coordinate after the shift is at most twice the span of its axis, and at most twice the extent.
The flat-axis shift of #735 is the case of span 0.

**Worst points.** They are shifted back by +c, rounded to doubles. Along a flat axis this is exact, because the scaled
coordinate is 0.

### (b) Progress guard
A child triangle is not pushed again, and its bound is kept in `stuck` (and `Upper ≥ stuck`), if either:
- its corners are the parent's corners as a multiset of points: splitting made no progress; or
- one of its corners may be off its triangle by more than the tolerance (`off > tol`, see (c)). Its gap cannot close
  below `tol`: every computed bound is at least the corner distance plus `off`.

`Converged` is false if `stuck > threshold + tol`.

### (c) The rounding allowance `off` and the certified lower bound
Notation: `fl` is a double operation, round to nearest. ε = 2^-53. a* is the exact point of A's triangle that a point a
stands for.

**c1. The midpoint's own error.** Per component k, TwoSum gives `s = fl(a + b)` and `r = (a − (s − bb)) + (b − bb)` with
`bb = s − a`, so that a + b = s + r exactly. This holds for any doubles without overflow (Knuth's TwoSum; it holds in the
subnormal range too).

The midpoint is `m_k = fl(0.5 s)`. Halving is exact unless the result is subnormal. Then it rounds by at most half the
smallest subnormal, 2^-1075.

The exact error of the component is m_k − (a_k + b_k)/2 = (m_k − s/2) − r/2. Two parts are bounded:
- **The halving of s.** |m_k − s/2| ≤ 2^-1075 if s ≠ 0 and |m_k| < 2^-1022, and 0 otherwise. The code adds one smallest
  subnormal, 2^-1074. This also covers m_k = 0 for a tiny s.
- **|r|/2.** It is computed as `fl(0.5·|r|)`, which is exact unless the result is subnormal, i.e. |r| < 2^-1021. Then the
  code adds 2^-1074 as well.

So e_k := fl(0.5|r|) + h_k ≥ |m_k − (a_k + b_k)/2|. The length of the error vector is at most the sum of its components'
moduli (L1 ≥ L2, no squares, which would underflow): err = `up(e_0 + e_1 + e_2)`.

`up(x) = max(fl(x·(1 + 4.5e-16)), next double above x)` for x > 0. It is above the exact result of up to two roundings that
produced x:
- **Normal x.** Each rounding is relative, at most 2^-53. Since (1 + 2^-53)^2 < 1 + 2.3e-16, the product
  x·(1 + 4.5e-16), rounded to nearest, which is at least x·(1 + 3.4e-16), is above the exact value.
- **Subnormal x.** Addition is exact there. A halving rounds by at most half the smallest subnormal, so one ulp up covers it.
- **Mixed.** A subnormal halving error of 2^-1075 inside a normal sum is below x·2.2e-16 for x ≥ 2^-1022.

The same `up` is used in c2 and c3.

**c2. Accumulation.** a and b stand for exact points a*, b* with |a − a*| ≤ off(a) and |b − b*| ≤ off(b). The exact midpoint
m* = (a* + b*)/2 is a point of the exact sub-triangle. Then

|m − m*| ≤ |m − (a + b)/2| + |(a + b)/2 − (a* + b*)/2| ≤ err + (off(a) + off(b))/2.

The code computes `off(m) = up(0.5·(off(a) + off(b)) + err)`. This has two roundings, a sum and a halving, the halving exact
for normal values, plus the final `up`, which covers both. The input vertices have off = 0.

**c3. Upper bound of a triangle (U0).** Let T be the rounded triangle with corners p_i, and T* the exact sub-triangle with
corners p_i*. Every point x* = Σ λ_i p_i* of T* is within Σ λ_i |p_i − p_i*| ≤ max_i off(p_i) of x = Σ λ_i p_i ∈ T. So
d(x*, B) ≤ d(x, B) + max off ≤ u(T) + max off, where u(T) is the bound computed for T. The code returns
`up(u + max off)` when off > 0.

u(T) is evaluated in floating point, as before (`TriangleDeviationBounds`). This is the model U0, stated in the API doc:
certified up to the floating-point evaluation of the distance queries, about 2^-50 of the coordinates. #742 (U2) removes
that caveat.

**c4. The certified lower bound (L).**
- The refinement runs unchanged on computed distances. `Lower` is the largest computed distance of a sample point, and
  `Worst` is that point.
- At the end, `CertifiedPointMeshDistance.LowerBound(To, p)` gives a value ≤ d(p, B) exactly, for two points: `Worst`,
  and the input vertex with the largest computed distance (its off is 0). See section 3.
- For `Worst`, the bound is reduced by its off: d(Worst*, B) ≥ d(Worst, B) − off(Worst), by the triangle inequality, and
  `Worst*` is a point of A. The subtraction and the result are rounded down (`ExactInterval.Down`, then `down`).
- The certified lower bound of the direction is min(`Lower`, max of the two certified values). Where the computed `Lower`
  is not above the exact value, it is reported unchanged, bit for bit.
- The symmetric result takes the larger certified value of the two directions.
- The second direction is pruned against the first direction's computed `Lower`. The first direction's `Upper` is at
  least that value, so every pruned triangle (bound ≤ prune) stays bounded by the reported upper bound.
- `Converged` is set to false if the certified lower bound is below the computed one and the gap exceeds `tol`.

## 3. `CertifiedPointMeshDistance` (exact point-to-mesh distance, lower bound)
**Pass 1.** For every triangle, a lower bound of the squared distance to its bounding box. Per axis, the gap below the box
or above it, each difference `Down`-rounded, each square `Down`-rounded, the sum `Down`-rounded (outward rounding,
`ExactInterval`). Any triangle with the smallest such bound is measured exactly; its exact squared distance D is an upper
bound of the minimum.

**Pass 2.** Every other triangle whose box bound is ≤ D, compared exactly (the double is decomposed into m·2^e), is
measured exactly. A box bound is a valid lower bound of that triangle's squared distance, so no nearer triangle is
skipped.

**Exact squared distance.** All 12 coordinates are written as integers times 2^e0, e0 the smallest exponent; this is exact
(Decompose). With e1 = V1 − V0, e2 = V2 − V0, d = V0 − P:
- a = e1·e1, b = e1·e2, c = e2·e2, dd = e1·d, ee = e2·d, f = d·d, and det = ac − b² (≥ 0, by Cauchy–Schwarz).
- If det > 0, s = b·ee − c·dd and t = b·dd − a·ee are det times the barycentric coordinates of the projection of P onto
  the plane.
- If s ≥ 0, t ≥ 0 and s + t ≤ det, the projection lies in the triangle. Then |d + S·e1 + T·e2|² with S = s/det, T = t/det
  is the squared distance: det²·d² = s(as + bt + 2·dd·det) + t(bs + ct + 2·ee·det) + f·det².
- Otherwise the nearest point lies on the boundary (also for det = 0: a degenerate triangle is the union of its edges). The
  result is the minimum of the three exact segment distances: for the segment [u, w], q = P − u, w' = w − u, t = q·w';
  |q|² if t ≤ 0, |P − w|² if t ≥ w'·w', else (|q|²·|w'|² − t²)/|w'|².
- All of this is integer arithmetic. The region test is exact.

**Conversion.** d² = num/den·2^exp goes to double with `ExactPoint3.ToDouble`. That truncates the quotient to 64 bits,
then rounds to nearest once, and once more in the subnormal range: at most one ulp above the exact value. `Down` is applied
twice, so the result is ≤ the exact value. Then `sqrt`, which rounds to nearest within half an ulp, and `Down` once more:
≤ the exact distance.

## 4. Behaviour on ordinary inputs (measured, 18 cases, symmetric and one-sided)
- **Refinement.** Unchanged: the same subdivisions in every case.
- **Lower bound.** Changes only where the computed distance of the worst point was above its exact distance, then by 1 to
  3 ulps down to the certified value. Examples:
  - spheres: 0.010000000000000142 → 0.009999999999999981;
  - two parallel grids 0.125 apart (closed form): the base reports 0.12500000000000003, above the exact distance; it is now
    ≤ 0.125.
- **Upper bound.** It grows by the off terms where midpoints round: 2e-16 relative near the origin, 1.1e-13 at offset 1e3,
  8.9e-10 at UTM coordinates (an ulp of 5.7e6 is 9.3e-10).
- **`WorstPoint`.** It can move to an equally distant point: the input vertex, when its certified value is the larger.
- **Time.** Up to about 40 ms more on 21k triangles, for the exact re-evaluation: two passes over B for two points. Other
  cases add 0 to 15 ms.

## 5. Tests and mutation
**Tests.**
- `HausdorffRoundedMidpointTests`:
  - folded spheres at offsets from 2^24 to 2^52;
  - the certified lower bound against the exactly shifted reference, also measured as given;
  - parallel grids at a closed-form distance (k = -40 … 20, offsets up to 2^49 times the size);
  - dense sampling, an independent brute force;
  - an interior maximum, children nearest one triangle, a needle that is its own child;
  - subnormal midpoints;
  - `Similarity.All()`, reuse, serial.
- `HausdorffRoundingTests`: the midpoint allowance against the exact error in integers, including subnormal sums and
  remainders and random pairs at all exponents.

**Mutation: 23 mutants, 20 killed.**
- **Equivalent:** the strict region test (`s > 0` instead of `s >= 0`). On the boundary of the region the face formula and
  the edge formula give the same distance.
- **Not killed:**
  - The guard on a child identical to its parent. It is reachable only when the tolerance exceeds the off of the rounded
    midpoint and the bound still exceeds lower + tol. In every constructed case the bound of such a needle was below that.
    It is kept as the stop against an otherwise endless loop.
  - The off term in a triangle's upper bound. In every constructed case a sibling with a looser bound covered the distance.
    It is needed for soundness wherever a tight bound (all corners nearest one triangle, or a polygon split) is taken on a
    rounded triangle.
