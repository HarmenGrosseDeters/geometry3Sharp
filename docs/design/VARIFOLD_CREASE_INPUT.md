# Varifold creases as the input of the sharp-feature pass

Status: **built, 2026-11-24** (owner go 2026-11-24, after the study
[research/VARIFOLD_FEATURES_CURVATURE_2026-11.md](../research/VARIFOLD_FEATURES_CURVATURE_2026-11.md), #1537; note approved by the
coordinator, then built in the same PR #1541). Sections 1-8: the design as approved; section 9: what was built and measured.

## 1. Problem

`AlphaWrapFeaturePass` (the wrap's sharp-feature pass, [ALPHA_WRAP_FEATURE_VALIDITY.md](ALPHA_WRAP_FEATURE_VALIDITY.md)) takes
the input's creases from the dihedral angles of the input's own edges (`build_miters`). An edge with one triangle is skipped.
On an unwelded soup every edge has one triangle, so the pass finds no crease and does nothing: `Prepare` returns false.
Measured at 100k triangles on four CAD-like soups (`mix:0.5`: unwelded, T-junctions, gaps, jitter, holes, duplicates,
flips; table 4): 0 input creases, 0 snaps, 0 splits. The same shapes welded give 1680-3360 creases and 1377-2837 snaps.
Welding first is not a fix: on those soups the welded dihedral reaches F1 0.03 (study, 4.1).

The study measured a connectivity-free crease test on the soup's unoriented varifold at F1 0.998-1.000 up to `mix:0.5`.
This note plugs that test into the pass as an opt-in.

## 2. Where the creases enter the pass

The pass uses the input creases in one place only. `near_miter(m)` asks whether a crease point m (the point of the two
vertex planes' intersection line on a crossing wrap edge) lies within `MaxMiterDistance` offsets of the miter line of an
input sharp edge. It is called in `crease_snap_pass` (at most twice per crossing vertex pair) and in `split_pass` (once per
crossing edge). Everything else connects to the input through nearest triangles (`make_label`, the plane snap, the exact
checks), and that already works on a soup. The validity proof does not use the creases at all: every operation is checked
on its own ([ALPHA_WRAP_FEATURE_VALIDITY.md](ALPHA_WRAP_FEATURE_VALIDITY.md) 5: "which creases come back ... has no
guarantee"). So a different crease source changes what is recovered, never the guarantees.

## 3. Design

**A point query instead of an edge list.** The study tested every soup edge and placed it against the fitted line L. Here
the pass asks at a crease point only:

- The pass already holds the two vertex planes, `la` and `lb`, from nearest input triangles. Their unshifted planes are
  `N . x = D - offset`.
- The candidate input crease point is `x0`, the point of the two planes' intersection line nearest m.
- The query takes the varifold ball of radius `e` around x0 and splits its mass:
  - cluster A: the mass whose unoriented plane is within 20 degrees of `la.N`;
  - cluster B: the same for `lb.N`.
- Accept the crease point when all of these hold:
  - each cluster has at least 20 % of the ball's mass;
  - each cluster's mass-weighted plane passes within `tol` of x0 (both sheets really meet there; they are not separate
    sheets that only face each other);
  - the dihedral of the two fitted half-planes is at least `CreaseAngleDegrees` (the pass's own threshold, 30).

This replaces only `near_miter(m)`. Two consequences:

- **No edge localisation.** That localisation step is what failed on `gap:1` in the study (F1 0.37). The fitted planes
  come from each patch's own mass, so a gap between patches shifts L with the patches but does not remove the crease. The
  limit is a gap so wide that one sheet's mass leaves the ball (gap > about e / 2). Gaps wider than about alpha are seen by
  the wrap itself, as two surfaces.
- **The cost scales with the wrap's crossing edges**, not with the input's edges.

**Radius in wrap units.** `e = VarifoldRadius * alpha`, default 3.5. The study's `e = 2 h` was 3.5 alpha at its spacing
(h / alpha = 1.74-1.86, table 4). Tying e to alpha, not to the input's edge length, makes the test independent of the
tessellation: a CAD soup with a few long slivers gets the same ball as a fine scan. The quadrature subdivides big triangles
to the spacing `e / 2` (`VarifoldMeasure.Sample` rule: edge midpoints, slivers halved first). `tol = 0.5 offset + 3 rms` of
the two planes' residuals: the pass's own scale, since `MaxMiterDistance` is 1 offset.

**API** (internal by default; one public switch on a post-fork class):

| member | where | default |
|---|---|---|
| `internal bool VarifoldCreases` | `AlphaWrapFeaturePass` | false: `build_miters` and `near_miter` as today, bit for bit (the new code is not reached) |
| `internal double VarifoldRadius = 3.5` | `AlphaWrapFeaturePass` | in alphas |
| `public bool AlphaWrapSharpFeaturesOnSoups` | `MeshRobustRepair` (post-fork, needs your review) | false; with `AlphaWrapSharpFeatures` it sets `VarifoldCreases` on the pass |
| `internal sealed class VarifoldCreaseQuery` | new, `mesh_ops/VarifoldCreaseQuery.cs` | built once per pass from the input and e; `bool IsCrease(Vector3d x0, Vector3d nA, Vector3d nB, double minDihedralDeg, double tol, out double dihedral)` |

With `VarifoldCreases` on, `Prepare` no longer returns false when the dihedral finds no crease. `InputCreases` then
counts the accepted crease points, so a soup run shows a non-zero number. No "auto" mode for now: picking the source from
the input's boundary-edge share is a later decision, and the default stays as it is. `MeshTetrahedralization` (P4(b))
builds its own pass; the switch can be forwarded there later, but that is not part of this PR.

**Code reuse.** Copy the quadrature of `benchmarks/SharpFeatureProbe/VarifoldMeasure.Sample` and the cell-sorted grid
(`SortedGrid`) into the helper, without the benchmark's five kernels and with the quadrature points in one array. The
library has no varifold code (study, 2), and `PointHashGrid3d` allocates a list per cell. The 3 x 3 eigen solve uses the
library's `SymmetricEigenSolver` (as `PointCloudNormals` does) or a fixed Jacobi.

## 4. Cost bound (before the build)

Command: `SharpFeatureProbe soup wrapbound benchmarks/SharpFeatureProbe/results/soup/wrapbound.jsonl tris=100000` (raw rows
there; 4 cores). Wrap 300 / 3000, `mix:0.5` soups, the feature pass on the clean welded copy for its crossing edges.

| shape | soup triangles | wrap of the clean copy, s | pass (clean), s | snaps + crossing edges (clean) | wrap of the soup, s | measure: sample + grid, s (points) | per-query cost, us |
|---|---|---|---|---|---|---|---|
| box | 142910 | 3.09 | 1.09 | 1933 + 191 | 17.4 | 0.43 (1.28 M) | 3.8 |
| slot | 134171 | 2.49 | 0.85 | 2837 + 530 | 21.9 | 0.27 (1.21 M) | 3.6 |
| vnotch | 113222 | 2.55 | 0.69 | 2616 + 416 | 19.6 | 0.18 (1.08 M) | 3.6 |
| fillet | 139433 | 2.08 | 0.74 | 1377 + 167 | 17.3 | 0.22 (1.23 M) | 3.4 |

Bound:

- **Queries.** At most 3 x (snaps + crossing edges), about 4.6k-10k per input, at 3.4-3.8 us each (the study's per-edge test,
  same ball): 0.02-0.04 s.
- **Measure.** Built once: 0.18-0.43 s. It scales with area / e^2, not with the triangle count: about 1.2 M points at
  alpha = diagonal / 300.
- **Total: 0.20-0.47 s**, which is:

  | against | share |
  |---|---|
  | the wrap of the clean copy | 8-15 % |
  | the wrap of the soup the pass actually follows | 1.1-2.5 % |
  | target | **< 20 % of the wrap** (the survey's kill line) |

The measure dominates. If the build misses the bound, the next lever is to sample only the input triangles within
`e + MaxMiterShift offset` of a crossing edge. Those are at most the triangles near creases, a few percent of the area.

**Finding on the way (not this PR).** The wrap of the `mix:0.5` soups takes 16-22 s for 830k-970k triangles, 4 times the
clean wrap. Command: `SharpFeatureProbe soup wrapbreak .../wrapbreak.jsonl tris=100000` (slot, one breakage at a time).

| slot breakage | wrap, s | wrap triangles |
|---|---|---|
| clean | 3.4 | 243k |
| every other single breakage | 2.3-3.1 | same as clean |
| `holes:0.025` | 13.6 | 508k |
| `mix:0.5` | 22.0 | 971k |

The holes are one triangle wide, 1.8 alpha, so wider than alpha: the wrap goes inside and wraps both sides of the sheet. It
behaves as specified, but 22 s on one input is over the 20 s line (CLAUDE.md), so it needs an issue with this diagnosis. The
coordinator decides who files it. It does not change the bound above, because the bound's denominator is the clean wrap.

## 5. Quality bound and the benchmark

The ceiling is the pass on the clean welded copy: its creases are exact. The "before" is the default pass on the soup: no
crease, and without `Prepare` not even the plane pass runs. The benchmark is a new `SharpFeatureProbe` mode `soupwrap`. It
runs on the four shapes with breakages clean, soup, nonconf, jitter:0.1, holes:0.1, dup:0.2, gap:0.3, gap:1, flip:0.2 and
mix:0.25 / 0.5 / 1. Each case has three rows: default pass on the soup, `VarifoldCreases` on the soup, and default pass on
the clean copy. Each row reports:

- the class metric of the sharp-feature study (`EdgeClass`: recall of the input's creases by the result's sharp edges within
  1 and 3 offsets, precision the other way);
- the crease normal error;
- the result's `NotCleanReason`;
- the pass time and the wrap time.

Targets:

| measure | target |
|---|---|
| recall within 3 offsets, varifold on the soup, up to `mix:0.5` | within 10 % of the clean-copy ceiling (the study's F1 >= 0.998 leaves room for the pass's own losses) |
| `gap:1` | stated with its number; the point query should keep it within 20 % of the ceiling (section 3) |
| precision | not below the ceiling's by more than 5 points |
| result | clean (closed, manifold, no self-intersection, no contact with the input) in every row, as the validity proof promises |

Stop rule (as the brief): if the measured cost or quality misses these bounds by more than 10 %, list the next candidates
(the local sampling above, a larger e for gaps, a corner test with three planes) with their bounds before stopping.

## 6. Determinism and invariance

- The pass is serial and keeps that. The measure is sampled serially in triangle order. The grid is sorted by cell key, then
  by index. A query visits cells and points in a fixed order and sums in that order.
- There are no parallel loops, so there is no floating-point reduction across threads. If the sampling is ever
  parallelised, then: per-triangle slots in parallel, added serially in triangle order (the pattern of
  `MeshPrincipalCurvature`).
- 2^k invariance: e, tol and the grid's origin and cell are relative (alpha, offset, the input's bounds), so a scale by 2^k
  gives the same points scaled and the same decisions. The result is the scaled mesh bit for bit, the property the pass
  already has.
- The default path is untouched: with `VarifoldCreases` false no new line runs.

## 7. Tests (each under 5 s: soups of 2k-6k triangles, the wrap at relative alpha 100-150)

| test | checks |
|---|---|
| `VarifoldCreases_UnweldedBox_RecoversCreases` | **fails on master**: today `InputCreases == 0` and no snap on an unwelded box soup; with the switch: snaps > 0, recall within 3 offsets at least 0.9 of the welded run's; the result clean (`NotCleanReason == ""`, closed, manifold) and not touching the input |
| `VarifoldCreases_BrokenSoup_KeepsGuarantees` | `mix:0.5` L-shape or slot soup: clean result, no contact, every moved vertex at least `MinDistance` offsets from the input, creases kept (recall bound as above) |
| `VarifoldCreases_GapAsWideAsATriangle` | `gap:1` box: recall at the measured level (a regression floor, not a promise) |
| `VarifoldCreases_DefaultUnchanged` | default pass on a welded L-shape: the same mesh bit for bit as with the switch present but false (and the existing pass tests unchanged) |
| `VarifoldCreases_Invariance` | `Similarity.All()` via `Invariance.cs`: 2^k scales give the scaled result bit for bit; the others the same snap and split counts and a clean result |
| `VarifoldCreaseQuery_Reuse` | one query object asked twice at the same points gives the same answers (bits); the pass's `Compute` run twice on one instance leaves a clean mesh |
| `VarifoldCreases_Deterministic` | two runs, and runs under `gParallel` thread counts 1 and 4: the same mesh bits (no parallel path; guards a later one) |
| `VarifoldCreaseQuery_Fillet_NoCrease`, `..._ThinWall_NoCrease` | a filleted edge with R > e and two parallel sheets 0.5 alpha apart: no crease accepted |

Stryker: `AlphaWrapFeaturePass.cs` and the new helper are not exact code. If they are in a mutation shard, I run Stryker
once locally on the changed file (per the brief); the gate is otherwise nightly on master.

## 8. Sources

Papers only, already listed for the study in [sources/meshing-and-repair.md](../sources/meshing-and-repair.md): Buet,
Leonardi, Masnou (ARMA 2017; Nonlinear Analysis 2022); Buet, Rumpf (M2AN 2022); Hutchinson (1986); Weber, Hahmann, Hagen
(SMI 2010, the Gauss map clustering this test resembles); Kobbelt et al. 2001 (the pass itself). The library row gets this
note and the new class added. No code read or ported.

## 9. Built and measured

**What changed from the note.** Two guards were added to the crease test after the first test run found false creases on
smooth parts. A thin cylinder (radius 0.25 alpha-units, below the ball) and a sphere next to a box, all unwelded, gave 28
sharp edges on the smooth parts against the wrap's 4.

- **The mass between the planes.** At most `MaxBetweenFraction = 0.3` of the ball's mass may have a normal between the two
  planes: outside both clusters and within 30 degrees of their great circle. A curved surface spreads its normals there. A
  third plane at a corner has its normal along the crease line and does not count. A first version, "the two planes hold 70
  % of the mass", rejected true creases near corners: the L-shape's coverage under the UTM similarity fell to 0.75.
- **Planarity.** Each cluster's rms distance from its plane must be at most `MaxRmsFraction = 0.1` of the radius. A thin
  cylinder gives each cluster two opposite strips.

The rest is as in sections 3-8: the point query replaces `near_miter`, the radius is 3.5 alpha, the tolerance 0.5 offset + 3
rms, and the default path is untouched.

**Benchmark.** Command: `SharpFeatureProbe soup soupwrap benchmarks/SharpFeatureProbe/results/soup/soupwrap.jsonl tris=100000
"breaks=clean;soup;nonconf;jitter:0.025;dup:0.2;gap:0.15;gap:0.3;flip:0.2;mixnh:0.25;mixnh:0.5"` (raw rows there; 4 cores, the
wrap 300 / 3000).

Each case runs the wrap of the broken soup with three passes on it:

| row | input of the pass | creases from |
|---|---|---|
| before | the soup | the dihedral (finds none) |
| after | the soup | `VarifoldCreases` |
| oracle | the clean welded copy | its exact dihedral |

The ceiling is the clean copy's own wrap with its exact creases.

The metric is recall and precision of the result's sharp edges (dihedral > 30 degrees) against the offset surface's creases
of the broken input itself, within 1 and 3 offsets. Every true crease is moved with its two patches by the gap breakage.
`mixnh` is the mixed breakage without holes: holes wider than alpha let the wrap inside (#1542).

Recall within 1 offset (within 3 for the gap rows):

| breakage | slot before -> after (ceiling) | vnotch before -> after (ceiling) | box | fillet |
|---|---|---|---|---|
| clean | 0.995 -> 0.995 | 0.979 -> 0.979 | 0.999 -> 0.999 | 0.998 -> 0.998 |
| soup (unwelded) | 0.836 -> **0.996** (0.995) | 0.900 -> **0.980** (0.979) | 0.977 -> 0.999 | 0.975 -> 0.998 |
| nonconf (T-junctions) | 0.838 -> 0.997 | 0.899 -> 0.978 | 0.977 -> 0.999 | 0.975 -> 0.997 |
| dup:0.2 | 0.838 -> 0.995 | 0.898 -> 0.978 | 0.978 -> 0.999 | 0.970 -> 0.998 |
| flip:0.2 | 0.836 -> 0.996 | 0.900 -> 0.977 | 0.978 -> 0.999 | 0.971 -> 0.998 |
| jitter:0.025 (oracle) | 0.802 -> 0.949 (0.987) | 0.860 -> 0.955 (0.968) | 0.934 -> 0.988 (0.986) | 0.931 -> 0.982 (0.988) |
| gap:0.15, within 3 offsets | 0.841 -> 0.992 | 0.822 -> 0.964 | 0.989 -> 0.994 | 0.972 -> 0.987 |
| gap:0.3, within 3 offsets | 0.570 -> 0.969 | 0.585 -> 0.877 | 0.668 -> 0.924 | 0.611 -> 0.889 |
| mixnh:0.5, within 3 offsets | 0.839 -> 0.967 | 0.805 -> 0.923 | 0.981 -> 0.984 | 0.921 -> 0.973 |

Every result is clean (`NotCleanReason == ""`, 160 of 160 rows).

**Against the targets** (section 5):

| target | result |
|---|---|
| recall within 10 % of the ceiling, up to `mix:0.5` | **met**. Unwelded, T-junction, duplicate and flip rows: equal to the ceiling. Jitter: within 4 % of the oracle. Gap rows (`gap:0.15` and the `mixnh` gaps), within 3 offsets: 0.92-0.99 against the ceiling's 0.99-1.00. |
| `gap:1` | not measurable at 100k: a gap of 1 h is 1.8 alpha, so the wrap goes inside the shell, as with the holes (#1542). `gap:0.3` (0.55 alpha) is the widest gap the wrap still closes: 0.88-0.97 within 3 offsets, above 0.9 times the ceiling on two of four shapes. |
| precision not more than 5 points below the ceiling | **met** where the geometry is not moved: unwelded, T-junctions, duplicates, flips. Jitter, against the oracle: box, fillet and vnotch within 3 points, **slot 10.6 points below** (0.828 against 0.934). |

The slot's precision miss does not move with the ball (radius 2.5, 3.5 and 5 alpha give 0.825, 0.828, 0.828:
`soup soupwrap .../soupwrap_radius.jsonl tris=100000 shapes=slot "breaks=jitter:0.025;mixnh:0.5" radius=2.5,3.5,5`). So the crease test accepts the same points. What differs is the pass's vertex planes: on the soup it takes them from the
nearest jittered triangles, while the oracle takes them from the clean ones (first candidate below).

**Cost.** The varifold's share of the pass (the measure plus the queries, timed inside the pass: `VarifoldBuildTicks`,
`VarifoldQueryTicks`):

| measure | value |
|---|---|
| total | 0.17-0.34 s over the 40 cases, the note's bound 0.20-0.47 s |
| of it, queries | 0.02-0.10 s, 1900-10300 queries |
| share of the soup's wrap | 5.9-11.8 % |
| target | < 20 % |

**Tests** (`VarifoldCreaseInputTests`, `VarifoldCreaseInputOnBaseTests`, `DocExamplesTests`): 19 tests, the slowest 3.7 s.

- `FeaturePass_UnweldedSoup_RecoversTheConcaveCrease` fails on master: no creases, coverage 0.
- Each guard of the crease test has a query-level test that isolates it.
- 14 hand mutants, all killed:
  - the share of each plane (A and B);
  - the between guard;
  - the plane distance (A and B);
  - the rms guard;
  - the dihedral threshold;
  - the cluster angle;
  - the half-plane orientation;
  - the kernel weight;
  - the radius cut;
  - in the pass: accept every crease point, take x0 = m, no varifold.
- Neither changed file is in a mutation shard, so there is no Stryker run.

**After the review** (an independent review of the diff against REVIEW_CHECKLIST classes 0-11; 12 had no trigger; no library
defect found):

- `SheetsThatDoNotMeet` now has a real gap between the floor and the wall.
- The default-path test pins the hashes that origin/master gives for the wrap and the pass of the L-shape.
- A triangle no longer than half the radius gets one point at its centroid instead of three. This bounds the memory on fine
  meshes to about one point per triangle: the reviewer measured 6 M points and 1.5 GB for 2 M tiny triangles before.
- The measure's build asks `CancelF`.
- The thin-plate limit (the two faces of a plate thinner than the ball share one unoriented cluster) and the meaning of
  `InputCreases` are documented.

The benchmark, re-run after these changes, gives the same recall and precision in every row; the cost numbers above are
from that re-run.

**Next candidates** at this point (the first one was built, section 10):

| candidate | bound |
|---|---|
| The vertex planes on a jittered soup from the varifold's fitted plane (mass-weighted over a ball) instead of the nearest triangle | up to the oracle: slot jitter +0.04 recall, +0.11 precision; nothing on the unmoved breakages, already at the ceiling |
| A gap-aware placement (planes of the two patches as the label planes near a gap) | `gap:0.3` within 3 offsets 0.88-0.97 now, within 1 offset 0.31-0.68; the bound is the ceiling 0.99, but the reference crease of a gapped input is ambiguous at one offset (the faces are 5 offsets apart) |
| Local sampling (only near crossing edges) | the measure is 70-90 % of the varifold's share; at most -0.15 to -0.3 s per pass; not needed while the share stays below 12 % |

## 10. The fitted vertex planes (coordinator 2026-11-24: the first candidate of section 9, same PR)

**What.** The pass gives every wrap vertex a plane: before, the plane of its nearest input triangle. On a jittered soup that
plane carries the triangle's own tilt.

With `VarifoldCreases`, the vertex takes the input's varifold plane at its nearest input point instead (`FitPlane`):
- the cluster is the triangles within 20 degrees of the nearest one, in a ball of `VarifoldPlaneRadius` = 1.5 alpha;
- the cluster must hold at least half the ball's mass and be planar (rms at most 0.1 of the ball);
- the normal is the mass-weighted mean of the cluster's normals, turned to the vertex's side;
- the plane is lifted by `VarifoldPlaneLift` = 2 rms.

The lift matters. The fitted plane runs through the middle of the jitter, but the pass keeps 0.9 offset from the outermost
triangles, so without the lift the moves onto the plane are rejected. Measured without the lift (slot, jitter): precision 0.828 -> 0.75-0.79, and the
pass was 3 times slower.

**Two levers for the cost.** Without them the planes cost 0.3-0.5 s, and the varifold's share was 18-27 % of the wrap
(`soupwrap_planes` rows, then superseded).

- **A noise gate.** The planes are used only when the median tilt of 512 triangles (at a fixed stride) against their fitted
  planes is above `VarifoldPlaneMinTiltDegrees` = 0.1 degrees. A clean tessellation measures 0.00; jitter of 0.025 h
  measures 0.38-0.45.
- **A parallel prefetch.** When the planes are used, the labels of all wrap vertices are made before the serial passes, in
  parallel (`gParallel.ForRange`, each vertex its own slot, no reduction across threads). The passes ask a vertex's label
  before they move it and nothing moves before them, so these are the labels the lazy path makes. A test checks the same
  bits with the prefetch, without it, and with every loop serial.

Also: cells of half the radius and a dense cell table where the grid is small (at most 4 cells per point).

**Benchmark** (the same command as section 9, raw rows `results/soup/soupwrap.jsonl`; the rows of section 9, without the
planes, are now `soupwrap_planes_off.jsonl`). Recall / precision within 1 offset:

| breakage | slot | vnotch | box | fillet |
|---|---|---|---|---|
| jitter:0.025, before | 0.802 / 0.597 | 0.860 / 0.650 | 0.934 / 0.863 | 0.931 / 0.844 |
| jitter:0.025, varifold without planes | 0.949 / 0.828 | 0.955 / 0.799 | 0.988 / 0.944 | 0.982 / 0.930 |
| **jitter:0.025, with the planes** | **0.981 / 0.932** | **0.969 / 0.840** | 0.987 / 0.933 | 0.982 / 0.932 |
| jitter:0.025, oracle | 0.987 / 0.934 | 0.968 / 0.830 | 0.986 / 0.934 | 0.988 / 0.937 |

The hole-free mixes, recall within 3 offsets / precision within 1 offset:

| breakage | slot | vnotch | box | fillet |
|---|---|---|---|---|
| mixnh:0.5, without planes | 0.967 / 0.472 | 0.923 / 0.340 | 0.984 / 0.271 | 0.973 / 0.186 |
| mixnh:0.5, with planes | 0.985 / 0.529 | 0.943 / 0.372 | 0.982 / 0.279 | 0.976 / 0.211 |

- The unwelded, T-junction, duplicate, flip and gap rows do not use the planes (tilt 0.00) and are as in section 9.
- Every result is clean: 160 of 160 rows.
- **The slot's precision miss is closed:** 0.932 against the oracle's 0.934.
- **Cost:** the varifold's share is **7.3-17.5 %** of the wrap over the 40 cases (0.19-0.51 s); the most is box and fillet
  at `mixnh:0.25`. The target is < 20 %. The pass itself takes longer where the planes are used (about 5 s against 1.5 s):
  it then moves nearly every vertex onto its denoised plane, each move checked exactly, as the oracle's pass does on the
  clean copy (about 5 s too).

**Tests** added: `FitPlane_AveragesAJitteredSheet`, `FitPlane_RefusesMixedOrCurved` (including two parallel sheets),
`VarifoldPlanes_JitteredSoup_KeepGuaranteesAndTheCrease`, `VarifoldPlanes_Prefetch_IsTheLazyPath` (the same bits with the
prefetch, without it and serial) and `VarifoldPlanes_CleanSoup_NotUsed`. The slowest new test takes 3.5 s in the full run
(`RobustRepair_SharpFeaturesOnSoups_RunsTheVarifoldSource`); the jittered-soup tests take 1.6 s and 1.3 s once split. Hand mutants: 24, 23 killed.

- New for the planes: the half-mass share, the rms guard, the orientation, the cluster test, the lift sign, the gate
  always off and always on, the fitted plane ignored, and the plane fit's radius cut.
- Repeated on the crease test: the earlier mutants.
- The survivor is the plane fit's kernel weight: uniform instead of `(1 - r^2 / R^2)^4`. It is equivalent at this scale:
  on a flat or jittered sheet both weights give the same plane to within the jitter. It is kept as an accepted survivor.

**Remaining candidates and their bounds** (the stop rule: none clears 10 % on the measured set):

| candidate | bound |
|---|---|
| Gap-aware placement | `gap:0.3`: recall within 3 offsets 0.877 and 0.889 (vnotch, fillet) against the ceiling's 0.989 and 1.000, at most +0.11 on 2 of 40 rows; elsewhere at most +0.07. That gap is outside the targets (up to `mix:0.5`, whose gap is 0.15 h), and at one offset the reference itself is ambiguous (the faces are moved 2.7-5.5 offsets apart). Below 10 % on every row within the target range. |
| Local sampling (measure only near crossing edges) | the measure is 0.15-0.25 s of the 0.19-0.51 s; the share is already at most 17.5 %: no target missed |
| A planes gate per region instead of per input | the gate is global; a mostly clean input with one noisy part uses the planes everywhere (cost only, not quality): at most the planes' 0.1-0.25 s |
| Corners (three planes) | not handled by the pass at all (ALPHA_WRAP_FEATURE_VALIDITY.md); not measured here |
