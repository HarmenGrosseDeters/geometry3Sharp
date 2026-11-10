# Terrain

Part of the [roadmap](../ROADMAP.md).

- **Scope:** slope construction and daylighting on TINs (open-pit mining): slope surface, overlay with the terrain,
  catch line, volumes, failure kinds, accuracy reporting, LocalFrame, RoundToGrid; the 2D quadtree it waits for;
  honest terrain metrics. Terrain / GIS is a showcase domain (Phase 4).
- **Status (2026-11-10):** the terrain topic is closed out (owner 2026-11-09: finish the terrain topic). Done: slope construction
  phase 1 (#357), the DGM reader and writer, the grid TIN with a guaranteed surface error and breaklines (`gs.TerrainGridTin`,
  #491 with the gaps closed in #504: survey heights, world-coordinate margin), the `g3cli` commands `dgm`, `dgm_tin` (#506) and
  `cell_volumes`, volume per cell (`gs.TerrainCellVolumes`, #507) and the terrain kernel mutation score (#508). Everything below
  "Parked" waits for a concrete use case; nothing is planned.
- **Related:** [design/TERRAIN.md](../design/TERRAIN.md) (§11 holds the follow-ups),
  [TERRAIN_1A_PLAN.md](../design/TERRAIN_1A_PLAN.md), [TERRAIN_1B_PLAN.md](../design/TERRAIN_1B_PLAN.md),
  [TERRAIN_1C_PLAN.md](../design/TERRAIN_1C_PLAN.md), [TERRAIN_ROUND_TO_GRID.md](../design/TERRAIN_ROUND_TO_GRID.md),
  [TERRAIN_DISTANCE_METRICS.md](../design/TERRAIN_DISTANCE_METRICS.md),
  [QUADTREE2D_SPEC.md](../design/QUADTREE2D_SPEC.md).

### Open

- **Slope-map performance (2026-11-10, closed):** profiled for the slow-test triage (#514). Bit-identical cleanups in
  #517 (~1.1x). Measured and dropped: merging equal-label faces during insertion (not bit-identical in near-degenerate
  designs, net 1.4x), a divide-and-conquer lower envelope (exact, hash-identical on all benchmarks, but only <= 1.15x:
  branch claude/terrain-envelope-dc with its design note TERRAIN_SLOPE_ENVELOPE_DC.md), and an angular fan max-diagram
  (upper bound 2x, not safely reachable: fans are not exact pyramids). The time follows the per-piece max-diagrams of
  ~270 planes (~100 ms per piece), linear in the number of pieces. The slow terrain tests are handled as test design /
  Nightly. A real gain would need fewer planes per fan, which changes the accuracy budget (owner: not wanted).

Nothing is open. The items below are **parked until a concrete use case** (owner 2026-11-09), in the order of the earlier plan.

- **Ramps as design geometry** (path, width, grade; access ramps into pits).
- **Lofting between contours** under the exact slope oracle: moved to the general `ContourLoft` operation,
  [research.md](research.md#open) priority 9 (owner 2026-11-09); terrain is one application, not a terrain item.
- **Proven tan interval and catch-line bound** (TERRAIN.md §5.5; today an exact certificate per result and a reported accuracy).
- **Sector polygons, exterior distance on demand.**
- **Berms and inter-ramp checks** (TERRAIN.md §10; the stepped surface is reachable with `TargetHeights` today).
- **Performance follow-ups, only when a real case is slow** (§11): the octagon 1 m replacement 6.1 → 8.8 s after D (a second
  realization), the combinatorial zeros of C, the band-only overlay, the rest of a failed realization (about 25-30 %), the extra
  realization on the L shape and the wavy 48-gon after (b). A positions-only copy for LocalFrame was looked at and dropped: the
  in-place translation with a bitwise restore (#355) costs no copy, and only a caller that shares the terrain concurrently
  would gain.
- **Slope envelope construction (slow tests, class F; parked, needs an owner decision).** The 48-gon designs take 3-5 s
  (`WavyToe_130mHigh` 3.6 s; `TerrainBenchmark slope toe=wavy n=48 r=150 h=130 ratio=1.5 tol=0.05`, 85-90 % in `SlopeMap.InsertPiece`).
  Profile (2026-11-10): the incremental map builds about 134k faces (1.6M `split_face` calls) for 48 pieces and simplifies them to 762;
  the order of insertion changes the time 2.5x (id order 8.9 s, fixed shuffle 3.4 s) but never the result. Constant factors are done
  (about 1.1x). Merging equal-label neighbours during insertion shrinks the live faces 20x but is not bit-identical in near-degenerate
  designs (`Merge_RaisesTheToeLowering`: 80 instead of 81 triangles, `MergedPositions` 0 instead of 3): the ghost vertices of planes that
  later lost are seen by Simplify's close-position merge, so the output is not a pure function of the labels there; and it gained only
  1.05-1.4x because the split count, not the face count, drives the time. The lever left is a different construction, for example the
  lower envelope of a fan per sector, or divide and conquer over the pieces. It changes the vertex history and so the near-degenerate
  outputs: the owner decides whether that is acceptable (or whether the near-degenerate cases get a canonical rule first).
- **RoundToGrid:** parked (owner decision 2026-11-03, R1: docs and measurement tools only, face 2 stays pinned). The local
  re-triangulation of the clipped region under the slope constraint is an open research item
  ([research.md](research.md#open-research-questions), snap rounding).
- **GeoTIFF reader:** the owner decides (own implementation, or LibTiff.Net, BSD, in an optional package); XYZ ASCII and ESRI
  ASCII Grid are in.
- **3D blocks** (volume per block with a z-extent, TERRAIN.md §9.3): the 2D columns are done, the z-extent waits for a use case.
- **Biomedical / orthotics** showcase: no work recorded.

### Closed in the close-out (2026-11-09 / 10)

- **DGM to TIN:** `gs.TerrainGridTin`, B2 of [design/TERRAIN_DGM_TO_TIN.md](../design/TERRAIN_DGM_TO_TIN.md) (§11, §11.1 for the gaps).
- **DGM reader:** `HeightGrid`, `HeightGridReader`, `HeightGridWriter`, `HeightGrid.BuildMesh` (`io/`); CLI / MCP `dgm`, `dgm_tin`.
- **Volume per cell:** `gs.TerrainCellVolumes`, CLI / MCP `cell_volumes` (TERRAIN.md §9.3).
- **Terrain mutation shards:** `terrain-kernel` 83.64 → 87.36 % (threshold 85); the shards were waiting for the restart (#302).
- **Honest terrain metrics** (task 5, C2(A), benchmark only): done in #399 (2026-11-08), see [below](#task-5-honest-terrain-metrics).

### Done / history

#### Phase 4 items: showcase domain, slope construction, 2D spatial index

- A showcase domain: terrain / GIS (in progress) and biomedical / orthotics
  - [ ] Slope construction and daylighting on TINs (open-pit mining; design in
    [design/TERRAIN.md](../design/TERRAIN.md)): per-segment slopes, never steeper than specified (verified per
    triangle), catch line with the terrain, cut / fill volumes. Phase 1 in three PRs, each usable and tested on its
    own: **1a** the slope surface from the cone envelope plus the "never steeper" verification oracle (usable on its
    own to check existing results); **1b** the overlay with the terrain and the connected catch line; **1c**
    volumes, failure kinds, accuracy reporting, km-scale benchmarks. Berms after phase 1. Priority: before the
    planned remesher work; starts after the `ExactMeshBoolean` work that is being scoped now.
  - [x] 2D spatial index: a loose quadtree over AABBs (TIN triangles, edges, points) with insert / remove, height
    queries and nearest-neighbour queries, complementing the existing hash grids. Clean-room from the behavioural
    spec [design/QUADTREE2D_SPEC.md](../design/QUADTREE2D_SPEC.md) (owner decisions in its §7) and the literature only;
    targets 1M triangles built in about 100-260 ms, height query about 2.5 µs, on net48 and net8. Comes after the
    `ExactMeshBoolean` work and before terrain phase 1; may speed up the height-field check of
    `TerrainVolumeDifference`. Implemented, on `quadtree-takeover` (#195): see the record in the spec. The side-by-side comparison on the owner's machine is done (2026-11-08): always faster than or as fast as the old quadtree.

#### Stage 5: quadtree

`quadtree` (#195): independent review, owner-machine performance and memory, side-by-side with the old quadtree. The owner's local optimizations merged in #266 (2026-11-01). **Done 2026-11-08:** the owner's side-by-side
comparison with the old quadtree: the new one is always faster or as fast.

Tier: L. Parallel: owner, local

#### Terrain session record (2026-11-01 to 2026-11-03)

**Terrain 1b** merged in #285, **1c part 1** in #297 (2026-11-02: one build per margin, 1a realization defects at large heights, Hambach DGM1 benchmark; octagon 1 m 43.6 → 17.8 s, 3.1 → 1.5 GB).

Next, #307: `CompositeKind` (owner: region-only output with mapping, plus the whole changed terrain as an option; default Full), band-only overlay, ideas B–D; target one edge on the 1 m grid < 1 s (0.30 s warm with Replacement in the draft).

Then (owner 2026-11-02, functional completeness first): the functional 1c items (volumes 5.7, StatusKind 5.6, catch-line accuracy reporting 5.5); then D, randomized order with a canonical tie rule and a canonical simplification (measured ≈ 2.3× on the wavy case); then mixed cut and fill, target heights, docs; then the combinatorial zeros of C and the band-only overlay.

**Pause (owner 2026-11-03):** after the (b) tolerance-budget PR and `RoundToGrid`, terrain pauses; a large package is complete. Before the pause, the docs (TERRAIN.md, OPERATIONS.md, CLI help) match what is merged, and the open terrain items are recorded here; the terrain mutation shards (#302) wait for the restart. The order below resumes after the pause, first with a DGM reader in the CLI (owner 2026-11-03: after the pause).

After phase 1, order of the later steps (owner 2026-11-02, replaces TERRAIN.md §11 step 8's order): (1) docs and CLI / MCP for terrain (finishes phase 1); (2) volume per cell, 2D columns first (§9.3); (3) ramps as design geometry (path, width, grade; access ramps into pits are modelled in practice); (4) lofting between contours with triangles chosen under the exact slope oracle and a status when no compliant triangulation exists (§9.2; two contours first, then branching); (5) performance follow-ups when a real case is slow (the octagon's second realization, C zeros, band-only overlay); (6) proven tan interval and catch-line bound; (7) sector polygons, exterior distance on demand; (8) berms and inter-ramp checks parked until a use case (not modelled in practice).

**Merged 2026-11-02:** #307 (`CompositeKind` Replacement, one toe edge on the 1 m grid 0.17 s catch line / 0.26 s replacement warm, octagon 1 m replacement 24.4 → 5.9 s), #314 (functional 1c: StatusKind, volumes, catch-line accuracy).

D merged in #318 (order-independent bit for bit; A/B: wavy 48-gon replacement 27.4 → 8.8 s, 1a case 7.3 → 3.6–4.0 s; octagon 1 m replacement 6.1 → 8.8 s slower, a second realization, follow-up); mixed cut/fill and target heights merged in #320 (2026-11-02; owner 2026-11-02 accepted the Auto approach: two independent runs on the terrain, joint triangulation of the triangles both replace, the catch points of both snapped; ToeOvertopped; CutVolume / FillVolume / CutArea / FillArea; target heights as the SlopeRegion contour at z = h), its first commit fixes a pre-existing CompositeFailed overlap at toe crossings.

Corner fix (exact `Orient2d`) merged in #329 (2026-11-02); CompositeFailed / NoDaylight fix at the terrain boundary merged in #338 (2026-11-03).

LocalFrame: A/B done, translating back fails at UTM in every case and gains no exact-stage rate; owner decision 2026-11-03: option 1, LocalFrame returns local outputs plus UsedOrigin, no translation back (the caller translates without guarantee); after the composite subnormal bugfix.

Then (owner 2026-11-03) an optional output step `RoundToGrid(origin, resolution)`: iterated snap rounding to the target grid (Hobby 1999; Halperin & Packer 2002, half-pixel vertex-edge clearance; Hershberger 2008 stable), then the exact slope/support checks re-run on the rounded coordinates with a status, so features below the grid merge in a controlled way instead of failing silently; prior art in GIS: a fixed precision model with a snap-rounding noder; building blocks `SnapRounding2` and the fixed-width exact points; first an upper bound: how often rounding to 1 mm at UTM breaks a slope check.

The band repair [64·e_xy, MinFeatureSize) of TERRAIN.md 5.4 merged in #350 (2026-11-03: a targeted edge collapse of needles at near-quadruple points, funded from the facet slack, replayed on later realizations; the #339 repro now succeeds at the origin and at UTM).

Composite subnormal bugfix merged in #352 (the step from 0 is the ulp of the coordinate scale; restores the bitwise composite comparison).

LocalFrame option 1 built; its A/B found a near-origin SlopeViolation on the Hambach wavy case: two gaps in the #350 band merge (no chains within a pass at near-quintuple points; nested plane sets in [r, rband)), merged in #354 (2026-11-03: iterate, cumulative per-vertex move cap ≤ rband, nested pairs; amp-30 waves near the origin 3 attempts 14.6 s → 1 attempt 5.6 s; the DGM case with LocalFrame succeeds in 2 attempts).

LocalFrame merged in #355 (2026-11-03: translate in place with a bitwise restore via VerticesBuffer, so caller timestamps and AABB trees stay valid; −0.0 terrains fall back; a positions-only copy is a §11 follow-up).

Clipped-region item merged in #356 (docs plus the face 2 pin; the early region check skips the terrain copy with identical decisions; every hash equal; octagon UTM −10 %, LocalFrame −4 to −14 %; the rest of a failed realization, about 25-30 %, is on §11).

**Phase 1 done**: #357 merged (2026-11-03; g3cli slope / MCP with GeoJSON toes, OPERATIONS.md example, SOURCES.md).

Auto toe-on-terrain bugfix merged in #359 (joint() parity by an order-preserving XOR merge of both runs' edge nodes; 56 of 960 Auto designs from CompositeFailed to Success).

(a) T-junctions merged in #361 (a point exactly inside an edge splits the copied neighbour or joins the replaced neighbour's part input; AssertComposite checks T-junctions and vertical cracks; 30 tests failed on master; outputs bitwise identical elsewhere).

Next (b): the composite's catch-point snap (moves up to MinFeatureSize) is not in the tolerance budget; at UTM the guarantee is unproven (certificate bounds 1.04-1.16 × Tol on the DGM cases, no measured excess so far), in the frame rotated 1e-3 utm Successes are wrong by up to 72 × Tol; fix: a snap share in the budget plus an exact measured certificate (δ raise, then NotCertified), choice between a reserved share and the certificate alone by cost, then RoundToGrid.

(b) merged in #363 (2026-11-03): a reserved Tolerance/4 plus the exact certificate over the moved and swept triangles (δ raise, then NotCertified as a SlopeViolation kind); 2880 sweep 150 → 0 wrong Successes, every frame but rotated 1e-3 utm stays Success (there CompositeFailed honestly, LocalFrame succeeds); certificate cost 1.3-2.3 %, 4.5 % on the edge case; the extra realization on the L shape and the wavy 48-gon is on §11.

RoundToGrid upper bound (2026-11-03): with the grid as the build's resolution the budget forces MinFeatureSize of 20-52 m at 1 mm (infeasible); rounding after the build fails 15-53 % (steeper) and 27-32 % (unsupported) of the region triangles at 1 / 0.1 mm; the unsupported ones need a λ floor of about 1.5 mm, the steeper ones a local re-triangulation of the clipped region on the grid under the slope constraint (rounding with constraints, open: Milenkovic; Fortune 1999); owner decision 2026-11-03: R1, RoundToGrid parked (docs and measurement tools only, face 2 stays pinned); the local re-triangulation under the slope constraint is an open research item (see the snap-rounding question under Open research questions). Terrain pauses after that PR and a handover note.

**Terrain 1a** (large) was started on 2026-11-01 in the cloud, ahead of this slot, at the owner's request, with the NoData guard of `TerrainSafetyLines` (a single vertex such as z = −9999 near a source refines until memory runs out; count the band's 3D area, not only the horizontal one).

#### Task 5: honest terrain metrics

Plan fact-checked on 2026-10-30 (see [platform.md](platform.md#plans-fact-checked-2026-10-30)):

- [x] **5. Honest terrain metrics (C2(A)):** done in #399 (2026-11-08: directed closest-point distances with horizontal / vertical components in TerrainBenchmark). [benchmark-only plan](../design/TERRAIN_DISTANCE_METRICS.md). Add shortest
  3D distance to the reference TIN and the horizontal/vertical components of the same closest-point displacement
  for `tdc` and `gtin`; retain vertical-ray metrics, report invalid/missing samples and measure overhead. Do not
  change terrain algorithms or the "never steeper" rule.
