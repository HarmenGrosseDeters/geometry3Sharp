# OpenInsideOutsideBound (spike 2026-11-19, data branch `runs/open-inside-outside`, never merged)

Upper bound and a prototype for hotspots item 6 (inside / outside of open input; lead: Feng and Crane, "A Heat Method for Generalized
Signed Distance", SIGGRAPH 2024). Container with 4 cores, Release net10.0, master 1d2a270a. Everything procedural, except the Thingi10K
open meshes (fetched at run time with `benchmarks/MeshRepairBenchmark/fetch_data.py --no-large`, not committed).

    dotnet run --project benchmarks/OpenInsideOutsideBound -c Release -- proc
    ... -- thingi <dir> [max_open] [max_tris]       (MeshRobustRepair on the open meshes)
    ... -- heat <n> [cg]                            (GWN against the signed heat prototype on the procedural cases)
    ... -- thingiheat <dir> <max_open> <n>          (GWN against heat on the open Thingi meshes, no ground truth)
    ... -- det                                      (bit-identical repeats)

`SignedHeatGrid.cs` is the prototype: regular grid, oriented area normals splatted to the nearest cell, one backward-Euler heat step
(t = h^2, 7-point Laplacian, Neumann), normalize, Poisson solve of the divergence, constant from the area-weighted mean over the input,
inside = phi < 0. Written from the method description in `docs/design/INSIDE_OUTSIDE_OPEN_INPUTS.md`; no authors' code was read.
Deviations from the paper: step 1 is a solve (the paper has a closed form), nearest-cell splat, grid only (no tet mesh), no AMG.
Solvers: library `SparseSymmetricCG` (matrix-free) and `SparseCholesky` (AMD).

## 1. Upper bound, procedural cases with analytic truth (30000 random points in the box + 10 %)

Missing = share of the true solid labelled outside, IoU = labelled solid against the true solid. The floor of the closed sphere is
0.9961 (the polyhedron is inscribed). "Repair" = `MeshRobustRepair` defaults; result IoU against the true solid.

| case | GWN > 1/2: missing | GWN IoU | share of samples with GWN within 0.1 of 1/2 | repair path | repair result IoU (truth) |
|---|---:|---:|---:|---|---:|
| sphere closed (control) | 0.004 | 0.996 | 0 | Repair | 0.996 |
| sphere cap 0.3 rad | 0.006 | 0.994 | 0.001 | Repair | 0.995 |
| sphere cap 0.6 | 0.026 | 0.975 | 0.006 | Repair | 0.981 |
| sphere cap 1.0 | 0.130 | 0.870 | 0.021 | Solidify | 0.871 |
| sphere cap 1.4 | 0.365 | 0.635 | 0.034 | Solidify | 0.644 |
| sphere two caps 1.0 | 0.372 | 0.628 | 0.062 | Solidify | 0.638 |
| sphere 8 holes 0.3 | 0.028 | 0.972 | 0.008 | Solidify | 0.969 |
| sphere 8 holes 0.6 | 0.929 | 0.071 | 0.024 | Solidify | 0.069 |
| sphere band 0.15 | 0.027 | 0.973 | 0.009 | Solidify | 0.971 |
| torus window 0.5 / 1.0 | 0.063 / 0.141 | 0.937 / 0.860 | 0.006 / 0.013 | Solidify | 0.936 / 0.857 |
| two overlapping spheres, closed (control) | 0.002 | 0.998 | 0 | ExactResolve | 0.998 |
| two spheres, caps cut 0.5 | 0.013 | 0.987 | 0.004 | ExactResolve | 0.990 |
| two spheres, caps cut 1.0 | 0.123 | 0.877 | 0.025 | Solidify | 0.874 |
| two spheres, facing caps 0.6 | 0.002 | 0.998 | 0 | ExactResolve | 0.998 |

Reading: the GWN solid is wrong only where a hole is large (a cap of 1.0 rad and more, many holes of 0.6 rad). With a hole of 0.6 rad or
less the loss is at most 3 %. Where the answer is wrong, the repair result is wrong by the same amount (the fallbacks follow the GWN), so a
perfect classifier would lift the volume IoU of these fallbacks from 0.07-0.87 to about 0.99. The exact resolve never runs on them
("not closed (winding number)"). Of the misclassified samples, 23-71 % lie within 0.1 of 1/2 in most cases (column `ambig_of_err` of `results/proc.csv`), but only 7 % in 8 holes 0.6, where GWN is confidently wrong: a threshold on |GWN - 1/2| flags most errors, not all.

## 2. Upper bound, Thingi10K: 80 open meshes of the 400 broken-category selection (<= 30000 triangles; 180 read, 79 closed)

| path | meshes |
|---|---:|
| ExactResolve | 59 |
| Repair | 9 |
| Solidify | 11 |
| AlphaWrap | 1 |

The 12 fallbacks (15 %) take 62 of the 163 s (38 %). Eleven of them stop at "volume differs from the input; input: not closed (winding number)":
the reference is the GWN solid of the open input, so no truth exists here. 7 of 80 meshes have more than 1 % of the box samples with GWN
within 0.1 of 1/2. Without ground truth the Thingi bound cannot say how often the GWN solid is wrong; it only bounds the share of
inputs where the decision matters (15 % fallbacks, 38 % of the time). A random Thingi sample (120 meshes) has 13 open ones, 1 fallback.

## 3. The prototype against GWN (procedural truth, IoU against the true solid)

| case | GWN | heat 64^3 CG | heat 48^3 Cholesky |
|---|---:|---:|---:|
| sphere closed | 0.996 | 0.995 | 0.995 |
| cap 0.3 / 0.6 | 0.994 / 0.975 | 0.993 / 0.978 | 0.993 / 0.975 |
| cap 1.0 / 1.4 | 0.870 / 0.635 | 0.900 / 0.732 | 0.886 / 0.703 |
| two caps 1.0 | 0.628 | 0.818 | 0.802 |
| 8 holes 0.3 / 0.6 | 0.972 / 0.071 | 0.990 / 0.910 | 0.988 / 0.897 |
| band 0.15 | 0.973 | 0.989 | 0.986 |
| torus closed (control) | 0.980 | 0.938 | 0.931 |
| torus window 0.5 / 1.0 | 0.937 / 0.860 | 0.898 / 0.736 | 0.885 / 0.708 |
| two overlapping spheres, closed (control) | 0.998 | **0.744** | 0.744 |
| two spheres cut 0.5 / 1.0 | 0.987 / 0.877 | 0.730 / 0.704 | 0.730 / 0.700 |
| cap 1.0, 10 % / 30 % of the triangles flipped | 0.799 / 0.028 | 0.899 / 0.887 | 0.887 / 0.877 |
| two spheres cut 1.0, 10 % / 30 % flipped | 0.807 / 0.116 | 0.706 / 0.703 | 0.701 / 0.701 |

- Heat is better where GWN fails: many holes, two caps, flipped normals (GWN collapses at 30 % flips, heat does not move).
  In the big-hole sphere cases it recovers about half of the loss (cap 1.4: 0.64 -> 0.73), not all of it: it fills the hole by the
  smooth continuation of the diffused normals and leaks.
- Heat is worse where GWN is right: overlapping or nested patches (0.74 for two closed overlapping spheres; the method has binary
  labels, GWN 2 in the lens is inside), and thin solids (torus tube 0.8 wide on a 64^3 grid of a 3.6 box: 0.94 even closed).
- Thingi open meshes, 64^3: the heat solid differs from the GWN solid on a median 2.5 % of the samples (14 of 80 above 5 %, 5 above 10 %).
  For the 59 meshes the exact resolve handles, the result agrees with the GWN solid (IoU 0.999) and not with the heat solid (0.56 mean,
  better in none), expected since the resolve labels by GWN; thin walls at this resolution explain much of it. No ground truth, so this
  shows only that heat is not a drop-in replacement.
- Time: 64^3 with CG: 0.7-1.2 s per input (about 300-480 iterations, no preconditioner); Cholesky (AMD) 48^3: 21 s, two
  factorizations, dominated by the 3D fill (110k unknowns). The library's Cholesky is not the right tool for a 3D grid; matrix-free CG
  (or a multigrid) is. GWN by the fast winding number costs less than 1 s for the same samples.
- Determinism: two and three repeats give bit-identical phi (CG 48^3 and Cholesky 24^3, one process, hashes in `det`); the invariance
  over `Similarity.All()` is not tested (a library version needs it).
