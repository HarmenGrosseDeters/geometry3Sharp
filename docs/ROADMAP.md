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
| 4. References | Pilot: M1 against NetTopologySuite. Then the golden subset. The session that produces `Triangle.NET` results is planned, not started (strategy, section 3) | M | 1 session |
| 5. Quadtree | `quadtree` (#195): independent review, owner-machine performance and memory, side-by-side with the old quadtree | L | 1 large session |
| 6. Meshing M1 | `feature/meshing2d` (#200): oracle and differential as gates. Only then the M2 design (#201, `docs/meshing2d-m2-design`): its recorded approvals are disputed, so question its domain decisions first. An M2 implementation comes after that | L | 1 large session, after stage 5 |
| 6b. Meshing provenance check | After M3, once: a separate comparison session (the dirty side, the only one that may read Shewchuk's `triangle.c` and Triangle.NET) compares the 2D mesher with them for copied expression: data structures and memory layout, function decomposition, control flow of distinctive routines, constants, names and comments, plus a similarity tool where it applies (C against C#). It writes a report only, never code. A clean session rewrites any flagged part from the spec alone. The meshing session keeps a provenance log ([design/MESHING2D_PROVENANCE.md](design/MESHING2D_PROVENANCE.md): paper / section or spec item per non-trivial routine) and avoids Triangle-typical expression. Reason: the BSL-1.0 release, and models may have seen `triangle.c` in training. Algorithms from the papers are free to implement; expression is not | M | 1 session, after M3 |
| 7. Envelope containment | Phase 5 #2 (Wang et al. 2020; `fast-envelope` is MIT; `MeshEnvelope` is the prototype). One piece of work serves four tasks: quality passes that stay near the input, the alpha-wrap simplification, the `Reducer` `MaxDeviation` check (92-97 % of its time) and the envelope-guaranteed pipeline. It comes before the alpha-wrap stage, which keeps failing on the trade-off between quality and fidelity that an envelope settles. **Started 2026-11-01** in the indirect-predicates session: design [design/ENVELOPE.md](design/ENVELOPE.md) (#258), approved by the owner: double inputs by a filter plus exponent alignment, polyhedra after the paper's Prop. 3.1 (eps/sqrt(3); `MeshEnvelope`'s 0.8/0.6 shape only as a proven option), an internal class first (`MeshEnvelope` replaced in its own PR once the BigInteger-clipping oracle agrees and it is within 5x of `IsWithin`), one PR per step. The envelope does not replace the per-operation self-intersection checks | L | 1 large session, after stage 5 |
| 7b. Alpha wrap quality | Owner decision on #199. No recorded policy passes every gate, so its production code is not for merging as it is, and it does not compile as it is. Next step: a bounded look at the failing local patches of the hard-feature policy, no threshold tuning | L | 1 large session, after stage 7 |
| B. Boolean performance | Continues, at the owner's request. Done: `AddPoint` shards (#222). Next, in order: fixed-width exact points, 4 limbs (#223: cherchi / cgal −8 %, Thingi10K −11 %, n-ary −26 %, all hashes identical); then the **predicate compiler on fixed-width inputs**, stacked on #223 and started without waiting for its mutation runs: regenerate the arrangement's `InCircle` / `Orient` from the generator of #221 with the exact bit bounds of the 4-limb points, so that the `BigInteger` fallback (about half of the calls with the grid-fitted bounds of #221) mostly disappears; the fallback rate is a reported metric. The generator stays an offline tool with a checked-in, reviewable output and an up-to-date test, not a Roslyn source generator, because the bounds review needs the generated code in the diff. The grid-fitted `ArrInCircle` of #221 is not merged. Later: planes / CSR (section 12 of the fixed-width design), and the generator reused for the envelope predicates (stage 7). Hand-over and ranked candidates: [perf/EXACT_BOOLEAN_PERFORMANCE.md](perf/EXACT_BOOLEAN_PERFORMANCE.md). Exact code: bounds review and mutation runs on every PR. **Status 2026-11-01:** #223 (fixed-width points) and #248 (pair search by a self-join of the box tree, cgal + Thingi10K −7.9 %) wait for their mutation runs; the compiler (#244) and the indirect predicates (#246 / #250 / #257, LPI / TPI filter 47-86 ns against 3-7 µs to construct, no expansion stage) are stacked on them. CSR is dropped as a step of its own (1-2 %); a GC trace on cherchi decides later. Next: cheaper exact constructions (#256): measured first, then the coplanar shared-vertex shortcut (7-8 %), a 1-limb `orient3d` kernel, then the constructions; real-data n-ary class `nreal` (multi-component Thingi10K meshes, arrays) | L | 1 large session |
| R. SafeRounding | Its own session, kept apart from the performance work (performance must stay bit-identical; this changes results on purpose). (1) Fix: SafeRounding moves exact vertices of pieces below its degenerate threshold on clean input (union volume 17.999992370605316 instead of 17.99999237060547), pinned as a skipped test in `ExactBooleanPropertyFindings` (#229). (2) A recommendation on design question 1b of [design/BOOLEAN_PROPERTY_TESTS.md](design/BOOLEAN_PROPERTY_TESTS.md): repair input that is already degenerate or not. (3) A bounded evaluation of the research item "Practical 3D snap rounding with guarantees" below, with Thingi10K: a concept, no implementation. (4) Fix `ExactTriangleIntersection.IntersectsBeyondShared`: for coplanar neighbours sharing one vertex where one is a sliver, the projection axis comes from a floating-point cross product that rounds to 0, so a false intersection is reported (and inherited by `MeshRepairMetrics.SelfIntersections`); choose the axis exactly. Repro from the remesher session (both triangles in y = 0, ids (0,1,2) / (3,1,4)); this gives the deferred exact coplanar clipping task a real caller. **Status 2026-11-01:** (4) merged in #249 (bit-identical on the boolean hashes); `IntrTriangle3Triangle3` exact for coplanar and nearly parallel pairs, merged in #253; (1) as the displacement bound D′ (edits refused beyond 1e-5·diag) in #251, its clean counts pending; (3) concept in [design/SAFE_ROUNDING.md](design/SAFE_ROUNDING.md) | L | 1 large session, when a large slot is free (after meshing or B) |
| 8. Planned follow-ups | Boolean property tests, DAP audit and honest terrain metrics, next to any stage from 4 on; the upstream ledger last, because it records what is merged. Terrain phase 1 after stage 5. After this wave: **CLI / MCP and docs for the new operations** (quadtree, mesher, n-ary and SafeRounding booleans in `g3cli`, examples in OPERATIONS.md), so that agents can use what was built (Direction, points 2 and 3). Candidate: a **Clipper2 adapter** as an optional package (`geometry3Sharp.Clipper2`; Clipper2 is Boost-licensed): robust integer 2D polygon booleans and offsets for terrain / GIS footprints and slicing, written against Clipper2's public API, not taken from the DAP fork; the core stays dependency-free and API compatible. Decide after the DAP audit's provenance list. Done: property tests, DAP audit (nothing to adopt), Clipper2 adapter (#243). The CLI / MCP docs session waits until quadtree, mesher M2 / M3, n-ary and SafeRounding are merged, so that it documents the finished state in one pass | S, M | up to 2 sessions |

Stages 4 and 6 are prepared in [MESHING2D_PLAN.md](design/MESHING2D_PLAN.md): the gates exist as commands; the verdict on #200 and the questions on #201 are there.

Why this order: a reference must exist before an implementation is accepted against it. The quadtree comes before meshing because terrain phase 1 waits for it and its PR is further along.

Limits on parallel work:

Order when slots free up (owner, 2026-11-01; on that day 4 large and 2 medium sessions ran, above the limit below):

1. Remesher done (medium): not replaced.
2. Stage R done and the milestone "ExactMeshBoolean 1.0" reached: first a **MeshRepair wrap-resolution session**
   (medium, owner 2026-11-01): in the MeshRepair-400 worst case, `wrap.simplify` takes 24 % of the time on 17 meshes,
   because tiny inputs (450-650 triangles) are wrapped at 180k-415k triangles (resolution from the bounding box, not
   the input); couple alpha / offset to the input, start coarser, or speed up the simplifier, under the F-score and
   fidelity gates (data: `benchmarks/MeshRepairBenchmark/cases/robust_phases_2026-11-01.csv`). It precedes stage 7b
   (alpha wrap quality). The volume checks (20 % of the time, all 400 meshes) go to the self-intersection session after
   #261; the slow `exact.resolve` on small inputs (67709, 42327) goes to the boolean-performance session as a diagnosis.
   **Terrain 1a** (large) was started on 2026-11-01 in the cloud, ahead of this slot, at the owner's request, with the NoData guard of
   `TerrainSafetyLines` (a single vertex such as z = −9999 near a source refines until memory runs out; count the band's
   3D area, not only the horizontal one).
3. Meshing done (large): the **stage 6b provenance check** (medium, report only), after M3 is merged.
4. Then the CLI / MCP docs (stage 8, medium), and a review-only session when the stacks and the meshing PRs wait for
   review at the same time.
5. Owner decisions still open: whether a large performance session pauses for Terrain, stage 7b (alpha wrap) after
   stage 7, and the `Triangle.NET` reference session of stage 4.

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
| Exact checks in BigInteger rationals at scale: the 2D mesher's oracle spends minutes on 25k-50k-point meshes (meshing itself 0.65 s), mostly in the global Delaunay and boundary-chain checks | Meshing (2026-11-01) | Any oracle or exact verifier on large inputs | Meshing session, now: measure, then the local Delaunay criterion (O(n) instead of global), then a double filter whose bound comes from `tools/PredicateCompiler`, BigInteger only as fallback; the oracle stays independent of `ExactPredicates`, which the mesher uses |
| `ExactMeshBoolean.coplanar_stack`: quadratic, pieces × coplanar triangles (42327: 495M `Orient` calls, 87 % of a 46 s resolve) | Boolean performance diagnosis (2026-11-01) | MeshRepair's exact stage on inputs with heavy coplanar overlap | Open. Idea: pieces of one source triangle are wholly inside or outside a coplanar triangle, whose edges are constraints, so decide membership by a flood fill over the piece adjacency, not per piece |
| SafeRounding on rotated (general double) inputs: 72-86 % of a resolve (67709 rotated 18.5 of 21.6 s; 42327 rotated 36.8 of 50.8 s), 18-28 % of 110-172M `Orient3d` calls exact | Boolean performance diagnosis (2026-11-01) | MeshRepair (its benchmark resolves a rotated copy too) | Open, after stage R. Measure kernel B's admission there first: a 2-limb kernel B only if that shows a real share |
| `Reducer` `MaxDeviation`: 92-97 % of its time | Phase 5 #2 | Remeshers that must stay near the input | Open: the envelope (stage 7) was stopped by its stop rule (#268, design/ENVELOPE.md §6: 7.5× `IsWithin` with ε/√3, and the wider 0.8/0.6 shape no faster than `MeshEnvelope`). Next candidate: faster `TriangleDeviationBounds` (BVH Hausdorff upper bounds) |

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
  about 17x the time (`benchmarks/RemeshQualityBenchmark`, PreventSelfIntersections). Open: the cost (a clearance
  filter; the 2D shortcut for coplanar neighbours, about -30 % on CAD meshes, after the
  `ExactTriangleIntersection` fix of stage R), and the thin walls, where the check blocks the folds but the result
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
    from Triangle / Triangle.NET code. Builds on `ExactTriangulation2` and the adaptive predicates.

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

- [ ] Hard cases from the issue trackers of those libraries reproduced as tests here (docs/QA.md 2.11), and passing
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
- AI bridges as a separate package (ONNX Runtime): project 2D segmentation masks (SAM, DINO) from several views onto face selections, texture reprojection via ExpMap

## Phase 5: candidates from recent research

Checked against what the library already has (exact booleans, alpha wrap, intrinsic triangulations, BFF / SLIM,
vector heat). Ordered by priority: effort against what it builds on and what it unlocks. Licenses checked at the
source repositories (October 2026); "paper only" means the reference code may not be ported.

| # | Feature | Research / port source | License of reference code | Why / builds on |
|---|---|---|---|---|
| 1 | Geodesic paths, loops and Bézier curves on surfaces (FlipOut). **Done:** paths, waypoints, loops (`MeshGeodesicPath`); Bézier curves open | Sharp & Crane 2020 *You Can Find Geodesic Paths in Triangle Meshes by Just Flipping Edges*; geometry-central | MIT | Almost entirely on `IntrinsicTriangulation`; cut lines (orthotics), UV seams, curves on surfaces |
| 2 | Polyhedral envelope containment. **Prototype:** `MeshEnvelope` (exact, tested), but not used by the `Reducer`: its exact path clips the query against every prism it spans, about 100 times slower than `TriangleDeviationBounds` once reduced triangles span many prisms (`benchmarks/ReducerBenchmark`). Open: the paper's algorithm (filtered indirect predicates on critical points, no clipping) | Wang, Schneider, Hu, Attene, Panozzo 2020 *Exact and Efficient Polyhedral Envelope Containment Check*; `fast-envelope` | MIT | The `MaxDeviation` check takes 92-97 % of the reduction time (`benchmarks/ReducerBenchmark`), so a fast exact check would pay off. Alternative: speed up `TriangleDeviationBounds` itself |
| 3 | ~~Robust 2D polygon booleans and offsets: port Clipper2~~ Not planned: Clipper2 (Angus Johnson, Boost-1.0) has an official C# version on NuGet; use it directly (at most a thin adapter to `GeneralPolygon2d`). A port would only duplicate it. Straight skeleton / 2D medial axis from papers if needed (CGAL is GPL) | Clipper2 | Boost-1.0 | |
| 4 | Constrained Delaunay tetrahedrization, then tetrahedral meshing | Diazzi, Panozzo, Vaxman, Attene 2023 *Constrained Delaunay Tetrahedrization: A Robust and Practical Approach*; Hu et al. 2020 *Fast Tetrahedral Meshing in the Wild* | CDT: GPL / LGPL, paper only; fTetWild: MPL-2.0, paper only | On `Delaunay3` and the implicit points of `ExactMeshArrangement` |
| 5 | Generalized signed distance and winding numbers for open / broken inputs | Feng & Crane 2024 *A Heat Method for Generalized Signed Distance*; Feng, Gillespie, Crane 2023 *Winding Numbers on Discrete Surfaces* | paper | Complements `MeshGeodesicDistance`, `VectorHeatMethod` and the repair stages |
| 6 | Grid-free PDE solving (Monte Carlo geometry processing) | Sawhney & Crane 2020 *Monte Carlo Geometry Processing* (walk on spheres); Sawhney et al. 2023 *Walk on Stars*; Zombie | MIT | Needs only closest-point and ray queries (`DMeshAABBTree`, winding numbers); works on dirty geometry |
| 7 | Foldover-free (injective) maps | Garanzha et al. 2021 *Foldover-free maps in 50 lines of code* | paper | Closes the gap noted in Phase 3: cone parameterizations are not guaranteed globally injective |
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
