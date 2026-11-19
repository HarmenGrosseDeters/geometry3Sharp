# QuadRemeshDiagnosis (data branch `runs/quad-stage2`, never merged; benchmark code only, no library change)

Diagnosis of the two open gates of stage 2 (docs/design/QUAD_REMESHING.md 4.2): irregular vertices 6 to 12 x the field's singularities, quads below 95 % on 4 of 8 shapes.
`Lab/PositionFieldLab.cs` and `Lab/QuadRemesherLab.cs` are copies of `mesh_ops/PositionField.cs` / `QuadRemesher.cs` (renamed, same algorithm, plus lever options);
the `base` row reproduces the published table bit for bit (77 / 76 / 176 / 16 / 92 / 96 / 32 irregular vertices). `Diagnose.cs` classifies, `Lab/IntegrableField.cs` is the one lever with an effect.

```
dotnet run -c Release --project benchmarks/QuadRemeshDiagnosis -- mode=diag [lever=<name>]   # cause classes (env DIAG_R = radius in units of s, default 1.5)
dotnet run -c Release --project benchmarks/QuadRemeshDiagnosis -- mode=levers [lever=<regex>] # one row per shape and lever
python3 benchmarks/QuadRemeshDiagnosis/results/make_tables.py                                   # the lever matrix below
```

Shapes: the 7 of QuadRemeshBenchmark (the 8th row of the task, the 100k sphere, was not run: it only repeats the sphere at 15 x the size).

## 1. Cause of every irregular vertex and non-quad face

Per input triangle the holonomy of the position field is computed from the integer lattice offsets of its three edges (translation and quarter turn of the neighbour's frame,
composed around the triangle): **F** net rotation != 0 (a cone of the lattice = a singularity of the cross field); **P** rotation 0 and translation != 0 (a dislocation: a
position-field singularity that is not a field singularity); **M** an edge the extraction ignores (mismatch above `MismatchLimit`). An output item (irregular vertex or
centroid of a non-quad face) gets the class of the nearest cause within R x s: F, else P, else B (input boundary), else M, else X (no cause near: an extraction artefact, a
collapsed or split edge, a resolution effect). Counts F / P / B / M / X:

| shape | irreg. V (sing. faces) | R = 0.75 | R = 1.5 | R = 2.5 | non-quad faces (R = 1.5) | P triangles (clusters) | field curl, sum of &#124;w&#124; (lattice units) |
|---|---|---|---|---|---|---|---|
| sphere | 77 (8) | 8 / 64 / 0 / 0 / 5 | 18 / 59 / 0 / 0 / 0 | 32 / 45 / 0 / 0 / 0 | 13 F, 53 P | 70 (67) | 64.9 |
| torus | 76 (12) | 13 / 58 / 0 / 0 / 5 | 23 / 53 / 0 / 0 / 0 | 38 / 38 / 0 / 0 / 0 | 28 F, 44 P | 70 (68) | 72.2 |
| bumpy | 176 (16) | 17 / 135 / 0 / 0 / 24 | 33 / 143 / 0 / 0 / 0 | 49 / 127 / 0 / 0 / 0 | 30 F, 114 P | 157 (150) | 138.6 |
| filleted-box | 16 (8) | 7 / 9 / 0 / 0 / 0 | 8 / 8 / 0 / 0 / 0 | 8 / 8 / 0 / 0 / 0 | 7 F, 9 P | 14 (14) | 29.0 |
| noisy-sphere | 92 (8) | 6 / 74 / 0 / 0 / 12 | 16 / 76 / 0 / 0 / 0 | 26 / 66 / 0 / 0 / 0 | 15 F, 71 P | 88 (84) | 99.3 |
| sphere-cap | 96 (16) | 11 / 69 / 5 / 1 / 10 | 23 / 73 / 0 / 0 / 0 | 40 / 56 / 0 / 0 / 0 | 22 F, 60 P | 120 (89), 64 within R of the boundary | 79.6 |
| open-patch | 32 (0) | 0 / 17 / 3 / 0 / 12 | 0 / 30 / 2 / 0 / 0 | 0 / 30 / 2 / 0 / 0 | 28 P | 42 (41), 37 within R of the boundary | 2.1 |

(raw: `results/diag_base_R*.md`; the sphere's valences by cause at R = 1.5: F: 4 x valence 3, 14 x valence 5; P: 6 x 3, 52 x 5, 1 x 6.)

Findings:
1. **Dominant class: P, 75-85 % of the irregular vertices and of the non-quad faces on the closed shapes** (R = 0.75: sphere 64 of 77, torus 58 of 76, bumpy 135 of 176, noisy sphere 74 of 92). Field singularities (F) are 10-20 %
   (the 8 cones of the sphere: 4 valence 3 and 14 valence 5 within 1.5 s, 8 at 0.75 s). Extraction artefacts (X) are 0 at R >= 1.5 and 0-14 % at 0.75; boundary (B) and M are negligible on the closed shapes. The
   P-triangles are single triangles with a roundoff mismatch of 0.3-0.5 s: 67 clusters on the sphere for 77 irregular vertices.
2. **The number of dislocations is the curl of the cross field in lattice units, not a property of the smoothing or the extraction.** Per triangle without a cone, w = sum over its edges of the
   edge vector in the averaged, aligned cross frame, in units of s (zero where the frames close up into a lattice). Sum of |w|: sphere 64.9 against 67 P clusters, torus 72.2 / 68, bumpy 138.6 / 150, noisy sphere 99.3 / 84
   (box 29.0 / 14, an over-estimate; cap 79.6 / 89). By the Gauss-Bonnet argument a coframe of unit spacing closes only where K = 0, so the curvature has to be moved to the cones, and a smooth field that does this has a curl mass of about
   (earth-mover cost of the curvature to the cones) / s: the count grows like 1/s (input fixed, `scale-2` / `scale-1` / `scale-0.5`: 31 / 77 / 179 on the sphere; the share of irregular vertices per face goes 9.6 / 6.0 / 3.6 %).
   Singularities of the field (8 cones) are the minimum; the position field needs ~60 more dipoles (triangle + valence 5) to absorb the metric. This matches the earlier finding that the dipoles stay under every parameter of stage 2.
3. **Boundary and ragged-boundary effects are the P count of the open shapes**: the open patch has no curl (2.1) but 37 of its 42 P triangles are within 1.5 s of the boundary (the two boundary rows of the lattice do not meet, see 4.2 "ragged boundary");
   on the cap 64 of 120.
4. The corrected offset formula (rotated neighbour frames) gives the same offsets as the library's `SlotOffset` on every edge of every shape (0 differences): the library is right, `b` is always within 45 degrees of `q_i`.

## 2. Upper bound

Gate: irregular <= 2 x singularities + 4 (sphere 20, torus 28, bumpy 36, box 20, noisy sphere 20, cap 36, patch 4).

| if a perfect fix removed ... | sphere | torus | bumpy | box | noisy sphere | cap | patch |
|---|---|---|---|---|---|---|---|
| all P, M, X (leaves F and B), R = 0.75 | 8 | 13 | 17 | 7 | 6 | 16 | 3 |
| the same, R = 1.5 (everything near a cone counted as caused by it) | 18 | 23 | 33 | 8 | 16 | 23 | 2 |
| the same, R = 2.5 (pessimistic) | 32 (no) | 38 (no) | 49 (no) | 8 | 26 (no) | 40 (no) | 2 |
| quads % (R = 1.5), all P, M, X turned into quads | 99.0 | 98.0 | 99.2 | 99.6 | 98.8 | 97.7 | 100 |

The gates are reachable **if the dislocations disappear** (R = 0.75 / 1.5: all shapes; R = 2.5, which counts every vertex within 2.5 s of a cone as the cone's, is pessimistic). No lever that moves vertices
or topology locally can do that, because the dislocations are in the lattice (finding 2). A lever must change the lattice the position field fits: its scale or its orientation. Stop condition from the task: a lever whose bound cannot reach the gates is stopped; every
lever of 3. except the last one is stopped on this ground (they act after the lattice is fixed, or smooth it with the same unit spacing).

## 3. Levers, each alone (irregular V / quads %, singular faces in the header; `results/levers_all.md` has the full rows, time, Hausdorff and corner p95)

| lever | sphere (8) | torus (12) | bumpy (16) | filleted-box (8) | noisy-sphere (8) | sphere-cap (16) | open-patch (0) |
|---|---|---|---|---|---|---|---|
| base | 77 / 94.8 | 76 / 94.9 | 176 / 95.9 | 16 / 99.1 | 92 / 93.2 | 96 / 91.4 | 32 / 95.8 |
| fix-offsets | 77 / 94.8 | 76 / 94.9 | 176 / 95.9 | 16 / 99.1 | 92 / 93.2 | 96 / 91.4 | 32 / 95.8 |
| no-cleanup | 77 / 94.8 | 76 / 94.9 | 178 / 95.9 | 16 / 99.1 | 92 / 93.2 | 97 / 91.4 | 32 / 95.8 |
| sweeps-5 | 73 / 93.8 | 70 / 95.4 | 162 / 95.8 | 23 / 98.6 | 91 / 92.5 | 94 / 91.5 | 34 / 96.0 |
| sweeps-100 | 78 / 94.5 | 79 / 95.4 | 163 / 95.8 | 14 / 99.2 | 90 / 93.8 | 98 / 90.5 | 34 / 95.9 |
| finest-sweeps-100 | 79 / 94.7 | 77 / 95.0 | 170 / 95.9 | 16 / 99.1 | 92 / 93.3 | 92 / 91.1 | 33 / 95.6 |
| weight-1/len | 70 / 94.7 | 62 / 96.7 | 177 / 95.9 | 24 / 98.8 | 88 / 94.4 | 103 / 91.6 | 42 / 95.3 |
| weight-len | 72 / 94.4 | 60 / 96.0 | 171 / 96.0 | 20 / 99.0 | 98 / 93.1 | 100 / 91.9 | 33 / 96.2 |
| round-at-end | 77 / 94.8 | 76 / 94.9 | 176 / 95.9 | 16 / 99.1 | 92 / 93.2 | 96 / 91.4 | 32 / 95.8 |
| collapse-0.3 | 77 / 94.8 | 76 / 94.9 | 176 / 95.9 | 16 / 99.1 | 92 / 93.2 | 96 / 91.4 | 32 / 95.8 |
| collapse-0.5 | 77 / 94.8 | 76 / 94.9 | 176 / 95.9 | 16 / 99.1 | 92 / 93.2 | 96 / 91.4 | 32 / 95.8 |
| collapse-0.6 | 75 / 94.8 | 76 / 94.9 | 174 / 95.9 | 16 / 99.1 | 92 / 93.2 | 96 / 91.4 | 17 / 98.3 |
| collapse-0.7 | 68 / 94.6 | 76 / 94.9 | 168 / 95.8 | 16 / 99.1 | 90 / 93.2 | 95 / 91.1 | 17 / 98.3 |
| mismatch-0.30 | 82 / 94.4 | 88 / 94.5 | 187 / 95.6 | 16 / 99.1 | 108 / 93.0 | 117 / 92.6 | 17 / 99.2 |
| mismatch-0.75 | 76 / 94.7 | 76 / 94.9 | 175 / 95.8 | 16 / 99.1 | 92 / 93.2 | 91 / 90.7 | 30 / 95.3 |
| mismatch-1.00 | 76 / 94.7 | 76 / 94.9 | 175 / 95.8 | 16 / 99.1 | 92 / 93.2 | 91 / 90.7 | 30 / 95.3 |
| couple-1 | 77 / 94.8 | 73 / 94.9 | 178 / 95.8 | 16 / 99.1 | 91 / 93.3 | 101 / 91.2 | 35 / 95.9 |
| couple-3 | 75 / 94.8 | 71 / 95.2 | 173 / 95.8 | 16 / 99.1 | 91 / 93.3 | 99 / 91.1 | 38 / 96.0 |
| couple-3-dev45 | 75 / 94.8 | 71 / 95.2 | 173 / 95.8 | 16 / 99.1 | 91 / 93.3 | 99 / 91.1 | 38 / 96.0 |
| free-scale-5x-max0.2 | 77 / 94.8 | 76 / 95.0 | 174 / 95.9 | 16 / 99.1 | 94 / 93.3 | 103 / 91.3 | 34 / 96.0 |
| free-scale-10x-max0.4 | 76 / 94.5 | 76 / 95.2 | 172 / 95.8 | 16 / 99.1 | 96 / 93.2 | 98 / 91.6 | 33 / 96.3 |
| integrable-scale | 31 / 98.2 | 36 / 97.6 | 171 / 95.9 | 33 / 98.4 | 54 / 96.1 | 85 / 94.3 | 26 / 94.9 |
| integrable-rot | 73 / 94.2 | 71 / 95.4 | 177 / 95.8 | 18 / 99.0 | 85 / 92.9 | 121 / 89.4 | 42 / 95.3 |
| integrable-both | 32 / 97.6 | 32 / 98.0 | 117 / 97.0 | 30 / 98.5 | 56 / 96.3 | 113 / 92.0 | 32 / 94.9 |
| integrable-both-small | 37 / 96.7 | 46 / 96.7 | 144 / 96.6 | 28 / 98.6 | 76 / 95.2 | 134 / 90.2 | 32 / 94.9 |
| integrable-scale-wide | 42 / 96.8 | 53 / 96.7 | 158 / 96.1 | 27 / 98.7 | 79 / 94.1 | 93 / 92.7 | 23 / 95.2 |
| integrable-both-wide | 46 / 96.9 | 51 / 96.9 | 140 / 96.6 | 20 / 98.7 | 67 / 95.4 | 117 / 89.7 | 32 / 94.9 |
| ratio-0.33 | 70 / 94.3 | 96 / 93.1 | 162 / 95.7 | 19 / 98.8 | 93 / 93.0 | 93 / 89.2 | 19 / 93.8 |
| scale-0.5 | 179 / 97.1 | 174 / 97.2 | 355 / 97.9 | 30 / 99.5 | 220 / 96.3 | 267 / 95.1 | 37 / 98.6 |
| scale-2 | 31 / 88.3 | 48 / 89.2 | 80 / 91.5 | 14 / 96.2 | 36 / 88.1 | 46 / 83.6 | 10 / 96.8 |

| shape | base: position + extraction (s) | integrable-scale: solve + position + extraction (s) |
|---|---|---|
| sphere | 0.39 | 0.33 + 0.05 + 0.00 |
| torus | 0.06 | 0.28 + 0.07 + 0.00 |
| bumpy | 0.15 | 0.75 + 0.14 + 0.01 |
| filleted-box | 0.07 | 0.45 + 0.07 + 0.00 |
| noisy-sphere | 0.05 | 0.29 + 0.05 + 0.00 |
| sphere-cap | 0.04 | 0.21 + 0.05 + 0.00 |
| open-patch | 0.02 | 0.11 + 0.02 + 0.00 |

(The `base` time of the sphere includes the JIT of the first run; the library benchmark warms up. The solve of the integrable levers runs on the cross field before the position field; it is the 4-iteration Gauss-Newton of `IntegrableField`.)

Reading, lever by lever:
- **Rounding**: `round-at-end` is bit for bit the same as `base` and that is exact, not a coincidence: every rounding of `o_i` shifts it by a lattice vector, so the lattice `o_i + s Z^2` and every later update are unchanged. Only the representative differs. Dead.
- **Smoothing iterations** (`sweeps-5/100`, `finest-sweeps-100`): -4 to +3 irregular vertices on the closed shapes, the box 14-23. Dead (the hierarchy does the work, as in the spike).
- **Neighbour weights** (`weight-1/len`, `weight-len`; the paper's weights are 1 for isotropic input): sphere 70-72, torus 60-62, noisy sphere 88-98, box 16 -> 20-24, bumpy unchanged. Mixed, at most 20 %. Not a lever for the gate.
- **Extraction collapse rule** (`collapse-*`): nothing below 0.5 s (no edge is that short); 0.6 and 0.7 change the sphere by -2 / -9 and the others by at most -8; **on the open patch 32 -> 17 and 95.8 -> 98.3 % quads** (it removes the valence-2 and 3 pairs of the ragged boundary). `mismatch-*`: 0.3 is worse everywhere, 0.75 / 1.0 equal to 0.5 or slightly worse. `no-cleanup`: the cleanup changes at most 2 vertices.
- **Local orientation coupling** (`couple-*`: a damped least-squares rotation of every lattice about its origin, up to 20 / 45 degrees, interleaved with position sweeps) and **local free scale** (`free-scale-*`, per vertex spacing up to e^0.2 / e^0.4): -6 .. +7 irregular vertices. The integer indices (the dislocation structure) stay frozen in a local update, so the misfit cannot move. Dead as a local update.
- **Finer input / smaller target**: `ratio-0.33` no change (as before); the scale rows show the 1/s growth of finding 2.
- **Integrable frames, solved globally** (`integrable-*`; own derivation after the scale and rotation freedom of mixed-integer parametrization, Bommes et al. 2009, no code read; `Lab/IntegrableField.cs`): per vertex a log spacing sigma_i and optionally a rotation phi_i, Gauss-Newton on the sum of |w|^2 over the triangles without a cone (CG on the normal equations, 0.1-0.75 s at 5-18k vertices), then the unchanged position field with the per-vertex spacing (also on the coarse levels) and the rotated orientation.
  `integrable-scale` (spacing 0.5-1.3 s): **sphere 77 -> 31 (94.8 -> 98.2 % quads), torus 76 -> 36 (97.6 %), noisy sphere 92 -> 54 (96.1 %), cap 96 -> 85 (94.3 %), box 16 -> 33 (worse), bumpy 176 -> 171, patch 32 -> 26 (94.9 %, worse)**.
  `integrable-both` (plus rotation up to 8-14 degrees): sphere 32 (97.6), torus 32 (98.0), bumpy 117 (97.0), noisy 56 (96.3), box 30, cap 113 (92.0, worse). Rotation alone does nothing (73 / 71): the co-exact part of the curl is the scale part. The solver removes only part of the curl (sphere 64.9 -> 21.9; the wide variant 14.0 with a worse result, 42 irregular), so this is **not** a tight bound of the lever. Hausdorff (0.07-0.37 s) and corner p95 stay within the gates (corner p95 6.0-12.2 on the closed shapes, base 4.9-15.7; the cap 17.5).
  Gate irregular <= 2 x sing. + 4 reached by none (torus 32 against 28 is the closest, sphere 31 against 20); the quad gate is reached on the sphere, torus, noisy sphere, box and bumpy sphere, not on the cap (94.3) and patch (94.9).
  Cost: the edge length varies by a factor of about 2 across the shape (the cube-sphere effect: smaller cells near the cones), which changes the default output (uniform size) - an owner decision.

## 4. Leaning (coordinator, owner)

1. The irregular gate of stage 2 is **not reachable by any local lever** of the position field or the extraction: 75-85 % of the irregular vertices are dislocations forced by the curl of the unit-spacing lattice (about 65 on the sphere at s = 0.1), the 8 cones are only 10-20 %. The gate "<= 2 x singularities" cannot hold for a fixed-spacing lattice on a curved surface; the count is about (curl mass) / s, so it should be stated relative to that (for the sphere about 8 + 65 dipole vertices at s = 0.1) or the lattice must be allowed to vary its spacing.
2. The one lever with a real effect is a size field that makes the frames integrable (`integrable-scale`): about half the irregular vertices and 96-98 % quads on the sphere, torus and noisy sphere, for 0.1-0.8 s more; it does not reach the gate, it does worse on the box, and it gives up uniform edge length. I would not put it in the default; as an opt-in `QuadRemesher.AdaptiveSize` it is a candidate library PR only after an owner decision on the non-uniform sizes, and it needs a more robust solver (the clamps and four Gauss-Newton steps leave 22 of 65 curl units, the box gets worse).
3. A small default change that is safe on the data: `CollapseLength` 0.4 -> 0.6 (open patch 95.8 -> 98.3 % quads, the closed shapes -2 to 0 irregular vertices, no shape worse). It changes the default output, so it waits for the go. The quad share of the sphere, torus and noisy sphere stays below 95 % with it.
4. For stage 3B (the session building `PureQuads`): the dipole count above is the floor of any approach that keeps the unit spacing and the field; a flow or local cleanup moves dipoles but cannot remove them without moving the spacing or the field. That agrees with the 4.3 upper bound (the flow removed face violations, not irregular vertices). I did not test this for the flow; it is an inference from finding 2.

Caveats: the class of an item depends on the radius R (tables at 0.75 / 1.5 / 2.5; P stays the largest class at all three); the "curl mass ~ P clusters" match is empirical (4 shapes within 20 %, box off by 2 x); the lever rows are single runs of deterministic code, no noise. About 3 hours of work, no library file changed.
