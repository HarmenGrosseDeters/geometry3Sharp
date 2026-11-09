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

Relation to upstream: stay API compatible with 1.0.324, which existing users depend on. Upstream's
`dotnet8` branch has breaking changes (it removes the float types); useful parts (eg USD) can be taken over
selectively.

## Now
Status on 2026-11-08 (evening, Berlin).
- **Exact Boolean 2.0** (milestone, owner 2026-11-07): clean results on closed inputs; speed target revised
  2026-11-08 to max(5 s, 20 µs × arrangement pieces) per operation (7 small, heavily self-intersecting models as
  documented exceptions). The queue (#397, #398, #406, #409, #412, #419, #420, #424, #425 far parts, #426 edge-key hash and #440's L3 test) merged as
  one integration PR, #446 (2026-11-09, 21e27a19), on green CI and review; its full mutation run of the touched shards
  runs on master after the merge (owner decision) and seeds the rounding baselines. L3 dropped (owner 2026-11-08:
  119829/119830 now run in 0.8-3.7 s and already report RoundingClean false honestly). The ±1e308 throw is fixed in
  #449 (robustness sweep S38: the volume checks scale by a power of two, MeshRobustRepair fails with ScaleNotFinite
  above an extent of about 1e154). Then: the final Thingi10K run, the control run and the thingi-pairs runs (#418).
  Lévy classification: phase 1 done (#428, no propagation now); phase 2 done
  (parallel fast-winding build, bit-identical; classification -23 % on both samples, -40 % on tree-bound open ops). [exact-boolean.md](roadmap/exact-boolean.md#exact-boolean-20-milestone)
- **Alpha Wrap 2.0: done** (owner 2026-11-09). Final 800 run: guarantees 800/800, 0 triangles < 0.5° / > 170°,
  p01 median 11.3° (min 10.6°), fidelity not worse; speed 611/800 within budget (97 % within 1.25x), documented.
  Next: **Alpha Wrap 3.0** (starts after Exact Boolean 2.0 closes: profile and bounds first, owner sets targets; speed levers in the wrap: intersection oracle, Delaunay insertion, pinch passes; parallel evaluate / serial apply)
  and Reducer / Remesher 2.0 (#454 hand-over). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#alpha-wrap-20-milestone)
- **QA 2.0** (milestone, owner 2026-11-09): the new test tools (SharpFuzz, FPTaylor, Z3, CsCheck, coverlet,
  mutation shards, test tiers) applied across the library, not as pilots: an inventory (tool x area) first, then
  a rollout by ROI. [platform.md](roadmap/platform.md#qa-20-milestone)
- **Mutation tooling:** #403 (rounding in 7 parts) and #445 (early stop, log artifact, ticker) merged. Rule since
  2026-11-09: only the incremental run gates a PR; full shard runs go to master after the merge. JOB-1 on the owner's
  machine: no kill data (Windows runner failure), so kills only in CI. Next: smaller test sets per shard from the
  exclusive kills, full runs weekly. [platform.md](roadmap/platform.md)
- **Meshing2D:** bundle merged (fast oracle #411/#427, corner choice #437, rule C #441: −18 % triangles on the corpus,
  no verdict worse; Z3 pilot 3 #443). Coarsening closed (re-measured: −4.5 % triangles for +50 % mesher time).
  Rule C on the snap grid is open.
  [meshing2d.md](roadmap/meshing2d.md#open)
- **Phase 5:** priority by return on investment in [research.md](roadmap/research.md#open); batch queries stage 1
  done (stage 2 after the queue); the rest starts after the 2.0 milestones, next to the CLI / MCP update and a
  Reducer / Remesher 2.0 milestone.
- **Terrain:** paused since 2026-11-03; restarts with a DGM reader in the CLI, designed with `QualityMesh2`.
  [terrain.md](roadmap/terrain.md#open)

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
- **Release:** package ID and publishing (the nuget.org ID belongs to upstream); contact with rms80 for the upstream
  ledger is prepared, not initiated. [platform.md](roadmap/platform.md#open)
- **Ports from geogram and other permissive libraries:** each port is a decision with an A/B.
  [sources-surveys.md](roadmap/sources-surveys.md#open)
- **Phase 5 research items:** one that is not small goes back to the owner (owner 2026-11-03).
  [research.md](roadmap/research.md#open)

## Next / queued

Planned, not started.

- Exact booleans: superseded by Exact Boolean 2.0 WP2 (the 81 unclean operations are classified there; M3 is #398);
  M2 of the manifold corpus remains.
  [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- Exact booleans: **Z3 pilot 2** (owner 2026-11-08; pilot 1 merged in #400; step (2) merged in #407: 20 rule lemmas unsat, 17 mutants sat), in `tools/Z3Pilot`; open: (1) prove the D1
  drift-accounting rule sound (or find a counterexample) before the Boolean session builds it; (2) the error-bound
  rules of the predicate generator, one lemma per rule. [research.md](roadmap/research.md#smt-checks-with-z3)
- Exact booleans: the `thingi-pairs` class in `BooleanComparison` (local task, owner 2026-11-04). [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- Exact booleans: EMBER levers 2-4 re-ranked after the full Thingi10K run; boolean allocations (not scheduled).
  [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- Meshing2D: the corner choice after #394 (MinSin2 against the exact shortest edge). [meshing2d.md](roadmap/meshing2d.md#open)
- Meshing2D: near-contact inputs (stmlf/1461), not measured. [meshing2d.md](roadmap/meshing2d.md#open)
- Meshing2D: SplitSegment speed; the <= 2x fork speed target stays a future goal (owner 2026-11-04). [meshing2d.md](roadmap/meshing2d.md#open)
- Meshing2D: oracle leftovers on very large foreign outputs; the 72 unjudged run-A timeouts. [meshing2d.md](roadmap/meshing2d.md#open)
- Terrain, after the pause: a DGM reader in the CLI, then the later steps (2)-(8), the §11 follow-ups and the
  mutation shards (#302). [terrain.md](roadmap/terrain.md#open)
- Remeshers: degenerate triangles, self-intersections, thin walls (Phase 2, open). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#open)
- Alpha wrap: the remaining pinch caps and the p01 gap (now in Alpha Wrap 2.0). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#open)
- Parameterization: AffineLocal log-map follow-up with estimated normals and far field (low), integer
  coordinates, geodesic Bézier curves. [parameterization-geodesics.md](roadmap/parameterization-geodesics.md#open)
- Platform: the upstream deviation ledger (task 6). [platform.md](roadmap/platform.md#open)
- Platform: the issue-tracker follow-ups (P2, S2, S3, flip-guard regressions, the P1 collapse). [platform.md](roadmap/platform.md#open)

## Later / research

- Later list (point clouds, conservative marching cubes, offsets, quad remeshing, deformation, tetrahedral meshing,
  TPMS, sparse SDF grids, batch ray queries, AI bridges): [research.md](roadmap/research.md#later)
- Phase 5 candidates from recent research: [research.md](roadmap/research.md#phase-5-candidates-from-recent-research)
- Open research questions (3D snap rounding, envelope pipeline, metamorphic testing, determinism, and more):
  [research.md](roadmap/research.md#open-research-questions)
- SMT checks with Z3 (pilot 1 merged in #400; pilot 2 started, see Next): [research.md](roadmap/research.md#smt-checks-with-z3)
- Mesh-kernel ideas and EMBER: [exact-boolean.md](roadmap/exact-boolean.md#external-boolean-corpora-2026-11-04)
- Geogram and other permissive libraries: [sources-surveys.md](roadmap/sources-surveys.md)
- RWTH Aachen papers and award-winning papers: [sources-surveys.md](roadmap/sources-surveys.md)
- Shared hotspots and leads for open tasks: [hotspots.md](roadmap/hotspots.md)

## Done

- Phase 1, foundation (targets, CI, package metadata, formats, API docs). [history.md](roadmap/history.md#phase-1-foundation)
- Takeover of the abandoned work, stages 0 to 3 (2026-10-31). [history.md](roadmap/history.md#stages-0-to-3)
- Phase 2, exact booleans: arrangement, winding numbers, n-ary, SafeRounding output. [exact-boolean.md](roadmap/exact-boolean.md#phase-2-items-on-exact-booleans)
- Milestone "ExactMeshBoolean 1.0" (2026-11-01: #223, #244, #248, #249, #251, #253). [exact-boolean.md](roadmap/exact-boolean.md#stage-r-saferounding)
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
