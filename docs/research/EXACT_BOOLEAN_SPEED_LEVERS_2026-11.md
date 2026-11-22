# Speed levers for the exact boolean and the rounding that keep every guarantee (2026-11-22)

Research spike (owner go 2026-11-22 for a study). **Measurement only: no library code is changed, and nothing here is
scheduled.** The boolean speed work is stopped (owner 2026-11-16) and Exact Boolean 4.0 stages 1b / 1c are on hold; every
implementation below needs the owner's go.

The question: what could make `ExactMeshBoolean` / `ExactMeshArrangement` and the rounding (`SafeRounding`,
`MeshRoundingRepair`) much faster **without weakening a guarantee**: exact on arbitrary double inputs, a clean result
(`NotCleanReason == None`), bit-for-bit determinism, and raw soups with self-intersections handled by default.

The probes are a benchmark command, `BooleanComparison seam-levers`
([SeamLevers.cs](../../benchmarks/BooleanComparison/SeamLevers.cs)), and its report script; raw rows in
[perf/seam_levers_2026-11-22/](../../benchmarks/BooleanComparison/perf/seam_levers_2026-11-22/). Commands in
[section 9](#9-reproduce).

## 1. Result in short

**The finding.** The boolean's cost is tied to the size of the operands, not to the size of the intersection. On two
tubes of 634k triangles in all that cross in a seam touching 806 of them, a union takes 2.47 s at 1 thread and 1.37 s at
4; the same seam between short tubes (40k triangles) takes 0.25 s and 0.09 s. **90-93 % of the time is work on triangles
that the boolean does not change**: 3.7 µs per far input triangle at 1 thread, 2.2 µs at 4. The largest part of it is
not the arrangement but the rounding (1.3 of the 3.7 µs at 1 thread, 1.0 of the 2.2 µs at 4), which builds its search and
bookkeeping structures over every result triangle although only the new ones can have a defect. On T6 (heavily
overlapping operands, the least favourable set for this) the local set is still only 11.7 % of the input triangles
(median per pair 6.8 %).

| # | lever | measured upper bound, seam inputs (union, 634k triangles) | measured upper bound, T6 (294 booleans) | guarantees | effort / risk | verdict |
|---|---|---|---|---|---|---|
| 1a | Rounding proportional to the new triangles: no tree of all result triangles, setup scans only where needed | −32 % at 1 thread, −45 % at 4 (all of the rounding's far work); the tree alone, by prototype: **at most −11 % at 1 thread** | the tree alone, by prototype: **at most −3.9 % at 1 thread** (−2.8 % on the booleans without an edit); all of it: about −14 to −19 % (estimate, section 4.2) | kept: the rule of ROUNDING_EXACT_PAIRS.md already skips the pairs of unchanged triangles; only the structure around it changes. Bit-identical | M, tier M (`MeshRoundingRepair` is not an exact file); risk low for the tree, medium for the scans | **build first**, in two steps |
| 1b | Pass-through of untouched regions in classification and assembly | −16 % at 1 thread, −14 % at 4 | about −7 % (estimate) | kept, bit-identical ids required | L, exact files, ids must be reproduced | after 1a, only if a seam workload is a target |
| 2 | Skip the operand self-join for operands certified clean (the library's own clean results, by a content stamp) | −20 % at 1 thread, −9 % at 4 | **0 on raw inputs**; a T6-like step with certified operands: −24 % at 1 thread, −15 % at 4 | kept only with a verified certificate (never a caller's flag); the stamp costs 1-2 % of the self-join it replaces | plan #1255, tier L | parked item, now with numbers; it unlocks nothing else on raw inputs |
| ceiling | All far work free (more than 1a + 1b + 2: setup and the trees of the far part stay even then) | −90 % at 1 thread, −93 % at 4 | – | as above | – | the ceiling, not a plan |
| 3 | The serial tail: `ExactPlanarSimplify`, result vertices, `setup.points`, input checks, classification's serial steps, contact count | not split (the rounding dominates there, lever 1a) | serial share 16.5 % of the 4-thread time: **at most −12 %** at 4 threads if all of it scaled, realistically half; contact count by prototype −0.4 % | kept, bit-identical | M each, partly exact files | worth it for 12 threads; one small PR at a time |
| 3x | (found on the way) the rounding's pair search increments one shared counter per skipped triangle from all threads | −4 % at 4 threads (72-114 ms → 11-47 ms of 1.37 s; a scratch switch, ten runs of one input) | not measured | kept, a count only | S | **build**: a few lines |
| 4 | Wide BVH, packetised double prefilters | traversal is 9 % of the union at 1 thread | traversal is at most 8.3 % of the unions at 1 thread: a tree twice as fast gives **≤ 4 %** (≤ 6 % counting the traversal three times per pair, as the script does); the SIMD filter spike measured 2 % | kept if scalar-identical | L, second code path per platform | **drop** (below the 10 % bar; 1a removes one of the three trees outright) |
| 5 | OPTION ONLY, not proposed: a separate fast kernel with a weaker guarantee | – | the repository's own runs: Manifold is 3.1-3.7x faster (geomean) and leaves 34 % of the operations (81 % on the size class) with degenerate triangles; dropping self-join and rounding from our pipeline gives at most 1.8x at 4 threads | **not kept** (no exactness on doubles, no clean result, no raw-soup handling) | a second product | owner decision whether such a product exists at all; this note does not recommend it |

**Leaning.** Build 3x and 1a (first the tree, then the scans): they keep every guarantee by an argument that is already
in the repository, touch no exact file, change no result bit, and attack the largest phase at every thread count (the
rounding is 27 % of T6 at 1 thread and 29 % at 4, and 35-47 % of a seam union). Lever 2 is the right next step only for
callers that chain booleans; lever 1b only with a seam workload as a target. Levers 4 and 5 are not worth building under
the stated guarantees.

## 2. Setup and strata

- **Machine:** the cloud container, 4 vCPU (no SMT), .NET 10.0.115, `DOTNET_TieredPGO=0` (#825). One thread:
  `taskset -c 0` and `DOTNET_PROCESSOR_COUNT=1`. Run-to-run spread: the two T6 runs at 4 threads of this study are 23 399 and 23 384 ms (0.06 % apart, minutes apart in one
  session); the earlier studies on this kind of container found ±3 % between runs of a set and 20-30 % between sessions
  (EXACT_BOOLEAN_PERFORMANCE.md), so nothing below 3 % is read as a gain here. Single large cases vary more: the union of
  the longest tubes took 2345-2778 ms at 1 thread over the six timings in the raw rows (best per run of three: 2467 and 2345).
- **Library:** master ed93ef26 (2026-11-22), `SafeRounding` on (the default), warm process, the fastest run per boolean.
- **Stratum S (seam), new:** two procedural closed tubes with a wobbling radius (no coplanar faces but the flat end caps, far from the seam), 96 segments around,
  A along x, B thinner, rotated by 73° about (0.3, 0.5, 0.8) and shifted, so that it passes through A near the origin.
  The half length (3, 12, 48) sets the size (40k, 159k, 634k input triangles in all); the seam is the same geometry for every
  length (the rings fall slightly differently): 804-806 input triangles are split, 1364-1383 have a box that meets a box of the other operand. **The time above
  the shortest pair is the work on triangles far from the seam** ("far work"). All 18 results (1 and 4 threads) are
  rounding clean; the 9 results at 4 threads have `NotCleanReason == None` (`--check`), and the 1-thread results have the
  same hashes. The longer unions and differences are not free of edits: the rounding makes 2-5 edits on each (0 on the
  shortest pair and on the intersections).
- **Stratum T6:** the 98 Thingi10K pairs of 1k-100k triangles, 294 booleans, fetched at run time into a scratch
  directory (nothing committed). Normal single booleans of raw, clean inputs, heavily overlapping (B is a rotated,
  shifted second model in the same unit box).
- **Shares** are wall time per phase, including the allocation and GC cost incurred inside the phase.
- **Not measured here:** 12 threads (the container has 4), net48, cold processes, ABC chains, the tail above 20 s.

## 3. Where the time goes today

### 3.1 T6, fresh (294 booleans, sums in ms)

`seam_levers_report.py phases`; 294 of 294 hashes equal at 1 and 4 threads, 294 of 294 rounding clean.

| phase | 1 thr | 4 thr | speed-up | share at 1 | share at 4 |
|---|---:|---:|---:|---:|---:|
| input | 356 | 310 | 1.15 | 0.9 % | 1.3 % |
| setup (points 681 / 735, triangles 366 / 275) | 1193 | 1132 | 1.05 | 2.9 % | 4.8 % |
| intersections.trees | 3143 | 1636 | 1.92 | 7.5 % | 7.0 % |
| intersections.pairs | 11 520 | 4004 | 2.88 | 27.6 % | 17.1 % |
| intersections, rest (cull, planes, intersect, gathering the pairs) | 2078 | 1584 | 1.31 | 5.0 % | 6.8 % |
| merge | 2662 | 1389 | 1.92 | 6.4 % | 5.9 % |
| triangulation | 2820 | 1174 | 2.40 | 6.8 % | 5.0 % |
| classification | 1870 | 1248 | 1.50 | 4.5 % | 5.3 % |
| assembly (of it simplify 1113 / 1138, vertices 1090 / 848) | 3598 | 2904 | 1.24 | 8.6 % | 12.4 % |
| rounding | 11 194 | 6747 | 1.66 | 26.8 % | 28.8 % |
| rest, outside the timers | 1274 | 1271 | 1.00 | 3.1 % | 5.4 % |
| **total** | **41 708** | **23 399** | **1.78** | | |

- **The "rest" is mostly not work.** `PhaseClock.Lap` stores whole milliseconds, so each of the eight top-level laps of a
  boolean loses half a millisecond on average: about 1 s over 294 booleans. The real untimed work is `count_contacts`
  (282 ms at 1 thread, section 6). The 12 % "untimed" of PLANE_BASED_KERNEL_STUDY section 1 has the same cause in part.
- **Against the earlier profiles:** the self-join is smaller (the `touch_only` filter, #1002), and the rounding is now as
  large as the pair search at 1 thread and the largest phase at 4 threads.
- Allocation 13.7 GB (48 MB per boolean), GC pauses 720 ms (1.7 %) at 1 thread and 553-625 ms (2.4-2.7 %) at 4.

### 3.2 Seam inputs (union; `seam_levers_report.py seam`)

| threads | half length | input triangles | ms | far work | setup | trees | pairs | classification | assembly | rounding |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 3 | 40 128 | 247 | – | 9 | 18 | 45 | 12 | 22 | 76 |
| 1 | 12 | 158 976 | 779 | 68 % | 32 | 80 | 172 | 49 | 77 | 258 |
| 1 | 48 | 634 176 | 2467 | 90 % | 163 | 287 | 538 | 186 | 241 | 868 |
| 4 | 3 | 40 128 | 92 | – | 5 | 7 | 15 | 7 | 12 | 28 |
| 4 | 12 | 158 976 | 333 | 72 % | 24 | 20 | 37 | 21 | 44 | 149 |
| 4 | 48 | 634 176 | 1370 | 93 % | 120 | 96 | 149 | 78 | 139 | 648 |

**Cost per far input triangle** (the longest pair against the shortest, µs):

| operation, threads | total | input | setup | trees | pairs | merge + triangulation | classification | assembly | rounding |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| union, 1 | 3.74 | 0.05 | 0.26 | 0.45 | 0.83 | 0.07 | 0.29 | 0.37 | **1.33** |
| union, 4 | 2.15 | 0.02 | 0.19 | 0.15 | 0.23 | 0.07 | 0.12 | 0.21 | **1.04** |
| difference, 1 | 2.21 | 0.03 | 0.25 | 0.45 | 0.38 | 0.06 | 0.11 | 0.19 | 0.69 |
| difference, 4 | 1.34 | 0.02 | 0.19 | 0.19 | 0.12 | 0.08 | 0.07 | 0.13 | 0.44 |
| intersection, 1 | 1.02 | 0.03 | 0.22 | 0.48 | 0.04 | 0.07 | 0.01 | 0.00 | 0.08 |
| intersection, 4 | 0.68 | 0.03 | 0.20 | 0.16 | 0.03 | 0.05 | 0.01 | 0.01 | 0.10 |

- The arrangement itself is already local where it can be: merge and triangulation hardly grow with the operands
  (untouched triangles are not triangulated), and the cull of T3a / T3b (#612) takes the far triangles of an
  Intersection out of the self-join and the output. An Intersection of the longest tubes still takes 0.75 s for a
  result of 9316 triangles: setup and trees of 634k input triangles (0.7 µs each).
- A union keeps the far triangles, and each one is paid for in every later phase. The rounding scales worst: 868 → 648 ms
  from 1 to 4 threads (1.34x).

## 4. Lever 1: work only near the seam

### 4.1 What is already local, and what a seam-local boolean must still do

Under the guarantees, without lever 2, the far triangles of a raw operand must still be read, checked (finite, closed,
oriented), put into a tree and joined with themselves: a raw soup may intersect itself anywhere. That is input, setup,
trees and the self-join: 1.6 of the 3.7 µs per far triangle of a union at 1 thread, 0.6 of the 2.2 µs at 4. **The rest,
2.0 µs at 1 thread and 1.4 µs at 4 (53 % and 64 % of the far work), is downstream of the arrangement and does not need
lever 2**: classification 0.29 / 0.12, assembly 0.37 / 0.21, rounding 1.33 / 1.04.

The local set is cheap to find with the two trees the arrangement builds anyway: 17 ms for the longest tubes, 130 ms for
all 98 T6 pairs (1 thread; `TriangleBoxTree.Any` per triangle).

### 4.2 Lever 1a: the rounding (the largest part)

Steps of `MeshRoundingRepair.Apply` on the union of the longest tubes (628 084 triangles when `Apply` starts, 628 078 in the result, 2015 new; a scratch
probe with a Stopwatch per step, the best of three runs at 1 thread, the range of three at 4;
`rounding_steps_tubes48_union.txt`):

| step | 1 thr ms | 4 thr ms | needed for the unchanged triangles? |
|---|---:|---:|---|
| `rebuild_tree` (boxes and tree of all triangles) | 375 | 156-205 | no: see the prototype below |
| `atPosition` / `canonId` (dictionary of all vertex positions) | 111 | 73-115 | only the positions near new vertices can be contacts the rounding made |
| rest of `Apply` (`false_contacts` twice, the removal of tiny components, the final state) | 109 | 121-165 | partly |
| `component_scales` (union-find, the diagonal of the largest component) | 55 | 55-64 | the scale is needed; it could come from the operands' components and the seam |
| `degenerate_triangles` | 37 | 14-17 | yes, a raw input may contain slivers; a parallel scan, cheap |
| finite and range scans | 35 | 12-18 | once per vertex, could be shared with the input check |
| anchors (three positions per triangle copied) | 32 | 12-18 | only for triangles an edit may touch |
| first pair search (queries from the 2015 new triangles) | 32 | 84-103 | this is the search itself; see lever 3x for the 4-thread time |
| `exactAtStart`, boundary edges | 34 | 25-64 | flags only |
| outside `Apply` (constructor, `IsThinOrTiny` of the inputs) | about 65 | – | – |

Everything but the 32 ms of the search is setup over all triangles.

**Why it stays correct.** The rule of [ROUNDING_EXACT_PAIRS.md](../design/ROUNDING_EXACT_PAIRS.md) (built, #446) already
proves that a pair of two result triangles whose corners are unmoved input points needs no test: the exact arrangement
has resolved every pair of input triangles, self-intersections of the operands included. Today's search uses it to skip
the *tests*, and queries the tree only from the new triangles; it still builds the tree of all triangles first.

**Prototype** (`seam-levers --local-rounding`, in the benchmark): a tree of the new triangles only, joined with itself,
and every other triangle's box passed through it once. Both structures enumerate the pairs of a new triangle with any
other triangle whose box overlaps, each pair once, so the candidate sets are equal by construction and the triangle tests,
and with them the result, are the same. The probe checks the **numbers** of pairs: equal on every boolean (9 of 9 tube
booleans, 294 of 294 on T6). Two limits of the probe: "new" is decided by position on the final result (a vertex that is
no input vertex position), while the library decides by vertex identity when `Apply` starts (a crossing that rounds onto
an input position is new there); on the one case compared, the union of the longest tubes, both give 2015 new triangles
(628 084 − 626 069 skipped). And it is not compared against `intersecting_pairs_all` itself: `CheckOnThisThread` would do
that in an implementation. Timed on one thread, without the tests (they are identical on both sides):

| set (1 thread) | booleans | rounding ms | result triangles | of them new | tree of all + queries | tree of the new + join + scan | saved | of the rounding | of the boolean |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| tubes, half 48, union | 1 | 868 | 628 078 | 2015 | 280 | 15 | 264 | 30 % | **11.3 %** |
| tubes, half 48, difference | 1 | 528 | 285 922 | 2004 | 118 | 7 | 110 | 21 % | 6.8 % |
| tubes, half 12, union | 1 | 236 | 152 882 | 2010 | 60 | 5 | 55 | 23 % | 6.7 % |
| T6, union | 98 | 6547 | 2 666 120 | 163 432 | 1424 | 423 | 1001 | 15 % | 4.7 % |
| T6, difference | 98 | 3856 | 1 810 276 | 157 405 | 959 | 297 | 662 | 17 % | 4.3 % |
| T6, intersection | 98 | 1690 | 256 636 | 151 835 | 328 | 212 | 117 | 7 % | 1.3 % |
| **T6, all** | 294 | 12 093 | 4 733 032 | 472 672 | 2711 | 932 | 1779 | 15 % | **3.9 %** |

(The T6 rows are from one run per boolean, 45.3 s in all against 41.7 s for the best of two in section 3.1: read the
ratios, not the absolute times.) At 4 threads the saving is smaller in share where the tree is built in parallel (from
8192 boxes); it was not measured there.

**These are upper bounds of step 1, not its expected gain.** The full tree also serves the queries of the edits, and a
design that simply builds it on the first edit gains nothing on a boolean with an edit: the seam unions and differences
of half 12 and 48 have 2-5 edits each (their saving would be 0), and on T6 the 40 booleans with edits hold 496 of the
1779 ms, which leaves **−2.8 %** (1283 of 45 343 ms). To reach the bounds, the few queries of an edit must be served
without the full tree, for example by the small tree plus one linear pass over the unchanged boxes per query (the probe's
pass over 626k boxes takes 14 ms *with* a tree query each), and the full tree built only once the edits pass a threshold,
as the dirty tree of #409 does at 256 changes. That design is not prototyped.

**Bounds.**
- Seam inputs: all of the rounding's far work is 1.33 of 3.74 µs per far triangle at 1 thread (36 % of the far work,
  −32 % of the longest union) and 1.04 of 2.15 µs at 4 (48 % of the far work, −45 % of the union).
- T6, estimate: the unchanged result triangles of T6 (4.26 M of 4.73 M) at the tubes' rate give 5.7 s
  of the 11.2 s rounding at 1 thread (−14 % of the set) and 4.4 s of the 6.7 s at 4 (−19 %). This transfers a per-triangle
  rate from one large result to many small ones and is not a measurement; the prototype's −3.9 % is.

**What an implementation would have to settle** (not built here):
- The full tree is also used by the edits (the queries of `improves`, the dirty tree, `false_contacts`): 215 edits in
  40 of the 294 T6 booleans, 2-5 on the longer tubes. See the paragraph on upper bounds above: a lazy tree alone gives
  −2.8 % on T6 and nothing on the seam unions.
- `component_scales` defines `maxLength` and the degenerate area; its value must be the same bit for bit.
- `atPosition` restricted to the neighbourhood of new vertices must give the same `Contacts`, `InitialFalseContacts`
  and canonical ids wherever they are read.
- An untouched tiny inside-out residue must still be removed (#1152).
- `remove_tiny_components` judges whole connected components (area, volume, touched): it needs the component sums over
  all triangles, or a local stand-in that gives the same decisions (noted by the snap rounding spike; section 8b).

### 4.3 Lever 1b: pass-through in classification and assembly

Union, per far triangle: classification 0.29 µs and assembly 0.37 µs at 1 thread (18 % of the far work), 0.12 and
0.21 µs at 4 (15 %). Today every untouched input triangle is a piece that goes through the edge groups, the union-find
of the patches, the pairing of corner slots and the vertex numbering, like a piece of a split triangle. A pass-through
would take the connectivity of an untouched region from its input mesh.

- Bound on T6 (estimate, the tubes' rates on 4.26 M far result triangles): 2.8 s of 41.7 s at 1 thread (−7 %).
- Risk: the result's vertex, triangle and edge ids are part of the bit-identical contract (the hash covers them), and
  the parallel assembly of #962 already reproduces them once; `ExactMeshBoolean.cs` is an exact file (tier L, mutation
  shards).
- Not prototyped: a prototype is most of the implementation.

### 4.4 Building blocks in the library

`TriangleBoxTree` (`Any`, `Query`, `SelfPairs`: the prototypes use nothing else), the cull of T3a / T3b
(`CullOutsideOf`, `Culled`: the same "far triangles take no part" idea for Intersection and Difference), `exactAtStart`
/ `exact_triangle` and the lemma of ROUNDING_EXACT_PAIRS.md, the dirty tree of the changed triangles (#409: a small tree
next to the large one already exists in `MeshRoundingRepair`), the blocks of 4096 for parallel scans, the bulk result
builder (`ExactMeshResultBuilder`).

## 5. Lever 2: skip the self-join of certified clean operands

The arrangement run with and without the operand self-join (`SelfIntersections`), on clean operands
(`seam_levers_report.py selfjoin`): the number of output pieces is the same in every case (98 of 98, 3 of 3; the probe compares counts, not the pieces), as the
self-join finds nothing.

| set | threads | arrangement with ms | without ms | self-join | of the arrangement | of the boolean |
|---|---:|---:|---:|---:|---:|---:|
| T6, 98 pairs (as a union: no cull) | 1 | 10 027 | 4362 | 5665 | 57 % | 30 % of the unions |
| T6, 98 pairs | 4 | 4688 | 2508 | 2180 | 46 % | 21 % of the unions |
| tubes, half 48 | 1 | 1062 | 565 | 497 | 47 % | 20 % |
| tubes, half 48 | 4 | 453 | 327 | 126 | 28 % | 9 % |

Over all three operations of T6 (the cull shrinks the self-join of Intersection and Difference; pairs phase minus the
A × B part of 462 / 163 ms): 10.1 s of 41.7 s at 1 thread (**−24 %**) and 3.5 s of 23.4 s at 4 (**−15 %**). This is the
upper bound for a step whose two operands are both certified. On a single boolean of raw inputs it is **0**: a
certificate for a raw operand costs the search it would save.

**The certificate.** Owner 2026-11-16: never a caller's flag; only the library's own clean results or a prepared operand
(plan #1255). A content stamp of an operand (every vertex's bits and every triangle, in id order) costs, for all 196 T6
operands, 48 ms as a 64-bit mix and 111 ms as SHA-256, against the 5665 ms of their self-joins: **0.8 % and 2.0 %**. So a
stamp taken when a boolean returns a clean result and checked when the mesh comes back as an operand is affordable, and
it does not depend on `DMesh3.Timestamp` (which a write to the public buffers bypasses). SHA-256 leaves no realistic
collision if the stamp also covers the vertex and triangle counts (the probe hashes the bare records, which is enough
for the timing but not injective); a 64-bit mix would be an accidental-collision argument only.

Open points for a design (plan #1255), not settled here:
- where the stamp lives. It must be something a caller cannot forge by setting a field (the plan's prepared-operand
  handle, or a table inside the library keyed by the mesh instance); otherwise it is a caller's flag in disguise;
- who pays for taking it: 1-2 % of a self-join on every clean result, also for callers who never chain, unless it is
  taken on request;
- that a clean result has no self-pair for the arrangement to find is an inference here (`NotCleanReason` runs an exact
  self-intersection search; contacts at duplicated vertices need a look), and the measurement above is on raw T6 inputs,
  not on boolean results. A test over the library's own results would have to show 0 self-pairs before anything is
  skipped.

**What it unlocks for lever 1.** With a certified operand the far triangles need no self-join (0.83 / 0.23 µs per far
triangle at 1 / 4 threads). The trees (0.45 / 0.15 µs) are still read by the A × B queries and the winding rays, and the
points and triangles of the far part by the output, so those stay unless 1b is built too. Lever 2 alone on seam inputs:
−20 % at 1 thread, −9 % at 4. It does not change the ranking of 1a, which needs no certificate.

## 6. Lever 3: the serial tail

What does not scale on T6 from 1 to 4 threads (4-thread ms, speed-up below 1.3):

| step | 4 thr ms | share of the 4-thread time | file |
|---|---:|---:|---|
| `ExactPlanarSimplify` | 1138 | 4.9 % | exact file |
| assembly: result vertices (`assembly.vertices_us`) | 848 | 3.6 % | `ExactMeshBoolean.cs` / `ExactMeshResultBuilder.cs` |
| `setup.points` | 735 | 3.1 % | `ExactMeshArrangement.cs` |
| input (finite, closed, oriented) | 310 | 1.3 % | `ExactMeshBoolean.cs` |
| classification: stacks, open prep, first pieces, decisions | 444 | 1.9 % | `ExactMeshBoolean.cs` |
| assembly: gather | 107 | 0.5 % | `ExactMeshBoolean.cs` |
| `count_contacts` (1-thread time; serial) | about 280 | 1.2 % | `ExactMeshBoolean.cs` |
| **sum** | **3862** | **16.5 %** | |

- **Bound:** if all of it scaled linearly, −12 % at 4 threads (three quarters of 16.5 %); realistically half. At 12
  threads the serial share is larger, so the gain is too; not measured here.
- **Rounding's serial setup** is the larger serial block on big results (section 4.2: at 4 threads the rounding of the
  seam union is 47 % of the boolean and scales 1.34x). Lever 1a removes that work instead of spreading it over threads.
- **Contact count, prototype:** the same count with an open-addressing table of vertex ids instead of
  `Dictionary<Vector3d, int>`: 282 → 109 ms over the 294 T6 booleans at 1 thread (**−0.4 %**), 44 → 18 ms on the seam
  union; the counts are equal on all 294 + 18 results, but every one of them is 0 (no result of the two strata has a contact), so
  this shows the timing only; the two counts are compared on a mesh with shared positions and −0.0 at every start of the
  probe (`ContactsSelfCheck`, 3 contacts). Faster on every result above 5000 vertices, equal within 0.1 ms below; small:
  worth a few lines, not a PR of its own.
- **Allocation:** a 1 GB gen0 budget (`DOTNET_GCgen0size=0x40000000`) as the upper bound of "no allocation pressure":
  T6 at 4 threads 23 384 → 22 807 ms (−2.5 %, nearly every phase 3-5 % faster: a small gain, below the 3 % this study
  reads as one, from one run per side). On the seam union the GC pauses are 92-158 ms of
  1.4-2.5 s (4-12 %, 1.15 GB allocated; gen2 collections of the large arrays). So allocation-free variants are a lever
  for large results only; they overlap with 1a (the rounding allocates 2.6 GB of the 13.7 GB on T6).

### 6.1 Lever 3x: a shared counter in the rounding's pair search

`intersecting_pairs_all` calls `Interlocked.Increment(ref skipped)` for every skipped exact triangle, from all threads.
On the seam union (626 069 skipped triangles) the search takes 27-76 ms at 1 thread and 72-114 ms at 4. With the
increment left out (a scratch switch, `skip_counter_tubes48_union.txt`): 11-47 ms at 4 threads, unchanged at 1. About
60 ms of 1370 ms (**−4 %** at 4 threads on this input; more with more threads). A count per block, summed in block
order, gives the same `SkippedExactQueries`. The evidence is ten runs per side of one input (mean difference 61 ms); it is not
measured on T6 (smaller results, fewer skipped triangles per boolean). Rank 1 below rests on its cost (a few lines), not
on the size of the gain.

## 7. Lever 4: wide trees and packetised filters

The tree work alone, on one thread (`seam_levers_report.py tree`; box pairs listed, no triangle test):

| set | tree build | self-join traversal | A in B queries | against the booleans |
|---|---:|---:|---:|---|
| T6, 98 pairs | 923 ms | 1399 ms (38.2 M box pairs) | 182 ms (5.1 M hits) | traversal 1581 ms = 8.3 % of the unions at 1 thread (18 943 ms; less for the culled operations; the script's line counts it three times per pair against all 294 booleans: 11.4 %, an overcount) |
| tubes, half 48 | 278 ms | 217 ms (4.1 M box pairs) | 10 ms | traversal 9 % of the union |

- **Bound:** a wide tree speeds up the traversal, not the build (the build is the Morton sort and would gain a collapse
  step). With the traversal twice as fast: at most −4 % of a T6 union at 1 thread (−6 % with the script's overcount), less at 4 threads (the pairs phase
  scales 2.9x) and for the other operations. The published gains for wide trees in collision detection are "up to
  50 %" on the proximity queries (section 8).
- **Packetised double filters:** the SIMD `Orient3d` spike (no FMA, bit-identical per lane) measured −11 % of the pair
  search and about 2 % of the total, within the noise (BooleanComparison README, "SIMD spike").
- **Verdict:** below the 10 % bar on both strata, with a second code path per platform (netstandard2.0 has no
  `Vector256`) and scalar-identity tests to keep. Dropped. Lever 1a removes the rounding's tree of all triangles
  outright, which a wide tree would only speed up.
- The tree *build* is the larger part (0.45 µs per far triangle at 1 thread, 7.5 % of T6): a cheaper build would matter
  more than a wider traversal. Not measured.

## 8. Lever 5 (option only, not proposed) and prior art

**A kernel with a weaker guarantee.** Measured in the repository's own runs (GitHub runners, 4 cores; BooleanComparison
README, "Results of the two full runs", 2026-11-18 / 19):

| set | ours, geomean ms | Manifold, geomean ms | ratio | ours clean or correctly empty | Manifold clean or correctly empty |
|---|---:|---:|---:|---:|---:|
| thingi-pairs, 1000 pairs, 3000 operations | 116 | 31-32 | 3.7x | 3000 of 3000 | 1985 (1015 with degenerate triangles) |
| size class, 100 pairs, 300 operations | 1396 | 444 | 3.1x | 300 of 300 | 58 (242 with degenerate triangles) |

- What the guarantees cost inside our own pipeline: the self-join (15 % at 4 threads on T6) and the rounding (29 %)
  together are 44 %: without them at most 1.8x at 4 threads. The rest of the gap to Manifold is architecture (floating
  point throughout, no exact arithmetic, no vertex identity work), not a setting.
- A quantised fixed-grid kernel: 0 of the 294 T6 booleans are on a short grid, and the on-grid result still needs the
  full rounding (PLANE_BASED_KERNEL_STUDY, no-go, owner 2026-11-19). Nothing new to add.
- This note does not recommend either. They would be a second product with a different contract; whether that exists is
  the owner's decision.

**How others solve it** (papers and documentation only; no code read, none of the banned sources):

| work | what it does about the levers here |
|---|---|
| Cherchi, Pellacini, Attene, Livesu, *Interactive and Robust Mesh Booleans*, ACM TOG 41(6), 2022 (arXiv 2205.14151; abstract and partial text) | inputs must be manifold, watertight and free of self-intersections (no self-join); one ray per patch; output snapped to floating point without a guarantee. Interactive up to about 200k triangles. Their speed comes in part from what lever 2 would need a certificate for |
| Manifold (wiki "Manifold Library", documentation) | "topology is exact, while geometry is inexact due to floating-point rounding"; manifold inputs required, self-overlapping input not guaranteed; the intersection work is ideally O(√n) for spheres behind an O(n log n) parallel broad phase: the output-sensitive shape of lever 1, without our guarantees |
| Trettner, Nehring-Wirxel, Kobbelt, *EMBER*, ACM TOG 41(4), 2022 (repository notes on the paper) | local arrangements, an operation-aware early-out (our T3a cull), skipping self-pairs of results known clean (lever 2), input quantised to 26 bits |
| *trueform: Fast And Robust Mesh CSG Via Topological Aggregation*, arXiv 2607.15905 (abstract only; code never read) | integer-exact predicates on the input planes, "exact without exact constructions", output rounded to floating point, decisions across faces by a majority vote within a topological unit; "up to two orders of magnitude faster". The abstract states neither a clean-output guarantee nor the handling of self-intersecting input |
| Tan, Weller, Zachmann, *SIMDop: SIMD Optimized Bounding Volume Hierarchies for Collision Detection*, IROS 2019; Ytterlid, Chalmers MSc thesis 2013 (search summaries only) | wide trees with SIMD traversal for collision queries, "up to 50 %" faster proximity tests: the size of gain assumed for lever 4 |
| Valque's thesis 2024, part II; Devillers, Lazard, Lenhart 2020 (BOOLEAN_RESEARCH_PRIOR_ART section 2) | local rounding with a guarantee is open research; lever 1a does not need it: it keeps today's rounding and only stops building structures for triangles the rounding cannot change |

No source was found that states an output-sensitive exact boolean on raw soups with a clean rounded result; the fast
systems get their locality by requiring clean inputs (Cherchi, Manifold) or by not producing a certified double mesh.
Terms searched: "output-sensitive / localized mesh boolean exact", "wide BVH dual traversal self-collision",
the three paper titles above.

## 8a. The related plans (#1253, #1255, #1258, #1279): what this study answers

Coordinator, 2026-11-22: the open plan PRs on boolean and rounding speed are covered here on the cost side. The guarantee
theory of 3D snap rounding (#1259, #1278) and the certifying checker (#1256) are other sessions' topics and are not
touched.

| plan | its question | answered here? | what this study adds |
|---|---|---|---|
| **#1253**, lead 1: the two unbuilt #1064 levers (centroid without `AddPoint`, exact `PlaneSide` kernel) on the tail case 42327 | their share on the normal stratum | **no** | not measured: both are classification work on one tail case; on T6 the whole classification is 4.5 % at 1 thread and 5.3 % at 4 (section 3.1), which caps anything inside it on the normal stratum |
| **#1253**, lead 2: normal-cone certificate for the operand self-join | an upper bound of the skippable pairs | **in part** (a bound, not the probe of the plan) | a certificate replaces the triangle tests of the self-join, not its traversal. On T6 the self-join is 5665 ms per pass over the 98 pairs, of which 1399 ms are traversal (section 7): at most 75 % of it, so **at most −18 % at 1 thread and −11 % at 4** over the 294 booleans if the certificate itself were free; on the seam union at most −11 % at 1 thread. It stays the only exact lever for the self-join of a *raw* operand (lever 2 needs a certificate of cleanness). Its cost side (the certificate was slower than the tests in the Reducer, new error bounds in exact code) is unchanged, so the verdict "parked" stands |
| **#1253**, lead 3: ABC chain step 3, where ours is 1.1-2.2x slower than Geogram and CGAL | a diagnosis | **no** (not measured; the ABC data was not fetched) | a hypothesis to test first: a step-3 operand is the result of step 2, so it is large, clean by our own check, and pays the full self-join, the trees and the rounding over all its triangles again; that is the profile of the seam stratum, where levers 2 and 1a remove 20 % and up to 32 % at 1 thread. `seam-levers --cases` runs every part of this study on the inputs that `chain-step` keeps: the first probe if the lead is reopened |
| **#1255**: EMBER lever 2, skip self-pairs of certified operands | step 1 of the plan: the upper bound before any API code | **yes for T6-like steps and seam inputs**, not on ABC chains at 12 threads | section 5: at most −24 % at 1 thread and −15 % at 4 for a step with two certified operands, −20 % / −9 % on the seam union, 0 on raw single booleans; the number of pieces is the same with and without the self-join on all 101 clean pairs; a content stamp costs 0.8 % (64-bit mix) to 2.0 % (SHA-256) of the self-join, so the plan's "content hash" variant is affordable and no defensive copy of the mesh is needed |
| **#1258**, lever (b): box-neighbour enumeration of long thin tilted triangles in the first search | pairs a tighter bound would remove | **no** | not measured: neither stratum has such triangles (the tubes are well shaped). Lever 1a does not change the number of box neighbours per query; it removes the tree those queries run in for the unchanged triangles, so (b) stays a separate question for 519511, 1517923 and 668056 |
| **#1258**, lever (c): the cost of `near` inside edit evaluations | a profile | **no** | not measured: T6 has 215 edits in 294 booleans and the tubes 2-5 per longer union or difference; this is the rotated, dirty stratum |
| **#1258**, lever (d): tree build and setup over all triangles when few vertices were rounded | the share of the full-mesh tree build | **yes**: this is lever 1a | sections 4.2 and 10: the tree alone is 15 % of the rounding on T6 (at most −3.9 % of the booleans at 1 thread, −2.8 % with a tree built on the first edit) and 30 % on the seam union (at most −11 %), by a prototype that finds the same number of candidate pairs; the setup around it is the larger part on large results (table of steps). One correction to the plan: `MeshRoundingRepair.cs` is not in the exact-file list of `.editorconfig`, so the tier is M with the rounding mutation shards, not an exact-code review; `TriangleBoxTree` is not changed by 1a |
| **#1258**, #837 lever 2 (rotated CAD class) | – | **no** | not measured |
| **#1279**: open-operand speed, label propagation (HP 8) | a refreshed profile of the open cohort and a soundness argument | **no** | both strata here are closed operands; nothing in this study measures the generalized winding number path. Levers 1a and 3x act after the classification and apply to open results unchanged; lever 1b (pass-through of untouched regions) is the closed-input relative of the propagation asked for and gives no argument for open ones |

So of the four plans, this study settles the first step of #1255 and lever (d) of #1258, tightens the bound of the
normal-cone lead of #1253, and leaves the tail and open-operand questions (#1253 leads 1 and 3, #1258 (b), (c) and
#837 lever 2, #1279) to measurements on their own strata.

## 8b. The removal of tiny components and the levers (audit finding, 2026-11-22)

The SolidBooleanPipeline audit found that SafeRounding's removal of tiny or thin components drops valid solids while the
run reports success and `RoundingClean`; only `Rounding.RemovedComponents` shows it. Reproduced here
(`seam-levers --parts drop`, `drop_untouched_cube.jsonl`): the box [0, 1024]³ united with a disjoint cube of edge 2^-k
beside it. The cube is an input solid that the boolean does not touch.

| cube edge | with SafeRounding | `RemovedComponents` | `RoundingClean`, `NotCleanReason` | without SafeRounding |
|---|---|---:|---|---|
| 2^0 … 2^-8 | both solids, 24 triangles | 0 | true, None | 24 triangles, None |
| 2^-9 … 2^-12 | **the cube is gone**, 12 triangles | 1 | true, None | 24 triangles, **"degenerate triangles"** |

**The mechanism** (`MeshRoundingRepair.remove_tiny_components`): the scan for degenerate triangles runs over all result
triangles with one threshold, 1e-12 of the squared diagonal of all triangles. The cube's faces (area 2^-19 from edge
2^-9 on) fall below it, so its 12 unchanged input triangles are "bad", the component counts as touched by a defect,
it is tiny, and it is removed. No edit and no new vertex is involved. The same criterion is what `NotCleanReason`
applies (the right column), so the removal is what makes the result "clean".

**1. Does a lever widen the drop?** No. Every lever of this note is accepted only bit-identical (result hashes, and for
1a also `RemovedComponents`, `RemovedTriangles` and `ScaleDisparity`), so each one drops exactly what is dropped today,
and none drops more. This has a consequence for lever 1a, step 2: the scan for degenerate triangles must keep running
over the unchanged triangles (section 4.2 lists it as needed), because that scan is how an untouched component reaches
the removal. Restricting it to the new triangles would change results. On the two strata of this study the question
does not arise: **0 components are removed in the 294 T6 booleans** (40 of them have degenerate triangles, 216 in all,
repaired by 215 edits) and none in the 18 tube booleans.

**2. Would local-only rounding confine the removal to the cut region?** It could, as a deliberate output change, not as
a side effect. The rule would be: a component is a candidate for removal only if it has a new triangle (one with a
vertex that is no input point) or a vertex an edit moved; a component of unchanged input triangles is kept whatever
its size. The data for it exists (`exactAtStart`, the flags lever 1a relies on), and it fits the lemma: an unchanged
component is the exact result, so removing it is never needed for a correct rounding. The cube above would be kept.
The slab of the audit (an intersection of thickness h, dropped from h = 2^-15 after a rotation) would not: it consists
of new triangles and lies in the cut region, where the thin-component rule is the documented bound of SAFE_ROUNDING.md
section 1 (thinner than `maxLength`). That half of the finding is a question for the snap rounding spike, not for the
cost side.

**3. The cost of not dropping.**
- Time: none. The removal is 2 % of the rounding on T6 (BOOLEAN_THREAD_SCALING.md 7.2), and keeping a component is less
  work than removing it.
- Status: the result is then **not clean by the public check**. With the cube kept, `NotCleanReason` reports
  "degenerate triangles" (the right column above) and `RemainingDegenerate` is 12, so `RoundingClean` is false, although
  the result is the exact union and a valid solid. The drop and the clean flag are two faces of one criterion, a triangle
  area relative to the diagonal of everything. Keeping the solid *and* calling the result clean needs that criterion to
  change (for example relative to the triangle's own component, or to tell a small well-shaped triangle from a sliver);
  `NotCleanReason`'s public semantics are fixed for this study, so this is an owner decision. The roadmap already
  carries the question ("Degeneracy criterion: reopen only if the degenerate-triangle results turn out to be
  well-shaped tiny triangles"): these are.
- Between the two: keep the unchanged component and report it (`ScaleDisparity`, as #985 does where an input is itself
  thin or tiny). Then no solid disappears silently, and the run is honest about not being clean.

## 9. Reproduce

```sh
T=/path/to/tool; dotnet build benchmarks/BooleanComparison -c Release -o $T
export DOTNET_TieredPGO=0
B="dotnet $T/BooleanComparison.dll"; R=benchmarks/BooleanComparison/perf/seam_levers_report.py
# seam stratum, 1 and 4 threads (all parts; --check adds NotCleanReason of every result)
DOTNET_PROCESSOR_COUNT=1 taskset -c 0 $B seam-levers --k 96 --half 3,12,48 --repeat 3 > tubes_t1.jsonl
$B seam-levers --k 96 --half 3,12,48 --repeat 3 --check > tubes_t4.jsonl
DOTNET_PROCESSOR_COUNT=1 taskset -c 0 $B seam-levers --k 96 --half 3,12,48 --repeat 3 --parts boolean --local-rounding > tubes_local_t1.jsonl
# T6 (cases-t6.txt: one case directory per line, built as in docs/perf/BOOLEAN_THREAD_SCALING.md, "Reproduce")
DOTNET_PROCESSOR_COUNT=1 taskset -c 0 $B profile --repeat 2 --safe-rounding cases-t6.txt > t6_profile_t1.jsonl
$B profile --repeat 2 --safe-rounding cases-t6.txt > t6_profile_t4.jsonl
DOTNET_GCgen0size=0x40000000 $B profile --repeat 2 --safe-rounding cases-t6.txt > t6_profile_t4_gen0size.jsonl
DOTNET_PROCESSOR_COUNT=1 taskset -c 0 $B seam-levers --cases cases-t6.txt --repeat 2 --parts arrangement,traversal,near,stamp > t6_parts_t1.jsonl
$B seam-levers --cases cases-t6.txt --repeat 2 --parts arrangement > t6_parts_t4.jsonl
DOTNET_PROCESSOR_COUNT=1 taskset -c 0 $B seam-levers --cases cases-t6.txt --repeat 1 --parts boolean --local-rounding > t6_local_t1.jsonl
# tables
python3 $R phases t6_profile_t1.jsonl t6_profile_t4.jsonl
python3 $R seam tubes_t1.jsonl tubes_t4.jsonl
python3 $R selfjoin t6_parts_t1.jsonl t6_parts_t4.jsonl tubes_t1.jsonl tubes_t4.jsonl
python3 $R tree t6_parts_t1.jsonl t6_profile_t1.jsonl; python3 $R tree tubes_t1.jsonl
python3 $R local tubes_local_t1.jsonl t6_local_t1.jsonl
$B seam-levers --parts drop > drop_untouched_cube.jsonl       # section 8b
```

The step times of section 4.2 and the counter switch of section 6.1 came from scratch patches of
`MeshRoundingRepair.cs` in a separate worktree (Stopwatch marks; an environment switch around the increment), built
into a tool directory of their own and not committed, as EXACT_BOOLEAN_PERFORMANCE.md prescribes for probes. Their
output is in the raw folder.

## 9a. Measured: lever 3x and the lazy first search (2026-11-22, not merged)

Both were built (branch `perf/rounding-lazy-pair-search`, commit e28849b, PR #1322 closed): the per-block count in `intersecting_pairs_all`,
and a first pair search with a tree of the new triangles only (new against new from that tree, new against exact from each exact
triangle's box), the tree of all triangles built at the first `near` query. Results were bit-identical (hash, `SkippedExactQueries`,
counts) on tube unions, differences, intersections and the exact-pairs cases; the pair sets equal by construction and by check mode. Rows:
[perf/lazy_first_search_2026-11-22/](../../benchmarks/BooleanComparison/perf/lazy_first_search_2026-11-22/README.md).

Seam union, tubes half 48, best of 3, noise about 5 %:

| | 1 thread total | 4 threads total | 4 threads rounding |
|---|---:|---:|---:|
| base | 2443 ms | 1168 ms | 483 ms |
| both levers | 2396 ms (-2 %) | 1194 ms (+2 %) | 516 ms |
| 3x only | 2460 ms | 1053 ms (vs 1078 in its own rounds, -2 %) | 413 ms (vs 472, -12 %) |

**Why the lazy search gives 0 on seam unions.** The first search itself falls from about 470 to about 103 ms at 1 thread, as the
prototype of section 4.2 said. But the first proposed edit (`improves` calls `near`, even when the edit is rejected) builds the full tree at
once, before any edit is committed: the seam union has 3 edits and about 30 `near` queries. The 375 ms moves from the search to the
first edit. This is the "gains nothing on a boolean with an edit" of section 4.2, now measured: the PR is only a gain for results without
edits, which seam unions are not.

**Estimate for the edit-query design (not built).** Serving the edits' `near` queries without the full tree needs a linear pass over the
unchanged boxes per query: about 14 ms at 1 thread (section 4.2's probe), so about 30 queries cost 420 ms, as much as the tree build (375 ms),
and about 120 ms against 156-205 at 4 threads. A cache of the boxes (30 MB for 626k triangles) brings a query to maybe 5 ms, so about 150 ms
against 375 ms at 1 thread (-9 % of the union, before the cost of the new-triangle tree and a fallback to the full tree after N queries),
and it grows with the number of queries: the T6 booleans with edits hold more. That is below the owner's 10 % bar for a speed PR and
adds a second query path to `near`. Not pursued: ranks 1 and 2 of section 10 are not worth a PR on their own; rank 3 (setup scans) remains the way to the
rounding's far work.

**3x alone** gives -12 % of the rounding at 4 threads (413 against 472 ms, one input, six rounds) and -2 % of the total, below the bar too.

## 10. Ranked implementation PRs (each needs the owner's go)

| rank | PR | gain (bound → expected) | guarantee argument | effort | risk |
|---|---|---|---|---|---|
| 1 | **3x: count skipped triangles per block** in `intersecting_pairs_all` | seam union −4 % at 4 threads (a switch, ten runs of one input); T6 not measured | a counter; `SkippedExactQueries` equal | S (hours) | low |
| 2 | **1a, step 1: the first pair search without a tree of all triangles**; the full tree built on the first edit | bound by prototype: seam union −11 %, T6 −3.9 % at 1 thread; expected with a tree built on the first edit: T6 −2.8 %, seam unions with edits 0; the bound needs the edits' queries served without the full tree (section 4.2) | the same candidate set by construction (equal pair counts on 303 booleans), the existing lemma | S for the lazy tree, M with the edit queries | low to medium: every later query must be served; `CheckOnThisThread` already compares searches |
| 3 | **1a, step 2: setup scans proportional to the new triangles** (`atPosition`, anchors, `component_scales` from the operands, shared finite / range scans) | seam: up to the rest of the rounding's far work (−21 % at 1 thread; about −30 % at 4, with the tree's 4-thread time taken from the step probe); T6 estimate −10 to −15 %; expected half | each scan needs its own argument that the unchanged triangles cannot contribute; results bit-identical | M-L | medium: `Contacts`, false contacts and the scales are observable |
| 4 | **3: serial tail**, one step per PR, by size: `ExactPlanarSimplify` per region, result vertices, `setup.points`, input checks; the flat contact count on the way | T6 at 4 threads: bound −12 %, expected −6 %; more at 12 threads | bit-identical ids; determinism tests per new loop | M each | medium; exact files need the allow-list review |
| 5 | **2: certified operands** (plan #1255, with the content stamp of section 5) | chained steps: bound −24 % at 1 thread, −15 % at 4; raw single booleans 0 | a verified stamp, never a flag | L | medium: API (additive), exact files |
| 6 | **1b: pass-through in classification and assembly** | seam: −16 % / −14 %; T6 estimate −7 % | bit-identical ids | L | high |
| – | 4 (wide tree, packets) | ≤ 4 % | – | L | dropped |
| – | 5 (weaker-guarantee kernel) | 3-4x, by giving up the guarantees | none | a second product | not proposed; owner decision |

Ranks 1-3 together bound the rounding on both strata and need no exact file and no API. Of the levers that gain on raw
single booleans (1-4 and 6), they are the ones outside the exact files.
