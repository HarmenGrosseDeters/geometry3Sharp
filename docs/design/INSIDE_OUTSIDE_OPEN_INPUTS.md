# Inside / outside for open and broken inputs (ROADMAP Phase 5 #5): upper bound, literature, sketch, spike

**Outcome of the spike (2026-11-02): stop.** With the open inputs resolved by the generalized winding number,
3 of the 111 fallbacks become exact results (the stop rule asked for at least 50 to go on, and stops below 25), and
12.7 % of the patches labelled by the winding number lie within 0.05 of a half-integer. Section 6 has the numbers.

Owner decision 2026-11-02: report only, nothing built. The owner decides after this note.

## 1. Upper bound: what hinges on the inside / outside decision

Data: `MeshRobustRepair` on the 400 set and the holdout (798 meshes), master fe46f5e. The settings were
`BENCH_FIDELITY=0` and the wrap quality pass off; neither changes the path a mesh takes. There were no timeouts. 424
of the 798 inputs are open (they have boundary edges).

| path | meshes | of them open | median time | total time | share of the time |
|---|---:|---:|---:|---:|---:|
| repair (the input repair is clean) | 348 | 72 | 0.9 s | 473 s | 10 % |
| exact resolve | 339 | 270 | 3.7 s | 1614 s | 35 % |
| solidify fallback | 77 | 54 | 7.0 s | 785 s | 17 % |
| alpha wrap fallback | 34 | 28 | 42.4 s | 1686 s | 37 % |

All 798 results are clean. Of the 111 meshes that leave the exact resolve for a fallback, the reason
(`ExactResolveReason`) is:

| reason | solidify | wrap | open inputs |
|---|---:|---:|---:|
| the resolve's result is closed, but differs in volume from the **input's winding-number solid** ("volume differs from the input; input: not closed (winding number)") | 45 | 21 | 63 of 66 |
| the resolve's result itself is **not winding-closed** ("not closed (winding number)") | 22 | 7 | 6 of 29 |
| other reasons with an open input (a failed resolve, degenerate triangles) | 2 | 3 | 5 |
| degenerate triangles, too many self-intersections, volume (closed input) | 8 | 3 | 8 |

- **The bound.** About 100 of the 111 fallbacks (90 %) come down to the inside / outside decision.
  - In the 66 cases of the first row, the input is open. The volume check compares the resolve with the input's
    solid (generalized winding number > 0.5), and for an open input that solid is not well-defined, so the check
    rejects results that may be fine.
  - In the 29 cases of the second row, the winding numbers of the resolved arrangement do not give a closed surface.
    23 of these inputs are closed but self-intersecting, with inconsistent orientation or nesting.
  - These fallbacks take 54 % of the total repair time.
- **What the wrap fallback costs** (the 34 cases, paired runs, quality pass off):
  - volume IoU against the input: median 0.77 (p10 0.07);
  - one-sided Hausdorff result → input: median 2.4 % of the diagonal (p90 6.4 %), against a median of 0.56 % for the
    exact resolve (benchmark README, "exact resolve keeps the geometry");
  - normal deviation: median 2.4°;
  - time: median 42 s, 11 times the exact resolve.
- **The solidify fallback** keeps the geometry better, but it approximates the surface by contouring. Its Hausdorff
  median is 0.48 % on its own cases (benchmark README).
- **Self-unions** of `ExactMeshBoolean` ([SAFE_ROUNDING.md](SAFE_ROUNDING.md) §8.4, the 400 set): 124 of the 212
  results that are not clean are open inputs: "not closed" alone 32, with self-intersections 82, with bowties 9, with
  an inside-out component 1. Their large Hausdorff distances to the exact result come in part from the parts of open
  inputs that the union discards: the same decision, inside the Boolean.
- **Snap rounding B** ([SAFE_ROUNDING.md](SAFE_ROUNDING.md) §8, criterion 2). With B on every unclean self-union
  there are 21 new clean results, but **9 of them lie 1.0 to 9.3e4 times the Hausdorff bound from the exact result**.
  These are open inputs: B's second resolve takes the union again and drops what the winding number of the snapped
  mesh excludes. The drift bound does not cover that side of the Hausdorff distance, so it is the same inside / outside
  decision.

A better decision on these inputs could move up to about 100 meshes from the fallbacks to the exact resolve, which
keeps the input surface. The time saved is at most about half of the repair time.

## 2. Literature

Read: what was read in this session, and on which host. Listing: only a search result or project page was seen, not
the paper.

| work | read? | what it gives | applicable here |
|---|---|---|---|
| Jacobson, Kavan, Sorkine-Hornung, *Robust Inside-Outside Segmentation using Generalized Winding Numbers*, ACM TOG 2013 | not in this session (in SOURCES, in use) | the generalized winding number (GWN) as a smooth inside / outside measure for open, non-manifold, self-intersecting meshes; graph cut segmentation of a tet mesh | in use already (volume IoU, `FastWindingNumber`); the segmentation step is the missing part |
| Barill, Dickson, Schmidt, Levin, Jacobson, *Fast Winding Numbers for Soups and Clouds*, ACM TOG 2018 | not in this session (in SOURCES, in use) | hierarchical, approximate GWN evaluation | in use (`DMeshAABBTree3.FastWindingNumber`); approximate, not suited to an exact decision |
| Feng, Gillespie, Crane, *Winding Numbers on Discrete Surfaces*, ACM TOG 42(4) 2023 | listing only (SIGGRAPH archive, project page) | winding numbers of curves on surfaces (jump Laplace equation), with broken and non-bounding curves fixed | a model (integer jumps across a cut, the fix for non-bounding parts); for curves on surfaces, not surfaces in space |
| Feng, Crane, *A Heat Method for Generalized Signed Distance*, ACM TOG 43(4) 92, 2024 | **read (full text)** by a probe session, from the authors' PDF; relayed by the coordinator | diffuse the oriented normals by one backward-Euler step of heat flow (t = h²), normalize, integrate by a Poisson solve (Neumann boundary condition, constant from the mean or a constraint on the input); the sign comes from the orientation of the diffused normals. In 3D on a conforming tet mesh (Crouzeix-Raviart) or a regular grid (step 1 in closed form, a 7-point Laplacian), with a sparse direct solver. No formal guarantee for broken input; its robustness to holes, noise, self-intersections and non-manifold input is empirical, and it needs mostly consistent orientation. It completes large holes better than the GWN (which gives saddle patches there). Cells thinner than h can get a wrong sign | a floating-point fallback or tie-breaker for cells where the GWN is ambiguous (open or holed input), never a certified label; not for nested or n-ary labels (below) |
| Feng, *Algorithms for Generalized Signed Distance and Winding Numbers*, PhD thesis, CMU-CS-26-104, 2026 (nzfeng.github.io/thesis/Thesis.pdf) | **read** by a probe session; relayed by the coordinator | no guarantees for broken input anywhere (explicitly not robust in the sense of exact predicates); large holes are fundamentally ambiguous, and no method decides them. For watertight input, exact closest point plus the classic winding number (our exact path). Contouring the GWN at ½ is called its weak spot: in a 2D benchmark more than half the area was misclassified in about 10 % of the cases. The heat method's sign was about 3× more accurate, but needs reliable normals (it tolerates about 30 % flipped normals, the GWN does not, while the GWN is less sensitive to normal noise). Signed distances give only binary labels: winding numbers, not SDFs, are the tool for nested or self-overlapping (n-ary) labels, and the heat method fails on three-region input. In 3D a grid or a constrained tet mesh containing the input faces, solved by algebraic multigrid with a direct fallback; benchmarks at 64³. Points as Tori (TOG 2026) is for point clouds and learned. Code: MIT | keep the GWN (and the exact winding where closed); the heat method at most as a fallback for large holes with consistent normals |
| Martens, Trettner, Bessmeltsev, *The Antipodal Method: Fast, Accurate, and Robust 3D Generalized Winding Numbers*, arXiv 2605.01536 (May 2026; also listed as ACM TOG, DOI 10.1145/3811323) | **read** (arxiv.org, abstract and HTML v1, the main text up to about 100k characters; the supplement not read) | see the box below | the closest fit for a per-cell decision, but its error control is ours to add (box below) |
| Xie, Hafner, Wojtan (ISTA), *Fast and Exact Winding Numbers for Triangle Meshes*, ACM TOG 45(4), article 41, 2026 (SIGGRAPH 2026, DOI 10.1145/3811339; paper CC BY 4.0) | **read (full text)** by a probe session, from research-explorer.ista.ac.at; relayed by the coordinator (the hosts do not resolve in this session) | GWN(M) = the signed ray-crossing count of M plus a cone C − GWN(C), where C joins every boundary edge of M to an apex behind the ray, so that C never meets the ray (Stokes on R³ minus the ray); GWN(C) is a sum of Van Oosterom-Strackee solid angles over the cone's triangles. "Exact" means without approximation, not exact arithmetic: all in double, ray casts in single precision (Embree), no exact predicates. No error bound for the boundary term, only empirical errors on Thingi10K (at most 1.46e-7 against Jacobson 2013); queries within 1e-14 of the surface are excluded. Cost O(\|B\|) per query plus one ray (\|B\| about \|F\|^0.28 on Thingi10K; soups gain nothing), about 100× faster than hierarchical or fast winding numbers in their tests. Rays through edges or vertices: the solid angle of the closed local patch instead; queries near the surface: the direct GWN per triangle. **Code (boundaryblaze on git.ista.ac.at): no license, all rights reserved: not read, not ported, not run; implement from the paper only** | as Antipodal: the ray part can be made exact with our predicates (symbolic perturbation instead of thresholds); the certified bound of the boundary term remains our own work |
| Martens, Bessmeltsev, *One-shot method for computing generalized winding numbers*, CGF 44(5) 2025 | not read; cited by Antipodal | an earlier exact method by the same group | as Antipodal |
| Xu, Barbič, *Signed Distance Fields for Polygon Soup Meshes*, GI 2014 | listing only | signed distance for soups via an offset surface | older alternative, close to what the wrap fallback already does |
| Spainhour, Weiss, ACM TOG 45(3) 2026 | not read; cited by Antipodal | GWN for parametric surfaces | not for triangle meshes |

**The Antipodal Method, as read:**
- **Decomposition.** w(p) = χ(x₀) + (1/4π) Σ over the boundary segments (a, b) of the signed area of the spherical
  triangle (a, b, x₁), projected onto the unit sphere around p. χ(x₀) is the signed count of the surface's crossings
  along the ray from p towards a random direction x₀, and x₁ = −x₀ is its antipode. The cost per query is O(B + log F),
  with B the boundary segments and F the faces; the boundary sum is about 92 % of it.
- **What is exact.** The formula is exact in the continuous setting. The implementation is floating point (f64 or
  f32, spherical areas by `atan2`); the reference values were computed in double with a double-double accumulator.
- **No formal error bound** is given for the boundary term. For robustness, rays that pass too close to the boundary,
  hit a cusp, or are nearly tangent (thresholds 1e-4) are discarded and a new random ray is shot. Off-by-integer
  errors are analysed in the supplement (not read). Query points closer than 1e-6 to the surface are excluded. Rays
  exactly through edges or vertices are not treated separately in the main text.
- **Consequences for this library.** The ray parity χ can be made exact with the existing exact predicates (`Orient3d`
  for crossings, with symbolic perturbation for degenerate rays). The boundary term needs an a-priori error bound of
  our own (a sum of B `atan2` terms), and the random ray must become a deterministic choice.

Licenses (AGENT_WORKFLOW §3):
- **geometry-central** (with the signed heat solver): MIT, LICENSE read. It may be read and ported, with a SOURCES entry.
- **libigl**: MPL-2.0. Do not port.
- **signed-heat-3d** (Feng 2024): MIT, LICENSE read.
- **antipodal** (github.com/MartensCedric/antipodal; Trettner, Martens 2026): MIT, LICENSE read. Both may be read and
  ported, with a SOURCES entry.
- **The papers**: implement from them.

Already in the ROADMAP (Phase 2 and the open-task table):
- `ExactMeshBoolean` uses exact winding numbers per patch (signed ray crossings, stacks of coincident triangles) and the
  GWN for open inputs, `ceil(w − ½)` per patch.
- Where an open boundary of an input lies on an edge that other pieces meet, no inside / outside choice fits all
  pieces. The pieces that run such an edge the same way are not joined there, so the result is open along it
  (`ConflictEdges`), by design.
- The GWN propagated across patches (integer jumps, "same value" for facing sides, merged within a tolerance) was
  tried on branch `claude/exact-boolean-gwn-propagation` (commit 0db709c5). It only moves the cracks:
  - synthetic sweep: 4 better and 2 worse at tolerance 0.5, 6 and 0 at 0.3;
  - Thingi10K, 195 open meshes: boundary edges 303564 → 272575, but 128 of the 585 results with fewer and 136 with
    more, closed results 128 → 127. No clear gain, not merged.
- The volume check already uses cells with the exact winding for closed meshes (#305, option A; sampling, B, as the
  fallback).

## 3. What the library has, what is missing

Has:
- `DMesh3.WindingNumber` (exact sum of solid angles, O(n) per query) and `DMeshAABBTree3.FastWindingNumber` (Barill
  et al., approximate).
- `WindingNumberOctree` / `MeshWindingNumberSolidify` (propagated node values, deterministic) and
  `MeshWindingNumberGrid`.
- `ExactMeshBoolean`: the exact arrangement, the classification of cells by exact integer winding numbers, and
  `IsWindingClosed`, the edge balance test that fails for open inputs.
- `MeshSignedDistanceGrid`, `CachingMeshSDF`.
- `VectorHeatMethod`, `MeshGeodesicDistance` (heat method on surfaces), `IntrinsicTriangulation`.
- `SparseSymmetricCG`, `SparseCholesky`.

Missing:
- **A certified GWN decision.** The GWN of a point compared with ½ is decided in floating point today. An exact ray
  count plus a boundary term with an error bound (antipodal / ray decomposition) would decide most cells exactly, with
  a rule for the ambiguous ones.
- **A cell labelling for open operands in `ExactMeshBoolean`.** Today the exact winding needs winding-closed input.
- **A 3D volume Laplacian / heat solver** (tet or grid) for the signed heat method. The library's heat methods work on
  surfaces only.

## 4. Sketch: where it would plug in

1. **The volume check of `MeshRobustRepair` (66 cases).**
   - Today the resolve is compared with the input's GWN > 0.5 solid, by sampling.
   - Change: for an open input, use the same labelling as the resolve's cells (see 2), so the check compares like with
     like.
   - Alternatively, compare with the input's signed-heat solid, if 2 is not built.
   - This is the cheapest step: it changes only which reference the check uses.
2. **`ExactMeshBoolean` with open operands (29 cases plus the self-unions).**
   - Label each cell of the exact arrangement by the GWN of the input at a point inside the cell, compared with ½.
     Inputs whose integer winding is consistent keep their labels.
   - A surface between cells labelled in and out is closed by construction, so the result is closed and the
     resolve's "not winding-closed" exit goes away.
   - **Decision:** exact ray parity (exact predicates; a deterministic ray direction with symbolic perturbation in
     place of the paper's random re-shoot) plus the boundary term in floating point, with an error bound of our own.
     Antipodal gives none, so the bound is new work and part of the spike's cost. If the bound does not decide a cell,
     use a second rule: the label of the neighbouring cell across the face with the largest area, or the signed heat
     sign.
   - **Exact code** (CLAUDE.md): `mesh_ops/ExactMesh*` bans FMA, `float` and parallel loops. The GWN evaluation that
     the labelling uses must follow that, or live outside the exact files as a pure input to them.
3. **The 23 closed self-intersecting inputs** (row 2) are covered by the same cell labelling, since GWN handles
   nesting and orientation issues.
4. **The signed heat method** only if 1-2 leave many cases. It needs a volume mesh or grid and a Poisson solve, and
   it would be a larger, separate piece.

Determinism and invariance: the cell labelling must use a fixed summation order (serial) and only relative
tolerances. Bit-identical under 2^k scales and the existing invariance tests apply.

## 5. Stop rule (proposal)

- First an upper-bound spike in scratch (not merged): label the cells of the resolve by the floating-point GWN at a
  cell point, and measure on the 400 set and the holdout how many of the about 100 cases go from a fallback to a clean
  exact result.
- **Go** if at least 50 of them do, with fidelity at least that of the exact path (Hausdorff < 1 %, IoU > 0.99 against
  the input's GWN solid) and no regression on the meshes that are resolved today.
- **Stop** if fewer than 25 do, or if the ambiguous cells (GWN within the error bound of ½) are more than a few
  percent of the cells on these meshes. Then the certified decision is not cheap, and the signed heat method becomes
  the question, as its own item.
- Step 1 (the volume check reference) can be measured on its own first, as the cheapest part of the bound.

## 6. Spike result (2026-11-02, scratch, not merged)

**What was tried.** `ExactMeshBoolean` already labels open meshes by the GWN per patch (`AllowOpenMeshes`, true by
default). Open inputs were refused before it: `MeshRobustRepair.resolve_exactly` returns "not closed (winding
number)" when `IsWindingClosed` fails. The spike skipped that gate with a switch, so that open inputs are resolved
with GWN labels (step 2 of the sketch). For the input route that also covers step 1, since the volume check then
compares against the input's GWN > ½ solid, the same definition. Counters recorded how many patches had a GWN within
0.05 or 0.01 of a half-integer. Code: worktree of master 58a782b, not pushed.

**Measured.** `MeshRobustRepair` on all 798 meshes of the 400 set and the holdout, switch off against on
(`BENCH_FIDELITY=0`, P=3):

| | value |
|---|---|
| fallbacks with the switch off (solidify 77, wrap 34) | 111 |
| of them exact with the switch on | **3** (1211688, 1211690, 74153, all from solidify) |
| still a fallback | 108 |
| regressions (exact or repair → fallback) | 0; no exact result changed |
| patches labelled by the GWN | 112440 |
| within 0.05 / 0.01 of a half-integer | **12.7 %** / 7.1 % |
| total time | 5168 s → 5460 s (+6 %) |

Why the fallbacks remain, from the reasons with the switch on:
- **62 cases (52 + 5 + 4 + 1): the resolve of the open input is "not closed".** Once the gate is gone, the
  GWN-labelled result has cracks, and it stays open where open input boundaries meet other pieces (`ConflictEdges`).
  This is the structural limit that the propagation experiment had already met: no labelling per patch or per cell
  closes these surfaces, because no inside / outside choice fits all pieces there.
- **32 cases (20 + 7 + 5): "volume differs from the input".** The result is closed, but its volume differs from the
  input's GWN solid. The resolve takes one GWN value per patch, while the reference samples the GWN point by point,
  and near ½ (12.7 % of the patches) the two differ.
- The rest: degenerate triangles, failed resolves, too many self-intersections, unchanged.

**Decision by the stop rule: stop.** 3 instead of at least 50, and the share of ambiguous patches is far above "a
few percent". The certified boundary term (Antipodal / FEWN plus our own error bound) would not change this: the 62
open results are not a precision problem, and the 32 volume differences come from the reference definition. A
closing step for the cracks would be a different item, namely a surface reconstruction across the missing surface;
the literature above (Feng's thesis) calls large holes undecidable. The wrap fallback stays the answer for these
inputs.



## 7. Measured 2026-11-19: upper bound against analytic truth and a signed heat prototype (spike, data branch, no library code)

Owner task of 2026-11-19 (hotspots item 6). **Leaning: no-go for a library PR.** An opt-in label source for large holes only if the
owner names that use case. Code, raw CSVs and the full README: branch
[`runs/open-inside-outside`](https://github.com/HarmenGrosseDeters/geometry3Sharp/tree/runs/open-inside-outside/benchmarks/OpenInsideOutsideBound)
(`benchmarks/OpenInsideOutsideBound`, a data branch, never merged; no meshes, Thingi10K is fetched at run time). Container with
4 cores, Release net10.0, master 1d2a270a. Section 6 measured how many fallbacks a labelling would turn exact; this section measures
the classifier itself, against procedural solids whose truth is analytic.

**Prototype.** `SignedHeatGrid`: regular grid, oriented area normals splatted to the nearest cell, one backward-Euler heat step
(t = h^2, 7-point Laplacian, Neumann), normalize, Poisson solve of the divergence, constant from the area-weighted mean over the
input, inside = phi < 0. Written from the method description in section 2; no authors' code was read. Solvers: the library's
`SparseSymmetricCG` (matrix-free) and `SparseCholesky`.

**Caveats.**
- It is the grid variant, and it deviates from the paper: step 1 is a solve (the paper has a closed form), the splat is to the nearest
  cell, no tet mesh, no multigrid. A tet-mesh version might handle thin features better; untried.
- Scale invariance (`Similarity.All()`, 2^k scales) is not tested. Determinism: repeated runs in one process give bit-identical phi
  (CG at 48^3, Cholesky at 24^3); no parallel-versus-serial test.
- Thingi10K has no ground truth: the open-mesh numbers bound where the decision matters, not how often the GWN is wrong.

### 7.1 Upper bound, procedural cases with analytic truth (30000 random points in the box + 10 %)

Missing = share of the true solid labelled outside, IoU = labelled solid against the true solid. The floor of the closed sphere is
0.9961 (the polyhedron is inscribed). "Repair" = `MeshRobustRepair` defaults; result IoU against the true solid.

| case | GWN > 1/2: missing | GWN IoU | share of samples with GWN within 0.1 of 1/2 | repair path | repair result IoU (truth) |
|---|---:|---:|---:|---|---:|
| sphere closed (control) | 0.004 | 0.996 | 0 | Repair | 0.996 |
| sphere cap 0.3 rad | 0.006 | 0.994 | 0.001 | Repair | 0.995 |
| sphere cap 0.6 | 0.026 | 0.975 | 0.006 | Repair | 0.981 |
| sphere cap 1.0 | 0.130 | 0.870 | 0.021 | Solidify | 0.871 |
| sphere cap 1.4 | 0.365 | 0.635 | 0.034 | Solidify | 0.644 |
| sphere two caps 1.0 | 0.372 | 0.628 | 0.062 | Solidify | 0.638 |
| sphere 8 holes 0.3 | 0.028 | 0.972 | 0.008 | Solidify | 0.969 |
| sphere 8 holes 0.6 | 0.929 | 0.071 | 0.024 | Solidify | 0.069 |
| sphere band 0.15 | 0.027 | 0.973 | 0.009 | Solidify | 0.971 |
| torus window 0.5 / 1.0 | 0.063 / 0.141 | 0.937 / 0.860 | 0.006 / 0.013 | Solidify | 0.936 / 0.857 |
| two overlapping spheres, closed (control) | 0.002 | 0.998 | 0 | ExactResolve | 0.998 |
| two spheres, caps cut 0.5 | 0.013 | 0.987 | 0.004 | ExactResolve | 0.990 |
| two spheres, caps cut 1.0 | 0.123 | 0.877 | 0.025 | Solidify | 0.874 |
| two spheres, facing caps 0.6 | 0.002 | 0.998 | 0 | ExactResolve | 0.998 |

Reading: the GWN solid is wrong only where a hole is large (a cap of 1.0 rad and more, many holes of 0.6 rad). With a hole of 0.6 rad or
less the loss is at most 3 %. Where the answer is wrong, the repair result is wrong by the same amount (the fallbacks follow the GWN), so a
perfect classifier would lift the volume IoU of these fallbacks from 0.07-0.87 to about 0.99. The exact resolve never runs on them
("not closed (winding number)"). Of the misclassified samples, 23-71 % lie within 0.1 of 1/2 in most cases (column `ambig_of_err` of `results/proc.csv`), but only 7 % in 8 holes 0.6, where GWN is confidently wrong: a threshold on |GWN - 1/2| flags most errors, not all.

### 7.2 Upper bound, Thingi10K: 80 open meshes of the 400 broken-category selection (<= 30000 triangles; 180 read, 79 closed)

| path | meshes |
|---|---:|
| ExactResolve | 59 |
| Repair | 9 |
| Solidify | 11 |
| AlphaWrap | 1 |

The 12 fallbacks (15 %) take 62 of the 163 s (38 %). Eleven of them stop at "volume differs from the input; input: not closed (winding number)":
the reference is the GWN solid of the open input, so no truth exists here. 7 of 80 meshes have more than 1 % of the box samples with GWN
within 0.1 of 1/2. Without ground truth the Thingi bound cannot say how often the GWN solid is wrong; it only bounds the share of
inputs where the decision matters (15 % fallbacks, 38 % of the time). A random Thingi sample (120 meshes) has 13 open ones, 1 fallback.

### 7.3 The prototype against GWN (procedural truth, IoU against the true solid)

| case | GWN | heat 64^3 CG | heat 48^3 Cholesky |
|---|---:|---:|---:|
| sphere closed | 0.996 | 0.995 | 0.995 |
| cap 0.3 / 0.6 | 0.994 / 0.975 | 0.993 / 0.978 | 0.993 / 0.975 |
| cap 1.0 / 1.4 | 0.870 / 0.635 | 0.900 / 0.732 | 0.886 / 0.703 |
| two caps 1.0 | 0.628 | 0.818 | 0.802 |
| 8 holes 0.3 / 0.6 | 0.972 / 0.071 | 0.990 / 0.910 | 0.988 / 0.897 |
| band 0.15 | 0.973 | 0.989 | 0.986 |
| torus closed (control) | 0.980 | 0.938 | 0.931 |
| torus window 0.5 / 1.0 | 0.937 / 0.860 | 0.898 / 0.736 | 0.885 / 0.708 |
| two overlapping spheres, closed (control) | 0.998 | **0.744** | 0.744 |
| two spheres cut 0.5 / 1.0 | 0.987 / 0.877 | 0.730 / 0.704 | 0.730 / 0.700 |
| cap 1.0, 10 % / 30 % of the triangles flipped | 0.799 / 0.028 | 0.899 / 0.887 | 0.887 / 0.877 |
| two spheres cut 1.0, 10 % / 30 % flipped | 0.807 / 0.116 | 0.706 / 0.703 | 0.701 / 0.701 |

- Heat is better where GWN fails: many holes, two caps, flipped normals (GWN collapses at 30 % flips, heat does not move).
  In the big-hole sphere cases it recovers about half of the loss (cap 1.4: 0.64 -> 0.73), not all of it: it fills the hole by the
  smooth continuation of the diffused normals and leaks.
- Heat is worse where GWN is right: overlapping or nested patches (0.74 for two closed overlapping spheres; the method has binary
  labels, GWN 2 in the lens is inside), and thin solids (torus tube 0.8 wide on a 64^3 grid of a 3.6 box: 0.94 even closed).
- Thingi open meshes, 64^3: the heat solid differs from the GWN solid on a median 2.5 % of the samples (14 of 80 above 5 %, 5 above 10 %).
  For the 59 meshes the exact resolve handles, the result agrees with the GWN solid (IoU 0.999) and not with the heat solid (0.56 mean,
  better in none), expected since the resolve labels by GWN; thin walls at this resolution explain much of it. No ground truth, so this
  shows only that heat is not a drop-in replacement.
- Time: 64^3 with CG: 0.7-1.2 s per input (about 300-480 iterations, no preconditioner); Cholesky (AMD) 48^3: 21 s, two
  factorizations, dominated by the 3D fill (110k unknowns). The library's Cholesky is not the right tool for a 3D grid; matrix-free CG
  (or a multigrid) is. GWN by the fast winding number costs less than 1 s for the same samples.
- Determinism: two and three repeats give bit-identical phi (CG 48^3 and Cholesky 24^3, one process, hashes in `det`); the invariance
  over `Similarity.All()` is not tested (a library version needs it).

### 7.4 Decision

- The winding number is wrong only for large holes (a cap of 1.0 rad and more, many holes of 0.6 rad); up to 0.6 rad the loss is at
  most 3 %. There a perfect classifier would lift the solidify and wrap fallbacks from 0.07-0.87 to about 0.99 volume IoU.
- The signed heat method fixes the many-holes and flipped-normal cases, recovers about half of a big-hole loss, and loses on
  overlapping or nested patches and on thin solids, which are common in the data. So it is not a replacement and not a reference for
  the volume check.
- The exact resolve stays blocked by `ConflictEdges` (section 6) whichever classifier is used; the only gain is fidelity of the
  fallbacks on such inputs.
- If the owner names large holes with sound normals as a use case: an opt-in label source for `MeshWindingNumberSolidify` or the wrap
  (not `ExactMeshBoolean`), matrix-free CG on a grid, used when the GWN ambiguity or the hole size flags the input. Not built.
