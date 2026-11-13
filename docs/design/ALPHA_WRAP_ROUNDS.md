# Alpha Wrap 3.0 lever (B): round-based parallel carving, guarantee and determinism note

Status: reviewed 2026-11-13 (independent review via the coordinator; its three apply / push fixes and comments folded in), basis of the build (owner's lever choice 2026-11-12, docs/roadmap/alpha-wrap-and-repair.md,
"Lever choice"; the owner required a guarantee argument for (B)). Builds on the AW3 probe (`aw3/B/` on runs/alpha-wrap-3:
`GUARANTEE-NOTE.md`, `rounds_probe.patch`, `rounds_tail.txt`) and the performance study (`aw3study/REPORT.md` on
runs/alpha-wrap-3-fable). Paper references are to Portaneri et al. 2022 as summarized in
[ALPHA_WRAP_PAPER_NOTES.md](ALPHA_WRAP_PAPER_NOTES.md) section 2.

## 1. What changes

Today `MeshAlphaWrap.refine_and_carve_loop` pops one gate (facet i of an inside cell c towards an outside cell n) at a
time from a max-heap keyed by Phi(f), evaluates it (oracle: rule 1 segment query, touch test, rule 2 projection) and
applies it (Delaunay insertion of a Steiner point, or carving c). The main carve is 51 % of the tail's time and fully
serial (study, section 2a: queries, Delaunay and bookkeeping a third each).

(B) processes the queue in **rounds**. A round:

1. **Pop** (serial): up to W gates from the heap, in heap order (W = min(RoundWindow, max(1, queue size /
   RoundQueueDivisor)): a function of the state, section 5). Gates that are stale by
   the existing stamp / label test are dropped as today (they count towards W). The first non-stale gate is admitted
   unconditionally; after it, the heap top is peeked and popped only if its key is at least `RoundKeyBand` times the first
   gate's key (section 3; a NaN key never passes the comparison, so it only enters a round as its first gate).
2. **Evaluate** (parallel, read only): for every gate of the round, against the triangulation as it is at the start of
   the round: the Phi(f) re-check, the dual edge, rule 1, the touch test, rule 2, and for a gate that inserts a point p
   the conflict region of p (the cavity: the cells whose circumsphere strictly contains p, grown to be star-shaped as in
   `Delaunay3.Insert`) with its boundary facets and their outer neighbours, and the TooClose / Failed / Outside checks of
   `Insert`. Each gate's result is written to its own slot; nothing shared is written (section 4.2).
3. **Select** (serial, cheap): walk the round in heap order and keep a gate if none of the cells in its **read set**
   R(g) is in the write set of a gate kept before it in this round; otherwise the gate is **deferred** (pushed back with
   its key and stamp, evaluated again in a later round). The sets:
   - carve: R = {c, n}, W = {c} (the label of c changes; n, an outside cell, is only read, for Phi(f) and the dual
     edge, so two carvings of a round can share it: in the build this was 2/3 of the deferrals with W = {c, n});
   - insertion: R = W = {c, n} + the cavity + the cavity's outer neighbours (their adjacency changes);
   - a gate that writes nothing (Phi(f) no longer above alpha: dropped; changed Phi(f): re-queued; rule 2 found no
     point, or `Insert` would reject the point: the facet stays): R as for its kind, W empty.
   In every case W(g) is a subset of R(g), so the write sets of the kept gates are pairwise disjoint as well.
   The first gate of a round that survives the stale test is always kept (nothing is marked yet). A carve gate whose n
   lies in an earlier kept gate's cavity is deferred through n in R. The labels of new cells are Inside and read nothing.
4. **Apply** (parallel over the kept gates, writes disjoint by step 3): insertions with the cell and vertex ids assigned
   serially in heap order before the parallel step (section 4.3); carvings set label[c] = Outside.
5. **Push** (parallel compute, serial push): the new gates of every kept gate, computed from the state after the round,
   pushed in heap order of the round (the facets of the new cells towards outside cells for an insertion; the facets of
   c's inside neighbours towards c for a carving; a kept gate whose Phi(f) changed goes back under its new key, at its
   place in this order), then the deferred gates.

The loop ends when the heap is empty, as today. Counters, `MaxSteinerPoints` (applied in heap order, as the serial loop checks it: when a
kept gate's rule decides to insert, before `Insert` could reject the point, so a gate whose point `Insert` would reject
counts too; the first such gate at the limit ends the round there, keeping the gates before it, and the loop with
PointLimitReached), the cancellation check (once per
round) and the `touched` / `changedSinceTrial` lists (appended in heap order) keep their meaning.

## 2. Serialization lemma

**Lemma.** Let a round keep the gates g_1, ..., g_k (heap order). Applying them in the round (evaluated against the
state S_0 at the start of the round) gives the same triangulation (as a set of labelled tetrahedra) and the same labels
as the serial loop applying g_1, ..., g_k one after the other, each evaluated against the state S_{j-1} after its
predecessors.

Proof, by induction on j. The evaluation of g_j reads only the cells of R(g_j), their vertices, their circumcenters, the
cached vertex distances (functions of the vertex positions) and the static input (oracle). The earlier gates g_1 ..
g_{j-1} changed only cells in their write sets, which are disjoint from R(g_j) (step 3), and added vertices; so every
cell of R(g_j) exists in S_{j-1} with the same vertices, neighbours, labels and stamp as in S_0. Hence:

- the Phi(f) re-check, the dual edge (circumcenters of c and n), the rule 1 query, the touch test (vertices of c), the
  rule 2 projection and its circumsphere test give the same values;
- for an insertion, the conflict region of p in S_{j-1} is the cavity found in S_0. The cells of S_0 containing p are
  cavity cells (a point of a closed tetrahedron other than a vertex lies strictly inside its circumball), so they are
  unchanged in S_{j-1} and the walk there ends in one of them. From there the search (BFS over InSphere, then the
  star-shape growth across every boundary facet that p does not see strictly from inside) only ever tests cavity cells
  and their outer neighbours, all in R(g_j) and unchanged; so it tests the same cells with the same results and stops at
  the same boundary. (The growth rule is monotone, so the grown region is its least fixed point and as a set does not
  depend on the start cell or the search order.) No other cell of S_{j-1} can join: the search reaches a cell only
  through a tested facet. Consistently, a cavity cell C stays a Delaunay cell of S_{j-1}, the serial result, so its open
  circumball holds no earlier point p_i of the round. The checks of `Insert` (outside the box, a vertex of the cavity removed, the nearest vertex closer than the
  separation: the nearest vertex of p is a vertex of its cavity) read only the cavity and its boundary, so they agree;
- a carving changes label[c] only; c is in no other kept gate's read set.

So g_j's decision and its effect on the cells are the same in both orders, and the states agree after g_j. Cell and
vertex ids differ from the serial run (section 4.3), the tetrahedra and labels do not. QED.

**Corollary.** The round algorithm is the serial algorithm with a different queue discipline: the gates of a round are
popped consecutively, a deferred gate is pushed back, and the new gates of a round are pushed at its end. The serial
loop's correctness never uses the pop order beyond "every queued gate is eventually popped": a gate that is popped
later than in master is re-validated by the stamp / label / Phi(f) test, as every gate already is.

## 3. The guarantees under the round order

Every guarantee of the class doc of `MeshAlphaWrap` and of the pipeline (`SimplifyAlphaWrap`, certification) is listed
with the reason it does not depend on the order (paper notes section 2):

| guarantee | why order-free |
|---|---|
| termination | Lemma 1 / 2 hold per insertion (the point lies inside the empty ball it breaks; its insertion radius is at least min(alpha, offset)), so the packing bound on the number of points holds for any order. Carvings: each changes a cell's label from inside to outside, at most once per cell id and stamp. Deferrals: at most W - 1 per round, and a round with a deferral has a kept gate that writes; a round that keeps no gate has popped only stale gates, each removed for good (still progress, as in the serial loop); every kept gate is a popped gate of the serial algorithm. Finite. |
| closed, oriented | the result is the interface of a labelling of a partition of the box; each step carves whole cells. Per step. |
| strict enclosure (no outside cell touches the input) | a cell that touches the input is never carved: the touch test is part of the gate's evaluation, which the lemma makes exact for the state the gate is applied to. Per step. |
| separation of the Steiner points (min(offset, alpha - offset), up to rounding; as in master not for the rule 2 projection fallbacks, `Counts["projection_fallbacks"]`) | per insertion, from the empty ball of the cell the point breaks in the state it is inserted into (lemma). |
| the derived angle bound sin(theta) > min(alpha, offset) / (2 alpha) | from the separation (per insertion) and the end-state size criterion (next row). |
| offset band [0, alpha + offset] and the size criterion (every final facet has Phi(f) < alpha) | from the end state: the heap is empty, so no traversable facet between an inside and an outside cell remains. This needs the invariant "every facet between an inside and an outside cell with Phi(f) > alpha (`push_gate`'s filter) has a valid gate in the heap or in the current round": an insertion pushes its new cells' facets, a carving pushes c's inside neighbours' facets towards c (step 5, from the state after the round, so a facet changed later in the round is pushed in its final form), a deferred gate goes back. |
| 2-manifold, no self-intersections | made by the pinch refinement and the final manifold step on the final labels, and checked by the result check (exact). Order-free. |
| no contact with the input (raw: vertices on the offset surface; simplified: `Reducer.KeepClearOf`) | per result triangle, exact checks downstream; independent of how the raw wrap was produced. |
| certification of the simplified wrap | runs on the result; unchanged. |

**What is not order-free** (and is measured, not proved): the triangle count, the measured angle distribution (p01, min
angle), the 2.0 angle floors of the quality refinement, and the Hausdorff distances. Largest-Phi-first is a heuristic for
quality, as in Delaunay refinement. The probe (W 256, serial rounds, the six tail meshes) changed raw triangles by
-0.04 .. +0.15 %, p01 by -0.06 .. +13 degrees, and the raw min angle by -0.11 .. +2.5 degrees with one mesh worse
(860049 1.29 -> 1.18, 679615 9.62 -> 7.33); W 1024 was worse (40118 5.01 -> 2.94). Mitigation built in: a **key band**
(a gate enters a round only if its key is at least `RoundKeyBand` times the round's first key, so a round never mixes
the largest facets with much smaller ones), measured against W (section 5). The gate for the merge is the owner's:
guarantees 800 / 800, p01 and min angle of the final output not worse than master on the 800 run.

## 4. Determinism: the result does not depend on the thread count

Claim: for fixed input and parameters (including W and the key band), the output is bit-identical for every
`MaxThreads` (1 = the serial reference of the round schedule) and every run.

### 4.1 The schedule is fixed by the state, not by threads
The gates of a round are the next W of a binary heap whose content and shape depend only on the sequence of pushes and
pops, all serial. Selection (step 3) walks the round in heap order with a mark array. Deferred gates and new gates are
pushed serially in heap order. So the sequence of rounds and their kept sets are functions of the input.

### 4.2 Evaluation writes nothing shared
- Oracle queries (`AlphaWrapOracle`) are read only (their hint is per query).
- Caches: the lazily filled caches (circumcenter `centerCache` / `centerStamp`, `cell_touches_input`'s touch cache,
  the NaN-lazy `vertexDistance`) are never written in a parallel phase, neither in the evaluation (step 2) nor in the
  push (step 5: `push_gate` -> `facet_empty_ball` -> `circumcenter`). Each parallel phase uses non-writing variants
  (read the cache when its stamp matches, else compute locally), and values are written to the caches in a serial or
  one-writer pass (each entry by exactly one task, the same value as a serial fill). A Vector3d entry and its separate
  stamp are not written atomically, so concurrent lazy fills would allow torn reads.
- Point location: `Delaunay3.Locate` uses one shared xorshift state for its stochastic walk and writes `LastWalkSteps`;
  `Insert` sets `lastCell`. The round's conflict search uses a read-only variant with its own walk state, seeded from the
  gate's position in the round, starts at c and writes none of these fields. The
  cavity found as a set does not depend on the walk (section 2); its order (the search order from the cell the walk
  ends in) is a function of the round-start state, p and the seed, the same for every thread count, so the new cell
  order below is fixed too.
- Scratch: every scratch field of `Delaunay3.Insert` has a per-task copy in the round path: `cavity`, `boundary`,
  `boundaryVerts`, `newCells`, `edgeLinks`, `vmark` / `vmarkValue`, `mark` / `markValue` (a small open-addressed set
  per evaluation) and the walk state `rng`.
- Floating point: every value is computed by one thread from the same operands in the same order as the serial
  reference; there is no reduction across threads.

### 4.3 Apply: ids assigned serially, writes disjoint
The evaluation slot of an insertion stores, per boundary facet, everything the new cell needs, all read from S_0: the
three vertex ids (oriented towards p), the outer neighbour and the facet index in that neighbour that faces the cavity.
The apply builds the new cells only from the slot, never from `cv` / `cn` of cavity cells or `FacetTo` on the outer
neighbour: a new cell can be written into a cavity id whose facets are still to be read, and `FacetTo(n, c)` can find
the wrong facet when n borders two cavity cells and one id is reused (`Delaunay3.Insert` allocates before freeing for this
reason). Before the parallel apply, a serial pass over the kept insertions in heap order assigns: the vertex id (append to the
vertex list) and the new cell ids: first the gate's own cavity ids in cavity order, then ids popped from the free stack,
then fresh ids (a cavity of T cells with no interior vertex and E_i interior edges has F = 2T + 2 - 2 E_i boundary
facets; if F < T, the unused cavity ids go onto the free stack in this pass, in cavity order); the arrays
(`Delaunay3` cells, labels, caches) are grown here. The parallel apply of gate g writes only: its cavity cells (reused
ids), its fresh ids, and the neighbour entry of its outer neighbours (in W(g)); writes of different gates are disjoint by
step 3. The edge links between new cells are found per gate (a local table). Who writes the per-cell fields: an id that
a gate (re)uses for a new cell is written only by that gate in the apply (cv, cn, alive = true, mark, stamp incremented
once, as `new_cell` does), so the stamp-keyed caches see the change; an id freed in the round and not reused by its own
gate (a cavity id beyond F) is freed in the serial pass (`free_cell`: alive = false, cell count, free-stack push), and if
a later gate of the same round takes it from the free stack, only that gate writes it in the apply. Cell count and
`lastCell` (the last kept insertion's first new cell) are set in the serial pass. Carvings write label[c] only. A later kept gate
may write a cell that an earlier kept gate only read (a carving of c where c was the outside cell n of no kept gate, as
n is outside; or a cell read by a gate that writes nothing): in heap order that read came first, and no apply reads the
state (insertions build from their plans, carvings write a label), so the parallel apply is unaffected.

### 4.4 Push
New gates are computed in parallel from the state after the round (read only) into per-gate lists and pushed serially in
heap order, then the deferred gates in heap order. The heap after the round is therefore the same for every thread count.

Together: each round maps the state to the next state by a function that does not involve the thread count, so the whole
carve does. The serial reference (MaxThreads = 1) runs the same code in a plain loop. 2^k scaling: no new constant with a
unit is introduced (the key band is a ratio of Phi values, W a count), so `Similarity.All()` invariance holds as before.

Compatibility: `RoundWindow = 0` keeps master's serial loop and its output bit for bit (an internal or public switch, see
section 5); the round loop with W > 0 changes the output bits against master, not the resolution (alpha, offset and the
size criterion are the same).

## 5. Choices for the build (recommendation first)

1. W: fixed 256 (probe: 3.94x / 14.9x round ceilings on 4 / 16 cores, +12 % re-evaluations); 1024 was worse on quality.
   Measured again with the key band; never derived from the core count.
2. Key band: 0.5 as a start, measured on the tail 20 and a normal stratum (quality against master, time).
3. All calls of `refine_and_carve` (main carve, pinch refinement, quality refinement) use the round loop: one code path.
   If the quality refinement's angle floors move, those calls fall back to W = 0 (serial), decided by the measurement.
4. API: `MeshAlphaWrap.MaxThreads` (0 = all cores, 1 = serial reference), as `PartitionedReduction`; the round window as
   an internal setting (tests use W = 0 for master's order). No public break (API validation against 1.0.324).
5. The parallel trial passes (pinch / manifold, second PR) are whole-label passes (`cells_moved_by_manifold_step`:
   pocket removal, manifold step on a copy, stars): they are deterministic functions of the labels, so the parallel
   version must reproduce the serial result bit for bit (per-chunk results merged in a fixed chunk order); no guarantee
   changes and no output change.

## 6. Tests (build PR)

- Determinism: the full wrap with MaxThreads 1 / 2 / 4 / 8 bit-identical (vertices, triangles, Counts that are not
  timings), repeated runs; on a procedural input large enough for rounds with many gates.
- Serialization: for a round schedule, applying the kept gates of each round one after the other through
  `Delaunay3.Insert` gives the same tetrahedra and labels (a test-only replay; the lemma above).
- `Delaunay3` round insertion: `Validate(checkDelaunay: true)` after rounds of disjoint insertions on random point sets,
  equal (as a set of tetrahedra) to serial insertion of the same points.
- Invariance over `Similarity.All()` with rounds on; reuse (Compute twice); W = 0 equals master's output (a golden
  hash from master).
- Result checks on success paths: closed, manifold, no self-intersections, input strictly inside, vertices on the offset
  surface.
- Round edge cases: an insertion with F < T whose freed id another gate reuses in the same round; an outer neighbour
  adjacent to two cavity cells; two kept cavities sharing an edge / a vertex; cospherical growth in a round (lattice
  points); `MaxSteinerPoints` reached mid-round right after a rejected insertion; the parallel push reaching an outside
  cell shared by two gates. The determinism case within 5 s (or `Slow` with a reason).
- Mutation checks: drop a cell from a read set, skip the deferral, push gates before the apply, use the shared walk
  state: the determinism or serialization tests must fail.
- Numbers: tail 20 and a normal stratum on 4 cores (16 if available), and the 800 run on the runner as the gate.
