# Varifold creases as the input of the sharp-feature pass

Status: **design, 2026-11-24** (owner go 2026-11-24, after the study
[research/VARIFOLD_FEATURES_CURVATURE_2026-11.md](../research/VARIFOLD_FEATURES_CURVATURE_2026-11.md), #1537). Step 1 of 2: no
library code until the coordinator's OK on this note.

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
