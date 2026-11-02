# Volume check sampling: a tighter region (option A built)

Owner decision 2026-11-01: low priority. The task is a tighter sample region for the volume IoU check of
`MeshRobustRepair`. The region must provably contain the union of both solids, so the estimator stays unbiased with
lower variance. The results will no longer be bit-identical; the quality thresholds stay. It is its own PR, with an A/B
on the 400 set.

This note records the measurement and the options. It was written on 2026-11-02, when the work was parked for a free
slot. Owner decision 2026-11-02: build option A, with B as the fallback. Sections 4 and 5 describe what was built and
its A/B.

## 1. Where the time goes

**Code:**
- `MeshRobustRepair.volume_check` calls `MeshRepairMetrics.VolumeIoUAtLeast`, which uses `volume_iou_at_least`.
- Samples are drawn in `sample_region`: per connected component (at most `MaxSampledComponents`), its box expanded by
  r = sqrt(n·A / (1.6 π)), with n components and A the component's area.
- The bound behind r: outside all expanded boxes, the winding number is below 0.4 (solid angle ≤ area / distance²).
- Rounds double, starting at 3125 samples. The loop stops on a Wilson interval of the share of the union that lies in
  only one solid, at most 100000 samples. It needs at least one sample in the input's solid.

**Measurement** (2026-11-02, master c419546 plus temporary instrumentation, not committed):
- 60 random meshes of the MeshRepairBenchmark 400 set (`random.seed(7)`), robust, `TIMEOUT=300`, 2 in parallel;
  65 volume checks in all.
- **Volume phases** (`volume` + `exact.volume` of `robust_phases`): 68 s of 315 s repair time, **21.6 %**. Of the
  checks, 91 % is sampling (53.7 s) and 9 % is the setup of the result's inside test (5.0 s).
- **Samples in the union:** 17.3 % overall, 21 % weighted by time. Quartiles per check: 4 %, 12 %, 22 %, 30 %, 68 %.
- **Cause:** the margin r. For one sphere of radius R, r ≈ 1.6 R, so the box has a side of about 5.2 R, and the hit rate
  is about 3 %.

**Upper bound.** The stop counts union samples, so the samples needed scale with 1 / hit rate.

| hit rate | saved, of the repair time |
|---|---|
| 50 % | 10.4 % |
| 80 % | 12.7 % |
| 100 % | 13.5 % |

This is optimistic in two ways. The remaining samples lie near the surfaces, where winding queries cost more than far
away. And building a tighter region adds setup.

## 2. Options

The region must contain input_solid ∪ result_solid. Both are defined by winding numbers: the input's generalized
winding number of its oriented copy (possibly open, self-intersecting), and the result's (closed, clean).

- **A. Cells, provable for closed meshes.**
  - Cover the box with an adaptive grid. A cell crossed by no triangle of a mesh (exact cell-triangle test) has a
    constant winding number for that mesh, if the mesh is winding-closed (integer winding number, which changes only
    across the surface). One evaluation at the cell's centre classifies it exactly.
  - Region = cells crossed by a triangle ∪ cells inside either solid.
  - Stratified: cells inside both solids and crossed by neither surface count with their exact volume and are not
    sampled. The estimator is then (V_both_known + V·both_sampled) / (V_both_known + V·union_sampled), still unbiased,
    with lower variance.
  - Applies when both meshes are winding-closed. The result always is; the input in roughly the cases that reach the
    exact stage (about 225 of 400).
- **B. Open inputs: keep today's region.**
  - The generalized winding number of an open mesh is not constant in empty cells.
  - Absolute bounds such as Σ A / (4 π d²) do not shrink near large flat faces: the sum diverges logarithmically for a
    point near a face.
  - I see no provable tightening there.
- **C. A cheaper first step, provable.** Replace the one total area per component by a hierarchical far-field bound:
  AABB-tree nodes, area / distance² per node. This finds the distance beyond which the winding number is < 0.4 per
  direction, and shrinks the margin r. It gains far from the surface only, so less than A.

**Recommendation:** A, with B as the fallback for open inputs, behind the existing estimator and its thresholds. A/B on
the 400 set:
- clean counts and IoU agreement unchanged within the estimator's interval;
- time per check;
- subset runs, since the full 400 needs hours of an active session.

## 3. Literature

Searched, snippets only [S]:
- The usual practice is a bounding box with 100k to 1M samples (3D learning papers), or voxel IoU. I found no source on
  stratified or importance sampling for this estimator.
- Barill, Dickson, Schmidt, Levin, Jacobson, *Fast winding numbers for soups and clouds*, SIGGRAPH 2018: an
  error-controlled far field, no guaranteed bound.
- Octree spatial enumeration (Meagher 1982; inside, outside and boundary cells) is the classic basis of option A.

## 4. What was built (option A, B as the fallback)

**Code:** `VolumeSampleCells` (mesh_ops/VolumeSampleCells.cs), used by the internal
`MeshRepairMetrics.VolumeIoUAtLeast(SolidReference, SolidReference, ...)` (and so by `MeshRobustRepair.volume_check` and
the public `VolumeIoUAtLeast(DMesh3, DMesh3, ...)`). Public `VolumeIoU` is unchanged: the benchmark's verdict IoU stays the
same instrument.

**Reused building blocks** (searched first): `ExactMeshBoolean.IsWindingClosed` decides A or B per mesh;
`DMeshAABBTree3` and its `FastWindingNumber`, already built for the inside tests, give the triangle candidates of a cell
and its classification; `ExactPredicates.Orient3d` / `Orient2d` decide the cell–triangle test. `WindingNumberOctree` refines
on corner values and copies values between nodes; it does not give per-cell touched / untouched with exact geometry, so
it is not reused. Its floating `TriangleBoxOverlap` is the test oracle on small integer coordinates.

**When:** both meshes winding-closed (the oriented copy of the input, and the result as it is) and the result has
triangles. Otherwise option B: `sample_region` and the loop as before, bit-identical.

**Cells.** The root box R is the bounds of the triangles of both meshes, expanded by 1 % (as before). Level L has the
spacing w / 2^L per axis (exact, by halving), and cell (i, j, k) of level L spans origin + h_L [i, i+1] x ...; so a cell of
level L and its children share their bounds bit for bit, and a 2^k scale gives the same cells, scaled. A cell is
*touched* if a triangle of either mesh meets the closed box (`TriangleTouchesBox`: the 13 axes of the separating axis
theorem, each decided by the signs of exact predicates; complete for degenerate triangles too). Untouched cells are
classified by the exact winding numbers of both meshes at the centre (`ExactWinding`: the signed crossings of a segment
to a point outside R, each decided by `ExactPredicates.Orient3d`; a segment through an edge, a vertex or in a triangle's
plane is degenerate and another direction is tried, as the exact classification of `ExactMeshBoolean` does). All
untouched children of a cell are classified by one evaluation: every closed child contains the parent's centre, so
together they form a connected set that no triangle meets. This needs the cell test on closed boxes: a surface through
the parent's centre touches all eight children, so then none of them is classified (test
`Cells_SurfaceThroughAParentCentre_TouchesAllChildren`, and the cover case "through centres"). Touched cells are refined level by level while their 8 children per cell fit in a budget of
4096 child tests, to at most 12 levels; the touched cells of the last level are sampled.

**Fallback when the cells are worse.** The cells are kept only if their sampled volume S is at most the volume of the
old region (the union of the components' expanded boxes, computed exactly by coordinate compression). With a small
component far away, the root box is long and thin, and the cells of the depth the budget allows can sample far more
volume than the old region (found by the independent review: a unit sphere and a speck at 10^6 gave about 9 union
samples instead of 1500); then the old region is used.

**The region contains input_solid ∪ result_solid.** Let solid_X = {p : w_X(p) > 1/2} for the winding number w_X.
1. Every point of R lies in exactly one cell of the final set (the known cells and the sampled ones), up to the shared
   faces (measure zero): the children of a cell partition it, and refinement only replaces a cell by its children.
2. The solids lie in R: the winding number of a closed mesh is 0 outside the convex hull of its triangles, which lies in
   their bounds.
3. A known cell C is a closed box that no triangle of A or B meets. C is connected and disjoint from both surfaces, so
   it lies in one connected component of the complement of each surface. On such a component the winding number of a
   winding-closed mesh is constant: it is an integer (every edge is used equally often in both directions, so the
   signed solid angles sum to a multiple of 4 pi) and continuous away from the surface. So w_A and w_B are constant
   on C, and the class at the centre is the class of every point of C.
4. Hence union = (known cells in A or B) ∪ (union ∩ sampled cells) and both = (known cells in both) ∪ (both ∩ sampled
   cells), disjointly: V(union) = K + V(union ∩ S), V(both) = K_b + V(both ∩ S), with K, K_b exact sums of cell volumes.
   Uniform samples in S estimate V(union ∩ S) and V(both ∩ S) without bias, so the IoU estimate
   (K_b + S·b̂) / (K + S·û) is the ratio of two unbiased volume estimates, as both / union was before (a ratio estimator:
   consistent, with a bias of order 1/n, as before). The cells inside exactly one solid are known too (K_o, in K); they
   enter the numerator of 1 - IoU exactly.

The classification is exact (the coordinator asked for it: the fast winding number of Barill et al. has no guaranteed
error bound, and a wrong rounding of it would bias the known volume). The samples in the touched cells are still
classified by the fast winding number (> 0.5), as before; there the estimate is the old one, restricted to S.

**Stop rule.** p = 1 - IoU = (K_o + S u q) / (K + S u), u the share of the samples in the union, q the share of those in
only one solid. Both get Wilson intervals; p is monotone in q (for u > 0) and in u (for fixed q), so its interval comes
from the four corners. The two intervals split the miss probability of one interval at z (Bonferroni): each uses z'
with erfc(z' / sqrt 2) = erfc(z / sqrt 2) / 2, z' = 4.17 for z = 4. So an early answer is wrong at most as often as
before per round (z = 4: about 6e-5 two-sided). With K = 0, p = q and only q's interval is used, at z: the old rule.

**Tests** (`VolumeSampleCellsTests`, `VolumeIoUSamplingTests`, `RepairSpeedTests`): the exact winding number against the
rounded fast winding number (nested shells, touching boxes, points in face planes); the interval of the stop rule against a
brute-force scan of both intervals (p rising and falling in u, the Bonferroni z); over 150 seeds no early answer on the
wrong side; the fallback for a far small component; the exact test against the
floating SAT on small integers (40000 triangles, a quarter degenerate) and at contacts one ulp apart, also at 2^20; the
cover on nested, touching, thin-shell, coplanar, disjoint solids and with faces exactly on cell planes (every point of a
known cell has its class, corners included; known and sampled cells tile R); the stratified estimate (known volume,
narrower interval, fewer samples); unbiasedness over 40 seeds; the RMS error near the threshold (5.4e-4 against 1.5e-3
before, fails on the old code); invariance over `Similarity.All()` (2^k bit for bit); parallel equals serial; reuse of the
input's solid and of a `MeshRobustRepair` instance; fallback B on open inputs and open results (bit-identical).

## 5. A/B on the 400 set

Full numbers and commands: [MeshRepairBenchmark README](../../benchmarks/MeshRepairBenchmark/README.md), section "Volume check:
sample cells for closed meshes". In short: no verdict and no repair path changed (273 repaired either way); the volume
phases dropped from 320.6 s to 295.7 s (−7.8 %), the 195 checks that use the cells from 98.5 s to 67.9 s (−31 %), with
38 % instead of 17 % of their samples in the union. The whole repair is not measurably faster. The gain stays below the
upper bound of section 1 because most checks stop after the first round anyway, the cells apply to 195 of 445 checks,
and the setup of the inside tests is unchanged. 117 checks built cells that sampled more than the region and fell back
(multi-component inputs); one octree per component box is the next lever if this is taken further.
