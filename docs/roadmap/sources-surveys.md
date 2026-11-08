# Sources and surveys

Part of the [roadmap](../ROADMAP.md).

- **Scope:** libraries and papers that may serve open items: geogram (owner 2026-11-03), other permissive libraries
  (survey 2026-11-03), RWTH Aachen papers (survey 2026-11-05), award-winning papers (October 2026 survey), PaMO
  (survey 2026-11-08).
- **Status:** documented only; nothing in these lists is read or planned unless an item below says so.
- **Related:** [SOURCES.md](../SOURCES.md) (what was used and how), the licence rules in
  [CLAUDE.md](../../CLAUDE.md).

### Open

- **Geogram:** the open items in [its table](#geogram-as-a-source-owner-2026-11-03) are to check first; none is read
  yet. A port is a decision with an A/B.
- **Other permissive libraries:** each row is to check, none read yet. PyMesh, ManifoldPlus and Directional: no
  LICENSE file found, not used until one is found.
- **RWTH Aachen papers:** documented only; the order if one is picked up is at the end of
  [their section](#rwth-aachen-papers-survey-2026-11-05).
- **Award-winning papers:** documented only. The affine heat method stopped at its gate (#371); generalized winding
  numbers for rational curves are Phase 5 next research item 3 ([research.md](research.md#phase-5-candidates-from-recent-research)).
- **PaMO (survey 2026-11-08):** paper only; a patent check comes before adopting any idea. Probe candidates are listed
  at the end of [its section](#pamo-parallel-mesh-optimization-survey-2026-11-08); none is planned.

### Done / history

#### PaMO, parallel mesh optimization (survey 2026-11-08)

S. Oh, X. Yuan, X. Wei, R. Shi, F. Xiang, M. Liu, H. Su, *PaMO: Parallel Mesh Optimization for Intersection-Free
Low-Poly Modeling on the GPU*, Pacific Graphics 2025 (Computer Graphics Forum 44(7));
[arXiv 2509.05595](https://arxiv.org/abs/2509.05595) v1, 6 Sep 2025, CC BY 4.0. Read: the full v1 text (HTML and the
PDF), owner request via the coordinator. No code read. Status: paper only; patent check before adopting any idea
(the paper says nothing about patents; its funding note names Hillbot Inc., whose CTO is an author).

The pipeline has three GPU stages (section numbers of the paper):

- **Re-meshing (Sec. 4):** an unsigned distance field on a narrow band. It is offset by ε = 0.9/R (input
  normalised to [0,1]³, R = 256 by default) and extracted with Dual Marching Cubes. Quad splits use an
  envelope/concavity test (Eqs. 14-15), and the interpolation weight is smoothed by a sigmoid (Eq. 13). Works on
  triangle soups. Freedom from self-intersections is shown on 10k random grids (8 failures for plain DualMC, 0 for
  theirs, Sec. 8.2); it is not proven. Floating point throughout.
- **Parallel simplification (Sec. 5, after Gautron and Kubisch 2023):**
  - The cost is QEM + 0.001 · edge length + 0.005 · Σ(1 − quality of the new triangles) (Eq. 1).
  - An edge collapses in a round if its 64-bit key (float cost in the high bits, edge id in the low bits) is the
    minimum over every triangle of its endpoints' one-rings (atomicMin). The link condition is checked.
  - After each round: an LBVH over the whole mesh and a Devillers-Guigue triangle test in mixed float/double
    precision. Shared-vertex 3D pairs are decided by a heuristic ("very short" segment = touching, App. 13.2).
    Measured on 10,100 pairs against CGAL: false-negative rate 0, false-positive rate 0.0114 (Table 7).
  - Offending collapses are undone; check and undo repeat (at most 3 passes, Table 8). An undone edge is barred
    from the next round.
- **Safe projection (Sec. 6):** moves the vertices with fixed connectivity. The energy is a two-sided approximate
  Chamfer distance to the input + membrane + bending + an IPC barrier, solved by projected Newton with additive
  CCD. "Safe" means intersection-free along the trajectory; there is no distance bound (CD and HD are measured
  only). Sharp features come back only as far as the fixed connectivity allows (Sec. 9).

Reported (RTX 4090 + i9-12900K, Table 1): 100 Thingi10K meshes with more than 300k faces, reduced to 1 % of the
faces.

| Method | Time (s) | HD (10⁻²) | CD (10⁻⁴) | min angle (°) | manifold | intersection-free |
|---|---|---|---|---|---|---|
| AlphaWrapping, then GK23 | 56.21 | 3.369 | 0.6451 | 4.081 | 100 % | 85 % |
| PaMO without stage 3 | 0.86 | 3.439 | 0.8468 | 3.317 | 100 % | 100 % |
| PaMO | 1.95 | 2.294 | 0.1846 | 2.994 | 100 % | 100 % |

On the full Thingi10K at 1 % (Table 3): 1.23 s, HD 2.907. At 10 % and 20 % (Table 2) PaMO's HD is worse than QEM's
(1.886 against 1.233 and 1.910 against 0.920); QEM there runs on the raw input and is intersection-free on only 12 %
of the meshes. Limitations (Sec. 9): no speed advantage on small meshes; gaps below the voxel size close, and cloth
becomes two layers; the simplifier never sees the input.

PaMO against Alpha Wrap 2.0 (our numbers: the 800 sample at 300/3000 and the T1 subset, see
[alpha-wrap-and-repair.md](alpha-wrap-and-repair.md)):

| | PaMO | Alpha Wrap 2.0 |
|---|---|---|
| closed, manifold | by construction (DualMC) + link condition | by construction (manifold step) + link condition |
| no self-intersections | measured (float test, 0 misses on their sample, heuristic at shared vertices) | guaranteed: exact predicates per collapse |
| distance to the input | none guaranteed; measured HD/CD | offset band; simplification certified within half the offset (one-sided Hausdorff upper bound) |
| resolution | voxel grid, R = 256 on [0,1]³ | α and offset relative to the bounding box (default 300/3000) |
| sharp features | stage 3 pulls vertices to the input; limited by fixed connectivity | none recovered (follow-up in the roadmap) |
| triangle quality | shape term in the cost; Table 1 min angle 3.0° | quality guard in the reducer; target p01 ≥ 10° and no triangle below 0.5° or above 170° |
| time | 1-2 s per mesh (GPU) | median about 7 s per mesh (4-core CPU, warm, T1 subset) |

Ideas for a CPU probe (none planned; each needs the patent check first):

1. **The round rule** (packed cost + id key, minimum over the one-ring triangles): independent collapses per round
   that do not depend on thread order. It fits option (A) of the parallel reducer if each collapse keeps our exact
   check, so no rollback is needed. Our serial reducer profile (2026-11-08) puts the self-intersection check at
   56-63 % of the reduction, which is what a parallel round would split.
2. **The length and shape terms in the cost:** cheap; their Table 5 shows triangles with a minimum angle below 10°
   going from 13.16 % to 2.81 % (below 1°: 0.56 % to 0.02 %). Our reducer
   rejects bad collapses with a guard instead; a cost term would change the order of collapses (not bit-identical,
   needs the quality and fidelity gates).
3. **Safe projection:** only reduced: a distance term plus exact rejection of moves that create intersections.
   After it, the Hausdorff certificate to the wrap has to be computed again. The IPC/CCD solver itself is not a
   fit (floating point, and costly on the CPU).

Correction of an earlier summary (the prior-art note for the parallel reducer): "rounds at local minima of the cost with a
self-intersection check and rollback" holds. But the check is floating point, not exact. "Hausdorff about that of
QEM" holds for stage 2 at 1 %; the full pipeline is about 32 % lower at 1 % and worse than QEM at 10-20 %.


#### Geogram as a source (owner 2026-11-03)

[Geogram](https://github.com/BrunoLevy/geogram) (Bruno Lévy, Inria) is BSD-3-Clause (`LICENSE` checked 2026-11-03,
"Copyright (c) 2000-2022 Inria"). Its own code may be read, analysed and ported; this replaces the earlier
"Geogram not read" notes (SOURCES.md, envelope). Rules:

- Only files under `src/lib/geogram` whose header carries the Inria BSD-3 notice; check each file's header before
  reading it.
- Never read `third_party/triangle` or the wrapper `delaunay/delaunay_triangle.*` (Triangle's non-free license and
  the mesher's clean room); `third_party/tetgen`, `delaunay/delaunay_tetgen.*` and whatever calls TetGen
  (AGPL) stay unread as well. Every other `third_party` library only after checking its own license (PoissonRecon,
  xatlas, HLBFGS, amgcl, OpenNL and so on).
- Ported code keeps the BSD-3 notice: the file header names the geogram source file and version, `docs/SOURCES.md`
  gets a row, and the package ships the notice (a `THIRD_PARTY_NOTICES` file; BSD-3 permits redistribution
  under BSL-1.0 with the notice). A port is a decision with an A/B, like any other change; reading for ideas
  needs no decision.
- Port into the exact code only under the exact-code rules (no FMA, no `float`, review of every bound).
- The mesher logs every geogram file it reads in MESHING2D_PROVENANCE.md, and the provenance check (6b) covers it.

Open items where geogram has an implementation to compare with or port from (to check first, not yet read):

| Open item | Geogram (to check) |
|---|---|
| Boolean performance (B) and the exact `Orient3d` hotspot ("kernel B", 1-limb fast path) | `numerics/PCK`, `expansion_nt`, `multi_precision`, `exact_geometry`, `interval_nt`: expansion arithmetic with an interval filter and homogeneous exact points |
| Mesh arrangements and booleans; SafeRounding and the research item "Practical 3D snap rounding" | `mesh/mesh_surface_intersection*`, `mesh_CSG*`, `triangle_intersection` (Lévy 2024/25, exact predicates and constructions for mesh CSG; SAFE_ROUNDING.md notes that it does not solve the rounding) |
| 2D mesher: snap speed, the exact kernel, constraint intersections | `delaunay/CDT_2d` (Lévy's own constrained Delaunay with exact predicates and constraint crossings; not the Triangle wrapper) |
| 2D mesher grading (2-2.5x triangle count on f-11 / f-40) | `delaunay/LFS` (local feature size) |
| Phase 5 #4: tetrahedral meshing | `delaunay/delaunay_3d`, `parallel_delaunay_3d` (own code; not the TetGen path) |
| Remesher quality and speed, Reducer | `mesh/mesh_remesh` (CVT / restricted Voronoi diagrams, `voronoi/`), `mesh_decimate`, `mesh_smoothing` |
| Mesh repair and hole filling (Phase 2) | `mesh/mesh_repair`, `mesh_fill_holes`, `mesh_preprocessing` |
| Parameterization (Phase 3) | `parameterization/` (LSCM, ABF++ and others) |
| Point clouds ("Later") | `points/` (Co3Ne and others) |

#### Other permissive libraries (survey 2026-11-03, owner request)

From [github.com/topics/geometry-processing](https://github.com/topics/geometry-processing), the first three pages by
stars. Each license comes from the repository's LICENSE file on its default branch, checked 2026-11-03. The same
rules as for geogram apply: check the per-file headers and the bundled third-party code before reading, a port
keeps the notice and gets a row in SOURCES.md, and the mesher logs what it reads.

| Library | License | Open items it may serve (to check, not yet read) |
|---|---|---|
| [elalish/manifold](https://github.com/elalish/manifold) | Apache-2.0 | Boolean performance (B): robust, fast mesh booleans in floating point with symbolic perturbation; a benchmark opponent and a source of ideas for the exact path |
| [pmp-library](https://github.com/pmp-library/pmp-library) | MIT | Remesher (uniform and adaptive remeshing), Reducer (decimation), hole filling, smoothing, parameterization |
| [hhoppe/Mesh-processing-library](https://github.com/hhoppe/Mesh-processing-library) | MIT (Microsoft) | Reducer and simplification: mesh optimization and progressive meshes (Hoppe 1993/1996), the authors' own code |
| [adobe/lagrange](https://github.com/adobe/lagrange) | Apache-2.0 | Mesh cleanup and repair (Phase 2), winding numbers (Phase 5 #5), remeshing |
| [nmwsharp/geometry-central](https://github.com/nmwsharp/geometry-central) | MIT | Already a source (Phase 3, intrinsic triangulations, heat method); the affine heat method (awards table) |
| [mlivesu/cinolib](https://github.com/mlivesu/cinolib) | MIT | Tetrahedral and hexahedral meshes (Phase 5 #4), polygon and polyhedral mesh utilities |
| [wjakob/instant-meshes](https://github.com/wjakob/instant-meshes) | BSD-3 | Quad remeshing ("Later") |
| [liminchen/OptCuts](https://github.com/liminchen/OptCuts) | MIT | Parameterization seams (Phase 3) |
| [patr-schm/TinyAD](https://github.com/patr-schm/TinyAD) | MIT | Foldover-free maps and other optimization (Phase 5 #7) |
| [microsoft/DirectXMesh](https://github.com/microsoft/DirectXMesh) | MIT | Vertex cache optimization and meshlets; no open item yet |
| [owensgroup/RXMesh](https://github.com/owensgroup/RXMesh) | BSD-2 | GPU mesh processing; ideas for parallel patterns only |
| [iShape-Rust/iOverlay](https://github.com/iShape-Rust/iOverlay) | MIT | 2D polygon booleans; Clipper2 already covers this |

Not usable: CGAL (GPL); libigl, TetWild and TriWild (MPL-2.0: not ported, implement from the paper); Easy3D and
QuadWild (GPL-3); DGtal (LGPL-3); mcut (LGPL-3 or commercial); MeshLib (non-commercial license); trueform (dual
license, non-commercial or commercial). PyMesh, ManifoldPlus and Directional: no LICENSE file found, so not used
until a license is found. The rest of the first three pages are learning or application projects without a
library to port.

#### RWTH Aachen papers (survey 2026-11-05)

These are papers from the Aachen graphics group (Kobbelt; and Campen, Bommes, Botsch and Trettner while there) that fit
the library and that the docs did not cite yet. EMBER, the mesh kernel, probabilistic quadrics, virtual source
propagation, surface map homology and Botsch-Kobbelt degenerate faces are already in the docs.
- **Sources:** web search and the arXiv API only. graphics.rwth-aachen.de and dblp are not reachable from the cloud
  sessions, so the list may be incomplete.
- **Licences:** a code licence named here is from a web search. Read the LICENSE file before using any code.

Documented only, nothing planned yet.

| Paper | Fit for us | Code |
|---|---|---|
| Nehring-Wirxel, Trettner, Kobbelt, *Fast Exact Booleans for Iterated CSG using Octree-Embedded BSPs*, Computer-Aided Design 135, 2021 (arXiv 2103.02486) | high for long chains of booleans (sweeps, milling simulation): a volumetric octree of BSPs over planes, fixed 256-bit integer arithmetic, up to 2.5 M mesh-plane cuts per second on one core. The EMBER group's predecessor; it is the chained-boolean case of EMBER idea 2 above. A patent check as for EMBER | none found |
| Campen, Kobbelt, *Polygonal Boundary Evaluation of Minkowski Sums and Swept Volumes*, CGF 29(5) (SGP), 2010 | high next to `MeshOffset` ("Later"): exact 3D Minkowski sums, offsets and swept volumes along a rigid motion, manifold and watertight; computes only in cells that hold boundary. We have 2D Minkowski sums only (Clipper2 add-on) | none (the authors' web service is offline) |
| Campen, Kobbelt, *Exact and Robust (Self-)Intersections for Polygonal Meshes*, CGF 29(2) (EG), 2010 | medium: an adaptive octree plus nested plane-based BSPs only in the cells with intersections, exact in fixed precision. Gives booleans and **outer hulls for mesh repair**: a candidate next to `MeshAutoRepair` and alpha wrap for "keep the outside, drop inner junk" | none found |
| Bischoff, Pavic, Kobbelt, *Automatic Restoration of Polygon Models*, TOG 24(4), 2005; Bischoff, Kobbelt, *Structure Preserving CAD Model Repair*, EG 2005 | medium: octree-volumetric repair that guarantees a closed manifold within a tolerance ε to the input and closes holes up to a size ρ. The CAD variant repairs only the critical regions (gaps, overlaps) volumetrically and keeps the rest of the tessellation. A design check for the repair pipeline: local volumetric repair instead of a global wrap | none found |
| Kobbelt, Botsch, Schwanecke, Seidel, *Feature Sensitive Surface Extraction from Volume Data* (Extended Marching Cubes), SIGGRAPH 2001 | medium: sharp features from a directed distance field plus normals inside the marching-cubes structure. Compare with our `DualContouring` (QEF, `Reducer` probabilistic quadrics) before deciding anything; possibly only a SOURCES note | none |
| Lyon, Campen, Kobbelt, *Quad Layouts via Constrained T-Mesh Quantization*, CGF 40(2) (EG), 2021 | for quad remeshing ("Later"): a conforming quad layout without T-junctions and with bounded angle distortion, by an ILP | none found |
| Cui, Gæde, Rotenberg, Kobbelt, Bærentzen, *Surface Reconstruction Using Rotation Systems*, TOG (SIGGRAPH Asia) 2024 (arXiv 2402.01893); follow-up *HRsR: Hierarchical Rotation System Reconstruction*, 2026 (arXiv 2606.07078) | medium for point clouds ("Later"): a mesh from points via a rotation system, with topology (genus) control; combinatorial rather than implicit | github.com/cuirq3/RsR, also in PyGEL; licence not checked |
| Schmidt, Pieper, Kobbelt, *Surface Maps via Adaptive Triangulations*, CGF 42(2) (EG), 2023, Günter Enderle best paper | low to medium: bijective maps between surfaces of the same genus on an adaptively refined triangulation; next to the parametrization work | github.com/patr-schm/surface-maps-via-adaptive-triangulations; README says MIT, LICENSE not checked |
| Speetzen, Kobbelt, *Freeform Shape Fabrication by Kerfing Stiff Materials*, CGF 43, 2024 | low: kerf patterns to bend sheet material into free-form shapes (fabrication) | not checked |

Order if one is picked up:
1. The Minkowski / swept volume paper together with the `MeshOffset` decision, because both are offsets on our exact boolean kernel.
2. The octree-BSP paper when chained booleans become a goal, with the EMBER re-evaluation after the full Thingi10K run.
3. The outer-hull and volumetric repair papers as design input for the repair pipeline after the Thingi10K bugfix task.

#### Award-winning papers (October 2026 survey)

Best papers and runners-up of SGP, SIGGRAPH, SIGGRAPH Asia and Eurographics, 2016-2026, that fit the library.
"No code found" means none in a web search (paper pages, author pages, GitHub); check with the authors before
starting. The lists for SIGGRAPH 2021 / 2023 and Eurographics 2021-2023 could not be retrieved completely.
Documented only, nothing planned yet.

Without a public implementation:

| Paper | Award | Code | Fit |
|---|---|---|---|
| Shi, Alliez, Desbrun, Bao, Huang, *Symmetry and Orbit Detection via Lie-Algebra Voting*, CGF 2016 | SGP best paper | only an unofficial student project (CGAL based); the authors published a reproducibility addendum with parameters | high: symmetry axes, repeated parts, mirroring in CAD / scan data; next to `PrimitiveFit3` |
| Chen, Schaefer, Desbrun, *Lightning-fast Method of Fundamental Solutions*, TOG 2024 | SIGGRAPH 2024 best paper | none found for this paper (the follow-up boundary element method, 2025, has code) | medium: boundary-only PDE solves (Laplace, Helmholtz) for harmonic fields and interpolation; overlaps with Phase 5 #6 |
| Born, Schmidt, Campen, Kobbelt, *Surface Map Homology Inference*, CGF 2021 | SGP 2021 best paper | none found | low: niche (correspondence of handles between surfaces); `MeshDiscCut` has homology building blocks |
| Kilian, Ramos Cisneros, Müller, Pottmann, *Meshes with Spherical Faces*, TOG 2023 | SIGGRAPH Asia 2023 best paper | none found | low to medium: free-form architecture, panel fabrication |

With a permissive implementation (portable, see Licenses):

| Paper | Award | Code | Fit |
|---|---|---|---|
| Soliman, Sharp, *The Affine Heat Method*, CGF 2025 | SGP 2025 best paper | geometry-central (MIT, `LogMapStrategy::AffineLocal`) | high: a more accurate log map, above all near the cut locus; an upgrade of `VectorHeatMethod` |
| Spainhour, Gunderman, Weiss, *Robust Containment Queries over Collections of Rational Parametric Curves via Generalized Winding Numbers*, TOG 2024 | SIGGRAPH 2024 best paper | Axom (BSD) | high: inside / outside of regions bounded by NURBS and Bézier curves (`NURBSCurve2`, CAD contours); follow-up for trimmed NURBS surfaces (2025) |
| Trettner, Bommes, Kobbelt, *Geodesic Distance Computation via Virtual Source Propagation*, CGF 2021 | SGP 2021 second place | MIT (C++ library of the authors) | medium: geodesic distances next to the heat method |
