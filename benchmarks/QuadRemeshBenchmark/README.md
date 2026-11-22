# QuadRemeshBenchmark

`gs.CrossField`, `gs.PositionField` and `gs.QuadRemesher` (stage 2 of [QUAD_REMESHING.md](../../docs/design/QUAD_REMESHING.md))
on procedural shapes: `MarchingCubes`, then 20 isotropic `Remesher` passes at edge `h = 0.5 s`, plus two open shapes (the unit
sphere below z = 0.5 and a 1 x 1 patch). One Markdown row per shape with the stage 2 gate numbers: the share of quads, the
irregular vertices (valence other than 4, other than 3 on the boundary) against the singular faces of the field, the corner
deviation from 90 degrees, the one-sided Hausdorff distance output to input in units of `s`, whether the polygon mesh is
manifold (`PolyMeshMetrics`), and the seconds of the cross field, the position field and the extraction with its cleanup.

```
dotnet run -c Release --project benchmarks/QuadRemeshBenchmark -- [only=<regex>] [ratio=0.5] [sweeps=20] [scale=1] [serial=1] [cleanup=0] [align=<s>] [mm=0.5] [eps=0.05] [big=<n>] [diag=1] [adapt=0|1|2] [iters=<n>] [tol=<x>] [maxratio=<x>] [damp=<x>] [collapse=<x>] [sharp=1] [creases=1] [creaseangle=30] [digest=1]
```

`serial=1` runs the position field without parallel loops (the result is the same bit for bit), `big=<n>` adds a sphere of about
`n` input vertices (the speed gate), `diag=1` prints the valence histogram and the edge lengths, `collapse=<x>` sets `CollapseLength` (default 0.6 since 2026-11-19, was 0.4: the results below are with 0.4; the open patch with 0.6 has 17 irregular vertices and 98.3 % quads, see QUAD_REMESHING.md 4.2). `adapt=1|2` turns `QuadRemesher.SizeAdaptation` on (Scale, ScaleAndRotation; the lines on stderr say whether it was applied, the curl before and after, the spacing range and the solver time), `iters`, `tol`, `maxratio`, `damp` set the solver of `IntegrableFrames`. `sharp=1` runs procedural sharp CAD shapes (`sharp-box`, `sharp-cylinder`, `l-profile`, `gear`) and reports Stage 4 feature coverage, angle deviation p95, and feature Hausdorff; `creases=1` enables `CrossField.AlignCreases` and `QuadRemesher.PreserveCreases` (at `creaseangle`, default 30 deg). `digest=1` prints an FNV-style digest (64-bit words) of the output mesh (vertex bits and face indices) on stderr, to compare two builds bit for bit.

## Results (2026-11-17, net10.0 Release, 4-core Xeon at 2.1 GHz in a cloud container, default options, master with the Hermitian CrossField of #1099)

| shape | input V | s | singular faces | faces | quads % | tris | 5-6 | irreg. V | irreg / sing. | boundary V | manifold | corner dev p50 / p95 (deg) | aspect p95 | Hausdorff out->in / s | t field (s) | t position (s) | t extract (s) | levels / colours / sweeps | collapsed / valence 2 / dropped |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| sphere | 6390 | 0.1 | 8 | 1275 | 94.8 | 66 | 0 | 77 | 9.6 | 0 | yes | 2.5 / 14.5 | 1.38 | 0.07 | 0.25 | 0.19 | 0.01 | 9 / 7 / 180 | 0 / 0 / 0 |
| torus | 7026 | 0.1 | 12 | 1413 | 94.9 | 71 | 1 | 76 | 6.3 | 0 | yes | 2.4 / 12.7 | 1.36 | 0.16 | 0.16 | 0.09 | 0.02 | 9 / 6 / 180 | 0 / 0 / 0 |
| bumpy | 17918 | 0.06 | 16 | 3533 | 95.9 | 137 | 7 | 176 | 11.0 | 6 | yes | 1.7 / 10.8 | 1.36 | 0.10 | 0.31 | 0.14 | 0.01 | 11 / 6 / 220 | 0 / 1 / 1 |
| filleted-box | 9092 | 0.08 | 8 | 1732 | 99.1 | 14 | 2 | 16 | 2.0 | 0 | yes | 1.4 / 4.9 | 1.13 | 0.19 | 0.10 | 0.06 | 0.00 | 9 / 6 / 180 | 0 / 0 / 0 |
| noisy-sphere | 6390 | 0.1 | 8 | 1272 | 93.2 | 78 | 8 | 92 | 11.5 | 6 | yes | 2.7 / 15.7 | 1.42 | 0.21 | 0.10 | 0.06 | 0.00 | 9 / 7 / 180 | 0 / 0 / 1 |
| sphere-cap | 4860 | 0.1 | 16 | 950 | 91.4 | 81 | 1 | 96 | 6.0 | 42 | yes | 2.6 / 19.9 | 1.49 | 0.27 | 0.03 | 0.03 | 0.00 | 9 / 7 / 180 | 0 / 1 / 1 |
| open-patch | 2601 | 0.04 | 0 | 659 | 95.8 | 27 | 1 | 32 | - | 118 | yes | 0.7 / 8.6 | 1.33 | 0.15 | 0.02 | 0.02 | 0.00 | 7 / 4 / 140 | 0 / 0 / 0 |

Gates of stage 2 (QUAD_REMESHING.md section 4) against these numbers:

| gate | result |
|---|---|
| at least 95 % quads | **partly met**: 99.1 % (filleted box), 98.6 % (100k sphere), 95.8 / 95.9 % (open patch, bumpy sphere); **not met**: 94.8 % sphere, 94.9 % torus, 93.2 % noisy sphere, 91.4 % sphere cap |
| no face above 6 sides | met by construction (`MaxFaceSides`; the faces above it are dropped, the last column counts them: at most 1) |
| irregular vertices at most 2 x singular faces + 4 | **met only for the filleted box** (16 against 20); 76-176 on the other shapes, 6 to 12 x the singular faces (no-go: above 4 x) |
| corner deviation p95 at most 15 degrees | met on the closed shapes except the noisy sphere (15.7); 19.9 on the sphere cap (ragged boundary) |
| Hausdorff at most 0.5 s | met: 0.07-0.27 s (one-sided, output to input) |
| a manifold polygon mesh | met on every row (`PolyMeshMetrics.IsManifold`) |
| at most 5 s per 100k input vertices on 4 cores | met for the position field and the extraction: 0.92 s + 0.07 s at 110k vertices (1.4 s serial); the cross field (stage 1, #1099) takes 3.6 s there |

Speed at 109761 input vertices, `big=100000`: parallel 0.92 s position field and 0.07 s extraction, serial 1.44 s and 0.07 s.

The cleanup of the paper (collapse of near-coincident points, valence 2 vertices, dangling edges) hardly fires on these shapes
(the last column). The defects are triangles paired with valence 5 vertices: on the sphere 66 triangles and 66 valence 5 vertices,
10 valence 3 vertices, nothing else; they stay when the number of sweeps (100), the regularization (0.3), the mismatch limit
(0.25 or 1.5), the input resolution (`ratio=0.33`) or a curvature aligned field (`align=1`, `align=10`) change.

## Lattice line of boundary vertices (#1179, 2026-11-19)

`PositionField.Prepare` removed the wrong component of the origin of a boundary vertex whose cross representative was nearer the perpendicular of the
boundary (`Cb = d` instead of the perpendicular): its origin stayed at its own place along the boundary, the origins of a whole boundary row
slid, all rounded to one lattice point, and the extraction merged the row into one cluster (130 vertices over 25 s on the first strip below), whose mean is
the degenerate star. Default options (`CollapseLength` 0.6), net10.0 Release, same cloud container; `before` is master at 77698bc5. Sphere, torus, bumpy, filleted-box and noisy-sphere (the last two shapes have boundary vertices only at their small cut, which are not on a branch that changed): the rows above are the same
before and after, apart from timings; the pinned hashes of sphere, torus and box are bit for bit the same. The pure-quad attempt (stage 3B) is for closed meshes only, so it and its gate (at most max(3 x stage 2, 1 s)) are unchanged.

| shape | | faces | quads % | tris | irreg. V | corner p95 (deg) | Hausdorff out->in / s | t position (s) | t extract (s) | collapsed |
|---|---|---|---|---|---|---|---|---|---|---|
| sphere-cap | before | 950 | 91.4 | 81 | 96 | 19.9 | 0.27 | 0.05 | 0.00 | 0 |
| sphere-cap | after | 946 | 91.9 | 73 | 95 | 19.2 | 0.37 | 0.04 | 0.00 | 0 |
| open-patch | before | 642 | 98.3 | 10 | 17 | 6.2 | 0.15 | 0.02 | 0.00 | 17 |
| open-patch | after | 642 | 99.2 | 3 | 9 | 4.9 | 0.05 | 0.02 | 0.00 | 0 |

The cap's Hausdorff rises by 0.10 s (still within the 0.5 s gate). The ragged boundary of the patch, which the longer `CollapseLength` removed, is gone with the fix (nothing is left to collapse).

Open strips of 1 x w (`z = 0.1 sin(3 x)`, 100 x w / 0.0125 cells, `QuadNonManifoldVertexTests`), quad % / one-sided Hausdorff in units of s / irregular vertices, position field plus extraction time 0.06-0.15 s before and after:

| w | s | before | after |
|---|---|---|---|
| 0.1 | 0.04 | 68.7 / 2.33 / 17 | 88.9 / 0.03 / 12 |
| 0.1 | 0.06 | 40.9 / 1.56 / 14 | 79.3 / 0.03 / 9 |
| 0.2 | 0.04 | 76.2 / 2.34 / 16 | 95.4 / 0.03 / 10 |
| 0.2 | 0.06 | 62.5 / 0.30 / 11 | 89.4 / 0.03 / 12 |
| 0.3 | 0.04 | 80.3 / 2.34 / 23 | 93.4 / 0.03 / 16 |
| 0.3 | 0.06 | 77.1 / 1.56 / 22 | 93.4 / 0.02 / 10 |

## Size field (`adapt`, 2026-11-19, docs/design/QUAD_REMESHING.md 4.5)

Irregular vertices / quads % / corner p95 (degrees), same input and field as above, default settings of `IntegrableFrames`, all with `CollapseLength` 0.6 (the default since 2026-11-19; the closed shapes are the same with 0.4); `adapt=0` is the table above with the new default.

| shape | adapt=0 | adapt=1 (Scale) | adapt=2 (ScaleAndRotation) | spacing range | solver s (1 / 2) | solver + position + extraction s (0 / 1 / 2) |
|---|---|---|---|---|---|---|
| sphere | 75 / 94.8 / 14.6 | 40 / 97.1 / 9.4 | 34 / 97.3 / 9.0 | 0.54-1.13 | 0.43 / 0.56 | 0.33 / 0.78 / 0.83 |
| torus | 76 / 94.9 / 12.7 | 35 / 98.0 / 9.2 | 46 / 97.6 / 9.6 | 0.59-1.69 | 0.19 / 0.26 | 0.15 / 0.37 / 0.41 |
| bumpy | 174 / 95.9 / 10.8 | 174 / 95.9 / 10.8 (not applied) | 126 / 97.0 / 9.7 | 0.53-1.24 | 0.39 / 0.88 | 0.15 / 0.54 / 1.07 |
| filleted-box | 16 / 99.1 / 4.9 | 16 / 99.1 / 4.9 (not applied) | 16 / 99.1 / 4.9 (not applied) | 1 (not applied) | 0.25 / 0.29 | 0.07 / 0.33 / 0.39 |
| noisy-sphere | 92 / 93.2 / 15.7 | 67 / 95.8 / 12.5 | 46 / 96.9 / 10.0 | 0.50-1.23 | 0.12 / 0.34 | 0.08 / 0.19 / 0.42 |
| sphere-cap | 96 / 91.4 / 19.9 | 83 / 94.7 / 17.6 (Hausdorff 1.39 s) | 81 / 94.3 / 17.7 | 0.55-1.61 | 0.14 / 0.09 | 0.04 / 0.18 / 0.16 |
| open-patch | 17 / 98.3 / 6.2 | 12 / 96.9 / 6.2 | 17 / 98.3 / 6.2 (not applied) | 0.96-1.04 (Scale only) | 0.09 / 0.04 | 0.02 / 0.11 / 0.07 |
| big-sphere | 352 / 98.6 / 5.8 | 245 / 99.0 / 5.2 | 234 / 99.1 / 4.9 | 0.46-1.02 | 13.91 / 17.62 | 1.06 / 14.92 / 18.58 |

The size field is not applied where it does not lower the curl by 20 % (the filleted box, the open patch with rotation): the result is the uniform one bit for bit. It helps on closed smooth shapes (sphere, torus, noisy sphere, the 110k sphere); the cap's Hausdorff exceeds the gate with Scale; the 110k input needs 16 s for the solver.

### Scale solves with one unknown per vertex (2026-11-19, owner approved)

`SizeAdaptation = Scale` (`IntegrableFrames.UseRotation = false`) solves for the log spacing only: n unknowns instead of 2n (the rotation unknown was decoupled and only held by the damping). Same input and field, 4 cores, solver seconds and solver + position field + extraction seconds, before (master c57dd5d2) / after, one run each on one machine:

| shape | solver s | solver + position + extraction s | output digest |
|---|---|---|---|
| sphere | 0.42 / 0.26 | 0.77 / 0.58 | differs |
| torus | 0.25 / 0.10 | 0.40 / 0.25 | differs |
| bumpy | 0.40 / 0.21 | 0.65 / 0.38 | same (not applied) |
| filleted-box | 0.28 / 0.11 | 0.37 / 0.19 | same (not applied) |
| noisy-sphere | 0.14 / 0.08 | 0.22 / 0.17 | differs |
| sphere-cap | 0.14 / 0.08 | 0.19 / 0.12 | differs |
| open-patch | 0.14 / 0.05 | 0.15 / 0.07 | differs |
| big-sphere (120806 input V in this run) | 14.76 / 5.24 | 16.05 / 6.40 | differs |

The metrics are equal on every shape (irregular vertices, quads %, corner deviation, curl before and after, spacing range: the benchmark tables above are unchanged); the digests of the applied shapes differ because the factorization order changes the rounding of the log spacing (about 1e-9 or less), which moves vertex coordinates in the last bits. Owner decision 2026-11-19: output change of the opt-in Scale mode at rounding level. Off and ScaleAndRotation are bit for bit as before.

## Sharp features (`sharp=1 creases=1`, Stage 4, 2026-11-21)

Procedural CAD shapes isotropically remeshed at `h = 0.5 s` with `PreventNormalFlips = true, MinTriangleQuality = 0.2, PreserveBoundaryAndFeatures(30)`. Command: `dotnet run -c Release --project benchmarks/QuadRemeshBenchmark -- sharp=1 creases=0` vs `creases=1` (net10.0 Release):

| shape | mode | input V | s | faces | quads % | irreg. V | manifold | feat cov % (`> 95`) | feat ang p95 deg (`<= 10`) | feat Haus / s (`<= 0.1`) | surf Haus / s | corner p95 (deg) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| sharp-box | `creases=0` | 6352 | 0.1 | 1166 | 97.3 | 34 | yes | 1.4 | 22.8 | 0.565 | 0.40 | 17.2 |
| sharp-box | `creases=1` | 6352 | 0.1 | 1219 | **99.5** | **12** | yes | **100.0** | **0.0** | **0.000** | **0.03** | **4.6** |
| sharp-cylinder | `creases=0` | 4229 | 0.1 | 818 | 92.9 | 65 | yes | 14.6 | 21.9 | 0.505 | 0.30 | 18.2 |
| sharp-cylinder | `creases=1` | 4229 | 0.1 | 834 | **92.3** | **60** | yes | **99.5** | **1.7** | **0.052** | **0.08** | **14.4** |
| l-profile | `creases=0` | 3565 | 0.08 | 681 | 95.9 | 43 | yes | 57.1 | 2.4 | 0.520 | 0.31 | 12.3 |
| l-profile | `creases=1` | 3565 | 0.08 | 650 | **98.5** | **16** | yes | **100.0** | **0.0** | **0.000** | **0.02** | **6.6** |
| gear | `creases=0` | 8816 | 0.06 | 1644 | 91.7 | 150 | yes | 17.4 | 6.6 | 0.596 | 0.42 | 20.0 |
| gear | `creases=1` | 8816 | 0.06 | 1614 | 91.8 | 168 | yes | **100.0** | **0.0** | **0.000** | **0.04** | **16.9** |
