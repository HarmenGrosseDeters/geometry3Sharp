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
  An idealised seed cuts the irregular vertices of the 9 closed benchmark shapes from 863 (uniform) and 493 (the
  library's size field) to 329-354, against 88 field singularities. With quads half as large: 1821 / 1175 / 647-685.
  With an input twice as fine: 756 / 595 / 205-332.
- *Pure quads:* exploratory only. The stage 3B repair gave clean pure-quad meshes on 9-10 of 27 runs on the seeded
  lattice, against 7 of 27 on the uniform one.
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
| discrete conformal map with cones | `ConeFlattening` (linearised Yamabe solve, cones placed greedily, curvature at the cones free) | prescribed cone angles of k pi / 2 need the nonlinear solve (the prototype: 150 lines, 3-5 Newton steps) |
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
vertices are exactly the cones, S = 8 on the genus 0 shapes. What can be measured cheaply is how much of the way a
*seed* goes, with the position field still doing the rounding.

The oracle of the prototype is the best isotropic seed there is, not an approximation of one:

1. the cones of the cross field (a singular face of index k/4 becomes the angle 2 pi - k pi / 2 at its vertex of
   smallest field magnitude);
2. the discrete conformal metric with exactly these cone angles (Springborn, Schröder, Pinkall 2008: Newton on the convex
   energy, fixed connectivity; residual below 1e-11 rad after 3-5 steps, no broken triangle on any of the 27 runs);
3. the cross field that is parallel in that flat metric (breadth first over the faces, one global rotation fitted to the
   input field);
4. from both, a spacing `exp(-u)` and a rotation per vertex for `PositionField`, normalized to the cell count of the
   uniform spacing; then the library's extraction and cleanup, unchanged (called by reflection; the same path with the
   library's own frames reproduces `QuadRemesher` bit for bit on every run).

Its frames close up exactly in the flat metric. What the position field still gets wrong with them is rounding and
sampling, not integrability. The second oracle column clamps the spacing at 0.7 from below, because a lattice finer
than the input triangles cannot be represented by per-vertex origins.

### 3.2 Irregular vertices (stage 2 mesh)

Default resolution (target edge x 1, input edge 0.5 s). Irregular vertices / quads %; "holes" marks a result with more
than 12 boundary vertices (faces above 6 sides are dropped by the extraction).

| shape | S | uniform | library best (Scale or ScaleAndRotation) | oracle | oracle, spacing >= 0.7 |
|---|---|---|---|---|---|
| sphere | 8 | 75 / 94.8 | 34 / 97.3 | 34 / 98.6 | 17 / 98.0 |
| torus | 12 | 76 / 94.9 | 35 / 98.0 | 36 / 97.9 | 37 / 97.6 |
| torus, alignment 10 | 0 | 106 / 93.6 | 18 / 99.0 | 28 / 98.4 | 20 / 98.6 |
| bumpy sphere | 16 | 174 / 95.9 | 126 / 97.0 | 64 / 99.4 (holes) | 73 / 97.9 |
| filleted box | 8 | 16 / 99.1 | 16 / 99.1 (not applied) | 16 / 99.4 | 19 / 99.5 |
| noisy sphere | 8 | 92 / 93.2 | 46 / 96.9 | 21 / 99.2 (holes) | 24 / 97.9 |
| capsule | 8 | 91 / 92.7 | 91 / 92.7 (not applied) | 28 / 96.6 (holes, 704 faces for 1089) | 46 / 92.3 (742 faces) |
| genus 2 | 20 | 148 / 95.1 | 59 / 98.2 | 63 / 98.0 | 52 / 98.4 |
| capped cylinder | 8 | 85 / 93.4 | 68 / 95.8 | 64 / 97.7 (holes) | 41 / 95.7 |
| **sum** | **88** | **863** | **493** | **354** | **329** |

The three resolutions (sum of the irregular vertices over the 9 shapes; S = 88 in each):

| run | uniform | library best | oracle | oracle, spacing >= 0.7 |
|---|---|---|---|---|
| default | 863 | 493 | 354 | 329 |
| target edge x 0.5 (4 x the quads) | 1821 | 1175 | 647 | 685 |
| input edge 0.25 s (4 x the input triangles) | 756 | 595 | 205 | 332 |

Reading:

- **The seed is worth about a third over the library's size field at the default resolution, and more where the
  size field struggles.** `IntegrableFrames` is not applied at all on the capsule, and on the bumpy sphere at the two
  finer resolutions. With a finer input it gains little (sphere 72 -> 52; oracle 12). The oracle does not depend on a
  Gauss-Newton iteration finding its way.
- **With an input fine enough the seed gets close to the cones** on smooth genus 0 shapes: sphere 12 for 8 cones,
  noisy sphere 10 for 8, aligned torus 15 for 0, bumpy sphere 33 for 16 (input edge 0.25 s, unclamped, no holes).
- **The rest is the position field's rounding, which neither paper addresses.** At the default resolution the seeded
  result is still 4 x the cone count. The irregular count of the oracle grows with the number of quads (354 -> 647),
  as a count of rounding defects does and a count of cones does not.
- **A conformal seed has a price near the cones.** The spacing drops to 0.06-0.5 of the target at the cone vertex and
  varies by a factor of 2 over the bulk (p5-p95 0.55-1.76). Unclamped, the lattice there is finer than the input and
  faces are lost (capsule: 704 faces for 1089, 54 boundary vertices). Clamped, the holes go and part of the gain goes
  with them on the capsule and the bumpy sphere.
- **Genus above 0 with cones needs holonomy constraints.** The parallel field of the conformal metric closes up only
  to 9-21 degrees on the smoothest torus and to 1 degree on the genus 2 shape. The seed is then no better than the
  library (torus 36 against 35). This is what the Penner papers add (2g holonomy angles), and it leaves the conformal
  class. The aligned torus (no cones) closes up exactly, as a flat torus must.
- **The filleted box does not gain** (16 everywhere): its 8 cones already sit where the curvature is.
- **The frame rotation of a conformal seed is small on these shapes**: p95 0.4-4.1 degrees on genus 0, 10 degrees on the
  smoothest torus. On smooth shapes the direction freedom that the rectangular model protects is hardly used.

### 3.3 The curl mass, and the two-scale model

The curl mass of QUAD_REMESHING.md 4.5 (lattice units) on the triangles away from the cones, default resolution. "Two
scales" is the prototype's damped Gauss-Newton with a log spacing per axis and vertex: the unknowns of the rectangular
model, solved as least squares, so a capacity measurement and not the paper's method.

| shape | uniform | Scale | ScaleAndRotation | two scales, directions fixed | two scales + rotation | conformal oracle | axis ratio p95 / max (two scales + rotation) |
|---|---|---|---|---|---|---|---|
| sphere | 61.8 | 26.5 | 25.3 | 18.0 | 11.4 | 11.5 | 1.27 / 2.50 |
| torus | 67.1 | 31.1 | 26.6 | 23.6 | 15.7 | 31.2 | 1.21 / 1.71 |
| torus, alignment 10 | 88.9 | 13.9 | 12.1 | 16.3 | 10.6 | 13.6 | 1.55 / 1.75 |
| bumpy sphere | 132.2 | 135.6 | 99.5 | 72.3 | 31.8 | 23.2 | 1.28 / 4.23 |
| filleted box | 25.7 | 29.8 | 23.7 | 21.4 | 14.9 | 23.0 | 1.11 / 1.81 |
| noisy sphere | 95.9 | 68.9 | 56.7 | 48.6 | 35.2 | 95.6 | 1.42 / 3.86 |
| capsule | 69.0 | 86.8 | 75.7 | 49.6 | 47.7 | 11.5 | 1.08 / 2.69 |
| genus 2 | 124.8 | 52.5 | 41.1 | 38.4 | 26.8 | 39.6 | 1.21 / 1.91 |
| capped cylinder | 87.0 | 77.7 | 65.5 | 55.7 | 36.3 | 36.7 | 1.25 / 1.75 |

- The second scale helps the least-squares solver on every shape (two scales + rotation is below ScaleAndRotation
  9 of 9, by a factor of 1.1 to 3.1), with axis ratios of 1.1-1.6 at p95. With the directions fixed it is still below
  ScaleAndRotation on 8 of 9. So the rectangular freedom is real on these shapes.
- No mesh was extracted for it: `PositionField` has one spacing per vertex, and a second one is a change of
  `PositionField`, the extraction's collapse length and the 3B repair, which this spike must not touch.
- **The curl mass is not a safe gate for a seed.** It is extrinsic (vertex normals and tangent planes). On the noisy
  sphere the oracle has a curl mass of 95.6 (uniform 95.9) and the best mesh of the row (21 irregular vertices for 92).
  A metric-based seed would bypass `SizeAdaptationMinGain` or needs an intrinsic measure.

### 3.4 Pure quads (exploratory)

The stage 3B repair, an internal class written for a uniform spacing, was run by reflection on the seeded lattices.
A run counts as clean only with the independent checks of `benchmarks/QuadFlow3BGate` (manifold, quads only, closed,
finite, no quad facing away from the input) and a one-sided Hausdorff distance of at most 0.5 s; the repair's own
status is not trusted. On the uniform lattice the reflection path agrees with `QuadRemesher.PureQuads` on all 27 runs.

| run (9 shapes each) | uniform | ScaleAndRotation | oracle | oracle, spacing >= 0.7 |
|---|---|---|---|---|
| default | 5 | 3 | 3 | 6 |
| target edge x 0.5 | 2 | 1 | 5 | 3 |
| input edge 0.25 s | 0 | 1 | 1 | 1 |
| **sum of 27** | **7** | **5** | **9** | **10** |

Every clean result has exactly the cones as irregular vertices, or 1 to 4 more (sphere 8, bumpy 16, genus 2 20-24).
The seed moves some shapes to clean (bumpy and noisy sphere at the default; sphere, torus and genus 2 at the finer
quads) and loses others (the sphere at the default). 27 runs and a repair used outside its contract: this says the
combination is not hopeless, not that it works. Whether 3B is correct on a size field is open (QUAD_REMESHING.md 4.5),
and its position solve assumes one edge length.

Over the 54 runs of the 3B gate, success correlates only weakly with a small curl mass: 16 of the 27 runs with the
lower curl mass per face are `Success`, 9 of the 27 with the higher (`results_gate.txt`).

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

Ranked by gain per effort. Efforts as in QUAD_REMESHING.md (S / M / L / XL); every option is opt-in and changes no
default output.

| # | option | measured or expected gain | effort | risk | needs |
|---|---|---|---|---|---|
| 1 | **Cone-metric seed**: a third `SizeAdaptation` mode. Cones from the field, the convex conformal solve with prescribed cone angles, the parallel field, spacing and rotation into `PositionField`, a lower clamp of the spacing. Closed meshes. | irregular vertices 493 -> 329-354 at the default, 1175 -> 647-685 at finer quads, 595 -> 205-332 with a finer input; works where `IntegrableFrames` is not applied (capsule, bumpy); 0.1-0.5 s at 6-18k vertices, 0.7-3 s at 22-72k (`IntegrableFrames` Scale: 5 s at 110k) | **M** | medium: holes or lost gain near the cones (the clamp is a trade); no gain at genus above 0 with cones without option 2; a gate that is not the extrinsic curl mass; boundaries not measured | owner: a new opt-in mode (additive API) |
| 2 | **Holonomy constraints** for genus above 0 (Capouellez, Zorin 2024): shear coordinates, 2g dual loops, minimum-norm Newton. Without flips first, Ptolemy flips for bad input later. | closes the torus and genus 2 seam (9-21 and 1 degrees today); expected to bring those rows to the level of the genus 0 rows; not measured | **L** on top of 1 | medium-high: the Jacobian through flips, loops rerouted through flips; no convergence proof beyond genus 0 | option 1 |
| 3 | **3B on a size field**: make `QuadFlowRepair` correct for a spacing per vertex and allow `PureQuads` with `SizeAdaptation`. | exploratory: 9-10 of 27 clean on the seeded lattice against 7 of 27 uniform, with the cones as the only irregular vertices | **M** | medium: the repair's position solve and its checks assume one edge length; the 54-run gate has to be re-measured | owner: the combination is `InvalidInput` by decision today; the quad worker's files |
| 4 | **Two scales** (the rectangular model as a seed): a second spacing per vertex in `PositionField`, the extraction and 3B; the solver as least squares (as measured) or the paper's KKT Newton. | curl mass 1.1-3.1 x below ScaleAndRotation, axis ratio p95 1.1-1.6; no mesh measured; its own value is keeping feature directions, which the benchmark cannot show | **L** | high: anisotropic cells through three stages; an indefinite solver for the paper's method; nonconvex, no existence theorem | a feature benchmark (stage 4) first |
| 5 | **The full chain**: feature-aligned Penner parametrization, quantization, QEx-style extraction, replacing the position field. | the floor: irregular vertices = cones (88 for 329-863), pure quads by construction, exact feature alignment | **XL** several times | high: a hard-aligned field (Bommes 2009), the symmetric double cover, overlay and layout, a quantizer and an exact extractor, all new and all from papers with GPL-only references | owner: a new direction, larger than stages 1-3 together |
| 6 | **Stop**: keep `IntegrableFrames` as it is. | - | - | - | - |

## 6. Leaning

**Option 1, after the quad integration PRs (#1229, #1293, #1231, #1236, #1234) are on master; then decide 3 on a
measurement with the real mode.** Reasons:

- It is the common core of both papers and the only part whose gain is measured end to end with the library's own
  extraction. It is convex, needs 3-5 Newton steps on blocks the library has, and is faster than the solver it
  complements.
- It fixes two measured weaknesses of `IntegrableFrames`: neither of its modes is applied on 2-3 of the 9 shapes, depending on the
  resolution (Scale alone: 4-5), and its gain shrinks with a finer input. A seed from the metric has neither problem.
- The remaining distance to the cones (4 x at the default) is rounding. Options 2 and 4 do not touch it; only 3 (the
  flow) and 5 (a quantizer) do. So after option 1 the next lever is 3, not a richer parametrization.

Not recommended now: option 4 before there is a sharp-feature benchmark that can show its one advantage; option 5 at
all without an owner decision on direction (it is the MIQ / QEx route that QUAD_REMESHING.md section 5 deferred, with a
better parametrization in front).

For the session that studies sheaf H^1 obstructions on the same pipeline: the seam defect of the parallel field in the
flat cone metric (table 3 of the results: 9-21 degrees on the torus, 1 degree on genus 2, 0 on genus 0) is the holonomy
obstruction measured directly, and the residual irregular vertices on a seed with zero curl are a rounding effect, not
an integrability one.

## 7. Limits of this spike

- Closed procedural shapes only; no boundaries, no sharp features, no real scans.
- The oracle fixes the cones of the library's field. Cones that suit a conformal map can be poor for a rectangular one
  (Corman and Crane say so), and better cones would move every column.
- The irregular counts of rows marked "holes" are flattered: lost faces are not counted as defects, only their boundary
  vertices of valence other than 3.
- The two-scale numbers are a least-squares capacity, not the paper's constrained solve, and have no mesh behind them.
- The pure-quad table uses an internal class outside its contract.
- One run per configuration, no timing claims beyond the order of magnitude.
- The two predecessor Penner papers were read second-hand; the two 2025 papers and the supplement of the first in
  full.
