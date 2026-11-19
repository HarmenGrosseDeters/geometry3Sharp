# WrapPassageProbe: can MeshAlphaWrap keep a passage open? (study, branch runs/wrap-passage, no PR)

Owner go 2026-11-19. Probe only; `geometry3Sharp_netstandard.csproj` gets an InternalsVisibleTo for it (study branch only).
Data: `data/main.jsonl` (180 wraps: 3 parts x 3 radii x 2 triangulations x 10 alphas), `data/fine.jsonl` (k = 1.01..1.04),
`data/off10.jsonl` (alpha / offset = 10 instead of 30, R = 3), `data/analysis_main.txt` (every row). Run:
`dotnet run -c Release --project benchmarks/WrapPassageProbe -- --out x.jsonl`, then `analyze.py x.jsonl [--rows]`.
Parts: block of side 36 minus a capsule chain around the pin axis (marching cubes of the SDF): cross bore, angled channel
(bend 63 degrees, two legs meeting inside), blind hole (pin ends 1 R above the rounded floor). R = 1.5, 3, 6.
Triangulations: grid h = R/6 aligned (T0), h = R/4.5 rotated by 23 degrees and shifted (T1). Input 13k to 264k triangles.

## 1. A priori bound (derived here, checked by the data; not in the paper)

Setting: pin axis A (any polyline), r = dist(A, input), offset d, alpha a. Every wrap vertex is within d_max of the input
(d_max = Offset; 2 Offset with PullPinchPoints), the facets of the carved wrap (no manifold step) all have Phi(f) <= a at
termination (every gate with Phi > a is carved or refined, and rejected points = 0).

Lemma (barycentric identity). For q in facet f = (p1,p2,p3), q = sum l_i p_i, and any ball with centre m and radius R through
the p_i: R^2 = |q-m|^2 + sum l_i |p_i-q|^2 >= min_i |p_i-q|^2. And for y in f with weights mu_i: sum mu_i |p_i-y|^2 = R^2 - |y-m|^2 <= R^2.
Proof of both: expand sum l_i |p_i-m|^2 around q.

1. Every q on A has |p_i - q| >= dist(q,input) - dist(p_i,input) >= r - d_max =: rho (triangle inequality).
2. So an interface facet that contains an axis point has Phi >= R >= rho. If rho > a, no interface facet of the carved wrap
   contains a point of A (it would be a gate with Phi > a). A is connected, so A lies entirely on one side of the carved wrap
   (all outside if one point, eg an end beyond the part, is outside). Pocket removal only fills outside cells not connected to
   the box; A is a connected outside set that reaches the outside end, so it is kept.
3. Clearance: for facets with Phi <= a and the minimal ball (R = Phi <= a), dist(q, f)^2 = sum mu_i|p_i-q|^2 - sum mu_i|p_i-y|^2
   >= rho^2 - a^2. **dist(A, carved wrap) >= sqrt((r - d)^2 - a^2)**, so a pin of radius p passes if a <= sqrt((r-d)^2 - p^2);
   open passage (p -> 0): **a < r - d** (the "r > alpha + offset" of the task).

Status: a proof for the carving loop, independent of sampling density, refinement rounds, rule 1 / 2 and box. It is NOT a
guarantee of the final result as shipped: it needs (a) rejected_points = 0 and no PointLimitReached / box corners
(Counts), (b) the manifold step not moving a cell at the axis (it only acts at pinches; never happened here, but not excluded:
a cell spanning a bore between two pinched wall vertices could in principle be moved), (c) d_max honest (Pull: 2 Offset;
projection_fallbacks and box-corner pinch points have no bound), (d) the coarsening (MaxEstimatedTriangles) changes alpha
and offset: use UsedAlpha / UsedOffset, (e) simplification: the result is certified within 0.5 Offset of the raw wrap
(one-sided, result to wrap), so clearance >= raw clearance - 0.5 Offset (not a guarantee of topology; the check below decides).
The converse (closing when r - d < a) is the dense-sampling heuristic: a facet across a tube has Phi about r - d, so it is
not carved when a exceeds that; the data below shows it is tight to 1 to 2 %.

## 2. A posteriori check (implemented in the probe: `Clearance`, `Measure`)

(i) Clearance of the axis from the wrap triangles, certified: dist-to-mesh is 1-Lipschitz, so branch and bound on the polyline
gives [lo, hi] with hi - lo <= 1e-3 R (interval lower bound (f(a)+f(b)-L)/2); pass iff lo >= p. Cost 1e3 to 1e4 nearest-triangle
queries (tens of ms on 3k triangles). Rounding: each query is a double nearest-point distance; a margin of a few ulps of the
mesh size makes it certified up to double rounding; a fully exact variant needs a rational segment-triangle comparison
(not built). (ii) The axis is outside: the generalized winding number at one axis point (exact solid-angle sum, tree-accelerated,
|w| < 0.5) plus (i) (no crossing, so the point stays on one side). The probe tests both ends; for blind holes the pocket end
matters (a sealed pocket gives w = 1).
A connectivity test of the two ends is too weak: the outside of a wrap is one connected region, so both ends are connected
around the part even when the bore is plugged; restricted to a tube neighbourhood it accepts a leak of any width (a pinhole,
a membrane touching the axis: in the data at k >= 1.05 both end windings are 0 and the clearance is 0.00 to 0.003 R). Only the
clearance tells whether a pin of radius p fits.

## 3. Measurements (alpha = k * alpha0, alpha0 = r / (1 + 1/30), offset = alpha / 30; "open" = clearance > 1 % r and both ends outside)

| k = alpha / alpha0 | 0.5 | 0.8 | 0.95 | 1.0 | 1.01 | 1.02 | 1.05 and above |
|---|---|---|---|---|---|---|---|
| open, raw (18 configs) | 18 | 18 | 18 | 18 | 6 of 18 | 0 of 18 | 0 of 18 (to k = 3) |
| open, simplified | same | same | same | same | same | same | same |
| median clearance / r | 0.870 | 0.639 | 0.425 | 0.262 | | | 0 |
| a priori bound / r (median) | 0.857 | 0.591 | 0.307 | 0 | | | 0 |

- **Theorem check**: 68 rows with a positive bound, 0 violations; measured / bound min 1.009, median 1.151 (the bound is
  nearly attained: the facets over the bore are chords of circumradius about alpha).
- **Bound tightness**: all 18 configs are open at k = 1.0 (measured clearance 0.13 to 0.37 r, the marching-cubes bore is polygonal,
  the certified r is its inscribed radius), 6 of 18 at 1.01, none from 1.02 up. So r - offset < alpha closes the passage within
  2 %: the bound is tight, the margin to spend is about 2 %.
- **Monotone**: no config passes after a fail (0 of 18 raw, 0 of 18 simplified; 0 of 6 at offset ratio 10). Pass/fail in alpha is
  a step at k about 1.01, no recovery at larger alpha (consistent with the bound being necessary in the dense regime).
- Offset ratio 10 (R = 3): the same picture, step between k = 1.0 and 1.02 (alpha0 recomputed with the larger offset).
- **Simplification**: the clearance is identical to the raw wrap in all 72 open rows (difference 0.0): the simplifier does not
  touch the big facets over the bore. Triangles 7.8k -> 3.1k at k = 0.5, 0.9k -> 0.6k at k = 3 (median); simplify 0.4 to 0.8 s.
- Size and time (median, parts of 6k to 264k input triangles, 4 cores): raw 7.8k triangles / 0.3 s at k = 0.5, 3.1k / 0.2 s at k = 1,
  0.9k / 0.1 s at k = 3. All 180 wraps Success, all simplifications certified.
- Not covered: manifold-step interaction (no violation seen), parts where the pin passes near other geometry, thin walls,
  open input (soups), PullPinchPoints, MaxEstimatedTriangles coarsening.
