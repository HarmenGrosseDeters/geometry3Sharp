# Design note: a canonical slope envelope, independent of the construction

Status: **stage 1 done, stage 2 (b) done, (c) not started (2026-11-24); the owner's decision (b) of 2026-11-24.** Branch `terrain/slope-envelope-canonical`.
Context: [roadmap/terrain.md](../roadmap/terrain.md) ("Slope envelope construction"), [TERRAIN_1C_PLAN.md](TERRAIN_1C_PLAN.md)
section 9 (randomized insertion order with a canonical result), the earlier prototype
[`claude/terrain-envelope-dc`](https://github.com/HarmenGrosseDeters/geometry3Sharp/tree/claude/terrain-envelope-dc)
(`TERRAIN_SLOPE_ENVELOPE_DC.md` there, not merged).

## Goal

Make the slope surface a pure function of the input, through the labelled envelope only. Then a faster construction (merging
during insertion, divide and conquer, an envelope per sector) can replace the incremental `SlopeMap.InsertPiece` without
changing any output.

## 1. Where the history could leak, and what the code does today

The output is built in three steps. Each step was checked for dependence on the construction (vertex and line ids,
face ids, the order of splits, cached state):

1. **Labels** (`InsertPiece`, `insert_into`). Each point gets the lexicographic minimum of (height, plane id) over the
   pieces; inside a piece, the maximum with the lowest id among equal planes (TERRAIN_1C_PLAN section 9). The decisions are
   exact (`Side`, `Crossing`). The prefilters (`fBox` and `prefilter_skips`) and the fan sector choice (`add_sector`) use
   approximations, but they are conservative: a correct skip changes nothing. They depend on the face shapes, so on the
   history. **A non-conservative prefilter is the one way the history reaches the labels.** That is what the
   2026-11-10 observation was (below).
2. **Partition** (`Simplify`). The faces become the components of one label (connected across non-barrier edges), cut by
   one ray from each reflex corner up to the component's boundary. Rays do not stop at other rays, so their order does
   not matter. Then every degree-2 vertex between collinear edges of one barrier (or none) is removed. Ids appear only
   as shortcuts that agree with an exact predicate: `on_one_line` (the same kernel line) implies `Orient == 0`, and
   `reflex` and `removable` decide the same either way. The ray list and `merge_part` take faces in id order, but the
   result is a union of full rays and does not depend on it. `OrderedFaces` sorts by the exact `CompareXY` of the
   corners; label, then id, break ties, and two distinct faces never tie. So the partition is a function of the labels,
   the barriers and the box.
3. **Realization** (`Realize`, `merge_close_positions`, `append_faces`, then `CollapseShortEdges`, `BandMerge`,
   `FlipNeedles` on the mesh). The positions are `K.Rounded`, a function of the exact point (not of the lines that define
   it). `labelsAt` is filled in `OrderedFaces` order. The close-position merge sorts all positions in a total order
   (barrier, label count, x, y). The mesh's vertex and triangle ids follow `OrderedFaces`. The mesh repairs work on that
   mesh only.

   **One latent leak:** `triangulate` (the quality DP) and `quality` (the ear fallback) read `K.Approx`.
   `ExactSlopeKernel.Approx` returns the interval midpoint of the vertex's defining crossing, unless the vertex was
   rounded before, and then the rounded position. So it depends on which two lines define the vertex (the construction)
   and on the call history. Today this is masked: `Realize` calls `Rounded` on every corner (for `labelsAt`) before it
   triangulates. Take that call away, or give one point two different defining lines, and the triangulation of a
   near-symmetric face (a regular polygon, where qualities nearly tie) could change.

**The 2026-11-10 observation was not a vertex-history leak.** The prototype found `Merge_RaisesTheToeLowering`
at 80 triangles and 0 merged positions, against 81 and 3 on the incremental path. The reason was the prefilter bug
#805, fixed in #806 (2026-11-13): faces smaller than the error of the approximate corners were skipped where the piece
is lower, at apexes where many planes meet. The incremental map was not the exact envelope there, and its wrong
labels were the "cluster" that the close-position merge saw. #806 folded the test into
`RegularPolygons_NoMergeAfterPrefilterFix_InEveryFrame`. On master, both polygons give 80 triangles and 0 merges with
every construction (below). The roadmap entry predates #806.

## 2. The canonical rule

Define the output as a function of the **labelled envelope**: the map from each point of the domain to its label
(lexicographic minimum of (height, plane id) over the pieces, the cap included), plus the barriers and the box. The
rules:

- **R1 (labels).** Any construction must produce, on every open face, exactly the labels of the exact envelope. A filter
  may skip work only if the skipped work cannot change a label (conservative, with the corners' error bound counted, as
  `prefilter_skips` after #806). This is the contract a new construction must meet. It is also the one place where a bug
  shows up as an "output change".
- **R2 (partition).** `Simplify` as in TERRAIN_1C_PLAN section 9: components, reflex rays to the boundary, removal of
  straight degree-2 vertices. Unchanged.
- **R3 (realization reads exact positions only).** Every geometric decision after `Simplify` uses the exact kernel
  (`Orient`, `CompareXY`) or `K.Rounded`, never `K.Approx`. **New:** `triangulate` and `quality` use `Rounded` (cached in
  the vertex after the first call). This changes no output today, because the corners are already rounded there. It
  makes the property hold by construction, not through the order of calls.

Argument: under R1 the labels are equal as functions. Under R2 the partition is a function of the labels, the barriers
and the box (section 1, step 2). Under R3 the mesh is a function of the partition's exact corners, in canonical order.
The repairs after `Realize` are functions of that mesh. So the output depends on the input only. Construction-specific
data (vertex, line and face ids, `Approx` midpoints, split order, prefilter decisions) does not reach it. Near-degenerate
designs (clusters of positions within 64 ulps) go through the same function. They merge the same way because they see
the same position set.

**Literature.** The labelled envelope is the minimization diagram of the piecewise-linear functions F_k (lower envelopes:
Sharir and Agarwal 1995; Edelsbrunner, Guibas and Sharir 1989). It is unique as a labelling, but its convex
decomposition is not. Making the decomposition canonical is the reflex-ray decomposition of TERRAIN_1C_PLAN section 9
(Keil's survey, cited there). Divide and conquer through the overlay of two envelopes is Edelsbrunner, Guibas and Sharir
1989 (as in the prototype note). No new sources. Library building blocks: `SlopeMap.Simplify` /
`OrderedFaces` (canonical partition), `ExactSlopeKernel.Rounded` / `CompareXY` (history-free positions and order), the
prototype's `MergeMin` / `Compact` (on `claude/terrain-envelope-dc`). No convex hull or envelope code elsewhere in
`mesh_ops` fits (the cones are max-of-planes per piece, not planes).

## 3. Measurements (master + this branch, net10.0 Release, this container)

A construction switch for tests: `TerrainSlopeDesign.CompactDuringInsert` calls the new `SlopeMap.Compact` after each
piece. It merges neighbouring faces of one label across non-barrier edges while the union stays convex, by exact
`Orient`. The prototype used an approximate turn test. A hook `AfterSimplify` gives the test the map, and
`SlopeMap.PartitionText` gives the partition per face (label and rounded corners). Each construction is compared with
the default on the map after `Simplify` (every build) and on the slope surface (`Invariance.Difference`, bit for bit):

| case | merged positions | triangles | merge during insert | reversed order |
|---|---|---|---|---|
| 19 stress cases (`TerrainSlopeOneBuildTests.Cases`) | 0 | 10-236 | identical (map and surface) | (nightly test, identical) |
| `WavyOnWaves` n 40 r 100 (band-merge reference) | **7**, 1 band merge | 420 | identical | identical |
| `WavyHambach(130)` (the benchmark design) | 0 | 476 | identical | identical |
| old pin n 16 (`Merge_RaisesTheToeLowering`) | 0 (was 3 before #806) | 80 (was 81) | identical | identical |
| old pin n 22 | 0 | 66 | identical | identical |
| 600 random regular polygons (n 3-24, a quarter at UTM) | 0 in all | - | - | - |

**Changed pins under R3: none.** With R3, all 574 `TerrainSlope*` tests outside Nightly pass, Slow ones included
(2 min 46 s). R3 only replaces `Approx` by `Rounded` where both are already the rounded value.

**Cost of the rule:** none at runtime (`Rounded` is cached and is computed for these corners anyway).

**Cost of the naive merge during insertion** (the first, global version of `Compact`; `WavyHambach(130)`, phase `insert`): 3.9 s plain against 9.3 s with
`Compact` after every piece. `Compact` scans every face ever made, 48 times, and the larger faces pass the prefilter more
often. The roadmap's finer variant gained 1.05-1.4x. So merging alone is not the lever, as the profile says: the splits
cost, not the faces.

## 4. Stage 2: the faster construction (coordinator's OK 2026-11-24)

Under R1 every construction is checked the same way: the pure-function test (section 5) must pass, bit for bit.

**(a) Upper bound.** Every piece inserted again into the final simplified map (`TerrainSlopeDesign.ReinsertAfterSimplify`,
a test switch). This is about the least work any incremental construction can do. The labels do not change
(`Reinsert_IntoTheFinalMap_ChangesNothing`).

| design | insert | split calls | faces made | again into the final map |
|---|---|---|---|---|
| `WavyHambach(130)` | 4.0 s | 1,588,211 | 139,612 | 0.12 s, 1,933 splits |
| `WavyOnWaves` n 40 | 1.9 s | 1,003,032 | 97,287 | 0.17 s, 1,799 splits |

Counted by call site: 99.6 % of the split calls are in the maximum of the piece's planes over W (`insert_into`, each part
against each plane), and 92 % of those do not cut. On average 222k `insert_into` calls take 5.6 planes each. The naive
merge during insertion made it worse (2.5M splits), because the merged faces are larger: 18.6 planes per call, and
every part is tested against every plane.

**(b) Built: merge during insertion plus planes per part.**

- **Per part** (`SlopeMap.PartSectors`): each part of W takes only the fixed planes and the fan planes of its own sector
  (`add_sector` on the part's corners). The argument is the same as for the face: a part lies in the face, so its
  window lies in the face's. A fan plane outside the window is below one inside it there, so the maximum and every label
  are unchanged; only splits by planes that lose at once are left out. A part's set is computed when the part is first
  tested and dropped when it is cut.
- **Merge during insertion** (`SlopeMap.Compact`, `TerrainSlopeDesign.CompactDuringInsert`): only the faces that the last
  piece labelled, merged with neighbours of the same label across non-barrier edges where the union stays convex
  (exact `Orient`).
- Both are internal and on by default. Both off is the earlier construction, which the tests compare against.

In-test counts (`WavyHambach(130)`), from about 4.0 s and 1.59M splits:

| construction | insert | split calls |
|---|---|---|
| per part alone | 2.9-3.6 s | 672k |
| per part and local merge | 2.5 s | 181k |

`TerrainBenchmark slope toe=wavy n=48 r=150 h=130 ratio=1.5 tol=0.05 repeat=3`, two rounds each, master and the branch
alternating on one machine. `hash_surface` is `22afefbc69823bf8` in all runs, 477 triangles, 2 merged positions:

| | master | branch |
|---|---|---|
| phase insert | 2.84 / 2.74 s | 2.46 / 2.30 s |
| slope_s (last of 3) | 3.02 / 2.90 s | 2.61 / 2.60 s |
| predicates | 17,272,841 | 12,325,667 |
| exact constructions | 65,034 | 44,918 |
| peak MB | 545 / 553 | 470 / 442 |

So (b) gains about 1.15x on the benchmark and 1.3-1.6x in the tests, under 2x. Most of the remaining time is not in the
splits (181k) but in the per-face work of the insertion: the virtual clip of W, which constructs crossings, and the
prefilters, all over the faces each piece reaches.

**(c) Divide and conquer: not started.** The condition for it holds: (a) shows room, and (b) gains under 2x. It did not
fit the session's budget. The approach is the prototype's `MergeMin` (carving the faces of one envelope into the other,
`claude/terrain-envelope-dc`) with an exact interior-disjoint test and no approximate skip, then `Compact` after each
merge. The pure-function test is its gate. A sector-local envelope is a variant of it (the leaves are the pieces of one
sector).

## 5. Tests

`TerrainSlopeEnvelopeCanonicalTests`. The PR tier, durations in the full terrain run:

- `PureFunction_ConstructionAndOrder_SameMapAndSurface` (2.0 s): five cheap cases (regular 10-gon and 16-gon, an open toe,
  a fill, a straight ramp for `InsertPolygonPiece`). The default is compared with the earlier construction, the earlier
  construction reversed, and a seeded order. Checked: the map after every `Simplify` (`PartitionText`: label and rounded
  corners per face, in `OrderedFaces`), status, builds, toe lowering, merges, rays, and the surface bit for bit with its
  triangle groups.
- `Reuse_SameMapAndSurface` (0.4 s): `Compute()` twice on one instance.
- `Invariance_EveryFrame_SameAsTheEarlierConstruction` (a regular 22-gon, up to 3.4 s; an open toe with lateral limits,
  0.2 s): over `Similarity.All()`, rotated about z. The exact frames map back to the identity bit for bit; the others equal the earlier construction.
  There is no parallel path, so no determinism test.
- `Reinsert_IntoTheFinalMap_ChangesNothing` (0.3 s): re-inserting every piece changes no label, with far fewer splits.
- `Default_GivesTheReferenceLabels` (0.5 s): R1 directly. The default gives the same map and surface as the reference
  insertion (`SlopeMap.NoPrefilters`: no prefilter, every fan plane), on the strip-piece cases (section 7 for ramps).
- `Barriers_HaveTheDomainOnOneSideOnly` (0.4 s): no barrier has domain faces on both sides, at any stage. This is the premise
  that makes the merge's barrier checks redundant.

Nightly, because the run is the check (the reason is in each test): the 19 stress cases under the earlier construction
(about 10 s); 600 random regular polygons (55 s); `WavyOnWaves` n 40 (7 merged positions, 8 s per build), the benchmark
design and the old pins, each under the earlier construction and the reversed order (40 s). No smaller design that
merges positions is known.

The other existing `TerrainSlope*` tests outside Nightly (578 with these) pass with the new defaults. Among them are
`InsertionOrder_Reduced_DoesNotChangeTheResult` and `RegularPolygons_NoMergeAfterPrefilterFix_InEveryFrame`.

Hand mutations (the PR-tier tests of the class):

| mutant | result |
|---|---|
| `try_merge` without the convexity check at the first end of the run | killed (all four) |
| per part without the fan sectors (fixed planes only) | killed (all four) |
| `Compact` without the barrier check on `h`, and also without the one in `try_merge` | survives, **equivalent**: a barrier never has domain faces on both sides. `BuildDomain` gives each region bounded by barriers one verdict from a barrier's side, and the two sides of a barrier get opposite verdicts (it fails otherwise). `in_domain` therefore already excludes such a merge; the checks stay as a guard. The premise is tested by `Barriers_HaveTheDomainOnOneSideOnly` (after the domain, after each insertion with its merges, after `Simplify`; open toes with lateral limits included), which kills the mutant that makes both sides domain |
| only one of the two barrier checks | survives (equivalent, as above) |
| R3 reverted (`Approx` in `triangulate`) | not killable by an output test while `Realize` rounds every corner first (equivalent today; R3 is defensive) |

## 6. Open points for the owner

- No output changes, so there is nothing to approve on the outputs. The roadmap entry is corrected (#805, not vertex
  history).
- `CompactDuringInsert`, `PartSectors`, `AfterSimplify` and `ReinsertAfterSimplify` stay internal (coordinator 2026-11-24).
- (c), divide and conquer, is the next step if the remaining 20x is wanted.

## 7. Found: the fan sectors of ramp pieces are not conservative (pre-existing, follow-up)

The invariance test with a ramp (`TerrainSlopeRampTests.Straight`, `LocalFrame`) failed in the frame "rotated" (about z,
31°). The earlier construction (as on master) gives 218 surface vertices and the default 216. The labels differ by about
0.15 m at one boundary, not only at near-degenerate vertices.

The reference insertion (`NoPrefilters`: no prefilter and every fan plane in every face) gives 217 for both constructions.
So the prefilters are not the cause; with them alone off, the two constructions still differ. The cause is the fan-sector
choice (`add_sector`) in `InsertPolygonPiece`: on some faces it leaves out a fan plane that is the piece's maximum. Which
faces depend on their shapes, so the result depends on the construction, and master's result is not the exact envelope
there either.

All other frames, and all strip-piece cases, agree with the reference. The surface still passes the exact check. A missed
plane lowers F_k, so the face's label can be a plane of the ramp unit where another one is the true minimum. The outcome
stays certified against the specs, but it is not the defined envelope.

Follow-up (not in this PR):
- Find the face where the window misses the maximum (the polygon piece's fans at several apexes: the angular range is
  measured from each apex, and a face that spans an apex closely, or the ramp's wall fans, may break the argument of
  TERRAIN_1C_PLAN section 9).
- Fix `add_sector` for polygon pieces, or take every fan plane there.
- Add the ramp back to `Invariance_EveryFrame_SameAsTheEarlierConstruction` and `Default_GivesTheReferenceLabels`.

Until then R1 holds for strip pieces only.
