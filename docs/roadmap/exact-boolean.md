# Exact booleans

Part of the [roadmap](../ROADMAP.md).

- **Scope:** `ExactMeshBoolean` / `ExactMeshArrangement`, SafeRounding, boolean performance (fixed-width points,
  predicate compiler, indirect predicates, cheaper constructions), the Exact Boolean 2.0 milestone, external corpora
  (manifold, Thingi10K, EMBER-style pairs), EMBER and the mesh-kernel paper, allocations, GPU and SIMD.
- **Status (2026-11-08):** milestone "ExactMeshBoolean 1.0" reached on 2026-11-01. Milestone "Exact Boolean 2.0"
  active (owner 2026-11-07).
- **Related:** [perf/EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md),
  [design/SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md),
  [design/BOOLEAN_PROPERTY_TESTS.md](../design/BOOLEAN_PROPERTY_TESTS.md),
  [design/FIXED_WIDTH_EXACT_POINTS.md](../design/FIXED_WIDTH_EXACT_POINTS.md),
  [design/INDIRECT_PREDICATES.md](../design/INDIRECT_PREDICATES.md),
  [design/CHEAPER_CONSTRUCTIONS.md](../design/CHEAPER_CONSTRUCTIONS.md),
  [design/PAIR_SEARCH.md](../design/PAIR_SEARCH.md); measured hotspots in [hotspots.md](hotspots.md); the research
  question on 3D snap rounding in [research.md](research.md#open-research-questions).

### Open

#### Exact Boolean 2.0 (milestone)

**Exact Boolean 2.0 (milestone, owner 2026-11-07).** Every operation of the #385 Thingi10K harness (union, intersection, difference; B rotated and translated) at most 5 s of Compute wall time on the cloud machine.
**Speed target revised (owner 2026-11-08):** per operation at most max(5 s, 20 µs × arrangement pieces) of Compute
wall time (an acceptance criterion of the benchmark, which records the piece count; the Lévy phase-1 samples showed
that the arrangement size predicts the time far better than the input size, log-log correlation 0.87 vs 0.75). With
all levers this leaves 11 ops on 7 small, heavily self-intersecting models (113685, 1059579, 105693, 107543, 107545,
107554, 113906) as documented exceptions (#428, `benchmarks/LevyClassification/results/exceptions_levers.md`).
Lévy phase 1 result (#428): no propagation now (bound 10-25 % on a minority, 2-4 ops); phase 2 after the merge queue
is one PR with the parallel fast-winding tree build (bit-identical; classification -29 to -42 % on tree-bound open
ops); the AddPoint fix measured only -2 % of classification (the earlier 29-57 % trace share counted lock spinning),
so it is optional; an Antipodal-style open-mesh GWN stays a later option (owner decision, patent check).

**`DMeshAABBTree3` profile (owner 2026-11-08, queued; after the Lévy phase-2 PR with the parallel fast-winding build
has merged):** a Sonnet session profiles the tree across its main users (repair, MaxDeviation in the Reducer, the
boolean classification's fast-winding cache with its O(n log n) HashSet build, alpha wrap) and gives an upper bound
per lever (build, winding cache, queries) before any change; bit-identical results, deterministic parallel builds.
The same session includes `WindingNumberOctree` (the adaptive octree of `MeshWindingNumberSolidify`, the repair's
solidify fallback, `MeshWindingNumberBoolean` and `WindingContouring`; it evaluates winding numbers at cell
corners), `DualContouring` in its sparse mode (fed by the octree's node values; its per-cell `long`-keyed
dictionaries); the Reducer's `TriangleHashGrid` is already examined (#422: hierarchical, cell size 3.0, rebuilt
layouts slower; at most 3-5 % of the wrap pipeline left), so it is not repeated; the order follows
where the profiles spend the time (owner 2026-11-08).

**Exact Boolean 3.0 (idea, owner 2026-11-08; after 2.0, not scheduled):** tighten the speed target again with two
levers that 2.0 does not use: (1) parallel algorithms beyond the current per-phase parallel loops — the cloud
machines have 4 cores, but the trend is to many more, so the arrangement, the classification (e.g. the Lévy-style
propagation or the open-mesh GWN, which were not worth it serially) and rounding should be re-measured for scaling
over core counts (deterministic by construction: fixed partitions, ordered merges, no floating-point reduction
across threads; exact code needs the review and the allow-list entry); (2) memory: footprint and allocation of the
exact points and the arrangement (a memory profile of the large inputs, 0.6-2.7 M triangles, first). Starts with an
upper bound per lever on the 2.0 benchmark set.
- Closed inputs, self-intersection free or self-intersecting: a clean result (closed, manifold, free of self-intersections, no degenerate triangles or bowties). Self-intersecting inputs are read as solids by winding number (inside = winding >= 1).
- Inputs of unknown status: classified first.
- Open inputs: no degenerate triangles and no crash; closedness is not required.
Baseline (#385 at cd7fb8ab, closed selfint = 0): 81 of 16 239 operations unclean, Boolean time median 0.17 s, p95 4.5 s, max 282 s. Work packages: (1) data and a phase profile of the slow tail with upper bounds; (2) root causes and bugfixes for the unclean results; (3) the rounding projection filter (#385 spikes, 20-64 % of rounding time); (4) EMBER ideas 2-4 and allocations, as the data justifies; (5) a control run. Session session_01AaGT5CTqL86zQ1wj56oXAq. Reachability unknown; the stop rule reports the cases the known levers cannot reach.

**Owner decisions 2026-11-07:**
- Input defects: the criterion is "the result has no defect the input does not already have" (B1 bowties, 47 ops, and D2 degenerate inputs, 11 ops, are not library defects).
- Hopeless repairs (L3, 119829/119830): a work-unit budget with an honest "not fully repaired" status, shown never to trigger on today's clean cases.
- 519511, and 1517923 if the levers fall short: documented exceptions to the 5 s target. The architecture assessment (EMBER, octree BSPs, Campen-Kobbelt, Cherchi 2022, Manifold, Trueform, Lévy 2024) found no candidate that produces doubles with a no-intersection guarantee; 5 s on 519511 is unlikely under any architecture because rounding a 520k-triangle result alone costs 3-5 s. No rewrite; L1 and the rounding-search levers survive any architecture.
- Contacts (self-touching solids): keep the contract (clean = clean in memory, as Manifold) and report a contact count (`ExactMeshBoolean.Contacts`); contacts created by rounding (M3) are defects (#398).
- SIMD: measured only; vectorized filters at most 2-4 %; a 4-wide BVH only after the algorithmic traversal fixes.

**WP1 result (4 cores):** 16 ops > 5 s in the selfint = 0 cohort (owner data: 31 on the owner machine); rounding 37 % of their time, mostly the full self-intersection search of the result (box-tree traversal up to 76 % of rounding). Other cohorts (owner data, ops > 5 s): self-intersecting 284, unknown 274, open 736; classification is the second lever there (Lévy 2024-style propagation across intersection curves, BSD-3 Geogram).

**WP2 triage of the 81 unclean ops:** B1 47 (input bowties), D2 11 (input degenerate), B2 3 (M3, fixed in #398), D1 20 (SafeRounding's additive drift budget exhausted in clusters; probe of tighter drift accounting next). The 80 empty unions are inside-out inputs (legitimate).

**D1 order (owner 2026-11-08):** the tighter drift accounting is stated as a lemma in the design note and checked
with Z3 (pilot 2, [research.md](research.md#smt-checks-with-z3)) before it is built.

**Lévy-style classification (owner 2026-11-08, queued):** for the self-intersecting, unknown and open cohorts;
phase 1 (data, profile, upper bound, Lévy 2024 / Geogram BSD-3, design note; no library code until the merge queue
clears). Started and paused the same day (owner: wait); its session is archived, the hand-over note is on branch
`claude/levy-classification-phase1` (docs/design/LEVY_CLASSIFICATION_HANDOVER.md). Restarted 2026-11-08 (owner): a fresh session
continues phase 1 now (data, design, benchmarks only); the library code PR (phase 2) waits until the exact-boolean
merge queue behind the #403 mutation gate has cleared.

**Final A/B of the speed levers (runner, 2026-11-08, 42 models x 3 ops, bit-identical):** master 342 s, ops > 5 s 20;
+#409 -13 %; +#412 -22 %; +#406 +#419 -41 % (rounding -68 %), ops > 5 s 6 (519511 x3, 1517923 union / intersection,
519507 union). Raw rows on runs/boolean2-combined (89f2943).

**thingi-pairs runs (owner 2026-11-08):** the EMBER-protocol class (#418) runs in full (1000 pairs plus the 100k-1M
size class; ours and Manifold; CGAL EPECK only on a fixed 100-pair sample, owner 2026-11-08) twice on a Haiku runner: a baseline on master right after #418 merges, and the
final run at the end of Boolean 2.0 together with the control run (WP5). Results as summary tables; raw rows on the
runner's data branch.

**Merge queue (owner 2026-11-08):** one combined mutation gate for #397, #398, #406, #409, #412, #419, #420, #424,
#425, #426 on an integration branch (Boolean session) once #403 is merged and the master baselines are seeded; then
the PRs merge one by one with normal CI. Open library items after the queue: L3 work-unit budget, the
ExactPredicates throw near ±1e308, a re-measurement of the remaining ops over 5 s (1517923, 519507).

**PRs:** #397 (projection filter, rounding -16 %, bit-identical), #398 (M3 + contact count), #403 (rounding mutation shard split; the full shard ran past the 350-min job limit).

#### Other open items

- **Default-hashed `long` keys** (found 2026-11-08, low priority): `ExactPlanarSimplify`'s flip-queue
  `HashSet<long>` used the default `long` hash (a ^ b), which collides for dense point ids; after the DG prefilter
  (#419) simplify on 519511 union doubled (fix: `EdgeKeyComparer`, own PR from master, Boolean session). Audit the
  other default-hashed `long` sets and dictionaries outside the exact boolean (terrain, geodesics,
  `WindingContouring`, `DualContouring`'s cell dictionaries) with a dense-key measurement before changing any;
  done in #431 (2026-11-08).
  Audit implementation and measurements: [LongHashAudit](../../benchmarks/LongHashAudit/README.md); packed tables
  use the existing comparer, linear indices and scalar-only `containerOf` keep the default hash.
- **Thingi10K full-run findings** (draft #385): the 29 unclean operations on clean closed inputs and M1 become one
  bugfix task with procedural reproductions once the full run is in (work package 2 of the milestone); 49 empty
  results to check; the report should stratify by input class. Details under
  [External boolean corpora](#external-boolean-corpora-2026-11-04).
- **manifold corpus findings** (2026-11-04): M1 (`hull-body - hull-mask`, 2 self-intersections left after
  SafeRounding on clean inputs); M2 (`Generic_Twin_7081` union takes 20 s); M3 (RoundingClean true but
  `NotCleanReason` reports self-intersections: clarify which is right and align them).
- **`thingi-pairs` class in `BooleanComparison`** (local task, owner 2026-11-04): a pinned list of 1000 pairs after
  the EMBER protocol, a size class of 100k-1M triangles, the volume identities and `NotCleanReason` as the oracle.
- **EMBER levers** (order owner 2026-11-04): re-evaluate after the full Thingi10K run; rank 2 (skip self-pairs of
  results certified free of self-intersections), 3 and 4 against the open ideas of the perf doc; 1 and 5 only as an
  owner decision on a second, opt-in kernel. A patent check before building its plane-based kernel.
- **Boolean allocations** (owner 2026-11-04, not scheduled): bytes per phase, an upper bound with a very large gen0,
  a PR only if that shows >= 10 % on large inputs. `System.Buffers`' ArrayPool would be a new netstandard2.0
  dependency, an owner decision.
- **Mesh-kernel ideas** (not scheduled): a `MeshKernel` operation (Phase 5 candidate), an exact 3D LP feasibility
  solver, exact convex polyhedron cutting, small filters, the procedural star-shaped test corpus.
- **Boolean performance leftovers:** planes in fixed width (section 12 of the fixed-width design; CSR dropped as a
  step of its own, a GC trace on cherchi decides later); the generator reused for the envelope predicates (status
  unclear, check: the envelope stopped on 2026-11-01); the slow `exact.resolve` on small inputs (67709, 42327),
  handed over from the raw-wrap work as a diagnosis (status unclear, check).
- **Exact arithmetic without `BigInteger`** (Phase 2 item, perf doc section 3): branches `claude/int256-spike` and
  `claude/collinear-shortcut`. Exact code, so it goes to the owner. Keep both branches.
- **`coplanar_stack` worst-case latency** (a flood fill over the piece adjacency): the owner's decision; numbers in
  [hotspots.md](hotspots.md#shared-hotspots-across-sessions).
- **Open inputs, winding numbers propagated across patches** (Phase 2): heuristic, no clear gain, not merged (branch
  `claude/exact-boolean-gwn-propagation`). Leads in [hotspots.md](hotspots.md#open-tasks-and-where-their-answer-may-already-be);
  the related Phase 5 #5 spike stopped (#331).
- **Exact coplanar triangle intersection:** the 2D clipping of `IntrTriangle2Triangle2` is unchanged; exact output
  coordinates stay open.
- **SafeRounding:** a patch-wise B is parked behind an off option (first measure whether a patch fits back); A's
  library code stays on `claude/snap-rounding-A-archive`.
- **GPU** (asked 2026-11-04): not now; if ever, ILGPU in an optional add-on for the pair search only. **SIMD** for
  the `Orient3d` filter was tried (about 2 %, [research.md](research.md#later)).

### Done / history

#### Stage B: boolean performance

Continues, at the owner's request.

Done: `AddPoint` shards (#222).

Next, in order: fixed-width exact points, 4 limbs (#223: cherchi / cgal −8 %, Thingi10K −11 %, n-ary −26 %, all hashes identical); then the **predicate compiler on fixed-width inputs**, stacked on #223 and started without waiting for its mutation runs: regenerate the arrangement's `InCircle` / `Orient` from the generator of #221 with the exact bit bounds of the 4-limb points, so that the `BigInteger` fallback (about half of the calls with the grid-fitted bounds of #221) mostly disappears; the fallback rate is a reported metric. The generator stays an offline tool with a checked-in, reviewable output and an up-to-date test, not a Roslyn source generator, because the bounds review needs the generated code in the diff. The grid-fitted `ArrInCircle` of #221 is not merged. Later: planes / CSR (section 12 of the fixed-width design), and the generator reused for the envelope predicates (stage 7). Hand-over and ranked candidates: [perf/EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md). Exact code: bounds review and mutation runs on every PR.

**Status 2026-11-01:** #223 (fixed-width points) and #248 (pair search by a self-join of the box tree, cgal + Thingi10K −7.9 %) wait for their mutation runs; the compiler (#244) and the indirect predicates (#246 / #250 / #257, LPI / TPI filter 47-86 ns against 3-7 µs to construct, no expansion stage) are stacked on them. CSR is dropped as a step of its own (1-2 %); a GC trace on cherchi decides later. Cheaper exact constructions merged in #256 (2026-11-01; cgal + Thingi10K −5.5 %; coplanar_stack not built, owner); the session is archived. #246 (indirect predicates step a) merges next; its stack #250 → #257 → #258 → #263 → #268 follows as internal code (envelope stopped). On 2026-11-01 the owner chose not to wait for PR mutation runs of #246, #251 and #256: a full run on master after the merges is their gate and the baseline, and a shard below its threshold becomes a follow-up fix

Tier: L. Parallel: 1 large session

#### Stage R: SafeRounding

Its own session, kept apart from the performance work (performance must stay bit-identical; this changes results on purpose). (1) Fix: SafeRounding moves exact vertices of pieces below its degenerate threshold on clean input (union volume 17.999992370605316 instead of 17.99999237060547), pinned as a skipped test in `ExactBooleanPropertyFindings` (#229). (2) A recommendation on design question 1b of [design/BOOLEAN_PROPERTY_TESTS.md](../design/BOOLEAN_PROPERTY_TESTS.md): repair input that is already degenerate or not. (3) A bounded evaluation of the research item "Practical 3D snap rounding with guarantees" below, with Thingi10K: a concept, no implementation. (4) Fix `ExactTriangleIntersection.IntersectsBeyondShared`: for coplanar neighbours sharing one vertex where one is a sliver, the projection axis comes from a floating-point cross product that rounds to 0, so a false intersection is reported (and inherited by `MeshRepairMetrics.SelfIntersections`); choose the axis exactly. Repro from the remesher session (both triangles in y = 0, ids (0,1,2) / (3,1,4)); this gives the deferred exact coplanar clipping task a real caller.

**Status 2026-11-01:** (4) merged in #249 (bit-identical on the boolean hashes); `IntrTriangle3Triangle3` exact for coplanar and nearly parallel pairs, merged in #253; (1) as the displacement bound D′ (edits refused beyond 1e-5·diag), merged in #251 on 2026-11-01: **milestone "ExactMeshBoolean 1.0" reached** (#223, #244, #248, #249, #251, #253). 1b (strip zip) merged; the UTM tolerance (ulp term, vertex removal, strict mode) merged in #287 on 2026-11-02. The pass-through API merged in #294 (2026-11-02); the CLI exposes the options.

Next: the snap-rounding experiment (options A, then B) in a fresh session, from the handoff in [design/SAFE_ROUNDING.md §6](../design/SAFE_ROUNDING.md#6-handoff-for-the-snap-rounding-experiment-2026-11-02); (3) concept in [design/SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md)

**Snap-rounding experiment (owner 2026-11-02):** prototypes A (extended Milenkovic–Sacks) and B (Valque-Lazard, drift-bounded) behind off-by-default options; go / no-go fixed in advance: B default-on only if it raises the clean share on the Thingi10K self-unions measurably within the drift and Hausdorff bounds at acceptable time; A only for a remainder only A fixes; otherwise measured and closed. First a bugfix for SafeRounding's 2^k invariance (absolute thresholds in MeshRoundingRepair).

**Verdict 2026-11-02 (#313, owner): measured and closed.** B not default-on (Thingi10K self-unions clean 188 → 209 within the drift bound, but 9 of the 21 new ones beyond the Hausdorff bound on open inputs; time median ×4.7, p95 ×66); A has 0 cases only it fixes. #313 (merged 2026-11-02) adds the §8 write-up, the benchmark harness (B as a tool) and the separation certificate; A's library code stays on `claude/snap-rounding-A-archive`. Invariance bugfix #316 (88 → 0 differing clusters, R22 fixed). Parked: a patch-wise B behind an off option (patch resolve ≈ 1/20 of a full SafeRounding per round; first measure whether a patch fits back).

Tier: L. Parallel: 1 large session, when a large slot is free (after meshing or B)

#### Exact coplanar triangle intersection (open task from the takeover)

- [x] **Exact coplanar triangle intersection** (no longer deferred: `DMeshAABBTree3.TestIntersection` / `FindAllIntersections`
  and `MeshQueries.TrianglesIntersection` call it). `IntrTriangle3Triangle3` decides coplanar, nearly parallel and sliver pairs with
  `ExactTriangleIntersection`, and `Find()` builds the contact from exactly decided pieces, rounding only the crossing points, so
  `Test()` and `Find()` agree (row R, item 2b, #253). The 2D clipping of `IntrTriangle2Triangle2` is unchanged; exact output
  coordinates stay open.

#### Phase 2 items on exact booleans

- [x] `ExactMeshBoolean` / `ExactMeshArrangement`: exact arrangement of two meshes (integer grid, intersection points
  as canonical rational points of three planes, exact constrained Delaunay retriangulation), patches classified by
  exact ray parity, coplanar overlaps by orientation. About 6 s for two meshes of 200k triangles each.
- [x] Self-intersecting, nested and duplicated inputs: exact winding numbers per patch (signed ray crossings,
  stacks of coincident triangles); generalized winding number for open inputs; touching solids split into
  manifold sheets; `Resolve(mesh)` for one mesh. Benchmark on Thingi10K: `BENCH_REPAIR=exact`, `analyze_exact.py`.
  Open inputs give consistently oriented results also where an open boundary lies on an edge that other pieces
  meet (no inside/outside choice fits all pieces there): pieces that run such an edge the same way are not joined,
  the result is open along it (`ConflictEdges`).
- [ ] Open inputs: generalized winding numbers propagated across patches (integer jumps across patches, "same value"
  for the sides that face each other around an edge, merged in order of increasing difference of the estimates
  while they agree within a tolerance) instead of one independent estimate per patch. It only moves the cracks
  where patches cross the missing surface of an open input; the orientation is consistent without it. Synthetic
  sweep (open box / box without two faces / holed sphere against 1200 random boxes, 3 operations): tolerance 0.5
  4 cases with fewer crack edges and 2 with more, tolerance 0.3 6 and 0. Heuristic, not merged; experiment on branch
  `claude/exact-boolean-gwn-propagation`. Thingi10K, `BENCH_REPAIR=exact` on the 195 open meshes of the 400 set
  (tolerance 0.5, against the assembly fix alone): boundary edges 303564 → 272575 in all, but 128 of the 585 results
  have fewer and 136 more, closed results 128 → 127, and one more timeout. No clear gain.
- [x] Iterated booleans with coplanar faces: new vertices inside flat regions and on straight edges between them
  are removed exactly and the regions retriangulated (`ExactPlanarSimplify`); the 10x10x10 cube grid (1999
  booleans) no longer grows exponentially.
- [x] More than two meshes in one operation: n-ary union / intersection / difference and any predicate on the
  winding numbers of all inputs (Zhou et al. 2016, eg `AtLeast(k)`), result code `Status`; only meshes with
  overlapping boxes are intersected, and only the meshes whose box contains a patch get a winding number ray.
  The 10x10x10 cube grid with voids (2000 meshes) in one pass: 27 s instead of 1999 pairwise booleans
  (`BooleanComparison nary`); CLI `g3cli boolean --more ... --op at_least --k 2`.
- [x] Rounding of the output without new self-intersections, or exact output coordinates: `ExactMeshBoolean.SafeRounding`
  (`MeshRoundingRepair`: after rounding, local edits checked with exact predicates on the doubles, after Milenkovic &
  Sacks 2019; the result is verified free of self-intersections and slivers, or the remaining ones are counted) and
  `ComputeExactVertices` (exact rational coordinates). `MeshRobustRepair` uses it in its exact stage: Thingi10K
  `robust` 265 → 270 repaired (same build, with and without).

- [ ] Exact boolean performance, ranked ideas in [EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md), section 3. The first idea, exact
  arithmetic without `BigInteger`, starts from two unmerged branches: `claude/int256-spike` (`Int384` planes, not for merging as it is) and
  `claude/collinear-shortcut` (reviewed, with tests, at most 2 % on the input classes, below the 10 % bar). Exact code, so it goes to the owner. Keep both branches.

#### External boolean corpora (2026-11-04)

**External boolean corpora (2026-11-04), findings to fix later:**
- **manifold `test/models`** (elalish/manifold, Apache-2.0; run locally on master d29ee3a5, models not committed): all 14 boolean cases of its `boolean_test.cpp` / `boolean_complex_test.cpp` pass manifold's own expectations (no crash, Success, non-empty, the one pinned volume 1.480266544 matched to 1e-10); 7 of 14 results are clean by `NotCleanReason`, 6 of the 7 others have unclean inputs (self-intersecting or degenerate). Findings: (M1) `hull-body - hull-mask`: clean closed inputs, 2 self-intersections left after SafeRounding (RoundingClean false); (M2) `Generic_Twin_7081` union (31k triangles, self-intersecting inputs) takes 20 s; (M3) `openscad-nonmanifold-crash` m + m translated (0, 0.6, 0): RoundingClean true but `NotCleanReason` reports self-intersections, so the two checks disagree (probably contacts at duplicated vertices, which the rounding allows); clarify which is right and align them.
- **Thingi10K full run** (draft #385, interim 1432 of 10 000 models): Alpha Wrap 1431/1432 clean (1 timeout; median 1.15 s); booleans on closed inputs without self-intersections 2299 of 2352 operations clean, 29 not (13 degenerate triangles, 10 bowties, 6 not closed: same class as M1), 49 empty results to check; unclean results on open or self-intersecting inputs are expected by spec (report should stratify by input class). The 29 (and M1) become one bugfix task with procedural reproductions once the full run is in.
- **EMBER-style probe** (2026-11-04, local, not committed): 30 random pairs of closed, non-self-intersecting Thingi10K models (1k-27k triangles), normalized, B randomly rotated and shifted, all three operations: 90/90 Success, 89 clean plus 1 correctly empty intersection, the volume identities V(A∪B)+V(A∩B) = V(A)+V(B) and V(A−B) = V(A)−V(A∩B) hold to 5e-10, 22 ms to 1.0 s per operation. There is no canonical Thingi10K boolean subset (EMBER / trueform: random solid pairs, rotation, overlapping boxes, a random operation; Cherchi et al. 2022: 3814 random pairs of 7628 clean meshes). **Local task (owner 2026-11-04):** a reproducible `thingi-pairs` class in `BooleanComparison` after the EMBER protocol: a pinned pair list with a fixed seed (models fetched at run time, never committed), 1000 pairs, a size class of 100k-1M triangles per operand for comparison with trueform / EMBER, the volume identities and `NotCleanReason` as the oracle, the other tools of the benchmark on the same pairs.
- **Boolean allocations (owner 2026-11-04, not scheduled):** on the 30 EMBER-style pairs (90 booleans, 1k-27k triangles) 6.8 GB allocated (about 75 MB per operation), GC pauses 0.87 s of about 14.5 s (6 %), 116 gen2 collections (large arrays on the LOH per boolean); a larger gen0 cut the pauses to 0.66 s with the total in the noise. The 15-34 % GC share of the benchmark README is on large inputs (`cherchi` 467 MB per boolean, n-ary 5.3 GB). Task: (1) `BooleanComparison profile` bytes per phase on `cherchi` and Thingi10K, the top two or three phases; (2) upper bound: the same with a very large gen0 / server GC on `cherchi`; (3) only if that shows >= 10 % on large inputs, one PR with bit-identical output that reuses the largest per-call allocations of those phases (buffers per instance, generation marks instead of sets, own array pools; `System.Buffers`' ArrayPool would be a new netstandard2.0 dependency, an owner decision), before/after numbers. Not a goal: allocation-free in total (the result mesh is allocated); the goal is no allocation that grows with the number of operations. GPU (asked 2026-11-04): not now; the GPU-friendly pair search is 21-22 % of the time (at most about 1.3x), FP64 throughput on consumer GPUs and FMA contraction conflict with the exact code; if ever, ILGPU (NCSA licence) in an optional add-on package for the pair search only.
- **EMBER (Trettner, Nehring-Wirxel, Kobbelt, SIGGRAPH 2022) against our boolean performance work** (read 2026-11-04; code not open, sold via Shaped Code GmbH: paper only; a patent check before building its plane-based kernel). Their Thingi10K protocol (1000 pairs, 1k-100k faces, 8 cores): geometric mean 3.6 ms (1.6 ms at about 20k faces), QuickCSG 17.9, CGAL 5.4 711, Mesh Arrangements 3164 ms; our 30-pair probe 60-300 ms typical on 4 cores (not comparable hardware; and their time ends at a polygon soup with T-junctions, ours includes triangulation, a closed manifold mesh and SafeRounding). Their levers, compared with `docs/perf/EXACT_BOOLEAN_PERFORMANCE.md`:
  1. Fixed-width integer arithmetic throughout: tried in part (fixed-width exact points #223, −8 to −26 %; `Int384` planes spike, planes phase 285 -> 53 ms, total about 0 % alone; planes in fixed width still open). EMBER's ≤ 256-bit bound comes from **quantizing the input to 26-bit integers** (about 15 nm in a 1 m scene); we keep double inputs bit-exact (62-70-bit grid widths), so the same widths are not available without changing that contract (owner decision; at most an opt-in mode).
  2. Skipping self-pairs for meshes known free of self-intersections (their NSI/NNC flags): our candidate 3 ("pair search within a mesh", up to about 45 % serially) was held back as a weaker guarantee. EMBER's point: results of its own operations are known to be free of them. **New, exact variant for us:** a result of `ExactMeshBoolean` with RoundingClean true is certified free of self-intersections, so chained / n-ary booleans could skip its self-pairs (and cache pairs across booleans) without weakening any guarantee. Not tried.
  3. Operation-aware early-out (discard regions whose winding numbers cannot change the indicator, e.g. cells with only B's faces in A−B; their largest single gain in the ablation): not tried; we intersect everything, then classify.
  4. Local classification by short segment traces to a carried reference point with a known winding vector, instead of global ray tests: not tried (our classification is parallel exact rays per patch).
  5. No global acceleration structure, adaptive kd subdivision with work stealing over pure subproblems (5.5x on 8 cores): our scaling is about 2x on 4 cores (serial merge, planes scan, assembly; open idea 4). Adopting it means a new boolean architecture, not a tweak.
  6. Memory reuse per branch: overlaps the allocation task above.
  **Order (owner 2026-11-04):** re-evaluate after the full Thingi10K run: profile the slowest cases and the outliers by phase, then rank 2 (exact, cheap, helps chained booleans), 3 and 4 against the remaining open ideas of the perf doc; 1 and 5 only as a decision on a second, opt-in kernel.
- **Mesh kernel (Nehring-Wirxel, Kern, Trettner, Kobbelt, *Exact and Efficient Mesh-Kernel Generation*, Eurographics 2025; sections 1-5 read 2026-11-04 from text the owner pasted).** `juliusnehring/mesh-kernel` has no licence file (LICENSE 404 on main and master, none in the README): code without a licence is not used, only its README was read. The paper's method: start from the input's AABB and cut it with the supporting plane of every face (Sorgente et al. 2022), exactly in the plane-based 26-bit integer framework of EMBER (inputs quantized once); the convex cut by a greedy walk to the plane and a zipper around the cut faces ([NTK21] §3.3); in parallel an exact Seidel LP for emptiness (about 400 of 2700 test meshes have a non-empty kernel), with the 2D and 1D subproblems kept as points embedded in 3D (intersections of three planes) instead of projecting and lifting, so the LP and the cutting share one arithmetic and always agree. Accelerations: early exit for convex inputs (kernel = mesh) and genus ≠ 0 (empty, from the Euler characteristic); coplanar connected faces merged by union-find (cheaper than gcd-normalized plane hashing); concave-edge planes first (each must cut); an integer AABB proxy of the current kernel with a one-line exact plane-box test (88 bits), updated only from the cut boundary; tracing started at the previous cut. Ideas for us:
  1. **A new operation `MeshKernel`** (Phase 5 candidate): fits our arithmetic without quantizing the input: the kernel's vertices are intersections of three face planes, which our indirect predicates (TPI, #246) evaluate exactly on double inputs; the cut, the LP and the accelerations above as in the paper (paper only, no code). Uses: star-shapedness and a kernel point (spherical / star-shaped parameterization and embeddings, Livesu 2024; Hinderink et al. 2023/2024), scaling centres for SBFEM, star-shaped decomposition, view regions. Prior art: CGAL's dual convex hull (O(h log h), slow, fails on degenerate kernels), Sorgente 2022 and Asiler 2024 (inexact).
  2. **An exact 3D LP feasibility solver** (Seidel, embedded subproblems) as a library building block: emptiness of half-space intersections, a witness point; candidates beyond the kernel: the terrain's exact slope feasibility (lofting: "no compliant triangulation" status), convex-region tests.
  3. **Exact convex polyhedron cutting** by walk-and-zipper, for convex cells (kernel, Voronoi / power cells, convex clipping).
  Evaluation (their numbers, Ryzen 9 5900X): real-world set of 2762 models (Princeton segmentation benchmark plus all closed, oriented genus-0 Thingi10K meshes; about 400 have a non-empty kernel) average 15.5 ms / median 0.63 ms, against Kergen (inexact) 197 / 1.5 ms, CGAL inexact 1377 ms, Polyhedron 1490 ms, CGAL exact 1830 ms; CGAL crashes on 314 (exact) / 352 (inexact) models, Kergen on 7, Polyhedron on 9; 394 kernels found by theirs, 386 by CGAL exact (identical except degenerate kernels CGAL misses and one quad mesh whose non-planar quads have no unique plane), fewer and often wrong by the inexact tools. Ablation: without the AABB proxy 2.4x slower, hashing coplanar planes +22 %, concave-first +17 %, the parallel LP 14 %. An oracle that cuts the kernel's own planes first saves only 20 %, so the plane order has little left. Their exact Seidel LP is competitive with the inexact one and much faster than SoPlex (exact or not). Open in the paper: parallelism (proposed: test many planes against the current kernel in parallel and eliminate those that miss it, repeat).
  5. **Test corpus without mesh files:** their synthetic star-shaped set (an icosphere at varying subdivision, a random subset of vertices moved a bounded random distance towards the centre; 10 000 models) is procedural and fits our rule; it gives guaranteed non-empty kernels for `MeshKernel` tests, plus degenerate kernels (a single vertex, edge or face) as pinned cases. Triangulate quads before computing planes (non-planar quads make the planes ambiguous).
  4. Small ones: the exact integer plane-AABB test as a filter where planes meet boxes; k-DOPs instead of AABBs in the pair search (upper bound first: pairs a k-DOP rejects that pass the boxes; the pair search is 21-22 % of the boolean time; the repo offers k-DOPs, the read sections only use AABBs).
