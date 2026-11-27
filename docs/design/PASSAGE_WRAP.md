# PassageWrap: an alpha wrap driven by "this passage must stay open"

`gs.PassageWrap` (mesh_ops/PassageWrap.cs, opt-in, additive; `MeshAlphaWrap` is unchanged). The caller gives test pins (a polyline
axis and a radius) and optionally a triangle budget. `Compute()` takes the coarsest alpha that the bound below allows, wraps once,
simplifies, and measures the result: a pin counts as passed only if the measurement says so. The study behind it (180 wraps, the
tightness and monotonicity data) is on the data branch `runs/wrap-passage` (commit fbb7d152,
`benchmarks/WrapPassageProbe/README.md`); this note is the proof and its preconditions, for the reviewer to check.

## 1. Setting and what the carving gives

Notation: the input I (any triangle set), the offset d > 0 and the alpha a of `MeshAlphaWrap`; Phi(f) of a Delaunay facet f is the
smallest radius of an empty ball through its three vertices (the paper's Definition 2, `TraverseByEmptyBall`: a gate is
traversable if Phi(f) > a).

- **(P1)** Every vertex p of the wrap is within d of the input: dist(p, I) <= d. The vertices of the carving are on the offset surface
  (rule 1 and rule 2 insert on it, up to rounding; box corners are not on the result, see (P4)). With `PullPinchPoints` the pulled
  points are up to 2d from the input: use d_max = 2d.
- **(P2)** At the end of the carving (and of every refinement that continues it) every facet between an inside and an outside cell has
  Phi(f) <= a: a gate with Phi(f) > a is popped and carved, or replaced by a Steiner point (rule 1 or 2); the loop ends when none is left.
  (Paper notes section 2; `MeshAlphaWrap.Compute` loop.)
- **(P3)** `MeshAlphaWrap.AlphaField` is not used (uniform alpha). (The proof needs only some ball through the three vertices with radius <= a: the smallest empty ball has R = Phi(f) <= a, and with `TraverseByEmptyBall` off, where gates use the circumradius, the circumball has R <= a: the bound holds for both.) The retry with another box (`RetryWithOtherBox`) starts a new run with fresh counts; the premises are those of the run that gave the result.
- **(P4)** No rejected Steiner point (`Counts["rejected_points"]` = 0: a rejected point leaves a gate with Phi > a on the surface), no
  `PointLimitReached`, no box corner pinch points (`CornerPinchPoints` = 0).

## 2. The identity

For a facet f with vertices p1, p2, p3 and weights l_i >= 0, sum l_i = 1, a point q = sum l_i p_i of f, and **any** ball with centre m and
radius R whose boundary contains p1, p2, p3 (|p_i - m| = R):

    sum l_i |p_i - m|^2 = |q - m|^2 + sum l_i |p_i - q|^2            (*)

Proof: |p_i - m|^2 = |p_i - q|^2 + 2 (p_i - q).(q - m) + |q - m|^2; multiply by l_i and sum; the middle term is 2 (sum l_i p_i - q).(q - m) = 0.
The left side of (*) is R^2. Hence

    R^2 >= sum l_i |p_i - q|^2 >= min_i |p_i - q|^2.                  (1)

Applied with q replaced by any point y of f (weights mu_i) and rearranged, for the same ball:

    sum mu_i |p_i - y|^2 = R^2 - |y - m|^2 <= R^2.                    (2)

## 3. The bound

Let A be a pin axis (any set), r = dist(A, I), and suppose every point q of A has dist(q, I) >= r. For q in A and a vertex p of the wrap, the
triangle inequality gives dist(q, I) <= |q - p| + dist(p, I), so with (P1)

    |p - q| >= r - d.                                                 (3)

**Claim 1 (the passage cannot be sealed).** If r - d > a, no interface facet f contains a point q of A. Proof: if q in f, by (1) and (3)
every ball through the vertices of f, in particular the smallest empty one, has R >= r - d > a, so Phi(f) > a, contradicting (P2). So A
meets no interface facet of the carved surface: A, connected, lies on one side of it, and the side is decided by one point (the end
beyond the part, or the winding number, section 5). Pocket removal turns into the inside only outside cells that are not connected to
the box: the cells along A are connected to each other and to the outside end, so they stay.

**Claim 2 (clearance).** Let f be an interface facet, y in f with weights mu_i, q in A, and the ball of Phi(f) <= a (P2). The identity (*) holds for any centre m, not only the centre of a ball (the proof used nothing else); with the barycentre y (weights mu_i) and the centre q it reads sum mu_i |p_i - q|^2 = |y - q|^2 + sum mu_i |p_i - y|^2. With (3) and (2):

    |y - q|^2 = sum mu_i |p_i - q|^2 - sum mu_i |p_i - y|^2 >= (r - d)^2 - Phi(f)^2 >= (r - d)^2 - a^2.

So **dist(A, carved wrap) >= sqrt((r - d)^2 - a^2)**. A pin of radius p passes if sqrt((r - d)^2 - a^2) >= p, that is

    a^2 + p^2 <= (r - d)^2.                                           (4)

With d = a k (k = 1 / OffsetRatio; 2 / OffsetRatio with pulled points) (4) is a quadratic in a with the positive root
`PassageWrap.MaxAlpha` = r (1 - u^2) / (k + sqrt(k^2 + (1 - k^2)(1 - u^2))) with u = p / r and 1 - u = (r - p) / r (algebraically equal to (-r k + sqrt(r^2 k^2 + (1 - k^2)(r^2 - p^2))) / (1 - k^2), zero if r <= p, without r^2/p^2 overflow/underflow or cancellation near p = r). The open passage is p -> 0: a (1 + k) < r.
Nothing in the proof uses the sampling density, the refinement rounds, rule 1 or 2, or the shape of the axis.

## 4. What the proof does not cover (the preconditions, and what the helper does about each)

| Not covered | Why | In `PassageWrap` |
|---|---|---|
| the manifold step (moving outside cells inside at pinches) | the moved cells' facets were never gates, so (P2) fails for them; a cell at the axis could be moved | the result is measured; clutter test with many pinches: no counterexample (6 seeds, with and without `PullPinchPoints`) |
| rejected points, `PointLimitReached`, box corner pinch points | (P4) | `BoundPremisesHeld` reports them; the status of a failed check is `PassageNotKept` |
| the simplification | a result within 0.5 offset of the raw wrap (one-sided, certified): clearance >= raw clearance - 0.5 offset, topology not argued | the simplified wrap is measured and kept only if it passes, else the raw wrap is returned |
| rounding of vertices to the offset surface, floating-point circumcenters | (P1) holds up to the bisection's 60 steps; Phi(f) is computed in doubles (a sliver cell can deviate) | alpha is 0.999 of the root; the check decides |
| budget coarsening (`MeshAlphaWrap.MaxEstimatedTriangles`) | scales alpha and offset: (4) must use the used values | the helper never coarsens: a budget only decides `BudgetExceeded` |
| `AlphaField` (P3) | local alpha is below the uniform one: the bound still holds with the smallest alpha, but not used here | not used |
| an input whose clearance r is not known to the caller | the helper measures it: certified lower bound (below) | `PinResults[i].InputClearance` |

## 5. The a posteriori check

`PassageWrap.PolylineClearance`: the distance of a polyline to a mesh, [lo, hi]. The distance to a mesh is 1-Lipschitz along the axis, so an
interval of length L with end values fa, fb has min >= (fa + fb - L) / 2 (the two cones meet, outward-rounded downward), and intervals are split best-first until the
smallest lower bound is within the tolerance of the smallest value found (hi) or an interval reaches adjacent representable doubles (stalled intervals keep their lower bound). Tolerance: 1e-3 of the pin radius, at least 1e-4 of the
size of mesh and axis. lo is certified up to the double rounding of each nearest-triangle query, covered by a margin of max(1e-12 * diag, 64 * MathUtil.Epsilon * coordMax).
A pin passes if lo >= radius and every vertex of the axis has |winding number| < 0.5 in the wrap (`DMeshAABBTree3.WindingNumber`, exact
solid angle sum); with lo > 0 the axis does not cross the surface, so one point decides, all vertices are tested. The winding test is not
redundant: a pin inside the material is far from the wrap's triangles too (`PinInsideTheMaterial_IsNeverPassed`). A connectivity
test of the two ends is too weak: the outside of a wrap is one connected region, so both ends are connected around a plugged bore.

## 6. Measured (study branch `runs/wrap-passage`, 180 wraps)

Cross bore, angled channel, blind hole; R 1.5, 3, 6; two triangulations; alpha = k * r / (1 + 1/30), offset alpha / 30. Open passage for
k <= 1.0 in 18 of 18 configurations, 6 of 18 at 1.01, 0 of 18 from 1.02 to 3.0: the bound is tight to about 2 %. No violation of
Claim 2 in 68 rows (measured / bound: min 1.009, median 1.15). Pass/fail is monotone in alpha (no pass after a fail, raw and simplified).
Simplification: clearance unchanged in all open rows. Offset ratio 10: the same step. Cost: one wrap (0.1 to 0.3 s on these parts) plus
the check (tens of ms) plus the simplification.

## 7. Algorithm

1. Validate; for every pin, the certified input clearance r_i (lo of `PolylineClearance` on the input's triangle tree);
   r_i <= p_i: `PinBlockedByInput`.
2. alpha = 0.999 min_i MaxAlpha(r_i, p_i), at most the input's bounding box diagonal and `MaxAlphaLimit` (a finer wrap on request).
3. Budget: the size estimated at that alpha (`EstimateDensity` * area, open components twice, as the wrap's own budget) above
   `MaxTriangles`: `BudgetExceeded`, nothing wrapped (never coarsen past the pins).
4. Wrap (offset alpha / `OffsetRatio`), measure every pin; if all pass, simplify (`MeshRobustRepair.SimplifyWithin`, within half an offset,
   the input kept clear) and keep it if it passes too.
5. A failed check: alpha * `FallbackFactor` (0.8), at most `MaxFallbackSteps` (3) times; then `PassageNotKept` with the last wrap and
   its measured intervals. A pass is never claimed without the check.
