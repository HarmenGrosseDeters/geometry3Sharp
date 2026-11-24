# Untapped Research Across Mathematics, Physics, Biology, and Chemistry for Computational Geometry

**Research survey & ROI evaluation — 21 November 2026.** Pure research and documentation: no library code changes, no benchmarks run. Process tier: **S (docs-only)**; any future spike or implementation requires its own scoped brief, upper-bound measurement, license check, and tier-appropriate review under [AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md).

## Relationship to existing research notes

[CROSS_FIELD_GEOMETRY.md](CROSS_FIELD_GEOMETRY.md) worked **inside-out**: it started from `geometry3Sharp`'s hard open problems (3D snap rounding, Q17 grid refinement, envelopes, open-input inside/outside) and searched other fields for mechanisms.

This survey works **outside-in**: it scans recent breakthroughs (**2021–2026**) across **mathematics, physics, biology, and chemistry** for geometric, topological, and algorithmic ideas that have **almost no practical adoption in mainstream computational geometry or 3D mesh libraries** (CGAL, libigl, geometry-central, Geogram, Open3D, or .NET). Every candidate is evaluated on two axes:

1. **General-field ROI:** whitespace and structural impact across computational geometry, CAD, robotics, simulation, additive manufacturing, and 3D shape analysis.
2. **`geometry3Sharp` ROI:** concrete synergy with this library's existing building blocks ([ROADMAP.md](../ROADMAP.md), [OPEN_TOPICS.md](../OPEN_TOPICS.md), [research.md](../roadmap/research.md)) under its hard engineering constraints: **CPU-only**, **`netstandard2.0` + `net10.0`**, **bit-for-bit determinism**, **no neural-training dependency**, and **Boost-compatible (paper-only or MIT/Apache/BSD) licensing**.

### Evidence discipline

- **Known:** established in the cited source under its stated hypotheses.
- **Transfer:** established in its home discipline; untested in practical computational geometry / mesh libraries.
- **Speculative:** algorithmic speedup, 3D generalization, or practical utility in general geometry processing is unproven.
- Citations `[R1]`–`[R46]` were verified against public arXiv, publisher, and author pages (abstracts, HTML/PDF excerpts, metadata). No external GPL/copyleft or restricted source code was inspected or ported. Before implementing any item, its primary paper must be read in full and any software/patent constraints checked per [CLAUDE.md](../../CLAUDE.md).

---

## Executive summary & ROI verdict

The highest-ROI opportunities are **not** exotic replacements for exact predicates or spatial trees. They fall into three clear pockets where other sciences solved a problem that computational geometry still treats with ad-hoc heuristics:

1. **Fast, deterministic, complete 3D invariants from chemistry** — specifically **Pointwise Distance Distributions (PDD)** and **Atomic Cluster Expansion (ACE)** — replace decades-old heuristic descriptors (FPFH, SHOT, pairwise histograms) with mathematically complete, continuous, linear-to-near-linear $O(3)$-invariant representations at modest implementation cost.
2. **Parallel, operator-adapted elliptic preconditioning from numerical homogenization** — **Maximin-ordered Sparse Inverse Cholesky / Multiscale Cholesky** — directly attacks the sequential elimination-tree bottleneck in sparse PDE solves (`SparseCholesky`, `BiharmonicMeshDeformer`, `CrossField`, `VectorHeatMethod`).
3. **Topology-without-clean-meshes and non-standard topology from GMT, algebraic topology, and biology** — **Discrete Varifolds / Flagfolds** (curvature and sharp features on broken soups without connectivity), **Cellular Sheaf $H^1$ obstruction diagnostics** (pinpointing why quad/orientation/seam constraints fail), **Circuit Topology / Knotoids** (open-curve entanglement), and **Scutoid curved-shell tessellations** (volumetric meshing of curved thick shells without extruded-prism distortion).

### Top-ROI recommendations: two lenses

#### A. Highest ROI for `geometry3Sharp` specifically (depth + existing synergy)

| Rank | Candidate | Why it wins here | First cheap falsification probe |
|---|---|---|---|
| **1** | **#2 Operator-Adapted / Multiscale Sparse Cholesky** (Applied Math) | Directly upgrades elliptic solves in `BiharmonicMeshDeformer`, `CrossField`, `VectorHeatMethod`, and the open `SparseCholesky` parallelization track; fully deterministic on CPU. | Prototype maximin ordering + local KL/multiscale preconditioner + deterministic PCG on the 100k-vertex `BiharmonicDeformationBenchmark` mesh; kill if total cold solve does not beat current `SparseCholesky` by ≥ 2× at equal residual. |
| **2** | **#11 Pointwise Distance Distributions (PDD)** (Crystallography / Chemistry) | Tiny implementation surface (builds directly on existing KD/BVH nearest-neighbor queries), deterministic, scale-normalized, zero training; immediately unlocks rigid-isometry deduplication, symmetry detection, and metamorphic/corpus indexing across meshes, point clouds, and TPMS lattices. | Implement finite-set PDD + 1D/EMD comparison on 100 Thingi10K meshes under `Similarity.All()` + vertex jitter; verify bit-identical invariance under rigid transforms/re-indexing and measure precision/recall + runtime vs. ICP/Hausdorff pre-filtering. |
| **3** | **#1 Discrete Varifolds & Flagfolds** (Geometric Measure Theory) | Directly targets two open pain points: sharp-feature detection on broken inputs (HP 4, `MeshAlphaWrap` feature pass) and connectivity-free curvature on `PointCloudSurface` / triangle soups without needing manifold repair first. | Evaluate regularized varifold first-variation / second-fundamental-form estimator on broken CAD soups from the lever-6 sharp-feature set using existing `DMeshAABBTree3` ball queries; kill if crease recall/precision does not clearly beat local PCA / dihedral heuristics or runtime exceeds 20% of `MeshAlphaWrap`. |
| **4** | **#3 Cellular Sheaf $H^1$ Obstruction Diagnostics** (Applied Topology) | Low-code diagnostic layer over existing sparse linear algebra that explicitly locates *why* `QuadRemesher` stage 3B (54% fallback rate) or non-manifold orientation repair fails, separating genuine topological obstructions from numerical noise. | Build an $O(2)/C_4$ cellular-sheaf Laplacian on the failing stage-3B `QuadRemesher` meshes; check whether the smallest harmonic/cohomological modes localize the exact regions causing integer-map failure. |

#### B. Highest ROI whitespace for computational geometry as a field (greenfield impact)

| Rank | Candidate | Whitespace unlocked across CAD / Robotics / Simulation / AM | Why nobody owns it yet |
|---|---|---|---|
| **1** | **#12 Atomic Cluster Expansion (ACE) for 3D Geometry** (Computational Chemistry) | Complete, deterministic, $O(1)$-per-basis-function $O(3)$-invariant local 3D patch/point-cloud descriptor replacing FPFH/SHOT/Spin Images for registration, CAD feature recognition, and training-free shape matching. | Trapped behind quantum-chemistry notation (Clebsch–Gordan trees, DFT potentials). |
| **2** | **#11 Pointwise Distance Distributions (PDD)** (Crystallography) | Continuous, generically complete metric space for finite and periodic 3D point configurations—instant near-duplicate detection and symmetry/periodicity classification at database scale. | Built for crystal CIF databases; virtually unknown in graphics/vision/CAD. |
| **3** | **#8 Circuit Topology & Bonded Knotoids** (Structural Biology) | Polynomial-time topological classification and entanglement metrics for *open* 3D curves and self-contacting sheets (cables, harnesses, robotics, sutures, toolpaths, cloth)—where classical knot theory fails. | Confined to protein/chromosome bioinformatics (PyMOL/Hi-C). |
| **4** | **#6 Scutoids & Curved-Shell 3D Tessellation** (Developmental Biology) | Watertight, distortion-minimizing volumetric tessellation of thick curved shells via apico-basal T1 transitions—solving a chronic failure mode of boundary-layer prism/hex extrusion. | Studied in developmental biology/bioRxiv without a general CAD/FEM shell mesher. |

### Anti-hype filter: impressive research with low near-term ROI here

- **Deterministic almost-linear min-cost flow (#16b, FOCS 2024):** Historic complexity breakthrough ($m^{1+o(1)}$), but dynamic expander hierarchies carry enormous constant factors that lose to network simplex / cost-scaling on typical 10k–200k-face meshes. **Watchlist only.**
- **Sum-of-Squares / SDP collision & distance certificates (#5b):** Powerful for curved NURBS/polynomial patches, but SDP solves are orders of magnitude slower than Tight-Inclusion (`ContinuousCollision`) on linear triangle meshes. Only pays if higher-order curved primitives become a core representation.
- **Stealthy hyperuniformity ground-state optimization (#14b):** Collective-coordinate optimization is too slow for interactive 3D meshing; simpler Voronoi/farthest-point blue-noise relaxation captures most practical sampling gains unless photonic/acoustic isotropy is specifically required.
- **3D Method of Fundamental Solutions (#15):** Exciting for clean/smooth boundaries, but external-source placement is fragile on dirty, thin-walled, or self-intersecting triangle soups without a prior clean wrap.

---

## Master ROI comparison matrix

Ratings are ordinal estimates following [CROSS_FIELD_GEOMETRY.md](CROSS_FIELD_GEOMETRY.md), where **ROI = (Expected Payoff × Feasibility) / (Implementation + Maintenance Cost)**.

| # | Field | Candidate | Status | General-Field Whitespace | `geometry3Sharp` Synergy | CPU & Determinism Fit | First-Probe Effort | Overall ROI Tier |
|---|---|---|---|---|---|---|---|---|
| 1 | Math (GMT) | Discrete Varifolds & Flagfolds | Transfer | High | High (`MeshAlphaWrap`, `PointCloudSurface`, `MeshPrincipalCurvature`) | High | Low–Mod | **Tier 1 (High)** |
| 2 | Math / Physics | Operator-Adapted / Multiscale Sparse Cholesky | Transfer | High | Very High (`SparseCholesky`, `BiharmonicMeshDeformer`, `CrossField`) | High (with fixed-order PCG) | Moderate | **Tier 1 (Very High)** |
| 3 | Math (Topology) | Cellular Sheaves & $H^1$ Obstructions | Transfer | High | High (`QuadRemesher`, `CrossField`, `MeshRobustRepair`) | High | Low | **Tier 1 (High)** |
| 4 | Physics (Cond. Matter) | Disclination Density Tensor $D_{ij}$ | Transfer | Medium–High | Medium (`CrossField`, future 3D frame fields) | High | Low–Mod | **Tier 2 (Medium–High)** |
| 5 | Math (Real Alg. Geom.) | Excursion-Aware Isotopy & SOS Certificates | Transfer / Spec. | High (implicits/CAD) | Medium–High (`ContouringTopologyCertificate`, `MeshOffset`) | High (subdivision) / Low (SDP) | Mod–High | **Tier 2 (Split: 5a Med-High / 5b Low)** |
| 6 | Biology (Dev. Bio.) | Scutoids & Curved-Shell 3D Tessellation | Speculative | Very High | Medium (`MeshOffset`, `MeshTetrahedralization`, shells) | High | Moderate | **Tier 2 (High Whitespace)** |
| 7 | Biology (Biophysics) | Tension Triangulations & Isogonal Modes | Transfer | Medium–High | Low–Med (polygonal remeshing, `DPolyMesh`) | High | Moderate | **Tier 2 (Medium)** |
| 8 | Biology (Struct. Bio.) | Circuit Topology & Bonded Knotoids | Transfer | Very High (open curves) | Low–Med (new domain: curves, sweeps, toolpaths) | High | Low | **Tier 1 (Field) / Tier 2 (Repo)** |
| 9 | Biology (Morphogenesis) | Incompatible Elasticity / Inverse Metric Design | Transfer | High (4D print / shells) | Low–Med (inverse of parameterization/ARAP) | Medium (nonlinear buckling) | High | **Tier 3 (Niche / R&D)** |
| 10 | Biology (Transport) | *Physarum* Dynamic Optimal Transport Networks | Transfer | High (routing / AM infill) | Medium (reuses sparse linear solves & meshes) | High | Low–Mod | **Tier 2 (Medium–High)** |
| 11 | Chemistry (Cryst.) | Pointwise Distance Distributions (PDD) | Transfer | Very High | High (point clouds, TPMS, corpus/metamorphic QA) | Very High | Low | **Tier 1 (Very High)** |
| 12 | Chemistry (Comp. Chem.) | Atomic Cluster Expansion (ACE) Local 3D Basis | Transfer | Very High | Medium–High (`PointCloudNormals`, `MeshICP`, feature ID) | Very High | Low–Mod | **Tier 1 (Very High)** |
| 13 | Biochemistry | Persistent Topological & Hodge Laplacians | Transfer | High | Medium (`MeshWallThickness`, cavities, shape analysis) | High | Moderate | **Tier 2 (Medium–High)** |
| 14 | Materials Chem. | Reticular Periodic Nets (EPINET/Systre) & Hyperuniformity | Transfer / Spec. | High (AM metamaterials) | Medium (`TpmsLattice`, sampling) | High (nets) / Low (stealthy opt.) | Low (nets) / High (opt.) | **Tier 2 (Split: 14a Med-High / 14b Low)** |
| 15 | Applied Math / Physics | AAA Rational & Lightning / MFS Mesh-Free PDEs | Transfer | High (2D/clean 3D PDEs) | Low–Med (2D conformal / clean interior fields) | High | Moderate | **Tier 2 (2D) / Tier 3 (3D)** |
| 16 | TCS / Discrete Geom. | Safe-Pruning Grid Compression & Det. Min-Cost Flow | Transfer / Spec. | Medium–High | Medium (`SNAP_ROUNDING_3D`, `QuadFlowRepair`) | Very High | Low (pruning) / Very High (flow) | **Tier 2 (16a) / Tier 3 (16b Watchlist)** |

*(Two late-2026 supporting pointers—DEC convergence on uniformly acute triangulations `[R45]` and power-diagram variable-radius offsetting / UDF medial-axis reconstruction `[R46]`—are folded into Sections 5, 16, and the supporting-pointers note below.)*

---

## Part I — Mathematics & Physics

### 1. Discrete / Volumetric Varifolds and Flagfolds
- **Field & sources:** Geometric Measure Theory — Buet, Leonardi & Masnou (`[R1]`, 2022), Buet & Rumpf (`[R2]`, 2022), Buet & Pennec (*Flagfolds*, `[R3]`, 2023), Buet, Leonardi, Masnou & Sagueni (*Approximate mean curvature flows of a general varifold*, `[R4]`, 2025), Campbell et al. (*Compression of Currents and Varifolds*, `[R5]`, 2024–2025).
- **Classification:** Known in GMT; **transfer** to broken-mesh and point-cloud geometry processing.
- **Core mechanism:** A $d$-varifold represents geometry as a nonnegative Radon measure $V$ on $\mathbb{R}^3 \times G_{d,3}$ (spatial position paired with an *unoriented* $d$-plane in the Grassmannian). Because neither manifold connectivity nor consistent normal orientation is required, the **regularized first variation** $\delta V * \rho_\varepsilon$ and weak second fundamental form yield **provably convergent mean curvature, Gaussian curvature, boundary/crease signals, and Brakke-type curvature flows** directly on unoriented point clouds, volumetric cells, or self-intersecting triangle soups, provided the regularization scale $\varepsilon$ dominates discretization spacing $h$. **Flagfolds** `[R3]` embed all Grassmannians $G_{d,3}$ simultaneously into a stratified space of covariance matrices, unifying 2D surfaces, 1D crease curves, and 0D corners in one connectivity-free object.
- **General CG payoff:** Rigorous differential geometry on raw scans, unoriented triangle soups, and mixed-dimension CAD wireframes without fragile pre-repair or normal-orientation pipelines.
- **`geometry3Sharp` synergy:** Directly serves `MeshAlphaWrap` sharp-feature detection (HP 4 in [OPEN_TOPICS.md](../OPEN_TOPICS.md)), `PointCloudSurface` ([POINT_CLOUD_RECONSTRUCTION.md](../design/POINT_CLOUD_RECONSTRUCTION.md)), and `MeshPrincipalCurvature` on messy inputs. Pairs naturally with `DMeshAABBTree3` / `PointHashGrid3d` radius queries.
- **Why unused & catches:** Published in pure-analysis journals with small prototype demos; naive pairwise kernel evaluation is $O(N^2)$ (requiring compactly supported kernels + spatial trees or Nyström compression `[R5]`), and scale selection $\varepsilon$ must be tied to local feature size / sampling density to avoid over-smoothing thin walls.
- **First falsification probe:** Compute compactly supported varifold first variation and Flagfold stratification ratios on broken Thingi10K / CAD lever-6 meshes; compare crease detection F1 score and runtime against existing dihedral/PCA heuristics.

### 2. Operator-Adapted Sparse Inverse Cholesky (Gamblets / KL-Minimization)
- **Field & sources:** Numerical Homogenization & Information Geometry — Schäfer, Katzfuss & Owhadi (`[R6]`, SIAM J. Sci. Comput. 2021), Chen, Schäfer, Huang & Desbrun (*Multiscale Cholesky Preconditioning for Ill-Conditioned Problems*, `[R7]`, ACM TOG 2021), Schäfer & Owhadi (`[R8]`, SIAM J. Sci. Comput. 2024), Chen, Owhadi & Schäfer (`[R9]`, Math. Comp. 2025).
- **Classification:** Known in numerical PDEs / GP statistics; **transfer** to production geometry kernels.
- **Core mechanism:** For elliptic operators (Laplace, screened Poisson, biharmonic), the *inverse* Cholesky factor $U$ of the stiffness or Green's matrix decays exponentially away from local multiscale neighborhoods when degrees of freedom are ordered by a **geometric maximin ordering** (coarse-to-fine spatial hierarchy). Minimizing the Kullback–Leibler divergence $\mathbb{D}_{\mathrm{KL}}$ over a fixed sparsity pattern decouples into **$N$ completely independent, small dense linear solves—one per column**—running in $O(N \log^{2d}(N/\varepsilon))$ time with **embarrassing parallelism** (no sequential elimination tree) and condition-number stability even on badly graded or high-aspect-ratio meshes `[R7]`.
- **General CG payoff:** Fast, scalable, deterministic parallel elliptic solvers and preconditioners for 3D volumetric FEM, biharmonic deformation, geodesic heat flows, and cross fields where classical direct Cholesky stalls on fill-in and serial tree dependencies.
- **`geometry3Sharp` synergy:** Very high. Directly relevant to `SparseCholesky`, `SparseHermitianCholesky`, `BiharmonicMeshDeformer` ([BIHARMONIC_DEFORMATION.md](../design/BIHARMONIC_DEFORMATION.md), ~7 s cold on 100k vertices), `CrossField` ([QUAD_REMESHING.md](../design/QUAD_REMESHING.md)), and `VectorHeatMethod`. Bit-for-bit parallelism is achievable by ordering each column deterministically and using fixed-order reductions in outer PCG.
- **Why unused & catches:** Classical AMD/nested-dissection direct Cholesky is entrenched in Eigen/SuiteSparse; KL/multiscale Cholesky produces an *approximate* factor / preconditioner (not an exact direct solve), so accuracy requires outer PCG iterations, and the maximin ordering overhead must be amortized or kept cheap via an octree/BVH.
- **First falsification probe:** Prototype reverse-maximin ordering + multiscale incomplete Cholesky preconditioner + deterministic PCG on the 100k-vertex `BiharmonicDeformationBenchmark` case; stop if total cold factorization + solve time is not at least 2× faster than current `SparseCholesky` at identical residual tolerance.

### 3. Cellular Sheaves and Persistent Sheaf Laplacians (Obstruction Cohomology)
- **Field & sources:** Applied Algebraic Topology — Hansen & Ghrist (`[R10]`, 2021), Wei & Wei (`[R11]`, 2023), Barbero et al. (`[R12]`, 2022), Riess et al. (`[R13]`, 2025).
- **Classification:** Known in applied topology; **transfer** to deterministic mesh/field diagnostics.
- **Core mechanism:** A cellular sheaf $\mathcal{F}$ assigns a vector space (stalk) to every vertex, edge, and face of a mesh together with linear **restriction maps** encoding local compatibility—such as orthogonal parallel transport, $C_4$ cross-field frame transitions, UV chart rotations, or orientation sign compatibility across non-manifold edges. The 0-th sheaf Laplacian $L_{\mathcal{F}}^0 = \delta^0{}^\top \delta^0$ has kernel $\ker L_{\mathcal{F}}^0 = H^0$ equal to the space of globally consistent sections; small positive eigenvalues quantify global frustration; and **first cohomology $H^1$** explicitly identifies the minimal cycles/seams causing topological obstruction.
- **General CG payoff:** Unifies orientation consistency on non-manifold meshes, frame-field holonomy, seamless UV period-jump feasibility, and local patch-stitching compatibility under a single linear-algebraic operator.
- **`geometry3Sharp` synergy:** High diagnostic value for `CrossField`, `QuadRemesher`, and `QuadFlowRepair` ([QUAD_REMESHING.md](../design/QUAD_REMESHING.md), where stage 3B falls back on 54% of cases) and for diagnosing structurally unorientable/inconsistent patches in `MeshRobustRepair`.
- **Why unused & catches:** Almost all recent sheaf-Laplacian papers targeted graph neural networks rather than classical geometry kernels. Linear/orthogonal sheaves diagnose real-valued compatibility cleanly, while integer-lattice integrability still needs an integer/flow solver on top.
- **First falsification probe:** Construct the $O(2)/C_4$ sheaf Laplacian on the failing stage-3B `QuadRemesher` meshes and measure whether $H^1$ support / smallest non-zero eigenvectors isolate the exact faces/seams that trigger fallback.

### 4. Disclination Density Tensor $D_{ij}$ and Topological Defect Kinematics
- **Field & sources:** Condensed-Matter Physics — Schimming & Viñals (*Kinematics and dynamics of disclination lines in 3D nematics*, `[R14]`, Proc. R. Soc. A 2023; *A tensor density measure of topological charge in 3D nematic phases*, `[R15]`, Proc. R. Soc. A 2024, arXiv:2308.04496).
- **Classification:** Known in liquid-crystal physics; **transfer** to cross/frame-field meshing.
- **Core mechanism:** From spatial derivatives of a continuous nematic order-parameter tensor $Q(x)$, Schimming & Viñals construct a gauge-invariant **disclination density tensor** $D_{ij}$ and a path-independent contour integral for topological charge. Near a 3D line defect, the rank-1 dyadic decomposition $D_{ij} \approx s\,\Omega_i t_j$ simultaneously extracts the **defect tangent $t$** and **Frank rotation vector $\Omega$**, and yields exact conservation and annihilation kinematics for defect loops.
- **General CG payoff:** Continuous, gauge-invariant singularity detection and dipole-annihilation cleanup for 2D/3D frame fields, replacing brittle combinatorial per-edge holonomy matching in quad and hex-dominant meshing.
- **`geometry3Sharp` synergy:** Medium. Relevant to reducing spurious irregular vertices in `CrossField` / `QuadRemesher` (currently 6–12× the singularity count on stage 2, [QUAD_REMESHING.md](../design/QUAD_REMESHING.md)) and foundational if 3D octahedral frame fields are ever added on top of `MeshTetrahedralization`.
- **Why unused & catches:** Published in soft-matter physics and never bridged into CAD meshing. Catch noted by the authors `[R15]`: $D_{ij}$ can be non-zero in defect-free double-twist/double-splay configurations, so extracted candidates must still be verified with the exact topological contour integral.
- **First falsification probe:** Evaluate the 2D/surface analog of $D$ on `CrossField` outputs where stage 2 produces excess irregular vertices; test whether tensor-guided dipole cancellation lowers irregular-vertex counts without degrading field alignment.

### 5. Excursion-Aware Simultaneous Isotopic Subdivision & Sum-of-Squares Certificates
- **Field & sources:** Real Algebraic Geometry — (a) Burr & Byrd (*Certified simultaneous isotopic approximation of curves via subdivision*, `[R16]`, arXiv:2302.04908, 2023–2024); (b) Marschner, Zhang, Solomon & Tamstorf (*Sum-of-Squares Geometry Processing*, `[R17]`, ACM TOG 2021; *SOS Collision Detection for Curved Shapes and Paths*, `[R18]`, SIGGRAPH 2023). *(Supporting note: late-2026 DEC convergence proof for Hodge–Dirac/Hodge–Laplace via uniformly acute triangulations `[R45]` similarly shows when discrete operators certify continuum topology/PDEs.)*
- **Classification:** **(5a) Known in 2D algebraic curves; speculative in 3D/general implicits.** **(5b) Known; transfer to curved CAD primitives, poor fit for linear triangles.**
- **Core mechanism:**
  - **(5a) Excursion-aware isotopy `[R16]`:** Explicitly formalizes *excursions* (a zero-set dipping across a cell face and returning—the exact silhouette double-crossing bottleneck that halted `ContouringTopologyCertificate` stage 2 refinement in [CONTOURING_TOPOLOGY.md](../design/CONTOURING_TOPOLOGY.md)) and certifies *simultaneous* ambient isotopy of multiple intersecting implicit varieties so no true intersections vanish and no spurious crossings appear.
  - **(5b) Positivstellensatz / SOS certificates `[R17][R18]`:** Recasts minimum distance, envelope containment, and swept non-intersection of polynomial patches into fixed-degree convex semidefinite programs (SDPs) that output a machine-checkable algebraic positivity proof.
- **General CG payoff:** (5a) Certified boolean/multi-material contouring of implicit and TPMS fields without over-refining benign excursions; (5b) certified exact queries on curved spline/polynomial patches without dense tessellation.
- **`geometry3Sharp` synergy:** **(5a) Medium–High** for `ContouringTopologyCertificate` Option B (implicits / `TpmsLattice`) and `MeshOffset` ([MESH_OFFSET.md](../design/MESH_OFFSET.md)); **(5b) Low** today because `ContinuousCollision` ([CCD.md](../design/CCD.md)) already handles linear triangles in microseconds, and SDP dependencies are heavy for `netstandard2.0`.
- **First falsification probe (5a only):** Test whether excursion-classified cell pairs can certify silhouette double-crossings on the 2D/3D analytic TPMS fixtures from `benchmarks/ContouringTopology` without triggering unbounded octree subdivision.

---

## Part II — Biology & Biophysics

### 6. Scutoids and Curved-Shell 3D Voronoi Tessellations
- **Field & sources:** Developmental Biology — Gómez-Gálvez et al. (*Nature Comm.* 2018), local/global epithelial 3D packing reorganization (`[R19]`, *Development* 2024), *3DTubularVoronoi* (`[R20]`, bioRxiv 2025).
- **Classification:** Known in biological modeling; **speculative** as a general CAD/FEM mesher.
- **Core mechanism:** Between two curved parallel/offset surfaces (e.g., apical and basal tissue boundaries of thickness $h$ on a curved tube or saddle), cells minimize interfacial energy not by remaining distorted prisms, but by performing **apico-basal T1 neighbor transitions partway through the shell thickness**. The resulting polyhedra ("scutoids") allow the inner and outer boundary meshes to have different, curvature-adapted 2D Voronoi topologies connected watertightly across the volume.
- **General CG payoff:** **Curved thick-shell volumetric meshing, boundary-layer/offset-volume tessellation, and curved masonry/architectural panel packing**—precisely where classical normal-extruded prism/hex layers pinch or self-intersect at high curvature.
- **`geometry3Sharp` synergy:** Medium (future extension). Connects conceptually to `MeshOffset` ([MESH_OFFSET.md](../design/MESH_OFFSET.md)), `MeshWallThickness`, and `MeshTetrahedralization` ([TET_MESHING.md](../design/TET_MESHING.md)), though curved-shell polyhedral meshing is not currently on the roadmap.
- **Why unused & catches:** Biology papers focus on cylinders, spheres, and biological energy functionals rather than general arbitrary-genus CAD shells with sharp creases; converting curved Voronoi scutoid cells into clean planar-faced polyhedra or valid FEM tets/hexes requires careful handling of non-planar twisted lateral faces.
- **First falsification probe:** Generate inner/outer offset surfaces of a high-curvature torus or saddle (`MeshOffset`), place independent centroidal Voronoi seeds on each surface, and test whether 3D restricted Voronoi tessellation inside the shell produces watertight scutoid cells with strictly better aspect ratio than normal prism extrusion.

### 7. Tension Triangulations and Isogonal Modes (Reciprocal Force-Balance Geometry)
- **Field & sources:** Morphogenesis / Biophysics — Brauns, Claussen, Lefebvre, Streichan & Shraiman (`[R21]`, *eLife* 2024; *PNAS* 2024).
- **Classification:** Known in epithelial mechanics; **transfer** to polygonal remeshing and architectural geometry.
- **Core mechanism:** A 2D polygonal mesh in force balance is dual to a **tension triangulation** (a Maxwell–Cremona reciprocal diagram whose edge lengths equal the tensions of the dual polygonal edges, and whose angles fix all junction angles). Deformations split cleanly into (1) changing the tension triangulation (driving controlled T1 edge flips when a dual edge collapses) and (2) **isogonal modes** (power-diagram weight shifts) that deform cell aspect ratios and areas while keeping every vertex angle and force-balance equation intact.
- **General CG payoff:** Angle-preserving polygonal remeshing, smooth T1 topological transitions without mesh inversion, and reciprocal force-balanced architectural shell / grid-shell optimization.
- **`geometry3Sharp` synergy:** Low–Medium. Only connects to `DPolyMesh` ([QUAD_REMESHING.md](../design/QUAD_REMESHING.md)) or future architectural/polygonal remeshing; no current internal caller.
- **Why unused & catches:** Published in developmental biology (*eLife*/*PNAS*); strictly 2D/surface-intrinsic so far, and requires maintaining a valid dual power/Delaunay triangulation (which constrains non-convex cell shapes).
- **First falsification probe:** Prototype 2D isogonal weight smoothing on a planar polygonal mesh and compare minimum angle preservation vs. standard Laplacian/centroidal Voronoi smoothing.

### 8. Circuit Topology and Bonded Knotoids for Open 3D Curves
- **Field & sources:** Structural Biology & Genomics — Mashaghi et al. (*Circuit Topology* of chromosomes and proteins, `[R22]`, 2022–2026), *Topology and Algebra of Bonded Knots and Braids* (`[R23]`, arXiv:2507.15086, 2025).
- **Classification:** Known in polymer/protein topology; **transfer** to computational geometry and robotics.
- **Core mechanism:** Classical knot theory requires closed loops, forcing arbitrary artificial closures on open 3D curves. Structural biology developed two complementary open-curve formalisms:
  - **Knotoids & bonded knots `[R23]`:** Topological equivalence classes of open 3D curves (and curves with internal cross-links/contacts) defined without closing endpoints.
  - **Circuit topology `[R22]`:** Given any self-contacting open curve (or folded ribbon), every pair of contact intervals along the arc-length parameter falls into one of three mutually exclusive algebraic relations—**Series ($S$), Parallel ($P$), or Cross ($X$)**—yielding a fast, deterministic, polynomial-time topological matrix and entanglement fraction.
- **General CG payoff:** Very high greenfield utility for **cable/wire-harness routing, robotic manipulation of deformable linear objects, surgical/textile simulation, 3D toolpath collision/entanglement checking, and open-curve simplification** where classical knot invariants do not apply.
- **`geometry3Sharp` synergy:** Low–Medium today (new capability, though it naturally complements `DCurve3`, `ContinuousCollision`, and `MeshGeodesicPath` if curve/cable analysis ever becomes a user domain). Zero external dependencies, deterministic, trivial to implement on CPU.
- **Why unused & catches:** Lives inside protein/Hi-C bioinformatics tools (`ProteinCT`, PyMOL). Circuit topology requires defining a contact-distance threshold (making it scale-parameterized, like persistence), while knotoid invariants require sampling projection directions on $S^2$ (deterministic geodesic grids on $S^2$ keep it bit-reproducible).
- **First falsification probe:** Implement the $O(C^2)$ Series/Parallel/Cross contact-relation classifier on procedural tangled 3D polylines (`DCurve3`) and verify stability under rigid `Similarity.All()` transforms and curve resampling.

### 9. Incompatible Elasticity and Inverse Metric Programming
- **Field & sources:** Plant/Tissue Morphogenesis & Soft-Matter Mechanics — *Morphing matter* review (`[R24]`, *Soft Science* 2023), *Programming evolution of geometry in shape-morphing sheets* (`[R25]`, Proc. R. Soc. A 2025), *Polymer Membrane Tensegrity* (`[R26]`, arXiv:2608.30501, 2026).
- **Classification:** Known in soft-matter physics; **transfer / speculative** for general CAD fabrication tools.
- **Core mechanism:** Instead of flattening a curved 3D mesh to 2D while minimizing distortion, morphogenesis prescribes an **incompatible 2D target metric** $g_{ij}$ (nonzero intrinsic Gaussian curvature) on a flat sheet so that swelling, shrinkage, or embedded rigid rods force the sheet to buckle into a desired 3D surface. Recent 2025–2026 finite-element and tensegrity-origami formulations solve the general anisotropic inverse problem for arbitrary smooth target surfaces.
- **General CG payoff:** Constructive inverse design for 4D printing, deployable curved structures, and flat-pack fabrication of doubly curved shells.
- **`geometry3Sharp` synergy:** Low today (niche fabrication domain; shares discrete metric/FEM machinery with parameterization and `ArapMeshDeformer`, but requires nonlinear shell mechanics and buckling-branch selection).
- **Why unused & catches:** Multi-stability (the same metric can buckle into multiple isometric 3D branches) makes certification hard; high implementation complexity relative to current library demand.
- **First falsification probe:** None recommended unless a concrete 4D-printing or deployable-surface user emerges.

### 10. *Physarum* (Slime Mold) Dynamic Optimal Transport for Loopy 3D Networks
- **Field & sources:** Biological Transport Networks — Facca, Cardin & Putti (`[R27]`, J. Comput. Phys. 2021), Lonardi, Facca, Putti & De Bacco (*Multicommodity optimal transport networks*, `[R28]`, Phys. Rev. Res. 2021; *PRL* 2023), Facca, Nordbotten & Hanson (*Network Inpainting via Optimal Transport*, `[R29]`, 2024).
- **Classification:** Known in applied math / transport modeling; **transfer** to CAD routing and additive-manufacturing infill.
- **Core mechanism:** Coupling an elliptic conservation equation $-\nabla \cdot (\mu \nabla p) = f$ with a nonlinear conductance-adaptation ODE $\dot{\mu} = |\mu \nabla p|^\beta - \mu$ yields continuous $L^1$ / branched optimal transport (`[R27]`). Crucially, **multicommodity or fluctuating-load formulations `[R28]` naturally converge to redundant, damage-tolerant, *loopy* 3D geometric networks** (like leaf veins and vasculature), overcoming the brittleness of loop-free Steiner trees.
- **General CG payoff:** Procedural 3D cooling channels, conformal microfluidic routing, fault-tolerant lightweight truss/infill generation, and **curve/network inpainting** on occluded 3D scans `[R29]`.
- **`geometry3Sharp` synergy:** Medium. Each time step is a symmetric positive-definite sparse linear solve over an existing triangle or tet mesh (`SparseCholesky` + `MeshTetrahedralization`), so the numerical engine already exists in-repo.
- **Why unused & catches:** Published in physics/applied-math transport journals rather than CAD/graphics; convergence can be slow as conductances approach zero (producing ill-conditioned elliptic systems that directly benefit from #2's multiscale preconditioner).
- **First falsification probe:** Run 50 backward-Euler *Physarum* iterations on a 3D tet mesh from `MeshTetrahedralization` with two competing source/sink pairs and measure solver conditioning, runtime, and extracted tubular network topology.

---

## Part III — Chemistry & Materials Science

### 11. Pointwise Distance Distributions (PDD) and Crystal Isometry Space
- **Field & sources:** Mathematical Crystallography / Materials Chemistry — Widdowson & Kurlin (*Resolving the Data Ambiguity for Periodic Crystals*, NeurIPS 2022; extended rigorous proofs for **finite and periodic** discrete sets in `[R30]`, SIAM 2025, arXiv:2108.04798v3).
- **Classification:** Known and proven in crystallography; **transfer** to 3D point clouds, CAD retrieval, and geometry QA.
- **Core mechanism:** For any finite or periodic point set $S \subset \mathbb{R}^d$ and integer $k$, record for each point $p \in S$ the sorted vector of distances to its $k$ nearest neighbors, collapse identical rows with multiplicity weights, and compare two distributions via the Earth Mover's Distance (or fast vector projections like Average Minimum Distance, AMD). Widdowson & Kurlin proved that PDD is:
  1. **Strictly invariant** under all rigid isometries, point permutations, and (for periodic structures) arbitrary unit-cell choices;
  2. **Lipschitz continuous** under bounded coordinate perturbation (unlike discrete symmetry groups or canonical-cell reductions);
  3. **Generically complete** (distinguishing all non-isometric point sets in general position, strictly stronger than pairwise-distance histograms and persistent homology);
  4. **Near-linear time** to compute via $k$-NN queries.
- **General CG payoff:** Database-scale near-duplicate detection for 3D point clouds and CAD assemblies, rigid-isometry retrieval without ICP alignment, automated periodicity/symmetry detection, and continuous indexing of 3D shapes.
- **`geometry3Sharp` synergy:** **Very high.** Tiny implementation surface on top of existing spatial trees (`DMeshAABBTree3` / point trees), 100% deterministic, zero ML/GPU dependencies, clean paper-only math (and reference Python code is MIT). Immediately useful for `PointCloudSurface`, `MeshICP` pre-filtering, `TpmsLattice` / periodic-structure verification, and metamorphic/corpus deduplication in QA benchmarks.
- **Why unused & catches:** Framed around the Cambridge Structural Database (CSD) and periodic crystals; for non-uniformly sampled meshes, PDD depends on vertex sampling density unless computed on area-uniformly resampled points (which requires a deterministic sampling rule) and is reflection-invariant unless supplemented with signed orientation invariants for chiral shapes.
- **First falsification probe:** Implement finite-set `PDD(S; k)` + `AMD(S; k)` in a small benchmark prototype; verify exact invariance on 100 Thingi10K meshes under `Similarity.All()` (rotations, non-unit 2^k scales and a far-from-origin UTM translation, plus vertex shuffling), measure perturbation Lipschitz stability, and benchmark query speed against `MeshHausdorffDistance` / `MeshICP`.

### 12. Atomic Cluster Expansion (ACE) as a Complete $O(3)$-Invariant Local 3D Basis
- **Field & sources:** Computational Chemistry — Drautz (*Phys. Rev. B* 2019), Dusson, Bachmayr, Csányi, Drautz, Etter, van der Oord & Ortner (*Completeness, efficiency and stability*, `[R31]`, J. Comput. Phys. 2022), *G-Equivariant Cluster Expansion* (`[R32]`, NeurIPS 2023), *Efficient MLIPs / GRACE* (`[R33]`, arXiv:2611.22444, Nov 2026) and *Factorised Cartesian ACE (FACE)* (`[R34]`, ChemRxiv 2026).
- **Classification:** Known and mathematically proven complete in computational chemistry; **transfer** to 3D point-cloud and mesh feature descriptors.
- **Core mechanism:** To represent arbitrary smooth functions of a local 3D neighborhood around point $i$ with exact permutation and $O(3)$ rotation/reflection invariance (or equivariance), ACE evaluates a single-particle radial × spherical-harmonic (or symmetric Cartesian monomial `[R34]`) basis on each neighbor, **sums over neighbors once** to form the atomic density $A_{i,nlm}$ (the "density trick"), and takes tensor products + Clebsch–Gordan / full-index contractions. This produces a **mathematically complete polynomial basis of arbitrary $N$-body geometric correlations at $O(1)$ cost per basis function**—completely eliminating the $O(k^\nu)$ combinatorial blowup of naive multi-point descriptors while scaling linearly with neighbor count.
- **General CG payoff:** A clean, deterministic, training-free, systematically improvable replacement for ad-hoc local 3D shape descriptors (FPFH, SHOT, Spin Images) across **point-cloud registration, local surface-patch matching, CAD feature recognition (fillets/chamfers/bosses), and lightweight linear regression on 3D geometry**.
- **`geometry3Sharp` synergy:** Medium–High. Directly strengthens `MeshICP` (global/feature-based initial alignment before ICP), `PointCloudNormals` / `PointCloudSurface`, and feature classification without any neural-network runtime dependency. The Cartesian-monomial formulation `[R34]` (20 independent monomials up to rank 3) is especially simple and fast in pure C# without heavy special-function libraries.
- **Why unused & catches:** Buried behind quantum-chemistry jargon (interatomic potentials, DFT force fields, Clebsch–Gordan couplings). Requires choosing a cutoff radius $r_c$ (scale-relative to local spacing or bounding box for scale invariance) and careful basis truncation to avoid combinatorial growth at very high body orders ($\nu > 4$).
- **Follow-up (2026-11-24):** checked against our building blocks in [ACE_DESCRIPTOR_STUDY.md](ACE_DESCRIPTOR_STUDY.md): verdict park (the missing pieces are global registration and a feature grammar, not the descriptor).
- **First falsification probe:** Implement low-order Cartesian ACE (ranks $l \le 3$, body orders $\nu \le 3$) on sampled 3D neighborhoods and benchmark rotational invariance, noise robustness, and correspondence recall on noisy/partial point-cloud registration vs. standard normal-histogram descriptors.

### 13. Persistent Topological and Hodge Laplacians
- **Field & sources:** Computational Biochemistry — Guo-Wei Wei's group (*Persistent Topological Laplacians—A Survey*, `[R35]`, MDPI 2025 / arXiv:2312.07563; *Persistent de Rham–Hodge Laplacians in the Eulerian Representation*, `[R36]`, arXiv:2408.00220, 2024).
- **Classification:** Known in biomolecular TDA; **transfer** to general 3D shape analysis and defect detection.
- **Core mechanism:** Across a multiscale filtration $K_t \hookrightarrow K_s$ (e.g., alpha-complex radii or SDF level sets), the persistent $q$-th combinatorial/Hodge Laplacian $\Delta_q^{t,s}$ decomposes into two pieces: its **zero eigenvalues (harmonic kernel)** recover the exact persistent Betti numbers $\beta_q^{t,s}$ (components, tunnels, cavities), while its **non-zero (non-harmonic) eigenvalues**—especially the Fiedler value $\lambda_{\min}^+$—track **continuous geometric changes** (narrowing necks, flattening cavities, local stiffness) *even when the topology does not change*. (Connects directly to Cellular Sheaves in #3 via persistent sheaf Laplacians `[R11]`.)
- **General CG payoff:** Multiscale detection of near-pinches, thin walls, narrow tunnels, and cavity bottlenecks across scales—unifying spectral geometry and persistent homology into one operator.
- **`geometry3Sharp` synergy:** Medium. Complements `MeshWallThickness`, `MeshTopologySummary`, and point-cloud cavity/tunnel inspection, though computing spectra across many filtration steps is non-trivial on large meshes unless restricted to coarse/alpha-complex skeletons or a few lowest eigenvalues via Lanczos.
- **Why unused & catches:** Developed almost exclusively for protein–ligand binding affinity and viral mutation forecasting; full eigendecompositions across filtrations are expensive without sparse shift-invert / Lanczos eigensolvers and careful filtration subsampling.
- **First falsification probe:** Compute the persistent 0- and 1-Laplacian lowest non-zero eigenvalues across a 10-step alpha filtration on procedural shells with varying neck/wall thicknesses; test whether $\lambda_{\min}^+$ cleanly flags imminent pinches before topology changes.

### 14. Reticular Periodic Nets (EPINET/Systre) and Stealthy Hyperuniform Tessellations
- **Field & sources:** Reticular Chemistry & Chemical Physics — (a) Hyde, Robins & Proserpio (*Twenty years of the EPINET database*, `[R37]`, Royal Society Interface Focus 2026); (b) Torquato et al. (*3D stealthy hyperuniform isotropic networks*, `[R38]`, PNAS Nexus 2024; *Structural properties of hyperuniform Voronoi networks*, `[R39]`, Phys. Rev. E 2025).
- **Classification:** **(14a) Known in reticular chemistry; transfer to procedural CAD/AM lattices.** **(14b) Known in materials physics; speculative for general meshing.**
- **Core mechanism:**
  - **(14a) Reticular periodic nets `[R37]`:** Maps 2D hyperbolic tilings onto triply periodic minimal surfaces (TPMS) and computes canonical topological invariants (*systre keys*) and maximal-symmetry barycentric embeddings—cataloging **800,000+ 3-periodic nets, multi-component woven/catenated lattices, and multi-channel labyrinths**.
  - **(14b) Stealthy hyperuniform tessellations `[R38][R39]`:** Point sets whose structure factor vanishes identically for small wavenumbers ($S(k) = 0$ for $|k| < K$), suppressing large-scale density fluctuations completely while remaining strictly isotropic (zero grid aliasing). Their Voronoi cells exhibit strong size anticorrelation (large cells adjacent to small cells).
- **General CG payoff:** (14a) Vastly expands procedural metamaterial and additive-manufacturing lattice design beyond the 5–6 standard nodal TPMS formulas (`Gyroid`, `Schwarz P/D`, `Neovius`) to woven, interwoven, and multi-channel topologies; (14b) isotropic, non-aliased 3D cellular microstructures and quasi-Monte-Carlo sampling.
- **`geometry3Sharp` synergy:** **(14a) Medium–High** as a clean structural follow-on to `TpmsLattice` ([TPMS_LATTICE.md](../design/TPMS_LATTICE.md)); **(14b) Low** because collective-coordinate ground-state optimization is expensive for large $N$ and overkill for standard mesh sampling.
- **First falsification probe (14a only):** Implement Delgado-Friedrichs's barycentric quotient-graph embedding (a tiny linear system per unit cell) for 5–10 low-transitivity RCSR/EPINET nets and measure strut-mesh generation and self-intersection cleanliness inside `TpmsLattice`-style bounded domains.

---

## Part IV — Additional Specialized Fronts (Applied Math & TCS)

### 15. AAA Rational Approximation and Lightning / MFS Mesh-Free PDE Solvers
- **Field & sources:** Approximation Theory & Mathematical Physics — Costa & Trefethen (*AAA-least squares rational approximation and solution of Laplace problems*, `[R40]`, EMS 2023), Xue, Waters & Trefethen (*LARS*, `[R41]`, 2023), Chen, Schäfer & Desbrun (*Lightning-fast Method of Fundamental Solutions*, `[R42]`, ACM TOG 2024).
- **Classification:** Known in 2D numerical analysis; **transfer** in 3D MFS.
- **Core mechanism:** Places rational poles (automatically adapted by the AAA algorithm in 2D) or fundamental-solution sources (in 3D) exponentially clustered outside corners and concave singularities, solving Laplace, biharmonic, Stokes, and conformal-mapping problems to 8–12 digits via a compact boundary least-squares solve **with zero interior meshing**.
- **General CG payoff:** Ultra-fast, deterministic, high-accuracy 2D conformal maps and smooth 3D harmonic/biharmonic interior fields—a deterministic alternative to Walk-on-Spheres/Stars Monte Carlo PDEs (which [research.md](../roadmap/research.md) ruled out due to non-determinism).
- **`geometry3Sharp` synergy:** Low–Medium. Clean fit for 2D planar domains (`Polygon2d` / `GeneralPolygon2d`), fragile on dirty/thin-walled 3D triangle soups where external source placement can cross opposite sheets.
- **First falsification probe:** Benchmark a small 2D AAA-LS / lightning Laplace solver on non-convex L-shaped and multiply connected polygons vs. `QualityMesh2` + FEM `SparseCholesky` for accuracy and runtime.

### 16. Safe-Pruning Grid Compression & Deterministic Almost-Linear Min-Cost Flow
- **Field & sources:** Theoretical Computer Science & Discrete Geometry — (a) *Rectilinear Matching to the Integer Grid in Nearly-Linear Time* (`[R43]`, arXiv:2607.10703, July 2026); (b) van den Brand, Chen, Kyng, Liu, Peng, Probst Gutenberg & Sachdeva (*A Deterministic Almost-Linear Time Algorithm for Minimum-Cost Flow*, `[R44]`, FOCS 2024, arXiv:2407.10830). *(Supporting note: late-2026 power-diagram variable-radius offsetting and UDF medial-axis reconstruction `[R46]` similarly replace dense grids with sparse dual diagrams.)*
- **Classification:** **(16a) Known in 2D exact grid matching; speculative transfer to 3D rounding collars.** **(16b) Known theory; impractical constant factors today.**
- **Core mechanism:**
  - **(16a) Simultaneous safe pruning `[R43]`:** Uses exact dyadic arithmetic and range-counting support bounds to compress the infinite target integer grid $\mathbb{Z}^2$ into an $O(n)$ certified candidate set—simultaneously valid for every $\ell_p$ norm and independent of coordinate spread.
  - **(16b) Deterministic almost-linear min-cost flow `[R44]`:** Derandomizes $m^{1+o(1)}$ min-cost flow via dynamic expander hierarchies and cut sparsifiers.
- **General CG payoff & `geometry3Sharp` synergy:** (16a) Directly inspires finite, certified candidate-grid bounds for exact grid snapping and local rounding collars ([SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md) and Idea 3 of [CROSS_FIELD_GEOMETRY.md](CROSS_FIELD_GEOMETRY.md)); (16b) relevant in principle to `QuadFlowRepair` ([QUAD_REMESHING.md](../design/QUAD_REMESHING.md)), but **watchlist only** until practical low-constant variants mature.
- **First falsification probe (16a only):** Test whether 2D/3D safe-pruning radius bounds meaningfully shrink the local candidate lattice set on real `SafeRounding` defect clusters without violating exact orientation constraints.

---

## Supporting late-2026 pointers (adjacent to existing modules)

- **DEC convergence for Hodge–Dirac / Hodge–Laplace on surfaces `[R45]`** (arXiv:2611.21960, Nov 2026): Proves convergence of Discrete Exterior Calculus on every closed oriented surface using uniformly acute triangulations—highlighting that *acute* (not merely Delaunay) intrinsic meshes carry strictly stronger PDE convergence guarantees.
- **Power-diagram / Laguerre variable-radius offsetting & UDF medial-axis reconstruction `[R46]`** (*OffsetCrust*, IEEE TVCG 2026; *UDF Mesh Reconstruction via Offset-Volume Medial Axis Extraction*, arXiv:2605.15369, Oct 2026): Reformulates variable-radius offsets and unsigned-distance reconstruction via power diagrams and medial balls rather than dense SDF grids—directly adjacent to `MeshOffset` and `MeshWallThickness`.

---

## Reference ledger

All references below were verified via public arXiv, publisher, or author pages on 21 November 2026. No external GPL/copyleft or restricted source files were opened.

| ID | Citation | Venue / Identifier |
|---|---|---|
| `[R1]` | B. Buet, G. P. Leonardi, S. Masnou, *Weak and Approximate Curvatures of a Measure: a Varifold Perspective* | Nonlinear Analysis 222 (2022), arXiv:1904.05930 |
| `[R2]` | B. Buet, M. Rumpf, *Mean Curvature Motion of Point Cloud Varifolds* | ESAIM: M2AN 56 (2022), arXiv:2010.09419 |
| `[R3]` | B. Buet, X. Pennec, *Flagfolds* | arXiv:2305.10583 (2023) |
| `[R4]` | B. Buet, G. P. Leonardi, S. Masnou, A. Sagueni, *Approximate mean curvature flows of a general varifold, and their limit spacetime Brakke flow* (see also arXiv:2509.06440) | arXiv:2510.00746 (2025) |
| `[R5]` | N. D. F. Campbell et al., *Compression of Currents and Varifolds* | SIAM (2024–2025), DOI:10.1137/24M1699656 |
| `[R6]` | F. Schäfer, M. Katzfuss, H. Owhadi, *Sparse Cholesky Factorization by Kullback–Leibler Minimization* | SIAM J. Sci. Comput. 43(3) (2021), arXiv:2004.14455 |
| `[R7]` | J. Chen, F. Schäfer, J. Huang, M. Desbrun, *Multiscale Cholesky Preconditioning for Ill-Conditioned Problems* | ACM Trans. Graph. 40(4) (2021) |
| `[R8]` | F. Schäfer, H. Owhadi, *Sparse Recovery of Elliptic Solvers from Matrix-Vector Products* | SIAM J. Sci. Comput. (2024), arXiv:2110.05351 |
| `[R9]` | Y. Chen, H. Owhadi, F. Schäfer, *Sparse Cholesky Factorization for Solving Nonlinear PDEs via Gaussian Processes* | Math. Comp. (2025), arXiv:2304.01294 |
| `[R10]` | J. Hansen, R. Ghrist, *Laplacians of Cellular Sheaves* (thesis & papers) | Univ. of Pennsylvania (2021) |
| `[R11]` | X. Wei, G.-W. Wei, *Persistent Sheaf Laplacians* | arXiv:2112.10906 (2021–2023) |
| `[R12]` | F. Barbero et al., *Sheaf Neural Networks with Connection Laplacians* | PMLR v196 / ICML TAG-ML (2022) |
| `[R13]` | H. Riess et al., *Cellular Sheaves on Higher-Dimensional Structures* | arXiv:2505.23993 (2025) |
| `[R14]` | C. D. Schimming, J. Viñals, *Kinematics and dynamics of disclination lines in three-dimensional nematics* | Proc. R. Soc. A 479 (2023), 20230042 |
| `[R15]` | C. D. Schimming, J. Viñals, *A tensor density measure of topological charge in three-dimensional nematic phases* | Proc. R. Soc. A 480 (2024), arXiv:2308.04496 |
| `[R16]` | M. Burr, W. Byrd, *Certified simultaneous isotopic approximation of curves via subdivision* | arXiv:2302.04908 (2023–2024) |
| `[R17]` | Z. Marschner, P. Zhang, D. Palmer, J. Solomon, *Sum-of-Squares Geometry Processing* | ACM Trans. Graph. 40(6) (2021), arXiv:2110.08451 |
| `[R18]` | P. Zhang, Z. Marschner, J. Solomon, R. Tamstorf, *Sum-of-Squares Collision Detection for Curved Shapes and Paths* | ACM SIGGRAPH (2023), DOI:10.1145/3588432.3591507 |
| `[R19]` | P. Gómez-Gálvez et al., *Local and global changes in cell density induce reorganisation of 3D packing in a proliferating epithelium* | Development 151(20) (2024), dev202362 |
| `[R20]` | *3DTubularVoronoi* (3D tubular epithelial Voronoi & scutoid rearrangements) | bioRxiv 2025.09.02.673513 (2025) |
| `[R21]` | F. Brauns, N. H. Claussen, M. F. Lefebvre, S. J. Streichan, B. I. Shraiman, *The Geometric Basis of Epithelial Convergent Extension* (and *PNAS* 2024 companion) | eLife 13:RP95521 (2024); PNAS 121 (2024) |
| `[R22]` | B. Scalvini, V. Sheikhhassani, J. Woodard, A. Mashaghi et al., *Circuit Topology* (genome motifs, ProteinCT, PyMOL plugin) | iScience / PMC8861635 (2022); MethodsX (2022); bioRxiv (2025–2026) |
| `[R23]` | *Topology and Algebra of Bonded Knots and Braids* (knotoids and bonded biomolecular chains) | arXiv:2507.15086 (2025) |
| `[R24]` | Yang et al., *Morphing matter: from mechanical principles to robotic applications* | Soft Science 3:38 (2023) |
| `[R25]` | *Programming evolution of geometry in shape-morphing sheets* (anisotropic metric inverse design) | Proc. R. Soc. A 481 (2025), 20240387 |
| `[R26]` | *Inverse Design of Polymer Films Morphing into Freeform 3D Surfaces* (Polymer Membrane Tensegrity) | arXiv:2608.30501 (2026) |
| `[R27]` | E. Facca, F. Cardin, M. Putti, *Branching structures emerging from a continuous optimal transport model* | J. Comput. Phys. 447 (2021), 110700 |
| `[R28]` | A. Lonardi, E. Facca, M. Putti, C. De Bacco, *Designing optimal networks for multicommodity transport problem* (and *PRL* 131, 2023) | Phys. Rev. Res. 3 (2021); PRL 131:267401 (2023) |
| `[R29]` | E. Facca, J. M. Nordbotten, E. A. Hanson, *Network Inpainting via Optimal Transport* | arXiv preprint (2024) |
| `[R30]` | D. Widdowson, V. Kurlin, *Pointwise Distance Distributions for Detecting Near-Duplicates in Finite and Periodic Point Sets* | NeurIPS (2022); SIAM (2025), arXiv:2108.04798v3 |
| `[R31]` | G. Dusson, M. Bachmayr, G. Csányi, R. Drautz, S. Etter, C. van der Oord, C. Ortner, *Atomic cluster expansion: Completeness, efficiency and stability* | J. Comput. Phys. 454 (2022) |
| `[R32]` | *A General Framework for Equivariant Neural Networks on Reductive Lie Groups* (G-Equivariant Cluster Expansion) | NeurIPS (2023) |
| `[R33]` | *Efficient Machine Learning Interatomic Potentials* (ACE / GRACE canonical complete expansions) | arXiv:2611.22444 (Nov 2026) |
| `[R34]` | *Factorised Cartesian Atomic Cluster Expansion for Low-Cost Potentials* (FACE) | ChemRxiv 15010957 (2026) |
| `[R35]` | X. Wei, G.-W. Wei, *Persistent Topological Laplacians—A Survey* | MDPI Mathematics 13(2):208 (2025), arXiv:2312.07563 |
| `[R36]` | *Persistent de Rham–Hodge Laplacians in the Eulerian Representation* | arXiv:2408.00220 (2024) |
| `[R37]` | S. T. Hyde, V. Robins et al., *Twenty years of the EPINET database* | Royal Society Interface Focus 16(2) (2026), 20250087 |
| `[R38]` | *Stealthy and hyperuniform isotropic photonic band gap structure in 3D* | PNAS Nexus 3(9) (2024), pgae383 |
| `[R39]` | S. Torquato et al., *Structural properties of hyperuniform Voronoi networks* | Phys. Rev. E 111:034123 (2025), arXiv:2411.06273 |
| `[R40]` | S. Costa, L. N. Trefethen, *AAA-least squares rational approximation and solution of Laplace problems* | EMS Press (2023), DOI:10.4171/8ECM/16 |
| `[R41]` | Y. Xue, S. L. Waters, L. N. Trefethen, *Computation of 2D Stokes flows via lightning and AAA rational approximation* (LARS) | arXiv:2306.13545 (2023) |
| `[R42]` | J. Chen, F. Schäfer, M. Desbrun, *Lightning-fast Method of Fundamental Solutions* | ACM Trans. Graph. (2024) |
| `[R43]` | *Rectilinear Matching to the Integer Grid in Nearly-Linear Time* | arXiv:2607.10703 (July 2026) |
| `[R44]` | J. van den Brand, L. Chen, R. Kyng, Y. P. Liu, R. Peng, M. Probst Gutenberg, S. Sachdeva, *A Deterministic Almost-Linear Time Algorithm for Minimum-Cost Flow* | FOCS (2024), arXiv:2407.10830 |
| `[R45]` | *Convergence of discrete exterior calculus for the Hodge–Dirac and Hodge–Laplace problems on surfaces* | arXiv:2611.21960 (Nov 2026) |
| `[R46]` | *OffsetCrust: Variable-Radius Offset Approximation With Power Diagrams* (IEEE TVCG 2026); *UDF Mesh Reconstruction via Offset-Volume Medial Axis Extraction* | IEEE TVCG (2026); arXiv:2605.15369 (Oct 2026) |
