# Arrangement: decide the proper candidate pairs from signs (Devillers-Guigue prefilter)

## 1. Goal and evidence

`ExactMeshArrangement.intersect(ti, ui)` decides every candidate pair of the pair search. For a pair whose triangles
straddle each other's plane it constructs the points where the edges cross the other plane (implicit points), takes
their extremes along the line of the two planes and compares them exactly. A probe on the combined L1 + #409 + #412
code (6 slow Thingi10K cohort models × 3 operations, 18.9M candidate pairs) found:

- every candidate pair is between the two meshes and shares no corner (the pair search already drops pairs separated
  by a plane, and pairs that only touch);
- 93.2 % of the candidates, 92.8 % of the phase's time, do not intersect: the points are constructed only to find
  that the two segments on the line do not overlap.

The phase is 7.5-8.5 s of each 519511 operation, 1.8-1.9 s of 1517923, 1.0-1.6 s of 668056/7.

## 2. The rule

Let `T = (t0, t1, t2)` and `U = (u0, u1, u2)` be the two input triangles in the arrangement's (double, exact)
coordinates, `[a, b, c, d] := ExactPredicates.Orient3d(a, b, c, d)`, the sign of `((b - a) x (c - a)) . (d - a)`,
`sT[k] = [u0, u1, u2, t_k]` and `sU[k] = [t0, t1, t2, u_k]` (the code computes these already, for the plane
separation it tests first).

The pair is **proper** if all six signs are non-zero and neither triple has one sign only (each triangle has vertices
strictly on both sides of the other's plane). Only proper pairs are decided by the prefilter; every pair with a zero
sign, and the coplanar pairs, take the construction as before.

For a proper pair: let `p1` be the vertex of `T` whose sign differs from the other two, `q1, r1` the next ones in cyclic
order (`p1 = t_i, q1 = t_{i+1}, r1 = t_{i+2}`), and `p2, q2, r2` likewise for `U`. Swap `q2, r2` if `sT[i] < 0`, swap
`q1, r1` if `sU[j] < 0`. The prefilter says *apart* iff

    [p1, q1, p2, q2] > 0  or  [p1, r1, r2, p2] > 0.

If apart, `intersect` returns (no points, nothing recorded). Otherwise it continues with the construction unchanged.

## 3. Lemma (equivalence)

Claim: for a proper pair, the prefilter says apart iff the construction finds `c > 0` (and so returns without
recording anything). Hence the arrangement, its counts (`IntersectingPairs`) and the Boolean results are unchanged.

Proof.

1. *The geometry.* Since `T` straddles `πU` and `U` straddles `πT`, the planes are not parallel and meet in a line
   `L`. `σT = T ∩ πU` is a segment on `L`; its ends are where the edges `p1q1` and `p1r1` cross `πU` (the edges at the
   vertex alone on its side; `q1r1` does not cross, since `q1, r1` are on one side). Likewise `σU = U ∩ πT` lies on `L`
   with its ends on `p2q2` and `p2r2`. `T ∩ U ⊆ πU ∩ πT = L`, so `T ∩ U = σT ∩ σU`.
2. *Today's construction.* In the proper case `plane_points(T)` yields exactly the two ends of `σT` (no vertex has sign
   0), `plane_points(U)` those of `σU`. `lo` is the larger of the two lower ends, `hi` the smaller of the two upper
   ends along `d = nT x nU` (a direction of `L`), all compared exactly. `c = compare(lo, hi) > 0` iff the segments
   are disjoint, `c = 0` iff they share exactly one point, `c < 0` iff they overlap in a segment.
3. *The predicates* (Devillers and Guigue 2002, section 3). After the swaps, `p1` lies on the positive side of the
   oriented plane `(p2, q2, r2)` and `p2` on the positive side of `(p1, q1, r1)`; a swap only reverses a triangle's
   orientation, so `σT`, `σU` and `L` are unchanged. Along `d = nT x nU` (`nT = (q1 - p1) x (r1 - p1)`,
   `nU = (q2 - p2) x (r2 - p2)`) write `i`, `j` for the ends of `σT` on `p1r1`, `p1q1` and `l`, `k` for the ends of
   `σU` on `p2q2`, `p2r2`. Then, checked by Z3 (section 5):
   - `i <= j` and `l <= k` (so `σT = [i, j]`, `σU = [l, k]`);
   - `[p1, q1, p2, q2] <= 0` iff `l <= j`;
   - `[p1, r1, r2, p2] <= 0` iff `i <= k`.
   The segments share a point iff `l <= j` and `i <= k`, so iff both predicates are `<= 0`; they are disjoint iff one
   is `> 0`. A predicate 0 is a coinciding end, a common point, which the prefilter leaves to the construction
   (`c = 0`, a point).
4. Together: apart (prefilter) ⇔ σT ∩ σU = ∅ ⇔ `c > 0` (construction). ∎

*Bridge for the Z3 check.* An affine map with positive determinant keeps every `Orient3d` sign, maps segments to
segments and keeps their order along the line (with the direction mapped accordingly, `d` transforms by the cofactor
matrix, which keeps the orientation for a positive determinant). One such map takes `(p2, q2, r2)` to
`(0,0,0), (1,0,0), (0,1,0)` and `p1` to a point with `z > 0` (`p1` is on the positive side, as normalized). So
without loss of generality `U` is that triangle and `T`'s nine coordinates are free; Z3 checks the claims for all of
them.

The sign convention is the paper's: `[a, b, c, d]` positive when `d` is on the side the normal of the
counter-clockwise triangle `(a, b, c)` points to, which is `ExactPredicates.Orient3d`. This is not taken on trust:
`TriTriPrefilterTests.ProperPairs_SameAsAnExactTest` compares the prefilter with the independent exact test
`ExactTriangleIntersection.Intersects` on 20000 proper pairs each of integer triangles in `[0, 3]`, `[0, 5]` (many
segments that touch at an end) and `[0, 1000]`, in every cyclic order and both orientations of the triangles; with
the swaps reversed, the orders reversed, a predicate dropped or `>= 0` instead of `> 0` it fails.

## 4. Evaluation in doubles

The prefilter evaluates two more `Orient3d` on the input doubles; the six side signs are computed before it anyway.
`Orient3d` is exact (filtered, exact fallback), so the prefilter's decision is exact. No constructed point, no
rounding, no new error bound, nothing for the bound review; the combinatorial lemma above is checked by Z3. Floating
point appears nowhere in the decision.

## 5. Verification

- Z3 (`tools/Z3Pilot/tritri_prefilter.py`, run by `test_tritri_prefilter.py` in the Z3 pilot workflow), over the reals
  (the code's predicates are exact, so no rounding is modelled), with `U` normalized as in the bridge above and the
  segments built from their definitions (edge parameters where an edge crosses the other plane, positions along `d`),
  not from the predicates: the two orders, the two predicate equivalences, and the lemma in both directions (apart
  but overlapping; not apart but disjoint) are UNSAT; the assumptions, both decisions and touching are SAT (not
  vacuous); four mutants (`>= 0`, either predicate alone, the opposite sign) are SAT. The test also checks that the
  code's normalization and predicates are the ones modelled (a source contract on `apart_on_the_line`).

- Check mode (`ExactMeshArrangement.CheckPrefilterOnThisThread`, read on the calling thread when the intersections
  are searched): every proper pair is decided by both, and a difference throws.
- `TriTriPrefilterTests`: the comparison with an independent exact test above; Booleans in check mode (turned grids,
  spheres, axis-aligned grids with zero signs, a box touching another's corner) with results equal to the run without
  the check and `PrefilterRejected > 0`; `Similarity.All()` (2^k bit for bit, the same number rejected); serial =
  parallel; `Compute()` twice.
- The early return is an equivalent mutant (the construction then decides the same, slower): marked for Stryker.

## 6. Prior art

- O. Devillers, P. Guigue, *Faster Triangle-Triangle Intersection Tests*, INRIA Research Report RR-4488, 2002 (also
  J. Graphics Tools 8(1), 2003): the orientation-only test, implemented here from the paper.
- T. Möller, *A Fast Triangle-Triangle Intersection Test*, J. Graphics Tools 2(2), 1997: the interval test along the
  line with computed intervals, which today's construction is the exact form of.
- Cherchi et al. 2022 and Lévy 2024 classify pairs with orientation predicates before constructing points as well.
