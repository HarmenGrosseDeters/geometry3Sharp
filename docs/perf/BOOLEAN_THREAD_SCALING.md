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
6. (2026-11-16, [section 7](#7-two-probes-on-the-head-of-962-the-discarded-pairs-of-the-pair-stage-and-the-rounding-sub-phases-2026-11-16)) On the head of #962: in the pair stage 99.7 % of the 81 M candidate pairs end without a crossing (the self-join of the clean T6 operands rejects 100 %), and using `TriangleTriangleFilter` before the exact test in `touch_only` takes 18 % off the pairs phase (−6 % of the boolean at 1 thread, −4 % at 4, bit-identical); the rounding's serial setup steps are 0.82 s, 5 % of the boolean at 4 threads.

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

## 6. Levers 2 and 4 built: the assembly and the classification's edge groups (2026-11-16)

Built in one PR: parallel stable `group_edges` (classification C.2 and assembly A.3), the pairing of corner slots and the patch joins with an
order-independent union-find (A.4, C.3), the vertices by a prefix sum over the classes (A.7), the used points (A.5), and a bulk builder for the
result mesh that gives the ids of `AppendVertex` / `AppendTriangle` (A.9, for the case that no edge has more than two pieces or two pieces
running it the same way; the rest appends as before). `ExactPlanarSimplify` (lever 3), the rounding's setup (5) and the gather A.1 are untouched.

**Upper bound first** (T6 set, 294 booleans, warm, `profile --repeat 2 --safe-rounding`, sub-phase timers `assembly.*_us` / `classification.*_us`,
sums in ms, master 414c6b5 plus the timers; raw rows `perf/assembly_parallel_2026-11-16/probe_base_t{1,4}.jsonl.gz`):

| step | 1 thr | 4 thr | what it is |
|---|---|---|---|
| assembly: gather A.1 / edges A.3 / pairing A.4 / used A.5 / vertices A.7 / triangles A.9 | 93 / 467 / 397 / 53 / 280 / 932 | 84 / 441 / 382 / 50 / 290 / 901 | serial (coords A.6: 286 / 135, already parallel) |
| classification: edges C.2 / patches C.3 / open prep C.4 / first pieces / decisions C.6 | 534 / 442 / 130 / 60 / 96 | 525 / 421 / 125 / 68 / 97 | serial (stacks, winding: parallel) |

The serial chain is 2148 ms of the 19 541 ms at 4 threads (11.0 %) in the assembly and 1236 ms (6.3 %) in the classification. An ideal linear speedup
of both at 4 threads would save 1611 + 927 = 2538 ms, **13.0 %**, the bound; the doc's estimate (4-6 %) is about half of it. The sizes of T6 limit the real
gain: the median result has 7.5k triangles, a parallel pass costs 10-100 µs, and a sort of 65 536 entries takes 1.3 ms serially, so the loops
run in parallel from 65 536 entries (`ExactMeshEdgeGroups.ParallelFrom`) and the smaller booleans run as before.

**Building blocks in the library** (searched first): `gParallel.ForRange` and its serial hooks, the blocks of 4096 with a slot each (`TriangleBlock`,
`TriangleBoxTree.BlockSize`), `TriangleBoxTree.sort_codes_parallel` (a sort of (code, index) pairs by runs and merges; not usable for 64-bit edge
keys with items without its own tuning), the serial stable radix sort `sort_by_key` (kept as the small-input path), the union-find of `classify`
and `assemble` (serial, arbitrary roots). **Literature** (papers only, no code read; [SOURCES.md](../SOURCES.md)): the blocked radix partition (Zagha and Blelloch 1991,
Satish et al. 2009, Polychroniou and Ross 2014), union by index with compare-and-swap (Anderson and Woll 1991, Jayanti and Tarjan 2016), prefix sums (Blelloch 1990).

**Why the results are the same bit for bit.** The stable sort has one result whatever the blocks. The union-find links the larger root under the smaller
one, so a class ends with its smallest slot as root whatever the order of the unions; the vertex ids are the classes in the order of their first slot, which is that
root, so the numbering is a prefix sum over the roots. The patches of the classification only use the root as a key. The groups of equal edges are cut by blocks of the
sorted list (a group belongs to the block it begins in), groups of more than two pieces are paired serially afterwards in the order of the sorted edges (they use exact
arithmetic and the BigInteger helpers), the counters are summed per block. The bulk builder takes the edge ids from the sorted groups (an edge is numbered by its first slot,
as `AppendTriangle` creates it), the edges at each vertex in the order of the inserts, and the reference counts, normals and time stamps of the appends
(`ExactBooleanParallelAssemblyTests` compares every one with the appended mesh, with block sizes down to 2 entries and under permuted loop orders).

**Result** (T6, 294 booleans, 4-vCPU container, `DOTNET_TieredPGO=0`, three interleaved rounds base / candidate, order reversed in round 2, sums of the faster of two runs per
boolean, ms; raw rows `perf/assembly_parallel_2026-11-16/`):

| phase, 4 threads | master (3 rounds) | mean | candidate (3 rounds) | mean | change |
|---|---|---|---|---|---|
| classification | 1377 / 1349 / 1310 | 1345 | 919 / 960 / 934 | 938 | **-30 %** |
| assembly without simplify | 2280 / 2203 / 2221 | 2235 | 1351 / 1401 / 1367 | 1373 | **-39 %** |
| simplify (unchanged) | 917 / 977 / 933 | 942 | 914 / 949 / 959 | 941 | 0 % |
| rounding (unchanged) | 4373 / 4316 / 4271 | 4320 | 4313 / 4374 / 4299 | 4329 | 0 % |
| **whole boolean** | 19 752 / 19 362 / 19 133 | 19 416 | 17 807 / 18 386 / 18 059 | 18 084 | **-6.9 %** |
| GC pauses (sum) | 310 / 285 / 318 | 304 | 370 / 370 / 346 | 362 | +58 ms |

At 1 thread (one run each): 35 712 → 35 561 ms (-0.4 %; classification 1481 → 1502, assembly 2379 → 2029); at 2 threads: 23 955 → 23 285 ms (-2.8 %). The
speedup of the set from 1 to 4 threads is 1.84x before and 1.97x after. The classification at 1 thread is within the noise (±3 %), the other phases were not slower in any round.
The larger share of the gain is the bulk builder (A.9 and A.7: 1191 ms → 676 ms at 4 threads) and the edge groups of both phases (A.3 441 → 280, C.2 525 → 333); the pairing and the patch joins halve (382 → 191, 421 → 229).
What stays serial in the assembly at 4 threads: `ExactPlanarSimplify` 941 ms (lever 3), the builder's serial counting sort of the edges at each vertex, the gather (80), and in the classification the
open prep (118), the decisions (92), the first pieces (37).

**The assembly +5.5 % after the parallel tree build (#956).** Checked with the sub-phase timers, candidate build, threshold 8192 against 262 144 (one run each, 4 threads):
no assembly step differs beyond the noise (vertices 676 / 633, pairing 191 / 183, edges 280 / 278, simplify 1066 / 1049); the assembly total is 1373 against 1317 ms (4 %), all of it in the
step that allocates the most (the result's arrays), which fits memory and GC pressure (GC pauses 362 against 304 ms in the sums above) rather than a slower loop. The per-subtree node arrays are
dead before the assembly starts (`arr.Trees = null`). Not pursued further: below the noise of one run.

**The owner's machine** (i7-8700, 6 cores / 12 threads, T6 set, `thread_scaling.ps1`, net10.0, 3 rounds A B A B, two processes per side and thread count; measured by the owner's session on head bf34eff, data on branch `runs/exact-numbers`, `results/ab-pr962-vs-master-2026-11-16/`):

| threads | master 414c6b5f | #962 bf34eff7 | change |
|---|---|---|---|
| 1 | 43.8 s | 42.2 s | -3.7 % |
| 4 | 24.4 s | 22.9 s | -6.2 % |
| 12 | 17.0 s | 15.4 s | -9.6 % |

Spread between runs: master about 1.5 %, the PR about 0.5 %. Phases at 12 threads: classification -37 %, assembly -34 %, intersections -1 %, rounding 0 %, the rest under 2 %. Result hashes: 294 of 294 equal across both sides, all thread counts and rounds.

**Not measured:** net48, cold processes (one per boolean), inputs above 100k triangles (the T6 set has none; the parallel paths start at 65 536 sorted entries, 22k pieces). The serial part of the vertex edge lists in the builder is a counting sort over about 3 entries per piece, a few ns each.

## 7. Two probes on the head of #962: the discarded pairs of the pair stage, and the rounding sub-phases (2026-11-16)

Measurement only, no library change. Base: the head of PR #962 (`claude/parallel-assembly`, 3b52946, parallel assembly), `SafeRounding`, the T6 set (98 pairs, 294 booleans), warm,
`profile --repeat 2` (best of two per boolean, sums over the 294), net10.0, 4 vCPU container (1 and 4 threads: `taskset` and `DOTNET_PROCESSOR_COUNT`). Two patches, each applies alone and
both together to that head (`git apply`, checked; `rounding_probe.patch` was regenerated on master ff34857, which changed `MeshRoundingRepair.cs` after the measurement: the timers are the same, the numbers below were measured with the patch on 3b52946): [`perf/pair_probe.patch`](../../benchmarks/BooleanComparison/perf/pair_probe.patch) (probe A) and
[`perf/rounding_probe.patch`](../../benchmarks/BooleanComparison/perf/rounding_probe.patch) (probe B). Reports: `perf/pair_probe_report.py`, `perf/rounding_probe_report.py`; raw rows (gzip, one line per boolean,
cases numbered c00-c97 in the order of the manifest, no meshes): [`perf/probe_2026-11-16/`](../../benchmarks/BooleanComparison/perf/probe_2026-11-16/).
**Gate: the `profile` hash of all 294 booleans is the same in all 23 runs (base, probe A in modes 1-3, probe B, the filter experiment; 1 and 4 threads), `scaling_report.py --hashes`: drift 0.**
All 12-thread values below are Amdahl extrapolations from the 1 and 4 thread runs (the container has 4 cores), not measurements; the owner can rerun the patches at 12 threads (below).

Control (no patch), mean of two runs: whole boolean 34.8 s at 1 thread, 17.0 s at 4 (2.05×); `intersections.pairs` 11.9 s / 3.8 s (3.1×; 34 % and 22 % of the boolean); `rounding` 7.2 s / 3.7 s (21 % / 22 %).

### 7.1 Probe A: work in `intersections.pairs`

**What it counts** (G3_PAIR_PROBE=1: counters and a timestamp per self-join task, no measurable cost: 12 062 against 12 041 and 11 702 ms in the pairs phase of the control; =2: also a timestamp per
pair, +20 % in the phase; =3: also the filter experiment of 7.1.3; G3_PROBE_SERIAL=1: the 1-thread runs of the probe use `gParallel.SerialOnThisThread`, so that the per-thread intervals are CPU time. Without it a second pool
thread time-slices the one core and every interval counts the other's time: the first 1-thread runs of mode 3 summed to twice the wall time and are not used). The shares of the loops are from
mode 2 (timestamps inflate the absolute values), the unbiased absolute times from mode 1.

#### 7.1.1 Pairs and time

All 294 booleans, per operand self-join (`self_pairs`: box pairs of one operand, `touch_only`), pairs of the two operands (`candidates`: box hits, `separated`), then `intersect` on what is left:

| stage | pairs | outcome | CPU time (1 thread, serial) |
|---|---|---|---|
| self-join (`touch_only`) | 65.9 M box pairs (per boolean: median 102 k, p90 530 k, max 2.8 M) | **100 % rejected (kept: 0 in all 294 booleans: T6 operands are clean)** | tasks 10.6 s = traversal 2.2 s + pair tests 8.4 s |
| A against B (`candidates`) | 15.2 M box hits (median 9.2 k per boolean) | 13.0 M `separated` (86 %), 2.17 M go on | 1.2 s (mode 2: queries 0.48 s, `separated` tests and bookkeeping 1.0 s, 1.6 s in all) |
| `intersect` | 2.17 M | 1.94 M apart by the prefilter (0.13 µs each), 0.24 M meeting pairs (1.7 µs each) | 0.66 s (phase 0.54-0.65 s) |

So of the 81.1 M pairs the pair loops process, 80.9 M (99.7 %) end without a crossing; the 0.24 M meeting pairs are the useful ones. Time on the discarded pairs: all of the self-join (10.6 s of
12.1 s in the pairs phase = 88 %) and the 13.0 M separated hits: **about 12.0 s of the 12.6 s of the pairs and intersect phases (95 %), 34 % of the 1-thread boolean**; the useful part is about 0.6 s.

The self-join by the cheapest test that decides each pair (`touch_only`; shared = corners the two triangles share; mode 2, 1 thread, shares of the loop time):

| shared | decided by | pairs | share of box pairs | µs per pair | share of loop time |
|---|---|---|---|---|---|
| 0 | quick1: all corners of U on one side of T's plane (1-3 `Orient3d`) | 24.05 M | 36.5 % | 0.08 | 18.0 % |
| 0 | quick2: the same for T against U's plane | 2.75 M | 4.2 % | 0.14 | 3.7 % |
| 0 | general: `IntersectsBeyondShared` = `Intersects` (the exact test) says no | 4.70 M | 7.1 % | 0.55 | **25.2 %** |
| 1 | quick1 / quick2 | 16.68 M / 4.18 M | 25.3 % / 6.3 % | 0.09 / 0.15 | 14.4 % / 6.0 % |
| 1 | coplanar neighbours: `IntersectsBeyondSharedCoplanar` says no | 4.16 M | 6.3 % | 0.45 | **18.3 %** |
| 1 | general (exact test) | 2.16 M | 3.3 % | 0.34 | 7.3 % |
| 2 | quick1 (the shared edge: one `Orient3d`) / coplanar | 6.44 M / 0.80 M | 9.8 % / 1.2 % | 0.09 / 0.22 | 5.4 % / 1.7 % |

82 % of the pairs (quick1 alone 71.6 %) are decided by the quick tests, which take 47.5 % of the loop time at 0.08-0.15 µs each, about the cost of the `Orient3d` floating-point stage plus loading the two triangles. The
**expensive 11.8 M pairs (18 % of the box pairs) are those that reach the exact test, general and coplanar: 52 % of the loop time (5.4 s of 10.2 s in mode 2, about 4.4 s of the 8.4 s of mode 1), 0.34-0.55 µs each.**

Scaling of the self-join (mode 2 at 4 threads against 1): traversal 2.16 → 3.0 s and tests 10.2 → 13.8 s of thread time, in sum 12.4 → 16.8 s for the same work, while the wall time of the phase falls 14.3 → 4.5 s. At 4 threads the threads
are busy for the whole phase (thread time 16.8 + 2.0 s over 4 × 4.5 s): the 3.1× of the self-join (lever 7) is **not load imbalance but a slowdown per operation of 1.25-1.4× at 4 threads in every class**
(quick1 0.08 → 0.10 µs, exact tests 0.55 → 0.75 µs, traversal 1.39×). Cache or memory contention, or vCPUs that are SMT siblings, cannot be told apart here; more tasks would not help (consistent with the 2048-task experiment above).

#### 7.1.2 Which test could decide earlier: an existing filter

Building blocks searched first ("Look before you build"): `TriangleTriangleFilter.BeyondShared` (`intersection/TriangleTriangleFilter.cs`) decides a pair by an exact `Orient2d` projection (edge separates) or by the axis plane, and answers `Undecided` otherwise;
its contract is that it never changes a decision of the exact test (`TriangleTriangleFilterTests`, also on coplanar slivers). `MeshRoundingRepair` (line 885) and the alpha-wrap passes use it; **`touch_only` calls the exact
test directly.** The literature on adjacent-feature culling (Volino and Magnenat-Thalmann 1994, normal cone and contour; Schvartzman, Pérez, Otaduy 2010, star contours; Curtis, Tamstorf, Manocha 2008) is
described in [SELF_INTERSECTION_PATCH_CERTIFICATE.md](../design/SELF_INTERSECTION_PATCH_CERTIFICATE.md): the patch certificate was built for the Reducer and stopped as slower there; it replaces pair tests
per patch, but the box pairs of the tree traversal are produced all the same (here 2.2 s of the 12.1 s).

#### 7.1.3 Experiment: the filter first (mode 3, and G3_PAIR_FILTER=1)

Mode 3 runs the filter, after the exact test and timed apart, on every pair whose rejection took the exact test (general and coplanar; 11.8 M pairs). It says Separate on 96 % of them (11.56 M), Undecided on 4 % (0.26 M) and **never Intersecting** (a contradiction would mean a defect):

| shared | filter says | pairs | filter | exact test it replaces |
|---|---|---|---|---|
| 0 | separate / undecided | 4.48 M / 0.22 M | 0.45 s / 0.03 s | 2.62 s / 0.07 s |
| 1 | separate / undecided | 6.28 M / 0.04 M | 0.59 s / 0.01 s | 2.64 s / 0.02 s |
| 2 | separate | 0.80 M | 0.07 s | 0.18 s |

Estimate from it: the exact tests of the decided pairs (5.4 s, mode 3) fall to the filter's 1.1 s, about −4.3 s thread time at 1 thread (timestamp-inflated). The real switch (G3_PAIR_FILTER=1: `touch_only` asks `BeyondShared` before the exact test, falls back to
the exact test on Undecided, nothing else changes), **A/B, the same binary, alternated, two rounds, no probe active**, T6, 294 booleans:

| threads | `intersections.pairs` control (2 runs) | filter first (2 runs) | change of the phase | whole boolean (mean) | hashes |
|---|---|---|---|---|---|
| 1 | 12 041, 11 702 | 9 737, 9 735 | **−18.0 %** (−2.1 s) | 34 811 → 33 652 ms (−3.3 %; noise ±3 %) | equal, 294 × 4 runs |
| 4 | 3 763, 3 849 | 3 073, 3 134 | **−18.5 %** (−0.70 s) | 16 962 → 16 076 ms (−5.2 %; noise ±3 %) | equal, 294 × 4 runs |

Mode 1 with the switch: test loop 8.44 → 6.27 s, tasks 10.6 → 8.5 s, phase 12.06 → 9.94 s (−17.6 %). What is left in the self-join after it (mode 2, 1 thread): the quick tests 82 % of the pairs, 4.9 s of 8.1 s; the filter's own decisions 17.5 % of the pairs, 3.0 s.

#### 7.1.4 Bounds and estimate

| what | 1 thread | 4 threads | 12 threads (extrapolated) | risk | effort |
|---|---|---|---|---|---|
| **upper bound**: every discarded pair free (self-join and `separated` hits cost nothing) | −12.0 s = −34 % of the boolean | ≈ −3.5 s = −21 % | pairs phase 2.0 s = −15 % | needs a certificate that costs less than the pair tests it replaces: the patch certificate did not (Reducer); not a plan | – |
| **filter first in `touch_only`** (measured) | −2.1 s = **−7.1 %** | −0.70 s = **−4.1 %** | −0.36 s = −2.8 % | **low**: the answer is the exact test's by the filter's contract (no new bound, no new predicate); measured equal on 294 booleans and 11.8 M pairs; `touch_only` is in the exact file `ExactMeshArrangement.cs`: its review and the mutation shard of the file apply | S |
| quick tests from a per-triangle plane (cached normal and error bound as a filter in front of `Orient3d`) | ≤ −1 s (half of the 4.9 s of quick tests; estimate, not measured) | ≤ −0.3 s | ≤ −0.15 s | medium: a new error bound (exact code, bound review) for 0.08 µs → maybe 0.05 µs per pair, and the loads of two `InputTriangle` objects may be the floor | M |
| fewer box pairs from the traversal (2.2 s at 1 thread, 18 % of the phase) | – | – | – | not a cheap test: needs a different pair generation | L |

### 7.2 Probe B: the rounding sub-phases

`MeshRoundingRepair.Apply` with `Stopwatch` scopes (a `long[]` of ticks and calls per step, no allocation; the main thread only; steps called from inside an outer step such as `repair_passes` are booked apart,
so that nothing is counted twice), copied into `PhaseMilliseconds` as `rounding.<Step>_us` and `_n` like the assembly's timers. Probe cost: not measurable (rounding 7.3 s / 3.7 s without it, 7.3 s / 3.5 s with it). Exclusive times,
sums over the 294 booleans, two runs, per boolean the faster; the speed-up is the 1-thread time over the 4-thread time; p = parallel fraction by Amdahl's law, `extrapolated 12 thr` from it:

| step of `Apply` | calls | 1 thr ms | 4 thr ms | speed-up | share of Apply at 1 / 4 thr | p | 12 thr ms (extrap.) |
|---|---|---|---|---|---|---|---|
| first full pair search (`intersecting_pairs_all`; parallel over triangles) | 270 | 3 567 | 1 397 | 2.55 | 48 % / 39 % | 0.81 | 915 |
| `rebuild_tree` (the only one per boolean; serial below 262 144 boxes) | 270 | 2 129 | 1 041 | 2.05 | 29 % / 29 % | 0.68 | 799 |
| `atPosition` / `canonId` (position dictionary) | 270 | 251 | 222 | 1.13 | 3.4 % / 7.1 % | 0.15 | 215 |
| `component_scales` (union-find) | 270 | 235 | 211 | 1.12 | 3.2 % / 5.8 % | 0.14 | 205 |
| `degenerate_triangles` | 270 | 219 | 123 | 1.79 | 3.0 % / 3.4 % | 0.59 | 101 |
| anchors (first scan of the triangles) | 294 | 208 | 110 | 1.89 | 2.8 % / 3.0 % | 0.63 | 88 |
| `remove_tiny_components` (without its nested calls) | 40 | 167 | 124 | 1.36 | 2.3 % / 3.4 % | 0.35 | 114 |
| finite scan, range check | 270 | 158 + 126 | 59 + 59 | 2.7 / 2.1 | 3.8 % / 3.3 % | 0.84 / 0.71 | 37 + 44 |
| input boundary edges (`BoundaryEdgeIndices` loop) | 294 | 107 | 106 | 1.00 | 1.4 % / 2.9 % | 0 | 106 |
| `exactAtStart` | 270 | 98 | 80 | 1.23 | 1.3 % / 2.2 % | 0.25 | 76 |
| **edit loop** `repair_passes` (without its nested `degenerate_triangles`, 51 ms / 29 ms) | 270 | **33** | **31** | 1.07 | **0.4 % / 0.9 %** | – | – |
| `false_contacts` (540 calls), untimed rest of `Apply` | | 29 + 4 | 24 + 3 | | 0.4 % / 0.7 % | | |
| **`Apply` total** | 294 | **7 382** | **3 618** | **2.04** | 100 % | | 2 781 (2.65×) |
| `count_contacts` after `Apply` (in `ExactMeshBoolean`) | 270 | 163 | 144 | 1.13 | (outside `Apply`) | | |

The serial part (steps below 1.25×: `atPosition`, `component_scales`, boundary, `exactAtStart`, `false_contacts`, the edit loop, the untimed rest) is **0.76 s of 7.38 s at 1 thread (10 %) and 0.68 s of 3.62 s at 4 threads (19 %)**; with `count_contacts`
0.82 s = 5.1 % of the 4-thread boolean (16.1 s in the probe run), and it hardly shrinks with more threads (extrapolation: 0.80 s of about 12.1 s = 6.6 % of the boolean at 12 threads). The edit loop is 0.4 % again (as in section 2): there is nothing in it.
The pair search and the tree are 77 % of `Apply` at 1 thread; both scale only 2-2.5× (the pair search for the reason of 7.1.1 most probably, the tree because of its serial sort and build).

Bounds (a step becoming linear: time = 1-thread time / n):

| lever | at 4 threads | at 12 threads (extrapolated) | effort / risk |
|---|---|---|---|
| tree build parallel below 262 144 boxes (the old lever 1; its rounding part) | −0.51 s = −3.2 % of the boolean | −0.62 s = −5.2 % | S, measured earlier (a constant) |
| serial setup steps parallel (`atPosition` by sort, `component_scales` by a parallel union-find, boundary loop, `exactAtStart`, `count_contacts`; 0.82 s at 4 threads) | −0.59 s = −3.7 % (realistic about half) | −0.72 s = −6.0 % (realistic about half) | S-M, not exact code, same components required |
| pair search balance | −0.51 s = −3.1 % | −0.62 s = −5.1 % | no cheap lever (7.1.1) |
| edit loop parallel | −0.03 s | – | do not build |

### 7.3 Ranked recommendation

1. **Filter first in `touch_only`** (probe A): −2.1 s at 1 thread (−7.1 %), −0.70 s at 4 (−4.1 %), about −2.8 % at 12 (extrapolated), bit-identical, no new exact code, effort S. It is the only lever of the list that is measured, never slower on this set, and removes work on 18 % of the pairs.
   Before it is merged: tests of `touch_only` with the filter against the exact test on a procedural corpus (coplanar neighbours, slivers, shared edge with folded flat triangles), the exact-file review of `ExactMeshArrangement.cs`, and the same A/B on the net48 runtime.
2. **Parallel tree build at ≈ 4096** (probe B: the rounding's `rebuild_tree` is 29 % of `Apply`, 2.05× at 4 threads): a constant, measured earlier at −5 to −7 % for all trees; its bound in the rounding alone is −3.2 % at 4 and −5.2 % at 12 threads.
3. **The rounding's serial setup** (`atPosition`, `component_scales`, boundary, `exactAtStart`, `count_contacts`): 0.82 s, 5.1 % of the boolean at 4 threads and about 6.6 % at 12; realistic gain half of that, at low risk (not exact code). It grows in share with the thread count and is the reason the 12-thread machine will show a lower speed-up than the 4-core one.
4. Quick tests from a cached plane (7.1.4): ≤ 1 s at 1 thread, needs a new error bound: only after 1 and 2, and only if a micro benchmark shows the loads are not the floor.
5. Not worth building: parallel edit loop (0.4 %), more tasks for the self-join (the per-operation slowdown, not imbalance, limits it), a patch certificate for the neighbour pairs (the traversal still makes the box pairs; stopped once before).

Open for the owner's 12-thread run: whether the 1.25-1.4× slowdown per operation at 4 threads is the container (shared vCPUs) or the code (then it will show as the same factor, or a larger one, at 12 threads with SMT); `G3_PAIR_PROBE=1` and the filter A/B are cheap to run there.

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
# the probes of section 7 (patch applied to the head of #962, 3b52946; G3_PROBE_SERIAL=1 only for the 1-thread probe runs)
git apply perf/pair_probe.patch; git apply perf/rounding_probe.patch      # then build BooleanComparison
G3_PAIR_PROBE=1 G3_PROBE_SERIAL=1 DOTNET_PROCESSOR_COUNT=1 taskset -c 0 dotnet frozen/BooleanComparison.dll profile --repeat 2 --safe-rounding cases-t6.txt > A1_t1.jsonl   # modes 1, 2, 3
G3_PAIR_PROBE=2 DOTNET_PROCESSOR_COUNT=4 taskset -c 0-3 dotnet frozen/BooleanComparison.dll profile --repeat 2 --safe-rounding cases-t6.txt > A2_t4.jsonl
G3_PAIR_FILTER=1 DOTNET_PROCESSOR_COUNT=4 taskset -c 0-3 dotnet frozen/BooleanComparison.dll profile --repeat 2 --safe-rounding cases-t6.txt > filt_t4.jsonl   # against a run without it, alternated
python3 benchmarks/BooleanComparison/perf/pair_probe_report.py classes 1T=A2_t1.jsonl 4T=A2_t4.jsonl; ... perboolean A2_t1.jsonl; ... filter 1T=A3_t1.jsonl; ... ab ctl1.jsonl,ctl2.jsonl filt1.jsonl,filt2.jsonl
python3 benchmarks/BooleanComparison/perf/rounding_probe_report.py table 1T=B1_t1.jsonl,B2_t1.jsonl 4T=B1_t4.jsonl,B2_t4.jsonl
python3 benchmarks/BooleanComparison/perf/scaling_report.py --hashes 1=base_t1.jsonl 4=<every probe run>      # drift 0
# on the owner's 12-thread machine: the same commands with the thread count 12 (no taskset, or an affinity mask of 12 processors)
```

Raw rows of the probes of section 7: [`perf/probe_2026-11-16/`](../../benchmarks/BooleanComparison/perf/probe_2026-11-16/) (`base_t*`: no patch; `A1`-`A3`, `A*f`: probe A modes 1-3 and with the filter switch; `B*`: probe B; `ctl*` / `filt*`: the A/B of 6.1.3).
Raw rows (gzip): [`perf/thread_scaling_2026-11-15/`](../../benchmarks/BooleanComparison/perf/thread_scaling_2026-11-15/) (`base_t{1,2,4}`: master; `probe2_t{1,4}` and `probe_t2`: the probe build;
`exp_*`: the experiments). The three named cases cold: `BooleanComparison profile --repeat 1 --safe-rounding --ops <op> <case dir>` with the probe build, best of 3 processes.
