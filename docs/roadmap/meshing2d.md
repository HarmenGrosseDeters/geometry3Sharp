# Meshing2D: the 2D quality mesher

Part of the [roadmap](../ROADMAP.md).

- **Scope:** public exact 2D meshing (`QualityMesh2`: constrained / conforming Delaunay, quality refinement, holes
  and regions, snap rounding to an output grid), a clean-room replacement for Triangle.NET; its oracle; the
  reference comparison runs; grading, coarsening, small input angles, subnormal fixes; the provenance check.
- **Status (2026-11-12):** M1-M3, `m2.snap`, hull edges on the grid, Q17, the fast oracle, grading steps 1 and 2,
  run A and the small-angle rule (#394, 2026-11-07) are merged; since then the corner choice (#437), rule C (#441),
  Z3 pilot 3 (#443) and rule C on the snap grid (#462); coarsening closed; the QA 2.0 T5 findings and owner decisions of
  2026-11-09/10 (#496, #526, #535, #542, #546, #553) are merged. Open (inventory 2026-11-12): two actionable items (three
  untraced single triangles from #542, the provenance delta after d9006aa), four blocked on corpus inputs, the rest research.
- **Related:** [design/MESHING2D_SPEC.md](../design/MESHING2D_SPEC.md),
  [MESHING2D_PLAN.md](../design/MESHING2D_PLAN.md), [MESHING2D_DECISIONS.md](../design/MESHING2D_DECISIONS.md),
  [MESHING2D_SNAP.md](../design/MESHING2D_SNAP.md), [MESHING2D_HULL_GRID.md](../design/MESHING2D_HULL_GRID.md),
  [MESHING2D_FAST_ORACLE.md](../design/MESHING2D_FAST_ORACLE.md), [MESHING2D_GRADING.md](../design/MESHING2D_GRADING.md),
  [MESHING2D_SMALL_ANGLES.md](../design/MESHING2D_SMALL_ANGLES.md),
  [MESHING2D_REFERENCE_COMPARISON.md](../design/MESHING2D_REFERENCE_COMPARISON.md),
  [MESHING2D_PROVENANCE.md](../design/MESHING2D_PROVENANCE.md),
  [MESHING2D_PROVENANCE_CHECK.md](../design/MESHING2D_PROVENANCE_CHECK.md),
  [ACCEPTANCE_STRATEGY.md](../design/ACCEPTANCE_STRATEGY.md).

### Open

Inventory 2026-11-12 (Meshing2D session, against git history and GitHub): every entry is actionable, blocked on inputs that
are not in the repository, or research / not scheduled. Settled entries (done, or closed by an owner decision) moved to
[Done / history](#settled-in-the-inventory-of-2026-11-12) with their commits and PRs.

Actionable:

- **#664 (bugfix, owner 2026-11-12):** the near-contact fan of #661 under the `rotated` similarity runs to the Steiner limit
  (about 400 000 vertices; same on master). Cause: the rotation rounds a T-junction end off A, so `PairOf` misses the
  meeting pair. Fix (in progress, one PR): the mesher treats an end within its tolerance of the other segment as meeting,
  and the oracle takes such an end as apex when the lines do not meet on both closed segments (owner 2026-11-12: excuse
  widening approved; both parts ship together).

- **2.5D output helper (owner 2026-11-12):** `ConstrainedMesh2` carries `Heights` per vertex (input heights, crossings and
  Steiner points interpolated), but there is no conversion to a 3D mesh; an additive helper builds a `DMesh3` with the heights
  as z (labels as groups where they fit). Effort S. In progress (short-topics Sonnet session, 2026-11-12).

- **Three single triangles at 1e-7..1e-9 radian (from #542):** in the near-contact fan (T-junction segments ending 2^-k beside a
  long segment, constrained quality mode), scale 1.13 at k = 26 and 28 leaves three single triangles below the bound that the
  oracle does not excuse (fan factors 1.13; [MESHING2D_SMALL_ANGLES.md](../design/MESHING2D_SMALL_ANGLES.md) section 2), not
  traced. Next: trace them with the fan generator of `Meshing2OracleCrossingRoundingTests`; a mesher defect gets a fix with a
  failing test, an oracle gap goes to the coordinator first (no oracle widening without a decision). The tie at fan scale 1.0
  (a split point exactly at the tolerance 2^-41 from another source, refused by the mesher, not excused by the oracle's
  `O11Unsplittable` margin) stays documented, not fixed (coordinator decision in #542). Effort S-M.
- **Provenance delta after `d9006aa`:** stage 6b-3 ([MESHING2D_PROVENANCE_CHECK.md](../design/MESHING2D_PROVENANCE_CHECK.md),
  ff0c378) covers master `d9006aa`, no (A) finding. Since then `QualityMesh2.cs`, `ConstrainedDelaunayMesh2.cs` and
  `SnapRounding2.cs` changed by +383 / −61 lines (#394 small-angle rule, #437 corner choice, #441 / #462 rule C, #496 the
  60° pairs, #512, #526, #553 pair keys), and the oracle much more. A delta check is due before a release that relies on the
  clean-room claim. It needs a dirty-side session (the only one that may read `triangle.c` / Triangle.NET); a clean session
  must not do it. **Owner 2026-11-12:** the exception is allowed for this check, as in stage 6b; the coordinator starts the
  dirty-side session when a slot is free; its report holds no reference code, names or structure. The clean side can hand over the list of changed routines. Effort M (dirty side).

Blocked here (inputs not in the repository: corpus, Herne tiles, run-A outputs, stmlf; not fetched):

- **SplitSegment speed:** the next mesher speed item outside grading (3.4 s of 7.3 s refinement on cq 373600 q30). An
  upper-bound check comes before any optimisation; it needs that input or a procedural input with the same profile
  (to be shown first). Effort M.
- **Fast-oracle leftovers** (no verdict of ours affected): 373600 q30 delaunay 9.8 s (pass on the remaining needles 7.5 s);
  corpus segments and conformity.
- **Judging very large outputs of other producers:** the quality phase on the two Herne q20 fork outputs (1171 s, or no
  result within 1800 s; profile first); `GlobalDelaunayIndexed`'s exact `InCircle` on the witness remainder (about 4 min
  each); f-16's triangle.c output not re-timed since `Conformity`'s `OverlapPairs` sweep was replaced in #392.
- **The other 72 own ORACLE-TIMEOUT outputs of run A** were not judged.

Research or not scheduled (not built without a new decision):

- **Shewchuk's Terminator** together with a region-based oracle excuse; a spec change that lowers the guarantee near small
  input angles ([ROADMAP](../ROADMAP.md) "Open decisions").
- **Matched shells** for T-junction wedges in the default mode (O(log 1/θ), option B of owner decision D, 2026-11-10;
  [MESHING2D_WEDGE_COST.md](../design/MESHING2D_WEDGE_COST.md)): today's behaviour is documented, the constrained quality
  mode is the answer for such inputs (stmlf/1461: 6.8x the reference relative to the LFS yardstick, 325 662 triangles at q30
  after #394).
- **Grading placement strategy:** f-11 1.8-2.0x the reference stays open (owner 2026-11-04: stop, #380 is the grading result;
  f-40 1.42x reached). About 5 vertices per octave against the reference's 2.7, so closing it needs a different placement
  strategy. Further options (a size function, one point per ring, quadtree meshing, smoothing) recorded, not scheduled
  (off-centres, Üngör 2004, are already the default split point; Erten / Üngör 2007's candidates are grading step 2).
- **<= 2x the fork's mesher-only time:** a future goal (owner 2026-11-04); after #384 about 2.7-2.8x on cq 373600 q30 and
  about 3.4x on q20. Fewer triangles are the expected route.
- **A Chew-2 variant** (Rand, CCCG 2011, 28.6°): a different algorithm; see the lens decision under Done / history.
- **Iterated rounding can depend on the order** (Hershberger 2013); no case is known. If one appears, the mesher returns its
  collapsed double chains as a certificate that the oracle checks.
- **Parked** (no use case; [platform.md](platform.md#stage-8-planned-follow-ups)): the `.poly` reader keeps crossing /
  touching / overlapping segment groups as constraints only, a vertex with d segments costs about d² comparisons (20k:
  about 6 s); mesh2d report areas underflow to 0 below coordinates of about 1e-160 (the area itself is then in or below the subnormal
  range, so a fix changes the report's format).

### Done / history

#### Settled in the inventory of 2026-11-12

Moved from Open; checked against git history and GitHub on 2026-11-12.

- **Drift promise on the snap grid (owner 2026-11-10, option A): done.** The QA 2.0 T5 campaign found chains at sub-pixel
  wedges up to 1.6 px from the input (p99.9 0.707 px over ~85 000 sources), caused by Q17, against the promise of half a pixel.
  The oracle judges chains against its own iterated image with a drift guard (rule O-G, f6372294, review 8c4e46a, #526; the
  wedge example 5bdeb8fd); O11 in [MESHING2D_SPEC.md](../design/MESHING2D_SPEC.md) states the real bound (input rounding at
  most half a pixel per axis; chains at sub-pixel wedges up to about 2 px; the oracle bounds the image at 2.5 px).
- **Collapsed stretches on the snap grid (owner 2026-11-10, option B): done.** For a stretch that the input-only image
  collapses, the rounded input is the only reference (a5c760aa, #535); the lobe bounds of #530 are gone, stretches with area
  stay judged by the full image (self-check B1 kept).
- **The #542 oracle gap: done.** A vertex computed from two near-parallel sources counts on either side of the apex within
  `RoundingReach2` (#542, 2026-11-10). Its two leftovers: the tie at fan scale 1.0 (documented, coordinator decision) and three
  single triangles (Open).
- **Small-angle exemption up to 60° (owner 2026-11-09): done** in #496 (aa7d451): the pair threshold in the mesher
  (`PairOf`, `SmallPairSin2`) and the oracle's excuse (`SpansSmallInputAngle`) is 60° less the slack, whatever the bound;
  such triangles are listed in `BadTriangles` (the report) and excused by the same rule; self-checks for 59°, goldens
  re-recorded with the reason ([MESHING2D_SMALL_ANGLES.md](../design/MESHING2D_SMALL_ANGLES.md), update 2026-11-09). The grid
  findings of the same campaign were handled as bugs: #499 (rule O-G, grid tube), #512 (`TrianglesOnDegenerate`), #534 (hull
  edges whole for the grid excuse), #535 (the spike case); the 10k strict grid run has no finding left
  ([platform.md](platform.md), QA 2.0 item 2).
- **Z3 checks for the mesher (owner 2026-11-08): done** in #443 (pilot 3, `tools/Z3Pilot/mesher_checks.py`, results
  `results/mesher_checks_2026-11-08.json`): (a) the `ShortestEdgeCorner` double filter of #437, (b) the real-arithmetic
  inequalities of the termination arguments; what is proved and what is only reviewed is in `tools/Z3Pilot/README.md`
  ("Mesher checks").
- **Default (conforming) mode near small angles: done** (rule C, owner 2026-11-08, #441, off the grid): corpus 252 runs at 20°
  and 30°, verdicts 249 ok / 3 unjudged → 250 / 2, no new failure; triangles of the 234 both-ok runs 550238 → 451893 (−18 %),
  meshing time 7020 → 6036 ms (runs/meshing2d-rule-c a3074096). **Rule C on the snap grid: done** (#462, owner 2026-11-09
  option A: the oracle computes chain identity from the output subsegment graph, `Oracle.OwnStretch`; procedural grid inputs,
  722 runs: wedges −83 %, pairs −74 %, Q17 zigzags −59 % triangles, no verdict regression). Herne not run (no tiles here).
- **Near-contact inputs: measured** 2026-11-10 (QA 2.0 T5, #546, procedural): near misses and close ends grow linearly in k,
  oracle ok; a T-junction wedge is constant (38 triangles) in the constrained quality mode and doubles per octave in the
  default mode (rule C revocations). Owner decision D (2026-11-10): documented as today's behaviour
  ([MESHING2D_WEDGE_COST.md](../design/MESHING2D_WEDGE_COST.md), #544); matched shells stay research (Open). CsCheck family
  `nearcontact`: 2 500 strict samples, no failing seed.
- **Pair keys on snap-grid parcels:** a quadratic run became linear (#553). **Oracle, vanishing domain parts:** a part below the
  vanishing bound may be dropped with its points and segments (#582, 2026-11-11).
- **Closed by owner decisions:** lenses up to 28.6° (2026-11-08: Rand's bound is for Chew-2, lenses stay at 25.65°);
  coarsening (2026-11-09: re-measured −4.5 % triangles for +54 % mesher time over 235 runs, not built); grading stops at #380
  (2026-11-04).
- The Phase 4 checkbox "Public exact 2D meshing" is closed: #272 merged and stage 6b done (#295, #296, delta 6b-3).

#### Stage 4: references

Pilot: M1 against NetTopologySuite.

Then the golden subset. The `Triangle.NET` reference session (strategy, section 3) is preparing since 2026-11-01 in the clean-room comparison session, output in `HarmenGrosseDeters/g3-meshing-references`; its comparison batches start once `m2.snap` is finished (owner)

Tier: M. Parallel: 1 session

#### Stage 6: meshing M1

`feature/meshing2d` (#200): oracle and differential as gates. Only then the M2 design (#201, `docs/meshing2d-m2-design`): its recorded approvals are disputed, so question its domain decisions first. An M2 implementation comes after that

Tier: L. Parallel: 1 large session, after stage 5

#### Stage 6b: meshing provenance check

After M3, once: a separate comparison session (the dirty side, the only one that may read Shewchuk's `triangle.c` and Triangle.NET) compares the 2D mesher with them for copied expression: data structures and memory layout, function decomposition, control flow of distinctive routines, constants, names and comments, plus a similarity tool where it applies (C against C#). It writes a report only, never code. A clean session rewrites any flagged part from the spec alone. The meshing session keeps a provenance log ([design/MESHING2D_PROVENANCE.md](../design/MESHING2D_PROVENANCE.md): paper / section or spec item per non-trivial routine) and avoids Triangle-typical expression. Reason: the BSL-1.0 release, and models may have seen `triangle.c` in training. Algorithms from the papers are free to implement; expression is not

Tier: M. Parallel: 1 session, after M3

Stages 4 and 6 are prepared in [MESHING2D_PLAN.md](../design/MESHING2D_PLAN.md): the gates exist as commands; the verdict on #200 and the questions on #201 are there.

#### Phase 4 item: public exact 2D meshing

  - [x] Public exact 2D meshing (constrained / conforming Delaunay, quality refinement with a size function, holes
    and regions by ring, snap rounding to an output grid), a clean-room replacement for Triangle.NET: from the
    behavioural spec [design/MESHING2D_SPEC.md](../design/MESHING2D_SPEC.md) (owner decisions in its §5) and the
    papers only (Ruppert, Chew, Shewchuk 2002, Üngör, Miller-Pav-Walkington, Cheng-Dey-Shewchuk 2012); never
    from Triangle / Triangle.NET code. Builds on `ExactTriangulation2` and the adaptive predicates. M2 (#233), M3
    (#252) and the oracle speedup (#265) merged 2026-11-01; #272 and the stage 6b provenance check are done (#295, #296, delta 6b-3); remaining work: Open.

#### Meshing session record (slot order item 3, 2026-11-01 to 2026-11-07)

3. Meshing done (large): M2 (#233), M3 (#252) and the oracle speedup (#265) merged on 2026-11-01; #272 and #274 merged. `m2.snap` (snap rounding with restore, refinement on the grid, §3g) merged in #277 on 2026-11-02; the reference comparison's set D starts now.

Hull edges on the grid (owner 2026-11-02, [design/MESHING2D_HULL_GRID.md](../design/MESHING2D_HULL_GRID.md), #310): option C, snap-round the hull edges as input segments (A as fallback); hull parts narrower than a pixel may collapse and are reported (Q17); acceptance the 192-run fuzz (master 119/192). Herne finding 2 (oracle merge zone hull-segment) in #309.

Merged 2026-11-02: #309 (Herne finding 2: oracle merge zone hull-segment, acute side only), #312 (oracle domain area without O(E²): debug2 50 min → 72 s); #315 (option C, hull edges on the grid: fuzz 192/192, Herne 9/9; Q17, domains narrower than a pixel everywhere, stays open).

Reference control run (set D, Herne, debug2) started on master a71171b8. #319 merged (oracle: O11-unsplittable exemptions, owner option B; corpus at 0/20/25/30 330/330, the exemption fires once; `mesh --no-oracle`, `oracle --stated-area`). The slow comparison rows were oracle time (water-huge3 73.7 s total, mesher 0.10 s); oracle speed merged in #321 (own grid, the oracle stays library-free: owner 2026-11-02; 373600 q20 69 → 15 s, q30 101 → 27 s, debug2 66 → 12 s, verdicts and details unchanged on 420 corpus runs).

Off-centres merged in #322 (2026-11-02; only for triangles bad by their angle, circumcentre fallback; corpus vertices −0.4 to −0.8 % at 20-30°).

The big reference run (ours, the fork, triangle.c with and without -D, all prepared inputs; 4869 cases, finished 2026-11-03 on f3d92da): ours 4854 VALID / 8 INVALID (7 BoundaryNotConstrained in GEOS overlay isochrone/stmlf, the fork fails the same 7, triangle.c is valid on 2; f-17/b needs --stated-area), fork 4661 / 202, triangle.c 2499 valid with 1853 aborts; only ours valid on 177 cases; triangle-count outliers under triage (f-10/c, f-11/c-d, TestCentroid/33, earcut jhl, stmlf/447 fewer; f-11/a-b, f-40 more: grading); snap-path times still 4-8x the fork's after #328.

Control run done (all Herne snap runs VALID); mesher-only times (`--no-oracle`): on par with the reference without snap, 5-20× slower with snap (373600 q20-snap 5.5 s vs 0.64 s); profiled: about half in `BelowFloor` (BigInteger pixel indices recomputed per check), a per-vertex cache gives 7.5-8.3 → 4.4-4.7 s byte-identical: a snap-path speed PR after #322; merged in #328 (2026-11-02).

Lens spike done (constrained quality, lenses to 25.65° = arcsin(√3/4), Shewchuk 2002 §6.4; circles above): catalogue vertices −7.2 %, corpus 20° 116k → 94k vertices, Herne 373600 q20-snap 93 320 → 62 303 triangles (reference 57 890), all pass; the lens PR comes as (B), without vertex deletion, provenance "unproven", with a requeue fix for a latent split defect (owner 2026-11-02; deletion (A) only if a real case fails to terminate).

Then lenses for Conforming=false (owner 2026-11-02: a second mode "constrained quality" judged as constrained Delaunay, O2 unchanged; an own optional input key so existing input_sha256 stay; lenses up to the proven termination bound of Shewchuk 2002, circles above). Raw alpha wrap fix A merged in #311 (59196 min angle 0° → 4.79°).

**Merged 2026-11-02:** #328 (snap-path speed), #332 (lenses (B) in the constrained-quality mode: catalogue vertices −7.2 %, corpus 20° 116k → 94k vertices, Herne 373600 q20-snap 93 320 → 62 303 triangles; **intentional behaviour change: the default output of `QualityMesh2` with `Conforming = false` changes**), #334 (grid-hole vertex renumbering bugfix). #337 (grid reroute: retag the triangles a reroute sweeps, merged 2026-11-03). T14 (conforming hole seeding: constraints inside holes queued) merged in #340, T13 (the oracle tolerates hull corners within the tube; one verdict changes, robustness1 M3@0 constrained) in #342, docs #345 (2026-11-03).

Done criterion (owner 2026-11-03): correctness, grading and speed at reference level; provisional targets: triangle count ≤ 1.25x triangle.c on f-11/a, f-11/b, f-40 and a corpus sample at the same bound, Herne snap runs ≤ 2x the fork's mesher-only time, no verdict worse.

Order: oracle speed, vertex deletion + T15, Q17, lenses (A), grading, snap speed.

Merged 2026-11-03: #348 (the ForcedByInputAngle excuse narrowed to the Shewchuk 2002 §6.3 bound), #351 (oracle speed: a grid and a double tube filter, GEOS shapely-829/1 714 s → 20 s and now VALID, verdicts identical elsewhere).

T15 merged in #353 (Delaunay vertex deletion in the kernel: ear clipping with exact orientations + Lawson flips, Devillers 1999/2002; hole content carved before refinement; corpus, rows and Herne identical to master).

Q17 merged in #358 (2026-11-03; the distance rule was replaced, the review found it both too loose and too strict): the oracle builds the iterated snap rounding image (Halperin & Packer 2002) of the input over the hot pixels and excuses a missing stretch or an unused input point only on parts that collapse in that image; plus a check that no boundary subsegment of the mesher's own output passes the interior of another hot pixel (Hobby 1999). The image starts with the input segment itself (review finding: rounding only the line between the endpoint pixel centres excused a 1.27 px strip and rejected a correct mesh); with that, the zigzag cases with refinement (M3) are VALID in every frame.

Owner decision 2026-11-03: strict, the refinement must not collapse what the rounding of the input keeps; the oracle accepts collapse only where the image with input hot pixels alone collapses (output vertices may reroute, never create a collapse). The zigzag cases with refinement (z0, z1: 23.5 and 169 px² pinched by grid points) are pinned INVALID in the Q17 PR; a mesher bugfix PR right after it rejects grid points whose pixel would join two input-image chains (upper bound: 0 such points on Herne and the catalogue).

Then a PR for the oracle's hull model for hull domains and the coverage tolerance (2 · L1 perimeter · 1 px checks almost nothing on narrow domains).

Iterated rounding can depend on the order (Hershberger 2013); no case is known here. If one appears, the mesher returns its collapsed double chains as a certificate that the oracle checks (certifying algorithms, McConnell et al. 2011), preferred over forbidding grid points there.

Pinned in #358: 5 refinement pinches (StarPolygonFuzz), zigzag M3, 9 restore false rejections and 3 false acceptances (the oracle lacks the SRR restore step; at M2 the input-only image equals the full image as an interim).

Mesher bugfix merged in #360 (refuse a reroute through a refinement vertex onto another chain's constraint edge; hull edges as owners in the oracle's GridUnsplittable; Herne R/U 1011/24, +0.4 to 6.2 % triangles as the new grading baseline; mesher-only Herne times −9 to +15 %; oracle Herne 90 → 130 s after a speed fix; 373600 q30 oracle 46 s noted for the hull-model PR.

The oracle's own SRR restore (Belussi et al. 2016, independent of SnapRounding2) merged in #362: the restore pins flipped (10 false rejections VALID, 3 false acceptances and the M2 wedge rejected, one hidden false acceptance of the interim found); Prop. 4.14/D14 followed over Listing 2, the discrepancy recorded.

Next: the hull model with coverage (a topological rule: no link shared by refinement, 0 on Herne with #360's rule; refinement pinches at a single vertex, 2-217 per Herne run and 13 in 12 fuzz inputs, are a known violation of the strict Q17 decision, counted with a ratchet), then the pinch fix in the mesher before the lenses plus coverage against the input-only domain).

Merged 2026-11-04: #367 (oracle: hull model, RefinementCoincidence rule, RefinementPinch as a counted known violation with fuzz and Herne ratchets, ChainSkipsHotPixel, opt-in --snapped-area), #372 (bugfix: when every outer ring collapses in snapping the domain is empty, not the hull; 10 star-fuzz seeds), #370 (lenses (A): Chew's deletion before a bisection; cq Herne q20 -17/-175/-72 triangles; the Herne cq 371800 q20 pinch ratchet temporarily 85 -> 92, to be brought back to <= 85 by (1)).

Measured 2026-11-04: every RefinementPinch vertex is a subsegment split point (none a circumcentre or off-centre), so (1) belongs in GridSplitPoint / ValidGridSplit; lenses cut the cq pinches (fuzz 11 -> 2, Herne 215/64/21 -> 92/44/11). #373 (pinch fix (1), 2026-11-04): a split may not reroute through a refinement vertex another chain already runs through (ValidGridSplit), with the oracle's GridUnsplittable mirror (no broader, may demand more); RefinementPinch is now a failing rule and every pinch count is 0 (fuzz 12 -> 0, all 12 Herne runs 0, the temporary 92 gone). Costs: unsplittable grid subsegments about double, triangles -0.5 to +4.7 % (cq 371800 q20), Herne oracle total 135 -> 159 s (outside CI, no budget set).

Grading step 1 merged in #375 (2026-11-04, details below).

Next: the **fast oracle** (owner 2026-11-04): the oracle's fast path at most 2x the mesher's time per run (today roughly 10-20x on Herne, 159 s total). Approach after certifying algorithms (McConnell, Mehlhorn, Naeher, Schweitzer 2011; the LEDA checkers): prove a valid planar triangulation of the domain (orientations, edge manifoldness, exact coverage), then the local (constrained) Delaunay test per edge; by the Delaunay lemma that replaces the global triangle-vertex pass. The input-only rounding and the Q17 chain / hot-pixel rules stay, made linear with filters and indexes. Every replacement needs an equivalence argument; the reference path (all exact, global) stays and Meshing2OracleModesTests keeps comparing them. Measure per check first; stop rule: if more than 4x remains after the two largest items, decide on numbers before building further.

Measured on master c0f7de6e: Herne oracle 6.0x the mesher in total (2.0-11.3x per run; quality 37 %, of which the merge-zone distance 45 % and GridBlocked rationals 36 %; Delaunay 24 %, needle fallbacks), corpus 34x (406 runs; coverage 35 %, O(triangles x boundary edges) in NearBoundary/Classify; small inputs with ms mesher times).

Order: (1) one boundary index for coverage and conformity, (2) a source index and double filters in front of the exact quality tests, then the stop-rule report (per-run ratios only where the mesher takes >= 100 ms, small runs with absolute times), then (3) Delaunay: the local test needs a completion witness for the conforming mode (empty circles across holes and concave boundaries), built by the oracle itself. (1)+(2) merged in #376 (2026-11-04, `docs/design/MESHING2D_FAST_ORACLE.md`): verdicts identical on Herne, corpus and catalogue; Herne oracle 153.6 -> 111.8 s (5.6x -> 4.1x the mesher, worst run 8.1x, median 1.9x), corpus 36.5x -> 24.7x (default) and 20.5x -> 14.8x (cq); a check mode compares every index and filter with its scan (box-level superset); one intended output difference (a hull domain without edges no longer throws). The stop rule fired (> 4x left); owner decision 2026-11-04: go on with (3), the local Delaunay test with an oracle-built completion witness, then the corpus items (segments, conformity); target redefined: <= 2x the mesher per run where the mesher takes >= 100 ms, <= 1 s oracle per smaller run; new stop rule: if (3) does not bring Herne to <= 2.5x in total, stop and go to grading step 2 (design: `MESHING2D_GRADING.md` section 4, #377). (3) merged in #379 (2026-11-04): the oracle's own Delaunay triangulation of the used vertices, certified (orientation, manifold edges, a linear walk of the boundary along the hull, local Delaunay per edge; any failure or exception falls back to the full pass), the old pass only on the output triangles not in it; verdicts identical; Herne 4.1x -> 3.6x, so the stop rule fired and the fast oracle ends here.

Left for a later pick-up: 373600 q30 delaunay 9.8 s = local test 1.9 + witness 0.9 + pass on the ~4400 remaining needles 7.5 (2.6 M in-circle hits, almost all merge-zone excused: ExcusedOffSegment 3.2, GridUnsplittable memo 3.1); corpus segments and conformity.

Next mesher speed item outside grading: SplitSegment (3.4 s of 7.3 s refinement on 373600 q30).

Oracle on large foreign outputs (reference run 2026-11-05: fork / triangle.c outputs of 200k-400k triangles with 170k-230k below the bound did not finish in 600 s): the quality phase hung in `CircumcentreBehindO11Unsplittable` → `Behind`, a scan of every subsegment per bad triangle (about 7 per second, about 9 h); fixed in #387 (the constructed point enters the subsegment grid by its interval; an unrefined 44k-triangle mesh at M3: quality 52 s -> 5 s, report identical).

Run A finding (reference session, 2026-11-05): m1/subnormal-32 at M3 (q20/25/30, both modes) INVALID (BoundaryNotConstrained, SubsegmentUnsourced, TriangleOutsideDomain, AreaMismatch), because split points of pieces below 2^-1026 rounded to the subnormal step off their segment; fixed in #388 (2026-11-05: such a piece is split only at a point exactly on its source, else it stays unsplittable; bit-identical from 2^-1026 up; now AngleBelowBound only, 6/8/14 and 5/7/14 against the fork's 14/20/34).

Oracle side: #389 (merged 2026-11-05 as 05f078da; owner 2026-11-05: AutoTolerance floored at one subnormal step 2^-1074, D3 updated; changes verdicts only below about 2^-1034; O11 keeps the mesher's relative tolerance).

**Run A done (2026-11-05 21:49 UTC, 6000 cases on d9006aad; report merged in #390, `MESHING2D_REFERENCE_COMPARISON.md` section 6):**

- cases as they are: 4862 VALID / 6 NO-OUTPUT (TestValid, invalid inputs) / 1 INVALID (f-17/b, CannotJudge for all);
- 8 verdicts better than on 2026-11-03, none worse, none with more than 10 % more triangles;
- only the reference valid: 0 as they are, 1 + 1 among the 1131 angle variants (stmlf/850 q30 default, stmlf/1326 q25 cq; one angle each, for the coarsening and quality week);
- median ratios ours / reference: triangles 1.00 (variants 1.03 / 1.00), mesher time 1.38;
- 84 own outputs ORACLE-TIMEOUT at the 120 s cap (angle variants at the Steiner limit, f-12/b, two buffer cases); a sample re-judge with the e2a0bb99 oracle is running;
- 170 outputs above 1.5x the reference's triangles, 75 with a reference of at least 100 triangles; the largest are stmlf/1461 cq 14.0x at q30 and ext/cdt/island 9.6x. This is the input for coarsening (week of 2026-11-09).

**Targeted QA follow-up (2026-11-08, #402):** a separate constrained M3 golden suite adds 52 independently judged CI rows, including quality-enabled F-38 cases, while retaining the previous mode baselines. Re-meshing all 79 historical AngleBelowBound operations and all 84 oracle timeouts on a482d490 gives 57 VALID (33 at the Steiner limit), 81 INVALID, 24 oracle timeouts and one mesher timeout. These are bounded results on selected exceptions, not a whole-corpus no-regression claim. Remaining failures and the current Conformity bottleneck are recorded with per-operation evidence in [the follow-up report](../design/MESHING2D_REFERENCE_COMPARISON.md#7-targeted-qa-recheck-2026-11-08). An isolated STRtree oracle pilot rechecks the 24 current oracle timeouts separately; no mesher or oracle quality rules are relaxed.

**Sample re-judge (2026-11-05, oracle e2a0bb99, 1800 s per call, 3 in parallel):** all 12 sampled own outputs that were ORACLE-TIMEOUT in run A are VALID (f-12/b, two buffer cases, 9 GEOS overlay variants at the Steiner limit; 141-1296 s each). The conformity phase is 90-98 % of every call; Delaunay and quality take under a second each. The other 72 own timeouts were not judged. **So `Conformity` (the `OverlapPairs` sweep) became a real item; done in #392 (2026-11-06: box pairs by a sweep with two segment trees over y, O((n + pairs) log n); conformity 10.5 s -> 0.32 s on 32k needle triangles; verdicts and counts unchanged, examples in index order)** for the week of 2026-11-09, next to coarsening: our own large outputs take 2-20 min in it. This is an oracle-only change. A faster pair order changes which example violations an INVALID report names, golden rows included, so the report change is deliberate and needs owner sign-off. Measure first: the active-list length, a y-index, and the upper bound of about 4x. On the two Herne q20 fork outputs (338k / 398k triangles, most below the bound) the quality phase is still the slow one even after #387: 1171 s, INVALID NotDelaunayLocal(200), on one, and no result within 1800 s on the other (conformity 36-45 s, Delaunay 118-233 s). This only affects judging other producers' huge outputs, so it has lower priority than conformity: profile the quality phase on such an output first.

**Triangle-count diagnosis (2026-11-05/06, mesher, owner-approved).** The question was what drives the triangle-count outliers.

- Coarsening after refinement (draft #391, probe): greedy deletion of Steiner vertices, all outputs VALID, but small gains: f-11 -17/-24 %, outliers about -15 %, corpus -3 %, +50 % mesher time. Not built.
- Cause A: there is no small-angle rule for subsegment encroachment. Hull edges and segments that meet at input angles of 0-5° start a mutual split cascade about 38 octaves deep. A cluster rule after Miller-Pav-Walkington / Shewchuk, measured as a probe in cq mode: island q30 8879 -> 741 triangles (reference 929), cdt -31 %, corpus cq q20 -31 %, stmlf/1461 -7 to -16 %.
- Cause B: lenses are allowed only up to 25.65°. The 28.6° of Rand (CCCG 2011) does not cover lenses (see Open).
- Side bug: island cq at 32°/34° without lenses returns Failed.
- Rest: near-contact or near-coincident input (stmlf/1461: T-junctions, angles of 0.013°, points 2^-15 of the extent off segments they do not touch), 6.8x the reference even relative to the LFS yardstick. Its own question, not measured.

Merged 2026-11-06: #395 (the Failed status was NodingLimit: tolerance splits in InsertPiece without progress; now every tolerance split must make both parts strictly shorter) and #396 (the mesher's small-input-angle test is now at least as strict as the oracle's, by 2^-30 + 2^-40; the oracle's arctan bound is credited to Miller-Pav-Walkington 2005).

Shewchuk's Terminator was measured on island / cdt / stmlf: it breaks the circumcentre link (cdt 8215 triangles) but leaves skinny triangles outside the oracle's excuse region, not only below its bound value, so it would need a much wider oracle excuse (research item). The design note was #394 (then a draft; merged 2026-11-07).

**Owner decision 2026-11-06: option (c)**, the mesher's vertex-encroachment exemption restricted to the apex region of a small-angle pair, in cq / constrained mode only. It is VALID under the unchanged oracle (probe: island 8879 -> 786, cdt -31 %, corpus cq q20 -31 %).

Research item, not scheduled: Shewchuk's Terminator (cdt 8215 in the probe) together with a region-based oracle excuse, a global floor arcsin(sin(phi/2)/sqrt 2) near small-angle clusters instead of the current wedge / spanning excuse; that is a spec change that lowers the guarantee near small input angles.

Refined after two reviews (owner 2026-11-06): exact pairing (no tolerance), and the encroaching vertex must be a refinement vertex on exactly one source, after Shewchuk 2002 §6.2's exceptions (input, crossing and multi-source vertices still encroach). Island q30 8879 -> 2971 (the earlier 786 also skipped real features), cdt 14 245, stmlf/1461 q30 325 662, all VALID.

Merged in #394 (2026-11-07, 59b9e4ec; Herne cq q30 -38/-6/-20 %, corpus cq 20° -29.7 %, 30° -2.3 %, no verdict worse).

Follow-up: the corner choice in doubles (MinSin2) vs the oracle's exact shortest edge at near-180° triangles.

Owner 2026-11-06: build the Failed bugfix first, then A for cq / constrained mode only (design note first, with the termination argument). Default (conforming) mode needs a spec decision on O2 near small angles. #390 section 6 paired the modes crossed; the matched angle-variant recomputation keeps the outliers (stmlf/1461 25.1x constrained vs constrained), and a docs correction follows.

Left, only for judging very large outputs of other producers (no verdict of ours affected): `Conformity`'s `OverlapPairs` sweep (f-16's triangle.c output 10-15 min: needle boxes keep the active list long; at most about 4x from a y-index, and any faster pair order changes which example violations an INVALID report names, golden rows included, so a deliberate report change) and `GlobalDelaunayIndexed`'s exact `InCircle` on the witness remainder (about 4 min each).

Finish plan (owner 2026-11-04): grading step 2 (petal candidates, Erten and Üngör 2007, scored as in step 1) and the exact double filters in front of SnapGrid.Pixel / CrossesInterior in one PR (Herne mesher −11.9 % vs pre-grading with −26 % triangles; f-11/a 215, f-11/b 363, f-40 184), with Herne snap runs measured against the fork's mesher-only times; then one last grading step time-boxed to one day; target revised by the owner (2026-11-04): a short look whether 1.25x is cheap, otherwise triangle count <= 1.5x the reference (1.4x welcome) on f-11/a, f-11/b, f-40 and the corpus sample.

Step 3 tried (2026-11-04, about 2.5 h): postponing near-bound triangles, stricter floors, a radius bonus, candidates beyond the circumcentre, other queue orders; the best general lever (smallest area first) gives f-11/a 1.69x, f-11/b 1.51x but f-40 1.55x; small cases swing ±15 % with the settings.

Owner decision 2026-11-04: stop; #380 is the grading result (f-40 1.42x reached, f-11 1.8-2.0x stays open).

Diagnosis for a later project: about 5 vertices per octave against the reference's 2.7, meshes better than needed (median smallest angle 31° at a 20° bound), so closing it needs a different placement strategy, not a lever on the choice. #380 merged (2026-11-04, grading step 2 + SnapGrid filters).

Next: SplitSegment for the <= 2x fork speed target (cq 373600 q30 3.1x, q20 4.0x after #380); measured upper bound: even with no grid split-point work and half the choice, q30 stays at about 2.3x (we are about 2.4x slower per triangle and make 1.34x the triangles), so a small exact speed PR (pending stack reuse, generation marks in TrianglesNear, a GridSplitPoint cache) comes first, then the target goes back to the owner.

Complexity check (mesher, 2026-11-04): everything per insertion is local (cavity, about 10 candidates, grid checks constant; queue O(log n); 1-2 grid re-sweeps); the one non-optimal part, point location walking from the last insertion (about sqrt(n) steps each, O(n^1.5) in total under worst-first), is fixed in the speed PR by handing the kernel the triangle the refiner's walk found (time-optimal reference: Har-Peled and Üngör 2005).

**Further options for the triangle count (and so speed), not scheduled** (owner asked for an assessment; recorded 2026-11-04), by expected gain for effort: (1) **coarsening after refinement**: delete free Steiner vertices while every affected triangle keeps the angle bound and all Q17/grid guarantees, with the kernel's exact Delaunay vertex deletion (T15); aims straight at the diagnosis (5 vs 2.7 vertices per octave, median smallest angle 31° at 20°); literature first (Delaunay coarsening, e.g. Miller, Talmor, Teng), risk: chain and pinch rules on the grid; a time-boxed probe behind a switch (f-11, f-40, Herne, corpus on triangles, time, verdicts) would settle it; (2) an explicit size function h = c·LFS with a growth parameter (`delaunay/LFS` exists; termination with both criteria to show); (3) one point per ring of bad triangles instead of per triangle (the conceptually right fix for f-11, a research project); (4) quadtree meshing (Bern, Eppstein, Gilbert: size-optimal but worse angles, a step back); (5) smoothing (Lloyd/CVT/ODT: better quality, not fewer triangles; only with (1)). Yardstick for 'fewer': an LFS-based lower bound, not the reference alone; fewer is not better by itself, our extra angle margin helps FEM and interpolation.

Speed PR merged in #384 (2026-11-04: allocations, then point location from the triangle the refiner found; Herne mesher 21.4 -> 19.3 s, cq 373600 q30 2.7x / q20 3.6x the fork; 21 golden M3 rows changed through the slot order of on-edge insertions, all oracle-valid).

Broad reference comparison (4869 M2 cases as on 2026-11-03 plus 1131 M3 runs at 20/25/30 degrees) and the provenance delta 6b-3 (stages 6b/6b-2 were done on 2026-11-02 in #295/#296, no (A) finding) run in the reference session on master d9006aad.

**Owner 2026-11-04:** coarsening (1) is the next mesher project, planned for the week of 2026-11-09 (no budget this week). The <= 2x fork speed target stays open as a future goal (after the speed PR about 2.7-2.8x on cq 373600 q30, about 3.4x on q20); no further work toward it after the speed PR; fewer triangles from coarsening are the expected route. Benefits: CI time (the mesher tests are half of it), faster Herne iterations for grading, and a verified-meshing option (every result with a certificate).

Then grading step 2, including a wider candidate search (another pixel instead of giving up; the transaction idea of #368) to win back the extra unsplittables.

Grading step 1 (merged in #375, 2026-11-04, `docs/design/MESHING2D_GRADING.md`; review fixes: grid rounding guarded at 2^52 pixels, no chosen point on a constrained edge): bad triangles worst first, and the split point of a triangle bad by its angle the best of the default point, the circumcentre and 6 points on the bisector, never with a smaller insertion radius than the default point; f-11/a 256 → 217, f-11/b 456 → 379, f-40 276 → 245 triangles (reference 108 / 205 / 130: still 1.9-2.0×, **the 1.25× target stays open**); corpus −0.6 to −9.0 %, catalogue −1.3/−1.4 %, Herne −12.4 % (all 12 VALID, pinches 0), no verdict worse; mesher time on Herne +26 % in total (−9 to +48 % per run; oracle −7 %), accepted as a known cost (coordinator 2026-11-04), with a **speed stop rule: the next grading step brings the mesher back to within +10 % of pre-grading on Herne, or says why not** (e.g. scoring incrementally instead of a cavity search per candidate); **intentional behaviour change: the output of `QualityMesh2` with `MinAngle > 0` changes** (both modes).

Next grading steps: a 2D candidate region (Erten and Üngör 2009, once the paper can be read), the bad-triangle criterion, the cq churn of lens deletion (A).

Earlier order: Owner 2026-11-03: lenses (A) first, then the constrained-quality bug (StarPolygonFuzz seeds 37 and 41: triangles outside the ring, unsourced subsegments; pinned strictly), then the pinch fix (1).

Candidate for (1) from the cross-field report (#368, idea 2): accept a refinement split as a transaction only if it keeps the input-only embedding, checked by incidences and vertex sectors in the closure of the reroutes it induces, not by edge overlap (Hershberger, *Snap Rounding: A Cautionary Tale*, SoCG 2025, documents pinches missed this way in practice); stop rule: drop it if it reduces to the rejected pixel-owner rule (acute corners over-rejected). Open: the 2-2.5x triangle count on f-11/f-40 (grading; off-centre use under review), the 12 tracker cases merged in #344 (60 runs, all pass); big-run triage done (the 7 INVALID are resolved by #342; the fewer-triangle cases are by spec, O11 merges within the automatic tolerance); T15 (owner decision 2026-11-03: (a), carve the hole content before refinement by Delaunay vertex deletion in the kernel (Chew 1990, Devillers 1999); O2 reads "the vertices of the output's triangles"; the same building block then unlocks lenses (A) with deletion, Shewchuk 2002 §6, provenance proven), Q17.

Diametral lenses dropped (owner 2026-11-02, measured): under Conforming (O2) they pass 46 of 55 catalogue M3 rows and 25–36 of 84 corpus inputs (NotDelaunayGlobal), hit the Steiner limit at high bounds and double the time; lenses for Conforming=false next in the meshing session after off-centres (owner 2026-11-02): the library default mode, where diametral-circle splits dominate the Herne meshes (1.6–3.4× the reference's triangles), with its own oracle rows (constrained Delaunay).

Off-centres (Üngör 2004) go ahead: corpus vertices −0.9/−0.8/−0.6 % at 20/25/30°. Oracle gap (pre-existing, earcut/water-huge at 25°, 0.84° corner): option (B), the three grid exemptions mirrored for O11-unsplittable pieces (both ends within the tube of another constraint), one shared predicate, excused counts reported per row, 25° added to the corpus angles; its own PR (bugfix).

Reference comparisons (owner 2026-11-02): a small control run (set D, Herne, debug2) once the Herne and debug2 fixes are merged; the big run with the new inputs (CDT rest, Natural Earth polygons, GEOS XML, DGM1, CGAL data) after off-centres (#322), with Shewchuk's `triangle.c` 1.6 as a second black-box reference (owner 2026-11-02; with and without -D; in the control inputs it crashes on every Herne -q run and aborts on f-14/b, f-14/c, sketchup).

Reference comparison analysed in [design/MESHING2D_REFERENCE_COMPARISON.md](../design/MESHING2D_REFERENCE_COMPARISON.md) (#293): one real defect (CDT issue-211 at M3 20°, bugfix next).

Owner 2026-11-02: refinement with diametral lenses plus off-centres (Chew 1989, Shewchuk 2002, Üngör 2004), its own PR after the bugfix, measured before/after. Stage 6b done: no copied expression against Triangle.NET (#295) nor against `triangle.c` 1.6 directly (#296); dirty evidence in `g3-meshing-references`, branch `claude/provenance-6b`.

The **stage 6b provenance check** (medium, report only; started 2026-11-02 after #277, session "Stage 6b: meshing provenance check"; clean report as docs PR, dirty evidence in `g3-meshing-references`) runs once after `m2.snap` (with refinement on the grid) is merged, so that one pass covers the whole 2D mesher (owner 2026-11-01); its own session, not the reference session, which must stay independent of our code.

Also a small **snap-rounding Q&A** session (owner decision 2026-11-01): the dirty side reads the owner's own commits in his Triangle.NET fork (written privately with Claude, owner's rights) and answers the meshing session's written questions in spec terms only (behaviour, edge cases, failures, parameters, test inputs as descriptions; no code, structure or names). The coordinator checks the report before the meshing session gets it; it is logged in the provenance log and SOURCES.md. The meshing session never reads the fork. Starts once the question list is in MESHING2D_SNAP.md.

**Open (2026-11-10, found by the MeshFuzzPilot):** `QualityMesh2` drops a part of the domain narrower than the tolerance (a sliver tail of a hull domain; its input points and segments end up in no triangle) with status `Success` and no diagnostic. The oracle now accepts this for a part whose twice area is at most tube times perimeter (the bound for a domain that may vanish altogether, no snap tolerance; `OracleReport.VanishedPoints/VanishedSegments`). Candidate under the no-silent-deletion rule: report such a dropped part in the diagnostics; the mesher is unchanged.
