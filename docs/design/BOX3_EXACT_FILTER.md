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
(difference, quotient) give `|a - t*| <= 2.5 u |a| + AbsErr`. The relative bounds hold when no operation underflowed or overflowed: `Q` outside
`[1e-290, 1e290]` and a bound `a` outside `{0} U [1e-290, 1e290]` are undecided (`return 3`, exact route); a bound beyond `1e290` in magnitude counts as an
infinity of its sign (only compared with other bounds, never used as a value); two such on one slab are undecided, except `L - P < 0 < H - P`, where
the slab is wider than any `t` and constrains nothing. A numerator `L - P` or `H - P` that overflows to an infinity while `P`, `L`, `H` are finite is an infinity
only when `|Q| <= 1` (the quotient is then beyond the range too); for `|Q| > 1` the quotient is a finite `t`, so the slab is undecided (`return 3`; `RayAabb` returns -1,
`RayAabbMay` answers "may hit"). The same holds for a non-finite `P`, `eP`, `Q`, `eQ` or extent of an oriented slab (`inf * 0 = NaN` makes every comparison false):
`FilterOriented` tests their sum once and goes to the exact route.

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
- `RayAabbMay` (culling in `DMeshAABBTree3`): the miss test only, no guards except the overflowed difference of a quotient that is finite ("may hit"): an overflowed quotient is an infinity of the right sign, an underflowed one has
  absolute error `<= 2^-1070`; a miss claimed is a true miss. Where nothing is certified it answers "may hit" with `t` outward by `3 u t`.

## 7. Evidence

`IntrBox3ExactTests` compare every class with a BigInteger oracle that shares no code with the library (random, lattice and one-ulp-off cases at
scales `2^-10 .. 2^10`, `1e-3`, offsets `1e6`), check the conservative form never culls an exact hit, and cover the cases that reach each branch
(parallel slabs, bounds beyond `1e290`, expansions of `1e308`, overflowing spans, zero directions). `benchmarks/IntrBox3Benchmark` has the costs.
