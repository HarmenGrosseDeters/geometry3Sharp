# Quad remeshing: feasibility study and staged plan (2026-11-17)

Owner decision 2026-11-17: start the study. This note covers use cases and metrics (section 1), the method and
licence survey (section 2), a spike measurement (section 3), a staged plan with go / no-go gates (section 4), and
the limits plus a recommendation (section 5). Nothing here is library code. The spike is the benchmark project
[benchmarks/QuadRemeshSpike](../../benchmarks/QuadRemeshSpike/README.md), a prototype that is not promoted as it is.
Roadmap entries: [research.md](../roadmap/research.md) ("Later": quad remeshing), and the frame-field entry in
[sources-surveys.md](../roadmap/sources-surveys.md).

## 0. What the library already has (search, AGENT_WORKFLOW section 2)

| need | building block | fit |
|---|---|---|
| clean isotropic input | `Remesher` / `RemesherPro` (projection, `MeshConstraintUtil.PreserveBoundaryAndFeatures`, `PinSharpVertices`, `RemeshSizing`) | direct: the spike's input stage |
| cotan Laplacian | `MeshParameterization.ForEachCotanEdge` / `TriangleCotangents` (mollified cotangents), used by LSCM, SCP, BFF, cone flattening | direct; the spike uses its own clamped copy |
| sparse SPD solve | `SparseCholesky` (minimum-degree ordering, refactorization with reuse), `SparseSymmetricCG`, `SymmetricSparseMatrix` | direct: Knöppel's eigenproblem by inverse iteration, as a 2n x 2n real system (no complex type needed) |
| eigenvectors | `SymmetricEigenSolver` (dense), the inverse iteration of `SpectralConformalParameterization` | the inverse iteration pattern carries over |
| curvature | `MeshCurvature` (Meyer et al. mean and Gaussian curvature, angle deficit) | **partial**: no principal directions or shape operator, which a curvature-aligned field needs (new, small: Rusinkiewicz 2004 or Cohen-Steiner-Morvan 2003, from the papers) |
| features | `MeshTopology.CreaseEdges` / `BoundaryEdges`, crease constraints in the Remesher, `MeshChartSegmentation` | direct: the feature edges become field constraints |
| cones and cuts | `ConeFlattening`, `MeshDiscCut`, `BoundaryFirstFlattening` | only for a parametrization-based extraction (MIQ / QGP route) |
| distance and fidelity | `MeshHausdorffDistance` (certified bounds), `MeshFidelityMetrics` | direct, after triangulating the quads |
| quad storage and output | `SimpleQuadMesh` (packed quads, per-quad face group, `WriteOBJ`; quads only, no triangles or n-gons, no reader); `DMesh3` is triangle-only; `OBJWriter` and `PLYWriter` write triangles | **gap**: a quad-dominant result needs a polygon container or a triangle-pair representation (section 4, stage 5) |
| exact predicates | `math/Exact*`, `AdaptivePredicates` | only for a QEx-style extraction from a parametrization |

## 1. Use cases and target quality

Use cases: (a) CAD / CAE-style surface meshes, where quads follow the principal directions and the features (a
pure-quad or quad-dominant shell mesh for FE); (b) subdivision-ready or animation-ready meshes (pure quads, few
singularities, edge loops); (c) a nicer display and retopology of scans (quad-dominant is enough). Only (a) and
(b) need pure quads. (c) and most visual uses accept quad-dominant meshes with a few triangles or pentagons.

| property | quad-dominant target | pure-quad target |
|---|---|---|
| face mix | >= 95 % quads by count, no faces above 6 sides | 100 % quads |
| corner angles | p50 deviation from 90 degrees <= 5, p95 <= 20 | the same |
| aspect (longest / shortest edge of a quad) | p95 <= 1.5 at uniform size | the same |
| irregular vertices (valence other than 4; at the boundary, other than 3) | <= 2 x the field's singularities, plus the defects of real input | about the field's singularities (8 on a genus-0 shape with 4-RoSy +1/4 cones) |
| fidelity | symmetric Hausdorff to the input <= 0.5 x the target edge length | the same |
| alignment | quad edges along feature lines and boundaries (angle error at feature edges p95 <= 10 degrees) | the same |
| size control | uniform target edge `s`; later a sizing field (`RemeshSizing`) | the same |
| validity | a manifold polygon mesh: every edge in 1 or 2 faces, no face folded over its neighbour | the same |
| time | seconds per 100k input vertices on 4 cores | the same |

Metrics, as the spike measures them (section 3): the face histogram by number of sides; quad corner angle
deviation (p50 / p95, and the share in 70-110 degrees); quad aspect (p50 / p95); valence and the irregular vertex
count; the irregular count after one Catmull-Clark step (a cheap conversion to pure quads); the field's singular
faces with a histogram of their indices (the total equals the Euler characteristic chi by construction, so it checks
only the transport); the symmetric `MeshHausdorffDistance`
of the fan-triangulated output to the input, divided by the bounding box diagonal; the time per stage. Later
stages add the angle between quad edges and feature edges, a manifold check of the polygon mesh, and the scaled
Jacobian of each quad (the usual FE quality measure).

## 2. Method survey (papers; licences checked 2026-11-17)

Licences were read from the projects' LICENSE / COPYING files or READMEs, without reading source code ("checked").
"From memory" marks citations that were not re-verified.

| method | idea | robustness on real meshes | effort here | code and licence | risks |
|---|---|---|---|---|---|
| **Knöppel, Crane, Pinkall, Schröder, *Globally Optimal Direction Fields*, ACM TOG 32(4), SIGGRAPH 2013** | n-RoSy field as a complex vertex field `z = u^n`. The smoothest field is the smallest eigenvector of a Hermitian connection Laplacian; curvature alignment and constraints come from one linear solve (the `s` parameter). The singularities are automatic and few. | Needs a manifold mesh (vertex frames); boundaries take Neumann or fixed directions. Noise only shifts singularities a little (spike: noisy sphere still has 8). | **S-M**: cotan weights plus `SparseCholesky` are enough; the spike field is about 100 lines | reference code fieldgen (GeometryCollective) **MIT**, checked; geometry-central **MIT**, checked | Principal directions are missing for the curvature-aligned variant. A 2n x 2n real factorization is 4 x the nonzeros of a scalar Laplacian. |
| Ray, Vallet, Li, Lévy, *N-Symmetry Direction Field Design*, ACM TOG 27(2), 2008; Palacios, Zhang, *Rotational Symmetry Field Design on Surfaces*, ACM TOG 26(3), 2007 (citations from memory) | field design with explicit singularity control | - | alternatives to Knöppel; not needed first | papers | - |
| **Jakob, Tarini, Panozzo, Sorkine-Hornung, *Instant Field-Aligned Meshes*, ACM TOG 34(6) 189, SIGGRAPH Asia 2015** | Local extrinsic smoothing of an orientation field and a position field (lattice), both by Gauss-Seidel on a multiresolution hierarchy, then extraction from the integer lattice offsets with cleanup. Quad-dominant output, or pure quads after one subdivision. | Very robust: works on point clouds, noisy and non-manifold input, because it is local and needs no global parametrization. It tolerates holes and has crease handling. Output has T-junction-like defects at position-field singularities. | **M-L**: the spike reached a usable quad-dominant result in about 400 lines; the cleanup and the boundary handling are the rest | instant-meshes **BSD-3 plus an "Enhancements" clause** (LBNL-style: Enhancements made publicly available are licensed back to the authors), checked. Its `.gitmodules` lists nanogui, pcg32, dset, tbb and pss (wjakob forks), and the repository states no licences for them. From the projects themselves: nanogui BSD-3, tbb Apache-2.0, pss MIT and rply MIT-style (checked); pcg32 Apache-2.0 (from the QuadriFlow README); dset not checked. No GPL found. | Owner 2026-11-17: paper only, the Enhancements clause is not accepted. Determinism: the paper's parallel Gauss-Seidel uses a graph colouring, so it is deterministic only with a fixed colouring and order. |
| **Huang, Zhou, Niessner, Shewchuk, Guibas, *QuadriFlow: A Scalable and Robust Method for Quadrangulation*, CGF 37(5), SGP 2018** | Instant-Meshes-style fields, plus a min-cost network flow that removes the position-field singularities (and an SAT step for the orientation singularities). Pure quads are the goal. | Robust like Instant Meshes; fewer singularities. Its watertightness is still not guaranteed. | **L**: needs a min-cost flow solver (new; Boost-licensed Lemon in the original) | QuadriFlow: LICENSE.txt is **BSD-3-style**, checked, without the Enhancements clause; the README says "MIT" (an inconsistency to note). It depends on Lemon (Boost), Eigen (MPL-2; the LGPL parts are avoided with `BUILD_FREE_LICENSE`), pcg32 (Apache), pss (MIT). | It is the cleanest permissive path to the spike's main gap (position singularities). Porting needs the same per-file check. |
| **Bommes, Zimmer, Kobbelt, *Mixed-Integer Quadrangulation*, ACM TOG 28(3) 77, SIGGRAPH 2009** (MIQ) | A seamless global parametrization aligned to a cross field, with integer constraints solved by greedy rounding; quads come from the integer grid. | Sensitive to bad meshes (needs a cut graph, cones, a valid parametrization); flips can occur. Highest quality on clean CAD-like input. | **XL**: a mixed-integer solver, cut graph and cone handling (`ConeFlattening` and `MeshDiscCut` help) | CoMISo **GPL-3**, checked; libigl's MIQ is in `igl/copyleft/comiso` (GPL), checked. Paper only. | Greedy mixed-integer rounding can fail or need many re-solves. Only GPL references exist to compare against. |
| **Ebke, Bommes, Chen, Kobbelt, *QEx: Robust Quad Mesh Extraction*, ACM TOG 32(6) 168, SIGGRAPH Asia 2013** | A robust extraction of the quad mesh from an integer-grid map, including small flips and degeneracies, using exact predicates in the parameter domain. | Robust given a parametrization; useless without MIQ / QGP. | **L**, on top of a parametrization; our exact predicates (`math/Exact*`, adaptive orient2d) fit the parameter-domain tests | libQEx **GPL-3** (commercial on request), checked. Paper only. | Only pays off with the XL parametrization route. |
| Campen, Bommes, Kobbelt, *Quantized Global Parametrization*, ACM TOG 34(6) 192, SIGGRAPH Asia 2015 | Quantizes a T-mesh of the field instead of greedy rounding: always valid integer constraints | as MIQ, more reliable | XL | no code found | - |
| **Lyon, Campen, Kobbelt, *Quad Layouts via Constrained T-Mesh Quantization*, CGF 40(2):305-314, EG 2021** | A coarse conforming quad layout (patches) by an ILP on the T-mesh, with bounded angle distortion | layout level; needs a field, a T-mesh and an ILP solver | XL (an ILP solver is not in the library) | no code found | ILP solver dependency |
| **Pietroni, Nuvoli, Alderighi, Cignoni, Tarini, *Reliable Feature-Line Driven Quad-Remeshing*, ACM TOG 40(4), SIGGRAPH 2021** (QuadWild) | Feature lines, then a field, then tracing into patches, then quadrangulation of each patch by an ILP. Reliable on CAD-like input. | Reliable on its test set; needs feature lines and an ILP | XL | quadwild **GPL-3**, checked; needs Gurobi (commercial) and optionally CoMISo (GPL). Paper only. | The ILP solver and Gurobi. Heistermann, Warnett, Bommes, *Min-Deviation-Flow in Bi-directed Graphs for T-Mesh Quantization*, ACM TOG 42(4), SIGGRAPH 2023 replaces the ILP with a flow (quadwild-bimdf **GPL-3**, checked; paper only). |
| Remacle et al., *Blossom-Quad*, IJNME 89:1102-1119, 2012 | Pairs the triangles by a min-cost perfect matching, then one subdivision for pure quads | Works on any triangle mesh. Quality is bounded by the triangulation: isotropic triangles give 60 / 120 degree rhombi. | **S** (greedy) to **M** (blossom matching) | gmsh GPL-2+ with a linking exception (from the docs, not checked directly); Blossom V is research-only. Paper only. | Low angle quality (spike: p50 26 degrees off 90). |
| Catmull-Clark split of any polygon mesh | one subdivision step: every n-gon becomes n quads | always works | **S** | Catmull, Clark 1978 (paper) | The irregular count grows by one per non-quad face; the size halves. |

Surveys: Bommes, Lévy, Pietroni, Puppo, Silva, Tarini, Zorin, *Quad-Mesh Generation and Processing: A Survey*, CGF
32(6):51-76, 2013; Vaxman, Campen, Diamanti, Panozzo, Bommes, Hildebrandt, Ben-Chen, *Directional Field Synthesis,
Design, and Processing*, CGF 35(2):545-572, EG 2016 STAR. libdirectional (Vaxman et al.) is "primarily MPL-2", per
its site (checked). MPL-2 is file-level copyleft, so do not port it. geogram (BSD-3, checked) documents only
triangle remeshing (`remesh_smooth`, isotropic and anisotropic 6D CVT; see
[GEOGRAM_SURVEY_2026-11.md](../research/GEOGRAM_SURVEY_2026-11.md) A5) and has no quad or frame-field code.

Summary of the licence situation: the only permissive end-to-end pipelines are **Instant Meshes** (BSD-3 plus the
Enhancements clause) and **QuadriFlow** (BSD-3 / MIT). For direction fields, Knöppel's fieldgen and
geometry-central are MIT. Everything from the Bommes / Kobbelt / Campen / Pietroni line is GPL (CoMISo, libQEx,
QuadWild, Bi-MDF, libigl copyleft) or has no code, so those methods can only be implemented from the papers.

## 3. Spike: field, position field, extraction (measured 2026-11-17)

Setup: [benchmarks/QuadRemeshSpike](../../benchmarks/QuadRemeshSpike/README.md) on master
5be24c54901f70a71501a4398ca66a338ead4771 (#1075) plus this branch. net10.0 Release, single-threaded, a 4-core
Xeon at 2.1 GHz (cloud container), after one untimed warm-up run of the whole pipeline. Procedural inputs:
`MarchingCubes`, then 20 `Remesher` passes at edge `h = s / 2`. Shapes and quad edge `s`:
- sphere (r = 1, `s` 0.1);
- torus (R = 1, r = 0.35, `s` 0.1);
- bumpy sphere (r = 1 + 0.08 sin 4x sin 4y sin 4z, `s` 0.06);
- filleted box (1 x 0.7 x 0.5 half extents, fillet radius 0.15, `s` 0.08);
- noisy sphere (vertices moved +-0.2 h along the normal).

The `s` values are 1.6-3 % of the bounding box diagonal. Hausdorff is measured against a remesh at `h / 2`. The
symmetric value includes the input's distance to the holes (defect faces are not triangulated). The one-sided value
from the output to the reference measures the faces that exist. Command: `dotnet run -c Release --project
benchmarks/QuadRemeshSpike` (default `sweeps=40`).

| shape | method | input V | singular faces (index x count); field eigenvalue | sum index | faces | quads % | tris | 5-6 | defects (<3 or >6 sides) | non-manifold tris | irreg. V | irreg. after 1 CC | angle dev p50 / p95 (deg) | corners in [70,110] % | aspect p50 / p95 | Hausdorff sym. / diag | Hausdorff out->ref / diag | t field | t pos | t extract (s) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| sphere | field+pos (IM-style) | 6390 | 8 (+1/4 x8); lambda 4, last change 1.5E-08 | 2.00 | 1280 | 93.6 | 79 | 3 | 0 | 0 | 84 | 166 | 2.4 / 14.8 | 97.0 | 1.07 / 1.40 | 0.0025 | 0.0025 | 0.30 | 0.42 | 0.02 |
| sphere | tri-pairing (s) | 1591 | - | - | 1764 | 80.2 | 350 | 0 | 0 | 0 | 438 | 788 | 26.5 / 35.6 | 16.7 | 1.15 / 1.40 | 0.0026 | 0.0026 | 0.00 | 0.00 | 0.10 |
| torus | field+pos (IM-style) | 7026 | 12 (-1/4 x6, +1/4 x6); lambda 4.74, last change 0.00049 | 0.00 | 1385 | 94.7 | 69 | 5 | 0 | 0 | 75 | 149 | 2.4 / 12.8 | 97.9 | 1.06 / 1.35 | 0.0036 | 0.0036 | 0.32 | 0.27 | 0.02 |
| torus | tri-pairing (s) | 1767 | - | - | 1955 | 80.8 | 376 | 0 | 0 | 0 | 483 | 859 | 26.1 / 35.2 | 17.6 | 1.15 / 1.39 | 0.0049 | 0.0049 | 0.00 | 0.00 | 0.12 |
| bumpy | field+pos (IM-style) | 17918 | 8 (+1/4 x8); lambda 4.03, last change 0.00054 | 2.00 | 3507 | 96.6 | 111 | 7 | 0 | 0 | 133 | 251 | 1.7 / 10.0 | 97.9 | 1.05 / 1.32 | 0.0018 | 0.0018 | 0.94 | 0.66 | 0.01 |
| bumpy | tri-pairing (s) | 4471 | - | - | 4994 | 79.0 | 1050 | 0 | 0 | 0 | 1382 | 2432 | 26.6 / 35.8 | 17.4 | 1.16 / 1.40 | 0.0022 | 0.0022 | 0.00 | 0.00 | 0.43 |
| filleted-box | field+pos (IM-style) | 9092 | 8 (+1/4 x8); lambda 1.32, last change 1E-15 | 2.00 | 1705 | 98.9 | 18 | 0 | 0 | 0 | 18 | 36 | 1.4 / 5.6 | 99.3 | 1.02 / 1.15 | 0.0062 | 0.0062 | 0.35 | 0.30 | 0.00 |
| filleted-box | tri-pairing (s) | 2279 | - | - | 2504 | 81.9 | 454 | 0 | 0 | 0 | 583 | 1037 | 25.8 / 34.7 | 19.0 | 1.16 / 1.41 | 0.0101 | 0.0101 | 0.00 | 0.00 | 0.11 |
| noisy-sphere | field+pos (IM-style) | 6390 | 8 (+1/4 x8); lambda 5.57, last change 0.00045 | 2.00 | 1270 | 94.3 | 71 | 1 | 0 | 0 | 81 | 153 | 2.7 / 15.1 | 96.9 | 1.07 / 1.38 | 0.0050 | 0.0050 | 0.23 | 0.24 | 0.00 |
| noisy-sphere | tri-pairing (s) | 1591 | - | - | 1767 | 79.9 | 356 | 0 | 0 | 0 | 519 | 875 | 25.9 / 34.8 | 19.4 | 1.15 / 1.38 | 0.0077 | 0.0077 | 0.00 | 0.00 | 0.09 |

Notes on the table:
- The tri-pairing rows' time includes their remesh.
- The noisy sphere's Hausdorff is measured against the clean sphere, so it includes the noise.
- "Field eigenvalue" is the Rayleigh estimate after 30 inverse iterations; "last change" is its relative change in
  the last iteration. The torus, the bumpy sphere and the noisy sphere are not fully converged (5e-4); the
  singularity structure is the same in every sensitivity run below.
- "Non-manifold tris" are fan triangles of the output that `DMesh3` rejects (a non-manifold edge).
- Valence is counted from the faces with 3-6 sides, so the vertices on the border of a defect face count as
  irregular.
- The position field is the paper-only version of section 3.1. A first version of the spike instead followed the
  Instant Meshes reference implementation's functions as recalled; the owner ruled that out on 2026-11-17 (section 4)
  and it was replaced. Against that version the paper-only one has:
  - 0.2-1.6 points fewer quads;
  - between 1 fewer and 23 more irregular vertices (sphere 62 -> 84, filleted box 7 -> 18);
  - no defect faces (3 before) and no non-manifold triangles (2 before);
  - a similar one-sided Hausdorff;
  - a position field 2.5-4 x faster (two roundings instead of a 4 x 4 search).

Sensitivity runs (same command with one parameter changed; field + pos rows):
- **The regularizer** `eps` of the middle point (0.01 and 0.2 instead of 0.05) barely matters: quads 93.8-99.0 %,
  irregular vertices 20-144, at most 1 defect face, at most 2 non-manifold triangles (the sphere at
  `eps=0.01`), one-sided Hausdorff 0.20-0.67 %.
- **Sweeps** barely matter either; the hierarchy does the work. With 10 sweeps per level: quads 93.7-98.5 %,
  irregular 26-140, and position time 0.07-0.21 s. With 100: quads 93.8-98.8 %, irregular 16-139, and 0.55-1.57 s.
- **Finer input** (`ratio=0.33`, 14k-41k vertices): quads 94.2-98.3 %, irregular 28-124. The noisy sphere has 2
  non-manifold triangles. The field takes 0.7-3.7 s and the position field 0.5-1.7 s.
- **Half the quad size** (`scale=0.5`, 25k-36k vertices): sphere 97.2 % quads, 139 irregular vertices, p95
  deviation 7.8 degrees; filleted box 99.3 %, 47 irregular; noisy sphere 96.9 %, 182 irregular. The field takes
  1.6-3.4 s and the position field 1.0-1.3 s.

Findings:
1. **The field works.** Knöppel's smoothest field on our cotan weights with `SparseCholesky` has exactly 8 singular
   faces, each of index +1/4, on the sphere, the bumpy sphere, the filleted box and the noisy sphere. That is the
   minimum for a 4-RoSy field on genus 0. (The total index equals chi on every shape, but that total depends only on
   the connection, not on the field: the edge terms cancel over a closed mesh, so it checks the transport, not the
   field. The per-face histogram is the field-dependent evidence.) On the torus the smoothest field has 12 singular
   faces (6 of +1/4, 6 of -1/4), not the singularity-free field that follows the principal directions. That is the
   known reason for a curvature-aligned variant (Knöppel's `s` parameter), which needs principal directions
   (section 0: missing). Time: 0.2-0.9 s at 6k-18k vertices and 3.7 s at 41k vertices, unoptimized (a 2n x 2n real
   Cholesky and 30 inverse iterations without a residual stop).
2. **A paper-only position field plus a naive extraction already give a usable quad-dominant mesh.**
   - 93.6-98.9 % quads;
   - corner deviation p50 1.4-2.7 and p95 5.6-15.1 degrees;
   - aspect p95 1.15-1.40;
   - no defect faces and no non-manifold triangles in the default run;
   - Hausdorff 0.18-0.62 % of the diagonal, about 0.09-0.20 `s` (symmetric and one-sided are equal without holes;
     the largest is on the filleted box, and the noisy sphere's includes the noise);
   - position field 0.24-0.66 s.

   On the filleted box (CAD-like, smooth features): 18 triangles and 18 irregular vertices, against 8 field
   singularities. The sensitivity runs still show sporadic defect faces and non-manifold triangles (at most 2), so
   the validity target of section 1 needs the stage 2 cleanup.
3. **The main gap is the position-field singularities.** Irregular vertices are 75-133 on the curved shapes,
   against 8 field singularities (or 0 on the torus). Most of them are triangles balanced by valence-5 vertices.
   On the sphere there are 79 triangles but only 3 faces with 5-6 sides. By Euler (sum of (4 - valence) + sum of
   (4 - sides) = 4 chi), the remaining degree deficit sits in about 68 net valence-5 vertices. The paper's
   extraction cleanup and QuadriFlow's min-cost flow target exactly this. The spike has neither.
4. **Triangle pairing is no path to quality.** On isotropic triangles it gives rhombi: about 80 % quads, but p50
   26 degrees off 90 and only 17-19 % of corners within 70-110 degrees. Per face it has 23-29 % irregular
   vertices, against 1.1-6.6 % for the field route: 4-7 x more on the curved shapes and 22 x on the box. Its
   Hausdorff is no better (0.22-1.01 %). It is fine as a fallback representation, not as a remesher.
5. **Pure quads by one Catmull-Clark step** roughly double the irregular count (36 on the box, 149-251 elsewhere)
   and halve the edge length. That is not acceptable as the pure-quad path; pure quads need stage 3 proper.
6. **Not measured:** open boundaries, sharp (non-filleted) features, non-manifold or broken input, anisotropic or
   graded sizing, real scans, parallel speed and large inputs (>100k vertices). Those risks carry into stages 2-4.

### 3.1 Derivations (papers only)

Cross field (Knöppel, Crane, Pinkall, Schröder 2013, the vertex-based discretization):
- Each vertex i has a tangent frame (e_i, b_i = n_i x e_i). Here n_i is the area-weighted normal, and e_i is the
  first neighbour edge projected into the tangent plane.
- A 4-RoSy field is the complex number z_i = u_i^4, where u_i is a unit tangent vector in that frame.
- The angle of edge ij in i's frame is theta_ij = atan2(d.b_i, d.e_i) with d = p_j - p_i. The discrete Levi-Civita
  transport from i to j adds rho_ij = theta_ji + pi - theta_ij, the same direction measured in j's frame.
- The smoothness energy is E(z) = sum_ij w_ij |z_j - r_ij z_i|^2 with r_ij = exp(4 i rho_ij) and the cotan weights
  w_ij (clamped to 1e-3 in the spike).
- E is a Hermitian form z^H A z. Its diagonal entries are the summed weights, and A_ji = -w_ij r_ij.
- The smoothest unit field minimizes E / (z^H M z), with M the diagonal of vertex areas. This is the smallest
  eigenvector of A z = lambda M z, the paper's main result.
- In real form z = x + i y, A = R + i I becomes [[R, -I], [I, R]]. Each edge then adds -w cos to (j, i) and
  (n+j, n+i), +w sin to (j, n+i) and -w sin to (i, n+j).
- The spike runs inverse iteration with a mass shift of 1e-6 relative to the mean area. This is our own choice; it
  only makes A definite.
- The index of face f is (sum over its edges of wrap(arg z_j - arg z_i - 4 rho_ij) + 4 wrap(sum rho)) / (2 pi 4).

Position field (Jakob, Tarini, Panozzo, Sorkine-Hornung 2015):
- The position field stores per vertex a point o_i in the tangent plane. It stands for the lattice
  {o_i + s (a q_i + b t_i)}, with a and b integers, t_i = n_i x q_i, and q_i the orientation field.
- The paper smooths o by repeatedly replacing o_i with the average of its neighbours' lattice points, translated
  into i's lattice, and by keeping o_i at the lattice point nearest to v_i. The spike derives each step as follows.

The steps:
- **Orientation compatibility.** The 4-RoSy at j is {+-q_j, +-t_j}. Its representative compatible with q_i is the
  one with the largest dot product with q_i. The spike keeps q_i fixed and rotates only j's field; this is its own
  choice and is asymmetric.
- **Middle point.** The lattices of i and j are compared near a common point x. The spike takes x as the point
  nearest the edge midpoint m that lies in both tangent planes (approximately, once
  regularized).
  - Write x = m + alpha n_i + beta n_j. The two plane constraints then give [1 c; c 1] (alpha, beta) = (r_i, r_j),
    with c = n_i.n_j and r_k = n_k.(p_k - m).
  - The system is singular for parallel planes. The paper gives no formula, so the spike chooses a Tikhonov term:
    1 + eps on the diagonal, with eps = 0.05. Exactly parallel planes then give x = m. The shift stays bounded,
    and 0.01 to 0.2 give the same results (sensitivity above).
- **Position compatibility.** Round x to the nearest point a of i's lattice. Then round a to the nearest point b of
  j's lattice: two nearest-point roundings, the spike's own choice. The integer offset of the two lattice origins
  is the difference of the two integer coordinate pairs.
- **Smoothing.** For vertex i, minimize sum_j |(a_j + delta) - b_j|^2 over a shift delta, where a_j is i's
  lattice point for edge ij. The rounding is fixed
  near the optimum, so shifting o_i shifts every a_j by delta. The minimizer is delta = mean_j (b_j - a_j), with
  uniform weights (own choice). Then project o_i + delta into i's tangent plane and round it to the lattice point
  nearest v_i. Gauss-Seidel in vertex order makes the result deterministic.
- **Hierarchy.** Greedy matching merges neighbour pairs, the closest first (own choice). A coarse vertex gets the
  area-weighted position and normal, and the area-weighted compatible orientation. Each level is solved
  coarse-to-fine, and the coarse o is prolonged by projection and rounding.
- **Extraction.** Union-find merges the vertices of edges with zero offset. Unit offsets become edges. Faces are
  traced in the tangent planes.

## 4. Staged plan

Each stage is a separate PR series that adds its sources to [SOURCES.md](../SOURCES.md) (Knöppel et al. 2013 in stage 1, Jakob et al. 2015 in stage 2), with the usual tests (CLAUDE.md: invariance over `Similarity.All()`, reuse,
determinism, the 5 s test rule) and a benchmark row in `benchmarks/`. A stage starts only after the go of the one
before it.

| stage | content | effort | risk | go / no-go gate |
|---|---|---|---|---|
| **0. Decisions** | The owner decisions below. | - | - | Decided 2026-11-17. |
| **1. Cross field** | A `CrossField` class (additive): Knöppel 2013 smoothest field and curvature-aligned field, with boundary and feature-edge alignment as constraints (one linear solve). Principal directions (Rusinkiewicz 2004, from the paper) as a small new block. Per-face singularity indices. Manifold input only (reports `NotManifold`). | **M** (about 1-2 worker sessions) | low: the spike already matches theory | On the procedural suite (plus open shapes and sharp-feature boxes): the per-face index histogram matches theory (8 x +1/4 on genus 0); singularities <= ideal + 4 on the sphere, box and torus (curvature-aligned torus: 0); <= 2 s per 50k vertices on 4 cores (measured 3.7 s at 41k single-threaded; levers: a residual stop instead of a fixed 30 iterations, an n x n complex Hermitian factorization instead of the 2n x 2n real form, and a nested-dissection ordering); bit-for-bit under 2^k scales; deterministic. No-go if the curvature alignment fails on the torus or noisy inputs. |
| **2. Position field and extraction (quad-dominant)** (built; merged with the irregular-vertex and 95 % quad gates open, owner 2026-11-17: [4.2](#42-stage-2-status-2026-11-17-built-the-irregular-vertex-gate-is-the-no-go)) | The Instant Meshes position field on a hierarchy, implemented from the paper only (owner 2026-11-17). The paper's extraction with cleanup (collapse of near-coincident points, removal of valence-2 vertices, T-junction fixes). Boundaries as lattice constraints. Output into the stage 5 type. | **L** (about 2-3 sessions) | **medium**: position singularities (spike finding 3); determinism of a parallel Gauss-Seidel (needs a fixed colouring) | On the suite: >= 95 % quads; no face above 6 sides; irregular <= 2 x field singularities + 4 (restated 2026-11-19 relative to the curl mass, [4.5](#45-the-irregular-vertex-gate-restated-and-the-size-field-owner-2026-11-19)); corner p95 <= 15 degrees; Hausdorff <= 0.5 `s`; a manifold polygon mesh; <= 5 s per 100k input vertices on 4 cores (a benchmark target; the tests use inputs small enough for the 5 s test rule). No-go: irregular > 4 x singularities after the cleanup means stage 3 (flow) becomes a prerequisite. |
| **3. Pure quads** | Option A: one Catmull-Clark split (S; doubles the irregular count). Option B: the QuadriFlow min-cost flow on the position field (BSD reference; a new flow solver), then pure quads by construction. Option C: local topology cleanup (collapses, rotations; Bommes, Lempfer, Kobbelt 2011, from memory). | A: S; B: L; C: M | B: a flow solver; C: no termination guarantee | 100 % quads; irregular <= field singularities + 10 % of stage 2's; the stage 2 quality gates still hold. No-go for B if the flow cannot reach it within max(3 x stage 2, 1 s) on clean runs (owner 2026-11-19, replacing 2 x stage 2; [4.4](#44-stage-3b-built-2026-11-19)). |
| **4. Features** | Sharp edges (`MeshTopology.CreaseEdges`, Remesher constraints) as field constraints and position-field snapping. Corners as forced singularities. Feature curves resampled first (see [REMESHER_CREASE_SLIVERS.md](REMESHER_CREASE_SLIVERS.md)). | **L** | **high**: noisy crease networks break into fragments (the same finding as the remesher); QuadWild-quality alignment needs patch tracing (XL) | On sharp CAD boxes, cylinders and gears: quad edges along > 95 % of feature length with angle error p95 <= 10 degrees; Hausdorff at the feature edges <= 0.1 `s`. No-go means smooth-only quad remeshing ships (features as a documented limit). |
| **5. API and writers** | Additive and compatible with 1.0.324: a new polygon container (`DPolyMesh`: compact face offsets plus vertex lists, face groups, `ToDMesh3()` that triangulates and records the triangle-pair map), OBJ polygon faces and PLY `vertex_indices` lists for n-gons, and `SimpleQuadMesh` from a pure-quad result. A `g3cli quadremesh` command. No change to `DMesh3`. | **S-M** | low; `dotnet pack` API validation catches breaks | The API validation passes; a round trip of OBJ and PLY through the readers keeps the faces (the polygon reader paths are added to `ReaderFuzzTests` if a reader is added). |

### 4.1 Stage 5 as built: `DPolyMesh` and `PolyMeshIO` (2026-11-17)

Additive, nothing of `DMesh3`, `PLYWriter`, `OBJWriter` or the standard readers changes in behavior (the `PLYReader` gives faces
whole to a builder that implements the new `IPolygonMeshBuilder`; every other builder gets the same fan triangles as before).
The types live in `mesh/SimpleQuadMesh.cs` and `io/MeshIOUtil.cs`, so that no project file changes.

- **`DPolyMesh`**: vertices as `x y z` doubles; faces as `FaceOffsets` (FaceCount + 1 entries), `FaceIndices` and one `FaceGroups`
  int per face (default 0). Construction for a producer such as stage 2: `new DPolyMesh(vertexCap, faceCap, indexCap)`, then
  `AppendVertices(xyz)` and `AppendFaces(counts, indices, groups)` (whole arrays, amortized O(1) per entry, no per-face allocation),
  or `AppendTriangle`, `AppendQuad`, `AppendFace(list, group)` one at a time. Invalid input (fewer than 3 vertices, repeated or
  out-of-range indices, non-finite coordinates) is never thrown for: `Validate()` returns a `PolyMeshReport`; the conversions and
  writers skip the faces that cannot be used.
- **Conversions**: `ToDMesh3(out triangleToFace, skippedFaces)` (all vertices keep their index as the `DMesh3` vertex id; triangle
  groups = face groups; triangles in face order, so the map is non-decreasing), `DPolyMesh.FromDMesh3(mesh)`,
  `TryToSimpleQuadMesh(out quads)` (all faces quads with valid indices; positions and face groups).
- **Triangulation rule** (deterministic, documented on the class): a triangle is itself; larger faces are ear-clipped in the plane of
  the Newell normal, scanning the corners 1, 2, ..., n-1, 0 and cutting the first convex corner whose triangle holds no other vertex
  of the remaining ring (boundary counts as inside; vertices at the same position as an ear corner are ignored); no ear: corner 1 is
  cut anyway; zero normal, or more than 256 vertices (bounds the work on hostile input): fan from vertex 0. A convex planar face is the fan from its vertex 0. The decisions are signs of sums of
  products of coordinate differences, so a power-of-two scale of the vertices gives the same triangles bit for bit; other
  similarities give the same triangles unless a corner is within rounding of straight.
- **Writers** (`PolyMeshIO.WriteOBJ`, `WritePLY` ascii and binary little endian, `WriteFile`): doubles as shortest round-trip text
  (PLY `float` with `doublePrecision: false`), faces whole, `g mmGroup<id>` lines / a `face_group` property only if some group is
  not 0, PLY list count `uchar` or `int` for faces above 255 vertices.
- **Readers** (`PolyMeshIO.ReadOBJ`, `ReadPLY`, `ReadFile`): the faces stay whole, so `read(write(mesh))` equals `mesh` bit for bit
  (coordinates, face lists, groups). `ReadPLY` is `PLYReader` with a `DPolyMeshBuilder`; `ReadOBJ` is a small separate parser
  (positions, faces, groups; the details are on `PolyMeshIO`). Both are in `ReaderFuzzTests` (`poly-obj`, `poly-ply`).
- Not done (stage 2 and later): `g3cli quadremesh`, normals / UVs / colors on `DPolyMesh`, a polygon `StandardMeshReader` entry.

Owner decisions (2026-11-17, after this study):
1. **Scope:** quad-dominant, stages 1, 2 and 5 (about 4-6 sessions, one PR per stage). Pure quads (stage 3),
   features (stage 4), MIQ / QEx and QuadWild stay out.
2. **Port or paper:** paper only. The spike's position field was re-derived from Jakob et al. 2015 and Knöppel et
   al. 2013 (section 3.1). All later stages are implemented from the papers only, and the Instant Meshes
   Enhancements clause is not accepted.
3. **Mesh type and output (stage 5):** an additive polygon mesh type with OBJ / PLY writers and conversion to and
   from `DMesh3`, with no change to `DMesh3` or the existing API.

### 4.1 Stage 1 status (2026-11-17)

`gs.CrossField` and `gs.MeshPrincipalCurvature` are in the library (`mesh_ops/`), with
[benchmarks/CrossFieldBenchmark](../../benchmarks/CrossFieldBenchmark/README.md). Measured gates: the index histogram
matches theory (8 x +1/4 on the sphere, the filleted box and the noisy sphere; 0 on the curvature-aligned torus and on
the noisy aligned torus, 12 singular faces on the smoothest torus); bit-for-bit under 2^k scales; deterministic.

**Speed gate: met on all benchmark shapes, including the noisy torus (updated 2026-11-20; `#1136`, `#1141`, `#1143`).** Stage 1 as first merged took
5.3-6.7 s per 55-60k vertices (serial: 3.9 s of sparse Cholesky and 0.8-1.6 s of solves). The sequence that brought it down, all
bit-identical to the code before it (golden hashes in the tests): the n x n complex Hermitian factorization
(`SparseHermitianCholesky`, `#1099`), the parallel pre/post loops, bit-identical vector kernels for the dense dot products of both
Cholesky classes (four products at once, `SparseCholeskyKernels`, `SparseHermitianCholeskyKernels`, `#1103`), one shared ordering and
symbolic analysis with the structure of L computed once (`CholeskyAnalysis`), **parallel solves** (`#1136`), **parallel numeric factorization** (`#1141`), and parallel `ParamMesh` triangle frames / bowtie check (`#1143`).

Serial baseline after the vector kernels (`CrossFieldBenchmark h=0.017`, one thread, best of 3, master 464988c8 vs f4f7de6f):

| shape | field | V | solves | before | after (serial) |
|---|---|---|---|---|---|
| sphere | smoothest | 54944 | 6 | 1.61 | 1.36 |
| sphere | aligned | 54944 | 6 | 1.71 | 1.37 |
| torus | smoothest | 60712 | 14 | 2.02 | 1.72 |
| torus | aligned | 60712 | 12 | 2.02 | 1.66 |
| noisy torus | smoothest | 60712 | 34 | 3.24 | 2.90 |
| noisy torus | aligned | 60712 | 21 | 2.43 | 2.16 |

With parallel solves (`#1136`) and parallel numeric factorization (`#1141`) on 4 cores, `noisy-torus smoothest` (60,712 V, 34 solves) drops to **1.96 s** (**1.61 s per 50k vertices**, `noisy-torus aligned`: 1.48 s, `sphere smoothest`: ~0.75-0.85 s), meeting the 2 s per 50k vertices gate on every shape. The index histograms and bits are unchanged.

What remains in the solver (not scheduled, gate met): the serial approximate minimum degree ordering (0.20-0.25 s; a parallel version would have to keep the exact permutation) and the 25 serial top supernodes of the solves; a nested dissection ordering would give 1.8x fewer flops and a shorter critical path, but changes the factor and the results (owner decision).
Numbers and the method: [benchmarks/SparseCholeskyBenchmark/README.md](../../benchmarks/SparseCholeskyBenchmark/README.md).

### 4.2 Stage 2 status (2026-11-17): built, the irregular-vertex gate is the no-go

`gs.PositionField`, `gs.QuadRemesher` and `gs.PolyMeshMetrics` are in the library (`mesh_ops/`), with
[benchmarks/QuadRemeshBenchmark](../../benchmarks/QuadRemeshBenchmark/README.md) (the table of numbers). Implemented from the
Jakob et al. 2015 paper and the derivations of section 3.1 (no Instant Meshes, QuadriFlow or other code read; SOURCES.md).

As built, beyond the spike:
- **Hierarchy and solver:** greedy matching of the closest neighbours (ties by vertex id), coarse to fine, the Gauss-Seidel step of
  section 3.1 with the per-edge middle point and compatible orientation precomputed (so a sweep is only the two roundings per edge).
  **Parallel and deterministic:** a fixed greedy colouring in vertex order (6-7 colours); the colours run one after the other and
  the vertices of one colour only read other colours, so serial and parallel execute the same updates (tests: bit for bit, with
  work items of 3 to 512 vertices, repeated runs). The position field of 110k vertices takes 0.9 s on 4 cores (1.4 s serial).
- **Boundaries:** a boundary vertex keeps a lattice line through it (its cross direction nearest to the boundary becomes
  exactly the boundary direction, the position keeps zero offset across it), a corner (the boundary turns by more than 30 degrees,
  or a vertex that is not a simple boundary vertex) is a lattice point itself. Boundary vertices merge in the hierarchy only along
  boundary edges, corners never. In the extraction a cluster takes the position of its members of the highest kind. Own choices.
  Measured on the open patch: the four corners are exact vertices and about half of the boundary vertices of the result are on
  the boundary line (to 1e-3 s); the others are on rows up to 0.3 s inside (the boundary row and the next row of the lattice do
  not always meet): a ragged boundary, an open item.
- **Extraction:** union-find over the edges with the same lattice point (the two roundings less than `MismatchLimit` = 0.5 s
  apart), unit offsets are edges, faces traced in the tangent planes, oriented by the normals; the outer loops of open meshes,
  folded faces and faces above `MaxFaceSides` (6) are dropped. The cleanup: edges shorter than 0.4 s collapse (shortest first,
  corners keep their position), valence 2 vertices off the boundary are removed, dangling edges are pruned.
- **Result type:** `DPolyMesh`; `PolyMeshMetrics` is the gate checker (face mix, valence, corner angles, manifold: edges in at most
  2 faces, consistent orientation, every vertex one fan).

Measured gates (default options; the table is in the benchmark README):

| gate | result |
|---|---|
| quads >= 95 % | 99.1 % filleted box, 98.6 % 100k sphere, 95.8 % patch, 95.9 % bumpy; **missed** 94.8 sphere, 94.9 torus, 93.2 noisy sphere, 91.4 sphere cap |
| no face above 6 sides | met (faces above it are dropped: at most 1 per shape) |
| irregular <= 2 x singularities + 4 | **missed**: filleted box 16 (limit 20); sphere 77 (limit 20), torus 76 (limit 28), bumpy 176 (36), noisy sphere 92 (20), sphere cap 96 (36), 100k sphere 354 (20) |
| corner p95 <= 15 degrees | 4.9-14.5 on the closed shapes, 15.7 noisy sphere, 19.9 sphere cap |
| Hausdorff <= 0.5 s | met (0.07-0.27 s one-sided) |
| manifold polygon mesh | met on every shape |
| <= 5 s per 100k vertices, 4 cores | met for stage 2 (1.0 s); the cross field alone takes 3.6 s there (with #1099) |

**No-go reached (section 4: irregular > 4 x singularities after the cleanup).** The sphere has 77 irregular vertices for 8
singular faces (9.6 x), the torus 76 for 12, the bumpy sphere 176 for 16 (11 x), the noisy sphere 92 (11.5 x), the 100k sphere 354
(44 x: the count grows with the number of faces, not with the singularities). They are triangles paired with valence 5 vertices
(sphere: 66 triangles, 66 valence 5, 10 valence 3), the position-field singularities of spike finding 3. The paper's cleanup removes
almost none of them (0 collapses and valence 2 removals on most shapes). Nothing in stage 2's reach moves them: 100 sweeps, the
regularization 0.3, the mismatch limit 0.25 or 1.5, a finer input (`ratio=0.33`), a curvature aligned field (`align` 1 and 10) give
70-82 irregular vertices on the sphere. By Euler (spike finding 3), a triangle and a valence 5 vertex are a dipole that only a
change of topology removes: stage 3 (the flow of QuadriFlow, or the local topology cleanup of option C) is a prerequisite for the
irregular-vertex gate, and the quad share of 95 % is borderline without it. Stage 3 was not started.

**Owner decision 2026-11-17:** stage 2 merges as quad-dominant remeshing with the missed gates recorded as open (irregular vertices 6 to 12 x the field singularities; quads below 95 % on 4 of the 8 shapes). Next is stage 3B (the QuadriFlow min-cost flow, from the paper only: Huang et al. 2018), starting with a small upper-bound measurement of how far the flow cuts the irregular count on the sphere and torus.

**Update 2026-11-19 (owner approved): `CollapseLength` 0.4 -> 0.6.** The extraction cleanup collapses edges shorter than `CollapseLength` x s (shortest first).
The ragged boundary of an open mesh is a row of short edges and 0.4 s never reached them; 0.6 s does. Before / after on the benchmark shapes
(irregular V / quads % / faces; position field + extraction seconds, 4 cores; the 110k sphere is `big=100000`):

| shape | 0.4 (before) | 0.6 (now) | seconds before / after | edges collapsed |
|---|---|---|---|---|
| sphere | 77 / 94.8 / 1275 | 75 / 94.8 / 1274 | 0.26 / 0.27 | 0 -> 1 collapsed |
| torus | 76 / 94.9 / 1413 | 76 / 94.9 / 1413 | 0.14 / 0.13 | 0 -> 0 collapsed |
| bumpy | 176 / 95.9 / 3533 | 174 / 95.9 / 3532 | 0.16 / 0.14 | 0 -> 2 collapsed |
| filleted-box | 16 / 99.1 / 1732 | 16 / 99.1 / 1732 | 0.07 / 0.07 | 0 -> 0 collapsed |
| noisy-sphere | 92 / 93.2 / 1272 | 92 / 93.2 / 1272 | 0.09 / 0.07 | 0 -> 0 collapsed |
| sphere-cap | 96 / 91.4 / 950 | 96 / 91.4 / 950 | 0.05 / 0.05 | 0 -> 0 collapsed |
| open-patch | 32 / 95.8 / 659 | 17 / 98.3 / 642 | 0.02 / 0.02 | 0 -> 17 collapsed |
| big-sphere | 354 / 98.6 / 21592 | 352 / 98.6 / 21591 | 0.87 / 0.88 | 0 -> 1 collapsed |

The open patch goes 32 -> 17 irregular vertices and 95.8 -> 98.3 % quads (faces 659 -> 642), the sphere 77 -> 75, the bumpy sphere 176 -> 174; the torus,
box, noisy sphere and cap are unchanged, nothing is worse, the times are the same. The pure-quad gate of 3B (`benchmarks/QuadFlow3BGate`, 54 runs) is
unchanged: 25 clean / 29 fallback / 0 undetected bad, time gate 25 / 25, irregular gate 25 / 25, Hausdorff 24 / 25, corner p95 17 / 25 (stage 2 on
the same rows 18 / 25), clean rows median 2.7 x stage 2 (before 2.6). **Non-manifold vertices on open strips (bugfix 2026-11-19, repaired, root cause fixed).** Found by the review of the `CollapseLength` change and not caused by it: an open strip of 1 x w (w = 0.1, 0.2, 0.3, that is 1.7 to 7.5 target edges wide, s = 0.04 and 0.06) gave 1 or 2 non-manifold vertices in 5 of 6 cases, with the old and the new collapse length and also with `Cleanup = false` (0 edges collapse there): the extraction makes them. The cause is a degenerate star of the extraction: one vertex of the polygon graph with a fan of triangles around it (19 triangles around one boundary vertex at w = 0.1, s = 0.04) whose kept faces fall into several fans that touch only at that vertex; the fans are separated by faces that are dropped (the outer loop, faces above 6 sides, folded faces), and an interior vertex gets them the same way (`BuildFaces_SplitsAnInteriorVertexOfValence4WhoseOppositeFacesAreDropped`; the strips produced boundary ones only). What is measured (issue #1179, the star): the width over the spacing is 2.50 / 1.67 / 5.00 / 3.33 / 7.50 / 5.00 on the six cases and the star appears in 5 of them, **so it does not depend on the width being a multiple of the spacing** (an earlier version of this text said so; the integer ratio 5.00 has it too); the lattices of neighbouring vertices agree (at most 2 of 2500 to 7300 edges have a mismatch above the 0.5 s of the extraction, the largest mismatch is 0.56 s); the star vertex lies off the input surface (z = 0.007 against 0.0997 at x = 0.5 on w = 0.1, s = 0.04), which is the one-sided Hausdorff of 1.56-2.34 s of five of the six cases (0.30 s in the case without a star). **Root cause (found and fixed 2026-11-19, #1179):** in `PositionField.Prepare` a boundary vertex whose cross representative is nearer the perpendicular of the boundary had `Cb = d`, so the lattice line constraint removed the component of the origin along the boundary instead of across it: the origins of a whole boundary row kept their own place along the line, neighbours 0.25 s apart all rounded to one lattice point, and the union-find merged the top row into one cluster (130 vertices over 25 s for w = 0.1, s = 0.04) whose mean is the star (the mean z of the row, not the surface). The fix is `Cb = dperp`. Strips after the fix: 79-95 % quads, one-sided Hausdorff 0.02-0.03 s, 9-16 irregular vertices (benchmark README); the open patch 99.2 % quads and 9 irregular vertices (was 98.3 % and 17, its ragged boundary was this bug), the sphere cap 91.9 % (Hausdorff 0.37 s, was 0.27); closed shapes bit for bit the same. The default output of the two open pinned shapes changed (owner approved 2026-11-19). **The repair** (`BuildFaces`): a vertex whose kept faces form more than one fan is split into one copy per fan, at the same position, numbered by the smallest face index of the fan; the faces and the quad share do not change. Before the root cause was fixed it left the star and the poor quads of a strip in place (quad share 41-80 % on the six cases, one-sided Hausdorff up to 2.3 s), and the copies counted as irregular vertices (14 -> 17 on w = 0.1, s = 0.04). Result: 6 of 6 cases manifold (1 of 6 before: w = 0.2, s = 0.06), face counts and quad shares identical before and after (68.7 / 40.9 / 76.2 / 62.5 / 80.3 / 77.1 %). A face whose loop contains the same vertex twice (a pinched face) is not repaired (only the first corner is looked at; `PolyMeshMetrics` reports it as before). A mesh that was manifold is built exactly as before (the five pinned shapes and the seven benchmark shapes are manifold).
The value 0.6 is the owner's; 0.7 was measured on the data branch `runs/quad-stage2` (sphere 77 -> 68 irregular vertices but quads 94.8 -> 94.6 %, sphere cap
quads 91.4 -> 91.1 %) and not taken.


### 4.3 Stage 3B upper bound (2026-11-17)

**Status: parked, not a verdict on option B.** The measurement below used a formulation written from memory of the QuadriFlow
paper's structure plus own derivations (no QuadriFlow, Instant Meshes or other code read). A second, bounded measurement from the
paper itself could not be done: the paper was not reachable from the worker session (the fetch host was outside the network
allowlist, a direct download was denied, and the formulation is not reconstructed from memory again). Option B resumes when the
papers (Huang et al. 2018; for option C Bommes, Lempfer, Kobbelt, "Global Structure Optimization of Quadrilateral Meshes", CGF 30(2),
2011; Tarini et al., "Simple Quad Domains for Field Aligned Mesh Parametrization", TOG 30(6), 2011) are available to a session.

Prototype (a throwaway, not committed): per mesh edge an integer diff in the frame of its lower vertex, target = the difference of the two
vertices' position field lattice points in lattice units, rounded. Constraint: the (frame-rotated) diffs sum to zero around every
face; the three edges of a face whose frame holonomy is not the identity are fixed to 0. Cost: L1 distance to the real target, two
tiers per direction, |diff| <= 1. The constraint rows were signed by a BFS so that the matrix is a network matrix; the columns that
stay unbalanced (they close around the singularities, 340-1250 arcs) were frozen. Solver: successive shortest paths with
potentials and Dijkstra (Ahuja, Magnanti, Orlin, Network Flows, 9.7). Extraction: the stage 2 extraction and cleanup on the repaired
diffs (zero diff merges, unit diff links).

| shape (s = 0.1, ratio 0.5) | field singularities | stage 2 irregular | extraction, unrepaired diffs | after the flow | gate (singularities + 10 % of stage 2) |
|---|---|---|---|---|---|
| sphere | 8 | 77 | 80 | 127 (triangles 70 to 156, quads 94.5 to 87.5 %) | about 16 |
| torus | 12 | 76 | 77 | 85 (triangles 71 to 98, quads 94.9 to 92.9 %) | about 20 |
| torus, `align` 10 (0 singularities) | 0 | 108 | 108 | 175, not manifold | about 11 |

- The flow removes the face violations (sphere 79 to 6 faces, torus 85 to 2, aligned torus 95 to 0) but not the irregular vertices.
  The aligned torus has no frozen column: the diffs are fully consistent and the extraction is still worse.
- Finer input does not help (ratio 0.25 / 0.17): sphere 72 / 75 to 101 / 149, torus 64 / 65 to 95 / 96.
- Cause seen: the repairs relabel thin strings of vertices (clusters of at most 2 vertices: about 1 to 24-290), so extra valence 5
  and 6 vertices appear. The excess to route is about 45 units whatever the resolution, and a unit travels 20-70 edges.
- Time: 0.15-0.25 s for the flow at 6-7k vertices (stage 2: 0.3 s), 1.2 s at 55k vertices: time is not the blocker.

Not measured, so open: the paper's exact flow network and its handling of the unbalanced columns (SAT step), the global position
solve, flip and fragment removal, and a compactness term in the cost. The share of the irregular vertices that the frozen columns
explain was not isolated (the aligned torus shows the inconsistency is not only theirs). Next: B again from the paper, or option C
(local cleanup of the valence 5 / triangle dipoles) with an upper bound first.

### 4.4 Stage 3B built (2026-11-19): pure quads by the sign-aware flow, opt-in

Built as `QuadRemesher.PureQuads` (default `false`: the stage 2 result, bit for bit as before; the stage 2 results of 54 runs hash the
same on master and on the branch). The method is the spike's chosen configuration (brief `LIBRARY_BRIEF.md` on the data branch
`runs/quad-3b`, section 7), written from Huang et al. 2018 (sections 3.2-3.6), Ahuja, Magnanti, Orlin and the SAT texts in
[SOURCES.md](../SOURCES.md); `mesh_ops/QuadFlowRepair.cs`, `solvers/MinCostFlow.cs`, `solvers/GroupSatSolver.cs`:

- the integer offsets of the position field per triangle edge with the frame transitions; the regularity constraint (sum of the three
  offsets = 0) as a network by a breadth-first rotation of the equations; the fixed variables (cycles with a holonomy) balanced greedily;
- the min-cost flow (per-source successive shortest paths with early termination, equal-cost unit steps merged into one arc, heap ties by
  node index) with the capacity growing from 2 by 2 until the flow is full; **cost mode 2**: the marginal cost of a unit step (two tiers, +1)
  times 100 when the step grows |d| against the sign of the real offset;
- the subdivision of every edge with |d|inf above 1 (a priority queue with lazy invalidation, order: largest |d|inf, triangle, slot), the
  greedy vertex moves, the local SAT over rings 1 to 6 (implication encoding, a **total budget of 300k decisions**), the position solve
  contracted to the clusters of d = 0 edges, the extraction (pairs over the hypotenuses, 2-gons cancel) and the projection onto the input;
- **exact detection**: every offset is a small integer, `det(D0, D1)` is an `int`; `InvertedTriangles` counts the negative ones.

Fallback contract: `PureQuadStatus` is `Success` only if every check passes; otherwise `Result` is the stage 2 mesh (identical to
`PureQuads = false`), `QuadRemeshStatus` stays `Success`, and the status names the first failed check: `FlowInfeasible` (the flow cannot be
completed within capacity 40, or the offsets are out of range), `BudgetExceeded` (the SAT budget or the split budget is used up and the
map still has folds), `OrientationFolds` (folds left without a budget problem), `NotManifold` (not a manifold of quads only after the
extraction), `FlippedFaces` (a quad faces away from the surface). The brief listed the budget after the fold count; here a map with folds
that the budget stopped is `BudgetExceeded`, because the repair did not finish (on the 54 runs: 26 of the 29 fallbacks).

**Time gate (owner 2026-11-19, replaces the 2 x stage 2 gate for 3B):** a clean run takes at most max(3 x stage 2, 1 s). Measured
(`benchmarks/QuadFlow3BGate`, 54 runs, net10.0 Release, 4 cores; `Compute()` against `Compute()`): clean rows **25 / 25 within the gate**
(ratio median 2.7, max 7.7; 0.16 s median, 0.85 s max); fallbacks at most 2.13 s (0.48 s median); 25 clean, 29 fallback, **0 undetected
bad**. On the clean rows: irregular vertices within singularities + 10 % of stage 2 on 25 / 25, Hausdorff <= 0.5 s on 24 / 25 (the filleted
box at scale 1.25: 0.52 s), corner deviation p95 <= 15 degrees on 17 / 25 (stage 2 on the same rows: 18 / 25). The fallback rate (54 %) stays: the
option is useful as an opt-in only. Largest stage on clean rows and on fallbacks: the min-cost flow (50 % / 54 % of the attempt), then the
SAT (20 % / 40 %); the numbers are in the benchmark README.

**Fold diagnosis (`runs/quad-folds`, `benchmarks/QuadFoldDiagnosis`, 2026-11-19, measurement only):** on the 54 gate runs, raising the SAT budget 10x or 100x saves **0 / 26** `BudgetExceeded` rows (only 2 / 3 `OrientationFolds`), confirming the fallbacks are structural rather than budget-limited. Nearly 100 % of persistent folds are locally un-inverted by taking back one flow correction, and folds are distributed across the surface rather than concentrated at seams; however, two penalized flow re-solves on persistent folds only lift clean runs from **25 / 54 to 28 / 54** while more than doubling total time (21.9 s -> 46.6 s). Owner plan (2026-11-20): build stage 4 sharp features next, then run an upper-bound spike of the 3B flow on the size-adapted position field (`IntegrableFrames`).

### 4.4a Stage 3B flow and sizing update (2026-11-21)

Three targeted improvements in `QuadFlowRepair` and `QuadRemesher` cut the stage 3B fallback rate on the 54-run `QuadFlow3BGate` suite from **54 % to 20 %** (uniform spacing) and **11 %** (with `SizeAdaptation = ScaleAndRotation`), with **0 undetected bad** and default (`PureQuads = false`) output unchanged bit for bit:

1. **Geometric sign reference (`rGeoA`) for `SignPenalty`:** previously `StepCost` checked `b * rA < 0`, where `rA ≈ (o_v - o_u) / s` collapses to `±0.01` smoothing noise on every converged `d = (0, 0)` edge, randomly blocking valid flow steps and inducing 100–500 inverted triangles right out of `SolveFlow`. Using the geometric edge projection `rGeoA[e] = 0.5 * ((P[v] - P[u]) · q_u / s_u + (P[v] - P[u]) · b / s_v)` for `SignPenalty` (while keeping `rA` for `MarginalCost`) drops flow-induced folds near zero and reduces total SAT time across clean rows from `0.65 s` to `0.04–0.08 s`.
2. **Cost-guided fixed-edge balancing:** in `SolveFlow()`, each balancing round now picks the lowest-cost unbalanced edge (preferring `|d| <= 1`) instead of repeatedly adjusting the first unbalanced edge up to `|d| = 8..9`, preventing artificial capacity blowups (`H = 10`) and extra supply.
3. **Local spacing (`SizeAdaptation` + `PureQuads`) and nearest-triangle flip check:** `BuildOffsets()` and `SolvePositions()` now scale by `PositionField.SpacingFactors` (`s * spc[v]`), lifting the `InvalidInput` block between `SizeAdaptation` and `PureQuads`. `Extract()` checks unprojected quad normals, projected quad normals against cluster normals, and projected quad normals against the nearest input triangle at the quad center (`0` undetected bad).

Measured (`dotnet run -c Release --project benchmarks/QuadFlow3BGate -- adapt=0`, `adapt=1`, `adapt=2`, 54 runs, net10.0 Release, single-run `repeat=1`):

| configuration | clean / fallback / bad | per-shape clean (of 6): sph / tor / tor10 / bmp / box / nsp / cap / g2 / cyl | time gate (clean) | irregular gate | Hausdorff <= 0.5 s | corner p95 <= 15 deg | ratio med / max | SAT sum clean (s) |
|---|---|---|---|---|---|---|---|---|
| baseline (`adapt=0`, 2026-11-19) | 25 / 29 / 0 | 3 / 3 / 6 / 1 / 5 / 1 / 0 / 6 / 0 | 25 / 25 | 25 / 25 | 24 / 25 | 17 / 25 | 2.6 / 8.9 | 0.65 |
| updated (`adapt=0`, uniform) | **43 / 11 / 0** | **6 / 6 / 6 / 6 / 6 / 6** / 1 / **6** / 0 | 41 / 43 | **43 / 43** | 41 / 43 | 20 / 43 | 2.5 / 5.3 | **0.04** |
| updated (`adapt=1`, `Scale`) | **43 / 11 / 0** | **6 / 6 / 6 / 6 / 6 / 6** / 1 / **6** / 0 | **43 / 43** | **43 / 43** | **42 / 43** | 26 / 43 | 1.6 / 3.0 | **0.05** |
| updated (`adapt=2`, `ScaleAndRotation`) | **48 / 6 / 0** | **6 / 6 / 6 / 6 / 6 / 6** / 1 / **6** / **5** | **48 / 48** | **48 / 48** | 42 / 48 | **29 / 48** | **1.4 / 2.0** | **0.08** |

Notes on the balancing and the flip check: `GreedyWorkLimit` counts heap operations (initial pushes, pops and re-pushes after a move; before the heap it counted edge visits), and the selection is the lowest-cost improving move, not the first one in edge order. The nearest-triangle flip check in `Extract()` is conservative on walls thinner than the edge length (the nearest input triangle to a quad midpoint can lie on the far side of the wall): such a quad counts as flipped and the result falls back to stage 2, never a wrong success.

Approximation with `SizeAdaptation`: the integer offsets are counts in the local lattice units, and `SolvePositions` turns the three offsets of a triangle into 3D with one spacing per triangle (the one at its frame vertex), so the target vectors are only approximately right where the spacing gradient is large; this is why the Hausdorff and corner gates pass on fewer rows with `ScaleAndRotation` (42 / 48 and 29 / 48) than the fallback rate suggests. The "54 % to 11 %" headline counts only the fallbacks, not the quality gates of the accepted rows (table above). A zero-area nearest input triangle skips only the surface-orientation part of the flip check (the cluster-normal checks stay).

Remaining limit (`capsule`, 1/6): with `CurvatureAlignment = 0`, all four `+1/4` singularities sit tightly clustered at each hemispherical cap (`unbal = 25..107`) with no cut edges crossing the barrel; by Stokes' theorem the barrel circumference integral is locked to the small sum of fixed cap edges, causing the flow to collapse the barrel regardless of flow costs. On `capped-cylinder`, `ScaleAndRotation` resolves the cap/barrel spacing mismatch (`0.45..1.58 s`) and raises clean runs from `0/6` to `5/6`.

#### 4.4c The area ratio of the map as a diagnostic (2026-11-22, #1301 option A)

`QuadRemesher.PureQuadAreaRatio` (and `PureQuadAreaRatioAfterFlow`, from `QuadFlowRepair.AreaRatio` / `AreaRatioAfterFlow`) reports the invariant of [QUAD_SHEAF_OBSTRUCTION_2026-11.md](../research/QUAD_SHEAF_OBSTRUCTION_2026-11.md) statement 2: `A = (1/2) sum det(D0, D1) (s * spacing)^2` over the triangles of the integer map, divided by the area of the input triangles (1 for a map that covers the surface once; folds enter with their negative determinants; with `SizeAdaptation` / `SizeFunction` the cell size is the local one). One pass over the triangles after the flow and after the repair. **Diagnostic only (owner decision for later): it is not a fallback reason, it does not change `PureQuadStatus`, `Result` or what counts as `Success`.** NaN when `PureQuads` is false or the attempt ended before the flow (open mesh, invalid input). On the sphere of the tests it is 0.89 after a clean run. Deterministic (serial, fixed order) and bit-identical under power-of-two scalings; tests: `PureQuadTests` (`LatticeAreaRatio_OfKnownMaps`: a flat grid gives 1, doubled cells 2, a folded quarter 1/2, a local spacing weights the cells; the run, the reuse and the similarity tests).

### 4.5 The irregular-vertex gate restated, and the size field (owner 2026-11-19)

**Owner 2026-11-19:** the gate "irregular <= 2 x singularities + 4" of stage 2 is restated relative to the defects that a lattice of uniform spacing forces, the size field below is an
opt-in (`QuadRemesher.SizeAdaptation`, default off, the default output unchanged bit for bit), and the data is the measurement of 2026-11-19 (data branch `runs/quad-stage2`, `benchmarks/QuadRemeshDiagnosis`).

**Where the irregular vertices come from.** Per input triangle the holonomy of the position field (the integer lattice offsets of its edges, composed around the triangle) tells a cone of the
lattice (net rotation, a singularity of the cross field), a dislocation (no rotation, a translation: a triangle paired with a valence 5 vertex) and an edge the extraction ignores. Each irregular
vertex and each non-quad face of the 7 shapes got the class of the nearest cause within 0.75, 1.5 and 2.5 x s. The dislocations are 75-85 % of both on the closed shapes at every radius (sphere at 0.75 s: 64 of 77
irregular vertices; the 8 cones account for 8; 5 have no cause nearby). The extraction itself (collapsed or split edges, resolution) accounts for none at 1.5 s and more and for 0-14 % at 0.75 s. On the open shapes the boundary adds its own
(open patch: 37 of its 42 dislocation triangles lie within 1.5 s of the boundary; the field has no curl there).

**The measure: the curl mass.** For every triangle without a singular face of the field, `w` is the sum over its three edges of the edge vector in the averaged, aligned cross frame of the edge, in units of the target edge length s
(zero where the frames close up into a lattice; the sum of `w` over a loop is the Burgers vector of the loop). `C` is the sum of `|w|` over those triangles, `IntegrableFrames.CurlBefore` (with the uniform spacing), in lattice units. It tracks
the dislocation count:

| shape | irregular V | singular faces S | curl mass C | dislocation clusters | (irregular - S) / C |
|---|---|---|---|---|---|
| sphere | 77 | 8 | 64.9 | 67 | 1.06 |
| torus | 76 | 12 | 72.2 | 68 | 0.89 |
| bumpy sphere | 176 | 16 | 138.6 | 150 | 1.15 |
| filleted box | 16 | 8 | 29.0 | 14 | 0.28 |
| noisy sphere | 92 | 8 | 99.3 | 84 | 0.85 |
| sphere cap (open) | 96 | 16 | 79.6 | 89 | 1.00 |
| open patch | 32 | 0 | 2.1 | 41 | boundary: 37 of 42 near it |

**Why a uniform spacing cannot do better.** A coframe of unit spacing (two closed 1-forms of unit length, orthogonal) has a vanishing connection form, so it exists only where the Gauss curvature is 0
(d theta_1 = -omega ^ theta_2, d theta_2 = omega ^ theta_1; both zero means omega = 0, and d omega = K dA). On a curved surface the curvature has to be moved to the cones of the field, and the smoothest field that does it has a curl whose mass
is about the transport cost of the curvature to the cones (earth mover distance, in units of s). The position field cannot rotate or rescale its lattice, so it pays that mass with dislocations, one per lattice unit. The mass, and so the count,
grows with 1 / s: the sphere at s x 2, x 1, x 0.5 (input resolution fixed) gives 31, 77, 179 irregular vertices (measured 1 / s^1.2). It does not depend on the sweeps, the hierarchy, the rounding, or the extraction: the rounding of the origins only
changes the representative of o_i and so not the lattice (the result is bit for bit the same), 5 and 100 sweeps move the count of the closed shapes by -14..+7, the neighbour weights (1 / edge length, edge length) by -16..+8 with the box worse, the extraction's
collapse length 0.6 / 0.7 by -2 / -9 on the sphere and 0 on the box (open patch 32 -> 17, the one place it matters), the mismatch limit 0.3 is worse everywhere, a local (damped least squares) coupling of orientation or scale per vertex by at most 7; the dislocations
sit in the integer structure of the lattice and a local update cannot move them. (This is also the floor of any later stage that keeps the unit spacing and the field; I did not test the flow of 3B on it.)

**The restated gate (closed meshes; owner 2026-11-19).** `irregular vertices <= 2 S + 4 + 1.25 C`, S the singular faces of the field, C the curl mass of the uniform spacing (`IntegrableFrames.CurlBefore`; `QuadRemesher.CurlBefore` is set only when `SizeAdaptation` is not Off, so it takes one run with `Scale` to evaluate the gate). The constant 1.25 is fitted to the six shapes with curl (the tests check the formula on the sphere and torus only): the measured maximum of (irregular - 2 S - 4) / C is 1.01 on the six shapes with curl (0.88 sphere, 0.61 torus, 1.01 bumpy, 0.73 noisy sphere,
0.70 cap), so it is a gate with a 25 % margin, not a bound. The 95 % quads gate is unchanged (4 of the 7 shapes below it at the default; the opt-in size field below reaches it on all closed shapes). Open meshes: the boundary adds dislocations
that C does not see (open patch 32 with C = 2.1, cap within the formula); the ragged boundary stays an open item, no gate. Test: `QuadSizeAdaptationTests` pin the curl mass against the irregular count on the sphere and torus.

**The size field (`IntegrableFrames`, `QuadRemesher.SizeAdaptation`).** A per-vertex log spacing sigma and optionally a rotation phi of the cross field are solved so that the scaled and rotated frames close up better: damped Gauss-Newton on the sum of
|w|^2 with edge terms exp(-sigma_e) Rot(phi_e) a_e (sparse Cholesky, mean of sigma 0 as the gauge, backtracking), and the position field then runs with the spacing `s exp(sigma_i)` and the rotated orientation (`PositionField.SpacingFactors`,
`RotationAngles`; the coarse levels average). The idea is the scale and rotation freedom of mixed-integer quadrangulation (Bommes et al. 2009), the formulation is ours (SOURCES.md). It is used only if it lowers the curl by 20 %
(`SizeAdaptationMinGain`). The first solver of the study (conjugate gradients, no gauge) only seemed to reach a curl of 22 of 65 on the sphere: part of that was a constant spacing drifting to infinity; fixed with the gauge, the minimum
the damped Gauss-Newton reaches is 28 of 65 (sphere), 29 of 72 (torus), 104 of 139 (bumpy), 59 of 99 (noisy sphere), 44 of 80 (cap); the box (29 -> 25) and the open patch (2.1 -> 1.9) do not gain 20 %, so they stay uniform.

Benchmark (`benchmarks/QuadRemeshBenchmark`, `adapt=0|1|2`: Off, Scale, ScaleAndRotation; with the default `CollapseLength` 0.6 of 2026-11-19 (the closed shapes are the same numbers as with 0.4); 4 cores; the time is the solver plus the position field plus the extraction, the field is the same for all):

| shape | Off: irregular V / quads % / corner p95 | Scale | ScaleAndRotation | spacing range (ScaleAndRotation) | solver (s), Scale / both | solver + position field + extraction (s), Off / Scale / both |
|---|---|---|---|---|---|---|
| sphere | 75 / 94.8 / 14.6 | 40 / 97.1 / 9.4 | 34 / 97.3 / 9.0 | 0.54-1.13 | 0.43 / 0.56 | 0.33 / 0.78 / 0.83 |
| torus | 76 / 94.9 / 12.7 | 35 / 98.0 / 9.2 | 46 / 97.6 / 9.6 | 0.59-1.69 | 0.19 / 0.26 | 0.15 / 0.37 / 0.41 |
| bumpy | 174 / 95.9 / 10.8 | 174 / 95.9 / 10.8 (not applied) | 126 / 97.0 / 9.7 | 0.53-1.24 | 0.39 / 0.88 | 0.15 / 0.54 / 1.07 |
| filleted-box | 16 / 99.1 / 4.9 | 16 / 99.1 / 4.9 (not applied) | 16 / 99.1 / 4.9 (not applied) | 1 (not applied) | 0.25 / 0.29 | 0.07 / 0.33 / 0.39 |
| noisy-sphere | 92 / 93.2 / 15.7 | 67 / 95.8 / 12.5 | 46 / 96.9 / 10.0 | 0.50-1.23 | 0.12 / 0.34 | 0.08 / 0.19 / 0.42 |
| sphere-cap | 96 / 91.4 / 19.9 | 83 / 94.7 / 17.6 (Hausdorff 1.39 s) | 81 / 94.3 / 17.7 | 0.55-1.61 | 0.14 / 0.09 | 0.04 / 0.18 / 0.16 |
| open-patch | 17 / 98.3 / 6.2 | 12 / 96.9 / 6.2 | 17 / 98.3 / 6.2 (not applied) | 0.96-1.04 (Scale only) | 0.09 / 0.04 | 0.02 / 0.11 / 0.07 |
| big-sphere | 352 / 98.6 / 5.8 | 245 / 99.0 / 5.2 | 234 / 99.1 / 4.9 | 0.46-1.02 | 13.91 / 17.62 | 1.06 / 14.92 / 18.58 |

Reading: on the sphere, torus and noisy sphere the size field cuts the irregular vertices (sphere 77 -> 34-40, torus 76 -> 35-46, noisy sphere 92 -> 46-67, the 110k sphere 354 -> 234-245) and lifts the quads to 96-98 % (the 110k sphere 98.6 -> 99.0 %),
with a corner p95 of 9.0-12.5 degrees (Off 12.7-15.7). The rotation helps on the sphere, the noisy sphere and the bumpy sphere (not applied with Scale alone, 176 -> 126 with ScaleAndRotation) and not on the torus (35 with Scale, 46 with both): Scale is the safer default
choice. None of the closed shapes reaches the old gate (sphere 34 against 20), all are inside the restated one. It does nothing on the filleted box (not applied: the curl does not drop by 20 %, the result is the uniform one bit for bit). On the open shapes it is mixed: the cap goes
96 -> 83 irregular with Scale (94.7 % quads) but its one-sided Hausdorff grows to 1.39 s (above the 0.5 s gate; the spacing reaches 2.5 near the boundary), with ScaleAndRotation 81 irregular, 94.3 % quads and the Hausdorff inside the gate; the open patch gets 32 -> 25 irregular and
95.8 -> 95.0 % quads with Scale. **Use it on closed smooth shapes only.** The edge length varies by a factor of 2 to 3 across a shape (spacing 0.54-1.13 of the target on the sphere, 0.59-1.69 on the torus), so the target edge length is the mean, not the size of every quad. The extraction's collapse limit is relative to the local spacing (a vertex of the polygon graph carries the spacing factor of its cluster); a fixed 0.6 s would collapse regular cells of the size field (sphere at s = 0.2: quads 96.5 -> 93.0 %, found by the review of the collapse default). Cost: 0.1-1.6 s for 5-18k input vertices, 16 s for the 110k sphere with ScaleAndRotation (the Cholesky factorization of
220k unknowns, refactorized every 8 steps), against 2 s for the position field of the same input: a heavy option, and the 110k run is the one input of the study above 10 s. Scale alone solves for the log spacing only (one unknown per vertex since 2026-11-19: the rotation unknown was decoupled and only held by the damping): the 110k sphere takes 5.2 s for the solver instead of 14.8 s, the quality numbers are the same, the output differs from the two-unknown layout at rounding level (owner 2026-11-19; the other shapes 0.05-0.26 s instead of 0.14-0.42 s). A solve on a coarse level of the position field's hierarchy (smoothed prolongation, then fine Gauss-Seidel sweeps) was measured the same day (`benchmarks/QuadCoarseScale` on branch `runs/quad-coarse-scale`) and keeps about half of the 110k sphere's reduction (378 -> 324 irregular vertices at 2.2 s, against 283 at 5.2 s; with the 20 % gate off, as no coarse variant passes it there), so it was not built. (A review found that the first version of the solver turned the lattice by the opposite angle of the one the position
field applies, which the curl numbers cannot show; `Frames_CurlAfter_IsTheCurlOfTheLatticeThatPositionFieldBuilds` checks it against an independent evaluation, and the table is from the corrected code.)

**Open-boundary handling and user sizing field (2026-11-20).** Two extensions to `IntegrableFrames` and `QuadRemesher` (both preserve default `SizeAdaptation = Off` output bit for bit, and closed shapes remain bit-identical in `Scale` and `ScaleAndRotation`):
- **Open boundaries:** boundary vertices' `phi` columns are eliminated directly in `Linearize` (fixing Gauss-Newton stalling on `open-patch` with `ScaleAndRotation`, which previously stalled at curl 2.1 -> 1.9 and was not applied), and `IntegrableFrames.BoundarySpacingWeight` (default `0.05`, with an exact KKT projection for the mean-zero gauge) regularizes boundary log spacing toward 0. Measured via `dotnet run -c Release --project benchmarks/QuadRemeshBenchmark -- adapt=1|2` (`master 976ef22` -> branch): `sphere-cap` `Scale` one-sided Hausdorff drops from **0.64 s to 0.37 s** (inside the 0.5 s gate; quads 94.4 -> 94.8 %); `sphere-cap` `ScaleAndRotation` improves from 88 -> **65** irregular vertices, 95.0 -> **96.2 %** quads, and corner p95 18.3 -> **13.4** degrees (Hausdorff 0.37 s); `open-patch` `ScaleAndRotation` is now applied (9 -> 7 irregular, 99.2 -> 99.5 % quads, corner p95 4.9 -> 2.7 degrees, Hausdorff 0.05 s). Closed shapes are bit-identical (`digest=1`).
- **User sizing field (opt-in):** `IntegrableFrames.SizeFunction` (`Func<Vector3d, double>`, compatible with `RemeshSizing.FromWallThickness`) and `SizeWeight` (default `1.0`), wired onto `QuadRemesher.SizeFunction` (default `null`). Invalid samples (`NaN`, `<= 0`, infinity) are ignored and counted in `InvalidSizeSamples`. When at least one used vertex has a valid sample and `SizeWeight > 0`, the mean-log-spacing = 0 gauge is omitted so absolute edge lengths are preserved, and `QuadRemesher` applies the field without requiring the `SizeAdaptationMinGain` curl drop.


Since 2026-11-21 (§4.4a), `SizeAdaptation` and `SizeFunction` combine with `PureQuads` (`QuadFlowRepair` uses the per-vertex spacing in both `BuildOffsets` and `SolvePositions`). Not done, so open: the dislocations that remain (about half) in stage 2, features in the solver.

Research spike 2026-11-22 ([QUAD_SEAMLESS_SEED_2026-11.md](../research/QUAD_SEAMLESS_SEED_2026-11.md), `benchmarks/QuadSeamlessSeedSpike`): a spacing and rotation from a conformal cone metric with the field's cones (an oracle for a seamless-parametrization seed, spacing clamped at 0.7, same cell count) cuts the irregular vertices of 9 closed shapes from 863 (uniform) and 493 (the best of Scale and ScaleAndRotation) to 407, against 88 singular faces; the pure-quad success rate did not move in an exploratory run; options and leaning are in the note, nothing is built.

### 4.6 Stage 4 built (2026-11-21): sharp feature / crease preservation, opt-in

Built as `PositionField.PreserveCreases` and `QuadRemesher.PreserveCreases` (default `false`: output is bit for bit identical to without the option) with `CreaseAngleDegrees` (default `30`):

- **Detection & classification (`PositionField.BuildFinest`):** interior sharp edges above `CreaseAngleDegrees` from `MeshTopology.CreaseEdges` (matching `CrossField.AlignCreases`) are marked as feature edges alongside boundary edges. Smooth crease vertices (crease degree 2, turn `<= CornerAngleDegrees`, not on an open boundary) get `Kind = 1` (lattice line along the crease tangent `D`, perpendicular removed via `Cb = N x D`); crease junctions (degree != 2), sharp crease corners (turn `> CornerAngleDegrees`), and boundary-crease junctions get `Kind = 2` (fixed lattice points).
- **Hierarchy & extraction:** in `Coarsen()`, `Kind = 1` vertices merge only along feature edges (`AB`) and `Kind = 2` corners never merge. During prolongation, `Kind = 1` vertices project onto `Cb` as well as `N`. In `QuadRemesher.Extract`, crease/corner clusters take both position and normal from their highest-kind members (so interior faces do not skew crease vertex normals in `BuildFaces`), and `CleanupGraph` keeps higher-kind corner and crease positions and normals fixed against interior collapses. When `QuadRemesher.Field` is `null` and `PreserveCreases` is `true`, `CrossField` is computed with `AlignCreases = true` and `CreaseAngleDegrees`. `CreaseAngleDegrees` is the one threshold of the field and of the lattice constraints; `CornerAngleDegrees` also classifies the crease corners (a crease vertex where the crease turns by more than it is a fixed lattice point); `PreserveCreases` implies `AlignCreases` for the computed field. `PreserveCreases` together with `PureQuads` is `InvalidInput` (the repair does not keep the feature curves: on a sharp box the combination used to report `PureQuadStatus.Success` with the feature coverage falling from 100 % to about 41 % and the feature Hausdorff rising from 0 to 0.59); making the repair keep feature curves is the Phase 4c measurement. Known limit: a zero-area input triangle has a zero normal, so all its edges count as creases (`MeshTopology`) and its vertices become lattice constraints with `PreserveCreases`; clean degenerate triangles first.

Measured against the stage 4 feature gates (`> 95 %` feature length coverage within `0.1 s` and `10 deg`, feature angle p95 `<= 10 deg`, feature Hausdorff `<= 0.1 s`, manifold) via `dotnet run -c Release --project benchmarks/QuadRemeshBenchmark -- sharp=1 creases=0|1` (net10.0 Release; procedural CAD shapes isotropically remeshed at `h = 0.5 s` with `PreventNormalFlips = true, MinTriangleQuality = 0.2, PreserveBoundaryAndFeatures`):

| shape | s | creases=0: cov % / ang p95 / feat Haus / surf Haus / quads % | creases=1: cov % / ang p95 / feat Haus / surf Haus / quads % | manifold |
|---|---|---|---|---|
| sharp-box | 0.1 | 1.4 / 22.8 / 0.565 s / 0.40 s / 97.3 | **100.0 / 0.0 / 0.000 s / 0.03 s / 99.5** | yes |
| sharp-cylinder | 0.1 | 14.6 / 21.9 / 0.505 s / 0.30 s / 92.9 | **99.5 / 1.7 / 0.052 s / 0.08 s / 92.3** | yes |
| l-profile | 0.08 | 57.1 / 2.4 / 0.520 s / 0.31 s / 95.9 | **100.0 / 0.0 / 0.000 s / 0.02 s / 98.5** | yes |
| gear | 0.06 | 17.4 / 6.6 / 0.596 s / 0.42 s / 91.7 | **100.0 / 0.0 / 0.000 s / 0.04 s / 91.8** | yes |

All four shapes meet every stage 4 gate at both `scale=1` and `scale=0.5` (`sharp-box` 100.0 % / 0.0 deg / 0.000 s; `sharp-cylinder` 99.2 % / 1.3 deg / 0.049 s; `l-profile` 100.0 % / 0.0 deg / 0.010 s; `gear` 100.0 % / 0.0 deg / 0.000 s, 95.2 % quads). Tests: `QuadCreasePreservationTests` (all four CAD shapes, exact 8 box corners, 120-deg sharp ridge prism, open L-bent sheet with boundary-crease junctions, degree-1 endpoint / degree-2 sharp turn / degree-4 junction classification, `PositionField` multi-level zero-sweep constraint check, explicit `Field`, threshold/smooth bit-identity, `SizeAdaptation` `Scale` and `ScaleAndRotation` composition with `SizeAdaptationApplied == true`, `Similarity.All()`, determinism, reuse).

## 5. Limits and recommendation

Limits:
- The spike ran on clean, closed, manifold, isotropically remeshed procedural shapes. Real meshes go through the
  `Remesher` first. Non-manifold or broken input then needs `MeshRobustRepair` before the field, and nothing in the
  spike shows how often that succeeds.
- The spike has no boundaries, sharp features or sizing field. Its extraction has no cleanup.
- Its times are single-threaded and unoptimized, and below 41k vertices. Instant Meshes reports seconds for
  millions of points with parallel smoothing (the paper, from memory).
- No guarantee: field-aligned methods (Instant Meshes, QuadriFlow) do not guarantee a valid, watertight quad mesh.
  Guarantees come only from the parametrization route (MIQ / QGP + QEx, XL, with GPL-only references) or from the
  layout methods (QuadWild, Lyon et al., which need ILP solvers).
- The quality of the CAD-oriented methods (QuadWild) on sharp CAD parts is out of reach of stages 1-4.

**Recommendation: go for stages 1 and 2 (plus 5) as quad-dominant remeshing, implemented from the Knöppel 2013
and Jakob et al. 2015 papers.** The spike shows that both stages work with existing blocks (`Remesher`, cotan
weights, `SparseCholesky`, `MeshHausdorffDistance`). It gives 93.6-98.9 % quads with corner p95 5.6-15.1 degrees
already without cleanup and from the papers alone, and the field matches theory (8 faces of index +1/4 on every genus-0 shape). The one measured
gap (position singularities, 75-133 irregular vertices on the curved shapes against 8 field singularities) is what the stage 2 cleanup
and, if needed, the stage 3B flow address. Stage 1 also stands alone: a cross field is useful for anisotropic
remeshing, hatching and seam layout. Defer pure quads (3B) and features (4) until stage 2's gates are measured. Do
not pursue MIQ / QEx / QuadWild / Lyon et al. now: each is XL, needs mixed-integer or ILP solvers, and has only GPL
or no reference code. Do not ship triangle pairing as a quad remesher.
