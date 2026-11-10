# Affine heat log map: brief 2 stopped at its accuracy gate

Research item 2 (owner 2026-11-03): an optional, more accurate logarithmic map for `VectorHeatMethod` after
Soliman & Sharp, *The Affine Heat Method*, CGF 2025, ported from geometry-central (MIT). The acceptance criterion
was not met: no pointwise regression against the existing log map, and better behaviour near the sphere's cut
locus. The prototype is not merged. Its code, tests, raw data and the reproduction commands stay on the branch
`agents/implement-logarithmic-map-brief-2` at `f8a5f388` (PR #371, closed). The gate's effort estimate is in
[RESEARCH_GATES_2026_11_03.md](RESEARCH_GATES_2026_11_03.md).

## What was built

- An additive `ComputeLogMap(int, LogMapStrategy)` with Legacy (the existing method, unchanged), AffineLocal and
  AffineAdaptive.
- AffineLocal solves the block-triangular homogeneous system with the existing scalar and vector heat factors.
  AffineAdaptive develops translations in the transported source frame. No new solver or data structure was needed.
- Reference tests assembled the full homogeneous matrices and solved them densely. Hand mutations of the
  translation sign and of the adaptive frame sign failed them.

## Result

Fixtures: a planar grid (exact oracle), a unit sphere and an open cylinder. The curved oracles are the
closed-form smooth log maps in a common gauge, not fitted. Significant regression means an error larger than
Legacy's by more than 1e-10.

| Variant (time factor 1) | Sphere regressions / 1536 | Cylinder regressions / 526 | Sphere band [3π/4, π] mean / max error |
|---|---:|---:|---:|
| Legacy (current) | baseline | baseline | 0.070 / 0.231 |
| AffineLocal | 456 | 68 | 0.281 / 1.811 |
| AffineAdaptive | 920 | 68 | 0.425 / 1.902 |
| potpourri3d 1.4.0 AffineLocal (geometry-central) | 460 | 70 | 0.278 / 1.796 |
| potpourri3d 1.4.0 AffineAdaptive | 920 | 70 | 0.419 / 1.887 |

- Both affine variants regress at all 216 samples of the sphere's last radius band.
- A time-factor sweep (0.0625, 0.25, 4) removed the cylinder regressions at small times, but did not rescue the
  sphere.
- The reference reproduces the failure: the port differs from potpourri3d by at most 0.016 on the intrinsic
  sphere, far below the analytical errors. So this is not a porting bug.
- The paper itself (section 5, p. 12) warns that its distances are inaccurate at the cut locus.
- Side finding: on the plane, potpourri3d's affine maps miss the exact offset (max 0.90 local, 0.18 adaptive)
  where the C# port is at roundoff. This was observed, not diagnosed.
- The affine queries were faster (warm, factorization excluded: plane 2.9 → 0.9 ms, sphere 2.5 → 2.2 ms), but
  speed was not the goal.

## Decision

The existing log map stays the only one. On these fixtures the library's construction is more accurate than
affine heat, also near the cut locus. An option that is less accurate than the default would add permanent
API surface for no benefit.

Reopen only with a new brief that names a workload where affine heat wins, for example far-field accuracy on
anisotropic meshes or the speed of many queries from one source, and a gate measured on that workload.

## Follow-up: the near-field gate (brief 3, 2026-11-08)

The first gate asked for a global replacement and failed toward the cut locus. This gate asks a narrower question,
for the use "local parameterization within a patch" (decals, texture stamps, surface brushes, local polar
coordinates): is `AffineLocal` clearly better than the alternatives a user has today, inside a patch radius? The
gate is stated here before it is run; the numbers go below it, whatever they are.

### Gate (fixed before the run)

- **Methods:** `AffineLocal` (TimeFactor 1, default options), `Legacy` (the current `ComputeLogMap(int)`),
  `MeshLocalParam` (discrete exponential map, Schmidt 2006) in both modes: `ExponentialMap_UpwindAvg` (its default)
  and `ExponentialMap`. `MeshLocalParam` gets the exact smooth surface normals at all vertices and the oracle's tangent
  frame at the source, which favours it over a user who has to estimate normals. The "best competitor" of a cell is
  the lowest of the three (Legacy and the two modes) per statistic.
- **Oracle:** the closed-form log map of the underlying smooth surface, in one gauge (the projected signpost-zero
  direction of the source, right-handed), evaluated at the mesh vertices; nothing is fitted. The noisy and the
  anisotropic fixtures keep their vertices on the smooth surface (noise is tangential), so the oracle stays exact.
- **Fixtures (procedural, no mesh files), 7:** plane (grid h = 0.1, alternating diagonals), plane noisy (the same,
  interior vertices moved by a uniform random offset of up to 0.3 h per axis, fixed seed), plane anisotropic (cells
  0.2 x 0.05, aspect 4), unit sphere (normalized cube, 17 vertices per edge, h ≈ 0.1), sphere noisy (tangential offset
  up to 0.3 h, renormalized), sphere latitude-longitude (20 x 80, aspect up to 4 at 60°, poles are vertices), open
  cylinder (radius 1, 64 around, 33 rows, height 4). Six fixed sources per fixture, away from boundaries (distance to the
  boundary at least the largest radius).
- **Radii** (patch radius, geodesic distance from the source, absolute, all below the cut locus): 0.3, 0.6, 1.2. That is
  3, 6 and 12 edge lengths on the isotropic meshes.
- **Cell:** fixture x radius, 21 cells. Per cell, the vertices of all six sources with exact distance 0 < r ≤ radius
  are pooled. Error per vertex: |computed - oracle| (absolute) divided by the mean edge length of the mesh. Statistics: mean and
  max over the cell.
- **AffineLocal wins clearly if all hold:**
  1. G1: its mean error is at most 0.5 x the best competitor's mean in at least 16 of the 21 cells (75%);
  2. G2: in no cell its mean error exceeds 1.25 x the best competitor's mean;
  3. G3: in no cell its max error exceeds 1.5 x the best competitor's max;
  4. G4: every patch vertex of every source is finite, and the source maps to zero.
- **Informational, not a criterion:** warm query time (median of 5, factorization excluded) of each method per fixture.
  `MeshLocalParam` is patch-local; the affine solve is global (one solve over the whole mesh), so for a small patch on
  a huge mesh `MeshLocalParam` may be cheaper; the docs will say so.
- **If it does not win clearly:** no API; the result goes here and into the roadmap subpage, and the work stops.

### Result of the near-field gate: does not win clearly (2026-11-08)

Command (code on the reference branch `agents/affine-local-logmap-gate` at `ab9ea81e`, not for merge; raw output
and per-vertex errors in `benchmarks/IntrinsicBenchmark/local-logmap-gate/`):
`dotnet run -c Release --project benchmarks/IntrinsicBenchmark -- --local-logmap points.csv`. The criteria above were
not changed after any run. Two earlier runs were discarded because of faults in the harness, found before any
conclusion was drawn: the normalized-cube generator leaves unwelded duplicate vertices along the cube edges (replaced by a
welded cube sphere), the latitude-longitude mesh was oriented inward, and the oracle returned distance 0 for the antipode
(cut locus). The harness now rejects non-manifold, duplicated, wrongly closed or inward-oriented fixtures.

Mean error / max error per cell, in mean edge lengths (rounded; the full table is in `gate-output.txt`):

| fixture | radius | AffineLocal | Legacy | MLP upwind | MLP exp | affine / best (mean) | affine / best (max) |
|---|---:|---|---|---|---|---:|---:|
| plane (3 variants) | 0.3-1.2 | 1e-15 .. 1e-14 | 0.11 .. 0.31 | 3e-7 .. 3e-5 | 3e-7 .. 3e-5 | ~1e-9 | ~1e-9 |
| sphere | 0.3 | 0.0037 / 0.0079 | 0.122 / 0.376 | 0.0026 / 0.0087 | 0.0055 / 0.0123 | **1.40** | 0.90 |
| sphere | 0.6 | 0.0144 / 0.031 | 0.212 / 0.573 | 0.0096 / 0.050 | 0.0205 / 0.075 | **1.51** | 0.63 |
| sphere | 1.2 | 0.052 / 0.115 | 0.274 / 0.828 | 0.066 / 0.491 | 0.101 / 0.611 | 0.78 | 0.23 |
| sphere noisy | 0.3 / 0.6 / 1.2 | 0.0038 / 0.0145 / 0.052 | 0.12 / 0.17 / 0.22 | 0.0031 / 0.0101 / 0.061 | 0.006 / 0.019 / 0.070 | 1.24 / 1.44 / 0.85 | 0.58 / 0.59 / 0.33 |
| sphere lat-long | 0.3 / 0.6 / 1.2 | 0.0035 / 0.0143 / 0.051 | 0.10 / 0.25 / 0.37 | 0.0035 / 0.0138 / 0.084 | 0.006 / 0.026 / 0.135 | 1.00 / 1.04 / 0.61 | 1.16 / 0.56 / 0.19 |
| cylinder | 0.3 / 0.6 / 1.2 | 0.0009 / 0.0018 / 0.0036 | 0.14 / 0.23 / 0.34 | 0.0016 / 0.0032 / 0.0065 | 0.0016 / 0.0032 / 0.0065 | 0.59 / 0.56 / 0.56 | 0.44 / 0.47 / 0.51 |

Criteria: **G1 fails** (mean at most half the best competitor in 9 of 21 cells, 16 needed); **G2 fails** (affine's mean is
1.40 and 1.51 times the best competitor's on the sphere at radii 0.3 and 0.6 and 1.44 times on the noisy sphere at 0.6); G3
holds; G4 holds. So no `ComputeLogMap(int, LogMapStrategy.AffineLocal)` ships and no API is added.

What the numbers say, beyond the verdict:
- Against **Legacy**, AffineLocal is 10 to 100 times more accurate inside the patch (and exact on the plane), as the
  first gate found. The weakness is Legacy's, not the affine method's strength.
- **MeshLocalParam is a strong baseline near the source**: on the plane it is exact to float roundoff (1e-7), and on the
  sphere at radii up to 6 edge lengths it is as good as or better than AffineLocal (mean ratios 1.0 to 1.5). Affine wins
  at 12 edge lengths (0.6 to 0.85 of the best competitor, max errors 3 to 6 times smaller), on the cylinder (about
  1.8 times), and has no outliers: MeshLocalParam's max error grows to 0.4 to 0.9 edge lengths at radius 1.2.
- Caveat in MeshLocalParam's favour, by design of the gate: it was given the exact smooth normals and the oracle's frame.
  With normals estimated from the mesh it would be less accurate; this was not measured, and does not change the verdict
  of the gate as fixed.
- Warm query times (ms, the whole mesh of 1500-2100 vertices; affine and Legacy solve globally, MeshLocalParam only the
  patch of radius 1.2): affine 0.8-1.8, Legacy 1.6-3.6, MeshLocalParam 0.2-2.9. Affine is faster than Legacy in every
  fixture (1.5 to 2.5 times) but not faster than MeshLocalParam on the curved fixtures. On a large mesh and a small patch the
  patch-local method scales with the patch, the global solve with the mesh.
- Reopen only with a workload where the patch is large relative to the mesh resolution (12 or more edge lengths) or
  where MeshLocalParam's normals are unavailable or poor, and a gate on that.

### Side question: the planar error of the legacy log map against the Vector Heat paper

Sharp, Soliman, Crane 2019 (arXiv 1805.09170) give no numeric planar error of the log map. What it reports:
convergence of the *transported vector* with mesh refinement (Fig. 13, 1250-11250 triangles, error relative to the
smooth solution: "roughly O(h)" for the vector heat method against O(h^2) for the polyhedral scheme), "a few degrees"
of difference from the polyhedral unfolding away from the cut locus (Fig. 14), time step t = h^2 with h the mean edge
length (Sec. 7.3, "Choice of t"), and an intrinsic Delaunay triangulation throughout. The log map itself is shown
qualitatively (Figs. 16, 19, 20, 21). Our legacy log map on a plane errs by 0.11 to 0.31 mean edge lengths (max 0.22 to
0.81) at radii of 3 to 12 edge lengths, using t = h^2 on the intrinsic Delaunay triangulation; the error is linear in h,
as the paper's O(h) says, and of the order that a first-order method with t = h^2 gives. There is no reported number to
be clearly worse than, so **this is not a defect by the roadmap's criterion**; no report to the coordinator as a defect, and
nothing is changed. The affine construction shows that the leading error can be removed (exact on the plane), which is
the improvement the roadmap item "Near-field log map" would have delivered.

## Follow-up 2: estimated normals and the far field (brief 4, 2026-11-10)

Owner scope 2026-11-10: measure `AffineLocal` against the same competitors when `MeshLocalParam` gets normals *estimated
from the mesh* (what a user has), and in the far field up to just before the cut locus. Ship `AffineLocal` only if it
wins clearly for the stated use **"robust local parameterization without good normals"**; otherwise this note takes the
result and the work stops (docs-only). The gate is fixed here before the run.

### Gate 2 (fixed before the run)

- **Methods and oracle:** as in the near-field gate (AffineLocal TimeFactor 1; Legacy; `MeshLocalParam` upwind and
  exponential-map modes; the closed-form smooth log map as oracle; the same 7 fixtures, sources and seed).
- **Estimated normals** for `MeshLocalParam`: angle-weighted vertex normals computed from the mesh alone (Thürmer &
  Wüthrich 1998; Max 1999), oriented outward by the mesh orientation. No smooth normal is used by any method.
- **Frame, same for all methods:** the source's tangent frame is built from the *estimated* normal `n0'` at the source:
  `e1'` = the intrinsic signpost-zero direction projected onto the plane of `n0'`, `e2' = n0' x e1'`. The 2D result of
  each method is mapped to 3D by this frame, `u e1' + v e2'`, and compared with the oracle's 3D tangent vector in the
  tangent plane of the smooth surface (error = 3D distance / mean edge length). So a normal error of the source frame
  costs every method alike, and the gauge of the oracle no longer matters.
- **Radii.** Near field: patch radius 0.3, 0.6, 1.2 (as before). Far field, sphere fixtures only (the cylinder's boundary
  is closer than the far radii; the planes have no cut locus and no far field inside the grid): bands
  (1.2, 1.8], (1.8, 2.4], (2.4, 2.85] of geodesic distance from the source; the antipode (cut locus) is at pi = 3.14,
  so 2.85 is 0.9 pi. `MeshLocalParam` runs to distance 3.0 (twice the largest radius would reach the cut locus).
- **Cells:** fixture x radius or band. The planes and the noisy plane have exactly planar normals, estimated or not, so
  the estimated-normal measurement adds nothing there; they are reported but excluded from the gate. The gate cells are
  the four curved fixtures at the three near radii (12) plus the three spheres at the three far bands (9): **21 cells**.
- **Floor:** an error mean below 1e-4 edge lengths counts as 1e-4 in the ratios (ties are not wins; the roundoff of
  `MeshLocalParam`'s `float` arithmetic is not a defect of it).
- **AffineLocal wins clearly if all hold** (the criteria of the first gate, unchanged, on these cells):
  1. G1: its mean error is at most 0.5 x the best competitor's mean in at least 16 of the 21 cells;
  2. G2: in no cell its mean exceeds 1.25 x the best competitor's mean;
  3. G3: in no cell its max exceeds 1.5 x the best competitor's max;
  4. G4: every vertex is finite, and the source maps to zero.
- **Informational:** the same table with the exact normals of the first gate for the far bands (so the effect of
  estimated against exact normals is visible); the worst cell per method; warm times.
- **If it does not win clearly:** no API; the result goes below and into the roadmap status of the PR; docs-only PR.

### Result of gate 2: does not win clearly (2026-11-10)

Command (harness and raw output on the reference branch `claude/param-affine-gate2-ref` at `979a00b`, not for merge; the
output is `benchmarks/IntrinsicBenchmark/local-logmap-gate/gate2-output.txt` there, 2 s run):
`dotnet run -c Release --project benchmarks/IntrinsicBenchmark -- --local-logmap-est points.csv`. The criteria above
were committed before the run and not changed. Mean / max error in mean edge lengths, the 21 gate cells (the planes
and the noisy plane are in the full table; their estimated normals are exactly the plane normal, and affine is at
roundoff there, 1e-14):

| fixture | band | AffineLocal | Legacy | MLP upwind (est. normals) | MLP exp (est.) | affine / best (mean) | affine / best (max) |
|---|---|---|---|---|---|---:|---:|
| sphere | <= 0.3 / 0.6 / 1.2 | 0.0051 / 0.016 / 0.054 | 0.12 / 0.21 / 0.28 | 0.0043 / 0.013 / 0.069 | 0.0066 / 0.022 / 0.10 | 1.18 / **1.29** / 0.78 | 0.98 / 0.67 / 0.24 |
| sphere | (1.2,1.8] / (1.8,2.4] / (2.4,2.85] | 0.13 / 0.30 / 1.63 | 0.37 / 0.45 / 0.86 | 0.35 / 1.06 / 2.2 | 0.40 / 1.06 / 3.0 | 0.38 / 0.66 / **1.90** | 0.30 / 0.79 / 1.41 |
| sphere noisy | <= 0.3 / 0.6 / 1.2 | 0.0145 / 0.033 / 0.079 | 0.12 / 0.18 / 0.24 | 0.0144 / 0.032 / 0.090 | 0.016 / 0.036 / 0.096 | 1.01 / 1.04 / 0.88 | 1.01 / 0.99 / 0.74 |
| sphere noisy | (1.2,1.8] / (1.8,2.4] / (2.4,2.85] | 0.17 / 0.31 / 1.64 | 0.33 / 0.43 / 0.72 | 0.34 / 0.95 / 1.9 | 0.33 / 0.81 / 2.7 | 0.52 / 0.74 / **2.29** | 0.52 / 0.78 / **1.85** |
| sphere lat-long | <= 0.3 / 0.6 / 1.2 | 0.0037 / 0.0145 / 0.051 | 0.10 / 0.25 / 0.37 | 0.0037 / 0.0142 / 0.084 | 0.0060 / 0.026 / 0.135 | 1.00 / 1.02 / 0.61 | 1.19 / 0.55 / 0.19 |
| sphere lat-long | (1.2,1.8] / (1.8,2.4] / (2.4,2.85] | 0.12 / 0.30 / 2.04 | 0.56 / 0.74 / 1.30 | 0.46 / 1.62 / 5.3 | 0.65 / 1.97 / 5.8 | 0.25 / 0.40 / **1.57** | 0.18 / 0.63 / 1.27 |
| cylinder | <= 0.3 / 0.6 / 1.2 | 0.0009 / 0.0018 / 0.0036 | 0.14 / 0.23 / 0.34 | 0.0016 / 0.0032 / 0.0065 | same | 0.59 / 0.56 / 0.56 | 0.44 / 0.47 / 0.51 |

**G1 fails** (mean at most half the best competitor in 3 of 21 cells, 16 needed:
all in the mid-field bands (1.2, 2.4] of the sphere and lat-long sphere); **G2 fails** (affine's mean is 1.29 times the best on the sphere at 0.6 and 1.6 to 2.3 times in the
band (2.4, 2.85]); **G3 fails** (1.85 times on the noisy sphere in that band); G4 holds. No API.

What the numbers say, beyond the verdict:
- **Estimated normals hardly hurt `MeshLocalParam`.** Angle-weighted vertex normals deviate from the smooth normal by 0.19
  degrees (mean) on the sphere, 0.07 on the lat-long sphere, and are exact on the plane and the cylinder; only the noisy
  sphere (mean 0.58, max 37 degrees) shows a difference: MeshLocalParam's mean error at 0.3 is 0.014 with estimated and
  0.003 with exact normals, a factor 4. AffineLocal needs no normals, but on that fixture it only equals the estimated-normal
  MeshLocalParam (ratio 1.0 to 1.04 up to radius 0.6, 0.88 at 1.2): its own error rises from 0.004 to 0.015 because the
  3D frame of the comparison now comes from the estimated source normal, which costs every method alike. So the stated use
  ("without good normals") gives no clear win: the competitor degrades only where the normals are really bad, and then
  the affine method does not pull ahead near the source.
- **Mid field, away from the cut locus, AffineLocal is clearly better**: in (1.2, 1.8] and (1.8, 2.4] its mean error
  is 0.25 to 0.75 of the best competitor on the three spheres (about 12 to 24 edge lengths; `MeshLocalParam` reaches 1 to 2
  edge lengths of error there, Legacy 0.4 to 0.7, affine 0.12 to 0.30).
- **Close to the cut locus (2.4 to 2.85, up to 0.9 pi) Legacy is better** than AffineLocal by 1.6 to 2.3 times in the
  mean (and 1.3 to 1.9 in the max), as the first gate found with the other fixtures and the paper (section 5) warns.
  `MeshLocalParam` is worse than both there (mean 1.9 to 5.8 edge lengths).
- `MeshLocalParam` left vertices unreached (infinite error) in the far bands of the plane-aniso and the cylinder
  fixtures (distance cut at 3.0 there, boundary of the grid); these cells are outside the gate, and the 21 gate cells
  have no infinite value.
- Reopen only with a brief that names a workload in the mid field of a closed or large surface (patch radius of 12 to 24
  edge lengths, well away from the cut locus), with its own pre-registered gate; a gate over the whole range up to the cut
  locus fails by construction.
