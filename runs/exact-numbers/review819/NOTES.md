# Numerics review of #819 (MeshHausdorffDistance, certified bounds, #738), head 437ad891

Read: docs/design/HAUSDORFF_CERTIFIED_BOUNDS.md (sections 2, 3), mesh_ops/MeshHausdorffDistance.cs (HausdorffRounding,
OneSided, certify, the scaled / shifted copies), queries/CertifiedPointMeshDistance.cs, ExactPoint3.ToDouble / ScaleB,
ExactInterval.Up / Down. Probe: `ReviewProbe819Tests.cs` (local, not part of the PR): exact integer checks of c1, c2 and
of section 3, and the error of the float distance query. The PR's 93 Hausdorff tests pass on the head.

## Findings

F1 (c1, a counterexample to the stated bound; exotic, no effect on a reported result). The halving allowance is tied
to the midpoint (`sum != 0 && |m_k| < 2^-1022`), but for the one value |s| = 2^-1021 - 2^-1074 (the largest double
below 2^-1021) the half 2^-1022 - 2^-1075 is a tie and rounds up to 2^-1022, a normal number: the halving rounded by
2^-1075 and no allowance is added. Example: a_k = 4.4501477170144023E-308, b_k = 0: m_k = 2.2250738585072014E-308,
exact error 2^-1075, err = 0. Probe: of 400,000 pairs, 6335 have such a sum in a component, 2542 of them get an err
below the exact L1 error; with 2^-1075 per such component the bound holds in every pair, and no other pair fails
(largest err / exact in the normal range 1.0000000000002). Fix: test the sum, `Math.Abs(sum) < 2 * MinNormal`, and
write "if 0 < |s| < 2^-1021" in c1; add the value to HausdorffRoundingTests (no test has it).

F2 (c3 and the API doc: the size of the U0 caveat is misstated by orders of magnitude). "certified up to the
floating-point evaluation of the distance queries, about 2^-50 of the coordinates" does not hold. The query is
sqrt of a squared distance evaluated by the quadratic form (`DistPoint3Triangle3.DistanceSqr`: c = |V0 - P|^2 and
terms that cancel to d^2), so its absolute error is about 2^-53 E^2 / d for a point at distance d from a triangle
with |V0 - P| about E, and up to about 1.5e-8 E for d below 1e-8 E, whatever the coordinates' offset. Measured, 2000
random triangles of size about 1, a point at height h over the interior, float query against the certified exact
distance (offsets 0, 1e3, 5.7e6 give the same picture):

| h | largest abs(float - exact) | float distances that are exactly 0 | 2^-50 of the coordinates (offset 0) |
|---|---|---|---|
| 1e-1 | 1.2e-15 | 0 | 8.9e-16 |
| 1e-3 | 9.8e-14 | 0 | |
| 1e-6 | 1.0e-10 | 0 | |
| 1e-9 | 1.4e-8 (below by up to the whole distance) | 1395 of 2000 | |
| 1e-12 | 1.1e-8 | 1390 of 2000 | |

So for meshes closer than about 1e-6 of their triangle size the U0 upper bound can be off by 1e-4 of the distance and
more, and below 1e-8 the float distances are noise. U0 is the owner's model; what has to change is the sentence (API
doc, c3, section 4): "about 2^-53 E^2 / d, at most about 2^-26 E (E: the size of the nearest triangle of the other
mesh)", and #742 should know that this, not 2^-50 of the coordinates, is what it removes. The certified lower bound is
not affected (it is the exact re-evaluation).

## Holds as stated (checked)

- c1 otherwise: TwoSum is exact for all doubles without overflow, the halving of r is exact unless |r| < 2^-1021
  (allowance added, the additions of the allowance are exact because the summand is subnormal or 0 there), L1 >= L2,
  `Up` covers the two additions of e1.
- c2: |m - m*| <= err + (off(a) + off(b)) / 2; MidOff rounds twice (the two additions; the halving is exact unless
  subnormal, and then the first addition was exact), `Up` covers it. Probe: 400,000 triples, exact comparison, no
  failure for inputs the code can produce (0 or at least 2^-1073).
- c3: the exact children cover the exact parent (midpoints of the stand-in points), d(x*, B) <= u(T) + max off,
  `Up(u + o)`.
- c4: cw = Down(Down(w - off)) and cv are <= the exact one-sided distance; min with Lower; the symmetric maximum; the
  pruning (Upper >= Lower by construction, so pruned triangles stay below the reported upper bound).
- Section 3: the box bound is rounded outward at every step; pass 2 compares exactly; the squared distance (face
  formula, region test, the three segments, degenerate triangles) is exact; ToDouble (truncated quotient, (double) of a
  65-bit BigInteger truncates to 64 bits and rounds once, ScaleB rounds once more only for a subnormal result) is
  covered by two Down steps, sqrt by one. Probe: 3000 point / mesh cases (1 to 29 triangles, scales 2^-1000 to 2^300,
  lattice and degenerate cases, a point on a vertex): the bound squared is <= the exact squared distance to 45 dyadic
  sample points of every triangle, in integers, in every case.
- (a): Sterbenz for both branches, lo and hi over both meshes, the size after the shift.
- (b): sound (Upper >= stuck); terminates (a cycle of children with rounded corners raises off by err per
  generation until off > tol).

## Notes (wording, no bound affected)

N1. `Up`: the constant 1 + 4.5e-16 is the double 1 + 2^-51, so the product rounded to nearest is at least
x (1 + 3.33e-16), not 3.4e-16; still above 2.3e-16.
N2. c2 says "two roundings, a sum and a halving": they are the two sums; the halving rounds only where the first sum
was exact.
N3. `MidOff(2^-1074, 0, 0)` returns 0 although the exact value is 2^-1075 (0.5 * 2^-1074 rounds to 0 and `o > 0` is
false). Not reachable: Midpoint and MidOff return 0 or at least 2^-1073. A comment, or `Up` for any nonzero input.
N4. An overflowing sum in Midpoint gives r = NaN and err = 0. Not reachable in the plain path (coordinates below
about 2^90) or on the copies (below 4); only with AsGivenForTests.
N5. The copies of the scale-down path (extent from 2^65) round coordinates below 2^(e - 1022) (by at most 2^-1075 of
the copy's unit). The certified lower bound still holds, because every certified value is at least about 2^-592 (in
the copy's unit) below the copy's exact distance (the Down steps) or is 0, and the rounding moves the distance by less
than 2^-1073; the note does not say so (the comment at pow2 says "do not decide a bound").
