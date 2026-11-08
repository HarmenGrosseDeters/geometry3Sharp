# Shared hotspots and leads

Part of the [roadmap](../ROADMAP.md).

- **Scope:** two lookup tables used across sessions: measured hotspots that several sessions hit, and open tasks
  with work elsewhere that may already answer them.
- **Status:** reference tables; check them before profiling a slow exact path or starting an open task
  ([AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md) section 2).

### Open

- `ExactMeshBoolean.coplanar_stack`: the worst-case latency fix (flood fill over the piece adjacency) is the owner's
  decision.
- `Reducer` `MaxDeviation`: shared BVH queries per collapse were being tried in the envelope session (status
  unclear, check).
- Every lead in the second table is untried.

### Done / history

#### Shared hotspots across sessions

Findings that one session measured and others hit too. Before profiling a slow exact path, check here; route a new
finding through the coordinator so that one session owns the fix and the others measure against it.

| Hotspot | Measured by | Also hit by | Owner of the fix |
|---|---|---|---|
| Exact stage of the adaptive `Orient3d` on nearly coplanar inputs: 88-90 % return 0, 190-250 ns each, coordinate differences within 62 bits in 91-100 % | Boolean performance (#256): 5-7 % of the boolean time | Reducer / Remesher `PreventSelfIntersections` (a tilted plate: 72 % of 99.7M calls exact, about 70 % of the check time, 50.8 s); `IntrTriangle3Triangle3` after #253 (coplanar pairs 4-40x slower) | #256 "kernel B", a 1-limb fast path in `ExactPredicates.orient3d_exact`; the self-intersection session adds a double SAT filter in front, so that fewer calls reach it |
| Filter bounds derived by hand | Indirect predicates (#244 / #250) | Every new floating-point filter (SAT filter, envelope) | `tools/PredicateCompiler`: derive bounds with it where it fits |
| Exact checks in BigInteger rationals at scale: the 2D mesher's oracle spends minutes on 25k-50k-point meshes (meshing itself 0.65 s), mostly in the global Delaunay and boundary-chain checks | Meshing (2026-11-01) | Any oracle or exact verifier on large inputs | Done in #265 (merged 2026-11-01): Shewchuk static filters and a 2-d tree in the oracle; the oracle may use `ExactPredicates`, independence comes from the reference path |
| `ExactMeshBoolean.coplanar_stack`: quadratic, pieces × coplanar triangles (42327: 495M `Orient` calls, 87 % of a 46 s resolve) | Boolean performance diagnosis (2026-11-01) | MeshRepair's exact stage on inputs with heavy coplanar overlap | Counted on the MeshRobustRepair path (2026-11-01, #256 doc 10a): 14.2 s of 71.9 s exact.resolve on the 11 top cases, about 0.4-0.5 % of the 400-set total; only 4 meshes lose 1-5 s each. Not built as a throughput optimization; a worst-case latency fix (flood fill over the piece adjacency) is the owner's decision. Re-measured 2026-11-02 (master c419546, standalone `ExactMeshBoolean` union of the unrotated 42327, 4624 triangles): 20.6 s; 81406 intersecting pairs, 146k points, 777k pieces; classification 14.0 s (68 %, includes `coplanar_stack`), triangulation 3.0 s. 67709 standalone: 2.1 s (its 12.3 s in the 400 run came from load and the stages before). Split measured 2026-11-02 (temporary instrumentation, nothing committed): standalone union of the raw 42327, classification 8.6 s of 12.0 s = `coplanar_stack` 6.16 s (72 %; 777k pieces, 99.3 % with a coplanar source, 164M `inside_coplanar` calls, about 213 per piece: pieces × coplanar triangles) + patch winding 2.04 s (139k patches; the raw mesh is not winding-closed, so generalized winding) + grouping 0.30 s; on the MeshRobustRepair path `coplanar_stack` 5.34 s and the winding rays 2.44 s (124k patches, exactly one ray each, 0 retries, about 20 µs per patch). The lever is the stack search (eg the flood fill over the piece adjacency), not the rays; a worst-case latency fix, the owner's decision |
| SafeRounding on rotated (general double) inputs: 72-86 % of a resolve (67709 rotated 18.5 of 21.6 s; 42327 rotated 36.8 of 50.8 s), 18-28 % of 110-172M `Orient3d` calls exact | Boolean performance diagnosis (2026-11-01) | MeshRepair (its benchmark resolves a rotated copy too) | Stage R done; started 2026-11-02 (owner) in the SafeRounding session. Upper bound: kernel B about 0 (the 64-bit `orient3d_int` already admits 99.9-100 % of the exact stages; the exact stage is 8-9 % of the CPU), dropped. Most cases hit `MaxPairTests` (20M) on hopeless open inputs. Being built, bit-identical: a canonical id per vertex instead of a dictionary lookup per `intersects()` (about 20 % of the CPU) and an incremental `intersecting_pairs` without the lock (8-37 % of a resolve); then stopping after the evaluation limit (up to 41-68 % on two cases) if `SmallestRejectedDrift` stays unchanged. **Merged in #327 (2026-11-02):** rounding −35 %, boolean −23 % on the rotated cases, bit-identical (263/263 hashes, 400/400 MeshRepair). Budget (owner 2026-11-02): `MaxPairTests` 20M → 5M (5M = 20M on the clean results) plus a pass-through `RoundingMaxPairTests` on `ExactMeshBoolean` and `MeshRobustRepair`, and `--rounding-max-pair-tests` in g3cli: merged in #335 (2026-11-03); hash gate 263/263 identical, no case at the limit |
| `Reducer` `MaxDeviation`: 92-97 % of its time | Phase 5 #2 | Remeshers that must stay near the input | Envelope **stopped (owner decision 2026-11-01)**: it met its stop criterion on these queries (#268, design/ENVELOPE.md §6: about 5× `IsWithin` after the review fixes, 4.4-5.9×, and it accepts only 8-17 % of what `IsWithin` certifies); the code merges as an internal tool (#263, #268). Spikes on `IsWithin` itself (ENVELOPE §6): the check is 90-98 % of a reduction (about 700k calls, 7-73 µs each); caching, one-ring-first search and a hash grid do not help, and exactness changes no decision. Owner decisions: a carried bound per triangle is not built (pays only at coarse tolerances, changes results); **shared BVH queries per collapse** (37-59 % of `IsWithin`'s queries are duplicates, upper bound about 10-20 % of the check time, bit-identical) is being tried in the envelope session, its own PR with an A/B in `ReducerBenchmark` |

#### Open tasks and where their answer may already be

Open tasks further up, and work further down, in the code or in the research lists, that may solve them. Read the right-hand side before starting the task on the left. Each row is a lead to check, not a result: none of these was tried here.

| Open task | Look at first | What it may give, and what it does not |
|---|---|---|
| Open inputs: winding numbers across patches (Phase 2), heuristic without clear gain | Phase 5 #5: Feng & Crane 2024, signed distance from geometry with holes and self-intersections, no repair first | Another way to decide inside and outside for open inputs, in place of tuning the propagation. Feng, Gillespie, Crane 2023 has the integer jumps, but for curves on a surface, so it is a model and not a direct fit for patches in 3D. geometry-central documents a signed heat solver: check its license before reading it |
| Remeshers leave the input surface, and the quality pass after `SimplifyAlphaWrap` (Phase 2) | Phase 5 #2: envelope containment, which its authors built into two surface remeshers. `MeshEnvelope` is the prototype | A guarantee to stay near the input in every local operation, so quality passes cannot trade away fidelity. It does not by itself prevent self-intersections: the per-operation intersection check stays a separate task |
| `Reducer`: `MaxDeviation` takes 92-97 % of the time (Phase 5 #2) | The same item | With the two rows above, and the envelope pipeline under Open research questions, one piece of work serves four tasks. So it comes first in Phase 5 once it is scheduled |
| Remeshing folds walls through each other (Phase 2) | `MeshWallThickness` (exists) for the thickness-aware sizing. Phase 5 #8, continuous collision detection, only if a check of the end state proves not to be enough | Sizing below the local wall thickness uses what we have. Whether a move that passes through a wall can end in a state a static check accepts is to be measured first |
| Sharp vertices are not pinned (Phase 2) | `MeshCurvature` (exists): angle deficit, handles boundary and bowtie vertices | The measure this task asks for is already there |
| Golden masters across runtimes ([strategy](../design/ACCEPTANCE_STRATEGY.md), section 4) | Open research questions: bit-for-bit determinism | The same problem, seen first in the prism fixtures. What the golden subset finds is material for the question |
| Boolean property tests (below) | Open research questions: metamorphic testing | Same method. Write them so that the generators can serve both |
