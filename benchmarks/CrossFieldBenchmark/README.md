# CrossFieldBenchmark

`gs.CrossField` (stage 1 of [QUAD_REMESHING.md](../../docs/design/QUAD_REMESHING.md)) on procedural closed shapes:
`MarchingCubes`, then 20 isotropic `Remesher` passes at edge `h`. Per shape the smoothest field and the curvature
aligned one (`CurvatureAlignment = s`). The columns are the Lanczos solves, whether it converged, the smoothness
energy per unit mass, the singular faces with the index histogram (in units of 1/4), the ideal number of singularities
of a 4-RoSy field on that genus (8 on genus 0, 0 on a torus), the sum of the indices (Poincare-Hopf: chi) and the best
time of `Compute()`.

```
dotnet run -c Release --project benchmarks/CrossFieldBenchmark -- [only=<regex>] [h=<edge>] [s=<alignment>] [reps=3] [tol=1e-4] [maxit=200]
```

## Results (2026-11-17, net10.0 Release, one thread, 4-core Xeon at 2.1 GHz in a cloud container, `s=10`, `reps=1`)

About 6k to 18k vertices (the default `h` per shape):

| shape | field | V | solves | singular faces (index x count) | ideal | sum index (chi) | time (s) |
|---|---|---|---|---|---|---|---|
| sphere | smoothest | 6390 | 9 | 8 (+1/4 x8) | 8 | 2 (2) | 0.3 |
| sphere | aligned | 6390 | 9 | 8 (+1/4 x8) | 8 | 2 (2) | 0.3 |
| torus | smoothest | 7026 | 14 | 12 (-1/4 x6, +1/4 x6) | 0 | 0 (0) | 0.25 |
| torus | aligned | 7026 | 7 | 0 | 0 | 0 (0) | 0.2 |
| filleted-box | smoothest | 9092 | 6 | 8 (+1/4 x8) | 8 | 2 (2) | 0.3 |
| filleted-box | aligned | 9092 | 5 | 8 (+1/4 x8) | 8 | 2 (2) | 0.3 |
| noisy-sphere | aligned | 6390 | 25 | 8 (+1/4 x8) | 8 | 2 (2) | 0.3 |
| noisy-torus | smoothest | 7026 | 24 | 12 (-1/4 x6, +1/4 x6) | 0 | 0 (0) | 0.3 |
| noisy-torus | aligned | 7026 | 9 | 0 | 0 | 0 (0) | 0.2 |
| bumpy | smoothest | 17918 | 14 | 16 (-1/4 x4, +1/4 x12) | 8 | 2 (2) | 1.2 |
| bumpy | aligned | 17918 | 15 | 16 (-1/4 x4, +1/4 x12) | 8 | 2 (2) | 1.2 |

The noisy shapes have every vertex moved by up to 0.2 edge lengths along its normal. The bumpy sphere (bumps of
amplitude 0.08 at wavelength 1.6) has more singularities than ideal because the bumps are real structure.

Speed at about 55k to 60k vertices (`h=0.017`, `s=10`, best of 3, 4 cores, net10.0 Release), before and after the n x n complex
Hermitian form (`SparseHermitianCholesky`) and the parallel pre/post loops (stage 1 follow-up, 2026-11-17):

| shape | field | V | before (s) | complex form, serial (s) | complex form + parallel loops (s) | per 50k vertices after (s) |
|---|---|---|---|---|---|---|
| sphere | smoothest | 54944 | 5.3 | 1.8 | 1.5 | 1.4 |
| sphere | aligned | 54944 | 5.9 | 2.1 | 1.7 | 1.5 |
| torus | smoothest | 60712 | 6.7 | 2.2 | 2.1 | 1.7 |
| torus | aligned | 60712 | 5.4 | 2.0 | 1.6 | 1.3 |
| noisy-torus | smoothest | 60712 | - | - | 3.3 (34 solves) | 2.7 |
| noisy-torus | aligned | 60712 | - | - | 1.9 | 1.5 |

Split of the torus at 60.7k, serial (s): `ParamMesh` 0.1, edges and frames 0.07, curvature terms 0.27 (aligned only), assembly
0.03, **factorization 1.2** (analysis and numeric), Lanczos solves 0.3 to 0.6, gauge, indices and face directions 0.1 to 0.14.
The factorization is serial; the loops that run in parallel (curvature, rho, face indices and directions) are about 0.5 s of
that, and a single thread (`DOTNET_PROCESSOR_COUNT=1`) takes 2.4 s (smoothest) and 1.9 s (aligned) on the torus.
The gate of at most 2 s per 50k vertices on 4 cores is met on the sphere and the torus (the noisy torus without alignment
needs 34 solves and takes 2.7 s per 50k). The results (the singular faces and their indices) are the same as before.
