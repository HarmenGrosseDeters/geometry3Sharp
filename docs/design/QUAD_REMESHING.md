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
| **Jakob, Tarini, Panozzo, Sorkine-Hornung, *Instant Field-Aligned Meshes*, ACM TOG 34(6) 189, SIGGRAPH Asia 2015** | Local extrinsic smoothing of an orientation field and a position field (lattice), both by Gauss-Seidel on a multiresolution hierarchy, then extraction from the integer lattice offsets with cleanup. Quad-dominant output, or pure quads after one subdivision. | Very robust: works on point clouds, noisy and non-manifold input, because it is local and needs no global parametrization. It tolerates holes and has crease handling. Output has T-junction-like defects at position-field singularities. | **M-L**: the spike reached a usable quad-dominant result in about 400 lines; the cleanup and the boundary handling are the rest | instant-meshes **BSD-3 plus an "Enhancements" clause** (LBNL-style: Enhancements made publicly available are licensed back to the authors), checked. Its `.gitmodules` lists nanogui, pcg32, dset, tbb and pss (wjakob forks), and the repository states no licences for them. From the projects themselves: nanogui BSD-3, tbb Apache-2.0, pss MIT and rply MIT-style (checked); pcg32 Apache-2.0 (from the QuadriFlow README); dset not checked. No GPL found. | Porting needs a per-file check; the Enhancements clause is an owner decision (implementing from the paper avoids it). Determinism: the paper's parallel Gauss-Seidel uses a graph colouring, so it is deterministic only with a fixed colouring and order. |
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

The `s` values are 2-3 % of the bounding box diagonal. Hausdorff is measured against a remesh at `h / 2`. The
symmetric value includes the input's distance to the holes (defect faces are not triangulated). The one-sided value
from the output to the reference measures the faces that exist. Command: `dotnet run -c Release --project
benchmarks/QuadRemeshSpike` (default `sweeps=40`).

| shape | method | input V | singular faces (index x count); field eigenvalue | sum index | faces | quads % | tris | 5-6 | defects (<3 or >6 sides) | non-manifold tris | irreg. V | irreg. after 1 CC | angle dev p50 / p95 (deg) | corners in [70,110] % | aspect p50 / p95 | Hausdorff sym. / diag | Hausdorff out->ref / diag | t field | t pos | t extract (s) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| sphere | field+pos (IM-style) | 6390 | 8 (+1/4 x8); lambda 4, last change 1.5E-08 | 2.00 | 1275 | 95.0 | 62 | 2 | 0 | 2 | 62 | 126 | 2.3 / 12.7 | 97.6 | 1.06 / 1.36 | 0.0021 | 0.0021 | 0.41 | 1.04 | 0.02 |
| sphere | tri-pairing (s) | 1591 | - | - | 1764 | 80.2 | 350 | 0 | 0 | 0 | 438 | 788 | 26.5 / 35.6 | 16.7 | 1.15 / 1.40 | 0.0026 | 0.0026 | 0.00 | 0.00 | 0.10 |
| torus | field+pos (IM-style) | 7026 | 12 (-1/4 x6, +1/4 x6); lambda 4.74, last change 0.00049 | 0.00 | 1414 | 96.3 | 50 | 2 | 1 | 0 | 52 | 105 | 2.2 / 9.7 | 98.1 | 1.05 / 1.32 | 0.0091 | 0.0046 | 0.29 | 1.07 | 0.02 |
| torus | tri-pairing (s) | 1767 | - | - | 1955 | 80.8 | 376 | 0 | 0 | 0 | 483 | 859 | 26.1 / 35.2 | 17.6 | 1.15 / 1.39 | 0.0049 | 0.0049 | 0.00 | 0.00 | 0.11 |
| bumpy | field+pos (IM-style) | 17918 | 8 (+1/4 x8); lambda 4.03, last change 0.00054 | 2.00 | 3576 | 96.8 | 110 | 4 | 1 | 0 | 134 | 249 | 1.9 / 9.3 | 98.1 | 1.04 / 1.32 | 0.0090 | 0.0020 | 0.89 | 2.73 | 0.02 |
| bumpy | tri-pairing (s) | 4471 | - | - | 4994 | 79.0 | 1050 | 0 | 0 | 0 | 1382 | 2432 | 26.6 / 35.8 | 17.4 | 1.16 / 1.40 | 0.0022 | 0.0022 | 0.00 | 0.00 | 0.44 |
| filleted-box | field+pos (IM-style) | 9092 | 8 (+1/4 x8); lambda 1.32, last change 1E-15 | 2.00 | 1755 | 99.5 | 8 | 0 | 0 | 0 | 7 | 15 | 0.7 / 2.4 | 99.7 | 1.01 / 1.04 | 0.0073 | 0.0073 | 0.37 | 1.18 | 0.01 |
| filleted-box | tri-pairing (s) | 2279 | - | - | 2504 | 81.9 | 454 | 0 | 0 | 0 | 583 | 1037 | 25.8 / 34.7 | 19.0 | 1.16 / 1.41 | 0.0101 | 0.0101 | 0.00 | 0.00 | 0.11 |
| noisy-sphere | field+pos (IM-style) | 6390 | 8 (+1/4 x8); lambda 5.57, last change 0.00045 | 2.00 | 1289 | 94.5 | 69 | 1 | 1 | 0 | 81 | 152 | 2.4 / 13.5 | 97.2 | 1.07 / 1.39 | 0.0137 | 0.0044 | 0.24 | 0.91 | 0.02 |
| noisy-sphere | tri-pairing (s) | 1591 | - | - | 1767 | 79.9 | 356 | 0 | 0 | 0 | 519 | 875 | 25.9 / 34.8 | 19.4 | 1.15 / 1.38 | 0.0077 | 0.0077 | 0.00 | 0.00 | 0.09 |

Notes on the table:
- The tri-pairing rows' time includes their remesh.
- The noisy sphere's Hausdorff is measured against the clean sphere, so it includes the noise.
- "Field eigenvalue" is the Rayleigh estimate after 30 inverse iterations; "last change" is its relative change in
  the last iteration. The torus, the bumpy sphere and the noisy sphere are not fully converged (5e-4); the
  singularity structure is the same with 10 or 100 position sweeps.
- "Non-manifold tris" are fan triangles of the output that `DMesh3` rejects (a non-manifold edge): 2 on the sphere.
- Valence is counted from the faces with 3-6 sides, so the vertices on the border of a defect face count as
  irregular.

Sensitivity runs (same command with one parameter changed; field + pos rows; before the warm-up and the extra
columns were added, which do not change the other columns):
- **Sweeps** barely matter. With 10 sweeps per level: quads 94.2-99.5 %, irregular vertices 8-129, and position
  time 0.3-0.7 s. With 100 sweeps: quads 94.6-99.4 %, irregular 9-130, and 2.1-6.3 s. The hierarchy does the work.
- **Finer input** (`ratio=0.33`, 14k-41k vertices): the quad share is unchanged (94.7-99.7 %), defects drop to 0,
  and the irregular count drops a little (6-115). The field takes 0.8-4.0 s.
- **Half the quad size** (`scale=0.5`, 25k-36k vertices): sphere 97.5 % quads, 141 irregular vertices, p95
  deviation 7.9 degrees; filleted box 99.8 %, 16 irregular vertices. The field takes 1.7-3.1 s and the position
  field 3.9-5.2 s.

Findings:
1. **The field works.** Knöppel's smoothest field on our cotan weights with `SparseCholesky` has exactly 8 singular
   faces, each of index +1/4, on the sphere, the bumpy sphere, the filleted box and the noisy sphere. That is the
   minimum for a 4-RoSy field on genus 0. (The total index equals chi on every shape, but that total depends only on
   the connection, not on the field: the edge terms cancel over a closed mesh, so it checks the transport, not the
   field. The per-face histogram is the field-dependent evidence.) On the torus the smoothest field has 12 singular
   faces (6 of +1/4, 6 of -1/4), not the singularity-free field that follows the principal directions. That is the
   known reason for a curvature-aligned variant (Knöppel's `s` parameter), which needs principal directions
   (section 0: missing). Time: 0.2-0.9 s at 6k-18k vertices and 4.0 s at 41k vertices, unoptimized (a 2n x 2n real
   Cholesky and 30 inverse iterations without a residual stop).
2. **An Instant-Meshes-style position field plus a naive extraction already give a usable quad-dominant mesh.**
   - 94.5-99.5 % quads;
   - corner deviation p50 0.7-2.4 and p95 2.4-13.5 degrees;
   - aspect p95 1.04-1.39;
   - 0 or 1 defect face;
   - one-sided Hausdorff output-to-reference 0.20-0.73 % of the diagonal, or 0.07-0.32 `s` (the largest on the
     filleted box);
   - symmetric Hausdorff, which includes the holes, 0.21-1.37 % (0.07-0.54 `s`; the noisy sphere's 1.37 % includes
     the noise).

   With the hole included, the bumpy sphere (0.54 `s`) already misses the stage 2 target of 0.5 `s`. The sphere's
   output has 2 non-manifold fan triangles, so the validity target of section 1 is not met yet. On the filleted box
   (CAD-like, smooth features) the result is close to ideal: 8 triangles and 7 irregular vertices.
3. **The main gap is the position-field singularities.** Irregular vertices are 52-134 on the curved shapes,
   against 8 field singularities (or 0 on the torus). Most of them are triangles balanced by valence-5 vertices.
   On the sphere there are 62 triangles but only 2 pentagons. By Euler (sum of (4 - valence) + sum of (4 - sides)
   = 4 chi), the remaining degree deficit sits in about 52 net valence-5 vertices. The paper's extraction cleanup
   and QuadriFlow's min-cost flow target exactly this. The spike has neither.
4. **Triangle pairing is no path to quality.** On isotropic triangles it gives rhombi: about 80 % quads, but p50
   26 degrees off 90 and only 17-19 % of corners within 70-110 degrees. Per face it has 23-29 % irregular
   vertices, against 0.4-6.3 % for the field route: about 5 x more on the spheres and 58 x on the box. Its one-sided
   Hausdorff is no better (0.22-1.01 %). It is fine as a fallback representation, not as a remesher.
5. **Pure quads by one Catmull-Clark step** roughly double the irregular count (15 on the box, 105-249 elsewhere)
   and halve the edge length. That is acceptable for the box-like case only, so pure quads need stage 3 proper.
6. **Not measured:** open boundaries, sharp (non-filleted) features, non-manifold or broken input, anisotropic or
   graded sizing, real scans, parallel speed and large inputs (>100k vertices). Those risks carry into stages 2-4.
7. **Provenance of the spike's formulas.** `MiddlePoint` (with its 1e-4 regularizer and the factors 2 and 0.25),
   the floor-plus-4x4 lattice search and the compatibility functions follow the Instant Meshes reference
   implementation's functions as recalled, not a re-derivation from the paper. This is allowed under its BSD
   licence, and the spike carries the attribution. Stage 2 either re-derives them from the paper or follows an
   owner ruling on the Enhancements clause (section 4, decision 4).

## 4. Staged plan

Each stage is a separate PR series that adds its sources to [SOURCES.md](../SOURCES.md) (Knöppel et al. 2013 in stage 1, Jakob et al. 2015 in stage 2), with the usual tests (CLAUDE.md: invariance over `Similarity.All()`, reuse,
determinism, the 5 s test rule) and a benchmark row in `benchmarks/`. A stage starts only after the go of the one
before it.

| stage | content | effort | risk | go / no-go gate |
|---|---|---|---|---|
| **0. Decisions** | The owner decisions below. | - | - | Decided by the owner. |
| **1. Cross field** | A `CrossField` class (additive): Knöppel 2013 smoothest field and curvature-aligned field, with boundary and feature-edge alignment as constraints (one linear solve). Principal directions (Rusinkiewicz 2004, from the paper) as a small new block. Per-face singularity indices. Manifold input only (reports `NotManifold`). | **M** (about 1-2 worker sessions) | low: the spike already matches theory | On the procedural suite (plus open shapes and sharp-feature boxes): the per-face index histogram matches theory (8 x +1/4 on genus 0); singularities <= ideal + 4 on the sphere, box and torus (curvature-aligned torus: 0); <= 2 s per 50k vertices on 4 cores (measured 4.0 s at 41k single-threaded; levers: a residual stop instead of a fixed 30 iterations, an n x n complex Hermitian factorization instead of the 2n x 2n real form, and a nested-dissection ordering); bit-for-bit under 2^k scales; deterministic. No-go if the curvature alignment fails on the torus or noisy inputs. |
| **2. Position field and extraction (quad-dominant)** | The Instant Meshes position field on a hierarchy, implemented from the paper (port only after the owner rules on the Enhancements clause). The paper's extraction with cleanup (collapse of near-coincident points, removal of valence-2 vertices, T-junction fixes). Boundaries as lattice constraints. Output into the stage 5 type. | **L** (about 2-3 sessions) | **medium**: position singularities (spike finding 3); determinism of a parallel Gauss-Seidel (needs a fixed colouring) | On the suite: >= 95 % quads; no face above 6 sides; irregular <= 2 x field singularities + 4; corner p95 <= 15 degrees; Hausdorff <= 0.5 `s`; a manifold polygon mesh; <= 5 s per 100k input vertices on 4 cores (a benchmark target; the tests use inputs small enough for the 5 s test rule). No-go: irregular > 4 x singularities after the cleanup means stage 3 (flow) becomes a prerequisite. |
| **3. Pure quads** | Option A: one Catmull-Clark split (S; doubles the irregular count). Option B: the QuadriFlow min-cost flow on the position field (BSD reference; a new flow solver), then pure quads by construction. Option C: local topology cleanup (collapses, rotations; Bommes, Lempfer, Kobbelt 2011, from memory). | A: S; B: L; C: M | B: a flow solver; C: no termination guarantee | 100 % quads; irregular <= field singularities + 10 % of stage 2's; the stage 2 quality gates still hold. No-go for B if the flow cannot reach it within 2 x stage 2's time. |
| **4. Features** | Sharp edges (`MeshTopology.CreaseEdges`, Remesher constraints) as field constraints and position-field snapping. Corners as forced singularities. Feature curves resampled first (see [REMESHER_CREASE_SLIVERS.md](REMESHER_CREASE_SLIVERS.md)). | **L** | **high**: noisy crease networks break into fragments (the same finding as the remesher); QuadWild-quality alignment needs patch tracing (XL) | On sharp CAD boxes, cylinders and gears: quad edges along > 95 % of feature length with angle error p95 <= 10 degrees; Hausdorff at the feature edges <= 0.1 `s`. No-go means smooth-only quad remeshing ships (features as a documented limit). |
| **5. API and writers** | Additive and compatible with 1.0.324: a new polygon container (`DPolyMesh`: compact face offsets plus vertex lists, face groups, `ToDMesh3()` that triangulates and records the triangle-pair map), OBJ polygon faces and PLY `vertex_indices` lists for n-gons, and `SimpleQuadMesh` from a pure-quad result. A `g3cli quadremesh` command. No change to `DMesh3`. | **S-M** | low; `dotnet pack` API validation catches breaks | The API validation passes; a round trip of OBJ and PLY through the readers keeps the faces (the polygon reader paths are added to `ReaderFuzzTests` if a reader is added). |

Owner decisions needed:
1. **Scope:** quad-dominant first (stages 1, 2, 5; recommended) or pure quads as the target (adds stage 3B, L).
2. **Mesh type:** a new additive polygon type (recommended); or `DMesh3` plus a triangle-pair map (keeps every
   DMesh3 operation, but n-gons cannot be stored); or extending `SimpleQuadMesh` (quads only, so it cannot hold a
   quad-dominant result).
3. **Output formats:** OBJ polygon faces and PLY face lists (recommended); is a polygon reader also wanted (it
   needs `ReaderFuzzTests`)?
4. **Port or paper:** Instant Meshes' Enhancements clause, and QuadriFlow's LICENSE / README mismatch: port after
   a per-file check, or implement from the papers only (recommended for the field and position field; both are
   short in the papers).
5. **Features in scope** (stage 4, high risk), and the budget: stages 1+2+5 are about 4-6 worker sessions, and the
   full plan about 8-12.

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
weights, `SparseCholesky`, `MeshHausdorffDistance`). It gives 94.5-99.5 % quads with corner p95 2.4-13.5 degrees
already without cleanup, and the field matches theory (8 faces of index +1/4 on every genus-0 shape). The one measured
gap (position singularities, 52-134 irregular vertices against 8 field singularities) is what the stage 2 cleanup
and, if needed, the stage 3B flow address. Stage 1 also stands alone: a cross field is useful for anisotropic
remeshing, hatching and seam layout. Defer pure quads (3B) and features (4) until stage 2's gates are measured. Do
not pursue MIQ / QEx / QuadWild / Lyon et al. now: each is XL, needs mixed-integer or ILP solvers, and has only GPL
or no reference code. Do not ship triangle pairing as a quad remesher.
