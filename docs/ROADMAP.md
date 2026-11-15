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
Status on 2026-11-15 (late evening, Berlin). Each subpage ends with an "Open for the future (2026-11-15)" list, ordered
by priority; this section names only what is in flight.

- **Exact Boolean 3.0** (closing out): #883, #881, #893, #843, #837, #922 (T6 against CGAL) and #953 (thread-scaling
  analysis) are merged; candidate C is a no-go (#915). Open: the mutation shards #913 (7-part split, its last shards
  running) and lever 1, `TriangleBoxTree` parallel build from 8192 boxes (#956, held until a re-run of #837's cases
  explains its +7 % classification). Then the thingi-pairs runs. [exact-boolean.md](roadmap/exact-boolean.md#open-for-the-future-2026-11-15)
- **Alpha Wrap 3.0:** done ((B) #774 and #898 merged, 800 run passed; the adaptive alpha spike is a no-go, #924). Open:
  the default decisions for lever 6 K (#736) and the raw-size budget (#846). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15)
- **Platform and CI:** draft PRs no longer run CI automatically (#952); the suite is faster (#954). In flight (CI
  hygiene session 2): the coverage gate (red since 2026-11-13), the scheduled mutation runs (every scheduled run
  since 2026-11-02 cancelled, none after 2026-11-09), the runtime-watch rule (#888). [platform.md](roadmap/platform.md#open-for-the-future-2026-11-15)
- **Determinism:** stage 0 and its obligations (#871, #891, #892, #934, #942, #951) are merged; #804's remaining
  casts are open. [platform.md](roadmap/platform.md#open-for-the-future-2026-11-15)
- **Local (owner machine, 2026-11-15/16):** the net48 vs net10 comparison (#929), the boolean candidate pairs and the
  thread scaling beyond 4 cores (#858). Finding so far: at 1 thread the boolean is about 45 % faster on 22370752 than
  on 2026-11-14's master (likely #769 and #881); #772 alone is neutral.
- **Research:** ARAP / biharmonic deformation #896 / #897 (owner, ready 2026-11-16); contouring certificate stage 3
  merged (#946), refinement deferred (#944). [research.md](roadmap/research.md#open-for-the-future-2026-11-15)
- **Meshing2D, terrain, Clipper2:** nothing in flight; #870, #855, #854 closed; Clipper2 #955 deferred (owner
  2026-11-15). [meshing2d.md](roadmap/meshing2d.md#open-for-the-future-2026-11-15),
  [terrain.md](roadmap/terrain.md#open-for-the-future-2026-11-15)

## Open decisions (owner)

Checked on 2026-11-15 (late evening).

- **Release:** package ID and publishing (the nuget.org ID belongs to upstream); after Exact Boolean 3.0 is closed out.
  [platform.md](roadmap/platform.md#open)
- **Alpha wrap defaults:** lever 6 K (#736) and the raw-size budget (#846) on by default or not; an 800 run per lever
  first. [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15)
- **Branch deletion:** superseded branches (eg `claude/int256-spike`, `claude/collinear-shortcut`); sessions cannot
  delete branches. [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- **Deprecating the float `MeshMeshCut` / `MeshBoolean`:** not now (coordinator 2026-11-15); first an exact imprint
  on `ExactMeshArrangement` (after the release). [exact-boolean.md](roadmap/exact-boolean.md#open-for-the-future-2026-11-15)
- **Terrain:** the slope envelope construction, berms and inter-ramp checks, the GeoTIFF reader, 3D blocks.
  [terrain.md](roadmap/terrain.md#open-for-the-future-2026-11-15)

Standing rules (not pending): a Phase 5 item that is not small goes to the owner first (owner 2026-11-03); each port
from geogram (BSD-3, may be read and ported, owner 2026-11-03) or another permissive library is a decision with an A/B
([sources-surveys.md](roadmap/sources-surveys.md#open)).

## Next / queued

In order:
- Merge #913 and #956; then the thingi-pairs baseline and final runs (Haiku runner) and the rest of #615.
- The 800 runs for the alpha wrap default decisions (Haiku runner), then the owner decides.
- Evaluate the owner's local run (#929, #858, the boolean candidate pairs) and write the finding into the boolean docs.
- Review and merge #896 / #897 (ARAP, biharmonic).
- Release preparation once the owner decides.

## Order after Exact Boolean 3.0 (owner 2026-11-15)

1. **Finish what runs.** Exact Boolean 3.0: T1b #843, the T6 check #922 and the thread-scaling analysis #953 are
   merged; left are the mutation shards #913, lever 1 #956 and the two thingi-pairs runs. Alpha Wrap 3.0 is done (the
   adaptive-alpha spike is a no-go, #924). Clipper2 is done (#914, #933, #938, #949; the remaining failures #955 are
   deferred, owner 2026-11-15).
2. **In parallel:** the contouring topology certificate, option A: stages 1-3 merged (#779, #925, #935, #946;
   certified runs 4 -> 10 of 14 at / 128, never wrong); refinement for thin features deferred (#944).
3. **Rounding** (hard problems 1 and 3): the plateau variant of the position search is a no-go before the release
   (owner 2026-11-15, no spike; #721 closed, it stays a design).
4. **Scale-free robust repair** (hard problem 9): not before the release, since the rounding spike is a no-go.
5. **Release** (owner decision): once the outputs are stable.
6. **Tet meshing:** after the release; it builds on the alpha wrap and the rounding. Sharp features (hard problem 4)
   only if CAD cases need them.

Alongside, without dependencies: the determinism proof (hard problem 11: the analyzer #906, then the net48 / net10
measurement); a quality-aware manifold step in the alpha wrap (diagnosis in #919, closed as valid
output). Last, by "depth over breadth" (owner 2026-11-10):
CCD (no caller in the library), point clouds, deformation, `ContourLoft`.

## Later / research

- Exact booleans, not scheduled: EMBER lever 2 (skip self-pairs of results certified free of self-intersections in chained
  booleans); levers 1 and 5 (quantized inputs, a new architecture): no rewrite (owner 2026-11-07), patent search done
  2026-11-10 (nothing found); exact output coordinates of the coplanar 2D
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
- November 2026 (2026-11-12 to 2026-11-15): CLI / MCP update (#707), Clipper2 vendored (#751, #851, #874), FPTaylor T12
  (#627), runtime watch (#726), determinism stage 0 (#871), monomial fallback (#769), CCD (#778, #793, #872), tet
  stages 1-2 (#775, #821), TPMS (#838, #879), `MeshOffset` (#799). [history.md](roadmap/history.md)

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
- **Suspended (owner 2026-11-12, after the weekly reset):** the limit above is out of force until the owner sets it
  again; the coordinator still starts one PR-sized topic per session and keeps parallel sessions off the same files.
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
