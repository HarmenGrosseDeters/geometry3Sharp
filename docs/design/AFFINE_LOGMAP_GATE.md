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
