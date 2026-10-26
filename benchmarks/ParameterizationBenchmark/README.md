# ParameterizationBenchmark

Timings and distortion of `LSCMParameterization`, `BoundaryFirstFlattening` (minimal area distortion and
disk), `ARAPParameterization`, `TutteEmbedding`, `SLIMParameterization` and of `SparseCholesky`, on procedural
meshes (no mesh files): a hemisphere, a sphere cap up to 0.8 pi, an ellipsoid cap (x scaled by 2) and a
cylinder cut open along a ruling, with jittered interior vertices, at three sizes.

```
dotnet run --project benchmarks/ParameterizationBenchmark -c Release [-- maxRings [methods]]   # default 120: ~48k vertices
# methods: comma separated subset of LSCM,BFF,BFFdsk,ARAP,Tutte,SLIM,SLIMt,SLIMnr,Cholesky
# SLIM: default options; SLIMt: from the Tutte embedding; SLIMnr: from Tutte, one factorization per iteration
```

Columns: vertices, wall time, iterations, converged, flipped UV triangles, mean / max conformal distortion
(s1/s2), mean area distortion (|log| of the area ratio relative to the global scale), symmetric Dirichlet
energy (4 = isometry).

## Results

4 cores (Intel Xeon 2.1 GHz, cloud container), .NET 10, Release. October 2026 (after the review fixes).

| mesh | V | LSCM | BFF | ARAP (it) | Tutte | SLIM (it) |
|---|---|---|---|---|---|---|
| hemisphere | 12k | 158 ms | 108 ms | 241 ms (13) | 58 ms | 3.2 s (29) |
| hemisphere | 48k | 0.78 s | 0.68 s | 1.31 s (13) | 0.38 s | 54 s (70) |
| cap 0.8 pi | 48k | 0.88 s | 0.67 s | 1.39 s (9) | 0.52 s | 62 s (83) |
| ellipsoid | 48k | 0.75 s | 0.92 s | 1.36 s (15) | 0.39 s | 72 s (100, not converged) |
| cylinder | 58k | 1.13 s | 1.09 s | 1.59 s (3) | 0.48 s | 19 s (17) |

The SLIM columns are from before the changes below (master 7f0f97c); see there for the current numbers.

### SLIM: lagged factorization, convergence test, LSCM start

Measured per iteration (from `SLIMParameterization.IterationCompleted`; the old code with a temporary stopwatch
per phase), 4 cores, .NET 10, Release, October 2026. Before (master 7f0f97c), each iteration assembled and
factorized the 2n x 2n proxy: at 48k vertices about 0.9 s per iteration, of which 0.72-0.74 s numeric
factorization, 0.11 s assembly, 0.04 s solve, 0.01 s line search; the steps were nearly always cut by the
flip-preventing bound (t < 1 in 68 of 70 iterations on the hemisphere, t = 0.02-0.2 on the ellipsoid).

Changes: the step is -H^-1 grad E with the proxy matrix H; the factorization of H is kept while its steps lower
the energy at least at the average rate since it was made (counting a factorization as 8 steps). Converged
means the proxy predicts a decrease below the tolerance (before: one step decreased by less, which also stopped
early, eg after 7 iterations 1% above the minimum). By default SLIM starts from LSCM if that is injective
(exact overlap test), else from Tutte.

| mesh | V | before: time (it) | SLIMnr | SLIMt (factorizations) | SLIM, default (factorizations) |
|---|---|---|---|---|---|
| hemisphere | 48k | 69 s (70) | 61 s (70) | 22 s (76; 21) | 4.3 s (6; 3) |
| cap 0.8 pi | 48k | 84 s (83) | 73 s (82) | 22 s (86; 19) | 6.5 s (10; 5) |
| ellipsoid | 48k | 99 s (100, not converged) | 88 s (100, not converged) | 24 s (100, not converged; 19) | 4.7 s (5; 3) |
| cylinder | 58k | 24 s (17) | 18 s (15) | 8.4 s (17; 5) | 3.9 s (1; 1) |
| hemisphere | 12k | 4.4 s (29) | 3.9 s (29) | 2.1 s (29; 11) | 0.59 s (6; 3) |
| ellipsoid | 12k | 5.9 s (41) | 4.6 s (40) | 1.9 s (42; 12) | 0.73 s (5; 3) |

Per iteration at 48k: with a new factorization 0.83-1.15 s, with a kept one 56-80 ms (median); the start
(Tutte or LSCM with the overlap test, plus the ordering) 2.0-2.9 s. The energies are the same to 4 digits
(ellipsoid: 4.0367 converged against 4.0572 at the iteration limit; cylinder from LSCM: the LSCM map is already
the isometry, SD 4.0000). From Tutte, the ellipsoid still does not converge in 100 iterations: the steps stay
cut by the flip bound at the strongly stretched triangles near the pole (eg s1 = 30, s2 = 0.04 at 12k vertices), so
the gain there is per iteration only. Anderson acceleration was not added for this reason: with the LSCM start
3-10 iterations remain, and from Tutte the step length, not the direction, is the limit.

### Before / after the review fixes (PR #109)

ARAP and Tutte now factorize their Laplacian once for u and v (before: twice); ARAP's local step also
changed (Procrustes over the edges, the same rotations). Minimum of three alternating runs
(`dotnet run -c Release -- 120 ARAP,Tutte`, master e9e1599 against this branch); the results (distortion,
iterations) are identical.

| mesh | V | ARAP before | ARAP after | Tutte before | Tutte after |
|---|---|---|---|---|---|
| hemisphere | 48k | 1670 ms | 1358 ms | 614 ms | 385 ms |
| cap 0.8 pi | 48k | 1520 ms | 1169 ms | 769 ms | 321 ms |
| ellipsoid | 48k | 1842 ms | 1428 ms | 626 ms | 349 ms |
| cylinder | 58k | 1698 ms | 1257 ms | 807 ms | 383 ms |

Tutte is about twice as fast (the factorization dominates); ARAP about 20–25% (its iterations dominate).

`SparseCholesky` on the cotan Laplacian of the 48k hemisphere (plus 1 on the diagonal of vertex 0, which makes
it definite): n = 48001, nnz(L) = 1.67M, first
factorization (ordering, symbolic, numeric) 0.58 s, refactorization with the same pattern 0.11 s, solve
11 ms; the interleaved u,v system (n = 96002, 2x2 blocks): 1.37 s, 0.27 s, 29 ms.

Distortion (48k): no method flips a triangle on these meshes (only Tutte and SLIM guarantee it). LSCM and
BFF are conformal up to discretization (mean s1/s2 1.004–1.014, decreasing linearly with the edge length);
ARAP and SLIM trade conformality for area (hemisphere: mean area distortion 0.16 against 0.35). The
cylinder is unfolded isometrically by LSCM, BFF and ARAP (SD = 4.0000).

Findings:
- SLIM (before the changes above) cost one factorization of the 2n x 2n proxy per iteration (about 0.9 s at
  48k vertices) and needed 30–100 iterations from Tutte; on the 48k ellipsoid it stopped at the iteration limit
  with a few very thin (but not flipped) UV triangles (max s1/s2 440). It now starts from LSCM when that is
  injective and keeps factorizations: 4–7 s at 48k.
- BFF in disk mode distorts the developable cylinder strongly (a rectangle made round), as expected.
