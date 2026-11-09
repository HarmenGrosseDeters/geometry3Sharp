# Generalized winding numbers 2026: Antipodal method (Martens/Trettner/Bessmeltsev) and FEWN (Xie/Hafner/Wojtan)

Date of research: 2026-11-08. Both papers were read in full (main text and, for Antipodal, the supplement appended to the arXiv PDF).

## Sources fetched

| Item | URL | Result |
|---|---|---|
| Antipodal arXiv abstract | https://arxiv.org/abs/2605.01536 | OK. Only v1, submitted Sat, 2 May 2026 17:01:54 UTC. Licence link is http://creativecommons.org/licenses/by/4.0/ |
| Antipodal PDF (15 pages, main text and supplement) | https://arxiv.org/pdf/2605.01536 | OK |
| Antipodal project page | https://martenscedric.github.io/academic-page/publications/antipodal_wn.html | 301 redirect to https://cedricmartens.com/publications/antipodal_wn.html, which could not be reached from here (connection failed). Not needed. |
| Antipodal Crossref | https://api.crossref.org/works/10.1145/3811323 | OK. Licence: CC BY 4.0 (vor), published 2026-07-03 |
| Antipodal code | https://github.com/MartensCedric/antipodal | The GitHub API is not enabled for this session and github.com returned 403. raw.githubusercontent.com worked: LICENSE, README.md and 4 headers |
| FEWN project page | https://visualcomputing.ist.ac.at/publications/2026/FEWN/ | OK |
| FEWN PDF (8 pages) | https://research-explorer.ista.ac.at/download/22241/22249/2026_TransactionsGraphics_Xie.pdf | OK |
| FEWN Crossref | https://api.crossref.org/works/10.1145/3811339 | OK. Licence: CC BY 4.0 (vor), published 2026-07-03 |
| FEWN code | https://git.ista.ac.at/wojtan-group/peiyuan-xie/boundaryblaze | OK through the GitLab API (project id 2353): metadata, file tree and README only |

## Exact citations

1. Cedric Martens, Philip Trettner, Mikhail Bessmeltsev. 2026. *The Antipodal Method: Fast, Accurate, and Robust 3D Generalized
   Winding Numbers.* ACM Trans. Graph. 45, 4, Article 43 (July 2026), 11 pages. https://doi.org/10.1145/3811323.
   Preprint: arXiv:2605.01536v1 [cs.GR], 2 May 2026. (Martens and Trettner contributed equally. Trettner is at Shaped Code GmbH
   and co-wrote EMBER, the exact mesh booleans paper.)
2. Peiyuan Xie, Christian Hafner, Chris Wojtan. 2026. *Fast and Exact Winding Numbers for Triangle Meshes.* ACM Trans. Graph. 45, 4,
   Article 41 (July 2026), 8 pages. https://doi.org/10.1145/3811339. The code is called "BoundaryBlaze". The paper's abbreviation
   is FEWN.
3. Context: C. Martens, M. Bessmeltsev. 2025. *One-Shot Method for Computing Generalized Winding Numbers.* Computer Graphics Forum 44, 5,
   e70194. https://doi.org/10.1111/cgf.70194 (arXiv 2408.04466).

Both 2026 papers are SIGGRAPH 2026 TOG papers. Each paper calls the other concurrent work: the Antipodal README benchmarks against
FEWN, and FEWN cites only One-Shot and Spainhour & Weiss 2026, not Antipodal.

---

## 1. Shared core idea (both papers): ray crossing count plus boundary term

**Formula.** For a mesh M with boundary edges ∂M and query point p:

    w_M(p) = χ(p, d)  +  (1/4π) · Σ_{(a,b) ∈ ∂M}  Ω(a − p, b − p, apex)

- **Integer part χ.** This is the signed number of crossings of the ray p + t·d (t > 0) with all mesh triangles. A crossing counts
  sgn(d·n). It is a plain signed ray-triangle crossing count, so it **can be computed with exact predicates**. Antipodal's
  supplement does exactly that.
- **Boundary term.** One signed solid angle (signed spherical triangle area) per boundary edge. The triangle is formed by the edge
  and an apex that lies *behind* the query point, opposite to the ray:
  - **Antipodal (eq. 9, Alg. 1):** the apex is the point at infinity in direction x1 = −x0, where x0 is the ray direction.
    Per edge: `Area = 2·atan2(x1·(A×B), 1 + A·B + A·x1 + B·x1)`, with A, B the unit directions from p to a, b. This is the
    Van Oosterom–Strackee formula with the third vertex x1. Result: `w = χ + Area/(4π)`.
    - Sign convention as printed: Alg. 1 returns χ + Area/4π, and the ray is shot towards x0 while the triangles are formed with
      x1 = −x0.
    - The code (`gwn_mesh.hh`, `math/common.hh`) uses the "half, unnormalized" variant: `atan2(x1·(v0×v1), l0 l1 + l1 x1·v0 + l0 x1·v1 + v0·v1)`
      with unnormalized v = a − p, summed and divided by 2π. Each term is multiplied by an integer edge weight.
  - **FEWN (eq. 5, 6):** w_M = w_{M+C} − w_C. Here C is a "generalized cone" that closes M: the cone has the same boundary as M, and
    its apex lies on the line through q opposite to the ray. The ray never hits C, so w_{M+C} = Σ sgn(r·n_i) over M alone. w_C is
    the sum of the Van Oosterom–Strackee solid angles of the cone triangles (one per boundary edge):
    `Ω = 2 arctan(det(a,b,c) / (abc + (a·b)c + (b·c)a + (c·a)b))`.
- **The boundary term is a plain sum over boundary edges.** It needs no spherical polygon arrangement and no Gauss–Bonnet
  evaluation at runtime. Gauss–Bonnet appears only in Antipodal's *proof*: the continuous formulation (Prop. 4.2 / 4.3, Lemma 4.1,
  supplement Sec. 1) uses a Whitney index, geodesic curvature and the spherical winding number ω(x1). The discretisation (Sec. 4.3,
  eqs. 16–17) shows that the per-edge triangle sum equals that expression. The same formula with a parametric boundary is integrated
  by adaptive Gauss–Kronrod for NURBS.
- **Equivalence.** Antipodal's per-edge term is the solid angle of the triangle (a, b, apex at infinity along −x0), which is the
  limit of FEWN's cone with a finite apex behind q. The two papers have the same decomposition and differ only in where the apex is
  placed. FEWN derives it from the additivity of 2-chains, plus a Stokes and contractibility remark (Sec. 4.1: ℝ³ minus the ray is
  contractible). Antipodal derives it from a "spherical shoelace" argument (Sec. 4.1, Fig. 4) and a Gauss–Bonnet / Whitney-index
  proof.
- **Ray choice.** Antipodal: x0 = RandomUnitVector() in Alg. 1. The robust code bakes in a fixed axis ray (−x) with symbolic
  perturbation. FEWN: "rays are generated from each query point along the +x direction" (Sec. 5). For grids, both reuse one ray
  for all queries on a line.

**Non-manifold edges, soups, duplicates.** Both papers use the *exterior edges* of Jacobson et al. 2013: an edge enters the boundary
with its net oriented count, and edges with zero net count are dropped.
- FEWN Sec. 5 (inset): "faces of C are attached to all exterior edges ... edges that appear with a nonzero count in the boundary
  of its incident faces, taking into account orientation".
- Antipodal code `build_boundary_segments` (MIT): a hash map over undirected edges accumulates +1/−1 by direction, emits segments
  with non-zero sum, and stores the sum as the weight.
- Consequences (my reading of the chain arithmetic, not stated in the papers):
  - Duplicate triangles with the same orientation count twice in χ and double the weights of their exterior edges.
  - Opposite-oriented duplicates cancel exactly, as 2-chains.
  - Self-intersections need no special treatment.
  - In a triangle soup every edge is a boundary edge (B = 3F), so the method costs as much as the naive O(F) sum.

## 2. Degenerate rays and points on the surface

### Antipodal (supplement Sec. 2, "Robustness and Mesh Predicates"; code `gwn_mesh_robust.hh`, `math/robust_predicates.hh`)

- **Two off-by-one sources.** The paper identifies (a) the 2π branch jump of atan2 and (b) the discontinuity of ray-triangle
  crossing at triangle edges, plus rays that lie in a face plane. Mathematically the two jumps cancel. To make them cancel
  *numerically*, the paper uses symbolic perturbation (Edelsbrunner–Mücke SoS, Yap, Devillers et al.) together with exact integer
  predicates (Nehring-Wirxel 2021, Trettner 2022 / EMBER).
- **Query perturbation.** q_ε = (x+ε1, y+ε2, z+ε3) with 0 ≪ ε3 ≪ ε2 ≪ ε1 ≪ any positive real. "This infinitesimal move of the query
  point does not change the real winding number in the volume and yields a consistently defined number whenever q is on the
  surface." So **a point on the surface gets a one-sided value**: the value on the side the lexicographic perturbation points to.
  It is not the average of the two sides.
- **Ray d = (0,0,1) in the paper** (−x in the code). Crossing becomes a 2D point-in-triangle test. The edge test is
  `sign((x+ε1−x0)(y0−y1) + (y+ε2−y0)(x1−x0))`, evaluated lexicographically: real part, then the ε1 coefficient (y0−y1), then the ε2
  coefficient (x1−x0). If all three are zero, the projected triangle is degenerate and is ignored. ε3 only enters the front/back
  (supporting plane) test.
- **Coupling.** The atan2 numerator sign((−d)·(a×b)) has the *same* real, ε1 and ε2 coefficients as the edge predicate (code
  comment: "atan2_num == −t_real"). If both are evaluated by the same exact code, the jumps in χ and in the boundary term coincide.
  For the grazing case num == 0, atan2 → 0 or ±π, and the code chooses with the perturbed sign `sign_num_perturbed`. When the ray
  passes exactly through a boundary vertex it takes the limit, and it rewrites `l + d.x` as `(d.y² + d.z²)/(l − d.x)` against
  cancellation.
- **Precision of the predicates.** In the paper: 30-bit fixed point with int64 arithmetic at "virtually no performance impact", or
  60-bit with 128-bit arithmetic "barely measurable due to the atan2 overhead". The code offers three policies:
  - `int64_grid`: 20-bit grid, about 1e-6 of the mesh size.
  - `int128_grid`: 50-bit grid.
  - `exact_float`: exact sign of the double 2×2 determinant with an adaptive fallback. This is the default for double.
  - Caveats from the README: "Mesh coordinates must fit in float (Embree culls in float)". "Robustness is not guaranteed under
    -ffast-math".
- **Rays that hit the boundary.** "If the ray with the direction d hits the boundary, we set d to another arbitrary direction"
  (supplement Sec. 2). The SoS robust path makes the fixed axis ray "unable to fail" (code header comment).
- **Accuracy (Sec. 5.2, Fig. 12).** Ground truth is naive Jacobson evaluated in double with a double-double accumulator, on 100
  random points per mesh. Ours (f64) is "on par" with Jacobson naive and hierarchical, and f32 is at the precision limit of the
  type. Accumulation round-off is not an issue (f64 with a double accumulator ≈ f64 with double-double). "Edge cases (e.g.,
  collinear/overlapping boundary segments that are present in 30% of our meshes) are handled correctly." The paper gives no formal
  forward error bound. The fractional part is a float sum of atan2 terms with absolute error of roughly B·ulp.
- **Parametric surfaces.** Near the trim boundary, at cusps or for tangent hits, the ray is discarded and another one is shot.

### FEWN (Sec. 3 last paragraph, Sec. 4 "Robustness", Sec. 5)

- The paper explicitly considers exact or adaptive predicates and SoS but rejects them, because high-performance ray tracers do
  not support them. Instead: "we mark each intersection point that is not clearly in the interior of a mesh face. Then we locally
  extract all triangles adjacent to these intersections and connect their boundary to the apex point behind the ray. This produces
  a small closed auxiliary mesh whose winding number equals the signed number of ray intersections in this region". That local
  winding number (eq. 6, floating point) replaces the uncertain count.
- **The paper does not specify** the threshold for "not clearly in the interior".
- Also: "we fall back to direct winding number evaluation if intersection and query points are in very close proximity" (Sec. 5).
  No threshold is given.
- Embree runs in float. The paper says this "will not affect the precision of the final result" because Embree only decides the
  integer count.
- **On the surface.** No consistent one-sided convention. Sec. 5.3: near-surface values "may vary depending on implementation";
  query points within 1e-14 of the mesh were excluded from the statistics, and there "different methods may differ by exactly ±1".
- **Stress tests.** Fig. 4: vertices randomised in the unit cube; L∞ = 2.95e-12 and L1 = 1.77e-8 against hierarchical on 2000²
  points. Fig. 5: a "Swiss roll" slice where every ray hits exactly an edge or vertex, handled correctly.
- **No error analysis.** The claim is "exact in the sense that it introduces no approximations apart from floating-point errors".

## 3. Cost per query, precomputation, parallelism

| | Antipodal | FEWN |
|---|---|---|
| Per query | O(B + log F): one BVH ray plus B atan2 terms (Sec. 5.1). "The bottleneck of our method is computing the sum of areas, taking almost 92% of the total time." | O(B) cone sum plus one ray (Sec. 5.2: "the overall time complexity of our algorithm is O(\|B\|)") |
| Hierarchy over boundary edges | **None.** Future work: "fast approximation of the fractional part ... by adaptively simplifying the surface boundary" (Sec. 6) | **None.** Soups: "exact methods like ours ... provide no acceleration over direct computation. In these cases, approximation methods like FWN are the only viable option" (Sec. 5.3, Sec. 6) |
| Worst case (soup, B = Θ(F)) | Same as naive Jacobson (Sec. 5.1) | Same as naive (Sec. 6) |
| Precomputation | Embree BVH plus boundary edge extraction. "stays below 100 ms even for large meshes" (Supp. Fig. 8); faster than Barill and Jacobson preprocessing | Adjacency structures for degeneracy handling plus Embree. Slightly slower than the FWN BVH build, much faster than hierarchical (Fig. 7 left) |
| Parallel | Embarrassingly parallel per query (CPU dispatchers, OpenGL compute) | Measured single-threaded; trivially parallel per query |
| Ray reuse | Grid lines: O(B + WH(log F + D)) for a W×H×D grid | Same (one ray per y,z line) |

Ray reuse helps little on open meshes. Antipodal supplement Table 1: even "free raycasting" gives only a 1.38× geometric-mean
speedup, because the boundary term dominates (Amdahl).

**Typical B/F.** Antipodal Supp. Fig. 7 fits B ≈ 4.45·F^0.286 on the open part of Thingi10K, "but at the same time there's a
significant amount of meshes with closer to O(F)". FEWN fits |B| = O(|F|^0.28). 85% of Thingi10K meshes have no boundary edges, and
only 5 meshes have more boundary edges than faces.

## 4. Reported benchmarks against Barill fast winding numbers (FWN)

**Antipodal** (CPU AMD Ryzen 9 7950X3D 16 cores, multithreaded; GPU RTX 4090; Embree):
- Data: 1417 open Thingi10K meshes, 32³ query grid, no ray reuse, precomputation excluded. Barill used libigl order 2, with the
  libigl parallel_for replaced by their own.
- Speed vs Barill: **3.3× faster (geometric mean), 10.6× faster (arithmetic mean)**. Barill "is consistently faster only for very
  large meshes (>100k faces)" (Sec. 5.1).
- Against Barill order 0: 2.6× slower (geometric mean). Order 1: 1.6× faster (Supp. Fig. 5).
- Against Jacobson hierarchical: 22× (geometric) / 94× (arithmetic). On 700 closed meshes: 73–127× vs hierarchical, 27–38× vs Barill.
- GPU: 13×/149× vs naive GPU Jacobson; about 1e9 queries/s for moderately complex meshes.
- Accuracy vs Barill: "very imprecise; orders 0 and 1 can be considered not useful" (Fig. 12).
- README (authors' own later benchmark, not peer reviewed): against FEWN on 1789 open Thingi10K meshes, 16³ grid, single thread,
  queries are 7.2× faster (geometric mean; range 3.1–1041×; faster on 100% of meshes) and preprocessing is 1.5× faster (geometric mean).

**FEWN** (MacBook Air M2, single-threaded, Embree, double):
- Data: every Thingi10K triangle mesh, 50³ lattice per mesh. FWN with β = 2, order 2. "Centered at a performance gain of about
  100×" in per-sample evaluation time over FWN and hierarchical (Fig. 7).
- Table 1, errors vs hierarchical, excluding points within 1e-14 of the mesh:

  | Method | Max L∞ | Avg L∞ | Max L1 |
  |---|---|---|---|
  | FWN, β = 2 | 1.15 | 6.95e-3 | — |
  | FWN, β = 15 | 1.15 | 2.95e-3 | — |
  | FEWN | 1.46e-7 | 2.31e-11 | 8.80e-7 |

  FWN's large errors come from its near/far criterion: "distance from the query point to the centroid of a BVH cell, rather than to
  the closest triangle", so large triangles near the query are treated as far field (Sec. 5.3).
- Fig. 6 (subdivided cat head, 500² queries): FEWN O(|B|); OneShot O(|B|²); FWN near-constant but approximate. FWN needs β = 15 to
  get the mean error below 1e-7.

Neither paper benchmarks meshes of 1–3 M triangles with few queries specifically.
- Antipodal: Thingi10K tops out around 10⁶ faces, and Barill's queries win there.
- Antipodal's preprocessing claim (<100 ms) is the relevant number for our "serial FWN cache build dominates" problem.

## 5. Relation to One-Shot (Martens & Bessmeltsev 2025)

- One-Shot also uses one ray for the integer offset. It then needs the *spherical arrangement* of the projected boundary, the region
  areas A_i and their Alexander numbers χ_i (Antipodal eq. 4). That costs O(B²) for the pairwise spherical segment intersections
  (FEWN Sec. 5.2), the arrangement is "nontrivial to implement efficiently and robustly", and the code supports only a single
  boundary component (Antipodal Supp. Fig. 6).
- Both 2026 papers replace the arrangement with a per-edge sum, so the cost is linear in B and independent of the number of boundary
  components.
- Spainhour & Weiss 2026 (TOG 45(3) Art. 26, trimmed NURBS) is the other concurrent relative. It uses a Stokes line integral with a
  vector potential on ℝ³ minus a *line* and numerical quadrature. FEWN Sec. 4.1 explains why the line version gives no closed form.

## 6. Paper licences

- **Antipodal, arXiv 2605.01536v1:** CC BY 4.0 (the arXiv abstract page links creativecommons.org/licenses/by/4.0/). The PDF first
  page says "This work is licensed under a Creative Commons Attribution 4.0 International License. © 2026 Copyright held by the
  owner/author(s). ACM 1557-7368/2026/7-ART43". Crossref for 10.1145/3811323 lists CC BY 4.0 for the version of record from
  2026-07-03 (open access).
- **FEWN, TOG 45(4) Art. 41:** the PDF first page says "This work is licensed under a Creative Commons Attribution 4.0 International
  License. © 2026 Copyright held by the owner/author(s). ACM 1557-7368/2026/7-ART41". Crossref for 10.1145/3811339 lists CC BY 4.0
  (vor, 2026-07-03). Open access via ACM DL and ISTA Research Explorer record 22241.
- Implementing from either paper is unproblematic. Cite them in docs/SOURCES.md.

## 7. Code licences

- **Antipodal: https://github.com/MartensCedric/antipodal is under the MIT License.** LICENSE text: "MIT License, Copyright (c) 2026
  Philip Trettner, Cedric Martens". The README repeats "released under the MIT License".
  - It is a header-only C++20 library with optional deps TBB, Embree 4 and OpenGL 4.3, which the user provides.
  - Because the licence is MIT, I skimmed for structure: `src/antipodal/gwn_mesh.hh`, `src/antipodal/gwn_mesh_robust.hh`,
    `src/antipodal/math/common.hh`, `src/antipodal/math/robust_predicates.hh`.
    - `gwn_mesh.hh`: float kernels (fractional, integer, full; single and batch with a Dispatcher).
    - `gwn_mesh_robust.hh`: `RobustMeshGwn<T, Precision>` with an Embree user geometry, an exact in-triangle test plus front/back
      test for the fixed −x ray, and a robust fractional term that shares the exact determinant.
    - `math/common.hh`: `build_boundary_segments` and `signed_spherical_tri_area_half_unorm`.
    - `math/robust_predicates.hh`: int64/int128 grid policies and exact_float with exact sign of 2×2 products (two_sum based),
      `edge_det`, `edge_sign`, `atan2_num`, `sign_num_perturbed`, `project_into_tri`.
  - The GitHub API was not available, so I could not see the commit history or check whether the licence changed. The LICENSE was
    read from the `main` branch via raw.githubusercontent.com.
  - We may port it under our rules, with attribution in docs/SOURCES.md and the MIT notice.
- **FEWN / BoundaryBlaze: https://git.ista.ac.at/wojtan-group/peiyuan-xie/boundaryblaze has no licence.**
  - The GitLab API reports `"license": null`.
  - The repository tree (public, created 2025-12-16, last commit 2026-07-13 "Add replicability stamp") has no LICENSE/COPYING file:
    `ext/libigl` (submodule), `include/{FEWN,FWN,Hierarchical,Intersector,MeshLib,OneShot,Util}.h`, `src/*.cpp`,
    `meshes/cathead.obj`, `CMakeLists.txt`, `README.md`, `plot_asymptotic.py`.
  - The README has only the GitLab template text under "License" (inside an HTML comment). It lists the deps Eigen, libigl, Embree
    and TBB and carries a Graphics Replicability Stamp.
  - **Unlicensed means all rights reserved, so do not read or port it.** I read only the README and the file listing, no source files.
  - Implement from the CC BY paper instead. The paper text is fully sufficient.

## 8. Patents

Neither paper mentions patents. A full-text search for "patent" in both PDFs (including the Antipodal supplement) found nothing.
Neither README mentions patents. I did not run a patent database search.

---

## 9. Assessment for our exact mesh boolean (patch classification on open meshes)

These are my conclusions, not claims from the papers.

1. **The decomposition fits our exact pipeline directly.**
   - χ is a signed ray-triangle crossing count over the *whole* mesh. Our existing exact patch ray (exact predicates, SoS) computes
     exactly this. For a closed mesh the boundary term is 0, and the result is what we already do.
   - For open meshes we add Σ over exterior edges of weight·atan2(...)/(2π) for the same ray direction. Only this term is floating
     point.
2. **Coupling is mandatory near grazing configurations.** If our exact ray uses SoS, the boundary term must break the num == 0
   (grazing) case of atan2 with *the same* perturbed sign. Otherwise we get ±1 errors when the ray passes through a boundary edge.
   - Antipodal supplement Sec. 2.1 shows that both are the same 2×2 determinant in the plane orthogonal to the ray (ray along a
     coordinate axis). The MIT code shows the vertex-limit and cancellation handling.
   - A random ray direction, as in Alg. 1, is incompatible with axis-aligned exact predicates. Use an axis ray with SoS, as in the
     robust code.
3. **Points on a mesh triangle (patch centroid).**
   - The boundary term is continuous across the interior of the surface. It is discontinuous only on the "shadow" sheet swept by
     boundary edges along the ray, where χ jumps oppositely. So the jump across the surface lives entirely in χ.
   - "Value just in front": evaluate χ with the query perturbed to the desired side. Antipodal's lexicographic perturbation gives one
     fixed side; we can choose the side via the patch normal.
   - "Average of both sides": χ over all triangles *not* containing q, plus ½·Σ_{T∋q} sgn(d·n_T), plus the boundary term. Coplanar
     or duplicate triangles through q all go into the ½ sum.
   - Requirement: q must not lie on a boundary edge, where the boundary term itself is singular. A patch interior point is fine.
   - Thresholding at ½ then works as now. The average is exactly representable up to the float error of the boundary term, which is
     about B·1e-16, far from ½ unless the true value is near ½.
4. **Cost in our regimes.**
   - 1–3 M triangles, few hundred queries: precomputation is one O(F) exterior-edge hash pass (parallelisable, or a sort-based
     deterministic variant) plus our existing exact ray structure. Queries cost O(B) each. This removes the serial far-field cache
     build entirely.
   - 10⁴–10⁵ patches: total cost ≈ #patches × B atan2 calls. That is good for B ≲ 10³–10⁴ and parallel over patches (no reduction
     across threads within a query, so it is deterministic).
   - The bad case is B ~ 10⁵+ (triangle soups, heavily fragmented meshes): 10⁴ × 10⁵ = 10⁹ atan2 calls. **Neither paper offers a
     hierarchy for this case.** Both state that soups get no speedup. An adaptive choice is needed: the boundary sum when
     #queries·B is small, otherwise keep Barill FWN. Antipodal's own future-work note (boundary simplification) is an unpublished idea.
   - Possible hybrid: compute χ exactly and approximate only the boundary term hierarchically. This has no published error bound.
5. **Non-manifold, duplicates, self-intersections** are handled by the exterior-edge weights. The only extra structure is the edge
   hash with oriented counts.
6. **Licence path.** We can implement from either paper (both CC BY 4.0) and may port the Antipodal MIT code (robust predicates and
   the coupling logic). We must not use the FEWN / BoundaryBlaze code (no licence).
