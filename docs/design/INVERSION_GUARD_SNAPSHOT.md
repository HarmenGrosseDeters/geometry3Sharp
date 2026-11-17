# The `RejectInvertingPasses` snapshot: can it be cheaper with the same guarantee?

Research task (owner 2026-11-17), roadmap item 5 of "Open for the future (2026-11-15)" in
[alpha-wrap-and-repair.md](../roadmap/alpha-wrap-and-repair.md) and item 14 of [research.md](../roadmap/research.md).
**Result: no single replacement qualifies.** No replacement of the snapshot can save 10 % of the run: the copy is at
most 8.6 % of it. The cheap exact lever on the check (a parallel sign count) gains nothing. The one combination
whose ceiling is above 10 % is an undo log in `DMesh3` together with component labels kept through the edge
operations, and only on `RemesherPro` (`FastestRemesh`, and `pro_iter` on bumpy_uniform) on closed meshes. It is intrusive (shared core code,
hot-path writes, a correctness argument for each edge operation), and its net gain is unmeasured. It is parked as an
option for the owner (section 4). Nothing in the library changes.

## 1. What the guard does

`Remesher.RejectInvertingPasses` (default on, also `RemesherPro`; `mesh/Remesher.cs`):

- `begin_inversion_guard`:
  - computes the signs of the closed components (`compute_component_signs`), cached from the end of the previous
    pass while `mesh.Timestamp` is unchanged, so there is one count per pass;
  - if there is a closed component with a volume, makes a full copy of the mesh (`new DMesh3(mesh)`) and of the
    constraints (`MeshConstraints.Copy`).
- `end_inversion_guard`:
  1. Computes the signs again:
     - a union-find of the triangles over their edges;
     - per closed component, the signed volume relative to the first vertex of its lowest triangle, summed in
       triangle-id order;
     - a tolerance of 1e-9 A^1.5, with A the summed area;
     - a vote by vertex id against the signs before the pass.
  2. If the counts of positive and negative components, or the sign of a component, changed, it restores the copy
     (`mesh.Copy`, `CopyFrom`).

The guarantee a replacement must keep:
- bit-identical output wherever no pass is rolled back;
- the same rollback decisions, and hence the same output everywhere.

`mesh.Copy` copies the free lists of the `RefCountVector`s and the `SmallListSet`. So after a rollback, the next pass
reuses the same ids as before the rolled-back pass, and a replacement must restore these exactly as well.

## 2. Measurement

**Instrumentation:**
- A scratch build of master 03a3cd26 (not committed), with a `Stopwatch` around:
  - the snapshot copy;
  - each call of `compute_component_signs`, and inside it the union-find (allocation and edge loop);
  - the guarded pass, from `begin_inversion_guard` to the end of `end_inversion_guard`.
- Cloud container, 4 cores (Xeon 2.1 GHz).

**Timed runs only:** `benchmarks/RemeshBenchmark` (`runs=3 only=<case>`). The counters are reset before the three
timed runs of each configuration, and the shares are of their wall time (`time_s`, which includes the setup), so they
are a lower bound on the share of the passes. This excludes the warm-up run, the serial run and the 2^k-scaled runs,
which dilute the shares of an aggregate count (a first version of this note had that error).
- `remesher` = `Remesher` with the `g3cli remesh` defaults: 20 x `BasicRemeshPass`, parallel smoothing and projection.
- `pro_fastest` = `RemesherPro.FastestRemesh(20)`.
- `pro_iter` = 20 x `RemesherPro.RemeshIteration`.

| input | config | snapshot copy | sign count | of it union-find | guard total |
|---|---|---:|---:|---:|---:|
| sphere_refine (6.3k tris, x0.5) | remesher | 3.0-3.7 % | 11.5-12.6 % | 5.3-5.8 % | 14.5-16.3 % |
| | pro_fastest | 5.2-8.6 % | 16.6-17.0 % | 6.4-8.2 % | 22.0-25.6 % |
| | pro_iter | 2.7-2.8 % | 10.5-11.6 % | 4.7-5.0 % | 13.3-14.4 % |
| bumpy_uniform (75k, x1) | remesher | 3.2-4.4 % | 7.2-8.1 % | 2.5 % | 10.4-12.5 % |
| | pro_fastest | 7.9-8.1 % | 13.9-15.1 % | 4.3-4.8 % | 21.8-23.1 % |
| | pro_iter | 7.4-8.5 % | 11.3-12.7 % | 3.5-3.8 % | 18.7-21.2 % |
| noisy_coarsen (303k, x3) | remesher | 6.1-6.7 % | 7.6-7.9 % | 2.8-2.9 % | 13.7-14.6 % |
| | pro_fastest | 3.8-4.4 % | 4.9-5.4 % | 1.9 % | 8.7-9.8 % |
| | pro_iter | 4.0-4.1 % | 4.8-5.0 % | 1.7-1.8 % | 8.8-9.0 % |
| open_stretched, terrain_band | all | none (no closed component) | 2.4-6.1 % | 1.6-3.9 % | 2.4-6.1 % |

The ranges come from three separate processes, each with three timed runs per configuration; one of them ran the
build of (d) below, which changes only the sign count and gives the same output bits.

**Discarded data:**
- Aggregate counts over all runs of a case: these shares are diluted, as described above.
- End-to-end on/off timings (`G3_GUARD=off`, runs=3): they are dominated by noise. They give 11-24 % for the open
  meshes, where the guard can cost at most the sign count, about 5 %.

**Other benchmarks:**
- `benchmarks/RemeshQualityBenchmark` (`fidelity=0`, all 192 procedural cases, `MinTriangleQuality` 0 and 0.2,
  `Remesher`): an aggregate over its two runs per case (no warm-up or serial runs there). The snapshot copy is 3.5 %
  of the guarded pass time and the sign count 8.0 % (union-find 3.6 %), a guard total of 11.5 %, over 7700 guarded
  passes.
- Thingi10K was not fetched here. Earlier measurements in [REDUCER_REMESHER_20.md](REDUCER_REMESHER_20.md):
  - section 12.4: on/off over sphere, torus, 78295, 93370 and 299291, 6-19 % (torus 12-19 %). The per-pass split on
    the sphere was copy 1.7 ms against signs 4.1 ms, i.e. about 30 % copy.
  - section 13: a bound on 93370, 78295, 299291 and a 170k sphere, 9-13 %, about 40 % of it the copy.
  - At the upper ends of these, the copy is at most about 6-8 % of a run.

**How often a pass is rolled back:**
- RemeshQualityBenchmark: 206 of the 7700 guarded passes (2.7 %), in 15 of its 384 runs:
  - cone12_x0.6 and cone16_x0.6: 4-6 passes per run, both quality settings;
  - 11 runs of the thin tubes (wall 0.05 / 0.1, target 1.2 / 1.6): 11-20 passes each.
- RemeshBenchmark: none in any of its runs.
- With the guard off (scratch switch `G3_GUARD=off`, one run of `only=^cone(12|16)_x0.6$`), cone12_x0.6 and
  cone16_x0.6 end with volume ratio 0 in both quality settings. With the guard on, 0.016-0.21.
- Restoring the 206 snapshots took 6 ms in all.

## 3. The candidates

**Bound for every snapshot replacement.** Replacing the snapshot leaves the sign count as it is, because the sign
count decides the rollback. So a replacement saves at most the copy share: at most 8.6 % of the run in every measured
configuration, whatever its cost and whether or not it is correct. That is below the 10 % bar, so none of (a)-(c) can
qualify alone:

- (a) **Undo log** of the changed vertices, triangles, edges and constraints.
  - Every pass smooths and projects all vertices: `smooth_vertices()` and `project_vertices()` are all vertices in
    both classes (the only override is `EdgeLoopRemesher`'s, and it has the guard off). Almost every free vertex moves,
    so the vertex part of the log is nearly the whole vertex buffer in every pass. That is about 15 % of the bytes of
    the copy.
  - The topology part must restore the free lists and the `SmallListSet` blocks exactly (section 1). That means
    logging inside `DMesh3`'s edge operations, a cost on the hot path of every caller.
- (b) **Copy-on-write / lazy snapshot** (dirty blocks of `DVector`):
  - smoothing dirties the vertex buffer in every pass;
  - the write barrier would sit on `DVector`'s indexer, which the whole library uses.
- (c) **Reusing the snapshot buffers** (copying into preallocated arrays):
  - bit-identical by construction;
  - saves only the allocation share of the copy, including the large-object-heap garbage collections that the
    per-pass arrays cause outside the copy timer;
  - bounded in section 13 of REDUCER_REMESHER_20.md (at most 4-5 % of the run, about 2 % expected), not built
    (owner 2026-11-12).

**A cheaper check.** The sign count is the larger share, but:

- (d) **A parallel, bit-identical sign count.**
  - Tried in code: the union-find roots computed once into an array (instead of `find` in three loops); the volume and
    area terms of each triangle computed in parallel blocks into arrays; then summed serially in triangle-id order
    (the same rounding).
  - The output hashes were equal to the old path on sphere_refine, bumpy_uniform and noisy_coarsen, all three
    configurations.
  - The sign count share was unchanged (within noise: 4.9-17.0 % against 4.8-17.0 %). The count is memory-bound, not
    bound by the arithmetic. Dropped.
- (e) **Per-component signed volume tracked incrementally** through split, collapse, flip, smoothing and projection.
  - The decision compares one particular floating-point sum (component-relative origin, triangle-id order) with a
    tolerance from another sum. An incremental sum rounds differently, so the decisions agree only away from the
    tolerance.
  - Rollbacks happen exactly near the tolerance: the flattened cones go to zero volume, and the tube volumes change
    sign back and forth over the passes. So an equivalent incremental check needs a certified filter (a forward error
    bound of the old sum, gamma_n times the sum of the absolute terms), with the old sum as the fallback.
  - It would not be cheaper. Almost every vertex moves in every pass, so almost every triangle's term changes, and the
    incremental update is Theta(T) per pass, the same order as the full sum it replaces.
  - A variant that skips the count when a bound shows that no sign can change (|dV| from the largest displacement
    times the area, plus the tetrahedra of the edge operations):
    - needs the area, which is Theta(T) again;
    - keeps the snapshot, because the decision is still known only after the pass;
    - must still produce the per-vertex signs for the new vertex ids, which the next pass's vote reads.
- (f) **Component labels kept through the edge operations**, instead of the union-find.
  - This one can be exact. The signs depend only on the partition into components and on which components are open:
    the origin and the order of the sums come from the triangle ids, not from the union-find roots. So correct labels
    give the same bits, and decisions change only if the labels are wrong.
  - The argument needs, per operation:
    - a split and a flip keep the partition, because the new triangles are joined to the old ones through an
      interior edge;
    - a collapse neither disconnects a component nor closes or opens one. This rests on the preconditions of
      `DMesh3.CollapseEdge` (the link condition, the tetrahedron and triangle cases, and an interior edge with two
      boundary vertices), for boundary edges as well;
    - the same for every other write of the remeshers and their subclasses.
  - Plausible, but it saves at most the union-find share: 1.7-8.2 %.
- (a) and (f) together, at zero overhead (copy plus union-find):
  - 13.2-15.0 % for sphere_refine and 12.3-12.9 % for bumpy_uniform with `pro_fastest`; 11.2-12.0 % for
    bumpy_uniform with `pro_iter`;
  - at most 9.5 % in every other configuration measured (5.7-9.5 for `remesher`, 7.5-7.7 for sphere `pro_iter`,
    5.7-6.3 for noisy `RemesherPro`);
  - these are ceilings: an undo log keeps the vertex buffer (about 15 % of the copy), logs on the hot path, and the
    labels cost their own maintenance.

**Prior art.**
- Cheap snapshots of a mutable structure are persistent data structures (Driscoll, Sarnak, Sleator and Tarjan,
  "Making data structures persistent", 1989). Fat nodes or path copying make a snapshot O(1) by moving the cost into
  every write: that is (a) and (b) above.
- Tracking the enclosed volume per operation through the divergence theorem is standard in volume-preserving
  remeshing. It works as a monitor, but not as a decision that must equal a particular floating-point sum: that is (e).

## 4. Conclusion

**Closed for the snapshot.**
- The copy is at most 8.6 % of the run, so no replacement of it can save 10 %.
- The exact cheap lever on the sign count, (d), gains nothing.
- Incremental volume, (e), is neither decision-identical near the tolerance nor cheaper, because the passes move
  almost every vertex.

**Parked, an owner option: (a) + (f).**
- An undo log inside `DMesh3`'s edge operations together with component labels kept through them.
- Ceiling: 11-15 % (copy plus union-find) with `RemesherPro` (`FastestRemesh`, and `pro_iter` on bumpy_uniform) on closed meshes of up to
  about 75k triangles, at most 9.5 % elsewhere. The net is lower by the vertex buffer, the logging and the labels'
  maintenance.
- The cost: a change to `DMesh3`, which is shared core code; writes on its hot path; and a correctness argument for
  each edge operation.
- Not started without an owner decision. The first step would be measuring the net of (a) alone on `pro_fastest`.

**Would change the answer:**
- passes that become much cheaper, so that the guard's share grows (measure again with the instrumentation of
  section 2);
- an owner decision to accept different rollback decisions near the tolerance (then (e) with its own tolerance, no
  longer bit-identical).

**Open meshes** pay the sign count in every pass (2.4-6.1 %) although they cannot roll back while they have no closed
component. Caching "no closed component" needs the argument of (f), and is below the bar as well.
