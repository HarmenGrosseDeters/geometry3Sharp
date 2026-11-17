# Alpha wrap: a quality-aware manifold step (#919 follow-up)

Roadmap: [alpha-wrap-and-repair.md](../roadmap/alpha-wrap-and-repair.md), "Open for the future", the item after "#919 closed
(2026-11-15)". Status: design note, 2026-11-17; the recommendation waits for the coordinator's go before the build.

## 1. The defect

`MeshAlphaWrap` promises the angle bound asin(min(δ, α − δ) / 2α) on every result triangle with a circumradius of at most
α (class doc). The triangles above it come from the manifold step (`make_manifold`): the cells it moves inside were
carved through facets larger than α, so the facets it puts on the result have no angle bound and no distance bound.
`RefineNonManifold` puts a Steiner point into such a cell first, but only where one keeps the separation
min(δ, α − δ) **and** lies on the offset surface. Cells it cannot refine are counted in `pinch_skipped` and left to the
manifold step.

Probe on soup 42 at α = diag / 20, δ = α / 10, on the rounds path (the instance `Rounds_DefaultPath_Soup42_QualityPinned`
pins). There are 18 cells flipped. 12 of them are forced: they belong to outside components cut off around a vertex or
edge. The other 6 are single-cell choices, from about 10 candidates each. All 4 triangles below the bound (2.866°;
minimum 2.12°) lie on two cells of the single-cell choices. They have a circumradius of 1.07 to 1.50 α and a shortest
edge of 1.11 δ, so the separation holds and only R > α breaks the bound. The cells are about 7 δ outside the input.
Their circumcenters are 12.6 to 16 δ out, and their circumradii are 1.2 to 1.5 α. The offset projection of the
circumcenter lies only about 0.5 δ inside the circumsphere, not 1 δ. The dual edges of the facets do not cross the
offset surface. So no point that keeps both the separation and the on-surface placement exists for these cells.

## 2. Candidates

All candidates leave the last manifold step as it is, so the result stays closed, oriented, 2-manifold and free of
self-intersections, and the input stays strictly inside. The options differ in which of the derived bounds they keep.
Measured (`DebugProbe` patch, not pushed) on 480 wraps: random soups 1..60, four (α, δ) pairs (diag/20 δ=α/10, diag/40 α/10, diag/60 α/10, diag/20 α/30),
both the serial and the rounds carving. All four variants had 0 failures, 0 unclean results and 0 separation
violations. Time is the sum over the 480 wraps, in one thread of the test run.

| variant | triangles | wraps with triangles below the bound (count) | min / max angle | manifold flips | max vertex distance / δ | max facet centroid distance / (δ + α) | time |
|---|---:|---:|---|---:|---:|---:|---:|
| default (master) | 1,048,346 | 43 (78) | 0.46° / 175.2° | 7465 | 1.00 | 1.194 | 20.7 s |
| **P: pulled pinch point** | 1,054,292 (+0.57 %) | **0 (0)** | 1.06° / 177.0° | 20 | 3.55 | 0.987 | 20.3 s |
| **P2: pulled, at most 2 δ out** | 1,054,066 (+0.55 %) | **0 (0)** | 1.06° / 173.5° | 217 | 2.00 | 0.980 | 20.5 s |
| P + A | 1,054,228 | 0 (0) | 1.06° / 174.6° | 30 | 4.49 | 0.980 | 20.4 s |

(The 1.06° minimum comes from α/δ = 30, where the bound is 0.955°.)

A, C and D were measured only on the first three (α, δ) pairs (360 wraps). The table lists the 22 wraps of those that
have a triangle below the bound on master:

| variant | of the 22 wraps below the bound on master | triangles | flips | max vertex distance / δ |
|---|---|---|---|---:|
| A: quality-aware flip choice | 3 fixed (soups 29 twice, 42), 1 worse (51), 18 unchanged | ±1 % | about the same | 1.00 |
| C: relaxed separation, depth 0.5 δ | 14 fixed, 4 worse (soup 5 at diag/60: 1 → 20 below, 1.34°); separation down to 0.50 δ | +0-3 % | mostly fewer | 1.00 |
| C: relaxed separation, depth 0.25 δ | worse (down to 0.99°); separation down to 0.30 δ | +0-3 % | mostly 0 | 1.00 |
| D: cell circumcenter as pinch point | 22 fixed | −0-5 % | 0 | up to 19.7 |
| P: pulled pinch point | 22 fixed | +0-3 % | 0 | up to 2.0 |

- **A, quality-aware choice of the flips.** In the single-cell branch of `fix_star`, a candidate whose exposed facets
  keep the bound wins over one that does not, before the "joins components" and volume keys. This is cheap and local,
  and it fixes soup 42. It cannot touch the forced flips (the cut-off components), which are most of them, and it
  guarantees nothing.
- **B, a quality pass on the facets of flipped cells.** Steiner points for the poor facets after the manifold step,
  then carving and the manifold step again. With the current point rules this is the same set of candidates the pinch
  refinement already tried (§1: none qualifies), so it adds nothing unless the rules change. This makes it P.
- **C, a relaxed pinch separation.** It refines more spots, but it gives up the class doc's separation (measured down
  to 0.30 δ), so the angle bound of the small triangles drops (asin(δ / 4α) at a factor of 0.5). It is often worse.
  Rejected.
- **D, the circumcenter itself** (Delaunay refinement off the surface: Chew 1993, Shewchuk 1998). It removes every flip
  but puts result vertices up to 19.7 δ (1.8 (δ + α)) out. Rejected.
- **P, a pulled pinch point (recommended, as P2).** This is a third candidate in `pinch_point`, after the two strict
  ones. Take the segment from the cell's circumcenter c to its offset projection q. When q lies less than s =
  min(δ, α − δ) deep inside the circumsphere (radius r), use x = c + (q − c)(r − s) / |q − c| instead: the point on
  that segment exactly s inside the sphere. With a cap, x is used only if d(x) ≤ 2δ.
  - Separation is kept. The sphere is empty, so every vertex is at least r from c and at least s from x. Insertion is
    exact (`InCircumsphere(c, x) > 0`).
  - The input stays outside the offset. Along the segment towards the closest point, the distance falls linearly, so
    d(x) = d(c) − (r − s) > d(q) = δ.
  - So the vertices lie in [δ, 2δ] from the input, not on the offset surface: this relaxes one documented promise
    ("the vertices lie on the offset surface"), and only for this option.
  - The spots that were left to the manifold step now become manifold by carving. Their facets are gates, so they keep
    the bound of the class doc (circumradius at most α, gates by Φ(f)). The facets the manifold step flipped were far
    out, so the result also comes closer to the input: the facet centroid maximum goes from 1.19 to 0.98 (δ + α).
  - Nothing is guaranteed for the flips that remain (217 over the 480 wraps), but none was below the bound in the
    sweep.
  - Cost: +0.5 % triangles, no measurable time.
  - The pulled point is the off-surface analogue of Üngör's off-centers (a point inside the circumball, closer than the
    center, chosen to keep a separation; *Off-centers*, LATIN 2004 / CGTA 2009). Refining where the restricted surface
    is not a disk is from Boissonnat and Oudot 2005 and from Cheng, Dey and Ramos 2007 (refinement at vertices that fail
    the topological disk condition). Portaneri et al. 2022 (§8) only name pinches as open: "forbid pinching", which
    gives larger volumes, or "ε-deflate", without a proof. A literature search found no published fix of the pinches of
    the alpha wrap.

## 3. Recommendation

Build P2 as an opt-in option, `MeshAlphaWrap.PullPinchPoints` (default off; the 2δ cap is a constant in the doc).
Leave out A: it adds nothing on top of P (P + A has a larger vertex distance and the same angles). Tests:
- soup 42 at α = diag / 20 with no triangle below the bound (fails on the old code: 4 below);
- vertices in [δ, 2δ];
- the separation bound;
- `Similarity.All()` invariance;
- reuse;
- MaxThreads 1 = N.
The 800-run corpus gate compares default and option (angles, clean, separation, triangle counts, time, vertex and
band distances). Whether the option becomes the default is the owner's decision; it relaxes the vertex-on-offset
promise.
