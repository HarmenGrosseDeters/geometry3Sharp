# Reducer: speculative parallel certification of `MaxDeviation` (design, before code)

Owner decision 2026-11-02: C first of the bundled Reducer measurement (lever C of
[RESEARCH_REDUCER_REMESHER.md](RESEARCH_REDUCER_REMESHER.md) section 1.3). The design note comes before the code; the
result must be bit-identical to master.

## 1. Measured upper bound (master 48be182, scratch instrumentation, not committed)

ReducerBenchmark: bumpy sphere (199692 triangles) and terrain (180000), tolerance 0.001 / 0.003 / 0.01 of the bbox
diagonal, reduction to 4 triangles.
- **The check is 90-98 % of the time.**
- **The existing parallelism is weak.** `collapse_within_deviation` runs `gParallel.ForRange` over the about 14 new
  triangles of one collapse. On 4 cores that gains only 1.0-1.46× over 1 core (at 0.01 the 1-core run is faster:
  8.7 s against 9.4 s). The tasks are too small.
- **Almost every check passes:** about 99 %.
- **Certificates stay valid.** Take windows of k consecutive checks of the serial run. A certificate computed at the
  start of its window is still valid when the serial loop reaches it if its ring shares no vertex with a collapse
  accepted earlier in the window. That holds for 99.6-99.8 % of the checks at k = 4, 99.0-99.6 % at k = 8 and
  96-98 % at k = 32.
- **Bound.** Model: the window is certified on 4 cores, and the invalid certificates are computed again serially.
  - On the check: 3.6-3.9× (k = 4) and 3.2-3.8× (k = 8).
  - On the total: 2.5-3.4× against serial, so **about 2.1-2.4× against today's 4-core run**.
  - The model is optimistic: it uses the serial run's own order (section 3 says what a real window loses).

## 2. The invariant: the serial loop decides, unchanged

`DoReduce` stays the same serial loop. It does `Dequeue` the top edge, then `CollapseEdge`, then `UpdateNeighbours`;
its order and its decisions do not change. The only change is inside `collapse_within_deviation(a, b, newPos)`: it
returns a result computed earlier, in parallel, if that result is still valid; otherwise it computes it as today.

This is bit-identical because:
- the result of `collapse_within_deviation` is a pure function of its inputs: a, b, newPos, the positions and the
  connectivity of the triangles around a and b, the reference tree (fixed during `DoReduce`), `MaxDeviation` and
  `MaxDeviationDepth`.
- its value is the AND over the new triangles (and the early query of newPos), so it does not depend on the order or
  the thread in which they are evaluated.
- the speculation does not write to the mesh, the queue, the quadrics or any state the serial loop reads. Its only
  output is a table of results.

What must be shown, by tests and by reading:
- **(i)** `collapse_within_deviation` has no side effects that the serial loop observes. `ShareDeviationQueries`
  caches live per call, and the reference tree is read-only. `deviationTarget.FindNearestTriangle` is already called
  from `gParallel` today, so it is thread-safe.
- **(ii)** A stored result is used only when its inputs are equal to the current ones (section 4).

## 3. The speculation: a window of the queue, read-only

When the serial loop reaches the check and has no valid stored result for the current edge, the speculation runs:
1. **Read the window.** It reads the k edges with the lowest priority from `EdgeQueue` without removing them. This
   is a new internal `IndexPriorityQueue.TopK(k, list)`: a best-first walk over the heap array with a small auxiliary
   heap, read-only. The current edge is the first of them. Which edges the window holds affects only the speed, never
   the result.
2. **Prepare each edge.** For each edge it runs the pure part of `CollapseEdge` up to the check: the constraints,
   `collapse_to`, `get_projected_collapse_position`, the quadric budget, `collapse_creates_flip_or_invalid`. This part
   is factored out of `CollapseEdge` into `prepare_collapse(eid, out a, out b, out iKeep, out newPos)`, which the
   serial path calls too, so that both compute the same newPos. Edges that fail before the check are skipped; the
   serial path decides them as today.
3. **Certify in parallel.** It runs `collapse_within_deviation_core(a, b, newPos)` for the prepared edges in
   parallel, one task per edge, with the inner loop over the new triangles serial in these tasks (no nested
   `gParallel`). The edge the serial loop is waiting for is one of them.
4. **Store the results** with their inputs (section 4).

What the window loses compared with the model of section 1: after each collapse, `UpdateNeighbours` changes the
priorities around the kept vertex. The next serial edge can then be one that is not in the window. It is computed
then, as part of a new window. The share of hits against the window is measured in the A/B (section 6).

## 4. Validity: vertex stamps and the inputs

Each mesh vertex gets a stamp: the value of a counter `clock` at its last change.
- On every accepted collapse (`mesh.CollapseEdge` Ok, then `SetVertex(iKeep, newPos)`), the clock is increased, and
  the kept vertex and all vertices of the triangles around it after the collapse get the new value. These are the
  only changes of position or connectivity inside the loop.
- Constraint changes, projection and `Reproject` happen outside the loop.

A stored result for edge e holds a, b, newPos and the clock at which it was computed. The serial loop uses it only if
all of these hold:
- the current edge is e, with the same a and b (in the same roles), and with newPos equal bit for bit (the current
  newPos comes from `prepare_collapse`, as before);
- no vertex of the triangles around a and b has a stamp greater than the stored clock. A collapse that changed one of
  these triangles stamps all of its vertices, because they are the triangles around its kept vertex; a vertex that
  moved is stamped too.

Otherwise the stored result is dropped and computed again. Edge ids that the mesh reuses are covered by the check
of a, b and newPos.

## 5. Determinism and scale

- **Determinism.** The serial decisions are the same with any number of threads, any k and any scheduling. Only
  which results come from the table changes. Test: hashes of the reduced mesh with the speculation off, with
  k = 1, 4, 16 and with 1 and 4 threads, all equal. Repeated runs are equal. Hashes are also compared against master
  (`ReducerBenchmark hashes`).
- **Scale.** No new thresholds. Invariance over `Similarity.All()` follows from the unchanged decisions; the
  existing Reducer invariance tests run with the speculation on.
- **Exact code.** `Reducer` is not in the exact files (`BannedSymbols`, `.editorconfig`), so no entry in
  `ExactCodeAllowList`. The parallel loop performs no floating-point reduction across threads: each task computes one
  boolean.

## 6. Switch, tests, measurement

- **Switch.** An internal `SpeculativeDeviationChecks` (default to be decided after the A/B; the owner decides). Its
  window k defaults to 4 × the core count, tuned in the A/B.
- **Tests.**
  - bit-identical against the serial path, on procedural meshes (bumpy sphere, terrain, a CAD-like box mesh with
    constraints and boundaries, a mesh with `collapse_to` cases);
  - stamps: a stored result is rejected after a nearby collapse. This is a direct test, in the style of
    `MemoizedRejections_NotForIntersections` of #292.
  - `TopK` returns the k smallest priorities, read-only, and the queue is unchanged afterwards.
  - mutation of the validity check (always use the stored result): the bit-identity tests must fail.
- **Measurement.** ReducerBenchmark (with check) before/after, on 1 and 4 cores, plus the window hit rate. The 34
  wrap cases must stay bit-identical; they do not use `MaxDeviation`, so this checks that the change is local.
- **Gate.** Bit-identical everywhere, and faster on 4 cores. The before/after numbers go into the ReducerBenchmark
  README.

## 7. Open points for the owner

- Default on or off after the A/B.
- **k and memory.** At most k stored results, dropped when used or invalid.
- **Interaction with `ShareDeviationQueries`.** That cache is per call, so it is unaffected; checked in the A/B.
