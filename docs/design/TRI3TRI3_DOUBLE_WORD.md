# Certified double-word arithmetic for the end points of a triangle crossing

Status: implemented in `math/ExactDoubleWord.cs` and `IntrTriangle3Triangle3.find_transversal`. Back to the [design index](README.md).

`IntrTriangle3Triangle3.Find` of two triangles that cross in general position returns a segment whose two end points are the
crossing of an edge of one triangle with the plane of the other. This note derives the error bounds of the arithmetic that
computes those points as the **correctly rounded exact values**, without BigInteger in the common case, and states what makes the
result independent of whether the certificate decided: where it cannot decide, the exact route (`crossing_exact`) rounds the same
way.

## 1. What is computed

For an edge `s`-`t` whose ends lie strictly on both sides of the plane through `V0`, `V1`, `V2` (exact `Orient3d`), with
`n = (V1 - V0) x (V2 - V0)` and `d(p) = n . (p - V0)` (the orientation determinant up to the common factor 1):

    X = s + w (t - s),   w = d(s) / (d(s) - d(t)),   d(s) and d(t) of opposite signs.

`X` is a vector of rationals of the doubles; each coordinate is rounded to the nearest double, ties to even. The exact route computes
the same `X` with BigInteger determinants (`grid`, `det`, `lerp`) and rounds with `round_ratio` (quotient with a sticky bit, then
round to nearest even). The double-word route computes each coordinate as a `DoubleWord` `Hi + Lo` with a bound `Err` such
that the exact coordinate lies in `[Hi + Lo - Err, Hi + Lo + Err]`, and accepts `Hi` only if the rounding is certain (section 4).

Note on `ExactPoint3.ToDouble`: it truncates its 64-bit quotient and converts with `(double)BigInteger`, which rounds toward zero, so
it can be one ulp off (example in the PR). `round_ratio` is used instead in this class; `ExactPoint3` is unchanged.

## 2. Primitives and conditions

`u = 2^-53`. A `DoubleWord` is *normalized* if `|Lo| <= u |Hi|`.

- **Strict IEEE double rounding** is assumed for every operation (SSE2 / ARM64 doubles, as for the rest of the exact code of the library): the error-free transforms are wrong with
  extended-precision intermediates (a 32-bit .NET Framework process on x87); there is no 32-bit test run (see `ExactIntervals`, DETERMINISM_PROOF.md 5.3).
- **TwoSum(a, b)**: `s = fl(a + b)`, `bb = s - a`, `e = (a - (s - bb)) + (b - bb)`; `s + e = a + b` exactly for every pair of doubles
  without overflow (Knuth/Moller; no ordering condition, which is why FastTwoSum is not used). `|e| <= u |s|`, so the output is normalized.
- **TwoProduct(a, b)** (`Expansion.TwoProduct`, Dekker/Veltkamp split with the constant 2^27 + 1, no FMA): `x + y = a b` exactly if no
  overflow occurs in the split (`|a|, |b| < 2^996`) and the error terms are representable: every double of the input is a multiple of
  its `ulp`, so with `|a|, |b| >= 2^-480` the lowest bit of every partial product is `>= 2^-1064` (normal inputs have their lowest
  bit at `>= 2^-532`), a multiple of the smallest subnormal `2^-1074`, so every partial product and the error term are exact.
- **Range rule** (`Checked`): `Hi` is `0` or in `[2^-480, 2^480]`, else the value is `Invalid` (`Err = +inf`), which propagates. Sums and products
  of two values in range stay below `2^960` (no overflow); a product `xh yh` that is not `0` is `>= 2^-960` before the result is
  checked, and a result with `|Hi| < 2^-480` is rejected. Lower-order terms (`xl yh`, `xl yl`) can underflow by at most
  `2^-1075` each (absolute); a result in range has `|xh yh| >= 2^-480`, so `2^-100 |xh yh| >= 2^-580`, which absorbs those terms
  with a factor of `2^495` to spare. The same holds for the sum (`S >= 2^-480` for a non-zero result of a sum whose terms are
  normal, and the underflow terms are `<= 2^-1074`).

## 3. Operations and their bounds

Each bound is stated for normalized operands with errors `ex`, `ey` (`|X' - X| <= ex` for the exact values `X'` the operands stand
for) and is derived below with `gamma` terms: a computed `fl(a op b)` has `|fl - exact| <= u |fl|` (no underflow, section 2).
`Err` is then computed in floating point from non-negative terms (at most 12 operations, each with relative error `<= u`), so the
computed value is at least `(1 - 12 u)` times the real value of the formula. The constant `Eps2 = 2^-100 = 64 u^2` exceeds each derived
error bound below by a factor of at least 6.4, which absorbs the rounding of the `Eps2` term only. The other terms (the operand errors
`ex`, `ey`, `|X| ey`, ...) have no such slack, so the final `Err` of every operation is multiplied by `ErrUp = 1 + 2^-48 = 1 + 32 u`: the computed
value of the formula is at least `(1 - 12 u)` times the real one (at most 12 operations on non-negative numbers), and `(1 + 32 u)(1 - 12 u) > 1`,
so the stored `Err` is never below the real bound. (Without it: `x = (1, 0, Err 1)` plus `y = (1, 0, Err 2^-53)` stores `Err = 1` after rounding,
and the exact value can be `3 + 2^-53`. The tests `ErrorBound_HoldsAtTheOperandErrorExtremes_*` use exact operands at plus and minus `Err`.) In the formulas the propagation terms are `ex + ey + Eps2 (|xh| + |yh|)` for a sum
(relative to the operands' magnitudes, not to the result, which may cancel), `|X| ey + |Y| ex + ex ey + Eps2 |xh yh|` for a product,
and the residual formula of section 3.3 for a quotient.

### 3.1 Sum (`operator +`)

`sh + sl = xh + yh` (TwoSum), `th + tl = xl + yl` (TwoSum), `c = fl(sl + th)`, `vh + vl = sh + c` (TwoSum), `w = fl(tl + vl)`,
`zh + zl = vh + w` (TwoSum). With `S = |xh| + |yh|`:

- `|sh| <= (1 + u) S`, `|sl| <= u |sh|`, `|th| <= (1 + u)(|xl| + |yl|) <= u (1 + u) S`.
- `|sl + th| <= 2.1 u S`, so `e_c = |c - (sl + th)| <= u |c| <= 2.1 u^2 S`.
- `|vl| <= u |vh| <= u (|sh| + |c|) <= 1.1 u S`, `|tl| <= u |th| <= 1.1 u^2 S`, so `|tl + vl| <= 1.2 u S` and
  `e_w = |w - (tl + vl)| <= 1.2 u^2 S`.
- `x + y = sh + sl + th + tl = (vh + vl - c) + sl + th + tl`, and the computed `z = vh + w`, so
  `z - (x + y) = (w - vl - tl) - (sl + th - c)`, and `|z - (x + y)| <= e_w + e_c <= 3.4 u^2 S`.

So `Err = ex + ey + 2^-100 S` bounds the error: `2^-100 = 64 u^2` is 18 times the derived `3.4 u^2` (the `u^3` terms are
`< 10^-15` of that).

### 3.2 Product (`operator *`)

`ch + cl1 = xh yh` (TwoProduct), `a1 = fl(xh yl)`, `a2 = fl(xl yh)`, `a3 = fl(xl yl)`, `t1 = fl(a1 + a2)`, `t2 = fl(t1 + a3)`, `cl3 = fl(cl1 + t2)`,
`zh + zl = ch + cl3` (TwoSum). The cross terms are rounded separately, as FMA is not used. With `P = |xh yh|`: `|xh yl|, |xl yh| <= uP`,
`|xl yl| <= u^2 P`, `|cl1| <= u |ch| <= uP`. The roundings are

- `a1`, `a2`: each `<= u (uP) = u^2 P`; `a3`: `<= u^3 P`;
- `t1`: `|a1 + a2| <= 2.1 uP`, error `<= 2.1 u^2 P`; `t2`: `|t1 + a3| <= 2.2 uP`, error `<= 2.2 u^2 P`;
- `cl3`: `|cl1 + t2| <= 3.3 uP`, error `<= 3.3 u^2 P`.

Their sum is `<= 9.7 u^2 P + O(u^3 P)`: the computed `z` differs from `x y` by at most `10 u^2 P` (and the TwoSum is exact). With operand
errors, `|X' Y' - X Y| <= |X| ey + |Y| ex + ex ey` (`|X| <= |xh| + |xl|`, `Mag`). So `Err = |X| ey + |Y| ex + ex ey + 2^-100 P`;
`2^-100 = 64 u^2` is 6.4 times `10 u^2`.

### 3.3 Quotient (`Div`)

`q1 = fl(xh / yh)`, `r = x - y q1` (with `*` and `-` above), `q2 = fl(rh / yh)`, `q = q1 + q2` (TwoSum, exact), `res = x - y q` (certified).
The bound does not depend on how good `q` is: the exact values `x'`, `y'` satisfy `x' / y' - q = (x' - q y') / y'`, and

    |x' - q y'| <= |res.Hi + res.Lo| + res.Err

(`res = x - y q` is a certified difference and product of the operands, so `res.Err` already contains `ex` and `|q| ey`: they are not
added again), and `|y'| >= |yh| - |yl| - ey`. So

    Err(q) = (|res.Hi + res.Lo| + res.Err) / (|yh| - |yl| - ey) (1 + 2^-48),

valid whenever the denominator `|yh| - |yl| - ey` is positive and `ey <= |yh| / 4` (else `Invalid`): with that guard the denominator is at least `0.7 |yh|`, so the two roundings that compute it (each at most `u` relative to its result, which is at least `0.7 |yh|`) cost at most `3 u` relative, inside the `ErrUp` margin. Without the guard `fl(fl(|yh| - |yl|) - ey)` can be larger than the true value by any factor (`y = (1, -2^-54, err 1 - 2^-53)`: computed `2^-53`, true `2^-54`), `Div_ByAnUncertainDenominator_...`. Since `q` agrees with `x / y` to about `2^-104`
relative (two correction steps), `Err` is about `2^-100` relative; the bound itself is valid for any `q`.

## 4. Certification of the rounding (`RoundsTo`)

A result `(Hi, Lo, Err)` with the exact value `V'` in `[Hi + Lo - Err, Hi + Lo + Err]`: `|V' - Hi| <= |Lo| + Err`. The nearest double to `V'`
is `Hi` if `|V' - Hi|` is smaller than half the gap to each neighbour of `Hi`. The gap above `|Hi|` is `ulp`; the gap below is `ulp`
too, except when `|Hi|` is a power of two, where it is `ulp / 2`. `RoundsTo` uses the smaller gap `g` on both sides and requires
`|Lo| + Err < g / 2`, strictly: at equality `V'` can be the midpoint (a tie, which rounds to even and may not be `Hi`). The conversion is mirrored
for negative values (magnitudes). An exact zero (`Hi = Lo = 0`, `Err = 0`, as the product of an exact zero, e.g. the difference of equal
coordinates) is certified; any other value with `Hi = 0` is not (the value may be any tiny number). A tie never certifies: `|Lo| + Err >= g/2`
since `Err > 0` whenever a rounding happened and a tie of exact operands has `|Lo| = g / 2` (a `TwoSum` result: `Hi` is then the
round-to-even neighbour, correct, but this is not relied on).

## 5. Selection of the end points (exact)

`odd_vertex`: no corner of either triangle lies in the plane of the other (`Orient3d`), and the corners of each triangle are split 1 against 2 by the plane of
the other, so each triangle has two crossing edges, those at the odd corner. The overlap of the segments in which the triangles cut the line `L` of the two planes
is the contact. Its end points are crossing points of those four edges with the plane of the other triangle, and a crossing point `X` of the edge `s`-`t` (strictly
across the plane) lies in the closed other triangle iff the three `Orient3d(s, t, a, b)` for its edges `a`-`b` do not have both signs (the tail of
`ExactTriangleIntersection.SegmentIntersectsTriangle`). For a contact of positive length exactly two of the four points qualify (an end of the overlap of two
intervals on `L` is an end of one of them lying inside the other, and the other two points lie outside), so `n == 2`; any other count leaves the pair to the exact route.
No floating-point comparison orders the points, so there is no margin or conditioning gate: an ill-conditioned point simply fails the certificate and takes `crossing_exact`.

A contact that is a single point has `n == 2` with both qualifying points equal: if a crossing edge of A is coplanar with a crossing edge of B, the crossing point of each
with the plane of the other triangle is the intersection of the lines of the two edges (the common plane of the two edges meets the plane of B in the line of B's edge,
which contains A's crossing point, and likewise for B's crossing point), the same rational point; both lie in the other triangle (each is on an edge of its own triangle, the
intersection of the two edge lines being on both edges), and both are rounded correctly, so `p0 == p1` and the pair goes to `find_exact`, which reports the Point. (A check
that rejected a coplanar pair of edges with four `Orient3d` was redundant with this and is removed.)

## 5a. The two routes return the same doubles

`crossing_dd` returns a point only when every coordinate is certified (section 4): the exact coordinate `V'` rounds to `Hi` under every value within `Err` of `Hi + Lo`, so
`Hi` is the correctly rounded `V'` (nearest, and not a tie); `crossing_exact` returns the correctly rounded `V'` (`round_ratio`: truncated quotient with a sticky remainder, ties to even).
A rounding to nearest is unique except at a tie, which `RoundsTo` never certifies, so the two routes return the same double for every input, not only for the tested ones; the tests check this on
random, axis-aligned, scaled and tie inputs. Consequently skipping the double-word route (or any guard that only hands the pair to `find_exact`) cannot change a result.

## 6. Tests and mutation checks

`IntrTriangle3Triangle3DoubleWordTests`: the error bound of each operation against BigInteger fractions on random and cancelling
operands (including operands that carry an error), `RoundsTo` at the tie and power-of-two boundaries, ties constructed with `w = 1/6`
(`t.x - s.x = 3 k 2^-52`, `k` odd: `x` is exactly a midpoint) that must round half to even through the exact route and must not be
certified, near ties, the crossing point of random, axis-aligned and scaled edges against an independent oracle (nearest double of the exact
rational), and `Find` end points against the rounded crossings of the edges. Mutation results: see the PR.

## 6a. Mutation checks of the changed lines (local run, 16 mutants of `ExactDoubleWord.cs` and `find_transversal`/`round_ratio`)

Killed (12 of 16 at the first run, `passes_through` and `p0 == p1` after the tests added for them; one guard argued, one removed): `Eps2` to `2^-106`; `<` to `<=` in `RoundsTo`; dropping `Err` from `RoundsTo`; the power-of-two lower gap; the `Eps2` term of the sum, of the product,
the residual error of the quotient; the exact-zero certificate; the range rule; and the two `round_ratio` variants (no sticky remainder; always round up at the midpoint).
Survivors, argued:

- Disabling the double-word route (`crossing_dd` never used): section 5a, the output is the same by construction, only the route differs (section 7 measures the difference).
- `passes_through` always true: four candidates, `n > 2` returns false, the pair takes the exact route with the same result; it is killed instead by
  `FindTransversal_IsTakenForGenericCrossings_...`, which asserts that the fast route is taken for generic crossings.
- The coplanar-edge check (four `Orient3d` per hit) is removed: section 5 shows `p0 == p1` for a touching contact, which `FindTransversal_...` and `EdgeCrossingEdge_...` pin (a mutant that drops
  `p0 == p1` fails them). `p0 == p1` also covers a rounding collapse of a positive-length segment, for which no input was found (400000 one-ulp nudged edge pairs, 300000 hits, none equal): kept as a guard.
- `n != 2` weakened to `n == 0`: the case `n == 1` does not occur (section 5: a contact has two qualifying points or none), so the check is a guard against a violated assumption, not covered by a test.
- The `Div` guard `err(y) <= |yh| / 4` is needed (without it the rounded denominator can exceed the true one); `yh == 0` gives `den <= 0` and an infinite `q1` is `Invalid`, so that guard stays removed.
- A product whose error underflows to 0 while an operand error is not 0 (`x = 0 +- 1e-300`, `y = 1e-100`) would have been a certified zero, whose sign the exact route may give differently: the error is floored at the smallest double (`Product_WhoseErrorUnderflows_...`). The same floor applies to the quotient (`x.Err / |y|` can underflow: `0 +- 1e-300` divided by `1e100`).
- The factor `1 + 2^-48` (`ErrUp`) is needed for the operand-error terms (section 3); the tests with exact operands at plus and minus `Err` fail without it.

### Stryker run (22 November 2026, `tests/mutation/stryker-config.json`, the spans of the exact route and `ExactDoubleWord.cs`, 224 mutants)

Score 94.2 % at the first run; the survivors that a test can tell are killed by direct tests of the computed values (`Mag_...`, `Product_KeepsTheProductOfTheLowWords`,
`Invalid_OperandError_...`, `Div_ErrIsTheResidualBoundOverTheLeastDenominator`, `RoundRatio_...`), each checked by hand against its mutant: `Mag` with `-`,
`t -= xl yl` (the cross terms cancel, only `xl yl` is left), `|yl|` with `+` in the denominator (a power of two with the largest `Lo`), `err <= inf` in `Checked`
(an infinite `Err` must give the `Invalid` value itself), the shift direction of `round_ratio` (a numerator just above a tie loses its low bit in a right shift).
The residual's `|res.Lo|` is read through `res.Mag` (one place, tested). The remaining survivors cannot change any value the code computes:
- `round_ratio`: `num.Sign <= 0` (zero returns on the line before); `shift > 0` for `>= 0` (a shift by 0 is the identity on both branches); `unit <= -1074` (the branch adds 0);
  `q >>> extra` (`q` is positive, where `>>>` and `>>` of a `BigInteger` agree); `den.Sign <= 0` (`den` is the difference of two non-zero values of opposite signs, never 0).
- `Div`: `||` to `&&` of the validity test (an invalid `x` or `y` has an infinite `Err`, which `r` or `res` carry, so `Div` returns `Invalid` anyway); `den <= 0` to `den < 0`
  (`den == 0` gives an infinite or NaN `Err`, which `Checked` rejects).
- Routing guards outside the spans (`find_transversal`, `odd_vertex`): a mutant there changes only which route computes the pair; the pair then goes to `find_exact`, whose output is bit-for-bit the
  same (section 5a, `FindTransversal_...` and the route-equality tests compare the two routes on all families).

## 7. Cost (22 November 2026, Release, 4 cores, `Find()` on 20000 hit pairs, `Stopwatch` per call)

| `Find()` on hits | master | double-word |
|---|---|---|
| random mesh-like, median / p90 | 759 / 923 ns | 952 / 1632 ns |
| axis-aligned lattice, median / p90 | 894 / 1098 ns | 1034 / 11088 ns |

Fallback to the exact route: 0 of 40000 points (random), 4.8 % (axis-aligned: a coordinate that is zero by construction, eg `s.x = t.x = 0`, does certify; a zero produced by an interior crossing, where the terms cancel with a non-zero `Err`, cannot be certified). The exact route
costs 3.2 to 7.7 us per point, so an approach without a certificate (plain double with a bound that falls back more often) cannot be cheaper.
