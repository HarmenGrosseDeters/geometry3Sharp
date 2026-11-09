# Alpha wrap, repair, Reducer and Remesher

Part of the [roadmap](../ROADMAP.md).

- **Scope:** `MeshAlphaWrap` and the wrap fallback of `MeshRobustRepair` (quality pass, raw-wrap fixes, wrap
  resolution), the volume checks of the repair, the envelope (stage 7), the `Reducer` and the remeshers (degenerate
  triangles, self-intersections, thin walls, feature pinning).
- **Status (2026-11-08):** milestone Alpha Wrap 2.0 started (session session_01AaGT5CSm6ZjSih3jZRg7HJ, see below). Wrap quality (#292), wrap resolution (#280), raw-wrap fixes (#311,
  #317), the volume check (#305) and Reducer A (#324) are merged; the envelope stopped (owner 2026-11-01). The
  remesher items of Phase 2 stay open.
- **Related:** [design/ALPHA_WRAP_QUALITY.md](../design/ALPHA_WRAP_QUALITY.md),
  [ALPHA_WRAP_PAPER_NOTES.md](../design/ALPHA_WRAP_PAPER_NOTES.md), [ENVELOPE.md](../design/ENVELOPE.md),
  [VOLUME_CHECK_SAMPLING.md](../design/VOLUME_CHECK_SAMPLING.md),
  [RESEARCH_REDUCER_REMESHER.md](../design/RESEARCH_REDUCER_REMESHER.md),
  [REDUCER_SPECULATIVE_CERTIFICATION.md](../design/REDUCER_SPECULATIVE_CERTIFICATION.md),
  [SELF_INTERSECTION_PATCH_CERTIFICATE.md](../design/SELF_INTERSECTION_PATCH_CERTIFICATE.md),
  [INSIDE_OUTSIDE_OPEN_INPUTS.md](../design/INSIDE_OUTSIDE_OPEN_INPUTS.md); hotspots in [hotspots.md](hotspots.md).

### Open

#### Alpha Wrap 2.0 (milestone)

**Result and close (owner 2026-11-09): Alpha Wrap 2.0 is done.** Final 800 run (master dfc93fdb, seed 2611, 300/3000,
library defaults, quality pass off, one process at a time on a 4-core machine; raw rows on runs/alpha-wrap-2 f800/,
000ae12e) against the pre-2.0 800 run: guarantees 800/800, no timeouts or crashes; contract 1 proven exceedance
(111959, 1.093, #317 stop rule); 0 triangles < 0.5° and 0 > 170° on every mesh (before 7 and 14676); p01 median 11.34°,
min 10.62° (before 7.62° / 3.49°); raw p01 median 20.11° (before 17.41°), raw >= CGAL on the T1 100; F-score 0.1 %
median 0.9914 (before 0.9911), IoU median 0.9805 (0.9803); final triangles +17 % (paired median). Speed: 611 of 800
within max(5 s, 50 µs × raw triangles), 92 of the 189 others within 10 %, 168 within 25 %; pipeline median 5.83 s, p90
11.88 s, max 97.5 s. Owner decision: close 2.0 with this documented; the speed levers go to **Alpha Wrap 3.0**: in the
189 over budget ~60 % of the time is the wrap itself (intersection oracle 14 %, Delaunay insertion 13 %, touch 12 %,
the two pinch passes 18 %), reduction 17 %, certification 7 %; together with parallelism. The Reducer / Remesher 2.0
hand-over is docs/design/PARTITIONED_REDUCTION_HANDOVER.md (#454).
**Reducer / Remesher 2.0 (owner 2026-11-09):** baseline in #457 (benchmarks/ReducerRemesherBaseline,
docs/design/REDUCER_REMESHER_20.md): topology kept and deterministic everywhere; Reducer 3.2 µs/tri, p01 0.6° on real
meshes, self-intersections created in 9 of 19 clean inputs; Remesher 11-23 µs/tri, degenerate triangles in 17 of 20,
self-intersections in 14 of 19, not bit-identical under 2^k in 4 cases. Targets: T1 Reducer quality / target within the
genus limit, T2 Remesher clean with p01 >= 5°, T3 self-intersection check by default at <= 4x / <= 8x (via #432's
slabs), T5 bit-identical 2^k scaling, T6 a fidelity gate; T4 (Remesher speed 1.3-1.5x) later. Opt-in first; the default
flips only after an 800-mesh run shows nowhere worse and the speed target met. Partitioned reduction stays internal
until T3 is measured. Order: T5, T2, T1, T3, T6, one PR each.
**Alpha Wrap 3.0 (owner 2026-11-09; starts once Exact Boolean 2.0 is closed):** speed and parallelism on top of 2.0.
Starting point (final 800 run, the 189 meshes over budget): pinch passes 18 %, reduction 17 %, intersection oracle 14 %,
Delaunay insertion 13 %, touch test 12 %, certification 7 %. Levers: (1) intersection oracle and touch test through
batched queries on the input tree (#448), cheap prefilters, per-facet caching; (2) Delaunay insertion in spatial
(BRIO / Hilbert) order with a compact tetrahedron layout and fewer allocations; (3) parallelism: evaluate the carving
candidates' gate tests in parallel batches and apply them serially in a fixed order (deterministic), certification in
parallel batches; (4) pinch passes more local (on #429); (5) reduction via PartitionedReducer (#470) and an incremental
tree (shared with Reducer / Remesher 2.x); (6) sharp features via dual contouring (owner question 2026-11-09): the
library already has manifold DC (mesh_generators/DualContouring.cs, Schaefer et al. 2007, QEF vertex per patch, Hermite
data projected onto the input) and WindingContouring (DC on the winding number, robust to soups, with a
self-intersection check); measure sharp-edge Chamfer and edge F-score (the option-1 metrics) on the 20 T1 meshes for
the wrap, WindingContouring, and a wrap post-pass that moves boundary vertices toward the DC QEF points under exact
validity checks (closed, manifold, no self-intersection, angle bounds). Process: a Sonnet session measures a per-phase profile, an upper bound per
lever, a short literature check (papers only, never CGAL code) and proposes targets (e.g. 95 % of the 800 within budget,
guarantees and angles unchanged, deterministic); the owner sets them; one PR per lever, measured with the 800 harness on
the runner; Opus only for the review of the parallel determinism. Together with the Exact Boolean 3.0 idea (parallel
and memory): both use "evaluate in parallel, apply serially".

**Alpha Wrap 3.0, sharp-feature probe (lever 6, measured 2026-11-09, benchmarks/SharpFeatureProbe, no library change).** 20 T1 meshes + 6 procedural CAD solids, medians vs A (current pipeline):
(B) dual contouring on the winding number at A's triangle count: edge Chamfer 0.22x (19/20 better), edge F 0.1 % 0.61 -> 0.98, crease normal error 14.9 -> 2.8 deg, but min angle 0.09 deg and 17765 triangles > 170 deg over the 20, no offset (it lies on the input), 1 of 20 self-intersecting, 3x time (13.9 s + size search);
(C) vertex post-pass toward the QEF points, all exact checks kept (C1 offset miter, C2 input crease, C3 = C1 before the simplification): Chamfer 0.87x / 0.68x / 0.86x, edge F 0.1 % 0.75-0.76, crease normal 0.58x, 20/20 clean, angle floors and offset band unchanged, +15-35 % time: recovers 17-42 % of B's Chamfer gain, about 40 % of its edge F; (D1) alpha / 2 as the bound of a locally smaller alpha (option 4): Chamfer 0.75x at 1.7x triangles and 2.5x time (9 meshes); the QEF-guided Steiner idea (D2) was not built (needs library changes; C3 is its cheap proxy). Side finding: the simplified wrap grazes the input on 20/20 meshes (the raw wrap never does).
How others solve it: Ju et al. 2002 (DC), Schaefer et al. 2007 (manifold DC), Kobbelt et al. 2001 (feature-sensitive extraction: feature vertex + edge flips), recent mitered offset meshing (arXiv 2412.15564, Feature-Preserving Offset Mesh Generation from Topology-Adapted Octrees, CGF 2023) which keep sharp features by DC / point-to-plane offsets, not by an alpha wrap; the alpha wrap paper itself names sharp features as a limit. Recommendation in the PR / report.

**Reducer / Remesher 2.x, speed (idea, owner asked 2026-11-09; not scheduled):** measured so far: with
PreventSelfIntersections the Reducer pays the per-collapse self-intersection query (PartitionedReducer #470: 2-3x on
60-100k triangles, nothing below 20k); the Remesher spends 70-73 % in serial edge operations with the move check, the
rest in smoothing / projection (12.6x plain at the median). Levers by expected gain: (1) a local check against an
incrementally updated tree instead of rebuilds (both, core-count independent); (2) the orientation prefilter of #419
(Devillers-Guigue) in front of the triangle-triangle test; (3) the Remesher projection through NearestPointBatch (#448,
3.7-7.2x, bit-identical; the caller switch after Lévy phase 2); (4) slabs for the Remesher's edge operations (<= 2.1x on
4 cores, large meshes only, a big build). First step: a per-operation profile of the plain Remesher (T4). Synergy with
Alpha Wrap 3.0 (an incremental tree helps all three).
**Status 2026-11-09 (T4, [REDUCER_REMESHER_20.md](../design/REDUCER_REMESHER_20.md) section 12):** profiled; the flip test of the plain Remesher was the hotspot (40 % of the run, 256 bytes
allocated per edge, because the closed-mesh hint was never set): fixed in PR 1, plain Remesher 1.49x (x1) / 1.29x (x2), bit-identical, `si` +3-5 %. Levers (1)-(3) measured and not built: the grid for the edge checks was incremental
already, the move check rebuilds because every vertex moves, #419's filter is in place, and `NearestPointBatch` is slower than the parallel loop on coherent vertex order. (4), slabs, not started: the plain profile has no serial share above 25 % left.
**T4 closed 2026-11-09:** the remaining levers were measured and none pays (section 12.4: warm-started projection
1-5 % overall, pair-test order, flip pre-test, guard arrays, `NearestPointBatch`, tree refit <= 2.5 % of `si`); only
PR 1 (#484) is built. Left open: the `RejectInvertingPasses` snapshot (6-19 % of the run) needs component labels that
survive the edge operations, i.e. a correctness argument (research). Crease-sliver probe (#492, docs and harness
only): 87 % of the slivers have two or more pinned vertices at the span ends and junctions of noisy crease networks;
unpinning free ends gives p01 2.54° -> 3.41° (slivers -3 %, fidelity equal); dropping crease fragments shorter than
4-16 edges removes 58 % of the slivers but drops F 0.67 -> 0.5 and doubles the outward Hausdorff (outside the T6 gate).
**Parked (owner 2026-11-09):** feature-curve operators (Wang-style) and merging noisy crease fragments, a research
item; not built until a variant is shown to keep the T6 fidelity gate.

**Owner decision 2026-11-08: quality and speed.** On the 800-mesh Thingi10K sample of MeshRepairBenchmark
(`BENCH_REPAIR=wrap`, 300/3000), the full pipeline (raw `MeshAlphaWrap`, `SimplifyAlphaWrap`, `AlphaWrapQualityPass`):
- guarantees (closed, manifold, free of self-intersections, offset band) and fidelity (F-score, Hausdorff, band) not
  worse than master;
- 0 triangles < 0.5° and 0 > 170° per mesh in the final output; the pinch caps (102793, 118678, 1505037) gone;
  **p01 (owner 2026-11-08):** the raw wrap reaches CGAL's p01 (the ~2° gap of #317; behaviour numbers only, CGAL code
  is never read); the final simplified output has p01 >= 10° as the median over the 800 sample (floor confirmed
  after the full-800 data); the CGAL comparison for the raw target runs on the 100 meshes of the T1 list only
  (median comparison), as a fixed reference that later runs do not repeat (owner 2026-11-08). Q1 (A+B+C, quality
  pass off, 800): final p01 median 11.53° (min 10.79°, master 7.62°), 0 triangles < 0.5° or > 170°, raw p01 median
  20.11°, F-score / IoU medians not worse, triangles +16.5 %; hd_back mixed per mesh (79-81 worse > 5 %, 124-126 better, medians equal), caused by B's Φ gating (paper
  Definition 2; CGAL shows equal or larger input→result distances on the probed meshes), accepted as paper behaviour
  (owner 2026-11-08; the circumradius gates would cost the raw p01 gain); T2 (100 T1 meshes, warm): 4a (#417)
  identical, wrap.manifold 32 → 7 s; 4b (local pinch trials) 94/100 identical, 6 marginal changes, pinch 159 → 68 s;
  speed budget passed by 24 → 27 → 35 of 100 (median 1.14 → 1.06× budget). Owner 2026-11-08: 4b becomes a PR,
  default on; parallel reducer option (A) is built as a deterministic prototype behind a switch, PR decision from
  its T1 measurement; (A) result on T1 100 (4 cores): budget met 46 → 72 of 100, time/budget median 1.02 → 0.84
  (p90 1.44 → 1.27), same guarantees and floors, deterministic across thread counts (fixed 8 slabs), not
  bit-identical to the serial reducer; owner 2026-11-08: PR, default on; target (a) met (CGAL 5.6 black box, same 100 T1 meshes: raw p01 median 18.41° vs ours
  19.45°, ours >= CGAL on 73/100, worst mesh 10.12° vs 15.49°, triangles > 170° 5 vs 0); sharp features (owner 2026-11-08): option 1, the Reducer's feature
  quadrics (FeatureAngle sweep 30/45/60° on 20 T1 meshes, full fidelity): no gain (sharp-edge Chamfer and edge F
  within 0.2 %, +2-4 % triangles, band slightly worse; raw rows on runs/alpha-wrap-features), closed; option 3, crease-aware Steiner points: upper bound on 100 meshes says no (convex creases
  already 0.56δ from the input edges; concave creases 4.7δ, set by the empty α-ball, about 0.45δ better than the
  α limit (α+δ)/sin(θ/2) − α; only a locally smaller α, option 4, could change that), not built; #405 (A, the
  simplification guard), #413 (quality pass off by default) and #422 (bit-identical Reducer speedups, T3:
  reduce 0.906, pipeline 0.975 per mesh) merged 2026-11-08; concave corner recovery is research, not in 2.0;
- speed (owner 2026-11-08, revised after WP1): per mesh, wall time <= max(5 s, 50 µs × raw-wrap triangles), i.e. 5 s
  up to 100k raw triangles and 2x today's per-triangle speed above that; measured in a warm process (after a warm-up
  run; cold times, about 2-2.5 s JIT more, are reported but not gated), alone on the 4-core cloud machine.
A stop rule reports the cases the known levers cannot reach; the owner decides on exceptions. Work packages: (1) baseline,
phase profile, upper bounds; (2) quality (pinch caps, slivers, caps; bug-or-inherent against the paper first); (3) speed;
(4) control run. Session session_01AaGT5CSm6ZjSih3jZRg7HJ (started 2026-11-08), draft #404 (`BENCH_REPAIR=wrappipe`). Long benchmark runs go to a runner session on Haiku
(session_01AaGT5AcURraKzWZfZRtW6r, owner 2026-11-08: runs only, no code; raw results on the data branch
`runs/alpha-wrap-2`, never merged).

**WP1 interim (2026-11-08, 55 meshes, serial):** pipeline median 10.8 s, p90 19.4, max 34.4; > 5 s on 53 of 55 (the
target needs about 2.2x at the median, 6-7x at the tail). Shares: simplify 39 % (Reducer 29 %), quality pass 27 %, raw
wrap 34 % (carve loop 23 %). Quality: the raw wrap is good (p01 median 17.3°); the bad angles come from the
simplification (976 triangles > 170° on 52 of 55; p01 median 7.8°), whose Reducer guard `MinCollapseTriangleQuality`
0.05 allows caps up to about 175°. Upper bound on 12 meshes: guard 0.3 gives 0 triangles > 170°, p01 median 10.8°,
+4 % triangles, 0.92x time, guarantees and band unchanged (F-score pending).

**WP1 report (2026-11-08):** raw wrap alone median 3.7 s, > 5 s on 13 of 55 (about 100 µs per raw triangle, up to
330k); known levers bring the warm median to about 7 s, the tail needs 3-7x, hence the size-dependent budget. Guard sweep
on 30 meshes: 0.3 gives 0 triangles > 170°, p01 median 10.7°, +16 % triangles, F-score within 0.1 %, 30/30 guarantees.
Raw wrap vs CGAL (12 meshes, behaviour only): p01 17.8 vs 19.6; prototypes: the paper's Phi(f) traversal (Def. 2)
18.3, Phi plus a Chew / Boissonnat-Oudot quality refinement 20.9 (+4 % triangles, +12 % time). Pinch caps: inherent to
our manifold extension (large outside cells), not a bug; lever: a two-sided manifold step. Plan, one PR each: A guard
0.3 + needle handling; B Phi + refinement; C two-sided manifold step (prototype, stop rule); WP3 constant factors.

**Serial Reducer profile (2026-11-08, owner request before any parallel reducer).** `SimplifyWithin` on 5 raw wraps
(26k-327k triangles: Thingi10K 789071, 102793, 100331, 497978, 65584; the `wrappipe` mode of MeshRepairBenchmark from
#404 on the unmerged bench branch with A, B and C, plus temporary timers that were not committed; warm, alone): 56-70 µs per collapse; the reduction is about 33 % of the
pipeline. Two methods: timers per section with counters, and ablations (one component off; the self-intersection check
refused no collapse on these wraps, so every ablation gave the same mesh, hashes checked). Shares of the reduction:

| part | share | notes |
|---|---|---|
| self-intersection check, total | 56-63 % | grid traversal + grid update + new triangles 33-40 %, candidates 22-28 % (exact tri-tri tests 9-17 %), pairs of new triangles 2-9 %; ~45 candidates and ~75 exact tests per collapse, 0 hits |
| bowtie check | 4-9 % | the raw wraps have no bowties |
| core: fold/quality guards, quadrics, queue, edit, setup | 35-38 % | ~27 µs per collapse; fold/quality 10-14 %, queue 6-8 % (stale pops 2 %) |
| GC pauses | ~7 % | 1.2 GB allocated on the 327k wrap |

79 % of the evaluated edges are refused by the fold/quality guard, which already runs before the self-intersection
check. 55-60 % of the evaluations are re-evaluations after a neighbour's collapse; 89 % of those are refused again
for the same reason (6-13 %; skipping them is not bit-identical). No published per-collapse figure of meshoptimizer
or another QEM code could be verified (GitHub returned 504), so none is quoted.

| lever | upper bound | status |
|---|---|---|
| 1 grid: cells hold (id, box), reference-cell rule instead of stamps, no key arrays | ~30-35 % | built bit-identical, **slower** (1.02-1.15x): not used. The cost is the cell lookups; base cells of 3 instead of 1.5 mean edge lengths take that lever (bit-identical) |
| 2 bowtie states per vertex | 4-9 % | built (#422) |
| 3 self-intersection check without allocations per collapse | ~5 % | built (#422) |
| 4 cheaper pre-reject before the exact tri-tri test | 9-17 %, realistic ≤ 5-8 % | not built (owner 2026-11-08: ≤ 2 % of the pipeline for a new error bound and an exact-code review) |
| 5 lazy / version-stamped queue | 2 % (the queue updates in place) | not built |
| 6 quadrics as struct of arrays | 3-5 % | not built |
| 7 early-out of the self-intersection query | 0 % (no hits) | n/a |
| 8 repeated certification | n/a: no per-collapse certification in this path; the final certify (5-10 % of the simplification) runs once | n/a |

Levers 2 + 3 + cells of 3 edge lengths (same setup, old and new binary alternating, median of 4 runs each; the
output hashes compared): the reduction 0.87-0.91x on the 4 larger wraps (102793 0.87, 100331 0.91,
497978 0.88, 65584 0.89), allocations -30 %, the same meshes bit for bit. Cells of 4.5 and 6 edge lengths are not
better; 0.75 is 1.09-1.22x.

#### Other open items

- **`external/cgal_wrap.cpp`** (behaviour comparison with CGAL's alpha wrap; links CGAL, GPL; never distributed
  compiled): stays while we compare against CGAL; remove it in its own PR once no comparison needs it (owner
  2026-11-08).

- **Raw alpha wrap** (now part of Alpha Wrap 2.0): the remaining pinch caps (102793, 118678, 1505037) and the p01 angle gap to CGAL's
  alpha wrap (about 2°); the ideas are recorded under [item 2](#wrap-resolution-raw-wrap-and-volume-checks-slot-order-item-2).
- **Quality pass after `SimplifyAlphaWrap`:** the Phase 2 checkbox is still open, but stage 7b merged the quality
  pass in #292 (2026-11-02), ON by default in the wrap fallback (status unclear, check: probably done).
- **Reducer / Remesher 2.0: done 2026-11-09** (baseline, T5, T2, T1, T3, T6; results, measured limits and open items T4 / feature-curve operators in
  [design/REDUCER_REMESHER_20.md](../design/REDUCER_REMESHER_20.md) sections 6 to 11; no default changed). The milestone idea as first written (owner 2026-11-08, upper bounds first):
  (1) bring the parallel slab reduction of (A) (#432; deterministic, independent of the core count) and the #405
  quality guard (compare with the worst triangle around the edge) to the general `Reducer` as options, since today
  both act only inside `SimplifyWithin`; (2) check the slab partition for the Remesher, above all with
  `PreventSelfIntersections` (6-10x cost today); (3) make `PreventSelfIntersections` cheaper with the batch queries
  and the mesh memory-layout results of the spatial-structure profile; (4) re-evaluate the defaults
  (`MinTriangleQuality` 0, `PreventSelfIntersections` off, a flip guard and its known regressions); (5) settle the
  "status unclear" items below (the Remesher session, shared BVH queries per collapse). The Alpha Wrap session writes a
  hand-over note on (A) for this before it is archived; the #422 speedups already apply to every Reducer user.
- **Remeshers, open Phase 2 items:** degenerate triangles (`MinTriangleQuality` is opt-in, library default 0);
  self-intersections (`PreventSelfIntersections` is opt-in, about 6-10x the time; what is left is the number of
  candidate pairs); walls folding through each other far above the wall thickness (135363 with the sizing: 1.1M
  triangles, 22613 pairs; with the check as well more than 90 minutes). Leads in
  [hotspots.md](hotspots.md#open-tasks-and-where-their-answer-may-already-be).
- **Reducer / Remesher upper bounds** (2026-11-02): B (the union-box query in the Remesher, 4-6 %) not built; D (the
  permission-grid accept filter) not built (owner 2026-11-02). Shared BVH queries per collapse were "being tried in
  the envelope session" (status unclear, check).
- **Remesher session** on the cost of `PreventSelfIntersections` (started 2026-11-02) (status unclear, check: the
  coplanar 2D shortcut and the clearance filter were measured and closed).
- **Envelope** (Phase 5 #2): stopped (owner decision 2026-11-01); the code is an internal tool. Open in Phase 5: the
  paper's algorithm ([research.md](research.md#phase-5-candidates-from-recent-research)).
- **Issue-tracker follow-ups** ([platform.md](platform.md#release-when-its-done)): P2 (Disk iteration, parked as
  research), S2 and S3 open, pinned and documented; the regressions a remesher flip-guard default would cause; the
  P1 collapse.
- **Inside / outside for open inputs** (Phase 5 #5): stopped (#331); the wrap fallback stays the answer.
- The slow `exact.resolve` on small inputs (67709, 42327) went to the boolean-performance session
  ([exact-boolean.md](exact-boolean.md#other-open-items)).

### Done / history

#### Stage 7: envelope containment

Phase 5 #2 (Wang et al. 2020; `fast-envelope` is MIT; `MeshEnvelope` is the prototype). One piece of work serves four tasks: quality passes that stay near the input, the alpha-wrap simplification, the `Reducer` `MaxDeviation` check (92-97 % of its time) and the envelope-guaranteed pipeline. It was planned before the alpha-wrap stage (7b), which keeps failing on the trade-off between quality and fidelity; since the stop, 7b uses `IsWithin` for that (see 7b). **Stopped 2026-11-01 (owner decision)** after its stop criterion was met; the code merges as an internal tool (#263, #268), see the hotspot row on `MaxDeviation`.

Started 2026-11-01 in the indirect-predicates session: design [design/ENVELOPE.md](../design/ENVELOPE.md) (#258), approved by the owner: double inputs by a filter plus exponent alignment, polyhedra after the paper's Prop. 3.1 (eps/sqrt(3); `MeshEnvelope`'s 0.8/0.6 shape only as a proven option), an internal class first (`MeshEnvelope` replaced in its own PR once the BigInteger-clipping oracle agrees and it is within 5x of `IsWithin`), one PR per step. The envelope does not replace the per-operation self-intersection checks

Tier: L. Parallel: 1 large session, after stage 5

#### Stage 7b: alpha wrap quality

Owner decision on #199. (History: #199's branch passed no gate and was not merged.)

**Done 2026-11-02: merged in #292** (34 wrap cases on master with #311: p01 3.75 → 8.08°, holdout 3.34 → 7.59°; share > 170° 0.64 → 0.05 %; time 1.21× / 1.07×; G1 accepted by the owner with its documented misses).

Then in the same session the bundled Reducer + Remesher upper bounds (2026-11-02): C, speculative parallel `MaxDeviation` certification, built bit-identical but slower than master (bumpy 0.001: 31 → 46 s; the queue reorders after each collapse, window hits 53-95 %, and the existing inner parallelism already uses about 2.2 cores): measured and closed (#323 docs); A, one union-box query per collapse, about 21 % of `SimplifyWithin`, bit-identical; B (the same in the Remesher) 4-6 %, not built; D, a permission-grid accept filter, sound but not bit-identical by guarantee, pays only at coarse tolerances (0.01: about 40 % of the total; 0.001: a loss): not built (owner 2026-11-02).

Then, in the same session, the upper bound and literature step of Phase 5 #5 (inside / outside for open and broken inputs).

**Earlier status:** the new quality pass (#292, G1: D0.1 + cap-flip exemption + guarded cap split) is ON by default in the wrap fallback (`AlphaWrapQualityPass`); 34 wrap cases: p01 3.66 → 7.94° (holdout 3.35 → 7.62°), share > 170° 0.63 → 0.05 %, (c1)/(c2) 18/18, noise gate 4/18 (holdout 1/16) as documented misses; time ratio being re-measured after the rejection cache (projected 1.22).

**Owner decision 2026-11-01:** 7b no longer waits for stage 7. The fidelity bound that the envelope was to provide comes from the existing `IsWithin` / `MaxDeviation` check (certified, conservative, 46-213 µs per query; exactness changes no decision there), not from the exact envelope: its ε/√3 polyhedra accept only 8-17 % of what `IsWithin` certifies (#268), so they would block exactly the quality operations 7b needs.

Order: (1) the MeshRepair wrap-resolution session first, since it changes 7b's input; (2) then 7b as a bounded diagnosis of the failing local patches of the hard-feature policy, no threshold tuning, starting with whether the trials on `fix/alpha-wrap-simplification-quality` already guarded their operations by a deviation check; (3) only then a quality pass with every operation guarded by `IsWithin` against the wrap tolerance. The exact envelope serves tests only as a sufficient check

Tier: L. Parallel: 1 large session, after the wrap-resolution session **Owner 2026-11-02:** acceptance (c) replaced by per-mesh floors (no triangle < 0.5°, global min / max not worse than the simplified wrap, share > 170° ≤ 0.1 %) plus relative improvement and the reported distribution (literature: Shewchuk 2002, Babuška–Aziz 1976, Morvan–Thibert 2004, Hu et al. 2017); G1 (D0.1 + cap-flip exemption + guarded cap split) accepted after one last variant, (d) misses documented, ON by default in the wrap fallback with a switch. Follow-up: degenerate triangles of the raw alpha wrap itself (eg 59196, where the simplification falls back to the unsimplified wrap).

**Owner decision 2026-11-08 (Alpha Wrap 2.0):** the quality pass is off by default (`MeshRobustRepair.AlphaWrapQualityPass
= false`, #413, after the simplification's quality guard #405): with the guard it adds little (p01 about 0.15° better,
no triangle below 0.5° or above 170° either way) at about 30 % of the pipeline's time.

#### Remesher (slot order item 1)

1. Remesher done (medium): not replaced.

#### Wrap resolution, raw wrap and volume checks (slot order item 2)

2. **MeshRepair wrap-resolution session** (medium, owner 2026-11-01; owner 2026-11-01 later: it starts when the slot of #256 frees, without waiting for the rest of stage R, whose 1b and UTM tolerance touch only SafeRounding): in the MeshRepair-400 worst case, `wrap.simplify` takes 24 % of the time on 17 meshes, because tiny inputs (450-650 triangles) are wrapped at 180k-415k triangles (resolution from the bounding box, not the input); couple alpha / offset to the input, start coarser, or speed up the simplifier, under the F-score and fidelity gates (data: `benchmarks/MeshRepairBenchmark/cases/robust_phases_2026-11-01.csv`). It precedes stage 7b (alpha wrap quality). The volume checks (20 % of the time, all 400 meshes): the redundant check before an exact resolve is gone (#273, bit-identical, −21 % on the 20 measured meshes).

Owner decision 2026-11-01, low priority, after stage R and the wrap session: a tighter sample region for the IoU check (only 16 % of samples hit the union; upper bound about 10 % of the 400-set time; measurement and options in [design/VOLUME_CHECK_SAMPLING.md](../design/VOLUME_CHECK_SAMPLING.md), parked 2026-11-02). It must provably contain the union of both solids, so the estimator stays unbiased with lower variance; results are no longer bit-identical, the quality thresholds stay; its own PR with an A/B; **merged 2026-11-02 in #305 (option A, cells for closed meshes with exact winding, B as the fallback): 400 set 0 verdict flips, repair path identical, volume phases −7.8 %, cell checks −31 % (38 % instead of 17 % of samples in the union).

Follow-up (owner 2026-11-02): one octree per component box measured and closed (upper bound 1.7 %; [design/VOLUME_CHECK_SAMPLING.md](../design/VOLUME_CHECK_SAMPLING.md) §5); the 42327 classification split done (diagnosis: coplanar_stack 72 %)**; **started 2026-11-02 (owner, ahead of 7b's merge, file-disjoint: MeshAlphaWrap / Delaunay3 only):** degenerate triangles of the raw alpha wrap (eg 59196): diagnosis done (needles from rule-2 projection of the clamped dual-edge point, all 21 triangles < 0.5° in 59196/163738; caps > 170° from make_manifold); fix A (project the unclamped circumcentre, exact guard) merged in #311; then B (owner): B3, refinement at the non-manifold sites, as a prototype with a stop rule (≥ 50 % of the caps gone on the 800 sample, otherwise docs only; B1/B2 dropped after the probe); the manifold-step caps (12–15 triangles > 170° on 4–6 of 19 meshes), measured on a large Thingi10K subset, bug-or-inherent verdict against the paper first, fix in its own PR; B3 met its stop rule (800 sample, 300/3000: caps −86 %, band max 8.76 → 1.11, 0 guarantee failures, time ×1.22 median) and is merged in #317 (2026-11-02), `RefineNonManifold` default on (owner 2026-11-02); the raw-wrap session is archived; first look against CGAL's alpha wrap (30 meshes + 19 wrap cases, 300/3000): guarantees, band and Hausdorff equal, CGAL +10–15 % triangles at about 4–6× our time; CGAL has 0 triangles < 0.5° / > 170° (ours 1/2 with B3, master 1/7) and a p01 angle about 2° higher; parked: the remaining pinch caps (102793, 118678, 1505037; all on cells the manifold step moves after B3 found no separation-preserving candidate, exposing facets up to 40 α where CGAL's output stays at 2–4 α; ideas: a third candidate on the segment to the nearest input point, refining the inner neighbours, the smallest-radius candidate in the manifold step) and the p01 gap; the slow `exact.resolve` on small inputs (67709, 42327) goes to the boolean-performance session as a diagnosis.

Owner decision 2026-11-08 (far-part bug, Boolean session, branched from #420): when an input's parts lie far apart compared to their size, `MeshRobustRepair` uses the largest component's diagonal for its internal scales (opt-in reference diagonal for `MeshPostRepairCleanup`, public default unchanged), and when the exact result is degenerate only by the all-triangles diagonal, `Apply()` returns **false** with the exact mesh and the reason instead of accepting an 8-triangle wrap blob (IoU 0.039). Separately, **measure first, no code**: count on the thingi cohort how often the wrap / solidify fallbacks are accepted with a poor volume IoU (they have no volume check by design); a guard is decided from that data.

#### Wrap resolution, 7b, Reducer and Remesher sessions (slot order item 4)

4.

**Started 2026-11-02:** the wrap-resolution session (#280 merged: C1 patch bound, wrap.simplify −69 %, results identical) continues with **stage 7b** (owner); **CLI / MCP docs** (stage 8, medium) run in their own session in the slot of the envelope session (stack #250-#268 merged, session archived). After 7b, in the same session (owner 2026-11-02): one bundled Reducer measurement, upper bounds only, from [design/RESEARCH_REDUCER_REMESHER.md](../design/RESEARCH_REDUCER_REMESHER.md) (#286): `MaxDeviation` by speculative parallel certification (bit-identical) and an adaptive permission-grid accept filter; the self-intersection check (73 % in `TriangleHashGrid.Query` on the wrap meshes) by one union-box query per collapse.

Owner 2026-11-02: the same union-box lever is measured for `Remesher.PreventSelfIntersections` too (6-10x after #298; every other lever closed there). Building needs the owner's go. The research session reads the full texts now (hosts allowed by the owner).

Reducer A merged in #324 (2026-11-02): −11 to −37 % on the reducer cases, `SimplifyWithin` −10.6 %. Also started 2026-11-02, above the session limit by owner exception: a medium **Remesher session** for the cost of `PreventSelfIntersections` (about 17x; levers: the coplanar 2D shortcut, unblocked since #249/#256, and a clearance filter), profile and upper bounds first. A review-only session only when the stacks and the meshing PRs wait for review at the same time.

#### Phase 2 items: alpha wrap, remeshers, feature detection, NotCleanReason

- [x] Alpha wrapping (`MeshAlphaWrap`, Portaneri et al. 2022): closed, manifold, intersection-free wrap with an offset
  around any input (soups, open meshes, point clouds), on an own exact 3D Delaunay triangulation (`Delaunay3`).
  Benchmark on Thingi10K: `BENCH_REPAIR=wrap`, `analyze_wrap.py` (comparison with the solidify fallback).
  - [ ] Quality pass after `MeshRobustRepair.SimplifyAlphaWrap`: the simplification keeps the fidelity (F-score, edges,
    normals equal at a quarter of the triangles) but the 1%-worst min angle drops from 16.2 to 3.8 degrees (17 wrap
    results of Thingi10K, benchmarks/MeshRepairBenchmark README, re-evaluation); eg a short remesh pass with
    `Remesher.MinTriangleQuality` on the simplified wrap, or a min angle bound in the simplification. Trials so far: `fix/alpha-wrap-simplification-quality`, none passes every gate (unverified, see Next, stage 7)
- [ ] Remeshers must not leave degenerate triangles. `Remesher` and `RemesherPro` (`g3cli remesh` settings) leave
  degenerate triangles or self-intersections on 18 of 20 clean Thingi10K meshes, 99% of the triangles with q < 0.1 at
  feature constraints (`benchmarks/RemeshBenchmark`). `RemesherPro` (8 x `RemeshIteration` and a full pass without
  smoothing) leaves a zero-area triangle in the band of `TerrainSafetyTests.SafetyLine_AlongSurface_BandIsOneCleanPiece`,
  where `Remesher` leaves none. Opt-in: `Remesher.MinTriangleQuality` (also `RemesherPro`; curve vertices smoothed
  along their curve, guarded vertex moves, flips of poor pairs, no splits or collapses that create flat triangles or
  flip normals; `benchmarks/RemeshQualityBenchmark`). Default decided with the fidelity metrics: `g3cli remesh` uses 0.2 (on
  Thingi10K all fidelity means equal or better, single meshes and coarse primitives worse in places, zero-area triangles in 0 instead of 17 results, 1.5x the time), the
  library default stays 0 (upstream behaviour; thin walls far below the edge length get worse with it). Open: the
  self-intersections (next item).
- [ ] Remeshers produce self-intersections: with the `g3cli remesh` settings without `--min-quality` 19 of 20 Thingi10K
  results have self-intersecting triangle pairs (19768 pairs, the inputs 1522), mostly from valence flips and collapses
  (no geometric check by default). `MinTriangleQuality` (0.2, the `g3cli` default now) reduces them (17 of 20 results,
  3105 pairs; far fewer are created during the passes)
  but does not prevent them, and 9 of 150 procedural primitives end with a few more pairs than without it, all at the
  bottom rim where the flat cap meets the side (projection moves across the rim, no normal flips): repro cases in
  `benchmarks/RemeshQualityBenchmark`: taper10_x0.15 (12 -> 16 pairs), taper16_x0.1 (0 -> 12), taper32_x0.1 (0 -> 12),
  taper32_x0.6 (0 -> 8), cone16_x0.45 (0 -> 10), cone24_x0.15 (0 -> 6), cone24_x0.3 (0 -> 10), cone24_x0.6 (0 -> 31),
  cone32_x0.6 (0 -> 8) (and cone12_x0.6, cone16_x0.6, flattened to zero volume without `RejectInvertingPasses`).
  Opt-in: `Remesher.PreventSelfIntersections` (also `RemesherPro`): an exact check of every collapse, flip, split and
  smoothing / projection move against a triangle grid. Every result whose input is free of self-intersections is
  free of them (all 192 procedural cases, 15 of 20 Thingi10K meshes; the other 5 inputs intersect themselves), at
  about 6-10x the time after #261 (measured on master b7fbc52); the axis-plane case of `TriangleTriangleFilter` takes
  the gears 15 % and the primitives 5 % lower, bit-identical (`benchmarks/SelfIntersectionBenchmark`). The 2D shortcut for coplanar neighbours (#261 took it, under 0.2 % left) and
  a clearance filter (at most 5-10 %) were measured and closed there; what is left is the number of candidate pairs.
  Open: the thin walls, where the check blocks the folds but the result
  ends far from the input (next item).
- [ ] Remeshing far above the wall thickness folds walls through each other and can turn a mesh inside out, with
  and without `MinTriangleQuality`: Thingi10K 135363 (a 0.8 plate with thin-walled tubes, walls 0.75, target = mean
  edge 11.6) ends inside-out in both variants (signed volume 4448 -> -4706 without, -4021 with the option; the sign
  changes at pass 10 / 8); the thin-walled tubes of `benchmarks/RemeshQualityBenchmark` (wall 0.05 / 0.1, targets
  0.5..1.6) end with dozens to hundreds of self-intersecting pairs in every case and 2%..700% of the input volume,
  some inside-out (`Remesher`: 3 of 24 without the option, none with it; `RemesherPro`: tube16_w0.05_t1.6 only
  without, tube32_w0.05_t1.6 only with it), the sign of the volume changing back and forth over the passes.
  Done: `Remesher.RejectInvertingPasses` (default on, also `RemesherPro`) rolls back a pass that turns a closed
  component inside out and counts it (`InvertingPassesRejected`); `g3cli remesh` reports the rolled-back passes and
  warns when the edge length is more than 4 times the estimated wall thickness. Done (opt-in):
  `Remesher.TargetEdgeLengthF` with `RemeshSizing.FromWallThickness` (target edge length the local wall thickness;
  `g3cli remesh --thickness-sizing 1`): the 24 tubes keep their volume (0.99..1.00 instead of 0.16..7.04) and surface
  (F 0.1 % 0.94..1.00 instead of 0.01..0.13); with `PreventSelfIntersections` as well all 24 are clean, at 12k..50k
  triangles and 6..550 s each. `PreventSelfIntersections` alone gives no pairs but keeps 0.54..4.8 of the volume
  (`benchmarks/RemeshQualityBenchmark`, Thin walls). Open: 135363 with the sizing has volume 1.00 and F 0.98 but 1.1M
  triangles, 22613 pairs (in a diagnostic run nearly all flat slivers in axis planes; whether stage R false
  positives are among them is not checked) and 146k near-degenerate triangles; with the check as well it ran for more
  than 90 minutes; the check alone is clean but keeps 1.31 of the volume. Depends on stage R and the
  faster self-intersection check (the shared triangle-triangle prefilter session), then possibly envelope
  containment (stage 7).
- [x] Feature detection misses vertex features: the apex of a smooth cone (no crease edges) is not pinned and is rounded
  off by the remeshers: cones with 12 or more slices (their side edges are no creases) 1.4..2.8 target edge lengths
  from the input; with `MinTriangleQuality` 1.4..3.2, more in 12 of these 24 cases of `benchmarks/RemeshQualityBenchmark`
  (its guards keep a few vertices near the apex up to an edge off the surface). Cones with up to 10 slices keep the
  apex as a junction of creases. Done: `MeshConstraintUtil.PinSharpVertices` (opt-in, after
  `PreserveBoundaryAndFeatures`) pins interior vertices with an absolute angle deficit of at least 75 degrees that
  have no curve constraint; `g3cli remesh` uses it by default (`--sharp-vertex-angle 75`, 0 = off). The cones end 0.20..0.66 instead of 1.37..3.15
  target from the input (mean Hausdorff of the 150 primitives 0.341 -> 0.115); on Thingi10K it pins 1 vertex in 20
  meshes (39460: Hausdorff 0.78 -> 0.33 target), the other 19 are unchanged (`benchmarks/RemeshQualityBenchmark`,
  PinSharpVertices).
- [x] `MeshRobustRepair.NotCleanReason` reported only the first failed check: "degenerate triangles" hid the
  self-intersections of most remeshed meshes, and the inside-out result of 135363 without `MinTriangleQuality`. Done in
  #159: `MeshRobustRepair.FindDefects` / `NotCleanReasons` report every failed check (`NotCleanReason` keeps the first),
  and the benchmarks write them (eg RemeshBenchmark `not_clean`).
