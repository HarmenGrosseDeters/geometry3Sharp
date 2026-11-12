# Roadmap

## How to use this file

- This index holds status only: the direction, what runs now, what waits on the owner, what is queued, and what is
  done, one to three lines per item with a link.
- Details, numbers and history live in the subpages under [roadmap/](roadmap/). Each subpage starts with a short
  header, then "Open" (what is still open, with owner decisions and dates), then "Done / history" (the record).
- Decisions are recorded with their date and "owner", eg "owner 2026-11-07".
- When an item changes status, update its line here and its subpage in the same PR. Keep status text in the PR,
  not in the roadmap (see [Session limits](#session-limits)).

Subpages: [exact-boolean.md](roadmap/exact-boolean.md), [meshing2d.md](roadmap/meshing2d.md),
[terrain.md](roadmap/terrain.md), [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md),
[parameterization-geodesics.md](roadmap/parameterization-geodesics.md), [platform.md](roadmap/platform.md),
[sources-surveys.md](roadmap/sources-surveys.md), [research.md](roadmap/research.md),
[hotspots.md](roadmap/hotspots.md), [history.md](roadmap/history.md).

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

**Depth over breadth (owner 2026-11-10):** round off the existing features first; a new topic only with a synergy
with an existing one. Must-win hard problems (stronger-model investigations): scale-free robust repair, rounding cost
on large results (with the 3D snap-rounding leads), a proof method for determinism
([research.md](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)).

Relation to upstream: stay API compatible with 1.0.324, which existing users depend on. Upstream's
`dotnet8` branch has breaking changes (it removes the float types); useful parts (eg USD) can be taken over
selectively.

## Now
Status on 2026-11-12 (morning, Berlin).
- **Exact Boolean 2.0: reached 2026-11-11** (D1 #513 merged as 69f13be3, counted as Exact Boolean 2.1, owner 2026-11-12; sign-off run in [exact-boolean.md](roadmap/exact-boolean.md)). Milestone (owner 2026-11-07): clean results on closed inputs; speed target revised
  2026-11-08 to max(5 s, 20 µs × arrangement pieces) per operation (7 small, heavily self-intersecting models as
  documented exceptions). The queue (#397, #398, #406, #409, #412, #419, #420, #424, #425 far parts, #426 edge-key hash and #440's L3 test) merged as
  one integration PR, #446 (2026-11-09, 21e27a19), on green CI and review; its full mutation run of the touched shards
  runs on master after the merge (owner decision) and seeds the rounding baselines. L3 dropped (owner 2026-11-08:
  119829/119830 now run in 0.8-3.7 s and already report RoundingClean false honestly). The ±1e308 throw is fixed in
  #449 (robustness sweep S38: the volume checks scale by a power of two, MeshRobustRepair fails with ScaleNotFinite
  above an extent of about 1e154). The final Thingi10K run and the control run are the sign-off run; the thingi-pairs runs (class merged in #418) have
  no recorded result; still wanted, queued behind Exact Boolean 3.0 (owner 2026-11-12).
  Lévy classification: phase 1 done (#428, no propagation now); phase 2 done
  (parallel fast-winding build, bit-identical; classification -23 % on both samples, -40 % on tree-bound open ops). [exact-boolean.md](roadmap/exact-boolean.md#exact-boolean-20-milestone)
- **Exact Boolean 3.0** (in progress, serial, one PR per stage): merged 2026-11-11: T3a cull (#612), T3b
  `IntersectEach` / `DifferenceEach` (#616), T3 input range of the rounding (#593), T2 memory (#619), T1 scaling levers
  (#631). The 55928 tail-case stage #633 merged 2026-11-12 (d6f6fe9d): budget, cancellation, L1, L2 and L3 done (L3 steps 1-2:
  classification filter from local corner coordinates, Orient's zeros from the constructions; Difference total 973 -> 370 s,
  hash unchanged). Step 3(a), a second InCircle filter, measured and not taken (no gain). An InCircle limb kernel (E: -20 to
  -30 s) is an open candidate, not part of this stage (coordinator 2026-11-12). Docs, shard resplit and the full local CI
  are done (allocations 534 -> 200 GB); waiting for the independent exact-code review of the new error bounds (started
  2026-11-12). Then T5 / T1b per the execution order; T6 (only all-clean method, <= 2x CGAL EPECK on 1k-100k) is checked at the end. [exact-boolean.md](roadmap/exact-boolean.md#exact-boolean-30-progress)
- **`DMeshAABBTree3` profile: done 2026-11-12** (no library change): no bit-identical tree lever above about 3 %; the
  memory layout (up to 2x on shuffled inputs) was measured on 52 Thingi10K models and dropped (file order is already at Morton speed, 2026-11-12); the solidify's
  `DualContouring` allocations bound about 4 % of wall.
  [exact-boolean.md](roadmap/exact-boolean.md#exact-boolean-20-milestone)
- **Alpha Wrap 2.0: done** (owner 2026-11-09). Final 800 run: guarantees 800/800, 0 triangles < 0.5° / > 170°,
  p01 median 11.3° (min 10.6°), fidelity not worse; speed 611/800 within budget (97 % within 1.25x), documented.
  [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#alpha-wrap-20-milestone)
- **Alpha Wrap 3.0** (in progress, targets set): phase 1 done 2026-11-12 (`runs/alpha-wrap-3`, `aw3/REPORT-phase1.md`:
  stacked levers about 1.57x on the 189, about 99 % of the 800 within budget). Targets (owner 2026-11-12): >= 95 % of
  the 800 within budget (stretch 98 %), >= 1.4x on the 189, guarantees and determinism unchanged; lever 6 not now.
  L3 (batched gate tests) measured at <= 1.085x and dropped (owner 2026-11-12, option A): target (b) lowered to about
  1.25x; L4, L1, L3b, L2 follow. Future research item (owner 2026-11-12, option B): deterministic round-based parallel
  carving (not master-identical; guarantee argument and probe first). Levers follow as PRs; PR 0 is #651. [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#alpha-wrap-20-milestone)
- **Reducer / Remesher 2.0: done 2026-11-09** (#457, #466, #468, #469, #470; T4 closed with #484, crease-sliver probe
  #492; [REDUCER_REMESHER_20.md](design/REDUCER_REMESHER_20.md) section 11). Remesher rim folds (#621): fixed by #640 (merged 2026-11-12, 36ef5a18; owner accepted the missed time gate,
  1.16-1.22x on Thingi, opt-in via `MinTriangleQuality`). Follow-ups closed (owner 2026-11-12): the snapshot PR is not
  built (no bit-identical gain), the crease-general fold check is exhausted (most remaining pairs are non-local;
  numbers in [REDUCER_REMESHER_20.md](design/REDUCER_REMESHER_20.md) section 13).
- **QA 2.0** (milestone, owner 2026-11-09): the new test tools (SharpFuzz, FPTaylor, Z3, CsCheck, coverlet,
  mutation shards, test tiers) applied across the library, not as pilots: an inventory (tool x area) first, then
  a rollout by ROI. Rollout continuing: status table [QA20_STATUS.md](design/QA20_STATUS.md) (#635); merged 2026-11-12: #639,
  #641, #642, #643, #646, #648, #657 (#645), #655 (#647); open PRs #627 (draft), #656 (#644); open `local` issues #636-#638;
  #628 fixed by #634.
  [platform.md](roadmap/platform.md#qa-20-milestone)
- **Mutation tooling:** #403 (rounding in 7 parts) and #445 (early stop, log artifact, ticker) merged. Rule since
  2026-11-09: only the incremental run gates a PR; full shard runs go to master after the merge. JOB-1 on the owner's
  machine: no kill data (Windows runner failure), so kills only in CI. Process since 2026-11-11 (owner): no full shards
  on PRs, full shard runs nightly on master. Next: smaller test sets per shard from the exclusive kills. Found 2026-11-11: Stryker 5.0 places mutants in the whole library (~138k) and
  the shard's `mutate` filter only skips them (579 tested in boolean-1), so the coverage capture pays for every placed
  mutant in the hot loops (boolean capture > 5.7 h for a 22 s suite); the fix (only the shard's files are
  mutated) is in #615 (CI-speed session, open), with the 3.0 process (no full shards on PRs, scheduled full runs).
  [platform.md](roadmap/platform.md)
- **Scheduled runs on GitHub are unreliable:** `coverage.yml` (cron 03:41 UTC) has no scheduled run yet, none on
  2026-11-12; `nightly.yml` (cron 02:23 UTC) ran once on schedule (2026-11-11, started 09:36 UTC), none on 2026-11-12.
  [platform.md](roadmap/platform.md#open)
- **Meshing2D:** bundle merged (fast oracle #411/#427, corner choice #437, rule C #441: −18 % triangles on the corpus,
  no verdict worse; Z3 pilot 3 #443); rule C on the snap grid done (#462); QA 2.0 T5 findings settled (60° pairs #496,
  iterated image #526, collapsed stretches #535, #542, near contact #546 with owner decision D). Coarsening closed.
  Inventory 2026-11-12: open are three untraced single triangles from #542 and the provenance delta after d9006aa
  (needs a dirty-side session before a release).
  [meshing2d.md](roadmap/meshing2d.md#open)
- **Phase 5:** priority by return on investment in [research.md](roadmap/research.md#open); batch queries stage 2
  merged (#448), intersection-free dual contouring options A and B merged (#552, #572), point clouds step C1 merged (#580);
  the CLI / MCP update is still queued.
- **Terrain:** closed out (owner 2026-11-09): DGM reader / writer, grid TIN (#491, #504), `g3cli` `dgm` / `dgm_tin`
  (#506), cell volumes (#507), mutation score (#508); the rest is parked until a use case. [terrain.md](roadmap/terrain.md#open)

## Open decisions (owner)

Checked line by line against the owner's decisions and git on 2026-11-12; settled items moved to "Later" or the subpages.

- **Clipper2 C# sources:** upstream archived 2026-10-28 (C++ only from now on, new maintainer at PolyClipping/Clipper2);
  integrate the frozen C# 2.0.0 sources into the addon and fix FINDINGS item 5 ourselves, or keep the package? No
  upstream report (owner 2026-11-11). [platform.md](roadmap/platform.md#open)
- **Release:** package ID and publishing (the nuget.org ID belongs to upstream); deliberately last (owner 2026-11-10: the
  library is unpublished, results may still change). [platform.md](roadmap/platform.md#open)
- **Exact Boolean 2.0 leftovers (asked 2026-11-12):** re-measure the speed exceptions (519511, 1517923, 519507) and
  manifold M2 (`Generic_Twin_7081`, 20 s) on master, or close them as documented exceptions; delete the superseded
  branches `claude/int256-spike` / `claude/collinear-shortcut` after #633 merges. [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)

Standing rules (not pending): a Phase 5 item that is not small goes to the owner first (owner 2026-11-03); each port
from geogram (BSD-3, may be read and ported, owner 2026-11-03) or another permissive library is a decision with an A/B
([sources-surveys.md](roadmap/sources-surveys.md#open)).

## Next / queued

The next steps per topic, in order ("in progress" where a session works on them).

- Exact Boolean 3.0 (#633 merged 2026-11-12): T5 (Antipodal PR 2, cost count first), T1b (parallel SafeRounding edits), the T6 check;
  then the thingi-pairs baseline and final runs (owner 2026-11-12). [exact-boolean.md](roadmap/exact-boolean.md#exact-boolean-30-progress)
- Meshing2D: the three single triangles from #542 traced and fixed (#661, merged 2026-11-12); the rotated-fan
  Steiner-limit bugfix #664 (#666, reviewed, after master merge); the 2.5D helper `ConstrainedMesh2.ToDMesh3()`
  (#663, merged 2026-11-12); the provenance delta after `d9006aa` by a separate dirty-side
  session, which may read Triangle.NET and `triangle.c` for this check only, as in stage 6b (owner exception 2026-11-12; starts
  when a session slot is free). SplitSegment speed and the oracle leftovers need corpus inputs that are not in the
  repository. [meshing2d.md](roadmap/meshing2d.md#open)
- Alpha Wrap 3.0: L4, L1, L3b, L2 (in progress, one PR per lever). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#alpha-wrap-20-milestone)
- Phase 5: the CLI / MCP update (in progress, Sonnet session since 2026-11-12: inventory and command list for the owner
  first, then small PRs). [research.md](roadmap/research.md#open)
- Phase 5: batch queries, the internal caller switches (spatial profile first, then switch only serial loops over many
  independent queries, bit-identical; Sonnet; owner 2026-11-12: queued, starts after the dirty-side provenance session
  when a slot is free). [research.md](roadmap/research.md#open)
- Parameterization: integer coordinates (the AffineLocal follow-up closed in #538, geodesic Bézier curves merged in
  #540). [parameterization-geodesics.md](roadmap/parameterization-geodesics.md#open)
- Platform: no issue-tracker follow-up left (the flip-guard default follow-up is closed: the Remesher
  defaults stay upstream's, owner November 2026; the P1 collapse is a documented limitation, the real fix is research; P2 parked, S2 / S3 upstream defaults kept, ray batches merged in #448). [platform.md](roadmap/platform.md#open)

## Later / research

- Exact booleans, not scheduled: EMBER lever 2 (skip self-pairs of results certified free of self-intersections in chained
  booleans); levers 1 and 5 (quantized inputs, a new architecture): no rewrite (owner 2026-11-07), patent search done
  2026-11-10 (nothing found); an InCircle limb kernel (candidate from #633); exact output coordinates of the coplanar 2D
  clipping. [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- Meshing2D research: Shewchuk's Terminator with a region-based oracle excuse (owner 2026-11-06, option c: not built),
  matched shells, the grading placement strategy, <= 2x the fork's mesher time. [meshing2d.md](roadmap/meshing2d.md#open)
- Remeshers: thin walls and opt-in self-intersection cost; Reducer / Remesher 2.0 and its follow-ups are closed (owner
  2026-11-12). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#open)
- Later list (point clouds, offsets, quad remeshing, deformation, tetrahedral meshing,
  TPMS, sparse SDF grids, batch ray queries, AI bridges): [research.md](roadmap/research.md#later)
- Phase 5 candidates from recent research: [research.md](roadmap/research.md#phase-5-candidates-from-recent-research)
- Open research questions (3D snap rounding, envelope pipeline, metamorphic testing, determinism, and more):
  [research.md](roadmap/research.md#open-research-questions)
- Intersection-free dual contouring (owner 2026-11-10: moved up, ahead of collision detection and tet meshing; options A
  and B merged in #552 and #572):
  [research.md](roadmap/research.md#open)
- Hard open problems, candidates for a selective investigation with the stronger Fable model (owner 2026-11-09):
  [research.md](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)
- SMT checks with Z3 (pilot 1 merged in #400; pilot 2 steps (1) and (2) merged in #416 and #407): [research.md](roadmap/research.md#smt-checks-with-z3)
- Mesh-kernel ideas and EMBER: [exact-boolean.md](roadmap/exact-boolean.md#external-boolean-corpora-2026-11-04)
- Geogram and other permissive libraries: [sources-surveys.md](roadmap/sources-surveys.md)
- RWTH Aachen papers and award-winning papers: [sources-surveys.md](roadmap/sources-surveys.md)
- Shared hotspots and leads for open tasks: [hotspots.md](roadmap/hotspots.md)

## Done

- Phase 1, foundation (targets, CI, package metadata, formats, API docs). [history.md](roadmap/history.md#phase-1-foundation)
- Takeover of the abandoned work, stages 0 to 3 (2026-10-31). [history.md](roadmap/history.md#stages-0-to-3)
- Phase 2, exact booleans: arrangement, winding numbers, n-ary, SafeRounding output. [exact-boolean.md](roadmap/exact-boolean.md#phase-2-items-on-exact-booleans)
- Milestone "ExactMeshBoolean 1.0" (2026-11-01: #223, #244, #248, #249, #251, #253). [exact-boolean.md](roadmap/exact-boolean.md#stage-r-saferounding)
- Milestone "Exact Boolean 2.0" (2026-11-11); D1 (#513) counted as Exact Boolean 2.1 (owner 2026-11-12). [exact-boolean.md](roadmap/exact-boolean.md#exact-boolean-20-milestone)
- Milestones "Alpha Wrap 2.0" (owner 2026-11-09) and "Reducer / Remesher 2.0" (2026-11-09).
  [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#alpha-wrap-20-milestone)
- Snap-rounding experiment measured and closed (#313, 2026-11-02). [exact-boolean.md](roadmap/exact-boolean.md#stage-r-saferounding)
- Envelope containment stopped at its stop criterion (owner 2026-11-01; #263, #268). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#stage-7-envelope-containment)
- Alpha wrap quality, stage 7b (#292, 2026-11-02); raw-wrap fixes (#311, #317); wrap resolution (#280); volume
  check (#305). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#stage-7b-alpha-wrap-quality)
- Phase 3, parameterization and geodesics. [parameterization-geodesics.md](roadmap/parameterization-geodesics.md#done--history)
- Phase 4, CLI / MCP, analysis building blocks, evaluation metrics; CLI / MCP docs for the new operations (2026-11-02). [platform.md](roadmap/platform.md#phase-4-items-cli--mcp-analysis-building-blocks-evaluation-metrics)
- Boolean property tests, DAP audit (nothing to adopt), Clipper2 adapter (#243). [platform.md](roadmap/platform.md#stage-8-planned-follow-ups)
- Issue-tracker fix list complete (2026-11-03). [platform.md](roadmap/platform.md#release-when-its-done)
- Meshing2D M2 (#233), M3 (#252), `m2.snap` (#277), provenance check 6b (#295, #296), Q17 (#358), fast oracle (#376,
  #379), grading (#375, #380), run A (#390), small-angle rule (#394). [meshing2d.md](roadmap/meshing2d.md#done--history)
- Quadtree (#195, #266): owner-machine side-by-side comparison done (2026-11-08): always faster than or as fast as
  the old quadtree. [terrain.md](roadmap/terrain.md#stage-5-quadtree)
- Terrain phase 1 (#357, 2026-11-03). [terrain.md](roadmap/terrain.md#terrain-session-record-2026-11-01-to-2026-11-03)
- Phase 5: geodesic paths and loops (#1), foldover-free maps (#7, #374); inside / outside for open inputs stopped
  (#331); the affine heat method stopped at its gate (#371). [research.md](roadmap/research.md#phase-5-candidates-from-recent-research)
- Test suite on German Windows (#383). [platform.md](roadmap/platform.md#test-suite-on-german-windows)

## Session limits

- At most 5 implementation sessions at once in the cloud, of which at most 2 large (tier L; [AGENT_WORKFLOW.md](AGENT_WORKFLOW.md)
  section 6), plus **one review session** that only reviews (independent reviews with REVIEW_CHECKLIST.md, bounds
  reviews, re-running numbers) and writes no production code. The owner's local work does not count. The limit
  applies to new starts: when sessions finish, they are not replaced until the count is below it. Each session has
  its own worktree and one PR-sized topic.
- **Exception (owner 2026-11-12):** three extra medium Sonnet sessions (CLI / MCP update, test-time backlog,
  short pipeline topics: fast-winding cache lever 3, DualContouring allocations, the
  issue-tracker follow-ups (flip-guard and P1, both closed), then the parked `.poly` reader / mesh2d area items after the CLI queue) on top of the limit;
  no further starts until the count is back below it.
- The bottleneck is review, serial merges and the mutation runners (a full shard takes hours), not sessions: a
  session whose PR waits for review does not start the next PR-sized topic in a mutated file.
- Parallel sessions must not change the same production files. Quadtree (`spatial/`) and meshing (`comp_geom/`) are
  disjoint in code; they ran at the same time because the owner worked on the quadtree locally and the review session
  took the meshing reviews.
- Merges are serial, because many PRs touch `docs/ROADMAP.md`, `docs/QA.md`, `docs/SOURCES.md`, `docs/OPERATIONS.md`
  and `ExactBooleanIssueCasesTests.cs`. Rebase the next PR and re-run CI after each merge. Keep status text in the
  PR, not in the roadmap.
- Full local CI and every timing or memory measurement run alone on the machine: the sweep's time limits react to
  load, and measurements taken next to other work are not comparable.
