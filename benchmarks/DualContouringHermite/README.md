# DualContouring: exact Hermite data

`DualContouring.ExactHermite` (default on, with `QEFEigenTolerance` 0.02) against the former behaviour (`ExactHermite = false`,
tolerance 0.1): the crossing of a sign-changing edge by 60 halvings on `Implicit` instead of linear interpolation, and the normal
from the central-difference gradient there (step 1/32 of the grid edge) instead of at the interpolated point (step 1/4 cell).

```
dotnet run --project benchmarks/DualContouringHermite -c Release -- 256 5          # reference cells, repeats
dotnet run --project benchmarks/DualContouringHermite -c Release -- 256 3 sweep     # tolerance sweep
```

Error: distance in cells of the vertices of the result to a bisection marching-cubes reference (256 cells over the long axis),
and of the reference vertices to the result; mean and max over both sets. Time: best of 5 `Generate()` (parallel, 4 cores,
includes the sampling). Shapes (`Shapes.cs`, procedural): `csg` = box ∩ sphere minus three cylinders; `bracket` = base plate,
upright plate, boss, hole, slot, 45 degree chamfer; `sphere`; `torus`. The grid is offset by a fraction of a cell.

| shape | cells | tris before / after | mean err before → after | max err before → after | ms before → after |
|---|---|---|---|---|---|
| csg | 16 | 2274 / 2272 | 0.0234 → 0.0114 | 0.336 → 0.095 | 12 → 20 |
| csg | 32 | 8976 / 8976 | 0.0128 → 0.0065 | 0.342 → 0.118 | 16 → 18 |
| csg | 64 | 35210 / 35196 | 0.0075 → 0.0033 | 0.311 → 0.136 | 74 → 78 |
| bracket | 16 | 992 / 992 | 0.0282 → 0.0054 | 0.333 → 0.199 | 2 → 3 |
| bracket | 32 | 3892 / 3892 | 0.0141 → 0.0020 | 0.453 → 0.138 | 6 → 8 |
| bracket | 64 | 15548 / 15550 | 0.0066 → 0.0015 | 0.320 → 0.192 | 24 → 30 |
| sphere | 16 | 1608 / 1608 | 0.0086 → 0.0119 | 0.052 → 0.053 | 2 → 3 |
| sphere | 32 | 6484 / 6484 | 0.0044 → 0.0061 | 0.025 → 0.026 | 14 → 11 |
| sphere | 64 | 25820 / 25820 | 0.0024 → 0.0032 | 0.022 → 0.017 | 41 → 53 |
| torus | 16 | 972 / 972 | 0.0241 → 0.0267 | 0.133 → 0.136 | 1 → 2 |
| torus | 32 | 4460 / 4460 | 0.0114 → 0.0126 | 0.057 → 0.062 | 7 → 8 |
| torus | 64 | 17196 / 17196 | 0.0060 → 0.0066 | 0.085 → 0.036 | 27 → 36 |

Reading: on the sharp shapes the mean error falls to a half (csg) or a fifth (bracket) and the max error by a factor of 1.7 to 3.5; the
triangle count is the same (csg 64: 14 fewer). The time rises by 0 to 100% on this cheap field (60 bisection and 6 gradient
evaluations of `Implicit` per sign-changing edge instead of 6, about 11 times as many; on a small grid the surface edges are a
large share of the work, and times of a few ms are noisy); an expensive field (a mesh distance, a winding number) costs
proportionally more, `ExactHermite = false` restores the old time. On smooth shapes the mean error is **higher**, by 10 to 40%
(sphere 0.0044 → 0.0061 at 32 cells; absolute +0.0006 to +0.0033 cells, under 1/30 of a cell), and the max error is equal or lower.
The volume of plates about one cell thick scatters more with the position of the grid (up to 20% instead of 10% between a grid and
a rotated one, `DualContouringFaceVertexTests`).

## Tolerance sweep (32 cells, ms omitted)

Mean error / max error in cells, `QEFEigenTolerance` 0.005 to 0.1 with exact Hermite data, and the interpolated data at 0.1 and 0.02:

| shape | interp 0.1 | interp 0.02 | exact 0.005 | exact 0.01 | exact 0.02 | exact 0.05 | exact 0.1 |
|---|---|---|---|---|---|---|---|
| csg | 0.0128 / 0.342 | 0.0097 / 0.330 | 0.0062 / 0.114 | 0.0063 / 0.118 | 0.0065 / 0.118 | 0.0066 / 0.118 | 0.0094 / 0.159 |
| bracket | 0.0141 / 0.453 | 0.0132 / 0.453 | 0.0020 / 0.138 | 0.0020 / 0.138 | 0.0020 / 0.138 | 0.0020 / 0.138 | 0.0021 / 0.138 |
| sphere | 0.0044 / 0.025 | 0.0044 / 0.025 | 0.0061 / 0.026 | 0.0061 / 0.026 | 0.0061 / 0.026 | 0.0061 / 0.026 | 0.0060 / 0.026 |
| torus | 0.0114 / 0.057 | 0.0112 / 0.058 | 0.0121 / 0.062 | 0.0121 / 0.062 | 0.0126 / 0.062 | 0.0125 / 0.060 | 0.0125 / 0.060 |

With exact data the result is flat from 0.005 to 0.05 and worse at 0.1 (csg: the weak features of the cylinders are dropped), so
0.02 sits inside the plateau with a factor of 2.5 to both sides. Interpolated data gains little from a lower tolerance (its
normals blend at the crease, the QEF is ill-conditioned there either way), so 0.1 stays the choice with `ExactHermite = false`.
