# Alpha wrap quality pass (stage 7b)

`MeshRobustRepair`'s alpha wrap fallback simplifies the wrap (`SimplifyAlphaWrap`, certified within half the wrap
offset, #280). The simplified wrap keeps slivers: the `Reducer` stops at needles and caps of about 1.7 degrees.
`AlphaWrapQualityPass` (internal; `MeshRobustRepair.AlphaWrapQualityPass`; on by default from #292, off since Alpha Wrap 2.0
(2026-11-08), when the simplification's quality guard made it nearly redundant) improves the triangle
quality of the simplified wrap without giving up the certificate.

## Design

Every operation is local and kept only if all of these hold:

- **gain:** it raises the smallest angle of the changed triangles by at least 0.5 degrees. Exception: a cap flip,
  which lowers the larger maximum angle from above 150 degrees by at least 5 degrees without lowering the smallest.
- **normal turn:** no changed triangle turns by more than 30 degrees or becomes degenerate.
- **tolerance:** every new triangle is certified within 0.95 of the tolerance (half the wrap offset) of the
  unsimplified wrap, by `TriangleDeviationBounds.IsWithin` with the patch bound. The margin of 0.95 exists because
  the final check resolves to 1 % of the tolerance, so a result right at the tolerance would leave it undecided.
- **self-intersections:** no new triangle intersects another one. The test is exact
  (`TriangleTriangleFilter.IntersectsBeyondShared`), with a `TriangleHashGrid` kept in step with every operation.
- **normal deviation** (`KeepNormalDeviation`, on in the default settings): the area-weighted mean angle between
  the new triangles' normals and the wrap's normals is at most that of the removed triangles plus 0.1 degrees. Cap
  flips on nearly flat pairs (dihedral angle at most 10 degrees) are exempt. The slack holds per operation: an
  area touched again in a later pass may drift further, which the fidelity gate (d) measures.

The operations:

- edge flips;
- tangential vertex moves to the ring centroid;
- cap splits: the longest edge of a cap above 160 degrees is split at the foot of the altitude. The new vertex lies
  on the old surface, and no new triangle gets a smaller smallest or a larger largest angle than the two it
  replaces. At most 5 % more triangles.
- cap flips.

The result is used only if it is clean (`NotCleanReason`) and the one-sided Hausdorff upper bound certifies the
whole mesh within the tolerance (`certified_within`, as for the simplification); otherwise the simplified wrap
stays. The pass is serial in a fixed order, so it is deterministic, and it uses only angles and relative thresholds,
so it is scale invariant (bit for bit under 2^k scales, `InvariantUnderSimilarities`).

What the pass guarantees, and what it only measures:
- **Guaranteed:** (a) and (b), by the final gate. The smallest angle of the mesh cannot drop, because every
  operation keeps the smallest angle of its triangles.
- **Measured only:** the largest angle and the share above 170 degrees, gates (c1b) and (c1c). Splits and cap flips
  bound the largest angle of their triangles, but an ordinary flip, move or collapse needs only a gain in the
  smallest angle and may raise the largest one locally. A guard for this would change every measured variant, so it
  is a follow-up, not part of this stage.

The default settings (`MeshRobustRepair.DefaultWrapQuality`) are variant G1 of the table below.

## Gates (owner decisions, 2026-11-02)

- **(a)** certified Hausdorff distance at most 0.5 offset.
- **(b)** clean result, no self-intersections.
- **(c1a)** no triangle below 0.5 degrees, and the global minimum angle at least that of the simplified wrap.
- **(c1b)** the global maximum angle at most that of the simplified wrap.
- **(c1c)** at most 0.1 % of the triangles above 170 degrees, per mesh. The comparisons of global angles use a
  1e-9 degree margin, for the rounding of renumbered, untouched triangles. 96373's 0.102 % counts as a fail.
- **(c2)** p01, the share below 10 degrees, the share below 20 degrees and the share above 150 degrees all better
  than for the simplified wrap.
- **(c3)** the angle distributions are reported.
- **(d)** fidelity, noise-aware: per mesh and metric, worse only by more than max(5 %, the spread over 5 sampling
  seeds of the baseline) (`paired_fidelity.py --noise`, `noise_floor.py`). The #199 gate (5 % only) is reported
  alongside. Measured on one seed, the #199 gate fails on 4 of 18 meshes for an identical result with permuted
  triangles: part of #199's failures was sampling noise.
- **(e)** time within 1.25 times the simplified run.
- (c1) applies where the pass runs. A mesh whose simplification fell back to the unsimplified wrap (59196 in the
  holdout) is listed separately; the degenerate triangles of the raw wrap are a ROADMAP follow-up.
- Splits and collapses were approved with a cap of 5 % more triangles. Each variant is an internal switch that the
  benchmark can combine (`BENCH_WQ`, benchmark README).
- After G1, one more variant (G1E) was decided. Later the owner asked directly for two more: Hu et al.'s collapse
  and a larger split budget.

## Variants (18 wrap cases of the 400 set)

Baseline: the simplified wrap. "noise" and "#199" are the meshes that fail the gates (d) and #199, out of 18. "c" is
the meshes that pass (c1) and (c2). p01 is the median of the 1 %-quantile of the smallest angle. A blank cell was
not measured for that variant.

| variant | settings | noise | #199 | c | p01 | notes |
|---|---|---|---|---|---|---|
| simplified | | – | – | – | 3.66 | |
| first | flips + moves | 11 | 14 | | 9.09 | best p01; normals drift (nangle +0.153°) |
| A | + FollowReference | 11 | 15 | | 8.47 | |
| B | + no flips across creases > 20° | 10 | 11 | | 8.88 | |
| C | + worst first | 11 | 14 | | 9.02 | |
| CB | C + B | 10 | 13 | | 8.80 | |
| D0.1 | + normal-deviation guard, 0.1° slack | 6 | 10 | | 7.23 | nangle +0.016° |
| D0.3 | slack 0.3° | 10 | 14 | | 7.88 | |
| D0.1+B | | 7 | 8 | | | |
| D0.1+worst | | 5 | 11 | | | |
| D0.1+E2 | 7-point normal sampling | | | | 5.69 | worse than the centroid sample |
| G2 | first + split 160' | 11 | 13 | 18 | 9.54 | quality good, fidelity not |
| **G1** | D0.1 + cap flip 150 + split 160' | **4** | **5** | **18** | **7.94** | **default** |
| G1C | G1 + no flips across creases | 3 | 7 | 17 | | |
| G1CP | G1C + pinned crease vertices | 4 | 6 | 17 | 7.89 | 96373 c1c 0.102 % |
| G1F1 | G1 + protected input edges (1 offset) | 1 | 6 | 3 | 5.47 | caps at the edges stay |
| G1F2 | protected edges (2 offsets) | 5 | 11 | 3 | 5.21 | |
| G1E | G1F1 for flips only | 1 | 4 | 5 | 6.40 | c1c on 13/18 (median 0.14 %), 14/16 holdout |
| HU | Hu et al.-like: no flips, worst first, follow, split 160', pinned creases | 9 | 13 | 5 | 5.19 | |
| HU20 | HU with 20 % growth | 9 | 13 | 5 | 5.19 | same medians; growth at most 5.9 %: the split guard limits it, not the budget |
| HUC | HU + needle collapses (+5 % and +20 % growth) | 9 | 11 | 5 | 5.34 | 6-28 % fewer triangles, both budgets bit-identical; time 1.47× |

`split 160'` is the cap split with the guard against new extreme angles. Without that guard (`split 160`), it made
new extremes: a global minimum of 0.12 degrees and a maximum of 179.7.

Discarded before the variants:

- **The first probe without the self-intersection guard and the patch bound:** 491 s on one mesh; the certification
  was then the bottleneck. This led to #280.
- **Operations at the full tolerance:** every result fell back. After an operation, the lower bound was at about
  0.999 of the tolerance, and the final certification could not decide. Fixed with the 0.95 margin.
- **Unguarded operations (#199):** they failed the #199 gate on 14 of 18 meshes. Part of that was the gate's noise
  (see (d)).

The variants were measured on master before #311, which changed 25 of the 34 raw wraps (rule 2 of the alpha wrap).
The before/after of the default on the current master, with the noise floor re-measured, is in the [benchmark
README](../../benchmarks/MeshRepairBenchmark/README.md#alpha-wrap-quality-pass-meshrobustrepairalphawrapqualitypass-stage-7b):
6 of 18 and 3 of 16 fidelity misses, time 1.21× and 1.07×.

G1's misses of (d) before #311, as worse/allowed:

| mesh | metric | worse / allowed |
|---|---|---|
| 106620 | fm_k_w1 | 1.15 |
| 163738 | fm_reversed_fwd | 1.50 (0 → 6e-05) |
| 47878 | fm_reversed_back | 1.07 |
| 96373 | fm_ecd_fwd | 8.0 |

96373's edge numbers: Chamfer 0.00111 → 0.00156, precision 0.9885 → 0.9834, recall 0.4273 → 0.432, sharp edge
length 23.2 → 23.6.

Holdout (16 wrap cases): G1 fails (d) on 1/16 (79136 fm_h_w1) and passes (c) on 13/15 meshes where the pass runs
(112853 c1c 0.105 %, 96306 c1c 0.223 %). On 59196 the pass does not run (unsimplified fallback). p01 median 3.17 →
7.60.

## Literature

- K. Hu, D.-M. Yan, D. Bommes, P. Alliez, B. Benes, *Error-Bounded and Feature Preserving Surface Remeshing with Minimal Angle
  Improvement*, IEEE TVCG 2017. Greedy worst first; collapse, split and relocation, no flips; feature vertices fixed;
  an operation must not create an angle below the current minimum; error check against the input, not certified.
  The HU, HU20 and HUC rows follow it. On the wrap, the flips that it avoids are what G1 gains most from, and its
  collapse adds little: the slivers sit where the certificate blocks the collapse.
- Babuška and Aziz 1976: large angles (caps), not small ones, spoil the interpolation of the gradient.
  Morvan and Thibert 2004: the normal error of a triangle grows with its largest angle. This is why caps matter for
  the normal metrics, and why there are cap splits and cap flips.
- Shewchuk, *What is a good linear element?*, 2002: the quality measures and why the min and max angle are reported
  separately.
- Portaneri et al., *Alpha Wrapping with an Offset*, ACM TOG 2022: the wrap and its offset, which the tolerance is
  half of.
- Also read: Wang et al. 2019 and Chen et al. 2023 (error-bounded remeshing) and PaMO 2025 (parallel mesh
  optimization). Their guarantees use their own error measures; here the certificate is `IsWithin` with the
  Hausdorff check.

Implemented from the papers only, with no reference code: Hu et al.'s code builds on CGAL (GPL).
