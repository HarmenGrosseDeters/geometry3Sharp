# ParameterizationBenchmark

Timings and distortion of `LSCMParameterization`, `BoundaryFirstFlattening` (minimal area distortion and
disk), `ARAPParameterization`, `TutteEmbedding`, `SLIMParameterization` and of `SparseCholesky`, on procedural
meshes (no mesh files): a hemisphere, a sphere cap up to 0.8 pi, an ellipsoid cap (x scaled by 2) and a
cylinder cut open along a ruling, with jittered interior vertices, at three sizes.

```
dotnet run --project benchmarks/ParameterizationBenchmark -c Release [-- maxRings]   # default 120: ~48k vertices
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

`SparseCholesky` on the cotan Laplacian of the 48k hemisphere: n = 48001, nnz(L) = 1.67M, first
factorization (ordering, symbolic, numeric) 0.58 s, refactorization with the same pattern 0.11 s, solve
11 ms; the interleaved u,v system (n = 96002, 2x2 blocks): 1.37 s, 0.27 s, 29 ms.

Distortion (48k): no method flips a triangle on these meshes (only Tutte and SLIM guarantee it). LSCM and
BFF are conformal up to discretization (mean s1/s2 1.004–1.014, decreasing linearly with the edge length);
ARAP and SLIM trade conformality for area (hemisphere: mean area distortion 0.16 against 0.35). The
cylinder is unfolded isometrically by LSCM, BFF and ARAP (SD = 4.0000).

Findings:
- SLIM costs one factorization of the 2n x 2n proxy per iteration (about 0.8 s at 48k vertices) and needs
  30–100 iterations; on the 48k ellipsoid it stops at the iteration limit with a few very thin (but not flipped)
  UV triangles (max s1/s2 440). Use it for its guarantee, or start it from LSCM/ARAP UVs without flips
  (`InitialUV`).
- BFF in disk mode distorts the developable cylinder strongly (a rectangle made round), as expected.
