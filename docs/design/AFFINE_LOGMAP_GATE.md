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
