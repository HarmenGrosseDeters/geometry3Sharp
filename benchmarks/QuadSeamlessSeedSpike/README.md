# QuadSeamlessSeedSpike

The upper-bound measurement of the research spike
[docs/research/QUAD_SEAMLESS_SEED_2026-11.md](../../docs/research/QUAD_SEAMLESS_SEED_2026-11.md): how far would the frames of a seamless
parametrization (Corman, Crane, *Rectangular Surface Parameterization*, 2025; Capouellez et al., *Feature-Aligned Parametrization in Penner
Coordinates*, 2025) cut the irregular vertices of `gs.QuadRemesher`, if they seeded `gs.PositionField`? A throwaway prototype in one file:
nothing of it is library code, and no library file is changed. Sources: [docs/sources/meshing-and-repair.md](../../docs/sources/meshing-and-repair.md).

```
dotnet run -c Release --project benchmarks/QuadSeamlessSeedSpike -- [only=<regex>] [scale=1] [ratio=0.5] [minsp=0.7] [aniso=0] [gate=1]
```

`scale` multiplies the target edge length s of every shape, `ratio` is the input edge length over s, `minsp` the lower clamp of the oracle's
spacing factor in the clamped column, `aniso=0` skips the two-scale solves (the slowest part), `gate=1` prints the table of the 54 runs of
[QuadFlow3BGate](../QuadFlow3BGate/README.md) instead. Inputs: the 9 closed procedural shapes of that gate (`MarchingCubes`, then 20
`Remesher` passes), no mesh file.

## What it computes

- **The conformal oracle.** The cones of the cross field (a singular face of index k/4 gives the angle 2 pi - k pi / 2 at its vertex of
  smallest field magnitude); the discrete conformal metric with these cone angles (Springborn, Schröder, Pinkall 2008: Newton on the convex
  energy with `SparseCholesky`, fixed connectivity, no flips); the cross field that is parallel in that flat metric (breadth first over the
  faces; the largest disagreement at an edge that closes a loop is the "seam defect"; one global rotation fitted to the input field); from
  both a spacing `exp(-u)` and a rotation per vertex, normalized to the cell count of the uniform spacing.
- **Table 1**, the curl mass of [QUAD_REMESHING.md](../../docs/design/QUAD_REMESHING.md) 4.5 (the formula of `IntegrableFrames`, re-implemented
  here with a spacing per axis; "library CurlBefore" is the library's own value on all non-singular triangles, the other columns leave out
  the triangles at the oracle's cone vertices as well) for: the uniform lattice, `IntegrableFrames` Scale and ScaleAndRotation, two scales per
  vertex with the directions fixed and with a rotation (a damped Gauss-Newton of this file with the unknowns of the rectangular model; least
  squares, not the paper's constrained solve), and the oracle.
- **Table 2**, the stage 2 mesh: `PositionField` with `SpacingFactors` and `RotationAngles`, then the private `QuadRemesher.Extract` by
  reflection. Per cell: irregular vertices / quads % / corner deviation p95 (degrees) / faces / one-sided Hausdorff distance in s, and the
  boundary vertices with the covered area if the result has holes (the extraction drops faces above 6 sides). The last column checks the
  reflection path: with no frames and with the frames of ScaleAndRotation it must give the mesh of `QuadRemesher` bit for bit, and the
  repair of table 4 on the uniform lattice must agree with `QuadRemesher.PureQuads`.
- **Table 3**, the oracle: cone vertices, Newton steps, the largest angle residual, triangles that break the triangle inequality, the seam
  defect, the spacing range and the rotation from the input field.
- **Table 4**, exploratory: the internal `QuadFlowRepair` of stage 3B (written for a uniform spacing) by reflection on each lattice. "clean"
  is decided here, not by the repair's status: manifold, quads only, closed, finite, valid, no quad facing away from the nearest input
  triangle, and a one-sided Hausdorff distance of at most 0.5 s.

## Results (2026-11-22, master e340ef5c plus this branch, net10.0 Release, 4-core cloud container, one run each)

Raw output: [results_default.txt](results_default.txt) (no arguments), [results_scale0.5.txt](results_scale0.5.txt) (`scale=0.5 aniso=0`),
[results_ratio0.25.txt](results_ratio0.25.txt) (`ratio=0.25 aniso=0`), [results_gate.txt](results_gate.txt) (`gate=1`).

Sum of the irregular vertices over the 9 shapes (88 singular faces of the field in every run), and clean pure-quad results of 9:

| run | uniform | best of Scale / ScaleAndRotation | oracle | oracle, spacing >= 0.7 | clean pure quads: uniform / ScaleAndRotation / oracle / clamped oracle |
|---|---|---|---|---|---|
| default | 863 | 493 | 354 | 329 | 5 / 3 / 3 / 6 |
| `scale=0.5` | 1821 | 1175 | 647 | 685 | 2 / 1 / 5 / 3 |
| `ratio=0.25` | 756 | 595 | 205 | 332 | 0 / 1 / 1 / 1 |

The per-shape tables and their reading are in the research note (section 3). Points to keep in mind when reading the raw files:

- The unclamped oracle loses faces near the cones where its lattice is finer than the input (capsule at the default: 704 faces for 1089
  and 54 boundary vertices); its irregular count is flattered there.
- The oracle's parallel field does not close up on shapes of genus above 0 with cones (seam defect 9-21 degrees on the torus, 1 degree on
  the genus 2 shape): a conformal metric has no holonomy constraints.
- The curl mass is extrinsic: the noisy sphere's oracle has the curl mass of the uniform lattice (95.6 for 95.9) and the fewest irregular
  vertices of its row (21 for 92).
- `gate=1`: over the 54 gate runs, 16 of the 27 runs with the lower curl mass per face are `Success`, 9 of the 27 with the higher.
