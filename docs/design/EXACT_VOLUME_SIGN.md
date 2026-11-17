# Exact sign of a signed volume (RC3, 2026-11-17)

`ExactPredicates.VolumeSign(xyz, corners)` returns the sign of S = sum over the n triangles t of
d_t = det(a_t - o, b_t - o, c_t - o), o the first corner of the first triangle, exactly. For a closed, consistently
oriented surface, S is six times its enclosed volume. `MeshPostRepairCleanup.FindInvertedComponents` takes the orientation
of each closed component from it. `NotCleanReason`'s "inside-out components" check goes through that function. Before,
the orientation was the sign of a floating-point volume relative to the centre of the whole mesh. For a sliver far from that
centre (the triage's chain sliver: exact volume +3.2e-21, area 5e-4, about 0.5 from the centre), the rounding of the sum
exceeded the volume and the sign was noise. The volumes in doubles stay for the magnitudes (sample depth, open bodies);
only the sign changes, and only where the floating-point sign was wrong or the volume is exactly zero (not inside out).

## Filter and bound

u = 2^-53. Per triangle, the differences u = a - o, v = b - o, w = c - o are each rounded once, and the term is evaluated
as `Orient3dFilter` evaluates its determinant: d_t = w_x (u_y v_z - u_z v_y) + w_y (u_z v_x - u_x v_z) + w_z (u_x v_y - u_y v_x).
In the same form, P_t is the permanent (the absolute values of the same products).

1. **Per term.** |fl(d_t) - d_t| <= B P_t + u |fl(d_t)| + 4 * 2^-1075 * (|w_t|_1 + 1), B = `Orient3dBound` =
   (7 + 56u) u. Shewchuk's errboundA (1997, section 4.3) is a constant for the sign decision: it bounds the error of the
   evaluation up to its last operation, whose error (at most u |fl(d_t)|) the comparison absorbs. As an absolute bound of the
   term, that last rounding is added: each monomial such as w_x u_y v_z has 8 roundings to first order (3 differences, 2
   products, 1 subtraction, 2 additions) against the 7 of errboundA. The constant applies to this evaluation (the form of
   `Orient3dFilter`, differences rounded once from the common origin o; P_t the permanent computed in floating point). The
   last part covers subnormal products: an absolute error of at most 2^-1075 each, 2 |w| 2^-1075 from the 2x2 products and
   3 * 2^-1075 from the outer ones. `UnderflowUnit` = 2^-1066 covers the factor 4 with room 128.
2. **Sum.** The terms are summed recursively in a loop. |fl(sum x_i) - sum x_i| <= gamma_(n-1) sum |x_i|, with
   gamma_k = k u / (1 - k u) (Higham 2002, 4.2). Additions of doubles do not underflow inexactly.
3. **Computed bound.** The three accumulated sums are sums of non-negative numbers, each at most 1 / (1 - gamma_(n-1)) times
   its computed value: P = sum P_t, Q = sum (|w_t|_1 + 1), and A = sum |fl(d_t)|, the absolute terms of steps 1 and 2.
   With gamma = gamma_(n-1), the error of steps 1 and 2 is at most
   (B P + U Q + (gamma + u) A) / (1 - gamma), with U = `UnderflowUnit`.
   The code computes E = fl(fl(fl(B P) + fl(U Q)) + fl(g A)) * fl(1 + 3 g) with g the computed gamma_(n+2), itself within two
   roundings of (n+2) u / (1 - (n+2) u). That is at most six roundings on each path: B P, the two additions, the final
   product, and fl(3g) with the addition 1 + 3g, each relative at most u.
   - Underflow in the bound: the results fl(B P), fl(U Q), fl(g A) and the product may be subnormal, each losing at most
     2^-1075. U Q >= 2^-1066 n has a slack of at least 508 * 2^-1075 per term against the needed 4 * 2^-1075, which absorbs
     those losses.
   - For the P and Q parts it suffices that (1 + 3g)(1 - 8u) >= 1 + gamma + 2 gamma^2: the first-order margin is
     (3(n+2) - (n-1) - 8) u = (2n - 1) u >= u for n >= 1, and the second-order terms are below it while g < 1/4.
   - For the A part, g (1 + 3g)(1 - 7u) >= (gamma + u)(1 + gamma + 2 gamma^2): the first-order margin is
     ((n+2) - (n-1) - 1) u - 7u g = 2u - 7ug > u, and the second-order terms (3g^2 against gamma^2 + 2 gamma u, both of
     order n^2 u^2) are covered while g < 1/4.

   So |fl(S) - S| <= E. The review of 2026-11-17 found the first version of this step counting five roundings and errboundA
   as an absolute bound; the bound held by the slack of g, as derived here, and empirically (200 000 random open soups
   from 2^-1000 to 2^500 with subnormal coordinates, 3000 coplanar soups of 200 triangles: no disagreement with the
   integer sum).
4. **Decision.** If fl(S) > E then S > 0, and if -fl(S) > E then S < 0. Otherwise the sum is evaluated exactly: every
   coordinate as m * 2^e, scaled to the smallest exponent (`to_integers`), the differences and determinants in `BigInteger`.
   The same holds where fl(S) or E is not finite (an overflow), or where g >= 1/4 (about 2^50 triangles). A coordinate that
   is not finite gives 0, so that, as the comparison V < 0 of NaN, nothing is inside out.

**Scope of the change.** For a closed component that is consistently oriented, S does not depend on the origin, and the
new sign differs from the old one only where the old floating-point sign was wrong, or where S is exactly zero (now not
inside out). A closed component with inconsistently oriented triangles (DMesh3 allows it) has a sum that depends on the
origin, so moving the origin from the mesh's centre to the component's first vertex can change its flag. Neither value
means anything there, and `NotCleanReason` reports "inconsistent orientation" first. The sample depth of the nesting test
still uses the floating-point |V|, as before.

## Tightness and tests

`ExactVolumeSignTests` and `FindInvertedComponentsExactTests` (the latter through the public API only, with its own BigInteger oracle, so that it also runs against the library before the fix, where three of its four tests fail):
- the filter agrees with the integer sum on 1800 random slivers (four scales including 2^±300, three offsets up to 6e6) and
  300 thin 64-gon prisms a few ulps thick;
- an adversarial sum where the recursive summation loses 200 terms of just under half an ulp. Its floating-point sum is
  128 * 2^-53 and its exact sum is negative, while the bound is about 418 * 2^-53. Without the summation term, or with a
  quarter of it, the filter would decide wrongly; the test kills those mutants. Inherent slack: about a factor 2 for sums
  where the sign matters (gamma_n sum |x_i| against errors of at most u |s_k| per step, with |s_k| at most half of sum |x_i|
  where the terms cancel);
- an adversarial sum for the underflow term: 1000 terms of 2^-1076, each lost to 0, against one term of -100 * 2^-1074.
  Its floating-point sum is negative, its exact sum positive; without the underflow term the filter would decide wrongly;
- open triangle soups (where the origin matters) down to subnormal coordinates (2^-1060, 1e-320) and up to 2^500, and
  fat solids decided by the filter alone (`VolumeSignFilter`);
- the triage's chain sliver and a synthetic sliver whose old floating-point sign was negative are not inside out; the same
  sliver reversed is;
- a pillow of volume exactly 0 is not inside out;
- non-finite coordinates have no sign;
- invariance over `Similarity.All()`.

Mutants that survive and are equivalent in practice: g with n + 1 in place of n + 2 (one u of slack, which the tests are
not tight enough to see), and the factor below. The factor (1 + 3 g) is second order (about 1e-13 relative at n = 1000): a mutant (1 - 3 g) survives the tests and is
equivalent in practice (it is not in the proof: the factor stays).
