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
./run_all.sh analysis; ./run_all.sh analysis std meters; ./run_all.sh analysis std geo   # analysis building blocks
python3 analyze_analysis.py results/analysis.jsonl results/analysis_std_meters.jsonl results/analysis_std_geo.jsonl
./run_all.sh wrap                # MeshAlphaWrap (BENCH_WRAP_ALPHA / BENCH_WRAP_OFFSET: diagonal / alpha, / offset)
CGAL_WRAP=/path/to/cgal_wrap ./run_all.sh cgalwrap   # CGAL's alpha wrap, same parameters (see below)
python3 analyze_wrap.py results/wrap.jsonl results/cgalwrap.jsonl   # guarantees, fidelity, time; paired
```

`run_all.sh <mode> [loader] [transform] [tolerance factor]` takes these options:
- **loader:** `std` (StandardMeshReader, STL welding), `soup` (no welding) or `manual` (own exact weld + `AppendTriangle`)
- **transform:** `meters` (scale ×0.001) or `geo` (translate by 500000, 5000000, 0)
- **tolerance factor:** multiplies `RepairTolerance` and `MinEdgeLengthTol`

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
(`CollapseSlivers`) and #79, `BENCH_P=2`; both columns from the same build (cd15490, the switch only), run at the same
time on the same machine; the final build (3d08569, bounded work, see below) repairs the same 273, with no timeout
(`results/robust_nosafe.jsonl`, `results/robust_safe.jsonl`, not versioned):

| 400 set | without safe rounding | with |
|---|---|---|
| clean¹ + no exact self-intersections | 398 | 398 (final build: 399) |
| **repaired** (also Hausdorff < 1 %, IoU > 0.99) | 266 | **273** |
| exact stage used | 140 | **153** |
| ... of them repaired | 91 | **100** |
| exact stage not used because of degenerate triangles | 13 | **2** (final build: 3) |
| ... because of self-intersections after 4 rounds | 2 | **0** |
| exact stage needed more than one resolve round | 21 | **3** |
| solidify / alpha wrap | 63 / 24 | 55 / 19 |
| timeouts (120 s) | 1 | 1 (final build: 0) |
| median repair time | 3.67 s | 3.63 s |
| sum of repair times | 2638 s | 2507 s |
| sum of the exact stage (227 meshes tried) | 534 s | 632 s |

7 meshes are repaired that were not before, none is lost: 1037029, 1102667, 110378, 126542 (the cleanup left slivers),
195683, 338196 (self-intersections after the rounds), 1505037 (timeout without). The safe rounding edits the
resolve in 56 of 161 rounds, the repair after the separation in 20. The exact stage costs 18 % more where it is
tried, but where it now succeeds the solidify or alpha wrap is saved, so the total time does not grow. Large
meshes (30): exact stage used 13 → 16 (315359, 562346, 816587); the strict count goes 14 → 13 because the benchmark
does not check results above 300k triangles for self-intersections, and these three are above (329k–497k). The exact
stage checks them exactly, and `MeshRepairMetrics.SelfIntersections` finds 0 in all three: Hausdorff 0.55 / 0.39 /
0.80 %, IoU 1.0 / 0.9999 / 0.995, so with them 18 → 20 large meshes are repaired, none is lost.

**Bounded work.** On 1368057.stl (93k triangles, 3326 slivers after the rounding, 426 of which cannot be removed
without an intersection) the first build took 27 s for the safe rounding and 800 s for the repair after the
separation, where `CollapseSlivers` had made 3000 intersections. The repair now bounds the evaluations (50 per defect)
and the exact pair tests (2·10⁷), keeps the old count for proposals of the same triangles and indexes the changed
triangles: 1368057 takes 74 s in the final run (56 s of it the exact stage, then the solidify, as without safe
rounding, which takes 30 s), the same edits elsewhere.

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
  - Instead, a bail-out pre-test on 500 fixed random points keeps the time down (Capel 2005), and the
    local optimization uses at most 2000 inliers. `LocalOptimization = false` turns the local optimization
    off (plain RANSAC).

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
but the wrap fits their winding number solid worse (IoU 0.958 → 0.421 and 0.993 → 0.512; the Hausdorff distance
improves to 1.3 % and 4.8 %). Together with 56107.stl that is 3 of about 150 solidified meshes per two selections
where stopping early gives up a cleaner (though still not repaired) solidify.
