# Volume check sampling: a tighter region (measurement and options, not built)

Owner decision 2026-11-01: low priority. The task is a tighter sample region for the volume IoU check of
`MeshRobustRepair`. The region must provably contain the union of both solids, so the estimator stays unbiased with
lower variance. The results will no longer be bit-identical; the quality thresholds stay. It is its own PR, with an A/B
on the 400 set.

This note records the measurement and the options. It was written on 2026-11-02, when the work was parked for a free
slot. The coordinator takes the choice between A and C to the owner.

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
