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
| noisy-sphere | aligned | 6390 | 33 | 8 (+1/4 x8) | 8 | 2 (2) | 0.3 |
| noisy-torus | smoothest | 7026 | 24 | 12 (-1/4 x6, +1/4 x6) | 0 | 0 (0) | 0.3 |
| noisy-torus | aligned | 7026 | 9 | 0 | 0 | 0 (0) | 0.2 |
| bumpy | smoothest | 17918 | 14 | 16 (-1/4 x4, +1/4 x12) | 8 | 2 (2) | 1.2 |
| bumpy | aligned | 17918 | 15 | 16 (-1/4 x4, +1/4 x12) | 8 | 2 (2) | 1.2 |

The noisy shapes have every vertex moved by up to 0.2 edge lengths along its normal. The bumpy sphere (bumps of
amplitude 0.08 at wavelength 1.6) has more singularities than ideal because the bumps are real structure.

Speed at about 55k to 60k vertices (`h=0.017`): the sphere takes 5.3 s smoothest and 5.9 s aligned, the torus 6.7 s
smoothest and 5.4 s aligned. The split (torus, aligned): frames and curvature 0.5 s, assembly and the sparse Cholesky
factorization (15.1M nonzeros in L) about 3.9 s, the Lanczos solves 0.1 s per solve, so 0.8 to 1.6 s. **The stage 1
gate of at most 2 s per 50k vertices on 4 cores is not met**: the code is serial (`SparseCholesky` has no parallel
path), and the factorization alone is 3.9 s. See the PR.
