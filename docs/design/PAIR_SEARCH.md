# Faster candidate pair search in the exact arrangement (design, for approval)

Status: **design, not yet implemented.** Tier L ([AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md)): the owner approves this
document first; step B adds an error bound, which gets an independent review. Stacked on #223
([FIXED_WIDTH_EXACT_POINTS.md](FIXED_WIDTH_EXACT_POINTS.md)).

## 1. Goal and evidence

**Goal.** Make the phase `intersections.pairs` of `ExactMeshArrangement.find_intersections` cheaper on the
real-world classes, with bit-identical results.

**Evidence** (serial: `gParallel.SerialOnThisThread`, `taskset -c 0`, `DOTNET_PROCESSOR_COUNT=1`, warm, #223 head; a
temporary probe with stopwatches, not committed; method in
[perf/EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md), "Where the time goes"):

| | `cgal` | Thingi10K | `cherchi` |
|---|---|---|---|
| total, serial | 32.4 s | 30.0 s | 54.0 s |
| pair search | **16.1 s (50 %)** | 14.4 s (48 %) | 21.1 s (39 %) |
| box queries | 4.4 s: 4.2M queries, 41.3M hits, **40.4M within one mesh** | 3.7 s: 47.2M hits, 44.3M within one mesh | 7.5 s: 92.7M hits, 91.7M within one mesh |
| `touch_only` quick test (`one_side_except_shared`) | 5.0 s: 18.8M pairs (13.2M share a vertex, 4.2M an edge) | 5.2 s: 21.2M | 7.3 s: 42.4M |
| `ExactTriangleIntersection.IntersectsBeyondShared` | 2.8 s: 2.3M pairs | 2.3 s: 1.9M | 1.5 s: 2.1M |
| `separated` (pairs of different meshes) | 0.3 s: 0.8M | 0.8 s: 3.0M | 0.4 s: 1.0M |
| rest (loop, lists; about 1 s of it is the probe) | 3.6 s | 2.6 s | 4.4 s |

On 4 cores the pair search is 29-30 % of the time on `cgal` / Thingi10K (it scales better than the other phases).

**Reading.** Almost all of the work is the search within one mesh (`SelfIntersections`, which `ExactMeshBoolean`
turns on): neighbouring triangles of a closed mesh share a vertex or an edge, their boxes overlap, and nearly all of
them only touch. Between the two meshes there is little to gain.

## 2. Scope and non-goals

**In scope:** `ExactMeshArrangement.find_intersections` / `candidates` / `touch_only` / `one_side_except_shared`,
`TriangleBoxTree` (a self-join), and a new variant of the shared-vertex test next to `IntersectsBeyondShared`.

**Non-goals:**
- `ExactTriangleIntersection.IntersectsBeyondShared` is not changed. It is shared with `Reducer`, `MeshRepairMetrics`
  and `DualContouring`, and the SafeRounding session (roadmap, row R, item 4) has a fix for it.
- No change of results: the set of candidate pairs stays the same; only their order may change, which the pipeline
  does not depend on (the pairs are intersected in parallel; point ids are assigned in parallel; the output is
  ordered by `ComparePoints`). `exact_hashes.sh` proves it.
- Public API (upstream 1.0.324) unchanged; netstandard2.0 keeps building (C# 7.3).

## 3. Steps, each measured on its own, each with the stop rule

### A. Self-join of the box tree (no new bound)

Today every triangle `ka` of mesh `m` queries `m`'s own tree and drops the hits with `kb <= ka`: half of the 40M
self hits are thrown away, and every pair is found twice.

- `TriangleBoxTree.SelfPairs`: a dual traversal of node pairs `(n1, n2)` with `n1 <= n2` (a node with itself
  recurses into `(left, left)`, `(left, right)`, `(right, right)`), boxes compared closed as in `Query`, leaf pairs
  `i < j` in leaf order. It reports each overlapping pair of boxes once, as `(min(ka, kb), max(ka, kb))`.
- Parallel over the node pairs of a fixed depth (a work list of independent subtree pairs), each worker with its
  own result list; the lists are concatenated in work-list order (deterministic for a given tree).
- The tests of a pair (`touch_only`) are the same as today; only the enumeration changes.
- Pairs of different meshes keep the per-triangle query (they are few).
- Expected: about half of the query time (`cgal` about 2 s of 32 s serial).

### B. A cached double plane per triangle as an orientation filter (new error bound)

`one_side_except_shared(T, U)` calls the adaptive `ExactPredicates.Orient3d(t0, t1, t2, p)` for 1-2 corners of `U`;
each call recomputes the differences and the cross product of `T` before its own filter.

- Per input triangle, once: `n = fl((t1 − t0) × (t2 − t0))` and the bounds of its error, from the exact double
  corners.
- Per corner `p`: `s = fl(n · fl(p − t0))` and a bound `E(p)`. If `|s| > E` the sign is the sign of
  `orient3d(t0, t1, t2, p)` (with the library's sign convention), else the existing adaptive `Orient3d` decides.
- The bound is derived in the code comment and in this document before code (inputs exact doubles; differences,
  cross product and dot product each rounded once; no FMA, the #225 analyzers check it), tested against exact
  determinants on near-coplanar, huge, tiny and subnormal inputs (`ExactFilterBoundTests` style), and reviewed
  independently. Where `n` or the products would overflow or underflow, the filter declines.
- Storage: 4 doubles per input triangle in an array of the arrangement (no objects).
- Expected: the quick test about halved (`cgal` about 2.5 s of 32 s serial).

### C. Orientations reused in the shared-vertex test (no new bound)

When the quick test does not decide (2.3M pairs on `cgal`), `IntersectsBeyondShared` computes the orientations of
the corners again. A private variant in the arrangement takes the signs already known. It is tested against
`IntersectsBeyondShared` on every pair of the test meshes and on random near-degenerate configurations (same answer
always).

- Expected: about 1 s of 2.8 s on `cgal`.

**Together:** an estimated 15-18 % of the serial time on `cgal` / Thingi10K, less on `cherchi`; somewhat less on 4
cores, since the phase scales well.

## 4. Acceptance and stop rule

- **Bit-identical:** `perf/exact_hashes.sh` (263 runs) against `perf/exact_hashes_reference.jsonl`, 0 differences,
  after each step.
- **The same candidate pairs:** a test compares the set of pairs of the new enumeration with the old one (kept as a
  test-only reference) on the test meshes, with self-intersections, open meshes, degenerate triangles, coincident
  vertices and many meshes.
- **Time:** `perf/run_classes.sh` A/B, two alternating rounds, per class; `profile` on grid 5 / grid 10 / spheres
  20.
- **Stop rule per step:** a step that does not give at least 3 % on the `cgal` + Thingi10K total (4 cores) is
  dropped, with its numbers recorded here. The whole PR follows the hand-over's 10 % bar on `cgal` / Thingi10K;
  below it, the measured steps stay recorded and only what clears its own 3 % is proposed.
- **Determinism:** `ExactBooleanDeterminismTests` (serial equals parallel, repeated runs), plus a repeated-runs test
  of the self-join's pair list.
- **Mutation:** the arrangement shards; new tests in their filters; the split offsets updated.
- **Review:** an independent review of the diff (and of the bound of step B) with `docs/REVIEW_CHECKLIST.md`.

## 5. Risks

- The self-join changes the order in which candidate pairs are found. Nothing downstream should depend on it, but
  the `CandidateBatchSize` batching (bounded memory with many meshes) has to keep working: the self-join runs per
  mesh, the cross-mesh pairs keep their batches.
- The filter of step B is new floating-point code in the exact pipeline: its bound must be proven, not fitted, and
  the fallback must stay the existing adaptive predicate.
- If most of the "rest" (3.6 s on `cgal`) is the loop overhead per hit rather than the probe, step A also cuts it,
  since it touches each pair once instead of twice.
