# Cartesian Atomic Cluster Expansion (ACE / FACE) as a local descriptor: study

Status: research, 2026-11-24. No library code, no measurements on meshes (this is a desk study; the numbers below are
cost formulas and published figures, marked as such). Question from the owner: is ACE useful for (a) the initialization of
`MeshICP` and (b) CAD feature recognition on meshes? It was ranked "Tier 1" in
[UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md) (entry 12) by transfer value alone, without a
check against what we have. This study is that check.

**Verdict: park.** ACE is a sound, deterministic, complete polynomial basis of a local neighbourhood. But neither use is
limited by the descriptor. For (a) the missing piece is a global-registration pipeline (correspondence search, robust
estimation); a classic descriptor (FPFH or a PCA-based one) fills the descriptor slot at less cost and with a known
literature. For (b) the missing piece is region segmentation and a feature grammar on fitted primitives, which we mostly have
(`PrimitiveFit3`, `MeshPrincipalCurvature`); a 3-body invariant is not what separates a hole from a boss. Condition to
reopen: a measured case where the simple descriptor fails and a multi-body invariant is the cure (section 6).

## 1. The method in our terms

### 1.1 What it computes

Input: a centre point `p_i` and its neighbours `{x_j}` within a cutoff `r_c`, each with an optional weight (for atoms:
species; for us: area, confidence). Relative vectors `r_ji = x_j - x_i`.

1. **One-particle basis** per neighbour: a radial part `R_n(|r|)` times an angular part, either spherical harmonics
   `Y_lm(r/|r|)` (Drautz 2019; Dusson et al. 2022) or Cartesian monomials / symmetric tensors `r^{⊗l}` (CACE, Cheng 2024).
2. **The density trick:** sum once over the neighbours, `A_{n,l}(i) = Σ_j w_j R_n(|r_ji|) · (angular basis of r_ji)`. This is
   the atomic density projected on the basis. The cost per centre is linear in the neighbour count `N`; all higher-order
   terms are products of `A` and need no further neighbour loops [Dusson 2022; Cheng 2024, Sec. A].
3. **Symmetrization:** products of `A` contracted so that the result is invariant. Body order `ν` + 1 is the number of
   points in the cluster (`ν = 1` is the sum of radial functions, a 2-body quantity: distances to the centre; `ν = 2` is
   `Σ_l |A_l|²`, a 3-body term with angles; and so on). Spherical form: Clebsch–Gordan couplings. Cartesian form: full index
   contractions of the symmetric tensors, which "maintains interaction body orders" and gives "a complete set of
   polynomially independent features" without the Clebsch–Gordan redundancy [Cheng 2024, abstract and Eqs. 7-10].
4. The result is a feature vector of length `n × N_L` (radial channels times angular invariants; Cheng's Fig. 2 plots
   `N_L` as small for `l_max` 2-4 and `ν_max` 2-4, the range he reports as sufficient in practice).

For the "20 monomials up to rank 3" in the earlier note: the symmetric rank-`l` tensors in 3D have `(l+1)(l+2)/2`
independent components (1, 3, 6, 10 for `l` = 0..3), so 20 up to rank 3. This is my count; it matches the earlier note.

### 1.2 Invariances

| Group element | Status |
|---|---|
| Permutation of neighbours | exact (the sum over `j`; the order of floating-point summation matters, section 3.3) |
| Translation | by construction: only `r_ji` enter. The centre is the point `p_i` itself, so "local centring" is free (no centroid needed) |
| Rotation `SO(3)` | exact in exact arithmetic; the invariants are contractions |
| Reflection `O(3)` | the invariants built from rank-`l` tensors by full contraction are even under `r -> -r` at the pair level and invariant under reflection; they therefore **cannot tell a chiral patch from its mirror image**. Dusson et al. and Drautz treat `O(3)` invariance; for a mesh that is a feature (mirrored parts match) or a defect (a left-hand and a right-hand part match). With oriented normals attached as extra vector weights a pseudo-scalar can be added |
| Scale | **not built in** (a radial basis with fixed `r_c`) |

### 1.3 Scale normalization (our case)

Atoms have a fixed physical cutoff. Meshes have no natural length. Options, with the one that fits our determinism rule
first:

1. **A length from the input that scales exactly with a `2^k` scaling:** `r_c = c · sqrt(area)` or `c · bounding diagonal`
   for a global descriptor, or `r_c = c · (local sampling spacing)` for a local one. A `2^k` scaling of the input then
   scales `r_c` by exactly `2^k` (sums of squares and their square roots scale exactly in binary floating point, barring
   under- or overflow), and `r_ji / r_c` is bit-identical. This is what the invariance test over `Similarity.All()` checks.
2. **Multi-scale:** a small set of radii `r_c · 2^{-m}`; the vector is the concatenation. The radius ladder is also what
   integral-invariant curvature does [Pottmann 2007].
3. **Local PCA scale:** the first eigenvalue of the neighbourhood covariance. Cheap, but noisy on a flat patch.

Scale-invariance by the sampling spacing breaks on non-uniform sampling (a CAD tessellation has long thin triangles and
fine fillets). Scale by a geometric quantity (area, curvature radius) is more robust and costs one pass.

### 1.4 Cost per descriptor

Derived by me from the structure above, not measured:

- Density: `O(N · n · M(L))` where `n` is the number of radial functions and `M(L) = (L+1)(L+2)(L+3)/6` the number of
  Cartesian monomials up to rank `L` (20 for `L = 3`) or `(L+1)²` harmonics.
- Invariants: `O(n^k · N_L)`-ish, independent of `N`. Cheng reports each invariant is evaluated independently with no
  stored high-dimensional product basis. Dusson et al. give a recursive evaluation that is the fast route for high `ν`.
- So with `N = 30..100`, `n = 6`, `L = 3`: about `1e4` flops for the density, a few hundred invariants. Cheaper than FPFH
  (`O(k)` per point, but with a second pass over the neighbours' histograms [Rusu 2009]) by a small factor, comparable to SHOT.
  Cost is not a deciding factor in either direction.

### 1.5 Stability and completeness

- **Completeness:** with the full basis, the invariants of order up to `ν = N` determine the neighbourhood up to the group
  [Dusson 2022]. In practice the series is truncated at `ν ≤ 3-4`. Pozdnyakov et al. (PRL 2020) show that 3-body (and
  4-body) invariants cannot separate certain different configurations; this is real incompleteness at low order.
  For a *surface* patch this matters less (a smooth patch is nearly determined by its fundamental forms), but a
  descriptor for a feature grammar can exploit that.
- **Noise and sampling:** the descriptor is a smooth (polynomial) function of the neighbour coordinates, hence Lipschitz in
  positions for a fixed neighbour set. It is **not** stable under changes of the neighbour set unless the radial basis goes
  to zero at `r_c` (a cutoff function), which ACE uses. It is a sum, so it depends on sampling density: uniform scaling of
  the weights changes the vector. Weighting each neighbour by its Voronoi / triangle-area share removes the density
  dependence to first order (my remark; the usual fix in descriptors on meshes). Dusson et al. discuss conditioning of the
  basis (orthogonal radial basis, regularization) as the numerical-stability issue of ACE.

### 1.6 Comparison to classic local descriptors

| Descriptor | Invariance | Needs normals / LRF | Cost per point | Behaviour on noise / sampling | Notes |
|---|---|---|---|---|---|
| Spin images [Johnson & Hebert 1999] | rotation about the normal (needs a normal) | normal | `O(N)` | sensitive to normal error and sampling | 2D histogram of (distance along, distance from) the normal axis |
| FPFH [Rusu 2009] | rigid, via point-pair features (Darboux frame, 3 angles) | normals | `O(k)` per point after the SPFH pass | sensitive to normal noise; the default descriptor of global registration pipelines | simple, the de-facto baseline |
| SHOT [Tombari 2010] | via a unique local reference frame | LRF (eigen-analysis of the covariance) | `O(N)` | the LRF is the weak point: sign and ordering ambiguities, unstable on symmetric patches | histograms over a spherical grid |
| 3D moment invariants (Sadjadi–Hall, Mamistvalov), 3D Zernike (Novotni–Klein) | rotation, via invariants of moment tensors or SH norms | none | `O(N · M)` | smooth, global moments are sensitive to partiality | **this is the 2-body / 3-body part of ACE with a fixed radial basis** |
| SH energy descriptor [Kazhdan 2003] | rotation, via norms of each `l` band | none | `O(N · L²)` | discards phase between bands, the exact incompleteness ACE adds back with `ν ≥ 3` | = `Σ_l |A_l|²`, ACE's `ν = 2` |
| Curvature / shape-index histograms; integral invariants [Pottmann 2007] | isometry, via the patch statistics | normals or none (integral version) | `O(N)` | integral invariants are robust to noise (that is their selling point) | ball-volume PCA gives principal curvatures at a scale |
| **ACE (Cartesian)** | rotation, reflection, translation; scale by `r_c` | none (a normal can be one weighted channel) | `O(N·n·M(L))` | polynomial, smooth, systematic in `ν` and `l` | complete at high order; the classic descriptors are low-order, hand-picked subsets of it |

The honest reading: ACE is the general framework that contains the "moment invariant" and "SH energy" family. It does not
introduce a new kind of information; it gives a systematic way to add higher-order terms. Whether the added orders help
registration is an empirical question that the literature on molecules answers for energies and nobody has answered for
surfaces (section 4).

## 2. Our building blocks

Searched under `/home/user/geometry3Sharp`:

| Need | Exists | Files |
|---|---|---|
| ICP | yes, local only: identity start, point-to-point (nearest triangle point) with an optional normal weight, SVD solve | `mesh_ops/MeshICP.cs` (`initialize()` sets `Rotation = Identity`, `Translation = 0`; no initial guess other than the user's), `solvers/FastQuaternionSVD.cs`; guards in `tests/geometry3Sharp.Tests/IcpAndRepairGuardTests.cs`; fixes R8 and R12 in `docs/ROBUSTNESS_SWEEP.md` |
| Global registration, RANSAC over correspondences, feature matching | **none.** `PrimitiveFit3` has a RANSAC loop for primitives, not for rigid transforms | `approximation/PrimitiveFit3.cs` (Plane, Sphere, Cylinder, Cone; `Seed`, `UseRansac`, `LocalOptimization`) |
| PCA, oriented box, covariance | yes | `containment/ContBox3.cs` (`ContOrientedBox3`, from GTEngine, Boost), `approximation/GaussPointsFit3.cs`, `approximation/OrthogonalPlaneFit3.cs` |
| Neighbour queries | yes | `spatial/PointHashGrid3d.cs`, `spatial/PointSetHashtable.cs`, `spatial/PointAABBTree3.cs`, `spatial/DMeshAABBTree.cs` |
| Normals of unoriented points | yes (PCA + alpha-wrap sign) | `mesh_ops/PointCloudNormals.cs`, `mesh_ops/PointCloudSurface.cs` |
| Curvature | yes | `mesh_ops/MeshCurvature.cs` (mean, Gaussian, angle deficit), `mesh_ops/MeshPrincipalCurvature.cs` (`K1`, `K2`, `Dir1`, `Dir2`) |
| Primitive fitting | yes | `approximation/PrimitiveFit3.cs` (RANSAC, inlier set, `RMSError`, `Result` enum) |
| Feature edges / creases | partial: constraint and crease handling in remeshing, sharp-feature pass in the wrap | `mesh/MeshConstraintUtil.cs`, `mesh/RemesherPro.cs`, `mesh/Reducer.cs`, `mesh_ops/AlphaWrapFeaturePass.cs`, `mesh_ops/MinimalHoleFill.cs` |
| Segmentation into regions | partial: connected components, face selection, UV-atlas charts, no planar/primitive region growing | `mesh_selection/MeshConnectedComponents.cs`, `mesh_selection/MeshFaceSelection.cs`, `parameterization/UVAtlasGenerator.cs` |
| Boundary loops (holes of a mesh, for hole/bore recognition) | yes | `mesh_selection/MeshBoundaryLoops.cs`, `mesh_selection/MeshRegionBoundaryLoops.cs` |
| Any descriptor | no. `spatial/NormalHistogram.cs` is a normal-direction histogram, not a patch descriptor | `spatial/NormalHistogram.cs` |

Also present as a CLI hook: `tools/g3cli/ShapeAnalysisCommands.cs` (shape analysis commands) is the place a feature command
would surface.

## 3. Fit for (a) and (b)

### 3.1 (a) Initialization of `MeshICP`

**The problem today.** `MeshICP` starts from the identity (`mesh_ops/MeshICP.cs`, `initialize()`), so it is a local method:
it converges only if the two shapes already overlap. There is **no tracked gap**: `docs/ROADMAP.md`, `docs/OPEN_TOPICS.md`
and `docs/roadmap/*` do not mention global registration or an ICP initializer, and a search of the repository's issues
(`ICP`, `registration`, `descriptor`, `feature recognition`) finds nothing open or closed on the topic (the only hit
for "registration" is #859, unrelated). The earlier research note raised it as an idea; nobody has asked for it.

**What a global stage needs** (the standard pipeline, e.g. FPFH + RANSAC, or FPFH + TEASER++ [Yang 2021]):
1. keypoints or a point sample, deterministic;
2. a local descriptor per point;
3. a nearest-neighbour search in descriptor space (a KD-tree in `n` dimensions: **not in the library**);
4. a robust estimator over correspondences (RANSAC with a seed, or a certifiable solver): **not in the library**;
5. a verification score and the hand-over to `MeshICP`.

ACE would fill step 2 only. Steps 3 and 4 are the larger work and are descriptor-independent. A pair of oriented bounding
boxes (`ContOrientedBox3`) with the 24 axis permutations/signs, scored by a Hausdorff or ICP residual, solves the most common
case (the same part in another pose, full overlap) with no descriptor at all; it fails for partial overlap and for symmetric
parts (a cylinder has no axis-fixed frame).

**What ACE adds over a simpler descriptor.**
- Over FPFH: no dependence on noisy normals (the neighbourhood positions alone), no histogram binning, a systematic way to
  add discriminating power. For registration of CAD parts with large planar and cylindrical regions, the discriminating
  information is in the *large-scale* arrangement (e.g. distances between a few planes), which a local patch descriptor of
  radius `r_c` does not see at all unless `r_c` is a large fraction of the part. A big `r_c` makes the descriptor a global
  one and loses partial-overlap robustness.
- Over PCA-frame descriptors: invariance without an LRF, so no sign/order ambiguity of eigenvectors. This is a real
  advantage over SHOT. It is also available in a much simpler form: eigenvalue ratios of the neighbourhood covariance are
  already rotation-invariant (1 scalar triple) and are the ACE `ν = 1, 2` content.

**Risks.**
- *Surfaces, not clusters.* ACE's density is a sum of delta functions at atoms. A mesh is a 2-manifold; vertices of a CAD
  tessellation are not a sampling of the surface (a planar face has few, long triangles). A correct density needs a
  deterministic area-uniform sampling or an analytic integral over the triangles (the integrals of `r^{⊗l} R_n(|r|)` over a
  triangle are not closed form for a general radial basis; a quadrature rule is needed). Weighting the vertices by their
  Voronoi/triangle area is the cheap option and is wrong on long thin triangles.
- *Cutoff radius.* One more parameter; the registration success rate is known to depend strongly on the support radius
  (every descriptor in table 1.6). Multi-scale concatenation costs `×` the number of scales.
- *Partial overlap.* The descriptor of a point near the border of the overlap is corrupted by the missing half. This
  limits all local descriptors equally.
- *Symmetry.* CAD parts have repeated and symmetric features (bolt circles, plane pairs); the descriptor of every repeat is
  the same, which is correct and makes RANSAC ambiguous. A descriptor cannot fix that; a verification by transformed
  overlap does.

### 3.2 (b) CAD feature recognition on meshes (holes, fillets, bosses, slots)

**The problem today.** None tracked. Nothing in `docs/roadmap/*` asks for it, and the repository has no feature
recognition module. The pieces for one exist: `MeshPrincipalCurvature` (K1, K2, Dir1, Dir2), `PrimitiveFit3` with RANSAC
(planes, spheres, cylinders, cones, `MaxRadius` and `MaxConeAngle` limits), `MeshBoundaryLoops`, the sharp-edge handling
of the remeshers.

**How features are usually recognized from meshes.** Segment into smooth regions or primitive patches, fit primitives
(Schnabel–Wahl–Klein efficient RANSAC is the standard), build the region adjacency graph, and match a grammar
(hole = cylinder with a circular boundary and an inward normal; fillet = a torus or cylinder strip between two planes
tangent to both; boss = a cylinder with outward normal capped by a plane; slot = two parallel planes and a half cylinder).
The grammar is the part that carries the semantics; the local descriptor only helps to find the regions (here a per-vertex
class from curvature: planar `K1 ≈ K2 ≈ 0`, cylindrical `K2 ≈ 0`, spherical `K1 ≈ K2`, saddle).

**What ACE would add.** A per-vertex descriptor that clusters into "plane / cylinder / sphere / edge / corner / fillet"
without curvature or normals. This is the classification that `K1`, `K2` and the normal covariance give from 2nd order
information, and that PCA eigenvalue ratios give at the sharp end (edge: two large and one medium; corner: three). A
training-free ACE vector would need a clustering or a labelled prototype set, so it **is not training-free in use** (it
needs prototypes or thresholds); thresholds on `K1`, `K2` are the same amount of tuning and are interpretable. Fillet vs
hole vs boss is a matter of **sign, topology and adjacency** (is the cylinder concave or convex, closed by a boundary
loop, tangent to the neighbours): not information in the *shape* of a small patch. With reflection invariance ACE cannot tell
a convex from a concave cylinder unless normals enter as a weighted channel, and then it is a normal-augmented descriptor, not
an "atoms-only" one.

**Risks.** The same as 3.1 (sampling of surfaces, cutoff radius), plus: features have a *size* (a fillet of radius 1 mm
in a 1 m part); a fixed `r_c` per descriptor means a descriptor family over scales and the same problem as curvature at a
scale; at a sharp feature the tessellation of CAD models is typically adapted to the curvature, so vertex density is a
function of the feature, which is a bias in the density-trick sum.

### 3.3 Determinism and the invariance rules

(`CLAUDE.md`: doubles, `Similarity.All()`, 2^k scales bit for bit, reuse test, parallel = serial.)

- **Doubles only,** no `float`, no FMA in the exact code; ACE would be outside the exact code (`math/Exact*`), so FMA and
  parallel loops are not banned by the analyzers, but a *parallel* density sum needs a fixed reduction order. Neighbours
  from a hash grid have no canonical order; sort the neighbours by `(distance, id)` or by a canonical key before summing, and
  sum in that order. A per-descriptor serial sum with a parallel loop over *descriptors* is deterministic and easy.
- **2^k scales, bit for bit.** Holds if `r_c` is derived by an exactly homogeneous expression (area root, bounding
  diagonal), `r_ji / r_c` is computed as a division (correctly rounded, same result for scaled operands) and all later steps are
  polynomial in these ratios. The radial basis must not contain absolute constants (an `exp(-r/λ)` with a fixed `λ`, or
  `r + 1e-9`). A cutoff function and a Bessel basis in the normalized variable are fine.
- **Rotations and reflections** are invariant only up to rounding (about 1e-15 relative per invariant in my estimate), as for any
  method in the library: the tests compare with a tolerance, only the 2^k group is exact.
- **Reuse / parallel tests** are routine.
- **Hidden risk:** descriptor matching by a nearest-neighbour search in descriptor space returns the same neighbour set only
  if ties are broken by id. Any RANSAC must use the `Seed` approach of `PrimitiveFit3`.

None of these is a blocker. They are the usual care, and they show that the descriptor cannot be the hard part.

## 4. Prior art

Searched (2026-11-24, public web): "atomic cluster expansion" with point cloud registration, CAD feature recognition, shape
descriptor; moment tensor descriptor (Shapeev) with surface registration; SOAP / bispectrum on 3D shape.

- **ACE or moment-tensor descriptors for surface registration or CAD feature recognition: nothing found.** The ACE family
  appears only in interatomic-potential work (Drautz 2019; Dusson 2022; CACE 2024; TACE 2025; LATTE; the 2026 review
  arXiv:2611.22444; FACE, ChemRxiv 2026).
- **The nearest relatives in vision are the rotation-invariant spherical-harmonic and moment descriptors**: Kazhdan,
  Funkhouser and Rusinkiewicz (SGP 2003, SH band energies, i.e. ACE's `ν = 2` term, for retrieval, not local registration),
  3D Zernike (Novotni and Klein), and the bispectrum used on 3D CT texture (a different domain). Learned
  rotation-invariant local descriptors for registration (SpinNet, RIGA, YOHO) are the current state of the art in learned
  matching and use handcrafted invariants (distances, angles, point-pair features) or an LRF as input, i.e. in spirit the
  low-body-order end of ACE.
- **Registration practice:** FPFH + RANSAC, or FPFH + a robust solver (TEASER++, certifiable, robust to over 99 % outliers
  [Yang 2021]), plus ICP for refinement. The descriptor is rarely the bottleneck in published failure analyses; outlier
  correspondences are, which is why robust estimators went from RANSAC to TEASER. (My reading of the cited papers'
  motivation; not a measured claim.)
- **CAD feature recognition** on B-rep uses graph neural networks or rule-based graph matching over faces (survey: Han,
  Pratt, Regli 2000), and on meshes primitive-RANSAC plus adjacency rules. No local-descriptor-only method.

"Nothing found" is limited by the search (a few queries, standard web search); a title using the words "cluster expansion" in
vision may exist and was not surfaced.

## 5. Source reachability and licenses

- Papers read (abstract level, via the arXiv API, plus the full text of CACE): Dusson et al. 2022 (arXiv:1911.03550,
  abstract), Cheng 2024 (arXiv:2402.07472, full text), the 2026 review (arXiv:2611.22444, abstract). Not read: the full text of
  Drautz 2019 and Dusson 2022 (paywall for the PRB and JCP versions; the arXiv version is free but I only used its abstract and
  the statements of the follow-ups); **the FACE paper (ChemRxiv 15010957) was unreachable** from this session (the proxy
  refused `chemrxiv.org`, HTTP 403) and I did not try to get around that. FACE is known here from a search result (its abstract:
  a factorised Cartesian ACE reducing the per-pair tensor operations; 5.7x faster inference than a 64-channel MACE on five
  rMD17 molecules at similar accuracy; search summary, unverified). It is an ML-potential architecture (learned radial and
  channel mixing); a descriptor study does not need it. The method statements in section 1 rest on Dusson 2022 and CACE.
- **Code licenses (checked, not opened):**
  - `ACEsuit/ACEpotentials.jl`: MIT (LICENSE file, 2022). `ACEsuit/ACE1.jl` / `ACE.jl`: the README states the Academic
    Software License (ASL), so not usable; a `LICENSE` file at the raw path was not found for ACE1.jl. **Not read.**
  - `BingqingCheng/cace` (CACE reference code): **CC BY-NC 4.0.** Not permissive. **Not read.**
  - `albert-hzbn/face-mlip` (FACE): MIT (LICENSE file, 2026). **Not read**: not needed; the structure is in the papers.
  - PCL (BSD), Open3D (MIT) implement FPFH; not read, not needed for this study.
  - No code of any project was opened for this study.

## 6. Verdict and ROI

**Park.** Reasons:

1. **The gap is not the descriptor.** (a) needs correspondence search and a robust estimator that the library does not have;
   a descriptor is one of five pieces. (b) needs region segmentation and an adjacency grammar; the per-vertex class from
   `MeshPrincipalCurvature` and `PrimitiveFit3` already covers the descriptor role.
2. **No demand on record.** No issue, no roadmap or open-topic entry asks for either.
3. **A simpler descriptor does the job to first order.** For (a): oriented-box candidate poses plus ICP residual scoring
   (no descriptor), then FPFH-style point-pair features if partial overlap is needed. For (b): curvature classes and primitive
   RANSAC regions.
4. **ACE's real advantages (completeness, no LRF) are conditional on a multi-body regime that surface patches mostly do not
   need;** its costs for meshes (a density on a 2-manifold, one more length parameter, reflection invariance that blinds
   convex from concave) are all in our case.

**Condition that would make it worthwhile** (reopen when all of these hold):
- a user case of global registration of **partial, symmetric or featureless-ish** CAD scans, recorded as an issue, where
  an oriented-box and FPFH-style baseline demonstrably fails (measured: success rate on a procedural family, below a fixed
  threshold), and
- the missing pipeline stages (descriptor-space neighbour search, seeded robust estimator) exist, so that the descriptor
  slot is the only thing left to swap.

**If the owner wants a cheap evidence step anyway (not recommended before the condition):** a throwaway benchmark
(`benchmarks/`, not the library): procedural CAD-like parts (box with holes, bosses, fillets), area-uniform sampling with a
fixed seed, three descriptors (PCA eigenvalue ratios plus normal covariance; a point-pair FPFH-like histogram; Cartesian ACE
with `L ≤ 3`, `ν ≤ 3`, `n = 4-6`), measuring per-vertex class accuracy under noise and the correct-correspondence ratio
for a rotated copy with 30 % partial overlap. About 3-5 days for one worker; the descriptor itself about 1 day. It would turn
this desk study into numbers; the kill criterion is "ACE correspondence ratio not clearly above the FPFH-like one".

**If adopted later, a stage plan** (smallest useful first PR first):

| Stage | Content | Tests | Benchmark | Effort |
|---|---|---|---|---|
| 1 | `LocalDescriptor` (internal, no public API): Cartesian ACE `L ≤ 3`, `ν ≤ 3` on a neighbour list with area weights; `r_c` from a caller-supplied length | invariance over `Similarity.All()` (2^k bit for bit, rotations with a tolerance), permutation of neighbours, reuse, serial = parallel | cost per descriptor against N | 3-4 days |
| 2 | descriptor-space nearest neighbour (KD-tree in `n` dimensions) and a seeded RANSAC estimator over correspondences | determinism (fixed seed, repeated runs), known-pose recovery, outlier rate sweep | recovery rate vs. outlier ratio | 4-5 days |
| 3 | `MeshGlobalRegistration` returning an initial `Rotation` / `Translation` for `MeshICP`; docs guide and example | recovery of a random rigid transform on procedural parts with partial overlap, handing the result to `MeshICP`, closed-form checks | success rate and time against the oriented-box baseline | 4-6 days |

Whole chain about 2-3 weeks of one worker; (b) is separate (about 2-3 weeks for a primitive-region feature grammar, mostly
unrelated to the descriptor). A descriptor-free first step for (a) (oriented-box poses plus residual scoring, plus tests) is
about 3-4 days and may be the right first PR if registration becomes a requirement.

**Recommendation for the owner:** park ACE; do not start a descriptor project. If a registration or feature-recognition
need appears, start from the descriptor-free baselines above and let a measured failure decide. Update the earlier note's
tier for entry 12 from "Tier 1" to "park" with a link here.

## References

See [sources/terrain-analysis-and-spatial.md](../sources/terrain-analysis-and-spatial.md#local-surface-descriptors-and-atomic-cluster-expansion-study-only-no-library-code)
for the list; the ones used above:

- R. Drautz, *Atomic cluster expansion for accurate and transferable interatomic potentials*, Phys. Rev. B 99, 014104 (2019).
- G. Dusson, M. Bachmayr, G. Csányi, R. Drautz, S. Etter, C. van der Oord, C. Ortner, *Atomic cluster expansion: Completeness,
  efficiency and stability*, J. Comput. Phys. 454, 110946 (2022), [arXiv:1911.03550](https://arxiv.org/abs/1911.03550).
- B. Cheng, *Cartesian atomic cluster expansion for machine learning interatomic potentials*, [arXiv:2402.07472](https://arxiv.org/abs/2402.07472) (2024).
- *Efficient Machine Learning Interatomic Potentials*, [arXiv:2611.22444](https://arxiv.org/abs/2611.22444) (2026).
- *Factorised Cartesian Atomic Cluster Expansion for Low-Cost Machine-Learning Interatomic Potentials*, ChemRxiv
  [10.26434/chemrxiv.15010957](https://doi.org/10.26434/chemrxiv.15010957) (2026); not reachable here.
- S. Pozdnyakov, M. Willatt, A. Bartók, C. Ortner, G. Csányi, M. Ceriotti, *Incompleteness of atomic structure
  representations*, Phys. Rev. Lett. 125, 166001 (2020), [arXiv:2001.11696](https://arxiv.org/abs/2001.11696).
- R. Rusu, N. Blodow, M. Beetz, *Fast Point Feature Histograms (FPFH) for 3D registration*, ICRA 2009.
- F. Tombari, S. Salti, L. Di Stefano, *Unique signatures of histograms for local surface description*, ECCV 2010.
- M. Kazhdan, T. Funkhouser, S. Rusinkiewicz, *Rotation invariant spherical harmonic representation of 3D shape descriptors*, SGP 2003.
- H. Pottmann, J. Wallner, Y.-L. Yang, Y.-K. Lai, S.-M. Hu, *Principal curvatures from the integral invariant viewpoint*, CAGD 24 (2007) 428-442.
- H. Yang, J. Shi, L. Carlone, *TEASER: Fast and Certifiable Point Cloud Registration*, IEEE T-RO 37(2), 2021, [arXiv:2001.07715](https://arxiv.org/abs/2001.07715).
- A. E. Johnson, M. Hebert, *Using spin images for efficient object recognition in cluttered 3D scenes*, IEEE TPAMI 21(5), 1999.
- R. Schnabel, R. Wahl, R. Klein, *Efficient RANSAC for point-cloud shape detection*, Computer Graphics Forum 26(2), 2007.
- J. Han, M. Pratt, W. C. Regli, *Manufacturing feature recognition from solid models: a status report*, IEEE Trans. Robotics and Automation 16(6), 2000.

Citations from memory (not re-fetched in this session): Johnson–Hebert, Schnabel et al., Han et al., Novotni–Klein,
Sadjadi–Hall, Mamistvalov, SpinNet / RIGA / YOHO (named in a search result, not read).
