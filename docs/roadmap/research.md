# Research, Phase 5 candidates and later

Part of the [roadmap](../ROADMAP.md).

- **Scope:** items that are not scheduled: the "Later" list, the Phase 5 candidates from recent research, the open
  research questions, and the SMT pilot.
- **Status (2026-11-08):** Phase 5 #1 (paths, loops) and #7 (#374) done, #5 stopped (#331), the affine heat method
  stopped at its gate (#371); everything else is not started.
- **Related:** [research/CROSS_FIELD_GEOMETRY.md](../research/CROSS_FIELD_GEOMETRY.md),
  [design/RESEARCH_GATES_2026_11_03.md](../design/RESEARCH_GATES_2026_11_03.md),
  [design/INSIDE_OUTSIDE_OPEN_INPUTS.md](../design/INSIDE_OUTSIDE_OPEN_INPUTS.md),
  [design/AFFINE_LOGMAP_GATE.md](../design/AFFINE_LOGMAP_GATE.md); sources in [sources-surveys.md](sources-surveys.md).

### Open

- **Priority by return on investment (owner 2026-11-08; after the 2.0 milestones, next to the CLI / MCP update;
  each item starts with an upper bound / prior-art check, Sonnet with review; design commands for the CLI / MCP
  alongside):**
  1. Batch ray / point queries (`IntersectBatch`, `IsInsideBatch`; owner idea 2026-11-02), M: every app needs mass
     ray casts and inside tests; BVH and exact tests exist; SIMD pays off here; existing API unchanged.
  2. Continuous collision detection (Tight-Inclusion, Wang et al. 2021, MIT, Phase 5 #8), M: conservative and
     provably correct; Unity, robotics, simulation; nothing comparable in .NET.
  3. TPMS / lattice structures from implicit functions, S: builds on `MarchingCubes` `LinearEdges` and its enclosure
     guarantee; additive manufacturing showcase.
  4. Point clouds in stages: normal orientation first (fast winding numbers exist), then screened Poisson, L.
  5. Deformation (ARAP, biharmonic; paper only, libigl is MPL), M; check existing Laplacian building blocks first.
  6. Geodesic Bézier curves (rest of Phase 5 #1), S; niche.
  Then: quad remeshing (L, licences to check), tetrahedral meshing (#4, L+, paper only), feature-preserving offsets
  (L, patent check, deferred 2026-11-05), intrinsic simplification (#9), Reach for the Spheres (#10), sparse SDF grids.
  Not to do: Monte Carlo PDEs (#6; no caller, conflicts with bit-for-bit determinism).
- **Phase 5 next research items** (owner 2026-11-03, only if the effort is really small): item 1 merged (#374),
  item 2 stopped at its gate (#371), item 3 only as the reduced method; probe `PlanarComplex.FindSolidRegions` first
  when it is picked up (later, owner 2026-11-04). One that is not small goes back to the owner.
- **Phase 5 table:** #1 Bézier curves open; #2 the paper's envelope algorithm open; #4, #6, #8, #9, #10 not started;
  #3 not planned (use Clipper2); #6 stays later (no caller yet).
- **SMT checks with Z3** (owner 2026-11-04): pilot 1 merged in #400 (2026-11-08); pilot 2 for the exact booleans: step (2), the predicate generator's filter-bound rules, merged in #407 (2026-11-08); step (1), the D1 drift lemma, waits for the Boolean session's design note.
- **Later:** point clouds, conservative marching cubes, feature-preserving offsets, quad remeshing, deformation,
  tetrahedral meshing, TPMS, sparse SDF grids, batch ray / point queries, AI bridges. Not scheduled.
- **Open research questions:** practical 3D snap rounding (next probes: ideas 1 and 3 of #368), the
  envelope-guaranteed pipeline, metamorphic testing, bit-for-bit determinism, exact intrinsic triangulations, Monte
  Carlo PDEs on soups, geometry APIs for agents, showcase domains with error bounds.

### Done / history

#### SMT checks with Z3

**SMT checks with Z3 (owner 2026-11-04, a pilot, not scheduled):** Z3 (Microsoft, MIT licence; NuGet `Microsoft.Z3` with a native library) only in a separate test or tool project, never in the library. Pilot, about half a day: (1) prove the `SnapGrid.Pixel` double filter of #380 with Z3's IEEE-754 theory: for every double x and width W in the stated range the filter's decision equals the exact floor(x/W) (today: a written derivation, review and 2.7M sampled cases); (2) for one surviving boundary mutant (e.g. the oracle's zone-filter margins of #376), ask Z3 for an input where original and mutant decide differently and pin it as a test, or show the mutant equivalent. If both work: a standard step for every new double filter in front of an exact test. Further fits: finite rules (EMBER-style early-out over winding numbers, oracle excuse rules never broader than the mesher rule), and an independent check of bit-width tables. Not a fit: error bounds of large predicates (Orient3d, InCircle; bit-precise FP does not scale there; tools like Gappa or FPTaylor, or our derivations plus review) and whole-algorithm properties (termination, Delaunay, boolean correctness; theorem provers, out of proportion; our certifying oracles cover them).

**Pilot 1 result (#400, merged 2026-11-08):** the `SnapGrid.Pixel` filter proved by 6 modular obligations (all unsat, preconditions sat; the monolithic attempt returns unknown and is not counted), a boundary-margin mutant shown equivalent and pinned by a test, in `tools/Z3Pilot` (Python `z3-solver`, hash-pinned, own workflow).

**Pilot 2, exact booleans (owner 2026-11-08, started after #400):** Z3 as a proof checker for small lemmas, never in
the library and never at run time. (1) D1 first: the Boolean 2.0 session states its tighter drift-accounting rule for
SafeRounding as a precise lemma (assumptions; claim: the Hausdorff invariant H(S, S0) <= MaxDrift is kept, while freedom from new intersections and degeneracy comes from the exact checks in improves()); lemma written in docs/design/ROUNDING_DRIFT_APEX.md (claude/d1-drift-lemma), Z3 check started 2026-11-08; Z3 (nonlinear real
arithmetic, with rounding modelled as (1 + δ), |δ| <= u) proves it or returns a counterexample, which becomes a
procedural test; D1 is built only after that. (2) The error-bound rules of the predicate generator (the per-operation
rules from which the static filter bounds are derived), one lemma per rule, as a mechanical second check next to the
bound review CLAUDE.md demands. This is consistent with "not a fit" above: the rules are small, the large predicates
are not proved as a whole. Pilot 1 used the Python package `z3-solver` (MIT), not the NuGet `Microsoft.Z3`; still only
in the tool project.

**Pilot 2, step (1) result (2026-11-08, draft PR #416):** the D1 drift lemma of SafeRounding (the apex rule of `docs/design/ROUNDING_DRIFT_APEX.md`) in `tools/Z3Pilot/drift_apex.py`, derived from the rule's specification in the file: I1, I2, `d' >= d` and `max d' <= maxLength` per illustrative edit shape (moves, 25 contractions, removal, flip, strip zips of a 3-vertex loop with the middle chain vertex as apex; zip, 4-vertex strip loop and ear contraction without an apex) and shape-free (only "apex in every added triangle" and "M over R and A" are used): 168 obligations unsat with satisfiable, replayed assumptions; 13 mutants sat (a non-apex vertex charged, `M` without `δ`, `M` over `A` only, no acceptance, an ear with one vertex charged). Zips, longer strip loops and ear contractions (the kept boundary vertex is never moved) have no apex and keep today's rule. The chain bound `d <= d̂ (1 + u)^k + k ε` holds with Higham (2.5) and is SAT with the note's first model (2.4), now a documented witness (note corrected in fd931c69). Review only: (E1)/(E2) per edit type, untouched triangles keep their geometry, the distance model, I1/I2 => Hausdorff, the induction over edits, one chain counter for all vertices; the source contract waits for the D1 implementation (skipped test).

**Pilot 2, step (2) result (2026-11-08, draft PR):** the filter rules of `tools/PredicateCompiler` (leaf, sum, product, magnitude, decision, zero shortcut, gate exponents, no-underflow grid, overflow, and the value rule) as 20 lemmas in `tools/Z3Pilot/filter_rules.py`: all unsat with satisfiable assumptions, 17 mutants (a constant one ulp too small, a missing `cx cy` or `/(1 - u)`, `>=` for `>`, the leaf rule under cancellation, a NaN magnitude that decides) sat with witnesses replayed outside Z3; about 84 s of solver time locally. No rule failed, no library change. A drift check ties the lemmas to `Compiler.cs`, and the generated filters' constants and the gates that run are re-derived from the generated code. Not proved by Z3: the rounding model itself (Higham (2.4)/(2.5); bit-precise binary64 attempts were UNKNOWN or too slow), the monotonicity of rounding (modelled as an axiom), the binary64-to-grid frame, and the structural induction over the expression graph (reviewed; see the README).

#### Later

- Point clouds: normal orientation (Dipole 2021, iPSR 2022, GCNO 2023), screened Poisson reconstruction, fast winding numbers (Barill et al. 2018)
- Conservative marching cubes (size S; noted 2026-11-05, paper only). Source: A. Jacobson, *A Sharp Conservative Offset
  for Marching Cubes*, arXiv 2609.13430, 2026.
  - The result: for a 1-Lipschitz function sampled on a grid of spacing h, the marching-cubes mesh of the level
    sigma > sqrt(3)/2 h contains the true zero-level set, and sqrt(3)/2 h is sharp. It is proven via a convex lift of
    the corner samples, even though the planar faces can lie on either side of the trilinear level set.
  - For us: a `MarchingCubes` option, or a documented recipe, for a guaranteed enclosing mesh of an SDF (collision
    hulls, cages, safety margins, a cheap conservative wrap or bound for alpha wrap and the envelope checks).
  - Preconditions to check: the guarantee holds only for a true 1-Lipschitz f, so our approximate mesh SDFs (narrow
    band, sweeping) need a margin or an exact distance at the samples; and only for the paper's marching-cubes face
    rules (compare with `MarchingCubes` / `MarchingCubesPro`). It gives containment only, not manifoldness or freedom
    from self-intersections.
  - Test plan: procedural SDFs (sphere, box, a rotated torus), all exact zero-level points inside at sigma slightly
    above the bound, plus a known-wrong case below it.
  - Status 2026-11-08: #423 (merged) adds the opt-in `RootfindingModes.LinearEdges` and a Z3 pilot (conditional winding
    enclosure with a `2^-26` level pad). Follow-up (owner decision 2026-11-08, done in #433): restate the FP obligation in a
    relative error model, which drops the preconditions `|f| <= 1` and sample-value gap `>= 2^-20` (a user cannot
    check that gap for SDFs, and the code does not check it).
  - Found in the #423 review (pre-existing, also in legacy modes, low priority): `MarchingCubesPro.GenerateContinuation`
    runs past `done_cells` at the upper grid edge or with seeds outside the bounds (`IndexOutOfRangeException`, in
    parallel an `AggregateException`, or a silent wrap into the next row), because `GridBounds.Contains` is inclusive.
    Also silent: `done_cells` is sized nx*ny*nz, so cell (n, j, k) shares an index with (0, j+1, k) and a whole
    component can be skipped without an exception (repro in the #423 review: bounds 0..2.5, h 1, spheres r 0.3 at
    nodes (4,1,1) and (1,2,1), seeds in cells (3,0,0) and (0,1,0)). Fix: size `done_cells` (n+1)^3 or make the
    neighbour test exclusive; changes legacy behaviour, so note it in UPSTREAM_CHANGES. Own bugfix PR with a test.
    Fixed in #430 (2026-11-08).
- 2D wrap (idea only, owner 2026-11-08; not scheduled). Clipper2 has no alpha wrap, but the Clipper2 addon can
  already do a morphological closing: `Offset` / `OffsetPolyLines` by r + offset, `Union`, `Offset` by -r, then
  `Simplify` (simple, non-self-intersecting polygons; gaps narrower than about 2r are closed). A small step would be
  a documented recipe or a `Clipper2Polygons.Close(...)` helper with tests. A real 2D alpha wrap (after Portaneri et
  al. 2022, cf. CGAL's 2026 2D package as a black box only) only if a use case needs coarse, low-vertex outlines or
  large point sets as input (concave-hull style).
- Feature-preserving offsets (`MeshOffset`, size L). Candidates: Zint et al. 2023, and Cao, Xu, Gao et al., *Robust and
  Feature-Preserving Offset Meshing* (arXiv 2412.15564, 2024, CC BY 4.0; no code link in the paper). Cao et al.:
  - per-vertex offset points by a constrained least-squares solve, several per vertex by dynamic programming where one
    does not fit;
  - a convex hull per vertex, edge and triangle;
  - the exact union of these hulls, classified by exact ray tests and the generalized winding number, then hole filling;
  - result: mitered (sharp-edge) offsets inward and outward, per-triangle distances, about as many triangles as the input.
  It complements alpha wrapping, it does not extend or replace it: it is watertight only for watertight,
  self-intersection-free input (it relies on the inexact winding number otherwise), while `MeshAlphaWrap` guarantees a
  closed manifold for any input but rounds off features. Its bottleneck, the exact union of many convex bodies, is our
  exact Boolean kernel; the EMBER early-out ideas would apply. Before building: a patent check (authors partly at
  Tencent LightSpeed Studios) and a comparison with Zint 2023. Not scheduled (budget; owner 2026-11-05)
- Quad remeshing (Instant Meshes 2015, QuadWild 2021)
- Deformation: ARAP, biharmonic, cage coordinates
- Tetrahedral meshing: see Phase 5 (constrained Delaunay tetrahedrization). fTetWild is MPL-2.0 (file-level
  copyleft), not MIT: implement from the paper (Hu et al. 2020) rather than port
- TPMS / lattice structures from the implicit functions
- Sparse SDF grids (OpenVDB-like), SIMD via `System.Numerics`. For the `Orient3d` filter this was tried: about 2 % in total, not worth a second code path (branch `claude/boolean-simd-spike`, kept as a record)
- Batch ray / point queries against a mesh or BVH (owner idea 2026-11-02): a separate batch API (eg `IntersectBatch(ReadOnlySpan<Ray3d>, ...)`, `IsInsideBatch(points)`), existing API unchanged. Only the floating-point filter of the exact ray-triangle test (Q2/Q3) runs SIMD (`Vector256` / `Vector512`) over the triangles of a BVH leaf or a ray packet; the rare cases inside the bound stay scalar on `Orient3d` + SoS, so results stay exact. No FMA and no cross-lane reduction, so SIMD equals the scalar path bit for bit (tested); netstandard2.0 keeps the scalar path. Prior art: Embree (Apache 2.0; Woop et al. 2013 watertight test over SIMD packets). When: after the Q2/Q3 prefilter, once a real caller needs throughput; upper bound first (the filter's share of the query time). Note the `Orient3d` SIMD spike above gave about 2 %; the difference here is a batch of independent tests per call. Also here: a coarser per-triangle bound (max-norms instead of permanents; new derivation and review) and per-ray reuse of the ray's second point, the two levers left after Q2/Q3 (single-triangle `Intersects` 2.5-4× base with the shared `Orient3d` filter; tree queries +5-30 %, repair total +0.9 %)
- AI bridges as a separate package (ONNX Runtime): project 2D segmentation masks (SAM, DINO) from several views onto face selections, texture reprojection via ExpMap

#### Phase 5: candidates from recent research

Checked against what the library already has (exact booleans, alpha wrap, intrinsic triangulations, BFF / SLIM,
vector heat). Ordered by priority: effort against what it builds on and what it unlocks. Licenses checked at the
source repositories (October 2026); "paper only" means the reference code may not be ported.

**Next research items (owner 2026-11-03), only if the effort is really small:** (1) #7 foldover-free maps
(Garanzha et al. 2021; on `SLIM` and `UVOverlap`; also a lead for the open P1 collapse), (2) the affine heat method
(Soliman & Sharp 2025, port from geometry-central, MIT; an upgrade of the `VectorHeatMethod` log map), (3) generalized
winding numbers for rational curves (Spainhour et al. 2024, Axom, BSD). Each starts with an effort estimate and a §2
prior-art check; one that is not small (about one PR in one short session) goes back to the owner instead of being
started. The owner runs them locally (2026-11-03); the coordinator reviews and merges the PRs. Status 2026-11-04: effort gates in #364 (item 1 stopped at the gate, item 3 only as the reduced method; its one internal caller would be the loop nesting of `PlanarComplex.FindSolidRegions`, which today decides it on sampled polygons with an absolute default tolerance of 0.1, so near-touching curve loops and scale may change the result; probe that first (two close concentric circles over `Similarity.All()`) when item 3 is picked up, later per owner 2026-11-04); item 2 built and stopped at its accuracy gate. Both affine variants are less accurate than the existing log map on the sphere and cylinder, geometry-central's own implementation (potpourri3d) included. The code is not merged; the findings are in [AFFINE_LOGMAP_GATE.md](../design/AFFINE_LOGMAP_GATE.md) (#371, closed). #6 (walk on stars, Zombie) stays later:
no caller yet, Neumann boundaries need a new closest-silhouette query, and Monte Carlo results conflict with the
bit-for-bit determinism and invariance tests.

| # | Feature | Research / port source | License of reference code | Why / builds on |
|---|---|---|---|---|
| 1 | Geodesic paths, loops and Bézier curves on surfaces (FlipOut). **Done:** paths, waypoints, loops (`MeshGeodesicPath`); Bézier curves open | Sharp & Crane 2020 *You Can Find Geodesic Paths in Triangle Meshes by Just Flipping Edges*; geometry-central | MIT | Almost entirely on `IntrinsicTriangulation`; cut lines (orthotics), UV seams, curves on surfaces |
| 2 | Polyhedral envelope containment. **Prototype:** `MeshEnvelope` (exact, tested), but not used by the `Reducer`: its exact path clips the query against every prism it spans, about 100 times slower than `TriangleDeviationBounds` once reduced triangles span many prisms (`benchmarks/ReducerBenchmark`). Open: the paper's algorithm (filtered indirect predicates on critical points, no clipping) | Wang, Schneider, Hu, Attene, Panozzo 2020 *Exact and Efficient Polyhedral Envelope Containment Check*; `fast-envelope` | MIT | The `MaxDeviation` check takes 92-97 % of the reduction time (`benchmarks/ReducerBenchmark`), so a fast exact check would pay off. Alternative: speed up `TriangleDeviationBounds` itself |
| 3 | ~~Robust 2D polygon booleans and offsets: port Clipper2~~ Not planned: Clipper2 (Angus Johnson, Boost-1.0) has an official C# version on NuGet; use it directly (at most a thin adapter to `GeneralPolygon2d`). A port would only duplicate it. Straight skeleton / 2D medial axis from papers if needed (CGAL is GPL) | Clipper2 | Boost-1.0 | |
| 4 | Constrained Delaunay tetrahedrization, then tetrahedral meshing | Diazzi, Panozzo, Vaxman, Attene 2023 *Constrained Delaunay Tetrahedrization: A Robust and Practical Approach*; Hu et al. 2020 *Fast Tetrahedral Meshing in the Wild* | CDT: GPL / LGPL, paper only; fTetWild: MPL-2.0, paper only | On `Delaunay3` and the implicit points of `ExactMeshArrangement` |
| 5 | **Next large item (owner 2026-11-02).** Upper bound and literature in [design/INSIDE_OUTSIDE_OPEN_INPUTS.md](../design/INSIDE_OUTSIDE_OPEN_INPUTS.md) (#325): about 100 of 111 fallbacks on 798 meshes hinge on inside / outside, the fallbacks take 54 % of the repair time; **spike result (#331): stop.** Resolving open inputs by GWN labels makes 3 of 111 fallbacks exact (0 regressions); 62 stay open at the structural crack / `ConflictEdges` limit (no labelling closes them), 32 differ from the pointwise GWN reference of the volume check; 12.7 % of 112440 patches are within 0.05 of a half-integer. A certified boundary term would not change this. The wrap fallback stays the answer for these inputs; literature (FEWN, Antipodal, Feng & Crane 2024, Feng's thesis) recorded in the note. Generalized signed distance and winding numbers for open / broken inputs | Feng & Crane 2024 *A Heat Method for Generalized Signed Distance*; Feng, Gillespie, Crane 2023 *Winding Numbers on Discrete Surfaces* | paper | Complements `MeshGeodesicDistance`, `VectorHeatMethod` and the repair stages |
| 6 | Grid-free PDE solving (Monte Carlo geometry processing) | Sawhney & Crane 2020 *Monte Carlo Geometry Processing* (walk on spheres); Sawhney et al. 2023 *Walk on Stars*; Zombie | MIT | Needs only closest-point and ray queries (`DMeshAABBTree`, winding numbers); works on dirty geometry |
| 7 | Foldover-free maps: fixed-boundary local untangling | Garanzha et al. 2021 *Foldover-free maps in 50 lines of code* | paper | **Merged in #374 (2026-11-04):** `FoldoverFreeParameterization`, no default atlas hook; independent review fix: the final flip-free iterate is returned (Converged true on all fixtures, annulus symmetric Dirichlet 686.97 -> 7.46), the epsilon schedule checked against equation 6. Open: the paper's energy-decrease stop instead of epsilon <= 1e-6 (about 131 iterations even from a valid start). [Measured acceptance](../../benchmarks/ParameterizationBenchmark/FOLDOVER_FREE.md): folded-disc gate passed; SLIM quality target not universally met; torus global-overlap fallback remains. Local flip freedom does not close the global-injectivity gap in Phase 3 |
| 8 | Continuous collision detection | Wang et al. 2021 *A Large Scale Benchmark and an Inclusion-Based Algorithm for Continuous Collision Detection* (Tight-Inclusion); Li et al. 2020 *IPC* | Tight-Inclusion: MIT | Conservative, provably correct CCD; Unity, robotics |
| 9 | Simplification with intrinsic error metrics | Liu et al. 2023 *Surface Simplification using Intrinsic Error Metrics* | paper | On the intrinsic triangulation infrastructure |
| 10 | Mesh from coarse / sparse SDFs | Sellán et al. 2023 *Reach for the Spheres*; 2024 *Reach for the Arcs* | paper | With the sparse SDF grids and TPMS items under Later |

#### Open research questions

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
  **Z3 as a rule checker (owner 2026-11-08, step 3 of the Z3 pilot, only once someone takes up the empty-ball lead):**
  (a) the lemma that a lower distance bound computed in doubles really is a lower bound (the rounding of every step,
  as in pilot 2); (b) the lemma "the ball contains the whole moved primitive plus the motion of both sides and no
  other primitive ⇒ no new intersection"; (c) for every candidate local rounding rule, a bounded counterexample
  search on tiny configurations (2-3 triangles, small grid) for a new intersection or degenerate triangle; each
  counterexample becomes a test. Not for Z3: the global guarantee over whole meshes (induction over cascades of moves,
  termination) and the running-time question.
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
