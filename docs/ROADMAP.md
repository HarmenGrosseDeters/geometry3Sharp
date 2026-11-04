# Roadmap

## Direction

geometry3Sharp is the only broad, permissively licensed (Boost) geometry library for .NET. C# is the language
of Unity, Rhino/Grasshopper, Revit and many desktop apps, and none of them has an equivalent of libigl,
CGAL or geometry-central.

The goal of this fork: a **geometry platform that coding agents (and people) can build domain apps on**,
rather than an app. That puts some things first:

1. **Robustness over breadth.** Exact predicates, deterministic results, clear result codes instead of
   silent failures or exceptions. Every change comes with tests; regressions are measured (eg the
   Thingi10K repair benchmark).
2. **A discoverable API.** XML docs on public classes, small examples per operation, consistent patterns
   (options object, `Compute()`, result with status).
3. **A headless layer.** A CLI / MCP server that exposes the operations on mesh files
   (repair, boolean, remesh, measure, convert).
4. **Modern build, wide reach.** net10.0 plus netstandard2.0 (Unity, .NET Framework), CI. A public package
   comes last (see Release).
5. **Licenses.** Keep the Boost license. Port only from MIT / Apache / BSD sources (eg Manifold,
   geometry-central), never from GPL code (CGAL, parts of TetWild). Sources are listed in [SOURCES.md](SOURCES.md).

Relation to upstream: stay API compatible with 1.0.324, which existing users depend on. Upstream's
`dotnet8` branch has breaking changes (it removes the float types); useful parts (eg USD) can be taken over
selectively.

## Next: taking over the abandoned work

Status on 2026-10-30. Several sessions stopped mid-way; their work is parked in draft PRs titled
`[WIP, quality unverified]`. **Its quality is questionable.** After takeover, question and check everything in it,
including its tests, numbers and recorded conclusions: re-run the numbers, run the mutation checks, and get the
independent review of [REVIEW_CHECKLIST.md](REVIEW_CHECKLIST.md). A green CI is not acceptance. A PR that does not
hold up is discarded with the owner, not patched until it passes. The stages run in this order:

| Stage | Work | Tier | Parallel |
|---|---|---|---|
| 0 to 3 | Done on 2026-10-31: local CI green, the small fixes and the parts of #193 merged, the plans checked. Hidden tests replaced by [references](design/ACCEPTANCE_STRATEGY.md) | | |
| 4. References | Pilot: M1 against NetTopologySuite. Then the golden subset. The `Triangle.NET` reference session (strategy, section 3) is preparing since 2026-11-01 in the clean-room comparison session, output in `HarmenGrosseDeters/g3-meshing-references`; its comparison batches start once `m2.snap` is finished (owner) | M | 1 session |
| 5. Quadtree | `quadtree` (#195): independent review, owner-machine performance and memory, side-by-side with the old quadtree. The owner's local optimizations merged in #266 (2026-11-01) | L | owner, local |
| 6. Meshing M1 | `feature/meshing2d` (#200): oracle and differential as gates. Only then the M2 design (#201, `docs/meshing2d-m2-design`): its recorded approvals are disputed, so question its domain decisions first. An M2 implementation comes after that | L | 1 large session, after stage 5 |
| 6b. Meshing provenance check | After M3, once: a separate comparison session (the dirty side, the only one that may read Shewchuk's `triangle.c` and Triangle.NET) compares the 2D mesher with them for copied expression: data structures and memory layout, function decomposition, control flow of distinctive routines, constants, names and comments, plus a similarity tool where it applies (C against C#). It writes a report only, never code. A clean session rewrites any flagged part from the spec alone. The meshing session keeps a provenance log ([design/MESHING2D_PROVENANCE.md](design/MESHING2D_PROVENANCE.md): paper / section or spec item per non-trivial routine) and avoids Triangle-typical expression. Reason: the BSL-1.0 release, and models may have seen `triangle.c` in training. Algorithms from the papers are free to implement; expression is not | M | 1 session, after M3 |
| 7. Envelope containment | Phase 5 #2 (Wang et al. 2020; `fast-envelope` is MIT; `MeshEnvelope` is the prototype). One piece of work serves four tasks: quality passes that stay near the input, the alpha-wrap simplification, the `Reducer` `MaxDeviation` check (92-97 % of its time) and the envelope-guaranteed pipeline. It was planned before the alpha-wrap stage (7b), which keeps failing on the trade-off between quality and fidelity; since the stop, 7b uses `IsWithin` for that (see 7b). **Stopped 2026-11-01 (owner decision)** after its stop criterion was met; the code merges as an internal tool (#263, #268), see the hotspot row on `MaxDeviation`. Started 2026-11-01 in the indirect-predicates session: design [design/ENVELOPE.md](design/ENVELOPE.md) (#258), approved by the owner: double inputs by a filter plus exponent alignment, polyhedra after the paper's Prop. 3.1 (eps/sqrt(3); `MeshEnvelope`'s 0.8/0.6 shape only as a proven option), an internal class first (`MeshEnvelope` replaced in its own PR once the BigInteger-clipping oracle agrees and it is within 5x of `IsWithin`), one PR per step. The envelope does not replace the per-operation self-intersection checks | L | 1 large session, after stage 5 |
| 7b. Alpha wrap quality | Owner decision on #199. (History: #199's branch passed no gate and was not merged.) **Done 2026-11-02: merged in #292** (34 wrap cases on master with #311: p01 3.75 → 8.08°, holdout 3.34 → 7.59°; share > 170° 0.64 → 0.05 %; time 1.21× / 1.07×; G1 accepted by the owner with its documented misses). Then in the same session the bundled Reducer + Remesher upper bounds (2026-11-02): C, speculative parallel `MaxDeviation` certification, built bit-identical but slower than master (bumpy 0.001: 31 → 46 s; the queue reorders after each collapse, window hits 53-95 %, and the existing inner parallelism already uses about 2.2 cores): measured and closed (#323 docs); A, one union-box query per collapse, about 21 % of `SimplifyWithin`, bit-identical; B (the same in the Remesher) 4-6 %, not built; D, a permission-grid accept filter, sound but not bit-identical by guarantee, pays only at coarse tolerances (0.01: about 40 % of the total; 0.001: a loss): not built (owner 2026-11-02). Then, in the same session, the upper bound and literature step of Phase 5 #5 (inside / outside for open and broken inputs). **Earlier status:** the new quality pass (#292, G1: D0.1 + cap-flip exemption + guarded cap split) is ON by default in the wrap fallback (`AlphaWrapQualityPass`); 34 wrap cases: p01 3.66 → 7.94° (holdout 3.35 → 7.62°), share > 170° 0.63 → 0.05 %, (c1)/(c2) 18/18, noise gate 4/18 (holdout 1/16) as documented misses; time ratio being re-measured after the rejection cache (projected 1.22). **Owner decision 2026-11-01:** 7b no longer waits for stage 7. The fidelity bound that the envelope was to provide comes from the existing `IsWithin` / `MaxDeviation` check (certified, conservative, 46-213 µs per query; exactness changes no decision there), not from the exact envelope: its ε/√3 polyhedra accept only 8-17 % of what `IsWithin` certifies (#268), so they would block exactly the quality operations 7b needs. Order: (1) the MeshRepair wrap-resolution session first, since it changes 7b's input; (2) then 7b as a bounded diagnosis of the failing local patches of the hard-feature policy, no threshold tuning, starting with whether the trials on `fix/alpha-wrap-simplification-quality` already guarded their operations by a deviation check; (3) only then a quality pass with every operation guarded by `IsWithin` against the wrap tolerance. The exact envelope serves tests only as a sufficient check | L | 1 large session, after the wrap-resolution session **Owner 2026-11-02:** acceptance (c) replaced by per-mesh floors (no triangle < 0.5°, global min / max not worse than the simplified wrap, share > 170° ≤ 0.1 %) plus relative improvement and the reported distribution (literature: Shewchuk 2002, Babuška–Aziz 1976, Morvan–Thibert 2004, Hu et al. 2017); G1 (D0.1 + cap-flip exemption + guarded cap split) accepted after one last variant, (d) misses documented, ON by default in the wrap fallback with a switch. Follow-up: degenerate triangles of the raw alpha wrap itself (eg 59196, where the simplification falls back to the unsimplified wrap). |
| B. Boolean performance | Continues, at the owner's request. Done: `AddPoint` shards (#222). Next, in order: fixed-width exact points, 4 limbs (#223: cherchi / cgal −8 %, Thingi10K −11 %, n-ary −26 %, all hashes identical); then the **predicate compiler on fixed-width inputs**, stacked on #223 and started without waiting for its mutation runs: regenerate the arrangement's `InCircle` / `Orient` from the generator of #221 with the exact bit bounds of the 4-limb points, so that the `BigInteger` fallback (about half of the calls with the grid-fitted bounds of #221) mostly disappears; the fallback rate is a reported metric. The generator stays an offline tool with a checked-in, reviewable output and an up-to-date test, not a Roslyn source generator, because the bounds review needs the generated code in the diff. The grid-fitted `ArrInCircle` of #221 is not merged. Later: planes / CSR (section 12 of the fixed-width design), and the generator reused for the envelope predicates (stage 7). Hand-over and ranked candidates: [perf/EXACT_BOOLEAN_PERFORMANCE.md](perf/EXACT_BOOLEAN_PERFORMANCE.md). Exact code: bounds review and mutation runs on every PR. **Status 2026-11-01:** #223 (fixed-width points) and #248 (pair search by a self-join of the box tree, cgal + Thingi10K −7.9 %) wait for their mutation runs; the compiler (#244) and the indirect predicates (#246 / #250 / #257, LPI / TPI filter 47-86 ns against 3-7 µs to construct, no expansion stage) are stacked on them. CSR is dropped as a step of its own (1-2 %); a GC trace on cherchi decides later. Cheaper exact constructions merged in #256 (2026-11-01; cgal + Thingi10K −5.5 %; coplanar_stack not built, owner); the session is archived. #246 (indirect predicates step a) merges next; its stack #250 → #257 → #258 → #263 → #268 follows as internal code (envelope stopped). On 2026-11-01 the owner chose not to wait for PR mutation runs of #246, #251 and #256: a full run on master after the merges is their gate and the baseline, and a shard below its threshold becomes a follow-up fix | L | 1 large session |
| R. SafeRounding | Its own session, kept apart from the performance work (performance must stay bit-identical; this changes results on purpose). (1) Fix: SafeRounding moves exact vertices of pieces below its degenerate threshold on clean input (union volume 17.999992370605316 instead of 17.99999237060547), pinned as a skipped test in `ExactBooleanPropertyFindings` (#229). (2) A recommendation on design question 1b of [design/BOOLEAN_PROPERTY_TESTS.md](design/BOOLEAN_PROPERTY_TESTS.md): repair input that is already degenerate or not. (3) A bounded evaluation of the research item "Practical 3D snap rounding with guarantees" below, with Thingi10K: a concept, no implementation. (4) Fix `ExactTriangleIntersection.IntersectsBeyondShared`: for coplanar neighbours sharing one vertex where one is a sliver, the projection axis comes from a floating-point cross product that rounds to 0, so a false intersection is reported (and inherited by `MeshRepairMetrics.SelfIntersections`); choose the axis exactly. Repro from the remesher session (both triangles in y = 0, ids (0,1,2) / (3,1,4)); this gives the deferred exact coplanar clipping task a real caller. **Status 2026-11-01:** (4) merged in #249 (bit-identical on the boolean hashes); `IntrTriangle3Triangle3` exact for coplanar and nearly parallel pairs, merged in #253; (1) as the displacement bound D′ (edits refused beyond 1e-5·diag), merged in #251 on 2026-11-01: **milestone "ExactMeshBoolean 1.0" reached** (#223, #244, #248, #249, #251, #253). 1b (strip zip) merged; the UTM tolerance (ulp term, vertex removal, strict mode) merged in #287 on 2026-11-02. The pass-through API merged in #294 (2026-11-02); the CLI exposes the options. Next: the snap-rounding experiment (options A, then B) in a fresh session, from the handoff in [design/SAFE_ROUNDING.md §6](design/SAFE_ROUNDING.md#6-handoff-for-the-snap-rounding-experiment-2026-11-02); (3) concept in [design/SAFE_ROUNDING.md](design/SAFE_ROUNDING.md) **Snap-rounding experiment (owner 2026-11-02):** prototypes A (extended Milenkovic–Sacks) and B (Valque-Lazard, drift-bounded) behind off-by-default options; go / no-go fixed in advance: B default-on only if it raises the clean share on the Thingi10K self-unions measurably within the drift and Hausdorff bounds at acceptable time; A only for a remainder only A fixes; otherwise measured and closed. First a bugfix for SafeRounding's 2^k invariance (absolute thresholds in MeshRoundingRepair). **Verdict 2026-11-02 (#313, owner): measured and closed.** B not default-on (Thingi10K self-unions clean 188 → 209 within the drift bound, but 9 of the 21 new ones beyond the Hausdorff bound on open inputs; time median ×4.7, p95 ×66); A has 0 cases only it fixes. #313 (merged 2026-11-02) adds the §8 write-up, the benchmark harness (B as a tool) and the separation certificate; A's library code stays on `claude/snap-rounding-A-archive`. Invariance bugfix #316 (88 → 0 differing clusters, R22 fixed). Parked: a patch-wise B behind an off option (patch resolve ≈ 1/20 of a full SafeRounding per round; first measure whether a patch fits back). | L | 1 large session, when a large slot is free (after meshing or B) |
| 8. Planned follow-ups | Boolean property tests, DAP audit and honest terrain metrics, next to any stage from 4 on; the upstream ledger last, because it records what is merged. Terrain phase 1 after stage 5. After this wave: **CLI / MCP and docs for the new operations** (quadtree, mesher, n-ary and SafeRounding booleans in `g3cli`, examples in OPERATIONS.md), so that agents can use what was built (Direction, points 2 and 3). Candidate: a **Clipper2 adapter** as an optional package (`geometry3Sharp.Clipper2`; Clipper2 is Boost-licensed): robust integer 2D polygon booleans and offsets for terrain / GIS footprints and slicing, written against Clipper2's public API, not taken from the DAP fork; the core stays dependency-free and API compatible. Decide after the DAP audit's provenance list. Done: property tests, DAP audit (nothing to adopt), Clipper2 adapter (#243). The CLI / MCP docs session waits until quadtree, mesher M2 / M3, n-ary and SafeRounding are merged, so that it documents the finished state in one pass. **CLI / MCP done 2026-11-02** (#284, #291, #299: mesh2d, query2d, xor, Triangle .poly/.node/.ele, snap and SafeRounding options, Meshing2Benchmark `mesh`). Parked: the `.poly` reader keeps crossing / touching / overlapping segment groups as constraints only (Triangle nodes them first), and a vertex with d segments costs about d² comparisons (20k: about 6 s); mesh2d report areas underflow to 0 below coordinates of about 1e-160 | S, M | up to 2 sessions |

Stages 4 and 6 are prepared in [MESHING2D_PLAN.md](design/MESHING2D_PLAN.md): the gates exist as commands; the verdict on #200 and the questions on #201 are there.

Why this order: a reference must exist before an implementation is accepted against it. The quadtree comes before meshing because terrain phase 1 waits for it and its PR is further along.

Limits on parallel work:

Order when slots free up (owner, 2026-11-01; on that day 4 large and 2 medium sessions ran, above the limit below):

1. Remesher done (medium): not replaced.
2. **MeshRepair wrap-resolution session** (medium, owner 2026-11-01; owner 2026-11-01 later: it starts when the slot of
   #256 frees, without waiting for the rest of stage R, whose 1b and UTM tolerance touch only SafeRounding): in the MeshRepair-400 worst case, `wrap.simplify` takes 24 % of the time on 17 meshes,
   because tiny inputs (450-650 triangles) are wrapped at 180k-415k triangles (resolution from the bounding box, not
   the input); couple alpha / offset to the input, start coarser, or speed up the simplifier, under the F-score and
   fidelity gates (data: `benchmarks/MeshRepairBenchmark/cases/robust_phases_2026-11-01.csv`). It precedes stage 7b
   (alpha wrap quality). The volume checks (20 % of the time, all 400 meshes): the redundant check before an exact resolve
   is gone (#273, bit-identical, −21 % on the 20 measured meshes). Owner decision 2026-11-01, low priority, after stage R
   and the wrap session: a tighter sample region for the IoU check (only 16 % of samples hit the union; upper bound about
   10 % of the 400-set time; measurement and options in [design/VOLUME_CHECK_SAMPLING.md](design/VOLUME_CHECK_SAMPLING.md),
   parked 2026-11-02). It must provably contain the union of both solids, so the estimator stays unbiased with
   lower variance; results are no longer bit-identical, the quality thresholds stay; its own PR with an A/B; **merged 2026-11-02 in #305 (option A, cells for closed meshes with exact winding, B as the fallback): 400 set 0 verdict flips, repair path identical, volume phases −7.8 %, cell checks −31 % (38 % instead of 17 % of samples in the union). Follow-up (owner 2026-11-02): one octree per component box measured and closed (upper bound 1.7 %; [design/VOLUME_CHECK_SAMPLING.md](design/VOLUME_CHECK_SAMPLING.md) §5); the 42327 classification split done (diagnosis: coplanar_stack 72 %)**; **started 2026-11-02 (owner, ahead of 7b's merge, file-disjoint: MeshAlphaWrap / Delaunay3 only):** degenerate triangles of the raw alpha wrap (eg 59196): diagnosis done (needles from rule-2 projection of the clamped dual-edge point, all 21 triangles < 0.5° in 59196/163738; caps > 170° from make_manifold); fix A (project the unclamped circumcentre, exact guard) merged in #311; then B (owner): B3, refinement at the non-manifold sites, as a prototype with a stop rule (≥ 50 % of the caps gone on the 800 sample, otherwise docs only; B1/B2 dropped after the probe); the manifold-step caps (12–15 triangles > 170° on 4–6 of 19 meshes), measured on a large Thingi10K subset, bug-or-inherent verdict against the paper first, fix in its own PR; B3 met its stop rule (800 sample, 300/3000: caps −86 %, band max 8.76 → 1.11, 0 guarantee failures, time ×1.22 median) and is merged in #317 (2026-11-02), `RefineNonManifold` default on (owner 2026-11-02); the raw-wrap session is archived; first look against CGAL's alpha wrap (30 meshes + 19 wrap cases, 300/3000): guarantees, band and Hausdorff equal, CGAL +10–15 % triangles at about 4–6× our time; CGAL has 0 triangles < 0.5° / > 170° (ours 1/2 with B3, master 1/7) and a p01 angle about 2° higher; parked: the remaining pinch caps (102793, 118678, 1505037; all on cells the manifold step moves after B3 found no separation-preserving candidate, exposing facets up to 40 α where CGAL's output stays at 2–4 α; ideas: a third candidate on the segment to the nearest input point, refining the inner neighbours, the smallest-radius candidate in the manifold step) and the p01 gap; the slow `exact.resolve` on small inputs (67709, 42327) goes to the boolean-performance session as a diagnosis.
   **Terrain 1b** merged in #285, **1c part 1** in #297 (2026-11-02: one build per margin, 1a realization defects at large heights, Hambach DGM1 benchmark; octagon 1 m 43.6 → 17.8 s, 3.1 → 1.5 GB). Next, #307: `CompositeKind` (owner: region-only output with mapping, plus the whole changed terrain as an option; default Full), band-only overlay, ideas B–D; target one edge on the 1 m grid < 1 s (0.30 s warm with Replacement in the draft). Then (owner 2026-11-02, functional completeness first): the functional 1c items (volumes 5.7, StatusKind 5.6, catch-line accuracy reporting 5.5); then D, randomized order with a canonical tie rule and a canonical simplification (measured ≈ 2.3× on the wavy case); then mixed cut and fill, target heights, docs; then the combinatorial zeros of C and the band-only overlay. **Pause (owner 2026-11-03):** after the (b) tolerance-budget PR and `RoundToGrid`, terrain pauses; a large package is complete. Before the pause, the docs (TERRAIN.md, OPERATIONS.md, CLI help) match what is merged, and the open terrain items are recorded here; the terrain mutation shards (#302) wait for the restart. The order below resumes after the pause, first with a DGM reader in the CLI (owner 2026-11-03: after the pause). After phase 1, order of the later steps (owner 2026-11-02, replaces TERRAIN.md §11 step 8's order): (1) docs and CLI / MCP for terrain (finishes phase 1); (2) volume per cell, 2D columns first (§9.3); (3) ramps as design geometry (path, width, grade; access ramps into pits are modelled in practice); (4) lofting between contours with triangles chosen under the exact slope oracle and a status when no compliant triangulation exists (§9.2; two contours first, then branching); (5) performance follow-ups when a real case is slow (the octagon's second realization, C zeros, band-only overlay); (6) proven tan interval and catch-line bound; (7) sector polygons, exterior distance on demand; (8) berms and inter-ramp checks parked until a use case (not modelled in practice). **Merged 2026-11-02:** #307 (`CompositeKind` Replacement, one toe edge on the 1 m grid 0.17 s catch line / 0.26 s replacement warm, octagon 1 m replacement 24.4 → 5.9 s), #314 (functional 1c: StatusKind, volumes, catch-line accuracy). D merged in #318 (order-independent bit for bit; A/B: wavy 48-gon replacement 27.4 → 8.8 s, 1a case 7.3 → 3.6–4.0 s; octagon 1 m replacement 6.1 → 8.8 s slower, a second realization, follow-up); mixed cut/fill and target heights merged in #320 (2026-11-02; owner 2026-11-02 accepted the Auto approach: two independent runs on the terrain, joint triangulation of the triangles both replace, the catch points of both snapped; ToeOvertopped; CutVolume / FillVolume / CutArea / FillArea; target heights as the SlopeRegion contour at z = h), its first commit fixes a pre-existing CompositeFailed overlap at toe crossings. Corner fix (exact `Orient2d`) merged in #329 (2026-11-02); CompositeFailed / NoDaylight fix at the terrain boundary merged in #338 (2026-11-03). LocalFrame: A/B done, translating back fails at UTM in every case and gains no exact-stage rate; owner decision 2026-11-03: option 1, LocalFrame returns local outputs plus UsedOrigin, no translation back (the caller translates without guarantee); after the composite subnormal bugfix. Then (owner 2026-11-03) an optional output step `RoundToGrid(origin, resolution)`: iterated snap rounding to the target grid (Hobby 1999; Halperin & Packer 2002, half-pixel vertex-edge clearance; Hershberger 2008 stable), then the exact slope/support checks re-run on the rounded coordinates with a status, so features below the grid merge in a controlled way instead of failing silently; prior art in GIS: a fixed precision model with a snap-rounding noder; building blocks `SnapRounding2` and the fixed-width exact points; first an upper bound: how often rounding to 1 mm at UTM breaks a slope check. The band repair [64·e_xy, MinFeatureSize) of TERRAIN.md 5.4 merged in #350 (2026-11-03: a targeted edge collapse of needles at near-quadruple points, funded from the facet slack, replayed on later realizations; the #339 repro now succeeds at the origin and at UTM). Composite subnormal bugfix merged in #352 (the step from 0 is the ulp of the coordinate scale; restores the bitwise composite comparison). LocalFrame option 1 built; its A/B found a near-origin SlopeViolation on the Hambach wavy case: two gaps in the #350 band merge (no chains within a pass at near-quintuple points; nested plane sets in [r, rband)), merged in #354 (2026-11-03: iterate, cumulative per-vertex move cap ≤ rband, nested pairs; amp-30 waves near the origin 3 attempts 14.6 s → 1 attempt 5.6 s; the DGM case with LocalFrame succeeds in 2 attempts). LocalFrame merged in #355 (2026-11-03: translate in place with a bitwise restore via VerticesBuffer, so caller timestamps and AABB trees stay valid; −0.0 terrains fall back; a positions-only copy is a §11 follow-up). Clipped-region item merged in #356 (docs plus the face 2 pin; the early region check skips the terrain copy with identical decisions; every hash equal; octagon UTM −10 %, LocalFrame −4 to −14 %; the rest of a failed realization, about 25-30 %, is on §11). **Phase 1 done**: #357 merged (2026-11-03; g3cli slope / MCP with GeoJSON toes, OPERATIONS.md example, SOURCES.md). Auto toe-on-terrain bugfix merged in #359 (joint() parity by an order-preserving XOR merge of both runs' edge nodes; 56 of 960 Auto designs from CompositeFailed to Success). (a) T-junctions merged in #361 (a point exactly inside an edge splits the copied neighbour or joins the replaced neighbour's part input; AssertComposite checks T-junctions and vertical cracks; 30 tests failed on master; outputs bitwise identical elsewhere). Next (b): the composite's catch-point snap (moves up to MinFeatureSize) is not in the tolerance budget; at UTM the guarantee is unproven (certificate bounds 1.04-1.16 × Tol on the DGM cases, no measured excess so far), in the frame rotated 1e-3 utm Successes are wrong by up to 72 × Tol; fix: a snap share in the budget plus an exact measured certificate (δ raise, then NotCertified), choice between a reserved share and the certificate alone by cost, then RoundToGrid. (b) merged in #363 (2026-11-03): a reserved Tolerance/4 plus the exact certificate over the moved and swept triangles (δ raise, then NotCertified as a SlopeViolation kind); 2880 sweep 150 → 0 wrong Successes, every frame but rotated 1e-3 utm stays Success (there CompositeFailed honestly, LocalFrame succeeds); certificate cost 1.3-2.3 %, 4.5 % on the edge case; the extra realization on the L shape and the wavy 48-gon is on §11. RoundToGrid upper bound (2026-11-03): with the grid as the build's resolution the budget forces MinFeatureSize of 20-52 m at 1 mm (infeasible); rounding after the build fails 15-53 % (steeper) and 27-32 % (unsupported) of the region triangles at 1 / 0.1 mm; the unsupported ones need a λ floor of about 1.5 mm, the steeper ones a local re-triangulation of the clipped region on the grid under the slope constraint (rounding with constraints, open: Milenkovic; Fortune 1999); owner decision 2026-11-03: R1, RoundToGrid parked (docs and measurement tools only, face 2 stays pinned); the local re-triangulation under the slope constraint is an open research item (see the snap-rounding question under Open research questions). Terrain pauses after that PR and a handover note.
   **Terrain 1a** (large) was started on 2026-11-01 in the cloud, ahead of this slot, at the owner's request, with the NoData guard of
   `TerrainSafetyLines` (a single vertex such as z = −9999 near a source refines until memory runs out; count the band's
   3D area, not only the horizontal one).
3. Meshing done (large): M2 (#233), M3 (#252) and the oracle speedup (#265) merged on 2026-11-01; #272 and #274 merged.
   `m2.snap` (snap rounding with restore, refinement on the grid, §3g) merged in #277 on 2026-11-02; the reference
   comparison's set D starts now.
   Hull edges on the grid (owner 2026-11-02, [design/MESHING2D_HULL_GRID.md](design/MESHING2D_HULL_GRID.md), #310): option C, snap-round the hull edges as input segments (A as fallback); hull parts narrower than a pixel may collapse and are reported (Q17); acceptance the 192-run fuzz (master 119/192). Herne finding 2 (oracle merge zone hull-segment) in #309.
   Merged 2026-11-02: #309 (Herne finding 2: oracle merge zone hull-segment, acute side only), #312 (oracle domain area without O(E²): debug2 50 min → 72 s); #315 (option C, hull edges on the grid: fuzz 192/192, Herne 9/9; Q17, domains narrower than a pixel everywhere, stays open). Reference control run (set D, Herne, debug2) started on master a71171b8. #319 merged (oracle: O11-unsplittable exemptions, owner option B; corpus at 0/20/25/30 330/330, the exemption fires once; `mesh --no-oracle`, `oracle --stated-area`). The slow comparison rows were oracle time (water-huge3 73.7 s total, mesher 0.10 s); oracle speed merged in #321 (own grid, the oracle stays library-free: owner 2026-11-02; 373600 q20 69 → 15 s, q30 101 → 27 s, debug2 66 → 12 s, verdicts and details unchanged on 420 corpus runs). Off-centres merged in #322 (2026-11-02; only for triangles bad by their angle, circumcentre fallback; corpus vertices −0.4 to −0.8 % at 20-30°). The big reference run (ours, the fork, triangle.c with and without -D, all prepared inputs; 4869 cases, finished 2026-11-03 on f3d92da): ours 4854 VALID / 8 INVALID (7 BoundaryNotConstrained in GEOS overlay isochrone/stmlf, the fork fails the same 7, triangle.c is valid on 2; f-17/b needs --stated-area), fork 4661 / 202, triangle.c 2499 valid with 1853 aborts; only ours valid on 177 cases; triangle-count outliers under triage (f-10/c, f-11/c-d, TestCentroid/33, earcut jhl, stmlf/447 fewer; f-11/a-b, f-40 more: grading); snap-path times still 4-8x the fork's after #328. Control run done (all Herne snap runs VALID); mesher-only times (`--no-oracle`): on par with the reference without snap, 5-20× slower with snap (373600 q20-snap 5.5 s vs 0.64 s); profiled: about half in `BelowFloor` (BigInteger pixel indices recomputed per check), a per-vertex cache gives 7.5-8.3 → 4.4-4.7 s byte-identical: a snap-path speed PR after #322; merged in #328 (2026-11-02). Lens spike done (constrained quality, lenses to 25.65° = arcsin(√3/4), Shewchuk 2002 §6.4; circles above): catalogue vertices −7.2 %, corpus 20° 116k → 94k vertices, Herne 373600 q20-snap 93 320 → 62 303 triangles (reference 57 890), all pass; the lens PR comes as (B), without vertex deletion, provenance "unproven", with a requeue fix for a latent split defect (owner 2026-11-02; deletion (A) only if a real case fails to terminate). Then lenses for Conforming=false (owner 2026-11-02: a second mode "constrained quality" judged as constrained Delaunay, O2 unchanged; an own optional input key so existing input_sha256 stay; lenses up to the proven termination bound of Shewchuk 2002, circles above). Raw alpha wrap fix A merged in #311 (59196 min angle 0° → 4.79°). **Merged 2026-11-02:** #328 (snap-path speed), #332 (lenses (B) in the constrained-quality mode: catalogue vertices −7.2 %, corpus 20° 116k → 94k vertices, Herne 373600 q20-snap 93 320 → 62 303 triangles; **intentional behaviour change: the default output of `QualityMesh2` with `Conforming = false` changes**), #334 (grid-hole vertex renumbering bugfix). #337 (grid reroute: retag the triangles a reroute sweeps, merged 2026-11-03). T14 (conforming hole seeding: constraints inside holes queued) merged in #340, T13 (the oracle tolerates hull corners within the tube; one verdict changes, robustness1 M3@0 constrained) in #342, docs #345 (2026-11-03). Done criterion (owner 2026-11-03): correctness, grading and speed at reference level; provisional targets: triangle count ≤ 1.25x triangle.c on f-11/a, f-11/b, f-40 and a corpus sample at the same bound, Herne snap runs ≤ 2x the fork's mesher-only time, no verdict worse. Order: oracle speed, vertex deletion + T15, Q17, lenses (A), grading, snap speed. Merged 2026-11-03: #348 (the ForcedByInputAngle excuse narrowed to the Shewchuk 2002 §6.3 bound), #351 (oracle speed: a grid and a double tube filter, GEOS shapely-829/1 714 s → 20 s and now VALID, verdicts identical elsewhere). T15 merged in #353 (Delaunay vertex deletion in the kernel: ear clipping with exact orientations + Lawson flips, Devillers 1999/2002; hole content carved before refinement; corpus, rows and Herne identical to master). Q17 merged in #358 (2026-11-03; the distance rule was replaced, the review found it both too loose and too strict): the oracle builds the iterated snap rounding image (Halperin & Packer 2002) of the input over the hot pixels and excuses a missing stretch or an unused input point only on parts that collapse in that image; plus a check that no boundary subsegment of the mesher's own output passes the interior of another hot pixel (Hobby 1999). The image starts with the input segment itself (review finding: rounding only the line between the endpoint pixel centres excused a 1.27 px strip and rejected a correct mesh); with that, the zigzag cases with refinement (M3) are VALID in every frame. Owner decision 2026-11-03: strict, the refinement must not collapse what the rounding of the input keeps; the oracle accepts collapse only where the image with input hot pixels alone collapses (output vertices may reroute, never create a collapse). The zigzag cases with refinement (z0, z1: 23.5 and 169 px² pinched by grid points) are pinned INVALID in the Q17 PR; a mesher bugfix PR right after it rejects grid points whose pixel would join two input-image chains (upper bound: 0 such points on Herne and the catalogue). Then a PR for the oracle's hull model for hull domains and the coverage tolerance (2 · L1 perimeter · 1 px checks almost nothing on narrow domains). Iterated rounding can depend on the order (Hershberger 2013); no case is known here. If one appears, the mesher returns its collapsed double chains as a certificate that the oracle checks (certifying algorithms, McConnell et al. 2011), preferred over forbidding grid points there. Pinned in #358: 5 refinement pinches (StarPolygonFuzz), zigzag M3, 9 restore false rejections and 3 false acceptances (the oracle lacks the SRR restore step; at M2 the input-only image equals the full image as an interim). Mesher bugfix merged in #360 (refuse a reroute through a refinement vertex onto another chain's constraint edge; hull edges as owners in the oracle's GridUnsplittable; Herne R/U 1011/24, +0.4 to 6.2 % triangles as the new grading baseline; mesher-only Herne times −9 to +15 %; oracle Herne 90 → 130 s after a speed fix; 373600 q30 oracle 46 s noted for the hull-model PR. The oracle's own SRR restore (Belussi et al. 2016, independent of SnapRounding2) merged in #362: the restore pins flipped (10 false rejections VALID, 3 false acceptances and the M2 wedge rejected, one hidden false acceptance of the interim found); Prop. 4.14/D14 followed over Listing 2, the discrepancy recorded. Next: the hull model with coverage (a topological rule: no link shared by refinement, 0 on Herne with #360's rule; refinement pinches at a single vertex, 2-217 per Herne run and 13 in 12 fuzz inputs, are a known violation of the strict Q17 decision, counted with a ratchet), then the pinch fix in the mesher before the lenses plus coverage against the input-only domain). Merged 2026-11-04: #367 (oracle: hull model, RefinementCoincidence rule, RefinementPinch as a counted known violation with fuzz and Herne ratchets, ChainSkipsHotPixel, opt-in --snapped-area), #372 (bugfix: when every outer ring collapses in snapping the domain is empty, not the hull; 10 star-fuzz seeds), #370 (lenses (A): Chew's deletion before a bisection; cq Herne q20 -17/-175/-72 triangles; the Herne cq 371800 q20 pinch ratchet temporarily 85 -> 92, to be brought back to <= 85 by (1)). Measured 2026-11-04: every RefinementPinch vertex is a subsegment split point (none a circumcentre or off-centre), so (1) belongs in GridSplitPoint / ValidGridSplit; lenses cut the cq pinches (fuzz 11 -> 2, Herne 215/64/21 -> 92/44/11). #373 (pinch fix (1), 2026-11-04): a split may not reroute through a refinement vertex another chain already runs through (ValidGridSplit), with the oracle's GridUnsplittable mirror (no broader, may demand more); RefinementPinch is now a failing rule and every pinch count is 0 (fuzz 12 -> 0, all 12 Herne runs 0, the temporary 92 gone). Costs: unsplittable grid subsegments about double, triangles -0.5 to +4.7 % (cq 371800 q20), Herne oracle total 135 -> 159 s (outside CI, no budget set). Grading step 1 merged in #375 (2026-11-04, details below). Next: the **fast oracle** (owner 2026-11-04): the oracle's fast path at most 2x the mesher's time per run (today roughly 10-20x on Herne, 159 s total). Approach after certifying algorithms (McConnell, Mehlhorn, Naeher, Schweitzer 2011; the LEDA checkers): prove a valid planar triangulation of the domain (orientations, edge manifoldness, exact coverage), then the local (constrained) Delaunay test per edge; by the Delaunay lemma that replaces the global triangle-vertex pass. The input-only rounding and the Q17 chain / hot-pixel rules stay, made linear with filters and indexes. Every replacement needs an equivalence argument; the reference path (all exact, global) stays and Meshing2OracleModesTests keeps comparing them. Measure per check first; stop rule: if more than 4x remains after the two largest items, decide on numbers before building further. Measured on master c0f7de6e: Herne oracle 6.0x the mesher in total (2.0-11.3x per run; quality 37 %, of which the merge-zone distance 45 % and GridBlocked rationals 36 %; Delaunay 24 %, needle fallbacks), corpus 34x (406 runs; coverage 35 %, O(triangles x boundary edges) in NearBoundary/Classify; small inputs with ms mesher times). Order: (1) one boundary index for coverage and conformity, (2) a source index and double filters in front of the exact quality tests, then the stop-rule report (per-run ratios only where the mesher takes >= 100 ms, small runs with absolute times), then (3) Delaunay: the local test needs a completion witness for the conforming mode (empty circles across holes and concave boundaries), built by the oracle itself. (1)+(2) merged in #376 (2026-11-04, `docs/design/MESHING2D_FAST_ORACLE.md`): verdicts identical on Herne, corpus and catalogue; Herne oracle 153.6 -> 111.8 s (5.6x -> 4.1x the mesher, worst run 8.1x, median 1.9x), corpus 36.5x -> 24.7x (default) and 20.5x -> 14.8x (cq); a check mode compares every index and filter with its scan (box-level superset); one intended output difference (a hull domain without edges no longer throws). The stop rule fired (> 4x left); owner decision 2026-11-04: go on with (3), the local Delaunay test with an oracle-built completion witness, then the corpus items (segments, conformity); target redefined: <= 2x the mesher per run where the mesher takes >= 100 ms, <= 1 s oracle per smaller run; new stop rule: if (3) does not bring Herne to <= 2.5x in total, stop and go to grading step 2 (design: `MESHING2D_GRADING.md` section 4, #377). (3) merged in #379 (2026-11-04): the oracle's own Delaunay triangulation of the used vertices, certified (orientation, manifold edges, a linear walk of the boundary along the hull, local Delaunay per edge; any failure or exception falls back to the full pass), the old pass only on the output triangles not in it; verdicts identical; Herne 4.1x -> 3.6x, so the stop rule fired and the fast oracle ends here. Left for a later pick-up: 373600 q30 delaunay 9.8 s = local test 1.9 + witness 0.9 + pass on the ~4400 remaining needles 7.5 (2.6 M in-circle hits, almost all merge-zone excused: ExcusedOffSegment 3.2, GridUnsplittable memo 3.1); corpus segments and conformity. Next mesher speed item outside grading: SplitSegment (3.4 s of 7.3 s refinement on 373600 q30). Finish plan (owner 2026-11-04): grading step 2 (petal candidates, Erten and Üngör 2007, scored as in step 1) and the exact double filters in front of SnapGrid.Pixel / CrossesInterior in one PR (Herne mesher −11.9 % vs pre-grading with −26 % triangles; f-11/a 215, f-11/b 363, f-40 184), with Herne snap runs measured against the fork's mesher-only times; then one last grading step time-boxed to one day; target revised by the owner (2026-11-04): a short look whether 1.25x is cheap, otherwise triangle count <= 1.5x the reference (1.4x welcome) on f-11/a, f-11/b, f-40 and the corpus sample. Step 3 tried (2026-11-04, about 2.5 h): postponing near-bound triangles, stricter floors, a radius bonus, candidates beyond the circumcentre, other queue orders; the best general lever (smallest area first) gives f-11/a 1.69x, f-11/b 1.51x but f-40 1.55x; small cases swing ±15 % with the settings. Owner decision 2026-11-04: stop; #380 is the grading result (f-40 1.42x reached, f-11 1.8-2.0x stays open). Diagnosis for a later project: about 5 vertices per octave against the reference's 2.7, meshes better than needed (median smallest angle 31° at a 20° bound), so closing it needs a different placement strategy, not a lever on the choice. #380 merged (2026-11-04, grading step 2 + SnapGrid filters). Next: SplitSegment for the <= 2x fork speed target (cq 373600 q30 3.1x, q20 4.0x after #380); measured upper bound: even with no grid split-point work and half the choice, q30 stays at about 2.3x (we are about 2.4x slower per triangle and make 1.34x the triangles), so a small exact speed PR (pending stack reuse, generation marks in TrianglesNear, a GridSplitPoint cache) comes first, then the target goes back to the owner. Complexity check (mesher, 2026-11-04): everything per insertion is local (cavity, about 10 candidates, grid checks constant; queue O(log n); 1-2 grid re-sweeps); the one non-optimal part, point location walking from the last insertion (about sqrt(n) steps each, O(n^1.5) in total under worst-first), is fixed in the speed PR by handing the kernel the triangle the refiner's walk found (time-optimal reference: Har-Peled and Üngör 2005). **Further options for the triangle count (and so speed), not scheduled** (owner asked for an assessment; recorded 2026-11-04), by expected gain for effort: (1) **coarsening after refinement**: delete free Steiner vertices while every affected triangle keeps the angle bound and all Q17/grid guarantees, with the kernel's exact Delaunay vertex deletion (T15); aims straight at the diagnosis (5 vs 2.7 vertices per octave, median smallest angle 31° at 20°); literature first (Delaunay coarsening, e.g. Miller, Talmor, Teng), risk: chain and pinch rules on the grid; a time-boxed probe behind a switch (f-11, f-40, Herne, corpus on triangles, time, verdicts) would settle it; (2) an explicit size function h = c·LFS with a growth parameter (`delaunay/LFS` exists; termination with both criteria to show); (3) one point per ring of bad triangles instead of per triangle (the conceptually right fix for f-11, a research project); (4) quadtree meshing (Bern, Eppstein, Gilbert: size-optimal but worse angles, a step back); (5) smoothing (Lloyd/CVT/ODT: better quality, not fewer triangles; only with (1)). Yardstick for 'fewer': an LFS-based lower bound, not the reference alone; fewer is not better by itself, our extra angle margin helps FEM and interpolation. Speed PR merged in #384 (2026-11-04: allocations, then point location from the triangle the refiner found; Herne mesher 21.4 -> 19.3 s, cq 373600 q30 2.7x / q20 3.6x the fork; 21 golden M3 rows changed through the slot order of on-edge insertions, all oracle-valid). Broad reference comparison (4869 M2 cases as on 2026-11-03 plus 1131 M3 runs at 20/25/30 degrees) and the provenance delta 6b-3 (stages 6b/6b-2 were done on 2026-11-02 in #295/#296, no (A) finding) run in the reference session on master d9006aad. **Owner 2026-11-04:** coarsening (1) is the next mesher project, planned for the week of 2026-11-09 (no budget this week). The <= 2x fork speed target stays open as a future goal (after the speed PR about 2.7-2.8x on cq 373600 q30, about 3.4x on q20); no further work toward it after the speed PR; fewer triangles from coarsening are the expected route. Benefits: CI time (the mesher tests are half of it), faster Herne iterations for grading, and a verified-meshing option (every result with a certificate). Then grading step 2, including a wider candidate search (another pixel instead of giving up; the transaction idea of #368) to win back the extra unsplittables. Grading step 1 (merged in #375, 2026-11-04, `docs/design/MESHING2D_GRADING.md`; review fixes: grid rounding guarded at 2^52 pixels, no chosen point on a constrained edge): bad triangles worst first, and the split point of a triangle bad by its angle the best of the default point, the circumcentre and 6 points on the bisector, never with a smaller insertion radius than the default point; f-11/a 256 → 217, f-11/b 456 → 379, f-40 276 → 245 triangles (reference 108 / 205 / 130: still 1.9-2.0×, **the 1.25× target stays open**); corpus −0.6 to −9.0 %, catalogue −1.3/−1.4 %, Herne −12.4 % (all 12 VALID, pinches 0), no verdict worse; mesher time on Herne +26 % in total (−9 to +48 % per run; oracle −7 %), accepted as a known cost (coordinator 2026-11-04), with a **speed stop rule: the next grading step brings the mesher back to within +10 % of pre-grading on Herne, or says why not** (e.g. scoring incrementally instead of a cavity search per candidate); **intentional behaviour change: the output of `QualityMesh2` with `MinAngle > 0` changes** (both modes). Next grading steps: a 2D candidate region (Erten and Üngör 2009, once the paper can be read), the bad-triangle criterion, the cq churn of lens deletion (A). Earlier order: Owner 2026-11-03: lenses (A) first, then the constrained-quality bug (StarPolygonFuzz seeds 37 and 41: triangles outside the ring, unsourced subsegments; pinned strictly), then the pinch fix (1). Candidate for (1) from the cross-field report (#368, idea 2): accept a refinement split as a transaction only if it keeps the input-only embedding, checked by incidences and vertex sectors in the closure of the reroutes it induces, not by edge overlap (Hershberger, *Snap Rounding: A Cautionary Tale*, SoCG 2025, documents pinches missed this way in practice); stop rule: drop it if it reduces to the rejected pixel-owner rule (acute corners over-rejected). Open: the 2-2.5x triangle count on f-11/f-40 (grading; off-centre use under review), the 12 tracker cases merged in #344 (60 runs, all pass); big-run triage done (the 7 INVALID are resolved by #342; the fewer-triangle cases are by spec, O11 merges within the automatic tolerance); T15 (owner decision 2026-11-03: (a), carve the hole content before refinement by Delaunay vertex deletion in the kernel (Chew 1990, Devillers 1999); O2 reads "the vertices of the output's triangles"; the same building block then unlocks lenses (A) with deletion, Shewchuk 2002 §6, provenance proven), Q17.
   Diametral lenses dropped (owner 2026-11-02, measured): under Conforming (O2) they pass 46 of 55 catalogue M3 rows and 25–36 of 84 corpus inputs (NotDelaunayGlobal), hit the Steiner limit at high bounds and double the time; lenses for Conforming=false next in the meshing session after off-centres (owner 2026-11-02): the library default mode, where diametral-circle splits dominate the Herne meshes (1.6–3.4× the reference's triangles), with its own oracle rows (constrained Delaunay). Off-centres (Üngör 2004) go ahead: corpus vertices −0.9/−0.8/−0.6 % at 20/25/30°. Oracle gap (pre-existing, earcut/water-huge at 25°, 0.84° corner): option (B), the three grid exemptions mirrored for O11-unsplittable pieces (both ends within the tube of another constraint), one shared predicate, excused counts reported per row, 25° added to the corpus angles; its own PR (bugfix).
   Reference comparisons (owner 2026-11-02): a small control run (set D, Herne, debug2) once the Herne and debug2 fixes are merged; the big run with the new inputs (CDT rest, Natural Earth polygons, GEOS XML, DGM1, CGAL data) after off-centres (#322), with Shewchuk's `triangle.c` 1.6 as a second black-box reference (owner 2026-11-02; with and without -D; in the control inputs it crashes on every Herne -q run and aborts on f-14/b, f-14/c, sketchup).
   Reference comparison analysed in [design/MESHING2D_REFERENCE_COMPARISON.md](design/MESHING2D_REFERENCE_COMPARISON.md) (#293): one real
   defect (CDT issue-211 at M3 20°, bugfix next). Owner 2026-11-02: refinement with diametral lenses plus off-centres (Chew 1989,
   Shewchuk 2002, Üngör 2004), its own PR after the bugfix, measured before/after. Stage 6b done: no copied expression against
   Triangle.NET (#295) nor against `triangle.c` 1.6 directly (#296); dirty evidence in `g3-meshing-references`, branch `claude/provenance-6b`.
   The **stage 6b provenance check** (medium, report only; started 2026-11-02 after #277, session "Stage 6b: meshing
   provenance check"; clean report as docs PR, dirty evidence in `g3-meshing-references`) runs once after `m2.snap` (with refinement on the grid) is
   merged, so that one pass covers the whole 2D mesher (owner 2026-11-01); its own session, not the reference session,
   which must stay independent of our code.
   Also a small **snap-rounding Q&A** session (owner decision 2026-11-01): the dirty side reads the owner's own commits
   in his Triangle.NET fork (written privately with Claude, owner's rights) and answers the meshing session's written
   questions in spec terms only (behaviour, edge cases, failures, parameters, test inputs as descriptions; no code,
   structure or names). The coordinator checks the report before the meshing session gets it; it is logged in the
   provenance log and SOURCES.md. The meshing session never reads the fork. Starts once the question list is in
   MESHING2D_SNAP.md.
4. **Started 2026-11-02:** the wrap-resolution session (#280 merged: C1 patch bound, wrap.simplify −69 %, results
   identical) continues with **stage 7b** (owner); **CLI / MCP docs** (stage 8, medium) run in their own session in the
   slot of the envelope session (stack #250-#268 merged, session archived).
   After 7b, in the same session (owner 2026-11-02): one bundled Reducer measurement, upper bounds only, from
   [design/RESEARCH_REDUCER_REMESHER.md](design/RESEARCH_REDUCER_REMESHER.md) (#286): `MaxDeviation` by speculative
   parallel certification (bit-identical) and an adaptive permission-grid accept filter; the self-intersection check
   (73 % in `TriangleHashGrid.Query` on the wrap meshes) by one union-box query per collapse. Owner 2026-11-02: the same union-box lever is measured for
   `Remesher.PreventSelfIntersections` too (6-10x after #298; every other lever closed there). Building needs the
   owner's go. The research session reads the full texts now (hosts allowed by the owner).
   Reducer A merged in #324 (2026-11-02): −11 to −37 % on the reducer cases, `SimplifyWithin` −10.6 %.
   Also started 2026-11-02, above the session limit by owner exception: a medium **Remesher session** for the cost of
   `PreventSelfIntersections` (about 17x; levers: the coplanar 2D shortcut, unblocked since #249/#256, and a clearance
   filter), profile and upper bounds first. A review-only session only when the stacks and the meshing PRs wait for review at the
   same time.
5. Decided 2026-11-01: stage 7b follows the wrap-resolution session (see 7b). The question whether a large performance
   session pauses for Terrain is moot: the boolean-performance session finished (#256) and Terrain runs.

- At most 5 implementation sessions at once in the cloud, of which at most 2 large (tier L; [AGENT_WORKFLOW.md](AGENT_WORKFLOW.md)
  section 6), plus **one review session** that only reviews (independent reviews with REVIEW_CHECKLIST.md, bounds
  reviews, re-running numbers) and writes no production code. The owner's local work does not count. The limit
  applies to new starts: when sessions finish, they are not replaced until the count is below it. Each session has
  its own worktree and one PR-sized topic.
- The bottleneck is review, serial merges and the mutation runners (a full shard takes hours), not sessions: a
  session whose PR waits for review does not start the next PR-sized topic in a mutated file.
- Parallel sessions must not change the same production files. Quadtree (`spatial/`) and meshing (`comp_geom/`) are
  disjoint in code; they run at the same time because the owner works on the quadtree locally and the review session
  takes the meshing reviews.
- Merges are serial, because many PRs touch `docs/ROADMAP.md`, `docs/QA.md`, `docs/SOURCES.md`, `docs/OPERATIONS.md`
  and `ExactBooleanIssueCasesTests.cs`. Rebase the next PR and re-run CI after each merge. Keep status text in the
  PR, not in the roadmap.
- Full local CI and every timing or memory measurement run alone on the machine: the sweep's time limits react to
  load, and measurements taken next to other work are not comparable.

Open task from the takeover:

- [x] **Exact coplanar triangle intersection** (no longer deferred: `DMeshAABBTree3.TestIntersection` / `FindAllIntersections`
  and `MeshQueries.TrianglesIntersection` call it). `IntrTriangle3Triangle3` decides coplanar, nearly parallel and sliver pairs with
  `ExactTriangleIntersection`, and `Find()` builds the contact from exactly decided pieces, rounding only the crossing points, so
  `Test()` and `Find()` agree (row R, item 2b, #253). The 2D clipping of `IntrTriangle2Triangle2` is unchanged; exact output
  coordinates stay open.

## Shared hotspots across sessions

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

## Open tasks and where their answer may already be

Open tasks further up, and work further down, in the code or in the research lists, that may solve them. Read the right-hand side before starting the task on the left. Each row is a lead to check, not a result: none of these was tried here.

| Open task | Look at first | What it may give, and what it does not |
|---|---|---|
| Open inputs: winding numbers across patches (Phase 2), heuristic without clear gain | Phase 5 #5: Feng & Crane 2024, signed distance from geometry with holes and self-intersections, no repair first | Another way to decide inside and outside for open inputs, in place of tuning the propagation. Feng, Gillespie, Crane 2023 has the integer jumps, but for curves on a surface, so it is a model and not a direct fit for patches in 3D. geometry-central documents a signed heat solver: check its license before reading it |
| Remeshers leave the input surface, and the quality pass after `SimplifyAlphaWrap` (Phase 2) | Phase 5 #2: envelope containment, which its authors built into two surface remeshers. `MeshEnvelope` is the prototype | A guarantee to stay near the input in every local operation, so quality passes cannot trade away fidelity. It does not by itself prevent self-intersections: the per-operation intersection check stays a separate task |
| `Reducer`: `MaxDeviation` takes 92-97 % of the time (Phase 5 #2) | The same item | With the two rows above, and the envelope pipeline under Open research questions, one piece of work serves four tasks. So it comes first in Phase 5 once it is scheduled |
| Remeshing folds walls through each other (Phase 2) | `MeshWallThickness` (exists) for the thickness-aware sizing. Phase 5 #8, continuous collision detection, only if a check of the end state proves not to be enough | Sizing below the local wall thickness uses what we have. Whether a move that passes through a wall can end in a state a static check accepts is to be measured first |
| Sharp vertices are not pinned (Phase 2) | `MeshCurvature` (exists): angle deficit, handles boundary and bowtie vertices | The measure this task asks for is already there |
| Golden masters across runtimes ([strategy](design/ACCEPTANCE_STRATEGY.md), section 4) | Open research questions: bit-for-bit determinism | The same problem, seen first in the prism fixtures. What the golden subset finds is material for the question |
| Boolean property tests (below) | Open research questions: metamorphic testing | Same method. Write them so that the generators can serve both |

## Geogram as a source (owner 2026-11-03)

[Geogram](https://github.com/BrunoLevy/geogram) (Bruno Lévy, Inria) is BSD-3-Clause (`LICENSE` checked 2026-11-03,
"Copyright (c) 2000-2022 Inria"). Its own code may be read, analysed and ported; this replaces the earlier
"Geogram not read" notes (SOURCES.md, envelope). Rules:

- Only files under `src/lib/geogram` whose header carries the Inria BSD-3 notice; check each file's header before
  reading it.
- Never read `third_party/triangle` or the wrapper `delaunay/delaunay_triangle.*` (Triangle's non-free license and
  the mesher's clean room); `third_party/tetgen`, `delaunay/delaunay_tetgen.*` and whatever calls TetGen
  (AGPL) stay unread as well. Every other `third_party` library only after checking its own license (PoissonRecon,
  xatlas, HLBFGS, amgcl, OpenNL and so on).
- Ported code keeps the BSD-3 notice: the file header names the geogram source file and version, `docs/SOURCES.md`
  gets a row, and the package ships the notice (a `THIRD_PARTY_NOTICES` file; BSD-3 permits redistribution
  under BSL-1.0 with the notice). A port is a decision with an A/B, like any other change; reading for ideas
  needs no decision.
- Port into the exact code only under the exact-code rules (no FMA, no `float`, review of every bound).
- The mesher logs every geogram file it reads in MESHING2D_PROVENANCE.md, and the provenance check (6b) covers it.

Open items where geogram has an implementation to compare with or port from (to check first, not yet read):

| Open item | Geogram (to check) |
|---|---|
| Boolean performance (B) and the exact `Orient3d` hotspot ("kernel B", 1-limb fast path) | `numerics/PCK`, `expansion_nt`, `multi_precision`, `exact_geometry`, `interval_nt`: expansion arithmetic with an interval filter and homogeneous exact points |
| Mesh arrangements and booleans; SafeRounding and the research item "Practical 3D snap rounding" | `mesh/mesh_surface_intersection*`, `mesh_CSG*`, `triangle_intersection` (Lévy 2024/25, exact predicates and constructions for mesh CSG; SAFE_ROUNDING.md notes that it does not solve the rounding) |
| 2D mesher: snap speed, the exact kernel, constraint intersections | `delaunay/CDT_2d` (Lévy's own constrained Delaunay with exact predicates and constraint crossings; not the Triangle wrapper) |
| 2D mesher grading (2-2.5x triangle count on f-11 / f-40) | `delaunay/LFS` (local feature size) |
| Phase 5 #4: tetrahedral meshing | `delaunay/delaunay_3d`, `parallel_delaunay_3d` (own code; not the TetGen path) |
| Remesher quality and speed, Reducer | `mesh/mesh_remesh` (CVT / restricted Voronoi diagrams, `voronoi/`), `mesh_decimate`, `mesh_smoothing` |
| Mesh repair and hole filling (Phase 2) | `mesh/mesh_repair`, `mesh_fill_holes`, `mesh_preprocessing` |
| Parameterization (Phase 3) | `parameterization/` (LSCM, ABF++ and others) |
| Point clouds ("Later") | `points/` (Co3Ne and others) |

## Other permissive libraries (survey 2026-11-03, owner request)

From [github.com/topics/geometry-processing](https://github.com/topics/geometry-processing), the first three pages by
stars. Each license comes from the repository's LICENSE file on its default branch, checked 2026-11-03. The same
rules as for geogram apply: check the per-file headers and the bundled third-party code before reading, a port
keeps the notice and gets a row in SOURCES.md, and the mesher logs what it reads.

| Library | License | Open items it may serve (to check, not yet read) |
|---|---|---|
| [elalish/manifold](https://github.com/elalish/manifold) | Apache-2.0 | Boolean performance (B): robust, fast mesh booleans in floating point with symbolic perturbation; a benchmark opponent and a source of ideas for the exact path |
| [pmp-library](https://github.com/pmp-library/pmp-library) | MIT | Remesher (uniform and adaptive remeshing), Reducer (decimation), hole filling, smoothing, parameterization |
| [hhoppe/Mesh-processing-library](https://github.com/hhoppe/Mesh-processing-library) | MIT (Microsoft) | Reducer and simplification: mesh optimization and progressive meshes (Hoppe 1993/1996), the authors' own code |
| [adobe/lagrange](https://github.com/adobe/lagrange) | Apache-2.0 | Mesh cleanup and repair (Phase 2), winding numbers (Phase 5 #5), remeshing |
| [nmwsharp/geometry-central](https://github.com/nmwsharp/geometry-central) | MIT | Already a source (Phase 3, intrinsic triangulations, heat method); the affine heat method (awards table) |
| [mlivesu/cinolib](https://github.com/mlivesu/cinolib) | MIT | Tetrahedral and hexahedral meshes (Phase 5 #4), polygon and polyhedral mesh utilities |
| [wjakob/instant-meshes](https://github.com/wjakob/instant-meshes) | BSD-3 | Quad remeshing ("Later") |
| [liminchen/OptCuts](https://github.com/liminchen/OptCuts) | MIT | Parameterization seams (Phase 3) |
| [patr-schm/TinyAD](https://github.com/patr-schm/TinyAD) | MIT | Foldover-free maps and other optimization (Phase 5 #7) |
| [microsoft/DirectXMesh](https://github.com/microsoft/DirectXMesh) | MIT | Vertex cache optimization and meshlets; no open item yet |
| [owensgroup/RXMesh](https://github.com/owensgroup/RXMesh) | BSD-2 | GPU mesh processing; ideas for parallel patterns only |
| [iShape-Rust/iOverlay](https://github.com/iShape-Rust/iOverlay) | MIT | 2D polygon booleans; Clipper2 already covers this |

Not usable: CGAL (GPL); libigl, TetWild and TriWild (MPL-2.0: not ported, implement from the paper); Easy3D and
QuadWild (GPL-3); DGtal (LGPL-3); mcut (LGPL-3 or commercial); MeshLib (non-commercial license); trueform (dual
license, non-commercial or commercial). PyMesh, ManifoldPlus and Directional: no LICENSE file found, so not used
until a license is found. The rest of the first three pages are learning or application projects without a
library to port.

## Phase 1: foundation

- [x] Targets net10.0 and netstandard2.0
- [x] CI on GitHub Actions: tests on net10.0 (Linux) and net48 (netstandard2.0 build, Windows), pack, package smoke test
- [x] Package metadata: SPDX license, README, XML docs, symbols
- [x] Mesh formats PLY, 3MF, glTF/GLB (read and write)
- Package ID and publishing: moved to Release, after the library "just works"
- [x] API docs and examples per operation ([OPERATIONS.md](OPERATIONS.md), examples run as tests)

- [x] Fork feature overview: [Additions in this fork](../README.md#additions-in-this-fork), grouped by capability
  with links to implemented operations and integration guides.

## Phase 2: robustness

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
- [ ] Exact boolean performance, ranked ideas in [EXACT_BOOLEAN_PERFORMANCE.md](perf/EXACT_BOOLEAN_PERFORMANCE.md), section 3. The first idea, exact
  arithmetic without `BigInteger`, starts from two unmerged branches: `claude/int256-spike` (`Int384` planes, not for merging as it is) and
  `claude/collinear-shortcut` (reviewed, with tests, at most 2 % on the input classes, below the 10 % bar). Exact code, so it goes to the owner. Keep both branches.

| Feature | Research / port source | Why |
|---|---|---|
| Exact, robust mesh booleans and arrangements | Zhou et al. 2016 *Mesh Arrangements for Solid Geometry*; Cherchi et al. 2020/2022 (indirect predicates); Trettner et al. 2022 *EMBER*; Manifold (Apache-2.0) | The biggest gap; the existing `MeshBoolean` is not robust, the winding-number variant is voxel based. `ExactPredicates` is the base. |
| Alpha wrapping / shrink wrap | Portaneri et al. 2022 *Alpha Wrapping with an Offset* | Guaranteed watertight manifold output from any input: scan repair, 3D printing, biomedical CAD. Complements `MeshAutoRepair`. |

## Phase 3: parameterization and geodesics

- [x] Parameterization: LSCM, ARAP, SLIM (Rabinovich et al. 2017), Boundary First Flattening (Sawhney & Crane 2017);
  UV packing; distortion measures; sparse Cholesky solver (`SparseCholesky`)
- [x] Cutting closed surfaces into discs (seams) and automatic chart segmentation; BFF with cone singularities:
  `MeshDiscCut` (tree-cotree cut graph with shortest loops, Eppstein; Erickson & Whittlesey; any genus, boundaries,
  components, bowties; vertices split along the seams), `MeshChartSegmentation` (normal-based region growing with
  compactness, VSA / D-Charts style), `ConeFlattening` (BFF with greedily placed cones; a cube's corners become cones
  and it unfolds without distortion), `UVOverlap` (exact overlap test), and the pipeline `UVAtlasGenerator`
  (cones where overlap-free, else charts split until within the distortion limits, packing, status code);
  `g3cli uv`. Cones are not guaranteed globally injective (eg on a torus); the pipeline then falls back to charts.
- [x] Intrinsic Delaunay triangulations (Sharp & Crane), for stable Laplacians and heat on poor meshes:
  `IntrinsicTriangulation` (edge flips with signposts, tracing of intrinsic edges over the input mesh,
  intrinsic mollification of degenerate triangles); `MeshGeodesicDistance.IntrinsicDelaunay`. On a plane of
  169-degree triangles the mean distance error drops from 2.2 to 0.06 edge lengths.
- [x] Vector Heat Method (Sharp, Soliman, Crane 2019), including a robust log map (beyond `MeshLocalParam`):
  `VectorHeatMethod` (transport, scalar extension, log map), on the intrinsic Delaunay triangulation,
  with the direct solver `SparseCholesky`.
  - [ ] **Near-field log map from affine heat (priority: medium-low, small).** Brief 2 (#371) failed its global
    gate only toward the cut locus. Near the source, AffineLocal is far more accurate than the current log map
    ([AFFINE_LOGMAP_GATE.md](design/AFFINE_LOGMAP_GATE.md)): exact on the plane where Legacy errs by 0.03-0.07; on
    the sphere for r < π/4 a mean of 0.0024 against 0.028; on the cylinder's first two bands 0.0006-0.0015 against
    0.05-0.08. Queries are also 2-3 times faster. Use: local parameterization (decals, texture stamps, surface
    brushes, local polar coordinates, geodesic means), the role of `MeshLocalParam` (discrete exponential map,
    Schmidt 2006). Task: a new gate on that use, comparing AffineLocal with `MeshLocalParam` and Legacy within a patch
    radius, on the smooth fixtures plus noisy and anisotropic meshes. If it wins clearly, ship it as
    `ComputeLogMap(int, LogMapStrategy.AffineLocal)` with its documented range ("accurate near the source, not for
    distances up to the cut locus"). Prior art: geometry-central offers AffineLocal exactly as the fast near-source
    variant. The code is on branch `agents/implement-logarithmic-map-brief-2` at f8a5f388; reuse it, do not port
    again. Why medium-low: no library caller (the value is API for users), after the mesher queue and the
    robustness and CI work; a good owner-local brief.
  - [ ] **The current log map is not exact on a plane (priority: low).** It errs by 0.03-0.07 on the planar grid
    of the #371 gate, up to 0.38 in the outer band. This is probably the heat method's own smoothing (time step
    ~h²). First compare with the Vector Heat paper's reported errors; treat it as a defect only if ours is clearly
    worse.
- [x] Intrinsic Delaunay *refinement* (vertex insertion): `IntrinsicTriangulation.DelaunayRefine` (insertion in
  faces and on edges with signpost updates, Chew / Ruppert circumcenter refinement with a minimum angle,
  boundary edges opposite obtuse angles split, concentric shells and skipped triangles at small input
  angles, status codes); options `IntrinsicRefine` in `MeshGeodesicDistance` and `VectorHeatMethod`,
  `IntrinsicTriangulation.BuildDelaunay` for the cotan Laplacian. All cotan weights non-negative also on the
  boundary; on the plane of 169-degree triangles the mean distance error drops from 0.06 to 0.02 edge
  lengths (max 0.8 to 0.1; `benchmarks/IntrinsicBenchmark`). Common subdivision by tracing (`ExtractCommonSubdivision`, area-checked, reports
  `Inconsistent` instead of wrong faces).
  - [ ] Integer (normal) coordinates (Gillespie, Sharp, Crane 2021) for a combinatorially exact common
    subdivision; signposts with the area checks suffice for the current operations

## Phase 4: platform layer

- [x] CLI / MCP server over the operations: `tools/g3cli` (measure, repair, boolean, resolve, remesh, simplify, uv,
  convert; JSON results with result codes; `g3cli mcp` over stdio)
- [x] Analysis building blocks: wall thickness (`MeshWallThickness`: ray, shape diameter function, inscribed ball /
  medial axis), deviation maps (`MeshDeviation`, statistics in `DeviationStatistics`), primitive fitting
  (`PrimitiveFit3`: plane, sphere, cylinder, cone; RANSAC + least squares)
- [x] Evaluation metrics, so that optimization decisions do not rest on Hausdorff distance and volume IoU alone (they
  miss lost features, reversed patches and topology changes): `MeshFidelityMetrics` (F-score at relative thresholds,
  Chamfer distance, normal consistency and the share of reversed normals, Edge Chamfer distance and edge
  precision / recall on sharp edges), `TriangleQualityStatistics` (Verdict metrics as distributions),
  `MeshTopologySummary` (components, Euler characteristic, genus, boundary loops, changes).
  - [x] In the benchmarks: MeshRepairBenchmark (`fm_*` fields, `analyze_fidelity.py` columns and flags, baseline on
    Thingi10K and Objaverse with the outer surface measures), RemeshBenchmark, UVAtlasBenchmark (UV quality, charts
    that are not discs)
  - [x] Volume IoU restricted to the outer surface: `MeshFidelityMetrics.OuterVolumeIoU` (solid of the reference's outer
    faces, classified in rounds, innermost interior faces first), next to `VolumeIoU`; `fm_oviou` in the benchmarks.
    Result on Objaverse (300 objects): the repaired count stays 65, and under the outer surface Hausdorff criterion it
    drops 121 → 115; the IoU misses are real volume losses, not interior faces (benchmarks/MeshRepairBenchmark README).
    The winding number IoU stays the criterion
  - [x] Curvature fidelity: `MeshCurvature` (Meyer et al. 2003: mixed areas, mean curvature normal, angle deficit;
    degenerate, boundary and bowtie vertices handled) and in `MeshFidelityMetrics` the curvature errors at corresponding
    points, the 1-Wasserstein distance of the distributions and the total absolute curvatures (`fm_h_*`, `fm_k_*`), in
    MeshRepairBenchmark, RemeshBenchmark and TerrainBenchmark (Hambach DGM1: the F-score is 1 for every TerrainGreedyTIN
    result, the curvature measures order them by MaxError)
- A showcase domain: terrain / GIS (in progress) and biomedical / orthotics
  - [ ] Slope construction and daylighting on TINs (open-pit mining; design in
    [design/TERRAIN.md](design/TERRAIN.md)): per-segment slopes, never steeper than specified (verified per
    triangle), catch line with the terrain, cut / fill volumes. Phase 1 in three PRs, each usable and tested on its
    own: **1a** the slope surface from the cone envelope plus the "never steeper" verification oracle (usable on its
    own to check existing results); **1b** the overlay with the terrain and the connected catch line; **1c**
    volumes, failure kinds, accuracy reporting, km-scale benchmarks. Berms after phase 1. Priority: before the
    planned remesher work; starts after the `ExactMeshBoolean` work that is being scoped now.
  - [ ] 2D spatial index: a loose quadtree over AABBs (TIN triangles, edges, points) with insert / remove, height
    queries and nearest-neighbour queries, complementing the existing hash grids. Clean-room from the behavioural
    spec [design/QUADTREE2D_SPEC.md](design/QUADTREE2D_SPEC.md) (owner decisions in its §7) and the literature only;
    targets 1M triangles built in about 100-260 ms, height query about 2.5 µs, on net48 and net8. Comes after the
    `ExactMeshBoolean` work and before terrain phase 1; may speed up the height-field check of
    `TerrainVolumeDifference`. Implemented, on `quadtree-takeover` (#195): see the record in the spec. Open: the timing, memory and side-by-side comparison on the owner's machine.
  - [ ] Public exact 2D meshing (constrained / conforming Delaunay, quality refinement with a size function, holes
    and regions by ring, snap rounding to an output grid), a clean-room replacement for Triangle.NET: from the
    behavioural spec [design/MESHING2D_SPEC.md](design/MESHING2D_SPEC.md) (owner decisions in its §5) and the
    papers only (Ruppert, Chew, Shewchuk 2002, Üngör, Miller-Pav-Walkington, Cheng-Dey-Shewchuk 2012); never
    from Triangle / Triangle.NET code. Builds on `ExactTriangulation2` and the adaptive predicates. M2 (#233), M3
    (#252) and the oracle speedup (#265) merged 2026-11-01; open: #272 and the stage 6b provenance check.

- [ ] Verifiable agent work ([QA.md 4a](QA.md#4a-making-agent-work-verifiable)): first the machine checks (new tests fail
  on old code, package validation against 1.0.324, banned APIs in exact code), together with a mutation shard per new
  robustness-critical module (the runs on PRs are incremental: tests/mutation/README.md). Acceptance
  against references for the 2D meshing ([strategy](design/ACCEPTANCE_STRATEGY.md)). The other items go with the modules they touch.
  - [x] Boolean metamorphic and property tests ([implementation plan](design/BOOLEAN_PROPERTY_TESTS.md), QA §4a
    items 9 and 10): volume identities, partition reconstruction, commutativity, vertex/triangle permutation and
    conforming subdivision; CsCheck campaigns with shrinking and replay on net10.0, the fixed corpus also on net48;
    analytic box oracle. Finding: `SafeRounding` moves exact vertices of pieces below its degenerate threshold
    (`ExactBooleanPropertyFindings`, skipped until fixed in its own tier-L PR).

## Focused follow-up work

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

**Test suite on German Windows (2026-11-04):** merged in #383 (test fixtures and pinned messages formatted invariantly, CRLF-normalized generated-file check, de-DE / en-US regression tests; the production .poly/.node/.ele writers were already invariant). CI shards rebalanced in #381 (envelope tests to net48 shard b: a 39 min, b 30 min; the mesher tests are the bulk of a). Open, small: `QualityMesh2CollapsedPartsReportTests.Oracle_ManyCollapsedPointsAreCheap` and `Predicates_MatchTheOracle_AtAllSizes` fail only in a full local run (pass in isolation): wall-clock budgets under CPU contention, the same class as the oracle timing test fixed with a DisableParallelization collection; check whether they belong there.

Plans, fact-checked on 2026-10-30; each states the owner decisions it still needs. The implementation or audit work remains open.
Tasks 4 to 6 can start independently once those are made; for task 4 they are made.

- [x] **4. DAP bug fixes (E.2):** [audit and adoption plan](design/DAP_BUGFIX_AUDIT.md). Audited 2026-11-01: the
  delta from `8f185f1` to `a2a4b9f` (and on to `158507b`) has no bug fix that applies here; it has formatting, API
  breaks, behavior changes and features, plus defects of its own that the [audit result](design/DAP_BUGFIX_AUDIT.md#audit-result-2026-11-01)
  lists so later feature ports avoid them. New features remain blocked on individual provenance.
- [ ] **5. Honest terrain metrics (C2(A)):** [benchmark-only plan](design/TERRAIN_DISTANCE_METRICS.md). Add shortest
  3D distance to the reference TIN and the horizontal/vertical components of the same closest-point displacement
  for `tdc` and `gtin`; retain vertical-ray metrics, report invalid/missing samples and measure overhead. Do not
  change terrain algorithms or the "never steeper" rule.
- [ ] **6. Upstream deviation ledger (E.5):** [documentation plan](design/UPSTREAM_LEDGER_PLAN.md). Create
  `docs/UPSTREAM.md` with pinned baselines, implemented additions, behavior/API differences and source/test/commit
  evidence. Complement the existing [migration notes](UPSTREAM_CHANGES.md); do not include uncommitted work as
  shipped. Prepare, but do not initiate, later contact with rms80.

## Release: when it's done

No public package until the library "just works": the operations give correct, documented results on hard inputs
without tuning, and it compares with the leading geometry processing libraries (eg the top of
[github.com/topics/geometry-processing](https://github.com/topics/geometry-processing): libigl, geogram, PMP,
cinolib, geometry-central, Manifold) on the operations it shares with them. Publishing a package makes us a
maintainer with users to support; that pays off only then.

- [ ] Hard cases from the issue trackers of those libraries reproduced as tests here (docs/QA.md 2.11), and passing. Booleans done (`ExactBooleanIssueCasesTests`); repair and alpha wrap merged in #326 (24 failure modes, MinimalHoleFill fix); groups 2-4 merged in #330 (2026-11-02); P3 by design (#349); Q2/Q3 (watertight exact ray-triangle: repair CPU +0.9 %, tree queries +2-29 %, single Intersects 4.2x, 260/260 boolean hashes) in #341 G3 (relative path-trace tolerance) in #343 and P1 (FlippedTriangles status, degenerate-UV rule relative to the surface triangle; UVAtlas A/B identical) in #346 and G2 in #347 (2026-11-03). **The fix list is complete** (2026-11-03): fixed R1, S1, G1-G3, P1, P4, Q1-Q3 and the Reducer and constraint bowties; P3 by design; R2 a limitation; P2 (Disk iteration, parked as research), S2 and S3 (upstream defaults kept, OPERATIONS.md recommends the guards) open, pinned and documented as decided. Follow-ups: the ray batch API (Later), the regressions a remesher flip-guard default would cause, the P1 collapse itself (virtual fan vertices or spectral conformal); the other operations started 2026-11-02 in a QA session (repair and alpha wrap, Reducer / Remesher, geodesics and parameterization, queries; mesher and terrain failure modes collected only). Findings in [ISSUE_CASE_FINDINGS.md](ISSUE_CASE_FINDINGS.md); owner 2026-11-02: every finding is fixed (correct first, then fast), distributed: QA session Q2+Q3 (exact ray/triangle crossings, IsInside), G3, P1, then G2, P2, S2, S3, P3; 7b session R1 (orientation rule for crossing components; merged in #333 on 2026-11-03: crossing components keep their orientation, A/B 3 inputs now accepted exactly instead of falling back, controls 1.008×), then S1 merged in #336 (2026-11-03; Reducer fold-over guard from area-weighted original normals plus a boundary ear rule, owner: PreventFoldOver default on, UPSTREAM_CHANGES row; noisy terrains 20/110 → 7/0 folds/walls, time on/off 1.00-1.14x, with MaxDeviation 1.01-1.02x)
- [ ] Package ID and publishing (nuget.org ID `geometry3Sharp` belongs to upstream); the pack and package smoke
  test already run in CI

## Later

- Point clouds: normal orientation (Dipole 2021, iPSR 2022, GCNO 2023), screened Poisson reconstruction, fast winding numbers (Barill et al. 2018)
- Feature-preserving offsets (Zint et al. 2023)
- Quad remeshing (Instant Meshes 2015, QuadWild 2021)
- Deformation: ARAP, biharmonic, cage coordinates
- Tetrahedral meshing: see Phase 5 (constrained Delaunay tetrahedrization). fTetWild is MPL-2.0 (file-level
  copyleft), not MIT: implement from the paper (Hu et al. 2020) rather than port
- TPMS / lattice structures from the implicit functions
- Sparse SDF grids (OpenVDB-like), SIMD via `System.Numerics`. For the `Orient3d` filter this was tried: about 2 % in total, not worth a second code path (branch `claude/boolean-simd-spike`, kept as a record)
- Batch ray / point queries against a mesh or BVH (owner idea 2026-11-02): a separate batch API (eg `IntersectBatch(ReadOnlySpan<Ray3d>, ...)`, `IsInsideBatch(points)`), existing API unchanged. Only the floating-point filter of the exact ray-triangle test (Q2/Q3) runs SIMD (`Vector256` / `Vector512`) over the triangles of a BVH leaf or a ray packet; the rare cases inside the bound stay scalar on `Orient3d` + SoS, so results stay exact. No FMA and no cross-lane reduction, so SIMD equals the scalar path bit for bit (tested); netstandard2.0 keeps the scalar path. Prior art: Embree (Apache 2.0; Woop et al. 2013 watertight test over SIMD packets). When: after the Q2/Q3 prefilter, once a real caller needs throughput; upper bound first (the filter's share of the query time). Note the `Orient3d` SIMD spike above gave about 2 %; the difference here is a batch of independent tests per call. Also here: a coarser per-triangle bound (max-norms instead of permanents; new derivation and review) and per-ray reuse of the ray's second point, the two levers left after Q2/Q3 (single-triangle `Intersects` 2.5-4× base with the shared `Orient3d` filter; tree queries +5-30 %, repair total +0.9 %)
- AI bridges as a separate package (ONNX Runtime): project 2D segmentation masks (SAM, DINO) from several views onto face selections, texture reprojection via ExpMap

## Phase 5: candidates from recent research

Checked against what the library already has (exact booleans, alpha wrap, intrinsic triangulations, BFF / SLIM,
vector heat). Ordered by priority: effort against what it builds on and what it unlocks. Licenses checked at the
source repositories (October 2026); "paper only" means the reference code may not be ported.

**Next research items (owner 2026-11-03), only if the effort is really small:** (1) #7 foldover-free maps
(Garanzha et al. 2021; on `SLIM` and `UVOverlap`; also a lead for the open P1 collapse), (2) the affine heat method
(Soliman & Sharp 2025, port from geometry-central, MIT; an upgrade of the `VectorHeatMethod` log map), (3) generalized
winding numbers for rational curves (Spainhour et al. 2024, Axom, BSD). Each starts with an effort estimate and a §2
prior-art check; one that is not small (about one PR in one short session) goes back to the owner instead of being
started. The owner runs them locally (2026-11-03); the coordinator reviews and merges the PRs. Status 2026-11-04: effort gates in #364 (item 1 stopped at the gate, item 3 only as the reduced method; its one internal caller would be the loop nesting of `PlanarComplex.FindSolidRegions`, which today decides it on sampled polygons with an absolute default tolerance of 0.1, so near-touching curve loops and scale may change the result; probe that first (two close concentric circles over `Similarity.All()`) when item 3 is picked up, later per owner 2026-11-04); item 2 built and stopped at its accuracy gate. Both affine variants are less accurate than the existing log map on the sphere and cylinder, geometry-central's own implementation (potpourri3d) included. The code is not merged; the findings are in [AFFINE_LOGMAP_GATE.md](design/AFFINE_LOGMAP_GATE.md) (#371, closed). #6 (walk on stars, Zombie) stays later:
no caller yet, Neumann boundaries need a new closest-silhouette query, and Monte Carlo results conflict with the
bit-for-bit determinism and invariance tests.

| # | Feature | Research / port source | License of reference code | Why / builds on |
|---|---|---|---|---|
| 1 | Geodesic paths, loops and Bézier curves on surfaces (FlipOut). **Done:** paths, waypoints, loops (`MeshGeodesicPath`); Bézier curves open | Sharp & Crane 2020 *You Can Find Geodesic Paths in Triangle Meshes by Just Flipping Edges*; geometry-central | MIT | Almost entirely on `IntrinsicTriangulation`; cut lines (orthotics), UV seams, curves on surfaces |
| 2 | Polyhedral envelope containment. **Prototype:** `MeshEnvelope` (exact, tested), but not used by the `Reducer`: its exact path clips the query against every prism it spans, about 100 times slower than `TriangleDeviationBounds` once reduced triangles span many prisms (`benchmarks/ReducerBenchmark`). Open: the paper's algorithm (filtered indirect predicates on critical points, no clipping) | Wang, Schneider, Hu, Attene, Panozzo 2020 *Exact and Efficient Polyhedral Envelope Containment Check*; `fast-envelope` | MIT | The `MaxDeviation` check takes 92-97 % of the reduction time (`benchmarks/ReducerBenchmark`), so a fast exact check would pay off. Alternative: speed up `TriangleDeviationBounds` itself |
| 3 | ~~Robust 2D polygon booleans and offsets: port Clipper2~~ Not planned: Clipper2 (Angus Johnson, Boost-1.0) has an official C# version on NuGet; use it directly (at most a thin adapter to `GeneralPolygon2d`). A port would only duplicate it. Straight skeleton / 2D medial axis from papers if needed (CGAL is GPL) | Clipper2 | Boost-1.0 | |
| 4 | Constrained Delaunay tetrahedrization, then tetrahedral meshing | Diazzi, Panozzo, Vaxman, Attene 2023 *Constrained Delaunay Tetrahedrization: A Robust and Practical Approach*; Hu et al. 2020 *Fast Tetrahedral Meshing in the Wild* | CDT: GPL / LGPL, paper only; fTetWild: MPL-2.0, paper only | On `Delaunay3` and the implicit points of `ExactMeshArrangement` |
| 5 | **Next large item (owner 2026-11-02).** Upper bound and literature in [design/INSIDE_OUTSIDE_OPEN_INPUTS.md](design/INSIDE_OUTSIDE_OPEN_INPUTS.md) (#325): about 100 of 111 fallbacks on 798 meshes hinge on inside / outside, the fallbacks take 54 % of the repair time; **spike result (#331): stop.** Resolving open inputs by GWN labels makes 3 of 111 fallbacks exact (0 regressions); 62 stay open at the structural crack / `ConflictEdges` limit (no labelling closes them), 32 differ from the pointwise GWN reference of the volume check; 12.7 % of 112440 patches are within 0.05 of a half-integer. A certified boundary term would not change this. The wrap fallback stays the answer for these inputs; literature (FEWN, Antipodal, Feng & Crane 2024, Feng's thesis) recorded in the note. Generalized signed distance and winding numbers for open / broken inputs | Feng & Crane 2024 *A Heat Method for Generalized Signed Distance*; Feng, Gillespie, Crane 2023 *Winding Numbers on Discrete Surfaces* | paper | Complements `MeshGeodesicDistance`, `VectorHeatMethod` and the repair stages |
| 6 | Grid-free PDE solving (Monte Carlo geometry processing) | Sawhney & Crane 2020 *Monte Carlo Geometry Processing* (walk on spheres); Sawhney et al. 2023 *Walk on Stars*; Zombie | MIT | Needs only closest-point and ray queries (`DMeshAABBTree`, winding numbers); works on dirty geometry |
| 7 | Foldover-free maps: fixed-boundary local untangling | Garanzha et al. 2021 *Foldover-free maps in 50 lines of code* | paper | **Merged in #374 (2026-11-04):** `FoldoverFreeParameterization`, no default atlas hook; independent review fix: the final flip-free iterate is returned (Converged true on all fixtures, annulus symmetric Dirichlet 686.97 -> 7.46), the epsilon schedule checked against equation 6. Open: the paper's energy-decrease stop instead of epsilon <= 1e-6 (about 131 iterations even from a valid start). [Measured acceptance](../benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md): folded-disc gate passed; SLIM quality target not universally met; torus global-overlap fallback remains. Local flip freedom does not close the global-injectivity gap in Phase 3 |
| 8 | Continuous collision detection | Wang et al. 2021 *A Large Scale Benchmark and an Inclusion-Based Algorithm for Continuous Collision Detection* (Tight-Inclusion); Li et al. 2020 *IPC* | Tight-Inclusion: MIT | Conservative, provably correct CCD; Unity, robotics |
| 9 | Simplification with intrinsic error metrics | Liu et al. 2023 *Surface Simplification using Intrinsic Error Metrics* | paper | On the intrinsic triangulation infrastructure |
| 10 | Mesh from coarse / sparse SDFs | Sellán et al. 2023 *Reach for the Spheres*; 2024 *Reach for the Arcs* | paper | With the sparse SDF grids and TPMS items under Later |

## Award-winning papers (October 2026 survey)

Best papers and runners-up of SGP, SIGGRAPH, SIGGRAPH Asia and Eurographics, 2016-2026, that fit the library.
"No code found" means none in a web search (paper pages, author pages, GitHub); check with the authors before
starting. The lists for SIGGRAPH 2021 / 2023 and Eurographics 2021-2023 could not be retrieved completely.
Documented only, nothing planned yet.

Without a public implementation:

| Paper | Award | Code | Fit |
|---|---|---|---|
| Shi, Alliez, Desbrun, Bao, Huang, *Symmetry and Orbit Detection via Lie-Algebra Voting*, CGF 2016 | SGP best paper | only an unofficial student project (CGAL based); the authors published a reproducibility addendum with parameters | high: symmetry axes, repeated parts, mirroring in CAD / scan data; next to `PrimitiveFit3` |
| Chen, Schaefer, Desbrun, *Lightning-fast Method of Fundamental Solutions*, TOG 2024 | SIGGRAPH 2024 best paper | none found for this paper (the follow-up boundary element method, 2025, has code) | medium: boundary-only PDE solves (Laplace, Helmholtz) for harmonic fields and interpolation; overlaps with Phase 5 #6 |
| Born, Schmidt, Campen, Kobbelt, *Surface Map Homology Inference*, CGF 2021 | SGP 2021 best paper | none found | low: niche (correspondence of handles between surfaces); `MeshDiscCut` has homology building blocks |
| Kilian, Ramos Cisneros, Müller, Pottmann, *Meshes with Spherical Faces*, TOG 2023 | SIGGRAPH Asia 2023 best paper | none found | low to medium: free-form architecture, panel fabrication |

With a permissive implementation (portable, see Licenses):

| Paper | Award | Code | Fit |
|---|---|---|---|
| Soliman, Sharp, *The Affine Heat Method*, CGF 2025 | SGP 2025 best paper | geometry-central (MIT, `LogMapStrategy::AffineLocal`) | high: a more accurate log map, above all near the cut locus; an upgrade of `VectorHeatMethod` |
| Spainhour, Gunderman, Weiss, *Robust Containment Queries over Collections of Rational Parametric Curves via Generalized Winding Numbers*, TOG 2024 | SIGGRAPH 2024 best paper | Axom (BSD) | high: inside / outside of regions bounded by NURBS and Bézier curves (`NURBSCurve2`, CAD contours); follow-up for trimmed NURBS surfaces (2025) |
| Trettner, Bommes, Kobbelt, *Geodesic Distance Computation via Virtual Source Propagation*, CGF 2021 | SGP 2021 second place | MIT (C++ library of the authors) | medium: geodesic distances next to the heat method |

## Open research questions

Problems where the library already has a head start and that could be researched (and published) here:

- **Practical 3D snap rounding with guarantees.** `SafeRounding` (after Milenkovic & Sacks 2019) verifies and repairs;
  open is a rounding that *guarantees* no new self-intersections with bounded displacement and practical running time
  (Devillers et al. 2018 is impractical). Thingi10K as the benchmark.
  Status: option B (Valque-Lazard) measured and closed in #313; a patch-wise B is parked (first measure whether a
  re-resolved patch fits back). Lead (owner 2026-11-03, from the walk-on-stars discussion): the transferable part of
  walk on spheres / stars is not the Monte Carlo estimate (a noisy estimate cannot certify combinatorial decisions)
  but its certified empty ball from a closest-point query. Used as a filter it is the "fast and correct" pattern:
  a conservative distance bound shows that a rounding move hits nothing nearby, so the exact check only runs where
  the ball is too small (the same pattern culls hot-pixel tests in 2D snap rounding and candidate pairs in the
  booleans). Monte Carlo stays a verification tool (as in the volume check), not a construction.
  Correction (cross-field report #368, `docs/research/CROSS_FIELD_GEOMETRY.md`): a point returned by a closest-point
  query only gives an *upper* distance bound; an empty ball needs a *lower* one, from a completed certified BVH search
  (conservative bounds on every unvisited node), and it must contain the whole moved primitive plus the motion of both
  sides (L > r + Δa + Δb). The report's idea 1 (a clearance certificate reused until the movement budget is spent,
  after Verlet skins) and idea 3 (fixed-collar search over representable positions per defect cluster) are the next
  probes here; each starts with its stated falsification run on a real rounding trace, not on a synthetic fixture.
- **An envelope-guaranteed exact pipeline.** Boolean → rounding → simplification / remeshing with every stage
  provably inside an ε-envelope and free of self-intersections (needs Phase 5 #2).
- **Metamorphic testing and fuzzing of geometry kernels.** Systematize the invariance tests over `Similarity.All()`,
  the defect generators and the differential predicate tests into a method and benchmark, applicable to other
  kernels as black boxes (without porting their code).
- **Bit-for-bit determinism across platforms.** net48 / net10, x64 / ARM, FMA, parallel paths: reproducible geometry
  for CAD and certification (eg medical devices).
- **Exact or certified intrinsic triangulations.** Integer coordinates (Gillespie, Sharp, Crane 2021, Phase 3) combined
  with exact predicates, so that signposts never fail silently.
- **Monte Carlo PDEs directly on polygon soups.** Walk on stars with generalized winding numbers instead of a clean
  inside / outside test, without a prior repair (needs Phase 5 #5, #6).
- **Geometry APIs for coding agents.** A benchmark of mesh / CAD tasks solved by agents through `g3cli` / MCP, and
  which API patterns (result codes, `NotCleanReason`, options objects) actually help them.
- **Showcase domains with error bounds.** Terrain: DEM → TIN with guaranteed error, exact cut / fill volumes.
  Orthotics: geodesic cut lines, wall thickness, feature-preserving offsets.
