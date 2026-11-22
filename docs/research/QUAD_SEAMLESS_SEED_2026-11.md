# Seamless parametrization as a seed for quad remeshing: research spike (2026-11-22)

Owner go 2026-11-22 for a research spike (no implementation). Topic: item 5 of
[SIGGRAPH_2025_2026_SURVEY_2026-11.md](SIGGRAPH_2025_2026_SURVEY_2026-11.md) section 1.6, against the quad pipeline of
[QUAD_REMESHING.md](../design/QUAD_REMESHING.md) (`CrossField`, `PositionField`, `IntegrableFrames`, `QuadRemesher`,
`QuadFlowRepair`).

**Question.** Can a rectangular or Penner-coordinate parametrization replace or seed the position-field integration, so
that pure-quad output succeeds more often, with better feature alignment and fewer irregular vertices? What is the
measured upper bound on the procedural quad benchmark inputs?

**Short answer.**
- *Replace:* not with these two papers alone. Both stop at a seamless parametrization (quarter-turn transitions, real
  translations). The step from there to quads is a quantization and an exact extraction, which both papers take from
  other tools, which the library does not have, and whose reference code is GPL.
- *Seed:* yes, for the isotropic part. A cone metric fits `PositionField.SpacingFactors` / `RotationAngles` as they are.
  An idealised seed, clamped so that no faces are lost and at the cell count of the uniform lattice, cuts the irregular
  vertices of the 9 closed benchmark shapes from 863 (uniform) and 493 (the library's size field) to 407, against 88
  field singularities. With quads half as large: 1821 / 1175 / 750. With an input twice as fine: 756 / 595 / 336. That is
  17 to 44 % below the library's size field, and still 4 to 9 times the singularities.
- *Pure quads:* no gain shown. The stage 3B repair gave clean pure-quad meshes on 7-8 of 27 runs on the seeded lattice
  and on 7 of 27 on the uniform one (exploratory, the repair is used outside its contract).
- *Feature alignment:* not measurable on the procedural benchmark (no sharp features; stage 4 is not started).

Nothing here is library code. The prototype is
[benchmarks/QuadSeamlessSeedSpike](../../benchmarks/QuadSeamlessSeedSpike/README.md). All numbers below are from its
`results_*.txt` files (master e340ef5c plus this branch, net10.0 Release, 4-core cloud container).

## 1. The two papers (paper text only)

Licences were read from the repositories' README and licence file names only; no source file was opened.

| | Corman, Crane, *Rectangular Surface Parameterization*, ACM TOG 44(4), SIGGRAPH 2025, DOI 10.1145/3731176 | Capouellez, Singh, Heistermann, Bommes, Zorin, *Feature-Aligned Parametrization in Penner Coordinates*, ACM TOG 44(4), SIGGRAPH 2025, DOI 10.1145/3731216 |
|---|---|---|
| map class | Jacobian = rotation times a positive diagonal: no shear, two axis scales | any cone metric (edge lengths with intrinsic flips), not restricted to a conformal class |
| unknowns | per vertex a conformal log factor `u` and an aspect log factor `v`, per face a frame rotation `theta` | one log length per edge of a fixed reference triangulation (Penner coordinates) |
| input | a cross field per face; only its singularities are fixed, the directions move with `theta` | cone angles and holonomy angles read off a cross field; feature edges |
| constraint | one closure equation per interior edge, linear in `u`, `v`, nonlinear in `theta` | vertex angle sums, 2g holonomy angles, per-sector angles at feature vertices, equal lengths of the two copies of a feature edge |
| solver | Newton on the KKT system (symmetric indefinite); the authors use a QP solver | Newton with the minimum-norm step, SPD normal systems; Ptolemy flips keep the metric Delaunay |
| reported | MATLAB, about a minute at 50k triangles; iteration counts not given | about 18 linear solves with features, 10 without; 17k Thingi10k models, one failure |
| existence | no theorem; fails with over-constraining boundary conditions | a seamless map exists for any cone signature except two torus cases (Shen et al. 2022); none known for feature graphs, so some feature edges are relaxed |
| injectivity | not guaranteed in the discrete setting | by construction from a valid flat metric, checked with exact predicates |
| output | seamless, not quantized | seamless, not quantized; on an overlay mesh |
| quad mesh | external quantization (Coudert-Osmont et al. 2024), then QEx | external chain: sanitization, T-mesh tracing, Bi-MDF quantization, QEx |
| code | MATLAB, **AGPL-3.0-or-later** (README): not readable for us | C++, **GPL-3.0 and MPL-2.0** licence files: not readable for us |

Predecessors of the second paper, read for the base method: Capouellez, Zorin, *Metric Optimization in Penner
Coordinates*, ACM TOG 42(6), 2023, DOI 10.1145/3618394; *Seamless Parametrization in Penner Coordinates*, ACM TOG
43(4), 2024, DOI 10.1145/3658202 (both through a summarizing fetch of the arXiv HTML, so second-hand).

What matters for us:

1. **Neither paper makes quads.** "Seamless" means the transitions across cuts are quarter turns; the translations are
   real numbers and the cones are not on integer points. The quad mesh needs a quantization (integer translations) and
   an extraction, and both papers take them from the Bern tools.
2. **The conformal case is the common core.** The rectangular model with `v = 0` is a conformal map. The Penner method
   projects onto its constraints with the discrete conformal solver of Campen et al. 2021 and adds the shear
   coordinates on top. A cone metric in the conformal class of the input is the cheapest member of both families: a
   convex problem with one unknown per vertex.
3. **What each adds over conformal.** The second scale `v` keeps the frame directions (features, curvature) where a
   conformal map has to turn them. The Penner shear coordinates give holonomy constraints (genus above 0) and exact
   feature alignment, with flips that keep the solve robust on bad meshes.

## 2. Fit with the library

Building blocks (search of the library, AGENT_WORKFLOW section 3):

| need | in the library | gap |
|---|---|---|
| cross field with cones | `CrossField` (per-vertex 4-RoSy, `SingularFaces`, `FaceIndex`) | no hard feature alignment (soft `AlignCreases`); no per-sector angles at feature vertices; no holonomy of homology loops |
| SPD sparse solve | `SparseCholesky` (reuse of the analysis, deterministic) | none for a cone metric or the Penner normal systems |
| symmetric indefinite solve | - | the KKT system of the rectangular paper needs an LDL^T or a QP solver; `SparseSymmetricCG` does not apply |
| discrete conformal map with cones | `ConeFlattening` (linearised Yamabe solve, cones placed greedily, curvature at the cones free) | prescribed cone angles of k pi / 2 need the nonlinear solve (in the prototype: about 100 lines, 3-5 Newton steps) |
| intrinsic flips | `IntrinsicTriangulation` (signposts, Euclidean flips, `FlipToDelaunay`) | Ptolemy flips on log lengths and the per-flip Jacobian; plan PR #1285 (P1) is about collapse and virtual fan vertices, not about this |
| cut to a disc, layout | `MeshDiscCut`, `BoundaryFirstFlattening` | a layout from a metric with quarter-turn seams; the overlay of the flipped and the input mesh (`IntrinsicCommonSubdivision` is a start) |
| size and rotation of the lattice | `PositionField.SpacingFactors`, `RotationAngles`; `IntegrableFrames` | one spacing per vertex only: a rectangular seed needs two |
| quantization | - | new (T-mesh or decimated-mesh quantization; the flow of `MinCostFlow` is a part of Bi-MDF at most) |
| extraction from an integer-grid map | - | new (QEx-style, exact predicates are there) |
| pure quads on a lattice | `QuadFlowRepair` (stage 3B) | written for a uniform spacing; `PureQuads` with `SizeAdaptation` is `InvalidInput` today |

Constraints: CPU only and netstandard2.0 + net10.0 are no issue (no dependency, `Math` and `SparseCholesky` only). Bit-for-bit
determinism: the cone metric solve is a serial Newton iteration on a deterministic factorization, and its inputs are
ratios of lengths and angles, so the 2^k scale invariance of the existing stages should carry over (not tested in the
spike). No neural part anywhere.

## 3. Upper bound

### 3.1 What is idealised

The theoretical floor is known without a measurement: a valid integer-grid map gives a pure quad mesh whose irregular
vertices are exactly the cones (S = 8 on the sphere, the box, the capsule and the cylinder; 88 over the 9 shapes). What
can be measured cheaply is how much of the way a *seed* goes, with the position field still doing the rounding.

The oracle of the prototype is an idealised isotropic seed: exact in the metric, with own choices in the transfer.

1. The cones of the cross field (a singular face of index k/4 becomes the angle 2 pi - k pi / 2 at its vertex of
   smallest field magnitude).
2. The discrete conformal metric with exactly these cone angles (Springborn, Schröder, Pinkall 2008: Newton on the convex
   energy, fixed connectivity; residual below 1e-11 rad after 3-5 steps, no broken triangle on any of the 27 runs).
3. The cross field that is parallel in that flat metric (breadth first over the faces, one global rotation fitted to the
   input field).
4. From both, a spacing `exp(-u)` and a rotation per vertex for `PositionField`, scaled to the expected cell count of
   the uniform spacing; then the library's extraction and cleanup, unchanged (called by reflection; the same path with
   the library's own frames reproduces `QuadRemesher` bit for bit on every run).

Its frames close up exactly in the flat metric. The position field does not see that metric but per-vertex frames in
the tangent planes, so the seed it gets is not curl-free in its own terms (section 3.3).

Two oracle columns:
- **unclamped**: near a cone the spacing falls below the input edge length, the lattice there cannot be represented by
  per-vertex origins, and faces are lost (a result is marked "holes" above 12 boundary vertices). Its irregular counts
  are flattered and its face counts are low.
- **clamped**: the spacing factor is at least 0.7 and the scale is found again so that the expected cell count is the
  uniform one. No result at the default has holes, and the face counts are within 3 % of the uniform ones. This is the
  column to compare. The value 0.7 was chosen on these 9 shapes.

### 3.2 Irregular vertices (stage 2 mesh)

Default resolution (target edge x 1, input edge 0.5 s). Irregular vertices / quads % / faces.

| shape | S | uniform | library best (Scale or ScaleAndRotation) | oracle, unclamped | oracle, clamped |
|---|---|---|---|---|---|
| sphere | 8 | 75 / 94.8 / 1274 | 34 / 97.3 / 1320 | 34 / 98.6 / 1253 | 27 / 97.8 / 1292 |
| torus | 12 | 76 / 94.9 / 1413 | 35 / 98.0 / 1411 | 36 / 97.9 / 1385 | 31 / 97.8 / 1382 |
| torus, alignment 10 | 0 | 106 / 93.6 / 1399 | 18 / 99.0 / 1559 | 28 / 98.4 / 1374 | 30 / 98.4 / 1409 |
| bumpy sphere | 16 | 174 / 95.9 / 3532 | 126 / 97.0 / 3583 | 64 / 99.4 / 3455 (holes) | 81 / 98.1 / 3593 |
| filleted box | 8 | 16 / 99.1 / 1732 | 16 / 99.1 / 1732 (not applied) | 16 / 99.4 / 1694 | 14 / 99.2 / 1692 |
| noisy sphere | 8 | 92 / 93.2 / 1272 | 46 / 96.9 / 1341 | 21 / 99.2 / 1252 (holes) | 31 / 97.3 / 1274 |
| capsule | 8 | 91 / 92.7 / 1089 | 91 / 92.7 / 1089 (not applied) | 28 / 96.6 / 704 (holes) | 69 / 94.0 / 1113 |
| genus 2 | 20 | 148 / 95.1 / 2619 | 59 / 98.2 / 2641 | 63 / 98.0 / 2572 | 51 / 98.5 / 2576 |
| capped cylinder | 8 | 85 / 93.4 / 1280 | 68 / 95.8 / 1323 | 64 / 97.7 / 1201 (holes) | 73 / 95.4 / 1266 |
| **sum** | **88** | **863** | **493** | **354** | **407** |

The three resolutions: the sum of the irregular vertices over the 9 shapes, and in brackets per 1000 faces (S = 88 in
each run).

| run | uniform | library best | oracle, unclamped | oracle, clamped |
|---|---|---|---|---|
| default | 863 (55.3) | 493 (30.8) | 354 (23.8), holes on 4 shapes | 407 (26.1) |
| target edge x 0.5 (4 x the quads) | 1821 (29.3) | 1175 (18.6) | 647 (10.8), holes on 6 | 750 (12.1) |
| input edge 0.25 s (4 x the input triangles) | 756 (48.7) | 595 (38.1) | 205 (13.4), holes on 1 | 336 (21.6) |

Reading:

- **Against the library's size field the clamped seed removes 17 % of the irregular vertices at the default, 36 % with
  finer quads and 44 % with a finer input.** It is behind on two rows of the default (aligned torus 30 against 18,
  capped cylinder 73 against 68) and ahead or equal on the other seven.
- **It gains where the size field does not run.** Neither mode of `IntegrableFrames` is applied on 2-3 of the 9 shapes,
  depending on the resolution (Scale alone: 4-5), and with a finer input the applied ones gain little (sphere 72 -> 52,
  the clamped oracle 23). The oracle does not depend on a Gauss-Newton iteration finding its way or on a gain threshold.
- **Without the clamp and with a fine input the count comes close to the cones** on the smooth genus 0 shapes: sphere 12
  for 8, noisy sphere 10 for 8, bumpy sphere 33 for 16, capped cylinder 20 for 8 (input edge 0.25 s, no holes on these
  rows). The clamp gives a part of that back (23, 30, 70, 40): what is lost by clamping is lost near the cones.
- **The seeded result is still 4 to 9 times the cone count.** What causes the rest was not separated: the seed is not
  curl-free in the position field's own frames (3.3), the per-vertex sampling improves with a finer input (sphere,
  unclamped: 34 -> 12 at the same quad size), and the rounding of the lattice origins is untouched. Neither paper
  addresses the last one; both hand it to a quantization.
- **A conformal seed has a price near the cones.** The smallest spacing factor is 0.11-0.49 of the target on the shapes
  with cones at the default (0.06-0.41 on the finer runs), with a p5-p95 range of 0.55-1.76. The capsule shows it best:
  unclamped 704 faces for 1089 and 54 boundary vertices; clamped 69 irregular vertices for 91.
- **Genus above 0 with cones: the parallel field does not close up** (9-21 degrees on the torus without alignment,
  1 degree on the genus 2 shape; 0 on every genus 0 shape and on the aligned torus, which has no cones). A conformal
  metric has no holonomy constraints; the Penner papers add them (2g angles) and leave the conformal class. The effect
  on the counts is not clear from 6 cells: the clamped oracle is ahead of the library's size field on all of them
  (torus 31 / 55 / 28 against 35 / 78 / 38, genus 2 51 / 86 / 49 against 59 / 111 / 74).
- **The filleted box hardly moves** (16 / 16 / 16 / 14): its 8 cones already sit where the curvature is.
- **The frame rotation of a conformal seed is small on these shapes**: p95 at most 4.1 degrees on the shapes without a seam
  defect, 10 degrees on the torus without alignment. On smooth shapes the direction freedom that the rectangular model
  protects is hardly used.

### 3.3 The curl mass, and the two-scale model

The curl mass of QUAD_REMESHING.md 4.5 (lattice units) on the triangles away from the cones, default resolution. The
four middle columns are the prototype's own damped Gauss-Newton: "control" with one log spacing per vertex (the
library's model, but with the prototype's clamp of 4 and triangle set), "two scales" with one per axis (the unknowns of
the rectangular model, as least squares: a capacity measurement, not the paper's constrained solve). At most 40 steps;
most two-scale solves stop at that limit, so their numbers are upper estimates.

| shape | uniform | library Scale | library ScaleAndRotation | control: one scale | control: one scale + rotation | two scales, directions fixed | two scales + rotation | conformal oracle | axis ratio p95 / max (two scales + rotation) |
|---|---|---|---|---|---|---|---|---|---|
| sphere | 61.8 | 26.5 | 25.3 | 17.0 | 14.5 | 18.0 | 11.4 | 11.5 | 1.27 / 2.50 |
| torus | 67.1 | 31.1 | 26.6 | 26.0 | 21.5 | 23.6 | 15.7 | 31.2 | 1.21 / 1.71 |
| torus, alignment 10 | 88.9 | 13.9 | 12.1 | 13.9 | 12.1 | 16.3 | 10.6 | 13.6 | 1.55 / 1.75 |
| bumpy sphere | 132.2 | 135.6 | 99.5 | 119.0 | 47.1 | 72.3 | 31.8 | 23.2 | 1.28 / 4.23 |
| filleted box | 25.7 | 29.8 | 23.7 | 22.6 | 18.4 | 21.4 | 14.9 | 23.0 | 1.11 / 1.81 |
| noisy sphere | 95.9 | 68.9 | 56.7 | 54.6 | 48.1 | 48.6 | 35.2 | 95.6 | 1.42 / 3.86 |
| capsule | 69.0 | 86.8 | 75.7 | 51.8 | 48.4 | 49.6 | 47.7 | 11.5 | 1.08 / 2.69 |
| genus 2 | 124.8 | 52.5 | 41.1 | 44.8 | 34.1 | 38.4 | 26.8 | 39.6 | 1.21 / 1.91 |
| capped cylinder | 87.0 | 77.7 | 65.5 | 65.2 | 51.7 | 55.7 | 36.3 | 36.7 | 1.42 / 3.09 |

- **The second scale is worth a factor of 1.0 to 1.5 in the curl mass**, against the control with the same solver
  (two scales + rotation against one scale + rotation: 9 of 9 lower, from 47.7 / 48.4 on the capsule to 31.8 / 47.1 on
  the bumpy sphere), with axis ratios of 1.1-1.6 at p95. With the directions fixed it is lower than its control on 7 of
  9 and higher on 2 (sphere, aligned torus), which a larger model cannot be at the optimum: those solves are not
  converged. The comparison against the library's columns would overstate it (1.1 to 3.1), because the prototype's
  solver settings alone account for most of that (control 14.5 against the library's 25.3 on the sphere).
- No mesh was extracted for two scales: `PositionField` has one spacing per vertex, and a second one is a change of
  `PositionField`, the extraction's collapse length and the 3B repair, which this spike must not touch.
- **The oracle is not curl-free in this measure, and the measure does not predict its meshes.** It is extrinsic (vertex
  normals and tangent planes). The oracle's curl mass is 11.5-39.6 on the smooth shapes and 95.6 on the noisy sphere
  (uniform 95.9), where it gives the second best mesh of the row (31 irregular vertices for 92; unclamped 21). A
  metric-based seed cannot be gated by `SizeAdaptationMinGain` on this curl mass; it needs an intrinsic measure or no
  gate.

### 3.4 Pure quads (exploratory)

The stage 3B repair, an internal class written for a uniform spacing, was run by reflection on the seeded lattices.
A run counts as clean only with the independent checks of `benchmarks/QuadFlow3BGate` (manifold, quads only, closed,
finite, no quad facing away from the input), a one-sided Hausdorff distance of at most 0.5 s and a corner deviation p95
of at most 15 degrees; the repair's own status is not trusted. Not checked: the Euler characteristic and the number of
components. On the uniform lattice the reflection path agrees with `QuadRemesher.PureQuads` on all 27 runs.

| run (9 shapes each) | uniform | ScaleAndRotation (where applied) | oracle, unclamped | oracle, clamped |
|---|---|---|---|---|
| default | 5 | 2 of 7 | 2 | 4 |
| target edge x 0.5 | 2 | 1 of 6 | 5 | 3 |
| input edge 0.25 s | 0 | 1 of 7 | 1 | 0 |
| **sum** | **7 of 27** | **4 of 20** | **8 of 27** | **7 of 27** |

Two more runs pass everything but the corner criterion (torus on the unclamped oracle, 19.9 degrees; genus 2 on
ScaleAndRotation, 15.9). Every clean result has the cones as its irregular vertices, or 1 to 4 more (torus 13 for 12,
genus 2 20-24 for 20).

**No gain in the success rate is shown.** The seed moves some shapes to clean (the sphere, torus and genus 2 at the finer
quads) and loses others (at the default the sphere and genus 2 unclamped, and the torus in both columns). The repair is used outside its contract (its
position solve assumes one edge length), so this says neither that the combination works nor that it cannot.

Over the 54 runs of the 3B gate, success correlates only weakly with a small curl mass: 16 of the 27 runs with the
lower curl mass per face are `Success`, 9 of the 27 with the higher (`results_gate.txt`).

### 3.5 Time

One run each, 4 cores, the prototype not optimized: the oracle takes 0.14-0.59 s at 5.5-18k input vertices and
0.60-3.2 s at 22-72k. `IntegrableFrames` in the same runs: Scale 0.04-0.21 s and 0.43-2.3 s, ScaleAndRotation
0.16-0.68 s and 1.4-9.1 s. So the oracle costs about what Scale costs and less than ScaleAndRotation on the larger
inputs. The two-scale Gauss-Newton is the slowest part of the prototype and was not timed.

## 4. How others solve it

The chain of both papers and of the Bern / Aachen line: field with cones -> seamless parametrization -> quantization ->
extraction (QEx: Ebke, Bommes, Campen, Kobbelt 2013). The quantization is its own research line: Campen, Bommes,
Kobbelt, *Quantized Global Parametrization*, ACM TOG 34(6), 2015; Lyon, Campen, Kobbelt 2021 (an ILP on the T-mesh);
Heistermann, Warnett, Bommes, *Min-Deviation-Flow in Bi-directed Graphs for T-Mesh Quantization*, ACM TOG 42(4), 2023,
DOI 10.1145/3592437; Coudert-Osmont, Desobry, Heistermann, Bommes, Ray, Sokolov, *Quad Mesh Quantization Without a
T-Mesh*, CGF 43(1), 2024, DOI 10.1111/cgf.14928 (on a decimated mesh, stated as easier to implement; abstract only).
The reference implementations are GPL or AGPL (QUAD_REMESHING.md section 2), so all of it is paper only for us.
A field-aligned lattice with a seed (our route) is the Instant Meshes / QuadriFlow family, where the scale freedom is
not part of the published methods; `IntegrableFrames` is our own formulation of it.

## 5. Options

Ordered by dependency and effort, not by a measured gain per effort (only option 1 has a measured gain). Efforts as in
QUAD_REMESHING.md (S / M / L / XL); every option is opt-in and changes no default output.

**1. Cone-metric seed (effort M, risk medium).** A third `SizeAdaptation` mode for closed meshes: cones from the field,
the convex conformal solve with prescribed cone angles, the parallel field, spacing and rotation into `PositionField`,
a lower clamp of the spacing.
- Measured (clamped oracle against the best of Scale and ScaleAndRotation): irregular vertices 493 -> 407 at the default,
  1175 -> 750 with finer quads, 595 -> 336 with a finer input. Applied on every shape.
- Risks: the clamp is a trade between holes and gain; the seam at genus above 0 with cones; a gate other than the
  extrinsic curl mass; boundaries were not measured.
- Needs: an owner go for a new opt-in mode (additive API).

**2. 3B on a size field (effort M, risk medium).** Make `QuadFlowRepair` correct for a spacing per vertex and allow
`PureQuads` with `SizeAdaptation`.
- Measured: nothing in its favour yet (7-8 of 27 clean on the seeded lattice, 7 of 27 uniform), with the repair
  outside its contract.
- Risks: the position solve and the checks assume one edge length; the 54-run gate has to be measured again.
- Needs: an owner decision (the combination is `InvalidInput` by decision today); the quad worker's files.

**3. Holonomy constraints for genus above 0 (effort L on top of 1, risk medium-high).** Capouellez, Zorin 2024: shear
coordinates, 2g dual loops, the minimum-norm Newton step; without flips first, Ptolemy flips for bad input later.
- Expected: closes the seam of 9-21 degrees (torus) and 1 degree (genus 2). The gain in irregular vertices is not
  measured, and the 6 cells of 3.2 do not show a deficit of the seed at genus above 0.
- Risks: the Jacobian through flips, loops rerouted through flips, no convergence proof beyond genus 0.

**4. Two scales, the rectangular model as a seed (effort L, risk high).** A second spacing per vertex in
`PositionField`, the extraction and 3B; the solver as least squares or the paper's KKT Newton.
- Measured: a curl mass 1.0 to 1.5 times below the one-scale control, axis ratio p95 1.1-1.6; no mesh. Its own value
  is keeping feature directions, which the benchmark cannot show.
- Risks: anisotropic cells through three stages; an indefinite solver for the paper's method; nonconvex, no existence
  theorem.
- Needs: a sharp-feature benchmark (stage 4) first.

**5. The full chain (effort XL several times, risk high).** Feature-aligned Penner parametrization, quantization,
QEx-style extraction, replacing the position field.
- Expected: the floor, irregular vertices = cones (88 for 407-863 at the default), pure quads by construction, exact
  feature alignment.
- Risks: a hard-aligned field (Bommes 2009), the symmetric double cover, overlay and layout, a quantizer and an exact
  extractor, all new and all from papers with GPL-only references.
- Needs: an owner decision on direction; it is larger than stages 1-3 together.

**6. Stop.** Keep `IntegrableFrames` as it is.

An observation outside the options: the control columns of 3.3 reach a lower curl mass than the library's
`IntegrableFrames` with the same model (sphere 14.5 against 25.3, bumpy sphere 47.1 against 99.5), with a wider clamp
(4 for 2.5), the cone one-rings left out and up to 40 steps. Whether that gives better meshes was not measured; it would
be a cheap measurement on the quad worker's side.

## 6. Leaning

**Option 1 as a small opt-in follow-up, after the quad integration PRs (#1229, #1293, #1231, #1236, #1234) are on
master. Not options 3, 4 or 5 now. Stopping (6) is also defensible.**

- Option 1 is the common core of both papers and the only part with a gain measured end to end through the library's
  own extraction: 17 to 44 % fewer irregular vertices than the size field at equal cell count, most with fine input or
  fine quads. It is convex, takes 3-5 Newton steps on blocks the library has, costs about what Scale costs, and runs on
  the shapes where neither mode of `IntegrableFrames` is applied.
- The gain is moderate, not a change of class: the result stays 4 to 9 times the cone count, and the pure-quad success
  rate did not move. If the goal is pure quads or the cone count, neither paper is the lever; the missing piece is the
  quantization, which is option 5 (or an improved 3B) and an owner decision on direction.
- Option 4 has its one advantage (feature directions) outside what the benchmark can show, and its measured capacity
  over one scale is small. It should wait for a sharp-feature benchmark.

For the session that studies sheaf H^1 obstructions on the same pipeline: the seam defect of the parallel field in the
flat cone metric (table 3 of the results: 9-21 degrees on the torus, 1 degree on genus 2, 0 on genus 0) is a direct
measurement of the rotational holonomy that a cone metric leaves open.

## 7. Limits of this spike

- Closed procedural shapes only; no boundaries, no sharp features, no real scans.
- The oracle fixes the cones of the library's field and moves each to a vertex. Cones that suit a conformal map can be
  poor for a rectangular one (Corman and Crane say so), and better cones would move every column.
- The causes of the remaining irregular vertices (sampling of the seed, rounding, residual curl in the position
  field's frames) were not separated.
- The unclamped oracle loses faces; its counts are flattered. The clamp of 0.7 was chosen on the same 9 shapes.
- Face counts differ between the columns: the clamped oracle is within 3 % of the uniform lattice at the default, the
  unclamped one up to 35 % below (capsule), the library's size field up to 14 % above (aligned torus, ScaleAndRotation;
  11 % for the Scale cell of the table). The per-1000-faces numbers are given for that reason.
- At genus above 0 the seam of the parallel field lies where the breadth-first tree puts it.
- The two-scale numbers are a least-squares capacity with solves that stop at the step limit, not the paper's
  constrained solve, and have no mesh behind them.
- The pure-quad table uses an internal class outside its contract.
- One run per configuration; the times are orders of magnitude.
- The two predecessor Penner papers were read second-hand; the two 2025 papers and the supplement of the first in
  full.
