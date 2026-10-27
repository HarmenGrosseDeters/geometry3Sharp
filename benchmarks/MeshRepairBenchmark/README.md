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
python3 fetch_data.py --holdout  # disjoint second selection in data/holdout/ (see "Holdout selection")
BENCH_DATA=holdout ./run_all.sh robust              # runs on data/holdout/, writes results/holdout/
python3 analyze_fidelity.py results/robust.jsonl    # repaired = clean + no self-intersections + faithful
python3 analyze.py results/default.jsonl            # status per category, exceptions, triangle loss
python3 analyze_quality.py results/cleanup.jsonl    # quality before/after
python3 analyze_stages.py results/instr.jsonl       # which pipeline step introduces which defect
./run_all.sh steps               # library MeshAutoRepair (OnStage): fidelity and winding closedness after every step
python3 analyze_steps.py results/steps.jsonl 0.01 0.99 123354.stl   # which step crosses the bounds (+ steps of a mesh)
./run_all.sh analysis; ./run_all.sh analysis std meters; ./run_all.sh analysis std geo   # analysis building blocks
python3 analyze_analysis.py results/analysis.jsonl results/analysis_std_meters.jsonl results/analysis_std_geo.jsonl
./run_all.sh wrap                # MeshAlphaWrap (BENCH_WRAP_ALPHA / BENCH_WRAP_OFFSET: diagonal / alpha, / offset)
CGAL_WRAP=/path/to/cgal_wrap ./run_all.sh cgalwrap   # CGAL's alpha wrap, same parameters (see below)
python3 analyze_wrap.py results/wrap.jsonl results/cgalwrap.jsonl   # guarantees, fidelity, time; paired
```

`run_all.sh <mode> [loader] [transform] [tolerance factor]` takes these options:
- **loader:** `std` (StandardMeshReader, STL welding), `soup` (no welding) or `manual` (own exact weld + `AppendTriangle`)
- **transform:** `meters` (scale ×0.001) or `geo` (translate by 500000, 5000000, 0)
- **tolerance factor:** `rel` (default): the library default, `MeshAutoRepair` tolerances relative to the size of
  the mesh; a number: absolute tolerances, the 1.0.324 defaults (`RepairTolerance` 1e-6, `MinEdgeLengthTol` 1e-4)
  times the factor (`1` reproduces the earlier absolute runs)

Each mesh runs in its own process with a timeout (120 s, large meshes 900 s), so hangs and crashes are recorded too.

The "repaired" criterion is defined once in `criteria.py` and used by `analyze_fidelity.py`, `analyze_paths.py` and
`analyze_wrap.py`: results whose exact self-intersections were not computed (more than 300k triangles) and results
without an IoU do not count as repaired. Meshes without a result (exception, timeout) stay in the denominators of
`analyze_quality.py` and `compare_modes.py`. `python3 test_analysis.py` checks the scripts on synthetic rows.
Tables below that were made before this (large meshes "repaired", "7/29"-style counts) may differ by these meshes.

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

## Current state (`robust`, 2026-10-26)
Build `5130ff3`: master after the review fixes #106, #111 and #112, plus #110. The result is the same as master `3219260`. Run with `BENCH_P=3`, default options, `analyze_fidelity.py` / `criteria.py`. The numbers in the sections further down come from older builds and different machine loads; they are a history, not the current state.

| | 400 set | large set (30) |
|---|---|---|
| **repaired** (clean, no exact self-intersections, Hausdorff < 1 %, IoU > 0.99) | **270** | **13** |
| path: repair kept / exact resolve / solidify / alpha wrap | 171 / 156 / 53 / 20 | 6 / 16 / 3 / 5 |
| repaired per path | 165 / 98 / 6 / 1 | 5 / 8 / 0 / 0 |
| median / p90 / max repair time | 2.9 s / 10.8 s / 99 s | 20.5 s / 50.5 s / 68 s |
| timeouts, crashes | 0 | 0 |

Compared with master `e9e1599` (before #106, `BENCH_P=2`), every mesh takes the same path and gets the same outcome. The fixes of the review change no result on this set; they cover the failure cases found in the review: unused vertices, components far apart, symmetric inputs to the wrap, and more than 500 closed components.

## Relative repair tolerances (`MeshAutoRepair.ToleranceMode`, 2026-10-26)
`MeshAutoRepair` tolerances relative to the diagonal of the triangles (default) instead of absolute (1e-6 / 1e-4).
`robust`, 400 set, `BENCH_P=2`, `TIMEOUT=300`; master `3e3d307` and the branch were run at the same time in the
same container. Transforms: `meters` = scale ×0.001, `geo` = translate by (500000, 5000000, 0).

| | repaired | clean | path: kept / exact / solidify / wrap | timeouts, crashes |
|---|---|---|---|---|
| master, mm | 272 | 400 | 171 / 178 / 34 / 17 | 0 |
| master, meters | 272 | 399 | 149 / 197 / 34 / 20 | 0 |
| master, geo | 273 | 400 | 171 / 178 / 32 / 19 | 0 |
| relative, mm | 272 | 400 | 172 / 177 / 34 / 17 | 0 |
| relative, meters | 273 | 399 | 174 / 172 / 34 / 20 | 0 |
| relative, geo | 273 | 400 | 172 / 177 / 32 / 19 | 0 |

Meshes whose result differs from the same mesh in mm (path / repaired / any of path, repaired, clean, triangle count):

| | meters | geo |
|---|---|---|
| master | 47 / 10 / 332 | 2 / 1 / 73 |
| relative | 6 / 1 / 114 | 2 / 1 / 74 |

- In meters, master collapsed and merged at 1000 times the relative tolerance: 22 meshes went from "repair kept" to
  the exact resolve, and 10 changed their repaired outcome. With relative tolerances 6 meshes still take another
  path in meters (288801, 47979, 61134, 61460, 72002, 76966), and 1 changes its outcome (47918, which also changes
  under geo). These come from rounding in the later stages (exact resolve, solidify, wrap), not from the repair
  tolerances: `geo` has the same 2 path changes on master and on the branch.
- mm, master vs relative: the same repaired count (272). 1 mesh takes another path (46583: exact resolve → repair
  kept, repaired in both), and 32 meshes differ by a few triangles (the tolerances are 1e-8 / 1e-6 of the diagonal
  instead of 1e-6 / 1e-4, and the orientation evidence is summed in a fixed order).

## Findings with geometry3Sharp 1.0.324 (NuGet)
The NuGet columns in this and the next section cannot be reproduced with the code in this repository. The benchmark project only references the fork, and `Program.cs` uses classes that the NuGet package does not have (`MeshPostRepairCleanup`, `MeshRepairMetrics`, ...). They come from an earlier, unversioned setup.
- No exceptions, hangs or crashes in any of the following setups:
  - the three loaders
  - meshes up to 1M triangles
  - geo coordinates
- 398 of 400 meshes are closed after the repair, but only 144 are topologically clean.
- **`MinEdgeLengthTol` and `RepairTolerance` are absolute.** With data in meters instead of mm:
  - 118/400 meshes lose more than 10 % of their triangles (max 88 %).
  - 32 of the 40 clean meshes are changed.

  Scale both tolerances with the model size or unit. (Since the relative tolerances PR this is the library default;
  `run_all.sh` results without a tolerance factor are relative, earlier ones were absolute.)
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
- (Resolved by the exact stage below for many meshes.)

### Exact resolve before the solidify

`MeshRobustRepair.ExactResolve` (default on): if the repaired mesh is not clean, its self-intersections are
resolved exactly (`ExactMeshBoolean.Resolve`; slivers collapsed by the cleanup, touching sheets separated by
`SeparateTouchingVertices`, resolved again while rounding leaves self-intersections), and the result is used if
it is clean and its volume agrees with the input. Otherwise the solidify runs as before. Same machine, same build,
`BENCH_EXACT=0` switches the stage off:

| 400 set | robust, without exact stage | robust, with exact stage |
|---|---|---|
| clean¹ + no exact self-intersections | 347 | **359** |
| **repaired** (also Hausdorff < 1 %, IoU > 0.99) | 211 | **253** |
| Hausdorff (max of both directions): median | 0.66 % | **0.37 %** |
| IoU: median | 0.9981 | **0.9999** |
| median repair time | 4.4 s | **3.9 s** |
| timeouts (120 s) | 1 | 3 |

42 meshes are repaired that were not before, none is lost. The stage is tried for 225 meshes and used for
119 (79 of them repaired; 11 needed more than one resolve round). Where it is used the median repair time drops
from 5.4 to 4.2 s (no solidify); where it is tried but not used it adds about 2 s. Why it is not used: the
result is not closed after the cleanup (32), the volume differs from the input already after the repair (29),
the repaired mesh is not closed in the winding number sense (28), degenerate triangles remain (17). Two large
meshes (42327, 42329) now hit the timeout.

**Solids that touch themselves along an edge.** Where the surface is connected around both ends of such an
edge, `ExactMeshBoolean` gave the two pairs of pieces at the edge the same vertices, and DMesh3 refused one
pair (a hole): the exact stage failed with "not closed" for 31 meshes, 30 of them for this reason. Now each pair
gets the edge split at a vertex of its own. Same machine, both runs on the current master with and without the
change:

| 400 set | before | after |
|---|---|---|
| clean¹ + no exact self-intersections | 368 | **373** |
| **repaired** (also Hausdorff < 1 %, IoU > 0.99) | 253 | **257** |
| exact stage used | 119 | **130** |
| exact stage not used because "not closed" | 31 | **1** |
| exact stage not used because of degenerate triangles | 21 | 37 |
| median repair time | 3.8 s | 3.8 s |

4 meshes are repaired that were not before, none is lost. Most of the former "not closed" cases now stop at the
next check: slivers left after the resolve and the cleanup (they were hidden, closedness is checked first).

**Slivers left after the resolve.** `MeshPostRepairCleanup` keeps slivers whose collapse would distort or fold a
neighbour, so the exact stage failed with "degenerate triangles" for 36 meshes. `CollapseSlivers` now collapses
them anyway (shortest edge below 1e-4 of the diagonal, link condition kept), flips caps and removes tiny closed
sliver components; the self-intersections that may create are resolved by the next exact round. Same machine,
both runs on master 7f40ba1 with and without the change (`results/robust_base52.jsonl`,
`results/robust_slivers.jsonl`, not versioned):

| 400 set | before | after |
|---|---|---|
| clean¹ + no exact self-intersections | 399 | 399 |
| **repaired** (also Hausdorff < 1 %, IoU > 0.99) | 258 | **261** |
| exact stage used | 132 | **140** |
| solidify / alpha wrap | 70 / 26 | 63 / 25 |
| exact stage not used because of degenerate triangles | 36 | **23** |
| ... because of self-intersections after 4 rounds | 1 | 4 |
| median repair time | 3.7 s | 3.9 s |
| total repair time | 2471 s | 2479 s |

3 meshes are repaired that were not before (126545, 255239, 313876), none is lost. Of the 36, 8 now use the exact
stage, 3 end with self-intersections after 4 rounds, 2 with another reason; 23 keep slivers that can neither be
collapsed (edges longer than the limit or link condition) nor flipped.


**Safe rounding of the resolve** (`MeshRobustRepair.SafeRounding`, default on; `BENCH_SAFE_ROUNDING=0` switches it
off). The resolve rounds the exact arrangement to double and then repairs the slivers and self-intersections that
the rounding leaves (`ExactMeshBoolean.SafeRounding`, `MeshRoundingRepair`: local edits, each checked with exact
predicates against the triangles near it), and repairs again after `SeparateTouchingVertices`. Master with #52
(`CollapseSlivers`), #79 and #94/#97, build 9b06745; both columns from the same build (the switch only), run at the
same time on the same machine, `BENCH_P=2` each (`results/robust_0.jsonl`, `results/robust_1.jsonl`, not versioned):

| 400 set | without safe rounding | with |
|---|---|---|
| clean¹ + no exact self-intersections | 398 | 398 |
| **repaired** (also Hausdorff < 1 %, IoU > 0.99) | 265 | **270** |
| exact stage used | 144 | **156** |
| ... of them repaired | 91 | **98** |
| exact stage not used because of degenerate triangles | 15 | **3** |
| ... because of self-intersections after 4 rounds | 1 | **0** |
| exact stage needed more than one resolve round | 23 | **4** |
| solidify / alpha wrap | 60 / 24 | 53 / 19 |
| timeouts (120 s) | 1 | 1 |
| median / p90 repair time | 3.64 s / 14.8 s | **3.38 s / 13.6 s** |
| sum of repair times | 2573 s | **2504 s** |
| sum of the exact stage (228 meshes tried) | 622 s | 746 s |
| large meshes repaired (30) ³ | 18 | **20** |
| large meshes: exact stage used | 13 | 16 |

5 meshes are repaired that were not before, none is lost: 1037029, 110378, 126542 (the cleanup left slivers),
195683, 338196 (self-intersections after the rounds); large: 562346, 816587. The exact stage costs 20 % more where
it is tried, but where it now succeeds the solidify or alpha wrap is saved, so the total time drops a little.
Exact stage used +12 against the two rows below (-13): one mesh failed for degenerate triangles before and now
because its volume differs from the input. Earlier builds of this change (cd15490 / 3d08569, before master's
#94/#97) gave 266 → 273 in the same comparison. Master's changes to the repair before the exact stage account for
the difference: 112532 now deviates 1.12 % (Hausdorff) in both columns (-1 each); 1102667 is now "not closed
(winding number)" before the exact stage and 1505037 hits the timeout in both columns, which were repaired only
with safe rounding before (-2 in the second column).

³ The benchmark does not check results above 300k triangles for self-intersections and counts them as not repaired.
The large results of the exact stage are checked by the stage itself (exactly); counted here, as the stage reports
them. Strictly counted: 14 → 13 (315359, 562346, 816587 are 329k–497k triangles; `MeshRepairMetrics.SelfIntersections`
finds 0 in all three, Hausdorff 0.55 / 0.39 / 0.80 %, IoU 1.0 / 0.9999 / 0.995).

**Bounded work.** On 1368057.stl (93k triangles, 3326 slivers after the rounding, 426 of which cannot be removed
without an intersection) the first build took 27 s for the safe rounding and 800 s for the repair after the
separation, where `CollapseSlivers` had made 3000 intersections. The repair now bounds the evaluations (50 per defect)
and the exact pair tests (2·10⁷), keeps the old count for proposals of the same triangles and indexes the changed
triangles: 1368057 takes 78 s (60 s of it the exact stage, which still fails; then the solidify, as without safe
rounding, which takes 29 s in all), the same edits elsewhere. It is the slowest mesh that finishes; 1505037 hits
the 120 s timeout in both columns.

### Exact booleans with safe rounding (`BENCH_REPAIR=exact`, `analyze_exact.py`)
`BENCH_REPAIR=exact` computes union, intersection and difference of each mesh with a rotated, shifted copy of
itself (`ExactMeshBoolean`), here with `SafeRounding` (the default of the mode, `BENCH_SAFE_ROUNDING=0` switches it
off). 400 set; without: build cd15490, `BENCH_P=2`; with: 3d08569, `BENCH_P=3`, the meshes where components were
removed rerun with 9b06745 (only closed components are removed: before, removing tiny open ones left 4 results with
holes). The booleans do not depend on the changes between these builds, but the runs had different loads: the times
are not comparable (on the 391 meshes both finished, 2099 s against 2121 s).

| 400 set | without safe rounding | with |
|---|---|---|
| exact winding (closed input), result closed and clean (`analyze_exact.py`) | 107 | **133** |
| ... results (of 3 per mesh; 561 in both) with degenerate triangles | 286 | **43** |
| ... with exact self-intersections ² | 181 | 111 |
| ... not closed in the winding number sense | 0 | 0 |
| all results (1173 / 1176, by the timeouts) with degenerate triangles | 698 | 291 |
| results verified free of both by the repair (`RoundingClean`) | – | 882 |
| timeouts (120 s) | 9 | 8 |

² `MeshRepairMetrics` counts sheets that touch at duplicated vertices (where the solids touch) as intersecting;
safe rounding keeps those contacts, so part of the remaining count is not an intersection. Most of the results the
repair leaves unclean come from open inputs (generalized winding numbers): their results are open, and the repair
does not edit at boundary vertices. The volume identities (V(A∪B) + V(A∩B) = V(A) + V(B)) stay at 1e-14 relative in
the median. The largest new deviations are inputs that are inside-out, whose solid is empty or tiny, so that the
results consist of slivers only:
- 83719.stl: the resolved solid has volume 1e-12 at a diagonal of 64; it is removed as slivers.
- 51352.stl and 51358.stl: the union's volume changes from 2e-5 to 1.2e-3 and from 0 to -4.4e-3. The vertex moves of
  up to 1e-5 of the diagonal sweep that much; it is below 5e-8 of the inputs' signed volumes (-1e5).

**Repair before the exact resolve: cut orientation seams before eroding.** Measured after every step
(`BENCH_REPAIR=steps`), several deviations came from the repair, not the resolve: where a non-orientable
region (a seam of inconsistently oriented edges) ends at a hole, the hole runs against the boundary orientation
and was not filled; the repair eroded the mesh instead (boundary triangles removed, up to 5 times) and filled
the larger holes (123354, 124709: Hausdorff 1.0 % → 1.9–2.3 %). The depth-first `OrientComponents` could also
turn a short seam into a long one (101397: 2 → 175 inconsistent edges), and closed non-orientable meshes
(112866) were not closed in the winding number sense, so the exact stage was not tried. Now:
- `OrientComponents` orients patches of consistent triangles along a maximum spanning tree and keeps a short seam;
- `MeshAutoRepair` cuts the mesh open along remaining seams before the hole filling (and once more at the end
  for closed meshes), so the holes are filled without erosion and the result is closed in the winding number sense;
- `MeshRobustRepair` resolves a repaired mesh whose volume differs once more with its closed components oriented by
  the input's solid (a nested part had been taken for a cavity, 116077), and skips the exact stage above
  `ExactResolveMaxPairs` (200 000) self-intersecting pairs (55928, about 500 000, would otherwise time out).

`MeshAutoRepair.CutOrientationSeams = false` (and `MeshRepairOrientation.KeepShortSeams = false`) restore the
earlier behaviour; the erosion stays as the fallback for holes that still cannot be filled.

Same machine, `BENCH_P=2`, `TIMEOUT=300`, master 7f0f97c (with #44, #52, #79, #96, #110–#112: safe rounding,
lost-component check) with and without the change, both selections run at the same time:

| | first selection, before | after | holdout, before | after |
|---|---|---|---|---|
| **repaired** (clean¹, no exact self-intersections, Hausdorff < 1 %, IoU > 0.99) | 270 | **272** | 266 | **270** |
| meshes repaired / lost | | +3 / −1 | | +4 / −0 |
| IoU < 0.99 | 41 | **35** | 45 | **41** |
| Hausdorff (max of both directions) > 1 % | 122 | **119** | 127 | **123** |
| exact stage used | 156 | **178** | 160 | **168** |
| median / p90 repair time | 3.9 / 15.3 s | 4.4 / 14.7 s | 4.0 / 15.0 s | 4.2 / 17.0 s |
| timeouts (300 s) | 0 | 0 | 1 | 1 (the same mesh, 113906) |

Gained: 101397, 277709, 73932; 132204, 195685, 49874, 88857. Lost: 498458. With the change, its exact stage
succeeds (the repair result oriented by the input), and the safe rounding reports no intersections left, so the
result is used; the benchmark's `MeshRepairMetrics.SelfIntersections` counts 10 pairs in it (the solidify that master
uses there has none). The safe rounding takes its count from `MeshRoundingRepair` when no touching vertices were
separated, which counts contacts differently (see ² above). Earlier measurements of this change: on master a8a5572
266 → 270 / 262 → 267, on 8597e32 258 → 264 / 256 → 260.

Cases the strict criterion cannot reach:
- zero-volume fins of the input (123354, 124709: the repair is faithful, the solid loses them: 1.3–1.5 %);
- a nested inner shell (116077: resolved exactly, the shell is not part of the solid: Hausdorff 4 %);
- inputs with flipped triangles (100037: the repair agrees with the consistently oriented input, IoU 0.9998,
  but the IoU reference is the winding number solid of the input as given, IoU 0.907);
- large open regions (106620, a relief without a bottom: any cap is more than 1 % from the input surface).


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


## Alpha wrapping (`MeshAlphaWrap`)
`BENCH_REPAIR=wrap`: alpha wrap of the input (Portaneri et al. 2022), alpha = diagonal / 100, offset = diagonal /
1000 unless set. Its guarantees are closed, 2-manifold, oriented outwards, no self-intersections, input inside; the
fidelity is set by alpha and offset (cavities and gaps narrower than about 2 alpha are wrapped over, the offset adds
volume). So the strict "repaired" criterion (Hausdorff < 1 %, IoU > 0.99) is not its goal at these settings.

### Against the solidify fallback (400 set; 4 meshes in parallel)
| | wrap (alpha 1/100, offset 1/1000) | `solidify_dc_adaptive` (resolution 128) |
|---|---|---|
| closed, manifold, oriented, no exact self-intersections | **400** | 349 |
| with exact self-intersections | 0 | 49 |
| exceptions / timeouts | 0 | 1 |
| Hausdorff result → input, median / p90 (% diagonal) | 0.99 / 1.15 | 0.46 / 1.18 |
| Hausdorff input → result, median / p90 | 1.57 / 4.80 | 0.91 / 4.95 |
| volume IoU, median | 0.925 | 0.997 |
| output triangles, median | 15k | 7k |
| time, median / max | **2.0 s / 9 s** | 6.8 s / 188 s |

With alpha 1/300 and offset 1/3000 (100 meshes, seed 1, `results/wrap_a300_sample100.jsonl`): all 100 meet the
guarantees (3 results above 300k triangles are not checked for self-intersections by the benchmark; one of them was
checked separately: none), Hausdorff result → input median 0.36 % (solidify 0.48 %), IoU median 0.976 (0.997),
134k triangles, 9 s median (before the speedups of PR #43).

### Against CGAL's alpha wrap
`external/cgal_wrap.cpp` runs `CGAL::alpha_wrap_3` (CGAL 5.6, Ubuntu package, `g++ -O2 -DNDEBUG`, inexact
constructions kernel) with the same relative alpha and offset; `BENCH_REPAIR=cgalwrap` checks its result like the
others. It links against CGAL (GPL): a compiled `cgal_wrap` is not distributed, and CGAL's implementation was not
looked at (docs/SOURCES.md). Build:
```
g++ -O2 -std=c++17 -DNDEBUG -DCGAL_NDEBUG external/cgal_wrap.cpp -o cgal_wrap -lgmp -lmpfr
```
Same 100 meshes (alpha 1/100) and the first 30 of them (alpha 1/300), 4 in parallel; times are each tool's own
wrap time (for MeshAlphaWrap including the JIT warm-up of a fresh process, for CGAL without file I/O):

| | MeshAlphaWrap 1/100 | CGAL 1/100 | MeshAlphaWrap 1/300 | CGAL 1/300 |
|---|---|---|---|---|
| guarantees met | 100/100 | 99/100 ¹ | 30/30 | 30/30 |
| Hausdorff result → input, median (% diagonal) | 0.99 | 0.99 | 0.36 | 0.36 |
| Hausdorff input → result, median | 1.64 | 1.68 | 1.57 | 1.57 |
| volume IoU, median | 0.925 | 0.927 | 0.968 | 0.967 |
| output triangles, median | 15.5k | 17.1k | 141k | 157k |
| time, median / max | **1.5 s / 4.9 s** | 3.7 s / 18.7 s | **7.3 s / 18 s** | 26 s / 110 s |
| time per mesh, ratio MeshAlphaWrap / CGAL, median (p10, p90) | 0.38 (0.17, 1.08) | | 0.27 (0.16, 0.38) | |
| µs per output triangle, median | 94 | 214 | 52 | 158 |

¹ 75846.obj references vertices that do not exist (index 42683 of 41560); CGAL's soup reader does not check the
indices and the driver crashes. g3's OBJ reader drops those faces.

The results agree within the noise; CGAL makes about 10 % more triangles. MeshAlphaWrap is slower than CGAL only on
small inputs (a few hundred triangles, below 1 s), where the JIT warm-up of the fresh process dominates: warm, in
one process, 97171.stl takes 182 ms (cold 451 ms; CGAL 598 ms) and 635745.stl 116 ms (CGAL 344 ms).

## Holdout selection (second, independent sample)
All fixes above were developed and measured on the same seeded selection, so the numbers may be tuned
to it. `fetch_data.py --holdout` draws a second selection of the same shape (6 × 60 broken meshes ≤ 100k
faces, 40 clean controls, 30 large meshes) with other seeds (2026, 2027), excluding every mesh of the
default selection: 0 of 430 meshes in common. Runs with `BENCH_P=2 TIMEOUT=300`, library state of
2026-10-25 evening. First selection with the same build: `default`, `cleanup` (rerun for this
comparison; large meshes not rerun) and `robust` (rerun after the dual contouring fixes above);
`solidify` and the large meshes of `default`/`cleanup` from the fidelity table above (older builds).

| | default | cleanup | robust | solidify |
|---|---|---|---|---|
| clean¹ + no exact self-intersections, holdout | 119 | 193 | **367** | 365 |
| ... first selection | 100 | 188 | 360 | 316 |
| **repaired**, holdout | 110 | 170 | **217** | 182 |
| ... first selection | 94 | 165 | 210 | 172 |
| median repair time, holdout | 0.31 s | 0.38 s | 4.1 s | 4.3 s |
| ... first selection | 0.32 s | 0.45 s | 3.7 s | 5.4 s |
| large meshes repaired, holdout | 0/30 | 4/30 | 8/30 | 8/30 |
| ... first selection | 4/30 | 5/30 | 9/30 | 6/30 |

The sampling error is about ±10 meshes (±3 %) per 400. `hybrid` was stopped after 106 meshes
(52 repaired); since `robust` is its library version, it was not rerun.

- **No sign of overfitting.** On the same build, `robust` repairs 217 vs 210 and leaves 367 vs 360 clean
  without self-intersections; `cleanup` repairs 170 vs 165 (193 vs 188 clean). All within the sampling
  error. The differences beyond it go up, not down:
  - `default`: +16 repaired, +19 clean on the same build. The holdout selection is easier for the plain
    pipeline (the first-selection numbers did not change with the later fixes: 95 → 94). The gain
    shrinks with the cleanup (+5) and `robust` (+7), so the fixes on top of it do not do better on the
    first selection than on new data.
  - `solidify`: +49 clean without self-intersections, the dual contouring fixes made after the
    first-selection run (`robust` gained 13 from them on the first selection).
- No crashes and no timeouts in any mode; all 400 meshes are closed after the repair.
- `robust` per category (repaired / 60): degenerate faces 45, not closed 32, not PWN 32, not vertex
  manifold 26, not edge manifold 23, duplicated faces 20; clean controls 39/40. Two clean controls
  (101556.stl, 439140.stl) are solidified because they self-intersect.
- What triggers the solidify in `robust`: kept the repair 174, self-intersections 105, degenerate
  triangles 83, inside-out components 19, volume differs from the input 19 (first selection: 173, 96,
  89, 25, 16).
- Why `robust` misses the strict criterion (183 meshes, first selection rerun 190):
  - 137 (first selection rerun 136): Hausdorff ≥ 1 % (133 of them solidified, e.g. 100026.stl,
    1017013.stl; 4 kept repairs, e.g. 122126.stl). The grid-bounded fidelity of the solidify is the main
    limit on both selections.
  - 29: self-intersections after the solidify (e.g. 101085.stl, 101952.stl; first selection rerun: 38
    meshes with self-intersections).
  - 13: IoU ≤ 0.99 (e.g. 1004826.stl, 104509.stl).
  - **4, not seen before: the solidified mesh is not consistently oriented** (4–87 edges; 100030.stl,
    226568.stl, 37402.stl, the last also with 4 degenerate triangles).
- Time: the solidify takes 85 % of the `robust` repair time (226 meshes, median 6.4 s); kept repairs
  still take 2.1 s (median) against 0.38 s for `cleanup`, spent in the exact self-intersection and
  volume IoU checks. Outliers that the size does not explain: 78968.stl (5k triangles, 150 s),
  49998.stl (12k triangles, 118 s).

### Which stage repaired which mesh (`robust`, 400 set)
`analyze_paths.py results/robust.jsonl data/selection.csv [--csv out.csv] [--markdown]` sorts the meshes by the
stage of `MeshRobustRepair` that produced the result: the repair itself (clean and the same volume as the
input), the exact resolve, the solidify, or the alpha wrap (last resort, see above); then the outcome by the
strict criterion and why the earlier stages were not used. One row per mesh (category, stage, outcome, reasons,
Hausdorff, IoU, triangles, time): [`cases/robust_stages_2026-10-25.csv`](cases/robust_stages_2026-10-25.csv).
The run below is master after #47 (alpha wrap fallback), before #44 (touching edges in the exact resolve),
#49 (faster checks) and #40 (orientation seams); it will be replaced by the final run.

Stage that produced the result, and its outcome ("deviation": Hausdorff ≥ 1 % of the diagonal):

| stage | meshes | repaired | repaired (self-int. not checked) | deviation > 1 % | volume (IoU) | self-intersections | not clean | Hausdorff median | IoU median | triangles median | time median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| repair | 172 | 165 | 0 | 7 | 0 | 0 | 0 | 0.11 % | 1.0000 | 3k | 2.0 s |
| exact resolve | 122 | 80 | 0 | 42 | 0 | 0 | 0 | 0.56 % | 0.9999 | 8k | 4.0 s |
| solidify | 75 | 9 | 0 | 59 | 7 | 0 | 0 | 2.36 % | 0.9735 | 7k | 7.2 s |
| alpha wrap | 31 | 1 | 0 | 24 | 6 | 0 | 0 | 2.37 % | 0.9441 | 133k | 19.0 s |

Why the stages before were not used (meshes):

| exact resolve | meshes |
|---|---|
| repair: self-intersections | 69 |
| repair: degenerate triangles | 40 |
| repair: inside-out components | 10 |
| repair: volume differs from the input | 3 |

| solidify | meshes |
|---|---|
| repair: degenerate triangles | 32 |
| exact: volume differs from the input | 26 |
| repair: self-intersections | 22 |
| exact: not closed (winding number) | 18 |
| exact: not closed | 18 |
| exact: degenerate triangles | 13 |
| repair: inside-out components | 11 |
| repair: volume differs from the input | 9 |
| repair: not closed | 1 |

| alpha wrap | meshes |
|---|---|
| solidify: self-intersections | 31 |
| repair: degenerate triangles | 17 |
| exact: not closed | 15 |
| exact: not closed (winding number) | 10 |
| repair: self-intersections | 6 |
| exact: degenerate triangles | 4 |
| repair: inside-out components | 4 |
| repair: volume differs from the input | 4 |
| exact: volume differs from the input | 2 |

Stage per defect category (meshes; in brackets: repaired):

| category | meshes | repair | exact resolve | solidify | alpha wrap |
|---|---|---|---|---|---|
| broken:Closed | 60 | 30 (29) | 9 (7) | 17 (1) | 4 (0) |
| broken:Edge manifold | 60 | 20 (19) | 26 (19) | 10 (1) | 4 (1) |
| broken:No degenerate faces | 60 | 28 (26) | 22 (16) | 5 (0) | 5 (0) |
| broken:No duplicated faces | 60 | 18 (18) | 24 (15) | 13 (2) | 5 (0) |
| broken:PWN | 60 | 20 (18) | 21 (12) | 14 (1) | 5 (0) |
| broken:Vertex manifold | 60 | 19 (19) | 17 (9) | 16 (4) | 8 (0) |
| clean | 40 | 37 (36) | 3 (2) | 0 (0) | 0 (0) |

Reading: the repair alone gives 165 of the 255 repaired meshes (most clean controls and meshes that are only
open); the exact resolve adds 80 and keeps the geometry (Hausdorff median 0.56 %). The solidify and the alpha
wrap make every remaining mesh closed and free of self-intersections, but remesh it: about 2.4 % deviation in
the median, so they rarely meet the strict criterion. Their inputs are mostly meshes the exact resolve cannot
take: not closed in the winding number sense, or whose resolved solid differs from the input's.


### `robust` on the current master (both selections)
Master of 2026-10-25 night (`56a8ed5`), with the changes merged since `a66049b`: among them the exact resolve
before the solidify, the alpha wrap as the last resort (#47) and the faster checks (#49). Before: the holdout run above (build `a66049b`). Same machine, `BENCH_P=2
TIMEOUT=300`. The large meshes of the first selection were not rerun.

| | holdout before | holdout now | first selection now |
|---|---|---|---|
| **repaired** | 217 | **256** | **258** |
| clean¹ + no exact self-intersections | 367 | 399 | 399 |
| meshes with exact self-intersections | 33 | 0 | 1 |
| large meshes repaired | 8/30 | 13/30 | – |
| median / p90 / max repair time | 4.1 s / 15.5 s / 150 s | 1.95 s / 10.0 s / 92 s | 1.96 s / 10.2 s / 57 s |
| sum of repair times | 2991 s | 1782 s | 1731 s |

- Both selections agree within 2 meshes. The holdout selection was used while developing #49, so the
  first selection is the cross-check here: no sign of overfitting to either.
- No crashes or timeouts; all meshes closed.
- The size-independent outliers are gone: 78968.stl 150 s → 2 s, 49998.stl 118 s → 3 s.
- Path taken (holdout / first selection) and median time:

  | path | holdout | first | median (holdout) |
  |---|---|---|---|
  | repair kept | 174 | 172 | 0.67 s |
  | exact resolve | 130 | 133 | 2.7 s |
  | solidify | 73 | 69 | 6.5 s |
  | alpha wrap | 23 | 26 | 22 s |

- The alpha wrap cases now take a third of the total time (659 s holdout, 603 s first selection): each
  first runs the exact resolve and the solidify, which are discarded (113906.stl: 11 s exact, 66 s
  solidify, 12 s wrap). Deciding earlier that the solidify will fail is the next lever.

### Effect of the faster checks (#49) alone
A/B on the same machine, one build after the other: master before #49 (`8e98439`) against the #49 branch
(`c5cf323`, the same master plus the speedups), `robust`, `BENCH_P=2`, `TIMEOUT=300` (large meshes 900).
What #49 changes, all with the same results except where noted:
- volume check: `MeshRepairMetrics.VolumeIoUAtLeast` classifies the samples in doubling rounds and stops
  once a Wilson interval (z = 4) lies on one side of `MinVolumeIoU` (all samples near the threshold);
- exact resolve: plane sides and `orient3d` along the classification rays of `ExactMeshBoolean` decided in
  doubles with error bounds, exact only where the filter cannot decide;
- reduction with `PreventSelfIntersections`: hierarchical `TriangleHashGrid`, each triangle reported once
  per query and only if its box overlaps (before: about 196 candidates per query, mostly duplicates);
- fast winding number: cache in arrays, leaf triangle corners contiguous (same values, about 25 % faster);
- `HasSelfIntersections` stops at the first pair (`NotCleanReason` only needs yes/no).

| | holdout before | holdout after | first selection before | first selection after | holdout large before | holdout large after |
|---|---|---|---|---|---|---|
| repaired | 253 | 253 | 254 | 254 | 13/30 | 13/30 |
| clean¹ + no exact self-intersections | 375 | 375 | 369 | 370 | 20 | 20 |
| sum of repair times | 2665 s | **1600 s** | 2450 s | **1551 s** | 1237 s | **627 s** |
| median repair time | 4.06 s | **1.89 s** | 3.85 s | **1.91 s** | 14.8 s | **10.0 s** |
| p90 / max | 13.9 s / 168 s | 9.3 s / 78 s | 11.2 s / 65 s | 8.9 s / 46 s | 67 s / 550 s | 51 s / 129 s |
| solidified meshes: sum | 1514 s | 1104 s | 1227 s | 956 s | 967 s | 443 s |

- Per mesh the time halves (median ratio 0.50 on both selections, 0.79 on the large meshes). The slowest
  meshes gain least: their time is in the solidify contouring, where the fast winding number is slow for CAD
  meshes with long triangles (hundreds of exact solid angles per query); the slowest 10 % now take 48 %
  of the time (before 42 %).
- Largest gains: 247516.stl (large) 550 → 70 s, 113906.stl 168 → 78 s, 1313553.stl 88 → 40 s (reduction
  50 → 19 s), 270401.stl 64 → 16 s, 1514500.stl 36 → 10 s (exact classification 17.7 → 2.9 s).
- No mesh of the holdout selection takes another path. On the first selection two do: 79020.stl flips
  between the solidify and the exact resolve from run to run already before #49 (`MeshAutoRepair` is not
  deterministic there); 45561.stl was accepted after the exact resolve because the volume check decided
  before any sample had hit the (tiny) input solid, fixed on master since (bfb1b0a: no early answer
  without an input sample).
- The losses are 0.2–1.4 s per mesh on solidified meshes, within the run-to-run noise of the solidify.

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
  quality. RANSAC depended on which of several nearly equal samples won. Changes:
  - LO-RANSAC: every new best is refined by least squares on its inliers. This helped in the first
    configuration (11 of 17 checked meshes became stable). In the final one it helps little; see the table
    below.
  - The better model was often a sphere or cylinder of huge radius, or a flat cone, imitating the dominant
    plane. New limits `MaxRadius` (10 × diagonal) and `MaxConeAngle` (88°). Candidates beyond them are
    counted in `RejectedByLimits`.
- **Fit missed small structures:**
  - Spheres from 4-point samples ran into `MaxIterations` (9 of 12 cases). With normals, every other
    sample is now 2 points with normals.
  - The test that a sample's own points are inliers threw away rough candidates from noisy vertex normals.
    Removed.
  - Instead, a bail-out pre-test on 500 fixed random points keeps the time down (Capel 2005), and the
    local optimization uses at most 2000 inliers.

  Over the 400 meshes: better / worse by more than 1 % of the points, and how often the rotated input
  reaches another quality. "first" is the version with the sample test and 4-point spheres; "now, LO off"
  is `LocalOptimization = false`.

  | | now vs first: better / worse | now, LO on vs off: better / worse | rotated differs: first / LO off / LO on | median time: first / LO off / LO on |
  |---|---|---|---|---|
  | plane | 26 / 4 | 3 / 4 | 0 / 0 / 0 | 52 / 144 / 148 ms |
  | sphere | 131 / 28 | 32 / 15 | 12 / 21 / 27 | 51 / 139 / 145 ms |
  | cylinder | 113 / 43 | 46 / 42 | 23 / 16 / 29 | 37 / 108 / 94 ms |
  | cone | 89 / 21 | 52 / 44 | 16 / 16 / 23 | 250 / 298 / 320 ms |

  - The gain over the first version, and the extra time, come from the sample changes (2-point spheres, no
    sample test).
  - LO-RANSAC costs about the same time (median −13 % to +7 % per primitive). It gives somewhat better models, but a rotated input reaches another quality a
    little more often. It stays on by default (`LocalOptimization`).
- **Tried and dropped:** localized sampling (Schnabel et al.: the other points of a sample near the first).
  It finds some small part quickly and then stops, and as it depends on a grid it is not rotation
  invariant: rotated quality differed on 16 of 17 checked meshes instead of 6.

### Results (400 set, final run, master of 2026-10-26 with #97)
- **No exceptions or wrong results; every invariant holds:**
  - inscribed ball ≤ ray everywhere, with the same NaN pattern (master's separation angle fix);
  - self deviation 0;
  - no bound violated (397 of 397 reductions);
  - every fit deterministic and its inliers within the threshold.
- **Timeouts:** 3 meshes exceed the 120 s budget of the whole analysis. Each fit runs three times
  (normal, determinism, rotated), and on these meshes (44k–93k triangles) a cylinder or cone fit takes
  9–16 s. Alone they finish in 73–112 s.
- **Median / p95 time per mesh:**
  - Ray 0.4 / 1.3 s, InscribedSphere 0.5 / 5.2 s, ShapeDiameter 1.0 / 6.6 s;
  - deviation with the certified bound 0.3 / 1.4 s;
  - fits 0.1–0.4 / 0.9–3.1 s.
- **Fit results:** 6 fits end in `RejectedByLimits` (1 sphere, 2 cylinders, 3 cones), 4 in `Failed`.
- **NaN shares:** the median ray NaN share is 0.3 %. It is above 10 % on 63 meshes, all open or
  self-intersecting (the clean ones stay below 1 %); open meshes are never flipped.
- **Sign disagreement:** pseudo-normal and winding number disagree on more than 1 % of the points on 70
  meshes, all broken ones (holes, self-intersections). On clean meshes there are at most 1.8 %
  disagreements, from self-intersections.
- **Rotated input:** the quality of the rotated fit differs by more than 1 % of the points on 0 (plane),
  27 (sphere), 29 (cylinder) and 23 (cone) meshes. These are mostly ties between equal features, or small
  structures (5–10 % of the points) whose vertex normals are too noisy for the minimal samples at the
  strict threshold.
- **mm vs meters:**
  - Thickness and NaN shares are identical except on 100040: 3 triangles of a mesh without a defined
    inside, whose rays pass exactly through edges of self-intersecting regions.
  - 702409 and 702413, which differed before, now agree: master's `FindInvertedComponents` nesting test
    no longer depends on the unit.
  - ShapeDiameter differs on 14 meshes. The SDF jumps where rays graze a wall.
  - The fits differ on 0–7 meshes per primitive: ties under rounding, as for the rotation.
- **geo (offset 5e6):**
  - Before the copy at the origin, `DMeshAABBTree3`'s float boxes (rounding 0.3 at 5e6) made the analysis
    twice as slow (median 11.1 s vs 5.4 s per mesh, 29 timeouts), with up to 30 % of the NaN pattern
    differing.
  - Now ray and inscribed-ball thickness and their NaN shares are identical to the origin on all meshes,
    with the same 3 timeouts and about the same time (median 6.2 s vs 5.7 s).
  - ShapeDiameter differs on 13 meshes and the fits on 0–9, as for meters.
  - The sign agreement differs on 8 meshes, by points near holes.

## Lost components check (`MeshRobustRepair.CheckLostComponents`, off by default)
`MeshRepairMetrics.LostComponents` finds input components whose solid a result lost. The volume IoU check does not notice such parts when they are below 1 % of the volume (eg a screw of an assembly). When the check is on, a repair result (clean or not) or exact resolve that lost a part is rejected, and the fallbacks start from the input. The table shows its effect on `robust` (400 + 30 set, `BENCH_LOST_COMPONENTS=1`). Base is master d5eeda1. "Meshes changed" counts meshes that ended at a different stage or with a different repaired outcome than the base.

| variant | meshes changed | of those repaired (base → with check) |
|---|---|---|
| all components judged (first version) | 29 | 15 → 9 (all 430: 283 → 276) |
| closed components only, box diagonal ≥ 1e-3 of the input's | 3 | 15 → 14 |

- **First version:** most rejections came from open fragments, such as single soup triangles. Next to them the input's solid is the solid of the parts around them, and the result does not reproduce that. The fallbacks then started from the open input: the exact resolve failed with "not closed (winding number)", and the mesh was solidified instead.
- **Closed components only:** three repairs are still rejected. In each, a nested closed part (a part inside another one) was oriented by the repair as a cavity, and the input reference treats it as solid. The fallbacks from the input were worse for 105335 (repaired before, not repaired after). For 1017012 and 67785 the outcome did not change.
- **Conclusion:** the check found no lost part on Thingi10K. It is off by default and meant for inputs with separate small parts.
- **Method:** only the 29 meshes changed by the first version were rerun with the second. The second version judges a subset of what the first judged, so it can change no other mesh. All runs used `BENCH_P=2` on the same machine.

## Alpha wrap result check and box retry (#110)
`MeshAlphaWrap` now checks its result before it reports Success. The result must be closed, have no bowties and no box corners. If the check fails, the wrap runs once more with a slightly different margin on each side of the box (`Counts["box_retries"]`). Rejected Steiner points are counted per rule (`rejected_rule1`, `rejected_rule2`). `wrap` mode, 400 set, base master d5eeda1, `BENCH_P=2`:

| | master | box with different margins on all sides, always (first version) | same box first, retry if the check fails (merged version) |
|---|---|---|---|
| Success | 400 | 399 (286985: box corner on the result, from the manifold step) | 400 |
| results different from master | | most (other box) | none (no retry needed) |
| Hausdorff both < 1 % | 44 | 46 | 44 |
| meshes with rejected rule 1 points | 3 (99911: 1835, 1681235: 5, 702413: 1) | 2 (99911: 2090, 1681235: 24) | as master |

- Synthetic sweep (spheres, boxes, a cylinder, soups; alpha = diagonal / 5 to / 50; 84 wraps): master reported Success with box corners on the result for 2 of them (a unit sphere, alpha = d/10 and d/15). The merged version retries those 2 and gets results without box corners.
- `robust`: the 25 meshes that reach the wrap fallback give the same results as before.
- Rejected rule 1 points with Success stay possible, as on master (99911). They are counted. The result still encloses the input and does not cross it, but it can come closer to the input than the offset there.
- I also tried exact circumcenters for nearly flat cells. They did not change the rejections on these meshes, so they are not included.

### Wrap fallback without the discarded solidify post-processing (`robust`, both selections)
Meshes that end in the alpha wrap ran the whole solidify first, including its cleanup and reduction, only to
discard the result for its self-intersections. Measured on the 191 meshes that `robust` solidified or wrapped
(both selections, contour checked before and after each post-processing step): all 49 discarded solidifies
already self-intersect in the raw contour, and of the 142 kept ones only one does (56107.stl, whose 4
intersecting pairs the reduction happened to remove; the result misses the strict criterion either way, Hausdorff
4.6 %). No cheap criterion before the solidify separated the two groups: the self-intersections of its input,
the reason the exact resolve failed and the hidden gap counts of the coarsest contour all overlap.

So `MeshWindingNumberSolidify.StopIfSelfIntersecting` (set by `MeshRobustRepair` when it wraps on
self-intersections) checks the raw contour and, if it self-intersects, skips the cleanup and the reduction; the
alpha wrap follows right away (`FinishPostProcess()` completes the solidify if the wrap fails). The check is free
where the dual contouring has already verified its contour (`DualContouring.SelfIntersectionFree`, it resolves
self-intersections and stops when an exact pass finds none): of the 141 kept solidifies, 6 needed the explicit
check (non-manifold fills or unresolved pairs), 2.6 s in total.

`BENCH_P=2 TIMEOUT=300`, both selections run at the same time on a 4-core machine (so the times are about 1.5×
those of a single run), master `d545c60` before, this change after:

| | first selection, before | after | holdout, before | after |
|---|---|---|---|---|
| repaired | 258 | 258 | 256 | 256 |
| clean + no exact self-intersections | 399 | 399 | 399 | 399 |
| path: repair / exact / solidify / wrap | 172 / 133 / 69 / 26 | 172 / 133 / 69 / 26 | 174 / 130 / 73 / 23 | 174 / 130 / 72 / 24 |
| wrap cases (meshes wrapped before): total time | 974 s | 853 s (−12 %) | 1011 s | 866 s (−14 %) |
| ... median | 30.0 s | 26.0 s | 35.0 s | 30.5 s |
| ... of it cleanup + reduce of the solidify | 110 s | 0 s | 122 s | 0 s |
| solidify cases (solidified before): total time | 778 s | 761 s | 872 s | 896 s |
| all 400: total time | 2723 s | 2599 s (−4.6 %) | 2678 s | 2572 s (−4.0 %) |

The only changed path is 56107.stl (solidified before, wrapped now; Hausdorff 4.6 % → 6.4 %, IoU 0.983 →
0.959, neither repaired). The solidify-case times differ within the noise of the shared machine (the holdout
total includes 56107.stl). The raw contour remains the larger part of the remaining wrap-case time (439 s and
463 s of the solidify, wrap 279 s and 284 s); avoiding it would need a predictor before the contouring, which
the data above does not offer.

Rerun after merging master `a8c9f8d` (safe rounding of the exact output, `MeshRoundingRepair`), which sends more
meshes through the exact resolve and fewer to the wrap; same setup, master before, this change after:

| | first selection, before | after | holdout, before | after |
|---|---|---|---|---|
| repaired | 270 | 270 | 266 | 266 |
| clean + no exact self-intersections | 399 | 399 | 395 | 395 |
| path: repair / exact / solidify / wrap | 171 / 156 / 53 / 20 | 171 / 156 / 53 / 20 | 174 / 160 / 51 / 14 | 174 / 160 / 49 / 16 |
| wrap cases (wrapped before): total time | 679 s | 603 s (−11 %) | 467 s | 398 s (−15 %) |
| ... of it cleanup + reduce of the solidify | 77 s | 0 s | 65 s | 0 s |
| all 400: total time | 2724 s | 2629 s (−3.5 %) | 2371 s | 2298 s (−3.1 %) |

113906.stl times out (300 s) before and after (holdout; counted as not repaired). Two more meshes whose contour
self-intersects but whose reduction removed the intersecting pairs are wrapped now instead of solidified:
112854.stl and 99736.stl (holdout). Neither is repaired before or after (Hausdorff 8.8 % and 3.4 % solidified),
but the wrap fits their winding number solid worse (IoU 0.958 → 0.421 and 0.993 → 0.512; Hausdorff 8.8 % → 1.3 % and
3.4 % → 4.8 %). Together with 56107.stl that is 3 of about 150 solidified meshes per two selections
where stopping early gives up a cleaner (though still not repaired) solidify.

### Is the wrap fallback still needed? With and without it, on the former wrap cases
The 32 meshes of both selections that an earlier run (before #44, #49, #40) wrapped, `robust` on master `e30ee26`
with and without `BENCH_WRAP_FALLBACK` (`BENCH_P=2`). 17 of them now go through the exact resolve; 13 are still
wrapped. Without the wrap, these 13 are returned with 6 to 7944 exact self-intersections:

| 32 former wrap cases | without wrap | with wrap |
|---|---|---|
| clean + no exact self-intersections | 19 | 31 (+1 not computed, > 300k triangles) |
| repaired (strict criterion) | 5 | 5 |

The wrap stays on as the last resort: it repairs none of them in the strict sense (its deviation is about the
offset plus what it wraps over), but it turns 12 self-intersecting results into clean ones.

### Simplifying the wrap result (`MeshRobustRepair.SimplifyAlphaWrap`)
The wrap has triangles of about alpha everywhere, also on flat regions: median 160k triangles on the 13 cases above.
`SimplifyAlphaWrap` (default on) reduces it (quadric error up to (offset / 2)², no collapse that creates a
self-intersection) and keeps the result only if it is clean and a one-sided Hausdorff upper bound certifies it
within half the offset of the unsimplified wrap, so that it stays about half an offset outside the input; otherwise
the unsimplified wrap is the result (`AlphaWrapSimplified`). `BENCH_WRAP_SIMPLIFY=0` turns it off;
`robust_wrap_tris` is the triangle count of the unsimplified wrap. The 13 wrapped meshes on master `42f53be` (with
#145) plus this change, 2 in parallel:

| 13 wrap cases | not simplified | simplified |
|---|---|---|
| triangles, median (max) | 160k (415k) | 34k (170k) |
| triangles / not simplified, median (p10, p90) | | 0.29 (0.13, 0.66) |
| simplified wrap accepted | | 13 of 13 |
| Hausdorff to the input, median | 2.20 % | 2.20 % (largest change 0.014 points) |
| IoU, median | 0.769 | 0.769 (largest change 0.002) |
| clean + no exact self-intersections | 12 (+1 not computed, > 300k triangles) | 13 |
| time per mesh, median (max) | 33 s (50 s) | 55 s (141 s) |
| ... of it the simplification, median (max) | | 24 s (101 s) |

Two earlier versions on the same 13 meshes (master `3d58441`):
- Certifying every collapse (`Reducer.MaxDeviation` = offset) took 10 to 30 times longer than the
  self-intersection guard and changed no triangle count (46244.stl: 15356 triangles either way, 176 s vs 14 s):
  median 159 s per mesh, one timeout at 900 s. Checking the whole result afterwards is enough.
- A bound of a full offset gives fewer triangles (median 30k; 46244.stl 15k instead of 22k), but the simplified
  wrap can then reach the input (the wrap lies only about one offset outside it), and on 87345.stl the quadric
  underestimated the distance (2.5 offsets, rejected by the check).

## Rejected rule 1 Steiner points in the alpha wrap: cause and fix
The cause was the oracle's triangle normals, which came from `Vector3d.Normalized`. That returns zero below an absolute length of 2.2e-16, so a small but valid triangle got no normal (99911 has 10 of them). `slab_entry` then treated every segment through the triangle's node box as starting inside the offset volume (`FirstEntry` = 0). The wrap inserted the far end of the dual edge, which lies outside the box, and the insertion was rejected (`Outside`). The facet stayed on the result and could be closer to the input than the offset.

The normals now come from `AlphaWrapOracle.Unit`, which has no absolute epsilon. A rule 1 rejection that still happens makes a Success result `Failed`. The manifold step keeps outside components with a box corner outside. Base: master 7f0f97c. `BENCH_P=2`, 400 set (`wrap`) and the 25 meshes that reach the wrap fallback (`robust`):

| | master | this change |
|---|---|---|
| `wrap`: Success | 400 | 400 |
| `wrap`: meshes with rejected rule 1 points | 2 (99911: 1835, 1681235: 5) | 0 |
| `wrap`: Hausdorff both < 1 % | 44 | 46 |
| `wrap`: IoU median / p10 | 0.924 / 0.688 | 0.924 / 0.688 |
| `wrap`: box retries | 0 | 0 |
| `robust`, 25 meshes that reach the wrap: repaired / median Hausdorff / median IoU | 1 / 1.93 % / 0.926 | 1 / 1.93 % / 0.926 |
| `robust`, all: repaired | 270 / 400, 13 / 30 | the same (the wrap runs only on those 25) |

Rule 1 rejections no longer happen on this set, so the stricter status (a Success with rejected rule 1 points is now Failed) costs nothing here. The change in how the normals are computed rounds them differently in the last bits. That changes many wrap results slightly: 275 of 400 differ in triangle count, but not in quality.

## Deterministic solidify (`WindingNumberOctree`, propagated node values)
`MeshWindingNumberSolidify` was not bit-for-bit deterministic across runs: with propagation, a node of the
winding number octree either evaluated its own fast winding number or copied a cached neighbour's, depending on
which neighbours other threads had evaluated first. The two differ in the last digits, which moved a few contour
vertices by about 1e-8 and, after the reduction, changed the triangle count (procedural thin plates at resolution
64, 2 cores: 13812 or 13828 triangles; the raw contour differed in 4 vertices in about a third of the runs). Values
that may be copied are now computed from the winding numbers rounded to integers (exact off the surface of closed,
oriented meshes), so a copy equals the node's own value.

`robust` on both selections, `BENCH_P=2 TIMEOUT=300`, both selections at the same time on 4 cores and nothing
else on the machine, master `7f0f97c` before, the change after (the first revision; the final one also no
longer allocates an array per node evaluation, which only removes work, and gives the same node values):

| | first selection, before | after | holdout, before | after |
|---|---|---|---|---|
| repaired | 270 | 270 | 266 | 266 |
| clean + no exact self-intersections | 399 | 399 | 395 | 395 |
| path changes | | 0 | | 0 |
| solidify phase, meshes that solidified (73 / 65) | 1164 s | 1164 s | 1042 s | 1085 s |
| all 400: total time | 4147 s | 4206 s (+1.4 %) | 3573 s | 3663 s (+2.5 %) |
| a second run of the same build: meshes that differ | 1 | 0 | 0 | 0 |

- Time: +1.4 % and +2.5 % in total, the solidify phase +0 % and +4 %. Two other runs of the change (one with
  other load during part of it) differed from each other by 3–5 % in total, so the difference is not clearly
  outside the noise of this machine; the change adds one rounding per evaluated node.
- The rounding does not change the results measurably: the same repaired and clean counts, no path changes; 137 of
  the 138 solidified or wrapped meshes have the same triangle count as on master (not 498458.stl).
- Two runs of the change give the same triangles, Hausdorff bounds, IoU and path on all 800 meshes. On this set
  the solidify's nondeterminism did not show up on master either: the one difference between two master runs,
  1017012.stl, is an exact-resolve result whose Hausdorff bound differs in the fifth digit. The unit tests show it
  (`AdaptiveOctreeTests.Octree_PropagatedNodeValues_DoNotDependOnTheQueryOrder`, which fails on the old code every
  time, and `MeshRobustRepairTests.Solidify_IsDeterministic`).

## Solidify bit for bit under 2^k scales
`InvarianceTests.Solidify_SameResultUnderSimilarity` found that the solidify gave different meshes for the same
input scaled by 2^10 or 2^-10 (exact in floating point), from absolute constants:
- the dual contouring's QEF compared the errors of the cell-restricted solutions with `1e-15 * (1 + |err|)`, an
  absolute tolerance for a squared length (one vertex moved by half a cell at 2^10); now
  `1e-12 * (cellSize^2 * planes + |err|)`;
- `MeshRefinerBase.TriangleQuality` was 0 when the summed squared edge lengths were below 1e-8, so on small meshes
  the reducer's `MinCollapseTriangleQuality` (used by the solidify) did nothing and the remesher's degenerate-flip
  tests were always on; and its normal-flip metric treated cross products below 2.2e-16 as zero;
- `MeshWindingNumberSolidify` and `WindingNumberOctree.Bounds` (also `MeshWindingNumberBoolean`) sized the grid
  from `CachedBounds`, which includes unused vertices: one far away gave an empty result (reported by QA).

`robust` on both selections, `BENCH_P=2 TIMEOUT=300`, both selections at the same time on 4 cores and nothing
else on the machine, master `2a455dc` before, the change after:

| | first selection, before | after | holdout, before | after |
|---|---|---|---|---|
| repaired | 272 | 272 | 270 | 270 |
| clean + no exact self-intersections | 398 | 398 | 395 | 395 |
| path changes | | 0 | | 0 |
| solidified or wrapped meshes / with another triangle count | 51 | 16 | 58 | 21 |
| ... Hausdorff (max of both directions), median | 3.67 % | 3.67 % | 4.66 % | 4.66 % |
| ... IoU, median | 0.9742 | 0.9741 | 0.9808 | 0.9808 |
| solidify phase | 660 s | 648 s | 847 s | 854 s |
| all 400: total time | 4001 s | 3993 s | 3751 s | 3694 s |

The ties in the QEF and the quality checks of small triangles now decide differently, which changes the triangle
count of about a third of the solidified meshes, but not their quality or the time. Repair and exact-resolve
results are unchanged.

## Further fidelity measures (`MeshFidelityMetrics`, `fm_*` fields)

Hausdorff distance and volume IoU decide "repaired". They do not see lost features, reversed patches, topology
changes or slivers, and they count interior faces that a repair to a solid drops on purpose as lost. Every run
now also records `gs.MeshFidelityMetrics` against the input (`benchmarks/Shared/FidelityFields.cs`; inside-out
closed components of the input reversed, and the whole input if its signed volume is negative;
`BENCH_FIDELITY=0` skips it). `analyze_fidelity.py` prints these measures as extra columns. The "repaired"
criterion stays as it is.

| Field | Measure (relative to the input's size: the diameter of its bounding sphere around the area centroid) |
|---|---|
| `fm_f_*`, `fm_p_*`, `fm_r_*` | F-score, precision (result within τ of the input), recall (input within τ of the result) at τ = 0.1 %, 0.5 %, 1 % (Tatarchenko et al. 2019); 50k area-weighted samples per mesh |
| `fm_of_*`, `fm_or_*`, `fm_outer_*` | the same against the input's **outer surface** only: where the input's winding number jumps by one (a single layer) and the rest does not enclose the point. Leaves out interior faces, nested shells, internal double walls and double-sided fins; `fm_outer_share` is the outer share of the input, `fm_outer_max` the largest sampled distance from it to the result |
| `fm_max_fwd/back` | largest sampled distance in each direction (a lower bound of the Hausdorff distance) |
| `fm_nc`, `fm_nangle_*`, `fm_reversed_*` | normal consistency (mean \|cos\| at the closest points), mean angle, share of samples within 1 % whose normals point against the other mesh's |
| `fm_ecd*`, `fm_ef/ep/er_*`, `fm_oer_*` | Edge Chamfer distance and edge F-score / precision / recall on sharp edges (dihedral > 30°, not at slivers narrower than 1e-5 of the size; Chen et al. 2022); `fm_oer_*`: recall of the input's outer sharp edges (edges inside the solid, eg of overlapping parts, left out) |
| `fm_topo_in/out`, `fm_topo_changes` | components, closed components, genus, Euler characteristic, boundary loops, bowties, discs; components lost / added, handles closed / created, boundary loops removed / added |
| `fm_q_in/out` | Verdict triangle quality: min / max angle, aspect ratio, scaled Jacobian, condition number (worst, 1 %, 5 %, median), counts outside the acceptable ranges |

They take a median of 1.5 s per mesh (max 13 s), next to 2.2 s for the Hausdorff and IoU measures.

### What they show that Hausdorff and IoU miss (`robust`, 400 set, branch 44cd69f, `BENCH_P=3`, `TIMEOUT=300`)

273 of 400 repaired, the same as on master (#147: 272-273). **19 of the 273 repaired meshes** raise a flag
(`analyze_fidelity.py`). These are cases where the "repaired" criterion is satisfied but something is not right,
or at least worth a look:

| Flag | Repaired meshes | Example: what the measures see (Hausdorff max of both directions, IoU) |
|---|---|---|
| handles: genus changed, input topologically clean | 4 | 357788 (exact resolve, HD 0.56 %, IoU 0.9996): one closed component, **genus 1 → 3**: the resolve created two handles |
| parts: components lost, input topologically clean | 3 | 702409, 702413 (exact, HD 0.31 %, IoU 1.0): 18 overlapping parts → 1, genus 4 → 0; the union of overlapping parts, expected for a solid, but the outer sharp edges are only 69 % / 82 % kept (`fm_oer`) and the triangles drop from 9268 to 3748 |
| reversed: > 1 % of the result points against the input | 3 | 814760 (repair kept the triangles, HD 0.10 %, IoU 0.997): **4.8 % of the result is inside-out**. The input has a closed inside-out part, and against the raw input the result agrees everywhere, so the part kept its orientation. `FindInvertedComponents` and `NotCleanReason` no longer report it in the result (3 → 2 components). 105335 (2 inside-out parts in the input) looks the same at 2.1 % (not checked mesh by mesh). 88572: a wall 0.03 % thick moved by 0.1 %, so the closest points lie on its other side (the measure cannot tell this from a flip) |
| edges: < 90 % of the outer sharp edges kept within 0.5 % | 6 | 1458679, 301927 (exact): 26 % / 27 % of the outer sharp edges lost at HD 0.11 % / 0.96 %; 72609 (solidify at 128 cells): 13 % rounded off; 39086 (kept): 13 % of very few edges |
| triangles: share with min angle < 10° up by > 1 % | 8 | 498458 (exact, HD 0.64 %, IoU 0.992): triangles 47k → 91k, **slivers 40 % → 53 %**; 1037029: 35 % → 44 %. The exact resolve's retriangulation adds slivers |
| surface: F-score 0.5 % < 0.95 | 2 | 346287 (exact): F 0.93; 45561 (wrap of an input without a solid): F 0.94 |

Two corrections were needed on real data before the measures could be trusted. Both are covered by tests now:
- **Sharp edges at slivers:** a first run counted 16 repaired meshes with lost edges. Every "lost" edge lay at a
  triangle of the input with a height below 1e-5 of the size (float STL rounding tilts such slivers by tens of
  degrees). These edges are no longer sharp (`SharpEdgeMinHeight`).
- **Inside-out open inputs:** two inputs that are open and inside-out as a whole (50309, 51358) showed 100 %
  reversed against correct, outward results. The benchmark now reverses such inputs first.

The flags are a list to look at, not a verdict. The union of overlapping parts (702409) and the genus change of
broken inputs are often what a solid repair should do. That is why the topology flags only count inputs whose
topology is defined (closed, no bowties). The reversed share, the outer edge recall, the handle count and the
sliver share are the ones that point at real defects above.

**Outer surface instead of the whole input** (the Objaverse question below): with the input-to-result
Hausdorff distance taken as the largest sampled distance (`fm_max_back`, a lower bound), 277 of 400 pass. Taken
from the outer surface only (`fm_outer_max`), 313 pass, **36 more**. The difference is only the interior and
double-sided faces of the input (its median outer share is 1.0, p10 0.86).

## Objaverse corpus (second data set)

Thingi10K holds broken 3D-print meshes. [Objaverse 1.0](https://huggingface.co/datasets/allenai/objaverse)
(Sketchfab models: game assets, scans, CAD exports) breaks differently: open shells, many separate parts,
double-sided and interior faces, inconsistent normals. `fetch_objaverse.py` downloads a reproducible selection
as a second corpus (docs/QA.md 2.7a):

```
python3 fetch_objaverse.py                      # ~620 MB: 300 objects with 500..100k faces, data/objaverse/
BENCH_DATA=objaverse ./run_all.sh robust        # writes results/objaverse/robust.jsonl
python3 analyze_fidelity.py results/objaverse/robust.jsonl
python3 analyze_paths.py results/objaverse/robust.jsonl
```

- **Selection:** dataset revision pinned (`REVISION` in the script); 4 of the 160 metadata shards and 300 of
  their objects drawn with seed 42 (`--count`, `--shards`, `--seed`).
- **Licenses:** the collection is ODC-By; every object has its own license. Only CC0 and CC-BY objects are
  selected (no NC, ND or SA). `data/objaverse/manifest.csv` lists uid, license, source URL and the
  attribution of every object. Nothing of it is committed (CLAUDE.md); results that name objects must keep
  their attribution.

**Baseline (`robust`, master 6ec47ed, `BENCH_P=4`, 120 s timeout):**

| Objaverse, 300 objects | |
|---|---|
| closed + topologically clean | 279 |
| ... and no exact self-intersections | 272 |
| **repaired** (also Hausdorff < 1 %, IoU > 0.99) | **64** |
| `timeout_or_crash` | 17 |
| repair time median / max | 9.6 s / 104 s |

The 17 are timeouts under the parallel load, not crashes: run alone with a 200 s timeout, three of four
checked finished in 50-60 s without an exception, the fourth took longer than 200 s. For comparison only (other
conditions: `BENCH_P=2`, `TIMEOUT=300`, an older master), the robust repair of the Thingi10K selection repaired
272 of 400 (above).

Per stage (`analyze_paths.py`): the first stage repaired 23 of 26; the exact resolve 39 of 136, solidify
2 of 64, alpha wrap 0 of 57. Most misses are deviation, not defects: the 97 exact-resolve results that miss
are all clean, without self-intersections and with IoU > 0.99, at a median Hausdorff distance of 2.7 %
(1.1 % to 25 %); in 82 of them the larger part is the distance from the input to the result (`hd_back`), ie
input surface that the result does not have. The likely cause was interior and double-sided faces that the
solid's boundary drops on purpose. The outer surface measures (next section) confirm it for most of them.

### With the outer surface measures (branch 44cd69f, `BENCH_P=3`, `TIMEOUT=300`)

Same 300 objects, robust repair, `fm_*` fields (section "Further fidelity measures"). 64 repaired, as in the
baseline above, and 5 `timeout_or_crash` (17 above at `BENCH_P=4`, 120 s):

| Objaverse, 300 objects | |
|---|---|
| repaired (certified Hausdorff < 1 % both ways, IoU > 0.99) | 64 |
| ... with Hausdorff input→result sampled from the whole input (`fm_max_back`) | 70 |
| ... sampled from the input's **outer surface** only (`fm_outer_max`) | **124** (+54) |
| outer share of the input: median / p10 | 0.94 / 0.58 (Thingi10K: 1.0 / 0.86) |
| F-score 1 %: against the whole input / its outer surface, median | 0.994 / 1.0 |

The baseline's misses: 120 results are clean, without self-intersections and with IoU > 0.99, but not
repaired. In 96 of them the input→result direction dominates. 60 of these 96 pass once only the outer surface
counts. Their input's outer share has a median of 0.90 and a p10 of 0.56: those were interior and double-sided
faces, not losses. The other 36 also miss from the outer surface. 16 of them miss in the result→input direction
as well, so they are real deviations.

Remaining reasons under the outer surface criterion (295 objects with a result, counted once per combination;
"outer" = the sampled outer surface distance is ≥ 1 %):

| failing | objects |
|---|---|
| IoU only | 31 |
| result→input Hausdorff only | 31 |
| outer + IoU | 45 |
| outer only | 18 |
| result→input + outer (+ IoU) | 35 |
| other (result→input + IoU, not clean, self-intersections) | 9 |

The IoU uses the input's winding number solid, which the same interior faces also distort. That would be the
next measure to restrict to the outer surface. Of the 64 repaired objects, 17 raise a flag: 14 the sliver share,
3 the reversed share and 3 the outer edge recall. So on this corpus too the flags list cases to look at, they do
not change the count.
