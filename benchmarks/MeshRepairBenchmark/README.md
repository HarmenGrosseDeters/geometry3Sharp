# MeshRepairBenchmark

Measures how well `gs.MeshAutoRepair` repairs real-world broken meshes, and what it breaks on the way.
The data comes from [Thingi10K](https://huggingface.co/datasets/Thingi10K/Thingi10K).

## Usage
```
python3 fetch_data.py            # ~1.5 GB: 400 meshes <= 100k faces + 30 meshes 100k..1M faces (--no-large skips those)
./run_all.sh default             # MeshAutoRepair
./run_all.sh cleanup             # MeshAutoRepair with PostRepairCleanup = true
./run_all.sh instr               # instrumented pipeline copy, quality after every step
./run_all.sh robust              # MeshRobustRepair: cleanup, solidify fallback, volume check
BENCH_P=2 ./run_all.sh hybrid    # BENCH_P: meshes in parallel
python3 analyze_fidelity.py results/robust.jsonl    # repaired = clean + no self-intersections + faithful
python3 analyze.py results/default.jsonl            # status per category, exceptions, triangle loss
python3 analyze_quality.py results/cleanup.jsonl    # quality before/after
python3 analyze_stages.py results/instr.jsonl       # which pipeline step introduces which defect
./run_all.sh analysis; ./run_all.sh analysis std meters; ./run_all.sh analysis std geo   # analysis building blocks
python3 analyze_analysis.py results/analysis.jsonl results/analysis_std_meters.jsonl results/analysis_std_geo.jsonl
```

`run_all.sh <mode> [loader] [transform] [tolerance factor]` takes these options:
- **loader:** `std` (StandardMeshReader, STL welding), `soup` (no welding) or `manual` (own exact weld + `AppendTriangle`)
- **transform:** `meters` (scale ×0.001) or `geo` (translate by 500000, 5000000, 0)
- **tolerance factor:** multiplies `RepairTolerance` and `MinEdgeLengthTol`

Each mesh runs in its own process with a timeout (120 s, large meshes 900 s), so hangs and crashes are recorded too.

The data selection is fixed (seeded) in `fetch_data.py`:
- 6 defect categories × 60 broken meshes: not closed, not edge-manifold, not vertex-manifold,
  degenerate faces, duplicated faces, not PWN
- 40 clean controls
- 30 large broken meshes

Thingi10K models have individual licenses, see `data/input_summary.csv`.

## Quality metrics
Each metric is counted before and after the repair:

| Metric | Definition |
|---|---|
| boundary edges | open edges |
| bowtie vertices | non-manifold vertices |
| inconsistent orientation | interior edges whose two triangles traverse it in the same direction |
| duplicate triangles | same vertex set |
| degenerate triangles | area < 1e-12 · diag² |
| sliver triangles | aspect ratio > 1000 |
| folds | dihedral angle > 175° |
| self-intersections | triangle pairs without a shared vertex; coplanar overlaps are not detected |
| exact self-intersections (`selfint_exact`) | `MeshRepairMetrics.SelfIntersections`: exact predicates, also pairs sharing a vertex or an edge and coplanar overlaps |
| inside-out components | closed, negative volume, not a cavity of another component |
| Hausdorff (`hd_fwd/back_lo/hi_rel`) | certified bounds (`MeshHausdorffDistance`) in both directions, relative to the diagonal |
| volume IoU (`iou`, `iou_missing`, `iou_extra`) | `MeshRepairMetrics.VolumeIoU`: 100k samples, inside = winding number > 0.5; reference is the input oriented like the solidify fallback (inside-out parts flipped) |

`analyze_fidelity.py` counts a mesh as **repaired** if it is topologically clean¹, has no exact
self-intersections, its Hausdorff upper bound is below 1 % of the diagonal in both directions and the IoU
is above 0.99. Thingi10K has no reference repairs, so the input itself (its winding number solid) is the
reference.

## Findings with geometry3Sharp 1.0.324 (NuGet)
- No exceptions, hangs or crashes in any of the following setups:
  - the three loaders
  - meshes up to 1M triangles
  - geo coordinates
- 398 of 400 meshes are closed after the repair, but only 144 are topologically clean.
- **`MinEdgeLengthTol` and `RepairTolerance` are absolute.** With data in meters instead of mm:
  - 118/400 meshes lose more than 10 % of their triangles (max 88 %).
  - 32 of the 40 clean meshes are changed.

  Scale both tolerances with the model size or unit.
- Defects introduced by the pipeline (meshes affected, out of 400):
  - duplicate triangles: 40
  - new degenerate triangles: 59
  - new folds: 66
  - inside-out components: 12
  - remaining bowties: 98
- Per step (`analyze_stages.py`):
  - Hole filling (`MinimalHoleFill`, `SimpleHoleFiller`) adds most degenerate triangles and folds.
  - `MeshEditor.DisconnectBowtie` opened 3108 new boundary edges in 9 meshes.
  - `MergeCoincidentEdges` adds duplicates and folds.
  - `MeshRepairOrientation.OrientComponents` can make orientation worse on non-orientable regions.

## Changes in this fork
1. **`MeshEditor.DisconnectBowtie` fix.** The old version grouped the one-ring triangles in a single,
   order-dependent pass and could split one fan into several pieces, which cut cracks into the mesh.
   It now unions the triangles over the edges at the vertex.
   Test: `tests/geometry3Sharp.Tests/MeshEditorBowtieTests.cs`. It fails with 1.0.324 (`dotnet test -p:UseNuGetG3=true`).
2. **`MeshPostRepairCleanup`** (new, `mesh_ops/`). It runs these steps:
   - remove duplicate triangle pairs
   - collapse needles and flip caps
   - flip folds, accepted only if the local fold count drops
   - disconnect bowties
   - reverse inside-out components that are not cavities
3. **`MeshAutoRepair.PostRepairCleanup`** (default `false`) runs the cleanup at the end of `Apply()`.

## Results with this fork (400 set; large set in the last row)
| | NuGet 1.0.324 | fork, default | fork, `PostRepairCleanup = true` |
|---|---|---|---|
| closed | 398 | 397 | 397 |
| topologically clean¹ | 144 | 146 | **289** |
| also no folds / self-intersections | 97 | 97 | **177** |
| meshes with bowtie vertices | 98 | 101 | 1 |
| meshes with duplicate triangles | 40 | 20 | 0 |
| meshes with degenerate triangles | 230 | 230 | 104 |
| meshes with folds | 257 | 257 | 204 |
| meshes with inside-out components | 17 | 14 | 3 |
| clean controls made worse | 0 | 0 | 0 |
| median / max repair time | 0.31 s / 3.7 s | 0.32 s / 3.9 s | 0.39 s / 4.9 s |
| large meshes topologically clean | 7/29 | 7/29 | **16/29** |

¹ closed, no bowties, consistently oriented, no duplicate or degenerate triangles, no inside-out components.

Known remaining issues:
- **Self-intersections are not handled by the cleanup.** `MeshRobustRepair` handles them with a solidify fallback (below).
- **About 100 meshes keep degenerate triangles** that can be neither collapsed nor flipped without creating new defects.
- **One mesh that could not be closed (53121.stl) keeps 2 bowtie vertices.**
- **Results vary slightly between runs (±1–3 defects).** `MeshRepairOrientation` and `MeshNormals` use `gParallel`.

## Fidelity results: solidify fallback and robust repair (400 set; large set in the last row)
Modes (`BENCH_REPAIR`):
- `hybrid_topo`: cleanup; if the result is not topologically clean, solidify the repaired mesh
  (`MeshWindingNumberSolidify`, adaptive dual contouring, resolution 128).
- `hybrid`: as `hybrid_topo`, and exact self-intersections also trigger the solidify.
- `solidify_dc_adaptive`: solidify the input directly.
- `robust`: `MeshRobustRepair`, the library version of `hybrid`. In addition, a clean result whose
  solid differs from the input's (volume IoU < 0.99) is replaced by the solidified input.

| | default | cleanup | hybrid_topo | hybrid | solidify | robust |
|---|---|---|---|---|---|---|
| clean¹ + no exact self-intersections | 100 | 188 | 265 | 342 | 316 | **347** |
| **repaired** (also Hausdorff < 1 %, IoU > 0.99) | 95 | 166 | 186 | **211** | 172 | **211** |
| median repair time | 0.37 s | 0.60 s | 0.68 s | 3.5 s | 5.4 s | 5.2 s |
| large meshes repaired | 4/30 | 5/30 | 6/30 | 9/30 | 6/30 | 9/30 |

The `solidify` run used an older build (before the dual contouring self-intersection fix); `hybrid` and
the earlier modes used the old IoU reference (raw winding number), which scored inside-out inputs as IoU 0.

`robust`, what triggers the solidify (400 meshes): kept the repair 173, self-intersections 96, degenerate
triangles 89 (almost all of them also self-intersect), inside-out components 25, volume differs from the
input 16, not closed 1. Three of the 40 clean controls are solidified: closed and manifold, but
self-intersecting.

Why `robust` misses the strict criterion (189 meshes):
- 116: the solidified mesh deviates more than 1 % (Hausdorff); a cell at resolution 128 is 0.8 % of the
  diagonal.
- 47: self-intersections remain after the solidify (hole fill of non-manifold cells, cells with several
  patches).
- 26: volume (IoU) or other.

Known remaining issues:
- Solidify fidelity is bounded by the grid (see the resolution 256 spike below).
- Dual contouring leaves triangles out where neighbouring cells split a shared face into patches
  differently; filling those holes can create self-intersections.
- Resolving self-intersections exactly (`ExactMeshBoolean.Resolve` on the repaired mesh) instead of
  solidifying keeps the input geometry. A first prototype on 20 failing meshes: 6 clean, the others open
  (the repaired mesh is not closed in the winding number sense) or with self-intersections from rounding
  the new vertices.


### Spike: solidify resolution 256 (`BENCH_SOLID_RES=256`, 30 meshes)
20 meshes that `robust` solidified and that missed the 1 % Hausdorff bound at resolution 128, and 10 it
solidified within the bound (random, seed 1; `results/robust_res256_spike.jsonl`, not versioned):

| | resolution 128 | resolution 256 |
|---|---|---|
| repaired | 7/30 | 13/30 |
| of the 20 over the Hausdorff bound | 0 | 8 |
| of the 10 within the bound: made worse | – | 2 (Hausdorff 0.43 % → 1.15 %; IoU 0.995 → 0.980) |
| self-intersections | – | fewer in 6 meshes, more in 4 |
| median / max repair time | 10.7 s / 42 s | 17.5 s / 293 s |
| output triangles | – | ×2.4 (median) |

Most of the remaining Hausdorff misses at 256 are not grid error: the input has no meaningful solid
(IoU 0.28–0.76 against the winding number solid) or the deviation comes from the repair before the
solidify. Extrapolated to the 400 set, resolution 256 would repair about 30–40 more meshes at 1.6× the
median time, 2.4× the triangles and a few new self-intersections. An adaptive choice (solidify at 128,
check the deviation, repeat at 256 only where it is too large) is cheaper than raising the default.

### Rerun after the dual contouring fixes (`robust`, 400 set)
Same mode on the master of 2026-10-25 evening: dual contouring resolves self-intersections over all
triangles with a last resort position, refines locally where polygons would create non-manifold edges,
the volume IoU reference orients inside-out inputs, and the exact predicates have all adaptive stages.

| | first `robust` run | rerun |
|---|---|---|
| repaired | 211 | 210 |
| clean¹ + no exact self-intersections | 347 | **360** |
| meshes with self-intersections | 51 | **38** |
| median / p90 repair time | 5.2 s / 17.4 s | **3.7 s / 10.6 s** |
| timeouts or crashes | 1 | 0 |
| large meshes repaired | 9/30 | 9/30 |

The fixes remove a quarter of the self-intersecting results and make the repair faster, but the strict
count does not move: the meshes they fix still deviate more than 1 % (Hausdorff), which is the grid limit of
the solidify (136 of the 190 misses). One mesh got worse (61460: 4 self-intersections after the solidify,
before 0, probably at the transition of the new non-manifold refinement).

## Analysis building blocks (`BENCH_REPAIR=analysis`)
`AnalysisBench.cs` runs `MeshWallThickness`, `MeshDeviation` and `PrimitiveFit3` on the inputs as they are
(no repair). `BENCH_ANALYSIS=thickness,deviation,fitting` picks the parts. Thingi10K has no reference
values, so the run records robustness (exceptions, timeouts, NaN shares, times) and checks invariants that
hold for any input:

| Check | Building block |
|---|---|
| inscribed ball ≤ ray, per triangle | wall thickness (Ray, InscribedSphere, ShapeDiameter up to 50k triangles) |
| deviation of a mesh to itself is 0 | deviation |
| pseudo-normal sign = winding number sign, 2000 random points of the box | deviation |
| largest vertex deviation ≤ certified Hausdorff bound, against a copy reduced to half | deviation |
| every inlier within the threshold; same result twice; same quality for the input rotated by 37° | fitting (plane, sphere, cylinder, cone on ≤ 20k vertices, threshold 1e-3 × diagonal) |
| the same relative values in mm, in meters (×0.001) and far from the origin (geo) | all |

### Found and fixed
Each fix has a unit test that fails on the code before the fix.
- **Inside-out meshes:** 2 of the 40 clean meshes are inside out as a whole: 47–97 % of their rays left
  through front faces (NaN), and the two signs disagreed on up to 72 % of the points. Inside-out closed
  components and inside-out meshes are now taken as flipped (`OrientInvertedParts`). The winding number
  sign uses |w| > 0.5.
- **Unit dependence 1:** on 36 meshes the NaN share changed between mm and meters. Coincident faces of
  touching parts are hit at the same t up to rounding, and the scale 0.001 is not exact in binary, so the
  entering face could win over the face the ray leaves through. Hits within a tolerance now count as the
  same distance, and of those the face the ray leaves through wins.
- **Unit dependence 2:** degenerate triangles (edges of 1e-15) have normals that are rounding noise. Below
  1e-12 diagonal² of area they get no value and do not count as walls. The vertex normal no longer
  compares its length with the absolute `MathUtil.ZeroTolerance` (`MeshAnalysisNormals`).
- **Fit depended on the rotation:** on 18 of 206 meshes, a fit of the rotated input reached another
  quality. RANSAC depended on which of several nearly equal samples won. New:
  - LO-RANSAC: every new best is refined by least squares on its inliers.
  - The better model was often a sphere or cylinder of huge radius, or a flat cone, imitating the dominant
    plane. New limits `MaxRadius` (10 × diagonal) and `MaxConeAngle` (88°).
- **Fit missed small structures:**
  - Spheres from 4-point samples ran into `MaxIterations` (9 of 12 cases). With normals, every other
    sample is now 2 points with normals.
  - The test that a sample's own points are inliers threw away rough candidates from noisy vertex normals.
    Removed.
  - Instead, a pre-test on 500 fixed random points keeps the time down (as the T(d,d) test of Matas and
    Chum), and the local optimization uses at most 2000 inliers.

  Compared with the first version (sample test, 4-point spheres), over the 400 meshes (more than 1 % of
  the points better / worse):

  | | better | worse | median time first → now |
  |---|---|---|---|
  | plane | 26 | 4 | 52 → 162 ms |
  | sphere | 131 | 28 | 51 → 142 ms |
  | cylinder | 113 | 43 | 37 → 120 ms |
  | cone | 89 | 21 | 250 → 420 ms |
- **Tried and dropped:** localized sampling (Schnabel et al.: the other points of a sample near the first).
  It finds some small part quickly and then stops, and as it depends on a grid it is not rotation
  invariant: rotated quality differed on 16 of 17 checked meshes instead of 6.

### Results (400 set, final run)
- **No exceptions or wrong results; every invariant holds:**
  - inscribed ball ≤ ray everywhere;
  - self deviation 0;
  - no bound violated (397 of 397 reductions);
  - every fit deterministic and its inliers within the threshold.
- **Timeouts:** 3 meshes exceed the 120 s budget of the whole analysis. Each fit runs three times
  (normal, determinism, rotated), and on these meshes (44k–93k triangles) a cylinder or cone fit takes
  9–14 s. Alone they finish in 73–112 s.
- **Median / p95 time per mesh:**
  - Ray 0.4 / 1.2 s, InscribedSphere 0.6 / 5.4 s, ShapeDiameter 1.0 / 6.9 s;
  - deviation with the certified bound 0.3 / 1.5 s;
  - fits 0.1–0.4 / 0.9–3.7 s.
- **NaN shares:** the median ray NaN share is 0.3 %. It is above 10 % on 56 meshes, all open or
  self-intersecting (the clean ones stay below 1 %).
- **Sign disagreement:** pseudo-normal and winding number disagree on more than 1 % of the points on 64
  meshes, all broken ones (holes, self-intersections). On clean meshes there are at most 1.8 %
  disagreements, from self-intersections.
- **Rotated input:** the quality of the rotated fit differs by more than 1 % of the points on 0 (plane),
  27 (sphere), 29 (cylinder) and 23 (cone) meshes. These are mostly ties between equal features, or small
  structures (5–10 % of the points) whose vertex normals are too noisy for the minimal samples at the
  strict threshold.
- **mm vs meters:**
  - Thickness and NaN shares are identical except on 3 meshes.
    - 702409 and 702413: their inside-out detection (`MeshPostRepairCleanup.FindInvertedComponents`)
      decides differently, probably because touching components put the sample points on another
      component's surface (w ≈ 0.5). This is a separate issue in that function.
    - 100040: 4 triangles of a mesh without a defined inside, whose rays pass exactly through edges of
      self-intersecting regions.
  - ShapeDiameter differs on 11 meshes. The SDF jumps where rays graze a wall.
  - The fits differ on 0–7 meshes per primitive: ties under rounding, as for the rotation.
- **geo:** run pending.

