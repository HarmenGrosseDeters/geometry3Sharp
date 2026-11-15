# Adaptive alpha spike: a cavity-aware level predictor (measurement only)

Owner decision 2026-11-15: the spike of `docs/roadmap/alpha-wrap-and-repair.md` ("adaptive alpha spike"), measurement
only, no library code in a PR. Follows the upper-bound study (`aw3/adaptive/README.md` on runs/adaptive-alpha-study): same
50 meshes (normal 30, tail 20 of `benchmarks/MeshRepairBenchmark/data/sample800_2611`, Thingi10K, fetched at run time),
same levels (W0 at alpha0 = diag / 300, offset = diag / 3000; W_L at 2^L alpha0, L = 1..3), same oracle (level of a W0
triangle = largest L whose wraps all stay within tau of it, both ways), same grading (Lipschitz g = 0.5 over the triangle
adjacency), tau = 1 offset. Master 06c3f7d, 4 cores.

Go criterion (owner): predictor false area below about 1 % on the tail too, at least about 2x on the corpus sum, and the
guarantees kept. **Result: no-go.** It stays a study.

## Literature

Dai, Wang, Yan, "Feature-preserving shrink wrapping with adaptive alpha", CAGD 111 (2024) 102321,
doi:10.1016/j.cagd.2024.102321: **not accessible** (not reachable from here, and the owner has no copy); from search snippets
only. Its goal is fidelity, not size: an adaptive step "based on normal projection" to keep sharp features and detail,
compared with alpha wrapping on Thingi10K, ABC and GrabCAD. Not compared further.

## Step A: predictors against the oracle levels

All predictors are computed from the input alone (no W0). Input samples at spacing alpha0 / 2. For each sample and side the
shrinking ball (Ma, Bae, Choi, Kerber, Rousseau 2012: the largest empty ball tangent there, against the input). A side
counts as exterior if the ball center has a fast winding number below 0.5. The level of a W0 triangle is the minimum over
the samples within offset + alpha0 of its centroid, then graded (g = 0.5). False area: share of W0's area where the graded
predicted level is above the oracle level.

| predictor | normal 30: false area median / max | normal: size gain, sum | tail 20: false area median / max | tail: size gain, sum | cost / W0 time (median, normal / tail) |
|---|---|---:|---|---:|---:|
| oracle (graded, free) | 0 | 3.69x | 0 | 2.29x | - |
| ball: reach (r + offset >= 0.7 alpha0: the fine wrap may enter; level L if r + offset >= 1.3 alpha_L), exterior sides | 0 / 0 | 2.36x | 0 / **41.2 %** | 1.72x | 1.16 / 1.88 |
| ball, both sides (no exterior test) | 0 / 0 | 1.40x | 0 / 9.3 % | 1.10x | same |

The ball predictor fails on 6 of the 20 tail meshes (false area 41.2 % 224108, 22.6 % 51909, 7.4 % 111959, 6.2 % 222695,
1.0 % 222687, 0.03 % 78968; deviation there up to 66 offsets). Variants on these 6 meshes and 7 normal meshes
(`A2..A5*.jsonl`):

| variant | false area on the 6 tail meshes | size gain (6 tail / 7 normal, per mesh) | why |
|---|---|---|---|
| exterior test with \|winding number\| < 0.5 (orientation-free) | as ball | as ball | the winding number is right near the surface: exactly one exterior side per sample on 224108 (`WindingDiag.cs.txt`) |
| hybrid: samples within offset + tau of W3 are level 3, the ball test only for the rest | up to 41.4 % | as ball; 12-79 % of the tail samples still need the ball | W3 being close is no evidence that the finer wraps agree |
| visibility of the coarse wraps (level = largest L whose wrap is within offset + tau of the sample; W1-W3 or W2-W3) | 0 % on 51909, 39.5 % on 224108, up to 30.9 % elsewhere | 1.1-7.9x | the coarse wraps' distance says nothing about their shape between the samples |
| + gap sag (gaps narrower than alpha: depth alpha - sqrt(alpha^2 - g^2) of the alpha ball, level only if the depths agree within tau / 2) | 41.1 % on 224108 | as ball | not the mechanism |
| + thin parts and convex curvature from the interior ball (R = interior radius + offset; the sag R - sqrt(R^2 - min(alpha, R)^2) may grow by tau / 2) | **0** on all 13 | 1.00-1.02x (one 1.43x) | safe, but the interior radius is not a curvature radius: flat faces of thick parts are constrained too |
| + planarity at scale alpha_L (largest distance of the input samples within alpha_L from the tangent plane; level only if it grows by at most tau) | 0 on 5 of 6, 6.0 % on 51909 | 1.00-1.21x | safe enough, but the planarity at scale alpha_L is far above the wraps' real deviation, and it costs another 0.1-0.5 W0 |

What the tail needs is three mechanisms at once: cavity reach (where the fine wrap enters and the coarse one does not), the
sag over gaps, and thin parts / convex curvature (the coarse wrap's larger triangles cut across bars a few alpha0 thick:
224108, a lattice, W1 240k -> W2 12.5k triangles). The cheap tests either miss one (unsafe on the tail) or are so conservative
that the gain is gone.

## Corpus sum: an upper bound

Wrap time of the 800 run (`runs/aw3-rounds-800`, head): 5083 s, of which the tail 20 are 14.1 %. Assuming the adaptive
wrap's time scales with its size (step B: 1036392 2.1x smaller and 1.8x faster, 220653 10.1x smaller and 8.8x faster) and adding the predictor's cost c (in W0 times):

| predictor | corpus wrap-time gain |
|---|---:|
| oracle levels (perfect), free | 3.39x |
| oracle levels, c = 0.1 | 2.53x |
| oracle levels, c = 0.25 | 1.84x |
| oracle levels, c = 0.5 | 1.26x |
| ball predictor (gains as measured, cost as measured 1.29 / 2.37 W0) | 0.53x (slower) |

So even a perfect predictor must cost under about 0.2 W0 to reach 2x. The shrinking balls alone cost 1.2-2.4 W0, a bracket
search over the level thresholds (step B) 0.16-1.5 W0.

## Step B: an adaptive wrap and its guarantees

`MeshAlphaWrap_localalpha.diff.txt` (probe patch, not library code): a local alpha `LocalAlphaF(x)` (at least Alpha) in the
four gate tests of the carving (facet radius against the local alpha at the facet's circumcenter) and in the box margin.
The local alpha comes from the ball predictor (bracket search, exterior sides), seeded at the samples and ball centers and
graded in space on a grid of spacing 2 alpha0 (chamfer, g = 0.5). Separation, offset and the refinement rules are unchanged
(they use alpha0).

| mesh | W0 triangles | adaptive triangles | W0 time (s) | predictor + adaptive wrap (s) | clean, vertex distance / offset, separation / offset | W0 area farther than 1 / 2 offsets from the adaptive wrap |
|---|---:|---:|---:|---:|---|---:|
| 1036392 | 130530 | 60966 (2.1x) | 3.7 | 2.9 + 2.1 = 5.0 (0.75x) | clean, 1.000, 1.02 | 3.0 % / 0.8 % |
| 109459 | 158154 | 33190 (4.8x) | 3.7 | 2.1 + 1.0 = 3.1 (1.18x) | clean, 1.000, 1.48 | 1.6 % / 0.5 % |
| 220653 | 138902 | 13754 (10.1x) | 3.5 | 1.3 + 0.4 = 1.7 (2.07x) | clean, 1.000, 1.45 | 0.6 % / 0.2 % |
| 1165782 | 105598 | 46234 (2.3x) | 3.8 | 2.3 + 1.3 = 3.6 (1.06x) | clean, 1.000, 1.06 | 2.0 % / 0.9 % |
| 1312965 | 103850 | 47982 (2.2x) | 3.4 | 4.1 + 1.5 = 5.6 (0.61x) | clean, 1.000, 1.45 | 1.4 % / 0.4 % |
| 224108 | 539700 | 107398 (5.0x) | 11.3 | 1.8 + 2.7 = 4.4 (2.56x) | clean, 1.000, 1.27 | 3.2 % / 1.1 % |
| 51909 | 448494 | 242710 (1.8x) | 10.4 | 6.5 + 8.2 = 14.8 (0.70x) | clean, 1.000, 1.00 | 0.5 % / 0.2 % |
| 111959 | 414388 | 116744 (3.5x) | 14.9 | 7.8 + 4.0 = 11.8 (1.27x) | clean, 1.000, 1.02 | **49.5 % / 39.0 %** |
| 222695 | 717234 | 468270 (1.5x) | 18.3 | 5.3 + 16.3 = 21.6 (0.85x) | clean, 1.000, 1.00 | 9.5 % / 6.4 % |
| 78968 | 486520 | 273602 (1.8x) | 15.8 | 24.4 + 9.6 = 34.0 (0.47x) | clean, 1.000, 1.00 | 2.8 % / 1.5 % |

- **Guarantees kept** on all 10: status Success, `NotCleanReason` empty (closed, manifold, no self-intersections), every
  vertex at the offset (1.000), separation at least 1.0 offset (the bound min(Offset, Alpha - Offset) with Alpha = alpha0).
  This follows from the patch: the local alpha only changes which gates are traversed, never the Steiner rules or the
  separation, and it is at least alpha0, so termination is unchanged.
- **Fidelity lost** where the predictor is wrong: 111959 (half the W0 area more than 1 offset away), 222695 (9.5 %). Even on
  the normal meshes 0.6-3 % of the area deviates by more than 1 offset (level transitions).
- **Time**: with the predictor the adaptive wrap is slower than W0 on 6 of 10 meshes.

## Recommendation

No-go. A predictor that is safe on the tail (false area under 1 %) and cheap enough (under about 0.2 W0) is not in sight
with input-only tests: the safe variants keep 1.0-1.2x, the fast one is unsafe (41 % false area on 224108) and still costs
more than the wrap it saves. The guarantees are not the problem: a local alpha of at least alpha0 in the gate test keeps them.
What could change this: a predictor that is a by-product of the wrap itself (eg a coarse-to-fine wrap that refines only
where the next finer level differs, which needs the finer level locally), not an input analysis. That is a new design, not
this spike.

## Files

- `A_ball.jsonl`: step A on all 50 meshes (ball predictor and three parameter sets). `A2..A5*.jsonl`: the variants, on the 6
  failing tail meshes first, then the normal meshes (runs stopped once the result was clear).
- `B_adaptive_wrap.jsonl`: step B.
- `PredictorProbe_A.cs.txt`, `PredictorProbe_A5.cs.txt`, `AdaptiveWrapProbe_B.cs.txt`, `WindingDiag.cs.txt`, `Probe.csproj.txt`
  (build against the library like the benchmarks), `MeshAlphaWrap_localalpha.diff.txt` (the probe patch for step B).
