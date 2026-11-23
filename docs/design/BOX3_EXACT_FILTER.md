# Exact line, ray and segment against box: the filter and its error bounds

`intersection/IntrExactBox3.cs` decides the closed solid box against a line, ray or segment exactly: no tolerance, touching counts. This note
derives the bounds of the double filter that decides almost all queries; what it cannot decide goes to integer arithmetic (`Dy`, `Frac`).

## 1. The decision

A component is `x(t) = o + t d`, `d = d1 - d0` (exact; `d0 = 0` for a line or ray, `d0 = o` for a segment between `o` and `d1`), with
`t` in `[tmin, tmax]` (none, `[0, inf)`, `[0, 1]`). The box is the intersection of three slabs `L_i <= u_i . (x - c) <= H_i`:

- axis-aligned: `u_i` the unit vector, `c = 0`, `L_i, H_i` the faces `Min_i - e`, `Max_i + e` (`e` the expansion, one rounding of each face);
- oriented: `u_i` the axis, `c` the center, `L_i = -E_i`, `H_i = E_i`, `E_i = Extent_i + e` (one rounding).

Each slab gives `L <= P + t Q <= H` with `P = u . (o - c)`, `Q = u . d`. For `Q > 0` the slab bounds `t` below by `(L - P) / Q` and above
by `(H - P) / Q` (the reverse for `Q < 0`); for `Q = 0` it either holds for all `t` (`L <= P <= H`) or for none. The contact is the interval
`[max lower bound, min upper bound]` with the limits of `t`: empty, a point (equal ends) or a segment. All of this is exact on the doubles as
given; the oriented box is the box when the axes are orthonormal (the assumption of the old code too), and the slab intersection otherwise.

## 2. Rounding model

`u` is `2^-53`. A double operation gives `fl(a op b) = (a op b)(1 + delta)`, `|delta| <= u`, unless the result is subnormal, where the
absolute error is at most `2^-1075`. `AbsErr = 10^-320 > 12 * 2^-1075` bounds the underflow of all operations of one bound.

## 3. P and Q (`Prep`)

`x_k = fl(fl(o_k - c_k) u_k)`: two roundings, so `|x_k - (o_k - c_k) u_k| <= (2u + u^2) |x_k| (1 + 2u)`; the sum `fl(fl(x_0 + x_1) + x_2)` adds
two roundings of partial sums each at most `u (|x_0| + |x_1| + |x_2|)(1 + 3u)`. Altogether
`|P - P*| <= 4.1 u (|x_0| + |x_1| + |x_2|) + underflow <= 6 u S + AbsErr`, `S = sum |x_k|`: this is `eP`. Likewise for `Q` with `dd_k = fl(d1_k - d0_k)`
(one more rounding, exact when `d0 = 0`): `|Q - Q*| <= 5.1 u sum |a_k| + AbsErr <= eQ`. A component that is exactly zero (`dd_k = 0` or
`u_k = 0` for all `k`) gives exact `Q = 0` and `eQ = 0`. For an axis-aligned box `P = o_i` and `Q = dd_i` have `eP = 0` and `eQ = u |Q|`
(`0` for a line or ray).

## 4. The bounds in t (`Step`)

Let `n = fl(L - P)`: `|n - (L - P*)| <= eP + u |n|` (`L` exact, one rounding), written `en`. The quotient `a = fl(n / Q)`. For `|Q| > 2 eQ` the exact `Q*` has the sign
of `Q` and `|Q*| >= |Q| - eQ`, and

    |a - t*| = |n/Q - n*/Q*| <= (|n - n*| + |n/Q| |Q - Q*|) / |Q*| + u |a|
             <= (en + |a| eQ) / (|Q| - eQ) + u |a|          (with (1 + u) absorbed in the factor 1.0000001 and 2 u |a|)

which is `TErr`: `ea = (en + |a| eQ) ri + 2 u |a| + AbsErr`, `ri = 1.0000001 / (|Q| - eQ)`. For exact `P` and `Q` (`eP = eQ = 0`) the two roundings
(difference, quotient) give `|a - t*| <= 2.5 u |a| + AbsErr`. These bounds hold when no operation underflowed or overflowed: `Q` outside `[1e-290, 1e290]` and a
finite bound `a` outside `{0} U [1e-290, 1e290]` are undecided (`return 3`, exact route).

### 4.1 The infinite bounds (the rule, with every error term)

A slab bound that is too large to be a number is replaced by an infinity of its sign, so that the algebra never forms `inf - inf` or `inf * 0`. That is sound
only if the replacement cannot change a comparison with any other value of the same run. The other values are: the bounds of the other slabs (and of the other
side of this slab), and the limits of the caller (`tmin`, `tmax`; 0 and none for a ray or line). Let `Huge = 1e290`. `Kind(a, ea, n, eP, aq, eQ)` classifies a
computed bound `a = n / Q` with its error `ea` (above; `ea` also for an infinite or overflowed `a`: NaN or infinite then means undecided):

- **0, a number:** `|a| + ea <= Huge`. Its exact value `t*` lies in `[-Huge, Huge]` and the relative bounds hold.
- **1, an infinity:** `|n| (1 - 2u) - eP - AbsErr > 2 Huge (aq + eQ)`. The exact numerator `n* = L - P*` has `|n*| >= |n| (1 - 2u) - eP` (`n` rounded once from `L - P`
  with `|P - P*| <= eP`; for an `n` that overflowed to an infinity, `|L - P| >= 1.7e308` takes the place of `|n| (1 - 2u)`), the exact `|Q*| <= aq + eQ`, so
  `|t*| = |n*| / |Q*| > 2 Huge`, and its sign is the sign of `n Q` (`|Q| > 2 eQ` keeps the sign of `Q*`, `|n*| > 0` that of `n`). This is **not** `|a| > Huge`: on an
  oriented slab `L - P` cancels, `eP` can be most of `|n|`, and a computed `a` beyond `1e290` can have an exact value far inside (the review of 23 November 2026: a computed
  `-1e290` bound whose exact value `-2.6e289` was the binding one).
- **2, undecided:** anything between (`Huge - ea < |a|`, not certainly beyond `2 Huge`), a NaN: the exact route.

So every number bound has `|t*| <= Huge`, every infinity has `|t*| > 2 Huge` on its side, and the limits of the caller are numbers within `+-Huge` (`Find` takes the exact route
for a limit beyond that; `+-double.MaxValue` means no limit). An infinity is therefore on the far side of every number bound and every limit, so it constrains nothing
that they do not already decide, and a comparison `lower infinity < upper number` is true for the exact values too. Two infinities on opposite sides of one slab (`a*` below `-2 Huge`, `b*` above `2 Huge`)
are a slab wider than any `t`: no constraint (`return 0`); two on the same side are undecided (`return 3`): the order of the exact values is unknown. The ray branch (origin certainly inside on the
entering side, only the upper bound) classifies its one bound the same way.

A numerator that overflows (`L - P`, `H - P` beyond 1.7976e308 while `P`, `L`, `H` are finite) is the same case: an infinity if `aq + eQ` is at most about `9e17`, else undecided. A non-finite `P`, `eP`, `Q`, `eQ` or extent
of an oriented slab (`inf * 0 = NaN` makes every comparison false) is tested once in `FilterOriented` (the sum of the 12 values and the three extents) and goes to the exact route.

`RayAabb` (exact `P`, `Q`; the box and the origin exact) never makes an infinity a value: a difference `face - origin` that overflows with `|di| > 1` returns -1; with `|di| <= 1`
the quotient is beyond `1.7e308 > Huge`, and every computed `tn` or `tf` outside `[Tiny, Huge]` returns -1 at the end (an infinite `tf` from the `min` is the one that other
slabs do not hide: `tf == +inf` returns -1, a `-inf` fails the range test). `RayAabbMay` is the miss test only: a difference that overflows gives an infinity in `tn` or `tf`, so the
margin `m` is infinite or NaN and `tn > tf + m` is false ("may hit"); finite huge values are numbers with the error `3 u` per bound, no infinity rule is used.

Standing test: `IntrBox3ExactTests.OrientedBoxes_CancellingSlabs_BoundsNearHuge_AgreeWithTheOracle_ForEveryEntry` (oriented boxes with the origin placed so that `P` cancels and
the bound is `1e285` to `1e292`, extreme scales, limits within `+-1e291`, seeded; every entry against the rational oracle; the ends by the route: `|g - e| <= 1/2 ulp(g)` in rationals where
the exact route ran, `6 u |e| + 1e-300` where the filter did, the points in the box).

A contact end beyond `+-double.MaxValue` is `+-Infinity` in `t` (it is a result, not a limit: the limits `+-MaxValue` mean "none"); the points are finite and on the box.

## 5. The verdicts (`Acc`)

Each slab updates `LoMinus = max(lv - le)`, `LoPlus = max(lv + le)`, `UpPlus = min(uv + ue)`, `UpMinus = min(uv - ue)` over the lower bounds `lv` and upper bounds
`uv` (the limits of `t` with error 0). The true `max lower <= LoPlus` and `>= LoMinus`; the true `min upper <= UpPlus` and `>= UpMinus`. So:

- `LoMinus > UpPlus`: the true max lower exceeds the true min upper: certified miss.
- `LoPlus < UpMinus`: the true max lower is below the true min upper: certified contact of positive length.
- otherwise undecided (a touching contact is always here: equal values with error `> 0`).

A slab with `Q = 0, eQ = 0` is decided on `P` alone (`P + eP < L` or `P - eP > H`: miss; `L < P - eP`, `P + eP < H`: no constraint; `eP = 0` and `L <= P <= H`: no
constraint; else undecided). The ends of a certified contact are returned from this route only when `err <= 6 u |t|` (a slab with a cancelling `P` is not accurate
enough: exact route, rounded once). Points on the face of the active axis-aligned slab are set to the face.

## 6. The early tests

- A ray whose origin is beyond a face and does not move toward it (`P - eP > E`, `Q >= eQ`, `Q >= 0`, or the mirror) misses: the true `P* > E`, `Q* >= 0`
  give `P* + t Q* > E` for all `t >= 0`.
- An origin strictly inside all slabs (`P - eP > -E`, `P + eP < E`) is in the box: a hit at `t = 0`.
- A ray with the origin inside the slab on the entering side has a negative lower bound (`(L - P) / Q < 0` when `n + en < 0`, `Q > 0`): below the limit `0`, so only the upper bound is
  computed.
- `RayAabb`: the same for the axis-aligned box in few operations; the faces and the origin are exact, each `t` is within `2.5 u` of the exact value, so
  `tn > tf + 3 u (|tn| + |tf|)` is a miss and `tf - tn > 3 u (|tn| + |tf|)` a contact (`tn`, `tf` the computed bounds).
- `RayAabbMay` (culling in `DMeshAABBTree3`): the miss test only, no guards: a difference that overflows gives an infinity in `tn` or `tf`, so the margin `m` is infinite or NaN and `tn > tf + m` is false ("may hit"); an overflowed quotient is an infinity of the right sign, an underflowed one has
  absolute error `<= 2^-1070`; a miss claimed is a true miss. Where nothing is certified it answers "may hit" with `t` outward by `3 u t`.

## 7. Evidence

`IntrBox3ExactTests` compare every class with a BigInteger oracle that shares no code with the library (random, lattice and one-ulp-off cases at
scales `2^-10 .. 2^10`, `1e-3`, offsets `1e6`), check the conservative form never culls an exact hit, and cover the cases that reach each branch
(parallel slabs, bounds beyond `1e290`, expansions of `1e308`, overflowing spans, zero directions). `benchmarks/IntrBox3Benchmark` has the costs.
