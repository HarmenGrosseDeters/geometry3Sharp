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

**Owner decision 2026-11-08: quality and speed.** On the 800-mesh Thingi10K sample of MeshRepairBenchmark
(`BENCH_REPAIR=wrap`, 300/3000), the full pipeline (raw `MeshAlphaWrap`, `SimplifyAlphaWrap`, `AlphaWrapQualityPass`):
- guarantees (closed, manifold, free of self-intersections, offset band) and fidelity (F-score, Hausdorff, band) not
  worse than master;
- 0 triangles < 0.5° and 0 > 170° per mesh in the final output; the pinch caps (102793, 118678, 1505037) gone;
  **p01 (owner 2026-11-08):** the raw wrap reaches CGAL's p01 (the ~2° gap of #317; behaviour numbers only, CGAL code
  is never read); the final simplified output has p01 >= 10° as the median over the 800 sample (floor confirmed
  after the full-800 data);
- at most 5 s wall time per mesh on the 4-core cloud machine.
A stop rule reports the cases the known levers cannot reach; the owner decides on exceptions. Work packages: (1) baseline,
phase profile, upper bounds; (2) quality (pinch caps, slivers, caps; bug-or-inherent against the paper first); (3) speed;
(4) control run. Session session_01AaGT5CSm6ZjSih3jZRg7HJ (started 2026-11-08), draft #404 (`BENCH_REPAIR=wrappipe`).

**WP1 interim (2026-11-08, 55 meshes, serial):** pipeline median 10.8 s, p90 19.4, max 34.4; > 5 s on 53 of 55 (the
target needs about 2.2x at the median, 6-7x at the tail). Shares: simplify 39 % (Reducer 29 %), quality pass 27 %, raw
wrap 34 % (carve loop 23 %). Quality: the raw wrap is good (p01 median 17.3°); the bad angles come from the
simplification (976 triangles > 170° on 52 of 55; p01 median 7.8°), whose Reducer guard `MinCollapseTriangleQuality`
0.05 allows caps up to about 175°. Upper bound on 12 meshes: guard 0.3 gives 0 triangles > 170°, p01 median 10.8°,
+4 % triangles, 0.92x time, guarantees and band unchanged (F-score pending).

#### Other open items

- **Raw alpha wrap** (now part of Alpha Wrap 2.0): the remaining pinch caps (102793, 118678, 1505037) and the p01 angle gap to CGAL's
  alpha wrap (about 2°); the ideas are recorded under [item 2](#wrap-resolution-raw-wrap-and-volume-checks-slot-order-item-2).
- **Quality pass after `SimplifyAlphaWrap`:** the Phase 2 checkbox is still open, but stage 7b merged the quality
  pass in #292 (2026-11-02), ON by default in the wrap fallback (status unclear, check: probably done).
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
