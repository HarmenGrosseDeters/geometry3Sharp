# Roadmap

## How to use this file

- This index holds status only: the direction, what runs now, what waits on the owner, what is queued, and what is
  done, one to three lines per item with a link.
- Details, numbers and history live in the subpages under [roadmap/](roadmap/). Each subpage starts with a short
  header, then "Open" (what is still open, with owner decisions and dates), then "Done / history" (the record).
- Decisions are recorded with their date and "owner", eg "owner 2026-11-07".
- When an item changes status, update its line here and its subpage in the same PR. Keep status text in the PR,
  not in the roadmap (see [Session limits](#session-limits)).
- "(status unclear, check)" marks items whose status the restructuring of 2026-11-08 could not settle from the text.

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
  (#631). In progress: the 55928 tail-case stage, draft #633: budget, cancellation, L1 and L2 done, L3 step 1 pushed
  (Difference total 973 -> 421 s, hash unchanged); open: the classification filter from local corner coordinates,
  the combinatorial zeros, possibly InCircle limbs. Then T5 / T1b per the execution order. [exact-boolean.md](roadmap/exact-boolean.md#exact-boolean-30-progress)
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
  #641, #648; open PRs #642, #643, #646; open bugfix issues #636-#638, #644, #645, #647 (labelled `local`); #628 fixed by #634.
  [platform.md](roadmap/platform.md#qa-20-milestone)
- **Mutation tooling:** #403 (rounding in 7 parts) and #445 (early stop, log artifact, ticker) merged. Rule since
  2026-11-09: only the incremental run gates a PR; full shard runs go to master after the merge. JOB-1 on the owner's
  machine: no kill data (Windows runner failure), so kills only in CI. Next: smaller test sets per shard from the
  exclusive kills, full runs weekly. Found 2026-11-11: Stryker 5.0 places mutants in the whole library (~138k) and
  the shard's `mutate` filter only skips them (579 tested in boolean-1), so the coverage capture pays for every placed
  mutant in the hot loops (boolean capture > 5.7 h for a 22 s suite); the fix (only the shard's files are
  mutated) is in #615 (CI-speed session, open), with the 3.0 process (no full shards on PRs, scheduled full runs).
  [platform.md](roadmap/platform.md)
- **Scheduled runs on GitHub are unreliable:** `coverage.yml` (cron 03:41 UTC) has no scheduled run yet, none on
  2026-11-12; `nightly.yml` (cron 02:23 UTC) ran once on schedule (2026-11-11, started 09:36 UTC), none on 2026-11-12.
  [platform.md](roadmap/platform.md#open)
- **Meshing2D:** bundle merged (fast oracle #411/#427, corner choice #437, rule C #441: −18 % triangles on the corpus,
  no verdict worse; Z3 pilot 3 #443); rule C on the snap grid done (#462). Coarsening closed (re-measured: −4.5 %
  triangles for +50 % mesher time).
  [meshing2d.md](roadmap/meshing2d.md#open)
- **Phase 5:** priority by return on investment in [research.md](roadmap/research.md#open); batch queries stage 2
  merged (#448), intersection-free dual contouring options A and B merged (#552, #572), point clouds step C1 merged (#580);
  the CLI / MCP update is still queued.
- **Terrain:** closed out (owner 2026-11-09): DGM reader / writer, grid TIN (#491, #504), `g3cli` `dgm` / `dgm_tin`
  (#506), cell volumes (#507), mutation score (#508); the rest is parked until a use case. [terrain.md](roadmap/terrain.md#open)

## Open decisions (owner)

- **Meshing2D, Terminator:** a region-based oracle excuse would lower the guarantee near small input angles (a spec
  change; research item, not scheduled). [meshing2d.md](roadmap/meshing2d.md#open)
- **Exact booleans, second kernel:** EMBER levers 1 (quantized 26-bit inputs) and 5 (a new architecture) only as a
  decision on a second, opt-in kernel (owner 2026-11-04); a patent check first. [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- **Exact booleans, arithmetic without `BigInteger`:** `claude/int256-spike` and `claude/collinear-shortcut` are
  exact code and go to the owner; keep both branches. [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- **Exact booleans, `coplanar_stack` worst-case latency** (flood fill over the piece adjacency): the owner's
  decision. [hotspots.md](roadmap/hotspots.md#shared-hotspots-across-sessions)
- **Boolean allocations:** `System.Buffers`' ArrayPool as a new netstandard2.0 dependency, only if step (3) is
  reached. [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- **Clipper2 C# sources:** upstream archived 2026-10-28 (C++ only from now on, new maintainer at PolyClipping/Clipper2);
  integrate the frozen C# 2.0.0 sources into the addon and fix FINDINGS item 5 ourselves, or keep the package? No
  upstream report (owner 2026-11-11). [platform.md](roadmap/platform.md#open)
- **Release:** package ID and publishing (the nuget.org ID belongs to upstream). [platform.md](roadmap/platform.md#open)
- **Ports from geogram and other permissive libraries:** each port is a decision with an A/B.
  [sources-surveys.md](roadmap/sources-surveys.md#open)
- **Phase 5 research items:** one that is not small goes back to the owner (owner 2026-11-03).
  [research.md](roadmap/research.md#open)

## Next / queued

Planned, not started.

- Exact booleans: superseded by Exact Boolean 2.0 WP2 (the 81 unclean operations are classified there; M3 is #398);
  M2 of the manifold corpus remains.
  [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- Exact booleans: EMBER levers 2-4 re-ranked after the full Thingi10K run; boolean allocations (not scheduled).
  [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- Meshing2D: near-contact inputs (stmlf/1461), not measured. [meshing2d.md](roadmap/meshing2d.md#open)
- Meshing2D: SplitSegment speed; the <= 2x fork speed target stays a future goal (owner 2026-11-04). [meshing2d.md](roadmap/meshing2d.md#open)
- Meshing2D: oracle leftovers on very large foreign outputs; the 72 unjudged run-A timeouts. [meshing2d.md](roadmap/meshing2d.md#open)
- Remeshers: degenerate triangles, self-intersections, thin walls (Phase 2, open). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#open)
- Parameterization: integer coordinates (the AffineLocal follow-up closed in #538, geodesic Bézier curves merged in
  #540). [parameterization-geodesics.md](roadmap/parameterization-geodesics.md#open)
- Platform: the issue-tracker follow-ups (P2, S2, S3, flip-guard regressions, the P1 collapse). [platform.md](roadmap/platform.md#open)

## Later / research

- Later list (point clouds, conservative marching cubes, offsets, quad remeshing, deformation, tetrahedral meshing,
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
