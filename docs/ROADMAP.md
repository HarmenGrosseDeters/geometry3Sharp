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
4. **Modern build, wide reach.** net10.0 plus netstandard2.0 (Unity, .NET Framework), CI. No public package is
   planned (owner 2026-11-16).
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
Status on 2026-11-17 (morning, Berlin). Each subpage ends with an "Open for the future" list, ordered by priority;
this section names only what is in flight.

- **Exact Boolean 3.0** (closing): the functional and speed work is done. #962 (parallel assembly), the probes #967 and
  the last speed PR #1002 (`touch_only` asks `TriangleTriangleFilter.BeyondShared` first, bit-identical) are merged;
  speed work has stopped (owner 2026-11-16). T6 is closed with the owner's 12-thread data (#1013: 1.43x CGAL summed,
  2.05x median; 1.14x / 1.70x without the rounding). Left: the completion gate, a full mutation run of every shard on
  master 6117a866 (run 44013112256, dispatched 2026-11-17). [exact-boolean.md](roadmap/exact-boolean.md#after-30-owner-2026-11-16)
- **Boolean robustness round** (owner 2026-11-16/17): the triage of #985, #993, #994, #1010 and #1011 is merged
  ([BOOLEAN_TRIAGE_2026-11.md](research/BOOLEAN_TRIAGE_2026-11.md), #1015): one library defect (RC1, an emptied result),
  one oracle false positive (RC3), the benchmark's re-welding of split pinches (RC2) and near-coincident surfaces (RC4).
  The bugfix PRs run (RC1, RC3, the benchmark, then an RC4 rerun); the owner's local CsCheck / MeshFuzzPilot campaigns
  add their findings later. [exact-boolean.md](roadmap/exact-boolean.md#after-30-owner-2026-11-16)
- **Exact imprint:** `ExactMeshImprint` merged (#981, #990). In flight: the corpus run against `MeshMeshCut`, the
  evidence for the deprecation decision. [exact-boolean.md](roadmap/exact-boolean.md#open-for-the-future-2026-11-15)
- **Alpha Wrap 3.0:** done. Defaults decided: lever 6 K stays opt-in (#736); the raw-size budget stays off, with the
  opt-in `MaxAlphaCoarsening` (#1014, in review; recipe budget 300 000 + cap 1.2; owner 2026-11-17). The 800 run of the default
  candidates is merged (#1008). [alpha-wrap-and-repair.md](roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15)
- **Dual contouring:** exact Hermite data by default (#991), octree simplification on by default with tolerance 0.25
  (#995, #1003). A faster QEF solver is below the bar (3-6 % in a probe session, 2026-11-17, no PR); the per-call
  allocation of `SymmetricEigenSolver` is not (pooling it in the same probe: -17 to -20 % at 4 threads on two of three
  shapes); the library fix is in flight and its PR carries the benchmark. The 2026-11-12 drop of
  "DualContouring allocations" (bound 4 %) measured the solidify's contour, not this one.
- **Deformation:** ARAP #896 and biharmonic #897 merged; ARAP initial positions and `Prepare` / `Solve` (#1004).
- **2D mesher:** #986 (Empty at an aspect ratio of 2^41 and more) is the documented automatic merge tolerance; it gets
  an explicit diagnostic and the documented limit, no status change (owner 2026-11-17). [meshing2d.md](roadmap/meshing2d.md#open-for-the-future-2026-11-15)
- **Corpora:** ABC chained booleans (#1001, #1005, #1007, #1012: ours clean through all steps in 75 of 90 chains,
  Manifold 34, CGAL 4) and thingi-contact (#982, #992, #997) run on GitHub runners.
- **Platform and CI:** coverage gate fixed (#959), runtime watch baseline (#960), the mutation process port (#958) and
  the 7-part split (#913). In flight: #999 (xatlas report), #1006 (UV parameterization mutation shards); #1009
  (benchmark `fm_ms` above 20 s) got `OuterFaceIncremental` as an opt-in (#1036, owner 2026-11-17); some inputs stay above 20 s with it, so #1009 stays open. [platform.md](roadmap/platform.md#open-for-the-future-2026-11-15)
- **Determinism:** stage 0 and its obligations are merged; the cast audit is done (#973). [platform.md](roadmap/platform.md#open-for-the-future-2026-11-15)
- **Research:** contouring certificate stage 3 (growth of failing blocks) merged; uncertified cells at /128 651 -> 381,
  no wrong run certified. Refinement and transitions are not built under the stop rule; #944 closed, the surviving
  mutant M16 is #1016. Metamorphic testing (#983, #984, #988). The Geogram survey is merged (#1017); owner 2026-11-17: spectral conformal opt-in with an atlas A/B, RDR stage 1b not now
  ([sources-surveys.md](roadmap/sources-surveys.md)). [research.md](roadmap/research.md#open-for-the-future-2026-11-15)
- **Robustness sweep leftovers** (owner 2026-11-17): relative tolerances S11-S13 and S16, the known failures R35
  (`MeshDeviation`), R13 and R15, double-precision frame queries (S6, S10), and the full-run test durations of #698.
- **Owner's local runs:** #929 (net48 vs net10) open.
- **Terrain:** nothing in flight. **Clipper2 #955:** precision diagnosis: 73 grid-rounding losses (oracle allowance), 1 resolution-dependent engine error, 8 zero-area rings at the 40-bit default; regressions in review (2026-11-17); frame and engine unchanged.

## Open decisions (owner)

Checked on 2026-11-17.

- **Deprecating the float `MeshMeshCut` / `MeshBoolean`:** after the imprint corpus run (in flight).
  [exact-boolean.md](roadmap/exact-boolean.md#open-for-the-future-2026-11-15)
- **Branch deletion:** superseded branches (eg `claude/int256-spike`, `claude/collinear-shortcut`); sessions cannot
  delete branches. [exact-boolean.md](roadmap/exact-boolean.md#other-open-items)
- **Terrain:** the slope envelope construction, berms and inter-ramp checks, the GeoTIFF reader, 3D blocks.
  [terrain.md](roadmap/terrain.md#open-for-the-future-2026-11-15)

Decided on 2026-11-17 (owner): split pinches stay "not clean" in `NotCleanReason` (only the benchmark reports them as
their own class); the cleanup never empties a non-empty result (RC1); the inside-out check gets an exact volume sign
(RC3); #846 stays off; #986 option (A).

Standing rules (not pending): a Phase 5 item that is not small goes to the owner first (owner 2026-11-03); each port
from geogram (BSD-3, may be read and ported, owner 2026-11-03) or another permissive library is a decision with an A/B
([sources-surveys.md](roadmap/sources-surveys.md#open)).

## Next / queued

In order:
- The 3.0 completion gate (run 44013112256); then the roadmap status "3.0 complete".
- The boolean bugfix round (RC1, RC3, the benchmark pinch class, the RC4 rerun), then the owner's campaign findings.
- The imprint corpus run, then the owner's deprecation decision on `MeshMeshCut` / `MeshBoolean`.
- After the completion gate: the thingi-pairs baseline and final runs (owner 2026-11-12).
- Geogram: the spectral conformal opt-in with an atlas A/B (owner 2026-11-17); RDR stage 1b not now.
- The robustness sweep leftovers and #698 (sessions running).

## Order after Exact Boolean 3.0 (owner 2026-11-15)

1. **Finish what runs.** Exact Boolean 3.0: everything but the completion gate is merged (#962, #967, #1002, T6
   closed in #1013). Alpha Wrap 3.0 is done (the
   adaptive-alpha spike is a no-go, #924). Clipper2 is done (#914, #933, #938, #949; the remaining failures #955 are
   diagnosed as 73 grid-rounding losses (oracle allowance), 1 resolution-dependent engine error and 8 zero-area rings at 40 bits; regressions in review, 2026-11-17).
2. **In parallel:** the contouring topology certificate, option A: stages 1-3 merged (#779, #925, #935, #946;
   certified runs 4 -> 10 of 14 at / 128, never wrong); refinement for thin features deferred (#944).
3. **Rounding** (hard problems 1 and 3): the plateau variant of the position search is a no-go
   (owner 2026-11-15, no spike; #721 closed, it stays a design).
4. **Scale-free robust repair** (hard problem 9): not scheduled, since the rounding spike is a no-go.
5. **Tet meshing:** not scheduled; it builds on the alpha wrap and the rounding. Sharp features (hard problem 4)
   only if CAD cases need them.

Alongside, without dependencies: the determinism proof (hard problem 11: the analyzer #906, then the net48 / net10
measurement; stage 1, the exact building blocks, in review: DETERMINISM_PROOF.md 5.4); a quality-aware manifold step in the alpha wrap (diagnosis in #919, closed as valid
output). Last, by "depth over breadth" (owner 2026-11-10):
CCD (no caller in the library), point clouds, deformation, `ContourLoft`.

## Later / research

- Exact booleans after 3.0 (owner 2026-11-16): maintenance mode; the exact imprint is the only new
  feature; everything else is parked or dropped, see the triage in [exact-boolean.md](roadmap/exact-boolean.md#after-30-owner-2026-11-16).
- Meshing2D research: Shewchuk's Terminator with a region-based oracle excuse (owner 2026-11-06, option c: not built),
  matched shells, the grading placement strategy, parked until a use case; the <= 2x fork speed target is closed (owner
  2026-11-16). [meshing2d.md](roadmap/meshing2d.md#open)
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
- Adaptive simplification of dual contouring (owner 2026-11-16: `DualContouring.Simplify`, default on with opt-out; `AdaptiveDualContouring`,
  #995 and the default-on PR): [design/DC_ADAPTIVE_SIMPLIFICATION.md](design/DC_ADAPTIVE_SIMPLIFICATION.md). Open idea, opt-in, not scheduled: a
  coarse-first construction (skip the fine cells of flat regions, no dense grid) needs bounds on the implicit (a Lipschitz constant or interval
  evaluation); measured upper bound of the sparse grid alone: 8-15 % of the time.
- Hard open problems, candidates for a selective investigation with the stronger Fable model (owner 2026-11-09):
  [research.md](roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)
- SMT checks with Z3 (pilot 1 merged in #400; pilot 2 steps (1) and (2) merged in #416 and #407): [research.md](roadmap/research.md#smt-checks-with-z3)
- Mesh-kernel ideas and EMBER (EMBER / octree-BSP architecture shelved, owner 2026-11-17): [exact-boolean.md](roadmap/exact-boolean.md#external-boolean-corpora-2026-11-04)
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
- Issue-tracker fix list complete (2026-11-03). [platform.md](roadmap/platform.md#quality-bar-when-its-done)
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
