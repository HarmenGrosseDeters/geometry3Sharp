# QuadRemeshBenchmark

`gs.CrossField`, `gs.PositionField` and `gs.QuadRemesher` (stage 2 of [QUAD_REMESHING.md](../../docs/design/QUAD_REMESHING.md))
on procedural shapes: `MarchingCubes`, then 20 isotropic `Remesher` passes at edge `h = 0.5 s`, plus two open shapes (the unit
sphere below z = 0.5 and a 1 x 1 patch). One Markdown row per shape with the stage 2 gate numbers: the share of quads, the
irregular vertices (valence other than 4, other than 3 on the boundary) against the singular faces of the field, the corner
deviation from 90 degrees, the one-sided Hausdorff distance output to input in units of `s`, whether the polygon mesh is
manifold (`PolyMeshMetrics`), and the seconds of the cross field, the position field and the extraction with its cleanup.

```
dotnet run -c Release --project benchmarks/QuadRemeshBenchmark -- [only=<regex>] [ratio=0.5] [sweeps=20] [scale=1] [serial=1] [cleanup=0] [align=<s>] [mm=0.5] [eps=0.05] [big=<n>] [diag=1]
```

`serial=1` runs the position field without parallel loops (the result is the same bit for bit), `big=<n>` adds a sphere of about
`n` input vertices (the speed gate), `diag=1` prints the valence histogram and the edge lengths.

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
