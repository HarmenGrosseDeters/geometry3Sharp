# Shared hotspots and leads

Part of the [roadmap](../ROADMAP.md).

- **Scope:** two lookup tables used across sessions: measured hotspots that several sessions hit, and open tasks
  with work elsewhere that may already answer them.
- **Status (2026-11-17):** reference tables; check them before profiling a slow exact path or starting an open task
  ([AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md) section 2).

### Open

- `ExactMeshBoolean.coplanar_stack`: the worst-case latency fix (flood fill over the piece adjacency) was not built,
  closed (owner 2026-11-01; CHEAPER_CONSTRUCTIONS.md section 10a). Issue #1047 (42327 with its rotated copy, 18-26 s per
  operation, the time is in the classification) is reduced by PR #1064 (merged 2026-11-17; box prefilter for the coplanar stacks and a memo of L2's
  exact sides, bit-identical, -7 to -14 % on 42327); the further levers are not built under the speed stop.
- `Reducer` `MaxDeviation`: shared BVH queries per collapse were tried and merged in #275 (2026-11-01, 1.03-1.12x,
  bit-identical); the envelope stopped (owner 2026-11-01).
- Leads in the second table: four are answered (wall-thickness sizing `RemeshSizing.FromWallThickness`, CCD stage 2
  #793, `MeshConstraintUtil.PinSharpVertices`, the boolean property tests #229; see the table). The others are untried.
- Shared measurement hazard (#825, closed 2026-11-15 as an environment effect, owner): under dynamic PGO, the .NET default, the same work runs 8 % slower in the median
  and 15-25 % slower in a quarter of the processes (42327's classification). Recent boolean A/Bs run with
  `DOTNET_TieredPGO=0` (#837, #843) or pool many processes per side (#893).
- Runtime watch (#726, merged 2026-11-13; first report #888, closed 2026-11-16 by #960: tail cases are judged by the
  regression rule only): 28 of 35 known extreme cases above the 20 s hard limit
  (18 alpha wrap pipelines of 21.9-77.7 s, 10 booleans up to 480.7 s for 55928). Thread scaling beyond 4 cores: measured by
  the owner (1 / 4 / 12 threads, A/B of #962; #858 closed 2026-11-17, [BOOLEAN_THREAD_SCALING.md](../perf/BOOLEAN_THREAD_SCALING.md)).
- CCD `MeshSweepCollision` (#866 / #940, closed 2026-11-16 as a documented limit (owner)): the 1M-triangle ToI frame takes 18.2-20.5 s parallel on 4 cores after #872
  (about 51 s serial; about 40 s before #872); about half is enumerating the whole-step self edge pairs. Sub-step tree (#945): 29.9-32.7 s serial / 10.6-10.9 s parallel; narrow-phase pre-filter (#940, CCD.md 10.12): 23.5-24.9 s serial / 8.8-10.4 s parallel, still above 20 s serial; the rest is broad-phase work (tree search about 11 s, preparation 3.5 s, surviving pairs 5 s). Owner 2026-11-16: stop here; serial above 20 s on this input is a documented limit.

### Done / history

#### Shared hotspots across sessions

Findings that one session measured and others hit too. Before profiling a slow exact path, check here; route a new
finding through the coordinator so that one session owns the fix and the others measure against it.

| Hotspot | Measured by | Also hit by | Owner of the fix |
|---|---|---|---|
| Exact stage of the adaptive `Orient3d` on nearly coplanar inputs: 88-90 % return 0, 190-250 ns each, coordinate differences within 62 bits in 91-100 % | Boolean performance (#256): 5-7 % of the boolean time | Reducer / Remesher `PreventSelfIntersections` (a tilted plate: 72 % of 99.7M calls exact, about 70 % of the check time, 50.8 s); `IntrTriangle3Triangle3` after #253 (coplanar pairs 4-40x slower) | #256 "kernel B", a 1-limb fast path in `ExactPredicates.orient3d_exact`; the self-intersection session adds a double SAT filter in front, so that fewer calls reach it |
| Filter bounds derived by hand | Indirect predicates (#244 / #250) | Every new floating-point filter (SAT filter, envelope) | `tools/PredicateCompiler`: derive bounds with it where it fits |
| `BigInteger` fallback of the exact predicates and the classification's sign stages: allocation and GC on the slow tail (#711 class: 5-47 s on 8-16 vertex inputs; 55928: 12.4M BigInteger InCircle / Orient stages, nearly half its allocation) | Exact-number study (2026-11-12, branch `runs/exact-numbers`) | Fuzz (#711), the 55928 tail, any exact stage beyond the expansion range | P2: generated monomial kernels on stack limbs (#769, merged 2026-11-15; the #711 class 289 -> 51 s); candidate B, the classification's sign stages (#772, merged 2026-11-14); candidate D, the arrangement's Orient / InCircle beyond the fixed width (#893, merged 2026-11-15, 55928 -12 %) |
| Exact checks in BigInteger rationals at scale: the 2D mesher's oracle spends minutes on 25k-50k-point meshes (meshing itself 0.65 s), mostly in the global Delaunay and boundary-chain checks | Meshing (2026-11-01) | Any oracle or exact verifier on large inputs | Done in #265 (merged 2026-11-01): Shewchuk static filters and a 2-d tree in the oracle; the oracle may use `ExactPredicates`, independence comes from the reference path |
| `ExactMeshBoolean.coplanar_stack`: quadratic, pieces × coplanar triangles (42327: 495M `Orient` calls, 87 % of a 46 s resolve) | Boolean performance diagnosis (2026-11-01) | MeshRepair's exact stage on inputs with heavy coplanar overlap | Counted on the MeshRobustRepair path (2026-11-01, #256 doc 10a): 14.2 s of 71.9 s exact.resolve on the 11 top cases, about 0.4-0.5 % of the 400-set total; only 4 meshes lose 1-5 s each. Not built as a throughput optimization; a worst-case latency fix (flood fill over the piece adjacency) is the owner's decision. Re-measured 2026-11-02 (master c419546, standalone `ExactMeshBoolean` union of the unrotated 42327, 4624 triangles): 20.6 s; 81406 intersecting pairs, 146k points, 777k pieces; classification 14.0 s (68 %, includes `coplanar_stack`), triangulation 3.0 s. 67709 standalone: 2.1 s (its 12.3 s in the 400 run came from load and the stages before). Split measured 2026-11-02 (temporary instrumentation, nothing committed): standalone union of the raw 42327, classification 8.6 s of 12.0 s = `coplanar_stack` 6.16 s (72 %; 777k pieces, 99.3 % with a coplanar source, 164M `inside_coplanar` calls, about 213 per piece: pieces × coplanar triangles) + patch winding 2.04 s (139k patches; the raw mesh is not winding-closed, so generalized winding) + grouping 0.30 s; on the MeshRobustRepair path `coplanar_stack` 5.34 s and the winding rays 2.44 s (124k patches, exactly one ray each, 0 retries, about 20 µs per patch). The lever is the stack search (eg the flood fill over the piece adjacency), not the rays; a worst-case latency fix, the owner's decision. 2026-11-17: the stack search is again 4.7-4.9 s of the 18-26 s of 42327 rotated (#1047); PR #1064 (box prefilter, merged 2026-11-17) takes it to 2.8-3.0 s |
| SafeRounding on rotated (general double) inputs: 72-86 % of a resolve (67709 rotated 18.5 of 21.6 s; 42327 rotated 36.8 of 50.8 s), 18-28 % of 110-172M `Orient3d` calls exact | Boolean performance diagnosis (2026-11-01) | MeshRepair (its benchmark resolves a rotated copy too) | Stage R done; started 2026-11-02 (owner) in the SafeRounding session. Upper bound: kernel B about 0 (the 64-bit `orient3d_int` already admits 99.9-100 % of the exact stages; the exact stage is 8-9 % of the CPU), dropped. Most cases hit `MaxPairTests` (20M) on hopeless open inputs. Being built, bit-identical: a canonical id per vertex instead of a dictionary lookup per `intersects()` (about 20 % of the CPU) and an incremental `intersecting_pairs` without the lock (8-37 % of a resolve); then stopping after the evaluation limit (up to 41-68 % on two cases) if `SmallestRejectedDrift` stays unchanged. **Merged in #327 (2026-11-02):** rounding −35 %, boolean −23 % on the rotated cases, bit-identical (263/263 hashes, 400/400 MeshRepair). Budget (owner 2026-11-02): `MaxPairTests` 20M → 5M (5M = 20M on the clean results) plus a pass-through `RoundingMaxPairTests` on `ExactMeshBoolean` and `MeshRobustRepair`, and `--rounding-max-pair-tests` in g3cli: merged in #335 (2026-11-03); hash gate 263/263 identical, no case at the limit. **Re-measured 2026-11-12** (master 46529be, [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md)): 1-69 % of a boolean on 33 rotated operations, median 23 % (67709 rotated 2.9 of 8.0 s, 42327 rotated 10.2 of 32.5 s); what is left is box-neighbour enumeration on clean results and, on 1368057 / 42327 union, work after the evaluation limit. **2026-11-12:** the passes stop proposing at the evaluation limit (owner, option A of [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md)): rounding −32 %, booleans −7 % on 33 rotated operations, 1368057 union 17.8 → 3.9 s, 33 of 33 hashes identical ([EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md)) |
| `Reducer` `MaxDeviation`: 92-97 % of its time | Phase 5 #2 | Remeshers that must stay near the input | Envelope **stopped (owner decision 2026-11-01)**: it met its stop criterion on these queries (#268, design/ENVELOPE.md §6: about 5× `IsWithin` after the review fixes, 4.4-5.9×, and it accepts only 8-17 % of what `IsWithin` certifies); the code merges as an internal tool (#263, #268). Spikes on `IsWithin` itself (ENVELOPE §6): the check is 90-98 % of a reduction (about 700k calls, 7-73 µs each); caching, one-ring-first search and a hash grid do not help, and exactness changes no decision. Owner decisions: a carried bound per triangle is not built (pays only at coarse tolerances, changes results); **shared BVH queries per collapse** (37-59 % of `IsWithin`'s queries are duplicates, upper bound about 10-20 % of the check time, bit-identical) is being tried in the envelope session, its own PR with an A/B in `ReducerBenchmark` |

#### Open tasks and where their answer may already be

Open tasks further up, and work further down, in the code or in the research lists, that may solve them. Read the right-hand side before starting the task on the left. Each row is a lead to check, not a result: none of these was tried here.

| Open task | Look at first | What it may give, and what it does not |
|---|---|---|
| Open inputs: winding numbers across patches (Phase 2), heuristic without clear gain | Phase 5 #5: Feng & Crane 2024, signed distance from geometry with holes and self-intersections, no repair first | Another way to decide inside and outside for open inputs, in place of tuning the propagation. Feng, Gillespie, Crane 2023 has the integer jumps, but for curves on a surface, so it is a model and not a direct fit for patches in 3D. geometry-central documents a signed heat solver: check its license before reading it |
| Remeshers leave the input surface, and the quality pass after `SimplifyAlphaWrap` (Phase 2) | Phase 5 #2: envelope containment, which its authors built into two surface remeshers. `MeshEnvelope` is the prototype | A guarantee to stay near the input in every local operation, so quality passes cannot trade away fidelity. It does not by itself prevent self-intersections: the per-operation intersection check stays a separate task |
| `Reducer`: `MaxDeviation` takes 92-97 % of the time (Phase 5 #2) | The same item | With the two rows above, and the envelope pipeline under Open research questions, one piece of work serves four tasks. So it comes first in Phase 5 once it is scheduled |
| Remeshing folds walls through each other (Phase 2) | `MeshWallThickness` (exists) for the thickness-aware sizing. Phase 5 #8, continuous collision detection, only if a check of the end state proves not to be enough | Sizing below the local wall thickness uses what we have: done as `RemeshSizing.FromWallThickness` (opt-in; 135363 still open, see [alpha-wrap-and-repair.md](alpha-wrap-and-repair.md)). CCD now exists (`ContinuousCollision`, `MeshSweepCollision`, #793); tried on the remesher in the Phase 2 measurement (#1075, [REMESHER_PHASE2_MEASURED.md](../perf/REMESHER_PHASE2_MEASURED.md), result 3); whether a move can truly pass a wall and end in a state a static check accepts stays open until the flagged contacts are classified |
| Sharp vertices are not pinned (Phase 2) | `MeshCurvature` (exists): angle deficit, handles boundary and bowtie vertices | The measure this task asks for is already there. Done: `MeshConstraintUtil.PinSharpVertices` uses the angle deficit |
| Golden masters across runtimes ([strategy](../design/ACCEPTANCE_STRATEGY.md), section 4) | Open research questions: bit-for-bit determinism | The same problem, seen first in the prism fixtures. What the golden subset finds is material for the question |
| Boolean property tests | Open research questions: metamorphic testing | Same method. Write them so that the generators can serve both. Done: the boolean property tests (#229); the metamorphic catalogue (#783) and its relations S1, C1, C2 (#839) merged 2026-11-13 |

### Open for the future (2026-11-15)

Written 2026-11-15; statuses re-checked 2026-11-17 (the heading keeps its date because other pages link to it).

1. The exact-boolean tail above 20 s (#888, closed): 55928 at 460-480 s in the first report; the levers #881 (allocations) and #893
   (limb kernels) are merged (2026-11-15); #1047 (42327) got #1064 (merged, -7 to -14 %; 19.5 s median for the union, still above 20 s in 1 of 10 processes).
2. The alpha wrap tail above 20 s (#888, closed): 18 pipelines of 21.9-77.7 s in the first report; (B) #774 is merged (2026-11-15).
3. ~~CCD `MeshSweepCollision` on the 1M ToI frame (#866 / #940)~~: closed 2026-11-16 as a documented limit (owner) after #945 and #977 (serial 23.5-24.9 s, parallel 8.8-10.4 s).
4. Dynamic PGO slowdown as a measurement hazard (#825, closed 2026-11-15 as an environment effect, owner).
5. Thread scaling beyond 4 cores: measured by the owner, #858 closed 2026-11-17.
6. Open inputs, inside / outside across patches: Feng & Crane 2024 as a lead, untried.
7. Remeshers near the input: the envelope lead, stopped (owner 2026-11-01); Phase 5 #2 open.
8. Golden masters across runtimes vs bit-for-bit determinism: lead untried.
