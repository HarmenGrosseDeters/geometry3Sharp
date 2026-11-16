# What limits the thread scaling of ExactMeshBoolean (2026-11-15)

Analysis only: no library change. Question (owner): what limits the speedup with the number of threads, so that the boolean gets faster
on the owner's 12-thread machine. Master 3b2ea3c (after #843), `ExactMeshBoolean` with `SafeRounding` (the default), the T6 case set.
The measuring tools and the exact commands are at the end; the run script for the owner's machine is
[`perf/thread_scaling.ps1`](../../benchmarks/BooleanComparison/README.md#thread-scaling-on-the-t6-set-perfthread_scalingps1).

## Summary

1. **The serial repair loop of the rounding is not what limits the scaling.** On the T6 set the edit loop of `MeshRoundingRepair`
   (`repair_passes`, `fix_degenerate`, `fix_intersection`, `improves`, `proposed_drift`, Ghost allocations, position search) takes
   **0.2 % of the boolean's time** (83 of 40 659 ms at 1 thread; 215 edits in 294 booleans, at most 80 in one; `position_search` and
   `proposed_drift` do not show up). The same result as T1b of 2026-11-13 ([EXACT_BOOLEAN_T1B_PROFILE.md](../design/EXACT_BOOLEAN_T1B_PROFILE.md),
   0.54 % on the heavy sample). #843 changed nothing on this set because there is nothing there to change. Parallelizing the edits is
   not a lever (bound: +0.2 % at any thread count).
2. **What does not scale is the serial work around the parallel loops.** Whole boolean, 294 booleans, 4 cores: 1.80× on master (s = 0.41 in
   Amdahl's law; 1.89× and s = 0.37 in the probe build, on which the shares and bounds below are computed); the parts that stay serial at 4 threads, with their share of the 4-thread time:
   box-tree builds 17 % (the input trees 9.5 %, the rounding's tree 7.8 %: below 262 144 boxes the build is serial), the assembly of the
   result 11 % (append triangles, `group_edges`, union-find, vertices), classification's serial steps 6 %, `ExactPlanarSimplify` 5 %, setup
   and input 4 %, rounding's component scales, tiny components, `atPosition` 2-3 %.
3. **The largest lever is a constant**: `TriangleBoxTree.ParallelBuildFrom = 262144` keeps the tree build serial for every T6 operand
   (1k-100k triangles). Setting it to 4096 (a benchmark switch, no library change): about −5 to −7 % on the 4-thread time of the whole set (two runs each, not interleaved, noise ±3 %; the trees phase
   itself falls clearly, 1826 → 1040 ms), with the same result hash for every boolean (the `profile` hash covers vertex ids, coordinates and triangles, not the exact vertices; all 4116 rows of the 14 runs in
   `perf/thread_scaling_2026-11-15/`, at 1, 2 and 4 threads). Cold, it takes the rounding of the three cases below from 129 / 127 / 117 ms to 113 / 100 / 110 ms at 4 threads (one run each, `cold_three.txt`).
4. Removing all serial parts (levers 1-6 below, Amdahl upper bound, probe build): 1.89× → 2.82× at 4 threads. The 12-thread figures (2.35× → 4.75×) are an
   extrapolation with the s of the 4-thread run on a machine without SMT, not a measurement; the 12-thread run on the owner's machine replaces them.
5. The two parallel loops that dominate the work (the self-join of each operand, 29 % of the 1-thread time, and the rounding's
   first pair search, 11 %) reach 3.1× and 2.9× at 4 threads. More, smaller tasks gave −3.8 %, which two runs cannot tell from the noise (±3 %); the
   loss against linear scaling is about 6 % of the 4-thread time. At 12 threads they are expected to be the largest block (from the s of the 4-thread run); the cause is not found
   (lever 7).

## Method, strata

- **Cases**: the T6 set (`perf/t6.py pairs`: 98 Thingi10K pairs, 1k-100k triangles in total, 294 booleans), SafeRounding on, results with
  a median of 7.5k triangles (p90 50k). Fetched at run time, nothing committed. The three cases named by the earlier session (p0466_203344+80414 difference,
  p0861_80557+285607 union, p0974_97674+472023 union; the largest roundings in its cold, one-process-per-boolean measurement) are among them; warm they rank only 13th, 49th and 12th by rounding time at 1 thread (the largest warm: p0887 union, 272 ms).
- **Stratum A (the tables below): warm**, `BooleanComparison profile --repeat 2 --safe-rounding`: one process runs all 98 cases after a
  warm-up, the faster of two runs per boolean counts, sums over the 294 booleans. This is a long-running application. A cold
  process (one per boolean, as `compare.py` runs it: JIT of the pipeline is partly paid in the boolean) is slower and scales worse,
  see the three cases below.
- **Machine**: the cloud container, 4 vCPU of an Intel Xeon @ 2.30 GHz (4 cores, no SMT), .NET 10.0.115. Threads: `taskset -c 0..n-1` and
  `DOTNET_PROCESSOR_COUNT=n`. Noise between two runs of the same configuration: 3-4 % of the total. Not measured here: 8 and 12 threads (the
  machine has 4), net48 (needs Windows): the script does both.
- **Instrument**: a probe patch ([`perf/serial_probe.patch`](../../benchmarks/BooleanComparison/perf/serial_probe.patch), not merged; it
  applies to master 3b2ea3c): Stopwatch scopes around the serial sub-steps of `MeshRoundingRepair`, `assemble` and `classify`, and the wall time
  of every `gParallel.ForRange` (by call site, labelled by the phase that follows). Switched on by `G3_SERIAL_PROBE=1`, read by
  `profile` as the `probe` field; it changes no result (the hashes of the probe runs equal those of the master runs, all 294 booleans) and costs 4-12 % of the time (36.4 s without it, 37.9 s and 40.7 s
  with two builds of it at 1 thread; 20.2 s and 21.5 s at 4). Shares are of the probe's total.
- **Not used**: `dotnet-trace` installs from NuGet (10.0.750501) and works; sampling stacks would give function-level shares, but the
  question is serial against parallel wall time, which the scopes measure directly. `dotnet-counters` was not needed: GC pauses are
  0.4 s of 36 s at 1 thread and 0.3 s at 4 (`gc.pause_ms` of the rows), so the GC is not what limits the scaling in this stratum.

## 1. The phases (the existing phase timers)

`perf/scaling_report.py --bound 1=… 2=… 4=…` ([raw rows](../../benchmarks/BooleanComparison/perf/thread_scaling_2026-11-15/), master, no probe).
s = serial fraction of Amdahl's law implied by the speedup (1 = no speedup, 0 = linear); "bound" below is the total speedup at 4 threads
if that phase alone were linear.

| phase | 1 thr ms | 2 thr | 4 thr | ×4 | s(4) | share at 1 thr |
|---|---|---|---|---|---|---|
| input | 283 | 221 | 225 | 1.26 | 0.73 | 0.8 % |
| setup.points | 580 | 555 | 582 | 1.00 | 1.00 | 1.6 % |
| setup.triangles | 322 | 201 | 145 | 2.22 | 0.27 | 0.9 % |
| intersections.trees | 2174 | 1785 | 1738 | 1.25 | 0.73 | 6.0 % |
| intersections.cull / planes / intersect | 279 / 498 / 567 | 203 / 329 / 475 | 171 / 237 / 365 | 1.6 / 2.1 / 1.6 | 0.5 / 0.3 / 0.5 | 3.8 % |
| intersections.pairs | 12083 | 6450 | 4033 | 3.00 | 0.11 | 33.2 % |
| merge (points, crossings, predict) | 2250 | 1177 | 842 | 2.7 | 0.2 | 6.2 % |
| triangulation | 2592 | 1326 | 906 | 2.86 | 0.13 | 7.1 % |
| classification | 1657 | 1463 | 1394 | 1.19 | 0.79 | 4.6 % |
| simplify (inside assembly in the rows, listed apart) | 1070 | 949 | 968 | 1.11 | 0.87 | 2.9 % |
| assembly (without simplify) | 2652 | 2279 | 2324 | 1.14 | 0.84 | 7.3 % |
| rounding | 7363 | 5003 | 4273 | 1.72 | 0.44 | 20.2 % |
| rest (outside the phase timers) | 2005 | 1973 | 2031 | 0.99 | 1.02 | 5.5 % |
| **total** | **36 375** | **24 389** | **20 234** | **1.80** | **0.41** | 100 % |

(The cold numbers of the earlier session, 113.6 s → 80.8 s over the same 294 booleans with one process each, are 1.4×: JIT and a cold heap add serial
time at the start of every process; the warm sums above are 36.4 → 20.2 s, 1.8×.)

## 2. (a) Rounding, sub-step by sub-step

Probe, sums over the 294 booleans (`perf/probe_report.py`, wall ms, inclusive; `R.Apply` is the whole rounding):

| sub-step of `MeshRoundingRepair.Apply` | 1 thr | 4 thr | ×4 | share of `Apply` at 1 thr | parallel? | splittable? |
|---|---|---|---|---|---|---|
| `intersecting_pairs_all` (first full pair search) | 4670 | 1638 | 2.85 | 54 % | parallel (`ForRange` over triangles) | already; balance only |
| `rebuild_tree` (`TriangleBoxTree`, serial below 262 144 boxes; boxes per block in parallel) | 1999 | 1678 | 1.19 | 23 % | boxes only | **yes**: the build exists in parallel, bit-identical, behind the constant |
| first per-triangle scans (anchors, finite, range; `for_triangles`) | 673 | 298 | 2.26 | 8 % | parallel blocks | already |
| `degenerate_triangles` (310 calls) | 323 | 157 | 2.05 | 4 % | parallel blocks | already |
| rest of `Apply` (`atPosition`, `exactAtStart`, boundary, `canonId`: serial setup loops; the remainder) | ≈344 | ≈382 | 0.90 | 4 % | serial | partly (`atPosition` dictionary: by sort; the rest are scans) |
| `component_scales` (union-find over triangles) | 257 | 254 | 1.01 | 3 % | serial | yes (parallel union-find gives the same components) |
| `remove_tiny_components` (40 calls) | 182 | 153 | 1.19 | 2 % | serial | by component |
| **the edit loop**: `repair_passes` (fix_degenerate 37, fix_intersection ≈0, improves 32, proposed_drift 1, commit_drift 0, try_collapse 28, try_flip 5) | **83** | **62** | 1.34 | **1 %** | serial | inherently ordered (edits against one mesh, ids by order); **not worth it** |
| `position_search`, `separate_false_contacts`, `remove_contact_vertices` | 0 | 0 | – | 0 % | serial | – |
| `false_contacts` (540 calls) | 40 | 38 | 1.04 | 0.5 % | serial | – |

Why there is no edit loop to speak of: 215 edits over 294 booleans (median 0, maximum 80); the rounding finds 0-1 intersecting pairs or degenerate
triangles in a typical result of a boolean of two clean models, so the cost is the *certification*: a tree, a full pair search, a scan.
`proposed_drift` and the Ghost allocations are called 231 times in total (384 Ghosts proposed), 1 ms. The edit loop dominates only on inputs with thousands of edits
(119829 union, T1b: 13 447 edits), which T6 does not contain.

**The three cases of the earlier session** (p0466 difference, p0861 union, p0974 union): warm, 48-92 ms of 230-345 ms of the boolean at 1 thread; cold (one process, best of 3, probe on; `cold_three.txt`):

| case | threads | boolean ms | rounding ms | `rebuild_tree` | pair search | `for_triangles` first scan | `component_scales` | `remove_tiny` | edit loop | edits |
|---|---|---|---|---|---|---|---|---|---|---|
| p0466 difference | 1 / 4 | 547 / 406 | 160 / 126 | 46 / 41 | 21 / 11 | 39 / 17 | 13 / 14 | 20 / 22 | 10 / 4 | 1 |
| p0861 union | 1 / 4 | 447 / 341 | 133 / 105 | 63 / 52 | 5 / 7 | 27 / 12 | 15 / 15 | 0 | 0 | 0 |
| p0974 union | 1 / 4 | 606 / 400 | 174 / 139 | 40 / 46 | 52 / 27 | 36 / 15 | 13 / 14 | 17 / 17 | 8 / 3 | 1 |

In these (cold stratum) the rounding does not scale because **the tree (35-50 % of it), the component
scales and the tiny components are serial**, and the first scan carries the JIT of the blocks; the edit loop is 0-10 ms of 105-174. With the tree
switch (`--tree-parallel-from 4096`, same hashes) the 4-thread rounding of these falls to 113 / 100 / 110 ms (−12 / −21 / −6 %), cold, one run each,
noise included (at 1 thread p0861 got slower, 139 → 187 ms).

## 3. (b) Triangulation and the other phases

| step | 1 thr ms | 4 thr ms | ×4 | serial / parallel | splittable | ordered because |
|---|---|---|---|---|---|---|
| triangulation (`ForRange` over input triangles; phase time without the probe) + the serial gather of pieces | 2592 | 906 | 2.86 | parallel per input triangle | already | – |
| merge.points / crossings / predict | 755 / 224 / 1271 | 410 / 54 / 378 | 1.8 / 4.1 / 3.4 | parallel clusters, triangles | already | – |
| intersections.trees (per mesh, 2 tasks; serial build inside) | 2499 | 2045 | 1.22 | parallel over the **2** meshes only, one dominates | **yes**: the same constant | – |
| intersections.pairs: self-join (`par ExactMeshArrangement.cs:2210`) and mesh pair (`:1926`) | 11 857 + 1376 | 3809 + 431 | 3.11 / 3.20 | parallel over 256 tasks / triangles | balance | – |
| setup.points (`build_points`) | 580 | 582 | 1.00 | serial | probably (exact points by index) | ids of points by order of creation |
| input (`prepare_inputs`: finite, closed, oriented per mesh) | 283 | 225 | 1.26 | per mesh | per triangle block | – |
| classification: `group_edges` C.2 | 590 | 586 | 1.01 | serial radix sort | **yes**: stable radix sort by blocks | the result order (stable) must stay |
| classification: patch joins C.3 (union-find over edges) | 472 | 452 | 1.04 | serial | yes (parallel union-find: the same partition) | the representative root changes, the patch order is by first piece |
| classification: patch winding C.5 | 571 | 275 | 2.08 | parallel over patches | already | – |
| classification: open prep C.4, decisions C.6 | 132 + 110 | 113 + 103 | 1.1 | serial | decisions in blocks | `keep`/`flip` per piece, independent |
| assembly: gather kept pieces A.1, slots A.2, used points A.5 | 106 + 25 + 53 | 87 + 22 + 49 | 1.2 | serial scans | yes (blocks) | – |
| assembly: `group_edges` A.3 | 501 | 466 | 1.07 | serial radix sort | yes (as C.2, the same function) | stable |
| assembly: edge pairing A.4 (union-find of corner slots) | 418 | 359 | 1.17 | serial | yes (same partition) | pairing at edges with more than two pieces is by order |
| assembly: vertices A.7 | 344 | 311 | 1.11 | serial | by prefix scan | `AppendVertex` ids by first occurrence over triangles |
| assembly: **append triangles A.9** | 1077 | 1015 | 1.06 | serial `DMesh3.AppendTriangle` | only as a bulk builder that reproduces the ids | triangle and edge ids are by order of insertion (the hash includes them) |
| assembly: PointsToVectors A.6 | 349 | 132 | 2.65 | parallel | already | – |
| `ExactPlanarSimplify` (`simplify`; probe build, 1070 / 968 without it) | 1253 | 1085 | 1.15 | serial (no `gParallel` in the file) | per planar region / component | the order of the result triangles |
| `count_contacts` and counts after the rounding | 202 | 197 | 1.03 | serial dictionary over the vertices | yes (sort by position) | – |

Everything not listed (planes, cull, intersect, crossings) is parallel and reaches 1.6-4×. "rest" of section 1 (5.5 % warm) is the arrangement's own untimed steps (about
1 s), `count_contacts` (0.2 s) and the input lap (0.46 s); not split further.

## 4. Building blocks in the library, literature

**In the library** (search of `core/gParallel.cs`, `mesh_ops/ExactMesh*`, `docs/design/DETERMINISM*.md`):

- `gParallel.ForRange(count, body)` / `(count, localInit, body)`: `Parallel.For` over index ranges, `SerialOnThisThread` and
  `SerialOrderOnThisThread` run it serially in any permutation (the environment adversary of `DETERMINISM_PROOF.md`, tests in
  `ExactBooleanDeterminismTests`): every new loop that writes its own slots is covered by it.
- Blocks of 4096 ids with one result slot per block (`TriangleBlock`, `TriangleBoxTree.BlockSize`): the pattern of `MeshRoundingRepair` (#843),
  `degenerate_triangles` and the tree's boxes; the result is gathered in index order.
- `TriangleBoxTree.build_parallel`, `sort_codes_parallel` (fixed runs sorted apart, merged pairwise, total order by (code, index): the same
  tree as the serial build; `SortRuns`, `TopDepth`), and `SelfPairTasks` (a fixed split of the self-join into tasks whose lists are
  appended in task order): schedule-independent by construction. Both are behind `ParallelBuildFrom = 262144` and `SelfJoinTasks = 256`.
- Sharded point indexes (`Shards = 64` dictionaries per key kind) with the test hooks `HashSalt` and `SingleShard` (issue 789): they show that no result depends
  on hashes or shards, which is the model for any parallel insertion: canonical order by `ComparePoints`, never by id.
- Thread-local state through `ForRange(count, localInit, …)` (`PatchSearch`, hit lists), the pattern for any loop that needs buffers.
- Exact code: loops in `math/Exact*`, `mesh_ops/ExactMesh*` need an entry in `core/ExactCodeAllowList.cs` and a review (no floating-point reduction
  across threads; a new loop in `ExactPlanarSimplify` or `ExactMeshBoolean.assemble` falls under this).

**Literature** (papers only, no code read; a short check, 2026-11-15): no paper on parallel snap rounding was found. Snap rounding
(Hobby 1999; Goodrich, Hershberger, Tanenbaum, SoCG 1997; Halperin and Packer: iterated snap rounding) is given as a sequential sweep or per-segment
algorithm. The nearest for the repair is parallel mesh adaptation by colouring: Rokos, Gorman, Kelly, *A thread-parallel algorithm for anisotropic mesh
adaptation* (arXiv 1308.2480: a maximal independent set of vertices per round, deferred commits) and PaMO (arXiv 2509.05595, 2025: independent regions,
one collapse per region, intersection check after each round, undo). Both change the order of the edits, i.e. the result, and both need the independent
regions to be far apart: with 0-80 edits per boolean there is nothing to gain from them here. EMBER (Trettner, Nehring-Wirxel, Kobbelt, SIGGRAPH 2022) is parallel by design (local arrangements, a multi-threaded work-stealing
subdivision, no global acceleration structure built beforehand): it avoids the serial tree build of lever 1 and the global assembly of lever 2 by construction, which we cannot. Not read: CGAL, Triangle, GEOS or other copyleft code.

## 5. Lever list

Measured on the T6 set, warm, 4 cores, **probe build** (1.89× instead of 1.80× because the probe costs more at 1 thread than at 4); **s** = serial fraction at 4 threads; "now" is the whole boolean's speedup. Bounds are Amdahl upper bounds for removing the
group's serial part alone (`perf/lever_bounds.py`); the 12-thread column is an extrapolation with the same s (the serial part does not shrink, the parallel part does; for a loop with load imbalance s usually grows with the threads), the real
gain on the 12-thread machine is to be taken from `thread_scaling.ps1`. "Realistic" = about half of the bound, the guess for a loop that has to be written; for the constant it was measured
(+5 to +8 % against a bound of +14 %). All levers must give **bit-identical results** (the hash of `profile` over 294 booleans, 1, 2, 4 threads); none touches the
exact arithmetic itself, 1, 2, 3 and 5 touch the exact files (`ExactMeshArrangement`, `ExactMeshBoolean`, `ExactPlanarSimplify`) and so the allow-list rule.

| # | lever | measured serial share | s | bound at 4 thr (now 1.89) | bound at 12 thr (now 2.35) | realistic | risk | effort |
|---|---|---|---|---|---|---|---|---|
| 1 | **Parallel tree build below 262 144 boxes** (`ParallelBuildFrom` ≈ 4096; the build, the Morton sort and the merge exist and equal the serial tree). Affects the input trees and the rounding's `rebuild_tree` (also the dirty-tree rebuilds) | 17 % of the 4-thread time (9.5 % + 7.8 %) | 0.77 | 2.15 | 2.88 | **measured about −5 to −7 %** of the 4-thread time (mean of two runs 20.8 → 19.4 s, not interleaved; trees phase 1826 → 1040 ms, rounding 4471 → 3951 ms, means), same hashes; at 12 threads more | low: the code and its tests exist (`ParallelBuildFrom` is set by tests); a constant in an exact file, no new loop; check the median-split trees (< 4096) stay serial | S (a day: the constant, a test at the new threshold, the benchmark) |
| 2 | **Assembly's serial chain** (A.1-A.5, A.7-A.10): parallel stable `group_edges`, parallel pairing, vertices by prefix scan, a bulk `DMesh3` builder for the triangles | 11 % (2324 ms) | 0.89 | 2.05 | 2.67 | +4 to +6 % at 4, +7 to +10 % at 12 | medium: vertex, triangle and edge **ids** must stay identical (the hash covers them); A.9 (append triangles, 1.0 s) needs a builder that reproduces `AppendTriangle`'s ids; the other steps are blocks and a union-find with the same partition | M for `group_edges` / pairing / vertices, L for A.9 |
| 3 | **`ExactPlanarSimplify`** per planar region or component (`simplify`) | 5 % | 0.82 | 1.96 | 2.49 | +2 to +3 % / +4 to +5 % | medium-high: exact code, an order of the result triangles that must be kept (collect per region, concatenate in region order); new loop: allow-list and review | M |
| 4 | **Classification's serial steps**: `group_edges` (the same function as in 2), patch joins (the union-find of 2), decisions in blocks | 6 % | 0.95 | 1.97 | 2.52 | +3 % / +5 % | low-medium (shares the work of 2) | S after 2 |
| 5 | **Rounding's serial setup**: `component_scales`, `remove_tiny_components`, `atPosition` / `exactAtStart` / `canonId` loops, `false_contacts`, the `count_contacts` after it | 2.4 % + 0.9 % | 0.87 | 1.92 | 2.42 | +1 to +2 % / +2 to +3 % | low (not exact code; same results required: the components and their areas as now) | S-M |
| 6 | setup.points (`build_points`) and the input lap | 4.0 % | 0.87 | 1.94 | 2.46 | +1 to +2 % / +2 to +3 % | medium (point ids by creation order) | M |
| 7 | **Load balance of the two big parallel loops** (self-join 3.1×, rounding pair search 2.9× at 4 threads; in the work, 29 % and 11 %) | 19.7 % + 7.6 % of the time, 0.09 / 0.13 | 0.09-0.13 | 1.97 (self-join) / 1.93 (rounding) | 2.52 / 2.43 | 4 tasks per thread are not the cause: `SelfJoinTasks` 256 → 2048 gave −3.8 % (inside the ±3 % noise); next: cache/memory behaviour, per-thread buffers, work-stealing granularity of `Parallel.For`; or less work (the neighbour pairs that `touch_only` discards: 11.9 s of 1-thread time) | low (the loops exist) | M, unknown cause |
| 8 | Parallelizing the rounding **edit loop** (`repair_passes`, `proposed_drift`, Ghosts, position search) | **0.2 %** (83 ms of 40.7 s) | – | 1.89 | 2.35 | none | high (an L-tier change of the edit order, results change; T1b: closed) | **do not build** |
| 9 | JIT / cold start (cold process: 3× a warm boolean; rounding 105-175 ms of the three cases, half of it the tree and the first scans) | cold stratum only | – | – | – | `TieredCompilation=0` (README, "After (cold)": 1.4-1.9×), not a code lever | – | – |

Combined (1, 2, 3, 4, 5, 6, i.e. every serial part at once, not 7): upper bound **2.82× at 4 threads, 4.75× at 12** against 1.89× and 2.35× now; realistic: lever 1 (+7 %, measured) and lever 2 (+5 %) together about 2.1× at 4 threads. At 12 threads the serial parts weigh more: under the measured s the whole boolean reaches only 2.35× at 12 threads, and the pair loops (7) are then the
largest block, not the serial tails.

Order of work: 1 (days, measured, a constant; a test at the new threshold is still to be added) → 2 with 4 (one `group_edges`, one union-find) → 5 → 3 → 6; 7 needs a diagnosis first (a probe of the tasks' times).
On the owner's machine (6 cores, 12 threads with SMT) run `thread_scaling.ps1` first: with SMT the second thread of a core gives 20-30 %, not 100 %, which the bounds above do not know.

## Experiments (benchmark-side switches, no library change in the PR)

4 threads, T6 set, warm, two runs each (total of the 294 booleans, ms; the best-per-boolean sum of both runs in brackets). All hashes equal to the 1-thread master run (all 14 files, 4116 rows, one hash per boolean).

| variant | run 1 | run 2 | mean | against control |
|---|---|---|---|---|
| control (master) | 20 448 | 21 122 | 20 785 (19 603) | – |
| `--tree-parallel-from 4096` | 19 209 | 19 496 | 19 353 (18 332) | −6.9 % (two runs, noise ±3 %) |
| `SelfJoinTasks` = 2048 (probe branch, `G3_SELF_TASKS`) | 19 834 | 20 160 | 19 997 (18 934) | −3.8 % (not separable from noise) |
| both | 19 762 | 19 709 | 19 736 (18 603) | −5.1 % (not additive: noise) |

## Reproduce

```sh
# case set and data (fetched at run time into $D; about 300 MB)
D=/path/outside/the/repo/t6
python3 benchmarks/BooleanComparison/perf/t6.py pairs $D/pairs_t6.csv
python3 benchmarks/BooleanComparison/perf/t6.py fetch $D $D/pairs_t6.csv
python3 benchmarks/BooleanComparison/data/thingi_pairs.py manifest $D/pairs_t6.csv $D $D/cases.jsonl
# one directory A.off / B.off per case (BooleanComparison thingi-pair <a> <b> <dir> <q> <t>, as compare.py does), cases-t6.txt: one directory per line
# the scaling run, one process per thread count (this container: 1, 2, 4)
for n in 1 2 4; do DOTNET_PROCESSOR_COUNT=$n taskset -c 0-$((n-1)) dotnet frozen/BooleanComparison.dll profile --repeat 2 --safe-rounding cases-t6.txt > base_t$n.jsonl; done
python3 benchmarks/BooleanComparison/perf/scaling_report.py --bound 1=base_t1.jsonl 2=base_t2.jsonl 4=base_t4.jsonl
python3 benchmarks/BooleanComparison/perf/scaling_report.py --hashes 1=base_t1.jsonl 4=base_t4.jsonl
# the probe: apply perf/serial_probe.patch to master 3b2ea3c, build, run with G3_SERIAL_PROBE=1 at 1 and 4 threads
python3 benchmarks/BooleanComparison/perf/probe_report.py --top 60 1=probe_t1.jsonl 4=probe_t4.jsonl
python3 benchmarks/BooleanComparison/perf/lever_bounds.py probe_t1.jsonl probe_t4.jsonl 4 12
# the tree experiment (no patch needed)
DOTNET_PROCESSOR_COUNT=4 taskset -c 0-3 dotnet frozen/BooleanComparison.dll profile --repeat 2 --safe-rounding --tree-parallel-from 4096 cases-t6.txt
```

Raw rows (gzip): [`perf/thread_scaling_2026-11-15/`](../../benchmarks/BooleanComparison/perf/thread_scaling_2026-11-15/) (`base_t{1,2,4}`: master; `probe2_t{1,4}` and `probe_t2`: the probe build;
`exp_*`: the experiments). The three named cases cold: `BooleanComparison profile --repeat 1 --safe-rounding --ops <op> <case dir>` with the probe build, best of 3 processes.
