# Exact booleans

Part of the [roadmap](../ROADMAP.md).

- **Scope:** `ExactMeshBoolean` / `ExactMeshArrangement`, SafeRounding, boolean performance (fixed-width points,
  predicate compiler, indirect predicates, cheaper constructions), the Exact Boolean 2.0 milestone, external corpora
  (manifold, Thingi10K, EMBER-style pairs), EMBER and the mesh-kernel paper, allocations, GPU and SIMD.
- **Status (2026-11-17):** milestone "ExactMeshBoolean 1.0" reached on 2026-11-01, "Exact Boolean 2.0" on 2026-11-11
  (D1 #513, counted as 2.1, merged as 69f13be3 after its 8 full rounding mutation shards passed). Exact Boolean 3.0:
  all functional and speed work is merged (see [3.0 progress](#exact-boolean-30-progress); last: #962 parallel
  assembly, #967, the `touch_only` filter #1002; speed work stopped, owner 2026-11-16), T6 is closed (#1013) and
  `ExactMeshImprint` is merged (#981, #990). Left for "3.0 complete": the full mutation run of every touched shard on
  master. The gate run 44013112256 (master 6117a866, 2026-11-17) had 35 shards green and 5 red: envelope-3 and
  geodesic-1 are fixed (#1051, merged); rounding-4, rounding-7 and terrain-overlay are in PR #1077 (open; its terrain-overlay
  break is an owner decision). The robustness round after 3.0 has started (triage #1015 merged; RC1 #1027, RC3 #1031 and
  RC4a #1040 merged, the pass-through of RC4a's parameters #1068 open; see [After 3.0](#after-30-owner-2026-11-16)).
  Exact Boolean 4.0 stage 0 (design analysis) started 2026-11-17 (owner), no PR yet. Open items: the [list at the end of this page](#open-for-the-future-2026-11-15).
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
Lévy phase 1 result (#428): no propagation now (bound 10-25 % on a minority, 2-4 ops); phase 2 (done 2026-11-09,
docs/design/LEVY_CLASSIFICATION.md 4.3: -40 to -45 % classification on the tree-bound open ops, -23 % on both samples,
hashes equal on all 171 ops) is the parallel fast-winding tree build (bit-identical; the section 4.1 probe gave -29 to -42 % on tree-bound open
ops); the AddPoint fix measured only -2 % of classification (the earlier 29-57 % trace share counted lock spinning),
so it is optional; the Antipodal Method for open operands is merged (#539, owner 2026-11-10, after the patent search); its PR 2 is 3.0 T5.

**Sign-off run (2026-11-11, runner; `runs/boolean2-final` f0d5194e, `runs/final/results/SIGNOFF.md`):** target set
on the D1 head f2245b6f (#513): 5413 models, 16239 ops, 15752 clean, 49 contacts only, 412 empty, 26 unclean (all
degenerate triangles), 0 failed; control on #385 cd7fb8ab: 15746 clean, 50 contacts, 31 unclean. Rest (not in the
target set): open 1474 models / 4422 ops (3010 of 3151 unclean are "not closed", expected for open inputs), self-
intersecting 2920 / 8760 (974 unclean, 843 of them degenerate), unknown 187 / 561 (92 unclean, 3 failed with
OutOfMemory on 252786 and 376253). Contact checks (4038d8e2): 49 of 49 target self-intersections and 1830 of 1882
in the rest are contacts; 52 real self-intersections remain in the rest (open 7, selfint 43, unknown 2), unexplained.
6 models unavailable (81313, 101633, 102610, 242236, 242237, 996816).

**`DMeshAABBTree3` profile (owner 2026-11-08; done 2026-11-12, no library change; after the Lévy phase-2 PR with the parallel fast-winding build
has merged; that is #501, merged 2026-11-10; profile done 2026-11-12, result below):** a Sonnet session profiles the tree across its main users (repair, MaxDeviation in the Reducer, the
boolean classification's fast-winding cache with its O(n log n) HashSet build, alpha wrap) and gives an upper bound
per lever (build, winding cache, queries) before any change; bit-identical results, deterministic parallel builds.
The same session includes `WindingNumberOctree` (the adaptive octree of `MeshWindingNumberSolidify`, the repair's
solidify fallback, `MeshWindingNumberBoolean` and `WindingContouring`; it evaluates winding numbers at cell
corners), `DualContouring` in its sparse mode (fed by the octree's node values; its per-cell `long`-keyed
dictionaries); the Reducer's `TriangleHashGrid` is already examined (#422: hierarchical, cell size 3.0, rebuilt
layouts slower; at most 3-5 % of the wrap pipeline left), so it is not repeated; the order follows
where the profiles spend the time (owner 2026-11-08). It also lists the library's serial loops over single tree
queries (nearest point, rays, inside tests: repair volume check, MaxDeviation / IsWithin, fidelity metrics,
deviation maps) with an upper bound each for switching them to the batch queries (research.md, priority 1).
It also measures the memory layout of the meshes as a lever (owner 2026-11-08): the original order vs a compacted
and spatially reordered mesh (vertices and triangles in Morton / Hilbert order; free-ID holes removed after many
edits), on the boolean set and the T1 wrap / repair meshes. Many algorithms break ties by ID, so reordering is either
internal with a map back where the result does not depend on the order, or a user option whose result holds for the
reordered input; the owner decides from the upper bound. Smaller candidates in the same profile: GC / allocations
(about 7 % of the Reducer; `ArrayPool` dropped: pooling measured without gain, memory went into 3.0 T2 #619) and per-vertex small
`HashSet<int>` / `List<int>` in hot loops.

**`DMeshAABBTree3` profile result (2026-11-12, no library change):** container, 4 cores, procedural inputs only (no
mesh files), `dotnet-trace` thread-time sampling with waits dropped (shares are of busy CPU), single runs; the probe
was a scratch console app, not committed.
- Tree share of the busy CPU: `Reducer` with `MaxDeviation` 48-62 % (bumpy sphere 200k, terrain 180k, 1e-3 of the
  diagonal; the nearest queries 14-19 %, the rest `TriangleDeviationBounds`' own code; 4 cores give 1.53x over
  serial, the per-collapse loop over about 7 triangles); `MeshRobustRepair` on a 114k soup 47 % (rays of
  `MeshRepairOrientation.compute_statistics` 22 %, the fast-winding cache build 7 %); `ExactMeshBoolean` with open
  operands of more than 1000 boundary segments 55 % (fast-winding evaluation 37 %, cache build 12 %), with few
  segments 0 % (Antipodal path); `MeshWindingNumberSolidify` (adaptive, 256) 16 %, next to `DualContouring` 18 %,
  `WindingNumberOctree` 6 % and GC 16 %.
- Levers, measured: (1) a slab ray-box test instead of `IntrLine3AxisAlignedBox3.DoClipping`: 0.93-0.96x, dropped;
  (2) the orientation rays as an early-out first-hit test: 0.81-0.85x, dropped; (3) the fast-winding cache's per-box
  `HashSet<int>` as a `List<int>` in the same order: 1.27-1.30x on the cache build, bit-identical (no box coefficient
  differs at 194k and 780k triangles), about 2-3 % of the repair and open-boolean CPU: done 2026-11-12
  ([FastWindingCacheBenchmark](../../benchmarks/FastWindingCacheBenchmark/README.md): 1.13-1.42x serial, 1.15-1.31x
  parallel on 35k to 850k triangles, checksums equal; `FastWindingCacheOrderTests` pins every box coefficient bit for bit); (4) bottom-up fast-winding moments: the
  coefficient passes are 65-75 % of the cache build (bound about 5-9 % of that CPU), not bit-identical, not pursued;
  (5) batch queries for serial loops: the serial tree CPU is at most 4 % per workload and all tree builds, dropped;
  (6) a parallel tree build: the build is 1.5-3.2 % of the CPU, dropped.
- (7) Memory layout: on a 780k mesh, a fully shuffled copy makes nearest queries 2.0x slower than generator order
  (2052 vs 1046 ms per 50k queries), Morton order recovers 1215 ms; rays and fast-winding evaluation change by
  10 % or less. The gain depends on how incoherent real inputs are: a Thingi10K measurement (file order vs Morton
  order) decides. Measured 2026-11-12 (cloud session, 52 Thingi10K models fetched at run time, 64 to 1.1M
  triangles, 11 above 200k; median of 5): file order is already at Morton speed (nearest file/Morton median 1.02,
  large class 1.03; rays 1.00; fast winding 1.01-1.03; build 0.98-1.04; the gains above 10 % are at the box's
  noise floor of about 10-15 %), while a shuffled copy is 1.37x slower on nearest queries in the large class.
  Dropped: real inputs are coherent. Side finding: fast-winding values depend on the triangle order at the
  approximation level (median relative difference 3.3e-6, max 5.4e-4), so any reorder would change them.
- (8) Allocations of the solidify (2.2 GB, GC pauses 0.99 s of 12.3 s wall): about 45 % come from `DualContouring`
  (`find_crossings` 21 %, `cell_patches` 9 %, `solve_qef` with a new `SymmetricEigenSolver` per call 12.5 %), the
  fast-winding cache 4 %, `SmallListSet.ValueItr` enumerators 3 %. Upper bound for removing the `DualContouring`
  share: about 0.45 s of GC pause, 4 % of wall (more if the allocation CPU falls with it). The `Reducer` is no
  allocation lever (24 GB, 0.12 s of pause).

**Exact Boolean 3.0 (owner 2026-11-08; in progress since 2026-11-10; functionally done 2026-11-17, mutation gate pending, see the status above):** tighten the speed target again with two
levers that 2.0 does not use: (1) parallel algorithms beyond the current per-phase parallel loops — the cloud
machines have 4 cores, but the trend is to many more, so the arrangement, the classification (e.g. the Lévy-style
propagation or the open-mesh GWN, which were not worth it serially) and rounding should be re-measured for scaling
over core counts (deterministic by construction: fixed partitions, ordered merges, no floating-point reduction
across threads; exact code needs the review and the allow-list entry); (2) memory: footprint and allocation of the
exact points and the arrangement (a memory profile of the large inputs, 0.6-2.7 M triangles, first). Starts with an
upper bound per lever on the 2.0 benchmark set.
**3.0 targets (owner 2026-11-10):** T1 scaling (bit-identical: serial floor 342 -> <= 170 s, 4-core time -20 % on the
73-model set; parallel deterministic sort in merge_points, classification setup, input orientation, simplify / assembly
per region, box-tree build) and T2 memory (<= 300 B retained / <= 1.2 KB peak per input triangle: input points without
FixedPoint3, struct-of-arrays triangles) approved; T3a / T3b approved (below); T5: first the Antipodal cost count on the slow
open models, patent check done by the owner 2026-11-10 (PDFs of arXiv:2605.01536 and
arXiv:2408.04466, Google Patents, Espacenet, DPMA, Shaped Code site: no patent, application or notice; freedom to operate),
so the method may be implemented from the paper or ported from the MIT reference code (attribution in docs/SOURCES.md, FMA
replaced by Dekker splitting) if the cost count shows a gain; T6 stay the only all-clean method and close the gap to CGAL EPECK on 1k-100k to <= 2x.
Pre-study: #532, docs/design/EXACT_BOOLEAN_30_PRESTUDY.md.

**3.0 process (owner 2026-11-11):** the per-PR full mutation shards cost a 6 h runner slot each and were cancelled at the limit
(#539) without a result, so during 3.0: no full shard runs on PRs (incremental where a baseline fits); CI, new tests on base
and the exact-code review with every new bound checked stay per PR; the full shards of the exact mesh pipeline run nightly on
master (survivors are tasks of the area's next PR); the 3.0 completion gate is a full run of every touched shard above its
threshold on master, on GitHub or run locally by the owner (a script under `tests/mutation` writes a result JSON with the head
sha, a CI check validates it). The boolean and arrangement shards are split finer so that each stays well under the 6 h job
limit. Remaining 3.0 steps (T3b onward) come as one PR per stage, one commit per step (CLAUDE.md). The workflow PR for
this process, #615, was closed unmerged; its content merged as #913 and #958 (2026-11-16). The test filter `FullyQualifiedName~ExactMeshBooleanTests` of the
boolean-1..4 shards now also matches 8 newer open-winding classes, so their coverage pass times out (seen on #883); the
fix was a narrower filter, applied in #883 and to the rounding shards of #843 (both merged 2026-11-15).

#### Exact Boolean 3.0 progress

- **T3a: merged 2026-11-11** (#612, 4af5fcd3). Operation-aware cull for `Difference` / `Intersection` with a closed first
  mesh: Bi triangles whose box meets no A triangle are culled per connected set by one exact ray if outside A; no new API.
  Warm medians off -> on: CAD 10k n = 500 40.0 -> 7.3 s (target <= 30 s), CAD 100k n = 100 1.78 -> 1.22 s, terrain n = 200
  1.64-1.85 -> 1.49-1.63 s (target <= 2.7 s, already met by master before T3a); never slower on small cases. Result ids of
  Difference / Intersection may be permuted (same geometry and volume bits), documented in OPERATIONS.md.
- **T3b: merged 2026-11-11** (#616, 9c2342f6; `IntersectEach` / `DifferenceEach`, additive, parallel over the Bi, bit-identical to separate
  booleans). Warm medians: terrain n = 200 IntersectEach 1.33 s (target <= 1.5 s), CAD 100k n = 50 2.7 s (target <= 4 s).
  Deviation: `DifferenceEach` gains only the parallelism (terrain 6.3 -> 4.0 s, CAD 32.4 -> 22.6 s). Exact-code review
  done; the parallel loop over the bodies is in `core/ExactCodeAllowList.cs` (`ExactMeshBoolean.each`).
- **Tail case 55928 (owner 2026-11-11: analyse and optimise; add a fail-fast budget):** an open Thingi10K model of
  about 88k triangles with about 500k self-intersecting pairs, with its rotated copy (class `thingi10k`), gives 15.1M
  pieces and takes 514-544 s per operation on 4 cores, 1858-1937 s on 1 core (runs/boolean2-final,
  `rest.jsonl`, `contacts_check_rest.jsonl`). Classification is 66 % and triangulation 29 %; the intersections are 7 s.
  The owner's bar: over 5 min is a defect in practice. Steps: (1) analysis by the T3a session (why classification is
  about 84 us per piece and triangulation superlinear here; an upper bound; levers that keep bit identity); (2) Manifold
  and CGAL on the same case as black boxes (time or refusal); (3) a check of the 2.15 x slower 1-core union of
  2026-11-11 (4175 s) against master 79a2c1ab (1937 s): machine load or regression; (4) an additive budget option that
  returns a status before the arrangement instead of running for an hour (as `ExactResolveMaxPairs` does in the
  repair). The single runs of 55928 and 252683 are not finished (tail, over 2 h on 1 core).
  Competitors (2): Manifold and CGAL EPECK refuse the input (not manifold / not a polygon mesh, under 1 s).
  Analysis (1), T3a 2026-11-11, no library code: 522,786 of 524,503 intersecting pairs are self pairs of each operand
  (two resolves of a crumpled model, about 1.7k A-B pairs); a 0.03-wide knot of 1,767 triangles (model about 65 across)
  alone gives 4.5M pieces. Classification: both operands are open, so every piece gets an exact Antipodal ray per open
  operand (about 30M rays, 480-890 plane-side tests each). Master 88342701, 4 cores, Difference: 953 s, classification
  769 s, triangulation 157 s; 79a2c1ab (before per-piece open classification and Antipodal) had 328 s classification
  on another machine: a suspected 2.3x regression, a same-machine A/B is running. Upper bound about 90-130 s per op
  (under 5 min, not seconds at 15M pieces).
  **Owner 2026-11-11: implement as proposed**, as one Opus stage after the T1 Sonnet levers, together with T1's
  classification lever (serial, same files):
  - L1: open winding per patch with a certified bound (the GWN gradient is the Biot-Savart field of the boundary,
    |grad w| <= #seg / (2 pi D)), propagated over the piece adjacency, re-evaluated near k + 1/2; bit-identical
    (classification about 769 -> 350 s here). This is also the bit-identical way back from the regression.
  - L2: short-segment tracing to local reference points instead of far rays (EMBER-style, carried over to the GWN);
    classification about 60-100 s, with L1 about 40 s. Accepted by the owner although it is not strictly bit-identical:
    a threshold can differ only where the winding lies within the evaluation error of a half integer without being on
    it, where the Antipodal Method is not exact either; it needs a written proof and an A/B against the recorded per-piece
    open winding (as the Antipodal A/B).
  - L3: triangulation (157 s, mildly superlinear in pieces per triangle), after the predicate statistics of the knot:
    cheaper or cached predicates on high-degree implicit points (bit-identical), or a different constraint insertion.
  - Fail-fast budget (additive): `ExactMeshBoolean.MaxPieces` (default 0 = unlimited) and a status `BudgetExceeded`
    without a result, checked right after the merge of the crossings (the exact piece count by Euler from the points per
    touched triangle, about 3 % into this case), `Counts["predicted_pieces"]` always set; optionally a limit on the open
    winding evaluations and a CancellationToken checked between phases.
  - Measured 2026-11-11 (T3a, one container, 4 cores, Difference): **regression confirmed**: 79a2c1ab 579 s
    (classification 390 s), master 88342701 973 s (classification 772 s, x1.98), same result hash 61550C3C2F352E72; the
    old code took the open winding per patch, master per piece (1.45e10 plane sides). Master allocates 534 GB (15 KB per
    piece in triangulation), 61 s GC pause. Knot predicate statistics: arrangement orient 5.7 % and incircle 1.8 % exact;
    the classification's 3.88e9 orient3d calls are almost all filtered, so its cost is the number of plane sides.
    Triangulation is 2.4 us per piece in the knot but 10.4 us on the full case: the cost is in the large triangles
    that cross the knot (169 with more than 10k pieces) plus allocation (L3 candidates, all bit-identical: fixed-width
    limbs for implicit points' exact stages, an array-based CDT, the superlinear constraint insertion; estimate
    172 -> 40-80 s). L1 upper bound: at a variation bound of 0.1, 16.5M instead of 30.2M evaluations (about one per
    patch), classification about 772 -> 420 s; the rest needs L2. Estimate L1 + L2 + L3: about 150-200 s per op.
    Runner, 1-core union on master 922c8d62: 4087 s (classification 82 %), peak RSS 9.2 GB (79a2c1ab: 1937 s); 4 cores 1111 s,
    9.7 GB, same hash. Design note: `docs/design/EXACT_BOOLEAN_TAIL_55928.md`, first in #626 (closed unmerged), now part of #633 (merged
    2026-11-12, d6f6fe9d), which superseded it. **Owner 2026-11-11:** a `Cancelled` status via an
    optional CancellationToken (yes); `MaxOpenWindingEvaluations` deferred until after L1/L2 are measured.
  - **Stage (#633, 2026-11-12), all bit-identical except L2's accepted half-integer band (none seen in the A/B):**
    the budget (`MaxPieces`, `BudgetExceeded`, `Counts["predicted_pieces"]`) and `Cancelled`; L1 per patch (used only
    with L2 off); L2 carried rays with candidates from the arrangement's partners and filters in a local frame; L2's
    centroids from their corners in that frame (exact centroids on 55928 for 1.2M of 30M evaluations); L3 a local
    filter for the triangulation's Orient and InCircle and Orient's zeros from the points' constructions. 55928,
    Difference, 4 cores: 973 s -> about 370-420 s (classification 772 -> 224-254 s, triangulation 172 -> 116 s,
    allocations 534 -> 200 GB), hash 61550C3C2F352E72 unchanged. `MaxOpenWindingEvaluations` is not needed. Open
    candidates (not in this stage): an allocation-free limb kernel for the 12.43M remaining BigInteger InCircle stages
    (estimated -20 to -30 s; built as candidate D, merged in #893 2026-11-15: 55928 -12 %, 352.8 -> 310.4 s); the GC setting of the application (474 -> 358 s, documented in OPERATIONS.md).
- **S3, sharing A's setup across the Bi** (owner 2026-11-11: later, after the 3.0 targets are reached; wait for now):
  the snap grid of A matches the first boolean's in only part of the booleans (terrain 1-83 of 200, CAD 100k 29 of 50,
  CAD 10k 309 of 500), so in general only A's box tree is shareable (about 2.1 s serial on CAD); effort about half a day
  for the tree, 1.5-2 days for the points per grid class.
- **T3 (#593): merged 2026-11-11** (f92f1c62). `MeshRoundingRepair` returns `OutsideRange` (mesh untouched, not clean)
  outside [2^-200, 2^200] or for subnormal coordinates; the boolean keeps its exact result there but `RoundingClean` is
  false. T3b (#616) merged 2026-11-11 (9c2342f6). T2 memory merged 2026-11-11 (#619, 5c0be691: box trees release their boxes and, after the classification, themselves; `InputTriangle` as a handle on arrays; corners from the input points; sph300: 391 -> 208 B retained, peak 1.05-1.26 -> 0.83-1.01 KB per input triangle, hashes equal; lever 1 dropped, see the pre-study correction; the 1.2 KB peak target is not met on cross150, 1.6-1.9 KB, where implicit points and output pieces dominate, out of reach of the layout levers). T1 scaling levers merged 2026-11-11 (#631, 6c5cb9ac; bit-identical, hashes equal; 4-core cloud, median of 5): lever 1, input preparation (open-addressing weld and one edge table, inputs prepared concurrently): flipped soup 1 core 387 -> 185 ms, 4 cores 151 -> 92, open soup 4 cores 91 -> 30; lever 2, parallel `TriangleBoxTree` build from 262144 boxes (same tree node for node): 600k triangles per mesh 138 -> 94 ms; lever 3 (assembly / ExactPlanarSimplify) dropped, at most 3-4 % of the op (`benchmarks/Boolean30Prestudy/results/t1_summary.md`). The 55928 tail-case stage merged 2026-11-12 (#633, d6f6fe9d; see its bullet above; the exact-code review of the new bounds in 22eb5903). T5 PR 2 merged 2026-11-12 (#675); T1b is closed by its bound (#843, merged 2026-11-15, see T1b below).
- **T5 PR 2 (Antipodal): merged 2026-11-12** (#675, a8c39f3f): carry the open winding across input-triangle edges; the
  cost count on the slow open models is in the PR.
  Reference check (c) done: with both switches lifted, Antipodal matches the direct GWN sum at every piece where FWN and
  Antipodal differ and the reference decides (117959: 1818, 122796: 43; FWN right at none); T5 faster on 7 of 10 models
  (323 vs 595 s), 122796 2x slower (14.7 vs 7.5 s). **Owner 2026-11-12: option (A), lift both switches**: merged 2026-11-13 in #766 (open inputs to the
  Antipodal Method up to 20000 boundary segments), with an OPERATIONS.md note on the cost.
- **Fuzz findings (#624 group a, 2026-11-12): all done.** #710 fixed by #732 (2026-11-12), #719 by #759 (2026-11-13),
  (A) merged in #766 (2026-11-13), #711 by P2 (#769, 2026-11-15). The original queue, serial: (1) #710 (bugfix): `ExactMeshBoolean`
  difference throws in `ExactTriangulation2.InsertConstraint` ("constraint was not inserted") on two cubes at ~3.7e-223
  with one corner at ~1.8e-284; first, a crash on finite input; (1b) #719 (coverage round 2, #716): the Antipodal
  open-mesh classification changes when one coordinate moves by 2^-1000 (union 1080 -> 1118 triangles; unit result with
  `AntipodalMaxBoundarySegments = 0`), diagnosed before (A) since (A) lifts the switches; (2) the (A) follow-up above; (3) #711: 5-47 s on 8-16
  vertex inputs whose coordinates span ~2^840; a profile first, then a bound, a faster path or a documented range (owner
  decision). The `RoundingClean` crash of the same group is the known class FINDINGS.md 5a (no new issue).
- **Snap rounding study (#721, owner 2026-11-12):** (1) option A: a hard stop of the rounding passes at the evaluation
  limit; meshes bit-identical (33/33 hashes), rounding -33 %, booleans -9 %; on results that report
  `EvaluationLimitReached`, `SmallestRejectedDrift` is taken over the proposals made before the limit (documented; the
  drift-only variant was measured and dropped: most post-limit work is try_move, drift-only already); (2) idea 3 approved, simple variant first: a seeded single-vertex position search after the passes,
  only defect-removing moves under the existing improves() (4 of 5 real D1 leftovers and 15 of 35 generated ones become
  clean; plateau steps need a termination lemma and a separate decision); design accepted (owner): deterministic
  enumeration (26 directions in the defect's local frame, sizes room / 2^j for J = 8, largest first, through try_move /
  improves()), only on results that are not rounding clean, built after (1), same file; variant fixed after the
  pre-check (owner 2026-11-12): axis-aligned directions instead of the local frame (equal or better everywhere, less
  code), J = 8, search and passes repeated until a search accepts nothing, a pair searched at the 3 corners of its first
  triangle (11 of 35 generated and 4 of 5 D1 cohort leftovers clean, 41.5 s against 36.4 s without search on the 35;
  local frame one round: 8 of 35; J = 24 gains 1-3 at 2-2.5x the proposals); sizes smallest first, room / 128 up to room
  (owner 2026-11-12, reverses "largest first": 10 instead of 11 of 35, the same 4 of 5, the cohort's drift 2-16x lower,
  as fix_degenerate / fix_intersection and Milenkovic-Sacks); built in #755 (merged 2026-11-13; hash gate 263 / 263
  identical, on the 33 rotated operations 28 identical, 2 newly clean, rounding +2.4 %); the D1 cohort acceptance run
  over all 5413 models (16 239 operations; `runs/position-search-cohort`, SAFE_ROUNDING.md section 9) is passed: no
  operation newly unclean, 16 232 rounding-clean results identical, 4 newly clean, MaxDrift within the bound;
  (3) research.md problem 3 restated; (4) the plateau variant of (2), 2026-11-15 (the note's section 10: design,
  Z3 lemma, probe on what the rounding leaves today): it can gain 1 of the 16 239 cohort operations and no clean result
  on the Thingi10K 400 self-unions, 3-9 of the 19 generated leftovers it reaches; owner 2026-11-15: no-go, no
  spike, the rule stays a design. A separate session studies every known extreme-runtime boolean case and ranks
  further levers (measured on master plus reviewed, unmerged Boolean 3.0 PRs merged locally).

**Exact Boolean 4.0 (owner 2026-11-10, milestone; stage 0, the design analysis, started 2026-11-17 (owner), no PR yet; later stages not scheduled):** exact output with provenance (pre-study topic 4,
target T4): an additive `ExactMesh` whose triangles keep their source plane and whose vertices keep their construction over
the original double inputs, so chained operations stay exact without rounding between steps (one rounding at the end,
coplanar faces stay mergeable). Effort L (4-8 weeks); starts with a design analysis on the strongest model (bit-length
bounds per construction, the arrangement's input path from constructions, consumers: chained booleans, tet meshing).
Stage 0 (owner 2026-11-17: use case chained booleans with exact intermediates, then tet meshing) is
[EXACT_BOOLEAN_40_DESIGN.md](../design/EXACT_BOOLEAN_40_DESIGN.md). It finds the degree constant along a chain under
plane-based provenance (derived bounds 7B+14 / 6B+12 bits; no measured vertex beyond them). Exact intermediates with one
final rounding make all 6 unclean abc-chained results clean (of 266; 4 non-empty, 2 by removing a thin exact solid; three
are RC4b cases) and make no result worse, but they regress on procedural contact chains of 5 or more steps (21 of 108 newly
unclean, slabs at the final rounding's effort limit). Verdict: go for stage 1 only (a lazy CSG `ExactMesh` over the existing
n-ary boolean, effort S, no exact-code change; the cull by the expression is a separate reviewed exact-code step, gated by
an order-balanced time measurement); no-go for
the materialized provenance build (stages 2-4) until a consumer needs exact intermediates without knowing the next
operation.
**Stage 1 built** (owner 2026-11-17, option A, opt-in): `ExactMesh` (`mesh_ops/LazyExactMesh.cs`), a lazy boolean
expression over double meshes computed as one n-ary `ExactMeshBoolean` (built-in shapes keep the built-in operations, bit
for bit); [design note section 8](../design/EXACT_BOOLEAN_40_DESIGN.md#8-stage-1-as-built). The long-chain regression on
contact chains is the drift bound (RC4b), not the effort budget (the same 21 results stay unclean at 16x the budget); a fix
is an output change (stage 1b, owner). Order-randomized timing: median ExactMesh / chain 0.53-1.01 (generic) and 0.60-0.89 (contact) per k on procedural
chains, a tail up to 21x on contact chains.

**T1b, parallel SafeRounding edits (owner 2026-11-10: allowed, results may change): closed by its bound** (coordinator
2026-11-13, option 1; #843, merged 2026-11-15): the edit loop is 0.54 % of the operation time, so parallel edits save at most 0.4 %
on 4 cores ([EXACT_BOOLEAN_T1B_PROFILE.md](../design/EXACT_BOOLEAN_T1B_PROFILE.md)). #843 instead parallelizes the serial work outside the
edits, bit-identical (rounding -15.7 %, boolean -4.7 % on 195 operations, 195 of 195 hashes equal). The original
item: the library is unpublished, so
changed rounding results are acceptable if they are not worse. Conditions: after D1 (#513) has landed, since both change the
rounding; deterministic over thread counts and runs (fixed spatial partition, ordered commit); gate: a cohort run on the
16 239 target operations with no op newly unclean, unclean count and MaxDrift distribution not worse, time measured.

**3.0 n-ary use case (owner 2026-11-10):** n-ary booleans are a real use case in two forms: T3a, A − (B1 ∪ … ∪ Bn)
in one pass (subtract many bodies from one, with operation-aware pruning and one rounding), and T3b, A ∩ Bi for many i at
once with separate results (shared preprocessing of A, parallel over i). Primary workload: terrain volume bodies (a TIN
sub-area of ~1 km, extruded to a solid, typically <= 10k triangles, out of a 10 x 10 km TIN of 500-800k triangles) against
construction bodies (pits, trenches, foundations, roads; 100-2k triangles, n = 5..200); secondary: CAD / machining (a part
of 10k-100k triangles minus up to 500 tools). Measured as topic 7 of the pre-study.

**T3a / T3b targets (owner 2026-11-10, approved; pre-study topic 7, #536 section 8-9):** all variants clean on the
procedural terrain and CAD workloads. T3a, A − (B1 ∪ … ∪ Bn): today's one-pass n-ary Difference is already right (chained
is 2.5-16x slower with up to 5x more triangles); no new API, an operation-aware early-out in the arrangement input path for
Difference / Intersection (Bi triangles whose box meets no A triangle and that lie outside A by one exact ray are dropped,
same exact result; the 6-45 % bound counts Bi pieces outside A, the triangle-level early-out reaches less, since pit and
trench walls cross the surface). Target (owner 2026-11-10, option a): terrain n = 200 <= 2.7 s (today 3.3-3.8 s), set
after the T3a PR's first step measures the droppable triangle share; CAD 10k n = 500 <= 30 s (today 49 s); never slower
than today's one pass. A piece-level early-out (drop Bi pieces outside A before classification) is a later lever if that
measurement shows the gap to the bound is large. T3b, A ∩ Bi each: an additive `IntersectEach(a, bodies)` (and
Difference) that computes A's closedness / orientation, self-intersection pairs and box tree once and runs the Bi in
parallel into fixed slots, bit-identical to separate booleans (A's repeated part is 29-89 % of today's serial time).
Target: terrain n = 200 <= 1.5 s (today 4.9 s serial / 2.7 s parallel), CAD 100k n = 50 <= 4 s (today 15.0 / 11.4 s).
Deviation (coordinator 2026-11-11, T3b PR): the targets hold for `IntersectEach`; `DifferenceEach` keeps all of A outside
each body, so A cannot be left out of its arrangements, and its target is "faster than separate booleans, by the
parallelism over the bodies (and shared preparation where measured)", not the numbers above.
Open follow-up S3 (owner 2026-11-11: not now; T3b reached its targets without it): share A's per-boolean preparation
across the bodies of `IntersectEach` / `DifferenceEach`.
- What it shares: A's box tree (built from the input doubles, so independent of the grid), its exact points and input
  triangles (`build_points` / `build_triangles` for mesh 0), its self pairs, its bounds.
- The grid condition: exact points can only be shared where every boolean has the same grid (`ScaleExp`, `Center`: the
  smallest coordinate exponent of A and Bi, the centre of their joint box). Measured (t3b-grid): terrain n = 200: 1 and
  83 of 200 booleans share the first one's grid (seed 1 / 2; 118-120 distinct grids, the bodies reach beyond A's box);
  CAD 100k n = 50: 29 of 50 (6 grids); CAD 10k n = 500: 309 of 500 (27 grids). So only the tree is shareable in general;
  points per grid class, with a fallback.
- The cost it would save, summed over the booleans (serial; about a third of it at 4 cores): CAD 100k n = 50
  Intersection: input 0.39 s, setup 1.79 s, trees 2.15 s, cull 0.62 s of 6.6 s; terrain n = 200: input 0.03-0.06 s,
  setup 0.25-0.31 s, trees 0.33 s of 3.5-3.7 s; the tree alone: 2.1 s (CAD) and 0.33 s (terrain).
- DifferenceEach: the same preparation is 3.8 s of 33.5 s serial on CAD 100k n = 50 (11 %): its time is assembly and
  rounding of results the size of A, which S3 does not touch.
- Effort: sharing the tree, about half a day (the arrangement takes a prebuilt mesh-0 tree; bit-identical as the tree
  depends only on the boxes); points and self pairs per grid class, 1.5-2 days with the fallback and the exact-code
  review.

**3.0 execution order (owner 2026-11-10):** serial, one 3.0 work item at a time (they share the exact files), each
one PR per feature with the exact-code review and the mutation shards: (1) T3a after #539 (Antipodal PR 1) merges (Opus,
the pre-study session); (2) T3b `IntersectEach` (Sonnet: additive, parallel over the bodies outside the exact kernel,
oracle bit-identical to n separate booleans); (3) T2 memory (Sonnet: data layout, hashes equal); (4) T1 levers one by one
(Sonnet for the box-tree build, simplify per region and the input orientation sort; an Opus design or review for the
`merge_points` sort and the classification union-find); (5) T5 Antipodal PR 2 and T1b (after D1) with Opus. Sonnet PRs in
exact files get the exact-code review by an Opus agent that sees the code, not the author's reasoning. Owner
2026-11-11: that review comes from an existing Opus session that knows the area and did not write the PR (for T2 and
later Sonnet stages: the T3a session); the Sonnet session starts no review subagent of its own.

**T5 plan change (2026-11-12, coordinator):** the cost count (21 slow open models, Union, 4 cores, master f0d3035d)
showed that lifting the fast-winding switches does not pay as L2 stands: classification 117959 92 -> 434 s, 1005586
46 -> 74 s, 122796 7.5 -> 46 s, 1087xxx about equal. The cause is L2's scope: 82-98 % of the evaluations on these models
come from input triangles of one piece, each with its own far ray (`front_crossings` 35.5 % of 1005586's profile). So
T5 PR 2 first carries the open winding across input-triangle edges
([design](../design/EXACT_BOOLEAN_T5_CROSS_CARRY.md)), measures with the switches as they are, and only then, after a
reference check of the changed hashes, decides on the switches.

**3.0 pre-study (owner 2026-11-10; done 2026-11-10, #532 and #536):** an Opus session (session_01AaGT5ATvj52jcajcJ6yYAS), measurements, benchmark
prototypes and a design note only (docs/design/EXACT_BOOLEAN_30_PRESTUDY.md), ending with proposed targets for the owner.
Topics: (1) parallel scaling per phase at 1/2/4 cores with an upper bound per lever; (2) memory of exact points and the
arrangement on the large inputs; (3) N-ary booleans / CSG trees in one arrangement pass vs chained binary ops (time,
roundings, clean status); (4) an exact-output option without rounding (additive API); (5) fast open-operand
classification (batched / dual-tree fast winding per patch) against the per-piece path; (6) positioning against the
published numbers of Manifold, CGAL, EMBER (paper only; patent search 2026-11-10 found nothing) and Cherchi et al. 2022.
- Closed inputs, self-intersection free or self-intersecting: a clean result (closed, manifold, free of self-intersections, no degenerate triangles or bowties). Self-intersecting inputs are read as solids by winding number (inside = winding >= 1).
- Inputs of unknown status: classified first.
- Open inputs: no degenerate triangles and no crash; closedness is not required.
Baseline (#385 at cd7fb8ab, closed selfint = 0): 81 of 16 239 operations unclean, Boolean time median 0.17 s, p95 4.5 s, max 282 s. Work packages: (1) data and a phase profile of the slow tail with upper bounds; (2) root causes and bugfixes for the unclean results; (3) the rounding projection filter (#385 spikes, 20-64 % of rounding time); (4) EMBER ideas 2-4 and allocations, as the data justifies; (5) a control run. Session session_01AaGT5CTqL86zQ1wj56oXAq. Reachability unknown; the stop rule reports the cases the known levers cannot reach.

**Owner decisions 2026-11-07:**
- Input defects: the criterion is "the result has no defect the input does not already have" (B1 bowties, 47 ops, and D2 degenerate inputs, 11 ops, are not library defects).
- Hopeless repairs (L3, 119829/119830): a work-unit budget with an honest "not fully repaired" status, shown never to trigger on today's clean cases.
  **Owner decision 2026-11-08: no L3 budget** (dropped). Finding (integration build d1b73d6c): neither case is hopeless
  or slow any more.
  - Times: union 3.5 / 3.7 s, difference 1.4 s, intersection 0.8 s (clean). The rounding was 21 s and became 3.1 s with
    the dirty-tree change (`perf/rounding_dirty_tree_2026-11-08.jsonl`).
  - The inputs carry about 13.4k tiny but well-shaped triangles each (edge ratio about 0.15, longest edge about 3.3e-6
    of the diagonal). Their area is just under `DegenerateArea` times the result's squared diagonal, so SafeRounding
    contracts them as decided (D', design/SAFE_ROUNDING.md section 1): union 13439 edits.
  - The 23 (union) and 5 (difference) triangles left are where the drift accumulated in the patch reaches the bound:
    the smallest rejected edit needs 1.01 times it. No effort limit is reached, and the result reports it honestly
    (`RoundingClean` false; `RoundingNotClean` with `StrictRounding`).
  - The effort budget for hopeless inputs already exists (`MaxPairTests`, `MaxEvaluationsPerDefect`,
    `EvaluationLimitReached`).
  - A procedural patch (`RoundingDenseTessellationTests`) scales about linearly, roughly 50 us of rounding per tiny
    triangle: 0.9k / 8.5k / 33k / 130k tiny triangles gave 0.16 / 0.6 / 1.7 / 6.6 s of rounding. Only the largest
    reached the existing limit.
- 519511, and 1517923 if the levers fall short: documented exceptions to the 5 s target. The architecture assessment (EMBER, octree BSPs, Campen-Kobbelt, Cherchi 2022, Manifold, Trueform, Lévy 2024) found no candidate that produces doubles with a no-intersection guarantee; 5 s on 519511 is unlikely under any architecture because rounding a 520k-triangle result alone costs 3-5 s. No rewrite; L1 and the rounding-search levers survive any architecture.
- Contacts (self-touching solids): keep the contract (clean = clean in memory, as Manifold) and report a contact count (`ExactMeshBoolean.Contacts`); contacts created by rounding (M3) are defects (#398).
- SIMD: measured only; vectorized filters at most 2-4 %; a 4-wide BVH only after the algorithmic traversal fixes.

**WP1 result (4 cores):** 16 ops > 5 s in the selfint = 0 cohort (owner data: 31 on the owner machine); rounding 37 % of their time, mostly the full self-intersection search of the result (box-tree traversal up to 76 % of rounding). Other cohorts (owner data, ops > 5 s): self-intersecting 284, unknown 274, open 736; classification is the second lever there (Lévy 2024-style propagation across intersection curves, BSD-3 Geogram).

**WP2 triage of the 81 unclean ops:** B1 47 (input bowties), D2 11 (input degenerate), B2 3 (M3, fixed in #398), D1 20 (SafeRounding's additive drift budget exhausted in clusters; the tighter drift accounting was built as D1, #513). The 80 empty unions are inside-out inputs (legitimate).

**D1 order (owner 2026-11-08):** the tighter drift accounting is stated as a lemma in the design note and checked
with Z3 (pilot 2, [research.md](research.md#smt-checks-with-z3)) before it is built.
**D1 after the apex rule (2026-11-09, research session):** [SAFE_ROUNDING_D1_DRIFT.md](../design/SAFE_ROUNDING_D1_DRIFT.md)
states the reference rule and its lemma, checked with Z3; a prototype patch clears all 13 unclean operations of the
14 D1 models and 119829 / 119830 (48 operations, none newly unclean, 119829 union drift 0.30 of the bound). Built
after the owner's decision of 2026-11-10 and merged in #513 (69f13be3), which completed Exact Boolean 2.0.

**Lévy-style classification (owner 2026-11-08, queued):** for the self-intersecting, unknown and open cohorts;
phase 1 (data, profile, upper bound, Lévy 2024 / Geogram BSD-3, design note; no library code until the merge queue
clears). Started and paused the same day (owner: wait); its session is archived, the hand-over note is on branch
`claude/levy-classification-phase1` (docs/design/LEVY_CLASSIFICATION_HANDOVER.md). Restarted 2026-11-08 (owner): a fresh session
continues phase 1 now (data, design, benchmarks only); the library code PR (phase 2) waits until the exact-boolean
merge queue behind the #403 mutation gate has cleared. Phase 2 merged in #501 (2026-11-10).

**Final A/B of the speed levers (runner, 2026-11-08, 42 models x 3 ops, bit-identical):** master 342 s, ops > 5 s 20;
+#409 -13 %; +#412 -22 %; +#406 +#419 -41 % (rounding -68 %), ops > 5 s 6 (519511 x3, 1517923 union / intersection,
519507 union). Raw rows on runs/boolean2-combined (89f2943).

**thingi-pairs runs (owner 2026-11-08):** the EMBER-protocol class (#418) runs in full (1000 pairs plus the 100k-1M
size class; ours and Manifold; CGAL EPECK only on a fixed 100-pair sample, owner 2026-11-08) twice on a Haiku runner: a baseline on master right after #418 merges, and the
final run at the end of Boolean 2.0 together with the control run (WP5). Results as summary tables; raw rows on the
runner's data branch. #418 merged 2026-11-09; no result of either run is recorded here. Owner 2026-11-12: both runs are still wanted,
queued behind Exact Boolean 3.0.

**T6 measurement (runner, 2026-11-15; ours vs CGAL EPECK on 1k-100k triangles): no-go, the gap is 3.6-4.2x at the median, not <= 2x.**
Workflow `t6-cgal.yml` (workflow_dispatch, GitHub-hosted `ubuntu-latest` with 4 cores, `cgal_bool` built unchanged from apt `libcgal-dev`, run as a black box;
the CGAL package version was not recorded), master at `dbfea7c`, run 43711354899. Set: the fixed 100-pair CGAL sample (`pairs_cgal_sample.csv`) restricted
to A + B in 1k-100k triangles: 98 pairs (27 in 1k-10k, 71 in 10k-100k; the 2 pairs above 100k are left out, no top-up needed), 3 operations, 294 operations
per configuration; one process per boolean, booleans alone as in every class of this harness, `-j 1`. Ours once with 1 thread
(`DOTNET_PROCESSOR_COUNT=1`, `taskset -c 0`) and once with all 4 cores; CGAL is single-threaded and ran in both jobs. Ratio = ours ms / CGAL EPECK ms per operation.
Raw rows: branch `runs/t6-cgal`, `run-43711354899/`.

| threads | bucket (A + B) | pairs | median ratio | p90 | max | ours clean | CGAL clean | refused / failed / timeout (both) |
|---|---|---|---|---|---|---|---|---|
| 1 | 1k-10k | 27 | 4.37 | 9.38 | 24.00 | 72/81 | 66/81 | 0 |
| 1 | 10k-100k | 71 | 4.21 | 8.14 | 13.44 | 198/213 | 155/213 | 0 |
| 1 | all | 98 | 4.23 | 8.43 | 24.00 | 270/294 | 221/294 | 0 |
| 4 | 1k-10k | 27 | 4.00 | 8.75 | 25.00 | 72/81 | 66/81 | 0 |
| 4 | 10k-100k | 71 | 3.55 | 6.54 | 10.44 | 198/213 | 155/213 | 0 |
| 4 | all | 98 | 3.61 | 7.09 | 25.00 | 270/294 | 221/294 | 0 |

"Clean" counts results with an empty `NotCleanReason`; the other operations are empty results (9 + 15 per configuration, both methods agree, volume-consistent),
for CGAL also `degenerate triangles` (6 + 43; no other reason). **All-clean: yes**, every one of our 294 results is clean or correctly empty, with no refusal, failure
or timeout, in both configurations; CGAL EPECK produced degenerate triangles in 49 of 294 operations (17 %) and no refusal or failure on this set.

Robustness of the ratio: CGAL's time is an integer number of ms, so the ratios of the fast operations are coarse (the maxima 24-25 are 25 ms vs 1 ms on a 22-triangle
intersection). Restricted to operations with CGAL >= 50 ms (151 / 136 operations): median 3.40 / 2.70, p90 5.91 / 4.52, max 9.41 / 6.68 (1 thread / 4 cores). Sum of our times
over the sum of CGAL's: 2.99 / 2.29 in 1k-10k and 3.52 / 2.86 in 10k-100k. Only 14 % (1 thread) and 19 % (4 cores) of the operations are within 2x. The 4 cores gain
about 15-25 % over 1 thread on these sizes.

Worst ratios with CGAL >= 50 ms (the five worst of the unrestricted list are 13-25x on operations of 1-4 ms for CGAL and are quantization-bound), phases in ms, 4 cores:

| pair | op | ours / CGAL ms | ratio | result tris | largest phases |
|---|---|---|---|---|---|
| p0694_1514502+43651 | union | 521 / 78 | 6.68 | 60746 | intersections 291, rounding 95, assembly 51, setup 33, classification 22 |
| p0694_1514502+43651 | difference | 494 / 74 | 6.68 | 52620 | intersections 286, rounding 82, assembly 45, setup 33 |
| p0861_80557+285607 | difference | 330 / 53 | 6.23 | 59414 | rounding 104, intersections 83, assembly 46, setup 38 |
| p0861_80557+285607 | union | 359 / 59 | 6.08 | 70760 | rounding 125, intersections 84, assembly 54, setup 38 |
| p0738_237739+777037 | union | 338 / 60 | 5.63 | 35464 | rounding 157, intersections 69, assembly 30, merge 25 |

The unrestricted worst five (1k-10k pairs p0521, p0300) take 13-82 ms for ours, of which `intersections` is 4-38 ms, `classification` 5-7 ms and `rounding` up to 21 ms, against 1-4 ms for CGAL: a
fixed cost per operation that is small in absolute terms and large in the ratio on small inputs.

**Verdict T6: no-go on the target as written** ("close the gap to CGAL EPECK on 1k-100k to <= 2x"): median 4.2x (1 thread) and 3.6x (4 cores), p90 7-8x, in both size
buckets; the "only all-clean method" half holds (ours clean or correctly empty 294/294, CGAL 17 % degenerate). To reach 2x the median has to drop by about 45 % (4 cores) to 53 % (1 thread);
the profile points at the pair search of `intersections` and at `rounding`. Nothing was optimised here; no mesh or input is committed (fetched at run time).

**Merge queue (owner 2026-11-08):** one combined mutation gate for #397, #398, #406, #409, #412, #419, #420, #424,
#425, #426 on an integration branch (Boolean session) once #403 is merged and the master baselines are seeded; then
the PRs merge one by one with normal CI. Done differently: the queue merged as one integration PR, #446 (2026-11-09,
21e27a19). Open library items after the queue (the L3 budget was dropped, see the owner
decisions above): the ExactPredicates throw near ±1e308 (fixed: robustness sweep S38), a re-measurement of the remaining ops over 5 s (1517923, 519507).

**PRs:** #397 (projection filter, rounding -16 %, bit-identical), #398 (M3 + contact count), #403 (rounding mutation shard split; the full shard ran past the 350-min job limit). All merged: #397 and #398 through #446, #403 as 1fad195d.

#### Other open items

Checked against the owner's decisions, git and the open PRs and issues on 2026-11-15; status lines re-checked 2026-11-17.

Open:

- **Exact Boolean 2.0 leftovers: closed (owner 2026-11-12).** The speed exceptions over 5 s (519511, 1517923, 519507)
  and manifold M2 (`Generic_Twin_7081` union 20 s, self-intersecting inputs, outside the 2.0 target set) stay documented
  exceptions of 2.0 and are not re-measured separately; the thingi-pairs runs after Exact Boolean 3.0 measure them on the
  3.0 state. M1 is superseded by the WP2 triage and D1, M3 fixed in #398.
- **Degeneracy criterion (`DegenerateArea` = 1e-12 × diag²):** its motivating cases 119829 / 119830 are clean since D1
  (#513); reopen only if the degenerate-triangle results of the sign-off turn out to be well-shaped tiny triangles.
- **EMBER lever 2** (skip self-pairs of results certified free of self-intersections in chained booleans): not scheduled.
- **InCircle limb kernel** (candidate from #633, E: -20 to -30 s of 370 s on 55928): built as candidate D of the
  exact-number study, merged in #893 (2026-11-15): the arrangement's Orient and InCircle beyond the fixed width on stack
  limbs; 55928 difference 352.8 -> 310.4 s (-12 %, 86 GB less allocation), other strata unchanged, hashes equal.
- **Race on `windingTrees` near 1e308** (#882, bug): fixed in #883 (merged 2026-11-15; its boolean-1..4 coverage-pass
  timeout was fixed by a narrower filter, see the 3.0 process above); #882 closed.
- **Measurement issues, all closed:** #825 (under dynamic PGO, the .NET default, 42327's classification is 8 % slower in the
  median and 15-25 % slower in a quarter of the processes; closed as an environment effect, owner 2026-11-15; boolean A/Bs run
  with `DOTNET_TieredPGO=0`); #858 (thread scaling beyond 4 cores: answered by the owner's 1 / 4 / 12-thread A/B of #962,
  closed 2026-11-17, see the T6 closing report below); #789 (`ExactPoint3` hashes seeded per process: closed by #934).
- **Runtime watch** (#726 merged 2026-11-13; first report #888 on e741c130, closed 2026-11-16 by #960: tail cases above the 20 s hard
  limit are judged by the regression rule only): 10 of the 12 Thingi10K
  boolean cases are above the 20 s hard limit (519511 and 498458 are not), eg 55928 union 480.7 s, difference 463.0 s,
  1005586 union 56.6 s, 117959 union 39.9 s.
- **Extreme-runtime boolean levers (owner 2026-11-12, study #734, `docs/perf/EXTREME_RUNTIME_BOOLEANS.md`):**
  (1) a hierarchical antipodal boundary sum (Barnes-Hut far field with a certified fallback to the full sum, decisions
  unchanged; 61 % of 117959 under (A), 18 % of 1087141; also the remedy for (A)'s 122796 regression): design note
  merged in #767 (2026-11-13), built and merged in #813 (2026-11-14); (4) boolean allocations (the >= 10 % trigger of the settled item below is met: GC 11.3 % of the
  inventory, 23-27 % on four ops): part one in the arrangement merged in #768 (2026-11-13); the open-winding tracing in
  #881 (merged 2026-11-15; 117959 33.2 -> 30.0 s, 18.3 -> 11.9 GB allocated, 38 of 38 hashes equal).
  The rounding-search levers of the study's section 8 have an upper bound in #837 (merged 2026-11-15): about 2 % on the normal
  stratum, 2-3 % on the tail for lever 1, about 20 % on rotated CAD parts for lever 2 (`near`); the tree rebuilds
  (8.2 %) are a larger target. #837 also saw 67709 union at 21.5 s under option A (9.0 s before).
  (3) A cost-weighted rounding budget for #711-type inputs (a `BigInteger` stage counts as k pair tests; #711 rounding
  15.6 s to 3.0 / 0.66 s; bit-identical on inputs without `BigInteger` stages) is approved and built in
  `MeshRoundingRepair` after #725, after a check of the 60 slow fuzz units; dropped 2026-11-12 in favour of P2 (below), re-measured once P2 is on master. Server GC advice goes into OPERATIONS.md.
- **Exact number representation beyond `BigInteger` (study, owner 2026-11-12):** a Fable study (branch
  `runs/exact-numbers`, measurement only, no library code) measures what the `BigInteger` / expansion share costs and what
  a better representation could gain: an upper bound first (stop if it is under 10 % of the profiled time), then
  prototypes (expansions with an extended exponent for spans like the 2^840 of #711, more fixed limbs, a pooled fallback,
  lazy filters), a literature check and a ranked plan. Code generation extends `tools/PredicateCompiler` (checked-in,
  reviewable output, as in stage B) rather than a Roslyn source generator. It does not overlap the settled
  `int256`/`Int384` spike above: that measured fixed width for the planes, this measures the fallback representation.
  #711 is measured first; results go into the serial exact-code queue (T3a: #710, #719, (A), #711) and do not change
  its order. The study shares rows directly with the boolean study, T3a, snap rounding and the AW3 performance study.
  Result and decision (owner, 2026-11-12): kernel P2 (allocation-free m*2^e monomial expansion on stack limbs for
  `orient2d_bigint` / `orient3d_bigint` / `incircle_bigint`, generated by a `tools/PredicateCompiler` pass,
  `BigInteger` kept as the test oracle) is built by the study itself after its final report (an exception to "studies
  push no library code"; exact review and mutation shards apply). It replaces #711 in T3a's queue (now #732, #719,
  (A), levers 1 and 4) and the cost-weighted rounding budget, which is dropped; the slow tail is re-measured once P2 is
  on master. Measured: orient3d 5077 to 202 ns, 0 B allocated; the 60 slow units plus the timeout unit 298 to 64 s,
  bit-identical. On normal operations exact arithmetic is ~5-6 % self time; the owner takes that gain too (2026-11-12,
  the 10 % stop rule of this study is lifted): the study measures what P2 and the remaining levers (expansion paths,
  filters) gain on the normal stratum and builds those with a measurable gain, ranked by gain per effort.
  P2 is built and merged (#769, 2026-11-15, 97909549): `math/Generated/DoublePredicates.cs` from `tools/PredicateCompiler/doubles.spec`
  by the monomial pass ([design](../design/EXACT_FALLBACK_MONOMIALS.md)); on master a7693fd the 61 units of the #711
  class go from 289 to 51 s (none above 5 s, hashes equal), normal operations do not change; the study's report with the single-thread
  shares per stratum, the prototype tables and the ranked candidates B (classification stages on stack limbs), C
  (point construction on limbs) and D (InCircle beyond 4 limbs) is on `runs/exact-numbers` (`REPORT.md`).
  Candidate B is merged (#772, 2026-11-14): the classification's exact sign stages (plane side, normal dot, the
  4 x 4 orientation; seven expressions and `orient3d` of `ExactMeshBoolean`) on stack limbs, `math/ExactLimbSigns.cs`
  ([design](../design/EXACT_LIMB_SIGNS.md)). Measured against master 683fb2c (4 cores, wall time with collections,
  median of 5): slow tail 42327 -15 to -17 % per operation (three quarters of it the collector's pause, 12.8 GB less
  allocation), 107554 -3 to -6 %; normal stratum unchanged within noise; hashes equal, also on the 61 units of the
  #711 class. The CPU gain of the side stage needs the limbs stored with the points. Candidate C measured
  (2026-11-13, study `REPORT.md` sections 5c and 5d): the centroid on stack limbs with a conversion back gains
  nothing; the exact point construction is 2.4 % / 4.9 % of the normal stratum's one-thread time (20 pairs / 13
  models with a rotated copy) and a limb construction would take under half of it, below the run-to-run range, so
  that part is dropped; the centroid and the planes' coefficients kept as limbs in the classification (bound 9 to
  15 % of the slow tail's one-thread time, nothing on the normal stratum) were parked until lever 1 (#813) had
  landed and measured again on 2026-11-15 ([design](../design/EXACT_LIMB_SIGNS.md), section 6): the bound is 9 % /
  16 % of the one-thread time of 107554 / 42327 (lever 1 does not change how often these stages run there); a probe
  that reads the operands from `BigInteger`'s own limbs and computes the centroid on stack limbs gains 6.3 % of the
  one-thread time of the two models together (42327 -6.7 %, confirmed; 107554 -5.5 %, inside the run-to-run range)
  and nothing on the normal stratum. Recommendation: go for that cheap form as one small PR, if the owner accepts
  the access to `BigInteger`'s private fields behind a layout check; no-go for limbs threaded through the
  classification. The owner declined that access (2026-11-15), and the route through the public API was probed the
  same day (section 7): limbs converted once and carried beside the planes and the centres gain nothing alone; with
  the limb centroid 42327 is 5.0 % faster on one thread in the median, not confirmed (the ranges overlap; its
  classification is confirmed 9.7 % lower), 107554 3.6 %, the two together 4.5 %, 4 cores and the normal strata
  nothing; hashes equal. A no-go by the 5 % line: the classification part of candidate C is closed and nothing of
  it is built. The study ends with this. Candidate D is built
  (2026-11-13): the last stage of the arrangement's `Orient` and `InCircle` for points beyond the fixed width (a
  BigInteger 3 x 3 determinant, 12.4 million calls in the difference of 55928 with its rotated copy) on stack limbs,
  `ExactLimbSigns.Orient2` / `InCircle` ([design](../design/EXACT_LIMB_SIGNS.md), section 5). Measured against the
  branch of B (4 cores, wall time with collections, median of 5): 55928 352.8 -> 310.4 s (-12.0 %, 15 s of it the
  collector's pause, allocation 180 -> 94 GB); 107554, 42327 and the normal stratum unchanged within the run-to-run
  range; hashes equal.
- **Normal-stratum levers 2026-11-12/13 (study #734 section 8, its uncovered levers):** measured on its 36 normal unions and
  its tail (4 cores, median of 5, alternating A/B, hashes equal):
  - **Pair search: the filter underflow term (#753).** The pair search was mostly the self-join's `touch_only` on adjacent
    triangles (0.47 M pairs on 104969, 6 M on `sph300`, 15 M on 1063860). Every `Orient3d` / `InCircle` / `InSphere` filter
    call computed a subnormal product (about 50 ns of microcode assist on x86). Now it is skipped where it cannot change
    the bound, so the bound is the same double.
    - Microbenchmark: orient3d 53 to 7.5 ns, incircle 56 to 9, insphere 70 to 28.
    - Normal 36: wall -9.6 %, pairs -48 %.
    - Tail: wall -5.7 %.
  - **Pair search, step 2 (compact per-triangle data in tree order for the self-join): dropped, no measurable gain.**
    On 104969, sph300, 1063860, 1036403 and 131971, on top of #753: wall +0.8 %, pairs +3.8 %. What is left of the self-join
    is branching and traversal, not memory.
  - **Merge: dominated by the exact-numbers study's C.** merge.points is concentrated in 1017018 (800 ms) and 112826 (414 ms)
    of about 1.6 s on the stratum. There the BigInteger point construction (`compute_exact`, `edge_plane_exact`,
    `BigIntegerCalculator.Gcd`) is 24-40 % of the phase's samples.
  - **Assembly: not worth it on its own.** On 112826: GC waits 19 % and memmove 8 % (lever 4); `assemble` 13.5 %,
    `SmallListSet.FindByKey` 6.7 % and `sort_by_key` 6 %. The part that is not allocation would give about 1 % of the
    stratum at 1.5x.
  - **Triangulation: dominated by lever 4 and the exact-numbers study.** On 112826: GC waits 37 %; Dictionary, HashSet and
    List resizing 15 %; BigInteger and FixedArith 16 %.
  - **Lever 4 (allocations), upper bound** on top of #753, with the GC pause stated separately:
    - Normal 36: the GC pause is 7.9 % of the wall. A 1 GB gen0 budget gives wall -7.5 %; server GC -5.2 % (single cases
      +32 % and +78 %).
    - Tail: -3.2 % with gen0 and -1.6 % with server GC (the four 3 s models -8 to -19 %, 1088051 about -2 %).
    - Allocations of the normal 36 by phase: rounding 23 %, the arrangement about 38 %, classification, assembly and
      simplify about 30 %, setup 4 %.
    - **Done (classification, L2's tracing):** the closure and delegates per `carried_crossings` call replaced by a
      struct visitor, `LocalFrame.Corner` without the exact point where the double difference is exact, the remaining
      BigInteger dot products on stack limbs, no copy for the projection query, one `carry_bound` box per thread.
      Bit-identical; 117959 union: classification 14.4 -> 7.9 GB, 23.9 -> 20.7 s, run 33.2 -> 30.0 s. What allocates
      there now: the `Center` per piece (about 5 %), the pierce candidates per step key, the per-frame dictionaries.
    - Done (arrangement): presizing and reuse of scratch buffers, merged in #768 (2026-11-13, part one).
    - Open (snap-rounding follow-up): reusing the `TriangleBoxTree` that `MeshRoundingRepair.rebuild_tree` builds again
      and again. #768 dropped its `Rebuild` because the method had no caller. The reference is commit 3ec47ffb on
      `claude/lever4-arrangement-allocs`: the reuse constructor, the check for a released tree, two tests, and the
      allow-list entry. Build it together with the call and a benchmark row.
- **Exact coplanar triangle intersection:** the 2D clipping of `IntrTriangle2Triangle2` is unchanged; exact output
  coordinates stay open, not scheduled.
- **Mesh-kernel ideas** (not scheduled): a `MeshKernel` operation (Phase 5 candidate), an exact 3D LP feasibility
  solver, exact convex polyhedron cutting, small filters, the procedural star-shaped test corpus.

Settled:

- **EMBER levers 1 and 5** (quantized 26-bit inputs, a new architecture): no rewrite (owner 2026-11-07); patent search
  done by the owner 2026-11-10 (nothing found). Lever 3 went into T3a (#612), lever 4 into #633 L2.
- **Exact arithmetic without `BigInteger`** (branches `claude/int256-spike`, `claude/collinear-shortcut`): superseded.
  The collinear idea is built in #633 step 2 (`CollinearByConstruction`); fixed width does not reach (three-plane points up
  to 309 bits, centroids up to 807 bits; #633, 2026-11-12); the Int384 planes spike gave about 0 % in total. The owner deletes the
  branches (owner 2026-11-17).
- **`coplanar_stack` worst-case latency:** not built, closed (owner 2026-11-01; CHEAPER_CONSTRUCTIONS.md section 10a).
- **`ArrayPool`:** pooling was measured without gain; no new dependency. Memory went into 3.0 T2 (#619); server GC
  is the application's choice (OPERATIONS.md). Allocations as such were reopened as lever 4 (#768 and #881 merged).
- **Boolean performance leftovers:** planes in fixed width dropped (see above); the generator for the envelope predicates
  done (ENVELOPE.md section 6); the slow `exact.resolve` on 67709 / 42327 diagnosed (CHEAPER_CONSTRUCTIONS.md section 10a).
- **Open inputs, winding propagated across patches** (branch `claude/exact-boolean-gwn-propagation`): superseded by the
  per-piece open classification (#528), Antipodal (#539) and #633 L1.
- **SafeRounding patch-wise B:** not pursued; the snap-rounding experiment closed in #313, D1 (#513) covers the drift
  failures; A's library code stays on `claude/snap-rounding-A-archive`.
- **Default-hashed `long` keys:** done in #431 (2026-11-08); [LongHashAudit](../../benchmarks/LongHashAudit/README.md).
- **Thingi10K full-run findings** (draft #385): superseded by the WP2 triage and the sign-off run.
- **`thingi-pairs` class:** done (#418, 2026-11-09); its baseline and final runs are queued behind 3.0 (owner 2026-11-12).
- **`thingi-contact` class (owner 2026-11-16):** Thingi10K pairs with exact contact (face contact, partial overlap, ulp shift, mirror; translations, power-of-two scales and reflections only, every sum checked exact), the hard cases for the exact predicates and the use case of `ExactMeshImprint`; first run of 30 pairs per variant on a GitHub runner (`thingi-contact.yml`, `workflow_dispatch`); PR #982 (merged 2026-11-16); first run 2026-11-16 (30 pairs per variant, ours / manifold / CGAL EPECK, [results](../../benchmarks/BooleanComparison/README.md#thingi10k-pairs-with-exact-contact)): 14 of 150 placements refused as inexact, findings filed as issues #993 (mirror: difference not closed) and #994 (face-contact: union not closed), not fixed in that PR; both closed 2026-11-17: the triage #1015 found edge contacts misread by the benchmark eval (RC2), fixed in the eval by #1021 and #1063, with `NotCleanReason` unchanged; `ours` drops the one-ulp shell A - A' by design (SafeRounding, owner decision 2026-11-16: default stays), the benchmark records `removed_components` to tell that from a really empty result.
- **`abc-chained` class and the ABC dataset (owner decisions 2026-11-16):** chains of booleans on triplets of ABC CAD models (Koch et al. 2019) placed with the exact contact transforms (translations, power-of-two scales, reflections, every sum checked exact), each method on its own rounded results (results of exact booleans re-entering the exact pipeline); ours / manifold / CGAL EPECK as black boxes on runners, oracle as `thingi-pairs` with an error budget per step, `SafeRounding` removed components and time per step. **Dataset decisions:** ABC is approved for tests and benchmarks with run-time fetching only (download on GitHub-hosted runners, never committed or redistributed, no model or derived mesh as artifact, no fetch in PR-tier tests); the Fusion 360 Gallery is excluded; GrabCAD is not pursued. Workflow `abc-chained.yml` (`workflow_dispatch` only); README section "Chained booleans on ABC models"; first run 2026-11-17 (30 chains per variant, ours / manifold / CGAL EPECK, [results](../../benchmarks/BooleanComparison/README.md#chained-booleans-on-abc-models)): 381 of 400 sampled models clean after the single precision snap and the exact weld, all 90 chains passed the exactness check; ours stays clean through all three steps in 75 of 90 chains (manifold 34, CGAL EPECK 4); findings were issues #1010 (results that lost components to the rounding: not closed / inside-out / self-intersecting) and #1011 (results with nothing removed that are not closed or degenerate); both closed (owner 2026-11-17) after the robustness-round fixes and a rerun on master 9133bc25, with the remaining cases as documented limits (RC4b drift bound; effort budget below D = 50000, no budget raise) and the iud p0008 step-2 residue as #1067 (open); see [Robustness round](#after-30-owner-2026-11-16). PRs #1001, #1005, #1007.
- **GPU / SIMD:** GPU not now (owner 2026-11-04; if ever, ILGPU in an add-on for the pair search only); SIMD for the
  `Orient3d` filter measured at 2-4 % ([research.md](research.md#later)).

### Done / history

#### Stage B: boolean performance

Continues, at the owner's request.

Done: `AddPoint` shards (#222).

Built in this order (all merged by 2026-11-01): fixed-width exact points, 4 limbs (#223: cherchi / cgal −8 %, Thingi10K −11 %, n-ary −26 %, all hashes identical); then the **predicate compiler on fixed-width inputs**, stacked on #223 and started without waiting for its mutation runs: regenerate the arrangement's `InCircle` / `Orient` from the generator of #221 with the exact bit bounds of the 4-limb points, so that the `BigInteger` fallback (about half of the calls with the grid-fitted bounds of #221) mostly disappears; the fallback rate is a reported metric. The generator stays an offline tool with a checked-in, reviewable output and an up-to-date test, not a Roslyn source generator, because the bounds review needs the generated code in the diff. The grid-fitted `ArrInCircle` of #221 is not merged. Later: planes / CSR (section 12 of the fixed-width design; dropped, see Settled), and the generator reused for the envelope predicates (stage 7; done). Hand-over and ranked candidates: [perf/EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md). Exact code: bounds review and mutation runs on every PR.

**Status 2026-11-01:** #223 (fixed-width points) and #248 (pair search by a self-join of the box tree, cgal + Thingi10K −7.9 %) wait for their mutation runs; the compiler (#244) and the indirect predicates (#246 / #250 / #257, LPI / TPI filter 47-86 ns against 3-7 µs to construct, no expansion stage) are stacked on them. CSR is dropped as a step of its own (1-2 %); a GC trace on cherchi decides later. Cheaper exact constructions merged in #256 (2026-11-01; cgal + Thingi10K −5.5 %; coplanar_stack not built, owner); the session is archived. #246 (indirect predicates step a) merges next; its stack #250 → #257 → #258 → #263 → #268 follows as internal code (envelope stopped). On 2026-11-01 the owner chose not to wait for PR mutation runs of #246, #251 and #256: a full run on master after the merges is their gate and the baseline, and a shard below its threshold becomes a follow-up fix

Tier: L. Parallel: 1 large session

#### Stage R: SafeRounding

Its own session, kept apart from the performance work (performance must stay bit-identical; this changes results on purpose). (1) Fix: SafeRounding moves exact vertices of pieces below its degenerate threshold on clean input (union volume 17.999992370605316 instead of 17.99999237060547), pinned as a skipped test in `ExactBooleanPropertyFindings` (#229). (2) A recommendation on design question 1b of [design/BOOLEAN_PROPERTY_TESTS.md](../design/BOOLEAN_PROPERTY_TESTS.md): repair input that is already degenerate or not. (3) A bounded evaluation of the research item "Practical 3D snap rounding with guarantees" below, with Thingi10K: a concept, no implementation. (4) Fix `ExactTriangleIntersection.IntersectsBeyondShared`: for coplanar neighbours sharing one vertex where one is a sliver, the projection axis comes from a floating-point cross product that rounds to 0, so a false intersection is reported (and inherited by `MeshRepairMetrics.SelfIntersections`); choose the axis exactly. Repro from the remesher session (both triangles in y = 0, ids (0,1,2) / (3,1,4)); this gives the deferred exact coplanar clipping task a real caller.

**Status 2026-11-01:** (4) merged in #249 (bit-identical on the boolean hashes); `IntrTriangle3Triangle3` exact for coplanar and nearly parallel pairs, merged in #253; (1) as the displacement bound D′ (edits refused beyond 1e-5·diag), merged in #251 on 2026-11-01: **milestone "ExactMeshBoolean 1.0" reached** (#223, #244, #248, #249, #251, #253). 1b (strip zip) merged; the UTM tolerance (ulp term, vertex removal, strict mode) merged in #287 on 2026-11-02. The pass-through API merged in #294 (2026-11-02); the CLI exposes the options.

Then: the snap-rounding experiment (options A, then B; measured and closed in #313, below) in a fresh session, from the handoff in [design/SAFE_ROUNDING.md §6](../design/SAFE_ROUNDING.md#6-handoff-for-the-snap-rounding-experiment-2026-11-02); (3) concept in [design/SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md)

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
- [x] (Superseded by #528, #539 and #633 L1; see Settled.) Open inputs: generalized winding numbers propagated across patches (integer jumps across patches, "same value"
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

- [x] (Superseded; see Settled: fixed width does not reach, P2 #769 and the limb kernels #772 / #893 replace the
  `BigInteger` stages.) Exact boolean performance, ranked ideas in [EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md), section 3. The first idea, exact
  arithmetic without `BigInteger`, starts from two unmerged branches: `claude/int256-spike` (`Int384` planes, not for merging as it is) and
  `claude/collinear-shortcut` (reviewed, with tests, at most 2 % on the input classes, below the 10 % bar). Exact code, so it went to the owner; the owner deletes both branches (owner 2026-11-17).

#### External boolean corpora (2026-11-04)

**External boolean corpora (2026-11-04), findings to fix later:**
- **manifold `test/models`** (elalish/manifold, Apache-2.0; run locally on master d29ee3a5, models not committed): all 14 boolean cases of its `boolean_test.cpp` / `boolean_complex_test.cpp` pass manifold's own expectations (no crash, Success, non-empty, the one pinned volume 1.480266544 matched to 1e-10); 7 of 14 results are clean by `NotCleanReason`, 6 of the 7 others have unclean inputs (self-intersecting or degenerate). Findings: (M1) `hull-body - hull-mask`: clean closed inputs, 2 self-intersections left after SafeRounding (RoundingClean false); (M2) `Generic_Twin_7081` union (31k triangles, self-intersecting inputs) takes 20 s; (M3) `openscad-nonmanifold-crash` m + m translated (0, 0.6, 0): RoundingClean true but `NotCleanReason` reports self-intersections, so the two checks disagree (probably contacts at duplicated vertices, which the rounding allows); clarify which is right and align them.
- **Thingi10K full run** (interim 1432 of 10 000 models, from the harness PR #385, closed unmerged; superseded by the sign-off run of 2026-11-11 above): Alpha Wrap 1431/1432 clean (1 timeout; median 1.15 s); booleans on closed inputs without self-intersections 2299 of 2352 operations clean, 29 not (13 degenerate triangles, 10 bowties, 6 not closed: same class as M1), 49 empty results to check; unclean results on open or self-intersecting inputs are expected by spec (report should stratify by input class). The 29 (and M1) become one bugfix task with procedural reproductions once the full run is in.
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

**Open operands per piece (2026-11-10, owner decision A).** The generalized winding number of an open operand varies
within a patch (a patch can reach from inside an open mesh to outside it through its hole), so open operands are now
classified at every piece, not once per patch (#528). Two overlapping open spheres had an empty intersection; with an
open A and a closed B, B's patches reach around A's hole (1375 of the union's 7328 triangles on the wrong side). Cost on
the 21 open models of the Lévy samples, on top of #501: total 4.34x, classification 18.4x, worst 113906 about 10x
(100 s); closed inputs unchanged. A certified per-patch bound cannot help much: in the 1087xxx soups 92-96 % of the
pieces lie in patches whose pieces the per-piece rule decides differently, and the own-mesh queries (half of them) no
bound decides. Item (B), a batched or dual-tree fast winding number per patch: measured upper bound 22-30 % of the
per-piece query work (the near-field triangle terms are 70-78 % of it; Exact Boolean 3.0 pre-study,
docs/design/EXACT_BOOLEAN_30_PRESTUDY.md section 5, PR #532): not planned. The remaining lever is what is evaluated: (C), the
exact Antipodal Method (LEVY_CLASSIFICATION.md 4.2, after the owner decision and patent check), or fewer queries.

### After 3.0 (owner 2026-11-16)

**Why stop here.** The 2x target against CGAL EPECK was set to drive the algorithm, not because the speed is
insufficient. The rule is an 80 % approach: stop once the effort is out of proportion to the gain.

- **Where we stand.** T6 run 43819984226 on master 414c6b5f, 4-core runner, our time divided by CGAL's single-threaded
  time:
  - all cores: 2.23x on the summed time, 2.91x median per operation;
  - 1 thread: 4.07x summed, 5.24x median.
- **12 threads (extrapolated, not measured).** Scaled with the owner's 4 to 12 thread factor of #962 (1.49):
  about 1.5x summed and about 1.95x median.
- **Small and large inputs alike.** The median ratio is about the same in the 1k-10k and 10k-100k buckets (5.3x and
  5.2x at 1 thread), so there is no large fixed cost for small inputs to remove.
- **Rounding is work CGAL does not do.** It is 21-27 % of our time. CGAL's exact result has no guarantee once it is
  converted to doubles: 49 of its 294 T6 results are unclean (degenerate triangles), while all of ours are clean.

**What the last levers gained.**
- #956: -5 to -7 % at 4 threads, for one changed constant.
- #962: -6 % at 4 threads and -10 % at 12, at medium to large effort (a parallel sort, a union-find, a bulk mesh builder,
  three rounds of mutation fixes).

**The levers left, at 12 threads.** Each touches an exact file, so each brings an exact-code review and mutation work.
- Parallel `ExactPlanarSimplify`: about -3 % (bound -6 %).
- The rounding's serial setup and load balance: -3 to -5 %.
- The builder's `vertices` step: -2 to -3 %.
- `setup.points` and `merge.points`: -1 to -2 % each.
- The rest of the classification: -1 %.

These are past the 80 % point and are dropped unless a caller needs them.

**The one open question: probe A.** (Probe B, run in the same session, splits the rounding into sub-phases.) Probe A measures the pair work in `intersections.pairs` that `touch_only` later
discards; an older probe found about a third of the 1-thread time there.
- If it shows a realistic gain of at least 10 % that stays bit-identical with no new exact arithmetic, that lever is the
  last speed PR of the boolean.
- Otherwise the speed work stops.
- **Result (probe A, #967; owner 2026-11-16):** the gain was below the 10 % bar (the filter first in `touch_only`: pairs phase -21 % / -13 %,
  whole boolean -8.8 % at 1 thread, about -1 to -5 % at 4, about -3 % extrapolated at 12; [BOOLEAN_THREAD_SCALING.md](../perf/BOOLEAN_THREAD_SCALING.md) sections 7.1, 7.5),
  but the owner approved it on 2026-11-16 as the last speed PR because its cost is small (an existing filter, bit-identical output, no new exact
  arithmetic). The speed work stops after it.
- Either way T6 closes with a report: the measured 12-thread ratio on the owner's machine (median and summed, with and
  without rounding) and the cleanliness of both sides.

**T6 closing report (2026-11-17).** The owner's 12-thread data: i7-8700, 6 cores / 12 threads, net10.0, 3 rounds per
side, `thread_scaling.ps1`, branch `runs/exact-numbers`, `results/ab-pr962-vs-master-2026-11-16/` (master 414c6b5f and
#962 head bf34eff7). CGAL runs on GitHub-hosted runners only, so its time on the owner's machine is estimated:
CGAL's single-threaded time from T6 run 43819984226 (the same master 414c6b5f, 4-core runner), times the ratio of our
master 1-thread time on the owner's machine to ours on the runner. The summed ratios use one global factor (0.56, the
ratio of the sums); the median ratios use the factor of each operation. With per-operation factors the summed ratios
are about 3 % higher (1.47x instead of 1.43x at 12 threads). Ours is the median of the three rounds per operation;
"without rounding" subtracts the `rounding` phase. The estimate assumes CGAL gains from the faster CPU like our
1-thread code does; it rests on one runner run.

| code | threads | summed ratio | summed, without rounding | median ratio | median, without rounding |
|---|---|---|---|---|---|
| master 414c6b5f | 1 | 4.07x | 3.25x | 5.24x | 4.24x |
| master 414c6b5f | 12 | 1.59x | 1.30x | 2.18x | 1.86x |
| #962 bf34eff7 | 1 | 3.95x | 3.12x | 4.93x | 4.01x |
| #962 bf34eff7 | 4 | 2.14x | 1.71x | 2.84x | 2.33x |
| #962 bf34eff7 | 12 | **1.43x** | **1.14x** | **2.05x** | **1.70x** |

- The earlier extrapolation for master at 12 threads (about 1.5x summed, 1.95x median) was slightly optimistic: measured
  1.59x and 2.18x.
- After #962, the boolean probes (#967) and the touch_only filter (#1002) come on top, the last two without a new
  12-thread run.
- **Cleanliness:** ours 294 of 294 clean or correctly empty (T6 run 43819984226); the result hashes on the owner's
  machine are equal across all rounds and thread counts, and equal between master and #962. CGAL: 49 of its 294 results are unclean
  after the conversion to doubles (degenerate triangles).
- **T6 is closed.** The remaining gap at 12 threads, about 1.4x summed, is in the range of the rounding, which CGAL
  does not do (without it, 1.14x).
- **Geogram, measured (2026-11-17, #1056 merged; [GEOGRAM_BOOLEAN_COMPARISON_2026-11.md](../research/GEOGRAM_BOOLEAN_COMPARISON_2026-11.md)).**
  Ours against the public Geogram 1.10.2 (`mesh_boolean_operation`, BSD-3, built without Triangle and TetGen) on GitHub
  runners.
  - T6: median ratio ours / Geogram 0.36 at 1 thread and 0.46 at 4 cores. Volumes agree on all 294 operations. Ours 294
    of 294 clean; Geogram leaves degenerate triangles in 42.
  - ABC chains: Geogram fails its radial sort on 46-49 of the later steps (27 of about 31 ulp-shift placements); ours has
    no failure.
  - Lead: step 3 of the chains (operands that share A's surface), where ours is 1.1-2.2x slower than Geogram and than
    CGAL. Not diagnosed; no speed work is scheduled for it (the boolean speed work stopped, owner 2026-11-16).

**Triage of the research ideas.**

| idea | relevance | decision |
|---|---|---|
| Exact imprint on `ExactMeshArrangement` (a robust replacement for the float `MeshMeshCut`, whose failures are in ROBUSTNESS_SWEEP R6), then the deprecation decision for `MeshMeshCut` / `MeshBoolean` | high | the only new boolean feature: done, design note [EXACT_IMPRINT.md](../design/EXACT_IMPRINT.md) (#979) and `ExactMeshImprint` (#981, #990, merged 2026-11-16; sliver cleanup at near contact #1050); the deprecation decision follows in the list at the end of this page |
| The tail above 20 s (55928 and others) | medium ("always finishes" matters more than the mean) | only on a runtime-watch regression or an issue |
| EMBER lever 2 (skip self-pairs of certified results in chained booleans) | low: probe A (#967, 7.1) puts the operand self-join at 10.6 s of the 12.1 s pairs phase at 1 thread, so skipping it is at most about −15 % of the boolean at 12 threads (extrapolated), and 0 on single booleans of raw inputs such as T6; the clean flag would come only from the library's own results (`NotCleanReason == None`) or a prepared operand, never from the caller | parked until a caller chains booleans (re-confirmed by the owner 2026-11-16) |
| Normal-cone patch certificate for the operand self-join (Volino and Magnenat-Thalmann 1994; [SELF_INTERSECTION_PATCH_CERTIFICATE.md](../design/SELF_INTERSECTION_PATCH_CERTIFICATE.md)) | low: estimated −7 to −10 % at 12 threads (not measured), at or below the 10 % bar of the stopping rule; needs new error bounds in exact code, and the same certificate was slower in the Reducer | parked (owner 2026-11-16) |
| Exact Boolean 4.0 (exact output with provenance) | low to medium, a large architecture step | stage 0 (the design analysis) started 2026-11-17 (owner), no PR yet; later stages parked until a concrete use case |
| Plateau variant of 3D snap rounding | low | parked (no-go, owner 2026-11-15) |
| Exact output coordinates of the coplanar 2D clipping | low | parked |
| S3 (share A's setup across the Bi) | low (T3b met its targets) | dropped |
| Reuse of the `TriangleBoxTree` in `rebuild_tree` | small speed lever | dropped unless probe B shows otherwise |
| EMBER levers 1 / 5, mesh-kernel ideas (a new architecture) | no rewrite (owner 2026-11-07) | dropped |

**Research directions after 3.0 (prior-art check, 2026-11-17):**
[BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](../research/BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md) covers (1) rounding with a
guarantee (partially known: the guarantee of Devillers et al. 2020 fits our drift bound and degenerate test on a grid
of about 2^-19 of the diagonal when the box of all parts is at most 1.36 times the diagonal of the largest component
and max |x| is below about 1e10 times that diagonal (an inference not yet proven, section 2.3 of the note);
open are the output size and a local scheme; a 2-3 day falsification experiment) and (2) a certifying boolean
(partially known building blocks, no certifying 3D mesh boolean found; a test-only checker for the classification as
the first stage). Owner 2026-11-17: (2) stages 1-2 (stage 1 the test-only checker of the classification and selection, C3 + C4;
stage 2 closedness and coverage, C1 + C5) are to be built as a correctness oracle for the robustness round; the experiment of (1) is not now (its gain on real data is the five RC4b cases).
Built (merged 2026-11-17 as #1061, [QA.md 2.4a](../QA.md)): the checker (C0 inputs and culling, C1, C3, C4, C5 closedness and coverage)
is the oracle of the boolean property campaigns and of a Nightly corpus; stage 3 (a simple exact recheck of the
embedding, C2a) and a certified mode for users are not scheduled.

**Octree-embedded BSPs / EMBER architecture: shelved (owner 2026-11-17).** The ideas that fit are built (the cull of
T3a #612, the local rays of #633 L2) or bounded (EMBER lever 2, at most about -15 % in chains at 12 threads,
extrapolated). What is left is a second, quantized
plane-based kernel: a speed lever after the speed work stopped, months of work, and it gives up exact computation on
the input doubles while still needing our rounding for double output. The plane-based representation stays a
question for the design analysis of Exact Boolean 4.0 (stage 0, started 2026-11-17 (owner), no PR yet); later stages wait for a use case.

**After 3.0 the boolean is in maintenance mode.** That means bugs from fuzzing and the runtime watch, regressions and API
stability, plus the exact imprint.

**Robustness round after 3.0 (owner 2026-11-16).** Before more boolean bug fixes, collect what the running campaigns find:
the ABC chained-booleans runs, the thingi-contact runs (#993, #994) and the metamorphic findings (#985). Then the owner
runs a long local campaign on the boolean:
- CsCheck property campaigns (`ExactBooleanPropertyTests.CsCheck.cs` and the other `Category=Campaign` classes that call
  the boolean or the rounding), with large sample counts (`G3_PROPERTY_SAMPLES`, `G3_PROPERTY_ALL=1`; QA.md, item 9);
- fuzzing with `tools/MeshFuzzPilot` (`union`, `intersection`, `difference`, `rounding-repair`).

All findings, old and new, are triaged together and fixed in one bugfix round, one PR per root cause.
Triage of #985, #993, #994, #1010, #1011 by root cause (2026-11-17, merged as #1015): [BOOLEAN_TRIAGE_2026-11.md](../research/BOOLEAN_TRIAGE_2026-11.md).

**Status of the round (2026-11-17).** #985, #993, #994, #1010 and #1011 are closed.
- RC1 (#985, SafeRounding emptied a thin result): the narrower rule (only where an exact input is itself thin or tiny nothing
  is removed, the result is reported not clean; a fat input with a thin result keeps today's drop), merged in #1027.
- RC2 (edge contacts read as "not closed" by the benchmark eval): the eval's pinch class and an exact order of tied angles,
  #1021 and #1063. `NotCleanReason` is unchanged; split pinches stay "not clean" (owner 2026-11-17).
- RC3 (inside-out from a floating-point volume sign): an exact (filtered) volume sign in `FindInvertedComponents`, #1031.
- RC4a (effort limit): the pair-test budget grows with the initial defects, #1040; the pass-through of its parameters to
  `MeshRobustRepair`, `ExactMeshImprint` and g3cli is PR #1068 (open). No budget raise below D = 50000 (owner 2026-11-17).
- RC4b (the drift bound `MaxLength`): kept as a documented limit (owner 2026-11-17).
- Left: the iud p0008 step-2 residue of 34 near-zero-volume triangles is #1067 (open); the CsCheck / fuzz campaign plan is #1038 (open).

### Open for the future (2026-11-15)

Written 2026-11-15; statuses re-checked 2026-11-17 (the heading keeps its date because other pages link to it). Superseded where it differs by [After 3.0](#after-30-owner-2026-11-16) (2026-11-16): #913 and #956 are merged, #858
is answered by the owner's 1 / 4 / 12-thread A/B of #962, the `rebuild_tree` reuse and S3 are dropped, and items 8 and 9
are parked or dropped as the triage says.

Done today and removed from this list: #883 (#882 race), #881 (lever 4), #893 (candidate D), #843 (T1b, SafeRounding
outside the edits), #837 (rounding-search upper bound: lever 1 dropped, lever 2 parked), candidate C re-measured after
lever 1 (#902) and closed as a no-go (#915), #789 closed by #934, #825 closed as an environment effect (owner
2026-11-15; A/B runs on 42327 use `DOTNET_TieredPGO=0`), #922 (T6 against CGAL EPECK), #953 (thread-scaling analysis).

1. Close out Exact Boolean 3.0: the mutation shards #913 and lever 1 of #953 (#956) are merged; what is left is the full
   mutation run of every touched shard on master (gate run 44013112256: 35 green, 5 red; envelope-3 and geodesic-1 fixed in
   #1051, rounding-4, rounding-7 and terrain-overlay in PR #1077, open; see the status at the top).
2. The thingi-pairs baseline and final runs, after 1 (owner 2026-11-12; a Haiku runner): not run yet.
3. The tail above 20 s: #888 is closed (#960, 2026-11-16); tail cases are judged by the regression rule only. #1047 (42327
   with its rotated copy, 18-26 s, the time is in the classification) is open; PR #1064 (box prefilter for the coplanar stacks,
   memo of L2's exact sides, -7 to -14 % on 42327, bit-identical; 19.5 s median for the union, still above 20 s in one of 10
   processes) is in review.
4. Rounding: the `rebuild_tree` reuse is dropped (see [After 3.0](#after-30-owner-2026-11-16)); lever 2 of #837 parked for the
   rotated-CAD class.
5. Thread scaling beyond 4 cores (#858): answered and closed 2026-11-17 by the owner's 1 / 4 / 12-thread A/B of #962 (T6 closing
   report above). Levers 2 and 4 of #953 are merged (#962, 2026-11-16, 4 threads -6.9 % on T6;
   [perf/BOOLEAN_THREAD_SCALING.md](../perf/BOOLEAN_THREAD_SCALING.md) section 6); the levers left are past the 80 % point and dropped unless a caller needs them (see [After 3.0](#after-30-owner-2026-11-16)).
6. Done: the rest of the #615 mutation process (no full shard on PRs, local runs with a result check, `resplit.py`)
   merged as #958 after #913; #615 is closed.
7. Exact imprint: cut the curves of a cut mesh into a target in place on `ExactMeshArrangement`, as a robust
   replacement for the float `MeshMeshCut`; only then decide on deprecating `MeshMeshCut` / `MeshBoolean` (coordinator
   2026-11-15). Design note: [EXACT_IMPRINT.md](../design/EXACT_IMPRINT.md) (2026-11-16, merged as #979). Implemented as `ExactMeshImprint` in one PR (2026-11-16: the arrangement
   mask, the `MeshRoundingRepair` locks, the class, tests, the `ExactImprint` benchmark: about half the time of the boolean on closed sphere pairs); the
   deprecation decision for `MeshMeshCut` / `MeshBoolean` followed the imprint corpus run (run 44017929170,
   [IMPRINT_CORPUS_2026-11.md](../research/IMPRINT_CORPUS_2026-11.md)): **decided, owner 2026-11-17**, both are `[Obsolete]` warnings (no error, no API break)
   pointing to `ExactMeshImprint` / `ExactMeshBoolean`. Known limit of the imprint at 1-ulp contact (slivers): #1034.
8. Research: 3D snap rounding #721 is closed; the plateau variant stays a design (no-go, owner
   2026-11-15). Draft: Thingi10K full-corpus harness #385.
9. Not scheduled: S3 (sharing A's setup across the Bi; dropped), EMBER lever 2 (parked, owner 2026-11-16), mesh-kernel ideas.
   Exact Boolean 4.0 (exact output with provenance): stage 0, the design analysis, started 2026-11-17 (owner), no PR yet; later stages parked until
   a concrete use case.
