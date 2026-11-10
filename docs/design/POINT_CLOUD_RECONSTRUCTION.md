# Point cloud reconstruction: "alpha wrap + Poisson" (ROADMAP Phase 5 priority 6, stage 1)

Status: stage 1 (prior art, building blocks, upper bound, options). No library code in this PR.

## 0. Owner decision (2026-11-10, relayed by the coordinator)

- Order: **A and B now, C1 next, C2 only on request.** Stage 2 as separate PRs: A (docs and pinned tests for `MeshAlphaWrap`
  on point sets), B (`PointCloudNormals`, additive API), IO (XYZ / PLY point readers and writers), then C1.
- IO: ASCII XYZ and point-only PLY readers and writers, both added to `ReaderFuzzTests`. LAS / LAZ not without a licence
  check first.
- Automatic `Alpha`: yes, from the measured sampling spacing (about 2.5x, with the noise condition). The condition is
  checked and reported in the result.
- Band versus octree for C2: not decided now.
- Paper references are verified against the papers before the `docs/SOURCES.md` entries (section 2 marks what is unverified).
Upstream demand: gradientspace/geometry3Sharp issues #65, #141, #96, #164 (point clouds in, a surface out).

## 1. Result in short

- `MeshAlphaWrap` already takes point clouds (`new MeshAlphaWrap(IEnumerable<Vector3d>)` or `Points`) and gives a closed,
  oriented, manifold surface that encloses every point. Measured on procedural torus samplings (section 4): `Success` and a
  closed result in all 20 runs, 20-1800 ms for 5k-20k points. It is the reconstruction with a guarantee, not yet the detail:
  the result sits at `Offset` outside the points and does not follow noise below about `Alpha`.
- The hard part of Poisson, **normal orientation**, is cheap once a wrap exists: the direction from a point to the nearest
  point of the wrap is outward. With a sign-free PCA normal axis this orients 100 % of 2000 sampled points whenever
  `Alpha` is at least about 2.5 sampling spacings and 4 noise sigmas (section 4), and fails (56-93 %) below that. That is
  a checkable condition, so the method can report when it does not apply.
- **Recommendation: build it in three small steps** (section 6): (A) document and pin the existing wrap on point clouds
  (S); (B) `PointCloudNormals`: PCA axes plus wrap-based orientation, with a confidence report (M); (C) a detail stage
  from the oriented points, first by contouring the fast winding number of the points (reuses `PointAABBTree3`
  and marching cubes, no solver), screened Poisson only if the owner wants its smoothness (M-L). The wrap stays the
  fallback and the bound for C.

## 2. How others solve it (prior art, papers only)

No code of any of these was read; the licence rule of CLAUDE.md applies (implement from papers). The bibliographic data
below was checked on 2026-11-10 against web search results (publisher, DBLP-like listings, SIGGRAPH history, arXiv, project
pages), not against the papers' own pages: the ACM and Wiley pages were not reachable from the session. What is marked
"unverified" was not confirmed and is re-checked before the entry goes into `docs/SOURCES.md` in stage 2. What the papers
say about the methods (not the bibliography) is from memory and unverified, unless a note says otherwise.

| Topic | Source | What we take |
|---|---|---|
| Poisson reconstruction | Kazhdan, Bolitho, Hoppe, "Poisson surface reconstruction", Proc. SGP 2006, pp. 61-70 | Indicator function from oriented points: solve laplace(chi) = div(V), V the smoothed normal field; octree + B-spline basis. |
| Screened Poisson | Kazhdan, Hoppe, "Screened Poisson surface reconstruction", ACM TOG 32(3), Article 29, 2013 (DOI 10.1145/2487228.2487237) | Adds a point-interpolation term (screening) so the surface passes near the samples; the fix for the over-smoothing of the first method. Data term and iso value = average of chi at the samples. |
| Alpha wrap | Portaneri, Rouxel-Labbe, Hemmer, Cohen-Steiner, Alliez, "Alpha wrapping with an offset", ACM TOG (SIGGRAPH 2022) 41(4), 2022 (volume / issue and article number unverified) | Already implemented (`MeshAlphaWrap`). Works on point sets, offset gives the distance to them. |
| Normals by propagation | Hoppe, DeRose, Duchamp, McDonald, Stuetzle, "Surface reconstruction from unorganized points", SIGGRAPH 1992, pp. 71-78 | PCA tangent plane normal, orientation propagated on a neighbour graph (MST). Fails where the graph jumps across thin parts. |
| Dipole propagation | Metzer, Hanocka, Zorin, Giryes, Panozzo, Cohen-Or, "Orienting point clouds with dipole propagation", ACM TOG 40(4), Article 165, 2021 (arXiv 2105.01604) | Propagation guided by a winding-number-like dipole field. |
| Winding-number orientation | Xu, Dou, Wang, Xin, Tu et al., "Globally consistent normal orientation for point clouds by regularizing the winding-number field", ACM TOG (SIGGRAPH 2023) 42(4), 2023, GCNO (arXiv 2304.11605; the complete author list and the issue number are unverified) | Orientation by optimizing the winding-number field of the oriented points. |
| Iterative Poisson | Hou, Wang, Wang, Qin, Qian, He, "Iterative Poisson surface reconstruction (iPSR) for unoriented points", ACM TOG 41(4), Article 128, 2022 (arXiv 2209.09510) | Poisson on guessed normals, refined by iterating the result; no separate orientation step. |
| Fast winding numbers | Barill, Dickson, Schmidt, Levin, Jacobson, "Fast winding numbers for soups and clouds", ACM TOG 37(4), Article 43, 2018 | Already in `PointAABBTree3.FastWindingNumber` (oriented points with area weights). The winding number itself is the generalized one of Jacobson, Kavan, Sorkine-Hornung, "Robust inside-outside segmentation using generalized winding numbers", ACM TOG 32(4), 33:1-33:12, 2013. |
| Survey | Berger, Tagliasacchi, Seversky, Alliez, Guennebaud, Levine, Sharf, Silva, "A survey of surface reconstruction from point clouds", Computer Graphics Forum 36(1), pp. 301-329, 2017 (DOI 10.1111/cgf.12802) | Positions the above: Poisson is the standard for oriented, noisy data; wraps and Delaunay methods for guarantees. |

How others solve normal orientation: all propagate or optimize **without an outside reference**; our wrap supplies one
(the unbounded outside component of the complement), which is the idea of the owner (2026-11-08) and, as far as the
sources above show, not a published pairing (nothing found for "alpha wrap" with "normal orientation" or "Poisson"; terms
searched from memory only, a web search is part of stage 2). Its limit is the same as the wrap's: a surface that is not
the boundary of a solid (an open sheet, two-sided thin walls below `2 Alpha`) has no outside side.

## 3. What the library already has

| Need | Building block | Note |
|---|---|---|
| Wrap of a point set | `mesh_ops/MeshAlphaWrap.cs` (`Points`, `Alpha`, `Offset`, `Status`) | Exact predicates, `AlphaWrapOracle` handles points. QA tests: `LatticePointCloud_CosphericalPoints`. |
| Point neighbourhoods | `spatial/PointSetHashtable.cs` (`FindInBall`), `spatial/PointAABBTree3.cs` (`FindNearestPoint`) | For kNN / PCA. No kNN query with k > 1 yet: add or use ball queries with the spacing. |
| Oriented points to winding number | `PointAABBTree3.FastWindingNumber` (`FWNBeta`, `FWNApproxOrder`, `FWNAreaEstimateF`) | Needs normals and an area per point (default 1.0 is wrong): the area from the kNN spacing. |
| Fast batch queries | `spatial/MeshBatchQueries.cs` (`NearestPointBatch`, `IsInsideBatch`) over `DMeshAABBTree3` | Orientation of all points = one `NearestPointBatch` on the wrap (3.7-7.2x a loop, BATCH_QUERIES.md). |
| Contouring of a field | `MarchingCubes`, `WindingContouring` (meshes only), `MeshSignedDistanceGrid` | Contouring of a point-set winding number needs a new field adapter, not new contouring. |
| Sparse solves | `solvers/SparseSymmetricCG.cs`, `SparseCholesky.cs`, `PackedSparseMatrix` | Enough for a uniform or narrow-band Poisson grid; no octree and no multigrid. |
| Eigen decomposition | `SymmetricEigenSolver`, `GaussPointsFit3` | PCA normals. |

Not there: kNN with k > 1, per-point area (local disc) estimate, an octree / narrow-band Poisson discretization, a
point-cloud IO path (the readers return meshes with vertices only; PLY/XYZ with normals are not parsed).

## 4. Upper bound: what a wrap gives on procedural samplings (measured)

Probe (throwaway console program, not committed; scratchpad only): torus R=3, r=1 (genus 1, so a topology error is
visible), N = 5000 and 20000 points area-weighted, 1 % of the bounding diagonal Gaussian noise ("noise"), a removed
patch ("hole"), a density ratio of 10:1 along the ring ("nonuniform"). `MeshAlphaWrap(points)` with `RelativeAlpha` 20
or 50 and `RelativeOffset` = 30 x `RelativeAlpha`. Orientation test: sign of (nearest wrap point - p) . n for the true
normal axis n, on up to 2000 points; `maxDev` = largest distance of a wrap vertex to the true torus. Linux container,
net10.0, single run each (timings are indicative).

| Sampling | N | RelAlpha | Time | Triangles | maxDev | Orientation correct |
|---|---|---|---|---|---|---|
| clean | 5000 | 20 / 50 | 268 / 1054 ms | 1782 / 15810 | 0.019 / 0.008 | 100 % / 85.4 % |
| clean | 20000 | 20 / 50 | 126 / 223 ms | 1928 / 5482 | 0.019 / 0.008 | 100 % / 100 % |
| noise 1 % | 5000 | 20 / 50 | 19 / 270 ms | 902 / 11040 | 0.289 / 0.347 | 100 % / 56.9 % |
| noise 1 % | 20000 | 20 / 50 | 43 / 157 ms | 966 / 4522 | 0.346 / 0.333 | 100 % / 100 % |
| hole | 5000 | 20 / 50 | 158 / 523 ms | 5080 / 15824 | 0.019 / 0.008 | 96.9 % / 85.3 % |
| hole | 20000 | 20 / 50 | 95 / 1634 ms | 2040 / 29628 | 0.019 / 0.008 | 100 % / 92.5 % |
| nonuniform | 5000 | 20 / 50 | 38 / 491 ms | 1728 / 15640 | 0.019 / 0.008 | 100 % / 82.2 % |
| nonuniform | 20000 | 20 / 50 | 87 / 1524 ms | 1938 / 27212 | 0.019 / 0.008 | 100 % / 93.5 % |
| noise+hole+nonuniform | 5000 | 20 / 50 | 19 / 190 ms | 1046 / 9914 | 0.301 / 0.296 | 99.8 % / 55.9 % |
| noise+hole+nonuniform | 20000 | 20 / 50 | 45 / 224 ms | 1028 / 8570 | 0.349 / 0.337 | 100 % / 57.5 % |

All 20 runs: `Success`, closed. Reading:
- Orientation is right where `Alpha` is large against the sampling: the sampling spacing is about 0.15 (N=5000) and 0.077
  (N=20000), the noise sigma 0.08, `Alpha` = diagonal / RelAlpha = 0.37 (20) or 0.15 (50). At RelAlpha 20 every row is
  >= 96.9 % (99.8-100 % except the hole at N=5000: the nearest wrap point beside a bridged hole is not outward); at RelAlpha 50
  `Alpha` is about one spacing or below two sigmas and the rows drop to 56-93 %. Rule of thumb: `Alpha` >= 2.5 spacings and
  >= 4 sigma; both can be estimated from the cloud (kNN distances, PCA residual), so `Alpha` can be chosen, and the
  condition checked, by the algorithm.
- Noise of 1 % moves the wrap's offset surface by up to 0.35 (4 sigma): the wrap is not a denoiser, it only encloses.
  Detail and denoising are the job of the second stage.
- Holes are bridged (the wrap fills what an `Alpha` ball cannot pass); Poisson also fills holes, with no bound.
- This probe used the true normal axes. With PCA axes the extra error is at creases and in noise; to be measured in B.

Not measured here (stage 2 does it before building): Poisson / winding contouring quality, real scanner noise (outliers,
varying noise), N > 20000, memory.

## 5. Options

| | What | Guarantee | Cost | Risk |
|---|---|---|---|---|
| A | Wrap only. Document the recipe, pin it with guard tests (torus, noise, hole). | Closed, oriented, manifold, no self-intersection, encloses all points (existing). | S, 1 PR | none; no detail below `Alpha`. |
| B | A + `PointCloudNormals`: PCA axes, orientation from the wrap, confidence report (fraction oriented, `Alpha` vs spacing / noise check). | Exact status when the condition fails; orientation is a heuristic with a measured rate, not a proof. | M, 1 PR | Open sheets and thin walls have no outside: reported, not guessed. |
| C1 | B + detail by contouring `FastWindingNumber` of the oriented points at 0.5 with marching cubes on a band around the wrap. | The result is closed (grid mesh); containment in the wrap can be enforced by intersecting the field with the wrap's inside. No guarantee on topology or self-intersection beyond what `MarchingCubes` gives. | M, 1-2 PRs; no solver | The winding number of a sparse / noisy cloud is expected to be blobby and to need good area estimates (our expectation; not confirmed against Barill et al. 2018, to be measured in C1). |
| C2 | B + screened Poisson (Kazhdan and Hoppe 2013) on a narrow-band grid around the wrap, CG solve, iso at the sample average, trimming by the wrap. | As C1; smoother, interpolates the samples through the screening term. | L: discretization, solver, tests; an estimate of the solve (not measured): 200k unknowns x 27 nonzeros x ~300 CG iterations is a few seconds | The most requested look; most code; no octree, so resolution is limited by a uniform band. |

How others solve it: A and the Poisson part of C2 are the standard (Berger et al. 2017); orientation by the wrap and the
trimming by the wrap are the new pairing (section 2); C1 follows Barill et al. 2018 and GCNO 2023 in using the
winding-number field of oriented points as the indicator (what each paper does exactly is unverified).

Guarantee that survives in every option: the wrap bounds the result. With the wrap as the outer limit (intersect), no
detail stage can leave a hole in the enclosure of the input, and no detail stage can be worse than A.

## 6. Recommendation and open questions (answered in section 0)

1. **A and B now, in this order, as two PRs** (A is S; B is M and is useful alone, for any pipeline that needs normals).
   Both are additive API (`PointCloudNormals` is a new class) and need no change to `MeshAlphaWrap`.
2. **Detail stage: C1 first, C2 on request.** C1 reuses `PointAABBTree3.FastWindingNumber`, marching cubes and the
   batch queries; its upper bound (section 4) is cheap to measure before building (a procedural torus with PCA normals,
   Hausdorff against the true torus). If C1 is not smooth enough, C2 follows on the same oriented points.
3. Open questions: (a) is point-cloud IO (PLY / XYZ with normals) part of the first deliverable (a new reader goes into
   `ReaderFuzzTests`)? (b) should `Alpha` be chosen automatically from the cloud (recommended: yes, with an override)?
   (c) C2 on a uniform band or on an octree (octree = the paper; a uniform band = 1/3 of the work, resolution limited)?

Tests planned for B (procedural only): sphere / torus / noisy / holed / non-uniform samplings with known orientation;
invariance over `Similarity.All()` (2^k scales bit for bit); reuse; determinism (parallel equals serial); a negative
test per failure mode (open sheet reported, `Alpha` below the spacing reported); mutation check of the orientation
sign and of the condition thresholds.

## 7. Status and follow-ups (2026-11-10)

Built: step A (`AlphaWrapPointCloudTests`, OPERATIONS.md note; #566) and step B (`PointCloudNormals`; #568). The XYZ reader and
writer (#571, draft) and step C1 follow in the week of 2026-11-16, as does the check of the paper references against the papers
themselves before the `docs/SOURCES.md` entries (section 2 marks what is unverified).

Residuals of the independent review of `PointCloudNormals` (no code now; a follow-up PR, or an issue, on Monday):
- A wrap with `WrapStatus == PointLimitReached` is accepted if it is closed. `IsClosed()` does not show that it is manifold or free
  of self-intersections, which the exact parity test of the probes needs. Reaching the limit takes more than 2M Steiner points,
  so no input has triggered it. Options: treat `PointLimitReached` as `WrapFailed`, or check `MeshRobustRepair.NotCleanReason`
  before counting confident points.
- A cloud with a few degenerate neighbourhoods (for example a short line among surface points) still gets `Success`, with zero
  normals for those points; only `AxisCount` shows it (`DegenerateInput` needs fewer than `MinConfidentFraction` of the points
  with an axis). Option: a status or a warning count for the zero normals.
- Two mutants of defensive paths survive (the closed-wrap check, a probe that is not finite): neither can be reached by a test input.
- `NoiseSigma` is about 0.6 of the true noise (calibrated by `NoiseFactor` 6.5); a better estimator (larger neighbourhood, or a
  robust fit) is part of C1 if the Alpha rule needs it.
